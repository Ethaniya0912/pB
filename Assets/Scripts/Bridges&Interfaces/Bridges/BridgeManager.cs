using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TDA.PB4.Bridge
{
    // ════════════════════════════════════════════════════════════════════
    // BridgeManager — 모든 Bridge 의 추상 부모 (★ 팀원 가이드)
    // ════════════════════════════════════════════════════════════════════
    //
    // 【제공 기능 — 자식 Bridge 가 자동 상속】
    //   ○ DontDestroyOnLoad 자동
    //   ○ root GameObject 강제
    //   ○ WorldBridgeManager 자동 등록 / 해제
    //   ○ 중복 인스턴스 안전 처리 (Awake 시 검사 + 자체 파괴)
    //   ○ ★ 위임 대상 자동 검색 helper (AutoFindImplementation)
    //   ○ ★ Debug 로그 시스템 (방향 + 메서드 + 인자 + 결과)
    //   ○ ★ 호출 통계 (Inspector 표시)
    //   ○ ★ 호출 history (최근 10회)
    //   ○ ★ ContextMenu 진단 (공통)
    //
    // 【새 도메인 Bridge 추가 시 자식 책임】
    //   1. DomainName 프로퍼티 override
    //   2. 위임 대상 SerializeField 선언 (자동 검색 / 수동 둘 다 OK)
    //   3. OnBridgeRegistered() override — 자동 검색 호출
    //   4. 라우팅 메서드 (Request~ / Notify~ / Get~) 작성
    //   5. 각 라우팅에서 DebugLog 호출
    //
    // 【Debug 로그 사용 패턴】
    //   DebugLog("→ ISpawnRequestReceiver", "ExecuteSpawnRequest", req);
    //   var result = SpawnReceiver.ExecuteSpawnRequest(req);
    //   DebugLog("← ISpawnRequestReceiver", "ExecuteSpawnRequest", null, result);
    // ════════════════════════════════════════════════════════════════════
    
    [DefaultExecutionOrder(BridgeExecutionOrder.Bridge)]
    public abstract class BridgeManager : MonoBehaviour
    {
        public abstract string DomainName { get; }
        public bool IsRegistered { get; private set; }
        
        // ════════════════════════════════════════════
        // Debug & Settings
        // ════════════════════════════════════════════
        
        [Header("★ Debug")]
        [SerializeField, Tooltip("Debug 로그 출력 — 통신 방향 + 메서드명 + 인자 + 결과")]
        protected bool _debugLog = false;
        
        [SerializeField, Tooltip("자동 검색 비활성화 (수동 SerializeField 할당만 사용)")]
        protected bool _disableAutoFind = false;
        
        // ════════════════════════════════════════════
        // Statistics (Runtime, Inspector 표시)
        // ════════════════════════════════════════════
        
        [Header("★ Statistics (Runtime, Read-only)")]
        [SerializeField, Tooltip("Bridge 가 받은 총 호출 횟수")]
        private int _totalCalls;
        
        [SerializeField, Tooltip("최근 10 회 호출 기록")]
        [TextArea(3, 12)]
        private string _recentCallsLog;
        
        private readonly Queue<string> _recentCalls = new Queue<string>();
        private const int MaxRecentCalls = 10;
        
        // ════════════════════════════════════════════
        // Awake / OnDestroy
        // ════════════════════════════════════════════
        
        protected virtual void Awake()
        {
            // ── 1. 중복 인스턴스 검사 ──
            if (WorldBridgeManager.HasInstance)
            {
                var existing = WorldBridgeManager.Instance.GetByType(GetType());
                if (existing != null && existing != this)
                {
                    Debug.LogWarning(
                        $"[{GetType().Name}] 중복 인스턴스 감지 ({gameObject.name}). " +
                        $"기존 ({existing.gameObject.name}) 유지 / 본 인스턴스 파괴.", this);
                    Destroy(gameObject);
                    return;
                }
            }
            
            // ── 2. root GameObject 강제 ──
            if (transform.parent != null)
            {
                Debug.LogWarning(
                    $"[{GetType().Name}] {DomainName} Bridge 는 root GameObject 여야 합니다. " +
                    $"DontDestroyOnLoad 적용 위해 root 로 자동 이동.", this);
                transform.SetParent(null);
            }
            
            // ── 3. DontDestroyOnLoad ──
            DontDestroyOnLoad(gameObject);
            
            // ── 4. WorldBridgeManager 등록 ──
            if (!WorldBridgeManager.HasInstance)
            {
                Debug.LogError(
                    $"[{GetType().Name}] {DomainName} Bridge: WorldBridgeManager.Instance 가 null. " +
                    $"Script Execution Order 에서 WorldBridgeManager 가 먼저 Awake 되도록 설정 필요.", this);
                return;
            }
            
            WorldBridgeManager.Instance.Register(this);
            IsRegistered = true;
            
            // ── 5. 자식 Bridge 의 초기화 후크 ──
            OnBridgeRegistered();
        }
        
        protected virtual void OnDestroy()
        {
            if (!IsRegistered) return;
            
            if (WorldBridgeManager.HasInstance)
            {
                WorldBridgeManager.Instance.UnregisterIfMatches(this);
            }
            
            IsRegistered = false;
            OnBridgeUnregistered();
        }
        
        /// <summary>Bridge 등록 후 후크. 자식이 자동 검색 / 위임 대상 검증 시 override.</summary>
        protected virtual void OnBridgeRegistered() { }
        
        /// <summary>Bridge 해제 후 후크. 자식이 정리 시 override.</summary>
        protected virtual void OnBridgeUnregistered() { }
        
        // ════════════════════════════════════════════════════════════════
        // ─── 자동 검색 helper ────────────────────────────────────────
        // ════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// 씬에서 인터페이스 / 구체 타입 구현 컴포넌트 자동 검색.
        /// 자식 Bridge 의 OnBridgeRegistered() 에서 호출.
        /// 
        /// 여러 구현체 발견 시 첫 번째 선택 + Warning.
        /// _disableAutoFind 가 true 면 null 반환.
        /// </summary>
        protected MonoBehaviour AutoFindImplementation<T>() where T : class
        {
            if (_disableAutoFind) return null;
            
            var all = FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .OfType<T>()
                .Cast<MonoBehaviour>()
                .ToArray();
            
            if (all.Length == 0) return null;
            
            if (all.Length > 1)
            {
                Debug.LogWarning(
                    $"[{DomainName}Bridge] AutoFind — {typeof(T).Name} 구현체 {all.Length} 개 발견. " +
                    $"첫 번째 선택 ({all[0].GetType().Name} @ {all[0].gameObject.name}). " +
                    $"명시적 SerializeField 할당 권장.", this);
            }
            
            if (_debugLog)
                Debug.Log(
                    $"[{DomainName}Bridge] AutoFind 성공: {typeof(T).Name} " +
                    $"→ {all[0].GetType().Name} ({all[0].gameObject.name})", this);
            
            return all[0];
        }
        
        /// <summary>구체 Component 자동 검색.</summary>
        protected T AutoFindComponent<T>() where T : Component
        {
            if (_disableAutoFind) return null;
            
            var found = FindFirstObjectByType<T>(FindObjectsInactive.Exclude);
            
            if (found != null && _debugLog)
                Debug.Log(
                    $"[{DomainName}Bridge] AutoFind 성공: {typeof(T).Name} " +
                    $"→ {found.gameObject.name}", this);
            
            return found;
        }
        
        // ════════════════════════════════════════════════════════════════
        // ─── Debug 로그 helper ───────────────────────────────────────
        // ════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// 통신 로그 출력 + 통계 + history 기록.
        /// direction = "→ TargetName" (요청) 또는 "← TargetName" (응답).
        /// </summary>
        protected void DebugLog(string direction, string method, object args = null, object result = null)
        {
            _totalCalls++;
            
            // 메시지 조립
            var sb = new System.Text.StringBuilder();
            sb.Append($"[{DomainName}Bridge ").Append(direction).Append("] ").Append(method);
            if (args != null) sb.Append("(").Append(FormatArg(args)).Append(")");
            if (result != null) sb.Append(" → ").Append(FormatArg(result));
            
            var fullMsg = sb.ToString();
            
            // history (debug flag 무관)
            RecordCall($"[{Time.time:F2}s] {direction} {method}{(args != null ? $"({FormatArg(args)})" : "")}");
            
            // 로그 출력 (debug flag 켜진 경우만)
            if (_debugLog)
                Debug.Log(fullMsg, this);
        }
        
        protected void DebugLogWarning(string direction, string method, string reason)
        {
            var fullMsg = $"[{DomainName}Bridge {direction}] {method} — {reason}";
            RecordCall($"[{Time.time:F2}s] ⚠ {direction} {method}: {reason}");
            
            if (_debugLog)
                Debug.LogWarning(fullMsg, this);
        }
        
        protected void DebugLogError(string direction, string method, string reason)
        {
            var fullMsg = $"[{DomainName}Bridge {direction}] {method} — {reason}";
            RecordCall($"[{Time.time:F2}s] ✗ {direction} {method}: {reason}");
            
            // Error 는 debug flag 무관하게 항상 출력
            Debug.LogError(fullMsg, this);
        }
        
        private static string FormatArg(object arg)
        {
            if (arg == null) return "null";
            return arg.ToString();
        }
        
        private void RecordCall(string entry)
        {
            if (_recentCalls.Count >= MaxRecentCalls) _recentCalls.Dequeue();
            _recentCalls.Enqueue(entry);
            _recentCallsLog = string.Join("\n", _recentCalls);
        }
        
        // ════════════════════════════════════════════════════════════════
        // ─── ContextMenu (공통 진단) ─────────────────────────────────
        // ════════════════════════════════════════════════════════════════
        
        [ContextMenu("[1] Print Statistics")]
        protected virtual void DebugPrintStatistics()
        {
            Debug.Log(
                $"=== [{DomainName}Bridge] 통계 ===\n" +
                $"  Domain: {DomainName}\n" +
                $"  IsRegistered: {IsRegistered}\n" +
                $"  Total Calls: {_totalCalls}\n" +
                $"  DebugLog: {_debugLog}\n" +
                $"  AutoFind: {(_disableAutoFind ? "Disabled" : "Enabled")}");
        }
        
        [ContextMenu("[2] Print Recent Calls")]
        protected virtual void DebugPrintRecentCalls()
        {
            Debug.Log($"=== [{DomainName}Bridge] 최근 {_recentCalls.Count} 호출 ===");
            int i = 1;
            foreach (var call in _recentCalls)
                Debug.Log($"  #{i++}: {call}");
        }
        
        [ContextMenu("[3] Toggle Debug Log")]
        protected virtual void DebugToggleLog()
        {
            _debugLog = !_debugLog;
            Debug.Log($"[{DomainName}Bridge] DebugLog = {_debugLog}");
        }
        
        [ContextMenu("[4] Clear Statistics")]
        protected virtual void DebugClearStatistics()
        {
            _totalCalls = 0;
            _recentCalls.Clear();
            _recentCallsLog = "";
            Debug.Log($"[{DomainName}Bridge] 통계 초기화.");
        }
    }
    
    /// <summary>Bridge 관련 ExecutionOrder 상수.</summary>
    public static class BridgeExecutionOrder
    {
        public const int World = -1000;
        public const int Bridge = -500;
    }
}
