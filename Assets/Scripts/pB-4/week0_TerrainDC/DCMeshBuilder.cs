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
            // [개선] GPU 노말을 기반으로 유지하고, 면 평균으로 보정
            // 기존: 모두 zero 초기화 → face 평균으로 덮어씀 (GPU 노말 소실)
            // 개선: GPU 노말을 저장해두고, face 평균과 블렌딩
            // 1단계: 현재 normals[] = GPU 노말 (호출부에서 이미 채워짐)
            // GPU 노말 복사본 보관
            var gpuNormals = new Unity.Collections.NativeArray<Vector3>(vertexCount, Unity.Collections.Allocator.Temp);
            for (int i = 0; i < vertexCount; i++)
                gpuNormals[i] = normals[i];

            // 2단계: 면 평균 누적
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

                normals[i0] += faceNormal;
                normals[i1] += faceNormal;
                normals[i2] += faceNormal;
            }

            // 3단계: face 평균 정규화 후 GPU 노말과 블렌딩
            // GPU 노말 (밀도 그래디언트) : face 노말 = 6:4 혼합
            // → triplanar slope 계산에 GPU 노말의 부드러움이 지배적으로 유지됨
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 faceN = normals[i];
                float faceLen = faceN.magnitude;
                faceN = faceLen > 0.0001f ? faceN / faceLen : Vector3.up;

                // GPU 노말(그래디언트 기반)과 face 노말 블렌딩
                Vector3 gpuN = gpuNormals[i];
                bool gpuValid = gpuN.sqrMagnitude > 0.001f;
                if (gpuValid)
                {
                    // GPU 노말(밀도 그래디언트) 0.6 + 면 노말 0.4 혼합
                    Vector3 blended = gpuN.normalized * 0.6f + faceN * 0.4f;
                    float bLen = blended.magnitude;
                    normals[i] = bLen > 0.0001f ? blended / bLen : faceN;
                }
                else
                {
                    normals[i] = faceN;
                }
            }
            gpuNormals.Dispose();
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
            var normalList = new System.Collections.Generic.List<Vector3>(); // [FIX-L]
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
                        // [FIX-L] GPU 노말 직접 수집
                        Vector3 gpuN = dcVerts[origIdx].normal;
                        normalList.Add(gpuN.sqrMagnitude > 0.001f ? gpuN : Vector3.up);
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
            // 2. 노말 계산 — GPU 노말 베이스 + Job 보조 스무딩 혼합
            //    [FIX-L] GPU SolveQEF 밀도 그래디언트 노말을 주 입력으로 사용.
            //    DCNormalFinalizeJob은 면 기하 평균을 계산하여 GPU 노말과 혼합.
            //    결과: 부드러운 곡면에서는 GPU 노말로 triplanar 재질 블렌딩 유지,
            //    날카로운 능선에서는 면 노말로 선명한 엣지 표현.
            // ────────────────────────────────────────────────────
            var nativePositions = new NativeArray<Vector3>(vertexList.ToArray(), Allocator.TempJob);
            var nativeIndices = new NativeArray<int>(indexList.ToArray(), Allocator.TempJob);
            // GPU 노말을 기반값으로 Job에 전달 (Job이 면 평균과 혼합)
            var nativeNormals = new NativeArray<Vector3>(normalList.ToArray(), Allocator.TempJob);

            var normalJob = new DCNormalFinalizeJob
            {
                positions = nativePositions,
                indices = nativeIndices,
                normals = nativeNormals,
                vertexCount = finalVertCount,
                indexCount = finalIdxCount
            };
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

            // ────────────────────────────────────────────────────
            // [라플라시안 바닥 스무딩]
            //   DC의 0.5m 격자 아티팩트로 인해 바닥 면이 계단식으로 각짐.
            //   Y축 노말이 높은 "바닥" 버텍스에만 라플라시안 스무딩 적용.
            //   XZ 평면(수평)으로만 이동 — Y(높이)는 고정하여 플랫 바닥 유지.
            //   2회 반복: 부드러움 vs 형태 보존 균형.
            // ────────────────────────────────────────────────────
            ApplyFloorSmoothing(mesh, context, chunkSize, voxelSize, iterations: 2, floorNormalYThreshold: 0.35f, smoothStrength: 0.55f);

            // [주의] mesh.normals는 이미 GPU노말(0.6)+면평균(0.4) 블렌딩 결과.
            //   RecalculateNormals() 호출 시 덮어씌워지므로 사용하지 않음.
            mesh.RecalculateTangents();  // 노말맵 베이킹을 위한 탄젠트 공간 생성

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
                Material mat = caveMaterial;
                if (mat == null && existingMeshJobManager != null)
                    mat = existingMeshJobManager.caveMaterial;
                renderer.sharedMaterial = mat;
                // [Fix-Shadow] DC 메쉬 앞뒤 양면 그림자 — Angular 면의 그림자 누락 방지
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;

                // [v3] 서브복셀 노말맵 베이킹 (isHeadless=false, renderer 생성 후)
                var normalBaker = GetComponent<CaveNormalBaker>();
                if (normalBaker != null)
                {
                    if (context.DensityCache != null && context.DensityCache.Length > 0)
                        normalBaker.BakeNormalMap(mesh, renderer, context.DensityCache,
                            context.DensityDcBasePos, context.DensityDcN, context.DensityVoxelSize);
                    else
                        normalBaker.BakeFlat(mesh, renderer);
                }

                var collider = context.ChunkObject.GetOrAddComponent<MeshCollider>();
                collider.sharedMesh = mesh;

                // Physics Bake (기존 PhysicsBakeJob 패턴)
                // MeshCollider 물리 베이크 — re-assign 방식 (Unity 버전 호환)
                collider.sharedMesh = null;
                collider.sharedMesh = mesh;

                if (collider != null)
                    collider.sharedMesh = mesh;
            }

            context.State = ChunkState.Completed;

            Debug.Log($"[DCMeshBuilder] 메쉬 생성 완료: {context.ChunkPos}, 버텍스={finalVertCount}, 삼각형={finalIdxCount / 3}, 쿼드={quadCount}");

            // ────────────────────────────────────────────────────
            // 5. 청크 이음새 스티칭 등록 (ChunkSeamStitcher)
            //    인접 청크와 경계 버텍스 Y 좌표 평균화 → 균열 제거
            // ────────────────────────────────────────────────────
            if (ChunkSeamStitcher.Instance != null && context.ChunkObject != null)
            {
                ChunkSeamStitcher.Instance.RegisterAndStitch(
                    context.ChunkPos,
                    mesh,
                    context.ChunkObject.transform,
                    voxelSize,
                    chunkSize * voxelSize
                );
            }

            // ────────────────────────────────────────────────────
            // 6. NavMesh 갱신 요청 + 콜백
            // ────────────────────────────────────────────────────
            if (CaveManager.Instance != null)
                CaveManager.Instance.requestNavMeshUpdate = true;

            onCompleted?.Invoke(context);
        }
        // END BuildMeshFromDCData

        /// <summary>
        /// DC 격자 아티팩트로 인한 바닥 각짐을 완화하는 라플라시안 스무딩.
        /// Y축 노말 성분이 높은 "바닥" 버텍스에만 적용하며, XZ 평면으로만 이동.
        /// </summary>
        private static void ApplyFloorSmoothing(Mesh mesh, ChunkRequestContext context, int chunkSize, float voxelSize, int iterations, float floorNormalYThreshold, float smoothStrength)
        {
            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] tris = mesh.triangles;
            int vCount = verts.Length;

            // 1. 인접 버텍스 목록 구성 (삼각형 공유 기준)
            var neighbors = new System.Collections.Generic.List<int>[vCount];
            for (int i = 0; i < vCount; i++)
                neighbors[i] = new System.Collections.Generic.List<int>();

            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (!neighbors[a].Contains(b)) neighbors[a].Add(b);
                if (!neighbors[a].Contains(c)) neighbors[a].Add(c);
                if (!neighbors[b].Contains(a)) neighbors[b].Add(a);
                if (!neighbors[b].Contains(c)) neighbors[b].Add(c);
                if (!neighbors[c].Contains(a)) neighbors[c].Add(a);
                if (!neighbors[c].Contains(b)) neighbors[c].Add(b);
            }

            // 2. 반복 스무딩
            var smoothed = new Vector3[vCount];
            for (int iter = 0; iter < iterations; iter++)
            {
                System.Array.Copy(verts, smoothed, vCount);
                for (int i = 0; i < vCount; i++)
                {
                    // 바닥 버텍스 판별: Y 노말 성분이 임계값 초과
                    if (normals[i].y < floorNormalYThreshold)
                        continue;

                    var nb = neighbors[i];
                    if (nb.Count == 0) continue;

                    // 인접 버텍스 XZ 평균 계산
                    float avgX = 0, avgZ = 0;
                    foreach (int j in nb) { avgX += verts[j].x; avgZ += verts[j].z; }
                    avgX /= nb.Count;
                    avgZ /= nb.Count;

                    // XZ만 이동 (Y=높이는 고정 → 플랫 바닥 유지)
                    // [청크 경계 스무딩 약화] 경계 근처 버텍스는 이음새 연속성을 위해 약하게 스무딩
                    float chunkWorldSize = chunkSize * voxelSize;
                    float bx = verts[i].x / chunkWorldSize;  // 0~1 범위 청크 내 위치
                    float bz = verts[i].z / chunkWorldSize;
                    float borderFactor = Mathf.Min(
                        Mathf.Min(bx, 1f - bx),
                        Mathf.Min(bz, 1f - bz)
                    ) * 8f;  // 경계에서 멀수록 최대 1.0
                    float effectiveStrength = smoothStrength * Mathf.Clamp01(borderFactor);

                    smoothed[i] = new Vector3(
                        Mathf.Lerp(verts[i].x, avgX, effectiveStrength),
                        verts[i].y,
                        Mathf.Lerp(verts[i].z, avgZ, effectiveStrength)
                    );
                }
                System.Array.Copy(smoothed, verts, vCount);
            }

            mesh.vertices = verts;
            mesh.RecalculateBounds();
        }
    }
}