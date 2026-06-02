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
        
        // ════════════════════════════════════════════════════════
        // ★ v1.2 — Bridge 자체 통계 + DebugLog 양방향 패턴 (E-04 / S2 신규)
        // ════════════════════════════════════════════════════════
        // Bridge TeamGuide v1.2 §5.3 측 정합 — 호출 흐름 측 양방향 (→ in / ← out) 측 추적
        // 본 통계는 Bridge 측 자체 호출 누적 (StateManager 측 통계 = E-03 측 별도)
        // ════════════════════════════════════════════════════════
        
        private int _bridgeCallCount;
        
        public int BridgeCallCount => _bridgeCallCount;
        
        /// <summary>
        /// 양방향 Bridge 호출 로그 헬퍼.
        /// _verboseLog = true 시 콘솔 출력. 항상 _bridgeCallCount 증가.
        /// </summary>
        /// <param name="direction">"→" (외부 → Bridge 진입) 또는 "←" (Bridge → 외부 응답)</param>
        /// <param name="interfaceName">"IFactionStateProvider" 또는 "IFactionStateCommandReceiver"</param>
        /// <param name="method">메서드 명</param>
        /// <param name="args">진입 측 인자 (옵션)</param>
        /// <param name="result">응답 측 결과 (옵션)</param>
        private void DebugLog(string direction, string interfaceName, string method,
                              string args = null, object result = null)
        {
            if (direction == "→") _bridgeCallCount++;
            
            if (!_verboseLog) return;
            
            if (direction == "→")
                Debug.Log($"[FactionBridge → {interfaceName}] {method}({args})");
            else
                Debug.Log($"[FactionBridge ← {interfaceName}] {method} → {result}");
        }
        
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
            DebugLog("→", "IFactionStateProvider", nameof(GetSnapshot), $"factionId={factionId}");
            
            if (_provider == null)
            {
                WarnNoProvider(nameof(GetSnapshot));
                return default;
            }
            var result = _provider.GetSnapshot(factionId);
            DebugLog("←", "IFactionStateProvider", nameof(GetSnapshot), null,
                     $"IsValid={result.IsValid}");
            return result;
        }
        
        public bool HasFaction(string factionId)
        {
            DebugLog("→", "IFactionStateProvider", nameof(HasFaction), $"factionId={factionId}");
            
            if (_provider == null)
            {
                WarnNoProvider(nameof(HasFaction));
                return false;
            }
            var result = _provider.HasFaction(factionId);
            DebugLog("←", "IFactionStateProvider", nameof(HasFaction), null, result);
            return result;
        }
        
        public IReadOnlyList<string> GetAllFactionIds()
        {
            DebugLog("→", "IFactionStateProvider", nameof(GetAllFactionIds), "");
            
            if (_provider == null)
            {
                WarnNoProvider(nameof(GetAllFactionIds));
                return System.Array.Empty<string>();
            }
            var result = _provider.GetAllFactionIds();
            DebugLog("←", "IFactionStateProvider", nameof(GetAllFactionIds), null,
                     $"count={result?.Count ?? 0}");
            return result;
        }
        
        // ════════════════════════════════════════════════════════
        // IFactionStateCommandReceiver — SetXxx 위임
        // ════════════════════════════════════════════════════════
        
        public bool SetMood(string factionId, FactionMoodTag tag, bool on)
        {
            DebugLog("→", "IFactionStateCommandReceiver", nameof(SetMood),
                     $"factionId={factionId}, tag={tag}, on={on}");
            
            if (_receiver == null) return FailNoReceiver(nameof(SetMood));
            
            var result = _receiver.SetMood(factionId, tag, on);
            DebugLog("←", "IFactionStateCommandReceiver", nameof(SetMood), null, result);
            return result;
        }
        
        public bool SetTactical(string factionId, FactionTacticalTag tag, bool on)
        {
            DebugLog("→", "IFactionStateCommandReceiver", nameof(SetTactical),
                     $"factionId={factionId}, tag={tag}, on={on}");
            
            if (_receiver == null) return FailNoReceiver(nameof(SetTactical));
            
            var result = _receiver.SetTactical(factionId, tag, on);
            DebugLog("←", "IFactionStateCommandReceiver", nameof(SetTactical), null, result);
            return result;
        }
        
        public bool SetLifecycle(string factionId, FactionLifecycleTag tag, bool on)
        {
            DebugLog("→", "IFactionStateCommandReceiver", nameof(SetLifecycle),
                     $"factionId={factionId}, tag={tag}, on={on}");
            
            if (_receiver == null) return FailNoReceiver(nameof(SetLifecycle));
            
            var result = _receiver.SetLifecycle(factionId, tag, on);
            DebugLog("←", "IFactionStateCommandReceiver", nameof(SetLifecycle), null, result);
            return result;
        }
        
        public bool SetRelation(string factionId, FactionRelationTag tag, bool on)
        {
            DebugLog("→", "IFactionStateCommandReceiver", nameof(SetRelation),
                     $"factionId={factionId}, tag={tag}, on={on}");
            
            if (_receiver == null) return FailNoReceiver(nameof(SetRelation));
            
            var result = _receiver.SetRelation(factionId, tag, on);
            DebugLog("←", "IFactionStateCommandReceiver", nameof(SetRelation), null, result);
            return result;
        }
        
        // ════════════════════════════════════════════════════════
        // IFactionStateCommandReceiver — ForceXxx 위임
        // ════════════════════════════════════════════════════════
        
        public void ForceMood(string factionId, FactionMoodTag tag, bool on)
        {
            DebugLog("→", "IFactionStateCommandReceiver", nameof(ForceMood),
                     $"factionId={factionId}, tag={tag}, on={on}");
            
            if (_receiver == null) { WarnNoReceiver(nameof(ForceMood)); return; }
            _receiver.ForceMood(factionId, tag, on);
            
            DebugLog("←", "IFactionStateCommandReceiver", nameof(ForceMood), null, "void");
        }
        
        public void ForceTactical(string factionId, FactionTacticalTag tag, bool on)
        {
            DebugLog("→", "IFactionStateCommandReceiver", nameof(ForceTactical),
                     $"factionId={factionId}, tag={tag}, on={on}");
            
            if (_receiver == null) { WarnNoReceiver(nameof(ForceTactical)); return; }
            _receiver.ForceTactical(factionId, tag, on);
            
            DebugLog("←", "IFactionStateCommandReceiver", nameof(ForceTactical), null, "void");
        }
        
        public void ForceLifecycle(string factionId, FactionLifecycleTag tag, bool on)
        {
            DebugLog("→", "IFactionStateCommandReceiver", nameof(ForceLifecycle),
                     $"factionId={factionId}, tag={tag}, on={on}");
            
            if (_receiver == null) { WarnNoReceiver(nameof(ForceLifecycle)); return; }
            _receiver.ForceLifecycle(factionId, tag, on);
            
            DebugLog("←", "IFactionStateCommandReceiver", nameof(ForceLifecycle), null, "void");
        }
        
        public void ForceRelation(string factionId, FactionRelationTag tag, bool on)
        {
            DebugLog("→", "IFactionStateCommandReceiver", nameof(ForceRelation),
                     $"factionId={factionId}, tag={tag}, on={on}");
            
            if (_receiver == null) { WarnNoReceiver(nameof(ForceRelation)); return; }
            _receiver.ForceRelation(factionId, tag, on);
            
            DebugLog("←", "IFactionStateCommandReceiver", nameof(ForceRelation), null, "void");
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
        
        // ════════════════════════════════════════════════════════
        // ★ v1.2 — Bridge 통계 + Provider 통계 ContextMenu (E-04 / S2 신규)
        // ════════════════════════════════════════════════════════
        
        /// <summary>
        /// Bridge 측 자체 호출 누적 출력. StateManager 측 통계와 별도 — Bridge 측만 측정.
        /// </summary>
        [ContextMenu("Print Bridge Stats")]
        public void PrintBridgeStats()
        {
            Debug.Log(
                $"[WorldFactionBridgeManager] === Bridge Stats ===\n" +
                $"  Connected      : {IsConnected}\n" +
                $"  BridgeCallCount: {_bridgeCallCount}\n" +
                $"  VerboseLog     : {_verboseLog}");
        }
        
        /// <summary>
        /// 측 underlying StateManager 측 Validation Stats 측 위임 호출.
        /// E-03 측 통계 카운터 (Set / Force / MaskViolation / MapperApply + 카테고리별) 출력.
        /// </summary>
        [ContextMenu("Print Provider Stats")]
        public void PrintProviderStats()
        {
            if (_provider is WorldFactionStateManager mgr)
            {
                mgr.PrintValidationStats();
            }
            else
            {
                Debug.LogWarning(
                    "[WorldFactionBridgeManager] PrintProviderStats — " +
                    "_provider 가 WorldFactionStateManager 아님 (or null). Bridge 연결 미정합.");
            }
        }
        
        /// <summary>
        /// Bridge 측 자체 카운터 0 초기화. StateManager 측 통계는 보존.
        /// </summary>
        [ContextMenu("Reset Bridge Stats")]
        public void ResetBridgeStats()
        {
            _bridgeCallCount = 0;
            Debug.Log("[WorldFactionBridgeManager] Bridge stats reset.");
        }
    }
}
