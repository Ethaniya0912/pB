using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TDA.PB4.Bridge
{
    // ════════════════════════════════════════════════════════════════════
    // WorldBridgeManager — Bridge 최상위 매개자 (★ 팀원 가이드)
    // ════════════════════════════════════════════════════════════════════
    //
    // 【이 클래스가 하는 일】
    //   모든 도메인 Bridge 의 등록 / 조회를 관리하는 싱글톤.
    //   외부 도메인은 본 매니저를 통해 다른 도메인 Bridge 조회.
    //
    // 【씬에 배치】
    //   1. 빈 GameObject 생성 — "WorldBridgeManager"
    //   2. 본 컴포넌트 추가 (root)
    //   3. 첫 씬에 한 번만 — DontDestroyOnLoad 자동
    //
    // 【사용 패턴】
    //   // 권장 — Bridge 자체 정적 프로퍼티
    //   WorldAIBridgeManager.Instance.ExecuteSpawnRequest(req);
    //
    //   // 직접 조회 (가능하나 비권장)
    //   var aiBridge = WorldBridgeManager.Instance.Get<WorldAIBridgeManager>();
    //
    // 【싱글톤 / DontDestroyOnLoad】
    //   ○ Instance static property
    //   ○ Awake() 에서 중복 검사 + Destroy(gameObject)
    //   ○ DontDestroyOnLoad
    //   ○ OnDestroy() 에서 Instance = null + _bridges.Clear()
    // ════════════════════════════════════════════════════════════════════
    
    [DefaultExecutionOrder(BridgeExecutionOrder.World)]
    public class WorldBridgeManager : MonoBehaviour
    {
        public static WorldBridgeManager Instance { get; private set; }
        public static bool HasInstance => Instance != null;
        
        private readonly Dictionary<Type, BridgeManager> _bridges = new();
        
        [Header("Settings")]
        [SerializeField, Tooltip("Bridge 등록 / 해제 시 로그 출력")]
        private bool _verboseLogging = true;
        
        [Header("Registered Bridges (Runtime, Read-only)")]
        [SerializeField, Tooltip("실행 중 등록된 Bridge 목록 (모니터링용)")]
        private List<BridgeManager> _registeredBridgesView = new();
        
        public IReadOnlyDictionary<Type, BridgeManager> RegisteredBridges => _bridges;
        public int Count => _bridges.Count;
        
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    $"[{nameof(WorldBridgeManager)}] 중복 인스턴스 감지 ({gameObject.name}). " +
                    $"기존 ({Instance.gameObject.name}) 유지 / 본 인스턴스 파괴.", this);
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            if (_verboseLogging)
                Debug.Log(
                    $"[{nameof(WorldBridgeManager)}] 초기화 완료 " +
                    $"(Singleton, ExecutionOrder = {BridgeExecutionOrder.World}, DontDestroyOnLoad).");
        }
        
        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                _bridges.Clear();
                _registeredBridgesView.Clear();
                
                if (_verboseLogging)
                    Debug.Log($"[{nameof(WorldBridgeManager)}] 인스턴스 해제.");
            }
        }
        
        // ════════════════════════════════════════════════════════════════
        // 등록 / 해제
        // ════════════════════════════════════════════════════════════════
        
        public void Register(BridgeManager bridge)
        {
            if (bridge == null)
            {
                Debug.LogError($"[{nameof(WorldBridgeManager)}] null Bridge 등록 시도.", this);
                return;
            }
            
            var type = bridge.GetType();
            
            if (_bridges.TryGetValue(type, out var existing) && existing != bridge)
            {
                Debug.LogWarning(
                    $"[{nameof(WorldBridgeManager)}] {type.Name} 중복 등록 — 새 인스턴스 무시.", bridge);
                return;
            }
            
            _bridges[type] = bridge;
            RefreshInspectorView();
            
            if (_verboseLogging)
                Debug.Log(
                    $"[{nameof(WorldBridgeManager)}] Bridge 등록: " +
                    $"{type.Name} (Domain: {bridge.DomainName})");
        }
        
        /// <summary>등록된 게 self 일 때만 해제 (안전 모드).</summary>
        public void UnregisterIfMatches(BridgeManager bridge)
        {
            if (bridge == null) return;
            
            var type = bridge.GetType();
            
            if (_bridges.TryGetValue(type, out var registered) && registered == bridge)
            {
                _bridges.Remove(type);
                RefreshInspectorView();
                
                if (_verboseLogging)
                    Debug.Log(
                        $"[{nameof(WorldBridgeManager)}] Bridge 해제: " +
                        $"{type.Name} (Domain: {bridge.DomainName})");
            }
        }
        
        // ════════════════════════════════════════════════════════════════
        // 조회
        // ════════════════════════════════════════════════════════════════
        
        public T Get<T>() where T : BridgeManager
        {
            if (_bridges.TryGetValue(typeof(T), out var bridge))
                return (T)bridge;
            
            Debug.LogWarning(
                $"[{nameof(WorldBridgeManager)}] {typeof(T).Name} 등록 X. " +
                $"Awake 순서 / Bridge prefab 배치 / Script Execution Order 확인.");
            return null;
        }
        
        /// <summary>Type 으로 직접 조회 (BridgeManager 부모의 중복 검사용).</summary>
        public BridgeManager GetByType(Type bridgeType)
        {
            _bridges.TryGetValue(bridgeType, out var bridge);
            return bridge;
        }
        
        public BridgeManager GetByDomain(string domainName)
        {
            return _bridges.Values.FirstOrDefault(b => b.DomainName == domainName);
        }
        
        public bool IsRegistered<T>() where T : BridgeManager
        {
            return _bridges.ContainsKey(typeof(T));
        }
        
        private void RefreshInspectorView()
        {
            _registeredBridgesView.Clear();
            _registeredBridgesView.AddRange(_bridges.Values);
        }
        
        // ════════════════════════════════════════════════════════════════
        // ContextMenu (디버깅)
        // ════════════════════════════════════════════════════════════════
        
        [ContextMenu("Print Registered Bridges")]
        private void DebugPrintRegisteredBridges()
        {
            Debug.Log($"=== [{nameof(WorldBridgeManager)}] 등록된 Bridge: {_bridges.Count} 개 ===");
            foreach (var kvp in _bridges)
            {
                Debug.Log(
                    $"  - {kvp.Key.Name} " +
                    $"(Domain: {kvp.Value.DomainName}, " +
                    $"GameObject: {kvp.Value.gameObject.name})");
            }
        }
    }
}
