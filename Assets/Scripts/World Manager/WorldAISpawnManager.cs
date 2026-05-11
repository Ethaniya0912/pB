// ─────────────────────────────────────────────────────────────────────
// WorldAISpawnManager (v2.5 — 개명 + Instance 오타 수정)
// ─────────────────────────────────────────────────────────────────────
// 갱신: 2026-05-07 v2.5
//   v2.4 → v2.5 변경 사항:
//     [개명] WorldAIManager → WorldAISpawnManager (Issues Report v7 §8)
//            동기: "WorldAIManager"는 World 전역 AI 매니저로 오해 가능.
//                  실제 책임은 NPC 스폰 라이프사이클(Instantiate + NetworkObject.Spawn
//                  + NavMesh 검증 + Agent 활성)로 한정 → 명칭 명료화.
//     [수정] public static Instant → Instance (오타 수정)
//            기존 코드 호출부도 함께 갱신 필요 (5개 파일).
//     [영향] HumanoidBootstrapper, Week2_T5_3_MobRegressionProbe,
//            Week2_T5_3_MobRegressionRunner, HumanoidVisualStageSetup 식별자 갱신.
//     [폐기] ScriptedEncounterTrigger.cs — 인지 시스템 테스트 도구로 작성됐으나
//            정식 사용 의도 없음. 디자이너 결정으로 폐기 (시나리오 영역으로 통합).
//     [보존] v2.4의 분산 강화 + NavMesh 검증 + 이벤트 발행 모든 동작.
//
// 갱신: 2026-05-04 v2.4 — 4 마리 뭉침 문제 해결
//   v2.3 → v2.4 변경 사항:
//     [수정] FALLBACK_SCATTER_RADIUS 1.5m → 5.0m (분산 반경 증가)
//     [추가] NAVMESH_SAMPLE_SCATTER_RADIUS 1.5m (snap 반경 분리)
//     [추가] _usedScatterPositions — 이미 사용한 좌표 회피 (0.5m 반경)
//     [수정] 분산 시도 횟수 5 → 8 (더 많은 후보)
//     [원인] v2.3 의 SamplePosition snap 반경 5m 가 너무 커서 분산 후보가 모두
//            baseCandidate 근처 같은 NavMesh 폴리곤에 수렴 → 4 마리 뭉침
//     [해결] 분산 반경 ↑ + snap 반경 ↓ + 사용 좌표 추적
//     [보존] v2.3 의 NavMesh 폴링 + 이벤트 발행 등 모든 동작
// ─────────────────────────────────────────────────────────────────────

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using CaveSystem;
using TDA.PB4.AI;        // [v2.5] GroupAIManager
using TDA.PB4.AI.Mob;    // [v2.5] MobAIBrain
using TDA.PB4.Interfaces; // [v2.5] ISpawnRequestReceiver
// IFactionGroupPolicy 구현체는 Reflection으로 자동 검색 — using 불필요.
// SpawnRequestSceneTool은 Editor assembly에 있어 메인에서 직접 import 불가.
// ContextMenu 메서드에서 fully qualified name으로 호출.

// =============================================================================
// [v2.5 §11.5] SpawnRequest DTO + Response — WorldAISpawnManager 동반
//
// 통합 의도: 시나리오 → AI 스폰 명령 DTO. WorldAISpawnManager의 핵심 입력이므로
//           단독 파일이 아닌 같은 파일에 통합. 인터페이스(ISpawnRequestReceiver)와
//           구현체(WorldAISpawnManager) 모두 본 DTO를 참조.
//
// Coding Standards 준수 사항:
//   §6.1 IdentifiableScriptableObject — N/A (DTO는 SO가 아니라 Pure C# class)
//   §8.2 매직 문자열 금지 — reason은 SpawnReason enum, locationStrategy도 enum
//   §9.6 ISavable — 향후 Wk7+ 부활 시스템 도입 시 구현 예정
//
// 위반 박제:
//   - factionId: string — coding_standards §8.2 위반 가능. MobFactionDataSO도 string이라
//                          프로젝트 일관성 차원 유지. Wk5 펙션 시스템 합의 시 재검토.
//   - biomeAffinityHint, persistentGroupId: string — Wk5/Wk7+ 본체 시 enum/SO ID 전환 예정.
// =============================================================================

namespace TDA.PB4.AI
{
    [Serializable]
    public class SpawnRequest
    {
        // ── 식별 ──────────────────────────────────────────────
        [NonSerialized] public Guid requestId = Guid.NewGuid();

        [Tooltip("이 스폰 요청의 사유. coding_standards §8.2 매직 문자열 회피 위해 enum 사용.")]
        public SpawnReason reason = SpawnReason.Manual;

        [NonSerialized] public DateTime requestedAt = DateTime.Now;

        // ── 펙션/정책 ─────────────────────────────────────────
        [Tooltip("MobFactionDataSO.factionId 와 일치해야 함. " +
                 "WorldAISpawnManager.factionPrefabMappings 매칭 키.")]
        public string factionId = "Skeleton";

        // ── 위치 결정 ─────────────────────────────────────────
        [Tooltip("위치 결정 전략. Enums.cs의 SpawnLocationStrategy 참조.")]
        public SpawnLocationStrategy locationStrategy = SpawnLocationStrategy.Explicit;

        [Tooltip("Strategy=Explicit일 때 사용. hasExplicitPosition=true여야 적용.")]
        public Vector3 explicitPosition = Vector3.zero;

        [Tooltip("explicitPosition 설정 여부. Vector3.zero와 (0,0,0) 좌표 구분용.")]
        public bool hasExplicitPosition = false;

        [Tooltip("Strategy=NearestSpawnNode 시 사용. -1=SpawnManager 위치 기준 자동.")]
        public int explicitCaveNodeIdx = -1;

        [Tooltip("Strategy=BiomeAffinityResolver 시 IBiomeSpawnResolver 호출 힌트. " +
                 "Wk5+ 지형팀 합의 후 본체 동작.")]
        public string biomeAffinityHint = "";

        // ── 분대 구성 ─────────────────────────────────────────
        [Range(1, 12)]
        [Tooltip("스폰할 멤버 수. min/maxMemberCount가 모두 0이면 이 값 고정 사용.")]
        public int memberCount = 3;

        [Tooltip("가변 멤버 수 최소값. 0이면 가변 비활성.")]
        public int minMemberCount = 0;

        [Tooltip("가변 멤버 수 최대값. 0이면 가변 비활성.")]
        public int maxMemberCount = 0;

        // ── 부활 (Wk7+) ──────────────────────────────────────
        [Tooltip("Wk7+ 부활 시스템용. 기존 그룹 persistent ID 지정 시 부활. " +
                 "Wk3~Wk6에서는 무시.")]
        public string persistentGroupId = "";

        // ── 가중치 ────────────────────────────────────────────
        [Range(0f, 1f)]
        [Tooltip("시나리오 우선순위. 큐에서 정렬 기준.")]
        public float priority = 0.5f;

        [NonSerialized]
        public Dictionary<string, float> modifiers = new();

        // ── 타임아웃 ──────────────────────────────────────────
        [Tooltip("이 시점(Time.time 기준)까지 처리 못 하면 폐기. null=무제한.")]
        [NonSerialized]
        public float? executeBeforeTime = null;

        // ── 가변 멤버 수 결정 ─────────────────────────────────
        public int ResolveMemberCount()
        {
            if (minMemberCount > 0 && maxMemberCount > 0 && maxMemberCount >= minMemberCount)
                return UnityEngine.Random.Range(minMemberCount, maxMemberCount + 1);
            return Mathf.Clamp(memberCount, 1, 12);
        }

        public float GetModifier(string key, float defaultValue = 0f)
        {
            return modifiers != null && modifiers.TryGetValue(key, out var v) ? v : defaultValue;
        }

        public override string ToString()
        {
            int count = ResolveMemberCount();
            string posStr = hasExplicitPosition ? explicitPosition.ToString("F1") :
                            (explicitCaveNodeIdx >= 0 ? $"node#{explicitCaveNodeIdx}" : "auto");
            return $"SpawnRequest[{factionId} ×{count} {locationStrategy} @{posStr} " +
                   $"prio={priority:F2} reason={reason}]";
        }
    }

    public class SpawnRequestResponse
    {
        public Guid requestId;
        public SpawnResult result;
        public string groupId = "";
        public string failReason = "";
        public List<string> spawnedMemberIds = new();
        public Vector3 actualSpawnPosition;
        public bool isCompleted = false;

        public override string ToString()
        {
            return $"SpawnResponse[{result} groupId={groupId} members={spawnedMemberIds.Count}" +
                   (string.IsNullOrEmpty(failReason) ? "" : $" reason={failReason}") + "]";
        }
    }
}

// =============================================================================
// WorldAISpawnManager 본체 (글로벌 namespace)
// =============================================================================

public class WorldAISpawnManager : MonoBehaviour, ISpawnRequestReceiver
{
    public static WorldAISpawnManager Instance { get; private set; }

    // ═════════════════════════════════════════════════════════════
    // ★ v2.2 — 이벤트 (HumanoidBootstrapper 등 외부 매니저가 구독)
    // ═════════════════════════════════════════════════════════════
    /// <summary>
    /// 한 NPC 스폰 직후 발행. 인스턴스 GameObject 전달.
    /// </summary>
    public static event Action<GameObject> OnCharacterSpawned;

    /// <summary>
    /// 모든 NPC 스폰 사이클 종료 후 발행. 스폰된 인스턴스 리스트 전달.
    /// HumanoidBootstrapper 가 이 시점에 RescanAndBootstrapNew 호출.
    /// </summary>
    public static event Action<List<GameObject>> OnAllCharactersSpawned;

    // ═════════════════════════════════════════════════════════════
    // 매직 넘버 const
    // ═════════════════════════════════════════════════════════════
    private const string TARGET_SCENE_NAME = "Scene_World_01";
    private const float NAVMESH_SAMPLE_RADIUS = 5.0f;       // 일반 SamplePosition 반경
    private const float NAVMESH_SAMPLE_SCATTER_RADIUS = 1.5f; // ★ v2.4 — 분산 후보 snap 반경 (작게)
    private const float POST_SPAWN_DELAY_SEC = 0.1f;
    private const float FALLBACK_SCATTER_RADIUS = 5.0f;     // ★ v2.4 — 1.5 → 5.0 (분산 반경 증가)
    private const float USED_POSITION_AVOID_RADIUS = 0.5f;  // ★ v2.4 — 사용된 좌표 회피 반경
    private const int SCATTER_MAX_ATTEMPTS = 8;           // ★ v2.4 — 5 → 8 (더 많은 시도)

    [Header("Debug")]
    [SerializeField] bool despawnCharacters = false;
    [SerializeField] bool respawnCharacters = false;

    [Header("Characters")]
    [Tooltip("스폰할 AI 캐릭터 프리팹 배열")]
    [SerializeField] GameObject[] aiCharacters;

    [Header("Spawn Points (★ v2 — Tier 2 Graph)")]
    [Tooltip("ON → Tier 2 그래프 노드 위치를 spawn point 후보로 활용. " +
             "OFF → 원본 프리팹 위치 그대로 스폰 (v1 동작)")]
    [SerializeField] bool useGraphNodeSpawnPoints = true;

    [Tooltip("명시적으로 사용할 노드 idx 목록. 비어있으면 nodesData 중 자동 선택. " +
             "★ 주의: 그래프는 월드 시드 의존 → 매 시드마다 같은 idx 가 다른 NodeRole 일 수 있음. " +
             "테스트 시 BlackboardUpdater 의 LogCurrentSnapshot 으로 idx 의미 확인 후 사용 권장. " +
             "검증된 의미 있는 idx 예시 (현재 시드): " +
             "[0]=Spawn(IDENTITY 0x11), [2]=Treasure(IDENTITY 0x04), [5]=Normal/Hub(IDENTITY 0x10)")]
    [SerializeField] int[] testSpawnNodeIndices;

    [Tooltip("ON → 스폰 후 NavMeshAgent 자동 활성화 + Warp (NavMesh 위 좌표일 때만). " +
             "OFF → Agent 비활성 상태 유지 (v1 동작)")]
    [SerializeField] bool autoEnableNavMeshAgent = true;

    [SerializeField] List<GameObject> spawnedInCharacters = new List<GameObject>();

    private bool _hasSpawnedOnce = false;

    // ★ v2.4 — 이번 스폰 사이클에서 사용한 분산 좌표 추적 (뭉침 방지)
    private List<Vector3> _usedScatterPositions = new List<Vector3>();

    // ═════════════════════════════════════════════════════════════
    // Unity 라이프사이클
    // ═════════════════════════════════════════════════════════════
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StartCoroutine(WaitForServerAndSceneRoutine());
    }

    private void Update()
    {
        if (respawnCharacters)
        {
            respawnCharacters = false;
            StartCoroutine(SpawnAllCharactersRoutine());
        }
        if (despawnCharacters)
        {
            despawnCharacters = false;
            DespawnAllCharacters();
        }
    }

    // ═════════════════════════════════════════════════════════════
    // 호스트 + 본 씬 진입 + ★ v2.3 NavMesh 대기 코루틴
    // ═════════════════════════════════════════════════════════════
    private IEnumerator WaitForServerAndSceneRoutine()
    {
        while (NetworkManager.Singleton == null) yield return null;
        while (!NetworkManager.Singleton.IsServer) yield return null;
        while (SceneManager.GetActiveScene().name != TARGET_SCENE_NAME) yield return null;

        // ★ v2.3 신규 — NavMesh 베이크 완료까지 폴링
        //   원인: v2.2 의 0.5초 대기는 NavMesh 미베이크 상태에서 스폰 시도 → 분산 폴백 fail
        //   해결: NavMesh.SamplePosition 으로 베이크 완료 감지 후 스폰
        yield return WaitForNavMeshReady(timeout: 30f);

        yield return new WaitForSeconds(0.5f);  // 안전 마진

        if (!_hasSpawnedOnce)
        {
            _hasSpawnedOnce = true;
            yield return SpawnAllCharactersRoutine();
        }
    }

    /// <summary>
    /// ★ v2.3 신규 — NavMesh 베이크 완료까지 폴링.
    /// 그래프 노드 또는 prefab 원본 위치 중 어느 하나가 NavMesh 위에 있으면 완료로 간주.
    /// 0.5초마다 폴링. timeout 초 도달 시 강제 진행 (분산 폴백 fail 가능성 경고).
    /// </summary>
    private IEnumerator WaitForNavMeshReady(float timeout)
    {
        float startTime = Time.time;
        int pollCount = 0;

        Debug.Log($"[WorldAISpawnManager] NavMesh 베이크 완료 대기 시작 — timeout {timeout}초");

        while (Time.time - startTime < timeout)
        {
            pollCount++;
            bool foundNavMesh = false;

            // 시도 1: 그래프 노드 위치 (앞 5 개만 검사 — 부담 최소화)
            var graph = CaveNodeGraphBuilder.Instance;
            if (graph != null && graph.nodesData != null && graph.nodesData.Count > 0)
            {
                int checkCount = Mathf.Min(graph.nodesData.Count, 5);
                for (int i = 0; i < checkCount; i++)
                {
                    if (NavMesh.SamplePosition(graph.nodesData[i].position,
                                                out _, NAVMESH_SAMPLE_RADIUS, NavMesh.AllAreas))
                    {
                        foundNavMesh = true;
                        break;
                    }
                }
            }

            // 시도 2: aiCharacters 의 prefab 원본 위치 (그래프 폴백)
            if (!foundNavMesh && aiCharacters != null && aiCharacters.Length > 0
                && aiCharacters[0] != null)
            {
                if (NavMesh.SamplePosition(aiCharacters[0].transform.position,
                                            out _, NAVMESH_SAMPLE_RADIUS, NavMesh.AllAreas))
                {
                    foundNavMesh = true;
                }
            }

            if (foundNavMesh)
            {
                Debug.Log($"[WorldAISpawnManager] ✓ NavMesh 베이크 완료 감지 — " +
                          $"{Time.time - startTime:F1}초 ({pollCount}회 폴링)");
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }

        Debug.LogWarning($"[WorldAISpawnManager] NavMesh 폴링 타임아웃 ({timeout}초) — " +
                         $"강제 진행. 분산 폴백이 작동 안 할 수 있음. " +
                         $"수동 Respawn 권장.");
    }

    // ═════════════════════════════════════════════════════════════
    // 스폰 코루틴 (v2.2 — 이벤트 발행 추가)
    // ═════════════════════════════════════════════════════════════
    private IEnumerator SpawnAllCharactersRoutine()
    {
        if (aiCharacters == null || aiCharacters.Length == 0)
        {
            Debug.LogWarning("[WorldAISpawnManager] aiCharacters 배열 비어있음 — 스폰 스킵");
            yield break;
        }

        // ★ v2.4 — 이번 스폰 사이클의 사용 좌표 추적 초기화
        _usedScatterPositions.Clear();

        List<Vector3> spawnPoints = ResolveSpawnPoints();

        Debug.Log($"[WorldAISpawnManager] 스폰 시작 — {aiCharacters.Length}개 prefab × " +
                  $"{spawnPoints.Count}개 spawn point");

        var newlySpawned = new List<GameObject>();

        for (int i = 0; i < aiCharacters.Length; i++)
        {
            var prefab = aiCharacters[i];
            if (prefab == null) continue;

            Vector3? targetPos = null;
            if (spawnPoints.Count > 0)
            {
                int spawnIdx = i % spawnPoints.Count;
                targetPos = spawnPoints[spawnIdx];
            }

            yield return SpawnOneCharacterRoutine(prefab, targetPos, newlySpawned);
        }

        Debug.Log($"[WorldAISpawnManager] 스폰 완료 — spawnedInCharacters.Count={spawnedInCharacters.Count}");

        // ★ v2.2 — 모든 NPC 스폰 종료 이벤트 발행
        if (newlySpawned.Count > 0)
        {
            try { OnAllCharactersSpawned?.Invoke(newlySpawned); }
            catch (Exception ex) { Debug.LogError($"[WorldAISpawnManager] OnAllCharactersSpawned 핸들러 에러: {ex}"); }
        }
    }

    // ═════════════════════════════════════════════════════════════
    // ★ v2.2 — 분산 폴백 + 이벤트 발행
    // ═════════════════════════════════════════════════════════════
    private IEnumerator SpawnOneCharacterRoutine(GameObject prefab, Vector3? requestedPos,
                                                  List<GameObject> newlySpawnedAccumulator)
    {
        // ── 이중 폴백으로 NavMesh 위 좌표 결정 ─────────────────
        Vector3 spawnPos;
        bool isOnNavMesh;

        // [1] 요청 좌표 (Tier 2 노드) 먼저 시도
        if (requestedPos.HasValue &&
            TryFindNavMeshPosition(requestedPos.Value, out spawnPos))
        {
            isOnNavMesh = true;
            Debug.Log($"[WorldAISpawnManager] ✓ Tier 2 노드 좌표 NavMesh 매칭: " +
                      $"요청={requestedPos.Value} → 매칭={spawnPos}");
        }
        // [2] 요청 좌표 fail → ★ v2.2 — prefab 원본 + 랜덤 분산 시도
        else if (TryFindScatteredNavMeshPosition(prefab.transform.position, out spawnPos))
        {
            isOnNavMesh = true;
            Debug.LogWarning($"[WorldAISpawnManager] 요청 좌표 NavMesh 외부 — " +
                             $"prefab 원본 분산 폴백: {spawnPos}");
        }
        // [3] 모두 fail → 원본 좌표 그대로 (Agent 비활성)
        else
        {
            spawnPos = prefab.transform.position;
            isOnNavMesh = false;
            Debug.LogError($"[WorldAISpawnManager] {prefab.name} — " +
                           $"NavMesh 검증 실패 (요청+원본 모두 외부). " +
                           $"Agent 활성화 스킵. NPC 동작 제한됨.");
        }

        // ── Instantiate + Spawn ──────────────────────────────
        GameObject instance = Instantiate(prefab, spawnPos, Quaternion.identity);

        var netObj = instance.GetComponent<NetworkObject>();
        if (netObj != null) netObj.Spawn();
        else Debug.LogWarning($"[WorldAISpawnManager] {prefab.name} 에 NetworkObject 없음");

        spawnedInCharacters.Add(instance);
        newlySpawnedAccumulator.Add(instance);

        // ── 한 프레임 대기 (Awake / OnNetworkSpawn 처리) ────
        yield return new WaitForSeconds(POST_SPAWN_DELAY_SEC);

        // ── NavMesh 위에서만 Agent 활성화 ────────────
        if (autoEnableNavMeshAgent && isOnNavMesh)
        {
            var agent = instance.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.enabled = true;
                agent.Warp(spawnPos);

                Debug.Log($"[WorldAISpawnManager] {instance.name} 스폰 ✓ " +
                          $"pos={spawnPos} agent=True isOnNavMesh=True");
            }
            else
            {
                Debug.LogWarning($"[WorldAISpawnManager] {instance.name} 에 " +
                                 $"NavMeshAgent 컴포넌트 없음");
            }
        }
        else if (autoEnableNavMeshAgent && !isOnNavMesh)
        {
            Debug.LogWarning($"[WorldAISpawnManager] {instance.name} NavMesh 외부 → " +
                             $"Agent 활성화 스킵. isOnNavMesh=False, pos={spawnPos}");
        }

        // ★ v2.2 — 개별 스폰 이벤트 발행
        try { OnCharacterSpawned?.Invoke(instance); }
        catch (Exception ex) { Debug.LogError($"[WorldAISpawnManager] OnCharacterSpawned 핸들러 에러: {ex}"); }
    }

    // ═════════════════════════════════════════════════════════════
    // NavMesh 검증 헬퍼
    // ═════════════════════════════════════════════════════════════
    /// <summary>
    /// 후보 좌표 주변 NAVMESH_SAMPLE_RADIUS 반경에서 NavMesh 위 좌표 검색.
    /// </summary>
    private bool TryFindNavMeshPosition(Vector3 candidate, out Vector3 result)
    {
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit,
                                   NAVMESH_SAMPLE_RADIUS, NavMesh.AllAreas))
        {
            result = hit.position;
            return true;
        }
        result = Vector3.zero;
        return false;
    }

    /// <summary>
    /// ★ v2.4 강화 — 분산 후보 좌표 + NavMesh 검증 + 사용 좌표 회피.
    ///   - 분산 반경: FALLBACK_SCATTER_RADIUS (5.0m) — 후보 좌표 흩뿌림 범위
    ///   - snap 반경: NAVMESH_SAMPLE_SCATTER_RADIUS (1.5m) — 가까운 NavMesh 만 매칭 (수렴 방지)
    ///   - 사용 좌표 회피: 이번 스폰 사이클의 _usedScatterPositions 와 0.5m 반경 회피
    ///   - 시도 횟수: SCATTER_MAX_ATTEMPTS (8)
    /// 시도 모두 fail 시 baseCandidate 자체를 반환 (예외 케이스).
    /// </summary>
    private bool TryFindScatteredNavMeshPosition(Vector3 baseCandidate, out Vector3 result)
    {
        // 시도 1: 정확한 baseCandidate 가 NavMesh 위인지 확인
        if (!NavMesh.SamplePosition(baseCandidate, out NavMeshHit baseHit,
                                     NAVMESH_SAMPLE_RADIUS, NavMesh.AllAreas))
        {
            result = Vector3.zero;
            return false;
        }

        // 시도 2~N: 분산 시도 (사용 좌표 회피)
        for (int attempt = 0; attempt < SCATTER_MAX_ATTEMPTS; attempt++)
        {
            // 분산 반경 (5m) 의 랜덤 좌표
            Vector3 randomOffset = new Vector3(
                UnityEngine.Random.Range(-FALLBACK_SCATTER_RADIUS, FALLBACK_SCATTER_RADIUS), 0,
                UnityEngine.Random.Range(-FALLBACK_SCATTER_RADIUS, FALLBACK_SCATTER_RADIUS));
            Vector3 scattered = baseCandidate + randomOffset;

            // snap 반경 (1.5m) 작게 — 가까운 NavMesh 만 매칭, 수렴 방지
            if (NavMesh.SamplePosition(scattered, out NavMeshHit scatterHit,
                                        NAVMESH_SAMPLE_SCATTER_RADIUS, NavMesh.AllAreas))
            {
                Vector3 candidate = scatterHit.position;

                // 이미 사용한 좌표와 0.5m 이내면 회피
                if (!IsTooCloseToUsedPosition(candidate))
                {
                    _usedScatterPositions.Add(candidate);
                    result = candidate;
                    return true;
                }
            }
        }

        // 모든 시도 fail → baseHit 사용 (마지막 폴백, 뭉칠 가능성 있음)
        Debug.LogWarning($"[WorldAISpawnManager] 분산 시도 {SCATTER_MAX_ATTEMPTS}회 모두 fail — " +
                         $"baseCandidate 폴백. 뭉침 가능성 있음.");
        _usedScatterPositions.Add(baseHit.position);
        result = baseHit.position;
        return true;
    }

    /// <summary>
    /// ★ v2.4 신규 — 후보 좌표가 이미 사용한 좌표와 USED_POSITION_AVOID_RADIUS (0.5m) 이내인가?
    /// </summary>
    private bool IsTooCloseToUsedPosition(Vector3 candidate)
    {
        foreach (var used in _usedScatterPositions)
        {
            if (Vector3.Distance(candidate, used) < USED_POSITION_AVOID_RADIUS)
                return true;
        }
        return false;
    }

    /// <summary>
    /// spawn point 후보 결정 (v2 보존).
    /// </summary>
    private List<Vector3> ResolveSpawnPoints()
    {
        var result = new List<Vector3>();

        if (!useGraphNodeSpawnPoints) return result;

        var graph = CaveNodeGraphBuilder.Instance;
        if (graph == null || graph.nodesData == null || graph.nodesData.Count == 0)
        {
            Debug.LogWarning("[WorldAISpawnManager] CaveNodeGraphBuilder 또는 nodesData 부재 — " +
                             "원본 위치 폴백");
            return result;
        }

        if (testSpawnNodeIndices != null && testSpawnNodeIndices.Length > 0)
        {
            foreach (int idx in testSpawnNodeIndices)
            {
                if (idx >= 0 && idx < graph.nodesData.Count)
                {
                    result.Add(graph.nodesData[idx].position);
                }
                else
                {
                    Debug.LogWarning($"[WorldAISpawnManager] testSpawnNodeIndices 의 {idx} 가 " +
                                     $"nodesData 범위(0~{graph.nodesData.Count - 1}) 벗어남 — 스킵");
                }
            }
            Debug.Log($"[WorldAISpawnManager] testSpawnNodeIndices 사용 → {result.Count}개 spawn point");
        }
        else
        {
            int count = Mathf.Min(aiCharacters?.Length ?? 0, graph.nodesData.Count);
            for (int i = 0; i < count; i++)
            {
                result.Add(graph.nodesData[i].position);
            }
            Debug.Log($"[WorldAISpawnManager] 자동 선택 → 첫 {count}개 노드 spawn point");
        }

        return result;
    }

    // ═════════════════════════════════════════════════════════════
    // 디스폰 (v1 보존)
    // ═════════════════════════════════════════════════════════════
    private void DespawnAllCharacters()
    {
        foreach (var character in spawnedInCharacters)
        {
            if (character == null) continue;
            var netObj = character.GetComponent<NetworkObject>();
            if (netObj != null) netObj.Despawn();
        }
        spawnedInCharacters.Clear();
    }

    private void DisableAllCharacters()
    {
        // 게임오브젝트를 비활성화하기 위해서, 네트워크에 비활성화상태를 싱크.
        // 만약 비활성화상태가 참일시, 클라이언트가 접속시 게임오브젝트를 비활성화
        // 먼 거리의 오브젝트를 메모리 절약을 위해 비활성화 할 수 있음.
        // 캐릭터가 지역마다 나뉠 수 있음 등(area_01, area_02).
    }

    // ═════════════════════════════════════════════════════════════════════
    // [v2.5 §11.5] ISpawnRequestReceiver — 정식 구현
    //
    // 동기:    ExecuteSpawnRequest = 즉시 응답 + Coroutine 백그라운드
    // 비동기:  QueueRequest = 큐 적재 + 순차 처리 + OnSpawnRequestCompleted 이벤트
    //
    // 동시성:  CanAcceptRequest로 사전 검증 (NavMesh / 한도 / prefab)
    // 한도:    maxConcurrentGroupsPerFaction / maxConcurrentGroupsTotal 강제
    // ═════════════════════════════════════════════════════════════════════

    [Serializable]
    public class FactionPrefabMapping
    {
        [Tooltip("매칭할 펙션 ID (예: Skeleton, Goblin)")]
        public string factionId;
        [Tooltip("이 펙션에서 스폰할 prefab")]
        public GameObject prefab;
        [Tooltip("기본 멤버 수 (SpawnRequest.memberCount 미지정 시)")]
        [Range(1, 12)] public int defaultMemberCount = 3;

        [Tooltip("[v2.5] 자동 생성된 그룹에 할당할 FactionGroupPolicySO. " +
                 "비워두면 GroupAIManager의 사기 시스템 비활성. " +
                 "예: PB4_PhalanxGroupPolicy(Skeleton_Policy)")]
        public TDA.PB4.Data.FactionGroupPolicySO policySO;

        [Tooltip("[v2.5] 자동 생성된 그룹 GameObject에 AddComponent할 정책 구현체. " +
                 "인스펙터 드롭다운으로 IFactionGroupPolicy 모든 구현체 자동 검색. " +
                 "비워두면 (None) 사기 시스템 비활성. " +
                 "새 구현체 추가 시 자동 발견 — 코드 수정 불필요.")]
        [PolicyImplementorPicker]
        public string policyImplementorTypeName = "";
    }

    [Header("━━━ ★ Wk3 SpawnRequest 매핑 ━━━━━━━━━━━━")]
    [Tooltip("[v2.5] SpawnRequest.factionId → 실제 prefab 매핑. " +
             "Wk5+ FactionDataSO 도입 시 대체 예정.")]
    public List<FactionPrefabMapping> factionPrefabMappings = new();

    [Tooltip("[v2.5] 스폰 후 그룹 자동 결성 ON. " +
             "factionId 기반 'Group_<faction>_<seq>' GameObject 생성 + RegisterMember.")]
    public bool autoFormGroupOnSpawn = true;

    [Tooltip("[v2.5] 펙션별 동시 활성 그룹 수 상한. 0=무제한.")]
    [Range(0, 20)] public int maxConcurrentGroupsPerFaction = 5;

    [Tooltip("[v2.5] 씬 전체 동시 활성 그룹 수 상한. 0=무제한.")]
    [Range(0, 50)] public int maxConcurrentGroupsTotal = 12;

    [Tooltip("[v2.5] 큐의 최대 적재량.")]
    [Range(1, 32)] public int maxQueuedRequests = 8;

    [Tooltip("[v2.5] 그룹 식별자 자동 증가 카운터 (런타임).")]
    [SerializeField] private int groupSequenceCounter = 0;

    // ── 큐 시스템 ────────────────────────────────────────────
    private readonly List<SpawnRequest> _queue = new();
    private bool _processingQueue = false;

    // ── ISpawnRequestReceiver 이벤트 ─────────────────────────
    public event Action<SpawnRequestResponse> OnSpawnRequestCompleted;

    // ─────────────────────────────────────────────────────────
    // ISpawnRequestReceiver 구현
    // ─────────────────────────────────────────────────────────

    public bool CanAcceptRequest(SpawnRequest req, out string failReason)
    {
        failReason = "";
        if (req == null) { failReason = "request null"; return false; }

        // 1. Prefab 매핑
        var mapping = factionPrefabMappings.Find(m => m.factionId == req.factionId);
        if (mapping == null || mapping.prefab == null)
        {
            failReason = $"factionId='{req.factionId}' 매핑 없음";
            return false;
        }

        // 2. 타임아웃
        if (req.executeBeforeTime.HasValue && Time.time > req.executeBeforeTime.Value)
        {
            failReason = $"timeout (executeBeforeTime={req.executeBeforeTime:F1}s, now={Time.time:F1}s)";
            return false;
        }

        // 3. 동시 그룹 한도
        if (autoFormGroupOnSpawn)
        {
            int totalGroups = UnityEngine.Object.FindObjectsByType<GroupAIManager>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            if (maxConcurrentGroupsTotal > 0 && totalGroups >= maxConcurrentGroupsTotal)
            {
                failReason = $"max concurrent groups total ({maxConcurrentGroupsTotal})";
                return false;
            }
            if (maxConcurrentGroupsPerFaction > 0)
            {
                int factionGroups = CountGroupsByFaction(req.factionId);
                if (factionGroups >= maxConcurrentGroupsPerFaction)
                {
                    failReason = $"max concurrent groups per faction '{req.factionId}' ({maxConcurrentGroupsPerFaction})";
                    return false;
                }
            }
        }

        // 4. 위치 검증 (Explicit/ScenarioDirected만 사전 검증)
        if (req.locationStrategy == SpawnLocationStrategy.Explicit ||
            req.locationStrategy == SpawnLocationStrategy.ScenarioDirected)
        {
            if (req.hasExplicitPosition)
            {
                if (!TryFindNavMeshPosition(req.explicitPosition, out _))
                {
                    failReason = $"NavMesh 외부 ({req.explicitPosition})";
                    return false;
                }
            }
        }

        return true;
    }

    public SpawnRequestResponse ExecuteSpawnRequest(SpawnRequest req)
    {
        var response = new SpawnRequestResponse { requestId = req?.requestId ?? Guid.Empty };

        if (!CanAcceptRequest(req, out var failReason))
        {
            response.result = ResolveFailReason(failReason);
            response.failReason = failReason;
            response.isCompleted = true;
            Debug.LogWarning($"[WorldAISpawnManager] ExecuteSpawnRequest 거절: {failReason}");
            OnSpawnRequestCompleted?.Invoke(response);
            return response;
        }

        Vector3? requestedPos = ResolveSpawnPosition(req);
        int count = req.ResolveMemberCount();
        var mapping = factionPrefabMappings.Find(m => m.factionId == req.factionId);

        var grp = autoFormGroupOnSpawn ? FindOrCreateGroupForFaction(req.factionId) : null;
        response.groupId = grp != null ? grp.gameObject.name : "";
        if (requestedPos.HasValue) response.actualSpawnPosition = requestedPos.Value;

        StartCoroutine(ExecuteSpawnRequestRoutine(mapping.prefab, requestedPos, count, grp, response, req));

        Debug.Log($"<color=#88FF88>[WorldAISpawnManager]</color> ExecuteSpawnRequest 시작: {req}");
        return response;
    }

    public void QueueRequest(SpawnRequest req)
    {
        if (req == null) return;

        if (_queue.Count >= maxQueuedRequests)
        {
            Debug.LogWarning($"[WorldAISpawnManager] 큐 한도 초과 ({maxQueuedRequests}) — 폐기: {req}");
            var resp = new SpawnRequestResponse
            {
                requestId = req.requestId,
                result = SpawnResult.FailedConcurrentLimit,
                failReason = $"queue full ({maxQueuedRequests})",
                isCompleted = true,
            };
            OnSpawnRequestCompleted?.Invoke(resp);
            return;
        }

        _queue.Add(req);
        _queue.Sort((a, b) => b.priority.CompareTo(a.priority));
        Debug.Log($"[WorldAISpawnManager] QueueRequest 적재: {req} (큐={_queue.Count}/{maxQueuedRequests})");

        if (!_processingQueue) StartCoroutine(ProcessQueueRoutine());
    }

    public bool CancelRequest(Guid requestId)
    {
        for (int i = 0; i < _queue.Count; i++)
        {
            if (_queue[i].requestId == requestId)
            {
                var req = _queue[i];
                _queue.RemoveAt(i);
                Debug.Log($"[WorldAISpawnManager] CancelRequest: {req}");
                return true;
            }
        }
        return false;
    }

    public int GetQueuedCount() => _queue.Count;

    // ─────────────────────────────────────────────────────────
    // 내부 헬퍼
    // ─────────────────────────────────────────────────────────

    private IEnumerator ProcessQueueRoutine()
    {
        _processingQueue = true;

        while (_queue.Count > 0)
        {
            var req = _queue[0];
            _queue.RemoveAt(0);

            if (req.executeBeforeTime.HasValue && Time.time > req.executeBeforeTime.Value)
            {
                var resp = new SpawnRequestResponse
                {
                    requestId = req.requestId,
                    result = SpawnResult.FailedTimeout,
                    failReason = "executeBeforeTime expired",
                    isCompleted = true,
                };
                OnSpawnRequestCompleted?.Invoke(resp);
                continue;
            }

            var execResp = ExecuteSpawnRequest(req);
            yield return new WaitUntil(() => execResp.isCompleted);
            yield return null;
        }

        _processingQueue = false;
    }

    private Vector3? ResolveSpawnPosition(SpawnRequest req)
    {
        switch (req.locationStrategy)
        {
            case SpawnLocationStrategy.Explicit:
            case SpawnLocationStrategy.ScenarioDirected:
                return req.hasExplicitPosition ? (Vector3?)req.explicitPosition : null;

            case SpawnLocationStrategy.NearestSpawnNode:
                return ResolveNearestSpawnNode(req);

            case SpawnLocationStrategy.BiomeAffinityResolver:
                Debug.LogWarning("[WorldAISpawnManager] BiomeAffinityResolver는 Wk5+ stub. NearestSpawnNode로 fallback.");
                return ResolveNearestSpawnNode(req);

            case SpawnLocationStrategy.FactionTerritory:
                Debug.LogWarning("[WorldAISpawnManager] FactionTerritory는 Wk5+ stub. NearestSpawnNode로 fallback.");
                return ResolveNearestSpawnNode(req);

            default:
                return null;
        }
    }

    private Vector3? ResolveNearestSpawnNode(SpawnRequest req)
    {
        var graph = CaveNodeGraphBuilder.Instance;
        if (graph == null || graph.nodesData == null || graph.nodesData.Count == 0)
        {
            Debug.LogWarning("[WorldAISpawnManager] CaveNodeGraphBuilder 부재 — 위치 결정 실패");
            return null;
        }

        if (req.explicitCaveNodeIdx >= 0 && req.explicitCaveNodeIdx < graph.nodesData.Count)
            return graph.nodesData[req.explicitCaveNodeIdx].position;

        Vector3 origin = req.hasExplicitPosition ? req.explicitPosition : transform.position;
        int bestIdx = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < graph.nodesData.Count; i++)
        {
            float d = Vector3.Distance(origin, graph.nodesData[i].position);
            if (d < bestDist) { bestDist = d; bestIdx = i; }
        }
        return bestIdx >= 0 ? (Vector3?)graph.nodesData[bestIdx].position : null;
    }

    private SpawnResult ResolveFailReason(string failReason)
    {
        if (failReason.Contains("매핑 없음")) return SpawnResult.FailedInvalidPrefab;
        if (failReason.Contains("timeout")) return SpawnResult.FailedTimeout;
        if (failReason.Contains("NavMesh")) return SpawnResult.FailedNavMesh;
        if (failReason.Contains("max concurrent groups total")) return SpawnResult.FailedConcurrentLimit;
        if (failReason.Contains("max concurrent groups per faction")) return SpawnResult.FailedFactionLimit;
        if (failReason.Contains("queue full")) return SpawnResult.FailedConcurrentLimit;
        return SpawnResult.FailedInvalidPolicy;
    }

    private int CountGroupsByFaction(string factionId)
    {
        var all = UnityEngine.Object.FindObjectsByType<GroupAIManager>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int count = 0;
        foreach (var g in all)
            if (g.gameObject.name.Contains(factionId, StringComparison.OrdinalIgnoreCase))
                count++;
        return count;
    }

    private IEnumerator ExecuteSpawnRequestRoutine(
        GameObject prefab, Vector3? requestedPos, int count,
        GroupAIManager grp, SpawnRequestResponse response, SpawnRequest req)
    {
        var newlyAccumulator = new List<GameObject>();

        // [v2.5] 클릭 위치 주변 분산
        //   첫 번째 멤버: 클릭 위치 정확히
        //   2~N번째: 클릭 위치 주변 원형 분산 (반경 2.5m + i*0.5m)
        //   requestedPos가 null이면 (Strategy 미설정) 기존 spawn point fallback (모든 멤버 null)
        for (int i = 0; i < count; i++)
        {
            Vector3? thisPos = null;
            if (requestedPos.HasValue)
            {
                if (i == 0)
                {
                    thisPos = requestedPos;
                }
                else
                {
                    // 원형 분산 — 2번째부터 균등 각도로 배치
                    float angle = ((i - 1) / (float)Mathf.Max(1, count - 1)) * Mathf.PI * 2f;
                    float radius = 2.5f + (i * 0.5f);
                    Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    thisPos = requestedPos.Value + offset;
                }
            }
            yield return SpawnOneCharacterRoutine(prefab, thisPos, newlyAccumulator);
        }

        if (newlyAccumulator.Count > 0)
            OnAllCharactersSpawned?.Invoke(newlyAccumulator);

        yield return new WaitForSeconds(POST_SPAWN_DELAY_SEC);

        if (grp != null)
        {
            int registered = 0;
            foreach (var go in newlyAccumulator)
            {
                if (go == null) continue;
                var brain = go.GetComponent<MobAIBrain>();
                if (brain != null)
                {
                    grp.RegisterMember(brain);
                    response.spawnedMemberIds.Add(go.name);
                    registered++;
                }
            }
            Debug.Log($"<color=#88FF88>[WorldAISpawnManager:GroupForm]</color> " +
                      $"{grp.gameObject.name}에 {registered}명 등록 완료 → 총 {grp.MemberCount}명");
        }
        else
        {
            foreach (var go in newlyAccumulator)
                if (go != null) response.spawnedMemberIds.Add(go.name);
        }

        response.result = SpawnResult.Success;
        response.isCompleted = true;
        OnSpawnRequestCompleted?.Invoke(response);

        Debug.Log($"<color=#88FF88>[WorldAISpawnManager]</color> ExecuteSpawnRequest 완료: " +
                  $"{response.spawnedMemberIds.Count}명 스폰 → {response}");
    }

    // [v2.5] 자동 생성된 그룹들의 부모 컨테이너 (DontDestroyOnLoad).
    //   Hierarchy 정리 — 평면 분산 대신 [Spawned Groups] 자식으로 모음.
    private GameObject _spawnedGroupsContainer;

    private Transform GetOrCreateGroupsContainer()
    {
        if (_spawnedGroupsContainer != null) return _spawnedGroupsContainer.transform;

        _spawnedGroupsContainer = new GameObject("[Spawned Groups]");
        DontDestroyOnLoad(_spawnedGroupsContainer);
        Debug.Log($"<color=#88FF88>[WorldAISpawnManager]</color> [Spawned Groups] 컨테이너 생성 → DontDestroyOnLoad");
        return _spawnedGroupsContainer.transform;
    }

    private GroupAIManager FindOrCreateGroupForFaction(string factionId)
    {
        var allGroups = UnityEngine.Object.FindObjectsByType<GroupAIManager>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (var g in allGroups)
        {
            if (g.gameObject.name.Contains(factionId, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[WorldAISpawnManager] 기존 그룹 발견 → 재사용: {g.gameObject.name}");
                return g;
            }
        }

        groupSequenceCounter++;
        var go = new GameObject($"Group_{factionId}_{groupSequenceCounter:D3}");

        // [v2.5] DontDestroyOnLoad 컨테이너 자식으로 배치 → Hierarchy 정리
        go.transform.SetParent(GetOrCreateGroupsContainer(), worldPositionStays: true);

        var grp = go.AddComponent<GroupAIManager>();

        // 펙션 매핑에서 PolicySO + Implementor 자동 결정 (Reflection)
        var mapping = factionPrefabMappings.Find(m => m.factionId == factionId);
        if (mapping != null)
        {
            MonoBehaviour implementor = CreatePolicyImplementor(go, mapping.policyImplementorTypeName);

            if (mapping.policySO != null || implementor != null)
            {
                grp.SetPolicy(mapping.policySO, implementor);
                Debug.Log($"<color=#88FF88>[WorldAISpawnManager]</color> 신규 그룹 생성: {go.name} " +
                          $"(policy={(mapping.policySO != null ? mapping.policySO.name : "null")} " +
                          $"impl={(implementor != null ? implementor.GetType().Name : "null")})");
            }
            else
            {
                Debug.LogWarning($"[WorldAISpawnManager] 신규 그룹 {go.name} 생성됐으나 " +
                                 $"factionPrefabMappings에 PolicySO + PolicyImplementorTypeName 모두 없음 — 사기 시스템 비활성. " +
                                 $"Inspector에서 매핑 설정 권장.");
            }
        }
        return grp;
    }

    /// <summary>
    /// [v2.5] Reflection으로 IFactionGroupPolicy + MonoBehaviour 구현체 자동 검색.
    /// 캐시되어 첫 호출만 비용. 새 구현체는 자동 발견 (enum 등록 불필요).
    /// </summary>
    private static List<Type> _cachedPolicyImplementorTypes;
    private static List<Type> GetAllPolicyImplementorTypes()
    {
        if (_cachedPolicyImplementorTypes != null) return _cachedPolicyImplementorTypes;

        var iface = typeof(TDA.PB4.Interfaces.Intelligence.IFactionGroupPolicy);
        var monoType = typeof(MonoBehaviour);
        _cachedPolicyImplementorTypes = new List<Type>();

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
            catch { continue; }

            foreach (var t in types)
            {
                if (t == null || t.IsAbstract) continue;
                if (!iface.IsAssignableFrom(t)) continue;
                if (!monoType.IsAssignableFrom(t)) continue;
                _cachedPolicyImplementorTypes.Add(t);
            }
        }
        return _cachedPolicyImplementorTypes;
    }

    /// <summary>
    /// [v2.5] typeName으로 IFactionGroupPolicy 구현체 동적 AddComponent.
    /// 못 찾으면 가능 옵션 로그 출력.
    /// </summary>
    private MonoBehaviour CreatePolicyImplementor(GameObject host, string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return null;

        var allTypes = GetAllPolicyImplementorTypes();
        var match = allTypes.FirstOrDefault(t => t.Name == typeName);
        if (match == null)
        {
            var options = string.Join(", ", allTypes.Select(t => t.Name));
            Debug.LogWarning($"[WorldAISpawnManager] PolicyImplementor 타입 '{typeName}' 없음. " +
                             $"가능: [{options}]");
            return null;
        }
        return host.AddComponent(match) as MonoBehaviour;
    }

    [ContextMenu("★ Wk3 Test/List Available Policy Implementor Types")]
    private void DebugListPolicyImplementorTypes()
    {
        var types = GetAllPolicyImplementorTypes();
        var list = string.Join("\n  - ", types.Select(t => $"{t.Name}  ({t.FullName})"));
        Debug.Log($"<color=#88FF88>[WorldAISpawnManager]</color> Available IFactionGroupPolicy implementors ({types.Count}):\n  - {list}");
    }

    // ═════════════════════════════════════════════════════════════
    // [v2.5] Editor ContextMenu — 씬 클릭 배치 도구
    // ═════════════════════════════════════════════════════════════

    [ContextMenu("★ Wk3 Test/Begin Click-Spawn (씬 클릭 1회 → 스폰)")]
    private void DebugBeginClickSpawn()
    {
#if UNITY_EDITOR
        SpawnRequestSceneTool.BeginClickPlacement(this);
#else
        Debug.LogWarning("Click-Spawn은 Editor 전용입니다.");
#endif
    }

    [ContextMenu("★ Wk3 Test/Cancel Click-Spawn")]
    private void DebugCancelClickSpawn()
    {
#if UNITY_EDITOR
        SpawnRequestSceneTool.CancelPlacement();
#endif
    }

    [ContextMenu("★ Wk3 Test/Print Queue Status")]
    private void DebugPrintQueueStatus()
    {
        Debug.Log($"[WorldAISpawnManager] 큐 상태: {_queue.Count}/{maxQueuedRequests} " +
                  $"(처리 중: {_processingQueue})");
        for (int i = 0; i < _queue.Count; i++)
            Debug.Log($"  [{i}] {_queue[i]}");
    }
}