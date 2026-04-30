#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Gate 5 Phase 4.5-A] BiomeSampler — Shader biome 선택 로직 C# 재현
    //
    // 목적:
    //   CaveDensityGenerator.compute L464-470의 biome 선택 공식을 C#에서 계산.
    //   Preview Editor가 Play 없이 월드 각 XZ 지점의 biome 분포 시각화 가능.
    //
    // ╔═══════════════════════════════════════════════════════════════════════╗
    // ║ [Biome Sampler 구현 옵션]                                              ║
    // ║                                                                        ║
    // ║ Option 1 (현재 채택): Mathf.PerlinNoise 기반 근사                       ║
    // ║   - 장점: Unity 내장, 즉시 사용, 성능 빠름                              ║
    // ║   - 단점: shader의 snoise2D와 정확히 다름 (패턴은 유사하나 일치 아님)    ║
    // ║   - 용도: "대략 분포" 파악 목적의 Preview에 충분                         ║
    // ║                                                                        ║
    // ║ Option 2 (향후 전환 가능): Simplex Noise C# 포팅                        ║
    // ║   - 장점: shader와 100% 일치하는 결과                                    ║
    // ║   - 단점: 포팅 비용 + 성능 느림                                          ║
    // ║   - 전환 방법: 이 클래스의 SampleNoise2D() 내부만 변경                  ║
    // ║     (호출부 API 유지) — 다른 파일 수정 불필요                            ║
    // ║                                                                        ║
    // ║ 전환 필요 시점 판단 기준:                                                ║
    // ║   (a) Shader와 Preview heatmap이 현저히 다른 패턴으로 디자이너 혼란     ║
    // ║   (b) 정밀한 biome boundary 검증 필요 시                                 ║
    // ║   (c) Phase 5 SubBiomeResolver와 통합 시 일치 요구                      ║
    // ║                                                                        ║
    // ║ 전환 시 수정 위치: 이 파일 SampleNoise2D() 메서드 단 한 군데.            ║
    // ╚═══════════════════════════════════════════════════════════════════════╝
    // ══════════════════════════════════════════════════════════════════════

    public static class BiomeSampler
    {
        // ══════════════════════════════════════════════════════════════════
        // [Phase 4.5-E E.1 → hotfix v5] Noise 모드 3종
        //   Perlin      = Mathf.PerlinNoise 근사 (기본, 시각만 비슷, runtime과 불일치)
        //   Simplex     = 표준 Stefan Gustavson (수학적 정상, runtime과 완전 일치는 아님)
        //   ShaderCompat = ★ Shader의 snoise2D를 C#에 byte-identical 포팅
        //                  (runtime과 100% 일치, Play mode biome 분포 정확 재현)
        // ══════════════════════════════════════════════════════════════════
        public enum NoiseMode { Perlin, Simplex, ShaderCompat }

        public static NoiseMode CurrentNoiseMode = NoiseMode.ShaderCompat;  // ★ 기본 ShaderCompat

        // [deprecated] UseSimplexNoise — 기존 UI 호환용 (읽기전용 연결)
        //   true  → Simplex
        //   false → ShaderCompat (기본)
        public static bool UseSimplexNoise
        {
            get => CurrentNoiseMode == NoiseMode.Simplex;
            set => CurrentNoiseMode = value ? NoiseMode.Simplex : NoiseMode.ShaderCompat;
        }

        /// <summary>
        /// Shader의 snoise2D 근사 — 3가지 모드 중 선택.
        /// 기본: ShaderCompat (runtime과 byte-identical).
        /// </summary>
        public static float SampleNoise2D(float x, float z)
        {
            switch (CurrentNoiseMode)
            {
                case NoiseMode.ShaderCompat:
                    // ★ Shader의 snoise2D와 수학적으로 동일
                    return ShaderNoiseCompat.snoise2D(x, z);

                case NoiseMode.Simplex:
                    // 표준 Stefan Gustavson (shader와 다른 hash → runtime 불일치)
                    return SimplexNoise2D.Sample(x, z);

                case NoiseMode.Perlin:
                default:
                    // Perlin 근사 (가장 부정확, runtime과 완전 다름)
                    float p = Mathf.PerlinNoise(x + 1000f, z + 1000f);
                    return p * 2f - 1f;
            }
        }

        /// <summary>
        /// 특정 월드 위치에서의 biome A/B 인덱스 + blend weight 계산.
        /// Shader의 공식과 동일한 식:
        ///   macroNoise = snoise2D(worldPos.xz / max(_MacroBiomeScale, 1.0))
        ///   normalizedNoise = macroNoise * 0.5 + 0.5
        ///   scaledMap = normalizedNoise * (biomeCount - 1)
        ///   idxA = floor(scaledMap), idxB = idxA + 1
        ///   blend = smoothstep(0, 1, frac(scaledMap))
        /// 
        /// [Q4 hotfix v3] balanceBiomes=true: CDF 역변환으로 균등화 (Preview 전용)
        /// </summary>
        public static BiomeSamplePoint SampleAt(
            Vector3 worldPos, int biomeCount, float macroScale,
            CardinalBiomeBiasSO bias = null,
            List<Vector3> perBiomeBiasVectors = null,
            bool balanceBiomes = false)
        {
            var sample = new BiomeSamplePoint { worldPos = worldPos };

            if (biomeCount <= 0)
            {
                sample.biomeIdxA = 0;
                sample.biomeIdxB = 0;
                sample.blendWeight = 0f;
                return sample;
            }

            // 1. Shader 공식 재현
            float safeMacro = Mathf.Max(macroScale, 1f);
            float macroNoise = SampleNoise2D(worldPos.x / safeMacro, worldPos.z / safeMacro);
            float normalizedNoise = macroNoise * 0.5f + 0.5f;  // [0, 1]

            // [Q4 hotfix v3] Balance 모드 — Perlin 분포 (중앙 bias) → 균등 분포
            //   원리: Perlin은 0.5 중앙에 값 집중 → sign(x)*sqrt(|x|)로 말단 강조
            //   이 변환은 대략 CDF 역함수 근사 (완벽한 역변환 아님, 시각적 개선 목적)
            if (balanceBiomes)
            {
                float centered = (normalizedNoise - 0.5f) * 2f;  // [-1, 1]
                // pow 0.5는 말단 쪽으로 값 분산 (acos-like 근사)
                float equalized = Mathf.Sign(centered) * Mathf.Pow(Mathf.Abs(centered), 0.5f);
                normalizedNoise = equalized * 0.5f + 0.5f;       // [0, 1] 재매핑
            }

            // 2. Cardinal Bias 적용 (현재 shader 미소비, Preview에서 "만약" 시뮬레이션)
            float biasContribution = 0f;
            if (bias != null && bias.biasStrength > 0.001f && perBiomeBiasVectors != null)
            {
                biasContribution = ComputeCardinalBiasContribution(
                    worldPos, bias, perBiomeBiasVectors, ref sample);
            }
            normalizedNoise = Mathf.Clamp01(normalizedNoise + biasContribution);

            float scaledMap = normalizedNoise * Mathf.Max(biomeCount - 1f, 0f);
            int idxA = Mathf.Clamp(Mathf.FloorToInt(scaledMap), 0, biomeCount - 1);
            int idxB = Mathf.Clamp(idxA + 1, 0, biomeCount - 1);
            float blend = Mathf.SmoothStep(0f, 1f, scaledMap - Mathf.Floor(scaledMap));

            sample.biomeIdxA = idxA;
            sample.biomeIdxB = idxB;
            sample.blendWeight = blend;
            return sample;
        }

        /// <summary>
        /// CardinalBias의 기여도 계산 — normalizedNoise에 더해지는 offset.
        /// 각 biome의 bias 방향과 worldPos 방향의 dot product × strength × falloff.
        ///
        /// 설계:
        ///   bias가 강한 방위에서는 normalizedNoise가 그 biome 인덱스 쪽으로 이동.
        ///   거리 falloff로 원점 근처는 약한 영향, 경계 근처는 강한 영향.
        /// </summary>
        private static float ComputeCardinalBiasContribution(
            Vector3 worldPos, CardinalBiomeBiasSO bias,
            List<Vector3> perBiomeBiasVectors, ref BiomeSamplePoint sample)
        {
            if (perBiomeBiasVectors == null || perBiomeBiasVectors.Count == 0) return 0f;

            float dist = worldPos.magnitude;
            float distNorm = Mathf.Clamp01(dist / Mathf.Max(bias.maxInfluenceDistance, 1f));
            float falloff = bias.distanceFalloff != null
                ? bias.distanceFalloff.Evaluate(distNorm)
                : 1f - distNorm;

            Vector3 worldDir = worldPos.sqrMagnitude > 0.001f ? worldPos.normalized : Vector3.zero;
            Vector3 accumBiasVec = Vector3.zero;
            float totalShift = 0f;

            for (int i = 0; i < perBiomeBiasVectors.Count; i++)
            {
                Vector3 bv = perBiomeBiasVectors[i];
                if (bv.sqrMagnitude < 0.001f) continue;

                float dotVal = Vector3.Dot(worldDir, bv.normalized);
                float contribution = dotVal * bv.magnitude * falloff * bias.biasStrength;

                // biome idx 방향으로 shift 누적
                //   biome i가 선호되면 normalizedNoise를 그쪽으로 유도
                //   간단화: biome 인덱스 중앙값에서 떨어진 i에 비례
                float biomeCount = perBiomeBiasVectors.Count;
                float targetRatio = i / Mathf.Max(biomeCount - 1f, 1f);  // [0, 1]
                float currentRatio = 0.5f;  // 중립
                totalShift += (targetRatio - currentRatio) * Mathf.Abs(contribution) * 0.3f;
                //  0.3 = bias 영향 clamp (과도한 shift 방지)

                accumBiasVec += bv.normalized * contribution;
            }

            sample.cardinalBiasApplied = accumBiasVec;
            return Mathf.Clamp(totalShift, -0.4f, 0.4f);  // 최대 ±0.4 offset
        }

        /// <summary>
        /// XZ 평면 grid 순회하여 BiomeSamplePoint 리스트 반환.
        /// Heatmap 렌더용 대량 샘플 생성.
        /// 
        /// [Phase 4.5-B hotfix v4] actualGridSize out 파라미터 — MAX_CELLS auto-adjust 후 값 반환
        /// </summary>
        public static List<BiomeSamplePoint> SampleGrid(
            PreviewInputs inputs, out float actualGridSize,
            List<IPreviewExtension> extensions = null)
        {
            var samples = new List<BiomeSamplePoint>();
            actualGridSize = Mathf.Max(inputs.heatmapGridSize, 2f);
            if (inputs.biomes == null || inputs.biomes.Count == 0) return samples;

            // Bias 파라미터 수집
            List<Vector3> biasVecs = null;
            if (inputs.cardinalBiasEnabled && inputs.cardinalBias != null
                && inputs.cardinalBias.biasVectors != null
                && inputs.cardinalBias.biasVectors.Length > 0)
            {
                biasVecs = new List<Vector3>(inputs.cardinalBias.biasVectors);
            }

            // Grid 순회
            float gridSize = actualGridSize;
            Vector3 bounds = inputs.dungeonBounds;
            int cellsX = Mathf.CeilToInt(bounds.x / gridSize);
            int cellsZ = Mathf.CeilToInt(bounds.z / gridSize);

            // 가드: 너무 많은 셀 방지
            int totalCells = cellsX * cellsZ;
            const int MAX_CELLS = 5000;
            if (totalCells > MAX_CELLS)
            {
                float ratio = Mathf.Sqrt((float)totalCells / MAX_CELLS);
                gridSize *= ratio;
                cellsX = Mathf.CeilToInt(bounds.x / gridSize);
                cellsZ = Mathf.CeilToInt(bounds.z / gridSize);
                Debug.LogWarning($"[BiomeSampler] Grid cells > {MAX_CELLS}. Auto-adjusted gridSize to {gridSize:F1}m.");
            }
            actualGridSize = gridSize;  // ★ 실제 사용 값 저장

            float yPlane = inputs.heatmapYPlane;
            for (int ix = 0; ix < cellsX; ix++)
            {
                float wx = -bounds.x * 0.5f + (ix + 0.5f) * gridSize;
                for (int iz = 0; iz < cellsZ; iz++)
                {
                    float wz = -bounds.z * 0.5f + (iz + 0.5f) * gridSize;
                    Vector3 pos = new Vector3(wx, yPlane, wz);
                    var sample = SampleAt(pos, inputs.biomes.Count, inputs.macroBiomeScale,
                        inputs.cardinalBias, biasVecs, inputs.balanceBiomes);

                    // ★ Grid 좌표 저장 (Renderer 동기화용)
                    sample.gridIx = ix;
                    sample.gridIz = iz;

                    // 확장 기능 호출 (Phase 5 SubBiome 등)
                    if (extensions != null)
                    {
                        foreach (var ext in extensions)
                        {
                            if (ext.IsActive(inputs))
                                ext.OnGenerateBiomeSample(sample, inputs);
                        }
                    }

                    samples.Add(sample);
                }
            }
            return samples;
        }

        /// <summary>
        /// biome noiseType → Color 매핑 (Scene view 렌더와 일치).
        /// BiomeDebugVisualizer.GetBiomeColor와 동일 규칙.
        /// </summary>
        public static Color GetBiomeColor(int noiseType)
        {
            switch (noiseType)
            {
                case 0: return new Color(0.6f, 0.4f, 0.2f);     // Karst
                case 1: return new Color(0.3f, 0.4f, 0.9f);     // Columnar
                case 2: return new Color(0.9f, 0.8f, 0.4f);     // Sedimentary
                case 3: return new Color(0.95f, 0.95f, 0.9f);   // Colonnade
                case 4: return new Color(0.5f, 0.5f, 0.5f);     // Rocky
                case 5: return new Color(1f, 0.5f, 0.2f);       // Canyon
                case 6: return new Color(0.3f, 0.8f, 0.85f);    // Marine
                default: return Color.magenta;
            }
        }

        public static string GetBiomeName(int noiseType)
        {
            switch (noiseType)
            {
                case 0: return "Karst";
                case 1: return "Columnar";
                case 2: return "Sedimentary";
                case 3: return "Colonnade";
                case 4: return "Rocky";
                case 5: return "Canyon";
                case 6: return "Marine";
                default: return $"Unknown({noiseType})";
            }
        }
    }
}
#endif
