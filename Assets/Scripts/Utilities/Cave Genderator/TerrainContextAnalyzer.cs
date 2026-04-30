// ===============================================================================
// 파일: TerrainContextAnalyzer.cs
// 역할: [Phase 17] ★ IJunctionContextAnalyzer 구현체 — Cave Generator 내부
// 도입: Tier 2 — 지형 정보 시스템
//
// ★ 책임:
//   - Phase 14~15에서 도입된 Junction Type / Tag / Role 정보 캐시
//   - Phase 18 도입 후 TagLookupTable 적용 + Application Log 갱신
//   - IJunctionContextAnalyzer 인터페이스 구현 — 외부 시스템에 제공
//
// ★ 사용:
//   [SerializeField] private TerrainContextAnalyzer terrain;
//   IJunctionContextAnalyzer api = terrain;
//   bool isBoss = api.HasIdentity(idx, ElementKind.Node, IdentityTag.IDENTITY_BOSS);
// ===============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace CaveSystem
{
    /// <summary>
    /// ★ Junction System v4 — Cave Generator 내부 구현체
    /// </summary>
    public class TerrainContextAnalyzer : MonoBehaviour, IJunctionContextAnalyzer
    {
        [SerializeField] private CaveNodeGraphBuilder graphBuilder;

        // ★ Phase 18 — TagLookupTable SO 주입
        [SerializeField, Tooltip("★ Phase 18 — Junction Tag Lookup Table (★ Inspector에서 SO 할당)")]
        private JunctionTagLookupTable tagLUT;

        // ─── 캐시 (★ Phase 17 단계 — 기본 데이터만) ───
        private ContextTagSet[] nodeTagsCache;
        private ContextTagSet[] edgeTagsCache;
        private ContextTagSet[] regionTagsCache;
        private ContextTagSet[] crossoverTagsCache;

        private TagApplicationLog[] nodeLogCache;
        private TagApplicationLog[] edgeLogCache;
        private TagApplicationLog[] regionLogCache;
        private TagApplicationLog[] crossoverLogCache;

        private bool isReady = false;
        public bool IsReady => isReady;

        // ═════════════════════════════════════════════════════════════════
        // ★ Auto-find graphBuilder + Rebuild on demand
        //   - Auto: GenerateGraphInternal 끝에서 자동 호출 (Phase 17 Option B)
        //   - Manual: Inspector ContextMenu "★ Rebuild Now" (Phase 17 Option A)
        //   - Visualizer 버튼: "Rebuild Now" (Phase 17 Option C)
        // ═════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (graphBuilder == null)
                graphBuilder = CaveNodeGraphBuilder.Instance;
        }

        [ContextMenu("★ Rebuild Now")]
        public void Rebuild()
        {
            if (graphBuilder == null)
            {
                graphBuilder = CaveNodeGraphBuilder.Instance;
                if (graphBuilder == null)
                {
                    Debug.LogWarning("[TerrainContextAnalyzer] graphBuilder not found");
                    return;
                }
            }
            // ─── Node 캐시 ───
            int nodeCount = graphBuilder.nodesData?.Count ?? 0;
            nodeTagsCache = new ContextTagSet[nodeCount];
            nodeLogCache = new TagApplicationLog[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                if (tagLUT != null)
                {
                    // ★ Phase 18 — LookupTable 적용
                    var ctx = BuildJunctionContext(ElementKind.Node, i);
                    nodeTagsCache[i] = tagLUT.ApplyAll(ctx, out nodeLogCache[i]);
                }
                else
                {
                    // ★ Phase 17 fallback — 기본 mapping
                    nodeTagsCache[i] = BuildNodeTags(i);
                    nodeLogCache[i] = new TagApplicationLog();
                }
            }

            // ─── Edge 캐시 ───
            int edgeCount = graphBuilder.edgesData?.Count ?? 0;
            edgeTagsCache = new ContextTagSet[edgeCount];
            edgeLogCache = new TagApplicationLog[edgeCount];
            for (int i = 0; i < edgeCount; i++)
            {
                if (tagLUT != null)
                {
                    var ctx = BuildJunctionContext(ElementKind.Passage, i);
                    edgeTagsCache[i] = tagLUT.ApplyAll(ctx, out edgeLogCache[i]);
                }
                else
                {
                    edgeTagsCache[i] = BuildEdgeTags(i);
                    edgeLogCache[i] = new TagApplicationLog();
                }
            }

            // ★ Phase 25/26 후 Region/Crossover 캐시 활성화
            regionTagsCache = new ContextTagSet[0];
            regionLogCache = new TagApplicationLog[0];
            crossoverTagsCache = new ContextTagSet[0];
            crossoverLogCache = new TagApplicationLog[0];

            isReady = true;

            // ★ Phase 17 — Rebuild 통계 로그
            int totalIdentity = 0, totalState = 0, totalEvent = 0, totalPotential = 0;
            for (int i = 0; i < (nodeTagsCache?.Length ?? 0); i++)
            {
                if (nodeTagsCache[i].identityTags != 0) totalIdentity++;
                if (nodeTagsCache[i].stateTags != 0) totalState++;
                if (nodeTagsCache[i].eventTags != 0) totalEvent++;
                if (nodeTagsCache[i].potentialTags != 0) totalPotential++;
            }
            for (int i = 0; i < (edgeTagsCache?.Length ?? 0); i++)
            {
                if (edgeTagsCache[i].identityTags != 0) totalIdentity++;
                if (edgeTagsCache[i].stateTags != 0) totalState++;
                if (edgeTagsCache[i].eventTags != 0) totalEvent++;
                if (edgeTagsCache[i].potentialTags != 0) totalPotential++;
            }
            Debug.Log($"<color=cyan>[TerrainContextAnalyzer]</color> Rebuild ✓ " +
                      $"nodes={(nodeTagsCache?.Length ?? 0)}, edges={(edgeTagsCache?.Length ?? 0)} | " +
                      $"Tags applied: Identity={totalIdentity}, State={totalState}, " +
                      $"Event={totalEvent}, Potential={totalPotential} | " +
                      $"LUT={(tagLUT != null ? "✓" : "fallback")}");
        }

        // ═════════════════════════════════════════════════════════════════
        // ★ Phase 18 — JunctionContext 빌더 (LookupTable 입력)
        // ═════════════════════════════════════════════════════════════════
        private JunctionContext BuildJunctionContext(ElementKind kind, int idx)
        {
            var ctx = new JunctionContext
            {
                kind = kind,
                elementIdx = idx,
                graphBuilder = graphBuilder
            };

            if (kind == ElementKind.Node && graphBuilder.nodesData != null &&
                idx >= 0 && idx < graphBuilder.nodesData.Count)
            {
                var n = graphBuilder.nodesData[idx];
                ctx.nodeRoomType = n.roomType;
                ctx.nodeRadius = n.radius;
                ctx.nodeDegree = ComputeNodeDegree(idx);
            }
            else if (kind == ElementKind.Passage)
            {
                var profiles = graphBuilder.lastPassageProfiles;
                if (profiles != null && idx >= 0 && idx < profiles.Count)
                {
                    var p = profiles[idx];
                    ctx.edgeWidth = profiles[idx].segments != null && profiles[idx].segments.Length > 0
                        ? AvgSegmentWidth(p) : 0f;
                    ctx.edgeLength = p.length;
                    ctx.edgePassageType = p.inferredType;
                    ctx.edgeLegacyTags = p.tags;
                    ctx.edgeRiskLevel = p.risk.level;
                    ctx.distinctBiomeCount = CountDistinctBiomes(p);

                    // ★ Edge가 연결된 노드 degree (★ for Bottleneck/Ambush 검출)
                    //   EdgeData에 startNodeIdx 없으므로 startPos/endPos 매칭
                    if (graphBuilder.edgesData != null && idx < graphBuilder.edgesData.Count)
                    {
                        var e = graphBuilder.edgesData[idx];
                        int idxA = FindNodeIdxByPos(e.startPos);
                        int idxB = FindNodeIdxByPos(e.endPos);
                        int dA = idxA >= 0 ? ComputeNodeDegree(idxA) : 0;
                        int dB = idxB >= 0 ? ComputeNodeDegree(idxB) : 0;
                        ctx.nodeDegree = Mathf.Max(dA, dB);  // ★ 더 큰 분기
                    }
                }
            }

            // ★ Phase 14~24 메커니즘 적용 추적은 graphBuilder의 별도 flag에서
            // (현재는 default false — Phase 19/24에서 noeData 확장 후 활성)
            ctx.wasRepositioned = false;
            ctx.wasDesperatelyPlaced = false;
            ctx.wasChamberLocked = false;

            return ctx;
        }

        private int ComputeNodeDegree(int nodeIdx)
        {
            if (graphBuilder.edgesData == null || graphBuilder.nodesData == null) return 0;
            if (nodeIdx < 0 || nodeIdx >= graphBuilder.nodesData.Count) return 0;

            Vector3 nodePos = graphBuilder.nodesData[nodeIdx].position;
            int deg = 0;
            for (int i = 0; i < graphBuilder.edgesData.Count; i++)
            {
                var e = graphBuilder.edgesData[i];
                // ★ position 매칭 (EdgeData에 startNodeIdx 없음)
                if (Vector3.SqrMagnitude(e.startPos - nodePos) < 0.01f ||
                    Vector3.SqrMagnitude(e.endPos - nodePos) < 0.01f)
                {
                    deg++;
                }
            }
            return deg;
        }

        private int FindNodeIdxByPos(Vector3 pos)
        {
            if (graphBuilder.nodesData == null) return -1;
            for (int i = 0; i < graphBuilder.nodesData.Count; i++)
            {
                if (Vector3.SqrMagnitude(graphBuilder.nodesData[i].position - pos) < 0.01f)
                    return i;
            }
            return -1;
        }

        private float AvgSegmentWidth(PassageMetric.PassageProfile p)
        {
            if (p.segments == null || p.segments.Length == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < p.segments.Length; i++) sum += p.segments[i].localWidth;
            return sum / p.segments.Length;
        }

        private int CountDistinctBiomes(PassageMetric.PassageProfile p)
        {
            if (p.segments == null || p.segments.Length == 0) return 0;
            var seen = new HashSet<int>();
            for (int i = 0; i < p.segments.Length; i++) seen.Add(p.segments[i].biomeIdx);
            return seen.Count;
        }

        // ═════════════════════════════════════════════════════════════════
        // ★ Phase 17 기본 — 기존 Role + Junction Type → Tag 4 Category mapping
        // (Phase 18 LookupTable 활성 시 더 풍부한 rule 적용)
        // ═════════════════════════════════════════════════════════════════

        private ContextTagSet BuildNodeTags(int idx)
        {
            var tags = new ContextTagSet();
            if (idx < 0 || idx >= (graphBuilder.nodesData?.Count ?? 0)) return tags;

            var node = graphBuilder.nodesData[idx];

            // Identity from RoomType
            switch (node.roomType)
            {
                case 1: tags.identityTags |= (uint)IdentityTag.IDENTITY_SPAWN; break;
                case 2: tags.identityTags |= (uint)IdentityTag.IDENTITY_BOSS; break;
                case 3: tags.identityTags |= (uint)IdentityTag.IDENTITY_TREASURE; break;
                case 4: tags.identityTags |= (uint)IdentityTag.IDENTITY_HUB; break;
            }

            // State — radius 기반 (★ Phase 27 NodeJunction 후 정확화)
            if (node.radius >= 12f) tags.stateTags |= (uint)StateTag.STATE_OPEN;
            else if (node.radius < 6f) tags.stateTags |= (uint)StateTag.STATE_ENCLOSED;

            return tags;
        }

        private ContextTagSet BuildEdgeTags(int idx)
        {
            var tags = new ContextTagSet();
            var profiles = graphBuilder.lastPassageProfiles;
            if (profiles == null || idx < 0 || idx >= profiles.Count) return tags;

            var profile = profiles[idx];

            // ─── State from PassageJunctionType (★ Phase 15 5m 정책) ───
            switch (profile.inferredType)
            {
                case PassageMetric.PassageJunctionType.Narrow:
                    tags.stateTags |= (uint)StateTag.STATE_NARROW;
                    break;
                case PassageMetric.PassageJunctionType.Standard:
                    tags.stateTags |= (uint)StateTag.STATE_STANDARD;
                    break;
                case PassageMetric.PassageJunctionType.Wide:
                    tags.stateTags |= (uint)StateTag.STATE_WIDE;
                    break;
                case PassageMetric.PassageJunctionType.Grand:
                    tags.stateTags |= (uint)StateTag.STATE_GRAND;
                    break;
                case PassageMetric.PassageJunctionType.Chimney:
                    tags.stateTags |= (uint)StateTag.STATE_VERTICAL;
                    break;
                case PassageMetric.PassageJunctionType.Sub5mEmergency:
                    // ★ 5m 정책 위반 → Event 기록 (Phase 15)
                    tags.eventTags |= (uint)EventTag.EVENT_SUB5M_VIOLATION;
                    break;
            }

            // Identity from existing 6 tags (호환)
            uint legacyTags = profile.tags;
            if ((legacyTags & PassageMetric.TAG_SACRED) != 0)
                tags.identityTags |= (uint)IdentityTag.IDENTITY_APPROACH; // ★ Bridge → ApproachPath 후보
            if ((legacyTags & PassageMetric.TAG_DANGER) != 0)
                tags.identityTags |= (uint)IdentityTag.IDENTITY_HAZARD;

            // State — Risk
            if (profile.risk.level == PassageMetric.RiskLevel.Critical)
                tags.stateTags |= (uint)StateTag.STATE_CRITICAL_RISK;

            // ─── Potential 추론 (★ Phase 18 후 LookupTable로 더 풍부) ───
            // Bridge/Cathedral → Sacred Venue 후보
            if (profile.inferredType == PassageMetric.PassageJunctionType.Bridge)
                tags.potentialTags |= (uint)PotentialTag.POTENTIAL_SACRED_VENUE;
            // Narrow → Ambush 후보
            if (profile.inferredType == PassageMetric.PassageJunctionType.Narrow)
                tags.potentialTags |= (uint)PotentialTag.POTENTIAL_AMBUSH;
            // Grand → Rush Down 후보
            if (profile.inferredType == PassageMetric.PassageJunctionType.Grand)
                tags.potentialTags |= (uint)PotentialTag.POTENTIAL_RUSH_DOWN;

            return tags;
        }

        // ═════════════════════════════════════════════════════════════════
        // ★ IJunctionContextAnalyzer 구현
        // ═════════════════════════════════════════════════════════════════

        public bool HasIdentity(int idx, ElementKind kind, IdentityTag tag)
        {
            var tags = GetCacheByKind(idx, kind);
            return (tags.identityTags & (uint)tag) != 0;
        }

        public bool HasState(int idx, ElementKind kind, StateTag tag)
        {
            var tags = GetCacheByKind(idx, kind);
            return (tags.stateTags & (uint)tag) != 0;
        }

        public bool HasEvent(int idx, ElementKind kind, EventTag tag)
        {
            var tags = GetCacheByKind(idx, kind);
            return (tags.eventTags & (uint)tag) != 0;
        }

        public bool HasPotential(int idx, ElementKind kind, PotentialTag tag)
        {
            var tags = GetCacheByKind(idx, kind);
            return (tags.potentialTags & (uint)tag) != 0;
        }

        public ContextTagSet GetAllTags(int idx, ElementKind kind)
        {
            return GetCacheByKind(idx, kind);
        }

        // ─── Role getters ──────────────────────────────────────────
        public NodeRole GetNodeRole(int idx)
        {
            if (graphBuilder.nodesData == null || idx < 0 || idx >= graphBuilder.nodesData.Count)
                return NodeRole.Normal;
            int rt = graphBuilder.nodesData[idx].roomType;
            switch (rt)
            {
                case 1: return NodeRole.Spawn;
                case 2: return NodeRole.Boss;
                case 3: return NodeRole.Treasure;
                case 4: return NodeRole.Hub;
                default: return NodeRole.Normal;
            }
        }

        public PassageRole GetPassageRole(int idx)
        {
            // ★ Phase 19에서 edge.assignedRole 추가 후 활성
            // 현재는 PassageJunctionType 기반 추론
            var profiles = graphBuilder?.lastPassageProfiles;
            if (profiles == null || idx < 0 || idx >= profiles.Count)
                return PassageRole.ConnectorPath;
            switch (profiles[idx].inferredType)
            {
                case PassageMetric.PassageJunctionType.Bridge:
                    return PassageRole.SacredCorridor;  // Sacred 후보지만 외부 결정
                case PassageMetric.PassageJunctionType.Collapse:
                    return PassageRole.HazardPath;
                case PassageMetric.PassageJunctionType.Narrow:
                    return PassageRole.ChallengePath;
                case PassageMetric.PassageJunctionType.Wide:
                case PassageMetric.PassageJunctionType.Grand:
                    return PassageRole.VistaPath;
                default:
                    return PassageRole.ConnectorPath;
            }
        }

        public RegionRole GetRegionRole(int idx) => RegionRole.Default;  // ★ Phase 26
        public CrossoverRole GetCrossoverRole(int idx) => CrossoverRole.Junction;  // ★ Phase 25

        // ─── Possible Role (★ Phase 27에서 정확화) ────────────────
        public NodeRole[] GetPossibleNodeRoles(int idx)
        {
            // 현재 NodeJunctionType 미구현 → 기본 후보만
            var current = GetNodeRole(idx);
            return new[] { current };
        }
        public PassageRole[] GetPossiblePassageRoles(int idx)
        {
            var current = GetPassageRole(idx);
            return new[] { current };
        }
        public RegionRole[] GetPossibleRegionRoles(int idx) => new[] { RegionRole.Default };
        public CrossoverRole[] GetPossibleCrossoverRoles(int idx) => new[] { CrossoverRole.Junction };

        // ─── Tag History ─────────────────────────────────────────
        public TagApplicationLog GetTagHistory(int idx, ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Node:
                    if (nodeLogCache != null && idx >= 0 && idx < nodeLogCache.Length)
                        return nodeLogCache[idx];
                    break;
                case ElementKind.Passage:
                    if (edgeLogCache != null && idx >= 0 && idx < edgeLogCache.Length)
                        return edgeLogCache[idx];
                    break;
                case ElementKind.Region:
                    if (regionLogCache != null && idx >= 0 && idx < regionLogCache.Length)
                        return regionLogCache[idx];
                    break;
                case ElementKind.Crossover:
                    if (crossoverLogCache != null && idx >= 0 && idx < crossoverLogCache.Length)
                        return crossoverLogCache[idx];
                    break;
            }
            return new TagApplicationLog();
        }

        // ─── ScenarioContext enumerate ───────────────────────────
        public List<ScenarioContext> EnumerateContexts(int idx, ElementKind kind)
        {
            var result = new List<ScenarioContext>();
            var tags = GetCacheByKind(idx, kind);

            // ★ IDENTITY_BOSS + POTENTIAL_SACRED_VENUE → BossBattle + SacredRitual
            if ((tags.identityTags & (uint)IdentityTag.IDENTITY_BOSS) != 0 &&
                (tags.potentialTags & (uint)PotentialTag.POTENTIAL_SACRED_VENUE) != 0)
            {
                result.Add(new ScenarioContext
                {
                    kind = "BossBattle", priority = 10,
                    source = "IDENTITY_BOSS + POTENTIAL_SACRED_VENUE"
                });
                result.Add(new ScenarioContext
                {
                    kind = "SacredRitual", priority = 8,
                    source = "POTENTIAL_SACRED_VENUE"
                });
            }
            // POTENTIAL_AMBUSH → AmbushSpawn
            if ((tags.potentialTags & (uint)PotentialTag.POTENTIAL_AMBUSH) != 0)
            {
                result.Add(new ScenarioContext
                {
                    kind = "AmbushSpawn", priority = 6,
                    source = "POTENTIAL_AMBUSH"
                });
            }
            // POTENTIAL_HIDDEN_LOOT → SecretLoot
            if ((tags.potentialTags & (uint)PotentialTag.POTENTIAL_HIDDEN_LOOT) != 0)
            {
                result.Add(new ScenarioContext
                {
                    kind = "SecretLoot", priority = 5,
                    source = "POTENTIAL_HIDDEN_LOOT"
                });
            }

            return result;
        }

        // ─── Spatial query ────────────────────────────────────────
        public int[] FindNodesByIdentity(IdentityTag tag)
        {
            var result = new List<int>();
            if (nodeTagsCache != null)
            {
                for (int i = 0; i < nodeTagsCache.Length; i++)
                    if ((nodeTagsCache[i].identityTags & (uint)tag) != 0) result.Add(i);
            }
            return result.ToArray();
        }

        public int[] FindEdgesByPotential(PotentialTag tag)
        {
            var result = new List<int>();
            if (edgeTagsCache != null)
            {
                for (int i = 0; i < edgeTagsCache.Length; i++)
                    if ((edgeTagsCache[i].potentialTags & (uint)tag) != 0) result.Add(i);
            }
            return result.ToArray();
        }

        public int[] FindElementsByEvent(EventTag tag, ElementKind kind)
        {
            var result = new List<int>();
            ContextTagSet[] cache = null;
            switch (kind)
            {
                case ElementKind.Node: cache = nodeTagsCache; break;
                case ElementKind.Passage: cache = edgeTagsCache; break;
                case ElementKind.Region: cache = regionTagsCache; break;
                case ElementKind.Crossover: cache = crossoverTagsCache; break;
            }
            if (cache != null)
            {
                for (int i = 0; i < cache.Length; i++)
                    if ((cache[i].eventTags & (uint)tag) != 0) result.Add(i);
            }
            return result.ToArray();
        }

        // ─── Quantitative Metric (Stage 3-E/F 노출) ─────────────
        public PassageMetric.RiskScore GetRiskScore(int idx)
        {
            var profiles = graphBuilder?.lastPassageProfiles;
            if (profiles == null || idx < 0 || idx >= profiles.Count)
                return default(PassageMetric.RiskScore);
            return profiles[idx].risk;
        }

        public PassageMetric.PassageProfile GetPassageProfile(int idx)
        {
            var profiles = graphBuilder?.lastPassageProfiles;
            if (profiles == null || idx < 0 || idx >= profiles.Count)
                return default(PassageMetric.PassageProfile);
            return profiles[idx];
        }

        // ─── Cache helpers ─────────────────────────────────────
        private ContextTagSet GetCacheByKind(int idx, ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Node:
                    if (nodeTagsCache != null && idx >= 0 && idx < nodeTagsCache.Length)
                        return nodeTagsCache[idx];
                    break;
                case ElementKind.Passage:
                    if (edgeTagsCache != null && idx >= 0 && idx < edgeTagsCache.Length)
                        return edgeTagsCache[idx];
                    break;
                case ElementKind.Region:
                    if (regionTagsCache != null && idx >= 0 && idx < regionTagsCache.Length)
                        return regionTagsCache[idx];
                    break;
                case ElementKind.Crossover:
                    if (crossoverTagsCache != null && idx >= 0 && idx < crossoverTagsCache.Length)
                        return crossoverTagsCache[idx];
                    break;
            }
            return new ContextTagSet();
        }
    }
}
