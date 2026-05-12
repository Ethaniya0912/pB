// ============================================================================
// FactionThresholdConfigSO.cs
// Faction 임계값 ScriptableObject
// 영역: 컨셉트 (정적 정의)
// 항목: P-12
// 11사안 v3 §4.2.1 박제 반영
// ============================================================================
//
// 임계 영역:
//   - 영토 확장 — 인구 임계 + 사기 + 인접 빈 영역
//   - 전쟁     — 공격성 + 우위 비율
//   - 방어     — 침입 감지 반경 + 방어 사기 보너스
//   - 분할     — 인구 임계 + 대장개체 자격
//
// 종족별 권장 프리셋 (★ 임시 — 디렉션 합류 시 협의):
//   Goblin    : populationOverflow 10 / split 25 (작은 그룹, 자주 분할)
//   Orc       : populationOverflow 18 / split 40 (큰 그룹, 잘 안 분할)
//   Skeleton  : populationOverflow 15 / split 30 (중간)
// ============================================================================

using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(
        menuName = "pB4/Faction/FactionThresholdConfig",
        fileName = "FactionThresholdConfig_NewFaction")]
    public class FactionThresholdConfigSO : ScriptableObject
    {
        [Header("━━━ Identity ━━━")]
        
        [Tooltip("FactionDefinitionSO 의 factionId 와 일치해야 함")]
        [SerializeField] private string _factionId = "";
        
        public string FactionId => _factionId;
        
        [Header("━━━ 영토 확장 임계 ━━━")]
        
        [Tooltip("영토 확장 트리거 인구 임계")]
        [SerializeField] private int _populationOverflowThreshold = 15;
        
        [Tooltip("영토 확장에 필요한 최소 사기 (0~1)")]
        [Range(0f, 1f)] [SerializeField] private float _moraleRequiredForExpansion = 0.6f;
        
        [Tooltip("영토 확장에 필요한 인접 빈 영역 비율 (0~1)")]
        [Range(0f, 1f)] [SerializeField] private float _adjacentEmptyTerritoryRequired = 0.3f;
        
        public int   PopulationOverflowThreshold      => _populationOverflowThreshold;
        public float MoraleRequiredForExpansion       => _moraleRequiredForExpansion;
        public float AdjacentEmptyTerritoryRequired   => _adjacentEmptyTerritoryRequired;
        
        [Header("━━━ 전쟁 임계 ━━━")]
        
        [Tooltip("전쟁 선포에 필요한 공격성 임계 (0~1)")]
        [Range(0f, 1f)] [SerializeField] private float _aggressionThreshold = 0.7f;
        
        [Tooltip("공격 결정에 필요한 우위 비율 (자기 인구 / 상대 인구)")]
        [SerializeField] private float _superiorityRatioForAttack = 1.2f;
        
        public float AggressionThreshold      => _aggressionThreshold;
        public float SuperiorityRatioForAttack => _superiorityRatioForAttack;
        
        [Header("━━━ 방어 임계 ━━━")]
        
        [Tooltip("침입자 감지 반경 (m)")]
        [SerializeField] private float _invasionDetectionRadius = 50f;
        
        [Tooltip("침입 시 방어 사기 보너스 (0~1)")]
        [Range(0f, 1f)] [SerializeField] private float _defensiveMoraleBonus = 0.2f;
        
        public float InvasionDetectionRadius => _invasionDetectionRadius;
        public float DefensiveMoraleBonus    => _defensiveMoraleBonus;
        
        [Header("━━━ 분할 임계 (P-17 분할 6 단계 활용) ━━━")]
        
        [Tooltip("분할 트리거 인구 임계. 확장 임계보다 높음.")]
        [SerializeField] private int _populationSplitThreshold = 30;
        
        [Tooltip("대장개체 자격 — 최소 카리스마 (0~1)")]
        [Range(0f, 1f)] [SerializeField] private float _leaderCharismaRequired = 0.7f;
        
        [Tooltip("대장개체 자격 — 최소 전투 명성 (0~1)")]
        [Range(0f, 1f)] [SerializeField] private float _leaderCombatPrestigeRequired = 0.5f;
        
        [Tooltip("대장개체 자격 — 최소 나이 (초)")]
        [SerializeField] private float _minimumAgeForLeader = 10f;
        
        public int   PopulationSplitThreshold        => _populationSplitThreshold;
        public float LeaderCharismaRequired          => _leaderCharismaRequired;
        public float LeaderCombatPrestigeRequired    => _leaderCombatPrestigeRequired;
        public float MinimumAgeForLeader             => _minimumAgeForLeader;
    }
}
