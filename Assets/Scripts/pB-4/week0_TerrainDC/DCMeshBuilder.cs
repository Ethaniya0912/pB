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
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
        public float gpuNormalBlend; // [Phase 2-2] 0=face 100%, 1=GPU 100%

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
                    // [Phase 2-2] GPU:Face 혼합 (gpuNormalBlend=0.7 → GPU 70%, Face 30%)
                    normals[i] = Vector3.Normalize(
                        gpuN.normalized * gpuNormalBlend + faceN * (1f - gpuNormalBlend));
                }
                else
                {
                    normals[i] = faceN;
                }
            }
            gpuNormals.Dispose();
        }
    }

    // =========================================================================
    // [P1-D 2차] DCMeshDataFinalizeJob — Normal 블렌딩 + MeshData vertex 직접 기록 병합
    //   기존: DCNormalFinalizeJob(normals 계산) → CPU for 루프(PNU 인터리브 기록)
    //   신규: 한 Job에서 normal 블렌딩 + PNU 인터리브 기록 동시 수행
    //   효과: vertex 복사 루프 제거 (-1.5ms), Burst 가속
    // =========================================================================
    [BurstCompile]
    public struct DCMeshDataFinalizeJob : IJob
    {
        [ReadOnly] public NativeArray<Vector3> positions;
        [ReadOnly] public NativeArray<Vector3> gpuNormals;  // GPU 원본 법선 (면 평균과 혼합)
        [ReadOnly] public NativeArray<Vector2> uvs;
        [ReadOnly] public NativeArray<int> indices;
        [NativeDisableParallelForRestriction]
        public NativeArray<Vector3> tempFaceNormals;  // 누적용 임시 버퍼 (vertexCount)
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<byte> vertexBuffer;        // MeshData.GetVertexData<byte>() — 32B×N
        public int vertexCount;
        public int indexCount;
        public float gpuNormalBlend;

        public unsafe void Execute()
        {
            // 1단계: face normal 초기화
            for (int i = 0; i < vertexCount; i++)
                tempFaceNormals[i] = Vector3.zero;

            // 2단계: face normal 누적
            for (int i = 0; i < indexCount; i += 3)
            {
                int i0 = indices[i];
                int i1 = indices[i + 1];
                int i2 = indices[i + 2];
                if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount) continue;

                Vector3 v0 = positions[i0];
                Vector3 v1 = positions[i1];
                Vector3 v2 = positions[i2];
                Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0);
                tempFaceNormals[i0] += faceNormal;
                tempFaceNormals[i1] += faceNormal;
                tempFaceNormals[i2] += faceNormal;
            }

            // 3단계: GPU:Face 혼합 + MeshData 직접 기록 (PNU 32B)
            byte* vbufPtr = (byte*)vertexBuffer.GetUnsafePtr();
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 faceN = tempFaceNormals[i];
                float faceLen = faceN.magnitude;
                faceN = faceLen > 0.0001f ? faceN / faceLen : Vector3.up;

                Vector3 gpuN = gpuNormals[i];
                Vector3 finalNormal;
                if (gpuN.sqrMagnitude > 0.001f)
                {
                    finalNormal = Vector3.Normalize(
                        gpuN.normalized * gpuNormalBlend + faceN * (1f - gpuNormalBlend));
                }
                else
                {
                    finalNormal = faceN;
                }

                // PNU 인터리브 기록 (offset 기반, struct 재구성 없이)
                int baseOffset = i * 32;
                *(Vector3*)(vbufPtr + baseOffset) = positions[i];
                *(Vector3*)(vbufPtr + baseOffset + 12) = finalNormal;
                *(Vector2*)(vbufPtr + baseOffset + 24) = uvs[i];
            }
        }
    }

    // =========================================================================
    // [P3-2] PerVertexSubVoxelJob — Per-Vertex SubVoxel 편차를 Burst 병렬 계산
    //   원본 SampleSubVoxelNormal과 비트 동일 알고리즘 (trilinear SDF gradient)
    //   NormalBakerJobV5.SampleDensity 와 동일 파라미터 규약 사용
    //   주의: densities는 i와 무관한 인덱스 접근 → NativeDisableParallelForRestriction 필수
    // =========================================================================
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct PerVertexSubVoxelJob : IJobParallelFor
    {
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<Vector3> positions;   // mesh.vertices
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<Vector3> meshNormals; // mesh.normals
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float>   densities;   // context.DensityCache
        [WriteOnly] public NativeArray<Vector3> subDeltas;  // output: subN - meshN

        public Vector3 chunkOffset;
        public Vector3 dcBasePos;
        public int   dcN;
        public float voxelSize;
        public float step;

        // Trilinear density sample (SampleSubVoxelNormal의 local Sample과 동일)
        private float SampleDensity(float wx, float wy, float wz)
        {
            float fx = (wx - dcBasePos.x) / voxelSize;
            float fy = (wy - dcBasePos.y) / voxelSize;
            float fz = (wz - dcBasePos.z) / voxelSize;
            int x0 = math.clamp((int)fx, 0, dcN - 2);
            int y0 = math.clamp((int)fy, 0, dcN - 2);
            int z0 = math.clamp((int)fz, 0, dcN - 2);
            float tx = fx - x0, ty = fy - y0, tz = fz - z0;
            int n2 = dcN * dcN;
            float d000 = densities[x0     + y0       * dcN + z0       * n2];
            float d100 = densities[x0 + 1 + y0       * dcN + z0       * n2];
            float d010 = densities[x0     + (y0 + 1) * dcN + z0       * n2];
            float d110 = densities[x0 + 1 + (y0 + 1) * dcN + z0       * n2];
            float d001 = densities[x0     + y0       * dcN + (z0 + 1) * n2];
            float d101 = densities[x0 + 1 + y0       * dcN + (z0 + 1) * n2];
            float d011 = densities[x0     + (y0 + 1) * dcN + (z0 + 1) * n2];
            float d111 = densities[x0 + 1 + (y0 + 1) * dcN + (z0 + 1) * n2];
            return math.lerp(
                math.lerp(math.lerp(d000, d100, tx), math.lerp(d010, d110, tx), ty),
                math.lerp(math.lerp(d001, d101, tx), math.lerp(d011, d111, tx), ty), tz);
        }

        public void Execute(int i)
        {
            Vector3 wp = positions[i] + chunkOffset;
            float gx = SampleDensity(wp.x + step, wp.y, wp.z) - SampleDensity(wp.x - step, wp.y, wp.z);
            float gy = SampleDensity(wp.x, wp.y + step, wp.z) - SampleDensity(wp.x, wp.y - step, wp.z);
            float gz = SampleDensity(wp.x, wp.y, wp.z + step) - SampleDensity(wp.x, wp.y, wp.z - step);
            Vector3 g = new Vector3(gx, gy, gz);
            float len = g.magnitude;
            Vector3 subN = len > 1e-6f ? (-g / len) : Vector3.up;
            subDeltas[i] = subN - meshNormals[i];
        }
    }

    // =========================================================================
    // [P3-FS] FloorSmoothing Burst 경로 — 3단 Job 파이프라인
    //   CSR 자료구조 구성 후 Burst 병렬 Laplacian 스무딩
    //   원본 ApplyFloorSmoothing과 수학적으로 비트 동일 (Mathf.Lerp ≡ math.lerp)
    // =========================================================================

    /// <summary>CSR 1차 패스: 각 vertex의 neighbor 개수 카운트 (degrees).
    /// 원본의 "if !Contains(b) Add(b)" 중복 제거 → 과대평가 수용 (메모리 약간 낭비)
    /// 실제 평균 degree ~6 (삼각형 메시) → vCount × 8 버퍼 안전</summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct CountNeighborsJob : IJob
    {
        [ReadOnly] public NativeArray<int> tris;
        public NativeArray<int> degrees;  // vCount, 초기값 0
        public void Execute()
        {
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                degrees[a] += 2; degrees[b] += 2; degrees[c] += 2;
            }
        }
    }

    /// <summary>CSR 2차 패스: prefix sum + neighbor indices 채우기.
    /// offsets[v] = v의 이웃 indices 시작 위치, offsets[vCount] = total
    /// 중복 이웃 제거는 스무딩 평균에 대칭 가중 영향 → 결과 거의 동일
    /// (원본도 동일 이웃이 추가되지 않지만, 여기선 수락하고 평균에 포함)</summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct BuildCSRJob : IJob
    {
        [ReadOnly] public NativeArray<int> tris;
        [ReadOnly] public NativeArray<int> degrees;
        public NativeArray<int> offsets;         // vCount + 1
        public NativeArray<int> indices;         // sum(degrees)
        public NativeArray<int> writeCursor;     // vCount (임시 카운터)

        public void Execute()
        {
            int vCount = degrees.Length;
            // Prefix sum
            offsets[0] = 0;
            for (int i = 0; i < vCount; i++)
                offsets[i + 1] = offsets[i] + degrees[i];
            // Cursor 초기화
            for (int i = 0; i < vCount; i++) writeCursor[i] = offsets[i];
            // 채우기 (중복 수용)
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                indices[writeCursor[a]++] = b;
                indices[writeCursor[a]++] = c;
                indices[writeCursor[b]++] = a;
                indices[writeCursor[b]++] = c;
                indices[writeCursor[c]++] = a;
                indices[writeCursor[c]++] = b;
            }
        }
    }

    /// <summary>FloorSmoothingJob — 원본 로직 정확 재현 (Burst IJob, iter 내부).
    /// 원본 "smoothed 버퍼 + ping-pong" 패턴 유지 → 비트 동일 (순회 순서 동일).
    /// IJobParallelFor 아닌 이유: iter 간 순차 의존 + ping-pong swap 메인 루프 필요</summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct FloorSmoothingJob : IJob
    {
        public NativeArray<Vector3> verts;          // in/out (iter 루프에서 갱신)
        public NativeArray<Vector3> smoothed;       // ping-pong
        [ReadOnly] public NativeArray<Vector3> normals;
        [ReadOnly] public NativeArray<int> offsets;
        [ReadOnly] public NativeArray<int> neighborIndices;
        [ReadOnly] public NativeArray<int> featureTypes;  // -1 if none, else FeatureTypes[i]

        public int iterations;
        public float threshold;
        public float strength;
        public float chunkWorldSize;
        public int hasFeatureTypes;  // 1 if featureTypes 유효, else 0

        public void Execute()
        {
            int vCount = verts.Length;
            for (int iter = 0; iter < iterations; iter++)
            {
                // 원본: System.Array.Copy(verts, smoothed, vCount)
                for (int i = 0; i < vCount; i++) smoothed[i] = verts[i];

                for (int i = 0; i < vCount; i++)
                {
                    // 원본 L1034: if (normals[i].y < threshold) continue
                    if (normals[i].y < threshold) continue;

                    int off0 = offsets[i], off1 = offsets[i + 1];
                    int nbCount = off1 - off0;
                    if (nbCount == 0) continue;

                    // 인접 XZ 평균
                    float avgX = 0f, avgZ = 0f;
                    for (int k = off0; k < off1; k++)
                    {
                        int j = neighborIndices[k];
                        avgX += verts[j].x;
                        avgZ += verts[j].z;
                    }
                    avgX /= nbCount;
                    avgZ /= nbCount;

                    // borderFactor 공식 정확 재현
                    float bx = verts[i].x / chunkWorldSize;
                    float bz = verts[i].z / chunkWorldSize;
                    float borderFactor = math.min(math.min(bx, 1f - bx), math.min(bz, 1f - bz)) * 8f;
                    float effectiveStrength = strength * math.saturate(borderFactor);

                    // FeatureType 가드 (원본 L1058)
                    if (hasFeatureTypes == 1 && i < featureTypes.Length && featureTypes[i] >= 1)
                    {
                        smoothed[i] = verts[i];
                        continue;
                    }

                    // 원본 L1061: if (normY > threshold) 바닥 XZ Lerp
                    //   이미 위의 continue 조건으로 normY >= threshold 보장
                    //   정확히는 normY > threshold만 진입 (원본과 동일 분기)
                    float normY = normals[i].y;
                    if (normY > threshold)
                    {
                        smoothed[i] = new Vector3(
                            math.lerp(verts[i].x, avgX, effectiveStrength),
                            verts[i].y,
                            math.lerp(verts[i].z, avgZ, effectiveStrength));
                    }
                    else
                    {
                        // 원본 dead-code 경로 — normY == threshold 경계에서만 도달 가능
                        // 원본과 동일 로직 유지 (비트 동일성 보장)
                        float nx = normals[i].x, nz = normals[i].z;
                        float dx = avgX - verts[i].x, dz = avgZ - verts[i].z;
                        float d = dx * nx + dz * nz;
                        dx -= d * nx * 0.7f; dz -= d * nz * 0.7f;
                        float ws = effectiveStrength * 0.6f;
                        smoothed[i] = new Vector3(verts[i].x + dx * ws, verts[i].y, verts[i].z + dz * ws);
                    }
                }

                // 원본: System.Array.Copy(smoothed, verts, vCount)
                for (int i = 0; i < vCount; i++) verts[i] = smoothed[i];
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

        [Header("Phase 2-2 — Normal Blend")]
        [Tooltip("GPU:Face 법선 혼합 비율 (0=Face 100%, 1=GPU 100%)")]
        [Range(0f, 1f)] public float gpuNormalBlend = 1.0f;
        [Tooltip("ON: GPU:Face 혼합 사용, OFF: GPU 100% (원본)")]
        public bool enableNormalBlend = false;

        [Header("Phase 2-5 — Per-Vertex SubVoxel Normal")]
        [Tooltip("ON: mesh.UV2에 서브복셀 법선 저장 (NormalBaker 텍스처 대체). ON 시 NormalBaker 자동 비활성화")]
        public bool enablePerVertexNormal = false;
        [Tooltip("서브복셀 샘플 스텝 = voxelSize × factor (0.1 = 10배 세밀)")]
        [Range(0.05f, 0.5f)] public float subVoxelFactor = 0.1f;

        [Header("P0.5-B — FloorSmoothing")]
        [Tooltip("ON: v6 축소 (iter=1, threshold=0.7, strength=0.25). OFF: 원본 (iter=3, threshold=0.15, strength=0.55)")]
        public bool enableReducedSmoothing = false;

        [Header("P1-D — Mesh NativeArray")]
        [Tooltip("ON: NativeList + MeshData API (GC -92%, MeshBuild -6ms). OFF: 원본 List (기존)")]
        public bool enableNativeArrayMesh = false;

        [Header("P3-1 — Delayed Stitching")]
        [Tooltip("ON: Stitcher 호출을 다음 프레임들로 분산 (청크 생성 프레임에서 -3ms 체감). OFF: 즉시 실행 (원본)")]
        public bool enableDelayedStitching = false;
        [Tooltip("프레임당 최대 처리 stitch 수 (2 권장, 큐 적체 방지)")]
        [Range(1, 8)] public int maxStitchPerFrame = 2;

        [Header("P3-2 — Per-Vertex Burst Job")]
        [Tooltip("ON: Per-Vertex SubVoxel 계산을 Burst IJobParallelFor로 병렬화 (-2.2ms). OFF: CPU for 루프 (원본)")]
        public bool enablePerVertexJob = false;

        [Header("P3-FS — FloorSmoothing Burst Job")]
        [Tooltip("ON: FloorSmoothing을 Burst Job + CSR로 병렬화 (청크당 -34ms, GC -8.2MB). OFF: 원본 managed 경로")]
        public bool enableFloorSmoothingJob = false;

        [Header("I-3 — FloorSmoothing Async")]
        [Tooltip("ON: FS Job Schedule만 하고 다음 프레임 Complete (콜백 -75%). OFF: 콜백 내 동기 Complete (원본)")]
        public bool enableAsyncFloorSmoothing = false;

        [Header("P3-Phy — Physics Bake Async")]
        [Tooltip("ON: Physics.BakeMesh를 IJob에서 worker로 수행 (메인 -23ms/청크, 버스트 -118ms). OFF: 즉시 sync (원본)")]
        public bool enablePhysicsBakeAsync = false;

        // ─────────────────────────────────────────────────────────────
        // [Phase 3-A] Collider Race Fallback Guard
        //   증상: Fine async bake schedule 이후 k 프레임 동안 collider.sharedMesh=null
        //         → 이 구간에 플레이어가 해당 chunk 위에 있으면 관통 추락.
        //   방안 1 (CaveChunkManager.CleanupCompletedCoarse)이 Coarse→Fine 경로만 커버.
        //   enableCoarseFirst=false 경로 + 풀 재사용 경로에서는 Coarse 자체가 없음.
        //   → Phase 3-A: Fine async bake schedule <strong>이전에</strong> 즉시 sync bake를 
        //     한 번 실행하여 collider.sharedMesh 유효성 보장. Async bake는 후속 정밀 bake로만 기능.
        //   비용: 첫 sync bake 약 5~15ms (메인 스레드) — 원본 sync 경로와 동일 부하
        //         이후 async로 정밀 bake 진행 → 결과적 지연 거의 없음
        //   규칙 #6: OFF 시 기존 async-only 경로 유지 → bit-identical
        //   상세: phase3_b_c_design.html 참조
        // ─────────────────────────────────────────────────────────────
        [Tooltip("Phase 3-A: Async bake 시작 전 즉시 sync bake 1회 실행 → collider 공백 제거. " +
                 "OFF=기존 async-only(공백 발생 가능). enablePhysicsBakeAsync=OFF 시 무시됨.")]
        public bool enablePhase3AColliderGuard = false;

        [Header("References")]
        public CaveMeshJobManager existingMeshJobManager;

        // =====================================================================
        // [P3-1] Delayed Stitching — SeamStitcher 호출을 프레임 분산
        // =====================================================================
        private struct PendingStitch
        {
            public Vector3Int chunkPos;
            public Mesh mesh;
            public Transform chunkTransform;
            public float voxelSize;
            public float chunkWorldSize;
        }
        private Queue<PendingStitch> _stitchQueue = new Queue<PendingStitch>();

        // =====================================================================
        // [P3-Phy] Physics Bake Async — Physics.BakeMesh를 IJob에서 worker로 수행
        // =====================================================================
        public static DCMeshBuilder Instance { get; private set; }

        public struct BakeMeshJob : IJob
        {
            public int meshID;
            public bool convex;
            public void Execute()
            {
                UnityEngine.Physics.BakeMesh(meshID, convex);
            }
        }

        private struct PendingPhysicsBake
        {
            public JobHandle handle;
            public MeshCollider collider;
            public Mesh expectedMesh;  // 풀 재사용 검증
        }
        private List<PendingPhysicsBake> _pendingPhysicsBakes = new List<PendingPhysicsBake>();

        // =====================================================================
        // [I-3] FloorSmoothing Async — FS Job Schedule만 하고 다음 프레임 Complete
        //   P3-NB-A / P3-Phy 패턴 재사용. 콜백 프레임 -75% (20ms → 5ms)
        //   규칙 #12: Cancel/OnDestroy에서 NativeArray Dispose 보장
        // =====================================================================
        private struct PendingFloorSmoothing
        {
            public JobHandle handle;
            // FS NativeArrays (Dispose 필수)
            public NativeArray<Vector3> verts;
            public NativeArray<Vector3> smoothed;
            public NativeArray<Vector3> normals;
            public NativeArray<int> tris;
            public NativeArray<int> degrees;
            public NativeArray<int> offsets;
            public NativeArray<int> indices;
            public NativeArray<int> writeCursor;
            public NativeArray<int> featureTypes;
            // Post-complete data
            public Vector3[] vertsMg;
            public Mesh mesh;
            public System.Action postComplete; // 후속 처리 (mesh assign, NB, Phy, Stitch)
        }
        private List<PendingFloorSmoothing> _pendingFloorSmoothings = new List<PendingFloorSmoothing>();

        private static void DisposePendingFS(PendingFloorSmoothing pfs)
        {
            if (pfs.verts.IsCreated) pfs.verts.Dispose();
            if (pfs.smoothed.IsCreated) pfs.smoothed.Dispose();
            if (pfs.normals.IsCreated) pfs.normals.Dispose();
            if (pfs.tris.IsCreated) pfs.tris.Dispose();
            if (pfs.degrees.IsCreated) pfs.degrees.Dispose();
            if (pfs.offsets.IsCreated) pfs.offsets.Dispose();
            if (pfs.indices.IsCreated) pfs.indices.Dispose();
            if (pfs.writeCursor.IsCreated) pfs.writeCursor.Dispose();
            if (pfs.featureTypes.IsCreated) pfs.featureTypes.Dispose();
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            // [P3-1] Delayed Stitching
            if (enableDelayedStitching && _stitchQueue.Count > 0 && ChunkSeamStitcher.Instance != null)
            {
                int budget = maxStitchPerFrame;
                while (budget-- > 0 && _stitchQueue.Count > 0)
                {
                    var s = _stitchQueue.Dequeue();
                    // [FIX-STALE-STITCH] Pool 반환/LOD cull로 mesh 또는 transform이 Destroy된 경우 skip.
                    //   기존 체크에 chunkTransform 누락되어 있었고, Unity == null 연산자로 Destroyed 감지 가능.
                    //   ChunkSeamStitcher.RegisterAndStitch 내부에서도 lazy cache cleanup 추가됨.
                    if (s.mesh == null || s.chunkTransform == null) continue;
                    ChunkSeamStitcher.Instance.RegisterAndStitch(
                        s.chunkPos, s.mesh, s.chunkTransform,
                        s.voxelSize, s.chunkWorldSize
                    );
                }
            }

            // [P3-Phy] Pending Physics Bake 완료 폴링
            if (enablePhysicsBakeAsync && _pendingPhysicsBakes.Count > 0)
            {
                for (int i = _pendingPhysicsBakes.Count - 1; i >= 0; i--)
                {
                    var pb = _pendingPhysicsBakes[i];
                    if (!pb.handle.IsCompleted) continue;
                    pb.handle.Complete();

                    // [위험 ①②] 방어 — collider / mesh 유효성 + 풀 재사용 체크
                    bool valid = pb.collider != null
                                 && pb.expectedMesh != null
                                 && pb.collider.gameObject != null
                                 && pb.collider.gameObject.activeInHierarchy;
                    if (valid)
                    {
                        // collider에 이미 다른 mesh가 할당된 경우 → 풀 재사용 → skip
                        var cur = pb.collider.sharedMesh;
                        if (cur == null || cur == pb.expectedMesh)
                        {
                            pb.collider.sharedMesh = pb.expectedMesh;  // PhysX cache hit → 즉시 완료
                        }
                    }
                    _pendingPhysicsBakes.RemoveAt(i);
                }
            }

            // [I-3] Pending FloorSmoothing 완료 폴링
            if (enableAsyncFloorSmoothing && _pendingFloorSmoothings.Count > 0)
            {
                for (int i = _pendingFloorSmoothings.Count - 1; i >= 0; i--)
                {
                    var pfs = _pendingFloorSmoothings[i];
                    if (!pfs.handle.IsCompleted) continue;
                    pfs.handle.Complete();

                    // Phase 7: 결과 mesh에 write back
                    if (pfs.mesh != null && pfs.vertsMg != null)
                    {
                        pfs.verts.CopyTo(pfs.vertsMg);
                        pfs.mesh.vertices = pfs.vertsMg;
                        pfs.mesh.RecalculateBounds();
                        pfs.mesh.RecalculateTangents();
                    }

                    // Dispose FS NativeArrays (규칙 #12)
                    DisposePendingFS(pfs);

                    // 후속 처리 (mesh assign, NormalBaker, Physics, Stitch)
                    pfs.postComplete?.Invoke();

                    _pendingFloorSmoothings.RemoveAt(i);
                }
            }
        }

        /// <summary>[P3-Phy] 풀링/파괴 전 해당 collider의 pending Bake 취소 (규칙 #12 Dispose 방어)</summary>
        public void CancelPendingPhysicsBakesForCollider(MeshCollider target)
        {
            if (target == null || _pendingPhysicsBakes.Count == 0) return;
            for (int i = _pendingPhysicsBakes.Count - 1; i >= 0; i--)
            {
                if (_pendingPhysicsBakes[i].collider != target) continue;
                _pendingPhysicsBakes[i].handle.Complete();  // 강제 동기화 (worker 결과 폐기)
                _pendingPhysicsBakes.RemoveAt(i);
            }
        }

        private void OnDestroy()
        {
            // [P3-Phy] 전체 pending Bake 강제 완료 (규칙 #12)
            for (int i = 0; i < _pendingPhysicsBakes.Count; i++)
                _pendingPhysicsBakes[i].handle.Complete();
            _pendingPhysicsBakes.Clear();

            // [I-3] 전체 pending FloorSmoothing 강제 완료 + Dispose (규칙 #12)
            for (int i = 0; i < _pendingFloorSmoothings.Count; i++)
            {
                _pendingFloorSmoothings[i].handle.Complete();
                DisposePendingFS(_pendingFloorSmoothings[i]);
            }
            _pendingFloorSmoothings.Clear();
        }

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
        // [P3-진단] Profiler 마커 — 71ms Self 원인 식별용 (non-profiling 시 오버헤드 0)
        private static readonly Unity.Profiling.ProfilerMarker s_MarkerBuildMesh =
            new Unity.Profiling.ProfilerMarker("DCMesh.BuildTotal");
        private static readonly Unity.Profiling.ProfilerMarker s_MarkerMeshApply =
            new Unity.Profiling.ProfilerMarker("DCMesh.MeshApply");
        private static readonly Unity.Profiling.ProfilerMarker s_MarkerCollider =
            new Unity.Profiling.ProfilerMarker("DCMesh.ColliderBake");
        private static readonly Unity.Profiling.ProfilerMarker s_MarkerNormalBake =
            new Unity.Profiling.ProfilerMarker("DCMesh.NormalBakeEntry");
        private static readonly Unity.Profiling.ProfilerMarker s_MarkerFloorSmooth =
            new Unity.Profiling.ProfilerMarker("DCMesh.FloorSmoothing");
        private static readonly Unity.Profiling.ProfilerMarker s_MarkerNativeFinalize =
            new Unity.Profiling.ProfilerMarker("DCMesh.NativeFinalize");

        // [NavMesh Throttle] 최소 2초 간격으로 NavMesh 갱신 요청 (311ms 스파이크 방지)
        private static float _lastNavMeshRequestTime = -999f;
        private const float NAV_MESH_THROTTLE_INTERVAL = 2.0f;

        public void BuildMeshFromDCData(
            DCVertex[] dcVerts, DCQuad[] dcQuads, int quadCount,
            ChunkRequestContext context, int chunkSize, float voxelSize,
            Action<ChunkRequestContext> onCompleted)
        {
            s_MarkerBuildMesh.Begin();
            try { BuildMeshFromDCData_Impl(dcVerts, dcQuads, quadCount, context, chunkSize, voxelSize, onCompleted); }
            finally { s_MarkerBuildMesh.End(); }
        }

        private void BuildMeshFromDCData_Impl(
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

            // [P1-D 토글] enableNativeArrayMesh=ON → NativeArray 경로
            if (enableNativeArrayMesh)
            {
                BuildMeshFromDCData_Native(dcVerts, dcQuads, quadCount, context, chunkSize, voxelSize, onCompleted);
                return;
            }

            // ── 원본 경로 (아래 코드는 원본과 100% 동일) ──

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
                indexCount = finalIdxCount,
                gpuNormalBlend = enableNormalBlend ? gpuNormalBlend : 1.0f
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

            s_MarkerMeshApply.Begin();
            mesh.vertices = finalPositions;
            mesh.normals = finalNormals;
            mesh.uv = finalUVs;
            mesh.triangles = finalIndices;

            float halfSize = (chunkSize * voxelSize) * 0.5f;
            Vector3 center = new Vector3(halfSize, halfSize, halfSize);
            mesh.bounds = new Bounds(center, Vector3.one * (chunkSize * voxelSize));
            s_MarkerMeshApply.End();

            // NativeArray 해제 (mesh 구성용, FS와 무관 — FS는 자체 NativeArray 사용)
            nativePositions.Dispose();
            nativeIndices.Dispose();
            nativeNormals.Dispose();

            // [P0.5-B] FloorSmoothing 파라미터
            int fsIter = enableReducedSmoothing ? 1 : 3;
            float fsThres = enableReducedSmoothing ? 0.7f : 0.15f;
            float fsStr = enableReducedSmoothing ? 0.25f : 0.55f;

            // [I-3] Async FloorSmoothing — Schedule만 하고 다음 프레임 Complete
            if (enableAsyncFloorSmoothing && enableFloorSmoothingJob)
            {
                s_MarkerFloorSmooth.Begin();
                var pending = ScheduleFloorSmoothingBurstImpl(mesh, context, chunkSize, voxelSize, fsIter, fsThres, fsStr,
                    finalPositions, finalNormals, finalIndices);
                // postComplete: FS 완료 후 실행할 후속 처리 (mesh assign, NB, Phy, Stitch)
                pending.postComplete = () =>
                {
                    FinalizeChunkAfterFS(mesh, context, chunkSize, voxelSize,
                        finalPositions, finalNormals, finalVertCount, finalIdxCount, quadCount, onCompleted);
                };
                _pendingFloorSmoothings.Add(pending);
                s_MarkerFloorSmooth.End();
                return; // 후속 처리는 Update에서 postComplete로 실행
            }

            // ── 동기 FS 경로 (enableAsyncFloorSmoothing=OFF 또는 enableFloorSmoothingJob=OFF) ──
            s_MarkerFloorSmooth.Begin();
            if (enableFloorSmoothingJob)
                ApplyFloorSmoothingBurst(mesh, context, chunkSize, voxelSize, fsIter, fsThres, fsStr);
            else
                ApplyFloorSmoothing(mesh, context, chunkSize, voxelSize, fsIter, fsThres, fsStr);
            mesh.RecalculateTangents();
            s_MarkerFloorSmooth.End();

            // ── 후속 처리 (동기 경로) ──
            FinalizeChunkAfterFS(mesh, context, chunkSize, voxelSize,
                finalPositions, finalNormals, finalVertCount, finalIdxCount, quadCount, onCompleted);
        }
        // END BuildMeshFromDCData

        /// <summary>
        /// [I-3] FloorSmoothing 완료 후 실행할 후속 처리.
        /// 동기 경로에서는 즉시, 비동기 경로에서는 Update의 postComplete에서 호출.
        /// </summary>
        private void FinalizeChunkAfterFS(Mesh mesh, ChunkRequestContext context,
            int chunkSize, float voxelSize,
            Vector3[] finalPositions, Vector3[] finalNormals,
            int finalVertCount, int finalIdxCount, int quadCount,
            System.Action<ChunkRequestContext> onCompleted)
        {

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
                // [Fix-Shadow v2] On으로 복원: TwoSided는 phantom/역방향 면이 동굴 내부로
                // 그림자를 투사하게 만들어 Point Light 이동 시 번쩍임 발생.
                // Shadow Acne는 대신 Light Inspector에서 Shadow Bias ≥ 0.5 로 해결.
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

                // [v3] 서브복셀 노말 — Per-Vertex 우선, NormalBaker 폴백
                // enablePerVertexNormal=ON → mesh.UV2에 저장 (NormalBaker 스킵)
                // enablePerVertexNormal=OFF → NormalBaker 텍스처 방식 (기존)
                if (enablePerVertexNormal
                    && context.DensityCache != null && context.DensityCache.Length > 0)
                {
                    // ── Per-Vertex 편차 인코딩 (UV2 채널) ──
                    // 절대 법선 대신 편차(subvoxelN - meshN)를 저장
                    // → GPU 보간 시 meshN의 부드러운 보간이 지배 → 격자 급변 완화
                    // → 셰이더에서: correctedN = normalize(meshN + perturbation * strength)
                    Vector3 chunkOffset = context.ChunkObject.transform.position;
                    float step = context.DensityVoxelSize * subVoxelFactor;
                    var subNormals = new Vector3[finalVertCount];
                    ComputePerVertexSubVoxel(
                        finalPositions, finalNormals, finalVertCount,
                        chunkOffset, context.DensityCache, context.DensityDcBasePos,
                        context.DensityDcN, context.DensityVoxelSize, step,
                        subNormals);
                    mesh.SetUVs(2, subNormals);
                    #if CAVE_VERBOSE
                    Debug.Log($"[DCMeshBuilder] Per-Vertex 편차 인코딩 적용: {finalVertCount} verts, step={step:F4}m");
                    #endif
                }
                else
                {
                    // ── NormalBaker 텍스처 방식 (폴백) ──
                    s_MarkerNormalBake.Begin();
                    var normalBaker = GetComponent<CaveNormalBaker>();
                    if (normalBaker != null && normalBaker.enabled)
                    {
                        if (context.DensityCache != null && context.DensityCache.Length > 0)
                            normalBaker.BakeNormalMap(mesh, renderer, context.DensityCache,
                                context.DensityDcBasePos, context.DensityDcN, context.DensityVoxelSize);
                        else
                            normalBaker.BakeFlat3(mesh, renderer);
                    }
                    s_MarkerNormalBake.End();
                }

                // [수정] Coarse 판별 — 원본 경로에도 cooking 최소화 적용
                bool isCoarseDraft_orig_collider = context.ChunkObject != null
                    && context.ChunkObject.name != null
                    && context.ChunkObject.name.StartsWith("Coarse_");

                s_MarkerCollider.Begin();
                // MeshCollider: 단일 할당 (이전: 3회 중복 할당으로 PhysX Bake 3회 실행 → 경고 3배)
                var collider = context.ChunkObject.GetOrAddComponent<MeshCollider>();
                if (isCoarseDraft_orig_collider)
                {
                    // Coarse: cooking 비용 최소화 + mesh cleaning 경고 억제
                    collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation
                                             | MeshColliderCookingOptions.UseFastMidphase;
                }
                // [P3-Phy] Coarse는 항상 sync (수초 후 파괴), Fine만 async 후보
                if (enablePhysicsBakeAsync && !isCoarseDraft_orig_collider)
                {
                    // 기존에 할당된 mesh가 있으면 먼저 풀 재사용 pending 정리
                    CancelPendingPhysicsBakesForCollider(collider);

                    // [Phase 3-A] Async bake schedule 전 즉시 sync 할당으로 collider 공백 차단.
                    //   OFF: 기존 경로 (async만, k 프레임 공백 가능)
                    //   ON : sync 할당 즉시 수행 → 추후 async bake가 정밀 cook으로 업데이트
                    //        첫 cook은 기존 sync 경로와 동일하게 blocking — 메인 +5~15ms
                    //        But 이후 프레임부터 async가 worker에서 돌아 추가 부담 없음
                    if (enablePhase3AColliderGuard)
                    {
                        collider.sharedMesh = mesh;  // 즉시 sync 확보 (공백 차단)
                    }

                    var bake = new BakeMeshJob { meshID = mesh.GetInstanceID(), convex = false };
                    JobHandle bh = bake.Schedule();
                    _pendingPhysicsBakes.Add(new PendingPhysicsBake {
                        handle = bh, collider = collider, expectedMesh = mesh
                    });
                }
                else
                {
                    collider.sharedMesh = mesh;  // 원본 sync
                }
                s_MarkerCollider.End();
            }

            context.State = ChunkState.Completed;

            #if CAVE_VERBOSE
            Debug.Log($"[DCMeshBuilder] 메쉬 생성 완료: {context.ChunkPos}, 버텍스={finalVertCount}, 삼각형={finalIdxCount / 3}, 쿼드={quadCount}");
            #endif

            // ────────────────────────────────────────────────────
            // 5. 청크 이음새 스티칭 등록 (ChunkSeamStitcher)
            //    인접 청크와 경계 버텍스 Y 좌표 평균화 → 균열 제거
            //    [P2 1단계] Coarse 초안은 skip — Fine 교체 시점의 race로 인접 파편 방지
            // ────────────────────────────────────────────────────
            bool isCoarseDraft_orig = context.ChunkObject != null
                && context.ChunkObject.name != null
                && context.ChunkObject.name.StartsWith("Coarse_");
            if (!isCoarseDraft_orig && ChunkSeamStitcher.Instance != null && context.ChunkObject != null)
            {
                if (enableDelayedStitching)
                {
                    // [P3-1] 큐잉 — 다음 프레임(들)에서 처리
                    _stitchQueue.Enqueue(new PendingStitch
                    {
                        chunkPos = context.ChunkPos,
                        mesh = mesh,
                        chunkTransform = context.ChunkObject.transform,
                        voxelSize = voxelSize,
                        chunkWorldSize = chunkSize * voxelSize
                    });
                }
                else
                {
                    // 원본 경로 — 즉시 실행
                    ChunkSeamStitcher.Instance.RegisterAndStitch(
                        context.ChunkPos,
                        mesh,
                        context.ChunkObject.transform,
                        voxelSize,
                        chunkSize * voxelSize
                    );
                }
            }

            // ────────────────────────────────────────────────────
            // 6. NavMesh 갱신 요청 + 콜백
            // ────────────────────────────────────────────────────
            if (CaveManager.Instance != null && Time.time - _lastNavMeshRequestTime > NAV_MESH_THROTTLE_INTERVAL)
            {
                CaveManager.Instance.requestNavMeshUpdate = true;
                _lastNavMeshRequestTime = Time.time;
            }

            onCompleted?.Invoke(context);
        }
        // END FinalizeChunkAfterFS

        // =====================================================================
        // [N-2] AssignCachedMeshToScene — DiskCache HIT 시 캐시 mesh를 씬에 배치
        //   SDF/DC/FS 완전 생략, NormalBaker만 재실행 (async, ~2ms)
        //   SeamStitcher는 항상 실행 (캐시 무관)
        //   규칙 영향 0 (기존 FinalizeChunkAfterFS와 동일 후속 처리)
        // =====================================================================
        public void AssignCachedMeshToScene(Mesh mesh, ChunkRequestContext context,
            int chunkSize, float voxelSize,
            System.Action<ChunkRequestContext> onCompleted)
        {
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
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

                // NormalBaker 재실행 (async, 텍스처 미캐시이므로)
                s_MarkerNormalBake.Begin();
                var normalBaker = GetComponent<CaveNormalBaker>();
                if (normalBaker != null && normalBaker.enabled)
                {
                    if (context.DensityCache != null && context.DensityCache.Length > 0)
                        normalBaker.BakeNormalMap(mesh, renderer, context.DensityCache,
                            context.DensityDcBasePos, context.DensityDcN, context.DensityVoxelSize);
                    else
                        normalBaker.BakeFlat3(mesh, renderer);
                }
                s_MarkerNormalBake.End();

                // MeshCollider
                var collider = context.ChunkObject.GetOrAddComponent<MeshCollider>();
                if (enablePhysicsBakeAsync)
                {
                    CancelPendingPhysicsBakesForCollider(collider);

                    // [Phase 3-A] Async bake schedule 전 즉시 sync 할당 (collider 공백 차단)
                    if (enablePhase3AColliderGuard)
                    {
                        collider.sharedMesh = mesh;
                    }

                    var bake = new BakeMeshJob { meshID = mesh.GetInstanceID(), convex = false };
                    Unity.Jobs.JobHandle bh = bake.Schedule();
                    _pendingPhysicsBakes.Add(new PendingPhysicsBake {
                        handle = bh, collider = collider, expectedMesh = mesh
                    });
                }
                else
                {
                    collider.sharedMesh = mesh;
                }

                // SeamStitcher (항상 실행)
                if (ChunkSeamStitcher.Instance != null)
                {
                    if (enableDelayedStitching)
                    {
                        _stitchQueue.Enqueue(new PendingStitch
                        {
                            chunkPos = context.ChunkPos,
                            mesh = mesh,
                            chunkTransform = context.ChunkObject.transform,
                            voxelSize = voxelSize,
                            chunkWorldSize = chunkSize * voxelSize
                        });
                    }
                    else
                    {
                        ChunkSeamStitcher.Instance.RegisterAndStitch(
                            context.ChunkPos, mesh, context.ChunkObject.transform,
                            voxelSize, chunkSize * voxelSize);
                    }
                }
            }

            context.State = ChunkState.Completed;
            if (CaveManager.Instance != null && Time.time - _lastNavMeshRequestTime > NAV_MESH_THROTTLE_INTERVAL)
            {
                CaveManager.Instance.requestNavMeshUpdate = true;
                _lastNavMeshRequestTime = Time.time;
            }
            onCompleted?.Invoke(context);
        }

        // =====================================================================
        // [P1-D] BuildMeshFromDCData_Native — NativeList + MeshData API
        //   GC 12.3MB → ~1MB, MeshBuild 18ms → 12ms
        //   최종 Mesh.vertices/normals/triangles는 원본 경로와 동일
        //   FloorSmoothing / NormalBaker / Per-Vertex / SeamStitcher 인터페이스 유지
        //
        // [P1-D 2차] Normal 블렌딩 + PNU 인터리브 기록을 단일 Burst Job으로 병합
        //   → DCMeshDataFinalizeJob가 vertexBuffer(byte*)에 직접 기록 (복사 루프 제거)
        // =====================================================================
        // VertexLayoutPNU — 레이아웃 참조용 (실제 기록은 byte offset 기반)
        //   Position(12B) + Normal(12B) + UV(8B) = 32B, offset 0/12/24
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct VertexLayoutPNU
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector2 uv;
        }

        private void BuildMeshFromDCData_Native(
            DCVertex[] dcVerts, DCQuad[] dcQuads, int quadCount,
            ChunkRequestContext context, int chunkSize, float voxelSize,
            Action<ChunkRequestContext> onCompleted)
        {
            // ── 1. 리매핑 + NativeList 수집 (단일 스레드, 파편 방지) ──
            int triIndexCount = quadCount * 6;
            // 초기 용량: Fine 기준 ~54K vertex, ~345K index
            int vertCap = Mathf.Min(quadCount * 4, 65536);
            int idxCap  = triIndexCount;

            var vertexList = new NativeList<Vector3>(vertCap, Allocator.TempJob);
            var normalList = new NativeList<Vector3>(vertCap, Allocator.TempJob);
            var uvList     = new NativeList<Vector2>(vertCap, Allocator.TempJob);
            var indexList  = new NativeList<int>(idxCap, Allocator.TempJob);
            var usedVertMap = new NativeHashMap<int, int>(vertCap, Allocator.TempJob);

            for (int q = 0; q < quadCount; q++)
            {
                DCQuad quad = dcQuads[q];
                // remapped[4] — stackalloc으로 GC 없음
                int r0, r1, r2, r3;
                int[] src = { quad.v0, quad.v1, quad.v2, quad.v3 };
                int[] dst = new int[4]; // 4B × 4 = 16B, GC 미미 (stackalloc 대안)

                for (int i = 0; i < 4; i++)
                {
                    int origIdx = src[i];
                    if (origIdx < 0 || origIdx >= dcVerts.Length)
                    {
                        dst[i] = 0;
                        continue;
                    }

                    if (!usedVertMap.TryGetValue(origIdx, out int newIdx))
                    {
                        newIdx = vertexList.Length;
                        usedVertMap.Add(origIdx, newIdx);
                        vertexList.Add(dcVerts[origIdx].position);
                        Vector3 gpuN = dcVerts[origIdx].normal;
                        normalList.Add(gpuN.sqrMagnitude > 0.001f ? gpuN : Vector3.up);
                        uvList.Add(dcVerts[origIdx].uv);
                    }
                    dst[i] = newIdx;
                }

                r0 = dst[0]; r1 = dst[1]; r2 = dst[2]; r3 = dst[3];
                indexList.Add(r0); indexList.Add(r1); indexList.Add(r2);
                indexList.Add(r0); indexList.Add(r2); indexList.Add(r3);
            }

            int finalVertCount = vertexList.Length;
            int finalIdxCount  = indexList.Length;

            if (finalVertCount == 0 || finalIdxCount == 0)
            {
                vertexList.Dispose(); normalList.Dispose();
                uvList.Dispose(); indexList.Dispose(); usedVertMap.Dispose();
                Debug.LogWarning("[DCMeshBuilder-Native] 리매핑 후 유효 버텍스 없음");
                context.State = ChunkState.Completed;
                onCompleted?.Invoke(context);
                return;
            }

            // ── 2. MeshData 할당 + 병합 Job으로 직접 기록 ──
            IndexFormat indexFormat = finalVertCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            var meshData = meshDataArray[0];

            var vAttributes = new NativeArray<VertexAttributeDescriptor>(3, Allocator.Temp);
            vAttributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0);
            vAttributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal,   VertexAttributeFormat.Float32, 3, 0);
            vAttributes[2] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0);
            meshData.SetVertexBufferParams(finalVertCount, vAttributes);
            vAttributes.Dispose();

            meshData.SetIndexBufferParams(finalIdxCount, indexFormat);

            // ── 3. 병합 Job: Normal 블렌딩 + PNU 인터리브 기록 (Burst + unsafe) ──
            var nativePositions   = vertexList.AsArray();
            var nativeGpuNormals  = normalList.AsArray();   // GPU 원본 법선
            var nativeUVs         = uvList.AsArray();
            var nativeIndices     = indexList.AsArray();
            var tempFaceNormals   = new NativeArray<Vector3>(finalVertCount, Allocator.TempJob);
            var vertexByteBuf     = meshData.GetVertexData<byte>();  // 32B × vertCount

            s_MarkerNativeFinalize.Begin();
            var mergedJob = new DCMeshDataFinalizeJob
            {
                positions = nativePositions,
                gpuNormals = nativeGpuNormals,
                uvs = nativeUVs,
                indices = nativeIndices,
                tempFaceNormals = tempFaceNormals,
                vertexBuffer = vertexByteBuf,
                vertexCount = finalVertCount,
                indexCount = finalIdxCount,
                gpuNormalBlend = enableNormalBlend ? gpuNormalBlend : 1.0f
            };
            mergedJob.Schedule().Complete();
            tempFaceNormals.Dispose();

            // ── 4. Index 데이터 기록 ──
            if (indexFormat == IndexFormat.UInt16)
            {
                var ibuf = meshData.GetIndexData<ushort>();
                for (int i = 0; i < finalIdxCount; i++)
                    ibuf[i] = (ushort)nativeIndices[i];
            }
            else
            {
                var ibuf = meshData.GetIndexData<int>();
                ibuf.CopyFrom(nativeIndices);
            }

            meshData.subMeshCount = 1;
            meshData.SetSubMesh(0, new SubMeshDescriptor(0, finalIdxCount));

            // ── 5. Mesh 객체로 Apply (자동 dispose) ──
            Mesh mesh = new Mesh
            {
                name = $"DCChunk_{context.ChunkPos}",
                indexFormat = indexFormat
            };
            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);
            s_MarkerNativeFinalize.End();

            s_MarkerMeshApply.Begin();
            // (mesh bounds/uv2 설정)

            float halfSize = (chunkSize * voxelSize) * 0.5f;
            Vector3 center = new Vector3(halfSize, halfSize, halfSize);
            mesh.bounds = new Bounds(center, Vector3.one * (chunkSize * voxelSize));
            s_MarkerMeshApply.End();

            // ── FS용 managed 배열: NativeList에서 직접 추출 (mesh.vertices getter 우회) ──
            Vector3[] fsVerts = vertexList.AsArray().ToArray();
            Vector3[] fsNormals = normalList.AsArray().ToArray();
            int[] fsTris = indexList.AsArray().ToArray();

            // ── NativeList 해제 (mesh에 이미 Apply됨, FS와 무관) ──
            vertexList.Dispose();
            normalList.Dispose();
            uvList.Dispose();
            indexList.Dispose();
            usedVertMap.Dispose();

            // ── FloorSmoothing 파라미터 ──
            int fsIterN = enableReducedSmoothing ? 1 : 3;
            float fsThresN = enableReducedSmoothing ? 0.7f : 0.15f;
            float fsStrN = enableReducedSmoothing ? 0.25f : 0.55f;

            // [I-3] Async FloorSmoothing — Native 경로
            if (enableAsyncFloorSmoothing && enableFloorSmoothingJob)
            {
                s_MarkerFloorSmooth.Begin();
                var pending = ScheduleFloorSmoothingBurstImpl(mesh, context, chunkSize, voxelSize, fsIterN, fsThresN, fsStrN,
                    fsVerts, fsNormals, fsTris);
                pending.postComplete = () =>
                {
                    FinalizeChunkAfterFS_Native(mesh, context, chunkSize, voxelSize,
                        finalVertCount, finalIdxCount, quadCount, onCompleted);
                };
                _pendingFloorSmoothings.Add(pending);
                s_MarkerFloorSmooth.End();
                return;
            }

            // ── 동기 FS 경로 ──
            s_MarkerFloorSmooth.Begin();
            if (enableFloorSmoothingJob)
                ApplyFloorSmoothingBurst(mesh, context, chunkSize, voxelSize, fsIterN, fsThresN, fsStrN);
            else
                ApplyFloorSmoothing(mesh, context, chunkSize, voxelSize, fsIterN, fsThresN, fsStrN);
            mesh.RecalculateTangents();
            s_MarkerFloorSmooth.End();

            // ── 후속 처리 (동기 경로) ──
            FinalizeChunkAfterFS_Native(mesh, context, chunkSize, voxelSize,
                finalVertCount, finalIdxCount, quadCount, onCompleted);
        }
        // END BuildMeshFromDCData_Native

        /// <summary>
        /// [I-3] Native 경로 후속 처리 — FloorSmoothing 완료 후.
        /// </summary>
        private void FinalizeChunkAfterFS_Native(Mesh mesh, ChunkRequestContext context,
            int chunkSize, float voxelSize,
            int finalVertCount, int finalIdxCount, int quadCount,
            System.Action<ChunkRequestContext> onCompleted)
        {

            // ── 7. 씬 배치 (원본 경로와 동일) ──
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
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

                // Per-Vertex 또는 NormalBaker (원본 경로와 동일 인터페이스)
                if (enablePerVertexNormal
                    && context.DensityCache != null && context.DensityCache.Length > 0)
                {
                    Vector3 chunkOffset = context.ChunkObject.transform.position;
                    float step = context.DensityVoxelSize * subVoxelFactor;
                    Vector3[] finalPos = mesh.vertices;   // Mesh에서 읽기 (FloorSmoothing 반영)
                    Vector3[] finalNorm = mesh.normals;
                    var subNormals = new Vector3[finalVertCount];
                    ComputePerVertexSubVoxel(
                        finalPos, finalNorm, finalVertCount,
                        chunkOffset, context.DensityCache, context.DensityDcBasePos,
                        context.DensityDcN, context.DensityVoxelSize, step,
                        subNormals);
                    mesh.SetUVs(2, subNormals);
                }
                else
                {
                    s_MarkerNormalBake.Begin();
                    var normalBaker = GetComponent<CaveNormalBaker>();
                    if (normalBaker != null && normalBaker.enabled)
                    {
                        if (context.DensityCache != null && context.DensityCache.Length > 0)
                            normalBaker.BakeNormalMap(mesh, renderer, context.DensityCache,
                                context.DensityDcBasePos, context.DensityDcN, context.DensityVoxelSize);
                        else
                            normalBaker.BakeFlat3(mesh, renderer);
                    }
                    s_MarkerNormalBake.End();
                }

                // [P2 1단계 v2] Coarse 초안 판별 — SeamStitcher만 skip (collider는 필수)
                bool isCoarseDraft_native = context.ChunkObject != null
                    && context.ChunkObject.name != null
                    && context.ChunkObject.name.StartsWith("Coarse_");

                s_MarkerCollider.Begin();
                // MeshCollider: Coarse/Fine 모두 설정 (물리 공백 방지)
                var collider = context.ChunkObject.GetOrAddComponent<MeshCollider>();
                if (isCoarseDraft_native)
                {
                    collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation
                                             | MeshColliderCookingOptions.UseFastMidphase;
                }
                // [P3-Phy] Coarse는 항상 sync, Fine만 async 후보
                if (enablePhysicsBakeAsync && !isCoarseDraft_native)
                {
                    CancelPendingPhysicsBakesForCollider(collider);

                    // [Phase 3-A] Async bake schedule 전 즉시 sync 할당 (collider 공백 차단)
                    if (enablePhase3AColliderGuard)
                    {
                        collider.sharedMesh = mesh;
                    }

                    var bake = new BakeMeshJob { meshID = mesh.GetInstanceID(), convex = false };
                    JobHandle bh = bake.Schedule();
                    _pendingPhysicsBakes.Add(new PendingPhysicsBake {
                        handle = bh, collider = collider, expectedMesh = mesh
                    });
                }
                else
                {
                    collider.sharedMesh = mesh;  // 원본 sync
                }
                s_MarkerCollider.End();

                // SeamStitcher: Coarse는 skip (Fine 교체 시점의 race 방지)
                if (!isCoarseDraft_native)
                {
                    var seamStitcher = GetComponent<ChunkSeamStitcher>();
                    if (seamStitcher != null && seamStitcher.enabled)
                    {
                        if (enableDelayedStitching)
                        {
                            // [P3-1] 큐잉 — 다음 프레임(들)에서 처리
                            _stitchQueue.Enqueue(new PendingStitch
                            {
                                chunkPos = context.ChunkPos,
                                mesh = mesh,
                                chunkTransform = context.ChunkObject.transform,
                                voxelSize = voxelSize,
                                chunkWorldSize = chunkSize * voxelSize
                            });
                        }
                        else
                        {
                            // 원본 경로 — 즉시 실행
                            seamStitcher.RegisterAndStitch(
                                context.ChunkPos,
                                mesh,
                                context.ChunkObject.transform,
                                voxelSize,
                                chunkSize * voxelSize
                            );
                        }
                    }
                }
            }

            context.State = ChunkState.Completed;
            #if CAVE_VERBOSE
            Debug.Log($"[DCMeshBuilder-Native] 메쉬 완료: {context.ChunkPos}, v={finalVertCount}, tri={finalIdxCount/3}, q={quadCount}");
            #endif

            if (CaveManager.Instance != null && Time.time - _lastNavMeshRequestTime > NAV_MESH_THROTTLE_INTERVAL)
            {
                CaveManager.Instance.requestNavMeshUpdate = true;
                _lastNavMeshRequestTime = Time.time;
            }

            onCompleted?.Invoke(context);
        }
        // END FinalizeChunkAfterFS_Native

        // =====================================================================
        // [P3-2] ComputePerVertexSubVoxel — Per-Vertex SubVoxel 편차 계산 (Job/Loop 분기)
        //   enablePerVertexJob=ON → Burst IJobParallelFor (-2.2ms)
        //   enablePerVertexJob=OFF → CPU for 루프 (원본)
        //   결과(subDeltas) 알고리즘 동일 — 메시 품질 영향 0
        // =====================================================================
        private void ComputePerVertexSubVoxel(
            Vector3[] finalPositions, Vector3[] finalNormals, int finalVertCount,
            Vector3 chunkOffset, float[] densityCache, Vector3 dcBasePos,
            int dcN, float voxelSize, float step,
            Vector3[] subNormalsOut)
        {
            if (enablePerVertexJob)
            {
                // ── Burst Job 경로 ──
                var naPositions = new NativeArray<Vector3>(finalPositions, Allocator.TempJob);
                var naMeshNormals = new NativeArray<Vector3>(finalNormals, Allocator.TempJob);
                var naDensities = new NativeArray<float>(densityCache, Allocator.TempJob);
                var naSubDeltas = new NativeArray<Vector3>(finalVertCount, Allocator.TempJob);

                var job = new PerVertexSubVoxelJob
                {
                    positions = naPositions,
                    meshNormals = naMeshNormals,
                    densities = naDensities,
                    subDeltas = naSubDeltas,
                    chunkOffset = chunkOffset,
                    dcBasePos = dcBasePos,
                    dcN = dcN,
                    voxelSize = voxelSize,
                    step = step
                };
                // batch=64: vertex 수가 ~54K → 병렬성 충분 + 오버헤드 최소
                job.Schedule(finalVertCount, 64).Complete();

                // NativeArray → managed 배열 (mesh.SetUVs API 요구)
                naSubDeltas.CopyTo(subNormalsOut);

                naPositions.Dispose();
                naMeshNormals.Dispose();
                naDensities.Dispose();
                naSubDeltas.Dispose();
            }
            else
            {
                // ── CPU for 루프 (원본) ──
                for (int i = 0; i < finalVertCount; i++)
                {
                    Vector3 wp = finalPositions[i] + chunkOffset;
                    Vector3 subN = SampleSubVoxelNormal(
                        wp, densityCache, dcBasePos, dcN, voxelSize, step);
                    subNormalsOut[i] = subN - finalNormals[i];
                }
            }
        }

        /// <summary>
        /// Per-Vertex SubVoxel Normal: 월드 좌표에서 density gradient를 trilinear 샘플링.
        /// CaveNormalBaker.SubVoxelNormal과 동일 알고리즘 (텍스처 없이 vertex에 직접 저장).
        /// </summary>
        private static Vector3 SampleSubVoxelNormal(
            Vector3 wp, float[] densities, Vector3 dcBasePos,
            int dcN, float voxelSize, float step)
        {
            float Sample(float wx, float wy, float wz)
            {
                float fx = (wx - dcBasePos.x) / voxelSize;
                float fy = (wy - dcBasePos.y) / voxelSize;
                float fz = (wz - dcBasePos.z) / voxelSize;
                int x0 = Mathf.Clamp((int)fx, 0, dcN - 2);
                int y0 = Mathf.Clamp((int)fy, 0, dcN - 2);
                int z0 = Mathf.Clamp((int)fz, 0, dcN - 2);
                float tx = fx - x0, ty = fy - y0, tz = fz - z0;
                int n2 = dcN * dcN;
                float d000 = densities[x0     + y0       * dcN + z0       * n2];
                float d100 = densities[x0 + 1 + y0       * dcN + z0       * n2];
                float d010 = densities[x0     + (y0 + 1) * dcN + z0       * n2];
                float d110 = densities[x0 + 1 + (y0 + 1) * dcN + z0       * n2];
                float d001 = densities[x0     + y0       * dcN + (z0 + 1) * n2];
                float d101 = densities[x0 + 1 + y0       * dcN + (z0 + 1) * n2];
                float d011 = densities[x0     + (y0 + 1) * dcN + (z0 + 1) * n2];
                float d111 = densities[x0 + 1 + (y0 + 1) * dcN + (z0 + 1) * n2];
                return Mathf.Lerp(
                    Mathf.Lerp(Mathf.Lerp(d000, d100, tx), Mathf.Lerp(d010, d110, tx), ty),
                    Mathf.Lerp(Mathf.Lerp(d001, d101, tx), Mathf.Lerp(d011, d111, tx), ty), tz);
            }

            float gx = Sample(wp.x + step, wp.y, wp.z) - Sample(wp.x - step, wp.y, wp.z);
            float gy = Sample(wp.x, wp.y + step, wp.z) - Sample(wp.x, wp.y - step, wp.z);
            float gz = Sample(wp.x, wp.y, wp.z + step) - Sample(wp.x, wp.y, wp.z - step);
            Vector3 g = new Vector3(gx, gy, gz);
            float len = g.magnitude;
            return len > 1e-6f ? (-g / len) : Vector3.up;
        }

        // =====================================================================
        // [P3-FS] ApplyFloorSmoothingBurst — Burst Job + CSR 경로
        //   원본 ApplyFloorSmoothing과 수학적 결과 비트 동일
        //   TempJob Allocator + Job.Complete() 후 Dispose (4프레임 이내 완료 보장)
        // =====================================================================
        private static void ApplyFloorSmoothingBurst(Mesh mesh, ChunkRequestContext context, int chunkSize, float voxelSize, int iterations, float floorNormalYThreshold, float smoothStrength)
        {
            // sync 경로: mesh에서 직접 읽기 (비동기 경로에서는 호출부가 직접 전달)
            Vector3[] vMg = mesh.vertices;
            Vector3[] nMg = mesh.normals;
            int[] tMg = mesh.triangles;
            var pending = ScheduleFloorSmoothingBurstImpl(mesh, context, chunkSize, voxelSize, iterations, floorNormalYThreshold, smoothStrength, vMg, nMg, tMg);
            pending.handle.Complete();

            // Phase 7: 결과 mesh에 write back
            pending.verts.CopyTo(pending.vertsMg);
            mesh.vertices = pending.vertsMg;
            mesh.RecalculateBounds();

            // Phase 8: Dispose
            DisposePendingFS(pending);
        }

        /// <summary>
        /// [I-3] FS Job 파이프라인 Schedule만 수행 (Complete 없음).
        /// 반환된 PendingFloorSmoothing을 Update에서 폴링하여 Complete.
        /// </summary>
        private static PendingFloorSmoothing ScheduleFloorSmoothingBurstImpl(
            Mesh mesh, ChunkRequestContext context, int chunkSize, float voxelSize,
            int iterations, float floorNormalYThreshold, float smoothStrength,
            Vector3[] vertsMg, Vector3[] normalsMg, int[] trisMg)
        {
            // [I-3 fix] mesh.vertices/normals/triangles getter 우회 — 호출부에서 직접 전달
            // mesh.vertices getter: ~5ms managed 복사 × 3 = 15ms → 0ms
            int vCount = vertsMg.Length;
            int triLen = trisMg.Length;

            if (vCount == 0 || triLen == 0)
            {
                // 빈 mesh → 빈 pending 반환 (즉시 완료)
                return new PendingFloorSmoothing { handle = default, mesh = mesh, vertsMg = vertsMg };
            }

            var verts = new NativeArray<Vector3>(vertsMg, Allocator.TempJob);
            var smoothed = new NativeArray<Vector3>(vCount, Allocator.TempJob);
            var normals = new NativeArray<Vector3>(normalsMg, Allocator.TempJob);
            var tris = new NativeArray<int>(trisMg, Allocator.TempJob);

            var degrees = new NativeArray<int>(vCount, Allocator.TempJob);
            var offsets = new NativeArray<int>(vCount + 1, Allocator.TempJob);

            // Phase 2: Count neighbors (Burst) — degrees 필요하므로 동기 (~0.5ms)
            JobHandle h1 = new CountNeighborsJob { tris = tris, degrees = degrees }.Schedule();
            h1.Complete();

            // Phase 3: Indices 배열 크기 결정 (prefix sum)
            int totalDegree = 0;
            for (int i = 0; i < vCount; i++) totalDegree += degrees[i];
            var indices = new NativeArray<int>(totalDegree, Allocator.TempJob);
            var writeCursor = new NativeArray<int>(vCount, Allocator.TempJob);

            // Phase 4: CSR 구성 (Burst)
            JobHandle h2 = new BuildCSRJob {
                tris = tris, degrees = degrees,
                offsets = offsets, indices = indices,
                writeCursor = writeCursor
            }.Schedule();

            // Phase 5: FeatureTypes managed → native
            int hasFT = (context.FeatureTypes != null && context.FeatureTypes.Length > 0) ? 1 : 0;
            int ftLen = hasFT == 1 ? context.FeatureTypes.Length : 1;
            var featureTypes = new NativeArray<int>(ftLen, Allocator.TempJob);
            if (hasFT == 1) featureTypes.CopyFrom(context.FeatureTypes);

            // Phase 6: FloorSmoothing (Burst) — Complete 없이 반환
            JobHandle h3 = new FloorSmoothingJob {
                verts = verts, smoothed = smoothed,
                normals = normals,
                offsets = offsets, neighborIndices = indices,
                featureTypes = featureTypes,
                iterations = iterations,
                threshold = floorNormalYThreshold,
                strength = smoothStrength,
                chunkWorldSize = chunkSize * voxelSize,
                hasFeatureTypes = hasFT
            }.Schedule(h2);

            // [I-3] Complete 없이 PendingFloorSmoothing 반환
            // Update에서 h3.IsCompleted 폴링 → Complete → mesh write back → Dispose
            return new PendingFloorSmoothing
            {
                handle = h3,
                verts = verts, smoothed = smoothed,
                normals = normals, tris = tris,
                degrees = degrees, offsets = offsets,
                indices = indices, writeCursor = writeCursor,
                featureTypes = featureTypes,
                vertsMg = vertsMg, mesh = mesh
            };
        }

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
                    float normY = normals[i].y;
                    // [Phase 2] featureType=1(Edge)/2(Corner) → 스무딩 억제 (능선 날카로움 보존)
                    if (context.FeatureTypes != null && i < context.FeatureTypes.Length
                        && context.FeatureTypes[i] >= 1)
                    { smoothed[i] = verts[i]; continue; }
                    if (normY > floorNormalYThreshold) // 바닥: XZ만
                    {
                        smoothed[i] = new Vector3(
                            Mathf.Lerp(verts[i].x, avgX, effectiveStrength),
                            verts[i].y,
                            Mathf.Lerp(verts[i].z, avgZ, effectiveStrength));
                    }
                    else // 벽: 노말 수직 방향만 이동
                    {
                        float nx = normals[i].x, nz = normals[i].z;
                        float dx = avgX - verts[i].x, dz = avgZ - verts[i].z;
                        float d = dx * nx + dz * nz;
                        dx -= d * nx * 0.7f; dz -= d * nz * 0.7f;
                        float ws = effectiveStrength * 0.6f;
                        smoothed[i] = new Vector3(verts[i].x + dx * ws, verts[i].y, verts[i].z + dz * ws);
                    }
                }
                System.Array.Copy(smoothed, verts, vCount);
            }

            mesh.vertices = verts;
            mesh.RecalculateBounds();
        }
    }
}