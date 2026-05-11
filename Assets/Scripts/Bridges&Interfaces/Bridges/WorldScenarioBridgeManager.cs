using UnityEngine;

// ★ ScenarioManager 신설 후 활성화
// using TDA.PB4.Scenario;

namespace TDA.PB4.Bridge
{
    // ════════════════════════════════════════════════════════════════════
    // WorldScenarioBridgeManager — Scenario 도메인 진입점 (★ 팀원 가이드)
    // ════════════════════════════════════════════════════════════════════
    //
    // 【이 클래스가 하는 일】
    //   외부 도메인 (Lobby / AI / Player / Terrain) 이 Scenario 호출 시 단일 진입점.
    //   게임 시퀀스 코디네이터의 진입점 — Scenario 가 다른 도메인 Bridge 들을 호출.
    //
    // 【★ 현재 상태 — ScenarioManager 미존재】
    //   ScenarioManager 가 프로젝트에 미존재.
    //   본 Bridge 는 placeholder — 호출 시 LogWarning.
    //   ScenarioManager 신설 후 SerializeField 타입 교체 + 라우팅 활성화.
    //
    // 【디버깅】
    //   Inspector > _debugLog 체크 시 placeholder 호출 로그 출력.
    // ════════════════════════════════════════════════════════════════════
    
    public class WorldScenarioBridgeManager : BridgeManager
    {
        public static WorldScenarioBridgeManager Instance =>
            WorldBridgeManager.HasInstance
                ? WorldBridgeManager.Instance.Get<WorldScenarioBridgeManager>()
                : null;
        
        public override string DomainName => "Scenario";
        
        // ★ ScenarioManager 신설 후 타입 교체:
        //   [SerializeField] private ScenarioManager _scenarioManager;
        [Header("Delegation Targets (Auto-found, override 가능)")]
        [SerializeField, Tooltip("ScenarioManager — 신설 후 실제 타입으로 교체")]
        private MonoBehaviour _scenarioManager;
        
        protected override void OnBridgeRegistered()
        {
            // ★ ScenarioManager 신설 후 자동 검색 활성화:
            // if (_scenarioManager == null)
            //     _scenarioManager = AutoFindComponent<ScenarioManager>();
            
            if (_scenarioManager == null)
            {
                Debug.LogWarning(
                    $"[{nameof(WorldScenarioBridgeManager)}] ScenarioManager 미할당 (placeholder). " +
                    $"ScenarioManager 신설 후 Inspector 할당 + 본 Bridge 코드 활성화 필요.", this);
            }
        }
        
        /// <summary>게임 시작 시퀀스 진입점. 호출자: Lobby (GameBootstrap)</summary>
        public void StartGame()
        {
            DebugLog("→ ScenarioManager", "StartGame");
            
            if (_scenarioManager == null)
            {
                DebugLogWarning("→ ScenarioManager", "StartGame", "ScenarioManager 미존재 (placeholder)");
                return;
            }
            // ★ ScenarioManager 신설 후: _scenarioManager.StartGame();
        }
        
        /// <summary>시나리오 이벤트 트리거. 호출자: AI / Player / Terrain</summary>
        public void TriggerEvent(string eventId)
        {
            DebugLog("→ ScenarioManager", "TriggerEvent", eventId);
            
            if (_scenarioManager == null)
            {
                DebugLogWarning("→ ScenarioManager", "TriggerEvent", "ScenarioManager 미존재 (placeholder)");
                return;
            }
            // ★ ScenarioManager 신설 후: _scenarioManager.TriggerEvent(eventId);
        }
        
        public string GetCurrentScenarioId()
        {
            // ★ ScenarioManager 신설 후: return _scenarioManager?.CurrentScenarioId;
            return null;
        }
        
        public bool IsScenarioActive()
        {
            // ★ ScenarioManager 신설 후: return _scenarioManager?.IsActive ?? false;
            return false;
        }
        
        [ContextMenu("[5] Print Scenario Domain Status")]
        private void DebugPrintScenarioDomainStatus()
        {
            Debug.Log(
                $"=== [{nameof(WorldScenarioBridgeManager)}] Scenario 도메인 상태 ===\n" +
                $"  ScenarioManager: " +
                  $"{(_scenarioManager != null ? $"OK ({_scenarioManager.gameObject.name})" : "★ null — 신설 필요")}");
        }
        
        // ════════════════════════════════════════════════════════════════
        // ─── ★ Mock Test ContextMenu ───────────────────────────────
        // ════════════════════════════════════════════════════════════════
        
        [ContextMenu("[Mock S1] Test StartGame")]
        private void MockS1_StartGame()
        {
            RunMock("StartGame", () => StartGame());
        }
        
        [ContextMenu("[Mock S2] Test TriggerEvent")]
        private void MockS2_TriggerEvent()
        {
            RunMock("TriggerEvent(\"boss_arrival\")", () => TriggerEvent("boss_arrival"));
        }
        
        [ContextMenu("[Mock S3] Test GetCurrentScenarioId + IsScenarioActive")]
        private void MockS3_GetState()
        {
            RunMock("GetCurrentScenarioId + IsScenarioActive", () =>
            {
                var id = GetCurrentScenarioId();
                var active = IsScenarioActive();
                Debug.Log($"[MOCK] scenarioId: {id ?? "null"}, isActive: {active}");
            });
        }
        
        [ContextMenu("[Mock S0] Run All Scenario Mock Tests")]
        private void MockS0_RunAll()
        {
            Debug.Log("════════════ [MOCK ALL] Scenario Bridge 전체 테스트 시작 ════════════");
            MockS1_StartGame();
            MockS2_TriggerEvent();
            MockS3_GetState();
            Debug.Log("════════════ [MOCK ALL] Scenario Bridge 전체 테스트 종료 ════════════");
        }
        
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
