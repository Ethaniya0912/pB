using UnityEngine;
using System.Collections.Generic;

namespace CaveSystem
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-G Stage 3-C, O2] SpatialHash
    //
    // Graph immutable phase에서 edge 검색 성능 최적화.
    //
    // Pregen 1회:
    //   1. Map 전체를 3D grid (cellSize ≈ chunkSize × 1.5)로 분할
    //   2. 각 edge가 겹치는 모든 cell에 edge index 등록
    //   3. GPU 전송용 2개 buffer 생성:
    //      - CellRangeBuffer : uint2[totalCells] — (start, count)
    //      - EdgeIndicesBuffer : uint[flatTotal]
    //
    // Streaming 시:
    //   Shader가 worldPos로 cellIdx 계산 → range lookup → 해당 edges만 읽음
    //   평균 edge reads/voxel: 50 → 3~5 (90% 감소)
    //
    // 특성:
    //   - Immutable 완벽 활용 (한번 계산 영구 사용)
    //   - Streaming 중 buffer 재전송 0
    //   - VRAM 오버헤드: ~30~200 KB (무시 가능)
    // ══════════════════════════════════════════════════════════════════════

    public class SpatialHash
    {
        public Vector3 gridOrigin;      // Cell (0,0,0)의 world min corner
        public float cellSize;
        public int cellsX, cellsY, cellsZ;
        public int totalCells;

        // Cell → edge indices 매핑
        public uint[] cellRanges;       // 각 cell당 (start, count) 압축: (count << 24) | start 는 아님
                                         // 실제론 uint2 사용 → GPU layout 직접 대응
        public uint[] cellStarts;       // offset 역할
        public uint[] cellCounts;       // 개수
        public uint[] flatEdgeIndices;  // 평탄화된 edge idx 리스트

        // GPU 전송용 combined uint2 (start, count) — CellRangeBuffer 데이터
        public Vector2Int[] cellRangesCombined;   // int2 to upload

        public int TotalIndexEntries => flatEdgeIndices != null ? flatEdgeIndices.Length : 0;
        public int VramSizeBytes => totalCells * 8 + TotalIndexEntries * 4;

        /// <summary>
        /// Graph 전체로부터 spatial hash 구축.
        /// 모든 edge가 확정된 후 (Supervisor + RoutePlanner 끝난 후) 호출.
        /// </summary>
        public static SpatialHash Build(
            List<EdgeData> edges, Vector3 dungeonBounds, float cellSizeOverride = -1f)
        {
            var hash = new SpatialHash();

            // Cell 크기: dungeon 전체를 13~21 cell로 — chunk_world_size × 1.5 근사
            // pB-4: chunkSize=107 × 0.15m × 1.5 ≈ 24m
            hash.cellSize = (cellSizeOverride > 0) ? cellSizeOverride : 24f;

            // Bounds 여유 확보
            float margin = hash.cellSize;
            Vector3 mapMin = -dungeonBounds * 0.5f - Vector3.one * margin;
            Vector3 mapMax = dungeonBounds * 0.5f + Vector3.one * margin;

            hash.gridOrigin = mapMin;
            Vector3 extent = mapMax - mapMin;
            hash.cellsX = Mathf.Max(1, Mathf.CeilToInt(extent.x / hash.cellSize));
            hash.cellsY = Mathf.Max(1, Mathf.CeilToInt(extent.y / hash.cellSize));
            hash.cellsZ = Mathf.Max(1, Mathf.CeilToInt(extent.z / hash.cellSize));
            hash.totalCells = hash.cellsX * hash.cellsY * hash.cellsZ;

            // Pass 1: 각 cell에 몇 개 edge 겹치는지 count
            var tempLists = new List<uint>[hash.totalCells];
            for (int i = 0; i < hash.totalCells; i++) tempLists[i] = new List<uint>();

            for (int edgeIdx = 0; edgeIdx < edges.Count; edgeIdx++)
            {
                var e = edges[edgeIdx];
                // Edge의 AABB는 이미 pre-computed (O1). 없으면 computed fallback.
                Vector3 aabbMin = e.aabbMin;
                Vector3 aabbMax = e.aabbMax;
                if (aabbMax.x - aabbMin.x < 0.01f)
                {
                    // AABB 미계산 — 런타임 계산
                    var tempE = e;
                    RouteGeometry.ComputeEdgeAABB(ref tempE);
                    aabbMin = tempE.aabbMin;
                    aabbMax = tempE.aabbMax;
                }

                // AABB → cell index 범위
                Vector3Int cellMin = hash.WorldToCellClamped(aabbMin);
                Vector3Int cellMax = hash.WorldToCellClamped(aabbMax);

                for (int cz = cellMin.z; cz <= cellMax.z; cz++)
                for (int cy = cellMin.y; cy <= cellMax.y; cy++)
                for (int cx = cellMin.x; cx <= cellMax.x; cx++)
                {
                    int ci = cx + cy * hash.cellsX + cz * hash.cellsX * hash.cellsY;
                    tempLists[ci].Add((uint)edgeIdx);
                }
            }

            // Pass 2: flat buffer로 변환 + range 계산
            int totalEntries = 0;
            for (int c = 0; c < hash.totalCells; c++) totalEntries += tempLists[c].Count;

            hash.cellRangesCombined = new Vector2Int[hash.totalCells];
            hash.cellStarts = new uint[hash.totalCells];
            hash.cellCounts = new uint[hash.totalCells];
            hash.flatEdgeIndices = new uint[Mathf.Max(1, totalEntries)];

            uint cursor = 0;
            for (int c = 0; c < hash.totalCells; c++)
            {
                uint count = (uint)tempLists[c].Count;
                hash.cellStarts[c] = cursor;
                hash.cellCounts[c] = count;
                hash.cellRangesCombined[c] = new Vector2Int((int)cursor, (int)count);

                for (int k = 0; k < count; k++)
                {
                    hash.flatEdgeIndices[cursor + k] = tempLists[c][k];
                }
                cursor += count;
            }

            return hash;
        }

        // ─────────────────────────────────────────────────────────────
        // World position → cell index (CPU 검증용, shader와 동일 로직)
        // ─────────────────────────────────────────────────────────────
        public Vector3Int WorldToCellClamped(Vector3 worldPos)
        {
            Vector3 local = worldPos - gridOrigin;
            int cx = Mathf.Clamp(Mathf.FloorToInt(local.x / cellSize), 0, cellsX - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt(local.y / cellSize), 0, cellsY - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt(local.z / cellSize), 0, cellsZ - 1);
            return new Vector3Int(cx, cy, cz);
        }

        public int WorldToCellIdx(Vector3 worldPos)
        {
            var c = WorldToCellClamped(worldPos);
            return c.x + c.y * cellsX + c.z * cellsX * cellsY;
        }

        /// <summary>
        /// Statistics — 평균/최대 cell당 edge 수, coverage 등.
        /// </summary>
        public string GetStats()
        {
            if (cellCounts == null || totalCells == 0) return "SpatialHash not built";

            uint max = 0;
            uint sum = 0;
            int occupiedCells = 0;
            for (int c = 0; c < totalCells; c++)
            {
                uint count = cellCounts[c];
                if (count > max) max = count;
                sum += count;
                if (count > 0) occupiedCells++;
            }

            float avgAll = sum / (float)totalCells;
            float avgOccupied = occupiedCells > 0 ? sum / (float)occupiedCells : 0f;
            float coverage = occupiedCells / (float)totalCells;

            return $"SpatialHash: grid={cellsX}×{cellsY}×{cellsZ} ({totalCells} cells, {cellSize}m), " +
                   $"entries={TotalIndexEntries}, vram={VramSizeBytes / 1024f:F1} KB, " +
                   $"avg/cell={avgAll:F2}, avg/occupied={avgOccupied:F1}, " +
                   $"max={max}, coverage={coverage * 100f:F1}%";
        }
    }
}
