using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;
using Random = UnityEngine.Random;

/// <summary>
/// 퀄리티 컨트롤 기반 맵 생성기 
/// (점수 미달 시 맵을 파기하지 않고, 끊어진 부분을 강제로 잇는 '수리(Repair)' 시스템 도입)
/// </summary>
[RequireComponent(typeof(NavMeshSurface))]
public class SimpleMapGenerator : MonoBehaviour
{
    [Header("3D Node-Based Map Settings")]
    public int width = 100;
    public int height = 35;
    public int depth = 100;
    public int totalRooms = 30;

    [Header("Quality Control")]
    public int targetScore = 80;
    public int maxRepairRetries = 10;

    [Header("Materials (Low Saturation)")]
    public Material liminalMaterial;
    public Material mineMaterial;
    public Material bunkerMaterial;
    public Material abyssMaterial;
    public Material woodMaterial;
    public Material bossCrystalMaterial;

    public Action<List<Vector3>> OnMapGenerated;
    public List<Vector3> FloorPositions { get; private set; } = new List<Vector3>();

    private NavMeshSurface navMeshSurface;
    private Transform chunkContainer;
    private DungeonAlgorithm algorithm;
    private DungeonRenderer rendererModule;

    void Start()
    {
        navMeshSurface = GetComponent<NavMeshSurface>();
        GenerateConnectedMap();
    }

    public void GenerateConnectedMap()
    {
        StartCoroutine(GenerateAndBakeRoutine());
    }

    private IEnumerator GenerateAndBakeRoutine()
    {
        Debug.Log("<color=yellow>=======================================</color>");
        Debug.Log("<color=yellow>[MapGenerator] 퀄리티 컨트롤 및 수리 기반 맵 프로세스 시작...</color>");

        ClearMap();
        yield return null;

        // 1. 기초 맵 데이터 단 한 번만 생성
        Debug.Log($"<color=cyan>▶ [맵 생성] 기초 3D 배열 데이터를 깎아냅니다...</color>");
        algorithm = new DungeonAlgorithm(width, height, depth, totalRooms);
        yield return StartCoroutine(algorithm.GenerateMapDataRoutine());

        int currentScore = 0;
        int repairAttempts = 0;
        string finalReport = "";

        // 2. 💡 생성 후 평가 -> 부족하면 "수리(Repair)" 루프
        while (currentScore < targetScore && repairAttempts < maxRepairRetries)
        {
            repairAttempts++;

            Debug.Log($"<color=cyan>▶ [평가 및 수리] {repairAttempts}회차 맵 퀄리티 평가 중...</color>");
            DungeonEvaluator evaluator = new DungeonEvaluator(width, height, depth, algorithm.VoxelMap, algorithm.AllNodes);
            yield return StartCoroutine(evaluator.EvaluateMapRoutine((score, report) => {
                currentScore = score;
                finalReport = report;
            }));

            if (currentScore < targetScore)
            {
                Debug.LogWarning($"<color=orange>[MapGenerator] ⚠️ 평가 미달 ({currentScore}점). 맵을 버리지 않고 부족한 부분을 수리(Repair)합니다!</color>\n{finalReport}");

                // 단절된 구역을 파악하여 터널로 억지로 이어버리는 수리 알고리즘 실행
                yield return StartCoroutine(algorithm.RepairMapRoutine());
            }
        }

        if (currentScore >= targetScore)
            Debug.Log($"<color=green>[MapGenerator] 🎯 {repairAttempts}회차 수리 끝에 최종 합격! ({currentScore}점)</color>\n{finalReport}");
        else
            Debug.LogError($"<color=red>[MapGenerator] ☠️ {maxRepairRetries}회 수리 실패. 최선을 다한 맵(점수: {currentScore})을 강제 렌더링합니다.</color>\n{finalReport}");

        // 3. 렌더링 및 베이킹
        Debug.Log("<color=cyan>▶ 3D 메쉬 렌더링 및 유기적 스무딩(Organic Smoothing) 작업을 시작합니다...</color>");
        SetupFallbackMaterials();
        rendererModule = new DungeonRenderer(width, height, depth, algorithm.VoxelMap, chunkContainer);
        yield return StartCoroutine(rendererModule.BuildOptimizedMeshesRoutine(GetMaterialForBiome, bossCrystalMaterial));

        Debug.Log("<color=cyan>▶ 환경 장식(기둥, 조명 등) 배치 중...</color>");
        DecorateWorld(algorithm.VoxelMap);

        Debug.Log("<color=cyan>▶ NavMesh 베이킹 중... (잠시 대기)</color>");
        yield return null;
        navMeshSurface.BuildNavMesh();

        ExtractValidFloorPositions(algorithm.VoxelMap);
        OnMapGenerated?.Invoke(FloorPositions);

        Debug.Log("<color=yellow>[MapGenerator] 모든 지형 생성 및 베이킹 프로세스가 완료되었습니다!</color>");
        Debug.Log("<color=yellow>=======================================</color>");
    }

    private void ClearMap()
    {
        if (chunkContainer != null) { Destroy(chunkContainer.gameObject); chunkContainer = null; }
        for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
        FloorPositions.Clear();

        GameObject chunkObj = new GameObject("VoxelChunks");
        chunkObj.transform.parent = this.transform;
        chunkContainer = chunkObj.transform;
    }

    private void DecorateWorld(VoxelData[,,] map)
    {
        for (int x = 1; x < width - 1; x += 3)
        {
            for (int y = 1; y < height - 1; y++)
            {
                for (int z = 1; z < depth - 1; z += 3)
                {
                    VoxelData data = map[x, y, z];
                    if (data.Type != VoxelType.Air) continue;

                    if (data.Biome == BiomeType.MineShafts && map[x, y - 1, z].Type == VoxelType.Solid)
                    {
                        if (Random.value < 0.15f)
                        {
                            int ceilY = y;
                            while (ceilY < height && map[x, ceilY, z].Type == VoxelType.Air) ceilY++;

                            if (ceilY < height && (ceilY - y) >= 3 && (ceilY - y) <= 7)
                            {
                                float pillarHeight = ceilY - y;
                                float posY = (y - 0.5f) + (pillarHeight / 2f);
                                GameObject support = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                                support.transform.position = new Vector3(x, posY, z);
                                support.transform.localScale = new Vector3(0.3f, pillarHeight / 2f, 0.3f);
                                support.transform.SetParent(chunkContainer);
                                support.GetComponent<Renderer>().material = woodMaterial;
                            }
                        }
                    }

                    if (data.IsBossRoom && map[x, y - 1, z].Type == VoxelType.Solid)
                    {
                        if (Random.value < 0.005f)
                        {
                            GameObject altar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            altar.transform.position = new Vector3(x, y + 1f, z);
                            altar.transform.localScale = new Vector3(2f, 3f, 2f);
                            altar.transform.SetParent(chunkContainer);
                            altar.GetComponent<Renderer>().material = bossCrystalMaterial;
                            Light l = altar.AddComponent<Light>(); l.color = Color.red; l.intensity = 4f; l.range = 20f;
                        }
                    }
                }
            }
        }
    }

    private void ExtractValidFloorPositions(VoxelData[,,] map)
    {
        FloorPositions.Clear();
        for (int x = 1; x < width - 1; x++)
            for (int y = 1; y < height - 1; y++)
                for (int z = 1; z < depth - 1; z++)
                    if (map[x, y, z].Type == VoxelType.Air && map[x, y - 1, z].Type == VoxelType.Solid)
                        FloorPositions.Add(new Vector3(x, y, z));
    }

    private Material GetMaterialForBiome(BiomeType biome)
    {
        switch (biome)
        {
            case BiomeType.LiminalHalls: return liminalMaterial;
            case BiomeType.MineShafts: return mineMaterial;
            case BiomeType.ConcreteBunker: return bunkerMaterial;
            case BiomeType.DeepAbyss: return abyssMaterial;
            default: return liminalMaterial;
        }
    }

    private void SetupFallbackMaterials()
    {
        if (liminalMaterial == null) { liminalMaterial = new Material(Shader.Find("Standard")); liminalMaterial.color = new Color(0.85f, 0.85f, 0.75f); }
        if (mineMaterial == null) { mineMaterial = new Material(Shader.Find("Standard")); mineMaterial.color = new Color(0.25f, 0.2f, 0.15f); }
        if (bunkerMaterial == null) { bunkerMaterial = new Material(Shader.Find("Standard")); bunkerMaterial.color = new Color(0.4f, 0.45f, 0.45f); }
        if (abyssMaterial == null) { abyssMaterial = new Material(Shader.Find("Standard")); abyssMaterial.color = new Color(0.15f, 0.1f, 0.15f); }
        if (woodMaterial == null) { woodMaterial = new Material(Shader.Find("Standard")); woodMaterial.color = new Color(0.3f, 0.2f, 0.1f); }
        if (bossCrystalMaterial == null)
        {
            bossCrystalMaterial = new Material(Shader.Find("Standard"));
            bossCrystalMaterial.color = new Color(0.8f, 0.1f, 0.1f);
            bossCrystalMaterial.EnableKeyword("_EMISSION");
            bossCrystalMaterial.SetColor("_EmissionColor", new Color(0.8f, 0.1f, 0.1f) * 1.5f);
        }
    }
}