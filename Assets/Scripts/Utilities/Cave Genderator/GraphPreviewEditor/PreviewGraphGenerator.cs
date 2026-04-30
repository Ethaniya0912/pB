#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Gate 5 Phase 4.5-A] PreviewGraphGenerator
    //
    // 역할:
    //   1. 임시 NodeGraphBuilder 인스턴스 생성 (HideFlags.HideAndDontSave)
    //   2. PreviewInputs 기반 임시 CaveBiomeSettings 구축
    //   3. builder.GenerateGraph(seed, overrideSettings) 호출
    //   4. 결과 수집 (nodes/edges/biomeSamples)
    //   5. Statistics 집계
    //   6. 임시 GameObject 즉시 파괴 (try-finally 보장)
    //
    // 안전 장치:
    //   - Application.isPlaying 체크 (Play 중 preview 금지)
    //   - try-finally로 임시 GameObject 반드시 파괴
    //   - Singleton CaveManager.Instance 건드리지 않음 (override param 사용)
    //   - Undo.RecordObject 호출 안 함 (undo 오염 방지)
    // ══════════════════════════════════════════════════════════════════════

    public static class PreviewGraphGenerator
    {
        private const string TEMP_GO_NAME = "__CavePreviewBuilder__";

        /// <summary>
        /// 메인 진입점. PreviewInputs 기반으로 graph 생성 + biome 샘플링.
        /// </summary>
        /// <param name="inputs">사용자 입력</param>
        /// <param name="extensions">확장 목록 (Phase 5+)</param>
        /// <param name="useSOs">Before/After용 — false면 matrix/constraint/bias 무시</param>
        public static PreviewData Generate(
            PreviewInputs inputs,
            List<IPreviewExtension> extensions = null,
            bool useSOs = true)
        {
            if (inputs == null)
            {
                Debug.LogError("[PreviewGraphGenerator] inputs is null");
                return null;
            }
            if (inputs.biomes == null || inputs.biomes.Count == 0)
            {
                Debug.LogWarning("[PreviewGraphGenerator] No biomes configured.");
                return null;
            }

            // Play 중 실행 방지
            if (Application.isPlaying)
            {
                Debug.LogWarning("[PreviewGraphGenerator] Play 중에는 preview 생성 안 함. Stop 후 재시도.");
                return null;
            }

            var data = new PreviewData();
            GameObject tempGO = null;

            try
            {
                // 1. 임시 GameObject + NodeGraphBuilder 생성
                tempGO = new GameObject(TEMP_GO_NAME);
                tempGO.hideFlags = HideFlags.HideAndDontSave;
                var builder = tempGO.AddComponent<CaveNodeGraphBuilder>();

                // 2. Builder 기본 필드 설정 (Inspector 값 주입)
                builder.targetRoomCount = inputs.targetRoomCount;
                builder.dungeonBounds = inputs.dungeonBounds;
                builder.mainPathWidth = inputs.mainPathWidth;
                builder.sidePathWidth = inputs.sidePathWidth;
                builder.minRoomRadius = inputs.minRoomRadius;
                builder.maxRoomRadius = inputs.maxRoomRadius;
                builder.bossRoomRadiusMultiplier = inputs.bossRoomRadiusMultiplier;
                builder.loopProbability = inputs.loopProbability;

                // 3. 임시 CaveBiomeSettings 생성 (useSOs 반영)
                var tempSettings = BuildTempBiomeSettings(inputs, useSOs);

                // 4. Graph 생성 — override 파라미터로 tempSettings 주입
                builder.GenerateGraph(inputs.seed, tempSettings);

                // 5. 결과 복사
                data.nodes = new List<NodeData>(builder.nodesData);
                data.edges = new List<EdgeData>(builder.edgesData);

                // 6. Biome 샘플링 (XZ grid) — [v4] actualGridSize 반환 받음
                float actualGridSize;
                data.biomeSamples = BiomeSampler.SampleGrid(inputs, out actualGridSize, extensions);
                data.actualGridSize = actualGridSize;

                // 7. [Phase 4.5-C] Critical path — node index 기반 HashSet 가져옴
                //   edgesData는 position 기반이라 (startPos, endPos) → nodeIdx 매핑 필요
                data.criticalPathEdges = builder.GetCriticalPathEdges();

                // Critical path edge index 매핑 — edgesData idx 기준으로 변환
                //   PreviewSceneRenderer가 edge rendering 중 data.criticalEdgeIndices 참조
                data.criticalEdgeIndices = new HashSet<int>();
                if (data.criticalPathEdges != null && data.criticalPathEdges.Count > 0)
                {
                    // 각 EdgeData의 start/end position이 어느 node에 해당하는지 매핑
                    //   node position = builder.nodesData[i].position
                    for (int e = 0; e < data.edges.Count; e++)
                    {
                        int idxA = FindNodeIndex(data.nodes, data.edges[e].startPos);
                        int idxB = FindNodeIndex(data.nodes, data.edges[e].endPos);
                        if (idxA < 0 || idxB < 0) continue;
                        Vector2Int key = new Vector2Int(Mathf.Min(idxA, idxB), Mathf.Max(idxA, idxB));
                        if (data.criticalPathEdges.Contains(key))
                            data.criticalEdgeIndices.Add(e);
                    }
                }

                // 8. Statistics 집계
                PreviewStatsCalculator.Calculate(data, inputs, tempSettings, useSOs);

                // 9. 임시 settings cleanup (SO 사본이므로 destroy 필요)
                DestroyTempSettings(tempSettings);

                data.generationTime = System.DateTime.Now;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PreviewGraphGenerator] 생성 실패: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                // 임시 GameObject 즉시 파괴 (중요: scene 오염 방지)
                if (tempGO != null)
                {
                    Object.DestroyImmediate(tempGO);
                }

                // Stray preview builders 있으면 정리
                CleanupStrayBuilders();
            }

            return data;
        }

        /// <summary>
        /// Scene에 혹시 남아있는 __CavePreviewBuilder__ 제거 (안전망).
        /// </summary>
        public static void CleanupStrayBuilders()
        {
            var strays = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            int removed = 0;
            foreach (var go in strays)
            {
                if (go != null && go.name == TEMP_GO_NAME)
                {
                    Object.DestroyImmediate(go);
                    removed++;
                }
            }
            if (removed > 0)
                Debug.Log($"[PreviewGraphGenerator] {removed}개의 stray preview builder 정리됨.");
        }

        /// <summary>
        /// [Phase 4.5-C] Position → node index 탐색 (edge가 가진 position으로 node 찾기).
        /// distance threshold 0.1m — floating point 안정성.
        /// </summary>
        private static int FindNodeIndex(List<NodeData> nodes, Vector3 pos)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if ((nodes[i].position - pos).sqrMagnitude < 0.01f)
                    return i;
            }
            return -1;
        }

        // ══════════════════════════════════════════════════════════════════
        // [Phase 4.5-D 제안 6] Seed Sensitivity Sweep
        //   여러 seed로 연속 Generate → 통계 평균/최악 집계
        //   결과를 UI에서 "이 config가 seed에 얼마나 민감한지" 판단 가능
        //   비용 가드: 기본 10 seeds, 세션당 최대 50
        // ══════════════════════════════════════════════════════════════════

        public const int DEFAULT_SWEEP_COUNT = 10;
        public const int MAX_SWEEP_COUNT = 50;

        /// <summary>
        /// 여러 seed로 연속 생성하여 평균/최악 통계 반환.
        /// base seed부터 시작해 seed+1, seed+2, ... 사용.
        /// </summary>
        public static SeedSweepResult Sweep(
            PreviewInputs inputs, int sweepCount = DEFAULT_SWEEP_COUNT,
            List<IPreviewExtension> extensions = null)
        {
            if (inputs == null || inputs.biomes == null || inputs.biomes.Count == 0)
            {
                Debug.LogWarning("[Sweep] inputs/biomes invalid.");
                return null;
            }
            sweepCount = Mathf.Clamp(sweepCount, 2, MAX_SWEEP_COUNT);

            var result = new SeedSweepResult
            {
                seeds = new int[sweepCount],
                statsPerSeed = new PreviewStats[sweepCount],
                meanStats = new PreviewStats(),
                worstStats = new PreviewStats(),
            };

            int baseSeed = inputs.seed;
            int origSeed = inputs.seed;

            try
            {
                for (int i = 0; i < sweepCount; i++)
                {
                    int thisSeed = baseSeed + i;
                    inputs.seed = thisSeed;
                    result.seeds[i] = thisSeed;

                    var d = Generate(inputs, extensions, useSOs: true);
                    result.statsPerSeed[i] = d != null ? d.stats : new PreviewStats();

                    // Progress bar (취소 가능)
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "Seed Sensitivity Sweep",
                        $"Seed {thisSeed} ({i + 1}/{sweepCount})",
                        (i + 1f) / sweepCount))
                    {
                        Debug.LogWarning("[Sweep] 사용자 취소.");
                        break;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                inputs.seed = origSeed;  // 원래 seed 복원
            }

            // 통계 집계 — 평균
            AggregateSweepStats(result);

            return result;
        }

        private static void AggregateSweepStats(SeedSweepResult r)
        {
            if (r == null || r.statsPerSeed == null || r.statsPerSeed.Length == 0) return;

            float sumNodeCount = 0, sumEdgeCount = 0, sumBossCount = 0;
            float sumMinW = 0, sumMaxSlope = 0, sumViolations = 0;
            int validCount = 0;

            // 최악 값 초기화
            float minMinW = float.MaxValue;
            float maxMaxSlope = 0;
            int maxViolTotal = 0;

            foreach (var s in r.statsPerSeed)
            {
                if (s == null) continue;
                validCount++;
                sumNodeCount += s.nodeCount;
                sumEdgeCount += s.edgeCount;
                sumBossCount += s.bossCount;
                sumMinW += s.minEdgeWidth;
                sumMaxSlope += s.maxEdgeSlope;
                int viol = s.violationLength + s.violationSlope + s.violationDirection;
                sumViolations += viol;

                if (s.minEdgeWidth < minMinW) minMinW = s.minEdgeWidth;
                if (s.maxEdgeSlope > maxMaxSlope) maxMaxSlope = s.maxEdgeSlope;
                if (viol > maxViolTotal) maxViolTotal = viol;
            }

            if (validCount == 0) return;
            float inv = 1f / validCount;
            r.meanStats.nodeCount = Mathf.RoundToInt(sumNodeCount * inv);
            r.meanStats.edgeCount = Mathf.RoundToInt(sumEdgeCount * inv);
            r.meanStats.bossCount = Mathf.RoundToInt(sumBossCount * inv);
            r.meanStats.minEdgeWidth = sumMinW * inv;
            r.meanStats.maxEdgeSlope = sumMaxSlope * inv;
            r.meanStats.violationLength = Mathf.RoundToInt((sumViolations * inv) / 3f);  // 단순 분배

            // Worst = 가장 안 좋은 값들의 조합
            r.worstStats.minEdgeWidth = minMinW < float.MaxValue ? minMinW : 0f;
            r.worstStats.maxEdgeSlope = maxMaxSlope;
            r.worstStats.violationLength = maxViolTotal;
        }

        /// <summary>
        /// PreviewInputs 기반 임시 CaveBiomeSettings 생성.
        /// useSOs=false면 matrix/constraint/bias 무시 (baseline 용).
        /// </summary>
        private static CaveBiomeSettings BuildTempBiomeSettings(PreviewInputs inputs, bool useSOs)
        {
            var settings = ScriptableObject.CreateInstance<CaveBiomeSettings>();
            settings.hideFlags = HideFlags.HideAndDontSave;

            settings.seed = inputs.seed;
            settings.macroBiomeScale = inputs.macroBiomeScale;
            settings.globalBiomes = new List<CaveBiomeData>(inputs.biomes);

            if (useSOs)
            {
                settings.roomGeometryMatrix = inputs.matrixEnabled ? inputs.matrix : null;
                settings.cardinalBias = inputs.cardinalBiasEnabled ? inputs.cardinalBias : null;
                // EdgeConstraint는 CaveBiomeData 내부 필드이므로 사본 CaveBiomeData 필요
                //   단순화: 원본 biome의 edgeConstraint 그대로 사용, enabled 토글만 반영
                //   이 경우 inputs.constraintEnabled=false라도 biome0.edgeConstraint는 유지됨
                //   → 원본 biome을 복제해 edgeConstraint를 null로 만든 사본 사용
                if (!inputs.constraintEnabled)
                {
                    settings.globalBiomes = CloneBiomesWithoutConstraint(inputs.biomes);
                }
            }
            else
            {
                settings.roomGeometryMatrix = null;
                settings.cardinalBias = null;
                settings.globalBiomes = CloneBiomesWithoutConstraint(inputs.biomes);
            }

            return settings;
        }

        /// <summary>
        /// biomes의 edgeConstraint 참조만 null로 만든 사본 리스트 생성.
        /// 원본 asset 수정 없음 (CreateInstance 사용).
        /// </summary>
        private static List<CaveBiomeData> CloneBiomesWithoutConstraint(List<CaveBiomeData> orig)
        {
            var list = new List<CaveBiomeData>(orig.Count);
            foreach (var b in orig)
            {
                if (b == null) { list.Add(null); continue; }
                var copy = Object.Instantiate(b);
                copy.hideFlags = HideFlags.HideAndDontSave;
                copy.edgeConstraint = null;
                list.Add(copy);
            }
            return list;
        }

        private static void DestroyTempSettings(CaveBiomeSettings settings)
        {
            if (settings == null) return;

            // 사본 CaveBiomeData들 destroy
            if (settings.globalBiomes != null)
            {
                foreach (var b in settings.globalBiomes)
                {
                    if (b != null && (b.hideFlags & HideFlags.DontSave) != 0)
                    {
                        // 사본만 destroy (원본 asset은 HideFlags.None이라 건드리지 않음)
                        Object.DestroyImmediate(b);
                    }
                }
            }
            Object.DestroyImmediate(settings);
        }
    }
}
#endif
