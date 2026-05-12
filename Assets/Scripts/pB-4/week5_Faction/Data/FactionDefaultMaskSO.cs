// ============================================================================
// FactionDefaultMaskSO.cs
// Faction 의 기본 프리셋 / 디자이너 가이드 ScriptableObject
// 영역: 컨셉트 (정적 정의)
// 항목: P-01b
// ============================================================================
//
// 책임:
//   - 4 Tag validMask (Mood/Tactical/Lifecycle/Relation) 정의
//   - 디자인 의도 텍스트 보관
//
// 권한:
//   - AI 자율 변경 (WorldFactionStateManager.SetMood) → ✓ validMask 검사
//   - 시나리오 명시 (WorldFactionStateManager.ForceMood) → ✗ validMask 우회
//   - Mapper 자동 변환 → ✓ validMask 적용
//   - FactionWorldState.explicitXxxBits → ✗ validMask 우회
//
// 종족별 .asset 권장:
//   - FactionDefaultMask_Goblin.asset
//   - FactionDefaultMask_Orc.asset
//   - FactionDefaultMask_Skeleton.asset
//
// 절대규칙 #3 회피: 기존 5 SO (Wk0 동결) 변경 X. 신규 SO 신설.
// ============================================================================

using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(
        menuName = "pB4/Faction/FactionDefaultMask",
        fileName = "FactionDefaultMask_NewFaction")]
    public class FactionDefaultMaskSO : ScriptableObject
    {
        // ────────────────────────────────────────────────────────
        // 4 Tag 기본 프리셋 (디자이너 가이드)
        // ────────────────────────────────────────────────────────
        
        [Header("━━━ 4 Tag 기본 프리셋 (디자이너 가이드) ━━━")]
        
        [Tooltip("AI 자율 발현 (SetMood) 이 가질 수 있는 Mood Tag. " +
                 "0xFFFFFFFF = 모두 가능 / 0 = 모두 불가능. " +
                 "시나리오 ForceMood 또는 explicitMoodBits 는 본 mask 우회.")]
        [SerializeField] private uint _validMoodMask      = 0xFFFFFFFFu;
        
        [Tooltip("AI 자율 발현 (SetTactical) 이 가질 수 있는 Tactical Tag.")]
        [SerializeField] private uint _validTacticalMask  = 0xFFFFFFFFu;
        
        [Tooltip("AI 자율 발현 (SetLifecycle) 이 가질 수 있는 Lifecycle Tag.")]
        [SerializeField] private uint _validLifecycleMask = 0xFFFFFFFFu;
        
        [Tooltip("AI 자율 발현 (SetRelation) 이 가질 수 있는 Relation Tag.")]
        [SerializeField] private uint _validRelationMask  = 0xFFFFFFFFu;
        
        public uint ValidMoodMask      => _validMoodMask;
        public uint ValidTacticalMask  => _validTacticalMask;
        public uint ValidLifecycleMask => _validLifecycleMask;
        public uint ValidRelationMask  => _validRelationMask;
        
        // ────────────────────────────────────────────────────────
        // 디자인 의도 텍스트
        // ────────────────────────────────────────────────────────
        
        [Header("━━━ 디자인 의도 (Inspector 메모) ━━━")]
        
        [Tooltip("이 Faction 의 기본 성격 / 디자이너 메모. 코드 동작에 영향 X.")]
        [SerializeField, TextArea(4, 8)] private string _designIntent = "";
        
        public string DesignIntent => _designIntent;
        
        // 예시:
        // Goblin     = "얍삽한 약자 무리. 적이 강하면 도망. MOOD_FEARFUL/PANIC 친화도 높음.
        //               전투 시 PANIC 자주 / Loyal_To_King 불가능."
        // Orc        = "전략적 호전 무리. AGGRESSIVE/CONFIDENT 자주. 후퇴 임계 높음.
        //               PANIC 어렵 / 전쟁 선언 우호."
        // Skeleton   = "감정 없는 언데드. 감정 Mood 거의 없음 (MOOD_VENGEFUL 만 가능).
        //               매복 / 보초 위주. RETREATING 불가능."
    }
}
