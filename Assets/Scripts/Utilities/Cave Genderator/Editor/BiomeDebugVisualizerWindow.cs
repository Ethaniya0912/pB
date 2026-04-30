#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
// ★ Phase 20 — Tag 4 Category enum + IJunctionContextAnalyzer 사용
// CaveSystem.Editor가 CaveSystem 의 자식 namespace이므로 자동 접근 가능

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Gate 5 Phase A.11 + Phase 4.5-F] BiomeDebugVisualizerWindow v2
    //
    // Runtime cave + graph 정보를 Scene view에 overlay. GraphPreviewWindow와
    // 렌더링 엔진을 공유하여 동일 시각 품질 획득.
    //
    // 구현 전략:
    //   1. CaveManager.Instance / CaveNodeGraphBuilder.Instance 에서 runtime 데이터 획득
    //   2. Runtime data → PreviewInputs + PreviewData 변환 (RebuildPreview)
    //   3. PreviewSceneRenderer.Render() 호출 → 모든 고급 기능 자동 적용
    //
    // 추가되는 기능 (GraphPreviewWindow 이식):
    //   ✓ Biome 경계 contour (colored)
    //   ✓ Biome 라벨 클릭 → 포함 노드 펼침 패널
    //   ✓ Critical Path highlight
    //   ✓ Blend transition zone
    //   ✓ Cross-section view
    //   ✓ Edge direction arrows, violation edges
    //   ✓ Node info panel (클릭 펼침)
    //
    // 유지되는 A.11 특화 기능:
    //   ✓ SculptFlags 벡터 시각화 (A.5)
    //   ✓ Steep edge highlight (E-β.7 maxSlope)
    //   ✓ Chunk wireframe (viewDistance 기반)
    //   ✓ Map bounds (dungeonBounds vs worldMapSize 불일치 경고)
    //
    // byte-identical: Rendering overlay만 — 실제 runtime cave 생성에 영향 없음.
    // ══════════════════════════════════════════════════════════════════════

    public class BiomeDebugVisualizerWindow : EditorWindow
    {
        // ═══ 토글 ═══
        // 기본 렌더링
        private static bool s_showNodes = true;
        private static bool s_showEdges = true;
        private static bool s_showRoomTypeLabels = true;
        private static bool s_showBiomeHeatmap = false;
        private static EdgeDisplayMode s_edgeDisplayMode = EdgeDisplayMode.Line;
        private static float s_edgeMeshSimplifyTolerance = 1.0f;  // [v6] 기본 0.2 → 1.0 (에디터 안정성)
        private static bool s_edgeMeshFilterByGraph = true;  // [v6] 파편/고립 mesh 제거 토글

        // Advanced — GraphPreviewWindow 이식 기능
        private static bool s_showBiomeBoundary = false;
        private static bool s_biomeBoundaryColored = true;
        private static bool s_showBiomeLabels = false;
        private static bool s_showBiomeNodeListPanel = true;
        private static bool s_showBlendTransitionZone = false;
        private static bool s_showCriticalPath = false;
        private static bool s_showEdgeDirection = false;
        private static bool s_showNodeInfoPanel = false;
        private static bool s_showNormalNodeInfo = false;  // [hotfix v3] Normal 노드도 Info Panel

        // [Phase 4.5-G Stage 3-F] Passage Info Panel 상태
        public enum PassageShowMode { ClickToOpen, CriticalAuto, AllOverlay }
        private static bool s_showPassageInfoPanel = false;
        private static PassageShowMode s_passageShowMode = PassageShowMode.ClickToOpen;
        private static bool s_passageRiskFilter = false;  // critical only filter (수동)
        public static int s_selectedPassageIdx = -1;     // 클릭 선택된 edge (PreviewSceneRenderer가 set)
        private static bool s_showRealScale = false;
        private static bool s_showCrossSection = false;
        private static CrossSectionAxis s_crossAxis = CrossSectionAxis.XY;
        private static float s_crossPlaneValue = 0f;

        // A.11 특화 기능
        private static bool s_showSculpt = false;
        private static bool s_highlightSteep = true;
        private static float s_biomeMaxSlope = 45f;
        private static bool s_showChunkWireframe = false;
        private static bool s_showMapBounds = true;

        // Heatmap 설정
        private static float s_heatmapGridSize = 10f;
        private static float s_heatmapOpacity = 0.55f;  // 0.35 → 0.55 (Play 모드에서 더 선명)
        private static float s_heatmapYPlane = 0f;
        private static bool s_showBlendWeightValue = false;  // [hotfix v7] blend 숫자 표시

        // ═══════════════════════════════════════════════════════════════════
        // [Phase 4.5-G P2] γ-aware 시각화 토글 — δ/ε State에서 의미 있음
        // ═══════════════════════════════════════════════════════════════════
        private static bool s_showCentralityHeatmap = false;     // 노드 centrality 색상
        private static bool s_showPlainRiskEdges = false;         // plain passage 위험 edges 강조
        private static bool s_showGammaVisualAmp = false;          // γ Visual Amp Passage Info에 표시
        private static bool s_showVoronoiCellGrid = false;         // Case 1 Voronoi cell 격자 overlay

        // ═══════════════════════════════════════════════════════════════════
        // [Phase 4.5-G η DebugAware Phase 3] Predictive Visualization 토글
        //   η/ε State에서만 의미 있음
        // ═══════════════════════════════════════════════════════════════════
        private static bool s_showPredictiveOverlay = false;       // segment effective width sphere
        private static bool s_showPredBelowMargin = false;          // BelowMargin segment 적색 강조
        private static bool s_showSingleSourceState = false;        // segment별 typeA/B/blank 색상
        private static bool s_ssStateColorByEffective = false;      // ★ η Fix: SS cube 색상을 effective biome 기반으로 (default mode 유지)

        // Runtime 상태 추적 (변경 감지용)
        private int _lastNodeCount = -1;
        private int _lastEdgeCount = -1;
        private bool _lastIsPlaying = false;
        private double _lastRebuildTime = 0;

        // Preview 데이터 (PreviewSceneRenderer에 전달)
        private PreviewInputs _inputs = new PreviewInputs();
        private PreviewData _data = new PreviewData();

        // UI 스크롤
        private Vector2 _scroll;

        [MenuItem("Window/Cave System/Biome Debug Visualizer")]
        public static void ShowWindow()
        {
            var w = GetWindow<BiomeDebugVisualizerWindow>("Biome Debug");
            w.minSize = new Vector2(340, 480);
        }

        private void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;
        private void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

        // ══════════════════════════════════════════════════════════════════
        // OnGUI — Editor Window UI
        // ══════════════════════════════════════════════════════════════════
        private void OnGUI()
        {
            EditorGUILayout.LabelField("A.11 Biome Debug Visualizer (v2)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Runtime cave의 graph + biome 정보를 Scene view에 overlay.\n" +
                "GraphPreviewWindow와 동일한 렌더링 엔진 사용. Play 또는 NodeGraph 생성 후 동작.",
                MessageType.Info);

            // Runtime 상태 표시
            bool isReady = CaveNodeGraphBuilder.Instance != null
                && CaveNodeGraphBuilder.Instance.nodesData != null
                && CaveNodeGraphBuilder.Instance.nodesData.Count > 0;
            if (!isReady)
            {
                EditorGUILayout.HelpBox(
                    "⚠ CaveNodeGraphBuilder.Instance 또는 nodesData 없음.\n" +
                    "Play mode 진입 또는 CaveManager.StartLobbyPregeneration 호출 필요.",
                    MessageType.Warning);
            }
            else
            {
                int nc = CaveNodeGraphBuilder.Instance.nodesData.Count;
                int ec = CaveNodeGraphBuilder.Instance.edgesData != null
                    ? CaveNodeGraphBuilder.Instance.edgesData.Count : 0;
                EditorGUILayout.HelpBox($"✓ Graph 감지: {nc} nodes, {ec} edges", MessageType.None);

                // [hotfix v5] Biome 배분 상태 실시간 표시
                if (_data != null && _data.biomeSamples != null && _data.biomeSamples.Count > 0)
                {
                    var biomeCounts = new Dictionary<int, int>();
                    foreach (var samp in _data.biomeSamples)
                    {
                        int dominantIdx = samp.blendWeight < 0.5f ? samp.biomeIdxA : samp.biomeIdxB;
                        biomeCounts[dominantIdx] = biomeCounts.GetValueOrDefault(dominantIdx) + 1;
                    }
                    string distStr = "";
                    foreach (var kv in biomeCounts)
                    {
                        string name = (kv.Key < _inputs.biomes.Count && _inputs.biomes[kv.Key] != null)
                            ? BiomeSampler.GetBiomeName(_inputs.biomes[kv.Key].noiseType)
                            : $"idx{kv.Key}";
                        float pct = 100f * kv.Value / _data.biomeSamples.Count;
                        distStr += $"  {name}={kv.Value}셀 ({pct:F0}%)\n";
                    }
                    EditorGUILayout.HelpBox(
                        $"Biome 배분 ({_data.biomeSamples.Count}셀, macroScale={_inputs.macroBiomeScale:F0}):\n" + distStr +
                        $"( NoiseMode: {BiomeSampler.CurrentNoiseMode} — " +
                        (BiomeSampler.CurrentNoiseMode == BiomeSampler.NoiseMode.ShaderCompat
                            ? "runtime과 일치 ✓"
                            : "runtime과 불일치 ⚠") + " )",
                        MessageType.None);
                }
                else if (_inputs.biomes.Count == 0)
                {
                    EditorGUILayout.HelpBox("⚠ biomes 리스트 0 — CaveManager.biomeSettings 확인 필요", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox($"biomes={_inputs.biomes.Count}개 로드됨, sample 대기...", MessageType.None);
                }

                // ─────────────────────────────────────────────────────────
                // [Phase 4.5-G Stage 2] Supervisor Report UI
                // ─────────────────────────────────────────────────────────
                DrawSupervisorReport();

                // ═════════════════════════════════════════════════════════
                // [Phase 4.5-G P2] γ State Diagnostics — 5-state + γ 메트릭
                //   현재 BiomeSyncMode 표시 + 활성 γ 토글 + Plain Risk 통계
                //   δ/ε에서 RunAfterf3MergeHook가 채운 lastPassageProfiles 활용
                // ═════════════════════════════════════════════════════════
                DrawGammaStateDiagnostics();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // ── 기본 렌더링 ─────────────────────────────────────
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("▼ Basic Rendering", EditorStyles.boldLabel);
            s_showNodes = EditorGUILayout.ToggleLeft("Show Nodes (roomType 색상)", s_showNodes);
            s_showEdges = EditorGUILayout.ToggleLeft("Show Edges (curvatureAmp 두께)", s_showEdges);
            if (s_showEdges)
            {
                EditorGUI.indentLevel++;
                s_edgeDisplayMode = (EdgeDisplayMode)EditorGUILayout.EnumPopup(
                    "└ Display Mode", s_edgeDisplayMode);
                if (s_edgeDisplayMode == EdgeDisplayMode.MeshOutline)
                {
                    // [v6] Graph 근접 필터 토글 (파편 제거)
                    bool newFilter = EditorGUILayout.ToggleLeft(
                        "└ Filter by Graph Proximity (파편/고립 mesh 제거)",
                        s_edgeMeshFilterByGraph);
                    if (newFilter != s_edgeMeshFilterByGraph)
                    {
                        s_edgeMeshFilterByGraph = newFilter;
                        _lastNodeCount = -1;
                        EdgeMeshOutlineExtractor.ForceRebuild(s_edgeMeshSimplifyTolerance,
                            s_edgeMeshFilterByGraph,
                            CaveNodeGraphBuilder.Instance?.nodesData,
                            CaveNodeGraphBuilder.Instance?.edgesData);
                    }

                    // [v6] Tolerance 범위 확장: 0.05 ~ 5.0m (기본 1.0)
                    float newTol = EditorGUILayout.Slider(
                        "└ Simplify Tolerance (m)", s_edgeMeshSimplifyTolerance, 0.05f, 5.0f);
                    if (!Mathf.Approximately(newTol, s_edgeMeshSimplifyTolerance))
                    {
                        s_edgeMeshSimplifyTolerance = newTol;
                        EdgeMeshOutlineExtractor.ForceRebuild(s_edgeMeshSimplifyTolerance,
                            s_edgeMeshFilterByGraph,
                            CaveNodeGraphBuilder.Instance?.nodesData,
                            CaveNodeGraphBuilder.Instance?.edgesData);
                    }

                    EditorGUILayout.HelpBox(
                        "높이면 Editor 성능↑ outline 간결↓\n" +
                        "낮추면 세밀한 outline, 에디터 부하↑ (0.2 이하는 주의)",
                        MessageType.None);

                    if (GUILayout.Button("└ Force Rebuild Mesh Outline"))
                    {
                        EdgeMeshOutlineExtractor.ForceRebuild(s_edgeMeshSimplifyTolerance,
                            s_edgeMeshFilterByGraph,
                            CaveNodeGraphBuilder.Instance?.nodesData,
                            CaveNodeGraphBuilder.Instance?.edgesData);
                    }
                }
                EditorGUI.indentLevel--;
            }
            s_showRoomTypeLabels = EditorGUILayout.ToggleLeft("Show RoomType Labels", s_showRoomTypeLabels);
            s_showRealScale = EditorGUILayout.ToggleLeft("Real Scale (실제 radius wireframe disc)", s_showRealScale);

            // ── Biome Heatmap + 경계 ──────────────────────────
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("▼ Biome Visualization", EditorStyles.boldLabel);
            s_showBiomeHeatmap = EditorGUILayout.ToggleLeft("Show Biome Heatmap (shader와 일치)", s_showBiomeHeatmap);
            if (s_showBiomeHeatmap)
            {
                EditorGUI.indentLevel++;
                float newGridSize = EditorGUILayout.Slider("Grid Size (m)", s_heatmapGridSize, 2f, 50f);
                if (!Mathf.Approximately(newGridSize, s_heatmapGridSize))
                {
                    s_heatmapGridSize = newGridSize;
                    _lastNodeCount = -1;  // Heatmap 재샘플링 강제
                }
                s_heatmapOpacity = EditorGUILayout.Slider("Opacity", s_heatmapOpacity, 0.1f, 1.0f);
                s_heatmapYPlane = EditorGUILayout.Slider("Y Plane (m)", s_heatmapYPlane, -25f, 25f);

                // [hotfix v7] Show Blend Weight Value — GraphPreview에서 이식
                s_showBlendWeightValue = EditorGUILayout.ToggleLeft(
                    "└ Show Blend Weight Value (전이 cell에 숫자 표시)", s_showBlendWeightValue);

                EditorGUI.indentLevel--;
            }

            s_showBiomeBoundary = EditorGUILayout.ToggleLeft("Show Biome Boundary (contour)", s_showBiomeBoundary);
            if (s_showBiomeBoundary)
            {
                EditorGUI.indentLevel++;
                s_biomeBoundaryColored = EditorGUILayout.ToggleLeft(
                    "└ Colored by Biome (색 혼합)", s_biomeBoundaryColored);
                s_showBlendTransitionZone = EditorGUILayout.ToggleLeft(
                    "└ Show Blend Transition Zone", s_showBlendTransitionZone);
                EditorGUI.indentLevel--;
            }

            s_showBiomeLabels = EditorGUILayout.ToggleLeft("Show Biome Name Labels", s_showBiomeLabels);
            if (s_showBiomeLabels)
            {
                EditorGUI.indentLevel++;
                s_showBiomeNodeListPanel = EditorGUILayout.ToggleLeft(
                    "└ Clickable (클릭 시 포함 노드 리스트)", s_showBiomeNodeListPanel);
                EditorGUI.indentLevel--;
            }

            // [hotfix v5] Noise 모드 3종 선택 — ShaderCompat가 기본 (runtime 일치)
            EditorGUILayout.Space(4);
            BiomeSampler.NoiseMode origMode = BiomeSampler.CurrentNoiseMode;
            var newMode = (BiomeSampler.NoiseMode)EditorGUILayout.EnumPopup(
                "Noise Mode (shader 일치용)", BiomeSampler.CurrentNoiseMode);
            if (newMode != origMode)
            {
                BiomeSampler.CurrentNoiseMode = newMode;
                _lastNodeCount = -1;  // Heatmap 재샘플링 강제
            }
            EditorGUILayout.HelpBox(
                "ShaderCompat = Runtime shader의 snoise2D와 100% 일치 (권장).\n" +
                "Simplex = 표준 Stefan Gustavson (shader와 hash 다름).\n" +
                "Perlin = Mathf.PerlinNoise 근사 (가장 부정확).",
                MessageType.None);

            // ── Advanced — 이식 기능 ──────────────────────────
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("▼ Advanced (GraphPreviewWindow 이식)", EditorStyles.boldLabel);
            s_showCriticalPath = EditorGUILayout.ToggleLeft(
                "Show Critical Path (Spawn→Boss 노랑 경로)", s_showCriticalPath);
            s_showEdgeDirection = EditorGUILayout.ToggleLeft(
                "Show Edge Direction Arrows", s_showEdgeDirection);
            s_showNodeInfoPanel = EditorGUILayout.ToggleLeft(
                "Show Node Info Panel (클릭 펼침)", s_showNodeInfoPanel);
            if (s_showNodeInfoPanel)
            {
                EditorGUI.indentLevel++;
                s_showNormalNodeInfo = EditorGUILayout.ToggleLeft(
                    "└ Include Normal Rooms (일반 노드도 패널 표시)", s_showNormalNodeInfo);
                EditorGUI.indentLevel--;
            }

            // ── [Phase 4.5-G Stage 3-F] Passage Info Panel ──────────────
            s_showPassageInfoPanel = EditorGUILayout.ToggleLeft(
                "Show Passage Info Panel (Stage 3-F)", s_showPassageInfoPanel);
            if (s_showPassageInfoPanel)
            {
                EditorGUI.indentLevel++;
                s_passageShowMode = (PassageShowMode)EditorGUILayout.EnumPopup(
                    "└ Show Mode", s_passageShowMode);
                s_passageRiskFilter = EditorGUILayout.ToggleLeft(
                    "└ Critical only (filter)", s_passageRiskFilter);

                // ClickToOpen 모드 — 안내 + 선택 표시
                if (s_passageShowMode == PassageShowMode.ClickToOpen)
                {
                    EditorGUILayout.HelpBox(
                        "Alt + 좌클릭으로 Scene에서 passage 선택", MessageType.Info);
                    EditorGUILayout.LabelField($"└ Selected: " +
                        (s_selectedPassageIdx >= 0 ? $"#{s_selectedPassageIdx}" : "(none)"));

                    if (GUILayout.Button("Clear Selection"))
                    {
                        s_selectedPassageIdx = -1;
                        SceneView.RepaintAll();
                    }
                }

                // Risk aggregate 표시
                if (CaveNodeGraphBuilder.Instance != null)
                {
                    var agg = CaveNodeGraphBuilder.Instance.lastRiskAggregate;
                    if (agg.totalEdges > 0)
                    {
                        EditorGUILayout.LabelField($"└ Risk: " +
                            $"<color=#3FB950>Safe={agg.safeCount}</color> " +
                            $"<color=#D29922>Caution={agg.cautionCount}</color> " +
                            $"<color=#F85149>Critical={agg.criticalCount}</color>",
                            new GUIStyle(EditorStyles.miniLabel) { richText = true });
                    }
                }

                EditorGUI.indentLevel--;
            }

            s_showCrossSection = EditorGUILayout.ToggleLeft(
                "Show Cross-Section (수직 단면)", s_showCrossSection);
            if (s_showCrossSection)
            {
                EditorGUI.indentLevel++;
                s_crossAxis = (CrossSectionAxis)EditorGUILayout.EnumPopup("Axis", s_crossAxis);
                if (s_crossAxis != CrossSectionAxis.None)
                {
                    Vector3 bounds = GetRuntimeDungeonBounds();
                    float range = s_crossAxis == CrossSectionAxis.XY
                        ? bounds.z * 0.5f : bounds.x * 0.5f;
                    s_crossPlaneValue = EditorGUILayout.Slider(
                        s_crossAxis == CrossSectionAxis.XY ? "Z value" : "X value",
                        s_crossPlaneValue, -range, range);
                }
                EditorGUI.indentLevel--;
            }

            // ── A.11 특화 기능 ────────────────────────────────
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("▼ A.11 Runtime Tools", EditorStyles.boldLabel);
            s_showSculpt = EditorGUILayout.ToggleLeft(
                "Show SculptFlags 벡터 (A.5, 노드 편향)", s_showSculpt);
            s_highlightSteep = EditorGUILayout.ToggleLeft(
                "Highlight Steep Edges (E-β.7)", s_highlightSteep);
            if (s_highlightSteep)
            {
                EditorGUI.indentLevel++;
                float newSlope = EditorGUILayout.Slider(
                    "Max Slope Threshold (°)", s_biomeMaxSlope, 5f, 90f);
                if (!Mathf.Approximately(newSlope, s_biomeMaxSlope))
                {
                    s_biomeMaxSlope = newSlope;
                    _lastNodeCount = -1;  // Violation edges 재계산
                }
                EditorGUI.indentLevel--;
            }

            s_showMapBounds = EditorGUILayout.ToggleLeft(
                "Show Map Bounds (dungeonBounds vs worldMapSize)", s_showMapBounds);
            s_showChunkWireframe = EditorGUILayout.ToggleLeft(
                "Show Chunk Wireframe (viewDistance)", s_showChunkWireframe);

            // ═══════════════════════════════════════════════════════════════
            // [Phase 16] ★ Junction v4 5-Axis Visualizer (★ Tier 2 핵심)
            //
            //   ★ 사용자 단층 본질 검증 도구:
            //     - L1 Node Roles (Spawn/Boss/Treasure/Hub/Normal)
            //     - L2 Passage Types (Narrow/Standard/Wide/Grand + Bridge/Spiral 등)
            //     - L3 Region (★ Phase 26에서 활성, 현재는 Biome substrate 분리 표시)
            //     - L4 Crossover (★ Phase 25에서 활성)
            //     - L5 Tag 4 Category (★ Phase 21에서 활성, 현재는 기존 6 tag 표시)
            //
            //   D2 보장: Editor only — 빌드 영향 없음
            // ═══════════════════════════════════════════════════════════════
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("▼ ★ Junction v4 5-Axis Visualizer (Phase 16)", EditorStyles.boldLabel);
            DrawJunctionV4Panel();

            // ── Legend ────────────────────────────────────────
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("▼ Legend", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Nodes: 🟢 Spawn, 🔴 Boss, 🟡 Treasure, 🔵 Sinkhole, ⚪ Normal",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Biomes: Karst=갈색, Columnar=파랑, Sedimentary=노랑, Colonnade=흰색, Rocky=회색, Canyon=주황, Marine=청록",
                EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();

            // 토글 변경 시 Scene 재렌더
            if (GUI.changed) SceneView.RepaintAll();
        }

        // ══════════════════════════════════════════════════════════════════
        // OnSceneGUI — Scene 렌더
        // ══════════════════════════════════════════════════════════════════
        private void OnSceneGUI(SceneView sv)
        {
            if (CaveNodeGraphBuilder.Instance == null) return;
            var rtNodes = CaveNodeGraphBuilder.Instance.nodesData;
            var rtEdges = CaveNodeGraphBuilder.Instance.edgesData;
            if (rtNodes == null || rtEdges == null) return;
            if (rtNodes.Count == 0) return;

            // [Phase 4.5-F hotfix v3] Play mode 진입/종료 시 heatmap이 갱신 안 되는 문제 방지
            //   1. Play/Edit 모드 전환 시 강제 재샘플링
            //   2. Play mode에서 2초마다 주기적 갱신 (SO 변경 감지용)
            bool isPlaying = EditorApplication.isPlaying;
            bool forceRebuild = false;
            if (isPlaying != _lastIsPlaying)
            {
                forceRebuild = true;
                _lastIsPlaying = isPlaying;
            }
            double now = EditorApplication.timeSinceStartup;
            if (isPlaying && now - _lastRebuildTime > 2.0)
            {
                forceRebuild = true;
            }

            // 1. Runtime 데이터 → PreviewInputs/Data 변환 (변경 시에만)
            if (forceRebuild || rtNodes.Count != _lastNodeCount || rtEdges.Count != _lastEdgeCount)
            {
                RebuildPreview(rtNodes, rtEdges);
                _lastNodeCount = rtNodes.Count;
                _lastEdgeCount = rtEdges.Count;
                _lastRebuildTime = now;
            }

            // 2. 토글을 PreviewInputs에 반영
            SyncInputsFromToggles();

            // 3. PreviewSceneRenderer 호출 — GraphPreviewWindow와 동일 품질
            PreviewSceneRenderer.Render(_data, _inputs, baseline: null, extensions: null);

            // 4. A.11 특화 기능 (Renderer에 포함 안 된 것만 추가 overlay)
            DrawA11Overlays(rtNodes, rtEdges);

            // 5. [Phase 4.5-G Stage 3-F] Edge 클릭 감지 (Passage Info Panel)
            if (s_showPassageInfoPanel && s_passageShowMode == (int)PassageShowMode.ClickToOpen)
            {
                Event e = Event.current;
                // Alt + 좌클릭으로 edge 선택 (일반 클릭은 Unity Scene 조작과 충돌 회피)
                if (e.type == EventType.MouseDown && e.button == 0 && e.alt)
                {
                    Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                    int idx = PreviewSceneRenderer.FindNearestPassageAt(
                        ray.origin, ray.direction, maxDistance: 5f);
                    if (idx >= 0)
                    {
                        s_selectedPassageIdx = idx;
                        e.Use();
                        SceneView.RepaintAll();
                    }
                }
            }

            // ══════════════════════════════════════════════════════════════
            // [Phase 4.5-G P2.D] γ 시각화 토글 4개 OnSceneGUI 렌더링
            //   activated only when γ/δ/ε State + 토글 ON
            // ══════════════════════════════════════════════════════════════
            DrawGammaVisualizations(rtNodes, rtEdges);
        }

        // ══════════════════════════════════════════════════════════════════
        // [Phase 4.5-G P2.D] γ 시각화 4개 — Centrality Heatmap / Plain Risk
        //   Edges / Voronoi Cell Grid / γ Visual Amp
        // ══════════════════════════════════════════════════════════════════
        private void DrawGammaVisualizations(List<NodeData> rtNodes, List<EdgeData> rtEdges)
        {
            var mgr = CaveSystem.CaveManager.Instance;
            if (mgr == null || !mgr.IsSingleSourceActive) return;
            var bs = mgr.biomeSettings;
            if (bs == null) return;

            // ── 1. Centrality Heatmap — 각 노드 색상 (0=녹, 1=적) ──────────
            if (s_showCentralityHeatmap && rtNodes != null)
            {
                Color origColor = Handles.color;
                foreach (var n in rtNodes)
                {
                    // PassageMetric.SampleBoundaryProximity = GetBlendCentralityAt 동등
                    float centrality = PassageMetric.SampleBoundaryProximity(n.position, bs);
                    centrality = Mathf.Clamp01(centrality);
                    // 색상: 0.0=초록 (안전) → 1.0=빨강 (blend zone 위험)
                    Color heatColor = Color.Lerp(
                        new Color(0.2f, 0.9f, 0.3f, 0.85f),  // 안전 녹색
                        new Color(1.0f, 0.2f, 0.2f, 0.95f),  // 위험 적색
                        centrality);
                    Handles.color = heatColor;
                    float r = Mathf.Max(n.radius * 0.45f, 1.5f);
                    Handles.SphereHandleCap(0, n.position + Vector3.up * 0.3f,
                        Quaternion.identity, r, EventType.Repaint);

                    // 임계 초과 노드는 라벨로 수치 표시
                    if (centrality > 0.5f)
                    {
                        Handles.Label(n.position + Vector3.up * (n.radius + 1.5f),
                            $"C={centrality:F2}",
                            new GUIStyle(EditorStyles.boldLabel)
                            {
                                normal = { textColor = heatColor },
                                fontSize = 10
                            });
                    }
                }
                Handles.color = origColor;
            }

            // ── 2. Plain Risk Edges — 위험 edges 굵은 적색 ──────────────────
            //   [P2.E] RiskScore.isPlainRisk + plainPassageRisk 직접 사용 (segment-based 정확)
            if (s_showPlainRiskEdges && CaveNodeGraphBuilder.Instance != null)
            {
                var profiles = CaveNodeGraphBuilder.Instance.lastPassageProfiles;
                if (profiles != null && profiles.Count > 0)
                {
                    Color origColor = Handles.color;
                    Color riskCriticalColor = new Color(1.0f, 0.15f, 0.15f, 0.95f);
                    Color riskModColor = new Color(1.0f, 0.55f, 0.1f, 0.85f);

                    foreach (var p in profiles)
                    {
                        // [P2.E] plainPassageRisk 기반 (segment-based 정확)
                        //   ≥ 0.6: Critical (isPlainRisk=true)
                        //   ≥ 0.3: Moderate (강조 표시)
                        bool isCritical = p.risk.isPlainRisk;       // ≥ 0.6
                        bool isModerate = !isCritical && p.risk.plainPassageRisk >= 0.3f;
                        if (!isCritical && !isModerate) continue;

                        Handles.color = isCritical ? riskCriticalColor : riskModColor;
                        float thickness = isCritical ? 8f : 5f;
                        Handles.DrawAAPolyLine(thickness, p.startPos, p.endPos);

                        // 라벨 — plainPassageRisk 수치 표시 (정확)
                        Vector3 mid = (p.startPos + p.endPos) * 0.5f;
                        string label = isCritical
                            ? $"★ Plain Risk {p.risk.plainPassageRisk:F2}"
                            : $"Plain Risk {p.risk.plainPassageRisk:F2}";
                        Handles.Label(mid + Vector3.up * 2f, label,
                            new GUIStyle(EditorStyles.boldLabel)
                            {
                                normal = { textColor = Handles.color },
                                fontSize = 11
                            });
                    }
                    Handles.color = origColor;
                }
            }

            // ── 3. Voronoi Cell Grid — Case 1 (Columnar) 영역에 cellSize=2.5m ─
            if (s_showVoronoiCellGrid && rtNodes != null && rtEdges != null)
            {
                Color origColor = Handles.color;
                Color gridColor = new Color(0.4f, 0.85f, 1.0f, 0.35f);  // 시안 반투명
                Handles.color = gridColor;

                const float CELL_SIZE = 2.5f;
                const int GRID_RADIUS = 5;  // 노드 주변 ±12.5m

                // Case 1 (Columnar) biome idx — bs.globalBiomes에서 noiseType=Columnar 검색
                int columnarIdx = -1;
                if (bs.globalBiomes != null)
                {
                    for (int i = 0; i < bs.globalBiomes.Count; i++)
                    {
                        if (bs.globalBiomes[i] != null
                            && bs.globalBiomes[i].noiseType == 1)  // BiomeNoiseType.Columnar = 1
                        {
                            columnarIdx = i;
                            break;
                        }
                    }
                }

                if (columnarIdx >= 0)
                {
                    foreach (var n in rtNodes)
                    {
                        // 노드 위치 biome 검사
                        int nodeBiome = PassageMetric.SampleBiomeIdx(n.position, bs);
                        if (nodeBiome != columnarIdx) continue;

                        // 노드 주변 격자 그리기 (XZ 평면, Y=node.y)
                        Vector3 origin = n.position;
                        for (int gx = -GRID_RADIUS; gx <= GRID_RADIUS; gx++)
                        {
                            float x = origin.x + gx * CELL_SIZE;
                            Handles.DrawLine(
                                new Vector3(x, origin.y, origin.z - GRID_RADIUS * CELL_SIZE),
                                new Vector3(x, origin.y, origin.z + GRID_RADIUS * CELL_SIZE));
                        }
                        for (int gz = -GRID_RADIUS; gz <= GRID_RADIUS; gz++)
                        {
                            float z = origin.z + gz * CELL_SIZE;
                            Handles.DrawLine(
                                new Vector3(origin.x - GRID_RADIUS * CELL_SIZE, origin.y, z),
                                new Vector3(origin.x + GRID_RADIUS * CELL_SIZE, origin.y, z));
                        }
                    }
                }
                Handles.color = origColor;
            }

            // ── 4. γ Visual Amp — Passage Info 선택 시 amp 곡선 표시 ────────
            //   Stage 3-F의 PassageInfoPanel이 이미 segment graph를 그리므로
            //   여기서는 선택된 edge의 worldPos 위에 γ amp 값만 부가 표시
            if (s_showGammaVisualAmp
                && s_selectedPassageIdx >= 0
                && CaveNodeGraphBuilder.Instance != null)
            {
                var profiles = CaveNodeGraphBuilder.Instance.lastPassageProfiles;
                if (profiles != null && s_selectedPassageIdx < profiles.Count)
                {
                    var profile = profiles[s_selectedPassageIdx];
                    var dispatcher = mgr.computeDispatcher;
                    float fadePower = (dispatcher != null) ? dispatcher.singleSourceFadePower : 2.0f;

                    if (profile.segments != null && profile.segments.Length > 0)
                    {
                        Color origColor = Handles.color;
                        for (int s = 0; s < profile.segments.Length; s++)
                        {
                            var seg = profile.segments[s];
                            // γ Visual Amp 공식: pow(1 - centrality, fadePower)
                            float amp = Mathf.Pow(1.0f - seg.blendCentrality, fadePower);
                            // 색상: amp 1.0 (활성)=cyan → amp 0.0 (plain)=gray
                            Color ampColor = Color.Lerp(
                                new Color(0.5f, 0.5f, 0.5f, 0.85f),
                                new Color(0.3f, 0.95f, 1.0f, 0.95f),
                                amp);
                            Handles.color = ampColor;
                            // 작은 sphere로 표시
                            Handles.SphereHandleCap(0,
                                seg.worldPos + Vector3.up * 1.0f,
                                Quaternion.identity, 0.4f, EventType.Repaint);
                            // 처음/중간/마지막 segment에만 수치 표시
                            if (s == 0 || s == profile.segments.Length - 1
                                || s == profile.segments.Length / 2)
                            {
                                Handles.Label(seg.worldPos + Vector3.up * 1.6f,
                                    $"amp={amp * 100f:F0}%",
                                    new GUIStyle(EditorStyles.miniLabel)
                                    {
                                        normal = { textColor = ampColor },
                                        fontSize = 9
                                    });
                            }
                        }
                        Handles.color = origColor;
                    }
                }
            }

            // ═══════════════════════════════════════════════════════════════
            // [Phase 4.5-G η DebugAware Phase 3] Predictive Overlay 렌더링
            //
            //   η DebugAware OR ε FullMerge state에서만 의미 있음.
            //   토글 OFF 시 렌더링 skip → 비용 ZERO.
            //
            //   3 토글:
            //     1) s_showPredictiveOverlay — 모든 segment effective width sphere
            //        색상: 안전(녹) → 주의(황) → 위험(적) 보간
            //     2) s_showPredBelowMargin — Below Safe Margin segment만 적색 큰 sphere
            //     3) s_showSingleSourceState — typeA=청, typeB=황, blank=회, dual=skip
            // ═══════════════════════════════════════════════════════════════
            bool anyPredOverlay = s_showPredictiveOverlay
                                || s_showPredBelowMargin
                                || s_showSingleSourceState;
            if (anyPredOverlay
                && mgr.IsDebugAwareOrLater
                && CaveNodeGraphBuilder.Instance != null)
            {
                var profiles = CaveNodeGraphBuilder.Instance.lastPassageProfiles;
                if (profiles != null)
                {
                    Color origColor = Handles.color;
                    const float MIN_SAFE_WIDTH = 2.0f;  // PassageMetric default

                    // ★ Q2: BelowMargin sphere 클릭 시 segment 선택 처리 위해
                    //   passage 인덱스 + segment 인덱스 추적 필요 → for 인덱스 루프
                    Event evt = Event.current;
                    SceneView sceneView = SceneView.currentDrawingSceneView;
                    Camera cam = sceneView != null ? sceneView.camera : null;

                    for (int pi = 0; pi < profiles.Count; pi++)
                    {
                        var profile = profiles[pi];
                        if (profile.segments == null) continue;
                        for (int si = 0; si < profile.segments.Length; si++)
                        {
                            var seg = profile.segments[si];

                            // ── (1) Predictive Effective Width ─────────────
                            //   sphere 색상: effective width가 작을수록 적색
                            //   sphere 크기: effective width 자체 (반지름)
                            if (s_showPredictiveOverlay)
                            {
                                // 위험도 [0, 1] — 작을수록 위험
                                //   effective < MIN_SAFE → 1.0 (적색)
                                //   effective > MIN_SAFE × 3 → 0.0 (녹색)
                                float danger = Mathf.Clamp01(
                                    1f - (seg.predEffectiveWidth - MIN_SAFE_WIDTH)
                                       / (MIN_SAFE_WIDTH * 2f));
                                Color predColor = Color.Lerp(
                                    new Color(0.3f, 0.95f, 0.4f, 0.45f),  // 녹 (안전)
                                    new Color(0.95f, 0.3f, 0.3f, 0.55f),  // 적 (위험)
                                    danger);
                                Handles.color = predColor;
                                // sphere 반지름 = effective width / 2 (시각적으로 통로 폭 표현)
                                float radius = Mathf.Max(seg.predEffectiveWidth * 0.5f, 0.3f);
                                Handles.SphereHandleCap(0, seg.worldPos,
                                    Quaternion.identity, radius * 0.6f, EventType.Repaint);
                            }

                            // ── (2) Below Safe Margin 강조 + ★ Q2 클릭 처리 ──
                            //   적색 큰 sphere + 라벨 + 클릭 시 해당 segment 선택
                            if (s_showPredBelowMargin && seg.predIsBelowSafeMargin)
                            {
                                Handles.color = new Color(1f, 0.15f, 0.15f, 0.85f);
                                Handles.SphereHandleCap(0, seg.worldPos,
                                    Quaternion.identity, 1.2f, EventType.Repaint);
                                // 적색 라벨로 effective width 수치 표시
                                Vector3 labelWorldPos = seg.worldPos + Vector3.up * 2.0f;
                                string labelText = $"★ {seg.predEffectiveWidth:F2}m\n" +
                                                   $"  smin={seg.predSminPenetration:F2}\n" +
                                                   $"  noise={seg.predNoiseAmp:F2}";

                                // ═══════════════════════════════════════════
                                // [η Phase 3 #2] hover 시 라벨 색 강조 (easing 효과)
                                //   라벨 영역에 mouse over 시 적색 → 흰색으로 부드럽게.
                                //   Q2 사용자 요청 — sphere뿐 아니라 hover된 텍스트도 강조.
                                // ═══════════════════════════════════════════
                                Color labelBaseColor = new Color(1f, 0.4f, 0.4f);
                                Color labelHoverColor = Color.white;

                                // 라벨의 GUI screen 좌표 추정 (Handles.Label 위치 기준)
                                Vector2 labelScreen = HandleUtility.WorldToGUIPoint(labelWorldPos);
                                // 라벨 영역 추정 (3 lines × ~12px 높이, ~120px 폭)
                                Rect labelHitRect = new Rect(
                                    labelScreen.x - 6, labelScreen.y - 2,
                                    140, 36);
                                bool labelHovered = (evt != null
                                                     && labelHitRect.Contains(evt.mousePosition));

                                Color labelFinalColor = labelHovered
                                    ? labelHoverColor : labelBaseColor;

                                Handles.Label(labelWorldPos, labelText,
                                    new GUIStyle(EditorStyles.miniLabel)
                                    {
                                        normal = { textColor = labelFinalColor },
                                        fontSize = 9,
                                        fontStyle = FontStyle.Bold
                                    });

                                // hover 시 cursor Link (편의)
                                if (labelHovered)
                                    EditorGUIUtility.AddCursorRect(labelHitRect, MouseCursor.Link);

                                // ═══════════════════════════════════════════
                                // ★ Q2: BelowMargin sphere OR 라벨 클릭 시 segment 선택
                                //   sphere: ray-sphere intersection
                                //   label : 2D screen Rect.Contains
                                //   둘 중 하나라도 hit이면 SelectSegmentForPassage
                                //   추가: η Predictive panel 자동 펼침 (PreviewSceneRenderer)
                                // ═══════════════════════════════════════════
                                if (evt != null && cam != null
                                    && evt.type == EventType.MouseDown
                                    && evt.button == 0)
                                {
                                    bool sphereHit = false;
                                    bool labelHit = labelHitRect.Contains(evt.mousePosition);

                                    // sphere ray-cast (라벨 hit 아닌 경우만 검사 — 우선순위)
                                    if (!labelHit)
                                    {
                                        Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
                                        Vector3 sphereCenter = seg.worldPos;
                                        float sphereRadius = 1.2f;
                                        Vector3 oc = ray.origin - sphereCenter;
                                        float b = Vector3.Dot(oc, ray.direction);
                                        float c = Vector3.Dot(oc, oc) - sphereRadius * sphereRadius;
                                        float disc = b * b - c;
                                        sphereHit = (disc >= 0f && (-b - Mathf.Sqrt(disc)) > 0f);
                                    }

                                    if (sphereHit || labelHit)
                                    {
                                        // segment 선택 + 자동 펼침
                                        SelectSegmentForPassage(pi, si);
                                        // η Predictive panel auto-expand (Q3 펼침 자동화)
                                        CaveSystem.Editor.PreviewSceneRenderer
                                            .AutoExpandPredictivePanel(pi);
                                        evt.Use();
                                        SceneView.RepaintAll();
                                    }
                                }
                            }

                            // ── (3) Single-Source State 색상 표시 ──────────
                            //   state별 색상 cube로 segment 표시
                            //     0=typeA=청, 1=typeB=황, 2=blank=회, 3=dual=skip
                            if (s_showSingleSourceState)
                            {
                                Color stateColor;
                                bool drawState = true;

                                // ═══════════════════════════════════════════════
                                // [η DebugAware Fix] SS cube 색상 모드 분기
                                //   ColorByEffective ON: GPU 실제 평가 biome 색
                                //   ColorByEffective OFF: 기존 mode 색 (typeA/B/blank)
                                // ═══════════════════════════════════════════════
                                if (s_ssStateColorByEffective && seg.singleSourceState != 3)
                                {
                                    // blank zone은 회색 그대로 — biome 정보 없음
                                    if (seg.singleSourceState == 2)
                                    {
                                        stateColor = new Color(0.55f, 0.55f, 0.55f, 0.65f);
                                    }
                                    else
                                    {
                                        // ★ effective biome (GPU 평가 biome) 색상
                                        //   biome index → noiseType 변환 후 BiomeSampler.GetBiomeColor 호출
                                        int effIdx = seg.EffectiveBiomeIdx;
                                        int noiseType = (effIdx >= 0 && _inputs.biomes != null
                                                         && effIdx < _inputs.biomes.Count
                                                         && _inputs.biomes[effIdx] != null)
                                            ? _inputs.biomes[effIdx].noiseType : 0;
                                        stateColor = BiomeSampler.GetBiomeColor(noiseType);
                                        stateColor.a = 0.75f;
                                    }
                                }
                                else
                                {
                                    // 기존 mode 색상 (typeA/B/blank/dual)
                                    switch (seg.singleSourceState)
                                    {
                                        case 0:  // typeA
                                            stateColor = new Color(0.3f, 0.6f, 1.0f, 0.75f);
                                            break;
                                        case 1:  // typeB
                                            stateColor = new Color(1.0f, 0.85f, 0.2f, 0.75f);
                                            break;
                                        case 2:  // blank
                                            stateColor = new Color(0.55f, 0.55f, 0.55f, 0.65f);
                                            break;
                                        default: // dual (3) — γ OFF: skip
                                            stateColor = Color.clear;
                                            drawState = false;
                                            break;
                                    }
                                }
                                if (drawState)
                                {
                                    Handles.color = stateColor;
                                    // 작은 cube (sphere와 시각 구분)
                                    Handles.CubeHandleCap(0,
                                        seg.worldPos + Vector3.up * 0.7f,
                                        Quaternion.identity, 0.5f, EventType.Repaint);
                                }
                            }
                        }
                    }
                    Handles.color = origColor;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // [η DebugAware Phase 3 Q2] Segment 선택 헬퍼
        //
        //   BelowMargin sphere 클릭 시 호출.
        //   기존 BDV 메커니즘 (s_selectedPassageIdx) + PreviewInputs (selectedSegmentPerPassage)
        //   동시 갱신하여 Passage Info Panel이 자동으로 해당 segment 펼치도록 함.
        // ════════════════════════════════════════════════════════════════════
        private void SelectSegmentForPassage(int passageIdx, int segmentIdx)
        {
            // (1) BDV 측 — passage 선택 (Passage Info Panel 표시)
            s_selectedPassageIdx = passageIdx;

            // ★ 사용자 요청 #2: 라벨/sphere 클릭 시 panel 자동 켜기
            //   BDV 측 toggle (sticky — 사용자가 끄지 않는 한 다음 클릭에도 유지)
            //   PreviewInputs 측 동기화 (UpdateInputsFromState에서 매 frame 갱신되므로
            //   여기서도 즉시 set하여 다음 OnSceneGUI에 반영)
            s_showPassageInfoPanel = true;

            // (2) PreviewInputs 측 — 해당 segment 자동 선택 + panel 표시
            if (_inputs != null)
            {
                _inputs.showPassageInfoPanel = true;  // ★ panel 자동 켜기

                if (_inputs.selectedSegmentPerPassage == null)
                    _inputs.selectedSegmentPerPassage = new Dictionary<int, int>();
                _inputs.selectedSegmentPerPassage[passageIdx] = segmentIdx;
                _inputs.selectedPassageIdx = passageIdx;
            }
            // 다음 frame 표시 갱신
            Repaint();
        }

        // ══════════════════════════════════════════════════════════════════
        // Runtime data → PreviewInputs/Data 변환
        // ══════════════════════════════════════════════════════════════════
        private void RebuildPreview(List<NodeData> rtNodes, List<EdgeData> rtEdges)
        {
            // ── PreviewInputs 구성 ──
            _inputs.biomes.Clear();
            Vector3 dungeonBounds = new Vector3(300f, 50f, 300f);
            float macroScale = 500f;

            // [hotfix v5] CaveManager.Instance가 null(Edit 모드 중 scene에 CaveManager 있지만
            //   Awake 전)이면 직접 FindFirstObjectByType 로 탐색
            CaveBiomeSettings settings = null;
            if (CaveManager.Instance != null && CaveManager.Instance.biomeSettings != null)
            {
                settings = CaveManager.Instance.biomeSettings;
            }
            else
            {
                // Fallback: Scene에서 CaveManager 컴포넌트 직접 찾기
                var cm = Object.FindFirstObjectByType<CaveManager>();
                if (cm != null && cm.biomeSettings != null)
                {
                    settings = cm.biomeSettings;
                }
            }

            if (settings != null)
            {
                if (settings.globalBiomes != null)
                {
                    foreach (var b in settings.globalBiomes)
                        if (b != null) _inputs.biomes.Add(b);
                }
                macroScale = settings.macroBiomeScale;
            }

            if (CaveNodeGraphBuilder.Instance != null)
            {
                dungeonBounds = CaveNodeGraphBuilder.Instance.dungeonBounds;
            }

            _inputs.dungeonBounds = dungeonBounds;
            _inputs.macroBiomeScale = macroScale;
            _inputs.seed = 12345;  // Runtime에서 실제 seed 추적 안 되면 표시용 only

            // ── PreviewData 구성 ──
            _data.nodes = new List<NodeData>(rtNodes);
            _data.edges = new List<EdgeData>(rtEdges);

            // BiomeSamples 생성 (heatmap + boundary 용)
            _inputs.heatmapGridSize = s_heatmapGridSize;
            _inputs.heatmapOpacity = s_heatmapOpacity;
            _inputs.heatmapYPlane = s_heatmapYPlane;

            if (_inputs.biomes.Count > 0)
            {
                float actualGridSize;
                _data.biomeSamples = BiomeSampler.SampleGrid(_inputs, out actualGridSize);
                _data.actualGridSize = actualGridSize;

                // [hotfix v5] 진단 로그 — biome 배분 상태 확인
                if (_data.biomeSamples != null && _data.biomeSamples.Count > 0)
                {
                    // biome idx별 cell 수 집계
                    var biomeCounts = new Dictionary<int, int>();
                    foreach (var s in _data.biomeSamples)
                    {
                        int dominantIdx = s.blendWeight < 0.5f ? s.biomeIdxA : s.biomeIdxB;
                        biomeCounts[dominantIdx] = biomeCounts.GetValueOrDefault(dominantIdx) + 1;
                    }
                    string distStr = "";
                    foreach (var kv in biomeCounts)
                    {
                        string name = (kv.Key < _inputs.biomes.Count && _inputs.biomes[kv.Key] != null)
                            ? BiomeSampler.GetBiomeName(_inputs.biomes[kv.Key].noiseType)
                            : $"idx{kv.Key}";
                        distStr += $" {name}={kv.Value}";
                    }
                    Debug.Log($"[BiomeDebug] RebuildPreview 완료: " +
                              $"biomes={_inputs.biomes.Count}, macroScale={macroScale}, " +
                              $"bounds={dungeonBounds}, samples={_data.biomeSamples.Count}, " +
                              $"gridSize={actualGridSize:F1}m, 배분:{distStr}");
                }
            }
            else
            {
                _data.biomeSamples = new List<BiomeSamplePoint>();
                _data.actualGridSize = s_heatmapGridSize;
                Debug.LogWarning($"[BiomeDebug] biomeSettings 또는 globalBiomes 없음 → heatmap 표시 불가. " +
                                 $"CaveManager.Instance={(CaveManager.Instance != null ? "있음" : "null")}, " +
                                 $"fallback search={(settings != null ? "성공" : "실패")}");
            }

            // Critical path — Builder의 accessor 사용 (Phase 4.5-C 추가됨)
            _data.criticalPathEdges = CaveNodeGraphBuilder.Instance.GetCriticalPathEdges();
            _data.criticalEdgeIndices = BuildCriticalEdgeIndices(_data.nodes, _data.edges,
                _data.criticalPathEdges);

            // Violation edges — slope 기반 재계산 (A.11 특화)
            _data.violationEdges = new HashSet<int>();
            for (int i = 0; i < _data.edges.Count; i++)
            {
                var e = _data.edges[i];
                float dy = Mathf.Abs(e.endPos.y - e.startPos.y);
                float dxz = Mathf.Sqrt(
                    (e.endPos.x - e.startPos.x) * (e.endPos.x - e.startPos.x) +
                    (e.endPos.z - e.startPos.z) * (e.endPos.z - e.startPos.z));
                float slopeDeg = dxz > 0.01f ? Mathf.Atan2(dy, dxz) * Mathf.Rad2Deg : 0f;
                if (slopeDeg > s_biomeMaxSlope) _data.violationEdges.Add(i);
            }

            // Matrix/stats는 runtime에선 사용 안 함 (Editor PreviewStatsCalculator 경로 필요)
            _data.matrixApplied = new bool[_data.nodes.Count];
            _data.stats = new PreviewStats();
        }

        private static HashSet<int> BuildCriticalEdgeIndices(
            List<NodeData> nodes, List<EdgeData> edges, HashSet<Vector2Int> criticalEdges)
        {
            var result = new HashSet<int>();
            if (criticalEdges == null || criticalEdges.Count == 0) return result;
            for (int e = 0; e < edges.Count; e++)
            {
                int idxA = FindNodeIndex(nodes, edges[e].startPos);
                int idxB = FindNodeIndex(nodes, edges[e].endPos);
                if (idxA < 0 || idxB < 0) continue;
                Vector2Int key = new Vector2Int(Mathf.Min(idxA, idxB), Mathf.Max(idxA, idxB));
                if (criticalEdges.Contains(key)) result.Add(e);
            }
            return result;
        }

        private static int FindNodeIndex(List<NodeData> nodes, Vector3 pos)
        {
            for (int i = 0; i < nodes.Count; i++)
                if ((nodes[i].position - pos).sqrMagnitude < 0.01f) return i;
            return -1;
        }

        // ══════════════════════════════════════════════════════════════════
        // 토글 → PreviewInputs 동기화
        // ══════════════════════════════════════════════════════════════════
        private void SyncInputsFromToggles()
        {
            // FeatureFlags 구성
            FeatureFlags f = 0;
            if (s_showNodes) f |= FeatureFlags.ShowNodes;
            if (s_showEdges) f |= FeatureFlags.ShowEdges;
            if (s_showBiomeHeatmap) f |= FeatureFlags.ShowBiomeHeatmap;
            if (s_showCriticalPath) f |= FeatureFlags.ShowCriticalPath;
            if (s_showEdgeDirection) f |= FeatureFlags.ShowEdgeDirection;
            if (s_showCrossSection) f |= FeatureFlags.ShowCrossSection;
            if (s_showBlendWeightValue) f |= FeatureFlags.ShowBlendWeight;  // [hotfix v7]
            _inputs.flags = f;

            // [hotfix v7] Show blend weight numeric display
            _inputs.showBlendWeightValue = s_showBlendWeightValue;

            // 개별 토글
            _inputs.showRealScale = s_showRealScale;
            _inputs.showBiomeBoundary = s_showBiomeBoundary;
            _inputs.biomeBoundaryColored = s_biomeBoundaryColored;
            _inputs.showBlendTransitionZone = s_showBlendTransitionZone;
            _inputs.showBiomeLabels = s_showBiomeLabels;
            _inputs.showBiomeNodeListPanel = s_showBiomeNodeListPanel;
            _inputs.showNodeInfoPanel = s_showNodeInfoPanel;
            _inputs.showNormalNodeInfo = s_showNormalNodeInfo;

            // [Phase 4.5-G Stage 3-F] Passage Info Panel
            _inputs.showPassageInfoPanel = s_showPassageInfoPanel;
            _inputs.passageShowMode = (int)s_passageShowMode;
            _inputs.passageRiskFilter = s_passageRiskFilter;
            _inputs.selectedPassageIdx = s_selectedPassageIdx;
            _inputs.crossAxis = s_showCrossSection ? s_crossAxis : CrossSectionAxis.None;
            _inputs.crossPlaneValue = s_crossPlaneValue;
            _inputs.heatmapGridSize = s_heatmapGridSize;
            _inputs.heatmapOpacity = s_heatmapOpacity;
            _inputs.heatmapYPlane = s_heatmapYPlane;

            // [중요] enableMCPreview = false (Runtime에선 MC Preview 끔 - byte-identical)
            _inputs.enableMCPreview = false;

            // [hotfix v4 + v6] Show Edges 렌더 모드
            _inputs.edgeDisplayMode = s_edgeDisplayMode;
            _inputs.edgeMeshSimplifyTolerance = s_edgeMeshSimplifyTolerance;
            _inputs.edgeMeshFilterByGraph = s_edgeMeshFilterByGraph;
        }

        // ══════════════════════════════════════════════════════════════════
        // [Phase 4.5-G Stage 2] Supervisor Report UI
        // ══════════════════════════════════════════════════════════════════
        private static bool s_showSupervisor = true;
        private static bool s_highlightViolations = true;
        private static int s_selectedViolationIdx = -1;

        private void DrawSupervisorReport()
        {
            if (CaveNodeGraphBuilder.Instance == null) return;
            var result = CaveNodeGraphBuilder.Instance.lastSupervisorResult;
            if (result.violations == null) return;

            s_showSupervisor = EditorGUILayout.Foldout(s_showSupervisor,
                $"▶ Supervisor Report (violations: {result.violations.Count})", true);
            if (!s_showSupervisor) return;

            EditorGUI.indentLevel++;

            // Status badge
            if (result.isValid)
            {
                EditorGUILayout.HelpBox(
                    $"✓ 원칙 모두 통과 (AutoRepair {result.repairIterations}회)",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"⚠ 잔여 violations: {result.violations.Count}\n" +
                    $"  Crossover: {result.crossoverCount}  /  " +
                    $"DegreeExceed: {result.degreeExceedCount}  /  " +
                    $"BiomeImbalance: {result.biomeImbalanceCount}\n" +
                    $"  AutoRepair 시도: {result.repairIterations}회",
                    MessageType.Warning);
            }

            s_highlightViolations = EditorGUILayout.ToggleLeft(
                "Highlight Violations in Scene (빨강/주황 Overlay)",
                s_highlightViolations);

            // 제어 버튼
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Re-run Validate", GUILayout.Height(20)))
            {
                // CaveNodeGraphBuilder의 public method 없이 재검증만 (수정 없음)
                var newResult = GraphSupervisor.Validate(
                    CaveNodeGraphBuilder.Instance.nodesData,
                    CaveNodeGraphBuilder.Instance.edgesData,
                    CaveManager.Instance != null ? CaveManager.Instance.biomeSettings : null,
                    planarityYExemptMeters: CaveNodeGraphBuilder.Instance.planarityYExemptMeters,
                    maxDegreeNormal: CaveNodeGraphBuilder.Instance.maxDegreeNormal,
                    checkPlanarity: CaveNodeGraphBuilder.Instance.enablePlanarityCheck,
                    checkDegree: CaveNodeGraphBuilder.Instance.enableDegreeCheck,
                    checkBiomeQuota: CaveNodeGraphBuilder.Instance.enableBiomeQuotaCheck);
                CaveNodeGraphBuilder.Instance.lastSupervisorResult = newResult;
            }
            if (GUILayout.Button("Auto-Repair Again", GUILayout.Height(20)))
            {
                // Runtime 데이터에 직접 AutoRepair (edges 변경 발생)
                var mstKeys = new HashSet<Vector2Int>();
                // MST 정보 없이 AutoRepair하면 MST edge까지 제거될 수 있음 → 경고
                if (EditorUtility.DisplayDialog("Auto-Repair 실행",
                    "MST edge 정보 없이 AutoRepair 실행 시 connectivity 문제 가능.\n" +
                    "Play mode 재진입 또는 Regenerate 권장. 계속?",
                    "계속", "취소"))
                {
                    var newResult = GraphSupervisor.AutoRepair(
                        CaveNodeGraphBuilder.Instance.nodesData,
                        CaveNodeGraphBuilder.Instance.edgesData,
                        mstKeys,
                        CaveManager.Instance != null ? CaveManager.Instance.biomeSettings : null,
                        planarityYExemptMeters: CaveNodeGraphBuilder.Instance.planarityYExemptMeters,
                        maxDegreeNormal: CaveNodeGraphBuilder.Instance.maxDegreeNormal,
                        maxIterations: CaveNodeGraphBuilder.Instance.supervisorMaxIterations);
                    CaveNodeGraphBuilder.Instance.lastSupervisorResult = newResult;
                }
            }
            EditorGUILayout.EndHorizontal();

            // Violation 리스트 (스크롤 가능)
            if (result.violations.Count > 0)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Violations (클릭 시 Scene 하이라이트):",
                    EditorStyles.miniBoldLabel);
                for (int i = 0; i < result.violations.Count; i++)
                {
                    var v = result.violations[i];
                    GUIStyle style = i == s_selectedViolationIdx
                        ? new GUIStyle(EditorStyles.helpBox) { fontStyle = FontStyle.Bold }
                        : EditorStyles.helpBox;
                    Color bg = GetViolationColor(v.type);
                    var orig = GUI.backgroundColor;
                    GUI.backgroundColor = bg;
                    if (GUILayout.Button($"#{i + 1} [{v.type}] {v.description}",
                        style, GUILayout.ExpandWidth(true)))
                    {
                        s_selectedViolationIdx = (i == s_selectedViolationIdx) ? -1 : i;
                        SceneView.RepaintAll();
                    }
                    GUI.backgroundColor = orig;
                }
            }

            EditorGUI.indentLevel--;
        }

        // ════════════════════════════════════════════════════════════════════
        // [Phase 4.5-G P2] γ State Diagnostics — δ/ε 진단 패널
        //
        //   표시 항목:
        //     1. 현재 BiomeSyncMode (5-state) + 활성 γ 토글 매트릭스
        //     2. δ/ε 위험 토글 상태 (RoutePlanner / SpatialHash / BiomeAware)
        //     3. PassageMetric Risk Aggregate (lastRiskAggregate, δ/ε에서 채워짐)
        //     4. Plain Passage Risk 통계 (γ Single-Source 모델 위험도)
        //     5. γ 시각화 토글 (centrality heatmap, plain risk edges, Voronoi grid)
        //
        //   동작 조건:
        //     α/β: 정보만 표시, γ 메트릭은 의미 없음 (회색 처리)
        //     γ:   Single-Source 메트릭 표시
        //     δ/ε: lastRiskAggregate + lastPassageProfiles 정보 활용 가능
        // ════════════════════════════════════════════════════════════════════
        // ════════════════════════════════════════════════════════════════════
        // [P2-1~P2-5] γ State Diagnostics 패널
        //   ★ Q4 사용자 요청: 접을 수 있도록 Foldout으로 변경
        //   default: 펼침 (사용자 검증 편의)
        //   접으면 모든 sub-section 숨김 → 화면 공간 확보
        // ════════════════════════════════════════════════════════════════════
        private static bool s_showStateDiagnostics = true;  // ★ default 펼침

        private void DrawGammaStateDiagnostics()
        {
            EditorGUILayout.Space(4);
            // ★ Q4: LabelField → Foldout 으로 변경 (클릭하여 접기 가능)
            s_showStateDiagnostics = EditorGUILayout.Foldout(
                s_showStateDiagnostics,
                "γ State Diagnostics",
                toggleOnLabelClick: true,
                style: EditorStyles.foldoutHeader);
            if (!s_showStateDiagnostics) return;  // ★ 접혀있으면 모든 sub-section skip
            EditorGUI.indentLevel++;

            // [P2-1] 현재 BiomeSyncMode 표시
            var mgr = CaveSystem.CaveManager.Instance;
            if (mgr == null)
            {
                EditorGUILayout.HelpBox("CaveManager.Instance 없음 — Play mode 진입 필요", MessageType.Warning);
                EditorGUI.indentLevel--;
                return;
            }

            var mode = mgr.biomeSyncMode;
            string modeLabel = GetBiomeSyncModeLabel(mode);
            string modeColor = GetBiomeSyncModeColor(mode);
            EditorGUILayout.LabelField(
                $"<color={modeColor}>BiomeSyncMode: {modeLabel}</color>",
                new GUIStyle(EditorStyles.label) { richText = true, fontStyle = FontStyle.Bold });

            // [P2-2] State별 활성 γ 토글 매트릭스
            DrawGammaToggleMatrix(mode);

            // [P2-3] δ/ε에서 PassageMetric Risk Aggregate
            if (mgr.IsDeltaOrLater && CaveNodeGraphBuilder.Instance != null)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("━ PassageMetric Risk Aggregate ━",
                    new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold });

                var agg = CaveNodeGraphBuilder.Instance.lastRiskAggregate;
                int totalEdges = agg.totalEdges;
                if (totalEdges > 0)
                {
                    // RiskAggregate 표시 (afterf3 RiskAggregate 구조: safeCount/cautionCount/criticalCount)
                    float safePct = agg.safeCount * 100f / totalEdges;
                    float cautionPct = agg.cautionCount * 100f / totalEdges;
                    float criticalPct = agg.criticalCount * 100f / totalEdges;

                    EditorGUILayout.LabelField(
                        $"<color=#3FB950>Safe: {agg.safeCount} ({safePct:F1}%)</color> | " +
                        $"<color=#D29922>Caution: {agg.cautionCount} ({cautionPct:F1}%)</color> | " +
                        $"<color=#F85149>Critical: {agg.criticalCount} ({criticalPct:F1}%)</color>",
                        new GUIStyle(EditorStyles.miniLabel) { richText = true });

                    // [P2-4] Plain Passage Risk 추정 — γ Single-Source 위험
                    //   양 끝 동일 blend pair + 통과 길이 길면 위험
                    int plainRiskCount = EstimatePlainPassageRiskCount(
                        CaveNodeGraphBuilder.Instance.lastPassageProfiles);
                    float plainPct = plainRiskCount * 100f / totalEdges;
                    string plainColor = plainPct > 5f ? "#F85149" : (plainPct > 2f ? "#D29922" : "#3FB950");
                    EditorGUILayout.LabelField(
                        $"<color={plainColor}>★ Plain Passage Risk: " +
                        $"{plainRiskCount}/{totalEdges} ({plainPct:F1}%)</color>",
                        new GUIStyle(EditorStyles.miniLabel) { richText = true });

                    if (plainPct > 5f)
                    {
                        EditorGUILayout.HelpBox(
                            "★ Plain Risk > 5% — Centrality-Aware Spawn 또는 nodeMaxCentrality 검토 필요",
                            MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "lastRiskAggregate 비어있음 — δ/ε 진입 후 dungeon 재생성 필요",
                        MessageType.Info);
                }
            }
            else if (mgr.IsSingleSourceActive)
            {
                EditorGUILayout.HelpBox(
                    "γ State 활성 — δ/ε으로 전환하면 PassageMetric Risk Aggregate 표시됨",
                    MessageType.Info);
            }

            // [P2-5] γ 시각화 토글
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("━ γ Visualization Toggles ━",
                new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold });

            using (new EditorGUI.DisabledScope(!mgr.IsSingleSourceActive))
            {
                s_showCentralityHeatmap = EditorGUILayout.ToggleLeft(
                    "Centrality Heatmap (노드 색상 — 0.0=녹, 1.0=적)", s_showCentralityHeatmap);
                s_showPlainRiskEdges = EditorGUILayout.ToggleLeft(
                    "Plain Risk Edges (위험 edges 적색 강조)", s_showPlainRiskEdges);
                s_showGammaVisualAmp = EditorGUILayout.ToggleLeft(
                    "γ Visual Amp (Passage Info Panel에 표시)", s_showGammaVisualAmp);
                s_showVoronoiCellGrid = EditorGUILayout.ToggleLeft(
                    "Case 1 Voronoi Cell Grid (cellSize 2.5m overlay)", s_showVoronoiCellGrid);
            }

            if (!mgr.IsSingleSourceActive)
            {
                EditorGUILayout.LabelField(
                    "  (γ/δ/ε에서만 의미 있음)",
                    new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic });
            }

            // ═══════════════════════════════════════════════════════════════
            // [Phase 4.5-G η DebugAware Phase 3] Predictive Visualization
            //   η DebugAware OR ε FullMerge state에서만 활성
            // ═══════════════════════════════════════════════════════════════
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("━ η Predictive Visualization (η DebugAware) ━",
                new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold });

            using (new EditorGUI.DisabledScope(!mgr.IsDebugAwareOrLater))
            {
                s_showPredictiveOverlay = EditorGUILayout.ToggleLeft(
                    "Predictive Effective Width (segment별 sphere)", s_showPredictiveOverlay);
                s_showPredBelowMargin = EditorGUILayout.ToggleLeft(
                    "★ Below Safe Margin Segments (적색 강조)", s_showPredBelowMargin);
                s_showSingleSourceState = EditorGUILayout.ToggleLeft(
                    "Single-Source State (typeA=청, typeB=황, blank=회)", s_showSingleSourceState);
                if (s_showSingleSourceState)
                {
                    // ═══════════════════════════════════════════════════════════
                    // [η DebugAware Fix] SS cube 색상 모드 — mode vs effective biome
                    //
                    //   ColorByMode (default OFF): mode 분류 (typeA=청, typeB=황, blank=회)
                    //     장점: bw<0.5 / bw>0.5 위치 시각 분류 (디버깅용)
                    //     단점: 같은 biome 평가하는 voxel이 다른 색 — 사용자 혼란
                    //
                    //   ColorByEffective (★ 권장): GPU 실제 평가 biome 색
                    //     장점: 같은 biome은 같은 색 → mesh 형태와 일치
                    //     단점: typeA/B mode 정보 없음
                    // ═══════════════════════════════════════════════════════════
                    EditorGUI.indentLevel++;
                    s_ssStateColorByEffective = EditorGUILayout.ToggleLeft(
                        "★ Color by Effective Biome (mesh 형태와 일치, mode 색 대신)",
                        s_ssStateColorByEffective);
                    EditorGUI.indentLevel--;
                }
            }

            if (!mgr.IsDebugAwareOrLater)
            {
                EditorGUILayout.LabelField(
                    "  (η DebugAware 또는 ε FullMerge에서만 의미 있음)",
                    new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic });
            }
            else if (CaveNodeGraphBuilder.Instance != null)
            {
                // η Predictive 통계 표시
                var profiles = CaveNodeGraphBuilder.Instance.lastPassageProfiles;
                if (profiles != null && profiles.Count > 0)
                {
                    int totalSegs = 0, belowMarginSegs = 0;
                    int ssTypeA = 0, ssTypeB = 0, ssBlank = 0, ssDual = 0;
                    foreach (var p in profiles)
                    {
                        if (p.segments == null) continue;
                        foreach (var s in p.segments)
                        {
                            totalSegs++;
                            if (s.predIsBelowSafeMargin) belowMarginSegs++;
                            switch (s.singleSourceState)
                            {
                                case 0: ssTypeA++; break;
                                case 1: ssTypeB++; break;
                                case 2: ssBlank++; break;
                                default: ssDual++; break;
                            }
                        }
                    }
                    if (totalSegs > 0)
                    {
                        float belowPct = belowMarginSegs * 100f / totalSegs;
                        string belowColor = belowPct > 5f ? "#F85149"
                                          : (belowPct > 2f ? "#D29922" : "#3FB950");
                        EditorGUILayout.LabelField(
                            $"<color={belowColor}>★ Below Safe Margin: " +
                            $"{belowMarginSegs}/{totalSegs} segments ({belowPct:F1}%)</color>",
                            new GUIStyle(EditorStyles.miniLabel) { richText = true });
                        EditorGUILayout.LabelField(
                            $"  Single-Source distribution: A={ssTypeA}, B={ssTypeB}, " +
                            $"blank={ssBlank}, dual={ssDual}",
                            EditorStyles.miniLabel);
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        // ─── BiomeSyncMode 라벨/색상 헬퍼 ───────────────────────────────────
        private string GetBiomeSyncModeLabel(CaveSystem.BiomeSyncMode mode)
        {
            switch (mode)
            {
                case CaveSystem.BiomeSyncMode.Legacy:                    return "α Legacy (Route_Astar baseline)";
                case CaveSystem.BiomeSyncMode.GpuAligned:                return "β GpuAligned (F3 + I7 Anchor)";
                case CaveSystem.BiomeSyncMode.SingleSourceEcotone:       return "γ SingleSourceEcotone (Single-Source + Voronoi)";
                case CaveSystem.BiomeSyncMode.SingleSourceEcotonePlus:   return "δ SingleSourceEcotonePlus (γ + 안전 afterf3)";
                case CaveSystem.BiomeSyncMode.FullMerge:                  return "ε FullMerge (δ + 위험 afterf3)";
                default: return mode.ToString();
            }
        }

        private string GetBiomeSyncModeColor(CaveSystem.BiomeSyncMode mode)
        {
            switch (mode)
            {
                case CaveSystem.BiomeSyncMode.Legacy:                    return "#8B949E";
                case CaveSystem.BiomeSyncMode.GpuAligned:                return "#D29922";
                case CaveSystem.BiomeSyncMode.SingleSourceEcotone:       return "#56D4DD";
                case CaveSystem.BiomeSyncMode.SingleSourceEcotonePlus:   return "#BC8CFF";
                case CaveSystem.BiomeSyncMode.FullMerge:                  return "#FF7EB6";
                default: return "#FFFFFF";
            }
        }

        // ─── State별 γ 토글 매트릭스 표시 ────────────────────────────────────
        private void DrawGammaToggleMatrix(CaveSystem.BiomeSyncMode mode)
        {
            var dispatcher = CaveSystem.CaveManager.Instance?.computeDispatcher;
            var graph = CaveSystem.CaveNodeGraphBuilder.Instance;

            bool singleSource = (mode == CaveSystem.BiomeSyncMode.SingleSourceEcotone
                              || mode == CaveSystem.BiomeSyncMode.SingleSourceEcotonePlus
                              || mode == CaveSystem.BiomeSyncMode.FullMerge);

            EditorGUILayout.LabelField("━ Active Toggles ━",
                new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold });

            string fmt(string label, bool on, string note = "")
            {
                string color = on ? "#3FB950" : "#8B949E";
                string icon = on ? "✓" : "·";
                string n = string.IsNullOrEmpty(note) ? "" : $"  <i>{note}</i>";
                return $"  <color={color}>{icon} {label}</color>{n}";
            }
            var st = new GUIStyle(EditorStyles.miniLabel) { richText = true };

            // γ 활성 토글
            EditorGUILayout.LabelField(fmt("Single-Source (FadePower=" +
                (dispatcher != null ? dispatcher.singleSourceFadePower.ToString("F1") : "?") + ")", singleSource), st);
            EditorGUILayout.LabelField(fmt("Soft-Terrace", singleSource), st);
            EditorGUILayout.LabelField(fmt("Columnar Voronoi (γ 옵션 A)", singleSource), st);
            EditorGUILayout.LabelField(fmt("Centrality-Aware Spawn",
                graph != null && graph.enableCentralityAwareSpawn,
                graph != null ? $"max={graph.nodeMaxCentrality:F2}" : ""), st);

            // δ/ε 추가 토글 (afterf3)
            if (mode == CaveSystem.BiomeSyncMode.SingleSourceEcotonePlus
             || mode == CaveSystem.BiomeSyncMode.FullMerge)
            {
                EditorGUILayout.LabelField("━ afterf3 Merge ━",
                    new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold });
                EditorGUILayout.LabelField(fmt("PassageMetric (CPU only, ZERO 위험)", true), st);
                EditorGUILayout.LabelField(fmt("EdgeData 72B", true,
                    "numWaypoints=0 fast-path"), st);

                bool ePlanner = graph != null && graph.enableRoutePlanner;
                bool eSpatial = graph != null && graph.enableSpatialHash;
                bool eBiomeR = graph != null && graph.enableBiomeAwareRouting;

                EditorGUILayout.LabelField(fmt("RoutePlanner", ePlanner,
                    ePlanner ? "★ HIGH 위험 — waypoint 주입" : "δ에서 강제 OFF"), st);
                EditorGUILayout.LabelField(fmt("SpatialHash", eSpatial,
                    eSpatial ? "MEDIUM 위험" : "δ에서 강제 OFF"), st);
                EditorGUILayout.LabelField(fmt("Biome-Aware Routing", eBiomeR,
                    eBiomeR ? "★ HIGH 위험" : "δ에서 강제 OFF"), st);
            }
        }

        // ─── Plain Passage Risk 추정 ─────────────────────────────────────────
        //   [P2.E] PassageMetric.RiskScore.isPlainRisk를 직접 사용 (정확한 측정)
        //   ComputeProfile에서 이미 segment-based plainPassageRisk를 계산함
        private int EstimatePlainPassageRiskCount(
            System.Collections.Generic.List<PassageMetric.PassageProfile> profiles)
        {
            if (profiles == null || profiles.Count == 0) return 0;
            int count = 0;
            foreach (var p in profiles)
            {
                // [P2.E] RiskScore.isPlainRisk 활성 시 카운트
                //   ComputeProfile에서 plainPassageRisk >= 0.6일 때 isPlainRisk=true
                if (p.risk.isPlainRisk) count++;
            }
            return count;
        }

        private Color GetViolationColor(GraphSupervisor.ViolationType type)
        {
            switch (type)
            {
                case GraphSupervisor.ViolationType.Crossover: return new Color(1f, 0.7f, 0.7f);
                case GraphSupervisor.ViolationType.DegreeExceed: return new Color(1f, 0.85f, 0.5f);
                case GraphSupervisor.ViolationType.BiomeImbalance: return new Color(0.7f, 0.9f, 1f);
                case GraphSupervisor.ViolationType.IsolatedNode: return new Color(1f, 0.5f, 0.5f);
                default: return Color.white;
            }
        }

        /// <summary>
        /// [Stage 2] Scene view에 violation 하이라이트 표시
        /// </summary>
        private void DrawSupervisorOverlay(List<NodeData> rtNodes, List<EdgeData> rtEdges)
        {
            if (CaveNodeGraphBuilder.Instance == null) return;
            var result = CaveNodeGraphBuilder.Instance.lastSupervisorResult;
            if (result.violations == null || result.violations.Count == 0) return;
            if (!s_highlightViolations) return;

            var prevZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            for (int i = 0; i < result.violations.Count; i++)
            {
                var v = result.violations[i];
                bool isSelected = i == s_selectedViolationIdx;
                float thickness = isSelected ? 6f : 3f;

                switch (v.type)
                {
                    case GraphSupervisor.ViolationType.Crossover:
                        // 두 edge를 빨강 선으로 덮어 그림 + 교차점 X 마커
                        if (v.edgeIdxA >= 0 && v.edgeIdxA < rtEdges.Count
                            && v.edgeIdxB >= 0 && v.edgeIdxB < rtEdges.Count)
                        {
                            var ea = rtEdges[v.edgeIdxA];
                            var eb = rtEdges[v.edgeIdxB];
                            Handles.color = new Color(1f, 0.2f, 0.2f, 0.9f);
                            Handles.DrawAAPolyLine(thickness,
                                new Vector3[] { ea.startPos, ea.endPos });
                            Handles.DrawAAPolyLine(thickness,
                                new Vector3[] { eb.startPos, eb.endPos });

                            // 교차점 X 표시
                            Vector3 midA = (ea.startPos + ea.endPos) * 0.5f;
                            Vector3 midB = (eb.startPos + eb.endPos) * 0.5f;
                            Vector3 cross = (midA + midB) * 0.5f;
                            float s = 1.5f;
                            Handles.color = Color.red;
                            Handles.DrawLine(cross + new Vector3(-s, 0, -s), cross + new Vector3(s, 0, s));
                            Handles.DrawLine(cross + new Vector3(-s, 0, s), cross + new Vector3(s, 0, -s));
                            if (isSelected)
                            {
                                Handles.Label(cross + Vector3.up * 2f,
                                    $"✕ Crossover #{i + 1}",
                                    new GUIStyle() { normal = { textColor = Color.red },
                                        fontStyle = FontStyle.Bold, fontSize = 12 });
                            }
                        }
                        break;

                    case GraphSupervisor.ViolationType.DegreeExceed:
                        if (v.nodeIdx >= 0 && v.nodeIdx < rtNodes.Count)
                        {
                            var n = rtNodes[v.nodeIdx];
                            Handles.color = new Color(1f, 0.7f, 0.2f, 0.9f);
                            // 경고 원형 (큰 disc)
                            Handles.DrawWireDisc(n.position + Vector3.up * 0.5f, Vector3.up,
                                n.radius * 1.3f);
                            Handles.DrawWireDisc(n.position + Vector3.up * 0.5f, Vector3.up,
                                n.radius * 1.5f);
                            if (isSelected)
                            {
                                Handles.Label(n.position + Vector3.up * 3f,
                                    $"⚠ Degree Exceed #{i + 1}",
                                    new GUIStyle() { normal = { textColor = new Color(1f, 0.7f, 0f) },
                                        fontStyle = FontStyle.Bold, fontSize = 12 });
                            }
                        }
                        break;

                    case GraphSupervisor.ViolationType.BiomeImbalance:
                        // Scene에는 표시 어려움 (전역 이슈) — UI에서만 표시
                        break;
                }
            }

            Handles.zTest = prevZTest;
        }

        // ══════════════════════════════════════════════════════════════════
        // A.11 특화 overlay — PreviewSceneRenderer에 포함되지 않은 것
        // ══════════════════════════════════════════════════════════════════
        private void DrawA11Overlays(List<NodeData> rtNodes, List<EdgeData> rtEdges)
        {
            // [Stage 2] Supervisor 하이라이트 먼저 (다른 overlay 위에 덮이지 않게 마지막도 가능)
            DrawSupervisorOverlay(rtNodes, rtEdges);

            // 1. SculptFlags 벡터 (A.5)
            if (s_showSculpt)
            {
                Handles.color = Color.yellow;
                foreach (var n in rtNodes)
                {
                    if (n.sculptFlags.sqrMagnitude > 0.01f)
                    {
                        Vector3 tip = n.position + n.sculptFlags * 5f;
                        Handles.DrawAAPolyLine(2f, n.position, tip);
                        // 끝점에 화살표
                        Handles.ConeHandleCap(0, tip,
                            Quaternion.LookRotation(n.sculptFlags.normalized),
                            0.4f, EventType.Repaint);
                    }
                }
            }

            // 2. Map Bounds (dungeonBounds vs worldMapSize 불일치 경고)
            if (s_showMapBounds) DrawMapBounds();

            // 3. Chunk Wireframe (viewDistance 기반)
            if (s_showChunkWireframe) DrawChunkWireframe();

            // 4. Steep edge highlight — s_showEdges OFF일 때만 별도 overlay
            //   (ON일 때는 PreviewSceneRenderer가 violationEdges로 처리)
            if (s_highlightSteep && !s_showEdges)
            {
                foreach (var e in rtEdges)
                {
                    float dy = Mathf.Abs(e.endPos.y - e.startPos.y);
                    float dxz = Mathf.Sqrt(
                        (e.endPos.x - e.startPos.x) * (e.endPos.x - e.startPos.x) +
                        (e.endPos.z - e.startPos.z) * (e.endPos.z - e.startPos.z));
                    float slopeDeg = dxz > 0.01f ? Mathf.Atan2(dy, dxz) * Mathf.Rad2Deg : 0f;
                    if (slopeDeg > s_biomeMaxSlope)
                    {
                        Handles.color = Color.red;
                        Handles.DrawAAPolyLine(3f, e.startPos, e.endPos);
                    }
                }
            }
        }

        private static void DrawMapBounds()
        {
            if (CaveNodeGraphBuilder.Instance == null) return;
            Vector3 dBounds = CaveNodeGraphBuilder.Instance.dungeonBounds;
            Vector3 center = CaveNodeGraphBuilder.Instance.transform.position;

            // dungeonBounds — cyan
            Handles.color = new Color(0f, 1f, 1f, 0.6f);
            Handles.DrawWireCube(center, dBounds);
            Handles.Label(center + new Vector3(dBounds.x * 0.5f, dBounds.y * 0.5f, 0f),
                $"dungeonBounds {dBounds}");

            // worldMapSize — yellow (불일치 시)
            if (CaveManager.Instance != null
                && CaveManager.Instance.chunkManager != null
                && CaveManager.Instance.chunkManager.terrainConfig != null)
            {
                Vector3 mapSize = CaveManager.Instance.chunkManager.terrainConfig.worldMapSize;
                Handles.color = new Color(1f, 1f, 0f, 0.5f);
                Handles.DrawWireCube(center, mapSize);
                if ((dBounds - mapSize).sqrMagnitude > 0.1f)
                {
                    Handles.Label(center + new Vector3(-mapSize.x * 0.5f, mapSize.y * 0.5f, 0f),
                        $"worldMapSize {mapSize}\n⚠ dungeonBounds와 불일치");
                }
            }
        }

        private static void DrawChunkWireframe()
        {
            if (CaveManager.Instance == null
                || CaveManager.Instance.chunkManager == null
                || CaveManager.Instance.chunkManager.terrainConfig == null) return;
            var tc = CaveManager.Instance.chunkManager.terrainConfig;
            float cws = tc.ChunkWorldSize;
            int vd = tc.viewDistance;
            int vdY = tc.viewDistanceY;

            Vector3 camPos = SceneView.lastActiveSceneView != null
                ? SceneView.lastActiveSceneView.camera.transform.position
                : Vector3.zero;
            Vector3Int camChunk = new Vector3Int(
                Mathf.RoundToInt(camPos.x / cws),
                Mathf.RoundToInt(camPos.y / cws),
                Mathf.RoundToInt(camPos.z / cws)
            );

            Handles.color = new Color(0.5f, 0.5f, 1f, 0.2f);
            for (int dx = -vd; dx <= vd; dx++)
            {
                for (int dz = -vd; dz <= vd; dz++)
                {
                    for (int dy = -vdY; dy <= vdY; dy++)
                    {
                        Vector3 chunkCenter = new Vector3(
                            (camChunk.x + dx) * cws + cws * 0.5f,
                            (camChunk.y + dy) * cws + cws * 0.5f,
                            (camChunk.z + dz) * cws + cws * 0.5f
                        );
                        Handles.DrawWireCube(chunkCenter, Vector3.one * cws);
                    }
                }
            }
        }

        private static Vector3 GetRuntimeDungeonBounds()
        {
            if (CaveNodeGraphBuilder.Instance != null)
                return CaveNodeGraphBuilder.Instance.dungeonBounds;
            return new Vector3(300f, 50f, 300f);
        }

        // ═══════════════════════════════════════════════════════════════
        // [Phase 16] ★ Junction v4 5-Axis Visualizer Panel
        // ═══════════════════════════════════════════════════════════════
        private static bool s_jvL1NodeFold = true;     // L1 Node Roles
        private static bool s_jvL2PassageFold = true;  // L2 Passage Types
        private static bool s_jvL3RegionFold = false;  // L3 Region (Phase 26)
        private static bool s_jvL4CrossoverFold = false; // L4 Crossover (Phase 25)
        private static bool s_jvL5TagFold = true;      // L5 Tag (Phase 21)
        private static bool s_jvAggFold = true;        // Aggregate Stats

        private static void DrawJunctionV4Panel()
        {
            var gb = CaveNodeGraphBuilder.Instance;
            if (gb == null)
            {
                EditorGUILayout.HelpBox("Graph not built — open Scene with CaveManager.", MessageType.Info);
                return;
            }

            // ─── A1 Node Roles ───────────────────────────────────
            s_jvL1NodeFold = EditorGUILayout.Foldout(s_jvL1NodeFold, "  A1 ─ Node Roles (Spawn/Boss/Treasure/Hub/Normal)");
            if (s_jvL1NodeFold) DrawSection_NodeRoles(gb);

            // ─── A2 Passage Types ────────────────────────────────
            s_jvL2PassageFold = EditorGUILayout.Foldout(s_jvL2PassageFold, "  A2 ─ Passage Types (★ 5m 정책 반영)");
            if (s_jvL2PassageFold) DrawSection_PassageTypes(gb);

            // ─── A3 Region (placeholder for Phase 26) ────────────
            s_jvL3RegionFold = EditorGUILayout.Foldout(s_jvL3RegionFold, "  A3 ─ Region [Phase 26 — graph clustering 7 type]");
            if (s_jvL3RegionFold) DrawSection_Region_Placeholder();

            // ─── A4 Crossover (placeholder for Phase 25) ─────────
            s_jvL4CrossoverFold = EditorGUILayout.Foldout(s_jvL4CrossoverFold, "  A4 ─ Crossover [Phase 25 — 6 type]");
            if (s_jvL4CrossoverFold) DrawSection_Crossover_Placeholder();

            // ─── L5 Tag 4 Category (★ Phase 21에서 4 Category, 현재 기존 6 tag 표시) ───
            s_jvL5TagFold = EditorGUILayout.Foldout(s_jvL5TagFold, "  L5 ─ Tag (★ Phase 21에서 4 Category 분리)");
            if (s_jvL5TagFold) DrawSection_Tags(gb);

            // ─── Aggregate ───────────────────────────────────────
            s_jvAggFold = EditorGUILayout.Foldout(s_jvAggFold, "  ★ Aggregate Statistics");
            if (s_jvAggFold) DrawSection_Aggregate(gb);
        }

        private static void DrawSection_NodeRoles(CaveNodeGraphBuilder gb)
        {
            // RoomType 카운트 — CaveNodeGraphBuilder.nodesData 활용
            int spawn = 0, boss = 0, treasure = 0, hub = 0, normal = 0, other = 0;
            try
            {
                if (gb.nodesData != null)
                {
                    for (int i = 0; i < gb.nodesData.Count; i++)
                    {
                        var n = gb.nodesData[i];
                        switch (n.roomType)
                        {
                            case 1: spawn++; break;
                            case 2: boss++; break;
                            case 3: treasure++; break;
                            case 4: hub++; break;
                            case 0: normal++; break;
                            default: other++; break;
                        }
                    }
                }
            }
            catch { /* 안전 */ }

            EditorGUILayout.LabelField($"    🟢 Spawn: {spawn}    🔴 Boss: {boss}    🟡 Treasure: {treasure}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"    🔵 Hub: {hub}    ⚪ Normal: {normal}    Other: {other}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"    Total: {spawn + boss + treasure + hub + normal + other} nodes",
                EditorStyles.miniLabel);

            // ★ Phase 19에서 Possible Roles 추가 표시 예정
            EditorGUILayout.LabelField("    ★ Phase 19 — Possible Roles (Cathedral/Vault/...) 미구현",
                EditorStyles.centeredGreyMiniLabel);
        }

        private static void DrawSection_PassageTypes(CaveNodeGraphBuilder gb)
        {
            // ★ Phase 15 — 5m 정책 반영된 분류
            var profiles = gb.lastPassageProfiles;
            if (profiles == null || profiles.Count == 0)
            {
                EditorGUILayout.LabelField("    (★ profile 없음 — η/ε mode 활성 필요)",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            int sub5m = 0, narrow = 0, standard = 0, wide = 0, grand = 0;
            int chimney = 0, ramp = 0, bridge = 0, collapse = 0, spiral = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                var t = profiles[i].inferredType;
                if (t == PassageMetric.PassageJunctionType.Sub5mEmergency) sub5m++;
                else if (t == PassageMetric.PassageJunctionType.Narrow) narrow++;
                else if (t == PassageMetric.PassageJunctionType.Standard) standard++;
                else if (t == PassageMetric.PassageJunctionType.Wide) wide++;
                else if (t == PassageMetric.PassageJunctionType.Grand) grand++;
                else if (t == PassageMetric.PassageJunctionType.Chimney) chimney++;
                else if (t == PassageMetric.PassageJunctionType.Ramp) ramp++;
                else if (t == PassageMetric.PassageJunctionType.Bridge) bridge++;
                else if (t == PassageMetric.PassageJunctionType.Collapse) collapse++;
                else if (t == PassageMetric.PassageJunctionType.Spiral) spiral++;
            }

            // ★ Width 4단계 (5m 정책)
            EditorGUILayout.LabelField($"    ★ Width 분포 (Phase 15 — 5m 정책):", EditorStyles.miniLabel);
            if (sub5m > 0)
            {
                var oldColor = GUI.color;
                GUI.color = new Color(1f, 0.4f, 0.4f);
                EditorGUILayout.LabelField($"      ⚠ Sub5mEmergency: {sub5m} (★ 5m 정책 위반 — δ/ε 처리 필요)",
                    EditorStyles.miniLabel);
                GUI.color = oldColor;
            }
            EditorGUILayout.LabelField($"      Narrow (5~7m): {narrow}    Standard (7~10m): {standard}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"      Wide (10~15m): {wide}    Grand (>15m): {grand}",
                EditorStyles.miniLabel);

            // 특수 type
            EditorGUILayout.LabelField($"    Y-axis: Chimney {chimney} / Ramp {ramp}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"    Special: ★Bridge {bridge} / ★Collapse {collapse} / Spiral {spiral}",
                EditorStyles.miniLabel);
        }

        private static void DrawSection_Region_Placeholder()
        {
            EditorGUILayout.LabelField("    [Phase 26 도입 후] Labyrinth / ChamberCluster / VerticalZone /",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField("                       GalleryWing / Sanctuary / FrontierZone(F19) / NomadZone(F15)",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField("    ★ Region ≠ Biome (★ Junction Doc v4)",
                EditorStyles.centeredGreyMiniLabel);
        }

        private static void DrawSection_Crossover_Placeholder()
        {
            EditorGUILayout.LabelField("    [Phase 25 도입 후] Overpass / Crossroads / T-Junction /",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField("                       ★ Y-Fork (F7!) / ★ CrossoverHub (F14!) / Bottleneck",
                EditorStyles.miniLabel);
        }

        private static void DrawSection_Tags(CaveNodeGraphBuilder gb)
        {
            var profiles = gb.lastPassageProfiles;
            if (profiles == null || profiles.Count == 0)
            {
                EditorGUILayout.LabelField("    (★ profile 없음)", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // 기존 6 tag (★ Phase 20 호환 표시)
            int tagNarrow = 0, tagWide = 0, tagVertical = 0, tagHorizontal = 0, tagSacred = 0, tagDanger = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                uint t = profiles[i].tags;
                if ((t & PassageMetric.TAG_NARROW) != 0) tagNarrow++;
                if ((t & PassageMetric.TAG_WIDE) != 0) tagWide++;
                if ((t & PassageMetric.TAG_VERTICAL) != 0) tagVertical++;
                if ((t & PassageMetric.TAG_HORIZONTAL) != 0) tagHorizontal++;
                if ((t & PassageMetric.TAG_SACRED) != 0) tagSacred++;
                if ((t & PassageMetric.TAG_DANGER) != 0) tagDanger++;
            }
            EditorGUILayout.LabelField($"    [Legacy 6 tags]", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"    Width: Narrow {tagNarrow} / Wide {tagWide}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"    Orient: Vertical {tagVertical} / Horizontal {tagHorizontal}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"    Char: ★Sacred {tagSacred} / ★Danger {tagDanger}", EditorStyles.miniLabel);

            // ─── ★ Phase 20 — Tag 4 Category (TerrainContextAnalyzer 활용) ───
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"    [★ Phase 20 — Tag 4 Category]", EditorStyles.miniLabel);
            DrawSection_Tag4Category();
        }

        // ═══════════════════════════════════════════════════════════════
        // [Phase 20] ★ Tag 4 Category 분리 패널
        // ═══════════════════════════════════════════════════════════════
        private static void DrawSection_Tag4Category()
        {
            var analyzer = FindFirstAnalyzer();
            if (analyzer == null)
            {
                EditorGUILayout.LabelField("    (★ TerrainContextAnalyzer 컴포넌트 씬에 추가 필요)",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // ★ Option C — Rebuild Now 버튼
            if (!analyzer.IsReady)
            {
                EditorGUILayout.LabelField("    (★ Rebuild required — analyzer cache 비어있음)",
                    EditorStyles.centeredGreyMiniLabel);
                if (GUILayout.Button("★ Rebuild Now", GUILayout.Height(24)))
                {
                    analyzer.Rebuild();
                }
                return;
            }

            // Ready 상태 — Rebuild 버튼 (선택 사항으로 작게)
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("★ Rebuild", GUILayout.Width(80), GUILayout.Height(18)))
                {
                    analyzer.Rebuild();
                }
            }

            // 모든 edge 의 ContextTagSet 합산
            int identityBoss = 0, identityTreasure = 0, identitySpawn = 0, identityHub = 0,
                identityFrontier = 0, identityDecision = 0, identityAmbush = 0;
            int stateNarrow = 0, stateStandard = 0, stateWide = 0, stateGrand = 0,
                stateBoundary = 0, stateMultiBiome = 0, stateCriticalRisk = 0;
            int eventReposition = 0, eventDesperate = 0, eventChamberLocked = 0,
                eventSub5m = 0, eventGuardActive = 0;
            int potAmbush = 0, potSacredVenue = 0, potRushDown = 0, potHiddenLoot = 0,
                potFallHazard = 0, potBottleneck = 0, potLostPlayer = 0;

            // Node 카운트
            try
            {
                var nodes = CaveNodeGraphBuilder.Instance?.nodesData;
                if (nodes != null)
                {
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        var t = analyzer.GetAllTags(i, ElementKind.Node);
                        if ((t.identityTags & (uint)IdentityTag.IDENTITY_BOSS) != 0) identityBoss++;
                        if ((t.identityTags & (uint)IdentityTag.IDENTITY_SPAWN) != 0) identitySpawn++;
                        if ((t.identityTags & (uint)IdentityTag.IDENTITY_TREASURE) != 0) identityTreasure++;
                        if ((t.identityTags & (uint)IdentityTag.IDENTITY_HUB) != 0) identityHub++;
                        if ((t.identityTags & (uint)IdentityTag.IDENTITY_FRONTIER) != 0) identityFrontier++;
                        if ((t.eventTags & (uint)EventTag.EVENT_CHAMBER_LOCKED) != 0) eventChamberLocked++;
                        if ((t.eventTags & (uint)EventTag.EVENT_REPOSITIONED) != 0) eventReposition++;
                        if ((t.eventTags & (uint)EventTag.EVENT_DESPERATELY_PLACED) != 0) eventDesperate++;
                    }
                }

                var edges = CaveNodeGraphBuilder.Instance?.edgesData;
                if (edges != null)
                {
                    for (int i = 0; i < edges.Count; i++)
                    {
                        var t = analyzer.GetAllTags(i, ElementKind.Passage);
                        if ((t.stateTags & (uint)StateTag.STATE_NARROW) != 0) stateNarrow++;
                        if ((t.stateTags & (uint)StateTag.STATE_STANDARD) != 0) stateStandard++;
                        if ((t.stateTags & (uint)StateTag.STATE_WIDE) != 0) stateWide++;
                        if ((t.stateTags & (uint)StateTag.STATE_GRAND) != 0) stateGrand++;
                        if ((t.stateTags & (uint)StateTag.STATE_BOUNDARY) != 0) stateBoundary++;
                        if ((t.stateTags & (uint)StateTag.STATE_MULTIBIOME) != 0) stateMultiBiome++;
                        if ((t.stateTags & (uint)StateTag.STATE_CRITICAL_RISK) != 0) stateCriticalRisk++;
                        if ((t.eventTags & (uint)EventTag.EVENT_SUB5M_VIOLATION) != 0) eventSub5m++;
                        if ((t.eventTags & (uint)EventTag.EVENT_GUARD_ACTIVE) != 0) eventGuardActive++;
                        if ((t.potentialTags & (uint)PotentialTag.POTENTIAL_AMBUSH) != 0) potAmbush++;
                        if ((t.potentialTags & (uint)PotentialTag.POTENTIAL_SACRED_VENUE) != 0) potSacredVenue++;
                        if ((t.potentialTags & (uint)PotentialTag.POTENTIAL_RUSH_DOWN) != 0) potRushDown++;
                        if ((t.potentialTags & (uint)PotentialTag.POTENTIAL_HIDDEN_LOOT) != 0) potHiddenLoot++;
                        if ((t.potentialTags & (uint)PotentialTag.POTENTIAL_FALL_HAZARD) != 0) potFallHazard++;
                        if ((t.potentialTags & (uint)PotentialTag.POTENTIAL_BOTTLENECK) != 0) potBottleneck++;
                        if ((t.potentialTags & (uint)PotentialTag.POTENTIAL_LOST_PLAYER) != 0) potLostPlayer++;
                    }
                }
            }
            catch { }

            EditorGUILayout.LabelField($"    ① IDENTITY (Role 미러)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"        Spawn:{identitySpawn}  Boss:{identityBoss}  Treasure:{identityTreasure}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"        Hub:{identityHub}  ★Frontier:{identityFrontier}",
                EditorStyles.miniLabel);

            EditorGUILayout.LabelField($"    ② STATE (현재 상태)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"        ★ Width: Narrow:{stateNarrow}  Standard:{stateStandard}  Wide:{stateWide}  Grand:{stateGrand}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"        Boundary:{stateBoundary}  ★MultiBiome:{stateMultiBiome}  CriticalRisk:{stateCriticalRisk}",
                EditorStyles.miniLabel);

            EditorGUILayout.LabelField($"    ③ EVENT (★ 어떤 일이 있었는가)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"        ChamberLocked:{eventChamberLocked} (★ N1)  Repositioned:{eventReposition} (★ R1)",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"        DesperatelyPlaced:{eventDesperate}  GuardActive:{eventGuardActive}",
                EditorStyles.miniLabel);
            if (eventSub5m > 0)
            {
                var oldColor = GUI.color;
                GUI.color = new Color(1f, 0.4f, 0.4f);
                EditorGUILayout.LabelField($"        ⚠ Sub5mViolation:{eventSub5m} (★ 5m 정책 위반)",
                    EditorStyles.miniLabel);
                GUI.color = oldColor;
            }

            EditorGUILayout.LabelField($"    ④ POTENTIAL (외부 추론용)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"        Ambush:{potAmbush}  Bottleneck:{potBottleneck}  SacredVenue:{potSacredVenue}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"        RushDown:{potRushDown}  HiddenLoot:{potHiddenLoot}  FallHazard:{potFallHazard}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"        LostPlayer:{potLostPlayer}", EditorStyles.miniLabel);
        }

        // ═══════════════════════════════════════════════════════════════
        // [Phase 21] ★ TerrainContextAnalyzer 캐싱
        // ═══════════════════════════════════════════════════════════════
        private static TerrainContextAnalyzer s_cachedAnalyzer = null;
        private static TerrainContextAnalyzer FindFirstAnalyzer()
        {
            if (s_cachedAnalyzer != null) return s_cachedAnalyzer;
#if UNITY_2023_1_OR_NEWER
            s_cachedAnalyzer = UnityEngine.Object.FindFirstObjectByType<TerrainContextAnalyzer>();
#else
            s_cachedAnalyzer = UnityEngine.Object.FindObjectOfType<TerrainContextAnalyzer>();
#endif
            return s_cachedAnalyzer;
        }

        private static void DrawSection_Aggregate(CaveNodeGraphBuilder gb)
        {
            var profiles = gb.lastPassageProfiles;
            if (profiles == null || profiles.Count == 0)
            {
                EditorGUILayout.LabelField("    (★ profile 없음)", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // Diversity Score = 1 - (max_type_count / total)
            var typeCounts = new System.Collections.Generic.Dictionary<PassageMetric.PassageJunctionType, int>();
            for (int i = 0; i < profiles.Count; i++)
            {
                var t = profiles[i].inferredType;
                if (!typeCounts.ContainsKey(t)) typeCounts[t] = 0;
                typeCounts[t]++;
            }
            int maxC = 0;
            foreach (var kv in typeCounts) if (kv.Value > maxC) maxC = kv.Value;
            float diversity = profiles.Count > 0 ? 1f - (float)maxC / profiles.Count : 0f;
            string divLabel = diversity > 0.6f ? "(HIGH)" : diversity > 0.3f ? "(MID)" : "(LOW)";

            // RiskScore 평균
            float avgRisk = 0f;
            int safeCount = 0, cautionCount = 0, criticalCount = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                var r = profiles[i].risk;
                avgRisk += r.total;
                if (r.level == PassageMetric.RiskLevel.Safe) safeCount++;
                else if (r.level == PassageMetric.RiskLevel.Caution) cautionCount++;
                else if (r.level == PassageMetric.RiskLevel.Critical) criticalCount++;
            }
            avgRisk /= Mathf.Max(1, profiles.Count);

            EditorGUILayout.LabelField($"    ★ Diversity Score: {diversity:F2} {divLabel}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"    ★ Risk Distribution: Safe {safeCount} / Caution {cautionCount} / Critical {criticalCount}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"    ★ Avg RiskScore: {avgRisk:F2}", EditorStyles.miniLabel);

            // 5m 정책 검증
            int sub5mCount = 0;
            for (int i = 0; i < profiles.Count; i++)
                if (profiles[i].inferredType == PassageMetric.PassageJunctionType.Sub5mEmergency) sub5mCount++;
            if (sub5mCount > 0)
            {
                var oldColor = GUI.color;
                GUI.color = new Color(1f, 0.4f, 0.4f);
                EditorGUILayout.LabelField($"    ⚠ ★ 5m 정책 위반 통로: {sub5mCount} ({100f * sub5mCount / profiles.Count:F0}%)",
                    EditorStyles.miniLabel);
                GUI.color = oldColor;
            }
            else
            {
                EditorGUILayout.LabelField($"    ✓ 5m 정책 100% 준수", EditorStyles.miniLabel);
            }
        }
    }
}
#endif
