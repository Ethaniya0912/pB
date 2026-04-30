using UnityEngine;

namespace CaveSystem
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-G F3] BiomeShaderSync — CPU/GPU Biome Sampling 통합
    //
    // 목적: CPU(PassageMetric / GraphBuilder)와 GPU(CaveDensityGenerator.compute)가
    //       같은 worldPos에서 같은 biome 결과를 반환하도록 보장.
    //
    // 배경 (CPU/GPU mismatch 분석 보고서):
    //   기존 CPU: Mathf.PerlinNoise + 1000m offset (Unity 내장)
    //   기존 GPU: snoise2D (Simplex, offset 없음)
    //   → 같은 위치에서 다른 biome 반환 → spawn quota / D4 / Risk / UI 모두 거짓
    //
    // 해결:
    //   기존 검증된 ShaderNoiseCompat.snoise2D 를 runtime용으로 wrap.
    //   biome 분류 식 (worldPos.xz/macroScale → snoise → ×0.5+0.5 → ×(N-1) → floor)
    //   GPU CaveDensityGenerator.compute L632~639 와 정확 동일.
    //
    // 4원칙 영향:
    //   - P1/P2/P3/P4: 0 (이 함수는 biome 분류만, SDF 변경 없음)
    //
    // 규칙 23 영향:
    //   - 0 (stateless static, lifecycle 무관)
    //
    // VRAM:
    //   - 추가 0 bytes (CPU-only, GPU buffer 미증가)
    //
    // Streaming 결정성:
    //   - snoise hash 기반 deterministic → 같은 worldPos = 같은 biome
    //   - chunk 재로드 시에도 동일 결과 보장
    //
    // 토글 가드 (D2):
    //   각 호출 측 (PassageMetric, GraphBuilder)에 useShaderCompat flag 보유.
    //   기본 OFF → byte-identical (legacy Mathf.PerlinNoise 경로).
    //   ON → 이 클래스 호출.
    // ══════════════════════════════════════════════════════════════════════

    public static class BiomeShaderSync
    {
        // ─────────────────────────────────────────────────────────────────
        // GPU snoise2D 정확 포팅 — byte-identical to CaveNoiseLibrary.hlsl
        //
        //   기존 Editor-only ShaderNoiseCompat 의 검증된 함수를 runtime용 복제.
        //   원본 변경 0 (Editor preview 호환성 유지).
        //   상수 / 연산 순서 모두 동일.
        // ─────────────────────────────────────────────────────────────────
        public static float Snoise2D(float vx, float vy)
        {
            // float2 i = floor(v + (v.x + v.y) * 0.36602540378);
            float skew = (vx + vy) * 0.36602540378f;
            float ix = Mathf.Floor(vx + skew);
            float iy = Mathf.Floor(vy + skew);

            // float2 x0 = v - i + (i.x + i.y) * 0.2113248654;
            float unskew = (ix + iy) * 0.2113248654f;
            float x0x = vx - ix + unskew;
            float x0y = vy - iy + unskew;

            // float2 i1 = (x0.x > x0.y) ? (1, 0) : (0, 1);
            float i1x, i1y;
            if (x0x > x0y) { i1x = 1f; i1y = 0f; }
            else           { i1x = 0f; i1y = 1f; }

            // float2 x1 = x0 - i1 + 0.2113248654;
            float x1x = x0x - i1x + 0.2113248654f;
            float x1y = x0y - i1y + 0.2113248654f;

            // float2 x2 = x0 - 1 + 2 * 0.2113248654;
            float x2x = x0x - 1f + 2f * 0.2113248654f;
            float x2y = x0y - 1f + 2f * 0.2113248654f;

            // i = fmod(i, 289.0) — HLSL fmod (trunc toward zero)
            ix = HlslFMod(ix, 289f);
            iy = HlslFMod(iy, 289f);

            // float3 p = (dot(i, (127.1, 311.7)), dot(i+i1, ...), dot(i+(1,1), ...))
            float p0 = ix * 127.1f + iy * 311.7f;
            float p1 = (ix + i1x) * 127.1f + (iy + i1y) * 311.7f;
            float p2 = (ix + 1f) * 127.1f + (iy + 1f) * 311.7f;

            // p = frac(sin(p) * 43758.5453123)
            p0 = HlslFrac(Mathf.Sin(p0) * 43758.5453123f);
            p1 = HlslFrac(Mathf.Sin(p1) * 43758.5453123f);
            p2 = HlslFrac(Mathf.Sin(p2) * 43758.5453123f);

            // float3 m = max(0.5 - (dot(x0,x0), dot(x1,x1), dot(x2,x2)), 0)
            float m0 = Mathf.Max(0.5f - (x0x * x0x + x0y * x0y), 0f);
            float m1 = Mathf.Max(0.5f - (x1x * x1x + x1y * x1y), 0f);
            float m2 = Mathf.Max(0.5f - (x2x * x2x + x2y * x2y), 0f);

            // m^4
            m0 *= m0; m0 *= m0;
            m1 *= m1; m1 *= m1;
            m2 *= m2; m2 *= m2;

            // float3 x = 2 * frac(p * 43758.5453123) - 1
            // float3 y = 2 * frac(p * 12345.6789012) - 1
            float xg0 = 2f * HlslFrac(p0 * 43758.5453123f) - 1f;
            float xg1 = 2f * HlslFrac(p1 * 43758.5453123f) - 1f;
            float xg2 = 2f * HlslFrac(p2 * 43758.5453123f) - 1f;
            float yg0 = 2f * HlslFrac(p0 * 12345.6789012f) - 1f;
            float yg1 = 2f * HlslFrac(p1 * 12345.6789012f) - 1f;
            float yg2 = 2f * HlslFrac(p2 * 12345.6789012f) - 1f;

            // d = (x.x*x0.x + y.x*x0.y, x.y*x1.x + y.y*x1.y, x.z*x2.x + y.z*x2.y)
            float d0 = xg0 * x0x + yg0 * x0y;
            float d1 = xg1 * x1x + yg1 * x1y;
            float d2 = xg2 * x2x + yg2 * x2y;

            // return 130 * dot(m, d)
            return 130f * (m0 * d0 + m1 * d1 + m2 * d2);
        }

        // HLSL fmod — trunc toward zero (C# % 와 동등)
        private static float HlslFMod(float a, float b)
        {
            return a - b * (float)System.Math.Truncate(a / b);
        }

        // HLSL frac — x - floor(x), 음수 입력 시 양수 결과
        private static float HlslFrac(float x)
        {
            return x - Mathf.Floor(x);
        }

        // ─────────────────────────────────────────────────────────────────
        // GPU CaveDensityGenerator.compute L632~639 정확 동일
        //
        //   float macroNoise = snoise2D(worldPos.xz / max(_MacroBiomeScale, 1.0));
        //   float normalizedNoise = macroNoise * 0.5 + 0.5;
        //   float scaledMap = normalizedNoise * max((_BiomeCount - 1), 0.0);
        //   int bIdxA = clamp((int) floor(scaledMap), 0, _BiomeCount - 1);
        //   float blendWeight = smoothstep(0.0, 1.0, frac(scaledMap));
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// GPU shader와 정확 일치하는 biome 인덱스 sampling.
        /// 기존 Mathf.PerlinNoise 기반 함수의 대체.
        /// 결정성: 같은 worldPos는 항상 같은 biome 반환 (streaming 안전).
        /// </summary>
        public static int SampleBiomeIdx(Vector3 pos, CaveBiomeSettings bs)
        {
            if (bs == null || bs.globalBiomes == null || bs.globalBiomes.Count == 0) return 0;

            float macroScale = Mathf.Max(1f, bs.macroBiomeScale);
            int biomeCount = bs.globalBiomes.Count;

            // GPU 동일: snoise2D, offset 없음
            float macroNoise = Snoise2D(pos.x / macroScale, pos.z / macroScale);
            float normalizedNoise = macroNoise * 0.5f + 0.5f;
            float scaledMap = normalizedNoise * Mathf.Max(biomeCount - 1, 0f);

            return Mathf.Clamp(Mathf.FloorToInt(scaledMap), 0, biomeCount - 1);
        }

        /// <summary>
        /// GPU shader effectiveBlendWeight 정확 일치.
        /// 결과: smoothstep(0, 1, frac(scaledMap))
        /// 0 = 우세 biome 중심, 0.5 = 두 biome 사이, 1 = 다음 biome 중심
        /// </summary>
        public static float SampleBlendWeight(Vector3 pos, CaveBiomeSettings bs)
        {
            if (bs == null || bs.globalBiomes == null || bs.globalBiomes.Count <= 1) return 0f;

            float macroScale = Mathf.Max(1f, bs.macroBiomeScale);
            int biomeCount = bs.globalBiomes.Count;

            float macroNoise = Snoise2D(pos.x / macroScale, pos.z / macroScale);
            float normalizedNoise = macroNoise * 0.5f + 0.5f;
            float scaledMap = normalizedNoise * Mathf.Max(biomeCount - 1, 0f);
            float frac = scaledMap - Mathf.Floor(scaledMap);

            // smoothstep(0, 1, frac) — GPU 동일
            return frac * frac * (3f - 2f * frac);
        }

        // ─────────────────────────────────────────────────────────────────
        // 디버그 / sanity check
        // ─────────────────────────────────────────────────────────────────

        /// <summary>(0, 0) 입력 결과 — GPU와 일치 검증용.</summary>
        public static float SanityCheckOrigin() => Snoise2D(0f, 0f);
    }
}
