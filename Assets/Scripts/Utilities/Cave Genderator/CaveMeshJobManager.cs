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
        public NativeArray<CaveOreData> OreData; // 완료 시 전달용
        public MeshCollider TargetCollider;
    }

    /// <summary>
    /// 회수된 Native 메모리를 유니티가 렌더링하고 충돌할 수 있는 객체로 가공하는 공장입니다.
    /// </summary>
    public class CaveMeshJobManager : MonoBehaviour
    {
        [Header("Settings")]
        public Material caveMaterial;
        public Material waterMaterial;
        public CaveSettings caveSettings;
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
            // MeshUpdateFlags를 통해 CPU 재연산(Bounds, Indices 검증 등)을 강제 생략하여 메인 스레드 스파이크 방지
            mesh.SetVertexBufferData(vertices, 0, 0, vertexCount, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.SetIndexBufferData(indices, 0, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            // 서브메시 및 바운딩 박스 설정
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, vertexCount), MeshUpdateFlags.DontRecalculateBounds);

            // 최적화된 고정 Bounds 할당 (16x16x16 청크 크기 기준 중앙 배치)
            float halfSize = (chunkSize * voxelSize) * 0.5f;
            Vector3 centerOffset = new Vector3(halfSize, halfSize, halfSize);
            mesh.bounds = new Bounds(centerOffset, new Vector3(chunkSize * voxelSize, chunkSize * voxelSize, chunkSize * voxelSize));

            // 데이터 전달이 끝난 NativeArray 즉시 해제 (메모리 누수 원천 차단)
            vertices.Dispose();
            indices.Dispose();

            // 3. 지형 오브젝트 및 컴포넌트 셋업
            MeshFilter filter = context.ChunkObject.GetComponent<MeshFilter>();
            if (filter == null) filter = context.ChunkObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = context.ChunkObject.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = context.ChunkObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = caveMaterial;

            MeshCollider collider = context.ChunkObject.GetComponent<MeshCollider>();
            if (collider == null) collider = context.ChunkObject.AddComponent<MeshCollider>();

            // 4. 물리 베이킹 비동기 Job 스케줄링 (Unity 6 핵심)
            var bakeJob = new PhysicsBakeJob { meshId = mesh.GetInstanceID() };
            JobHandle bakeHandle = bakeJob.Schedule();

            // 5. 진행 상태 등록 (Update 루프에서 완료 여부 감시)
            activeJobs.Add(new MeshJobContext
            {
                BakeJobHandle = bakeHandle,
                GeneratedMesh = mesh,
                ChunkContext = context,
                OreData = ores,
                TargetCollider = collider
            });

            // 6. 지하 수위(Water Level) 동적 배치 판별
            CheckAndSpawnWaterPlane(context);
        }

        private void CheckAndSpawnWaterPlane(ChunkRequestContext context)
        {
            float chunkWorldY = context.ChunkPos.y * chunkSize * voxelSize;
            float chunkTopY = chunkWorldY + (chunkSize * voxelSize);

            // 현재 청크의 Y축 고도 범위 내에 WaterLevel이 존재하는지 확인
            if (caveSettings.waterLevel >= chunkWorldY && caveSettings.waterLevel <= chunkTopY)
            {
                // 충돌체가 없는 단순 Plane 메시 생성
                GameObject waterObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                waterObj.name = "WaterPlane";
                waterObj.transform.SetParent(context.ChunkObject.transform);

                // 물은 항상 위를 보도록 X축 90도 회전
                waterObj.transform.localRotation = Quaternion.Euler(90, 0, 0);

                // 청크 크기에 맞게 스케일 조정 (청크 중앙 배치)
                float halfSize = (chunkSize * voxelSize) * 0.5f;
                waterObj.transform.localPosition = new Vector3(halfSize, caveSettings.waterLevel - chunkWorldY, halfSize);
                waterObj.transform.localScale = new Vector3(chunkSize * voxelSize, chunkSize * voxelSize, 1f);

                // 불필요한 기본 콜라이더 즉시 제거
                Destroy(waterObj.GetComponent<Collider>());

                MeshRenderer waterRenderer = waterObj.GetComponent<MeshRenderer>();
                waterRenderer.sharedMaterial = waterMaterial;
                waterRenderer.shadowCastingMode = ShadowCastingMode.Off; // 물은 그림자 캐스팅 제외 최적화
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
                    // 취소된 경우 Job을 강제로 끝내고 NativeArray를 정리함
                    jobCtx.BakeJobHandle.Complete();
                    if (jobCtx.OreData.IsCreated) jobCtx.OreData.Dispose();
                    activeJobs.RemoveAt(i);
                    continue;
                }

                if (jobCtx.BakeJobHandle.IsCompleted)
                {
                    jobCtx.BakeJobHandle.Complete();

                    // 물리 트리가 완성된 직후 메인 스레드에 할당해야 스파이크가 발생하지 않음
                    if (jobCtx.TargetCollider != null && jobCtx.GeneratedMesh != null)
                    {
                        jobCtx.TargetCollider.sharedMesh = jobCtx.GeneratedMesh;
                    }

                    // 모든 작업 완료, Callback 트리거
                    externalCallback?.Invoke(jobCtx.ChunkContext, jobCtx.OreData);

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
            }
            activeJobs.Clear();
        }
    }
}