// ============================================================================
// FactionWorldState.cs — STUB / 어댑테이션 포인트
// MetaScenarioGenerator 의 출력 + AI 측 입력 struct
// 영역: 헬퍼
// ============================================================================
//
// ★ 어댑테이션 포인트:
//   사용자 측 GameBlackboard 에 이미 동일한 FactionWorldState struct 가 있다면:
//     → 본 파일 삭제
//     → FactionWorldStateMapper 의 using 을 사용자 측 namespace 로 변경
//
//   사용자 측에 없다면:
//     → 본 stub 사용 가능
//
// 필드 추가 시 양측 동기화 필요:
//   - 자동 변환 필드: populationLevel / isDecimated / isDominant / territoryNodes
//   - 시나리오 명시 비트 (mask 우회): explicitMoodBits / Tactical / Lifecycle / Relation
//
// MetaScenarioGenerator 가 본 struct 채워서 매니저에 전달.
// FactionWorldStateMapper 가 4 Tag 비트마스크로 변환.
// ============================================================================

using System.Collections.Generic;

namespace TDA.PB4.Faction
{
    /// <summary>
    /// MetaScenarioGenerator → WorldFactionStateManager 전달 struct.
    /// 시드 + 시나리오 로직으로 결정된 한 Faction 의 월드 상태.
    /// </summary>
    public struct FactionWorldState
    {
        // ────────────────────────────────────────
        // 자동 변환 (FactionWorldStateMapper 에서 4 Tag 로 변환)
        // ────────────────────────────────────────
        
        /// <summary> 인구 수준 (0~1). 0 = 멸종 직전 / 1 = 최대 </summary>
        public float populationLevel;
        
        /// <summary> 결정적 패배 여부 — LIFECYCLE_DEFEATED + MOOD_GRIEVING 자동 부여 </summary>
        public bool  isDecimated;
        
        /// <summary> 우세 여부 — TACTICAL_OFFENSIVE + MOOD_AGGRESSIVE + LIFECYCLE_DOMINANT 자동 부여 </summary>
        public bool  isDominant;
        
        /// <summary> 점유 노드 ID 리스트 (CaveNodeGraph idx) </summary>
        public List<int> territoryNodes;
        
        // ────────────────────────────────────────
        // 시나리오 명시 비트 (Mapper 에서 DefaultMask 우회)
        // ────────────────────────────────────────
        
        /// <summary> ★ 시나리오가 명시한 Mood 비트. DefaultMask 우회. </summary>
        public uint explicitMoodBits;
        
        /// <summary> ★ 시나리오가 명시한 Tactical 비트. DefaultMask 우회. </summary>
        public uint explicitTacticalBits;
        
        /// <summary> ★ 시나리오가 명시한 Lifecycle 비트. DefaultMask 우회. </summary>
        public uint explicitLifecycleBits;
        
        /// <summary> ★ 시나리오가 명시한 Relation 비트. DefaultMask 우회. </summary>
        public uint explicitRelationBits;
    }
}
