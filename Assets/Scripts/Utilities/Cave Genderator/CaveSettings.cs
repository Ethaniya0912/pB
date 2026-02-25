using UnityEngine;
using System.Collections.Generic;

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

    /// <summary>
    /// 동굴 생성의 모든 파라미터를 제어하는 마스터 설정 파일입니다.
    /// URP 17 Volume Framework와의 연동을 고려하여 데이터가 분리되어 있습니다.
    /// </summary>
    [CreateAssetMenu(fileName = "CaveSettings", menuName = "Cave System/Cave Settings", order = 1)]
    public class CaveSettings : ScriptableObject
    {
        [Header("Debugging")]
        [Tooltip("인스펙터에서 생성 단계를 시각적으로 디버깅할 수 있습니다.")]
        public GenerationDebugStage debugStage = GenerationDebugStage.ErosionSimulated;

        [Header("Noise Settings (지형 뼈대 제어)")]
        public int seed = 12345;
        public float frequency = 0.05f;

        [Range(1, 8)]
        public int maxOctaves = 5;
        public float lacunarity = 2.0f;
        public float gain = 0.5f;

        [Header("Limestone SDF (석회암 질감 제어)")]
        [Tooltip("smin 함수의 k값. 높을수록 동굴 터널이 부드럽게 융합됩니다.")]
        public float sdfSmoothness = 2.5f;

        [Header("Environment (환경 생태계)")]
        [Tooltip("이 고도 이하의 빈 공간은 지하수로 채워집니다.")]
        public float waterLevel = -10.0f;

        [Range(0f, 1f)]
        [Tooltip("천장이 뚫린 싱크홀이 생성될 확률입니다.")]
        public float sinkholeProbability = 0.05f;

        public float caveRadius = 15.0f;

        [Header("Ore Distribution (광물 분포)")]
        public List<OreProbability> oreDistributions = new List<OreProbability>();

        /// <summary>
        /// 하드웨어 성능(Quality Level)에 따라 동적으로 노이즈 연산 옥타브를 스케일링합니다.
        /// 모바일 및 저사양 기기에서 Compute Shader 부하를 획기적으로 줄여줍니다.
        /// </summary>
        /// <returns>현재 활성화해야 할 옥타브 수</returns>
        public int GetActiveOctaves()
        {
            int qualityLevel = QualitySettings.GetQualityLevel();

            // 저사양 기기 (Low / Very Low)
            if (qualityLevel <= 1)
            {
                return Mathf.Clamp(maxOctaves - 3, 2, 8); // 최소 2옥타브 보장
            }
            // 중사양 기기 (Medium / High)
            else if (qualityLevel <= 3)
            {
                return Mathf.Clamp(maxOctaves - 1, 2, 8);
            }

            // 고사양 기기 (Ultra)
            return maxOctaves;
        }
    }
}