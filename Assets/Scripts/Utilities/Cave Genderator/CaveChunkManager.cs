// Assets/Scripts/Utilities/Cave Genderator/CaveChunkManager.cs
// NRE 방어 로직 및 파라미터 제어 기반 2.5D 레이어 로딩 최적화 적용
using UnityEngine;
using System.Collections.Generic;
using System;

namespace CaveSystem
{
    public class CaveChunkManager : MonoBehaviour
    {
        [Header("Optimization")]
        public bool enableOcclusionCulling = true;

        [Header("Chunk Settings")]
        public int ChunkSize = 16;
        public float VoxelSize = 1.0f;
        public int ViewDistance = 4;
        public int PhysicsDistance = 2;

        [Header("Pooling")]
        public int MaxPoolSize = 500;
        public GameObject ChunkPrefab;

        // [파라미터화: 하드코딩 변수 노출]
        [Header("Vertical Streaming Optimization")]
        [Tooltip("이 속도 이하로 추락하면 하층부 프리로딩을 시작합니다.")]
        public float fallVelocityThreshold = -5.0f;

        [Tooltip("추락 시 기본적으로 미리 생성할 하층부 청크의 깊이(개수)입니다.")]
        public int preLoadBaseDepth = 2;

        [Tooltip("층 전환 시 핑퐁 현상을 막기 위한 여유 거리(m)입니다.")]
        public float transitionMargin = 10.0f;

        private Dictionary<Vector3Int, ChunkRequestContext> activeChunks;
        private Queue<ChunkRequestContext> chunkPool;
        private Queue<Vector3Int> generationQueue;

        private Vector3Int lastPlayerChunkPos;
        private bool isInitialized = false;

        // 동적 데이터 추적 변수
        private int activeLayerIndex = 0;
        private float lastPlayerY = 0f;
        private float playerVelocityY = 0f;

        // [거대 청크 최적화: AABB 컬링] 가비지 생성을 막기 위한 프러스텀 평면 배열 캐싱
        private Plane[] frustumPlanes = new Plane[6];

        public void InitializePool()
        {
            activeChunks = new Dictionary<Vector3Int, ChunkRequestContext>(MaxPoolSize);
            chunkPool = new Queue<ChunkRequestContext>(MaxPoolSize);
            generationQueue = new Queue<Vector3Int>(MaxPoolSize);

            Transform poolRoot = new GameObject("ChunkPoolRoot").transform;
            poolRoot.SetParent(this.transform);

            for (int i = 0; i < MaxPoolSize; i++)
            {
                GameObject obj = Instantiate(ChunkPrefab, poolRoot);
                obj.SetActive(false);

                ChunkRequestContext ctx = new ChunkRequestContext
                {
                    ChunkObject = obj,
                    State = ChunkState.Completed
                };
                chunkPool.Enqueue(ctx);
            }

            lastPlayerChunkPos = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            isInitialized = true;
        }

        public void ForceGenerateChunksAroundPlayer()
        {
            UpdateChunkPositions();
        }

        public bool IsInitialGenerationComplete()
        {
            return generationQueue.Count == 0;
        }

        private void Update()
        {
            if (!isInitialized || CaveManager.Instance == null || CaveManager.Instance.CurrentState == GlobalSystemState.Initializing) return;
            if (CaveManager.Instance.playerTransform == null) return;

            // 플레이어 하강 속도 추적
            float currentPlayerY = CaveManager.Instance.playerTransform.position.y;
            playerVelocityY = (currentPlayerY - lastPlayerY) / Time.deltaTime;
            lastPlayerY = currentPlayerY;

            CheckLayerTransition(currentPlayerY);

            UpdateChunkPositions();
            ProcessGenerationQueue();
            ProcessLODAndCulling();
        }

        // [파라미터화: 동적 바운더리 산출을 위한 헬퍼 함수]
        private DepthLayer GetActiveLayer()
        {
            var layers = CaveManager.Instance.caveSettings.depthLayers;
            if (layers == null || layers.Count == 0) return default;
            int index = Mathf.Clamp(activeLayerIndex, 0, layers.Count - 1);
            return layers[index];
        }

        // 임시 더미 함수: 기획된 노드 데이터를 기반으로 싱크홀 접근 여부 판단
        private bool IsNearSinkhole(Vector3 pos)
        {
            // 추후 CaveNodeGraph 연동 예정
            return false;
        }

        // [파라미터화: 히스테리시스 데드존 적용 및 층 전환]
        private void CheckLayerTransition(float currentY)
        {
            var layers = CaveManager.Instance.caveSettings.depthLayers;
            if (layers == null || layers.Count == 0) return;

            DepthLayer currentLayer = GetActiveLayer();
            float currentLayerFloor = currentLayer.minAltitude;

            // 바닥 기준점보다 'transitionMargin' 이상 완전히 떨어졌을 때만 층 전환 확정 (핑퐁 방지)
            if (currentY < currentLayerFloor - transitionMargin && activeLayerIndex < layers.Count - 1)
            {
                activeLayerIndex++; // 하층부로 전환 확정

                // 이전 층의 멀어진 청크들을 강제로 메모리 풀에 반환
                ForceCleanupUpperLayerChunks(currentLayerFloor);

                Debug.Log($"[CaveChunkManager] Layer Transitioned to Layer {activeLayerIndex} ({layers[activeLayerIndex].layerName})");
            }
        }

        private void ForceCleanupUpperLayerChunks(float transitionAltitude)
        {
            List<Vector3Int> chunksToRemove = new List<Vector3Int>();

            foreach (var kvp in activeChunks)
            {
                float chunkYWorld = kvp.Key.y * ChunkSize * VoxelSize;
                // 전환된 층 기준보다 높은 곳에 있는 청크 색출
                if (chunkYWorld > transitionAltitude)
                {
                    chunksToRemove.Add(kvp.Key);
                }
            }

            foreach (var pos in chunksToRemove)
            {
                ChunkRequestContext ctx = activeChunks[pos];
                ctx.Dispose();

                ctx.ChunkObject.SetActive(false);
                chunkPool.Enqueue(ctx);
                activeChunks.Remove(pos);
            }
        }

        private void UpdateChunkPositions()
        {
            Vector3 playerPos = CaveManager.Instance.playerTransform.position;
            float worldChunkSize = ChunkSize * VoxelSize;

            Vector3Int currentChunkPos = new Vector3Int(
                Mathf.FloorToInt(playerPos.x / worldChunkSize),
                Mathf.FloorToInt(playerPos.y / worldChunkSize),
                Mathf.FloorToInt(playerPos.z / worldChunkSize)
            );

            if (currentChunkPos != lastPlayerChunkPos)
            {
                lastPlayerChunkPos = currentChunkPos;

                DepthLayer currentLayer = GetActiveLayer();
                int currentLayerMaxY = Mathf.FloorToInt(currentLayer.maxAltitude / worldChunkSize);
                int currentLayerMinY = Mathf.FloorToInt(currentLayer.minAltitude / worldChunkSize);

                bool isNearSinkhole = IsNearSinkhole(playerPos);
                float currentLayerFloorY = currentLayer.minAltitude;
                bool isFallingDown = playerPos.y < currentLayerFloorY + 2.0f && playerVelocityY < fallVelocityThreshold;

                if (isNearSinkhole || isFallingDown)
                {
                    int dynamicDepth = Mathf.CeilToInt(Mathf.Abs(playerVelocityY) / worldChunkSize);
                    currentLayerMinY -= (preLoadBaseDepth + dynamicDepth);
                }

                // [거대 청크 최적화: 우선순위 거리 정렬] 임시 리스트에 생성 타겟 좌표 모으기
                List<Vector3Int> pendingChunks = new List<Vector3Int>();

                for (int x = -ViewDistance; x <= ViewDistance; x++)
                {
                    for (int y = currentLayerMinY; y <= currentLayerMaxY; y++)
                    {
                        for (int z = -ViewDistance; z <= ViewDistance; z++)
                        {
                            Vector3Int targetPos = new Vector3Int(currentChunkPos.x + x, y, currentChunkPos.z + z);

                            // 2D 평면상(X, Z)의 거리만 검사하여 로딩 반경 제한
                            Vector3Int xzPlayerPos = new Vector3Int(currentChunkPos.x, 0, currentChunkPos.z);
                            Vector3Int xzTargetPos = new Vector3Int(targetPos.x, 0, targetPos.z);

                            if (Vector3Int.Distance(xzPlayerPos, xzTargetPos) > ViewDistance) continue;

                            if (!activeChunks.ContainsKey(targetPos) && !generationQueue.Contains(targetPos))
                            {
                                pendingChunks.Add(targetPos);
                            }
                        }
                    }
                }

                // [거대 청크 최적화: 우선순위 거리 정렬] 플레이어와 가까운 곳부터 오름차순 정렬
                if (pendingChunks.Count > 0)
                {
                    pendingChunks.Sort((a, b) =>
                    {
                        // 최적화를 위해 Vector3 변환 후 sqrMagnitude 사용 (제곱근 연산 방지)
                        float distA = ((Vector3)(a - currentChunkPos)).sqrMagnitude;
                        float distB = ((Vector3)(b - currentChunkPos)).sqrMagnitude;
                        return distA.CompareTo(distB);
                    });

                    // 정렬된 순서대로 Queue에 삽입하여 항상 발밑과 눈앞부터 생성되게 보장
                    foreach (var pos in pendingChunks)
                    {
                        generationQueue.Enqueue(pos);
                    }
                }
            }
        }

        private void ProcessGenerationQueue()
        {
            if (generationQueue.Count > 0 && chunkPool.Count > 0)
            {
                Vector3Int posToGenerate = generationQueue.Dequeue();

                if (activeChunks.ContainsKey(posToGenerate)) return;

                // 2D 평면 거리 한 번 더 검사 (큐에 있는 동안 플레이어가 멀어졌을 수 있음)
                Vector3Int xzPlayerPos = new Vector3Int(lastPlayerChunkPos.x, 0, lastPlayerChunkPos.z);
                Vector3Int xzTargetPos = new Vector3Int(posToGenerate.x, 0, posToGenerate.z);
                if (Vector3Int.Distance(xzPlayerPos, xzTargetPos) > ViewDistance) return;

                ChunkRequestContext context = chunkPool.Dequeue();
                context.ChunkPos = posToGenerate;
                context.State = ChunkState.Queued;
                context.ChunkObject.name = $"Chunk_{posToGenerate.x}_{posToGenerate.y}_{posToGenerate.z}";
                context.ChunkObject.transform.position = new Vector3(posToGenerate.x, posToGenerate.y, posToGenerate.z) * ChunkSize * VoxelSize;
                context.ChunkObject.SetActive(true);

                activeChunks.Add(posToGenerate, context);
                CaveManager.Instance.computeDispatcher.EnqueueChunk(context);
            }
        }

        private void ProcessLODAndCulling()
        {
            List<Vector3Int> chunksToRemove = new List<Vector3Int>();

            Camera mainCam = Camera.main;
            float worldChunkSize = ChunkSize * VoxelSize;

            // [거대 청크 최적화: AABB 컬링] 메인 카메라 절두체 평면 캐싱 (GC 없음)
            if (enableOcclusionCulling && mainCam != null)
            {
                GeometryUtility.CalculateFrustumPlanes(mainCam, frustumPlanes);
            }

            foreach (var kvp in activeChunks)
            {
                Vector3Int chunkPos = kvp.Key;
                ChunkRequestContext ctx = kvp.Value;

                Vector3Int xzPlayerPos = new Vector3Int(lastPlayerChunkPos.x, 0, lastPlayerChunkPos.z);
                Vector3Int xzChunkPos = new Vector3Int(chunkPos.x, 0, chunkPos.z);

                float distXZ = Vector3Int.Distance(xzPlayerPos, xzChunkPos);

                // XZ 평면 기준으로 멀어지면 언로드 (Hard Unload)
                if (distXZ > ViewDistance + 1)
                {
                    chunksToRemove.Add(chunkPos);
                }
                else
                {
                    MeshRenderer renderer = ctx.ChunkObject.GetComponent<MeshRenderer>();

                    // [거대 청크 최적화: AABB 컬링] 
                    if (enableOcclusionCulling && mainCam != null)
                    {
                        // 청크의 중앙 좌표와 실제 물리적 부피를 갖는 Bounds 생성
                        Vector3 chunkCenterWorld = ctx.ChunkObject.transform.position + new Vector3(worldChunkSize * 0.5f, worldChunkSize * 0.5f, worldChunkSize * 0.5f);
                        Bounds chunkBounds = new Bounds(chunkCenterWorld, Vector3.one * worldChunkSize);

                        // 카메라 프러스텀과 1mm라도 교차하는지 완벽히 검사
                        bool isVisible = GeometryUtility.TestPlanesAABB(frustumPlanes, chunkBounds);

                        // Y축 포함 완벽한 3D 절대 거리 검사
                        float distAbsolute = Vector3Int.Distance(lastPlayerChunkPos, chunkPos);

                        // 예외 축소: 오직 플레이어가 정확히 두 발을 딛고 있는 바로 그 청크(거리 0)만 컬링 면제
                        if (distAbsolute < 1.0f)
                        {
                            if (renderer != null && !renderer.enabled) renderer.enabled = true;
                        }
                        else
                        {
                            // 시야 내/외 여부에 따라 렌더러 즉각 토글
                            if (renderer != null && renderer.enabled != isVisible) renderer.enabled = isVisible;
                        }
                    }
                    else
                    {
                        if (renderer != null && !renderer.enabled) renderer.enabled = true;
                    }

                    // Y축을 포함한 절대 거리 계산 (물리 컬링용)
                    float physDistAbsolute = Vector3Int.Distance(lastPlayerChunkPos, chunkPos);

                    if (physDistAbsolute > PhysicsDistance)
                    {
                        MeshCollider mc = ctx.ChunkObject.GetComponent<MeshCollider>();
                        if (mc != null && mc.enabled) mc.enabled = false;
                    }
                    else
                    {
                        MeshCollider mc = ctx.ChunkObject.GetComponent<MeshCollider>();
                        if (mc != null && !mc.enabled) mc.enabled = true;
                    }
                }
            }

            foreach (var pos in chunksToRemove)
            {
                ChunkRequestContext ctx = activeChunks[pos];
                ctx.Dispose();

                ctx.ChunkObject.SetActive(false);
                chunkPool.Enqueue(ctx);
                activeChunks.Remove(pos);
            }
        }
    }
}