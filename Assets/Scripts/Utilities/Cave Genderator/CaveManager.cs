using System;
using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

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

            // [🔥 추가: 지형이 다 만들어진 직후 NavMesh를 실시간으로 굽기!]
            if (navMeshSurface != null)
            {
                Debug.Log("NavMesh를 런타임에 굽습니다...");
                //navMeshSurface.BuildNavMesh();
            }

            currentState = GlobalSystemState.Ready;
            Log("<color=cyan>✔️ 시스템 준비 완료. 동굴 탐험을 시작합니다.</color>");
        }


        private IEnumerator UpdateNavMeshRoutine()
        {
            while (true)
            {
                // 3초마다 갱신을 시도합니다 (너무 자주 구우면 프레임이 떨어집니다)
                yield return new WaitForSeconds(3.0f);

                // 현재 지형 생성 작업이 모두 끝났고, 한가할 때만 길찾기 메쉬를 다시 굽습니다.
                // [에러 수정] chunkManager.generationQueue 가 protected 레벨이어서 발생하는 에러를 해결하기 위해,
                // 이미 public으로 구현되어 있는 IsInitialGenerationComplete() 함수를 호출하여 큐가 비어있는지 안전하게 확인합니다.
                if (navMeshSurface != null && chunkManager != null && chunkManager.IsInitialGenerationComplete() && currentState == GlobalSystemState.Ready)
                {
                    // Unity AI Navigation 버전에 따라 BuildNavMeshAsync() 가 있다면 그것을 쓰는 것이 훨씬 좋습니다.
                    navMeshSurface.BuildNavMesh();
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
            // [🔥 State 고아화 방어 1] 비동기 요청 직전 이미 파기된 청크라면 안전하게 종료
            if (context.State == ChunkState.Aborted)
            {
                computeDispatcher.IsBusy = false;
                return;
            }

            ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
            ComputeBuffer.CopyCount(triBuffer, countBuffer, 0);

            AsyncGPUReadback.Request(countBuffer, (countRequest) => {
                // [🔥 State 고아화 방어 2] 첫 번째 Readback 중 청크 파기 시
                if (context.State == ChunkState.Aborted)
                {
                    countBuffer.Release();
                    computeDispatcher.IsBusy = false;
                    return;
                }

                if (countRequest.hasError)
                {
                    UnityEngine.Debug.LogError("[CaveManager] Count Readback Error");
                    countBuffer.Release();
                    computeDispatcher.IsBusy = false;
                    return;
                }

                int triangleCount = countRequest.GetData<int>()[0];
                countBuffer.Release();

                // 삼각형이 하나도 없으면 빈 청크이므로 조기 종료
                if (triangleCount <= 0)
                {
                    context.State = ChunkState.Completed;
                    computeDispatcher.IsBusy = false;
                    return;
                }

                AsyncGPUReadback.Request(triBuffer, (dataRequest) => {
                    // [🔥 State 고아화 방어 3] 정점 Readback 중 청크 파기 시
                    if (context.State == ChunkState.Aborted)
                    {
                        computeDispatcher.IsBusy = false;
                        return;
                    }

                    if (dataRequest.hasError)
                    {
                        UnityEngine.Debug.LogError("[CaveManager] Mesh Data Readback Error");
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
                        // [🔥 State 고아화 방어 4] 광물 카운트 Readback 중 파기 시 (메모리 해제 필수)
                        if (context.State == ChunkState.Aborted)
                        {
                            oreCountBuf.Release();
                            vertices.Dispose(); // 언매니지드 누수 방지
                            computeDispatcher.IsBusy = false;
                            return;
                        }

                        if (oCountReq.hasError)
                        {
                            UnityEngine.Debug.LogError("[CaveManager] Ore Count Readback Error");
                            oreCountBuf.Release();
                            vertices.Dispose();
                            computeDispatcher.IsBusy = false;
                            return;
                        }

                        int oreCount = oCountReq.GetData<int>()[0];
                        oreCountBuf.Release();
                        // 부분 수정 : 광물 데이터가 없더라도 메쉬 데이터는 유효할 수 있으므로, oreCount가 0인 경우에도 메쉬 처리 루틴을 계속 진행합니다.
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
                                    UnityEngine.Debug.LogError("[CaveManager] 치명적 오류: 광물 메타데이터 비동기 리드백 실패.");
                                    vertices.Dispose();
                                    computeDispatcher.IsBusy = false;
                                    return;
                                }

                                NativeArray<CaveOreData> ores = new NativeArray<CaveOreData>(oreCount, Allocator.Persistent);
                                ores.CopyFrom(oDataReq.GetData<CaveOreData>().GetSubArray(0, oreCount));

                                meshJobManager.ProcessMeshJob(context, vertices, ores, (ctx, finalOres) => {
                                    // [🔥 핵심 수정: 시스템 릴레이 연결]
                                    // 물리 베이킹이 끝난 후, 이 데이터를 생태계와 멀티 스포너 매니저에 넘겨줍니다.
                                    if (CaveEcosystemManager.Instance != null)
                                        CaveEcosystemManager.Instance.ProcessEcosystem(finalOres);

                                    if (CaveSystem.Multiplayer.CaveSpawnerManager.Instance != null)
                                        CaveSystem.Multiplayer.CaveSpawnerManager.Instance.RegisterSpawnerData(ctx.ChunkPos, finalOres);

                                    if (finalOres.IsCreated) finalOres.Dispose();
                                    ctx.State = ChunkState.Completed;
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
                            });
                            computeDispatcher.IsBusy = false; // 락 해제
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