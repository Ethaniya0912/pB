#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Gate 5 Phase E-β.8] Playability Auto-Audit
    //
    // 목적:
    //   E-β 구현 후 생성된 cave graph가 7개 플레이어빌리티 불변 조건을 
    //   충족하는지 자동 검증.
    //   
    // 7 불변 조건 (§5 Path Organicity 보고서):
    //   #1 통로 최소 폭 2.0m
    //   #2 바닥 연속성 (Δy/Δxz ≤ tan(15°) = 0.27)
    //   #3 천장 최소 높이 floorY + 2.2m
    //   #4 curvature offset ≤ edge width × 0.5
    //   #5 NavMesh bakable (unreachable < 5%)
    //   #6 방향감 유지 (endTaper=0)
    //   #7 Biome 전환 부드러움
    //
    // 현재 구현:
    //   #1, #2, #4 — graph 레벨에서 측정 (이번 버전)
    //   #3 — shader 계산 값이라 런타임 측정 필요 (추후)
    //   #5 — NavMesh bake 후 측정 (추후)
    //   #6 — 코드 레벨 보장 (EvaluateCurvedTunnel의 c0=startPos, c4=endPos)
    //   #7 — biome boundary 샘플링 (A.6에서 재검증)
    //
    // 사용법:
    //   Window → Cave System → Playability Auditor
    //   "Run Audit" 클릭 → 결과 표시
    // ══════════════════════════════════════════════════════════════════════

    public class PlayabilityAuditorWindow : EditorWindow
    {
        private AuditResult _lastResult;
        private Vector2 _scrollPos;

        [MenuItem("Window/Cave System/Playability Auditor")]
        public static void ShowWindow()
        {
            var w = GetWindow<PlayabilityAuditorWindow>("Playability Auditor");
            w.minSize = new Vector2(500, 400);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("E-β.8 Playability Auto-Audit", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "생성된 cave graph의 플레이어빌리티 불변 조건 7개 중 3개 (graph-level) 검증.\n" +
                "#3, #5는 런타임/NavMesh 필요, #6, #7은 코드 레벨 보장.",
                MessageType.Info);

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Audit", GUILayout.Height(30)))
            {
                _lastResult = RunAudit();
            }

            EditorGUILayout.Space();
            if (_lastResult != null)
            {
                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
                DrawResult(_lastResult);
                EditorGUILayout.EndScrollView();
            }
        }

        // ──────────────────────────────────────────────────────────────
        private class AuditResult
        {
            public int totalEdges;
            public int totalNodes;

            // #1 통로 최소 폭
            public int narrowEdgeCount;     // width < 2.0m
            public float minWidth;

            // #2 바닥 연속성 (slope)
            public int steepEdgeCount;      // slope > 45° (전역 한계)
            public int biomeSteepCount;     // biome maxSlope 초과
            public float maxSlopeDeg;

            // #4 curvature clamp 검증
            public int curvatureClampedCount;  // biome curvatureAmp > width × 0.5 (HLSL에서 clamp됨)

            // [E-α.9] Map size 일관성 체크
            public Vector3 dungeonBounds;
            public Vector3 worldMapSize;
            public float viewDistanceWorld;  // viewDistance × ChunkWorldSize
            public bool mapSizeMismatch;

            public List<string> warnings = new List<string>();
            public List<string> infos = new List<string>();
        }

        private AuditResult RunAudit()
        {
            var r = new AuditResult();

            if (CaveNodeGraphBuilder.Instance == null
                || CaveNodeGraphBuilder.Instance.edgesData == null
                || CaveNodeGraphBuilder.Instance.edgesData.Count == 0)
            {
                r.warnings.Add("CaveNodeGraphBuilder.Instance 또는 edgesData 없음. Play 상태에서 graph 생성 후 Audit 실행.");
                return r;
            }

            var edges = CaveNodeGraphBuilder.Instance.edgesData;
            var nodes = CaveNodeGraphBuilder.Instance.nodesData;
            r.totalEdges = edges.Count;
            r.totalNodes = nodes != null ? nodes.Count : 0;
            r.minWidth = float.MaxValue;
            r.maxSlopeDeg = 0f;

            // biome maxSlope 조회
            float biomeMaxSlope = 45f;
            float biomeCurvatureAmp = 0f;
            if (CaveManager.Instance != null
                && CaveManager.Instance.biomeSettings != null
                && CaveManager.Instance.biomeSettings.globalBiomes != null
                && CaveManager.Instance.biomeSettings.globalBiomes.Count > 0
                && CaveManager.Instance.biomeSettings.globalBiomes[0] != null)
            {
                biomeMaxSlope = CaveManager.Instance.biomeSettings.globalBiomes[0].maxSlope;
                biomeCurvatureAmp = CaveManager.Instance.biomeSettings.globalBiomes[0].curvatureAmp;
            }

            foreach (var e in edges)
            {
                // #1 통로 폭
                if (e.width < 2.0f) r.narrowEdgeCount++;
                if (e.width < r.minWidth) r.minWidth = e.width;

                // #2 slope
                float dy = Mathf.Abs(e.endPos.y - e.startPos.y);
                float dxz = Mathf.Sqrt(
                    (e.endPos.x - e.startPos.x) * (e.endPos.x - e.startPos.x) +
                    (e.endPos.z - e.startPos.z) * (e.endPos.z - e.startPos.z)
                );
                if (dxz > 0.01f)
                {
                    float slopeDeg = Mathf.Atan2(dy, dxz) * Mathf.Rad2Deg;
                    if (slopeDeg > r.maxSlopeDeg) r.maxSlopeDeg = slopeDeg;
                    if (slopeDeg > 45f) r.steepEdgeCount++;
                    if (slopeDeg > biomeMaxSlope) r.biomeSteepCount++;
                }

                // #4 curvature clamp (biome 값이 edge 폭 × 0.5 초과 시 HLSL에서 clamp)
                if (biomeCurvatureAmp > e.width * 0.5f)
                    r.curvatureClampedCount++;
            }

            // 결과 정리
            if (r.narrowEdgeCount > 0)
                r.warnings.Add($"#1 {r.narrowEdgeCount}개 edge의 width < 2.0m (플레이어 통과 어려움)");
            else
                r.infos.Add("#1 모든 edge width ≥ 2.0m ✓");

            if (r.steepEdgeCount > 0)
                r.warnings.Add($"#2 {r.steepEdgeCount}개 edge slope > 45° (NavMesh bake 실패 위험)");
            else
                r.infos.Add("#2 모든 edge slope ≤ 45° ✓");

            if (r.biomeSteepCount > 0)
                r.warnings.Add($"#2 {r.biomeSteepCount}개 edge가 biome.maxSlope ({biomeMaxSlope}°) 초과 (지질 정체성 일탈)");

            if (r.curvatureClampedCount > 0)
                r.infos.Add($"#4 {r.curvatureClampedCount}개 edge에서 curvatureAmp가 width×0.5 초과 → HLSL safeAmp clamp 적용 (자동 보호)");
            else if (biomeCurvatureAmp > 0.01f)
                r.infos.Add("#4 모든 edge의 curvatureAmp가 width × 0.5 이내 — clamp 불필요 ✓");

            r.infos.Add("#6 방향감 유지 — EvaluateCurvedTunnel의 c0=startPos, c4=endPos 강제 (코드 보장) ✓");
            r.infos.Add("#3 천장 최소 높이 — runtime shader 계산. 별도 검증 필요.");
            r.infos.Add("#5 NavMesh bakable — NavMesh bake 후 별도 확인 필요.");
            r.infos.Add("#7 Biome 전환 — A.6 작업 시 재검증 예정.");

            // ─────────────────────────────────────────
            // [Gate 5 Phase E-α.9] Map size 일관성 체크
            //   dungeonBounds (NodeGraphBuilder) vs worldMapSize (CaveTerrainConfig)
            //   vs viewDistance × ChunkWorldSize
            // ─────────────────────────────────────────
            r.dungeonBounds = CaveNodeGraphBuilder.Instance.dungeonBounds;
            
            CaveTerrainConfig terrainCfg = null;
            if (CaveManager.Instance != null && CaveManager.Instance.chunkManager != null)
                terrainCfg = CaveManager.Instance.chunkManager.terrainConfig;
            
            if (terrainCfg != null)
            {
                r.worldMapSize = terrainCfg.worldMapSize;
                float cws = terrainCfg.ChunkWorldSize;
                float vd = terrainCfg.viewDistance;
                r.viewDistanceWorld = (2f * vd + 1f) * cws;  // 전체 view 반경 직경

                // dungeonBounds와 worldMapSize 불일치 체크
                Vector3 diff = r.dungeonBounds - r.worldMapSize;
                if (diff.sqrMagnitude > 0.1f)
                {
                    r.mapSizeMismatch = true;
                    r.warnings.Add($"[E-α.9] dungeonBounds({r.dungeonBounds}) ≠ worldMapSize({r.worldMapSize}). " +
                                   "단일 source 유지 권장 — NodeGraphBuilder.dungeonBounds = terrainConfig.worldMapSize.");
                }
                else
                {
                    r.infos.Add($"[E-α.9] Map size 일관성: dungeonBounds == worldMapSize == {r.worldMapSize} ✓");
                }

                // viewDistance × ChunkWorldSize가 worldMapSize보다 크면 경고
                float viewXZ = r.viewDistanceWorld;
                if (viewXZ > r.worldMapSize.x || viewXZ > r.worldMapSize.z)
                {
                    r.warnings.Add($"[E-α.9] viewDistance 전체 범위 {viewXZ:F0}m > worldMapSize ({r.worldMapSize.x:F0}, {r.worldMapSize.z:F0})m " +
                                   "→ 플레이어가 노드 없는 영역에 진입 가능 (빈 chunk 렌더).");
                }
                else
                {
                    r.infos.Add($"[E-α.9] viewDistance 범위 {viewXZ:F0}m ≤ worldMapSize ✓");
                }
            }
            else
            {
                r.infos.Add("[E-α.9] CaveTerrainConfig 미접근 — map size 체크 skip.");
            }

            return r;
        }

        private void DrawResult(AuditResult r)
        {
            EditorGUILayout.LabelField($"Graph Stats", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"  Edges: {r.totalEdges}");
            EditorGUILayout.LabelField($"  Nodes: {r.totalNodes}");

            if (r.totalEdges > 0)
            {
                EditorGUILayout.LabelField($"  Min edge width: {r.minWidth:F2} m");
                EditorGUILayout.LabelField($"  Max edge slope: {r.maxSlopeDeg:F1}°");
            }

            // [E-α.9] Map size 섹션
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Map Size (E-α.9)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"  dungeonBounds: {r.dungeonBounds}");
            EditorGUILayout.LabelField($"  worldMapSize:  {r.worldMapSize}");
            EditorGUILayout.LabelField($"  viewDistance 전체 범위: {r.viewDistanceWorld:F0} m");

            EditorGUILayout.Space();

            if (r.warnings.Count > 0)
            {
                EditorGUILayout.LabelField("Warnings", EditorStyles.boldLabel);
                foreach (var w in r.warnings)
                    EditorGUILayout.HelpBox(w, MessageType.Warning);
            }

            if (r.infos.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Info", EditorStyles.boldLabel);
                foreach (var i in r.infos)
                    EditorGUILayout.HelpBox(i, MessageType.Info);
            }
        }
    }
}
#endif
