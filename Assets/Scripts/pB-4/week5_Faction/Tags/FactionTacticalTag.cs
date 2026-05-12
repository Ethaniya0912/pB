// ============================================================================
// FactionTacticalTag.cs
// 전술 카테고리 [Flags] uint enum
// 영역: 컨셉트 (정적 정의)
// 항목: P-05 (4 Tag enum 중 2)
// ============================================================================

using System;

namespace TDA.PB4.Faction.Tags
{
    /// <summary>
    /// Faction 의 전술 상태 카테고리.
    /// [Flags] uint 비트마스크.
    /// 시나리오 / AI 가 그룹 단위 행동 결정 시 참조.
    /// </summary>
    [Flags]
    public enum FactionTacticalTag : uint
    {
        NONE                = 0u,
        
        TACTICAL_OFFENSIVE    = 1u << 0,  // 공세
        TACTICAL_DEFENSIVE    = 1u << 1,  // 방어
        TACTICAL_RETREATING   = 1u << 2,  // 후퇴 중
        TACTICAL_AMBUSH       = 1u << 3,  // 매복
        TACTICAL_PATROL       = 1u << 4,  // 정찰
        TACTICAL_GUARDING     = 1u << 5,  // 경비
        TACTICAL_MIGRATING    = 1u << 6,  // 이동 중
        TACTICAL_HOLDING      = 1u << 7,  // 진지 사수
        TACTICAL_BESIEGING    = 1u << 8,  // 포위 / 공성
        TACTICAL_SCATTERED    = 1u << 9,  // 분산
        
        // 10~31 비트 — 예비
    }
}
