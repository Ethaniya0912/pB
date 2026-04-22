// =============================================================================
// TrustMatrix.cs  |  pB-4 Project — Week 4 (Day 2 T2.4 매뉴얼 v3 사양 적용)
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   NPC의 플레이어에 대한 신뢰도를 관리한다.
//   신뢰도(0.0~100.0)가 4단계 임계점을 넘나들며 NPC의 행동이 극적으로 변한다.
//
//   4단계 임계점 (매뉴얼 사양 enum 순서):
//     [0] Hostility   (0~19):    독자 행동/도망. 플레이어를 적으로 간주
//     [1] Doubt       (20~49):   자기 보존 점수 우선. 위험한 명령 거부
//     [2] Cooperation (50~89):   표준 전투 대형. 합리적 명령 복종
//     [3] BlindTrust  (90~100):  자신 욕구보다 플레이어 명령 우선
//
//   명령 복종 필터:
//     AcceptanceScore = (Trust × 0.6) + (1-Severity × 0.4) - FearValue
//     AcceptanceScore > 0 이면 명령 수행, ≤ 0 이면 명령 거부
//
// [Day 2 T2.4 v3 변경 요약]
//   1) ITrustProvider 인터페이스 구현 (4 메서드)
//   2) TrustTier enum 순서: Hostility=0 ... BlindTrust=3 (매뉴얼 사양)
//   3) AddDelta(float, string) 어댑터 메서드 추가 — 기존 ChangeTrust 호출
//   4) [Errata F6] EventBus.RaiseTrustTierChanged 전역 브릿지 (Tier 전이 시)
//   5) OnTierChanged 로컬 이벤트 (Dashboard 개별 NPC 감시용)
//   6) 기존 고급 로직 모두 보존 (TrustChangeReason, ApplyTrustTrigger, 등)
// =============================================================================
using System;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Interfaces.Intelligence;

namespace TDA.PB4.AI
{
    /// <summary>
    /// 신뢰도 단계. Inspector에서 현재 단계를 확인할 수 있다.
    /// [매뉴얼 사양] 인덱스 순서: 0=Hostility, 1=Doubt, 2=Cooperation, 3=BlindTrust.
    /// IAlignmentProvider의 인덱스 의미와 일치하도록 낮은 값=낮은 신뢰로 정렬.
    /// </summary>
    public enum TrustTier
    {
        /// <summary>0. 0~19. 적대. 독자 행동/도망.</summary>
        Hostility = 0,
        /// <summary>1. 20~49. 의심. 위험한 명령 거부.</summary>
        Doubt = 1,
        /// <summary>2. 50~89. 협력. 합리적 명령에 복종.</summary>
        Cooperation = 2,
        /// <summary>3. 90~100. 절대 신뢰. 자기 희생도 감수.</summary>
        BlindTrust = 3
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
        /// <summary>수동 조절 (테스트/디버그/인터페이스 호출용)</summary>
        Manual
    }

    public class TrustMatrix : MonoBehaviour, ITrustProvider
    {
        [Header("━━━ 신뢰도 현재 상태 ━━━━━━━━━━━━━━━━")]
        [Tooltip("현재 신뢰도 (0.0~100.0). " +
                 "Play 모드에서 이벤트에 따라 자동 변화. " +
                 "Inspector 슬라이더로 테스트 시 수동 조절 가능.")]
        [SerializeField, Range(0f, 100f)] private float currentTrust = 60f;

        [Tooltip("현재 신뢰도 단계 (Read Only). " +
                 "Hostility/Doubt/Cooperation/BlindTrust 중 하나.")]
        [SerializeField] private TrustTier currentTier = TrustTier.Cooperation;

        [Header("━━━ 4단계 임계값 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Blind Trust 하한. 이 값 이상이면 '절대 신뢰' 상태. " +
                 "NPC가 자신의 생존보다 플레이어 명령을 우선시한다. " +
                 "90 = 매우 높은 신뢰가 필요.")]
        [Range(70f, 100f)]
        public float blindTrustThreshold = 90f;

        [Tooltip("Cooperation 하한. 이 값 이상~BlindTrust 미만이면 '협력' 상태. " +
                 "표준 전투 대형. 합리적 명령에 복종. " +
                 "50 = 중간 수준의 신뢰.")]
        [Range(30f, 89f)]
        public float cooperationThreshold = 50f;

        [Tooltip("Doubt 하한. 이 값 이상~Cooperation 미만이면 '의심' 상태. " +
                 "자기 보존 점수가 우선. 위험한 명령 거부. " +
                 "20 = 상당한 불신.")]
        [Range(5f, 49f)]
        public float doubtThreshold = 20f;
        // Hostility는 Doubt 미만 (0 ~ doubtThreshold)

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

        [Tooltip("[D3] 명령 수락 판정 임계값. AcceptanceScore가 이 값보다 크면 수락. " +
                 "0.0 = 양수면 수락 (관대, 사용자 기존 사양). " +
                 "0.3 = 약간 보수적 (매뉴얼 v3 권장). " +
                 "0.5 = 매우 보수적 (위험 명령 거부 강함). " +
                 "디자이너가 NPC별로 조정 가능.")]
        [Range(0f, 1f)]
        public float commandAcceptanceThreshold = 0f;

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
        // 이벤트
        // ==================================================================

        /// <summary>로컬 Tier 전이 이벤트. 인자: (oldTierIndex, newTierIndex).</summary>
        /// <remarks>
        /// 각 NPC의 Dashboard 등이 개별 감시 용도. 
        /// 전역 통지는 EventBus.OnTrustTierChanged 사용 (Day 3 IncidentRecorder 등).
        /// </remarks>
        public event System.Action<int, int> OnTierChanged;

        // ==================================================================
        // 신뢰도 변화 (기존 본격 API)
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
                Debug.Log($"[Trust] {name}: {reason}: {oldTrust:F1} → {currentTrust:F1} ({delta:+0.#;-0.#;0}) Tier: {oldTier} → {currentTier}");

            // 단계 변경 시 이벤트 발행
            if (oldTier != currentTier)
            {
                // [중요 이벤트] Tier 변경은 debugLog 무관 항상 출력 (검증/디버깅 가시성)
                Debug.Log($"[Trust] {name}: ⚠ 단계 변경! {oldTier} → {currentTier} (trust {oldTrust:F1}→{currentTrust:F1}, reason={reason})");

                int oldIdx = (int)oldTier;
                int newIdx = (int)currentTier;

                // [기존] 로컬 이벤트 (Dashboard 등이 개별 NPC 감시)
                OnTierChanged?.Invoke(oldIdx, newIdx);

                // [Errata F6] 전역 EventBus 브릿지 (Day 3 IncidentRecorder/SpeechAssembler 구독)
                EventBus.RaiseTrustTierChanged(name, oldIdx, newIdx);

                // [기존] FactionWorldState 변화 이벤트도 함께 발행
                EventBus.RaiseFactionStateChanged("player_trust",
                    new FactionWorldState
                    {
                        populationLevel = currentTrust / 100f,
                        isDominant = currentTier == TrustTier.BlindTrust
                    });
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
        // ITrustProvider 인터페이스 구현 (Day 2 T2.4)
        // ==================================================================

        /// <summary>
        /// [ITrustProvider] 0(적대)~1(맹신) 정규화 신뢰도.
        /// </summary>
        public float GetNormalizedTrust() => currentTrust / 100f;

        /// <summary>
        /// [ITrustProvider] 현재 Tier 인덱스. 0=Hostility, 3=BlindTrust.
        /// </summary>
        public int GetCurrentTierIndex() => (int)currentTier;

        /// <summary>
        /// [ITrustProvider] 신뢰도에 델타 적용. ChangeTrust의 어댑터.
        /// </summary>
        /// <param name="amount">-100~+100.</param>
        /// <param name="reason">TrustChangeReason enum 이름 또는 "Manual" 등 자유 문자열.</param>
        /// <remarks>
        /// reason 문자열을 TrustChangeReason enum으로 매핑 시도. 매칭 실패 시 Manual로 처리.
        /// 디버그 로그에는 원본 reason 문자열도 함께 출력.
        /// </remarks>
        public void AddDelta(float amount, string reason)
        {
            // string → enum 매핑 시도
            if (!System.Enum.TryParse<TrustChangeReason>(reason, out var reasonEnum))
            {
                reasonEnum = TrustChangeReason.Manual;
                if (debugLog)
                    Debug.Log($"[Trust] {name}: AddDelta reason='{reason}' (enum 매핑 실패 → Manual)");
            }
            ChangeTrust(amount, reasonEnum);
        }

        /// <summary>
        /// [ITrustProvider] 명령 수락 여부 평가.
        /// AcceptanceScore = (Trust × 0.6) + ((1-Severity) × 0.4) - Fear
        /// </summary>
        /// <param name="commandSeverity">명령의 심각도 (0.0=안전, 1.0=자살급 위험)</param>
        /// <param name="npcFear">NPC의 현재 공포 수치 (0.0~1.0)</param>
        /// <returns>true=명령 수락, false=명령 거부</returns>
        public bool EvaluateCommand(float commandSeverity, float npcFear)
        {
            float normalizedTrust = currentTrust / 100f;

            // 심각도 반전: 심각도가 낮을수록(안전할수록) 수락 점수가 높아야 함
            float severityFactor = 1f - Mathf.Clamp01(commandSeverity);

            lastAcceptanceScore = (normalizedTrust * trustWeight)
                                + (severityFactor * severityWeight)
                                - Mathf.Clamp01(npcFear);

            // [D3] commandAcceptanceThreshold 슬라이더 적용. 기본 0.0 (사용자 사양). v3 매뉴얼 권장은 0.3.
            lastCommandAccepted = lastAcceptanceScore > commandAcceptanceThreshold;

            if (debugLog)
                Debug.Log($"[Trust] {name}: 명령 판정: Accept={lastAcceptanceScore:F3} " +
                          $"(trust={normalizedTrust:F2}×{trustWeight} + sev={severityFactor:F2}×{severityWeight} - fear={npcFear:F2}) " +
                          $"→ {(lastCommandAccepted ? "✅수락" : "❌거부")}");

            return lastCommandAccepted;
        }

        // ==================================================================
        // 트라우마 연동 헬퍼 (기존 보존)
        // ==================================================================

        /// <summary>신뢰도에 의한 트라우마 공포 상쇄량. TraumaSystem에서 사용.</summary>
        public float GetTraumaCancellation() => (currentTrust / 100f) * traumaCancellationRate;

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

        /// <summary>현재 신뢰도 원시값.</summary>
        public float CurrentTrust => currentTrust;

        /// <summary>현재 단계.</summary>
        public TrustTier CurrentTier => currentTier;

        // ==================================================================
        // Inspector 디버그용 Context Menu
        // ==================================================================

        [ContextMenu("Set to BlindTrust (95)")]
        private void DebugSetBlindTrust() => ChangeTrust(95f - currentTrust, TrustChangeReason.Manual);

        [ContextMenu("Set to Cooperation (60)")]
        private void DebugSetCooperation() => ChangeTrust(60f - currentTrust, TrustChangeReason.Manual);

        [ContextMenu("Set to Doubt (30)")]
        private void DebugSetDoubt() => ChangeTrust(30f - currentTrust, TrustChangeReason.Manual);

        [ContextMenu("Set to Hostility (10)")]
        private void DebugSetHostility() => ChangeTrust(10f - currentTrust, TrustChangeReason.Manual);

        /// <summary>OnValidate에서 currentTier 자동 계산 (Inspector slider 조작 시).</summary>
        private void OnValidate()
        {
            currentTier = EvaluateTier(currentTrust);
        }
    }
}
