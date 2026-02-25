using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI 스포너 (단일 책임: 제공받은 위치 데이터를 바탕으로 적군을 스폰하고 파라미터를 세팅하는 역할만 수행)
/// </summary>
[RequireComponent(typeof(SimpleMapGenerator))]
public class SimpleAISpawner : MonoBehaviour
{
    [Header("AI Testing Setup")]
    [Tooltip("지능이 높은 테스트용 적 프리팹 (순찰 및 지원요청)")]
    public GameObject smartEnemyPrefab;
    [Tooltip("지능이 낮은 테스트용 적 프리팹 (들개, 괴물 등 무리생활)")]
    public GameObject dumbEnemyPrefab;

    [Tooltip("스폰할 스마트 적의 수")]
    public int smartEnemyCount = 3;
    [Tooltip("스폰할 무식한 적의 수")]
    public int dumbEnemyCount = 5;

    private SimpleMapGenerator mapGenerator;

    private void Awake()
    {
        mapGenerator = GetComponent<SimpleMapGenerator>();

        // 맵 생성기의 이벤트에 스폰 로직을 구독(Subscribe)시킴
        if (mapGenerator != null)
        {
            mapGenerator.OnMapGenerated += HandleMapGenerated;
        }
    }

    private void OnDestroy()
    {
        // 메모리 누수 방지를 위해 이벤트 구독 해제
        if (mapGenerator != null)
        {
            mapGenerator.OnMapGenerated -= HandleMapGenerated;
        }
    }

    /// <summary>
    /// 맵 생성이 완료되면 이 함수가 자동으로 호출됩니다.
    /// </summary>
    /// <param name="validFloorPositions">맵 생성기가 만든 이동 가능한 바닥 위치 리스트</param>
    private void HandleMapGenerated(List<Vector3> validFloorPositions)
    {
        if (validFloorPositions == null || validFloorPositions.Count < 10)
        {
            Debug.LogWarning("[SimpleAISpawner] 스폰 가능한 바닥 공간이 부족합니다.");
            return;
        }

        SpawnTestingAI(validFloorPositions);
    }

    private void SpawnTestingAI(List<Vector3> floorPositions)
    {
        // --- 스마트 적 (순찰자) 배치 ---
        if (smartEnemyPrefab != null)
        {
            for (int i = 0; i < smartEnemyCount; i++)
            {
                Vector3 spawnPos = floorPositions[Random.Range(0, floorPositions.Count)] + Vector3.up * 1f;
                GameObject enemy = Instantiate(smartEnemyPrefab, spawnPos, Quaternion.identity);

                AICharacterCombatManager combatManager = enemy.GetComponent<AICharacterCombatManager>();
                if (combatManager != null)
                {
                    // 이 적만을 위한 고유 순찰 구역(웨이포인트) 3~5개 무작위 생성
                    int waypointCount = Random.Range(3, 6);
                    List<Transform> myWaypoints = new List<Transform>();
                    for (int w = 0; w < waypointCount; w++)
                    {
                        Vector3 wpPos = floorPositions[Random.Range(0, floorPositions.Count)];
                        GameObject wp = new GameObject($"WP_Smart_{i}_{w}");
                        wp.transform.position = wpPos;
                        wp.transform.parent = this.transform; // 스포너 하위에 웨이포인트 정리
                        myWaypoints.Add(wp.transform);
                    }

                    combatManager.patrolWaypoints = myWaypoints.ToArray();
                    combatManager.aiIntelligenceLevel = 8;
                    combatManager.combatLevel = 8;
                }
            }
        }

        // --- 멍청한 적 (무리 생활자) 배치 ---
        if (dumbEnemyPrefab != null)
        {
            for (int i = 0; i < dumbEnemyCount; i++)
            {
                Vector3 spawnPos = floorPositions[Random.Range(0, floorPositions.Count)] + Vector3.up * 1f;
                GameObject enemy = Instantiate(dumbEnemyPrefab, spawnPos, Quaternion.identity);

                AICharacterCombatManager combatManager = enemy.GetComponent<AICharacterCombatManager>();
                if (combatManager != null)
                {
                    combatManager.aiIntelligenceLevel = 3;
                    combatManager.combatLevel = 3;
                    combatManager.groupUpChance = 95f; // 아주 높은 확률로 동료를 찾아 무리 지음
                }
            }
        }

        Debug.Log("<color=green>[SimpleAISpawner] 지형 데이터를 기반으로 AI 자동 스폰 완료!</color>");
    }
}