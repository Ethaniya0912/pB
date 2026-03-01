using UnityEngine;
using Unity.Netcode;
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
    /// </summary>
    public class CaveSpawnerManager : NetworkBehaviour
    {
        public static CaveSpawnerManager Instance { get; private set; }

        [Header("Spawn Prefabs (NetworkObjects)")]
        public GameObject treasureChestPrefab;
        public GameObject spiderLairPrefab;
        public GameObject bossArenaTriggerPrefab;

        [Tooltip("일반 몹 프리팹들 (랜덤하게 하나가 스폰됩니다)")]
        public GameObject[] regularMobPrefabs; // 배열로 변경하여 여러 종류의 몹 스폰 지원

        [Tooltip("희귀 광맥 실제 채굴용 프리팹")]
        public GameObject rareOrePrefab; // 광맥 스폰용 추가

        // 스폰 정책 관련 파라미터 (인스펙터 제어용)
        [Header("Spawner Settings")]
        [Tooltip("플레이어 스폰 가능 위치(RoomType 1)의 최대 수집 개수 제한")]
        public int maxPlayerSpawnPoints = 10;

        [Tooltip("메인/서브 복도 및 일반 방(RoomType 0)에 몹 스포너가 생성될 확률 (0.001 ~ 0.1 추천)")]
        [Range(0.0001f, 0.1f)] public float corridorMobSpawnProbability = 0.05f;

        [Tooltip("보스 방(RoomType 2)에 몹 스포너가 추가로 생성될 확률")]
        [Range(0f, 1f)] public float bossRoomMobSpawnProbability = 0.3f;

        [Tooltip("희귀 광맥(99)이 생성될 확률 (GPU 데이터 폭주 방지용)")]
        [Range(0.0001f, 0.1f)] public float rareOreSpawnProbability = 0.005f;

        [Header("Debug Visualization")]
        public bool showGizmos = true;
        public bool showDebugLogs = true;

        // 청크 좌표를 Key로 사용하는 전용 관리 딕셔너리
        private Dictionary<Vector3Int, List<SpawnerPointData>> chunkSpawnerDataMap = new Dictionary<Vector3Int, List<SpawnerPointData>>();

        // 에디터 네비게이션용 데이터 리스트 (기즈모 및 Go 버튼용)
        [HideInInspector] public List<SpawnerPointData> editorAllSpawners = new List<SpawnerPointData>();

        // 플레이어 스폰 포인트 누적 카운트
        private int currentPlayerSpawnCount = 0;

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
        /// 새로운 맵 생성 시 누적된 스포너 데이터를 완전히 초기화합니다.
        /// </summary>
        public void ClearSpawnerData()
        {
            chunkSpawnerDataMap.Clear();
            editorAllSpawners.Clear();
            currentPlayerSpawnCount = 0;
            Log("🔄 기존 스포너 데이터 캐시가 모두 초기화되었습니다.");
        }

        private void Log(string msg)
        {
            if (showDebugLogs) Debug.Log($"<color=#E67E22>[CaveSpawnerManager]</color> {msg}");
        }

        /// <summary>
        /// CaveManager의 HandleGpuResult 단계에서 광석/특이점 데이터가 회수될 때 호출됩니다.
        /// </summary>
        public void RegisterSpawnerData(Vector3Int chunkPos, Unity.Collections.NativeArray<CaveOreData> rawOreData)
        {
            if (!chunkSpawnerDataMap.ContainsKey(chunkPos))
            {
                chunkSpawnerDataMap[chunkPos] = new List<SpawnerPointData>();
            }

            List<SpawnerPointData> spawnerList = chunkSpawnerDataMap[chunkPos];

            int addedCount = 0;
            int skippedOverlapCount = 0;

            // 1. GPU에서 넘어온 특수 지형(보스, 보물, 스폰, 광맥 등) 메타데이터 파싱
            for (int i = 0; i < rawOreData.Length; i++)
            {
                CaveOreData data = rawOreData[i];

                int roomType = (data.oreType >> 16) & 0xFFFF;
                int isOre = data.oreType & 0xFFFF;

                int finalType = roomType;
                if (roomType == 0 && isOre > 0) finalType = 99; // 방 속성 없이 광물 플래그만 있으면 광맥

                if (finalType > 0) // (GPU는 최적화를 위해 Type 0을 배출하지 않음)
                {
                    if (finalType == 1 || finalType == 2 || finalType == 3)
                    {
                        // 스폰, 보스, 보물 상자도 바닥에만 배치
                        if (data.normal.y < 0.7f) continue;
                        if (!HasHeadroom(data.position)) continue;
                    }
                    else if (finalType == 99)
                    {
                        // [추가] 희귀 광맥 데이터 폭주 방지를 위한 확률 필터링
                        if (Random.value > rareOreSpawnProbability) continue;
                    }

                    if (finalType == 1 && currentPlayerSpawnCount >= maxPlayerSpawnPoints)
                        continue;

                    bool isTooClose = false;
                    foreach (var existing in spawnerList)
                    {
                        if (existing.spawnerType == finalType)
                        {
                            float sqrMinDist = 4.0f; // 기본 2m 배척 반경
                            if (finalType == 99) sqrMinDist = 100.0f; // [수정] 광맥도 뭉치지 않게 최소 10m 간격으로 대폭 증가

                            if (Vector3.SqrMagnitude(existing.position - data.position) < sqrMinDist)
                            {
                                isTooClose = true;
                                break;
                            }
                        }
                    }

                    // 중요 구역 중복 방지
                    if (!isTooClose && (finalType == 1 || finalType == 2 || finalType == 4))
                    {
                        if (spawnerList.Any(s => s.spawnerType == finalType)) isTooClose = true;
                    }

                    if (!isTooClose)
                    {
                        var newSpawner = new SpawnerPointData
                        {
                            position = data.position,
                            normal = data.normal,
                            spawnerType = finalType,
                            isCleared = false
                        };

                        spawnerList.Add(newSpawner);
                        editorAllSpawners.Add(newSpawner);

                        if (finalType == 1) currentPlayerSpawnCount++;
                        addedCount++;
                    }
                    else
                    {
                        skippedOverlapCount++;
                    }
                }
            }

            // [핵심 추가] 2. 일반 몹 스포너(RoomType 0) 강제 물리 탐색
            // GPU가 빈 공간(Type 0) 데이터를 보내면 병목이 생기므로, C#이 물리 엔진을 통해 직접 찾아냅니다.
            GenerateRegularMobSpawns(chunkPos, spawnerList, ref addedCount);

            if (addedCount > 0)
            {
                Log($"[분석 완료] 청크 {chunkPos} -> 유효 데이터 신규 등록: {addedCount}개 / 겹침 배척(스킵): {skippedOverlapCount}개 / 누적: {spawnerList.Count}개");
            }

            // 등록 완료 후 즉시 스폰 시도 (게임 씬, 서버 권한일 경우에만)
            if (IsServer && UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex != 0)
            {
                TrySpawnEntitiesInChunk(chunkPos);
            }
        }

        /// <summary>
        /// 완성된 물리 메쉬 위에서 Raycast를 쏘아, 완벽한 일반 몹 스폰 지점을 수색합니다.
        /// </summary>
        private void GenerateRegularMobSpawns(Vector3Int chunkPos, List<SpawnerPointData> spawnerList, ref int addedCount)
        {
            if (CaveManager.Instance == null || CaveManager.Instance.chunkManager == null) return;

            float chunkSize = CaveManager.Instance.chunkManager.ChunkSize * CaveManager.Instance.chunkManager.VoxelSize;
            Vector3 chunkBase = new Vector3(chunkPos.x, chunkPos.y, chunkPos.z) * chunkSize;

            int attemptCount = 30; // 청크당 30번 레이캐스트 스캔 (비용 매우 저렴함)

            // Terrain 및 Default 레이어 검사
            int terrainMask = LayerMask.GetMask("Default", "Terrain");
            if (terrainMask == 0) terrainMask = ~0;

            for (int i = 0; i < attemptCount; i++)
            {
                // 인스펙터의 확률에 따라 스캔 여부 결정 (최적화)
                if (Random.value > corridorMobSpawnProbability * 10f) continue;

                // 청크의 가장 높은 곳에서 무작위로 X, Z 좌표를 골라 아래로 Ray를 쏩니다.
                Vector3 rayStart = chunkBase + new Vector3(
                    Random.Range(0f, chunkSize),
                    chunkSize + 2.0f,
                    Random.Range(0f, chunkSize)
                );

                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, chunkSize + 4.0f, terrainMask))
                {
                    // 땅이 충분히 평평한지 (기울기 0.7 이상)
                    if (hit.normal.y >= 0.7f)
                    {
                        // 몹이 낄 수 있는 좁은 천장이 아닌지 검사
                        if (HasHeadroom(hit.point))
                        {
                            // 기존 몹들과 10m 간격 유지 (뭉침 방지)
                            bool isTooClose = false;
                            foreach (var existing in spawnerList)
                            {
                                if (existing.spawnerType == 0 && Vector3.SqrMagnitude(existing.position - hit.point) < 100.0f)
                                {
                                    isTooClose = true;
                                    break;
                                }
                            }

                            if (!isTooClose)
                            {
                                var newSpawner = new SpawnerPointData
                                {
                                    position = hit.point,
                                    normal = hit.normal,
                                    spawnerType = 0,
                                    isCleared = false
                                };

                                spawnerList.Add(newSpawner);
                                editorAllSpawners.Add(newSpawner);
                                addedCount++;
                                Log($"[물리 탐색 성공] 🕷️ <b>일반 몹 스포너</b> 발견 위치: {hit.point}");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 해당 좌표 위로 플레이어나 몹이 설 수 있는 충분한 공간(Headroom)이 있는지 물리적 검사
        /// </summary>
        private bool HasHeadroom(Vector3 position)
        {
            Vector3 startPos = position + Vector3.up * 0.5f;
            float checkDistance = 1.5f; // 총 2.0m 확인 (0.5m 반경 캡슐 + 1.5m 위로)

            int terrainLayerMask = LayerMask.GetMask("Default", "Terrain");
            if (terrainLayerMask == 0) terrainLayerMask = ~0;

            if (Physics.SphereCast(startPos, 0.4f, Vector3.up, out RaycastHit hit, checkDistance, terrainLayerMask))
            {
                return false; // 천장에 닿음
            }

            return true; // 공간 확보됨
        }

        /// <summary>
        /// 특정 청크가 로드되거나 플레이어가 접근했을 때 저장된 데이터를 바탕으로 NetworkObject를 스폰합니다.
        /// </summary>
        public void TrySpawnEntitiesInChunk(Vector3Int chunkPos)
        {
            if (!IsServer || !chunkSpawnerDataMap.ContainsKey(chunkPos)) return;

            List<SpawnerPointData> spawnerList = chunkSpawnerDataMap[chunkPos];

            int spawnedCount = 0;

            for (int i = 0; i < spawnerList.Count; i++)
            {
                SpawnerPointData data = spawnerList[i];
                if (data.isCleared) continue;

                GameObject spawnedObj = null;

                switch (data.spawnerType)
                {
                    case 0: // 일반 통로/방 (일반 몹 스폰)
                        if (regularMobPrefabs != null && regularMobPrefabs.Length > 0)
                        {
                            GameObject selectedMob = regularMobPrefabs[Random.Range(0, regularMobPrefabs.Length)];
                            spawnedObj = Instantiate(selectedMob, data.position, Quaternion.identity);
                        }
                        break;
                    case 1: // 스폰 구역 (플레이어 시작점)
                        break;
                    case 2: // 보스 방 (트리거 스폰 및 추가 몹 스폰)
                        if (bossArenaTriggerPrefab)
                        {
                            spawnedObj = Instantiate(bossArenaTriggerPrefab, data.position, Quaternion.identity);
                        }

                        if (regularMobPrefabs != null && regularMobPrefabs.Length > 0 && Random.value < bossRoomMobSpawnProbability)
                        {
                            Vector3 mobPos = data.position + Vector3.right * 1.5f;
                            if (HasHeadroom(mobPos))
                            {
                                GameObject selectedMob = regularMobPrefabs[Random.Range(0, regularMobPrefabs.Length)];
                                GameObject mobObj = Instantiate(selectedMob, mobPos, Quaternion.identity);
                                NetworkObject mobNetObj = mobObj.GetComponent<NetworkObject>();
                                if (mobNetObj != null && !mobNetObj.IsSpawned) mobNetObj.Spawn();
                            }
                        }
                        break;
                    case 3: // 보물 방 (상자 스폰)
                        if (treasureChestPrefab)
                        {
                            Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, data.normal), data.normal);
                            spawnedObj = Instantiate(treasureChestPrefab, data.position, rot);
                        }
                        break;
                    case 99: // 희귀 광맥 실제 오브젝트 스폰
                        if (rareOrePrefab)
                        {
                            // 벽면의 노멀(기울기)에 맞춰서 자연스럽게 회전
                            Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, data.normal), data.normal);
                            spawnedObj = Instantiate(rareOrePrefab, data.position, rot);
                        }
                        break;
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
                    spawnedCount++;
                }
            }

            if (spawnedCount > 0)
                Log($"[스폰 완료] 청크 {chunkPos} 신규 네트워크 스폰: {spawnedCount}개");
        }

#if UNITY_EDITOR
        /// <summary>
        /// 디버깅을 위해 에디터 씬 뷰에 스포너 위치와 텍스트 라벨을 기즈모로 시각화합니다.
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!showGizmos || editorAllSpawners == null || editorAllSpawners.Count == 0) return;

            GUIStyle labelStyle = new GUIStyle();
            labelStyle.fontSize = 11;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.alignment = TextAnchor.MiddleCenter;

            Dictionary<int, int> typeCounters = new Dictionary<int, int>();

            for (int i = 0; i < editorAllSpawners.Count; i++)
            {
                var data = editorAllSpawners[i];

                if (!typeCounters.ContainsKey(data.spawnerType)) typeCounters[data.spawnerType] = 0;
                typeCounters[data.spawnerType]++;
                int currentCount = typeCounters[data.spawnerType];

                Color gizmoColor = Color.white;
                string typeName = "Unknown";
                float yOffset = 1.0f;

                switch (data.spawnerType)
                {
                    case 0: // 일반 통로/방 (하얀색 캡슐 형태 - 키높이 가시화)
                        gizmoColor = new Color(1f, 0.4f, 0.2f, 0.5f);
                        Gizmos.color = gizmoColor;
                        Gizmos.DrawSphere(data.position + Vector3.up * 1f, 0.4f);
                        Gizmos.DrawLine(data.position, data.position + Vector3.up * 2f);
                        typeName = "Mob Spawn";
                        yOffset = 2.2f;
                        break;
                    case 1: // 스폰 구역
                        gizmoColor = new Color(0f, 1f, 0f, 0.5f);
                        Gizmos.color = gizmoColor;
                        Gizmos.DrawCube(data.position + Vector3.up, new Vector3(1f, 2f, 1f));
                        typeName = "Player Spawn";
                        yOffset = 2.5f;
                        break;
                    case 2: // 보스 방
                        gizmoColor = new Color(1f, 0.2f, 0.2f, 0.7f);
                        Gizmos.color = gizmoColor;
                        Gizmos.DrawCube(data.position, Vector3.one * 1.5f);
                        typeName = "Boss Arena";
                        yOffset = 1.5f;
                        break;
                    case 3: // 보물 방
                        gizmoColor = Color.yellow;
                        Gizmos.color = gizmoColor;
                        Gizmos.DrawSphere(data.position, 0.6f);
                        typeName = "Treasure";
                        yOffset = 1.0f;
                        break;
                    case 4: // 싱크홀 구역
                        gizmoColor = new Color(0f, 1f, 1f, 0.6f);
                        Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
                        Vector3 top = data.position + Vector3.up * 15f;
                        Gizmos.DrawLine(top, data.position + new Vector3(2, 0, 2));
                        Gizmos.DrawLine(top, data.position + new Vector3(-2, 0, 2));
                        Gizmos.DrawLine(top, data.position + new Vector3(2, 0, -2));
                        Gizmos.DrawLine(top, data.position + new Vector3(-2, 0, -2));
                        Gizmos.color = gizmoColor;
                        Gizmos.DrawWireSphere(data.position, 2.8f);
                        typeName = "Sinkhole";
                        yOffset = 3.5f;
                        break;
                    case 99: // 희귀 광맥
                        gizmoColor = new Color(0f, 1f, 0.5f, 0.8f);
                        Gizmos.color = gizmoColor;
                        Gizmos.DrawSphere(data.position, 0.3f);
                        typeName = "Rare Ore";
                        yOffset = 0.6f;
                        break;
                }

                labelStyle.normal.textColor = gizmoColor;
                Handles.Label(data.position + Vector3.up * yOffset, $"{typeName} #{currentCount}", labelStyle);
            }
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// 에디터 인스펙터 커스텀: 각 자원/특이점 별로 카메라를 이동시키는 네비게이션 기능 제공
    /// </summary>
    [CustomEditor(typeof(CaveSpawnerManager))]
    public class CaveSpawnerManagerEditor : UnityEditor.Editor
    {
        private Dictionary<int, int> typeIndexMap = new Dictionary<int, int>();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            CaveSpawnerManager manager = (CaveSpawnerManager)target;

            if (manager.editorAllSpawners == null || manager.editorAllSpawners.Count == 0)
            {
                EditorGUILayout.HelpBox("아직 추출된 스포너 데이터가 없습니다. (지형을 구우면 나타납니다)", MessageType.Info);
                return;
            }

            GUILayout.Space(15);
            GUILayout.Label("🔍 씬 뷰 스포너 네비게이터", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Go 버튼을 누르면 해당 타입의 다음 오브젝트로 씬 카메라가 이동합니다. (3m 앞 클로즈업)", MessageType.None);

            var groupedSpawners = manager.editorAllSpawners.GroupBy(x => x.spawnerType).OrderBy(x => x.Key);

            foreach (var group in groupedSpawners)
            {
                int sType = group.Key;
                int count = group.Count();
                string typeName = GetTypeName(sType);

                if (!typeIndexMap.ContainsKey(sType))
                    typeIndexMap[sType] = 0;

                EditorGUILayout.BeginHorizontal("box");

                GUILayout.Label($"<b>{typeName}</b> ({count}개)", new GUIStyle(EditorStyles.label) { richText = true }, GUILayout.Width(150));
                GUILayout.Label($"[{typeIndexMap[sType] + 1}/{count}]", GUILayout.Width(60));

                if (GUILayout.Button("Go", GUILayout.Width(50), GUILayout.Height(20)))
                {
                    FocusOnNext(manager, sType, group.ToList());
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private string GetTypeName(int type)
        {
            switch (type)
            {
                case 0: return "일반 몹 스폰 (0)";
                case 1: return "플레이어 스폰 구역 (1)";
                case 2: return "보스 아레나 (2)";
                case 3: return "보물 상자 (3)";
                case 4: return "싱크홀/갓레이 (4)";
                case 99: return "희귀 광맥 (99)";
                default: return $"알 수 없음 ({type})";
            }
        }

        private void FocusOnNext(CaveSpawnerManager manager, int type, List<SpawnerPointData> list)
        {
            int currentIndex = typeIndexMap[type];
            SpawnerPointData targetData = list[currentIndex];

            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                Vector3 lookDir = (targetData.normal == Vector3.zero) ? Vector3.forward : targetData.normal;
                Vector3 offset = (lookDir * 3f) + (Vector3.up * 1.5f);

                view.LookAt(targetData.position + offset, Quaternion.LookRotation(-offset.normalized), 3f);
                view.Repaint();
            }

            typeIndexMap[type] = (currentIndex + 1) % list.Count;
            SceneView.RepaintAll();
        }
    }
#endif
}