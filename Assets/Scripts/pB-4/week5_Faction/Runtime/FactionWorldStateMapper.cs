// ============================================================================
// FactionWorldStateMapper.cs
// FactionWorldState → FactionStateBits 변환 정적 함수
// 영역: 헬퍼
// 항목: P-02 부속 (P-04 변환 함수)
// ============================================================================
//
// 변환 단계 4:
//   ① 자동 변환     — populationLevel / isDecimated / isDominant → 4 Tag 비트
//   ② DefaultMask   — 자동 변환 결과에만 mask 적용 (디자이너 의도 보호)
//   ③ explicit 비트 — 시나리오 명시 비트 OR 추가 (mask 우회)
//   ④ 결과 반환
//
// 책임 분배 (v1.3 docx 정합):
//   - 자동 변환    → AI 측 해석 → DefaultMask 적용
//   - explicit 비트 → 시드/시나리오 권한 → DefaultMask 우회
// ============================================================================

using TDA.PB4.Data;
using TDA.PB4.Faction.Tags;

namespace TDA.PB4.Faction
{
    public static class FactionWorldStateMapper
    {
        // ────────────────────────────────────────
        // 임계값 상수 (Wk5+ FactionThresholdConfigSO 와 통합 검토)
        // ────────────────────────────────────────
        
        private const float DORMANT_THRESHOLD     = 0.1f;
        private const float THREATENED_THRESHOLD  = 0.3f;
        private const float FLOURISHING_THRESHOLD = 0.7f;
        
        // ────────────────────────────────────────
        // 메인 변환
        // ────────────────────────────────────────
        
        public static FactionStateBits ToTagBits(
            FactionWorldState worldState,
            FactionDefinitionSO definition)
        {
            // ① 자동 변환
            uint moodBits      = (uint)FactionMoodTag.MOOD_CALM;
            uint tacticalBits  = 0u;
            uint lifecycleBits = 0u;
            uint relationBits  = (uint)FactionRelationTag.RELATION_PLAYER_NEUTRAL;
            
            ApplyLifecycleAndMood(worldState, ref moodBits, ref lifecycleBits);
            ApplyDominance(worldState, ref moodBits, ref tacticalBits, ref lifecycleBits);
            
            // ② DefaultMask 적용 (디자이너 의도 보호)
            ApplyDefaultMask(definition, ref moodBits, ref tacticalBits, ref lifecycleBits, ref relationBits);
            
            // ③ explicit 비트 OR (mask 우회 — 시드/시나리오 권한)
            moodBits      |= worldState.explicitMoodBits;
            tacticalBits  |= worldState.explicitTacticalBits;
            lifecycleBits |= worldState.explicitLifecycleBits;
            relationBits  |= worldState.explicitRelationBits;
            
            // ④ 결과 반환
            return new FactionStateBits
            {
                moodBits      = moodBits,
                tacticalBits  = tacticalBits,
                lifecycleBits = lifecycleBits,
                relationBits  = relationBits
            };
        }
        
        // ────────────────────────────────────────
        // 자동 변환 분기 1 — Lifecycle + Mood (인구 / 패배)
        // ────────────────────────────────────────
        
        private static void ApplyLifecycleAndMood(
            FactionWorldState worldState,
            ref uint moodBits, ref uint lifecycleBits)
        {
            if (worldState.isDecimated)
            {
                lifecycleBits |= (uint)FactionLifecycleTag.LIFECYCLE_DEFEATED;
                moodBits      |= (uint)FactionMoodTag.MOOD_GRIEVING;
                return;
            }
            
            if (worldState.populationLevel < DORMANT_THRESHOLD)
                lifecycleBits |= (uint)FactionLifecycleTag.LIFECYCLE_DORMANT;
            else if (worldState.populationLevel < THREATENED_THRESHOLD)
                lifecycleBits |= (uint)FactionLifecycleTag.LIFECYCLE_THREATENED;
            else if (worldState.populationLevel > FLOURISHING_THRESHOLD)
                lifecycleBits |= (uint)FactionLifecycleTag.LIFECYCLE_FLOURISHING;
            else
                lifecycleBits |= (uint)FactionLifecycleTag.LIFECYCLE_ACTIVE;
        }
        
        // ────────────────────────────────────────
        // 자동 변환 분기 2 — 우세 (Dominant)
        // ────────────────────────────────────────
        
        private static void ApplyDominance(
            FactionWorldState worldState,
            ref uint moodBits, ref uint tacticalBits, ref uint lifecycleBits)
        {
            if (!worldState.isDominant) return;
            
            tacticalBits  |= (uint)FactionTacticalTag.TACTICAL_OFFENSIVE;
            moodBits      |= (uint)FactionMoodTag.MOOD_AGGRESSIVE;
            lifecycleBits |= (uint)FactionLifecycleTag.LIFECYCLE_DOMINANT;
        }
        
        // ────────────────────────────────────────
        // DefaultMask 적용 (자동 변환 결과만)
        // ────────────────────────────────────────
        
        private static void ApplyDefaultMask(
            FactionDefinitionSO definition,
            ref uint moodBits, ref uint tacticalBits, ref uint lifecycleBits, ref uint relationBits)
        {
            var mask = definition?.DefaultMask;
            if (mask == null) return;
            
            moodBits      &= mask.ValidMoodMask;
            tacticalBits  &= mask.ValidTacticalMask;
            lifecycleBits &= mask.ValidLifecycleMask;
            relationBits  &= mask.ValidRelationMask;
        }
    }
}
