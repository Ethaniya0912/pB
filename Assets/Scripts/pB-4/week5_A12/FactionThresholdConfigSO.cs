// ═══════════════════════════════════════════════════════════════════════════════
// A12 — FactionThresholdConfigSO.cs
// Layer: L0 Data / Namespace: TDA.PB4.AI
// ═══════════════════════════════════════════════════════════════════════════════
// 펙션 별 11 임계 + 전술 행동 ScriptableObject.
//
// 사용 방법:
//   1. Project 패널 우클릭 → Create → pB-4/AI/FactionThresholdConfig
//   2. 펙션 별 .asset 생성: Goblin / Orc / Skeleton
//   3. Inspector 에서 11 임계 + 전술 행동 가중치 설정
//   4. GroupAIManager 의 _factionConfig 필드에 드래그 연결
//
// 호출자:
//   - GroupAIManager.CheckThresholds (Wk5 패치)
//   - IGroupDashboard.GetActiveThresholds (시나리오 측 응답)
//
// 위치: Assets/Scripts/pB-4/week5_A12/FactionThresholdConfigSO.cs
// 버전: v1.1 (2026-05-12)
// 기반: 기술개발 개론서 v11 §5.5 / pB4_A12_FactionThresholdConfigSO_v1.docx
//
// [v1.1 — 2026-05-12] 임계 사용 여부 (use* flag) 추가
//   - 11 bool flag (useMinPopulation 등) — 펙션 별 일부 임계만 활성 가능
//   - IsEnabled(type) 헬퍼 메서드
//   - IsViolation 안 IsEnabled 사전 검사 (비활성 = 항상 false)
//   - 예시: Skeleton 은 사기/Trust/Doubt/트라우마/유기 5 임계 비활성 (언데드)
// ═══════════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.AI
{
    [CreateAssetMenu(
        fileName = "FactionThresholdConfig",
        menuName = "pB-4/AI/FactionThresholdConfig",
        order = 100)]
    public class FactionThresholdConfigSO : ScriptableObject
    {
        // ═══════════════════════════════════════════════════════════════════
        // 펙션 식별
        // ═══════════════════════════════════════════════════════════════════
        
        [Header("━━━ 펙션 식별 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━")]
        
        [Tooltip("펙션 ID — Goblin / Orc / Skeleton / Bandit 등.")]
        public string factionID = "Goblin";
        
        [Tooltip("펙션 사람 읽는 이름.")]
        public string factionName = "고블린 부족";
        
        // ═══════════════════════════════════════════════════════════════════
        // 인구 / 전사 임계 (3 종)
        // ═══════════════════════════════════════════════════════════════════
        
        [Header("━━━ 인구 / 전사 ━━━━━━━━━━━━━━━━━━━━━━━━━")]
        
        [Tooltip("그룹 인구 최소 임계 — 미만 시 retreat 트리거.\n" +
                 "Goblin 30 / Orc 10 / Skeleton 5.")]
        [Range(1, 100)] public int minPopulation = 30;
        
        [Tooltip("전사 자 누적 최대 — 초과 시 panic.")]
        [Range(1, 100)] public int maxCasualties = 20;
        
        [Tooltip("연속 전사 최대 — 초과 시 즉시 panic chain.")]
        [Range(1, 20)] public int maxConsecutiveDeaths = 3;
        
        // ═══════════════════════════════════════════════════════════════════
        // 사기 / 신뢰도 임계 (3 종)
        // ═══════════════════════════════════════════════════════════════════
        
        [Header("━━━ 사기 / 신뢰도 ━━━━━━━━━━━━━━━━━━━━━━━")]
        
        [Tooltip("평균 사기 최소 (0~1) — 미만 시 desertion 트리거.")]
        [Range(0f, 1f)] public float minAverageMorale = 0.3f;
        
        [Tooltip("그룹 평균 Trust 최소 (0~100) — 미만 시 cooperation 깨짐.")]
        [Range(0f, 100f)] public float minAverageTrust = 30f;
        
        [Tooltip("Doubt 단계 멤버 최대 수 — 초과 시 internal 분열.")]
        [Range(0, 20)] public int maxDoubtCount = 5;
        
        // ═══════════════════════════════════════════════════════════════════
        // 영토 / 전술 임계 (3 종)
        // ═══════════════════════════════════════════════════════════════════
        
        [Header("━━━ 영토 / 전술 ━━━━━━━━━━━━━━━━━━━━━━━━━")]
        
        [Tooltip("영토 통제 최소 (0~1) — 미만 시 territory loss 트리거.")]
        [Range(0f, 1f)] public float minTerritoryControl = 0.4f;
        
        [Tooltip("적 우세 최대 (적/우방 비율) — 초과 시 strategic retreat.")]
        [Range(1f, 10f)] public float maxEnemyAdvantage = 2.5f;
        
        [Tooltip("후퇴 누적 최대 — 초과 시 full collapse.")]
        [Range(1, 20)] public int maxRetreatCount = 5;
        
        // ═══════════════════════════════════════════════════════════════════
        // 트라우마 / 사회 임계 (2 종)
        // ═══════════════════════════════════════════════════════════════════
        
        [Header("━━━ 트라우마 / 사회 ━━━━━━━━━━━━━━━━━━━━━━")]
        
        [Tooltip("트라우마 멤버 비율 최대 (0~1) — 초과 시 group cohesion 붕괴.")]
        [Range(0f, 1f)] public float maxTraumatizedRatio = 0.4f;
        
        [Tooltip("유기 사건 누적 최대 — 초과 시 trust 전체 붕괴.")]
        [Range(1, 20)] public int maxAbandonCount = 3;
        
        // ═══════════════════════════════════════════════════════════════════
        // 펙션 전술 행동 가중치
        // ═══════════════════════════════════════════════════════════════════
        
        [Header("━━━ 펙션 전술 가중치 ━━━━━━━━━━━━━━━━━━━━")]
        
        [Tooltip("Quick Retreat 가중치 — 위반 시 후퇴 행동 가능성.\n" +
                 "Goblin 0.8 / Orc 0.2 / Skeleton 0.5.")]
        [Range(0f, 1f)] public float quickRetreatWeight = 0.8f;
        
        [Tooltip("Strategic Withdraw 가중치 — Orc 의 전략적 철수.\n" +
                 "Goblin 0.1 / Orc 0.7 / Skeleton 0.3.")]
        [Range(0f, 1f)] public float strategicWithdrawWeight = 0.1f;
        
        [Tooltip("Phalanx Formation 가중치 — Orc 방진.\n" +
                 "Goblin 0.0 / Orc 0.8 / Skeleton 0.0.")]
        [Range(0f, 1f)] public float phalanxFormationWeight = 0.0f;
        
        [Tooltip("Ambush Reset 가중치 — Skeleton 매복.\n" +
                 "Goblin 0.2 / Orc 0.1 / Skeleton 0.9.")]
        [Range(0f, 1f)] public float ambushResetWeight = 0.2f;
        
        [Tooltip("Panic Chain 전파 가중치 — 모든 펙션 (Goblin 최대).\n" +
                 "Goblin 1.5 / Orc 0.8 / Skeleton 1.0.")]
        [Range(0f, 2f)] public float panicChainMultiplier = 1.5f;
        
        // ═══════════════════════════════════════════════════════════════════
        // 임계 사용 여부 (★ v1.1 — 각 임계 별 on/off flag)
        // ═══════════════════════════════════════════════════════════════════
        //
        // 펙션 별 일부 임계만 활성 가능. 예:
        //   Goblin    — 모든 11 임계 활성
        //   Orc       — Doubt 임계 비활성 (분대 규율 = 의심 무관)
        //   Skeleton  — 사기 / Trust / Doubt / 트라우마 / 유기 비활성 (언데드)
        //
        // false 시 IsViolation 항상 false 반환 + CheckThresholds 검사 스킵.
        // ═══════════════════════════════════════════════════════════════════
        
        [Header("━━━ 임계 사용 여부 (★ v1.1) ━━━━━━━━━━━━━")]
        
        [Tooltip("인구 임계 사용 여부.")] public bool useMinPopulation = true;
        [Tooltip("전사 자 임계 사용 여부.")] public bool useMaxCasualties = true;
        [Tooltip("연속 전사 임계 사용 여부.")] public bool useMaxConsecutiveDeaths = true;
        [Tooltip("평균 사기 임계 사용 여부 (Skeleton 같은 언데드 = false).")] public bool useMinAverageMorale = true;
        [Tooltip("평균 Trust 임계 사용 여부 (언데드 = false).")] public bool useMinAverageTrust = true;
        [Tooltip("Doubt 멤버 임계 사용 여부 (분대 규율 강한 펙션 = false).")] public bool useMaxDoubtCount = true;
        [Tooltip("영토 통제 임계 사용 여부.")] public bool useMinTerritoryControl = true;
        [Tooltip("적 우세 임계 사용 여부.")] public bool useMaxEnemyAdvantage = true;
        [Tooltip("후퇴 누적 임계 사용 여부.")] public bool useMaxRetreatCount = true;
        [Tooltip("트라우마 비율 임계 사용 여부 (트라우마 영향 없는 펙션 = false).")] public bool useMaxTraumatizedRatio = true;
        [Tooltip("유기 사건 임계 사용 여부 (사회 X 펙션 = false).")] public bool useMaxAbandonCount = true;
        
        // ═══════════════════════════════════════════════════════════════════
        // 검사 메서드 — GroupAIManager 호출
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// 임계 사용 여부 검사 (★ v1.1).
        /// false 반환 시 해당 임계는 검사 스킵.
        /// </summary>
        public bool IsEnabled(FactionThresholdType type)
        {
            switch (type)
            {
                case FactionThresholdType.MinPopulation:        return useMinPopulation;
                case FactionThresholdType.MaxCasualties:        return useMaxCasualties;
                case FactionThresholdType.MaxConsecutiveDeaths: return useMaxConsecutiveDeaths;
                case FactionThresholdType.MinAverageMorale:     return useMinAverageMorale;
                case FactionThresholdType.MinAverageTrust:      return useMinAverageTrust;
                case FactionThresholdType.MaxDoubtCount:        return useMaxDoubtCount;
                case FactionThresholdType.MinTerritoryControl:  return useMinTerritoryControl;
                case FactionThresholdType.MaxEnemyAdvantage:    return useMaxEnemyAdvantage;
                case FactionThresholdType.MaxRetreatCount:      return useMaxRetreatCount;
                case FactionThresholdType.MaxTraumatizedRatio:  return useMaxTraumatizedRatio;
                case FactionThresholdType.MaxAbandonCount:      return useMaxAbandonCount;
                default: return false;
            }
        }
        
        /// <summary>
        /// 위반 1 건 검사 (각 FactionThresholdType 에 대응).
        /// ★ v1.1 — IsEnabled 검사 통합 (비활성 = 항상 false).
        /// </summary>
        public bool IsViolation(FactionThresholdType type, float currentValue)
        {
            // ★ v1.1 — 비활성 임계는 위반 아님
            if (!IsEnabled(type)) return false;
            
            switch (type)
            {
                case FactionThresholdType.MinPopulation:        return currentValue < minPopulation;
                case FactionThresholdType.MaxCasualties:        return currentValue > maxCasualties;
                case FactionThresholdType.MaxConsecutiveDeaths: return currentValue > maxConsecutiveDeaths;
                case FactionThresholdType.MinAverageMorale:     return currentValue < minAverageMorale;
                case FactionThresholdType.MinAverageTrust:      return currentValue < minAverageTrust;
                case FactionThresholdType.MaxDoubtCount:        return currentValue > maxDoubtCount;
                case FactionThresholdType.MinTerritoryControl:  return currentValue < minTerritoryControl;
                case FactionThresholdType.MaxEnemyAdvantage:    return currentValue > maxEnemyAdvantage;
                case FactionThresholdType.MaxRetreatCount:      return currentValue > maxRetreatCount;
                case FactionThresholdType.MaxTraumatizedRatio:  return currentValue > maxTraumatizedRatio;
                case FactionThresholdType.MaxAbandonCount:      return currentValue > maxAbandonCount;
                default: return false;
            }
        }
        
        /// <summary>해당 type 의 임계값 반환.</summary>
        public float GetThresholdValue(FactionThresholdType type)
        {
            switch (type)
            {
                case FactionThresholdType.MinPopulation:        return minPopulation;
                case FactionThresholdType.MaxCasualties:        return maxCasualties;
                case FactionThresholdType.MaxConsecutiveDeaths: return maxConsecutiveDeaths;
                case FactionThresholdType.MinAverageMorale:     return minAverageMorale;
                case FactionThresholdType.MinAverageTrust:      return minAverageTrust;
                case FactionThresholdType.MaxDoubtCount:        return maxDoubtCount;
                case FactionThresholdType.MinTerritoryControl:  return minTerritoryControl;
                case FactionThresholdType.MaxEnemyAdvantage:    return maxEnemyAdvantage;
                case FactionThresholdType.MaxRetreatCount:      return maxRetreatCount;
                case FactionThresholdType.MaxTraumatizedRatio:  return maxTraumatizedRatio;
                case FactionThresholdType.MaxAbandonCount:      return maxAbandonCount;
                default: return 0f;
            }
        }
        
        /// <summary>
        /// 위반 종합 시 최적 전술 행동 결정 (펙션 차별화).
        /// 가중치 max → 그 행동 선택.
        /// </summary>
        public FactionTacticalBehavior DetermineBehavior(List<FactionThresholdViolation> violations)
        {
            if (violations == null || violations.Count == 0)
                return FactionTacticalBehavior.Hold;
            
            // 위반 종류에 따라 펙션 가중치 평가
            float quickWeight = quickRetreatWeight;
            float strategicWeight = strategicWithdrawWeight;
            float phalanxWeight = phalanxFormationWeight;
            float ambushWeight = ambushResetWeight;
            
            // 인구 / 전사 위반 = retreat 강조
            foreach (var v in violations)
            {
                if (v.type == FactionThresholdType.MinPopulation || v.type == FactionThresholdType.MaxCasualties)
                {
                    quickWeight *= 1.3f;
                    strategicWeight *= 1.2f;
                }
            }
            
            // 최대값 행동 선택
            float maxWeight = Mathf.Max(quickWeight,
                Mathf.Max(strategicWeight,
                    Mathf.Max(phalanxWeight, ambushWeight)));
            
            if (Mathf.Approximately(maxWeight, quickWeight))       return FactionTacticalBehavior.QuickRetreat;
            if (Mathf.Approximately(maxWeight, strategicWeight))   return FactionTacticalBehavior.StrategicWithdraw;
            if (Mathf.Approximately(maxWeight, phalanxWeight))     return FactionTacticalBehavior.PhalanxFormation;
            if (Mathf.Approximately(maxWeight, ambushWeight))      return FactionTacticalBehavior.AmbushReset;
            
            return FactionTacticalBehavior.Hold;
        }
    }
}
