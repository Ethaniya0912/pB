using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System;
using Unity.Profiling;        // Unity 프로파일러 연동용
using System.Diagnostics;     // 정밀 시간 측정용 (Stopwatch)

namespace CaveSystem
{
    /// <summary>
    /// [Phase 3 & 4] CPU의 연산 데이터(그래프 설계도)를 GPU로 전송하고, 
    /// 컴퓨트 셰이더의 멀티 커널 실행 및 비동기 회수(AsyncReadback)를 총괄하는 고급 디스패처입니다.
    /// 프로덕션 레벨의 메모리 추적 및 기즈모 디버깅 기능이 포함되어 있습니다.
    /// </summary>
    public class CaveComputeDispatcher : MonoBehaviour
    {
        [Header("Compute Shaders")]
        public ComputeShader densityShader;
        public ComputeShader marchingCubesShader;
        public CaveBiomeSettings caveSettings;

        [Header("Pro Debugging & Profiling")]
        [Tooltip("상세한 디스패치 시간, VRAM 할당량, 커널 스레드 상태를 콘솔에 출력합니다.")]
        public bool showVerboseLogs = true;
        [Tooltip("최근 디스패치된 청크들의 연산 범위를 씬 뷰에 기즈모로 추적하여 표시합니다.")]
        public bool drawChunkHistoryGizmos = true;
        [Range(1, 20)]
        [Tooltip("씬 뷰에 유지할 기즈모 히스토리 개수입니다.")]
        public int maxGizmoHistory = 5;

        // --- GPU 통신용 버퍼 ---
        private ComputeBuffer nodeBuffer;
        private ComputeBuffer edgeBuffer;

        // 지형 연산용 공통 버퍼
        private ComputeBuffer voxelBuffer;
        private ComputeBuffer triangleBuffer;
        private ComputeBuffer oreBuffer;

        // 간접 그리기 및 카운트용 버퍼
        private ComputeBuffer triCountBuffer;
        private ComputeBuffer oreCountBuffer;

        // 마칭 큐브 룩업 테이블 버퍼
        private ComputeBuffer edgeTableBuffer;
        private ComputeBuffer triTableBuffer;

        private int allocatedPointsPerAxis = 0;

        // 커널 캐싱
        private int kernelGenerateDensity;
        private int kernelSimulateErosion;
        private int kernelGenerateMesh;

        // --- 프로파일링 및 디버깅 데이터 ---
        static readonly ProfilerMarker s_SetupBuffersMarker = new ProfilerMarker("CaveCompute.SetupGraphBuffers");
        static readonly ProfilerMarker s_DispatchMarker = new ProfilerMarker("CaveCompute.DispatchChunk");
        static readonly ProfilerMarker s_AllocationMarker = new ProfilerMarker("CaveCompute.AllocateVRAM");

        // 기즈모 시각화를 위한 히스토리 큐
        private Queue<ChunkDebugInfo> debugChunkHistory = new Queue<ChunkDebugInfo>();

        private struct ChunkDebugInfo
        {
            public Vector3 BasePosition;
            public int ChunkSize;
            public float VoxelSize;
            public Color DisplayColor;
            public float Timestamp;
        }

        private void Awake()
        {
            ValidateReferences();
            InitializeKernels();
            InitializeLookupTables();
        }

        /// <summary>
        /// [Pro Debug] 셰이더 및 설정 파일 누락 여부를 사전에 철저히 검증합니다.
        /// </summary>
        private void ValidateReferences()
        {
            if (densityShader == null)
                UnityEngine.Debug.LogError("[CaveComputeDispatcher] 🚨 치명적 오류: Density Shader가 할당되지 않았습니다! 인스펙터를 확인하세요.");
            if (marchingCubesShader == null)
                UnityEngine.Debug.LogError("[CaveComputeDispatcher] 🚨 치명적 오류: Marching Cubes Shader가 할당되지 않았습니다! 인스펙터를 확인하세요.");
            if (caveSettings == null)
                UnityEngine.Debug.LogError("[CaveComputeDispatcher] 🚨 치명적 오류: CaveBiomeSettings가 할당되지 않았습니다! 셋팅 에셋을 연결하세요.");
        }

        /// <summary>
        /// 커널 ID를 사전에 캐싱하여 런타임 성능을 극대화합니다.
        /// </summary>
        private void InitializeKernels()
        {
            if (densityShader != null)
            {
                kernelGenerateDensity = densityShader.FindKernel("GenerateDensity");
                kernelSimulateErosion = densityShader.FindKernel("SimulateErosion");
            }
            if (marchingCubesShader != null)
            {
                kernelGenerateMesh = marchingCubesShader.FindKernel("GenerateMesh");
            }
        }

        /// <summary>
        /// 마칭 큐브를 위한 Edge/Triangle 룩업 테이블을 GPU VRAM에 업로드합니다.
        /// </summary>
        private void InitializeLookupTables()
        {
            Stopwatch sw = null;
            if (showVerboseLogs) sw = Stopwatch.StartNew();

            edgeTableBuffer = new ComputeBuffer(256, sizeof(int));
            edgeTableBuffer.SetData(MarchingCubesTables.EdgeTable);

            triTableBuffer = new ComputeBuffer(4096, sizeof(int));
            triTableBuffer.SetData(MarchingCubesTables.TriangleTable);

            if (showVerboseLogs)
            {
                sw.Stop();
                UnityEngine.Debug.Log($"<color=#00FFFF>[ComputeDispatcher]</color> LUT 버퍼 VRAM 업로드 완료. (소요시간: {sw.Elapsed.TotalMilliseconds:F3}ms)");
            }
        }

        /// <summary>
        /// Phase 2에서 완성된 글로벌 노드 그래프 데이터를 GPU 버퍼로 패킹합니다.
        /// 빈 배열이 들어오는 예외 상황을 철저히 방어합니다.
        /// </summary>
        public void SetupGraphBuffers(List<NodeData> nodes, List<EdgeData> edges)
        {
            s_SetupBuffersMarker.Begin();
            Stopwatch sw = null;
            if (showVerboseLogs) sw = Stopwatch.StartNew();

            ReleaseGraphBuffers();

            if (nodes == null || nodes.Count == 0)
                UnityEngine.Debug.LogWarning("[CaveComputeDispatcher] ⚠️ 수신된 노드(방) 데이터가 0개입니다. 뼈대 없는 빈 암석 덩어리가 생성됩니다.");

            int nodeStride = Marshal.SizeOf(typeof(NodeData));
            int edgeStride = Marshal.SizeOf(typeof(EdgeData));

            // GPU 에러 방지를 위해 최소 1개의 크기는 보장하여 버퍼를 생성합니다.
            int nodeCount = Mathf.Max(1, nodes?.Count ?? 0);
            nodeBuffer = new ComputeBuffer(nodeCount, nodeStride);
            if (nodes != null && nodes.Count > 0) nodeBuffer.SetData(nodes);

            int edgeCount = Mathf.Max(1, edges?.Count ?? 0);
            edgeBuffer = new ComputeBuffer(edgeCount, edgeStride);
            if (edges != null && edges.Count > 0) edgeBuffer.SetData(edges);

            if (showVerboseLogs)
            {
                sw.Stop();
                long vramBytes = (nodeCount * nodeStride) + (edgeCount * edgeStride);
                UnityEngine.Debug.Log($"<color=#00FFFF>[ComputeDispatcher]</color> 그래프 데이터 GPU 버퍼 패킹 완료. (노드: {nodes?.Count}개, 엣지: {edges?.Count}개, 사용량: {vramBytes / 1024f:F2} KB) - 소요시간: {sw.Elapsed.TotalMilliseconds:F3}ms");
            }
            s_SetupBuffersMarker.End();
        }

        /// <summary>
        /// 단일 청크에 대한 밀도 생성, 침식 시뮬레이션, 마칭 큐브 연산을 연속 실행합니다.
        /// </summary>
        public void DispatchChunk(ChunkRequestContext context, int chunkSize, float voxelSize, Action<ChunkRequestContext, ComputeBuffer, ComputeBuffer> onGpuCompleted)
        {
            s_DispatchMarker.Begin();
            Stopwatch sw = null;

            if (showVerboseLogs)
            {
                sw = Stopwatch.StartNew();
                UnityEngine.Debug.Log($"<color=#F39C12>[ComputeDispatcher]</color> 청크 {context.ChunkPos} 파이프라인 개시 (ChunkSize: {chunkSize}, VoxelSize: {voxelSize}m)");
            }

            if (nodeBuffer == null || edgeBuffer == null)
            {
                UnityEngine.Debug.LogError("[CaveComputeDispatcher] 🚨 그래프 버퍼가 초기화되지 않았습니다. SetupGraphBuffers를 먼저 호출하세요.");
                s_DispatchMarker.End();
                return;
            }

            // [메모리 어긋남 방지 핵심] 큐브의 꼭짓점을 구하려면 점(Point)은 ChunkSize + 1 개가 필요합니다.
            int pointsPerAxis = chunkSize + 1;
            AllocateTempBuffers(pointsPerAxis, chunkSize);

            Vector3 chunkBasePos = new Vector3(context.ChunkPos.x, context.ChunkPos.y, context.ChunkPos.z) * (chunkSize * voxelSize);
            DepthLayer currentLayer = caveSettings.GetLayerSettings(chunkBasePos.y);

            // 디버그 히스토리 큐 업데이트
            UpdateDebugHistory(chunkBasePos, chunkSize, voxelSize);

            // ----------------------------------------------------
            // 커널 1: 밀도장 연산 (Density Field Generation)
            // ----------------------------------------------------
            densityShader.SetBuffer(kernelGenerateDensity, "_VoxelBuffer", voxelBuffer);
            densityShader.SetBuffer(kernelGenerateDensity, "_NodeBuffer", nodeBuffer);
            densityShader.SetInt("_NodeCount", CaveNodeGraphBuilder.Instance.nodesData.Count);
            densityShader.SetBuffer(kernelGenerateDensity, "_EdgeBuffer", edgeBuffer);
            densityShader.SetInt("_EdgeCount", CaveNodeGraphBuilder.Instance.edgesData.Count);

            densityShader.SetVector("_ChunkBasePosition", chunkBasePos);
            densityShader.SetInt("_PointsPerAxis", pointsPerAxis);
            densityShader.SetFloat("_VoxelSize", voxelSize);
            densityShader.SetInt("_DebugStage", (int)caveSettings.debugStage);

            // 바이옴 상세 파라미터 주입
            densityShader.SetFloat("_SdfSmoothness", currentLayer.sdfSmoothness);
            densityShader.SetFloat("_FloorAltitude", currentLayer.minAltitude);
            densityShader.SetFloat("_CeilAltitude", currentLayer.maxAltitude);
            densityShader.SetFloat("_FloorBlendRadius", currentLayer.floorBlendRadius);
            densityShader.SetFloat("_CeilBlendRadius", currentLayer.ceilBlendRadius);
            densityShader.SetFloat("_NoiseFreq", currentLayer.noiseFrequency);
            densityShader.SetFloat("_FloorBumpAmp", currentLayer.floorBumpAmplitude);
            densityShader.SetFloat("_FloorBumpFreq", currentLayer.floorBumpFrequency);
            densityShader.SetFloat("_SinkholeProb", currentLayer.sinkholeProbability);
            densityShader.SetFloat("_SinkholeSmooth", currentLayer.sinkholeSmoothness);
            densityShader.SetFloat("_LedgeStepHeight", currentLayer.ledgeStepHeight);
            densityShader.SetFloat("_SpiralFrequency", currentLayer.spiralFrequency);
            densityShader.SetFloat("_SpiralAmplitude", currentLayer.spiralAmplitude);

            // 점(Point)의 개수를 기준으로 스레드 그룹 파견 (예: 65 / 8 = 8.125 -> 9개 그룹)
            int densityThreadGroups = Mathf.CeilToInt(pointsPerAxis / 8.0f);
            if (showVerboseLogs) UnityEngine.Debug.Log($"  ▶ [Kernel 1] 밀도장 디스패치 (Threads: {densityThreadGroups}^3)");
            densityShader.Dispatch(kernelGenerateDensity, densityThreadGroups, densityThreadGroups, densityThreadGroups);

            // ----------------------------------------------------
            // 커널 2: 물리 수력 침식 시뮬레이션 (Hydraulic Erosion)
            // ----------------------------------------------------
            if (caveSettings.debugStage == GenerationDebugStage.ErosionSimulated)
            {
                densityShader.SetBuffer(kernelSimulateErosion, "_VoxelBuffer", voxelBuffer);
                densityShader.SetInt("_PointsPerAxis", pointsPerAxis);

                // 임의로 청크의 면적에 비례하여 물방울(Droplet) 개수 설정
                int dropletCount = chunkSize * chunkSize * 2;
                int erosionThreadGroups = Mathf.CeilToInt(dropletCount / 64.0f);

                if (showVerboseLogs) UnityEngine.Debug.Log($"  ▶ [Kernel 2] 침식 시뮬레이션 디스패치 (Droplets: {dropletCount}, Threads: {erosionThreadGroups})");
                densityShader.Dispatch(kernelSimulateErosion, erosionThreadGroups, 1, 1);
            }

            // ----------------------------------------------------
            // 커널 3: 마칭 큐브 메쉬 추출 (Marching Cubes)
            // ----------------------------------------------------
            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_VoxelBuffer", voxelBuffer);
            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_TriangleBuffer", triangleBuffer);
            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_OreBuffer", oreBuffer);

            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_EdgeTable", edgeTableBuffer);
            marchingCubesShader.SetBuffer(kernelGenerateMesh, "_TriangleTable", triTableBuffer);

            marchingCubesShader.SetVector("_ChunkBasePosition", chunkBasePos);
            marchingCubesShader.SetInt("_PointsPerAxis", pointsPerAxis); // 밀도 배열 인덱싱을 위한 점의 개수
            marchingCubesShader.SetInt("_ChunkSize", chunkSize);         // 실제 그려질 큐브의 개수
            marchingCubesShader.SetFloat("_VoxelSize", voxelSize);
            marchingCubesShader.SetFloat("_IsoLevel", 0.0f);

            // AppendBuffer의 카운터를 0으로 초기화
            triangleBuffer.SetCounterValue(0);
            oreBuffer.SetCounterValue(0);

            // 큐브(Cube)의 개수를 기준으로 스레드 그룹 파견
            int meshThreadGroups = Mathf.CeilToInt(chunkSize / 8.0f);
            if (showVerboseLogs) UnityEngine.Debug.Log($"  ▶ [Kernel 3] 마칭 큐브 디스패치 (Threads: {meshThreadGroups}^3)");
            marchingCubesShader.Dispatch(kernelGenerateMesh, meshThreadGroups, meshThreadGroups, meshThreadGroups);

            if (showVerboseLogs)
            {
                sw.Stop();
                UnityEngine.Debug.Log($"<color=#27AE60>[ComputeDispatcher]</color> 청크 {context.ChunkPos} 파이프라인 큐잉 완료! (CPU 할당 시간: {sw.Elapsed.TotalMilliseconds:F3}ms)");
            }

            // 완료 콜백 (이후 CaveMeshJobManager에서 비동기 Readback 수행)
            onGpuCompleted?.Invoke(context, triangleBuffer, oreBuffer);
            s_DispatchMarker.End();
        }

        /// <summary>
        /// 안전한 메모리 할당 및 VRAM 사용량 로깅
        /// </summary>
        private void AllocateTempBuffers(int pointsPerAxis, int chunkSize)
        {
            s_AllocationMarker.Begin();
            if (voxelBuffer == null || allocatedPointsPerAxis != pointsPerAxis)
            {
                ReleaseTempBuffers();
                allocatedPointsPerAxis = pointsPerAxis;

                int voxelCount = pointsPerAxis * pointsPerAxis * pointsPerAxis;
                int cubeCount = chunkSize * chunkSize * chunkSize;

                // 1. CaveVoxel (8바이트)
                int voxelStride = Marshal.SizeOf(typeof(CaveVoxel));
                // 2. CaveTriangle (96바이트) - 한 큐브당 최대 5개 트라이앵글 생성 가능
                int triStride = Marshal.SizeOf(typeof(CaveTriangle));
                // 3. CaveOreData (32바이트)
                int oreStride = Marshal.SizeOf(typeof(CaveOreData));

                voxelBuffer = new ComputeBuffer(voxelCount, voxelStride);
                triangleBuffer = new ComputeBuffer(cubeCount * 5, triStride, ComputeBufferType.Append);
                oreBuffer = new ComputeBuffer(cubeCount, oreStride, ComputeBufferType.Append);

                triCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
                oreCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);

                if (showVerboseLogs)
                {
                    long totalBytes = (voxelCount * voxelStride) + (cubeCount * 5 * triStride) + (cubeCount * oreStride);
                    float totalMB = totalBytes / (1024f * 1024f);
                    UnityEngine.Debug.Log($"<color=#8E44AD>[VRAM Allocation]</color> 런타임 버퍼 재할당 발생. (용량: {totalMB:F2} MB, VoxelCount: {voxelCount})");
                }
            }
            s_AllocationMarker.End();
        }

        private void UpdateDebugHistory(Vector3 basePos, int size, float vSize)
        {
            if (!drawChunkHistoryGizmos) return;

            if (debugChunkHistory.Count >= maxGizmoHistory)
            {
                debugChunkHistory.Dequeue();
            }

            // 새로 디스패치되는 청크는 밝은 노란색으로 등록 (시간이 지날수록 흐려짐 처리를 위해 Timestamp 기록)
            debugChunkHistory.Enqueue(new ChunkDebugInfo
            {
                BasePosition = basePos,
                ChunkSize = size,
                VoxelSize = vSize,
                DisplayColor = new Color(1f, 0.8f, 0.2f, 0.8f),
                Timestamp = Time.time
            });
        }

        /// <summary>
        /// [Pro Debug] 씬 뷰에 최근 디스패치된 청크들의 AABB(바운딩 박스) 영역을 표시합니다.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (!drawChunkHistoryGizmos || debugChunkHistory.Count == 0) return;

            foreach (var info in debugChunkHistory)
            {
                float worldChunkSize = info.ChunkSize * info.VoxelSize;
                Vector3 centerOffset = new Vector3(worldChunkSize, worldChunkSize, worldChunkSize) * 0.5f;
                Vector3 chunkCenter = info.BasePosition + centerOffset;

                // 생성 후 지난 시간에 따라 색상 페이딩 (방금 생성된 것은 노랗고, 오래된 것은 파랗게)
                float age = Time.time - info.Timestamp;
                Color finalColor = Color.Lerp(info.DisplayColor, new Color(0.1f, 0.5f, 1f, 0.2f), Mathf.Clamp01(age / 5.0f));

                // 큐브 영역 기즈모
                Gizmos.color = finalColor;
                Gizmos.DrawWireCube(chunkCenter, Vector3.one * worldChunkSize);

                // 안전 마진(PointsPerAxis)을 포함한 실제 메모리 할당 영역
                float pointWorldSize = (info.ChunkSize + 1) * info.VoxelSize;
                Gizmos.color = new Color(finalColor.r, finalColor.g, finalColor.b, 0.1f);
                Gizmos.DrawCube(info.BasePosition + (Vector3.one * pointWorldSize * 0.5f), Vector3.one * pointWorldSize);
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
            if (edgeTableBuffer != null) { edgeTableBuffer.Release(); edgeTableBuffer = null; }
            if (triTableBuffer != null) { triTableBuffer.Release(); triTableBuffer = null; }
        }
    }
}