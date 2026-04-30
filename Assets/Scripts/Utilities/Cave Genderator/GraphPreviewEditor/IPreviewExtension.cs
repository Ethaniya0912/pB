#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Gate 5 Phase 4.5-A] Preview Editor — 공통 데이터 구조 + 확장 인터페이스
    //
    // 이 파일은 Graph Preview Editor의 타입 정의 단일 파일.
    // 분류:
    //   1. PreviewInputs       — 사용자 입력
    //   2. FeatureFlags        — on/off 토글 enum
    //   3. BiomeSamplePoint    — 각 XZ sample 정보
    //   4. PreviewStats        — 통계 집계
    //   5. PreviewData         — 생성 결과 (final output)
    //   6. IPreviewExtension   — Phase 5+ 확장 인터페이스
    // ══════════════════════════════════════════════════════════════════════

    // ──────────────────────────────────────────────────────────────────
    // 1. PreviewInputs — 사용자가 Window에서 설정한 모든 입력
    // ──────────────────────────────────────────────────────────────────
    [System.Serializable]
    public class PreviewInputs
    {
        // Biome 기본 설정 (CaveBiomeSettings 역할)
        public List<CaveBiomeData> biomes = new List<CaveBiomeData>();
        public float macroBiomeScale = 500f;

        // SO slots
        public RoomGeometryMatrixSO matrix;
        public EdgeConstraintSO constraint;
        public CardinalBiomeBiasSO cardinalBias;

        // SO 개별 on/off (Before/After Compare 용)
        public bool matrixEnabled = true;
        public bool constraintEnabled = true;
        public bool cardinalBiasEnabled = true;

        // Graph parameters
        public int seed = 12345;
        public Vector3 dungeonBounds = new Vector3(300f, 50f, 300f);
        public int targetRoomCount = 20;
        public float mainPathWidth = 8f;
        public float sidePathWidth = 4f;
        public float minRoomRadius = 8f;
        public float maxRoomRadius = 15f;
        public float bossRoomRadiusMultiplier = 2.0f;
        public float loopProbability = 0.15f;

        // Feature toggles
        public FeatureFlags flags = FeatureFlags.ShowNodes | FeatureFlags.ShowEdges
                                     | FeatureFlags.ShowBiomeHeatmap | FeatureFlags.ShowBlendWeight
                                     | FeatureFlags.ShowViolations | FeatureFlags.ShowMatrixAura;

        // Heatmap parameters
        public float heatmapGridSize = 10f;
        public float heatmapOpacity = 0.35f;
        public float heatmapYPlane = 0f;  // 히트맵 그리는 Y 높이

        // [제안 4] Cross-section view
        public CrossSectionAxis crossAxis = CrossSectionAxis.None;
        public float crossPlaneValue = 0f;  // 축 값 (XY 평면이면 Z값, YZ면 X값)

        // ══════════════════════════════════════════════════════════
        // [Phase 4.5-B hotfix v2] 사용자 요청 신규 기능
        // ══════════════════════════════════════════════════════════

        // [Q2] 실제 사이즈 렌더링 (노드/엣지를 실제 스케일로)
        //   false (기본): 축소 gizmo (n.radius × 0.3 sphere, edge 얇은 선)
        //   true: 실제 radius sphere + edge 실제 width (wireframe disk)
        public bool showRealScale = false;

        // [Q3] Biome Divide — 경계선 + 라벨 + blend weight 수치
        //   ShowBiomeHeatmap과 독립 토글 (heatmap 색 + 경계선 동시 가능)
        public bool showBiomeBoundary = false;   // biome A≠B 경계에 선
        public bool showBiomeLabels = false;     // 각 셀 중심에 biome 이름 텍스트
        public bool showBlendWeightValue = false; // blend weight 숫자 표시

        // [Q4] Biome 분포 극단적 불균형 해결 — Preview 전용 균등화 토글
        //   false (기본): 실제 shader와 일치 (Perlin 중앙 bias → 중앙 biome 압도)
        //   true: CDF 역변환으로 biome 분포 균등화 (각 biome 대략 1/N 수렴)
        //   Shader 영향 없음 — Preview 시각화만 변경
        //   실제 shader 공식 수정은 Phase 5 별도 과제 (참조: pb4_phase5_biome_balance_task.html)
        public bool balanceBiomes = false;

        // [Q4] Node 정보 패널 — 클릭/선택 시 확장
        public bool showNodeInfoPanel = true;   // 노드 옆 접힘/펼침 라벨
        public HashSet<int> expandedNodeIndices = new HashSet<int>();  // 펼쳐진 노드 idx

        // [Q1 hotfix v3] Normal 노드도 Info Panel 포함 (작은 폰트 + 반투명)
        //   false (기본): 특수 노드만 info panel (기존 동작)
        //   true: Normal 노드도 info panel (작은 글자로 과밀 방지)
        public bool showNormalNodeInfo = false;

        // ══════════════════════════════════════════════════════════
        // [Phase 4.5-G Stage 3-F] Passage Info Panel
        // ══════════════════════════════════════════════════════════
        public bool showPassageInfoPanel = false;
        public int passageShowMode = 0;          // 0=Click, 1=CriticalAuto, 2=AllOverlay
        public bool passageRiskFilter = false;   // critical only filter
        public int selectedPassageIdx = -1;      // 클릭 선택된 edge index (-1=없음)
        public HashSet<int> expandedPassageIndices = new HashSet<int>();  // 펼쳐진 passage idx
        // [신규] Per-segment effects 표시 — passageIdx → segmentIdx (-1 = 합집합)
        public Dictionary<int, int> selectedSegmentPerPassage = new Dictionary<int, int>();

        // ══════════════════════════════════════════════════════════
        // [Phase 4.5-B hotfix v4] Biome 라벨 클릭 펼침 기능
        // ══════════════════════════════════════════════════════════

        // [Issue 2] Biome 라벨 클릭 → 포함 노드 목록 펼침
        //   true = biome 라벨을 클릭 가능한 버튼으로 (토글 시 확장)
        public bool showBiomeNodeListPanel = true;

        // 펼쳐진 biome noiseType (라벨 클릭으로 개별 토글)
        public HashSet<int> expandedBiomeNoiseTypes = new HashSet<int>();

        // ══════════════════════════════════════════════════════════
        // [Phase 4.5-B hotfix v5] Biome boundary 색상 + blend 영역 표시
        // ══════════════════════════════════════════════════════════

        // [v5-1] boundary 색상 모드
        //   true = biome 색상 혼합 (기본, 양쪽 biome 색 섞음)
        //   false = 흰색 단색 (v4 기본)
        public bool biomeBoundaryColored = true;

        // [v5-1] Blend (전이) 영역 표시 — blend weight 중간 구간을 별도 색으로 표시
        //   blend weight ∈ [blendTransitionMin, blendTransitionMax] 구간 = 전이 영역
        //   블렌딩이 활발한 zone 시각화
        public bool showBlendTransitionZone = false;

        // [v5-2] Per-biome blend threshold — 각 biome별 전이 영역 폭 조정
        //   key = biome noiseType (0~6)
        //   value = blend extension (0~0.5) — 0.2면 전이영역 = [0.3, 0.7], 0.4면 [0.1, 0.9]
        //   미설정 시 default 0.2 사용
        public Dictionary<int, float> perBiomeBlendExtension = new Dictionary<int, float>();
        public float defaultBlendExtension = 0.2f;

        // ══════════════════════════════════════════════════════════════════
        // [Phase 4.5-D] 제안 4/6/8 지원 필드
        // ══════════════════════════════════════════════════════════════════

        // [제안 6] Seed Sweep 설정
        public int seedSweepCount = 10;     // sweep할 seed 개수 (2~50)

        // [제안 8] Realtime SO Inspector Preview
        //   true = SO 수정 시 자동 Generate (debounce 150ms)
        //   false (기본) = 수동 Generate
        public bool realtimeSOInspector = false;

        // ══════════════════════════════════════════════════════════════════
        // [Phase 4.5-E] MC Preview — SDF Marching Cubes mesh 렌더
        //   enableMCPreview = false (기본) 시 전체 파이프라인 비활성 → byte-identical
        // ══════════════════════════════════════════════════════════════════

        public bool enableMCPreview = false;  // ★ 기본 false (byte-identical 보장)

        // Render mode — wireframe / surface / both
        public PreviewMCRenderMode mcRenderMode = PreviewMCRenderMode.Wireframe;

        // Range 모드
        public PreviewMeshRangeMode mcRangeMode = PreviewMeshRangeMode.SingleRoom;
        public float mcCustomRadius = 50f;   // CustomRadius 모드 전용

        // Voxel 해상도 (미터 단위)
        public float mcVoxelSize = 0.5f;

        // SDF 파라미터
        public float mcSminStrength = 2.0f;
        public float mcFloorAltitude = -5f;
        public float mcCeilAltitude = 15f;
        public float mcFloorVarAmp = 2f;
        public float mcCeilVarAmp = 2f;

        // Feature toggles (v1)
        public bool mcEnableCurvature = true;
        public bool mcEnableFloorVar = true;
        public bool mcEnableBiomeBlend = true;

        // [Phase 4.5-F hotfix v4 + v6] Show Edges 렌더 모드
        public EdgeDisplayMode edgeDisplayMode = EdgeDisplayMode.Line;
        public float edgeMeshSimplifyTolerance = 1.0f;   // [v6] 기본 0.2 → 1.0
        public bool edgeMeshFilterByGraph = true;        // [v6] graph 근접 필터
    }

    public enum CrossSectionAxis
    {
        None = 0,   // top-down (XZ 평면)
        XY = 1,     // Z 고정, XY 단면
        YZ = 2,     // X 고정, YZ 단면
    }

    // ──────────────────────────────────────────────────────────────────
    // 2. FeatureFlags — 시각화 on/off
    // ──────────────────────────────────────────────────────────────────
    [System.Flags]
    public enum FeatureFlags
    {
        None             = 0,
        // Core
        ShowNodes        = 1 << 0,
        ShowEdges        = 1 << 1,
        ShowBiomeHeatmap = 1 << 2,
        ShowBlendWeight  = 1 << 3,
        ShowCardinalVec  = 1 << 4,
        ShowViolations   = 1 << 5,
        ShowMatrixAura   = 1 << 6,
        ShowRoomLabels   = 1 << 7,

        // Extended
        CompareMode      = 1 << 8,
        ShowBiomeDist    = 1 << 9,
        
        // 제안 1/2/3
        ShowCriticalPath = 1 << 10,
        ShowEdgeDirection = 1 << 11,
        ShowNodeClustering = 1 << 12,
        
        // 제안 4/6
        ShowCrossSection = 1 << 13,
        ShowSeedSweep    = 1 << 14,
        
        // 제안 5
        ShowPlayability  = 1 << 15,
    }

    // ──────────────────────────────────────────────────────────────────
    // 3. BiomeSamplePoint — XZ grid 한 셀의 biome 정보
    // ──────────────────────────────────────────────────────────────────
    public class BiomeSamplePoint
    {
        public Vector3 worldPos;
        public int biomeIdxA;
        public int biomeIdxB;
        public float blendWeight;           // smoothstep 적용된 [0, 1]
        public Vector3 cardinalBiasApplied; // 이 지점에 적용된 bias 벡터 합

        // [Phase 4.5-B hotfix v4] Grid 좌표 — Contour tracing용 정확한 인덱싱
        //   worldPos/gridSize 계산은 floating point 오차로 부정확 → 생성 시 그대로 저장
        public int gridIx;
        public int gridIz;

        // [Phase 5 확장 예약 필드]
        //   현재 null/기본값, Phase 5 SubBiome 소비 시 활성화.
        //   확장 패턴: IPreviewExtension.OnGenerateBiomeSample에서 값 설정.
        public int? subBiomeIdx;
        public float? fogDensity;
        public int? oreTypeHint;

        // [Phase 5/6 임의 확장]
        //   IPreviewExtension이 자체 필드 추가 시 이 Dictionary 활용.
        public Dictionary<string, object> extensions = new Dictionary<string, object>();
    }

    // ──────────────────────────────────────────────────────────────────
    // 4. PreviewStats — 통계 집계
    // ──────────────────────────────────────────────────────────────────
    public class PreviewStats
    {
        // Graph 기본
        public int nodeCount;
        public int edgeCount;
        public int spawnCount;
        public int bossCount;
        public int treasureCount;
        public int sinkholeCount;
        public int normalCount;
        public float minEdgeWidth;
        public float maxEdgeSlope;

        // Matrix 적용
        public int matrixAppliedNodeCount;
        public Dictionary<string, int> matrixEntryHits = new Dictionary<string, int>();
        // key = $"rt{rt}_b{biomeType}" → hit count

        // Constraint violation
        public int violationLength;
        public int violationSlope;
        public int violationDirection;

        // Cardinal Bias 영향 (계산만)
        public Dictionary<int, float> cardinalBiasMeanPush = new Dictionary<int, float>();
        // key = biome idx → bias 평균 푸시 거리

        // Biome distribution (전체 XZ 영역)
        public Dictionary<int, float> biomeAreaRatio = new Dictionary<int, float>();
        // key = noiseType → 비율 [0, 1]

        // [제안 1] Critical path
        public int criticalPathEdgeCount;
        public float criticalPathLength;

        // [제안 5] Playability summary (7 불변 조건 체크)
        public PlayabilitySummary playability = new PlayabilitySummary();

        // [제안 6] Seed sweep (여러 seed 통계)
        public SeedSweepResult seedSweep;
    }

    public class PlayabilitySummary
    {
        // 7 불변 조건 체크 — PlayabilityAuditor 통합
        public bool passWidth = true;        // #1
        public bool passSlope = true;         // #2
        public bool passCeiling = true;       // #3 (shader, 가정)
        public bool passCurvatureClamp = true; // #4
        public bool passNavMesh = true;       // #5 (bake 필요)
        public bool passDirection = true;     // #6
        public bool passBiomeTrans = true;    // #7

        public List<string> warnings = new List<string>();
    }

    public class SeedSweepResult
    {
        public int[] seeds;
        public PreviewStats[] statsPerSeed;
        public PreviewStats meanStats;  // 평균
        public PreviewStats worstStats; // 최악값
    }

    // ──────────────────────────────────────────────────────────────────
    // 5. PreviewData — 생성 결과
    // ──────────────────────────────────────────────────────────────────
    public class PreviewData
    {
        public List<NodeData> nodes = new List<NodeData>();
        public List<EdgeData> edges = new List<EdgeData>();
        public List<BiomeSamplePoint> biomeSamples = new List<BiomeSamplePoint>();
        public PreviewStats stats = new PreviewStats();

        // [Phase 4.5-B hotfix v4] Sampler가 auto-adjust한 실제 gridSize 저장
        //   MAX_CELLS clamp로 grid 크기 변경될 수 있음 → renderer 동기화 필수
        public float actualGridSize = 10f;

        // Critical path (NodeGraphBuilder에서 노출 필요 — 현재는 재계산)
        public HashSet<Vector2Int> criticalPathEdges = new HashSet<Vector2Int>();

        // [Phase 4.5-C] Critical edge indices — data.edges 배열 index 기준 (렌더용)
        //   Vector2Int 기반 criticalPathEdges를 edges 배열 index로 매핑한 결과
        public HashSet<int> criticalEdgeIndices = new HashSet<int>();

        // Node 추가 메타 (각 node가 matrix entry 적용됐는지 등)
        public bool[] matrixApplied;  // length == nodes.Count

        // Violation edge 인덱스 (edges 배열 기준)
        public HashSet<int> violationEdges = new HashSet<int>();

        // 생성 타임스탬프 (Realtime 트리거 체크용)
        public System.DateTime generationTime = System.DateTime.Now;
    }

    // ──────────────────────────────────────────────────────────────────
    // 6. IPreviewExtension — 확장 인터페이스 (Phase 5+)
    // ──────────────────────────────────────────────────────────────────
    public interface IPreviewExtension
    {
        /// <summary>UI에 표시할 확장 이름.</summary>
        string DisplayName { get; }

        /// <summary>현재 inputs 상태에서 이 확장이 활성 상태인지 (dep 체크).</summary>
        bool IsActive(PreviewInputs inputs);

        /// <summary>Feature Toggles 섹션에 확장 토글 그리기.</summary>
        void DrawToggleGUI(PreviewInputs inputs);

        /// <summary>Biome sampling 중 각 지점마다 호출. sample에 확장 필드 추가.</summary>
        void OnGenerateBiomeSample(BiomeSamplePoint p, PreviewInputs inputs);

        /// <summary>Scene 렌더 중 확장 overlay 그리기.</summary>
        void OnDrawSceneOverlay(PreviewData data, PreviewInputs inputs);

        /// <summary>Statistics 패널에 확장 통계 표시.</summary>
        void OnDrawStatsPanel(PreviewData data, PreviewInputs inputs);
    }
}
#endif
