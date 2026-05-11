using System;
using UnityEngine;
using TDA.PB4.AI;
using TDA.PB4.Interfaces;

namespace TDA.PB4.Bridge.Mocks
{
    // ════════════════════════════════════════════════════════════════════
    // MockAIReceivers.cs — AI 도메인 Bridge 협업 계약 Mock 구현체
    // ════════════════════════════════════════════════════════════════════
    //
    // 【용도】
    //   ○ 실제 구현체 (WorldAISpawnManager / GroupAIManager) 미존재 / 미완성 시
    //   ○ Bridge 의 라우팅 동작 단독 테스트
    //   ○ Wk5+ 작업 전 인터페이스 시그니처 검증
    //
    // 【사용】
    //   1. 빈 GameObject 생성 — "MockAIReceivers"
    //   2. 본 Mock 컴포넌트 추가 (3 종 중 필요한 것)
    //   3. WorldAIBridgeManager 의 Force Auto-Find 실행 → Mock 자동 발견
    //   4. WorldAIBridgeManager 의 Mock Test 메뉴 실행 → Mock 응답 확인
    //
    // 【주의 — 실제 빌드에 포함 X】
    //   #if UNITY_EDITOR 조건부 컴파일 권장.
    //   본 파일은 디버깅 / 개발 시점만 활성화.
    // ════════════════════════════════════════════════════════════════════
    
#if UNITY_EDITOR
    
    /// <summary>
    /// ISpawnRequestReceiver Mock 구현체.
    /// 실제 WorldAISpawnManager 미존재 시 Bridge 테스트용.
    /// </summary>
    public class MockSpawnReceiver : MonoBehaviour, ISpawnRequestReceiver
    {
        [SerializeField, Tooltip("호출 시 로그 출력")]
        private bool _logCalls = true;
        
        [SerializeField, Tooltip("CanAcceptRequest 의 반환값")]
        private bool _alwaysAccept = true;
        
        [SerializeField, Tooltip("GetQueuedCount 의 반환값")]
        private int _mockQueuedCount = 0;
        
        public event Action<SpawnRequestResponse> OnSpawnRequestCompleted;
        
        public bool CanAcceptRequest(SpawnRequest req, out string failReason)
        {
            if (_logCalls) Debug.Log($"[MockSpawnReceiver] CanAcceptRequest 호출", this);
            failReason = _alwaysAccept ? "" : "Mock: 거부";
            return _alwaysAccept;
        }
        
        public SpawnRequestResponse ExecuteSpawnRequest(SpawnRequest req)
        {
            if (_logCalls) Debug.Log($"[MockSpawnReceiver] ExecuteSpawnRequest 호출", this);
            return default(SpawnRequestResponse);
        }
        
        public void QueueRequest(SpawnRequest req)
        {
            if (_logCalls) Debug.Log($"[MockSpawnReceiver] QueueRequest 호출", this);
            _mockQueuedCount++;
        }
        
        public bool CancelRequest(Guid requestId)
        {
            if (_logCalls) Debug.Log($"[MockSpawnReceiver] CancelRequest 호출: {requestId}", this);
            return false;
        }
        
        public int GetQueuedCount()
        {
            return _mockQueuedCount;
        }
        
        /// <summary>비동기 완료 이벤트 강제 발행 (테스트용).</summary>
        [ContextMenu("Fire OnSpawnRequestCompleted")]
        private void FireCompletedEvent()
        {
            OnSpawnRequestCompleted?.Invoke(default(SpawnRequestResponse));
            Debug.Log($"[MockSpawnReceiver] OnSpawnRequestCompleted 발행", this);
        }
    }
    
    /// <summary>
    /// IGroupDashboard Mock 구현체 (Wk5+ GroupAIManager 신설 전 테스트용).
    /// </summary>
    public class MockGroupDashboard : MonoBehaviour, IGroupDashboard
    {
        [Header("Mock Values")]
        [SerializeField] private string _groupId = "MockGroup_01";
        [SerializeField] private string _factionId = "Goblin";
        [SerializeField] private int _sequenceNum = 1;
        [SerializeField] private int _population = 5;
        [SerializeField] private int _maxPopulation = 10;
        [SerializeField, Range(0, 1)] private float _morale = 0.7f;
        [SerializeField] private int _escalationLevel = 0;
        [SerializeField] private Vector3 _centerPos;
        [SerializeField] private float _territoryRadius = 15f;
        [SerializeField] private bool _logCalls = false;
        
        public event Action<GroupThresholdEvent> OnThresholdCrossed;
        
        // ── IGroupAIInfo (부모) ──
        public float GetMorale() { LogIfEnabled(nameof(GetMorale)); return _morale; }
        public bool HasAvailableToken() { LogIfEnabled(nameof(HasAvailableToken)); return true; }
        
        // ── IGroupDashboard ──
        public string GetGroupId() { LogIfEnabled(nameof(GetGroupId)); return _groupId; }
        public string GetFactionId() { LogIfEnabled(nameof(GetFactionId)); return _factionId; }
        public int GetSequenceNum() => _sequenceNum;
        public int GetPopulation() { LogIfEnabled(nameof(GetPopulation)); return _population; }
        public int GetMaxPopulation() => _maxPopulation;
        public int GetReinforcementCount() => 0;
        public int GetCasualtyCount() => 0;
        public int GetEscalationLevel() => _escalationLevel;
        public float GetCombatElapsedTime() => 0f;
        public Vector3 GetCenterPosition() { LogIfEnabled(nameof(GetCenterPosition)); return _centerPos; }
        public int GetCurrentCaveNodeIdx() => 0;
        public float GetTerritoryRadius() => _territoryRadius;
        public string GetCurrentSceneId() => "MockScene";
        public float GetHungerLevel() => 0.3f;
        public float GetReproductionUrge() => 0.2f;
        public float GetTerritoryPressure() => 0.4f;
        public int GetDefeatsAtCurrentLocation() => 0;
        public Vector3? GetLastSafePosition() => _centerPos;
        public GroupThresholdState[] GetActiveThresholds() => Array.Empty<GroupThresholdState>();
        
        private void LogIfEnabled(string method)
        {
            if (_logCalls) Debug.Log($"[MockGroupDashboard] {method}", this);
        }
        
        /// <summary>Mock Threshold 이벤트 강제 발행 (테스트용).</summary>
        [ContextMenu("Fire OnThresholdCrossed (MoraleCollapse)")]
        private void FireMoraleCollapse()
        {
            OnThresholdCrossed?.Invoke(new GroupThresholdEvent
            {
                groupId = _groupId,
                type = TDA.PB4.Interfaces.GroupThresholdType.MoraleCollapse,
                gameTime = Time.time,
                value = _morale,
                threshold = 0.3f
            });
            Debug.Log($"[MockGroupDashboard] OnThresholdCrossed (MoraleCollapse) 발행", this);
        }
    }
    
    /// <summary>
    /// IGroupCommandReceiver Mock 구현체.
    /// </summary>
    public class MockGroupCommandReceiver : MonoBehaviour, IGroupCommandReceiver
    {
        [SerializeField] private bool _logCalls = true;
        
        private readonly System.Collections.Generic.List<SquadCommand> _activeCommands = new();
        
        public void IssueCommand(SquadCommand cmd)
        {
            if (_logCalls) Debug.Log($"[MockGroupCommandReceiver] IssueCommand: {cmd?.type}", this);
            if (cmd != null) _activeCommands.Add(cmd);
        }
        
        public bool CancelCommand(Guid commandId)
        {
            if (_logCalls) Debug.Log($"[MockGroupCommandReceiver] CancelCommand: {commandId}", this);
            return _activeCommands.RemoveAll(c => c.commandId == commandId) > 0;
        }
        
        public SquadCommand[] GetActiveCommands() => _activeCommands.ToArray();
    }
    
#endif
}
