// =============================================================================
// BiomeSDF_ProfileSO.cs  |  pB-4 Project — Week 0
// Layer  : Data/SO (지형)
// Owner  : Person B
//
// 역할:
//   바이옴별 SDF 밀도장 노이즈 레시피를 데이터 드리븐으로 정의.
//   기존 CaveBiomeData의 noiseFrequency/yCompression 등을 대체하지 않고,
//   5레이어 파이프라인의 확장된 파라미터를 독립 SO로 관리.
//   기존 CaveBiomeData가 할당된 경우 레거시 동작, BiomeSDF_ProfileSO가
//   추가 할당된 경우 이 SO의 파라미터가 우선 적용.
//
// GPU 전송:
//   GetBiomeParamData()로 기존 BiomeParamData 구조체에 패킹하여
//   기존 CaveComputeDispatcher.UpdateBiomeBuffer() 패턴 준수.
// =============================================================================
using System;
using UnityEngine;

namespace CaveSystem
{
    public enum NoiseType { Perlin, Simplex, Worley, RidgedPerlin, RidgedSimplex, WorleyF2F1 }
    public enum NoiseBlendMode { Add, Max, Subtract, Multiply }

    [Serializable]
    public struct NoiseLayerConfig
    {
        public NoiseType type;

        // [Gate 5 Phase A.2/A.3] 원칙 2 (Nyquist) 이중 보호
        //   (1) asset-time: 여기 [Range(1,3)] — Designer Inspector 단계 clamp
        //   (2) compile-time: CaveNoiseLibrary.hlsl MAX_OCTAVES=3 — shader runtime clamp
        //   voxelSize=0.15m 기준 octaves=3 + lacunarity=2 → final λ = base / 4
        //   scale.x 0.5 (λ 2m) 넣으면 final λ 0.5m = Nyquist 마진 3.3×
        [Range(1, 3)] public int octaves;
        [Range(1f, 4f)] public float lacunarity;
        [Range(0.1f, 1f)] public float persistence;
        public Vector3 scale;       // 비등방성 지원 (XYZ 독립 스케일)
        [Range(0f, 5f)] public float amplitude;
    }

    [Serializable]
    public struct DomainWarpConfig
    {
        [Range(0.01f, 0.5f)] public float warpScale;
        [Range(0f, 2f)] public float warpAmplitude;

        // [Gate 5 Phase A.3] warpOctaves도 동일 가드 (기존 (1,4) → (1,3))
        //   Warp은 position displacement라 Nyquist 직접 위반은 아니지만
        //   octaves 증가 시 비용 대비 효과 한계. 3 이상 의미 없음.
        [Range(1, 3)] public int warpOctaves;

        // [Gate 5 Phase A.3/A.4] 재귀 워핑 — 기본 OFF (규칙 #6 + 원칙 1)
        //   recursiveWarp=true는 warp 결과를 다시 warp 입력으로 사용 (fBm-of-fBm).
        //   원칙 1 위반 가능성 (analytical → 보조 → 보조의 보조). A.4에서 검증 후 활성.
        public bool recursiveWarp;
    }

    [Serializable]
    public struct BiomeBlendConfig
    {
        [Range(20f, 100f)] public float blendRadius;
        [Range(0.01f, 0.1f)] public float blendNoiseScale;
        [Range(0f, 0.5f)] public float blendNoiseAmplitude;
    }

    /// <summary>
    /// 바이옴별 SDF 5레이어 파이프라인 파라미터.
    /// Inspector에서 전체 SDF 특성을 시각적으로 조정 가능.
    /// </summary>
    [CreateAssetMenu(fileName = "NewBiomeSDF", menuName = "Cave System/Biome SDF Profile")]
    public class BiomeSDF_ProfileSO : ScriptableObject
    {
        [Header("Identity")]
        public TDA.PB4.Data.BiomeType biomeType;
        public string profileName;

        [Header("Layer 2 — Primary Noise")]
        public NoiseLayerConfig primaryNoise = new NoiseLayerConfig
        {
            type = NoiseType.Perlin,
            octaves = 3,   // [A.3] MAX_OCTAVES 상한 맞춤 (기존 4 → 3)
            lacunarity = 2.0f,
            persistence = 0.5f,
            scale = new Vector3(0.02f, 0.02f, 0.02f),
            amplitude = 1.0f
        };

        [Header("Layer 2 — Secondary Noise (optional)")]
        public bool useSecondaryNoise = false;
        public NoiseLayerConfig secondaryNoise;
        public NoiseBlendMode secondaryBlendMode = NoiseBlendMode.Add;
        [Range(0f, 1f)] public float secondaryBlendWeight = 0.3f;

        [Header("Layer 3 — Domain Warping")]
        public DomainWarpConfig domainWarp = new DomainWarpConfig
        {
            warpScale = 0.05f,
            warpAmplitude = 0.8f,
            warpOctaves = 2,
            recursiveWarp = false  // [A.3] 기본 OFF (규칙 #6 — A.4에서 검증 후 활성)
        };

        [Header("Layer 1 — SDF Smoothing")]
        [Range(0.1f, 5f)] public float smoothK = 2.0f;

        [Header("Layer 5 — Biome Boundary")]
        public BiomeBlendConfig biomeBlend = new BiomeBlendConfig
        {
            blendRadius = 50f,
            blendNoiseScale = 0.03f,
            blendNoiseAmplitude = 0.3f
        };

        /// <summary>
        /// [Gate 5 Phase A.3] MAX_OCTAVES 런타임 clamp — C# 측에서도 이중 보호.
        /// shader 측 CaveNoiseLibrary.hlsl MAX_OCTAVES=3과 동기화.
        /// asset-time [Range(1,3)]가 깨진 경우 (assetDB 수동 편집 등) 방어.
        /// </summary>
        public const int MAX_OCTAVES_CS = 3;

        /// <summary>
        /// 기존 BiomeParamData 구조체로 패킹.
        /// CaveComputeDispatcher.UpdateBiomeBuffer() 패턴 준수.
        /// primaryNoise의 핵심 파라미터를 기존 필드에 매핑.
        ///
        /// [Gate 5 Phase A.3]
        ///   규칙 #6: 호출 안 하면 기존 경로 → byte-identical.
        ///           Dispatcher가 caveSettings.sdfProfiles[i]이 할당된 경우에만
        ///           이 메서드로 오버라이드 (fallback pattern).
        ///   원칙 2: octaves는 MAX_OCTAVES_CS로 runtime clamp 적용.
        ///   노트: secondaryNoise 블렌드는 아직 BiomeParamData 포맷에 수용 슬롯 없음 →
        ///         A.3 range에서는 primary만 반영. secondary는 A.6/A.7 별도 경로.
        /// </summary>
        public BiomeParamData GetBiomeParamData()
        {
            int safeOctaves = Mathf.Clamp(primaryNoise.octaves, 1, MAX_OCTAVES_CS);
            // (현재 BiomeParamData는 octaves 필드 없음 — A.6 이후 확장 여지)
            // 여기선 결과적 signature만 기존 포맷에 패킹, shader는 noiseType 분기로 해석

            return new BiomeParamData
            {
                noiseFrequency = primaryNoise.scale.x,
                yCompression = primaryNoise.scale.y / Mathf.Max(0.001f, primaryNoise.scale.x),
                sminStrength = smoothK,
                terraceSteps = 0f, // 기존 레거시 필드, 여기서는 미사용
                bumpAmplitude = primaryNoise.amplitude,
                bumpFrequency = primaryNoise.scale.z,
                noiseType = (int)primaryNoise.type,
                blendDamping = 1.0f   // [Phase 4.5-G] 레거시 profile은 감쇠 없음 (backward compat)
            };
        }

        // 에디터 변경 시 실시간 갱신
        public static event Action OnProfileModified;
        private void OnValidate()
        {
            if (Application.isPlaying)
                OnProfileModified?.Invoke();
        }
    }
}
