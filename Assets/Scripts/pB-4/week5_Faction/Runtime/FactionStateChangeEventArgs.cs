// ============================================================================
// FactionStateChangeEventArgs.cs
// Faction 상태 변경 이벤트 인자 struct
// 영역: 헬퍼
// 항목: P-02 부속
// ============================================================================
//
// WorldFactionStateManager.OnFactionStateChanged 이벤트의 페이로드.
// 구독자 (GroupAIManager / Audio / VFX 등) 가 변경 정보 받음.
//
// before/after 비트마스크로 정확히 어떤 Tag 가 추가/제거됐는지 추적 가능.
// ============================================================================

namespace TDA.PB4.Faction
{
    public struct FactionStateChangeEventArgs
    {
        public string factionId;
        
        public FactionStateBits bitsBefore;
        public FactionStateBits bitsAfter;
        
        public float timestamp;
        
        // ────────────────────────────────────────
        // 편의 — 어떤 비트가 변경됐는지
        // ────────────────────────────────────────
        
        /// <summary> Mood 비트 변경 여부 </summary>
        public bool MoodChanged      => bitsBefore.moodBits      != bitsAfter.moodBits;
        public bool TacticalChanged  => bitsBefore.tacticalBits  != bitsAfter.tacticalBits;
        public bool LifecycleChanged => bitsBefore.lifecycleBits != bitsAfter.lifecycleBits;
        public bool RelationChanged  => bitsBefore.relationBits  != bitsAfter.relationBits;
        
        /// <summary> Mood 에서 새로 켜진 비트 (이전 0 → 이후 1) </summary>
        public uint MoodBitsTurnedOn      => ~bitsBefore.moodBits      & bitsAfter.moodBits;
        public uint TacticalBitsTurnedOn  => ~bitsBefore.tacticalBits  & bitsAfter.tacticalBits;
        public uint LifecycleBitsTurnedOn => ~bitsBefore.lifecycleBits & bitsAfter.lifecycleBits;
        public uint RelationBitsTurnedOn  => ~bitsBefore.relationBits  & bitsAfter.relationBits;
        
        /// <summary> Mood 에서 새로 꺼진 비트 (이전 1 → 이후 0) </summary>
        public uint MoodBitsTurnedOff      => bitsBefore.moodBits      & ~bitsAfter.moodBits;
        public uint TacticalBitsTurnedOff  => bitsBefore.tacticalBits  & ~bitsAfter.tacticalBits;
        public uint LifecycleBitsTurnedOff => bitsBefore.lifecycleBits & ~bitsAfter.lifecycleBits;
        public uint RelationBitsTurnedOff  => bitsBefore.relationBits  & ~bitsAfter.relationBits;
    }
}
