#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Gate 5 Phase 4.5-B] GraphPreviewWindow — 메인 EditorWindow
    //
    // Window > Cave System > Graph Preview Editor
    //
    // 섹션:
    //   1. Inputs (Biomes + SO slots + Graph params)
    //   2. Feature Toggles (on/off)
    //   3. Action Buttons (Generate, Compare, Clear)
    //   4. Statistics (Graph stats, SO impact, Biome dist)
    //   5. Extensions (Phase 5+ IPreviewExtension 여기 등록)
    //
    // Scene View 오버레이는 PreviewSceneRenderer가 담당.
    // ══════════════════════════════════════════════════════════════════════

    public class GraphPreviewWindow : EditorWindow
    {
        // 상태
        private PreviewInputs _inputs = new PreviewInputs();
        private PreviewData _lastResult;
        private PreviewData _baseline;

        // [Phase 4.5-D 제안 6] Sweep 결과 보관
        private SeedSweepResult _lastSweepResult;

        // [Phase 4.5-D 제안 8] Realtime watcher 구독 여부
        private bool _realtimeSubscribed = false;

        // [Phase 4.5-E] MC Preview mesh renderer (기본 inactive - enableMCPreview 토글 따라 활성화)
        private PreviewMeshRenderer _mcRenderer = new PreviewMeshRenderer();
        private Vector2 _scrollPos;

        // [Phase 4.5-B hotfix] 수동 로딩용 직접 참조
        private CaveBiomeSettings _manualBiomeSettings;

        // Extensions 등록 (Phase 5+에서 확장)
        private List<IPreviewExtension> _extensions = new List<IPreviewExtension>();

        [MenuItem("Window/Cave System/Graph Preview Editor")]
        public static void ShowWindow()
        {
            var w = GetWindow<GraphPreviewWindow>("Graph Preview");
            w.minSize = new Vector2(450f, 600f);
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            RegisterExtensions();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            PreviewGraphGenerator.CleanupStrayBuilders();
            // [Phase 4.5-D 제안 8] Realtime watcher 정리
            if (_realtimeSubscribed)
            {
                PreviewRealtimeWatcher.UnregisterCallback();
                _realtimeSubscribed = false;
            }
            // [Phase 4.5-E] MC mesh 리소스 정리
            _mcRenderer?.Cleanup();
        }

        /// <summary>
        /// Phase 5+에서 신규 IPreviewExtension 구현을 여기에 등록.
        /// 현재는 빈 상태 (Phase 4.5-B MVP).
        /// </summary>
        private void RegisterExtensions()
        {
            _extensions.Clear();
            // Phase 5 B.8에서 추가 예정:
            //   _extensions.Add(new SubBiomePreviewExtension());
        }

        // ──────────────────────────────────────────────────────────────
        // [Phase 4.5-B hotfix v2] UI 상태 - Foldout
        private bool _foldInputs = true;
        private bool _foldQuickActions = true;
        private bool _foldDisplay = true;
        private bool _foldDetailedToggles = false;  // 기본 접힘
        private bool _foldStats = true;
        private bool _foldExtensions = false;

        // 현재 선택된 탭
        private int _selectedTab = 0;
        private readonly string[] _tabNames = { "Setup", "Display", "Stats" };

        // ──────────────────────────────────────────────────────────────
        // Main GUI
        // ──────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // ═════ 제목 + 상태 표시줄 ═════
            DrawHeader();

            // ═════ 주요 액션 (항상 위에) ═════
            DrawQuickActions();
            EditorGUILayout.Space(6);

            // ═════ 탭 기반 섹션 ═════
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames, GUILayout.Height(24));
            EditorGUILayout.Space(4);

            switch (_selectedTab)
            {
                case 0: DrawSetupTab(); break;
                case 1: DrawDisplayTab(); break;
                case 2: DrawStatsTab(); break;
            }

            EditorGUILayout.EndScrollView();
            SceneView.RepaintAll();
        }

        // ──── 제목/상태 헤더 ────
        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            GUIStyle title = new GUIStyle(EditorStyles.boldLabel);
            title.fontSize = 13;
            EditorGUILayout.LabelField("🗺 Graph Preview Editor", title);

            // 상태 표시 (마지막 생성 시각)
            GUIStyle right = new GUIStyle(EditorStyles.miniLabel);
            right.alignment = TextAnchor.MiddleRight;
            string status = _lastResult != null
                ? $"Last: {_lastResult.generationTime:HH:mm:ss} ({_lastResult.nodes.Count}N/{_lastResult.edges.Count}E)"
                : "Not generated";
            EditorGUILayout.LabelField(status, right, GUILayout.Width(200));

            EditorGUILayout.EndHorizontal();

            // 요약 정보 바
            if (_lastResult != null)
            {
                var s = _lastResult.stats;
                EditorGUILayout.LabelField(
                    $"Spawn:{s.spawnCount}  Boss:{s.bossCount}  Treasure:{s.treasureCount}  " +
                    $"Sinkhole:{s.sinkholeCount}  Normal:{s.normalCount}   " +
                    $"|  Min W: {s.minEdgeWidth:F1}m   Max Slope: {s.maxEdgeSlope:F0}°",
                    EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        // ──── 주요 액션 버튼 (항상 상단) ────
        private void DrawQuickActions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
            if (GUILayout.Button("⚡ Generate Preview", GUILayout.Height(32)))
            {
                GenerateMain();
            }
            GUI.backgroundColor = Color.white;

            // Seed Randomize
            if (GUILayout.Button("🎲 Random Seed", GUILayout.Width(110), GUILayout.Height(32)))
            {
                _inputs.seed = Random.Range(1, int.MaxValue);
                GenerateMain();
            }

            GUI.enabled = (_lastResult != null);
            if (GUILayout.Button("✖ Clear", GUILayout.Width(70), GUILayout.Height(32)))
            {
                _lastResult = null;
                _baseline = null;
                _inputs.expandedNodeIndices.Clear();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            // Compare 모드 (토글 ON 시만 표시)
            if ((_inputs.flags & FeatureFlags.CompareMode) != 0)
            {
                if (GUILayout.Button("📊 Compare Before/After (baseline vs with SOs)"))
                {
                    GenerateCompare();
                }
            }

            // [Phase 4.5-C] Export Stats 버튼
            if (_lastResult != null)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("📄 Export Stats (CSV)", GUILayout.Height(22)))
                {
                    ExportStatsCSV();
                }
                if (GUILayout.Button("📋 Export Stats (JSON)", GUILayout.Height(22)))
                {
                    ExportStatsJSON();
                }
                EditorGUILayout.EndHorizontal();
            }

            // Seed 표시 + 변경
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Seed:", GUILayout.Width(40));
            _inputs.seed = EditorGUILayout.IntField(_inputs.seed, GUILayout.Width(100));
            EditorGUILayout.LabelField($"(Ctrl+R로 랜덤)", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ──── Setup 탭 ────
        private void DrawSetupTab()
        {
            _foldInputs = EditorGUILayout.BeginFoldoutHeaderGroup(_foldInputs, "📁 Input Source");
            if (_foldInputs)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawInputsSection();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // Graph parameters — 별도 foldout
            _foldQuickActions = EditorGUILayout.BeginFoldoutHeaderGroup(_foldQuickActions, "⚙ Graph Parameters");
            if (_foldQuickActions)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawGraphParamsSection();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ──── Display 탭 ────
        private void DrawDisplayTab()
        {
            _foldDisplay = EditorGUILayout.BeginFoldoutHeaderGroup(_foldDisplay, "👁 Display Essentials");
            if (_foldDisplay)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawEssentialDisplayToggles();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            _foldDetailedToggles = EditorGUILayout.BeginFoldoutHeaderGroup(_foldDetailedToggles,
                "🎛 Advanced Toggles");
            if (_foldDetailedToggles)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawAdvancedToggles();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ──── Stats 탭 ────
        private void DrawStatsTab()
        {
            _foldStats = EditorGUILayout.BeginFoldoutHeaderGroup(_foldStats, "📈 Statistics");
            if (_foldStats)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawStatisticsSection();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            _foldExtensions = EditorGUILayout.BeginFoldoutHeaderGroup(_foldExtensions,
                "🔌 Extensions (Phase 5+)");
            if (_foldExtensions)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawExtensionsSection();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ──── Inputs (소스 선택) ────
        private void DrawInputsSection()
        {
            // 두 가지 loading 방식 제공
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.5f, 0.9f, 0.5f);
            if (GUILayout.Button("🔄 Auto-load from Scene", GUILayout.Height(22)))
            {
                AutoLoadFromScene();
            }
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("Clear", GUILayout.Width(60), GUILayout.Height(22)))
            {
                _inputs = new PreviewInputs();
            }
            EditorGUILayout.EndHorizontal();

            // Manual 직접 할당
            EditorGUILayout.BeginHorizontal();
            _manualBiomeSettings = (CaveBiomeSettings)EditorGUILayout.ObjectField(
                "Manual BiomeSettings", _manualBiomeSettings, typeof(CaveBiomeSettings), false);
            GUI.enabled = (_manualBiomeSettings != null);
            if (GUILayout.Button("→ Load", GUILayout.Width(60)))
            {
                LoadFromBiomeSettings(_manualBiomeSettings);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Biomes 리스트 (컴팩트)
            int bCount = _inputs.biomes?.Count ?? 0;
            EditorGUILayout.LabelField($"Biomes ({bCount}):", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            for (int i = 0; i < bCount; i++)
            {
                EditorGUILayout.BeginHorizontal();
                _inputs.biomes[i] = (CaveBiomeData)EditorGUILayout.ObjectField(
                    $"[{i}]", _inputs.biomes[i], typeof(CaveBiomeData), false);
                if (GUILayout.Button("✖", GUILayout.Width(22))) { _inputs.biomes.RemoveAt(i); bCount--; i--; }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ Add Biome", GUILayout.Height(18)))
            {
                _inputs.biomes.Add(null);
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(4);

            // SO slots + 개별 on/off (컴팩트)
            EditorGUILayout.LabelField("SO Modifiers:", EditorStyles.miniBoldLabel);
            DrawSOSlot<RoomGeometryMatrixSO>("Room Matrix", ref _inputs.matrix, ref _inputs.matrixEnabled);
            DrawSOSlot<EdgeConstraintSO>("Edge Constraint", ref _inputs.constraint, ref _inputs.constraintEnabled);
            DrawSOSlot<CardinalBiomeBiasSO>("Cardinal Bias", ref _inputs.cardinalBias, ref _inputs.cardinalBiasEnabled);
        }

        private void DrawSOSlot<T>(string label, ref T slot, ref bool enabled) where T : Object
        {
            EditorGUILayout.BeginHorizontal();
            slot = (T)EditorGUILayout.ObjectField(label, slot, typeof(T), false);
            GUI.backgroundColor = enabled ? Color.green : Color.gray;
            enabled = GUILayout.Toggle(enabled, enabled ? "ON" : "OFF", "Button",
                GUILayout.Width(42), GUILayout.Height(18));
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        // ──── Graph Params (별도 foldout) ────
        private int _selectedPresetIdx = -1;  // -1 = 선택 없음

        private void DrawGraphParamsSection()
        {
            // [Phase 4.5-D 제안 7] Preset Library dropdown
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("🎛 Preset:", GUILayout.Width(60));
            string[] presetNames = new string[PreviewPresets.All.Count + 1];
            presetNames[0] = "(none — custom)";
            for (int i = 0; i < PreviewPresets.All.Count; i++)
                presetNames[i + 1] = PreviewPresets.All[i].name;

            int newIdx = EditorGUILayout.Popup(_selectedPresetIdx + 1, presetNames) - 1;
            if (newIdx != _selectedPresetIdx && newIdx >= 0)
            {
                PreviewPresets.ApplyPreset(_inputs, PreviewPresets.All[newIdx]);
                _selectedPresetIdx = newIdx;
                Debug.Log($"[GraphPreview] Preset applied: {PreviewPresets.All[newIdx].name}");
            }
            else if (newIdx < 0)
            {
                _selectedPresetIdx = -1;
            }
            EditorGUILayout.EndHorizontal();

            // Preset 설명
            if (_selectedPresetIdx >= 0 && _selectedPresetIdx < PreviewPresets.All.Count)
            {
                EditorGUILayout.HelpBox(
                    PreviewPresets.All[_selectedPresetIdx].description, MessageType.None);
            }

            EditorGUILayout.Space(4);

            _inputs.macroBiomeScale = EditorGUILayout.FloatField("Macro Biome Scale", _inputs.macroBiomeScale);
            _inputs.dungeonBounds = EditorGUILayout.Vector3Field("Dungeon Bounds", _inputs.dungeonBounds);
            _inputs.targetRoomCount = EditorGUILayout.IntSlider("Target Room Count",
                _inputs.targetRoomCount, 5, 80);

            EditorGUILayout.BeginHorizontal();
            _inputs.mainPathWidth = EditorGUILayout.Slider("Main Path Width",
                _inputs.mainPathWidth, 2f, 20f);
            EditorGUILayout.EndHorizontal();
            _inputs.sidePathWidth = EditorGUILayout.Slider("Side Path Width",
                _inputs.sidePathWidth, 2f, 20f);

            EditorGUILayout.Space(2);
            _inputs.minRoomRadius = EditorGUILayout.Slider("Min Room Radius",
                _inputs.minRoomRadius, 2f, 30f);
            _inputs.maxRoomRadius = EditorGUILayout.Slider("Max Room Radius",
                _inputs.maxRoomRadius, 2f, 40f);
        }

        // ──── Essential Display (기본 토글) ────
        private void DrawEssentialDisplayToggles()
        {
            // [Q2] 실제 사이즈 토글 — 강조
            GUI.backgroundColor = _inputs.showRealScale ? new Color(1f, 0.8f, 0.4f) : Color.white;
            _inputs.showRealScale = EditorGUILayout.ToggleLeft(
                "📏 Show Real Scale (노드/엣지 실제 크기)", _inputs.showRealScale);
            GUI.backgroundColor = Color.white;
            EditorGUILayout.HelpBox(
                _inputs.showRealScale
                    ? "ON: Node의 실제 radius, Edge의 실제 width를 wireframe으로 표시 (실제 게임 스케일)"
                    : "OFF: 축소 gizmo로 표시 (개요 파악 용이)",
                MessageType.None);

            EditorGUILayout.Space(4);

            // [Q4] Node 정보 패널
            _inputs.showNodeInfoPanel = EditorGUILayout.ToggleLeft(
                "🔖 Show Node Info Panel (클릭 접힘/펼침)", _inputs.showNodeInfoPanel);

            // [Q1 hotfix v3] Normal 노드도 Info Panel 포함
            if (_inputs.showNodeInfoPanel)
            {
                EditorGUI.indentLevel++;
                _inputs.showNormalNodeInfo = EditorGUILayout.ToggleLeft(
                    "└ Include Normal Rooms (작은 폰트)", _inputs.showNormalNodeInfo);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // Core 시각화
            EditorGUILayout.LabelField("Visualization:", EditorStyles.miniBoldLabel);
            DrawFlag(FeatureFlags.ShowNodes, "Nodes (roomType 색상)");
            DrawFlag(FeatureFlags.ShowEdges, "Edges (curvature 두께)");

            // [Phase 4.5-F hotfix v4] Edges Display Mode — Line/CapsuleWire/MeshOutline
            bool edgesEnabled = (_inputs.flags & FeatureFlags.ShowEdges) != 0;
            if (edgesEnabled)
            {
                EditorGUI.indentLevel++;
                _inputs.edgeDisplayMode = (EdgeDisplayMode)EditorGUILayout.EnumPopup(
                    "└ Display Mode", _inputs.edgeDisplayMode);
                if (_inputs.edgeDisplayMode == EdgeDisplayMode.MeshOutline)
                {
                    // [v6] Graph 근접 필터
                    _inputs.edgeMeshFilterByGraph = EditorGUILayout.ToggleLeft(
                        "└ Filter by Graph (파편 제거)", _inputs.edgeMeshFilterByGraph);

                    // [v6] tolerance 범위 확장
                    float newTol = EditorGUILayout.Slider(
                        "└ Simplify Tol (m)", _inputs.edgeMeshSimplifyTolerance, 0.05f, 5.0f);
                    if (!Mathf.Approximately(newTol, _inputs.edgeMeshSimplifyTolerance))
                    {
                        _inputs.edgeMeshSimplifyTolerance = newTol;
                        EdgeMeshOutlineExtractor.ForceRebuild(
                            _inputs.edgeMeshSimplifyTolerance,
                            _inputs.edgeMeshFilterByGraph,
                            _lastResult?.nodes, _lastResult?.edges);
                    }
                    if (GUILayout.Button("└ Rebuild Outline", GUILayout.Height(18)))
                    {
                        EdgeMeshOutlineExtractor.ForceRebuild(
                            _inputs.edgeMeshSimplifyTolerance,
                            _inputs.edgeMeshFilterByGraph,
                            _lastResult?.nodes, _lastResult?.edges);
                    }
                    EditorGUILayout.HelpBox(
                        "Runtime DC mesh에서 boundary/silhouette edge 추출.\n" +
                        "Play mode에서만 의미 있음 (Edit mode는 mesh 없음).\n" +
                        "Simplify 높이면 에디터 안정적 (기본 1.0m).",
                        MessageType.None);
                }
                EditorGUI.indentLevel--;
            }
            DrawFlag(FeatureFlags.ShowBiomeHeatmap, "Biome Heatmap");
            DrawFlag(FeatureFlags.ShowMatrixAura, "Matrix Aura (적용 node 링)");
            DrawFlag(FeatureFlags.ShowViolations, "Violations (빨간 엣지)");

            EditorGUILayout.Space(4);

            // [Q3] Biome Divide — 강조 (새 기능)
            EditorGUILayout.LabelField("🗺 Biome Divide (경계/라벨):", EditorStyles.miniBoldLabel);
            _inputs.showBiomeBoundary = EditorGUILayout.ToggleLeft(
                "Show Biome Boundary (외곽선 contour)", _inputs.showBiomeBoundary);

            // [v5-1 hotfix] Boundary 색상 옵션
            if (_inputs.showBiomeBoundary)
            {
                EditorGUI.indentLevel++;
                _inputs.biomeBoundaryColored = EditorGUILayout.ToggleLeft(
                    "└ Colored by Biome (biome 색 혼합, 권장)",
                    _inputs.biomeBoundaryColored);

                // [v5-1] Blend Transition Zone 표시 토글
                _inputs.showBlendTransitionZone = EditorGUILayout.ToggleLeft(
                    "└ Show Blend Transition Zone (전이 영역 노란 overlay)",
                    _inputs.showBlendTransitionZone);
                EditorGUI.indentLevel--;
            }

            _inputs.showBiomeLabels = EditorGUILayout.ToggleLeft(
                "Show Biome Name Labels (영역별 이름)", _inputs.showBiomeLabels);

            // [v4 hotfix] Biome 라벨 클릭 → 포함 노드 리스트 펼침
            if (_inputs.showBiomeLabels)
            {
                EditorGUI.indentLevel++;
                _inputs.showBiomeNodeListPanel = EditorGUILayout.ToggleLeft(
                    "└ Clickable (클릭 시 포함 노드 리스트 펼침)",
                    _inputs.showBiomeNodeListPanel);
                EditorGUI.indentLevel--;
            }

            _inputs.showBlendWeightValue = EditorGUILayout.ToggleLeft(
                "Show Blend Weight Values (경계 셀 수치)", _inputs.showBlendWeightValue);

            // [v5-2 hotfix] Per-biome blend extension 설정
            if (_inputs.showBlendTransitionZone)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Per-Biome Blend Extension:", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox(
                    "각 biome별 전이 영역 폭 조정. 0.2(기본)=전이영역[0.3, 0.7]. 큰 값일수록 넓은 전이영역.",
                    MessageType.None);

                EditorGUI.indentLevel++;

                // Default
                _inputs.defaultBlendExtension = EditorGUILayout.Slider("Default",
                    _inputs.defaultBlendExtension, 0.05f, 0.5f);

                // 현재 등록된 biome들 개별 설정
                if (_inputs.biomes != null)
                {
                    HashSet<int> seenTypes = new HashSet<int>();
                    foreach (var biome in _inputs.biomes)
                    {
                        if (biome == null) continue;
                        int nt = biome.noiseType;
                        if (seenTypes.Contains(nt)) continue;
                        seenTypes.Add(nt);

                        string name = BiomeSampler.GetBiomeName(nt);
                        float current = _inputs.perBiomeBlendExtension.TryGetValue(nt, out float v)
                            ? v : _inputs.defaultBlendExtension;

                        EditorGUILayout.BeginHorizontal();
                        float newVal = EditorGUILayout.Slider(name, current, 0.05f, 0.5f);
                        if (GUILayout.Button("↺", GUILayout.Width(22)))  // 기본값 리셋
                        {
                            _inputs.perBiomeBlendExtension.Remove(nt);
                        }
                        else if (Mathf.Abs(newVal - current) > 0.001f)
                        {
                            _inputs.perBiomeBlendExtension[nt] = newVal;
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // [Q4 hotfix v3] Balanced Biomes — Preview 전용 균등화
            EditorGUILayout.LabelField("⚖ Biome Balancing (Preview 전용):", EditorStyles.miniBoldLabel);
            GUI.backgroundColor = _inputs.balanceBiomes ? new Color(0.8f, 0.8f, 1f) : Color.white;
            _inputs.balanceBiomes = EditorGUILayout.ToggleLeft(
                "🎚 Show Balanced Biomes (Perlin 중앙 bias 보정)", _inputs.balanceBiomes);
            GUI.backgroundColor = Color.white;
            EditorGUILayout.HelpBox(
                _inputs.balanceBiomes
                    ? "ON: 각 biome 대략 1/N 비율로 균등화 (Preview 전용, shader는 불변). " +
                      "Shader 실제 결과는 중앙 biome이 압도 (Phase 5 개선 예정)."
                    : "OFF: 실제 shader와 일치 (Perlin 중앙 bias → 중앙 biome 압도). " +
                      "Columnar 88% 같은 불균형이 정상 결과.",
                MessageType.None);
        }

        // ──── Advanced Toggles ────
        private void DrawAdvancedToggles()
        {
            EditorGUILayout.LabelField("Extended Features:", EditorStyles.miniBoldLabel);
            DrawFlag(FeatureFlags.ShowBlendWeight, "Blend Weight Fade (경계 흐림)");
            DrawFlag(FeatureFlags.ShowCardinalVec, "Cardinal Bias Arrows");
            DrawFlag(FeatureFlags.ShowRoomLabels, "All Room Type Labels (Normal 포함)");
            DrawFlag(FeatureFlags.CompareMode, "Compare Mode (Before/After)");
            DrawFlag(FeatureFlags.ShowBiomeDist, "Biome Distribution Chart");

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("제안 기능:", EditorStyles.miniBoldLabel);
            DrawFlag(FeatureFlags.ShowCriticalPath, "Critical Path (제안 1, Phase 4.5-C)");
            DrawFlag(FeatureFlags.ShowEdgeDirection, "Edge Direction (제안 2)");
            DrawFlag(FeatureFlags.ShowPlayability, "Playability Summary (제안 5)");

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Phase 4.5-D (활성화됨):", EditorStyles.miniBoldLabel);
            DrawFlag(FeatureFlags.ShowNodeClustering, "🔥 Node Clustering (제안 3) — 밀집도 heatmap");
            DrawFlag(FeatureFlags.ShowCrossSection, "✂ Cross-Section (제안 4) — XY/YZ 단면");
            DrawFlag(FeatureFlags.ShowSeedSweep, "🎲 Seed Sensitivity (제안 6) — Sweep 통계");

            // Cross-Section 세부 파라미터
            if ((_inputs.flags & FeatureFlags.ShowCrossSection) != 0)
            {
                EditorGUI.indentLevel++;
                _inputs.crossAxis = (CrossSectionAxis)EditorGUILayout.EnumPopup(
                    "Axis", _inputs.crossAxis);
                if (_inputs.crossAxis != CrossSectionAxis.None)
                {
                    float range = _inputs.crossAxis == CrossSectionAxis.XY
                        ? _inputs.dungeonBounds.z * 0.5f
                        : _inputs.dungeonBounds.x * 0.5f;
                    _inputs.crossPlaneValue = EditorGUILayout.Slider(
                        _inputs.crossAxis == CrossSectionAxis.XY ? "Z value" : "X value",
                        _inputs.crossPlaneValue, -range, range);
                }
                EditorGUI.indentLevel--;
            }

            // Seed Sweep 실행 버튼
            if ((_inputs.flags & FeatureFlags.ShowSeedSweep) != 0)
            {
                EditorGUI.indentLevel++;
                _inputs.seedSweepCount = EditorGUILayout.IntSlider("Sweep Count",
                    _inputs.seedSweepCount, 2, PreviewGraphGenerator.MAX_SWEEP_COUNT);
                if (GUILayout.Button($"🎲 Run Seed Sweep ({_inputs.seedSweepCount} seeds)"))
                {
                    RunSeedSweep();
                }
                EditorGUI.indentLevel--;
            }

            // [Phase 4.5-D 제안 8] Realtime SO Inspector
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("⚡ Auto-Regenerate:", EditorStyles.miniBoldLabel);
            bool wasRealtime = _inputs.realtimeSOInspector;
            _inputs.realtimeSOInspector = EditorGUILayout.ToggleLeft(
                "Realtime SO Inspector Preview (제안 8) — 150ms debounce",
                _inputs.realtimeSOInspector);
            // 토글 상태 변경 시 watcher 구독/해제
            if (wasRealtime != _inputs.realtimeSOInspector)
            {
                UpdateRealtimeWatcher();
            }
            if (_inputs.realtimeSOInspector)
            {
                EditorGUILayout.HelpBox(
                    "SO 저장 시 (Ctrl+S 또는 Inspector 편집 완료) 자동으로 Generate.\n" +
                    "Debounce 150ms — 슬라이더 드래그 중엔 재생성 안 함.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(6);
            DrawMCPreviewSection();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Heatmap Settings:", EditorStyles.miniBoldLabel);
            _inputs.heatmapGridSize = EditorGUILayout.Slider("Grid Size (m)",
                _inputs.heatmapGridSize, 2f, 50f);
            _inputs.heatmapOpacity = EditorGUILayout.Slider("Opacity",
                _inputs.heatmapOpacity, 0.1f, 0.8f);
            _inputs.heatmapYPlane = EditorGUILayout.Slider("Y Plane (m)",
                _inputs.heatmapYPlane, -25f, 25f);
        }

        // ══════════════════════════════════════════════════════════════════
        // [Phase 4.5-E] MC Preview UI
        //   enableMCPreview 토글이 master — OFF면 모든 기능 비활성화
        //   ON일 때만 세부 파라미터 표시
        // ══════════════════════════════════════════════════════════════════
        private void DrawMCPreviewSection()
        {
            EditorGUILayout.LabelField("🧊 MC SDF Preview Mesh:", EditorStyles.miniBoldLabel);

            // Master toggle — 강조 색상
            GUI.backgroundColor = _inputs.enableMCPreview ? new Color(1f, 0.7f, 0.3f) : Color.white;
            _inputs.enableMCPreview = EditorGUILayout.ToggleLeft(
                "Enable MC Preview (Phase 4.5-E, OFF 시 영향 없음)",
                _inputs.enableMCPreview);
            GUI.backgroundColor = Color.white;

            if (!_inputs.enableMCPreview)
            {
                EditorGUILayout.HelpBox(
                    "OFF: 기존 시스템과 100% 동일 동작. MC mesh 리소스 생성 안 됨.\n" +
                    "ON: SDF 샘플링 + Marching Cubes로 mesh 생성 → Scene에 wireframe 표시.",
                    MessageType.None);
                return;
            }

            // ─── 상세 파라미터 (ON일 때만) ───
            EditorGUI.indentLevel++;

            // [hotfix v2] Render mode
            _inputs.mcRenderMode = (PreviewMCRenderMode)EditorGUILayout.EnumPopup(
                "Render Mode", _inputs.mcRenderMode);

            // Range mode
            _inputs.mcRangeMode = (PreviewMeshRangeMode)EditorGUILayout.EnumPopup(
                "Range Mode", _inputs.mcRangeMode);

            if (_inputs.mcRangeMode == PreviewMeshRangeMode.CustomRadius)
            {
                _inputs.mcCustomRadius = EditorGUILayout.Slider(
                    "Custom Radius (m)", _inputs.mcCustomRadius, 10f, 200f);
            }

            // Voxel size
            _inputs.mcVoxelSize = EditorGUILayout.Slider(
                "Voxel Size (m)", _inputs.mcVoxelSize, 0.2f, 5f);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("SDF Parameters:", EditorStyles.miniBoldLabel);
            _inputs.mcSminStrength = EditorGUILayout.Slider(
                "Smin Strength", _inputs.mcSminStrength, 0.5f, 8f);
            _inputs.mcFloorAltitude = EditorGUILayout.FloatField(
                "Floor Altitude", _inputs.mcFloorAltitude);
            _inputs.mcCeilAltitude = EditorGUILayout.FloatField(
                "Ceil Altitude", _inputs.mcCeilAltitude);
            _inputs.mcFloorVarAmp = EditorGUILayout.Slider(
                "Floor Var Amp", _inputs.mcFloorVarAmp, 0f, 8f);
            _inputs.mcCeilVarAmp = EditorGUILayout.Slider(
                "Ceil Var Amp", _inputs.mcCeilVarAmp, 0f, 8f);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("v1 Features:", EditorStyles.miniBoldLabel);
            _inputs.mcEnableCurvature = EditorGUILayout.ToggleLeft(
                "Enable Curvature", _inputs.mcEnableCurvature);
            _inputs.mcEnableFloorVar = EditorGUILayout.ToggleLeft(
                "Enable Floor/Ceil Variation", _inputs.mcEnableFloorVar);
            _inputs.mcEnableBiomeBlend = EditorGUILayout.ToggleLeft(
                "Enable Biome Blend", _inputs.mcEnableBiomeBlend);

            // [hotfix v2] SO 파라미터 반영 현황
            EditorGUILayout.HelpBox(
                "반영 (v1):\n" +
                "  ✓ Edge.curvatureAmp → Curvature 토글로 제어\n" +
                "  ✓ Edge.width, Node.radius → sdCapsule/sdSphere\n" +
                "  ✓ RoomGeometryMatrix / EdgeConstraint / CardinalBias → graph 생성 단계 반영\n" +
                "  ✓ CaveBiomeData.floorVarAmp/ceilVarAmp (존재 시) → biome blend\n" +
                "  ✓ macroBiomeScale, balanceBiomes → biome sampling\n" +
                "\n" +
                "v2 이월 (미반영):\n" +
                "  ✗ Stalactite / SedimentLayer / Sinkhole shaft\n" +
                "  ✗ Primitive routing (Phase 4 E-α)\n" +
                "  ✗ Biome별 sminStrength/floorAltitude 개별 값",
                MessageType.Info);

            EditorGUILayout.Space(2);

            // [hotfix v5] Noise 모드 3종 선택 — ShaderCompat가 기본 (runtime 일치)
            BiomeSampler.NoiseMode origMode = BiomeSampler.CurrentNoiseMode;
            var newMode = (BiomeSampler.NoiseMode)EditorGUILayout.EnumPopup(
                "Noise Mode", BiomeSampler.CurrentNoiseMode);
            if (newMode != origMode)
            {
                BiomeSampler.CurrentNoiseMode = newMode;
                GenerateMain();  // noise 변경 시 전체 재생성
            }
            EditorGUILayout.HelpBox(
                "ShaderCompat = Runtime shader와 100% 일치 (권장). " +
                "Simplex = 표준. Perlin = 근사.",
                MessageType.None);

            // 생성 통계 표시
            if (_mcRenderer != null && _mcRenderer.LastVertexCount > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    $"Last gen: {_mcRenderer.LastVertexCount:N0} verts, " +
                    $"{_mcRenderer.LastTriangleCount:N0} tris, " +
                    $"{_mcRenderer.LastGenerateMs:F1}ms",
                    MessageType.None);
            }

            EditorGUI.indentLevel--;
        }

        private void DrawFlag(FeatureFlags flag, string label)
        {
            bool was = (_inputs.flags & flag) != 0;
            bool now = EditorGUILayout.Toggle(label, was);
            if (now != was)
            {
                if (now) _inputs.flags |= flag;
                else _inputs.flags &= ~flag;
            }
        }

        // DrawActionsSection 제거됨 — DrawQuickActions (헤더 영역)에 흡수

        private void GenerateMain()
        {
            _lastResult = PreviewGraphGenerator.Generate(_inputs, _extensions, useSOs: true);
            _baseline = null;
            SceneView.RepaintAll();
        }

        private void GenerateCompare()
        {
            _baseline = PreviewGraphGenerator.Generate(_inputs, _extensions, useSOs: false);
            _lastResult = PreviewGraphGenerator.Generate(_inputs, _extensions, useSOs: true);
            SceneView.RepaintAll();
        }

        // ──────────────────────────────────────────────────────────────
        // [Phase 4.5-D 제안 6] Seed Sweep 실행
        // ──────────────────────────────────────────────────────────────
        private void RunSeedSweep()
        {
            _lastSweepResult = PreviewGraphGenerator.Sweep(
                _inputs, _inputs.seedSweepCount, _extensions);
            // 마지막 seed의 결과로 렌더 유지
            if (_lastSweepResult != null)
            {
                Debug.Log($"[GraphPreview] Sweep 완료 — {_inputs.seedSweepCount} seeds 분석");
            }
            SceneView.RepaintAll();
        }

        // ──────────────────────────────────────────────────────────────
        // [Phase 4.5-D 제안 8] Realtime watcher 구독 관리
        // ──────────────────────────────────────────────────────────────
        private void UpdateRealtimeWatcher()
        {
            if (_inputs.realtimeSOInspector && !_realtimeSubscribed)
            {
                PreviewRealtimeWatcher.RegisterCallback(() =>
                {
                    Debug.Log("[GraphPreview] Realtime SO Inspector trigger → Generate");
                    GenerateMain();
                    Repaint();
                });
                _realtimeSubscribed = true;
            }
            else if (!_inputs.realtimeSOInspector && _realtimeSubscribed)
            {
                PreviewRealtimeWatcher.UnregisterCallback();
                _realtimeSubscribed = false;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // [Phase 4.5-C] Export Stats — CSV / JSON
        // ──────────────────────────────────────────────────────────────

        private void ExportStatsCSV()
        {
            if (_lastResult == null) return;
            string path = EditorUtility.SaveFilePanel(
                "Export Preview Stats (CSV)", "",
                $"preview_stats_seed{_inputs.seed}.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;

            var sb = new System.Text.StringBuilder();
            var s = _lastResult.stats;

            sb.AppendLine("Section,Key,Value");
            sb.AppendLine($"Graph,Seed,{_inputs.seed}");
            sb.AppendLine($"Graph,GenerationTime,{_lastResult.generationTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Graph,DungeonBounds,\"{_inputs.dungeonBounds}\"");
            sb.AppendLine($"Graph,NodeCount,{s.nodeCount}");
            sb.AppendLine($"Graph,EdgeCount,{s.edgeCount}");
            sb.AppendLine($"Graph,SpawnCount,{s.spawnCount}");
            sb.AppendLine($"Graph,BossCount,{s.bossCount}");
            sb.AppendLine($"Graph,TreasureCount,{s.treasureCount}");
            sb.AppendLine($"Graph,SinkholeCount,{s.sinkholeCount}");
            sb.AppendLine($"Graph,NormalCount,{s.normalCount}");
            sb.AppendLine($"Graph,MinEdgeWidth,{s.minEdgeWidth:F2}");
            sb.AppendLine($"Graph,MaxEdgeSlope,{s.maxEdgeSlope:F1}");
            sb.AppendLine($"Matrix,AppliedNodes,{s.matrixAppliedNodeCount}");
            sb.AppendLine($"Constraint,ViolationLength,{s.violationLength}");
            sb.AppendLine($"Constraint,ViolationSlope,{s.violationSlope}");
            sb.AppendLine($"Constraint,ViolationDirection,{s.violationDirection}");

            // Biome distribution
            foreach (var kv in s.biomeAreaRatio)
            {
                sb.AppendLine($"BiomeRatio,{BiomeSampler.GetBiomeName(kv.Key)},{kv.Value:F4}");
            }

            // Per-node
            sb.AppendLine();
            sb.AppendLine("NodeIndex,Type,PositionX,PositionY,PositionZ,Radius,MatrixApplied");
            for (int i = 0; i < _lastResult.nodes.Count; i++)
            {
                var n = _lastResult.nodes[i];
                bool ma = _lastResult.matrixApplied != null
                    && i < _lastResult.matrixApplied.Length && _lastResult.matrixApplied[i];
                sb.AppendLine($"{i},{n.roomType},{n.position.x:F2},{n.position.y:F2}," +
                              $"{n.position.z:F2},{n.radius:F2},{ma}");
            }

            // Per-edge
            sb.AppendLine();
            sb.AppendLine("EdgeIndex,StartX,StartZ,EndX,EndZ,Width,CurvatureAmp,IsCritical,IsViolation");
            for (int i = 0; i < _lastResult.edges.Count; i++)
            {
                var e = _lastResult.edges[i];
                bool isC = _lastResult.criticalEdgeIndices != null && _lastResult.criticalEdgeIndices.Contains(i);
                bool isV = _lastResult.violationEdges != null && _lastResult.violationEdges.Contains(i);
                sb.AppendLine($"{i},{e.startPos.x:F2},{e.startPos.z:F2},{e.endPos.x:F2}," +
                              $"{e.endPos.z:F2},{e.width:F2},{e.curvatureAmp:F3},{isC},{isV}");
            }

            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log($"[GraphPreview] Stats exported: {path}");
        }

        private void ExportStatsJSON()
        {
            if (_lastResult == null) return;
            string path = EditorUtility.SaveFilePanel(
                "Export Preview Stats (JSON)", "",
                $"preview_stats_seed{_inputs.seed}.json", "json");
            if (string.IsNullOrEmpty(path)) return;

            // 간단한 수동 JSON 구성 (JsonUtility는 Dictionary 지원 안 함)
            var sb = new System.Text.StringBuilder();
            var s = _lastResult.stats;
            sb.AppendLine("{");
            sb.AppendLine($"  \"seed\": {_inputs.seed},");
            sb.AppendLine($"  \"generationTime\": \"{_lastResult.generationTime:yyyy-MM-dd HH:mm:ss}\",");
            sb.AppendLine($"  \"dungeonBounds\": [{_inputs.dungeonBounds.x},{_inputs.dungeonBounds.y},{_inputs.dungeonBounds.z}],");
            sb.AppendLine("  \"graph\": {");
            sb.AppendLine($"    \"nodeCount\": {s.nodeCount},");
            sb.AppendLine($"    \"edgeCount\": {s.edgeCount},");
            sb.AppendLine($"    \"spawnCount\": {s.spawnCount},");
            sb.AppendLine($"    \"bossCount\": {s.bossCount},");
            sb.AppendLine($"    \"treasureCount\": {s.treasureCount},");
            sb.AppendLine($"    \"sinkholeCount\": {s.sinkholeCount},");
            sb.AppendLine($"    \"normalCount\": {s.normalCount},");
            sb.AppendLine($"    \"minEdgeWidth\": {s.minEdgeWidth:F2},");
            sb.AppendLine($"    \"maxEdgeSlope\": {s.maxEdgeSlope:F1}");
            sb.AppendLine("  },");
            sb.AppendLine("  \"violations\": {");
            sb.AppendLine($"    \"length\": {s.violationLength},");
            sb.AppendLine($"    \"slope\": {s.violationSlope},");
            sb.AppendLine($"    \"direction\": {s.violationDirection}");
            sb.AppendLine("  },");
            sb.AppendLine($"  \"matrixAppliedNodeCount\": {s.matrixAppliedNodeCount},");
            sb.AppendLine("  \"biomeAreaRatio\": {");
            bool first = true;
            foreach (var kv in s.biomeAreaRatio)
            {
                if (!first) sb.AppendLine(",");
                sb.Append($"    \"{BiomeSampler.GetBiomeName(kv.Key)}\": {kv.Value:F4}");
                first = false;
            }
            sb.AppendLine();
            sb.AppendLine("  }");
            sb.AppendLine("}");

            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log($"[GraphPreview] Stats exported: {path}");
        }

        // ──── Statistics ────
        private void DrawStatisticsSection()
        {
            EditorGUILayout.LabelField("▼ Statistics", EditorStyles.boldLabel);
            if (_lastResult == null)
            {
                EditorGUILayout.HelpBox("Generate Preview 클릭 후 통계 표시.", MessageType.None);
                return;
            }

            EditorGUI.indentLevel++;
            var s = _lastResult.stats;

            EditorGUILayout.LabelField("Graph", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"  Nodes: {s.nodeCount}  " +
                $"(Spawn:{s.spawnCount}, Boss:{s.bossCount}, Treasure:{s.treasureCount}, " +
                $"Sinkhole:{s.sinkholeCount}, Normal:{s.normalCount})");
            EditorGUILayout.LabelField($"  Edges: {s.edgeCount}");
            EditorGUILayout.LabelField($"  Min edge width: {s.minEdgeWidth:F2} m");
            EditorGUILayout.LabelField($"  Max edge slope: {s.maxEdgeSlope:F1}°");

            EditorGUILayout.Space();

            // Matrix
            if (_inputs.matrix != null && _inputs.matrixEnabled)
            {
                EditorGUILayout.LabelField("Matrix Impact", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(
                    $"  Applied: {s.matrixAppliedNodeCount}/{s.nodeCount} nodes");
            }

            // Constraint violations
            if (_inputs.constraint != null && _inputs.constraintEnabled)
            {
                int totalViol = s.violationLength + s.violationSlope + s.violationDirection;
                EditorGUILayout.LabelField("Constraint Violations", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField($"  Total: {totalViol}/{s.edgeCount} edges");
                if (s.violationLength > 0)
                    EditorGUILayout.LabelField($"    Length: {s.violationLength}");
                if (s.violationSlope > 0)
                    EditorGUILayout.LabelField($"    Slope: {s.violationSlope}");
                if (s.violationDirection > 0)
                    EditorGUILayout.LabelField($"    Direction: {s.violationDirection}");
            }

            // Biome distribution
            if (s.biomeAreaRatio != null && s.biomeAreaRatio.Count > 0
                && (_inputs.flags & FeatureFlags.ShowBiomeDist) != 0)
            {
                EditorGUILayout.LabelField("Biome Distribution", EditorStyles.miniBoldLabel);

                // [5.1 hotfix v3] Bar chart + cell 개수
                //   ascii bar로 비율 시각화. 전체 셀 수는 sum으로 계산
                float totalRatio = 0f;
                foreach (var kv in s.biomeAreaRatio) totalRatio += kv.Value;
                int totalCells = (_lastResult != null && _lastResult.biomeSamples != null)
                    ? _lastResult.biomeSamples.Count : 0;

                // 정렬 (큰 값 먼저)
                var sorted = new List<System.Collections.Generic.KeyValuePair<int, float>>(s.biomeAreaRatio);
                sorted.Sort((x, y) => y.Value.CompareTo(x.Value));

                foreach (var kv in sorted)
                {
                    string name = BiomeSampler.GetBiomeName(kv.Key);
                    float percent = kv.Value * 100f;
                    int cellCount = Mathf.RoundToInt(kv.Value * totalCells);
                    
                    // ASCII bar (20 char 기준)
                    int barLen = Mathf.RoundToInt(kv.Value * 20f);
                    string bar = new string('█', barLen) + new string('░', 20 - barLen);
                    
                    EditorGUILayout.LabelField(
                        $"  {name,-13} {bar} {percent,5:F1}%  ({cellCount} cells)");
                }

                // [5.2 hotfix v3] Balance Ratio 경고
                if (sorted.Count >= 2)
                {
                    float maxRatio = sorted[0].Value;
                    float minRatio = sorted[sorted.Count - 1].Value;
                    if (minRatio > 0.001f)
                    {
                        float balanceRatio = maxRatio / minRatio;
                        string ratioStr = $"Balance Ratio: {balanceRatio:F1}x (max/min)";
                        
                        if (balanceRatio >= 5f)
                        {
                            EditorGUILayout.HelpBox(
                                $"⚠ {ratioStr}\n\n" +
                                $"극단적 편향 감지: {BiomeSampler.GetBiomeName(sorted[0].Key)} " +
                                $"{sorted[0].Value * 100f:F1}% vs " +
                                $"{BiomeSampler.GetBiomeName(sorted[sorted.Count - 1].Key)} " +
                                $"{sorted[sorted.Count - 1].Value * 100f:F1}%\n" +
                                $"권장 해결:\n" +
                                $"  1. CaveBiomeSettings.globalBiomes 순서 조정 " +
                                $"(중앙 위치 biome이 우세).\n" +
                                $"  2. ⚖ Balance Biomes 토글로 균등화 결과 확인 (Preview 전용).\n" +
                                $"  3. Phase 5 shader 개선 대기 (근본 해결).",
                                MessageType.Warning);
                        }
                        else if (balanceRatio >= 2f)
                        {
                            EditorGUILayout.HelpBox(
                                $"ℹ {ratioStr} — 약간 편향",
                                MessageType.Info);
                        }
                        else
                        {
                            EditorGUILayout.LabelField($"  ✓ {ratioStr} (균등)",
                                EditorStyles.miniLabel);
                        }
                    }
                }
            }

            // Cardinal Bias
            if (_inputs.cardinalBias != null && _inputs.cardinalBiasEnabled
                && s.cardinalBiasMeanPush != null && s.cardinalBiasMeanPush.Count > 0)
            {
                EditorGUILayout.LabelField("Cardinal Bias (shader 미소비, 계산만)", EditorStyles.miniBoldLabel);
                foreach (var kv in s.cardinalBiasMeanPush)
                {
                    string name = (kv.Key < _inputs.biomes.Count && _inputs.biomes[kv.Key] != null)
                        ? BiomeSampler.GetBiomeName(_inputs.biomes[kv.Key].noiseType)
                        : $"Biome[{kv.Key}]";
                    EditorGUILayout.LabelField($"  {name} mean push: {kv.Value:F1} m");
                }
            }

            // Playability (제안 5)
            if ((_inputs.flags & FeatureFlags.ShowPlayability) != 0 && s.playability != null)
            {
                EditorGUILayout.LabelField("Playability (제안 5)", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField($"  #1 Width:   {(s.playability.passWidth ? "✓" : "✗")}");
                EditorGUILayout.LabelField($"  #2 Slope:   {(s.playability.passSlope ? "✓" : "✗")}");
                EditorGUILayout.LabelField($"  #4 Curve:   {(s.playability.passCurvatureClamp ? "✓" : "✗")}");
                EditorGUILayout.LabelField($"  #6 Dir:     {(s.playability.passDirection ? "✓" : "✗")}");
                if (s.playability.warnings.Count > 0)
                {
                    foreach (var w in s.playability.warnings)
                        EditorGUILayout.HelpBox(w, MessageType.Warning);
                }
            }

            // [Phase 4.5-D 제안 6] Seed Sweep 결과 표시
            if (_lastSweepResult != null && _lastSweepResult.statsPerSeed != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(
                    $"🎲 Seed Sweep ({_lastSweepResult.seeds.Length} seeds)",
                    EditorStyles.miniBoldLabel);

                var mean = _lastSweepResult.meanStats;
                var worst = _lastSweepResult.worstStats;

                EditorGUILayout.LabelField(
                    $"  Mean:  {mean.nodeCount} nodes, {mean.edgeCount} edges, " +
                    $"Min W: {mean.minEdgeWidth:F2}m, Max Slope: {mean.maxEdgeSlope:F1}°");
                EditorGUILayout.LabelField(
                    $"  Worst: Min W: {worst.minEdgeWidth:F2}m, " +
                    $"Max Slope: {worst.maxEdgeSlope:F1}°, " +
                    $"Violations: {worst.violationLength}");

                // 경고 — worst case가 심각하면
                if (worst.minEdgeWidth < 2.0f)
                {
                    EditorGUILayout.HelpBox(
                        $"⚠ Worst-case 통로 폭 {worst.minEdgeWidth:F1}m < 2.0m — " +
                        "일부 seed에서 플레이 불가 통로 발생 가능성. 파라미터 재조정 권장.",
                        MessageType.Warning);
                }
                if (worst.maxEdgeSlope > 50f)
                {
                    EditorGUILayout.HelpBox(
                        $"⚠ Worst-case 경사 {worst.maxEdgeSlope:F0}° > 50° — " +
                        "NavMesh bake 실패 가능성. maxSlope 증가 또는 rule 강화 권장.",
                        MessageType.Warning);
                }
            }

            EditorGUI.indentLevel--;
        }

        // ──── Extensions ────
        private void DrawExtensionsSection()
        {
            if (_extensions.Count == 0)
            {
                EditorGUILayout.LabelField("▼ Extensions (Phase 5+)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "SubBiome, FloorLayer, Ore 등 확장은 Phase 5 B.8 이후 추가. " +
                    "현재 IPreviewExtension 인터페이스 stub 상태.",
                    MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("▼ Extensions", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            foreach (var ext in _extensions)
            {
                if (!ext.IsActive(_inputs)) continue;
                EditorGUILayout.LabelField(ext.DisplayName, EditorStyles.miniBoldLabel);
                ext.DrawToggleGUI(_inputs);
                if (_lastResult != null)
                    ext.OnDrawStatsPanel(_lastResult, _inputs);
            }
            EditorGUI.indentLevel--;
        }

        // ──── Scene GUI ────
        private void OnSceneGUI(SceneView sv)
        {
            if (_lastResult == null) return;

            // [Phase 4.5-E hotfix v2] 렌더 순서 중요
            //   1. MC mesh 먼저 (반투명 solid, Scene 깊이 영향)
            //   2. Handles 기반 overlay (라벨/노드 정보) 나중에 — MC 위에 덮어쓰기
            //   → MC 내부에 있어도 라벨이 가려지지 않음
            _mcRenderer.UpdateIfNeeded(_lastResult, _inputs);
            _mcRenderer.Draw(_inputs);

            PreviewSceneRenderer.Render(_lastResult, _inputs, _baseline, _extensions);
        }

        // ──── Utilities ────
        
        /// <summary>
        /// [Phase 4.5-B hotfix] Scene에서 CaveBiomeSettings를 자동 탐색.
        /// 
        /// 기존 문제: CaveManager.Instance는 Awake에서만 세팅 → Editor 모드에서 null.
        ///          파일 폴더 위치와는 무관.
        /// 
        /// 수정 전략 (우선순위):
        ///   1. CaveManager.Instance.biomeSettings (Play 중 또는 최근 Play 있었으면)
        ///   2. Scene의 CaveManager 컴포넌트 직접 탐색 (Awake 안 타도 찾음)
        ///   3. Project 전체에서 CaveBiomeSettings asset 검색 (하나면 사용)
        ///   4. 실패 시 Manual slot 사용 안내
        /// </summary>
        private void AutoLoadFromScene()
        {
            CaveBiomeSettings bs = null;
            string sourceLog = "";

            // 전략 1: Singleton (Play 후 남아있는 경우)
            if (CaveManager.Instance != null && CaveManager.Instance.biomeSettings != null)
            {
                bs = CaveManager.Instance.biomeSettings;
                sourceLog = "CaveManager.Instance (Singleton)";
            }

            // 전략 2: FindObjectOfType — Editor 모드에서도 Scene 컴포넌트 탐색 가능
            //   DontDestroyOnLoad 등 포함하려면 FindObjectsByType 사용
            if (bs == null)
            {
                var managers = FindObjectsByType<CaveManager>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var mgr in managers)
                {
                    if (mgr != null && mgr.biomeSettings != null)
                    {
                        bs = mgr.biomeSettings;
                        sourceLog = $"Scene의 CaveManager ({mgr.name}) 직접 탐색";
                        break;
                    }
                }
            }

            // 전략 3: AssetDatabase 전체 검색 — CaveBiomeSettings asset 단일일 때 사용
            if (bs == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:CaveBiomeSettings");
                if (guids != null && guids.Length > 0)
                {
                    if (guids.Length == 1)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        bs = AssetDatabase.LoadAssetAtPath<CaveBiomeSettings>(path);
                        sourceLog = $"AssetDatabase 자동 탐색 ({path})";
                    }
                    else
                    {
                        // 여러 개 발견 시 사용자에게 선택 요청
                        string list = "";
                        foreach (var g in guids)
                            list += "\n  " + AssetDatabase.GUIDToAssetPath(g);
                        EditorUtility.DisplayDialog("Auto-load 애매함",
                            $"Project에 CaveBiomeSettings asset이 {guids.Length}개 있습니다. " +
                            $"수동으로 Manual slot에 할당해 주세요:{list}",
                            "OK");
                        return;
                    }
                }
            }

            // 결과 처리
            if (bs == null)
            {
                EditorUtility.DisplayDialog("Auto-load 실패",
                    "CaveBiomeSettings를 찾을 수 없습니다.\n\n" +
                    "확인 사항:\n" +
                    "1. Scene에 CaveManager 컴포넌트 + biomeSettings 필드가 할당되어 있는가?\n" +
                    "2. Project에 CaveBiomeSettings.asset 파일이 존재하는가?\n\n" +
                    "해결: 아래 Manual BiomeSettings slot에 직접 할당 후 [→ Load] 클릭.",
                    "OK");
                return;
            }

            LoadFromBiomeSettings(bs);
            Debug.Log($"[GraphPreview] Auto-loaded from: {sourceLog}");
        }

        /// <summary>
        /// CaveBiomeSettings 에서 _inputs 필드 채우기 공통 로직.
        /// Auto-load와 Manual 버튼에서 모두 호출.
        /// </summary>
        private void LoadFromBiomeSettings(CaveBiomeSettings bs)
        {
            if (bs == null)
            {
                Debug.LogWarning("[GraphPreview] LoadFromBiomeSettings: null 전달");
                return;
            }

            if (bs.globalBiomes != null)
            {
                _inputs.biomes = new List<CaveBiomeData>(bs.globalBiomes);
            }
            else
            {
                _inputs.biomes = new List<CaveBiomeData>();
            }

            _inputs.matrix = bs.roomGeometryMatrix;
            _inputs.cardinalBias = bs.cardinalBias;
            _inputs.macroBiomeScale = bs.macroBiomeScale;
            _inputs.seed = bs.seed;

            // EdgeConstraint: biomes[0]의 필드에서 가져옴
            if (_inputs.biomes.Count > 0 && _inputs.biomes[0] != null)
            {
                _inputs.constraint = _inputs.biomes[0].edgeConstraint;
            }

            // NodeGraphBuilder 기본값도 Scene의 builder에서 가져와 동기화
            //   Editor 모드에서도 Scene 컴포넌트 탐색 가능
            var builders = FindObjectsByType<CaveNodeGraphBuilder>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (builders != null && builders.Length > 0)
            {
                var b = builders[0];
                _inputs.dungeonBounds = b.dungeonBounds;
                _inputs.targetRoomCount = b.targetRoomCount;
                _inputs.mainPathWidth = b.mainPathWidth;
                _inputs.sidePathWidth = b.sidePathWidth;
                _inputs.minRoomRadius = b.minRoomRadius;
                _inputs.maxRoomRadius = b.maxRoomRadius;
                _inputs.bossRoomRadiusMultiplier = b.bossRoomRadiusMultiplier;
                _inputs.loopProbability = b.loopProbability;
                Debug.Log($"[GraphPreview] NodeGraphBuilder 파라미터도 동기화됨 ({b.name})");
            }

            _manualBiomeSettings = bs;  // slot에도 표시 (사용자 확인용)
            Debug.Log($"[GraphPreview] Loaded: {_inputs.biomes.Count} biomes, " +
                      $"matrix={(_inputs.matrix != null ? "✓" : "—")}, " +
                      $"constraint={(_inputs.constraint != null ? "✓" : "—")}, " +
                      $"cardinalBias={(_inputs.cardinalBias != null ? "✓" : "—")}");
        }

        // ──── 제거된 구 메서드 (하위 호환 제거) ────
        //   AutoLoadFromCaveManager() → AutoLoadFromScene() 으로 대체
    }
}
#endif
