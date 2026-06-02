// ============================================================================
// FactionBridgeDiagnostics.cs
// Faction 측 본격 디버그 / 진단 도구 — Inspector + ContextMenu 8 종
// 영역: 사이버 (Diagnostics Service)
// 항목: P-06 / E-06
// ============================================================================
//
// 책임:
//   - WorldFactionStateManager + WorldFactionBridgeManager 측 본격 디버그 진입점
//   - Inspector 측 통계 시각 (Set / Force / MaskViolation / MapperApply / Bridge 호출)
//   - ContextMenu 8 종 측 본격 — Print Snapshot / Force Mood / Test Set vs Force /
//     List Factions / Reset State / Force Lifecycle / Print Validation Stats / Dump JSON
//   - JSON dump 본격 — Faction 측 본격 상태 측 직렬화 (디버그 / 세이브 측 진입 자료)
//
// 사용 패턴:
//   ① 씬 측 빈 GameObject "FactionBridgeDiagnostics" 측 본 컴포넌트 측 추가
//   ② Inspector 측 _targetFactionId + 4 테스트 Tag 측 본격 정합
//   ③ Play 진입 시 측 우클릭 측 ContextMenu 측 본격 호출
//
// 본 컴포넌트 측 본 영역 (메타 모델 v3 §6.4 정합):
//   - 도메인: Faction
//   - 영역: 사이버 (Diagnostics — 전역 단일 진입점)
//   - 단일 책임: \"Faction 측 본격 진단 / 시각 / JSON dump\"
//
// 분리 의의 (SRP):
//   본격 WorldFactionStateManager + WorldFactionBridgeManager 측 본격 ContextMenu
//   측 본격 분리 — 디버그 도구 측 본 매니저 측 본격 책임 측 분리.
// ============================================================================

using System.Text;
using UnityEngine;
using TDA.PB4.Faction.Tags;

namespace TDA.PB4.Faction
{
    public class FactionBridgeDiagnostics : MonoBehaviour
    {
        // ────────────────────────────────────────
        // Inspector — 테스트 대상
        // ────────────────────────────────────────
        
        [Header("━━━ 테스트 대상 Faction ━━━")]
        
        [Tooltip("ContextMenu 측 본격 대상 FactionId (비어있으면 모든 Faction 측 본격 적용 — 일부 ContextMenu 측)")]
        [SerializeField] private string _targetFactionId = "Goblin";
        
        // ────────────────────────────────────────
        // Inspector — 테스트 Tag (4 카테고리)
        // ────────────────────────────────────────
        
        [Header("━━━ 테스트 Tag (4 카테고리) ━━━")]
        
        [SerializeField] private FactionMoodTag      _testMoodTag      = FactionMoodTag.MOOD_PANIC;
        [SerializeField] private FactionTacticalTag  _testTacticalTag  = FactionTacticalTag.TACTICAL_DEFENSIVE;
        [SerializeField] private FactionLifecycleTag _testLifecycleTag = FactionLifecycleTag.LIFECYCLE_THREATENED;
        [SerializeField] private FactionRelationTag  _testRelationTag  = FactionRelationTag.RELATION_PLAYER_HOSTILE;
        
        [Tooltip("테스트 Tag 측 ON (true) / OFF (false) 본격 정합")]
        [SerializeField] private bool _testTagOn = true;
        
        // ────────────────────────────────────────
        // Inspector — 통계 시각 (Runtime, read-only 캐시)
        // ────────────────────────────────────────
        
        [Header("━━━ 통계 (Runtime read-only — Play 진입 시 측 본격 갱신) ━━━")]
        
        [SerializeField] private int _cachedSetCallCount;
        [SerializeField] private int _cachedForceCallCount;
        [SerializeField] private int _cachedMaskViolationCount;
        [SerializeField] private int _cachedMapperApplyCount;
        [SerializeField] private int _cachedBridgeCallCount;
        
        [Header("━━━ 통계 — 카테고리별 분기 (Set) ━━━")]
        [SerializeField] private int _cachedSetMood;
        [SerializeField] private int _cachedSetTactical;
        [SerializeField] private int _cachedSetLifecycle;
        [SerializeField] private int _cachedSetRelation;
        
        [Header("━━━ 통계 — 카테고리별 분기 (Force) ━━━")]
        [SerializeField] private int _cachedForceMood;
        [SerializeField] private int _cachedForceTactical;
        [SerializeField] private int _cachedForceLifecycle;
        [SerializeField] private int _cachedForceRelation;
        
        [Header("━━━ 통계 — 카테고리별 분기 (MaskViolation) ━━━")]
        [SerializeField] private int _cachedMaskViolationMood;
        [SerializeField] private int _cachedMaskViolationTactical;
        [SerializeField] private int _cachedMaskViolationLifecycle;
        [SerializeField] private int _cachedMaskViolationRelation;
        
        // ────────────────────────────────────────
        // Update — 통계 캐시 갱신 (Play 진입 시)
        // ────────────────────────────────────────
        
        private void Update()
        {
            if (!Application.isPlaying) return;
            
            var mgr = WorldFactionStateManager.Instance;
            if (mgr != null)
            {
                _cachedSetCallCount       = mgr.SetCallCount;
                _cachedForceCallCount     = mgr.ForceCallCount;
                _cachedMaskViolationCount = mgr.MaskViolationCount;
                _cachedMapperApplyCount   = mgr.MapperApplyCount;
                
                _cachedSetMood      = mgr.GetSetCallsByCategory((int)FactionStateTagCategory.Mood);
                _cachedSetTactical  = mgr.GetSetCallsByCategory((int)FactionStateTagCategory.Tactical);
                _cachedSetLifecycle = mgr.GetSetCallsByCategory((int)FactionStateTagCategory.Lifecycle);
                _cachedSetRelation  = mgr.GetSetCallsByCategory((int)FactionStateTagCategory.Relation);
                
                _cachedForceMood      = mgr.GetForceCallsByCategory((int)FactionStateTagCategory.Mood);
                _cachedForceTactical  = mgr.GetForceCallsByCategory((int)FactionStateTagCategory.Tactical);
                _cachedForceLifecycle = mgr.GetForceCallsByCategory((int)FactionStateTagCategory.Lifecycle);
                _cachedForceRelation  = mgr.GetForceCallsByCategory((int)FactionStateTagCategory.Relation);
                
                _cachedMaskViolationMood      = mgr.GetMaskViolationsByCategory((int)FactionStateTagCategory.Mood);
                _cachedMaskViolationTactical  = mgr.GetMaskViolationsByCategory((int)FactionStateTagCategory.Tactical);
                _cachedMaskViolationLifecycle = mgr.GetMaskViolationsByCategory((int)FactionStateTagCategory.Lifecycle);
                _cachedMaskViolationRelation  = mgr.GetMaskViolationsByCategory((int)FactionStateTagCategory.Relation);
            }
            
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge != null)
            {
                _cachedBridgeCallCount = bridge.BridgeCallCount;
            }
        }
        
        // ════════════════════════════════════════════════════════
        // ContextMenu ① Print Snapshot
        // ════════════════════════════════════════════════════════
        
        /// <summary>
        /// _targetFactionId 측 본격 Snapshot 출력. 비어있으면 모든 Faction 측 출력.
        /// </summary>
        [ContextMenu("① Print Snapshot")]
        private void PrintSnapshot()
        {
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge == null) { WarnNoBridge("Print Snapshot"); return; }
            
            if (string.IsNullOrEmpty(_targetFactionId))
            {
                var ids = bridge.GetAllFactionIds();
                Debug.Log($"[Diagnostics] === Print Snapshot — {ids.Count} Faction 측 본격 출력 ===");
                foreach (var id in ids) PrintSingleSnapshot(id);
            }
            else
            {
                PrintSingleSnapshot(_targetFactionId);
            }
        }
        
        private void PrintSingleSnapshot(string factionId)
        {
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge == null) return;
            
            var snap = bridge.GetSnapshot(factionId);
            if (!snap.IsValid)
            {
                Debug.LogWarning($"[Diagnostics] {factionId} 측 등록 없음");
                return;
            }
            
            Debug.Log($"[Diagnostics] {snap}");
        }
        
        // ════════════════════════════════════════════════════════
        // ContextMenu ② Force Mood
        // ════════════════════════════════════════════════════════
        
        /// <summary>
        /// _targetFactionId 측 본격 _testMoodTag 측 Force 적용 (validMask 우회).
        /// </summary>
        [ContextMenu("② Force Mood")]
        private void ForceMoodMenu()
        {
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge == null) { WarnNoBridge("Force Mood"); return; }
            
            bridge.ForceMood(_targetFactionId, _testMoodTag, _testTagOn);
            Debug.Log(
                $"[Diagnostics] Force Mood — {_targetFactionId}.{_testMoodTag} = {_testTagOn}");
        }
        
        // ════════════════════════════════════════════════════════
        // ContextMenu ③ Test Set vs Force
        // ════════════════════════════════════════════════════════
        
        /// <summary>
        /// _testMoodTag 측 본격 Set vs Force 측 차이 측 본격 시각.
        /// Set 측 validMask 차단 시 Force 측 본격 우회 측 동작 측 박제.
        /// </summary>
        [ContextMenu("③ Test Set vs Force")]
        private void TestSetVsForce()
        {
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge == null) { WarnNoBridge("Test Set vs Force"); return; }
            
            Debug.Log(
                $"[Diagnostics] === Test Set vs Force — {_targetFactionId}.{_testMoodTag}={_testTagOn} ===");
            
            // 1. Set 측 본격 시도 (validMask 검사)
            bool setResult = bridge.SetMood(_targetFactionId, _testMoodTag, _testTagOn);
            string setLabel = setResult ? "✓ APPLIED" : "✗ REJECTED (validMask)";
            Debug.Log($"  ① Set : {setLabel}");
            
            // 2. Force 측 본격 시도 (validMask 우회)
            bridge.ForceMood(_targetFactionId, _testMoodTag, _testTagOn);
            Debug.Log($"  ② Force: ✓ APPLIED (validMask 우회)");
            
            // 3. 최종 Snapshot 측 본격 점검
            var snap = bridge.GetSnapshot(_targetFactionId);
            if (snap.IsValid)
            {
                bool hasTag = snap.HasMood(_testMoodTag);
                Debug.Log(
                    $"  ③ 최종 Snapshot — {_testMoodTag} 측 보유: {hasTag} " +
                    $"(MoodBits=0x{snap.MoodBits:X8})");
            }
        }
        
        // ════════════════════════════════════════════════════════
        // ContextMenu ④ List Factions
        // ════════════════════════════════════════════════════════
        
        /// <summary>
        /// 등록된 모든 Faction 측 본격 list 출력.
        /// </summary>
        [ContextMenu("④ List Factions")]
        private void ListFactionsMenu()
        {
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge == null) { WarnNoBridge("List Factions"); return; }
            
            var ids = bridge.GetAllFactionIds();
            Debug.Log($"[Diagnostics] === 등록 측 {ids.Count} Faction ===");
            foreach (var id in ids)
            {
                var snap = bridge.GetSnapshot(id);
                Debug.Log(
                    $"  - {id}: " +
                    $"M=0x{snap.MoodBits:X8} T=0x{snap.TacticalBits:X8} " +
                    $"L=0x{snap.LifecycleBits:X8} R=0x{snap.RelationBits:X8}");
            }
        }
        
        // ════════════════════════════════════════════════════════
        // ContextMenu ⑤ Reset State (모든 Faction)
        // ════════════════════════════════════════════════════════
        
        /// <summary>
        /// 모든 Faction 측 본격 초기 상태 (LIFECYCLE_ACTIVE) 측 reset.
        /// 본 동작 측 StateManager.ResetAllState() 측 본격 위임.
        /// </summary>
        [ContextMenu("⑤ Reset State (모든 Faction)")]
        private void ResetStateMenu()
        {
            var mgr = WorldFactionStateManager.Instance;
            if (mgr == null) { WarnNoMgr("Reset State"); return; }
            
            mgr.ResetAllState();
        }
        
        // ════════════════════════════════════════════════════════
        // ContextMenu ⑥ Force Lifecycle
        // ════════════════════════════════════════════════════════
        
        /// <summary>
        /// _targetFactionId 측 본격 _testLifecycleTag 측 Force 적용.
        /// </summary>
        [ContextMenu("⑥ Force Lifecycle")]
        private void ForceLifecycleMenu()
        {
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge == null) { WarnNoBridge("Force Lifecycle"); return; }
            
            bridge.ForceLifecycle(_targetFactionId, _testLifecycleTag, _testTagOn);
            Debug.Log(
                $"[Diagnostics] Force Lifecycle — {_targetFactionId}.{_testLifecycleTag} = {_testTagOn}");
        }
        
        // ════════════════════════════════════════════════════════
        // ContextMenu ⑦ Print Validation Stats
        // ════════════════════════════════════════════════════════
        
        /// <summary>
        /// StateManager 측 통계 + Bridge 측 통계 측 본격 통합 출력.
        /// E-03 측 PrintValidationStats + E-04 측 PrintBridgeStats 측 본격 위임.
        /// </summary>
        [ContextMenu("⑦ Print Validation Stats")]
        private void PrintValidationStatsMenu()
        {
            var mgr = WorldFactionStateManager.Instance;
            var bridge = WorldFactionBridgeManager.Instance;
            
            if (mgr != null)    mgr.PrintValidationStats();
            else                WarnNoMgr("Print Validation Stats");
            
            if (bridge != null) bridge.PrintBridgeStats();
            else                WarnNoBridge("Print Validation Stats");
        }
        
        // ════════════════════════════════════════════════════════
        // ContextMenu ⑧ Dump JSON
        // ════════════════════════════════════════════════════════
        
        /// <summary>
        /// 모든 Faction 측 본격 상태 측 JSON 측 본격 직렬화 + Console 출력.
        /// 디버그 / 세이브 / 시각화 측 진입 자료.
        /// </summary>
        [ContextMenu("⑧ Dump JSON")]
        private void DumpJson()
        {
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge == null) { WarnNoBridge("Dump JSON"); return; }
            
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"timestamp\": " + Time.time.ToString("F2") + ",");
            sb.AppendLine("  \"factions\": [");
            
            var ids = bridge.GetAllFactionIds();
            for (int i = 0; i < ids.Count; i++)
            {
                var snap = bridge.GetSnapshot(ids[i]);
                if (!snap.IsValid) continue;
                
                sb.AppendLine("    {");
                sb.AppendLine($"      \"factionId\": \"{snap.FactionId}\",");
                sb.AppendLine($"      \"moodBits\": \"0x{snap.MoodBits:X8}\",");
                sb.AppendLine($"      \"tacticalBits\": \"0x{snap.TacticalBits:X8}\",");
                sb.AppendLine($"      \"lifecycleBits\": \"0x{snap.LifecycleBits:X8}\",");
                sb.AppendLine($"      \"relationBits\": \"0x{snap.RelationBits:X8}\",");
                sb.AppendLine($"      \"timestamp\": {snap.Timestamp:F2}");
                string trailingComma = (i < ids.Count - 1) ? "," : "";
                sb.AppendLine($"    }}{trailingComma}");
            }
            
            sb.AppendLine("  ]");
            sb.Append("}");
            
            Debug.Log($"[Diagnostics] === JSON Dump ({ids.Count} Faction) ===\n{sb}");
        }
        
        // ────────────────────────────────────────
        // 경고 헬퍼
        // ────────────────────────────────────────
        
        private static void WarnNoBridge(string menuName) =>
            Debug.LogWarning(
                $"[Diagnostics] {menuName} — WorldFactionBridgeManager.Instance 측 없음. " +
                $"씬 측 본격 GameObject 측 배치 필요.");
        
        private static void WarnNoMgr(string menuName) =>
            Debug.LogWarning(
                $"[Diagnostics] {menuName} — WorldFactionStateManager.Instance 측 없음. " +
                $"씬 측 본격 GameObject 측 배치 필요.");
    }
}
