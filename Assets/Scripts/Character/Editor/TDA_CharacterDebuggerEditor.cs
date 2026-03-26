// =============================================================================
// TDA_CharacterDebuggerEditor.cs  |  TDA Project
// Layer  : Editor Tool — 컨텍스트 메뉴 등록 + CustomEditor Inspector
//
// 변경 이력:
//   [REQ-4] SceneView 우클릭 메뉴 최상위(priority=0) 등록
//           Hierarchy 우클릭 [TDA] 메뉴 항목 추가
//           GameView 외부에서도 정상 동작
//   [REQ-7] Inspector에 창 크기/텍스트 크기 파라미터 슬라이더 표시
//   [REQ-8] Inspector에 표시 항목 플래그 섹션
// =============================================================================
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TDA.Character;
using TDA.Character.AI;
using TDA.Character.Player;
using TDA.EditorTools;

namespace TDA.EditorTools
{
    // =========================================================================
    // [REQ-4] SceneView 우클릭 최상위 등록
    // =========================================================================
    [InitializeOnLoad]
    // [수정] CS0260 에러 해결: 하단에 partial 선언이 있으므로 여기에도 partial 키워드 추가
    public static partial class TDA_CharacterDebuggerMenuRegistrar
    {
        static TDA_CharacterDebuggerMenuRegistrar()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sv)
        {
            Event e = Event.current;

            // [REQ-4] ContextClick 시점에 Selection 직접 사용 (MouseDown 캡처 제거)
            if (e.type != EventType.ContextClick) return;

            GameObject sel = Selection.activeGameObject;
            if (sel == null || sel.GetComponent<CharacterManager>() == null) return;

            var menu = new GenericMenu();

            // [REQ-4] priority 없이 "" prefix 사용 → 최상위 아이템으로 표시
            menu.AddItem(new GUIContent("TDA/디버거 표시"), false, () => AttachAndShow(sel));
            menu.AddItem(new GUIContent("TDA/디버거 숨기기"), false, () => HideDebugger(sel));
            menu.AddSeparator("TDA/");
            menu.AddItem(new GUIContent("TDA/디버거 제거"), false, () => RemoveDebugger(sel));

            menu.ShowAsContext();
            e.Use();
        }

        private static void AttachAndShow(GameObject go)
        {
            if (go.GetComponent<PlayerManager>() != null) EnableDebugger<PlayerManagerDebugger>(go);
            else if (go.GetComponent<AICharacterManager>() != null) EnableDebugger<AICharacterManagerDebugger>(go);
            else EnableDebugger<CharacterManagerDebugger>(go);

            Selection.activeGameObject = go;
            EditorUtility.SetDirty(go);
        }

        private static void EnableDebugger<T>(GameObject go) where T : CharacterManagerDebugger
        {
            var d = go.GetComponent<T>() ?? go.AddComponent<T>();
            d.showDebugHUD = true;
            EditorUtility.SetDirty(d);
        }

        private static void HideDebugger(GameObject go)
        {
            var d = go.GetComponent<CharacterManagerDebugger>();
            if (d != null) { d.showDebugHUD = false; EditorUtility.SetDirty(d); }
        }

        private static void RemoveDebugger(GameObject go)
        {
            var ai = go.GetComponent<AICharacterManagerDebugger>();
            var player = go.GetComponent<PlayerManagerDebugger>();
            var base_ = go.GetComponent<CharacterManagerDebugger>();
            if (ai != null) Object.DestroyImmediate(ai);
            if (player != null) Object.DestroyImmediate(player);
            if (base_ != null) Object.DestroyImmediate(base_);
            EditorUtility.SetDirty(go);
        }
    }

    // =========================================================================
    // [REQ-4] Hierarchy 우클릭 메뉴 — 최상위에 표시 (priority 낮은 값)
    // =========================================================================
    public static class TDA_DebuggerHierarchyMenu
    {
        [MenuItem("GameObject/TDA/디버거 표시", false, 0)]
        private static void ShowFromHierarchy()
        {
            var go = Selection.activeGameObject;
            if (go == null || go.GetComponent<CharacterManager>() == null) return;
            TDA_CharacterDebuggerMenuRegistrar.EnableDebuggerPublic(go);
        }

        [MenuItem("GameObject/TDA/디버거 숨기기", false, 1)]
        private static void HideFromHierarchy()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var d = go.GetComponent<CharacterManagerDebugger>();
            if (d != null) { d.showDebugHUD = false; EditorUtility.SetDirty(d); }
        }

        [MenuItem("GameObject/TDA/디버거 표시", true)]
        [MenuItem("GameObject/TDA/디버거 숨기기", true)]
        private static bool ValidateHierarchyMenu()
            => Selection.activeGameObject != null &&
               Selection.activeGameObject.GetComponent<CharacterManager>() != null;
    }

    // =========================================================================
    // CustomEditor Inspector
    // =========================================================================
    [CustomEditor(typeof(CharacterManagerDebugger), true)]
    public class CharacterManagerDebuggerInspector : Editor
    {
        private static readonly Color HeaderColor = new Color(0.12f, 0.22f, 0.38f);
        private static readonly Color AccentColor = new Color(0.18f, 0.46f, 0.71f);
        private static readonly Color WarningColor = new Color(0.8f, 0.4f, 0.1f);
        private static readonly Color OkColor = new Color(0.2f, 0.7f, 0.3f);

        // 섹션 접기 상태
        private bool _foldHUD = true;
        private bool _foldSize = false;
        private bool _foldPause = true;
        private bool _foldDisplay = false;
        private bool _foldAnim = false;
        private bool _foldHistory = false;
        private bool _foldAIExtra = false;

        public override void OnInspectorGUI()
        {
            var d = (CharacterManagerDebugger)target;
            serializedObject.Update();

            Color prev = GUI.backgroundColor;

            // ── 헤더 ───────────────────────────────────────────────────────
            DrawHeader(d);
            GUILayout.Space(4);

            // ── HUD 토글 큰 버튼 ────────────────────────────────────────────
            GUI.backgroundColor = d.showDebugHUD ? OkColor : new Color(0.3f, 0.3f, 0.3f);
            var bigBtn = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
            if (GUILayout.Button(d.showDebugHUD ? "▶  HUD ON  (클릭하면 끔)" : "○  HUD OFF  (클릭하면 켬)",
                bigBtn, GUILayout.Height(30)))
            {
                d.showDebugHUD = !d.showDebugHUD;
                EditorUtility.SetDirty(d);
            }
            GUI.backgroundColor = prev;
            GUILayout.Space(6);

            // ── [REQ-7] 창/텍스트 크기 ─────────────────────────────────────
            _foldSize = EditorGUILayout.Foldout(_foldSize, "📐  창/텍스트 크기 설정", true, EditorStyles.foldoutHeader);
            if (_foldSize)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("hudWidth"), new GUIContent("HUD 너비 (px)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("hudHeight"), new GUIContent("HUD 높이 (px)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("fontSize"), new GUIContent("본문 글꼴 크기"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sectionFontSize"), new GUIContent("섹션 헤더 크기"));
                EditorGUI.indentLevel--;
            }

            // ── [REQ-8] 표시 항목 제어 ─────────────────────────────────────
            _foldDisplay = EditorGUILayout.Foldout(_foldDisplay, "👁  표시 항목 제어", true, EditorStyles.foldoutHeader);
            if (_foldDisplay)
            {
                EditorGUI.indentLevel++;
                GUILayout.Label("공통 탭", EditorStyles.boldLabel);
                Prop("showSectionFlags", "상태 플래그");
                Prop("showSectionStats", "생존 스탯");
                Prop("showSectionCombat", "전투 상태");

                GUILayout.Space(4);
                GUILayout.Label("처형/QTE 탭", EditorStyles.boldLabel);
                Prop("showSectionExecution", "처형 시스템");
                Prop("showSectionQTE", "QTE 상태");

                GUILayout.Space(4);
                GUILayout.Label("타겟 탭", EditorStyles.boldLabel);
                Prop("showSectionTargetInfo", "타겟 기본 정보");
                Prop("showSectionTargetPanorama", "타겟 파노라마");

                // AI 전용
                var ai = target as AICharacterManagerDebugger;
                if (ai != null)
                {
                    GUILayout.Space(4);
                    GUILayout.Label("AI 탭", EditorStyles.boldLabel);
                    Prop("showSectionFSM", "FSM 상태");
                    Prop("showSectionPoise", "포이즈 시각화");
                    Prop("showSectionNavMesh", "NavMesh 이동");
                    Prop("showSectionAIExec", "AI 처형 상태");
                }
                EditorGUI.indentLevel--;
            }

            // ── [REQ-6] 애니메이션 옵션 ─────────────────────────────────────
            _foldAnim = EditorGUILayout.Foldout(_foldAnim, "🎬  애니메이션 탭 옵션", true, EditorStyles.foldoutHeader);
            if (_foldAnim)
            {
                EditorGUI.indentLevel++;
                Prop("showActionIDEnumName", "ActionID → enum 이름 표시");
                Prop("fallbackToAnimEventEnum", "AnimationEventType enum 폴백");
                EditorGUI.indentLevel--;
            }

            // ── Pause 트리거 ────────────────────────────────────────────────
            _foldPause = EditorGUILayout.Foldout(_foldPause, "⏸  Pause 트리거", true, EditorStyles.foldoutHeader);
            if (_foldPause)
            {
                EditorGUI.indentLevel++;
                Prop("pauseOnDeath", "사망 시 Pause");
                Prop("pauseOnPoiseBreak", "포이즈 파괴 시 Pause");
                Prop("pauseOnEventTypes", "특정 이벤트 시 Pause", true);

                var pg = serializedObject.FindProperty("pauseOnGroggy");
                var pe = serializedObject.FindProperty("pauseOnExecutionStart");
                var ps = serializedObject.FindProperty("pauseOnStateEnter");
                var pgr = serializedObject.FindProperty("pauseOnGripChange");

                if (pg != null) EditorGUILayout.PropertyField(pg, new GUIContent("그로기 진입 시 Pause"));
                if (pe != null) EditorGUILayout.PropertyField(pe, new GUIContent("처형 시작 시 Pause"));
                if (ps != null) EditorGUILayout.PropertyField(ps, new GUIContent("지정 State 진입 시 Pause"));
                if (pgr != null) EditorGUILayout.PropertyField(pgr, new GUIContent("파지 변경 시 Pause"));
                EditorGUI.indentLevel--;
            }

            // ── 히스토리 ───────────────────────────────────────────────────
            _foldHistory = EditorGUILayout.Foldout(_foldHistory, "📋  히스토리", true, EditorStyles.foldoutHeader);
            if (_foldHistory)
            {
                EditorGUI.indentLevel++;
                Prop("maxHistoryCount", "최대 기록 수");
                EditorGUI.indentLevel--;
            }

            // ── Play Mode 제어 ──────────────────────────────────────────────
            if (Application.isPlaying)
            {
                GUILayout.Space(6);
                GUILayout.Label("── Play Mode 제어 ──", EditorStyles.miniLabel);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("히스토리 초기화", GUILayout.Height(22))) d.ClearHistory();
                GUI.backgroundColor = WarningColor;
                if (GUILayout.Button("⏸ Pause", GUILayout.Height(22))) EditorApplication.isPaused = true;
                GUI.backgroundColor = OkColor;
                if (GUILayout.Button("▶ 재개", GUILayout.Height(22))) EditorApplication.isPaused = false;
                GUI.backgroundColor = prev;
                GUILayout.EndHorizontal();
            }

            // ── Edit Mode Null 미리보기 ─────────────────────────────────────
            if (!Application.isPlaying)
            {
                GUILayout.Space(4);
                var cm = d.GetComponent<CharacterManager>();
                if (cm != null) DrawQuickNullPreview(cm, d);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void Prop(string name, string label, bool includeChildren = false)
        {
            var p = serializedObject.FindProperty(name);
            if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label), includeChildren);
        }

        private void DrawHeader(CharacterManagerDebugger d)
        {
            Rect r = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, HeaderColor);
            string typeName = d.GetType().Name.Replace("Debugger", "");
            EditorGUI.LabelField(new Rect(r.x + 8, r.y + 4, r.width - 16, 16),
                $"TDA Debugger  —  {typeName}",
                new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, normal = { textColor = Color.white } });
            EditorGUI.LabelField(new Rect(r.x + 8, r.y + 20, r.width - 16, 14),
                "캐릭터 우클릭 → TDA/디버거 표시",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.7f, 0.85f, 1f) } });
        }

        private void DrawQuickNullPreview(CharacterManager c, CharacterManagerDebugger d)
        {
            GUILayout.Label("── Edit Mode Null 체크 ──", EditorStyles.miniLabel);
            NullRow("characterController", c.characterController == null, true);
            NullRow("animator", c.animator == null, true);
            NullRow("characterNetworkManager", c.characterNetworkManager == null, true);
            NullRow("characterAnimationManager", c.characterAnimationManager == null, true);
            NullRow("characterEventManager", c.characterEventManager == null, true);
            NullRow("characterCombatManager", c.characterCombatManager == null, false);
            NullRow("characterStatsManager", c.characterStatsManager == null, false);

            if (d is AICharacterManagerDebugger)
            {
                var ai = c as AICharacterManager;
                if (ai != null)
                {
                    NullRow("defaultState", ai.defaultState == null, true);
                    NullRow("aiCharacterCombatManager", ai.aiCharacterCombatManager == null, true);
                    NullRow("navMeshAgent", ai.navMeshAgent == null, true);
                }
            }
            if (d is PlayerManagerDebugger)
            {
                var pm = c as PlayerManager;
                if (pm != null)
                {
                    NullRow("playerNetworkManager", pm.playerNetworkManager == null, true);
                    NullRow("playerCombatManager", pm.playerCombatManager == null, true);
                    NullRow("playerLocomotionManager", pm.playerLocomotionManager == null, true);
                }
            }
        }

        private void NullRow(string label, bool isNull, bool critical)
        {
            GUILayout.BeginHorizontal();
            Color ic = isNull ? (critical ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 0.6f, 0.2f))
                               : new Color(0.3f, 0.9f, 0.4f);
            var st = new GUIStyle(EditorStyles.label) { normal = { textColor = ic } };
            GUILayout.Label(isNull ? "✗" : "✓", st, GUILayout.Width(16));
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
        }
    }
}

// ── TDA_CharacterDebuggerMenuRegistrar 에 public 헬퍼 추가 (Hierarchy 메뉴 공유)
namespace TDA.EditorTools
{
    public static partial class TDA_CharacterDebuggerMenuRegistrar
    {
        public static void EnableDebuggerPublic(GameObject go)
        {
            if (go.GetComponent<PlayerManager>() != null) EnableDebugger<PlayerManagerDebugger>(go);
            else if (go.GetComponent<AICharacterManager>() != null) EnableDebugger<AICharacterManagerDebugger>(go);
            else EnableDebugger<CharacterManagerDebugger>(go);
            Selection.activeGameObject = go;
            EditorUtility.SetDirty(go);
        }

        // [수정] CS0121 에러 해결: 기존 코드 하단에 중복 선언되어있던 private static void EnableDebugger<T>(GameObject go) 제거 완료
    }
}
#endif