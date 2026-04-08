// =============================================================================
// CaveTerrainConfig.cs  |  pB-4 Project
// =============================================================================
// VoxelSize, ChunkSize, PointsPerAxis 등 지형 해상도 파라미터를
// 프로젝트 전체가 공유하는 단일 ScriptableObject.
//
// 사용법:
//   [SerializeField] CaveTerrainConfig terrainConfig;
//   int N = terrainConfig.DCPointsPerAxis;
//   float ws = terrainConfig.ChunkWorldSize;
//
// CaveChunkManager, DCPipelineExtension, CaveMeshJobManager,
// TerrainCacheManager 모두 이 SO를 참조.
// =============================================================================
using UnityEngine;

namespace CaveSystem
{
    [CreateAssetMenu(menuName = "CaveSystem/Terrain Config", fileName = "CaveTerrainConfig")]
    public class CaveTerrainConfig : ScriptableObject
    {
        // ── 핵심 파라미터 ──────────────────────────────────────────────────
        [Header("World Size")]
        [Tooltip("청크 하나의 목표 월드 크기 (m). VoxelSize에 따라 ChunkSize가 자동 계산됨.")]
        [Min(4f)] public float targetWorldChunkSize = 16f;

        [Tooltip("복셀 하나의 월드 크기 (m). 작을수록 디테일 향상, 메모리·연산 비용 증가.")]
        [Min(0.1f)] public float voxelSize = 0.5f;

        // ── 자동 계산 프로퍼티 ─────────────────────────────────────────────
        /// <summary>복셀 그리드 크기 = targetWorldChunkSize / voxelSize (정수 반올림)</summary>
        public int ChunkSize => Mathf.Max(4, Mathf.RoundToInt(targetWorldChunkSize / voxelSize));

        /// <summary>실제 청크 월드 크기 = ChunkSize * voxelSize</summary>
        public float ChunkWorldSize => ChunkSize * voxelSize;

        /// <summary>MC 포인트 수 = ChunkSize + 2 (Ghost Voxel 패딩)</summary>
        public int MCPointsPerAxis => ChunkSize + 2;

        /// <summary>DC 포인트 수 = ChunkSize + 3 (Seamless Overlap 패딩)</summary>
        public int DCPointsPerAxis => ChunkSize + 3;

        // ── 렌더링 거리 ────────────────────────────────────────────────────
        [Header("Render Distance")]
        [Tooltip("플레이어 주변 청크 생성 반경 (청크 단위).")]
        [Min(1)] public int viewDistance = 2;

        [Tooltip("Y축 청크 생성 반경 (청크 단위).")]
        [Min(1)] public int viewDistanceY = 1;

        /// <summary>총 청크 수 = (2*viewDistance+1)^2 * (2*viewDistanceY+1)</summary>
        public int TotalChunkCount =>
            (2 * viewDistance + 1) * (2 * viewDistance + 1) * (2 * viewDistanceY + 1);

        // ── GPU 버퍼 예측 (DC 모드) ────────────────────────────────────────
        [Header("Buffer Preview (Read-Only)")]
        [Tooltip("DC 모드 기준 청크 1개 예상 GPU 메모리 (MB).")]
        public float EstimatedDCBufferMB => CalcDCBufferMB();

        private float CalcDCBufferMB()
        {
            int N = DCPointsPerAxis;
            long N3 = (long)N * N * N;
            long voxel = N3 * 16;
            long hermite = N3 * 3 * 16; // [O3] DCHermiteEdge 압축: 32B→16B
            long dcVert = N3 * 32; // [O1] DCVertex 압축: 56B→32B
            long dcQuad = N3 * 16;
            return (voxel + hermite + dcVert + dcQuad) / (1024f * 1024f);
        }

        // ── OnValidate ────────────────────────────────────────────────────
        private void OnValidate()
        {
            // voxelSize가 targetWorldChunkSize보다 크면 ChunkSize < 1 방지
            if (voxelSize > targetWorldChunkSize)
            {
                Debug.LogWarning($"[CaveTerrainConfig] voxelSize({voxelSize}) > targetWorldChunkSize({targetWorldChunkSize}). 자동 보정.");
                voxelSize = targetWorldChunkSize * 0.5f;
            }

            // ChunkSize가 너무 크면 GPU 메모리 경고
            int cs = ChunkSize;
            if (cs > 64)
                Debug.LogWarning($"[CaveTerrainConfig] ChunkSize={cs}는 DC 모드에서 GPU 메모리 부담이 큽니다. targetWorldChunkSize를 줄이거나 voxelSize를 늘리세요.");

            float mem = EstimatedDCBufferMB;
            if (mem > 50f)
                Debug.LogError($"[CaveTerrainConfig] 청크당 예상 DC 버퍼 {mem:F1}MB > 50MB 마일스톤 초과!");
            else
                Debug.Log($"[CaveTerrainConfig] ChunkSize={cs}, VoxelSize={voxelSize}, WorldSize={ChunkWorldSize}m, DC버퍼≈{mem:F1}MB/청크");
        }

#if UNITY_EDITOR
        // ── Editor 헬퍼 ───────────────────────────────────────────────────
        [ContextMenu("메모리 예측 출력")]
        private void PrintMemoryEstimate()
        {
            int N = DCPointsPerAxis;
            Debug.Log(
                $"[CaveTerrainConfig] ── 메모리 예측 ──\n" +
                $"  ChunkSize={ChunkSize}  VoxelSize={voxelSize}m  WorldSize={ChunkWorldSize}m\n" +
                $"  DC PointsPerAxis={N}  N³={N * N * N}\n" +
                $"  voxelBuffer:      {(long)N * N * N * 16 / 1024}KB\n" +
                $"  hermiteEdgeBuffer:{(long)N * N * N * 3 * 16 / 1024}KB (O3압축)\n" +
                $"  dcVertexBuffer:   {(long)N * N * N * 32 / 1024}KB (O1압축)\n" +
                $"  dcQuadBuffer:     {(long)N * N * N * 16 / 1024}KB\n" +
                $"  합계:              {EstimatedDCBufferMB:F1}MB/청크\n" +
                $"  총 청크 {TotalChunkCount}개 × {EstimatedDCBufferMB:F1}MB = {TotalChunkCount * EstimatedDCBufferMB:F0}MB\n" +
                $"  (단, 디스패처 버퍼는 청크 수와 무관하게 1세트 공유)"
            );
        }
#endif
    }
}