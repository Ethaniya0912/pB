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
            var dcExt = GetComponent<CaveSystem.DCPipelineExtension>();
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