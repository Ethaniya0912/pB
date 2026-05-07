// ─────────────────────────────────────────────────────────────────────
// WorldAIManager (v2.4 — 분산 강화 + 사용 좌표 회피)
// ─────────────────────────────────────────────────────────────────────
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
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using CaveSystem;

public class WorldAIManager : MonoBehaviour
{
    public static WorldAIManager Instant { get; private set; }

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
        if (Instant == null) Instant = this;
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

        Debug.Log($"[WorldAIManager] NavMesh 베이크 완료 대기 시작 — timeout {timeout}초");

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
                Debug.Log($"[WorldAIManager] ✓ NavMesh 베이크 완료 감지 — " +
                          $"{Time.time - startTime:F1}초 ({pollCount}회 폴링)");
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }

        Debug.LogWarning($"[WorldAIManager] NavMesh 폴링 타임아웃 ({timeout}초) — " +
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
            Debug.LogWarning("[WorldAIManager] aiCharacters 배열 비어있음 — 스폰 스킵");
            yield break;
        }

        // ★ v2.4 — 이번 스폰 사이클의 사용 좌표 추적 초기화
        _usedScatterPositions.Clear();

        List<Vector3> spawnPoints = ResolveSpawnPoints();

        Debug.Log($"[WorldAIManager] 스폰 시작 — {aiCharacters.Length}개 prefab × " +
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

        Debug.Log($"[WorldAIManager] 스폰 완료 — spawnedInCharacters.Count={spawnedInCharacters.Count}");

        // ★ v2.2 — 모든 NPC 스폰 종료 이벤트 발행
        if (newlySpawned.Count > 0)
        {
            try { OnAllCharactersSpawned?.Invoke(newlySpawned); }
            catch (Exception ex) { Debug.LogError($"[WorldAIManager] OnAllCharactersSpawned 핸들러 에러: {ex}"); }
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
            Debug.Log($"[WorldAIManager] ✓ Tier 2 노드 좌표 NavMesh 매칭: " +
                      $"요청={requestedPos.Value} → 매칭={spawnPos}");
        }
        // [2] 요청 좌표 fail → ★ v2.2 — prefab 원본 + 랜덤 분산 시도
        else if (TryFindScatteredNavMeshPosition(prefab.transform.position, out spawnPos))
        {
            isOnNavMesh = true;
            Debug.LogWarning($"[WorldAIManager] 요청 좌표 NavMesh 외부 — " +
                             $"prefab 원본 분산 폴백: {spawnPos}");
        }
        // [3] 모두 fail → 원본 좌표 그대로 (Agent 비활성)
        else
        {
            spawnPos = prefab.transform.position;
            isOnNavMesh = false;
            Debug.LogError($"[WorldAIManager] {prefab.name} — " +
                           $"NavMesh 검증 실패 (요청+원본 모두 외부). " +
                           $"Agent 활성화 스킵. NPC 동작 제한됨.");
        }

        // ── Instantiate + Spawn ──────────────────────────────
        GameObject instance = Instantiate(prefab, spawnPos, Quaternion.identity);

        var netObj = instance.GetComponent<NetworkObject>();
        if (netObj != null) netObj.Spawn();
        else Debug.LogWarning($"[WorldAIManager] {prefab.name} 에 NetworkObject 없음");

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

                Debug.Log($"[WorldAIManager] {instance.name} 스폰 ✓ " +
                          $"pos={spawnPos} agent=True isOnNavMesh=True");
            }
            else
            {
                Debug.LogWarning($"[WorldAIManager] {instance.name} 에 " +
                                 $"NavMeshAgent 컴포넌트 없음");
            }
        }
        else if (autoEnableNavMeshAgent && !isOnNavMesh)
        {
            Debug.LogWarning($"[WorldAIManager] {instance.name} NavMesh 외부 → " +
                             $"Agent 활성화 스킵. isOnNavMesh=False, pos={spawnPos}");
        }

        // ★ v2.2 — 개별 스폰 이벤트 발행
        try { OnCharacterSpawned?.Invoke(instance); }
        catch (Exception ex) { Debug.LogError($"[WorldAIManager] OnCharacterSpawned 핸들러 에러: {ex}"); }
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
        Debug.LogWarning($"[WorldAIManager] 분산 시도 {SCATTER_MAX_ATTEMPTS}회 모두 fail — " +
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
            Debug.LogWarning("[WorldAIManager] CaveNodeGraphBuilder 또는 nodesData 부재 — " +
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
                    Debug.LogWarning($"[WorldAIManager] testSpawnNodeIndices 의 {idx} 가 " +
                                     $"nodesData 범위(0~{graph.nodesData.Count - 1}) 벗어남 — 스킵");
                }
            }
            Debug.Log($"[WorldAIManager] testSpawnNodeIndices 사용 → {result.Count}개 spawn point");
        }
        else
        {
            int count = Mathf.Min(aiCharacters?.Length ?? 0, graph.nodesData.Count);
            for (int i = 0; i < count; i++)
            {
                result.Add(graph.nodesData[i].position);
            }
            Debug.Log($"[WorldAIManager] 자동 선택 → 첫 {count}개 노드 spawn point");
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
}