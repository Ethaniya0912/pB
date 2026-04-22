// =============================================================================
// PerceptionProbeWindow.cs  |  pB-4 AI 인지 테스트 프로브 v2.1
//
// [4단계 재귀 비판 결과 수정 목록]
//   B4:  패닉 전파 → [인지경유] → [BT강제/효과직접]로 레이블 교정
//        HandlePanicChain() 은 FleeSwarmAction.OnStart() private → 외부 호출 구조상 불가
//   B7:  Gizmo 레이블 "0.3×50m" 삭제 → SoundPerception baseRadius=15f 명시
//   B8:  DrawPersistentGizmos Queue → List + reverse-remove 교체 (GC 절감)
//   B10: DrawRealtimeStatus Repaint() → EditorApplication.update + 0.1s 타이머
//   E3:  다수 AI 선택 지원 — Selection.objects 순회
//   E4:  FireForceCombat() Player 캐시 추가
//   E5:  fear 수정에 Undo.RecordObject() 적용
//
// 설계 원칙:
//   ① 스크립트 1개 — EditorWindow + SceneView Gizmo 통합
//   ② 씬 설정 0 — SceneView.duringSceneGui 자동 구독
//   ③ 7개 프로브 — [인지경유] vs [BT강제] 명확 분류
//   ④ 도형+텍스트 항상 병행
//
// 메뉴: pB-4 → Perception Probe (Ctrl+Shift+P)
// =============================================================================
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using TDA.Character.AI;
using TDA.PB4.AI.Mob;
using TDA.PB4.AI.Perception;
using TDA.PB4.Diagnostics;
using Unity.Behavior;
using UnityEditor;
using UnityEngine;

namespace TDA.PB4.Editor
{
    public class PerceptionProbeWindow : EditorWindow
    {
        // =====================================================================
        // 프로브 정의
        // [B4 Fix] 패닉 전파 → isPerception=false (BT강제) 로 교정
        //          실제로 HandlePanicChain()을 경유하지 않음 → 효과직접 분류
        // =====================================================================

        private struct ProbeGizmo
        {
            public Vector3 position;
            public float radius;
            public Color color;
            public string label;
            public float expireTime;
        }

        private static readonly (string icon, string name, string key, bool isPerception, string tooltip)[] PROBES =
        {
            ("🔊", "발자국 소리",    "Alt+1", true,
             "[인지경유] SoundEventEmitter.OnSoundEmitted → SoundPerception.HandleSoundEmitted 파이프라인 통과"),
            ("💥", "전투 소음",     "Alt+2", true,
             "[인지경유] Combat 볼륨 0.8. SoundPerception 청각 범위+벽 감쇠 포함"),
            ("😱", "공포 증폭",     "Alt+3", false,
             "[BT강제] brain.fear += deltaAmount. UpdateDecision 강제 호출로 Flee 분기 테스트"),
            ("🧊", "공포+각성 리셋", "Alt+4", false,
             "[BT강제] fear=0 + Awareness=Unaware + LastHeard/Seen 초기화. 테스트 상태 복원"),
            ("💭", "메모리 주입",    "Alt+5", false,
             "[BT강제] LastSeenPosition BB 직접 기록. StalkAction 기억 수색 테스트"),
            ("🌐", "패닉 전파",     "Alt+6", false,   // [B4 Fix] false로 교정
             "[BT강제/효과직접] brain.fear 직접 수정. HandlePanicChain() 경유 불가 (FleeSwarmAction private)"),
            ("🎯", "강제 전투 진입", "Alt+7", false,
             "[BT강제] ForceSetAwareness(Combat) + BB 직접 갱신. Attack 3단계 플로우 테스트"),
            ("🏔️", "NarrowPath 주입", "Alt+8", false,
             "[BT강제] TerrainTags BB = NarrowPath. CircleStrafe 반경x0.4 + Strike 타이밍x0.5 검증. Zone: Ctrl+클릭"),
            ("🔓", "NarrowPath 해제", "Alt+9", false,
             "[BT강제] TerrainTags BB 클리어. NarrowPath 상태 종료 후 정상 CircleStrafe 복귀 확인."),
        };

        // =====================================================================
        // 상태
        // =====================================================================

        // -1 = 비선택 상태 (프로브 미활성). 씬뷰 클릭/ev.Use() 없음 → 오브젝트 이동 정상.
        // ESC 또는 현재 선택 버튼 재클릭 시 -1로 복귀.
        private int _selectedProbe = -1;

        // 프로브 발사 볼륨 (0~1) — Footstep 기본=0.3, Combat=0.8
        // 높을수록 sourceRadius 증가 → AI 반응 범위 확대
        private float _probeVolume = 0.5f;
        private float _fearDelta = 0.3f;
        private Vector3 _mouseWorldPos;
        private bool _mouseHitValid;
        private Vector2 _logScroll;

        // ── NarrowPath Zone ───────────────────────────────────────────────────
        private bool _narrowZoneActive = false;      // Zone 활성 여부
        private Vector3 _narrowZoneCenter;           // Zone 중심
        private float _narrowZoneWidth = 3f;        // 통로 폭 (m)
        private float _narrowZoneLength = 8f;        // 통로 길이 (m)
        private float _narrowZoneHeight = 3f;        // 통로 높이 (m)
        private bool _narrowZoneDragging = false;    // Ctrl+드래그 중
        private Vector3 _narrowZoneDragStart;
        private bool _narrowZoneAutoUpdate = true;   // AI 위치 감지 자동 적용

        // ── AI 목록 패널 ─────────────────────────────────────────────────
        private Vector2 _mobListScroll;
        private AICharacterManager _pinnedMob;  // 윈도우에서 선택한 몹
        private bool _showMobList = true;
        private bool _showDrivePanel = false;
        private bool _showSOPanel = false;
        private Dictionary<AICharacterManager, bool> _debuggerToggle = new();
        private Dictionary<AICharacterManager, bool> _driveExpanded = new();
        private List<AICharacterManager> _cachedMobList = new();
        private float _mobListCacheTime = -99f;
        private const float MOB_CACHE_SEC = 0.5f;
        // SO 편집 dirty 추적
        private HashSet<UnityEngine.Object> _dirtySOSet = new();
        private readonly List<string> _eventLog = new();
        private const int MAX_LOG = 5;

        // [B8 Fix] Queue → List (매 프레임 new Queue 생성 제거)
        private readonly List<ProbeGizmo> _gizmoList = new();
        private const float GIZMO_DURATION = 2.5f;

        // [E4 Fix] Player 캐시
        private GameObject _cachedPlayer;

        // [B10 Fix] Repaint 타이머 (0.1s 간격)
        private double _lastRepaintTime;
        private const double REPAINT_INTERVAL = 0.1;

        // =====================================================================
        // 메뉴
        // =====================================================================

        [MenuItem("pB-4/Perception Probe %#p", false, 200)]
        public static void Open() => GetWindow<PerceptionProbeWindow>("🔬 Perception Probe");

        // =====================================================================
        // 생명주기
        // =====================================================================

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            titleContent = new GUIContent("🔬 Perception Probe");
            minSize = new Vector2(700f, 400f);  // 좌우 분할에 필요한 최소 너비

            // [B10 Fix] EditorApplication.update 타이머 등록
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= OnEditorUpdate;
        }

        // [B10 Fix] 0.1s 간격으로만 Repaint
        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRepaintTime >= REPAINT_INTERVAL)
            {
                _lastRepaintTime = now;
                Repaint();
            }
        }

        // =====================================================================
        // EditorWindow UI
        // =====================================================================

        private Vector2 _leftScroll, _rightScroll;

        private void OnGUI()
        {
            float totalW = position.width;
            float totalH = position.height;
            float splitW = Mathf.Max(totalW * 0.52f, 260f);  // 좌측 너비
            float rightW = totalW - splitW - 6f;

            EditorGUILayout.BeginHorizontal();

            // ── 좌측: 대상 AI 목록 + 상세 패널 ──────────────────────────────
            EditorGUILayout.BeginVertical(GUILayout.Width(splitW), GUILayout.ExpandHeight(true));
            DrawTargetAI();
            EditorGUILayout.EndVertical();

            // 구분선
            GUILayout.Box(GUIContent.none, GUILayout.Width(2), GUILayout.ExpandHeight(true));

            // ── 우측: 프로브 버튼 + 슬라이더 + 상태 + 로그 ──────────────────
            EditorGUILayout.BeginVertical(GUILayout.Width(rightW), GUILayout.ExpandHeight(true));
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            DrawProbeButtons();
            if (_selectedProbe == 2)
            {
                EditorGUILayout.Space(4);
                DrawFearSlider();
            }
            if (_selectedProbe == 7 || _selectedProbe == 8)
            {
                EditorGUILayout.Space(4);
                DrawNarrowZonePanel();
            }
            EditorGUILayout.Space(6);
            DrawRealtimeStatus();
            EditorGUILayout.Space(6);
            DrawEventLog();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        // ── NarrowPath Zone 설정 패널 ──────────────────────────────────────────
        private void DrawNarrowZonePanel()
        {
            var boxStyle = new GUIStyle(GUI.skin.box);
            EditorGUILayout.BeginVertical(boxStyle);

            // 헤더
            EditorGUILayout.LabelField("🏔️  NarrowPath Zone 설정",
                new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 });
            EditorGUILayout.Space(2);

            // Zone 활성 토글
            EditorGUILayout.BeginHorizontal();
            bool newActive = EditorGUILayout.Toggle("Zone 활성", _narrowZoneActive,
                GUILayout.Width(140));
            if (newActive != _narrowZoneActive)
            {
                _narrowZoneActive = newActive;
                if (!newActive)
                    AddLog("🔓 NarrowPath Zone 비활성화");
            }
            GUILayout.Label("(Ctrl+클릭: Scene에 배치)", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (_narrowZoneActive)
            {
                // 크기 슬라이더
                _narrowZoneWidth = EditorGUILayout.Slider("폭 (m)", _narrowZoneWidth, 1f, 8f);
                _narrowZoneLength = EditorGUILayout.Slider("길이 (m)", _narrowZoneLength, 2f, 20f);
                _narrowZoneHeight = EditorGUILayout.Slider("높이 (m)", _narrowZoneHeight, 1f, 6f);

                EditorGUILayout.Space(2);
                _narrowZoneAutoUpdate = EditorGUILayout.Toggle("AI 자동 감지 (Play중)", _narrowZoneAutoUpdate);

                // 위치 표시
                EditorGUILayout.LabelField("중심",
                    $"({_narrowZoneCenter.x:F1}, {_narrowZoneCenter.y:F1}, {_narrowZoneCenter.z:F1})",
                    EditorStyles.miniLabel);

                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("선택 AI에 주입", GUILayout.Height(26)))
                    FireNarrowPathInject(GetTargetAIs());
                if (GUILayout.Button("선택 AI 해제", GUILayout.Height(26)))
                    FireNarrowPathClear(GetTargetAIs());
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Zone 제거", GUILayout.Height(22)))
                {
                    _narrowZoneActive = false;
                    FireNarrowPathClear(GetTargetAIs());
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.HelpBox(
                    "CircleStrafe가 NarrowPath 감지 시:\n  · OrbitRadius × 0.4 (배회 반경 60% 축소)\n  · StrikeTriggerTime × 0.5 (Strike 전환 50% 빠름)",
                    MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        // ── 대상 AI ──
        private void DrawTargetAI()
        {
            // ── AI 목록 헤더 ─────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            _showMobList = EditorGUILayout.Foldout(_showMobList, "대상 AI", true, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("씬 갱신", GUILayout.Width(60)))
                _mobListCacheTime = -99f;
            EditorGUILayout.EndHorizontal();

            if (!_showMobList) return;

            // AI 목록 캐시 갱신
            if (Time.realtimeSinceStartup - _mobListCacheTime > MOB_CACHE_SEC)
            {
                _mobListCacheTime = Time.realtimeSinceStartup;
                _cachedMobList = new List<AICharacterManager>(
                    FindObjectsByType<AICharacterManager>(FindObjectsSortMode.None));
                _cachedMobList.Sort((a, b) => string.Compare(a.name, b.name));
            }

            if (_cachedMobList.Count == 0)
            {
                EditorGUILayout.HelpBox("씬에 AICharacterManager가 없습니다.", MessageType.Info);
                return;
            }

            // ── 목록 스크롤 뷰 ───────────────────────────────────────────────
            // 목록: 남은 세로 공간을 전부 사용 (스크롤 없이 쫙 표시)
            _mobListScroll = EditorGUILayout.BeginScrollView(_mobListScroll,
                GUILayout.ExpandHeight(true));

            foreach (var mob in _cachedMobList)
            {
                if (mob == null) continue;
                bool isSelected = (Selection.activeGameObject == mob.gameObject)
                               || (_pinnedMob == mob);
                bool isHierSel = Selection.gameObjects != null &&
                                  System.Array.IndexOf(Selection.gameObjects, mob.gameObject) >= 0;

                // 행 배경
                Color rowBg = isSelected
                    ? new Color(0.25f, 0.55f, 0.95f, 0.85f)
                    : new Color(0.18f, 0.18f, 0.22f, 0.6f);

                EditorGUILayout.BeginHorizontal(GUILayout.Height(20f));

                // ─ 선택 버튼 (이름)
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = rowBg;
                GUIStyle rowSt = new GUIStyle(GUI.skin.button)
                { alignment = TextAnchor.MiddleLeft, fontSize = 11, fixedHeight = 20 };
                rowSt.normal.textColor = isSelected ? Color.white : new Color(0.85f, 0.88f, 0.95f);
                rowSt.fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal;

                var brain = mob.GetComponent<MobAIBrain>();
                string winner = "---";
                string awareStr = "";
                if (brain != null)
                {
                    // UtilityWinner BB 읽기 시도
                    var btAgent = mob.GetComponent<BehaviorGraphAgent>();
                    if (btAgent?.BlackboardReference != null)
                    {
                        try { btAgent.BlackboardReference.GetVariable("UtilityWinner", out BlackboardVariable<string> bv); winner = bv?.Value ?? "---"; } catch { }
                    }
                }
                var perception = mob.GetComponent<AIPerceptionSystem>();
                if (perception != null) awareStr = perception.CurrentAwareness.ToString()[..Mathf.Min(3, perception.CurrentAwareness.ToString().Length)];

                string label = $"  {mob.name}  [{winner}] {awareStr}";
                if (GUILayout.Button(label, rowSt))
                {
                    // 단일 선택 → Hierarchy 동기화
                    Selection.activeGameObject = mob.gameObject;
                    _pinnedMob = mob;
                    Repaint();
                }
                GUI.backgroundColor = prev;

                // ─ AIPerceptionDebugger 토글
                var debugger = mob.GetComponent<AIPerceptionDebugger>();
                if (debugger != null)
                {
                    if (!_debuggerToggle.ContainsKey(mob))
                        _debuggerToggle[mob] = debugger.enabled;
                    bool cur = _debuggerToggle[mob];
                    bool next = GUILayout.Toggle(cur, "DBG",
                        new GUIStyle(GUI.skin.toggle) { fontSize = 10 }, GUILayout.Width(38));
                    if (next != cur)
                    {
                        _debuggerToggle[mob] = next;
                        debugger.enabled = next;
                        EditorUtility.SetDirty(debugger);
                    }
                }
                else
                {
                    GUI.enabled = false;
                    GUILayout.Toggle(false, "DBG",
                        new GUIStyle(GUI.skin.toggle) { fontSize = 10 }, GUILayout.Width(38));
                    GUI.enabled = true;
                }

                // ─ 드라이브 / SO 패널 펼침 버튼
                if (!_driveExpanded.ContainsKey(mob)) _driveExpanded[mob] = false;
                bool exp = _driveExpanded[mob];
                if (GUILayout.Button(exp ? "▲" : "▼",
                    new GUIStyle(GUI.skin.button) { fontSize = 10 }, GUILayout.Width(22)))
                    _driveExpanded[mob] = !exp;

                EditorGUILayout.EndHorizontal();

                // ─ 펼쳐진 패널: 생득욕구 + Utility + Faction/Combat SO
                if (_driveExpanded.GetValueOrDefault(mob, false))
                    DrawMobDetailPanel(mob);
            }

            EditorGUILayout.EndScrollView();
        }

        // ── 개별 몹 상세 패널 ────────────────────────────────────────────────
        private void DrawMobDetailPanel(AICharacterManager mob)
        {
            var brain = mob.GetComponent<MobAIBrain>();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (brain != null)
            {
                // ─ 생득 욕구 (Survival Drives) ─────────────────────────────
                EditorGUILayout.LabelField("생득 욕구", EditorStyles.boldLabel);
                Undo.RecordObject(brain, "Probe: Drive Edit");
                brain.fear = DrawDriveSlider("Fear", brain.fear);
                brain.hunger = DrawDriveSlider("Hunger", brain.hunger);
                brain.greed = DrawDriveSlider("Greed", brain.greed);
                brain.fatigue = DrawDriveSlider("Fatigue", brain.fatigue);
                EditorUtility.SetDirty(brain);

                // ─ Utility Scores (ReadOnly) ────────────────────────────────
                var flags = System.Reflection.BindingFlags.Instance
                          | System.Reflection.BindingFlags.Public
                          | System.Reflection.BindingFlags.NonPublic;
                float uAtk = ReadFloat(brain, "u_attack", flags);
                float uFle = ReadFloat(brain, "u_flee", flags);
                float uIdl = ReadFloat(brain, "u_idle", flags);
                float uPat = ReadFloat(brain, "u_patrol", flags);
                EditorGUILayout.LabelField("Utility Scores (ReadOnly)", EditorStyles.miniLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"Atk:{uAtk:F2}  Fle:{uFle:F2}  Idl:{uIdl:F2}  Pat:{uPat:F2}",
                                           EditorStyles.miniLabel);
                EditorGUI.indentLevel--;

                // ─ Utility Threshold 직접 편집 ──────────────────────────────
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Utility Thresholds", EditorStyles.boldLabel);
                var aggFI = brain.GetType().GetField("aggressionThreshold", flags)
                         ?? brain.GetType().GetField("AggressionThreshold", flags);
                var fleFI = brain.GetType().GetField("fleeThreshold", flags)
                         ?? brain.GetType().GetField("FleeThreshold", flags);
                if (aggFI != null)
                {
                    float cur = (float)aggFI.GetValue(brain);
                    float next = EditorGUILayout.Slider("Aggression", cur, 0f, 1f);
                    if (Mathf.Abs(next - cur) > 0.001f) { aggFI.SetValue(brain, next); EditorUtility.SetDirty(brain); }
                }
                if (fleFI != null)
                {
                    float cur = (float)fleFI.GetValue(brain);
                    float next = EditorGUILayout.Slider("Flee", cur, 0f, 1f);
                    if (Mathf.Abs(next - cur) > 0.001f) { fleFI.SetValue(brain, next); EditorUtility.SetDirty(brain); }
                }
            }

            // ─ Faction / Combat Profile SO 편집 ────────────────────────────
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Combat Profile SO", EditorStyles.boldLabel);
            DrawSOEditor(mob);

            EditorGUILayout.EndVertical();
        }

        private float DrawDriveSlider(string label, float val)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(60));
            float next = EditorGUILayout.Slider(val, 0f, 1f);
            EditorGUILayout.EndHorizontal();
            return next;
        }

        private static float ReadFloat(object obj, string fieldName,
            System.Reflection.BindingFlags flags)
        {
            try { var fi = obj.GetType().GetField(fieldName, flags); return fi != null ? (float)fi.GetValue(obj) : 0f; }
            catch { return 0f; }
        }

        // ─ Combat Profile SO 편집 ─────────────────────────────────────────
        private Dictionary<AICharacterManager, bool> _soExpanded = new();
        private void DrawSOEditor(AICharacterManager mob)
        {
            // PB4DecisionAdapter에서 CombatProfile SO 찾기
            object soObj = null;
            MonoBehaviour adapter = null;
            var flags = System.Reflection.BindingFlags.Instance
                      | System.Reflection.BindingFlags.Public
                      | System.Reflection.BindingFlags.NonPublic;

            foreach (var mb in mob.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                string tn = mb.GetType().Name;
                if (!tn.Contains("Decision") && !tn.Contains("Adapter") && !tn.Contains("PB4")) continue;
                adapter = mb;
                foreach (var fi in mb.GetType().GetFields(flags))
                {
                    string fn = fi.Name.ToLower();
                    if (fn.Contains("combat") && fn.Contains("profile"))
                    { soObj = fi.GetValue(mb); break; }
                }
                if (soObj != null) break;
            }

            if (soObj == null)
            {
                EditorGUILayout.HelpBox("CombatProfile SO를 찾지 못했습니다.", MessageType.None);
                return;
            }

            // SO 필드 편집
            bool isDirty = _dirtySOSet.Contains(soObj as UnityEngine.Object);
            string dirtyMark = isDirty ? " *" : "";
            EditorGUILayout.LabelField($"{soObj.GetType().Name}{dirtyMark}", EditorStyles.miniLabel);

            var soType = soObj.GetType();
            var soFields = soType.GetFields(System.Reflection.BindingFlags.Instance
                                           | System.Reflection.BindingFlags.Public);
            foreach (var fi in soFields)
            {
                if (fi.FieldType == typeof(float))
                {
                    float cur = (float)fi.GetValue(soObj);
                    // Range 어트리뷰트 확인
                    var range = fi.GetCustomAttribute<RangeAttribute>();
                    float min = range?.min ?? 0f, max = range?.max ?? 20f;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(fi.Name, GUILayout.Width(140));
                    float next = EditorGUILayout.Slider(cur, min, max);
                    EditorGUILayout.EndHorizontal();
                    if (Mathf.Abs(next - cur) > 0.001f)
                    {
                        fi.SetValue(soObj, next);
                        if (soObj is UnityEngine.Object uobj)
                        {
                            EditorUtility.SetDirty(uobj);
                            _dirtySOSet.Add(uobj);
                        }
                    }
                }
                else if (fi.FieldType == typeof(int))
                {
                    int cur = (int)fi.GetValue(soObj);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(fi.Name, GUILayout.Width(140));
                    int next = EditorGUILayout.IntField(cur);
                    EditorGUILayout.EndHorizontal();
                    if (next != cur)
                    {
                        fi.SetValue(soObj, next);
                        if (soObj is UnityEngine.Object uobj)
                        { EditorUtility.SetDirty(uobj); _dirtySOSet.Add(uobj); }
                    }
                }
                else if (fi.FieldType == typeof(bool))
                {
                    bool cur = (bool)fi.GetValue(soObj);
                    bool next = EditorGUILayout.Toggle(fi.Name, cur);
                    if (next != cur)
                    {
                        fi.SetValue(soObj, next);
                        if (soObj is UnityEngine.Object uobj)
                        { EditorUtility.SetDirty(uobj); _dirtySOSet.Add(uobj); }
                    }
                }
            }

            // Save 버튼
            if (_dirtySOSet.Count > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
                if (GUILayout.Button($"💾 SO 저장 ({_dirtySOSet.Count}개 변경)", GUILayout.Width(180)))
                {
                    foreach (var obj in _dirtySOSet)
                    {
                        if (obj == null) continue;
                        EditorUtility.SetDirty(obj);
                        AssetDatabase.SaveAssetIfDirty(obj);
                    }
                    _dirtySOSet.Clear();
                    AddLog("💾 SO 저장 완료");
                }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("↩ 되돌리기", GUILayout.Width(80)))
                {
                    // 디스크에서 에셋 재로드하여 런타임 수정 취소
                    foreach (var obj in _dirtySOSet)
                    {
                        if (obj == null) continue;
                        string path = AssetDatabase.GetAssetPath(obj);
                        if (!string.IsNullOrEmpty(path))
                            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                        EditorUtility.ClearDirty(obj);
                    }
                    _dirtySOSet.Clear();
                    AddLog("↩ SO 변경 취소 (디스크에서 재로드)");
                }
                EditorGUILayout.EndHorizontal();
            }
        }


        private void DrawProbeButtons()
        {
            EditorGUILayout.LabelField("프로브 (Scene View 마우스 위치에 발동)", EditorStyles.boldLabel);

            // ── 볼륨 슬라이더: 청각 프로브(isPerception=true) 선택 시에만 표시 ──────
            // 발자국(0), 전투소음(1)만 해당. BT강제 프로브는 볼륨 개념 없음.
            bool _curIsPerception = _selectedProbe >= 0 && _selectedProbe < PROBES.Length && PROBES[_selectedProbe].isPerception;
            if (_curIsPerception)
            {
                GUILayout.Space(4);
                var boxSt = new GUIStyle(GUI.skin.box);
                EditorGUILayout.BeginVertical(boxSt);
                EditorGUILayout.LabelField("청각 볼륨 설정", EditorStyles.miniLabel);
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label($"볼륨  {_probeVolume:F2}", GUILayout.Width(72));
                    _probeVolume = GUILayout.HorizontalSlider(_probeVolume, 0.05f, 1f);
                }
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Footstep(0.3)", GUILayout.Width(100))) _probeVolume = 0.3f;
                    if (GUILayout.Button("Combat(0.8)", GUILayout.Width(90))) _probeVolume = 0.8f;
                    if (GUILayout.Button("Max(1.0)", GUILayout.Width(75))) _probeVolume = 1.0f;
                }
                EditorGUILayout.EndVertical();
                GUILayout.Space(4);
            }

            for (int i = 0; i < PROBES.Length; i++)
            {
                var (icon, name, key, isPerception, tip) = PROBES[i];
                bool selected = _selectedProbe == i;

                Color bg = isPerception
                    ? (selected ? new Color(0.20f, 0.50f, 0.90f) : new Color(0.15f, 0.35f, 0.65f))
                    : (selected ? new Color(0.80f, 0.20f, 0.20f) : new Color(0.55f, 0.15f, 0.15f));

                var style = new GUIStyle(GUI.skin.button)
                {
                    fontStyle = selected ? FontStyle.Bold : FontStyle.Normal,
                    alignment = TextAnchor.MiddleLeft,
                    fixedHeight = 24,
                };

                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = bg;
                string label = $"  {icon} {name}   {key}   {(isPerception ? "[인지경유]" : "[BT강제]")}";
                if (GUILayout.Button(new GUIContent(label, tip), style))
                {
                    if (selected)
                    {
                        // 이미 선택된 버튼 재클릭 → 선택 해제 (토글)
                        _selectedProbe = -1;
                        AddLog($"🔕 [{icon} {name}] 선택 해제. 씬뷰 오브젝트 조작 가능.");
                        SceneView.RepaintAll();
                    }
                    else
                    {
                        _selectedProbe = i;
                        // [인지경유] 프로브: 씬뷰 마우스 위치 필요
                        // [BT강제]   프로브: 마우스 위치 불필요 — 대상 AI에 직접 적용
                        if (isPerception)
                        {
                            if (_mouseHitValid)
                                FireProbe(i, _mouseWorldPos);
                            else
                                AddLog($"⚠️ '{name}': Scene View 위에 마우스를 올린 후 클릭하세요.");
                        }
                        else
                        {
                            // BT강제: 대상 AI 위치 사용 (마우스 위치 무관)
                            var _btnTargets = GetTargetAIs();
                            Vector3 _btnPos = _btnTargets.Count > 0
                                ? _btnTargets[0].transform.position
                                : _mouseWorldPos;
                            FireProbe(i, _btnPos);
                        }
                    }
                }
                GUI.backgroundColor = prev;
            }
        }

        private void DrawFearSlider()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("공포 증폭량", GUILayout.Width(80));
            _fearDelta = EditorGUILayout.Slider(_fearDelta, 0f, 1f);
            EditorGUILayout.EndHorizontal();
        }

        // ── [B10 Fix] 실시간 상태 — Repaint는 EditorUpdate 타이머에서 ──
        private void DrawRealtimeStatus()
        {
            var targets = GetTargetAIs();
            if (targets.Count == 0) return;

            var ai = targets[0];   // 다수 선택 시 첫 번째만 상태 표시
            EditorGUILayout.LabelField($"실시간 상태 — {ai.name}", EditorStyles.boldLabel);

            var brain = ai.GetComponent<MobAIBrain>();
            var perception = ai.GetComponent<AIPerceptionSystem>();
            var btAgent = ai.GetComponent<BehaviorGraphAgent>();

            if (perception != null)
                EditorGUILayout.LabelField($"  AwarenessState : {perception.CurrentAwareness}");
            if (btAgent?.BlackboardReference != null)
            {
                btAgent.BlackboardReference.GetVariable("UtilityWinner", out BlackboardVariable<string> wv);
                EditorGUILayout.LabelField($"  UtilityWinner  : {wv?.Value ?? "---"}");
            }
            if (brain != null)
                EditorGUILayout.LabelField($"  Fear           : {brain.fear:F3}");
            // Repaint()는 여기서 호출하지 않음 — OnEditorUpdate 타이머가 담당
        }

        // ── 이벤트 로그 ──
        private void DrawEventLog()
        {
            EditorGUILayout.LabelField($"이벤트 로그 (최근 {MAX_LOG}개)", EditorStyles.boldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(88));
            foreach (var log in _eventLog)
                EditorGUILayout.LabelField(log, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        // =====================================================================
        // Scene View Gizmo + 단축키
        // =====================================================================

        private void OnSceneGUI(SceneView sv)
        {
            // 마우스 → 월드 좌표
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            _mouseHitValid = Physics.Raycast(ray, out RaycastHit hit, 500f);
            if (_mouseHitValid) _mouseWorldPos = hit.point;

            // 커서 Gizmo — 플레이 중 + 프로브 선택된 경우에만 표시
            if (_mouseHitValid
                && _selectedProbe >= 0
                && UnityEngine.Application.isPlaying)
            {
                Handles.color = new Color(0.3f, 1f, 0.9f, 0.8f);
                Handles.DrawWireDisc(_mouseWorldPos, Vector3.up, 0.25f);
                Handles.Label(_mouseWorldPos + Vector3.up * 0.3f,
                    $"← [{PROBES[_selectedProbe].icon} {PROBES[_selectedProbe].name}]  [ESC: 해제]");
            }

            // ── ESC: 프로브 선택 해제 ─────────────────────────────────────────────
            // 선택된 프로브가 있을 때 ESC를 누르면 -1(비선택)로 복귀.
            // 비선택 상태에서는 ev.Use()를 호출하지 않으므로 씬뷰 오브젝트 이동 정상 동작.
            if (Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Escape
                && _selectedProbe >= 0)
            {
                _selectedProbe = -1;
                Event.current.Use();
                sv.Repaint();
                AddLog("🔕 프로브 선택 해제 (ESC). 씬뷰 오브젝트 조작 가능.");
            }

            // 단축키 감지 — [인지경유] 프로브만 _mouseHitValid 필요
            // [BT강제] 프로브는 마우스 위치 불필요이므로 조건 분리
            bool _isPerceptionProbe = _selectedProbe >= 0
                                   && _selectedProbe < PROBES.Length
                                   && PROBES[_selectedProbe].isPerception;
            bool _keyTriggered = Event.current.type == EventType.KeyDown && Event.current.alt
                                 && (_mouseHitValid || !_isPerceptionProbe);
            if (_keyTriggered)
            {
                int idx = Event.current.keyCode switch
                {
                    KeyCode.Alpha1 => 0,
                    KeyCode.Alpha2 => 1,
                    KeyCode.Alpha3 => 2,
                    KeyCode.Alpha4 => 3,
                    KeyCode.Alpha5 => 4,
                    KeyCode.Alpha6 => 5,
                    KeyCode.Alpha7 => 6,
                    KeyCode.Alpha8 => 7,
                    KeyCode.Alpha9 => 8,
                    _ => -1
                };
                if (idx >= 0)
                {
                    _selectedProbe = idx;
                    FireProbe(idx, _mouseWorldPos);
                    Event.current.Use();
                    sv.Repaint();
                }
            }

            // ── 씬뷰 좌클릭으로 현재 선택된 프로브 발사 ─────────────────────────
            // 조건:
            //   · MouseDown 이벤트 (한 번만 발사, Drag 아님)
            //   · 좌클릭 (button == 0)
            //   · Ctrl 없음 (Ctrl+클릭은 NarrowZone 드래그용으로 예약)
            //   · [인지경유] 프로브: _mouseHitValid 필요 (발자국/전투소음 등 위치 필수)
            //   · [BT강제] 프로브: 위치 불필요, 클릭만으로 발사 가능
            //   · HandleUtility.nearestControl == 0: 씬 뷰 오브젝트 선택과 충돌 방지
            //     (다른 Handle이 활성화된 상태면 무시)
            var ev = Event.current;
            // ── 씬뷰 좌클릭으로 현재 선택된 프로브 발사 ─────────────────────────
            // _selectedProbe == -1(비선택) 이면 ev.Use() 절대 호출 안함
            // → 씬뷰 오브젝트 선택/이동 정상 동작
            bool isLeftClick = ev.type == EventType.MouseDown
                            && ev.button == 0
                            && !ev.control
                            && !ev.alt
                            && _selectedProbe >= 0                          // 비선택이면 스킵
                            && UnityEngine.Application.isPlaying;           // 플레이 중에만

            if (isLeftClick)
            {
                bool isPerception = PROBES[_selectedProbe].isPerception;

                if (!isPerception)
                {
                    // [BT강제] 프로브: 위치 관계없이 즉시 발사
                    FireProbe(_selectedProbe, _mouseWorldPos);
                    ev.Use();
                    sv.Repaint();
                }
                else if (_mouseHitValid)
                {
                    // [인지경유] 프로브: 지형 히트 확인 후 발사
                    FireProbe(_selectedProbe, _mouseWorldPos);
                    ev.Use();
                    sv.Repaint();
                }
                // _mouseHitValid == false → ev.Use() 안함 → 오브젝트 선택 유지
            }

            DrawPersistentGizmos();
            DrawNarrowZoneGizmo(sv);
            HandleNarrowZoneDrag(sv);
            UpdateNarrowZone();
        }

        // ── NarrowPath Zone Gizmo 렌더링 ─────────────────────────────────────
        private void DrawNarrowZoneGizmo(SceneView sv)
        {
            if (!_narrowZoneActive) return;

            var half = new Vector3(_narrowZoneWidth * 0.5f,
                                   _narrowZoneHeight * 0.5f,
                                   _narrowZoneLength * 0.5f);
            Vector3 c = _narrowZoneCenter;

            // 외곽선 박스
            Handles.color = new Color(0.85f, 0.55f, 0.10f, 0.90f);
            Handles.DrawWireCube(c + Vector3.up * half.y, new Vector3(_narrowZoneWidth, _narrowZoneHeight, _narrowZoneLength));

            // 반투명 면 채우기 (앞/뒤 벽)
            Handles.color = new Color(0.85f, 0.55f, 0.10f, 0.12f);
            Vector3[] front = {
                c + new Vector3(-half.x, 0,        half.z),
                c + new Vector3( half.x, 0,        half.z),
                c + new Vector3( half.x, half.y*2, half.z),
                c + new Vector3(-half.x, half.y*2, half.z),
            };
            Vector3[] back = {
                c + new Vector3(-half.x, 0,        -half.z),
                c + new Vector3( half.x, 0,        -half.z),
                c + new Vector3( half.x, half.y*2, -half.z),
                c + new Vector3(-half.x, half.y*2, -half.z),
            };
            Handles.DrawSolidRectangleWithOutline(front,
                new Color(0.85f, 0.55f, 0.10f, 0.10f),
                new Color(0.85f, 0.55f, 0.10f, 0.80f));
            Handles.DrawSolidRectangleWithOutline(back,
                new Color(0.85f, 0.55f, 0.10f, 0.10f),
                new Color(0.85f, 0.55f, 0.10f, 0.80f));

            // 레이블
            Handles.color = Color.white;
            Handles.Label(c + Vector3.up * (_narrowZoneHeight + 0.3f),
                $"NarrowPath Zone  W:{_narrowZoneWidth:F1}m  L:{_narrowZoneLength:F1}m  H:{_narrowZoneHeight:F1}m  Auto:{(_narrowZoneAutoUpdate ? "ON" : "OFF")}",
                new GUIStyle(GUI.skin.label) { richText = false, fontSize = 11, fontStyle = FontStyle.Bold });

            // 중심 이동 핸들
            EditorGUI.BeginChangeCheck();
            Vector3 newCenter = Handles.PositionHandle(_narrowZoneCenter, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
                _narrowZoneCenter = new Vector3(newCenter.x, _narrowZoneCenter.y, newCenter.z);

            // 폭/길이 핸들
            Handles.color = new Color(0.85f, 0.55f, 0.10f, 0.80f);
            float newW = Handles.ScaleValueHandle(_narrowZoneWidth,
                c + Vector3.right * half.x, Quaternion.identity, 0.5f, Handles.CubeHandleCap, 0.1f);
            float newL = Handles.ScaleValueHandle(_narrowZoneLength,
                c + Vector3.forward * half.z, Quaternion.identity, 0.5f, Handles.CubeHandleCap, 0.1f);
            _narrowZoneWidth = Mathf.Max(newW, 0.5f);
            _narrowZoneLength = Mathf.Max(newL, 0.5f);
        }

        // ── NarrowPath Zone Ctrl+클릭으로 빠른 배치 ────────────────────────────
        private void HandleNarrowZoneDrag(SceneView sv)
        {
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 0 && ev.control && _mouseHitValid)
            {
                _narrowZoneCenter = _mouseWorldPos;
                _narrowZoneActive = true;
                AddLog($"🏔️ NarrowPath Zone 배치: {_mouseWorldPos:F1}  W={_narrowZoneWidth:F1}m L={_narrowZoneLength:F1}m");
                ev.Use();
                sv.Repaint();
            }
        }

        // [B8 Fix] List + reverse-remove — 매 프레임 new Queue 생성 제거
        private void DrawPersistentGizmos()
        {
            float now = (float)EditorApplication.timeSinceStartup;

            for (int i = _gizmoList.Count - 1; i >= 0; i--)
            {
                var g = _gizmoList[i];
                if (g.expireTime < now)
                {
                    _gizmoList.RemoveAt(i);
                    continue;
                }

                float alpha = Mathf.Clamp01((g.expireTime - now) / GIZMO_DURATION);
                Handles.color = new Color(g.color.r, g.color.g, g.color.b, g.color.a * alpha);

                if (g.radius > 0f)
                    Handles.DrawWireDisc(g.position, Vector3.up, g.radius);
                else
                    Handles.DrawSolidDisc(g.position, Vector3.up, 0.2f);

                Handles.Label(g.position + Vector3.up * 0.4f, g.label);
            }
        }

        // =====================================================================
        // 프로브 발동
        // =====================================================================

        private void FireProbe(int idx, Vector3 pos)
        {
            // [E3 Fix] 다수 AI 지원 — 일부 프로브는 전체 선택에 적용
            var targets = GetTargetAIs();
            var primary = targets.Count > 0 ? targets[0] : null;

            switch (idx)
            {
                case 0: FireFootstep(pos); break;
                case 1: FireCombatNoise(pos); break;
                case 2: FireFearBoost(targets); break;  // 전체 선택에 적용
                case 3: FireFearReset(targets); break;  // 전체 선택에 적용
                case 4: FireMemoryInject(pos, primary); break;
                case 5: FirePanicPropagate(pos); break;
                case 6: FireForceCombat(primary); break;
                case 7: FireNarrowPathInject(targets); break;
                case 8: FireNarrowPathClear(targets); break;
            }
            SceneView.RepaintAll();
        }

        // ── [인지경유] 발자국 소리 ──
        private void FireFootstep(Vector3 pos)
        {
            // SoundPerception.HandleSoundEmitted의 Footstep baseRadius = 15f
            // → OnSoundEvent(pos, 0.3f × 15f = 4.5m) 전달
            // Gizmo는 AI hearingRange 기준 (별개 개념)
            SoundEventEmitter.EmitSound(pos, SoundType.Footstep, _probeVolume);
            EnqueueGizmo(pos, 15f, new Color(0.4f, 0.8f, 1f, 0.7f),
                "🔊 Footstep vol=0.3\n(baseRadius=15m)");
            AddLog($"🔊 발자국 at {pos:F1}");
        }

        // ── [인지경유] 전투 소음 ──
        private void FireCombatNoise(Vector3 pos)
        {
            // Combat baseRadius = 40f → OnSoundEvent sourceRadius = 0.8 × 40 = 32m
            SoundEventEmitter.EmitSound(pos, SoundType.Combat, _probeVolume);
            EnqueueGizmo(pos, 40f, new Color(1f, 0.3f, 0.3f, 0.6f),
                "💥 Combat vol=0.8\n(baseRadius=40m)");
            AddLog($"💥 전투소음 at {pos:F1}");
        }

        // ── [BT강제] 공포 증폭 — [E3] 다수 AI 지원 + [E5] Undo ──
        private void FireFearBoost(List<AICharacterManager> targets)
        {
            if (targets == null || targets.Count == 0)
            {
                AddLog("⚠️ FireFearBoost: 대상 AI 없음. 대상 AI를 선택하세요.");
                return;
            }
            foreach (var ai in targets)
            {
                if (ai == null) { AddLog("⚠️ AI null 스킵"); continue; }
                var brain = ai.GetComponent<MobAIBrain>();
                if (brain == null)
                {
                    AddLog($"⚠️ {ai.name}: MobAIBrain 없음");
                    continue;
                }

                float before = brain.fear;
                brain.fear = Mathf.Clamp01(brain.fear + _fearDelta);

                // Play Mode에서 BT 재평가 강제 (fear 변경만으로는 BT가 즉시 반응 안 함)
                try
                {
                    var flags = System.Reflection.BindingFlags.Instance
                              | System.Reflection.BindingFlags.Public
                              | System.Reflection.BindingFlags.NonPublic;
                    var mi = brain.GetType().GetMethod("UpdateDecision", flags)
                          ?? brain.GetType().GetMethod("ForceDecisionUpdate", flags)
                          ?? brain.GetType().GetMethod("EvaluateUtility", flags);
                    if (mi != null)
                    {
                        mi.Invoke(brain, null);
                        AddLog($"😱 공포증폭 {ai.name}: {before:F2}→{brain.fear:F2} (+UpdateDecision)");
                    }
                    else
                    {
                        AddLog($"😱 공포증폭 {ai.name}: {before:F2}→{brain.fear:F2} (UpdateDecision 미발견, fear만 반영)");
                    }
                }
                catch (System.Exception ex)
                {
                    AddLog($"⚠️ UpdateDecision 오류: {ex.Message}");
                }

                EnqueueGizmo(ai.transform.position, 0f, new Color(1f, 0.5f, 0.1f, 0.9f),
                    $"😱 Fear {before:F2}→{brain.fear:F2}");
            }
        }

        // ── [BT강제] 공포+각성 리셋 — [E3] 다수 AI 지원 ──
        private void FireFearReset(List<AICharacterManager> targets)
        {
            foreach (var ai in targets)
            {
                var brain = ai.GetComponent<MobAIBrain>();
                var perception = ai.GetComponent<AIPerceptionSystem>();
                var btAgent = ai.GetComponent<BehaviorGraphAgent>();

                if (brain != null)
                {
                    Undo.RecordObject(brain, "Probe: Fear Reset");
                    brain.fear = 0f;
                    EditorUtility.SetDirty(brain);
                }
                perception?.ForceSetAwareness(AwarenessState.Unaware, "ProbeReset");
                btAgent?.SetVariableValue("LastHeardPosition", Vector3.zero);
                btAgent?.SetVariableValue("LastSeenPosition", Vector3.zero);

                EnqueueGizmo(ai.transform.position, 0f, new Color(0.3f, 1f, 0.4f, 0.9f),
                    "🧊 RESET  fear=0\nUnaware + 기억초기화");
                AddLog($"🧊 리셋 {ai.name}");
            }
        }

        // ── [BT강제] 메모리 주입 ──
        private void FireMemoryInject(Vector3 pos, AICharacterManager ai)
        {
            if (ai == null) return;
            var btAgent = ai.GetComponent<BehaviorGraphAgent>();
            btAgent?.SetVariableValue("LastSeenPosition", pos);

            float d = Vector3.Distance(ai.transform.position, pos);
            EnqueueGizmo(pos, 0f, new Color(0.7f, 0.3f, 1f, 0.9f),
                $"💭 LastSeen  ({d:F1}m)");
            AddLog($"💭 메모리주입 {ai.name}: {pos:F1}");
        }

        // ── [BT강제/효과직접] 패닉 전파 ──
        // [B4 Fix] HandlePanicChain() 경유 불가 (FleeSwarmAction.OnStart() private)
        //          → brain.fear 직접 수정. 레이블을 [BT강제]로 교정.
        private void FirePanicPropagate(Vector3 pos)
        {
            const float radius = 5f;
            int count = 0;
            foreach (var col in Physics.OverlapSphere(pos, radius))
            {
                var brain = col.GetComponent<MobAIBrain>();
                if (brain == null) continue;

                Undo.RecordObject(brain, "Probe: Panic Propagate");
                brain.fear = Mathf.Clamp01(brain.fear + 0.3f);
                EditorUtility.SetDirty(brain);
                count++;
            }
            EnqueueGizmo(pos, radius, new Color(1f, 0.6f, 0.1f, 0.6f),
                $"🌐 Panic +0.3  ({count}마리)\n[BT강제/효과직접]");
            AddLog($"🌐 패닉전파 at {pos:F1} → {count}마리");
        }

        // ── [BT강제] 강제 전투 진입 — [E4] Player 캐시 ──
        private void FireForceCombat(AICharacterManager ai)
        {
            if (ai == null) return;

            // [E4 Fix] Player 캐시
            if (_cachedPlayer == null || !_cachedPlayer.activeInHierarchy)
                _cachedPlayer = GameObject.FindGameObjectWithTag("Player");

            var perception = ai.GetComponent<AIPerceptionSystem>();
            var btAgent = ai.GetComponent<BehaviorGraphAgent>();

            perception?.ForceSetAwareness(AwarenessState.Combat, "ProbeForceCombat");

            if (_cachedPlayer != null)
            {
                btAgent?.SetVariableValue("HasTarget", true);
                btAgent?.SetVariableValue("UtilityWinner", "Attack");
                btAgent?.SetVariableValue("Target", _cachedPlayer.transform);
            }

            EnqueueGizmo(ai.transform.position, 0f, new Color(1f, 0.2f, 0.2f, 0.9f),
                "🎯 [COMBAT]  강제 진입");
            AddLog($"🎯 강제전투 {ai.name}"
                   + (_cachedPlayer != null ? $" → {_cachedPlayer.name}" : " (Player 없음)"));
        }

        // ── [BT강제] NarrowPath 태그 주입 ──────────────────────────────────────
        private void FireNarrowPathInject(List<AICharacterManager> targets)
        {
            if (targets.Count == 0)
            {
                AddLog("⚠️ FireNarrowPathInject: 대상 AI를 선택하세요.");
                return;
            }
            foreach (var ai in targets)
            {
                var btAgent = ai.GetComponent<BehaviorGraphAgent>();
                if (btAgent?.BlackboardReference == null) continue;

                // TerrainTags BB 변수에 "NarrowPath" 설정
                try
                {
                    btAgent.BlackboardReference.GetVariable("TerrainTags",
                        out BlackboardVariable<string> tagVar);
                    if (tagVar != null)
                    {
                        string cur = tagVar.Value ?? "";
                        if (!cur.Contains("NarrowPath"))
                            tagVar.Value = string.IsNullOrEmpty(cur)
                                ? "NarrowPath"
                                : cur + ",NarrowPath";
                    }
                    else
                    {
                        btAgent.SetVariableValue("TerrainTags", "NarrowPath");
                    }
                }
                catch
                {
                    btAgent.SetVariableValue("TerrainTags", "NarrowPath");
                }

                EnqueueGizmo(ai.transform.position, 1.5f,
                    new Color(0.85f, 0.55f, 0.10f, 0.80f),
                    "🏔️ [NARROW] CircleStrafe×0.4 Strike×0.5");
                AddLog($"🏔️ NarrowPath 주입 → {ai.name}  (반경×0.4 / Strike×0.5)");
            }
        }

        // ── [BT강제] NarrowPath 태그 해제 ──────────────────────────────────────
        private void FireNarrowPathClear(List<AICharacterManager> targets)
        {
            if (targets.Count == 0)
            {
                AddLog("⚠️ FireNarrowPathClear: 대상 AI를 선택하세요.");
                return;
            }
            foreach (var ai in targets)
            {
                var btAgent = ai.GetComponent<BehaviorGraphAgent>();
                if (btAgent?.BlackboardReference == null) continue;

                try
                {
                    btAgent.BlackboardReference.GetVariable("TerrainTags",
                        out BlackboardVariable<string> tagVar);
                    if (tagVar != null)
                    {
                        string cleared = (tagVar.Value ?? "")
                            .Replace("NarrowPath", "")
                            .Trim(',').Trim();
                        tagVar.Value = cleared;
                    }
                    else
                    {
                        btAgent.SetVariableValue("TerrainTags", "");
                    }
                }
                catch
                {
                    btAgent.SetVariableValue("TerrainTags", "");
                }

                EnqueueGizmo(ai.transform.position, 0f,
                    new Color(0.28f, 1f, 0.55f, 0.80f), "🔓 [NARROW CLEAR]");
                AddLog($"🔓 NarrowPath 해제 → {ai.name}");
            }
        }

        // ── NarrowPath Zone 자동 업데이트 ────────────────────────────────────
        // Zone이 활성화된 경우 매 SceneView 갱신마다 호출
        // Zone 안에 있는 AI는 자동으로 NarrowPath 주입, 밖이면 해제
        private void UpdateNarrowZone()
        {
            if (!_narrowZoneActive || !Application.isPlaying) return;
            if (!_narrowZoneAutoUpdate) return;

            // Zone 방향: Z축 기준 Box
            var half = new Vector3(_narrowZoneWidth * 0.5f,
                                   _narrowZoneHeight * 0.5f,
                                   _narrowZoneLength * 0.5f);

            foreach (var ai in GameObject.FindObjectsByType<AICharacterManager>(
                         FindObjectsSortMode.None))
            {
                Vector3 local = ai.transform.position - _narrowZoneCenter;
                bool inside = Mathf.Abs(local.x) <= half.x
                           && Mathf.Abs(local.y) <= half.y
                           && Mathf.Abs(local.z) <= half.z;

                var btAgent = ai.GetComponent<BehaviorGraphAgent>();
                if (btAgent?.BlackboardReference == null) continue;

                try
                {
                    btAgent.BlackboardReference.GetVariable("TerrainTags",
                        out BlackboardVariable<string> tagVar);
                    if (tagVar == null) continue;

                    string cur = tagVar.Value ?? "";
                    bool hasTag = cur.Contains("NarrowPath");

                    if (inside && !hasTag)
                    {
                        tagVar.Value = string.IsNullOrEmpty(cur) ? "NarrowPath" : cur + ",NarrowPath";
                        AddLog($"🏔️ Zone 진입 → {ai.name}");
                    }
                    else if (!inside && hasTag)
                    {
                        tagVar.Value = cur.Replace("NarrowPath", "").Trim(',').Trim();
                        AddLog($"🔓 Zone 이탈 → {ai.name}");
                    }
                }
                catch { }
            }
        }

        // =====================================================================
        // 유틸
        // =====================================================================

        // [E3 Fix] Selection.objects 다수 선택 지원
        private static List<AICharacterManager> GetTargetAIs()
        {
            var result = new List<AICharacterManager>();
            foreach (var obj in Selection.objects)
            {
                if (obj is GameObject go)
                {
                    var ai = go.GetComponent<AICharacterManager>();
                    if (ai != null) result.Add(ai);
                }
            }
            return result;
        }

        private void EnqueueGizmo(Vector3 pos, float radius, Color color, string label)
        {
            _gizmoList.Add(new ProbeGizmo
            {
                position = pos,
                radius = radius,
                color = color,
                label = label,
                expireTime = (float)EditorApplication.timeSinceStartup + GIZMO_DURATION,
            });
        }

        private void AddLog(string msg)
        {
            _eventLog.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
            if (_eventLog.Count > MAX_LOG)
                _eventLog.RemoveAt(_eventLog.Count - 1);
        }
    }
}
#endif