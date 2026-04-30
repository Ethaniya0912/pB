#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Gate 5 Phase 4.5-A] PreviewStatsCalculator
    //
    // 생성된 PreviewData에 대해 통계 집계:
    //   - Graph 통계 (nodes/edges count, roomType 분포, min/max 측정)
    //   - Matrix 적용 통계
    //   - EdgeConstraint violation 집계
    //   - CardinalBias 평균 push 거리
    //   - Biome area ratio (전체 XZ 영역에서 각 biome 비율)
    //   - Playability 7 불변 조건 간단 체크 (제안 5)
    // ══════════════════════════════════════════════════════════════════════

    public static class PreviewStatsCalculator
    {
        public static void Calculate(
            PreviewData data, PreviewInputs inputs, CaveBiomeSettings settings, bool useSOs)
        {
            if (data == null || data.stats == null) return;
            var stats = data.stats;

            // ──── Graph 기본 ────
            stats.nodeCount = data.nodes.Count;
            stats.edgeCount = data.edges.Count;
            stats.minEdgeWidth = float.MaxValue;
            stats.maxEdgeSlope = 0f;

            foreach (var n in data.nodes)
            {
                switch (n.roomType)
                {
                    case 0: stats.normalCount++; break;
                    case 1: stats.spawnCount++; break;
                    case 2: stats.bossCount++; break;
                    case 3: stats.treasureCount++; break;
                    case 4: stats.sinkholeCount++; break;
                }
            }

            // ──── Edge 분석 ────
            EdgeConstraintSO constraint = null;
            float biomeMaxSlope = 45f;
            float biomeCurvAmp = 0f;
            if (settings != null && settings.globalBiomes != null
                && settings.globalBiomes.Count > 0 && settings.globalBiomes[0] != null)
            {
                constraint = settings.globalBiomes[0].edgeConstraint;
                biomeMaxSlope = settings.globalBiomes[0].maxSlope;
                biomeCurvAmp = settings.globalBiomes[0].curvatureAmp;
            }

            data.violationEdges.Clear();
            for (int i = 0; i < data.edges.Count; i++)
            {
                var e = data.edges[i];
                if (e.width < stats.minEdgeWidth) stats.minEdgeWidth = e.width;

                Vector3 edgeVec = e.endPos - e.startPos;
                float dy = Mathf.Abs(edgeVec.y);
                float dxz = Mathf.Sqrt(edgeVec.x * edgeVec.x + edgeVec.z * edgeVec.z);
                float slopeDeg = (dxz > 0.01f) ? Mathf.Atan2(dy, dxz) * Mathf.Rad2Deg : 89f;
                float edgeLen = edgeVec.magnitude;

                if (slopeDeg > stats.maxEdgeSlope) stats.maxEdgeSlope = slopeDeg;

                // Violation 체크 (constraint 있을 때만)
                bool isViolation = false;
                if (constraint != null)
                {
                    if (!constraint.IsLengthValid(edgeLen))
                    {
                        stats.violationLength++;
                        isViolation = true;
                    }
                    if (!constraint.IsSlopeValid(slopeDeg, biomeMaxSlope))
                    {
                        stats.violationSlope++;
                        isViolation = true;
                    }
                    // Direction score (제안 2)
                    if (constraint.directionStrength > 0.001f
                        && edgeVec.sqrMagnitude > 0.001f
                        && constraint.preferredDirection.sqrMagnitude > 0.001f)
                    {
                        float score = constraint.EvaluateDirectionScore(edgeVec);
                        if (score < 0.3f)  // threshold
                        {
                            stats.violationDirection++;
                            isViolation = true;
                        }
                    }
                }
                if (isViolation) data.violationEdges.Add(i);
            }
            if (stats.minEdgeWidth == float.MaxValue) stats.minEdgeWidth = 0f;

            // ──── Matrix 적용 통계 ────
            data.matrixApplied = new bool[data.nodes.Count];
            if (settings != null && settings.roomGeometryMatrix != null)
            {
                int biome0Type = 0;
                if (settings.globalBiomes != null && settings.globalBiomes.Count > 0
                    && settings.globalBiomes[0] != null)
                    biome0Type = settings.globalBiomes[0].noiseType;

                for (int i = 0; i < data.nodes.Count; i++)
                {
                    var entryOpt = settings.roomGeometryMatrix.Lookup(data.nodes[i].roomType, biome0Type);
                    if (entryOpt.HasValue)
                    {
                        data.matrixApplied[i] = true;
                        stats.matrixAppliedNodeCount++;
                        string key = $"rt{data.nodes[i].roomType}_b{biome0Type}";
                        if (!stats.matrixEntryHits.ContainsKey(key))
                            stats.matrixEntryHits[key] = 0;
                        stats.matrixEntryHits[key]++;
                    }
                }
            }

            // ──── Cardinal Bias 영향 (계산만) ────
            if (inputs.cardinalBias != null && useSOs && inputs.cardinalBiasEnabled)
            {
                ComputeCardinalBiasStats(data, inputs, stats);
            }

            // ──── Biome area ratio ────
            if (data.biomeSamples != null && data.biomeSamples.Count > 0
                && inputs.biomes != null && inputs.biomes.Count > 0)
            {
                // idxA 기준으로 단순 카운트 (A 쪽이 우세한 biome으로 간주)
                Dictionary<int, int> counts = new Dictionary<int, int>();
                foreach (var s in data.biomeSamples)
                {
                    // idxA가 우세한지 idxB가 우세한지 blend로 판정
                    int dominantIdx = (s.blendWeight < 0.5f) ? s.biomeIdxA : s.biomeIdxB;
                    if (dominantIdx < 0 || dominantIdx >= inputs.biomes.Count) continue;
                    var biome = inputs.biomes[dominantIdx];
                    if (biome == null) continue;
                    int noiseType = biome.noiseType;
                    if (!counts.ContainsKey(noiseType)) counts[noiseType] = 0;
                    counts[noiseType]++;
                }
                int total = data.biomeSamples.Count;
                foreach (var kv in counts)
                {
                    stats.biomeAreaRatio[kv.Key] = (float)kv.Value / Mathf.Max(total, 1);
                }
            }

            // ──── Playability 7 조건 (제안 5) ────
            ComputePlayabilitySummary(data, inputs, stats, biomeCurvAmp);
        }

        private static void ComputeCardinalBiasStats(
            PreviewData data, PreviewInputs inputs, PreviewStats stats)
        {
            if (inputs.cardinalBias == null || inputs.cardinalBias.biasVectors == null) return;
            var biasVecs = inputs.cardinalBias.biasVectors;

            for (int i = 0; i < biasVecs.Length && i < inputs.biomes.Count; i++)
            {
                Vector3 bv = biasVecs[i];
                if (bv.sqrMagnitude < 0.001f) continue;
                // 각 node가 이 biome 방향으로 얼마나 "쏠렸는지" 계산
                //   현재 소비 안 하므로 단순 estimation
                float meanPush = bv.magnitude * inputs.cardinalBias.biasStrength * 20f;
                stats.cardinalBiasMeanPush[i] = meanPush;
            }
        }

        private static void ComputePlayabilitySummary(
            PreviewData data, PreviewInputs inputs, PreviewStats stats, float biomeCurvAmp)
        {
            var p = stats.playability;

            // #1 통로 폭
            if (stats.minEdgeWidth < 2.0f)
            {
                p.passWidth = false;
                p.warnings.Add($"#1 최소 edge width {stats.minEdgeWidth:F1}m < 2.0m");
            }

            // #2 경사
            if (stats.maxEdgeSlope > 45f)
            {
                p.passSlope = false;
                p.warnings.Add($"#2 최대 slope {stats.maxEdgeSlope:F0}° > 45° (NavMesh bake 위험)");
            }

            // #4 curvature clamp
            int clampedCount = 0;
            foreach (var e in data.edges)
            {
                if (biomeCurvAmp > e.width * 0.5f) clampedCount++;
            }
            // clamp는 자동 적용 — 경고 아니고 정보
            if (clampedCount > 0)
                p.warnings.Add($"#4 {clampedCount}개 edge가 curvature clamp 발동 (safeAmp 자동 보호)");

            // #6 방향감 유지 — EvaluateCurvedTunnel 보장 (항상 pass)

            // #3, #5 — runtime/NavMesh 필요, preview 불가
        }
    }
}
#endif
