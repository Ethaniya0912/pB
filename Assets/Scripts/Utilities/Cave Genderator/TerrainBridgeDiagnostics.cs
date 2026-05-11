using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using CaveSystem;
using TDA.PB4.Bridge;

namespace TDA.PB4.Diagnostics
{
    // ════════════════════════════════════════════════════════════════════
    // TerrainBridgeDiagnostics.cs v2  |  Terrain Bridge 풍부한 Live 시나리오
    // ════════════════════════════════════════════════════════════════════
    //
    // 【v1 → v2 변경】
    //   ★ T9 Edge Tag 분포 추가          — State Tag 가 Edge 에 있는 듯
    //   ★ T10 RiskScore Reflection 추가  — 형 이름만 출력 → 필드 디코딩
    //   ★ T11 노드 통합 분석 추가          — Node + Edge + Risk + Profile
    //   ★ T12 ScenarioContext 일람 추가   — EnumerateContexts API
    //   ★ T0 Run All 확장                — 12 시나리오 순차
    //
    // 【설치】
    //   1. 빈 GameObject "[Diag] TerrainBridge" 생성
    //   2. 본 컴포넌트 추가 (v1 컴포넌트가 있으면 교체)
    //   3. Play 모드 진입 → Inspector ContextMenu 로 시나리오 실행
    // ════════════════════════════════════════════════════════════════════

    public class TerrainBridgeDiagnostics : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField, Tooltip("Console 로그 출력")]
        private bool _verboseLogging = true;

        [SerializeField, Tooltip("단일 노드 분석 메뉴의 대상 인덱스 (T4, T10, T11, T12)")]
        private int _targetNodeIdx = 0;

        [SerializeField, Tooltip("Edge 분석 시 최대 검색 인덱스 (T9)")]
        private int _maxEdgeIdxToProbe = 100;

        [Header("★ Last Result (Runtime, Read-only)")]
        [SerializeField]
        private string _lastTestName = "(아직 없음)";

        [SerializeField, TextArea(5, 30)]
        private string _lastResultSummary = "(아직 실행 안 됨)";

        // ════════════════════════════════════════════════════════════════
        // Bridge 접근
        // ════════════════════════════════════════════════════════════════

        private WorldTerrainBridgeManager Bridge
            => WorldTerrainBridgeManager.Instance;

        private bool ValidateBridge(out string errorMsg)
        {
            errorMsg = null;
            if (Bridge == null) { errorMsg = "★ WorldTerrainBridgeManager.Instance = null"; return false; }
            if (!Bridge.IsTerrainContextReady) { errorMsg = "★ TerrainContextAnalyzer 미준비"; return false; }
            return true;
        }

        // ════════════════════════════════════════════════════════════════
        // Live T1 — 14 IdentityTag 모두 검색
        // ════════════════════════════════════════════════════════════════

        [ContextMenu("[Live T1] 14 IdentityTag 모두 검색")]
        public void Live_AllIdentityCensus()
        {
            _lastTestName = "Live T1 — 14 IdentityTag 모두 검색";
            if (!ValidateBridge(out var err)) { Report(err); return; }

            var sb = NewReport();
            var allTags = System.Enum.GetValues(typeof(IdentityTag))
                .Cast<IdentityTag>().Where(t => t != IdentityTag.None).ToArray();

            int totalCount = 0, nonZeroTags = 0;
            foreach (var tag in allTags)
            {
                var nodes = Bridge.FindNodesByIdentity(tag);
                var count = nodes?.Length ?? 0;
                totalCount += count;
                if (count > 0) nonZeroTags++;

                var preview = (count > 0)
                    ? $" (idx={string.Join(",", nodes.Take(5))}{(count > 5 ? "..." : "")})"
                    : "";
                sb.AppendLine($"  {tag}: {count} 개{preview}");
            }

            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"활성 정체성 종류: {nonZeroTags}/{allTags.Length}");
            sb.AppendLine($"총 정체성 인스턴스 (중복 포함): {totalCount}");

            Report(sb.ToString());
        }

        // ════════════════════════════════════════════════════════════════
        // Live T2 — 보스 노드 종합 분석
        // ════════════════════════════════════════════════════════════════

        [ContextMenu("[Live T2] 보스 노드 종합 분석")]
        public void Live_BossNodesComprehensive()
        {
            _lastTestName = "Live T2 — 보스 노드 종합 분석";
            if (!ValidateBridge(out var err)) { Report(err); return; }

            var bossNodes = Bridge.FindNodesByIdentity(IdentityTag.IDENTITY_BOSS);
            var sb = NewReport();

            if (bossNodes == null || bossNodes.Length == 0)
            {
                sb.AppendLine("  보스 노드 없음.");
            }
            else
            {
                sb.AppendLine($"  총 {bossNodes.Length} 개 보스 노드:");
                sb.AppendLine();
                foreach (var idx in bossNodes)
                {
                    var role = Bridge.GetNodeRole(idx);
                    var tags = Bridge.GetTags(idx, ElementKind.Node);
                    sb.AppendLine($"  ─ idx={idx}, NodeRole: {role}");

                    var identityNames = DecodeFlags<IdentityTag>(tags.identityTags);
                    if (identityNames.Count > 0) sb.AppendLine($"    Identity: {string.Join(", ", identityNames)}");

                    var stateNames = DecodeFlags<StateTag>(tags.stateTags);
                    if (stateNames.Count > 0) sb.AppendLine($"    State (Node): {string.Join(", ", stateNames)}");

                    // v2 — Edge 도 검사
                    try
                    {
                        var edgeTags = Bridge.GetTags(idx, ElementKind.Passage);
                        var edgeStates = DecodeFlags<StateTag>(edgeTags.stateTags);
                        if (edgeStates.Count > 0) sb.AppendLine($"    State (Edge): {string.Join(", ", edgeStates)}");
                    }
                    catch { }

                    // v2 — Risk Reflection
                    try
                    {
                        var risk = Bridge.GetRiskScore(idx);
                        sb.AppendLine($"    Risk: {ReflectFields(risk)}");
                    }
                    catch (System.Exception ex) { sb.AppendLine($"    Risk: ({ex.Message})"); }

                    sb.AppendLine();
                }
            }

            Report(sb.ToString());
        }

        // ════════════════════════════════════════════════════════════════
        // Live T3 — 스폰 지점 종합 분석
        // ════════════════════════════════════════════════════════════════

        [ContextMenu("[Live T3] 스폰 지점 종합 분석")]
        public void Live_SpawnNodesComprehensive()
        {
            _lastTestName = "Live T3 — 스폰 지점 종합 분석";
            if (!ValidateBridge(out var err)) { Report(err); return; }

            var spawnNodes = Bridge.FindNodesByIdentity(IdentityTag.IDENTITY_SPAWN);
            var sb = NewReport();

            if (spawnNodes == null || spawnNodes.Length == 0)
            {
                sb.AppendLine("  스폰 노드 없음.");
            }
            else
            {
                sb.AppendLine($"  총 {spawnNodes.Length} 개 스폰 노드:");
                foreach (var idx in spawnNodes)
                {
                    var role = Bridge.GetNodeRole(idx);
                    var tags = Bridge.GetTags(idx, ElementKind.Node);
                    sb.AppendLine($"  ─ idx={idx}, NodeRole: {role}");

                    var identityNames = DecodeFlags<IdentityTag>(tags.identityTags);
                    if (identityNames.Count > 0) sb.AppendLine($"    Identity: {string.Join(", ", identityNames)}");
                }
            }

            Report(sb.ToString());
        }

        // ════════════════════════════════════════════════════════════════
        // Live T4 — 단일 노드 깊은 분석 (Node 만)
        // ════════════════════════════════════════════════════════════════

        [ContextMenu("[Live T4] 단일 노드 깊은 분석 (Node 만)")]
        public void Live_SingleNodeDeep()
        {
            _lastTestName = $"Live T4 — 단일 노드 (idx={_targetNodeIdx}, Node 만)";
            if (!ValidateBridge(out var err)) { Report(err); return; }

            var sb = NewReport();
            try
            {
                var role = Bridge.GetNodeRole(_targetNodeIdx);
                var tags = Bridge.GetTags(_targetNodeIdx, ElementKind.Node);
                var possibleRoles = Bridge.GetPossibleNodeRoles(_targetNodeIdx);

                sb.AppendLine($"  idx: {_targetNodeIdx}");
                sb.AppendLine($"  NodeRole (확정): {role}");
                sb.AppendLine($"  NodeRole (가능): {string.Join(", ", possibleRoles)}");
                sb.AppendLine();
                sb.AppendLine($"  Identity: {DescribeFlags<IdentityTag>(tags.identityTags)}");
                sb.AppendLine($"  State:    {DescribeFlags<StateTag>(tags.stateTags)}");
                sb.AppendLine($"  Event:    {DescribeFlags<EventTag>(tags.eventTags)}");
                sb.AppendLine($"  Potential:{DescribeFlags<PotentialTag>(tags.potentialTags)}");
            }
            catch (System.Exception ex) { sb.AppendLine($"  ★ 예외: {ex.Message}"); }

            Report(sb.ToString());
        }

        // ════════════════════════════════════════════════════════════════
        // Live T5 — Potential Edge 분포
        // ════════════════════════════════════════════════════════════════

        [ContextMenu("[Live T5] Potential Edge 분포")]
        public void Live_PotentialDistribution()
        {
            _lastTestName = "Live T5 — Potential Edge 분포";
            if (!ValidateBridge(out var err)) { Report(err); return; }

            var allTags = System.Enum.GetValues(typeof(PotentialTag))
                .Cast<PotentialTag>().Where(t => t != PotentialTag.None).ToArray();

            var sb = NewReport();
            int totalCount = 0;
            foreach (var tag in allTags)
            {
                var edges = Bridge.FindEdgesByPotential(tag);
                var count = edges?.Length ?? 0;
                totalCount += count;
                var preview = (count > 0)
                    ? $" (idx={string.Join(",", edges.Take(5))}{(count > 5 ? "..." : "")})"
                    : "";
                sb.AppendLine($"  {tag}: {count} 개{preview}");
            }
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"총 Potential Edge 인스턴스: {totalCount}");

            Report(sb.ToString());
        }

        // ════════════════════════════════════════════════════════════════
        // Live T6 — Event 분포
        // ════════════════════════════════════════════════════════════════

        [ContextMenu("[Live T6] Event 분포 (Node)")]
        public void Live_EventDistribution()
        {
            _lastTestName = "Live T6 — Event 분포 (Node)";
            if (!ValidateBridge(out var err)) { Report(err); return; }

            var allTags = System.Enum.GetValues(typeof(EventTag))
                .Cast<EventTag>().Where(t => t != EventTag.None).ToArray();

            var sb = NewReport();
            int totalCount = 0;
            foreach (var tag in allTags)
            {
                var elements = Bridge.FindElementsByEvent(tag, ElementKind.Node);
                var count = elements?.Length ?? 0;
                totalCount += count;
                var preview = (count > 0)
                    ? $" (idx={string.Join(",", elements.Take(5))}{(count > 5 ? "..." : "")})"
                    : "";
                sb.AppendLine($"  {tag}: {count} 개{preview}");
            }
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"총 Event 인스턴스: {totalCount}");

            Report(sb.ToString());
        }

        // ════════════════════════════════════════════════════════════════
        // Live T7 — 보스 + 주변 정체성
        // ════════════════════════════════════════════════════════════════

        [ContextMenu("[Live T7] 보스 + 주변 정체성")]
        public void Live_BossSurroundings()
        {
            _lastTestName = "Live T7 — 보스 + 주변 정체성";
            if (!ValidateBridge(out var err)) { Report(err); return; }

            var sb = NewReport();
            var boss = Bridge.FindNodesByIdentity(IdentityTag.IDENTITY_BOSS);
            var arena = Bridge.FindNodesByIdentity(IdentityTag.IDENTITY_BOSS_ARENA);
            var approach = Bridge.FindNodesByIdentity(IdentityTag.IDENTITY_APPROACH);
            var escape = Bridge.FindNodesByIdentity(IdentityTag.IDENTITY_ESCAPE);

            sb.AppendLine($"  Boss: {boss?.Length ?? 0} 개");
            sb.AppendLine($"  Boss Arena: {arena?.Length ?? 0} 개");
            sb.AppendLine($"  Approach: {approach?.Length ?? 0} 개");
            sb.AppendLine($"  Escape: {escape?.Length ?? 0} 개");
            sb.AppendLine();
            sb.AppendLine($"  ※ 평가:");
            sb.AppendLine($"    Boss + Arena: {(boss?.Length > 0 && arena?.Length > 0 ? "OK" : "★ 한쪽 누락")}");
            sb.AppendLine($"    Approach/Escape: {(approach?.Length > 0 && escape?.Length > 0 ? "OK" : "★ 한쪽 누락")}");

            Report(sb.ToString());
        }

        // ════════════════════════════════════════════════════════════════
        // Live T8 — Junction Lifecycle
        // ════════════════════════════════════════════════════════════════

        [ContextMenu("[Live T8] Junction Lifecycle")]
        public void Live_JunctionLifecycle()
        {
            _lastTestName = "Live T8 — Junction Lifecycle";
            if (!ValidateBridge(out var err)) { Report(err); return; }

            var sb = NewReport();
            try
            {
                var stage = Bridge.GetJunctionStage();
                var distance = Bridge.GetJunctionDistance();
                sb.AppendLine($"  Junction Stage: {stage}");
                sb.AppendLine($"  Junction Distance: {distance:F2}m");
            }
            catch (System.Exception ex) { sb.AppendLine($"  ★ 예외: {ex.Message}"); }

            Report(sb.ToString());
        }

        // ════════════════════════════════════════════════════════════════
        // ★ Live T9 (NEW v2) — Edge Tag 분포 (State Tag 어디 있는지)
        // ════════════════════════════════════════════════════════════════

        [ContextMenu("[Live T9] ★ Edge Tag 분포 (State 어디?)")]
        public void Live_EdgeTagDistribution()
        {
            _lastTestName = $"Live T9 — Edge Tag 분포 (0~{_maxEdgeIdxToProbe})";
            if (!ValidateBridge(out var err)) { Report(err); return; }

            var sb = NewReport();
            sb.AppendLine($"  Edge 인덱스 0~{_maxEdgeIdxToProbe} 까지 검사:");
            sb.AppendLine();

            int probedCount = 0;
            int nonZeroCount = 0;
            var stateCounts = new Dictionary<StateTag, int>();
            var potentialCounts = new Dictionary<PotentialTag, int>();

            for (int i = 0; i <= _maxEdgeIdxToProbe; i++)
            {
                ContextTagSet edgeTags;
                try { edgeTags = Bridge.GetTags(i, ElementKind.Passage); }
                catch { break; }  // 범위 벗어남

                probedCount++;

                if (edgeTags.stateTags != 0 || edgeTags.identityTags != 0 ||
                    edgeTags.eventTags != 0 || edgeTags.potentialTags != 0)
                {
                    nonZeroCount++;

                    foreach (StateTag flag in System.Enum.GetValues(typeof(StateTag)))
                    {
                        if (flag == StateTag.None) continue;
                        if ((edgeTags.stateTags & (uint)flag) != 0)
                            stateCounts[flag] = stateCounts.GetValueOrDefault(flag) + 1;
                    }

                    foreach (PotentialTag flag in System.Enum.GetValues(typeof(PotentialTag)))
                    {
                        if (flag == PotentialTag.None) continue;
                        if ((edgeTags.potentialTags & (uint)flag) != 0)
                            potentialCounts[flag] = potentialCounts.GetValueOrDefault(flag) + 1;
                    }
                }
            }

            sb.AppendLine($"  검사한 Edge 수: {probedCount}");
            sb.AppendLine($"  Tag 있는 Edge 수: {nonZeroCount}");
            sb.AppendLine();

            if (stateCounts.Count > 0)
            {
                sb.AppendLine($"  ── Edge 의 State Tag 분포 ──");
                foreach (var kv in stateCounts.OrderByDescending(x => x.Value))
                    sb.AppendLine($"    {kv.Key}: {kv.Value} 개");
            }
            else
            {
                sb.AppendLine($"  ★ Edge 에 State Tag 모두 0");
            }

            sb.AppendLine();
            if (potentialCounts.Count > 0)
            {
                sb.AppendLine($"  ── Edge 의 Potential Tag 분포 ──");
                foreach (var kv in potentialCounts.OrderByDescending(x => x.Value))
                    sb.AppendLine($"    {kv.Key}: {kv.Value} 개");
            }
            else
            {
                sb.AppendLine($"  ★ Edge 에 Potential Tag 모두 0");
            }

            Report(sb.ToString());
        }

        // ════════════════════════════════════════════════════════════════
        // ★ Live T10 (NEW v2) — RiskScore + PassageProfile Reflection
        // ════════════════════════════════════════════════════════════════

        [ContextMenu("[Live T10] ★ RiskScore + PassageProfile Reflection")]
        public void Live_RiskProfileReflection()
        {
            _lastTestName = $"Live T10 — RiskScore + PassageProfile (idx={_targetNodeIdx})";
            if (!ValidateBridge(out var err)) { Report(err); return; }

            var sb = NewReport();
            sb.AppendLine($"  idx: {_targetNodeIdx}");
            sb.AppendLine();

            try
            {
                var risk = Bridge.GetRiskScore(_targetNodeIdx);
                sb.AppendLine($"  ── RiskScore ──");
                sb.AppendLine(ReflectFields(risk, "    "));
            }
            catch (System.Exception ex) { sb.AppendLine($"  RiskScore 실패: {ex.Message}"); }

            sb.AppendLine();
            try
            {
                var profile = Bridge.GetPassageProfile(_targetNodeIdx);
                sb.AppendLine($"  ── PassageProfile ──");
                sb.AppendLine(ReflectFields(profile, "    "));
            }
            catch (System.Exception ex) { sb.AppendLine($"  PassageProfile 실패: {ex.Message}"); }

            Report(sb.ToString());
        }

        // ════════════════════════════════════════════════════════════════
        // ★ Live T11 (NEW v2) — 노드 통합 (Node + Edge + Risk + Profile)
        // ════════════════════════════════════════════════════════════════

        [ContextMenu("[Live T11] ★ 노드 통합 (Node + Edge + Risk + Profile)")]
        public void Live_NodeFullIntegrated()
        {
            _lastTestName = $"Live T11 — 노드 통합 (idx={_targetNodeIdx})";
            if (!ValidateBridge(out var err)) { Report(err); return; }

            var sb = NewReport();
            sb.AppendLine($"  idx: {_targetNodeIdx}");
            sb.AppendLine();

            try
            {
                // Node
                var nodeRole = Bridge.GetNodeRole(_targetNodeIdx);
                var nodeTags = Bridge.GetTags(_targetNodeIdx, ElementKind.Node);
                sb.AppendLine($"  ── Node ──");
                sb.AppendLine($"    Role: {nodeRole}");
                sb.AppendLine($"    Identity: {DescribeFlags<IdentityTag>(nodeTags.identityTags)}");
                sb.AppendLine($"    State:    {DescribeFlags<StateTag>(nodeTags.stateTags)}");
                sb.AppendLine($"    Event:    {DescribeFlags<EventTag>(nodeTags.eventTags)}");
                sb.AppendLine($"    Potential:{DescribeFlags<PotentialTag>(nodeTags.potentialTags)}");
            }
            catch (System.Exception ex) { sb.AppendLine($"  Node 실패: {ex.Message}"); }

            sb.AppendLine();
            try
            {
                // Edge
                var edgeTags = Bridge.GetTags(_targetNodeIdx, ElementKind.Passage);
                var passageRole = Bridge.GetPassageRole(_targetNodeIdx);
                sb.AppendLine($"  ── Edge ──");
                sb.AppendLine($"    PassageRole: {passageRole}");
                sb.AppendLine($"    Identity: {DescribeFlags<IdentityTag>(edgeTags.identityTags)}");
                sb.AppendLine($"    State:    {DescribeFlags<StateTag>(edgeTags.stateTags)}");
                sb.AppendLine($"    Event:    {DescribeFlags<EventTag>(edgeTags.eventTags)}");
                sb.AppendLine($"    Potential:{DescribeFlags<PotentialTag>(edgeTags.potentialTags)}");
            }
            catch (System.Exception ex) { sb.AppendLine($"  Edge 실패: {ex.Message}"); }

            sb.AppendLine();
            try
            {
                var risk = Bridge.GetRiskScore(_targetNodeIdx);
                sb.AppendLine($"  ── Risk ──");
                sb.AppendLine(ReflectFields(risk, "    "));
            }
            catch (System.Exception ex) { sb.AppendLine($"  Risk 실패: {ex.Message}"); }

            sb.AppendLine();
            try
            {
                var profile = Bridge.GetPassageProfile(_targetNodeIdx);
                sb.AppendLine($"  ── Profile ──");
                sb.AppendLine(ReflectFields(profile, "    "));
            }
            catch (System.Exception ex) { sb.AppendLine($"  Profile 실패: {ex.Message}"); }

            Report(sb.ToString());
        }

        // ════════════════════════════════════════════════════════════════
        // ★ Live T12 (NEW v2) — ScenarioContext 일람
        // ════════════════════════════════════════════════════════════════

        [ContextMenu("[Live T12] ★ ScenarioContext 일람")]
        public void Live_EnumerateContexts()
        {
            _lastTestName = $"Live T12 — ScenarioContext (idx={_targetNodeIdx})";
            if (!ValidateBridge(out var err)) { Report(err); return; }

            var sb = NewReport();
            sb.AppendLine($"  idx: {_targetNodeIdx}");
            sb.AppendLine();

            // Node
            try
            {
                var nodeContexts = Bridge.EnumerateContexts(_targetNodeIdx, ElementKind.Node);
                sb.AppendLine($"  ── Node ScenarioContext: {nodeContexts?.Count ?? 0} 개 ──");
                if (nodeContexts != null)
                    foreach (var ctx in nodeContexts)
                        sb.AppendLine($"    {ReflectFields(ctx, "      ")}");
            }
            catch (System.Exception ex) { sb.AppendLine($"  Node ScenarioContext 실패: {ex.Message}"); }

            sb.AppendLine();

            // Edge
            try
            {
                var edgeContexts = Bridge.EnumerateContexts(_targetNodeIdx, ElementKind.Passage);
                sb.AppendLine($"  ── Edge ScenarioContext: {edgeContexts?.Count ?? 0} 개 ──");
                if (edgeContexts != null)
                    foreach (var ctx in edgeContexts)
                        sb.AppendLine($"    {ReflectFields(ctx, "      ")}");
            }
            catch (System.Exception ex) { sb.AppendLine($"  Edge ScenarioContext 실패: {ex.Message}"); }

            Report(sb.ToString());
        }

        // ════════════════════════════════════════════════════════════════
        // Live T0 — Run All Comprehensive
        // ════════════════════════════════════════════════════════════════

        [ContextMenu("[Live T0] Run All Comprehensive Tests (12 시나리오)")]
        public void Live_RunAllComprehensive()
        {
            Debug.Log($"════ [TerrainDiag v2] Run All ════", this);

            Live_AllIdentityCensus(); Debug.Log("─────");
            Live_BossNodesComprehensive(); Debug.Log("─────");
            Live_SpawnNodesComprehensive(); Debug.Log("─────");
            Live_SingleNodeDeep(); Debug.Log("─────");
            Live_PotentialDistribution(); Debug.Log("─────");
            Live_EventDistribution(); Debug.Log("─────");
            Live_BossSurroundings(); Debug.Log("─────");
            Live_JunctionLifecycle(); Debug.Log("─────");
            Live_EdgeTagDistribution(); Debug.Log("─────");
            Live_RiskProfileReflection(); Debug.Log("─────");
            Live_NodeFullIntegrated(); Debug.Log("─────");
            Live_EnumerateContexts();

            Debug.Log($"════ [TerrainDiag v2] Run All 완료 ════", this);
        }

        // ════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════

        private StringBuilder NewReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"════════ {_lastTestName} ════════");
            return sb;
        }

        private void Report(string content)
        {
            _lastResultSummary = content;
            if (_verboseLogging) Debug.Log(content, this);
        }

        /// <summary>Flag enum 의 활성 비트들을 이름 리스트로 디코딩.</summary>
        private static List<string> DecodeFlags<T>(uint bits) where T : System.Enum
        {
            var result = new List<string>();
            foreach (T flag in System.Enum.GetValues(typeof(T)))
            {
                uint flagValue = System.Convert.ToUInt32(flag);
                if (flagValue == 0) continue;
                if ((bits & flagValue) != 0) result.Add(flag.ToString());
            }
            return result;
        }

        /// <summary>비트 + 디코딩 이름 한 줄 출력.</summary>
        private static string DescribeFlags<T>(uint bits) where T : System.Enum
        {
            var names = DecodeFlags<T>(bits);
            var nameStr = names.Count > 0 ? string.Join(", ", names) : "(None)";
            return $"0x{bits:X8} = {nameStr}";
        }

        /// <summary>★ struct 의 모든 public 필드 + property 출력 (Reflection).</summary>
        private static string ReflectFields(object obj, string indent = "  ")
        {
            if (obj == null) return $"{indent}(null)";

            var type = obj.GetType();
            var sb = new StringBuilder();
            sb.AppendLine($"{indent}{type.Name} {{");

            // Fields
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object value;
                try { value = f.GetValue(obj); } catch { value = "<read failed>"; }
                sb.AppendLine($"{indent}  {f.Name}: {FormatValue(value)}");
            }

            // Properties
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead) continue;
                if (p.GetIndexParameters().Length > 0) continue;

                object value;
                try { value = p.GetValue(obj); } catch { value = "<read failed>"; }
                sb.AppendLine($"{indent}  {p.Name}: {FormatValue(value)}");
            }

            sb.Append($"{indent}}}");
            return sb.ToString();
        }

        private static string FormatValue(object value)
        {
            if (value == null) return "null";

            var type = value.GetType();

            // Primitive / string / enum: ToString
            if (type.IsPrimitive || value is string || type.IsEnum)
                return value.ToString();

            // Vector3 / Vector2 등 — Unity struct
            if (value is Vector3 v3) return $"({v3.x:F2}, {v3.y:F2}, {v3.z:F2})";
            if (value is Vector2 v2) return $"({v2.x:F2}, {v2.y:F2})";

            return value.ToString();
        }
    }
}