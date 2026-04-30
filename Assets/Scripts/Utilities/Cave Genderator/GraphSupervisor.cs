using System.Collections.Generic;
using UnityEngine;

namespace CaveSystem
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-G Stage 2] GraphSupervisor
    //
    // 목적:
    //   Graph 생성 후 원칙 준수 여부 검증 + 자동 수정.
    //
    // 검증 원칙:
    //   P5 Planarity  — edge 간 2D crossover (Y 차이 충분한 경우 예외)
    //   P6 Degree     — 노드당 최대 edge 수 (roomType별 차등)
    //   P7 Biome Quota — biome 별 nodeDensityWeight 기반 배분 균형
    //
    // AutoRepair:
    //   Crossover 발견 → 더 긴 loop edge 제거 (MST edge 보호)
    //   Degree 초과 → 가장 최근 loop edge 제거
    //   Biome 불균형 → (현재는 경고만, 실제 재배치는 Phase 6+에서)
    //
    // 성능:
    //   P5: O(E²) = E<100 → 10000 ops → ~1ms
    //   P6: O(E) — instantaneous
    //   P7: O(N × biomes) — instantaneous
    //   AutoRepair: O(iter × violations) — 보통 3 iter 이하
    //
    // ══════════════════════════════════════════════════════════════════════

    public static class GraphSupervisor
    {
        public enum ViolationType
        {
            Crossover,        // 두 edge가 2D 교차 + Y 근접
            DegreeExceed,     // 노드의 degree가 max 초과
            BiomeImbalance,   // biome 별 quota 대비 ±40% 초과 편차
            IsolatedNode,     // 연결 끊어진 노드 (critical)
        }

        public struct Violation
        {
            public ViolationType type;
            public int nodeIdx;       // 관련 노드 (Degree/Biome/Isolated)
            public int edgeIdxA;      // 관련 edge (Crossover 2개 중 첫째, Degree 시 초과 edge)
            public int edgeIdxB;      // Crossover 두 번째
            public int biomeIdx;      // Biome imbalance 관련 biome
            public float severity;    // 0~1 정도
            public string description;
        }

        public struct ValidationResult
        {
            public bool isValid;
            public List<Violation> violations;
            public int crossoverCount, degreeExceedCount, biomeImbalanceCount, isolatedCount;
            public int repairIterations;   // AutoRepair 사용 시 몇 번 iter 했는지

            public static ValidationResult Empty()
            {
                return new ValidationResult
                {
                    isValid = true,
                    violations = new List<Violation>()
                };
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 공개 API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Graph 검증 — 원칙 위반 사항 모두 수집.
        /// </summary>
        public static ValidationResult Validate(
            List<NodeData> nodes, List<EdgeData> edges,
            CaveBiomeSettings biomeSettings,
            float planarityYExemptMeters = 6.0f,
            int maxDegreeNormal = 4,
            bool checkPlanarity = true,
            bool checkDegree = true,
            bool checkBiomeQuota = true)
        {
            var result = ValidationResult.Empty();

            if (nodes == null || edges == null || nodes.Count == 0)
                return result;

            // ── P5 Planarity ─────────────────────────
            if (checkPlanarity)
            {
                for (int i = 0; i < edges.Count; i++)
                {
                    for (int j = i + 1; j < edges.Count; j++)
                    {
                        if (IsCrossoverEdge(edges[i], edges[j], planarityYExemptMeters))
                        {
                            result.violations.Add(new Violation
                            {
                                type = ViolationType.Crossover,
                                edgeIdxA = i,
                                edgeIdxB = j,
                                severity = 1.0f,
                                description = $"Edge {i} ↔ Edge {j}: 2D 교차 + Y 근접 (crossover)"
                            });
                            result.crossoverCount++;
                        }
                    }
                }
            }

            // ── P6 Degree ────────────────────────────
            if (checkDegree)
            {
                var degrees = new Dictionary<int, int>();
                // Edge → node idx 매핑 (edge 내 node 위치로 근접 검사 — Runtime NodeData는 index 저장 안 함)
                // 대신 edge의 startPos/endPos를 node position과 matching
                var edgeNodeMap = BuildEdgeNodeMap(nodes, edges);

                foreach (var kv in edgeNodeMap)
                {
                    int edgeIdx = kv.Key;
                    int[] nodeIndices = kv.Value;
                    foreach (int nIdx in nodeIndices)
                    {
                        if (nIdx >= 0)
                        {
                            degrees[nIdx] = degrees.ContainsKey(nIdx) ? degrees[nIdx] + 1 : 1;
                        }
                    }
                }

                foreach (var kv in degrees)
                {
                    int nIdx = kv.Key;
                    int deg = kv.Value;
                    int maxDeg = GetMaxDegreeForRoomType(nodes[nIdx].roomType, maxDegreeNormal);
                    if (deg > maxDeg)
                    {
                        result.violations.Add(new Violation
                        {
                            type = ViolationType.DegreeExceed,
                            nodeIdx = nIdx,
                            severity = Mathf.Min(1f, (deg - maxDeg) / 3f),
                            description = $"Node {nIdx} (roomType={nodes[nIdx].roomType}): " +
                                          $"degree={deg} > max {maxDeg}"
                        });
                        result.degreeExceedCount++;
                    }
                }
            }

            // ── P7 Biome Quota ───────────────────────
            if (checkBiomeQuota && biomeSettings != null && biomeSettings.globalBiomes != null
                && biomeSettings.globalBiomes.Count > 0)
            {
                // 각 노드가 어떤 biome인지 BiomeSampler 로직 (CPU-side 단순화)
                //   실제 shader와 일치하려면 ShaderNoiseCompat 필요. 여기선 근사만.
                var biomeNodeCounts = CountNodesByBiome(nodes, biomeSettings);

                // Total density weight 계산
                float totalWeight = 0f;
                for (int b = 0; b < biomeSettings.globalBiomes.Count; b++)
                {
                    var bd = biomeSettings.globalBiomes[b];
                    if (bd != null) totalWeight += bd.nodeDensityWeight;
                }
                if (totalWeight < 0.01f) totalWeight = biomeSettings.globalBiomes.Count;

                for (int b = 0; b < biomeSettings.globalBiomes.Count; b++)
                {
                    var bd = biomeSettings.globalBiomes[b];
                    if (bd == null) continue;

                    float expectedRatio = bd.nodeDensityWeight / totalWeight;
                    int expectedCount = Mathf.RoundToInt(nodes.Count * expectedRatio);
                    int actualCount = biomeNodeCounts.ContainsKey(b) ? biomeNodeCounts[b] : 0;

                    // ±40% 편차 허용 (최소 절대 편차 2)
                    int tolerance = Mathf.Max(2, Mathf.RoundToInt(expectedCount * 0.4f));
                    if (Mathf.Abs(actualCount - expectedCount) > tolerance)
                    {
                        result.violations.Add(new Violation
                        {
                            type = ViolationType.BiomeImbalance,
                            biomeIdx = b,
                            severity = Mathf.Clamp01(
                                Mathf.Abs(actualCount - expectedCount) / (float)(expectedCount + 1)),
                            description = $"Biome {bd.biomeName} (#{b}): " +
                                          $"expected={expectedCount}, actual={actualCount} " +
                                          $"(weight={bd.nodeDensityWeight:F2})"
                        });
                        result.biomeImbalanceCount++;
                    }
                }
            }

            result.isValid = result.violations.Count == 0;
            return result;
        }

        /// <summary>
        /// [Stage 2] AutoRepair — validation violations 자동 수정.
        /// 현재: Crossover loop edge 제거 / Degree 초과 loop edge 제거.
        /// BiomeImbalance는 로그만 (재배치는 Phase 6+).
        /// </summary>
        public static ValidationResult AutoRepair(
            List<NodeData> nodes,
            List<EdgeData> edges,
            HashSet<Vector2Int> mstEdgeKeys,  // MST edge 보호 (절대 제거 금지)
            CaveBiomeSettings biomeSettings,
            float planarityYExemptMeters = 6.0f,
            int maxDegreeNormal = 4,
            int maxIterations = 3)
        {
            ValidationResult lastResult = ValidationResult.Empty();

            for (int iter = 0; iter < maxIterations; iter++)
            {
                lastResult = Validate(nodes, edges, biomeSettings,
                    planarityYExemptMeters, maxDegreeNormal,
                    checkPlanarity: true, checkDegree: true, checkBiomeQuota: true);
                lastResult.repairIterations = iter + 1;

                if (lastResult.isValid) break;

                bool didRepair = false;

                // Edge-based violations 처리 (Crossover, DegreeExceed)
                //   우선순위: Crossover → Degree
                //   제거 대상: MST edge가 아닌 loop edge 중 더 긴 쪽
                var edgesToRemove = new HashSet<int>();

                foreach (var v in lastResult.violations)
                {
                    if (v.type == ViolationType.Crossover)
                    {
                        int removeIdx = ChooseEdgeToRemove(v.edgeIdxA, v.edgeIdxB, edges, mstEdgeKeys, nodes);
                        if (removeIdx >= 0 && !edgesToRemove.Contains(removeIdx))
                        {
                            edgesToRemove.Add(removeIdx);
                            didRepair = true;
                        }
                    }
                    else if (v.type == ViolationType.DegreeExceed)
                    {
                        // 해당 노드에 연결된 edge 중 MST가 아닌 loop edge, 가장 긴 것 제거
                        int removeIdx = FindLongestLoopEdgeAtNode(v.nodeIdx, nodes, edges, mstEdgeKeys);
                        if (removeIdx >= 0 && !edgesToRemove.Contains(removeIdx))
                        {
                            edgesToRemove.Add(removeIdx);
                            didRepair = true;
                        }
                    }
                    // BiomeImbalance: 로그만, 실제 수정은 Phase 6+에서 Junction 시스템과 결합
                }

                if (!didRepair) break;

                // Edges 제거 (뒤부터 — index 안정성)
                var sortedToRemove = new List<int>(edgesToRemove);
                sortedToRemove.Sort((a, b) => b.CompareTo(a));
                foreach (int idx in sortedToRemove)
                {
                    if (idx >= 0 && idx < edges.Count)
                        edges.RemoveAt(idx);
                }
            }

            // 최종 검증 결과 반환
            var finalResult = Validate(nodes, edges, biomeSettings,
                planarityYExemptMeters, maxDegreeNormal,
                checkPlanarity: true, checkDegree: true, checkBiomeQuota: true);
            finalResult.repairIterations = lastResult.repairIterations;
            return finalResult;
        }

        // ─────────────────────────────────────────────────────────────────
        // 내부 헬퍼
        // ─────────────────────────────────────────────────────────────────

        /// <summary>두 edge가 crossover 관계인지 (2D 교차 + Y 근접).</summary>
        private static bool IsCrossoverEdge(EdgeData a, EdgeData b, float yExempt)
        {
            // 공유 노드 (start/end 거의 같은 position)는 제외
            const float shareThreshold = 0.5f;
            if (Vector3.Distance(a.startPos, b.startPos) < shareThreshold) return false;
            if (Vector3.Distance(a.startPos, b.endPos)   < shareThreshold) return false;
            if (Vector3.Distance(a.endPos,   b.startPos) < shareThreshold) return false;
            if (Vector3.Distance(a.endPos,   b.endPos)   < shareThreshold) return false;

            // 2D 교차
            bool xzCross = Segments2DIntersect(
                new Vector2(a.startPos.x, a.startPos.z), new Vector2(a.endPos.x, a.endPos.z),
                new Vector2(b.startPos.x, b.startPos.z), new Vector2(b.endPos.x, b.endPos.z));
            if (!xzCross) return false;

            // Y 차이
            float aMidY = (a.startPos.y + a.endPos.y) * 0.5f;
            float bMidY = (b.startPos.y + b.endPos.y) * 0.5f;
            return Mathf.Abs(aMidY - bMidY) < yExempt;
        }

        private static bool Segments2DIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float CCW(Vector2 a, Vector2 b, Vector2 c) =>
                (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

            float d1 = CCW(p3, p4, p1);
            float d2 = CCW(p3, p4, p2);
            float d3 = CCW(p1, p2, p3);
            float d4 = CCW(p1, p2, p4);

            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                   ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        /// <summary>Edge → 연결된 2개 node index 매핑 (position 기반 최근접).</summary>
        private static Dictionary<int, int[]> BuildEdgeNodeMap(List<NodeData> nodes, List<EdgeData> edges)
        {
            var map = new Dictionary<int, int[]>();
            for (int e = 0; e < edges.Count; e++)
            {
                int nA = FindNearestNodeIndex(edges[e].startPos, nodes);
                int nB = FindNearestNodeIndex(edges[e].endPos, nodes);
                map[e] = new int[] { nA, nB };
            }
            return map;
        }

        private static int FindNearestNodeIndex(Vector3 pos, List<NodeData> nodes)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                float d = Vector3.SqrMagnitude(pos - nodes[i].position);
                if (d < bestD) { bestD = d; best = i; }
            }
            // 0.5m 이내면 valid
            return (bestD < 0.25f) ? best : -1;
        }

        private static int GetMaxDegreeForRoomType(int roomType, int maxDegreeNormal)
        {
            switch (roomType)
            {
                case 1: return 3;                 // Spawn
                case 2: return maxDegreeNormal;   // Boss
                case 3: return 2;                 // Treasure
                case 4: return 2;                 // Sinkhole
                default: return maxDegreeNormal;  // Normal
            }
        }

        /// <summary>각 노드의 biome idx 결정 (shader와 일치).</summary>
        private static Dictionary<int, int> CountNodesByBiome(
            List<NodeData> nodes, CaveBiomeSettings settings)
        {
            var counts = new Dictionary<int, int>();
            if (settings == null || settings.globalBiomes == null || settings.globalBiomes.Count == 0)
                return counts;

            float macroScale = Mathf.Max(1f, settings.macroBiomeScale);
            int biomeCount = settings.globalBiomes.Count;

            for (int i = 0; i < nodes.Count; i++)
            {
                var pos = nodes[i].position;
                // ═══════════════════════════════════════════════════════════
                // [η DebugAware Phase 9 + Fix #2] effective biome 카운팅
                //   기존: BiomeShaderSync.SampleBiomeIdx → bIdxA만 → 부정확
                //   수정: GPU 평가 biome ((bw<0.5)?bIdxA:bIdxB) 카운팅 → 정확
                //
                //   결과: BiomeImbalance violation이 GPU 실제 분포와 일치.
                //         GraphBuilder.GetBiomeIdxAt와 일관 — 같은 효과.
                // ═══════════════════════════════════════════════════════════
                int bIdxA = CaveSystem.BiomeShaderSync.SampleBiomeIdx(pos, settings);
                int idx;
                if (biomeCount >= 2)
                {
                    int bIdxB = Mathf.Clamp(bIdxA + 1, 0, biomeCount - 1);
                    float bw = CaveSystem.BiomeShaderSync.SampleBlendWeight(pos, settings);
                    idx = (bw < 0.5f) ? bIdxA : bIdxB;
                }
                else
                {
                    idx = bIdxA;
                }
                if (idx < 0 || idx >= biomeCount) idx = 0;
                counts[idx] = counts.ContainsKey(idx) ? counts[idx] + 1 : 1;
            }
            return counts;
        }

        /// <summary>Crossover 두 edge 중 제거할 edge 선택 (MST 보호 + 긴 edge 우선).</summary>
        private static int ChooseEdgeToRemove(int idxA, int idxB,
            List<EdgeData> edges, HashSet<Vector2Int> mstEdgeKeys, List<NodeData> nodes)
        {
            bool aIsMST = IsMSTEdge(edges[idxA], mstEdgeKeys, nodes);
            bool bIsMST = IsMSTEdge(edges[idxB], mstEdgeKeys, nodes);

            if (aIsMST && bIsMST) return -1;  // 둘 다 MST → 제거 불가 (violation 남김)
            if (aIsMST) return idxB;          // A가 MST면 B 제거
            if (bIsMST) return idxA;          // B가 MST면 A 제거

            // 둘 다 loop edge → 더 긴 쪽 제거
            float lenA = Vector3.Distance(edges[idxA].startPos, edges[idxA].endPos);
            float lenB = Vector3.Distance(edges[idxB].startPos, edges[idxB].endPos);
            return lenA > lenB ? idxA : idxB;
        }

        private static bool IsMSTEdge(EdgeData e, HashSet<Vector2Int> mstEdgeKeys, List<NodeData> nodes)
        {
            if (mstEdgeKeys == null || mstEdgeKeys.Count == 0) return false;
            int nA = FindNearestNodeIndex(e.startPos, nodes);
            int nB = FindNearestNodeIndex(e.endPos, nodes);
            if (nA < 0 || nB < 0) return false;
            int lo = Mathf.Min(nA, nB);
            int hi = Mathf.Max(nA, nB);
            return mstEdgeKeys.Contains(new Vector2Int(lo, hi));
        }

        private static int FindLongestLoopEdgeAtNode(int nodeIdx,
            List<NodeData> nodes, List<EdgeData> edges, HashSet<Vector2Int> mstEdgeKeys)
        {
            if (nodeIdx < 0 || nodeIdx >= nodes.Count) return -1;
            Vector3 nodePos = nodes[nodeIdx].position;

            int bestIdx = -1;
            float bestLen = -1f;
            for (int i = 0; i < edges.Count; i++)
            {
                // 이 edge가 nodeIdx에 연결된 edge인지
                bool touches = Vector3.Distance(edges[i].startPos, nodePos) < 0.5f
                            || Vector3.Distance(edges[i].endPos, nodePos) < 0.5f;
                if (!touches) continue;

                // MST는 제외
                if (IsMSTEdge(edges[i], mstEdgeKeys, nodes)) continue;

                float len = Vector3.Distance(edges[i].startPos, edges[i].endPos);
                if (len > bestLen)
                {
                    bestLen = len;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }
    }
}
