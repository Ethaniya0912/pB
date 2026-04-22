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
        // [CaveTerrainConfig] VoxelSize/ChunkSize 중앙화 SO 참조
        // SO 없으면 하위 호환 fallback으로 Inspector 직접 설정 유지
        [Tooltip("CaveTerrainConfig SO (없으면 아래 직접 설정값 사용)")]
        public CaveTerrainConfig terrainConfig;

        // fallback: SO 미설정 시 직접 사용
        [Tooltip("복셀 그리드 크기. terrainConfig 있으면 자동 계산됨.")]
        public int ChunkSize = 64;
        [Tooltip("복셀 월드 크기(m). terrainConfig 있으면 자동 적용됨.")]
        public float VoxelSize = 0.5f;

        // 실제 사용 프로퍼티: SO 우선, 없으면 직접 값 사용
        public int EffectiveChunkSize => terrainConfig != null ? terrainConfig.ChunkSize : ChunkSize;
        public float EffectiveVoxelSize => terrainConfig != null ? terrainConfig.voxelSize : VoxelSize;
        public float ChunkWorldSize => EffectiveChunkSize * EffectiveVoxelSize;
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

        // [P2 1단계 — Coarse-First Loading]
        [Header("P2 — Coarse-First Loading")]
        [Tooltip("ON: Coarse(저해상) 임시 메시를 먼저 생성 → Fine 완료 시 교체. OFF: Fine만 직접 생성 (원본)")]
        public bool enableCoarseFirst = false;
        [Tooltip("Coarse 목표 청크 크기 (voxels). 권장: Fine ChunkSize의 1/2 (기본 64, targetWorldChunkSize=16m 기준 voxelSize 0.25m)")]
        [Min(4)] public int coarseTargetChunkSize = 64;

        // [Fine-First Radius + FOV]
        [Header("Fine-First Priority")]
        [Tooltip("이 반경(청크 단위) 이내의 청크는 Coarse 생략 → Fine 직접 생성 (0=비활성, 기존 Coarse-First 유지)")]
        [Range(0, 5)] public int fineFirstRadius = 0;
        [Tooltip("ON: 카메라 정면 방향 청크도 Fine-First 적용 (FOV 기반)")]
        public bool useFovPriority = false;
        [Tooltip("정면 판정 내적 임계값 (0.3 ≈ 72°, 0.5 ≈ 60°, 0.7 ≈ 45°)")]
        [Range(0f, 0.9f)] public float fovDotThreshold = 0.3f;

        // [N-1] Predictive Pre-generation
        [Header("N-1 — Predictive Pre-generation")]
        [Tooltip("ON: 이동 방향 예측 → viewDistance+N 청크 미리 큐잉 (체감 지연 0). OFF: 기존 큐잉 (원본)")]
        public bool enablePredictiveLoading = false;
        [Tooltip("이동 방향으로 미리 생성할 청크 수 (1~4)")]
        [Range(1, 4)] public int predictAheadChunks = 2;
        [Tooltip("예측에 필요한 최소 속도 (m/s). 정지 시 예측 안 함.")]
        public float predictMinSpeed = 1.0f;

        private Dictionary<Vector3Int, ChunkRequestContext> activeChunks = new Dictionary<Vector3Int, ChunkRequestContext>();
        private Queue<ChunkRequestContext> generationQueue = new Queue<ChunkRequestContext>();
        private Queue<GameObject> chunkPool = new Queue<GameObject>();

        // [P2 1단계] Coarse 전용 상태 — Fine 경로와 완전 격리
        private Dictionary<Vector3Int, GameObject> coarseObjects = new Dictionary<Vector3Int, GameObject>();
        private Queue<Vector3Int> coarsePendingQueue = new Queue<Vector3Int>();
        private HashSet<Vector3Int> coarseRequested = new HashSet<Vector3Int>();

        private Vector3Int lastPlayerChunkPos = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
        private Vector3 _lastPlayerWorldPos; // [N-1] 속도 계산용
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

            // [P2 1단계] Coarse 정리 — Fine 완료된 경우 대응 Coarse 제거
            if (enableCoarseFirst)
                CleanupCompletedCoarse();
        }

        public void ForceGenerateChunksAroundPlayer()
        {
            lastPlayerChunkPos = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            UpdateChunkPositions();
        }

        // [FIX-K] DC Readback 완료를 추적하는 카운터
        // Interlocked: Increment=Dispatch직전, Decrement=onGpuCompleted콜백
        private volatile int _activeGeneratingCount = 0;

        public bool IsInitialGenerationComplete()
        {
            // [FIX-K] DC Readback이 완전히 끝난 뒤에만 Ready 판정
            // [P2 1단계] Coarse-First ON 시 Coarse 큐도 완료 대기
            bool fineDone = generationQueue.Count == 0 && _activeGeneratingCount == 0;
            if (!enableCoarseFirst) return fineDone;
            return fineDone && coarsePendingQueue.Count == 0;
        }

        private void UpdateChunkPositions()
        {
            Vector3 playerPos = CaveManager.Instance.playerTransform.position;
            float chunkWorldSize = ChunkWorldSize; // CaveTerrainConfig 자동 반영

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

            // [Fine-First] 카메라 정면 방향 캐싱 (FOV 우선 판정용)
            Vector3 cameraForward = Vector3.forward;
            if (useFovPriority)
            {
                Camera mainCam = Camera.main;
                if (mainCam != null) cameraForward = mainCam.transform.forward;
            }
            float chunkWorldSizeFF = ChunkWorldSize;

            foreach (var pos in pendingChunks)
            {
                ChunkRequestContext ctx = new ChunkRequestContext
                {
                    ChunkPos = pos,
                    State = ChunkState.Queued
                };
                generationQueue.Enqueue(ctx);
                activeChunks.Add(pos, ctx);

                // [P2 1단계] Coarse-First: 동일 pos에 Coarse 초안도 큐잉
                // [Fine-First] 근거리 또는 정면 청크는 Coarse 생략 → Fine 직접 생성
                if (enableCoarseFirst && !coarseRequested.Contains(pos))
                {
                    bool fineOnly = false;

                    // (A) 반경 체크: Chebyshev distance ≤ fineFirstRadius
                    if (fineFirstRadius > 0)
                    {
                        int dx = Mathf.Abs(pos.x - currentChunkPos.x);
                        int dy = Mathf.Abs(pos.y - currentChunkPos.y);
                        int dz = Mathf.Abs(pos.z - currentChunkPos.z);
                        int chebyshev = Mathf.Max(dx, Mathf.Max(dy, dz));
                        if (chebyshev <= fineFirstRadius)
                            fineOnly = true;
                    }

                    // (B) FOV 정면 체크: 카메라 forward · 방향 > threshold
                    if (!fineOnly && useFovPriority)
                    {
                        Vector3 chunkCenter = new Vector3(pos.x + 0.5f, pos.y + 0.5f, pos.z + 0.5f) * chunkWorldSizeFF;
                        Vector3 toChunk = (chunkCenter - playerPos).normalized;
                        float dot = Vector3.Dot(cameraForward, toChunk);
                        if (dot > fovDotThreshold)
                            fineOnly = true;
                    }

                    if (!fineOnly)
                    {
                        coarsePendingQueue.Enqueue(pos);
                        coarseRequested.Add(pos);
                    }
                }
            }
        }

        private void ProcessLODAndCulling()
        {
            Camera mainCam = Camera.main;
            if (mainCam == null) return;

            GeometryUtility.CalculateFrustumPlanes(mainCam, frustumPlanes);
            Vector3 camPos = mainCam.transform.position;
            float chunkWorldSize = ChunkWorldSize; // CaveTerrainConfig 자동 반영

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
                // [ChunkSeamStitcher] 청크 제거 시 경계 캐시 정리
                ChunkSeamStitcher.Instance?.UnregisterChunk(pos);
                activeChunks.Remove(pos);

                // [P2 1단계] Fine 청크 제거 시 대응 Coarse도 제거 (누수 방지)
                if (enableCoarseFirst)
                    DestroyCoarse(pos);
            }
        }

        // 부분 수정
        private void ProcessGenerationQueue()
        {
            if (CaveManager.Instance != null && CaveManager.Instance.computeDispatcher.IsBusy)
                return;

            // [P2 1단계] Coarse-First 우선 — Coarse 초안이 대기 중이면 먼저 처리
            if (enableCoarseFirst && TryProcessCoarseQueue())
                return;

            if (generationQueue.Count > 0 && chunkPool.Count > 0)
            {
                ChunkRequestContext context = generationQueue.Dequeue();

                if (!activeChunks.ContainsKey(context.ChunkPos) || context.State == ChunkState.Aborted) return;

                GameObject chunkObj = GetFromPool();
                chunkObj.transform.position = new Vector3(context.ChunkPos.x, context.ChunkPos.y, context.ChunkPos.z) * ChunkWorldSize;
                chunkObj.name = $"Chunk_{context.ChunkPos.x}_{context.ChunkPos.y}_{context.ChunkPos.z}";
                chunkObj.SetActive(true);

                MeshFilter mf = chunkObj.GetComponent<MeshFilter>();
                MeshCollider mc = chunkObj.GetComponent<MeshCollider>();
                if (mf != null) mf.sharedMesh = null;
                // [Phase 3-A / FIX-COLLIDER-RACE] Pool에서 꺼낸 chunk의 collider도 명시적 초기화.
                //   ReturnToPool에서 이미 null 처리하지만, 방어 코드로 여기서도 명시.
                //   enableCoarseFirst=false 경로에서 Fine 직접 생성 시에도 이 경로 통과하므로
                //   collider를 "결정적 null" 상태로 만들어 DCMeshBuilder 진입 보장.
                if (mc != null) mc.sharedMesh = null;

                context.ChunkObject = chunkObj;

                // [🔥 핵심 수정: 캐시 매니저 연동]
                // 로비(Headless)에서 구워둔 데이터가 존재한다면 GPU 디스패치를 생략하고 즉시 적용합니다.
                if (TerrainCacheManager.Instance != null &&
                    TerrainCacheManager.Instance.ConsumeCache(context.ChunkPos, out Mesh cachedMesh, out Unity.Collections.NativeArray<CaveOreData> cachedOres))
                {
                    if (mf != null) mf.sharedMesh = cachedMesh;
                    if (mc != null) mc.sharedMesh = cachedMesh;

                    // 캐시에서 가져온 특이점 데이터를 생태계와 스포너에 등록
                    if (CaveEcosystemManager.Instance != null)
                        CaveEcosystemManager.Instance.ProcessEcosystem(cachedOres);

                    if (CaveSystem.Multiplayer.CaveSpawnerManager.Instance != null)
                        CaveSystem.Multiplayer.CaveSpawnerManager.Instance.RegisterSpawnerData(context.ChunkPos, cachedOres);

                    // 사용이 끝난 언매니지드 배열 수동 해제
                    if (cachedOres.IsCreated) cachedOres.Dispose();

                    context.State = ChunkState.Completed;
                    return; // 캐시를 썼으므로 GPU 파견 생략
                }

                // 캐시에 없다면 기존처럼 GPU에 굽기 지시
                context.State = ChunkState.Generating;
                if (CaveManager.Instance != null)
                {
                    // [FIX-K] Dispatch 직전 카운터 증가
                    System.Threading.Interlocked.Increment(ref _activeGeneratingCount);
                    CaveManager.Instance.computeDispatcher.DispatchChunk(
                        context,
                        EffectiveChunkSize,
                        EffectiveVoxelSize,
                        (ctx, tri, ore) =>
                        {
                            // [FIX-K] Readback 완료 시 카운터 감소
                            System.Threading.Interlocked.Decrement(ref _activeGeneratingCount);
                            CaveManager.Instance.HandleGpuResult(ctx, tri, ore);
                        }
                    );
                }
            }
        }

        private GameObject GetFromPool()
        {
            // 1. 풀에 이미 만들어진 청크가 있다면 꺼내서 사용
            if (chunkPool.Count > 0)
            {
                GameObject obj = chunkPool.Dequeue();

                // [안전장치] 풀에서 꺼낸 객체도 렌더러 그림자 설정 강제 활성화 보장
                MeshRenderer existingRenderer = obj.GetComponent<MeshRenderer>();
                if (existingRenderer != null)
                {
                    existingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    existingRenderer.receiveShadows = true;
                }

                return obj;
            }

            // 2. 풀이 비어있다면 프리팹으로부터 새로 생성
            GameObject newObj = Instantiate(ChunkPrefab, transform);

            // [추가] 런타임에 새로 생성된 지형의 그림자 속성을 명시적으로 강제 활성화
            MeshRenderer newRenderer = newObj.GetComponent<MeshRenderer>();
            if (newRenderer != null)
            {
                newRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                newRenderer.receiveShadows = true;
            }

            return newObj;
        }

        private void ReturnToPool(GameObject obj)
        {
            // [P3-NB-A] 풀링 전 pending bake 취소 — 풀 재사용으로 stale 텍스처 방지
            var returningRenderer = obj.GetComponent<MeshRenderer>();
            if (returningRenderer != null && CaveNormalBaker.Instance != null)
                CaveNormalBaker.Instance.CancelPendingBakesForRenderer(returningRenderer);

            // [P3-Phy] 풀링 전 pending physics bake 취소
            var returningCollider = obj.GetComponent<MeshCollider>();
            if (returningCollider != null && DCMeshBuilder.Instance != null)
                DCMeshBuilder.Instance.CancelPendingPhysicsBakesForCollider(returningCollider);

            // [FIX-STALE-CACHE] Stitcher _cache에서 이 chunk 위치 선제 제거.
            //   원래 L314 UnregisterChunk 호출은 LOD cull 경로에만 존재 →
            //   ReturnToPool이 LOD cull 외 경로(예: Fine 도착 시 Coarse 파괴)로 호출되면
            //   Stitcher._cache가 Destroy된 mesh 참조를 그대로 들고 있게 됨.
            //   ReturnToPool 진입 시 반드시 Stitcher cache도 정리해야 stale reference 방지.
            //   name 파싱: "Chunk_x_y_z" 또는 "Coarse_x_y_z" 형식
            Vector3Int? parsedPos = null;
            if (obj != null)
            {
                parsedPos = TryParseChunkPos(obj.name);
            }
            if (ChunkSeamStitcher.Instance != null && parsedPos.HasValue)
            {
                ChunkSeamStitcher.Instance.UnregisterChunk(parsedPos.Value);
            }

            // [S7 / FIX-STALE-SPAWNER] 규칙 #23 확장 적용.
            //   CaveSpawnerManager.chunkSpawnerDataMap / pendingSpawnChunks / deferredChunks에
            //   이 chunk 위치의 데이터가 남아있으면 제거.
            //   이미 스폰된 NetworkObject는 건드리지 않음 (기획 의도 — 플레이어 상호작용 대기).
            if (CaveSystem.Multiplayer.CaveSpawnerManager.Instance != null && parsedPos.HasValue)
            {
                CaveSystem.Multiplayer.CaveSpawnerManager.Instance.UnregisterChunk(parsedPos.Value);
            }

            // [FIX-TEX] 풀링 전 NormalBaker 텍스처 Destroy (누수 방지)
            //   MaterialPropertyBlock에 할당된 _DCNormalMap_X/Y/Z 텍스처는
            //   GameObject가 풀로 반환되어도 GPU 메모리에 영구 존재
            //   → 명시적 Destroy 필요 (청크당 512²×4B×3ch×mipmap ≈ 4MB)
            DestroyDCNormalTextures(returningRenderer);

            obj.name = "Chunk_Pooled";
            obj.SetActive(false);

            MeshFilter mf = obj.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                // [FIX-MESH] DC 파이프라인 메시는 "DCChunk_*" 이름 사용
                //   원본 MC 파이프라인은 "ChunkMesh" 이름 사용
                //   둘 다 매칭하여 Destroy (누수 방지)
                string meshName = mf.sharedMesh.name;
                if (meshName.Contains("ChunkMesh") || meshName.Contains("DCChunk"))
                {
                    Destroy(mf.sharedMesh);
                }
                mf.sharedMesh = null;
            }

            // [Phase 3-A / FIX-COLLIDER-RACE] MeshCollider의 sharedMesh도 명시적 null 처리.
            //   기존 코드는 mf만 정리하고 mc는 방치 → Destroy된 mesh 참조 잔존 →
            //   다음 풀 재사용 시 stale reference로 혼란 가능.
            //   Pool 반환 시점에 완전히 정리하여 "pool에서 꺼낸 직후 mc.sharedMesh 상태"를
            //   결정적(null)으로 만든다.
            //   규칙 #6: 기존 코드는 mc 정리 없음 → 이전 상태는 undefined behavior.
            //           본 수정은 "undefined를 defined(null)로" 변경 → 기존 의존 경로에
            //           영향 없음 (어차피 미정의였던 상태).
            if (returningCollider != null && returningCollider.sharedMesh != null)
            {
                returningCollider.sharedMesh = null;
            }

            chunkPool.Enqueue(obj);
        }

        // =====================================================================
        // [P2 1단계] Coarse-First 헬퍼 (모든 경로에서 enableCoarseFirst 가드됨)
        // =====================================================================

        /// <summary>Coarse 큐에서 하나를 처리. 처리했으면 true 반환 → 이번 프레임 Fine 처리 생략.</summary>
        private bool TryProcessCoarseQueue()
        {
            // 큐 앞쪽 유효성 정리 (이미 Fine 완료 / 청크 제거 / 중복 Coarse)
            while (coarsePendingQueue.Count > 0)
            {
                Vector3Int peekPos = coarsePendingQueue.Peek();

                if (!activeChunks.TryGetValue(peekPos, out var fineCtx)
                    || fineCtx.State == ChunkState.Completed
                    || fineCtx.State == ChunkState.Aborted)
                {
                    coarsePendingQueue.Dequeue();
                    coarseRequested.Remove(peekPos);
                    continue;
                }
                if (coarseObjects.ContainsKey(peekPos))
                {
                    coarsePendingQueue.Dequeue();
                    continue;
                }
                break;
            }

            if (coarsePendingQueue.Count == 0) return false;

            Vector3Int targetPos = coarsePendingQueue.Dequeue();

            // Coarse는 Fine과 월드 사이즈 동일 유지 (중요: 인접 청크 경계 정렬)
            //   ChunkWorldSize = EffectiveChunkSize × EffectiveVoxelSize (Fine 기준)
            //   coarseActualVs = ChunkWorldSize / coarseTargetChunkSize
            //   → 월드 크기 완벽 일치, voxelSize만 증가
            float fineWorld = ChunkWorldSize;
            int coarseN = Mathf.Max(4, coarseTargetChunkSize);
            float coarseVs = fineWorld / coarseN;

            // Coarse 전용 GameObject (풀 사용 안 함 — 별도 생명주기)
            if (ChunkPrefab == null) return false;
            GameObject coarseObj = Instantiate(ChunkPrefab, transform);
            coarseObj.transform.position = new Vector3(targetPos.x, targetPos.y, targetPos.z) * fineWorld;
            // "Coarse_" prefix — DCMeshBuilder가 SeamStitcher skip 판별
            coarseObj.name = $"Coarse_{targetPos.x}_{targetPos.y}_{targetPos.z}";
            coarseObj.SetActive(true);

            MeshRenderer cr = coarseObj.GetComponent<MeshRenderer>();
            if (cr != null)
            {
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                cr.receiveShadows = true;
            }
            // [P2 1단계 v2] Coarse MeshCollider 활성 유지 — 플레이어 physics 공백 방지
            //   이전: Coarse 동안 Fine 기다리느라 collider 없음 → 플레이어 지면 관통 낙하
            //   현재: Coarse collider도 활성 → Fine 도착 전까지 Coarse가 물리 담당
            //         Fine 도착 시 Coarse GameObject 제거로 자동 교체 (CleanupCompletedCoarse)
            MeshCollider cc = coarseObj.GetComponent<MeshCollider>();
            if (cc != null) cc.enabled = true;

            coarseObjects[targetPos] = coarseObj;

            ChunkRequestContext coarseCtx = new ChunkRequestContext
            {
                ChunkPos = targetPos,
                State = ChunkState.Generating,
                ChunkObject = coarseObj
            };

            if (CaveManager.Instance != null)
            {
                System.Threading.Interlocked.Increment(ref _activeGeneratingCount);
                CaveManager.Instance.computeDispatcher.DispatchChunk(
                    coarseCtx,
                    coarseN,
                    coarseVs,
                    (ctx, tri, ore) =>
                    {
                        System.Threading.Interlocked.Decrement(ref _activeGeneratingCount);
                        CaveManager.Instance.HandleGpuResult(ctx, tri, ore);
                    }
                );
            }

            return true;
        }

        /// <summary>Fine 완료된 Coarse 초안 제거 (매 프레임 폴링)</summary>
        /// <remarks>
        /// [FIX-COLLIDER-RACE] Fine collider 유효성 확인 후에만 Coarse 파괴.
        ///   원인: P3-Phy (enablePhysicsBakeAsync) 도입으로 Fine의 context.State=Completed 시점이
        ///         collider.sharedMesh 할당 시점보다 앞설 수 있음. 이 구간 동안 Coarse가 파괴되면
        ///         Fine collider가 아직 null → 플레이어 관통 추락.
        ///   방어: Fine collider가 실제로 mesh를 가진 후에만 toRemove에 추가.
        ///   enablePhysicsBakeAsync=OFF 시: L942 sync 할당으로 조건 즉시 만족 → 기존과 동일 (규칙 #6).
        ///   상세: collider_fallthrough_audit.html 참조.
        /// </remarks>
        private void CleanupCompletedCoarse()
        {
            if (coarseObjects.Count == 0) return;

            List<Vector3Int> toRemove = null;
            foreach (var kvp in coarseObjects)
            {
                Vector3Int pos = kvp.Key;
                if (!activeChunks.TryGetValue(pos, out var fineCtx))
                {
                    // Fine 청크가 LOD cull로 제거됨 → Coarse도 제거
                    (toRemove ??= new List<Vector3Int>()).Add(pos);
                    continue;
                }
                if (fineCtx.State == ChunkState.Completed)
                {
                    // [FIX-COLLIDER-RACE] Fine collider 유효성 체크 추가.
                    //   State=Completed ≠ collider.sharedMesh 할당 완료 (async bake pending 가능).
                    //   Fine collider가 실제로 mesh를 가진 후에만 Coarse 파괴 허용.
                    //   Fine collider 미준비 시 Coarse 유지 (다음 프레임 재시도).
                    var fineCollider = fineCtx.ChunkObject != null
                        ? fineCtx.ChunkObject.GetComponent<MeshCollider>()
                        : null;
                    bool fineColliderReady = fineCollider != null
                                          && fineCollider.sharedMesh != null;
                    if (fineColliderReady)
                    {
                        (toRemove ??= new List<Vector3Int>()).Add(pos);
                    }
                    // else: Fine collider async bake pending → Coarse 물리 담당 유지
                }
            }

            if (toRemove != null)
            {
                foreach (var pos in toRemove)
                    DestroyCoarse(pos);
            }
        }

        /// <summary>Coarse GameObject + 메시 완전 제거 + 상태 추적 해제</summary>
        private void DestroyCoarse(Vector3Int pos)
        {
            if (coarseObjects.TryGetValue(pos, out var obj) && obj != null)
            {
                // [P3-NB-A] 안전망 — Coarse는 진입점에서 skip되므로 보통 pending 없음
                var coarseRenderer = obj.GetComponent<MeshRenderer>();
                if (coarseRenderer != null && CaveNormalBaker.Instance != null)
                    CaveNormalBaker.Instance.CancelPendingBakesForRenderer(coarseRenderer);

                // [P3-Phy] Coarse collider는 sync 경로지만 pending에 들어있을 가능성 대비
                var coarseCollider = obj.GetComponent<MeshCollider>();
                if (coarseCollider != null && DCMeshBuilder.Instance != null)
                    DCMeshBuilder.Instance.CancelPendingPhysicsBakesForCollider(coarseCollider);

                // [FIX-TEX] Coarse 텍스처 Destroy (안전망 — Coarse는 보통 bake skip)
                DestroyDCNormalTextures(coarseRenderer);

                MeshFilter mf = obj.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Destroy(mf.sharedMesh);
                    mf.sharedMesh = null;
                }
                Destroy(obj);
            }
            coarseObjects.Remove(pos);
            coarseRequested.Remove(pos);
        }

        // =====================================================================
        // [FIX-TEX] DC NormalMap 텍스처 Destroy 헬퍼
        //   MaterialPropertyBlock에 할당된 _DCNormalMap_X/Y/Z 텍스처를 명시 해제.
        //   이전에는 청크 반환 시 텍스처만 남아 GPU 메모리 누수 (청크당 ~4MB).
        //   GetPropertyBlock → GetTexture → Destroy → SetPropertyBlock(clear)
        // =====================================================================
        private static readonly string[] _dcNormalMapNames = {
            "_DCNormalMap_X", "_DCNormalMap_Y", "_DCNormalMap_Z"
        };

        private void DestroyDCNormalTextures(MeshRenderer renderer)
        {
            if (renderer == null) return;

            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb);

            bool anyDestroyed = false;
            for (int i = 0; i < _dcNormalMapNames.Length; i++)
            {
                var tex = mpb.GetTexture(_dcNormalMapNames[i]);
                if (tex != null)
                {
                    Destroy(tex);
                    anyDestroyed = true;
                }
            }

            // MPB 초기화 (null 텍스처 참조 방지)
            if (anyDestroyed)
            {
                renderer.SetPropertyBlock(null);
            }
        }

        /// <summary>
        /// [FIX-STALE-CACHE] GameObject 이름에서 chunk 좌표 파싱.
        ///   "Chunk_x_y_z" 또는 "Coarse_x_y_z" 포맷 지원.
        ///   파싱 실패 시 null 반환.
        ///   ReturnToPool에서 Stitcher unregister에 사용.
        /// </summary>
        private static Vector3Int? TryParseChunkPos(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            // "Chunk_Pooled" 같은 풀 이름은 파싱 실패 → null
            // "Chunk_x_y_z" 또는 "Coarse_x_y_z"
            string stripped = name;
            if (stripped.StartsWith("Chunk_")) stripped = stripped.Substring(6);
            else if (stripped.StartsWith("Coarse_")) stripped = stripped.Substring(7);
            else return null;

            var parts = stripped.Split('_');
            if (parts.Length != 3) return null;
            if (int.TryParse(parts[0], out int x)
             && int.TryParse(parts[1], out int y)
             && int.TryParse(parts[2], out int z))
            {
                return new Vector3Int(x, y, z);
            }
            return null;
        }
    }
}