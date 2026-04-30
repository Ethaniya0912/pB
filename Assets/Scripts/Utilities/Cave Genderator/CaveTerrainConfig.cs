// =============================================================================
// CaveTerrainConfig.cs  |  pB-4 Project
// =============================================================================
// VoxelSize, ChunkSize, PointsPerAxis 등 지형 해상도 파라미터를
// 프로젝트 전체가 공유하는 단일 ScriptableObject.
//
// 사용법:
//   [SerializeField] CaveTerrainConfig terrainConfig;
//   int N = terrainConfig.DCPointsPerAxis;
//   float ws = terrainConfig.ChunkWorldSize;
//
// CaveChunkManager, DCPipelineExtension, CaveMeshJobManager,
// TerrainCacheManager 모두 이 SO를 참조.
// =============================================================================
using UnityEngine;

namespace CaveSystem
{
    [CreateAssetMenu(menuName = "CaveSystem/Terrain Config", fileName = "CaveTerrainConfig")]
    public class CaveTerrainConfig : ScriptableObject
    {
        // ── 핵심 파라미터 ──────────────────────────────────────────────────
        [Header("World Size")]
        [Tooltip("청크 하나의 목표 월드 크기 (m). VoxelSize에 따라 ChunkSize가 자동 계산됨.")]
        [Min(4f)] public float targetWorldChunkSize = 16f;

        [Tooltip("복셀 하나의 월드 크기 (m). 작을수록 디테일 향상, 메모리·연산 비용 증가.")]
        [Min(0.1f)] public float voxelSize = 0.5f;

        // ── 자동 계산 프로퍼티 ─────────────────────────────────────────────
        /// <summary>복셀 그리드 크기 = targetWorldChunkSize / voxelSize (정수 반올림)</summary>
        public int ChunkSize => Mathf.Max(4, Mathf.RoundToInt(targetWorldChunkSize / voxelSize));

        /// <summary>실제 청크 월드 크기 = ChunkSize * voxelSize</summary>
        public float ChunkWorldSize => ChunkSize * voxelSize;

        /// <summary>MC 포인트 수 = ChunkSize + 2 (Ghost Voxel 패딩)</summary>
        public int MCPointsPerAxis => ChunkSize + 2;

        // ─────────────────────────────────────────────────────────────
        // [Phase 3-B — DC Overlap Removal + Cross-Chunk QEF] 
        //
        // 문제 (FragC-α 근본 원인 + DC cell-local QEF 한계):
        //   현재 DCPointsPerAxis = ChunkSize + 3 (양쪽 +1.5 padding, 실질 2 voxel overlap)
        //   → 인접 chunk가 같은 voxel 영역에서 각자 mesh를 생성
        //   → overlap 영역에 mesh 중복 → 파편 시각적 현상 + VRAM 낭비 + GPU 낭비
        //   추가로 DC cell-local QEF: 각 cell QEF는 자기 cell 내부 edges만 사용 → 
        //   인접 chunk의 마주보는 cell과 다른 해 (7~15% 오차) → seam crack/overlap
        //
        // 해결 (Schaefer 2005 + Ghost Edge Cache):
        //   DCPointsPerAxis = ChunkSize + 1 (overlap 제거)
        //   경계 cell의 QEF는 neighbor chunk의 Hermite edges를 ghost input으로 누적
        //   → 양쪽 chunk가 대칭 input → 같은 QEF chain → 같은 vertex (bit-identical)
        //   → overlap 근본 제거 + seam 완벽 일치 (규칙 #22)
        //
        // 규칙 #6 (Toggle OFF bit-identical):
        //   enablePhase3OverlapRemoval=false 시 ChunkSize+3 유지 + AccumulateGhostEdges 미호출
        //   → 기존 동작 완전 동일
        //
        // 규칙 #15 (FORMAT_VERSION):
        //   토글 ON 시 mesh 결과 변경 → CaveDiskCache FORMAT_VERSION 18→19 승격
        //
        // 규칙 #20 (DC 아키텍처 연쇄 효과):
        //   이 토글 영향:
        //     ✓ DCPointsPerAxis: ChunkSize+3 → ChunkSize+1
        //     ✓ bakedBasePos shift: -voxelSize → 0
        //     ✓ Boundary cell QEF는 ghost edges 포함하여 수렴
        //     ✓ paramHash에 토글 포함 (규칙 #10)
        //     ✓ Mass Point QEF (Phase 2)는 boundary ghost edges 포함해야 정확한 c_m
        //
        // ═══════════════════════════════════════════════════════════════
        // [규칙 #28 — TOGGLE JUSTIFICATION]
        // ═══════════════════════════════════════════════════════════════
        //
        // (1) 기본값 이유 (왜 false 기본):
        //     ■ 2026-04-22 이전: CPU 측 facesEdges 수집 경로가 끊겨 있었음
        //       → 토글 ON 시 BindPhase3GhostBuffers의 totalEdges=0 분기로 빠져 OFF처럼 작동
        //       → 즉 "작동 안 하는 토글"이었음
        //     ■ 2026-04-22 이후 (이 세션): DCPipelineExtension에 
        //       ExtractBoundaryHermiteEdges kernel dispatch + async readback +
        //       ChunkGhostData.facesEdges 채움 경로를 연결하여 "실제로 작동"하는 상태로 전환
        //     ■ 기본값은 여전히 false — 사용자가 신규 동작을 검증한 후 on-demand로 전환
        //
        // (2) 활성화 전제 (무엇이 되어야 true로 켤 수 있는가):
        //     ■ DCPipelineExtension.DispatchExtractBoundaryHermiteEdges 메서드 존재 (이번 세션 완료)
        //     ■ CaveDualContouring.compute에 ExtractBoundaryHermiteEdges kernel 존재 (이번 세션 완료)
        //     ■ ChunkGhostDataManager.StoreChunkGhostData + QueryNeighbors 정상 작동 (기존 완료)
        //     ■ CaveDiskCache FORMAT_VERSION 승급 (ON 전환 시 캐시 자동 무효화)
        //
        // (3) 활성화 후 영향 (다른 토글/기능에 미치는 변화):
        //     ■ enableVertexPositionMirror → OFF 권장으로 전환
        //       Mirror는 band-aid였음 (규칙 #27). Phase 3-B 활성 시 수학적으로 불필요.
        //       Mirror 유지 시 seam vertex를 이중으로 건드려 오히려 noise 유발 가능.
        //     ■ enableHaloAwareBake → 정상 작동
        //       Mirror 의존 주석이 있으나 Phase 3-B가 vertex position seamless 보장하므로
        //       Halo Bake의 neighbor vertex 기여 계산이 더 정확해짐.
        //     ■ outOfSnap % 로깅 → 참고용으로만 사용 (수학적 의미 감소)
        //     ■ vertexMirrorSnapMultiplier → 무의미해짐 (Mirror OFF 권장이므로)
        //
        // 관련 파일:
        //   CaveTerrainConfig.cs       — 토글 + DCPointsPerAxis 분기 (본 필드)
        //   CaveComputeDispatcher.cs   — bakedBasePos shift 분기
        //   ChunkGhostDataManager.cs   — ChunkGhostData.facesEdges[6] 저장소
        //   DCPipelineExtension.cs     — [신규] Extract kernel dispatch + readback + Populate
        //   CaveDualContouring.compute — [신규] ExtractBoundaryHermiteEdges kernel + AccumulateGhostEdges
        //   CaveDiskCache.cs           — FORMAT_VERSION 18→19 + paramHash에 토글 포함
        // ─────────────────────────────────────────────────────────────
        [Header("Phase 3-B — DC Overlap Removal + Cross-Chunk QEF")]
        [Tooltip("ON: DCPointsPerAxis=ChunkSize+1 + Cross-Chunk QEF (neighbor Hermite ghost edges 사용). " +
                 "수학적으로 seam 완벽 일치 → Mirror 불필요. VRAM -15~20%, outOfSnap 0~0.5%. " +
                 "ON 전환 시 enableVertexPositionMirror=false 권장. " +
                 "OFF(기본): DCPointsPerAxis=ChunkSize+3, cell-local QEF (기존 동작, bit-identical).")]
        public bool enablePhase3OverlapRemoval = false;

        // ═════════════════════════════════════════════════════════════
        // [Gate 2 / B-α] GenerateQuads Boundary Ownership
        //   설명: Phase3 OverlapRemoval이 실제로는 overlap 제거를 하지 않음 —
        //         PointsPerAxis 축소 + Ghost QEF Binding만 수행.
        //         이 토글이 true이면 GenerateQuads kernel에서 sid[axis]=0 인
        //         cell의 axis edge quad 생성을 skip → 진정한 mesh 중복 제거.
        //
        //   의존성: Phase3 ON 상태에서만 의미 있음 (OFF시에도 작동하지만 효과 미미)
        //   규칙 #6: false 시 기존 동작 bit-identical
        //   효과: Image 3/4 스타일 mesh overlap + 지그재그 근본 제거
        // ═════════════════════════════════════════════════════════════
        [Tooltip("[Gate 2] GenerateQuads에서 overlap cell의 quad 생성 skip. " +
                 "ON: 자기 담당 edge만 생성 → mesh 중복 제거. " +
                 "OFF: 기존 동작 (sid[axis]=0 cell도 생성 — overlap 발생).")]
        public bool enableBoundaryOwnership = false;

        // ═════════════════════════════════════════════════════════════
        // [Gate 4-A Phase 2/3] Vertex Position Mirror
        //   목적: 경계 vertex가 양 chunk에서 bit-identical 보장 →
        //         crack / 뚫고 나오는 현상 근본 해결.
        //   Phase 2 (저장): ON 시 readback 완료 후 boundary vertex를 
        //                   ChunkGhostData.facesVerticesWorld에 저장 (이 세션 구현)
        //   Phase 3 (복원): Finalize 전 neighbor Ghost Cache에서 vertex 조회 →
        //                   가장 가까운 neighbor vertex로 덮어쓰기 (다음 세션)
        //   의존성: Phase3 Ghost Cache 활성 (enableGhostDensityBaking=true 권장)
        //   규칙 #6: OFF 시 저장/복원 모두 skip → bit-identical
        //
        // ═══════════════════════════════════════════════════════════════
        // [BAND-AID 2026 — 규칙 #27] Vertex Mirror는 일시적 우회책
        // ═══════════════════════════════════════════════════════════════
        //   증상: DC chunk 경계에서 mesh overlap 또는 hole
        //   원인 (판명됨): DC cell-local QEF의 수학적 한계
        //                  양쪽 cell이 각자 내부 edges만으로 QEF → 7~15% 서로 다른 해
        //   Mirror 방식의 한계:
        //     vertex position만 이동 + quad topology는 자기 것 유지
        //     → snapMult 스펙트럼의 어느 지점에서도 overlap ↔ hole 사이에서 선택
        //     → "완벽한 지점"은 수학적으로 존재하지 않음
        //   근본 해결책: enablePhase3OverlapRemoval (Cross-Chunk QEF, 규칙 #22)
        //              양쪽 chunk가 대칭 ghost input → 같은 QEF chain → seam 완벽 일치
        //   제거 조건: enablePhase3OverlapRemoval=true 검증 완료 후:
        //              1) outOfSnap 0~0.5% 확인
        //              2) 시각적 seam 완벽 확인
        //              3) 이 토글을 false로 고정 + [DEPRECATED] 태그 추가
        //              4) MirrorFromNeighbors 호출 경로 제거
        //   도입 기록:
        //     - 2026-04-15경 Gate 4-A Phase 2로 시작
        //     - vertexMirrorSnapMultiplier 0.5→1.0→1.2→1.5 튜닝 여러 세션 (낭비)
        //     - 2026-04-22 Phase 3-B가 이미 구현되어 있었음이 발견됨
        //     - Phase 3-B 완성 (끊긴 facesEdges 수집 경로 연결) 후 Mirror는 제거 대상
        // ═══════════════════════════════════════════════════════════════
        [Tooltip("[BAND-AID] Phase 3-B 활성 전 seam 보정용 임시책. " +
                 "근본 해결은 enablePhase3OverlapRemoval=true. " +
                 "Phase 3-B 검증 후 이 토글은 false 고정/제거 대상.")]
        public bool enableVertexPositionMirror = false;

        [Tooltip("[BAND-AID] Mirror 시 인접 chunk의 vertex와 매칭할 거리 허용치 (voxelSize 배수). " +
                 "어떤 값으로도 완벽한 seam 불가능 (DC 수학 한계). " +
                 "Phase 3-B=true 시 이 파라미터는 무의미.")]
        [Range(0.3f, 3.0f)] public float vertexMirrorSnapMultiplier = 1.5f;

        // ═════════════════════════════════════════════════════════════
        // [Gate 4-C] Halo-aware Normal Map Baking
        //   목적: 경계 texel에 neighbor chunk의 vertex 기여를 추가 →
        //         truly seamless texture (G3 texel stitch는 43% 커버, 여기는 100%)
        //   의존성: enableVertexPositionMirror=true (Ghost Cache에 vertex+normal 저장됨)
        //   규칙 #6: OFF 시 V5 Job 결과 bit-identical, Halo Pass 미실행
        //   성능: chunk당 +10~50ms (async bake path)
        // ═════════════════════════════════════════════════════════════
        [Tooltip("[Gate 4-C] V5 Bake 완료 후 neighbor boundary vertex를 accum에 추가 기여. " +
                 "OFF: 기존 V5만 (경계 texel은 chunk별 독립). " +
                 "ON: 경계 texel에 neighbor 기여 → seamless texture. " +
                 "의존성: enableVertexPositionMirror=true 필수.")]
        public bool enableHaloAwareBake = false;

        // ─────────────────────────────────────────────────────────────
        // [F-4 / FORMAT_VERSION 13] Ghost Density Buffer
        //
        // 문제 (Image 4 stripe):
        //   NormalBaker의 SampleDensity에서 chunk boundary clamp가 비대칭 반응
        //   → 인접 chunk 사이 gradient 불일치 → Debug View=Normals에서 세로 stripe
        //   → Image 1/2/3의 chunk 경계 texture flip + lighting 불연속
        //
        // 해결 (업계 표준 Halo Exchange 패턴):
        //   ChunkGhostDataManager에 각 chunk의 density cache를 ref-only 등록.
        //   NormalBaker Job이 SampleDensityGhostAware()로 out-of-range 시
        //   neighbor density 조회 → gradient 수학적 seamless 보장.
        //
        // 비용:
        //   VRAM 0, Streaming overhead ~3.2ms/chunk (KPI 15ms의 21%),
        //   Managed heap 실질 +0 (shared reference), Job 복사 ~15-20MB 일시.
        //
        // 규칙 #6 (Toggle OFF bit-identical):
        //   enableGhostDensityBaking=false 시 SampleDensity가 기존 clamp 경로 유지
        //   → 기존 동작과 byte-identical.
        //
        // 규칙 #15 (FORMAT_VERSION):
        //   토글 ON 시 normal texture 결과 변경 → CaveDiskCache FV 12→13 승급.
        //   (mesh position/topology는 무변 → paramHash 불필요, 규칙 #10 무관)
        //
        // 관련 파일:
        //   CaveTerrainConfig.cs       — 토글 owner (본 필드)
        //   ChunkGhostDataManager.cs   — DensitySnapshot + 3 API
        //   CaveComputeDispatcher.cs   — density readback 시점 RegisterDensity
        //   CaveNormalBaker.cs         — SampleDensityGhostAware Burst Job 확장
        //   DCMeshBuilder.cs           — PerVertexSubVoxelJob 동일 확장
        //   CaveDiskCache.cs           — FORMAT_VERSION 12→13
        // ─────────────────────────────────────────────────────────────
        [Header("F-4 — Ghost Density Buffer (Halo Exchange)")]
        [Tooltip("ON: NormalBaker의 SampleDensity가 chunk boundary 넘어서 인접 chunk density 조회. " +
                 "Image 4 stripe 근본 해결. Streaming 약 +3.2ms/chunk. " +
                 "OFF(기본): clamp 경로 (기존 동작, byte-identical).")]
        public bool enableGhostDensityBaking = false;

        // ═══════════════════════════════════════════════════════════════════
        // [Gate 5 Phase E-β — Path Organicity / Curvature]
        //
        // [규칙 #28] enableCurvedTunnels
        //   (1) 목적: edge 통로를 sdCapsule 단일 직선이 아닌 5-point
        //             제어점 chain + smin으로 곡선화 → "일직선 반복" 해소
        //   (2) 기본값 OFF 이유: 규칙 #6 — OFF 시 fast-path에서 sdCapsule 직접 호출
        //             → byte-identical 보장. 명시 토글로 안전성 이중 보장.
        //   (3) 활성화 전제: curvatureAmplitude > 0.01 및 biome.curvatureAmp > 0
        //             (전역 또는 biome 중 하나라도 0이면 직선 fallback)
        //   (4) 영향: 모든 edge의 dNormal 계산이 EvaluateCurvedTunnel로 전환.
        //             sediment/canyon 부속 로직은 직선 edge 기준 유지 (프로토타입).
        //             SDF 비용 sdCapsule 1회 → 4회 + 3 smin ≈ 4~5× 증가.
        //             규칙 #10 — paramHash 포함 (biome.curvatureAmp 포함).
        //             규칙 #15 — FORMAT_VERSION 22 → 23 승급.
        //
        // 플레이어빌리티 가드 (HLSL 내부 적용):
        //   - curvature offset amp ≤ edge.width × 0.5 (통로 차단 방지)
        //   - 양 끝 endTaper=0 (chunk boundary 접속부 직선 유지, 규칙 #22)
        //   - 짧은 edge (< 2m) 직선 fallback
        //
        // [규칙 #28] curvatureAmplitude (E-β.3.5부터 "multiplier"로 의미 전환)
        //   (1) 목적: 전역 curvature 계수. biome.curvatureAmp의 multiplier로 작동.
        //             effectiveAmp = biome.curvatureAmp × curvatureAmplitude
        //   (2) 기본값 1.0 (신규): biome 값 그대로 사용 의미.
        //             기존 0.0 (deprecated): 모든 biome을 0으로 덮어씌움 → OFF 동일 효과.
        //   (3) 활성화 전제: enableCurvedTunnels=true 및 biome.curvatureAmp > 0
        //   (4) 영향: 전역 튜닝용. 0.5 = 모든 biome amp 절반, 2.0 = 2배 증폭.
        //             0으로 설정 시 biome 설정 무시하고 전역 OFF와 동일.
        //             per-biome 제어는 CaveBiomeData.curvatureAmp Inspector.
        // ═══════════════════════════════════════════════════════════════════
        [Header("E-β — Path Organicity (Curvature)")]
        [Tooltip("ON: 통로를 제어점 chain으로 곡선화 (5-point, smin). " +
                 "OFF(기본): sdCapsule 직선 (byte-identical). " +
                 "활성 시 curvatureAmplitude > 0 및 biome.curvatureAmp > 0 필요.")]
        public bool enableCurvedTunnels = false;

        [Tooltip("전역 curvature multiplier. " +
                 "effectiveAmp = biome.curvatureAmp × 이 값. " +
                 "1.0 = biome 값 그대로, 0.5 = 절반, 2.0 = 2배. " +
                 "per-biome amp는 CaveBiomeData.curvatureAmp Inspector에서 조정.")]
        [Range(0f, 3f)]
        public float curvatureAmplitude = 1.0f;  // [E-β.3.5] 의미 변경 — multiplier

        // ═══════════════════════════════════════════════════════════════════
        // [Gate 5 Phase E-β.4/.5] Floor/Ceil Y Variation 전역 토글
        //
        // [규칙 #28] enableFloorVariation
        //   (1) 목적: 전역 floor Y 기복 (worldPos.xz noise 기반)
        //   (2) 기본값 OFF: 규칙 #6 — 내부 fast-path에서 floorVar=0 → byte-identical
        //   (3) 활성화 전제: floorVariationMultiplier > 0.01 및 biome.floorVarAmp > 0
        //   (4) 영향: shader floorVar 계산. Hard bound [-0.5m, +1.5m] 내부 적용.
        //             규칙 #10: paramHash 포함 (effectiveFloorVar).
        //             규칙 #15: FV 23 → 24 (E-β.9 일괄 승급 예정).
        //
        // [규칙 #28] enableCeilVariation
        //   (1) 목적: 전역 ceiling Y 변이 (음의 방향)
        //   (2) 기본값 OFF: 규칙 #6 byte-identical
        //   (3) 활성화 전제: ceilVariationMultiplier > 0.01 및 biome.ceilVarAmp > 0
        //   (4) 영향: shader ceilVar 계산. 음의 방향만 변조 (위로 솟지 않음).
        // ═══════════════════════════════════════════════════════════════════
        [Header("E-β — Floor/Ceil Variation")]
        [Tooltip("ON: 바닥 Y가 worldPos.xz noise 기반으로 기복 (±biome.floorVarAmp m). " +
                 "OFF(기본): 평탄 (byte-identical). " +
                 "Hard bound: [-0.5m, +1.5m] — jump 한계 내부 clamp.")]
        public bool enableFloorVariation = false;

        [Tooltip("Floor variation 전역 multiplier. " +
                 "effective = biome.floorVarAmp × 이 값. 1.0 = biome 값 그대로.")]
        [Range(0f, 2f)]
        public float floorVariationMultiplier = 1.0f;

        [Tooltip("ON: 천장 Y가 noise 기반 변이 (음의 방향만, 하락만). " +
                 "OFF(기본): 평탄 (byte-identical).")]
        public bool enableCeilVariation = false;

        [Tooltip("Ceiling variation 전역 multiplier. " +
                 "effective = biome.ceilVarAmp × 이 값.")]
        [Range(0f, 2f)]
        public float ceilVariationMultiplier = 1.0f;

        // ═══════════════════════════════════════════════════════════════════
        // [Gate 5 Phase A.4] DomainWarp XYZ + 재귀
        //
        // [규칙 #28] enableWarpY + warpYScale
        //   (1) 목적: Y축 warp 추가 (기존 XZ만). Y 방향 자연 변형.
        //   (2) 기본값 OFF: 규칙 #6 — XZ만 warp 기존 동작 유지.
        //   (3) 활성화 전제: warpYScale > 0.01.
        //   (4) 영향: shader에서 worldPos.y에도 warpY 적용.
        //             warpYScale 0.3 권장 (XZ warp의 30% 강도) — 바닥 안정성 보호.
        //
        // [규칙 #28] enableWarpRecursive
        //   (1) 목적: 1차 warp된 좌표를 다시 warp → fractal displacement.
        //   (2) 기본값 OFF: 규칙 #6.
        //   (3) 활성화 전제: _WarpAmplitude > 0.01 (1차 warp 있어야 의미).
        //   (4) 영향: 재귀 2차 warp amp = 1차의 40%. XZ만 (안전).
        // ═══════════════════════════════════════════════════════════════════
        [Header("A.4 — DomainWarp XYZ")]
        [Tooltip("ON: Y축 warp 추가 (기존 XZ + Y 3축). " +
                 "OFF(기본): XZ만 (byte-identical). " +
                 "warpYScale > 0.01 필요.")]
        public bool enableWarpY = false;

        [Tooltip("Y warp scale (XZ warp amp 대비 비율). 0.3 권장 (30% 강도, 바닥 안정).")]
        [Range(0f, 1f)]
        public float warpYScale = 0.3f;

        [Tooltip("ON: 1차 warp 좌표를 다시 warp (fractal). 40% 강도 XZ만. " +
                 "OFF(기본): 1단 warp만 (byte-identical).")]
        public bool enableWarpRecursive = false;

        // ═══════════════════════════════════════════════════════════════════
        // [Gate 5 Phase A.8] Stalactite 활성화
        //
        // [규칙 #28] enableStalactite
        //   (1) 목적: Case 0 (Karst) 천장에 cell-based 종유석.
        //   (2) 기본값 OFF: 규칙 #6 byte-identical.
        //   (3) 활성화 전제: 동굴에 Case 0 영역 존재.
        //   (4) 영향: 3m cell grid, 40% 확률, 길이 0.5~2m, 플레이어빌리티 보호 내부 clamp.
        // ═══════════════════════════════════════════════════════════════════
        [Header("A.8 — Stalactite (Case 0 Karst)")]
        [Tooltip("ON: Case 0 천장에 종유석. " +
                 "OFF(기본): 없음 (byte-identical). " +
                 "3m cell grid 기반, 길이 0.5~2m.")]
        public bool enableStalactite = false;

        // ═══════════════════════════════════════════════════════════════════
        // [Gate 5 Phase E-α.4] Primitive Routing 전역 토글
        //
        // [규칙 #28] enablePrimitiveRouting
        //   (1) 목적: CaveBiomeData의 tunnelPrimitive/roomPrimitive enum 소비 활성화.
        //             biome별 지질 특성에 맞는 primitive 형태 (Box/HexPrism/Slot 등) 선택.
        //   (2) 기본값 OFF 이유: 규칙 #6 — 기존 sdCapsule/sdSphere 경로 유지, byte-identical.
        //   (3) 활성화 전제: CaveBiomeData.tunnelPrimitive 또는 roomPrimitive가 enum 0 이외.
        //             + 현재 single-biome 단일 uniform (biome 0 값 사용).
        //   (4) 영향: SDF 평가 비용 증가 (가장 큰 primitive는 ~2× 기준).
        //             curvature + primitive 동시는 curvature 우선 (후속 통합 예정).
        //             규칙 #10: paramHash는 현재 단계에서는 routing 필드 포함 안 함
        //                       (토글 OFF 기본이라 영향 없음, Phase 4-3 E-α.10 승급 시 포함).
        //
        // 제약 (현재 버전):
        //   - Curvature active (edge.curvatureAmp > 0 × enableCurvedTunnels) 시 primitive 무시
        //   - 다중 biome 시 biome 0의 enum만 전역 사용 — E-α.5 이후 per-edge/per-node 확장
        // ═══════════════════════════════════════════════════════════════════
        [Header("E-α.4 — Primitive Routing")]
        [Tooltip("ON: CaveBiomeData.tunnelPrimitive/roomPrimitive enum 소비. " +
                 "OFF(기본): 기존 sdCapsule/sdSphere (byte-identical). " +
                 "curvature 활성 edge에서는 이 routing 무시 (curvature 우선).")]
        public bool enablePrimitiveRouting = false;

        // ═══════════════════════════════════════════════════════════════════
        // [Gate 5 Phase E-α.8] Cliff Joint — Case 5 경계 수직 절벽
        //
        // [규칙 #28] enableCliffJoint
        //   (1) 목적: Canyon (Case 5) ↔ 非Canyon 경계에서 수직 절벽 SDF 추가.
        //             A.7 cliffJoint 변수를 실제 shader에서 소비 활성화.
        //   (2) 기본값 OFF: 규칙 #6 — cliffJoint 계산은 되지만 무시.
        //   (3) 활성화 전제: typeA != typeB (biome 경계) + 한쪽 Case 5.
        //   (4) 영향: finalDensity에 cliff plane 혼합. smax(finalDensity, cliff, k=1.0).
        //             cliff 강도는 blend 중심 (0.5)에서 최대, 양 끝에서 fade.
        //             cliff plane height는 worldPos.y=2m 기준 (추후 per-node로 확장).
        //             규칙 #10: paramHash 포함 (E-α.10 승급 시 일괄).
        // ═══════════════════════════════════════════════════════════════════
        [Header("E-α.8 — Cliff Joint")]
        [Tooltip("ON: Case 5 (Canyon) 경계에서 수직 절벽 생성 (cliffJoint 소비 활성). " +
                 "OFF(기본): cliffJoint 계산만 되고 무시 (byte-identical).")]
        public bool enableCliffJoint = false;

        // ═══════════════════════════════════════════════════════════════════
        // [Gate 5 Phase E-α.9] World Map Size — 단일 source of truth
        //
        // 기존 분산:
        //   - CaveNodeGraphBuilder.dungeonBounds (노드 배치 범위)
        //   - CaveTerrainConfig.viewDistance × ChunkWorldSize (렌더 범위)
        //   → 일관성 체크 없음. 불일치 시 빈 chunk 렌더 위험.
        //
        // E-α.9 통합:
        //   worldMapSize 단일 필드로 노드 배치 범위 명시.
        //   NodeGraphBuilder.dungeonBounds는 이 값 참조 권장 (migration 필요).
        //   Playability Auditor에서 viewDistance × ChunkWorldSize ≤ worldMapSize 체크.
        //
        // 현재 상태:
        //   ✓ 필드 정의만 — 실제 dungeonBounds 대체는 사용자가 수동 migration
        //   ✓ Auditor에서 읽기 전용 참조 가능
        //   기존 dungeonBounds 경로 건드리지 않음 → byte-identical 완전 보장
        // ═══════════════════════════════════════════════════════════════════
        [Header("E-α.9 — World Map Size (single source)")]
        [Tooltip("월드 맵 전체 크기 (m). NodeGraphBuilder.dungeonBounds의 권장 참조값. " +
                 "Playability Auditor에서 viewDistance × ChunkWorldSize와 일관성 체크. " +
                 "기본 (300, 50, 300) = NodeGraphBuilder 기존 기본값과 일치.")]
        public Vector3 worldMapSize = new Vector3(300f, 50f, 300f);

        /// <summary>
        /// DC 포인트 수.
        ///   Phase 3-B ON: ChunkSize + 1 (Schaefer 정통, overlap 제거, ghost cache로 경계 공유)
        ///   Phase 3-B OFF(기본): ChunkSize + 3 (Seamless Overlap 패딩, 기존 동작)
        /// </summary>
        public int DCPointsPerAxis =>
            enablePhase3OverlapRemoval ? (ChunkSize + 1) : (ChunkSize + 3);

        // ── 렌더링 거리 ────────────────────────────────────────────────────
        [Header("Render Distance")]
        [Tooltip("플레이어 주변 청크 생성 반경 (청크 단위).")]
        [Min(1)] public int viewDistance = 2;

        [Tooltip("Y축 청크 생성 반경 (청크 단위).")]
        [Min(1)] public int viewDistanceY = 1;

        /// <summary>총 청크 수 = (2*viewDistance+1)^2 * (2*viewDistanceY+1)</summary>
        public int TotalChunkCount =>
            (2 * viewDistance + 1) * (2 * viewDistance + 1) * (2 * viewDistanceY + 1);

        // ── GPU 버퍼 예측 (DC 모드) ────────────────────────────────────────
        [Header("Buffer Preview (Read-Only)")]
        [Tooltip("DC 모드 기준 청크 1개 예상 GPU 메모리 (MB).")]
        public float EstimatedDCBufferMB => CalcDCBufferMB();

        private float CalcDCBufferMB()
        {
            int N = DCPointsPerAxis;
            long N3 = (long)N * N * N;
            long voxel   = N3 * 16;
            long hermite = N3 * 3 * 32;
            long dcVert  = N3 * 56;
            long dcQuad  = N3 * 16;
            return (voxel + hermite + dcVert + dcQuad) / (1024f * 1024f);
        }

        // ── OnValidate ────────────────────────────────────────────────────
        private void OnValidate()
        {
            // voxelSize가 targetWorldChunkSize보다 크면 ChunkSize < 1 방지
            if (voxelSize > targetWorldChunkSize)
            {
                Debug.LogWarning($"[CaveTerrainConfig] voxelSize({voxelSize}) > targetWorldChunkSize({targetWorldChunkSize}). 자동 보정.");
                voxelSize = targetWorldChunkSize * 0.5f;
            }

            // ChunkSize가 너무 크면 GPU 메모리 경고
            int cs = ChunkSize;
            if (cs > 64)
                Debug.LogWarning($"[CaveTerrainConfig] ChunkSize={cs}는 DC 모드에서 GPU 메모리 부담이 큽니다. targetWorldChunkSize를 줄이거나 voxelSize를 늘리세요.");

            float mem = EstimatedDCBufferMB;
            if (mem > 50f)
                Debug.LogError($"[CaveTerrainConfig] 청크당 예상 DC 버퍼 {mem:F1}MB > 50MB 마일스톤 초과!");
            else
                Debug.Log($"[CaveTerrainConfig] ChunkSize={cs}, VoxelSize={voxelSize}, WorldSize={ChunkWorldSize}m, DC버퍼≈{mem:F1}MB/청크");
        }

#if UNITY_EDITOR
        // ── Editor 헬퍼 ───────────────────────────────────────────────────
        [ContextMenu("메모리 예측 출력")]
        private void PrintMemoryEstimate()
        {
            int N = DCPointsPerAxis;
            Debug.Log(
                $"[CaveTerrainConfig] ── 메모리 예측 ──\n" +
                $"  ChunkSize={ChunkSize}  VoxelSize={voxelSize}m  WorldSize={ChunkWorldSize}m\n" +
                $"  DC PointsPerAxis={N}  N³={N*N*N}\n" +
                $"  voxelBuffer:      {(long)N*N*N*16/1024}KB\n" +
                $"  hermiteEdgeBuffer:{(long)N*N*N*3*32/1024}KB\n" +
                $"  dcVertexBuffer:   {(long)N*N*N*56/1024}KB\n" +
                $"  dcQuadBuffer:     {(long)N*N*N*16/1024}KB\n" +
                $"  합계:              {EstimatedDCBufferMB:F1}MB/청크\n" +
                $"  총 청크 {TotalChunkCount}개 × {EstimatedDCBufferMB:F1}MB = {TotalChunkCount * EstimatedDCBufferMB:F0}MB\n" +
                $"  (단, 디스패처 버퍼는 청크 수와 무관하게 1세트 공유)"
            );
        }
#endif
    }
}
