using UnityEngine;
using System.Collections.Generic;
using System;

namespace CaveSystem
{
    /// <summary>
    /// [Phase 7] 동적 청크 로딩 및 오클루전 컬링 매니저.
    /// GPU 연산 요청 시 CaveManager의 HandleGpuResult 콜백을 연결하여 실제 메시 생성을 유도합니다.
    /// </summary>
    public class CaveChunkManager : MonoBehaviour
    {
        [Header("Chunk Settings")]
        public int ChunkSize = 64;
        public float VoxelSize = 0.5f;
        public int ViewDistance = 2;
        public int PhysicsDistance = 1;

        [Header("Optimization")]
        public bool EnableOcclusionCulling = true;
        public LayerMask ChunkLayerMask;

        [Header("Vertical Streaming")]
        public float fallVelocityThreshold = -5.0f;
        public int preLoadBaseDepth = 2;
        public float transitionMargin = 10.0f;

        [Header("Pooling System")]
        public int MaxPoolSize = 100;
        public GameObject ChunkPrefab;

        private Dictionary<Vector3Int, ChunkRequestContext> activeChunks = new Dictionary<Vector3Int, ChunkRequestContext>();
        private Queue<ChunkRequestContext> generationQueue = new Queue<ChunkRequestContext>();
        private Queue<GameObject> chunkPool = new Queue<GameObject>();

        private Vector3Int lastPlayerChunkPos = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
        private bool isInitialized = false;
        private Plane[] frustumPlanes = new Plane[6];

        public void InitializePool()
        {
            if (ChunkPrefab == null)
            {
                Debug.LogError("[CaveChunkManager] Chunk Prefab이 할당되지 않았습니다!");
                return;
            }

            // 기존 풀이 있다면 정리 후 새로 생성
            while (chunkPool.Count > 0)
            {
                GameObject obj = chunkPool.Dequeue();
                if (obj != null) Destroy(obj);
            }

            for (int i = 0; i < MaxPoolSize; i++)
            {
                GameObject obj = Instantiate(ChunkPrefab, transform);
                obj.SetActive(false);
                chunkPool.Enqueue(obj);
            }
            isInitialized = true;
        }

        private void Update()
        {
            if (!isInitialized || CaveManager.Instance == null || CaveManager.Instance.currentState == GlobalSystemState.Initializing)
                return;

            if (CaveManager.Instance.playerTransform == null)
                return;

            UpdateChunkPositions();
            ProcessLODAndCulling();
            ProcessGenerationQueue();
        }

        public void ForceGenerateChunksAroundPlayer()
        {
            lastPlayerChunkPos = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            UpdateChunkPositions();
        }

        public bool IsInitialGenerationComplete()
        {
            return generationQueue.Count == 0;
        }

        private void UpdateChunkPositions()
        {
            Vector3 playerPos = CaveManager.Instance.playerTransform.position;
            float chunkWorldSize = ChunkSize * VoxelSize;

            Vector3Int currentChunkPos = new Vector3Int(
                Mathf.FloorToInt(playerPos.x / chunkWorldSize),
                Mathf.FloorToInt(playerPos.y / chunkWorldSize),
                Mathf.FloorToInt(playerPos.z / chunkWorldSize)
            );

            if (currentChunkPos == lastPlayerChunkPos) return;
            lastPlayerChunkPos = currentChunkPos;

            DepthLayer currentLayer = CaveManager.Instance.biomeSettings.GetLayerSettings(playerPos.y);

            int currentLayerMaxY = Mathf.FloorToInt(currentLayer.maxAltitude / chunkWorldSize);
            int currentLayerMinY = Mathf.FloorToInt(currentLayer.minAltitude / chunkWorldSize);

            Rigidbody playerRb = CaveManager.Instance.playerTransform.GetComponent<Rigidbody>();
            bool isFallingDown = playerRb != null && playerRb.linearVelocity.y < fallVelocityThreshold &&
                                 playerPos.y < currentLayer.minAltitude + transitionMargin;

            if (isFallingDown)
            {
                int dynamicDepth = Mathf.CeilToInt(Mathf.Abs(playerRb.linearVelocity.y) / chunkWorldSize);
                currentLayerMinY -= (preLoadBaseDepth + dynamicDepth);
            }

            List<Vector3Int> pendingChunks = new List<Vector3Int>();

            for (int x = -ViewDistance; x <= ViewDistance; x++)
            {
                for (int z = -ViewDistance; z <= ViewDistance; z++)
                {
                    for (int y = currentLayerMinY - currentChunkPos.y; y <= currentLayerMaxY - currentChunkPos.y; y++)
                    {
                        Vector3Int targetPos = currentChunkPos + new Vector3Int(x, y, z);
                        if (!activeChunks.ContainsKey(targetPos))
                        {
                            pendingChunks.Add(targetPos);
                        }
                    }
                }
            }

            pendingChunks.Sort((a, b) => {
                float distA = (a - currentChunkPos).sqrMagnitude;
                float distB = (b - currentChunkPos).sqrMagnitude;
                return distA.CompareTo(distB);
            });

            foreach (var pos in pendingChunks)
            {
                ChunkRequestContext ctx = new ChunkRequestContext
                {
                    ChunkPos = pos,
                    State = ChunkState.Queued
                };
                generationQueue.Enqueue(ctx);
                activeChunks.Add(pos, ctx);
            }
        }

        private void ProcessLODAndCulling()
        {
            Camera mainCam = Camera.main;
            if (mainCam == null) return;

            GeometryUtility.CalculateFrustumPlanes(mainCam, frustumPlanes);
            Vector3 camPos = mainCam.transform.position;
            float chunkWorldSize = ChunkSize * VoxelSize;

            List<Vector3Int> chunksToRemove = new List<Vector3Int>();

            foreach (var kvp in activeChunks)
            {
                ChunkRequestContext ctx = kvp.Value;
                if (ctx.ChunkObject == null) continue;

                float dist = Vector3.Distance(lastPlayerChunkPos, kvp.Key);

                if (dist > ViewDistance + 1.5f)
                {
                    chunksToRemove.Add(kvp.Key);
                    continue;
                }

                if (EnableOcclusionCulling)
                {
                    Vector3 chunkCenter = new Vector3(kvp.Key.x, kvp.Key.y, kvp.Key.z) * chunkWorldSize + Vector3.one * (chunkWorldSize * 0.5f);
                    Bounds bounds = new Bounds(chunkCenter, Vector3.one * chunkWorldSize);

                    bool isVisible = GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);

                    if (isVisible && dist > 1.5f)
                    {
                        if (Physics.Linecast(camPos, chunkCenter, out RaycastHit hit, ChunkLayerMask))
                        {
                            if (hit.distance < Vector3.Distance(camPos, chunkCenter) - (chunkWorldSize * 0.5f))
                            {
                                isVisible = false;
                            }
                        }
                    }

                    MeshRenderer renderer = ctx.ChunkObject.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.enabled = isVisible || dist <= 1.5f;
                    }
                }

                MeshCollider collider = ctx.ChunkObject.GetComponent<MeshCollider>();
                if (collider != null)
                {
                    collider.enabled = dist <= PhysicsDistance;
                }
            }

            foreach (var pos in chunksToRemove)
            {
                if (activeChunks[pos].ChunkObject != null)
                {
                    ReturnToPool(activeChunks[pos].ChunkObject);
                }
                activeChunks.Remove(pos);
            }
        }

        private void ProcessGenerationQueue()
        {
            // [🔥 Race Condition 조치] 디스패처가 바쁘면(Readback 대기 중이면) 이번 프레임은 스킵합니다.
            if (CaveManager.Instance != null && CaveManager.Instance.computeDispatcher.IsBusy)
                return;

            if (generationQueue.Count > 0 && chunkPool.Count > 0)
            {
                ChunkRequestContext context = generationQueue.Dequeue();

                if (!activeChunks.ContainsKey(context.ChunkPos)) return;

                GameObject chunkObj = GetFromPool();
                chunkObj.transform.position = new Vector3(context.ChunkPos.x, context.ChunkPos.y, context.ChunkPos.z) * (ChunkSize * VoxelSize);
                chunkObj.name = $"Chunk_{context.ChunkPos.x}_{context.ChunkPos.y}_{context.ChunkPos.z}";
                chunkObj.SetActive(true);

                MeshFilter mf = chunkObj.GetComponent<MeshFilter>();
                if (mf != null) mf.sharedMesh = null;

                context.ChunkObject = chunkObj;
                context.State = ChunkState.Generating;

                if (CaveManager.Instance != null)
                {
                    CaveManager.Instance.computeDispatcher.DispatchChunk(
                        context,
                        ChunkSize,
                        VoxelSize,
                        CaveManager.Instance.HandleGpuResult
                    );
                }
            }
        }

        private GameObject GetFromPool()
        {
            if (chunkPool.Count > 0) return chunkPool.Dequeue();

            GameObject newObj = Instantiate(ChunkPrefab, transform);
            return newObj;
        }

        private void ReturnToPool(GameObject obj)
        {
            obj.name = "Chunk_Pooled";
            obj.SetActive(false);

            MeshFilter mf = obj.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                if (mf.sharedMesh.name.Contains("ChunkMesh"))
                {
                    Destroy(mf.sharedMesh);
                }
                mf.sharedMesh = null;
            }

            chunkPool.Enqueue(obj);
        }
    }
}