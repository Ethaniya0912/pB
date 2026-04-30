#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-E E.3] PreviewSDF — 메인 SDF 계산
    //
    // 입력: world position p
    // 출력: SDF value (음수 = cave 내부, 양수 = solid rock)
    //
    // 포함 기능 (v1):
    //   ✓ Node sphere SDF
    //   ✓ Edge capsule SDF (+ curvature 토글)
    //   ✓ Smin blending
    //   ✓ Floor/ceil plane carving
    //   ✓ FloorVar/CeilVar 노이즈
    //   ✓ 2-biome blending (A/B blend weight)
    //
    // 미포함 (v2 이월):
    //   ✗ Stalactite
    //   ✗ SedimentLayer
    //   ✗ Sinkhole 수직 shaft
    //   ✗ Primitive routing (Phase 4 E-α)
    //
    // byte-identical 보장: 이 파일은 MC Preview 전용, 호출되기 전까지 기존 영향 0.
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>SDF 평가에 필요한 context — graph + biome 설정.</summary>
    public class PreviewSDFContext
    {
        public List<NodeData> nodes;
        public List<EdgeData> edges;
        public List<CaveBiomeData> biomes;

        // Smin strength — shader sminStrength와 동일 개념
        public float sminStrength = 2.0f;

        // Floor/ceil plane
        public float floorAltitude = -5f;
        public float ceilAltitude = 15f;
        public float floorVarAmp = 2f;
        public float ceilVarAmp = 2f;
        public float floorNoiseScale = 0.05f;  // 낮을수록 완만한 변화

        // Biome blending
        public float macroBiomeScale = 500f;
        public bool useSimplexNoise = false;  // Option 1/2 전환
        public bool balanceBiomes = false;

        // Feature toggles (v1 간소화)
        public bool enableCurvature = true;
        public bool enableFloorVar = true;
        public bool enableBiomeBlend = true;

        // 성능/범위 — 이 반경 밖 nodes/edges는 SDF 기여 skip
        public Vector3 center = Vector3.zero;
        public float maxRange = 500f;

        // ───── 내부 cache (생성 시 한 번 초기화) ─────
        private List<NodeData> _nearbyNodes;
        private List<EdgeData> _nearbyEdges;

        /// <summary>center/maxRange 기준으로 근접 노드/엣지만 필터링 (성능).</summary>
        public void PrepareNearbyCache()
        {
            _nearbyNodes = new List<NodeData>();
            _nearbyEdges = new List<EdgeData>();
            float rangeSq = maxRange * maxRange;

            if (nodes != null)
            {
                foreach (var n in nodes)
                {
                    if ((n.position - center).sqrMagnitude <= rangeSq + n.radius * n.radius)
                        _nearbyNodes.Add(n);
                }
            }

            if (edges != null)
            {
                foreach (var e in edges)
                {
                    // edge 가 range 안에 닿는지 (단순화: midpoint 기준)
                    Vector3 mid = (e.startPos + e.endPos) * 0.5f;
                    float halfLen = Vector3.Distance(e.startPos, e.endPos) * 0.5f;
                    float effRange = maxRange + halfLen + e.width;
                    if ((mid - center).sqrMagnitude <= effRange * effRange)
                        _nearbyEdges.Add(e);
                }
            }
        }

        public List<NodeData> GetNearbyNodes() => _nearbyNodes ?? nodes;
        public List<EdgeData> GetNearbyEdges() => _nearbyEdges ?? edges;
    }

    public static class PreviewSDF
    {
        /// <summary>
        /// 특정 월드 위치의 SDF 값 반환.
        /// cave 내부: 음수, 벽: 0, solid rock: 양수.
        /// </summary>
        public static float Evaluate(Vector3 p, PreviewSDFContext ctx)
        {
            if (ctx == null) return float.MaxValue;

            // ───── 1. Base geometry (nodes + edges) ─────
            float baseSDF = float.MaxValue;

            var nearNodes = ctx.GetNearbyNodes();
            var nearEdges = ctx.GetNearbyEdges();

            // Node spheres
            if (nearNodes != null)
            {
                foreach (var n in nearNodes)
                {
                    float nSDF = PreviewSDFHelpers.sdSphere(p, n.position, n.radius);
                    baseSDF = (baseSDF == float.MaxValue)
                        ? nSDF
                        : PreviewSDFHelpers.smin(baseSDF, nSDF, ctx.sminStrength);
                }
            }

            // Edge capsules (curvature 선택적)
            if (nearEdges != null)
            {
                foreach (var e in nearEdges)
                {
                    float radius = e.width * 0.5f;
                    float eSDF;
                    if (ctx.enableCurvature && e.curvatureAmp > 0.01f)
                    {
                        eSDF = PreviewSDFHelpers.sdCurvedCapsule(
                            p, e.startPos, e.endPos, radius, e.curvatureAmp);
                    }
                    else
                    {
                        eSDF = PreviewSDFHelpers.sdCapsule(p, e.startPos, e.endPos, radius);
                    }
                    baseSDF = (baseSDF == float.MaxValue)
                        ? eSDF
                        : PreviewSDFHelpers.smin(baseSDF, eSDF, ctx.sminStrength);
                }
            }

            if (baseSDF == float.MaxValue) baseSDF = 100f;  // 빈 graph 방어

            // ───── 2. Biome blend — 2 biomes의 파라미터 선형 보간 ─────
            //   현재 간소화: floor/ceil Var만 biome별 다름
            //   v2: sediment 등 추가 가능
            float floorVarEff = ctx.floorVarAmp;
            float ceilVarEff = ctx.ceilVarAmp;

            if (ctx.enableBiomeBlend && ctx.biomes != null && ctx.biomes.Count > 0)
            {
                // Set simplex option temporarily (BiomeSampler)
                bool origSimplex = BiomeSampler.UseSimplexNoise;
                BiomeSampler.UseSimplexNoise = ctx.useSimplexNoise;
                try
                {
                    var sample = BiomeSampler.SampleAt(p, ctx.biomes.Count,
                        ctx.macroBiomeScale, null, null, ctx.balanceBiomes);

                    // biome A/B 파라미터 가져오기
                    var bA = (sample.biomeIdxA >= 0 && sample.biomeIdxA < ctx.biomes.Count)
                        ? ctx.biomes[sample.biomeIdxA] : null;
                    var bB = (sample.biomeIdxB >= 0 && sample.biomeIdxB < ctx.biomes.Count)
                        ? ctx.biomes[sample.biomeIdxB] : null;

                    if (bA != null && bB != null)
                    {
                        // floorVarAmp가 CaveBiomeData에 있다면 활용 — 기본 fallback은 ctx 값
                        float fA = GetFloorVarAmpSafe(bA, ctx.floorVarAmp);
                        float fB = GetFloorVarAmpSafe(bB, ctx.floorVarAmp);
                        float cA = GetCeilVarAmpSafe(bA, ctx.ceilVarAmp);
                        float cB = GetCeilVarAmpSafe(bB, ctx.ceilVarAmp);

                        floorVarEff = Mathf.Lerp(fA, fB, sample.blendWeight);
                        ceilVarEff = Mathf.Lerp(cA, cB, sample.blendWeight);
                    }
                }
                finally
                {
                    BiomeSampler.UseSimplexNoise = origSimplex;
                }
            }

            // ───── 3. Floor/Ceil carving ─────
            //   floorSDF: p.y < floorY 이면 floor 아래 (solid) → SDF > 0
            //   ceilSDF: p.y > ceilY 이면 ceil 위 (solid) → SDF > 0
            //   cave는 floor와 ceil 사이에만 존재 → max(baseSDF, max(floorSDF, ceilSDF))
            float floorY = ctx.floorAltitude;
            float ceilY = ctx.ceilAltitude;

            if (ctx.enableFloorVar && (floorVarEff > 0.01f || ceilVarEff > 0.01f))
            {
                bool origSimplex = BiomeSampler.UseSimplexNoise;
                BiomeSampler.UseSimplexNoise = ctx.useSimplexNoise;
                try
                {
                    float noiseXZ = BiomeSampler.SampleNoise2D(
                        p.x * ctx.floorNoiseScale, p.z * ctx.floorNoiseScale);
                    floorY += noiseXZ * floorVarEff;
                    ceilY += noiseXZ * ceilVarEff;
                }
                finally
                {
                    BiomeSampler.UseSimplexNoise = origSimplex;
                }
            }

            float floorSDF = floorY - p.y;   // p가 floor 아래 → 양수
            float ceilSDF = p.y - ceilY;     // p가 ceil 위 → 양수

            // cave 내부 = baseSDF < 0 AND p가 floor/ceil 사이 (floorSDF < 0 AND ceilSDF < 0)
            // max(baseSDF, floorSDF, ceilSDF) < 0 일 때만 cave
            return Mathf.Max(baseSDF, Mathf.Max(floorSDF, ceilSDF));
        }

        // ───── Helper: CaveBiomeData의 floor/ceilVarAmp 안전 접근 ─────
        //   현재 CaveBiomeData에 해당 필드가 있는지 reflection으로 확인
        //   없으면 context 기본값 반환 (byte-identical)
        private static float GetFloorVarAmpSafe(CaveBiomeData biome, float fallback)
        {
            if (biome == null) return fallback;
            var field = typeof(CaveBiomeData).GetField("floorVarAmp");
            if (field != null)
            {
                var val = field.GetValue(biome);
                if (val is float f) return f;
            }
            return fallback;
        }

        private static float GetCeilVarAmpSafe(CaveBiomeData biome, float fallback)
        {
            if (biome == null) return fallback;
            var field = typeof(CaveBiomeData).GetField("ceilVarAmp");
            if (field != null)
            {
                var val = field.GetValue(biome);
                if (val is float f) return f;
            }
            return fallback;
        }
    }
}
#endif
