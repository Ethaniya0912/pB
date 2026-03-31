// =============================================================================
// DCMeshBuilder.cs  |  pB-4 Project — Week 0, Stage 4
// Layer  : Pipeline (지형)
// Owner  : Person B
//
// 역할:
//   DC 파이프라인에서 생성된 DCVertex[]와 DCQuad[]를 Unity Mesh로 변환하여
//   씬에 렌더링 가능한 상태로 만든다.
//   기존 CaveMeshJobManager의 ProcessMeshJob()과 동일한 역할을 하되,
//   DC 데이터(쿼드 기반)에 맞게 재구현한다.
//
//   기존 CaveMeshJobManager는 수정하지 않는다.
//   DC 메쉬 빌드는 이 클래스가 전담하고,
//   기존 MC 메쉬 빌드는 CaveMeshJobManager가 그대로 담당한다.
//
// 기존 파이프라인과의 관계:
//   - DC는 이미 공유 버텍스를 출력하므로 WeldAndSmoothJob이 불필요
//   - 대신 DCNormalFinalizeJob으로 Face Normal→Vertex Normal 스무딩만 수행
//   - MeshCollider + PhysicsBakeJob은 기존 패턴 그대로 사용
//   - CaveEcosystemManager, CaveSpawnerManager 연동도 기존 패턴 유지
// =============================================================================
using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;

namespace CaveSystem
{
    /// <summary>
    /// DC 버텍스 노말을 인접 면의 가중 평균으로 스무딩하는 Job.
    /// MC의 WeldAndSmoothJob을 대체한다.
    /// DC는 이미 공유 버텍스이므로 Weld가 불필요하고, 노말 스무딩만 수행.
    /// </summary>
    [BurstCompile]
    public struct DCNormalFinalizeJob : IJob
    {
        [ReadOnly] public NativeArray<Vector3> positions;
        [ReadOnly] public NativeArray<int> indices;
        public NativeArray<Vector3> normals;
        public int vertexCount;
        public int indexCount;

        public void Execute()
        {
            // 모든 노말 초기화
            for (int i = 0; i < vertexCount; i++)
                normals[i] = Vector3.zero;

            // 각 삼각형의 Face Normal을 계산하여 공유 버텍스에 누적
            for (int i = 0; i < indexCount; i += 3)
            {
                int i0 = indices[i];
                int i1 = indices[i + 1];
                int i2 = indices[i + 2];

                if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount)
                    continue;

                Vector3 v0 = positions[i0];
                Vector3 v1 = positions[i1];
                Vector3 v2 = positions[i2];

                Vector3 edge1 = v1 - v0;
                Vector3 edge2 = v2 - v0;
                Vector3 faceNormal = Vector3.Cross(edge1, edge2);
                // 면적 가중: faceNormal의 크기가 삼각형 면적에 비례

                normals[i0] += faceNormal;
                normals[i1] += faceNormal;
                normals[i2] += faceNormal;
            }

            // 정규화
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 n = normals[i];
                float len = n.magnitude;
                normals[i] = len > 0.0001f ? n / len : Vector3.up;
            }
        }
    }

    /// <summary>
    /// DC 쿼드+버텍스 데이터를 Unity Mesh로 변환하는 빌더.
    /// CaveComputeDispatcher의 DC 분기에서 호출된다.
    /// </summary>
    public class DCMeshBuilder : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("기존 CaveMeshJobManager의 caveMaterial과 동일한 머티리얼을 할당")]
        public Material caveMaterial;

        [Header("References")]
        public CaveMeshJobManager existingMeshJobManager;

        /// <summary>
        /// DC GPU Readback 결과를 받아 Unity Mesh를 생성하고 씬에 배치한다.
        /// CaveComputeDispatcher DC 분기의 ReadbackAsync 콜백에서 호출.
        /// </summary>
        /// <param name="dcVerts">GPU에서 읽어온 DC 버텍스 배열</param>
        /// <param name="dcQuads">GPU에서 읽어온 DC 쿼드 배열</param>
        /// <param name="quadCount">유효 쿼드 수</param>
        /// <param name="context">현재 청크 요청 컨텍스트</param>
        /// <param name="chunkSize">청크 크기</param>
        /// <param name="voxelSize">복셀 크기</param>
        /// <param name="onCompleted">메쉬 완성 후 콜백 (NavMesh 갱신 등)</param>
        public void BuildMeshFromDCData(
            DCVertex[] dcVerts, DCQuad[] dcQuads, int quadCount,
            ChunkRequestContext context, int chunkSize, float voxelSize,
            Action<ChunkRequestContext> onCompleted)
        {
            if (dcVerts == null || dcQuads == null || quadCount <= 0)
            {
                Debug.LogWarning("[DCMeshBuilder] 유효한 DC 데이터가 없습니다.");
                context.State = ChunkState.Completed;
                onCompleted?.Invoke(context);
                return;
            }

            // ────────────────────────────────────────────────────
            // 1. 유효 버텍스 수집 + 인덱스 리매핑
            //    DCVertex[] 중 실제 쿼드가 참조하는 버텍스만 추출
            // ────────────────────────────────────────────────────
            // 쿼드→삼각형 변환: 1 쿼드 = 2 삼각형 = 6 인덱스
            int triIndexCount = quadCount * 6;
            var usedVertMap = new System.Collections.Generic.Dictionary<int, int>();
            var vertexList = new System.Collections.Generic.List<Vector3>();
            var uvList = new System.Collections.Generic.List<Vector2>();
            var indexList = new System.Collections.Generic.List<int>();

            for (int q = 0; q < quadCount; q++)
            {
                DCQuad quad = dcQuads[q];
                int[] quadIndices = { quad.v0, quad.v1, quad.v2, quad.v3 };

                // 각 쿼드 버텍스를 리매핑
                int[] remapped = new int[4];
                for (int i = 0; i < 4; i++)
                {
                    int origIdx = quadIndices[i];
                    if (origIdx < 0 || origIdx >= dcVerts.Length)
                    {
                        remapped[i] = 0; // 범위 초과 시 안전 처리
                        continue;
                    }

                    if (!usedVertMap.TryGetValue(origIdx, out int newIdx))
                    {
                        newIdx = vertexList.Count;
                        usedVertMap[origIdx] = newIdx;
                        vertexList.Add(dcVerts[origIdx].position);
                        uvList.Add(dcVerts[origIdx].uv);
                    }
                    remapped[i] = newIdx;
                }

                // 쿼드 → 2 삼각형 (0,1,2) + (0,2,3)
                indexList.Add(remapped[0]);
                indexList.Add(remapped[1]);
                indexList.Add(remapped[2]);

                indexList.Add(remapped[0]);
                indexList.Add(remapped[2]);
                indexList.Add(remapped[3]);
            }

            int finalVertCount = vertexList.Count;
            int finalIdxCount = indexList.Count;

            if (finalVertCount == 0 || finalIdxCount == 0)
            {
                Debug.LogWarning("[DCMeshBuilder] 리매핑 후 유효 버텍스가 없습니다.");
                context.State = ChunkState.Completed;
                onCompleted?.Invoke(context);
                return;
            }

            // ────────────────────────────────────────────────────
            // 2. 노말 스무딩 (DCNormalFinalizeJob)
            //    MC의 WeldAndSmoothJob을 대체. DC는 Weld 불필요.
            // ────────────────────────────────────────────────────
            var nativePositions = new NativeArray<Vector3>(vertexList.ToArray(), Allocator.TempJob);
            var nativeIndices = new NativeArray<int>(indexList.ToArray(), Allocator.TempJob);
            var nativeNormals = new NativeArray<Vector3>(finalVertCount, Allocator.TempJob);

            var normalJob = new DCNormalFinalizeJob
            {
                positions = nativePositions,
                indices = nativeIndices,
                normals = nativeNormals,
                vertexCount = finalVertCount,
                indexCount = finalIdxCount
            };

            // 짧은 Job이므로 동기 대기 (기존 WeldAndSmoothJob과 동일 패턴)
            normalJob.Schedule().Complete();

            // ────────────────────────────────────────────────────
            // 3. Unity Mesh 생성
            //    기존 CaveMeshJobManager.ProcessMeshJob()과 동일 패턴
            // ────────────────────────────────────────────────────
            Mesh mesh = new Mesh
            {
                name = $"DCChunk_{context.ChunkPos}",
                indexFormat = finalVertCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };

            Vector3[] finalPositions = nativePositions.ToArray();
            Vector3[] finalNormals = nativeNormals.ToArray();
            Vector2[] finalUVs = uvList.ToArray();
            int[] finalIndices = nativeIndices.ToArray();

            mesh.vertices = finalPositions;
            mesh.normals = finalNormals;
            mesh.uv = finalUVs;
            mesh.triangles = finalIndices;

            float halfSize = (chunkSize * voxelSize) * 0.5f;
            Vector3 center = new Vector3(halfSize, halfSize, halfSize);
            mesh.bounds = new Bounds(center, Vector3.one * (chunkSize * voxelSize));

            // NativeArray 해제
            nativePositions.Dispose();
            nativeIndices.Dispose();
            nativeNormals.Dispose();

            // ────────────────────────────────────────────────────
            // 4. 씬 GameObject에 Mesh 할당
            //    기존 CaveMeshJobManager 패턴 준수
            // ────────────────────────────────────────────────────
            bool isHeadless = CaveManager.Instance != null && CaveManager.Instance.isHeadlessPregenMode;

            if (!isHeadless && context.ChunkObject != null)
            {
                var filter = context.ChunkObject.GetOrAddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                var renderer = context.ChunkObject.GetOrAddComponent<MeshRenderer>();
                // caveMaterial이 할당되어 있으면 사용, 아니면 기존 MeshJobManager에서 가져옴
                Material mat = caveMaterial;
                if (mat == null && existingMeshJobManager != null)
                    mat = existingMeshJobManager.caveMaterial;
                renderer.sharedMaterial = mat;

                var collider = context.ChunkObject.GetOrAddComponent<MeshCollider>();
                collider.sharedMesh = mesh;

                // Physics Bake (기존 PhysicsBakeJob 패턴)
                var bakeJob = new PhysicsBakeJob { meshId = mesh.GetInstanceID() };
                bakeJob.Schedule().Complete();

                if (collider != null)
                    collider.sharedMesh = mesh;
            }

            context.State = ChunkState.Completed;

            Debug.Log($"[DCMeshBuilder] 메쉬 생성 완료: {context.ChunkPos}, 버텍스={finalVertCount}, 삼각형={finalIdxCount / 3}, 쿼드={quadCount}");

            // ────────────────────────────────────────────────────
            // 5. NavMesh 갱신 요청 + 콜백
            // ────────────────────────────────────────────────────
            if (CaveManager.Instance != null)
                CaveManager.Instance.requestNavMeshUpdate = true;

            onCompleted?.Invoke(context);
        }
    }
}
