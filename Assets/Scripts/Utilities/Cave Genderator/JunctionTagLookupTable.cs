// ===============================================================================
// 파일: JunctionTagLookupTable.cs
// 역할: [Phase 18] ★ Tag LookupTable + Application Log
//
// ★ 사용자 지적 반영:
//   - "왜 이 Tag가 부여됐는가" 추적 가능
//   - 중복 방지 (같은 ruleId 두 번 적용 시 경고)
//   - LookupTable 기반으로 condition 체크
//   - 디자이너 친화 (SO Inspector에서 rule 정의 가능)
//
// ★ Phase 19~20 후 — 4 Axis Role 매핑 + Tag 4 Category 활용
// ===============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaveSystem
{
    /// <summary>
    /// ★ Junction Context — TagRule condition 평가에 필요한 정보
    /// </summary>
    public struct JunctionContext
    {
        public ElementKind kind;
        public int elementIdx;
        public CaveNodeGraphBuilder graphBuilder;

        // ★ 자주 쓰는 데이터 사전 추출
        public int nodeRoomType;       // Node only
        public float nodeRadius;       // Node only
        public int nodeDegree;         // Node only

        public float edgeWidth;        // Passage only
        public float edgeLength;       // Passage only
        public PassageMetric.PassageJunctionType edgePassageType;  // Passage only
        public uint edgeLegacyTags;    // Passage only

        public PassageMetric.RiskLevel edgeRiskLevel;
        public int distinctBiomeCount; // Passage only — 통과한 biome 개수

        public bool wasRepositioned;   // ★ R1 (Phase 24)
        public bool wasDesperatelyPlaced; // ★ 옵션 B
        public bool wasChamberLocked;  // ★ N1 (Phase 14)
    }

    /// <summary>
    /// ★ Tag rule — condition 만족 시 tag 부여
    /// </summary>
    [Serializable]
    public struct TagRule
    {
        public string ruleId;        // ★ "narrow_at_branch_ambush"
        public string category;       // "Identity" / "State" / "Event" / "Potential"
        public string reasoning;      // ★ 사람이 읽는 설명
        public string phase;          // ★ "Phase14_N1" / "Phase15_5m" / "Phase24b_LookupTable" 등
        public int priority;          // 동시 적용 시 우선순위

        // ★ Inspector에서 condition을 SO로 정의 (Phase 18 단순 버전)
        // 실제 condition은 Predicate&lt;JunctionContext&gt; 으로 코드에서 매칭
        public ConditionKind conditionKind;
        public uint requiredFlags;    // condition 비트마스크
        public uint requiredFlags2;   // 보조 condition

        public uint identityToApply;  // 부여할 Identity tag
        public uint stateToApply;
        public uint eventToApply;
        public uint potentialToApply;
    }

    /// <summary>
    /// ★ Built-in condition 종류 (Phase 18 — 단순 enum 기반)
    /// </summary>
    public enum ConditionKind
    {
        AlwaysTrue,
        // Node
        NodeRoomTypeEquals,        // requiredFlags = roomType
        NodeRadiusGreaterThan,     // requiredFlags = float bits
        NodeDegreeGreaterEqual,    // requiredFlags = degree
        // Passage
        PassageTypeEquals,         // requiredFlags = (uint)PassageJunctionType
        PassageWidthInRange,       // requiredFlags = min, requiredFlags2 = max (float bits)
        PassageHasLegacyTag,       // requiredFlags = TAG_ bit
        PassageRiskCritical,
        PassageMultiBiome,         // distinctBiomeCount >= 3
        // Event triggers (★ Phase 14~24 메커니즘 적용 추적)
        WasRepositioned,
        WasDesperatelyPlaced,
        WasChamberLocked,
        // Combined
        BothPassageNarrowAndBranch  // ambush trigger
    }

    /// <summary>
    /// ★ JunctionTagLookupTable — ScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "JunctionTagLookupTable", menuName = "Cave/Junction Tag Lookup Table")]
    public class JunctionTagLookupTable : ScriptableObject
    {
        [Tooltip("★ Identity rules — Role 미러 (외부 노출용)")]
        public List<TagRule> identityRules = new List<TagRule>();

        [Tooltip("★ State rules — 현재 지형 상태")]
        public List<TagRule> stateRules = new List<TagRule>();

        [Tooltip("★ Event rules — 어떤 일이 있었는가 (★ 추적 + 중복 방지)")]
        public List<TagRule> eventRules = new List<TagRule>();

        [Tooltip("★ Potential rules — 외부 추론용 가능성")]
        public List<TagRule> potentialRules = new List<TagRule>();

        // ═════════════════════════════════════════════════════════════════
        // ★ ApplyAll — 모든 rule 적용 후 ContextTagSet + Log 반환
        // ═════════════════════════════════════════════════════════════════
        public ContextTagSet ApplyAll(JunctionContext ctx, out TagApplicationLog log)
        {
            var tags = new ContextTagSet();
            log = new TagApplicationLog();

            // ★ 우선순위 기반 정렬 적용
            ApplyRuleList(identityRules, ctx, ref tags, log, "Identity");
            ApplyRuleList(stateRules, ctx, ref tags, log, "State");
            ApplyRuleList(eventRules, ctx, ref tags, log, "Event");
            ApplyRuleList(potentialRules, ctx, ref tags, log, "Potential");

            return tags;
        }

        private void ApplyRuleList(List<TagRule> rules, JunctionContext ctx,
                                   ref ContextTagSet tags, TagApplicationLog log, string category)
        {
            // priority 내림차순
            var sorted = new List<TagRule>(rules);
            sorted.Sort((a, b) => b.priority.CompareTo(a.priority));

            foreach (var rule in sorted)
            {
                // ★ 중복 방지 — 같은 ruleId 이미 적용?
                if (log.HasRule(rule.ruleId))
                {
                    Debug.LogWarning($"[TagLookupTable] Duplicate rule '{rule.ruleId}' skipped");
                    continue;
                }

                if (!EvaluateCondition(rule, ctx)) continue;

                // ★ Tag 부여
                uint applied = 0;
                tags.identityTags |= rule.identityToApply;
                tags.stateTags |= rule.stateToApply;
                tags.eventTags |= rule.eventToApply;
                tags.potentialTags |= rule.potentialToApply;

                if (category == "Identity") applied = rule.identityToApply;
                else if (category == "State") applied = rule.stateToApply;
                else if (category == "Event") applied = rule.eventToApply;
                else if (category == "Potential") applied = rule.potentialToApply;

                // ★ 로그 기록
                log.entries.Add(new TagApplication
                {
                    ruleId = rule.ruleId,
                    category = category,
                    reasoning = rule.reasoning,
                    phase = rule.phase,
                    tagsApplied = applied
                });
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // ★ Condition 평가
        // ═════════════════════════════════════════════════════════════════
        private bool EvaluateCondition(TagRule rule, JunctionContext ctx)
        {
            switch (rule.conditionKind)
            {
                case ConditionKind.AlwaysTrue:
                    return true;

                // ─── Node ───
                case ConditionKind.NodeRoomTypeEquals:
                    return ctx.kind == ElementKind.Node &&
                           (uint)ctx.nodeRoomType == rule.requiredFlags;
                case ConditionKind.NodeRadiusGreaterThan:
                    if (ctx.kind != ElementKind.Node) return false;
                    return ctx.nodeRadius > BitConverter.Int32BitsToSingle((int)rule.requiredFlags);
                case ConditionKind.NodeDegreeGreaterEqual:
                    return ctx.kind == ElementKind.Node &&
                           ctx.nodeDegree >= (int)rule.requiredFlags;

                // ─── Passage ───
                case ConditionKind.PassageTypeEquals:
                    return ctx.kind == ElementKind.Passage &&
                           (uint)ctx.edgePassageType == rule.requiredFlags;
                case ConditionKind.PassageWidthInRange:
                    if (ctx.kind != ElementKind.Passage) return false;
                    float wMin = BitConverter.Int32BitsToSingle((int)rule.requiredFlags);
                    float wMax = BitConverter.Int32BitsToSingle((int)rule.requiredFlags2);
                    return ctx.edgeWidth >= wMin && ctx.edgeWidth < wMax;
                case ConditionKind.PassageHasLegacyTag:
                    return ctx.kind == ElementKind.Passage &&
                           (ctx.edgeLegacyTags & rule.requiredFlags) != 0;
                case ConditionKind.PassageRiskCritical:
                    return ctx.kind == ElementKind.Passage &&
                           ctx.edgeRiskLevel == PassageMetric.RiskLevel.Critical;
                case ConditionKind.PassageMultiBiome:
                    return ctx.kind == ElementKind.Passage && ctx.distinctBiomeCount >= 3;

                // ─── Event triggers ───
                case ConditionKind.WasRepositioned:
                    return ctx.wasRepositioned;
                case ConditionKind.WasDesperatelyPlaced:
                    return ctx.wasDesperatelyPlaced;
                case ConditionKind.WasChamberLocked:
                    return ctx.wasChamberLocked;

                // ─── Combined ───
                case ConditionKind.BothPassageNarrowAndBranch:
                    if (ctx.kind != ElementKind.Passage) return false;
                    return ctx.edgePassageType == PassageMetric.PassageJunctionType.Narrow &&
                           ctx.nodeDegree >= 3;
            }
            return false;
        }

        // ═════════════════════════════════════════════════════════════════
        // ★ Default rules 생성 (★ Phase 18 시작 시 SO에 자동 채움)
        // ═════════════════════════════════════════════════════════════════

        [ContextMenu("★ Populate Default Rules")]
        public void PopulateDefaultRules()
        {
            identityRules.Clear();
            stateRules.Clear();
            eventRules.Clear();
            potentialRules.Clear();

            // ─── Identity rules — Role 미러 ─────────────────────
            identityRules.Add(new TagRule
            {
                ruleId = "boss_role_mirror", category = "Identity",
                reasoning = "Boss role을 외부에 노출",
                phase = "Phase18_LookupTable", priority = 10,
                conditionKind = ConditionKind.NodeRoomTypeEquals,
                requiredFlags = 2,  // Boss
                identityToApply = (uint)IdentityTag.IDENTITY_BOSS
            });
            identityRules.Add(new TagRule
            {
                ruleId = "spawn_role_mirror", category = "Identity",
                reasoning = "Spawn role 외부 노출",
                phase = "Phase18_LookupTable", priority = 10,
                conditionKind = ConditionKind.NodeRoomTypeEquals,
                requiredFlags = 1,
                identityToApply = (uint)IdentityTag.IDENTITY_SPAWN
            });
            identityRules.Add(new TagRule
            {
                ruleId = "treasure_role_mirror", category = "Identity",
                reasoning = "Treasure role 외부 노출",
                phase = "Phase18_LookupTable", priority = 10,
                conditionKind = ConditionKind.NodeRoomTypeEquals,
                requiredFlags = 3,
                identityToApply = (uint)IdentityTag.IDENTITY_TREASURE
            });
            identityRules.Add(new TagRule
            {
                ruleId = "hub_role_mirror", category = "Identity",
                reasoning = "Hub role (degree ≥ 3) 외부 노출",
                phase = "Phase18_LookupTable", priority = 8,
                conditionKind = ConditionKind.NodeDegreeGreaterEqual,
                requiredFlags = 3,
                identityToApply = (uint)IdentityTag.IDENTITY_HUB
            });

            // ─── State rules — Width 4 단계 (★ Phase 15) ─────
            stateRules.Add(new TagRule
            {
                ruleId = "state_narrow", category = "State",
                reasoning = "Narrow 통로 (5~7m)",
                phase = "Phase15_5m", priority = 10,
                conditionKind = ConditionKind.PassageTypeEquals,
                requiredFlags = (uint)PassageMetric.PassageJunctionType.Narrow,
                stateToApply = (uint)StateTag.STATE_NARROW
            });
            stateRules.Add(new TagRule
            {
                ruleId = "state_standard", category = "State",
                reasoning = "Standard 통로 (7~10m, fallback)",
                phase = "Phase15_5m", priority = 10,
                conditionKind = ConditionKind.PassageTypeEquals,
                requiredFlags = (uint)PassageMetric.PassageJunctionType.Standard,
                stateToApply = (uint)StateTag.STATE_STANDARD
            });
            stateRules.Add(new TagRule
            {
                ruleId = "state_wide", category = "State",
                reasoning = "Wide 통로 (10~15m)",
                phase = "Phase15_5m", priority = 10,
                conditionKind = ConditionKind.PassageTypeEquals,
                requiredFlags = (uint)PassageMetric.PassageJunctionType.Wide,
                stateToApply = (uint)StateTag.STATE_WIDE
            });
            stateRules.Add(new TagRule
            {
                ruleId = "state_grand", category = "State",
                reasoning = "Grand 통로 (>15m, Boss 진입)",
                phase = "Phase15_5m", priority = 10,
                conditionKind = ConditionKind.PassageTypeEquals,
                requiredFlags = (uint)PassageMetric.PassageJunctionType.Grand,
                stateToApply = (uint)StateTag.STATE_GRAND
            });
            stateRules.Add(new TagRule
            {
                ruleId = "state_critical_risk", category = "State",
                reasoning = "RiskScore Critical 통로",
                phase = "Phase18_LookupTable", priority = 5,
                conditionKind = ConditionKind.PassageRiskCritical,
                stateToApply = (uint)StateTag.STATE_CRITICAL_RISK
            });
            stateRules.Add(new TagRule
            {
                ruleId = "state_multibiome", category = "State",
                reasoning = "★ 3+ biome 통과 (F15)",
                phase = "Phase18_LookupTable", priority = 5,
                conditionKind = ConditionKind.PassageMultiBiome,
                stateToApply = (uint)StateTag.STATE_MULTIBIOME | (uint)StateTag.STATE_BOUNDARY
            });

            // ─── Event rules — 메커니즘 적용 추적 ────────────
            eventRules.Add(new TagRule
            {
                ruleId = "event_chamber_locked", category = "Event",
                reasoning = "★ N1 (Phase 14)으로 chamber 단일화됨",
                phase = "Phase14_N1", priority = 10,
                conditionKind = ConditionKind.WasChamberLocked,
                eventToApply = (uint)EventTag.EVENT_CHAMBER_LOCKED
            });
            eventRules.Add(new TagRule
            {
                ruleId = "event_repositioned", category = "Event",
                reasoning = "★ R1 (Phase 24)으로 사전 회피 이동",
                phase = "Phase24_R1", priority = 10,
                conditionKind = ConditionKind.WasRepositioned,
                eventToApply = (uint)EventTag.EVENT_REPOSITIONED
            });
            eventRules.Add(new TagRule
            {
                ruleId = "event_desperately_placed", category = "Event",
                reasoning = "★ 옵션 B desperately quota 부득이 boundary 배치",
                phase = "OptionB_Desperate", priority = 10,
                conditionKind = ConditionKind.WasDesperatelyPlaced,
                eventToApply = (uint)EventTag.EVENT_DESPERATELY_PLACED
            });
            eventRules.Add(new TagRule
            {
                ruleId = "event_sub5m_violation", category = "Event",
                reasoning = "★ 5m 정책 위반 통로 (Phase 15)",
                phase = "Phase15_5m", priority = 10,
                conditionKind = ConditionKind.PassageTypeEquals,
                requiredFlags = (uint)PassageMetric.PassageJunctionType.Sub5mEmergency,
                eventToApply = (uint)EventTag.EVENT_SUB5M_VIOLATION
            });

            // ─── Potential rules — 외부 추론용 ────────────────
            potentialRules.Add(new TagRule
            {
                ruleId = "potential_ambush_narrow_branch", category = "Potential",
                reasoning = "★ Narrow + 분기 → AI 매복 위치 후보",
                phase = "Phase18_LookupTable", priority = 8,
                conditionKind = ConditionKind.BothPassageNarrowAndBranch,
                potentialToApply = (uint)PotentialTag.POTENTIAL_AMBUSH | (uint)PotentialTag.POTENTIAL_BOTTLENECK
            });
            potentialRules.Add(new TagRule
            {
                ruleId = "potential_sacred_venue_bridge", category = "Potential",
                reasoning = "★ Bridge passage → 외부 Sacred 결정 후보",
                phase = "Phase18_LookupTable", priority = 7,
                conditionKind = ConditionKind.PassageTypeEquals,
                requiredFlags = (uint)PassageMetric.PassageJunctionType.Bridge,
                potentialToApply = (uint)PotentialTag.POTENTIAL_SACRED_VENUE
            });
            potentialRules.Add(new TagRule
            {
                ruleId = "potential_rush_grand", category = "Potential",
                reasoning = "Grand passage → 빠른 진입/추격 후보",
                phase = "Phase18_LookupTable", priority = 6,
                conditionKind = ConditionKind.PassageTypeEquals,
                requiredFlags = (uint)PassageMetric.PassageJunctionType.Grand,
                potentialToApply = (uint)PotentialTag.POTENTIAL_RUSH_DOWN |
                                  (uint)PotentialTag.POTENTIAL_CHASE_SCENE
            });
            potentialRules.Add(new TagRule
            {
                ruleId = "potential_fall_hazard_bridge", category = "Potential",
                reasoning = "Bridge → 낙하 위험",
                phase = "Phase18_LookupTable", priority = 7,
                conditionKind = ConditionKind.PassageTypeEquals,
                requiredFlags = (uint)PassageMetric.PassageJunctionType.Bridge,
                potentialToApply = (uint)PotentialTag.POTENTIAL_FALL_HAZARD
            });

            Debug.Log($"[TagLookupTable] Populated {identityRules.Count} identity, {stateRules.Count} state, " +
                      $"{eventRules.Count} event, {potentialRules.Count} potential rules");
        }
    }
}
