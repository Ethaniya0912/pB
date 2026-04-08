// =============================================================================
// ChunkSeamStitcher.cs  |  pB-4 Project — Week 0~1
// =============================================================================
// ChunkBorderCache Option 3 구현.
// 각 청크 완료 시 경계면 버텍스 데이터를 캐시하고,
// 인접 청크가 완료되면 경계 버텍스 Y좌표를 평균화하여 이음새 균열 제거.
//
// [동작 원리]
//   청크 A (pos 0,0,0) 완료 → +X 경계 버텍스 저장
//   청크 B (pos 1,0,0) 완료 → -X 경계 버텍스 저장
//   → 두 경계가 모두 캐시되면 Stitch Pass 실행:
//      동일 XZ 위치의 버텍스 쌍을 찾아 Y 평균 적용 → 두 메쉬 모두 업데이트
//
// [통합 방법]
//   CaveChunkManager에 이 컴포넌트를 AddComponent 또는 MonoBehaviour에 통합
//   DCMeshBuilder.BuildMeshFromDCData 완료 후 RegisterChunkBoundary 호출
// =============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace CaveSystem
{
    /// <summary>청크 경계 버텍스 데이터 (스티칭에 필요한 최소 정보)</summary>
    public class ChunkBoundaryData
    {
        public Vector3Int chunkPos;
        public Mesh mesh;
        public Transform chunkTransform; // 청크 오브젝트의 Transform
        public float voxelSize;
        public float chunkWorldSize;

        // 6방향 경계 버텍스 인덱스: [0]=+X, [1]=-X, [2]=+Y, [3]=-Y, [4]=+Z, [5]=-Z
        public List<int>[] boundaryIndices = new List<int>[6]
        {
            new List<int>(), new List<int>(),
            new List<int>(), new List<int>(),
            new List<int>(), new List<int>()
        };
    }

    /// <summary>
    /// 청크 경계 이음새 스티칭 관리자.
    /// CaveChunkManager와 함께 사용하거나 싱글톤으로 운용.
    /// </summary>
    public class ChunkSeamStitcher : MonoBehaviour
    {
        // ── 정적 방향 배열 ────────────────────────────────────────────────
        private static readonly Vector3Int[] Dirs = {
            new Vector3Int( 1, 0, 0),  // 0: +X
            new Vector3Int(-1, 0, 0),  // 1: -X
            new Vector3Int( 0, 1, 0),  // 2: +Y
            new Vector3Int( 0,-1, 0),  // 3: -Y
            new Vector3Int( 0, 0, 1),  // 4: +Z
            new Vector3Int( 0, 0,-1),  // 5: -Z
        };
        // 방향 i의 반대 방향 인덱스 (i^1 = i XOR 1)
        // +X(0) ↔ -X(1),  +Y(2) ↔ -Y(3),  +Z(4) ↔ -Z(5)

        // ── 캐시 ──────────────────────────────────────────────────────────
        private readonly Dictionary<Vector3Int, ChunkBoundaryData> _cache
            = new Dictionary<Vector3Int, ChunkBoundaryData>();

        [Header("Debug")]
        [SerializeField] private int stitchedPairCount;

        // ── 싱글톤 ────────────────────────────────────────────────────────
        public static ChunkSeamStitcher Instance { get; private set; }
        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ─────────────────────────────────────────────────────────────────
        // 공개 API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 청크 메쉬 완성 직후 호출. 경계 버텍스를 분류하고 인접 청크와 스티칭.
        /// </summary>
        public void RegisterAndStitch(
            Vector3Int chunkPos, Mesh mesh, Transform chunkTransform,
            float voxelSize, float chunkWorldSize)
        {
            if (mesh == null) return;

            var data = BuildBoundaryData(chunkPos, mesh, chunkTransform, voxelSize, chunkWorldSize);
            _cache[chunkPos] = data;

            // 6방향 인접 청크와 스티칭 시도
            for (int d = 0; d < 6; d++)
            {
                Vector3Int neighborPos = chunkPos + Dirs[d];
                if (_cache.TryGetValue(neighborPos, out ChunkBoundaryData neighborData))
                {
                    StitchPair(data, d, neighborData, d ^ 1);
                }
            }
        }

        /// <summary>청크 제거 시 캐시에서 삭제</summary>
        public void UnregisterChunk(Vector3Int chunkPos)
        {
            _cache.Remove(chunkPos);
        }

        // ─────────────────────────────────────────────────────────────────
        // 내부 구현
        // ─────────────────────────────────────────────────────────────────

        /// <summary>메쉬에서 경계 버텍스 인덱스 분류</summary>
        private static ChunkBoundaryData BuildBoundaryData(
            Vector3Int chunkPos, Mesh mesh, Transform t,
            float voxelSize, float chunkWorldSize)
        {
            var data = new ChunkBoundaryData
            {
                chunkPos = chunkPos,
                mesh = mesh,
                chunkTransform = t,
                voxelSize = voxelSize,
                chunkWorldSize = chunkWorldSize
            };

            Vector3[] verts = mesh.vertices;
            float snap = voxelSize * 0.6f; // 경계 감지 거리

            for (int i = 0; i < verts.Length; i++)
            {
                float x = verts[i].x, y = verts[i].y, z = verts[i].z;

                if (x >= chunkWorldSize - snap) data.boundaryIndices[0].Add(i); // +X
                if (x <= snap) data.boundaryIndices[1].Add(i); // -X
                if (y >= chunkWorldSize - snap) data.boundaryIndices[2].Add(i); // +Y
                if (y <= snap) data.boundaryIndices[3].Add(i); // -Y
                if (z >= chunkWorldSize - snap) data.boundaryIndices[4].Add(i); // +Z
                if (z <= snap) data.boundaryIndices[5].Add(i); // -Z
            }
            return data;
        }

        /// <summary>
        /// 두 청크의 접촉 경계에서 버텍스 Y좌표 평균화.
        /// dirA: 청크 A의 어느 면이 청크 B와 접하는지 (0~5)
        /// dirB: 청크 B의 어느 면이 청크 A와 접하는지 (= dirA ^ 1)
        /// </summary>
        private void StitchPair(ChunkBoundaryData A, int dirA,
                                 ChunkBoundaryData B, int dirB)
        {
            // XZ/YZ/XY 면에 따라 다른 비교 함수 사용
            // +X(0)/-X(1): YZ 평면에서 (y,z) 좌표 비교
            // +Y(2)/-Y(3): XZ 평면에서 (x,z) 좌표 비교
            // +Z(4)/-Z(5): XY 평면에서 (x,y) 좌표 비교

            Vector3[] vertsA = A.mesh.vertices;
            Vector3[] vertsB = B.mesh.vertices;

            // 청크 B의 로컬→A의 로컬 변환 오프셋
            Vector3 offsetBA = Vector3.zero;
            if (A.chunkTransform != null && B.chunkTransform != null)
                offsetBA = B.chunkTransform.position - A.chunkTransform.position;

            bool modifiedA = false;
            bool modifiedB = false;

            // [Phase 1] Soft Snap 잔류 갭(±0.25m) 흡수를 위해 0.55→0.75
            float snapDist = A.voxelSize * 0.75f;
            var indicesA = A.boundaryIndices[dirA];
            var indicesB = B.boundaryIndices[dirB];

            // 각 버텍스 쌍에 대해 XZ 또는 적절한 2D 위치 비교
            foreach (int iA in indicesA)
            {
                Vector3 vA = vertsA[iA];
                foreach (int iB in indicesB)
                {
                    // B의 로컬 좌표를 A의 좌표계로 변환
                    Vector3 vBinA = vertsB[iB] + offsetBA;
                    float d2 = 0;

                    // 접합 축(dirA/2)에 수직한 2축에서 거리 계산
                    int axis = dirA / 2; // 0=X, 1=Y, 2=Z
                    if (axis == 0) d2 = (vA.y - vBinA.y) * (vA.y - vBinA.y) + (vA.z - vBinA.z) * (vA.z - vBinA.z);
                    else if (axis == 1) d2 = (vA.x - vBinA.x) * (vA.x - vBinA.x) + (vA.z - vBinA.z) * (vA.z - vBinA.z);
                    else d2 = (vA.x - vBinA.x) * (vA.x - vBinA.x) + (vA.y - vBinA.y) * (vA.y - vBinA.y);

                    if (d2 < snapDist * snapDist)
                    {
                        // 접합 축 좌표의 평균값 적용
                        float avgVal;
                        if (axis == 0) avgVal = (vA.x + vBinA.x) * 0.5f;
                        else if (axis == 1) avgVal = (vA.y + vBinA.y) * 0.5f;
                        else avgVal = (vA.z + vBinA.z) * 0.5f;

                        if (axis == 0) { vertsA[iA].x = avgVal; vertsB[iB].x = avgVal - offsetBA.x; }
                        else if (axis == 1) { vertsA[iA].y = avgVal; vertsB[iB].y = avgVal - offsetBA.y; }
                        else { vertsA[iA].z = avgVal; vertsB[iB].z = avgVal - offsetBA.z; }

                        modifiedA = true; modifiedB = true;
                        break; // 가장 가까운 쌍만 처리
                    }
                }
            }

            if (modifiedA)
            {
                A.mesh.vertices = vertsA;
                A.mesh.RecalculateBounds();
                // MeshCollider 재베이크 — sharedMesh 재할당으로 강제 갱신 (PhysicsBakeJob 미사용)
                if (A.chunkTransform != null)
                {
                    var col = A.chunkTransform.GetComponent<MeshCollider>();
                    if (col != null) { col.sharedMesh = null; col.sharedMesh = A.mesh; }
                }
            }
            if (modifiedB)
            {
                B.mesh.vertices = vertsB;
                B.mesh.RecalculateBounds();
                if (B.chunkTransform != null)
                {
                    var col = B.chunkTransform.GetComponent<MeshCollider>();
                    if (col != null) { col.sharedMesh = null; col.sharedMesh = B.mesh; }
                }
            }

            if (modifiedA || modifiedB)
            {
                stitchedPairCount++;
                Debug.Log($"[ChunkSeamStitcher] Stitch+Rebake: {A.chunkPos} ↔ {B.chunkPos} (dir {dirA})");
            }
        }
    }
}