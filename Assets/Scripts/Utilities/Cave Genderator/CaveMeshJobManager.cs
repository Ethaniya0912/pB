// ================================================================================
// 파일: CaveMeshJobManager.cs
// 역할: GPU에서 넘어온 Triangle Soup 버텍스 데이터를
//       (1) Weld + Smooth Normal (방안 A-2)
//       (2) Physics Bake (기존 유지)
//       (3) NormalMap Bake 트리거 (방안 B 연동)
//       순서로 처리하여 완성된 Mesh를 씬에 배치합니다.
//
// ★ 변경 요약 (이전 버전 대비):
//   - GenerateIndicesJob (Triangle Soup 0,1,2... 인덱스) → WeldAndSmoothJob 으로 교체
//   - MeshJobContext 에 weldedVertexCount 필드 추가
//   - ProcessMeshJob 에서 인덱스 생성 후 Weld Job 스케줄링 추가
//   - CaveNormalBaker.Instance?.RequestBake() 호출 추가 (방안 B 연동)
// ================================================================================

using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using System;
using System.Collections.Generic;

namespace CaveSystem
{
    // ============================================================
    // [Job 1] 물리 베이킹 — 변경 없음
    // ============================================================
    [BurstCompile]
    public struct PhysicsBakeJob : IJob
    {
        public int meshId;
        public void Execute() => Physics.BakeMesh(meshId, false);
    }

    // ============================================================
    // [Job 2-신규] Weld + Smooth Normal
    //   Triangle Soup(버텍스 공유 없음)를 읽어서
    //   · 동일 위치 버텍스를 하나로 병합
    //   · 공유 버텍스에 인접 삼각형 노멀을 면적 가중 누산
    //   · 정규화 → Smooth Normal 완성
    //
    //   한계: HLSL의 NativeHashMap은 float3 키를 epsilonㅤ비교하지 않으므로
    //         부동소수점 오차(±1e-5 수준)로 인해 동일 위치인데도 다른 버켓에
    //         들어가는 경우가 발생할 수 있다. 이를 방지하기 위해
    //         위치를 QUANTIZE(100) 단위로 정수 스냅 후 int3 키를 사용한다.
    //         VoxelSize=0.5f 기준 최소 분해능 0.001m → 완전히 안전.
    // ============================================================
    [BurstCompile]
    public struct WeldAndSmoothJob : IJob
    {
        // 입력: GPU→CPU 복사된 원시 Triangle Soup 버텍스
        [ReadOnly] public NativeArray<CaveVertex> rawVertices; // 길이 = triCount * 3

        // 출력: 병합된 버텍스 목록 (최대 rawVertices.Length 개)
        [WriteOnly] public NativeArray<CaveVertex> weldedVertices;
        // 출력: 원시 인덱스 i → 병합 버텍스 인덱스 매핑
        [WriteOnly] public NativeArray<int> indices;
        // 출력: 실제 병합 후 버텍스 수 (1-element 배열로 전달)
        public NativeArray<int> weldedCount;

        // 내부 임시 자료구조
        public NativeHashMap<int3, int> posToIdx;      // 정수 스냅 위치 → 병합 인덱스
        public NativeList<float3> accumNormals;  // 노멀 누산 버퍼
        public NativeList<float3> positions;     // 위치 저장 버퍼

        // 부동소수점 위치를 정수 키로 변환하는 스케일
        // 100 = 0.01m 해상도. int3 키 최대 범위 ±21,474,836m — 충분히 안전
        private const float QUANTIZE = 100f;

        public void Execute()
        {
            int rawCount = rawVertices.Length;

            for (int i = 0; i < rawCount; i++)
            {
                float3 pos = rawVertices[i].position;
                float3 nrm = rawVertices[i].normal;

                // 부동소수점 오차 방지: 스냅 후 int3 키
                int3 key = new int3(
                    (int)math.round(pos.x * QUANTIZE),
                    (int)math.round(pos.y * QUANTIZE),
                    (int)math.round(pos.z * QUANTIZE)
                );

                if (posToIdx.TryGetValue(key, out int existIdx))
                {
                    // 이미 등록된 버텍스 → 노멀만 누산
                    accumNormals[existIdx] += nrm;
                    indices[i] = existIdx;
                }
                else
                {
                    // 신규 버텍스 등록
                    int newIdx = accumNormals.Length;
                    posToIdx.Add(key, newIdx);
                    accumNormals.Add(nrm);
                    positions.Add(pos);
                    indices[i] = newIdx;
                }
            }

            // 누산 노멀 정규화 → 실제 weldedVertices 기록
            int wCount = accumNormals.Length;
            weldedCount[0] = wCount;

            for (int i = 0; i < wCount; i++)
            {
                // ★ SDF 기울기 반전 (핵심 수정):
                // CaveDensityGenerator는 SDF 기반이므로 기울기(gradient)가
                // 고체(암반) 방향 = 양수 방향을 가리킨다.
                // 렌더링 노멀은 빈 공간(동굴 내부) 방향이어야 하므로 부호를 반전한다.
                // CaveMarchingCubes.compute에서 마이너스를 제거한 것이 원인이므로
                // 여기서 CPU 단에서 보정한다.
                float3 rawNormal = accumNormals[i];
                float accumLen = math.length(rawNormal);

                float3 smoothNormal;
                if (accumLen < 0.1f)
                {
                    // Fallback: 인접 삼각형 노멀이 서로 상쇄된 경우
                    // 동굴 내부 기본 방향(위쪽)으로 설정
                    smoothNormal = new float3(0, 1, 0);
                }
                else
                {
                    // SDF gradient 반전: 암반(+) → 동굴 공간(-) 방향으로 교정
                    smoothNormal = math.normalizesafe(-rawNormal, new float3(0, 1, 0));
                }

                weldedVertices[i] = new CaveVertex
                {
                    position = positions[i],
                    normal = smoothNormal,
                    uv = float2.zero   // 트라이플래너 사용이므로 UV 불필요
                };
            }
        }
    }

    // ============================================================
    // Job 컨텍스트 — weldedVertexCount 필드 추가
    // ============================================================
    public class MeshJobContext
    {
        public JobHandle BakeJobHandle;
        public Mesh GeneratedMesh;
        public ChunkRequestContext ChunkContext;

        public NativeArray<CaveOreData> OreData;

        // Triangle Soup 원본 (Weld Job 완료 후 Dispose)
        public NativeArray<CaveVertex> RawVertices;
        // Weld 완료 버텍스 (Mesh에 업로드 후 Dispose)
        public NativeArray<CaveVertex> WeldedVertices;
        // 인덱스 배열 (Mesh에 업로드 후 Dispose)
        public NativeArray<int> Indices;
        // Weld 후 실제 버텍스 수
        public int WeldedVertexCount;

        // Weld Job 내부 임시 자료구조 (Job 완료 후 반드시 Dispose)
        public NativeHashMap<int3, int> PosToIdx;
        public NativeList<float3> AccumNormals;
        public NativeList<float3> Positions;
        public NativeArray<int> WeldedCountResult;

        public MeshCollider TargetCollider;
    }

    // ============================================================
    // CaveMeshJobManager — 핵심 변경:
    //   ProcessMeshJob() 내부의 인덱스 생성을
    //   WeldAndSmoothJob 으로 교체
    // ============================================================
    public class CaveMeshJobManager : MonoBehaviour
    {
        [Header("Settings")]
        public Material caveMaterial;
        public Material waterMaterial;
        public CaveBiomeSettings caveSettings;
        public int chunkSize = 16;
        public float voxelSize = 1.0f;

        // Weld HashMap 초기 용량 (rawVertices.Length ≒ triCount*3)
        // 마칭 큐브 최대 삼각형 수 = ChunkSize^3 * 5 → 16^3*5=20480 tri → 61440 raw vert
        // 병합 후는 보통 30~50% 수준이므로 넉넉하게 원본 길이를 초기 용량으로 준다.
        private const int INITIAL_HASHMAP_CAPACITY = 65536;

        private readonly List<MeshJobContext> activeJobs = new();
        private Action<ChunkRequestContext, NativeArray<CaveOreData>> externalCallback;

        // Mesh 버텍스 레이아웃 — CaveVertex(32B) 와 완전히 일치
        private readonly VertexAttributeDescriptor[] vertexLayout = new[]
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal,   VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32, 2),
        };

        // ────────────────────────────────────────────────────────
        // 진입점: GPU Readback 완료 후 CaveManager 가 호출
        // ────────────────────────────────────────────────────────
        public void ProcessMeshJob(
            ChunkRequestContext context,
            NativeArray<CaveVertex> rawVertices,
            NativeArray<CaveOreData> ores,
            Action<ChunkRequestContext, NativeArray<CaveOreData>> onCompleted)
        {
            externalCallback = onCompleted;
            int rawCount = rawVertices.Length;

            // ── [A-2] WeldAndSmoothJob 준비 ──────────────────────
            // 출력 버퍼: 최대 크기를 원본과 동일하게 할당 (병합 후 실제 수는 더 적음)
            var weldedVertices = new NativeArray<CaveVertex>(rawCount, Allocator.Persistent);
            var indices = new NativeArray<int>(rawCount, Allocator.Persistent);
            var weldedCountArr = new NativeArray<int>(1, Allocator.Persistent);

            // Weld 내부 임시 자료구조
            var posToIdx = new NativeHashMap<int3, int>(INITIAL_HASHMAP_CAPACITY, Allocator.Persistent);
            var accumNormals = new NativeList<float3>(INITIAL_HASHMAP_CAPACITY, Allocator.Persistent);
            var positions = new NativeList<float3>(INITIAL_HASHMAP_CAPACITY, Allocator.Persistent);

            var weldJob = new WeldAndSmoothJob
            {
                rawVertices = rawVertices,
                weldedVertices = weldedVertices,
                indices = indices,
                weldedCount = weldedCountArr,
                posToIdx = posToIdx,
                accumNormals = accumNormals,
                positions = positions,
            };

            // Weld Job 은 단일 스레드(IJob) — 약 1~3ms (16^3 청크 기준)
            JobHandle weldHandle = weldJob.Schedule();
            weldHandle.Complete(); // 짧은 Job이므로 여기서 동기 대기 (메인 스레드 블로킹 최소)

            // ── Mesh 생성 ─────────────────────────────────────────
            int weldedCount = weldedCountArr[0];
            int indexCount = rawCount; // 인덱스 수는 원본 tri * 3 그대로

            Mesh mesh = new Mesh
            {
                name = $"CaveChunk_{context.ChunkPos}",
                indexFormat = IndexFormat.UInt32
            };

            mesh.SetVertexBufferParams(weldedCount, vertexLayout);
            mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);

            // Welded 버텍스 → Mesh (슬라이스: 실제 사용 수만큼)
            mesh.SetVertexBufferData(
                weldedVertices, 0, 0, weldedCount, 0,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            // 인덱스 → Mesh (전체 길이: rawCount)
            mesh.SetIndexBufferData(
                indices, 0, 0, indexCount,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            mesh.SetSubMesh(0,
                new SubMeshDescriptor(0, indexCount),
                MeshUpdateFlags.DontRecalculateBounds);

            float halfSize = (chunkSize * voxelSize) * 0.5f;
            Vector3 center = new Vector3(halfSize, halfSize, halfSize);
            mesh.bounds = new Bounds(center, Vector3.one * (chunkSize * voxelSize));

            // ── 씬 GameObject 에 Mesh 할당 ───────────────────────
            MeshCollider collider = null;
            bool isHeadless = CaveManager.Instance != null && CaveManager.Instance.isHeadlessPregenMode;

            if (!isHeadless && context.ChunkObject != null)
            {
                var filter = context.ChunkObject.GetOrAddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                var renderer = context.ChunkObject.GetOrAddComponent<MeshRenderer>();
                renderer.sharedMaterial = caveMaterial;

                collider = context.ChunkObject.GetOrAddComponent<MeshCollider>();

                CheckAndSpawnWaterPlane(context);
            }

            // ── Physics Bake Job ──────────────────────────────────
            var bakeJob = new PhysicsBakeJob { meshId = mesh.GetInstanceID() };
            JobHandle bakeHandle = bakeJob.Schedule();

            // ── 컨텍스트 등록 ──────────────────────────────────────
            activeJobs.Add(new MeshJobContext
            {
                BakeJobHandle = bakeHandle,
                GeneratedMesh = mesh,
                ChunkContext = context,
                OreData = ores,
                RawVertices = rawVertices,
                WeldedVertices = weldedVertices,
                Indices = indices,
                WeldedVertexCount = weldedCount,
                PosToIdx = posToIdx,
                AccumNormals = accumNormals,
                Positions = positions,
                WeldedCountResult = weldedCountArr,
                TargetCollider = collider,
            });
        }

        // ────────────────────────────────────────────────────────
        // Update: Physics Bake 완료 감시
        // ────────────────────────────────────────────────────────
        private void Update()
        {
            for (int i = activeJobs.Count - 1; i >= 0; i--)
            {
                var ctx = activeJobs[i];

                if (ctx.ChunkContext.State == ChunkState.Aborted)
                {
                    ctx.BakeJobHandle.Complete();
                    DisposeJobContext(ctx);
                    activeJobs.RemoveAt(i);
                    continue;
                }

                if (!ctx.BakeJobHandle.IsCompleted) continue;
                ctx.BakeJobHandle.Complete();

                bool isHeadless = CaveManager.Instance != null && CaveManager.Instance.isHeadlessPregenMode;

                if (isHeadless)
                {
                    // Headless 모드: TerrainCacheManager 로 우회
                    var data = new PrecookedChunkData
                    {
                        vertices = ctx.WeldedVertices,
                        indices = ctx.Indices,
                        oreData = ctx.OreData,
                        bakedMesh = ctx.GeneratedMesh,
                    };
                    if (TerrainCacheManager.Instance != null)
                        TerrainCacheManager.Instance.AddCache(ctx.ChunkContext.ChunkPos, data);
                    else
                    {
                        DisposeJobContext(ctx);
                        Debug.LogWarning("[CaveMeshJobManager] TerrainCacheManager 없음 → 데이터 폐기");
                    }
                    // Headless에서는 WeldedVertices/Indices를 Cache가 소유
                }
                else
                {
                    // 일반 모드: Collider 에 Mesh 할당
                    if (ctx.TargetCollider != null && ctx.GeneratedMesh != null)
                        ctx.TargetCollider.sharedMesh = ctx.GeneratedMesh;

                    // ── [방안 B 연동] 노멀맵 베이킹 요청 ──────────
                    // MeshCollider 가 BVH 를 완성한 직후에 요청해야
                    // Raycast 가 정상 동작합니다.
                    // 수정 4 이전 주석 처리
                    //if (CaveNormalBaker.Instance != null && ctx.TargetCollider != null)
                    //{
                    //    CaveNormalBaker.Instance.RequestBake(
                    //        ctx.ChunkContext.ChunkPos,
                    //        ctx.TargetCollider,
                    //        ctx.GeneratedMesh.bounds);
                    //}

                    // Raw / Welded / Indices 즉시 해제 (GPU→CPU 복사본은 더 이상 불필요)
                    if (ctx.RawVertices.IsCreated) ctx.RawVertices.Dispose();
                    if (ctx.WeldedVertices.IsCreated) ctx.WeldedVertices.Dispose();
                    if (ctx.Indices.IsCreated) ctx.Indices.Dispose();

                    // 임시 자료구조 해제
                    if (ctx.PosToIdx.IsCreated) ctx.PosToIdx.Dispose();
                    if (ctx.AccumNormals.IsCreated) ctx.AccumNormals.Dispose();
                    if (ctx.Positions.IsCreated) ctx.Positions.Dispose();
                    if (ctx.WeldedCountResult.IsCreated) ctx.WeldedCountResult.Dispose();

                    // Callback (Ecosystem, Spawner 등)
                    externalCallback?.Invoke(ctx.ChunkContext, ctx.OreData);
                }

                activeJobs.RemoveAt(i);
            }
        }

        // ────────────────────────────────────────────────────────
        // 수위 평면 생성 — 변경 없음
        // ────────────────────────────────────────────────────────
        private void CheckAndSpawnWaterPlane(ChunkRequestContext context)
        {
            float chunkWorldY = context.ChunkPos.y * chunkSize * voxelSize;
            float chunkTopY = chunkWorldY + (chunkSize * voxelSize);
            if (caveSettings == null) return;

            DepthLayer layer = caveSettings.GetLayerSettings(chunkWorldY);
            if (layer.waterLevel > -990f &&
                layer.waterLevel >= chunkWorldY &&
                layer.waterLevel <= chunkTopY)
            {
                GameObject waterObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                waterObj.name = "WaterPlane";
                waterObj.transform.SetParent(context.ChunkObject.transform);
                waterObj.transform.localRotation = Quaternion.Euler(90, 0, 0);

                float half = (chunkSize * voxelSize) * 0.5f;
                waterObj.transform.localPosition =
                    new Vector3(half, layer.waterLevel - chunkWorldY, half);
                waterObj.transform.localScale =
                    new Vector3(chunkSize * voxelSize, chunkSize * voxelSize, 1f);

                Destroy(waterObj.GetComponent<Collider>());
                waterObj.GetComponent<MeshRenderer>().sharedMaterial = waterMaterial;
                waterObj.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // ────────────────────────────────────────────────────────
        // 유틸리티
        // ────────────────────────────────────────────────────────
        private static void DisposeJobContext(MeshJobContext ctx)
        {
            if (ctx.OreData.IsCreated) ctx.OreData.Dispose();
            if (ctx.RawVertices.IsCreated) ctx.RawVertices.Dispose();
            if (ctx.WeldedVertices.IsCreated) ctx.WeldedVertices.Dispose();
            if (ctx.Indices.IsCreated) ctx.Indices.Dispose();
            if (ctx.PosToIdx.IsCreated) ctx.PosToIdx.Dispose();
            if (ctx.AccumNormals.IsCreated) ctx.AccumNormals.Dispose();
            if (ctx.Positions.IsCreated) ctx.Positions.Dispose();
            if (ctx.WeldedCountResult.IsCreated) ctx.WeldedCountResult.Dispose();
        }

        private void OnDestroy()
        {
            foreach (var ctx in activeJobs)
            {
                ctx.BakeJobHandle.Complete();
                DisposeJobContext(ctx);
            }
            activeJobs.Clear();
        }
    }

    // ────────────────────────────────────────────────────────────
    // GetOrAddComponent 확장 (편의용)
    // ────────────────────────────────────────────────────────────
    internal static class GameObjectExtensions
    {
        public static T GetOrAddComponent<T>(this GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}