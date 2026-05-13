// ═══════════════════════════════════════════════════════════════════════════════
// Wk4-2 — HumanoidAIBrain.Acceptance.cs (partial v2.0)
// ═══════════════════════════════════════════════════════════════════════════════
// AcceptanceScore 옵션 (다) 통합안 — complexity 분기 (선형 vs 비선형).
// ★ [09] BLOCKING-1 해소 — PB4Interfaces.cs AcceptanceScore 시그니처 동결.
//
// 의존: HumanoidAIBrain 본체 (TrustMatrix / PersonalityMatrix / fear 필드)
// 호출자: Wk4-6 DilemmaPivot / 시나리오 → AI 명령 채널
//
// 위치: Assets/Scripts/pB-4/week4_AI_Social/HumanoidAIBrain.Acceptance.cs
// 버전: v2.0 (2026-05-12) — ★ 본체 필드 직접 사용 (대대적 재작성)
//
// ─── v1.x → v2.0 변경 사항 (2026-05-12) — 본체 정합화 ──────────────────────────
//
// [발견 — 사용자 측 본체 구조]
//   - BaseAIBrain 에 fear 필드 이미 존재 (line 51) — currentFear 아님
//   - HumanoidAIBrain 에 trustMatrix SerializeField 이미 존재 (line 138)
//   - HumanoidAIBrain 에 traumaSystem SerializeField 이미 존재 (line 139)
//   - 기존 TrustMatrix.cs = NGO 2.0 N:M ITrustProvider 본격 구현
//     namespace: TDA.PB4.AI (★ Social 아님)
//     TrustTier: namespace top-level (nested 아님)
//     주요 메서드: CurrentTrust / CurrentTier / ChangeTrust(delta, TrustChangeReason)
//
// [변경]
//   - using TDA.PB4.AI.Social → using TDA.PB4.AI
//   - _trustMatrix SerializeField 제거 → 본체 trustMatrix 직접 사용 (partial class)
//   - _currentFear SerializeField 제거 → 본체 fear 직접 사용
//   - currentFear 참조 → fear
//   - _trustMatrix.ComputeAcceptanceBase(...) → trustMatrix.ComputeAcceptanceBase(...)
//     ★ 기존 TrustMatrix.cs 에 본 메서드 추가 필요 (README 가이드 참조)
//
// 기반: 기술개발 개론서 v11 §5.4.3 A / Wk4 의제 종합 v2 §2.2
// ═══════════════════════════════════════════════════════════════════════════════

using UnityEngine;
using TDA.PB4.Interfaces;
using TDA.PB4.AI;          // ★ v2.0 — TrustMatrix 의 실제 namespace
 
namespace TDA.PB4.AI.Humanoid
{
    /// <summary>
    /// HumanoidAIBrain 의 AcceptanceScore 평가 부분 (partial).
    /// 본 partial = Wk4-2 의제 본격 구현.
    /// </summary>
    public partial class HumanoidAIBrain
    {
        // ═══════════════════════════════════════════════════════════════════
        // Inspector 필드 (Wk4-2)
        // ═══════════════════════════════════════════════════════════════════
        
        [Header("━━━ AcceptanceScore (Wk4-2) ━━━━━━━━━━━━")]
        
        [Tooltip("인격 복잡도 임계. complexity > _complexityThreshold → 옵션 B 비선형 곡선 사용.")]
        [SerializeField, Range(0f, 1f)] private float _complexityThreshold = 0.5f;
        // ★ Wk4 임시 0.5 / Wk6 1000회 자동화 결과로 0.6 본격 튜닝
        
        [Tooltip("옵션 B 비선형 곡선 (고난도 결정 표현). 복잡 인격 NPC 에 적용.")]
        [SerializeField] private AnimationCurve _personalityComplexityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        
        [Header("━━━ 5축 가중치 (linearScore) ━━━━━━━━━━━━")]
        
        [SerializeField, Range(-2f, 2f)] private float _wBrutality = 0.15f;
        [SerializeField, Range(-2f, 2f)] private float _wLoyalty   = 0.30f;
        [SerializeField, Range(-2f, 2f)] private float _wGreed     = 0.10f;
        [SerializeField, Range(-2f, 2f)] private float _wEmpathy   = 0.25f;
        [SerializeField, Range(-2f, 2f)] private float _wCunning   = 0.20f;
        
        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━━━━")]
        
        [SerializeField] private bool _acceptanceDebugLog = false;
        
        // ★ v2.0 — TrustMatrix / fear 는 본체 필드 직접 사용 (partial class)
        // 본체: BaseAIBrain.fear (line 51) / HumanoidAIBrain.trustMatrix (line 138)
        
        // ═══════════════════════════════════════════════════════════════════
        // 핵심 메서드 — AcceptanceScore 평가 (옵션 (다) 통합안)
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// ★ [09] BLOCKING-1 해소 — AcceptanceScore 시그니처 동결.
        /// 5축 가중 합 + TrustMatrix 결합 + complexity 분기 (선형 vs 비선형).
        /// </summary>
        /// <param name="cmd">시나리오 측이 제공하는 명령 요청.</param>
        /// <returns>AcceptanceScore (0~1, 단 일부 극단 케이스 음수 가능).</returns>
        public float EvaluateAcceptance(ScenarioCommandRequest cmd)
        {
            // 1. 5축 가중 합 (선형 점수)
            // ★ v1.3 — brutality 5축 조합 (PersonalityMatrix 에 brutality 필드 없음)
            //   추정: 우호성 낮음 + 안정성 낮음 = 잔혹함 (감정 통제 부족)
            float brutality = (1f - personality.agreeable) * (1f - personality.stability);
            float loyalty   = 1f - personality.directness;      // 직설성 반대
            float greed     = 1f - personality.agreeable;       // 우호성 반대
            float empathy   = personality.stability * personality.agreeable;
            float cunning   = personality.openness * (1f - personality.directness);
            
            float linearScore = _wBrutality * brutality
                              + _wLoyalty   * loyalty
                              + _wGreed     * greed
                              + _wEmpathy   * empathy
                              + _wCunning   * cunning;
            
            // 2. TrustMatrix 결합 — 본체 trustMatrix 직접 사용 (partial class)
            //    ★ 기존 TrustMatrix.cs 에 ComputeAcceptanceBase 메서드 추가 필요 (README 가이드)
            float trustBase = trustMatrix != null
                ? trustMatrix.ComputeAcceptanceBase(cmd.severity, fear)
                : 0f;
            
            linearScore += trustBase;
            
            // 3. 분기 — complexity 임계
            float complexity = ComputeComplexity();
            float finalScore;
            string branchName;
            
            if (complexity > _complexityThreshold)
            {
                // 옵션 B — 비선형 곡선 (복잡 인격 → 망설임 / 후회 표현)
                float clampedInput = Mathf.Clamp01(linearScore);
                finalScore = _personalityComplexityCurve.Evaluate(clampedInput) * cmd.scenarioModifier;
                branchName = "옵션 B 비선형";
            }
            else
            {
                // 옵션 A — 단순 선형 (단순 인격 → 즉답)
                finalScore = linearScore * cmd.scenarioModifier;
                branchName = "옵션 A 선형";
            }
            
            // 4. 디버그 로그
            if (_acceptanceDebugLog)
            {
                Debug.Log($"<color=#A78BFA>[Wk4-2 Acceptance]</color> {gameObject.name}: " +
                          $"linear={linearScore:F2} / complexity={complexity:F2} ({branchName}) " +
                          $"→ <b>{finalScore:F2}</b> (cmd: {cmd.commandId})", this);
            }
            
            return finalScore;
        }
        
        // ═══════════════════════════════════════════════════════════════════
        // 인격 복잡도 계산 (PersonalityMatrix 5축 → complexity)
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// 5축 → 인격 복잡도 계산. 본 메서드 = Wk0 동결 PersonalityMatrix 변경 X.
        /// 공식: 비-단순한 조합 = (openness × stability × (1 - control)).
        /// </summary>
        public float ComputeComplexity()
        {
            // 단순 사람 = control 높음 / openness 낮음 / stability 낮음 → complexity 낮음
            // 복잡 사람 = openness 높음 / stability 높음 / control 낮음 → complexity 높음
            float complexity = personality.openness 
                             * personality.stability 
                             * (1f - personality.control);
            
            return Mathf.Clamp01(complexity);
        }
        
        // ═══════════════════════════════════════════════════════════════════
        // ContextMenu (Inspector 디버그)
        // ═══════════════════════════════════════════════════════════════════
        
        [ContextMenu("[Wk4-2] Print Acceptance Test (default cmd)")]
        private void PrintAcceptanceTest()
        {
            var cmd = ScenarioCommandRequest.Default;
            cmd.commandId = "TestCommand";
            cmd.severity = 0.5f;
            
            float score = EvaluateAcceptance(cmd);
            float complexity = ComputeComplexity();
            
            float brutality = (1f - personality.agreeable) * (1f - personality.stability);
            
            Debug.Log($"═══ Wk4-2 Acceptance Test ═══\n" +
                      $"  NPC                = {gameObject.name}\n" +
                      $"  Personality        = b{brutality:F2} l{(1-personality.directness):F2} " +
                      $"g{(1-personality.agreeable):F2} e{(personality.stability*personality.agreeable):F2} " +
                      $"c{(personality.openness*(1-personality.directness)):F2}\n" +
                      $"  Complexity         = {complexity:F2}\n" +
                      $"  Branch             = {(complexity > _complexityThreshold ? "옵션 B 비선형" : "옵션 A 선형")}\n" +
                      $"  Threshold          = {_complexityThreshold:F2}\n" +
                      $"  Trust              = {(trustMatrix != null ? trustMatrix.CurrentTrust.ToString("F1") : "<없음>")}\n" +
                      $"  Fear (본체)        = {fear:F2}\n" +
                      $"  Final Score        = {score:F2}", this);
        }
        
        [ContextMenu("[Wk4-2] Test: MoralChoice (severity 0.8)")]
        private void TestMoralChoice()
        {
            var cmd = new ScenarioCommandRequest
            {
                severity = 0.8f,
                scenarioModifier = 1.0f,
                type = ScenarioCommandType.MoralChoice,
                commandId = "MoralChoiceTest"
            };
            float score = EvaluateAcceptance(cmd);
            Debug.Log($"[Wk4-2 Test] MoralChoice → Acceptance = <b>{score:F2}</b>", this);
        }
        
        [ContextMenu("[Wk4-2] Test: DangerousOrder (severity 0.95)")]
        private void TestDangerousOrder()
        {
            var cmd = new ScenarioCommandRequest
            {
                severity = 0.95f,
                scenarioModifier = 1.0f,
                type = ScenarioCommandType.DangerousOrder,
                commandId = "DangerousTest"
            };
            float score = EvaluateAcceptance(cmd);
            Debug.Log($"[Wk4-2 Test] DangerousOrder → Acceptance = <b>{score:F2}</b>", this);
        }
    }
}
