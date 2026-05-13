// ═══════════════════════════════════════════════════════════════════════════════
// Wk4-4 — AbandonRescueResolver.cs (본격 구현 v1.0)
// ═══════════════════════════════════════════════════════════════════════════════
// 동료 위기 시 NPC 의 3 분기 결정 (Rescue / BriefSupport / Abandon).
// 신규 작성 [10] — 사전 코드 없음.
//
// 의존: TrustMatrix (Wk4-1) / PersonalityMatrix (Wk0 동결)
// 호출자: Wk4-6 DilemmaPivot / HumanoidAIBrain
// 후속: PersonalityTagResolver (태그 발현 — TODO Wk5+) / Wk6 RAG 코사인 (유기된 자 검색 입력)
//
// 위치: Assets/Scripts/pB-4/week4_AI_Social/AbandonRescueResolver.cs
// 버전: v1.4 (2026-05-12) — _selfTrust 자동 연결 (Reset + Awake fallback)
//
// ─── v1.3 → v1.4 변경 사항 (2026-05-12) ────────────────────────────────────────
//   - Reset() 메서드 추가 — Inspector 부착 시 자동 GetComponent<TrustMatrix>
//   - Awake() 메서드 추가 — Play 진입 시 fallback (Inspector 드래그 누락 시)
//   - 변경 이유: 사용자가 Inspector 에서 _selfTrust 드래그 안 해도 자동 작동
//                Wk4 본격 검증 시 발견 (PrintState 에서 Self Trust = <없음>)
//
// ─── v1.2 → v1.3 변경 사항 (2026-05-12) — 컴파일 에러 해소 ─────────────────────
//   - PrintState ContextMenu 의 _selfTrust.Value → _selfTrust.CurrentTrust
//   - 이유: 기존 TrustMatrix 의 API = CurrentTrust property (Value 없음)
//
// ─── v1.1 → v1.2 변경 사항 (2026-05-12) — 사용자 측 본체 정합 ─────────────────
//   - using TDA.PB4.AI 추가 (TrustMatrix / TrustChangeReason)
//   - AdjustTrust(delta, string) → ChangeTrust(delta, TrustChangeReason)
//     의미별 매핑:
//       영웅적 구출   → RescueAction
//       구출 목격     → SharedSurvival
//       엄호 후 포기  → CoverAndRetreat
//       즉시 유기     → AbandonAction
//       유기 목격     → TacticalBetrayal
//   - 변경 이유: 사용자 측 기존 본격 TrustMatrix.cs API 정합 (ITrustProvider / NGO 2.0)
//
// ─── v1.0 → v1.1 변경 사항 (2026-05-12) ────────────────────────────────────────
//   - AbandonRescueOutcome enum 을 WkSocialContracts.cs 에서 본 파일로 이전
//   - using TDA.PB4.Interfaces 제거 (외부 의존성 해소)
//   - 분리 이유: 본 enum 은 AI 내부 사용 (다른 파트 노출 X) → AI 도메인 안 배치 적절
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using UnityEngine;
using TDA.PB4.AI;   // ★ v1.2 — TrustMatrix / TrustChangeReason namespace

namespace TDA.PB4.AI.Social
{
    // ═══════════════════════════════════════════════════════════════════
    // ★ v1.1 — 본 파일 내부 enum 통합 (이전 WkSocialContracts.cs 에서 이전)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>유기/구출 분기 결과 — 3 종.</summary>
    public enum AbandonRescueOutcome
    {
        Rescue = 0,             // 위험 무릅쓰고 구출
        BriefSupport = 1,       // 잠시 엄호 후 포기
        Abandon = 2             // 즉시 유기
    }

    // ═══════════════════════════════════════════════════════════════════
    // AbandonRescueResolver 클래스
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 동료 위기 시 NPC 의 도덕적 선택 — 위험 무릅쓰고 구출 / 잠시 엄호 후 포기 / 즉시 유기.
    /// Empathy + Trust + 자기 보존 (fear) 종합 분기.
    /// 결과 = 본인 / 동료 / 파티 전체 trust 변동 + 태그 발현.
    /// </summary>
    public class AbandonRescueResolver : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════
        // Inspector 필드
        // ═══════════════════════════════════════════════════════════════════

        [Header("━━━ NPC 측 컴포넌트 참조 ━━━━━━━━━━━━━━━━")]

        [Tooltip("본인의 TrustMatrix (Wk4-1). 결과 적용 시 자기 trust 변동.\n" +
                 "★ v1.4 — Reset() / Awake() 에서 자동 GetComponent 호출 (Inspector 드래그 안 해도 자동 연결).")]
        [SerializeField] private TrustMatrix _selfTrust;

        // ═══════════════════════════════════════════════════════════════════
        // ★ v1.4 (2026-05-12) — _selfTrust 자동 연결
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Inspector 에서 컴포넌트 부착 시 자동 호출 — _selfTrust 자동 연결.
        /// 같은 GameObject 의 TrustMatrix 를 찾아 자동 할당.
        /// </summary>
        private void Reset()
        {
            if (_selfTrust == null)
            {
                _selfTrust = GetComponent<TrustMatrix>();
                if (_selfTrust == null)
                    _selfTrust = GetComponentInParent<TrustMatrix>();
            }
        }

        /// <summary>
        /// Play 진입 시 fallback — Reset() 안 호출된 경우 / Inspector 누락 방지.
        /// </summary>
        private void Awake()
        {
            if (_selfTrust == null)
            {
                _selfTrust = GetComponent<TrustMatrix>();
                if (_selfTrust == null)
                    _selfTrust = GetComponentInParent<TrustMatrix>();

                if (_selfTrust != null)
                    Debug.Log($"[Wk4-4 AbandonRescue] {name}: _selfTrust 자동 연결 (Awake fallback)", this);
                else
                    Debug.LogWarning($"[Wk4-4 AbandonRescue] {name}: ⚠ TrustMatrix 컴포넌트 없음 — _selfTrust 연결 실패", this);
            }
        }

        [Tooltip("Empathy 가중치 계산용 — empathy = stability × agreeable.")]
        [SerializeField] private float _stability = 0.5f;
        [SerializeField] private float _agreeable = 0.5f;
        // ★ 실제 사용 시 = HumanoidAIBrain 의 personality 참조 / 단독 사용 시 = Inspector 슬라이더

        [Header("━━━ 분기 임계 ━━━━━━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("Rescue 진입 — Empathy 이상 임계 (디폴트 0.65).")]
        [SerializeField, Range(0f, 1f)] private float _empathyThresholdHigh = 0.65f;

        [Tooltip("Abandon 진입 — Empathy 이하 임계 (디폴트 0.35).")]
        [SerializeField, Range(0f, 1f)] private float _empathyThresholdLow = 0.35f;

        [Tooltip("Abandon 보조 조건 — 자기 보존 (fear) 이상 임계 (디폴트 0.6).")]
        [SerializeField, Range(0f, 1f)] private float _fearThresholdHigh = 0.6f;

        [Tooltip("Rescue 보조 조건 — 동료 Trust 이상 임계 (디폴트 50).")]
        [SerializeField, Range(0f, 100f)] private float _allyTrustThresholdHigh = 50f;

        [Header("━━━ 결과 적용 강도 ━━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("Rescue 시 본인 trust +값.")]
        [SerializeField] private float _rescueSelfDelta = 50f;

        [Tooltip("Rescue 시 파티 trust +값.")]
        [SerializeField] private float _rescuePartyDelta = 40f;

        [Tooltip("BriefSupport 시 해당 동료 trust -값.")]
        [SerializeField] private float _briefSupportAllyDelta = -40f;

        [Tooltip("BriefSupport 시 타 동료 trust -값.")]
        [SerializeField] private float _briefSupportPartyDelta = -10f;

        [Tooltip("Abandon 시 해당 동료 trust -값.")]
        [SerializeField] private float _abandonAllyDelta = -100f;

        [Tooltip("Abandon 시 파티 trust -값.")]
        [SerializeField] private float _abandonPartyDelta = -30f;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━━━━")]

        [SerializeField] private bool _debugLog = false;

        // ═══════════════════════════════════════════════════════════════════
        // 공개 속성
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>본인의 Empathy 가중치 (stability × agreeable).</summary>
        public float Empathy => Mathf.Clamp01(_stability * _agreeable);

        // ═══════════════════════════════════════════════════════════════════
        // 이벤트
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>분기 결정 시 발행 (outcome, allyId).</summary>
        public event Action<AbandonRescueOutcome, string> OnSituationResolved;

        // ═══════════════════════════════════════════════════════════════════
        // 핵심 — 분기 결정
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 동료 위기 시 본인 NPC 의 3 분기 결정.
        /// </summary>
        /// <param name="allyId">위기 동료 ID.</param>
        /// <param name="allyTrust">위기 동료에 대한 본인의 Trust 수치 0~100.</param>
        /// <param name="ownFearLevel">본인의 현재 fear 0~1 (자기 보존).</param>
        /// <returns>3 분기 중 결정 결과.</returns>
        public AbandonRescueOutcome ResolveSituation(string allyId, float allyTrust, float ownFearLevel)
        {
            float empathy = Empathy;
            AbandonRescueOutcome outcome;
            string reasonText;

            // 분기 1 — 위험 무릅쓰고 구출 (Rescue)
            if (empathy >= _empathyThresholdHigh && allyTrust > _allyTrustThresholdHigh)
            {
                outcome = AbandonRescueOutcome.Rescue;
                reasonText = $"Empathy {empathy:F2} ≥ {_empathyThresholdHigh} + Trust {allyTrust:F0} > {_allyTrustThresholdHigh}";
            }
            // 분기 3 — 즉시 유기 (Abandon)
            else if (empathy < _empathyThresholdLow && ownFearLevel > _fearThresholdHigh)
            {
                outcome = AbandonRescueOutcome.Abandon;
                reasonText = $"Empathy {empathy:F2} < {_empathyThresholdLow} + Fear {ownFearLevel:F2} > {_fearThresholdHigh}";
            }
            // 분기 2 — 잠시 엄호 후 포기 (BriefSupport) — 기본값
            else
            {
                outcome = AbandonRescueOutcome.BriefSupport;
                reasonText = $"중간 — Empathy {empathy:F2} / Fear {ownFearLevel:F2} / Trust {allyTrust:F0}";
            }

            if (_debugLog)
            {
                Debug.Log($"<color=#F472B6>[Wk4-4 AbandonRescue]</color> {gameObject.name} → " +
                          $"동료 [{allyId}] 위기 → <b>{outcome}</b> ({reasonText})", this);
            }

            OnSituationResolved?.Invoke(outcome, allyId);
            return outcome;
        }

        // ═══════════════════════════════════════════════════════════════════
        // 결과 적용 — Trust 변동 + 태그 발현
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 분기 결과를 실제 Trust 매트릭스에 적용.
        /// </summary>
        /// <param name="outcome">ResolveSituation 결과.</param>
        /// <param name="allyId">위기 동료 ID (디버그 / 진단).</param>
        /// <param name="allyTrustMatrix">위기 동료의 TrustMatrix (해당 동료 측 영향).</param>
        /// <param name="partyTrustMatrices">파티 다른 멤버들의 TrustMatrix (목격 영향).</param>
        public void ApplyOutcome(
            AbandonRescueOutcome outcome,
            string allyId,
            TrustMatrix allyTrustMatrix,
            TrustMatrix[] partyTrustMatrices)
        {
            switch (outcome)
            {
                case AbandonRescueOutcome.Rescue:
                    // 본인 +50 / 파티 +40 / "생명의 은인" 태그
                    _selfTrust?.ChangeTrust(_rescueSelfDelta, TrustChangeReason.RescueAction);

                    if (partyTrustMatrices != null)
                    {
                        foreach (var pt in partyTrustMatrices)
                        {
                            pt?.ChangeTrust(_rescuePartyDelta, TrustChangeReason.SharedSurvival);
                        }
                    }

                    // TODO Wk5+ — PersonalityTagResolver 에 "생명의 은인" 태그 발현
                    // TagResolver?.AddTag(gameObject, "생명의 은인");
                    break;

                case AbandonRescueOutcome.BriefSupport:
                    // 해당 동료 -40 / 파티 -10
                    allyTrustMatrix?.ChangeTrust(_briefSupportAllyDelta, TrustChangeReason.CoverAndRetreat);

                    if (partyTrustMatrices != null)
                    {
                        foreach (var pt in partyTrustMatrices)
                        {
                            pt?.ChangeTrust(_briefSupportPartyDelta, TrustChangeReason.CoverAndRetreat);
                        }
                    }
                    break;

                case AbandonRescueOutcome.Abandon:
                    // 해당 동료 -100 / 파티 -30 / "비겁자" 태그 + 유기된 자 "Betrayed" 태그
                    allyTrustMatrix?.ChangeTrust(_abandonAllyDelta, TrustChangeReason.AbandonAction);

                    if (partyTrustMatrices != null)
                    {
                        foreach (var pt in partyTrustMatrices)
                        {
                            pt?.ChangeTrust(_abandonPartyDelta, TrustChangeReason.TacticalBetrayal);
                        }
                    }

                    // TODO Wk5+ — PersonalityTagResolver
                    // TagResolver?.AddTag(gameObject, "비겁자");
                    // TagResolver?.AddTag(allyId, "Betrayed");
                    // TagResolver?.AddTag(allyId, "Self_Reliant");
                    // ★ 유기된 자 태그 = Wk6 RAG 코사인 검색 입력
                    break;
            }

            if (_debugLog)
            {
                Debug.Log($"<color=#F472B6>[Wk4-4 AbandonRescue]</color> ApplyOutcome 완료 → " +
                          $"<b>{outcome}</b> / 동료 [{allyId}] / 파티 {partyTrustMatrices?.Length ?? 0} 명", this);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // ContextMenu 디버그
        // ═══════════════════════════════════════════════════════════════════

        [ContextMenu("[Wk4-4] Print Current State")]
        private void PrintState()
        {
            Debug.Log($"═══ Wk4-4 AbandonRescueResolver State ═══\n" +
                      $"  NPC               = {gameObject.name}\n" +
                      $"  Stability         = {_stability:F2}\n" +
                      $"  Agreeable         = {_agreeable:F2}\n" +
                      $"  Empathy           = {Empathy:F2}\n" +
                      $"  Thresholds        = High({_empathyThresholdHigh}) / Low({_empathyThresholdLow})\n" +
                      $"  Self Trust        = {(_selfTrust != null ? _selfTrust.CurrentTrust.ToString("F1") : "<없음>")}", this);
        }

        [ContextMenu("[Wk4-4] Test: Rescue Scenario (Empathy 0.8, Trust 75, Fear 0.2)")]
        private void TestRescue()
        {
            _stability = 0.9f; _agreeable = 0.89f;   // empathy ≈ 0.8
            var outcome = ResolveSituation("TestAlly_Marcus", 75f, 0.2f);
            Debug.Log($"[Wk4-4 Test] Rescue 시나리오 → <b>{outcome}</b>", this);
        }

        [ContextMenu("[Wk4-4] Test: BriefSupport Scenario (Empathy 0.5, Trust 60, Fear 0.4)")]
        private void TestBriefSupport()
        {
            _stability = 0.7f; _agreeable = 0.71f;   // empathy ≈ 0.5
            var outcome = ResolveSituation("TestAlly_Marcus", 60f, 0.4f);
            Debug.Log($"[Wk4-4 Test] BriefSupport 시나리오 → <b>{outcome}</b>", this);
        }

        [ContextMenu("[Wk4-4] Test: Abandon Scenario (Empathy 0.2, Trust 30, Fear 0.8)")]
        private void TestAbandon()
        {
            _stability = 0.4f; _agreeable = 0.5f;   // empathy ≈ 0.2
            var outcome = ResolveSituation("TestAlly_Marcus", 30f, 0.8f);
            Debug.Log($"[Wk4-4 Test] Abandon 시나리오 → <b>{outcome}</b>", this);
        }
    }
}