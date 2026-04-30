#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Gate 5 Phase 4.5-B] PreviewSceneRenderer
    //
    // Scene view에 PreviewData overlay 렌더.
    // 렌더 순서 (배경 → 전경):
    //   1. Biome Heatmap (XZ grid, alpha 0.35)
    //   2. dungeonBounds wireframe
    //   3. CardinalBias 화살표 + maxInfluence ring
    //   4. Baseline graph (compare 모드)
    //   5. Current Edges (curvatureAmp 두께, violation 색)
    //   6. Current Nodes (roomType 색상)
    //   7. Matrix aura (흰 outline)
    //   8. Labels (roomType, edge slope 등)
    //   9. Extension overlays (Phase 5+)
    //
    // 성능 가드:
    //   - Heatmap 최대 5000 cells (BiomeSampler에서 auto-adjust)
    //   - Labels: roomType Normal 제외
    //   - AAPolyLine width cap: 8
    // ══════════════════════════════════════════════════════════════════════

    public static class PreviewSceneRenderer
    {
        public static void Render(PreviewData data, PreviewInputs inputs,
                                   PreviewData baseline = null,
                                   List<IPreviewExtension> extensions = null)
        {
            if (data == null || inputs == null) return;

            // [v4] Biome node list panel이 접근할 수 있도록 static 참조 저장
            _currentRenderData = data;

            // [Phase 4.5-E hotfix v2] MC mesh가 있을 때 라벨이 가려지지 않도록
            //   zTest Always 설정 → Handles가 depth 무시하고 항상 그려짐
            //   주의: 이는 Handles (gizmo 위젯)에만 영향, 씬의 다른 mesh엔 영향 없음
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            // [Phase 4.5-D] Highlight 상태 변화 감지 및 애니메이션 업데이트
            UpdateHighlightAnimations(data, inputs);

            // 1. Biome Heatmap
            if (HasFlag(inputs.flags, FeatureFlags.ShowBiomeHeatmap))
            {
                DrawBiomeHeatmap(data, inputs);
            }

            // 2. dungeonBounds wireframe
            DrawDungeonBounds(inputs);

            // 3. CardinalBias 화살표
            if (HasFlag(inputs.flags, FeatureFlags.ShowCardinalVec))
            {
                DrawCardinalBiasVectors(inputs);
            }

            // 4. Baseline graph (compare 모드)
            if (HasFlag(inputs.flags, FeatureFlags.CompareMode) && baseline != null)
            {
                DrawBaselineGraph(baseline);
            }

            // [Phase 4.5-D 제안 3] 4.5. Node Clustering Heatmap (edge 아래, node 아래)
            if (HasFlag(inputs.flags, FeatureFlags.ShowNodeClustering))
            {
                DrawNodeClusteringHeatmap(data, inputs);
            }

            // 5. Edges
            if (HasFlag(inputs.flags, FeatureFlags.ShowEdges))
            {
                DrawEdges(data, inputs);
            }

            // 6. Nodes
            if (HasFlag(inputs.flags, FeatureFlags.ShowNodes))
            {
                DrawNodes(data, inputs);
            }

            // [Phase 4.5-G Stage 3-F] Passage Info Panel
            if (inputs.showPassageInfoPanel
                && HasFlag(inputs.flags, FeatureFlags.ShowEdges))
            {
                DrawPassageInfoPanels(data, inputs);
            }

            // 9. Extensions
            if (extensions != null)
            {
                foreach (var ext in extensions)
                {
                    if (ext.IsActive(inputs))
                        ext.OnDrawSceneOverlay(data, inputs);
                }
            }

            // [Phase 4.5-D 제안 4] 10. Cross-section overlay (최상위 — 다른 요소 위에)
            if (HasFlag(inputs.flags, FeatureFlags.ShowCrossSection)
                && inputs.crossAxis != CrossSectionAxis.None)
            {
                DrawCrossSectionOverlay(data, inputs);
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 1. Biome Heatmap + [Q3] Biome Divide (contour tracing + 라벨 + blend weight)
        // ──────────────────────────────────────────────────────────────
        private static Material _heatmapFillMat;
        private static Material _heatmapOutlineMat;

        /// <summary>
        /// [Phase 4.5-F hotfix v7] Heatmap fill 전용 material 준비.
        /// Handles.DrawSolidRectangleWithOutline은 fill이 zTest를 무시하지 못해
        /// Runtime mesh에 가려지는 문제 발생. Custom material로 zTest Always 강제.
        /// </summary>
        private static void EnsureHeatmapMaterials()
        {
            if (_heatmapFillMat == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) return;
                _heatmapFillMat = new Material(shader);
                _heatmapFillMat.hideFlags = HideFlags.HideAndDontSave;
                _heatmapFillMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                _heatmapFillMat.SetInt("_ZWrite", 0);
                _heatmapFillMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _heatmapFillMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _heatmapFillMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
        }

        /// <summary>[hotfix v7] Custom GL quad 렌더 — zTest Always 보장.</summary>
        private static void DrawHeatmapCellGL(Vector3[] corners, Color fill)
        {
            EnsureHeatmapMaterials();
            if (_heatmapFillMat == null) return;

            _heatmapFillMat.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(fill);
            GL.Vertex(corners[0]);
            GL.Vertex(corners[1]);
            GL.Vertex(corners[2]);
            GL.Vertex(corners[3]);
            GL.End();
        }

        private static void DrawBiomeHeatmap(PreviewData data, PreviewInputs inputs)
        {
            if (data.biomeSamples == null || inputs.biomes == null) return;

            // [v4 fix] 실제 sample에 사용된 gridSize 우선 사용 (auto-adjust 반영)
            float gridSize = data.actualGridSize > 0.01f
                ? data.actualGridSize : inputs.heatmapGridSize;
            float baseOpacity = inputs.heatmapOpacity;
            bool showBlend = HasFlag(inputs.flags, FeatureFlags.ShowBlendWeight);

            // [Phase 4.5-F hotfix v3] Play 모드 대응 — Runtime mesh가 있으면
            //   heatmap이 mesh에 가려져 보이지 않는 문제 방지
            //   heatmap에는 zTest Always를 별도로 적용 (Handles.zTest와 독립적)
            var prevZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            // [Q3] Biome Divide 옵션
            bool showBoundary = inputs.showBiomeBoundary;
            bool showLabels = inputs.showBiomeLabels;
            bool showBlendValue = inputs.showBlendWeightValue;
            bool showTransitionZone = inputs.showBlendTransitionZone;  // [v5-1]

            // Biome 별로 sample 그룹핑 (dominant idx 기준) — 라벨 배치용
            Dictionary<int, List<BiomeSamplePoint>> biomeSampleGroups = null;
            if (showLabels)
            {
                biomeSampleGroups = new Dictionary<int, List<BiomeSamplePoint>>();
            }

            // [v4 fix] Dominant biome 맵 구성 — sample.gridIx/gridIz 사용 (부정확 계산 X)
            Dictionary<(int, int), int> dominantMap = null;
            if (showBoundary)
            {
                dominantMap = new Dictionary<(int, int), int>();
                foreach (var sample in data.biomeSamples)
                {
                    int dominant = sample.blendWeight < 0.5f ? sample.biomeIdxA : sample.biomeIdxB;
                    if (dominant >= 0 && dominant < inputs.biomes.Count
                        && inputs.biomes[dominant] != null)
                    {
                        dominantMap[(sample.gridIx, sample.gridIz)] = inputs.biomes[dominant].noiseType;
                    }
                }
            }

            foreach (var sample in data.biomeSamples)
            {
                if (sample.biomeIdxA < 0 || sample.biomeIdxA >= inputs.biomes.Count) continue;
                if (sample.biomeIdxB < 0 || sample.biomeIdxB >= inputs.biomes.Count) continue;
                var biomeA = inputs.biomes[sample.biomeIdxA];
                var biomeB = inputs.biomes[sample.biomeIdxB];
                if (biomeA == null || biomeB == null) continue;

                Color cA = BiomeSampler.GetBiomeColor(biomeA.noiseType);
                Color cB = BiomeSampler.GetBiomeColor(biomeB.noiseType);

                // [Phase 4.5-F hotfix] 색상 명확화 로직
                //   1. Saturation boost (색 채도 강화)
                //   2. 단일 biome 영역 (blendWeight 0 또는 1 근처)은 순수 biome 색
                //   3. 전이 영역 (0.3~0.7)만 실제 gradient blend
                Color blended;
                if (sample.blendWeight < 0.15f)
                {
                    // 거의 A biome (blendWeight < 0.15) — A 순수 색
                    blended = BoostSaturation(cA, 1.15f);
                }
                else if (sample.blendWeight > 0.85f)
                {
                    // 거의 B biome (blendWeight > 0.85) — B 순수 색
                    blended = BoostSaturation(cB, 1.15f);
                }
                else
                {
                    // 전이 영역 — remapped blendWeight로 gradient
                    //   [0.15, 0.85] 범위를 [0, 1]로 재매핑
                    float t = Mathf.InverseLerp(0.15f, 0.85f, sample.blendWeight);
                    blended = Color.Lerp(
                        BoostSaturation(cA, 1.15f),
                        BoostSaturation(cB, 1.15f),
                        t);
                }
                blended.a = baseOpacity;

                // Blend 경계 (blend 0.3~0.7)는 흐릿하게 — 경계 가시화
                if (showBlend)
                {
                    float edgeWeight = 1f - Mathf.Abs(sample.blendWeight - 0.5f) * 2f;
                    blended.a *= (1f - edgeWeight * 0.5f);
                }

                Vector3 cellMin = sample.worldPos - new Vector3(gridSize * 0.5f, 0f, gridSize * 0.5f);
                Vector3[] corners = new Vector3[]
                {
                    cellMin,
                    cellMin + new Vector3(gridSize, 0, 0),
                    cellMin + new Vector3(gridSize, 0, gridSize),
                    cellMin + new Vector3(0, 0, gridSize),
                };

                // [Q3 hotfix v3 + v7] 셀별 fill은 custom GL (zTest Always)
                //   Handles.DrawSolidRectangleWithOutline은 Play mode의 runtime mesh에 가려짐.
                //   Custom GL 렌더로 "mesh 무시하고 항상 앞에" 그림.
                DrawHeatmapCellGL(corners, blended);

                // [Q3 hotfix v3] 셀별 outline은 최소화 (투명)
                //   대신 biome 경계 edge만 별도로 그림 (아래 DrawBiomeBoundaryContour)
                //   outline은 Handles API 유지 — 이미 Handles.zTest=Always로 정상 동작
                //   Color outlineColor = new Color(blended.r, blended.g, blended.b, 0.08f);
                //   Handles.DrawSolidRectangleWithOutline(corners, blended, outlineColor);

                // [v5-1] Blend Transition Zone 표시 — 두 biome이 섞이는 영역 오버레이
                //   biome A ≠ B 이고, blend weight가 per-biome threshold 내에 있으면
                //   cell에 점선/사선 패턴 또는 반투명 오버레이로 강조
                if (showTransitionZone && biomeA.noiseType != biomeB.noiseType)
                {
                    // per-biome threshold — A와 B 중 큰 값 사용 (넓은 쪽 우선)
                    float extA = inputs.perBiomeBlendExtension.TryGetValue(biomeA.noiseType, out float ea)
                        ? ea : inputs.defaultBlendExtension;
                    float extB = inputs.perBiomeBlendExtension.TryGetValue(biomeB.noiseType, out float eb)
                        ? eb : inputs.defaultBlendExtension;
                    float maxExt = Mathf.Max(extA, extB);
                    // 전이영역 = [0.5 - maxExt, 0.5 + maxExt]
                    float lo = 0.5f - maxExt;
                    float hi = 0.5f + maxExt;

                    if (sample.blendWeight >= lo && sample.blendWeight <= hi)
                    {
                        // 전이 영역 강조 — 약간 밝게 + 테두리 강조
                        //   [hotfix v7] Custom GL로 runtime mesh에 가려지지 않도록
                        Color hiC = new Color(1f, 0.9f, 0.3f, 0.25f);
                        DrawHeatmapCellGL(corners, hiC);
                    }
                }

                // [Q3] Blend weight 수치 표시 (경계 셀만)
                if (showBlendValue && sample.blendWeight > 0.15f && sample.blendWeight < 0.85f)
                {
                    string weightStr = $"{sample.blendWeight:F2}";
                    // 거리 체크로 너무 많은 라벨 방지
                    Camera sceneCam = SceneView.lastActiveSceneView?.camera;
                    if (sceneCam != null)
                    {
                        float dist = Vector3.Distance(sceneCam.transform.position, sample.worldPos);
                        if (dist < gridSize * 20f)  // 카메라 근처만
                        {
                            Handles.Label(sample.worldPos, weightStr);
                        }
                    }
                }

                // Biome 그룹핑 (라벨 배치)
                if (biomeSampleGroups != null)
                {
                    int dominantIdx = sample.blendWeight < 0.5f ? sample.biomeIdxA : sample.biomeIdxB;
                    if (!biomeSampleGroups.ContainsKey(dominantIdx))
                        biomeSampleGroups[dominantIdx] = new List<BiomeSamplePoint>();
                    biomeSampleGroups[dominantIdx].Add(sample);
                }
            }

            // [Q3 hotfix v3, v4 fix] Contour tracing — biome 경계에만 선 그리기
            if (showBoundary && dominantMap != null)
            {
                DrawBiomeBoundaryContour(data.biomeSamples, dominantMap, gridSize,
                    inputs.biomeBoundaryColored);
            }

            // [Q3] Biome 이름 라벨 — 각 biome 영역 중심에 1개씩
            if (showLabels && biomeSampleGroups != null)
            {
                DrawBiomeNameLabels(biomeSampleGroups, inputs);
            }

            // [Phase 4.5-F hotfix v3] zTest 복원 — 다른 Handles 렌더에 영향 없도록
            Handles.zTest = prevZTest;
        }

        /// <summary>
        /// [Q3 hotfix v4, v5-1] Contour tracing — 각 sample.worldPos 기준 경계선.
        /// biomeColored=true면 경계 양쪽 biome 색상을 평균내서 표시.
        /// </summary>
        private static void DrawBiomeBoundaryContour(
            List<BiomeSamplePoint> samples,
            Dictionary<(int, int), int> dominantMap,
            float gridSize,
            bool biomeColored)
        {
            float half = gridSize * 0.5f;

            // sample lookup을 위해 index 정리
            Dictionary<(int, int), BiomeSamplePoint> sampleLookup = null;
            if (biomeColored)
            {
                sampleLookup = new Dictionary<(int, int), BiomeSamplePoint>();
                foreach (var s in samples)
                    sampleLookup[(s.gridIx, s.gridIz)] = s;
            }

            foreach (var sample in samples)
            {
                int ix = sample.gridIx;
                int iz = sample.gridIz;
                if (!dominantMap.TryGetValue((ix, iz), out int thisBiomeType)) continue;

                Vector3 cellCenter = sample.worldPos;

                // 헬퍼: 두 biome type을 받아 경계선 색 반환
                Color GetBoundaryColor(int tA, int tB)
                {
                    if (!biomeColored)
                        return new Color(1f, 1f, 1f, 0.95f);  // v4 기본: 흰색

                    Color cA = BiomeSampler.GetBiomeColor(tA);
                    Color cB = BiomeSampler.GetBiomeColor(tB);
                    // 두 색 평균 + 밝기 boost (경계선이므로 뚜렷하게)
                    Color avg = (cA + cB) * 0.5f;
                    // 밝기 약간 up + opacity 높게
                    avg.r = Mathf.Clamp01(avg.r * 1.3f);
                    avg.g = Mathf.Clamp01(avg.g * 1.3f);
                    avg.b = Mathf.Clamp01(avg.b * 1.3f);
                    avg.a = 0.95f;
                    return avg;
                }

                // +X 인접 비교
                if (dominantMap.TryGetValue((ix + 1, iz), out int rightBiomeType)
                    && rightBiomeType != thisBiomeType)
                {
                    Handles.color = GetBoundaryColor(thisBiomeType, rightBiomeType);
                    Vector3 s = cellCenter + new Vector3(half, 0.05f, -half);
                    Vector3 e = cellCenter + new Vector3(half, 0.05f, half);
                    Handles.DrawAAPolyLine(3f, s, e);
                }

                // +Z 인접 비교
                if (dominantMap.TryGetValue((ix, iz + 1), out int frontBiomeType)
                    && frontBiomeType != thisBiomeType)
                {
                    Handles.color = GetBoundaryColor(thisBiomeType, frontBiomeType);
                    Vector3 s = cellCenter + new Vector3(-half, 0.05f, half);
                    Vector3 e = cellCenter + new Vector3(half, 0.05f, half);
                    Handles.DrawAAPolyLine(3f, s, e);
                }

                // 월드 외곽 — 자기 biome 색상으로
                Color selfColor = biomeColored
                    ? GetBoundaryColor(thisBiomeType, thisBiomeType)
                    : new Color(1f, 1f, 1f, 0.95f);
                Handles.color = selfColor;

                if (!dominantMap.ContainsKey((ix + 1, iz)))
                {
                    Vector3 s = cellCenter + new Vector3(half, 0.05f, -half);
                    Vector3 e = cellCenter + new Vector3(half, 0.05f, half);
                    Handles.DrawAAPolyLine(3f, s, e);
                }
                if (!dominantMap.ContainsKey((ix, iz + 1)))
                {
                    Vector3 s = cellCenter + new Vector3(-half, 0.05f, half);
                    Vector3 e = cellCenter + new Vector3(half, 0.05f, half);
                    Handles.DrawAAPolyLine(3f, s, e);
                }
                if (!dominantMap.ContainsKey((ix - 1, iz)))
                {
                    Vector3 s = cellCenter + new Vector3(-half, 0.05f, -half);
                    Vector3 e = cellCenter + new Vector3(-half, 0.05f, half);
                    Handles.DrawAAPolyLine(3f, s, e);
                }
                if (!dominantMap.ContainsKey((ix, iz - 1)))
                {
                    Vector3 s = cellCenter + new Vector3(-half, 0.05f, -half);
                    Vector3 e = cellCenter + new Vector3(half, 0.05f, -half);
                    Handles.DrawAAPolyLine(3f, s, e);
                }
            }
        }

        /// <summary>
        /// [Q3] 각 biome 영역의 중심에 biome 이름 + 커다란 라벨 표시.
        /// [v4 hotfix] 클릭 가능한 버튼 + 펼침 시 포함 노드 리스트 표시.
        /// </summary>
        private static void DrawBiomeNameLabels(
            Dictionary<int, List<BiomeSamplePoint>> groups, PreviewInputs inputs)
        {
            Camera sceneCam = SceneView.lastActiveSceneView?.camera;
            if (sceneCam == null) return;

            bool clickable = inputs.showBiomeNodeListPanel;

            foreach (var kv in groups)
            {
                int biomeIdx = kv.Key;
                var samples = kv.Value;
                if (samples.Count < 3) continue;  // 너무 작은 영역은 라벨 스킵

                if (biomeIdx < 0 || biomeIdx >= inputs.biomes.Count) continue;
                if (inputs.biomes[biomeIdx] == null) continue;

                int noiseType = inputs.biomes[biomeIdx].noiseType;
                string biomeName = BiomeSampler.GetBiomeName(noiseType);
                Color biomeColor = BiomeSampler.GetBiomeColor(noiseType);

                // 그룹 중심 (평균 좌표)
                Vector3 center = Vector3.zero;
                foreach (var s in samples) center += s.worldPos;
                center /= samples.Count;

                // 라벨을 약간 위로 띄워 bubble 형태
                Vector3 labelPos = center + Vector3.up * 3f;

                if (clickable)
                {
                    // [v4] 클릭 가능 버튼 + 펼침 패널
                    DrawBiomeClickableLabel(labelPos, biomeName, samples.Count,
                        biomeColor, noiseType, inputs, sceneCam);
                }
                else
                {
                    // 단순 라벨 (기존 동작)
                    GUIStyle bigLabel = new GUIStyle();
                    bigLabel.fontSize = 14;
                    bigLabel.fontStyle = FontStyle.Bold;
                    bigLabel.alignment = TextAnchor.MiddleCenter;
                    bigLabel.normal.textColor = Color.black;
                    bigLabel.normal.background = MakeColorTexture(biomeColor);
                    bigLabel.padding = new RectOffset(6, 6, 2, 2);
                    Handles.Label(labelPos, $"  {biomeName}  [{samples.Count} cells]  ", bigLabel);
                }
            }
        }

        /// <summary>
        /// [v4 hotfix] Biome 라벨을 클릭 가능한 버튼으로 표시.
        /// 클릭 시 펼침 → 해당 biome 영역에 속한 노드 목록 패널.
        /// </summary>
        private static void DrawBiomeClickableLabel(
            Vector3 labelPos, string biomeName, int cellCount,
            Color biomeColor, int noiseType, PreviewInputs inputs, Camera sceneCam)
        {
            Vector3 screenPos = sceneCam.WorldToScreenPoint(labelPos);
            if (screenPos.z < 0f) return;
            screenPos.y = sceneCam.pixelHeight - screenPos.y;

            Handles.BeginGUI();
            try
            {
                bool expanded = inputs.expandedBiomeNoiseTypes.Contains(noiseType);
                string arrow = expanded ? "▼" : "▶";
                string btnLabel = $"{arrow} {biomeName}  [{cellCount} cells]";

                GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
                btnStyle.fontSize = 13;
                btnStyle.fontStyle = FontStyle.Bold;
                btnStyle.alignment = TextAnchor.MiddleCenter;
                btnStyle.padding = new RectOffset(8, 8, 3, 3);
                btnStyle.normal.textColor = Color.black;
                btnStyle.normal.background = MakeColorTexture(biomeColor);

                Vector2 size = btnStyle.CalcSize(new GUIContent(btnLabel));
                Rect btnRect = new Rect(
                    screenPos.x - size.x * 0.5f,
                    screenPos.y - size.y * 0.5f,
                    size.x + 8,
                    size.y + 4);

                if (GUI.Button(btnRect, btnLabel, btnStyle))
                {
                    if (expanded) inputs.expandedBiomeNoiseTypes.Remove(noiseType);
                    else inputs.expandedBiomeNoiseTypes.Add(noiseType);
                    SceneView.RepaintAll();
                }

                // 펼친 상태 → 포함 노드 리스트 패널
                if (expanded)
                {
                    DrawBiomeNodeListPanel(btnRect, noiseType, biomeColor, inputs);
                }
            }
            finally
            {
                Handles.EndGUI();
            }
        }

        /// <summary>
        /// [v4 hotfix] 펼친 biome 라벨 아래 — 이 biome에 속한 노드 리스트 표시.
        /// 각 노드의 roomType, position, radius 간략 정보.
        /// </summary>
        private static void DrawBiomeNodeListPanel(
            Rect anchorRect, int noiseType, Color biomeColor, PreviewInputs inputs)
        {
            // 이 biome에 속한 노드 찾기
            //   노드 위치 → BiomeSampler.SampleAt으로 dominant biome 계산
            //   노드 데이터는 _currentRenderData에 접근 필요 → static 변수 사용
            if (_currentRenderData == null || _currentRenderData.nodes == null) return;

            List<(int idx, NodeData node)> matchingNodes = new List<(int, NodeData)>();
            for (int i = 0; i < _currentRenderData.nodes.Count; i++)
            {
                var n = _currentRenderData.nodes[i];
                var sample = BiomeSampler.SampleAt(n.position, inputs.biomes.Count,
                    inputs.macroBiomeScale, null, null, inputs.balanceBiomes);
                int dominantIdx = sample.blendWeight < 0.5f ? sample.biomeIdxA : sample.biomeIdxB;
                if (dominantIdx < 0 || dominantIdx >= inputs.biomes.Count) continue;
                if (inputs.biomes[dominantIdx] == null) continue;
                if (inputs.biomes[dominantIdx].noiseType == noiseType)
                {
                    matchingNodes.Add((i, n));
                }
            }

            // 패널 높이: 헤더 + 각 노드 1줄씩 (최대 10개까지, 이상은 스크롤 생략)
            int maxDisplay = Mathf.Min(matchingNodes.Count, 10);
            float lineH = 14f;
            float panelH = (maxDisplay + 1) * lineH + 12f;
            float panelW = 220f;

            Rect panelRect = new Rect(
                anchorRect.x + anchorRect.width * 0.5f - panelW * 0.5f,
                anchorRect.y + anchorRect.height + 4,
                panelW, panelH);

            // 배경 (biome 색 반투명)
            Color bgC = new Color(biomeColor.r * 0.3f, biomeColor.g * 0.3f, biomeColor.b * 0.3f, 0.92f);
            GUIStyle bgStyle = new GUIStyle();
            bgStyle.normal.background = MakeBGTexture(bgC);
            GUI.Box(panelRect, GUIContent.none, bgStyle);

            // 헤더
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
            headerStyle.fontSize = 10;
            headerStyle.normal.textColor = new Color(1f, 1f, 1f, 0.95f);
            headerStyle.padding = new RectOffset(6, 6, 2, 2);
            float ly = panelRect.y + 4;
            GUI.Label(new Rect(panelRect.x, ly, panelW, lineH),
                $"◆ Nodes in this biome: {matchingNodes.Count}", headerStyle);
            ly += lineH;

            // 각 노드 라인
            GUIStyle lineStyle = new GUIStyle(EditorStyles.label);
            lineStyle.fontSize = 10;
            lineStyle.normal.textColor = new Color(0.95f, 0.95f, 0.95f);
            lineStyle.padding = new RectOffset(8, 4, 0, 0);

            for (int i = 0; i < maxDisplay; i++)
            {
                var (idx, n) = matchingNodes[i];
                string rt = GetRoomTypeShortName(n.roomType);
                string info = $"#{idx}  {rt,-8}  r={n.radius:F1}m  ({n.position.x:F0},{n.position.z:F0})";

                // RoomType별 색상 부여
                Color c = GetRoomTypeColor(n.roomType);
                GUIStyle colorStyle = new GUIStyle(lineStyle);
                colorStyle.normal.textColor = c;
                GUI.Label(new Rect(panelRect.x, ly, panelW, lineH), info, colorStyle);
                ly += lineH;
            }

            // 10개 초과 알림
            if (matchingNodes.Count > 10)
            {
                GUI.Label(new Rect(panelRect.x, ly, panelW, lineH),
                    $"  ... and {matchingNodes.Count - 10} more", lineStyle);
            }
        }

        // [v4] 현재 렌더 중인 PreviewData 참조 (Biome node list panel에서 접근용)
        private static PreviewData _currentRenderData;

        // [Phase 4.5-D] 노드별 highlight 애니메이션 상태 (biome 선택, Info Panel 클릭 등)
        //   key = 노드 인덱스, value = 0~1 진행도 (1 = 완전 선명)
        private static Dictionary<int, AnimationState> _nodeHighlightStates
            = new Dictionary<int, AnimationState>();

        // 이전 frame에 펼쳐있던 biome (상태 변화 감지 → 애니메이션 trigger)
        private static HashSet<int> _lastExpandedBiomeTypes = new HashSet<int>();
        private static HashSet<int> _lastExpandedNodeIndices = new HashSet<int>();

        // ═══════════════════════════════════════════════════════════════════
        // [η DebugAware Phase 3.5] Q2/Q3 — Hover easing + Predictive expansion
        //
        //   Q2: 기존 static GUI hover (즉시 흰색)을 easing으로 부드럽게.
        //       각 hover-able element에 AnimationState 등록 → EaseOutCubic 보간.
        //
        //   Q3: η Predictive panel 화살표 ▶ 클릭 시 1~6단계 산식 + 그래프 펼쳐짐.
        //       per-passage expansion state 추적.
        //
        //   Q2 클릭 → passage selectedSegmentPerPassage 갱신 (기존 메커니즘 활용).
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>η Predictive expansion 상태 — passage 인덱스 별 펼침 여부.</summary>
        private static HashSet<int> _expandedPredictivePassages = new HashSet<int>();

        /// <summary>UI element hover 상태 — key = 임의 ID, value = 0~1 hover 강도 (easing).</summary>
        private static Dictionary<string, AnimationState> _uiHoverStates
            = new Dictionary<string, AnimationState>();

        /// <summary>
        /// UI element hover 강도를 easing으로 계산.
        /// Q2: 마우스 진입/이탈 시 EaseOutCubic 0.18s로 자연스럽게.
        /// </summary>
        private static float GetHoverEased(string id, Rect r, Vector2 mousePos)
        {
            if (!_uiHoverStates.TryGetValue(id, out var state))
            {
                state = new AnimationState();
                _uiHoverStates[id] = state;
                AnimationCoordinator.Register(state);
            }
            bool isHovered = r.Contains(mousePos);
            // SetTarget 멱등 — 이미 같은 target이면 no-op
            state.SetTarget(isHovered ? 1.0f : 0.0f, 0.18f, Easing.EaseOutCubic, 0f);
            return state.currentValue;
        }

        /// <summary>η Predictive panel 펼침 toggle.</summary>
        private static bool IsPredictiveExpanded(int passageIdx)
            => _expandedPredictivePassages.Contains(passageIdx);

        private static void TogglePredictiveExpanded(int passageIdx)
        {
            if (_expandedPredictivePassages.Contains(passageIdx))
                _expandedPredictivePassages.Remove(passageIdx);
            else
                _expandedPredictivePassages.Add(passageIdx);
        }

        // ═══════════════════════════════════════════════════════════════════
        // [η Phase 3 #2] Auto-expand Predictive panel
        //   외부 (BDV)에서 sphere/라벨 클릭 시 호출.
        //   해당 passage의 Predictive panel을 강제로 펼침 상태로 만듦.
        //   기존 toggle과 다름 — 클릭 시 항상 펼침 (편의성).
        // ═══════════════════════════════════════════════════════════════════
        public static void AutoExpandPredictivePanel(int passageIdx)
        {
            _expandedPredictivePassages.Add(passageIdx);
        }

        private static string GetRoomTypeShortName(int rt)
        {
            switch (rt)
            {
                case 1: return "Spawn";
                case 2: return "Boss";
                case 3: return "Treasure";
                case 4: return "Sinkhole";
                default: return "Normal";
            }
        }

        private static Texture2D _cachedBGTex;
        private static Color _cachedBGColor;
        private static Texture2D MakeBGTexture(Color c)
        {
            if (_cachedBGTex != null && _cachedBGColor == c) return _cachedBGTex;
            _cachedBGTex = new Texture2D(1, 1);
            _cachedBGTex.hideFlags = HideFlags.HideAndDontSave;
            _cachedBGTex.SetPixel(0, 0, c);
            _cachedBGTex.Apply();
            _cachedBGColor = c;
            return _cachedBGTex;
        }

        /// <summary>
        /// [Phase 4.5-F hotfix] 노드가 속한 dominant biome의 색 반환.
        /// blendWeight < 0.5면 biomeIdxA, else biomeIdxB.
        /// Saturation boost 적용되어 뚜렷한 색.
        /// </summary>
        private static Color GetNodeBiomeColor(NodeData n, PreviewInputs inputs)
        {
            if (inputs.biomes == null || inputs.biomes.Count == 0) return Color.white;
            var sample = BiomeSampler.SampleAt(n.position, inputs.biomes.Count,
                inputs.macroBiomeScale, null, null, inputs.balanceBiomes);
            int dominantIdx = sample.blendWeight < 0.5f ? sample.biomeIdxA : sample.biomeIdxB;
            if (dominantIdx < 0 || dominantIdx >= inputs.biomes.Count) return Color.white;
            if (inputs.biomes[dominantIdx] == null) return Color.white;
            return BoostSaturation(BiomeSampler.GetBiomeColor(inputs.biomes[dominantIdx].noiseType), 1.15f);
        }

        private static Texture2D _cachedColorTex;

        /// <summary>
        /// [Phase 4.5-F hotfix] HSV로 변환 후 saturation 곱하여 색 채도 강화.
        /// 단조 색상(파랑 단색)이 아닌 biome 고유 색이 선명하게 보이도록.
        /// </summary>
        private static Color BoostSaturation(Color c, float saturationMult)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            s = Mathf.Clamp01(s * saturationMult);
            v = Mathf.Clamp01(v * 1.05f);  // 밝기도 5% 증가
            Color result = Color.HSVToRGB(h, s, v);
            result.a = c.a;
            return result;
        }
        private static Color _cachedColor;
        private static Texture2D MakeColorTexture(Color c)
        {
            // 매 프레임 새 Texture 생성은 비용 큼 → 마지막 color 캐싱
            if (_cachedColorTex != null && _cachedColor == c) return _cachedColorTex;
            _cachedColorTex = new Texture2D(1, 1);
            _cachedColorTex.hideFlags = HideFlags.HideAndDontSave;
            _cachedColorTex.SetPixel(0, 0, new Color(c.r, c.g, c.b, 0.85f));
            _cachedColorTex.Apply();
            _cachedColor = c;
            return _cachedColorTex;
        }

        // ──────────────────────────────────────────────────────────────
        // 2. dungeonBounds wireframe
        // ──────────────────────────────────────────────────────────────
        private static void DrawDungeonBounds(PreviewInputs inputs)
        {
            Handles.color = new Color(0f, 1f, 1f, 0.6f);
            Handles.DrawWireCube(Vector3.zero, inputs.dungeonBounds);
        }

        // ──────────────────────────────────────────────────────────────
        // 3. CardinalBias 화살표 + ring
        // ──────────────────────────────────────────────────────────────
        private static void DrawCardinalBiasVectors(PreviewInputs inputs)
        {
            if (!inputs.cardinalBiasEnabled) return;
            var bias = inputs.cardinalBias;
            if (bias == null || bias.biasVectors == null) return;

            Vector3 origin = Vector3.zero;

            // maxInfluence ring
            Handles.color = new Color(1f, 1f, 1f, 0.2f);
            Handles.DrawWireDisc(origin, Vector3.up, bias.maxInfluenceDistance);

            // 각 biome의 bias vector 화살표
            for (int i = 0; i < bias.biasVectors.Length && i < inputs.biomes.Count; i++)
            {
                Vector3 bv = bias.biasVectors[i];
                if (bv.sqrMagnitude < 0.001f) continue;
                if (inputs.biomes[i] == null) continue;

                Color c = BiomeSampler.GetBiomeColor(inputs.biomes[i].noiseType);
                c.a = 0.85f;
                Handles.color = c;

                // 화살표 길이 = bv 방향 × maxInfluence × strength
                float arrowLen = bias.maxInfluenceDistance * bias.biasStrength * bv.magnitude;
                // clamp to dungeonBounds 안
                arrowLen = Mathf.Min(arrowLen, inputs.dungeonBounds.x * 0.5f);
                Vector3 endPos = origin + bv.normalized * arrowLen;

                Handles.DrawAAPolyLine(4f, new Vector3[] { origin, endPos });
                Handles.ConeHandleCap(0, endPos, Quaternion.LookRotation(bv.normalized),
                    2.5f, EventType.Repaint);

                Handles.Label(endPos + Vector3.up * 3f,
                    $"{BiomeSampler.GetBiomeName(inputs.biomes[i].noiseType)}\n{bv}");
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 4. Baseline graph (Before/After Compare)
        //   [Phase 4.5-C 강화] 점선 느낌 + 푸른색으로 차별화
        // ──────────────────────────────────────────────────────────────
        private static void DrawBaselineGraph(PreviewData baseline)
        {
            // Baseline = SO 적용 전 상태 — 회청색, 반투명
            Color edgeC = new Color(0.35f, 0.55f, 0.75f, 0.55f);
            Color nodeC = new Color(0.45f, 0.65f, 0.85f, 0.4f);

            // Baseline edges (얇은 선, 차별적 색)
            Handles.color = edgeC;
            foreach (var e in baseline.edges)
            {
                Handles.DrawAAPolyLine(2.5f, new Vector3[] { e.startPos, e.endPos });
            }
            // Baseline nodes (작은 sphere)
            Handles.color = nodeC;
            foreach (var n in baseline.nodes)
            {
                Handles.SphereHandleCap(0, n.position, Quaternion.identity,
                    Mathf.Max(n.radius * 0.22f, 0.4f), EventType.Repaint);
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 5. Edges
        // ──────────────────────────────────────────────────────────────
        private static void DrawEdges(PreviewData data, PreviewInputs inputs)
        {
            bool showViolations = HasFlag(inputs.flags, FeatureFlags.ShowViolations);
            bool showCritical = HasFlag(inputs.flags, FeatureFlags.ShowCriticalPath);
            bool showDirection = HasFlag(inputs.flags, FeatureFlags.ShowEdgeDirection);
            bool realScale = inputs.showRealScale;

            // [Phase 4.5-F hotfix v4] Show Edges 렌더 모드 분기
            //   방안 C (MeshOutline) — 실제 runtime DC mesh에서 edge 추출
            //   방안 A/B (Line / CapsuleWire) — graph edges 기반
            if (inputs.edgeDisplayMode == EdgeDisplayMode.MeshOutline)
            {
                DrawRuntimeMeshOutline(data, inputs);
                // MeshOutline 모드에서도 critical path / violation 은 graph 기반 선으로 중첩
                DrawGraphEdgesOverlay(data, inputs, onlySpecial: true);
                return;
            }

            // 기존 로직 (Line / CapsuleWire)
            DrawGraphEdgesOverlay(data, inputs, onlySpecial: false);
        }

        /// <summary>
        /// [Phase 4.5-F hotfix v4+v6] 방안 C — Runtime DC mesh outline 렌더.
        /// EdgeMeshOutlineExtractor cache 사용 → 성능 경량.
        /// graphNodes/edges 전달하여 파편/고립 mesh 제거 필터 적용.
        /// </summary>
        private static void DrawRuntimeMeshOutline(PreviewData data, PreviewInputs inputs)
        {
            EdgeMeshOutlineExtractor.GetSegments(
                inputs.edgeMeshSimplifyTolerance,
                out var starts, out var ends,
                inputs.edgeMeshFilterByGraph,
                data?.nodes, data?.edges);

            if (starts == null || starts.Count == 0) return;

            // 단일 색 + 얇은 선 — mesh outline은 자잘한 선 많음
            Color meshOutlineColor = new Color(0.6f, 0.85f, 1.0f, 0.85f);
            Handles.color = meshOutlineColor;

            // 성능: DrawAAPolyLine 대신 DrawLine (단일 호출 × N) — mass 렌더
            for (int i = 0; i < starts.Count; i++)
            {
                Handles.DrawLine(starts[i], ends[i]);
            }
        }

        /// <summary>
        /// 기존 graph edge 렌더 로직. onlySpecial=true면 critical / violation 만.
        /// </summary>
        private static void DrawGraphEdgesOverlay(PreviewData data, PreviewInputs inputs, bool onlySpecial)
        {
            bool showViolations = HasFlag(inputs.flags, FeatureFlags.ShowViolations);
            bool showCritical = HasFlag(inputs.flags, FeatureFlags.ShowCriticalPath);
            bool showDirection = HasFlag(inputs.flags, FeatureFlags.ShowEdgeDirection);
            bool realScale = inputs.showRealScale;
            bool capsuleWire = inputs.edgeDisplayMode == EdgeDisplayMode.CapsuleWire;

            for (int i = 0; i < data.edges.Count; i++)
            {
                var e = data.edges[i];
                bool isViolation = data.violationEdges != null && data.violationEdges.Contains(i);
                // [Phase 4.5-C] Critical path 실제 인덱스 사용
                bool isCritical = data.criticalEdgeIndices != null
                    && data.criticalEdgeIndices.Contains(i);

                // onlySpecial 모드면 특수 edge만 표시
                if (onlySpecial && !((isViolation && showViolations) || (isCritical && showCritical)))
                    continue;

                Color c;
                float thickness = Mathf.Clamp(e.curvatureAmp * 2f + 1.5f, 1.5f, 8f);

                if (isViolation && showViolations)
                {
                    c = new Color(0.9f, 0.2f, 0.2f, 0.95f);
                    thickness = Mathf.Max(thickness, 4f);  // 두껍게 하이라이트
                }
                else if (isCritical && showCritical)
                {
                    c = new Color(1f, 0.85f, 0f, 0.9f);  // 노랑 (critical path)
                    thickness = Mathf.Max(thickness, 5f);
                }
                else if (e.curvatureAmp > 0.01f)
                {
                    c = Color.Lerp(Color.gray, new Color(0.8f, 0.5f, 0.95f),
                                    Mathf.Clamp01(e.curvatureAmp / 3f));
                }
                else
                {
                    c = new Color(0.7f, 0.7f, 0.7f, 0.9f);
                }

                Handles.color = c;
                Handles.DrawAAPolyLine(thickness, new Vector3[] { e.startPos, e.endPos });

                // [Q2 hotfix v3 + hotfix v4] realScale 또는 CapsuleWire 모드 — 파이프 형태
                if (realScale || capsuleWire)
                {
                    Vector3 dir = (e.endPos - e.startPos).normalized;
                    if (dir.sqrMagnitude > 0.001f)
                    {
                        Color wireC = c;
                        wireC.a = 0.35f;
                        Handles.color = wireC;

                        // Cylinder body: 일정 간격 disk (edge를 5~8개 segment로 분할)
                        float edgeLen = Vector3.Distance(e.startPos, e.endPos);
                        int segCount = Mathf.Clamp(Mathf.CeilToInt(edgeLen / 4f), 5, 15);
                        for (int s = 0; s <= segCount; s++)
                        {
                            float t = (float)s / segCount;
                            Vector3 pt = Vector3.Lerp(e.startPos, e.endPos, t);
                            Handles.DrawWireDisc(pt, dir, e.width);
                        }

                        // 축 방향 라인 (4개): cylinder surface 시각화
                        Vector3 perp1 = Vector3.Cross(dir, Vector3.up);
                        if (perp1.sqrMagnitude < 0.001f)
                            perp1 = Vector3.Cross(dir, Vector3.right);
                        perp1 = perp1.normalized;
                        Vector3 perp2 = Vector3.Cross(dir, perp1).normalized;

                        Color lineC = c;
                        lineC.a = 0.25f;
                        Handles.color = lineC;
                        Vector3[] axisOffsets = {
                            perp1 * e.width,
                            -perp1 * e.width,
                            perp2 * e.width,
                            -perp2 * e.width,
                        };
                        foreach (var offset in axisOffsets)
                        {
                            Handles.DrawAAPolyLine(1.5f,
                                e.startPos + offset, e.endPos + offset);
                        }
                    }
                }

                // [제안 2] Edge Direction 화살표
                if (showDirection)
                {
                    Vector3 mid = (e.startPos + e.endPos) * 0.5f;
                    Vector3 dirE = (e.endPos - e.startPos).normalized;
                    if (dirE.sqrMagnitude > 0.001f)
                    {
                        Handles.color = isViolation ? new Color(1f, 0.3f, 0.3f) : new Color(0.3f, 0.9f, 0.3f);
                        Handles.ConeHandleCap(0, mid, Quaternion.LookRotation(dirE),
                            1.2f, EventType.Repaint);
                    }
                }
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 6. Nodes
        // ──────────────────────────────────────────────────────────────
        private static void DrawNodes(PreviewData data, PreviewInputs inputs)
        {
            bool showAura = HasFlag(inputs.flags, FeatureFlags.ShowMatrixAura);
            bool showLabels = HasFlag(inputs.flags, FeatureFlags.ShowRoomLabels);
            bool showInfo = inputs.showNodeInfoPanel;
            bool realScale = inputs.showRealScale;

            // [Q4] Node 정보 패널용: 카메라 접근
            Camera sceneCam = SceneView.lastActiveSceneView?.camera;

            for (int i = 0; i < data.nodes.Count; i++)
            {
                var n = data.nodes[i];
                Color roomColor = GetRoomTypeColor(n.roomType);

                // [Phase 4.5-D] Highlight 값 (0~1) — biome 라벨 / Info panel 클릭 시 1
                float highlight = GetNodeHighlight(i);

                // [Phase 4.5-F hotfix] 노드 색상을 biome 색으로 lerp — 해당 노드가 속한 biome이 펼쳐졌을 때
                //   highlight 값은 AnimationState가 이미 easing (EaseOutBack) 적용 중
                //   → highlight 0.5일 때 roomColor와 biomeColor의 중간, 1일 때 완전 biome 색
                Color c = roomColor;
                if (highlight > 0.01f && inputs.biomes != null && inputs.biomes.Count > 0)
                {
                    Color biomeC = GetNodeBiomeColor(n, inputs);
                    // highlight × 0.75 — 완전 biome 색으로 가면 roomType 식별 어려움 → 75%까지만
                    c = Color.Lerp(roomColor, biomeC, Mathf.Min(highlight, 1f) * 0.75f);
                }

                // [Q2] 실제 사이즈 vs 축소 gizmo
                if (realScale)
                {
                    // 실제 radius를 반영한 3-축 wireframe disk (방의 실제 크기 감각)
                    // highlight 있으면 radius 살짝 커짐 (1.0 → 1.1)
                    float scaleBoost = 1f + highlight * 0.1f;
                    float effR = n.radius * scaleBoost;

                    c.a = Mathf.Lerp(0.45f, 0.8f, highlight);
                    Handles.color = c;
                    Handles.DrawWireDisc(n.position, Vector3.up, effR);
                    Handles.DrawWireDisc(n.position, Vector3.right, effR);
                    Handles.DrawWireDisc(n.position, Vector3.forward, effR);
                    // 솔리드 작은 sphere도 함께 (노드 위치 식별용)
                    c.a = Mathf.Lerp(0.7f, 1.0f, highlight);
                    Handles.color = c;
                    Handles.SphereHandleCap(0, n.position, Quaternion.identity,
                        0.8f + highlight * 0.3f, EventType.Repaint);
                }
                else
                {
                    // 축소 gizmo (기존) — highlight 시 1.15배 커짐
                    float scaleBoost = 1f + highlight * 0.15f;
                    c.a = Mathf.Lerp(0.5f, 0.95f, highlight);
                    Handles.color = c;
                    float drawRadius = Mathf.Max(n.radius * 0.3f * scaleBoost, 0.5f);
                    Handles.SphereHandleCap(0, n.position, Quaternion.identity,
                        drawRadius, EventType.Repaint);

                    // highlight > 0.01 시 외곽선 추가 (선명화)
                    if (highlight > 0.01f)
                    {
                        Color outlineC = new Color(1f, 1f, 1f, highlight * 0.9f);
                        Handles.color = outlineC;
                        float outlineR = drawRadius * 1.15f;
                        Handles.DrawWireDisc(n.position, Vector3.up, outlineR);
                        Handles.DrawWireDisc(n.position, Vector3.right, outlineR);
                        Handles.DrawWireDisc(n.position, Vector3.forward, outlineR);
                    }
                }

                // Matrix aura (7. Matrix aura)
                if (showAura && data.matrixApplied != null && i < data.matrixApplied.Length
                    && data.matrixApplied[i])
                {
                    Handles.color = new Color(1f, 1f, 1f, 0.8f);
                    float auraR = realScale ? n.radius * 1.15f : n.radius * 0.35f;
                    Handles.DrawWireDisc(n.position, Vector3.up, auraR);
                    Handles.DrawWireDisc(n.position, Vector3.right, auraR);
                    Handles.DrawWireDisc(n.position, Vector3.forward, auraR);
                }

                // [Q1 hotfix v3] 노드 정보 패널 — Normal 노드도 선택적 포함
                bool includeThisNode = n.roomType != 0 || inputs.showNormalNodeInfo;
                if (showInfo && includeThisNode)
                {
                    DrawNodeInfoLabel(data, inputs, i, n, sceneCam);
                }
                // 기본 라벨만 (정보 패널 OFF or 제외 노드)
                else if (showLabels || n.roomType != 0)
                {
                    string label = GetRoomTypeName(n.roomType);
                    Handles.Label(n.position + Vector3.up * (GetLabelOffset(n.radius, realScale)), label);
                }
            }
        }

        /// <summary>
        /// [Q4] Node 정보 패널 — 노드 위에 접힘/펼침 toggle 라벨.
        /// Scene View GUI로 클릭 가능한 버튼 렌더.
        /// </summary>
        private static void DrawNodeInfoLabel(
            PreviewData data, PreviewInputs inputs,
            int nodeIdx, NodeData n, Camera sceneCam)
        {
            if (sceneCam == null) return;

            // 월드 → 스크린 좌표 변환 (Handles.BeginGUI 안에서)
            Vector3 labelWorld = n.position + Vector3.up * GetLabelOffset(n.radius, inputs.showRealScale);
            Vector3 screenPos = sceneCam.WorldToScreenPoint(labelWorld);

            // 카메라 뒤 = skip
            if (screenPos.z < 0f) return;

            // Scene view에서 Screen Y 뒤집힘 보정
            screenPos.y = sceneCam.pixelHeight - screenPos.y;

            Handles.BeginGUI();
            try
            {
                bool isNormal = (n.roomType == 0);
                string typeName = isNormal ? "Normal" : GetRoomTypeName(n.roomType);
                bool expanded = inputs.expandedNodeIndices.Contains(nodeIdx);
                string arrow = expanded ? "▼" : "▶";
                string btnLabel = $"{arrow} {typeName} #{nodeIdx}";

                // [Q1 hotfix v3] Normal 노드는 작은 폰트 + 반투명 (과밀 방지)
                GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
                btnStyle.fontSize = isNormal ? 9 : 11;
                btnStyle.alignment = TextAnchor.MiddleLeft;
                btnStyle.padding = new RectOffset(
                    isNormal ? 3 : 6, isNormal ? 3 : 6, isNormal ? 1 : 2, isNormal ? 1 : 2);

                Color textC = GetRoomTypeColor(n.roomType);
                if (isNormal)
                {
                    textC.a = 0.5f;  // Normal은 반투명
                    textC = new Color(0.75f, 0.75f, 0.75f, 0.5f);  // 연한 회색
                }
                btnStyle.normal.textColor = textC;
                btnStyle.fontStyle = isNormal ? FontStyle.Normal : FontStyle.Bold;

                Vector2 size = btnStyle.CalcSize(new GUIContent(btnLabel));
                Rect btnRect = new Rect(screenPos.x - size.x * 0.5f, screenPos.y,
                    size.x + (isNormal ? 2 : 4), isNormal ? 14 : 20);

                if (GUI.Button(btnRect, btnLabel, btnStyle))
                {
                    // 토글
                    if (expanded) inputs.expandedNodeIndices.Remove(nodeIdx);
                    else inputs.expandedNodeIndices.Add(nodeIdx);
                    SceneView.RepaintAll();
                }

                // 펼친 상태 → 상세 정보 패널
                if (expanded)
                {
                    DrawExpandedNodeInfo(data, inputs, nodeIdx, n, btnRect);
                }
            }
            finally
            {
                Handles.EndGUI();
            }
        }

        /// <summary>
        /// [Q4] 펼친 상태 정보 패널 — 노드 상세 정보 표시.
        /// Handles.BeginGUI 블록 안에서 호출됨.
        /// </summary>
        private static void DrawExpandedNodeInfo(
            PreviewData data, PreviewInputs inputs,
            int nodeIdx, NodeData n, Rect btnRect)
        {
            // 정보 패널 위치 (버튼 바로 아래)
            Rect panelRect = new Rect(btnRect.x, btnRect.y + btnRect.height + 2,
                                       240f, 132f);

            // 배경
            GUI.Box(panelRect, GUIContent.none);
            GUIStyle bg = new GUIStyle(GUI.skin.box);

            // 정보 내용
            GUIStyle labelStyle = new GUIStyle(EditorStyles.label);
            labelStyle.fontSize = 10;
            labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            labelStyle.alignment = TextAnchor.MiddleLeft;
            labelStyle.padding = new RectOffset(8, 4, 1, 1);

            GUIStyle keyStyle = new GUIStyle(labelStyle);
            keyStyle.normal.textColor = new Color(0.6f, 0.75f, 1f);
            keyStyle.fontStyle = FontStyle.Bold;

            float ly = panelRect.y + 4;
            float lineH = 14f;

            // Line 1: Position
            GUI.Label(new Rect(panelRect.x, ly, 60, lineH), "Position:", keyStyle);
            GUI.Label(new Rect(panelRect.x + 60, ly, 180, lineH),
                $"({n.position.x:F1}, {n.position.y:F1}, {n.position.z:F1})", labelStyle);
            ly += lineH;

            // Line 2: Radius
            GUI.Label(new Rect(panelRect.x, ly, 60, lineH), "Radius:", keyStyle);
            GUI.Label(new Rect(panelRect.x + 60, ly, 180, lineH), $"{n.radius:F2} m", labelStyle);
            ly += lineH;

            // Line 3: Room Type
            GUI.Label(new Rect(panelRect.x, ly, 60, lineH), "Type:", keyStyle);
            GUI.Label(new Rect(panelRect.x + 60, ly, 180, lineH),
                $"{GetRoomTypeName(n.roomType)} ({n.roomType})", labelStyle);
            ly += lineH;

            // Line 4: Sculpt Flags
            GUI.Label(new Rect(panelRect.x, ly, 60, lineH), "Sculpt:", keyStyle);
            GUI.Label(new Rect(panelRect.x + 60, ly, 180, lineH),
                $"Narrow:{n.sculptFlags.x:F2} High:{n.sculptFlags.y:F2} Open:{n.sculptFlags.z:F2}",
                labelStyle);
            ly += lineH;

            // Line 5: Matrix Applied
            bool matrixApplied = data.matrixApplied != null
                && nodeIdx < data.matrixApplied.Length
                && data.matrixApplied[nodeIdx];
            GUI.Label(new Rect(panelRect.x, ly, 60, lineH), "Matrix:", keyStyle);
            GUI.Label(new Rect(panelRect.x + 60, ly, 180, lineH),
                matrixApplied ? "✓ Applied" : "— Not applied", labelStyle);
            ly += lineH;

            // Line 6: Biome (이 위치의 우세 biome)
            int biomeIdx = SampleDominantBiomeAt(n.position, inputs);
            string biomeName = (biomeIdx >= 0 && biomeIdx < inputs.biomes.Count
                                && inputs.biomes[biomeIdx] != null)
                ? BiomeSampler.GetBiomeName(inputs.biomes[biomeIdx].noiseType)
                : "?";
            GUI.Label(new Rect(panelRect.x, ly, 60, lineH), "Biome:", keyStyle);
            GUI.Label(new Rect(panelRect.x + 60, ly, 180, lineH), biomeName, labelStyle);
            ly += lineH;

            // Line 7: 연결 edge 수 카운트
            int connEdges = 0;
            for (int e = 0; e < data.edges.Count; e++)
            {
                var edge = data.edges[e];
                if ((edge.startPos - n.position).sqrMagnitude < 0.01f ||
                    (edge.endPos - n.position).sqrMagnitude < 0.01f)
                {
                    connEdges++;
                }
            }
            GUI.Label(new Rect(panelRect.x, ly, 60, lineH), "Edges:", keyStyle);
            GUI.Label(new Rect(panelRect.x + 60, ly, 180, lineH),
                $"{connEdges} connections", labelStyle);
        }

        /// <summary>라벨의 Y 오프셋 계산.</summary>
        private static float GetLabelOffset(float radius, bool realScale)
        {
            return realScale ? (radius + 2f) : (radius * 0.3f + 2f);
        }

        /// <summary>특정 위치의 우세 biome 인덱스 샘플 (정보 패널용).</summary>
        private static int SampleDominantBiomeAt(Vector3 worldPos, PreviewInputs inputs)
        {
            if (inputs.biomes == null || inputs.biomes.Count == 0) return -1;
            var sample = BiomeSampler.SampleAt(worldPos, inputs.biomes.Count,
                inputs.macroBiomeScale, null, null);
            return sample.blendWeight < 0.5f ? sample.biomeIdxA : sample.biomeIdxB;
        }

        // ══════════════════════════════════════════════════════════════════
        // [Phase 4.5-G Stage 3-F] Passage Info Panel
        // ══════════════════════════════════════════════════════════════════

        private static void DrawPassageInfoPanels(PreviewData data, PreviewInputs inputs)
        {
            // PassageProfile 데이터 로드 (CaveNodeGraphBuilder에서 계산됨)
            var builder = CaveNodeGraphBuilder.Instance;
            if (builder == null || builder.lastPassageProfiles == null
                || builder.lastPassageProfiles.Count == 0)
                return;

            Camera sceneCam = SceneView.lastActiveSceneView?.camera;
            if (sceneCam == null) return;

            int showMode = inputs.passageShowMode;
            // 0=Click only, 1=Critical auto, 2=All overlay
            for (int i = 0; i < builder.lastPassageProfiles.Count; i++)
            {
                var profile = builder.lastPassageProfiles[i];

                // Filter
                if (inputs.passageRiskFilter && profile.risk.level != PassageMetric.RiskLevel.Critical)
                    continue;

                // 표시 결정
                bool showLabel = false;
                if (showMode == 1 && profile.risk.level == PassageMetric.RiskLevel.Critical)
                    showLabel = true;  // Critical auto-show
                else if (showMode == 2)
                    showLabel = true;  // All overlay
                else if (showMode == 0)
                    showLabel = (inputs.selectedPassageIdx == i);  // Click only — selected만

                if (!showLabel && !inputs.expandedPassageIndices.Contains(i)) continue;

                // Label 위치 — passage midpoint (waypoint 있으면 first waypoint 기준)
                Vector3 labelPos = PassageMetric.SampleAlongEdge(profile.segments.Length > 0
                    ? new EdgeData {
                        startPos = profile.startPos,
                        endPos = profile.endPos,
                        flags = (uint)profile.waypointCount
                    } : default,
                    0.5f);
                // 위 simplistic 처리는 직선 가정 — segments[5] (중앙 sample) 사용이 더 정확
                if (profile.segments != null && profile.segments.Length >= 6)
                    labelPos = profile.segments[profile.segments.Length / 2].worldPos;

                DrawPassageInfoLabel(data, inputs, i, profile, labelPos, sceneCam);

                // [신규] Selected segment Scene highlight
                //   - 펼쳐진 패널 + segment 선택된 경우만
                //   - 선택된 segment 위치에 sphere + adjacent segments connecting line
                if (inputs.expandedPassageIndices.Contains(i)
                    && inputs.selectedSegmentPerPassage != null
                    && inputs.selectedSegmentPerPassage.TryGetValue(i, out int selSeg)
                    && selSeg >= 0 && profile.segments != null
                    && selSeg < profile.segments.Length)
                {
                    DrawSegmentHighlight(profile, selSeg);
                }
            }
        }

        /// <summary>
        /// [신규] 선택된 segment를 Scene에 강조 표시.
        ///   - 큰 sphere (yellow halo)
        ///   - 이전/다음 segment와 연결선 (highlight)
        ///   - segment 인덱스 라벨
        /// </summary>
        private static void DrawSegmentHighlight(
            PassageMetric.PassageProfile profile, int segIdx)
        {
            if (profile.segments == null || segIdx < 0 || segIdx >= profile.segments.Length) return;

            var seg = profile.segments[segIdx];
            float radius = Mathf.Max(seg.localWidth * 0.5f, 0.5f);

            // (1) 노란 halo sphere
            Color prevColor = Handles.color;
            Handles.color = new Color(1f, 0.85f, 0.2f, 0.85f);
            Handles.DrawWireDisc(seg.worldPos, Vector3.up, radius * 1.4f);
            Handles.DrawWireDisc(seg.worldPos, Vector3.right, radius * 1.4f);
            Handles.DrawWireDisc(seg.worldPos, Vector3.forward, radius * 1.4f);

            // (2) 내부 채워진 disc
            Handles.color = new Color(1f, 0.85f, 0.2f, 0.25f);
            Handles.DrawSolidDisc(seg.worldPos, Vector3.up, radius);

            // (3) 이전/다음 segment와 연결선 (강조)
            Handles.color = new Color(1f, 0.85f, 0.2f, 0.7f);
            if (segIdx > 0)
                Handles.DrawAAPolyLine(4f, profile.segments[segIdx - 1].worldPos, seg.worldPos);
            if (segIdx < profile.segments.Length - 1)
                Handles.DrawAAPolyLine(4f, seg.worldPos, profile.segments[segIdx + 1].worldPos);

            // (4) 라벨 — "seg N (t=0.45)"
            Handles.color = Color.white;
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel);
            labelStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);
            labelStyle.fontSize = 11;
            Handles.Label(seg.worldPos + Vector3.up * (radius + 0.5f),
                          $"seg {segIdx} (t={seg.t:F2})", labelStyle);

            Handles.color = prevColor;
        }

        private static void DrawPassageInfoLabel(
            PreviewData data, PreviewInputs inputs,
            int passageIdx, PassageMetric.PassageProfile profile,
            Vector3 worldPos, Camera sceneCam)
        {
            Handles.BeginGUI();

            // World → Screen
            Vector3 screen = sceneCam.WorldToScreenPoint(worldPos);
            if (screen.z < 0) { Handles.EndGUI(); return; }

            // Y flip (Unity Scene GUI)
            float guiY = sceneCam.pixelHeight - screen.y;
            float btnX = screen.x + 8f;
            float btnY = guiY - 11f;

            bool isExpanded = inputs.expandedPassageIndices.Contains(passageIdx);

            // 토글 버튼 — risk level 색상
            var color = profile.risk.GetLevelColor();
            string label = $" {GetPassageTypeShort(profile.inferredType)} #{passageIdx} · " +
                           $"{profile.risk.GetLevelStr()} {profile.risk.total:F2}";
            string fullLabel = (isExpanded ? "▼" : "▶") + label;

            Rect btnRect = new Rect(btnX, btnY, 220f, 22f);

            GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
            btnStyle.fontSize = 10;
            btnStyle.alignment = TextAnchor.MiddleLeft;
            btnStyle.normal.textColor = color;
            btnStyle.fontStyle = FontStyle.Bold;
            btnStyle.padding = new RectOffset(6, 6, 2, 2);

            if (GUI.Button(btnRect, fullLabel, btnStyle))
            {
                if (isExpanded) inputs.expandedPassageIndices.Remove(passageIdx);
                else inputs.expandedPassageIndices.Add(passageIdx);
            }

            if (isExpanded)
            {
                DrawExpandedPassageInfo(data, inputs, passageIdx, profile, btnRect);
            }

            Handles.EndGUI();
        }

        /// <summary>
        /// 펼쳐진 Passage 상세 패널 — 유동 정보 시각화 (10-segment).
        /// </summary>
        private static void DrawExpandedPassageInfo(
            PreviewData data, PreviewInputs inputs,
            int passageIdx, PassageMetric.PassageProfile profile, Rect btnRect)
        {
            Rect panelRect = new Rect(btnRect.x, btnRect.y + btnRect.height + 2,
                                       340f, 360f);
            GUI.Box(panelRect, GUIContent.none);

            GUIStyle keyStyle = new GUIStyle(EditorStyles.label);
            keyStyle.fontSize = 10;
            keyStyle.normal.textColor = new Color(0.6f, 0.75f, 1f);
            keyStyle.fontStyle = FontStyle.Bold;
            keyStyle.padding = new RectOffset(8, 4, 1, 1);
            keyStyle.alignment = TextAnchor.MiddleLeft;

            GUIStyle valStyle = new GUIStyle(EditorStyles.label);
            valStyle.fontSize = 10;
            valStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            valStyle.padding = new RectOffset(2, 4, 1, 1);
            valStyle.alignment = TextAnchor.MiddleLeft;

            float ly = panelRect.y + 4;
            float lineH = 14f;

            // Line 1: Passage Type + Tags
            GUI.Label(new Rect(panelRect.x, ly, 60, lineH), "Type:", keyStyle);
            GUI.Label(new Rect(panelRect.x + 60, ly, 240, lineH),
                $"{profile.inferredType} [{PassageMetric.TagsToString(profile.tags)}]", valStyle);
            ly += lineH;

            // Line 2: Length + Waypoints
            GUI.Label(new Rect(panelRect.x, ly, 60, lineH), "Length:", keyStyle);
            GUI.Label(new Rect(panelRect.x + 60, ly, 240, lineH),
                $"{profile.length:F1} m | Waypoints: {profile.waypointCount}", valStyle);
            ly += lineH;

            // Line 3: Width range — ★ η Fix #3: predictive 기반으로 (이전 localWidth)
            GUI.Label(new Rect(panelRect.x, ly, 60, lineH), "Width:", keyStyle);

            // ═══════════════════════════════════════════════════════════════
            // [η DebugAware Fix #3] Width 표시 변환 — localWidth → predEffectiveWidth
            //
            //   기존: profile.minLocalWidth → profile.maxLocalWidth (declared width)
            //   문제: localWidth는 edge.width × (1 + boost) — noise/smin 영향 미반영
            //
            //   Phase 2 fix: predEffectiveWidth 사용 (이미 계산됨, η/ε에서)
            //     = localWidth - smin penetration - noise amp×2 - curve impact
            //   Phase 28 (장기): GPU mesh 실제 측정값
            // ═══════════════════════════════════════════════════════════════
            string widthStr;
            if (profile.segments != null && profile.segments.Length > 0
                && profile.segments[0].predEffectiveWidth > 0.001f)
            {
                // ★ Predictive 활성 (η/ε) — effective width 표시
                float predMin = float.MaxValue, predMax = float.MinValue;
                for (int si = 0; si < profile.segments.Length; si++)
                {
                    float ew = profile.segments[si].predEffectiveWidth;
                    if (ew < predMin) predMin = ew;
                    if (ew > predMax) predMax = ew;
                }
                widthStr = $"{predMin:F2} → {predMax:F2} m (predictive)";
            }
            else
            {
                // Legacy / γ / δ — localWidth 표시 (declared)
                widthStr = $"{profile.minLocalWidth:F2} → {profile.maxLocalWidth:F2} m (declared)";
            }
            GUI.Label(new Rect(panelRect.x + 60, ly, 280, lineH), widthStr, valStyle);
            ly += lineH;

            // Line 4: Y slope
            GUI.Label(new Rect(panelRect.x, ly, 60, lineH), "Y slope:", keyStyle);
            GUI.Label(new Rect(panelRect.x + 60, ly, 240, lineH),
                $"{profile.ySlope:F2} ({(profile.ySlope > 0.5f ? "Steep" : profile.ySlope > 0.2f ? "Sloped" : "Horizontal")})",
                valStyle);
            ly += lineH;

            // Line 5: Biome 양 끝 + 자연 pair 분류 (D4)
            GUI.Label(new Rect(panelRect.x, ly, 60, lineH), "Biomes:", keyStyle);
            string pairDesc = PassageMetric.DescribeNaturalPair(
                profile.dominantBiomeA, profile.dominantBiomeB);
            GUI.Label(new Rect(panelRect.x + 60, ly, 240, lineH),
                $"#{profile.dominantBiomeA} → #{profile.dominantBiomeB} [{pairDesc}]", valStyle);
            ly += lineH + 3f;

            // ═══════════════════════════════════════════════════════════════
            // [η DebugAware Phase 6] visitedBiomes 표시 (결함 #3 해결)
            //   "Karst(15%) → Columnar(40%) → Colonnade(45%)"처럼 통과 순서 + 점유율
            //   default null (η OFF 시) → 표시 skip → byte-identical
            // ═══════════════════════════════════════════════════════════════
            if (profile.visitedBiomes != null
                && profile.visitedBiomes.Length > 1)
            {
                GUI.Label(new Rect(panelRect.x, ly, 60, lineH), "Visited:", keyStyle);
                var sb = new System.Text.StringBuilder();
                for (int v = 0; v < profile.visitedBiomes.Length; v++)
                {
                    if (v > 0) sb.Append(" → ");
                    sb.Append($"#{profile.visitedBiomes[v]}({profile.biomeOccupancy[v] * 100f:F0}%)");
                }
                GUIStyle visitedStyle = new GUIStyle(valStyle);
                visitedStyle.normal.textColor = new Color(0.55f, 0.85f, 1.0f);  // η 청
                visitedStyle.fontSize = 9;
                GUI.Label(new Rect(panelRect.x + 60, ly, 240, lineH),
                    sb.ToString(), visitedStyle);
                ly += lineH + 2;
                // trueDominant이 양 끝과 다르면 강조 표시
                if (profile.trueDominantBiome >= 0
                    && profile.trueDominantBiome != profile.dominantBiomeA
                    && profile.trueDominantBiome != profile.dominantBiomeB)
                {
                    GUIStyle trueDomStyle = new GUIStyle(valStyle);
                    trueDomStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
                    trueDomStyle.fontSize = 9;
                    GUI.Label(new Rect(panelRect.x + 60, ly, 240, lineH),
                        $"★ trueDominant=#{profile.trueDominantBiome} (passage 다수 점유)",
                        trueDomStyle);
                    ly += lineH;
                }
                ly += 2;
            }

            // ── Width profile bar (10 segments) ──
            GUI.Label(new Rect(panelRect.x, ly, 80, lineH), "Width:", keyStyle);
            DrawWidthProfileBar(profile, panelRect.x + 60, ly + 2, 220, 8);
            ly += lineH + 4;

            // ── Biome profile bar (10 segments) ──
            GUI.Label(new Rect(panelRect.x, ly, 80, lineH), "Biome:", keyStyle);
            DrawBiomeProfileBar(profile, panelRect.x + 60, ly + 2, 220, 8);
            ly += lineH + 6;

            // ── Risk breakdown ──
            //   selected segment 있으면 그 값 사용, 없으면 전체 평균
            int selSegEarly = -1;
            if (inputs.selectedSegmentPerPassage != null
                && inputs.selectedSegmentPerPassage.TryGetValue(passageIdx, out int sEarly))
                selSegEarly = sEarly;

            float dispLen, dispNar, dispBlend, dispMix, dispCurv, dispWp;
            string riskLabelSuffix;
            if (selSegEarly >= 0 && profile.segments != null && selSegEarly < profile.segments.Length)
            {
                var seg = profile.segments[selSegEarly];
                dispLen = seg.segLengthRisk;
                dispNar = seg.segNarrowRisk;
                dispBlend = seg.segBlendRisk;
                dispMix = seg.segMixRisk;
                dispCurv = seg.segCurvatureRisk;
                dispWp = seg.segWaypointRisk;
                riskLabelSuffix = $" [seg {selSegEarly}]";
            }
            else
            {
                dispLen = profile.risk.lengthRisk;
                dispNar = profile.risk.narrowRisk;
                dispBlend = profile.risk.blendRisk;
                dispMix = profile.risk.biomeMixRisk;
                dispCurv = profile.risk.curvatureRisk;
                dispWp = profile.risk.waypointRisk;
                riskLabelSuffix = " [avg]";
            }

            DrawRiskBreakdownLine(panelRect.x, ly, "L (length)" + riskLabelSuffix, dispLen, 0.20f);
            ly += lineH;
            DrawRiskBreakdownLine(panelRect.x, ly, "W (narrow)", dispNar, 0.25f);
            ly += lineH;
            DrawRiskBreakdownLine(panelRect.x, ly, "B (transit)", dispBlend, 0.20f);
            ly += lineH;
            DrawRiskBreakdownLine(panelRect.x, ly, "M (mix)", dispMix, 0.15f);
            ly += lineH;
            DrawRiskBreakdownLine(panelRect.x, ly, "C (curve)", dispCurv, 0.10f);
            ly += lineH;
            DrawRiskBreakdownLine(panelRect.x, ly, "Y (waypoint)", dispWp, 0.10f);
            ly += lineH + 4;

            // ── Active Effects (per-segment 선택 가능, 그래프 형태) ──
            //   "All" + 0~9 segments — 텍스트가 아닌 mini bar graph
            //   각 segment 위에 LWBMCY 6 metric 누적 bar 표시 (Risk 시각화)
            int selectedSeg = -1;
            if (inputs.selectedSegmentPerPassage != null
                && inputs.selectedSegmentPerPassage.TryGetValue(passageIdx, out int s))
                selectedSeg = s;

            GUIStyle efxKeyStyle = new GUIStyle(keyStyle);
            efxKeyStyle.normal.textColor = new Color(1f, 0.78f, 0.4f);
            string efxLabel = (selectedSeg < 0)
                ? "Effects [All]:"
                : $"Effects [seg {selectedSeg}]:";
            GUI.Label(new Rect(panelRect.x, ly, 110, lineH), efxLabel, efxKeyStyle);
            ly += lineH + 1;

            // Segment selector — Mini stacked bar graph
            //   각 segment 칸 위에 6 risk metric 누적 표시 (위→아래: L W B M C Y)
            DrawSegmentSelectorGraph(profile, panelRect.x + 4, ly, panelRect.width - 8, 36,
                                      passageIdx, selectedSeg, inputs);
            ly += 36 + 4;

            // 선택된 segment의 effects (또는 합집합)
            uint displayEffects;
            if (selectedSeg < 0)
            {
                displayEffects = 0u;
                if (profile.segments != null)
                {
                    for (int i = 0; i < profile.segments.Length; i++)
                        displayEffects |= profile.segments[i].activeEffects;
                }
            }
            else
            {
                displayEffects = (profile.segments != null && selectedSeg < profile.segments.Length)
                    ? profile.segments[selectedSeg].activeEffects : 0u;
            }

            GUIStyle efxValStyle = new GUIStyle(valStyle);
            efxValStyle.fontSize = 9;
            efxValStyle.wordWrap = true;
            string efxStr = PassageMetric.EffectsToString(displayEffects);

            // ★ η Predictive 데이터 검증 — 표시 가능 여부
            bool hasPredictiveData = false;
            PassageMetric.PassageSegment selSeg = default;
            if (selectedSeg >= 0 && profile.segments != null && selectedSeg < profile.segments.Length)
            {
                selSeg = profile.segments[selectedSeg];
                // ═══════════════════════════════════════════════════════════
                // [η DebugAware Fix] Panel info 정확화 — A/B/effective 명시
                //
                //   기존 'biome=#0' 표기는 bIdxA만 (GPU 평가 biome ≠ bIdxA when bw>0.5).
                //   사용자 보고: gallery#8 panel '#0(Karst)'인데 mesh는 Columnar 형태.
                //
                //   해결: A=#X(name)/B=#Y(name), eff=#Z(name) 모두 표시
                //         effective는 GPU가 실제 평가하는 biome — mesh 형태와 일치.
                // ═══════════════════════════════════════════════════════════
                int effIdx = selSeg.EffectiveBiomeIdx;
                string biomeAName = (selSeg.biomeIdxA >= 0 && inputs.biomes != null
                                     && selSeg.biomeIdxA < inputs.biomes.Count
                                     && inputs.biomes[selSeg.biomeIdxA] != null)
                    ? inputs.biomes[selSeg.biomeIdxA].biomeName : "?";
                string biomeBName = (selSeg.biomeIdxB >= 0 && inputs.biomes != null
                                     && selSeg.biomeIdxB < inputs.biomes.Count
                                     && inputs.biomes[selSeg.biomeIdxB] != null)
                    ? inputs.biomes[selSeg.biomeIdxB].biomeName : "?";
                string effName = (effIdx >= 0 && inputs.biomes != null
                                  && effIdx < inputs.biomes.Count
                                  && inputs.biomes[effIdx] != null)
                    ? inputs.biomes[effIdx].biomeName : "?";
                efxStr = $"@t={selSeg.t:F2}, A=#{selSeg.biomeIdxA}({biomeAName})/B=#{selSeg.biomeIdxB}({biomeBName}) " +
                         $"→ eff=#{effIdx}({effName}), BW={selSeg.blendWeight:F2}, BP={selSeg.blendCentrality:F2}\n{efxStr}";
                hasPredictiveData = (selSeg.predEffectiveWidth > 0.001f
                                     || selSeg.singleSourceState != 0
                                     || selSeg.predNearbyEdgeCount > 0
                                     // 음수 effective width도 의미 있음 (★ 매우 위험 영역)
                                     || selSeg.predEffectiveWidth < -0.001f);
            }

            // [기존 효과 텍스트 — 3 lines fixed]
            GUI.Label(new Rect(panelRect.x, ly, 305, lineH * 3), efxStr, efxValStyle);
            ly += lineH * 3 + 2;

            // ════════════════════════════════════════════════════════════════
            // [Phase 4.5-G η DebugAware Phase 3 Q3] Predictive Panel — 펼침 가능
            //
            //   ▶ 접힘 (default): 핵심 한 줄 요약 (eff width / SS state / smin / noise)
            //   ▼ 펼침: 6단계 산식 + 안전 마진 침해율 진행 그래프
            //
            //   Q2 통합: 헤더가 hover 시 Easing.EaseOutCubic으로 부드럽게 색 변화.
            //           클릭 시 expansion toggle.
            //
            //   D2: hasPredictiveData=false 시 전체 skip → 비용 ZERO
            // ════════════════════════════════════════════════════════════════
            if (hasPredictiveData)
            {
                bool expanded = IsPredictiveExpanded(passageIdx);
                Vector2 mousePos = Event.current.mousePosition;

                // ── (1) Toggle Header ────────────────────────────────
                Rect headerRect = new Rect(panelRect.x, ly, 305, lineH);
                string hoverID = $"η_predictive_header_{passageIdx}";
                float hoverT = GetHoverEased(hoverID, headerRect, mousePos);

                // hover 시 흰색으로 부드럽게 밝아짐 (Q2 정확 구현)
                Color baseColor = new Color(0.55f, 0.85f, 1.0f, 0.95f);    // η 청색
                Color hoverColor = new Color(1f, 1f, 1f, 1f);              // 흰색
                Color headerTextColor = Color.Lerp(baseColor, hoverColor, hoverT);

                // 배경도 hover 시 살짝 강조
                Color bgBase = new Color(0.15f, 0.20f, 0.30f, 0.5f);
                Color bgHover = new Color(0.25f, 0.35f, 0.45f, 0.7f);
                EditorGUI.DrawRect(headerRect, Color.Lerp(bgBase, bgHover, hoverT));

                GUIStyle headerStyle = new GUIStyle(valStyle);
                headerStyle.fontSize = 10;
                headerStyle.fontStyle = FontStyle.Bold;
                headerStyle.normal.textColor = headerTextColor;
                headerStyle.padding = new RectOffset(6, 6, 0, 0);

                // Single-Source state 라벨
                string ssLbl = selSeg.singleSourceState switch
                {
                    0 => $"A=#{selSeg.singleSourceTypeA}",
                    1 => $"B=#{selSeg.singleSourceTypeB}",
                    2 => "blank",
                    _ => "dual"
                };
                string arrow = expanded ? "▼" : "▶";
                string warnPx = selSeg.predIsBelowSafeMargin ? "★ " : "";
                string headerText =
                    $"{arrow} η Predictive | {warnPx}eff={selSeg.predEffectiveWidth:F2}m | " +
                    $"SS:{ssLbl} | smin={selSeg.predSminPenetration:F2} | " +
                    $"noise={selSeg.predNoiseAmp:F2} | N={selSeg.predNearbyEdgeCount}";
                GUI.Label(headerRect, headerText, headerStyle);

                // hover 시 cursor 변경
                EditorGUIUtility.AddCursorRect(headerRect, MouseCursor.Link);

                // 클릭 처리 — toggle expand
                if (Event.current.type == EventType.MouseDown
                    && headerRect.Contains(mousePos)
                    && Event.current.button == 0)
                {
                    TogglePredictiveExpanded(passageIdx);
                    Event.current.Use();
                    // 다음 frame Repaint 위해 SceneView 마킹
                    SceneView.RepaintAll();
                }
                ly += lineH + 2;

                // ── (2) 펼친 경우 6단계 산식 표시 ─────────────────────
                if (expanded)
                {
                    DrawPredictiveStages(panelRect.x, ref ly, panelRect.width,
                                          selSeg, lineH, valStyle);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // [Phase 4.5-G η DebugAware Phase 3 Q3] Predictive 6단계 산식 + 그래프
        //
        //   각 단계의 산식과 결과값 + 안전 마진 침해율 그래프 표시.
        //
        //   1단계 [LOOKUP]    biome → BiomeAmpInfo (4원칙 분류)
        //   2단계 [AMP]       baseAmp × ampScale × SafetyFactor
        //   3단계 [SMIN]      인접 N edges fan → smin penetration
        //   4단계 [CURVE]     curvatureAmp × 0.5 (curve 영향)
        //   5단계 [WIDTH]     localWidth - smin - widthImpact - curveImpact
        //   6단계 [VERDICT]   effectiveWidth vs MIN_SAFE_WIDTH (2.0m)
        //
        //   각 단계 옆에 "안전 마진 침해율" mini bar (적색 채움)
        // ════════════════════════════════════════════════════════════════════
        private static void DrawPredictiveStages(
            float x, ref float ly, float w,
            PassageMetric.PassageSegment seg, float lineH, GUIStyle baseValStyle)
        {
            const float MIN_SAFE_WIDTH = 2.0f;
            const float STAGE_PAD = 4f;
            const float BAR_W = 80f;          // 우측 mini bar 폭
            const float BAR_H = 10f;
            const float TEXT_W_RATIO = 0.62f;  // 좌측 텍스트 영역 비율

            float textW = w * TEXT_W_RATIO;
            float barX = x + textW + STAGE_PAD;

            // 단계 표시용 style
            GUIStyle stageLabel = new GUIStyle(baseValStyle);
            stageLabel.fontSize = 9;
            stageLabel.padding = new RectOffset(8, 4, 0, 0);
            stageLabel.wordWrap = true;

            GUIStyle stageHeader = new GUIStyle(stageLabel);
            stageHeader.fontStyle = FontStyle.Bold;
            stageHeader.normal.textColor = new Color(1f, 0.8f, 0.4f);

            // ── 사전 계산 ────────────────────────────────────────────
            // BiomeAmpInfo (lookup table)
            var ampInfo = PassageMetric.LookupBiomeAmpInfo(seg.biomeIdx);
            string classStr = ampInfo.principleClass switch
            {
                PassageMetric.NoiseClass.PureAnalytical => "PureAnalytical",
                PassageMetric.NoiseClass.Hybrid => "Hybrid",
                _ => "PureNoise"
            };

            float widthImpact = ampInfo.symmetric ? seg.predNoiseAmp * 2f : seg.predNoiseAmp;
            float curveImpact = 0f;  // edge.curvatureAmp는 segment에 없으므로 reverse 추정
            float reconstructedLW = seg.predEffectiveWidth + seg.predSminPenetration
                                  + widthImpact + curveImpact;

            // ── Stage 1: LOOKUP ──────────────────────────────────────
            string s1 = $"① LOOKUP — biome=#{seg.biomeIdx} → {classStr}\n" +
                        $"  baseAmp={ampInfo.baseAmp:F2}m, accuracy={ampInfo.accuracyGrade:F2}\n" +
                        $"  symmetric={(ampInfo.symmetric ? "± 양방향" : "단방향")}, " +
                        $"violation={(ampInfo.principleViolation ? "★ YES" : "OK")}";
            GUI.Label(new Rect(x, ly, textW, lineH * 3), s1, stageLabel);
            DrawProgressBar(barX, ly + 4, BAR_W, BAR_H,
                            ampInfo.principleViolation ? 0.8f : 0.2f,
                            "violation");
            ly += lineH * 3 + 2;

            // ── Stage 2: AMP (ampScale + SafetyFactor) ───────────────
            string s2 = $"② AMP — pow(1-BP, FadePower) × Safety\n" +
                        $"  ampScale = {seg.singleSourceAmpScale:F2}\n" +
                        $"  predNoiseAmp = baseAmp × ampScale × {ampInfo.SafetyFactor:F2} = {seg.predNoiseAmp:F2}m";
            GUI.Label(new Rect(x, ly, textW, lineH * 3), s2, stageLabel);
            // 침해율: noise 영향 / MIN_SAFE
            float ampImpactPct = Mathf.Clamp01((widthImpact) / MIN_SAFE_WIDTH);
            DrawProgressBar(barX, ly + 4, BAR_W, BAR_H, ampImpactPct, "noise impact");
            ly += lineH * 3 + 2;

            // ── Stage 3: SMIN (인접 edges fan) ───────────────────────
            string s3 = $"③ SMIN — 인접 edges fan 누적\n" +
                        $"  N={seg.predNearbyEdgeCount} edges (≤5m)\n" +
                        $"  smin = (N-1) × 2.5/4 = {seg.predSminPenetration:F2}m";
            GUI.Label(new Rect(x, ly, textW, lineH * 3), s3, stageLabel);
            float sminPct = Mathf.Clamp01(seg.predSminPenetration / MIN_SAFE_WIDTH);
            DrawProgressBar(barX, ly + 4, BAR_W, BAR_H, sminPct, "smin impact");
            ly += lineH * 3 + 2;

            // ── Stage 4: CURVE (curvature × 0.5) ─────────────────────
            string s4 = $"④ CURVE — curvAmp × 0.5\n" +
                        $"  curveImpact ≈ {curveImpact:F2}m\n" +
                        $"  (edge 단위 — segment별 동일)";
            GUI.Label(new Rect(x, ly, textW, lineH * 3), s4, stageLabel);
            float curvePct = Mathf.Clamp01(curveImpact / MIN_SAFE_WIDTH);
            DrawProgressBar(barX, ly + 4, BAR_W, BAR_H, curvePct, "curve impact");
            ly += lineH * 3 + 2;

            // ── Stage 5: WIDTH (실효 폭) ─────────────────────────────
            string widthSign = seg.predEffectiveWidth >= 0 ? "+" : "★";
            string s5 = $"⑤ WIDTH — 실효 통과 폭\n" +
                        $"  lw={reconstructedLW:F2}m - smin{seg.predSminPenetration:F2} " +
                        $"- noise{widthImpact:F2} - curve{curveImpact:F2}\n" +
                        $"  = {widthSign} {seg.predEffectiveWidth:F2}m";
            GUIStyle s5Style = new GUIStyle(stageLabel);
            if (seg.predEffectiveWidth < 0)
                s5Style.normal.textColor = new Color(1f, 0.4f, 0.4f);  // 음수 = 적색
            GUI.Label(new Rect(x, ly, textW, lineH * 3), s5, s5Style);
            // 누적 침해율: (lw - effW) / lw
            float totalImpactPct = (reconstructedLW > 0.01f)
                ? Mathf.Clamp01(1f - seg.predEffectiveWidth / reconstructedLW) : 1f;
            DrawProgressBar(barX, ly + 4, BAR_W, BAR_H, totalImpactPct, "total impact");
            ly += lineH * 3 + 2;

            // ── Stage 6: VERDICT ─────────────────────────────────────
            string verdict = seg.predIsBelowSafeMargin
                ? $"★ BELOW SAFE MARGIN ({MIN_SAFE_WIDTH:F1}m)"
                : "✓ Safe";
            Color verdictColor = seg.predIsBelowSafeMargin
                ? new Color(1f, 0.3f, 0.3f) : new Color(0.4f, 1f, 0.5f);
            string s6 = $"⑥ VERDICT — {verdict}\n" +
                        $"  effective {seg.predEffectiveWidth:F2}m " +
                        (seg.predIsBelowSafeMargin ? "<" : "≥") +
                        $" MIN_SAFE {MIN_SAFE_WIDTH:F1}m";
            GUIStyle s6Style = new GUIStyle(stageHeader);
            s6Style.normal.textColor = verdictColor;
            GUI.Label(new Rect(x, ly, textW, lineH * 2), s6, s6Style);
            // 최종 안전도 bar (effW / MIN_SAFE)
            float safetyPct = Mathf.Clamp01(seg.predEffectiveWidth / (MIN_SAFE_WIDTH * 1.5f));
            DrawSafetyBar(barX, ly + 4, BAR_W, BAR_H, safetyPct);
            ly += lineH * 2 + 4;
        }

        /// <summary>침해율 mini bar — 적색 fill (값이 클수록 위험).</summary>
        private static void DrawProgressBar(float x, float y, float w, float h, float pct, string label)
        {
            EditorGUI.DrawRect(new Rect(x, y, w, h), new Color(0.15f, 0.15f, 0.18f, 0.85f));
            float fillW = Mathf.Clamp01(pct) * w;
            // 색상: 0=녹 → 0.5=황 → 1=적
            Color fillColor = pct < 0.5f
                ? Color.Lerp(new Color(0.3f, 0.85f, 0.4f), new Color(1f, 0.85f, 0.2f), pct * 2f)
                : Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(1f, 0.3f, 0.3f), (pct - 0.5f) * 2f);
            EditorGUI.DrawRect(new Rect(x, y, fillW, h), fillColor);
            // 테두리
            DrawRectBorder(new Rect(x, y, w, h), new Color(0.4f, 0.4f, 0.5f, 0.8f));
        }

        /// <summary>안전도 bar — 녹색 fill (값이 클수록 안전).</summary>
        private static void DrawSafetyBar(float x, float y, float w, float h, float pct)
        {
            EditorGUI.DrawRect(new Rect(x, y, w, h), new Color(0.15f, 0.15f, 0.18f, 0.85f));
            float fillW = Mathf.Clamp01(pct) * w;
            Color fillColor = pct < 0.3f
                ? new Color(1f, 0.3f, 0.3f)
                : (pct < 0.6f ? new Color(1f, 0.85f, 0.2f) : new Color(0.3f, 0.95f, 0.4f));
            EditorGUI.DrawRect(new Rect(x, y, fillW, h), fillColor);
            DrawRectBorder(new Rect(x, y, w, h), new Color(0.4f, 0.4f, 0.5f, 0.8f));
        }

        /// <summary>Rect 테두리 (4 lines).</summary>
        private static void DrawRectBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y + r.height - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.x + r.width - 1, r.y, 1, r.height), c);
        }

        // ──────────────────────────────────────────────────────────────────
        // [신규] Segment selector mini stacked bar graph
        //   각 segment(0~9)에 대해 6 risk metric을 색상별 누적 bar로 시각화
        //   클릭 시 해당 segment 선택, 우상단 "All" 박스도 클릭 가능
        // ──────────────────────────────────────────────────────────────────
        private static void DrawSegmentSelectorGraph(
            PassageMetric.PassageProfile profile,
            float x, float y, float w, float h,
            int passageIdx, int selectedSeg, PreviewInputs inputs)
        {
            if (profile.segments == null || profile.segments.Length == 0) return;
            int numSegs = profile.segments.Length;

            // 좌측: "All" 박스 (24px) | 우측: numSegs cells
            float allBoxW = 24f;
            float gap = 2f;
            float cellsAreaX = x + allBoxW + gap;
            float cellsAreaW = w - allBoxW - gap;
            float cellW = cellsAreaW / numSegs;

            // Risk metric 색상 (LWBMCY)
            //   L=blue, W=red(narrow), B=cyan(blend), M=magenta(mix), C=orange(curve), Y=green(waypoint)
            Color cL = new Color(0.40f, 0.60f, 1.00f);  // L Length
            Color cW = new Color(0.95f, 0.45f, 0.40f);  // W Narrow
            Color cB = new Color(0.30f, 0.85f, 0.95f);  // B Blend
            Color cM = new Color(0.85f, 0.45f, 0.95f);  // M Mix
            Color cC = new Color(1.00f, 0.65f, 0.25f);  // C Curve
            Color cY = new Color(0.45f, 0.85f, 0.45f);  // Y Waypoint

            // ── (1) "All" 박스 ──
            Rect allRect = new Rect(x, y, allBoxW, h);
            EditorGUI.DrawRect(allRect, (selectedSeg < 0)
                ? new Color(0.25f, 0.30f, 0.4f) : new Color(0.10f, 0.12f, 0.16f));
            // 테두리
            DrawCellBorder(allRect, (selectedSeg < 0)
                ? new Color(1f, 0.78f, 0.4f) : new Color(0.4f, 0.4f, 0.5f));
            // "All" 텍스트
            GUIStyle allTextStyle = new GUIStyle(EditorStyles.label);
            allTextStyle.fontSize = 9;
            allTextStyle.alignment = TextAnchor.MiddleCenter;
            allTextStyle.normal.textColor = (selectedSeg < 0) ? new Color(1f, 0.85f, 0.4f) : Color.white;
            allTextStyle.fontStyle = FontStyle.Bold;
            GUI.Label(allRect, "All", allTextStyle);

            // 클릭 처리
            if (Event.current.type == EventType.MouseDown
                && allRect.Contains(Event.current.mousePosition))
            {
                if (inputs.selectedSegmentPerPassage.ContainsKey(passageIdx))
                    inputs.selectedSegmentPerPassage.Remove(passageIdx);
                Event.current.Use();
                SceneView.RepaintAll();
            }

            // ── (2) Segment cells (mini stacked bar) ──
            for (int i = 0; i < numSegs; i++)
            {
                var seg = profile.segments[i];
                float cx = cellsAreaX + i * cellW;
                Rect cellRect = new Rect(cx, y, cellW - 0.5f, h);

                // BG
                bool isSelected = (selectedSeg == i);
                EditorGUI.DrawRect(cellRect, isSelected
                    ? new Color(0.20f, 0.20f, 0.22f)
                    : new Color(0.10f, 0.10f, 0.12f));

                // ── Stacked bar: 6 metrics, 가중치 적용해 누적 (LWBMCY 순) ──
                // 각 metric의 가중치(20/25/20/15/10/10 %) × value
                float wL = seg.segLengthRisk * 0.20f;
                float wW = seg.segNarrowRisk * 0.25f;
                float wB = seg.segBlendRisk * 0.20f;
                float wM = seg.segMixRisk * 0.15f;
                float wC = seg.segCurvatureRisk * 0.10f;
                float wY = seg.segWaypointRisk * 0.10f;
                float total = wL + wW + wB + wM + wC + wY;

                // 막대는 cellRect 내부에서 위에서 아래로 누적
                //   total 1.0 = 전체 cell 높이
                float innerPad = 1f;
                float innerX = cellRect.x + innerPad;
                float innerW = cellRect.width - innerPad * 2;
                float innerH = cellRect.height - innerPad * 2 - 8;  // 하단 8px = segment idx 표시
                float innerYBase = cellRect.y + innerPad + innerH;  // 바닥 기준

                float yCursor = innerYBase;
                // L bottom-up 쌓기
                if (wL > 0.001f) { float bh = innerH * wL; yCursor -= bh; EditorGUI.DrawRect(new Rect(innerX, yCursor, innerW, bh), cL); }
                if (wW > 0.001f) { float bh = innerH * wW; yCursor -= bh; EditorGUI.DrawRect(new Rect(innerX, yCursor, innerW, bh), cW); }
                if (wB > 0.001f) { float bh = innerH * wB; yCursor -= bh; EditorGUI.DrawRect(new Rect(innerX, yCursor, innerW, bh), cB); }
                if (wM > 0.001f) { float bh = innerH * wM; yCursor -= bh; EditorGUI.DrawRect(new Rect(innerX, yCursor, innerW, bh), cM); }
                if (wC > 0.001f) { float bh = innerH * wC; yCursor -= bh; EditorGUI.DrawRect(new Rect(innerX, yCursor, innerW, bh), cC); }
                if (wY > 0.001f) { float bh = innerH * wY; yCursor -= bh; EditorGUI.DrawRect(new Rect(innerX, yCursor, innerW, bh), cY); }

                // 하단 segment idx
                GUIStyle idxStyle = new GUIStyle(EditorStyles.label);
                idxStyle.fontSize = 8;
                idxStyle.alignment = TextAnchor.MiddleCenter;
                idxStyle.normal.textColor = isSelected ? Color.yellow : new Color(0.6f, 0.6f, 0.6f);
                idxStyle.padding = new RectOffset(0, 0, 0, 0);
                GUI.Label(new Rect(cellRect.x, cellRect.y + cellRect.height - 9, cellRect.width, 8),
                          i.ToString(), idxStyle);

                // 선택된 경우 테두리 강조
                if (isSelected)
                    DrawCellBorder(cellRect, new Color(1f, 0.85f, 0.3f));

                // 클릭 처리
                if (Event.current.type == EventType.MouseDown
                    && cellRect.Contains(Event.current.mousePosition))
                {
                    inputs.selectedSegmentPerPassage[passageIdx] = i;
                    Event.current.Use();
                    SceneView.RepaintAll();
                }
            }
        }

        /// <summary>1픽셀 두께 사각형 테두리.</summary>
        private static void DrawCellBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y + r.height - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.x + r.width - 1, r.y, 1, r.height), c);
        }

        /// <summary>10-segment width 변화 bar 그래프 (mini histogram).</summary>
        private static void DrawWidthProfileBar(
            PassageMetric.PassageProfile profile, float x, float y, float w, float h)
        {
            if (profile.segments == null || profile.segments.Length == 0) return;

            float maxW = Mathf.Max(profile.maxLocalWidth, 0.1f);
            float segW = w / profile.segments.Length;

            for (int i = 0; i < profile.segments.Length; i++)
            {
                float lw = profile.segments[i].localWidth;
                float ratio = Mathf.Clamp01(lw / maxW);
                float barH = h * ratio;
                float bx = x + segW * i;
                float by = y + (h - barH);

                Color c = Color.Lerp(new Color(0.3f, 0.7f, 0.3f), new Color(0.4f, 0.85f, 0.4f), ratio);
                EditorGUI.DrawRect(new Rect(bx, by, segW - 1f, barH), c);
            }
        }

        /// <summary>10-segment biome 변화 bar 그래프 (색상으로 biome 표시).</summary>
        private static void DrawBiomeProfileBar(
            PassageMetric.PassageProfile profile, float x, float y, float w, float h)
        {
            if (profile.segments == null || profile.segments.Length == 0) return;

            float segW = w / profile.segments.Length;
            for (int i = 0; i < profile.segments.Length; i++)
            {
                float bx = x + segW * i;
                int bi = profile.segments[i].biomeIdx;
                Color c = GetBiomePreviewColor(bi);
                // blend centrality에 따라 어둡게 (중심에서 mix 표시)
                float bc = profile.segments[i].blendCentrality;
                c = Color.Lerp(c, c * 0.55f, bc);

                EditorGUI.DrawRect(new Rect(bx, y, segW - 1f, h), c);
            }
        }

        /// <summary>Risk 1줄 그리기: "Label  ███▃▃▃▃▃▃▃ 0.42"</summary>
        private static void DrawRiskBreakdownLine(float x, float y, string label, float val, float weight)
        {
            GUIStyle keyStyle = new GUIStyle(EditorStyles.label);
            keyStyle.fontSize = 9;
            keyStyle.normal.textColor = new Color(0.7f, 0.8f, 1f);
            keyStyle.padding = new RectOffset(8, 4, 0, 0);
            keyStyle.alignment = TextAnchor.MiddleLeft;

            GUIStyle valStyle = new GUIStyle(EditorStyles.label);
            valStyle.fontSize = 9;
            valStyle.padding = new RectOffset(2, 4, 0, 0);
            valStyle.alignment = TextAnchor.MiddleRight;

            // Label
            GUI.Label(new Rect(x, y, 78, 12), label, keyStyle);

            // Bar (140 wide)
            float barX = x + 80;
            float barW = 140f;
            // BG
            EditorGUI.DrawRect(new Rect(barX, y + 2, barW, 8), new Color(0.15f, 0.15f, 0.18f));
            // Fill
            float fillW = Mathf.Clamp01(val) * barW;
            Color fc = (val < 0.3f) ? new Color(0.3f, 0.75f, 0.35f)
                     : (val < 0.6f) ? new Color(0.85f, 0.65f, 0.2f)
                     : new Color(0.9f, 0.32f, 0.25f);
            EditorGUI.DrawRect(new Rect(barX, y + 2, fillW, 8), fc);

            // Numeric value (right side)
            valStyle.normal.textColor = (val > 0.5f) ? new Color(1f, 0.8f, 0.4f) : new Color(0.85f, 0.85f, 0.85f);
            GUI.Label(new Rect(barX + barW + 4, y, 40, 12),
                $"{val:F2}×{weight:F2}", valStyle);
        }

        /// <summary>Passage Junction Type → 짧은 표시명.</summary>
        private static string GetPassageTypeShort(PassageMetric.PassageJunctionType type)
        {
            switch (type)
            {
                case PassageMetric.PassageJunctionType.Crawl:    return "Crawl";
                case PassageMetric.PassageJunctionType.Corridor: return "Corridor";
                case PassageMetric.PassageJunctionType.Gallery:  return "Gallery";
                case PassageMetric.PassageJunctionType.Chimney:  return "Chimney";
                case PassageMetric.PassageJunctionType.Ramp:     return "Ramp";
                case PassageMetric.PassageJunctionType.Bridge:   return "Bridge";
                case PassageMetric.PassageJunctionType.Collapse: return "Collapse";
                case PassageMetric.PassageJunctionType.Spiral:   return "Spiral";
            }
            return "Passage";
        }

        /// <summary>Biome idx → preview 색상 (간단 LUT).</summary>
        private static Color GetBiomePreviewColor(int biomeIdx)
        {
            switch (biomeIdx)
            {
                case 0: return new Color(0.45f, 0.62f, 0.78f);  // Karst — blue-gray
                case 1: return new Color(0.78f, 0.55f, 0.35f);  // Columnar — orange
                case 2: return new Color(0.72f, 0.68f, 0.50f);  // Sedimentary — tan
                case 3: return new Color(0.62f, 0.45f, 0.78f);  // Colonnade — purple
                case 4: return new Color(0.55f, 0.55f, 0.58f);  // Rocky — gray
                case 5: return new Color(0.78f, 0.68f, 0.42f);  // Canyon — sand
                case 6: return new Color(0.35f, 0.65f, 0.78f);  // Marine — teal
            }
            return new Color(0.5f, 0.5f, 0.5f);
        }

        // ══════════════════════════════════════════════════════════════════
        // [Stage 3-F] Edge 클릭 감지
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Mouse click 시 가장 가까운 passage 찾아 selectedPassageIdx 설정.
        /// BiomeDebugVisualizer가 OnSceneGUI에서 호출.
        /// </summary>
        public static int FindNearestPassageAt(
            Vector3 mouseWorldOrigin, Vector3 mouseWorldDir,
            float maxDistance = 100f)
        {
            var builder = CaveNodeGraphBuilder.Instance;
            if (builder == null || builder.edgesData == null) return -1;

            int bestIdx = -1;
            float bestDist = maxDistance;

            for (int i = 0; i < builder.edgesData.Count; i++)
            {
                var e = builder.edgesData[i];
                Vector3 mid = (e.startPos + e.endPos) * 0.5f;

                // Ray-segment 거리 (간단 — closest point 비교)
                float rayD = Vector3.Cross(mouseWorldDir, mid - mouseWorldOrigin).magnitude;
                if (rayD < bestDist)
                {
                    bestDist = rayD;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        // ══════════════════════════════════════════════════════════════════

        // ──────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────
        private static bool HasFlag(FeatureFlags flags, FeatureFlags check)
        {
            return (flags & check) != 0;
        }

        private static Color GetRoomTypeColor(int rt)
        {
            switch (rt)
            {
                case 1: return Color.green;
                case 2: return Color.red;
                case 3: return new Color(1f, 0.84f, 0f);
                case 4: return Color.cyan;
                default: return Color.white;
            }
        }

        private static string GetRoomTypeName(int rt)
        {
            switch (rt)
            {
                case 1: return "Spawn";
                case 2: return "Boss";
                case 3: return "Treasure";
                case 4: return "Sinkhole";
                default: return "";  // Normal은 라벨 없음 (과밀 방지)
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // [Phase 4.5-D] Highlight 애니메이션 관리
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 펼쳐진 biome / Info Panel 상태와 비교하여 노드별 highlight target 조정.
        /// Biome 라벨 클릭 시: 포함 노드들 → highlight 1 (EaseOutBack, stagger)
        /// Info Panel 클릭 시: 해당 노드 → highlight 1
        /// 아무 선택 없음: 모두 → highlight 0 (EaseInCubic)
        /// </summary>
        private static void UpdateHighlightAnimations(PreviewData data, PreviewInputs inputs)
        {
            if (data == null || data.nodes == null) return;

            // 1. 이 프레임의 "활성" 노드 인덱스 집합 수집
            HashSet<int> activeNodeIndices = new HashSet<int>();

            // 1-1. 펼쳐진 biome → 포함 노드
            if (inputs.expandedBiomeNoiseTypes != null && inputs.expandedBiomeNoiseTypes.Count > 0
                && inputs.biomes != null && inputs.biomes.Count > 0)
            {
                for (int i = 0; i < data.nodes.Count; i++)
                {
                    var n = data.nodes[i];
                    var sample = BiomeSampler.SampleAt(n.position, inputs.biomes.Count,
                        inputs.macroBiomeScale, null, null, inputs.balanceBiomes);
                    int dominantIdx = sample.blendWeight < 0.5f ? sample.biomeIdxA : sample.biomeIdxB;
                    if (dominantIdx < 0 || dominantIdx >= inputs.biomes.Count) continue;
                    if (inputs.biomes[dominantIdx] == null) continue;
                    int noiseType = inputs.biomes[dominantIdx].noiseType;
                    if (inputs.expandedBiomeNoiseTypes.Contains(noiseType))
                    {
                        activeNodeIndices.Add(i);
                    }
                }
            }

            // 1-2. 펼쳐진 노드 (Info Panel 클릭)도 highlight
            if (inputs.expandedNodeIndices != null)
            {
                foreach (var idx in inputs.expandedNodeIndices)
                    activeNodeIndices.Add(idx);
            }

            // 2. 상태 변화 감지 → 애니메이션 trigger
            //    stagger effect: 새로 활성화된 노드만 delay 부여
            bool biomeChanged = !_lastExpandedBiomeTypes.SetEquals(inputs.expandedBiomeNoiseTypes);
            int staggerIdx = 0;

            // 2-1. 활성 노드 → target 1.0
            foreach (var idx in activeNodeIndices)
            {
                if (!_nodeHighlightStates.TryGetValue(idx, out var state))
                {
                    state = new AnimationState();
                    _nodeHighlightStates[idx] = state;
                    AnimationCoordinator.Register(state);
                }
                // stagger delay (biome 클릭 시만)
                float delay = biomeChanged ? staggerIdx * 0.03f : 0f;
                state.SetTarget(1.0f, 0.3f, Easing.EaseOutBack, delay);
                staggerIdx++;
            }

            // 2-2. 비활성이지만 highlight 남은 노드 → target 0
            var toRemove = new List<int>();
            foreach (var kv in _nodeHighlightStates)
            {
                if (!activeNodeIndices.Contains(kv.Key))
                {
                    if (kv.Value.targetValue > 0.001f)
                    {
                        kv.Value.SetTarget(0.0f, 0.2f, Easing.EaseInCubic);
                    }
                    // 완료된 state 제거 (메모리 정리)
                    if (!kv.Value.IsAnimating && kv.Value.currentValue < 0.001f)
                    {
                        toRemove.Add(kv.Key);
                    }
                }
            }
            foreach (var idx in toRemove)
            {
                AnimationCoordinator.Unregister(_nodeHighlightStates[idx]);
                _nodeHighlightStates.Remove(idx);
            }

            // 3. 현재 상태 저장 (다음 frame 비교용)
            _lastExpandedBiomeTypes = new HashSet<int>(inputs.expandedBiomeNoiseTypes);
            _lastExpandedNodeIndices = new HashSet<int>(inputs.expandedNodeIndices);
        }

        /// <summary>노드 인덱스에 대한 현재 highlight 값 반환 (0~1).</summary>
        public static float GetNodeHighlight(int nodeIdx)
        {
            if (_nodeHighlightStates.TryGetValue(nodeIdx, out var state))
                return state.currentValue;
            return 0f;
        }

        // ══════════════════════════════════════════════════════════════════
        // [Phase 4.5-D 제안 3] Node Clustering Heatmap
        //   XZ grid 각 셀마다 인접 노드 개수를 카운트.
        //   색상: 낮음(파랑) → 중간(녹색) → 높음(빨강) 그라디언트.
        //   용도: "노드가 한 곳에 너무 몰렸는지" 시각적으로 판단.
        // ══════════════════════════════════════════════════════════════════
        private static void DrawNodeClusteringHeatmap(PreviewData data, PreviewInputs inputs)
        {
            if (data == null || data.nodes == null || data.nodes.Count == 0) return;

            // Grid 설정: heatmap grid와 같은 cell size 사용 (일관성)
            float gridSize = data.actualGridSize > 0.01f ? data.actualGridSize : 10f;
            Vector3 bounds = inputs.dungeonBounds;
            float radius = gridSize * 1.5f;  // 각 cell center 중심으로 이 반경 내 노드 카운트

            // Grid 순회 — XZ 평면만 (Y는 dungeonBounds.y 중심)
            int cellsX = Mathf.CeilToInt(bounds.x / gridSize);
            int cellsZ = Mathf.CeilToInt(bounds.z / gridSize);

            // 성능 가드
            const int MAX_CLUSTER_CELLS = 3000;
            if (cellsX * cellsZ > MAX_CLUSTER_CELLS)
            {
                float shrink = Mathf.Sqrt((float)(cellsX * cellsZ) / MAX_CLUSTER_CELLS);
                gridSize *= shrink;
                radius = gridSize * 1.5f;
                cellsX = Mathf.CeilToInt(bounds.x / gridSize);
                cellsZ = Mathf.CeilToInt(bounds.z / gridSize);
            }

            // 각 cell별 density 계산 + max 트래킹 (정규화용)
            int[,] densityMap = new int[cellsX, cellsZ];
            int maxDensity = 0;

            for (int ix = 0; ix < cellsX; ix++)
            {
                float wx = -bounds.x * 0.5f + (ix + 0.5f) * gridSize;
                for (int iz = 0; iz < cellsZ; iz++)
                {
                    float wz = -bounds.z * 0.5f + (iz + 0.5f) * gridSize;
                    Vector2 cellXZ = new Vector2(wx, wz);

                    int count = 0;
                    foreach (var n in data.nodes)
                    {
                        Vector2 nodeXZ = new Vector2(n.position.x, n.position.z);
                        if (Vector2.Distance(cellXZ, nodeXZ) <= radius)
                            count++;
                    }
                    densityMap[ix, iz] = count;
                    if (count > maxDensity) maxDensity = count;
                }
            }

            if (maxDensity == 0) return;

            // 색상: blue(낮음) → green(중간) → red(높음)
            float heatmapY = inputs.heatmapYPlane + 0.12f;  // biome heatmap 위에 살짝 띄움
            for (int ix = 0; ix < cellsX; ix++)
            {
                float wx = -bounds.x * 0.5f + (ix + 0.5f) * gridSize;
                for (int iz = 0; iz < cellsZ; iz++)
                {
                    int count = densityMap[ix, iz];
                    if (count == 0) continue;  // 빈 셀은 스킵

                    float wz = -bounds.z * 0.5f + (iz + 0.5f) * gridSize;
                    float t = (float)count / maxDensity;

                    // blue → green → red
                    Color c;
                    if (t < 0.5f)
                        c = Color.Lerp(new Color(0.2f, 0.4f, 1f), new Color(0.2f, 1f, 0.3f), t * 2f);
                    else
                        c = Color.Lerp(new Color(0.2f, 1f, 0.3f), new Color(1f, 0.2f, 0.2f), (t - 0.5f) * 2f);
                    c.a = 0.35f + t * 0.35f;  // 밀집 높을수록 더 진하게

                    Vector3 pos = new Vector3(wx, heatmapY, wz);
                    Vector3 cellMin = pos - new Vector3(gridSize * 0.5f, 0f, gridSize * 0.5f);
                    Vector3[] corners =
                    {
                        cellMin,
                        cellMin + new Vector3(gridSize, 0, 0),
                        cellMin + new Vector3(gridSize, 0, gridSize),
                        cellMin + new Vector3(0, 0, gridSize),
                    };
                    Handles.DrawSolidRectangleWithOutline(corners, c,
                        new Color(c.r, c.g, c.b, 0.1f));

                    // 밀집 높은 셀(상위 20%)에는 숫자 표시
                    if (t > 0.7f)
                    {
                        Camera sceneCam = SceneView.lastActiveSceneView?.camera;
                        if (sceneCam != null)
                        {
                            float dist = Vector3.Distance(sceneCam.transform.position, pos);
                            if (dist < gridSize * 30f)
                            {
                                GUIStyle style = new GUIStyle();
                                style.fontSize = 10;
                                style.fontStyle = FontStyle.Bold;
                                style.normal.textColor = Color.white;
                                style.alignment = TextAnchor.MiddleCenter;
                                Handles.Label(pos, $"{count}", style);
                            }
                        }
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // [Phase 4.5-D 제안 4] Cross-section View — XY / YZ 수직 단면
        //   crossAxis = XY → Z = crossPlaneValue 로 고정, X-Y 단면
        //   crossAxis = YZ → X = crossPlaneValue 로 고정, Y-Z 단면
        //   Cut plane 렌더 + 절단면 근처 노드/엣지 강조
        // ══════════════════════════════════════════════════════════════════
        private static void DrawCrossSectionOverlay(PreviewData data, PreviewInputs inputs)
        {
            if (data == null || data.nodes == null) return;
            Vector3 bounds = inputs.dungeonBounds;
            float planeVal = inputs.crossPlaneValue;

            // Cut plane 시각화 (반투명 사각형)
            Vector3[] corners;
            Vector3 planeNormal;
            if (inputs.crossAxis == CrossSectionAxis.XY)
            {
                // Z = planeVal 에 XY 평면 (세로로 세운 벽)
                float h = bounds.y;
                float w = bounds.x;
                corners = new Vector3[]
                {
                    new Vector3(-w * 0.5f, -h * 0.5f, planeVal),
                    new Vector3( w * 0.5f, -h * 0.5f, planeVal),
                    new Vector3( w * 0.5f,  h * 0.5f, planeVal),
                    new Vector3(-w * 0.5f,  h * 0.5f, planeVal),
                };
                planeNormal = Vector3.forward;
            }
            else // YZ
            {
                // X = planeVal 에 YZ 평면
                float h = bounds.y;
                float d = bounds.z;
                corners = new Vector3[]
                {
                    new Vector3(planeVal, -h * 0.5f, -d * 0.5f),
                    new Vector3(planeVal,  h * 0.5f, -d * 0.5f),
                    new Vector3(planeVal,  h * 0.5f,  d * 0.5f),
                    new Vector3(planeVal, -h * 0.5f,  d * 0.5f),
                };
                planeNormal = Vector3.right;
            }

            // 반투명 면 + 외곽선
            Color planeC = new Color(1f, 0.9f, 0.3f, 0.12f);
            Color planeOutline = new Color(1f, 0.9f, 0.3f, 0.85f);
            Handles.DrawSolidRectangleWithOutline(corners, planeC, planeOutline);

            // 절단면 근처 노드 (거리 5m 내) 강조
            float hitThreshold = 5f;
            foreach (var n in data.nodes)
            {
                float dist = inputs.crossAxis == CrossSectionAxis.XY
                    ? Mathf.Abs(n.position.z - planeVal)
                    : Mathf.Abs(n.position.x - planeVal);

                if (dist < hitThreshold + n.radius)
                {
                    Color c = GetRoomTypeColor(n.roomType);
                    c.a = 0.9f;
                    Handles.color = c;
                    Handles.DrawWireDisc(n.position, planeNormal, n.radius * 1.1f);

                    // 노드 label
                    Handles.Label(n.position + Vector3.up * (n.radius + 0.5f),
                        $"{GetRoomTypeName(n.roomType)} Y={n.position.y:F1}");
                }
            }

            // 절단면과 교차하는 edge 강조
            foreach (var e in data.edges)
            {
                bool crosses;
                if (inputs.crossAxis == CrossSectionAxis.XY)
                {
                    crosses = (e.startPos.z - planeVal) * (e.endPos.z - planeVal) <= 0;
                }
                else
                {
                    crosses = (e.startPos.x - planeVal) * (e.endPos.x - planeVal) <= 0;
                }

                if (crosses)
                {
                    Handles.color = new Color(1f, 0.9f, 0.3f, 0.8f);
                    Handles.DrawAAPolyLine(5f, new Vector3[] { e.startPos, e.endPos });

                    // 교차점 계산 후 표시
                    float t;
                    if (inputs.crossAxis == CrossSectionAxis.XY)
                    {
                        float denom = e.endPos.z - e.startPos.z;
                        t = Mathf.Abs(denom) > 0.001f
                            ? (planeVal - e.startPos.z) / denom
                            : 0.5f;
                    }
                    else
                    {
                        float denom = e.endPos.x - e.startPos.x;
                        t = Mathf.Abs(denom) > 0.001f
                            ? (planeVal - e.startPos.x) / denom
                            : 0.5f;
                    }
                    t = Mathf.Clamp01(t);
                    Vector3 intersect = Vector3.Lerp(e.startPos, e.endPos, t);
                    Handles.color = Color.yellow;
                    Handles.SphereHandleCap(0, intersect, Quaternion.identity, 0.8f, EventType.Repaint);
                }
            }

            // Plane 정보 라벨
            Vector3 labelPos = inputs.crossAxis == CrossSectionAxis.XY
                ? new Vector3(0, bounds.y * 0.5f + 3f, planeVal)
                : new Vector3(planeVal, bounds.y * 0.5f + 3f, 0);
            GUIStyle bigStyle = new GUIStyle();
            bigStyle.fontSize = 12;
            bigStyle.fontStyle = FontStyle.Bold;
            bigStyle.normal.textColor = new Color(1f, 0.9f, 0.3f);
            bigStyle.alignment = TextAnchor.MiddleCenter;
            string axisLabel = inputs.crossAxis == CrossSectionAxis.XY
                ? $"Cross-Section: XY plane @ Z={planeVal:F1}"
                : $"Cross-Section: YZ plane @ X={planeVal:F1}";
            Handles.Label(labelPos, axisLabel, bigStyle);
        }
    }
}
#endif
