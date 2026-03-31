// =============================================================================
// DCPerformanceProfiler.cs  |  pB-4 Project — Week 0, Stage 6
// Layer  : Editor / Runtime Debug
// Owner  : Person B
//
// 역할:
//   DC 파이프라인 전체의 성능 프로파일링.
//   - 청크당 버텍스 수, 삼각형 수, GPU 버퍼 메모리
//   - NavMesh 재생성 소요 시간
//   - DC 3커널 각각의 GPU 시간 (추정)
//   - 기존 MC 파이프라인과의 비교 데이터
//
//   Week 0 마일스톤: "청크당 GPU 버퍼 메모리 50MB 이하, NavMesh 재생성 200ms 이하"
//
// 사용법:
//   CaveManager와 동일한 GameObject에 부착.
//   Play 모드에서 Console에 자동으로 프로파일링 결과 출력.
//   커스텀 에디터 v0.6의 SDF_LayerDebugger와 연동 (Week 6).
// =============================================================================
using UnityEngine;
using System.Collections.Generic;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace CaveSystem
{
    /// <summary>
    /// 단일 청크의 DC 파이프라인 프로파일링 결과를 저장하는 구조체.
    /// </summary>
    [System.Serializable]
    public struct DCProfileResult
    {
        public Vector3Int chunkPos;
        public int vertexCount;
        public int triangleCount;
        public int quadCount;
        public float totalTimeMs;
        public float hermiteTimeMs;   // Stage 2
        public float qefTimeMs;       // Stage 3
        public float quadGenTimeMs;   // Stage 4 GPU
        public float meshBuildTimeMs; // Stage 4 CPU
        public float normalBakeTimeMs;// Stage 5
        public long gpuBufferBytes;
    }

    public class DCPerformanceProfiler : MonoBehaviour
    {
        [Header("Profiling Settings")]
        [Tooltip("프로파일링 활성화 여부")]
        public bool enableProfiling = true;

        [Tooltip("최근 N개 청크의 프로파일링 결과를 보관")]
        [Range(10, 100)]
        public int historySize = 30;

        [Header("Performance Targets (Week 0 마일스톤)")]
        [Tooltip("청크당 GPU 버퍼 메모리 상한 (바이트)")]
        public long maxGpuBufferBytes = 50L * 1024 * 1024; // 50MB

        [Tooltip("NavMesh 재생성 소요 시간 상한 (밀리초)")]
        public float maxNavMeshRebuildMs = 200f;

        // 프로파일링 결과 히스토리
        [Header("Debug — Results (Read Only)")]
        [SerializeField] private List<DCProfileResult> history = new List<DCProfileResult>();
        [SerializeField] private float avgVertexCount;
        [SerializeField] private float avgTriangleCount;
        [SerializeField] private float avgTotalTimeMs;
        [SerializeField] private long totalGpuBufferBytes;
        [SerializeField] private float lastNavMeshRebuildMs;

        // NavMesh 측정용
        private Stopwatch navMeshStopwatch = new Stopwatch();

        // ================================================================
        // 공개 API: DCMeshBuilder / CaveComputeDispatcher에서 호출
        // ================================================================

        /// <summary>
        /// DC 청크 생성 완료 후 프로파일링 결과를 기록한다.
        /// DCMeshBuilder.BuildMeshFromDCData() 완료 시 호출.
        /// </summary>
        public void RecordChunkResult(DCProfileResult result)
        {
            if (!enableProfiling) return;

            history.Add(result);
            if (history.Count > historySize)
                history.RemoveAt(0);

            UpdateAverages();

            // 마일스톤 검증 로그
            string memStatus = result.gpuBufferBytes <= maxGpuBufferBytes ? "✅" : "⚠️ 초과!";
            Debug.Log($"[DCProfiler] 청크 {result.chunkPos}: " +
                      $"V={result.vertexCount}, T={result.triangleCount}, Q={result.quadCount}, " +
                      $"시간={result.totalTimeMs:F1}ms, GPU메모리={result.gpuBufferBytes / 1024}KB {memStatus}");
        }

        /// <summary>
        /// NavMesh 갱신 시작 시 호출.
        /// CaveManager.UpdateNavMeshRoutine()에서 BuildNavMesh() 직전에 호출.
        /// </summary>
        public void StartNavMeshTimer()
        {
            if (!enableProfiling) return;
            navMeshStopwatch.Restart();
        }

        /// <summary>
        /// NavMesh 갱신 완료 시 호출.
        /// CaveManager.UpdateNavMeshRoutine()에서 BuildNavMesh() 직후에 호출.
        /// </summary>
        public void StopNavMeshTimer()
        {
            if (!enableProfiling) return;
            navMeshStopwatch.Stop();
            lastNavMeshRebuildMs = (float)navMeshStopwatch.Elapsed.TotalMilliseconds;

            string status = lastNavMeshRebuildMs <= maxNavMeshRebuildMs ? "✅" : "⚠️ 초과!";
            Debug.Log($"[DCProfiler] NavMesh 갱신: {lastNavMeshRebuildMs:F1}ms {status} (상한: {maxNavMeshRebuildMs}ms)");
        }

        /// <summary>
        /// DC 파이프라인의 GPU 버퍼 메모리 사용량을 계산한다.
        /// DCPipelineExtension에서 AllocateBuffers() 후 호출.
        /// </summary>
        public static long CalculateGPUBufferMemory(int pointsPerAxis)
        {
            int voxelCount = pointsPerAxis * pointsPerAxis * pointsPerAxis;
            int maxEdges = voxelCount * 3;
            int maxVertices = voxelCount;
            int maxQuads = maxEdges;

            long hermiteMem = maxEdges * 32L;      // DCHermiteEdge = 32B
            long vertexMem = maxVertices * 56L;     // DCVertex = 56B
            long quadMem = maxQuads * 16L;          // DCQuad = 16B
            long countMem = 4L;                     // quadCount = 4B
            long sculptMem = 64 * 16L;              // GameplaySculptData estimate

            return hermiteMem + vertexMem + quadMem + countMem + sculptMem;
        }

        // ================================================================
        // 내부 유틸리티
        // ================================================================
        private void UpdateAverages()
        {
            if (history.Count == 0) return;

            float totalVerts = 0, totalTris = 0, totalTime = 0;
            long totalMem = 0;

            foreach (var r in history)
            {
                totalVerts += r.vertexCount;
                totalTris += r.triangleCount;
                totalTime += r.totalTimeMs;
                totalMem += r.gpuBufferBytes;
            }

            avgVertexCount = totalVerts / history.Count;
            avgTriangleCount = totalTris / history.Count;
            avgTotalTimeMs = totalTime / history.Count;
            totalGpuBufferBytes = totalMem;
        }

        /// <summary>
        /// 전체 프로파일링 요약을 Console에 출력한다.
        /// </summary>
        public void PrintSummary()
        {
            if (history.Count == 0)
            {
                Debug.Log("[DCProfiler] 프로파일링 데이터가 없습니다.");
                return;
            }

            Debug.Log($"[DCProfiler] ═══ 성능 요약 ({history.Count}개 청크) ═══\n" +
                      $"  평균 버텍스: {avgVertexCount:F0}\n" +
                      $"  평균 삼각형: {avgTriangleCount:F0}\n" +
                      $"  평균 시간: {avgTotalTimeMs:F1}ms\n" +
                      $"  총 GPU 메모리: {totalGpuBufferBytes / (1024 * 1024)}MB\n" +
                      $"  NavMesh 갱신: {lastNavMeshRebuildMs:F1}ms\n" +
                      $"  ─── 마일스톤 ───\n" +
                      $"  GPU 메모리 50MB 이하: {(totalGpuBufferBytes <= maxGpuBufferBytes ? "✅ PASS" : "❌ FAIL")}\n" +
                      $"  NavMesh 200ms 이하: {(lastNavMeshRebuildMs <= maxNavMeshRebuildMs ? "✅ PASS" : "❌ FAIL")}");
        }

        // 에디터에서 수동 요약 출력
        [ContextMenu("Print Performance Summary")]
        private void PrintSummaryMenu() => PrintSummary();
    }
}
