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

        // ═══════════════════════════════════════════════════════════════
        // [Gate 5 Phase A.1 — DepthLayer Extension]
        //
        // v34 MD §2.2/§6.1에서 제시된 DepthLayer 수직 확장 필드들.
        // 모두 Dispatcher fallback 패턴 준수 (Critical Rule: Range 금지).
        // 값이 0이면 Dispatcher가 기본값으로 주입 → 기존 hardcode 동작과
        // byte-identical 유지 (규칙 #6).
        //
        // [규칙 #28] canyonCeilingHeight
        //   (1) 목적: Case 5 협곡 수직 확장 상한을 SDF EvaluateSkeleton에 전달
        //             기존 shader 내 maxCanyonHeight = 25.0 하드코딩을 외부화
        //   (2) 기본값 0 이유: 0은 "미지정" 의미 — Dispatcher가 25.0 fallback 적용
        //             기존 DepthLayer asset 전체 호환 (규칙 #6)
        //   (3) 활성화 전제: 값 > 0.01 이면 override 활성
        //             권장 범위: 25.0 (기존) ~ 45.0 (SS1 목표 협곡)
        //   (4) 영향: Case 5 canyon skeleton의 수직 상한만 변경
        //             다른 Case는 영향 없음 (wallMask / canyonWeight로 분리됨)
        //
        // [규칙 #28] warpAmplitudeOverride
        //   (1) 목적: DomainWarp _WarpAmplitude를 layer별로 override 가능하게
        //             일부 layer(해식동굴 등)는 낮은 warp 선호
        //   (2) 기본값 0 이유: 0은 "미지정" — Dispatcher가 global effectiveWarp 유지
        //             기존 동작 byte-identical
        //   (3) 활성화 전제: 값 > 0.01 이면 override 활성
        //             권장 범위: 0.2 (낮음) ~ 0.8 (높음), 기본 0.5
        //   (4) 영향: 해당 layer 영역의 domain warp 강도만 변경
        // ═══════════════════════════════════════════════════════════════
        [Header("[A.1] v34 MD Extension (Range 금지 — 0 = Dispatcher fallback)")]
        [Tooltip("Case 5 canyon 수직 상한 (m). 0 = fallback 25.0. SS1 협곡 목표: 45.0")]
        public float canyonCeilingHeight;

        [Tooltip("Domain warp amplitude override. 0 = fallback (global _WarpAmplitude 유지). 권장 0.2~0.8")]
        public float warpAmplitudeOverride;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // [FORMAT_VERSION 11] DepthLayer GPU 전송용 구조체
    //   목적: _EnablePerVoxelLayerBlend == 1 시 shader가 각 voxel의 worldPos.y 기준
    //         두 인접 layer를 blend하여 SDF C⁰ 연속성 확보 (인접 chunk mesh 교차 해소)
    //   규격: 20 × float = 80 bytes (16-byte 정렬 준수)
    //   Critical Rule: DepthLayer SO 본체(Range attribute 등)는 건드리지 않고
    //                  이 GPU-전용 struct에만 필드 복사 (원칙 준수)
    // ═══════════════════════════════════════════════════════════════════════════════
    [StructLayout(LayoutKind.Sequential)]
    public struct DepthLayerGPUData
    {
        public float minAltitude;
        public float maxAltitude;
        public float floorBlendRadius;
        public float ceilBlendRadius;
        public float floorBumpAmplitude;
        public float floorBumpFrequency;
        public float sdfSmoothness;
        public float sinkholeProbability;
        public float sinkholeMinRadius;
        public float sinkholeMaxRadius;
        public float sinkholeSmoothness;
        public float ledgeStepHeight;
        public float spiralFrequency;
        public float spiralAmplitude;
        public float tunnelWidthScale;
        public float roomSizeScale;
        public float sedimentAmplitude;
        public float floorDetailAmplitude;
        public float floorDetailFrequency;
        public float floorDetailRadius;

        // [Gate 5 Phase A.1] v34 MD §6.1 extension — Dispatcher fallback 패턴
        public float canyonCeilingHeight;   // 0 → fallback 25.0 (GPU 측에서도 0이면 무효 처리)
        public float warpAmplitudeOverride; // 0 → fallback (global _WarpAmplitude 유지)

        public static DepthLayerGPUData From(DepthLayer src)
        {
            return new DepthLayerGPUData
            {
                minAltitude          = src.minAltitude,
                maxAltitude          = src.maxAltitude,
                floorBlendRadius     = src.floorBlendRadius,
                ceilBlendRadius      = src.ceilBlendRadius,
                floorBumpAmplitude   = src.floorBumpAmplitude,
                floorBumpFrequency   = src.floorBumpFrequency,
                sdfSmoothness        = src.sdfSmoothness,
                sinkholeProbability  = src.sinkholeProbability,
                sinkholeMinRadius    = src.sinkholeMinRadius,
                sinkholeMaxRadius    = src.sinkholeMaxRadius,
                sinkholeSmoothness   = src.sinkholeSmoothness,
                ledgeStepHeight      = src.ledgeStepHeight,
                spiralFrequency      = src.spiralFrequency,
                spiralAmplitude      = src.spiralAmplitude,
                tunnelWidthScale     = src.tunnelWidthScale > 0.01f ? src.tunnelWidthScale : 1f,
                roomSizeScale        = src.roomSizeScale    > 0.01f ? src.roomSizeScale    : 1f,
                sedimentAmplitude    = src.sedimentAmplitude,
                floorDetailAmplitude = src.floorDetailAmplitude,
                floorDetailFrequency = src.floorDetailFrequency,
                floorDetailRadius    = src.floorDetailRadius,

                // [A.1] Dispatcher fallback 패턴 (Critical Rule: Range 금지 → 0 값 방지용)
                canyonCeilingHeight   = src.canyonCeilingHeight   > 0.01f ? src.canyonCeilingHeight   : 25.0f,
                warpAmplitudeOverride = src.warpAmplitudeOverride > 0.01f ? src.warpAmplitudeOverride : 0f
                // warpAmplitudeOverride=0은 "override 안 함" 의미 → Dispatcher에서 effectiveWarp 유지
            };
            // tunnelWidthScale/roomSizeScale은 Dispatcher의 ">0.01 ? : 1"
            // fallback pattern (Critical Rule: Range attribute 금지 → 0 값 방지)을 여기서 일관 적용
        }
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

        // ═══════════════════════════════════════════════════════════════
        // [Gate 5 Phase A.3] BiomeSDF_ProfileSO optional override
        //
        // [규칙 #28] sdfProfiles
        //   (1) 목적: globalBiomes와 병렬로 BiomeSDF_ProfileSO 할당 가능.
        //             할당된 slot은 해당 프로파일의 GetBiomeParamData()가
        //             기존 CaveBiomeData.GetStructData()를 override.
        //   (2) 기본값 null 이유: 규칙 #6 — null 전부면 기존 경로 유지 →
        //             byte-identical 보장. 점진적 마이그레이션 가능.
        //   (3) 활성화 전제: globalBiomes와 동일 인덱스에 null 아닌 profile 할당.
        //             각 profile은 자체 NoiseLayerConfig/DomainWarp/Blend 설정 보유.
        //   (4) 영향: 해당 인덱스의 biome만 새 SDF 파이프라인 사용. 다른 biome은
        //             레거시. 점진 전환 안전.
        //
        // 권장 사용:
        //   - globalBiomes.Count == sdfProfiles.Count 유지 (1:1 매핑)
        //   - sdfProfiles[i] = null 이면 globalBiomes[i] 사용 (fallback)
        //   - sdfProfiles[i].biomeType는 참고용, 실제 매핑은 List 인덱스
        // ═══════════════════════════════════════════════════════════════
        [Header("[A.3] Biome SDF Profiles (optional — null = legacy 경로)")]
        [Tooltip("globalBiomes와 인덱스 1:1 매핑. null slot은 레거시 CaveBiomeData 사용 (규칙 #6).")]
        public List<BiomeSDF_ProfileSO> sdfProfiles = new List<BiomeSDF_ProfileSO>();

        // ═══════════════════════════════════════════════════════════════
        // [Gate 5 Phase A.6] Cardinal Biome Bias (optional)
        //
        // [규칙 #28] cardinalBias
        //   (1) 목적: 월드 방위(동서남북상하)에 따른 biome 분포 편향.
        //             예 북쪽=Karst, 남쪽=Canyon, 하층=Sedimentary.
        //   (2) 기본값 null: 규칙 #6 — 기존 MacroBiome noise 경로 유지.
        //   (3) 활성화 전제: CardinalBiomeBiasSO asset 할당.
        //             biasStrength > 0 및 biasVectors.Length == globalBiomes.Count.
        //   (4) 영향: 현재 골격 단계 — SO 할당만 있고 shader 소비 없음.
        //             Phase 5 B.1~.15 시기에 shader 통합.
        // ═══════════════════════════════════════════════════════════════
        [Header("[A.6] Cardinal Biome Bias (optional)")]
        [Tooltip("월드 방위 기반 biome 분포 편향 SO. null이면 MacroBiome 노이즈만 (기존 동작, byte-identical).")]
        public CardinalBiomeBiasSO cardinalBias;

        // ═══════════════════════════════════════════════════════════════════
        // [Gate 5 Phase E-α.7] Room Geometry Matrix (optional)
        //
        // [규칙 #28] roomGeometryMatrix
        //   (1) 목적: roomType (Spawn/Boss/Treasure...) × biomeType (Case 0~6) 조합으로
        //             Room geometry/radius/sculptFlags 세밀 결정.
        //   (2) 기본값 null: 규칙 #6 — 기존 경로 (CaveBiomeData.roomPrimitive 전역).
        //   (3) 활성화 전제: MatrixSO 할당 + entries 리스트 채움.
        //   (4) 영향: NodeGraphBuilder NodeData 생성 시 radius/sculptFlags/primitive override.
        //             현재는 C# 레벨 override (graph 단계). shader primitive 소비는 Phase 5 통합.
        // ═══════════════════════════════════════════════════════════════════
        [Header("[E-α.7] Room Geometry Matrix (optional)")]
        [Tooltip("roomType × biomeType 매트릭스. null이면 기본 CaveBiomeData 값만 (기존 동작, byte-identical).")]
        public RoomGeometryMatrixSO roomGeometryMatrix;

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

        // ═══════════════════════════════════════════════════════════════════════════
        // [FORMAT_VERSION 11] GPU 전송용 DepthLayer 배열 생성
        //   Dispatcher가 _EnablePerVoxelLayerBlend == 1 시 이 배열을 ComputeBuffer로
        //   shader에 전달. Shader 내부 GetLayerBlended(worldPos.y)가 각 voxel의 Y에
        //   따라 두 인접 layer를 blend → C⁰ 연속성 확보.
        //
        //   반환 배열은 **maxAltitude 기준 내림차순 정렬** (위 layer가 index 0).
        //   빈 depthLayers 대비: 단일 기본 layer 반환 (빈 버퍼 방지).
        // ═══════════════════════════════════════════════════════════════════════════
        public DepthLayerGPUData[] BuildDepthLayerGPUArray()
        {
            if (depthLayers == null || depthLayers.Count == 0)
            {
                return new DepthLayerGPUData[]
                {
                    new DepthLayerGPUData
                    {
                        minAltitude = -100f, maxAltitude = 100f,
                        tunnelWidthScale = 1f, roomSizeScale = 1f
                    }
                };
            }

            // 원본 List 순서 보존 — 사용자가 Inspector에서 정렬한 순서를 신뢰
            //   (재정렬 시 paramHash 영향 있음 → 규칙 #10 준수)
            var arr = new DepthLayerGPUData[depthLayers.Count];
            for (int i = 0; i < depthLayers.Count; i++)
                arr[i] = DepthLayerGPUData.From(depthLayers[i]);
            return arr;
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
            // [FORMAT_VERSION 11] DepthLayerGPUData 정렬 검증
            int layerSize = Marshal.SizeOf(typeof(DepthLayerGPUData));
            if (layerSize != 80 || layerSize % 16 != 0)
            {
                Debug.LogError($"[치명적 오류] DepthLayerGPUData의 메모리 크기가 80바이트(16바이트 정렬)가 아닙니다! 현재: {layerSize}");
            }
        }
#endif
    }
}