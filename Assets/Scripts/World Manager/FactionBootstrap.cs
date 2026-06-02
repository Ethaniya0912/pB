// ============================================================================
// FactionBootstrap.cs
// 씬 진입 시 FactionDefinitionSO 측 자동 발견 + WorldFactionStateManager 측 등록
// 영역: 사이버 (Bootstrap Service)
// 항목: P-04 / E-05
// ============================================================================
//
// 책임:
//   - Resources/AI_Faction/ 측 FactionDefinitionSO 자산 측 자동 발견
//   - WorldFactionStateManager.Instance.RegisterFaction(def) 측 본격 호출
//   - (옵션) WorldFactionStateManager + WorldFactionBridgeManager 측 자동 발견 / 생성
//   - 기본 초기 상태 측 LIFECYCLE_ACTIVE 측 정합 (StateManager 측 RegisterFaction 측 본격)
//
// 사용 패턴:
//   ① 씬 측 빈 GameObject "FactionBootstrap" 측 본 컴포넌트 측 추가
//   ② Resources/AI_Faction/ 폴더 측 FactionDefinition_*.asset 측 배치
//   ③ Play 진입 시 측 본 컴포넌트 측 Awake 측 자동 등록 본격
//
// 본 컴포넌트 측 본 영역 (메타 모델 v3 §6.4 정합):
//   - 도메인: Faction
//   - 영역: 사이버 (전역 단일 / Awake 시점 측 본격 진입)
//   - 단일 책임: \"씬 진입 시 측 Faction 측 자동 등록\"
//
// 본 컴포넌트 측 본 동작 측 본격 절차:
//   1. WorldFactionStateManager.Instance 측 보장 (없으면 본격 발견 / 옵션 측 생성)
//   2. WorldFactionBridgeManager.Instance 측 보장 (Bridge 측 _provider 측 자동 발견 측 트리거)
//   3. Resources.LoadAll<FactionDefinitionSO>(_resourcesSubPath) 측 본격 발견
//   4. 발견 0 측 _manualList 측 fallback 측 사용
//   5. 각 FactionDefinitionSO 측 RegisterFaction 측 본격 호출
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Data;

namespace TDA.PB4.Faction
{
    [DefaultExecutionOrder(-100)]  // Singleton 매니저들보다 먼저 진입 측 의도
    public class FactionBootstrap : MonoBehaviour
    {
        // ────────────────────────────────────────
        // Inspector — 자동 발견 설정
        // ────────────────────────────────────────
        
        [Header("━━━ 자동 발견 (Resources) ━━━")]
        
        [Tooltip("Resources.LoadAll 측 본격 자동 발견 — true 권장")]
        [SerializeField] private bool _autoDiscoverFromResources = true;
        
        [Tooltip("Resources 측 본 sub 경로 (예: \"AI_Faction\" → Resources/AI_Faction/*.asset)")]
        [SerializeField] private string _resourcesSubPath = "AI_Faction";
        
        // ────────────────────────────────────────
        // Inspector — 수동 fallback
        // ────────────────────────────────────────
        
        [Header("━━━ 수동 list (자동 발견 0 시 fallback) ━━━")]
        
        [Tooltip("Resources 측 자동 발견 0 시 본 list 측 사용. 빈 list 측 fallback 없음.")]
        [SerializeField] private List<FactionDefinitionSO> _manualList = new List<FactionDefinitionSO>();
        
        // ────────────────────────────────────────
        // Inspector — 자동 생성 (옵션)
        // ────────────────────────────────────────
        
        [Header("━━━ 매니저 자동 생성 (디자이너 측 본격 배치 측 fallback) ━━━")]
        
        [Tooltip("WorldFactionStateManager 측 씬 측 미발견 시 측 GameObject 측 자동 생성. " +
                 "디자이너 측 본격 GameObject 측 배치 권장 (Inspector 측 본격 정합 가능).")]
        [SerializeField] private bool _autoCreateStateManager = false;
        
        [Tooltip("WorldFactionBridgeManager 측 씬 측 미발견 시 측 GameObject 측 자동 생성.")]
        [SerializeField] private bool _autoCreateBridgeManager = false;
        
        // ────────────────────────────────────────
        // Inspector — 디버그
        // ────────────────────────────────────────
        
        [Header("━━━ 디버그 ━━━")]
        
        [SerializeField] private bool _verboseLog = true;
        
        // ────────────────────────────────────────
        // Awake — 본격 진입
        // ────────────────────────────────────────
        
        private void Awake()
        {
            // 1. WorldFactionStateManager 측 본격 발견 / 생성
            var stateManager = EnsureStateManager();
            if (stateManager == null)
            {
                Debug.LogError(
                    "[FactionBootstrap] WorldFactionStateManager 측 없음 — Bootstrap 측 본격 중단. " +
                    "씬 측 GameObject 측 배치 또는 _autoCreateStateManager 측 true 권장.");
                return;
            }
            
            // 2. WorldFactionBridgeManager 측 본격 발견 / 생성
            EnsureBridgeManager();
            
            // 3. FactionDefinitionSO 측 본격 발견
            var definitions = DiscoverDefinitions();
            if (definitions.Count == 0)
            {
                Debug.LogWarning(
                    $"[FactionBootstrap] FactionDefinitionSO 측 발견 0 — Resources/{_resourcesSubPath}/ " +
                    $"측 자산 측 배치 또는 _manualList 측 정합 필요.");
                return;
            }
            
            // 4. 각 FactionDefinitionSO 측 본격 등록
            int registered = 0;
            foreach (var def in definitions)
            {
                if (stateManager.RegisterFaction(def)) registered++;
            }
            
            Debug.Log(
                $"[FactionBootstrap] {registered} / {definitions.Count} Faction 측 본격 등록 " +
                $"(중복 측 자동 skip)");
        }
        
        // ────────────────────────────────────────
        // 본격 도구 — Manager 측 본격 발견 / 생성
        // ────────────────────────────────────────
        
        private WorldFactionStateManager EnsureStateManager()
        {
            // 1. Singleton 측 본격 점검
            var mgr = WorldFactionStateManager.Instance;
            if (mgr != null) return mgr;
            
            // 2. 씬 측 본격 발견
            mgr = FindObjectOfType<WorldFactionStateManager>();
            if (mgr != null) return mgr;
            
            // 3. 자동 생성 측 옵션 점검
            if (!_autoCreateStateManager) return null;
            
            var go = new GameObject("WorldFactionStateManager (Auto)");
            mgr = go.AddComponent<WorldFactionStateManager>();
            DontDestroyOnLoad(go);
            
            if (_verboseLog)
                Debug.Log("[FactionBootstrap] WorldFactionStateManager 측 자동 생성");
            
            return mgr;
        }
        
        private void EnsureBridgeManager()
        {
            // 1. Singleton 측 본격 점검
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge != null) return;
            
            // 2. 씬 측 본격 발견
            bridge = FindObjectOfType<WorldFactionBridgeManager>();
            if (bridge != null) return;
            
            // 3. 자동 생성 측 옵션 점검
            if (!_autoCreateBridgeManager) return;
            
            var go = new GameObject("WorldFactionBridgeManager (Auto)");
            go.AddComponent<WorldFactionBridgeManager>();
            DontDestroyOnLoad(go);
            
            if (_verboseLog)
                Debug.Log("[FactionBootstrap] WorldFactionBridgeManager 측 자동 생성");
        }
        
        // ────────────────────────────────────────
        // 본격 도구 — FactionDefinitionSO 측 본격 발견
        // ────────────────────────────────────────
        
        private List<FactionDefinitionSO> DiscoverDefinitions()
        {
            var list = new List<FactionDefinitionSO>();
            
            // 1. Resources 측 자동 발견
            if (_autoDiscoverFromResources)
            {
                var loaded = Resources.LoadAll<FactionDefinitionSO>(_resourcesSubPath);
                if (loaded != null && loaded.Length > 0)
                {
                    list.AddRange(loaded);
                    if (_verboseLog)
                        Debug.Log(
                            $"[FactionBootstrap] Resources/{_resourcesSubPath}/ 측 " +
                            $"{loaded.Length} 자산 측 본격 발견");
                }
            }
            
            // 2. 수동 fallback (Resources 측 0 발견 시)
            if (list.Count == 0 && _manualList.Count > 0)
            {
                foreach (var def in _manualList)
                {
                    if (def != null) list.Add(def);
                }
                
                if (_verboseLog)
                    Debug.Log(
                        $"[FactionBootstrap] 수동 list 측 {list.Count} 자산 측 본격 사용 (fallback)");
            }
            
            return list;
        }
        
        // ────────────────────────────────────────
        // 진단 (ContextMenu)
        // ────────────────────────────────────────
        
        [ContextMenu("Re-discover and Register")]
        private void RediscoverAndRegister()
        {
            var mgr = WorldFactionStateManager.Instance;
            if (mgr == null)
            {
                Debug.LogWarning(
                    "[FactionBootstrap] WorldFactionStateManager.Instance 측 없음 — " +
                    "Play 진입 측 본격 호출 권장.");
                return;
            }
            
            var definitions = DiscoverDefinitions();
            int registered = 0;
            foreach (var def in definitions)
            {
                if (mgr.RegisterFaction(def)) registered++;
            }
            
            Debug.Log(
                $"[FactionBootstrap] Re-discover: {registered} 신규 등록 / " +
                $"{definitions.Count - registered} 중복 skip");
        }
        
        [ContextMenu("Print Discovered Definitions")]
        private void PrintDiscoveredDefinitions()
        {
            var definitions = DiscoverDefinitions();
            Debug.Log($"[FactionBootstrap] 발견 측 {definitions.Count} 자산:");
            foreach (var def in definitions)
            {
                Debug.Log($"  - {def.name} (FactionId={def.FactionId})");
            }
        }
    }
}
