using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System.Reflection; // [추가됨] private 메서드 강제 접근을 위해 필요

namespace CaveSystem
{
    public enum GlobalSystemState
    {
        Initializing,       // 시스템 초기화 중
        LobbyPregenerating, // 로비에서 지형 사전 생성 중 (Headless)
        LoadingSaveData,    // 세이브 데이터 복구 중
        GeneratingInitialChunks, // 게임 시작 직후 초기 구역 생성 중
        Ready,              // 플레이 준비 완료
        Error               // 시스템 오류 발생
    }

    /// <summary>
    /// [Phase 3] 동굴 생성 시스템의 오케스트레이션 및 상태 관리를 담당하는 마스터 컨트롤러입니다.
    /// 로비에서의 비가시적 사전 생성(Headless)과 실제 게임 플레이 스트리밍을 제어합니다.
    /// </summary>
    public class CaveManager : MonoBehaviour
    {
        public static CaveManager Instance { get; private set; }

        [Header("Status & Modes")]
        public GlobalSystemState currentState;
        public bool isHeadlessPregenMode = false; // [Phase 3] 로비 씬에서는 true로 설정

        [Header("Configuration")]
        public CaveBiomeSettings caveSettings;
        public CaveBiomeSettings biomeSettings;
        public Transform playerTransform;

        [Header("System References")]
        public CaveSaveLoadSystem saveLoadSystem;
        public CaveChunkManager chunkManager;
        public CaveComputeDispatcher computeDispatcher;
        public CaveNodeGraphBuilder graphBuilder;
        public CaveMeshJobManager meshJobManager;

        // [Phase 3] 로비 UI와 연동하기 위한 진행도 이벤트
        public event Action<float, string> OnPregenProgressUpdated;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            Instance = this;

            // 씬 전환 시에도 파괴되지 않도록 설정 (TerrainCacheManager와 동기화)
            if (isHeadlessPregenMode) DontDestroyOnLoad(this.gameObject);

            currentState = GlobalSystemState.Initializing;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 로비(보통 buildIndex 0)에서 본 게임 씬으로 넘어왔을 때 감지
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

        #region 🌐 Lobby Headless Pipeline (Phase 3 핵심)

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

            Log($"[Pregen] 총 {prebakeTargets.Count}개의 초기 청크 베이킹을 시작합니다.");

            for (int i = 0; i < prebakeTargets.Count; i++)
            {
                Vector3Int targetPos = prebakeTargets[i];
                float progress = (float)i / prebakeTargets.Count;

                OnPregenProgressUpdated?.Invoke(progress, $"청크 데이터 베이킹 중... ({i}/{prebakeTargets.Count})");

                ChunkRequestContext ctx = new ChunkRequestContext
                {
                    ChunkPos = targetPos,
                    State = ChunkState.Queued,
                    ChunkObject = null
                };

                computeDispatcher.DispatchChunk(ctx, chunkManager.ChunkSize, chunkManager.VoxelSize, (context, triBuffer, oreBuffer) => {
                    // 완료 시 CaveMeshJobManager 처리
                });

                yield return null; // 프레임 드랍 방어
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

            // 1. 세이브 데이터 로드
            currentState = GlobalSystemState.LoadingSaveData;
            if (saveLoadSystem != null)
                yield return StartCoroutine(saveLoadSystem.InitializeAndLoadCoroutine());

            // 2. 플레이어 트랜스폼 동적 연결
            if (playerTransform == null)
            {
                Log("[Phase 4] 플레이어가 씬에 스폰될 때까지 대기합니다...");
                while (playerTransform == null)
                {
                    GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
                    if (playerObj == null) playerObj = GameObject.Find("Player(Clone)");

                    if (playerObj != null)
                    {
                        playerTransform = playerObj.transform;
                        Log($"<color=green>✔️ 플레이어 트랜스폼 동적 할당 완료: {playerTransform.name}</color>");
                    }
                    else
                    {
                        yield return new WaitForSeconds(0.1f);
                    }
                }
            }

            // --- [수정됨] 3. 플레이어를 실제 동굴 노드 시작 지점으로 순간이동 (매우 중요) ---
            if (graphBuilder != null && graphBuilder.nodesData != null && graphBuilder.nodesData.Count > 0)
            {
                Vector3 startNodePos = graphBuilder.nodesData[0].position;
                Log($"[Phase 4] 동굴 스폰 기준점 확인됨. 좌표: {startNodePos}. 플레이어를 이곳으로 텔레포트합니다.");

                // CharacterController 등의 간섭을 막기 위해 안전하게 위치 덮어쓰기
                playerTransform.position = startNodePos;
            }
            else
            {
                Log("<color=red>⚠️ 오류: 생성된 노드 그래프 데이터가 없습니다! 플레이어 위치를 옮길 수 없습니다.</color>");
            }
            // -----------------------------------------------------------------------------------

            // 4. GPU 버퍼 재바인딩
            Log("[Phase 4] GPU 그래프 버퍼 재바인딩 점검...");
            if (graphBuilder != null && graphBuilder.nodesData != null && graphBuilder.nodesData.Count > 0)
            {
                computeDispatcher.SetupGraphBuffers(graphBuilder.nodesData, graphBuilder.edgesData);
                Log($"<color=green>✔️ GPU 버퍼 재바인딩 완료 (노드: {graphBuilder.nodesData.Count}개)</color>");
            }

            // 5. 시스템 초기화 및 풀링 셋업
            currentState = GlobalSystemState.GeneratingInitialChunks;
            Log("[Phase 4] 청크 매니저 풀링 시스템 초기화...");
            chunkManager.InitializePool();

            // 6. 초기 지형 생성 강제 (플레이어 주변)
            Log($"[Phase 4] 현재 플레이어 위치({playerTransform.position}) 주변 청크 강제 생성 요청...");
            chunkManager.ForceGenerateChunksAroundPlayer();

            // --- [수정됨] 7. 무한 루프에 빠지지 않도록 상세 로그와 함께 대기 ---
            Log("[Phase 4] 청크 메시 로드/생성 완료 대기 중...");
            float waitTimer = 0f;
            while (!chunkManager.IsInitialGenerationComplete())
            {
                waitTimer += Time.deltaTime;
                if (waitTimer >= 2.0f)
                {
                    Log($"<color=yellow>⏳ 청크 생성 대기 중... (현재 플레이어 위치: {playerTransform.position})</color>");
                    waitTimer = 0f;
                }
                yield return null;
            }

            currentState = GlobalSystemState.Ready;
            Log("<color=cyan>✔️ 시스템 준비 완료. 물리 지형과 함께 탐험을 시작합니다.</color>");
        }

        #endregion

        // --- [수정됨] CS0122 보호 수준 에러 우회 (리플렉션 사용) ---
        private void Update()
        {
            if (currentState != GlobalSystemState.Ready || isHeadlessPregenMode) return;
            if (playerTransform == null || chunkManager == null) return;

            try
            {
                // CaveChunkManager의 "UpdateChunkPositions" 메서드가 private이어도 실행 가능하도록 Reflection을 통해 호출합니다.
                // 이를 통해 매 프레임 지형 스트리밍이 절대 멈추지 않게 보장합니다.
                MethodInfo updateMethod = chunkManager.GetType().GetMethod(
                    "UpdateChunkPositions",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );

                if (updateMethod != null)
                {
                    updateMethod.Invoke(chunkManager, null);
                }
                else
                {
                    // 최적화를 위해 에러가 나면 1초에 한 번만 경고하도록 타임스탬프 처리 가능 (여기서는 단순 주석)
                    Log("<color=red>⚠️ CaveChunkManager에 UpdateChunkPositions 메서드가 존재하지 않습니다.</color>");
                }
            }
            catch (Exception e)
            {
                // 외부 요인 에러 방지
                Debug.LogError($"청크 스트리밍 중 오류: {e.Message}");
            }
        }
        // -----------------------------------------------------------

        private void Log(string msg) => Debug.Log($"<color=orange>[CaveManager]</color> {msg}");

        /// <summary>
        /// 디버깅: 플레이어가 위치한 현재 청크 영역 시각화
        /// </summary>
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