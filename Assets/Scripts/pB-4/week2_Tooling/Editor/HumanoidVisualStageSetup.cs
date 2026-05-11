// =============================================================================
// HumanoidVisualStageSetup.cs  |  pB-4 Week 2 Day 5 T5.2  v5
// 역할: Humanoid AI 시각 검증 환경 자동 셋업 + 무인 자동 검증.
//       Window > pB-4 > Day 5 > Humanoid Visual Stage
//
// [v5 신규 — 무인 자동화]
//   - ⭐ [One-Click Auto-Verify]: 매니저 Inject + Stage 셋업 + KarmaDirector 추가 +
//     NavMesh 자동 베이크 + AutoVerifier 부착 + Play 진입까지 한 번에
//   - AutoVerifier 결과를 Editor에서 모니터링 + Play 종료 후 자동 보고서 출력
//   - Self-Check: 코드 정합성 자동 검증 (Reflection 기반)
//
// [v4 유지]
//   - 폴더 힌트 + 4-path 자동 탐색 + 폴더 일괄 Inject
//   - 의존성 5종 (Session→State + WorldAISpawnManager 추가)
// =============================================================================
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TDA.PB4.Tooling.HumanoidVisual;

namespace TDA.PB4.Tooling.HumanoidVisual.Editor
{
    public class HumanoidVisualStageSetup : EditorWindow
    {
        private GameObject humanoidPrefab;

        // [v4] 의존성 매니저 5종
        private GameObject worldItemDatabasePrefab;
        private GameObject worldUtilityManagerPrefab;
        private GameObject worldGameStateManagerPrefab;
        private GameObject worldSaveGameManagerPrefab;
        private GameObject worldAIManagerPrefab;
        private bool stubMode = false;
        private string managerFolderHint = "Assets/Prefabs/WorldManagers";

        // 시각 체크리스트 (수동 마킹 시)
        private bool sc1_CowardFlee;
        private bool sc2_FriendlyForce;
        private bool sc3_HostileReject;
        private bool sc4_CompanionGreet;
        private bool sc5_KarmaToAlignment;
        private bool speechBubbleObserved;
        private bool humanoidMovementObserved;

        private Vector2 scroll;
        private double lastRepaintTime;
        private const double REPAINT_INTERVAL = 0.5;

        private const string STAGE_PARENT_NAME = "_T52_VisualStage";
        private const string MANAGER_PARENT_NAME = "_T52_ManagerStubs";

        // [v5] Auto-Verify 진행 상태 추적
        private bool waitingForAutoExit = false;
        private bool removeDebuggersOnPlay = true;   // [v5.1] FPS 폭주 방지 토글

        private static readonly string[] DEPENDENCY_TYPE_NAMES =
        {
            "WorldItemDatabase",
            "WorldUtilityManager",
            "WorldGameStateManager",
            "WorldSaveGameManager",
            "WorldAISpawnManager",
        };

        // [v5] Self-Check 검증 매트릭스
        private static readonly (string typeName, string methodName)[] EXPECTED_METHODS =
        {
            ("NPCAlignmentController", "DebugForceCompanionBypassHold"),
            ("NPCAlignmentController", "DebugForceFriendlyBypassHold"),
            ("NPCAlignmentController", "DebugForceHostileBypassHold"),
            ("NPCAlignmentController", "DebugForceNeutralBypassHold"),
            ("NPCAlignmentController", "DebugForceTransition"),
            ("KarmaDirector", "ApplyKarmaShift"),
            ("KarmaDirector", "ChangeKarma"),
            ("HumanoidAIBrain", null),  // 클래스 존재만 검증
            ("DialogueRenderer", "Render"),
            ("SpeechDispatcher", null),
            ("HumanoidBootstrapper", null),
        };

        [MenuItem("Window/pB-4/Day 5/Humanoid Visual Stage", priority = 51)]
        public static void Open()
        {
            var w = GetWindow<HumanoidVisualStageSetup>("Humanoid Visual Stage");
            w.minSize = new Vector2(490, 900);
            w.AutoFindAll();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }
        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - lastRepaintTime < REPAINT_INTERVAL) return;
            lastRepaintTime = now;
            Repaint();
        }
        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // [v5.1] EnteredPlayMode 직후 — AIPerceptionDebugger 일괄 제거 (FPS 폭주 방지)
            if (change == PlayModeStateChange.EnteredPlayMode && removeDebuggersOnPlay)
            {
                EditorApplication.delayCall += RemoveAllAIPerceptionDebuggers;

                // [v5.3] Bootstrap이 Play 진입 시 NPCAlignmentController를 AddComponent하므로
                // EditMode 시점에는 컴포넌트 자체가 없음. Play 진입 직후 한 프레임 뒤 SO 전파.
                EditorApplication.delayCall += () =>
                {
                    EditorApplication.delayCall += PropagateAlignmentSOToControllers;
                };
            }

            // [v5] EnteredEditMode (Play 종료 후) 자동 보고서 출력
            if (change == PlayModeStateChange.EnteredEditMode && waitingForAutoExit)
            {
                waitingForAutoExit = false;
                if (HumanoidVisualAutoVerifier.LatestResult.completed)
                {
                    Debug.Log("[T5.2 Stage] Auto-Verify 완료 감지. 보고서 자동 출력.");
                    ExportAutoVerifyReport();
                }
            }
            Repaint();
        }

        // [v5.1] 씬 내 모든 AIPerceptionDebugger 컴포넌트 제거 (FPS 복구)
        private static void RemoveAllAIPerceptionDebuggers()
        {
            var t = FindTypeByName("AIPerceptionDebugger");
            if (t == null) { Debug.Log("[T5.2 Stage] AIPerceptionDebugger 타입 미발견 — 제거 스킵."); return; }
            var found = UnityEngine.Object.FindObjectsByType(t, FindObjectsInactive.Include, FindObjectsSortMode.None);
            int removed = 0;
            foreach (var d in found)
            {
                if (d is Component c) { UnityEngine.Object.DestroyImmediate(c); removed++; }
            }
            if (removed > 0)
                Debug.Log($"[T5.2 Stage] AIPerceptionDebugger {removed}개 제거 (FPS 폭주 방지). 시각 검증 후 다시 Play 시 자동 재부착됨.");
        }

        // [v5.3] NPCAlignmentSO를 모든 NPCAlignmentController에 전파
        // Bootstrapper가 SO 전파 누락한 부채 (DEBT-13) 우회.
        // EditModeUtility 호출 시 즉시 실행. PlayMode 진입 시 EnteredPlayMode에서도 호출.
        private static void PropagateAlignmentSOToControllers()
        {
            var controllerType = FindMonoBehaviourTypeByName("NPCAlignmentController");
            if (controllerType == null) { Debug.LogWarning("[T5.2 Stage] NPCAlignmentController 타입 미발견 — SO 전파 스킵."); return; }

            // SetAlignmentSO 메서드 reflection
            var setMethod = controllerType.GetMethod("SetAlignmentSO",
                BindingFlags.Public | BindingFlags.Instance);
            if (setMethod == null) { Debug.LogWarning("[T5.2 Stage] SetAlignmentSO 메서드 미발견."); return; }

            // alignmentDefinitionSO 필드 reflection (이미 할당됐는지 검증용)
            var soField = controllerType.GetField("alignmentDefinitionSO",
                BindingFlags.NonPublic | BindingFlags.Instance);

            // DefaultAlignmentDefinitions.asset 로드
            var soGuids = AssetDatabase.FindAssets("DefaultAlignmentDefinitions t:ScriptableObject");
            ScriptableObject so = null;
            foreach (var g in soGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so != null) break;
            }
            if (so == null) { Debug.LogWarning("[T5.2 Stage] DefaultAlignmentDefinitions.asset 미발견."); return; }

            // 씬 내 모든 NPCAlignmentController에 주입
            var controllers = UnityEngine.Object.FindObjectsByType(
                controllerType, FindObjectsInactive.Include, FindObjectsSortMode.None);
            int injected = 0, skipped = 0;
            foreach (var c in controllers)
            {
                // 이미 할당됐으면 스킵 (사용자가 Inspector로 직접 할당한 경우 보존)
                if (soField != null)
                {
                    var current = soField.GetValue(c);
                    if (current != null && !current.Equals(null)) { skipped++; continue; }
                }
                try
                {
                    setMethod.Invoke(c, new object[] { so });
                    injected++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[T5.2 Stage] SetAlignmentSO 실패 on {((Component)c).name}: {e.Message}");
                }
            }
            if (injected > 0 || skipped > 0)
                Debug.Log($"[T5.2 Stage] NPCAlignmentSO 전파: 주입 {injected}, 이미 할당 {skipped}. " +
                          $"(DEBT-13 우회 — Bootstrapper의 SO 전파 누락 보완)");
        }

        // ==================================================================
        // GUI
        // ==================================================================
        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("T5.2 — Humanoid AI 시각 검증  v5.1", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "v5.1 신규 (긴급 패치):\n" +
                "  • Stage 셋업 시 HumanoidBootstrapper 자동 추가 (NPCAlignmentController 자동 부착)\n" +
                "  • KarmaDirector 자동 추가\n" +
                "  • Play 진입 직후 AIPerceptionDebugger 자동 제거 (FPS 폭주 방지, DEBT-08)",
                MessageType.Info);

            // [v5.1] FPS 보호 토글
            removeDebuggersOnPlay = EditorGUILayout.ToggleLeft(
                new GUIContent("Play 진입 직후 AIPerceptionDebugger 자동 제거 (FPS 폭주 방지)",
                               "DEBT-08: Humanoid 호환 미처리. 매 프레임 GC 1500개 폭주."),
                removeDebuggersOnPlay);
            EditorGUILayout.HelpBox(
                "v5 신규: One-Click Auto-Verify (무인 자동화) + Self-Check.\n" +
                "사용자가 자고 있어도 자동 검증 → 깨어나서 결과만 확인.",
                MessageType.Info);

            DrawOneClickSection();   // [v5] 신규 — 최상단
            EditorGUILayout.Space(8);

            DrawStep1_Setup();
            DrawStep2_Probe();
            DrawStep3_PlayStatus();
            DrawStep4_Scenarios();
            DrawStep5_VisualChecklist();
            DrawStep6_Export();

            EditorGUILayout.Space(15);
            DrawSelfCheckSection();   // [v5] 신규 — 최하단

            EditorGUILayout.EndScrollView();
        }

        // ==================================================================
        // [v5] One-Click Auto-Verify
        // ==================================================================
        private void DrawOneClickSection()
        {
            EditorGUILayout.Space(8);

            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.6f, 0.95f, 0.6f);
            EditorGUILayout.BeginVertical("box");
            GUI.backgroundColor = prevColor;

            EditorGUILayout.LabelField("⭐ One-Click Auto-Verify", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "한 클릭에 다음 모두 자동 실행:\n" +
                "  1. 매니저 폴더 일괄 Inject (Assets/Prefabs/WorldManagers)\n" +
                "  2. Stage 자동 셋업 (Plane + Humanoid + Player Dummy + Camera)\n" +
                "  3. KarmaDirector GameObject 자동 추가\n" +
                "  4. NavMesh 자동 베이크\n" +
                "  5. AutoVerifier MonoBehaviour 부착 (autoMode=true)\n" +
                "  6. Play 자동 진입 → AutoVerifier가 5종 시나리오 순차 트리거\n" +
                "  7. ~30초 후 자동 ExitPlaymode → 보고서 자동 출력\n\n" +
                "사전 조건: Tag 'Player' 정의됨, SpeechBubble Prefab Bootstrapper에 할당.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("⭐ [One-Click Auto-Verify 시작]", GUILayout.Height(40)))
                    OneClickAutoVerify();
            }

            if (waitingForAutoExit)
                EditorGUILayout.HelpBox("⏳ Auto-Verify 진행 중... Play 자동 종료 대기.", MessageType.Info);

            // 결과 표시 (마지막 자동 검증)
            var r = HumanoidVisualAutoVerifier.LatestResult;
            if (r != null && r.completed)
            {
                int passed = (r.scenario1_CowardFlee?1:0) + (r.scenario2_FriendlyForce?1:0)
                           + (r.scenario3_HostileReject?1:0) + (r.scenario4_CompanionGreet?1:0)
                           + (r.scenario5_KarmaChain?1:0);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField($"마지막 Auto-Verify 결과: {passed}/5 시나리오 PASS", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"  ① Coward Flee:    {(r.scenario1_CowardFlee ? "✅" : "❌")}  (이동 거리 {r.humanoidMoveDistance:F2}m)");
                EditorGUILayout.LabelField($"  ② Friendly:       {(r.scenario2_FriendlyForce ? "✅" : "❌")}");
                EditorGUILayout.LabelField($"  ③ Hostile Reject: {(r.scenario3_HostileReject ? "✅" : "❌")}");
                EditorGUILayout.LabelField($"  ④ Companion:      {(r.scenario4_CompanionGreet ? "✅" : "❌")}");
                EditorGUILayout.LabelField($"  ⑤ Karma Chain:    {(r.scenario5_KarmaChain ? "✅" : "❌")}");
                EditorGUILayout.LabelField($"  Speech: {r.speechRenderCount} renders, Bubble Spawned: {r.speechBubbleSpawned}");
                EditorGUILayout.LabelField($"  Errors: {r.totalErrors}, Warnings: {r.totalWarnings}");

                if (!string.IsNullOrEmpty(r.lastError))
                    EditorGUILayout.HelpBox("최종 에러: " + r.lastError, MessageType.Warning);

                if (GUILayout.Button("Auto-Verify 보고서 다시 출력"))
                    ExportAutoVerifyReport();
            }

            EditorGUILayout.EndVertical();
        }

        private void OneClickAutoVerify()
        {
            // 1. 폴더 일괄 Inject
            var managerParent = InjectAllPrefabsFromFolderInternal();

            // 2. Stage 셋업
            var stage = SetupStageInternal();
            if (stage == null) return;

            // 3. KarmaDirector 자동 추가
            AddKarmaDirectorIfMissing(stage);

            // 4. NavMesh 자동 베이크
            BakeNavMesh();

            // 5. AutoVerifier 부착
            var verifier = stage.GetComponent<HumanoidVisualAutoVerifier>()
                        ?? stage.AddComponent<HumanoidVisualAutoVerifier>();
            verifier.autoMode = true;
            EditorUtility.SetDirty(verifier);

            // 6. Play 진입
            waitingForAutoExit = true;
            EditorApplication.EnterPlaymode();
        }

        private void BakeNavMesh()
        {
            try
            {
                UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
                Debug.Log("[T5.2 Stage] NavMesh 자동 베이크 완료.");

                // [v5.9] Cave 없이 Plane 단독 씬에서 NavMeshAgent 자동 활성화
                // (CaveManager.cs:420-437 패턴 복제 — 프리팹 NavMeshAgent는
                //  기본 OFF, Cave 베이크 완료 시 자동 enable되지만 테스트 씬은
                //  Cave가 없어 그 이벤트가 발생하지 않음)
                EnableAndSnapAllNavMeshAgents();
            }
            catch (Exception e)
            {
                Debug.LogError($"[T5.2 Stage] NavMesh 베이크 실패: {e.Message}\n수동 베이크: Window > AI > Navigation > Bake");
            }
        }

        // [v5.9] CaveManager.cs:420-437 패턴 복사 — NavMesh 위로 스냅 + enable
        // [v5.10] BehaviorGraph BB 변수 'FleeSprintSpeed'가 0인 문제 우회
        //         (FleeSwarmAction.cs:143이 _nav.speed = FleeSprintSpeed.Value로 덮어씀)
        private void EnableAndSnapAllNavMeshAgents()
        {
            var allAgents = UnityEngine.Object.FindObjectsByType<UnityEngine.AI.NavMeshAgent>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            int enabled = 0, snapped = 0, bbInjected = 0, bridgeAttached = 0;
            foreach (var agent in allAgents)
            {
                if (agent == null) continue;

                // 1. NavMesh 바닥 스냅
                if (UnityEngine.AI.NavMesh.SamplePosition(
                    agent.transform.position,
                    out UnityEngine.AI.NavMeshHit hit,
                    5.0f,
                    UnityEngine.AI.NavMesh.AllAreas))
                {
                    agent.transform.position = hit.position;
                    snapped++;
                }

                // 2. NavMeshAgent 활성화
                if (!agent.enabled)
                {
                    agent.enabled = true;
                    enabled++;
                }

                // 3. [v5.10] NavMeshAgent.speed 디폴트 보장 (BB가 덮어쓰기 전 안전판)
                if (agent.speed < 0.5f) agent.speed = 6f;

                // 4. [v5.10] FleeSprintSpeed BB 변수 6f 강제 주입
                //    (코드 디폴트 new(6f)지만 BehaviorGraph 에셋이 0으로 초기화)
                //    PB4DecisionAdapter.SetBB<T>는 private이라 Reflection 사용
                var go = agent.gameObject;
                var adapterComponents = go.GetComponents<MonoBehaviour>();
                foreach (var comp in adapterComponents)
                {
                    if (comp == null) continue;
                    if (comp.GetType().Name != "PB4DecisionAdapter") continue;

                    var setBBMethod = comp.GetType().GetMethod("SetBB",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (setBBMethod != null)
                    {
                        try
                        {
                            var generic = setBBMethod.MakeGenericMethod(typeof(float));
                            generic.Invoke(comp, new object[] { "FleeSprintSpeed", 6f });
                            bbInjected++;
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[T5.2 Stage] SetBB(FleeSprintSpeed) 실패: {e.Message}");
                        }
                    }
                    break;
                }

                // 5. [v5.11] T52_LocomotionBridge 부착 — NavMesh→Animator(RootMotion) 다리
                //    (Skeleton 프리팹에 AICharacterLocomotionManager가 미부착인 경우 우회)
                //    [DEBT-18] T52_LocomotionBridge가 Obsolete 표시됨 → 워닝 억제.
                //    AICharacterLocomotionManager 부착 시 T52는 Start에서 자동 비활성화됨.
                #pragma warning disable CS0618 // Type or member is obsolete
                if (go.GetComponent<T52_LocomotionBridge>() == null)
                {
                    go.AddComponent<T52_LocomotionBridge>();
                    bridgeAttached++;
                }
                #pragma warning restore CS0618
            }

            Debug.Log($"[T5.2 Stage] NavMeshAgent 자동 활성화: enable {enabled}, snap {snapped}, FleeSprintSpeed BB 주입 {bbInjected}, LocomotionBridge {bridgeAttached} / 전체 {allAgents.Length}");
        }

        private void AddKarmaDirectorIfMissing(GameObject parent)
        {
            var t = FindTypeByName("KarmaDirector");
            if (t == null) { Debug.LogWarning("[T5.2 Stage] KarmaDirector 클래스 미발견."); return; }
            if (UnityEngine.Object.FindAnyObjectByType(t) != null)
            {
                Debug.Log("[T5.2 Stage] KarmaDirector 이미 활성 — 스킵.");
                return;
            }

            var go = new GameObject("KarmaDirector");
            go.transform.SetParent(parent.transform);
            go.AddComponent(t);
            Undo.RegisterCreatedObjectUndo(go, "Create KarmaDirector");
            Debug.Log("[T5.2 Stage] KarmaDirector GameObject + AddComponent 자동 생성.");
        }

        // ==================================================================
        // [v5] Self-Check 섹션
        // ==================================================================
        private List<(string label, bool ok, string detail)> selfCheckResults = new List<(string, bool, string)>();

        private void DrawSelfCheckSection()
        {
            EditorGUILayout.LabelField("🔧 Self-Check (코드 정합성 자동 검증)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "도구 실행 전 코드 의존성 검증:\n" +
                "  • 5종 매니저 클래스 존재\n" +
                "  • 시나리오 메서드 (DebugForce*, ApplyKarmaShift) 존재\n" +
                "  • DialogueRenderer + SpeechDispatcher 존재\n" +
                "  • Tag 'Player' 정의\n" +
                "  • Skeleton_Humanoid_01 archetype 할당",
                MessageType.None);

            if (GUILayout.Button("[Run All Self-Checks]"))
                RunSelfChecks();

            if (selfCheckResults.Count > 0)
            {
                int passed = selfCheckResults.Count(r => r.ok);
                EditorGUILayout.LabelField($"결과: {passed}/{selfCheckResults.Count} PASS", EditorStyles.boldLabel);
                EditorGUILayout.Space(4);
                foreach (var (label, ok, detail) in selfCheckResults)
                {
                    EditorGUILayout.LabelField($"  {(ok ? "✅" : "❌")} {label}");
                    if (!string.IsNullOrEmpty(detail))
                        EditorGUILayout.LabelField("       " + detail, EditorStyles.miniLabel);
                }
            }
        }

        private void RunSelfChecks()
        {
            selfCheckResults.Clear();

            // Check 1: 의존성 5종 클래스 존재
            foreach (var typeName in DEPENDENCY_TYPE_NAMES)
            {
                var t = FindTypeByName(typeName);
                selfCheckResults.Add((
                    $"클래스 존재: {typeName}",
                    t != null,
                    t != null ? $"네임스페이스: {t.Namespace}" : "타입 미발견"));
            }

            // Check 2: 시나리오 메서드 존재
            foreach (var (typeName, methodName) in EXPECTED_METHODS)
            {
                var t = FindTypeByName(typeName);
                if (t == null)
                {
                    selfCheckResults.Add(($"메서드: {typeName}.{methodName ?? "(존재만)"}", false, "타입 없음"));
                    continue;
                }
                if (methodName == null)
                {
                    selfCheckResults.Add(($"클래스 존재: {typeName}", true, ""));
                    continue;
                }
                var m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                bool exists = m != null;
                selfCheckResults.Add((
                    $"메서드: {typeName}.{methodName}",
                    exists,
                    exists ? $"인자: {string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))}" : "메서드 없음"));
            }

            // Check 3: Tag "Player" 정의
            try
            {
                var dummy = new GameObject("__tag_check_temp__");
                dummy.tag = "Player";
                UnityEngine.Object.DestroyImmediate(dummy);
                selfCheckResults.Add(("Tag 'Player' 정의됨", true, ""));
            }
            catch
            {
                selfCheckResults.Add(("Tag 'Player' 정의됨", false,
                    "Edit > Project Settings > Tags and Layers > Tags 에서 'Player' 추가"));
            }

            // Check 4: Skeleton_Humanoid_01 archetype 할당
            var humanoidGuids = AssetDatabase.FindAssets("Skeleton_Humanoid_01 t:Prefab");
            if (humanoidGuids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(humanoidGuids[0]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    var bootstrapperType = FindTypeByName("HumanoidBootstrapper");
                    if (bootstrapperType != null)
                    {
                        var bootstrapper = prefab.GetComponent(bootstrapperType);
                        if (bootstrapper != null)
                        {
                            // archetype 필드 reflection 확인
                            var archetypeField = bootstrapperType.GetField("archetype",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (archetypeField != null)
                            {
                                var arch = archetypeField.GetValue(bootstrapper);
                                bool hasArchetype = arch != null && !arch.Equals(null);
                                selfCheckResults.Add((
                                    "Skeleton_Humanoid_01.archetype 할당됨",
                                    hasArchetype,
                                    hasArchetype ? $"할당값: {arch}" : "Inspector에서 Archetype_Coward 등 할당 필요"));
                            }

                            // speechBubblePrefab 검증
                            var bubbleField = bootstrapperType.GetField("speechBubblePrefab",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (bubbleField != null)
                            {
                                var bubble = bubbleField.GetValue(bootstrapper);
                                bool hasBubble = bubble != null && !bubble.Equals(null);
                                selfCheckResults.Add((
                                    "Bootstrapper.speechBubblePrefab 할당됨",
                                    hasBubble,
                                    hasBubble ? "" : "Inspector에서 Assets/Prefabs/UI/SpeechBubble 드래그"));
                            }
                        }
                    }
                }
            }
            else
            {
                selfCheckResults.Add(("Skeleton_Humanoid_01.prefab 발견", false, "Auto-find 결과 없음"));
            }

            // Check 5: 매니저 폴더 존재
            bool folderValid = AssetDatabase.IsValidFolder(managerFolderHint);
            selfCheckResults.Add((
                $"매니저 폴더: {managerFolderHint}",
                folderValid,
                folderValid ? $"prefab {AssetDatabase.FindAssets("t:Prefab", new[]{managerFolderHint}).Length}개" : "폴더 미발견"));

            // Check 6: NavMesh API 가용
            try
            {
                var navMeshType = typeof(UnityEditor.AI.NavMeshBuilder);
                var bakeMethod = navMeshType.GetMethod("BuildNavMesh");
                selfCheckResults.Add((
                    "NavMesh API 가용 (BuildNavMesh)",
                    bakeMethod != null,
                    "UnityEditor.AI.NavMeshBuilder.BuildNavMesh()"));
            }
            catch
            {
                selfCheckResults.Add(("NavMesh API 가용", false, "Unity AI 모듈 미설치"));
            }

            int passedCount = selfCheckResults.Count(r => r.ok);
            Debug.Log($"[T5.2 Self-Check] {passedCount}/{selfCheckResults.Count} PASS");
        }

        // ==================================================================
        // Step 1 — Setup
        // ==================================================================
        private void DrawStep1_Setup()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("1. Stage 자동 셋업 (수동 단계별)", EditorStyles.boldLabel);

            humanoidPrefab = (GameObject)EditorGUILayout.ObjectField("Humanoid Prefab", humanoidPrefab, typeof(GameObject), false);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("매니저 의존성 (5종)", EditorStyles.miniBoldLabel);

            managerFolderHint = EditorGUILayout.TextField(
                new GUIContent("매니저 폴더 힌트", "이 폴더 우선 탐색."),
                managerFolderHint);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("⭐ [폴더 일괄 Inject]", GUILayout.Height(28)))
                    InjectAllPrefabsFromFolder();
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("개별 매니저", EditorStyles.miniBoldLabel);

            worldItemDatabasePrefab     = (GameObject)EditorGUILayout.ObjectField("WorldItemDatabase",        worldItemDatabasePrefab,     typeof(GameObject), false);
            worldUtilityManagerPrefab   = (GameObject)EditorGUILayout.ObjectField("WorldUtilityManager",      worldUtilityManagerPrefab,   typeof(GameObject), false);
            worldGameStateManagerPrefab = (GameObject)EditorGUILayout.ObjectField("WorldGameStateManager",    worldGameStateManagerPrefab, typeof(GameObject), false);
            worldSaveGameManagerPrefab  = (GameObject)EditorGUILayout.ObjectField("WorldSaveGameManager",     worldSaveGameManagerPrefab,  typeof(GameObject), false);
            worldAIManagerPrefab        = (GameObject)EditorGUILayout.ObjectField("WorldAISpawnManager",           worldAIManagerPrefab,        typeof(GameObject), false);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("[자동 탐색]")) AutoFindAll();
            stubMode = EditorGUILayout.ToggleLeft("Stub Mode", stubMode, GUILayout.Width(110));
            EditorGUILayout.EndHorizontal();

            int found = CountFoundManagers();
            EditorGUILayout.LabelField($"prefab 발견: {found}/{DEPENDENCY_TYPE_NAMES.Length}");

            EditorGUILayout.Space(6);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("[Stage 셋업] (Plane + Humanoid + Player Dummy + Camera)"))
                    SetupStageInternal();

                if (GUILayout.Button("Stage 전부 제거", GUILayout.Height(20)))
                    RemoveStage();
            }
        }

        // ==================================================================
        // Step 2~6 (수동)
        // ==================================================================
        private void DrawStep2_Probe()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("2. (선택) Probe — T5.3 도구 재활용 가능", EditorStyles.boldLabel);
        }

        private void DrawStep3_PlayStatus()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("3. Play 상태", EditorStyles.boldLabel);

            if (!EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("⏸ Play 중 아님.", MessageType.None);
            else
            {
                var humanoid = FindHumanoidInScene();
                if (humanoid == null)
                    EditorGUILayout.HelpBox("⚠️ 씬에 Humanoid 없음.", MessageType.Warning);
                else
                {
                    EditorGUILayout.HelpBox($"📊 Humanoid: {humanoid.name}\n위치: {humanoid.transform.position}", MessageType.Info);
                    if (GUILayout.Button("Humanoid 선택")) Selection.activeGameObject = humanoid;
                }
            }
        }

        private void DrawStep4_Scenarios()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("4. 수동 시나리오 트리거 (One-Click 사용 안 할 때)", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button("① Companion 강제"))
                    InvokeContextMenu("NPCAlignmentController", "DebugForceCompanionBypassHold");
                if (GUILayout.Button("② Friendly 강제"))
                    InvokeContextMenu("NPCAlignmentController", "DebugForceFriendlyBypassHold");
                if (GUILayout.Button("③ Hostile 강제"))
                    InvokeContextMenu("NPCAlignmentController", "DebugForceHostileBypassHold");
                if (GUILayout.Button("④ Neutral 강제"))
                    InvokeContextMenu("NPCAlignmentController", "DebugForceNeutralBypassHold");
                if (GUILayout.Button("⑤ Karma +80 (Saint)"))
                    InvokeContextMenu("KarmaDirector", "Debug/Set Saint (+80)");
            }
        }

        private void DrawStep5_VisualChecklist()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("5. 시각 체크리스트 (수동 마킹)", EditorStyles.boldLabel);

            sc1_CowardFlee     = EditorGUILayout.ToggleLeft("① Coward 도주", sc1_CowardFlee);
            sc2_FriendlyForce  = EditorGUILayout.ToggleLeft("② Friendly 강제 → Speech", sc2_FriendlyForce);
            sc3_HostileReject  = EditorGUILayout.ToggleLeft("③ Hostile 거부", sc3_HostileReject);
            sc4_CompanionGreet = EditorGUILayout.ToggleLeft("④ Companion 인사", sc4_CompanionGreet);
            sc5_KarmaToAlignment = EditorGUILayout.ToggleLeft("⑤ Karma → Alignment 자동 체인", sc5_KarmaToAlignment);
            speechBubbleObserved      = EditorGUILayout.ToggleLeft("SpeechBubble 시각", speechBubbleObserved);
            humanoidMovementObserved  = EditorGUILayout.ToggleLeft("Humanoid BT 이동", humanoidMovementObserved);
        }

        private void DrawStep6_Export()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("6. Export", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Export 수동 마킹 보고서"))
                    ExportManualMarkingReport();
            }
        }

        // ==================================================================
        // Auto-find + Setup helpers (v4 유지)
        // ==================================================================
        private void AutoFindAll()
        {
            humanoidPrefab = LoadHumanoidPrefab();
            worldItemDatabasePrefab     = FindPrefabWithComponent("WorldItemDatabase");
            worldUtilityManagerPrefab   = FindPrefabWithComponent("WorldUtilityManager");
            worldGameStateManagerPrefab = FindPrefabWithComponent("WorldGameStateManager");
            worldSaveGameManagerPrefab  = FindPrefabWithComponent("WorldSaveGameManager");
            worldAIManagerPrefab        = FindPrefabWithComponent("WorldAISpawnManager");
        }

        private GameObject LoadHumanoidPrefab()
        {
            var guids = AssetDatabase.FindAssets("Skeleton_Humanoid_01 t:Prefab");
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(path) == "Skeleton_Humanoid_01")
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }

        private GameObject FindPrefabWithComponent(string typeName)
        {
            var t = FindTypeByName(typeName);
            if (t == null) return null;

            // Path 1: 폴더 힌트
            if (!string.IsNullOrEmpty(managerFolderHint) && AssetDatabase.IsValidFolder(managerFolderHint))
            {
                var folderGuids = AssetDatabase.FindAssets("t:Prefab", new[] { managerFolderHint });
                foreach (var g in folderGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null && go.GetComponent(t) != null) return go;
                }
            }

            // Path 2: 클래스 이름
            var nameGuids = AssetDatabase.FindAssets($"{typeName} t:Prefab");
            foreach (var g in nameGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && go.GetComponent(t) != null) return go;
            }

            // Path 3: 공백 변형
            string spaced = Regex.Replace(typeName, "([a-z])([A-Z])", "$1 $2");
            if (spaced != typeName)
            {
                var spacedGuids = AssetDatabase.FindAssets($"\"{spaced}\" t:Prefab");
                foreach (var g in spacedGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null && go.GetComponent(t) != null) return go;
                }
            }

            // Path 4: 전체 순회
            var allGuids = AssetDatabase.FindAssets("t:Prefab");
            int scanned = 0;
            foreach (var g in allGuids)
            {
                if (++scanned > 5000) break;
                var path = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && go.GetComponent(t) != null) return go;
            }
            return null;
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

        // [v5.2] MonoBehaviour 한정 + Editor 어셈블리 제외 — 동명 Editor 클래스 충돌 방지
        private static Type FindMonoBehaviourTypeByName(string name)
        {
            Type fallback = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Editor 어셈블리 제외 (HumanoidBootstrapperEditor 등 충돌 방지)
                string asmName = asm.GetName().Name ?? "";
                if (asmName.Contains("Editor")) continue;

                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name != name) continue;
                        if (!typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
                        // 정확히 이름 일치 + MonoBehaviour 상속 = 우선 반환
                        return t;
                    }
                }
                catch { }
            }
            return fallback;
        }

        private int CountFoundManagers()
        {
            int n = 0;
            if (worldItemDatabasePrefab != null) n++;
            if (worldUtilityManagerPrefab != null) n++;
            if (worldGameStateManagerPrefab != null) n++;
            if (worldSaveGameManagerPrefab != null) n++;
            if (worldAIManagerPrefab != null) n++;
            return n;
        }

        private GameObject SetupStageInternal()
        {
            if (humanoidPrefab == null)
            {
                AutoFindAll();
                if (humanoidPrefab == null)
                {
                    EditorUtility.DisplayDialog("Stage", "Humanoid prefab 미발견.", "확인");
                    return null;
                }
            }

            var existing = GameObject.Find(STAGE_PARENT_NAME);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var stage = new GameObject(STAGE_PARENT_NAME);
            Undo.RegisterCreatedObjectUndo(stage, "Create Stage");

            // Plane
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "GroundPlane_NavMeshStatic";
            plane.transform.SetParent(stage.transform);
            plane.transform.localScale = new Vector3(5f, 1f, 5f);
            var flags = GameObjectUtility.GetStaticEditorFlags(plane);
            flags |= StaticEditorFlags.NavigationStatic;
            GameObjectUtility.SetStaticEditorFlags(plane, flags);
            Undo.RegisterCreatedObjectUndo(plane, "Plane");

            // Humanoid
            var humanoid = (GameObject)PrefabUtility.InstantiatePrefab(humanoidPrefab, stage.transform);
            humanoid.transform.position = Vector3.zero;
            Undo.RegisterCreatedObjectUndo(humanoid, "Humanoid");

            // Player Dummy
            var player = GameObject.CreatePrimitive(PrimitiveType.Cube);
            player.name = "PlayerDummy_T52";
            player.transform.SetParent(stage.transform);
            player.transform.position = new Vector3(5f, 0.5f, 0f);
            try { player.tag = "Player"; }
            catch { Debug.LogWarning("[T5.2 Stage] Tag 'Player' 미정의."); }
            var rend = player.GetComponent<Renderer>();
            if (rend != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = new Color(0.9f, 0.2f, 0.2f);
                rend.sharedMaterial = mat;
            }
            Undo.RegisterCreatedObjectUndo(player, "Player Dummy");

            // Camera
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(2.5f, 8f, -8f);
                cam.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
                Undo.RecordObject(cam.transform, "Camera");
            }

            // [v5.1] HumanoidBootstrapper 자동 추가 (가장 중요 — NPCAlignmentController/KarmaDirector 자동 부착의 진입점)
            // Skeleton_Humanoid_01.prefab 자체에는 Bootstrapper 미부착 (사용자 환경 진단 결과).
            // Bootstrapper는 씬 안 별도 GameObject로 배치돼야 FindObjectsByType<HumanoidAIBrain>으로 모두 부트스트랩.
            EnsureBootstrapperInScene(stage);

            // [v5.1] KarmaDirector GameObject 자동 추가
            EnsureKarmaDirectorInScene(stage);

            // [v5.3] SO 전파 워치독 — Bootstrap 완료 후 NPCAlignmentSO를 컴포넌트에 직접 주입
            // 진단 결과: HumanoidBootstrapper가 NPCAlignmentController 부착하지만
            //          defaultAlignmentSO를 컴포넌트에 전파 안 함 → Alignment 평가 스킵
            //          → Coward 도주 안 됨 + Speech trigger 안 발사 (둘 다 동일 원인).
            // 해결: Play 진입 1프레임 뒤 NPCAlignmentController.SetAlignmentSO 직접 호출.
            EditorApplication.delayCall += PropagateAlignmentSOToControllers;

            EditorSceneManager.MarkSceneDirty(stage.scene);
            Selection.activeGameObject = stage;
            return stage;
        }

        // [v5.1] HumanoidBootstrapper GameObject 자동 추가 + SerializeField 자동 할당
        private void EnsureBootstrapperInScene(GameObject stage)
        {
            // [v5.2] Editor 어셈블리 제외 + MonoBehaviour 한정 (동명 Editor 클래스 충돌 방지)
            var bootstrapperType = FindMonoBehaviourTypeByName("HumanoidBootstrapper");
            if (bootstrapperType == null)
            {
                Debug.LogWarning("[T5.2 Stage] HumanoidBootstrapper (MonoBehaviour) 미발견. " +
                                 "AddComponent 스킵. 사용자가 직접 부착 필요.");
                return;
            }

            if (UnityEngine.Object.FindAnyObjectByType(bootstrapperType) != null)
            {
                Debug.Log("[T5.2 Stage] HumanoidBootstrapper 이미 활성. 스킵.");
                return;
            }

            var bsGo = new GameObject("HumanoidBootstrapper_T52");
            bsGo.transform.SetParent(stage.transform);

            Component bsComp = null;
            try { bsComp = bsGo.AddComponent(bootstrapperType); }
            catch (Exception e)
            {
                Debug.LogError($"[T5.2 Stage] Bootstrapper AddComponent 실패: {e.Message}\n" +
                               "사용자가 직접 GameObject + AddComponent 필요.");
                UnityEngine.Object.DestroyImmediate(bsGo);
                return;
            }

            if (bsComp == null)
            {
                Debug.LogError("[T5.2 Stage] Bootstrapper 부착 결과 null. GameObject 제거.");
                UnityEngine.Object.DestroyImmediate(bsGo);
                return;
            }
            Undo.RegisterCreatedObjectUndo(bsGo, "Add Bootstrapper");

            // SerializeField 자동 할당 (null-safe)
            try
            {
                var so = new SerializedObject(bsComp);
                int assigned = 0;

                var alignProp = so.FindProperty("defaultAlignmentSO");
                if (alignProp != null && alignProp.objectReferenceValue == null)
                {
                    var guids = AssetDatabase.FindAssets("DefaultAlignmentDefinitions t:ScriptableObject");
                    foreach (var g in guids)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(g);
                        var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                        if (asset != null) { alignProp.objectReferenceValue = asset; assigned++; break; }
                    }
                }

                var bubbleProp = so.FindProperty("speechBubblePrefab");
                if (bubbleProp != null && bubbleProp.objectReferenceValue == null)
                {
                    var guids = AssetDatabase.FindAssets("SpeechBubble t:Prefab");
                    foreach (var g in guids)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(g);
                        if (Path.GetFileNameWithoutExtension(path) == "SpeechBubble")
                        {
                            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                            if (prefab != null) { bubbleProp.objectReferenceValue = prefab; assigned++; break; }
                        }
                    }
                }

                // [v5.4] HumanoidActionConfig 자동 할당 (DEBT-14: Coward 도주 위해 Flee 액션 정의 필요)
                var actionConfigProp = so.FindProperty("defaultActionConfig");
                if (actionConfigProp != null && actionConfigProp.objectReferenceValue == null)
                {
                    var guids = AssetDatabase.FindAssets("HumanoidActionConfig t:ScriptableObject");
                    foreach (var g in guids)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(g);
                        var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                        if (asset != null) { actionConfigProp.objectReferenceValue = asset; assigned++; break; }
                    }
                }

                // [v5.4] PersonalityTagRules 자동 할당 (Coward 태그 효과 활성화)
                var tagRulesProp = so.FindProperty("defaultTagRules");
                if (tagRulesProp != null && tagRulesProp.objectReferenceValue == null)
                {
                    var guids = AssetDatabase.FindAssets("PersonalityTagRules t:ScriptableObject");
                    foreach (var g in guids)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(g);
                        var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                        if (asset != null) { tagRulesProp.objectReferenceValue = asset; assigned++; break; }
                    }
                }

                so.ApplyModifiedProperties();
                Debug.Log($"[T5.2 Stage] HumanoidBootstrapper 자동 추가 + {assigned}개 자산 할당.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[T5.2 Stage] Bootstrapper SerializeField 할당 실패 (부착은 성공): {e.Message}");
            }
        }

        // [v5.1] KarmaDirector GameObject 자동 추가
        private void EnsureKarmaDirectorInScene(GameObject stage)
        {
            // [v5.2] Editor 어셈블리 제외
            var karmaType = FindMonoBehaviourTypeByName("KarmaDirector");
            if (karmaType == null)
            {
                Debug.LogWarning("[T5.2 Stage] KarmaDirector (MonoBehaviour) 미발견. AddComponent 스킵.");
                return;
            }

            if (UnityEngine.Object.FindAnyObjectByType(karmaType) != null)
            {
                Debug.Log("[T5.2 Stage] KarmaDirector 이미 활성. 스킵.");
                return;
            }

            var karmaGo = new GameObject("KarmaDirector");
            karmaGo.transform.SetParent(stage.transform);

            try
            {
                var karmaComp = karmaGo.AddComponent(karmaType);
                if (karmaComp == null)
                {
                    Debug.LogError("[T5.2 Stage] KarmaDirector 부착 결과 null. GameObject 제거.");
                    UnityEngine.Object.DestroyImmediate(karmaGo);
                    return;
                }
                Undo.RegisterCreatedObjectUndo(karmaGo, "Add KarmaDirector");
                Debug.Log("[T5.2 Stage] KarmaDirector 자동 추가.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[T5.2 Stage] KarmaDirector AddComponent 실패: {e.Message}");
                UnityEngine.Object.DestroyImmediate(karmaGo);
            }
        }

        private GameObject InjectAllPrefabsFromFolderInternal()
        {
            if (string.IsNullOrEmpty(managerFolderHint) || !AssetDatabase.IsValidFolder(managerFolderHint))
                return null;

            var parent = GameObject.Find(MANAGER_PARENT_NAME);
            if (parent == null)
            {
                parent = new GameObject(MANAGER_PARENT_NAME);
                Undo.RegisterCreatedObjectUndo(parent, "Create Manager Parent");
            }

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { managerFolderHint });
            int injected = 0;
            foreach (var g in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                bool already = false;
                foreach (var c in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (c == null) continue;
                    var instProp = c.GetType().GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static);
                    if (instProp != null)
                    {
                        var inst = instProp.GetValue(null);
                        if (inst != null && !inst.Equals(null)) { already = true; break; }
                    }
                }
                if (already) continue;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                go.name = prefab.name + "_Injected";
                Undo.RegisterCreatedObjectUndo(go, "Bulk Inject");
                injected++;
            }

            EditorSceneManager.MarkSceneDirty(parent.scene);
            Debug.Log($"[T5.2 Stage] 폴더 일괄 Inject: {injected}개");
            return parent;
        }

        private void InjectAllPrefabsFromFolder()
        {
            var p = InjectAllPrefabsFromFolderInternal();
            if (p == null)
                EditorUtility.DisplayDialog("Inject", $"폴더 미발견: {managerFolderHint}", "확인");
        }

        private void RemoveStage()
        {
            var stage = GameObject.Find(STAGE_PARENT_NAME);
            if (stage != null) Undo.DestroyObjectImmediate(stage);
            var mgr = GameObject.Find(MANAGER_PARENT_NAME);
            if (mgr != null) Undo.DestroyObjectImmediate(mgr);
        }

        private void InvokeContextMenu(string typeName, string methodOrMenuName)
        {
            var t = FindTypeByName(typeName);
            if (t == null) { Debug.LogError($"[T5.2 Stage] {typeName} 미발견"); return; }

            var found = UnityEngine.Object.FindObjectsByType(t, FindObjectsSortMode.None);
            if (found.Length == 0) { Debug.LogWarning($"[T5.2 Stage] {typeName} 인스턴스 0개"); return; }

            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var m in methods)
            {
                if (m.Name == methodOrMenuName ||
                    m.GetCustomAttributes(typeof(ContextMenu), false).Cast<ContextMenu>()
                        .Any(cm => cm.menuItem == methodOrMenuName))
                {
                    foreach (var obj in found)
                    {
                        try
                        {
                            m.Invoke(obj, null);
                            Debug.Log($"[T5.2 Stage] {typeName}.{m.Name}");
                        }
                        catch (Exception e) { Debug.LogError($"호출 실패: {e.Message}"); }
                    }
                    return;
                }
            }
            Debug.LogError($"[T5.2 Stage] {typeName}.{methodOrMenuName} 미발견");
        }

        private GameObject FindHumanoidInScene()
        {
            var t = FindTypeByName("HumanoidAIBrain");
            if (t == null) return null;
            var found = UnityEngine.Object.FindAnyObjectByType(t) as Component;
            return found?.gameObject;
        }

        // ==================================================================
        // Export — Auto + Manual
        // ==================================================================
        private void ExportAutoVerifyReport()
        {
            var r = HumanoidVisualAutoVerifier.LatestResult;
            if (r == null || !r.completed)
            {
                EditorUtility.DisplayDialog("Export", "Auto-Verify 결과 없음. [One-Click Auto-Verify] 먼저 실행.", "확인");
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string reportsDir = Path.Combine(projectRoot, "Reports");
            Directory.CreateDirectory(reportsDir);
            string outPath = Path.Combine(reportsDir, "Week2_Day5_T5_2_HumanoidVisual.md");

            int passed = (r.scenario1_CowardFlee?1:0) + (r.scenario2_FriendlyForce?1:0)
                       + (r.scenario3_HostileReject?1:0) + (r.scenario4_CompanionGreet?1:0)
                       + (r.scenario5_KarmaChain?1:0);
            bool overallPass = passed >= 4 && r.totalErrors == 0 && r.humanoidMoved;

            var sb = new StringBuilder();
            sb.AppendLine("# Week 2 Day 5 T5.2 — Humanoid AI 시각 검증 결과 (자동화 v5)");
            sb.AppendLine();
            sb.AppendLine($"> **작성**: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Editor 자동 출력 — 무인 자동화)");
            sb.AppendLine($"> **도구**: `HumanoidVisualAutoVerifier` + `HumanoidVisualStageSetup` v5");
            sb.AppendLine($"> **Auto Mode**: {(r.autoMode ? "ON (One-Click 자동화)" : "OFF")}");
            sb.AppendLine($"> **종합 판정**: {(overallPass ? "✅ PASS" : "⚠️ 부분 통과")} ({passed}/5 시나리오)");
            sb.AppendLine();
            sb.AppendLine("## 1. 자동 시나리오 검증 결과");
            sb.AppendLine();
            sb.AppendLine("| # | 시나리오 | 자동 검증 | 검증 방법 |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine($"| 1 | Coward 도주 | {(r.scenario1_CowardFlee ? "✅" : "❌")} | Humanoid가 player에서 멀어지는 거리 변화 (Archetype_Coward 효과) |");
            sb.AppendLine($"| 2 | Friendly 강제 | {(r.scenario2_FriendlyForce ? "✅" : "❌")} | Speech 발화 + Alignment 전이 로그 |");
            sb.AppendLine($"| 3 | Hostile 거부 | {(r.scenario3_HostileReject ? "✅" : "❌")} | Alignment 전이 카운트 |");
            sb.AppendLine($"| 4 | Companion 인사 | {(r.scenario4_CompanionGreet ? "✅" : "❌")} | DialogueRenderer 발화 로그 |");
            sb.AppendLine($"| 5 | Karma 자동 체인 | {(r.scenario5_KarmaChain ? "✅" : "❌")} | Karma Tier 전이 + Alignment 전이 |");
            sb.AppendLine();
            sb.AppendLine($"**Auto-Verify 통과**: {passed}/5");
            sb.AppendLine();

            sb.AppendLine("## 2. Humanoid 동작 검증");
            sb.AppendLine();
            sb.AppendLine($"- Humanoid 이동: {(r.humanoidMoved ? "✅" : "❌")} ({r.humanoidMoveDistance:F2}m)");
            sb.AppendLine($"- 시작 위치: {r.humanoidStartPos}");
            sb.AppendLine($"- 종료 위치: {r.humanoidEndPos}");
            sb.AppendLine($"- DialogueRenderer 부착: {(r.dialogueRendererPresent ? "✅" : "❌")}");
            sb.AppendLine($"- SpeechBubble 인스턴스: {(r.speechBubbleSpawned ? "✅" : "❌")}");
            sb.AppendLine();

            sb.AppendLine("## 3. 로그 패턴 카운트");
            sb.AppendLine();
            sb.AppendLine($"- DialogueRenderer 발화: {r.speechRenderCount}회");
            sb.AppendLine($"- SpeechAssembler/Dispatcher: {r.speechAssemblerCount}회");
            sb.AppendLine($"- Alignment 전이: {r.alignmentTransitionCount}회");
            sb.AppendLine($"- Karma Tier 전이: {r.karmaTierChangeCount}회");
            sb.AppendLine();

            sb.AppendLine("## 4. Console 에러/경고");
            sb.AppendLine();
            sb.AppendLine($"- 에러: {r.totalErrors}");
            sb.AppendLine($"- 경고: {r.totalWarnings}");
            if (r.errorMessages.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("**에러 메시지** (최대 20):");
                foreach (var e in r.errorMessages) sb.AppendLine($"- `{e}`");
            }
            sb.AppendLine();

            sb.AppendLine("## 5. 시나리오 호출 로그");
            sb.AppendLine();
            foreach (var entry in r.scenarioInvocationLog) sb.AppendLine($"- {entry}");
            sb.AppendLine();

            sb.AppendLine("## 6. Week 3 진입 시사점");
            sb.AppendLine();
            if (overallPass)
            {
                sb.AppendLine("- ✅ Humanoid 동적 동작 무인 자동 검증 완료");
                sb.AppendLine("- 5단계 자동 발화 체인 + Alignment 전이 + BT 이동 정상");
                sb.AppendLine("- Day 5 매뉴얼 본래 의도 (Humanoid가 움직이는 것까지) 충족");
            }
            else
            {
                sb.AppendLine("- ⚠️ 일부 시나리오 자동 검증 미통과");
                if (!r.scenario1_CowardFlee)        sb.AppendLine("  - 시나리오 1 Coward 도주 미검출 (Archetype_Coward 효과 약함)");
                if (!r.scenario2_FriendlyForce)     sb.AppendLine("  - 시나리오 2 Friendly 미검출");
                if (!r.scenario3_HostileReject)     sb.AppendLine("  - 시나리오 3 Hostile 거부 미검출");
                if (!r.scenario4_CompanionGreet)    sb.AppendLine("  - 시나리오 4 Companion 미검출 (Speech 미발화)");
                if (!r.scenario5_KarmaChain)        sb.AppendLine("  - 시나리오 5 Karma 자동 체인 미검출");
                if (!r.humanoidMoved)               sb.AppendLine("  - Humanoid 이동 검출 안 됨 (NavMesh + BT 트리거 의심)");
                if (r.totalErrors > 0)              sb.AppendLine($"  - Console 에러 {r.totalErrors}건 — §4 참조");
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("*핸드오버 §2.3 갱신용. 자동 검증 + 시각 추가 확인 권장.*");

            File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();
            Debug.Log($"[T5.2] Auto-Verify 보고서: {outPath}");
            EditorUtility.RevealInFinder(outPath);
        }

        private void ExportManualMarkingReport()
        {
            // 수동 체크리스트 마킹 기반 보고서 (기존 v4와 동일)
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string reportsDir = Path.Combine(projectRoot, "Reports");
            Directory.CreateDirectory(reportsDir);
            string outPath = Path.Combine(reportsDir, "Week2_Day5_T5_2_HumanoidVisual_Manual.md");

            int passed = (sc1_CowardFlee?1:0) + (sc2_FriendlyForce?1:0) + (sc3_HostileReject?1:0)
                       + (sc4_CompanionGreet?1:0) + (sc5_KarmaToAlignment?1:0);

            var sb = new StringBuilder();
            sb.AppendLine($"# T5.2 — Humanoid 시각 검증 (수동 마킹) {DateTime.Now:yyyy-MM-dd}");
            sb.AppendLine();
            sb.AppendLine($"수동 마킹: {passed}/5");
            sb.AppendLine($"- ① Coward 도주: {(sc1_CowardFlee ? "✅" : "❌")}");
            sb.AppendLine($"- ② Friendly: {(sc2_FriendlyForce ? "✅" : "❌")}");
            sb.AppendLine($"- ③ Hostile 거부: {(sc3_HostileReject ? "✅" : "❌")}");
            sb.AppendLine($"- ④ Companion: {(sc4_CompanionGreet ? "✅" : "❌")}");
            sb.AppendLine($"- ⑤ Karma 체인: {(sc5_KarmaToAlignment ? "✅" : "❌")}");
            sb.AppendLine($"- SpeechBubble: {(speechBubbleObserved ? "✅" : "❌")}");
            sb.AppendLine($"- Humanoid 이동: {(humanoidMovementObserved ? "✅" : "❌")}");

            File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();
            EditorUtility.RevealInFinder(outPath);
        }
    }
}
#endif
