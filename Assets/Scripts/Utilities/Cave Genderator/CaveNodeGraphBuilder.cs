using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace CaveSystem
{
    /// <summary>
    /// [Phase 2] 기획자의 의도가 반영된 수학적 3D 그래프(방과 통로)를 메인 스레드에서 생성하는 핵심 두뇌 클래스입니다.
    /// </summary>
    public class CaveNodeGraphBuilder : MonoBehaviour
    {
        public static CaveNodeGraphBuilder Instance { get; private set; }

        [Header("Dungeon Graph Rules (던전 규모)")]
        public int targetRoomCount = 20;
        public Vector3 dungeonBounds = new Vector3(300, 50, 300);

        [Header("Room Sizing (방 크기 제어)")]
        public float minRoomRadius = 8.0f;
        public float maxRoomRadius = 15.0f;
        public float bossRoomRadiusMultiplier = 2.0f;

        [Header("Connection Rules (통로 생성 규칙)")]
        public float mainPathWidth = 8.0f;  // 보스방으로 가는 메인 통로
        public float sidePathWidth = 4.0f;  // 일반/보물방 곁가지 통로
        [Range(0f, 1f)] public float loopProbability = 0.15f; // 막다른 길을 방지하는 루프 생성 확률

        // ─────────────────────────────────────────────────────────
        // [Phase 4.5-G Stage 1-C + Stage 2] Graph Supervisor 설정
        // ─────────────────────────────────────────────────────────
        [Header("Graph Supervisor (Phase 4.5-G)")]
        [Tooltip("P5 Planarity 검사 활성화 — 2D crossover edge 자동 제거")]
        public bool enablePlanarityCheck = true;

        [Tooltip("P5 Planarity: Y 차이 이 값 이상이면 crossover 허용 (입체 교차).")]
        [Range(0f, 30f)] public float planarityYExemptMeters = 6.0f;

        [Tooltip("P6 Degree 검사 활성화 — 노드당 max degree 초과 edge 제거")]
        public bool enableDegreeCheck = true;

        [Tooltip("P6 Degree: 일반 노드 최대 degree (Spawn/Boss/Treasure는 별도)")]
        [Range(2, 8)] public int maxDegreeNormal = 4;

        [Tooltip("P7 Biome 배분 검사 활성화 — nodeDensityWeight 기반 quota")]
        public bool enableBiomeQuotaCheck = true;

        [Tooltip("Supervisor AutoRepair 최대 반복 횟수")]
        [Range(1, 10)] public int supervisorMaxIterations = 3;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 4.5-G F3] CPU/GPU Biome Sampling Sync — atomic preset 부속 토글
        //   OFF (기본): legacy Mathf.PerlinNoise + 1000m offset (Route_Astar baseline)
        //   ON: BiomeShaderSync.Snoise2D 사용 → GPU shader와 정확 일치
        //   atomic preset GpuAligned 시 자동 ON.
        //   주의: F3 OFF + 다운스트림 (BlendBoost, G2) ON은 잘못된 위치에 보호 적용됨.
        //         atomic preset이 자동으로 짝을 맞춰줌 — Inspector 수동 변경 시 주의.
        // ═══════════════════════════════════════════════════════════════════════════════
        [Header("Phase 4.5-G: F3 / Stage 3-D / J5 (Atomic-managed)")]
        [Tooltip("[F3] CPU biome 분류 함수 선택.\n" +
                 "OFF (기본): Mathf.PerlinNoise+1000m (legacy, GPU와 ~50% 위치 mismatch).\n" +
                 "ON: BiomeShaderSync.Snoise2D (GPU 정확 일치).\n" +
                 "atomic preset GpuAligned 시 자동 ON.")]
        public bool useShaderCompatBiomeSampling = false;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [η DebugAware Fix — #2] Effective biome quota counting (default ON)
        //
        //   ON: GetBiomeIdxAt → GPU 실제 평가 biome ((bw<0.5)?bIdxA:bIdxB) 반환
        //   OFF: bIdxA만 반환 (legacy, BiomeImbalance 부정확)
        //
        //   사용자 결정: "이걸 기본옵션으로 확정하기" → default true
        //   영향 범위: BiomeQuota 검사 + biomeCurrent 카운팅 + Supervisor CountNodesByBiome
        //   D2 보장: useShaderCompatBiomeSampling=false (Legacy) 시 진입 안 함
        // ═══════════════════════════════════════════════════════════════════════════════
        [Tooltip("[η Fix #2] biome quota 검사 시 effective biome 사용.\n" +
                 "ON (default): GPU 평가 biome ((bw<0.5)?bIdxA:bIdxB) 반환 — 정확한 quota.\n" +
                 "OFF: bIdxA만 (legacy, BiomeImbalance 부정확).\n" +
                 "useShaderCompatBiomeSampling=true일 때만 의미.")]
        public bool useEffectiveBiomeForQuota = true;

        [Tooltip("[Stage 3-D D4] Critical risk passage width 보강 강도.\n" +
                 "0 (기본): 비활성, byte-identical.\n" +
                 "0.3: cross-biome edge width × (1 + 0.3 × blendCoverage × naturalMul).\n" +
                 "F3 ON 권장 — F3 OFF 시 잘못된 edge에 적용됨.\n" +
                 "atomic preset GpuAligned 시 자동 0.3.")]
        [Range(0f, 0.5f)] public float blendWidthBoost = 0f;

        [Tooltip("[J5] F3 진단 로그 — CPU↔GPU mismatch 측정 (100 sample loop).\n" +
                 "OFF (기본, 권장): 비용 0, byte-identical.\n" +
                 "ON: GenerateGraphInternal 끝에 magenta 로그 출력. F3 ON 시 의미 있음.\n" +
                 "atomic preset GpuAligned 시 자동 ON.")]
        public bool enableJ5DiagLog = false;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 4.5-G P4] Centrality-Aware Spawn — γ State (SingleSourceEcotone) 부속 토글
        //
        //   Single-Source 모델에서 노드가 blend zone에 배치되면 두 노드 사이 passage가
        //   visual amp ≈ 0 (plain wall)이 되는 문제를 해결.
        //
        //   동작:
        //     SpawnNodes — candidate 위치의 blendCentrality 검사
        //                   centrality > nodeMaxCentrality면 거절 (70% attempt까지)
        //     BuildMSTAndLoops — edge 후보의 midpoint centrality 가중
        //                   위험 edge는 거리 페널티 추가 → MST가 안전 edge 선호
        //
        //   사용자 결정: blend center 최소화 + faded 영역 길게
        //               → nodeMaxCentrality 낮게 (0.3~0.4)
        //               → edgeCentralityPenalty 높게 (1.5~2.0)
        // ═══════════════════════════════════════════════════════════════════════════════
        [Header("Phase 4.5-G P4: Centrality-Aware Spawn (γ State)")]
        [Tooltip("[Centrality-Aware Spawn] 노드 배치 시 blend zone 검사.\n" +
                 "OFF (기본): centrality 무관 — Route_Astar baseline 동작.\n" +
                 "ON: blendCentrality < nodeMaxCentrality 인 위치만 채택.\n" +
                 "γ State (SingleSourceEcotone) 진입 시 atomic preset이 자동 ON.\n" +
                 "Single-Source 모델에서 plain passage 방지에 필수.")]
        public bool enableCentralityAwareSpawn = false;

        [Tooltip("[Centrality-Aware Spawn] 노드 배치 허용 centrality 임계.\n" +
                 "0.2: 자기 영역만 (매우 엄격, spawn 성공률 ↓ 가능)\n" +
                 "0.3 (사용자 권장): 자기 영역 + 약한 transition\n" +
                 "0.5: 중간 transition까지 허용\n" +
                 "1.0: 검사 안 함 (현재 동작과 동일)\n" +
                 "사용자 요청: blend center 최소화 → 0.3 권장")]
        [Range(0.2f, 1.0f)] public float nodeMaxCentrality = 0.3f;

        [Tooltip("[Edge Centrality Penalty] edge midpoint 위험도 가중.\n" +
                 "0.0: edge 검사 안 함\n" +
                 "1.0: 위험 edge 거리 × 2배 (선호 ↓)\n" +
                 "2.0 (사용자 권장): edge 거리 × 3배 (강한 페널티 — faded 영역 길게)\n" +
                 "3.0: 매우 강한 페널티")]
        [Range(0f, 3f)] public float edgeCentralityPenalty = 2.0f;

        [Tooltip("[Edge Centrality Threshold] edge midpoint centrality가 이 값 초과 시 페널티 적용.\n" +
                 "0.3 (사용자 권장): faded 영역 길게 — edge midpoint이 약한 transition만 허용.\n" +
                 "0.5: 중간 transition까지 무페널티.")]
        [Range(0.2f, 1.0f)] public float edgeCentralityThreshold = 0.3f;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 4.5-G δ/ε] afterf3 합병 — 위험 토글 격리 (rebuild 우선)
        //
        //   분류:
        //     ZERO 위험 (δ/ε 모두 ON): PassageMetric (CPU only) / RouteGeometry (유틸) /
        //                              EdgeData 72B (waypoint=0 fast-path) / J5 본체 / Editor
        //     HIGH/MEDIUM 위험 (δ에서 default OFF, ε에서 자동 ON):
        //       enableRoutePlanner    — edge waypoint 주입으로 SDF curve 변경
        //       enableSpatialHash     — voxel-edge lookup 최적화 (cellSize 결함 시 edge missed)
        //       enableBiomeAwareRouting — biome 회피 routing (RoutePlanner 종속)
        //
        //   D2 보장: δ에서 모든 위험 토글 OFF + EdgeData numWaypoints=0 → γ와 byte-identical
        // ═══════════════════════════════════════════════════════════════════════════════
        [Header("Phase 4.5-G δ/ε: afterf3 Merge (위험 토글, default OFF)")]

        [Tooltip("[★ HIGH 위험] RoutePlanner — 3-Phase A* edge waypoint 주입.\n" +
                 "OFF (δ default): 직선 edge 그대로 (γ byte-identical).\n" +
                 "ON (ε auto): collision 발생 edge에 waypoint 주입 → SDF curve 변경.\n" +
                 "    좁은 통로 (sidePathWidth 4m) 부근 SDF 파편 위험.\n" +
                 "atomic preset FullMerge (ε) 진입 시 자동 ON.\n" +
                 "atomic preset SingleSourceEcotonePlus (δ) 진입 시 강제 OFF.")]
        public bool enableRoutePlanner = false;

        [Tooltip("[MEDIUM 위험] SpatialHash — voxel-edge lookup 최적화.\n" +
                 "OFF (δ default): brute force lookup (γ와 동일 SDF).\n" +
                 "ON (ε auto): grid hash 활성. cellSize 잘못 시 edge missed → 큰 결함.\n" +
                 "atomic preset FullMerge (ε) 진입 시 자동 ON.\n" +
                 "atomic preset SingleSourceEcotonePlus (δ) 진입 시 강제 OFF.")]
        public bool enableSpatialHash = false;

        [Tooltip("[SpatialHash] grid cellSize.\n" +
                 "afterf3 default: 24m (chunk 16m × 1.5).\n" +
                 "권장 8~64m. 작을수록 정확, 클수록 메모리 절약.")]
        [Range(8f, 64f)] public float spatialHashCellSize = 24f;

        [Tooltip("[★ HIGH 위험] Biome-Aware Routing — biome 경계 회피 routing.\n" +
                 "RoutePlanner ON일 때만 의미. SDF path 변경 위험 누적.\n" +
                 "atomic preset FullMerge (ε) 진입 시 자동 ON.\n" +
                 "atomic preset SingleSourceEcotonePlus (δ) 진입 시 강제 OFF.")]
        public bool enableBiomeAwareRouting = false;

        [Tooltip("[Biome-Aware Routing] biome 교차 임계.\n" +
                 "afterf3 default: 0.5. RoutePlanner ON일 때 활성.")]
        [Range(0f, 1f)] public float biomeAwareCrossThreshold = 0.5f;

        // ════════════════════════════════════════════════════════════════════
        // [η DebugAware Phase 4] 3-Pass Optimization 토글 + 파라미터
        //
        //   D2 안전: default OFF — η state 선택해도 검증 전엔 EdgeData 변경 없음.
        //            사용자가 명시적으로 ON 시에만 자동 조정 활성.
        // ════════════════════════════════════════════════════════════════════
        [Header("η DebugAware — Predictive Adjustment (Phase 4)")]
        [Tooltip("[η Phase 4] 3-Pass Optimization 활성.\n" +
                 "OFF (default): η state여도 EdgeData 자동 변경 없음 (측정만).\n" +
                 "ON: 위험 segments 있는 edges의 curvature/width 자동 조정.\n" +
                 "    1순위: curvAmp 감소 (직선화)\n" +
                 "    2순위: width 증가 (1.5× cap)\n" +
                 "    Pass 3: 노드 근처 가중 평균 보간\n" +
                 "γ/δ에서는 무시됨 (η/ε에서만 의미).")]
        public bool enablePredictiveAdjustment = false;

        [Tooltip("[η Phase 4] MIN_SAFE_WIDTH — 안전 마진 (m).\n" +
                 "predEffectiveWidth가 이 값보다 작으면 위험 판정.\n" +
                 "★ 사용자 요청: 최소 5m 보장 (player diameter 1m + buffer 4m).\n" +
                 "★ default 5.0m (이전 2.0m).\n" +
                 "보수적: 6.0m / 공격적: 4.0m / 매우 공격적: 3.0m.")]
        [Range(3.0f, 10.0f)] public float predictiveMinSafeWidth = 5.0f;

        [Tooltip("[η Phase 4] Pass 3 SMOOTH 영향력 [0~1].\n" +
                 "0 = SMOOTH 비활성 (Pass 2만)\n" +
                 "0.3 = 보수적 (디테일 보존 우선)\n" +
                 "0.5 = 균형 (default)\n" +
                 "0.7 = 강한 보간 (시각 완결성 우선)\n" +
                 "1.0 = 노드 평균과 완전 일치 (디테일 손실)")]
        [Range(0f, 1f)] public float predictiveSmoothFactor = 0.5f;

        [Tooltip("[ζ Phase 8] Edge Co-Location Penalty — fan node 파편 방지.\n" +
                 "OFF (default): 기존 MST cost 그대로.\n" +
                 "ON (η/ε에서만 의미): 같은 방향(<45°) edges에 4× distance penalty.\n" +
                 "효과: 한 노드에서 fan-out edges가 다양한 방향으로 분산.")]
        public bool enableEdgeCoLocationPenalty = false;

        [Tooltip("[ζ Phase 7] Edge ID Noise Decorrelation (GPU).\n" +
                 "OFF (default): 기존 SDF, 인접 edges 동일 noise pattern.\n" +
                 "ON (η/ε에서만 의미): edge별 hash offset → fan node 파편 분산.\n" +
                 "범위: Pure Noise biomes (Case 0/4/5/6)만 영향.")]
        public bool enableEdgeIDDecorrelation = false;

        [Tooltip("[Phase 11] Case 4 Rocky Voronoi 재설계.\n" +
                 "OFF (default): 기존 snoise 5번 (P4 amp 6m 위반).\n" +
                 "ON (η/ε에서만 의미): Voronoi3D + analytical sin → amp 4.2m로 축소.\n" +
                 "효과: 4원칙 PureNoise → Hybrid 접근, fan 파편 30% 감소.")]
        public bool enableCase4Voronoi = false;

        // ── δ/ε afterf3 데이터 캐시 (Editor 시각화 + 진단) ─────────────────────────
        [HideInInspector] public List<PassageMetric.PassageProfile> lastPassageProfiles
            = new List<PassageMetric.PassageProfile>();
        [HideInInspector] public PassageMetric.RiskAggregate lastRiskAggregate;
        [HideInInspector] public RoutePlanner.PlanStats lastRouteStats;
        [HideInInspector] public SpatialHash lastSpatialHash;

        // 생성된 최종 데이터
        [HideInInspector] public List<NodeData> nodesData = new List<NodeData>();
        [HideInInspector] public List<EdgeData> edgesData = new List<EdgeData>();

        // [Stage 2] Supervisor 결과 (BiomeDebugVisualizer 표시용)
        [HideInInspector] public GraphSupervisor.ValidationResult lastSupervisorResult;

        // 디버깅/기즈모 전용 데이터 캐싱
        private List<Vector3> rawPositions = new List<Vector3>();
        private Dictionary<int, int> roomTypes = new Dictionary<int, int>();
        private Dictionary<int, float> roomRadii = new Dictionary<int, float>();
        private List<Vector2Int> allEdges = new List<Vector2Int>();
        private HashSet<Vector2Int> criticalPathEdges = new HashSet<Vector2Int>();

        // [Phase 4.5-C] Preview Editor 지원 — criticalPathEdges 외부 접근
        //   읽기 전용 사본 반환. 실제 내부 상태는 private 유지.
        public HashSet<Vector2Int> GetCriticalPathEdges()
        {
            return new HashSet<Vector2Int>(criticalPathEdges);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        // ═══════════════════════════════════════════════════════════════════
        // [Gate 5 Phase 4.5-A] Preview 지원용 override settings (Editor 전용)
        //   null = Singleton CaveManager.Instance.biomeSettings 사용 (기존 경로)
        //   non-null = 이 값을 사용 (Preview Editor에서 임시 설정 주입)
        //   byte-identical 보장: 기존 호출 (GenerateGraph(seed))은 null 유지 → 동작 동일
        // ═══════════════════════════════════════════════════════════════════
        private CaveBiomeSettings _overrideSettings = null;

        /// <summary>
        /// 현재 유효한 biomeSettings 반환 — override 있으면 그것, 없으면 Singleton.
        /// CaveManager 참조 지점을 이 헬퍼로 통합하여 Preview Editor 지원.
        /// </summary>
        private CaveBiomeSettings ResolveBiomeSettings()
        {
            if (_overrideSettings != null) return _overrideSettings;
            if (CaveManager.Instance != null) return CaveManager.Instance.biomeSettings;
            return null;
        }

        /// <summary>
        /// 주어진 Seed를 바탕으로 100% 결정론적(Deterministic)인 노드 그래프를 생성합니다.
        /// 
        /// [Gate 5 Phase 4.5-A] overrideSettings 파라미터 추가 (Preview Editor 지원)
        ///   null (기본) = Singleton CaveManager.biomeSettings 사용 — 기존 동작 byte-identical
        ///   non-null = 이 settings로 Preview 생성 (Editor 전용)
        /// </summary>
        public void GenerateGraph(int seed, CaveBiomeSettings overrideSettings = null)
        {
            // [Phase 4.5-A] override 설정 (finally에서 복원)
            _overrideSettings = overrideSettings;
            try
            {
                GenerateGraphInternal(seed);
            }
            finally
            {
                _overrideSettings = null;  // 다음 호출 영향 방지
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 4.5-G Atomic Preset] BiomeSyncMode 적용 (5-State)
        //   GenerateGraphInternal 시작 시점에 호출되어 모든 atomic-managed 토글을 일괄 설정.
        //   같은 frame 내 atomic 보장 — mid-state 진입 차단.
        //
        //   Legacy:                Inspector 값 그대로 (수동 테스트 허용)
        //   GpuAligned:            F3 ON, BB 0.3, J5 ON 일괄 강제 (dual blend 유지)
        //   SingleSourceEcotone:   GpuAligned 모두 + Centrality-Aware Spawn ON (γ)
        //   ★ SingleSourceEcotonePlus: γ 모두 + 안전 afterf3 (PassageMetric F3-sync) (δ)
        //                              ★ 위험 토글 강제 OFF: RoutePlanner / SpatialHash / Biome-Aware
        //                              → 위험 토글 OFF + EdgeData waypoint=0 → γ byte-identical
        //   ★ FullMerge:           δ 모두 + 위험 토글 강제 ON (ε, afterf3 fully merged)
        //
        //   E4 P1 / I7 / E4 P2 / Soft-Terrace / Single-Source / Voronoi P1은 Dispatcher 측 atomic.
        // ═══════════════════════════════════════════════════════════════════════════════
        private void ApplyBiomeSyncMode()
        {
            if (CaveManager.Instance == null) return;
            var mode = CaveManager.Instance.biomeSyncMode;

            switch (mode)
            {
                case CaveSystem.BiomeSyncMode.Legacy:
                    // Inspector 값 그대로 — 수동 테스트 허용 (모든 토글 OFF가 default)
                    break;

                case CaveSystem.BiomeSyncMode.GpuAligned:
                    // F3 + BB + J5 일괄 ON (dual blend 유지)
                    useShaderCompatBiomeSampling = true;
                    if (blendWidthBoost < 0.001f) blendWidthBoost = 0.3f;
                    enableJ5DiagLog = true;
                    // Centrality-Aware Spawn은 OFF (β는 dual blend라 영향 적음)
                    break;

                case CaveSystem.BiomeSyncMode.SingleSourceEcotone:
                    // F3 + BB + J5 + Centrality-Aware Spawn 모두 ON (γ)
                    useShaderCompatBiomeSampling = true;
                    if (blendWidthBoost < 0.001f) blendWidthBoost = 0.3f;
                    enableJ5DiagLog = true;
                    enableCentralityAwareSpawn = true;  // ★ γ 핵심 — plain passage 방지
                    break;

                case CaveSystem.BiomeSyncMode.SingleSourceEcotonePlus:  // ★ δ
                    // γ 모두 그대로 (rebuild 우선)
                    useShaderCompatBiomeSampling = true;
                    if (blendWidthBoost < 0.001f) blendWidthBoost = 0.3f;
                    enableJ5DiagLog = true;
                    enableCentralityAwareSpawn = true;
                    // ZERO 위험 afterf3 자동 ON
                    PassageMetric.useShaderCompatSampling = true;  // F3 동기 (CPU only)
                    // ★ HIGH/MEDIUM 위험 토글 강제 OFF — γ byte-identical 보장
                    enableRoutePlanner = false;
                    enableSpatialHash = false;
                    enableBiomeAwareRouting = false;
                    break;

                case CaveSystem.BiomeSyncMode.DebugAware:  // ★ η
                    // δ 모두 그대로 (η는 δ의 superset)
                    useShaderCompatBiomeSampling = true;
                    if (blendWidthBoost < 0.001f) blendWidthBoost = 0.3f;
                    enableJ5DiagLog = true;
                    enableCentralityAwareSpawn = true;
                    PassageMetric.useShaderCompatSampling = true;
                    PassageMetric.enableSegmentAmpSmoothing = true;  // [Phase 12-C] ★ Pass 1.5
                    // δ와 동일하게 위험 토글 OFF (η는 별도 Predictive 인프라만)
                    enableRoutePlanner = false;
                    enableSpatialHash = false;
                    enableBiomeAwareRouting = false;
                    // ═══════════════════════════════════════════════════════════════
                    // ★ Phase 1: enum + Predictive Lookup Table만 활성
                    //   PassageSegment 확장, 3-Pass Optimization 등은 Phase 2~5에서.
                    //   현재는 enum + IsDebugAwareOrLater helper만 정의 → δ와 byte-identical.
                    // ═══════════════════════════════════════════════════════════════
                    // ═══════════════════════════════════════════════════════════════
                    // [Phase 12] ★ ζ Blank Zone + G1 Tunnel Surface Protection
                    //
                    //   Inspector default 0 (OFF) → η에서 권장값으로 자동 변경
                    //   사용자가 명시적으로 설정한 값이 있으면 (>= 0.001) 그대로 유지
                    //   D2 보장: γ/δ는 OFF 유지, η/ε에서만 자동 ON
                    // ═══════════════════════════════════════════════════════════════
                    {
                        var disp = (CaveManager.Instance != null)
                            ? CaveManager.Instance.computeDispatcher : null;
                        if (disp != null)
                        {
                            // ζ: blankZoneWidth 0 → 0.10 (~33m 평탄 zone)
                            if (disp.blankZoneWidth < 0.001f)
                                disp.blankZoneWidth = 0.10f;
                            // G1: tunnelGuardMargin 0 → 1.5m (표준 보호)
                            if (disp.tunnelGuardMargin < 0.001f)
                                disp.tunnelGuardMargin = 1.5f;
                            // M1 [Phase 12-D]: columnarDCOffset 0 → 0.5 (단층 본질 해결)
                            if (disp.columnarDCOffset < 0.001f)
                                disp.columnarDCOffset = 0.5f;
                            // M5 [Phase 12-E]: 모든 case 단층 해결 (Sediment/Colonnade/Rocky 등)
                            disp.enableBaseSDFLevelOffset = true;
                            // G2 [Phase 13]: tunnelExpansionFactor 0 → 0.5 (디테일 100% + 통로 보호)
                            if (disp.tunnelExpansionFactor < 0.001f)
                                disp.tunnelExpansionFactor = 0.5f;
                            // ★ N1 [Phase 14]: nodeChamberLockRadiusMul 0 → 1.5 (chamber 단위 biome lock)
                            //   ★ F2 boundary 노드 / F10 큰 chamber / F19 boundary cluster 단층 본질 해결
                            if (disp.nodeChamberLockRadiusMul < 0.001f)
                                disp.nodeChamberLockRadiusMul = 1.5f;
                            // outerFadeMargin/maxTunnelExpansion은 default (0.5/1.5) 그대로 사용
                            // nodeChamberLockInnerFrac도 default 0.5 그대로
                        }
                    }
                    break;

                case CaveSystem.BiomeSyncMode.FullMerge:  // ★ ε — 항상 모든 통합 (영구 정책)
                    // δ + η 모두 그대로 (η Predictive 인프라 자동 상속)
                    useShaderCompatBiomeSampling = true;
                    if (blendWidthBoost < 0.001f) blendWidthBoost = 0.3f;
                    enableJ5DiagLog = true;
                    enableCentralityAwareSpawn = true;
                    PassageMetric.useShaderCompatSampling = true;
                    PassageMetric.enableSegmentAmpSmoothing = true;  // [Phase 12-C] ★ Pass 1.5
                    // ════════════════════════════════════════════════════════════
                    // ★ ε 영구 정책 — η Phase 4~11 모든 토글 자동 ON
                    //   향후 새 기능 추가 시에도 이 case에 자동 통합되도록 패턴 유지.
                    // ════════════════════════════════════════════════════════════
                    enablePredictiveAdjustment = true;     // Phase 4 — 3-Pass Optimization
                    enableEdgeCoLocationPenalty = true;    // Phase 8 — fan node 분산
                    enableEdgeIDDecorrelation = true;      // Phase 7 — GPU noise 분산
                    enableCase4Voronoi = true;             // Phase 11 — Rocky 재설계
                    // ★ 위험 토글 강제 ON — afterf3 fully merged
                    enableRoutePlanner = true;
                    enableSpatialHash = true;
                    enableBiomeAwareRouting = true;
                    // RoutePlanner static config 전파 (afterf3 패턴)
                    RoutePlanner.enableBiomeAwareRouting = true;
                    RoutePlanner.biomeCrossThreshold = biomeAwareCrossThreshold;
                    // [Phase 12] ★ ζ + G1 권장값 자동 설정 (η와 동일)
                    {
                        var disp = (CaveManager.Instance != null)
                            ? CaveManager.Instance.computeDispatcher : null;
                        if (disp != null)
                        {
                            if (disp.blankZoneWidth < 0.001f)
                                disp.blankZoneWidth = 0.10f;
                            if (disp.tunnelGuardMargin < 0.001f)
                                disp.tunnelGuardMargin = 1.5f;
                            // M1 [Phase 12-D]: columnarDCOffset 자동 0.5
                            if (disp.columnarDCOffset < 0.001f)
                                disp.columnarDCOffset = 0.5f;
                            // M5 [Phase 12-E]: enableBaseSDFLevelOffset 자동 ON
                            disp.enableBaseSDFLevelOffset = true;
                            // G2 [Phase 13]: tunnelExpansionFactor 자동 0.5
                            if (disp.tunnelExpansionFactor < 0.001f)
                                disp.tunnelExpansionFactor = 0.5f;
                            // ★ N1 [Phase 14]: nodeChamberLockRadiusMul 자동 1.5
                            //   → chamber radius × 1.5 = ~12m lock 영역
                            //   ★ F2/F10/F19 단층 본질 해결
                            if (disp.nodeChamberLockRadiusMul < 0.001f)
                                disp.nodeChamberLockRadiusMul = 1.5f;
                        }
                    }
                    break;
            }
        }

        private void GenerateGraphInternal(int seed)
        {
            // ★ STEP 0: atomic preset 일괄 적용 (다른 모든 로직 전에)
            ApplyBiomeSyncMode();

            // 1. 초기화 및 난수 고정
            Random.InitState(seed);
            nodesData.Clear();
            edgesData.Clear();
            rawPositions.Clear();
            roomTypes.Clear();
            roomRadii.Clear();
            allEdges.Clear();
            criticalPathEdges.Clear();

            // 2. Poisson Disk 형태의 거리 기반 배척 노드 스폰 (방 흩뿌리기)
            SpawnNodes();

            if (rawPositions.Count < 2)
            {
                Debug.LogWarning("[CaveNodeGraphBuilder] 생성된 노드가 너무 적습니다. 바운더리를 넓히거나 반경을 줄이세요.");
                return;
            }

            // 3. 거리 기반 크루스칼(Kruskal) 최소 신장 트리(MST) 알고리즘 + 루프 복구
            BuildMSTAndLoops();

            // 4. 위상 수학적 역할 부여 (스폰, 보스, 보물) 및 크리티컬 패스 도출
            AssignRolesAndPathfinding();

            // 5. GPU용 구조체(Struct)로 데이터 패킹
            PackDataForGPU();

            // ═════════════════════════════════════════════════════════════════════
            // [Phase 4.5-G δ/ε] afterf3 hook — 안전 기능 무조건 + 위험 기능 토글 격리
            //
            //   동작:
            //     δ 또는 ε State에서 PackDataForGPU 직후 실행
            //     ZERO 위험 (δ/ε): EdgeData waypoint=0 명시 초기화 (D2 fast-path 보장)
            //                      PassageMetric.RiskScore 계산 (CPU only)
            //     HIGH/MEDIUM 위험 (ε에서 자동 ON): RoutePlanner / SpatialHash 활성
            //
            //   D2 보장: δ에서 위험 토글 OFF → fast-path 유지 → γ byte-identical
            // ═════════════════════════════════════════════════════════════════════
            if (CaveManager.Instance != null && CaveManager.Instance.IsDeltaOrLater)
            {
                RunAfterf3MergeHook();

                // ═══════════════════════════════════════════════════════════════
                // [η Phase 4] 3-Pass Optimization으로 EdgeData 변경됐을 시
                //   GPU buffer 동기화 — PackDataForGPU 재호출 필수.
                //   Pass 2/3가 edgesData[i].curvatureAmp, edgesData[i].width 변경 가능.
                //   D2 안전: enablePredictiveAdjustment=false면 변경 없음 → no-op.
                // ═══════════════════════════════════════════════════════════════
                if (CaveManager.Instance.IsDebugAwareOrLater
                    && enablePredictiveAdjustment)
                {
                    PackDataForGPU();
                }
            }

            // ─────────────────────────────────────────────────────────
            // [Phase 4.5-G Stage 2] Graph Supervisor — 원칙 검증 + AutoRepair
            //   PackDataForGPU 이후 nodesData/edgesData 완성된 상태에서 실행
            //   AutoRepair가 edges 제거하면 edgesData 기반으로 다시 PackDataForGPU 필요
            // ─────────────────────────────────────────────────────────
            if (enablePlanarityCheck || enableDegreeCheck || enableBiomeQuotaCheck)
            {
                RunSupervisor();
            }
            else
            {
                lastSupervisorResult = GraphSupervisor.ValidationResult.Empty();
            }

            Debug.Log($"<color=green>[GraphBuilder]</color> 던전 설계 완료! (Seed: {seed}, 방: {nodesData.Count}개, 통로: {edgesData.Count}개)");
        }

        /// <summary>
        /// [Stage 2] Supervisor 실행 — Validate + AutoRepair
        ///   AutoRepair 후 edges 변경이 있으면 edgesData 재패킹.
        /// </summary>
        private void RunSupervisor()
        {
            // MST edge keys 계산 (AutoRepair에서 보호)
            //   allEdges 중 unused 아닌 것들 — 단순화: 첫 (N-1)개를 MST로 간주
            //   (BuildMSTAndLoops에서 MST가 먼저 추가되므로 index 기반 판별 가능)
            //   하지만 allEdges 이후 순서가 바뀔 수 있으니 여기선 nodesData.Count-1 기준
            int nodeCount = nodesData.Count;
            int mstEdgeCount = Mathf.Min(nodeCount - 1, allEdges.Count);
            var mstKeys = new HashSet<Vector2Int>();
            for (int i = 0; i < mstEdgeCount; i++)
            {
                var e = allEdges[i];
                int lo = Mathf.Min(e.x, e.y);
                int hi = Mathf.Max(e.x, e.y);
                mstKeys.Add(new Vector2Int(lo, hi));
            }

            // CaveBiomeSettings 접근 (CaveManager 경유)
            CaveBiomeSettings biomeSettings = null;
            if (CaveManager.Instance != null)
                biomeSettings = CaveManager.Instance.biomeSettings;

            int edgesBefore = edgesData.Count;
            lastSupervisorResult = GraphSupervisor.AutoRepair(
                nodesData, edgesData, mstKeys, biomeSettings,
                planarityYExemptMeters: planarityYExemptMeters,
                maxDegreeNormal: maxDegreeNormal,
                maxIterations: supervisorMaxIterations);
            int edgesAfter = edgesData.Count;

            if (lastSupervisorResult.violations.Count > 0 || edgesBefore != edgesAfter)
            {
                Debug.Log($"<color=yellow>[Supervisor]</color> " +
                          $"Violations 잔여: {lastSupervisorResult.violations.Count} " +
                          $"(crossover={lastSupervisorResult.crossoverCount}, " +
                          $"degree={lastSupervisorResult.degreeExceedCount}, " +
                          $"biomeImbalance={lastSupervisorResult.biomeImbalanceCount}), " +
                          $"edges {edgesBefore}→{edgesAfter}, iter={lastSupervisorResult.repairIterations}");
            }
            else
            {
                Debug.Log($"<color=green>[Supervisor]</color> 모든 원칙 통과 ✓ (iter={lastSupervisorResult.repairIterations})");
            }

            // ═══════════════════════════════════════════════════════════════
            // [Phase 17] ★ TerrainContextAnalyzer 자동 Rebuild hook (Option B)
            //   ★ Graph + Supervisor 완료 후 외부 분석기 cache 갱신
            //   ★ D2 보장: 컴포넌트 미할당 시 no-op (FindFirstObjectByType null)
            // ═══════════════════════════════════════════════════════════════
            try
            {
#if UNITY_2023_1_OR_NEWER
                var analyzer = UnityEngine.Object.FindFirstObjectByType<TerrainContextAnalyzer>();
#else
                var analyzer = UnityEngine.Object.FindObjectOfType<TerrainContextAnalyzer>();
#endif
                if (analyzer != null)
                {
                    analyzer.Rebuild();
                    Debug.Log($"<color=cyan>[TerrainContextAnalyzer]</color> Auto-rebuild ✓");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[TerrainContextAnalyzer] Auto-rebuild failed: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Phase 4.5-G δ/ε] RunAfterf3MergeHook — afterf3 인프라 통합
        //
        //   호출 조건: CaveManager.IsDeltaOrLater (δ 또는 ε State)
        //   호출 위치: PackDataForGPU 직후, Supervisor 직전
        //
        //   동작 분류:
        //     [ZERO 위험 — 무조건 실행]
        //       1. EdgeData waypoint=0 명시 초기화 (D2 fast-path 보장)
        //          ※ PackDataForGPU에서 이미 0으로 초기화될 가능성 — 명시적 보강
        //       2. PassageMetric.RiskScore 계산 (CPU only, Editor 시각화용)
        //          afterf3 패턴 정확 매칭: ComputeProfile per-edge → ComputeAggregate
        //
        //     [HIGH 위험 — enableRoutePlanner 토글, ε에서만 ON]
        //       3. RoutePlanner — collision 발생 edge에 waypoint 주입
        //          현재 P1: 본체 호출 단순화 (afterf3 정확 매칭은 P2 보강 단계)
        //
        //     [MEDIUM 위험 — enableSpatialHash 토글, ε에서만 ON]
        //       4. SpatialHash 빌드 — voxel-edge lookup 최적화
        //          현재 P1: 빌드만 (Compute 통합은 P1.E)
        //
        //   D2 보장: δ에서 위험 토글 OFF + waypoint=0 → γ와 동일 SDF
        // ═══════════════════════════════════════════════════════════════════════════════
        private void RunAfterf3MergeHook()
        {
            if (CaveManager.Instance == null) return;
            bool isFullMerge = CaveManager.Instance.IsFullMerge;

            // ── Step 1: EdgeData waypoint=0 명시 초기화 (D2 fast-path) ──────────
            //   PackDataForGPU에서 default 0이지만, afterf3는 numWaypoints에 의존하므로 명시
            //   flags bits[0..1] = 0 → fast-path
            for (int i = 0; i < edgesData.Count; i++)
            {
                var ed = edgesData[i];
                ed.w1_packed = 0;
                ed.w2_packed = 0;
                // flags bits[0..1] = 0 (numWaypoints), 다른 bits 보존
                ed.flags &= ~3u;  // bits[0..1] 클리어
                edgesData[i] = ed;
            }

            // ── Step 2: PassageMetric (CPU only, ZERO 위험) ─────────────────────
            //   afterf3 정확 호출 패턴: edge별 ComputeProfile → ComputeAggregate
            try
            {
                lastPassageProfiles.Clear();
                var bs = CaveManager.Instance.biomeSettings;
                if (bs != null && bs.globalBiomes != null && bs.globalBiomes.Count > 0)
                {
                    // ═══════════════════════════════════════════════════════════════
                    // [Phase 4.5-G η DebugAware Phase 2] Predictive Measurement 활성
                    //   η DebugAware OR ε FullMerge state에서만 enablePredictive=true
                    //   다른 state (δ만)에서는 default false → 기존 동작 (byte-identical)
                    // ═══════════════════════════════════════════════════════════════
                    bool enablePredictive = false;
                    bool singleSourceActive = false;
                    float fadePower = 2.0f;
                    float minSafeWidth = predictiveMinSafeWidth;  // ★ Phase 4: Inspector 값 사용
                    if (CaveManager.Instance != null)
                    {
                        enablePredictive = CaveManager.Instance.IsDebugAwareOrLater;
                        singleSourceActive = CaveManager.Instance.IsSingleSourceActive;
                        // FadePower는 Dispatcher Inspector 값 우선
                        if (CaveManager.Instance.computeDispatcher != null)
                            fadePower = CaveManager.Instance.computeDispatcher.singleSourceFadePower;
                    }

                    for (int i = 0; i < edgesData.Count; i++)
                    {
                        var e = edgesData[i];
                        int biomeA = PassageMetric.SampleBiomeIdx(e.startPos, bs);
                        int biomeB = PassageMetric.SampleBiomeIdx(e.endPos, bs);

                        var profile = PassageMetric.ComputeProfile(
                            e, i, biomeA, biomeB, bs,
                            numSegments: 10, blendWidthBoost: blendWidthBoost,
                            // [η DebugAware Phase 2] Predictive params
                            allEdges: enablePredictive ? edgesData : null,
                            enablePredictive: enablePredictive,
                            fadePower: fadePower,
                            singleSourceActive: singleSourceActive,
                            minSafeWidth: minSafeWidth);
                        lastPassageProfiles.Add(profile);
                    }
                    lastRiskAggregate = PassageMetric.ComputeAggregate(lastPassageProfiles);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"<color=orange>[δ/ε PassageMetric]</color> 분석 실패 (계속 진행): {ex.Message}");
                lastPassageProfiles.Clear();
                lastRiskAggregate = default;
            }

            // ── Step 3: RoutePlanner (HIGH 위험, ε에서만 ON) ────────────────────
            //   현재 P1 범위에서는 단순 stub만 — 실제 waypoint 주입은 P2 보강에서
            //   afterf3 정확 매칭하려면 newEdges 리스트 재구성 + Supervisor 재실행 필요
            if (enableRoutePlanner)
            {
                Debug.LogWarning(
                    "<color=orange>[ε RoutePlanner]</color> P1 단계에서는 stub. " +
                    "P2 보강 단계에서 afterf3 정확 호출 패턴 통합 예정. " +
                    "현재 ε는 PassageMetric만 활성, waypoint 주입 없음 (= δ와 동일 SDF).");
                lastRouteStats = new RoutePlanner.PlanStats { totalEdges = edgesData.Count };
            }

            // ── Step 4: SpatialHash (MEDIUM 위험, ε에서만 ON) ───────────────────
            //   현재 P1 범위에서는 빌드만 — Compute 통합은 P1.E에서
            if (enableSpatialHash)
            {
                try
                {
                    // afterf3 패턴: static Build(edges, dungeonBounds, cellSizeOverride)
                    lastSpatialHash = SpatialHash.Build(edgesData, dungeonBounds, spatialHashCellSize);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"<color=orange>[ε SpatialHash]</color> 빌드 실패 (계속 진행): {ex.Message}");
                    lastSpatialHash = null;
                }
            }

            // ════════════════════════════════════════════════════════════════════
            // [η DebugAware Phase 4] 3-Pass Optimization (Predict → Resolve → Smooth)
            //
            //   η DebugAware OR ε FullMerge State에서만 활성.
            //   Step 2 PassageMetric에서 측정된 predEffectiveWidth를 활용해
            //   위험 edges의 curvature/width를 자동 조정.
            //
            //   원칙:
            //     Pass 1 PREDICT — Step 2에서 이미 완료 (predEffectiveWidth 계산됨)
            //     Pass 2 RESOLVE — 위험 edge만 최소 변경
            //                       1순위: curvAmp 감소 (직선화 — 시각 영향 최소)
            //                       2순위: width 증가 (1.5× cap)
            //     Pass 3 SMOOTH — 노드 근처 가중 평균 보간 (시각 완결성)
            //
            //   D2 보장: η OFF 시 hasPredictiveData=false → Pass 진입 전 skip
            //   변경된 edges는 PackDataForGPU 호출하여 GPU에 재전송 필요.
            //
            //   ★ enablePredictiveAdjustment 토글: default OFF (검증 안전)
            //       사용자 명시적으로 ON 시에만 EdgeData 자동 변경 허용.
            // ════════════════════════════════════════════════════════════════════
            bool runOptim = (CaveManager.Instance != null
                             && CaveManager.Instance.IsDebugAwareOrLater
                             && enablePredictiveAdjustment
                             && lastPassageProfiles != null
                             && lastPassageProfiles.Count > 0);
            if (runOptim)
            {
                int adjustedCount = 0;
                int smoothedNodes = 0;

                try
                {
                    // ── Pass 2: RESOLVE — 위험 edges 자동 조정 ─────────────────
                    for (int e = 0; e < edgesData.Count && e < lastPassageProfiles.Count; e++)
                    {
                        var profile = lastPassageProfiles[e];
                        if (profile.segments == null || profile.segments.Length == 0) continue;

                        // 이 edge에 위험 segment가 있는지 + 최대 부족분 측정
                        float maxShortage = 0f;
                        bool anyDanger = false;
                        for (int s = 0; s < profile.segments.Length; s++)
                        {
                            var seg = profile.segments[s];
                            if (seg.predIsBelowSafeMargin)
                            {
                                anyDanger = true;
                                float shortage = predictiveMinSafeWidth - seg.predEffectiveWidth;
                                if (shortage > maxShortage) maxShortage = shortage;
                            }
                        }
                        if (!anyDanger) continue;  // ★ 안전한 edge는 변경 안 함 (디테일 보존)

                        var ed = edgesData[e];
                        float origCurv = ed.curvatureAmp;
                        float origWidth = ed.width;
                        float remaining = maxShortage;

                        // ── 우선순위 1: curvature 감소 (직선화) ────────────────
                        //   감소량 limit: 현재 curvAmp × 1 (완전히 0까지)
                        //   효과: shortage의 50% (curve impact = curvAmp × 0.5)
                        if (ed.curvatureAmp > 0.01f)
                        {
                            float curvReduction = Mathf.Min(ed.curvatureAmp,
                                                            remaining * 0.5f * 2f); // = remaining × 1.0
                            ed.curvatureAmp -= curvReduction;
                            if (ed.curvatureAmp < 0.01f) ed.curvatureAmp = 0f;
                            remaining -= curvReduction * 0.5f;
                        }

                        // ── 우선순위 2: width 증가 (남은 shortage만) ──────────
                        //   상한: 1.5× original cap (시각 일관성)
                        if (remaining > 0.01f)
                        {
                            float maxNewWidth = origWidth * 1.5f;
                            float widthIncrement = Mathf.Min(remaining * 1.2f,
                                                              maxNewWidth - ed.width);
                            if (widthIncrement > 0)
                            {
                                ed.width += widthIncrement;
                            }
                        }

                        // 변경 사항 반영
                        if (ed.curvatureAmp != origCurv || ed.width != origWidth)
                        {
                            edgesData[e] = ed;
                            adjustedCount++;
                        }
                    }

                    // ── Pass 3: SMOOTH — 노드 근처 가중 평균 보간 ──────────────
                    //   각 node의 connectedEdges 평균 width/curvAmp 계산.
                    //   t < 0.15 또는 t > 0.85 영역에서 50% 가중 보간 적용.
                    //
                    //   주의: edgesData level에서는 per-edge width만 있으므로
                    //         실제 보간은 edge.width를 노드 평균과 50% 보간.
                    //         (per-segment 보간은 θ state로 미래)
                    if (predictiveSmoothFactor > 0.001f)
                    {
                        // ★ EdgeData에는 nodeA/nodeB 없음 → position 기반 매칭
                        //   nodesData의 position을 hash하여 빠른 조회
                        const float POS_EPS = 0.5f;  // 노드 일치 판정 거리 (m)
                        int FindNodeIdxByPos(Vector3 pos)
                        {
                            int best = -1;
                            float bestDist = POS_EPS;
                            for (int n = 0; n < nodesData.Count; n++)
                            {
                                float d = Vector3.Distance(nodesData[n].position, pos);
                                if (d < bestDist) { bestDist = d; best = n; }
                            }
                            return best;  // -1 = no match (자유 endpoint)
                        }

                        // 1) 각 노드별 평균 계산
                        var nodeAvgWidth = new Dictionary<int, float>();
                        var nodeAvgCurv = new Dictionary<int, float>();
                        var nodeEdgeCount = new Dictionary<int, int>();

                        // edge별 node idx 캐시 (재계산 방지)
                        var edgeNodeIdx = new (int a, int b)[edgesData.Count];

                        for (int e = 0; e < edgesData.Count; e++)
                        {
                            int a = FindNodeIdxByPos(edgesData[e].startPos);
                            int b = FindNodeIdxByPos(edgesData[e].endPos);
                            edgeNodeIdx[e] = (a, b);

                            float w = edgesData[e].width;
                            float c = edgesData[e].curvatureAmp;

                            if (a >= 0)
                            {
                                if (!nodeAvgWidth.ContainsKey(a))
                                { nodeAvgWidth[a] = 0f; nodeAvgCurv[a] = 0f; nodeEdgeCount[a] = 0; }
                                nodeAvgWidth[a] += w; nodeAvgCurv[a] += c; nodeEdgeCount[a]++;
                            }
                            if (b >= 0)
                            {
                                if (!nodeAvgWidth.ContainsKey(b))
                                { nodeAvgWidth[b] = 0f; nodeAvgCurv[b] = 0f; nodeEdgeCount[b] = 0; }
                                nodeAvgWidth[b] += w; nodeAvgCurv[b] += c; nodeEdgeCount[b]++;
                            }
                        }

                        // 평균 계산 (degree ≥ 2 노드만)
                        var keys = new List<int>(nodeEdgeCount.Keys);
                        foreach (int k in keys)
                        {
                            if (nodeEdgeCount[k] >= 2)
                            {
                                nodeAvgWidth[k] /= nodeEdgeCount[k];
                                nodeAvgCurv[k] /= nodeEdgeCount[k];
                                smoothedNodes++;
                            }
                        }

                        // 2) edge.width를 양 끝 노드 평균과 보간
                        for (int e = 0; e < edgesData.Count; e++)
                        {
                            (int a, int b) = edgeNodeIdx[e];
                            int countA = (a >= 0) ? nodeEdgeCount.GetValueOrDefault(a, 0) : 0;
                            int countB = (b >= 0) ? nodeEdgeCount.GetValueOrDefault(b, 0) : 0;
                            if (countA < 2 && countB < 2) continue;

                            var ed2 = edgesData[e];
                            float waA = (a >= 0) ? nodeAvgWidth.GetValueOrDefault(a, ed2.width) : ed2.width;
                            float waB = (b >= 0) ? nodeAvgWidth.GetValueOrDefault(b, ed2.width) : ed2.width;
                            float avgNodeWidth = (waA + waB) * 0.5f;

                            // predictiveSmoothFactor만큼 보간 (0=변화 없음, 1=완전 평균)
                            ed2.width = Mathf.Lerp(ed2.width, avgNodeWidth, predictiveSmoothFactor);
                            edgesData[e] = ed2;
                        }
                    }

                    Debug.Log($"<color=#FFD700>[η Phase 4 3-Pass]</color> " +
                              $"Adjusted {adjustedCount}/{edgesData.Count} edges, " +
                              $"smoothed {smoothedNodes} fan nodes " +
                              $"(MIN_SAFE={predictiveMinSafeWidth:F1}m, " +
                              $"smoothFactor={predictiveSmoothFactor:F2})");

                    // PackDataForGPU 재호출은 호출자 책임 (GenerateGraphInternal에서 별도 처리)
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"<color=orange>[η Phase 4]</color> 3-Pass 실패 (계속 진행): {ex.Message}");
                }
            }

            // ── Diagnostic 로그 (δ/ε 진입 명시) ─────────────────────────────────
            string mode = isFullMerge ? "ε FullMerge" : "δ SingleSourceEcotonePlus";
            string riskState = $"RoutePlanner={(enableRoutePlanner ? "STUB" : "OFF")} " +
                               $"SpatialHash={(enableSpatialHash ? "ON" : "OFF")} " +
                               $"BiomeAware={(enableBiomeAwareRouting ? "ON" : "OFF")}";
            Debug.Log($"<color=cyan>[{mode}]</color> afterf3 hook 완료 — " +
                      $"PassageProfiles={lastPassageProfiles.Count} | {riskState}");
        }
        // ═══════════════════════════════════════════════════════════════════════════════

        #region 1. Node Spawning
        private void SpawnNodes()
        {
            int maxAttempts = 30; // 무한 루프 방지용 Limit

            // [수정됨] 방들이 Y축으로 날뛰지 않도록 Y 좌표를 0 부근으로 안정화
            Vector3 spawnPos = new Vector3(
                Random.Range(-dungeonBounds.x * 0.1f, dungeonBounds.x * 0.1f),
                Random.Range(-5.0f, 5.0f), // Y축 안정화
                Random.Range(-dungeonBounds.z * 0.1f, dungeonBounds.z * 0.1f)
            );
            rawPositions.Add(spawnPos);

            // ─────────────────────────────────────────────────────────
            // [Phase 4.5-G Stage 1-D] Biome-weighted node placement
            //   CaveBiomeSettings 있으면 biome quota 계산 → 부족한 biome 우선
            //   부족 biome에 생성되는 position만 채택 (rejection sampling)
            //   충분 biome이면 normal placement
            // ─────────────────────────────────────────────────────────
            Dictionary<int, int> biomeQuota = null;
            Dictionary<int, int> biomeCurrent = null;
            CaveBiomeSettings bs = (CaveManager.Instance != null)
                ? CaveManager.Instance.biomeSettings : null;
            if (enableBiomeQuotaCheck && bs != null && bs.globalBiomes != null
                && bs.globalBiomes.Count > 0)
            {
                biomeQuota = new Dictionary<int, int>();
                biomeCurrent = new Dictionary<int, int>();
                float totalWeight = 0f;
                for (int b = 0; b < bs.globalBiomes.Count; b++)
                {
                    if (bs.globalBiomes[b] != null)
                        totalWeight += bs.globalBiomes[b].nodeDensityWeight;
                }
                if (totalWeight < 0.01f) totalWeight = bs.globalBiomes.Count;

                for (int b = 0; b < bs.globalBiomes.Count; b++)
                {
                    if (bs.globalBiomes[b] == null) continue;
                    float ratio = bs.globalBiomes[b].nodeDensityWeight / totalWeight;
                    biomeQuota[b] = Mathf.RoundToInt(targetRoomCount * ratio);
                    biomeCurrent[b] = 0;
                }

                // Spawn (이미 추가) 의 biome도 카운트
                int spawnBiome = GetBiomeIdxAt(spawnPos, bs);
                if (biomeCurrent.ContainsKey(spawnBiome)) biomeCurrent[spawnBiome]++;
            }

            for (int i = 1; i < targetRoomCount; i++)
            {
                bool placed = false;
                // Quota 기반 search — 부족한 biome 우선
                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    Vector3 candidatePos = new Vector3(
                        Random.Range(-dungeonBounds.x * 0.5f, dungeonBounds.x * 0.5f),
                        Random.Range(-dungeonBounds.y * 0.1f, dungeonBounds.y * 0.1f),
                        Random.Range(-dungeonBounds.z * 0.5f, dungeonBounds.z * 0.5f)
                    );

                    // 겹침 검사
                    bool overlap = false;
                    foreach (var pos in rawPositions)
                    {
                        if (Vector3.Distance(pos, candidatePos) < minRoomRadius * 2.5f)
                        {
                            overlap = true;
                            break;
                        }
                    }
                    if (overlap) continue;

                    // [Stage 1-D] Biome quota 확인
                    //   attempt 1~15: quota 부족 biome만 채택
                    //   attempt 16~30: quota 무시 (공간 채우기 우선)
                    //
                    //   ★ η DebugAware Fix [옵션 B]: candidate biome의 quota 진행률 추적
                    //   desperatelyUnderQuota: cur < quota / 2 (50% 미만)
                    //   → 이 biome은 centrality 검사 우회 권리 부여 (다음 분기에서 사용)
                    bool candidateDesperatelyUnderQuota = false;
                    if (biomeQuota != null && attempt < maxAttempts / 2)
                    {
                        int bIdx = GetBiomeIdxAt(candidatePos, bs);
                        int cur = biomeCurrent.GetValueOrDefault(bIdx);
                        int quota = biomeQuota.GetValueOrDefault(bIdx);
                        if (cur >= quota)
                            continue;  // 이 biome은 이미 채워짐, 다른 위치 시도
                        // ★ desperately = cur가 quota의 50% 미만 (우회 자격 부여)
                        candidateDesperatelyUnderQuota = (cur * 2 < quota);
                    }

                    // ═══════════════════════════════════════════════════════════════
                    // [Phase 4.5-G P4] Centrality-Aware Spawn — γ State 부속
                    //   blendCentrality 검사: 자기 영역에만 노드 배치
                    //   attempt 0~70%: 엄격 검사 (centrality > threshold면 거절)
                    //   attempt 70%~100%: fallback (검사 무시 — 공간 부족 보호)
                    //
                    //   사용자 요청: blend center 최소화 + faded 영역 길게
                    //   → nodeMaxCentrality default 0.3 (자기 영역 + 약한 transition만)
                    //
                    //   ★ η DebugAware Fix [옵션 B]: BiomeImbalance 해소 (Colonnade 7%만 spawn 문제)
                    //   원인 분석: Colonnade가 spatial 한쪽 패치라 자기영역 깊은 곳 부족
                    //              → centrality 검사로 거의 모든 candidate 거절 → quota 못 채움
                    //   해결: desperatelyUnderQuota인 biome은 centrality 우회 (∴ blend boundary
                    //         근처에도 배치 허용). quota 50% 채워지면 다시 엄격 모드로.
                    //   D2 보장: enableCentralityAwareSpawn=false면 모든 분기 skip → 동일 동작.
                    //            biomeQuota=null (Inspector 토글 OFF)면 desperately=false 유지.
                    // ═══════════════════════════════════════════════════════════════
                    if (enableCentralityAwareSpawn && bs != null && attempt < (maxAttempts * 0.7f))
                    {
                        if (!candidateDesperatelyUnderQuota)
                        {
                            // 일반 모드: centrality 엄격 검사
                            float spawnCentrality = GetBlendCentralityAt(candidatePos, bs);
                            if (spawnCentrality > nodeMaxCentrality)
                                continue;  // blend zone 깊은 곳 거절, 다른 위치 시도
                        }
                        // ★ desperately mode: centrality 검사 우회
                        //   BiomeImbalance 우선 — Colonnade 같은 부족 biome 보장
                        //   quota 50% 충족하면 자동으로 일반 모드 복귀
                    }

                    // 채택
                    rawPositions.Add(candidatePos);
                    if (biomeCurrent != null)
                    {
                        int bIdx = GetBiomeIdxAt(candidatePos, bs);
                        biomeCurrent[bIdx] = biomeCurrent.GetValueOrDefault(bIdx) + 1;
                    }
                    placed = true;
                    break;
                }
                if (!placed) break; // 공간이 가득 차면 조기 종료
            }

            // 초기 반경 셋팅
            for (int i = 0; i < rawPositions.Count; i++)
            {
                roomRadii[i] = Random.Range(minRoomRadius, maxRoomRadius);
                roomTypes[i] = 0; // 일반 방
            }

            // [Stage 1-D] Biome 배분 로그
            if (biomeCurrent != null && biomeQuota != null)
            {
                string distStr = "";
                foreach (var kv in biomeCurrent)
                {
                    string name = (kv.Key < bs.globalBiomes.Count && bs.globalBiomes[kv.Key] != null)
                        ? bs.globalBiomes[kv.Key].biomeName : $"biome{kv.Key}";
                    int q = biomeQuota.GetValueOrDefault(kv.Key);
                    distStr += $"  {name}={kv.Value}/{q}";
                }
                Debug.Log($"<color=cyan>[GraphBuilder Stage 1-D]</color> Biome-weighted placement: {rawPositions.Count} nodes |{distStr}");
            }
        }

        /// <summary>
        /// [Stage 1-D] worldPos 에 해당하는 biome idx 결정
        /// (shader와 근사 일치 — 실제로는 ShaderNoiseCompat 필요하나 배치 단계에선 근사로 충분).
        /// </summary>
        /// <summary>
        /// [Stage 1-D] worldPos 에 해당하는 biome idx 결정.
        ///
        /// ★ η DebugAware Fix: effective biome 반환 (이전 bIdxA만)
        ///   GPU 실제 평가: (bw<0.5) ? bIdxA : bIdxB
        ///   - biomeQuota 검사가 GPU 평가 biome 기준으로 정확
        ///   - CountNodesByBiome 카운팅도 정확 (Supervisor와 일관)
        ///   - Inspector 토글 useEffectiveBiomeForQuota=true (default) — 영향 활성
        ///
        /// useShaderCompatBiomeSampling=false (legacy)면 기존 Mathf.PerlinNoise 그대로
        /// (D2 byte-identical 보장 — Route_Astar baseline).
        /// </summary>
        private int GetBiomeIdxAt(Vector3 pos, CaveBiomeSettings bs)
        {
            if (bs == null || bs.globalBiomes == null || bs.globalBiomes.Count == 0) return 0;
            float macroScale = Mathf.Max(1f, bs.macroBiomeScale);
            int biomeCount = bs.globalBiomes.Count;

            // F3 토글에 따라 분기 (BiomeShaderSync와 일치)
            if (useShaderCompatBiomeSampling)
            {
                int bIdxA = BiomeShaderSync.SampleBiomeIdx(pos, bs);

                // ═══════════════════════════════════════════════════════════
                // [η DebugAware Fix — #2] effective biome 반환 (default ON)
                //   GPU L591~593: bIdxB = bIdxA + 1
                //   GPU 실제 평가: (bw<0.5) ? typeA : typeB
                //   useEffectiveBiomeForQuota=true (default): GPU 평가 biome 반환
                //   useEffectiveBiomeForQuota=false: 기존 bIdxA만 (legacy 호환)
                // ═══════════════════════════════════════════════════════════
                if (useEffectiveBiomeForQuota && biomeCount >= 2)
                {
                    int bIdxB = Mathf.Clamp(bIdxA + 1, 0, biomeCount - 1);
                    float bw = BiomeShaderSync.SampleBlendWeight(pos, bs);
                    return (bw < 0.5f) ? bIdxA : bIdxB;
                }
                return bIdxA;
            }

            // Legacy: Mathf.PerlinNoise + 1000m offset
            float n = Mathf.PerlinNoise(pos.x / macroScale + 1000f, pos.z / macroScale + 1000f);
            float scaled = n * Mathf.Max(biomeCount - 1, 0f);
            return Mathf.Clamp(Mathf.FloorToInt(scaled), 0, biomeCount - 1);
        }

        /// <summary>
        /// [Phase 4.5-G P4] worldPos에서 blendCentrality 계산 (CPU 측 근사, GPU shader와 일치)
        ///   centrality = 4 × w × (1 - w), w = smoothstep(frac(scaledMap))
        ///   range [0, 1]: 0 = 자기 영역 깊은 곳, 1 = blend center
        ///   F3 토글에 따라 macroNoise 함수 분기 (GpuAligned/SingleSourceEcotone에서 정확).
        /// </summary>
        private float GetBlendCentralityAt(Vector3 pos, CaveBiomeSettings bs)
        {
            if (bs == null || bs.globalBiomes == null || bs.globalBiomes.Count == 0) return 0f;
            float macroScale = Mathf.Max(1f, bs.macroBiomeScale);
            int biomeCount = bs.globalBiomes.Count;
            if (biomeCount < 2) return 0f;  // single biome → no blend possible

            // F3 토글에 따라 noise 분기
            float macroNoise;
            if (useShaderCompatBiomeSampling)
            {
                // GPU shader 동일 — snoise2D, no offset
                macroNoise = BiomeShaderSync.Snoise2D(pos.x / macroScale, pos.z / macroScale);
            }
            else
            {
                // Legacy: Mathf.PerlinNoise + 1000m offset, [0,1] → [-1,1] 변환
                macroNoise = Mathf.PerlinNoise(pos.x / macroScale + 1000f, pos.z / macroScale + 1000f) * 2f - 1f;
            }

            float scaledMap = (macroNoise * 0.5f + 0.5f) * Mathf.Max(biomeCount - 1, 0f);
            float frac = scaledMap - Mathf.Floor(scaledMap);
            float w = frac * frac * (3f - 2f * frac);  // smoothstep — GPU와 일치
            return 4f * w * (1f - w);                   // centrality
        }
        #endregion

        #region 2. Graph Building (Kruskal's MST)
        private void BuildMSTAndLoops()
        {
            int n = rawPositions.Count;
            List<EdgeTemp> possibleEdges = new List<EdgeTemp>();

            // ═══════════════════════════════════════════════════════════════════════
            // [Phase 4.5-G P4] Edge Centrality Penalty — γ State 부속
            //   edge 후보 중간점 centrality 가중. 위험 edge는 거리 페널티 추가 →
            //   MST가 안전 edge 선호. enableCentralityAwareSpawn ON일 때만 적용.
            //
            //   사용자 요청: faded 영역 길게 → edgeCentralityPenalty 높게 (default 2.0)
            // ═══════════════════════════════════════════════════════════════════════
            CaveBiomeSettings bs = ResolveBiomeSettings();
            bool useEdgePenalty = enableCentralityAwareSpawn && bs != null && edgeCentralityPenalty > 0.001f;

            // ═══════════════════════════════════════════════════════════════
            // [η DebugAware Phase 8] ζ FragmentSafe — Edge Co-Location Penalty
            //
            //   목적: 한 노드에서 여러 edges가 비슷한 방향으로 출발하면
            //         GPU smin이 누적되어 fan에 파편 발생.
            //         angle 유사한 edges에 distance penalty 추가 → 다른 방향 선호.
            //
            //   D2 보장: enableEdgeCoLocationPenalty=false (default) 시 fast-path
            //             η DebugAware OR ε FullMerge에서만 사용 (atomic 토글)
            //
            //   구현: MST 그리기 후, 각 후보 edge에 대해 이미 추가된 edges와
            //         같은 from/to 노드에서의 angle 비교 → 유사하면 penalty.
            //         지금은 단순화 — possibleEdges 정렬 전에 angle 분산 가산.
            // ═══════════════════════════════════════════════════════════════
            bool useCoLocPenalty = false;
            if (CaveManager.Instance != null
                && CaveManager.Instance.IsDebugAwareOrLater
                && enableEdgeCoLocationPenalty)
            {
                useCoLocPenalty = true;
            }

            // co-location penalty 계산 위해 노드별 출발 edges 추적 (incremental)
            //   key = node idx, value = list of (toIdx, direction)
            var nodeEdgeDirs = useCoLocPenalty
                ? new Dictionary<int, List<(int to, Vector3 dir)>>() : null;
            if (useCoLocPenalty)
            {
                for (int i = 0; i < n; i++) nodeEdgeDirs[i] = new List<(int, Vector3)>();
            }

            // 모든 가능한 연결선(완전 그래프)의 거리 계산 (노드 수가 적어 O(N^2)도 매우 빠름)
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    float baseDist = Vector3.Distance(rawPositions[i], rawPositions[j]);
                    float weightedDist = baseDist;

                    if (useEdgePenalty)
                    {
                        // edge midpoint + 양 끝 centrality 검사
                        Vector3 midPoint = (rawPositions[i] + rawPositions[j]) * 0.5f;
                        float midC = GetBlendCentralityAt(midPoint, bs);
                        float startC = GetBlendCentralityAt(rawPositions[i], bs);
                        float endC = GetBlendCentralityAt(rawPositions[j], bs);
                        float maxC = Mathf.Max(midC, Mathf.Max(startC, endC));

                        // threshold 초과 시 거리 페널티 추가
                        //   penalty 2.0 + (maxC - 0.3) = 0.7 → 거리 × (1 + 1.4) = 2.4× → 안전 edge 선호
                        if (maxC > edgeCentralityThreshold)
                        {
                            float over = maxC - edgeCentralityThreshold;
                            weightedDist *= (1f + over * edgeCentralityPenalty);
                        }
                    }

                    // ★ Phase 8: Co-Location Penalty — 정렬 후 적용 (post-process)
                    //   여기서는 baseDist만 계산. 실제 penalty는 Sort 후 increment.

                    possibleEdges.Add(new EdgeTemp
                    {
                        from = i,
                        to = j,
                        dist = weightedDist
                    });
                }
            }

            // 짧은 거리 순 정렬 (가중 거리 기준 — 위험 edge는 더 멀게 인식되어 후순위)
            possibleEdges.Sort((a, b) => a.dist.CompareTo(b.dist));

            // ═══════════════════════════════════════════════════════════════
            // [Phase 8] Co-Location Penalty 적용 (정렬 직후, MST 진행 중)
            //   각 edge가 추가될 때 양 끝 노드의 기존 edges와 angle 비교.
            //   유사한 angle (< 45°) 발견 시 다음 후보들의 cost 증가.
            //
            //   단순화: 정렬은 그대로, MST 알고리즘 안에서 실시간 평가하면 너무 복잡.
            //   대신 Sort 후 두 번째 pass로 cost 재조정.
            // ═══════════════════════════════════════════════════════════════
            if (useCoLocPenalty)
            {
                const float ANGLE_SIMILAR_DEG = 45f;
                const float ANGLE_PENALTY_BASE = 4.0f;
                float angleSimilarRad = ANGLE_SIMILAR_DEG * Mathf.Deg2Rad;
                float cosThreshold = Mathf.Cos(angleSimilarRad);

                for (int idx = 0; idx < possibleEdges.Count; idx++)
                {
                    var e = possibleEdges[idx];
                    Vector3 dir = (rawPositions[e.to] - rawPositions[e.from]).normalized;
                    float baseDist = Vector3.Distance(rawPositions[e.from], rawPositions[e.to]);

                    // 양 끝 노드 모두 검사
                    int penalty = 0;
                    foreach (var (otherTo, otherDir) in nodeEdgeDirs[e.from])
                    {
                        if (Vector3.Dot(dir, otherDir) > cosThreshold) penalty++;
                    }
                    Vector3 dirRev = -dir;
                    foreach (var (otherTo, otherDir) in nodeEdgeDirs[e.to])
                    {
                        if (Vector3.Dot(dirRev, otherDir) > cosThreshold) penalty++;
                    }
                    if (penalty > 0)
                    {
                        // 같은 방향 edge 1개당 baseDist × ANGLE_PENALTY_BASE 추가
                        float additionalDist = baseDist * penalty * ANGLE_PENALTY_BASE;
                        e.dist += additionalDist;
                        possibleEdges[idx] = e;
                    }
                    // 이 edge가 (가설적으로) 추가되면 다음 검사에 영향
                    nodeEdgeDirs[e.from].Add((e.to, dir));
                    nodeEdgeDirs[e.to].Add((e.from, dirRev));
                }

                // penalty 적용 후 재정렬
                possibleEdges.Sort((a, b) => a.dist.CompareTo(b.dist));
            }

            // Union-Find (Disjoint Set) 초기화
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int Find(int i)
            {
                if (parent[i] == i) return i;
                return parent[i] = Find(parent[i]);
            }

            void Union(int i, int j)
            {
                int rootI = Find(i);
                int rootJ = Find(j);
                if (rootI != rootJ) parent[rootI] = rootJ;
            }

            // [Stage 1-C + 1-D] Degree 추적 — 노드별 현재 degree
            int[] degrees = new int[n];

            // MST 구성 및 버려지는 엣지 수집
            List<EdgeTemp> unusedEdges = new List<EdgeTemp>();

            foreach (var edge in possibleEdges)
            {
                if (Find(edge.from) != Find(edge.to))
                {
                    // MST edge — 연결성 필수이므로 Planarity 검사 생략
                    //   (Supervisor Validate에서 후처리 violations 기록)
                    Union(edge.from, edge.to);
                    allEdges.Add(new Vector2Int(edge.from, edge.to));
                    degrees[edge.from]++;
                    degrees[edge.to]++;
                }
                else
                {
                    unusedEdges.Add(edge);
                }
            }

            // ═══════════════════════════════════════════════════════════════
            // [Phase 4.5-G Stage 1-C + 1-D] Loop edge 추가 시 P5/P6 예방적 체크
            //   MST는 그대로 유지, 추가 loop edge만 원칙 준수 검증
            //   이후 Supervisor가 MST 포함 전체 검증 (AutoRepair)
            // ═══════════════════════════════════════════════════════════════
            int loopsConsidered = 0, loopsAccepted = 0, loopsRejDegree = 0, loopsRejPlanarity = 0;

            foreach (var edge in unusedEdges)
            {
                if (Random.value >= loopProbability) continue;
                loopsConsidered++;

                // [P6] Degree 체크 — 일반 노드 기준
                if (enableDegreeCheck)
                {
                    int maxDegFrom = GetMaxDegreeForNode(edge.from);
                    int maxDegTo = GetMaxDegreeForNode(edge.to);
                    if (degrees[edge.from] >= maxDegFrom || degrees[edge.to] >= maxDegTo)
                    {
                        loopsRejDegree++;
                        continue;
                    }
                }

                // [P5+S7] Planarity 체크 — 기존 edge 와의 2D 교차 (Y 차이 큰 경우 예외)
                if (enablePlanarityCheck)
                {
                    if (WouldCrossoverWithExistingEdges(edge.from, edge.to))
                    {
                        loopsRejPlanarity++;
                        continue;
                    }
                }

                allEdges.Add(new Vector2Int(edge.from, edge.to));
                degrees[edge.from]++;
                degrees[edge.to]++;
                loopsAccepted++;
            }

            Debug.Log($"<color=cyan>[GraphBuilder]</color> Loop edges: considered={loopsConsidered}, " +
                      $"accepted={loopsAccepted}, rejected(degree)={loopsRejDegree}, " +
                      $"rejected(planarity)={loopsRejPlanarity}");
        }

        /// <summary>
        /// [Stage 1-C] 후보 edge가 기존 allEdges와 2D 교차하는지 검사.
        /// Y 차이가 planarityYExemptMeters 이상이면 입체 교차로 간주 (실제 mesh 겹침 없음).
        /// </summary>
        private bool WouldCrossoverWithExistingEdges(int candidateFrom, int candidateTo)
        {
            Vector3 a1 = rawPositions[candidateFrom];
            Vector3 a2 = rawPositions[candidateTo];

            foreach (var existingEdge in allEdges)
            {
                // 공유 노드가 있으면 교차 아님 (같은 끝점)
                if (existingEdge.x == candidateFrom || existingEdge.x == candidateTo ||
                    existingEdge.y == candidateFrom || existingEdge.y == candidateTo)
                    continue;

                Vector3 b1 = rawPositions[existingEdge.x];
                Vector3 b2 = rawPositions[existingEdge.y];

                // 2D (XZ plane) 교차 검사
                bool xzCross = Segments2DIntersect(
                    new Vector2(a1.x, a1.z), new Vector2(a2.x, a2.z),
                    new Vector2(b1.x, b1.z), new Vector2(b2.x, b2.z));
                if (!xzCross) continue;

                // Y 차이 검사 — 충분히 멀면 3D에서 교차 없음
                float aMidY = (a1.y + a2.y) * 0.5f;
                float bMidY = (b1.y + b2.y) * 0.5f;
                if (Mathf.Abs(aMidY - bMidY) >= planarityYExemptMeters)
                    continue;  // S7 예외

                return true;  // 2D 교차 + Y 가까움 = 실제 crossover
            }
            return false;
        }

        /// <summary>2D 선분 교차 검사 — CCW 기반 표준 알고리즘.</summary>
        private static bool Segments2DIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float CCW(Vector2 a, Vector2 b, Vector2 c) =>
                (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

            float d1 = CCW(p3, p4, p1);
            float d2 = CCW(p3, p4, p2);
            float d3 = CCW(p1, p2, p3);
            float d4 = CCW(p1, p2, p4);

            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                return true;

            return false;  // collinear 무시 (매우 드물고 영향 미미)
        }

        /// <summary>[Stage 1-C] 노드 타입에 따른 max degree 반환.</summary>
        private int GetMaxDegreeForNode(int nodeIdx)
        {
            int rt = roomTypes.ContainsKey(nodeIdx) ? roomTypes[nodeIdx] : 0;
            switch (rt)
            {
                case 1: return 3;                 // Spawn — 명확성
                case 2: return maxDegreeNormal;   // Boss — 일반
                case 3: return 2;                 // Treasure — 비밀
                case 4: return 2;                 // Sinkhole — 수직
                default: return maxDegreeNormal;  // Normal
            }
        }

        private struct EdgeTemp { public int from, to; public float dist; }
        #endregion

        #region 3. Role Assignment & Pathfinding
        private void AssignRolesAndPathfinding()
        {
            int n = rawPositions.Count;
            Dictionary<int, List<int>> adjList = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++) adjList[i] = new List<int>();

            foreach (var edge in allEdges)
            {
                adjList[edge.x].Add(edge.y);
                adjList[edge.y].Add(edge.x);
            }

            // BFS를 통해 0번(스폰)에서 가장 먼 노드(보스) 찾기
            Queue<int> queue = new Queue<int>();
            int[] dist = new int[n];
            int[] prev = new int[n];
            for (int i = 0; i < n; i++) { dist[i] = -1; prev[i] = -1; }

            queue.Enqueue(0);
            dist[0] = 0;
            int farthestNode = 0;
            int maxDist = 0;

            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                if (dist[curr] > maxDist)
                {
                    maxDist = dist[curr];
                    farthestNode = curr;
                }

                foreach (int neighbor in adjList[curr])
                {
                    if (dist[neighbor] == -1)
                    {
                        dist[neighbor] = dist[curr] + 1;
                        prev[neighbor] = curr;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // 역할 부여
            roomTypes[0] = 1; // 1: 스폰 방
            roomRadii[0] = maxRoomRadius * 1.2f;

            roomTypes[farthestNode] = 2; // 2: 보스 방
            roomRadii[farthestNode] = maxRoomRadius * bossRoomRadiusMultiplier;

            // 크리티컬 패스 역추적 (보스 -> 스폰)
            int step = farthestNode;
            while (step != 0 && prev[step] != -1)
            {
                int parent = prev[step];
                // 양방향 모두 검색될 수 있도록 저장 포맷을 일정하게 유지
                criticalPathEdges.Add(new Vector2Int(Mathf.Min(step, parent), Mathf.Max(step, parent)));
                step = parent;
            }

            // 연결된 선이 1개뿐인 잎(Leaf) 노드를 찾아 보물 방으로 지정
            for (int i = 0; i < n; i++)
            {
                if (i != 0 && i != farthestNode && adjList[i].Count == 1)
                {
                    roomTypes[i] = 3; // 3: 보물 방
                    roomRadii[i] = minRoomRadius * 1.5f;
                }
            }
        }
        #endregion

        #region 4. Data Packing for GPU
        private void PackDataForGPU()
        {
            // NodeData 패킹 (16바이트 정렬에 맞춘 C# 구조체 활용)
        //
        // [Gate 5 Phase E-α.7] RoomGeometryMatrix 조회 (한 번만)
        //   null이면 기존 경로 (byte-identical).
        // [Phase 4.5-A] CaveManager → ResolveBiomeSettings (override 지원)
        RoomGeometryMatrixSO matrix = null;
        int biome0Type = 0;
        CaveBiomeSettings settings = ResolveBiomeSettings();
        if (settings != null)
        {
            matrix = settings.roomGeometryMatrix;
            if (settings.globalBiomes != null
                && settings.globalBiomes.Count > 0
                && settings.globalBiomes[0] != null)
            {
                biome0Type = settings.globalBiomes[0].noiseType;
            }
        }

        for (int i = 0; i < rawPositions.Count; i++)
            {
                int rt = roomTypes[i];
                float effectiveRadius = roomRadii[i];
                Vector3 sculpt;

                // [Gate 5 Phase A.5] GameplaySculpt — roomType별 자동 sculpt 편향
                //   x = wantNarrow    (-1~+1: 넓게↔좁게)
                //   y = wantHighGround (-1~+1: 낮게↔높게)
                //   z = wantOpen      (-1~+1: 밀집↔개방)
                //   쉐이더 소비는 A.7/.8에서 (현재는 데이터만 전달)
                switch (rt)
                {
                    case 1: sculpt = new Vector3(+0.3f, +0.2f, -0.3f); break; // Spawn: 약간 좁음, 밝음 느낌
                    case 2: sculpt = new Vector3(-0.5f, -0.2f, +0.8f); break; // Boss: 넓고 개방
                    case 3: sculpt = new Vector3(+0.7f, +0.5f, -0.7f); break; // Treasure: 좁고 높이 은닉
                    case 4: sculpt = new Vector3(-0.8f, +0.9f, +0.5f); break; // Sinkhole: 수직 shaft
                    default: sculpt = Vector3.zero; break;                    // Normal: 중립
                }

                // [Gate 5 Phase E-α.7] RoomGeometryMatrix override (Matrix 할당 시만)
                //   매트릭스에 (rt, biome0Type) 조합 있으면 radiusMultiplier + sculptFlags 덮어씀.
                //   byte-identical: matrix null OR entry 없음 → 기존 경로.
                if (matrix != null)
                {
                    MatrixEntry? entryOpt = matrix.Lookup(rt, biome0Type);
                    if (entryOpt.HasValue)
                    {
                        MatrixEntry entry = entryOpt.Value;
                        effectiveRadius = roomRadii[i] * entry.radiusMultiplier;
                        if (entry.useSculptOverride)
                            sculpt = entry.sculptFlagsOverride;
                        // primitiveOverride는 현재 per-node 아닌 전역 uniform 사용.
                        // Phase 5에서 per-node primitive buffer 확장 시 활용.
                    }
                }

                nodesData.Add(new NodeData
                {
                    position = rawPositions[i],
                    radius = effectiveRadius,
                    roomType = rt,
                    sculptFlags = sculpt
                });
            }

            // [Gate 5 Phase E-β.3.5] Biome curvatureAmp 조회
            //   현재: single-biome 가정 (globalBiomes[0] 사용)
            //   E-β.7에서: edge midpoint biome weight sampling으로 확장 예정
            //   연결 에러 없음 이유: global graph → 모든 chunk 일관, endTaper=0 → 접속부 일치
            // [Phase 4.5-A] CaveManager → ResolveBiomeSettings (override 지원)
            float defaultCurvAmp = 0f;
            CaveBiomeData biome0 = null;
            EdgeConstraintSO edgeConstraint = null;
            float biomeMaxSlope = 45f;
            CaveBiomeSettings biomeSettingsE6 = ResolveBiomeSettings();
            if (biomeSettingsE6 != null
                && biomeSettingsE6.globalBiomes != null
                && biomeSettingsE6.globalBiomes.Count > 0
                && biomeSettingsE6.globalBiomes[0] != null)
            {
                biome0 = biomeSettingsE6.globalBiomes[0];
                defaultCurvAmp = biome0.curvatureAmp;
                edgeConstraint = biome0.edgeConstraint;    // [E-α.5] 선택 SO
                biomeMaxSlope = biome0.maxSlope;
            }

            // [Gate 5 Phase E-α.6] CardinalBiomeBias 접근 (현재 골격 수준)
            //   biome별 방위 선호 정보. 현재 단계에서는 "정보만 조회",
            //   실제 노드별 biomeType override는 Phase 4-2 후속 또는 Phase 5 B.6에서 구현.
            //   byte-identical 보장: bias null → 기존 graph 생성 경로.
            CardinalBiomeBiasSO cardinalBias = null;
            if (biomeSettingsE6 != null)
            {
                cardinalBias = biomeSettingsE6.cardinalBias;
            }

            // [Gate 5 Phase E-β.6] Edge slope 검증 로그
            //   각 edge의 경사를 측정하여 maxSlope 초과 시 경고 로그.
            //   [E-α.6 확장] EdgeConstraint가 할당되어 있으면 해당 SO의 규칙 사용.
            //                null이면 biomeMaxSlope (45° 기본) 경고 로그만.
            int slopeWarnCount = 0;
            int constraintRejectCount = 0;

            // EdgeData 패킹
            foreach (var edge in allEdges)
            {
                // 작은 인덱스를 앞으로 하여 일관된 키 생성
                Vector2Int edgeKey = new Vector2Int(Mathf.Min(edge.x, edge.y), Mathf.Max(edge.x, edge.y));
                bool isCritical = criticalPathEdges.Contains(edgeKey);

                Vector3 startP = rawPositions[edge.x];
                Vector3 endP = rawPositions[edge.y];

                // [E-β.6] Slope 측정
                Vector3 edgeVec = endP - startP;
                float dy = Mathf.Abs(edgeVec.y);
                float dxz = Mathf.Sqrt(edgeVec.x * edgeVec.x + edgeVec.z * edgeVec.z);
                float slopeDeg = (dxz > 0.01f)
                    ? Mathf.Atan2(dy, dxz) * Mathf.Rad2Deg
                    : 89f;  // 완전 수직
                float edgeLen = edgeVec.magnitude;

                if (slopeDeg > biomeMaxSlope)
                    slopeWarnCount++;

                // [Gate 5 Phase E-α.6] EdgeConstraint 적용 (비파괴적: 위반 시 로그만)
                //   byte-identical: edgeConstraint==null → 모든 edge 통과 (기존 동작)
                //   할당 시: 위반 edge도 현재는 생성 (로그만). Phase 4-3 E-α.9 audit에서 활용.
                //   실제 rejection은 정책 결정 후 도입 (자칫 graph 단절 위험).
                if (edgeConstraint != null)
                {
                    if (!edgeConstraint.IsLengthValid(edgeLen))
                        constraintRejectCount++;
                    if (!edgeConstraint.IsSlopeValid(slopeDeg, biomeMaxSlope))
                        constraintRejectCount++;
                }

                edgesData.Add(new EdgeData
                {
                    startPos = startP,
                    endPos = endP,
                    width = isCritical ? mainPathWidth : sidePathWidth, // 메인 경로는 넓게, 나머지는 좁게
                    curvatureAmp = defaultCurvAmp  // [E-β.3.5] per-edge curvature from biome
                });
            }

            if (slopeWarnCount > 0)
            {
                Debug.LogWarning($"[E-α.6] {slopeWarnCount} edge(s) exceed biome.maxSlope ({biomeMaxSlope}°) " +
                                 $"(NavMesh bake 위험). Phase 4-3 Playability Auditor에서 상세 분석.");
            }
            if (constraintRejectCount > 0)
            {
                Debug.LogWarning($"[E-α.6] {constraintRejectCount} edge(s) violate EdgeConstraint rules " +
                                 $"(length/slope). 현재 비파괴적 로그만 — Phase 4-3에서 rejection 정책 도입 예정.");
            }
        }
        #endregion

        #region 5. Editor Gizmo Visualization
        private void OnDrawGizmos()
        {
            // 맵의 전체 바운더리를 그립니다.
            Gizmos.color = new Color(1, 1, 1, 0.1f);
            Gizmos.DrawWireCube(transform.position, dungeonBounds);

            if (rawPositions == null || rawPositions.Count == 0) return;

            // 1. 엣지 (길) 그리기
            foreach (var edge in allEdges)
            {
                Vector3 start = rawPositions[edge.x];
                Vector3 end = rawPositions[edge.y];

                Vector2Int edgeKey = new Vector2Int(Mathf.Min(edge.x, edge.y), Mathf.Max(edge.x, edge.y));

                if (criticalPathEdges.Contains(edgeKey))
                {
                    // 메인 동선 (크리티컬 패스) - 굵고 선명한 보라색
                    Gizmos.color = Color.magenta;
                    DrawThickLine(start, end, 3f);
                }
                else
                {
                    // 곁가지 길 - 얇은 파란색
                    Gizmos.color = new Color(0.2f, 0.6f, 1.0f, 0.5f);
                    Gizmos.DrawLine(start, end);
                }
            }

            // 2. 노드 (방) 그리기
            for (int i = 0; i < rawPositions.Count; i++)
            {
                Vector3 pos = rawPositions[i];
                float radius = roomRadii.ContainsKey(i) ? roomRadii[i] : minRoomRadius;
                int type = roomTypes.ContainsKey(i) ? roomTypes[i] : 0;

                switch (type)
                {
                    case 1: // Spawn (Green)
                        Gizmos.color = Color.green;
                        Gizmos.DrawSphere(pos, radius);
                        break;
                    case 2: // Boss (Red)
                        Gizmos.color = new Color(1, 0, 0, 0.8f);
                        Gizmos.DrawSphere(pos, radius);
                        break;
                    case 3: // Treasure (Yellow)
                        Gizmos.color = Color.yellow;
                        Gizmos.DrawWireSphere(pos, radius);
                        break;
                    default: // Normal (White)
                        Gizmos.color = new Color(1, 1, 1, 0.3f);
                        Gizmos.DrawWireSphere(pos, radius);
                        break;
                }
            }
        }

        // 에디터에서 굵은 선을 그리기 위한 헬퍼 함수
        private void DrawThickLine(Vector3 start, Vector3 end, float thickness)
        {
            Camera c = Camera.current;
            if (c == null) return;

            // 단순 선을 여러 겹 겹쳐 그려 굵기를 모방합니다. (씬 뷰 카메라 기준)
            Vector3 right = Vector3.Cross((end - start).normalized, c.transform.forward).normalized * (thickness * 0.1f);

            Gizmos.DrawLine(start - right, end - right);
            Gizmos.DrawLine(start, end);
            Gizmos.DrawLine(start + right, end + right);
        }
        #endregion
    }
}