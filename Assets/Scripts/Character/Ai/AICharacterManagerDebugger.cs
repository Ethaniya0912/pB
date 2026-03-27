// =============================================================================
// AICharacterManagerDebugger.cs  |  TDA Project
// Layer  : Editor Tool — AI 전용 디버거
//
// 변경 이력:
//   [REQ-1] Execution/QTE 탭 확장 (isExecutable 포함)
//   [REQ-2] 타겟 파노라마 탭 (베이스 상속)
//   [REQ-3] FSM/포이즈/NavMesh/처형 섹션 개별 접기
//   [REQ-6] ActionID enum 이름 표시 (베이스 상속)
//   [REQ-7] 창 크기/텍스트 크기 인스펙터 파라미터 (베이스 상속)
//   [REQ-8] 각 탭 표시 항목 플래그
//   AIExecutionManager → AICharacterExecutionManager 이름 변경 대응
//   [NEW-1] 파노라마 패널 — AI 전용 기본 탭: 공통(0), FSM(5), AI상세(6), 타겟(4)
//   [NEW-2] 락온 사이드 HUD (베이스 상속)
//   [NEW-3] 최소화/숨기기 기능 (베이스 상속)
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using TDA.Character;
using TDA.Character.AI;
using TDA.EditorTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TDA.EditorTools
{
    [DisallowMultipleComponent]
    public class AICharacterManagerDebugger : CharacterManagerDebugger
    {
        // =====================================================================
        // [REQ-8] AI 탭 표시 플래그
        // =====================================================================
        [Header("─── AI 표시 항목 제어 ─────────────────────────────")]
        public bool showSectionFSM = true;
        public bool showSectionPoise = true;
        public bool showSectionNavMesh = true;
        public bool showSectionAIExec = true;

        // =====================================================================
        // Pause 트리거
        // =====================================================================
        [Header("─── AI Pause 트리거 ──────────────────────────────")]
        public bool pauseOnGroggy = false;
        public bool pauseOnExecutionStart = false;
        public AIState pauseOnStateEnter;

        // =====================================================================
        // 내부 참조
        // =====================================================================
        private AICharacterManager _ai;
        private NavMeshAgent _nav;
        private CharacterExecutionManager _execMgr;

        // =====================================================================
        // FSM 히스토리
        // =====================================================================
        private struct StateEntry
        {
            public float enterTime, exitTime;
            public string stateName;
        }
        private const int MAX_STATE = 30;
        private StateEntry[] _stateHist = new StateEntry[MAX_STATE];
        private int _stateHead = 0, _stateCount = 0;
        private AIState _prevState = null;
        private float _stateEnterTime = 0f;

        private float _groggyTimer = 0f;
        private bool _isInGroggy = false;
        private int _execCount = 0;
        private bool _prevExec = false;

        // 포이즈 로컬 추적
        private float _aiPrevPoise = -1f;
        private int _aiPoiseBreakCnt = 0;
        private float _aiLastPoiseBreak = -1f;

        // [REQ-3] 섹션 접기 상태
        private bool _foldFSM = true;
        private bool _foldFSMHist = true;
        private bool _foldAIPoise = true;
        private bool _foldNav = true;
        private bool _foldAIExec = true;
        private Vector2 _fsmScroll;

        // 탭
        protected override string[] TabLabels =>
            new[] { "공통", "이벤트", "애니", "처형/QTE", "타겟", "FSM", "AI상세", "참조" };

        // =====================================================================
        // 생명주기
        // =====================================================================
        protected override void Awake()
        {
            base.Awake();
            CacheComponents();

            // AI 전용 파노라마 기본 탭 설정: 공통(0), FSM(5), AI상세(6), 타겟(4)
            if (panoramaTabs == null || panoramaTabs.Length == 0)
                panoramaTabs = new int[] { 0, 5, 4 };
        }

        protected override void CacheComponents()
        {
            base.CacheComponents();
            _ai = GetComponent<AICharacterManager>();
            _nav = GetComponent<NavMeshAgent>();
            _execMgr = GetComponent<CharacterExecutionManager>();
        }

        protected override void Update()
        {
            base.Update();
            if (!showDebugHUD || _ai == null) return;
            PollState();
            TrackGroggy();
            TrackExec();
            TrackAIPoise();
        }

        // =====================================================================
        // FSM 변화 감지
        // =====================================================================
        private void PollState()
        {
            AIState cur = _ai.currentState;
            if (cur == _prevState) return;

            if (_prevState != null && _stateCount > 0)
            {
                int last = ((_stateHead - 1) % MAX_STATE + MAX_STATE) % MAX_STATE;
                _stateHist[last].exitTime = Time.time;
            }

            int idx = _stateHead % MAX_STATE;
            _stateHist[idx] = new StateEntry
            {
                enterTime = Time.time,
                exitTime = 0f,
                stateName = cur != null ? cur.GetType().Name : "null"
            };
            _stateHead++;
            _stateCount = Mathf.Min(_stateCount + 1, MAX_STATE);
            _stateEnterTime = Time.time;

            bool enterGroggy = cur is GroggyState;
            if (!_isInGroggy && enterGroggy)
            {
                _isInGroggy = true;
                _groggyTimer = 0f;
#if UNITY_EDITOR
                if (pauseOnGroggy) EditorApplication.isPaused = true;
#endif
            }
            else if (_isInGroggy && !enterGroggy)
                _isInGroggy = false;

#if UNITY_EDITOR
            if (pauseOnStateEnter != null && cur == pauseOnStateEnter)
                EditorApplication.isPaused = true;
#endif
            _prevState = cur;
        }

        private void TrackGroggy()
        {
            if (!_isInGroggy) return;
            bool executing = _execMgr != null && _ai.IsSpawned && _execMgr.isBeingExecuted.Value;
            if (!executing) _groggyTimer += Time.deltaTime;
        }

        private void TrackExec()
        {
            if (_execMgr == null || !_ai.IsSpawned) return;
            bool curr = _execMgr.isBeingExecuted.Value;
            if (!_prevExec && curr)
            {
                _execCount++;
#if UNITY_EDITOR
                if (pauseOnExecutionStart) EditorApplication.isPaused = true;
#endif
            }
            _prevExec = curr;
        }

        private void TrackAIPoise()
        {
            if (!_ai.IsSpawned || _ai.characterNetworkManager == null) return;
            float p = _ai.characterNetworkManager.currentPoise.Value;
            if (_aiPrevPoise > 0f && p <= 0f) { _aiPoiseBreakCnt++; _aiLastPoiseBreak = Time.time; }
            _aiPrevPoise = p;
        }

        // =====================================================================
        // Null 체크 추가
        // =====================================================================
        protected override void RefreshNullCheck()
        {
            base.RefreshNullCheck();
            if (_ai == null) return;
            Add("aiCharacterCombatManager", _ai.aiCharacterCombatManager == null, true, "탐지·공격 없음");
            Add("aiCharacterLocomotionManager", _ai.aiCharacterLocomotionManager == null, true, "NavMesh 이동 없음");
            Add("navMeshAgent", _nav == null, true, "경로 계산 없음");
            Add("characterExecutionManager", _execMgr == null, false, "처형 isExecutable 없음");
            Add("defaultState (SO)", _ai.defaultState == null, true, "FSM Tick 미실행");
            Add("currentState (런타임)", _ai.currentState == null, true, "Update NullRef");

            if (_ai.currentState is CombatStanceState css)
            {
                Add("CombatStanceState.groggyState", css.groggyState == null, false, "포이즈 파괴 전환 없음");
                Add("CombatStanceState.attackState", css.attackState == null, false, "공격 전환 없음");
                Add("CombatStanceState.pursueTargetState", css.pursueTargetState == null, false, "추격 전환 없음");
            }
            if (_ai.currentState is GroggyState grog)
                Add("GroggyState.combatStanceState", grog.combatStanceState == null, false, "그로기 복귀 없음");
        }

        // =====================================================================
        // AI 탭 라우팅
        // =====================================================================
        protected override void DrawTab(int tab)
        {
            switch (tab)
            {
                case 0: DrawCommonPanel(); break;
                case 1: DrawEventHistoryPanel_Proxy(); break;
                case 2: DrawAnimHistoryPanel_Proxy(); break;
                case 3: DrawExecutionQTEPanel(); break;
                case 4: DrawTargetPanel(); break;
                case 5: DrawFSMPanel(); break;
                case 6: DrawAIDetailPanel(); break;
                case 7: DrawNullCheckPanel(); break;
            }
        }

        // =====================================================================
        // 사이드 HUD — AI 전용 탭 확장
        // =====================================================================
        protected override string[] GetSideTabLabels() =>
            new[] { "공통", "스탯", "전투", "타겟의타겟" };

#if UNITY_EDITOR
        // =====================================================================
        // Panel 5 — FSM
        // =====================================================================
        private void DrawFSMPanel()
        {
            if (showSectionFSM)
            {
                _foldFSM = DrawFoldout(_foldFSM, "🤖 FSM 현재 상태");
                if (_foldFSM) DrawFSMCurrent();
            }
            _foldFSMHist = DrawFoldout(_foldFSMHist, $"  📜 히스토리 ({_stateCount}개)");
            if (_foldFSMHist) DrawFSMHistory();
        }

        private void DrawFSMCurrent()
        {
            if (_ai.currentState == null) { WarningLabel("  currentState = null"); return; }
            string sn = _ai.currentState.GetType().Name;
            Color sc = GetStateColor(_ai.currentState);
            string hex = ColorUtility.ToHtmlStringRGB(sc);
            float dur = Time.time - _stateEnterTime;
            GUILayout.Label($"<b><color=#{hex}>▶ {sn}</color></b>  ({dur:F1}s)", HudStyle.Get(fontSize));

            if (_isInGroggy)
            {
                GroggyState grog = _ai.currentState as GroggyState;
                float maxD = grog != null ? grog.groggyDuration : 4f;
                float ratio = Mathf.Clamp01(_groggyTimer / maxD);
                InfoLabel($"  ⏱ 그로기: {_groggyTimer:F1}s / {maxD:F1}s", new Color(0.9f, 0.5f, 1f));
                Rect br = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                    GUILayout.Height(10), GUILayout.ExpandWidth(true));
                GUI.color = new Color(0.2f, 0.1f, 0.3f); GUI.DrawTexture(br, Texture2D.whiteTexture);
                Rect f = new Rect(br.x, br.y, br.width * ratio, br.height);
                GUI.color = new Color(0.8f, 0.3f, 1f); GUI.DrawTexture(f, Texture2D.whiteTexture);
                GUI.color = Color.white;
                bool ex = _execMgr != null && _ai.IsSpawned && _execMgr.isBeingExecuted.Value;
                if (ex) InfoLabel("  ⏸ 처형 중 — 타이머 정지", new Color(0.8f, 0.5f, 1f));
            }
        }

        private void DrawFSMHistory()
        {
            _fsmScroll = GUILayout.BeginScrollView(_fsmScroll, GUILayout.Height(160));
            for (int i = 0; i < _stateCount; i++)
            {
                int idx = ((_stateHead - 1 - i) % MAX_STATE + MAX_STATE) % MAX_STATE;
                var s = _stateHist[idx];
                float d = s.exitTime > 0f ? s.exitTime - s.enterTime : Time.time - s.enterTime;
                Color sc2 = GetStateColorByName(s.stateName);
                string h2 = ColorUtility.ToHtmlStringRGB(sc2);
                string act = s.exitTime <= 0f ? " <color=#FFD54F>●</color>" : "";
                GUILayout.Label(
                    $"<color={(i == 0 ? "#FFD54F" : "#90A4AE")}>[{s.enterTime:F1}s]</color>  " +
                    $"<color=#{h2}>{s.stateName}</color>  <color=#607D8B>({d:F1}s)</color>{act}",
                    HudStyle.Get(fontSize));
            }
            GUILayout.EndScrollView();
        }

        // =====================================================================
        // Panel 6 — AI 상세 (포이즈 + NavMesh)
        // =====================================================================
        private void DrawAIDetailPanel()
        {
            if (showSectionPoise)
            {
                _foldAIPoise = DrawFoldout(_foldAIPoise, "💢 포이즈 시각화");
                if (_foldAIPoise) DrawPoiseSection();
            }
            if (showSectionNavMesh)
            {
                _foldNav = DrawFoldout(_foldNav, "🗺 NavMesh & 이동");
                if (_foldNav) DrawNavSection();
            }
        }

        private void DrawPoiseSection()
        {
            if (!_ai.IsSpawned || _ai.characterNetworkManager == null) { SmallLabel("  (미스폰)"); return; }
            float p = _ai.characterNetworkManager.currentPoise.Value;
            float mp = _ai.characterNetworkManager.maxPoise.Value;
            Color pc = p <= 0f ? Color.red : p / Mathf.Max(mp, 1f) < 0.3f ? new Color(1f, 0.5f, 0f) : new Color(0.4f, 0.7f, 1f);
            StatBar("포이즈", p, mp, pc);
            if (p <= 0f) WarningLabel("  ⚡ POISE BROKEN");
            if (_ai.isPoiseActive) InfoLabel("  🛡 POISE ACTIVE", new Color(1f, 0.9f, 0f));
            SmallLabel($"  파괴: {_aiPoiseBreakCnt}회  마지막: {(_aiLastPoiseBreak > 0f ? _aiLastPoiseBreak.ToString("F1") + "s" : "-")}");
        }

        private void DrawNavSection()
        {
            if (_nav == null) { WarningLabel("  NavMeshAgent 없음"); return; }
            BoolRow("isOnNavMesh", _nav.isOnNavMesh, false, danger: !_nav.isOnNavMesh);
            BoolRow("isActiveAndEnabled", _nav.isActiveAndEnabled, false);
            BoolRow("canMove", _ai.canMove, false, invertWarning: true);
            SmallLabel($"  velocity     : {_nav.velocity.magnitude:F2} m/s");
            SmallLabel($"  speed (설정) : {_nav.speed:F1}");
            SmallLabel($"  stoppingDist : {_nav.stoppingDistance:F1}");
            string psc = _nav.pathStatus == NavMeshPathStatus.PathComplete ? "#66BB6A"
                       : _nav.pathStatus == NavMeshPathStatus.PathPartial ? "#FF9800" : "#EF5350";
            GUILayout.Label($"  pathStatus : <color={psc}><b>{_nav.pathStatus}</b></color>", HudStyle.Get(fontSize));
            if (_nav.updatePosition)
                WarningLabel("  ⚠ updatePosition=true (RootMotion 규약 위반!)");
            else
                InfoLabel("  ✓ updatePosition=false", new Color(0.4f, 1f, 0.4f));

            float mv = _ai.animator != null ? _ai.animator.GetFloat(TDA.Core.Events.AnimatorParameterHash.moveAmount) : 0f;
            SmallLabel($"  Animator moveAmount : {mv:F3}");
        }

        // =====================================================================
        // [REQ-1] 처형/QTE 패널 Override — isExecutable 추가
        // =====================================================================
        protected override void DrawExecutionSection()
        {
            if (!showSectionAIExec) { SmallLabel("  (표시 비활성화)"); return; }

            _foldAIExec = DrawFoldout(_foldAIExec, "⚔ AI 처형 상태");
            if (!_foldAIExec) return;

            if (_execMgr == null) { WarningLabel("  CharacterExecutionManager 없음"); return; }

            if (_ai.IsSpawned)
            {
                BoolRow("isBeingExecuted", _execMgr.isBeingExecuted.Value, false);
                var aiExecMgr = _execMgr as TDA.Character.AI.AICharacterExecutionManager;
                if (aiExecMgr != null)
                    BoolRow("isExecutable", aiExecMgr.isExecutable.Value, false);
            }
            else SmallLabel("  (미스폰)");

            SmallLabel($"  처형 진입 횟수: {_execCount}회 (세션)");
        }

        // =====================================================================
        // 유틸 — 타이틀 바 상태 표시 (AI 상태 색상 반영)
        // =====================================================================
        protected override string GetCurrentStateName()
        {
            if (_ai == null || _ai.currentState == null) return "---";
            string st = _ai.currentState.GetType().Name;
            Color sc = GetStateColor(_ai.currentState);
            string hex = ColorUtility.ToHtmlStringRGB(sc);
            return $"<color=#{hex}>{st}</color>";
        }

        private Color GetStateColor(AIState s)
        {
            if (s == null) return Color.gray;
            return GetStateColorByName(s.GetType().Name);
        }

        private Color GetStateColorByName(string n)
        {
            switch (n)
            {
                case "IdleState": return new Color(0.6f, 0.6f, 0.6f);
                case "PatrolState": return new Color(0.3f, 0.6f, 1f);
                case "PursueTargetState": return new Color(1f, 0.6f, 0.2f);
                case "CombatStanceState":
                case "SkeletonCombatStanceState": return new Color(1f, 0.9f, 0.2f);
                case "AttackState":
                case "SkeletonAttackState": return new Color(1f, 0.3f, 0.3f);
                case "GroggyState": return new Color(0.8f, 0.3f, 1f);
                case "FleeState": return new Color(0.3f, 1f, 0.4f);
                case "RetreatState": return new Color(0.3f, 1f, 0.8f);
                case "CallForHelpState": return new Color(1f, 0.8f, 0.4f);
                default: return new Color(0.7f, 0.7f, 0.7f);
            }
        }
#endif
    }
}