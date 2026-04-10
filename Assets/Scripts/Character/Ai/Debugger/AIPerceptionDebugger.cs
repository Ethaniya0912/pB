// =============================================================================
// AIPerceptionDebugger.cs  |  pB-4 AI 인지 시각화 디버거 v4.0
//
// [v3.0 전체 보존 + v4.0 신규 기능 추가]
//
// ■ v3.0 유지
//   ① GL 렌더링 (OnRenderObject) — Game View 링/호/선
//   ② 퓨처리스틱 패널 — 다크 반투명 + 9-slice 라운드 + 좌측정렬 Rich Text
//   ③ BB 14변수 / 타겟·그룹 연결선 / EngageRange 링
//   ④ 비선택 AI 시야/청각 윤곽 (OnDrawGizmos)
//   ⑤ Scene View 패널 (HandleUtility.WorldToGUIPoint)
//
// ■ v4.0 신규 (AIDebugger Feature Design Report v2.0 기반)
//   F-1: BTGraphRenderer — Unity Behavior 1.0.15 소스코드 분석 기반 자동 파싱
//        · Reflection: BehaviorGraph.Graphs (1필드) + NodeDescriptionAttribute.Name
//        · 공개 API: BehaviorGraphModule.Root, Composite.Children, Node.CurrentStatus
//        · 텍스트 트리(Game View) + Bezier 박스(Scene View)
//   F-2: AIDebuggerSettings SO 연동 — 거리 임계값 / 표시항목 / 겹침 전략
//   F-3: CharacterPanelRenderer — HP/STA/NavMesh/Animator 전역 상태
//   OVL: OverlapResolver — Grid Cell O(n) 다중 AI 패널 겹침 자동 회피
//
// ■ 단축키 (AIDebugKeys.cs 단일 관리)
//   F1             = 디버그 모드 순환 (Minimum → Standard → Focus → Off)
//   F2             = AI 핀 고정 / 해제  📌
//   F3             = GL 3D 오버레이 ON/OFF
//   Ctrl+Shift+B   = BT 그래프 크기 순환 (전체→축소→숨김)
//   Ctrl+Shift+C   = 캐릭터 패널 ON/OFF
//   Ctrl+Shift+L   = LOD 설정 창 열기
//
// ■ 배치
//   Assets/Scripts/Character/Ai/Debugger/AIPerceptionDebugger.cs
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using Unity.Behavior;
using TDA.Character.AI;
using TDA.Character;
using TDA.PB4.AI.Mob;
using TDA.PB4.AI.Perception;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TDA.PB4.Diagnostics
{
    /// <summary>
    /// pB-4 AI 인지·행동 실시간 디버깅 컴포넌트.
    /// Play Mode 진입 시 씬의 모든 AICharacterManager에 자동 부착된다.
    /// </summary>
    [AddComponentMenu("")]  // Inspector Add Component 목록에서 숨김 (자동 부착 전용)
    public class AIPerceptionDebugger : MonoBehaviour
    {
        // =====================================================================
        //  자동 부착 — Editor Play Mode 훅 (빌드 미포함)
        //  RuntimeInitializeOnLoadMethod는 #if UNITY_EDITOR 적용 불가이므로
        //  EditorApplication.playModeStateChanged를 사용한다.
        // =====================================================================

        /// <summary>그룹 AI 정보 패널을 Game View에 렌더링한다. (Fix #10)</summary>
        private void DrawGroupPanel(Vector2 topLeft, GUIStyle textStyle, GUIStyle panelStyle)
        {
            if (!_stylesReady || _stylePanel == null) return;
            var members = GetGroupMembers();
            if (members.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<color=#28B8FF><b>GROUP  {members.Count + 1} members</b></color>");

            foreach (var m in members)
            {
                if (m == null) continue;
                float dist = Vector3.Distance(transform.position, m.transform.position);
                // 멤버의 BB 값 읽기 시도
                var mBrain = m.GetComponent<MobAIBrain>();
                float mFear = mBrain != null ? mBrain.fear : 0f;
                var mDebugger = m.GetComponent<AIPerceptionDebugger>();
                string mWinner = mDebugger != null ? mDebugger.GetWinner() : "---";

                string wHex2 = mWinner switch
                {
                    "Attack" => H_DANGER,
                    "Flee" => H_OK,
                    "Patrol" => H_BLUE,
                    _ => H_NEUT
                };
                int fearFill = Mathf.Clamp(Mathf.RoundToInt(mFear * 8f), 0, 8);
                sb.AppendLine($"<color={H_NEUT}>{m.name[..Mathf.Min(10, m.name.Length)]}</color>" +
                              $" <color={wHex2}>{mWinner[..Mathf.Min(3, mWinner.Length)]}</color>" +
                              $" <color={H_VALUE}>{dist:F1}m</color>" +
                              $" <color=#FF4545>{new string('█', fearFill)}</color>");
            }

            string content = sb.ToString().TrimEnd();
            var ts = new GUIStyle(textStyle) { fontSize = 9, richText = true };
            Vector2 sz = ts.CalcSize(new GUIContent(content));
            float pw = Mathf.Max(sz.x + 20f, 130f), ph = sz.y + 14f;

            float gpTitleH = 18f;
            float totalH = ph + gpTitleH;
            Rect gpPanel = new Rect(topLeft.x, topLeft.y, pw, totalH);
            GUI.Box(gpPanel, GUIContent.none, panelStyle);
            // 타이틀 바
            Rect gpTitle = new Rect(gpPanel.x, gpPanel.y, pw, gpTitleH);
            Color gpPrev = GUI.color;
            GUI.color = new Color(0.10f, 0.45f, 0.55f, 0.85f);
            GUI.DrawTexture(gpTitle, Texture2D.whiteTexture);
            GUI.color = gpPrev;
            var gpTst = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            gpTst.normal.textColor = Color.white;
            GUI.Label(new Rect(gpTitle.x + 6, gpTitle.y, pw - 6, gpTitleH), "Group AI", gpTst);
            // 드래그
            HandleDrag(gpTitle, ref _groupDragging, ref _groupDragStart, ref _groupDragOff);
            GUI.Label(new Rect(topLeft.x + 8f, topLeft.y + gpTitleH + 4f, pw - 16f, ph - 8f), content, ts);
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void RegisterPlayHook()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                    AttachToAllAI();
            };
        }

        /// <summary>씬 내 모든 AICharacterManager에 이 컴포넌트를 부착한다.</summary>
        private static void AttachToAllAI()
        {
            foreach (var ai in FindObjectsByType<AICharacterManager>(FindObjectsSortMode.None))
                if (ai.GetComponent<AIPerceptionDebugger>() == null)
                    ai.gameObject.AddComponent<AIPerceptionDebugger>();
        }
#endif

        // =====================================================================
        //  F1 디버그 모드 — 4단계 순환
        // =====================================================================

        /// <summary>디버그 표시 밀도 모드. F1을 누를 때마다 순환한다.</summary>
        public enum DebugMode
        {
            /// <summary>Minimum: Winner 색상 아이콘 원만 표시. 겹침 없음.</summary>
            Minimum,
            /// <summary>Standard: LOD 기반 패널 표시 (기본).</summary>
            Standard,
            /// <summary>Focus: 핀된 AI만 전체 패널. 나머지 숨김.</summary>
            Focus,
            /// <summary>Off: 모든 오버레이 숨김.</summary>
            Off
        }

        // ── static 전역 상태 (모든 인스턴스 공유) ────────────────────────────

        /// <summary>[F1] 현재 디버그 모드.</summary>
        private static DebugMode _debugMode = DebugMode.Standard;

        /// <summary>[F3] GL 3D 오버레이(링·호·선) ON/OFF.</summary>
        private static bool _glOverlay = true;

        /// <summary>[F2] 핀 고정된 AI GameObject. null이면 핀 없음.</summary>
        private static GameObject _pinnedAI = null;

        /// <summary>[Ctrl+Shift+B] BT 그래프 패널 크기. 0=전체, 1=축소, 2=숨김.</summary>
        private static int _btGraphSize = 0;

        /// <summary>[Ctrl+Shift+C] 캐릭터 상태 패널 ON/OFF.</summary>
        private static bool _charPanelVisible = false;

        // =====================================================================
        //  LOD 거리 임계값 (AIDebuggerSettings SO 우선, 없으면 기본값 사용)
        // =====================================================================

        private const float LOD_ICON_DEFAULT = 40f;
        private const float LOD_HEADER_DEFAULT = 25f;
        private const float LOD_SUMMARY_DEFAULT = 14f;
        private const float LOD_FULL_DEFAULT = 8f;

        private float LodIcon => AIDebuggerSettings.Instance?.lodIconDist ?? LOD_ICON_DEFAULT;
        private float LodHeader => AIDebuggerSettings.Instance?.lodHeaderDist ?? LOD_HEADER_DEFAULT;
        private float LodSummary => AIDebuggerSettings.Instance?.lodSummaryDist ?? LOD_SUMMARY_DEFAULT;
        private float LodFull => AIDebuggerSettings.Instance?.lodFullDist ?? LOD_FULL_DEFAULT;

        // =====================================================================
        //  색상 팔레트 — 퓨처리스틱 다크 테마
        // =====================================================================

        // 패널
        private static readonly Color COL_BG = new Color(0.07f, 0.08f, 0.11f, 0.90f);
        private static readonly Color COL_BORDER = new Color(0.28f, 0.76f, 1.00f, 0.65f);

        // GL 3D 오버레이
        private static readonly Color COL_VISION = new Color(1.00f, 1.00f, 0.45f, 0.12f);
        private static readonly Color COL_VISION_BD = new Color(1.00f, 1.00f, 0.45f, 0.55f);
        private static readonly Color COL_HEARING = new Color(0.28f, 0.75f, 1.00f, 0.22f);
        private static readonly Color COL_HEARING_BD = new Color(0.28f, 0.75f, 1.00f, 0.55f);
        private static readonly Color COL_ENGAGE = new Color(1.00f, 0.32f, 0.32f, 0.35f);
        private static readonly Color COL_TARGET = new Color(1.00f, 0.18f, 0.18f, 0.80f);
        private static readonly Color COL_GROUP = new Color(0.18f, 0.78f, 1.00f, 0.42f);
        private static readonly Color COL_NAV = new Color(0.18f, 0.95f, 0.72f, 0.75f);
        private static readonly Color COL_MEMORY_S = new Color(0.80f, 0.35f, 1.00f, 0.80f);
        private static readonly Color COL_MEMORY_H = new Color(0.35f, 0.60f, 1.00f, 0.80f);

        // Rich Text 헥스 상수
        private const string H_LABEL = "#6EC8FF";
        private const string H_VALUE = "#F0EBC8";
        private const string H_DANGER = "#FF4545";
        private const string H_WARN = "#FFA520";
        private const string H_OK = "#45EF70";
        private const string H_NEUT = "#9AA0AE";
        private const string H_BLUE = "#45AAFF";
        private const string H_PURP = "#DCB8FF";
        private const string H_DIM = "#404855";
        private const string H_PIN = "#FFE510";
        private const string H_TARGET = "#FF2828";
        private const string H_GROUP = "#28B8FF";
        private const string H_YELLOW = "#FFEE20";
        private const string H_MODE_MIN = "#9AA0AE";
        private const string H_MODE_STD = "#6EC8FF";
        private const string H_MODE_FOC = "#FFE510";

        // =====================================================================
        //  컴포넌트 캐시
        // =====================================================================

        private AICharacterManager _ai;
        private NavMeshAgent _nav;
        private MobAIBrain _brain;
        private BehaviorGraphAgent _btAgent;
        private AIPerceptionSystem _perception;

        // =====================================================================
        //  텍스처 / GL 머티리얼 캐시 (static — 씬 전체 공유)
        // =====================================================================

        private static Texture2D _panelBgTex;  // 라운드 패널 배경
        private static Texture2D _minIconTex;  // Minimum 모드 원형 아이콘
        private static Material _glMat;       // GL 드로잉 머티리얼

        // =====================================================================
        //  GUIStyle 캐시
        // =====================================================================

        private GUIStyle _stylePanel;
        private GUIStyle _styleText;
        private bool _stylesReady;

        // ── 패널 드래그 상태 (req #3) ───────────────────────────────────────
        private bool _mainDragging, _charDragging, _groupDragging, _btDragging;
        private Vector2 _mainLastBase;
        private Vector2 _mainDragStart, _charDragStart, _groupDragStart, _btDragStart;
        private Vector2 _mainDragOff, _charDragOff, _groupDragOff, _btDragOff;

        // ── Scene View 전용 AI Info 드래그 상태 (Game View _mainDragOff와 분리) ─
        // Scene View에서도 AI Info 패널을 드래그할 수 있도록 별도 관리
        private bool _svInfoDragging;
        private Vector2 _svInfoDragStart, _svInfoDragOff;

        // ── 패널 커스텀 크기 (우·하단 핸들 드래그 리사이즈) ─────────────────
        private float _mainCustomW = 0f;   // 0 = 컨텐츠 자동 크기
        private float _mainCustomH = 0f;
        private bool _rsW, _rsH, _rsCorner;     // resize 방향 플래그
        private Vector2 _rsMouse;               // resize 드래그 시작 마우스 위치
        private float _rsStartW, _rsStartH;     // resize 드래그 시작 크기
        private const float RS_HANDLE = 7f;     // 핸들 영역 두께 (px)

        // 스냅 위치 캐시 (연결선 렌더링용)
        private Rect _lastMainPanelRect;
        private bool _lastPanelOnRight;
        private float _lastPanelScale = 1f;
        private static Material _connMat;  // 연결선 GL 머티리얼
        private const float TITLE_H = 18f;   // 타이틀 바 높이 (드래그 핸들)
        private AIDebuggerSettings.BTTheme _lastAppliedTheme = (AIDebuggerSettings.BTTheme)(-1);
        private int _lastAppliedCorner = -1;
        private int _lastAppliedBorderWidth = -1;
        private float _lastAppliedOpacity = -1f;
        private int _lastAppliedFontSize = -1;
        private int _lastAppliedColorHash = -1;

        // =====================================================================
        //  그룹 멤버 캐시 (1초 갱신)
        // =====================================================================

        private List<AICharacterManager> _groupCache = new();
        private float _groupCacheTime = -999f;
        private const float GROUP_CACHE_SEC = 1.0f;
        private const float GROUP_RADIUS = 22f;

        // =====================================================================
        //  내부 렌더러 인스턴스
        // =====================================================================

        // 선언 시점에 초기화 — Awake 전에 OnGUI가 호출되어도 NullRef 방지
        private BTGraphRenderer _btGraph = new BTGraphRenderer();
        private CharacterPanelRenderer _charPanel = new CharacterPanelRenderer();

        // =====================================================================
        //  생명주기
        // =====================================================================

        private const string PREF_BT_SIZE = "AIDebugger_BTGraphSize";

        private void Awake()
        {
#if UNITY_EDITOR
            // BT Graph 크기 불러오기 (EditorPrefs에 저장된 값)
            _btGraphSize = UnityEditor.EditorPrefs.GetInt(PREF_BT_SIZE, 0);
#endif
            _ai = GetComponent<AICharacterManager>();
            _nav = GetComponent<NavMeshAgent>();
            _brain = GetComponent<MobAIBrain>();
            _btAgent = GetComponent<BehaviorGraphAgent>();
            _perception = GetComponent<AIPerceptionSystem>();

            _btGraph = new BTGraphRenderer();
            _charPanel = new CharacterPanelRenderer();
            _charPanel.CacheComponents(gameObject);
        }

#if UNITY_EDITOR
        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneViewGUI;
            // BT 그래프 파싱 — 활성화 시 1회 (구조 고정)
            if (_btAgent != null)
                _btGraph.ParseGraph(_btAgent);
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneViewGUI;
        }
#endif

        // =====================================================================
        //  단축키 처리 — AIDebugKeys 상수 참조 (하드코딩 금지)
        // =====================================================================

        private void Update()
        {
            // F1: 디버그 모드 순환
            if (Input.GetKeyDown(AIDebugKeys.CYCLE_MODE))
                _debugMode = (DebugMode)(((int)_debugMode + 1) % 4);

            // F2: 핀 고정 / 해제
            if (Input.GetKeyDown(AIDebugKeys.PIN_AI))
                _pinnedAI = (_pinnedAI == gameObject) ? null : gameObject;

            // F3: GL 오버레이 ON/OFF
            if (Input.GetKeyDown(AIDebugKeys.TOGGLE_GL))
                _glOverlay = !_glOverlay;

            // Ctrl+Shift+B: BT 그래프 크기 순환 (0→1→2→0)
            if (AIDebugKeys.CtrlShift(AIDebugKeys.BT_GRAPH))
                _btGraphSize = (_btGraphSize + 1) % 3;
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetInt(PREF_BT_SIZE, _btGraphSize);
#endif

            // Ctrl+Shift+C: 캐릭터 패널 ON/OFF
            if (AIDebugKeys.CtrlShift(AIDebugKeys.CHAR_PANEL))
                _charPanelVisible = !_charPanelVisible;

#if UNITY_EDITOR
            // Ctrl+Shift+L: LOD 설정 창
            if (AIDebugKeys.CtrlShift(AIDebugKeys.LOD_WINDOW))
                AIDebuggerSettingsWindow.Open();
#endif

            // BT 그래프 교체 감지 — O(1) 참조 비교
            if (_btAgent != null && _btGraph.NeedsReParse(_btAgent))
                _btGraph.ParseGraph(_btAgent);

#if UNITY_EDITOR
            // 스피너 애니메이션을 위해 Scene View 강제 갱신 (Running 노드 존재 시)
            UnityEditor.SceneView.RepaintAll();
#endif
        }

        // =====================================================================
        //  BB 안전 읽기
        // =====================================================================

        /// <summary>Blackboard 변수를 안전하게 읽는다. 예외 시 default 반환.</summary>
        private T GetBB<T>(string key)
        {
            try
            {
                if (_btAgent?.BlackboardReference == null) return default;
                _btAgent.BlackboardReference.GetVariable(key, out BlackboardVariable<T> v);
                return v != null ? v.Value : default;
            }
            catch { return default; }
        }

        // ── SO Fallback: BB가 0이면 PB4DecisionAdapter.CombatProfile에서 직접 읽기 ──
        private object _combatProfile;
        private MonoBehaviour _decisionAdapter;
        private float _combatProfileCacheTime = -99f;

        private void EnsureCombatProfileCache()
        {
            if (Time.time - _combatProfileCacheTime < 2f) return;
            _combatProfileCacheTime = Time.time;
            _combatProfile = null; _decisionAdapter = null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var mb in GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                string tn = mb.GetType().Name;
                if (!tn.Contains("Decision") && !tn.Contains("Adapter") && !tn.Contains("PB4")) continue;
                _decisionAdapter = mb;
                // CombatProfile 필드 탐색
                foreach (var fi in mb.GetType().GetFields(flags))
                {
                    string fn = fi.Name.ToLower();
                    if (fn.Contains("combat") && fn.Contains("profile"))
                    { _combatProfile = fi.GetValue(mb); break; }
                }
                if (_combatProfile != null) break;
            }
        }

        private float GetCombatStat(string bbKey)
        {
            float bb = GetBB<float>(bbKey);
            if (bb > 0.001f) return bb;  // BB에 정상 값 있으면 사용

            EnsureCombatProfileCache();
            if (_combatProfile == null) return 0f;

            // BB 키 → SO 필드명 매핑
            string soField = bbKey switch
            {
                "EngageRange" => "engageRange",
                "StalkSpeed" => "stalkSpeed",
                "OrbitRadius" => "orbitRadius",
                "StrikeTriggerTime" => "strikeTriggerTime",
                "FleeSprintSpeed" => "fleeSprintSpeed",
                _ => char.ToLower(bbKey[0]) + bbKey[1..]
            };
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fi2 = _combatProfile.GetType().GetField(soField, flags);
            if (fi2 == null)
            {
                // 대소문자 무시 탐색
                foreach (var f in _combatProfile.GetType().GetFields(flags))
                    if (string.Equals(f.Name, soField, StringComparison.OrdinalIgnoreCase))
                    { fi2 = f; break; }
            }
            try { return fi2 != null ? Convert.ToSingle(fi2.GetValue(_combatProfile)) : 0f; }
            catch { return 0f; }
        }

        // =====================================================================
        //  상태 읽기 헬퍼
        // =====================================================================

        private string GetWinner() => GetBB<string>("UtilityWinner") ?? "---";
        private float GetFear() => _brain != null ? _brain.fear : 0f;
        private bool IsPerforming() => _ai != null && _ai.isPerformingAction;
        private AwarenessState GetAwareness() => _perception != null
                                                  ? _perception.CurrentAwareness
                                                  : AwarenessState.Unaware;


        private float CamDist()
        {
#if UNITY_EDITOR
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) return Vector3.Distance(sv.camera.transform.position, transform.position);
#endif
            return Camera.main != null ? Vector3.Distance(Camera.main.transform.position, transform.position) : float.MaxValue;
        }

        private Vector3 CamFacingDir()
        {
            Vector3 camPos;
#if UNITY_EDITOR
            var sv = SceneView.lastActiveSceneView;
            camPos = sv != null ? sv.camera.transform.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
#else
            camPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
#endif
            Vector3 d = camPos - transform.position; d.y = 0f;
            return d.sqrMagnitude > 0.001f ? d.normalized : Vector3.forward;
        }

        private Vector3 CamRight()
        {
#if UNITY_EDITOR
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) return sv.camera.transform.right;
#endif
            return Camera.main != null ? Camera.main.transform.right : Vector3.right;
        }

        private bool IsFullDetail()
        {
#if UNITY_EDITOR
            return _pinnedAI == gameObject || Selection.activeGameObject == gameObject;
#else
            return _pinnedAI == gameObject;
#endif
        }

        private List<AICharacterManager> GetGroupMembers()
        {
            if (Time.time - _groupCacheTime < GROUP_CACHE_SEC) return _groupCache;
            _groupCacheTime = Time.time;
            _groupCache.Clear();
            foreach (var other in FindObjectsByType<AICharacterManager>(FindObjectsSortMode.None))
            {
                if (other.gameObject == gameObject) continue;
                if (Vector3.Distance(transform.position, other.transform.position) <= GROUP_RADIUS)
                    _groupCache.Add(other);
            }
            return _groupCache;
        }

        // =====================================================================
        //  패널 내용 생성 — LOD + Settings + DebugMode 표시
        // =====================================================================

        /// <summary>LOD/Settings/DebugMode에 따른 패널 Rich Text를 반환한다.</summary>
        // Light 모드용 대체 색상 (어두운 배경이 없는 밝은 패널에서 가독성 향상)
        private string GetHex(string darkHex, string lightHex)
        {
            bool isLight = (AIDebuggerSettings.Instance?.theme ?? AIDebuggerSettings.BTTheme.Dark)
                           == AIDebuggerSettings.BTTheme.Light;
            return isLight ? lightHex : darkHex;
        }

        private string BuildPanelContent(float dist)
        {
            var s = AIDebuggerSettings.Instance;
            bool _lpLight = (s?.theme ?? AIDebuggerSettings.BTTheme.Dark)
                            == AIDebuggerSettings.BTTheme.Light;
            string _HL = _lpLight ? "#1A5FA0" : H_LABEL;
            string _HV = _lpLight ? "#2A1A00" : H_VALUE;
            string _HB = _lpLight ? "#004488" : H_BLUE;
            string _HP = _lpLight ? "#660099" : H_PURP;
            string _HG = _lpLight ? "#005588" : H_GROUP;
            string _HN = _lpLight ? "#444444" : H_NEUT;
            string _HD = _lpLight ? "#777777" : H_DIM;
            string _HY = _lpLight ? "#7A4500" : H_YELLOW;  // 노랑→진한 갈색
            string _HW = _lpLight ? "#8B4000" : H_WARN;    // 주황→진한 주황갈색
            string _HT = _lpLight ? "#CC0000" : H_TARGET;  // 빨강→진한 빨강
            string _HO = _lpLight ? "#006600" : H_OK;      // 초록→진한 초록
            string _HR = _lpLight ? "#BB0000" : H_DANGER;  // 위험→진한 빨강
            bool _isLightPanel = (s?.theme ?? AIDebuggerSettings.BTTheme.Dark)
                                 == AIDebuggerSettings.BTTheme.Light;
            // Light 모드에서 가독성 어두운 색으로 덮어쓰기
            if (_isLightPanel)
            {
                // 원본 상수를 지역 변수로 덮어쓰기는 C#에서 불가 → Rich Text 내 색을 아래서 inline 처리
            }

            string winner = GetWinner();
            float fear = GetFear();
            var aware = GetAwareness();
            bool perf = IsPerforming();
            bool pinned = (_pinnedAI == gameObject);

            string wHex = winner switch { "Attack" => H_DANGER, "Flee" => H_OK, "Patrol" => H_BLUE, _ => H_NEUT };

            // 현재 모드 표시 태그 (헤더 우측)
            string modeTag = _debugMode switch
            {
                DebugMode.Minimum => $"<color={H_MODE_MIN}>(MIN)</color>",
                DebugMode.Standard => $"<color={H_MODE_STD}>(STD)</color>",
                DebugMode.Focus => $"<color={H_MODE_FOC}>(FOC)</color>",
                _ => ""
            };

            string pinStr = pinned ? $" <color={H_PIN}>📌</color>" : "";
            string perfStr = perf ? $"<color={_HY}>⚡ </color>" : "";
            string header = $"{perfStr}<color={wHex}><b>[{winner}]</b></color>  {gameObject.name}{pinStr} {modeTag}";

            bool isSummary = dist <= LodSummary || IsFullDetail();
            if (!isSummary) return header;

            string aHex = aware switch { AwarenessState.Combat => H_DANGER, AwarenessState.Alert => H_WARN, AwarenessState.Suspicious => H_YELLOW, _ => H_NEUT };
            string sep = $"\n<color={_HD}>──────────────────────</color>";
            string awareL = $"\n<color={_HL}>AWARE  </color><color={aHex}><b>{aware}</b></color>";

            // ── 생득 욕구 4개 (MobAIBrain) ──
            float hunger = _brain != null ? _brain.hunger : 0f;
            float greed = _brain != null ? _brain.greed : 0f;
            float fatigue = _brain != null ? _brain.fatigue : 0f;

            string fearHex = fear > 0.7f ? H_DANGER : fear > 0.4f ? H_WARN : _HN;
            string hungerHex = hunger > 0.7f ? H_WARN : hunger > 0.4f ? H_YELLOW : _HN;
            string greedHex = greed > 0.7f ? H_WARN : greed > 0.4f ? _HB : _HN;
            string fatigueHex = fatigue > 0.7f ? H_DANGER : fatigue > 0.4f ? H_WARN : _HN;
            // Drive bar helper: inline to avoid lambda brace issues
            string DriveBar(float v, string col)
            {
                int ff = Mathf.Clamp(Mathf.RoundToInt(v * 8f), 0, 8);
                return $"<color={col}>{new string('\u2588', ff)}</color>" +
                       $"<color={_HD}>{new string('\u2591', 8 - ff)}</color>" +
                       $" <color={_HV}>{v:F2}</color>";
            }

            string drivesL =
                $"\n<color={_HL}>FEAR   </color>" + DriveBar(fear, fearHex) +
                $"\n<color={_HL}>HUNGER </color>" + DriveBar(hunger, hungerHex) +
                $"\n<color={_HL}>GREED  </color>" + DriveBar(greed, greedHex) +
                $"\n<color={_HL}>FATIGUE</color>" + DriveBar(fatigue, fatigueHex);

            // ── Utility Scores (private 필드 → Reflection으로 읽기) ──
            float uAtk = ReadUtility("u_attack");
            float uFlee = ReadUtility("u_flee");
            float uIdle = ReadUtility("u_idle");
            float uPat = ReadUtility("u_patrol");
            string utilL = $"\n<color={_HL}>U</color> " +
                $"<color={_HL}>ATK</color><color={_HV}>{uAtk:F2}</color> " +
                $"<color={_HL}>FLE</color><color={_HV}>{uFlee:F2}</color> " +
                $"<color={_HL}>IDL</color><color={_HV}>{uIdle:F2}</color> " +
                $"<color={_HL}>PAT</color><color={_HV}>{uPat:F2}</color>";

            string fearL = drivesL + utilL;

            // policy/engage 읽기 (Summary + Full 공통)
            string policy = GetBB<string>("PolicyType") ?? "---";
            float engage = GetCombatStat("EngageRange");

            bool isFull = dist <= LodFull || IsFullDetail();
            if (!isFull)
            {
                float stalkSp2 = GetCombatStat("StalkSpeed");
                float orbitR2 = GetCombatStat("OrbitRadius");
                float strikeT2 = GetCombatStat("StrikeTriggerTime");
                float fleeSpd2 = GetCombatStat("FleeSprintSpeed");
                string sk2 = (strikeT2 >= 9999f || float.IsInfinity(strikeT2)) ? "∞" : $"{strikeT2:F1}s";
                string cBB =
                    $"\n<color={_HL}>POLICY </color><color={_HP}>{policy}</color>  <color={_HL}>ENGAGE</color><color={_HV}> {engage:F1}m</color>" +
                    $"\n<color={_HL}>STALK </color><color={_HV}>{stalkSp2:F1}</color>  <color={_HL}>ORBIT </color><color={_HV}>{orbitR2:F1}m</color>  <color={_HL}>FLEE </color><color={_HV}>{fleeSpd2:F1}</color>  <color={_HL}>STRIKE</color><color={_HV}> {sk2}</color>";
                if (_brain?.currentTarget != null)
                {
                    float td2 = Vector3.Distance(transform.position, _brain.currentTarget.position);
                    cBB += $"\n<color={_HT}>▶ {_brain.currentTarget.name}  </color><color={_HV}>{td2:F1}m</color>";
                }
                return header + sep + awareL + fearL + cBB;
            }

            string bbSection = "";
            if (s == null || s.showBBVariables)
            {
                string policy3 = GetBB<string>("PolicyType") ?? "---";
                float engage3 = GetCombatStat("EngageRange");
                float stalk3 = GetCombatStat("StalkSpeed");
                float orbit3 = GetCombatStat("OrbitRadius");
                float strikeT3 = GetCombatStat("StrikeTriggerTime");
                float fleeSp3 = GetCombatStat("FleeSprintSpeed");
                string tags3 = GetBB<string>("TerrainTags") ?? "";
                string sk3 = (strikeT3 >= 9999f || float.IsInfinity(strikeT3)) ? "∞" : $"{strikeT3:F1}s";
                bbSection = sep
                    + $"\n<color={_HL}>POLICY  </color><color={_HP}>{policy3}</color>"
                    + $"\n<color={_HL}>ENGAGE  </color><color={_HV}>{engage3:F1}m</color>"
                    + $"\n<color={_HL}>STALK   </color><color={_HV}>{stalk3:F1}</color>"
                    + $"\n<color={_HL}>ORBIT   </color><color={_HV}>{orbit3:F1}m</color>"
                    + $"\n<color={_HL}>STRIKE  </color><color={_HV}>{sk3}</color>"
                    + $"\n<color={_HL}>FLEE    </color><color={_HV}>{fleeSp3:F1}</color>"
                    + (string.IsNullOrEmpty(tags3) ? "" : $"\n<color={_HL}>TAGS    </color><color={H_WARN}>{tags3}</color>");
            }

            string tgtSection = "";
            if ((s == null || s.showTargetInfo) && _brain?.currentTarget != null)
            {
                float td = Vector3.Distance(transform.position, _brain.currentTarget.position);
                tgtSection = sep
                    + $"\n<color={_HT}>▶ TARGET</color>  <color={_HV}>{_brain.currentTarget.name}</color>"
                    + $"\n<color={_HL}>  DIST  </color><color={_HV}>{td:F1}m</color>";
            }

            string memSection = "";
            if (s == null || s.showMemoryInfo)
            {
                var seen2 = GetBB<Vector3>("LastSeenPosition");
                var heard2 = GetBB<Vector3>("LastHeardPosition");
                if (seen2 != Vector3.zero || heard2 != Vector3.zero)
                {
                    memSection = sep;
                    if (seen2 != Vector3.zero) memSection += $"\n<color={_HL}>SEEN  </color><color={_HP}>{Vector3.Distance(transform.position, seen2):F1}m</color>";
                    if (heard2 != Vector3.zero) memSection += $"\n<color={_HB}>HEARD </color><color={_HV}>{Vector3.Distance(transform.position, heard2):F1}m</color>";
                }
            }

            string perfSection = "";
            if (s == null || s.showActionStatus)
            {
                bool perf2 = IsPerforming();
                if (perf2) perfSection += sep + $"\n<color={H_YELLOW}><b>⚡ isPerformingAction</b></color>";
                bool isGrounded = GetBB<bool>("isGrounded"); bool isLockedOn = GetBB<bool>("isLockedOn");
                bool isDefending = GetBB<bool>("isDefending"); bool isCrazy = GetBB<bool>("isCrazy");
                float chargeR = GetBB<float>("ChargeRatio"); int guardSt = GetBB<int>("guardStyle");
                string actSt = GetBB<string>("ActionState") ?? "";
                var ssb = new System.Text.StringBuilder();
                if (isGrounded) ssb.Append($"<color={H_OK}>●GND</color> ");
                if (isLockedOn) ssb.Append($"<color={H_DANGER}>🔒LOK</color> ");
                if (isDefending) ssb.Append($"<color={_HB}>🛡DEF</color> ");
                if (isCrazy) ssb.Append($"<color={H_WARN}>⚠CRZ</color> ");
                if (chargeR > 0.05f) ssb.Append($"<color={H_WARN}>CHG:{chargeR:F0}%</color> ");
                if (guardSt > 0) ssb.Append($"<color={_HB}>GD:{guardSt}</color> ");
                if (!string.IsNullOrEmpty(actSt)) ssb.Append($"<color={_HP}>[{actSt}]</color>");
                if (ssb.Length > 0) perfSection += sep + "\n" + ssb.ToString().TrimEnd();
            }

            string grpSection = "";
            if (s == null || s.showGroupInfo)
            {
                var gm = GetGroupMembers();
                if (gm.Count > 0) grpSection = sep + $"\n<color={_HG}>GROUP</color>  <color={_HV}>{gm.Count + 1} members nearby</color>";
            }

            return header + sep + awareL + fearL + bbSection + tgtSection + memSection + perfSection + grpSection;
        }

        // =====================================================================
        //  Game View 패널 (OnGUI)
        // =====================================================================

        private void OnGUI()
        {
            // 드래그 이벤트(MouseDown/Drag/Up)는 Repaint/Layout 필터에서 제외
            var evType = Event.current.type;
            bool isMouseEvent = evType == EventType.MouseDown ||
                                evType == EventType.MouseDrag ||
                                evType == EventType.MouseUp;
            if (!isMouseEvent &&
                evType != EventType.Repaint &&
                evType != EventType.Layout) return;

            if (_debugMode == DebugMode.Off || _ai == null) return;
            if (_debugMode == DebugMode.Focus && _pinnedAI != gameObject) return;

            // Camera.main은 pause 중에도 유지되지만 null safety 처리
            var _cam = Camera.main;
            if (_cam == null) return;

            { // 파라미터 변경 감지
                var si = AIDebuggerSettings.Instance;
                var ct = si?.theme ?? AIDebuggerSettings.BTTheme.Dark;
                int cr = si?.panelCornerRadius ?? 8; int bw = si?.panelBorderWidth ?? 2;
                float op = si?.panelOpacity ?? 0.90f; int fs = si?.baseFontSize ?? 11;
                Color bg = si != null ? (ct == AIDebuggerSettings.BTTheme.Light ? si.panelBgColorLight : si.panelBgColor) : Color.black;
                Color bd = si != null ? (ct == AIDebuggerSettings.BTTheme.Light ? si.panelBdColorLight : si.panelBdColor) : Color.blue;
                int ch = (int)(bg.r * 255) ^ ((int)(bg.g * 255) << 8) ^ ((int)(bg.b * 255) << 16) ^ ((int)(bd.r * 255) << 1);
                if (ct != _lastAppliedTheme || cr != _lastAppliedCorner || bw != _lastAppliedBorderWidth
                    || op != _lastAppliedOpacity || fs != _lastAppliedFontSize || ch != _lastAppliedColorHash)
                {
                    _stylesReady = false; _panelBgTex = null; _btGraph?.InvalidateLayout();
                    _lastAppliedTheme = ct; _lastAppliedCorner = cr; _lastAppliedBorderWidth = bw;
                    _lastAppliedOpacity = op; _lastAppliedFontSize = fs; _lastAppliedColorHash = ch;
                }
            }

            float snapWorldY = AIDebuggerSettings.Instance?.snapWorldY ?? 0.5f;
            Vector3 sp3 = _cam.WorldToScreenPoint(transform.position + Vector3.up * snapWorldY);
            if (sp3.z < 0f) return;
            // GUI 좌표 변환: Screen y(하단 기준) → GUI y(상단 기준)
            Vector2 sp = new Vector2(sp3.x, Screen.height - sp3.y);
            float dist = Vector3.Distance(_cam.transform.position, transform.position);
            if (dist > LodIcon) return;
            if (!_stylesReady) InitStyles();
            if (_debugMode == DebugMode.Minimum) { DrawMinimumIcon(sp, GetWinner()); return; }

            var _fss = AIDebuggerSettings.Instance;
            // matrix 스케일 모드: 항상 최고 품질 fontSize 사용
            int _bfs = _fss?.baseFontSize ?? 11;
            bool useScale = _fss?.useMatrixScale ?? true;
            _styleText.fontSize = _fss?.fontSizeBody > 0 ? _fss.fontSizeBody : _bfs;

            // 패널 스케일 계산
            float pScale = CalcPanelScale(dist, _fss);
            if (useScale && pScale <= (_fss?.scaleMin ?? 0.35f))
            {
                DrawMinimumIcon(sp, GetWinner());
                return;
            }
            _lastPanelScale = pScale;

            string content = BuildPanelContent(dist) ?? string.Empty;
            if (_styleText == null || _stylePanel == null) return;

            Vector2 cSize = _styleText.CalcSize(new GUIContent(content));
            float padH = 16f, padV = 12f;
            float pw = Mathf.Max(cSize.x + padH * 2f, 160f), ph = cSize.y + padV * 2f + TITLE_H;

            // ── FocusOnly 조기 반환 ─────────────────────────────────────────
            {
                var _earlyMode = _fss?.overlapMode ?? AIDebuggerSettings.OverlapMode.Auto;
                if (_earlyMode == AIDebuggerSettings.OverlapMode.FocusOnly && _pinnedAI != gameObject)
                    return;  // 핀되지 않은 AI는 FocusOnly 모드에서 패널 숨김
            }

            // ── 스냅 위치 계산 (unscaled 좌표) ─────────────────────────
            // onRight: _lastPanelOnRight로 초기화 → _mainDragging=true 경로에서 미할당 방지
            bool onRight = _lastPanelOnRight;
            Vector2 snapBase = _mainDragging
                ? _mainLastBase
                : CalcSnapPosition(sp, pw, ph, pScale, _fss, out onRight, GetInstanceID());
            if (!_mainDragging)
            {
                var oMode = _fss?.overlapMode ?? AIDebuggerSettings.OverlapMode.Auto;
                var evT = Event.current.type;

                // ── Layout 이벤트에서만 위치 계산·갱신 ───────────────────────
                // Repaint 에서도 갱신하면 Layout과 Repaint가 서로 다른 dict 상태를
                // 참조해 패널이 흔들리는 문제 발생 → Layout에서만 갱신, 나머지는 캐시 사용
                if (evT != EventType.Layout)
                {
                    // Layout 이 아닌 이벤트: 캐시된 위치 재사용
                    snapBase = _mainLastBase;
                    onRight = _lastPanelOnRight;
                    OverlapResolver.Register(GetInstanceID(),
                        new Rect(snapBase.x, snapBase.y, pw * pScale, ph * pScale));
                }
                else
                {
                    // ── Layout: 겹침 해소 모드별 처리 ────────────────────────
                    if (oMode == AIDebuggerSettings.OverlapMode.SnapSide ||
                        oMode == AIDebuggerSettings.OverlapMode.Auto)
                    {
                        // SnapSide: 캐릭터 X위치 기반 좌·우 결정 → 이후 Y축 분리로 마무리
                        float halfSW = Screen.width * 0.5f;
                        float pw_s = pw * pScale, ph_s = ph * pScale;
                        float ox2 = _fss?.snapOffsetX ?? 16f;
                        float oy2 = _fss?.snapOffsetY ?? 0f;
                        float baseY2 = Mathf.Clamp(sp.y - ph_s * 0.5f + oy2, 4f, Screen.height - ph_s - 4f);
                        bool charOnLeft = sp.x < halfSW;
                        float snapX = charOnLeft
                            ? Mathf.Clamp(sp.x + ox2, 4f, Screen.width - pw_s - 4f)
                            : Mathf.Clamp(sp.x - ox2 - pw_s, 4f, Screen.width - pw_s - 4f);
                        onRight = charOnLeft;

                        // 같은 쪽에 몰릴 때 Y축 분리 — SnapSide와 Auto 모두 적용
                        Rect c2 = new Rect(snapX, baseY2, pw_s, ph_s);
                        for (int i = 0; i < 10; i++)
                        {
                            if (!OverlapResolver.CheckOverlap(c2, GetInstanceID())) break;
                            c2.y += ph_s + 8f;
                        }
                        if (c2.yMax > Screen.height - 4f)
                        {
                            Rect cu2 = new Rect(snapX, baseY2, pw_s, ph_s);
                            for (int i = 0; i < 10; i++)
                            {
                                if (!OverlapResolver.CheckOverlap(cu2, GetInstanceID())) break;
                                cu2.y -= (ph_s + 8f);
                            }
                            if (cu2.y >= 4f) c2 = cu2;
                        }
                        c2.y = Mathf.Clamp(c2.y, 4f, Screen.height - ph_s - 4f);
                        snapBase = new Vector2(c2.x, c2.y);
                    }
                    else if (oMode == AIDebuggerSettings.OverlapMode.SeparateForce)
                    {
                        // SeparateForce: X·Y 2D 반발력으로 자연스럽게 분산
                        // GridJitter와 달리 양 축 모두 밀어내므로 시각적으로 다른 패턴
                        float cW3 = pw * pScale, cH3 = ph * pScale;
                        Rect c3 = new Rect(snapBase.x, snapBase.y, cW3, cH3);
                        float strength = _fss?.separateForceStrength ?? 0.5f;
                        int iters = _fss?.separateForceIterations ?? 6;
                        for (int iter = 0; iter < iters; iter++)
                        {
                            Vector2 disp = OverlapResolver.GetSeparation(c3, GetInstanceID());
                            if (disp == Vector2.zero) break;
                            c3.x += disp.x * strength;
                            c3.y += disp.y * strength;
                            c3.x = Mathf.Clamp(c3.x, 4f, Screen.width - cW3 - 4f);
                            c3.y = Mathf.Clamp(c3.y, 4f, Screen.height - cH3 - 4f);
                        }
                        // 반발력 후에도 겹치면 Y축 밀기로 마무리 (이전 프레임 등록이 없을 때 대비)
                        for (int _oi = 0; _oi < 10; _oi++)
                        {
                            if (!OverlapResolver.CheckOverlap(c3, GetInstanceID())) break;
                            c3.y += cH3 + 8f;
                        }
                        c3.y = Mathf.Clamp(c3.y, 4f, Screen.height - cH3 - 4f);
                        snapBase = new Vector2(c3.x, c3.y);
                    }
                    else if (oMode == AIDebuggerSettings.OverlapMode.GridJitter)
                    {
                        // GridJitter: Y축 단방향 순차 밀기 (가장 단순·예측 가능)
                        float cW = pw * pScale, cH = ph * pScale;
                        Rect candR = new Rect(snapBase.x, snapBase.y, cW, cH);
                        for (int _oi = 0; _oi < 10; _oi++)
                        {
                            if (!OverlapResolver.CheckOverlap(candR, GetInstanceID())) break;
                            candR.y += cH + 8f;
                        }
                        if (candR.yMax > Screen.height - 4f)
                        {
                            Rect candUp = new Rect(snapBase.x, snapBase.y, cW, cH);
                            for (int _oi = 0; _oi < 10; _oi++)
                            {
                                if (!OverlapResolver.CheckOverlap(candUp, GetInstanceID())) break;
                                candUp.y -= (cH + 8f);
                            }
                            if (candUp.y >= 4f) candR = candUp;
                        }
                        candR.y = Mathf.Clamp(candR.y, 4f, Screen.height - cH - 4f);
                        snapBase = new Vector2(candR.x, candR.y);
                    }
                    // None / FocusOnly: 해소 없이 CalcSnapPosition 결과 그대로 사용

                    // ── 몹 가림 방지 (Layout에서만 적용) ──────────────────────
                    // 캐릭터의 화면 예상 영역과 패널이 겹치면 바깥으로 밀어냄.
                    // 자신의 캐릭터만 회피 (다른 AI 캐릭터는 OverlapResolver가 처리).
                    if (_fss?.avoidMobOcclusion ?? true)
                    {
                        float mW = (_fss?.mobExclusionW ?? 100f) * pScale;
                        float mH = (_fss?.mobExclusionH ?? 220f) * pScale;
                        // sp = 캐릭터 허리 높이 기준 → 몸 중앙 기준 rect
                        Rect mobR = new Rect(sp.x - mW * 0.5f, sp.y - mH * 0.6f, mW, mH);
                        Rect panR = new Rect(snapBase.x, snapBase.y, pw * pScale, ph * pScale);

                        for (int _mi = 0; _mi < 6; _mi++)
                        {
                            if (!panR.Overlaps(mobR)) break;
                            // 겹침 방향: 수평 or 수직 중 오버랩이 작은 쪽으로 밀어냄
                            float dx = panR.center.x - mobR.center.x;
                            float dy = panR.center.y - mobR.center.y;
                            float ovX = (panR.width + mobR.width) * 0.5f - Mathf.Abs(dx);
                            float ovY = (panR.height + mobR.height) * 0.5f - Mathf.Abs(dy);
                            if (ovX < ovY)
                                panR.x += Mathf.Sign(dx) * (ovX + 4f);
                            else
                                panR.y += Mathf.Sign(dy) * (ovY + 4f);
                            panR.x = Mathf.Clamp(panR.x, 4f, Screen.width - panR.width - 4f);
                            panR.y = Mathf.Clamp(panR.y, 4f, Screen.height - panR.height - 4f);
                        }
                        snapBase = new Vector2(panR.x, panR.y);
                    }

                    // Layout에서만 안정 위치 저장 + 등록
                    OverlapResolver.Register(GetInstanceID(),
                        new Rect(snapBase.x, snapBase.y, pw * pScale, ph * pScale));
                    _mainLastBase = snapBase;
                    _lastPanelOnRight = onRight;
                }
            }
            else
            {
                onRight = _lastPanelOnRight;
            }

            // 리사이즈 적용: 커스텀 크기가 있으면 사용
            if (_mainCustomW > 0f) pw = _mainCustomW / pScale;
            if (_mainCustomH > 0f) ph = _mainCustomH / pScale;

            Vector2 _mainBase = snapBase + _mainDragOff;
            Rect panelRect = new Rect(_mainBase.x, _mainBase.y, pw, ph);

            // ── GUI.matrix 스케일 적용 ─────────────────────────────────
            _styleText.richText = true;
            Matrix4x4 _matBak = GUI.matrix;
            if (useScale && pScale < 0.999f)
                GUIUtility.ScaleAroundPivot(new Vector2(pScale, pScale),
                                            new Vector2(panelRect.x, panelRect.y));

            DrawPanelTitled(panelRect, "AI Info", content, _stylePanel, _styleText,
                            ref _mainDragging, ref _mainDragStart, ref _mainDragOff);

            // ── 리사이즈 핸들 (패널 우·하단 엣지 드래그) ─────────────────
            DrawResizeHandles(panelRect, ref _mainCustomW, ref _mainCustomH, pScale);

            // scaled 후 실제 화면 상의 rect 저장
            _lastMainPanelRect = new Rect(panelRect.x, panelRect.y,
                                          pw * pScale, ph * pScale);
            GUI.matrix = _matBak;

            // ── 연결선 (패널 → 캐릭터) ────────────────────────────────
            DrawConnectorLine(_lastMainPanelRect, onRight, sp, _fss);

            var s3 = AIDebuggerSettings.Instance;
            if (_btGraph != null && (s3 == null || s3.showBTGraph) && _btGraphSize < 2 && _btAgent != null)
            {
                float dd = Camera.main != null ? Vector3.Distance(Camera.main.transform.position, transform.position) : 0f;
                // distScale을 DrawBTGraph에 직접 전달 — GL 좌표도 함께 스케일됨
                _btGraph.DrawBTGraph(_lastMainPanelRect, _btGraphSize, dd, false, _stylePanel, s3,
                                     useScale ? pScale : 1f);
            }

            bool showChar = (s3?.showCharacterPanel ?? false) || _charPanelVisible;

            // ── 캐릭터 패널 위치 (scaled rect 기반) ─────────────────────
            var charAnchor = s3?.charPanelAnchor ?? AIDebuggerSettings.CharPanelAnchor.RightOfMain;
            Vector2 charBase;
            if (charAnchor == AIDebuggerSettings.CharPanelAnchor.RightOfMain)
                charBase = new Vector2(_lastMainPanelRect.xMax + 6f, _lastMainPanelRect.y);
            else
                charBase = new Vector2(_lastMainPanelRect.x, _lastMainPanelRect.yMax + 4f);
            charBase += _charDragOff;

            float charBottom = charBase.y;
            if (_charPanel != null && showChar)
            {
                Matrix4x4 _chMatBak = GUI.matrix;
                if (useScale && pScale < 0.999f)
                    GUIUtility.ScaleAroundPivot(new Vector2(pScale, pScale),
                                                new Vector2(charBase.x, charBase.y));
                _charPanel.DrawInGameView(charBase, _styleText, _stylePanel);
                GUI.matrix = _chMatBak;
                charBottom = charBase.y + _charPanel.LastPanelHeight * pScale;
            }

            // ── 그룹 AI 패널 위치 ───────────────────────────────────────
            var grpAnchor = s3?.groupPanelAnchor ?? AIDebuggerSettings.GroupPanelAnchor.BelowChar;
            Vector2 grpBase = grpAnchor == AIDebuggerSettings.GroupPanelAnchor.BelowChar
                ? new Vector2(charBase.x, charBottom + 4f)
                : new Vector2(panelRect.x, panelRect.yMax + 4f);
            grpBase += _groupDragOff;
            if (s3?.showGroupPanel ?? true)
                DrawGroupPanel(grpBase, _styleText, _stylePanel);
        }

        // ── 캐릭터 이름 해시 → 고유 색상 ────────────────────────────────────
        private static Color GetCharColor(string name)
        {
            int h = 0; foreach (char ch in name) h = h * 31 + ch;
            return Color.HSVToRGB((Mathf.Abs(h) % 360) / 360f, 0.75f, 0.95f);
        }

        // ── GUI.matrix 거리 스케일 계산 ─────────────────────────────────────
        private float CalcPanelScale(float dist, AIDebuggerSettings sett)
        {
            if (sett == null || !sett.useMatrixScale) return 1f;
            float refD = Mathf.Max(sett.scaleRefDist, 0.1f);
            float exp = sett.scaleExp;
            float raw = Mathf.Pow(refD / Mathf.Max(dist, 0.01f), exp);
            return Mathf.Clamp(raw, sett.scaleMin, 1f);
        }

        // ── 캐릭터 옆 스냅 위치 계산 ─────────────────────────────────────
        // 기본: 우측 배치. 우측이 화면 밖이거나 다른 패널과 겹치면 좌측으로 이동.
        private Vector2 CalcSnapPosition(Vector2 guiPos, float pw, float ph,
                                         float scale, AIDebuggerSettings sett,
                                         out bool right, int excludeId = -1)
        {
            float vw = pw * scale, vh = ph * scale;
            float ox = sett?.snapOffsetX ?? 16f;
            float oy = sett?.snapOffsetY ?? 0f;
            var side = sett?.snapSide ?? AIDebuggerSettings.PanelSnapSide.Auto;

            float baseY = Mathf.Clamp(guiPos.y - vh * 0.5f + oy, 4f, Screen.height - vh - 4f);

            float rx = guiPos.x + ox;
            float lx = guiPos.x - ox - vw;

            Rect rightRect = new Rect(rx, baseY, vw, vh);
            Rect leftRect = new Rect(lx, baseY, vw, vh);

            if (side == AIDebuggerSettings.PanelSnapSide.Right)
            {
                right = true;
                float fx = Mathf.Clamp(rx, 4f, Screen.width - vw - 4f);
                return new Vector2(fx, baseY);
            }
            if (side == AIDebuggerSettings.PanelSnapSide.Left)
            {
                right = false;
                float fx = Mathf.Clamp(lx, 4f, Screen.width - vw - 4f);
                return new Vector2(fx, baseY);
            }

            // Auto: 우측 우선, 겹치면 좌측 — excludeId를 넘겨 자기 자신과 충돌 방지
            bool rightFits = (rx + vw) < Screen.width - 4f;
            bool rightClear = rightFits && !OverlapResolver.CheckOverlap(rightRect, excludeId);
            bool leftFits = lx >= 4f;

            if (rightClear) { right = true; return new Vector2(rx, baseY); }
            if (leftFits && !OverlapResolver.CheckOverlap(leftRect, excludeId))
            { right = false; return new Vector2(lx, baseY); }
            right = rightFits;
            float fallX = right ? Mathf.Clamp(rx, 4f, Screen.width - vw - 4f)
                                : Mathf.Clamp(lx, 4f, Screen.width - vw - 4f);
            return new Vector2(fallX, baseY);
        }

        // ── 리사이즈 핸들: 패널 우·하단 엣지 드래그로 크기 조절 ──────────────
        /// <summary>
        /// 패널의 우측/하단/코너 엣지에 반투명 핸들을 그리고 드래그 크기 조절을 처리한다.
        /// customW/customH가 0이면 자동(컨텐츠 기반) 크기 사용.
        /// </summary>
        private void DrawResizeHandles(Rect panel, ref float customW, ref float customH, float scale = 1f)
        {
            if (Event.current.type == EventType.Layout) return;
            float hs = RS_HANDLE;

            // 핸들 영역 정의 (unscaled panel 기준)
            Rect hRight = new Rect(panel.xMax - hs, panel.y + TITLE_H, hs, panel.height - TITLE_H);
            Rect hBottom = new Rect(panel.x, panel.yMax - hs, panel.width, hs);
            Rect hCorner = new Rect(panel.xMax - hs, panel.yMax - hs, hs, hs);

            // 핸들 시각화
            var ev = Event.current;
            bool hoverR = hRight.Contains(ev.mousePosition);
            bool hoverB = hBottom.Contains(ev.mousePosition);
            bool hoverC = hCorner.Contains(ev.mousePosition);

            Color prev = GUI.color;
            if (hoverC || _rsCorner)
            { GUI.color = new Color(0.28f, 0.76f, 1f, 0.70f); GUI.DrawTexture(hCorner, Texture2D.whiteTexture); }
            if (hoverR || _rsW)
            { GUI.color = new Color(0.28f, 0.76f, 1f, 0.35f); GUI.DrawTexture(hRight, Texture2D.whiteTexture); }
            if (hoverB || _rsH)
            { GUI.color = new Color(0.28f, 0.76f, 1f, 0.35f); GUI.DrawTexture(hBottom, Texture2D.whiteTexture); }
            GUI.color = prev;

            Vector2 mp = ev.mousePosition;

            if (ev.type == EventType.MouseDown && ev.button == 0)
            {
                if (hoverC) { _rsCorner = true; _rsMouse = mp; _rsStartW = customW > 0 ? customW : panel.width; _rsStartH = customH > 0 ? customH : panel.height; ev.Use(); }
                else if (hoverR) { _rsW = true; _rsMouse = mp; _rsStartW = customW > 0 ? customW : panel.width; ev.Use(); }
                else if (hoverB) { _rsH = true; _rsMouse = mp; _rsStartH = customH > 0 ? customH : panel.height; ev.Use(); }
            }

            if (ev.type == EventType.MouseDrag && ev.button == 0 && (_rsW || _rsH || _rsCorner))
            {
                float dX = (mp.x - _rsMouse.x) / scale;
                float dY = (mp.y - _rsMouse.y) / scale;
                if (_rsW || _rsCorner) customW = Mathf.Max(_rsStartW + dX, 120f);
                if (_rsH || _rsCorner) customH = Mathf.Max(_rsStartH + dY, 48f);
                ev.Use();
            }

            if (ev.type == EventType.MouseUp) { _rsW = _rsH = _rsCorner = false; }
        }

        // ── GL 연결선 렌더링 ──────────────────────────────────────────────
        private static void EnsureConnMat()
        {
            if (_connMat != null) return;
            var sh = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default");
            if (sh == null) return;
            _connMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _connMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _connMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _connMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _connMat.SetInt("_ZWrite", 0);
        }

        private void DrawConnectorLine(Rect panelRect, bool onRight, Vector2 charGuiPos,
                                       AIDebuggerSettings sett, Color? colorOvr = null,
                                       float vpW = 0f, float vpH = 0f)
        {
            if (sett == null || !sett.showConnectorLine) return;
            EnsureConnMat(); if (_connMat == null) return;

            // 패널 연결점: 캐릭터 방향 쪽 edge 중앙
            Vector2 panelAnchor = onRight
                ? new Vector2(panelRect.x, panelRect.center.y)
                : new Vector2(panelRect.xMax, panelRect.center.y);

            Color col = colorOvr.HasValue ? new Color(colorOvr.Value.r, colorOvr.Value.g, colorOvr.Value.b, sett.connectorColor.a) : sett.connectorColor;
            float lw = sett.connectorWidth;
            float dash = sett.connectorDash;
            float dotR = sett.connectorDotR;

            // GL 픽셀 매트릭스:
            // · Game View(OnGUI)  → Screen.width/height (게임 해상도와 IMGUI 좌표 일치)
            // · Scene View        → vpW/vpH (sv.position.width/height) 를 받아야
            //   IMGUI 좌표계와 GL 좌표계가 일치함.
            //   Screen.width/height는 게임 해상도이므로 Scene View에서 사용 불가.
            float _glW = vpW > 0f ? vpW : Screen.width;
            float _glH = vpH > 0f ? vpH : Screen.height;

            _connMat.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, _glW, _glH, 0);
            GL.Begin(GL.QUADS);

            // 점선 / 실선 그리기
            Vector2 d = charGuiPos - panelAnchor;
            float len = d.magnitude;
            if (len < 0.5f) { GL.End(); GL.PopMatrix(); return; }
            Vector2 dir = d / len;
            Vector2 perp = new Vector2(-dir.y, dir.x) * (lw * 0.5f);

            if (dash < 0.5f)
            {
                // 실선
                GL.Color(col);
                GL.Vertex3(panelAnchor.x + perp.x, panelAnchor.y + perp.y, 0f);
                GL.Vertex3(panelAnchor.x - perp.x, panelAnchor.y - perp.y, 0f);
                GL.Vertex3(charGuiPos.x - perp.x, charGuiPos.y - perp.y, 0f);
                GL.Vertex3(charGuiPos.x + perp.x, charGuiPos.y + perp.y, 0f);
            }
            else
            {
                // 점선
                float drawn = 0f; bool on = true;
                while (drawn < len)
                {
                    float segEnd = Mathf.Min(drawn + dash, len);
                    if (on)
                    {
                        Vector2 s2 = panelAnchor + dir * drawn;
                        Vector2 e2 = panelAnchor + dir * segEnd;
                        GL.Color(col);
                        GL.Vertex3(s2.x + perp.x, s2.y + perp.y, 0f);
                        GL.Vertex3(s2.x - perp.x, s2.y - perp.y, 0f);
                        GL.Vertex3(e2.x - perp.x, e2.y - perp.y, 0f);
                        GL.Vertex3(e2.x + perp.x, e2.y + perp.y, 0f);
                    }
                    drawn += dash; on = !on;
                }
            }

            GL.End();

            // 끝 원형 마커 (캐릭터 위치) — TRIANGLES
            if (dotR > 0.5f)
            {
                GL.Begin(GL.TRIANGLES);
                const int SEGS = 12;
                for (int si = 0; si < SEGS; si++)
                {
                    float a0 = si / (float)SEGS * Mathf.PI * 2f;
                    float a1 = (si + 1) / (float)SEGS * Mathf.PI * 2f;
                    GL.Color(col);
                    GL.Vertex3(charGuiPos.x, charGuiPos.y, 0f);
                    GL.Vertex3(charGuiPos.x + Mathf.Cos(a0) * dotR, charGuiPos.y + Mathf.Sin(a0) * dotR, 0f);
                    GL.Vertex3(charGuiPos.x + Mathf.Cos(a1) * dotR, charGuiPos.y + Mathf.Sin(a1) * dotR, 0f);
                }
                GL.End();
            }

            GL.PopMatrix();
        }

        private float ReadUtility(string fieldName)
        {
            if (_brain == null) return 0f;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            FieldInfo fi;
            switch (fieldName)
            {
                case "u_attack": if (s_uAtk == null) s_uAtk = _brain.GetType().GetField(fieldName, flags); fi = s_uAtk; break;
                case "u_flee": if (s_uFlee == null) s_uFlee = _brain.GetType().GetField(fieldName, flags); fi = s_uFlee; break;
                case "u_idle": if (s_uIdle == null) s_uIdle = _brain.GetType().GetField(fieldName, flags); fi = s_uIdle; break;
                case "u_patrol": if (s_uPat == null) s_uPat = _brain.GetType().GetField(fieldName, flags); fi = s_uPat; break;
                default: fi = _brain.GetType().GetField(fieldName, flags); break;
            }
            try { return fi != null ? (float)fi.GetValue(_brain) : 0f; }
            catch { return 0f; }
        }

        // ── 타이틀 바 패널 렌더링 + 드래그 핸들러 ────────────────────────────

        /// <summary>타이틀 바가 있는 패널을 그리고 드래그를 처리한다.</summary>
        // 패널 타이틀 텍스처 캐시 (전체 패널 하나 + 타이틀 오버레이)
        private Texture2D _panelFullTex;   // 패널 전체 배경 (4코너 라운드)
        private Texture2D _titleOverlayTex; // 타이틀 영역 색칠 (상단 라운드)
        private int _lastTitleCR = -1, _lastTitleBW = -1;

        private void RebuildPanelTextures()
        {
            var sett = AIDebuggerSettings.Instance;
            int cr = Mathf.RoundToInt((sett?.panelCornerRadius ?? 8) / 15f * 120f);
            int bw = sett?.panelBorderWidth ?? 2;
            if (_panelFullTex != null && _lastTitleCR == cr && _lastTitleBW == bw) return;
            if (_panelFullTex != null) UnityEngine.Object.Destroy(_panelFullTex);
            if (_titleOverlayTex != null) UnityEngine.Object.Destroy(_titleOverlayTex);
            float op = sett?.panelOpacity ?? 0.90f;
            bool isLight = (sett?.theme ?? AIDebuggerSettings.BTTheme.Dark) == AIDebuggerSettings.BTTheme.Light;
            Color bgC = isLight ? (sett?.panelBgColorLight ?? new Color(0.88f, 0.91f, 0.95f)) : (sett?.panelBgColor ?? new Color(0.07f, 0.08f, 0.11f));
            Color bdC = isLight ? (sett?.panelBdColorLight ?? new Color(0.22f, 0.50f, 0.85f)) : (sett?.panelBdColor ?? new Color(0.28f, 0.76f, 1.00f));
            Color titleC = new Color(0.18f, 0.55f, 0.90f, op * 0.92f);
            // 전체 패널 배경 (4코너 모두 라운드)
            _panelFullTex = BuildRoundedTexStatic(256, cr, WithAlpha(bgC, op), WithAlpha(bdC, op), bw);
            // 타이틀 오버레이: 상단 라운드, 하단 직각 (배경 위에 덮어씀)
            _titleOverlayTex = BuildRoundedTexCorners(256, cr, titleC, WithAlpha(bdC, op), bw, true, true, false, false);
            _lastTitleCR = cr; _lastTitleBW = bw;
        }

        private void DrawPanelTitled(Rect panel, string title, string content,
                                     GUIStyle panelStyle, GUIStyle textStyle,
                                     ref bool dragging, ref Vector2 dragStart, ref Vector2 dragOff)
        {
            RebuildPanelTextures();
            var sett = AIDebuggerSettings.Instance;
            int cr = Mathf.Clamp(sett?.panelCornerRadius ?? 8, 0, 15);

            // ── 1. 전체 패널 배경 (4코너 라운드) ────────────────────────
            var fullSt = new GUIStyle { normal = { background = _panelFullTex }, border = new RectOffset(20, 20, 20, 20) };
            GUI.Box(panel, GUIContent.none, fullSt);

            // ── 2. 타이틀 오버레이 (상단 라운드, 본문 위에 색칠) ─────────
            Rect titleBar = new Rect(panel.x, panel.y, panel.width, TITLE_H);
            var titleSt = new GUIStyle { normal = { background = _titleOverlayTex }, border = new RectOffset(20, 20, 20, 0) };
            GUI.Box(titleBar, GUIContent.none, titleSt);

            // ── 3. 타이틀 텍스트 ─────────────────────────────────────────
            float margin = Mathf.Max(cr * 0.6f, 5f);
            var lblSt = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                richText = false,
            };
            lblSt.normal.textColor = Color.white;
            GUI.Label(new Rect(titleBar.x + margin, titleBar.y, titleBar.width - margin * 2f, TITLE_H),
                      title, lblSt);

            // ── 4. 캐릭터 고유 색상 인디케이터 (좌측 3px 세로선) ─────────
            Color charAccent = GetCharColor(gameObject.name);
            Color prevIA = GUI.color; GUI.color = charAccent;
            float indW = 3f, indR = Mathf.Min(cr, 3f);
            GUI.DrawTexture(new Rect(panel.x, panel.y, indW, panel.height), Texture2D.whiteTexture);
            GUI.color = prevIA;

            // ── 5. 드래그 핸들 ───────────────────────────────────────────
            HandleDrag(titleBar, ref dragging, ref dragStart, ref dragOff);

            // ── 5. 본문 콘텐츠 ───────────────────────────────────────────
            float padH = 10f;
            Rect textRect = new Rect(panel.x + padH, panel.y + TITLE_H + 4f,
                                     panel.width - padH * 2f,
                                     panel.height - TITLE_H - 8f);
            GUI.Label(textRect, content, textStyle);
        }

        /// <summary>Rect 영역에서 마우스 드래그를 감지해 dragOff를 갱신한다.</summary>
        private static void HandleDrag(Rect handle,
                                       ref bool dragging, ref Vector2 dragStart, ref Vector2 dragOff)
        {
            var ev = Event.current;
            Vector2 mPos = ev.mousePosition;
            if (ev.type == EventType.MouseDown && ev.button == 0 && handle.Contains(mPos))
            {
                dragging = true;
                // dragStart = 클릭 당시 마우스 위치 - 현재 오프셋 = 고정 기준점
                dragStart = mPos - dragOff;
                ev.Use();
            }
            else if (ev.type == EventType.MouseDrag && ev.button == 0 && dragging)
            {
                // 오프셋 = 현재 마우스 위치 - 기준점
                dragOff = mPos - dragStart;
                ev.Use();
            }
            else if (ev.type == EventType.MouseUp && dragging)
            {
                dragging = false;
                ev.Use();
            }
        }

        private void DrawMinimumIcon(Vector2 center, string winner)
        {
            Color col = winner switch { "Attack" => new Color(1f, 0.28f, 0.28f, 0.85f), "Flee" => new Color(0.28f, 1f, 0.45f, 0.85f), "Patrol" => new Color(0.35f, 0.60f, 1f, 0.85f), _ => new Color(0.6f, 0.6f, 0.6f, 0.70f) };
            if (_minIconTex == null) { _minIconTex = new Texture2D(16, 16, TextureFormat.RGBA32, false); for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) _minIconTex.SetPixel(x, y, Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(8, 8)) < 7f ? Color.white : Color.clear); _minIconTex.Apply(); }
            Color prev = GUI.color; GUI.color = col;
            GUI.DrawTexture(new Rect(center.x - 8, center.y - 8, 16, 16), _minIconTex);
            GUI.color = prev;
        }

        private void InitStyles()
        {
            try
            {
                bool _light = (AIDebuggerSettings.Instance?.theme ?? AIDebuggerSettings.BTTheme.Dark) == AIDebuggerSettings.BTTheme.Light;
                float _op = AIDebuggerSettings.Instance?.panelOpacity ?? 0.90f;
                Color _bg, _bd;
                var _sInst = AIDebuggerSettings.Instance;
                if (_sInst != null)
                {
                    _bg = WithAlpha(_light ? _sInst.panelBgColorLight : _sInst.panelBgColor, _op);
                    _bd = WithAlpha(_light ? _sInst.panelBdColorLight : _sInst.panelBdColor, Mathf.Min(_op + 0.1f, 1f));
                }
                else { _bg = new Color(0.07f, 0.08f, 0.11f, _op); _bd = COL_BORDER; }
                int _cr = Mathf.Clamp(AIDebuggerSettings.Instance?.panelCornerRadius ?? 8, 0, 15);
                int _bw = AIDebuggerSettings.Instance?.panelBorderWidth ?? 2;
                if (_panelBgTex == null) { int _rPx = Mathf.RoundToInt(_cr / 15f * 120f); _panelBgTex = BuildRoundedTex(256, _rPx, _bg, _bd, _bw); }
                _stylePanel = new GUIStyle(); _stylePanel.normal.background = _panelBgTex; _stylePanel.border = new RectOffset(7, 7, 7, 7);
                GUIStyle baseLabelStyle = GUI.skin != null ? GUI.skin.label : new GUIStyle();
                bool _isLightTxt = _light;
                _styleText = new GUIStyle(baseLabelStyle) { richText = true, alignment = TextAnchor.UpperLeft, wordWrap = false };
                _styleText.normal.textColor = _isLightTxt ? new Color(0.10f, 0.12f, 0.20f) : new Color(0.93f, 0.90f, 0.78f);
                _stylesReady = true;
            }
            catch (Exception ex) { Debug.LogWarning($"[AIPerceptionDebugger] InitStyles 실패: {ex.Message}"); _stylesReady = false; }
        }

        internal static Texture2D BuildRoundedTexStatic(int texSize, int r, Color fill, Color border, int bw)
            => BuildRoundedTex(texSize, r, fill, border, bw);

        /// <summary>코너별 독립 라운드 처리. tl/tr/bl/br = 해당 코너 라운드 여부.</summary>
        internal static Texture2D BuildRoundedTexCorners(int texSize, int r,
            Color fill, Color border, int bw,
            bool tl, bool tr, bool bl, bool br)
        {
            int w = texSize, h = texSize;
            r = Mathf.Clamp(r, 0, w / 2 - 1);
            bw = Mathf.Clamp(bw, 1, 16);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[w * h];

            for (int py = 0; py < h; py++)
            {
                for (int px = 0; px < w; px++)
                {
                    float fx = px + 0.5f, fy = py + 0.5f;
                    // 코너 중심 — 해당 코너가 라운드 비활성이면 qx/qy=-1(직선 처리)
                    bool inTL = px < r && py < r;
                    bool inTR = px >= w - r && py < r;
                    bool inBL = px < r && py >= h - r;
                    bool inBR = px >= w - r && py >= h - r;

                    float distIn;
                    if (inTL && tl)
                        distIn = r - Vector2.Distance(new Vector2(fx, fy), new Vector2(r, r));
                    else if (inTR && tr)
                        distIn = r - Vector2.Distance(new Vector2(fx, fy), new Vector2(w - r, r));
                    else if (inBL && bl)
                        distIn = r - Vector2.Distance(new Vector2(fx, fy), new Vector2(r, h - r));
                    else if (inBR && br)
                        distIn = r - Vector2.Distance(new Vector2(fx, fy), new Vector2(w - r, h - r));
                    else
                    {
                        float dL = fx, dR = w - fx, dT = fy, dB = h - fy;
                        distIn = Mathf.Min(dL, dR, dT, dB);
                    }

                    if (distIn < 0f)
                        pixels[py * w + px] = Color.clear;
                    else if (distIn < bw + 0.8f)
                        pixels[py * w + px] = Color.Lerp(border, fill, Mathf.Clamp01(distIn - bw + 0.8f));
                    else
                        pixels[py * w + px] = fill;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D BuildRoundedTex(int texSize, int r, Color fill, Color border, int borderWidth = 2)
        {
            int w = texSize, h = texSize;
            r = Mathf.Clamp(r, 0, w / 2 - 1);
            int bw = Mathf.Clamp(borderWidth, 1, 16);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[w * h];
            for (int py = 0; py < h; py++)
            {
                for (int px = 0; px < w; px++)
                {
                    float fx = px + 0.5f, fy = py + 0.5f;
                    float qx = px < r ? r : (px >= w - r ? w - r : -1f);
                    float qy = py < r ? r : (py >= h - r ? h - r : -1f);
                    float distIn;
                    if (qx > 0 && qy > 0) distIn = r - Vector2.Distance(new Vector2(fx, fy), new Vector2(qx, qy));
                    else { float dL = fx, dR = w - fx, dT = fy, dB = h - fy; distIn = Mathf.Min(dL, dR, dT, dB); }
                    if (distIn < 0f) pixels[py * w + px] = Color.clear;
                    else if (distIn < bw + 0.8f) pixels[py * w + px] = Color.Lerp(border, fill, Mathf.Clamp01(distIn - bw + 0.8f));
                    else pixels[py * w + px] = fill;
                }
            }
            tex.SetPixels(pixels); tex.Apply(); return tex;
        }

        internal static Color WithAlpha(Color col, float a) => new Color(col.r, col.g, col.b, a);

        // =====================================================================
        //  GL 3D 오버레이 (Game View + Scene View 양쪽)
        // =====================================================================
        private void OnRenderObject()
        {
            if (_debugMode == DebugMode.Off || !_glOverlay || _ai == null) return;
            if (_debugMode == DebugMode.Focus && _pinnedAI != gameObject) return;
            var cam = Camera.current; if (cam == null) return;
            if (Vector3.Distance(cam.transform.position, transform.position) > LodIcon) return;
            EnsureGLMat(); if (_glMat == null) return;
            var s = AIDebuggerSettings.Instance;
            _glMat.SetPass(0); GL.PushMatrix(); GL.MultMatrix(Matrix4x4.identity); GL.Begin(GL.LINES);
            Vector3 base3D = transform.position + Vector3.up * 0.08f;
            if ((s == null || s.showVisionCone) && _perception != null) { var aw = GetAwareness(); Color vc = aw switch { AwarenessState.Combat => new Color(1f, 0.2f, 0.2f, 0.18f), AwarenessState.Alert => new Color(1f, 0.6f, 0.1f, 0.16f), AwarenessState.Suspicious => new Color(1f, 1f, 0.3f, 0.14f), _ => COL_VISION }; DrawGLArc(base3D, transform.forward, _perception.visionAngle, _perception.visionRange, vc, new Color(vc.r, vc.g, vc.b, vc.a * 4.5f), 20); }
            if ((s == null || s.showHearingCircle) && _perception != null) DrawGLCircle(base3D, _perception.hearingRange, COL_HEARING, COL_HEARING_BD, 40);
            if (s == null || s.showEngageRing) { float eng = GetCombatStat("EngageRange"); if (eng > 0.5f) DrawGLCircle(base3D, eng, COL_ENGAGE, new Color(COL_ENGAGE.r, COL_ENGAGE.g, COL_ENGAGE.b, 0.7f), 32); }
            if ((s == null || s.showTargetLine) && _brain?.currentTarget != null) DrawGLLine(transform.position + Vector3.up * 1.2f, _brain.currentTarget.position + Vector3.up * 1.2f, COL_TARGET);
            if (s == null || s.showGroupLines) foreach (var m in GetGroupMembers()) DrawGLLine(transform.position + Vector3.up * 0.5f, m.transform.position + Vector3.up * 0.5f, COL_GROUP);
            if ((s == null || s.showNavPath) && _nav != null && _nav.hasPath) { var corners = _nav.path.corners; Color nc = _nav.pathStatus == NavMeshPathStatus.PathPartial ? new Color(1f, 0.5f, 0.1f, 0.8f) : COL_NAV; for (int i = 0; i < corners.Length - 1; i++) DrawGLLine(corners[i], corners[i + 1], nc); }
            if (s == null || s.showMemoryMarkers) { var seen = GetBB<Vector3>("LastSeenPosition"); var heard = GetBB<Vector3>("LastHeardPosition"); if (seen != Vector3.zero) DrawGLCross(seen + Vector3.up * 0.1f, 0.35f, COL_MEMORY_S); if (heard != Vector3.zero) DrawGLCross(heard + Vector3.up * 0.1f, 0.35f, COL_MEMORY_H); }
            GL.End(); GL.PopMatrix();
        }

        private static void EnsureGLMat()
        {
            if (_glMat != null) return;
            Shader sh = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("GUI/Text Shader") ?? Shader.Find("Sprites/Default");
            if (sh == null) return;
            _glMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _glMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _glMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _glMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _glMat.SetInt("_ZWrite", 0);
            _glMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        }
        private static void DrawGLArc(Vector3 origin, Vector3 fwd, float angle, float range, Color fill, Color border, int segs = 20) { float half = angle * 0.5f; Vector3 lDir = Quaternion.Euler(0, -half, 0) * fwd, rDir = Quaternion.Euler(0, half, 0) * fwd; for (int i = 0; i < segs; i++) { Vector3 d0 = Quaternion.Euler(0, Mathf.Lerp(-half, half, (float)i / segs), 0) * fwd; Vector3 d1 = Quaternion.Euler(0, Mathf.Lerp(-half, half, (float)(i + 1) / segs), 0) * fwd; GL.Color(fill); GL.Vertex(origin); GL.Vertex(origin + d0 * range); GL.Vertex(origin + d0 * range); GL.Vertex(origin + d1 * range); } GL.Color(border); GL.Vertex(origin); GL.Vertex(origin + lDir * range); GL.Vertex(origin); GL.Vertex(origin + rDir * range); Vector3 prev = origin + lDir * range; for (int i = 1; i <= segs; i++) { Vector3 cur = origin + Quaternion.Euler(0, Mathf.Lerp(-half, half, (float)i / segs), 0) * fwd * range; GL.Vertex(prev); GL.Vertex(cur); prev = cur; } }
        private static void DrawGLCircle(Vector3 center, float radius, Color fillCol, Color borderCol, int segs = 36) { float step = 360f / segs * Mathf.Deg2Rad; Vector3 prev = center + new Vector3(radius, 0, 0); for (int i = 1; i <= segs; i++) { float ang = i * step; Vector3 cur = center + new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius); GL.Color(fillCol); GL.Vertex(center); GL.Vertex(cur); GL.Color(borderCol); GL.Vertex(prev); GL.Vertex(cur); prev = cur; } }
        private static void DrawGLLine(Vector3 a, Vector3 b, Color c) { GL.Color(c); GL.Vertex(a); GL.Vertex(b); }
        private static void DrawGLCross(Vector3 center, float size, Color color) { float s = size * 0.5f; GL.Color(color); GL.Vertex(center + new Vector3(-s, 0, 0)); GL.Vertex(center + new Vector3(s, 0, 0)); GL.Vertex(center + new Vector3(0, 0, -s)); GL.Vertex(center + new Vector3(0, 0, s)); }

#if UNITY_EDITOR
        private void OnSceneViewGUI(SceneView sv)
        {
            if (_debugMode == DebugMode.Off || _ai == null) return;
            if (_debugMode == DebugMode.Focus && _pinnedAI != gameObject) return;
            float dist = Vector3.Distance(sv.camera.transform.position, transform.position);
            if (dist > LodIcon) return;
            if (!_stylesReady) InitStyles();

            var sett = AIDebuggerSettings.Instance;
            float svScale = CalcPanelScale(dist, sett);
            bool useScale = sett?.useMatrixScale ?? true;

            // ── ① 콘텐츠 크기 계산 (단 1회) ─────────────────────────────────
            string content = BuildPanelContent(dist);
            _styleText.fontSize = sett?.fontSizeBody > 0 ? sett.fontSizeBody : 11;
            Vector2 cSize = _styleText.CalcSize(new GUIContent(content));
            float mainPW = Mathf.Max(cSize.x + 32f, 160f);
            float mainPH = cSize.y + 24f + TITLE_H;
            float sMainW = mainPW * svScale;
            float sMainH = mainPH * svScale;

            // BT Graph 표시 여부 + 예상 높이 (AI Info 아래에 배치)
            // GraphH: ParseGraph 후 ComputeLayout이 설정하는 실제 노드 트리 높이
            // + 18f(BT 타이틀 바) + 24f(여백) = 실제 렌더 높이에 가깝게 추정
            bool showBT = (sett == null || sett.showBTGraph) && _btGraphSize < 2 && _btAgent != null;
            float btGraphH = _btGraph != null ? _btGraph.GraphH : 150f;
            float btEstH = showBT
                ? Mathf.Max(btGraphH * (sett?.btGraphScale ?? 0.85f) * svScale + 42f, 80f)
                : 0f;

            // ── ② 그룹 바운딩 박스 (AI Info + BT Graph 세로 스택) ────────────
            float groupW = sMainW + 8f;
            float groupH = sMainH + (showBT ? btEstH + 4f : 0f);

            // ── ③ 캐릭터 스크린 앵커 ───────────────────────────────────────────
            // HandleUtility.WorldToGUIPoint: 해당 SceneView의 로컬 GUI 좌표 반환
            // 허리 높이(1.2f) 기준 → 줌인 시 패널이 화면 위로 튀어나가는 문제 억제
            Vector2 charPt = HandleUtility.WorldToGUIPoint(transform.position + Vector3.up * 1.2f);

            // ── ④ 자연 위치: 캐릭터 우측에 바로 스냅 (원본 방식 유지) ──────────
            // ※ 좌우 전환 로직(sv.position.width 비교)은 좌표계 불일치로 오동작 →
            //   단순히 우측 고정 후 Y만 클램프. 우측 공간 부족은 SVGroupResolver가 처리.
            float ox = sett?.snapOffsetX ?? 16f;
            float natX = charPt.x + ox;
            float natY = charPt.y - sMainH * 0.5f;   // 패널 세로 중앙을 캐릭터 Y에 맞춤
            natY = Mathf.Clamp(natY, 4f, Mathf.Max(sv.position.height - groupH - 4f, 4f));

            // ── ⑤ 다중 AI 그룹 겹침 해소 (SVGroupResolver) ────────────────────
            Vector2 origin = SVGroupResolver.Resolve(
                GetInstanceID(), new Vector2(natX, natY),
                groupW, groupH, sv.position.height);

            // 해소 후 Y만 클램프 (X는 원본처럼 우측 고정, 화면 밖으로 나가더라도 허용)
            origin.y = Mathf.Clamp(origin.y, 4f, Mathf.Max(sv.position.height - groupH - 4f, 4f));

            // ── 몹 가림 방지 (Scene View) ─────────────────────────────────
            // 캐릭터의 Scene View GUI 예상 영역과 패널 그룹이 겹치면 바깥으로 밀어냄
            if (sett?.avoidMobOcclusion ?? true)
            {
                float svMW = sett?.mobExclusionW ?? 100f;
                float svMH = sett?.mobExclusionH ?? 220f;
                // charPt = 허리 높이 기준 → 몸 중앙 rect
                Rect svMobR = new Rect(charPt.x - svMW * 0.5f, charPt.y - svMH * 0.6f, svMW, svMH);
                Rect svPanR = new Rect(origin.x, origin.y, groupW, groupH);

                for (int _smi = 0; _smi < 6; _smi++)
                {
                    if (!svPanR.Overlaps(svMobR)) break;
                    float dxS = svPanR.center.x - svMobR.center.x;
                    float dyS = svPanR.center.y - svMobR.center.y;
                    float ovXS = (svPanR.width + svMobR.width) * 0.5f - Mathf.Abs(dxS);
                    float ovYS = (svPanR.height + svMobR.height) * 0.5f - Mathf.Abs(dyS);
                    if (ovXS < ovYS)
                        svPanR.x += Mathf.Sign(dxS) * (ovXS + 4f);
                    else
                        svPanR.y += Mathf.Sign(dyS) * (ovYS + 4f);
                    svPanR.x = Mathf.Clamp(svPanR.x, 4f, sv.position.width - groupW - 4f);
                    svPanR.y = Mathf.Clamp(svPanR.y, 4f, sv.position.height - groupH - 4f);
                }
                origin = new Vector2(svPanR.x, svPanR.y);
            }

            float px = origin.x + _svInfoDragOff.x;
            float py = origin.y + _svInfoDragOff.y;
            Rect aiScaled = new Rect(px, py, sMainW, sMainH);

            // ── ⑥ IMGUI 패널 렌더링 ─────────────────────────────────────────
            Handles.BeginGUI();

            if (_debugMode != DebugMode.Minimum)
            {
                // AI Info 패널 — Scene View 전용 드래그 오프셋(_svInfoDragOff) 적용
                Matrix4x4 mat0 = GUI.matrix;
                if (useScale && svScale < 0.999f)
                    GUIUtility.ScaleAroundPivot(Vector2.one * svScale, new Vector2(px, py));
                DrawPanelTitled(new Rect(px, py, mainPW, mainPH), "AI Info", content,
                                _stylePanel, _styleText,
                                ref _svInfoDragging, ref _svInfoDragStart, ref _svInfoDragOff);
                GUI.matrix = mat0;

                // 연결선: AI Info 좌측 → 캐릭터 스크린 위치
                // sv.position.width/height 전달 → GL LoadPixelMatrix가 Scene View 좌표계와 일치
                if (sett?.showConnectorLine ?? true)
                    DrawConnectorLine(aiScaled, true, charPt, sett, GetCharColor(gameObject.name),
                                      sv.position.width, sv.position.height);

                // Char 패널 (AI Info 우측에 붙임)
                bool showChar = (sett?.showCharacterPanel ?? false) || _charPanelVisible;
                if (showChar && _charPanel != null)
                {
                    Vector2 charBase = new Vector2(aiScaled.xMax + 6f, aiScaled.y) + _charDragOff;
                    Matrix4x4 mat1 = GUI.matrix;
                    if (useScale && svScale < 0.999f)
                        GUIUtility.ScaleAroundPivot(Vector2.one * svScale, charBase);
                    _charPanel.DrawInGameView(charBase, _styleText, _stylePanel);
                    GUI.matrix = mat1;
                }

                // Group AI 패널 (AI Info 바로 아래)
                if (sett?.showGroupPanel ?? true)
                {
                    Vector2 grpBase = new Vector2(aiScaled.x, aiScaled.yMax + 4f) + _groupDragOff;
                    DrawGroupPanel(grpBase, _styleText, _stylePanel);
                }
            }

            Handles.EndGUI();

            // ── ⑦ BT Graph (DrawHandlesOverlay 내부에서 BeginGUI/EndGUI 처리) ──
            // AI Info scaled rect를 넘겨 그 바로 아래에 배치
            if (showBT)
                _btGraph.DrawHandlesOverlay(transform.position, sv, _btGraphSize,
                                            _stylePanel, dist, aiScaled);

            // ── ⑧ 3D 마커: 캐릭터 귀속 수직선 + 원 ─────────────────────────
            Color markerCol = GetCharColor(gameObject.name); markerCol.a = 0.90f;
            Color prevHC = Handles.color;
            Handles.color = markerCol;
            Handles.DrawAAPolyLine(2.5f,
                transform.position + Vector3.up * 1.0f,
                transform.position + Vector3.up * 2.2f);
            Handles.DrawWireDisc(transform.position + Vector3.up * 2.2f, Vector3.up, 0.07f);

            if (_pinnedAI == gameObject)
            {
                Handles.color = new Color(1f, 0.90f, 0.10f, 0.90f);
                Handles.DrawSolidDisc(transform.position + Vector3.up * 2.5f, Vector3.up, 0.10f);
                Handles.Label(transform.position + Vector3.up * 2.7f,
                    "<color=#FFE510>\ud83d\udccc PINNED  (F2 해제)</color>", RichLabel());
            }
            Handles.color = prevHC;
        }

        private void OnDrawGizmos()
        {
            if (_debugMode == DebugMode.Off || _ai == null) return;
            if (CamDist() > LodIcon || IsFullDetail()) return;
            DrawStateLabel(CamDist(), minimal: true);
            if (_perception != null) { Vector3 pos = transform.position + Vector3.up * 0.1f; float half = _perception.visionAngle * 0.5f; Vector3 left = Quaternion.Euler(0, -half, 0) * transform.forward, rgt = Quaternion.Euler(0, half, 0) * transform.forward; Handles.color = new Color(1f, 1f, 0.4f, 0.12f); Handles.DrawLine(pos, pos + left * _perception.visionRange); Handles.DrawLine(pos, pos + rgt * _perception.visionRange); Handles.DrawWireArc(pos, Vector3.up, left, _perception.visionAngle, _perception.visionRange); Handles.color = new Color(0.28f, 0.75f, 1f, 0.10f); Handles.DrawWireDisc(pos, Vector3.up, _perception.hearingRange); }
        }

        private void OnDrawGizmosSelected()
        {
            if (_debugMode == DebugMode.Off || _ai == null) return;
            DrawStateLabel(0f, minimal: false); DrawFearBar3D(); DrawPerceptionArc3D(); DrawHearingCircle3D(); DrawEngageRing3D(); DrawNavPath3D(); DrawMemoryMarkers3D(); DrawTargetLine3D(); DrawGroupLines3D();
            if (AIDebuggerSettings.Instance?.showUtilityBars ?? true) DrawUtilityBars3D();
            if (IsPerforming()) DrawActionIndicator3D();
            if (_pinnedAI == gameObject) { Handles.color = new Color(1f, 0.90f, 0.10f, 0.90f); Handles.DrawSolidDisc(transform.position + Vector3.up * 3.3f, Vector3.up, 0.10f); Handles.Label(transform.position + Vector3.up * 3.55f, "<color=#FFE510>📌 PINNED  (F2 해제)</color>", RichLabel()); }
        }

        /// <summary>AI 상태 레이블(Winner + 이름)을 3D 공간에 표시한다.</summary>
        private void DrawStateLabel(float dist, bool minimal)
        {
            string winner = GetWinner();
            Color c = winner switch
            {
                "Attack" => new Color(1f, 0.28f, 0.28f),
                "Flee" => new Color(0.28f, 1f, 0.42f),
                "Patrol" => new Color(0.35f, 0.60f, 1f),
                _ => Color.gray
            };
            Vector3 pos = transform.position + Vector3.up * 2.9f;
            Handles.color = c;
            Handles.DrawSolidDisc(pos, Vector3.up, minimal ? 0.06f : 0.09f);
            string label = minimal
                ? $"[{winner}] {gameObject.name}"
                : $"[{winner}] {gameObject.name}\nFear:{GetFear():F2}  {GetAwareness()}";
            Handles.Label(pos + Vector3.up * 0.18f, label);
        }
        /// <summary>Fear 수치를 3D 바 형태로 AI 머리 위에 렌더링한다.</summary>
        private void DrawFearBar3D()
        {
            float fear = GetFear();
            Vector3 camR = CamRight();
            Vector3 root = transform.position + Vector3.up * 2.4f;
            // 배경 바 (회색)
            Handles.color = new Color(0.3f, 0.3f, 0.3f, 0.45f);
            Handles.DrawAAPolyLine(5f, root, root + camR * 2.2f);
            // 값 바 (cyan→red 그라디언트)
            Handles.color = Color.Lerp(Color.cyan, Color.red, fear);
            Handles.DrawAAPolyLine(5f, root, root + camR * (fear * 2.2f));
            string state = fear > 0.7f ? "⚠ Flee 임박" : fear > 0.4f ? "경계" : "안정";
            Handles.Label(root + camR * (fear * 2.2f) + Vector3.up * 0.14f,
                $"<color=#FFA520>Fear {fear:F2}</color> {state}", RichLabel());
        }
        /// <summary>선택된 AI의 시야각 아크를 3D Handles로 렌더링한다.</summary>
        private void DrawPerceptionArc3D()
        {
            if (_perception == null) return;
            float range = _perception.visionRange, angle = _perception.visionAngle;
            var aware = GetAwareness();
            Color fill = aware switch
            {
                AwarenessState.Combat => new Color(1f, 0.20f, 0.20f, 0.22f),
                AwarenessState.Alert => new Color(1f, 0.60f, 0.10f, 0.17f),
                AwarenessState.Suspicious => new Color(1f, 1f, 0.30f, 0.14f),
                _ => new Color(0.5f, 0.5f, 0.5f, 0.08f)
            };
            Vector3 pos = transform.position + Vector3.up * 0.1f;
            Vector3 fwd = transform.forward;
            Vector3 left = Quaternion.Euler(0, -angle * 0.5f, 0) * fwd;
            Vector3 rgt = Quaternion.Euler(0, angle * 0.5f, 0) * fwd;
            Handles.color = fill;
            Handles.DrawSolidArc(pos, Vector3.up, left, angle, range);
            Handles.color = new Color(fill.r, fill.g, fill.b, Mathf.Min(fill.a * 5f, 0.8f));
            Handles.DrawWireArc(pos, Vector3.up, left, angle, range);
            Handles.DrawLine(pos, pos + left * range);
            Handles.DrawLine(pos, pos + rgt * range);
            Handles.Label(pos + fwd * range,
                $"<color=#FFEE20>시야 {range}m / {angle}°</color>\n<color=#9AA0AE>[{aware}]</color>",
                RichLabel());
        }
        /// <summary>청각 범위를 3D 원 Handles로 렌더링한다.</summary>
        private void DrawHearingCircle3D()
        {
            if (_perception == null) return;
            Handles.color = new Color(0.28f, 0.75f, 1f, 0.35f);
            Handles.DrawWireDisc(transform.position + Vector3.up * 0.06f, Vector3.up, _perception.hearingRange);
            Handles.Label(
                transform.position + Vector3.up * 0.06f
                    + CamFacingDir() * _perception.hearingRange
                    + Vector3.up * 0.22f,
                $"<color=#6EC8FF>청각 {_perception.hearingRange}m</color>  sens={_perception.hearingSensitivity:F1}",
                RichLabel());
        }
        /// <summary>EngageRange 링을 3D Handles 원으로 렌더링한다.</summary>
        private void DrawEngageRing3D()
        {
            float eng = GetCombatStat("EngageRange");
            if (eng < 0.5f) return;
            Vector3 pos = transform.position + Vector3.up * 0.04f;
            Handles.color = new Color(1f, 0.32f, 0.32f, 0.45f);
            Handles.DrawWireDisc(pos, Vector3.up, eng);
            Handles.Label(pos + CamFacingDir() * eng + Vector3.up * 0.18f,
                $"<color=#FF4545>Engage {eng:F1}m</color>", RichLabel());
        }
        /// <summary>NavMesh 경로를 3D Handles 선으로 렌더링한다.</summary>
        private void DrawNavPath3D()
        {
            if (_nav == null || !_nav.hasPath) return;
            var corners = _nav.path.corners;
            if (corners.Length < 2) return;
            bool partial = _nav.pathStatus == NavMeshPathStatus.PathPartial;
            Handles.color = partial ? new Color(1f, 0.5f, 0.1f, 0.9f) : COL_NAV;
            Handles.DrawPolyLine(corners);
            Vector3 dst = corners[corners.Length - 1];
            Handles.DrawSolidDisc(dst, Vector3.up, 0.22f);
            string nm = _brain?.currentTarget != null ? _brain.currentTarget.name : "?";
            string partialStr = partial ? "\n<color=#FFA520>⚠ PathPartial</color>" : "";
            Handles.Label(dst + Vector3.up * 0.32f,
                $"<color=#18F0BE>→ {nm}  {Vector3.Distance(transform.position, dst):F1}m</color>{partialStr}",
                RichLabel());
        }
        /// <summary>LastSeen / LastHeard 위치를 3D 마커로 렌더링한다.</summary>
        private void DrawMemoryMarkers3D()
        {
            var seen = GetBB<Vector3>("LastSeenPosition");
            var heard = GetBB<Vector3>("LastHeardPosition");
            if (seen != Vector3.zero)
            {
                Handles.color = COL_MEMORY_S;
                Handles.DrawSolidDisc(seen + Vector3.up * 0.05f, Vector3.up, 0.20f);
                Handles.Label(seen + Vector3.up * 0.40f,
                    $"<color=#DC90FF>LastSeen  {Vector3.Distance(transform.position, seen):F1}m</color>",
                    RichLabel());
            }
            if (heard != Vector3.zero)
            {
                Handles.color = COL_MEMORY_H;
                Handles.DrawSolidDisc(heard + Vector3.up * 0.05f, Vector3.up, 0.20f);
                Handles.Label(heard + Vector3.up * 0.40f,
                    $"<color=#5898FF>LastHeard {Vector3.Distance(transform.position, heard):F1}m</color>",
                    RichLabel());
            }
        }
        /// <summary>현재 타겟까지 추적선을 3D Handles로 렌더링한다.</summary>
        private void DrawTargetLine3D()
        {
            if (_brain?.currentTarget == null) return;
            Vector3 from = transform.position + Vector3.up * 1.5f;
            Vector3 to = _brain.currentTarget.position + Vector3.up * 1.5f;
            Handles.color = new Color(1f, 0.18f, 0.18f, 0.85f);
            Handles.DrawAAPolyLine(3f, from, to);
            Handles.DrawSolidDisc(to, Vector3.up, 0.18f);
            float dist = Vector3.Distance(transform.position, _brain.currentTarget.position);
            Handles.Label(Vector3.Lerp(from, to, 0.7f) + Vector3.up * 0.25f,
                $"<color=#FF2828>▶ {_brain.currentTarget.name}  {dist:F1}m</color>",
                RichLabel());
        }
        /// <summary>그룹 멤버까지 점선을 렌더링하고 멤버 수를 표시한다.</summary>
        private void DrawGroupLines3D()
        {
            var members = GetGroupMembers();
            if (members.Count == 0) return;
            Vector3 myPos = transform.position + Vector3.up * 0.6f;
            foreach (var m in members)
            {
                if (m == null) continue;
                float fade = Mathf.Lerp(0.55f, 0.15f,
                    Vector3.Distance(transform.position, m.transform.position) / GROUP_RADIUS);
                Handles.color = new Color(0.18f, 0.78f, 1f, fade);
                Handles.DrawDottedLine(myPos, m.transform.position + Vector3.up * 0.6f, 4f);
            }
            Handles.Label(transform.position + Vector3.up * 3.8f,
                $"<color=#28B8FF>GROUP  {members.Count + 1} members</color>", RichLabel());
        }
        private void DrawUtilityBars3D()
        {
            if (_brain == null) return;
            float fear = GetFear();
            Vector3 camR = CamRight(); Vector3 up = Vector3.up;
            Vector3 origin = transform.position + camR * 1.8f + up * 1.0f;
            Handles.color = new Color(0.04f, 0.04f, 0.06f, 0.82f);
            Handles.DrawSolidRectangleWithOutline(new Vector3[] { origin + up * (-0.08f) + camR * (-0.42f), origin + up * 1.12f + camR * (-0.42f), origin + up * 1.12f + camR * (1.35f + 0.22f), origin + up * (-0.08f) + camR * (1.35f + 0.22f) }, new Color(0.04f, 0.04f, 0.06f, 0.82f), new Color(0.28f, 0.76f, 1f, 0.55f));
            var lblSt = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 10, alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold }; lblSt.normal.textColor = Color.white;
            var valSt = new GUIStyle(lblSt) { alignment = TextAnchor.MiddleLeft };
            void Bar(Vector3 p, float sc, Color col, string lbl) { Handles.color = new Color(0.15f, 0.15f, 0.18f, 0.95f); Handles.DrawAAPolyLine(7f, p, p + camR * 1.35f); Handles.color = sc > 0.01f ? col : new Color(col.r, col.g, col.b, 0.2f); if (sc > 0.01f) Handles.DrawAAPolyLine(7f, p, p + camR * (sc * 1.35f)); Handles.Label(p + camR * (-0.38f) + up * 0.02f, $"<color=#E0E8FF><b>{lbl}</b></color>", lblSt); Handles.Label(p + camR * (1.35f + 0.06f) + up * 0.02f, $"<color=#FFE88A>{sc:F2}</color>", valSt); }
            Bar(origin + up * 0.9f, Mathf.Clamp01((1f - fear) * 0.8f), new Color(1f, 0.28f, 0.28f, 0.95f), "Atk");
            Bar(origin + up * 0.6f, Mathf.Clamp01(fear * 1.2f), new Color(0.28f, 1f, 0.45f, 0.95f), "Fle");
            Bar(origin + up * 0.3f, Mathf.Clamp01(0.5f - fear * 0.4f), new Color(0.35f, 0.60f, 1f, 0.95f), "Pat");
            Bar(origin, Mathf.Clamp01(0.3f - fear * 0.2f), new Color(0.75f, 0.75f, 0.78f, 0.95f), "Idl");
        }
        private void DrawActionIndicator3D() { Handles.color = new Color(1f, 0.90f, 0.10f, 0.65f); Handles.DrawWireDisc(transform.position + Vector3.up * 0.06f, Vector3.up, 0.58f); Handles.Label(transform.position + Vector3.up * 0.75f, "<color=#FFE510><b>⚡ ACTION</b></color>", RichLabel()); }
        private static GUIStyle s_richLabel;
        private static GUIStyle RichLabel() { if (s_richLabel != null) return s_richLabel; s_richLabel = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 10, alignment = TextAnchor.UpperLeft }; s_richLabel.normal.textColor = Color.white; return s_richLabel; }
        private void DrawSceneUtilityBarsGUI(Vector2 topLeft, float fear)
        {
            if (!_stylesReady || _stylePanel == null) return;
            float panelW = 160f, barH = 14f, pad = 6f, panelH = 4 * (barH + 2f) + pad * 2f;
            Rect panel = new Rect(topLeft.x, topLeft.y, panelW, panelH);
            GUI.Box(panel, GUIContent.none, _stylePanel);
            float[] scores = { Mathf.Clamp01((1f - fear) * 0.8f), Mathf.Clamp01(fear * 1.2f), Mathf.Clamp01(0.5f - fear * 0.4f), Mathf.Clamp01(0.3f - fear * 0.2f) };
            string[] names = { "Atk", "Fle", "Pat", "Idl" };
            Color[] cols = { new Color(1f, 0.28f, 0.28f, 0.95f), new Color(0.28f, 1f, 0.45f, 0.95f), new Color(0.35f, 0.60f, 1f, 0.95f), new Color(0.75f, 0.75f, 0.78f, 0.95f) };
            var lblSt = new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.MiddleLeft }; lblSt.normal.textColor = new Color(0.8f, 0.88f, 1f);
            var valSt = new GUIStyle(lblSt) { alignment = TextAnchor.MiddleRight }; valSt.normal.textColor = new Color(1f, 0.93f, 0.6f);
            for (int k = 0; k < 4; k++) { float y = panel.y + pad + k * (barH + 2f); float lw = 30f, bw = panelW - lw - pad * 2f - 28f; GUI.Label(new Rect(panel.x + pad, y, lw, barH), names[k], lblSt); Color prev = GUI.color; GUI.color = new Color(0.15f, 0.15f, 0.18f, 0.9f); GUI.DrawTexture(new Rect(panel.x + pad + lw, y + barH * 0.25f, bw, barH * 0.5f), Texture2D.whiteTexture); GUI.color = cols[k]; float fillW = Mathf.Max(bw * scores[k], 0f); if (fillW > 0.5f) GUI.DrawTexture(new Rect(panel.x + pad + lw, y + barH * 0.25f, fillW, barH * 0.5f), Texture2D.whiteTexture); GUI.color = prev; GUI.Label(new Rect(panel.x + pad + lw + bw + 2f, y, 26f, barH), $"{scores[k]:F2}", valSt); }
        }
#endif  // UNITY_EDITOR

        // =====================================================================
        //  ■ 내부 클래스 1: BTGraphRenderer  (F-1)
        // =====================================================================
        // Utility Score Reflection 캐시 (MobAIBrain private 필드)
        private static FieldInfo s_uAtk, s_uFlee, s_uIdle, s_uPat;

        private class BTGraphRenderer
        {
            private const string GRAPHS_FIELD = "Graphs";
            private const string NODE_NAME_PROP = "Name";
            private static FieldInfo s_graphsField;
            private static PropertyInfo s_nodeNameProp;
            private static FieldInfo s_rootField;

            private struct BTNodeInfo { public Node Node; public int Depth; public string DisplayName; public bool IsComposite; }
            private readonly List<BTNodeInfo> _nodes = new();
            private BehaviorGraph _cachedGraph;
            private BehaviorGraphAgent _cachedAgent;

            private const float NODE_W = 130f, NODE_H = 18f, NODE_GAP = 3f, INDENT = 12f, COMPACT_W = 70f;
            private const float GV_NODE_W_DEF = 128f, GV_NODE_H_DEF = 20f;
            private const float GV_H_GAP = 8f, GV_V_GAP = 24f;
            // 설정값 우선, 없으면 기본값
            private static float GV_NODE_W => AIDebuggerSettings.Instance?.btNodeWidth > 0 ? AIDebuggerSettings.Instance.btNodeWidth : GV_NODE_W_DEF;
            private static float GV_NODE_H => AIDebuggerSettings.Instance?.btNodeHeight > 0 ? AIDebuggerSettings.Instance.btNodeHeight : GV_NODE_H_DEF;

            private Vector2[] _nodePos;
            private float _graphW, _graphH;
            public float GraphW => _graphW;
            public float GraphH => _graphH;
            private bool _layoutDirty = true;
            private float _cachedVGap = -1f, _cachedHGap = -1f;
            private Texture2D _nodeTex;
            private int _lastNodeCR = -1;
            private Texture2D _btPanelTex;        // BT 패널 배경 (공통 라운드)
            private int _btLastCR = -1;
            private int _btLastBW = -1;
            // BT 그래프 패널 드래그 상태
            public bool BTDragging;
            public Vector2 BTDragStart, BTDragOff;
            private Node.Status[] _prevStatuses;
            private float[] _nodeFlashEndTimes;

            public void InvalidateLayout() { _layoutDirty = true; }

            private static Color NodeBg(BTNodeInfo info, Node.Status st)
            {
                var s = AIDebuggerSettings.Instance;
                float op = s?.nodeOpacity ?? 0.92f;
                bool isLight = (s?.theme ?? AIDebuggerSettings.BTTheme.Dark) == AIDebuggerSettings.BTTheme.Light;
                Color col;
                if (isLight)
                {
                    col = st switch
                    {
                        Node.Status.Running => new Color(1.00f, 0.92f, 0.30f, 1f),
                        Node.Status.Waiting => new Color(0.68f, 0.82f, 1.00f, 1f),
                        Node.Status.Success => new Color(0.60f, 0.95f, 0.68f, 1f),
                        Node.Status.Failure => new Color(1.00f, 0.65f, 0.65f, 1f),
                        _ => info.IsComposite ? new Color(0.72f, 0.80f, 0.96f, 1f) : new Color(0.84f, 0.87f, 0.92f, 1f)
                    };
                }
                else
                {
                    col = st switch
                    {
                        Node.Status.Running => s?.btColorNodeRunning ?? new Color(0.20f, 0.15f, 0.04f, 1f),
                        Node.Status.Waiting => s?.btColorNodeWaiting ?? new Color(0.06f, 0.10f, 0.22f, 1f),
                        Node.Status.Success => s?.btColorNodeSuccess ?? new Color(0.06f, 0.18f, 0.09f, 1f),
                        Node.Status.Failure => s?.btColorNodeFailure ?? new Color(0.22f, 0.06f, 0.06f, 1f),
                        _ => info.IsComposite ? (s?.btColorNodeComposite ?? new Color(0.08f, 0.11f, 0.22f, 1f)) : (s?.btColorNodeDefault ?? new Color(0.10f, 0.12f, 0.18f, 1f))
                    };
                }
                return AIPerceptionDebugger.WithAlpha(col, col.a * op);
            }

            private static Color NodeBorder(Node.Status st)
            {
                bool isLight = (AIDebuggerSettings.Instance?.theme ?? AIDebuggerSettings.BTTheme.Dark) == AIDebuggerSettings.BTTheme.Light;
                if (isLight) return st switch { Node.Status.Running => new Color(0.80f, 0.60f, 0.00f, 1f), Node.Status.Waiting => new Color(0.10f, 0.45f, 0.90f, 1f), Node.Status.Success => new Color(0.05f, 0.60f, 0.20f, 1f), Node.Status.Failure => new Color(0.80f, 0.10f, 0.10f, 1f), _ => new Color(0.30f, 0.40f, 0.60f, 0.9f) };
                return st switch { Node.Status.Running => new Color(1f, 0.90f, 0.10f, 1f), Node.Status.Waiting => new Color(0.28f, 0.75f, 1f, 1f), Node.Status.Success => new Color(0.28f, 1f, 0.45f, 1f), Node.Status.Failure => new Color(1f, 0.28f, 0.28f, 1f), _ => new Color(0.28f, 0.40f, 0.55f, 0.8f) };
            }
            private static string GetStatusIcon(Node.Status st) => st switch { Node.Status.Running => "⚡", Node.Status.Waiting => "⏳", Node.Status.Success => "✅", Node.Status.Failure => "❌", _ => "○" };

            public void ParseGraph(BehaviorGraphAgent agent)
            {
                _nodes.Clear();
                if (agent?.Graph == null) return;
                _cachedGraph = agent.Graph; _cachedAgent = agent;
                if (s_graphsField == null) { s_graphsField = typeof(BehaviorGraph).GetField(GRAPHS_FIELD, BindingFlags.Instance | BindingFlags.NonPublic); if (s_graphsField == null) return; }
                if (s_nodeNameProp == null) s_nodeNameProp = typeof(NodeDescriptionAttribute).GetProperty(NODE_NAME_PROP, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                try
                {
                    var graphsList = s_graphsField.GetValue(agent.Graph) as System.Collections.IList;
                    if (graphsList == null || graphsList.Count == 0) return;
                    object firstModule = graphsList[0]; if (firstModule == null) return;
                    if (s_rootField == null) s_rootField = firstModule.GetType().GetField("Root", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    Node root = s_rootField?.GetValue(firstModule) as Node;
                    if (root != null) { TraverseDFS(root, 0); Debug.Log($"[BTGraph] Parsed {_nodes.Count} nodes"); InvalidateLayout(); }
                }
                catch (Exception e) { Debug.LogWarning($"[BTGraphRenderer] 파싱 실패: {e.Message}"); }
            }
            public bool NeedsReParse(BehaviorGraphAgent agent) => agent?.Graph != null && agent.Graph != _cachedGraph;

            private static readonly Dictionary<Type, FieldInfo[]> s_childFieldsCache = new();
            private void TraverseDFS(Node node, int depth)
            {
                if (node == null || depth > 24) return;
                for (int i = 0; i < _nodes.Count; i++) if (_nodes[i].Node == node) return;
                _nodes.Add(new BTNodeInfo { Node = node, Depth = depth, DisplayName = GetNodeName(node), IsComposite = node is Composite });
                if (node is Composite comp) { foreach (var child in comp.Children) TraverseDFS(child, depth + 1); }
                else { foreach (var child in GetNonCompositeChildren(node)) TraverseDFS(child, depth + 1); }
            }
            private static IEnumerable<Node> GetNonCompositeChildren(Node node)
            {
                var t = node.GetType();
                if (!s_childFieldsCache.TryGetValue(t, out var childFields))
                {
                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    var nodeType = typeof(Node); var found = new List<FieldInfo>();
                    foreach (var fi in t.GetFields(flags))
                    {
                        if (!nodeType.IsAssignableFrom(fi.FieldType)) continue;
                        if (fi.Name == "m_Parent" || fi.Name == "Parent" || fi.Name == "m_Observer" || fi.Name == "Observer" || fi.Name == "m_Graph" || fi.Name == "Graph") continue;
                        if (fi.FieldType == typeof(BehaviorGraphAgent)) continue;
                        found.Add(fi);
                    }
                    childFields = found.ToArray(); s_childFieldsCache[t] = childFields;
                }
                foreach (var fi in childFields) { if (fi.GetValue(node) is Node child && child != null) yield return child; }
            }
            private static string GetNodeName(Node node)
            {
                try { var attr = node.GetType().GetCustomAttribute<NodeDescriptionAttribute>(); if (attr != null && s_nodeNameProp != null) { string n = s_nodeNameProp.GetValue(attr) as string; if (!string.IsNullOrEmpty(n)) return n; } } catch { }
                return node.GetType().Name.Replace("Action", "").Replace("Composite", "").Replace("Node", "");
            }

            // ── BT Graph 렌더링 ────────────────────────────────────────────────
            private SceneView _cachedSV;
            private static Material s_btGLMat;
            private static bool EnsureBTGLMat() { if (s_btGLMat != null) return true; var sh = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default"); if (sh == null) return false; s_btGLMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave }; s_btGLMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); s_btGLMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); s_btGLMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off); s_btGLMat.SetInt("_ZWrite", 0); s_btGLMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always); return true; }

            public void DrawBTGraph(Rect mainPanel, int sizeMode, float dist, bool isSceneView, GUIStyle panelStyle, AIDebuggerSettings sett, float distScale = 1f)
            {
                if (_nodes.Count == 0) return;
                float lodIcon = sett?.lodIconDist ?? 40f, lodHeader = sett?.lodHeaderDist ?? 25f, lodSummary = sett?.lodSummaryDist ?? 14f;
                if (dist > lodIcon) return;
                float baseScale = sett?.btGraphScale ?? 0.85f;
                // distScale: 거리 기반 스케일 (CalcPanelScale 결과) — LOD 배율 대체
                float scale = baseScale * (sizeMode == 1 ? 0.6f : 1f) * distScale;
                var mode = sett?.btGraphDisplayMode ?? AIDebuggerSettings.BTGraphDisplayMode.NodeGraph;
                bool compactText = dist > lodHeader;
                float curVGap = GetVGap(), curHGap = GetHGap();
                if (_cachedVGap != curVGap || _cachedHGap != curHGap) { _cachedVGap = curVGap; _cachedHGap = curHGap; _layoutDirty = true; }
                if (_layoutDirty || _nodePos == null || _nodePos.Length != _nodes.Count) { ComputeLayout(); _layoutDirty = false; }
                if (Event.current.type == EventType.Repaint) UpdateFlashState();
                float graphTotalW = _graphW * scale + 20f, graphTotalH = _graphH * scale + 20f;
                Vector2 origin;
                if (isSceneView) origin = new Vector2(mainPanel.x, mainPanel.y);
                else
                {
                    float screenW = Screen.width; bool goLeft = false;
                    var anchor = sett?.btGraphAnchor ?? AIDebuggerSettings.BTGraphAnchor.Auto;
                    if (anchor == AIDebuggerSettings.BTGraphAnchor.Left) goLeft = true;
                    else if (anchor == AIDebuggerSettings.BTGraphAnchor.Auto)
                    {
                        // 캐릭터 패널이 우측일 경우 그 너머로 BT Graph 배치
                        bool charOnRight = (sett?.charPanelAnchor
                            ?? AIDebuggerSettings.CharPanelAnchor.RightOfMain)
                            == AIDebuggerSettings.CharPanelAnchor.RightOfMain;
                        // charW도 현재 scale 반영 (AI Info 패널과 거리 일정하게 유지)
                        float charW = charOnRight ? 160f * scale : 0f;
                        float rightEdge = mainPanel.xMax + (charOnRight ? charW + 10f : 0f);
                        goLeft = (rightEdge + graphTotalW + 10f) > screenW;
                    }
                    origin = goLeft
                        ? new Vector2(mainPanel.x - graphTotalW - 8f, mainPanel.y)
                        : new Vector2(mainPanel.xMax + (((sett?.charPanelAnchor
                              ?? AIDebuggerSettings.CharPanelAnchor.RightOfMain)
                              == AIDebuggerSettings.CharPanelAnchor.RightOfMain)
                              ? 170f : 8f),
                              mainPanel.y);
                }
                // BT Graph 위치 오프셋 (설정값 + 드래그 오프셋)
                origin += new Vector2(sett?.btPanelOffset.x ?? 0f, sett?.btPanelOffset.y ?? 0f);
                origin += BTDragOff;  // 타이틀 드래그 오프셋
                Rect panel = new Rect(origin.x, origin.y, graphTotalW, graphTotalH);
                bool isLight = (sett?.theme ?? AIDebuggerSettings.BTTheme.Dark) == AIDebuggerSettings.BTTheme.Light;
                float opBG = sett?.panelOpacity ?? 0.90f;
                // ── BT 그래프 패널 배경 (공통 cornerRadius/borderWidth 적용) ──
                Color _bgFill = isLight
                    ? (sett != null ? AIPerceptionDebugger.WithAlpha(sett.panelBgColorLight, opBG) : new Color(0.88f, 0.90f, 0.94f, opBG))
                    : (sett != null ? AIPerceptionDebugger.WithAlpha(sett.panelBgColor, opBG) : new Color(0.05f, 0.07f, 0.11f, opBG));
                Color _bdFill = isLight
                    ? (sett != null ? AIPerceptionDebugger.WithAlpha(sett.panelBdColorLight, 0.7f * opBG) : new Color(0.22f, 0.50f, 0.85f, 0.7f * opBG))
                    : (sett != null ? AIPerceptionDebugger.WithAlpha(sett.panelBdColor, 0.7f * opBG) : new Color(0.28f, 0.70f, 1.00f, 0.7f * opBG));
                int _btCR2 = Mathf.RoundToInt((sett?.panelCornerRadius ?? 8) / 15f * 120f);
                int _btBW2 = sett?.panelBorderWidth ?? 2;
                if (_btPanelTex == null || _btLastCR != _btCR2 || _btLastBW != _btBW2)
                {
                    if (_btPanelTex != null) UnityEngine.Object.Destroy(_btPanelTex);
                    _btPanelTex = AIPerceptionDebugger.BuildRoundedTexStatic(256, _btCR2, _bgFill, _bdFill, _btBW2);
                    _btLastCR = _btCR2; _btLastBW = _btBW2;
                }
                var _btSt = new GUIStyle { normal = { background = _btPanelTex }, border = new RectOffset(20, 20, 20, 20) };
                Color prevBG = GUI.color; GUI.color = Color.white;
                GUI.Box(panel, GUIContent.none, _btSt);
                GUI.color = prevBG;
                // ── BT Graph 타이틀 바 ──────────────────────────────────────
                float btTitleH = 18f;
                int runCount = 0;
                for (int i = 0; i < _nodes.Count; i++)
                    if (_nodes[i].Node.CurrentStatus == Node.Status.Running ||
                        _nodes[i].Node.CurrentStatus == Node.Status.Waiting) runCount++;
                string hdrText = $"BT Graph  [{runCount} running]";

                // 타이틀 바 배경 (상단 라운드 코너)
                int _btTitleCR = Mathf.RoundToInt((sett?.panelCornerRadius ?? 8) / 15f * 120f);
                Color _btTitleBg = new Color(0.10f, 0.42f, 0.72f, (sett?.panelOpacity ?? 0.90f) * 0.95f);
                Color _btBdCol2 = isLight ? (sett?.panelBdColorLight ?? Color.blue) : (sett?.panelBdColor ?? new Color(0.28f, 0.76f, 1f));
                // 간단하게: GUI.DrawTexture로 타이틀 영역 채움
                Rect btTitleBar = new Rect(panel.x, panel.y, panel.width, btTitleH);
                Color prevBT = GUI.color;
                GUI.color = _btTitleBg;
                GUI.DrawTexture(btTitleBar, Texture2D.whiteTexture);
                GUI.color = prevBT;

                var hdrSt = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(Mathf.RoundToInt(9f * scale), 8),
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                };
                hdrSt.normal.textColor = new Color(0.85f, 0.95f, 1f);
                float titleMargin = Mathf.Max(_btTitleCR / 4f, 5f);
                GUI.Label(new Rect(panel.x + titleMargin, panel.y, panel.width - titleMargin * 2, btTitleH),
                          hdrText, hdrSt);

                // BT Graph 타이틀 바 드래그 (Instance 메서드로 직접 처리)
                var _btEv = Event.current; Vector2 _btMp = _btEv.mousePosition;
                if (_btEv.type == EventType.MouseDown && _btEv.button == 0 && btTitleBar.Contains(_btMp))
                { BTDragging = true; BTDragStart = _btMp - BTDragOff; _btEv.Use(); }
                else if (_btEv.type == EventType.MouseDrag && _btEv.button == 0 && BTDragging)
                { BTDragOff = _btMp - BTDragStart; _btEv.Use(); }
                else if (_btEv.type == EventType.MouseUp && BTDragging)
                { BTDragging = false; _btEv.Use(); }

                Vector2 contentOrigin = new Vector2(panel.x + 8f, panel.y + btTitleH + 2f);
                float viewW = isSceneView && _cachedSV != null ? _cachedSV.position.width : Screen.width;
                float viewH = isSceneView && _cachedSV != null ? _cachedSV.position.height : Screen.height;
                var parentMap = BuildParentMap();
                // Scene View: IMGUI y=0은 툴바 아래이지만 GL y=0은 창 상단
                // → 차이만큼 보정하여 GL 화살표가 노드 박스와 정확히 스냅되도록 함
                float svYOff = 0f;
#if UNITY_EDITOR
                if (isSceneView && _cachedSV != null && _cachedSV.camera != null)
                {
                    // camera.pixelHeight = 물리 픽셀, position.height = 논리 단위(포인트)
                    // pixelsPerPoint로 나눠 단위를 맞춘 뒤 툴바 높이 계산
                    float ppp = UnityEditor.EditorGUIUtility.pixelsPerPoint;
                    float contentH = _cachedSV.camera.pixelHeight / ppp;
                    svYOff = Mathf.Max(0f, _cachedSV.position.height - contentH);
                    // contentH 이 0이면 기본값 사용
                    if (svYOff == 0f || contentH <= 0f)
                        svYOff = UnityEditor.EditorStyles.toolbar.fixedHeight;
                }
#endif
                // GL.Viewport를 전체 창으로 설정했으므로 좌표 보정 불필요
                // (Viewport fix가 작동하면 svYOff=0, 아니면 svYOff가 보정)
                if (mode == AIDebuggerSettings.BTGraphDisplayMode.NodeGraph)
                    DrawNodeGraph(contentOrigin, scale, compactText, viewW, viewH, parentMap, panel.yMax, svYOff);
                else DrawTextTree(contentOrigin, scale);
            }

            private void DrawNodeGraph(Vector2 origin, float scale, bool compact, float viewW, float viewH, Dictionary<int, int> parentMap, float panelBottomY = float.MaxValue, float svYOff = 0f)
            {
                DrawConnectionsGL(origin, scale, parentMap, viewW, viewH, svYOff);
                DrawNodeBoxes(origin, scale, compact, panelBottomY);
            }

            private void DrawConnectionsGL(Vector2 origin, float scale, Dictionary<int, int> parentMap, float viewW, float viewH, float svYOff = 0f)
            {
                if (Event.current.type != EventType.Repaint) return;
                if (!EnsureBTGLMat()) return;
                float nw = GV_NODE_W * scale, nh = GV_NODE_H * scale;
                s_btGLMat.SetPass(0); GL.PushMatrix();
                // Scene View: IMGUI y=0은 툴바 아래, GL LoadPixelMatrix는 창 전체 기준
                // svYOff(툴바 높이)를 각 GL 정점 Y에 더해 보정 (Viewport 덮어쓰기 불필요)
                float _glH = (svYOff > 0f) ? viewH : viewH;
                GL.LoadPixelMatrix(0, viewW, _glH, 0);
                GL.Begin(GL.QUADS);
                foreach (var kv in parentMap)
                {
                    int i = kv.Key, pi = kv.Value;
                    float arrH = 9f * Mathf.Max(scale, 0.5f), arrHW = 6f * Mathf.Max(scale, 0.5f);
                    Vector2 from = origin + _nodePos[pi] * scale + new Vector2(nw * 0.5f, nh);
                    Vector2 toBox = origin + _nodePos[i] * scale + new Vector2(nw * 0.5f, 0f);
                    Vector2 to = toBox - new Vector2(0f, arrH);
                    float dy = Mathf.Abs(toBox.y - from.y);
                    float vGap = Mathf.Max(dy * 0.5f, 14f * Mathf.Max(scale, 0.5f));
                    Vector2 cp1 = new Vector2(from.x, from.y + vGap), cp2 = new Vector2(to.x, to.y - vGap);
                    Node.Status pSt = _nodes[pi].Node.CurrentStatus, cSt = _nodes[i].Node.CurrentStatus;
                    bool active = (pSt == Node.Status.Running || pSt == Node.Status.Waiting) && (cSt == Node.Status.Running || cSt == Node.Status.Waiting);
                    var _sett = AIDebuggerSettings.Instance;
                    float _lineOp = _sett?.lineOpacity ?? 0.85f;
                    bool _lmLine = (_sett?.theme ?? AIDebuggerSettings.BTTheme.Dark) == AIDebuggerSettings.BTTheme.Light;
                    Color _defA = _lmLine ? new Color(0.75f, 0.40f, 0.00f, 1f) : new Color(1f, 0.88f, 0.08f, 1f);
                    Color _defI = _lmLine ? new Color(0.15f, 0.35f, 0.70f, 1f) : new Color(0.22f, 0.42f, 0.62f, 1f);
                    Color lineCol = active ? AIPerceptionDebugger.WithAlpha(_sett?.btColorLineActive ?? _defA, _lineOp) : AIPerceptionDebugger.WithAlpha(_sett?.btColorLineInactive ?? _defI, _lineOp * 0.6f);
                    float lw = (active ? 2.5f : 1.5f) * Mathf.Max(scale, 0.5f);
                    // Flash
                    Color drawCol = lineCol;
                    if (active && _nodeFlashEndTimes != null && i < _nodeFlashEndTimes.Length) { float rem = _nodeFlashEndTimes[i] - Time.realtimeSinceStartup; if (rem > 0f) { float flash = Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 18f)); drawCol = Color.Lerp(lineCol, Color.white, (rem / 0.6f) * flash); } }
                    DrawBezierGL50(from, to, cp1, cp2, drawCol, lw, svYOff);
                    // Bubbles
                    if (active)
                    {
                        float splineLen = EstimateSplineLength(from, to, cp1, cp2, 12);
                        float baseSpeed = _sett?.bubbleSpeed ?? 0.65f;
                        float speed = (_sett?.bubbleSpeedMode ?? AIDebuggerSettings.BubbleSpeedMode.Fixed) switch { AIDebuggerSettings.BubbleSpeedMode.Normalized => baseSpeed * (300f / Mathf.Max(splineLen, 50f)), _ => baseSpeed };
                        float baseT = (Time.realtimeSinceStartup * speed) % 1f;
                        float pulseA = (0.55f + Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 5f)) * 0.45f) * (_sett?.bubbleOpacity ?? 0.85f);
                        bool _lmBub = (_sett?.theme ?? AIDebuggerSettings.BTTheme.Dark) == AIDebuggerSettings.BTTheme.Light;
                        Color _defBub = _lmBub ? new Color(0.65f, 0.30f, 0.00f, 1f) : new Color(1f, 0.97f, 0.55f, 1f);
                        Color bCol = _sett != null ? AIPerceptionDebugger.WithAlpha(_sett.btColorBubble, pulseA) : AIPerceptionDebugger.WithAlpha(_defBub, pulseA);
                        float br = (_sett?.btBubbleSize ?? 3.8f) * Mathf.Max(scale, 0.5f);
                        int bCount = Mathf.Clamp(Mathf.RoundToInt(splineLen / 100f * (_sett?.bubbleCountPer100px ?? 4)), 2, 12);
                        float step = 1f / bCount;
                        float depthFactor = _nodes[pi].Depth / Mathf.Max(_nodes.Count * 0.2f, 1f);
                        var bMode = _sett?.bubbleSpeedMode ?? AIDebuggerSettings.BubbleSpeedMode.Fixed;
                        for (int b = 0; b < bCount; b++)
                        {
                            float rawT = (baseT + b * step) % 1f;
                            float t = bMode switch { AIDebuggerSettings.BubbleSpeedMode.Accelerate => Mathf.Pow(rawT, Mathf.Max(1f - depthFactor * 0.6f, 0.2f)), AIDebuggerSettings.BubbleSpeedMode.EaseIn => rawT * rawT, _ => rawT };
                            Vector2 bpos = EvalBezier(from, to, cp1, cp2, t);
                            GL.Color(bCol);
                            GL.Vertex3(bpos.x, bpos.y - br + svYOff, 0f);
                            GL.Vertex3(bpos.x + br, bpos.y + svYOff, 0f);
                            GL.Vertex3(bpos.x, bpos.y + br + svYOff, 0f);
                            GL.Vertex3(bpos.x - br, bpos.y + svYOff, 0f);
                        }
                    }
                    GL.End(); GL.Begin(GL.TRIANGLES); GL.Color(drawCol);
                    GL.Vertex3(toBox.x, toBox.y + svYOff, 0f);
                    GL.Vertex3(toBox.x - arrHW, toBox.y - arrH + svYOff, 0f);
                    GL.Vertex3(toBox.x + arrHW, toBox.y - arrH + svYOff, 0f);
                    GL.End(); GL.Begin(GL.QUADS);
                }
                GL.End(); GL.PopMatrix();
            }

            private static void DrawBezierGL50(Vector2 p0, Vector2 p3, Vector2 cp1, Vector2 cp2, Color col, float width, float yOff = 0f)
            {
                const int SEGS = 50; Vector2 prev = p0; float hw = width * 0.5f;
                for (int s = 1; s <= SEGS; s++)
                {
                    float t = s / (float)SEGS, u = 1f - t;
                    Vector2 cur = u * u * u * p0 + 3f * u * u * t * cp1 + 3f * u * t * t * cp2 + t * t * t * p3;
                    Vector2 d = cur - prev; float dLen = d.magnitude;
                    if (dLen > 0.01f)
                    {
                        Vector2 perp = new Vector2(-d.y, d.x) * (hw / dLen);
                        GL.Color(col);
                        GL.Vertex3(prev.x + perp.x, prev.y + perp.y + yOff, 0f);
                        GL.Vertex3(prev.x - perp.x, prev.y - perp.y + yOff, 0f);
                        GL.Vertex3(cur.x - perp.x, cur.y - perp.y + yOff, 0f);
                        GL.Vertex3(cur.x + perp.x, cur.y + perp.y + yOff, 0f);
                    }
                    prev = cur;
                }
            }

            private static Vector2 EvalBezier(Vector2 p0, Vector2 p3, Vector2 cp1, Vector2 cp2, float t) { float u = 1f - t; return u * u * u * p0 + 3f * u * u * t * cp1 + 3f * u * t * t * cp2 + t * t * t * p3; }
            private static float EstimateSplineLength(Vector2 p0, Vector2 p3, Vector2 cp1, Vector2 cp2, int segs) { float len = 0f; Vector2 prev = p0; for (int s = 1; s <= segs; s++) { Vector2 cur = EvalBezier(p0, p3, cp1, cp2, s / (float)segs); len += (cur - prev).magnitude; prev = cur; } return len; }

            private void UpdateFlashState()
            {
                if (_prevStatuses == null || _prevStatuses.Length != _nodes.Count) { _prevStatuses = new Node.Status[_nodes.Count]; _nodeFlashEndTimes = new float[_nodes.Count]; for (int k = 0; k < _nodes.Count; k++) _prevStatuses[k] = _nodes[k].Node.CurrentStatus; return; }
                for (int k = 0; k < _nodes.Count; k++) { var cur = _nodes[k].Node.CurrentStatus; if (cur != _prevStatuses[k] && cur == Node.Status.Running) _nodeFlashEndTimes[k] = Time.realtimeSinceStartup + 0.5f; _prevStatuses[k] = cur; }
            }

            private void DrawNodeBoxes(Vector2 origin, float scale, bool compact, float panelBottomY = float.MaxValue)
            {
                float nw = GV_NODE_W * scale, nh = GV_NODE_H * scale;
                for (int i = 0; i < _nodes.Count; i++)
                {
                    var info = _nodes[i]; Node.Status st = info.Node.CurrentStatus;
                    Rect box = new Rect(origin.x + _nodePos[i].x * scale, origin.y + _nodePos[i].y * scale, nw, nh);
                    if (st == Node.Status.Running) { float t2 = Time.realtimeSinceStartup * 4f; float pulse = (Mathf.Sin(t2) + 1f) * 0.12f + 0.08f; var pc = GUI.color; GUI.color = new Color(1f, 0.88f, 0.08f, pulse); GUI.DrawTexture(new Rect(box.x - 4, box.y - 4, nw + 8, nh + 8), Texture2D.whiteTexture); GUI.color = pc; }
                    var prev = GUI.color;
                    // Rounded node bg
                    int _ncr = Mathf.RoundToInt((AIDebuggerSettings.Instance?.panelCornerRadius ?? 8) / 15f * 40f);
                    if (_nodeTex == null || _lastNodeCR != _ncr) { if (_nodeTex != null) UnityEngine.Object.Destroy(_nodeTex); _nodeTex = AIPerceptionDebugger.BuildRoundedTexStatic(64, _ncr, Color.white, Color.white, 0); _lastNodeCR = _ncr; }
                    GUI.color = NodeBg(info, st); GUI.DrawTexture(box, _nodeTex); GUI.color = prev;
                    GUI.color = NodeBorder(st); GUI.DrawTexture(new Rect(box.x, box.y, 3f, nh), Texture2D.whiteTexture); GUI.color = prev;
                    if (!compact)
                    {
                        string icon = GetStatusIcon(st);
                        var _sett_fs = AIDebuggerSettings.Instance; int _btNodeFS = _sett_fs?.fontSizeBTNode > 0 ? _sett_fs.fontSizeBTNode : 0;
                        int fs = _btNodeFS > 0 ? Mathf.Max(Mathf.RoundToInt(_btNodeFS * scale), 6) : Mathf.Max(Mathf.RoundToInt(8.5f * scale), 6);
                        int maxCh = Mathf.RoundToInt(13f * Mathf.Max(scale, 0.5f));
                        string label = $"{icon} {Truncate(info.DisplayName, maxCh)}";
                        var ns = new GUIStyle(GUI.skin.label) { fontSize = fs, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
                        bool _isLight = (AIDebuggerSettings.Instance?.theme ?? AIDebuggerSettings.BTTheme.Dark) == AIDebuggerSettings.BTTheme.Light;
                        ns.normal.textColor = st == Node.Status.Running
                            ? (_isLight ? new Color(0.35f, 0.20f, 0.00f) : (AIDebuggerSettings.Instance?.btColorTextRunning ?? new Color(1f, 0.95f, 0.35f)))
                            : (_isLight ? new Color(0.10f, 0.12f, 0.20f) : (AIDebuggerSettings.Instance?.btColorTextNormal ?? new Color(0.88f, 0.88f, 0.88f)));
                        GUI.Label(new Rect(box.x + 5, box.y, nw - 5, nh), label, ns);
                        // BB values
                        var sett2 = AIDebuggerSettings.Instance;
                        if ((sett2 == null || sett2.showBTConditionValues) && _cachedAgent != null)
                        {
                            string bbInfo = GetNodeBBValues(info.Node, _cachedAgent);
                            if (!string.IsNullOrEmpty(bbInfo))
                            {
                                int bbFs = Mathf.Max(Mathf.RoundToInt(8f * Mathf.Max(scale, 0.6f)), 6);
                                float lineH = bbFs + 4f;
                                string[] bbLines = bbInfo.Split('\n');
                                int numLines = Mathf.Min(bbLines.Length, 3);
                                float bbH = numLines * lineH + 4f;
                                if (box.yMax + bbH <= panelBottomY)
                                {
                                    Rect bbBox = new Rect(box.x, box.yMax, nw, bbH);
                                    Color prevC = GUI.color;
                                    bool _bbLight = (sett2?.theme ?? AIDebuggerSettings.BTTheme.Dark) == AIDebuggerSettings.BTTheme.Light;
                                    GUI.color = _bbLight ? new Color(0.70f, 0.80f, 0.95f, 0.92f) : new Color(0.04f, 0.07f, 0.14f, 0.92f);
                                    GUI.DrawTexture(bbBox, Texture2D.whiteTexture);
                                    GUI.color = new Color(0.28f, 0.75f, 1f, 0.85f); GUI.DrawTexture(new Rect(bbBox.x, bbBox.y, 2.5f, bbH), Texture2D.whiteTexture);
                                    GUI.color = prevC;
                                    var bbSt = new GUIStyle(GUI.skin.label) { fontSize = bbFs, alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip, wordWrap = false, richText = true };
                                    bool _bbLightTxt = _bbLight; bbSt.normal.textColor = _bbLightTxt ? new Color(0.05f, 0.15f, 0.45f) : Color.white;
                                    for (int ln = 0; ln < numLines; ln++) GUI.Label(new Rect(bbBox.x + 5, bbBox.y + ln * lineH + 2, bbBox.width - 6, lineH), bbLines[ln], bbSt);
                                }
                            }
                        }
                    }
                    else { string icon = GetStatusIcon(st); int fs = Mathf.Max(Mathf.RoundToInt(7f * scale), 5); var ns = new GUIStyle(GUI.skin.label) { fontSize = fs, alignment = TextAnchor.MiddleCenter }; ns.normal.textColor = NodeBorder(st); GUI.Label(box, icon, ns); }
                }
            }

            private void DrawTextTree(Vector2 origin, float scale)
            {
                int fs = Mathf.Max(Mathf.RoundToInt(9f * scale), 6); float rowH = (GV_NODE_H + 2f) * scale, indW = 14f * scale;
                for (int i = 0; i < _nodes.Count; i++)
                {
                    var info = _nodes[i]; Node.Status st = info.Node.CurrentStatus;
                    float y = origin.y + i * rowH, x = origin.x + info.Depth * indW;
                    if (st == Node.Status.Running || st == Node.Status.Waiting) { float pulse = (Mathf.Sin(Time.realtimeSinceStartup * 4f) + 1f) * 0.07f + 0.05f; var pc = GUI.color; GUI.color = new Color(1f, 0.88f, 0.08f, pulse); GUI.DrawTexture(new Rect(origin.x, y, GV_NODE_W * scale, rowH), Texture2D.whiteTexture); GUI.color = pc; }
                    if (i > 0) { var tSt = new GUIStyle(GUI.skin.label) { fontSize = fs }; tSt.normal.textColor = new Color(0.3f, 0.5f, 0.7f, 0.6f); GUI.Label(new Rect(x - indW, y, indW, rowH), "└", tSt); }
                    string icon = GetStatusIcon(st), txt = $"{icon} {Truncate(info.DisplayName, 18)}";
                    var ns = new GUIStyle(GUI.skin.label) { fontSize = fs, clipping = TextClipping.Clip }; ns.normal.textColor = st == Node.Status.Running ? new Color(1f, 0.95f, 0.35f) : NodeBorder(st);
                    GUI.Label(new Rect(x, y, GV_NODE_W * scale, rowH), txt, ns);
                }
            }

            // BB values with Rich Text
            private static readonly string[] s_opSkipNames = { "m_Self", "m_Graph", "m_Agent", "m_Parent", "m_Observer" };
            private static string GetNodeBBValues(Node node, BehaviorGraphAgent agent)
            {
                if (node == null || agent?.BlackboardReference == null) return "";
                try
                {
                    var sett = AIDebuggerSettings.Instance;
                    string kc = ColorToHex(sett?.btColorTextBBKey ?? new Color(0.55f, 0.88f, 1.00f));
                    string vc = ColorToHex(sett?.btColorTextBBValue ?? new Color(1.00f, 0.88f, 0.55f));
                    string sc = "#607080";
                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    var entries = new List<string>();
                    foreach (var fi in node.GetType().GetFields(flags))
                    {
                        if (System.Array.IndexOf(s_opSkipNames, fi.Name) >= 0) continue;
                        if (!fi.FieldType.Name.StartsWith("BlackboardVariable")) continue;
                        var valueProp = fi.FieldType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (valueProp == null) continue;
                        var bvObj = fi.GetValue(node); if (bvObj == null) continue;
                        object val = valueProp.GetValue(bvObj); if (val == null) continue;
                        string nm = fi.Name; while (nm.StartsWith("m") || nm.StartsWith("k") || nm.StartsWith("_")) nm = nm.TrimStart('m', 'k', '_');
                        if (nm.Length == 0) nm = fi.Name; if (nm.Length > 14) nm = nm.Substring(0, 13) + "…";
                        string valStr = FormatBBValue(val);
                        entries.Add($"<color={kc}>{nm}</color><color={sc}>:</color><color={vc}>{valStr}</color>");
                        if (entries.Count >= 6) break;
                    }
                    if (entries.Count == 0) return "";
                    var sb = new System.Text.StringBuilder();
                    for (int e = 0; e < entries.Count; e++) { if (e > 0) sb.Append('\n'); sb.Append(entries[e]); }
                    return sb.ToString();
                }
                catch { return ""; }
            }
            private static string FormatBBValue(object val) { if (val is float f) return Mathf.Abs(f) < 0.001f ? "0" : f.ToString("F2"); if (val is bool b) return b ? "✓T" : "✗F"; if (val is int iv) return iv.ToString(); if (val is Vector3 v3) return $"({v3.x:F1},{v3.z:F1})"; if (val is UnityEngine.Object uo && uo != null) { string n = uo.name; return n.Length > 12 ? n.Substring(0, 11) + "…" : n; } string s = val.ToString(); return s.Length > 14 ? s.Substring(0, 13) + "…" : s; }
            private static string ColorToHex(Color c) => $"#{Mathf.RoundToInt(c.r * 255):X2}{Mathf.RoundToInt(c.g * 255):X2}{Mathf.RoundToInt(c.b * 255):X2}";
            private static string Truncate(string s, int max) => s != null && s.Length > max ? s.Substring(0, max) + "…" : (s ?? "");

            // Layout computation
            private static float GetVGap() => AIDebuggerSettings.Instance?.btNodeVGap ?? 36f;
            private static float GetHGap() => AIDebuggerSettings.Instance?.btNodeHGap ?? 10f;
            private void ComputeLayout()
            {
                int n = _nodes.Count; _nodePos = new Vector2[n]; if (n == 0) { _graphW = 0; _graphH = 0; return; }
                var childrenOf = new List<List<int>>(n); for (int i = 0; i < n; i++) childrenOf.Add(new List<int>());
                foreach (var kv in BuildParentMap()) childrenOf[kv.Value].Add(kv.Key);
                float hgap = GetHGap(); float[] sw = new float[n];
                for (int i = n - 1; i >= 0; i--) { if (childrenOf[i].Count == 0) sw[i] = GV_NODE_W + hgap; else foreach (int ci in childrenOf[i]) sw[i] += sw[ci]; }
                float maxX = 0, maxY = 0; AssignPos(0, 0f, childrenOf, sw, ref maxX, ref maxY);
                float bbExtra = (AIDebuggerSettings.Instance?.showBTConditionValues ?? true) ? GV_NODE_H * 1.8f : 0f;
                _graphW = Mathf.Max(maxX + GV_NODE_W, GV_NODE_W + 10f); _graphH = Mathf.Max(maxY + GV_NODE_H + bbExtra, GV_NODE_H + 10f);
            }
            private void AssignPos(int idx, float xOff, List<List<int>> childrenOf, float[] sw, ref float maxX, ref float maxY)
            {
                float x = xOff + sw[idx] * 0.5f - GV_NODE_W * 0.5f; float y = _nodes[idx].Depth * (GV_NODE_H + GetVGap());
                _nodePos[idx] = new Vector2(x, y); if (x > maxX) maxX = x; if (y > maxY) maxY = y;
                float cx = xOff; foreach (int ci in childrenOf[idx]) { AssignPos(ci, cx, childrenOf, sw, ref maxX, ref maxY); cx += sw[ci]; }
            }
            private Dictionary<int, int> BuildParentMap() { var map = new Dictionary<int, int>(_nodes.Count); for (int i = 1; i < _nodes.Count; i++) { int pi = FindParentInLayout(i); if (pi >= 0) map[i] = pi; } return map; }
            private int FindParentInLayout(int idx) { int d = _nodes[idx].Depth; for (int i = idx - 1; i >= 0; i--) if (_nodes[i].Depth == d - 1) return i; return -1; }

#if UNITY_EDITOR
            public void DrawHandlesOverlay(Vector3 aiWorldPos, SceneView sv, int sizeMode = 0, GUIStyle panelStyle = null, float dist = 0f, Rect infoPanelRect = default)
            {
                if (_nodes.Count == 0) return; _cachedSV = sv;
                Vector2 anchor = HandleUtility.WorldToGUIPoint(aiWorldPos + Vector3.up * 3.8f);
                var _svSett2 = AIDebuggerSettings.Instance;
                float _svDS = 1f;
#if UNITY_EDITOR
                if (_svSett2?.useMatrixScale ?? true)
                {
                    float _svDist = Vector3.Distance(sv.camera.transform.position, aiWorldPos);
                    _svDS = Mathf.Clamp(
                        Mathf.Pow(Mathf.Max(_svSett2?.scaleRefDist ?? 6f, 0.1f) / Mathf.Max(_svDist, 0.01f),
                                  _svSett2?.scaleExp ?? 1f),
                        _svSett2?.scaleMin ?? 0.35f, 1f);
                }
#endif
                Handles.BeginGUI();
                Rect virtualPanel = infoPanelRect.width > 0 ? new Rect(infoPanelRect.x, infoPanelRect.yMax + 4f, infoPanelRect.width, 4f) : new Rect(anchor.x, anchor.y, 10f, 10f);
                DrawBTGraph(virtualPanel, sizeMode, dist, true, panelStyle, _svSett2, _svDS);
                Handles.EndGUI();
            }
#endif
        }

        // =====================================================================
        //  ■ 내부 클래스 2: CharacterPanelRenderer  (F-3)
        // =====================================================================
        private class CharacterPanelRenderer
        {
            private CharacterStatsManager _stats;
            private object _statsObj;
            private NavMeshAgent _nav;
            private Animator _anim;
            public float LastPanelHeight { get; private set; }
            private const string C_LBL = "#B8CCFF", C_VAL = "#F0EBC8", C_HP = "#45EF70", C_STA = "#28B8FF", C_WARN = "#FFA520", C_DIM = "#404855", C_STA2 = "#A0D8FF";

            public void CacheComponents(GameObject go)
            {
                _stats = go.GetComponent<CharacterStatsManager>();
                if (_stats == null) { foreach (var comp in go.GetComponents<MonoBehaviour>()) { if (comp == null) continue; string tn = comp.GetType().Name.ToLower(); if (tn.Contains("stat") || tn.Contains("health")) { _statsObj = comp; break; } } }
                _nav = go.GetComponent<NavMeshAgent>();
                _anim = go.GetComponent<Animator>();
            }

            private const float TITLE_H_CHAR = 18f;

            public void DrawInGameView(Vector2 topLeft, GUIStyle textStyle, GUIStyle panelStyle)
            {
                string content = BuildContent();
                if (string.IsNullOrEmpty(content)) { LastPanelHeight = 0f; return; }
                DrawInGameViewTitled(topLeft, textStyle, panelStyle, "Character",
                                     ref _dummyDrag, ref _dummyDragStart, ref _dummyDragOff, TITLE_H_CHAR);
            }
            private bool _dummyDrag; private Vector2 _dummyDragStart, _dummyDragOff;

            public void DrawInGameViewTitled(Vector2 topLeft, GUIStyle textStyle, GUIStyle panelStyle,
                                             string title,
                                             ref bool dragging, ref Vector2 dragStart, ref Vector2 dragOff,
                                             float titleH)
            {
                string content = BuildContent();
                if (string.IsNullOrEmpty(content)) { LastPanelHeight = 0f; return; }
                int fs = AIDebuggerSettings.Instance?.fontSizeCharPanel > 0
                          ? AIDebuggerSettings.Instance.fontSizeCharPanel : 10;
                var ts = new GUIStyle(textStyle) { fontSize = fs, richText = true };
                Vector2 sz = ts.CalcSize(new GUIContent(content));
                float pw = Mathf.Max(sz.x + 20f, 150f);
                float ph = sz.y + 14f + titleH;
                LastPanelHeight = ph;

                Rect panel = new Rect(topLeft.x, topLeft.y, pw, ph);
                // 공통 설정으로 라운드 패널 렌더링
                var sett = AIDebuggerSettings.Instance;
                int cr2 = Mathf.RoundToInt((sett?.panelCornerRadius ?? 8) / 15f * 120f);
                int bw2 = sett?.panelBorderWidth ?? 2;
                float op2 = sett?.panelOpacity ?? 0.90f;
                bool isLt = (sett?.theme ?? AIDebuggerSettings.BTTheme.Dark) == AIDebuggerSettings.BTTheme.Light;
                Color bgC2 = isLt ? (sett?.panelBgColorLight ?? new Color(0.88f, 0.91f, 0.95f)) : (sett?.panelBgColor ?? new Color(0.07f, 0.08f, 0.11f));
                Color bdC2 = isLt ? (sett?.panelBdColorLight ?? new Color(0.22f, 0.50f, 0.85f)) : (sett?.panelBdColor ?? new Color(0.28f, 0.76f, 1.00f));
                // 전체 배경 (4코너 라운드)
                var fullTex2 = AIPerceptionDebugger.BuildRoundedTexStatic(256, cr2, AIPerceptionDebugger.WithAlpha(bgC2, op2), AIPerceptionDebugger.WithAlpha(bdC2, op2), bw2);
                var fullSt2 = new GUIStyle { normal = { background = fullTex2 }, border = new RectOffset(20, 20, 20, 20) };
                GUI.Box(panel, GUIContent.none, fullSt2);

                // 타이틀 바 오버레이 (상단 라운드)
                Rect titleBar = new Rect(panel.x, panel.y, pw, titleH);
                Color titleC2 = new Color(0.10f, 0.50f, 0.65f, op2 * 0.92f);
                var titleTex2 = AIPerceptionDebugger.BuildRoundedTexCorners(256, cr2, titleC2, AIPerceptionDebugger.WithAlpha(bdC2, op2), bw2, true, true, false, false);
                var titleSt2b = new GUIStyle { normal = { background = titleTex2 }, border = new RectOffset(20, 20, 20, 0) };
                GUI.Box(titleBar, GUIContent.none, titleSt2b);
                var tst = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 10,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
                tst.normal.textColor = Color.white;
                GUI.Label(new Rect(titleBar.x + 6, titleBar.y, pw - 6, titleH), title, tst);

                // 드래그
                var ev = Event.current; Vector2 mp = ev.mousePosition;
                if (ev.type == EventType.MouseDown && ev.button == 0 && titleBar.Contains(mp))
                { dragging = true; dragStart = mp - dragOff; ev.Use(); }
                else if (ev.type == EventType.MouseDrag && dragging)
                { dragOff = mp - dragStart; ev.Use(); }
                else if (ev.type == EventType.MouseUp && dragging)
                { dragging = false; ev.Use(); }

                // 본문
                GUI.Label(new Rect(panel.x + 8, panel.y + titleH + 4, pw - 16, ph - titleH - 8), content, ts);
            }

            private static readonly string[] HP_CUR = { "currentHealth", "CurrentHealth", "health", "HP", "hp", "baseHealth", "BaseHealth", "maxHealth", "MaxHealth", "healthPoints" };
            private static readonly string[] HP_MAX = { "maxHealth", "MaxHealth", "baseHealth", "BaseHealth", "maxHP", "MaxHP" };
            private static readonly string[] STA_CUR = { "currentStamina", "CurrentStamina", "stamina", "Stamina", "baseStamina", "BaseStamina", "maxStamina" };
            private static readonly string[] STA_MAX = { "maxStamina", "MaxStamina", "baseStamina", "BaseStamina", "staminaMax", "maxSta" };
            private static MemberInfo s_hpCur, s_hpMax, s_staCur, s_staMax;

            private static float ReadStat(object target, ref MemberInfo cache, string[] names)
            {
                if (target == null) return 0f;
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                if (cache == null) { var t = target.GetType(); foreach (var name in names) { var fi = t.GetField(name, flags); if (fi != null) { cache = fi; break; } var pi = t.GetProperty(name, flags); if (pi != null) { cache = pi; break; } } }
                if (cache == null) return 0f;
                try { object val = cache is FieldInfo fi2 ? fi2.GetValue(target) : cache is PropertyInfo pi2 ? pi2.GetValue(target) : 0f; return Convert.ToSingle(val); } catch { return 0f; }
            }

            // ── 애니메이션 이벤트 감지 ──────────────────────────────────────
            private static string s_lastAnimEvent = "";
            private static float s_lastAnimEventTime = -1f;
            private const float ANIM_EVENT_SEC = 1.5f;

            private static string GetRecentAnimEvent(Animator anim, int layer)
            {
                try
                {
                    if (!anim.isActiveAndEnabled) return "";
                    var clips = anim.GetCurrentAnimatorClipInfo(layer);
                    if (clips.Length == 0) return "";
                    float normT = anim.GetCurrentAnimatorStateInfo(layer).normalizedTime % 1f;
                    var clip = clips[0].clip; float clipT = normT * clip.length;
                    foreach (var ev in clip.events) { if (Mathf.Abs(ev.time - clipT) < 0.12f) { s_lastAnimEvent = ev.functionName; s_lastAnimEventTime = Time.realtimeSinceStartup; } }
                    if (!string.IsNullOrEmpty(s_lastAnimEvent) && Time.realtimeSinceStartup - s_lastAnimEventTime < ANIM_EVENT_SEC) return s_lastAnimEvent;
                    return "";
                }
                catch { return ""; }
            }
            private static string GetClipName(Animator anim, int layer) { try { var clips = anim.GetCurrentAnimatorClipInfo(layer); return clips.Length > 0 ? clips[0].clip.name : ""; } catch { return ""; } }

            private string BuildContent()
            {
                var sb = new System.Text.StringBuilder();
                object statsTarget = _stats != null ? (object)_stats : _statsObj;
                if (statsTarget != null)
                {
                    float hp = ReadStat(statsTarget, ref s_hpCur, HP_CUR);
                    float mxHp = Mathf.Max(ReadStat(statsTarget, ref s_hpMax, HP_MAX), 1f);
                    float st = ReadStat(statsTarget, ref s_staCur, STA_CUR);
                    float mxSt = Mathf.Max(ReadStat(statsTarget, ref s_staMax, STA_MAX), 1f);
                    int hpFill = Mathf.Clamp(Mathf.RoundToInt(hp / mxHp * 10f), 0, 10);
                    int stFill = Mathf.Clamp(Mathf.RoundToInt(st / mxSt * 10f), 0, 10);
                    string hpHex = hp / mxHp < 0.3f ? C_WARN : C_HP;
                    sb.AppendLine($"<color={C_LBL}>HP   </color><color={hpHex}>{new string('█', hpFill)}</color><color={C_DIM}>{new string('░', 10 - hpFill)}</color> <color={C_VAL}>{hp:F0}/{mxHp:F0}</color>");
                    sb.AppendLine($"<color={C_LBL}>STA  </color><color={C_STA}>{new string('█', stFill)}</color><color={C_DIM}>{new string('░', 10 - stFill)}</color> <color={C_VAL}>{st:F0}/{mxSt:F0}</color>");
                }
                if (_nav != null)
                {
                    string pathCol = _nav.pathStatus == NavMeshPathStatus.PathComplete ? C_HP : C_WARN;
                    string pathStr = _nav.pathStatus.ToString().Replace("Path", "");
                    sb.AppendLine($"<color={C_LBL}>SPD  </color><color={C_VAL}>{_nav.velocity.magnitude:F1}m/s</color>  <color={pathCol}>{pathStr}</color>  <color={C_VAL}>{_nav.remainingDistance:F1}m</color>");
                }
                if (_anim != null && _anim.isActiveAndEnabled)
                {
                    string name0 = GetClipName(_anim, 0);
                    if (!string.IsNullOrEmpty(name0))
                    {
                        float nTime0 = _anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
                        string evStr = GetRecentAnimEvent(_anim, 0);
                        sb.AppendLine($"<color={C_LBL}>ANIM </color><color={C_VAL}>{name0}</color><color={C_DIM}> {nTime0:F2}</color>");
                        if (!string.IsNullOrEmpty(evStr)) sb.AppendLine($"<color=#FFD700><b>▶ {evStr}</b></color>");
                    }
                    if (_anim.layerCount > 1) { string name1 = GetClipName(_anim, 1); if (!string.IsNullOrEmpty(name1)) sb.AppendLine($"<color={C_LBL}>UPP  </color><color={C_VAL}>{name1}</color>"); }
                    try { int pCount = 0; foreach (var ap in _anim.parameters) { if (pCount >= 4) break; if (ap.type == AnimatorControllerParameterType.Float) { float pv = _anim.GetFloat(ap.name); if (Mathf.Abs(pv) > 0.05f) { string pn = ap.name.Length > 8 ? ap.name.Substring(0, 8) : ap.name; sb.AppendLine($"<color={C_DIM}>{pn}</color><color={C_STA2}>:{pv:F2}</color>"); pCount++; } } } } catch { }
                }
                return sb.ToString().TrimEnd();
            }
        }

        // =====================================================================
        //  ■ 내부 static 클래스: OverlapResolver
        // =====================================================================
        private static class OverlapResolver
        {
            // ★ Dictionary<int,Rect>: instanceId당 1개만 유지 (중복 등록 방지)
            // List.Add 방식은 OnGUI 다중 호출(Layout+Repaint+MouseDown)마다 쌓여
            // 자기 자신 충돌 감지 → 패널이 클릭 시 도망가는 버그 발생.
            private static readonly Dictionary<int, Rect> s_placed = new();
            private static int s_lastFrame = -1;
            private const float PADDING = 8f;

            private static void RefreshFrame()
            {
                if (Time.frameCount != s_lastFrame) { s_placed.Clear(); s_lastFrame = Time.frameCount; }
            }

            // excludeId: 자기 자신 rect와의 충돌 무시
            public static bool CheckOverlap(Rect r, int excludeId = -1)
            {
                RefreshFrame();
                foreach (var kv in s_placed)
                {
                    if (kv.Key == excludeId) continue;
                    if (r.Overlaps(kv.Value)) return true;
                }
                return false;
            }

            // instanceId 기반 덮어쓰기 — 같은 프레임 재호출 시 누적 없음
            public static void Register(int instanceId, Rect r) { RefreshFrame(); s_placed[instanceId] = r; }

            // 구 API 하위 호환 (호출부 수정 불필요)
            public static void Register(Rect r) { /* 사용 안 함 — 하위 호환용 */ }

            /// <summary>SeparateForce 모드: 겹친 모든 패널 방향의 합산 반발 벡터 반환.</summary>
            public static Vector2 GetSeparation(Rect r, int excludeId = -1)
            {
                RefreshFrame();
                Vector2 total = Vector2.zero;
                foreach (var kv in s_placed)
                {
                    if (kv.Key == excludeId) continue;
                    Rect o = kv.Value;
                    if (!r.Overlaps(o)) continue;
                    float dx = r.center.x - o.center.x, dy = r.center.y - o.center.y;
                    float ovX = (r.width + o.width) * 0.5f - Mathf.Abs(dx);
                    float ovY = (r.height + o.height) * 0.5f - Mathf.Abs(dy);
                    if (ovX < ovY) total += new Vector2(Mathf.Sign(dx) * ovX, 0f);
                    else total += new Vector2(0f, Mathf.Sign(dy) * ovY);
                }
                return total;
            }

            public static Vector2 Resolve(Vector2 pos, float pw, float ph, AIDebuggerSettings.OverlapMode mode)
            {
                if (mode == AIDebuggerSettings.OverlapMode.None) return pos;
                RefreshFrame();
                Rect r = new Rect(pos.x, pos.y, pw, ph);
                for (int iter = 0; iter < 12; iter++)
                {
                    bool hit = false;
                    foreach (var kv in s_placed) { if (!r.Overlaps(kv.Value)) continue; hit = true; r.y = kv.Value.yMax + PADDING; break; }
                    if (!hit) break;
                }
                s_placed[-999] = r;
                return new Vector2(r.x, r.y);
            }
        }

        // =====================================================================
        //  ■ SVGroupResolver  —  Scene View 전용 캐릭터 그룹 겹침 해소
        //
        //  · 각 AI의 패널 그룹(AI Info + BT Graph)을 하나의 Rect 단위로 관리
        //  · 이미 배치된 그룹과 겹치면 Y축 방향으로 밀어냄
        //  · 같은 instanceId가 같은 프레임에 재호출 될 때 캐시 반환
        //    → BT Graph DrawHandlesOverlay 등 다중 콜백에서 위치 일관성 보장
        // =====================================================================
        private static class SVGroupResolver
        {
            private static readonly List<(int id, Rect rect)> s_placed = new();
            private static int s_lastFrame = -1;
            private const float PAD = 10f;
            private const int MAX_ITER = 24;

            private static void Refresh()
            {
                if (Time.frameCount != s_lastFrame)
                { s_placed.Clear(); s_lastFrame = Time.frameCount; }
            }

            /// <summary>
            /// 자연 위치에서 시작해 이미 배치된 그룹들과 겹치지 않는
            /// 위치를 계산하고 등록한다. 같은 instanceId는 캐시를 반환한다.
            /// </summary>
            public static Vector2 Resolve(int instanceId, Vector2 natural,
                                          float gw, float gh, float svHeight)
            {
                Refresh();

                // 같은 프레임 재호출 → 캐시된 위치 즉시 반환 (그룹 내 패널 일관성)
                foreach (var (id, r) in s_placed)
                    if (id == instanceId) return new Vector2(r.x, r.y);

                // ── ① 아래 방향 밀기 ─────────────────────────────────────────
                Rect cDown = new Rect(natural.x, natural.y, gw, gh);
                for (int iter = 0; iter < MAX_ITER; iter++)
                {
                    bool hit = false;
                    foreach (var (_, r) in s_placed)
                    {
                        if (!cDown.Overlaps(r)) continue;
                        cDown.y = r.yMax + PAD;
                        hit = true; break;
                    }
                    if (!hit) break;
                }

                // ── ② 위 방향 밀기 ───────────────────────────────────────────
                Rect cUp = new Rect(natural.x, natural.y, gw, gh);
                for (int iter = 0; iter < MAX_ITER; iter++)
                {
                    bool hit = false;
                    foreach (var (_, r) in s_placed)
                    {
                        if (!cUp.Overlaps(r)) continue;
                        cUp.y = r.y - gh - PAD;
                        hit = true; break;
                    }
                    if (!hit) break;
                }

                // ── ③ 더 화면 안쪽이고 자연 위치에 가까운 방향 선택 ─────────
                bool downOnScreen = cDown.y >= 4f && cDown.yMax <= svHeight - 4f;
                bool upOnScreen = cUp.y >= 4f && cUp.yMax <= svHeight - 4f;

                Rect chosen;
                if (downOnScreen && upOnScreen)
                    // 자연 위치(Y)에서 이동 거리가 짧은 쪽 선택
                    chosen = Mathf.Abs(cDown.y - natural.y) <= Mathf.Abs(cUp.y - natural.y)
                             ? cDown : cUp;
                else if (downOnScreen) chosen = cDown;
                else if (upOnScreen) chosen = cUp;
                else chosen = cDown;   // 둘 다 화면 밖 → 아래 선택 후 클램프

                // ── ④ 최종 화면 하단 클램프 ─────────────────────────────────
                chosen.y = Mathf.Clamp(chosen.y, 4f, Mathf.Max(svHeight - gh - 4f, 4f));
                s_placed.Add((instanceId, chosen));
                return new Vector2(chosen.x, chosen.y);
            }
        }
    }

}