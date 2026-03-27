// =============================================================================
// CharacterManagerDebugger.cs  |  TDA Project
// Layer  : Editor Tool — 공통 베이스 디버거 컴포넌트
//
// 변경 이력:
//   [REQ-1] Execution / QTE Manager 모니터링 탭 추가
//   [REQ-2] 락온 타겟 정보 + 파노라마 패널 추가
//   [REQ-3] 각 섹션 개별 접기/펼치기 (foldout) 기능
//   [REQ-4] SceneView/Hierarchy 우클릭 메뉴 최상위 등록
//   [REQ-5] 게임뷰 포커스 중 ESC → 마우스 복원 후 버튼 동작 보장
//   [REQ-6] 애니 탭에 ActionID Enum 원본 변수명 표시 옵션
//   [REQ-7] 텍스트 크기·창 크기 인스펙터 파라미터화
//   [REQ-8] 각 탭 표시 항목 인스펙터 플래그
//   [NEW-1] 하단 파노라마 패널 — 여러 탭 동시 표시 (펼침/접힘)
//   [NEW-2] 락온 대상 Side HUD — 메인 HUD 우측에 동일 구조 HUD 표시
//           락온 대상 전용 하단 파노라마 패널 (메인 파노라마 바로 위)
//   [NEW-3] 각 창(메인 HUD / 사이드 HUD / 메인 파노라마 / 타겟 파노라마)
//           개별 숨기기 / 최소화 기능
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.Character;
using TDA.Core.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TDA.EditorTools
{
    [DisallowMultipleComponent]
    public class CharacterManagerDebugger : MonoBehaviour
    {
        // =====================================================================
        // [REQ-7] 창 크기 / 텍스트 크기 인스펙터 파라미터
        // =====================================================================
        [Header("─── HUD 크기 설정 ───────────────────────────────")]
        [Tooltip("HUD 창 너비 (px)")]
        [Range(220, 500)]
        public int hudWidth = 300;

        [Tooltip("HUD 창 높이 (px)")]
        [Range(200, 800)]
        public int hudHeight = 520;

        [Tooltip("본문 텍스트 크기")]
        [Range(8, 16)]
        public int fontSize = 11;

        [Tooltip("섹션 헤더 텍스트 크기")]
        [Range(9, 18)]
        public int sectionFontSize = 11;

        // =====================================================================
        // [REQ-3] On/Off 및 기본 설정
        // =====================================================================
        [Header("─── TDA Character Debugger ───────────────────────")]
        public bool showDebugHUD = false;

        // =====================================================================
        // [NEW-1] 파노라마 패널 설정
        // =====================================================================
        [Header("─── 파노라마 패널 ────────────────────────────────")]
        [Tooltip("하단 파노라마 패널 표시 여부")]
        public bool showPanorama = true;

        [Tooltip("파노라마에 동시 표시할 탭 목록 (인덱스). 비어있으면 전체)")]
        public int[] panoramaTabs = new int[] { 0, 3, 4 };  // 공통, 처형/QTE, 타겟

        [Tooltip("파노라마 단일 패널 높이")]
        [Range(80, 300)]
        public int panoramaPanelHeight = 160;

        // =====================================================================
        // [NEW-2] 락온 사이드 HUD 설정
        // =====================================================================
        [Header("─── 락온 사이드 HUD ──────────────────────────────")]
        [Tooltip("락온 대상 사이드 HUD 표시 여부 (락온 중에만 표시)")]
        public bool showTargetSideHUD = true;

        [Tooltip("락온 대상 하단 파노라마 표시 여부")]
        public bool showTargetPanorama = true;

        [Tooltip("사이드 HUD와 메인 HUD 사이 간격 (px)")]
        [Range(4, 20)]
        public int sideHudGap = 8;

        // =====================================================================
        // [REQ-8] 공통 탭 표시 플래그
        // =====================================================================
        [Header("─── 표시 항목 제어 (공통 탭) ───────────────────────")]
        [Tooltip("공통 탭: 상태 플래그 섹션 표시")]
        public bool showSectionFlags = true;
        [Tooltip("공통 탭: 생존 스탯(HP/스태미나/포이즈) 섹션 표시")]
        public bool showSectionStats = true;
        [Tooltip("공통 탭: 전투 상태 섹션 표시")]
        public bool showSectionCombat = true;

        [Header("─── 표시 항목 제어 (처형/QTE 탭) ─────────────────")]
        [Tooltip("처형 탭: 처형 상태 섹션 표시")]
        public bool showSectionExecution = true;
        [Tooltip("처형 탭: QTE 상태 섹션 표시")]
        public bool showSectionQTE = true;

        [Header("─── 표시 항목 제어 (타겟 탭) ─────────────────────")]
        [Tooltip("타겟 탭: 타겟 기본 정보 표시")]
        public bool showSectionTargetInfo = true;
        [Tooltip("타겟 탭: 타겟 파노라마 (상태/스탯) 표시")]
        public bool showSectionTargetPanorama = true;

        // =====================================================================
        // [REQ-6] 애니메이션 탭 옵션
        // =====================================================================
        [Header("─── 애니메이션 탭 옵션 ─────────────────────────────")]
        [Tooltip("true 이면 ActionID를 enum 원본 변수명으로 표시합니다.\n" +
                 "false 이면 숫자 ID만 표시합니다.")]
        public bool showActionIDEnumName = true;

        [Tooltip("ActionID가 ActionID enum에 없는 hash값일 때도 변환 시도합니다.\n" +
                 "(AnimationEventType enum까지 검색)")]
        public bool fallbackToAnimEventEnum = false;

        // =====================================================================
        // Pause 트리거
        // =====================================================================
        [Header("─── Pause 트리거 ─────────────────────────────────")]
        public bool pauseOnDeath = false;
        public bool pauseOnPoiseBreak = false;
        public AnimationEventType[] pauseOnEventTypes = new AnimationEventType[0];

        // =====================================================================
        // 히스토리 설정
        // =====================================================================
        [Header("─── 히스토리 설정 ───────────────────────────────")]
        [Range(10, 100)]
        public int maxHistoryCount = 50;

        // =====================================================================
        // 내부 참조
        // =====================================================================
        protected CharacterManager character;
        private CharacterEventManager eventManager;

        // =====================================================================
        // 히스토리 링 버퍼
        // =====================================================================
        protected struct DebugEventEntry
        {
            public float time;
            public AnimationEventType type;
            public string source;
        }

        protected struct DebugAnimEntry
        {
            public float time;
            public int actionID;
            public string label;
            public bool isPerformingAction;
        }

        private DebugEventEntry[] _eventHistory;
        private int _eventHead = 0, _eventCount = 0;

        private DebugAnimEntry[] _animHistory;
        private int _animHead = 0, _animCount = 0;

        // =====================================================================
        // HUD 레이아웃 상태 — 메인 HUD
        // =====================================================================
        private Rect _hudRect = new Rect(10, 10, 300, 520);
        private bool _isDragging = false;
        private Vector2 _dragOffset = Vector2.zero;
        private int _activeTab = 0;
        private Vector2 _scrollPos;

        // [NEW-3] 메인 HUD 최소화 상태
        private bool _hudMinimized = false;

        // =====================================================================
        // [NEW-2] 락온 사이드 HUD 상태
        // =====================================================================
        private bool _sideHudMinimized = false;
        private int _sideActiveTab = 0;
        private Vector2 _sideScrollPos;
        private bool _sideIsDragging = false;
        private Vector2 _sideDragOffset = Vector2.zero;
        private Rect _sideHudRect;  // 런타임에 메인 HUD에서 계산

        // =====================================================================
        // [NEW-1] 메인 파노라마 패널 상태
        // =====================================================================
        private bool _panoramaVisible = true;   // 인스펙터 showPanorama와 별개로 런타임 토글
        private bool _panoramaMinimized = false;
        private Rect _panoramaRect;             // 런타임에 화면 하단에서 계산
        private bool _panoramaIsDragging = false;
        private Vector2 _panoramaDragOffset = Vector2.zero;
        private Vector2[] _panoramaScrollPos;   // 탭별 스크롤
        private const float PANORAMA_TAB_WIDTH = 220f;
        private const float PANORAMA_HEADER_H = 26f;

        // =====================================================================
        // [NEW-2] 락온 타겟 파노라마 상태
        // =====================================================================
        private bool _targetPanoVisible = true;
        private bool _targetPanoMinimized = false;
        private Rect _targetPanoRect;
        private bool _targetPanoIsDragging = false;
        private Vector2 _targetPanoDragOffset = Vector2.zero;
        private Vector2 _targetPanoScroll;

        // =====================================================================
        // [REQ-3] 섹션 접기 상태 (공통 탭)
        // =====================================================================
        private bool _foldFlags = true;
        private bool _foldStats = true;
        private bool _foldCombat = true;
        private bool _foldExec = true;
        private bool _foldQTE = true;
        private bool _foldTarget = true;
        private bool _foldTargetPano = true;

        // 탭 목록 (기본 — 서브클래스에서 override)
        protected virtual string[] TabLabels =>
            new[] { "공통", "이벤트", "애니", "처형/QTE", "타겟", "참조" };

        // 포이즈/사망 추적
        private float _lastPoiseValue = -1f;
        protected int _poiseBreakCount = 0;
        protected float _lastPoiseBreakTime = -1f;
        private bool _prevIsDead = false;

        // Null 체크 캐시
        protected List<(string name, bool isNull, bool isCritical, string symptom)> _nullEntries
            = new List<(string, bool, bool, string)>();
        private float _nullCheckTimer = 0f;

        // =====================================================================
        // [REQ-5] 게임뷰 포커스 감지 — ESC 처리
        // =====================================================================
        private bool _cursorWasLocked = false;

        // =====================================================================
        // Unity 생명주기
        // =====================================================================
        protected virtual void Awake()
        {
            CacheComponents();
            _eventHistory = new DebugEventEntry[maxHistoryCount];
            _animHistory = new DebugAnimEntry[maxHistoryCount];
        }

        protected virtual void OnEnable()
        {
            if (eventManager == null)
                eventManager = GetComponent<CharacterEventManager>();
            if (eventManager != null)
                eventManager.OnAnimationEventTriggered += OnAnimEventReceived;
        }

        protected virtual void OnDisable()
        {
            if (eventManager != null)
                eventManager.OnAnimationEventTriggered -= OnAnimEventReceived;
        }

        protected virtual void Update()
        {
            if (!showDebugHUD || character == null) return;

            // [REQ-5] 게임뷰 포커스 중 ESC → 커서 복원
            HandleCursorUnlock();

            if (character.IsSpawned && character.characterNetworkManager != null)
            {
                var nm = character.characterNetworkManager;
                float poise = nm.currentPoise.Value;
                if (_lastPoiseValue > 0f && poise <= 0f)
                {
                    _poiseBreakCount++;
                    _lastPoiseBreakTime = Time.time;
#if UNITY_EDITOR
                    if (pauseOnPoiseBreak) EditorApplication.isPaused = true;
#endif
                }
                _lastPoiseValue = poise;

                bool isDead = nm.isDead.Value;
                if (!_prevIsDead && isDead)
                {
#if UNITY_EDITOR
                    if (pauseOnDeath) EditorApplication.isPaused = true;
#endif
                }
                _prevIsDead = isDead;
            }

            _nullCheckTimer += Time.deltaTime;
            if (_nullCheckTimer >= 0.5f) { _nullCheckTimer = 0f; RefreshNullCheck(); }

            // HUD Rect을 인스펙터 파라미터와 동기화
            _hudRect.width = hudWidth;
            if (!_hudMinimized)
                _hudRect.height = hudHeight;
            else
                _hudRect.height = 26f;  // 최소화: 타이틀 바만

            // 파노라마 스크롤 배열 초기화
            if (_panoramaScrollPos == null || _panoramaScrollPos.Length != TabLabels.Length)
                _panoramaScrollPos = new Vector2[TabLabels.Length];
        }

        // [REQ-5] ESC로 커서 잠금 해제
        private void HandleCursorUnlock()
        {
#if UNITY_EDITOR
            bool isLocked = Cursor.lockState == CursorLockMode.Locked;
            if (isLocked && !_cursorWasLocked)
                _cursorWasLocked = true;
            if (_cursorWasLocked && Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _cursorWasLocked = false;
            }
            if (!isLocked)
                _cursorWasLocked = false;
#endif
        }

        // =====================================================================
        // 컴포넌트 캐시
        // =====================================================================
        protected virtual void CacheComponents()
        {
            character = GetComponent<CharacterManager>();
            eventManager = GetComponent<CharacterEventManager>();
        }

        // =====================================================================
        // 이벤트 수신 → 링 버퍼
        // =====================================================================
        private void OnAnimEventReceived(AnimationEventType t)
        {
            int idx = _eventHead % maxHistoryCount;
            _eventHistory[idx] = new DebugEventEntry { time = Time.time, type = t, source = "" };
            _eventHead++;
            _eventCount = Mathf.Min(_eventCount + 1, maxHistoryCount);
            CheckEventPauseTrigger(t);
        }

        public void RecordAnimationPlay(int actionID, string label, bool isPerformingAction)
        {
            int idx = _animHead % maxHistoryCount;
            _animHistory[idx] = new DebugAnimEntry
            {
                time = Time.time,
                actionID = actionID,
                label = label,
                isPerformingAction = isPerformingAction
            };
            _animHead++;
            _animCount = Mathf.Min(_animCount + 1, maxHistoryCount);
        }

        private void CheckEventPauseTrigger(AnimationEventType t)
        {
#if UNITY_EDITOR
            if (pauseOnEventTypes == null) return;
            foreach (var target in pauseOnEventTypes)
                if (t == target) { EditorApplication.isPaused = true; return; }
#endif
        }

        // =====================================================================
        // Null 체크 캐시 갱신
        // =====================================================================
        protected virtual void RefreshNullCheck()
        {
            _nullEntries.Clear();
            if (character == null) return;

            Add("characterController", character.characterController == null, true, "Move() NullRef — 이동 없음");
            Add("animator", character.animator == null, true, "애니메이션 전체 불동작");
            Add("characterNetworkManager", character.characterNetworkManager == null, true, "NetworkVariable 접근 불가");
            Add("characterAnimationManager", character.characterAnimationManager == null, true, "PlayTargetActionFunnel NullRef");
            Add("characterCombatManager", character.characterCombatManager == null, true, "히트박스 제어 없음");
            Add("characterLocomotionManager", character.characterLocomotionManager == null, false, "중력/이동 로직 없음");
            Add("characterStatsManager", character.characterStatsManager == null, false, "HP/스태미나 재생 없음");
            Add("characterDefenseManager", character.characterDefenseManager == null, false, "방어/패링 판정 없음");
            Add("characterEventManager", character.characterEventManager == null, true, "이벤트 브로드캐스트 없음");
            Add("characterExecutionManager", character.characterExecutionManager == null, false, "처형 시스템 연결 없음");
            Add("lockOnTransform", character.lockOnTransform == null, false, "카메라 락온 조준점 없음");
        }

        protected void Add(string name, bool isNull, bool isCritical, string symptom)
            => _nullEntries.Add((name, isNull, isCritical, symptom));

        // =====================================================================
        // OnGUI — 메인 HUD + 사이드 HUD + 파노라마 패널들
        // =====================================================================
#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!showDebugHUD || !Application.isPlaying || character == null) return;
            if (Cursor.lockState == CursorLockMode.Locked) return;

            // ── 파노라마 배열 초기화 ──
            if (_panoramaScrollPos == null || _panoramaScrollPos.Length != TabLabels.Length)
                _panoramaScrollPos = new Vector2[TabLabels.Length];

            // ── 락온 대상 가져오기 ──
            CharacterManager lockOnTarget = GetLockOnTarget();
            bool isLockedOn = lockOnTarget != null;

            // ── 1. 메인 파노라마 패널 (최하단) ──
            if (showPanorama && _panoramaVisible)
                DrawMainPanorama(lockOnTarget);

            // ── 2. 타겟 파노라마 패널 (메인 파노라마 바로 위) ──
            if (isLockedOn && showTargetPanorama && _targetPanoVisible)
                DrawTargetPanoramaPanel(lockOnTarget);

            // ── 3. 메인 HUD ──
            HandleDrag(ref _hudRect, ref _isDragging, ref _dragOffset);

            float mainBgH = _hudMinimized ? 26f : hudHeight;
            GUI.color = new Color(0f, 0f, 0f, 0.84f);
            GUI.DrawTexture(new Rect(_hudRect.x, _hudRect.y, hudWidth, mainBgH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(_hudRect.x, _hudRect.y, hudWidth, mainBgH));
            DrawMainTitleBar();
            if (!_hudMinimized)
            {
                DrawTabStrip();
                float contentH = hudHeight - 82f;
                _scrollPos = GUILayout.BeginScrollView(_scrollPos,
                    GUILayout.Width(hudWidth),
                    GUILayout.Height(Mathf.Max(contentH, 50f)));
                DrawTab(_activeTab);
                GUILayout.EndScrollView();
                DrawToolbar();
            }
            GUILayout.EndArea();

            // ── 4. 락온 사이드 HUD ──
            if (isLockedOn && showTargetSideHUD)
                DrawSideHUD(lockOnTarget);
        }

        // =====================================================================
        // 락온 대상 가져오기
        // =====================================================================
        protected virtual CharacterManager GetLockOnTarget()
        {
            if (character == null) return null;
            var cm = character.characterCombatManager;
            if (cm == null) return null;
            return cm.currentTarget;
        }

        // =====================================================================
        // [NEW-2] 사이드 HUD (락온 대상)
        // =====================================================================
        private void DrawSideHUD(CharacterManager target)
        {
            float sideX = _hudRect.x + hudWidth + sideHudGap;
            float sideY = _hudRect.y;
            float sideH = _sideHudMinimized ? 26f : hudHeight;

            _sideHudRect = new Rect(sideX, sideY, hudWidth, sideH);
            HandleDrag(ref _sideHudRect, ref _sideIsDragging, ref _sideDragOffset);

            GUI.color = new Color(0f, 0.05f, 0.12f, 0.88f);
            GUI.DrawTexture(new Rect(_sideHudRect.x, _sideHudRect.y, hudWidth, sideH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(_sideHudRect.x, _sideHudRect.y, hudWidth, sideH));

            // 사이드 타이틀 바
            GUILayout.BeginHorizontal(GUILayout.Height(26));
            GUILayout.Label(
                $"<b><color=#FF8A65>🎯 {target.name}</color></b>  <color=#B0BEC5>{target.GetType().Name}</color>",
                HudStyle.Get(fontSize), GUILayout.ExpandWidth(true));

            // [NEW-3] 최소화 버튼
            string minIcon = _sideHudMinimized ? "▲" : "▼";
            if (GUILayout.Button(minIcon, GUILayout.Width(22), GUILayout.Height(22)))
                _sideHudMinimized = !_sideHudMinimized;
            // 숨기기 버튼
            if (GUILayout.Button("×", GUILayout.Width(22), GUILayout.Height(22)))
                showTargetSideHUD = false;
            GUILayout.EndHorizontal();

            if (!_sideHudMinimized)
            {
                // 사이드 탭 스트립
                string[] labels = GetSideTabLabels();
                int newTab = GUILayout.Toolbar(_sideActiveTab, labels,
                    HudStyle.TabStyle, GUILayout.Height(24));
                if (newTab != _sideActiveTab) { _sideActiveTab = newTab; _sideScrollPos = Vector2.zero; }

                float contentH = hudHeight - 82f;
                _sideScrollPos = GUILayout.BeginScrollView(_sideScrollPos,
                    GUILayout.Width(hudWidth),
                    GUILayout.Height(Mathf.Max(contentH, 50f)));

                DrawSideTab(target, _sideActiveTab);

                GUILayout.EndScrollView();

                // 사이드 툴바
                GUILayout.BeginHorizontal(GUILayout.Height(26));
                if (GUILayout.Button("▶", GUILayout.Height(22))) EditorApplication.isPaused = false;
                if (GUILayout.Button("⏸", GUILayout.Height(22))) EditorApplication.isPaused = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        // 사이드 HUD 탭 라벨
        protected virtual string[] GetSideTabLabels() =>
            new[] { "공통", "스탯", "전투", "타겟의타겟" };

        // 사이드 HUD 탭 내용
        protected virtual void DrawSideTab(CharacterManager target, int tab)
        {
            switch (tab)
            {
                case 0: DrawSideCommon(target); break;
                case 1: DrawSideStats(target); break;
                case 2: DrawSideCombat(target); break;
                case 3: DrawSideTargetOfTarget(target); break;
            }
        }

        private void DrawSideCommon(CharacterManager target)
        {
            if (!target.IsSpawned || target.characterNetworkManager == null)
            { SmallLabel("  (미스폰)"); return; }
            var nm = target.characterNetworkManager;

            SectionLabel("🔑 상태");
            BoolRow("isPerformingAction", target.isPerformingAction, true);
            BoolRow("canMove", target.canMove, false, invertWarning: true);
            BoolRow("isDead", nm.isDead.Value, true, danger: true);
            BoolRow("isLockedOn", nm.isLockedOn.Value, false);
            BoolRow("isSprinting", nm.isSprinting.Value, false);
            BoolRow("isChargingAtk", nm.isChargingAttack.Value, false);

            if (target.characterExecutionManager != null && target.IsSpawned)
                BoolRow("isBeingExecuted", target.characterExecutionManager.isBeingExecuted.Value, false);

            GUILayout.Space(4);
            float dist = Vector3.Distance(character.transform.position, target.transform.position);
            SmallLabel($"  거리 : {dist:F1}m");
            SmallLabel($"  타입 : {target.GetType().Name}");
        }

        private void DrawSideStats(CharacterManager target)
        {
            if (!target.IsSpawned || target.characterNetworkManager == null)
            { SmallLabel("  (미스폰)"); return; }
            var nm = target.characterNetworkManager;

            SectionLabel("❤ 생존 스탯");
            StatBar("HP", nm.currentHealth.Value, nm.maxHealth.Value, new Color(0.2f, 0.8f, 0.2f));
            StatBar("스태미나", nm.currentStamina.Value, nm.maxStamina.Value, new Color(0.9f, 0.75f, 0.1f));
            float poise = nm.currentPoise.Value, maxP = nm.maxPoise.Value;
            Color pc = poise <= 0f ? Color.red
                     : poise / Mathf.Max(maxP, 1f) < 0.3f ? new Color(1f, 0.5f, 0f)
                     : new Color(0.4f, 0.7f, 1f);
            StatBar("포이즈", poise, maxP, pc);
            if (poise <= 0f) WarningLabel("  ⚡ POISE BROKEN");
            if (target.isPoiseActive) InfoLabel("  🛡 POISE ACTIVE", new Color(1f, 0.9f, 0f));
        }

        private void DrawSideCombat(CharacterManager target)
        {
            var cm = target.characterCombatManager;
            if (cm == null) { SmallLabel("  CombatManager 없음"); return; }

            SectionLabel("⚔ 전투 상태");
            if (target.IsSpawned)
            {
                BoolRow("isAttacking", cm.isAttacking.Value, false);
                BoolRow("canCombo", cm.canCombo.Value, false);
            }
            SmallLabel($"  AttackType   : {cm.currentAttackType}");
            SmallLabel($"  lastActionHash : {cm.lastAttackAnimationPerformedHash}");
        }

        private void DrawSideTargetOfTarget(CharacterManager target)
        {
            var cm = target.characterCombatManager;
            CharacterManager tot = cm?.currentTarget;
            SectionLabel("🎯 타겟의 타겟");
            if (tot == null) { SmallLabel("  없음"); return; }
            InfoLabel($"  ▶ {tot.name}", new Color(1f, 0.9f, 0.4f));
            SmallLabel($"  타입 : {tot.GetType().Name}");
            if (tot.IsSpawned && tot.characterNetworkManager != null)
                BoolRow("isDead", tot.characterNetworkManager.isDead.Value, true, danger: true);
        }

        // =====================================================================
        // [NEW-1] 메인 파노라마 패널 (화면 하단, 여러 탭 동시 표시)
        // =====================================================================
        private void DrawMainPanorama(CharacterManager lockOnTarget)
        {
            int[] tabs = (panoramaTabs != null && panoramaTabs.Length > 0) ? panoramaTabs : GetDefaultPanoramaTabs();
            string[] allLabels = TabLabels;

            float panelW = PANORAMA_TAB_WIDTH;
            int tabCount = tabs.Length;
            float totalW = panelW * tabCount + 4f;
            float panelH = _panoramaMinimized ? PANORAMA_HEADER_H : (PANORAMA_HEADER_H + panoramaPanelHeight + 4f);

            // 최초 위치: 화면 하단 좌측
            if (_panoramaRect.width < 10f)
                _panoramaRect = new Rect(10f, Screen.height - panelH - 10f, totalW, panelH);
            _panoramaRect.width = totalW;
            _panoramaRect.height = panelH;

            HandleDrag(ref _panoramaRect, ref _panoramaIsDragging, ref _panoramaDragOffset);

            // 배경
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(_panoramaRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(_panoramaRect);

            // 파노라마 헤더
            GUILayout.BeginHorizontal(GUILayout.Height(PANORAMA_HEADER_H));
            GUILayout.Label("<b><color=#80DEEA>📊 파노라마</color></b>",
                HudStyle.Get(fontSize), GUILayout.ExpandWidth(true));
            // 최소화
            string minIcon = _panoramaMinimized ? "▲" : "▼";
            if (GUILayout.Button(minIcon, GUILayout.Width(22), GUILayout.Height(22)))
                _panoramaMinimized = !_panoramaMinimized;
            // 숨기기
            if (GUILayout.Button("×", GUILayout.Width(22), GUILayout.Height(22)))
                _panoramaVisible = false;
            GUILayout.EndHorizontal();

            if (!_panoramaMinimized)
            {
                GUILayout.BeginHorizontal();
                for (int i = 0; i < tabCount; i++)
                {
                    int tabIdx = tabs[i];
                    if (tabIdx < 0 || tabIdx >= allLabels.Length) continue;
                    string label = allLabels[tabIdx];

                    GUILayout.BeginVertical(GUILayout.Width(panelW));

                    // 탭 헤더 (클릭하면 메인 HUD도 해당 탭으로 이동)
                    if (GUILayout.Button($"<b><color=#80DEEA>{label}</color></b>",
                        HudStyle.PanoramaHeader(sectionFontSize), GUILayout.Height(20)))
                    {
                        _activeTab = tabIdx;
                        _scrollPos = Vector2.zero;
                    }

                    if (_panoramaScrollPos != null && tabIdx < _panoramaScrollPos.Length)
                    {
                        _panoramaScrollPos[tabIdx] = GUILayout.BeginScrollView(
                            _panoramaScrollPos[tabIdx],
                            GUILayout.Width(panelW),
                            GUILayout.Height(panoramaPanelHeight));
                    }
                    else
                    {
                        GUILayout.BeginScrollView(Vector2.zero,
                            GUILayout.Width(panelW),
                            GUILayout.Height(panoramaPanelHeight));
                    }

                    DrawTab(tabIdx);

                    GUILayout.EndScrollView();
                    GUILayout.EndVertical();

                    // 구분선
                    if (i < tabCount - 1)
                    {
                        Rect sep = GUILayoutUtility.GetRect(2f, panoramaPanelHeight + 20f,
                            GUILayout.Width(2f));
                        GUI.color = new Color(0.4f, 0.6f, 0.8f, 0.4f);
                        GUI.DrawTexture(sep, Texture2D.whiteTexture);
                        GUI.color = Color.white;
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        private int[] GetDefaultPanoramaTabs()
        {
            string[] labels = TabLabels;
            int[] result = new int[labels.Length];
            for (int i = 0; i < labels.Length; i++) result[i] = i;
            return result;
        }

        // =====================================================================
        // [NEW-2] 락온 타겟 파노라마 패널 (메인 파노라마 바로 위)
        // =====================================================================
        private void DrawTargetPanoramaPanel(CharacterManager target)
        {
            float panelH = _targetPanoMinimized ? PANORAMA_HEADER_H : (PANORAMA_HEADER_H + 140f + 4f);
            float mainPanoH = (_panoramaVisible && !_panoramaMinimized)
                ? (PANORAMA_HEADER_H + panoramaPanelHeight + 4f)
                : PANORAMA_HEADER_H;
            float mainPanoY = _panoramaRect.width > 10f ? _panoramaRect.y : (Screen.height - mainPanoH - 10f);

            if (_targetPanoRect.width < 10f)
            {
                float tpY = mainPanoY - panelH - 4f;
                _targetPanoRect = new Rect(10f, tpY, PANORAMA_TAB_WIDTH * 3f, panelH);
            }
            _targetPanoRect.height = panelH;

            HandleDrag(ref _targetPanoRect, ref _targetPanoIsDragging, ref _targetPanoDragOffset);

            GUI.color = new Color(0f, 0.04f, 0.1f, 0.88f);
            GUI.DrawTexture(_targetPanoRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(_targetPanoRect);

            // 헤더
            GUILayout.BeginHorizontal(GUILayout.Height(PANORAMA_HEADER_H));
            GUILayout.Label(
                $"<b><color=#FF8A65>🎯 타겟 파노라마: {target.name}</color></b>",
                HudStyle.Get(fontSize), GUILayout.ExpandWidth(true));
            string minIcon = _targetPanoMinimized ? "▲" : "▼";
            if (GUILayout.Button(minIcon, GUILayout.Width(22), GUILayout.Height(22)))
                _targetPanoMinimized = !_targetPanoMinimized;
            if (GUILayout.Button("×", GUILayout.Width(22), GUILayout.Height(22)))
                _targetPanoVisible = false;
            GUILayout.EndHorizontal();

            if (!_targetPanoMinimized)
            {
                _targetPanoScroll = GUILayout.BeginScrollView(_targetPanoScroll,
                    GUILayout.Height(140f));
                DrawTargetPanoramaContent(target);
                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();
        }

        private void DrawTargetPanoramaContent(CharacterManager target)
        {
            if (!target.IsSpawned || target.characterNetworkManager == null)
            { SmallLabel("  (타겟 미스폰)"); return; }

            var nm = target.characterNetworkManager;

            // 3열 레이아웃 — 스탯 / 상태 플래그 / 전투
            GUILayout.BeginHorizontal();

            // 열1: 스탯 바
            GUILayout.BeginVertical(GUILayout.Width(PANORAMA_TAB_WIDTH - 4f));
            SectionLabel("❤ 스탯");
            StatBar("HP", nm.currentHealth.Value, nm.maxHealth.Value, new Color(0.2f, 0.8f, 0.2f));
            StatBar("스태미나", nm.currentStamina.Value, nm.maxStamina.Value, new Color(0.9f, 0.75f, 0.1f));
            float tp = nm.currentPoise.Value, tmp = nm.maxPoise.Value;
            Color tpc = tp <= 0f ? Color.red : tp / Mathf.Max(tmp, 1f) < 0.3f ? new Color(1f, 0.5f, 0f) : new Color(0.4f, 0.7f, 1f);
            StatBar("포이즈", tp, tmp, tpc);
            if (tp <= 0f) WarningLabel("  ⚡ POISE BROKEN");
            GUILayout.EndVertical();

            // 열2: 상태 플래그
            GUILayout.BeginVertical(GUILayout.Width(PANORAMA_TAB_WIDTH - 4f));
            SectionLabel("🔑 상태");
            BoolRow("isPerformingAction", target.isPerformingAction, true);
            BoolRow("isDead", nm.isDead.Value, true, danger: true);
            BoolRow("isSprinting", nm.isSprinting.Value, false);
            BoolRow("isChargingAtk", nm.isChargingAttack.Value, false);
            if (target.characterExecutionManager != null && target.IsSpawned)
                BoolRow("isBeingExecuted", target.characterExecutionManager.isBeingExecuted.Value, false);
            GUILayout.EndVertical();

            // 열3: 전투
            GUILayout.BeginVertical(GUILayout.Width(PANORAMA_TAB_WIDTH - 4f));
            SectionLabel("⚔ 전투");
            var cm = target.characterCombatManager;
            if (cm != null && target.IsSpawned)
            {
                BoolRow("isAttacking", cm.isAttacking.Value, false);
                BoolRow("canCombo", cm.canCombo.Value, false);
                SmallLabel($"  AtkType: {cm.currentAttackType}");
                string tgt = cm.currentTarget != null ? cm.currentTarget.name : "없음";
                SmallLabel($"  Target: {tgt}");
            }
            else SmallLabel("  CombatManager 없음");
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        // =====================================================================
        // 탭 내용 라우팅 (서브클래스에서 override 가능)
        // =====================================================================
        protected virtual void DrawTab(int tab)
        {
            switch (tab)
            {
                case 0: DrawCommonPanel(); break;
                case 1: DrawEventHistoryPanel(); break;
                case 2: DrawAnimHistoryPanel(); break;
                case 3: DrawExecutionQTEPanel(); break;
                case 4: DrawTargetPanel(); break;
                case 5: DrawNullCheckPanel(); break;
            }
        }

        // ── 타이틀 바 ─────────────────────────────────────────────────────────
        private void DrawMainTitleBar()
        {
            GUILayout.BeginHorizontal(GUILayout.Height(26));
            string stateStr = GetCurrentStateName();
            GUILayout.Label(
                $"<b><color=#4FC3F7>{character.name}</color></b>  <color=#B0BEC5>{stateStr}</color>",
                HudStyle.Get(fontSize), GUILayout.ExpandWidth(true));

            // [NEW-3] 파노라마 토글
            string panoIcon = (_panoramaVisible && showPanorama) ? "📊" : "📉";
            if (GUILayout.Button(panoIcon, GUILayout.Width(24), GUILayout.Height(22)))
            {
                if (!showPanorama) showPanorama = true;
                _panoramaVisible = !_panoramaVisible;
            }

            // [NEW-3] 최소화
            string minIcon = _hudMinimized ? "▲" : "▼";
            if (GUILayout.Button(minIcon, GUILayout.Width(22), GUILayout.Height(22)))
                _hudMinimized = !_hudMinimized;

            // 닫기
            if (GUILayout.Button("×", GUILayout.Width(22), GUILayout.Height(22)))
                showDebugHUD = false;
            GUILayout.EndHorizontal();
        }

        protected virtual string GetCurrentStateName() => character.GetType().Name;

        // ── 탭 스트립 ─────────────────────────────────────────────────────────
        private void DrawTabStrip()
        {
            string[] labels = TabLabels;
            int newTab = GUILayout.Toolbar(_activeTab, labels,
                HudStyle.TabStyle, GUILayout.Height(24));
            if (newTab != _activeTab) { _activeTab = newTab; _scrollPos = Vector2.zero; }
        }

        // ── 하단 툴바 ─────────────────────────────────────────────────────────
        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(GUILayout.Height(26));
            if (GUILayout.Button("▶", GUILayout.Height(22))) EditorApplication.isPaused = false;
            if (GUILayout.Button("⏸", GUILayout.Height(22))) EditorApplication.isPaused = true;
            if (GUILayout.Button("🗑 초기화", GUILayout.Height(22))) ClearHistory();

            // [NEW-3] 사이드 HUD 토글
            string sideIcon = showTargetSideHUD ? "🎯 ON" : "🎯 OFF";
            if (GUILayout.Button(sideIcon, GUILayout.Height(22)))
            {
                showTargetSideHUD = !showTargetSideHUD;
                if (showTargetSideHUD) _targetPanoVisible = true; // 사이드 켜면 타겟 파노라마도 리셋
            }
            GUILayout.EndHorizontal();
        }

        // =====================================================================
        // Panel 0 — 공통 상태
        // =====================================================================
        protected virtual void DrawCommonPanel()
        {
            if (showSectionFlags)
            {
                _foldFlags = DrawFoldout(_foldFlags, "🔑 상태 플래그");
                if (_foldFlags)
                {
                    BoolRow("isPerformingAction", character.isPerformingAction, true);
                    BoolRow("applyRootMotion",
                        character.animator != null && character.animator.applyRootMotion, false);
                    BoolRow("canMove", character.canMove, false, invertWarning: true);
                    BoolRow("canRotate", character.canRotate, false, invertWarning: true);
                    BoolRow("isPoiseActive", character.isPoiseActive, false);

                    if (character.IsSpawned && character.characterNetworkManager != null)
                    {
                        var nm = character.characterNetworkManager;
                        BoolRow("isDead", nm.isDead.Value, true, danger: true);
                        BoolRow("isLockedOn", nm.isLockedOn.Value, false);
                        BoolRow("isSprinting", nm.isSprinting.Value, false);
                        BoolRow("isChargingAtk", nm.isChargingAttack.Value, false);
                    }
                }
            }

            if (showSectionStats && character.IsSpawned && character.characterNetworkManager != null)
            {
                _foldStats = DrawFoldout(_foldStats, "❤ 생존 스탯");
                if (_foldStats)
                {
                    var nm = character.characterNetworkManager;
                    StatBar("HP", nm.currentHealth.Value, nm.maxHealth.Value, new Color(0.2f, 0.8f, 0.2f));
                    StatBar("스태미나", nm.currentStamina.Value, nm.maxStamina.Value, new Color(0.9f, 0.75f, 0.1f));
                    float poise = nm.currentPoise.Value;
                    float maxP = nm.maxPoise.Value;
                    Color pc = poise <= 0f ? Color.red
                             : poise / Mathf.Max(maxP, 1f) < 0.3f ? new Color(1f, 0.5f, 0f)
                             : new Color(0.4f, 0.7f, 1f);
                    StatBar("포이즈", poise, maxP, pc);
                    if (poise <= 0f) WarningLabel("⚡ POISE BROKEN");
                    if (character.isPoiseActive) InfoLabel("🛡 POISE ACTIVE", new Color(1f, 0.9f, 0f));
                    if (_poiseBreakCount > 0)
                        SmallLabel($"  포이즈 파괴: {_poiseBreakCount}회  마지막: {_lastPoiseBreakTime:F1}s");
                }
            }

            if (showSectionCombat && character.characterCombatManager != null)
            {
                _foldCombat = DrawFoldout(_foldCombat, "⚔ 전투 상태");
                if (_foldCombat)
                {
                    var cm = character.characterCombatManager;
                    if (character.IsSpawned)
                    {
                        BoolRow("isAttacking", cm.isAttacking.Value, false);
                        BoolRow("canCombo", cm.canCombo.Value, false);
                    }
                    SmallLabel($"  AttackType   : {cm.currentAttackType}");
                    SmallLabel($"  lastActionHash : {cm.lastAttackAnimationPerformedHash}");
                    string tgt = cm.currentTarget != null ? cm.currentTarget.name : "없음";
                    SmallLabel($"  currentTarget : {tgt}");
                }
            }
        }

        // =====================================================================
        // Panel 1 — 이벤트 히스토리
        // =====================================================================
        private void DrawEventHistoryPanel()
        {
            SectionLabel($"📡 이벤트 히스토리 ({_eventCount}개)");
            if (_eventCount == 0) { GUILayout.Label("  (없음)", HudStyle.SmallGray(fontSize)); return; }
            for (int i = 0; i < _eventCount; i++)
            {
                int idx = ((_eventHead - 1 - i) % maxHistoryCount + maxHistoryCount) % maxHistoryCount;
                var e = _eventHistory[idx];
                string col = (Time.time - e.time) < 0.5f ? "#FFD54F" : "#90A4AE";
                GUILayout.Label($"<color={col}>[{e.time:F2}s]</color>  <b>{e.type}</b>",
                    HudStyle.Get(fontSize));
            }
        }
        protected void DrawEventHistoryPanel_Proxy() => DrawEventHistoryPanel();

        // =====================================================================
        // Panel 2 — 애니메이션 히스토리
        // =====================================================================
        private void DrawAnimHistoryPanel()
        {
            SectionLabel($"🎬 애니메이션 히스토리 ({_animCount}개)");
            if (_animCount == 0) { GUILayout.Label("  (없음 — AnimationManager 연동 필요)", HudStyle.SmallGray(fontSize)); return; }
            for (int i = 0; i < _animCount; i++)
            {
                int idx = ((_animHead - 1 - i) % maxHistoryCount + maxHistoryCount) % maxHistoryCount;
                var a = _animHistory[idx];
                bool isRecent = (Time.time - a.time) < 0.5f;
                string col = isRecent ? "#FFD54F" : "#90A4AE";
                string perf = a.isPerformingAction ? "<color=#EF9A9A>●</color>" : "<color=#90A4AE>○</color>";
                string idStr = ResolveActionIDLabel(a.actionID);
                GUILayout.Label(
                    $"{perf} <color={col}>[{a.time:F2}s]</color>  {idStr}  {a.label}",
                    HudStyle.Get(fontSize));
            }
        }
        protected void DrawAnimHistoryPanel_Proxy() => DrawAnimHistoryPanel();

        // [REQ-6] ActionID를 enum 변수명으로 변환
        protected string ResolveActionIDLabel(int id)
        {
            if (!showActionIDEnumName) return $"ID:{id}";
            if (Enum.IsDefined(typeof(ActionID), id))
                return $"<color=#CE93D8>{(ActionID)id}</color>(<color=#90A4AE>{id}</color>)";
            if (fallbackToAnimEventEnum && Enum.IsDefined(typeof(AnimationEventType), id))
                return $"<color=#80DEEA>evt:{(AnimationEventType)id}</color>(<color=#90A4AE>{id}</color>)";
            return $"ID:{id}";
        }

        // =====================================================================
        // [REQ-1] Panel 3 — 처형 / QTE 상태
        // =====================================================================
        protected virtual void DrawExecutionQTEPanel()
        {
            if (showSectionExecution)
            {
                _foldExec = DrawFoldout(_foldExec, "⚔ 처형 시스템");
                if (_foldExec) DrawExecutionSection();
            }
            if (showSectionQTE)
            {
                _foldQTE = DrawFoldout(_foldQTE, "🎯 QTE 상태");
                if (_foldQTE) DrawQTESection();
            }
        }

        protected virtual void DrawExecutionSection()
        {
            var execMgr = character.characterExecutionManager;
            if (execMgr == null) { WarningLabel("  characterExecutionManager = null"); return; }
            if (character.IsSpawned)
                BoolRow("isBeingExecuted", execMgr.isBeingExecuted.Value, false);
            else
                SmallLabel("  (미스폰)");
        }

        protected virtual void DrawQTESection()
        {
            var qteMgr = character.GetComponent<TDA.Character.CharacterQTEManager>();
            if (qteMgr == null) { SmallLabel("  CharacterQTEManager 없음"); return; }
            BoolRow("isQTEActive", qteMgr.isQTEActive, false);
            SmallLabel($"  phaseIndex : {qteMgr.currentPhaseIndex}");
            SmallLabel($"  qteTarget  : {(qteMgr.qteTarget != null ? qteMgr.qteTarget.name : "없음")}");
        }

        // =====================================================================
        // [REQ-2] Panel 4 — 락온 타겟 정보
        // =====================================================================
        protected virtual void DrawTargetPanel()
        {
            var cm = character.characterCombatManager;
            CharacterManager target = cm?.currentTarget;

            if (showSectionTargetInfo)
            {
                _foldTarget = DrawFoldout(_foldTarget, "🎯 타겟 정보");
                if (_foldTarget)
                {
                    if (target == null) { SmallLabel("  타겟 없음"); }
                    else
                    {
                        InfoLabel($"  ▶ {target.name}", new Color(1f, 0.9f, 0.4f));
                        SmallLabel($"  타입 : {target.GetType().Name}");
                        SmallLabel($"  거리 : {Vector3.Distance(character.transform.position, target.transform.position):F1}m");
                        if (target.IsSpawned && target.characterNetworkManager != null)
                        {
                            var tnm = target.characterNetworkManager;
                            BoolRow("  isDead", tnm.isDead.Value, true, danger: true);
                            BoolRow("  isLockedOn", tnm.isLockedOn.Value, false);
                        }
                    }
                }
            }

            if (showSectionTargetPanorama && target != null)
            {
                _foldTargetPano = DrawFoldout(_foldTargetPano, "📊 타겟 파노라마");
                if (_foldTargetPano) DrawTargetPanorama(target);
            }
        }

        private void DrawTargetPanorama(CharacterManager target)
        {
            if (!target.IsSpawned || target.characterNetworkManager == null)
            { SmallLabel("  (타겟 미스폰)"); return; }
            var tnm = target.characterNetworkManager;
            StatBar("HP", tnm.currentHealth.Value, tnm.maxHealth.Value, new Color(0.2f, 0.8f, 0.2f));
            StatBar("스태미나", tnm.currentStamina.Value, tnm.maxStamina.Value, new Color(0.9f, 0.75f, 0.1f));
            float tp = tnm.currentPoise.Value, tmp = tnm.maxPoise.Value;
            Color tpc = tp <= 0f ? Color.red : tp / Mathf.Max(tmp, 1f) < 0.3f ? new Color(1f, 0.5f, 0f) : new Color(0.4f, 0.7f, 1f);
            StatBar("포이즈", tp, tmp, tpc);
            GUILayout.Space(3);
            BoolRow("isPerformingAction", target.isPerformingAction, true);
            BoolRow("isSprinting", tnm.isSprinting.Value, false);
            BoolRow("isChargingAtk", tnm.isChargingAttack.Value, false);
            if (target.characterExecutionManager != null && target.IsSpawned)
                BoolRow("isBeingExecuted", target.characterExecutionManager.isBeingExecuted.Value, false);
        }

        // =====================================================================
        // Panel 5 — Null 체크
        // =====================================================================
        protected virtual void DrawNullCheckPanel()
        {
            SectionLabel("🔴 참조 체크 (Panel F)");
            foreach (var entry in _nullEntries)
            {
                GUILayout.BeginHorizontal();
                string icon = entry.isNull
                    ? $"<color={(entry.isCritical ? "#EF5350" : "#FF9800")}>✗</color>"
                    : "<color=#66BB6A>✓</color>";
                GUILayout.Label(icon, HudStyle.Get(fontSize), GUILayout.Width(14));
                GUILayout.Label(entry.name, HudStyle.SmallLabel(fontSize), GUILayout.Width(175));
                if (entry.isNull)
                    GUILayout.Label(entry.symptom, HudStyle.SmallGray(fontSize), GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(4);
            int nullCount = 0, critCount = 0;
            foreach (var e in _nullEntries) { if (e.isNull) { nullCount++; if (e.isCritical) critCount++; } }
            if (nullCount > 0) WarningLabel($"  Null {nullCount}개 (치명적: {critCount}개)");
            else InfoLabel("  모든 참조 정상", new Color(0.4f, 1f, 0.4f));
        }

        // =====================================================================
        // [REQ-3] 접기/펼치기 헬퍼
        // =====================================================================
        protected bool DrawFoldout(bool state, string label)
        {
            GUILayout.Space(3);
            GUILayout.BeginHorizontal();
            string arrow = state ? "▼" : "▶";
            if (GUILayout.Button($"{arrow} {label}", HudStyle.FoldoutBtn(sectionFontSize),
                GUILayout.ExpandWidth(true), GUILayout.Height(20)))
                state = !state;
            GUILayout.EndHorizontal();
            return state;
        }

        // =====================================================================
        // 드래그 처리 (공통 헬퍼)
        // =====================================================================
        private void HandleDrag(ref Rect rect, ref bool isDragging, ref Vector2 dragOffset)
        {
            Event e = Event.current;
            Rect titleRect = new Rect(rect.x, rect.y, rect.width, 26f);
            if (e.type == EventType.MouseDown && titleRect.Contains(e.mousePosition))
            {
                isDragging = true;
                dragOffset = e.mousePosition - new Vector2(rect.x, rect.y);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && isDragging)
            {
                rect.x = Mathf.Clamp(e.mousePosition.x - dragOffset.x, 0, Screen.width - rect.width);
                rect.y = Mathf.Clamp(e.mousePosition.y - dragOffset.y, 0, Screen.height - rect.height);
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
                isDragging = false;
        }

        // =====================================================================
        // GUI 헬퍼
        // =====================================================================
        protected void SectionLabel(string text)
        {
            GUILayout.Space(4);
            GUILayout.Label(text, HudStyle.Section(sectionFontSize));
            GUILayout.Space(2);
        }

        protected void BoolRow(string label, bool value, bool warnOnTrue,
            bool invertWarning = false, bool danger = false)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, HudStyle.SmallLabel(fontSize), GUILayout.Width(170));
            bool bad = danger ? value : (warnOnTrue ? value : (invertWarning ? !value : false));
            string icon = value ? "● ON" : "○ OFF";
            string col = bad ? "#EF5350" : (value ? "#66BB6A" : "#90A4AE");
            GUILayout.Label($"<color={col}><b>{icon}</b></color>", HudStyle.Get(fontSize));
            GUILayout.EndHorizontal();
        }

        protected void StatBar(string label, float current, float max, Color barColor)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"  {label}", HudStyle.SmallLabel(fontSize), GUILayout.Width(70));
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Height(12), GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.2f, 0.2f, 0.2f); GUI.DrawTexture(r, Texture2D.whiteTexture);
            float ratio = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            Rect fill = new Rect(r.x, r.y, r.width * ratio, r.height);
            GUI.color = barColor; GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.Label($"  {current:F0}/{max:F0}", HudStyle.SmallLabel(fontSize), GUILayout.Width(70));
            GUILayout.EndHorizontal();
        }

        protected void WarningLabel(string text)
            => GUILayout.Label($"<color=#EF5350><b>{text}</b></color>", HudStyle.Get(fontSize));

        protected void InfoLabel(string text, Color c)
        {
            string hex = ColorUtility.ToHtmlStringRGB(c);
            GUILayout.Label($"<color=#{hex}>{text}</color>", HudStyle.Get(fontSize));
        }

        protected void SmallLabel(string text)
            => GUILayout.Label(text, HudStyle.SmallGray(fontSize));

        // =====================================================================
        // [REQ-7] 동적 GUIStyle (fontSize 파라미터 반영)
        // =====================================================================
        protected static class HudStyle
        {
            private static int _cachedFontSize = -1;
            private static int _cachedSectionSize = -1;
            private static GUIStyle _richLabel, _section, _smallLabel, _smallGray, _tab, _foldBtn, _panoHeader;

            public static GUIStyle Get(int fs)
            {
                if (_richLabel == null || _cachedFontSize != fs)
                {
                    _cachedFontSize = fs;
                    _richLabel = new GUIStyle(GUI.skin.label)
                    {
                        richText = true,
                        fontSize = fs,
                        wordWrap = false,
                        normal = { textColor = Color.white },
                        padding = new RectOffset(4, 4, 1, 1),
                    };
                }
                return _richLabel;
            }

            public static GUIStyle Section(int fs)
            {
                if (_section == null || _cachedSectionSize != fs)
                {
                    _cachedSectionSize = fs;
                    _section = new GUIStyle(GUI.skin.label)
                    {
                        richText = true,
                        fontSize = fs,
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = new Color(0.6f, 0.85f, 1f) },
                        padding = new RectOffset(4, 4, 2, 2),
                    };
                }
                return _section;
            }

            public static GUIStyle SmallLabel(int fs)
            {
                return new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(fs - 1, 8),
                    normal = { textColor = new Color(0.85f, 0.85f, 0.85f) },
                    padding = new RectOffset(4, 4, 1, 1),
                };
            }

            public static GUIStyle SmallGray(int fs)
            {
                return new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(fs - 1, 8),
                    normal = { textColor = new Color(0.55f, 0.55f, 0.55f) },
                    padding = new RectOffset(4, 4, 1, 1),
                };
            }

            public static GUIStyle TabStyle
            {
                get
                {
                    if (_tab == null)
                        _tab = new GUIStyle(GUI.skin.button) { fontSize = 10, padding = new RectOffset(4, 4, 2, 2) };
                    return _tab;
                }
            }

            public static GUIStyle FoldoutBtn(int fs)
            {
                return new GUIStyle(GUI.skin.button)
                {
                    fontSize = fs,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = new Color(0.7f, 0.9f, 1f), background = MakeTex(new Color(0.15f, 0.2f, 0.3f, 0.8f)) },
                    padding = new RectOffset(6, 4, 2, 2),
                };
            }

            // [NEW-1] 파노라마 패널 헤더 버튼 스타일
            public static GUIStyle PanoramaHeader(int fs)
            {
                if (_panoHeader == null)
                {
                    _panoHeader = new GUIStyle(GUI.skin.button)
                    {
                        fontSize = fs,
                        fontStyle = FontStyle.Bold,
                        richText = true,
                        alignment = TextAnchor.MiddleLeft,
                        normal = { textColor = new Color(0.5f, 0.88f, 1f), background = MakeTex(new Color(0.1f, 0.18f, 0.28f, 1f)) },
                        padding = new RectOffset(6, 4, 2, 2),
                    };
                }
                return _panoHeader;
            }

            private static Texture2D MakeTex(Color c)
            {
                var t = new Texture2D(1, 1);
                t.SetPixel(0, 0, c);
                t.Apply();
                return t;
            }
        }
#endif

        // =====================================================================
        // 히스토리 초기화 (빌드에서도 호출 가능)
        // =====================================================================
        public void ClearHistory()
        {
            _eventHead = 0; _eventCount = 0;
            _animHead = 0; _animCount = 0;
            _poiseBreakCount = 0;
        }
    }
}