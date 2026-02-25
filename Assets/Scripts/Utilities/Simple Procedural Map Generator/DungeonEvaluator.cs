using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 단일 책임: 맵의 물리적 연결성뿐만 아니라, 게임 디자인적 공간(Pacing)과 노드 고유성까지 세분화하여 평가합니다.
/// </summary>
public class DungeonEvaluator
{
    private int width, height, depth;
    private VoxelData[,,] VoxelMap;
    private List<RoomNode> AllNodes;

    public DungeonEvaluator(int w, int h, int d, VoxelData[,,] map, List<RoomNode> nodes)
    {
        width = w; height = h; depth = d;
        VoxelMap = map;
        AllNodes = nodes;
    }

    public IEnumerator EvaluateMapRoutine(Action<int, string> onComplete)
    {
        int score = 0;
        string report = "=== 🗺️ 맵 퀄리티 정밀 분석 리포트 ===\n";

        // ----------------------------------------------------
        // 1. 보행 구역 및 단절 검사
        // ----------------------------------------------------
        Debug.Log("<color=grey>    -> [평가 1/5] 보행 구역(NavMesh) 섬(Island) 스캔 중...</color>");
        List<List<Vector3Int>> islands = null;
        yield return GetFloorIslandsRoutine(res => islands = res);

        int totalWalkable = 0;
        foreach (var isl in islands) totalWalkable += isl.Count;
        int mainIslandSize = islands.Count > 0 ? islands[0].Count : 0;
        float connectivityRatio = totalWalkable > 0 ? (float)mainIslandSize / totalWalkable : 0;

        report += $"[1] 보행 구역(NavMesh) 연결성\n  - 총 보행 타일: {totalWalkable}개\n  - 남은 독립 구역: {islands.Count}개\n";

        int connectivityScore = Mathf.RoundToInt(connectivityRatio * 30f);
        score += connectivityScore;

        if (islands.Count <= 1) { score += 20; report += $"✅ [연결성: {connectivityScore + 20}/50] 100% 완벽한 단일 네트워크!\n"; }
        else if (connectivityRatio >= 0.98f) { score += 10; report += $"⚠️ [연결성: {connectivityScore + 10}/50] 미세 파편 잔존 (98% 이상 연결됨).\n"; }
        else { int pen = 20 + (islands.Count * 5); score -= pen; report += $"❌ [연결성: 0/50] 치명적 단절 발생 (페널티 -{pen}점)\n"; }

        // ----------------------------------------------------
        // 2. 게임 디자인적 공간 비율 (Wide vs Narrow Pacing)
        // ----------------------------------------------------
        Debug.Log("<color=grey>    -> [평가 2/5] 전투 공간(Wide)과 이동 통로(Narrow) 비율 분석 중...</color>");
        int wideSpaceCount = 0;
        int narrowSpaceCount = 0;

        for (int x = 2; x < width - 2; x++)
        {
            for (int y = 2; y < height - 2; y++)
            {
                for (int z = 2; z < depth - 2; z++)
                {
                    if (VoxelMap[x, y, z].Type == VoxelType.Air)
                    {
                        int airNeighbors = 0;
                        for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++) for (int k = -1; k <= 1; k++)
                                    if (VoxelMap[x + i, y + j, z + k].Type == VoxelType.Air) airNeighbors++;

                        if (airNeighbors > 18) wideSpaceCount++; // 주변이 뻥 뚫린 방
                        else if (airNeighbors < 10) narrowSpaceCount++; // 좁은 통로
                    }
                }
            }
        }

        float wideRatio = (float)wideSpaceCount / Mathf.Max(1, (wideSpaceCount + narrowSpaceCount));
        report += $"\n[2] 게임성: 공간 페이싱 (Pacing)\n  - 전투방(Wide) 비율: {wideRatio:P1} (권장 40~70%)\n";

        if (wideRatio >= 0.4f && wideRatio <= 0.7f) { score += 20; report += $"✅ [공간성: 20/20] 방과 통로의 완급 조절이 아주 훌륭합니다.\n"; }
        else { score += 5; report += $"⚠️ [공간성: 5/20] 통로가 너무 길거나, 방이 너무 커서 밋밋합니다.\n"; }

        // ----------------------------------------------------
        // 3. 노드 고유성 및 완결성 검사
        // ----------------------------------------------------
        Debug.Log("<color=grey>    -> [평가 3/5] 노드(방) 고유 공간 완결성 검증 중...</color>");
        int intactNodes = 0;
        foreach (var node in AllNodes)
        {
            if (VoxelMap[node.position.x, node.position.y, node.position.z].Type == VoxelType.Air)
                intactNodes++;
        }

        float nodeHealth = (float)intactNodes / AllNodes.Count;
        report += $"\n[3] 노드 완결성 및 고유 공간 확보\n  - 노드 보존율: {nodeHealth:P1} ({intactNodes}/{AllNodes.Count}개 생존)\n";

        if (nodeHealth > 0.9f) { score += 15; report += $"✅ [노드: 15/15] 각 방이 침범받지 않고 고유 공간을 유지했습니다.\n"; }
        else { report += $"❌ [노드: 0/15] 터널이 방을 너무 많이 파괴했습니다.\n"; }

        // ----------------------------------------------------
        // 4. 노이즈 및 쓰레기 데이터 검사
        // ----------------------------------------------------
        Debug.Log("<color=grey>    -> [평가 4/5] 메쉬 무결성 노이즈 스캔 중...</color>");
        int floatingCubes = 0;
        int isolatedHoles = 0;

        for (int x = 2; x < width - 2; x++)
        {
            for (int y = 2; y < height - 2; y++)
            {
                for (int z = 2; z < depth - 2; z++)
                {
                    bool isSolid = VoxelMap[x, y, z].Type == VoxelType.Solid;
                    int neighbors = 0;
                    if (VoxelMap[x + 1, y, z].Type == VoxelType.Solid) neighbors++;
                    if (VoxelMap[x - 1, y, z].Type == VoxelType.Solid) neighbors++;
                    if (VoxelMap[x, y + 1, z].Type == VoxelType.Solid) neighbors++;
                    if (VoxelMap[x, y - 1, z].Type == VoxelType.Solid) neighbors++;
                    if (VoxelMap[x, y, z + 1].Type == VoxelType.Solid) neighbors++;
                    if (VoxelMap[x, y, z - 1].Type == VoxelType.Solid) neighbors++;

                    if (isSolid && neighbors <= 1) floatingCubes++;
                    if (!isSolid && neighbors >= 5) isolatedHoles++;
                }
            }
        }

        report += $"\n[4] 메쉬 무결성 (노이즈)\n  - 찌꺼기 큐브/구멍: {floatingCubes + isolatedHoles}개\n";
        if (floatingCubes + isolatedHoles <= 10) { score += 15; report += $"✅ [무결성: 15/15] 맵이 깔끔합니다.\n"; }
        else { score -= 20; report += $"⚠️ [무결성: -20점] 찌꺼기가 너무 많아 최적화가 필요합니다.\n"; }

        report += $"==================================\n";
        onComplete?.Invoke(score, report);
    }

    internal IEnumerator GetFloorIslandsRoutine(Action<List<List<Vector3Int>>> onComplete)
    {
        List<List<Vector3Int>> islands = new List<List<Vector3Int>>();
        bool[,,] visited = new bool[width, height, depth];
        List<Vector3Int> allFloors = new List<Vector3Int>();

        for (int x = 1; x < width - 1; x++)
        {
            for (int y = 1; y < height - 1; y++)
            {
                for (int z = 1; z < depth - 1; z++)
                {
                    if (VoxelMap[x, y, z].Type == VoxelType.Air && VoxelMap[x, y - 1, z].Type == VoxelType.Solid)
                        allFloors.Add(new Vector3Int(x, y, z));
                    else
                        visited[x, y, z] = true;
                }
            }
            if (x % 20 == 0) yield return null;
        }

        Vector3Int[] moveDirs = { Vector3Int.right, Vector3Int.left, Vector3Int.forward, Vector3Int.back };
        int loopCount = 0;

        foreach (var startNode in allFloors)
        {
            if (visited[startNode.x, startNode.y, startNode.z]) continue;

            List<Vector3Int> currentIsland = new List<Vector3Int>();
            Queue<Vector3Int> queue = new Queue<Vector3Int>();

            queue.Enqueue(startNode);
            visited[startNode.x, startNode.y, startNode.z] = true;
            currentIsland.Add(startNode);

            while (queue.Count > 0)
            {
                Vector3Int curr = queue.Dequeue();

                foreach (var dir in moveDirs)
                {
                    for (int yOffset = -1; yOffset <= 1; yOffset++)
                    {
                        Vector3Int n = curr + dir + new Vector3Int(0, yOffset, 0);
                        if (n.x > 0 && n.x < width - 1 && n.y > 0 && n.y < height - 1 && n.z > 0 && n.z < depth - 1)
                        {
                            if (!visited[n.x, n.y, n.z])
                            {
                                visited[n.x, n.y, n.z] = true;
                                queue.Enqueue(n);
                                currentIsland.Add(n);
                                break;
                            }
                        }
                    }
                }

                if (loopCount++ > 15000) { loopCount = 0; yield return null; }
            }
            islands.Add(currentIsland);
        }

        islands.Sort((a, b) => b.Count.CompareTo(a.Count));
        onComplete?.Invoke(islands);
    }
}