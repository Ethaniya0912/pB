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
        private int kernelClearHermite  = -1;   // [FIX-D]
        private int kernelCollectHermite = -1;
        private int kernelSolveQEF      = -1;
        private int kernelGenerateQuads = -1;

        private CaveComputeDispatcher baseDispatcher;
        private int   currentPointsPerAxis = 0;
        private bool  _isDestroyed = false;     // [FIX-F]

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
            kernelClearHermite   = dcComputeShader.FindKernel("ClearHermiteBuffer"); // [FIX-D]
            kernelCollectHermite = dcComputeShader.FindKernel("CollectHermiteData");
            kernelSolveQEF       = dcComputeShader.FindKernel("SolveQEF");
            kernelGenerateQuads  = dcComputeShader.FindKernel("GenerateQuads");

            Debug.Log($"[DCPipeline] 커널 초기화: Clear={kernelClearHermite}, Hermite={kernelCollectHermite}, QEF={kernelSolveQEF}, Quad={kernelGenerateQuads}");
        }

        // ==================================================================
        // 버퍼 할당/해제
        // ==================================================================
        public void AllocateBuffers(int pointsPerAxis)
        {
            if (currentPointsPerAxis == pointsPerAxis) return;
            ReleaseBuffers();

            currentPointsPerAxis = pointsPerAxis;
            int N3       = pointsPerAxis * pointsPerAxis * pointsPerAxis;
            int maxEdges = N3 * 3;

            int hermiteStride = Marshal.SizeOf(typeof(DCHermiteEdge)); // 32
            int vertexStride  = Marshal.SizeOf(typeof(DCVertex));      // 56
            int quadStride    = Marshal.SizeOf(typeof(DCQuad));        // 16
            int sculptStride  = Marshal.SizeOf(typeof(GameplaySculptData)); // 16

            hermiteEdgeBuffer  = new ComputeBuffer(maxEdges, hermiteStride);
            dcVertexBuffer     = new ComputeBuffer(N3,       vertexStride);
            dcQuadBuffer       = new ComputeBuffer(N3,       quadStride);    // N3: FIX-7과 동기화
            quadCountBuffer    = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

            var graphBuilder = CaveNodeGraphBuilder.Instance;
            int sculptCount  = Mathf.Max(1,
                graphBuilder != null ? graphBuilder.nodesData.Count + graphBuilder.edgesData.Count : 64);
            gameplaySculptBuffer = new ComputeBuffer(sculptCount, sculptStride);

            Debug.Log($"[DCPipeline] 버퍼 할당: N={pointsPerAxis}, Hermite={maxEdges}, Vertex={N3}");
        }

        private void ReleaseBuffers()
        {
            hermiteEdgeBuffer?.Release();  hermiteEdgeBuffer  = null;
            dcVertexBuffer?.Release();     dcVertexBuffer     = null;
            dcQuadBuffer?.Release();       dcQuadBuffer       = null;
            quadCountBuffer?.Release();    quadCountBuffer    = null;
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

        // [FIX-E] DCVertex 버퍼 클리어 (featureType=-1 마커 보장)
        private void ClearDCVertexBuffer(int pointsPerAxis)
        {
            // GPU에서 모든 원소를 -1로 초기화하는 가장 간단한 방법:
            // DCVertex.featureType 오프셋(24바이트)을 포함한 56바이트 구조체를
            // 전체 0으로 채우면 featureType=0(유효)로 보임 → 문제.
            // 대신 CPU에서 -1 마킹한 배열로 SetData.
            int N3 = pointsPerAxis * pointsPerAxis * pointsPerAxis;
            var cleared = new DCVertex[N3];
            for (int i = 0; i < N3; i++)
                cleared[i].featureType = -1;
            dcVertexBuffer.SetData(cleared);
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
            ClearHermiteEdgeBuffer(pointsPerAxis);

            // [FIX-E] DCVertex 버퍼 클리어 (featureType=-1 초기화)
            ClearDCVertexBuffer(pointsPerAxis);

            int threadGroups = Mathf.CeilToInt(pointsPerAxis / 8.0f);

            // --- Stage 2: Hermite Data 수집 ---
            dcComputeShader.SetBuffer(kernelCollectHermite, "_VoxelBuffer",       voxelBuffer);
            dcComputeShader.SetBuffer(kernelCollectHermite, "_HermiteEdgeBuffer", hermiteEdgeBuffer);
            dcComputeShader.SetInt  ("_PointsPerAxis", pointsPerAxis);
            dcComputeShader.SetFloat("_ChunkSize",     chunkWorldSize); // [FIX-A, FIX-B]
            // [FIX-B] _ChunkOffset 전달 제거 — 셰이더는 순수 로컬 좌표만 사용
            dcComputeShader.Dispatch(kernelCollectHermite, threadGroups, threadGroups, threadGroups);

            // --- Stage 3: QEF 풀기 ---
            // [FIX-C] _VoxelBuffer를 SolveQEF에도 바인딩 (oreType, 그래디언트 읽기)
            dcComputeShader.SetBuffer(kernelSolveQEF, "_VoxelBuffer",       voxelBuffer);
            dcComputeShader.SetBuffer(kernelSolveQEF, "_HermiteEdgeBuffer", hermiteEdgeBuffer);
            dcComputeShader.SetBuffer(kernelSolveQEF, "_DCVertexBuffer",    dcVertexBuffer);
            dcComputeShader.SetBuffer(kernelSolveQEF, "_GameplaySculptBuffer", gameplaySculptBuffer);
            // _PointsPerAxis, _ChunkSize는 이미 위에서 설정됨 (uniform 유지)
            dcComputeShader.Dispatch(kernelSolveQEF, threadGroups, threadGroups, threadGroups);

            // --- Stage 4: 쿼드 생성 ---
            quadCountBuffer.SetData(new int[] { 0 });
            // [FIX-C] _VoxelBuffer를 GenerateQuads에도 바인딩 (밀도 부호 읽기)
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_VoxelBuffer",  voxelBuffer);
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_DCVertexBuffer", dcVertexBuffer);
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_DCQuadBuffer", dcQuadBuffer);
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_QuadCount",    quadCountBuffer);
            dcComputeShader.Dispatch(kernelGenerateQuads, threadGroups, threadGroups, threadGroups);
        }

        /// <summary>비동기 GPU 읽기</summary>
        public void ReadbackAsync(System.Action<DCVertex[], DCQuad[], int> onComplete)
        {
            if (quadCountBuffer == null || dcVertexBuffer == null || dcQuadBuffer == null)
            {
                Debug.LogWarning("[DCPipeline] 버퍼 미할당. DispatchDC() 먼저 호출 필요.");
                onComplete?.Invoke(null, null, 0);
                return;
            }

            AsyncGPUReadback.Request(quadCountBuffer, (countReq) =>
            {
                // [FIX-F] Play 종료 후 콜백 진입 차단
                if (_isDestroyed || quadCountBuffer == null) return;
                if (countReq.hasError)
                {
                    Debug.LogError("[DCPipeline] QuadCount readback 실패");
                    onComplete?.Invoke(null, null, 0);
                    return;
                }

                int quadCount = countReq.GetData<int>()[0];
                if (quadCount == 0)
                {
                    onComplete?.Invoke(null, null, 0);
                    return;
                }

                AsyncGPUReadback.Request(dcVertexBuffer, (vertReq) =>
                {
                    if (_isDestroyed || dcVertexBuffer == null) return;
                    if (vertReq.hasError)
                    {
                        onComplete?.Invoke(null, null, 0);
                        return;
                    }
                    var verts = vertReq.GetData<DCVertex>().ToArray();

                    AsyncGPUReadback.Request(dcQuadBuffer, (quadReq) =>
                    {
                        if (_isDestroyed || dcQuadBuffer == null) return;
                        if (quadReq.hasError)
                        {
                            onComplete?.Invoke(null, null, 0);
                            return;
                        }
                        var quads = quadReq.GetData<DCQuad>().ToArray();
                        onComplete?.Invoke(verts, quads, quadCount);
                    });
                });
            });
        }

        private void OnSDFProfileChanged()
        {
            Debug.Log("[DCPipeline] SDF Profile 변경 감지 → GPU 버퍼 갱신 예정");
        }

        public bool IsInitialized => kernelCollectHermite >= 0;
        public ComputeBuffer DCVertexBuffer      => dcVertexBuffer;
        public ComputeBuffer DCQuadBuffer        => dcQuadBuffer;
        public ComputeBuffer GameplaySculptBuffer => gameplaySculptBuffer;
    }
}
