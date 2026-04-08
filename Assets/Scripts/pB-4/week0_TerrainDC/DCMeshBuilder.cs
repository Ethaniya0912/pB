// =============================================================================
// DCMeshBuilder.cs  |  pB-4 Project
// =============================================================================
// CaveComputeDispatcher.cs 319번째 줄 호출 시그니처:
//
//   meshBuilder.BuildMeshFromDCData(
//       dcVerts, dcQuads, quadCount,
//       context, chunkSize, voxelSize,
//       (completedCtx) => { sw.Stop(); profiler; onGpuCompleted; IsBusy=false; }
//   );
//
// ■ DC 메시 적용 흐름
//   HandleGpuResult(CaveManager) → DC 모드이면 return(무시)
//   ∴ DC 메시는 BuildMeshFromDCData 내부에서 직접 context.ChunkObject에 적용해야 함
//
//   [1] mesh 빌드 완료
//   [2] context.ChunkObject.GetComponent<MeshFilter>().sharedMesh = mesh
//   [3] context.ChunkObject.GetComponent<MeshCollider>().sharedMesh = mesh  (있으면)
//   [4] context.State = ChunkState.Completed
//   [5] onCompleted?.Invoke(context)
//       → CaveComputeDispatcher 콜백: profiler, Log, onGpuCompleted, IsBusy=false
//       → CaveChunkManager 콜백: _activeGeneratingCount--, HandleGpuResult(ignored)
//
// ■ ChunkRequestContext 실제 필드 (CaveDataStructs.cs 기준)
//   ChunkPos, State, ChunkObject, DensityCache, DensityDcN,
//   DensityDcBasePos, DensityVoxelSize, FeatureTypes
//   ※ .Mesh 필드 없음 — ChunkObject.MeshFilter로 직접 적용
//
// [변경 이력]
// FIX-MESH-APPLY : context.Mesh = mesh (없는 필드) → MeshFilter.sharedMesh 직접 적용
// FIX-SIGNATURE  : ChunkRequestContext, chunkSize int, void, Action<> onCompleted
// FIX-NORMAL v2  : GPU 70% + Face 30% 혼합 (chunk 편향 수렴 방지)
// FIX-SMOOTHING  : iterations 1, threshold 0.7, strength 0.25
// O1-UV          : DCVertex.uv 제거 → worldPos 기반 triplanar UV 재계산
// FRAG-FILTER-2  : Union-Find CC 분석 → 고립 파편 CPU 단계 제거
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CaveSystem
{
    public class DCMeshBuilder : MonoBehaviour
    {
        // ──────────────────────────────────────────────────────────────────────
        // Inspector
        // ──────────────────────────────────────────────────────────────────────

        [Header("Fragment Cleanup")]
        [Tooltip("연결 컴포넌트 쿼드 수가 이 값 미만이면 고립 파편 제거. 0=비활성.")]
        public int minQuadCount = 32;

        [Tooltip("컴포넌트 분포 Console 출력 (minQuadCount 조정 진단용)")]
        public bool logComponentStats = false;

        [Header("Floor Smoothing")]
        [Tooltip("스무딩 반복 횟수 (1 권장 — 벽면 보존)")]
        public int smoothingIterations = 1;
        [Tooltip("|normalY| 이상인 버텍스만 스무딩 (0.7 = 수평면만)")]
        public float floorNormalYThreshold = 0.7f;
        [Tooltip("스무딩 강도 0~1 (0.25 권장)")]
        public float floorSmoothStrength = 0.25f;

        [Header("Normal Blending")]
        [Tooltip("GPU SDF 노멀 기여 비율 (0.7 권장)")]
        [Range(0f, 1f)]
        public float gpuNormalWeight = 0.7f;

        // ──────────────────────────────────────────────────────────────────────
        // 공개 API
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// DC GPU Readback 완료 후 호출.
        /// <para>
        /// 메시 빌드 →  context.ChunkObject 의 MeshFilter / MeshCollider 직접 적용 →
        /// context.State = Completed → onCompleted 콜백 호출.
        /// </para>
        /// </summary>
        public void BuildMeshFromDCData(
            DCVertex[] dcVerts,
            DCQuad[] dcQuads,
            int quadCount,
            ChunkRequestContext context,
            int chunkSize,
            float voxelSize,
            Action<ChunkRequestContext> onCompleted)
        {
            // ── 조기 종료 보호 ────────────────────────────────────────────────
            if (dcVerts == null || dcQuads == null || quadCount <= 0)
            {
                Debug.LogWarning($"[DCMeshBuilder][{context?.ChunkPos}] 빈 데이터. 건너뜀.");
                if (context != null) context.State = ChunkState.Completed;
                onCompleted?.Invoke(context);
                return;
            }

            // ── Step 0: 파편 제거 (Union-Find CC 분석) ─────────────────────
            DCVertex[] workVerts = dcVerts;
            DCQuad[] workQuads = dcQuads;
            int workCount = quadCount;

            if (minQuadCount > 0)
            {
                if (logComponentStats)
                    FragmentFilter.LogComponentStats(dcVerts, dcQuads, quadCount);

                int removed = FragmentFilter.Filter(
                    dcVerts, dcQuads, quadCount, minQuadCount,
                    out workVerts, out workQuads);

                workCount = workQuads.Length;

                if (removed > 0)
                    Debug.Log($"[DCMeshBuilder][{context.ChunkPos}] " +
                              $"FRAG-FILTER-2: {removed} quads 제거 → 잔여 {workCount}");
            }

            if (workCount == 0)
            {
                Debug.Log($"[DCMeshBuilder][{context.ChunkPos}] 파편 제거 후 빈 메시. 건너뜀.");
                context.State = ChunkState.Completed;
                onCompleted?.Invoke(context);
                return;
            }

            // ── Step 1: 버텍스 목록 구축 ────────────────────────────────────
            var positions = new List<Vector3>(workCount * 2);
            var normals = new List<Vector3>(workCount * 2);
            var uvs = new List<Vector2>(workCount * 2);
            var indices = new List<int>(workCount * 6);
            var remap = new Dictionary<int, int>(workVerts.Length);
            var remapReverse = new List<int>(workVerts.Length); // 출력 idx → src idx

            for (int q = 0; q < workCount; q++)
            {
                DCQuad quad = workQuads[q];
                int[] srcIdx = { quad.v0, quad.v1, quad.v2, quad.v3 };
                int[] dstIdx = new int[4];

                for (int k = 0; k < 4; k++)
                {
                    int src = srcIdx[k];
                    if (!remap.TryGetValue(src, out int dst))
                    {
                        dst = positions.Count;
                        remap[src] = dst;
                        remapReverse.Add(src);

                        DCVertex v = workVerts[src];
                        positions.Add(v.position);
                        normals.Add(Vector3.up);   // Step 3에서 교체
                        // [O1] DCVertex.uv 제거됨 → worldPos 기반 triplanar UV
                        uvs.Add(new Vector2(v.position.x, v.position.z) * 0.1f);
                    }
                    dstIdx[k] = dst;
                }

                // quad → tri×2
                indices.Add(dstIdx[0]); indices.Add(dstIdx[1]); indices.Add(dstIdx[2]);
                indices.Add(dstIdx[0]); indices.Add(dstIdx[2]); indices.Add(dstIdx[3]);
            }

            int vCount = positions.Count;
            int triCount = indices.Count / 3;

            // ── Step 2: Face 노멀 누적 ──────────────────────────────────────
            var faceNAccum = new Vector3[vCount];
            var faceNCount = new int[vCount];

            for (int t = 0; t < triCount; t++)
            {
                int ia = indices[t * 3];
                int ib = indices[t * 3 + 1];
                int ic = indices[t * 3 + 2];
                Vector3 faceN = Vector3.Cross(
                    positions[ib] - positions[ia],
                    positions[ic] - positions[ia]);
                faceNAccum[ia] += faceN; faceNCount[ia]++;
                faceNAccum[ib] += faceN; faceNCount[ib]++;
                faceNAccum[ic] += faceN; faceNCount[ic]++;
            }

            // ── Step 3: GPU 70% + Face 30% 혼합 ────────────────────────────
            // [FIX-NORMAL v2]
            // hermite 평균 노멀이 청크 지배적 SDF 방향으로 수렴 → blendWeights 편향
            // Face 노멀 30% 혼합으로 능선 경계 자연 전환 + triplanar 패턴 복원
            float faceWeight = 1f - gpuNormalWeight;

            for (int i = 0; i < vCount; i++)
            {
                int src = remapReverse[i];

                Vector3 gpuN = workVerts[src].normal;
                gpuN = gpuN.sqrMagnitude > 0.0001f ? gpuN.normalized : Vector3.up;

                Vector3 faceN = faceNCount[i] > 0
                    ? faceNAccum[i] / faceNCount[i]
                    : Vector3.up;
                faceN = faceN.sqrMagnitude > 0.0001f ? faceN.normalized : Vector3.up;

                Vector3 blended = gpuN * gpuNormalWeight + faceN * faceWeight;
                normals[i] = blended.sqrMagnitude > 0.0001f
                    ? blended.normalized
                    : faceN;
            }

            // ── Step 4: FloorSmoothing 전 진단 ──────────────────────────────
#if UNITY_EDITOR
            LogNormalDist(context.ChunkPos, normals, "FloorSmoothing 전");
#endif

            // ── Step 5: Unity Mesh 구성 ─────────────────────────────────────
            var mesh = new Mesh();
            mesh.name = $"DCMesh_{context.ChunkPos}";
            mesh.indexFormat = vCount > 65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;

            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();

            // ── Step 6: FloorSmoothing ──────────────────────────────────────
            // [FIX-SMOOTHING] iterations 1, threshold 0.7(수평면만), strength 0.25
            ApplyFloorSmoothing(mesh, context, voxelSize,
                iterations: smoothingIterations,
                floorNormalYThreshold: floorNormalYThreshold,
                smoothStrength: floorSmoothStrength);

            // ── Step 7: FloorSmoothing 후 진단 ──────────────────────────────
#if UNITY_EDITOR
            {
                var postN = new List<Vector3>();
                mesh.GetNormals(postN);
                LogNormalDist(context.ChunkPos, postN, "FloorSmoothing 후");
            }
#endif

            // ── Step 8: ChunkObject에 메시 직접 적용 ────────────────────────
            // [FIX-MESH-APPLY]
            // DC 모드에서 CaveManager.HandleGpuResult는 return(무시)하므로
            // 메시를 여기서 직접 context.ChunkObject 에 적용해야 한다.
            // context.Mesh 필드는 ChunkRequestContext에 존재하지 않음.
            if (context.ChunkObject != null)
            {
                var mf = context.ChunkObject.GetComponent<MeshFilter>();
                if (mf != null)
                    mf.sharedMesh = mesh;
                else
                    Debug.LogWarning($"[DCMeshBuilder][{context.ChunkPos}] " +
                                     "ChunkObject에 MeshFilter가 없습니다.");

                var mc = context.ChunkObject.GetComponent<MeshCollider>();
                if (mc != null)
                    mc.sharedMesh = mesh;

                // [v3 복원] 서브복셀 노말맵 베이킹 (renderer 적용 후)
                var normalBaker = GetComponent<CaveNormalBaker>();
                if (normalBaker != null)
                {
                    var renderer = context.ChunkObject.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        if (context.DensityCache != null && context.DensityCache.Length > 0)
                            normalBaker.BakeNormalMap(mesh, renderer, context.DensityCache,
                                context.DensityDcBasePos, context.DensityDcN, context.DensityVoxelSize);
                        else
                            normalBaker.BakeFlat3(mesh, renderer);
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[DCMeshBuilder][{context.ChunkPos}] " +
                                 "context.ChunkObject가 null입니다. " +
                                 "CaveChunkManager에서 ChunkObject 할당 여부를 확인하세요.");
            }

            // ── Step 9: 완료 처리 ───────────────────────────────────────────
            context.State = ChunkState.Completed;
            onCompleted?.Invoke(context);
            // ↑ CaveComputeDispatcher 람다: sw.Stop(), profiler, onGpuCompleted, IsBusy=false
        }

        // ──────────────────────────────────────────────────────────────────────
        // Floor Smoothing
        // ──────────────────────────────────────────────────────────────────────

        private void ApplyFloorSmoothing(
            Mesh mesh,
            ChunkRequestContext context,
            float voxelSize,
            int iterations = 1,
            float floorNormalYThreshold = 0.7f,
            float smoothStrength = 0.25f)
        {
            Vector3[] verts = mesh.vertices;
            Vector3[] norms = mesh.normals;
            int[] tris = mesh.triangles;
            int vCnt = verts.Length;

            var accumN = new Vector3[vCnt];
            var accumC = new int[vCnt];
            int total = 0;

            for (int iter = 0; iter < iterations; iter++)
            {
                Array.Clear(accumN, 0, vCnt);
                Array.Clear(accumC, 0, vCnt);

                for (int t = 0; t < tris.Length; t += 3)
                {
                    int ia = tris[t], ib = tris[t + 1], ic = tris[t + 2];
                    Vector3 avg = (norms[ia] + norms[ib] + norms[ic]) / 3f;
                    accumN[ia] += avg; accumC[ia]++;
                    accumN[ib] += avg; accumC[ib]++;
                    accumN[ic] += avg; accumC[ic]++;
                }

                int iterHit = 0;
                for (int i = 0; i < vCnt; i++)
                {
                    // threshold=0.7 → |normalY| > 0.7 수평면만 대상 (벽면 보존)
                    if (Mathf.Abs(norms[i].y) < floorNormalYThreshold) continue;
                    if (accumC[i] == 0) continue;
                    Vector3 avg = accumN[i] / accumC[i];
                    if (avg.sqrMagnitude < 0.0001f) continue;
                    norms[i] = Vector3.Lerp(norms[i], avg.normalized, smoothStrength).normalized;
                    iterHit++;
                }
                total += iterHit;
            }

            mesh.SetNormals(norms);

            Debug.Log($"[DCMeshBuilder][{context.ChunkPos}] ApplyFloorSmoothing\n" +
                      $"  iter={iterations} threshold={floorNormalYThreshold} strength={smoothStrength}\n" +
                      $"  처리={total}/{vCnt} " +
                      $"({(vCnt > 0 ? (float)total / vCnt * 100f : 0f):F1}%)" +
                      " ※ 비율이 낮을수록 벽면 보존됨");
        }

        // ──────────────────────────────────────────────────────────────────────
        // 진단 헬퍼 (UNITY_EDITOR only)
        // ──────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        private static void LogNormalDist(
            Vector3Int chunkPos, List<Vector3> norms, string label)
        {
            int domX = 0, domY = 0, domZ = 0, wall = 0;
            float sumY = 0f;
            foreach (var n in norms)
            {
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                if (ax > ay && ax > az) domX++;
                else if (ay > az) domY++;
                else domZ++;
                if (ay < 0.15f) wall++;
                sumY += ay;
            }
            float avg = norms.Count > 0 ? sumY / norms.Count : 0f;
            Debug.Log($"[DCMeshBuilder][{chunkPos}] ★ {label}\n" +
                      $"  총={norms.Count} domX={domX} domY={domY} domZ={domZ}\n" +
                      $"  wall(|ny|<0.15)={wall} avgAbsNY={avg:F3}");
        }
#endif

        // ──────────────────────────────────────────────────────────────────────
        // Fragment Filter
        // ──────────────────────────────────────────────────────────────────────

        private static class FragmentFilter
        {
            public static int Filter(
                DCVertex[] verts,
                DCQuad[] quads,
                int quadCount,
                int minQuads,
                out DCVertex[] outVerts,
                out DCQuad[] outQuads)
            {
                int V = verts.Length;
                int[] par = new int[V];
                int[] rank = new int[V];
                for (int i = 0; i < V; i++) par[i] = i;

                for (int q = 0; q < quadCount; q++)
                {
                    Union(par, rank, quads[q].v0, quads[q].v1);
                    Union(par, rank, quads[q].v1, quads[q].v2);
                    Union(par, rank, quads[q].v2, quads[q].v3);
                }

                var compSize = new Dictionary<int, int>(64);
                for (int q = 0; q < quadCount; q++)
                {
                    int root = Find(par, quads[q].v0);
                    compSize.TryGetValue(root, out int c);
                    compSize[root] = c + 1;
                }

                var valid = new List<DCQuad>(quadCount);
                int removed = 0;
                for (int q = 0; q < quadCount; q++)
                {
                    int root = Find(par, quads[q].v0);
                    if (compSize[root] >= minQuads) valid.Add(quads[q]);
                    else removed++;
                }

                if (removed == 0)
                {
                    outVerts = verts;
                    outQuads = quads;
                    return 0;
                }

                bool[] used = new bool[V];
                foreach (var q in valid)
                    used[q.v0] = used[q.v1] = used[q.v2] = used[q.v3] = true;

                int[] remap = new int[V];
                var newVerts = new List<DCVertex>(V);
                for (int i = 0; i < V; i++)
                {
                    remap[i] = used[i] ? newVerts.Count : -1;
                    if (used[i]) newVerts.Add(verts[i]);
                }

                var newQuads = new DCQuad[valid.Count];
                for (int i = 0; i < valid.Count; i++)
                {
                    var q = valid[i];
                    newQuads[i] = new DCQuad
                    {
                        v0 = remap[q.v0],
                        v1 = remap[q.v1],
                        v2 = remap[q.v2],
                        v3 = remap[q.v3]
                    };
                }

                outVerts = newVerts.ToArray();
                outQuads = newQuads;
                return removed;
            }

            public static void LogComponentStats(
                DCVertex[] verts, DCQuad[] quads, int quadCount)
            {
                if (verts == null || quads == null || quadCount <= 0) return;
                int V = verts.Length;
                int[] par = new int[V];
                int[] rank = new int[V];
                for (int i = 0; i < V; i++) par[i] = i;
                for (int q = 0; q < quadCount; q++)
                {
                    Union(par, rank, quads[q].v0, quads[q].v1);
                    Union(par, rank, quads[q].v1, quads[q].v2);
                    Union(par, rank, quads[q].v2, quads[q].v3);
                }
                var cs = new Dictionary<int, int>(64);
                for (int q = 0; q < quadCount; q++)
                {
                    int r = Find(par, quads[q].v0);
                    cs.TryGetValue(r, out int c); cs[r] = c + 1;
                }
                int tiny = 0, sm = 0, med = 0, lg = 0;
                foreach (int sz in cs.Values)
                {
                    if (sz < 16) tiny++;
                    else if (sz < 64) sm++;
                    else if (sz < 512) med++;
                    else lg++;
                }
                Debug.Log($"[FragmentFilter] {cs.Count}개 컴포넌트 | " +
                          $"tiny<16={tiny} small<64={sm} med<512={med} large={lg} | " +
                          $"quads={quadCount}");
            }

            private static int Find(int[] p, int x)
            {
                while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; }
                return x;
            }

            private static void Union(int[] p, int[] rank, int a, int b)
            {
                int ra = Find(p, a), rb = Find(p, b);
                if (ra == rb) return;
                if (rank[ra] < rank[rb]) { int t = ra; ra = rb; rb = t; }
                p[rb] = ra;
                if (rank[ra] == rank[rb]) rank[ra]++;
            }
        }
    }
}