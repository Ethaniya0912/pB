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
    // ============ [O1] 압축 DCVertex C# 구조체 (32B) ============
    // GPU에서 COMPRESSED_VERTEX 활성 시 이 포맷으로 readback
    // DecompressVertices()로 DCVertex[]로 변환 후 DCMeshBuilder에 전달
    [StructLayout(LayoutKind.Sequential)]
    public struct DCVertexCompressed
    {
        public Vector3 position;    // 12B
        public uint octNormal;      // 4B
        public uint uvPacked;       // 4B
        public int featureType;     // 4B
        public uint errMat;         // 4B (lower16=half qefError, upper16=materialIndex)
        public uint padding;        // 4B
        // Total: 32B
    }

    // ============ [O1] C# 디코딩 헬퍼 ============
    public static class DCVertexDecompressor
    {
        // OctWrap — HLSL과 동일 (축별 분리, 벡터 ternary 버그 방지)
        static Vector2 OctWrap(Vector2 v)
        {
            return new Vector2(
                (1f - Mathf.Abs(v.y)) * (v.x >= 0f ? 1f : -1f),
                (1f - Mathf.Abs(v.x)) * (v.y >= 0f ? 1f : -1f)
            );
        }

        public static Vector3 OctDecode(uint packed)
        {
            float x = (float)(packed & 0xFFFF) / 65535f * 2f - 1f;
            float y = (float)((packed >> 16) & 0xFFFF) / 65535f * 2f - 1f;
            Vector3 n = new Vector3(x, y, 1f - Mathf.Abs(x) - Mathf.Abs(y));
            if (n.z < 0f)
            {
                Vector2 w = OctWrap(new Vector2(n.x, n.y));
                n.x = w.x; n.y = w.y;
            }
            return n.normalized;
        }

        public static Vector2 UnpackHalf2(uint packed)
        {
            return new Vector2(
                Mathf.HalfToFloat((ushort)(packed & 0xFFFF)),
                Mathf.HalfToFloat((ushort)((packed >> 16) & 0xFFFF))
            );
        }

        /// <summary>
        /// DCVertexCompressed[] → DCVertex[] 변환.
        /// DCMeshBuilder는 DCVertex[]만 수신 → 인터페이스 변경 없음.
        /// </summary>
        public static DCVertex[] Decompress(DCVertexCompressed[] compressed)
        {
            var result = new DCVertex[compressed.Length];
            for (int i = 0; i < compressed.Length; i++)
            {
                ref var c = ref compressed[i];
                result[i] = new DCVertex
                {
                    position      = c.position,
                    normal        = OctDecode(c.octNormal),
                    uv            = UnpackHalf2(c.uvPacked),
                    featureType   = c.featureType,
                    materialIndex = (int)((c.errMat >> 16) & 0xFFFF),
                    qefError      = Mathf.HalfToFloat((ushort)(c.errMat & 0xFFFF)),
                    padding       = Vector3.zero
                };
            }
            return result;
        }
    }

    [RequireComponent(typeof(CaveComputeDispatcher))]
    public class DCPipelineExtension : MonoBehaviour
    {
        [Header("DC Pipeline Control")]
        [Tooltip("true=Dual Contouring, false=Marching Cubes")]
        public bool useDualContouring = false;

        [Header("Phase 2 — DC 메시 품질")]
        [Tooltip("ON: Hermite gradient ±3 확장 → smax 전이대 법선 안정화")]
        public bool enableVisualSmooth = false;
        [Tooltip("ON: eigenvalue confidence blend (불안정→massPoint, 안정→QEF)")]
        public bool enableMassPointA = false;
        [Tooltip("ON: Tikhonov lambda 0.01→0.1 (vertex를 voxel 중심으로 수렴)")]
        public bool enableMassPointB = false;

        // ─────────────────────────────────────────────────────────────
        // [Phase 2 — Mass Point QEF (Schaefer/Warren 2005)]
        //   수학: (AᵀA + (λ_reg + λ_mp)·I) x = Aᵀb + λ_mp·c_m
        //   대상 문제:
        //     FragC-γ (float 오차): 양쪽 chunk가 같은 edge의 intersection centroid로 수렴
        //                            → vertex 위치 자연 일치, seam 안정화
        //     FragB 원인 1 잔존 (sharp smax corner): ATA ill-conditioned 시 c_m bias로 안정
        //     FragA 일부 (Nyquist edge count < 3): c_m가 voxel 중심으로 자동 pull
        //   MassPointA/B와의 차이 (규칙 #19):
        //     기존 MassPointA: 사후 lerp (QEF 해 ← massPoint 방향으로 blend)
        //     기존 MassPointB: λ_reg 강화 (Tikhonov만, mass point 무관)
        //     Phase 2       : normal equation에 수학적 통합 — 정통 Schaefer/Warren
        //   상세 분석: fragC_strategic_evaluation.html §4 T3-3 참조.
        //   규칙 #6: OFF 시 SolveQEF_Cholesky 기존 경로 호출 → bit-identical
        //   규칙 #3: GenerateQuads 미수정 (SolveQEF 커널 내부 분기만)
        // ─────────────────────────────────────────────────────────────
        [Tooltip("ON: (AᵀA+λI)x = Aᵀb+λ·c_m 통합 — FragC-γ + FragB 원인 1 잔존 완화. " +
                 "OFF=기존 SolveQEF_Cholesky 호출(bit-identical). MassPointA/B와 독립.")]
        public bool enablePhase2MassPointQEF = false;

        [Tooltip("Mass Point bias 강도 λ_mp. 0=QEF 그대로, 대=massPoint 수렴. " +
                 "권장 0.1~1.0. 0.5 시작 추천. 큰 값은 sharp feature 손실 가능.")]
        public float phase2MassPointStrength = 0.5f;

        // ─────────────────────────────────────────────────────────────
        // [Phase 3-B — Ghost QEF Binding]
        //
        // CaveTerrainConfig.enablePhase3OverlapRemoval이 ON일 때만 작동.
        // ChunkGhostDataManager에서 인접 chunk의 boundary edges를 조회 →
        // StructuredBuffer로 GPU에 주입 → SolveQEF 커널의 AccumulateGhostEdges가 사용.
        //
        // 규칙 #6: OFF 시 _EnablePhase3GhostQEF=0 주입 → compute shader에서 분기 skip
        //          → 기존 경로 bit-identical
        // 규칙 #12: StructuredBuffer는 매 dispatch마다 재생성/Release (누수 방지)
        // 규칙 #19: ghost edges는 원시 intersection + normal (합성 지표 없음)
        // 규칙 #23: _ghostEdgeBuffer는 본 클래스가 소유 → Release 생명주기 명시
        // ─────────────────────────────────────────────────────────────

        // 내부 상태 - dispatch 간 재사용 (메모리 할당 최소화)
        private ComputeBuffer _ghostEdgeBuffer = null;
        private int _ghostEdgeBufferCapacity = 0;   // LRU 확장 상한

        [Header("P0.5-A — GPU Clear")]
        [Tooltip("ON: GPU Dispatch Clear (0.5ms). OFF: CPU SetData (18ms, 원본)")]
        public bool enableGpuClear = false;

        [Header("P1-A — Compressed Voxel")]
        [Tooltip("ON: density 4B + ore 4B 분리 (캐시 4× 효율). OFF: CaveVoxel 16B (원본)")]
        public bool enableCompressedVoxel = false;

        [Header("O3 — Compressed Hermite")]
        [Tooltip("ON: DCHermiteEdge 32B→16B (OctWrap+half3, VRAM -122MB). OFF: 원본 32B")]
        public bool enableCompressedHermite = false;

        [Header("O1 — Compressed Vertex")]
        [Tooltip("ON: DCVertex 56B→32B (OctWrap+half2, VRAM -122MB). OFF: 원본 56B")]
        public bool enableCompressedVertex = false;

        [Header("P1-B — GPU Vertex Compaction")]
        [Tooltip("ON: 유효 vertex만 readback (71MB→3.8MB). OFF: 전체 readback (원본)")]
        public bool enableCompaction = false;

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

        // [P1-A] 압축 버퍼
        private ComputeBuffer densityBuffer;     // float (4B/voxel)
        private ComputeBuffer oreBuffer;         // int (4B/voxel)

        // [P1-B] Compaction 버퍼
        private ComputeBuffer compactVertexBuffer;   // DCVertex (유효만)
        private ComputeBuffer remapTable;            // int (N³, GPU 내부만)
        private ComputeBuffer validVertexCountBuffer; // atomic counter (4B)

        // --- 커널 캐싱 ---
        private int kernelClearHermite  = -1;   // [FIX-D]
        private int kernelCollectHermite = -1;
        private int kernelSolveQEF      = -1;
        private int kernelGenerateQuads = -1;
        private int kernelClearDCVertex = -1;   // [P0.5-A]
        private int kernelSplitVoxel   = -1;   // [P1-A]
        private int kernelCompactAndRemap = -1; // [P1-B]
        private int kernelRemapQuads     = -1;  // [P1-B]
        private int kernelExtractBoundaryHermite = -1; // [Phase 3-B]

        // ═══════════════════════════════════════════════════════════════
        // [Phase 3-B / Gate 2-γ] Ghost Edge QEF 관련 버퍼
        // 
        // boundaryExtractBuffer:
        //   AppendStructuredBuffer<ExtractedGhostEdge>, capacity 약 200K edges (40B × 200K ≈ 8MB)
        //   (6 face × (N-1)² cells × 3 axis × surface-crossing ratio ≈ 이론 상한)
        //   실제 사용량은 chunk surface 밀도에 따라 10K~100K
        //
        // boundaryExtractCount:
        //   Append count (ComputeBuffer.CopyCount 대상)
        //
        // ghostEdgeInputBuffer:
        //   StructuredBuffer<GhostEdge> (32B), 이전 chunk에서 저장된 neighbor의 extracted edges를
        //   CPU 측에서 재직렬화하여 다음 chunk의 SolveQEF에 주입.
        //   capacity: 6 face × 50K = 300K × 32B ≈ 9.6MB (합리적)
        // ═══════════════════════════════════════════════════════════════
        private ComputeBuffer boundaryExtractBuffer;    // [Phase 3-B] AppendBuffer
        private ComputeBuffer boundaryExtractCount;     // [Phase 3-B] counter readback
        private ComputeBuffer ghostEdgeInputBuffer;     // [Phase 3-B] neighbor edges (SolveQEF input)
        private const int BOUNDARY_EXTRACT_CAPACITY = 200000; // 상한, 필요시 조정
        private const int GHOST_EDGE_INPUT_CAPACITY  = 300000;

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
            kernelClearDCVertex  = dcComputeShader.FindKernel("ClearDCVertexBuffer"); // [P0.5-A]
            kernelSplitVoxel     = dcComputeShader.FindKernel("SplitVoxelBuffer");    // [P1-A]
            kernelCompactAndRemap = dcComputeShader.FindKernel("CompactAndRemap");    // [P1-B]
            kernelRemapQuads     = dcComputeShader.FindKernel("RemapQuads");          // [P1-B]
            kernelExtractBoundaryHermite = dcComputeShader.FindKernel("ExtractBoundaryHermiteEdges"); // [Phase 3-B]

            Debug.Log($"[DCPipeline] 커널 초기화: ClearH={kernelClearHermite}, ClearDC={kernelClearDCVertex}, Split={kernelSplitVoxel}, Hermite={kernelCollectHermite}, QEF={kernelSolveQEF}, Quad={kernelGenerateQuads}, ExtractBoundary={kernelExtractBoundaryHermite}");
        }

        // ==================================================================
        // 버퍼 할당/해제
        // ==================================================================
        private bool _lastCompressedHermite = false; // [O3] 토글 변경 감지
        private bool _lastCompressedVertex = false; // [O1] 토글 변경 감지

        public void AllocateBuffers(int pointsPerAxis)
        {
            // [O3/O1] 토글 변경 시에도 재할당 (stride 변경)
            bool hermiteToggleChanged = (enableCompressedHermite != _lastCompressedHermite);
            bool vertexToggleChanged  = (enableCompressedVertex != _lastCompressedVertex);
            if (currentPointsPerAxis == pointsPerAxis && !hermiteToggleChanged && !vertexToggleChanged) return;

            ReleaseBuffers();

            currentPointsPerAxis = pointsPerAxis;
            _lastCompressedHermite = enableCompressedHermite;
            _lastCompressedVertex  = enableCompressedVertex;
            int N3       = pointsPerAxis * pointsPerAxis * pointsPerAxis;
            int maxEdges = N3 * 3;

            // [O3] hermiteStride: 압축 ON=16B (uint4), OFF=32B (DCHermiteEdge)
            int hermiteStride = enableCompressedHermite ? 16 : Marshal.SizeOf(typeof(DCHermiteEdge));
            // [O1] vertexStride: 압축 ON=32B (DCVertexCompressed), OFF=56B (DCVertex)
            int vertexStride  = enableCompressedVertex ? 32 : Marshal.SizeOf(typeof(DCVertex));
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

            // [P1-A] 압축 버퍼 (enableCompressedVoxel 시 사용, 미리 할당)
            densityBuffer = new ComputeBuffer(N3, sizeof(float));   // 4B/voxel
            oreBuffer     = new ComputeBuffer(N3, sizeof(int));     // 4B/voxel

            // [P1-B] Compaction 버퍼 — [O1] stride 동기화
            compactVertexBuffer   = new ComputeBuffer(N3, vertexStride);
            remapTable            = new ComputeBuffer(N3, sizeof(int));
            validVertexCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

            Debug.Log($"[DCPipeline] 버퍼 할당: N={pointsPerAxis}, Hermite={maxEdges}×{hermiteStride}B, Vertex={N3}×{vertexStride}B");
        }

        private void ReleaseBuffers()
        {
            hermiteEdgeBuffer?.Release();  hermiteEdgeBuffer  = null;
            dcVertexBuffer?.Release();     dcVertexBuffer     = null;
            dcQuadBuffer?.Release();       dcQuadBuffer       = null;
            quadCountBuffer?.Release();    quadCountBuffer    = null;
            gameplaySculptBuffer?.Release(); gameplaySculptBuffer = null;
            densityBuffer?.Release();        densityBuffer = null;         // [P1-A]
            oreBuffer?.Release();            oreBuffer = null;             // [P1-A]
            compactVertexBuffer?.Release();  compactVertexBuffer = null;   // [P1-B]
            remapTable?.Release();           remapTable = null;            // [P1-B]
            validVertexCountBuffer?.Release(); validVertexCountBuffer = null; // [P1-B]
            // [Phase 3-B] ghost edge buffer 해제 (규칙 #12)
            _ghostEdgeBuffer?.Release();   _ghostEdgeBuffer = null;
            _ghostEdgeBufferCapacity = 0;
            // [Phase 3-B / Gate 2-γ] Extract kernel 버퍼 해제
            boundaryExtractBuffer?.Release(); boundaryExtractBuffer = null;
            boundaryExtractCount?.Release();  boundaryExtractCount = null;
            ghostEdgeInputBuffer?.Release();  ghostEdgeInputBuffer = null;
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

        // [P0.5-A] DCVertex 버퍼 클리어 — enableGpuClear 토글
        //   OFF: 원본 CPU SetData (18ms)
        //   ON:  GPU Dispatch (0.5ms)
        private void ClearDCVertexBuffer(int pointsPerAxis)
        {
            int N3 = pointsPerAxis * pointsPerAxis * pointsPerAxis;

            if (enableGpuClear && kernelClearDCVertex >= 0)
            {
                // GPU Clear (P0.5-A)
                dcComputeShader.SetInt("_DCVertexBufferSize", N3);
                dcComputeShader.SetBuffer(kernelClearDCVertex, "_DCVertexBuffer", dcVertexBuffer);
                int groups = Mathf.CeilToInt(N3 / 256.0f);
                dcComputeShader.Dispatch(kernelClearDCVertex, groups, 1, 1);
            }
            else
            {
                // 원본 CPU SetData
                var cleared = new DCVertex[N3];
                for (int i = 0; i < N3; i++)
                    cleared[i].featureType = -1;
                dcVertexBuffer.SetData(cleared);
            }
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
                               float voxelSize, ComputeBuffer voxelBuffer,
                               bool isCoarse = false)  // [Phase 3-B / Approach B] Coarse 청크 격리
        {
            if (dcComputeShader == null || kernelCollectHermite < 0)
            {
                Debug.LogError("[DCPipeline] Compute Shader 또는 커널 미초기화.");
                return;
            }

            AllocateBuffers(pointsPerAxis);

            // [O3] COMPRESSED_HERMITE 키워드 — 모든 hermite 관련 커널 전에 설정
            if (enableCompressedHermite)
                dcComputeShader.EnableKeyword("COMPRESSED_HERMITE");
            else
                dcComputeShader.DisableKeyword("COMPRESSED_HERMITE");

            // [O1] COMPRESSED_VERTEX 키워드 — 모든 vertex 관련 커널 전에 설정
            if (enableCompressedVertex)
                dcComputeShader.EnableKeyword("COMPRESSED_VERTEX");
            else
                dcComputeShader.DisableKeyword("COMPRESSED_VERTEX");

            // [FIX-A] _ChunkSize = (N-1) * voxelSize → spacing = voxelSize (정확 일치)
            //   기존: _ChunkSize = chunkSize(=64) → spacing = 64/(N-1) ≈ 0.985 ≠ voxelSize
            float chunkWorldSize = (float)(pointsPerAxis - 1) * voxelSize;

            // [FIX-D] Hermite 버퍼 클리어 (이전 청크 잔존 데이터 제거)
            ClearHermiteEdgeBuffer(pointsPerAxis);

            // [FIX-E] DCVertex 버퍼 클리어 (featureType=-1 초기화)
            ClearDCVertexBuffer(pointsPerAxis);

            // [P1-A] 압축 토글 uniform 설정
            dcComputeShader.SetInt("_UseCompressedVoxel", enableCompressedVoxel ? 1 : 0);

            // [P1-A] enableCompressedVoxel=ON: Split 커널로 density/ore 분리
            if (enableCompressedVoxel && kernelSplitVoxel >= 0)
            {
                int N3 = pointsPerAxis * pointsPerAxis * pointsPerAxis;
                dcComputeShader.SetInt("_SplitBufferSize", N3);
                dcComputeShader.SetBuffer(kernelSplitVoxel, "_VoxelBuffer",    voxelBuffer);
                dcComputeShader.SetBuffer(kernelSplitVoxel, "_DensityBuffer",  densityBuffer);
                dcComputeShader.SetBuffer(kernelSplitVoxel, "_OreBuffer",      oreBuffer);
                int splitGroups = Mathf.CeilToInt(N3 / 256.0f);
                dcComputeShader.Dispatch(kernelSplitVoxel, splitGroups, 1, 1);
            }

            int threadGroups = Mathf.CeilToInt(pointsPerAxis / 8.0f);

            // --- Stage 2: Hermite Data 수집 ---
            dcComputeShader.SetBuffer(kernelCollectHermite, "_VoxelBuffer",       voxelBuffer);
            dcComputeShader.SetBuffer(kernelCollectHermite, "_HermiteEdgeBuffer", hermiteEdgeBuffer);
            // [P1-A] 압축 버퍼 바인딩 (OFF 시에도 바인딩 — 셰이더 null 참조 방지)
            dcComputeShader.SetBuffer(kernelCollectHermite, "_DensityBuffer",  densityBuffer);
            dcComputeShader.SetBuffer(kernelCollectHermite, "_OreBuffer",      oreBuffer);
            dcComputeShader.SetInt  ("_PointsPerAxis", pointsPerAxis);
            dcComputeShader.SetFloat("_ChunkSize",     chunkWorldSize); // [FIX-A, FIX-B]
            // [Phase 2] DC 품질 토글 — 전 커널 공유 uniform
            dcComputeShader.SetInt("_EnableVisualSmooth", enableVisualSmooth ? 1 : 0);
            dcComputeShader.SetInt("_EnableMassPointA",   enableMassPointA ? 1 : 0);
            dcComputeShader.SetInt("_EnableMassPointB",   enableMassPointB ? 1 : 0);
            // [Phase 2 — Mass Point QEF] Schaefer/Warren 2005 통합 Mass Point bias.
            //   OFF(기본): 기존 SolveQEF_Cholesky 호출 → bit-identical (규칙 #6)
            //   ON      : (AᵀA+λI)x = Aᵀb+λ·c_m — FragC-γ + FragB 원인 1 잔존 완화
            dcComputeShader.SetInt("_EnablePhase2MassPointQEF",  enablePhase2MassPointQEF ? 1 : 0);
            dcComputeShader.SetFloat("_Phase2MassPointStrength", phase2MassPointStrength);
            // [FIX-B] _ChunkOffset 전달 제거 — 셰이더는 순수 로컬 좌표만 사용
            dcComputeShader.Dispatch(kernelCollectHermite, threadGroups, threadGroups, threadGroups);

            // --- Stage 3: QEF 풀기 ---
            // [FIX-C] _VoxelBuffer를 SolveQEF에도 바인딩 (oreType, 그래디언트 읽기)
            dcComputeShader.SetBuffer(kernelSolveQEF, "_VoxelBuffer",       voxelBuffer);
            dcComputeShader.SetBuffer(kernelSolveQEF, "_DensityBuffer",     densityBuffer);  // [P1-A]
            dcComputeShader.SetBuffer(kernelSolveQEF, "_OreBuffer",         oreBuffer);      // [P1-A]
            dcComputeShader.SetBuffer(kernelSolveQEF, "_HermiteEdgeBuffer", hermiteEdgeBuffer);
            dcComputeShader.SetBuffer(kernelSolveQEF, "_DCVertexBuffer",    dcVertexBuffer);
            dcComputeShader.SetBuffer(kernelSolveQEF, "_GameplaySculptBuffer", gameplaySculptBuffer);

            // [Phase 3-B] Ghost QEF uniform + buffer 바인딩
            //   CaveTerrainConfig.enablePhase3OverlapRemoval이 ON일 때만 효력
            //   관련 CaveTerrainConfig 조회 + ChunkGhostDataManager에서 neighbors 수집
            // [Phase 3-B 보강] isCoarse=true 시 Phase 3-B 전체 skip
            //   이유: Coarse voxelSize(~0.5m)와 Fine voxelSize(~0.15m) 불일치 시
            //         ghost edges의 intersectionPoint scale이 다름 → 잘못된 QEF 주입
            //         → gap 또는 geometry 오류 유발
            BindPhase3GhostBuffers(chunkPos, isCoarse);

            // _PointsPerAxis, _ChunkSize는 이미 위에서 설정됨 (uniform 유지)
            dcComputeShader.Dispatch(kernelSolveQEF, threadGroups, threadGroups, threadGroups);

            // --- Stage 4: 쿼드 생성 ---
            quadCountBuffer.SetData(new int[] { 0 });
            // [FIX-C] _VoxelBuffer를 GenerateQuads에도 바인딩 (밀도 부호 읽기)
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_VoxelBuffer",  voxelBuffer);
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_DensityBuffer", densityBuffer);  // [P1-A]
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_OreBuffer",     oreBuffer);      // [P1-A]
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_DCVertexBuffer", dcVertexBuffer);
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_DCQuadBuffer", dcQuadBuffer);
            dcComputeShader.SetBuffer(kernelGenerateQuads, "_QuadCount",    quadCountBuffer);

            // [Gate 2 / B-α] Boundary Ownership uniform 주입
            //   CaveTerrainConfig.enableBoundaryOwnership 조회 → GenerateQuads kernel의 sid[axis]=0 skip 조건 제어
            //   OFF 시 0 주입 → bit-identical (규칙 #6)
            bool bo = false;
            if (CaveManager.Instance != null
                && CaveManager.Instance.chunkManager != null
                && CaveManager.Instance.chunkManager.terrainConfig != null)
            {
                bo = CaveManager.Instance.chunkManager.terrainConfig.enableBoundaryOwnership;
            }
            dcComputeShader.SetInt("_EnableBoundaryOwnership", bo ? 1 : 0);

            dcComputeShader.Dispatch(kernelGenerateQuads, threadGroups, threadGroups, threadGroups);

            // [P1-B] GPU Vertex Compaction — GenerateQuads 이후 실행
            if (enableCompaction && kernelCompactAndRemap >= 0 && kernelRemapQuads >= 0)
            {
                int N3 = pointsPerAxis * pointsPerAxis * pointsPerAxis;
                validVertexCountBuffer.SetData(new int[] { 0 });

                // Kernel 1: CompactAndRemap
                dcComputeShader.SetInt("_CompactBufferSize", N3);
                dcComputeShader.SetBuffer(kernelCompactAndRemap, "_DCVertexBuffer",      dcVertexBuffer);
                dcComputeShader.SetBuffer(kernelCompactAndRemap, "_CompactVertexBuffer",  compactVertexBuffer);
                dcComputeShader.SetBuffer(kernelCompactAndRemap, "_RemapTable",           remapTable);
                dcComputeShader.SetBuffer(kernelCompactAndRemap, "_ValidVertexCount",     validVertexCountBuffer);
                int compactGroups = Mathf.CeilToInt(N3 / 256.0f);
                dcComputeShader.Dispatch(kernelCompactAndRemap, compactGroups, 1, 1);

                // Kernel 2: RemapQuads — quadCount는 GPU에서 결정되었으므로
                // 최대 N³개를 dispatch하되 커널 내부에서 _RemapQuadCount로 가드
                // quadCount를 CPU에서 모르므로 최대치(N³)로 dispatch
                dcComputeShader.SetInt("_RemapQuadCount", N3); // 커널에서 quadCount 범위 체크
                dcComputeShader.SetBuffer(kernelRemapQuads, "_DCQuadBuffer",  dcQuadBuffer);
                dcComputeShader.SetBuffer(kernelRemapQuads, "_RemapTable",    remapTable);
                dcComputeShader.Dispatch(kernelRemapQuads, compactGroups, 1, 1);
            }

            // ═══════════════════════════════════════════════════════════════
            // [Phase 3-B / Gate 2-γ] boundary Hermite edges 추출 + async readback
            //
            // 목적:
            //   현재 chunk가 생성한 Hermite buffer의 6개 boundary face edges를
            //   compact AppendBuffer로 추출 → 비동기 readback → ChunkGhostData.facesEdges에 저장.
            //   이후 neighbor chunk가 SolveQEF 시 BindPhase3GhostBuffers로 이 데이터 사용.
            //
            // 토글 가드:
            //   enablePhase3OverlapRemoval=false 시 skip → 기존 경로와 동일
            //   토글 ON 시에도 kernelExtractBoundaryHermite >= 0 체크
            //
            // [Phase 3-B 보강 — 규칙 #26] isCoarse 가드
            //   isCoarse=true 시 Extract 자체를 skip — Coarse voxelSize 기준 edges가
            //   facesEdges에 저장되면 Fine neighbor가 읽을 때 scale 불일치 → gap 유발.
            //   Coarse mesh는 scene에 배치 안 되므로 Phase 3-B에 기여할 이유 없음 (DCMeshBuilder.S2와 대칭).
            // ═══════════════════════════════════════════════════════════════
            bool phase3Enabled = false;
            if (CaveManager.Instance != null
                && CaveManager.Instance.chunkManager != null
                && CaveManager.Instance.chunkManager.terrainConfig != null
                && CaveManager.Instance.chunkManager.terrainConfig.enablePhase3OverlapRemoval
                && kernelExtractBoundaryHermite >= 0
                && !isCoarse)  // [Phase 3-B 보강] Coarse 청크는 skip
            {
                phase3Enabled = true;
            }

            if (phase3Enabled)
            {
                DispatchExtractBoundaryHermiteEdges(chunkPos, pointsPerAxis, threadGroups);
            }
        }

        /// <summary>
        /// [Phase 3-B] Extract kernel dispatch + async readback 큐잉.
        ///   - boundaryExtractBuffer (AppendStructuredBuffer) 초기화
        ///   - kernel dispatch
        ///   - CopyCount로 edge 수 readback
        ///   - 이후 edge buffer 자체 readback → CPU 측 faceEdges에 직렬화
        /// </summary>
        private void DispatchExtractBoundaryHermiteEdges(Vector3Int chunkPos, int pointsPerAxis, int threadGroups)
        {
            // 버퍼 lazy 할당 (크기 고정 — BOUNDARY_EXTRACT_CAPACITY)
            if (boundaryExtractBuffer == null || !boundaryExtractBuffer.IsValid())
            {
                // 40B = ExtractedGhostEdge struct (float3 pos + float3 nrm + 4*int)
                boundaryExtractBuffer = new ComputeBuffer(BOUNDARY_EXTRACT_CAPACITY, 40, ComputeBufferType.Append);
            }
            if (boundaryExtractCount == null || !boundaryExtractCount.IsValid())
            {
                boundaryExtractCount = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
            }

            // AppendBuffer counter 초기화
            boundaryExtractBuffer.SetCounterValue(0);

            // kernel binding
            dcComputeShader.SetBuffer(kernelExtractBoundaryHermite, "_HermiteEdgeBuffer", hermiteEdgeBuffer);
            dcComputeShader.SetBuffer(kernelExtractBoundaryHermite, "_BoundaryHermiteOutput", boundaryExtractBuffer);
            dcComputeShader.SetInt("_PointsPerAxis", pointsPerAxis);

            // Dispatch (CollectHermite와 동일 sweep: 전체 cell을 훑으며 boundary cell만 통과)
            dcComputeShader.Dispatch(kernelExtractBoundaryHermite, threadGroups, threadGroups, threadGroups);

            // Edge 수 readback (counter)
            ComputeBuffer.CopyCount(boundaryExtractBuffer, boundaryExtractCount, 0);

            // ═══════════════════════════════════════════════════════════════
            // [Phase 3-B 보강 — SYNC READBACK] async → 동기 readback 전환
            //
            // 이유:
            //   AsyncGPUReadback은 1~3 프레임 지연 발생 → 다음 chunk가 SolveQEF 실행 시점에
            //   이 chunk의 facesEdges가 아직 비어있을 수 있음 → Ghost QEF 미작동 → gap 발생.
            //
            //   동기 readback은 blocking이지만 boundary edges 크기가 작아(~1MB 이하) 비용 미미:
            //     - count buffer: 4B
            //     - edge buffer: edgeCount × 40B (평균 10K edges = 400KB)
            //     - 전체 readback 시간: ~0.5~2ms per chunk
            //
            //   Phase 3-B의 이론적 완벽 seam을 실제 달성하려면 sync readback 필수.
            // ═══════════════════════════════════════════════════════════════
            int[] countArr = new int[1];
            boundaryExtractCount.GetData(countArr);  // BLOCKING — 즉시 edgeCount 확보
            int edgeCount = countArr[0];

            if (edgeCount <= 0)
            {
                RegisterEmptyFacesEdges(chunkPos, pointsPerAxis);
                return;
            }

            // edge buffer 동기 readback
            if (edgeCount > BOUNDARY_EXTRACT_CAPACITY)
            {
                Debug.LogWarning($"[Phase3B:Extract] {chunkPos} edgeCount={edgeCount} exceeds capacity {BOUNDARY_EXTRACT_CAPACITY} — truncating");
                edgeCount = BOUNDARY_EXTRACT_CAPACITY;
            }
            var rawData = new ExtractedGhostEdgeCPU[edgeCount];
            boundaryExtractBuffer.GetData(rawData, 0, 0, edgeCount);  // BLOCKING

            PopulateFacesEdgesFromReadbackArray(chunkPos, rawData, edgeCount, pointsPerAxis);
        }

        /// <summary>
        /// [Phase 3-B] GPU ExtractedGhostEdge struct의 CPU side 레이아웃 (40B, faceId 포함).
        ///   shader의 struct ExtractedGhostEdge와 1:1 일치.
        /// </summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Size = 40)]
        private struct ExtractedGhostEdgeCPU
        {
            public Vector3 intersectionPoint;  // 12B (0)
            public Vector3 normal;             // 12B (12)
            public int cellIndexLinear;        // 4B (24)
            public int edgeAxis;               // 4B (28)
            public int faceId;                 // 4B (32) — 0=+X 1=-X 2=+Y 3=-Y 4=+Z 5=-Z
            public int _pad;                   // 4B (36)
        }

        /// <summary>
        /// [Phase 3-B] Readback된 edges를 faceId로 분리하여 ChunkGhostData.facesEdges[6]에 저장.
        /// [SYNC 경로] ComputeBuffer.GetData로 받은 C# array 처리
        /// </summary>
        private void PopulateFacesEdgesFromReadbackArray(Vector3Int chunkPos,
            ExtractedGhostEdgeCPU[] rawData, int edgeCount, int pointsPerAxis)
        {
            if (ChunkGhostDataManager.Instance == null) return;

            // 자기 chunk의 ChunkGhostData 가져오기 (없으면 생성)
            ChunkGhostData data;
            if (!ChunkGhostDataManager.Instance.TryGetChunkGhostData(chunkPos, out data))
            {
                // ChunkGhostData 생성자가 (float vs, int cs) 필수 + facesEdges 내부 초기화함
                float vsNew = (CaveManager.Instance != null && CaveManager.Instance.chunkManager != null)
                    ? CaveManager.Instance.chunkManager.VoxelSize : 0.15f;
                int csNew = (CaveManager.Instance != null && CaveManager.Instance.chunkManager != null)
                    ? CaveManager.Instance.chunkManager.ChunkSize : 107;
                data = new ChunkGhostData(vsNew, csNew);
            }
            for (int f = 0; f < 6; f++)
            {
                if (data.facesEdges[f] == null)
                    data.facesEdges[f] = new System.Collections.Generic.List<GhostBoundaryEdge>();
                else
                    data.facesEdges[f].Clear();
            }

            // faceId별 분류하여 List에 Add
            for (int i = 0; i < edgeCount; i++)
            {
                var ege = rawData[i];
                if (ege.faceId < 0 || ege.faceId > 5) continue;
                data.facesEdges[ege.faceId].Add(new GhostBoundaryEdge(
                    ege.intersectionPoint,
                    ege.normal,
                    ege.cellIndexLinear,
                    ege.edgeAxis
                ));
            }

            // chunk 메타 정보 업데이트
            if (CaveManager.Instance != null && CaveManager.Instance.chunkManager != null)
            {
                data.voxelSize = CaveManager.Instance.chunkManager.VoxelSize;
                data.chunkSize = CaveManager.Instance.chunkManager.ChunkSize;
            }
            data.timestamp = System.DateTime.Now.Ticks;

            // cache 저장 (StoreChunkGhostData는 LRU 관리 + 신규/기존 자동 처리)
            ChunkGhostDataManager.Instance.StoreChunkGhostData(chunkPos, data);

            int total = 0;
            for (int f = 0; f < 6; f++) total += data.facesEdges[f]?.Count ?? 0;
            if (total > 0)
            {
                Debug.Log($"[Phase3B:Populate-SYNC] {chunkPos} total={total} " +
                          $"(+X={data.facesEdges[0]?.Count ?? 0}, -X={data.facesEdges[1]?.Count ?? 0}, " +
                          $"+Y={data.facesEdges[2]?.Count ?? 0}, -Y={data.facesEdges[3]?.Count ?? 0}, " +
                          $"+Z={data.facesEdges[4]?.Count ?? 0}, -Z={data.facesEdges[5]?.Count ?? 0})");
            }
        }

        /// <summary>
        /// [Phase 3-B] edgeCount=0인 경우 빈 facesEdges 저장 (NullRef 방지).
        /// </summary>
        private void RegisterEmptyFacesEdges(Vector3Int chunkPos, int pointsPerAxis)
        {
            if (ChunkGhostDataManager.Instance == null) return;
            ChunkGhostData data;
            if (!ChunkGhostDataManager.Instance.TryGetChunkGhostData(chunkPos, out data))
            {
                float vsNew = (CaveManager.Instance != null && CaveManager.Instance.chunkManager != null)
                    ? CaveManager.Instance.chunkManager.VoxelSize : 0.15f;
                int csNew = (CaveManager.Instance != null && CaveManager.Instance.chunkManager != null)
                    ? CaveManager.Instance.chunkManager.ChunkSize : 107;
                data = new ChunkGhostData(vsNew, csNew);
            }
            for (int f = 0; f < 6; f++)
            {
                if (data.facesEdges[f] == null)
                    data.facesEdges[f] = new System.Collections.Generic.List<GhostBoundaryEdge>();
                else
                    data.facesEdges[f].Clear();
            }
            ChunkGhostDataManager.Instance.StoreChunkGhostData(chunkPos, data);
        }

        /// <summary>비동기 GPU 읽기 — enableCompaction ON/OFF 분기</summary>
        public void ReadbackAsync(System.Action<DCVertex[], DCQuad[], int> onComplete)
        {
            if (quadCountBuffer == null || dcVertexBuffer == null || dcQuadBuffer == null)
            {
                Debug.LogWarning("[DCPipeline] 버퍼 미할당. DispatchDC() 먼저 호출 필요.");
                onComplete?.Invoke(null, null, 0);
                return;
            }

            if (enableCompaction && compactVertexBuffer != null && validVertexCountBuffer != null)
            {
                // ── P1-B: Compact Readback 경로 (3.8MB) ──
                // 1) quadCount + validVertexCount 동시 readback
                AsyncGPUReadback.Request(quadCountBuffer, (countReq) =>
                {
                    if (_isDestroyed || quadCountBuffer == null) return;
                    if (countReq.hasError) { onComplete?.Invoke(null, null, 0); return; }
                    int quadCount = countReq.GetData<int>()[0];
                    if (quadCount == 0) { onComplete?.Invoke(null, null, 0); return; }

                    AsyncGPUReadback.Request(validVertexCountBuffer, (validReq) =>
                    {
                        if (_isDestroyed || validVertexCountBuffer == null) return;
                        if (validReq.hasError) { onComplete?.Invoke(null, null, 0); return; }
                        int validCount = validReq.GetData<int>()[0];
                        if (validCount == 0) { onComplete?.Invoke(null, null, 0); return; }

                        // 2) compactVertexBuffer (validCount × stride) — (src, size, offset)
                        // [O1] stride = 32B (compressed) 또는 56B (original)
                        int vertStride = enableCompressedVertex ? 32 : Marshal.SizeOf(typeof(DCVertex));
                        int vertByteSize = validCount * vertStride;
                        AsyncGPUReadback.Request(compactVertexBuffer, vertByteSize, 0, (vertReq) =>
                        {
                            if (_isDestroyed || compactVertexBuffer == null) return;
                            if (vertReq.hasError) { onComplete?.Invoke(null, null, 0); return; }

                            // [O1] 압축 시 DCVertexCompressed → DCVertex 변환
                            DCVertex[] verts;
                            if (enableCompressedVertex)
                            {
                                var compressed = vertReq.GetData<DCVertexCompressed>().ToArray();
                                verts = DCVertexDecompressor.Decompress(compressed);
                            }
                            else
                            {
                                verts = vertReq.GetData<DCVertex>().ToArray();
                            }

                            // 3) remapped dcQuadBuffer (quadCount × 16B) — (src, size, offset)
                            int quadByteSize = quadCount * Marshal.SizeOf(typeof(DCQuad));
                            AsyncGPUReadback.Request(dcQuadBuffer, quadByteSize, 0, (quadReq) =>
                            {
                                if (_isDestroyed || dcQuadBuffer == null) return;
                                if (quadReq.hasError) { onComplete?.Invoke(null, null, 0); return; }
                                var quads = quadReq.GetData<DCQuad>().ToArray();
                                onComplete?.Invoke(verts, quads, quadCount);
                            });
                        });
                    });
                });
            }
            else
            {
                // ── 원본 경로 (71.1MB) ──
                AsyncGPUReadback.Request(quadCountBuffer, (countReq) =>
                {
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
                        // [O1] 압축 시 DCVertexCompressed → DCVertex 변환
                        DCVertex[] verts;
                        if (enableCompressedVertex)
                        {
                            var compressed = vertReq.GetData<DCVertexCompressed>().ToArray();
                            verts = DCVertexDecompressor.Decompress(compressed);
                        }
                        else
                        {
                            verts = vertReq.GetData<DCVertex>().ToArray();
                        }

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
        }

        private void OnSDFProfileChanged()
        {
            Debug.Log("[DCPipeline] SDF Profile 변경 감지 → GPU 버퍼 갱신 예정");
        }

        public bool IsInitialized => kernelCollectHermite >= 0;
        public ComputeBuffer DCVertexBuffer      => dcVertexBuffer;
        public ComputeBuffer DCQuadBuffer        => dcQuadBuffer;
        public ComputeBuffer GameplaySculptBuffer => gameplaySculptBuffer;

        // ═════════════════════════════════════════════════════════════
        // [Phase 3-B] Ghost QEF Buffer Binding
        //
        // 동작:
        //   1. CaveTerrainConfig.enablePhase3OverlapRemoval 확인 (OFF이면 조기 종료)
        //   2. ChunkGhostDataManager.Instance.QueryNeighbors(chunkPos) 호출
        //   3. 6개 face의 edges를 단일 GhostEdge[] 배열로 직렬화 + offsets/counts 계산
        //   4. _ghostEdgeBuffer 재할당 필요 시 Release + new ComputeBuffer
        //   5. SetData + SetBuffer + uniform 주입
        //
        // 규칙 #6: OFF 경로는 _EnablePhase3GhostQEF=0 주입 + 빈 dummy buffer 바인딩 (shader 안전)
        // 규칙 #12: _ghostEdgeBuffer는 ReleaseBuffers에서 해제 (이미 추가됨)
        // 규칙 #19: GhostEdge 내용은 인접 chunk의 원시 intersectionPoint + normal
        // 규칙 #23: 버퍼 소유권은 DCPipelineExtension. ChunkGhostDataManager는 CPU 데이터만
        // ═════════════════════════════════════════════════════════════
        private static readonly int GHOST_EDGE_STRUCT_SIZE = 32;   // float3 + float3 + int + int
        private static readonly int GHOST_DUMMY_SIZE = 1;          // Unity는 size 0 ComputeBuffer 불허

        private void BindPhase3GhostBuffers(Vector3Int chunkPos, bool isCoarse = false)
        {
            // 1. 토글 OFF 경로 — 빈 값 주입 + 조기 종료 (규칙 #6 bit-identical)
            // [Phase 3-B 보강] isCoarse=true 시 Coarse voxelSize 불일치로 ghost edges 무효화
            bool enableGhost = false;
            if (!isCoarse                                            // [Phase 3-B 보강] Coarse 청크는 ghost edges 안 받음
                && CaveManager.Instance != null
                && CaveManager.Instance.chunkManager != null
                && CaveManager.Instance.chunkManager.terrainConfig != null
                && CaveManager.Instance.chunkManager.terrainConfig.enablePhase3OverlapRemoval
                && ChunkGhostDataManager.Instance != null)
            {
                enableGhost = true;
            }

            if (!enableGhost)
            {
                // OFF 경로: _EnablePhase3GhostQEF=0 주입 → AccumulateGhostEdges 호출 안 됨
                // 더미 버퍼라도 바인딩 필요 (SetBuffer 누락 시 Unity 경고)
                dcComputeShader.SetInt("_EnablePhase3GhostQEF", 0);
                dcComputeShader.SetInts("_GhostFaceOffsets", new int[] { 0, 0, 0, 0 });
                dcComputeShader.SetInts("_GhostFaceOffsetsZ", new int[] { 0, 0 });
                dcComputeShader.SetInts("_GhostFaceCounts", new int[] { 0, 0, 0, 0 });
                dcComputeShader.SetInts("_GhostFaceCountsZ", new int[] { 0, 0 });
                EnsureDummyGhostBuffer();
                dcComputeShader.SetBuffer(kernelSolveQEF, "_GhostEdgeBuffer", _ghostEdgeBuffer);
                return;
            }

            // 2. 인접 chunk 조회
            ChunkGhostData[] neighbors = ChunkGhostDataManager.Instance.QueryNeighbors(chunkPos);
            // neighbors[0~5] = +X/-X/+Y/-Y/+Z/-Z (ChunkGhostDataManager.QueryNeighbors 규약)

            // 3. 총 edge 수 계산 + face별 offset 준비
            int[] faceCounts = new int[6];
            int totalEdges = 0;
            int neighborsWithData = 0;
            for (int f = 0; f < 6; f++)
            {
                if (neighbors[f] == null) { faceCounts[f] = 0; continue; }
                // 주의: neighbors[f]의 facesEdges[opposite_face_of_f]를 가져와야 함
                //   ex: +X 인접 chunk의 -X face가 이 chunk의 +X face와 맞닿음
                //   oppositeFace[f] = f ^ 1  (0↔1, 2↔3, 4↔5)
                int oppositeFace = f ^ 1;
                var faceEdges = neighbors[f].facesEdges[oppositeFace];
                int cnt = faceEdges != null ? faceEdges.Count : 0;
                faceCounts[f] = cnt;
                totalEdges += cnt;
                if (cnt > 0) neighborsWithData++;
            }

            // [Phase 3-B 진단] 실제로 받은 ghost edges 로그 — async timing 이슈 확인용
            Debug.Log($"[Phase3B:Bind] {chunkPos} totalGhostEdges={totalEdges}, " +
                      $"neighborsWithData={neighborsWithData}/6, " +
                      $"faceCounts=(+X={faceCounts[0]}, -X={faceCounts[1]}, " +
                      $"+Y={faceCounts[2]}, -Y={faceCounts[3]}, +Z={faceCounts[4]}, -Z={faceCounts[5]})");

            int[] faceOffsets = new int[6];
            int runningOffset = 0;
            for (int f = 0; f < 6; f++)
            {
                faceOffsets[f] = runningOffset;
                runningOffset += faceCounts[f];
            }

            // 4. totalEdges=0 이면 OFF 경로와 동일 동작 (uniform만 0/zero 주입)
            if (totalEdges == 0)
            {
                dcComputeShader.SetInt("_EnablePhase3GhostQEF", 0);
                dcComputeShader.SetInts("_GhostFaceOffsets", new int[] { 0, 0, 0, 0 });
                dcComputeShader.SetInts("_GhostFaceOffsetsZ", new int[] { 0, 0 });
                dcComputeShader.SetInts("_GhostFaceCounts", new int[] { 0, 0, 0, 0 });
                dcComputeShader.SetInts("_GhostFaceCountsZ", new int[] { 0, 0 });
                EnsureDummyGhostBuffer();
                dcComputeShader.SetBuffer(kernelSolveQEF, "_GhostEdgeBuffer", _ghostEdgeBuffer);
                return;
            }

            // 5. GhostEdge[] 직렬화 (chunk 좌표계 변환)
            //    neighbors[f]의 edges는 해당 neighbor chunk의 local 좌표계 (chunk 원점 기준)
            //    현재 chunk에서 사용하려면 offset = (neighborPos - thisPos) * chunkWorldSize 만큼 shift
            //    (neighbor의 +X face edge는 이 chunk의 local 좌표로는 +chunkWorldSize 근처에 위치)
            float chunkWorldSize = CaveManager.Instance.chunkManager.ChunkWorldSize;
            Vector3Int[] neighborOffsets = new Vector3Int[] {
                new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
                new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1)
            };

            GhostEdgeGPU[] packed = new GhostEdgeGPU[totalEdges];
            int writeIdx = 0;
            for (int f = 0; f < 6; f++)
            {
                if (neighbors[f] == null) continue;
                int oppositeFace = f ^ 1;
                var faceEdges = neighbors[f].facesEdges[oppositeFace];
                if (faceEdges == null) continue;

                Vector3 shiftToThis = (Vector3)neighborOffsets[f] * chunkWorldSize;
                for (int k = 0; k < faceEdges.Count; k++)
                {
                    var ge = faceEdges[k];
                    packed[writeIdx++] = new GhostEdgeGPU
                    {
                        // 인접 chunk local → 이 chunk local: neighbor local + shift
                        intersectionPoint = ge.intersectionPoint + shiftToThis,
                        normal            = ge.normal,
                        cellIndexLinear   = ge.cellIndexLinear,
                        edgeAxis          = ge.edgeAxis
                    };
                }
            }

            // 6. ComputeBuffer 재할당/재사용
            if (_ghostEdgeBuffer == null || _ghostEdgeBufferCapacity < totalEdges)
            {
                _ghostEdgeBuffer?.Release();
                // capacity는 1.5배 오버프로비전 (LRU 확장 최소화)
                _ghostEdgeBufferCapacity = Mathf.NextPowerOfTwo(Mathf.Max(totalEdges, 256));
                _ghostEdgeBuffer = new ComputeBuffer(
                    _ghostEdgeBufferCapacity, GHOST_EDGE_STRUCT_SIZE);
            }
            _ghostEdgeBuffer.SetData(packed, 0, 0, totalEdges);

            // 7. Uniform + buffer 주입
            dcComputeShader.SetInt("_EnablePhase3GhostQEF", 1);
            dcComputeShader.SetInts("_GhostFaceOffsets",
                new int[] { faceOffsets[0], faceOffsets[1], faceOffsets[2], faceOffsets[3] });
            dcComputeShader.SetInts("_GhostFaceOffsetsZ",
                new int[] { faceOffsets[4], faceOffsets[5] });
            dcComputeShader.SetInts("_GhostFaceCounts",
                new int[] { faceCounts[0], faceCounts[1], faceCounts[2], faceCounts[3] });
            dcComputeShader.SetInts("_GhostFaceCountsZ",
                new int[] { faceCounts[4], faceCounts[5] });
            dcComputeShader.SetBuffer(kernelSolveQEF, "_GhostEdgeBuffer", _ghostEdgeBuffer);
        }

        /// <summary>
        /// [Phase 3-B] OFF 경로에서도 null이 아닌 dummy buffer가 필요 (Unity 경고 회피).
        /// </summary>
        private void EnsureDummyGhostBuffer()
        {
            if (_ghostEdgeBuffer == null || _ghostEdgeBufferCapacity == 0)
            {
                _ghostEdgeBuffer?.Release();
                _ghostEdgeBufferCapacity = GHOST_DUMMY_SIZE;
                _ghostEdgeBuffer = new ComputeBuffer(
                    GHOST_DUMMY_SIZE, GHOST_EDGE_STRUCT_SIZE);
            }
        }

        /// <summary>
        /// [Phase 3-B] GPU struct - compute shader의 GhostEdge와 layout 일치.
        /// float3 + float3 + int + int = 32B (GHOST_EDGE_STRUCT_SIZE)
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct GhostEdgeGPU
        {
            public Vector3 intersectionPoint;
            public Vector3 normal;
            public int cellIndexLinear;
            public int edgeAxis;
        }
    }
}
