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
                // DC 3커널 실행: Hermite Data 수집 → QEF 풀기 → 쿼드 생성
                dcExtension.DispatchDC(context.ChunkPos, pointsPerAxis, chunkSize, voxelBuffer);

                // DC 비동기 읽기 → 기존 onGpuCompleted 콜백 대신 DC 전용 처리
                dcExtension.ReadbackAsync((dcVerts, dcQuads, quadCount) =>
                {
                    // ─── Stage 4: DC 쿼드 → Unity Mesh 빌드 ───
                    var meshBuilder = GetComponent<DCMeshBuilder>();
                    if (meshBuilder != null && dcVerts != null && quadCount > 0)
                    {
                        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                        sw.Start();

                        meshBuilder.BuildMeshFromDCData(
                            dcVerts, dcQuads, quadCount,
                            context, chunkSize, voxelSize,
                            (completedCtx) =>
                            {
                                sw.Stop();

                                // ─── Stage 5: Normal Map 베이킹 ───
                                var normalBaker = GetComponent<CaveNormalBaker>();
                                if (normalBaker != null && completedCtx.ChunkObject != null)
                                {
                                    var filter = completedCtx.ChunkObject.GetComponent<UnityEngine.MeshFilter>();
                                    var renderer = completedCtx.ChunkObject.GetComponent<UnityEngine.MeshRenderer>();
                                    if (filter != null && filter.sharedMesh != null)
                                    {
                                        normalBaker.BakeNormalMap(filter.sharedMesh, renderer);
                                    }
                                }

                                // ─── Stage 6: 성능 프로파일링 ───
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
                                        gpuBufferBytes = DCPerformanceProfiler.CalculateGPUBufferMemory(pointsPerAxis)
                                    });
                                }

                                Debug.Log($"[DC] 청크 완성: {context.ChunkPos}, {quadCount} quads, {sw.ElapsedMilliseconds}ms");
                            }
                        );
                    }
                    else
                    {
                        Debug.Log($"[DC] 청크 비어있음: {context.ChunkPos}, quads={quadCount}");
                        context.State = ChunkState.Completed;
                    }

                    IsBusy = false;
                });
                return; // ← MC 커널 2를 완전히 스킵하고 여기서 종료
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
            int requiredVoxelCount = pointsPerAxis * pointsPerAxis * pointsPerAxis;
            int maxCubeCount = chunkSize * chunkSize * chunkSize;

            if (voxelBuffer == null || currentPointsPerAxis != pointsPerAxis)
            {
                ReleaseTempBuffers();
                currentPointsPerAxis = pointsPerAxis;

                voxelBuffer = new ComputeBuffer(requiredVoxelCount, Marshal.SizeOf(typeof(CaveVoxel)));
                triangleBuffer = new ComputeBuffer(maxCubeCount * 5, Marshal.SizeOf(typeof(CaveTriangle)), ComputeBufferType.Append);
                oreBuffer = new ComputeBuffer(maxCubeCount, Marshal.SizeOf(typeof(CaveOreData)), ComputeBufferType.Append);
                triCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
                oreCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
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
        }

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