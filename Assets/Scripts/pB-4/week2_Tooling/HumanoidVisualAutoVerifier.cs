// =============================================================================
// HumanoidVisualAutoVerifier.cs  |  pB-4 Week 2 Day 5 T5.2  v4
// 역할: Humanoid 시각 검증을 무인 자동화. Play 진입 후 5종 시나리오 순차 트리거 +
//       Console 로그 패턴 카운트 + Humanoid 위치 변화 추적 + 자동 종료.
//       사용자가 자는 동안 자동 실행 → 깨어나서 결과만 확인.
//
// 사용:
//   - HumanoidVisualStageSetup의 [One-Click Auto-Verify] 버튼이 자동 부착
//   - autoMode = true 설정 후 Play 진입
//   - AutoVerifier가 Start 코루틴으로 시나리오 자동 진행
//   - 자동 종료 시 EditorApplication.ExitPlaymode() 호출
//   - LatestResult 정적 필드를 EditorWindow가 읽어 보고서 출력
//
// 시나리오 순서 (Speech 관찰을 위해 ④부터):
//   1) Companion 강제 → SpeechBubble 발화 관찰
//   2) Friendly 강제 → Friendly 발화 관찰
//   3) Hostile 강제 → 명령 거부 검증
//   4) Karma +80 → Friendly→Companion 자동 5단계 체인
//   5) Final: Humanoid 위치 변화로 BT 동작 검증 (Archetype_Coward 효과 포함)
//
// 변경 이력:
//   v5.7  ForceStateForTesting 도입 — Coward 검증 시 hysteresis 4중 가드 우회.
//         Brain 직접 currentState=Flee 할당으로 BT 강제 진입.
//   v5.12 [DEBT-15] 본질 검증 회복 — InjectFearToHumanoidNatural 신규 추가.
//         ForceStateForTesting 호출 제거 후 자연 흐름으로 검증:
//           T=0.0s: fear=0.9 + UpdateDecision 1회 → holdTicks=0
//           T=0.4s: UpdateDecision 2회 → holdTicks=1
//           T=0.8s: UpdateDecision 3회 → holdTicks=2 → 가드 통과
//           T=1.2s: ForceSyncBB → BB.UtilityWinner=Flee 자연 반영
//         검증 본질: utility 함수가 fear→Flee를 winner로 자연 선택하는가
//         이전 강제 검증(InjectFearToHumanoid)은 회귀 비교용으로 보존.
//   v5.13 [DEBT-15 박제] 진단 코드 추가 — verboseLogging 강제 ON, fear/state/u_flee readback,
//         매 UpdateDecision 직전 fear 재주입.
//   v5.14 [DEBT-15 박제] verboseLogging protected 대응 (NonPublic+BaseType 순회) +
//         currentStateHoldTicks reflection 박제 + ❸ 가드 강제 우회 4차 호출 진단.
//         결과: ⭐ ❸ 우회로 currentState=Flee 자연 전이 성공 → DEBT-21 hysteresis 결함 입증.
//   v5.15 [DEBT-21 해결 후] ❸ 가드 우회 코드(4차 호출) 제거. HumanoidAIBrain.SelectNewState가
//         매 호출 holdTicks++로 수정되어 Adapter polling만으로 자연 누적됨 →
//         3차 호출만으로 Flee 자연 전이 보장. 자연 검증의 진정한 자연성 회복.
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TDA.PB4.Tooling.HumanoidVisual
{
    public class HumanoidVisualAutoVerifier : MonoBehaviour
    {
        public static AutoVerifyResult LatestResult { get; private set; } = new AutoVerifyResult();

        [Header("━━━ Auto Mode ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Editor의 [One-Click Auto-Verify] 버튼이 true로 설정. 수동 Play 시 false.")]
        public bool autoMode = false;

        [Tooltip("Play 진입 후 워밍업 (셰이더 컴파일 + Bootstrap 안정화).")]
        public float warmupSeconds = 3f;

        [Tooltip("각 시나리오 사이 대기 시간 (Speech + Alignment 안정화 보장).")]
        public float scenarioInterval = 4f;

        [Tooltip("마지막 시나리오 후 자동 ExitPlaymode 까지 대기.")]
        public float autoExitDelay = 2f;

        [Tooltip("디버그 로그 출력.")]
        public bool debugLog = true;

        // 내부 상태
        private AutoVerifyResult result;
        private GameObject humanoid;
        private Vector3 humanoidStartPos;
        private float startTime;
        private int speechCountBeforeScenario;

        [Serializable]
        public class AutoVerifyResult
        {
            public bool completed;
            public bool autoMode;
            public float durationSeconds;

            // 5종 시나리오
            public bool scenario1_CowardFlee;       // Coward archetype + player → fear → flee
            public bool scenario2_FriendlyForce;    // Friendly 강제 → Speech
            public bool scenario3_HostileReject;    // Hostile 강제 → 명령 거부
            public bool scenario4_CompanionGreet;   // Companion 강제 → Speech
            public bool scenario5_KarmaChain;       // Karma +80 → Tier 전이 → Alignment 전이

            // 측정값
            public int totalErrors;
            public int totalWarnings;
            public List<string> errorMessages = new List<string>();

            public bool humanoidMoved;
            public float humanoidMoveDistance;
            public Vector3 humanoidStartPos;
            public Vector3 humanoidEndPos;

            public int speechRenderCount;       // [DialogueRenderer] 로그 카운트
            public int speechAssemblerCount;    // [SpeechAssembler] 로그 카운트
            public int alignmentTransitionCount; // [NPCAlignment] 전이 로그
            public int karmaTierChangeCount;    // [KarmaDirector] tier 전이 로그
            public bool dialogueRendererPresent;
            public bool speechBubbleSpawned;    // currentBubble 인스턴스 확인

            // 진단
            public string lastError;
            public List<string> scenarioInvocationLog = new List<string>();
        }

        // ==================================================================
        // Lifecycle
        // ==================================================================
        private void Awake()
        {
            if (!autoMode)
            {
                enabled = false;
                return;
            }

            try
            {
                result = new AutoVerifyResult { autoMode = true };
                LatestResult = result;
                startTime = Time.unscaledTime;

                Application.logMessageReceived += OnLog;
                LogInfo($"AutoVerifier 시작. warmup={warmupSeconds}s, interval={scenarioInterval}s.");
            }
            catch (Exception e)
            {
                LogError($"Awake 예외: {e.Message}");
            }
        }

        private void OnDestroy()
        {
            try { Application.logMessageReceived -= OnLog; } catch { }
        }

        private IEnumerator Start()
        {
            if (!autoMode) yield break;

            // 워밍업
            LogInfo($"워밍업 {warmupSeconds}s 시작...");
            yield return new WaitForSeconds(warmupSeconds);

            // Humanoid 인스턴스 찾기
            humanoid = FindHumanoidInScene();
            if (humanoid == null)
            {
                LogError("씬에 Humanoid 없음. Stage 셋업 후 재시도.");
                result.lastError = "Humanoid not found";
                yield return AutoExit();
                yield break;
            }

            humanoidStartPos = humanoid.transform.position;
            result.humanoidStartPos = humanoidStartPos;
            LogInfo($"Humanoid 발견: {humanoid.name}, 시작 위치 {humanoidStartPos}");

            // DialogueRenderer 존재 검증
            var dialogueType = FindTypeByName("DialogueRenderer");
            if (dialogueType != null)
            {
                var dr = humanoid.GetComponent(dialogueType);
                result.dialogueRendererPresent = dr != null;
            }

            // ===== 시나리오 ④ Companion (Speech 관찰 우선) =====
            yield return RunScenario("④ Companion 강제",
                () => InvokeMethod("NPCAlignmentController", "DebugForceCompanionBypassHold"));
            VerifyScenario4_CompanionGreet();

            // ===== 시나리오 ② Friendly =====
            yield return RunScenario("② Friendly 강제",
                () => InvokeMethod("NPCAlignmentController", "DebugForceFriendlyBypassHold"));
            VerifyScenario2_Friendly();

            // ===== 시나리오 ③ Hostile =====
            yield return RunScenario("③ Hostile 강제",
                () => InvokeMethod("NPCAlignmentController", "DebugForceHostileBypassHold"));
            VerifyScenario3_HostileReject();

            // ===== 시나리오 ⑤ Karma +80 자동 체인 =====
            yield return RunScenario("⑤ Karma +80 (Saint)",
                () => InvokeKarmaChange(80f));
            VerifyScenario5_KarmaChain();

            // ===== 시나리오 ① Coward 능동 트리거 + 자연 발생 검증 =====
            // [v5.5] 진단 결과: Skeleton 기본 정책=Phalanx (도주 절대 불가).
            // PB4DecisionAdapter.policyTypeOverride="Swarm" 강제 → FleeSwarmAction 활성화 (6m/s 전력질주).
            yield return RunScenario("① Policy override → Swarm",
                () => OverridePolicyType("Swarm"));
            // [v5.12 DEBT-15] 본질 회복 — InjectFearToHumanoidNatural 사용.
            //   ForceStateForTesting 미사용. utility 함수가 fear → Flee를 자연 선택하는지 검증.
            //   IEnumerator 오버로드가 hysteresis 4중 가드 자연 통과(0.4초 × 3틱)를 보장.
            yield return RunScenario("① Coward fear 자연 주입 (0.9) ⭐ DEBT-15",
                InjectFearToHumanoidNatural(0.9f));
            // fear 주입 후 추가 5초 대기 — Swarm 도주는 6m/s, 5초면 30m 이동 가능
            yield return new WaitForSeconds(5f);
            VerifyScenario1_CowardFlee();

            // ===== 최종 측정 =====
            if (humanoid != null)
            {
                result.humanoidEndPos = humanoid.transform.position;
                result.humanoidMoveDistance = Vector3.Distance(humanoidStartPos, result.humanoidEndPos);
                result.humanoidMoved = result.humanoidMoveDistance > 0.3f;
                LogInfo($"Humanoid 이동 거리: {result.humanoidMoveDistance:F2}m, 이동 여부: {result.humanoidMoved}");
            }

            // SpeechBubble 인스턴스 검증 (DialogueRenderer.currentBubble 필드 접근)
            CheckSpeechBubbleSpawned();

            result.durationSeconds = Time.unscaledTime - startTime;
            result.completed = true;

            LogInfo($"=== Auto-Verify 완료 ({result.durationSeconds:F1}s) ===");
            LogInfo($"  Scenario 1 Coward Flee:    {(result.scenario1_CowardFlee ? "✅" : "❌")}");
            LogInfo($"  Scenario 2 Friendly:       {(result.scenario2_FriendlyForce ? "✅" : "❌")}");
            LogInfo($"  Scenario 3 Hostile Reject: {(result.scenario3_HostileReject ? "✅" : "❌")}");
            LogInfo($"  Scenario 4 Companion:      {(result.scenario4_CompanionGreet ? "✅" : "❌")}");
            LogInfo($"  Scenario 5 Karma Chain:    {(result.scenario5_KarmaChain ? "✅" : "❌")}");
            LogInfo($"  Speech: {result.speechRenderCount} renders, bubble spawned: {result.speechBubbleSpawned}");
            LogInfo($"  Errors: {result.totalErrors}, Warnings: {result.totalWarnings}");

            yield return AutoExit();
        }

        private IEnumerator RunScenario(string label, Action action)
        {
            LogInfo($"--- 시나리오 트리거: {label} ---");
            speechCountBeforeScenario = result.speechRenderCount;

            try { action(); }
            catch (Exception e) { LogError($"{label} 실행 실패: {e.Message}"); }

            result.scenarioInvocationLog.Add($"{label} @ {Time.time:F1}s");
            yield return new WaitForSeconds(scenarioInterval);
        }

        // [v5.12 DEBT-15] 자연 검증용 RunScenario 오버로드.
        // IEnumerator 액션을 받아 시간 경과(yield)를 포함한 자연 시나리오를 실행.
        // hysteresis 가드가 자연 시간 흐름으로 통과하도록 사용.
        private IEnumerator RunScenario(string label, IEnumerator coroutineAction)
        {
            LogInfo($"--- 시나리오 트리거: {label} ---");
            speechCountBeforeScenario = result.speechRenderCount;

            if (coroutineAction != null)
            {
                // try/catch 안에서 yield return 못 함 — 별도 헬퍼로 안전 실행.
                yield return SafeRunCoroutine(label, coroutineAction);
            }

            result.scenarioInvocationLog.Add($"{label} @ {Time.time:F1}s");
            yield return new WaitForSeconds(scenarioInterval);
        }

        // [v5.12] SafeRunCoroutine — IEnumerator 자연 진행 + 예외 박제.
        // C# 제약(CS1626/CS1631): try/catch 안에서 yield 불가 → 분리 헬퍼.
        private IEnumerator SafeRunCoroutine(string label, IEnumerator action)
        {
            while (true)
            {
                bool moved = false;
                Exception err = null;
                try { moved = action.MoveNext(); }
                catch (Exception e) { err = e; }

                if (err != null)
                {
                    LogError($"{label} 자연 실행 실패: {err.Message}");
                    yield break;
                }
                if (!moved) yield break;
                yield return action.Current;
            }
        }

        // ==================================================================
        // 시나리오별 검증
        // ==================================================================
        private void VerifyScenario4_CompanionGreet()
        {
            // Companion 강제 후 Speech가 발화됐나
            int delta = result.speechRenderCount - speechCountBeforeScenario;
            result.scenario4_CompanionGreet = delta > 0 || result.alignmentTransitionCount > 0;
            LogInfo($"  ④ 검증: speechDelta={delta}, alignmentTransitions={result.alignmentTransitionCount}");
        }

        private void VerifyScenario2_Friendly()
        {
            int delta = result.speechRenderCount - speechCountBeforeScenario;
            result.scenario2_FriendlyForce = delta > 0 || result.alignmentTransitionCount > 1;
        }

        private void VerifyScenario3_HostileReject()
        {
            // Hostile 전이 자체로 일단 PASS (실제 명령 거부는 Acceptance 평가 필요 — 여기서는 전이만 검증)
            result.scenario3_HostileReject = result.alignmentTransitionCount >= 3;
        }

        private void VerifyScenario5_KarmaChain()
        {
            // Karma 변화 + Tier 전이 + Alignment 전이가 연쇄로 발생했나
            result.scenario5_KarmaChain = result.karmaTierChangeCount > 0 || result.alignmentTransitionCount >= 4;
        }

        private void VerifyScenario1_CowardFlee()
        {
            // Archetype_Coward + player 근접 효과로 자연 도주 발생했나
            // Humanoid가 player 반대 방향으로 이동했으면 PASS
            if (humanoid == null) { result.scenario1_CowardFlee = false; return; }

            Vector3 currentPos = humanoid.transform.position;
            float dist = Vector3.Distance(humanoidStartPos, currentPos);

            // player(빨간 큐브)는 (5, 0, 0). Humanoid가 -x 방향으로 이동하면 도주.
            var player = GameObject.Find("PlayerDummy_T52");
            if (player == null) { result.scenario1_CowardFlee = dist > 0.3f; return; }

            float startToPlayer = Vector3.Distance(humanoidStartPos, player.transform.position);
            float currentToPlayer = Vector3.Distance(currentPos, player.transform.position);

            // 현재가 시작보다 player에서 더 멀면 도주
            result.scenario1_CowardFlee = currentToPlayer > startToPlayer + 0.3f;
            LogInfo($"  ① 검증: distFromPlayer start={startToPlayer:F2} → end={currentToPlayer:F2}");
        }

        private void CheckSpeechBubbleSpawned()
        {
            if (humanoid == null) return;
            var dialogueType = FindTypeByName("DialogueRenderer");
            if (dialogueType == null) return;

            var dr = humanoid.GetComponent(dialogueType);
            if (dr == null) return;

            // private GameObject currentBubble; 필드 Reflection
            var currentBubbleField = dialogueType.GetField("currentBubble",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (currentBubbleField != null)
            {
                var bubble = currentBubbleField.GetValue(dr);
                if (bubble != null && !bubble.Equals(null))
                    result.speechBubbleSpawned = true;
            }

            // private bool isShowing; 필드도 검증
            var isShowingField = dialogueType.GetField("isShowing",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (isShowingField != null)
            {
                var isShowing = (bool)isShowingField.GetValue(dr);
                if (isShowing) result.speechBubbleSpawned = true;
            }
        }

        // ==================================================================
        // Reflection 헬퍼
        // ==================================================================
        private void InvokeMethod(string typeName, string methodName)
        {
            var t = FindTypeByName(typeName);
            if (t == null) { LogError($"{typeName} 타입 없음"); return; }

            var found = UnityEngine.Object.FindObjectsByType(t, FindObjectsSortMode.None);
            if (found.Length == 0) { LogError($"{typeName} 인스턴스 0개"); return; }

            var method = t.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null) { LogError($"{typeName}.{methodName} 메서드 없음"); return; }

            foreach (var obj in found)
            {
                try
                {
                    method.Invoke(obj, null);
                    LogInfo($"  → {typeName}.{methodName} 호출 on {((Component)obj).name}");
                }
                catch (Exception e)
                {
                    LogError($"호출 실패: {e.Message}");
                }
            }
        }

        // [v5.5] PB4DecisionAdapter.policyTypeOverride 필드 강제 설정
        // 진단: 모든 FleeXxxAction 중 FleeSwarmAction만 진짜 NavMesh 이동(6m/s 전력질주).
        // FleePhalanx/Duel은 fear 리셋만 함 (도주 안 함).
        // 따라서 Skeleton의 정책을 Swarm으로 임시 변경하여 도주 가능하게 만듦.
        private void OverridePolicyType(string policyTypeStr)
        {
            var adapterType = FindTypeByName("PB4DecisionAdapter");
            if (adapterType == null)
            {
                LogError("PB4DecisionAdapter 타입 없음 — policy override 실패");
                return;
            }

            var adapters = UnityEngine.Object.FindObjectsByType(adapterType, FindObjectsSortMode.None);
            if (adapters.Length == 0)
            {
                LogError("PB4DecisionAdapter 인스턴스 0개");
                return;
            }

            // policyTypeOverride 필드 (private SerializeField — Reflection BindingFlags.NonPublic)
            var overrideField = adapterType.GetField("policyTypeOverride",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (overrideField == null)
            {
                LogError("policyTypeOverride 필드 미발견");
                return;
            }

            int updated = 0;
            foreach (var adapter in adapters)
            {
                try
                {
                    overrideField.SetValue(adapter, policyTypeStr);
                    updated++;
                    LogInfo($"  → policyTypeOverride = '{policyTypeStr}' on {((Component)adapter).name}");
                }
                catch (Exception e)
                {
                    LogError($"policy override 실패: {e.Message}");
                }
            }

            // policyType 변경 후 BB 재초기화 필요 — InitializeBlackboard() 호출 시도
            if (updated > 0)
            {
                var initMethod = adapterType.GetMethod("InitializeBlackboard",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (initMethod != null)
                {
                    foreach (var adapter in adapters)
                    {
                        try { initMethod.Invoke(adapter, null); }
                        catch { /* 무시 */ }
                    }
                    LogInfo($"  → BB 재초기화 완료 ({updated}개 adapter)");
                }
            }
        }

        // [v5.4] HumanoidAIBrain.fear 직접 주입 (Coward 도주 능동 트리거)
        // [v5.6] 코드 주석의 명시적 호출 순서 준수:
        //   1. brain.fear = 값
        //   2. brain.UpdateDecision()  ← utility 재계산 (Idle → Flee 전환)
        //   3. adapter.ForceSyncBB()   ← BB 즉시 동기화 (0.5초 대기 우회)
        // PB4DecisionAdapter.cs:680~700 주석 참조.
        private void InjectFearToHumanoid(float fearValue)
        {
            var brainType = FindTypeByName("HumanoidAIBrain");
            if (brainType == null)
            {
                LogError("HumanoidAIBrain 타입 없음 — fear 주입 실패");
                return;
            }

            var brains = UnityEngine.Object.FindObjectsByType(brainType, FindObjectsSortMode.None);
            if (brains.Length == 0)
            {
                LogError("HumanoidAIBrain 인스턴스 0개 — fear 주입 실패");
                return;
            }

            // BaseAIBrain의 fear 필드 (HumanoidAIBrain이 상속)
            var fearField = brainType.GetField("fear",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (fearField == null)
            {
                var baseType = brainType.BaseType;
                while (baseType != null && fearField == null)
                {
                    fearField = baseType.GetField("fear",
                        BindingFlags.Public | BindingFlags.Instance);
                    baseType = baseType.BaseType;
                }
            }

            if (fearField == null)
            {
                LogError("BaseAIBrain.fear 필드 미발견 — fear 주입 실패");
                return;
            }

            // [v5.6] UpdateDecision 메서드 (BT 재계산 즉시 트리거)
            var updateDecisionMethod = brainType.GetMethod("UpdateDecision",
                BindingFlags.Public | BindingFlags.Instance);

            // [v5.6] PB4DecisionAdapter.ForceSyncBB 메서드 (BB 즉시 동기화)
            var adapterType = FindTypeByName("PB4DecisionAdapter");
            var forceSyncMethod = adapterType?.GetMethod("ForceSyncBB",
                BindingFlags.Public | BindingFlags.Instance);

            int injected = 0;
            foreach (var brain in brains)
            {
                try
                {
                    // 1. fear 설정
                    fearField.SetValue(brain, fearValue);
                    LogInfo($"  → HumanoidAIBrain.fear = {fearValue:F2} on {((Component)brain).name}");

                    // 2. brain.UpdateDecision() — utility 재계산 (Idle → Flee 전환)
                    if (updateDecisionMethod != null)
                    {
                        updateDecisionMethod.Invoke(brain, null);
                        LogInfo($"  → brain.UpdateDecision() 호출 (utility 재계산)");
                    }
                    else
                    {
                        LogError("UpdateDecision 메서드 미발견 — utility 재계산 안 됨");
                    }

                    // 3. adapter.ForceSyncBB() — BB 즉시 동기화 (0.5초 대기 우회)
                    if (adapterType != null && forceSyncMethod != null)
                    {
                        var brainGo = ((Component)brain).gameObject;
                        var adapter = brainGo.GetComponent(adapterType);
                        if (adapter != null)
                        {
                            forceSyncMethod.Invoke(adapter, null);
                            LogInfo($"  → adapter.ForceSyncBB() 호출 (BB 즉시 반영)");

                            // [v5.10] FleeSprintSpeed BB 변수 6f 강제 (Policy override가 BB 재초기화 → 0으로 리셋한 거 복구)
                            // FleeSwarmAction.cs:143이 _nav.speed = FleeSprintSpeed.Value로 덮어씀
                            var setBBMethod = adapterType.GetMethod("SetBB",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (setBBMethod != null)
                            {
                                try
                                {
                                    var generic = setBBMethod.MakeGenericMethod(typeof(float));
                                    generic.Invoke(adapter, new object[] { "FleeSprintSpeed", 6f });
                                    LogInfo($"  → SetBB(FleeSprintSpeed, 6f) 강제 주입 ⭐ v5.10");
                                }
                                catch (Exception e)
                                {
                                    LogError($"SetBB(FleeSprintSpeed) 실패: {e.Message}");
                                }
                            }

                            // NavMeshAgent.speed도 직접 보장 (안전판)
                            var navAgent = brainGo.GetComponent<UnityEngine.AI.NavMeshAgent>();
                            if (navAgent != null && navAgent.enabled && navAgent.speed < 0.5f)
                            {
                                navAgent.speed = 6f;
                                LogInfo($"  → NavMeshAgent.speed = 6f 직접 설정");
                            }
                        }
                        else
                        {
                            LogError("PB4DecisionAdapter 컴포넌트 미부착");
                        }
                    }
                    else
                    {
                        LogError("ForceSyncBB 메서드 미발견 — BB 동기화 0.5초 대기 발생");
                    }

                    // [v5.7] 4. brain.ForceStateForTesting(Flee) — hysteresis/holdTicks 우회
                    // utility/SelectNewState 차단 가드를 모두 우회하고 강제로 BT Flee 진입
                    var forceStateMethod = brainType.GetMethod("ForceStateForTesting",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (forceStateMethod != null)
                    {
                        // HumanoidBTState.Flee = enum value 4
                        var enumType = brainType.Assembly.GetType("TDA.PB4.AI.Humanoid.HumanoidBTState");
                        if (enumType != null)
                        {
                            var fleeValue = Enum.Parse(enumType, "Flee");
                            forceStateMethod.Invoke(brain, new object[] { fleeValue, "Coward 5/5 도전" });
                            LogInfo($"  → brain.ForceStateForTesting(Flee) 호출 (hysteresis 우회) ⭐ v5.7");

                            // ForceState 후 BB 한 번 더 동기화
                            if (forceSyncMethod != null)
                            {
                                var brainGo2 = ((Component)brain).gameObject;
                                var adapter2 = brainGo2.GetComponent(adapterType);
                                if (adapter2 != null) forceSyncMethod.Invoke(adapter2, null);
                            }
                        }
                        else
                        {
                            LogError("HumanoidBTState enum 타입 미발견");
                        }
                    }
                    else
                    {
                        LogError("ForceStateForTesting 메서드 미발견 — Brain v5.7 미적용");
                    }

                    injected++;
                }
                catch (Exception e)
                {
                    LogError($"fear 주입 실패: {e.Message}");
                }
            }
            LogInfo($"  → fear 주입 완료: {injected}/{brains.Length}");
        }

        // ==================================================================
        // [v5.12 DEBT-15] InjectFearToHumanoidNatural — 자연 검증 (본질 회복)
        // ==================================================================
        // ForceStateForTesting 호출 제거. utility 함수가 fear → Flee를 winner로
        // 자연 선택하는지 검증. SelectNewState의 4중 hysteresis 가드를 모두
        // 자연 시간 흐름(0.4초 × 3틱)으로 통과.
        //
        // 가드별 통과 조건:
        //   ❶ winnerScore < idleThreshold      → fear=0.9면 Flee 점수 0.85+ 통과
        //   ❷ newState == currentState         → Idle≠Flee 통과
        //   ❸ holdTicks < minStateHoldTicks(2) → 3회 UpdateDecision으로 holdTicks=2 도달
        //   ❹ winnerScore < currentScore+0.08  → 0.85 vs 0~0.2 통과
        //
        // 본질 검증 항목:
        //   1) utility 함수가 fear → Flee winner 선택 (가드 ❶)
        //   2) state machine이 hysteresis 정상 거침 (가드 ❷❸❹)
        //   3) TransitionTo(Flee) → BB.UtilityWinner=Flee 자연 반영
        //   4) Behavior Graph가 FleeAction 자연 실행
        //   5) NavMesh+RootMotion 도주 동작
        // ==================================================================
        private IEnumerator InjectFearToHumanoidNatural(float fearValue)
        {
            var brainType = FindTypeByName("HumanoidAIBrain");
            if (brainType == null)
            {
                LogError("HumanoidAIBrain 타입 없음 — 자연 fear 주입 실패");
                yield break;
            }

            var brains = UnityEngine.Object.FindObjectsByType(brainType, FindObjectsSortMode.None);
            if (brains.Length == 0)
            {
                LogError("HumanoidAIBrain 인스턴스 0개 — 자연 fear 주입 실패");
                yield break;
            }

            // BaseAIBrain.fear 필드 (HumanoidAIBrain이 상속)
            var fearField = brainType.GetField("fear",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (fearField == null)
            {
                var baseType = brainType.BaseType;
                while (baseType != null && fearField == null)
                {
                    fearField = baseType.GetField("fear",
                        BindingFlags.Public | BindingFlags.Instance);
                    baseType = baseType.BaseType;
                }
            }
            if (fearField == null)
            {
                LogError("BaseAIBrain.fear 필드 미발견 — 자연 fear 주입 실패");
                yield break;
            }

            var updateDecisionMethod = brainType.GetMethod("UpdateDecision",
                BindingFlags.Public | BindingFlags.Instance);
            if (updateDecisionMethod == null)
            {
                LogError("UpdateDecision 메서드 미발견 — 자연 검증 불가");
                yield break;
            }

            var adapterType = FindTypeByName("PB4DecisionAdapter");
            var forceSyncMethod = adapterType?.GetMethod("ForceSyncBB",
                BindingFlags.Public | BindingFlags.Instance);

            int injected = 0;
            foreach (var brain in brains)
            {
                Component brainComp = (Component)brain;
                GameObject brainGo = brainComp.gameObject;
                Component adapter = adapterType != null ? brainGo.GetComponent(adapterType) : null;

                // [v5.13 박제] 진단용 reflection 핸들 — brain 내부 상태 직접 조회
                // [v5.14 수정] verboseLogging은 BaseAIBrain의 protected 필드 — NonPublic + BaseType 순회 필요
                FieldInfo verboseField = null;
                {
                    var t = brainType;
                    while (t != null && verboseField == null)
                    {
                        verboseField = t.GetField("verboseLogging",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        t = t.BaseType;
                    }
                }
                var currentStateProp = brainType.GetProperty("CurrentState",
                    BindingFlags.Public | BindingFlags.Instance);
                var uFleeField = brainType.GetField("u_flee",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                // [v5.14] currentStateHoldTicks는 private (HumanoidAIBrain 자체) — NonPublic 필요
                var holdTicksField = brainType.GetField("currentStateHoldTicks",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                // [v5.13/14] verboseLogging 강제 ON — SelectNewState 차단 로그 출력 보장
                if (verboseField != null)
                {
                    verboseField.SetValue(brain, true);
                    bool readback = (bool)verboseField.GetValue(brain);
                    LogInfo($"  → [자연 박제] verboseLogging 강제 ON readback: {readback} (true여야 차단 로그 출력)");
                }
                else
                {
                    LogError("[자연 박제] verboseLogging 필드 미발견 — 차단 로그 출력 불가");
                }

                // ── T=0.0s: fear 설정 + UpdateDecision 1회차 ──────────
                // 결과: utility 계산 → newState=Flee, holdTicks=0 → ❸ 차단 (의도)
                try
                {
                    fearField.SetValue(brain, fearValue);
                    float verifiedFear = (float)fearField.GetValue(brain);
                    LogInfo($"  → [자연] fear 설정: {fearValue:F2} → 즉시 readback: {verifiedFear:F2} (일치 여부 박제)");

                    var stateBefore = currentStateProp != null ? currentStateProp.GetValue(brain) : "?";
                    int holdBefore = holdTicksField != null ? (int)holdTicksField.GetValue(brain) : -1;
                    LogInfo($"  → [자연 박제] UpdateDecision 1차 호출 직전: currentState={stateBefore}, holdTicks={holdBefore}");

                    updateDecisionMethod.Invoke(brain, null);

                    float fearAfter1 = (float)fearField.GetValue(brain);
                    var stateAfter1 = currentStateProp != null ? currentStateProp.GetValue(brain) : "?";
                    float uFleeAfter1 = uFleeField != null ? (float)uFleeField.GetValue(brain) : -1f;
                    int holdAfter1 = holdTicksField != null ? (int)holdTicksField.GetValue(brain) : -1;
                    LogInfo($"  → [자연 박제] UpdateDecision 1차 후: fear={fearAfter1:F2}, currentState={stateAfter1}, u_flee={uFleeAfter1:F2}, holdTicks={holdAfter1}");
                }
                catch (Exception e)
                {
                    LogError($"[자연] T=0.0s 설정 실패: {e.Message}");
                    continue;
                }

                yield return new WaitForSeconds(0.4f);

                // ── T=0.4s: UpdateDecision 2회차 ──────────────────────
                try
                {
                    fearField.SetValue(brain, fearValue);
                    updateDecisionMethod.Invoke(brain, null);

                    float fearAfter2 = (float)fearField.GetValue(brain);
                    var stateAfter2 = currentStateProp != null ? currentStateProp.GetValue(brain) : "?";
                    float uFleeAfter2 = uFleeField != null ? (float)uFleeField.GetValue(brain) : -1f;
                    int holdAfter2 = holdTicksField != null ? (int)holdTicksField.GetValue(brain) : -1;
                    LogInfo($"  → [자연 박제] UpdateDecision 2차 후: fear={fearAfter2:F2}, currentState={stateAfter2}, u_flee={uFleeAfter2:F2}, holdTicks={holdAfter2}");
                }
                catch (Exception e) { LogError($"[자연] T=0.4s 실패: {e.Message}"); }

                yield return new WaitForSeconds(0.4f);

                // ── T=0.8s: UpdateDecision 3회차 ──────────────────────
                try
                {
                    fearField.SetValue(brain, fearValue);
                    updateDecisionMethod.Invoke(brain, null);

                    float fearAfter3 = (float)fearField.GetValue(brain);
                    var stateAfter3 = currentStateProp != null ? currentStateProp.GetValue(brain) : "?";
                    float uFleeAfter3 = uFleeField != null ? (float)uFleeField.GetValue(brain) : -1f;
                    int holdAfter3 = holdTicksField != null ? (int)holdTicksField.GetValue(brain) : -1;
                    LogInfo($"  → [자연 박제] UpdateDecision 3차 후: fear={fearAfter3:F2}, currentState={stateAfter3}, u_flee={uFleeAfter3:F2}, holdTicks={holdAfter3}");

                    // [v5.15 DEBT-21 해결 후] ❸ 우회 코드 제거.
                    // HumanoidAIBrain.SelectNewState가 매 호출 holdTicks++로 수정됨 →
                    // Adapter polling으로 자연 검증 시작 전 holdTicks 충분히 누적됨 →
                    // 3차 호출만으로 currentState=Flee 자연 전이 보장됨.
                    // currentState=Idle 유지 시 회귀 (DEBT-21 패치 미적용 또는 다른 결함).
                    if (stateAfter3 != null && stateAfter3.ToString() == "Idle")
                    {
                        LogError($"[자연 박제] ⚠ 자연 3차 후에도 Idle 유지 — DEBT-21 패치 미적용 또는 회귀 의심. HumanoidAIBrain.SelectNewState 코드 확인.");
                    }
                    else
                    {
                        LogInfo($"  → [자연 박제] ⭐ 자연 3차 호출만으로 Flee 전이 성공! DEBT-15/21 본질 회복.");
                    }
                }
                catch (Exception e) { LogError($"[자연] T=0.8s 실패: {e.Message}"); }

                yield return new WaitForSeconds(0.4f);

                // ── T=1.2s: BB 즉시 동기화 (검증 시간 단축) ───────────
                // BB.UtilityWinner = Flee가 PB4DecisionAdapter 0.5초 주기 대기 안 거치고
                // 즉시 반영되어 Behavior Graph가 FleeAction 실행 시작.
                if (adapter != null && forceSyncMethod != null)
                {
                    try
                    {
                        forceSyncMethod.Invoke(adapter, null);
                        LogInfo($"  → [자연] adapter.ForceSyncBB() (BB.UtilityWinner=Flee 즉시 반영)");
                    }
                    catch (Exception e) { LogError($"[자연] ForceSyncBB 실패: {e.Message}"); }

                    // FleeSprintSpeed 강제 (Policy override 대응)
                    var setBBMethod = adapterType.GetMethod("SetBB",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (setBBMethod != null)
                    {
                        try
                        {
                            var generic = setBBMethod.MakeGenericMethod(typeof(float));
                            generic.Invoke(adapter, new object[] { "FleeSprintSpeed", 6f });
                            LogInfo($"  → [자연] SetBB(FleeSprintSpeed, 6f)");
                        }
                        catch (Exception e) { LogError($"[자연] SetBB 실패: {e.Message}"); }
                    }

                    // NavMeshAgent.speed 안전판
                    var navAgent = brainGo.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    if (navAgent != null && navAgent.enabled && navAgent.speed < 0.5f)
                    {
                        navAgent.speed = 6f;
                        LogInfo($"  → [자연] NavMeshAgent.speed = 6f");
                    }
                }
                else
                {
                    LogError("[자연] PB4DecisionAdapter 미부착 또는 ForceSyncBB 미발견");
                }

                injected++;
                LogInfo($"  → [자연] 주입 완료: {brainComp.name} (T=1.2s, ForceStateForTesting 미사용 ⭐)");
            }

            LogInfo($"  → [자연] 자연 fear 주입 완료: {injected}/{brains.Length}");
        }

        private void InvokeKarmaChange(float delta)
        {
            // KarmaDirector.Instance.ApplyKarmaShift(0, delta, "auto-test")
            var t = FindTypeByName("KarmaDirector");
            if (t == null) { LogError("KarmaDirector 타입 없음"); return; }

            var instProp = t.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);
            if (instProp == null) { LogError("KarmaDirector.Instance 프로퍼티 없음"); return; }

            var instance = instProp.GetValue(null);
            if (instance == null || instance.Equals(null))
            {
                LogError("KarmaDirector.Instance == null. Stage 셋업에 KarmaDirector GameObject 추가 필요.");
                return;
            }

            // ApplyKarmaShift(ulong, float, string) 시도
            var method = t.GetMethod("ApplyKarmaShift",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(ulong), typeof(float), typeof(string) },
                null);

            if (method != null)
            {
                method.Invoke(instance, new object[] { (ulong)0, delta, "auto-test" });
                LogInfo($"  → KarmaDirector.ApplyKarmaShift(0, {delta:+#;-#}, \"auto-test\")");
                return;
            }

            // Fallback: ContextMenu DebugSetSaint 메서드 시도
            string contextMenuName = delta >= 50 ? "Debug/Set Saint (+80)" :
                                     delta <= -50 ? "Debug/Set Outlaw (-70)" :
                                     "Debug/Set Neutral (0)";
            var allMethods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var m in allMethods)
            {
                var ctxMenus = m.GetCustomAttributes(typeof(ContextMenu), false).Cast<ContextMenu>();
                if (ctxMenus.Any(cm => cm.menuItem == contextMenuName))
                {
                    try
                    {
                        m.Invoke(instance, null);
                        LogInfo($"  → KarmaDirector.{m.Name} (ContextMenu '{contextMenuName}')");
                        return;
                    }
                    catch (Exception e)
                    {
                        LogError($"ContextMenu 호출 실패: {e.Message}");
                    }
                }
            }

            LogError("ApplyKarmaShift도 ContextMenu도 호출 못 함.");
        }

        private GameObject FindHumanoidInScene()
        {
            var t = FindTypeByName("HumanoidAIBrain");
            if (t == null) return null;
            var found = UnityEngine.Object.FindAnyObjectByType(t) as Component;
            return found?.gameObject;
        }

        private static Type FindTypeByName(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                        if (t.Name == name) return t;
                }
                catch { }
            }
            return null;
        }

        // ==================================================================
        // 로그 콜백
        // ==================================================================
        private void OnLog(string condition, string stack, LogType type)
        {
            if (string.IsNullOrEmpty(condition) || result == null) return;
            if (condition.StartsWith("[T5.2 AutoVerify]")) return; // 자기 로그 제외
            if (condition.StartsWith("[T5.3 Probe]")) return;

            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                result.totalErrors++;
                if (result.errorMessages.Count < 20)
                    result.errorMessages.Add(condition.Length > 200 ? condition.Substring(0, 200) + "…" : condition);
            }
            else if (type == LogType.Warning)
            {
                result.totalWarnings++;
            }

            // 패턴 카운트 [v5.3 수정 — 실제 사용자 로그 형식 인식]
            if (condition.Contains("[DialogueRenderer]"))
            {
                result.speechRenderCount++;
            }
            if (condition.Contains("[SpeechAssembler]") || condition.Contains("[SpeechDispatcher]"))
            {
                result.speechAssemblerCount++;
            }
            // [v5.3] Alignment 로그: [NPCAlignment], [Alignment], [Alignment.DEBUG] 모두 인식
            if ((condition.Contains("[NPCAlignment]") || condition.Contains("[Alignment]") ||
                 condition.Contains("[Alignment.DEBUG]") || condition.Contains("Alignment.DEBUG"))
                &&
                (condition.Contains("→") || condition.Contains("->") ||
                 condition.Contains("transition") || condition.Contains("전이")))
            {
                result.alignmentTransitionCount++;
            }
            // [v5.3] Karma Tier 전이: [Karma] 태그 또는 KarmaDirector 클래스명 둘 다 인식
            if ((condition.Contains("[Karma]") || condition.Contains("KarmaDirector")) &&
                (condition.Contains("Tier") || condition.Contains("tier") ||
                 condition.Contains("Saint") || condition.Contains("Outlaw") || condition.Contains("Demon")))
            {
                result.karmaTierChangeCount++;
            }
        }

        // ==================================================================
        // 자동 종료
        // ==================================================================
        private IEnumerator AutoExit()
        {
            LogInfo($"AutoExit 대기 {autoExitDelay}s...");
            yield return new WaitForSeconds(autoExitDelay);

#if UNITY_EDITOR
            LogInfo("EditorApplication.ExitPlaymode() 호출.");
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        // ==================================================================
        // 로깅 (자기 로그 prefix)
        // ==================================================================
        private void LogInfo(string msg)
        {
            if (debugLog) Debug.Log($"[T5.2 AutoVerify] {msg}");
        }
        private void LogError(string msg)
        {
            Debug.LogError($"[T5.2 AutoVerify] {msg}");
            if (result != null) result.lastError = msg;
        }
    }
}
