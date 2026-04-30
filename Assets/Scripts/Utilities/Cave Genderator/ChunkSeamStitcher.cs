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

        // ═══════════════════════════════════════════════════════════════════════════════
        // [B-8] Stitcher Tuning — Dispatcher 토글과 연동
        // ═══════════════════════════════════════════════════════════════════════════════
        // Dispatcher의 enableStitching/snapDistMultiplier/enableStitchLogs 값을 읽어
        // 런타임에 동작 변경. Dispatcher가 없으면 기본값 사용 (안전 fallback).
        //
        // 연동 방식:
        //   StitchPair() 진입 시 Dispatcher 참조 조회 (null 안전)
        //   토글 OFF → stitch 미실행 (seam 보임, OFF 동작 비교용)
        //   multiplier → snapDist 동적 계산 (기존 0.75 기본값 대체)
        //   diag logs → 기존 Debug.Log을 토글 조건부로 전환
        // ═══════════════════════════════════════════════════════════════════════════════
        private CaveComputeDispatcher _dispatcher;

        private CaveComputeDispatcher GetDispatcher()
        {
            // Awake에서 한 번만 설정하되 missing 케이스 대응 위해 lazy 조회
            if (_dispatcher == null)
                _dispatcher = FindObjectOfType<CaveComputeDispatcher>();
            return _dispatcher;
        }
        // ═══════════════════════════════════════════════════════════════════════════════

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

            // [B-8] Stitching 토글 체크 — OFF 시 등록도 안 함 (seam 비교용)
            var dispatcher = GetDispatcher();
            if (dispatcher != null && !dispatcher.enableStitching) return;

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

            // [B-8] snapDist 동적 계산 — Dispatcher의 multiplier 값 사용
            //   기본: voxelSize × 0.75 (기존 동작)
            //   튜닝: multiplier 범위 0.3 ~ 2.0 (Inspector Range)
            var dispatcher = GetDispatcher();
            float multiplier = (dispatcher != null) ? dispatcher.snapDistMultiplier : 0.75f;
            float snapDist = A.voxelSize * multiplier;
            var indicesA = A.boundaryIndices[dirA];
            var indicesB = B.boundaryIndices[dirB];

            // ═══════════════════════════════════════════════════════════════════════
            // [Track E — FORMAT_VERSION 12] Normal Averaging 사전 준비
            //   enableNormalAveraging=false (기본) 시 normalsA/B는 null 유지 → 기존 로직만 실행
            //   → backup과 byte-identical 동작 보장.
            //
            //   enableNormalAveraging=true 시 매칭된 vertex 쌍의 normal도 평균화 →
            //   chunk overlap 영역에서 shader triplanar blend가 smooth하게 전환 (Issue 2 해소)
            //
            //   Normal은 direction 값이라 offsetBA (position transform)와 무관하게 평균 가능.
            //   단 두 chunk가 서로 정반대 방향을 바라볼 수는 없으므로 단순 평균 후 정규화.
            // ═══════════════════════════════════════════════════════════════════════
            bool normAvgEnabled = (dispatcher != null) && dispatcher.enableNormalAveraging;
            Vector3[] normalsA = null;
            Vector3[] normalsB = null;
            if (normAvgEnabled)
            {
                normalsA = A.mesh.normals;
                normalsB = B.mesh.normals;
                // mesh.normals가 빈 배열이거나 vertex 수와 불일치하면 skip
                if (normalsA == null || normalsA.Length != vertsA.Length) normalsA = null;
                if (normalsB == null || normalsB.Length != vertsB.Length) normalsB = null;
            }
            bool normalsModifiedA = false;
            bool normalsModifiedB = false;

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

                        // [Track E] 매칭된 쌍의 normal 평균화
                        //   position 매칭 조건(snapDist 이내)이 참일 때만 normal도 합침
                        //   → 먼 vertex의 방향과 섞이지 않음 (선명도 유지)
                        if (normAvgEnabled && normalsA != null && normalsB != null)
                        {
                            Vector3 avgN = normalsA[iA] + normalsB[iB];
                            float mag = avgN.magnitude;
                            if (mag > 0.0001f)
                            {
                                avgN /= mag; // 정규화
                                normalsA[iA] = avgN;
                                normalsB[iB] = avgN;
                                normalsModifiedA = true;
                                normalsModifiedB = true;
                            }
                        }
                        break; // 가장 가까운 쌍만 처리
                    }
                }
            }

            if (modifiedA)
            {
                A.mesh.vertices = vertsA;
                // [Track E] normal도 수정됐으면 재할당 (vertices보다 먼저가 아닌, 함께 설정)
                if (normalsModifiedA && normalsA != null)
                    A.mesh.normals = normalsA;
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
                if (normalsModifiedB && normalsB != null)
                    B.mesh.normals = normalsB;
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
                // [B-8] 상세 로그 토글 — 기본 OFF (spam 방지)
                if (dispatcher != null && dispatcher.enableStitchLogs)
                {
                    string normTag = normAvgEnabled ? " +normAvg" : "";
                    Debug.Log($"[ChunkSeamStitcher] Stitch+Rebake{normTag}: {A.chunkPos} ↔ {B.chunkPos} (dir {dirA}, snap={snapDist:F3}m)");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Gate 3 / A-α] Track E-II — Normal Map Texel Stitch
        // ═══════════════════════════════════════════════════════════════════════════════
        // 배경: F-4는 chunk별 normal gradient를 수학적으로 일치시키지만
        //       bake pipeline(rasterization + dilation + bilinear) asymmetry로
        //       경계 texel 값에 미세 차이 잔존 → 셰이더에서 색차 발생.
        //
        // 원리: Track E (mesh.normals 평균)와 같은 pairing 로직 재사용.
        //       Matched pair의 normal map texel을 양 chunk에서 읽고 평균 후 writeback.
        //
        // 구현 단계:
        //   Phase 1 (Day 1 - 현재): API 스켈레톤 + trigger hook — 호출 경로만 완성
        //   Phase 2 (Day 2): 실제 texel 평균 로직 — renderer.GetPropertyBlock → Texture2D
        //                    → GetPixel(matched UV) → 평균 → SetPixel → Apply
        //   Phase 3 (Day 3): AsyncGPUReadback 최적화 + 성능 튜닝
        //
        // 호출 시점: CaveNormalBaker.MarkChunkBaked 에서 chunk complete 도달 시
        //           → 이 chunk와 baked neighbor 간 StitchNormalMapTexels 호출
        //
        // 규칙 #6: enableNormalTexelStitch=false 시 no-op → bit-identical
        // ═══════════════════════════════════════════════════════════════════════════════

        [Header("Gate 3 / A-α — Normal Map Texel Stitch (Day 2+)")]
        [Tooltip("ON: chunk complete 시 경계 normal map texel 평균 → 색차 제거. " +
                 "OFF: Track E (mesh normal)만 작동. 기본 OFF (Day 2 완성 전까지 안전).")]
        public bool enableNormalTexelStitch = false;

        [Tooltip("경계 texel strip 두께 (pixels). 기본 4 — 경계 인접 texel 그룹 평균.")]
        [Range(1, 16)]
        public int texelStripWidth = 4;

        [Tooltip("Track E-II 디버그 로그")]
        public bool texelStitchLogs = false;

        /// <summary>
        /// [Gate 3 / A-α 실구현 v1]
        /// 두 chunk 경계의 normal map texel을 평균화. CaveNormalBaker.MarkChunkBaked에서 호출.
        ///
        /// 알고리즘:
        ///   1. 경계 vertex 쌍 (Track E pairing 재수행) — snap distance 이내 matched
        ///   2. 각 pair의 world position P에서 3채널 UV 계산 (V5 Baker와 동일 공식):
        ///        _DCNormalMap_Y: uv = (P.x, P.z) * tiling
        ///        _DCNormalMap_X: uv = (P.z, P.y) * tiling
        ///        _DCNormalMap_Z: uv = (P.x, P.y) * tiling
        ///   3. UV → pixel coord (wrap: frac), 양 chunk texture의 같은 pixel 평균
        ///   4. Apply(false) — mipmap 재생성 없이 즉시 GPU 업로드
        ///
        /// 주의: Texture2D.isReadable 필요 (new Texture2D(..., true)로 생성되어 OK)
        /// 성능: GetPixel/SetPixel per pixel — 1차 구현. 향후 GetPixels32 배치로 최적화.
        /// </summary>
        /// <param name="chunkA_pos">A chunk 좌표</param>
        /// <param name="chunkB_pos">B chunk 좌표 (A의 neighbor)</param>
        /// <param name="directionFromA">A 기준 direction index (0=+X, 1=-X, ..., 5=-Z)</param>
        public void StitchNormalMapTexels(Vector3Int chunkA_pos, Vector3Int chunkB_pos, int directionFromA)
        {
            if (!enableNormalTexelStitch) return;
            if (directionFromA < 0 || directionFromA > 5) return;

            // 캐시된 경계 데이터 조회
            if (!_cache.TryGetValue(chunkA_pos, out var A)) return;
            if (!_cache.TryGetValue(chunkB_pos, out var B)) return;
            if (A.mesh == null || B.mesh == null) return;
            if (A.chunkTransform == null || B.chunkTransform == null) return;

            // 반대 방향 인덱스 (i^1 원리): +X(0)↔-X(1), +Y(2)↔-Y(3), +Z(4)↔-Z(5)
            int dirB = directionFromA ^ 1;

            var indicesA = A.boundaryIndices[directionFromA];
            var indicesB = B.boundaryIndices[dirB];
            if (indicesA == null || indicesB == null) return;
            if (indicesA.Count == 0 || indicesB.Count == 0) return;

            // Renderer 조회 — MPB에서 normal map 3채널 확보
            var rendererA = A.chunkTransform.GetComponent<MeshRenderer>();
            var rendererB = B.chunkTransform.GetComponent<MeshRenderer>();
            if (rendererA == null || rendererB == null) return;

            var mpbA = new MaterialPropertyBlock();
            var mpbB = new MaterialPropertyBlock();
            rendererA.GetPropertyBlock(mpbA);
            rendererB.GetPropertyBlock(mpbB);

            var texAX = mpbA.GetTexture("_DCNormalMap_X") as Texture2D;
            var texAY = mpbA.GetTexture("_DCNormalMap_Y") as Texture2D;
            var texAZ = mpbA.GetTexture("_DCNormalMap_Z") as Texture2D;
            var texBX = mpbB.GetTexture("_DCNormalMap_X") as Texture2D;
            var texBY = mpbB.GetTexture("_DCNormalMap_Y") as Texture2D;
            var texBZ = mpbB.GetTexture("_DCNormalMap_Z") as Texture2D;
            if (texAX == null || texAY == null || texAZ == null
             || texBX == null || texBY == null || texBZ == null) return;
            if (!texAX.isReadable)
            {
                if (texelStitchLogs)
                    Debug.LogWarning($"[Track E-II] Texture not readable, stitch skipped for {chunkA_pos}");
                return;
            }

            int texSize = texAX.width;
            if (texSize <= 0) return;

            // tiling 값 — CaveNormalBaker와 동일 tiling 사용 (셰이더 UV 일치)
            float tiling = 0.2f;
            if (CaveNormalBaker.Instance != null)
                tiling = CaveNormalBaker.Instance.tiling;

            // snap distance — Dispatcher 설정 재사용 (기존 StitchPair와 동일 패턴)
            var dispatcher = GetDispatcher();
            float snapDist = A.voxelSize * 0.75f;
            if (dispatcher != null)
                snapDist = A.voxelSize * dispatcher.snapDistMultiplier;
            float snapSq = snapDist * snapDist;

            // world-space vertex 계산용 offset
            Vector3 offsetA = A.chunkTransform.position;
            Vector3 offsetB = B.chunkTransform.position;

            var vertsA = A.mesh.vertices;  // mesh local
            var vertsB = B.mesh.vertices;

            int stitched = 0;

            // Matched pair 순회 — A 경계 vertex 기준으로 B에서 가장 가까운 pair 찾기
            foreach (int iA in indicesA)
            {
                if (iA < 0 || iA >= vertsA.Length) continue;
                Vector3 pA_world = offsetA + vertsA[iA];

                // B에서 snap dist 이내 가장 가까운 vertex
                int bestIB = -1;
                float bestSq = snapSq;
                foreach (int iB in indicesB)
                {
                    if (iB < 0 || iB >= vertsB.Length) continue;
                    Vector3 pB_world = offsetB + vertsB[iB];
                    float d2 = (pA_world - pB_world).sqrMagnitude;
                    if (d2 < bestSq) { bestSq = d2; bestIB = iB; }
                }
                if (bestIB < 0) continue;

                // Matched! pair world position = 평균 (Track E가 이미 snap했지만 double-check)
                Vector3 pB_world2 = offsetB + vertsB[bestIB];
                Vector3 P = (pA_world + pB_world2) * 0.5f;

                // 3채널 texel 평균 — V5 Baker UV 공식 일치
                //   _DCNormalMap_Y: uv = (P.x, P.z) * tiling
                //   _DCNormalMap_X: uv = (P.z, P.y) * tiling
                //   _DCNormalMap_Z: uv = (P.x, P.y) * tiling
                // [v4] stripRadius = texelStripWidth-1 (Inspector 값, 기본 4 → radius 3 → 7x7 평균)
                //      coverage 확장 → chunk별 독립 texel의 단절 해소
                int stripRadius = Mathf.Max(0, texelStripWidth - 1);
                AverageTexelAt(texAY, texBY, P.x, P.z, tiling, texSize, stripRadius);
                AverageTexelAt(texAX, texBX, P.z, P.y, tiling, texSize, stripRadius);
                AverageTexelAt(texAZ, texBZ, P.x, P.y, tiling, texSize, stripRadius);
                stitched++;
            }

            if (stitched > 0)
            {
                // GPU 업로드 (mipmap 재생성 off — overhead 최소)
                texAX.Apply(false); texAY.Apply(false); texAZ.Apply(false);
                texBX.Apply(false); texBY.Apply(false); texBZ.Apply(false);

                if (texelStitchLogs)
                    Debug.Log($"[Track E-II] Stitched {chunkA_pos} ↔ {chunkB_pos} dir={directionFromA}: " +
                              $"{stitched} pairs (tiling={tiling:F2}, texSize={texSize})");
            }
        }

        /// <summary>
        /// [Track E-II v4] 두 texture의 UV 부근 strip 내 모든 texel을 평균.
        /// stripRadius=0이면 1 texel만 (v3 동작), stripRadius=2이면 5x5=25 texel 영역.
        /// Dilation asymmetry 보완: 경계 주변 texel들이 chunk별 독립 상태였던 것을 해결.
        /// </summary>
        private static void AverageTexelAt(Texture2D texA, Texture2D texB,
                                            float worldU, float worldV,
                                            float tiling, int texSize, int stripRadius)
        {
            float uu = worldU * tiling;
            float vv = worldV * tiling;
            // wrap [0, 1)
            uu = uu - Mathf.Floor(uu);
            vv = vv - Mathf.Floor(vv);
            int cx = Mathf.Clamp((int)(uu * texSize), 0, texSize - 1);
            int cy = Mathf.Clamp((int)(vv * texSize), 0, texSize - 1);

            // [v4] Strip radius 확장 — 경계 texel 그룹 평균으로 dilation asymmetry 보완
            //      stripRadius=0: 1 texel (v3 동작)
            //      stripRadius=1: 3x3=9 texels
            //      stripRadius=2: 5x5=25 texels
            //      stripRadius=3: 7x7=49 texels
            int rMin = -stripRadius, rMax = stripRadius;
            for (int dy = rMin; dy <= rMax; dy++)
            {
                int py = cy + dy;
                if (py < 0 || py >= texSize) continue;
                for (int dx = rMin; dx <= rMax; dx++)
                {
                    int px = cx + dx;
                    if (px < 0 || px >= texSize) continue;

                    Color cA = texA.GetPixel(px, py);
                    Color cB = texB.GetPixel(px, py);
                    Color avg = new Color(
                        (cA.r + cB.r) * 0.5f,
                        (cA.g + cB.g) * 0.5f,
                        (cA.b + cB.b) * 0.5f,
                        1f);
                    texA.SetPixel(px, py, avg);
                    texB.SetPixel(px, py, avg);
                }
            }
        }
    }
}