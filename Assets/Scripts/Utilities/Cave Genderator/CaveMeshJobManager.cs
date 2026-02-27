using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using System;
using System.Collections.Generic;

namespace CaveSystem
{
    /// <summary>
    /// 물리 연산용 Job입니다. Unity 6.0의 Physics.BakeMesh를 백그라운드 워커 스레드에서 안전하게 실행합니다.
    /// </summary>
    [BurstCompile]
    public struct PhysicsBakeJob : IJob
    {
        public int meshId;

        public void Execute()
        {
            // 스레드 분산되어 메인 스레드 멈춤 없이 무거운 BVH 트리를 미리 계산합니다.
            Physics.BakeMesh(meshId, false);
        }
    }

    /// <summary>
    /// 마칭 큐브 특성상 인덱스는 0, 1, 2, 3... 순서대로 나열됩니다. 이를 병렬로 초고속 생성합니다.
    /// </summary>
    [BurstCompile]
    public struct GenerateIndicesJob : IJobParallelFor
    {
        [WriteOnly] public NativeArray<int> indices;

        public void Execute(int index)
        {
            indices[index] = index;
        }
    }

    /// <summary>
    /// 진행 중인 Job의 상태를 감시하는 구조체
    /// </summary>
    public class MeshJobContext
    {
        public JobHandle BakeJobHandle;
        public Mesh GeneratedMesh;
        public ChunkRequestContext ChunkContext;

        public NativeArray<CaveOreData> OreData;

        // [Phase 3] TerrainCacheManager 전달을 위해 메모리 해제를 지연시킬 참조 보관
        public NativeArray<CaveVertex> Vertices;
        public NativeArray<int> Indices;

        public MeshCollider TargetCollider;
    }

    /// <summary>
    /// 회수된 Native 메모리를 유니티가 렌더링하고 충돌할 수 있는 객체로 가공하는 공장입니다.
    /// 비가시적(Headless) 모드 시 캐시 매니저로 데이터를 우회시킵니다.
    /// </summary>
    public class CaveMeshJobManager : MonoBehaviour
    {
        [Header("Settings")]
        public Material caveMaterial;
        public Material waterMaterial;
        public CaveBiomeSettings caveSettings;
        public int chunkSize = 16;
        public float voxelSize = 1.0f;

        private List<MeshJobContext> activeJobs = new List<MeshJobContext>();
        private Action<ChunkRequestContext, NativeArray<CaveOreData>> externalCallback;

        // CaveVertex 구조체 레이아웃과 완벽히 일치하는 Descriptor (총 32 bytes)
        private readonly VertexAttributeDescriptor[] vertexLayout = new[]
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2)
        };

        public void ProcessMeshJob(ChunkRequestContext context, NativeArray<CaveVertex> vertices, NativeArray<CaveOreData> ores, Action<ChunkRequestContext, NativeArray<CaveOreData>> onCompleted)
        {
            externalCallback = onCompleted;
            int vertexCount = vertices.Length;

            // 1. 인덱스 배열 생성 (Burst Job)
            NativeArray<int> indices = new NativeArray<int>(vertexCount, Allocator.TempJob);
            var indexJob = new GenerateIndicesJob { indices = indices };
            JobHandle indexHandle = indexJob.Schedule(vertexCount, 64);
            indexHandle.Complete(); // 인덱스 생성은 극도로 빠르므로 여기서 대기해도 무방

            // 2. Unity 6.0 Advanced Mesh API를 이용한 GC Free 메시 할당
            Mesh mesh = new Mesh();
            mesh.name = $"CaveChunk_{context.ChunkPos}";

            // 32비트 인덱스 지원 (수만 개의 정점을 위해 필수)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            // 데이터 레이아웃 파라미터 셋업
            mesh.SetVertexBufferParams(vertexCount, vertexLayout);
            mesh.SetIndexBufferParams(vertexCount, UnityEngine.Rendering.IndexFormat.UInt32);

            // GC 할당 없이 NativeArray의 메모리를 그대로 Mesh에 다이렉트 복사
            mesh.SetVertexBufferData(vertices, 0, 0, vertexCount, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.SetIndexBufferData(indices, 0, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            // 서브메시 및 바운딩 박스 설정
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, vertexCount), MeshUpdateFlags.DontRecalculateBounds);

            // 최적화된 고정 Bounds 할당 (16x16x16 청크 크기 기준 중앙 배치)
            float halfSize = (chunkSize * voxelSize) * 0.5f;
            Vector3 centerOffset = new Vector3(halfSize, halfSize, halfSize);
            mesh.bounds = new Bounds(centerOffset, new Vector3(chunkSize * voxelSize, chunkSize * voxelSize, chunkSize * voxelSize));

            // [Phase 3] 기존의 vertices.Dispose(); indices.Dispose(); 삭제
            // (Headless 모드에서는 데이터를 보존하여 캐시 매니저로 넘겨야 하기 때문입니다)

            MeshCollider collider = null;

            // 3. 지형 오브젝트 셋업 (Headless 모드 분기 처리)
            bool isHeadless = (CaveManager.Instance != null && CaveManager.Instance.isHeadlessPregenMode);

            if (!isHeadless)
            {
                // 일반 게임 모드: 씬의 GameObject에 렌더러와 콜라이더 즉시 할당
                MeshFilter filter = context.ChunkObject.GetComponent<MeshFilter>();
                if (filter == null) filter = context.ChunkObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                MeshRenderer renderer = context.ChunkObject.GetComponent<MeshRenderer>();
                if (renderer == null) renderer = context.ChunkObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = caveMaterial;

                collider = context.ChunkObject.GetComponent<MeshCollider>();
                if (collider == null) collider = context.ChunkObject.AddComponent<MeshCollider>();

                // 지하 수위(Water Level) 동적 배치 판별
                CheckAndSpawnWaterPlane(context);
            }

            // 4. 물리 베이킹 비동기 Job 스케줄링 (Unity 6 핵심)
            var bakeJob = new PhysicsBakeJob { meshId = mesh.GetInstanceID() };
            JobHandle bakeHandle = bakeJob.Schedule();

            // 5. 진행 상태 등록 (메모리 해제를 지연시키기 위해 구조체에 보관)
            activeJobs.Add(new MeshJobContext
            {
                BakeJobHandle = bakeHandle,
                GeneratedMesh = mesh,
                ChunkContext = context,
                OreData = ores,
                Vertices = vertices,
                Indices = indices,
                TargetCollider = collider
            });
        }

        private void CheckAndSpawnWaterPlane(ChunkRequestContext context)
        {
            float chunkWorldY = context.ChunkPos.y * chunkSize * voxelSize;
            float chunkTopY = chunkWorldY + (chunkSize * voxelSize);

            DepthLayer currentLayer = caveSettings.GetLayerSettings(chunkWorldY);

            if (currentLayer.waterLevel >= chunkWorldY && currentLayer.waterLevel <= chunkTopY)
            {
                GameObject waterObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                waterObj.name = "WaterPlane";
                waterObj.transform.SetParent(context.ChunkObject.transform);

                waterObj.transform.localRotation = Quaternion.Euler(90, 0, 0);

                float halfSize = (chunkSize * voxelSize) * 0.5f;
                waterObj.transform.localPosition = new Vector3(halfSize, currentLayer.waterLevel - chunkWorldY, halfSize);
                waterObj.transform.localScale = new Vector3(chunkSize * voxelSize, chunkSize * voxelSize, 1f);

                Destroy(waterObj.GetComponent<Collider>());

                MeshRenderer waterRenderer = waterObj.GetComponent<MeshRenderer>();
                waterRenderer.sharedMaterial = waterMaterial;
                waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        void Update()
        {
            // 진행 중인 백그라운드 베이킹 Job 완료 검사
            for (int i = activeJobs.Count - 1; i >= 0; i--)
            {
                var jobCtx = activeJobs[i];

                if (jobCtx.ChunkContext.State == ChunkState.Aborted)
                {
                    // 취소된 경우 Job 강제 종료 후 모든 NativeArray 수동 해제
                    jobCtx.BakeJobHandle.Complete();
                    if (jobCtx.OreData.IsCreated) jobCtx.OreData.Dispose();
                    if (jobCtx.Vertices.IsCreated) jobCtx.Vertices.Dispose();
                    if (jobCtx.Indices.IsCreated) jobCtx.Indices.Dispose();
                    activeJobs.RemoveAt(i);
                    continue;
                }

                if (jobCtx.BakeJobHandle.IsCompleted)
                {
                    jobCtx.BakeJobHandle.Complete();

                    bool isHeadless = (CaveManager.Instance != null && CaveManager.Instance.isHeadlessPregenMode);

                    if (isHeadless)
                    {
                        // [Phase 3] Headless 로비 모드: 
                        // 메시에 씌우지 않고 TerrainCacheManager로 데이터를 조용히 우회(Bypass)시킵니다.
                        PrecookedChunkData data = new PrecookedChunkData
                        {
                            vertices = jobCtx.Vertices,
                            indices = jobCtx.Indices,
                            oreData = jobCtx.OreData,
                            bakedMesh = jobCtx.GeneratedMesh
                        };

                        if (TerrainCacheManager.Instance != null)
                        {
                            TerrainCacheManager.Instance.AddCache(jobCtx.ChunkContext.ChunkPos, data);
                        }
                        else
                        {
                            Debug.LogWarning("[CaveMeshJobManager] TerrainCacheManager가 존재하지 않아 데이터를 수동 폐기합니다.");
                            if (jobCtx.Vertices.IsCreated) jobCtx.Vertices.Dispose();
                            if (jobCtx.Indices.IsCreated) jobCtx.Indices.Dispose();
                            if (jobCtx.OreData.IsCreated) jobCtx.OreData.Dispose();
                        }
                    }
                    else
                    {
                        // 일반 플레이 모드: 완성된 물리 트리를 콜라이더에 즉시 할당
                        if (jobCtx.TargetCollider != null && jobCtx.GeneratedMesh != null)
                        {
                            jobCtx.TargetCollider.sharedMesh = jobCtx.GeneratedMesh;
                        }

                        // 일반 모드에서는 TerrainCacheManager로 넘기지 않으므로, 여기서 즉시 Dispose
                        if (jobCtx.Vertices.IsCreated) jobCtx.Vertices.Dispose();
                        if (jobCtx.Indices.IsCreated) jobCtx.Indices.Dispose();

                        // 외부 Callback 트리거 (생태계 매니저 등)
                        externalCallback?.Invoke(jobCtx.ChunkContext, jobCtx.OreData);
                    }

                    activeJobs.RemoveAt(i);
                }
            }
        }

        void OnDestroy()
        {
            // 종료 시 대기 중인 모든 Job을 강제 완료시켜 메모리 릭(Leak) 방지
            foreach (var job in activeJobs)
            {
                job.BakeJobHandle.Complete();
                if (job.OreData.IsCreated) job.OreData.Dispose();
                if (job.Vertices.IsCreated) job.Vertices.Dispose();
                if (job.Indices.IsCreated) job.Indices.Dispose();
            }
            activeJobs.Clear();
        }
    }
}