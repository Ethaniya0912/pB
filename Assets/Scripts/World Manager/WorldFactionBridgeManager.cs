// ============================================================================
// WorldFactionBridgeManager.cs
// Faction 도메인 Bridge 라우터
// 영역: 사이버 (Bridge 매니저)
// 항목: P-03
// ============================================================================
//
// 책임:
//   - 외부 도메인 (Terrain / Scenario / AI 외부) ↔ Faction 구현체 사이 라우팅
//   - IFactionStateProvider / IFactionStateCommandReceiver 노출
//   - AutoFindImplementation — 씬에서 WorldFactionStateManager 자동 발견
//   - WorldTerrainBridge 와 대칭 패턴
//
// ★ 어댑테이션 포인트:
//   사용자 측 Bridge 인프라 v13 에 BridgeManager 베이스 클래스가 있다면:
//     - public class WorldFactionBridgeManager : BridgeManager<IFactionStateProvider, IFactionStateCommandReceiver>
//     형태로 변경 가능. 현재는 MonoBehaviour 직접 상속.
//
// 사용 패턴:
//   var snapshot = WorldFactionBridgeManager.Instance.GetSnapshot("Goblin");
//   WorldFactionBridgeManager.Instance.SetMood("Goblin", FactionMoodTag.MOOD_PANIC, true);
//   WorldFactionBridgeManager.Instance.ForceMood("Goblin", FactionMoodTag.MOOD_LOYAL_TO_KING, true);
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Interfaces;
using TDA.PB4.Faction.Tags;

namespace TDA.PB4.Faction
{
    public class WorldFactionBridgeManager : MonoBehaviour,
        IFactionStateProvider,
        IFactionStateCommandReceiver
    {
        // ────────────────────────────────────────
        // Singleton
        // ────────────────────────────────────────
        
        public static WorldFactionBridgeManager Instance { get; private set; }
        
        // ────────────────────────────────────────
        // Inspector
        // ────────────────────────────────────────
        
        [Header("━━━ Bridge 설정 ━━━")]
        
        public string DomainName => "Faction";
        
        [Tooltip("씬에서 WorldFactionStateManager 자동 발견 — false 시 수동 할당")]
        [SerializeField] private bool _autoFindImplementation = true;
        
        [Tooltip("수동 할당용 — _autoFindImplementation = false 일 때 사용")]
        [SerializeField] private WorldFactionStateManager _manualImplementation;
        
        [Header("━━━ 디버그 ━━━")]
        
        [SerializeField] private bool _verboseLog = false;
        
        // ────────────────────────────────────────
        // 내부 참조
        // ────────────────────────────────────────
        
        private IFactionStateProvider        _provider;
        private IFactionStateCommandReceiver _receiver;
        
        public bool IsConnected => _provider != null && _receiver != null;
        
        // ────────────────────────────────────────
        // ★ v1.1 — Event 위임 (add/remove 패턴)
        //
        // 외부 도메인이 Bridge.OnFactionStateChanged += handler 호출 시
        // 실제로는 _provider (= WorldFactionStateManager) 의 event 에 위임.
        // Bridge 가 단순 라우터 역할만 — 자체 발행 X.
        // ────────────────────────────────────────
        
        public event Action<FactionStateChangeEventArgs> OnFactionStateChanged
        {
            add
            {
                if (_provider == null)
                {
                    Debug.LogWarning(
                        "[WorldFactionBridgeManager] OnFactionStateChanged add — _provider 없음. " +
                        "Bridge 의 ResolveImplementation 이후 다시 구독 필요.");
                    return;
                }
                _provider.OnFactionStateChanged += value;
            }
            remove
            {
                if (_provider == null) return;
                _provider.OnFactionStateChanged -= value;
            }
        }
        
        // ────────────────────────────────────────
        // Awake
        // ────────────────────────────────────────
        
        private void Awake()
        {
            // Singleton
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[WorldFactionBridgeManager] 중복 인스턴스 — 본 게임오브젝트 파괴");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            ResolveImplementation();
        }
        
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _provider = null;
            _receiver = null;
        }
        
        // ────────────────────────────────────────
        // 구현체 해석
        // ────────────────────────────────────────
        
        private void ResolveImplementation()
        {
            WorldFactionStateManager mgr;
            
            if (_autoFindImplementation)
            {
                mgr = FindObjectOfType<WorldFactionStateManager>();
                if (mgr == null)
                {
                    Debug.LogWarning(
                        "[WorldFactionBridgeManager] 씬에 WorldFactionStateManager 없음 — " +
                        "Bridge 연결 안됨. AutoFind off + 수동 할당 또는 매니저 추가 필요.");
                    return;
                }
            }
            else
            {
                mgr = _manualImplementation;
                if (mgr == null)
                {
                    Debug.LogWarning(
                        "[WorldFactionBridgeManager] 수동 할당 모드인데 _manualImplementation 비어있음.");
                    return;
                }
            }
            
            _provider = mgr;
            _receiver = mgr;
            
            if (_verboseLog)
                Debug.Log($"[WorldFactionBridgeManager] {DomainName} Bridge 연결 → {mgr.name}");
        }
        
        // ════════════════════════════════════════════════════════
        // IFactionStateProvider 위임
        // ════════════════════════════════════════════════════════
        
        public FactionStateSnapshot GetSnapshot(string factionId)
        {
            if (_provider == null)
            {
                WarnNoProvider(nameof(GetSnapshot));
                return default;
            }
            return _provider.GetSnapshot(factionId);
        }
        
        public bool HasFaction(string factionId)
        {
            if (_provider == null)
            {
                WarnNoProvider(nameof(HasFaction));
                return false;
            }
            return _provider.HasFaction(factionId);
        }
        
        public IReadOnlyList<string> GetAllFactionIds()
        {
            if (_provider == null)
            {
                WarnNoProvider(nameof(GetAllFactionIds));
                return System.Array.Empty<string>();
            }
            return _provider.GetAllFactionIds();
        }
        
        // ════════════════════════════════════════════════════════
        // IFactionStateCommandReceiver — SetXxx 위임
        // ════════════════════════════════════════════════════════
        
        public bool SetMood(string factionId, FactionMoodTag tag, bool on) =>
            _receiver?.SetMood(factionId, tag, on) ?? FailNoReceiver(nameof(SetMood));
        
        public bool SetTactical(string factionId, FactionTacticalTag tag, bool on) =>
            _receiver?.SetTactical(factionId, tag, on) ?? FailNoReceiver(nameof(SetTactical));
        
        public bool SetLifecycle(string factionId, FactionLifecycleTag tag, bool on) =>
            _receiver?.SetLifecycle(factionId, tag, on) ?? FailNoReceiver(nameof(SetLifecycle));
        
        public bool SetRelation(string factionId, FactionRelationTag tag, bool on) =>
            _receiver?.SetRelation(factionId, tag, on) ?? FailNoReceiver(nameof(SetRelation));
        
        // ════════════════════════════════════════════════════════
        // IFactionStateCommandReceiver — ForceXxx 위임
        // ════════════════════════════════════════════════════════
        
        public void ForceMood(string factionId, FactionMoodTag tag, bool on)
        {
            if (_receiver == null) { WarnNoReceiver(nameof(ForceMood)); return; }
            _receiver.ForceMood(factionId, tag, on);
        }
        
        public void ForceTactical(string factionId, FactionTacticalTag tag, bool on)
        {
            if (_receiver == null) { WarnNoReceiver(nameof(ForceTactical)); return; }
            _receiver.ForceTactical(factionId, tag, on);
        }
        
        public void ForceLifecycle(string factionId, FactionLifecycleTag tag, bool on)
        {
            if (_receiver == null) { WarnNoReceiver(nameof(ForceLifecycle)); return; }
            _receiver.ForceLifecycle(factionId, tag, on);
        }
        
        public void ForceRelation(string factionId, FactionRelationTag tag, bool on)
        {
            if (_receiver == null) { WarnNoReceiver(nameof(ForceRelation)); return; }
            _receiver.ForceRelation(factionId, tag, on);
        }
        
        // ────────────────────────────────────────
        // 진단 / 로깅
        // ────────────────────────────────────────
        
        private void WarnNoProvider(string method) =>
            Debug.LogWarning($"[WorldFactionBridgeManager] {method} — _provider 없음 (구현체 미해석)");
        
        private void WarnNoReceiver(string method) =>
            Debug.LogWarning($"[WorldFactionBridgeManager] {method} — _receiver 없음 (구현체 미해석)");
        
        private bool FailNoReceiver(string method)
        {
            WarnNoReceiver(method);
            return false;
        }
        
        [ContextMenu("Print Bridge Status")]
        private void PrintBridgeStatus()
        {
            Debug.Log($"[WorldFactionBridgeManager] DomainName={DomainName} " +
                      $"AutoFind={_autoFindImplementation} " +
                      $"Connected={IsConnected} " +
                      $"FactionCount={_provider?.GetAllFactionIds().Count ?? 0}");
        }
        
        [ContextMenu("Re-resolve Implementation")]
        private void RerResolveImplementation() => ResolveImplementation();
    }
}
