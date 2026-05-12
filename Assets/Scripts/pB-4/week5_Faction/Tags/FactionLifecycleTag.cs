// ============================================================================
// FactionLifecycleTag.cs
// 생애 주기 카테고리 [Flags] uint enum
// 영역: 컨셉트 (정적 정의)
// 항목: P-05 (4 Tag enum 중 3)
// ============================================================================

using System;

namespace TDA.PB4.Faction.Tags
{
    /// <summary>
    /// Faction 의 생애 주기 카테고리.
    /// [Flags] uint 비트마스크.
    /// FactionWorldState.populationLevel 등의 자동 변환 결과.
    /// </summary>
    [Flags]
    public enum FactionLifecycleTag : uint
    {
        NONE                  = 0u,
        
        LIFECYCLE_DORMANT     = 1u << 0,  // 휴면 (인구 < 10%)
        LIFECYCLE_THREATENED  = 1u << 1,  // 위협 (인구 < 30%)
        LIFECYCLE_ACTIVE      = 1u << 2,  // 활성 (인구 정상)
        LIFECYCLE_FLOURISHING = 1u << 3,  // 번성 (인구 > 70%)
        LIFECYCLE_DOMINANT    = 1u << 4,  // 우세 (영토 다수 점유)
        LIFECYCLE_DEFEATED    = 1u << 5,  // 패배 (isDecimated)
        LIFECYCLE_SPLITTING   = 1u << 6,  // 분할 중 (P-17 분할 6 단계 중)
        LIFECYCLE_EMERGING    = 1u << 7,  // 신생 (분할 후 새 펙션)
        LIFECYCLE_EXTINCT     = 1u << 8,  // 멸종
        
        // 9~31 비트 — 예비
    }
}
