// Assets/Scripts/Utilities/Cave Genderator/CaveChunkManager.cs
// NRE 방어를 위한 Null 체크 로직 추가
using UnityEngine;
using System.Collections.Generic;

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

        private Dictionary<Vector3Int, ChunkRequestContext> activeChunks;
        private Queue<ChunkRequestContext> chunkPool;
        private Queue<Vector3Int> generationQueue;

        private Vector3Int lastPlayerChunkPos;
        private bool isInitialized = false;

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
            // [에러 수정] CaveManager 초기화 전 호출 방어
            if (!isInitialized || CaveManager.Instance == null || CaveManager.Instance.CurrentState == GlobalSystemState.Initializing) return;

            UpdateChunkPositions();
            ProcessGenerationQueue();
            ProcessLODAndCulling();
        }

        private void UpdateChunkPositions()
        {
            // [에러 수정] 플레이어 트랜스폼 참조 방어
            if (CaveManager.Instance.playerTransform == null) return;

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

                for (int x = -ViewDistance; x <= ViewDistance; x++)
                {
                    for (int y = -ViewDistance; y <= ViewDistance; y++)
                    {
                        for (int z = -ViewDistance; z <= ViewDistance; z++)
                        {
                            Vector3Int targetPos = new Vector3Int(currentChunkPos.x + x, currentChunkPos.y + y, currentChunkPos.z + z);

                            if (Vector3Int.Distance(currentChunkPos, targetPos) > ViewDistance) continue;

                            if (!activeChunks.ContainsKey(targetPos) && !generationQueue.Contains(targetPos))
                            {
                                generationQueue.Enqueue(targetPos);
                            }
                        }
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
                if (Vector3Int.Distance(lastPlayerChunkPos, posToGenerate) > ViewDistance) return;

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
            Vector3 camPos = mainCam != null ? mainCam.transform.position : Vector3.zero;
            Vector3 camForward = mainCam != null ? mainCam.transform.forward : Vector3.forward;
            float halfWorldChunkSize = ChunkSize * VoxelSize * 0.5f;

            foreach (var kvp in activeChunks)
            {
                float dist = Vector3Int.Distance(lastPlayerChunkPos, kvp.Key);
                ChunkRequestContext ctx = kvp.Value;

                if (dist > ViewDistance + 1)
                {
                    chunksToRemove.Add(kvp.Key);
                }
                else
                {
                    MeshRenderer renderer = ctx.ChunkObject.GetComponent<MeshRenderer>();

                    if (enableOcclusionCulling && mainCam != null)
                    {
                        Vector3 chunkCenterWorld = ctx.ChunkObject.transform.position + new Vector3(halfWorldChunkSize, halfWorldChunkSize, halfWorldChunkSize);
                        Vector3 dirToChunk = (chunkCenterWorld - camPos).normalized;
                        float dotProduct = Vector3.Dot(camForward, dirToChunk);

                        if (dotProduct < -0.2f && dist > 1)
                        {
                            if (renderer != null && renderer.enabled) renderer.enabled = false;
                        }
                        else
                        {
                            if (renderer != null && !renderer.enabled) renderer.enabled = true;
                        }
                    }
                    else
                    {
                        if (renderer != null && !renderer.enabled) renderer.enabled = true;
                    }

                    if (dist > PhysicsDistance)
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