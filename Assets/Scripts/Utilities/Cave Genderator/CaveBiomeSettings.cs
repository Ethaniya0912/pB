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
        [Tooltip("이 레이어의 식별 이름 (예: 1F, B1, 심층부 등)")]
        public string layerName;

        [Tooltip("이 레이어가 적용되는 최대 Y 고도 (이 값 이하에서 활성)")]
        public float maxAltitude;

        [Tooltip("이 레이어가 적용되는 최소 Y 고도 (이 값 초과에서 활성)")]
        public float minAltitude;

        [Header("2.5D SDF Constraints")]
        [Tooltip("글로벌 바닥면(FloorAltitude=minAltitude)의 smax 블렌딩 반경 (m).\n"
                 + "통로 SDF와 바닥 평면의 전환대 크기.\n"
                 + "너무 크면 바닥면이 통로 내부로 침투 → 파편.\n"
                 + "tunnelWidthScale에 비례 축소됨.\n"
                 + "권장: 3~8m")]
        public float floorBlendRadius;

        [Tooltip("글로벌 천장면(CeilAltitude=maxAltitude)의 smax 블렌딩 반경 (m).\n"
                 + "floorBlendRadius와 동일 원리.\n"
                 + "권장: 3~8m")]
        public float ceilBlendRadius;

        [Tooltip("글로벌 바닥면의 Y 방향 노이즈 진폭 (m).\n"
                 + "바닥 평면을 약간 울퉁불퉁하게 만듦.\n"
                 + "권장: 0.1~0.5m / 0 = 완전 평탄")]
        public float floorBumpAmplitude;

        [Tooltip("글로벌 바닥 노이즈의 XZ 빈도.\n"
                 + "높을수록 좁은 주기로 반복.\n"
                 + "권장: 0.03~0.1")]
        public float floorBumpFrequency;

        [Header("Floor Surface Detail")]
        [Tooltip("바닥 표면 바위/요철 높이 (m). FloorAltitude 근처에서만 적용.\n"
                 + "Sign-Flip Guard가 파편을 차단하지만 진폭은 낮게 유지 권장.\n"
                 + "권장: 0.3~0.8m / 0 = 비활성")]
        public float floorDetailAmplitude;

        [Tooltip("바위/요철 XZ 빈도. 낮을수록 넓은 구획 바위.\n"
                 + "0.07 ≈ 90m 주기.\n"
                 + "권장: 0.04~0.12")]
        public float floorDetailFrequency;

        [Tooltip("바닥 표면 노이즈가 영향을 미치는 높이 반경 (m).\n"
                 + "FloorAltitude 위로 이 높이까지 노이즈 적용.\n"
                 + "권장: 2~4m")]
        public float floorDetailRadius;

        [Header("Corridor Surface Style")]
        [Tooltip("통로 벽면 표면 스타일.\n"
                 + "0 = smooth (매끄러운 암반 — 파편 최소)\n"
                 + "0.1~0.2 = slight organic (약간의 자연 요철)\n"
                 + "0.3+ = strata 수평 밴드 발생 — 파편 띠 위험!\n"
                 + "⚠ 0.3 이상은 수평 파편 띠의 직접 원인")]
        [Range(0f, 1f)]
        public float corridorSurface;

        [Header("Sediment Profile")]
        [Tooltip("통로 U자 퇴적 구배 진폭 (m).\n"
                 + "벽 근처 바닥이 중앙보다 높아지는 높이.\n"
                 + "실사 동굴처럼 벽 근처 암설 퇴적 효과.\n"
                 + "0 = 비활성, 0.3~0.8m 권장")]
        public float sedimentAmplitude;

        [Header("Tunnel & Room Scale")]
        [Tooltip("통로(엣지) 크기 배율.\n"
                 + "1.0 = 기본, 0.5 = 절반, 0.3 = 아늑한 동굴\n"
                 + "Floor/Ceil BlendRadius도 이 값에 비례 축소됨.\n"
                 + "※ SO에서 0이면 자동으로 1.0 적용")]
        [Range(0.1f, 2.0f)]
        public float tunnelWidthScale;

        [Tooltip("방(노드) 크기 배율. 통로와 별도 조절.\n"
                 + "1.0 = 기본, 0.5 = 절반\n"
                 + "⚠ 0.1 이하는 복셀 해상도보다 작아져 파편 위험\n"
                 + "※ SO에서 0이면 자동으로 1.0 적용")]
        [Range(0.1f, 2.0f)]
        public float roomSizeScale;

        [Header("Geometry Settings")]
        [Tooltip("바이옴 디테일 노이즈의 기본 주파수.\n"
                 + "높을수록 촘촘한 패턴.\n"
                 + "권장: 0.03~0.1")]
        public float noiseFrequency;

        [Tooltip("SDF 스무딩 (smin/smax k값).\n"
                 + "높을수록 노드-엣지 접합이 부드러움.\n"
                 + "권장: 1.5~4.0")]
        public float sdfSmoothness;

        [Header("Sinkhole & Ledge")]
        [Tooltip("청크당 싱크홀 생성 확률.\n0 = 비활성, 0.1 = 10%")]
        [Range(0f, 1f)] public float sinkholeProbability;

        [Tooltip("싱크홀 최소 반지름 (m). 권장: 4~8")]
        public float sinkholeMinRadius;

        [Tooltip("싱크홀 최대 반지름 (m). 권장: 8~20")]
        public float sinkholeMaxRadius;

        [Tooltip("싱크홀 경계 smax 부드러움. 권장: 2~6")]
        public float sinkholeSmoothness;

        [Tooltip("싱크홀 내부 수직 단차 높이 (m). 권장: 1~3")]
        public float ledgeStepHeight;

        [Tooltip("싱크홀 나선 패턴 Y축 주파수. 권장: 0.1~0.5")]
        public float spiralFrequency;

        [Tooltip("싱크홀 나선 진폭 (m). 0 = 순수 수직. 권장: 0.5~2.0")]
        public float spiralAmplitude;

        [Header("Atmosphere")]
        [Tooltip("이 레이어의 안개 색상. 깊은 층일수록 어두운 색 권장.")]
        public Color layerFogColor;

        [Tooltip("지하수면 Y 고도. 이 높이 이하에 물 렌더링.\n사용 안 하면 minAltitude 이하로 설정")]
        public float waterLevel;

        [Header("Ecosystem")]
        [Tooltip("이 레이어에서 생성되는 광석 종류와 확률 목록.")]
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

        [Tooltip("동굴 생성 시드. 같은 시드 = 같은 지형.")]
        public int seed = 12345;

        [Tooltip("노이즈 옥타브 수. QualityLevel에 따라 자동 조절됨.")]
        [Range(1, 8)]
        public int maxOctaves = 5;

        [Tooltip("fBm 라쿠나리티. 각 옥타브 주파수 배율. 2.0 = 표준")]
        public float lacunarity = 2.0f;

        [Tooltip("fBm 게인. 각 옥타브 진폭 감쇠율. 0.5 = 표준")]
        public float gain = 0.5f;

        [Header("Multi-Biome Distribution")]
        [Tooltip("거시적 바이옴 맵 스케일. 클수록 한 바이옴 영역이 넓어짐.\n500 = 약 500m 간격 경계")]
        public float macroBiomeScale = 500.0f;

        [Tooltip("월드에 출현할 바이옴 데이터 에셋 목록. 인덱스 = GPU 바이옴 ID.")]
        public List<CaveBiomeData> globalBiomes = new List<CaveBiomeData>();

        [Header("Layered Constraints Data")]
        [Tooltip("Y 고도별 레이어 설정. 각 레이어에 별도 지형 파라미터 적용.")]
        public List<DepthLayer> depthLayers = new List<DepthLayer>();

        public int GetActiveOctaves()
        {
            int qualityLevel = QualitySettings.GetQualityLevel();
            if (qualityLevel <= 1) return Mathf.Clamp(maxOctaves - 3, 2, 8);
            else if (qualityLevel <= 3) return Mathf.Clamp(maxOctaves - 1, 2, 8);
            return maxOctaves;
        }

        public DepthLayer GetLayerSettings(float playerY)
        {
            if (depthLayers == null || depthLayers.Count == 0) return default;
            foreach (var layer in depthLayers)
            {
                if (playerY <= layer.maxAltitude && playerY > layer.minAltitude) return layer;
            }
            return depthLayers[depthLayers.Count - 1];
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void ValidateDataStructures()
        {
            int biomeSize = Marshal.SizeOf(typeof(BiomeParamData));
            if (biomeSize % 16 != 0 || biomeSize != 32)
                Debug.LogError($"[치명적 오류] BiomeParamData 16바이트 정렬 위반! 현재: {biomeSize}B");
            int nodeSize = Marshal.SizeOf(typeof(NodeData));
            if (nodeSize != 32)
                Debug.LogError($"[치명적 오류] NodeData 크기 ≠ 32B! 현재: {nodeSize}");
            int oreDataSize = Marshal.SizeOf(typeof(CaveOreData));
            if (oreDataSize != 32)
                Debug.LogError($"[치명적 오류] CaveOreData 크기 ≠ 32B! 현재: {oreDataSize}");
        }
#endif
    }
}