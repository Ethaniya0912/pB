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

    // [파라미터화: 하드코딩 변수 노출] 특정 고도 구간에서 사용될 모든 파라미터 묶음을 정의합니다.
    [System.Serializable]
    public struct DepthLayer
    {
        public string layerName;          // 예: "1층: 건조한 단층 지대"
        public float maxAltitude;         // 예: 15.0f (천장 고도)
        public float minAltitude;         // 예: -50.0f (하층부 전환 고도)

        [Header("2.5D SDF Constraints")]
        public float floorBlendRadius;    // 기본 3.0 (바닥 필렛 곡률)
        public float ceilBlendRadius;     // 기본 4.0 (천장 필렛 곡률)
        public float floorBumpAmplitude;  // 기본 2.0 (바닥 요철 깊이/높이)
        public float floorBumpFrequency;  // 기본 0.05 (바닥 요철 넓이/빈도)

        [Header("Geometry Settings")]
        public float noiseFrequency;      // 뼈대 생성 촘촘함 (빈도)
        public float sdfSmoothness;       // 벽면 부드러움 (smin k값)

        [Header("Sinkhole & Ledge (함정 및 발판)")]
        [Range(0f, 1f)] public float sinkholeProbability; // 5% = 0.05
        public float sinkholeMinRadius;   // 기본 6.0
        public float sinkholeMaxRadius;   // 기본 12.0
        public float sinkholeSmoothness;  // 기본 5.0

        public float ledgeStepHeight;     // 기본 2.5 (계단 단차)
        public float spiralFrequency;     // 기본 0.5 (나선형 촘촘함)
        public float spiralAmplitude;     // 기본 2.0 (나선형 튀어나온 깊이)

        [Header("Biome & Atmosphere")]
        public Color layerFogColor;       // 이 층의 포그 색상
        public Material terrainMaterial;  // 층 전용 트라이플래너 매터리얼

        public float waterLevel;

        [Header("Ecosystem")]
        public List<OreProbability> oreDistributions; // 층별 드롭 테이블
    }

    /// <summary>
    /// 동굴 생성의 파라미터를 고도(Depth)별로 제어하는 마스터 설정 파일입니다.
    /// 하드코딩되었던 매직 넘버를 흡수하여 완벽한 데이터 주도형 아키텍처를 완성합니다.
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

        [Header("Layered Biome Data")]
        // [파라미터화: 하드코딩 변수 노출] 리스트 형태로 여러 층(DepthLayer)의 데이터를 관리합니다.
        public List<DepthLayer> depthLayers = new List<DepthLayer>();

        /// <summary>
        /// 하드웨어 성능(Quality Level)에 따라 동적으로 노이즈 연산 옥타브를 스케일링합니다.
        /// </summary>
        public int GetActiveOctaves()
        {
            int qualityLevel = QualitySettings.GetQualityLevel();

            // 저사양 기기 (Low / Very Low)
            if (qualityLevel <= 1)
            {
                return Mathf.Clamp(maxOctaves - 3, 2, 8);
            }
            // 중사양 기기 (Medium / High)
            else if (qualityLevel <= 3)
            {
                return Mathf.Clamp(maxOctaves - 1, 2, 8);
            }

            // 고사양 기기 (Ultra)
            return maxOctaves;
        }

        /// <summary>
        /// [파라미터화: 하드코딩 변수 노출]
        /// 게임 루프에서 플레이어의 위치를 받아 어떤 층의 데이터를 사용할지 반환해 주는 핵심 API입니다.
        /// </summary>
        /// <param name="playerY">플레이어의 현재 Y 고도</param>
        /// <returns>해당 고도에 매칭되는 DepthLayer 구조체</returns>
        public DepthLayer GetLayerSettings(float playerY)
        {
            // 기본값 반환 방어
            if (depthLayers == null || depthLayers.Count == 0)
            {
                Debug.LogWarning("[CaveBiomeSettings] ⚠️ 정의된 DepthLayer가 없습니다. 빈 레이어 데이터를 반환합니다.");
                return default;
            }

            foreach (var layer in depthLayers)
            {
                // 현재 Y좌표가 해당 레이어의 고도 범위 내에 존재하는지 확인
                if (playerY <= layer.maxAltitude && playerY > layer.minAltitude)
                {
                    return layer;
                }
            }

            // [파라미터화: 하드코딩 변수 노출] Fallback 처리
            // 플레이어가 맵 밖이나 정의된 최하층보다 더 깊은 곳으로 떨어졌을 경우,
            // 크래시를 방지하기 위해 리스트의 가장 마지막(가장 깊은) 레이어 데이터를 반환합니다.
            return depthLayers[depthLayers.Count - 1];
        }
    }
}