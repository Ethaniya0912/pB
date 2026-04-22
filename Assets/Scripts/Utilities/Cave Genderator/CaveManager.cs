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

    public class CaveManager : MonoBehaviour
    {
        public static CaveManager Instance { get; private set; }

        [Header("Status & Modes")]
        public GlobalSystemState currentState;
        public bool isHeadlessPregenMode = false;

        // NavMesh 동적 갱신을 요청하는 플래그
        [HideInInspector] public bool requestNavMeshUpdate = false;

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

        // ─────────────────────────────────────────────────────────────
        // [NavMesh 성능 최적화 토글 - Phase 3-A 연장]
        //
        // 문제: navMeshSurface.BuildNavMesh()는 메인 스레드 블로킹 호출.
        //   씬 내 전체 MeshCollider를 source로 재빌드 (27 chunks × 75k tri = 2M tri).
        //   Recast RasterizeTriangles 수 초 소요 → 9.6s 스파이크 (Profiler 확증).
        //   Phase 3-A가 collider를 즉시 유효화 → NavMesh가 완전 source 받음 → 비용 최대화.
        //
        // 두 축 독립 토글:
        //
        //   [Async] enableAsyncNavMeshBuild:
        //     OFF(기본): navMeshSurface.BuildNavMesh() (sync blocking) — 규칙 #6 bit-identical
        //     ON      : NavMeshBuilder.UpdateNavMeshDataAsync (프레임 분산)
        //
        //   [Incremental] enableIncrementalNavMesh:
        //     OFF(기본): 씬 전체 bounds 대상 재빌드
        //     ON      : 변경된 chunk bounds만 update (-90% 비용)
        //
        //   4가지 조합 매트릭스:
        //     Async OFF + Incremental OFF = 기존 (bit-identical) — 규칙 #6 준수
        //     Async ON  + Incremental OFF = 전체 async (frame drop 제거, 총량은 동일)
        //     Async OFF + Incremental ON  = 부분 sync (빠름 but 여전히 블로킹)
        //     Async ON  + Incremental ON  = 부분 async (최적, 권장)
        //
        // 규칙 #6: 두 토글 모두 OFF 시 기존 BuildNavMesh 호출 경로 → bit-identical
        // 규칙 #20: DC/Collider 변경이 NavMesh source 완성도에 영향 → 이번 토글이 대응
        // 규칙 #23: NavMeshData/AsyncOperation 생명주기 매트릭스에 포함
        // ─────────────────────────────────────────────────────────────
        [Header("NavMesh Optimization (Phase 3-A 연장)")]
        [Tooltip("ON: NavMeshBuilder.UpdateNavMeshDataAsync — 프레임 분산 (스파이크 제거). " +
                 "OFF(기본): 기존 navMeshSurface.BuildNavMesh() sync (bit-identical).")]
        public bool enableAsyncNavMeshBuild = false;

        [Tooltip("ON: 변경된 chunk bounds만 재빌드 (-90% 비용). " +
                 "OFF(기본): 전체 씬 bounds. Async 토글과 독립 작동.")]
        public bool enableIncrementalNavMesh = false;

        [Tooltip("Incremental 모드에서 chunk bounds 주변 확장 여유 (m). " +
                 "Agent radius + 경사 연결 필요 공간 포함. 기본 5m.")]
        public float incrementalBoundsExpand = 5.0f;

        // NavMesh async/incremental 내부 상태 (토글 ON 시에만 사용)
        private UnityEngine.AI.NavMeshData _navMeshDataPersistent = null;
        private UnityEngine.AI.NavMeshDataInstance _navMeshDataInstance;
        // AsyncOperation은 UnityEngine 루트 네임스페이스. UnityEngine.AI가 아님.
        // NavMeshBuilder.UpdateNavMeshDataAsync의 반환 타입과 일치.
        private UnityEngine.AsyncOperation _pendingNavMeshOp = null;
        private readonly System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildSource> _navMeshSources
            = new System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildSource>();

        // [Incremental 모드] 어느 chunk가 변경되었는지 추적 (HashSet으로 중복 제거)
        private readonly System.Collections.Generic.HashSet<Vector3Int> _changedChunksForNavMesh
            = new System.Collections.Generic.HashSet<Vector3Int>();

        /// <summary>
        /// [NavMesh] 특정 chunk가 변경되었음을 알림 (Incremental 모드용).
        /// requestNavMeshUpdate=true 대체 호출. 기존 플래그와 함께 사용 가능.
        /// Incremental OFF면 chunk 위치는 무시되고 requestNavMeshUpdate만 세팅.
        /// </summary>
        public void RequestNavMeshUpdateForChunk(Vector3Int chunkPos)
        {
            requestNavMeshUpdate = true;
            if (enableIncrementalNavMesh)
            {
                _changedChunksForNavMesh.Add(chunkPos);
            }
        }

        public event Action<float, string> OnPregenProgressUpdated;

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

            // [Phase 3-A 연장 / 규칙 #23] NavMeshData 등록 해제
            //   OnDestroy가 없었으므로 OnDisable에서 처리 (유효 시점 동일)
            CleanupNavMeshOptimization();
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
            StartCoroutine(PrebakeSpawnAreaRoutine(seed));
        }

        private IEnumerator PrebakeSpawnAreaRoutine(int seed)
        {
            currentState = GlobalSystemState.LobbyPregenerating;
            Log($"[Pregen] 시드 {seed}를 기반으로 설계도 작성을 시작합니다.");

            graphBuilder.GenerateGraph(seed);
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

                ChunkRequestContext ctx = new ChunkRequestContext { ChunkPos = targetPos, State = ChunkState.Queued };

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
        //
        // [Phase 3-A 연장] 4조합 분기 추가:
        //   Async OFF + Incremental OFF = 기존 BuildNavMesh (규칙 #6 bit-identical)
        //   Async ON  + Incremental OFF = 전체 씬 async
        //   Async OFF + Incremental ON  = 부분 sync
        //   Async ON  + Incremental ON  = 부분 async (권장)
        private IEnumerator UpdateNavMeshRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(2.0f);

                if (requestNavMeshUpdate && navMeshSurface != null && currentState == GlobalSystemState.Ready)
                {
                    // 이전 async 작업이 아직 완료되지 않았으면 대기 (race 방지)
                    if (_pendingNavMeshOp != null && !_pendingNavMeshOp.isDone)
                    {
                        continue;
                    }

                    requestNavMeshUpdate = false;

                    // [분기 결정] 4조합 중 어느 경로로 갈 것인가
                    bool useOptimizedPath = enableAsyncNavMeshBuild || enableIncrementalNavMesh;

                    if (!useOptimizedPath)
                    {
                        // ─────────────────────────────────────────────
                        // 경로 A: 기존 BuildNavMesh (규칙 #6 bit-identical)
                        // ─────────────────────────────────────────────
                        yield return StartCoroutine(LegacyBuildNavMeshRoutine());
                    }
                    else
                    {
                        // ─────────────────────────────────────────────
                        // 경로 B: Async/Incremental 최적화 경로
                        // ─────────────────────────────────────────────
                        yield return StartCoroutine(OptimizedBuildNavMeshRoutine());
                    }
                }
            }
        }

        /// <summary>
        /// [기존 경로] navMeshSurface.BuildNavMesh() 메인 스레드 블로킹.
        ///   토글 Async OFF + Incremental OFF 시 사용 — 규칙 #6 bit-identical.
        ///   원본 코드와 동작 동일 (agent 비활성화/재활성화 포함).
        /// </summary>
        private IEnumerator LegacyBuildNavMeshRoutine()
        {
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

        /// <summary>
        /// [최적화 경로] Async/Incremental 조합 처리.
        ///   Async ON  + Incremental OFF: 전체 씬 async
        ///   Async OFF + Incremental ON : 부분 sync
        ///   Async ON  + Incremental ON : 부분 async (권장)
        ///
        /// 기존 방식과의 차이:
        ///   - Agent 비활성화 불필요: UpdateNavMeshData는 기존 NavMeshData를 업데이트하므로
        ///     agent의 path가 갑자기 무효화되지 않음 (BuildNavMesh처럼 교체하는 게 아님).
        ///   - 첫 호출 시 NavMeshData 초기화 + NavMesh.AddNavMeshData 등록
        ///   - 이후 호출은 UpdateNavMeshDataAsync로 같은 data를 갱신
        /// </summary>
        private IEnumerator OptimizedBuildNavMeshRoutine()
        {
            // [첫 호출 초기화] NavMeshData 생성 + scene에 등록
            if (_navMeshDataPersistent == null)
            {
                _navMeshDataPersistent = new UnityEngine.AI.NavMeshData();
                _navMeshDataInstance = UnityEngine.AI.NavMesh.AddNavMeshData(_navMeshDataPersistent);
                Log("[NavMesh 최적화] NavMeshData 초기화 완료");
            }

            // [Bounds 계산] Incremental이면 변경 chunk만, 아니면 전체 씬
            UnityEngine.Bounds bounds;
            if (enableIncrementalNavMesh && _changedChunksForNavMesh.Count > 0)
            {
                bounds = CalculateChangedChunksBounds();
                // 처리 직전 set clear (새 변경이 이후 누적될 수 있도록)
                _changedChunksForNavMesh.Clear();
            }
            else
            {
                // 전체 씬 bounds
                bounds = CalculateSceneBounds();
                // 전체 빌드면 pending chunks도 소비 (의미 없음)
                _changedChunksForNavMesh.Clear();
            }

            // [Source 수집] NavMeshSurface의 설정 재사용
            _navMeshSources.Clear();
            UnityEngine.AI.NavMeshBuilder.CollectSources(
                bounds,
                navMeshSurface.layerMask,
                navMeshSurface.useGeometry,
                navMeshSurface.defaultArea,
                new System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildMarkup>(),
                _navMeshSources
            );

            var settings = UnityEngine.AI.NavMesh.GetSettingsByID(navMeshSurface.agentTypeID);

            if (enableAsyncNavMeshBuild)
            {
                // ───────── 비동기 경로 ─────────
                _pendingNavMeshOp = UnityEngine.AI.NavMeshBuilder.UpdateNavMeshDataAsync(
                    _navMeshDataPersistent, settings, _navMeshSources, bounds);

                // 완료까지 프레임별 양보 (메인 스레드 non-blocking)
                while (_pendingNavMeshOp != null && !_pendingNavMeshOp.isDone)
                {
                    yield return null;
                }
                _pendingNavMeshOp = null;
            }
            else
            {
                // ───────── Incremental + Sync 경로 ─────────
                // bounds가 작으므로 sync라도 부담 훨씬 적음
                UnityEngine.AI.NavMeshBuilder.UpdateNavMeshData(
                    _navMeshDataPersistent, settings, _navMeshSources, bounds);
                yield return null;
            }

            // 스폰 대기 몬스터 처리 (기존 동작 유지)
            if (CaveSystem.Multiplayer.CaveSpawnerManager.Instance != null)
            {
                CaveSystem.Multiplayer.CaveSpawnerManager.Instance.ProcessPendingSpawns();
            }

            // Agent snap은 불필요 — 기존 NavMeshData를 업데이트한 것이라 agent path 유효
            // But 신규 chunk 영역에서는 agent가 표류 중일 수 있음 → 선택적으로 snap
            // (기존 LegacyBuildNavMeshRoutine과 달리 agent 비활성화가 아니었으므로 위험 낮음)

            Log($"[NavMesh 최적화] 갱신 완료 — Async={enableAsyncNavMeshBuild}, " +
                $"Incremental={enableIncrementalNavMesh}, Sources={_navMeshSources.Count}, " +
                $"Bounds={bounds.size}");
        }

        /// <summary>
        /// [Incremental] 변경된 chunk들의 합쳐진 bounds 계산.
        /// Agent radius + 경사 연결 여유 포함.
        /// </summary>
        private UnityEngine.Bounds CalculateChangedChunksBounds()
        {
            if (_changedChunksForNavMesh.Count == 0 || chunkManager == null)
            {
                return CalculateSceneBounds();
            }

            float chunkWorldSize = chunkManager.ChunkWorldSize;
            bool first = true;
            UnityEngine.Bounds result = new UnityEngine.Bounds();

            foreach (var chunkPos in _changedChunksForNavMesh)
            {
                UnityEngine.Vector3 chunkMin = new UnityEngine.Vector3(
                    chunkPos.x, chunkPos.y, chunkPos.z) * chunkWorldSize;
                UnityEngine.Vector3 chunkMax = chunkMin
                    + UnityEngine.Vector3.one * chunkWorldSize;
                UnityEngine.Bounds chunkBounds = new UnityEngine.Bounds();
                chunkBounds.SetMinMax(chunkMin, chunkMax);

                if (first)
                {
                    result = chunkBounds;
                    first = false;
                }
                else
                {
                    result.Encapsulate(chunkBounds);
                }
            }

            // Agent radius + 경사 연결 여유
            result.Expand(incrementalBoundsExpand * 2f);
            return result;
        }

        /// <summary>
        /// [Fallback] 씬 전체 bounds 계산. Incremental OFF 또는 첫 호출 시.
        /// </summary>
        private UnityEngine.Bounds CalculateSceneBounds()
        {
            // 크게 잡되 합리적인 상한
            if (navMeshSurface != null && navMeshSurface.navMeshData != null)
            {
                return navMeshSurface.navMeshData.sourceBounds;
            }
            // Fallback: 원점 기준 넉넉한 bounds (1km³)
            return new UnityEngine.Bounds(UnityEngine.Vector3.zero,
                UnityEngine.Vector3.one * 1000f);
        }

        /// <summary>
        /// [정리] OnDestroy 시 NavMeshData 등록 해제 — 규칙 #23 생명주기 준수.
        /// </summary>
        private void CleanupNavMeshOptimization()
        {
            if (_navMeshDataInstance.valid)
            {
                _navMeshDataInstance.Remove();
            }
            _navMeshDataPersistent = null;
            _pendingNavMeshOp = null;
            _navMeshSources.Clear();
            _changedChunksForNavMesh.Clear();
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
                Debug.LogWarning("[CaveManager] DC 모드에서 MC 콜백이 호출되었습니다. 무시합니다.");
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

                                    // [Incremental NavMesh] chunk pos 전달 — Incremental OFF 시
                                    // 내부적으로 requestNavMeshUpdate만 세팅 (기존 동작 유지).
                                    RequestNavMeshUpdateForChunk(ctx.ChunkPos);
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
                                // [Incremental NavMesh] chunk pos 전달
                                RequestNavMeshUpdateForChunk(ctx.ChunkPos);
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