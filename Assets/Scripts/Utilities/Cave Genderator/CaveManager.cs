using System;
using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using System.Linq;

namespace CaveSystem
{
    public enum GlobalSystemState
    {
        Initializing,
        LobbyPregenerating,
        LoadingSaveData,
        GeneratingInitialChunks,
        Ready,
        Error
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // [Atomic Preset] BiomeSyncMode — F3 + Stage 3-C/D + I7 + E4 P2 + G2 + J5 통합 토글
    //
    //   목적: F3와 그 다운스트림 (BlendBoost, RoutePlanner, G2, I7, J5)이 mid-state
    //         (일부 ON / 일부 OFF) 진입을 차단. CaveManager 레벨 single source of truth.
    //
    //   Legacy (기본):  Route_Astar baseline 그대로 — 모든 cross-biome 보호 OFF
    //                   byte-identical 보장 (D2 원칙 준수).
    //
    //   GpuAligned:     F3 ON + Stage 3-C ON + BB 0.3 + G2 ON + I7 ON + E4 P2 ON + J5 ON
    //                   E4 P1은 별도 옵션 (Anchor 패턴이 대체).
    //                   사용자 검증 후 default 전환 결정.
    //
    //   E4 P1 (BlendDetailSuppression)은 atomic preset 외부 — Dispatcher Inspector
    //   에서 별도 토글. ON 시 모든 blend 영역 detail 글로벌 약화 (R-VIS 영향).
    //
    //   적용 시점:
    //     CPU측 (GraphBuilder): GenerateGraphInternal 시작 시점 일괄 적용
    //     GPU측 (Dispatcher): 각 Dispatch 호출 시작 시점 일괄 적용
    //     같은 frame 내 atomic 보장 — mid-state 진입 차단.
    // ═══════════════════════════════════════════════════════════════════════════════
    public enum BiomeSyncMode
    {
        /// <summary>Route_Astar baseline 호환 — 모든 cross-biome 보호 OFF, byte-identical D2.</summary>
        Legacy,
        /// <summary>F3 + Stage 3-C + I7 + E4 P2 + BB + G2 + J5 일괄 ON. dual blend 유지. 검증용.</summary>
        GpuAligned,
        /// <summary>★ Single-Source-Ecotone + Soft-Terrace + Centrality-Aware Spawn + Voronoi P1.</summary>
        SingleSourceEcotone,
        /// <summary>
        /// ★ δ — γ의 다음 버전 (rebuild 우선).
        /// γ + 안전 afterf3 인프라 (PassageMetric / RouteGeometry / EdgeData 72B / J5 본체 / Editor).
        /// 위험 afterf3 (RoutePlanner / SpatialHash / Biome-Aware Routing) 토글 default OFF.
        /// 위험 토글 OFF 시 γ와 byte-identical 보장.
        /// </summary>
        SingleSourceEcotonePlus,
        /// <summary>
        /// ★ η — δ + Predictive Width Measurement + 결함 1~4 수정.
        /// PassageSegment 확장 (Single-Source typeA/B 추적, predEffectiveWidth 등),
        /// 4원칙 audit 기반 PredictiveLookupTable, 3-Pass Optimization (Predict→Resolve→Smooth),
        /// width-aware curvature, visitedBiomes 표시.
        /// 모든 측정/조정 토글로 격리 — 토글 OFF 시 δ와 byte-identical 보장.
        /// </summary>
        DebugAware,
        /// <summary>
        /// ★ ε — 합병 버전. <strong>항상 모든 통합 단계 (영구 정책)</strong>.
        /// δ + η + 위험 afterf3 (RoutePlanner / SpatialHash / Biome-Aware Routing) 자동 ON.
        /// 향후 새 기능 추가 시 자동으로 ε에 포함되도록 코드 패턴 유지.
        /// afterf3 fully merged + DebugAware 모든 기능 자동 상속.
        /// </summary>
        FullMerge
    }

    public class CaveManager : MonoBehaviour
    {
        public static CaveManager Instance { get; private set; }

        [Header("Status & Modes")]
        public GlobalSystemState currentState;
        public bool isHeadlessPregenMode = false;

        // NavMesh 동적 갱신을 요청하는 플래그
        [HideInInspector] public bool requestNavMeshUpdate = false;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Atomic Preset] BiomeSyncMode 토글 — single source of truth
        //   기본 Legacy → Route_Astar byte-identical
        //   사용자가 다른 mode 선택 시 GraphBuilder/Dispatcher가 다음 generation부터 적용
        //
        //   7-State Atomic:
        //     Legacy             — Route_Astar 호환 baseline
        //     GpuAligned         — F3 + I7+Anchor + BB + G2 + J5 (dual blend 유지)
        //     SingleSourceEcotone — Single-Source + Soft-Terrace + Centrality-Aware Spawn + Voronoi P1
        //                          (γ — 현재 권장 baseline)
        //     SingleSourceEcotonePlus — γ + 안전 afterf3 (★ δ, rebuild 우선)
        //                              위험 afterf3 (RoutePlanner/SpatialHash) 토글 default OFF
        //                              → γ와 byte-identical 보장
        //     DebugAware         — δ + Predictive Width + 4원칙 lookup + 3-Pass Optimization (★ η)
        //                          모든 prediction/adjustment 토글 default OFF → δ byte-identical
        //     FullMerge          — δ + η + 위험 afterf3 자동 ON (★ ε, 합병 버전, 영구 정책)
        // ═══════════════════════════════════════════════════════════════════════════════
        [Header("Atomic Biome Sync Preset")]
        [Tooltip("7-State Atomic Preset:\n" +
                 "  Legacy (기본): Route_Astar byte-identical, 모든 보호 OFF.\n" +
                 "  GpuAligned: F3+I7+Anchor+BB+G2+J5 ON (dual blend 유지). 검증용.\n" +
                 "  SingleSourceEcotone (γ): Single-Source + Soft-Terrace + Centrality-Aware + Voronoi P1.\n" +
                 "  SingleSourceEcotonePlus (★ δ): γ + 안전 afterf3 인프라.\n" +
                 "                                  위험 afterf3 (RoutePlanner/SpatialHash) 토글 default OFF.\n" +
                 "                                  → γ와 byte-identical (위험 토글 OFF 시).\n" +
                 "  DebugAware (★ η): δ + Predictive Width + 4원칙 lookup + 3-Pass Optimization.\n" +
                 "                    모든 prediction/adjustment 토글 default OFF → δ와 byte-identical.\n" +
                 "  FullMerge (★ ε): δ + η + 위험 afterf3 자동 ON. 항상 모든 통합 (영구 정책).\n" +
                 "주의: 변경은 다음 dungeon generation부터 atomic 적용. mid-state 진입 차단.")]
        public BiomeSyncMode biomeSyncMode = BiomeSyncMode.Legacy;

        public bool IsGpuAligned => biomeSyncMode == BiomeSyncMode.GpuAligned;
        public bool IsSingleSourceEcotone => biomeSyncMode == BiomeSyncMode.SingleSourceEcotone;
        public bool IsSingleSourceEcotonePlus => biomeSyncMode == BiomeSyncMode.SingleSourceEcotonePlus;
        /// <summary>★ η — DebugAware state.</summary>
        public bool IsDebugAware => biomeSyncMode == BiomeSyncMode.DebugAware;
        public bool IsFullMerge => biomeSyncMode == BiomeSyncMode.FullMerge;
        /// <summary>β 또는 γ 또는 δ 또는 η 또는 ε — Legacy가 아닌 모든 enhanced state.</summary>
        public bool IsAnyEnhanced => biomeSyncMode != BiomeSyncMode.Legacy;
        /// <summary>γ 또는 δ 또는 η 또는 ε — Single-Source 활성 state.</summary>
        public bool IsSingleSourceActive =>
            biomeSyncMode == BiomeSyncMode.SingleSourceEcotone ||
            biomeSyncMode == BiomeSyncMode.SingleSourceEcotonePlus ||
            biomeSyncMode == BiomeSyncMode.DebugAware ||
            biomeSyncMode == BiomeSyncMode.FullMerge;
        /// <summary>★ δ 또는 η 또는 ε — afterf3 인프라 (PassageMetric/RouteGeometry/72B EdgeData) 활성.</summary>
        public bool IsDeltaOrLater =>
            biomeSyncMode == BiomeSyncMode.SingleSourceEcotonePlus ||
            biomeSyncMode == BiomeSyncMode.DebugAware ||
            biomeSyncMode == BiomeSyncMode.FullMerge;
        /// <summary>★ η 또는 ε — Predictive Measurement 인프라 활성.
        /// Phase 1: enum + lookup table만 활성. 측정/조정은 Phase 2~5에서 추가.</summary>
        public bool IsDebugAwareOrLater =>
            biomeSyncMode == BiomeSyncMode.DebugAware ||
            biomeSyncMode == BiomeSyncMode.FullMerge;

        [Header("Configuration")]
        public CaveBiomeSettings biomeSettings;
        public GameObject playerPrefab;
        public Transform playerTransform;

        [Header("System References")]
        public CaveSaveLoadSystem saveLoadSystem;
        public CaveChunkManager chunkManager;
        public CaveComputeDispatcher computeDispatcher;
        public CaveNodeGraphBuilder graphBuilder;
        public CaveMeshJobManager meshJobManager;

        [Header("Nav Mesh")]
        public NavMeshSurface navMeshSurface;

        public event Action<float, string> OnPregenProgressUpdated;

        // ═══════════════════════════════════════════════════════════════════════════════
        // [B-6] BiomeData / shader toggle 변경 감지 → Dispatcher에 dirty 신호 전달
        // ═══════════════════════════════════════════════════════════════════════════════
        // 사용 방법 (3가지):
        //   1. 수동: Inspector context menu "Trigger Regen" 호출
        //   2. 자동: BiomeSettings OnValidate에서 Manager.TriggerParamDirty() 호출
        //   3. 런타임: 게임 로직에서 파라미터 변경 시 TriggerParamDirty() 호출
        //
        // 주의:
        //   - Dispatcher의 enableAutoRegeneration=false면 no-op
        //   - paramHash 직접 계산보다 "전체 기록된 chunk → dirty queue" 방식 사용
        //   - DetectDirtyChunks()가 현재 paramHash와 비교하여 변경된 것만 등록
        // ═══════════════════════════════════════════════════════════════════════════════

        [Header("디버깅 — 진단 로그")]
        [Tooltip("ON → DC/MC 콜백 충돌 등 진단 LogWarning 출력.")]
        [SerializeField] private bool _verboseDiagLogging = false;

        [ContextMenu("B-6: Trigger Parameter Dirty (재생성 트리거)")]
        public void TriggerParamDirty()
        {
            if (computeDispatcher == null)
            {
                Debug.LogWarning("[CaveManager] computeDispatcher 참조 없음. B-6 trigger 불가.");
                return;
            }
            if (!computeDispatcher.enableAutoRegeneration)
            {
                Debug.LogWarning("[CaveManager] computeDispatcher.enableAutoRegeneration=false. 토글 ON 필요.");
                return;
            }
            // "변경됨" 신호만 보냄 → Dispatcher가 다음 프레임부터 batch 처리
            // 특정 paramHash 비교 없이 전체 재생성: "__FORCE_ALL__" sentinel 사용
            computeDispatcher.DetectDirtyChunks("__FORCE_ALL__");
            Debug.Log("[CaveManager] B-6: 모든 chunk dirty 마킹 완료. Dispatcher가 batch 단위로 재생성 진행.");
        }

        [ContextMenu("B-10: Log KPI Report")]
        public void TriggerKpiReport()
        {
            if (computeDispatcher != null)
                computeDispatcher.LogKpiReport();
        }

        [ContextMenu("B-10: Reset KPI Stats")]
        public void TriggerKpiReset()
        {
            if (computeDispatcher != null)
                computeDispatcher.ResetKpiStats();
        }
        // ═══════════════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            Instance = this;

            if (isHeadlessPregenMode) DontDestroyOnLoad(this.gameObject);

            currentState = GlobalSystemState.Initializing;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            StartCoroutine(UpdateNavMeshRoutine());
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (isHeadlessPregenMode && scene.buildIndex != 0)
            {
                Log($"[Phase 3] 씬 전환 감지됨({scene.name}). Headless 모드를 해제하고 물리 지형 생성을 시작합니다.");
                isHeadlessPregenMode = false;
                StartCoroutine(InitializeGameModeRoutine());
            }
        }

        private IEnumerator Start()
        {
            if (isHeadlessPregenMode)
            {
                Log("[Pregen] 비가시적 사전 생성 모드로 진입합니다.");
                yield break;
            }

            yield return StartCoroutine(InitializeGameModeRoutine());
        }

        private void Update()
        {
            if (currentState == GlobalSystemState.Ready && playerTransform == null)
            {
                FindAndAssignPlayer();
            }
        }

        private void FindAndAssignPlayer()
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                playerTransform = player.transform;
                Log($"<color=green>✔️ 플레이어 트랜스폼 할당 완료: {player.name}</color>");
            }
            else if (NetworkManager.Singleton != null &&
                     NetworkManager.Singleton.LocalClient != null &&
                     NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                playerTransform = NetworkManager.Singleton.LocalClient.PlayerObject.transform;
                Log($"<color=green>✔️ 네트워크 로컬 플레이어 트랜스폼 할당 완료.</color>");
            }
        }

        #region 🌐 Lobby Headless Pipeline

        public void StartLobbyPregeneration(int seed)
        {
            if (currentState == GlobalSystemState.LobbyPregenerating) return;

            // [hotfix] 의존성 사전 검증 — null이면 명확한 에러 후 조기 반환
            if (!ValidatePregenDependencies())
            {
                Debug.LogError("[CaveManager] Pregen 의존성 누락으로 작업을 시작할 수 없습니다. " +
                               "Inspector에서 graphBuilder/computeDispatcher/chunkManager 참조 확인.");
                currentState = GlobalSystemState.Error;
                return;
            }

            StartCoroutine(PrebakeSpawnAreaRoutine(seed));
        }

        /// <summary>
        /// [hotfix] Runtime 의존성 사전 검증 — 어느 필드가 누락인지 구체적으로 로깅.
        /// Inspector 참조가 null인 경우 Runtime 자동 탐색 (FindFirstObjectByType) 시도.
        /// 이 검증을 통과해야 PrebakeSpawnAreaRoutine 진입 가능.
        /// </summary>
        private bool ValidatePregenDependencies()
        {
            // 1. Inspector 참조 null이면 Runtime 자동 탐색 시도 (Lobby→Dungeon scene 분리 대응)
            if (graphBuilder == null)
            {
                graphBuilder = FindFirstObjectByType<CaveNodeGraphBuilder>();
                if (graphBuilder != null)
                    Log($"<color=yellow>[CaveManager] graphBuilder Runtime 자동 탐색 성공: {graphBuilder.gameObject.name}</color>");
            }
            if (computeDispatcher == null)
            {
                computeDispatcher = FindFirstObjectByType<CaveComputeDispatcher>();
                if (computeDispatcher != null)
                    Log($"<color=yellow>[CaveManager] computeDispatcher Runtime 자동 탐색 성공: {computeDispatcher.gameObject.name}</color>");
            }
            if (chunkManager == null)
            {
                chunkManager = FindFirstObjectByType<CaveChunkManager>();
                if (chunkManager != null)
                    Log($"<color=yellow>[CaveManager] chunkManager Runtime 자동 탐색 성공: {chunkManager.gameObject.name}</color>");
            }

            // 2. 최종 검증 — 자동 탐색 실패 시 에러 로깅
            bool ok = true;
            if (graphBuilder == null)
            {
                Debug.LogError("[CaveManager] graphBuilder 참조 누락 " +
                               "(Inspector 확인 또는 현재 scene에 CaveNodeGraphBuilder 컴포넌트 존재 확인). " +
                               "Lobby→Dungeon scene 분리 상황이면 scene 로드 후 호출되도록 조정 필요.");
                ok = false;
            }
            if (computeDispatcher == null)
            {
                Debug.LogError("[CaveManager] computeDispatcher 참조 누락 " +
                               "(Inspector 확인 또는 현재 scene에 CaveComputeDispatcher 컴포넌트 존재 확인).");
                ok = false;
            }
            if (chunkManager == null)
            {
                Debug.LogError("[CaveManager] chunkManager 참조 누락 " +
                               "(Inspector 확인 또는 현재 scene에 CaveChunkManager 컴포넌트 존재 확인).");
                ok = false;
            }
            if (biomeSettings == null)
            {
                Debug.LogError("[CaveManager] biomeSettings 참조 누락 " +
                               "(Inspector의 Configuration 섹션에서 CaveBiomeSettings asset 할당 필요).");
                ok = false;
            }
            return ok;
        }

        private IEnumerator PrebakeSpawnAreaRoutine(int seed)
        {
            currentState = GlobalSystemState.LobbyPregenerating;
            Log($"[Pregen] 시드 {seed}를 기반으로 설계도 작성을 시작합니다.");

            graphBuilder.GenerateGraph(seed);

            // [hotfix] GenerateGraph 결과 검증
            if (graphBuilder.nodesData == null || graphBuilder.nodesData.Count == 0)
            {
                Debug.LogError($"[CaveManager] GenerateGraph 결과 비정상 — " +
                               $"nodesData={(graphBuilder.nodesData == null ? "null" : "0 nodes")}. " +
                               $"biomeSettings의 dungeonBounds/targetRoomCount 확인.");
                currentState = GlobalSystemState.Error;
                yield break;
            }

            computeDispatcher.SetupGraphBuffers(graphBuilder.nodesData, graphBuilder.edgesData);

            Vector3 spawnPos = graphBuilder.nodesData[0].position;
            float worldChunkSize = chunkManager.ChunkSize * chunkManager.VoxelSize;

            Vector3Int spawnChunkPos = new Vector3Int(
                Mathf.FloorToInt(spawnPos.x / worldChunkSize),
                Mathf.FloorToInt(spawnPos.y / worldChunkSize),
                Mathf.FloorToInt(spawnPos.z / worldChunkSize)
            );

            List<Vector3Int> prebakeTargets = new List<Vector3Int>();
            int range = 2;

            for (int x = -range; x <= range; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -range; z <= range; z++)
                    {
                        prebakeTargets.Add(spawnChunkPos + new Vector3Int(x, y, z));
                    }
                }
            }

            for (int i = 0; i < prebakeTargets.Count; i++)
            {
                Vector3Int targetPos = prebakeTargets[i];
                float progress = (float)i / prebakeTargets.Count;
                OnPregenProgressUpdated?.Invoke(progress, $"지형 데이터 베이킹 중... ({i}/{prebakeTargets.Count})");

                ChunkRequestContext ctx = new ChunkRequestContext
                {
                    ChunkPos = targetPos,
                    State = ChunkState.Queued,
                    // [Approach B / LOD Isolation] Prebake = Coarse preview 경로
                    //   - Ghost Cache RegisterDensity skip (Coarse voxelSize가 Fine buffer를 오염하는 것 방지)
                    //   - Vertex Mirror skip (Coarse vertex는 Fine grid와 snap distance 불일치)
                    //   - Halo Bake skip (voxelSize 불일치 감지 로직에서 어차피 reject됨)
                    //   이후 CaveChunkManager.ProcessGenerationQueue가 Fine voxelSize로 재생성 시
                    //   IsCoarse=false로 생성되어 정상 파이프라인 참여.
                    IsCoarse = true
                };

                yield return new WaitUntil(() => !computeDispatcher.IsBusy);

                computeDispatcher.DispatchChunk(ctx, chunkManager.ChunkSize, chunkManager.VoxelSize, HandleGpuResult);
                yield return null;
            }

            OnPregenProgressUpdated?.Invoke(1.0f, "지형 생성 완료! 게임 시작 가능.");
            currentState = GlobalSystemState.Ready;
            Log("[Pregen] 모든 사전 생성 공정이 완료되었습니다.");
        }

        #endregion

        #region 🎮 Game Play Pipeline

        private IEnumerator InitializeGameModeRoutine()
        {
            Log("<color=cyan>[Phase 4] 게임 씬 진입: 지형 물리화 루틴 시작</color>");

            currentState = GlobalSystemState.LoadingSaveData;
            if (saveLoadSystem != null)
                yield return StartCoroutine(saveLoadSystem.InitializeAndLoadCoroutine());

            if (graphBuilder.nodesData == null || graphBuilder.nodesData.Count == 0)
            {
                int currentSeed = (biomeSettings != null) ? biomeSettings.seed : 12345;
                if (Multiplayer.TerrainSyncNetworkManager.Instance != null && Multiplayer.TerrainSyncNetworkManager.Instance.SyncedWorldSeed.Value != 0)
                {
                    currentSeed = Multiplayer.TerrainSyncNetworkManager.Instance.SyncedWorldSeed.Value;
                }
                Log($"<color=red>⚠️ 설계도 복구 중... (Seed: {currentSeed})</color>");
                graphBuilder.GenerateGraph(currentSeed);
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                if (GameObject.FindWithTag("Player") == null)
                {
                    Vector3 spawnPos = graphBuilder.nodesData[0].position + Vector3.up * 2.5f;

                    if (WorldSaveGameManager.Instance != null && WorldSaveGameManager.Instance.currentCharacterData != null)
                    {
                        var data = WorldSaveGameManager.Instance.currentCharacterData;
                        if (data.xPosition != 0 || data.yPosition != 0)
                        {
                            spawnPos = new Vector3(data.xPosition, data.yPosition + 0.5f, data.zPosition);
                            Log($"📂 세이브 데이터 슬롯의 위치에서 스폰합니다: {spawnPos}");
                        }
                    }

                    if (playerPrefab != null)
                    {
                        Log($"<color=yellow>🚀 플레이어 프리팹 생성을 시작합니다. 위치: {spawnPos}</color>");
                        GameObject playerObj = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
                        NetworkObject netObj = playerObj.GetComponent<NetworkObject>();
                        if (netObj != null)
                        {
                            netObj.SpawnAsPlayerObject(NetworkManager.Singleton.LocalClientId);
                        }
                    }
                    else
                    {
                        LogError("치명적 오류: CaveManager에 Player Prefab이 할당되지 않았습니다!");
                    }
                }
            }

            float searchTimer = 0f;
            while (playerTransform == null && searchTimer < 3.0f)
            {
                FindAndAssignPlayer();
                searchTimer += Time.deltaTime;
                yield return null;
            }

            if (playerTransform == null)
            {
                Log("<color=yellow>⚠️ 플레이어 객체를 찾지 못했습니다. 메인 카메라를 임시 생성 기준으로 설정합니다.</color>");
                if (Camera.main != null) playerTransform = Camera.main.transform;
                else playerTransform = this.transform;
            }

            Log("[Phase 4] GPU 그래프 버퍼 재바인딩...");
            computeDispatcher.SetupGraphBuffers(graphBuilder.nodesData, graphBuilder.edgesData);

            currentState = GlobalSystemState.GeneratingInitialChunks;
            chunkManager.InitializePool();

            Log($"[Phase 4] 현재 위치({playerTransform.position}) 주변 초기 지형 생성을 개시합니다.");
            chunkManager.ForceGenerateChunksAroundPlayer();

            yield return new WaitUntil(() => chunkManager.IsInitialGenerationComplete());

            currentState = GlobalSystemState.Ready;
            Log("<color=cyan>✔️ 시스템 준비 완료. 동굴 탐험을 시작합니다.</color>");

            // 시스템이 준비되면 최초 1회 NavMesh 갱신을 강제 요청합니다.
            requestNavMeshUpdate = true;
        }

        // [🔥 핵심 수정: NavMesh 에러 완벽 차단 로직]
        private IEnumerator UpdateNavMeshRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(2.0f);

                if (requestNavMeshUpdate && navMeshSurface != null && currentState == GlobalSystemState.Ready)
                {
                    requestNavMeshUpdate = false;
                    Log("새로운 청크 지형 감지됨. 안전한 NavMesh 런타임 갱신을 수행합니다...");

                    // 1. 공사(NavMesh 갱신) 전 맵에 존재하는 모든 에이전트(몹)의 활동을 일시 정지하여 에러를 막습니다.
                    UnityEngine.AI.NavMeshAgent[] allAgents = FindObjectsByType<UnityEngine.AI.NavMeshAgent>(FindObjectsSortMode.None);
                    foreach (var agent in allAgents)
                    {
                        if (agent != null && agent.gameObject.activeInHierarchy)
                        {
                            agent.enabled = false;
                        }
                    }

                    // 2. NavMesh 새롭게 굽기 (이 과정에서 기존 길이 지워지고 새로 덮어씌워집니다)
                    navMeshSurface.BuildNavMesh();

                    // 한 프레임 대기하여 엔진이 NavMesh 갱신을 완료할 시간을 줍니다.
                    yield return null;

                    // =========================================================================================
                    // 🚨 [핵심 버그 수정] NavMesh가 완전히 구워진 직후에, 스폰 대기 중이던 몬스터들을 소환합니다!
                    // 이 타이밍에 스폰해야 NavMeshAgent가 길을 찾지 못하고 에러를 뿜는 현상을 원천 차단할 수 있습니다.
                    // =========================================================================================
                    if (CaveSystem.Multiplayer.CaveSpawnerManager.Instance != null)
                    {
                        CaveSystem.Multiplayer.CaveSpawnerManager.Instance.ProcessPendingSpawns();
                    }

                    // 3. 에이전트 재가동 및 바닥 스냅(Snap)
                    // 허공에 떠있거나 살짝 파묻혀 에러를 내던 에이전트들을 새로운 바닥에 찰싹 붙여줍니다.
                    foreach (var agent in allAgents)
                    {
                        if (agent != null && agent.gameObject.activeInHierarchy)
                        {
                            UnityEngine.AI.NavMeshHit hit;
                            // 반경 5m 내의 가장 가까운 유효 NavMesh 바닥을 찾습니다.
                            if (UnityEngine.AI.NavMesh.SamplePosition(agent.transform.position, out hit, 5.0f, UnityEngine.AI.NavMesh.AllAreas))
                            {
                                agent.transform.position = hit.position;
                            }
                            // 안전하게 확보된 길 위에서 에이전트를 다시 켭니다.
                            agent.enabled = true;
                        }
                    }

                    Log("✔️ NavMesh 갱신 및 몬스터 재배치/스폰 완료.");
                }
            }
        }

        #endregion

        #region ⚡ GPU Result Bridge (Readback Logic)

        /// <summary>
        /// GPU에서 비동기로 넘어온 데이터를 콜백 받아 처리합니다.
        /// </summary>
        public void HandleGpuResult(ChunkRequestContext context, ComputeBuffer triBuffer, ComputeBuffer oreBuffer)
        {
            // ═══ [pB-4 Week 0] DC 모드 안전 가드 ═══
            // DC 모드에서는 CaveComputeDispatcher.DispatchChunk() 내부에서 이미 DC 전용 처리를 하고
            // return하므로, 이 콜백이 호출되지 않아야 한다. 만약 호출되면 안전하게 무시.
            var dcExt = computeDispatcher != null ? computeDispatcher.GetComponent<DCPipelineExtension>() : null;
            if (dcExt != null && dcExt.useDualContouring)
            {
                if (_verboseDiagLogging)
                {
                    Debug.LogWarning("[CaveManager] DC 모드에서 MC 콜백이 호출되었습니다. 무시합니다.");
                }
                computeDispatcher.IsBusy = false;
                return;
            }
            // ═══ DC 안전 가드 끝. 아래는 기존 MC 코드 그대로 ═══

            if (context.State == ChunkState.Aborted)
            {
                computeDispatcher.IsBusy = false;
                return;
            }

            ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
            ComputeBuffer.CopyCount(triBuffer, countBuffer, 0);

            AsyncGPUReadback.Request(countBuffer, (countRequest) => {
                if (context.State == ChunkState.Aborted)
                {
                    countBuffer.Release();
                    computeDispatcher.IsBusy = false;
                    return;
                }

                if (countRequest.hasError)
                {
                    countBuffer.Release();
                    computeDispatcher.IsBusy = false;
                    return;
                }

                int triangleCount = countRequest.GetData<int>()[0];
                countBuffer.Release();

                if (triangleCount <= 0)
                {
                    context.State = ChunkState.Completed;
                    computeDispatcher.IsBusy = false;
                    return;
                }

                AsyncGPUReadback.Request(triBuffer, (dataRequest) => {
                    if (context.State == ChunkState.Aborted)
                    {
                        computeDispatcher.IsBusy = false;
                        return;
                    }

                    if (dataRequest.hasError)
                    {
                        computeDispatcher.IsBusy = false;
                        return;
                    }

                    var triData = dataRequest.GetData<CaveTriangle>();
                    int vertexCount = triangleCount * 3;
                    NativeArray<CaveVertex> vertices = new NativeArray<CaveVertex>(vertexCount, Allocator.Persistent);

                    for (int i = 0; i < triangleCount; i++)
                    {
                        vertices[i * 3 + 0] = triData[i].v0;
                        vertices[i * 3 + 1] = triData[i].v1;
                        vertices[i * 3 + 2] = triData[i].v2;
                    }

                    ComputeBuffer oreCountBuf = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
                    ComputeBuffer.CopyCount(oreBuffer, oreCountBuf, 0);

                    AsyncGPUReadback.Request(oreCountBuf, (oCountReq) => {
                        if (context.State == ChunkState.Aborted)
                        {
                            oreCountBuf.Release();
                            vertices.Dispose();
                            computeDispatcher.IsBusy = false;
                            return;
                        }

                        if (oCountReq.hasError)
                        {
                            oreCountBuf.Release();
                            vertices.Dispose();
                            computeDispatcher.IsBusy = false;
                            return;
                        }

                        int oreCount = oCountReq.GetData<int>()[0];
                        oreCountBuf.Release();

                        if (oreCount > 0)
                        {
                            AsyncGPUReadback.Request(oreBuffer, (oDataReq) => {
                                if (context.State == ChunkState.Aborted)
                                {
                                    vertices.Dispose();
                                    computeDispatcher.IsBusy = false;
                                    return;
                                }
                                if (oDataReq.hasError)
                                {
                                    vertices.Dispose();
                                    computeDispatcher.IsBusy = false;
                                    return;
                                }

                                NativeArray<CaveOreData> ores = new NativeArray<CaveOreData>(oreCount, Allocator.Persistent);
                                ores.CopyFrom(oDataReq.GetData<CaveOreData>().GetSubArray(0, oreCount));

                                meshJobManager.ProcessMeshJob(context, vertices, ores, (ctx, finalOres) => {
                                    if (CaveEcosystemManager.Instance != null)
                                        CaveEcosystemManager.Instance.ProcessEcosystem(finalOres);

                                    if (CaveSystem.Multiplayer.CaveSpawnerManager.Instance != null)
                                        CaveSystem.Multiplayer.CaveSpawnerManager.Instance.RegisterSpawnerData(ctx.ChunkPos, finalOres);

                                    if (finalOres.IsCreated) finalOres.Dispose();
                                    ctx.State = ChunkState.Completed;

                                    requestNavMeshUpdate = true;
                                });
                                computeDispatcher.IsBusy = false;
                            });
                        }
                        else
                        {
                            NativeArray<CaveOreData> emptyOres = new NativeArray<CaveOreData>(0, Allocator.Persistent);
                            meshJobManager.ProcessMeshJob(context, vertices, emptyOres, (ctx, finalOres) => {
                                if (finalOres.IsCreated) finalOres.Dispose();
                                ctx.State = ChunkState.Completed;
                                requestNavMeshUpdate = true;
                            });
                            computeDispatcher.IsBusy = false;
                        }
                    });
                });
            });
        }

        #endregion

        public void Log(string msg) => UnityEngine.Debug.Log($"<color=orange>[CaveManager]</color> {msg}");
        public void LogError(string msg) => UnityEngine.Debug.LogError($"<color=orange>[CaveManager]</color> {msg}");

        private void OnDrawGizmos()
        {
            if (playerTransform == null || chunkManager == null) return;

            float worldChunkSize = chunkManager.ChunkSize * chunkManager.VoxelSize;
            Vector3Int currentPos = new Vector3Int(
                Mathf.FloorToInt(playerTransform.position.x / worldChunkSize),
                Mathf.FloorToInt(playerTransform.position.y / worldChunkSize),
                Mathf.FloorToInt(playerTransform.position.z / worldChunkSize)
            );

            Gizmos.color = currentState == GlobalSystemState.Ready ? Color.green : Color.yellow;
            Vector3 center = new Vector3(currentPos.x, currentPos.y, currentPos.z) * worldChunkSize + (Vector3.one * worldChunkSize * 0.5f);
            Gizmos.DrawWireCube(center, Vector3.one * worldChunkSize);
        }
    }
}