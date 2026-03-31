// =============================================================================
// TDAMasterInspector_Main.cs  |  TDA Project
// 경로 : Assets/Editor/TDAMasterInspector_Main.cs
//
// 역할 : EditorWindow 기반 — 상태, 생명주기, 씬 스캔, 툴바, 좌측 패널
// 분리 구성:
//   _Main.cs  — 이 파일 (창 기반, 스캔, 좌우 레이아웃)
//   _Tabs.cs  — 탭별 콘텐츠 (Animation / Camera / Effects / Weapon)
//   _Utils.cs — GUIStyle, 폴드아웃, 시각화 헬퍼
// =============================================================================
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TDA.Cameras;
using TDA.Character;
using TDA.Character.AI;
using TDA.Character.Animation;
using TDA.Character.Player;
using TDA.World;
using UnityEditor;
using UnityEngine;

namespace TDA.EditorTools
{
    public partial class TDAMasterInspector : EditorWindow
    {
        // =====================================================================
        // 메뉴 등록
        // =====================================================================
        [MenuItem("Tools/TDA/Master Inspector")]
        public static void OpenWindow()
        {
            var win = GetWindow<TDAMasterInspector>("TDA Master Inspector");
            win.minSize = new Vector2(820, 560);
            win.Show();
        }

        // =====================================================================
        // 내부 상태
        // =====================================================================
        private List<CharacterManager> characters = new List<CharacterManager>();
        private int selectedIndex = -1;
        private CharacterManager SelectedChar =>
            (selectedIndex >= 0 && selectedIndex < characters.Count)
                ? characters[selectedIndex] : null;

        internal enum Tab { Animation, Camera, Effects, Weapon }
        private Tab currentTab = Tab.Animation;

        private Vector2 leftScroll;
        private Vector2 rightScroll;
        private string searchFilter = "";

        // ── 소스 모드 ─────────────────────────────────────────────────────────
        private enum SourceMode { Scene, Prefab }
        private SourceMode sourceMode = SourceMode.Scene;
        private List<CharacterManager> prefabCharacters = new List<CharacterManager>();

        // ── 폴드아웃 상태 저장 ────────────────────────────────────────────────
        // key = "캐릭터명/섹션/서브섹션" 형태
        internal Dictionary<string, bool> foldouts = new Dictionary<string, bool>();

        // ── 탭별 스크롤 ───────────────────────────────────────────────────────
        internal Vector2 tabScroll;

        // =====================================================================
        // Unity 생명주기
        // =====================================================================
        private void OnEnable()
        {
            EditorApplication.hierarchyChanged += ScanScene;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            ScanScene();
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= ScanScene;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode ||
                state == PlayModeStateChange.EnteredEditMode)
            {
                selectedIndex = -1;
                if (state == PlayModeStateChange.EnteredPlayMode)
                    sourceMode = SourceMode.Scene;
                ScanScene();
                Repaint();
            }
        }

        // =====================================================================
        // 씬 / 프리팹 스캔
        // =====================================================================
        private void ScanScene()
        {
            if (sourceMode == SourceMode.Prefab) { ScanPrefabs(); return; }

            characters.Clear();
#if UNITY_2023_1_OR_NEWER
            var found = FindObjectsByType<CharacterManager>(FindObjectsSortMode.None);
#else
            var found = FindObjectsOfType<CharacterManager>();
#endif
            characters.AddRange(
                found.OrderBy(c => c is PlayerManager ? 0 : 1)
                     .ThenBy(c => c.name));
        }

        private void ScanPrefabs()
        {
            characters.Clear();
            prefabCharacters.Clear();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var cm = prefab.GetComponent<CharacterManager>()
                      ?? prefab.GetComponentInChildren<CharacterManager>();
                if (cm == null) continue;
                prefabCharacters.Add(cm);
            }

            prefabCharacters = prefabCharacters
                .OrderBy(c => c is PlayerManager ? 0 : 1)
                .ThenBy(c => c.name).ToList();
            characters.AddRange(prefabCharacters);
        }

        // =====================================================================
        // OnGUI 메인
        // =====================================================================
        private void OnGUI()
        {
            InitStyles();
            if (Application.isPlaying) Repaint();

            DrawToolbar();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeftPanel();
                DrawDivider();
                DrawRightPanel();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 툴바
        // ─────────────────────────────────────────────────────────────────────
        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("⚙ TDA Master Inspector", EditorStyles.boldLabel,
                    GUILayout.Width(180));
                GUILayout.FlexibleSpace();

                // Play/Edit 배지
                if (Application.isPlaying)
                {
                    GUI.color = new Color(0.4f, 1f, 0.4f);
                    GUILayout.Label("● PLAY", EditorStyles.toolbarButton, GUILayout.Width(58));
                    GUI.color = Color.white;
                }
                else
                {
                    GUI.color = new Color(0.65f, 0.65f, 0.65f);
                    GUILayout.Label("■ EDIT", EditorStyles.toolbarButton, GUILayout.Width(58));
                    GUI.color = Color.white;
                }

                // 소스 모드 토글
                GUI.backgroundColor = sourceMode == SourceMode.Scene
                    ? new Color(0.4f, 0.7f, 1f) : Color.white;
                if (GUILayout.Button("🏔 씬", EditorStyles.toolbarButton, GUILayout.Width(48)))
                { sourceMode = SourceMode.Scene; selectedIndex = -1; ScanScene(); }

                GUI.backgroundColor = sourceMode == SourceMode.Prefab
                    ? new Color(1f, 0.75f, 0.3f) : Color.white;
                if (GUILayout.Button("📦 프리팹", EditorStyles.toolbarButton, GUILayout.Width(66)))
                { sourceMode = SourceMode.Prefab; selectedIndex = -1; ScanScene(); }

                GUI.backgroundColor = Color.white;
                GUILayout.Space(4);

                // 검색
                GUILayout.Label("검색:", EditorStyles.toolbarButton, GUILayout.Width(34));
                searchFilter = EditorGUILayout.TextField(searchFilter,
                    EditorStyles.toolbarSearchField, GUILayout.Width(130));
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(20)))
                    searchFilter = "";

                GUILayout.Space(4);

                if (GUILayout.Button("↺ 재스캔", EditorStyles.toolbarButton, GUILayout.Width(68)))
                    ScanScene();

                GUI.backgroundColor = new Color(0.5f, 0.9f, 0.5f);
                if (GUILayout.Button("💾 저장", EditorStyles.toolbarButton, GUILayout.Width(58)))
                {
                    AssetDatabase.SaveAssets();
                    Debug.Log("[TDAMasterInspector] 저장 완료.");
                }
                GUI.backgroundColor = Color.white;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 좌측 패널 — 캐릭터 목록
        // ─────────────────────────────────────────────────────────────────────
        private void DrawLeftPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(220)))
            {
                // 헤더 카운트 바
                DrawPanelHeader();

                EditorGUILayout.Space(2);
                leftScroll = EditorGUILayout.BeginScrollView(leftScroll,
                    GUILayout.ExpandHeight(true));

                var list = characters
                    .Where(c => c != null &&
                        (string.IsNullOrEmpty(searchFilter) ||
                         c.name.IndexOf(searchFilter,
                             StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();

                if (list.Count == 0)
                    DrawEmptyListHint();
                else
                    DrawCharacterList(list);

                EditorGUILayout.EndScrollView();
                DrawLeftFooter();
            }
        }

        private void DrawPanelHeader()
        {
            int players = characters.Count(c => c is PlayerManager);
            int ais = characters.Count(c => c is not PlayerManager);

            Rect r = EditorGUILayout.GetControlRect(false, 28f);
            EditorGUI.DrawRect(r, new Color(0.12f, 0.18f, 0.28f, 1f));

            string label = sourceMode == SourceMode.Scene ? "씬 캐릭터" : "프리팹 캐릭터";
            EditorGUI.LabelField(
                new Rect(r.x + 6, r.y + 2, r.width - 6, 14),
                label, styleSectionBg);

            // 플레이어/AI 카운트 미니 배지
            string badge = $"🟦{players}  🟥{ais}";
            EditorGUI.LabelField(
                new Rect(r.x + 6, r.y + 14, r.width - 6, 12),
                badge, EditorStyles.miniLabel);
        }

        private void DrawEmptyListHint()
        {
            string msg = sourceMode == SourceMode.Scene
                ? "씬에 CharacterManager 없음.\n▶ 실행하거나 [📦 프리팹] 으로 검색하세요."
                : "프로젝트에 CharacterManager 를 가진 프리팹 없음.";
            EditorGUILayout.HelpBox(msg,
                sourceMode == SourceMode.Scene ? MessageType.Info : MessageType.Warning);
        }

        private void DrawCharacterList(List<CharacterManager> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                bool isPlayer = c is PlayerManager;
                bool selected = SelectedChar == c;
                int realIdx = characters.IndexOf(c);
                int warns = CountWarnings(c);

                // 행 배경
                Rect rowRect = EditorGUILayout.GetControlRect(false, 38f);
                Color bg = selected
                    ? new Color(0.25f, 0.42f, 0.75f, 0.5f)
                    : (i % 2 == 0 ? new Color(0.18f, 0.18f, 0.18f, 0.25f) : Color.clear);
                EditorGUI.DrawRect(rowRect, bg);

                // 좌측 색상 스트라이프
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 3, rowRect.height),
                    isPlayer ? new Color(0.35f, 0.65f, 1f) : new Color(1f, 0.42f, 0.42f));

                // 타입 아이콘
                EditorGUI.LabelField(
                    new Rect(rowRect.x + 7, rowRect.y + 10, 18, 18),
                    isPlayer ? "🟦" : "🟥",
                    isPlayer ? styleBadgePlayer : styleBadgeAI);

                // 이름
                string nameLabel = c.name.Length > 18 ? c.name[..18] + "…" : c.name;
                EditorGUI.LabelField(
                    new Rect(rowRect.x + 26, rowRect.y + 5, rowRect.width - 50, 16),
                    nameLabel,
                    selected ? EditorStyles.boldLabel : EditorStyles.label);

                // 서브 텍스트 (AI group)
                if (!isPlayer)
                {
                    EditorGUI.LabelField(
                        new Rect(rowRect.x + 26, rowRect.y + 20, rowRect.width - 50, 12),
                        c.characterGroup.ToString(), EditorStyles.miniLabel);
                }

                // 경고 배지
                if (warns > 0)
                {
                    GUI.color = new Color(1f, 0.8f, 0.1f);
                    EditorGUI.LabelField(
                        new Rect(rowRect.xMax - 30, rowRect.y + 10, 28, 16),
                        $"⚠{warns}", styleWarn);
                    GUI.color = Color.white;
                }
                else
                {
                    EditorGUI.LabelField(
                        new Rect(rowRect.xMax - 22, rowRect.y + 10, 20, 16),
                        "✅", styleOK);
                }

                // 클릭 이벤트
                if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
                {
                    selectedIndex = realIdx;
                    Selection.activeGameObject = c.gameObject;
                    EditorGUIUtility.PingObject(c.gameObject);
                }
            }
        }

        private void DrawLeftFooter()
        {
            if (SelectedChar == null) return;
            EditorGUILayout.Space(3);
            Rect r = EditorGUILayout.GetControlRect(false, 22f);
            EditorGUI.DrawRect(r, new Color(0.1f, 0.1f, 0.15f, 0.8f));
            bool sp = SelectedChar is PlayerManager;
            string info = sp
                ? "🟦 Player"
                : $"🟥 {SelectedChar.characterGroup}";
            EditorGUI.LabelField(
                new Rect(r.x + 4, r.y + 4, r.width, 14),
                info, EditorStyles.miniLabel);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 구분선
        // ─────────────────────────────────────────────────────────────────────
        private void DrawDivider()
        {
            Rect r = EditorGUILayout.GetControlRect(false, GUILayout.Width(1),
                GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(r, new Color(0.08f, 0.08f, 0.08f, 0.6f));
        }

        // ─────────────────────────────────────────────────────────────────────
        // 우측 패널
        // ─────────────────────────────────────────────────────────────────────
        private void DrawRightPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                if (SelectedChar == null)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.HelpBox(
                        characters.Count == 0
                            ? "캐릭터가 없습니다.\n【씬 모드】 실행하거나 씬에 캐릭터를 배치하세요.\n【프리팹 모드】 위 툴바의 [📦 프리팹] 버튼을 누르세요."
                            : "← 좌측 목록에서 캐릭터를 클릭하세요.",
                        MessageType.Info);
                    GUILayout.FlexibleSpace();
                    return;
                }

                DrawCharacterHeader();
                DrawTabBar();
                EditorGUILayout.Space(4);

                tabScroll = EditorGUILayout.BeginScrollView(tabScroll,
                    GUILayout.ExpandHeight(true));

                switch (currentTab)
                {
                    case Tab.Animation: DrawAnimationTab(); break;
                    case Tab.Camera: DrawCameraTab(); break;
                    case Tab.Effects: DrawEffectsTab(); break;
                    case Tab.Weapon: DrawWeaponTab(); break;
                }

                EditorGUILayout.EndScrollView();
            }
        }

        // 캐릭터 헤더 배너
        private void DrawCharacterHeader()
        {
            var c = SelectedChar;
            bool isPlayer = c is PlayerManager;
            int warns = CountWarnings(c);

            Rect r = EditorGUILayout.GetControlRect(false, 46f);
            EditorGUI.DrawRect(r, new Color(0.10f, 0.16f, 0.28f, 1f));

            // 좌측 컬러 바
            EditorGUI.DrawRect(new Rect(r.x, r.y, 4, r.height),
                isPlayer ? new Color(0.35f, 0.65f, 1f) : new Color(1f, 0.42f, 0.42f));

            // 이름
            EditorGUI.LabelField(
                new Rect(r.x + 12, r.y + 6, r.width - 120, 18),
                c.name, EditorStyles.boldLabel);

            // 서브 정보
            string sub = isPlayer ? "Player" : $"AI · {c.characterGroup}";
            if (!isPlayer)
            {
                var aiC = c.GetComponent<AICharacterCombatManager>();
                if (aiC != null) sub += $"  · Lv {aiC.combatLevel}";
            }
            EditorGUI.LabelField(
                new Rect(r.x + 12, r.y + 26, r.width - 120, 14),
                sub, EditorStyles.miniLabel);

            // 경고 요약
            Rect warnRect = new Rect(r.xMax - 110, r.y + 8, 60, 30);
            if (warns > 0)
            {
                GUI.color = new Color(1f, 0.78f, 0.1f);
                EditorGUI.LabelField(warnRect, $"⚠ {warns} 경고", styleWarn);
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = new Color(0.3f, 0.95f, 0.5f);
                EditorGUI.LabelField(warnRect, "✅ 정상", styleOK);
                GUI.color = Color.white;
            }

            // Ping 버튼
            if (GUI.Button(new Rect(r.xMax - 46, r.y + 12, 40, 20), "Ping"))
            {
                EditorGUIUtility.PingObject(c.gameObject);
                Selection.activeGameObject = c.gameObject;
            }
        }

        // 탭 바
        private void DrawTabBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                TabBtn(Tab.Animation, "🎬 애니메이션");
                TabBtn(Tab.Camera, "📷 카메라");
                TabBtn(Tab.Effects, "💥 이펙트");
                TabBtn(Tab.Weapon, "⚔ 무기");
            }
        }

        private void TabBtn(Tab t, string label)
        {
            bool on = currentTab == t;
            GUI.backgroundColor = on ? new Color(0.35f, 0.55f, 1f) : new Color(0.22f, 0.22f, 0.22f);
            if (GUILayout.Toggle(on, label, EditorStyles.toolbarButton))
                currentTab = t;
            GUI.backgroundColor = Color.white;
        }

        // =====================================================================
        // 경고 수 계산
        // =====================================================================
        internal int CountWarnings(CharacterManager c)
        {
            int w = 0;
            var animMgr = c.GetComponent<CharacterAnimationManager>();
            if (animMgr == null) return ++w;

            var animSer = new SerializedObject(animMgr);
            var setSOObj = animSer.FindProperty("baseAnimationSet")
                               ?.objectReferenceValue as CharacterAnimationSetSO;
            if (setSOObj == null) w++;
            else
                foreach (var m in setSOObj.animationMappings)
                    if (m.paramsSO == null || m.paramsSO.targetClip == null) w++;

            var pem = c.GetComponent<PlayerEventManager>();
            if (pem != null)
            {
                var pemSer = new SerializedObject(pem);
                if (pemSer.FindProperty("hitVignetteSequence")?.objectReferenceValue == null) w++;
                if (pemSer.FindProperty("heavyShakeStance")?.objectReferenceValue == null) w++;
            }

#if UNITY_2023_1_OR_NEWER
            var sfxMgr = FindObjectsByType<WorldSoundFXManager>(FindObjectsSortMode.None)
                             .FirstOrDefault();
#else
            var sfxMgr = FindObjectOfType<WorldSoundFXManager>();
#endif
            if (sfxMgr != null)
            {
                var sfxSer = new SerializedObject(sfxMgr);
                var arr = sfxSer.FindProperty("physicalDamageSFX");
                if (arr != null && arr.arraySize == 0) w++;
            }

            if (FindBlurController(c) == null) w++;
            return w;
        }

        // =====================================================================
        // 블러 컨트롤러 검색 헬퍼
        // =====================================================================
        internal MonoBehaviour FindBlurController(CharacterManager c)
        {
            foreach (var mb in c.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb.GetType().Name == "ObjectMotionBlurController") return mb;

#if UNITY_2023_1_OR_NEWER
            var pcam = FindObjectsByType<PlayerCamera>(FindObjectsSortMode.None).FirstOrDefault();
#else
            var pcam = FindObjectOfType<PlayerCamera>();
#endif
            if (pcam != null)
                foreach (var mb in pcam.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb.GetType().Name == "ObjectMotionBlurController") return mb;

            return null;
        }

        // =====================================================================
        // Dirty 마킹
        // =====================================================================
        internal void MarkDirty(UnityEngine.Object obj)
        {
            if (obj == null) return;
            EditorUtility.SetDirty(obj);
        }
    }
}
#endif