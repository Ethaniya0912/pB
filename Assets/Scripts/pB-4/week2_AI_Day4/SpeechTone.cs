// =============================================================================
// SpeechTone.cs  |  pB-4 Week 2 Day 4
// Layer  : Data (enum + constants)
// Owner  : Person A
//
// 역할:
//   SpeechAssembler가 반환하는 Tone 분류.
//   DialogueRenderer가 Tone에 따라 DialoguePresetSO를 선택.
//
// 4계층 원칙:
//   - [Rule 2] 매직 넘버 금지 — "Warm" 문자열 대신 enum 사용.
//
// Week 7 확장 예약:
//   - 본 enum에 Glitched, Depersonalized 등 Trauma 발화용 Tone 추가 예정.
// =============================================================================
namespace TDA.PB4.AI.Speech
{
    /// <summary>발화 감정 톤. Renderer가 시각 스타일 결정에 사용.</summary>
    public enum SpeechTone
    {
        Warm = 0,        // agreeable 높음 — 친밀/애정
        Cold = 1,        // openness 낮음 — 냉담/거리감
        Neutral = 2,     // 기본/무표정
        Aggressive = 3,  // directness 높음 + agreeable 낮음 — 공격적
        Anxious = 4,     // fear 높음 (Trauma 연동) — 불안/떨림
    }

    /// <summary>
    /// SpeechTrigger 식별자 상수.
    /// NPCAlignmentController.TryTransition에서 defaultDialogueKey로 사용.
    /// [Rule 2] 매직 문자열 금지 — 모든 키는 이곳에서 관리.
    /// </summary>
    public static class SpeechTriggerIds
    {
        // Alignment 전이 기본 (4진영)
        public const string HOSTILE_DEFAULT    = "hostile_default";
        public const string NEUTRAL_DEFAULT    = "neutral_default";
        public const string FRIENDLY_DEFAULT   = "friendly_default";
        public const string COMPANION_DEFAULT  = "companion_default";

        // ★ D-06 신규 + 명명 갱신 (Wk5 D1) — GroupArchetype Collapse 발화 트리거 4종.
        // GroupArchetypeSO.onCollapseSpeechId 값과 매칭.
        // GroupAIManager.Escalation 측 OnEscalationTierChanged 시점 측 Collapse 진입 시 발화 (D-08).
        public const string GROUP_ARCHETYPE_FRENZY_COLLAPSE     = "group_archetype_frenzy_collapse";
        public const string GROUP_ARCHETYPE_COLD_LOGIC_COLLAPSE = "group_archetype_cold_logic_collapse";
        public const string GROUP_ARCHETYPE_DESPAIR_COLLAPSE    = "group_archetype_despair_collapse";
        public const string GROUP_ARCHETYPE_PARALYSIS_COLLAPSE  = "group_archetype_paralysis_collapse";

        // Week 7 확장 예약 (현재는 미사용):
        // public const string TRAUMA_SHOCK  = "trauma_acute_shock";
        // public const string PIVOT_MERCY   = "pivot_mercy";
        // public const string COMMAND_REFUSED = "cmd_refused";
    }
}
