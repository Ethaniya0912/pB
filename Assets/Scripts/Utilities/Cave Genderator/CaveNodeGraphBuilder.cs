using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace CaveSystem
{
    /// <summary>
    /// [Phase 2] 기획자의 의도가 반영된 수학적 3D 그래프(방과 통로)를 메인 스레드에서 생성하는 핵심 두뇌 클래스입니다.
    /// 
    /// [Tier 2 추가 (규칙 #17)]
    ///   T2-1: ValidateEdgeSeparation - Loop edge 중 기존 edge와 너무 가까운 것 제거
    ///   T2-2: ApplySeparationSteering - Node 위치를 flocking separation으로 조정
    ///   T2-3: Gizmo 시각화 & 로그
    /// 
    /// [규칙 준수]
    ///   #1 sdCapsule 건드리지 않음 (GPU shader 영역 - 본 파일 무관)
    ///   #3 GenerateQuads 건드리지 않음 (GPU compute - 본 파일 무관)
    ///   #5 Range attribute를 추가하지 않음 (DepthLayer SO와 무관하나 동일 원칙 적용)
    ///   #6 토글 OFF 시 Random 호출 순서/결과 bit-identical (T2 로직은 Random 호출 안 함)
    ///   #13 voxelSize = 0.15m (edgeSeparationVoxelSize 필드로 노출, 기본값 일치)
    ///   #17 Non-manifold 위험 구역 사전 검증 - 본 Tier가 규칙 #17 구현체
    /// </summary>
    public class CaveNodeGraphBuilder : MonoBehaviour
    {
        public static CaveNodeGraphBuilder Instance { get; private set; }

        [Header("Dungeon Graph Rules (던전 규모)")]
        public int targetRoomCount = 20;
        public Vector3 dungeonBounds = new Vector3(300, 50, 300);

        [Header("Room Sizing (방 크기 제어)")]
        public float minRoomRadius = 8.0f;
        public float maxRoomRadius = 15.0f;
        public float bossRoomRadiusMultiplier = 2.0f;

        [Header("Connection Rules (통로 생성 규칙)")]
        public float mainPathWidth = 8.0f;  // 보스방으로 가는 메인 통로
        public float sidePathWidth = 4.0f;  // 일반/보물방 곁가지 통로
        [Range(0f, 1f)] public float loopProbability = 0.15f; // 막다른 길을 방지하는 루프 생성 확률

        // ─────────────────────────────────────────────────────────────
        // [Tier 2] Non-Manifold 예방 (규칙 #17)
        // 본 블록은 토글 OFF 시 기존 코드와 bit-identical 동작 보장.
        // 모든 T2 로직은 Random 호출을 하지 않으므로 seed-determinism 영향 없음.
        // ─────────────────────────────────────────────────────────────
        [Header("Tier 2: Non-Manifold Prevention (규칙 #17)")]
        [Tooltip("T2-2: Node 위치 separation steering. OFF 시 원본과 동일.")]
        public bool enableT2NodeSeparation = false;

        [Tooltip("T2-1: Loop edge 중 기존 edge와 너무 가까운 것 제거. OFF 시 원본과 동일.")]
        public bool enableT2EdgeValidation = false;

        [Tooltip("Voxel 크기 (규칙 #13). Nyquist 안전 거리 = 2 × 이 값.")]
        public float edgeSeparationVoxelSize = 0.15f;

        [Tooltip("Node separation 반복 횟수 제한. 30 근처 권장.")]
        public int nodeSeparationMaxIterations = 30;

        [Tooltip("Node separation 시 Y축 이동 약화 계수 (2.5D 던전 원칙 준수).")]
        public float nodeSeparationYDamping = 0.1f;

        [Tooltip("T2-3: 시각화 기즈모 (violation edges 빨강 표시).")]
        public bool enableT2DebugGizmos = true;

        // ─────────────────────────────────────────────────────────────
        // [Phase 1.5 / 옵션 A] Floor Y-Clamp (FragB 원인 3 대응)
        //   통로 노드 Y 좌표가 FloorAltitude 근처로 가는 것을 방지.
        //   규칙 #6: OFF 시 원본 코드와 bit-identical (Random 호출 없음).
        //   규칙 #17 보완: floor-tunnel smax corner 유발 조건을 SDF 이전 단계에서 차단.
        // ─────────────────────────────────────────────────────────────
        [Header("Phase 1.5: Floor Y-Clamp (규칙 #17 보완)")]
        [Tooltip("옵션 A: Node Y 좌표를 FloorAltitude + margin 이상으로 강제. OFF 시 원본과 bit-identical.")]
        public bool enableFloorYClamp = false;

        [Tooltip("FloorAltitude 참조값 — CaveBiomeSettings의 minAltitude와 일치해야 함. 기본 -5.0")]
        public float floorAltitudeReference = -5.0f;

        [Tooltip("통로 Y가 FloorAltitude 위로 최소 이 만큼 떨어져야 함. 권장: sidePathWidth/2 + 4 ≈ 6.0")]
        public float tunnelYMinHeightAboveFloor = 6.0f;

        // 생성된 최종 데이터
        [HideInInspector] public List<NodeData> nodesData = new List<NodeData>();
        [HideInInspector] public List<EdgeData> edgesData = new List<EdgeData>();

        // 디버깅/기즈모 전용 데이터 캐싱
        private List<Vector3> rawPositions = new List<Vector3>();
        private Dictionary<int, int> roomTypes = new Dictionary<int, int>();
        private Dictionary<int, float> roomRadii = new Dictionary<int, float>();
        private List<Vector2Int> allEdges = new List<Vector2Int>();
        private HashSet<Vector2Int> criticalPathEdges = new HashSet<Vector2Int>();

        // [T2-1] MST 확정 edge만 별도 저장 - loop edge 구분용.
        // 본 필드는 BuildMSTAndLoops에서 항상 채워짐 (토글 OFF여도 영향 無).
        // 메모리: 30 edges × 8B ≈ 240B. 무시 가능.
        private HashSet<Vector2Int> mstConfirmedEdges = new HashSet<Vector2Int>();

        // [T2-3] Gizmo용: T2-1 실행 시 제거된 edge 기록.
        private List<(Vector3 a, Vector3 b)> t2RemovedLoopEdges = new List<(Vector3, Vector3)>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// 주어진 Seed를 바탕으로 100% 결정론적(Deterministic)인 노드 그래프를 생성합니다.
        /// </summary>
        public void GenerateGraph(int seed)
        {
            // 1. 초기화 및 난수 고정
            Random.InitState(seed);
            nodesData.Clear();
            edgesData.Clear();
            rawPositions.Clear();
            roomTypes.Clear();
            roomRadii.Clear();
            allEdges.Clear();
            criticalPathEdges.Clear();
            mstConfirmedEdges.Clear();
            t2RemovedLoopEdges.Clear();

            // 2. Poisson Disk 형태의 거리 기반 배척 노드 스폰 (방 흩뿌리기)
            SpawnNodes();

            // [Phase 1.5 / 옵션 A] Floor Y-Clamp (규칙 #17 보완, FragB 원인 3 대응).
            //   SpawnNodes 직후, T2-2 separation 이전에 적용.
            //   Random 호출 없음 → seed 영향 없음.
            //   OFF 시 rawPositions 기존 유지 → 원본과 bit-identical.
            //   T2-2가 이후 Y를 damping하므로, 여기서 먼저 clamp하여 하한 확보.
            if (enableFloorYClamp)
            {
                ApplyFloorYClamp();
            }

            // [T2-2] Node separation steering (규칙 #17).
            //   - SpawnNodes 이후, BuildMSTAndLoops 이전에 실행.
            //   - Random 호출 없음 → seed 영향 없음.
            //   - OFF 시 완전히 건너뜀 → 원본과 bit-identical.
            if (enableT2NodeSeparation && rawPositions.Count >= 2)
            {
                ApplySeparationSteering();
            }

            if (rawPositions.Count < 2)
            {
                Debug.LogWarning("[CaveNodeGraphBuilder] 생성된 노드가 너무 적습니다. 바운더리를 넓히거나 반경을 줄이세요.");
                return;
            }

            // 3. 거리 기반 크루스칼(Kruskal) 최소 신장 트리(MST) 알고리즘 + 루프 복구
            BuildMSTAndLoops();

            // [T2-1] Loop edge 중 기존 edge와 너무 가까운 것 제거 (규칙 #17).
            //   - BuildMSTAndLoops 직후, AssignRoles 이전에 실행.
            //   - Random 호출 없음 → seed 영향 없음.
            //   - OFF 시 건너뜀.
            //   - MST edge는 절대 건드리지 않음 → connectivity 보장.
            if (enableT2EdgeValidation)
            {
                ValidateEdgeSeparation();
            }

            // 4. 위상 수학적 역할 부여 (스폰, 보스, 보물) 및 크리티컬 패스 도출
            AssignRolesAndPathfinding();

            // 5. GPU용 구조체(Struct)로 데이터 패킹
            PackDataForGPU();

            Debug.Log($"<color=green>[GraphBuilder]</color> 던전 설계 완료! (Seed: {seed}, 방: {nodesData.Count}개, 통로: {edgesData.Count}개)");
        }

        #region 1. Node Spawning
        private void SpawnNodes()
        {
            int maxAttempts = 30; // 무한 루프 방지용 Limit

            // [수정됨] 방들이 Y축으로 날뛰지 않도록 Y 좌표를 0 부근으로 안정화
            Vector3 spawnPos = new Vector3(
                Random.Range(-dungeonBounds.x * 0.1f, dungeonBounds.x * 0.1f),
                Random.Range(-5.0f, 5.0f), // Y축 안정화
                Random.Range(-dungeonBounds.z * 0.1f, dungeonBounds.z * 0.1f)
            );
            rawPositions.Add(spawnPos);

            for (int i = 1; i < targetRoomCount; i++)
            {
                bool placed = false;
                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    // [수정됨] 2.5D 던전 특성에 맞춰 Y축 변동 폭을 극단적으로 줄임
                    Vector3 candidatePos = new Vector3(
                        Random.Range(-dungeonBounds.x * 0.5f, dungeonBounds.x * 0.5f),
                        Random.Range(-dungeonBounds.y * 0.1f, dungeonBounds.y * 0.1f), // Y축 안정화
                        Random.Range(-dungeonBounds.z * 0.5f, dungeonBounds.z * 0.5f)
                    );

                    // 겹침 검사 (최소 반경의 2배만큼 떨어져 있어야 함)
                    bool overlap = false;
                    foreach (var pos in rawPositions)
                    {
                        if (Vector3.Distance(pos, candidatePos) < minRoomRadius * 2.5f)
                        {
                            overlap = true;
                            break;
                        }
                    }

                    if (!overlap)
                    {
                        rawPositions.Add(candidatePos);
                        placed = true;
                        break;
                    }
                }
                if (!placed) break; // 공간이 가득 차면 조기 종료
            }

            // 초기 반경 셋팅
            for (int i = 0; i < rawPositions.Count; i++)
            {
                roomRadii[i] = Random.Range(minRoomRadius, maxRoomRadius);
                roomTypes[i] = 0; // 일반 방
            }
        }
        #endregion

        #region 1.4 Phase 1.5 - Floor Y-Clamp (옵션 A, 규칙 #17 보완)
        /// <summary>
        /// [Phase 1.5 / 옵션 A] Node Y 좌표를 FloorAltitude + margin 이상으로 강제.
        /// 
        /// 목적:
        ///   FragB 원인 3 (floor-tunnel smax coupling) 차단.
        ///   통로 Y가 FloorAltitude에 가까우면 smax(tunnel, floor, k)가 corner 생성.
        ///   여기서 Y 하한을 보장하면 해당 corner 조건 자체가 발생하지 않음.
        /// 
        /// 규칙 준수:
        ///   - Random 호출 없음 → seed-deterministic
        ///   - rawPositions의 Y 성분만 수정, X/Z 불변
        ///   - dungeonBounds 상한도 함께 유지 (기존 SpawnNodes의 Y 범위 존중)
        /// 
        /// 경고:
        ///   - 본 clamp는 <strong>이미 Random으로 생성된 rawPositions를 후처리</strong>.
        ///     즉 Random 호출 시퀀스 및 Y 외 좌표는 원본과 동일.
        ///   - minSpawn 값은 floorAltitudeReference + tunnelYMinHeightAboveFloor.
        ///   - 예: floorAltitude=-5, margin=6 → Y 하한 = +1.
        /// </summary>
        private void ApplyFloorYClamp()
        {
            float minY = floorAltitudeReference + tunnelYMinHeightAboveFloor;

            // SpawnNodes의 Y 상한과 일관되게 유지
            float maxY = dungeonBounds.y * 0.1f;

            // minY > maxY면 기획 설정 오류 → 경고만 하고 건너뜀
            if (minY >= maxY)
            {
                Debug.LogWarning(
                    $"<color=orange>[Floor Y-Clamp]</color> 설정 비정상: " +
                    $"minY={minY:F2} >= maxY={maxY:F2}. " +
                    $"floorAltitudeReference={floorAltitudeReference}, " +
                    $"tunnelYMinHeightAboveFloor={tunnelYMinHeightAboveFloor}, " +
                    $"dungeonBounds.y={dungeonBounds.y}. clamp 건너뜀."
                );
                return;
            }

            int clampedCount = 0;
            float totalLift = 0f;

            for (int i = 0; i < rawPositions.Count; i++)
            {
                Vector3 p = rawPositions[i];
                if (p.y < minY)
                {
                    totalLift += (minY - p.y);
                    p.y = minY;
                    rawPositions[i] = p;
                    clampedCount++;
                }
            }

            if (clampedCount > 0)
            {
                Debug.Log(
                    $"<color=cyan>[Phase 1.5 / Y-Clamp]</color> " +
                    $"{clampedCount}/{rawPositions.Count} nodes clamped to Y≥{minY:F2}m " +
                    $"(avg lift={(totalLift / clampedCount):F2}m)"
                );
            }
            else
            {
                Debug.Log(
                    $"<color=cyan>[Phase 1.5 / Y-Clamp]</color> All nodes already above Y={minY:F2}m (no-op)."
                );
            }
        }
        #endregion

        #region 1.5 Tier 2 - Node Separation Steering (T2-2, 규칙 #17)
        /// <summary>
        /// [T2-2] Flocking separation steering으로 node 위치를 조정.
        /// Random 호출 없음 → seed-deterministic 보장.
        /// 수렴 조건: 어떤 node도 이동하지 않음 또는 maxIterations 도달.
        /// </summary>
        private void ApplySeparationSteering()
        {
            int n = rawPositions.Count;

            // 이상 간격: capsule 두 개 + 2×voxelSize 안전 margin (규칙 #17).
            // sidePathWidth은 capsule 지름이므로 반지름 = sidePathWidth/2.
            // 두 방 간에 통로가 지나가려면 최소 통로 두께 만큼은 비어야 함.
            float targetGap = sidePathWidth + 2f * edgeSeparationVoxelSize;

            const float pushStep = 0.5f; // 단일 iter 최대 이동 제한
            int totalMoves = 0;

            for (int iter = 0; iter < nodeSeparationMaxIterations; iter++)
            {
                Vector3[] deltas = new Vector3[n];
                bool anyMoved = false;

                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        Vector3 diff = rawPositions[j] - rawPositions[i];
                        float d = diff.magnitude;

                        // 두 node가 포함할 radius + targetGap 만큼 떨어져 있어야 이상적
                        float ideal = roomRadii[i] + roomRadii[j] + targetGap;

                        if (d > 0.001f && d < ideal)
                        {
                            Vector3 dir = diff / d; // normalized
                            float push = (ideal - d) * 0.5f;
                            push = Mathf.Min(push, pushStep);
                            deltas[i] -= dir * push;
                            deltas[j] += dir * push;
                            anyMoved = true;
                        }
                    }
                }

                if (!anyMoved) break; // 수렴

                // 이동 적용 - Y축 약화 + bounds clamp
                // [Phase 1.5 연동] Y-Clamp가 ON이면 Y 하한도 함께 유지.
                //                  OFF 시 원본 코드와 동일 (dungeonBounds만 적용).
                float yMin = enableFloorYClamp
                    ? (floorAltitudeReference + tunnelYMinHeightAboveFloor)
                    : (-dungeonBounds.y * 0.1f);
                float yMax = dungeonBounds.y * 0.1f;
                // yMin > yMax 방지 (Y-Clamp 설정 오류 시 fallback)
                if (yMin >= yMax) yMin = -dungeonBounds.y * 0.1f;

                for (int i = 0; i < n; i++)
                {
                    Vector3 d = deltas[i];
                    d.y *= nodeSeparationYDamping; // 2.5D 원칙 준수

                    Vector3 next = rawPositions[i] + d;

                    // dungeonBounds 이탈 방지 (SpawnNodes와 동일한 범위)
                    next.x = Mathf.Clamp(next.x, -dungeonBounds.x * 0.5f, dungeonBounds.x * 0.5f);
                    next.y = Mathf.Clamp(next.y, yMin, yMax);
                    next.z = Mathf.Clamp(next.z, -dungeonBounds.z * 0.5f, dungeonBounds.z * 0.5f);

                    rawPositions[i] = next;

                    if (d.sqrMagnitude > 0.0001f) totalMoves++;
                }
            }

            Debug.Log($"<color=cyan>[T2-2]</color> Separation steering: {totalMoves} node-moves (targetGap={targetGap:F2}m)");
        }
        #endregion

        #region 2. Graph Building (Kruskal's MST)
        private void BuildMSTAndLoops()
        {
            int n = rawPositions.Count;
            List<EdgeTemp> possibleEdges = new List<EdgeTemp>();

            // 모든 가능한 연결선(완전 그래프)의 거리 계산 (노드 수가 적어 O(N^2)도 매우 빠름)
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    possibleEdges.Add(new EdgeTemp
                    {
                        from = i,
                        to = j,
                        dist = Vector3.Distance(rawPositions[i], rawPositions[j])
                    });
                }
            }

            // 짧은 거리 순 정렬
            possibleEdges.Sort((a, b) => a.dist.CompareTo(b.dist));

            // Union-Find (Disjoint Set) 초기화
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int Find(int i)
            {
                if (parent[i] == i) return i;
                return parent[i] = Find(parent[i]);
            }

            void Union(int i, int j)
            {
                int rootI = Find(i);
                int rootJ = Find(j);
                if (rootI != rootJ) parent[rootI] = rootJ;
            }

            // MST 구성 및 버려지는 엣지 수집
            List<EdgeTemp> unusedEdges = new List<EdgeTemp>();

            foreach (var edge in possibleEdges)
            {
                if (Find(edge.from) != Find(edge.to))
                {
                    Union(edge.from, edge.to);
                    allEdges.Add(new Vector2Int(edge.from, edge.to));

                    // [T2-1 지원] MST 확정 edge 별도 기록 (loop edge 구분용).
                    // 본 기록은 토글 OFF여도 항상 수행 (오버헤드 무시 가능, 메모리 <1KB).
                    // OFF 상태에서 ValidateEdgeSeparation이 호출되지 않으므로 무해.
                    mstConfirmedEdges.Add(EdgeKey(edge.from, edge.to));
                }
                else
                {
                    unusedEdges.Add(edge);
                }
            }

            // 순환 루프(Cycle) 추가: 막다른 길 완화
            foreach (var edge in unusedEdges)
            {
                if (Random.value < loopProbability)
                {
                    allEdges.Add(new Vector2Int(edge.from, edge.to));
                    // mstConfirmedEdges에는 추가하지 않음 - 이게 loop edge 식별의 핵심.
                }
            }
        }

        /// <summary>
        /// 양방향 키 정규화 (min, max 순). allEdges와 criticalPathEdges가 
        /// 동일 방식(L266)으로 저장하므로 일관성 유지.
        /// </summary>
        private static Vector2Int EdgeKey(int a, int b)
        {
            return new Vector2Int(Mathf.Min(a, b), Mathf.Max(a, b));
        }

        private struct EdgeTemp { public int from, to; public float dist; }
        #endregion

        #region 2.5 Tier 2 - Edge Separation Validation (T2-1, 규칙 #17)
        /// <summary>
        /// [T2-1] MST가 완성된 후, loop edge 중 기존 edge와 Nyquist 거리 이하로 
        /// 가까운 것을 제거. MST edge는 건드리지 않음 → connectivity 유지.
        /// 
        /// Random 호출 없음. 결정론적.
        /// 
        /// 제거 대상:
        ///   loop edge (mstConfirmedEdges에 없는 edge) 중 
        ///   다른 edge와의 capsule 간격이 (wA + wB + 2×voxelSize) 미만인 것.
        /// 
        /// MST-MST 쌍이 가까운 경우는 여기서 처리하지 않음 (T2-2 node separation에서 해결).
        /// </summary>
        private void ValidateEdgeSeparation()
        {
            float safetyMargin = 2f * edgeSeparationVoxelSize;

            // criticalPathEdges는 AssignRolesAndPathfinding에서 채워지므로 이 시점엔 빈 상태.
            // 따라서 width 판정 시 "일단 sidePathWidth 기준"으로 검사 (보수적).
            // 이후 PackDataForGPU에서 critical은 mainPathWidth로 승격되므로 
            // 더 여유 있는 편으로 보정됨.
            float defaultEdgeWidth = sidePathWidth * 0.5f; // capsule 반지름

            int removedCount = 0;
            int iterations = 0;
            const int maxIters = 10; // 무한 루프 방지

            while (iterations < maxIters)
            {
                iterations++;
                int removedThisIter = 0;

                // 인덱스 기반 순회 (역순 삭제 안전)
                for (int i = allEdges.Count - 1; i >= 0; i--)
                {
                    Vector2Int eI = EdgeKey(allEdges[i].x, allEdges[i].y);

                    // MST edge는 절대 건드리지 않음
                    if (mstConfirmedEdges.Contains(eI)) continue;

                    Vector3 A0 = rawPositions[eI.x];
                    Vector3 A1 = rawPositions[eI.y];
                    float wA = defaultEdgeWidth;

                    bool removeThis = false;

                    for (int j = 0; j < allEdges.Count; j++)
                    {
                        if (i == j) continue;

                        Vector2Int eJ = EdgeKey(allEdges[j].x, allEdges[j].y);

                        // 공유 endpoint(합류점)은 "가까움"을 체크하면 안 됨 - 당연히 거리가 0
                        if (eI.x == eJ.x || eI.x == eJ.y || eI.y == eJ.x || eI.y == eJ.y)
                            continue;

                        Vector3 B0 = rawPositions[eJ.x];
                        Vector3 B1 = rawPositions[eJ.y];
                        float wB = defaultEdgeWidth;

                        float d = SegmentSegmentDistance(A0, A1, B0, B1);
                        float required = wA + wB + safetyMargin;

                        if (d < required)
                        {
                            removeThis = true;
                            break;
                        }
                    }

                    if (removeThis)
                    {
                        // T2-3 시각화용 기록
                        t2RemovedLoopEdges.Add((A0, A1));
                        allEdges.RemoveAt(i);
                        removedThisIter++;
                        removedCount++;
                    }
                }

                // 수렴: 이번 iter에 제거 없음
                if (removedThisIter == 0) break;
            }

            Debug.Log($"<color=cyan>[T2-1]</color> Edge validation: {removedCount} loop edges removed (iters={iterations}, margin={safetyMargin:F2}m)");
        }

        /// <summary>
        /// 두 3D 선분의 최단거리 계산. 선분-선분 closest point algorithm.
        /// 참고: Real-Time Collision Detection (Christer Ericson), Ch. 5.
        /// </summary>
        private static float SegmentSegmentDistance(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
        {
            Vector3 u = p2 - p1;
            Vector3 v = p4 - p3;
            Vector3 w = p1 - p3;
            float a = Vector3.Dot(u, u);
            float b = Vector3.Dot(u, v);
            float c = Vector3.Dot(v, v);
            float d = Vector3.Dot(u, w);
            float e = Vector3.Dot(v, w);
            float D = a * c - b * b;
            float sc, tc;

            // 평행 또는 매우 짧은 선분
            if (D < 1e-7f)
            {
                sc = 0f;
                tc = (b > c ? d / b : e / c);
            }
            else
            {
                sc = (b * e - c * d) / D;
                tc = (a * e - b * d) / D;
            }

            sc = Mathf.Clamp01(sc);
            tc = Mathf.Clamp01(tc);

            Vector3 dP = w + (sc * u) - (tc * v);
            return dP.magnitude;
        }
        #endregion

        #region 3. Role Assignment & Pathfinding
        private void AssignRolesAndPathfinding()
        {
            int n = rawPositions.Count;
            Dictionary<int, List<int>> adjList = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++) adjList[i] = new List<int>();

            foreach (var edge in allEdges)
            {
                adjList[edge.x].Add(edge.y);
                adjList[edge.y].Add(edge.x);
            }

            // BFS를 통해 0번(스폰)에서 가장 먼 노드(보스) 찾기
            Queue<int> queue = new Queue<int>();
            int[] dist = new int[n];
            int[] prev = new int[n];
            for (int i = 0; i < n; i++) { dist[i] = -1; prev[i] = -1; }

            queue.Enqueue(0);
            dist[0] = 0;
            int farthestNode = 0;
            int maxDist = 0;

            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                if (dist[curr] > maxDist)
                {
                    maxDist = dist[curr];
                    farthestNode = curr;
                }

                foreach (int neighbor in adjList[curr])
                {
                    if (dist[neighbor] == -1)
                    {
                        dist[neighbor] = dist[curr] + 1;
                        prev[neighbor] = curr;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // 역할 부여
            roomTypes[0] = 1; // 1: 스폰 방
            roomRadii[0] = maxRoomRadius * 1.2f;

            roomTypes[farthestNode] = 2; // 2: 보스 방
            roomRadii[farthestNode] = maxRoomRadius * bossRoomRadiusMultiplier;

            // 크리티컬 패스 역추적 (보스 -> 스폰)
            int step = farthestNode;
            while (step != 0 && prev[step] != -1)
            {
                int parent = prev[step];
                // 양방향 모두 검색될 수 있도록 저장 포맷을 일정하게 유지
                criticalPathEdges.Add(new Vector2Int(Mathf.Min(step, parent), Mathf.Max(step, parent)));
                step = parent;
            }

            // 연결된 선이 1개뿐인 잎(Leaf) 노드를 찾아 보물 방으로 지정
            for (int i = 0; i < n; i++)
            {
                if (i != 0 && i != farthestNode && adjList[i].Count == 1)
                {
                    roomTypes[i] = 3; // 3: 보물 방
                    roomRadii[i] = minRoomRadius * 1.5f;
                }
            }
        }
        #endregion

        #region 4. Data Packing for GPU
        private void PackDataForGPU()
        {
            // NodeData 패킹 (16바이트 정렬에 맞춘 C# 구조체 활용)
            for (int i = 0; i < rawPositions.Count; i++)
            {
                nodesData.Add(new NodeData
                {
                    position = rawPositions[i],
                    radius = roomRadii[i],
                    roomType = roomTypes[i],
                    padding = Vector3.zero
                });
            }

            // EdgeData 패킹
            foreach (var edge in allEdges)
            {
                // 작은 인덱스를 앞으로 하여 일관된 키 생성
                Vector2Int edgeKey = new Vector2Int(Mathf.Min(edge.x, edge.y), Mathf.Max(edge.x, edge.y));
                bool isCritical = criticalPathEdges.Contains(edgeKey);

                edgesData.Add(new EdgeData
                {
                    startPos = rawPositions[edge.x],
                    endPos = rawPositions[edge.y],
                    width = isCritical ? mainPathWidth : sidePathWidth, // 메인 경로는 넓게, 나머지는 좁게
                    padding = 0f
                });
            }
        }
        #endregion

        #region 5. Editor Gizmo Visualization
        private void OnDrawGizmos()
        {
            // 맵의 전체 바운더리를 그립니다.
            Gizmos.color = new Color(1, 1, 1, 0.1f);
            Gizmos.DrawWireCube(transform.position, dungeonBounds);

            if (rawPositions == null || rawPositions.Count == 0) return;

            // 1. 엣지 (길) 그리기
            foreach (var edge in allEdges)
            {
                Vector3 start = rawPositions[edge.x];
                Vector3 end = rawPositions[edge.y];

                Vector2Int edgeKey = new Vector2Int(Mathf.Min(edge.x, edge.y), Mathf.Max(edge.x, edge.y));

                if (criticalPathEdges.Contains(edgeKey))
                {
                    // 메인 동선 (크리티컬 패스) - 굵고 선명한 보라색
                    Gizmos.color = Color.magenta;
                    DrawThickLine(start, end, 3f);
                }
                else
                {
                    // 곁가지 길 - 얇은 파란색
                    Gizmos.color = new Color(0.2f, 0.6f, 1.0f, 0.5f);
                    Gizmos.DrawLine(start, end);
                }
            }

            // [T2-3] 제거된 loop edges 시각화 (빨강 dotted)
            if (enableT2DebugGizmos && t2RemovedLoopEdges != null && t2RemovedLoopEdges.Count > 0)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.7f);
                foreach (var (a, b) in t2RemovedLoopEdges)
                {
                    // 단속선 효과 모방
                    Vector3 dir = b - a;
                    float len = dir.magnitude;
                    if (len < 0.01f) continue;
                    Vector3 u = dir / len;
                    const float dash = 0.7f;
                    for (float t = 0f; t < len; t += dash * 2f)
                    {
                        Vector3 s = a + u * t;
                        Vector3 e = a + u * Mathf.Min(t + dash, len);
                        Gizmos.DrawLine(s, e);
                    }
                }
            }

            // 2. 노드 (방) 그리기
            for (int i = 0; i < rawPositions.Count; i++)
            {
                Vector3 pos = rawPositions[i];
                float radius = roomRadii.ContainsKey(i) ? roomRadii[i] : minRoomRadius;
                int type = roomTypes.ContainsKey(i) ? roomTypes[i] : 0;

                switch (type)
                {
                    case 1: // Spawn (Green)
                        Gizmos.color = Color.green;
                        Gizmos.DrawSphere(pos, radius);
                        break;
                    case 2: // Boss (Red)
                        Gizmos.color = new Color(1, 0, 0, 0.8f);
                        Gizmos.DrawSphere(pos, radius);
                        break;
                    case 3: // Treasure (Yellow)
                        Gizmos.color = Color.yellow;
                        Gizmos.DrawWireSphere(pos, radius);
                        break;
                    default: // Normal (White)
                        Gizmos.color = new Color(1, 1, 1, 0.3f);
                        Gizmos.DrawWireSphere(pos, radius);
                        break;
                }
            }
        }

        // 에디터에서 굵은 선을 그리기 위한 헬퍼 함수
        private void DrawThickLine(Vector3 start, Vector3 end, float thickness)
        {
            Camera c = Camera.current;
            if (c == null) return;

            // 단순 선을 여러 겹 겹쳐 그려 굵기를 모방합니다. (씬 뷰 카메라 기준)
            Vector3 right = Vector3.Cross((end - start).normalized, c.transform.forward).normalized * (thickness * 0.1f);

            Gizmos.DrawLine(start - right, end - right);
            Gizmos.DrawLine(start, end);
            Gizmos.DrawLine(start + right, end + right);
        }
        #endregion
    }
}
