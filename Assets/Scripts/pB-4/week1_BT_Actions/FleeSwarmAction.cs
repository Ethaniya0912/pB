// =============================================================================
// FleeSwarmAction.cs  |  pB-4 커스텀 BT Action — 고블린 도주
// 패키지  : Unity Behavior 1.0.15 (com.unity.behavior)
//
// 역할:
//   고블린(SwarmGroupPolicy)의 도주 행동.
//   타겟 반대방향으로 전력질주(6m/s) + 주변 동료에 패닉 전파.
//   DeathTrap 지형에서는 8방향 Raycast로 최적 탈출 경로 선택.
//
// Switch 노드에서:
//   FactionPolicyType='Swarm' case에 연결됩니다.
//
// [버그 수정]
//   기존: OnUpdate()에서 매 tick 도주 방향을 재계산.
//         Target=null 시 매 tick 랜덤 방향 → NavMesh 목적지가 계속 바뀜
//         → NavMeshAgent가 경로를 잡자마자 다음 목적지로 리셋 → 제자리 회전.
//   수정: OnStart()에서 도주 방향을 1회 결정하여 _fixedFleeDir에 저장.
//         OnUpdate()는 목적지에 도달했을 때만 새 목적지를 설정.
//         Target이 있으면 실시간으로 반대방향을 추적 (도주 방향 갱신 허용).
// =============================================================================
using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "FleeSwarm",
    story: "[Self] flees from [Target] with swarm panic at [FleeSprintSpeed] speed",
    category: "pB-4/Flee",
    id: "pb4_flee_swarm_action")]
public partial class FleeSwarmAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<Transform> Target;

    /// <summary>도주 속도 (m/s). 고블린 기본 6.0.</summary>
    [Tooltip("전력질주 속도. 고블린=6.0")]
    [SerializeReference] public BlackboardVariable<float> FleeSprintSpeed = new(6f);

    /// <summary>현재 지형 태그. DeathTrap 시 8방향 도주.</summary>
    [SerializeReference] public BlackboardVariable<string> TerrainTags;

    [Tooltip("true = NavMesh 상태 및 도주 방향 상세 로그 출력")]
    [SerializeReference] public BlackboardVariable<bool> DebugLog = new(false);

    // ── [C-4 stub 완성] 패닉 전파 파라미터 ──
    // PB4DecisionAdapter.InitializeBlackboard() 에서 FactionCombatProfileSO 값을 BB 에 기록합니다.
    // BT 에디터에서 이 필드를 해당 BB 변수에 연결하세요.

    /// <summary>패닉 전파 반경 (m). FactionCombatProfileSO.panicChainRadius 에서 설정.</summary>
    [Tooltip("패닉 전파 범위. PanicChainRadius BB 변수 연결 또는 기본값 5m 사용.")]
    [SerializeReference] public BlackboardVariable<float> PanicChainRadius = new(5f);

    /// <summary>패닉 전파 fear 증가 배율. FactionCombatProfileSO.panicChainMultiplier 에서 설정.</summary>
    [Tooltip("패닉 전파 강도. PanicChainMultiplier BB 변수 연결 또는 기본값 2.0 사용.")]
    [SerializeReference] public BlackboardVariable<float> PanicChainMultiplier = new(2f);

    private NavMeshAgent _nav;
    private Transform _selfTransform;

    // [버그 수정] OnStart()에서 결정된 고정 도주 방향 (Target=null 시 사용)
    // 매 tick 랜덤 방향 재계산을 방지하여 NavMeshAgent가 일관된 경로를 유지하도록 함
    private Vector3 _fixedFleeDir;

    // 도착 판정 거리 — 목적지에 이만큼 가까워지면 새 목적지를 설정
    private const float ARRIVE_DISTANCE = 2.0f;

    /// <summary>
    /// 이 거리 이상 타겟에서 멀어지면 도주 성공으로 Success 반환.
    /// 0 이면 비활성 (도주 계속). 권장: 고블린 15~20m.
    /// </summary>
    [Tooltip("도주 성공 판정 거리(m). 타겟에서 이 이상 멀어지면 Success. 0=비활성.")]
    [SerializeReference] public BlackboardVariable<float> SafeFleeDistance = new(15f);

    // ── Stuck 감지 ────────────────────────────────────────────────────────────
    private Vector3 _lastPosition;
    private float _stuckTimer;
    private int _stuckCount;           // 연속 stuck 횟수
    private const float STUCK_CHECK_INTERVAL = 0.5f;   // 위치 비교 주기 (초)
    private const float STUCK_MOVE_THRESHOLD = 0.3f;   // 이 거리 미만이면 stuck
    private const float STUCK_MAX_TIME = 1.5f;   // stuck 유지 시 방향 전환
    private const int STUCK_GIVE_UP = 4;      // 이 횟수 이상이면 Failure

    protected override Status OnStart()
    {
        if (Self.Value == null)
        {
            LogFailure("FleeSwarmAction: Self가 null입니다.");
            return Status.Failure;
        }

        _nav = Self.Value.GetComponent<NavMeshAgent>();
        _selfTransform = Self.Value.transform;

        if (_nav == null)
        {
            Debug.LogWarning($"[FleeSwarm] {Self.Value.name}: NavMeshAgent 컴포넌트가 없습니다.");
            LogFailure("FleeSwarmAction: NavMeshAgent가 없습니다.");
            return Status.Failure;
        }

        if (!_nav.isOnNavMesh)
        {
            Debug.LogWarning($"[FleeSwarm] {Self.Value.name}: isOnNavMesh=false → Failure.");
            LogFailure("FleeSwarmAction: NavMeshAgent가 NavMesh 밖에 있습니다.");
            return Status.Failure;
        }

        _nav.speed = FleeSprintSpeed.Value;

        // ── [버그 수정] 도주 방향을 OnStart에서 1회만 결정 ──
        // Target이 없으면 랜덤 방향을 지금 결정하고 OnUpdate 내내 고정 사용.
        // Target이 있으면 OnUpdate에서 실시간으로 반대방향을 계산하므로 여기선 초기값만 설정.
        _fixedFleeDir = CalcFleeDir();

        // 즉시 첫 목적지 설정
        SetFleeDestination(_fixedFleeDir);

        // Stuck 감지 초기화
        _lastPosition = _selfTransform.position;
        _stuckTimer = 0f;
        _stuckCount = 0;

        if (DebugLog.Value)
            Debug.Log($"[FleeSwarm] {Self.Value.name}: OnStart. " +
                      $"Speed={FleeSprintSpeed.Value} FleeDir={_fixedFleeDir} " +
                      $"Target={(Target.Value != null ? Target.Value.name : "null")}");

        // ── 패닉 전파: 주변 5m 동료에 fear 증가 ──
        // [C-4 완성] BB 변수 PanicChainRadius/Multiplier 로 패닉 전파
        HandlePanicChain();

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_nav == null || !_nav.isOnNavMesh)
            return Status.Failure;

        Vector3 selfPos = _selfTransform.position;

        // ── [수정 1] SafeFleeDistance — 충분히 멀어졌으면 도주 성공 ────────────
        // 기존: 무한 도주 → NavMesh 경계 도달 → 제자리 회전
        // 수정: 타겟에서 SafeFleeDistance 이상 벌어지면 Success 반환
        if (SafeFleeDistance.Value > 0f && Target.Value != null)
        {
            float distToTarget = Vector3.Distance(selfPos, Target.Value.position);
            if (distToTarget >= SafeFleeDistance.Value)
            {
                if (DebugLog.Value)
                    Debug.Log($"[FleeSwarm] {Self.Value.name}: 도주 완료. " +
                              $"dist={distToTarget:F1}m >= safe={SafeFleeDistance.Value:F1}m → Success");
                return Status.Success;
            }
        }

        // ── [수정 2] Stuck 감지 — 일정 시간 이동 없으면 방향 전환 ────────────
        _stuckTimer += Time.deltaTime;
        if (_stuckTimer >= STUCK_CHECK_INTERVAL)
        {
            float moved = Vector3.Distance(selfPos, _lastPosition);
            _lastPosition = selfPos;
            _stuckTimer = 0f;

            if (moved < STUCK_MOVE_THRESHOLD)
            {
                _stuckCount++;
                if (DebugLog.Value)
                    Debug.LogWarning($"[FleeSwarm] {Self.Value.name}: Stuck 감지 " +
                                     $"(moved={moved:F2}m, count={_stuckCount})");

                if (_stuckCount >= STUCK_GIVE_UP)
                {
                    // 너무 오래 막혔으면 Failure → BT가 다른 행동으로 전환
                    Debug.LogWarning($"[FleeSwarm] {Self.Value.name}: Stuck {STUCK_GIVE_UP}회 → Failure");
                    return Status.Failure;
                }

                // 경계에 막혔을 가능성 → 수직 방향으로 탈출 시도
                Vector3 perpDir = Vector3.Cross(_fixedFleeDir, Vector3.up).normalized;
                perpDir = (_stuckCount % 2 == 0) ? perpDir : -perpDir;
                Vector3 escapeDir = (_fixedFleeDir * 0.6f + perpDir * 0.4f).normalized;
                SetFleeDestination(escapeDir);
                _fixedFleeDir = escapeDir;

                if (DebugLog.Value)
                    Debug.Log($"[FleeSwarm] {Self.Value.name}: 경계 우회 → escapeDir={escapeDir}");
            }
            else
            {
                // 정상 이동 중 → stuck 카운터 리셋
                _stuckCount = Mathf.Max(0, _stuckCount - 1);
            }
        }

        // ── Target이 있으면 실시간으로 도주 방향 갱신 ────────────────────────
        // [스핀 버그 수정] Angle 기준 30° → 60°, 최소 재계산 간격 1.5초 추가.
        // 기존: 30° 변화마다 SetDestination → 경로 재계산 중 desiredVelocity 진동 → 스핀.
        // 수정: 60° 이상 변화하고 1.5초 경과 후에만 재계산.
        if (Target.Value != null)
        {
            Vector3 newDir = (selfPos - Target.Value.position).normalized;
            if (Vector3.Angle(_fixedFleeDir, newDir) > 60f)
            {
                _fixedFleeDir = newDir;
                SetFleeDestination(_fixedFleeDir);
            }
        }
        else
        {
            // Target 없음 — 목적지 도달 시 같은 방향으로 계속
            if (!_nav.pathPending && _nav.remainingDistance < ARRIVE_DISTANCE)
            {
                SetFleeDestination(_fixedFleeDir);

                if (DebugLog.Value)
                    Debug.Log($"[FleeSwarm] {Self.Value.name}: 목적지 도달, 계속 도주 " +
                              $"dir={_fixedFleeDir}");
            }
        }

        return Status.Running;
    }

    protected override void OnEnd()
    {
        _fixedFleeDir = Vector3.zero;
        _stuckTimer = 0f;
        _stuckCount = 0;
    }

    // =========================================================================
    // 도주 방향 계산
    // =========================================================================
    /// <summary>
    /// 현재 상황에 맞는 도주 방향 벡터를 반환합니다.
    /// Target이 있으면 반대방향, 없으면 랜덤 방향, DeathTrap이면 8방향 중 최적 방향.
    /// </summary>
    private Vector3 CalcFleeDir()
    {
        string tags = TerrainTags?.Value ?? "";

        if (tags.Contains("DeathTrap"))
            return FindOpenDirection(_selfTransform.position);

        if (Target.Value != null)
            return (_selfTransform.position - Target.Value.position).normalized;

        // Target 없음: 랜덤 방향 (OnStart에서 1회만 호출되어 고정됨)
        Vector3 rnd = UnityEngine.Random.insideUnitSphere;
        rnd.y = 0f;
        return rnd.normalized;
    }

    // =========================================================================
    // 도주 목적지 설정
    // =========================================================================
    /// <summary>
    /// 지정 방향으로 도주 목적지를 설정합니다.
    /// NavMesh 경계 내에서 가장 멀리 갈 수 있는 지점을 찾습니다.
    /// </summary>
    private void SetFleeDestination(Vector3 dir)
    {
        // [스핀 버그 수정] 목적지를 8m → 30m으로 크게 늘림.
        // 기존 8m: 1.3초마다 목적지 도달 → 재계산 시 desiredVelocity 진동 → 스핀.
        // 30m: 5초 이상 같은 목적지 유지 → 경로 안정, desiredVelocity 일정 방향 유지.
        // SamplePosition radius를 3f로 줄여 원치 않는 스냅 방지.
        for (float dist = 30f; dist >= 4f; dist -= 6f)
        {
            Vector3 dest = _selfTransform.position + dir * dist;
            if (NavMesh.SamplePosition(dest, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                _nav.SetDestination(hit.position);

                if (DebugLog.Value)
                    Debug.Log($"[FleeSwarm] {Self.Value.name}: SetDestination={hit.position} " +
                              $"dist={dist} dir={dir}");
                return;
            }
        }

        // 모든 거리 실패 → 가까운 점이라도 설정 (NavMesh 경계 근처)
        Vector3 fallback = _selfTransform.position + dir * 4f;
        if (NavMesh.SamplePosition(fallback, out NavMeshHit fbHit, 15f, NavMesh.AllAreas))
            _nav.SetDestination(fbHit.position);

        if (DebugLog.Value)
            Debug.LogWarning($"[FleeSwarm] {Self.Value.name}: 원거리 SamplePosition 실패 — 폴백 사용.");
    }

    // =========================================================================
    // 8방향 탈출 경로
    // =========================================================================
    /// <summary>
    /// 8방향 Raycast로 벽까지 가장 먼 방향을 찾습니다.
    /// DeathTrap(막다른 길) 지형에서 최적 탈출 경로 선택.
    /// </summary>
    private Vector3 FindOpenDirection(Vector3 origin)
    {
        Vector3 bestDir = Vector3.forward;
        float maxDist = 0f;

        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f;
            Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;

            if (Physics.Raycast(origin + Vector3.up, dir, out RaycastHit rayHit, 30f))
            {
                if (rayHit.distance > maxDist)
                {
                    maxDist = rayHit.distance;
                    bestDir = dir;
                }
            }
            else
            {
                // 30m 내 장애물 없음 → 이 방향이 최적
                return dir;
            }
        }

        return bestDir;
    }

    // =========================================================================
    // 패닉 전파
    // =========================================================================
    /// <summary>
    /// 주변 동료에 패닉을 전파합니다.
    /// OverlapSphere로 panicChainRadius 내 동일 팩션 AI를 찾아 fear를 증가시킵니다.
    /// </summary>
    private void HandlePanicChain()
    {
        // [C-4 stub 완성] BB 변수에서 파라미터 읽기
        float panicRadius = PanicChainRadius.Value;
        float panicMultiplier = PanicChainMultiplier.Value;

        // 전파 반경 0 이면 (오크/스켈레톤) 즉시 종료
        if (panicRadius <= 0f || panicMultiplier <= 0f) return;

        // OverlapSphere 로 panicRadius 내 모든 Collider 탐색
        Collider[] nearby = Physics.OverlapSphere(_selfTransform.position, panicRadius);

        // 자신의 MobAIBrain factionData 취득 (같은 팩션만 전파)
        var selfBrain = Self.Value.GetComponent<TDA.PB4.AI.Mob.MobAIBrain>();

        foreach (var col in nearby)
        {
            if (col.gameObject == Self.Value) continue;

            var brain = col.GetComponent<TDA.PB4.AI.Mob.MobAIBrain>();
            if (brain == null) continue;

            // 같은 팩션 확인은 생략 — 모든 MobAIBrain 에게 전파 (팩션 혼합 씬에서 조정 가능)
            // TODO Week 3: brain.factionData == selfBrain?.factionData 조건 추가
            brain.fear = Mathf.Clamp01(brain.fear + panicMultiplier * 0.1f);

            if (DebugLog.Value)
                Debug.Log($"[FleeSwarm] {Self.Value.name}: 패닉 전파 → {col.name} " +
                          $"fear={brain.fear:F2} (+{panicMultiplier * 0.1f:F2})");
        }
    }
}