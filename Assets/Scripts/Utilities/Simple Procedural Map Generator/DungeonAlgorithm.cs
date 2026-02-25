using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// 던전 배열 생성, 절대 영토(Voronoi) 기반 청크 분할, 2.5D 하이트맵 터널링 전담 코어 알고리즘
/// </summary>
public class DungeonAlgorithm
{
    public VoxelData[,,] VoxelMap { get; private set; }
    public List<RoomNode> AllNodes { get; private set; }

    private int width, height, depth;
    private int totalRooms;

    public DungeonAlgorithm(int w, int h, int d, int rooms)
    {
        width = w; height = h; depth = d; totalRooms = rooms;
        VoxelMap = new VoxelData[width, height, depth];
        AllNodes = new List<RoomNode>();
    }

    public IEnumerator GenerateMapDataRoutine()
    {
        Debug.Log("<color=grey>    -> [1/6] 맵 공간 초기화 및 방(Node) 고유 영역 확보 중...</color>");
        InitializeVoxelMap();
        GenerateNodeGraph();
        yield return null;

        Debug.Log("<color=grey>    -> [2/6] 보로노이(Voronoi) 다이어그램 기반 바이옴 절대 영토 분할 중...</color>");
        yield return AssignBiomesVoronoiRoutine();

        Debug.Log("<color=grey>    -> [3/6] 2.5D 하이트맵 기반 완벽한 층계 터널 개통 중...</color>");
        yield return CarveRoomsAndTunnelsRoutine();

        Debug.Log("<color=grey>    -> [4/6] 씬 뷰 단면 렌더링을 위해 측면 외벽 껍질 제거 중...</color>");
        CutOutAntFarmBoundary();
        yield return null;

        Debug.Log("<color=grey>    -> [5/6] 3D 완전 매립(Flood Fill) 파편 청소 및 고립 구역 강제 연결 진행 중...</color>");
        yield return EnsureConnectivityAndCleanUpRoutine();

        Debug.Log("<color=grey>    -> [6/6] 데이터 레벨 노이즈(공중 큐브 및 구멍) 최종 박멸 중...</color>");
        yield return RemoveVoxelNoiseRoutine();
    }

    private void InitializeVoxelMap()
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                for (int z = 0; z < depth; z++)
                    VoxelMap[x, y, z] = new VoxelData
                    {
                        Type = VoxelType.Solid,
                        Biome = BiomeType.MineShafts // 기본값
                    };
    }

    /// <summary>
    /// 노드(방)들이 서로 너무 가깝지 않게 충분한 고유 공간(Pacing)을 확보하며 스폰시킵니다.
    /// </summary>
    private void GenerateNodeGraph()
    {
        FactionType[] factions = {
            FactionType.TheForgotten,
            FactionType.AnomalySwarm,
            FactionType.CultOfDepths
        };

        int attempts = 0;
        while (AllNodes.Count < totalRooms && attempts < 2000)
        {
            attempts++;
            Vector3Int pos = new Vector3Int(Random.Range(15, width - 15), Random.Range(5, height - 10), Random.Range(15, depth - 15));

            // 각 노드는 최소 16칸 이상의 독립된 반경을 보장받음
            bool tooClose = false;
            foreach (var existingNode in AllNodes)
            {
                if (Vector3Int.Distance(pos, existingNode.position) < 16f)
                {
                    tooClose = true; break;
                }
            }
            if (tooClose) continue;

            RoomNode node = new RoomNode { position = pos };

            // 4개의 바이옴을 균등하게 할당
            node.biome = (BiomeType)(AllNodes.Count % 4);

            if (node.biome == BiomeType.LiminalHalls) node.radius = Random.Range(4, 7);
            else if (node.biome == BiomeType.ConcreteBunker) node.radius = Random.Range(6, 10);
            else if (node.biome == BiomeType.MineShafts) node.radius = Random.Range(3, 6);
            else node.radius = Random.Range(8, 12);

            AllNodes.Add(node);
        }

        for (int i = 0; i < Mathf.Min(3, AllNodes.Count); i++)
        {
            AllNodes[i].isBase = true;
            AllNodes[i].faction = factions[i];
            AllNodes[i].radius = 9;
        }

        // MST (최소 신장 트리)를 사용하여 모든 노드를 한 붓 그리기로 무조건 연결되게 뼈대를 짭니다.
        List<RoomNode> connectedNetwork = new List<RoomNode>();
        List<RoomNode> unconnNodes = new List<RoomNode>(AllNodes);

        connectedNetwork.Add(unconnNodes[0]);
        unconnNodes.RemoveAt(0);

        while (unconnNodes.Count > 0)
        {
            RoomNode bestConnected = null;
            RoomNode bestUnconn = null;
            float shortestDist = float.MaxValue;

            foreach (var cNode in connectedNetwork)
            {
                foreach (var uNode in unconnNodes)
                {
                    float d = Vector3Int.Distance(cNode.position, uNode.position);
                    if (d < shortestDist)
                    {
                        shortestDist = d; bestConnected = cNode; bestUnconn = uNode;
                    }
                }
            }
            bestConnected.connectedRooms.Add(bestUnconn);
            bestUnconn.connectedRooms.Add(bestConnected);
            connectedNetwork.Add(bestUnconn);
            unconnNodes.Remove(bestUnconn);
        }
    }

    /// <summary>
    /// 💡 [혁신 기술 1] 보로노이 다이어그램(Voronoi Diagram)을 활용한 절대 영토 분할
    /// 맵의 모든 복셀이 자신과 가장 가까운 노드(방)의 바이옴을 상속받습니다. 청크 섞임을 100% 방지합니다.
    /// </summary>
    private IEnumerator AssignBiomesVoronoiRoutine()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int z = 0; z < depth; z++)
                {
                    float minDist = float.MaxValue;
                    BiomeType bestBiome = BiomeType.MineShafts;
                    Vector3Int p = new Vector3Int(x, y, z);

                    foreach (var node in AllNodes)
                    {
                        float dist = Vector3Int.Distance(p, node.position);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            bestBiome = node.biome;
                        }
                    }
                    VoxelMap[x, y, z].Biome = bestBiome;
                }
            }
            if (x % 10 == 0) yield return null;
        }
    }

    private IEnumerator CarveRoomsAndTunnelsRoutine()
    {
        int loopC = 0;
        foreach (var node in AllNodes)
        {
            // 방을 뚫을 때 바이옴은 이미 Voronoi로 세팅되어 있으므로 형태(Type)만 Air로 바꿉니다.
            if (node.biome == BiomeType.LiminalHalls || node.biome == BiomeType.ConcreteBunker)
                CarveBox(node.position, new Vector3Int(node.radius, node.radius / 2, node.radius), node);
            else if (node.biome == BiomeType.MineShafts)
                CarveFlatSphere(node.position, node.radius, node);
            else
                CarveSphere(node.position, node.radius, node);

            if (++loopC % 5 == 0) yield return null;
        }

        HashSet<string> drawnTunnels = new HashSet<string>();
        foreach (var node in AllNodes)
        {
            foreach (var neighbor in node.connectedRooms)
            {
                string id1 = $"{node.position}_{neighbor.position}";
                string id2 = $"{neighbor.position}_{node.position}";
                if (drawnTunnels.Contains(id1) || drawnTunnels.Contains(id2)) continue;

                yield return DrawTunnelRoutine(node, neighbor);
                drawnTunnels.Add(id1);
            }
        }
    }

    private IEnumerator DrawTunnelRoutine(RoomNode start, RoomNode end)
    {
        int radius = 2; // 방(Wide)과 터널(Narrow)의 게임성 대비 확보

        if (start.biome == BiomeType.LiminalHalls || start.biome == BiomeType.ConcreteBunker)
        {
            // 인공 구역은 직각(ㄱ자)으로 꺾어지게 터널링
            Vector3Int mid = new Vector3Int(start.position.x, start.position.y, end.position.z);
            yield return CarveWalkableTunnelRoutine(start.position, mid, radius);
            yield return CarveWalkableTunnelRoutine(mid, end.position, radius);
        }
        else
        {
            yield return CarveWalkableTunnelRoutine(start.position, end.position, radius);
        }
    }

    // ==========================================
    // 💡 [혁신 기술 2] 2.5D 하이트맵 프레스 터널링 (수직굴 막힘 및 단절 완벽 해결)
    // ==========================================
    private IEnumerator CarveWalkableTunnelRoutine(Vector3Int start, Vector3Int end, int radius)
    {
        Dictionary<Vector2Int, int> heightMap = new Dictionary<Vector2Int, int>();
        float dist = Vector3.Distance(start, end);
        int steps = Mathf.CeilToInt(dist * 3.0f); // 촘촘한 스텝

        // 1. 터널 경로가 지나갈 2D 평면(X,Z) 상의 최고점(Y)을 기록하는 하이트맵을 작성합니다.
        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 0 : (float)i / steps;
            Vector3 pos = Vector3.Lerp(start, end, t);

            // 자연스러운 수평 구불거림 (Y축은 훼손하지 않아 절벽이 생기지 않음)
            pos.x += Mathf.Sin(t * Mathf.PI * 2f) * 2f;
            pos.z += Mathf.Cos(t * Mathf.PI * 2f) * 2f;

            int pY = Mathf.RoundToInt(pos.y);

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (dx * dx + dz * dz <= radius * radius)
                    {
                        Vector2Int pos2D = new Vector2Int(Mathf.RoundToInt(pos.x) + dx, Mathf.RoundToInt(pos.z) + dz);

                        // 경로가 겹칠 경우 가장 높은 위치(Y)를 채택하여 완만한 오르막길(계단)을 형성합니다.
                        if (!heightMap.ContainsKey(pos2D))
                            heightMap[pos2D] = pY;
                        else
                            heightMap[pos2D] = Mathf.Max(heightMap[pos2D], pY);
                    }
                }
            }
        }

        // 2. 완성된 2D 하이트맵을 바탕으로 위에서 아래로 프레스로 찍어내듯 3D 공간을 파냅니다.
        // 머리 위를 막는 불상사(팀킬 버그)가 물리적으로 발생할 수 없는 완벽한 방식입니다.
        int count = 0;
        foreach (var kvp in heightMap)
        {
            int x = kvp.Key.x;
            int z = kvp.Key.y;
            int y = kvp.Value;

            // 바닥을 두께 3칸으로 튼튼하게 깔아 밑이 뚫리는 것 방지
            for (int dy = -3; dy <= -1; dy++)
            {
                if (IsValid(x, y + dy, z)) VoxelMap[x, y + dy, z].Type = VoxelType.Solid;
            }

            // 바닥 위쪽으로 머리 위 5칸을 시원하게 공기로 뚫어줌 (절대 막히지 않음)
            for (int dy = 0; dy <= 4; dy++)
            {
                if (IsValid(x, y + dy, z)) VoxelMap[x, y + dy, z].Type = VoxelType.Air;
            }

            if (++count % 100 == 0) yield return null;
        }
    }

    private void CarveBox(Vector3Int center, Vector3Int extents, RoomNode meta)
    {
        for (int x = -extents.x; x <= extents.x; x++)
            for (int y = -extents.y; y <= extents.y; y++)
                for (int z = -extents.z; z <= extents.z; z++)
                    ApplyVoxelData(center + new Vector3Int(x, y, z), meta);
    }

    private void CarveSphere(Vector3Int center, int radius, RoomNode meta)
    {
        for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
                for (int z = -radius; z <= radius; z++)
                    if (x * x + y * y + z * z <= radius * radius)
                        ApplyVoxelData(center + new Vector3Int(x, y, z), meta);
    }

    private void CarveFlatSphere(Vector3Int center, int radius, RoomNode meta)
    {
        for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
                for (int z = -radius; z <= radius; z++)
                    if (x * x + (y * y * 2.5f) + z * z <= radius * radius) // Y축을 납작하게 찌그러뜨림
                        ApplyVoxelData(center + new Vector3Int(x, y, z), meta);
    }

    private void ApplyVoxelData(Vector3Int pos, RoomNode meta)
    {
        if (IsValid(pos.x, pos.y, pos.z))
        {
            VoxelMap[pos.x, pos.y, pos.z].Type = VoxelType.Air;
            // 바이옴은 Voronoi로 유지하되, 거점 데이터만 기록
            if (meta.isBase) VoxelMap[pos.x, pos.y, pos.z].IsBase = true;
            if (meta.isBoss) VoxelMap[pos.x, pos.y, pos.z].IsBossRoom = true;
        }
    }

    private void CutOutAntFarmBoundary()
    {
        // 씬 뷰 시야 확보를 위해 껍질 제거. (단, 바닥 y=0은 NavMesh 계산을 위해 절대 지우지 않음)
        for (int x = 0; x < width; x++)
            for (int y = 1; y < height; y++)
                for (int z = 0; z < depth; z++)
                    if (x == 0 || x == width - 1 || z == 0 || z == depth - 1)
                        VoxelMap[x, y, z].Type = VoxelType.Air;
    }

    private IEnumerator EnsureConnectivityAndCleanUpRoutine()
    {
        DungeonEvaluator evaluator = new DungeonEvaluator(width, height, depth, VoxelMap, AllNodes);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var getIslandsMethod = typeof(DungeonEvaluator).GetMethod("GetFloorIslandsRoutine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            List<List<Vector3Int>> islands = null;
            yield return (IEnumerator)getIslandsMethod.Invoke(evaluator, new object[] { (Action<List<List<Vector3Int>>>)((res) => islands = res) });

            if (islands == null || islands.Count <= 1) break;

            List<Vector3Int> mainIsland = islands[0];
            Debug.Log($"<color=grey>      - 고립 구역 {islands.Count - 1}개 매립 청소 및 연결 중...</color>");

            for (int i = 1; i < islands.Count; i++)
            {
                bool isProtected = false;
                foreach (var pos in islands[i])
                {
                    if (VoxelMap[pos.x, pos.y, pos.z].IsBase || VoxelMap[pos.x, pos.y, pos.z].IsBossRoom)
                    {
                        isProtected = true; break;
                    }
                }

                // 30칸 이상의 의미 있는 구역만 살리고 나머지는 영구 매립
                if (islands[i].Count > 30 || isProtected)
                {
                    yield return ConnectIslandsRoutine(mainIsland, islands[i]);
                }
                else
                {
                    if (islands[i].Count > 0)
                        yield return FillAirVolumeWithSolidRoutine(islands[i][0]);
                }
                if (i % 5 == 0) yield return null;
            }
        }
    }

    public IEnumerator RepairMapRoutine()
    {
        yield return EnsureConnectivityAndCleanUpRoutine();
        yield return RemoveVoxelNoiseRoutine();
    }

    private IEnumerator FillAirVolumeWithSolidRoutine(Vector3Int startPos)
    {
        Queue<Vector3Int> queue = new Queue<Vector3Int>();
        queue.Enqueue(startPos);
        int fillCount = 0;
        Vector3Int[] dirs = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right, Vector3Int.forward, Vector3Int.back };

        while (queue.Count > 0 && fillCount < 3000)
        {
            Vector3Int curr = queue.Dequeue();
            if (VoxelMap[curr.x, curr.y, curr.z].Type != VoxelType.Air) continue;
            if (VoxelMap[curr.x, curr.y, curr.z].IsBase || VoxelMap[curr.x, curr.y, curr.z].IsBossRoom) continue;

            VoxelMap[curr.x, curr.y, curr.z].Type = VoxelType.Solid;
            fillCount++;

            foreach (var d in dirs)
            {
                Vector3Int n = curr + d;
                if (IsValid(n.x, n.y, n.z) && VoxelMap[n.x, n.y, n.z].Type == VoxelType.Air)
                    queue.Enqueue(n);
            }
            if (fillCount % 500 == 0) yield return null;
        }
    }

    private IEnumerator ConnectIslandsRoutine(List<Vector3Int> mainList, List<Vector3Int> targetList)
    {
        Vector3Int bestMain = default;
        Vector3Int bestTarget = default;
        float minDist = float.MaxValue;

        for (int i = 0; i < 300; i++)
        {
            Vector3Int m = mainList[Random.Range(0, mainList.Count)];
            Vector3Int t = targetList[Random.Range(0, targetList.Count)];
            float d = Vector3.Distance(m, t);
            if (d < minDist) { minDist = d; bestMain = m; bestTarget = t; }
        }

        yield return CarveWalkableTunnelRoutine(bestMain, bestTarget, 4); // 굵게 뚫어 연결
    }

    private IEnumerator RemoveVoxelNoiseRoutine()
    {
        for (int pass = 0; pass < 2; pass++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                for (int y = 1; y < height - 1; y++)
                {
                    for (int z = 1; z < depth - 1; z++)
                    {
                        bool isSolid = VoxelMap[x, y, z].Type == VoxelType.Solid;
                        int solidNeighbors = 0;
                        if (VoxelMap[x + 1, y, z].Type == VoxelType.Solid) solidNeighbors++;
                        if (VoxelMap[x - 1, y, z].Type == VoxelType.Solid) solidNeighbors++;
                        if (VoxelMap[x, y + 1, z].Type == VoxelType.Solid) solidNeighbors++;
                        if (VoxelMap[x, y - 1, z].Type == VoxelType.Solid) solidNeighbors++;
                        if (VoxelMap[x, y, z + 1].Type == VoxelType.Solid) solidNeighbors++;
                        if (VoxelMap[x, y, z - 1].Type == VoxelType.Solid) solidNeighbors++;

                        if (isSolid && solidNeighbors <= 1) VoxelMap[x, y, z].Type = VoxelType.Air;
                        if (!isSolid && solidNeighbors >= 5) VoxelMap[x, y, z].Type = VoxelType.Solid;
                    }
                }
                if (x % 20 == 0) yield return null;
            }
        }
    }

    private bool IsValid(int x, int y, int z)
    {
        return x > 0 && x < width - 1 && y > 0 && y < height - 1 && z > 0 && z < depth - 1;
    }
}