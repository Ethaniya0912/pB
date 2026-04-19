using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CaveSystem
{
    public enum GenerationDebugStage
    {
        RawVoronoiSkeleton = 0,
        DomainWarped = 1,
        NaturalFloor = 2,
        BiomeDetails = 3,
        ErosionSimulated = 4
    }

    [System.Serializable]
    public struct OreProbability
    {
        public int oreType;
        [Range(0f, 1f)] public float probability;
        public float clusterSize;
    }

    [System.Serializable]
    public struct DepthLayer
    {
        public string layerName;
        public float maxAltitude;
        public float minAltitude;

        [Header("2.5D SDF Constraints")]
        public float floorBlendRadius;
        public float ceilBlendRadius;
        public float floorBumpAmplitude;
        public float floorBumpFrequency;

        [Header("Geometry Settings")]
        public float noiseFrequency;
        public float sdfSmoothness;

        [Header("Sinkhole & Ledge")]
        [Range(0f, 1f)] public float sinkholeProbability;
        public float sinkholeMinRadius;
        public float sinkholeMaxRadius;
        public float sinkholeSmoothness;

        public float ledgeStepHeight;
        public float spiralFrequency;
        public float spiralAmplitude;

        [Header("Phase 1 — Scaling & Sediment")]
        [Range(0.1f, 2f)] public float tunnelWidthScale;
        [Range(0.1f, 2f)] public float roomSizeScale;
        [Range(0f, 0.5f)] public float sedimentAmplitude;

        [Header("Phase 1.5 — Floor Detail")]
        [Range(0f, 0.5f)] public float floorDetailAmplitude;
        [Range(0.01f, 1f)] public float floorDetailFrequency;
        [Range(0.5f, 5f)] public float floorDetailRadius;

        [Header("Atmosphere")]
        public Color layerFogColor;
        public float waterLevel;

        [Header("Ecosystem")]
        public List<OreProbability> oreDistributions;
    }

    /// <summary>
    /// 동굴 생성의 파라미터를 제어하는 마스터 설정 파일입니다.
    /// 구조적(Layer) 설정과 표면 질감(Biome) 설정을 분리 관리합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "CaveBiomeSettings", menuName = "Cave System/Layered Biome Settings", order = 1)]
    public class CaveBiomeSettings : ScriptableObject
    {
        [Header("Global Settings")]
        [Tooltip("인스펙터에서 생성 단계를 시각적으로 디버깅할 수 있습니다.")]
        public GenerationDebugStage debugStage = GenerationDebugStage.ErosionSimulated;

        public int seed = 12345;

        [Range(1, 8)]
        public int maxOctaves = 5;
        public float lacunarity = 2.0f;
        public float gain = 0.5f;

        [Header("Multi-Biome Distribution")]
        [Tooltip("거시적(Macro) 바이옴 맵의 스케일입니다. 값이 클수록 한 바이옴의 영역이 넓어집니다.")]
        public float macroBiomeScale = 500.0f;

        [Tooltip("월드에 출현할 바이옴 데이터 에셋들을 등록합니다. 이 인덱스가 GPU 바이옴 ID가 됩니다.")]
        public List<CaveBiomeData> globalBiomes = new List<CaveBiomeData>();

        [Header("Layered Constraints Data")]
        public List<DepthLayer> depthLayers = new List<DepthLayer>();

        /// <summary>
        /// 하드웨어 성능(Quality Level)에 따라 동적으로 노이즈 연산 옥타브를 스케일링합니다.
        /// </summary>
        public int GetActiveOctaves()
        {
            int qualityLevel = QualitySettings.GetQualityLevel();
            if (qualityLevel <= 1) return Mathf.Clamp(maxOctaves - 3, 2, 8);
            else if (qualityLevel <= 3) return Mathf.Clamp(maxOctaves - 1, 2, 8);
            return maxOctaves;
        }

        /// <summary>
        /// 플레이어의 Y 고도를 바탕으로 현재 층(Layer)의 데이터를 반환합니다.
        /// </summary>
        public DepthLayer GetLayerSettings(float playerY)
        {
            if (depthLayers == null || depthLayers.Count == 0) return default;

            foreach (var layer in depthLayers)
            {
                if (playerY <= layer.maxAltitude && playerY > layer.minAltitude) return layer;
            }
            // 맵 밖으로 떨어졌을 경우 가장 깊은 층 반환 (Fallback)
            return depthLayers[depthLayers.Count - 1];
        }

#if UNITY_EDITOR
        /// <summary>
        /// 에디터 로드 시점에 데이터 구조체의 메모리 정렬(Alignment) 무결성을 강제 검증합니다.
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void ValidateDataStructures()
        {
            int biomeSize = Marshal.SizeOf(typeof(BiomeParamData));
            if (biomeSize % 16 != 0 || biomeSize != 32)
            {
                Debug.LogError($"[치명적 오류] BiomeParamData가 16바이트 정렬 규칙을 위반했습니다! 현재 크기: {biomeSize} Bytes. GPU 메모리 밀림이 발생합니다.");
            }
            int nodeSize = Marshal.SizeOf(typeof(NodeData));
            if (nodeSize != 32)
            {
                Debug.LogError($"[치명적 오류] NodeData의 메모리 크기가 32바이트가 아닙니다! 패딩을 확인하세요. 현재: {nodeSize}");
            }
            int oreDataSize = Marshal.SizeOf(typeof(CaveOreData));
            if (oreDataSize != 32)
            {
                Debug.LogError($"[치명적 오류] CaveOreData의 메모리 크기가 32바이트가 아닙니다! 패딩을 확인하세요. 현재: {oreDataSize}");
            }
        }
#endif
    }
}