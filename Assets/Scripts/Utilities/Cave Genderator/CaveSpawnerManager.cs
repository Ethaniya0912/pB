using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CaveSystem.Multiplayer
{
    /// <summary>
    /// 지형 특이점 데이터 영구 저장용 전용 구조체입니다.
    /// </summary>
    public struct SpawnerPointData
    {
        public Vector3 position;
        public Vector3 normal;
        public int spawnerType; // 0=일반(몹), 1=스폰구역, 2=보스, 3=보물상자, 4=싱크홀(광원), 99=희귀광맥
        public bool isCleared;  // 플레이어가 소탕하거나 열었는지 추적
    }

    /// <summary>
    /// [리팩토링 3순위: 메타데이터 해독 및 기즈모 오버레이 시각화]
    /// 마칭 큐브에서 회수된 특이점(메타데이터)을 분석하여 서버 메모리에 영구 저장하고,
    /// 씬 뷰(Scene View)에서 직관적인 기즈모를 통해 레벨 디자인 검증을 돕습니다.
    ///
    /// [최적화 v2]
    ///   - 일괄 스폰 제거: StartCoroutine(SpawnBatchCoroutine) 으로 프레임 분산
    ///   - 근접 활성화: 플레이어가 activationRadius 이내일 때만 스폰 큐 진입
    ///   - 주기적 근접 검사: proximityCheckInterval 초마다 미스폰 지점 재검사
    /// </summary>
    public class CaveSpawnerManager : NetworkBehaviour
    {
        public static CaveSpawnerManager Instance { get; private set; }

        // =====================================================================
        // Inspector
        // =====================================================================
        [Header("Spawn Prefabs (NetworkObjects)")]
        public GameObject treasureChestPrefab;
        public GameObject spiderLairPrefab;
        public GameObject bossArenaTriggerPrefab;

        [Tooltip("일반 몹 프리팹들 (랜덤하게 하나가 스폰됩니다)")]
        public GameObject[] regularMobPrefabs;

        [Tooltip("희귀 광맥 실제 채굴용 프리팹")]
        public GameObject rareOrePrefab;

        [Header("Spawner Settings")]
        [Tooltip("플레이어 스폰 가능 위치(RoomType 1)의 최대 수집 개수 제한")]
        public int maxPlayerSpawnPoints = 10;

        [Tooltip("메인/서브 복도 및 일반 방(RoomType 0)에 몹 스포너가 생성될 확률")]
        [Range(0.0001f, 0.1f)] public float corridorMobSpawnProbability = 0.05f;

        [Tooltip("보스 방(RoomType 2)에 몹 스포너가 추가로 생성될 확률")]
        [Range(0f, 1f)] public float bossRoomMobSpawnProbability = 0.3f;

        [Tooltip("희귀 광맥(99)이 생성될 확률 (GPU 데이터 폭주 방지용)")]
        [Range(0.0001f, 0.1f)] public float rareOreSpawnProbability = 0.005f;

        // ─────────────────────────────────────────────────────────────────────
        // Fix-1: 분산 스폰 설정
        // ─────────────────────────────────────────────────────────────────────
        [Header("Batched Spawn Settings (Fix-1: 프레임 분산)")]
        [Tooltip("코루틴 한 배치에서 처리할 최대 스폰 수.\n" +
                 "낮출수록 프레임 히치가 줄지만 스폰 완료까지 시간이 늘어납니다. (권장: 3~5)")]
        [Range(1, 20)]
        public int spawnBatchSize = 4;

        [Tooltip("배치 간 대기 프레임 수. 1 = 매 프레임마다 batchSize 개씩 스폰합니다.")]
        [Range(1, 10)]
        public int spawnBatchFrameInterval = 1;

        // ─────────────────────────────────────────────────────────────────────
        // Fix-2: 근접 활성화 설정
        // ─────────────────────────────────────────────────────────────────────
        [Header("Proximity Activation (Fix-2: 플레이어 근접 시에만 스폰)")]
        [Tooltip("스폰 지점으로부터 이 거리 안에 플레이어가 있을 때만 스폰합니다. (0 = 비활성화, 항상 스폰)")]
        [Range(0f, 200f)]
        public float activationRadius = 60f;

        [Tooltip("미스폰 지점을 재검사하는 주기(초). 플레이어가 이동하면 이 간격으로 새 지점이 활성화됩니다.")]
        [Range(1f, 30f)]
        public float proximityCheckInterval = 5f;

        [Header("Debug Visualization")]
        public bool showGizmos = true;
        public bool showDebugLogs = true;

        // =====================================================================
        // 내부 상태
        // =====================================================================
        // 청크 좌표 → 스포너 목록
        private Dictionary<Vector3Int, List<SpawnerPointData>> chunkSpawnerDataMap
            = new Dictionary<Vector3Int, List<SpawnerPointData>>();

        // 에디터 기즈모용
        [HideInInspector] public List<SpawnerPointData> editorAllSpawners = new List<SpawnerPointData>();

        private int currentPlayerSpawnCount = 0;

        // NavMesh 굽기 완료 전 대기 청크
        private HashSet<Vector3Int> pendingSpawnChunks = new HashSet<Vector3Int>();

        // Fix-2: 근접 조건 미충족으로 아직 스폰되지 않은 청크 (재검사 대상)
        private HashSet<Vector3Int> deferredChunks = new HashSet<Vector3Int>();

        // 분산 스폰 코루틴 실행 중 여부 (중복 실행 방지)
        private Coroutine _spawnCoroutine;
        private Coroutine _proximityCheckCoroutine;

        // ─────────────────────────────────────────────────────────────────────
        // 몹 이름 풀
        // ─────────────────────────────────────────────────────────────────────
        private static readonly string[] MobNamePool =
        {
            "Malphas","Mordred","Vargas","Belial","Ulric","Godrick","Ormuz","Aldrich","Cassian","Gregor",
            "Guldan","Meridia","Zarak","Salazar","Mortimer","Belladonna","Enoch","Malak","Jered","Tenebris",
            "Ratbone","Curse","Slash","Crow","Viper","Scar","Fenrir","Nyx","Jolt","Deadeye"
        };
        private Dictionary<string, int> mobNameCounters = new Dictionary<string, int>();

        // =====================================================================
        // Unity 생명주기
        // =====================================================================
        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
            DontDestroyOnLoad(gameObject);
        }

        public override void OnNetworkSpawn()
        {
            if (NetworkObject != null)
                NetworkObject.DestroyWithScene = false;

            // 서버 전용: 근접 재검사 코루틴 시작
            if (IsServer)
                _proximityCheckCoroutine = StartCoroutine(ProximityCheckLoop());
        }

        public override void OnNetworkDespawn()
        {
            if (_spawnCoroutine != null) { StopCoroutine(_spawnCoroutine); _spawnCoroutine = null; }
            if (_proximityCheckCoroutine != null) { StopCoroutine(_proximityCheckCoroutine); _proximityCheckCoroutine = null; }
        }

        // =====================================================================
        // 공개 API
        // =====================================================================
        public void ClearSpawnerData()
        {
            chunkSpawnerDataMap.Clear();
            editorAllSpawners.Clear();
            currentPlayerSpawnCount = 0;
            pendingSpawnChunks.Clear();
            deferredChunks.Clear();
            mobNameCounters.Clear();

            if (_spawnCoroutine != null) { StopCoroutine(_spawnCoroutine); _spawnCoroutine = null; }
            Log("🔄 기존 스포너 데이터 캐시가 모두 초기화되었습니다.");
        }

        /// <summary>
        /// CaveManager 의 HandleGpuResult 단계에서 호출됩니다.
        /// 지형 데이터를 파싱하고 대기열에 등록합니다.
        /// </summary>
        public void RegisterSpawnerData(Vector3Int chunkPos, Unity.Collections.NativeArray<CaveOreData> rawOreData)
        {
            if (!chunkSpawnerDataMap.ContainsKey(chunkPos))
                chunkSpawnerDataMap[chunkPos] = new List<SpawnerPointData>();

            List<SpawnerPointData> spawnerList = chunkSpawnerDataMap[chunkPos];
            int addedCount = 0, skippedCount = 0;

            // 1. GPU 특수 지형 메타데이터 파싱
            for (int i = 0; i < rawOreData.Length; i++)
            {
                CaveOreData data = rawOreData[i];
                int roomType = (data.oreType >> 16) & 0xFFFF;
                int isOre = data.oreType & 0xFFFF;
                int finalType = (roomType == 0 && isOre > 0) ? 99 : roomType;

                if (finalType <= 0) continue;

                if (finalType == 1 || finalType == 2 || finalType == 3)
                {
                    if (data.normal.y < 0.7f || !HasHeadroom(data.position)) continue;
                }
                else if (finalType == 99)
                {
                    if (Random.value > rareOreSpawnProbability) continue;
                }

                if (finalType == 1 && currentPlayerSpawnCount >= maxPlayerSpawnPoints) continue;

                bool tooClose = false;
                foreach (var ex in spawnerList)
                {
                    if (ex.spawnerType != finalType) continue;
                    float minSqr = (finalType == 99) ? 100f : 4f;
                    if (Vector3.SqrMagnitude(ex.position - data.position) < minSqr)
                    { tooClose = true; break; }
                }

                if (!tooClose && (finalType == 1 || finalType == 2 || finalType == 4))
                    if (spawnerList.Any(s => s.spawnerType == finalType)) tooClose = true;

                if (!tooClose)
                {
                    var sp = new SpawnerPointData
                    { position = data.position, normal = data.normal, spawnerType = finalType, isCleared = false };
                    spawnerList.Add(sp);
                    editorAllSpawners.Add(sp);
                    if (finalType == 1) currentPlayerSpawnCount++;
                    addedCount++;
                }
                else skippedCount++;
            }

            // 2. 일반 몹 물리 탐색 (RoomType 0)
            GenerateRegularMobSpawns(chunkPos, spawnerList, ref addedCount);

            if (addedCount > 0)
                Log($"[분석 완료] 청크 {chunkPos} → 신규 {addedCount}개 / 스킵 {skippedCount}개 / 누적 {spawnerList.Count}개");

            // NavMesh 굽기 완료 후 ProcessPendingSpawns() 가 처리
            if (IsServer && UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex != 0)
                pendingSpawnChunks.Add(chunkPos);
        }

        /// <summary>
        /// CaveManager 에서 NavMesh 굽기 완료 직후 호출됩니다.
        /// 근접 조건 검사 후 조건 충족 청크는 분산 스폰 큐에 넣습니다.
        /// </summary>
        public void ProcessPendingSpawns()
        {
            if (pendingSpawnChunks.Count == 0) return;

            var toProcess = new List<Vector3Int>(pendingSpawnChunks);
            pendingSpawnChunks.Clear();

            // Fix-2: 근접 조건이 없거나, 플레이어가 범위 내에 있는 청크만 즉시 스폰 큐에 진입
            var readyChunks = new List<Vector3Int>();
            var deferChunks = new List<Vector3Int>();

            foreach (var chunkPos in toProcess)
            {
                if (activationRadius <= 0f || IsAnyPlayerNearChunk(chunkPos))
                    readyChunks.Add(chunkPos);
                else
                    deferChunks.Add(chunkPos);
            }

            foreach (var c in deferChunks)
                deferredChunks.Add(c);

            if (readyChunks.Count > 0)
                EnqueueBatchSpawn(readyChunks);
        }

        // =====================================================================
        // Fix-1: 분산 스폰 코루틴
        // =====================================================================
        /// <summary>
        /// 스폰할 청크 목록을 받아 batchSize 개씩 프레임에 나눠 처리합니다.
        /// </summary>
        private void EnqueueBatchSpawn(List<Vector3Int> chunks)
        {
            if (_spawnCoroutine != null)
            {
                // 이미 실행 중이면 새 청크를 이어서 처리하도록 코루틴 재시작
                StopCoroutine(_spawnCoroutine);
            }
            _spawnCoroutine = StartCoroutine(SpawnBatchCoroutine(chunks));
        }

        private IEnumerator SpawnBatchCoroutine(List<Vector3Int> chunks)
        {
            // 청크 → 개별 SpawnerPoint 목록으로 평탄화
            var allPoints = new List<(Vector3Int chunk, int index)>();
            foreach (var chunkPos in chunks)
            {
                if (!chunkSpawnerDataMap.TryGetValue(chunkPos, out var list)) continue;
                for (int i = 0; i < list.Count; i++)
                    allPoints.Add((chunkPos, i));
            }

            int processedThisBatch = 0;

            for (int i = 0; i < allPoints.Count; i++)
            {
                var (chunkPos, idx) = allPoints[i];
                if (!chunkSpawnerDataMap.TryGetValue(chunkPos, out var list)) continue;

                SpawnSingleEntry(chunkPos, list, idx);

                processedThisBatch++;

                // batchSize 개 처리마다 지정 프레임 수 대기
                if (processedThisBatch >= spawnBatchSize)
                {
                    processedThisBatch = 0;
                    for (int f = 0; f < spawnBatchFrameInterval; f++)
                        yield return null;
                }
            }

            _spawnCoroutine = null;
            Log($"[분산 스폰 완료] 총 {allPoints.Count}개 처리 (배치크기: {spawnBatchSize}, 프레임간격: {spawnBatchFrameInterval})");
        }

        // =====================================================================
        // Fix-2: 근접 재검사 루프
        // =====================================================================
        private IEnumerator ProximityCheckLoop()
        {
            var wait = new WaitForSeconds(proximityCheckInterval);
            while (true)
            {
                yield return wait;
                CheckDeferredChunks();
            }
        }

        private void CheckDeferredChunks()
        {
            if (deferredChunks.Count == 0) return;

            var nowReady = new List<Vector3Int>();
            foreach (var chunkPos in deferredChunks)
            {
                if (IsAnyPlayerNearChunk(chunkPos))
                    nowReady.Add(chunkPos);
            }

            if (nowReady.Count == 0) return;

            foreach (var c in nowReady)
                deferredChunks.Remove(c);

            Log($"[근접 재검사] 플레이어 접근으로 {nowReady.Count}개 청크 스폰 큐 진입");
            EnqueueBatchSpawn(nowReady);
        }

        /// <summary>
        /// 청크 중심에서 activationRadius 안에 로컬 오너 플레이어가 있는지 검사합니다.
        /// 서버에서만 호출됩니다.
        /// </summary>
        private bool IsAnyPlayerNearChunk(Vector3Int chunkPos)
        {
            if (activationRadius <= 0f) return true;

            Vector3 chunkCenter = GetChunkCenter(chunkPos);
            float sqrRadius = activationRadius * activationRadius;

            // NetworkManager 스폰 목록에서 PlayerManager 를 찾습니다.
            if (NetworkManager.Singleton == null) return false;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                if (Vector3.SqrMagnitude(client.PlayerObject.transform.position - chunkCenter) <= sqrRadius)
                    return true;
            }
            return false;
        }

        private Vector3 GetChunkCenter(Vector3Int chunkPos)
        {
            if (CaveManager.Instance == null || CaveManager.Instance.chunkManager == null)
                return new Vector3(chunkPos.x, chunkPos.y, chunkPos.z) * 32f;

            float chunkSize = CaveManager.Instance.chunkManager.ChunkSize
                            * CaveManager.Instance.chunkManager.VoxelSize;
            return (new Vector3(chunkPos.x, chunkPos.y, chunkPos.z) + Vector3.one * 0.5f) * chunkSize;
        }

        // =====================================================================
        // 개별 스폰 엔트리 (SpawnBatchCoroutine 에서 호출)
        // =====================================================================
        private void SpawnSingleEntry(Vector3Int chunkPos, List<SpawnerPointData> list, int idx)
        {
            if (idx >= list.Count) return;
            SpawnerPointData data = list[idx];
            if (data.isCleared) return;

            GameObject spawnedObj = null;

            switch (data.spawnerType)
            {
                case 0: // 일반 몹
                    if (regularMobPrefabs != null && regularMobPrefabs.Length > 0)
                    {
                        Vector3 pos = data.position;
                        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit nh, 3f, UnityEngine.AI.NavMesh.AllAreas))
                            pos = nh.position;

                        var prefab = regularMobPrefabs[Random.Range(0, regularMobPrefabs.Length)];
                        spawnedObj = Instantiate(prefab, pos, Quaternion.identity);
                        spawnedObj.name = GetRandomMobName();
                    }
                    break;

                case 1: // 플레이어 스폰 구역 — 오브젝트 스폰 없음
                    break;

                case 2: // 보스 방
                    if (bossArenaTriggerPrefab)
                        spawnedObj = Instantiate(bossArenaTriggerPrefab, data.position, Quaternion.identity);

                    if (regularMobPrefabs != null && regularMobPrefabs.Length > 0
                        && Random.value < bossRoomMobSpawnProbability)
                    {
                        Vector3 mobPos = data.position + Vector3.right * 1.5f;
                        if (HasHeadroom(mobPos))
                        {
                            if (UnityEngine.AI.NavMesh.SamplePosition(mobPos, out UnityEngine.AI.NavMeshHit nh, 3f, UnityEngine.AI.NavMesh.AllAreas))
                                mobPos = nh.position;

                            var prefab = regularMobPrefabs[Random.Range(0, regularMobPrefabs.Length)];
                            var mobObj = Instantiate(prefab, mobPos, Quaternion.identity);
                            mobObj.name = GetRandomMobName();
                            ActivateAndNetworkSpawn(mobObj);
                        }
                    }
                    break;

                case 3: // 보물 상자
                    if (treasureChestPrefab)
                    {
                        Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, data.normal), data.normal);
                        spawnedObj = Instantiate(treasureChestPrefab, data.position, rot);
                    }
                    break;

                case 99: // 희귀 광맥
                    if (rareOrePrefab)
                    {
                        Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, data.normal), data.normal);
                        spawnedObj = Instantiate(rareOrePrefab, data.position, rot);
                    }
                    break;
            }

            if (spawnedObj != null)
            {
                ActivateAndNetworkSpawn(spawnedObj);
                data.isCleared = true;
                list[idx] = data;
            }
        }

        private void ActivateAndNetworkSpawn(GameObject obj)
        {
            if (obj.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent))
                agent.enabled = true;

            var netObj = obj.GetComponent<NetworkObject>();
            if (netObj != null && !netObj.IsSpawned)
                netObj.Spawn();
        }

        // =====================================================================
        // 레거시 호환 — TrySpawnEntitiesInChunk (외부에서 직접 호출 시 대응)
        // =====================================================================
        public void TrySpawnEntitiesInChunk(Vector3Int chunkPos)
        {
            if (!IsServer || !chunkSpawnerDataMap.ContainsKey(chunkPos)) return;

            if (activationRadius > 0f && !IsAnyPlayerNearChunk(chunkPos))
            {
                deferredChunks.Add(chunkPos);
                return;
            }
            EnqueueBatchSpawn(new List<Vector3Int> { chunkPos });
        }

        // =====================================================================
        // 내부 유틸
        // =====================================================================
        private void GenerateRegularMobSpawns(Vector3Int chunkPos, List<SpawnerPointData> spawnerList, ref int addedCount)
        {
            if (CaveManager.Instance == null || CaveManager.Instance.chunkManager == null) return;

            float chunkSize = CaveManager.Instance.chunkManager.ChunkSize * CaveManager.Instance.chunkManager.VoxelSize;
            Vector3 chunkBase = new Vector3(chunkPos.x, chunkPos.y, chunkPos.z) * chunkSize;

            int terrainMask = LayerMask.GetMask("Default", "Terrain");
            if (terrainMask == 0) terrainMask = ~0;

            for (int i = 0; i < 30; i++)
            {
                if (Random.value > corridorMobSpawnProbability * 10f) continue;

                Vector3 rayStart = chunkBase + new Vector3(
                    Random.Range(0f, chunkSize), chunkSize + 2f, Random.Range(0f, chunkSize));

                if (!Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, chunkSize + 4f, terrainMask)) continue;
                if (hit.normal.y < 0.7f) continue;
                if (!HasHeadroom(hit.point)) continue;

                bool tooClose = spawnerList.Any(ex => ex.spawnerType == 0
                    && Vector3.SqrMagnitude(ex.position - hit.point) < 100f);

                if (tooClose) continue;

                var sp = new SpawnerPointData
                { position = hit.point, normal = hit.normal, spawnerType = 0, isCleared = false };
                spawnerList.Add(sp);
                editorAllSpawners.Add(sp);
                addedCount++;
                Log($"[물리 탐색] 🕷️ 일반 몹 스포너: {hit.point}");
            }
        }

        private bool HasHeadroom(Vector3 position)
        {
            int mask = LayerMask.GetMask("Default", "Terrain");
            if (mask == 0) mask = ~0;
            return !Physics.SphereCast(position + Vector3.up * 0.5f, 0.4f, Vector3.up, out _, 1.5f, mask);
        }

        private string GetRandomMobName()
        {
            string name = MobNamePool[Random.Range(0, MobNamePool.Length)];
            if (!mobNameCounters.ContainsKey(name)) mobNameCounters[name] = 0;
            mobNameCounters[name]++;
            return $"Mob_{name}_{mobNameCounters[name]}";
        }

        private void Log(string msg)
        {
            if (showDebugLogs) Debug.Log($"<color=#E67E22>[CaveSpawnerManager]</color> {msg}");
        }

        // =====================================================================
        // 에디터 기즈모
        // =====================================================================
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos || editorAllSpawners == null || editorAllSpawners.Count == 0) return;

            var labelStyle = new GUIStyle
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            // Fix-2: 활성화 반경 시각화 — 청크 중심 기즈모 (에디터 전용)
            if (activationRadius > 0f)
            {
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.08f);
                foreach (var kv in chunkSpawnerDataMap)
                {
                    Vector3 center = GetChunkCenter(kv.Key);
                    Gizmos.DrawWireSphere(center, activationRadius);
                }
            }

            var typeCounters = new Dictionary<int, int>();

            foreach (var data in editorAllSpawners)
            {
                if (!typeCounters.ContainsKey(data.spawnerType)) typeCounters[data.spawnerType] = 0;
                typeCounters[data.spawnerType]++;
                int cnt = typeCounters[data.spawnerType];

                Color gizmoColor = Color.white;
                string typeName = "Unknown";
                float yOffset = 1.0f;

                switch (data.spawnerType)
                {
                    case 0:
                        gizmoColor = new Color(1f, 0.4f, 0.2f, 0.5f);
                        Gizmos.color = gizmoColor;
                        Gizmos.DrawSphere(data.position + Vector3.up, 0.4f);
                        Gizmos.DrawLine(data.position, data.position + Vector3.up * 2f);
                        typeName = "Mob Spawn"; yOffset = 2.2f;
                        break;
                    case 1:
                        gizmoColor = new Color(0f, 1f, 0f, 0.5f);
                        Gizmos.color = gizmoColor;
                        Gizmos.DrawCube(data.position + Vector3.up, new Vector3(1f, 2f, 1f));
                        typeName = "Player Spawn"; yOffset = 2.5f;
                        break;
                    case 2:
                        gizmoColor = new Color(1f, 0.2f, 0.2f, 0.7f);
                        Gizmos.color = gizmoColor;
                        Gizmos.DrawCube(data.position, Vector3.one * 1.5f);
                        typeName = "Boss Arena"; yOffset = 1.5f;
                        break;
                    case 3:
                        gizmoColor = Color.yellow;
                        Gizmos.color = gizmoColor;
                        Gizmos.DrawSphere(data.position, 0.6f);
                        typeName = "Treasure"; yOffset = 1.0f;
                        break;
                    case 4:
                        gizmoColor = new Color(0f, 1f, 1f, 0.6f);
                        Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
                        Vector3 top = data.position + Vector3.up * 15f;
                        Gizmos.DrawLine(top, data.position + new Vector3(2, 0, 2));
                        Gizmos.DrawLine(top, data.position + new Vector3(-2, 0, 2));
                        Gizmos.DrawLine(top, data.position + new Vector3(2, 0, -2));
                        Gizmos.DrawLine(top, data.position + new Vector3(-2, 0, -2));
                        Gizmos.color = gizmoColor;
                        Gizmos.DrawWireSphere(data.position, 2.8f);
                        typeName = "Sinkhole"; yOffset = 3.5f;
                        break;
                    case 99:
                        gizmoColor = new Color(0f, 1f, 0.5f, 0.8f);
                        Gizmos.color = gizmoColor;
                        Gizmos.DrawSphere(data.position, 0.3f);
                        typeName = "Rare Ore"; yOffset = 0.6f;
                        break;
                }

                labelStyle.normal.textColor = gizmoColor;
                Handles.Label(data.position + Vector3.up * yOffset, $"{typeName} #{cnt}", labelStyle);
            }
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(CaveSpawnerManager))]
    public class CaveSpawnerManagerEditor : UnityEditor.Editor
    {
        private Dictionary<int, int> typeIndexMap = new Dictionary<int, int>();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manager = (CaveSpawnerManager)target;

            if (manager.editorAllSpawners == null || manager.editorAllSpawners.Count == 0)
            {
                EditorGUILayout.HelpBox("아직 추출된 스포너 데이터가 없습니다. (지형을 구우면 나타납니다)", MessageType.Info);
                return;
            }

            GUILayout.Space(15);
            GUILayout.Label("🔍 씬 뷰 스포너 네비게이터", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Go 버튼을 누르면 해당 타입의 다음 오브젝트로 씬 카메라가 이동합니다.", MessageType.None);

            var grouped = manager.editorAllSpawners.GroupBy(x => x.spawnerType).OrderBy(x => x.Key);

            foreach (var group in grouped)
            {
                int sType = group.Key;
                int count = group.Count();
                if (!typeIndexMap.ContainsKey(sType)) typeIndexMap[sType] = 0;

                EditorGUILayout.BeginHorizontal("box");
                GUILayout.Label($"<b>{GetTypeName(sType)}</b> ({count}개)",
                    new GUIStyle(EditorStyles.label) { richText = true }, GUILayout.Width(170));
                GUILayout.Label($"[{typeIndexMap[sType] + 1}/{count}]", GUILayout.Width(60));
                if (GUILayout.Button("Go", GUILayout.Width(50), GUILayout.Height(20)))
                    FocusOnNext(sType, group.ToList());
                EditorGUILayout.EndHorizontal();
            }
        }

        private string GetTypeName(int t) => t switch
        {
            0 => "일반 몹 스폰 (0)",
            1 => "플레이어 스폰 구역 (1)",
            2 => "보스 아레나 (2)",
            3 => "보물 상자 (3)",
            4 => "싱크홀/갓레이 (4)",
            99 => "희귀 광맥 (99)",
            _ => $"알 수 없음 ({t})"
        };

        private void FocusOnNext(int type, List<SpawnerPointData> list)
        {
            int idx = typeIndexMap[type];
            var target = list[idx];
            var view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                Vector3 lookDir = (target.normal == Vector3.zero) ? Vector3.forward : target.normal;
                Vector3 offset = lookDir * 3f + Vector3.up * 1.5f;
                view.LookAt(target.position + offset, Quaternion.LookRotation(-offset.normalized), 3f);
                view.Repaint();
            }
            typeIndexMap[type] = (idx + 1) % list.Count;
            SceneView.RepaintAll();
        }
    }
#endif
}