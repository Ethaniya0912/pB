// ============================================================================
// FactionStateTags.cs
// Faction 측 4 카테고리 상태 Tag enum 통합 파일
// 영역: 컨셉트 (정적 정의)
// 항목: P-05 (4 Tag enum)
// ============================================================================
//
// 책임:
//   - Faction 측 4 카테고리 상태 Tag enum 통합 보관
//   - 모두 [Flags] uint 비트마스크 — 동시 다수 Tag 보유 가능
//   - FactionStateBits 측 4 필드 (moodBits / tacticalBits /
//     lifecycleBits / relationBits) 와 1:1 정합 — 동일 순서 유지
//
// 통합 사연 (S2 / E-01 정리):
//   기존 4 파일 (FactionMoodTag.cs / FactionTacticalTag.cs /
//                FactionLifecycleTag.cs / FactionRelationTag.cs)
//   측 분산 → 본 단일 파일 측 통합.
//
//   - 외부 호출자 영향 0 (namespace TDA.PB4.Faction.Tags + 타입명 동일)
//   - 정정 / 검수 측 4 파일 동시 점검 시 본 파일 단일 점검으로 일괄 갱신
//   - 신규 카테고리 추가 시 본 파일 측 1 회 정정
//
// 정합 순서 (Mood → Tactical → Lifecycle → Relation):
//   FactionStateBits 측 4 필드 / FactionDefaultMaskSO 측 4 ValidMask /
//   FactionWorldState 측 4 explicit 필드 / IFactionStateCommandReceiver
//   측 8 메서드 (Set × 4 + Force × 4) 측 모두 동일 순서 정합.
//
// 후속 추가 예정 (E-06 진입 시):
//   - public enum FactionStateTagCategory { Mood, Tactical, Lifecycle, Relation }
//     → FactionBridgeDiagnostics 측 카운터 카테고리 키 / Dashboard 측 분류 키
// ============================================================================

using System;

namespace TDA.PB4.Faction.Tags
{
    // ═══════════════════════════════════════════════════════════════════
    // ① Mood — 감정 상태 카테고리
    // ═══════════════════════════════════════════════════════════════════
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
        
        // 10~31 비트 — 예비 (확장용)
    }
    
    // ═══════════════════════════════════════════════════════════════════
    // ② Tactical — 전술 상태 카테고리
    // ═══════════════════════════════════════════════════════════════════
    /// <summary>
    /// Faction 의 전술 상태 카테고리.
    /// [Flags] uint 비트마스크.
    /// 시나리오 / AI 가 그룹 단위 행동 결정 시 참조.
    /// FactionStateBits.tacticalBits 에 저장.
    /// </summary>
    [Flags]
    public enum FactionTacticalTag : uint
    {
        NONE                  = 0u,
        
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
    
    // ═══════════════════════════════════════════════════════════════════
    // ③ Lifecycle — 생애 주기 카테고리
    // ═══════════════════════════════════════════════════════════════════
    /// <summary>
    /// Faction 의 생애 주기 카테고리.
    /// [Flags] uint 비트마스크.
    /// FactionWorldState.populationLevel / isDecimated / isDominant 측 자동 변환 결과.
    /// FactionStateBits.lifecycleBits 에 저장.
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
    
    // ═══════════════════════════════════════════════════════════════════
    // ④ Relation — 외부 (Player / 다른 Faction) 관계 카테고리
    // ═══════════════════════════════════════════════════════════════════
    /// <summary>
    /// Faction 의 외부 (Player / 다른 Faction) 와의 관계 카테고리.
    /// [Flags] uint 비트마스크.
    /// 비트 layout — bit 0~4 = Player 관계 / bit 5~7 = 의도된 gap /
    /// bit 8+ = Faction 간 관계.
    /// FactionStateBits.relationBits 에 저장.
    /// </summary>
    [Flags]
    public enum FactionRelationTag : uint
    {
        NONE                          = 0u,
        
        // Player 관계 (bit 0 ~ 4)
        RELATION_PLAYER_NEUTRAL       = 1u << 0,  // 중립
        RELATION_PLAYER_FRIENDLY      = 1u << 1,  // 우호
        RELATION_PLAYER_HOSTILE       = 1u << 2,  // 적대
        RELATION_PLAYER_FEARED        = 1u << 3,  // 플레이어 두려워함
        RELATION_PLAYER_REVERED       = 1u << 4,  // 플레이어 숭배
        
        // bit 5 ~ 7 = 의도된 gap (Player ↔ Faction 분리)
        
        // Faction 간 관계 (bit 8 ~ 12)
        RELATION_ALLIED               = 1u << 8,  // 동맹 보유
        RELATION_AT_WAR               = 1u << 9,  // 전쟁 중
        RELATION_TRIBUTARY            = 1u << 10, // 종속 / 조공
        RELATION_RIVAL                = 1u << 11, // 경쟁
        RELATION_ISOLATED             = 1u << 12, // 고립 (외교 단절)
        
        // 13~31 비트 — 예비
    }
    
    // ═══════════════════════════════════════════════════════════════════
    // ⑤ FactionStateTagCategory — 4 카테고리 측 인덱스 enum (★ E-06 신규)
    // ═══════════════════════════════════════════════════════════════════
    /// <summary>
    /// Faction 측 4 카테고리 측 인덱스 enum.
    /// FactionStateBits 측 4 필드 (moodBits / tacticalBits / lifecycleBits / relationBits)
    /// 측 정확한 순서 정합 — `int` 측 cast 측 본격 인덱스 측 정합 (0 ~ 3).
    /// 
    /// 사용처:
    ///   - WorldFactionStateManager 측 통계 카운터 측 카테고리 키
    ///   - FactionBridgeDiagnostics 측 본격 분류 키
    ///   - PB4DebugDashboard_v06 측 Faction 패널 측 4 시각 영역 키
    /// </summary>
    public enum FactionStateTagCategory : byte
    {
        Mood      = 0,
        Tactical  = 1,
        Lifecycle = 2,
        Relation  = 3,
    }
}
