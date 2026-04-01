// =============================================================================
// TrustMatrix.cs  |  pB-4 Project — Week 4
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   NPC의 플레이어에 대한 신뢰도를 관리한다.
//   신뢰도(0.0~100.0)가 4단계 임계점을 넘나들며 NPC의 행동이 극적으로 변한다.
//
//   4단계 임계점:
//     Blind Trust  (90~100): 자신의 생리적 욕구보다 플레이어 명령 우선
//     Cooperation  (50~89):  표준적 전투 대형. 합리적 명령 복종
//     Doubt        (20~49):  자기 보존 점수 우선. 위험한 명령 거부
//     Hostility    (0~19):   독자 행동/도망. 플레이어를 적으로 간주할 수 있음
//
//   명령 복종 필터:
//     AcceptanceScore = (Trust × 0.6) + (CommandSeverity × 0.4) - FearValue
//     AcceptanceScore > 0 이면 명령 수행, ≤ 0 이면 명령 거부
//
// 연계:
//   - UtilityMasterFormula.trustFactor: 이 클래스의 GetNormalizedTrust()를 매 틱 갱신
//   - TraumaSystem: 트라우마 공포 가중치를 Trust로 상쇄
//   - HumanoidAIBrain: UpdateDecision()에서 AcceptanceScore 확인
// =============================================================================
using System;
using UnityEngine;
using TDA.PB4.Core;

namespace TDA.PB4.AI
{
    /// <summary>
    /// 신뢰도 단계. Inspector에서 현재 단계를 확인할 수 있다.
    /// </summary>
    public enum TrustTier
    {
        /// <summary>90~100. 절대적 신뢰. 자기 희생도 감수.</summary>
        BlindTrust,
        /// <summary>50~89. 협력적. 합리적 명령에 복종.</summary>
        Cooperation,
        /// <summary>20~49. 의심. 위험한 명령 거부.</summary>
        Doubt,
        /// <summary>0~19. 적대. 독자 행동/도망.</summary>
        Hostility
    }

    /// <summary>
    /// 신뢰도 변화 이벤트의 원인 유형.
    /// 어떤 행위가 신뢰도를 변경시켰는지 추적.
    /// </summary>
    public enum TrustChangeReason
    {
        /// <summary>전술적으로 합리적인 명령 수행 성공. +15</summary>
        TacticalAlignment,
        /// <summary>생존 위기에서 함께 싸움. +20</summary>
        SharedSurvival,
        /// <summary>자원(힐/탄약)을 공정하게 분배. +10</summary>
        ResourceSharing,
        /// <summary>NPC를 위험에 내버려둔 전술적 배신. -40</summary>
        TacticalBetrayal,
        /// <summary>플레이어의 전투 실수/무능 노출. -5</summary>
        IncompetenceExposure,
        /// <summary>NPC를 구출. +30~50</summary>
        RescueAction,
        /// <summary>NPC를 유기. -70</summary>
        AbandonAction,
        /// <summary>엄호 후 포기. -10~-40</summary>
        CoverAndRetreat,
        /// <summary>수동 조절 (테스트/디버그용)</summary>
        Manual
    }

    public class TrustMatrix : MonoBehaviour
    {
        [Header("━━━ 신뢰도 현재 상태 ━━━━━━━━━━━━━━━━")]
        [Tooltip("현재 신뢰도 (0.0~100.0). " +
                 "Play 모드에서 이벤트에 따라 자동 변화. " +
                 "Inspector 슬라이더로 테스트 시 수동 조절 가능.")]
        [Range(0f, 100f)]
        [SerializeField] private float currentTrust = 60f;

        [Tooltip("현재 신뢰도 단계 (Read Only). " +
                 "BlindTrust/Cooperation/Doubt/Hostility 중 하나.")]
        [SerializeField] private TrustTier currentTier = TrustTier.Cooperation;

        [Header("━━━ 4단계 임계값 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Blind Trust 하한. 이 값 이상이면 '절대 신뢰' 상태. " +
                 "NPC가 자신의 생존보다 플레이어 명령을 우선시한다. " +
                 "90 = 매우 높은 신뢰가 필요.")]
        [Range(70f, 100f)]
        public float blindTrustThreshold = 90f;

        [Tooltip("Cooperation 하한. 이 값 이상~BlindTrust 미만이면 '협력' 상태. " +
                 "표준적 전투 대형. 합리적 명령에 복종. " +
                 "50 = 중간 수준의 신뢰.")]
        [Range(30f, 89f)]
        public float cooperationThreshold = 50f;

        [Tooltip("Doubt 하한. 이 값 이상~Cooperation 미만이면 '의심' 상태. " +
                 "자기 보존 점수가 우선. 위험한 명령 거부. " +
                 "20 = 상당한 불신.")]
        [Range(5f, 49f)]
        public float doubtThreshold = 20f;
        // Hostility는 Doubt 미만이므로 별도 임계값 불필요 (0~doubtThreshold)

        [Header("━━━ 명령 복종 수식 가중치 ━━━━━━━━━━━━")]
        [Tooltip("AcceptanceScore에서 신뢰도의 가중치. " +
                 "0.6 = 명령 수락 판정의 60%가 신뢰도에 의존.")]
        [Range(0f, 1f)]
        public float trustWeight = 0.6f;

        [Tooltip("AcceptanceScore에서 명령 심각도의 가중치. " +
                 "0.4 = 명령이 위험할수록 40%까지 영향. " +
                 "높은 심각도 = '적진 돌격' 같은 위험한 명령.")]
        [Range(0f, 1f)]
        public float severityWeight = 0.4f;

        [Header("━━━ 트라우마 연동 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("트라우마 시스템의 공포 가중치를 신뢰도로 상쇄하는 비율. " +
                 "(currentTrust/100) × 이 값 만큼 트라우마 공포 감소. " +
                 "1.0 = 신뢰도 100이면 트라우마 공포를 100% 상쇄. " +
                 "0.5 = 신뢰도 100이어도 트라우마 공포를 50%만 상쇄.")]
        [Range(0f, 1f)]
        public float traumaCancellationRate = 0.7f;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("신뢰도 변화를 Console에 출력")]
        public bool debugLog = false;

        [Tooltip("마지막 AcceptanceScore (Read Only)")]
        [SerializeField] private float lastAcceptanceScore;

        [Tooltip("마지막 명령 수락/거부 (Read Only)")]
        [SerializeField] private bool lastCommandAccepted;

        // ==================================================================
        // 신뢰도 변화
        // ==================================================================

        /// <summary>
        /// 신뢰도를 변경한다. 원인(reason)과 변화량(delta)을 기록.
        /// </summary>
        /// <param name="delta">변화량. +는 상승, -는 하락.</param>
        /// <param name="reason">변화 원인.</param>
        public void ChangeTrust(float delta, TrustChangeReason reason)
        {
            float oldTrust = currentTrust;
            TrustTier oldTier = currentTier;

            currentTrust = Mathf.Clamp(currentTrust + delta, 0f, 100f);
            currentTier = EvaluateTier(currentTrust);

            if (debugLog)
                Debug.Log($"[Trust] {reason}: {oldTrust:F1} → {currentTrust:F1} ({delta:+0.#}) Tier: {oldTier} → {currentTier}");

            // 단계 변경 시 이벤트 발행
            if (oldTier != currentTier)
            {
                EventBus.RaiseFactionStateChanged("player_trust",
                    new FactionWorldState { populationLevel = currentTrust / 100f, isDominant = currentTier == TrustTier.BlindTrust });

                if (debugLog)
                    Debug.Log($"[Trust] ⚠️ 단계 변경! {oldTier} → {currentTier}");
            }
        }

        /// <summary>사전 정의된 트리거별 신뢰도 변화.</summary>
        public void ApplyTrustTrigger(TrustChangeReason reason)
        {
            float delta = reason switch
            {
                TrustChangeReason.TacticalAlignment => 15f,
                TrustChangeReason.SharedSurvival => 20f,
                TrustChangeReason.ResourceSharing => 10f,
                TrustChangeReason.TacticalBetrayal => -40f,
                TrustChangeReason.IncompetenceExposure => -5f,
                TrustChangeReason.RescueAction => 40f,
                TrustChangeReason.AbandonAction => -70f,
                TrustChangeReason.CoverAndRetreat => -25f,
                _ => 0f
            };
            ChangeTrust(delta, reason);
        }

        // ==================================================================
        // 명령 복종 필터
        // AcceptanceScore = (Trust × trustWeight) + (Severity × severityWeight) - Fear
        // ==================================================================

        /// <summary>
        /// 플레이어의 명령을 NPC가 수락할지 판정.
        /// </summary>
        /// <param name="commandSeverity">명령의 심각도 (0.0=안전, 1.0=자살급 위험)</param>
        /// <param name="npcFear">NPC의 현재 공포 수치 (0.0~1.0)</param>
        /// <returns>true=명령 수락, false=명령 거부</returns>
        public bool EvaluateCommand(float commandSeverity, float npcFear)
        {
            float normalizedTrust = currentTrust / 100f;

            // 심각도 반전: 심각도가 낮을수록(안전할수록) 수락 점수가 높아야 함
            float severityFactor = 1f - commandSeverity;

            lastAcceptanceScore = (normalizedTrust * trustWeight)
                                + (severityFactor * severityWeight)
                                - npcFear;

            lastCommandAccepted = lastAcceptanceScore > 0f;

            if (debugLog)
                Debug.Log($"[Trust] 명령 판정: Accept={lastAcceptanceScore:F3} " +
                          $"(trust={normalizedTrust:F2}×{trustWeight} + sev={severityFactor:F2}×{severityWeight} - fear={npcFear:F2}) " +
                          $"→ {(lastCommandAccepted ? "✅수락" : "❌거부")}");

            return lastCommandAccepted;
        }

        // ==================================================================
        // 유틸리티
        // ==================================================================

        private TrustTier EvaluateTier(float trust)
        {
            if (trust >= blindTrustThreshold) return TrustTier.BlindTrust;
            if (trust >= cooperationThreshold) return TrustTier.Cooperation;
            if (trust >= doubtThreshold) return TrustTier.Doubt;
            return TrustTier.Hostility;
        }

        /// <summary>0.0~1.0 정규화된 신뢰도. UtilityMasterFormula.trustFactor에 연결.</summary>
        public float GetNormalizedTrust() => currentTrust / 100f;

        /// <summary>신뢰도에 의한 트라우마 공포 상쇄량. TraumaSystem에서 사용.</summary>
        public float GetTraumaCancellation() => (currentTrust / 100f) * traumaCancellationRate;

        /// <summary>현재 신뢰도 원시값.</summary>
        public float CurrentTrust => currentTrust;
        /// <summary>현재 단계.</summary>
        public TrustTier CurrentTier => currentTier;
    }
}
