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
        [Range(1, 8)] public int octaves;
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
        [Range(1, 4)] public int warpOctaves;
        public bool recursiveWarp;  // 2단계 재귀적 워핑 활성화
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
            octaves = 4,
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
            recursiveWarp = true
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
        /// 기존 BiomeParamData 구조체로 패킹.
        /// CaveComputeDispatcher.UpdateBiomeBuffer() 패턴 준수.
        /// primaryNoise의 핵심 파라미터를 기존 필드에 매핑.
        /// </summary>
        public BiomeParamData GetBiomeParamData()
        {
            return new BiomeParamData
            {
                noiseFrequency = primaryNoise.scale.x,
                yCompression = primaryNoise.scale.y / Mathf.Max(0.001f, primaryNoise.scale.x),
                sminStrength = smoothK,
                terraceSteps = 0f, // 기존 레거시 필드, 여기서는 미사용
                bumpAmplitude = primaryNoise.amplitude,
                bumpFrequency = primaryNoise.scale.z,
                noiseType = (int)primaryNoise.type,
                padding = 0f
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
