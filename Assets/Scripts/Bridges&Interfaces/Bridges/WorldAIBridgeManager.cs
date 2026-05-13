using System;
using UnityEngine;
using TDA.PB4.AI;           // SpawnRequest, SpawnRequestResponse
using TDA.PB4.Interfaces;   // ISpawnRequestReceiver, IGroupDashboard, IGroupCommandReceiver, SquadCommand

namespace TDA.PB4.Bridge
{
    // ════════════════════════════════════════════════════════════════════
    // WorldAIBridgeManager — AI 도메인 진입점 (★ 팀원 가이드)
    // ════════════════════════════════════════════════════════════════════
    //
    // 【이 클래스가 하는 일】
    //   외부 도메인 (Scenario / Player / pC 컨텐츠) 이 AI 도메인 호출 시
    //   단일 진입점. 협업 계약 (ISpawnRequestReceiver / IGroupDashboard /
    //   IGroupCommandReceiver) 인터페이스에 위임.
    //
    // 【설계 결정 — 인터페이스 의존 + 자동 검색】
    //   ○ Bridge 가 구체 구현 (WorldAISpawnManager / GroupAIManager) 을 모름
    //   ○ Awake 시 씬에서 인터페이스 구현체 자동 검색
    //   ○ 수동 SerializeField 할당도 가능 (자동 검색 실패 시 / 명시적 선택 시)
    //
    // 【씬에 배치】
    //   1. 빈 GameObject — "WorldAIBridgeManager" (root)
    //   2. 본 컴포넌트 추가
    //   3. (자동 검색 사용 시) Inspector 할당 불필요
    //      또는 수동 할당: Inspector 에서 ISpawnRequestReceiver 구현 컴포넌트 드래그
    //
    // 【외부 도메인 호출 (★ 권장 패턴)】
    //   // 동기 Spawn
    //   var resp = WorldAIBridgeManager.Instance.ExecuteSpawnRequest(req);
    //
    //   // 비동기 큐 적재
    //   WorldAIBridgeManager.Instance.QueueRequest(req);
    //   WorldAIBridgeManager.Instance.OnSpawnRequestCompleted += handler;
    //
    //   // 그룹 정보 (Wk5+)
    //   var morale = WorldAIBridgeManager.Instance.GetDashboard()?.GetMorale();
    //
    // 【디버깅】
    //   ○ Inspector > Debug > _debugLog 체크 시 모든 통신 로그 출력
    //   ○ Inspector > Statistics — 총 호출 / 최근 10 호출 history
    //   ○ ContextMenu — Print Statistics / Print Recent Calls / Toggle Debug
    //                   Force Auto-Find / Validate Delegation Targets
    // ════════════════════════════════════════════════════════════════════
    
    public class WorldAIBridgeManager : BridgeManager
    {
        public static WorldAIBridgeManager Instance =>
            WorldBridgeManager.HasInstance
                ? WorldBridgeManager.Instance.Get<WorldAIBridgeManager>()
                : null;
        
        public override string DomainName => "AI";
        
        // ════════════════════════════════════════════
        // 위임 대상 — 자동 검색 + 수동 할당 가능
        // ════════════════════════════════════════════
        
        [Header("Delegation Targets (Auto-found, override 가능)")]
        [SerializeField, Tooltip("ISpawnRequestReceiver 구현 컴포넌트 (자동 검색 / 수동 할당)")]
        private MonoBehaviour _spawnReceiverImpl;
        private ISpawnRequestReceiver SpawnReceiver => _spawnReceiverImpl as ISpawnRequestReceiver;
        
        [SerializeField, Tooltip("IGroupDashboard 구현 컴포넌트 (Wk5+)")]
        private MonoBehaviour _groupDashboardImpl;
        private IGroupDashboard GroupDashboard => _groupDashboardImpl as IGroupDashboard;
        
        [SerializeField, Tooltip("IGroupCommandReceiver 구현 컴포넌트 (Wk5+)")]
        private MonoBehaviour _groupCommandReceiverImpl;
        private IGroupCommandReceiver GroupCommandReceiver => _groupCommandReceiverImpl as IGroupCommandReceiver;
        
        // ════════════════════════════════════════════
        // 등록 후 후크 — 자동 검색 + 검증
        // ════════════════════════════════════════════
        
        protected override void OnBridgeRegistered()
        {
            // 자동 검색 (SerializeField 미할당 시)
            if (_spawnReceiverImpl == null)
                _spawnReceiverImpl = AutoFindImplementation<ISpawnRequestReceiver>();
            
            if (_groupDashboardImpl == null)
                _groupDashboardImpl = AutoFindImplementation<IGroupDashboard>();
            
            if (_groupCommandReceiverImpl == null)
                _groupCommandReceiverImpl = AutoFindImplementation<IGroupCommandReceiver>();
            
            ValidateDelegationTargets();
        }
        
        private void ValidateDelegationTargets()
        {
            // Spawn Receiver — 가장 중요 (P0)
            if (_spawnReceiverImpl == null)
                Debug.LogWarning(
                    $"[{nameof(WorldAIBridgeManager)}] ISpawnRequestReceiver 미할당. " +
                    $"AutoFind 도 실패 — 씬에 구현체 없음.", this);
            else if (SpawnReceiver == null)
                Debug.LogError(
                    $"[{nameof(WorldAIBridgeManager)}] _spawnReceiverImpl 가 " +
                    $"ISpawnRequestReceiver 미구현 ({_spawnReceiverImpl.GetType().Name}).", this);
            
            // Group Dashboard — Wk5+ 작업 (경고만)
            if (_groupDashboardImpl != null && GroupDashboard == null)
                Debug.LogError(
                    $"[{nameof(WorldAIBridgeManager)}] _groupDashboardImpl 가 " +
                    $"IGroupDashboard 미구현 ({_groupDashboardImpl.GetType().Name}).", this);
            
            // Group Command Receiver — Wk5+ 작업 (경고만)
            if (_groupCommandReceiverImpl != null && GroupCommandReceiver == null)
                Debug.LogError(
                    $"[{nameof(WorldAIBridgeManager)}] _groupCommandReceiverImpl 가 " +
                    $"IGroupCommandReceiver 미구현 ({_groupCommandReceiverImpl.GetType().Name}).", this);
        }
        
        // ════════════════════════════════════════════════════════════════
        // ─── ISpawnRequestReceiver 라우팅 ────────────────────────────
        // ════════════════════════════════════════════════════════════════
        
        /// <summary>사전 검증. 큐 적재 없이 호출 가능 여부 확인.</summary>
        public bool CanAcceptRequest(SpawnRequest req, out string failReason)
        {
            if (SpawnReceiver == null)
            {
                DebugLogError("→ ISpawnRequestReceiver", "CanAcceptRequest", "위임 대상 null");
                failReason = "ISpawnRequestReceiver 미등록";
                return false;
            }
            
            DebugLog("→ ISpawnRequestReceiver", "CanAcceptRequest", req);
            var result = SpawnReceiver.CanAcceptRequest(req, out failReason);
            DebugLog("← ISpawnRequestReceiver", "CanAcceptRequest", null,
                $"result={result}, failReason={failReason}");
            
            return result;
        }
        
        /// <summary>동기 실행. Coroutine 시작 후 즉시 응답.</summary>
        public SpawnRequestResponse ExecuteSpawnRequest(SpawnRequest req)
        {
            if (SpawnReceiver == null)
            {
                DebugLogError("→ ISpawnRequestReceiver", "ExecuteSpawnRequest", "위임 대상 null");
                return default;
            }
            
            DebugLog("→ ISpawnRequestReceiver", "ExecuteSpawnRequest", req);
            var result = SpawnReceiver.ExecuteSpawnRequest(req);
            DebugLog("← ISpawnRequestReceiver", "ExecuteSpawnRequest", null, result);
            
            return result;
        }
        
        /// <summary>비동기 큐 적재. 결과는 OnSpawnRequestCompleted 이벤트.</summary>
        public void QueueRequest(SpawnRequest req)
        {
            if (SpawnReceiver == null)
            {
                DebugLogError("→ ISpawnRequestReceiver", "QueueRequest", "위임 대상 null");
                return;
            }
            
            DebugLog("→ ISpawnRequestReceiver", "QueueRequest", req);
            SpawnReceiver.QueueRequest(req);
        }
        
        /// <summary>큐에 적재된 요청 취소.</summary>
        public bool CancelRequest(Guid requestId)
        {
            if (SpawnReceiver == null) return false;
            
            DebugLog("→ ISpawnRequestReceiver", "CancelRequest", requestId);
            var result = SpawnReceiver.CancelRequest(requestId);
            DebugLog("← ISpawnRequestReceiver", "CancelRequest", null, result);
            
            return result;
        }
        
        /// <summary>현재 큐 대기 요청 수.</summary>
        public int GetQueuedCount()
        {
            if (SpawnReceiver == null) return 0;
            
            var result = SpawnReceiver.GetQueuedCount();
            if (_debugLog) DebugLog("→ ISpawnRequestReceiver", "GetQueuedCount", null, result);
            
            return result;
        }
        
        /// <summary>비동기 처리 완료 이벤트.</summary>
        public event Action<SpawnRequestResponse> OnSpawnRequestCompleted
        {
            add { if (SpawnReceiver != null) SpawnReceiver.OnSpawnRequestCompleted += value; }
            remove { if (SpawnReceiver != null) SpawnReceiver.OnSpawnRequestCompleted -= value; }
        }
        
        // ════════════════════════════════════════════════════════════════
        // ─── IGroupDashboard 라우팅 (Wk5+) ───────────────────────────
        // ════════════════════════════════════════════════════════════════
        
        /// <summary>Dashboard 직접 노출 (외부가 인터페이스 그대로 사용 — 특수 용도. 일반 호출자는 아래 라우팅 메서드 사용).</summary>
        public IGroupDashboard GetDashboard()
        {
            if (GroupDashboard == null)
            {
                DebugLogWarning("→ IGroupDashboard", "GetDashboard", "위임 대상 null (Wk5+ 신설 대기)");
                return null;
            }
            
            DebugLog("→ IGroupDashboard", "GetDashboard");
            return GroupDashboard;
        }
        
        // ════════════════════════════════════════════════════════════════
        // ★ A7 — IGroupDashboard 라우팅 (메서드 라우팅 통일 v2)
        // 위임 대상: GroupDashboard (구현체 — GroupAIManager)
        // 호출자: 시나리오 / UI / 펙션 / 기타 외부
        // ════════════════════════════════════════════════════════════════
        
        // ── 식별 (3) ─────────────────────────────────────────────────
        
        public string GetGroupId()
        {
            DebugLog("→ Dashboard", "GetGroupId");
            return GroupDashboard?.GetGroupId() ?? "";
        }
        
        public string GetFactionId()
        {
            DebugLog("→ Dashboard", "GetFactionId");
            return GroupDashboard?.GetFactionId() ?? "";
        }
        
        public int GetSequenceNum()
        {
            DebugLog("→ Dashboard", "GetSequenceNum");
            return GroupDashboard?.GetSequenceNum() ?? 0;
        }
        
        // ── 인구 (4) ─────────────────────────────────────────────────
        
        public int GetPopulation()
        {
            DebugLog("→ Dashboard", "GetPopulation");
            return GroupDashboard?.GetPopulation() ?? 0;
        }
        
        public int GetMaxPopulation()
        {
            DebugLog("→ Dashboard", "GetMaxPopulation");
            return GroupDashboard?.GetMaxPopulation() ?? 0;
        }
        
        public int GetReinforcementCount()
        {
            DebugLog("→ Dashboard", "GetReinforcementCount");
            return GroupDashboard?.GetReinforcementCount() ?? 0;
        }
        
        public int GetCasualtyCount()
        {
            DebugLog("→ Dashboard", "GetCasualtyCount");
            return GroupDashboard?.GetCasualtyCount() ?? 0;
        }
        
        // ── 상태 (2 — GetMorale 은 IGroupAIInfo 에서 이미 라우팅) ──
        
        public int GetEscalationLevel()
        {
            DebugLog("→ Dashboard", "GetEscalationLevel");
            return GroupDashboard?.GetEscalationLevel() ?? 0;
        }
        
        public float GetCombatElapsedTime()
        {
            DebugLog("→ Dashboard", "GetCombatElapsedTime");
            return GroupDashboard?.GetCombatElapsedTime() ?? 0f;
        }
        
        // ── 위치 / 영토 (4) ──────────────────────────────────────────
        
        public Vector3 GetCenterPosition()
        {
            DebugLog("→ Dashboard", "GetCenterPosition");
            return GroupDashboard?.GetCenterPosition() ?? Vector3.zero;
        }
        
        public int GetCurrentCaveNodeIdx()
        {
            DebugLog("→ Dashboard", "GetCurrentCaveNodeIdx");
            return GroupDashboard?.GetCurrentCaveNodeIdx() ?? -1;
        }
        
        public float GetTerritoryRadius()
        {
            DebugLog("→ Dashboard", "GetTerritoryRadius");
            return GroupDashboard?.GetTerritoryRadius() ?? 0f;
        }
        
        public string GetCurrentSceneId()
        {
            DebugLog("→ Dashboard", "GetCurrentSceneId");
            return GroupDashboard?.GetCurrentSceneId() ?? "";
        }
        
        // ── 욕구 (Wk5+ stub, 3) ──────────────────────────────────────
        
        public float GetHungerLevel()
        {
            DebugLog("→ Dashboard", "GetHungerLevel");
            return GroupDashboard?.GetHungerLevel() ?? 0f;
        }
        
        public float GetReproductionUrge()
        {
            DebugLog("→ Dashboard", "GetReproductionUrge");
            return GroupDashboard?.GetReproductionUrge() ?? 0f;
        }
        
        public float GetTerritoryPressure()
        {
            DebugLog("→ Dashboard", "GetTerritoryPressure");
            return GroupDashboard?.GetTerritoryPressure() ?? 0f;
        }
        
        // ── 학습 / 이력 (2) ──────────────────────────────────────────
        
        public int GetDefeatsAtCurrentLocation()
        {
            DebugLog("→ Dashboard", "GetDefeatsAtCurrentLocation");
            return GroupDashboard?.GetDefeatsAtCurrentLocation() ?? 0;
        }
        
        public Vector3? GetLastSafePosition()
        {
            DebugLog("→ Dashboard", "GetLastSafePosition");
            return GroupDashboard?.GetLastSafePosition();
        }
        
        // ── 임계 이벤트 (1 + event) ──────────────────────────────────
        
        public GroupThresholdState[] GetActiveThresholds()
        {
            DebugLog("→ Dashboard", "GetActiveThresholds");
            return GroupDashboard?.GetActiveThresholds() ?? new GroupThresholdState[0];
        }
        
        /// <summary>임계 이벤트 구독. Bridge 가 add/remove 를 GroupDashboard 에 위임.</summary>
        public event Action<GroupThresholdEvent> OnThresholdCrossed
        {
            add
            {
                DebugLog("→ Dashboard", "OnThresholdCrossed += subscriber");
                if (GroupDashboard != null) GroupDashboard.OnThresholdCrossed += value;
            }
            remove
            {
                DebugLog("→ Dashboard", "OnThresholdCrossed -= subscriber");
                if (GroupDashboard != null) GroupDashboard.OnThresholdCrossed -= value;
            }
        }
        
        // ════════════════════════════════════════════════════════════════
        // ─── IGroupCommandReceiver 라우팅 (Wk5+) ─────────────────────
        // ════════════════════════════════════════════════════════════════
        
        public void IssueCommand(SquadCommand cmd)
        {
            if (GroupCommandReceiver == null)
            {
                DebugLogError("→ IGroupCommandReceiver", "IssueCommand", "위임 대상 null (Wk5+ 신설 대기)");
                return;
            }
            
            DebugLog("→ IGroupCommandReceiver", "IssueCommand", cmd?.type);
            GroupCommandReceiver.IssueCommand(cmd);
        }
        
        public bool CancelCommand(Guid commandId)
        {
            if (GroupCommandReceiver == null) return false;
            
            DebugLog("→ IGroupCommandReceiver", "CancelCommand", commandId);
            var result = GroupCommandReceiver.CancelCommand(commandId);
            DebugLog("← IGroupCommandReceiver", "CancelCommand", null, result);
            return result;
        }
        
        public SquadCommand[] GetActiveCommands()
        {
            if (GroupCommandReceiver == null) return Array.Empty<SquadCommand>();
            
            var result = GroupCommandReceiver.GetActiveCommands();
            if (_debugLog) DebugLog("→ IGroupCommandReceiver", "GetActiveCommands", null,
                $"count={result?.Length ?? 0}");
            return result;
        }
        
        // ════════════════════════════════════════════
        // ContextMenu (도메인 특화)
        // ════════════════════════════════════════════
        
        [ContextMenu("[5] Validate Delegation Targets")]
        private void DebugValidateDelegationTargets()
        {
            ValidateDelegationTargets();
            Debug.Log($"[{nameof(WorldAIBridgeManager)}] 위임 대상 검증 완료.");
        }
        
        [ContextMenu("[6] Force Auto-Find")]
        private void DebugForceAutoFind()
        {
            _spawnReceiverImpl = AutoFindImplementation<ISpawnRequestReceiver>();
            _groupDashboardImpl = AutoFindImplementation<IGroupDashboard>();
            _groupCommandReceiverImpl = AutoFindImplementation<IGroupCommandReceiver>();
            ValidateDelegationTargets();
            Debug.Log($"[{nameof(WorldAIBridgeManager)}] 강제 자동 검색 완료.");
        }
        
        [ContextMenu("[7] Print AI Domain Status")]
        private void DebugPrintAIDomainStatus()
        {
            Debug.Log(
                $"=== [{nameof(WorldAIBridgeManager)}] AI 도메인 상태 ===\n" +
                $"  ISpawnRequestReceiver: " +
                  $"{(SpawnReceiver != null ? $"OK ({_spawnReceiverImpl.GetType().Name})" : "★ null")}\n" +
                $"  IGroupDashboard: " +
                  $"{(GroupDashboard != null ? $"OK ({_groupDashboardImpl.GetType().Name})" : "null (Wk5+)")}\n" +
                $"  IGroupCommandReceiver: " +
                  $"{(GroupCommandReceiver != null ? $"OK ({_groupCommandReceiverImpl.GetType().Name})" : "null (Wk5+)")}");
        }
        
        // ════════════════════════════════════════════════════════════════
        // ─── ★ Mock Test ContextMenu ───────────────────────────────
        //
        // 사용:
        //   1. GameObject 우클릭 > Mock 메뉴 선택
        //   2. 자동으로 Debug 로그 활성화 + 시나리오 실행
        //   3. Console 에서 [MOCK START] ~ [MOCK END] 사이 로그 확인
        //   4. 종료 후 Debug 로그 원상 복구
        //
        // 위임 대상이 null 일 때도 동작 — Bridge 의 fallback 동작 검증.
        // ════════════════════════════════════════════════════════════════
        
        [ContextMenu("[Mock A1] Test ExecuteSpawnRequest")]
        private void MockA1_ExecuteSpawnRequest()
        {
            RunMock("ExecuteSpawnRequest", () =>
            {
                var req = default(SpawnRequest);
                // ★ 사용자 측 SpawnRequest 필드를 알면 채워넣기:
                // req.factionId = "Goblin"; req.memberCount = 5; 등
                var resp = ExecuteSpawnRequest(req);
                Debug.Log($"[MOCK] 응답: {resp}");
            });
        }
        
        [ContextMenu("[Mock A2] Test CanAcceptRequest")]
        private void MockA2_CanAcceptRequest()
        {
            RunMock("CanAcceptRequest", () =>
            {
                var req = default(SpawnRequest);
                var ok = CanAcceptRequest(req, out string reason);
                Debug.Log($"[MOCK] 결과: accept={ok}, reason={reason}");
            });
        }
        
        [ContextMenu("[Mock A3] Test QueueRequest")]
        private void MockA3_QueueRequest()
        {
            RunMock("QueueRequest + GetQueuedCount", () =>
            {
                var before = GetQueuedCount();
                QueueRequest(default(SpawnRequest));
                var after = GetQueuedCount();
                Debug.Log($"[MOCK] queue: before={before}, after={after}");
            });
        }
        
        [ContextMenu("[Mock A4] Test GetDashboard (Wk5+)")]
        private void MockA4_GetDashboard()
        {
            RunMock("GetDashboard", () =>
            {
                var dash = GetDashboard();
                Debug.Log($"[MOCK] Dashboard: {(dash != null ? "OK" : "null — Wk5+ 신설 대기")}");
                if (dash != null)
                {
                    Debug.Log($"  - GroupId: {dash.GetGroupId()}");
                    Debug.Log($"  - Morale: {dash.GetMorale()}");
                    Debug.Log($"  - Population: {dash.GetPopulation()}");
                }
            });
        }
        
        [ContextMenu("[Mock A5] Test IssueCommand (Wk5+)")]
        private void MockA5_IssueCommand()
        {
            RunMock("IssueCommand", () =>
            {
                // ★ CommandType 이 TDA.PB4.AI / TDA.PB4.Interfaces 두 namespace 에 존재 — 명시적 지정
                var cmd = new SquadCommand { type = TDA.PB4.Interfaces.CommandType.FocusFire };
                IssueCommand(cmd);
                var active = GetActiveCommands();
                Debug.Log($"[MOCK] active commands: {active.Length}");
            });
        }
        
        [ContextMenu("[Mock A0] Run All AI Mock Tests")]
        private void MockA0_RunAll()
        {
            Debug.Log("════════════ [MOCK ALL] AI Bridge 전체 테스트 시작 ════════════");
            MockA1_ExecuteSpawnRequest();
            MockA2_CanAcceptRequest();
            MockA3_QueueRequest();
            MockA4_GetDashboard();
            MockA5_IssueCommand();
            Debug.Log("════════════ [MOCK ALL] AI Bridge 전체 테스트 종료 ════════════");
        }
        
        /// <summary>Mock 시나리오 실행 helper. Debug 로그 임시 활성화 + 경계선 출력.</summary>
        private void RunMock(string scenarioName, System.Action action)
        {
            var prevDebug = _debugLog;
            _debugLog = true;
            
            Debug.Log($"════════ [MOCK START] {scenarioName} ════════", this);
            try
            {
                action?.Invoke();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MOCK FAIL] {scenarioName}: {ex.Message}\n{ex.StackTrace}", this);
            }
            finally
            {
                _debugLog = prevDebug;
                Debug.Log($"════════ [MOCK END] {scenarioName} ════════", this);
            }
        }
    }
}
