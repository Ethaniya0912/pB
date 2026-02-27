using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace CaveSystem
{
    /// <summary>
    /// [Phase 2] 기획자의 의도가 반영된 수학적 3D 그래프(방과 통로)를 메인 스레드에서 생성하는 핵심 두뇌 클래스입니다.
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

        // 생성된 최종 데이터
        [HideInInspector] public List<NodeData> nodesData = new List<NodeData>();
        [HideInInspector] public List<EdgeData> edgesData = new List<EdgeData>();

        // 디버깅/기즈모 전용 데이터 캐싱
        private List<Vector3> rawPositions = new List<Vector3>();
        private Dictionary<int, int> roomTypes = new Dictionary<int, int>();
        private Dictionary<int, float> roomRadii = new Dictionary<int, float>();
        private List<Vector2Int> allEdges = new List<Vector2Int>();
        private HashSet<Vector2Int> criticalPathEdges = new HashSet<Vector2Int>();

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

            // 2. Poisson Disk 형태의 거리 기반 배척 노드 스폰 (방 흩뿌리기)
            SpawnNodes();

            if (rawPositions.Count < 2)
            {
                Debug.LogWarning("[CaveNodeGraphBuilder] 생성된 노드가 너무 적습니다. 바운더리를 넓히거나 반경을 줄이세요.");
                return;
            }

            // 3. 거리 기반 크루스칼(Kruskal) 최소 신장 트리(MST) 알고리즘 + 루프 복구
            BuildMSTAndLoops();

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
                }
            }
        }

        private struct EdgeTemp { public int from, to; public float dist; }
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