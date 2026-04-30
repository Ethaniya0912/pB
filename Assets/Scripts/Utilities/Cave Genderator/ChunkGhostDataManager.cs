// =============================================================================
// ChunkGhostDataManager.cs  |  pB-4 Project — Phase 3-B
// =============================================================================
// [역할]
//   Phase 3-B DC Overlap Removal의 핵심 자료구조:
//   인접 chunk 간 "boundary vertex / edge intersection" 정보를 공유.
//
// [동작 원리 - Schaefer 2005 변형]
//   기존 (Phase 3-B OFF, DCPointsPerAxis=ChunkSize+3):
//     양쪽 chunk가 overlap 영역에 mesh를 각자 생성 → mesh 중복 + 파편
//
//   Phase 3-B ON (DCPointsPerAxis=ChunkSize+1):
//     각 chunk는 자기 범위[0, ChunkWorldSize]에만 mesh 생성 (overlap 없음)
//     But 경계 QEF는 인접 chunk의 boundary edge intersection 정보 필요
//     → 이 매니저가 "boundary 6면의 edge 정보"를 영구 저장/조회
//     → 인접 chunk 생성 시 이 정보를 compute shader에 주입 → seam 일치
//
// [관리 대상]
//   Dictionary<Vector3Int, ChunkGhostData>
//     key = chunk position
//     value = ChunkGhostData {
//       face[6] = { boundary edges (intersection + normal) per face cell }
//     }
//
// [규칙 준수]
//   #6 bit-identical: 매니저 자체는 enablePhase3OverlapRemoval=false 시 사용 안 됨
//                     (DCMeshBuilder가 토글 OFF면 Store/Query 호출 안 함)
//   #12 Dispose: 저장 자료구조는 managed. Boundary edges는 value struct 배열 사용
//   #23 생명주기: UnregisterChunk 공개 API → CaveChunkManager.ReturnToPool에서 호출
//   #19 SDF 합성 문서화: boundary edges의 출처는 DC compute shader의 _HermiteEdgeBuffer
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace CaveSystem
{
    /// <summary>
    /// [Phase 3-B] Chunk boundary 6면의 ghost vertex / edge 정보.
    /// 인접 chunk의 boundary QEF 계산 시 참조됨.
    /// </summary>
    public struct GhostBoundaryEdge
    {
        public Vector3 intersectionPoint;   // world-local (chunk 원점 기준)
        public Vector3 normal;              // unit vector
        public int cellIndexLinear;         // 경계면 내 cell 1D index
        public int edgeAxis;                // 0=X, 1=Y, 2=Z

        public GhostBoundaryEdge(Vector3 p, Vector3 n, int cellIdx, int axis)
        {
            intersectionPoint = p;
            normal = n;
            cellIndexLinear = cellIdx;
            edgeAxis = axis;
        }
    }

    /// <summary>
    /// [Phase 3-B] 단일 chunk의 6면 boundary 정보.
    /// face index: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z
    /// </summary>
    public class ChunkGhostData
    {
        public List<GhostBoundaryEdge>[] facesEdges = new List<GhostBoundaryEdge>[6];
        public float voxelSize;  // chunk 생성 시점 voxelSize (호환성 체크용)
        public int chunkSize;    // chunk 생성 시점 chunkSize
        public long timestamp;   // 디버깅용 기록 시각

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Gate 4-A / Phase 1] Boundary Vertex World Positions
        //   목적: Chunk A의 경계 vertex world position을 공유하여,
        //         Chunk B가 같은 경계에서 생성하는 vertex를 A값으로 덮어쓰기 →
        //         양 chunk의 boundary vertex bit-identical 보장.
        //
        //   저장 시점: DCPipelineExtension.ReadbackAsync에서 vertex readback 완료 후
        //   사용 시점: Chunk B의 FinalizeChunkAfterFS_Native 전 / 혹은 stitch pass에서
        //   face index: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z
        //
        //   Phase 1 (현재): 필드 정의 + 저장 API만
        //   Phase 2 (다음): 복원 hook + race handling + vertex 덮어쓰기
        //
        //   규칙 #6: hasBoundaryVertices=false 시 기존 동작 유지 (bit-identical)
        //
        // [Gate 4-C / Halo Bake] facesNormalsWorld 추가 (Path D)
        //   Halo-aware bake에서 경계 texel에 neighbor vertex의 normal도 기여시키려면
        //   vertex position뿐만 아니라 normal vector도 필요.
        //   facesVerticesWorld[i][j]의 normal = facesNormalsWorld[i][j]
        //   (동일 인덱스 매칭, 두 배열은 항상 쌍으로 유지)
        // ═══════════════════════════════════════════════════════════════════════════════
        public List<Vector3>[] facesVerticesWorld = new List<Vector3>[6];
        public List<Vector3>[] facesNormalsWorld = new List<Vector3>[6];   // [G4-C] 동일 순서
        public bool hasBoundaryVertices = false;   // Phase 2에서 복원 시점 확인용

        public ChunkGhostData(float vs, int cs)
        {
            voxelSize = vs;
            chunkSize = cs;
            for (int i = 0; i < 6; i++)
            {
                facesEdges[i] = new List<GhostBoundaryEdge>();
                facesVerticesWorld[i] = new List<Vector3>();   // [G4-A Phase 1]
                facesNormalsWorld[i]  = new List<Vector3>();   // [G4-C] Halo Bake용
            }
            timestamp = System.DateTime.UtcNow.Ticks;
        }

        /// <summary>이 ChunkGhostData의 총 edge 개수 (디버깅 / 메트릭)</summary>
        public int TotalEdgeCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < 6; i++)
                    total += facesEdges[i]?.Count ?? 0;
                return total;
            }
        }

        /// <summary>[G4-A Phase 1] 이 ChunkGhostData의 총 boundary vertex 개수</summary>
        public int TotalBoundaryVertexCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < 6; i++)
                    total += facesVerticesWorld[i]?.Count ?? 0;
                return total;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // [F-4 / FORMAT_VERSION 13] Ghost Density Buffer — DensitySnapshot
    // ═══════════════════════════════════════════════════════════════════════════════
    // 배경: Image 4의 chunk 경계 normal stripe는 NormalBaker의 SampleDensity가
    //       clamp((int)fx, 0, dcN-2)로 boundary 근처에서 chunk-local 오염 발생.
    //       업계 표준 Halo Exchange 패턴에 따라 인접 chunk의 density cache를 공유하여
    //       out-of-range 샘플링 시 neighbor density 조회 → gradient 수학적 seamless 보장.
    //
    // 동작:
    //   [STORE] CaveComputeDispatcher의 density readback 완료 시점에
    //           RegisterDensity(chunkPos, density, dcBasePos, dcN, voxelSize) 호출.
    //   [READ]  CaveNormalBaker.BakeNormalMap 진입 시 QueryDensityNeighbors(chunkPos)로
    //           6방향 neighbor DensitySnapshot[6] 조회 → Burst Job의 
    //           SampleDensityGhostAware()가 out-of-range에서 조회.
    //   [CLEANUP] CaveChunkManager.ReturnToPool의 UnregisterChunk(pos)가 자동 제거.
    //
    // 메모리 전략:
    //   densityCache는 ChunkRequestContext.DensityCache의 same reference 보유.
    //   Dictionary가 strong ref → context.Dispose()에도 GC되지 않음.
    //   UnregisterChunk에서 Remove → GC 허용.
    //
    // 규칙 준수: #6 (토글 OFF byte-identical), #12 (Dispose OK), #15 (FV 12→13),
    //           #23 (UnregisterChunk 이미 존재).
    // ═══════════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// [F-4] 단일 chunk의 density field snapshot.
    /// Reference-only 저장 — densityCache는 ChunkRequestContext와 공유.
    /// </summary>
    public struct DensitySnapshot
    {
        public float[] densityCache;  // dcN^3 floats (reference shared with context)
        public Vector3 dcBasePos;     // chunkBasePos - voxelSize
        public int     dcN;           // chunkSize + 3 = 110
        public float   voxelSize;     // 0.15m
        public bool    exists;        // struct 기본값(default(DensitySnapshot)) 구분용
    }

    /// <summary>
    /// [Phase 3-B] Boundary ghost cache 싱글톤.
    /// DCMeshBuilder가 chunk 완료 시 Store,
    /// CaveComputeDispatcher가 다음 chunk 생성 시 Query.
    /// [F-4 확장] Density snapshot도 병행 관리 — NormalBaker의 Halo Exchange 지원.
    /// </summary>
    public class ChunkGhostDataManager : MonoBehaviour
    {
        public static ChunkGhostDataManager Instance { get; private set; }

        [Tooltip("디버그 로그 출력")]
        public bool showDebugLogs = false;

        [Tooltip("최대 캐시된 chunk 수 (LRU 제한). 0=무제한. 기본 200은 활성 27 + 예비 170.")]
        public int maxCachedChunks = 200;

        private readonly Dictionary<Vector3Int, ChunkGhostData> _cache
            = new Dictionary<Vector3Int, ChunkGhostData>();

        // LRU tracking (maxCachedChunks 초과 시 오래된 것부터 제거)
        private readonly Queue<Vector3Int> _insertionOrder
            = new Queue<Vector3Int>();

        // ═══════════════════════════════════════════════════════════════
        // [F-4] Density snapshot 저장소 — Hermite edges와 독립적 수명
        //   Reference-only 저장이므로 메모리 오버헤드 거의 없음.
        //   context.DensityCache와 same reference 공유 → strong ref holder.
        // ═══════════════════════════════════════════════════════════════
        private readonly Dictionary<Vector3Int, DensitySnapshot> _densityCache
            = new Dictionary<Vector3Int, DensitySnapshot>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _cache.Clear();
            _insertionOrder.Clear();
            _densityCache.Clear();  // [F-4] density snapshot도 정리
        }

        // ═══════════════════════════════════════════════════════════════
        // 공개 API
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// [Store] chunk 완료 시 DCMeshBuilder가 호출.
        /// 6면 boundary edges를 cache에 저장.
        /// </summary>
        public void StoreChunkGhostData(Vector3Int chunkPos, ChunkGhostData data)
        {
            if (data == null) return;

            bool isNew = !_cache.ContainsKey(chunkPos);
            _cache[chunkPos] = data;

            if (isNew)
            {
                _insertionOrder.Enqueue(chunkPos);
                EnforceCacheLimit();
            }

            if (showDebugLogs)
                Debug.Log($"[GhostCache] Store {chunkPos}: {data.TotalEdgeCount} edges");
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Gate 4-A Phase 2 v2] SaveBoundaryVertices — 좌표계 수정
        //   Chunk readback 완료 시점에 boundary vertex world position을 Ghost Cache에 저장.
        //   Phase 3에서 인접 chunk가 복원 시 사용.
        //
        //   v2 수정 (좌표계 mismatch 해결):
        //     v1 결함: localPos (DCVertex.position) 직접 판정 — DC grid 기준 좌표라
        //               chunk 실영역 경계와 1 voxel offset 발생 → +X 감지 안 됨, -X는 overlap 감지
        //     v2 수정: world position → chunk origin 기준 local로 변환 후 판정
        //               → Phase3 ON/OFF, overlap 유무 무관하게 정확한 경계 판정
        //
        //   알고리즘:
        //     1. 기존 ChunkGhostData 조회 (없으면 신규 생성)
        //     2. facesVerticesWorld[6] 초기화 (재생성 시 stale 제거)
        //     3. 모든 vertex에 대해:
        //        a. worldPos = chunkBasePos_callerArg + localPos (caller는 dcBasePos 전달)
        //        b. chunkOriginWorld = chunkPos × chunkWorldSize (chunk 실 origin)
        //        c. rel = worldPos - chunkOriginWorld (chunk 실영역 상대 좌표)
        //        d. 6면 판정: |rel.axis - boundary| < eps
        //     4. hasBoundaryVertices = true
        //
        //   Tolerance: eps = voxelSize × 1.0 (1 voxel 폭 — overlap 영역의 vertex도 포함하여
        //              neighbor와 world position 일치하는 모든 vertex 확보)
        //
        //   규칙 #6: 토글 OFF 시 호출자가 skip → 기존 동작 bit-identical
        // ═══════════════════════════════════════════════════════════════════════════════
        public void SaveBoundaryVertices(Vector3Int chunkPos, DCVertex[] verts,
                                          Vector3 chunkBasePosArg, float voxelSize, int chunkSize)
        {
            if (verts == null || verts.Length == 0) return;

            ChunkGhostData data;
            if (!_cache.TryGetValue(chunkPos, out data))
            {
                data = new ChunkGhostData(voxelSize, chunkSize);
                _cache[chunkPos] = data;
                _insertionOrder.Enqueue(chunkPos);
                EnforceCacheLimit();
            }

            // 기존 boundary vertex/normal 초기화 (재생성 시 stale 데이터 제거)
            for (int i = 0; i < 6; i++)
            {
                data.facesVerticesWorld[i].Clear();
                data.facesNormalsWorld[i].Clear();
            }

            // [v2] chunk 실 origin 계산 — chunkPos × chunkWorldSize
            //   chunkBasePosArg는 호출자가 dcBasePos로 전달하므로 좌표계 변환 필요
            //   chunkOriginWorld = chunkBasePos_real (dc offset 없는 실 origin)
            float chunkWorldSize = chunkSize * voxelSize;
            Vector3 chunkOriginWorld = new Vector3(
                chunkPos.x * chunkWorldSize,
                chunkPos.y * chunkWorldSize,
                chunkPos.z * chunkWorldSize);

            float eps = voxelSize * 1.0f;   // 1 voxel tolerance — overlap 영역 vertex 포함

            int totalBoundary = 0;
            Vector3 minRel = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 maxRel = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            int validVertCount = 0;

            for (int i = 0; i < verts.Length; i++)
            {
                if (verts[i].featureType < 0) continue;
                validVertCount++;

                Vector3 localPos = verts[i].position;
                Vector3 worldPos = chunkBasePosArg + localPos;       // world position (기존과 수치 동일)
                Vector3 rel = worldPos - chunkOriginWorld;           // chunk 실 origin 기준 local
                Vector3 vN = verts[i].normal;                        // [G4-C] Halo Bake용 normal

                // 분포 추적 (디버깅용)
                minRel = Vector3.Min(minRel, rel);
                maxRel = Vector3.Max(maxRel, rel);

                // 6면 판정 — chunk 실영역 경계 ±eps 영역의 vertex
                //   +X face: rel.x ≈ chunkWorldSize
                //   -X face: rel.x ≈ 0
                //   (y, z 동일)
                //   한 vertex가 여러 face에 속할 수 있음 (모서리/꼭짓점)
                bool anyFace = false;
                if (Mathf.Abs(rel.x - chunkWorldSize) < eps) { data.facesVerticesWorld[0].Add(worldPos); data.facesNormalsWorld[0].Add(vN); anyFace = true; }
                if (Mathf.Abs(rel.x - 0f)              < eps) { data.facesVerticesWorld[1].Add(worldPos); data.facesNormalsWorld[1].Add(vN); anyFace = true; }
                if (Mathf.Abs(rel.y - chunkWorldSize) < eps) { data.facesVerticesWorld[2].Add(worldPos); data.facesNormalsWorld[2].Add(vN); anyFace = true; }
                if (Mathf.Abs(rel.y - 0f)              < eps) { data.facesVerticesWorld[3].Add(worldPos); data.facesNormalsWorld[3].Add(vN); anyFace = true; }
                if (Mathf.Abs(rel.z - chunkWorldSize) < eps) { data.facesVerticesWorld[4].Add(worldPos); data.facesNormalsWorld[4].Add(vN); anyFace = true; }
                if (Mathf.Abs(rel.z - 0f)              < eps) { data.facesVerticesWorld[5].Add(worldPos); data.facesNormalsWorld[5].Add(vN); anyFace = true; }
                if (anyFace) totalBoundary++;
            }

            data.hasBoundaryVertices = true;

            if (showDebugLogs)
                Debug.Log($"[GhostCache:G4A-P2-v2] SaveBoundaryVertices {chunkPos}: " +
                          $"{totalBoundary}/{validVertCount} boundary verts " +
                          $"(+X={data.facesVerticesWorld[0].Count}, -X={data.facesVerticesWorld[1].Count}, " +
                          $"+Y={data.facesVerticesWorld[2].Count}, -Y={data.facesVerticesWorld[3].Count}, " +
                          $"+Z={data.facesVerticesWorld[4].Count}, -Z={data.facesVerticesWorld[5].Count}) " +
                          $"relRange=[{minRel}~{maxRel}] chunkWorldSize={chunkWorldSize:F2}");
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Gate 4-A Phase 2 / Path B] SaveBoundaryVerticesFromMesh
        //   Cache HIT 경로용 오버로드 — DCVertex[] 대신 Vector3[] + mesh 참조로 저장.
        //   mesh.vertices는 이미 local space이므로 DCVertex.position과 동일 좌표계.
        //   Mesh에 featureType이 없으므로 모든 vertex 포함 (invalid 없음 — mesh가 이미 정제됨).
        //
        //   용도:
        //     - DCMeshBuilder.AssignCachedMeshToScene에서 호출
        //     - Cache HIT 시 mesh는 이미 고정이므로 Mirror 불필요, Save만 (neighbor 생성 시 master 역할)
        //
        //   chunkBasePosArg: 여기서는 chunk 실 origin (chunkPos × chunkWorldSize)을 전달 가능
        //     → mesh.vertices가 chunk local 좌표인지 world 좌표인지 확인 후 caller가 적절히 전달
        //     → Unity 기본: MeshFilter의 mesh.vertices는 local. Transform.position이 chunk origin
        //     → caller는 chunkTransform.position을 chunkBasePosArg로 전달 (world = transform + local)
        //
        //   규칙 #6: 토글 OFF 시 호출자가 skip
        // ═══════════════════════════════════════════════════════════════════════════════
        public void SaveBoundaryVerticesFromMesh(Vector3Int chunkPos, Vector3[] meshVerts,
                                                   Vector3 chunkBasePosArg, float voxelSize, int chunkSize,
                                                   Vector3[] meshNormals = null)
        {
            if (meshVerts == null || meshVerts.Length == 0) return;
            bool hasNormals = (meshNormals != null && meshNormals.Length == meshVerts.Length);

            ChunkGhostData data;
            if (!_cache.TryGetValue(chunkPos, out data))
            {
                data = new ChunkGhostData(voxelSize, chunkSize);
                _cache[chunkPos] = data;
                _insertionOrder.Enqueue(chunkPos);
                EnforceCacheLimit();
            }

            for (int i = 0; i < 6; i++)
            {
                data.facesVerticesWorld[i].Clear();
                data.facesNormalsWorld[i].Clear();
            }

            float chunkWorldSize = chunkSize * voxelSize;
            Vector3 chunkOriginWorld = new Vector3(
                chunkPos.x * chunkWorldSize,
                chunkPos.y * chunkWorldSize,
                chunkPos.z * chunkWorldSize);
            float eps = voxelSize * 1.0f;
            Vector3 fallbackNormal = Vector3.up;   // normal 없으면 fallback

            int totalBoundary = 0;
            for (int i = 0; i < meshVerts.Length; i++)
            {
                Vector3 localPos = meshVerts[i];
                Vector3 worldPos = chunkBasePosArg + localPos;
                Vector3 rel = worldPos - chunkOriginWorld;
                Vector3 vN = hasNormals ? meshNormals[i] : fallbackNormal;

                bool anyFace = false;
                if (Mathf.Abs(rel.x - chunkWorldSize) < eps) { data.facesVerticesWorld[0].Add(worldPos); data.facesNormalsWorld[0].Add(vN); anyFace = true; }
                if (Mathf.Abs(rel.x - 0f)              < eps) { data.facesVerticesWorld[1].Add(worldPos); data.facesNormalsWorld[1].Add(vN); anyFace = true; }
                if (Mathf.Abs(rel.y - chunkWorldSize) < eps) { data.facesVerticesWorld[2].Add(worldPos); data.facesNormalsWorld[2].Add(vN); anyFace = true; }
                if (Mathf.Abs(rel.y - 0f)              < eps) { data.facesVerticesWorld[3].Add(worldPos); data.facesNormalsWorld[3].Add(vN); anyFace = true; }
                if (Mathf.Abs(rel.z - chunkWorldSize) < eps) { data.facesVerticesWorld[4].Add(worldPos); data.facesNormalsWorld[4].Add(vN); anyFace = true; }
                if (Mathf.Abs(rel.z - 0f)              < eps) { data.facesVerticesWorld[5].Add(worldPos); data.facesNormalsWorld[5].Add(vN); anyFace = true; }
                if (anyFace) totalBoundary++;
            }

            data.hasBoundaryVertices = true;

            if (showDebugLogs)
                Debug.Log($"[GhostCache:G4A-P2/B] SaveBoundaryVerticesFromMesh {chunkPos} (cache, hasN={hasNormals}): " +
                          $"{totalBoundary}/{meshVerts.Length} boundary verts " +
                          $"(+X={data.facesVerticesWorld[0].Count}, -X={data.facesVerticesWorld[1].Count}, " +
                          $"+Y={data.facesVerticesWorld[2].Count}, -Y={data.facesVerticesWorld[3].Count}, " +
                          $"+Z={data.facesVerticesWorld[4].Count}, -Z={data.facesVerticesWorld[5].Count})");
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // [Gate 4-A Phase 3] MirrorFromNeighbors — Boundary Vertex Position Override
        //   Chunk B가 readback 직후, 6 neighbor의 Ghost Cache 조회 → 기존에 저장된
        //   neighbor boundary vertex 위치로 자기 경계 vertex 덮어쓰기.
        //   → 두 chunk의 경계 vertex world position이 bitwise 동일해짐
        //   → mesh crack / "뚫고 나오는" 현상 근본 해결
        //
        //   Master-Slave 패턴 (시간적):
        //     먼저 readback 완료한 chunk가 "master" — Ghost Cache에 저장됨
        //     나중 chunk가 "slave" — neighbor 있으면 덮어쓰기, 없으면 그대로 저장 (다음 master)
        //     → 양방향 대칭 결과 보장 (최종적으로 둘 다 master의 값 공유)
        //
        //   공간 색인:
        //     각 face의 boundary vertex를 voxel-size grid hash에 색인 → O(1) 최근접 탐색
        //     naive O(N²) 대비 chunk당 수천 연산 → 수십 연산으로 감소
        //
        //   작동:
        //     1. 각 6 face에 대해:
        //        a. neighborPos = chunkPos + offsets[face]
        //        b. neighbor의 Ghost Cache 조회 (없으면 skip — 나중 처리될 때 이 chunk가 master)
        //        c. neighbor의 "반대 face" (face^1) vertex 목록 확보
        //        d. 이를 grid hash에 색인
        //        e. 자기 verts를 순회하며 face에 속한 것 찾기 + 가장 가까운 neighbor vertex 탐색
        //        f. snapDist 이내이면 vertex.position 덮어쓰기 (world pos 차이를 localPos에 반영)
        //   반환: 덮어쓴 vertex 개수
        //
        //   규칙 #6: 토글 OFF 시 호출자가 skip → bit-identical
        //   규칙 #15: vertex 덮어쓰기로 mesh 자체 변경 → FORMAT_VERSION 승격 필수
        // ═══════════════════════════════════════════════════════════════════════════════
        public int MirrorFromNeighbors(Vector3Int chunkPos, DCVertex[] verts,
                                        Vector3 chunkBasePosArg, float voxelSize, int chunkSize,
                                        float snapDistMultiplier = 0.75f)
        {
            if (verts == null || verts.Length == 0) return 0;

            float chunkWorldSize = chunkSize * voxelSize;
            Vector3 chunkOriginWorld = new Vector3(
                chunkPos.x * chunkWorldSize,
                chunkPos.y * chunkWorldSize,
                chunkPos.z * chunkWorldSize);
            float eps = voxelSize * 1.0f;
            float snapDist = voxelSize * snapDistMultiplier;
            float snapSq = snapDist * snapDist;

            // Face offsets: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z
            var offsets = new Vector3Int[] {
                new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
                new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
            };

            int totalMirrored = 0;
            int facesProcessed = 0;
            // [DIAG] Mirror skip 원인별 통계
            int diag_totalOnFace = 0;      // 6 face에 속한 자기 vertex 총합
            int diag_noNeighborData = 0;   // neighbor Ghost Cache 없음
            int diag_outOfSnap = 0;        // on-face이지만 snap 범위 내 neighbor 없음

            // Grid hash를 재사용 (face마다 clear + fill)
            var hashBuckets = new Dictionary<long, List<Vector3>>();
            float hashCell = snapDist * 2.0f;  // bucket 크기 = 검색 반경 * 2 (인접 bucket만 조회로 충분)

            for (int face = 0; face < 6; face++)
            {
                Vector3Int neighborPos = chunkPos + offsets[face];
                if (!_cache.TryGetValue(neighborPos, out ChunkGhostData nData)) { diag_noNeighborData++; continue; }
                if (!nData.hasBoundaryVertices) { diag_noNeighborData++; continue; }

                int neighborFace = face ^ 1;   // 반대 face — A의 +X와 B의 -X는 같은 world 경계
                var neighborVerts = nData.facesVerticesWorld[neighborFace];
                if (neighborVerts == null || neighborVerts.Count == 0) continue;

                // Grid hash 빌드
                hashBuckets.Clear();
                foreach (var nv in neighborVerts)
                {
                    long key = HashCell3D(nv, hashCell);
                    if (!hashBuckets.TryGetValue(key, out var list))
                    {
                        list = new List<Vector3>();
                        hashBuckets[key] = list;
                    }
                    list.Add(nv);
                }

                // 자기 vertex 중 이 face에 속한 것들을 순회 + 덮어쓰기
                int mirroredThisFace = 0;
                for (int i = 0; i < verts.Length; i++)
                {
                    if (verts[i].featureType < 0) continue;

                    Vector3 localPos = verts[i].position;
                    Vector3 worldPos = chunkBasePosArg + localPos;
                    Vector3 rel = worldPos - chunkOriginWorld;

                    // face 판정 — SaveBoundaryVertices와 동일 공식
                    bool onFace = false;
                    switch (face)
                    {
                        case 0: onFace = Mathf.Abs(rel.x - chunkWorldSize) < eps; break;
                        case 1: onFace = Mathf.Abs(rel.x - 0f)              < eps; break;
                        case 2: onFace = Mathf.Abs(rel.y - chunkWorldSize) < eps; break;
                        case 3: onFace = Mathf.Abs(rel.y - 0f)              < eps; break;
                        case 4: onFace = Mathf.Abs(rel.z - chunkWorldSize) < eps; break;
                        case 5: onFace = Mathf.Abs(rel.z - 0f)              < eps; break;
                    }
                    if (!onFace) continue;
                    diag_totalOnFace++;

                    // 가장 가까운 neighbor vertex 탐색 (hash bucket + 인접 26 buckets)
                    Vector3? best = null;
                    float bestSq = snapSq;
                    long cx = (long)Mathf.Floor(worldPos.x / hashCell);
                    long cy = (long)Mathf.Floor(worldPos.y / hashCell);
                    long cz = (long)Mathf.Floor(worldPos.z / hashCell);
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        long key = EncodeHashKey(cx + dx, cy + dy, cz + dz);
                        if (!hashBuckets.TryGetValue(key, out var bucket)) continue;
                        foreach (var nv in bucket)
                        {
                            float d2 = (nv - worldPos).sqrMagnitude;
                            if (d2 < bestSq) { bestSq = d2; best = nv; }
                        }
                    }

                    if (best.HasValue)
                    {
                        // world pos 차이 → local pos에 반영 (덮어쓰기)
                        Vector3 newLocal = best.Value - chunkBasePosArg;
                        verts[i].position = newLocal;
                        mirroredThisFace++;
                    }
                    else
                    {
                        diag_outOfSnap++;
                    }
                }

                if (mirroredThisFace > 0) { totalMirrored += mirroredThisFace; facesProcessed++; }
            }

            if (showDebugLogs && (totalMirrored > 0 || diag_totalOnFace > 0))
                Debug.Log($"[GhostCache:G4A-P3] MirrorFromNeighbors {chunkPos}: " +
                          $"{totalMirrored} verts mirrored across {facesProcessed} face(s), " +
                          $"snapDist={snapDist:F3}m | " +
                          $"DIAG: onFace={diag_totalOnFace}, noNeighbor={diag_noNeighborData}, " +
                          $"outOfSnap={diag_outOfSnap} ({(diag_totalOnFace > 0 ? (100f * diag_outOfSnap / diag_totalOnFace):0):F1}%)");

            return totalMirrored;
        }

        // Grid hash helper — world position → 1D cell key
        private static long HashCell3D(Vector3 p, float cellSize)
        {
            long cx = (long)Mathf.Floor(p.x / cellSize);
            long cy = (long)Mathf.Floor(p.y / cellSize);
            long cz = (long)Mathf.Floor(p.z / cellSize);
            return EncodeHashKey(cx, cy, cz);
        }

        private static long EncodeHashKey(long x, long y, long z)
        {
            // Z-order 유사 - 21 bits × 3 (overflow 안전)
            return (x & 0x1FFFFF) | ((y & 0x1FFFFF) << 21) | ((z & 0x1FFFFF) << 42);
        }

        /// <summary>
        /// [Query] chunk 생성 전 CaveComputeDispatcher가 호출.
        /// 6방향 인접 chunk의 boundary 정보 수집.
        /// 없는 방향은 null을 반환 (최초 생성 / LOD cull 경우).
        /// </summary>
        public ChunkGhostData[] QueryNeighbors(Vector3Int chunkPos)
        {
            var result = new ChunkGhostData[6];
            // face 순서 맞춤: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z
            var offsets = new[] {
                new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
                new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1)
            };

            for (int f = 0; f < 6; f++)
            {
                Vector3Int neighborPos = chunkPos + offsets[f];
                if (_cache.TryGetValue(neighborPos, out ChunkGhostData data))
                    result[f] = data;
            }
            return result;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // [F-4 Ghost Density Buffer] — 3 API: Register / QueryNeighbors / TryGet
        // ═══════════════════════════════════════════════════════════════════════════════

        // ─────────────────────────────────────────────────────────────
        // [F-4-RB Re-Bake Trigger] OnDensityRegistered 이벤트
        //   배경: 새 chunk density 등록 시, 이미 bake된 인접 chunk의 normal이 해당
        //         neighbor를 반영하지 못한 상태로 남아있음 (fallback clamp).
        //         이벤트로 CaveNormalBaker에 알림 → re-bake 큐에 등록 → 완전한
        //         neighbor density 확보 후 재계산.
        //   규칙 #6: enableGhostDensityBaking=false 시 Register 호출 자체가 skip되므로
        //            이벤트도 발생 안 함 → OFF 상태 byte-identical 유지.
        // ─────────────────────────────────────────────────────────────
        public event System.Action<Vector3Int> OnDensityRegistered;

        /// <summary>
        /// [F-4 Store] CaveComputeDispatcher의 density readback 완료 시 호출.
        /// densityCache는 ChunkRequestContext.DensityCache와 same reference 공유 (strong ref).
        /// 이후 context.Dispose()로 context가 null 처리돼도 이 Dictionary가 배열 생존을 보장.
        /// [F-4-RB] Register 후 OnDensityRegistered 이벤트 발생 → 구독자(NormalBaker)가
        ///          인접 chunk re-bake 트리거.
        /// </summary>
        public void RegisterDensity(Vector3Int chunkPos, float[] density,
                                     Vector3 dcBasePos, int dcN, float voxelSize)
        {
            if (density == null || density.Length == 0) return;
            _densityCache[chunkPos] = new DensitySnapshot {
                densityCache = density,
                dcBasePos    = dcBasePos,
                dcN          = dcN,
                voxelSize    = voxelSize,
                exists       = true
            };

            // [진단] F-4-RB 디버깅용 무조건 로그 — 구독자 수 확인
            int subCount = OnDensityRegistered?.GetInvocationList().Length ?? 0;
            Debug.Log($"[GhostMgr:DIAG] RegisterDensity {chunkPos} len={density.Length}, " +
                      $"subscribers={subCount}, totalCache={_densityCache.Count}");

            if (showDebugLogs)
                Debug.Log($"[GhostCache:F-4] RegisterDensity {chunkPos}: {density.Length} floats");

            // [F-4-RB] Re-bake 트리거 발행
            OnDensityRegistered?.Invoke(chunkPos);
        }

        /// <summary>
        /// [F-4 Query] CaveNormalBaker 진입 시 호출. 6방향 neighbor snapshot 조회.
        /// 없는 방향은 exists=false인 default(DensitySnapshot) 반환.
        /// </summary>
        public DensitySnapshot[] QueryDensityNeighbors(Vector3Int chunkPos)
        {
            var result = new DensitySnapshot[6];
            var offsets = new[] {
                new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
                new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1)
            };

            for (int f = 0; f < 6; f++)
            {
                Vector3Int neighborPos = chunkPos + offsets[f];
                if (_densityCache.TryGetValue(neighborPos, out DensitySnapshot snap))
                    result[f] = snap;
                // else: result[f] = default(DensitySnapshot) = {exists=false}
            }
            return result;
        }

        /// <summary>
        /// [F-4 Direct Get] 특정 chunk의 density snapshot 직접 조회 (self density 재확인용).
        /// </summary>
        public bool TryGetDensity(Vector3Int chunkPos, out DensitySnapshot snap)
        {
            return _densityCache.TryGetValue(chunkPos, out snap);
        }

        /// <summary>
        /// [Unregister] 규칙 #23 생명주기 매트릭스.
        /// CaveChunkManager.ReturnToPool에서 호출.
        /// [F-4 확장] density snapshot도 함께 제거 → managed 배열 GC 허용
        /// </summary>
        public void UnregisterChunk(Vector3Int chunkPos)
        {
            bool removedEdges = _cache.Remove(chunkPos);
            bool removedDensity = _densityCache.Remove(chunkPos);  // [F-4]
            if (removedEdges || removedDensity)
            {
                if (showDebugLogs)
                    Debug.Log($"[GhostCache] Unregister {chunkPos} " +
                              $"(edges={removedEdges}, density={removedDensity})");
            }
        }

        /// <summary>
        /// [Clear] 월드 리셋/씬 전환 시 전체 정리.
        /// </summary>
        public void ClearAll()
        {
            _cache.Clear();
            _insertionOrder.Clear();
            _densityCache.Clear();  // [F-4]
            if (showDebugLogs)
                Debug.Log("[GhostCache] ClearAll");
        }

        /// <summary>
        /// [Query API] 특정 chunk의 ghost data 직접 조회 (테스트/디버그).
        /// </summary>
        public bool TryGetChunkGhostData(Vector3Int chunkPos, out ChunkGhostData data)
        {
            return _cache.TryGetValue(chunkPos, out data);
        }

        // ═══════════════════════════════════════════════════════════════
        // 내부
        // ═══════════════════════════════════════════════════════════════

        /// <summary>LRU 제한 강제 — 오래된 entry부터 제거</summary>
        private void EnforceCacheLimit()
        {
            if (maxCachedChunks <= 0) return;
            while (_cache.Count > maxCachedChunks && _insertionOrder.Count > 0)
            {
                var oldPos = _insertionOrder.Dequeue();
                _cache.Remove(oldPos);
            }
        }

        /// <summary>[Diagnostics] 현재 캐시 상태 요약</summary>
        public string GetCacheStats()
        {
            int totalEdges = 0;
            foreach (var kvp in _cache)
                totalEdges += kvp.Value.TotalEdgeCount;
            long totalDensityBytes = 0;
            foreach (var kvp in _densityCache)
                totalDensityBytes += (long)(kvp.Value.densityCache?.Length ?? 0) * 4;
            return $"Chunks={_cache.Count}, TotalEdges={totalEdges}, " +
                   $"AvgEdgesPerChunk={(_cache.Count > 0 ? totalEdges / _cache.Count : 0)}, " +
                   $"[F-4] DensityChunks={_densityCache.Count}, " +
                   $"DensityMem={totalDensityBytes / (1024 * 1024)}MB";
        }
    }
}
