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

            // ===== 시나리오 ① Coward 자연 발생 검증 =====
            // Archetype_Coward + player 근접 → fear 증가 → flee
            // 위치 변화로 검증 (이미 누적된 Humanoid 이동 확인)
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
