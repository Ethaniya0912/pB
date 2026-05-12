// ============================================================================
// FactionRelationTag.cs
// 관계 카테고리 [Flags] uint enum
// 영역: 컨셉트 (정적 정의)
// 항목: P-05 (4 Tag enum 중 4)
// ============================================================================

using System;

namespace TDA.PB4.Faction.Tags
{
    /// <summary>
    /// Faction 의 외부 (Player / 다른 Faction) 와의 관계 카테고리.
    /// [Flags] uint 비트마스크.
    /// </summary>
    [Flags]
    public enum FactionRelationTag : uint
    {
        NONE                          = 0u,
        
        // Player 관계
        RELATION_PLAYER_NEUTRAL       = 1u << 0,  // 중립
        RELATION_PLAYER_FRIENDLY      = 1u << 1,  // 우호
        RELATION_PLAYER_HOSTILE       = 1u << 2,  // 적대
        RELATION_PLAYER_FEARED        = 1u << 3,  // 플레이어 두려워함
        RELATION_PLAYER_REVERED       = 1u << 4,  // 플레이어 숭배
        
        // Faction 간 관계
        RELATION_ALLIED               = 1u << 8,  // 동맹 보유
        RELATION_AT_WAR               = 1u << 9,  // 전쟁 중
        RELATION_TRIBUTARY            = 1u << 10, // 종속 / 조공
        RELATION_RIVAL                = 1u << 11, // 경쟁
        RELATION_ISOLATED             = 1u << 12, // 고립 (외교 단절)
        
        // 13~31 비트 — 예비
    }
}
