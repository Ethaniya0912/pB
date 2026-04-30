#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-E E.5] PreviewMeshRenderer — SDF→MC→Mesh 파이프라인
    //
    // 역할:
    //   1. PreviewData + Range 설정 → SDF grid 샘플링
    //   2. MarchingCubes 호출 → vertices/triangles
    //   3. Unity Mesh 생성
    //   4. Scene에 wireframe + 반투명 surface 렌더
    //   5. Debounce regenerate (슬라이더 드래그 중 빈번한 생성 방지)
    //
    // byte-identical 보장: enableMCPreview 토글 false면 이 메서드 호출 전혀 없음.
    //   Material/Mesh 리소스도 생성 안 됨.
    // ══════════════════════════════════════════════════════════════════════

    public enum PreviewMeshRangeMode
    {
        SingleRoom = 0,      // 선택 node 중심 50m 반경 (기본)
        CriticalPath = 1,    // spawn→boss 경로 포함 영역
        CustomRadius = 2,    // 사용자 slider 10~200m
        FullDungeon = 3,     // 전체 dungeon (저해상도 자동)
    }

    /// <summary>[Phase 4.5-E hotfix v2] MC mesh 렌더 모드.</summary>
    public enum PreviewMCRenderMode
    {
        Wireframe = 0,   // 선만 (라벨 안 가림, 성능 유리)
        Surface = 1,     // 반투명 solid (biome 색 식별 용이)
        Both = 2,        // surface + wireframe 중첩 (최대 정보, 라벨 가려질 수 있음)
    }

    /// <summary>[Phase 4.5-F hotfix v4] Show Edges 렌더 모드 — 방안 A/B/C 전환.</summary>
    public enum EdgeDisplayMode
    {
        Line = 0,           // 방안 A 기본 — 단순 AAPolyLine
        CapsuleWire = 1,    // 방안 A+B — edge.width 반영 3-axis wireframe disc
        MeshOutline = 2,    // 방안 C — DC runtime mesh boundary 추출 + 단순화 (cache)
    }

    public class PreviewMeshRenderer
    {
        // 생성된 mesh + 메타
        private Mesh _previewMesh;
        private Material _wireframeMat;
        private Material _surfaceMat;

        // 마지막 생성 파라미터 (변화 감지용)
        private int _lastParamHash;
        private double _lastGenerateTime;
        private const double DEBOUNCE_SECONDS = 0.15;
        private bool _pendingRegenerate;

        // 생성 통계 (UI 표시용)
        public int LastVertexCount { get; private set; }
        public int LastTriangleCount { get; private set; }
        public float LastGenerateMs { get; private set; }

        // ──────────────────────────────────────────────────────────────
        // Main API
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Enable 토글 + 파라미터 변화 감지 → Debounce 후 regenerate.
        /// 매 Render 호출에서 이 메서드 통해 상태 유지.
        /// </summary>
        public void UpdateIfNeeded(PreviewData data, PreviewInputs inputs)
        {
            if (!inputs.enableMCPreview)
            {
                // 토글 OFF — mesh 정리
                if (_previewMesh != null)
                {
                    Object.DestroyImmediate(_previewMesh);
                    _previewMesh = null;
                }
                return;
            }

            if (data == null || data.nodes == null || data.nodes.Count == 0) return;

            // 파라미터 hash 변화 감지
            int hash = ComputeParamHash(data, inputs);
            if (hash != _lastParamHash)
            {
                _lastParamHash = hash;
                _pendingRegenerate = true;
                _lastGenerateTime = EditorApplication.timeSinceStartup;
            }

            // Debounce 체크
            if (_pendingRegenerate
                && EditorApplication.timeSinceStartup - _lastGenerateTime >= DEBOUNCE_SECONDS)
            {
                _pendingRegenerate = false;
                RegenerateMesh(data, inputs);
            }
        }

        /// <summary>Scene에 mesh 렌더. Render() 메서드에서 호출.</summary>
        public void Draw(PreviewInputs inputs)
        {
            if (!inputs.enableMCPreview || _previewMesh == null) return;

            EnsureMaterials();

            // [hotfix v2] Wireframe vs Surface 모드
            //   Surface: solid shaded (vertex color), 반투명
            //   Wireframe: line only (vertex color)
            //   Both: wireframe 위에 반투명 surface 중첩
            if (inputs.mcRenderMode == PreviewMCRenderMode.Surface
                || inputs.mcRenderMode == PreviewMCRenderMode.Both)
            {
                // Solid surface (반투명 blend)
                _surfaceMat.SetPass(0);
                Graphics.DrawMeshNow(_previewMesh, Matrix4x4.identity);
            }

            if (inputs.mcRenderMode == PreviewMCRenderMode.Wireframe
                || inputs.mcRenderMode == PreviewMCRenderMode.Both)
            {
                // Wireframe — GL 모드 전환으로 line 렌더링
                GL.wireframe = true;
                _wireframeMat.SetPass(0);
                Graphics.DrawMeshNow(_previewMesh, Matrix4x4.identity);
                GL.wireframe = false;
            }
        }

        /// <summary>윈도우 종료 시 리소스 해제.</summary>
        public void Cleanup()
        {
            if (_previewMesh != null)
            {
                Object.DestroyImmediate(_previewMesh);
                _previewMesh = null;
            }
            if (_wireframeMat != null)
            {
                Object.DestroyImmediate(_wireframeMat);
                _wireframeMat = null;
            }
            if (_surfaceMat != null)
            {
                Object.DestroyImmediate(_surfaceMat);
                _surfaceMat = null;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Regenerate
        // ──────────────────────────────────────────────────────────────

        private void RegenerateMesh(PreviewData data, PreviewInputs inputs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // 1. Range 결정
            ComputeRange(data, inputs, out Vector3 center, out Vector3 size);

            // 2. Voxel 해상도 결정
            float voxelSize = ComputeVoxelSize(size, inputs);

            // 3. SDF context 준비
            var ctx = new PreviewSDFContext
            {
                nodes = data.nodes,
                edges = data.edges,
                biomes = inputs.biomes,
                sminStrength = Mathf.Max(inputs.mcSminStrength, 0.1f),
                floorAltitude = inputs.mcFloorAltitude,
                ceilAltitude = inputs.mcCeilAltitude,
                floorVarAmp = inputs.mcFloorVarAmp,
                ceilVarAmp = inputs.mcCeilVarAmp,
                floorNoiseScale = 0.05f,
                macroBiomeScale = inputs.macroBiomeScale,
                useSimplexNoise = BiomeSampler.UseSimplexNoise,
                balanceBiomes = inputs.balanceBiomes,
                enableCurvature = inputs.mcEnableCurvature,
                enableFloorVar = inputs.mcEnableFloorVar,
                enableBiomeBlend = inputs.mcEnableBiomeBlend,
                center = center,
                maxRange = Mathf.Max(size.x, size.z) * 0.6f,
            };
            ctx.PrepareNearbyCache();

            // 4. SDF grid 샘플링
            int resX = Mathf.CeilToInt(size.x / voxelSize) + 1;
            int resY = Mathf.CeilToInt(size.y / voxelSize) + 1;
            int resZ = Mathf.CeilToInt(size.z / voxelSize) + 1;

            // 성능 가드
            int totalCells = resX * resY * resZ;
            const int MAX_CELLS = 500000;  // ~100ms 상한
            if (totalCells > MAX_CELLS)
            {
                float shrink = Mathf.Pow((float)totalCells / MAX_CELLS, 1f / 3f);
                voxelSize *= shrink;
                resX = Mathf.CeilToInt(size.x / voxelSize) + 1;
                resY = Mathf.CeilToInt(size.y / voxelSize) + 1;
                resZ = Mathf.CeilToInt(size.z / voxelSize) + 1;
                Debug.LogWarning($"[PreviewMC] Cell count 초과 → voxelSize auto-adjust → {voxelSize:F2}m");
            }

            Vector3 origin = center - size * 0.5f;
            float[,,] sdfGrid = new float[resX, resY, resZ];

            for (int x = 0; x < resX; x++)
            {
                for (int y = 0; y < resY; y++)
                {
                    for (int z = 0; z < resZ; z++)
                    {
                        Vector3 p = origin + new Vector3(x, y, z) * voxelSize;
                        sdfGrid[x, y, z] = PreviewSDF.Evaluate(p, ctx);
                    }
                }
            }

            // 5. MC 실행
            var mcData = MarchingCubes.Generate(sdfGrid, origin, voxelSize, 0f);

            // 6. Unity Mesh 변환 + [Phase 4.5-E hotfix v2] Vertex color 샘플링
            if (_previewMesh == null)
            {
                _previewMesh = new Mesh { name = "PreviewMCMesh" };
                _previewMesh.hideFlags = HideFlags.HideAndDontSave;
                _previewMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            _previewMesh.Clear();
            if (mcData.vertices != null && mcData.vertices.Length > 0)
            {
                _previewMesh.vertices = mcData.vertices;
                _previewMesh.triangles = mcData.triangles;

                // [hotfix v2] 각 vertex에 biome color 할당 — blended
                Color[] colors = SampleVertexColors(mcData.vertices, inputs);
                _previewMesh.colors = colors;

                _previewMesh.RecalculateNormals();
                _previewMesh.RecalculateBounds();
            }

            sw.Stop();
            LastVertexCount = mcData.vertices?.Length ?? 0;
            LastTriangleCount = mcData.triangleCount;
            LastGenerateMs = (float)sw.Elapsed.TotalMilliseconds;

            Debug.Log($"[PreviewMC] Regenerate: {LastVertexCount} verts, " +
                      $"{LastTriangleCount} tris, {LastGenerateMs:F1}ms " +
                      $"(voxel {voxelSize:F2}m, cells {resX}x{resY}x{resZ})");

            SceneView.RepaintAll();
        }

        /// <summary>
        /// [Phase 4.5-E hotfix v2] 각 vertex 위치에서 biome 색상 샘플링.
        /// blendWeight 기반으로 biomeA/B 색상 lerp → cave wall이 biome마다 다른 색.
        /// </summary>
        private Color[] SampleVertexColors(Vector3[] verts, PreviewInputs inputs)
        {
            var colors = new Color[verts.Length];
            if (inputs.biomes == null || inputs.biomes.Count == 0)
            {
                // biome 없음 → 기본 cyan
                var defColor = new Color(0.5f, 0.85f, 1f, 1f);
                for (int i = 0; i < colors.Length; i++) colors[i] = defColor;
                return colors;
            }

            // Simplex 일시 적용 (복원됨)
            bool origSimplex = BiomeSampler.UseSimplexNoise;
            try
            {
                for (int i = 0; i < verts.Length; i++)
                {
                    var sample = BiomeSampler.SampleAt(verts[i], inputs.biomes.Count,
                        inputs.macroBiomeScale, null, null, inputs.balanceBiomes);

                    var bA = (sample.biomeIdxA >= 0 && sample.biomeIdxA < inputs.biomes.Count)
                        ? inputs.biomes[sample.biomeIdxA] : null;
                    var bB = (sample.biomeIdxB >= 0 && sample.biomeIdxB < inputs.biomes.Count)
                        ? inputs.biomes[sample.biomeIdxB] : null;

                    Color cA = (bA != null) ? BiomeSampler.GetBiomeColor(bA.noiseType) : Color.gray;
                    Color cB = (bB != null) ? BiomeSampler.GetBiomeColor(bB.noiseType) : cA;
                    Color blended = Color.Lerp(cA, cB, sample.blendWeight);
                    blended.a = 1f;
                    colors[i] = blended;
                }
            }
            finally
            {
                BiomeSampler.UseSimplexNoise = origSimplex;
            }
            return colors;
        }

        // ──────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────

        private void ComputeRange(PreviewData data, PreviewInputs inputs,
            out Vector3 center, out Vector3 size)
        {
            switch (inputs.mcRangeMode)
            {
                case PreviewMeshRangeMode.SingleRoom:
                    {
                        // 첫 번째 Spawn 또는 index 0 node 중심
                        NodeData focusNode = data.nodes[0];
                        foreach (var n in data.nodes)
                        {
                            if (n.roomType == 1) { focusNode = n; break; }  // Spawn
                        }
                        center = focusNode.position;
                        size = Vector3.one * 50f;
                        break;
                    }
                case PreviewMeshRangeMode.CriticalPath:
                    {
                        // Critical edges의 bounding box
                        Vector3 min = Vector3.positiveInfinity;
                        Vector3 max = Vector3.negativeInfinity;
                        bool any = false;
                        if (data.criticalEdgeIndices != null && data.criticalEdgeIndices.Count > 0)
                        {
                            foreach (var idx in data.criticalEdgeIndices)
                            {
                                if (idx < 0 || idx >= data.edges.Count) continue;
                                var e = data.edges[idx];
                                min = Vector3.Min(min, Vector3.Min(e.startPos, e.endPos));
                                max = Vector3.Max(max, Vector3.Max(e.startPos, e.endPos));
                                any = true;
                            }
                        }
                        if (!any)
                        {
                            // fallback: 모든 node
                            foreach (var n in data.nodes)
                            {
                                min = Vector3.Min(min, n.position);
                                max = Vector3.Max(max, n.position);
                            }
                        }
                        center = (min + max) * 0.5f;
                        size = (max - min) + Vector3.one * 20f;  // 여유
                        break;
                    }
                case PreviewMeshRangeMode.CustomRadius:
                    center = Vector3.zero;  // dungeon 중심
                    float r = Mathf.Clamp(inputs.mcCustomRadius, 10f, 200f);
                    size = new Vector3(r * 2f, Mathf.Min(r * 2f, inputs.dungeonBounds.y), r * 2f);
                    break;
                case PreviewMeshRangeMode.FullDungeon:
                default:
                    center = Vector3.zero;
                    size = inputs.dungeonBounds;
                    break;
            }
        }

        private float ComputeVoxelSize(Vector3 size, PreviewInputs inputs)
        {
            // 사용자 slider 값
            float userVoxel = Mathf.Max(inputs.mcVoxelSize, 0.1f);

            // FullDungeon은 자동으로 큰 voxel
            if (inputs.mcRangeMode == PreviewMeshRangeMode.FullDungeon)
            {
                return Mathf.Max(userVoxel, 2.0f);
            }
            return userVoxel;
        }

        private int ComputeParamHash(PreviewData data, PreviewInputs inputs)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + inputs.seed;
                h = h * 31 + (int)inputs.mcRangeMode;
                h = h * 31 + inputs.mcCustomRadius.GetHashCode();
                h = h * 31 + inputs.mcVoxelSize.GetHashCode();
                h = h * 31 + inputs.mcSminStrength.GetHashCode();
                h = h * 31 + inputs.mcFloorAltitude.GetHashCode();
                h = h * 31 + inputs.mcCeilAltitude.GetHashCode();
                h = h * 31 + inputs.mcFloorVarAmp.GetHashCode();
                h = h * 31 + inputs.mcCeilVarAmp.GetHashCode();
                h = h * 31 + (inputs.mcEnableCurvature ? 1 : 0);
                h = h * 31 + (inputs.mcEnableFloorVar ? 1 : 0);
                h = h * 31 + (inputs.mcEnableBiomeBlend ? 1 : 0);
                h = h * 31 + (BiomeSampler.UseSimplexNoise ? 1 : 0);
                h = h * 31 + (inputs.balanceBiomes ? 1 : 0);
                h = h * 31 + data.nodes.Count;
                h = h * 31 + data.edges.Count;
                h = h * 31 + inputs.dungeonBounds.GetHashCode();
                // Note: mcRenderMode는 제외 — mesh 자체는 동일, Draw만 다름 (regenerate 불필요)
                return h;
            }
        }

        private void EnsureMaterials()
        {
            // [hotfix v2] Internal-Colored는 vertex color를 지원 (SHADER_TAG _Color와 별도)
            //   surface = 반투명 solid (SrcAlpha/OneMinusSrcAlpha)
            //   wireframe = 불투명 line 렌더용
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            if (_surfaceMat == null)
            {
                _surfaceMat = new Material(shader);
                _surfaceMat.hideFlags = HideFlags.HideAndDontSave;
                _surfaceMat.SetColor("_Color", new Color(1f, 1f, 1f, 0.45f));  // white = vertex color 그대로
                _surfaceMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _surfaceMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _surfaceMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _surfaceMat.SetInt("_ZWrite", 0);
                _surfaceMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            }

            if (_wireframeMat == null)
            {
                _wireframeMat = new Material(shader);
                _wireframeMat.hideFlags = HideFlags.HideAndDontSave;
                _wireframeMat.SetColor("_Color", new Color(1f, 1f, 1f, 1f));
                // Wireframe은 불투명 line
                _wireframeMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                _wireframeMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                _wireframeMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _wireframeMat.SetInt("_ZWrite", 1);
            }
        }
    }
}
#endif
