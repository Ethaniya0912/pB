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
        // HUD 레이아웃 상태
        // =====================================================================
        private Rect _hudRect = new Rect(10, 10, 300, 520);
        private bool _isDragging = false;
        private Vector2 _dragOffset = Vector2.zero;
        private int _activeTab = 0;
        private Vector2 _scrollPos;

        // [REQ-3] 섹션 접기 상태 (공통 탭)
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
            _hudRect.height = hudHeight;
        }

        // [REQ-5] ESC로 커서 잠금 해제
        private void HandleCursorUnlock()
        {
#if UNITY_EDITOR
            bool isLocked = Cursor.lockState == CursorLockMode.Locked;

            // 잠금 상태 진입 감지
            if (isLocked && !_cursorWasLocked)
                _cursorWasLocked = true;

            // ESC 입력 감지 → 커서 복원
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
        // OnGUI
        // =====================================================================
#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!showDebugHUD || !Application.isPlaying || character == null) return;

            // [REQ-5] 커서 잠금 중에는 HUD 처리 건너뜀 (버튼 오작동 방지)
            if (Cursor.lockState == CursorLockMode.Locked) return;

            HandleDrag();

            GUI.color = new Color(0f, 0f, 0f, 0.84f);
            GUI.DrawTexture(_hudRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(_hudRect);
            DrawTitleBar();
            DrawTabStrip();

            float contentH = _hudRect.height - 82f;
            _scrollPos = GUILayout.BeginScrollView(_scrollPos,
                GUILayout.Width(_hudRect.width),
                GUILayout.Height(Mathf.Max(contentH, 50f)));

            DrawTab(_activeTab);

            GUILayout.EndScrollView();
            DrawToolbar();
            GUILayout.EndArea();
        }

        // ── 탭 내용 라우팅 (서브클래스에서 override 가능) ───────────────────
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
        private void DrawTitleBar()
        {
            GUILayout.BeginHorizontal(GUILayout.Height(26));
            string stateStr = GetCurrentStateName();
            GUILayout.Label(
                $"<b><color=#4FC3F7>{character.name}</color></b>  <color=#B0BEC5>{stateStr}</color>",
                HudStyle.Get(fontSize), GUILayout.ExpandWidth(true));
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
            GUILayout.EndHorizontal();
        }

        // =====================================================================
        // Panel 0 — 공통 상태
        // =====================================================================
        protected virtual void DrawCommonPanel()
        {
            // [REQ-3] 섹션별 접기
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
        // Proxy for subclasses
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

                // [REQ-6] ActionID enum 원본 변수명 표시
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

            // ActionID enum 검색
            if (Enum.IsDefined(typeof(ActionID), id))
                return $"<color=#CE93D8>{(ActionID)id}</color>(<color=#90A4AE>{id}</color>)";

            // 폴백: AnimationEventType enum 검색
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
            // 베이스: CharacterQTEManager 체크
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

        // [REQ-2] 타겟 파노라마 — 타겟의 HP/스태미나/포이즈/전투상태 한눈에
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

            // 타겟의 처형 상태
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
        // 드래그 처리
        // =====================================================================
        private void HandleDrag()
        {
            Event e = Event.current;
            Rect titleRect = new Rect(_hudRect.x, _hudRect.y, _hudRect.width, 26f);
            if (e.type == EventType.MouseDown && titleRect.Contains(e.mousePosition))
            {
                _isDragging = true;
                _dragOffset = e.mousePosition - new Vector2(_hudRect.x, _hudRect.y);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _isDragging)
            {
                _hudRect.x = Mathf.Clamp(e.mousePosition.x - _dragOffset.x, 0, Screen.width - _hudRect.width);
                _hudRect.y = Mathf.Clamp(e.mousePosition.y - _dragOffset.y, 0, Screen.height - _hudRect.height);
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
                _isDragging = false;
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
            // 캐시: fontSize가 바뀌면 재생성
            private static int _cachedFontSize = -1;
            private static int _cachedSectionSize = -1;
            private static GUIStyle _richLabel, _section, _smallLabel, _smallGray, _tab, _foldBtn;

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