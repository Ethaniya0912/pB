// ═══════════════════════════════════════════════════════════════════════════════
// A12 — ThresholdCheckResult.cs
// Layer: L3 Domain (AI) / Namespace: TDA.PB4.AI
// ═══════════════════════════════════════════════════════════════════════════════
// 11 threshold 종류 enum + 위반 자료 구조.
//
// 호출자:
//   - FactionThresholdConfigSO (정의)
//   - GroupAIManager (검사 + EventBus 발행)
//   - IGroupDashboard.GetActiveThresholds (시나리오 측 응답)
//
// 위치: Assets/Scripts/pB-4/week5_A12/ThresholdCheckResult.cs
// 버전: v1.0 (2026-05-12)
// 기반: 기술개발 개론서 v11 §5.5 / pB4_A12_FactionThresholdConfigSO_v1.docx
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using UnityEngine;

namespace TDA.PB4.AI
{
    /// <summary>
    /// 펙션 위협 임계 11 종 — Wk5 본격 / 부록 B 11 슬롯 중 #5.
    /// 각 임계 = 펙션 별 다른 값 (Goblin/Orc/Skeleton 차별화).
    /// </summary>
    public enum FactionThresholdType
    {
        // 인구 / 전사 (3 종)
        MinPopulation = 0,             // 그룹 인구 최소 임계
        MaxCasualties = 1,              // 전사 자 누적 최대
        MaxConsecutiveDeaths = 2,       // 연속 전사 최대 (panic 트리거)

        // 사기 / 신뢰도 (3 종)
        MinAverageMorale = 3,           // 평균 사기 최소
        MinAverageTrust = 4,            // 그룹 평균 Trust 최소
        MaxDoubtCount = 5,              // Doubt 단계 멤버 최대 수

        // 영토 / 전술 (3 종)
        MinTerritoryControl = 6,        // 영토 통제 최소 (0~1)
        MaxEnemyAdvantage = 7,          // 적 우세 최대 (적/우방 비율)
        MaxRetreatCount = 8,            // 후퇴 누적 최대

        // 트라우마 / 사회 (2 종)
        MaxTraumatizedRatio = 9,        // 트라우마 멤버 비율 최대
        MaxAbandonCount = 10            // 유기 사건 누적 최대
    }

    /// <summary>
    /// 임계 위반 1 건 자료 구조.
    /// 호출자: GroupAIManager.CheckThresholds → OnThresholdsViolated 이벤트
    /// </summary>
    [System.Serializable]
    public class FactionThresholdViolation
    {
        public FactionThresholdType type;
        public float currentValue;
        public float thresholdValue;
        public string reason;
        public float timestamp;

        public FactionThresholdViolation(FactionThresholdType type, float current, float threshold, string reason = "")
        {
            this.type = type;
            this.currentValue = current;
            this.thresholdValue = threshold;
            this.reason = string.IsNullOrEmpty(reason)
                ? $"{type}: {current:F2} ≤/≥ {threshold:F2}"
                : reason;
            this.timestamp = Time.time;
        }

        public override string ToString()
        {
            return $"[{type}] {currentValue:F2} vs {thresholdValue:F2} ({reason})";
        }
    }

    /// <summary>
    /// 펙션 전술 행동 종류 (펙션 차별화 — Goblin/Orc/Skeleton).
    /// 본 enum 은 ThresholdViolation 시 펙션 행동 결정용.
    /// </summary>
    public enum FactionTacticalBehavior
    {
        QuickRetreat = 0,           // Goblin — 즉시 후퇴
        StrategicWithdraw = 1,      // Orc — 전략적 철수 (분대 명령)
        AmbushReset = 2,            // Skeleton — 매복 재설정
        PhalanxFormation = 3,       // Orc — 방진 형성
        PanicChain = 4,             // 패닉 전파 (모든 펙션)
        Desert = 5,                 // 도주 (Trust 낮은 멤버)
        Hold = 6                    // 무행동 (대기)
    }
}
