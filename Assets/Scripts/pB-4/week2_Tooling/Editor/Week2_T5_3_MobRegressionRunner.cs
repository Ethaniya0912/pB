// =============================================================================
// Week2_T5_3_MobRegressionRunner.cs  |  pB-4 Week 2 Day 5 T5.3  v4
// 역할: Mob 회귀 테스트 EditorWindow.
//       Window > pB-4 > Day 5 > T5.3 Mob Regression
//
// [v4 신규 — 자동 탐색 강화 (HumanoidVisualStageSetup과 동일 패턴)]
//   - 의존성 목록 정정: Session → State, WorldAISpawnManager 추가 (5종)
//   - 폴더 힌트 (Assets/Prefabs/WorldManagers/)
//   - [폴더 일괄 Inject] 버튼 — 의존성 추측 없이 폴더 전체 인스턴스화
//   - 4-path 자동 탐색 (폴더 → 클래스명 → 공백변형 → 전체순회)
// =============================================================================
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TDA.PB4.AI.Mob;
using TDA.PB4.Tooling.MobRegression;

namespace TDA.PB4.Tooling.MobRegression.Editor
{
    public class Week2_T5_3_MobRegressionRunner : EditorWindow
    {
        // 통과 기준
        private const float MIN_FPS_PASS = 60f;
        private const float MIN_MIN_FPS_PASS = 45f;
        private const int MAX_ERRORS = 0;
        private const int MAX_WARNINGS = 5;

        // Mob prefabs
        private GameObject goblinPrefab;
        private GameObject orcPrefab;
        private GameObject skeletonPrefab;
        private int spawnCountPerFaction = 5;
        private float spawnRadius = 3f;
        private float groupSpacing = 12f;

        // [v4] Manager dependencies — 5종
        private GameObject worldItemDatabasePrefab;
        private GameObject worldUtilityManagerPrefab;
        private GameObject worldGameStateManagerPrefab;
        private GameObject worldSaveGameManagerPrefab;
        private GameObject worldAIManagerPrefab;
        private bool stubMode = false;

        // [v4] 폴더 힌트
        private string managerFolderHint = "Assets/Prefabs/WorldManagers";

        // 시각 체크리스트
        private bool visSwarmFormation;
        private bool visDuelFormation;
        private bool visPhalanxFormation;
        private bool visAttackTokenRotation;
        private string attackTokenNote = "(GroupAIManager 미활성 — Mob BT 내부 토큰만 관찰)";
        private bool wk1FilesUntouched;

        private Vector2 scroll;
        private double lastRepaintTime;
        private const double REPAINT_INTERVAL = 0.25;

        private const string SPAWN_PARENT_NAME = "_T53_SpawnedMobs";
        private const string MANAGER_PARENT_NAME = "_T53_ManagerStubs";

        // [v4] 의존성 목록 정정 (Probe와 동기)
        private static readonly string[] DEPENDENCY_TYPE_NAMES =
        {
            "WorldItemDatabase",
            "WorldUtilityManager",
            "WorldGameStateManager",   // v4: Session → State
            "WorldSaveGameManager",
            "WorldAISpawnManager",          // v4: 신규
        };

        [MenuItem("Window/pB-4/Day 5/T5.3 Mob Regression", priority = 50)]
        public static void Open()
        {
            var w = GetWindow<Week2_T5_3_MobRegressionRunner>("T5.3 Mob Regression");
            w.minSize = new Vector2(480, 850);
            w.AutoFindPrefabs();
            w.AutoFindManagerPrefabs();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }
        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }
        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - lastRepaintTime < REPAINT_INTERVAL) return;
            lastRepaintTime = now;
            Repaint();
        }
        private void OnPlayModeChanged(PlayModeStateChange change) => Repaint();

        // ==================================================================
        // GUI
        // ==================================================================
        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("T5.3 — Mob 회귀 테스트  v4", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "v4: 폴더 힌트 + 공백 변형 매칭 + 폴더 일괄 Inject.\n" +
                "통과 기준: Avg FPS≥60, Min FPS≥45, 에러 0, 경고 ≤5.\n" +
                "흐름: Setup(1) → Manager(1.5) → Probe(2) → Play(3) → 시각(4) → Export(5)",
                MessageType.Info);

            DrawStep1_Setup();
            DrawStep1_5_ManagerDependencies();
            DrawStep2_Probe();
            DrawStep3_Measurement();
            DrawStep4_VisualChecks();
            DrawStep5_Export();
            DrawDiagnostics();

            EditorGUILayout.EndScrollView();
        }

        // ==================================================================
        // Step 1 — Setup
        // ==================================================================
        private void DrawStep1_Setup()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("1. Setup — Mob 자산 + 스폰", EditorStyles.boldLabel);

            goblinPrefab   = (GameObject)EditorGUILayout.ObjectField("Goblin Prefab",   goblinPrefab,   typeof(GameObject), false);
            orcPrefab      = (GameObject)EditorGUILayout.ObjectField("Orc Prefab",      orcPrefab,      typeof(GameObject), false);
            skeletonPrefab = (GameObject)EditorGUILayout.ObjectField("Skeleton Prefab", skeletonPrefab, typeof(GameObject), false);

            if (GUILayout.Button("자동 탐색 (Data/Prefabs/Mob/)"))
                AutoFindPrefabs();

            EditorGUILayout.Space(4);
            spawnCountPerFaction = EditorGUILayout.IntSlider("팩션당 마릿수", spawnCountPerFaction, 3, 10);
            spawnRadius          = EditorGUILayout.Slider("그룹 내 산개 반경", spawnRadius, 1f, 10f);
            groupSpacing         = EditorGUILayout.Slider("그룹간 간격",        groupSpacing, 5f, 30f);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("기존 Mob 감지"))
                {
                    int n = FindObjectsByType<MobAIBrain>(FindObjectsSortMode.None).Length;
                    EditorUtility.DisplayDialog("T5.3", $"씬 내 MobAIBrain 인스턴스: {n}개", "확인");
                }
                if (GUILayout.Button("5×3 자동 스폰 (현재 씬)"))
                    SpawnAllFactions();
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("스폰된 Mob 전부 제거", GUILayout.Height(20)))
                    DespawnSpawnedMobs();
            }
        }

        // ==================================================================
        // [v4] Step 1.5 — Manager Dependencies
        // ==================================================================
        private void DrawStep1_5_ManagerDependencies()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("1.5 Manager Dependencies (v4 — 5종)", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Mob_01은 외부 매니저 싱글톤에 의존.\n" +
                "v4 변경: Session → State + WorldAISpawnManager 추가, 폴더 힌트 + 공백 변형 매칭.",
                MessageType.None);

            // [v4] 폴더 힌트
            managerFolderHint = EditorGUILayout.TextField(
                new GUIContent("매니저 폴더 힌트", "이 폴더 우선 탐색."),
                managerFolderHint);

            // [v4] 폴더 일괄 Inject
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("⭐ [폴더 일괄 Inject] — 폴더의 모든 매니저 prefab 인스턴스화 (권장)",
                    GUILayout.Height(28)))
                    InjectAllPrefabsFromFolder();
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("개별 매니저 (자동 탐색 또는 수동)", EditorStyles.miniBoldLabel);

            worldItemDatabasePrefab     = (GameObject)EditorGUILayout.ObjectField("WorldItemDatabase",        worldItemDatabasePrefab,     typeof(GameObject), false);
            worldUtilityManagerPrefab   = (GameObject)EditorGUILayout.ObjectField("WorldUtilityManager",      worldUtilityManagerPrefab,   typeof(GameObject), false);
            worldGameStateManagerPrefab = (GameObject)EditorGUILayout.ObjectField("WorldGameStateManager",    worldGameStateManagerPrefab, typeof(GameObject), false);
            worldSaveGameManagerPrefab  = (GameObject)EditorGUILayout.ObjectField("WorldSaveGameManager",     worldSaveGameManagerPrefab,  typeof(GameObject), false);
            worldAIManagerPrefab        = (GameObject)EditorGUILayout.ObjectField("WorldAISpawnManager",           worldAIManagerPrefab,        typeof(GameObject), false);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("[자동 탐색] (4-path)"))
                AutoFindManagerPrefabs();
            if (GUILayout.Button("씬 내 매니저 감지"))
                DetectManagersInScene();
            EditorGUILayout.EndHorizontal();

            int foundPrefabs = CountFoundManagerPrefabs();
            int sceneActive = CountSceneActiveManagers();
            EditorGUILayout.LabelField($"prefab 발견: {foundPrefabs}/5    씬 활성: {sceneActive}/5");

            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || foundPrefabs == 0))
            {
                if (GUILayout.Button($"[Inject 개별 매니저 {foundPrefabs}개]"))
                    InjectIndividualManagers();
            }

            EditorGUILayout.Space(4);

            stubMode = EditorGUILayout.ToggleLeft(
                new GUIContent("Stub Mode", "매니저 prefab 없을 때 빈 stub + Equipment 일시 disable"),
                stubMode);

            using (new EditorGUI.DisabledScope(!stubMode || EditorApplication.isPlaying))
            {
                if (GUILayout.Button("[Stub] 빈 매니저 일괄 적용"))
                    ApplyStubMode();
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("매니저 stub/instance 일괄 제거"))
                    RemoveAllManagerStubs();
            }
        }

        // ==================================================================
        // Step 2 — Probe
        // ==================================================================
        private void DrawStep2_Probe()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("2. Probe 부착", EditorStyles.boldLabel);

            var existing = FindFirstObjectByType<Week2_T5_3_MobRegressionProbe>();
            if (existing != null)
            {
                EditorGUILayout.HelpBox($"Probe 부착됨: {existing.gameObject.name}", MessageType.None);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Probe 선택")) Selection.activeGameObject = existing.gameObject;
                if (GUILayout.Button("Probe 제거")) Undo.DestroyObjectImmediate(existing.gameObject);
                EditorGUILayout.EndHorizontal();

                if (existing.stubMode != stubMode)
                    EditorGUILayout.HelpBox(
                        $"⚠️ Probe.stubMode={existing.stubMode}, Runner.stubMode={stubMode} 불일치.",
                        MessageType.Warning);
            }
            else
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
                {
                    if (GUILayout.Button("Probe 생성 (Stub Mode = " + (stubMode ? "ON" : "OFF") + ")"))
                    {
                        var go = new GameObject("_T53_Probe");
                        Undo.RegisterCreatedObjectUndo(go, "Create T5.3 Probe");
                        var probe = Undo.AddComponent<Week2_T5_3_MobRegressionProbe>(go);
                        probe.stubMode = stubMode;
                        Selection.activeGameObject = go;
                    }
                }
            }
        }

        // ==================================================================
        // Step 3 — Measurement Status
        // ==================================================================
        private void DrawStep3_Measurement()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("3. 측정 진행 상태", EditorStyles.boldLabel);

            var probe = Week2_T5_3_MobRegressionProbe.Instance;
            var r = Week2_T5_3_MobRegressionProbe.LatestResult;

            if (!EditorApplication.isPlaying)
            {
                if (r != null && r.sessionCompleted)
                {
                    EditorGUILayout.HelpBox(
                        $"✅ 측정 완료\n" +
                        $"  Duration: {r.durationSeconds:F1}s, Frames: {r.frameSamples}\n" +
                        $"  Avg/Min FPS: {r.avgFps:F1}/{r.minFps:F1}\n" +
                        $"  Errors/Warnings: {r.errors}/{r.warnings}\n" +
                        $"  Brains: {r.activeBrainCount}\n" +
                        $"  의존성: {r.presentDependencies.Count}/{r.presentDependencies.Count + r.missingDependencies.Count} 활성, Stub: {(r.stubModeActive ? "ON" : "OFF")}",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("⏸ Play 중 아님.", MessageType.None);
                }
            }
            else
            {
                if (probe == null)
                    EditorGUILayout.HelpBox("⚠️ Probe 없음. Play 종료 → Step 2.", MessageType.Warning);
                else
                    DrawProbeStatusPanel(probe, r);
            }
        }

        private void DrawProbeStatusPanel(Week2_T5_3_MobRegressionProbe probe, MobRegressionResult r)
        {
            string phaseLabel = probe.Phase switch
            {
                ProbePhase.NotStarted => "❓ 시작 안 됨",
                ProbePhase.WarmingUp  => $"⏳ 워밍업 ({probe.ElapsedSeconds:F1}s / {probe.warmupSeconds:F1}s)",
                ProbePhase.Measuring  => probe.RemainingSeconds < 0
                    ? $"📊 측정 중 ({probe.ElapsedSeconds:F1}s / 무제한)"
                    : $"📊 측정 중 ({probe.ElapsedSeconds:F1}s / {probe.autoStopAfterSeconds:F0}s, 남음 {probe.RemainingSeconds:F1}s)",
                ProbePhase.Finalized  => $"✅ 측정 완료 ({probe.ElapsedSeconds:F1}s)",
                _ => probe.Phase.ToString()
            };

            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = probe.Phase == ProbePhase.Finalized ? new Color(0.7f, 1f, 0.7f) :
                                  probe.Phase == ProbePhase.WarmingUp ? new Color(1f, 0.95f, 0.7f) :
                                  new Color(0.85f, 0.95f, 1f);
            EditorGUILayout.BeginVertical("box");
            GUI.backgroundColor = prevColor;

            EditorGUILayout.LabelField(phaseLabel, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Brains: {probe.TrackedBrainCount}    Stub: {(r.stubModeActive ? "ON" : "OFF")}");
            EditorGUILayout.LabelField($"Frames: {r.frameSamples}    Transitions: {r.stateTransitions}");
            EditorGUILayout.LabelField($"FPS — avg: {r.avgFps:F1}, min: {r.minFps:F1}");
            EditorGUILayout.LabelField($"Console — errors: {r.errors}, warnings: {r.warnings}");

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("의존성 (5종):", EditorStyles.miniBoldLabel);
            foreach (var name in DEPENDENCY_TYPE_NAMES)
            {
                bool present = r.presentDependencies.Contains(name);
                EditorGUILayout.LabelField($"  {(present ? "✅" : "❌")} {name}");
            }

            if (!string.IsNullOrEmpty(r.lastError))
                EditorGUILayout.HelpBox("Probe 예외: " + r.lastError, MessageType.Error);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(probe.Phase == ProbePhase.Finalized))
            {
                if (GUILayout.Button("⏹ 즉시 종료", GUILayout.Height(28)))
                {
                    probe.FinalizeProbe();
                    Debug.Log("[T5.3 Runner] 즉시 종료 요청.");
                }
            }
        }

        // ==================================================================
        // Step 4 — Visual Checks
        // ==================================================================
        private void DrawStep4_VisualChecks()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("4. 시각 체크리스트", EditorStyles.boldLabel);

            DrawVisualRow(ref visSwarmFormation, "Goblin",
                "Goblin 5마리 — Swarm 산개",
                "5마리 흩어져 표적 둘러싸기 시도");
            DrawVisualRow(ref visDuelFormation, "Orc",
                "Orc 5마리 — Duel 1:1",
                "각자 다른 표적, 1:1 교전");
            DrawVisualRow(ref visPhalanxFormation, "Skeleton",
                "Skeleton 5마리 — Phalanx 정렬",
                "횡대/방진 유지");

            EditorGUILayout.BeginHorizontal();
            visAttackTokenRotation = EditorGUILayout.ToggleLeft(
                "AttackToken 순환 또는 N/A", visAttackTokenRotation, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Focus All", GUILayout.Width(80)))
                FocusOnFaction(null);
            EditorGUILayout.EndHorizontal();
            attackTokenNote = EditorGUILayout.TextField("  메모", attackTokenNote);

            wk1FilesUntouched = EditorGUILayout.ToggleLeft(
                "git diff: Week 1 파일 직접 수정 없음", wk1FilesUntouched);
        }

        private void DrawVisualRow(ref bool toggle, string factionPrefix, string label, string hint)
        {
            EditorGUILayout.BeginHorizontal();
            toggle = EditorGUILayout.ToggleLeft(new GUIContent(label, hint), toggle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Focus", GUILayout.Width(60)))
                FocusOnFaction(factionPrefix);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("    " + hint, EditorStyles.miniLabel);
        }

        // ==================================================================
        // Step 5 — Export
        // ==================================================================
        private void DrawStep5_Export()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("5. Export", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                var r = Week2_T5_3_MobRegressionProbe.LatestResult;
                bool canExport = r != null && r.sessionCompleted;

                using (new EditorGUI.DisabledScope(!canExport))
                {
                    if (GUILayout.Button("Export Reports/Week2_Day5_T5_3_MobRegression.md", GUILayout.Height(30)))
                        ExportMarkdown();
                }
                if (!canExport)
                    EditorGUILayout.HelpBox("활성화 조건: Probe Finalized + Play 종료.", MessageType.None);
            }
        }

        private void DrawDiagnostics()
        {
            if (!EditorApplication.isPlaying) return;
            var probe = Week2_T5_3_MobRegressionProbe.Instance;
            if (probe == null) return;

            if (probe.Phase == ProbePhase.Measuring &&
                probe.autoStopAfterSeconds > 0 &&
                probe.ElapsedSeconds > probe.autoStopAfterSeconds + 1f)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("🔧 진단", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    $"경과: {probe.ElapsedSeconds:F1}s, 임계: {probe.autoStopAfterSeconds:F0}s\n조치: [⏹ 즉시 종료]",
                    MessageType.Warning);
            }
        }

        // ==================================================================
        // Setup helpers
        // ==================================================================
        private void AutoFindPrefabs()
        {
            goblinPrefab = LoadPrefab("Goblin_01", "/Mob/");
            orcPrefab = LoadPrefab("Orc_01", "/Mob/");
            skeletonPrefab = LoadPrefab("Skeleton_01", "/Mob/");
        }

        private GameObject LoadPrefab(string name, string pathContains)
        {
            var guids = AssetDatabase.FindAssets($"{name} t:Prefab");
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (path.Contains(pathContains) && Path.GetFileNameWithoutExtension(path) == name)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }

        private void SpawnAllFactions()
        {
            if (goblinPrefab == null && orcPrefab == null && skeletonPrefab == null)
            {
                EditorUtility.DisplayDialog("T5.3", "팩션 prefab 미발견.", "확인");
                return;
            }

            var parent = GameObject.Find(SPAWN_PARENT_NAME);
            if (parent != null) Undo.DestroyObjectImmediate(parent);
            parent = new GameObject(SPAWN_PARENT_NAME);
            Undo.RegisterCreatedObjectUndo(parent, "Spawn T5.3 Mobs");

            SpawnGroup(goblinPrefab,   "Goblin",   new Vector3(-groupSpacing, 0, 0), parent.transform);
            SpawnGroup(orcPrefab,      "Orc",      Vector3.zero,                      parent.transform);
            SpawnGroup(skeletonPrefab, "Skeleton", new Vector3( groupSpacing, 0, 0), parent.transform);

            EditorSceneManager.MarkSceneDirty(parent.scene);
            Selection.activeGameObject = parent;
        }

        private void SpawnGroup(GameObject prefab, string label, Vector3 center, Transform parent)
        {
            if (prefab == null) { Debug.LogWarning($"[T5.3] {label} prefab 없음 — 스킵"); return; }
            for (int i = 0; i < spawnCountPerFaction; i++)
            {
                float angle = (i / (float)spawnCountPerFaction) * Mathf.PI * 2f;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * spawnRadius;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                instance.name = $"{label}_{i:D2}";
                instance.transform.position = center + offset;
                Undo.RegisterCreatedObjectUndo(instance, "Spawn T5.3 Mob");
            }
        }

        private void DespawnSpawnedMobs()
        {
            var parent = GameObject.Find(SPAWN_PARENT_NAME);
            if (parent != null) Undo.DestroyObjectImmediate(parent);
        }

        // ==================================================================
        // [v4] Manager dependency helpers — 4-path search
        // ==================================================================
        private void AutoFindManagerPrefabs()
        {
            worldItemDatabasePrefab     = FindPrefabWithComponent("WorldItemDatabase");
            worldUtilityManagerPrefab   = FindPrefabWithComponent("WorldUtilityManager");
            worldGameStateManagerPrefab = FindPrefabWithComponent("WorldGameStateManager");
            worldSaveGameManagerPrefab  = FindPrefabWithComponent("WorldSaveGameManager");
            worldAIManagerPrefab        = FindPrefabWithComponent("WorldAISpawnManager");

            int found = CountFoundManagerPrefabs();
            Debug.Log($"[T5.3] 매니저 자동 탐색: {found}/5");
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

            // Path 2: 클래스 이름 매칭
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

        private int CountFoundManagerPrefabs()
        {
            int n = 0;
            if (worldItemDatabasePrefab != null) n++;
            if (worldUtilityManagerPrefab != null) n++;
            if (worldGameStateManagerPrefab != null) n++;
            if (worldSaveGameManagerPrefab != null) n++;
            if (worldAIManagerPrefab != null) n++;
            return n;
        }

        private int CountSceneActiveManagers()
        {
            int n = 0;
            foreach (var name in DEPENDENCY_TYPE_NAMES)
            {
                var t = FindTypeByName(name);
                if (t != null && UnityEngine.Object.FindAnyObjectByType(t) != null) n++;
            }
            return n;
        }

        private void DetectManagersInScene()
        {
            int n = CountSceneActiveManagers();
            var sb = new StringBuilder($"씬 내 매니저 활성: {n}/5\n\n");
            foreach (var name in DEPENDENCY_TYPE_NAMES)
                sb.AppendLine($"{name}: {(IsManagerActive(name) ? "✅" : "❌")}");
            EditorUtility.DisplayDialog("T5.3", sb.ToString(), "확인");
        }

        private bool IsManagerActive(string typeName)
        {
            var t = FindTypeByName(typeName);
            return t != null && UnityEngine.Object.FindAnyObjectByType(t) != null;
        }

        private void InjectIndividualManagers()
        {
            var parent = GameObject.Find(MANAGER_PARENT_NAME);
            if (parent == null)
            {
                parent = new GameObject(MANAGER_PARENT_NAME);
                Undo.RegisterCreatedObjectUndo(parent, "Create Manager Parent");
            }

            int injected = 0;
            injected += InjectOneManager(worldItemDatabasePrefab,     "WorldItemDatabase",     parent);
            injected += InjectOneManager(worldUtilityManagerPrefab,   "WorldUtilityManager",   parent);
            injected += InjectOneManager(worldGameStateManagerPrefab, "WorldGameStateManager", parent);
            injected += InjectOneManager(worldSaveGameManagerPrefab,  "WorldSaveGameManager",  parent);
            injected += InjectOneManager(worldAIManagerPrefab,        "WorldAISpawnManager",        parent);

            EditorSceneManager.MarkSceneDirty(parent.scene);
            Debug.Log($"[T5.3] 개별 매니저 inject: {injected}개.");
        }

        private int InjectOneManager(GameObject prefab, string typeName, GameObject parent)
        {
            if (prefab == null) return 0;
            if (IsManagerActive(typeName)) { Debug.Log($"[T5.3] {typeName} 이미 활성 — 스킵"); return 0; }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            instance.name = typeName + "_Injected";
            Undo.RegisterCreatedObjectUndo(instance, "Inject Manager");
            return 1;
        }

        // [v4] 폴더 일괄 Inject
        private void InjectAllPrefabsFromFolder()
        {
            if (string.IsNullOrEmpty(managerFolderHint) || !AssetDatabase.IsValidFolder(managerFolderHint))
            {
                EditorUtility.DisplayDialog("폴더 일괄 Inject",
                    $"폴더 미발견: {managerFolderHint}\n예: Assets/Prefabs/WorldManagers", "확인");
                return;
            }

            var parent = GameObject.Find(MANAGER_PARENT_NAME);
            if (parent == null)
            {
                parent = new GameObject(MANAGER_PARENT_NAME);
                Undo.RegisterCreatedObjectUndo(parent, "Create Manager Parent");
            }

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { managerFolderHint });
            int injected = 0;
            int skipped = 0;
            var skippedNames = new List<string>();

            foreach (var g in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                bool alreadyActive = false;
                var components = prefab.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var c in components)
                {
                    if (c == null) continue;
                    var instProp = c.GetType().GetProperty("Instance",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (instProp != null)
                    {
                        var inst = instProp.GetValue(null);
                        if (inst != null && !inst.Equals(null)) { alreadyActive = true; break; }
                    }
                }
                if (alreadyActive) { skipped++; skippedNames.Add(prefab.name); continue; }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                instance.name = prefab.name + "_Injected";
                Undo.RegisterCreatedObjectUndo(instance, "Bulk Inject");
                injected++;
            }

            EditorSceneManager.MarkSceneDirty(parent.scene);
            Selection.activeGameObject = parent;

            string skippedList = skipped > 0 ? "\n\n스킵: " + string.Join(", ", skippedNames) : "";
            EditorUtility.DisplayDialog("폴더 일괄 Inject",
                $"✅ {managerFolderHint}\n  인스턴스화: {injected}개  스킵: {skipped}개{skippedList}", "확인");
        }

        private void ApplyStubMode()
        {
            var parent = GameObject.Find(MANAGER_PARENT_NAME);
            if (parent == null)
            {
                parent = new GameObject(MANAGER_PARENT_NAME);
                Undo.RegisterCreatedObjectUndo(parent, "Create Manager Parent");
            }

            int created = 0;
            foreach (var typeName in DEPENDENCY_TYPE_NAMES)
            {
                if (IsManagerActive(typeName)) continue;
                var t = FindTypeByName(typeName);
                if (t == null) { Debug.LogWarning($"[T5.3] {typeName} 타입 미발견"); continue; }
                var stub = new GameObject(typeName + "_Stub");
                stub.transform.SetParent(parent.transform);
                stub.AddComponent(t);
                Undo.RegisterCreatedObjectUndo(stub, "Stub");
                created++;
            }

            EditorSceneManager.MarkSceneDirty(parent.scene);
            stubMode = true;
            Debug.Log($"[T5.3] Stub Mode: {created}개 stub 매니저 생성");
        }

        private void RemoveAllManagerStubs()
        {
            var parent = GameObject.Find(MANAGER_PARENT_NAME);
            if (parent != null) Undo.DestroyObjectImmediate(parent);
        }

        // ==================================================================
        // Scene View Focus
        // ==================================================================
        private void FocusOnFaction(string prefix)
        {
            var brains = FindObjectsByType<MobAIBrain>(FindObjectsSortMode.None);
            GameObject[] matches = string.IsNullOrEmpty(prefix)
                ? brains.Select(b => b.gameObject).ToArray()
                : brains.Where(b => b.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .Select(b => b.gameObject).ToArray();

            if (matches.Length == 0)
            {
                EditorUtility.DisplayDialog("Focus", "매치 없음", "확인");
                return;
            }

            Selection.objects = matches;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null)
            {
                Bounds bounds = new Bounds(matches[0].transform.position, Vector3.one);
                foreach (var go in matches) bounds.Encapsulate(go.transform.position);
                bounds.Expand(spawnRadius * 2f + 2f);
                sv.Frame(bounds, false);
                sv.Repaint();
            }
        }

        // ==================================================================
        // Markdown Export
        // ==================================================================
        private void ExportMarkdown()
        {
            var r = Week2_T5_3_MobRegressionProbe.LatestResult;
            if (r == null || !r.sessionCompleted)
            {
                EditorUtility.DisplayDialog("T5.3", "측정 결과 없음.", "확인");
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string reportsDir = Path.Combine(projectRoot, "Reports");
            Directory.CreateDirectory(reportsDir);
            string outPath = Path.Combine(reportsDir, "Week2_Day5_T5_3_MobRegression.md");

            bool passFps = r.avgFps >= MIN_FPS_PASS && r.minFps >= MIN_MIN_FPS_PASS;
            bool passErrors = r.errors <= MAX_ERRORS;
            bool passWarnings = r.warnings <= MAX_WARNINGS;
            bool passCount = r.activeBrainCount >= spawnCountPerFaction * 3;
            bool passVisual = visSwarmFormation && visDuelFormation && visPhalanxFormation;
            bool passDeps = r.missingDependencies.Count == 0;
            bool overallPass = passFps && passErrors && passWarnings && passCount && passVisual && wk1FilesUntouched && passDeps;

            var sb = new StringBuilder();
            sb.AppendLine("# Week 2 Day 5 T5.3 — Mob 회귀 테스트 결과 (v4)");
            sb.AppendLine();
            sb.AppendLine($"> **작성**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"> **도구**: `Week2_T5_3_MobRegressionRunner` v4");
            sb.AppendLine($"> **종합 판정**: {(overallPass ? "✅ PASS" : "❌ FAIL")}");
            sb.AppendLine($"> **Stub Mode**: {(r.stubModeActive ? "ON" : "OFF")}");
            sb.AppendLine();
            sb.AppendLine("## 1. 통과 기준");
            sb.AppendLine();
            sb.AppendLine("| 지표 | 기준 | 측정값 | 판정 |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine($"| Avg FPS | ≥{MIN_FPS_PASS} | {r.avgFps:F1} | {(r.avgFps >= MIN_FPS_PASS ? "✅" : "❌")} |");
            sb.AppendLine($"| Min FPS | ≥{MIN_MIN_FPS_PASS} | {r.minFps:F1} | {(r.minFps >= MIN_MIN_FPS_PASS ? "✅" : "❌")} |");
            sb.AppendLine($"| 에러 | ={MAX_ERRORS} | {r.errors} | {(r.errors <= MAX_ERRORS ? "✅" : "❌")} |");
            sb.AppendLine($"| 경고 | ≤{MAX_WARNINGS} | {r.warnings} | {(r.warnings <= MAX_WARNINGS ? "✅" : "❌")} |");
            sb.AppendLine($"| Brain | ≥{spawnCountPerFaction * 3} | {r.activeBrainCount} | {(passCount ? "✅" : "❌")} |");
            sb.AppendLine($"| Manager 의존성 | 5/5 | {r.presentDependencies.Count}/5 | {(passDeps ? "✅" : "❌")} |");
            sb.AppendLine($"| Swarm | 시각 | - | {(visSwarmFormation ? "✅" : "❌")} |");
            sb.AppendLine($"| Duel | 시각 | - | {(visDuelFormation ? "✅" : "❌")} |");
            sb.AppendLine($"| Phalanx | 시각 | - | {(visPhalanxFormation ? "✅" : "❌")} |");
            sb.AppendLine($"| AttackToken | 시각 | - | {(visAttackTokenRotation ? "✅" : "⚠️")} |");
            sb.AppendLine($"| Week 1 무수정 | git diff | - | {(wk1FilesUntouched ? "✅" : "❌")} |");
            sb.AppendLine();
            sb.AppendLine("## 2. 환경");
            sb.AppendLine();
            sb.AppendLine($"- Duration: {r.durationSeconds:F1}s, Frames: {r.frameSamples}, Brains: {r.activeBrainCount}, Transitions: {r.stateTransitions}, Stub: {(r.stubModeActive ? "ON" : "OFF")}");
            sb.AppendLine();

            sb.AppendLine("## 3. 상태 분포");
            sb.AppendLine();
            if (r.finalStateDistribution.Count == 0) sb.AppendLine("(없음)");
            else
            {
                sb.AppendLine("| State | Count |");
                sb.AppendLine("|---|---|");
                foreach (var kv in r.finalStateDistribution.OrderByDescending(kv => kv.Value))
                    sb.AppendLine($"| {kv.Key} | {kv.Value} |");
            }
            sb.AppendLine();

            sb.AppendLine("## 4. Fear 분포");
            sb.AppendLine();
            if (r.fearByFaction.Count == 0) sb.AppendLine("(없음)");
            else
            {
                sb.AppendLine("| Faction | Count | Avg | Min | Max |");
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var kv in r.fearByFaction.OrderBy(kv => kv.Key))
                    sb.AppendLine($"| {kv.Key} | {kv.Value.count} | {kv.Value.Avg:F3} | {kv.Value.min:F3} | {kv.Value.max:F3} |");
            }
            sb.AppendLine();

            sb.AppendLine("## 5. PanicChain");
            sb.AppendLine();
            if (!r.panicChainTriggered) sb.AppendLine("⚠️ 미트리거.");
            else
            {
                sb.AppendLine($"- Before: {r.panicChainFearBefore:F3}, After: {r.panicChainFearAfter:F3}");
                sb.AppendLine($"- 결과: {r.panicChainNote}");
            }
            sb.AppendLine();

            if (r.errors > 0)
            {
                sb.AppendLine("## 6. Console 에러");
                sb.AppendLine();
                foreach (var e in r.errorMessages) sb.AppendLine($"- `{e}`");
                sb.AppendLine();
            }
            if (r.warnings > 0)
            {
                sb.AppendLine("## 7. Console 경고");
                sb.AppendLine();
                foreach (var w in r.warningMessages) sb.AppendLine($"- `{w}`");
                sb.AppendLine();
            }

            sb.AppendLine("## 8. Week 3 시사점");
            sb.AppendLine();
            if (overallPass) sb.AppendLine("- ✅ Week 3 D1 진입 가능");
            else
            {
                sb.AppendLine("- ❌ 회귀 의심");
                if (!passFps) sb.AppendLine("  - FPS 저하");
                if (!passErrors) sb.AppendLine($"  - 에러 {r.errors}건");
                if (!passWarnings) sb.AppendLine($"  - 경고 {r.warnings}건");
                if (!passCount) sb.AppendLine("  - Brain 초기화 실패");
                if (!passDeps) sb.AppendLine($"  - 의존성 누락 {r.missingDependencies.Count}건");
                if (!passVisual) sb.AppendLine("  - GroupPolicy 시각 미통과");
                if (!wk1FilesUntouched) sb.AppendLine("  - Week 1 파일 수정");
            }
            sb.AppendLine();

            sb.AppendLine("## 9. 매니저 의존성 검증 (v4 — 5종)");
            sb.AppendLine();
            sb.AppendLine("| 매니저 | 상태 |");
            sb.AppendLine("|---|---|");
            foreach (var name in DEPENDENCY_TYPE_NAMES)
            {
                bool present = r.presentDependencies.Contains(name);
                sb.AppendLine($"| {name} | {(present ? "✅" : "❌")} |");
            }
            sb.AppendLine();
            if (r.stubModeActive)
                sb.AppendLine("**Stub Mode**: Equipment 컴포넌트 일시 disable. 측정 종료 시 자동 복원.");
            else if (r.missingDependencies.Count > 0)
                sb.AppendLine("**누락**: " + string.Join(", ", r.missingDependencies));
            else
                sb.AppendLine("✅ 5종 모두 활성. 실제 환경 동등.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("*핸드오버 §2.4 갱신용.*");

            File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();
            Debug.Log($"[T5.3] 보고서: {outPath}");
            EditorUtility.RevealInFinder(outPath);
        }
    }
}
#endif
