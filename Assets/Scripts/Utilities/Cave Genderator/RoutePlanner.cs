using UnityEngine;
using System.Collections.Generic;

namespace CaveSystem
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-G Stage 3-C] RoutePlanner — Node-based A*
    //
    // 목적: start/end 노드 간 edge를 만들 때, 기존 edges와 collision 발생 시
    //       기존 nodes를 "transit waypoint"로 삼아 우회 경로 탐색.
    //
    // 3-Phase 알고리즘:
    //   Phase 1: 직선 시도 (early exit if collision-free)
    //   Phase 2: 1-transit 탐색 (모든 노드 중 최단 경유)
    //   Phase 3: 2-transit A* (Phase 2 실패 시)
    //
    // Transit 해석: Flyby (§4 Canonical Reference)
    //   Transit node의 degree 변경 없음. waypoint는 node 근처 offset 위치.
    //
    // 복잡도: Phase 1 O(E), Phase 2 O(N × E), Phase 3 O(N² × E)
    // ══════════════════════════════════════════════════════════════════════

    public static class RoutePlanner
    {
        // Flyby 시 node radius 외부 이 거리만큼 떨어진 점에 waypoint 배치
        public const float FlybyBuffer = 3.0f;

        // Waypoint pack 범위 제한 (midpoint 기준 ±10m — Y axis 기준)
        // 이 범위 초과하면 packing 정밀도 손실 → route 실패로 처리
        public const float MaxOffsetX = 5.0f;
        public const float MaxOffsetY = 10.0f;
        public const float MaxOffsetZ = 10.0f;

        // ═══════════════════════════════════════════════════════════════════
        // [Phase 4.5-G G2] Biome-Aware Routing Configuration
        //
        //   PassageMetric.useShaderCompatSampling 패턴과 동일 — static field
        //   CaveNodeGraphBuilder.GenerateGraphInternal 시작에서 전파.
        //
        //   D2 토글 원칙:
        //     enableBiomeAwareRouting = false (기본) → byte-identical
        //     enableBiomeAwareRouting = true → biome cross 시 우회 강제
        //
        //   4원칙 영향: 0 (graph 단계, SDF amplitude/frequency 변경 없음)
        //   규칙 23 영향: 0 (stateless static, lifecycle 무관)
        //   VRAM 영향: 0 (CPU-only, GPU buffer 미증가)
        //   Streaming 결정성: BiomeShaderSync 결과는 deterministic (보장됨)
        // ═══════════════════════════════════════════════════════════════════
        public static bool enableBiomeAwareRouting = false;
        public static CaveBiomeSettings biomeSettingsRef = null;  // 주입용
        public static int biomeCrossDetectedCount = 0;             // 통계 (각 PlanRoute 호출 시 누적)
        public static float biomeCrossThreshold = 0.3f;            // [G2] cross 판정 임계 (Inspector 동기화)

        /// <summary>
        /// G2 호출 전 외부에서 호출 — 통계 리셋.
        /// CaveNodeGraphBuilder.RunRoutePlanner 시작에서 호출 권장.
        /// </summary>
        public static void ResetBiomeAwareStats()
        {
            biomeCrossDetectedCount = 0;
        }

        // ─────────────────────────────────────────────────────────────────
        // Route 결과 struct
        // ─────────────────────────────────────────────────────────────────
        public struct Route
        {
            public bool success;
            public int numWaypoints;
            public Vector3 waypoint1;       // world pos (numWaypoints >= 1)
            public Vector3 waypoint2;       // world pos (numWaypoints >= 2)
            public int transitNodeIdx1;     // -1 if none (flyby source)
            public int transitNodeIdx2;     // -1 if none
        }

        // ═══════════════════════════════════════════════════════════════════
        // [G2] Biome Consistency Check
        //
        //   start, end 양 끝점의 biome과 다른 biome을 segments 중간에서 사용한
        //   비율을 측정. 임계 (기본 30%) 초과 시 cross-biome으로 판정 → 우회 trigger.
        //
        //   샘플링: 10 segments (start/end 제외, 중간 9 점).
        //   GPU shader와 정확 일치 (BiomeShaderSync.SampleBiomeIdx).
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// passage가 양 끝과 다른 biome zone을 가로지르는지 검사.
        /// 결과: true = consistent (가로지름 없음 또는 임계 미만), false = cross-biome (우회 필요)
        /// </summary>
        public static bool BiomeConsistencyCheck(
            Vector3 start, Vector3 end, CaveBiomeSettings bs,
            int sampleCount = 10, float thresholdRatio = 0.3f)
        {
            if (bs == null || bs.globalBiomes == null || bs.globalBiomes.Count <= 1)
                return true;  // biome 정보 없으면 검사 skip

            int startBiome = BiomeShaderSync.SampleBiomeIdx(start, bs);
            int endBiome = BiomeShaderSync.SampleBiomeIdx(end, bs);

            int crossingSegments = 0;
            for (int i = 1; i < sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                Vector3 mid = Vector3.Lerp(start, end, t);
                int midBiome = BiomeShaderSync.SampleBiomeIdx(mid, bs);

                // 양 끝 biome 어느 것과도 다른 biome을 통과 → crossing
                if (midBiome != startBiome && midBiome != endBiome)
                    crossingSegments++;
            }

            float crossRatio = (float)crossingSegments / (sampleCount - 1);
            return crossRatio < thresholdRatio;  // false = cross-biome (우회 필요)
        }

        /// <summary>
        /// Start/End 노드 간 edge 경로 계획.
        /// Obstacles = 이미 확정된 edges.
        /// Nodes = 모든 노드 (transit 후보).
        /// </summary>
        public static Route PlanRoute(
            int startNodeIdx, int endNodeIdx, float width,
            List<NodeData> allNodes, List<EdgeData> existingEdges,
            int maxTransitNodes = 2)
        {
            if (startNodeIdx < 0 || startNodeIdx >= allNodes.Count ||
                endNodeIdx < 0 || endNodeIdx >= allNodes.Count)
                return new Route { success = false };

            Vector3 start = allNodes[startNodeIdx].position;
            Vector3 end = allNodes[endNodeIdx].position;

            // ── Phase 1: 직선 시도 ─────────────────────────────────────
            bool edgeCollide = RouteGeometry.SegmentCollidesWith(start, end, width, existingEdges);
            bool nodeCollide = RouteGeometry.SegmentCollidesWithNodes(
                start, end, width, allNodes, startNodeIdx, endNodeIdx);

            // [G2 토글 가드] Biome-aware routing — cross-biome 검출 시 우회 강제
            //   D2 OFF (기본): 검사 skip, 기존 동작 그대로 (byte-identical)
            //   ON: cross-biome 발견 시 edgeCollide=true 처리 → A* 우회 trigger
            if (enableBiomeAwareRouting && biomeSettingsRef != null)
            {
                bool biomeOk = BiomeConsistencyCheck(start, end, biomeSettingsRef,
                                                     sampleCount: 10,
                                                     thresholdRatio: biomeCrossThreshold);
                if (!biomeOk)
                {
                    edgeCollide = true;  // 우회 강제
                    biomeCrossDetectedCount++;
                }
            }

            if (!edgeCollide && !nodeCollide)
            {
                return new Route
                {
                    success = true,
                    numWaypoints = 0,
                    transitNodeIdx1 = -1,
                    transitNodeIdx2 = -1
                };
            }

            // ── Phase 2: 1-transit 탐색 ────────────────────────────────
            Route best1 = FindBest1Transit(
                startNodeIdx, endNodeIdx, width, allNodes, existingEdges);

            if (best1.success) return best1;

            // ── Phase 3: 2-transit A* (maxTransitNodes >= 2 일 때) ────
            if (maxTransitNodes >= 2)
            {
                Route best2 = FindBest2TransitAStar(
                    startNodeIdx, endNodeIdx, width, allNodes, existingEdges);
                if (best2.success) return best2;
            }

            // 모든 시도 실패
            return new Route { success = false };
        }

        // ─────────────────────────────────────────────────────────────
        // Phase 2: 1-transit 탐색
        //   모든 노드를 후보로. 두 segment 모두 collision-free인 것 중 최단.
        // ─────────────────────────────────────────────────────────────
        private static Route FindBest1Transit(
            int startIdx, int endIdx, float width,
            List<NodeData> allNodes, List<EdgeData> existingEdges)
        {
            Vector3 start = allNodes[startIdx].position;
            Vector3 end = allNodes[endIdx].position;

            int bestMidIdx = -1;
            Vector3 bestWaypoint = Vector3.zero;
            float bestLen = float.MaxValue;

            for (int midIdx = 0; midIdx < allNodes.Count; midIdx++)
            {
                if (midIdx == startIdx || midIdx == endIdx) continue;

                // Flyby waypoint 계산 — transit node 근처, 실제 node 내부 아님
                Vector3 flyby = ComputeFlybyWaypoint(start, end, allNodes[midIdx]);

                // Waypoint가 midpoint 기준 packing 범위 내인지 확인
                Vector3 midpoint = (start + end) * 0.5f;
                Vector3 offset = flyby - midpoint;
                if (Mathf.Abs(offset.x) > MaxOffsetX ||
                    Mathf.Abs(offset.y) > MaxOffsetY ||
                    Mathf.Abs(offset.z) > MaxOffsetZ)
                    continue;  // packing 정밀도 초과

                // Seg1: start → flyby
                if (RouteGeometry.SegmentCollidesWith(start, flyby, width, existingEdges)) continue;
                if (RouteGeometry.SegmentCollidesWithNodes(
                    start, flyby, width, allNodes, startIdx, -1, midIdx)) continue;

                // Seg2: flyby → end
                if (RouteGeometry.SegmentCollidesWith(flyby, end, width, existingEdges)) continue;
                if (RouteGeometry.SegmentCollidesWithNodes(
                    flyby, end, width, allNodes, -1, endIdx, midIdx)) continue;

                float totalLen = Vector3.Distance(start, flyby) + Vector3.Distance(flyby, end);
                if (totalLen < bestLen)
                {
                    bestLen = totalLen;
                    bestMidIdx = midIdx;
                    bestWaypoint = flyby;
                }
            }

            if (bestMidIdx >= 0)
            {
                return new Route
                {
                    success = true,
                    numWaypoints = 1,
                    waypoint1 = bestWaypoint,
                    transitNodeIdx1 = bestMidIdx,
                    transitNodeIdx2 = -1
                };
            }
            return new Route { success = false };
        }

        // ─────────────────────────────────────────────────────────────
        // Phase 3: 2-transit A* (단순화 버전)
        //   N 이 작으므로 full A* 아닌 nested search:
        //     모든 (mid1, mid2) 쌍에 대해 3-segment collision 검사
        //   복잡도 O(N² × E) — N=20, E=50이면 20000 ops (~3ms)
        // ─────────────────────────────────────────────────────────────
        private static Route FindBest2TransitAStar(
            int startIdx, int endIdx, float width,
            List<NodeData> allNodes, List<EdgeData> existingEdges)
        {
            Vector3 start = allNodes[startIdx].position;
            Vector3 end = allNodes[endIdx].position;
            Vector3 midpoint = (start + end) * 0.5f;

            int bestMid1 = -1, bestMid2 = -1;
            Vector3 bestWp1 = Vector3.zero, bestWp2 = Vector3.zero;
            float bestLen = float.MaxValue;

            for (int m1 = 0; m1 < allNodes.Count; m1++)
            {
                if (m1 == startIdx || m1 == endIdx) continue;

                Vector3 flyby1 = ComputeFlybyWaypoint(start, end, allNodes[m1]);
                Vector3 offset1 = flyby1 - midpoint;
                if (Mathf.Abs(offset1.x) > MaxOffsetX ||
                    Mathf.Abs(offset1.y) > MaxOffsetY ||
                    Mathf.Abs(offset1.z) > MaxOffsetZ) continue;

                // Seg1: start → flyby1
                if (RouteGeometry.SegmentCollidesWith(start, flyby1, width, existingEdges)) continue;
                if (RouteGeometry.SegmentCollidesWithNodes(
                    start, flyby1, width, allNodes, startIdx, -1, m1)) continue;

                for (int m2 = 0; m2 < allNodes.Count; m2++)
                {
                    if (m2 == startIdx || m2 == endIdx || m2 == m1) continue;

                    Vector3 flyby2 = ComputeFlybyWaypoint(flyby1, end, allNodes[m2]);
                    Vector3 offset2 = flyby2 - midpoint;
                    if (Mathf.Abs(offset2.x) > MaxOffsetX ||
                        Mathf.Abs(offset2.y) > MaxOffsetY ||
                        Mathf.Abs(offset2.z) > MaxOffsetZ) continue;

                    // Seg2: flyby1 → flyby2
                    if (RouteGeometry.SegmentCollidesWith(flyby1, flyby2, width, existingEdges)) continue;
                    if (RouteGeometry.SegmentCollidesWithNodes(
                        flyby1, flyby2, width, allNodes, -1, -1, m2)) continue;

                    // Seg3: flyby2 → end
                    if (RouteGeometry.SegmentCollidesWith(flyby2, end, width, existingEdges)) continue;
                    if (RouteGeometry.SegmentCollidesWithNodes(
                        flyby2, end, width, allNodes, -1, endIdx, m2)) continue;

                    float totalLen = Vector3.Distance(start, flyby1)
                                   + Vector3.Distance(flyby1, flyby2)
                                   + Vector3.Distance(flyby2, end);
                    if (totalLen < bestLen)
                    {
                        bestLen = totalLen;
                        bestMid1 = m1;
                        bestMid2 = m2;
                        bestWp1 = flyby1;
                        bestWp2 = flyby2;
                    }
                }
            }

            if (bestMid1 >= 0)
            {
                return new Route
                {
                    success = true,
                    numWaypoints = 2,
                    waypoint1 = bestWp1,
                    waypoint2 = bestWp2,
                    transitNodeIdx1 = bestMid1,
                    transitNodeIdx2 = bestMid2
                };
            }
            return new Route { success = false };
        }

        // ─────────────────────────────────────────────────────────────
        // Flyby waypoint 계산
        //   Transit node 주변 (radius + FlybyBuffer) 외부의 점.
        //   방향: (segment midpoint → transitNode) — transit을 "지나가는" 힌트.
        // ─────────────────────────────────────────────────────────────
        private static Vector3 ComputeFlybyWaypoint(
            Vector3 segStart, Vector3 segEnd, NodeData transitNode)
        {
            Vector3 segMid = (segStart + segEnd) * 0.5f;
            Vector3 toward = transitNode.position - segMid;
            float dist = toward.magnitude;

            if (dist < 0.01f)
            {
                // segment midpoint와 node 일치 — perpendicular 방향 fallback
                Vector3 segDir = (segEnd - segStart).normalized;
                Vector3 up = Mathf.Abs(segDir.y) > 0.9f ? Vector3.right : Vector3.up;
                toward = Vector3.Cross(segDir, up).normalized * (transitNode.radius + FlybyBuffer);
                return transitNode.position + toward;
            }

            // segMid → transitNode 방향으로 이동, node 앞 버퍼에서 멈춤
            Vector3 dir = toward / dist;
            float flybyDist = dist - (transitNode.radius + FlybyBuffer);
            if (flybyDist < 0.5f)
            {
                // node가 매우 가까움 — node 주위를 옆으로 지나감
                Vector3 segDir = (segEnd - segStart).normalized;
                Vector3 perp = Vector3.Cross(segDir, dir).normalized;
                if (perp.sqrMagnitude < 0.01f)
                    perp = Vector3.Cross(segDir, Vector3.up).normalized;
                return transitNode.position + perp * (transitNode.radius + FlybyBuffer);
            }

            return segMid + dir * flybyDist;
        }

        // ─────────────────────────────────────────────────────────────
        // Statistics (디버그용)
        // ─────────────────────────────────────────────────────────────
        public struct PlanStats
        {
            public int totalEdges;
            public int directCount;       // numWaypoints = 0
            public int oneTransitCount;   // numWaypoints = 1
            public int twoTransitCount;   // numWaypoints = 2
            public int failedCount;

            public override string ToString()
            {
                return $"Total={totalEdges}: Direct={directCount}, " +
                       $"1-Transit={oneTransitCount}, 2-Transit={twoTransitCount}, " +
                       $"Failed={failedCount}";
            }
        }
    }
}
