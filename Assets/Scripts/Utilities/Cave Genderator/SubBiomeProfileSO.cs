using UnityEngine;
using System;
using System.Collections.Generic;

namespace CaveSystem
{
    // ═══════════════════════════════════════════════════════════════════════
    // [Gate 5 Phase B.0] SubBiomeProfileSO — v3 Per-Layer LF+HF Material
    //
    // 목적:
    //   CaveBiomeData (Hub) 하위의 "완비된 세부 변주" SO.
    //   하나의 SubBiome = 자체 surface rendering + detail + environment 완결.
    //   예) 카르스트.subBiomes = [DryKarst, WetKarst, FloodedKarst]
    //
    // 설계 철학:
    //   - Floor Layer / Wall Layer 이원 구조 (수평/수직 노출에 따라 분리)
    //   - 각 Layer가 자체 LF + HF material set 보유 (흙/자갈/돌/이끼 각자 디테일)
    //   - MOHR 채널 규약: M=R, O=G, H=B, R=A (4채널 packing)
    //
    // 규칙 #6 byte-identical 보장:
    //   B.0에서는 "골격만" — shader 소비 없음.
    //   CaveBiomeData.subBiomes가 empty list (.Count==0)면 모든 후속 로직 skip.
    //   Dispatcher/shader는 기존 경로 그대로 사용.
    //   Phase 5 (B.1~B.15)에서 Texture2DArray packing + shader 로직 추가.
    //
    // 범위 분리:
    //   ✓ B.0 — SO 클래스 + 필드 정의 + Inspector 편집 가능 + 이벤트 체인
    //   ✗ B.0 — Texture2DArray packing, shader 소비, SubBiomeResolver
    // ═══════════════════════════════════════════════════════════════════════


    // ──────────────────────────────────────────────────────────────────
    // POM 적용 대상
    //   Floor Layer만 POM 적용 / Wall Layer만 / 둘 다 / 없음
    // ──────────────────────────────────────────────────────────────────
    public enum PomTarget
    {
        None = 0,
        FloorOnly = 1,
        WallOnly = 2,
        Both = 3
    }


    // ──────────────────────────────────────────────────────────────────
    // LayerMaterialSet — 하나의 완결된 material
    //
    // 구성:
    //   LF (Low Frequency) — 메인 material base
    //     - Triplanar sampling 대상
    //     - lfTileScale 작게 (원거리 식별)
    //   HF (High Frequency / Detail) — 근거리 디테일
    //     - LF 위에 overlay
    //     - hfTileScale 크게 (타일링 반복 숨김)
    //     - hasHF=false면 LF만 사용
    //
    // Placement:
    //   Floor Layer: layerStartParam = altitude (0~30m)
    //   Wall  Layer: layerStartParam = depth 또는 altitude (context별)
    //
    // MOHR 채널 규약:
    //   R = Metallic, G = Occlusion, B = Height (POM+blend), A = Roughness
    //   별도 lfHeight (선택, 16-bit 고정밀 POM용)
    // ──────────────────────────────────────────────────────────────────
    [System.Serializable]
    public struct LayerMaterialSet
    {
        [Header("Identity")]
        [Tooltip("Layer 이름 (예: '흙', '자갈', '돌', '이끼', '표층 석회암', '심층 백운암')")]
        public string layerName;

        // ── LF (Low Frequency) — 메인 material base ────────────────
        [Header("LF — Base Material (대형 texture)")]
        [Tooltip("sRGB base color — Triplanar sampling용 메인 albedo.")]
        public Texture2D lfAlbedo;

        [Tooltip("Tangent-space normal map.")]
        public Texture2D lfNormal;

        [Tooltip("Packed 4-channel: R=Metallic, G=Occlusion, B=Height(POM/Blend), A=Roughness.")]
        public Texture2D lfMOHR;

        [Tooltip("(선택) 별도 16-bit heightmap. null이면 lfMOHR.B 채널 fallback.")]
        public Texture2D lfHeight;

        [Tooltip("LF tile scale. 작을수록 텍스처 크게 보임 (원거리 식별 선호).")]
        [Range(0.1f, 10f)] public float lfTileScale;

        [Range(0f, 2f)] public float lfNormalIntensity;

        // ── HF (High Frequency / Detail) — 근거리 디테일 ───────────
        [Header("HF — Detail Material (근거리, 타일링 숨김)")]
        [Tooltip("이 layer에 HF 적용 여부. false면 LF만 사용.")]
        public bool hasHF;

        [Tooltip("Detail albedo — 흑백 또는 tint로 LF 위에 overlay.")]
        public Texture2D hfAlbedo;

        [Tooltip("Detail normal — LF normal과 blend.")]
        public Texture2D hfNormal;

        [Tooltip("(선택) HF MOHR. null이면 lfMOHR 재사용.")]
        public Texture2D hfMOHR;

        [Tooltip("HF tile scale — 크게 설정하여 근거리 디테일 밀도 ↑ (반복 숨김).")]
        [Range(1f, 50f)] public float hfTileScale;

        [Tooltip("HF overlay 강도. 0=LF만, 1=HF 완전 덮음.")]
        [Range(0f, 1f)] public float hfInfluence;

        [Range(0f, 2f)] public float hfNormalIntensity;

        // ── Placement ───────────────────────────────────────────────
        [Header("Placement")]
        [Tooltip("Layer 활성 시작 파라미터. Floor는 altitude(m), Wall은 depth/altitude(context별).")]
        public float layerStartParam;

        [Tooltip("다음 layer와 blend 범위 (m). 작을수록 sharp transition.")]
        [Range(0.05f, 5f)] public float layerBlendRange;

        // ── Physical Properties (gameplay 반영, 선택) ──────────────
        [Header("Physical (선택)")]
        [Tooltip("마찰 계수 (0=매끄러움, 1=거침). 플레이어 slip 물리.")]
        [Range(0f, 1f)] public float friction;

        [Tooltip("습윤도 (0=건조, 1=수중). splash/소리/시각 효과용.")]
        [Range(0f, 1f)] public float wetness;
    }


    // ──────────────────────────────────────────────────────────────────
    // OreVeinRule — B.9 광맥 생성 규칙
    // ──────────────────────────────────────────────────────────────────
    [System.Serializable]
    public struct OreVeinRule
    {
        [Tooltip("광물 식별자 이름 (예: 'Copper', 'Iron', 'Pearl').")]
        public string oreName;

        [Tooltip("OreSystem의 광물 타입 ID.")]
        public int oreTypeID;

        [Tooltip("이 sub-biome 영역 내 광맥 spawn 확률.")]
        [Range(0f, 1f)] public float spawnProbability;

        [Tooltip("고도(Y)별 분포 커브. X=normalized depth, Y=probability multiplier.")]
        public AnimationCurve altitudeDistribution;
    }


    // ══════════════════════════════════════════════════════════════════
    // SubBiomeProfileSO — 메인 SO
    // ══════════════════════════════════════════════════════════════════
    [CreateAssetMenu(fileName = "NewSubBiome", menuName = "Cave System/Sub Biome Profile", order = 11)]
    public class SubBiomeProfileSO : ScriptableObject
    {
        // ═══════════════════════════════════════
        // Identity
        // ═══════════════════════════════════════
        [Header("Identity")]
        [Tooltip("Sub-biome 식별 이름 (예: 'Dry Karst', 'Wet Karst', 'Flooded Karst').")]
        public string subBiomeName = "New SubBiome";

        // ═══════════════════════════════════════
        // Variation (B.1 / B.8) — 항상 Additive
        // ═══════════════════════════════════════
        [Header("Variation (Additive)")]
        [Tooltip("Shader에서 base color에 곱함. 흰색=변화없음.")]
        public Color hueTint = Color.white;

        [Tooltip("Luminance에 더함. 0=변화없음, -=어둡게, +=밝게.")]
        [Range(-0.5f, 0.5f)] public float brightnessOffset = 0f;

        [Header("B.8 SubBiomeResolver")]
        [Tooltip("이 sub-biome 영역 결정 noise 가중치. 높을수록 넓은 영역 차지.")]
        [Range(0f, 1f)] public float resolverWeight = 1f;

        [Tooltip("Resolver noise 주기. 큰 값=작고 촘촘한 영역, 작은 값=크고 부드러운 영역.")]
        [Range(0.01f, 0.5f)] public float resolverFrequency = 0.05f;

        [Range(0f, 100f)] public float resolverOffsetX = 0f;
        [Range(0f, 100f)] public float resolverOffsetZ = 0f;

        // ═══════════════════════════════════════
        // B.1 Triplanar + Stochastic + Macro
        // ═══════════════════════════════════════
        [Header("B.1 Triplanar")]
        [Range(1f, 16f)] public float triplanarHardness = 4f;
        public bool stochasticSampling = true;

        [Tooltip("Macro variation noise scale (m). 원거리 색/강도 변주.")]
        [Range(0.5f, 32f)] public float macroScale = 8f;

        [Range(0f, 0.5f)] public float macroInfluence = 0.15f;

        // ═══════════════════════════════════════
        // ★ L1 FLOOR LAYERS (수평면, altitude별)
        // ═══════════════════════════════════════
        [Header("★ L1 Floor Layers (수평면)")]
        [Tooltip("바닥 layers. 각 Layer가 자체 LF+HF material 세트. " +
                 "예 Dry Karst: [흙, 자갈, 돌, 이끼] — 각자 LF+HF. " +
                 "altitude로 vertical blend. 최소 1, 최대 4 권장.")]
        public List<LayerMaterialSet> floorLayers = new List<LayerMaterialSet>();

        // ═══════════════════════════════════════
        // ★ L2 WALL LAYERS (수직면, depth별)
        // ═══════════════════════════════════════
        [Header("★ L2 Wall Layers (수직면)")]
        [Tooltip("벽면 layers. 각 Layer가 자체 LF+HF material 세트. " +
                 "예 Dry Karst: [표층 석회암, 심층 백운암]. " +
                 "depth/altitude로 blend. 최소 1, 최대 3 권장.")]
        public List<LayerMaterialSet> wallLayers = new List<LayerMaterialSet>();

        [Tooltip("wallMask threshold (normalY 기준). 이보다 수직(|normalY|<threshold)이면 wall layer 활성.")]
        [Range(0f, 1f)] public float wallMaskThreshold = 0.5f;

        [Tooltip("wallMask transition 부드러움. 작을수록 sharp.")]
        [Range(0.05f, 0.5f)] public float wallMaskSoftness = 0.2f;

        // ═══════════════════════════════════════
        // B.2 POM (Parallax Occlusion Mapping)
        // ═══════════════════════════════════════
        [Header("B.2 POM")]
        [Tooltip("POM 활성화. 각 LayerMaterialSet의 heightMap 또는 MOHR.B 채널 소비.")]
        public bool enablePOM = false;

        [Tooltip("POM 최대 displacement (world unit). 큰 값은 렌더 왜곡 가능.")]
        [Range(0f, 0.1f)] public float pomHeight = 0.02f;

        [Tooltip("POM ray marching step 수. 많을수록 품질 ↑ 성능 ↓.")]
        [Range(4, 64)] public int pomSteps = 16;

        [Tooltip("POM 적용 대상.")]
        public PomTarget pomTarget = PomTarget.Both;

        // ═══════════════════════════════════════
        // B.3 Sediment Blend (Floor에만 적용)
        // ═══════════════════════════════════════
        [Header("B.3 Sediment Blend (Floor만)")]
        [Tooltip("수평 sediment stratification 활성화.")]
        public bool enableSedimentBlend = false;

        [Range(0f, 1f)] public float sedimentSharpness = 0.5f;

        [Tooltip("Sediment 층리 높이별 분포 커브.")]
        public AnimationCurve sedimentCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        // ═══════════════════════════════════════
        // B.7 Fog Modulation
        // ═══════════════════════════════════════
        [Header("B.7 Fog")]
        [Tooltip("true면 이 sub-biome 진입 시 fog 적용. FogManager가 smooth transition.")]
        public bool overrideFog = false;

        public Color fogColor = Color.gray;

        [Tooltip("Fog density 배율 (global fog density에 곱).")]
        [Range(0f, 0.1f)] public float fogDensityMultiplier = 1f;

        // ═══════════════════════════════════════
        // B.9 Ore Vein
        // ═══════════════════════════════════════
        [Header("B.9 Ore Vein")]
        [Tooltip("이 sub-biome 영역의 광물 생성 규칙 리스트.")]
        public List<OreVeinRule> oreRules = new List<OreVeinRule>();

        [Tooltip("전체 광맥 생성 빈도 배율.")]
        [Range(0f, 1f)] public float oreFrequency = 0.1f;

        // ═══════════════════════════════════════
        // B.10 Global Intensity
        // ═══════════════════════════════════════
        [Header("B.10 Intensity")]
        [Tooltip("GlobalIntensity 점수 가중치.")]
        [Range(0f, 2f)] public float intensityScoreWeight = 1f;

        // ═══════════════════════════════════════
        // Optional Override (Hub 값 덮어쓰기)
        //   -1 = Hub의 CaveBiomeData 값 유지
        //   >=0 = 이 값으로 덮어씀
        // ═══════════════════════════════════════
        [Header("Optional Override (−1 = Hub 값 유지)")]
        [Tooltip("-1이면 CaveBiomeData.bumpAmplitude 사용. 아니면 이 값으로 덮어씀.")]
        public float floorBumpOverride = -1f;

        [Tooltip("-1이면 CaveBiomeData.wetnessMultiplier 사용.")]
        public float wetnessOverride = -1f;

        [Tooltip("-1이면 CaveBiomeData.curvatureAmp 사용.")]
        public float curvatureAmpOverride = -1f;

        // ═══════════════════════════════════════
        // Editor Event (핫 리로드)
        // ═══════════════════════════════════════
        public static event Action OnSubBiomeModified;

        private void OnValidate()
        {
            // Playing 중 Inspector 편집 시 Dispatcher 갱신용
            if (Application.isPlaying)
            {
                OnSubBiomeModified?.Invoke();
            }
        }
    }
}
