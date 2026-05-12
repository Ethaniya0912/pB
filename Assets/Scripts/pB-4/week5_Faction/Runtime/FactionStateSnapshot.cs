// ============================================================================
// FactionStateSnapshot.cs
// 외부 도메인 전달용 Immutable DTO (readonly struct)
// 영역: 헬퍼
// 항목: P-04
// ============================================================================
//
// 책임:
//   - WorldFactionStateManager 의 내부 FactionStateBits 를 외부에 안전 노출
//   - readonly struct — 컴파일 시간 mutation 차단
//   - HasMood / HasTactical / HasLifecycle / HasRelation 검사 메서드
//
// 사용 패턴:
//   var snapshot = WorldFactionBridgeManager.Instance.GetSnapshot("Goblin");
//   if (snapshot.HasMood(FactionMoodTag.MOOD_PANIC)) {
//       // ...
//   }
// ============================================================================

using TDA.PB4.Faction.Tags;

namespace TDA.PB4.Faction
{
    /// <summary>
    /// Faction 의 한 시점 상태 스냅샷.
    /// ★ readonly struct — Immutable 보장 (mutation 시도 시 컴파일 에러).
    /// 외부 도메인은 본 객체만 받음.
    /// </summary>
    public readonly struct FactionStateSnapshot
    {
        public readonly string FactionId;
        public readonly uint   MoodBits;
        public readonly uint   TacticalBits;
        public readonly uint   LifecycleBits;
        public readonly uint   RelationBits;
        public readonly float  Timestamp;
        
        // ──────────────────────────────────
        // 생성자
        // ──────────────────────────────────
        
        public FactionStateSnapshot(
            string factionId,
            FactionStateBits bits,
            float timestamp)
        {
            FactionId     = factionId;
            MoodBits      = bits.moodBits;
            TacticalBits  = bits.tacticalBits;
            LifecycleBits = bits.lifecycleBits;
            RelationBits  = bits.relationBits;
            Timestamp     = timestamp;
        }
        
        public FactionStateSnapshot(
            string factionId,
            uint moodBits, uint tacticalBits, uint lifecycleBits, uint relationBits,
            float timestamp)
        {
            FactionId     = factionId;
            MoodBits      = moodBits;
            TacticalBits  = tacticalBits;
            LifecycleBits = lifecycleBits;
            RelationBits  = relationBits;
            Timestamp     = timestamp;
        }
        
        // ──────────────────────────────────
        // 검사 메서드
        // ──────────────────────────────────
        
        public bool HasMood(FactionMoodTag tag) =>
            (MoodBits & (uint)tag) != 0u;
        
        public bool HasTactical(FactionTacticalTag tag) =>
            (TacticalBits & (uint)tag) != 0u;
        
        public bool HasLifecycle(FactionLifecycleTag tag) =>
            (LifecycleBits & (uint)tag) != 0u;
        
        public bool HasRelation(FactionRelationTag tag) =>
            (RelationBits & (uint)tag) != 0u;
        
        // ──────────────────────────────────
        // 편의 검사
        // ──────────────────────────────────
        
        /// <summary> Snapshot 이 유효한지 (factionId 비어있지 않음) </summary>
        public bool IsValid => !string.IsNullOrEmpty(FactionId);
        
        /// <summary> 모든 Tag 비트가 0 인지 </summary>
        public bool IsEmpty =>
            MoodBits == 0u && TacticalBits == 0u &&
            LifecycleBits == 0u && RelationBits == 0u;
        
        // ──────────────────────────────────
        // 디버깅
        // ──────────────────────────────────
        
        public override string ToString() =>
            $"FactionStateSnapshot[{FactionId} " +
            $"M=0x{MoodBits:X8} T=0x{TacticalBits:X8} " +
            $"L=0x{LifecycleBits:X8} R=0x{RelationBits:X8} " +
            $"@{Timestamp:F2}]";
    }
}
