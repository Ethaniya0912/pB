// ============================================================================
// FactionStateBits.cs
// 4 Tag uint 비트마스크 struct (internal mutable)
// 영역: 헬퍼
// 항목: P-05 부속
// ============================================================================

namespace TDA.PB4.Faction
{
    /// <summary>
    /// Faction 의 4 Tag 비트마스크 보관 struct.
    /// ★ internal mutable — WorldFactionStateManager 내부 Dictionary 전용.
    /// 외부 노출 시 FactionStateSnapshot (readonly struct) 으로 변환.
    /// </summary>
    public struct FactionStateBits
    {
        public uint moodBits;
        public uint tacticalBits;
        public uint lifecycleBits;
        public uint relationBits;
    }
}
