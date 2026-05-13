// ═══════════════════════════════════════════════════════════════════════════════
// Wk4-6 — HumanoidAIBrain.Dilemma.cs (partial v2.0)
// ═══════════════════════════════════════════════════════════════════════════════
// DilemmaPivot 7 노드 입력 — AcceptanceScore + TrustMatrix + Trauma 종합 분기.
// Wk5+ IGroupCommandReceiver 의 prototype.
//
// 의존: HumanoidAIBrain 본체 (trustMatrix / traumaSystem / fear 필드)
//        DilemmaPivotSO (★ 시나리오팀 제공 — 본 파일은 placeholder)
// 호출자: 시나리오 시스템 (Bridge 라우팅 통한 IssueDilemmaCommand)
//
// 위치: Assets/Scripts/pB-4/week4_AI_Social/HumanoidAIBrain.Dilemma.cs
// 버전: v2.1 (2026-05-12) — ITraumaProvider 패턴 채택 (본체 line 628 정합)
//
// ─── v2.0 → v2.1 변경 사항 (2026-05-12) — 컴파일 에러 해소 ─────────────────────
//   - traumaSystem.ComputeFearModifier(fear, trust) → ITraumaProvider.GetFearMultiplier()
//     이유: 기존 TraumaSystem 에 ComputeFearModifier 메서드 없음
//           본체 HumanoidAIBrain.cs line 628 패턴 채택 (자연스러운 정합)
//   - using TDA.PB4.Interfaces.Intelligence 추가 (ITraumaProvider namespace)
//   - 패턴:
//       float adjustedFear = fear;
//       if (traumaSystem != null && traumaSystem is ITraumaProvider traumaProv)
//           adjustedFear = Mathf.Clamp01(fear * traumaProv.GetFearMultiplier());
//
// ─── v1.x → v2.0 변경 사항 (2026-05-12) — 본체 정합화 ──────────────────────────
//
// [변경]
//   - using TDA.PB4.AI.Social → using TDA.PB4.AI
//   - _trustMatrix / _traumaSystem SerializeField 제거
//     → 본체 trustMatrix / traumaSystem 직접 사용 (partial class)
//   - currentFear 참조 → 본체 fear
//   - _trustMatrix.Value → trustMatrix.CurrentTrust (기존 TrustMatrix.cs API)
//   - _trustMatrix.AdjustTrust(delta, reason)
//     → trustMatrix.ChangeTrust(delta, TrustChangeReason.Manual) (기존 API)
//   - _trustMatrix.CurrentTier → trustMatrix.CurrentTier (기존 API 동일)
//   - TrustMatrix.TrustTier.Cooperation → TrustTier.Cooperation (top-level enum)
//   - _traumaSystem.ComputeFearModifier(fear, trust)
//     → ★ 임시 처리 (본체 TraumaSystem 에 본 메서드 추가 필요 — README 가이드)
//
// 기반: 기술개발 개론서 v11 §5.4.2 / Wk4 의제 종합 v2 §2.6
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using UnityEngine;
using TDA.PB4.Interfaces;
using TDA.PB4.AI;          // ★ v2.0 — TrustMatrix / TrustTier / TrustChangeReason
using TDA.PB4.Interfaces.Intelligence;   // ★ v2.1 — ITraumaProvider (본체 line 628 패턴)

namespace TDA.PB4.AI.Humanoid
{
    /// <summary>
    /// HumanoidAIBrain 의 DilemmaPivot 평가 부분 (partial).
    /// 본 partial = Wk4-6 의제 본격 구현 + Wk5+ IGroupCommandReceiver prototype.
    /// </summary>
    public partial class HumanoidAIBrain
    {
        // ═══════════════════════════════════════════════════════════════════
        // Inspector 필드 (Wk4-6)
        // ═══════════════════════════════════════════════════════════════════
        
        [Header("━━━ DilemmaPivot (Wk4-6) ━━━━━━━━━━━━━━━━━━")]
        
        [Tooltip("Path A (Comply) 진입 임계 — AcceptanceScore 이상.")]
        [SerializeField, Range(0f, 1f)] private float _complyThreshold = 0.6f;
        
        [Tooltip("Path C (StrongRefuse) 진입 임계 — AcceptanceScore 이하.")]
        [SerializeField, Range(0f, 1f)] private float _strongRefuseThreshold = 0.3f;
        
        [Tooltip("Karma 변동 강도 (Path C 시 음수).")]
        [SerializeField] private float _karmaDeltaOnRefuse = -30f;
        
        [Tooltip("Trust 변동 강도 (자기 Trust 변화).")]
        [SerializeField] private float _trustDeltaOnComply = -5f;       // 도덕 부담
        [SerializeField] private float _trustDeltaOnRefuse = +10f;      // 자기 가치 확인
        
        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━━━━")]
        
        [SerializeField] private bool _dilemmaDebugLog = false;
        
        // ★ v2.0 — trustMatrix / traumaSystem / fear 는 본체 필드 직접 사용 (partial)
        
        // ═══════════════════════════════════════════════════════════════════
        // 이벤트
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>DilemmaPivot 해결 시 발행 — 시나리오팀 분기 결정 입력.</summary>
        public event Action<DilemmaOutcome> OnDilemmaResolved;
        
        // ═══════════════════════════════════════════════════════════════════
        // 핵심 메서드 — DilemmaPivot 평가
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// DilemmaPivot 노드 진입 시 본인 NPC 의 분기 결정.
        /// AcceptanceScore (Wk4-2) + TrustMatrix (Wk4-1) + Trauma (Wk4-3) 종합.
        /// </summary>
        public DilemmaOutcome EvaluateDilemma(float severity, float scenarioModifier, string commandId)
        {
            // 1. ScenarioCommandRequest 구성
            var cmd = new ScenarioCommandRequest
            {
                severity = severity,
                scenarioModifier = scenarioModifier,
                type = ScenarioCommandType.MoralChoice,
                commandId = commandId
            };
            
            // 2. Wk4-2 EvaluateAcceptance 호출 (5축 + Trust + complexity 분기)
            float acceptance = EvaluateAcceptance(cmd);
            
            // 3. Wk4-3 트라우마 보정 (현재 fear 에 적용)
            //    ★ v2.1 — 본체 line 628 패턴 채택 (ITraumaProvider.GetFearMultiplier)
            //    기존 TraumaSystem 에 ComputeFearModifier 없음 → 본체와 동일 ITraumaProvider 사용
            float adjustedFear = fear;
            if (traumaSystem != null && traumaSystem is ITraumaProvider traumaProv)
                adjustedFear = Mathf.Clamp01(fear * traumaProv.GetFearMultiplier());
            
            // adjustedFear 가 큰 경우 = 분기 결정에 영향 (acceptance 추가 감소)
            float fearPenalty = adjustedFear * 0.3f;
            float finalAcceptance = Mathf.Clamp01(acceptance - fearPenalty);
            
            // 4. 분기 결정
            DilemmaPath path;
            float karmaDelta;
            float trustDelta;
            string reasonText;
            
            if (finalAcceptance >= _complyThreshold)
            {
                path = DilemmaPath.Comply;
                karmaDelta = 0f;
                trustDelta = _trustDeltaOnComply;
                reasonText = $"Comply — Acceptance {finalAcceptance:F2} ≥ {_complyThreshold}";
            }
            else if (finalAcceptance <= _strongRefuseThreshold)
            {
                path = DilemmaPath.StrongRefuse;
                karmaDelta = _karmaDeltaOnRefuse;
                trustDelta = _trustDeltaOnRefuse;
                reasonText = $"StrongRefuse — Acceptance {finalAcceptance:F2} ≤ {_strongRefuseThreshold}";
            }
            else
            {
                path = DilemmaPath.PartialRefuse;
                karmaDelta = _karmaDeltaOnRefuse * 0.5f;
                trustDelta = 0f;
                reasonText = $"PartialRefuse — Acceptance {finalAcceptance:F2} (중간)";
            }
            
            // 5. Trust 변동 적용 — 본체 trustMatrix 의 ChangeTrust 사용 (기존 API)
            if (trustMatrix != null && Mathf.Abs(trustDelta) > 0.01f)
            {
                trustMatrix.ChangeTrust(trustDelta, TrustChangeReason.Manual);
            }
            
            // 6. 결과 구성
            var outcome = new DilemmaOutcome
            {
                path = path,
                acceptanceScore = finalAcceptance,
                trustTier = (int)(trustMatrix != null ? trustMatrix.CurrentTier : TrustTier.Cooperation),
                karmaDelta = karmaDelta,
                trustDelta = trustDelta,
                reason = reasonText
            };
            
            // 7. event 발행 (시나리오팀 분기 결정 입력)
            OnDilemmaResolved?.Invoke(outcome);
            
            if (_dilemmaDebugLog)
            {
                Debug.Log($"<color=#FB923C>[Wk4-6 Dilemma]</color> {gameObject.name}: " +
                          $"cmd [{commandId}] / sev {severity:F2} → Acceptance {acceptance:F2} - fearPenalty {fearPenalty:F2} = {finalAcceptance:F2} " +
                          $"→ <b>{path}</b> (Karma Δ{karmaDelta:F0} / Trust Δ{trustDelta:F0})", this);
            }
            
            return outcome;
        }
        
        // ═══════════════════════════════════════════════════════════════════
        // Wk5+ IGroupCommandReceiver prototype — Bridge 라우팅 통한 호출
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// 시나리오 → AI 명령 채널의 첫 prototype.
        /// Wk5+ IGroupCommandReceiver 본격 정의 시 본 메서드가 해당 인터페이스 구현.
        /// </summary>
        public DilemmaOutcome IssueDilemmaCommand(float severity, float scenarioModifier, string commandId)
        {
            return EvaluateDilemma(severity, scenarioModifier, commandId);
        }
        
        // ═══════════════════════════════════════════════════════════════════
        // ContextMenu 디버그
        // ═══════════════════════════════════════════════════════════════════
        
        [ContextMenu("[Wk4-6] Print Dilemma Test (severity 0.5)")]
        private void PrintDilemmaTest()
        {
            var outcome = EvaluateDilemma(0.5f, 1.0f, "TestDilemma");
            Debug.Log($"═══ Wk4-6 Dilemma Test ═══\n" +
                      $"  NPC               = {gameObject.name}\n" +
                      $"  Path              = <b>{outcome.path}</b>\n" +
                      $"  AcceptanceScore   = {outcome.acceptanceScore:F2}\n" +
                      $"  TrustTier         = {outcome.trustTier}\n" +
                      $"  Karma Delta       = {outcome.karmaDelta:F0}\n" +
                      $"  Trust Delta       = {outcome.trustDelta:F0}\n" +
                      $"  Reason            = {outcome.reason}", this);
        }
        
        [ContextMenu("[Wk4-6] Test: High Severity Moral Choice (0.85)")]
        private void TestHighSeverity()
        {
            var outcome = EvaluateDilemma(0.85f, 1.0f, "도시약탈명령");
            Debug.Log($"[Wk4-6 Test] 도시약탈명령 → <b>{outcome.path}</b> (Acc {outcome.acceptanceScore:F2})", this);
        }
        
        [ContextMenu("[Wk4-6] Test: Low Severity (0.2)")]
        private void TestLowSeverity()
        {
            var outcome = EvaluateDilemma(0.2f, 1.0f, "단순요청");
            Debug.Log($"[Wk4-6 Test] 단순요청 → <b>{outcome.path}</b> (Acc {outcome.acceptanceScore:F2})", this);
        }
    }
}
