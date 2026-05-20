// ============================================================================
// FactionDefinitionSO.cs
// Faction 의 정적 SO 들을 모은 Inspector 진입점 ScriptableObject
// 영역: 컨셉트 (정적 정의)
// 항목: P-01
// ============================================================================
//
// 책임:
//   - 한 Faction (Goblin / Orc / Skeleton 등) 의 모든 정적 SO 참조 통합
//   - Inspector 에서 한 자산만 열면 모든 부속 SO 진입 가능
//   - ★ v1.2 (S2 / E-02) — 발화 트리거 ID 2 + Escalation 차등 1 자체 보유
//
// 4 Tag validMask + 디자인 의도는 FactionDefaultMaskSO (P-01b) 로 분리.
// 5 축 문화는 FactionCultureSO (P-10) — Wk5+ 단계.
// 임계값은 FactionThresholdConfigSO (P-12) — Wk5+ 단계.
//
// 기존 5 SO (Wk0 동결, 절대규칙 #3) 참조:
//   - FactionGroupPolicySO    (그룹 정책)
//   - FactionTierSO            (계층)
//   - MobFactionDataSO         (Mob 별 데이터)
//   - FactionCombatProfileSO   (전투 프로필)
//   - BiomeAffinitySO          (바이옴 친화도)
//
// 종족별 .asset 권장:
//   - FactionDefinition_Goblin.asset
//   - FactionDefinition_Orc.asset
//   - FactionDefinition_Skeleton.asset
// ============================================================================

using UnityEngine;
using TDA.PB4.AI;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(
        menuName = "pB4/Faction/FactionDefinition",
        fileName = "FactionDefinition_NewFaction")]
    public class FactionDefinitionSO : ScriptableObject
    {
        // ────────────────────────────────────────────────────────
        // Identity
        // ────────────────────────────────────────────────────────

        [Header("━━━ Identity ━━━")]

        [Tooltip("Faction 고유 식별자 (예: \"Goblin\", \"Orc\", \"Skeleton\")")]
        [SerializeField] private string _factionId = "";

        public string FactionId => _factionId;

        // ────────────────────────────────────────────────────────
        // 기존 5 SO 묶음 (Wk0 동결)
        // ────────────────────────────────────────────────────────

        [Header("━━━ 기존 5 SO (Wk0 동결, 절대규칙 #3) ━━━")]

        [Tooltip("그룹 정책 (사기 / 토큰 / panic chain / 포메이션 등)")]
        [SerializeField] private FactionGroupPolicySO _groupPolicy;

        [Tooltip("계층 분류")]
        [SerializeField] private FactionTierSO _tier;

        [Tooltip("Mob 별 데이터 (factionId / factionName / I·H·O·A)")]
        [SerializeField] private MobFactionDataSO _mobData;

        [Tooltip("전투 프로필 (Stalk / Strafe / Strike / Flee / Tactical / Perception)")]
        [SerializeField] private FactionCombatProfileSO _combatProfile;

        [Tooltip("바이옴 친화도 — 영토 자동 발현 핵심")]
        [SerializeField] private BiomeAffinitySO _biomeAffinity;

        public FactionGroupPolicySO GroupPolicy => _groupPolicy;
        public FactionTierSO Tier => _tier;
        public MobFactionDataSO MobData => _mobData;
        public FactionCombatProfileSO CombatProfile => _combatProfile;
        public BiomeAffinitySO BiomeAffinity => _biomeAffinity;

        // ────────────────────────────────────────────────────────
        // 부속 SO 참조 (별도 .asset)
        // ────────────────────────────────────────────────────────

        [Header("━━━ 부속 SO (별도 .asset) ━━━")]

        [Tooltip("기본 프리셋 / 디자이너 가이드 — 4 Tag validMask + 디자인 의도 (P-01b)")]
        [SerializeField] private FactionDefaultMaskSO _defaultMask;

        [Tooltip("(Wk5+) 5 축 문화 (P-10) — Brutality / Territoriality / Cohesion / Diplomacy / Cunning")]
        [SerializeField] private FactionCultureSO _culture;

        [Tooltip("(Wk5+) 임계값 (P-12) — 영토 확장 / 전쟁 / 방어 / 분할 임계")]
        [SerializeField] private FactionThresholdConfigSO _threshold;

        public FactionDefaultMaskSO DefaultMask => _defaultMask;
        public FactionCultureSO Culture => _culture;
        public FactionThresholdConfigSO Threshold => _threshold;

        // ────────────────────────────────────────────────────────
        // ★ v1.2 — 발화 트리거 (Lifecycle 진입 시) — E-02 / S2 신규
        // ────────────────────────────────────────────────────────

        [Header("━━━ v1.2 — 발화 트리거 (Lifecycle 진입 시) ━━━")]

        [Tooltip("종족 측 Lifecycle Emerging 진입 시 발화 트리거 ID. " +
                 "예: \"faction_goblin_emerging_enter\". " +
                 "ScenarioArcManager / SpeechAssembler 측 분기 키. " +
                 "빈 문자열 = 발화 없음 (안전 기본값).")]
        [SerializeField] private string _speechTriggerIdOnEnter = "";

        [Tooltip("종족 측 Lifecycle Decline 진입 시 발화 트리거 ID. " +
                 "예: \"faction_goblin_decline_enter\". " +
                 "쇠퇴 / 멸종 진행 시 발화. " +
                 "빈 문자열 = 발화 없음.")]
        [SerializeField] private string _speechTriggerIdOnDecline = "";

        public string SpeechTriggerIdOnEnter => _speechTriggerIdOnEnter;
        public string SpeechTriggerIdOnDecline => _speechTriggerIdOnDecline;

        // ────────────────────────────────────────────────────────
        // ★ v1.2 — Escalation 종족 차등 (S1 D-02 / V-B6-04 통과) — E-02 / S2 신규
        // ────────────────────────────────────────────────────────

        [Header("━━━ v1.2 — Escalation 차등 ━━━")]

        [Tooltip("종족 측 Escalation Tier 측 곱 인자 (Range 0.5 ~ 2.0). " +
                 "예: 오크 0.8 (느린 Esc) / 고블린 1.3 (빠른 Esc) / 스켈레톤 0.5 (매우 느림). " +
                 "GroupAIManager.Escalation 측 V-B6-04 통과 자료. " +
                 "기본 1.0 = 차등 없음.")]
        [Range(0.5f, 2.0f)]
        [SerializeField] private float _escalationModifier = 1.0f;

        public float EscalationModifier => _escalationModifier;
    }
}