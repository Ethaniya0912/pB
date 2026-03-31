// =============================================================================
// DCPipelineExtension.cs  |  pB-4 Project — Week 0
// Layer  : Pipeline (지형)
// Owner  : Person B
//
// 역할:
//   기존 CaveComputeDispatcher를 직접 수정하지 않고, DC 전용 버퍼와
//   디스패치 로직을 래퍼 클래스로 확장.
//   기존 MC 파이프라인과 공존하며, useDualContouring 플래그로 분기.
//
// 기존 파일과의 관계:
//   - CaveComputeDispatcher.cs: 기존 MC 파이프라인 유지 (수정 최소화)
//   - CaveManager.cs: HandleGpuResult에 DC 분기 추가 필요 (별도 PR)
//   - CaveMeshJobManager.cs: WeldAndSmoothJob 제거, DCNormalFinalizeJob 추가
//
// Stage 진행:
//   Stage 1: 이 파일 + DCDataStructs.cs 생성, 빈 커널 스켈레톤
//   Stage 2: CollectHermiteData 커널 구현
//   Stage 3: SolveQEF 커널 구현 (핵심 난관)
//   Stage 4: GenerateQuads 커널 구현 + 메쉬 출력
//   Stage 5: CaveNormalBaker 연동
//   Stage 6: 성능 최적화 + 검증
// =============================================================================
using UnityEngine;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

namespace CaveSystem
{
    /// <summary>
    /// DC 전환을 위한 CaveComputeDispatcher 확장.
    /// 기존 디스패처에 컴포넌트로 부착하여 DC 버퍼와 커널을 관리.
    /// </summary>
    [RequireComponent(typeof(CaveComputeDispatcher))]
    public class DCPipelineExtension : MonoBehaviour
    {
        [Header("DC Pipeline Control")]
        [Tooltip("true=Dual Contouring, false=Marching Cubes (레거시)")]
        public bool useDualContouring = false;

        [Header("DC Compute Shader")]
        [Tooltip("CaveDualContouring.compute — 3개 커널(Hermite/QEF/Quad)")]
        public ComputeShader dcComputeShader;

        [Header("SDF Enhancement")]
        [Tooltip("BiomeSDF_ProfileSO 목록. 기존 CaveBiomeSettings.globalBiomes와 1:1 매핑.")]
        public BiomeSDF_ProfileSO[] sdfProfiles;

        // --- DC 전용 GPU 버퍼 ---
        private ComputeBuffer hermiteEdgeBuffer;
        private ComputeBuffer dcVertexBuffer;
        private ComputeBuffer dcQuadBuffer;
        private ComputeBuffer quadCountBuffer;
        private ComputeBuffer gameplaySculptBuffer;

        // --- 커널 캐싱 ---
        private int kernelCollectHermite = -1;
        private int kernelSolveQEF = -1;
        private int kernelGenerateQuads = -1;

        // 기존 디스패처 참조
        private CaveComputeDispatcher baseDispatcher;
        private int currentPointsPerAxis = 0;

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
            ReleaseBuffers();
        }

        // ==================================================================
        // Stage 1: 커널 초기화
        // ==================================================================
        private void InitializeDCKernels()
        {
            kernelCollectHermite = dcComputeShader.FindKernel("CollectHermiteData");
            kernelSolveQEF = dcComputeShader.FindKernel("SolveQEF");
            kernelGenerateQuads = dcComputeShader.FindKernel("GenerateQuads");

            Debug.Log($"[DCPipeline] 커널 초기화 완료: Hermite={kernelCollectHermite}, QEF={kernelSolveQEF}, Quad={kernelGenerateQuads}");
        }

        // ==================================================================
        // 버퍼 할당/해제
        // ==================================================================
        public void AllocateBuffers(int pointsPerAxis)
        {
            if (currentPointsPerAxis == pointsPerAxis) return;
            ReleaseBuffers();

            currentPointsPerAxis = pointsPerAxis;
            int voxelCount = pointsPerAxis * pointsPerAxis * pointsPerAxis;
            int maxEdges = voxelCount * 3;  // 3축 에지
            int maxVertices = voxelCount;    // 최대 복셀당 1 버텍스
            int maxQuads = maxEdges;         // 최대 에지당 1 쿼드

            int hermiteStride = Marshal.SizeOf(typeof(DCHermiteEdge));   // 32
            int vertexStride = Marshal.SizeOf(typeof(DCVertex));         // 56
            int quadStride = Marshal.SizeOf(typeof(DCQuad));             // 16
            int sculptStride = Marshal.SizeOf(typeof(GameplaySculptData)); // 16

            hermiteEdgeBuffer = new ComputeBuffer(maxEdges, hermiteStride);
            dcVertexBuffer = new ComputeBuffer(maxVertices, vertexStride);
            dcQuadBuffer = new ComputeBuffer(maxQuads, quadStride);
            quadCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

            // GameplaySculpt 버퍼: 노드/엣지 수만큼 할당 (Phase 1에서 채움)
            var graphBuilder = CaveNodeGraphBuilder.Instance;
            int sculptCount = Mathf.Max(1, 
                (graphBuilder != null ? graphBuilder.nodesData.Count + graphBuilder.edgesData.Count : 64));
            gameplaySculptBuffer = new ComputeBuffer(sculptCount, sculptStride);

            Debug.Log($"[DCPipeline] 버퍼 할당 완료: {pointsPerAxis}³, Hermite={maxEdges}, Vertex={maxVertices}, Quad={maxQuads}, Sculpt={sculptCount}");
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

        // ==================================================================
        // Stage 2~4: DC 디스패치 (기존 MC Dispatch와 동일 위치에서 호출)
        // ==================================================================

        /// <summary>
        /// 기존 CaveComputeDispatcher.DispatchChunk() 내부에서 MC 커널 대신 호출.
        /// 밀도장(voxelBuffer)은 기존 커널 1이 이미 생성한 상태.
        /// 이 메서드가 MC의 '커널 2: marchingCubesShader.Dispatch' 를 대체한다.
        /// 
        /// [CaveComputeDispatcher.DispatchChunk() 내부에 삽입할 코드]
        /// ───────────────────────────────────────────────────────────
        /// var dcExt = GetComponent&lt;DCPipelineExtension&gt;();
        /// if (dcExt != null &amp;&amp; dcExt.useDualContouring &amp;&amp; dcExt.IsInitialized)
        /// {
        ///     dcExt.DispatchDC(context.ChunkPos, pointsPerAxis, chunkSize, voxelBuffer);
        ///     dcExt.ReadbackAsync((verts, quads, count) =&gt; {
        ///         Debug.Log($"[DC] Readback: {count} quads");
        ///         IsBusy = false;
        ///     });
        ///     return; // MC 커널 2 스킵
        /// }
        /// // ─── 기존 MC 커널 2 코드가 여기 아래에 그대로 유지됨 ───
        /// ───────────────────────────────────────────────────────────
        /// </summary>
        public void DispatchDC(Vector3Int chunkPos, int pointsPerAxis, float chunkSize,
            ComputeBuffer voxelBuffer /* 기존 밀도장 */)
        {
            if (dcComputeShader == null || kernelCollectHermite < 0)
            {
                Debug.LogError("[DCPipeline] Compute Shader 또는 커널이 초기화되지 않았습니다.");
                return;
            }

            AllocateBuffers(pointsPerAxis);

            // --- Stage 2: Hermite Data 수집 ---
            dcComputeShader.SetBuffer(kernelCollectHermite, "_VoxelBuffer", voxelBuffer);
            dcComputeShader.SetBuffer(kernelCollectHermite, "_HermiteEdgeBuffer", hermiteEdgeBuffer);
            dcComputeShader.SetInt("_PointsPerAxis", pointsPerAxis);
            dcComputeShader.SetFloat("_ChunkSize", chunkSize);
            dcComputeShader.SetVector("_ChunkOffset", new Vector4(chunkPos.x, chunkPos.y, chunkPos.z, 0));

            int threadGroups = Mathf.CeilToInt(pointsPerAxis / 8f);
            dcComputeShader.Dispatch(kernelCollectHermite, threadGroups, threadGroups, threadGroups);

            // --- Stage 3: QEF 풀기 ---
            dcComputeShader.SetBuffer(kernelSolveQEF, "_HermiteEdgeBuffer", hermiteEdgeBuffer);
            dcComputeShader.SetBuffer(kernelSolveQEF, "_DCVertexBuffer", dcVertexBuffer);
            dcComputeShader.SetBuffer(kernelSolveQEF, "_GameplaySculptBuffer", gameplaySculptBuffer);
            dcComputeShader.Dispatch(kernelSolveQEF, threadGroups, threadGroups, threadGroups);

            // --- Stage 4: 쿼드 생성 ---
            quadCountBuffer.SetData(new int[] { 0 });
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_DCVertexBuffer", dcVertexBuffer);
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_DCQuadBuffer", dcQuadBuffer);
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_QuadCount", quadCountBuffer);
            dcComputeShader.Dispatch(kernelGenerateQuads, threadGroups, threadGroups, threadGroups);
        }

        /// <summary>
        /// 비동기 GPU 읽기. CaveManager.HandleGpuResult DC 분기에서 호출.
        /// DispatchDC()가 먼저 호출된 후에만 사용 가능.
        /// </summary>
        public void ReadbackAsync(System.Action<DCVertex[], DCQuad[], int> onComplete)
        {
            // Null Guard: DispatchDC()가 호출되지 않은 상태에서 Readback 시도 방지
            if (quadCountBuffer == null || dcVertexBuffer == null || dcQuadBuffer == null)
            {
                Debug.LogWarning("[DCPipeline] 버퍼가 할당되지 않았습니다. DispatchDC()를 먼저 호출하세요.");
                onComplete?.Invoke(null, null, 0);
                return;
            }

            AsyncGPUReadback.Request(quadCountBuffer, (countReq) =>
            {
                if (countReq.hasError) { Debug.LogError("[DCPipeline] QuadCount readback failed"); return; }
                int quadCount = countReq.GetData<int>()[0];

                if (quadCount == 0) { onComplete?.Invoke(null, null, 0); return; }

                // 버텍스 읽기
                AsyncGPUReadback.Request(dcVertexBuffer, (vertReq) =>
                {
                    if (vertReq.hasError) return;
                    var verts = vertReq.GetData<DCVertex>().ToArray();

                    // 쿼드 읽기
                    AsyncGPUReadback.Request(dcQuadBuffer, (quadReq) =>
                    {
                        if (quadReq.hasError) return;
                        var quads = quadReq.GetData<DCQuad>().ToArray();
                        onComplete?.Invoke(verts, quads, quadCount);
                    });
                });
            });
        }

        // ==================================================================
        // SDF 프로파일 변경 핫리로드
        // ==================================================================
        private void OnSDFProfileChanged()
        {
            // TODO: sdfProfiles → 기존 UpdateBiomeBuffer() 패턴으로 GPU 갱신
            Debug.Log("[DCPipeline] SDF Profile 변경 감지 → GPU 버퍼 갱신 예정");
        }

        // ==================================================================
        // 공개 프로퍼티 (CaveManager에서 접근)
        // ==================================================================
        public bool IsInitialized => kernelCollectHermite >= 0;
        public ComputeBuffer DCVertexBuffer => dcVertexBuffer;
        public ComputeBuffer DCQuadBuffer => dcQuadBuffer;
        public ComputeBuffer GameplaySculptBuffer => gameplaySculptBuffer;
    }
}
