// ============================================================================
// FactionMoodTag.cs
// 감정 카테고리 [Flags] uint enum
// 영역: 컨셉트 (정적 정의)
// 항목: P-05 (4 Tag enum 중 1)
// ============================================================================

using System;

namespace TDA.PB4.Faction.Tags
{
    /// <summary>
    /// Faction 의 감정 상태 카테고리.
    /// [Flags] uint 비트마스크 — 동시 다수 Tag 보유 가능.
    /// FactionStateBits.moodBits 에 저장.
    /// </summary>
    [Flags]
    public enum FactionMoodTag : uint
    {
        NONE                = 0u,
        
        MOOD_CALM           = 1u << 0,   // 평온
        MOOD_ALERT          = 1u << 1,   // 경계
        MOOD_AGGRESSIVE     = 1u << 2,   // 공격적
        MOOD_PANIC          = 1u << 3,   // 공포
        MOOD_FEARFUL        = 1u << 4,   // 두려움
        MOOD_VENGEFUL       = 1u << 5,   // 복수심
        MOOD_GRIEVING       = 1u << 6,   // 애도
        MOOD_CONFIDENT      = 1u << 7,   // 자신감
        MOOD_DESPERATE      = 1u << 8,   // 절박
        MOOD_LOYAL_TO_KING  = 1u << 9,   // 왕에게 충성 (인간형 펙션 전용)
        
        // 14~31 비트 — 예비 (확장용)
    }
}
