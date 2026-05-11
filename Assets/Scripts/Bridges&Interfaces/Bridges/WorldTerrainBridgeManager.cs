using UnityEngine;
using CaveSystem;          // IJunctionContextAnalyzer + ElementKind, IdentityTag, StateTag, EventTag, PotentialTag, ContextTagSet, NodeRole, PassageRole, RegionRole, CrossoverRole, TagApplicationLog, ScenarioContext, PassageMetric
using TDA.PB4.Terrain;     // JunctionLifecycleManager, JunctionStage
using System.Collections.Generic;

namespace TDA.PB4.Bridge
{
    // ════════════════════════════════════════════════════════════════════
    // WorldTerrainBridgeManager — Terrain 도메인 진입점 (★ 팀원 가이드)
    // ════════════════════════════════════════════════════════════════════
    //
    // 【이 클래스가 하는 일】
    //   외부 도메인 (AI / Scenario / Player) 이 Terrain 호출 시 단일 진입점.
    //   IJunctionContextAnalyzer + JunctionLifecycleManager 에 위임.
    //
    // 【설계 결정 — 인터페이스 의존 + 자동 검색】
    //   ○ IJunctionContextAnalyzer 인터페이스에만 의존 (구현체 모름)
    //   ○ Awake 시 씬에서 구현체 자동 검색 (TerrainContextAnalyzer 자동 발견)
    //   ○ JunctionLifecycleManager 는 협업 계약 인터페이스 미정의 — 구체 타입 유지
    //
    // 【씬에 배치】
    //   1. 빈 GameObject — "WorldTerrainBridgeManager" (root)
    //   2. 본 컴포넌트 추가
    //   3. 자동 검색 사용 시 Inspector 할당 불필요 — TerrainContextAnalyzer 와
    //      JunctionLifecycleManager 가 씬에 있으면 자동 발견
    //
    // 【외부 도메인 호출 예시】
    //   bool isBoss = WorldTerrainBridgeManager.Instance
    //       .HasIdentity(nodeIdx, ElementKind.Node, IdentityTag.IDENTITY_BOSS);
    //   var role = WorldTerrainBridgeManager.Instance.GetNodeRole(nodeIdx);
    //   WorldTerrainBridgeManager.Instance.FixateJunction();
    //
    // 【디버깅】
    //   Inspector > _debugLog 체크 시 모든 통신 로그 / 호출 통계 / 호출 history.
    // ════════════════════════════════════════════════════════════════════
    
    public class WorldTerrainBridgeManager : BridgeManager
    {
        public static WorldTerrainBridgeManager Instance =>
            WorldBridgeManager.HasInstance
                ? WorldBridgeManager.Instance.Get<WorldTerrainBridgeManager>()
                : null;
        
        public override string DomainName => "Terrain";
        
        // ════════════════════════════════════════════
        // 위임 대상 — 자동 검색 + 수동 할당 가능
        // ════════════════════════════════════════════
        
        [Header("Delegation Targets (Auto-found, override 가능)")]
        [SerializeField, Tooltip("IJunctionContextAnalyzer 구현 컴포넌트 (예: TerrainContextAnalyzer)")]
        private MonoBehaviour _contextAnalyzerImpl;
        private IJunctionContextAnalyzer ContextAnalyzer => _contextAnalyzerImpl as IJunctionContextAnalyzer;
        
        [SerializeField, Tooltip("정크션 4단계 lifecycle. TDA.PB4.Terrain.")]
        private JunctionLifecycleManager _lifecycle;
        
        // ════════════════════════════════════════════
        // 등록 후 후크 — 자동 검색 + 검증
        // ════════════════════════════════════════════
        
        protected override void OnBridgeRegistered()
        {
            if (_contextAnalyzerImpl == null)
                _contextAnalyzerImpl = AutoFindImplementation<IJunctionContextAnalyzer>();
            
            if (_lifecycle == null)
                _lifecycle = AutoFindComponent<JunctionLifecycleManager>();
            
            ValidateDelegationTargets();
        }
        
        private void ValidateDelegationTargets()
        {
            if (_contextAnalyzerImpl == null)
                Debug.LogError(
                    $"[{nameof(WorldTerrainBridgeManager)}] IJunctionContextAnalyzer 미할당. " +
                    $"AutoFind 도 실패 — 씬에 TerrainContextAnalyzer 없음.", this);
            else if (ContextAnalyzer == null)
                Debug.LogError(
                    $"[{nameof(WorldTerrainBridgeManager)}] _contextAnalyzerImpl 가 " +
                    $"IJunctionContextAnalyzer 미구현 ({_contextAnalyzerImpl.GetType().Name}).", this);
            
            if (_lifecycle == null)
                Debug.LogWarning(
                    $"[{nameof(WorldTerrainBridgeManager)}] JunctionLifecycleManager 미할당.", this);
        }
        
        // ════════════════════════════════════════════════════════════════
        // ─── IJunctionContextAnalyzer 라우팅 ─────────────────────────
        // ════════════════════════════════════════════════════════════════
        
        public bool IsTerrainContextReady => ContextAnalyzer?.IsReady ?? false;
        
        // ── Tag 4 카테고리 검사 ──
        public bool HasIdentity(int idx, ElementKind kind, IdentityTag tag)
        {
            if (ContextAnalyzer == null) return false;
            DebugLog("→ IJunctionContextAnalyzer", "HasIdentity", $"idx={idx}, kind={kind}, tag={tag}");
            var result = ContextAnalyzer.HasIdentity(idx, kind, tag);
            DebugLog("← IJunctionContextAnalyzer", "HasIdentity", null, result);
            return result;
        }
        
        public bool HasState(int idx, ElementKind kind, StateTag tag)
        {
            if (ContextAnalyzer == null) return false;
            DebugLog("→ IJunctionContextAnalyzer", "HasState", $"idx={idx}, kind={kind}, tag={tag}");
            return ContextAnalyzer.HasState(idx, kind, tag);
        }
        
        public bool HasEvent(int idx, ElementKind kind, EventTag tag)
        {
            if (ContextAnalyzer == null) return false;
            DebugLog("→ IJunctionContextAnalyzer", "HasEvent", $"idx={idx}, kind={kind}, tag={tag}");
            return ContextAnalyzer.HasEvent(idx, kind, tag);
        }
        
        public bool HasPotential(int idx, ElementKind kind, PotentialTag tag)
        {
            if (ContextAnalyzer == null) return false;
            DebugLog("→ IJunctionContextAnalyzer", "HasPotential", $"idx={idx}, kind={kind}, tag={tag}");
            return ContextAnalyzer.HasPotential(idx, kind, tag);
        }
        
        public ContextTagSet GetTags(int idx, ElementKind kind)
        {
            if (ContextAnalyzer == null) return default;
            DebugLog("→ IJunctionContextAnalyzer", "GetAllTags", $"idx={idx}, kind={kind}");
            return ContextAnalyzer.GetAllTags(idx, kind);
        }
        
        // ── Role 조회 ──
        public NodeRole GetNodeRole(int nodeIdx)
        {
            if (ContextAnalyzer == null) return default;
            var result = ContextAnalyzer.GetNodeRole(nodeIdx);
            DebugLog("→ IJunctionContextAnalyzer", "GetNodeRole", $"idx={nodeIdx}", result);
            return result;
        }
        
        public PassageRole GetPassageRole(int edgeIdx)
        {
            if (ContextAnalyzer == null) return default;
            var result = ContextAnalyzer.GetPassageRole(edgeIdx);
            DebugLog("→ IJunctionContextAnalyzer", "GetPassageRole", $"idx={edgeIdx}", result);
            return result;
        }
        
        public RegionRole GetRegionRole(int idx)
            => ContextAnalyzer != null ? ContextAnalyzer.GetRegionRole(idx) : default;
        
        public CrossoverRole GetCrossoverRole(int idx)
            => ContextAnalyzer != null ? ContextAnalyzer.GetCrossoverRole(idx) : default;
        
        // ── Possible Roles ──
        public NodeRole[] GetPossibleNodeRoles(int idx)
            => ContextAnalyzer?.GetPossibleNodeRoles(idx) ?? System.Array.Empty<NodeRole>();
        
        public PassageRole[] GetPossiblePassageRoles(int idx)
            => ContextAnalyzer?.GetPossiblePassageRoles(idx) ?? System.Array.Empty<PassageRole>();
        
        public RegionRole[] GetPossibleRegionRoles(int idx)
            => ContextAnalyzer?.GetPossibleRegionRoles(idx) ?? System.Array.Empty<RegionRole>();
        
        public CrossoverRole[] GetPossibleCrossoverRoles(int idx)
            => ContextAnalyzer?.GetPossibleCrossoverRoles(idx) ?? System.Array.Empty<CrossoverRole>();
        
        // ── Tag 적용 추적 / 시나리오 ──
        public TagApplicationLog GetTagHistory(int idx, ElementKind kind)
        {
            if (ContextAnalyzer == null) return default;
            DebugLog("→ IJunctionContextAnalyzer", "GetTagHistory", $"idx={idx}, kind={kind}");
            return ContextAnalyzer.GetTagHistory(idx, kind);
        }
        
        public List<ScenarioContext> EnumerateContexts(int idx, ElementKind kind)
        {
            if (ContextAnalyzer == null) return new List<ScenarioContext>();
            DebugLog("→ IJunctionContextAnalyzer", "EnumerateContexts", $"idx={idx}, kind={kind}");
            return ContextAnalyzer.EnumerateContexts(idx, kind);
        }
        
        // ── 검색 ──
        public int[] FindNodesByIdentity(IdentityTag tag)
        {
            if (ContextAnalyzer == null) return System.Array.Empty<int>();
            var result = ContextAnalyzer.FindNodesByIdentity(tag);
            DebugLog("→ IJunctionContextAnalyzer", "FindNodesByIdentity", $"tag={tag}",
                $"count={result?.Length ?? 0}");
            return result;
        }
        
        public int[] FindEdgesByPotential(PotentialTag tag)
        {
            if (ContextAnalyzer == null) return System.Array.Empty<int>();
            var result = ContextAnalyzer.FindEdgesByPotential(tag);
            DebugLog("→ IJunctionContextAnalyzer", "FindEdgesByPotential", $"tag={tag}",
                $"count={result?.Length ?? 0}");
            return result;
        }
        
        public int[] FindElementsByEvent(EventTag tag, ElementKind kind)
        {
            if (ContextAnalyzer == null) return System.Array.Empty<int>();
            var result = ContextAnalyzer.FindElementsByEvent(tag, kind);
            DebugLog("→ IJunctionContextAnalyzer", "FindElementsByEvent", $"tag={tag}, kind={kind}",
                $"count={result?.Length ?? 0}");
            return result;
        }
        
        // ── Metrics ──
        public PassageMetric.RiskScore GetRiskScore(int idx)
            => ContextAnalyzer != null ? ContextAnalyzer.GetRiskScore(idx) : default;
        
        public PassageMetric.PassageProfile GetPassageProfile(int idx)
            => ContextAnalyzer != null ? ContextAnalyzer.GetPassageProfile(idx) : default;
        
        // ── Rebuild ──
        public void RebuildTerrainContext()
        {
            if (ContextAnalyzer == null) return;
            DebugLog("→ IJunctionContextAnalyzer", "Rebuild");
            ContextAnalyzer.Rebuild();
        }
        
        // ════════════════════════════════════════════════════════════════
        // ─── JunctionLifecycleManager 라우팅 ─────────────────────────
        // ════════════════════════════════════════════════════════════════
        
        public JunctionStage GetJunctionStage()
        {
            if (_lifecycle == null) return JunctionStage.Dormant;
            var result = _lifecycle.CurrentStage;
            DebugLog("→ JunctionLifecycleManager", "GetJunctionStage", null, result);
            return result;
        }
        
        public float GetJunctionDistance()
            => _lifecycle != null ? _lifecycle.CurrentDistance : -1f;
        
        public void FixateJunction()
        {
            if (_lifecycle == null)
            {
                DebugLogError("→ JunctionLifecycleManager", "FixateJunction", "위임 대상 null");
                return;
            }
            DebugLog("→ JunctionLifecycleManager", "Fixate");
            _lifecycle.Fixate();
        }
        
        // ════════════════════════════════════════════
        // ContextMenu (도메인 특화)
        // ════════════════════════════════════════════
        
        [ContextMenu("[5] Validate Delegation Targets")]
        private void DebugValidateDelegationTargets()
        {
            ValidateDelegationTargets();
            Debug.Log($"[{nameof(WorldTerrainBridgeManager)}] 위임 대상 검증 완료.");
        }
        
        [ContextMenu("[6] Force Auto-Find")]
        private void DebugForceAutoFind()
        {
            _contextAnalyzerImpl = AutoFindImplementation<IJunctionContextAnalyzer>();
            _lifecycle = AutoFindComponent<JunctionLifecycleManager>();
            ValidateDelegationTargets();
            Debug.Log($"[{nameof(WorldTerrainBridgeManager)}] 강제 자동 검색 완료.");
        }
        
        [ContextMenu("[7] Print Terrain Domain Status")]
        private void DebugPrintTerrainDomainStatus()
        {
            Debug.Log(
                $"=== [{nameof(WorldTerrainBridgeManager)}] Terrain 도메인 상태 ===\n" +
                $"  IJunctionContextAnalyzer: " +
                  $"{(ContextAnalyzer != null ? $"OK ({_contextAnalyzerImpl.GetType().Name}, Ready={ContextAnalyzer.IsReady})" : "★ null")}\n" +
                $"  JunctionLifecycleManager: " +
                  $"{(_lifecycle != null ? $"OK (Stage={_lifecycle.CurrentStage})" : "null")}");
        }
        
        // ════════════════════════════════════════════════════════════════
        // ─── ★ Mock Test ContextMenu ───────────────────────────────
        // ════════════════════════════════════════════════════════════════
        
        [ContextMenu("[Mock T1] Test GetNodeRole(idx=0)")]
        private void MockT1_GetNodeRole()
        {
            RunMock("GetNodeRole(0)", () =>
            {
                var role = GetNodeRole(0);
                Debug.Log($"[MOCK] NodeRole = {role}");
            });
        }
        
        [ContextMenu("[Mock T2] Test HasIdentity(0, Node, BOSS)")]
        private void MockT2_HasIdentity()
        {
            RunMock("HasIdentity(0, Node, BOSS)", () =>
            {
                var result = HasIdentity(0, ElementKind.Node, IdentityTag.IDENTITY_BOSS);
                Debug.Log($"[MOCK] HasIdentity = {result}");
            });
        }
        
        [ContextMenu("[Mock T3] Test FindNodesByIdentity(BOSS)")]
        private void MockT3_FindBossNodes()
        {
            RunMock("FindNodesByIdentity(BOSS)", () =>
            {
                var nodes = FindNodesByIdentity(IdentityTag.IDENTITY_BOSS);
                Debug.Log($"[MOCK] Boss nodes count: {nodes?.Length ?? 0}");
                if (nodes != null && nodes.Length > 0)
                {
                    var preview = string.Join(", ", System.Linq.Enumerable.Take(nodes, 5));
                    Debug.Log($"[MOCK] First 5: [{preview}]");
                }
            });
        }
        
        [ContextMenu("[Mock T4] Test GetTags(idx=0, Node)")]
        private void MockT4_GetTags()
        {
            RunMock("GetTags(0, Node)", () =>
            {
                var tags = GetTags(0, ElementKind.Node);
                Debug.Log(
                    $"[MOCK] ContextTagSet @ idx=0:\n" +
                    $"  Identity bits: 0x{tags.identityTags:X8}\n" +
                    $"  State bits:    0x{tags.stateTags:X8}\n" +
                    $"  Event bits:    0x{tags.eventTags:X8}\n" +
                    $"  Potential bits: 0x{tags.potentialTags:X8}");
            });
        }
        
        [ContextMenu("[Mock T5] Test GetJunctionStage")]
        private void MockT5_GetJunctionStage()
        {
            RunMock("GetJunctionStage", () =>
            {
                var stage = GetJunctionStage();
                var dist = GetJunctionDistance();
                Debug.Log($"[MOCK] Stage: {stage}, Distance: {dist:F2}m");
            });
        }
        
        [ContextMenu("[Mock T6] Test FixateJunction")]
        private void MockT6_FixateJunction()
        {
            RunMock("FixateJunction", () =>
            {
                FixateJunction();
                var stage = GetJunctionStage();
                Debug.Log($"[MOCK] After Fixate: stage={stage}");
            });
        }
        
        [ContextMenu("[Mock T0] Run All Terrain Mock Tests")]
        private void MockT0_RunAll()
        {
            Debug.Log("════════════ [MOCK ALL] Terrain Bridge 전체 테스트 시작 ════════════");
            MockT1_GetNodeRole();
            MockT2_HasIdentity();
            MockT3_FindBossNodes();
            MockT4_GetTags();
            MockT5_GetJunctionStage();
            // MockT6_FixateJunction();  // 영구 고착이라 마지막에 / 또는 별도 호출
            Debug.Log("════════════ [MOCK ALL] Terrain Bridge 전체 테스트 종료 ════════════");
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
