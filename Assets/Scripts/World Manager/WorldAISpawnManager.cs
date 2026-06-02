// ─────────────────────────────────────────────────────────────────────
// WorldAISpawnManager (v2.6 — FactionDefinitionSO 통합)
// ─────────────────────────────────────────────────────────────────────
// 갱신: 2026-05-12 v2.6
//   v2.5 → v2.6 변경 사항:
//     [추가] FactionPrefabMapping 에 factionDefinition (FactionDefinitionSO) 필드
//            동기: 펙션 정보 단일 진입점 — 정책 SO + A12 임계 SO + (Wk5+) 문화/계층 등
//     [변경] FindOrCreateGroupForFaction 안 SetPolicy → SetFactionDefinition 호출
//            (factionDefinition 있으면 단일 호출로 모든 펙션 정보 자동 부착)
//     [후방 호환] factionDefinition == null 시 기존 v2.5 SetPolicy 방식 fallback
//     [의제 주석] BiomeAffinity 분리 의제 (시나리오팀 협의 / Wk5+)
//     [보존] v2.5 의 모든 동작 (큐 / NavMesh / 이벤트 / 분산 / ContextMenu)
//
// ═════════════════════════════════════════════════════════════════════
// ★ BiomeAffinity 분리 의제 (Wk5+ 시나리오팀 협의 — 본 zip 작업 외)
// ─────────────────────────────────────────────────────────────────────
// 현재 FactionDefinitionSO 가 BiomeAffinitySO 를 참조 = 도메인 경계 의제:
//   - 펙션 정의 (펙션 자체 정보) ≠ 펙션-지역 친화도 (게임 디자인 / 분포 룰)
//   - 향후 BiomeAffinity 별도 SO (예: WorldBiomeFactionRegistrySO) 로 분리 권장
// 관련 컴포넌트:
//   - FactionDefinitionSO._biomeAffinity → 펙션 SO 가 지역 정보 보유
//   - BiomeAffinitySO 자체는 별도 SO 로 존재 (참조만 함)
// 분리 방안:
//   1) FactionDefinitionSO 에서 _biomeAffinity 제거
//   2) WorldBiomeFactionRegistrySO 신규 (Faction ↔ Biome 매핑)
//   3) BiomeSpawnResolver 가 Registry 조회로 동작
// 본 분리 = 시나리오팀 합의 후 본격 진행 (Wk5+ 진입 시점).
// pB4_ScenarioTeamAgenda 의 27 번째 항목으로 기록 권장.
// ═════════════════════════════════════════════════════════════════════
//
// 갱신: 2026-05-07 v2.5 — 개명 + Instance 오타 수정 (보존)
// 갱신: 2026-05-04 v2.4 — 4 마리 뭉침 문제 해결 (보존)
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
using TDA.PB4.AI.Humanoid; // ★ D-08+ — PersonalityMatrix (자동 archetype 매핑)
using TDA.PB4.AI.Mob;    // [v2.5] MobAIBrain
using TDA.PB4.Data;      // ★ [v2.6] FactionDefinitionSO + FactionGroupPolicySO
using TDA.PB4.Interfaces; // [v2.5] ISpawnRequestReceiver
// =============================================================================
// [v2.5 §11.5] SpawnRequest DTO + Response (v2.5 보존)
// =============================================================================
namespace TDA.PB4.AI
{
    [Serializable]
    public class SpawnRequest
    {
        [NonSerialized] public Guid requestId = Guid.NewGuid();
        [Tooltip("스폰 사유.")]
        public SpawnReason reason = SpawnReason.Manual;
        [NonSerialized] public DateTime requestedAt = DateTime.Now;
        [Tooltip("MobFactionDataSO.factionId 와 일치.")]
        public string factionId = "Skeleton";
        [Tooltip("위치 결정 전략.")]
        public SpawnLocationStrategy locationStrategy = SpawnLocationStrategy.Explicit;
        [Tooltip("Strategy=Explicit일 때 사용.")]
        public Vector3 explicitPosition = Vector3.zero;
        [Tooltip("explicitPosition 설정 여부.")]
        public bool hasExplicitPosition = false;
        [Tooltip("Strategy=NearestSpawnNode 시 사용. -1=자동.")]
        public int explicitCaveNodeIdx = -1;
        [Tooltip("BiomeAffinityResolver 힌트.")]
        public string biomeAffinityHint = "";
        [Range(1, 12)]
        [Tooltip("스폰할 멤버 수.")]
        public int memberCount = 3;
        [Tooltip("가변 멤버 최소값.")] public int minMemberCount = 0;
        [Tooltip("가변 멤버 최대값.")] public int maxMemberCount = 0;
        [Tooltip("Wk7+ 부활 시스템용.")] public string persistentGroupId = "";
        [Range(0f, 1f)][Tooltip("시나리오 우선순위.")] public float priority = 0.5f;
        [NonSerialized] public Dictionary<string, float> modifiers = new();
        [Tooltip("이 시점까지 처리 못 하면 폐기.")]
        [NonSerialized] public float? executeBeforeTime = null;

        // ★ D-08+ (Wk5 D1) — 시나리오 명시 archetype 지정 (자동 매핑 우회)
        // null이면 자동 5축 매핑이 작동. 값이 있으면 그것을 우선 적용.
        // 사용 예: 보스 spawn 시 명시 archetype, 카르마 분기 시 그룹별 다른 archetype 등.
        // 우선순위: Inspector 직접 할당 > overrideArchetype > 자동 매핑 > fallback
        [Tooltip("시나리오 명시 archetype. null이면 자동 매핑이 작동.")]
        public GroupArchetypeSO overrideArchetype = null;

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
    // 이벤트
    // ═════════════════════════════════════════════════════════════
    public static event Action<GameObject> OnCharacterSpawned;
    public static event Action<List<GameObject>> OnAllCharactersSpawned;

    // ═════════════════════════════════════════════════════════════
    // 매직 넘버 const
    // ═════════════════════════════════════════════════════════════
    private const string TARGET_SCENE_NAME = "Scene_World_01";
    private const float NAVMESH_SAMPLE_RADIUS = 5.0f;
    private const float NAVMESH_SAMPLE_SCATTER_RADIUS = 1.5f;
    private const float POST_SPAWN_DELAY_SEC = 0.1f;
    private const float FALLBACK_SCATTER_RADIUS = 5.0f;
    private const float USED_POSITION_AVOID_RADIUS = 0.5f;
    private const int SCATTER_MAX_ATTEMPTS = 8;

    [Header("Debug")]
    [SerializeField] bool despawnCharacters = false;
    [SerializeField] bool respawnCharacters = false;

    [Header("Characters")]
    [Tooltip("스폰할 AI 캐릭터 프리팹 배열")]
    [SerializeField] GameObject[] aiCharacters;

    [Header("Spawn Points (Tier 2 Graph)")]
    [SerializeField] bool useGraphNodeSpawnPoints = true;
    [SerializeField] int[] testSpawnNodeIndices;
    [SerializeField] bool autoEnableNavMeshAgent = true;
    [SerializeField] List<GameObject> spawnedInCharacters = new List<GameObject>();

    private bool _hasSpawnedOnce = false;
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

    private void Start() { StartCoroutine(WaitForServerAndSceneRoutine()); }

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
    // 호스트 + 본 씬 진입 + NavMesh 대기 코루틴 (v2.3 보존)
    // ═════════════════════════════════════════════════════════════
    private IEnumerator WaitForServerAndSceneRoutine()
    {
        while (NetworkManager.Singleton == null) yield return null;
        while (!NetworkManager.Singleton.IsServer) yield return null;
        while (SceneManager.GetActiveScene().name != TARGET_SCENE_NAME) yield return null;
        yield return WaitForNavMeshReady(timeout: 30f);
        yield return new WaitForSeconds(0.5f);
        if (!_hasSpawnedOnce)
        {
            _hasSpawnedOnce = true;
            yield return SpawnAllCharactersRoutine();
        }
    }

    private IEnumerator WaitForNavMeshReady(float timeout)
    {
        float startTime = Time.time;
        int pollCount = 0;
        Debug.Log($"[WorldAISpawnManager] NavMesh 베이크 완료 대기 시작 — timeout {timeout}초");
        while (Time.time - startTime < timeout)
        {
            pollCount++;
            bool foundNavMesh = false;
            var graph = CaveNodeGraphBuilder.Instance;
            if (graph != null && graph.nodesData != null && graph.nodesData.Count > 0)
            {
                int checkCount = Mathf.Min(graph.nodesData.Count, 5);
                for (int i = 0; i < checkCount; i++)
                {
                    if (NavMesh.SamplePosition(graph.nodesData[i].position,
                                                out _, NAVMESH_SAMPLE_RADIUS, NavMesh.AllAreas))
                    { foundNavMesh = true; break; }
                }
            }
            if (!foundNavMesh && aiCharacters != null && aiCharacters.Length > 0 && aiCharacters[0] != null)
            {
                if (NavMesh.SamplePosition(aiCharacters[0].transform.position,
                                            out _, NAVMESH_SAMPLE_RADIUS, NavMesh.AllAreas))
                { foundNavMesh = true; }
            }
            if (foundNavMesh)
            {
                Debug.Log($"[WorldAISpawnManager] ✓ NavMesh 베이크 완료 — {Time.time - startTime:F1}초 ({pollCount}회)");
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
        }
        Debug.LogWarning($"[WorldAISpawnManager] NavMesh 폴링 타임아웃 ({timeout}초) — 강제 진행");
    }

    // ═════════════════════════════════════════════════════════════
    // 스폰 코루틴 (v2.4 분산 보존)
    // ═════════════════════════════════════════════════════════════
    private IEnumerator SpawnAllCharactersRoutine()
    {
        if (aiCharacters == null || aiCharacters.Length == 0)
        {
            Debug.LogWarning("[WorldAISpawnManager] aiCharacters 배열 비어있음 — 스킵");
            yield break;
        }
        _usedScatterPositions.Clear();
        List<Vector3> spawnPoints = ResolveSpawnPoints();
        Debug.Log($"[WorldAISpawnManager] 스폰 시작 — {aiCharacters.Length}개 prefab × {spawnPoints.Count}개 spawn point");
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
        Debug.Log($"[WorldAISpawnManager] 스폰 완료 — count={spawnedInCharacters.Count}");
        if (newlySpawned.Count > 0)
        {
            try { OnAllCharactersSpawned?.Invoke(newlySpawned); }
            catch (Exception ex) { Debug.LogError($"[WorldAISpawnManager] OnAllCharactersSpawned 에러: {ex}"); }
        }
    }

    private IEnumerator SpawnOneCharacterRoutine(GameObject prefab, Vector3? requestedPos,
                                                  List<GameObject> newlySpawnedAccumulator)
    {
        Vector3 spawnPos;
        bool isOnNavMesh;
        if (requestedPos.HasValue && TryFindNavMeshPosition(requestedPos.Value, out spawnPos))
        {
            isOnNavMesh = true;
            Debug.Log($"[WorldAISpawnManager] ✓ Tier 2 노드 NavMesh 매칭: 요청={requestedPos.Value} → {spawnPos}");
        }
        else if (TryFindScatteredNavMeshPosition(prefab.transform.position, out spawnPos))
        {
            isOnNavMesh = true;
            Debug.LogWarning($"[WorldAISpawnManager] 요청 좌표 NavMesh 외부 — prefab 분산 폴백: {spawnPos}");
        }
        else
        {
            spawnPos = prefab.transform.position;
            isOnNavMesh = false;
            Debug.LogError($"[WorldAISpawnManager] {prefab.name} — NavMesh 검증 실패. Agent 스킵.");
        }

        GameObject instance = Instantiate(prefab, spawnPos, Quaternion.identity);
        var netObj = instance.GetComponent<NetworkObject>();
        if (netObj != null) netObj.Spawn();
        else Debug.LogWarning($"[WorldAISpawnManager] {prefab.name} 에 NetworkObject 없음");
        spawnedInCharacters.Add(instance);
        newlySpawnedAccumulator.Add(instance);
        yield return new WaitForSeconds(POST_SPAWN_DELAY_SEC);

        if (autoEnableNavMeshAgent && isOnNavMesh)
        {
            var agent = instance.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.enabled = true;
                agent.Warp(spawnPos);
                Debug.Log($"[WorldAISpawnManager] {instance.name} 스폰 ✓ pos={spawnPos} agent=True");
            }
            else
                Debug.LogWarning($"[WorldAISpawnManager] {instance.name} 에 NavMeshAgent 없음");
        }
        else if (autoEnableNavMeshAgent && !isOnNavMesh)
            Debug.LogWarning($"[WorldAISpawnManager] {instance.name} NavMesh 외부 → Agent 스킵");

        try { OnCharacterSpawned?.Invoke(instance); }
        catch (Exception ex) { Debug.LogError($"[WorldAISpawnManager] OnCharacterSpawned 에러: {ex}"); }
    }

    // ═════════════════════════════════════════════════════════════
    // NavMesh 검증 헬퍼 (v2.4 보존)
    // ═════════════════════════════════════════════════════════════
    private bool TryFindNavMeshPosition(Vector3 candidate, out Vector3 result)
    {
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, NAVMESH_SAMPLE_RADIUS, NavMesh.AllAreas))
        { result = hit.position; return true; }
        result = Vector3.zero;
        return false;
    }

    private bool TryFindScatteredNavMeshPosition(Vector3 baseCandidate, out Vector3 result)
    {
        if (!NavMesh.SamplePosition(baseCandidate, out NavMeshHit baseHit, NAVMESH_SAMPLE_RADIUS, NavMesh.AllAreas))
        { result = Vector3.zero; return false; }
        for (int attempt = 0; attempt < SCATTER_MAX_ATTEMPTS; attempt++)
        {
            Vector3 randomOffset = new Vector3(
                UnityEngine.Random.Range(-FALLBACK_SCATTER_RADIUS, FALLBACK_SCATTER_RADIUS), 0,
                UnityEngine.Random.Range(-FALLBACK_SCATTER_RADIUS, FALLBACK_SCATTER_RADIUS));
            Vector3 scattered = baseCandidate + randomOffset;
            if (NavMesh.SamplePosition(scattered, out NavMeshHit scatterHit, NAVMESH_SAMPLE_SCATTER_RADIUS, NavMesh.AllAreas))
            {
                Vector3 candidate = scatterHit.position;
                if (!IsTooCloseToUsedPosition(candidate))
                {
                    _usedScatterPositions.Add(candidate);
                    result = candidate;
                    return true;
                }
            }
        }
        Debug.LogWarning($"[WorldAISpawnManager] 분산 시도 {SCATTER_MAX_ATTEMPTS}회 fail — baseCandidate 폴백");
        _usedScatterPositions.Add(baseHit.position);
        result = baseHit.position;
        return true;
    }

    private bool IsTooCloseToUsedPosition(Vector3 candidate)
    {
        foreach (var used in _usedScatterPositions)
            if (Vector3.Distance(candidate, used) < USED_POSITION_AVOID_RADIUS) return true;
        return false;
    }

    private List<Vector3> ResolveSpawnPoints()
    {
        var result = new List<Vector3>();
        if (!useGraphNodeSpawnPoints) return result;
        var graph = CaveNodeGraphBuilder.Instance;
        if (graph == null || graph.nodesData == null || graph.nodesData.Count == 0)
        {
            Debug.LogWarning("[WorldAISpawnManager] CaveNodeGraphBuilder 부재 — 원본 위치 폴백");
            return result;
        }
        if (testSpawnNodeIndices != null && testSpawnNodeIndices.Length > 0)
        {
            foreach (int idx in testSpawnNodeIndices)
            {
                if (idx >= 0 && idx < graph.nodesData.Count) result.Add(graph.nodesData[idx].position);
                else Debug.LogWarning($"[WorldAISpawnManager] idx {idx} 범위 외 — 스킵");
            }
            Debug.Log($"[WorldAISpawnManager] testSpawnNodeIndices → {result.Count}개 spawn point");
        }
        else
        {
            int count = Mathf.Min(aiCharacters?.Length ?? 0, graph.nodesData.Count);
            for (int i = 0; i < count; i++) result.Add(graph.nodesData[i].position);
            Debug.Log($"[WorldAISpawnManager] 자동 선택 → {count}개 노드");
        }
        return result;
    }

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

    // ═════════════════════════════════════════════════════════════════════
    // ISpawnRequestReceiver — 정식 구현 (v2.5 보존 + v2.6 FactionDefinitionSO)
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

        [Tooltip("[v2.7] true → 그룹 결성 안 함. 떠돌이 (Wandering) 개체 — Mob / Humanoid 모두 가능. " +
                 "GroupAIManager 미부착 = 그룹 사기 / A12 임계 / 토큰 시스템 비활성. " +
                 "Humanoid 떠돌이 / 야생 Mob 시나리오에 유용.")]
        public bool isWandering = false;

        // ═══════════════════════════════════════════════════════════════════
        // ★ v2.6 — FactionDefinitionSO 단일 진입점 (Wk5 본격)
        // ═══════════════════════════════════════════════════════════════════
        [Tooltip("[v2.6] 펙션 정의 SO 단일 진입점. 본 SO 1 종 = 정책 + A12 임계 + (Wk5+) 문화/계층 모두. " +
                 "이 필드 사용 시 아래 policySO 자동 무시 (FactionDefinitionSO 의 GroupPolicy 우선). " +
                 "권장 사용 — 펙션 SO 단일 편집 = 모든 정보 한 곳.")]
        public FactionDefinitionSO factionDefinition;

        // ═══════════════════════════════════════════════════════════════════
        // v2.5 fallback 필드 (factionDefinition 미사용 시)
        // ═══════════════════════════════════════════════════════════════════
        [Tooltip("[v2.5] 자동 생성된 그룹에 할당할 FactionGroupPolicySO. " +
                 "★ factionDefinition 설정 시 자동 무시. fallback 으로만 사용.")]
        public FactionGroupPolicySO policySO;

        [Tooltip("[v2.5] 자동 생성된 그룹 GameObject에 AddComponent할 정책 구현체. " +
                 "인스펙터 드롭다운으로 IFactionGroupPolicy 모든 구현체 자동 검색. " +
                 "비워두면 (None) 사기 시스템 비활성. " +
                 "새 구현체 추가 시 자동 발견 — 코드 수정 불필요.")]
        [PolicyImplementorPicker]
        public string policyImplementorTypeName = "";
    }

    [Header("━━━ ★ Wk3 SpawnRequest 매핑 ━━━━━━━━━━━━")]
    [Tooltip("[v2.6] SpawnRequest.factionId → 실제 prefab + FactionDefinitionSO 매핑.")]
    public List<FactionPrefabMapping> factionPrefabMappings = new();

    [Tooltip("[v2.5] 스폰 후 그룹 자동 결성 ON.")]
    public bool autoFormGroupOnSpawn = true;

    [Tooltip("[v2.5] 펙션별 동시 활성 그룹 수 상한. 0=무제한.")]
    [Range(0, 20)] public int maxConcurrentGroupsPerFaction = 5;

    [Tooltip("[v2.5] 씬 전체 동시 활성 그룹 수 상한. 0=무제한.")]
    [Range(0, 50)] public int maxConcurrentGroupsTotal = 12;

    [Tooltip("[v2.5] 큐의 최대 적재량.")]
    [Range(1, 32)] public int maxQueuedRequests = 8;

    [Tooltip("[v2.5] 그룹 식별자 자동 증가 카운터.")]
    [SerializeField] private int groupSequenceCounter = 0;

    // ═════════════════════════════════════════════════════════════
    // ★ D-08+ (Wk5 D1) — Group Archetype 자동 매핑
    // ═════════════════════════════════════════════════════════════
    [Header("━━━ ★ Group Archetype 자동 매핑 ━━━━━━━━━━━━")]
    [Tooltip("Spawn 시점에 GroupAIManager.groupArchetype 이 null 이면\n" +
             "본 pool 의 자산 중 leader NPC 의 5축 좌표와 유클리드 거리가\n" +
             "가장 가까운 1 종을 자동 할당. Inspector 직접 할당 시 본 매핑 측 무력.")]
    [SerializeField] private GroupArchetypeSO[] groupArchetypePool;

    [Tooltip("Pool 비어있거나 Leader 의 5축 측정 실패 시 사용할 fallback 자산.")]
    [SerializeField] private GroupArchetypeSO fallbackGroupArchetype;

    [Tooltip("Tie-break 거리. 최소 거리 + 본 값 이내의 후보들을 묶어 random pick → 다양성 보장.\n" +
             "0 = 절대 결정론 / 0.15 = 가벼운 다양성 / 0.30 = 느슨한 random.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float archetypeTieBreakDistance = 0.15f;

    [Tooltip("자동 매핑 결과를 Console 에 출력. 운영 시 false 권장.")]
    [SerializeField] private bool autoAssignArchetypeDebugLog = true;

    private readonly List<SpawnRequest> _queue = new();
    private bool _processingQueue = false;

    public event Action<SpawnRequestResponse> OnSpawnRequestCompleted;

    // ─────────────────────────────────────────────────────────
    // ISpawnRequestReceiver 구현
    // ─────────────────────────────────────────────────────────
    public bool CanAcceptRequest(SpawnRequest req, out string failReason)
    {
        failReason = "";
        if (req == null) { failReason = "request null"; return false; }
        var mapping = factionPrefabMappings.Find(m => m.factionId == req.factionId);
        if (mapping == null || mapping.prefab == null)
        { failReason = $"factionId='{req.factionId}' 매핑 없음"; return false; }
        if (req.executeBeforeTime.HasValue && Time.time > req.executeBeforeTime.Value)
        { failReason = $"timeout"; return false; }
        if (autoFormGroupOnSpawn)
        {
            int totalGroups = UnityEngine.Object.FindObjectsByType<GroupAIManager>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            if (maxConcurrentGroupsTotal > 0 && totalGroups >= maxConcurrentGroupsTotal)
            { failReason = $"max concurrent groups total ({maxConcurrentGroupsTotal})"; return false; }
            if (maxConcurrentGroupsPerFaction > 0)
            {
                int factionGroups = CountGroupsByFaction(req.factionId);
                if (factionGroups >= maxConcurrentGroupsPerFaction)
                { failReason = $"max concurrent groups per faction '{req.factionId}'"; return false; }
            }
        }
        if (req.locationStrategy == SpawnLocationStrategy.Explicit ||
            req.locationStrategy == SpawnLocationStrategy.ScenarioDirected)
        {
            if (req.hasExplicitPosition && !TryFindNavMeshPosition(req.explicitPosition, out _))
            { failReason = $"NavMesh 외부 ({req.explicitPosition})"; return false; }
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

        // ★ v2.7 — isWandering 분기: 떠돌이 / 야생 개체는 그룹 결성 X
        GroupAIManager grp = null;
        if (autoFormGroupOnSpawn && (mapping == null || !mapping.isWandering))
        {
            grp = FindOrCreateGroupForFaction(req.factionId);
        }
        else if (mapping != null && mapping.isWandering)
        {
            Debug.Log($"<color=#88FF88>[WorldAISpawnManager]</color> Wandering 모드 — {req.factionId} 그룹 결성 스킵");
        }

        response.groupId = grp != null ? grp.gameObject.name : "(wandering)";
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
            Debug.LogWarning($"[WorldAISpawnManager] 큐 한도 초과 — 폐기: {req}");
            var resp = new SpawnRequestResponse
            {
                requestId = req.requestId,
                result = SpawnResult.FailedConcurrentLimit,
                failReason = $"queue full",
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
                Debug.LogWarning("[WorldAISpawnManager] BiomeAffinityResolver는 Wk5+ stub.");
                return ResolveNearestSpawnNode(req);
            case SpawnLocationStrategy.FactionTerritory:
                Debug.LogWarning("[WorldAISpawnManager] FactionTerritory는 Wk5+ stub.");
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
        for (int i = 0; i < count; i++)
        {
            Vector3? thisPos = null;
            if (requestedPos.HasValue)
            {
                if (i == 0) thisPos = requestedPos;
                else
                {
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
            int registered = 0, skipped = 0;
            foreach (var go in newlyAccumulator)
            {
                if (go == null) continue;
                // ★ v2.7 — BaseAIBrain (Mob + Humanoid 모두) 검사
                var brain = go.GetComponent<TDA.PB4.AI.BaseAIBrain>();
                if (brain != null)
                {
                    grp.RegisterMember(brain);
                    response.spawnedMemberIds.Add(go.name);
                    registered++;

                    // ★ E-07 (S2) — AIPerceptionSystem._factionId 자동 주입.
                    // 본 spawn 시점 측 group 측 FactionId 측 본격 알 수 있는 시점.
                    // 디자이너 측 Inspector 측 수동 입력 회피 + 즉시 Mood 동기화.
                    var perception = go.GetComponent<TDA.PB4.AI.Perception.AIPerceptionSystem>();
                    if (perception != null)
                    {
                        // Priority — req.factionId (★ SpawnRequest 측 본격 보장) > grp.GetFactionId() (★ policySO.factionId 측 정합 의존)
                        string fid = !string.IsNullOrEmpty(req?.factionId)
                            ? req.factionId
                            : grp.GetFactionId();

                        if (!string.IsNullOrEmpty(fid))
                        {
                            perception.SetFactionId(fid);
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"<color=#FFAA66>[WorldAISpawnManager / E-07]</color> " +
                                $"{go.name} — AIPerceptionSystem._factionId 자동 주입 실패. " +
                                $"req.factionId='{req?.factionId ?? "null"}' / " +
                                $"grp.GetFactionId()='{grp.GetFactionId()}' (policySO.factionId 정합 점검 필요).");
                        }
                    }
                }
                else
                {
                    skipped++;
                    Debug.LogWarning($"[WorldAISpawnManager] {go.name} — BaseAIBrain 없음 → 그룹 등록 스킵");
                }
            }
            Debug.Log($"<color=#88FF88>[WorldAISpawnManager:GroupForm]</color> " +
                      $"{grp.gameObject.name}에 {registered}명 등록 (스킵 {skipped}) → 총 {grp.MemberCount}명");

            // ★ D-08+ — Group Archetype 자동 매핑 (Inspector 직접 할당 없을 시)
            TryAutoAssignGroupArchetype(grp, req);
        }
        else
        {
            // ★ v2.7 — Wandering 분기 (그룹 결성 X) — 떠돌이 / 야생 개체
            foreach (var go in newlyAccumulator)
                if (go != null) response.spawnedMemberIds.Add(go.name);
            Debug.Log($"<color=#88FF88>[WorldAISpawnManager:Wandering]</color> " +
                      $"{newlyAccumulator.Count}명 스폰 — Wandering (그룹 미부착)");
        }
        response.result = SpawnResult.Success;
        response.isCompleted = true;
        OnSpawnRequestCompleted?.Invoke(response);
        Debug.Log($"<color=#88FF88>[WorldAISpawnManager]</color> ExecuteSpawnRequest 완료: {response.spawnedMemberIds.Count}명");
    }

    // ═════════════════════════════════════════════════════════════
    // ★ D-08+ (Wk5 D1) — Group Archetype 자동 매핑
    //
    // 우선순위 (3 단계) :
    //   1) Inspector 직접 할당 보존 (groupAI.GroupArchetype != null → skip)
    //   2) SpawnRequest.overrideArchetype (시나리오 명시 지정 — ★ Wk5 D1 추가)
    //   3) 자동 5축 매핑 — 아래 흐름
    //      a) Leader (members[0]) 의 5축 좌표 측정
    //      b) Pool 의 4 자산과 유클리드 거리 측정 → 가장 가까운 1 종
    //      c) tieBreakDistance 이내 동률 후보들 random pick → 다양성 보장
    //      d) Leader 5축 측정 실패 (Skeleton 등) → random pool fallback
    //      e) Pool 비어있음 → fallbackGroupArchetype
    //
    // 시나리오팀 사용 예시 :
    //   var request = new SpawnRequest {
    //       factionId = "Goblin",
    //       memberCount = 5,
    //       overrideArchetype = madArchetype  // ★ 시나리오 명시
    //   };
    //   WorldAISpawnManager.Instance.QueueRequest(request);
    //
    // 차후 의제 :
    //   - ScenarioArchetypeDirector (런타임 동적 override — 지역/팩션/그룹)
    //   - 보스 NPC 별도 플래그 (NPC 단위 archetype 차등)
    // ═════════════════════════════════════════════════════════════
    private void TryAutoAssignGroupArchetype(GroupAIManager groupAI, SpawnRequest req)
    {
        if (groupAI == null) return;

        // ─── 1. Inspector 직접 할당 보존 (최우선) ───
        if (groupAI.GroupArchetype != null)
        {
            if (autoAssignArchetypeDebugLog)
                Debug.Log($"[SpawnMgr] {groupAI.name}: GroupArchetype 이미 할당됨 → skip");
            return;
        }

        // ─── 2. SpawnRequest.overrideArchetype (시나리오 명시 지정) ───
        if (req != null && req.overrideArchetype != null)
        {
            groupAI.SetGroupArchetype(req.overrideArchetype);
            if (autoAssignArchetypeDebugLog)
                Debug.Log($"[SpawnMgr] {groupAI.name}: GroupArchetype 시나리오 override → " +
                          $"'{req.overrideArchetype.archetypeName}'");
            return;
        }

        // ─── 3. 자동 5축 매핑 — Pool 비어있음 → fallback ───
        if (groupArchetypePool == null || groupArchetypePool.Length == 0)
        {
            if (fallbackGroupArchetype != null)
            {
                groupAI.SetGroupArchetype(fallbackGroupArchetype);
                if (autoAssignArchetypeDebugLog)
                    Debug.Log($"[SpawnMgr] {groupAI.name}: Pool 비어있음 → fallback '{fallbackGroupArchetype.archetypeName}'");
            }
            else if (autoAssignArchetypeDebugLog)
            {
                Debug.LogWarning($"[SpawnMgr] {groupAI.name}: Pool + fallback 모두 비어있음 → 자동 매핑 차단");
            }
            return;
        }

        // ─── 3. Leader 5축 좌표 측정 ───
        if (!TryGetLeaderPersonality(groupAI, out float c, out float s, out float o, out float a, out float d))
        {
            // 측정 실패 (Skeleton 등 5축 없는 NPC) → random pool fallback
            var rand = groupArchetypePool[UnityEngine.Random.Range(0, groupArchetypePool.Length)];
            var chosen = rand ?? fallbackGroupArchetype;
            if (chosen != null) groupAI.SetGroupArchetype(chosen);
            if (autoAssignArchetypeDebugLog)
                Debug.Log($"[SpawnMgr] {groupAI.name}: Leader 5축 측정 실패 → random fallback '{chosen?.archetypeName ?? "null"}'");
            return;
        }

        // ─── 4. 4 자산 측 거리 측정 ───
        float bestDist = float.MaxValue;
        GroupArchetypeSO best = null;
        foreach (var arch in groupArchetypePool)
        {
            if (arch == null) continue;
            float dist = arch.ComputeDistanceTo(c, s, o, a, d);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = arch;
            }
        }

        // ─── 5. Tie-break — bestDist + tieBreak 이내 후보들 random pick ───
        var candidates = new List<GroupArchetypeSO>(groupArchetypePool.Length);
        foreach (var arch in groupArchetypePool)
        {
            if (arch == null) continue;
            float dist = arch.ComputeDistanceTo(c, s, o, a, d);
            if (dist <= bestDist + archetypeTieBreakDistance) candidates.Add(arch);
        }

        var finalChoice = candidates.Count > 1
            ? candidates[UnityEngine.Random.Range(0, candidates.Count)]
            : best;

        if (finalChoice == null)
        {
            if (autoAssignArchetypeDebugLog)
                Debug.LogWarning($"[SpawnMgr] {groupAI.name}: 후보 선정 실패 → fallback");
            if (fallbackGroupArchetype != null) groupAI.SetGroupArchetype(fallbackGroupArchetype);
            return;
        }

        groupAI.SetGroupArchetype(finalChoice);
        if (autoAssignArchetypeDebugLog)
        {
            Debug.Log(
                $"[SpawnMgr] {groupAI.name}: GroupArchetype 자동 할당 → '{finalChoice.archetypeName}' " +
                $"(leader 5축 [C={c:F2} S={s:F2} O={o:F2} A={a:F2} D={d:F2}] " +
                $"→ dist={bestDist:F3} / 후보 {candidates.Count}종)");
        }
    }

    private bool TryGetLeaderPersonality(GroupAIManager groupAI,
        out float c, out float s, out float o, out float a, out float d)
    {
        c = s = o = a = d = 0.5f;
        if (groupAI == null) return false;

        var members = groupAI.Members;
        if (members == null || members.Count == 0) return false;

        // 첫 살아있는 HumanoidAIBrain 멤버 1명 → leader
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            if (m?.brain == null) continue;

            var humanoid = m.brain as HumanoidAIBrain;
            if (humanoid == null) continue;  // Skeleton 등 5축 없는 NPC skip

            var p = humanoid.Personality;
            c = p.control;
            s = p.stability;
            o = p.openness;
            a = p.agreeable;
            d = p.directness;
            return true;
        }
        return false;  // 모든 멤버가 5축 없음
    }

    // ═════════════════════════════════════════════════════════════
    // [Spawned Groups] 컨테이너
    // ═════════════════════════════════════════════════════════════
    private GameObject _spawnedGroupsContainer;

    private Transform GetOrCreateGroupsContainer()
    {
        if (_spawnedGroupsContainer != null) return _spawnedGroupsContainer.transform;
        _spawnedGroupsContainer = new GameObject("[Spawned Groups]");
        DontDestroyOnLoad(_spawnedGroupsContainer);
        Debug.Log($"<color=#88FF88>[WorldAISpawnManager]</color> [Spawned Groups] 컨테이너 생성 → DontDestroyOnLoad");
        return _spawnedGroupsContainer.transform;
    }

    // ═════════════════════════════════════════════════════════════
    // ★ v2.6 — FindOrCreateGroupForFaction (FactionDefinitionSO 우선)
    // ═════════════════════════════════════════════════════════════
    private GroupAIManager FindOrCreateGroupForFaction(string factionId)
    {
        // 기존 그룹 재사용
        var allGroups = UnityEngine.Object.FindObjectsByType<GroupAIManager>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var g in allGroups)
        {
            if (g.gameObject.name.Contains(factionId, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[WorldAISpawnManager] 기존 그룹 재사용: {g.gameObject.name}");
                return g;
            }
        }

        // 신규 그룹 결성
        groupSequenceCounter++;
        var go = new GameObject($"Group_{factionId}_{groupSequenceCounter:D3}");
        go.transform.SetParent(GetOrCreateGroupsContainer(), worldPositionStays: true);
        var grp = go.AddComponent<GroupAIManager>();

        var mapping = factionPrefabMappings.Find(m => m.factionId == factionId);
        if (mapping != null)
        {
            // 정책 구현체 (Reflection)
            MonoBehaviour implementor = CreatePolicyImplementor(go, mapping.policyImplementorTypeName);

            // ★ v2.6 — FactionDefinitionSO 우선 적용
            if (mapping.factionDefinition != null)
            {
                grp.SetFactionDefinition(mapping.factionDefinition, implementor);

                // ★ E-07 (S2) — WorldFactionStateManager 측 자동 등록
                // 본 호출은 idempotent (E-05) — 같은 Faction 측 중복 spawn 시 1 회만 등록.
                // 초기 상태 = LIFECYCLE_ACTIVE (E-05 정합).
                if (TDA.PB4.Faction.WorldFactionStateManager.Instance != null)
                {
                    TDA.PB4.Faction.WorldFactionStateManager.Instance
                        .RegisterFaction(mapping.factionDefinition);
                }

                Debug.Log($"<color=#88FF88>[WorldAISpawnManager]</color> 신규 그룹 생성 (v2.6 FactionDef): {go.name} " +
                          $"(factionDef={mapping.factionDefinition.name}, " +
                          $"policy={mapping.factionDefinition.GroupPolicy?.name ?? "null"}, " +
                          $"threshold={mapping.factionDefinition.Threshold?.name ?? "null"}, " +
                          $"impl={implementor?.GetType().Name ?? "null"})");
            }
            // v2.5 fallback — policySO 만 적용 (factionDefinition 미사용 시)
            else if (mapping.policySO != null || implementor != null)
            {
                grp.SetPolicy(mapping.policySO, implementor);
                Debug.LogWarning($"<color=#88FF88>[WorldAISpawnManager]</color> 신규 그룹 생성 (v2.5 fallback): {go.name} " +
                                 $"(policy={mapping.policySO?.name ?? "null"}, " +
                                 $"impl={implementor?.GetType().Name ?? "null"}) " +
                                 $"★ factionDefinition 미설정 — A12 임계 비활성. " +
                                 $"본격 권장 = mapping.factionDefinition 설정.");
            }
            else
            {
                Debug.LogWarning($"[WorldAISpawnManager] 신규 그룹 {go.name} — " +
                                 $"factionDefinition / policySO / impl 모두 없음. 사기 + A12 비활성.");
            }
        }
        return grp;
    }

    /// <summary>
    /// [v2.5] Reflection 으로 IFactionGroupPolicy + MonoBehaviour 구현체 자동 검색.
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

    private MonoBehaviour CreatePolicyImplementor(GameObject host, string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return null;
        var allTypes = GetAllPolicyImplementorTypes();
        var match = allTypes.FirstOrDefault(t => t.Name == typeName);
        if (match == null)
        {
            var options = string.Join(", ", allTypes.Select(t => t.Name));
            Debug.LogWarning($"[WorldAISpawnManager] PolicyImplementor 타입 '{typeName}' 없음. 가능: [{options}]");
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
    // Editor ContextMenu (v2.5 보존)
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
        Debug.Log($"[WorldAISpawnManager] 큐 상태: {_queue.Count}/{maxQueuedRequests} (처리 중: {_processingQueue})");
        for (int i = 0; i < _queue.Count; i++)
            Debug.Log($"  [{i}] {_queue[i]}");
    }
}