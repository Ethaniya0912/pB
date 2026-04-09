// =============================================================================
// DCPipelineExtension.cs  |  pB-4 Project — Week 0
// =============================================================================
// [버그 수정 목록]
// FIX-A: _ChunkSize = (pointsPerAxis-1)*voxelSize  (기존: chunkSize 정수값 그대로 → 간격 오류)
// FIX-B: _ChunkOffset 제거  (기존: 청크 정수 좌표 → 잘못된 월드 좌표)
// FIX-C: voxelBuffer를 SolveQEF, GenerateQuads에도 바인딩  (기존: CollectHermiteData만 바인딩)
// FIX-D: ClearHermiteBuffer 커널 추가 및 디스패치 전 호출  (청크 간 잔존 데이터 오염 방지)
// FIX-E: DCVertexBuffer 초기화 (ClearDCVertexBuffer 추가)
// FIX-F: _isDestroyed 플래그로 Play모드 종료 시 ReadbackAsync 크래시 방지
// FIX-G: DispatchDC 시그니처에 voxelSize 추가 (FIX-A 계산에 필요)
// =============================================================================

using UnityEngine;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

namespace CaveSystem
{
    [RequireComponent(typeof(CaveComputeDispatcher))]
    public class DCPipelineExtension : MonoBehaviour
    {
        [Header("DC Pipeline Control")]
        [Tooltip("true=Dual Contouring, false=Marching Cubes")]
        public bool useDualContouring = false;

        [Header("DC Compute Shader")]
        public ComputeShader dcComputeShader;

        [Header("SDF Enhancement")]
        public BiomeSDF_ProfileSO[] sdfProfiles;

        // --- DC 전용 GPU 버퍼 ---
        private ComputeBuffer hermiteEdgeBuffer;
        private ComputeBuffer dcVertexBuffer;
        private ComputeBuffer dcQuadBuffer;
        private ComputeBuffer quadCountBuffer;
        private ComputeBuffer gameplaySculptBuffer;

        // --- 커널 캐싱 ---
        private int kernelClearHermite = -1;   // [FIX-D]
        private int kernelClearVertex = -1;   // [FIX-VERT] GPU 버텍스 클리어
        private int kernelCollectHermite = -1;
        private int kernelSolveQEF = -1;
        private int kernelGenerateQuads = -1;

        private CaveComputeDispatcher baseDispatcher;
        private int currentPointsPerAxis = 0;
        private bool _isDestroyed = false;     // [FIX-F]

        private void Awake()
        {
            baseDispatcher = GetComponent<CaveComputeDispatcher>();
            if (dcComputeShader != null)
                InitializeDCKernels();
        }

        private void OnEnable()
        {
            BiomeSDF_ProfileSO.OnProfileModified += OnSDFProfileChanged;
        }

        private void OnDisable()
        {
            BiomeSDF_ProfileSO.OnProfileModified -= OnSDFProfileChanged;
        }

        private void OnDestroy()
        {
            _isDestroyed = true;    // [FIX-F] ReadbackAsync 콜백 진입 차단
            ReleaseBuffers();
        }

        // ==================================================================
        // 커널 초기화
        // ==================================================================
        private void InitializeDCKernels()
        {
            kernelClearHermite = dcComputeShader.FindKernel("ClearHermiteBuffer"); // [FIX-D]
            kernelClearVertex = dcComputeShader.FindKernel("ClearVertexBuffer");  // [FIX-VERT]
            kernelCollectHermite = dcComputeShader.FindKernel("CollectHermiteData");
            kernelSolveQEF = dcComputeShader.FindKernel("SolveQEF");
            kernelGenerateQuads = dcComputeShader.FindKernel("GenerateQuads");

            Debug.Log($"[DCPipeline] 커널 초기화: Clear={kernelClearHermite}, ClearVert={kernelClearVertex}, Hermite={kernelCollectHermite}, QEF={kernelSolveQEF}, Quad={kernelGenerateQuads}");
        }

        // ==================================================================
        // 버퍼 할당/해제
        // ==================================================================
        public void AllocateBuffers(int pointsPerAxis)
        {
            if (currentPointsPerAxis == pointsPerAxis) return;
            ReleaseBuffers();

            currentPointsPerAxis = pointsPerAxis;
            int N3 = pointsPerAxis * pointsPerAxis * pointsPerAxis;
            int maxEdges = N3 * 3;

            int hermiteStride = 16; // [O3] DCHermiteEdge 압축: 32B→16B (intersectionNormal Oct16 인코딩)
            int vertexStride = 32; // [O1] DCVertex 압축: 56B→32B (Marshal.SizeOf 대신 하드코딩)
                                   // position(12)+normal(12)+featureType(4)+materialIndex(4)
            int quadStride = Marshal.SizeOf(typeof(DCQuad));        // 16
            int sculptStride = Marshal.SizeOf(typeof(GameplaySculptData)); // 16

            hermiteEdgeBuffer = new ComputeBuffer(maxEdges, hermiteStride);
            dcVertexBuffer = new ComputeBuffer(N3, vertexStride);
            dcQuadBuffer = new ComputeBuffer(N3, quadStride);    // N3: FIX-7과 동기화
            quadCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

            var graphBuilder = CaveNodeGraphBuilder.Instance;
            int sculptCount = Mathf.Max(1,
                graphBuilder != null ? graphBuilder.nodesData.Count + graphBuilder.edgesData.Count : 64);
            gameplaySculptBuffer = new ComputeBuffer(sculptCount, sculptStride);

            Debug.Log($"[DCPipeline] 버퍼 할당: N={pointsPerAxis}, Hermite={maxEdges}, Vertex={N3}");
        }

        private void ReleaseBuffers()
        {
            hermiteEdgeBuffer?.Release(); hermiteEdgeBuffer = null;
            dcVertexBuffer?.Release(); dcVertexBuffer = null;
            dcQuadBuffer?.Release(); dcQuadBuffer = null;
            quadCountBuffer?.Release(); quadCountBuffer = null;
            gameplaySculptBuffer?.Release(); gameplaySculptBuffer = null;
            currentPointsPerAxis = 0;
        }

        // [FIX-D] Hermite 버퍼 클리어
        private void ClearHermiteEdgeBuffer(int pointsPerAxis)
        {
            int hermiteCount = 3 * pointsPerAxis * pointsPerAxis * pointsPerAxis;
            dcComputeShader.SetInt("_HermiteBufferSize", hermiteCount);
            dcComputeShader.SetBuffer(kernelClearHermite, "_HermiteEdgeBuffer", hermiteEdgeBuffer);
            int groups = Mathf.CeilToInt(hermiteCount / 256.0f);
            dcComputeShader.Dispatch(kernelClearHermite, groups, 1, 1);
        }

        // [FIX-VERT] DCVertex 버퍼 GPU 클리어 — CPU SetData 대체
        // 기존 CPU 방식: new DCVertex[N3] 생성 후 SetData → 주 스레드 블로킹 0.3ms
        // 신규 GPU 방식: ClearVertexBuffer 커널 → 병렬 초기화 ~0.01ms
        private void ClearDCVertexBuffer(int pointsPerAxis)
        {
            if (kernelClearVertex < 0)
            {
                // 폴백: GPU 커널 미초기화 시 CPU 방식 유지
                int N3 = pointsPerAxis * pointsPerAxis * pointsPerAxis;
                var cleared = new DCVertex[N3];
                for (int i = 0; i < N3; i++) cleared[i].featureType = -1;
                dcVertexBuffer.SetData(cleared);
                return;
            }
            int total = pointsPerAxis * pointsPerAxis * pointsPerAxis;
            dcComputeShader.SetBuffer(kernelClearVertex, "_DCVertexBuffer", dcVertexBuffer);
            // _PointsPerAxis는 이미 ClearHermiteEdgeBuffer 이후 설정됨
            int groups = Mathf.CeilToInt(total / 256.0f);
            dcComputeShader.Dispatch(kernelClearVertex, groups, 1, 1);
        }

        // ==================================================================
        // DC 디스패치
        // ==================================================================

        /// <summary>
        /// CaveComputeDispatcher의 DC 분기에서 호출.
        /// 밀도장(voxelBuffer)은 이미 생성된 상태여야 함.
        /// </summary>
        /// <param name="chunkPos">청크 그리드 좌표</param>
        /// <param name="pointsPerAxis">복셀 그리드 크기 (chunkSize + 3 권장)</param>
        /// <param name="chunkSize">청크의 복셀 수 (64 등)</param>
        /// <param name="voxelSize">복셀 하나의 월드 크기 (0.5 등)</param>
        /// <param name="voxelBuffer">밀도장 버퍼</param>
        public void DispatchDC(Vector3Int chunkPos, int pointsPerAxis, int chunkSize,
                               float voxelSize, ComputeBuffer voxelBuffer) // [FIX-G]
        {
            if (dcComputeShader == null || kernelCollectHermite < 0)
            {
                Debug.LogError("[DCPipeline] Compute Shader 또는 커널 미초기화.");
                return;
            }

            AllocateBuffers(pointsPerAxis);

            // [FIX-A] _ChunkSize = (N-1) * voxelSize → spacing = voxelSize (정확 일치)
            //   기존: _ChunkSize = chunkSize(=64) → spacing = 64/(N-1) ≈ 0.985 ≠ voxelSize
            float chunkWorldSize = (float)(pointsPerAxis - 1) * voxelSize;

            // [FIX-D] Hermite 버퍼 클리어 (이전 청크 잔존 데이터 제거)
            // _PointsPerAxis를 먼저 설정 → ClearHermite / ClearVertex 공용
            dcComputeShader.SetInt("_PointsPerAxis", pointsPerAxis);
            ClearHermiteEdgeBuffer(pointsPerAxis);

            // [FIX-VERT] DCVertex 버퍼 GPU 클리어 (featureType=-1 초기화)
            ClearDCVertexBuffer(pointsPerAxis);

            int threadGroups = Mathf.CeilToInt(pointsPerAxis / 8.0f);

            // --- Stage 2: Hermite Data 수집 ---
            dcComputeShader.SetBuffer(kernelCollectHermite, "_VoxelBuffer", voxelBuffer);
            dcComputeShader.SetBuffer(kernelCollectHermite, "_HermiteEdgeBuffer", hermiteEdgeBuffer);
            // _PointsPerAxis는 클리어 단계에서 이미 설정됨
            dcComputeShader.SetFloat("_ChunkSize", chunkWorldSize); // [FIX-A, FIX-B]
            // [FIX-B] _ChunkOffset 전달 제거 — 셰이더는 순수 로컬 좌표만 사용
            dcComputeShader.Dispatch(kernelCollectHermite, threadGroups, threadGroups, threadGroups);

            // --- Stage 3: QEF 풀기 ---
            // [FIX-C] _VoxelBuffer를 SolveQEF에도 바인딩 (oreType, 그래디언트 읽기)
            dcComputeShader.SetBuffer(kernelSolveQEF, "_VoxelBuffer", voxelBuffer);
            dcComputeShader.SetBuffer(kernelSolveQEF, "_HermiteEdgeBuffer", hermiteEdgeBuffer);
            dcComputeShader.SetBuffer(kernelSolveQEF, "_DCVertexBuffer", dcVertexBuffer);
            dcComputeShader.SetBuffer(kernelSolveQEF, "_GameplaySculptBuffer", gameplaySculptBuffer);
            // _PointsPerAxis, _ChunkSize는 이미 위에서 설정됨 (uniform 유지)
            dcComputeShader.Dispatch(kernelSolveQEF, threadGroups, threadGroups, threadGroups);

            // --- Stage 4: 쿼드 생성 ---
            quadCountBuffer.SetData(new int[] { 0 });
            // [FIX-C] _VoxelBuffer를 GenerateQuads에도 바인딩 (밀도 부호 읽기)
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_VoxelBuffer", voxelBuffer);
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_DCVertexBuffer", dcVertexBuffer);
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_DCQuadBuffer", dcQuadBuffer);
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_QuadCount", quadCountBuffer);
            dcComputeShader.Dispatch(kernelGenerateQuads, threadGroups, threadGroups, threadGroups);
        }

        /// <summary>비동기 GPU 읽기 — Partial Readback
        /// [PARTIAL-READBACK]
        ///   구 방식: dcVertexBuffer 전량(N³×32B=41MB) + dcQuadBuffer 전량(N³×16B=20MB) = 61MB
        ///   신 방식: Step1 quad 부분 읽기(quadCount×16B≈0.7MB)
        ///            → Step2 유효 vertex 인덱스 범위 계산
        ///            → Step3 vertex 부분 읽기([minIdx,maxIdx]×32B)
        ///            → quad 인덱스를 -minIdx 오프셋 적용 후 콜백
        ///   기대 절감: quad 97% + vertex 30~70% 절감
        /// </summary>
        public void ReadbackAsync(System.Action<DCVertex[], DCQuad[], int> onComplete)
        {
            if (quadCountBuffer == null || dcVertexBuffer == null || dcQuadBuffer == null)
            {
                Debug.LogWarning("[DCPipeline] 버퍼 미할당. DispatchDC() 먼저 호출 필요.");
                onComplete?.Invoke(null, null, 0);
                return;
            }

            // ── Step 1: quadCount 읽기 ────────────────────────────────────────
            AsyncGPUReadback.Request(quadCountBuffer, (countReq) =>
            {
                if (_isDestroyed || quadCountBuffer == null) return;  // [FIX-F]
                if (countReq.hasError)
                {
                    Debug.LogError("[DCPipeline] QuadCount readback 실패");
                    onComplete?.Invoke(null, null, 0);
                    return;
                }

                int quadCount = countReq.GetData<int>()[0];
                if (quadCount <= 0)
                {
                    onComplete?.Invoke(null, null, 0);
                    return;
                }

                // ── Step 2: quad 부분 읽기 (quadCount × 16B만) ───────────────
                // [PARTIAL-QUAD] 기존 N³×16B=20MB → quadCount×16B≈0.7MB
                const int kQuadStride = 16; // sizeof(DCQuad) = 4×int
                int quadReadBytes = quadCount * kQuadStride;
                // 버퍼 상한 초과 방지 (quadCount가 잘못 클 때 안전망)
                quadReadBytes = Mathf.Min(quadReadBytes, dcQuadBuffer.count * kQuadStride);

                AsyncGPUReadback.Request(dcQuadBuffer, quadReadBytes, 0, (quadReq) =>
                {
                    if (_isDestroyed || dcQuadBuffer == null) return;
                    if (quadReq.hasError)
                    {
                        Debug.LogError("[DCPipeline] Quad partial readback 실패");
                        onComplete?.Invoke(null, null, 0);
                        return;
                    }

                    // [FIX-DISPOSED] NativeArray는 이 콜백 스코프 안에서만 유효.
                    // 내부 vertex 콜백(b__2)이 quadsRaw를 클로저로 캡처하면
                    // quad 콜백 리턴 시 Unity가 NativeArray를 해제 → ObjectDisposedException.
                    // 해결: 즉시 ToArray()로 관리형 배열에 복사 → 수명 보장.
                    var quadsRaw = quadReq.GetData<DCQuad>().ToArray();

                    // ── Step 3: 유효 vertex 인덱스 범위 계산 ─────────────────
                    int N3 = currentPointsPerAxis * currentPointsPerAxis * currentPointsPerAxis;
                    int minIdx = int.MaxValue;
                    int maxIdx = int.MinValue;

                    for (int i = 0; i < quadsRaw.Length; i++)
                    {
                        DCQuad q = quadsRaw[i];
                        // 범위 밖 인덱스는 GPU 버그 → 무시
                        if (q.v0 >= 0 && q.v0 < N3) { if (q.v0 < minIdx) minIdx = q.v0; if (q.v0 > maxIdx) maxIdx = q.v0; }
                        if (q.v1 >= 0 && q.v1 < N3) { if (q.v1 < minIdx) minIdx = q.v1; if (q.v1 > maxIdx) maxIdx = q.v1; }
                        if (q.v2 >= 0 && q.v2 < N3) { if (q.v2 < minIdx) minIdx = q.v2; if (q.v2 > maxIdx) maxIdx = q.v2; }
                        if (q.v3 >= 0 && q.v3 < N3) { if (q.v3 < minIdx) minIdx = q.v3; if (q.v3 > maxIdx) maxIdx = q.v3; }
                    }

                    if (minIdx > maxIdx)
                    {
                        // 유효 인덱스 없음 (전부 범위 밖)
                        onComplete?.Invoke(null, null, 0);
                        return;
                    }

                    // ── Step 4: vertex 부분 읽기 [minIdx, maxIdx] ─────────────
                    // [PARTIAL-VERT] 기존 N³×32B=41MB → (maxIdx-minIdx+1)×32B
                    const int kVertStride = 32; // sizeof(DCVertex)
                    int vertRangeCount = maxIdx - minIdx + 1;
                    int vertReadBytes = vertRangeCount * kVertStride;
                    int vertReadOffset = minIdx * kVertStride;

                    float savedQuadMB = (dcQuadBuffer.count - quadCount) * kQuadStride / 1048576f;
                    float savedVertMB = (N3 - vertRangeCount) * kVertStride / 1048576f;
                    Debug.Log($"[DCPipeline][Partial] quad={quadCount}개({quadReadBytes / 1048576f:F1}MB↓{savedQuadMB:F0}MB절감) " +
                              $"vert=[{minIdx}~{maxIdx}]({vertReadBytes / 1048576f:F1}MB↓{savedVertMB:F0}MB절감)");

                    AsyncGPUReadback.Request(dcVertexBuffer, vertReadBytes, vertReadOffset, (vertReq) =>
                    {
                        if (_isDestroyed || dcVertexBuffer == null) return;
                        if (vertReq.hasError)
                        {
                            Debug.LogError("[DCPipeline] Vertex partial readback 실패");
                            onComplete?.Invoke(null, null, 0);
                            return;
                        }

                        // vertsPartial[i] = vertex at FlatIndex (minIdx + i)
                        var vertsPartial = vertReq.GetData<DCVertex>().ToArray();

                        // ── Step 5: quad 인덱스 -minIdx 오프셋 적용 ──────────
                        // [PARTIAL-OFFSET] DCMeshBuilder는 workVerts[q.vN]으로 접근
                        //   → 기존 FlatIndex 기준 → 부분 배열 기준으로 재조정
                        var quadsAdj = new DCQuad[quadsRaw.Length];
                        for (int i = 0; i < quadsRaw.Length; i++)
                        {
                            quadsAdj[i] = new DCQuad
                            {
                                v0 = quadsRaw[i].v0 - minIdx,
                                v1 = quadsRaw[i].v1 - minIdx,
                                v2 = quadsRaw[i].v2 - minIdx,
                                v3 = quadsRaw[i].v3 - minIdx,
                            };
                        }

                        onComplete?.Invoke(vertsPartial, quadsAdj, quadCount);
                    });
                });
            });
        }

        private void OnSDFProfileChanged()
        {
            Debug.Log("[DCPipeline] SDF Profile 변경 감지 → GPU 버퍼 갱신 예정");
        }

        public bool IsInitialized => kernelCollectHermite >= 0;
        public ComputeBuffer DCVertexBuffer => dcVertexBuffer;
        public ComputeBuffer DCQuadBuffer => dcQuadBuffer;
        public ComputeBuffer GameplaySculptBuffer => gameplaySculptBuffer;
    }
}