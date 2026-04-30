using UnityEngine;
using System;
using System.Collections.Generic;

namespace CaveSystem
{
    // ═══════════════════════════════════════════════════════════════════════
    // [Gate 5 Phase E-α.3] Primitive 선택 enum
    //
    // CaveGeometryPrimitives.hlsl의 EvaluateTunnelByBiome / EvaluateRoomByBiome
    // 라우터와 1:1 매핑. enum 값 = shader int 분기 ID.
    //
    // 기본값 (Capsule=0, Sphere=0) 사용 시 기존 sdCapsule/sdSphere와 byte-identical.
    // ═══════════════════════════════════════════════════════════════════════

    public enum TunnelPrimitiveType
    {
        Capsule = 0,        // 기본 (기존 sdCapsule)
        HexPrism = 1,       // Columnar — 육각 단면
        Box = 2,            // Sedimentary — 사각 wide-low
        Slot = 3,           // Canyon — 좁고 높은 슬롯
        CapsuleBlocks = 4,  // Rocky — 블록 변주
    }

    public enum RoomPrimitiveType
    {
        Sphere = 0,         // 기본 (기존 구형)
        Pear = 1,           // Karst — 서양배
        HexColumnSpace = 2, // Columnar — 육각 공간
        DiscPlate = 3,      // Sedimentary — 납작 원판
        CathedralVault = 4, // Colonnade / Boss — 궁륭
    }
    /// <summary>
    /// 개별 지대(Biome)의 기하학적 형태와 특성을 정의하는 독립적인 데이터 에셋입니다.
    /// 에디터에서 수정을 감지하여 실시간 핫리로드(Hot-Reload)를 지원합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCaveBiome", menuName = "Cave System/Biome Data")]
    public class CaveBiomeData : ScriptableObject
    {
        [Header("Biome Identity")]
        public string biomeName = "New Biome";

        [Header("Geometry Parameters (형태 제어)")]
        [Tooltip("지질 Case ID. " +
                 "0: 석회암/카르스트 (Karst), " +
                 "1: 주상절리 (Columnar Basalt), " +
                 "2: 단층/퇴적암 (Faulted/Sedimentary), " +
                 "3: 신전풍 기둥 (Colonnade), " +
                 "4: 거친 블록 암반 (Rocky), " +
                 "5: 협곡 (Canyon), " +
                 "6: 해식동굴 (Marine/Sea Cave)")]
        [Range(0, 6)]
        public int noiseType = 0;

        [Tooltip("노이즈의 오밀조밀함. 값이 클수록 거칠고 좁게 파입니다.")]
        [Range(0.01f, 0.2f)] public float noiseFrequency = 0.05f;

        [Tooltip("Y축 압착률. 1.0 미만 시 지형이 위아래로 눌려 단층(Fault-line) 느낌을 줍니다.")]
        [Range(0.1f, 2.0f)] public float yCompression = 1.0f;

        [Tooltip("방과 통로가 융합되는 부드러움의 정도 (smin K값).")]
        [Range(0.1f, 10.0f)] public float sminStrength = 2.5f;

        [Tooltip("퇴적암 층리(Terrace) 단계. 0이면 사용하지 않으며, 높을수록 계단이 촘촘해집니다.")]
        [Range(0.0f, 10.0f)] public float terraceSteps = 0.0f;

        // ─────────────────────────────────────────────────────────
        // [Phase 4.5-G Stage 1-A] Per-biome Blend Damping
        //
        // Biome이 인접 biome과 blend될 때 detail noise amplitude 감쇠 비율.
        // 1.0 = 감쇠 없음 (sharp 특성 완벽 보존)
        // 0.5 = blend 중심에서 50% 감쇠 (smooth biome에 적합)
        //
        // 권장값:
        //   Karst       : 0.60   (가장 smooth, 강한 감쇠 자연)
        //   Columnar    : 0.85   (각진 hex 특성 보존)
        //   Sedimentary : 0.70   (층리 약간 흐림 허용)
        //   Canyon      : 0.90   (수직 slot 보존)
        //   Rocky       : 0.65   (낙반 완화, 플레이어 친화)
        //   Colonnade   : 1.00   (blend 최소화 - Stage 1-B에서 예외 처리)
        //   Marine      : 0.65   (파동 요철 부드럽게)
        //
        // Shader에서는 "양 biome 중 작은 값" 기준으로 적용 (보수적).
        // ─────────────────────────────────────────────────────────
        [Header("Blend Behavior (Phase 4.5-G)")]
        [Tooltip("Blend 중심에서 이 biome의 detail noise amplitude 비율. " +
                 "1.0 = 감쇠 없음 (sharp 특성 보존, Columnar/Canyon 등). " +
                 "0.6 = 40% 감쇠 (Karst/Marine 등 smooth).")]
        [Range(0.3f, 1.0f)] public float blendDamping = 0.7f;

        [Tooltip("노드 배치 가중치. 높을수록 이 biome에 노드가 더 많이 배치됨. " +
                 "1.5 = 평균보다 50% 많음 (Karst). " +
                 "0.5 = 평균의 50% (Colonnade, 대홀 자체가 1개 방). " +
                 "0.6 = 60% (Canyon, 좁은 공간).")]
        [Range(0.2f, 3.0f)] public float nodeDensityWeight = 1.0f;

        [Header("Floor Bumpiness (바닥 요철)")]
        [Tooltip("바닥 요철의 깊이 및 높이 진폭입니다.")]
        [Range(0.0f, 5.0f)] public float bumpAmplitude = 2.0f;

        [Tooltip("바닥 요철의 넓이 빈도입니다.")]
        [Range(0.01f, 0.2f)] public float bumpFrequency = 0.05f;

        [Header("Visual & Ecosystem")]
        public Material terrainMaterial;
        [Range(0f, 1f)] public float wetnessMultiplier = 0.0f;

        // ─────────────────────────────────────────────────────────
        // [Gate 5 Phase E-β.3.5] Path Organicity (Edge Curvature)
        //
        // 이 바이옴에 속한 edge가 생성될 때 부여되는 curvature amplitude (m).
        //   0 = 직선 (sdCapsule, 기본) — 규칙 #6 byte-identical
        //   > 0 = EvaluateCurvedTunnel capsule chain (자연 곡선 통로)
        //
        // 지질학적 권장값:
        //   0.0 (Colonnade 신전풍 — 대칭 직선)
        //   0.3 (Columnar 주상절리 — 수직 기둥 사이 직선성 강)
        //   1.0 (Sedimentary 퇴적암 — 수평 우세이되 약간 meandering)
        //   2.0 (Karst 카르스트 — 불규칙 용식 침식)
        //   3.0 (Canyon 협곡 — 하천 meandering 최대)
        //
        // 플레이어빌리티 가드 (HLSL 내부):
        //   - offset amp ≤ edge.width × 0.5 하드 clamp → 통로 차단 불가
        //   - endTaper=0 양끝 → 인접 edge 접속부 정확히 일치 → 연결 에러 없음
        //   - SDF 평가 시 smin(k=1.5)로 C¹ 연속 조인트
        //
        // 다중 biome 월드에서의 경계 처리:
        //   - edge는 global NodeGraph에서 midpoint biome의 amp 부여
        //   - 인접 edge가 서로 다른 biome이어도 endTaper=0으로 위치 일치 보장
        //   - 시각적 곡률 형태 차이는 발생 가능 (이는 지질 차이의 자연 표현)
        // ─────────────────────────────────────────────────────────
        [Header("Path Organicity (E-β Edge Curvature)")]
        [Tooltip("이 바이옴에 속한 통로의 곡률 강도 (m). " +
                 "0 = 직선. 권장: Karst 2, Columnar 0.3, Sedimentary 1, Colonnade 0, Rocky 1.5, Canyon 3, Marine 1. " +
                 "CaveTerrainConfig.enableCurvedTunnels = true 및 " +
                 "curvatureMultiplier > 0 조건에서 활성.")]
        [Range(0f, 3f)]
        public float curvatureAmp = 0f;

        // ═══════════════════════════════════════════════════════════════════
        // [Gate 5 Phase E-α.3] Primitive Selection — Per-Biome Geometry Identity
        //
        // 통로/방 형태를 biome별 지질 특성에 맞게 선택.
        // 기본값 (Capsule/Sphere) 사용 시 기존 동작과 byte-identical.
        //
        // 권장 매핑 (noiseType → primitive):
        //   0 Karst       → Capsule (tunnel) / Pear (room)
        //   1 Columnar    → HexPrism (tunnel) / HexColumnSpace (room)
        //   2 Sedimentary → Box (tunnel) / DiscPlate (room)
        //   3 Colonnade   → Capsule (tunnel) / CathedralVault (room)
        //   4 Rocky       → CapsuleBlocks (tunnel) / Sphere (room)
        //   5 Canyon      → Slot (tunnel) / Sphere (room)
        //   6 Marine      → Capsule (tunnel) / Sphere (room)
        //
        // 활성화:
        //   CaveTerrainConfig.enablePrimitiveRouting 전역 토글 + 이 필드.
        //   routing OFF 또는 enum 0 → 기존 Capsule/Sphere 경로 (byte-id).
        // ═══════════════════════════════════════════════════════════════════
        [Header("E-α Primitive Selection")]
        [Tooltip("통로 프리미티브 선택. Capsule(기본)=기존 sdCapsule. " +
                 "HexPrism=Columnar 주상절리, Box=Sedimentary 수평 통로, " +
                 "Slot=Canyon 협곡, CapsuleBlocks=Rocky 거친 블록. " +
                 "CaveTerrainConfig.enablePrimitiveRouting=true 조건에서 활성.")]
        public TunnelPrimitiveType tunnelPrimitive = TunnelPrimitiveType.Capsule;

        [Tooltip("방 프리미티브 선택. Sphere(기본)=기존 구형. " +
                 "Pear=Karst 용식방, HexColumnSpace=Columnar, DiscPlate=Sedimentary 납작 방, " +
                 "CathedralVault=Colonnade/Boss 궁륭. " +
                 "CaveTerrainConfig.enablePrimitiveRouting=true 조건에서 활성.")]
        public RoomPrimitiveType roomPrimitive = RoomPrimitiveType.Sphere;

        // ═══════════════════════════════════════════════════════════════════
        // [Gate 5 Phase E-α.5] Edge Constraint — 방향/경사 편향
        //
        // Case별 edge 생성 규칙:
        //   null = 기존 등방성 경로 (byte-identical)
        //   non-null + directionStrength > 0 = 방향 편향 적용
        //
        // 권장 매핑 (noiseType → EdgeConstraint 특성):
        //   0 Karst:       수평 약선호 (directionStrength=0.3, preferredDir=(1,0,1))
        //   1 Columnar:    수직 허용 (allowVerticalEdges=true)
        //   2 Sedimentary: 수평 강선호 (directionStrength=0.8, preferredDir=(1,0,0))
        //   3 Colonnade:   중립 (directionStrength=0)
        //   4 Rocky:       중립 (불규칙)
        //   5 Canyon:      수직 허용 + 수평 (directionStrength=0.4, allowVert=true)
        //   6 Marine:      수평 강선호 (directionStrength=0.7, preferredDir=(1,0,1))
        // ═══════════════════════════════════════════════════════════════════
        [Header("E-α.5 Edge Constraint")]
        [Tooltip("Edge 생성 제약 SO. null이면 등방성 (기존 경로, byte-identical). " +
                 "할당 시 방향 선호/경사 허용/길이 제약 적용.")]
        public EdgeConstraintSO edgeConstraint;

        // ═══════════════════════════════════════════════════════════════════
        // [Gate 5 Phase E-β.7] Per-Biome amp matrix
        //   E-β.4 Floor variation, E-β.5 Ceil variation, E-β.6 Max slope
        //   biome별로 지질학적 특성 반영. CaveTerrainConfig 전역 toggle × 이 값.
        //
        // 권장 Case별 amp (§3.5 보고서):
        //   ┌────────────┬──────┬──────┬─────────┐
        //   │ Case       │floor │ ceil │ maxSlope│
        //   ├────────────┼──────┼──────┼─────────┤
        //   │ Karst      │ 1.5  │  4   │  30°    │
        //   │ Columnar   │ 0.5  │  1   │  80°    │
        //   │ Sedimentary│ 0.3  │  1   │  15°    │
        //   │ Colonnade  │ 0.0  │ 0.5  │   5°    │
        //   │ Rocky      │ 1.5  │  3   │  45°    │
        //   │ Canyon     │ 0.5  │  5   │  10°    │
        //   └────────────┴──────┴──────┴─────────┘
        // ═══════════════════════════════════════════════════════════════════
        [Header("Path Organicity (E-β Floor/Ceil/Slope)")]
        [Tooltip("Floor Y 변이 진폭 (m). 0=평탄. 플레이어 jump 한계 1.5m로 내부 clamp. " +
                 "권장: Karst 1.5, Columnar 0.5, Sedimentary 0.3, Colonnade 0, Rocky 1.5, Canyon 0.5, Marine 0.3.")]
        [Range(0f, 1.5f)]
        public float floorVarAmp = 0f;

        [Tooltip("Ceiling Y 변이 진폭 (m, 음의 방향만). 0=평탄. " +
                 "권장: Karst 4, Columnar 1, Sedimentary 1, Colonnade 0.5, Rocky 3, Canyon 5, Marine 2.")]
        [Range(0f, 5f)]
        public float ceilVarAmp = 0f;

        [Tooltip("허용 edge 경사 각도 (°). NavMesh bake 한계 ≤ 45°. " +
                 "권장: Karst 30, Columnar 80, Sedimentary 15, Colonnade 5, Rocky 45, Canyon 10, Marine 20. " +
                 "E-β.6 검증 단계 — 현재 경고 로그만, 실제 rejection은 향후 단계.")]
        [Range(5f, 90f)]
        public float maxSlope = 45f;

        // ═══════════════════════════════════════════════════════════════════
        // [Gate 5 Phase A.10] Case 6 Marine 전용 파라미터
        // ═══════════════════════════════════════════════════════════════════
        [Header("Case 6 Marine (해식동굴 전용)")]
        [Tooltip("조수선 높이 (world unit Y). Case 6 L1 notch가 이 Y 근처 ±1.5m 수평 파임. " +
                 "기본 0 = 월드 원점 높이. DepthLayer에 따라 조정 필요.")]
        public float tideLineY = 0f;

        // ═══════════════════════════════════════════════════════════════════
        // [Gate 5 Phase B.0] SubBiome Profiles (v3 Hub + SubBiomeProfile 2-Tier)
        //
        // [규칙 #28] subBiomes — toggle 역할 (list empty/non-empty)
        //   (1) 목적: 이 biome 내부의 세부 변주 (예: 카르스트 → [Dry, Wet, Flooded]).
        //             각 SubBiomeProfileSO는 자체 surface rendering + detail + environment 완비.
        //   (2) 기본값 빈 리스트 이유: 규칙 #6 — empty 시 후속 로직 전부 skip.
        //             Dispatcher/shader는 기존 경로 그대로 → byte-identical 보장.
        //   (3) 활성화 전제: 리스트에 SubBiomeProfileSO asset 1개 이상 할당.
        //             Phase 5 (B.1~B.15)에서 Texture2DArray packing + SubBiomeResolver + shader 소비.
        //   (4) 영향: 현재 B.0 단계에서는 "골격만". 할당해도 shader 영향 없음.
        //             Phase 5 이후: 할당된 sub 중 SubBiomeResolver가 월드좌표 기반 index 결정,
        //             해당 SubBiome의 floorLayers/wallLayers/fog/ore가 소비됨.
        //             paramHash 영향: B.0에서는 empty일 때 기여 0 (byte-identical). 
        //             non-empty 시 B.1 이후 hash 포함.
        // ═══════════════════════════════════════════════════════════════════
        [Header("[B.0] SubBiome Profiles (선택)")]
        [Tooltip("이 biome의 하위 세부 변주. 비어있으면 CaveBiomeData 기본값만 사용 (현재 동작). " +
                 "리스트가 있으면 SubBiomeResolver가 월드 좌표 기반 index 결정 (Phase 5에서 소비). " +
                 "예) 카르스트.subBiomes = [DryKarst, WetKarst, FloodedKarst]. " +
                 "B.0 시점: 할당해도 shader 영향 없음 (골격 단계).")]
        public List<SubBiomeProfileSO> subBiomes = new List<SubBiomeProfileSO>();

        // 에디터 변경 감지 이벤트 (CaveComputeDispatcher가 구독)
        public static event Action OnBiomeModified;

        private void OnValidate()
        {
            // 플레이 모드 중에 수치를 변경하면 즉시 이벤트를 발생시켜 GPU 버퍼를 갱신합니다.
            if (Application.isPlaying)
            {
                OnBiomeModified?.Invoke();
            }
        }

        /// <summary>
        /// 참조 객체(Material 등)를 제외하고 GPU로 전송할 32바이트 정렬 순수 데이터만 패킹합니다.
        /// [Phase 4.5-G Stage 1-A] padding 슬롯을 blendDamping 으로 재활용.
        /// </summary>
        public BiomeParamData GetStructData()
        {
            return new BiomeParamData
            {
                noiseFrequency = this.noiseFrequency,
                yCompression = this.yCompression,
                sminStrength = this.sminStrength,
                terraceSteps = this.terraceSteps,
                bumpAmplitude = this.bumpAmplitude,
                bumpFrequency = this.bumpFrequency,
                noiseType = this.noiseType,
                blendDamping = this.blendDamping,   // 이전 padding 슬롯 재활용
            };
        }
    }
}