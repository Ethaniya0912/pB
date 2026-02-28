using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

namespace CaveSystem.Multiplayer
{
    /// <summary>
    /// 지형 특이점 데이터 영구 저장용 전용 구조체입니다.
    /// </summary>
    public struct SpawnerPointData
    {
        public Vector3 position;
        public Vector3 normal;
        public int spawnerType; // 0=일반, 1=스폰, 2=보스, 3=보물상자, 4=싱크홀(광원), 99=희귀광맥
        public bool isCleared;  // 플레이어가 소탕하거나 열었는지 추적
    }

    /// <summary>
    /// [리팩토링 3순위: 메타데이터 해독 및 기즈모 오버레이 시각화]
    /// 마칭 큐브에서 회수된 특이점(메타데이터)을 분석하여 서버 메모리에 영구 저장하고,
    /// 씬 뷰(Scene View)에서 직관적인 기즈모를 통해 레벨 디자인 검증을 돕습니다.
    /// </summary>
    public class CaveSpawnerManager : NetworkBehaviour
    {
        public static CaveSpawnerManager Instance { get; private set; }

        [Header("Spawn Prefabs (NetworkObjects)")]
        public GameObject treasureChestPrefab;
        public GameObject spiderLairPrefab;
        public GameObject bossArenaTriggerPrefab;

        [Header("Debug Visualization")]
        public bool showGizmos = true;

        // 청크 좌표를 Key로 사용하는 전용 관리 딕셔너리
        private Dictionary<Vector3Int, List<SpawnerPointData>> chunkSpawnerDataMap = new Dictionary<Vector3Int, List<SpawnerPointData>>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            // 씬 전환 시에도 특이점 데이터가 소멸하지 않도록 보호
            DontDestroyOnLoad(gameObject);
        }

        public override void OnNetworkSpawn()
        {
            if (NetworkObject != null)
            {
                NetworkObject.DestroyWithScene = false;
            }
        }

        /// <summary>
        /// CaveManager의 HandleGpuResult 단계에서 광석/특이점 데이터가 회수될 때 호출됩니다.
        /// </summary>
        public void RegisterSpawnerData(Vector3Int chunkPos, Unity.Collections.NativeArray<CaveOreData> rawOreData)
        {
            // 에디터에서 기즈모를 보기 위해 호스트/서버 상관없이 데이터 캐싱은 허용 (실제 스폰은 서버만)
            if (!chunkSpawnerDataMap.ContainsKey(chunkPos))
            {
                chunkSpawnerDataMap[chunkPos] = new List<SpawnerPointData>();
            }

            List<SpawnerPointData> spawnerList = chunkSpawnerDataMap[chunkPos];

            // 1. 넘어온 데이터 중 비트플래그가 담긴 oreType을 해독하여 저장
            for (int i = 0; i < rawOreData.Length; i++)
            {
                CaveOreData data = rawOreData[i];

                // [🔥 메타데이터 해독] 
                // 상위 16비트: roomType (어떤 방에 속해 있는가)
                // 하위 16비트: isOre (단순 광물인가)
                int roomType = (data.oreType >> 16) & 0xFFFF;
                int isOre = data.oreType & 0xFFFF;

                // 일반 바위가 아닌 '기믹 방'이거나 '광물'일 때 캐싱 진행
                if (roomType > 0 || isOre > 0)
                {
                    // 겹침 방지를 위해 동일 좌표 근처에 이미 등록되었는지 검사 (거리 필터링)
                    bool isTooClose = false;
                    foreach (var existing in spawnerList)
                    {
                        if (Vector3.SqrMagnitude(existing.position - data.position) < 4.0f) // 반경 2m 이내 배척
                        {
                            isTooClose = true;
                            break;
                        }
                    }

                    if (!isTooClose)
                    {
                        int finalType = roomType > 0 ? roomType : 99; // 방 속성이 없으면 광물(99)로 취급

                        spawnerList.Add(new SpawnerPointData
                        {
                            position = data.position,
                            normal = data.normal,
                            spawnerType = finalType,
                            isCleared = false
                        });
                    }
                }
            }

            // 등록 완료 후 즉시 스폰 시도 (게임 씬, 서버 권한일 경우에만)
            if (IsServer && UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex != 0)
            {
                TrySpawnEntitiesInChunk(chunkPos);
            }
        }

        /// <summary>
        /// 특정 청크가 로드되거나 플레이어가 접근했을 때 저장된 데이터를 바탕으로 NetworkObject를 스폰합니다.
        /// </summary>
        public void TrySpawnEntitiesInChunk(Vector3Int chunkPos)
        {
            if (!IsServer || !chunkSpawnerDataMap.ContainsKey(chunkPos)) return;

            List<SpawnerPointData> spawnerList = chunkSpawnerDataMap[chunkPos];

            for (int i = 0; i < spawnerList.Count; i++)
            {
                SpawnerPointData data = spawnerList[i];
                if (data.isCleared) continue; // 이미 처리된 기믹은 건너뜀

                GameObject spawnedObj = null;

                switch (data.spawnerType)
                {
                    case 2: // 보스 방 (트리거 스폰)
                        if (bossArenaTriggerPrefab) spawnedObj = Instantiate(bossArenaTriggerPrefab, data.position, Quaternion.identity);
                        break;
                    case 3: // 보물 방 (상자 스폰)
                        if (treasureChestPrefab)
                        {
                            Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, data.normal), data.normal);
                            spawnedObj = Instantiate(treasureChestPrefab, data.position, rot);
                        }
                        break;
                        // (※ case 4: 싱크홀 광원은 CaveLightingManager가 별도로 C# 로컬 인스턴싱 처리)
                }

                if (spawnedObj != null)
                {
                    NetworkObject netObj = spawnedObj.GetComponent<NetworkObject>();
                    if (netObj != null && !netObj.IsSpawned)
                    {
                        netObj.Spawn();
                    }

                    data.isCleared = true;
                    spawnerList[i] = data;
                }
            }
        }

        /// <summary>
        /// [🔥 핵심 추가] 디버깅을 위해 에디터 씬 뷰에 스포너 위치를 기즈모로 시각화합니다.
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!showGizmos || chunkSpawnerDataMap == null || chunkSpawnerDataMap.Count == 0) return;

            foreach (var kvp in chunkSpawnerDataMap)
            {
                foreach (var data in kvp.Value)
                {
                    switch (data.spawnerType)
                    {
                        case 2: // 보스 방 (붉은 반투명 큐브)
                            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.7f);
                            Gizmos.DrawCube(data.position, Vector3.one * 1.5f);
                            break;
                        case 3: // 보물 방 (노란색 구)
                            Gizmos.color = Color.yellow;
                            Gizmos.DrawSphere(data.position, 0.6f);
                            break;
                        case 4: // 싱크홀 구역 (하늘색 광선)
                            Gizmos.color = Color.cyan;
                            Gizmos.DrawWireSphere(data.position, 2.0f);
                            Gizmos.DrawRay(data.position, Vector3.down * 15f);
                            break;
                        case 99: // 단순 희귀 광맥
                            Gizmos.color = new Color(0f, 1f, 0.5f, 0.8f);
                            Gizmos.DrawSphere(data.position, 0.3f);
                            break;
                    }
                }
            }
        }
    }
}