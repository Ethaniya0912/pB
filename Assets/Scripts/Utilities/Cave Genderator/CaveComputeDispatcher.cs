using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System;

namespace CaveSystem
{
    /// <summary>
    /// [Phase 3] CPU의 연산 데이터(그래프 설계도 및 바이옴 데이터)를 GPU로 전송하고, 
    /// 컴퓨트 셰이더의 멀티 커널 실행 및 비동기 회수(AsyncReadback)를 총괄하는 디스패처입니다.
    /// </summary>
    public class CaveComputeDispatcher : MonoBehaviour
    {
        [Header("Compute Shaders")]
        public ComputeShader densityShader;
        public ComputeShader marchingCubesShader;
        public CaveBiomeSettings caveSettings;

        // [🔥 Race Condition 조치] GPU 버퍼 덮어쓰기 방지를 위한 락(Lock) 플래그
        public bool IsBusy { get; set; } = false;

        // --- GPU 통신용 버퍼 ---
        private ComputeBuffer nodeBuffer;
        private ComputeBuffer edgeBuffer;
        private ComputeBuffer biomeBuffer; // [에러 조치] 다중 지대 파라미터 버퍼 추가

        // 지형 연산용 공통 버퍼 (청크 생성 시 재사용)
        private ComputeBuffer voxelBuffer;
        // [Inspector] Domain Warp 진폭 — 천장/벽 형태 자연화 (권장: 0.5)
        [SerializeField] private float warpAmplitude = 0.5f;

        // [AABB 최적화 토글]
        [Header("AABB Optimization")]
        [Tooltip("D: 적응형 마진 (smin×3+warp+2). OFF=고정 10m")]
        public bool enableAdaptiveMargin = false;
        [Tooltip("C: CPU 사전 필터링 (청크별 노드/엣지). OFF=전체 순회")]
        public bool enableChunkPreFilter = false;

        // [P2 1단계 — Warp Normalization]
        [Header("P2 — Warp Normalization")]
        [Tooltip("ON: warp × (voxelSize / 0.125) — LOD 간 voxel 단위 변위 일관성. OFF: warpAmplitude 원본값 (기존)")]
        public bool enableWarpNormalization = false;
        [Tooltip("정규화 기준 voxelSize (Fine 기준). 이 값에서 warp 변위가 원본과 동일")]
        [SerializeField] private float warpNormalizationBaseVoxelSize = 0.125f;

        [Header("Phase 1 — SDF Feature Toggles")]
        [Tooltip("방/통로 크기 배율 (SO에서 값 읽기)")]
        public bool enableScaling = false;
        [Tooltip("per-edge 폭 ±20% 변형")]
        public bool enableWidthVariation = false;
        [Tooltip("U자 퇴적 (SO에서 값 읽기)")]
        public bool enableSediment = false;
        [Tooltip("바닥 표면 디테일 노이즈 (SO에서 값 읽기)")]
        public bool enableFloorDetail = false;

        // ─────────────────────────────────────────────────────────────
        // [Phase 1.5 / 옵션 B Parabolic Lift] — 제거됨.
        // 원인: finalDensity를 거리 지표로 오인 사용 → 수평층 전역 파편 유발.
        // 대체: 옵션 A (CaveNodeGraphBuilder의 enableFloorYClamp).
        // 본 제거 이유는 phase_15_chunk_boundary_analysis.html 참조.
        // ─────────────────────────────────────────────────────────────

        // CPU 사전 필터링용 임시 버퍼
        private ComputeBuffer filteredNodeBuffer, filteredEdgeBuffer;
        private int filteredNodeCount, filteredEdgeCount;

        private ComputeBuffer triangleBuffer;
        private ComputeBuffer oreBuffer;
        private ComputeBuffer triCountBuffer;
        private ComputeBuffer oreCountBuffer;

        // [🔥 추가: 마칭 큐브 룩업 테이블 버퍼]
        private ComputeBuffer mcEdgeTableBuffer;
        private ComputeBuffer mcTriangleTableBuffer;

        // 메모리 재할당 체크용 변수
        private int currentPointsPerAxis = 0;

        // 커널 캐싱
        private int kernelGenerateDensity;
        private int kernelSimulateErosion;
        private int kernelGenerateMesh;

        private void Awake()
        {
            InitializeKernels();
            SetupMarchingCubesTables(); // [🔥 추가] 테이블 버퍼 초기화
            UpdateBiomeBuffer(); // 초기화 시 바이옴 버퍼를 무조건 1회 셋업합니다.
        }

        private void OnEnable()
        {
            // 에디터에서 기획자가 바이옴 데이터를 수정하면 즉시 감지하여 GPU 버퍼를 갱신합니다.
            CaveBiomeData.OnBiomeModified += UpdateBiomeBuffer;
        }

        private void OnDisable()
        {
            // 메모리 누수 방지
            CaveBiomeData.OnBiomeModified -= UpdateBiomeBuffer;
        }

        private void InitializeKernels()
        {
            kernelGenerateDensity = densityShader.FindKernel("GenerateDensity");
            kernelSimulateErosion = densityShader.FindKernel("SimulateErosion");
            kernelGenerateMesh = marchingCubesShader.FindKernel("GenerateMesh");
        }

        // [🔥 추가] MarchingCubesTables 데이터를 GPU 버퍼로 로드
        private void SetupMarchingCubesTables()
        {
            mcEdgeTableBuffer = new ComputeBuffer(256, sizeof(int));
            mcEdgeTableBuffer.SetData(MarchingCubesTables.EdgeTable);

            mcTriangleTableBuffer = new ComputeBuffer(4096, sizeof(int));
            mcTriangleTableBuffer.SetData(MarchingCubesTables.TriangleTable);
        }

        /// <summary>
        /// [에러 조치] CaveBiomeSettings에 등록된 바이옴 에셋들을 구조체 배열로 패킹하여 GPU에 업로드합니다.
        /// </summary>
        public void UpdateBiomeBuffer()
        {
            if (caveSettings == null || caveSettings.globalBiomes == null || caveSettings.globalBiomes.Count == 0)
            {
                Debug.LogWarning("[CaveComputeDispatcher] 바이옴 데이터가 세팅되지 않았습니다. 안전한 기본값을 주입합니다.");
                if (biomeBuffer != null) { biomeBuffer.Release(); biomeBuffer = null; }

                biomeBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(BiomeParamData)));
                biomeBuffer.SetData(new BiomeParamData[] { new BiomeParamData { padding = 0f } });
                return;
            }

            int count = caveSettings.globalBiomes.Count;
            int stride = Marshal.SizeOf(typeof(BiomeParamData));

            // 배열 크기가 달라졌다면 재할당을 위해 기존 버퍼를 파괴
            if (biomeBuffer != null && biomeBuffer.count != count)
            {
                biomeBuffer.Release();
                biomeBuffer = null;
            }

            if (biomeBuffer == null)
            {
                biomeBuffer = new ComputeBuffer(count, stride);
            }

            // ScriptableObject에서 순수 구조체 데이터만 추출
            BiomeParamData[] biomeDataArray = new BiomeParamData[count];
            for (int i = 0; i < count; i++)
            {
                if (caveSettings.globalBiomes[i] != null)
                {
                    biomeDataArray[i] = caveSettings.globalBiomes[i].GetStructData();
                }
                else
                {
                    biomeDataArray[i] = new BiomeParamData { padding = 0f };
                }
            }

            biomeBuffer.SetData(biomeDataArray);
        }

        /// <summary>
        /// Phase 2에서 완성된 글로벌 노드 그래프 데이터를 GPU 버퍼로 패킹합니다.
        /// </summary>
        public void SetupGraphBuffers(List<NodeData> nodes, List<EdgeData> edges)
        {
            ReleaseGraphBuffers();

            int nodeStride = Marshal.SizeOf(typeof(NodeData));
            int edgeStride = Marshal.SizeOf(typeof(EdgeData));

            int nodeCount = Mathf.Max(1, nodes.Count);
            nodeBuffer = new ComputeBuffer(nodeCount, nodeStride);
            if (nodes.Count > 0) nodeBuffer.SetData(nodes);

            int edgeCount = Mathf.Max(1, edges.Count);
            edgeBuffer = new ComputeBuffer(edgeCount, edgeStride);
            if (edges.Count > 0) edgeBuffer.SetData(edges);

            Debug.Log($"<color=cyan>[ComputeDispatcher]</color> 그래프 데이터 GPU 버퍼 패킹 완료.");
        }

        /// <summary>
        /// 단일 청크에 대한 밀도 생성, 침식 시뮬레이션, 마칭 큐브 연산을 연속 실행합니다.
        /// </summary>
        public void DispatchChunk(ChunkRequestContext context, int chunkSize, float voxelSize, Action<ChunkRequestContext, ComputeBuffer, ComputeBuffer> onGpuCompleted)
        {
            if (IsBusy)
            {
                Debug.LogWarning("[ComputeDispatcher] ⚠️ GPU가 현재 사용 중입니다. Dispatch 요청이 무시되었습니다.");
                return;
            }
            IsBusy = true; // 락(Lock) 걸기

            if (nodeBuffer == null || edgeBuffer == null)
            {
                Debug.LogError("[ComputeDispatcher] 그래프 버퍼가 초기화되지 않았습니다.");
                IsBusy = false;
                return;
            }

            // [에러 조치] 바이옴 버퍼 널 체크 및 안전 보장
            if (biomeBuffer == null)
            {
                UpdateBiomeBuffer();
            }

            // [🚨 3번 조치 완수] 조명 이음새(Normal Seam) 방지를 위한 +2 패딩(Double Ghost Voxel) 도입
            int pointsPerAxis = chunkSize + 2;
            AllocateTempBuffers(pointsPerAxis, chunkSize);

            Vector3 chunkBasePos = new Vector3(context.ChunkPos.x, context.ChunkPos.y, context.ChunkPos.z) * (chunkSize * voxelSize);
            DepthLayer currentLayer = caveSettings.GetLayerSettings(chunkBasePos.y);

            // ----------------------------------------------------
            // 커널 1: 밀도장 연산 (Density Field Generation)
            // ----------------------------------------------------
            densityShader.SetBuffer(kernelGenerateDensity, "_VoxelBuffer", voxelBuffer);
            densityShader.SetBuffer(kernelGenerateDensity, "_NodeBuffer", nodeBuffer);
            densityShader.SetInt("_NodeCount", CaveNodeGraphBuilder.Instance != null ? CaveNodeGraphBuilder.Instance.nodesData.Count : 0);
            densityShader.SetBuffer(kernelGenerateDensity, "_EdgeBuffer", edgeBuffer);
            densityShader.SetInt("_EdgeCount", CaveNodeGraphBuilder.Instance != null ? CaveNodeGraphBuilder.Instance.edgesData.Count : 0);

            // [🔥 에러 조치: 바이옴 파라미터 및 버퍼 명시적 주입]
            densityShader.SetBuffer(kernelGenerateDensity, "_BiomeBuffer", biomeBuffer);
            densityShader.SetInt("_BiomeCount", biomeBuffer.count);
            densityShader.SetFloat("_MacroBiomeScale", Mathf.Max(caveSettings.macroBiomeScale, 1.0f));

            densityShader.SetVector("_ChunkBasePosition", chunkBasePos);
            densityShader.SetInt("_ChunkSize", chunkSize);
            densityShader.SetInt("_PointsPerAxis", pointsPerAxis);
            densityShader.SetFloat("_VoxelSize", voxelSize);
            densityShader.SetInt("_DebugStage", (int)caveSettings.debugStage);

            // 레이어별 SDF 파라미터 적용
            densityShader.SetFloat("_SdfSmoothness", currentLayer.sdfSmoothness);
            densityShader.SetFloat("_FloorAltitude", currentLayer.minAltitude);
            densityShader.SetFloat("_CeilAltitude", currentLayer.maxAltitude);
            densityShader.SetFloat("_FloorBlendRadius", currentLayer.floorBlendRadius);
            densityShader.SetFloat("_CeilBlendRadius", currentLayer.ceilBlendRadius);

            // 바닥 요철 파라미터
            densityShader.SetFloat("_FloorBumpAmplitude", currentLayer.floorBumpAmplitude);
            densityShader.SetFloat("_FloorBumpFrequency", currentLayer.floorBumpFrequency);

            // 3D 스레드 실행 (PointsPerAxis를 기준으로 넉넉하게 할당하여 유령 복셀 구역 연산)
            int threadGroups3D = Mathf.CeilToInt(pointsPerAxis / 8.0f);

            // [P2 1단계] warp 정규화: warp × (voxelSize / baseVoxelSize)
            //   OFF: warpAmplitude 그대로 (원본)
            //   ON:  LOD 간 voxel 단위 변위 일관성 보장 (Fine 기준 4 voxels)
            float effectiveWarp = enableWarpNormalization
                ? warpAmplitude * (voxelSize / Mathf.Max(0.001f, warpNormalizationBaseVoxelSize))
                : warpAmplitude;
            densityShader.SetFloat("_WarpAmplitude", effectiveWarp);

            // [AABB] 적응형 마진 계산 — warp 정규화 시 effectiveWarp 사용 (규칙 #1)
            float aabbMargin = 10.0f; // 기본 = 원본
            if (enableAdaptiveMargin)
            {
                float sminMax = 2.0f; // 바이옴 sminStrength 최대 추정
                if (caveSettings.globalBiomes?.Count > 0 && caveSettings.globalBiomes[0] != null)
                    sminMax = caveSettings.globalBiomes[0].GetStructData().sminStrength;
                aabbMargin = sminMax * 3f + effectiveWarp + 2f;
            }
            densityShader.SetFloat("_AABBMargin", aabbMargin);

            // [Phase 1] SDF 토글
            densityShader.SetInt("_EnableScaling", enableScaling ? 1 : 0);
            densityShader.SetInt("_EnableWidthVariation", enableWidthVariation ? 1 : 0);
            densityShader.SetInt("_EnableSediment", enableSediment ? 1 : 0);
            densityShader.SetFloat("_TunnelWidthScale", currentLayer.tunnelWidthScale > 0.01f ? currentLayer.tunnelWidthScale : 1f);
            densityShader.SetFloat("_RoomSizeScale", currentLayer.roomSizeScale > 0.01f ? currentLayer.roomSizeScale : 1f);
            densityShader.SetFloat("_SedimentAmplitude", currentLayer.sedimentAmplitude);
            densityShader.SetInt("_EnableFloorDetail", enableFloorDetail ? 1 : 0);
            densityShader.SetFloat("_FloorDetailAmplitude", currentLayer.floorDetailAmplitude);
            densityShader.SetFloat("_FloorDetailFrequency", currentLayer.floorDetailFrequency);
            densityShader.SetFloat("_FloorDetailRadius", currentLayer.floorDetailRadius);

            densityShader.Dispatch(kernelGenerateDensity, threadGroups3D, threadGroups3D, threadGroups3D);

            // ═══════════════════════════════════════════════════════════════
            // [pB-4 Week 0] Dual Contouring 분기
            // 밀도장(커널 1)은 위에서 이미 생성 완료.
            // useDualContouring=true이면 MC(커널 2) 대신 DC 3커널을 실행.
            // useDualContouring=false이면 이 블록을 완전히 건너뛰어 기존 MC 동작 100% 유지.
            // ═══════════════════════════════════════════════════════════════
            var dcExtension = GetComponent<DCPipelineExtension>();
            if (dcExtension != null && dcExtension.useDualContouring && dcExtension.IsInitialized)
            {
                // ================================================================
                // [N-2] DiskCache — 캐시 HIT 시 GPU 파이프라인 완전 생략
                // ================================================================
                var diskCache = GetComponent<CaveDiskCache>();
                if (diskCache != null && diskCache.enableDiskCache)
                {
                    DepthLayer cacheLayer = caveSettings.GetLayerSettings(chunkBasePos.y);
                    float cacheEffectiveWarp = enableWarpNormalization
                        ? warpAmplitude * (voxelSize / 0.125f)
                        : warpAmplitude;

                    var meshBuilder = GetComponent<DCMeshBuilder>();
                    string paramHash = diskCache.ComputeParamHash(
                        caveSettings.seed, voxelSize, chunkSize,
                        cacheLayer,
                        enableScaling, enableWidthVariation, enableSediment, enableFloorDetail,
                        enableWarpNormalization, cacheEffectiveWarp,
                        meshBuilder != null && meshBuilder.enableReducedSmoothing,
                        meshBuilder != null && meshBuilder.enableFloorSmoothingJob,
                        dcExtension.enableCompressedHermite, dcExtension.enableCompressedVertex,
                        3.0f // _DCNormalAmplify 기본값
                    );
                    string cacheKey = diskCache.GetCacheKey(context.ChunkPos, paramHash);

                    if (diskCache.HasCache(cacheKey))
                    {
                        // ── 캐시 HIT: 디스크에서 로드, GPU 완전 생략 ──
                        diskCache.LoadAsync(cacheKey, (cachedData) =>
                        {
                            if (cachedData == null)
                            {
                                // 로드 실패 → 캐시 파일 삭제, 다음 사이클에서 GPU 재생성
                                Debug.LogWarning($"[DC-Cache] 로드 실패, 캐시 삭제: {cacheKey}");
                                try { System.IO.File.Delete(diskCache.GetCachePath(cacheKey)); } catch { }
                                context.State = ChunkState.Completed;
                                onGpuCompleted?.Invoke(context, null, null);
                                IsBusy = false;
                                return;
                            }

                            var data = cachedData.Value;
                            Mesh mesh = CaveDiskCache.BuildMeshFromCache(data, $"DCChunk_{context.ChunkPos}");
                            context.FeatureTypes = data.featureTypes;

                            if (meshBuilder != null)
                            {
                                meshBuilder.AssignCachedMeshToScene(mesh, context, chunkSize, voxelSize,
                                    (completedCtx) =>
                                    {
                                        Debug.Log($"[DC-Cache] HIT: {context.ChunkPos}");
                                        onGpuCompleted?.Invoke(completedCtx, null, null);
                                        IsBusy = false;
                                    });
                            }
                            else
                            {
                                context.State = ChunkState.Completed;
                                onGpuCompleted?.Invoke(context, null, null);
                                IsBusy = false;
                            }
                        });
                        return; // GPU 파이프라인 생략
                    }

                    // ── 캐시 MISS: 인스턴스 필드 설정 → 기존 DC 경로 실행 → 완료 시 저장 ──
                    _pendingCacheKey = cacheKey;
                    _pendingDiskCache = diskCache;
                    // fall through to existing DC code below
                }
                else
                {
                    _pendingCacheKey = null;
                    _pendingDiskCache = null;
                }

                // ── DC 파이프라인 (DiskCache 유무 무관, 기존 코드) ──
                // [FIX-H] DC는 +3 패딩 (Seamless Overlap)
                int dcPointsPerAxis = chunkSize + 3;
                AllocateTempBuffers(dcPointsPerAxis, chunkSize, isDCMode: true);
                int dcTG = Mathf.CeilToInt(dcPointsPerAxis / 8.0f);

                // DC용 밀도장 재생성 (dcBasePos = -voxelSize 오프셋으로 오버랩)
                Vector3 dcBasePos = chunkBasePos - new Vector3(voxelSize, voxelSize, voxelSize);
                densityShader.SetBuffer(kernelGenerateDensity, "_VoxelBuffer", voxelBuffer);

                // [Rank 2] CPU 사전 필터링
                if (enableChunkPreFilter)
                {
                    float chunkWorldSize = chunkSize * voxelSize;
                    PreFilterForChunk(dcBasePos, chunkWorldSize + voxelSize * 3, aabbMargin + 5f);
                    BindFilteredBuffers(kernelGenerateDensity);
                }
                else
                {
                    densityShader.SetBuffer(kernelGenerateDensity, "_NodeBuffer", nodeBuffer);
                    densityShader.SetInt("_NodeCount", CaveNodeGraphBuilder.Instance != null ? CaveNodeGraphBuilder.Instance.nodesData.Count : 0);
                    densityShader.SetBuffer(kernelGenerateDensity, "_EdgeBuffer", edgeBuffer);
                    densityShader.SetInt("_EdgeCount", CaveNodeGraphBuilder.Instance != null ? CaveNodeGraphBuilder.Instance.edgesData.Count : 0);
                }
                densityShader.SetBuffer(kernelGenerateDensity, "_BiomeBuffer", biomeBuffer);
                densityShader.SetInt("_BiomeCount", biomeBuffer.count);
                densityShader.SetFloat("_MacroBiomeScale", Mathf.Max(caveSettings.macroBiomeScale, 1.0f));
                densityShader.SetVector("_ChunkBasePosition", dcBasePos);
                densityShader.SetInt("_ChunkSize", chunkSize);
                densityShader.SetInt("_PointsPerAxis", dcPointsPerAxis);
                densityShader.SetFloat("_VoxelSize", voxelSize);
                densityShader.SetInt("_DebugStage", (int)caveSettings.debugStage);
                densityShader.SetFloat("_SdfSmoothness", currentLayer.sdfSmoothness);
                densityShader.SetFloat("_FloorAltitude", currentLayer.minAltitude);
                densityShader.SetFloat("_CeilAltitude", currentLayer.maxAltitude);
                densityShader.SetFloat("_FloorBlendRadius", currentLayer.floorBlendRadius);
                densityShader.SetFloat("_CeilBlendRadius", currentLayer.ceilBlendRadius);
                densityShader.SetFloat("_FloorBumpAmplitude", currentLayer.floorBumpAmplitude);
                densityShader.SetFloat("_FloorBumpFrequency", currentLayer.floorBumpFrequency);
                // [P2 1단계] effectiveWarp는 MC 분기에서 이미 계산됨 (같은 DispatchChunk 호출 내)
                densityShader.SetFloat("_WarpAmplitude", effectiveWarp);
                densityShader.SetFloat("_AABBMargin", aabbMargin);
                densityShader.SetInt("_EnableScaling", enableScaling ? 1 : 0);
                densityShader.SetInt("_EnableWidthVariation", enableWidthVariation ? 1 : 0);
                densityShader.SetInt("_EnableSediment", enableSediment ? 1 : 0);
                densityShader.SetFloat("_TunnelWidthScale", currentLayer.tunnelWidthScale > 0.01f ? currentLayer.tunnelWidthScale : 1f);
                densityShader.SetFloat("_RoomSizeScale", currentLayer.roomSizeScale > 0.01f ? currentLayer.roomSizeScale : 1f);
                densityShader.SetFloat("_SedimentAmplitude", currentLayer.sedimentAmplitude);
                densityShader.SetInt("_EnableFloorDetail", enableFloorDetail ? 1 : 0);
                densityShader.SetFloat("_FloorDetailAmplitude", currentLayer.floorDetailAmplitude);
                densityShader.SetFloat("_FloorDetailFrequency", currentLayer.floorDetailFrequency);
                densityShader.SetFloat("_FloorDetailRadius", currentLayer.floorDetailRadius);
                densityShader.Dispatch(kernelGenerateDensity, dcTG, dcTG, dcTG);

                // 침식도 DC에 적용
                densityShader.SetBuffer(kernelSimulateErosion, "_VoxelBuffer", voxelBuffer);
                densityShader.Dispatch(kernelSimulateErosion, dcTG, dcTG, dcTG);

                // [v3] voxelBuffer density readback → NormalBakerV3 서브복셀 베이킹
                Vector3 bakedBasePos = new Vector3(context.ChunkPos.x, context.ChunkPos.y, context.ChunkPos.z)
                                       * (chunkSize * voxelSize) - Vector3.one * voxelSize;
                int totalVoxels = dcPointsPerAxis * dcPointsPerAxis * dcPointsPerAxis;
                UnityEngine.Rendering.AsyncGPUReadback.Request(voxelBuffer, (densReq) =>
                {
                    // density 배열 추출 (or null if error)
                    float[] densityData = null;
                    if (!densReq.hasError)
                    {
                        var rawVoxels = densReq.GetData<CaveVoxel>();
                        densityData = new float[rawVoxels.Length];
                        for (int di = 0; di < rawVoxels.Length; di++)
                            densityData[di] = rawVoxels[di].density;
                    }

                    // [FIX-G] DC 3커널 디스패치 (density readback 완료 후)
                    dcExtension.DispatchDC(context.ChunkPos, dcPointsPerAxis, chunkSize, voxelSize, voxelBuffer);

                    dcExtension.ReadbackAsync((dcVerts, dcQuads, quadCount) =>
                    {
                        // density + featureType 전달
                        context.DensityCache = densityData;
                        context.DensityDcN = dcPointsPerAxis;
                        context.DensityDcBasePos = bakedBasePos;
                        context.DensityVoxelSize = voxelSize;
                        // [Phase 2] featureType 추출 (null/빈 배열 안전 처리)
                        if (dcVerts != null && dcVerts.Length > 0)
                        {
                            var ftArr = new int[dcVerts.Length];
                            for (int fi = 0; fi < dcVerts.Length; fi++)
                                ftArr[fi] = dcVerts[fi].featureType;
                            context.FeatureTypes = ftArr;
                        }
                        else
                        {
                            context.FeatureTypes = null;
                        }

                        var meshBuilder = GetComponent<DCMeshBuilder>();
                        // [진단 개선] meshBuilder 미부착과 실제 빈 청크를 분리 로깅
                        if (meshBuilder == null)
                        {
                            Debug.LogError("[DC] DCMeshBuilder 컴포넌트 없음! CaveComputeDispatcher와 동일 GameObject에 부착 필요.");
                            context.State = ChunkState.Completed;
                            onGpuCompleted?.Invoke(context, null, null);
                            IsBusy = false;
                            return;
                        }

                        if (dcVerts != null && quadCount > 0)
                        {
                            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                            sw.Start();
                            meshBuilder.BuildMeshFromDCData(
                                dcVerts, dcQuads, quadCount,
                                context, chunkSize, voxelSize,
                                (completedCtx) =>
                                {
                                    sw.Stop();

                                    // // [v3] NormalBaker는 DCMeshBuilder에서 density와 함께 호출됨
                                    var profiler = GetComponent<DCPerformanceProfiler>();
                                    if (profiler != null)
                                    {
                                        profiler.RecordChunkResult(new DCProfileResult
                                        {
                                            chunkPos = context.ChunkPos,
                                            vertexCount = dcVerts.Length,
                                            triangleCount = quadCount * 2,
                                            quadCount = quadCount,
                                            totalTimeMs = (float)sw.Elapsed.TotalMilliseconds,
                                            gpuBufferBytes = DCPerformanceProfiler.CalculateGPUBufferMemory(dcPointsPerAxis)
                                        });
                                    }

                                    Debug.Log($"[DC] 청크 완성: {context.ChunkPos}, {quadCount} quads, {sw.ElapsedMilliseconds}ms");

                                    // [N-2] 캐시 MISS 후 완료 → 디스크에 저장
                                    if (_pendingCacheKey != null && _pendingDiskCache != null)
                                    {
                                        var meshForCache = completedCtx.ChunkObject?.GetComponent<MeshFilter>()?.sharedMesh;
                                        _pendingDiskCache.SaveAsync(_pendingCacheKey, meshForCache, completedCtx.FeatureTypes);
                                        _pendingCacheKey = null;
                                        _pendingDiskCache = null;
                                    }

                                    // [FIX-I] onGpuCompleted 호출 → completedChunks 증가
                                    onGpuCompleted?.Invoke(completedCtx, null, null);
                                    // [FIX-J] IsBusy 해제를 BuildMesh 완료 시점으로 이동
                                    IsBusy = false;
                                }
                            );
                        }
                        else
                        {
                            Debug.Log($"[DC] 빈 청크 (표면 없음): {context.ChunkPos}, quads={quadCount}");
                            context.State = ChunkState.Completed;
                            // [FIX-I] 빈 청크도 onGpuCompleted 호출
                            onGpuCompleted?.Invoke(context, null, null);
                            IsBusy = false;
                        }
                        // IsBusy=false 제거 ← FIX-J (BuildMesh 완료 전 해제 방지)
                    }); // end dcExtension.ReadbackAsync
                }); // end density AsyncGPUReadback.Request
                return;
            }
            // ═══ DC 분기 끝. 아래는 기존 MC 코드가 그대로 유지됨 ═══


            // ----------------------------------------------------
            // 커널 2: 마칭 큐브 메쉬 추출 (Marching Cubes)
            // ----------------------------------------------------
            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_VoxelBuffer", voxelBuffer);
            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_TriangleBuffer", triangleBuffer);
            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_OreBuffer", oreBuffer);

            // [🔥 추가: 룩업 테이블 버퍼 셰이더 주입]
            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_EdgeTable", mcEdgeTableBuffer);
            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_TriangleTable", mcTriangleTableBuffer);

            marchingCubesShader.SetVector("_ChunkBasePosition", chunkBasePos);
            marchingCubesShader.SetInt("_ChunkSize", chunkSize);
            marchingCubesShader.SetInt("_PointsPerAxis", pointsPerAxis);
            marchingCubesShader.SetFloat("_VoxelSize", voxelSize);
            marchingCubesShader.SetFloat("_IsoLevel", 0.0f);

            triangleBuffer.SetCounterValue(0);
            oreBuffer.SetCounterValue(0);

            // 마칭 큐브 스레드는 삼각형을 만드는 기준이므로 ChunkSize 기준으로 할당
            int mcThreadGroups = Mathf.CeilToInt(chunkSize / 8.0f);
            marchingCubesShader.Dispatch(kernelGenerateMesh, mcThreadGroups, mcThreadGroups, mcThreadGroups);

            onGpuCompleted?.Invoke(context, triangleBuffer, oreBuffer);
        }

        public void EnqueueChunk(ChunkRequestContext context)
        {
            int size = 16;
            float vSize = 1.0f;
            DispatchChunk(context, size, vSize, null);
        }

        private void AllocateTempBuffers(int pointsPerAxis, int chunkSize)
        {
            AllocateTempBuffers(pointsPerAxis, chunkSize, isDCMode: false);
        }

        // [triangleBuffer 최적화] DC 모드에서는 voxelBuffer만 할당
        // isDCMode=true → triangleBuffer/oreBuffer 스킵 (청크당 14.1MB 절약)
        private void AllocateTempBuffers(int pointsPerAxis, int chunkSize, bool isDCMode)
        {
            int requiredVoxelCount = pointsPerAxis * pointsPerAxis * pointsPerAxis;
            int maxCubeCount = chunkSize * chunkSize * chunkSize;

            if (voxelBuffer == null || currentPointsPerAxis != pointsPerAxis)
            {
                ReleaseTempBuffers();
                currentPointsPerAxis = pointsPerAxis;

                voxelBuffer = new ComputeBuffer(requiredVoxelCount, Marshal.SizeOf(typeof(CaveVoxel)));

                if (!isDCMode)
                {
                    // MC 전용 버퍼: DC 모드에서는 생략 (14.1MB 절약)
                    triangleBuffer = new ComputeBuffer(maxCubeCount * 5, Marshal.SizeOf(typeof(CaveTriangle)), ComputeBufferType.Append);
                    oreBuffer = new ComputeBuffer(maxCubeCount, Marshal.SizeOf(typeof(CaveOreData)), ComputeBufferType.Append);
                    triCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
                    oreCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
                }
            }
        }

        private void ReleaseGraphBuffers()
        {
            if (nodeBuffer != null) { nodeBuffer.Release(); nodeBuffer = null; }
            if (edgeBuffer != null) { edgeBuffer.Release(); edgeBuffer = null; }
        }

        private void ReleaseTempBuffers()
        {
            if (voxelBuffer != null) { voxelBuffer.Release(); voxelBuffer = null; }
            if (triangleBuffer != null) { triangleBuffer.Release(); triangleBuffer = null; }
            if (oreBuffer != null) { oreBuffer.Release(); oreBuffer = null; }
            if (triCountBuffer != null) { triCountBuffer.Release(); triCountBuffer = null; }
            if (oreCountBuffer != null) { oreCountBuffer.Release(); oreCountBuffer = null; }
            if (filteredNodeBuffer != null) { filteredNodeBuffer.Release(); filteredNodeBuffer = null; }
            if (filteredEdgeBuffer != null) { filteredEdgeBuffer.Release(); filteredEdgeBuffer = null; }
        }

        // ═══ [Rank 2] CPU 사전 필터링: 청크별 노드/엣지 AABB 교차 검사 ═══
        // 청크 AABB와 노드/엣지 영향 범위가 겹치는 것만 GPU에 전달
        // 효과: GPU 순회 ~70~90% 감소 (노드 50→5~15개, 엣지 80→10~25개)
        public void PreFilterForChunk(Vector3 chunkWorldMin, float chunkWorldSize, float margin)
        {
            if (CaveNodeGraphBuilder.Instance == null) return;
            var allNodes = CaveNodeGraphBuilder.Instance.nodesData;
            var allEdges = CaveNodeGraphBuilder.Instance.edgesData;
            Vector3 cMin = chunkWorldMin - Vector3.one * margin;
            Vector3 cMax = chunkWorldMin + Vector3.one * (chunkWorldSize + margin);

            var fNodes = new System.Collections.Generic.List<NodeData>();
            var fEdges = new System.Collections.Generic.List<EdgeData>();

            for (int i = 0; i < allNodes.Count; i++)
            {
                var n = allNodes[i];
                float r = n.radius + margin;
                if (n.position.x + r < cMin.x || n.position.x - r > cMax.x) continue;
                if (n.position.y + r < cMin.y || n.position.y - r > cMax.y) continue;
                if (n.position.z + r < cMin.z || n.position.z - r > cMax.z) continue;
                fNodes.Add(n);
            }
            for (int i = 0; i < allEdges.Count; i++)
            {
                var e = allEdges[i];
                Vector3 mid = (e.startPos + e.endPos) * 0.5f;
                float halfLen = Vector3.Distance(e.startPos, e.endPos) * 0.5f;
                float r = halfLen + e.width + margin;
                if (mid.x + r < cMin.x || mid.x - r > cMax.x) continue;
                if (mid.y + r < cMin.y || mid.y - r > cMax.y) continue;
                if (mid.z + r < cMin.z || mid.z - r > cMax.z) continue;
                fEdges.Add(e);
            }

            filteredNodeCount = fNodes.Count;
            filteredEdgeCount = fEdges.Count;

            // 최소 1개 보장 (빈 버퍼 방지)
            if (filteredNodeCount == 0) { fNodes.Add(new NodeData()); filteredNodeCount = 0; }
            if (filteredEdgeCount == 0) { fEdges.Add(new EdgeData()); filteredEdgeCount = 0; }

            if (filteredNodeBuffer != null) filteredNodeBuffer.Release();
            if (filteredEdgeBuffer != null) filteredEdgeBuffer.Release();
            filteredNodeBuffer = new ComputeBuffer(fNodes.Count, System.Runtime.InteropServices.Marshal.SizeOf<NodeData>());
            filteredEdgeBuffer = new ComputeBuffer(fEdges.Count, System.Runtime.InteropServices.Marshal.SizeOf<EdgeData>());
            filteredNodeBuffer.SetData(fNodes);
            filteredEdgeBuffer.SetData(fEdges);
        }

        // 사전 필터된 버퍼를 density shader에 바인딩
        public void BindFilteredBuffers(int kernel)
        {
            densityShader.SetBuffer(kernel, "_NodeBuffer", filteredNodeBuffer);
            densityShader.SetInt("_NodeCount", filteredNodeCount);
            densityShader.SetBuffer(kernel, "_EdgeBuffer", filteredEdgeBuffer);
            densityShader.SetInt("_EdgeCount", filteredEdgeCount);
        }

        // =====================================================================
        // [N-2] DiskCache — pending cache save (completion callback에서 참조)
        // =====================================================================
        private string _pendingCacheKey = null;
        private CaveDiskCache _pendingDiskCache = null;

        private void OnDestroy()
        {
            ReleaseGraphBuffers();
            ReleaseTempBuffers();
            if (biomeBuffer != null) { biomeBuffer.Release(); biomeBuffer = null; }

            // [🔥 추가: 룩업 테이블 버퍼 해제]
            if (mcEdgeTableBuffer != null) { mcEdgeTableBuffer.Release(); mcEdgeTableBuffer = null; }
            if (mcTriangleTableBuffer != null) { mcTriangleTableBuffer.Release(); mcTriangleTableBuffer = null; }
        }
    }
}