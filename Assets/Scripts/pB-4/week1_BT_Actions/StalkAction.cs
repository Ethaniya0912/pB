// =============================================================================
// StalkAction.cs  |  pB-4 커스텀 BT Action — 타겟 접근
// =============================================================================
// BB 소유권 테이블 (예방 체크리스트 #2):
//   BB["Target"]             Reader — Target != null이면 직접 추적
//   BB["PredictedPosition"]  Reader — Target!=null 시 우선 사용
//   BB["LastHeardPosition"]  Reader — Target==null 수색 모드 1순위 폴백
//                            Writer: PB4DecisionAdapter (Suspicious 수색 모드)
//                            ※ AIPerceptionSystem.lastKnownPosition과 별개!
//   BB["LastSeenPosition"]   Reader — Target==null 수색 모드 2순위 폴백
//   BB["StalkSpeed"]         Reader — 이동 속도
//   BB["EngageRange"]        Reader — CircleStrafe 전환 거리 + stoppingDistance 기준
//                            ※ 수색 모드에서는 EngageRange를 stoppingDistance로 쓰면
//                               소리 위치가 EngageRange 이내일 때 이동하지 않는 버그 발생!
//                               (예방 체크리스트 #6 — stoppingDistance vs 목적지 거리)
//                               Target==null 시 stoppingDistance = 0.5f 고정.
//   BB["TerrainTags"]        Reader — SpookyCave 지형 속도 보정
//
// 타이밍 버그 이력 (예방 체크리스트 참고):
//   Bug: externallyTicked=true 환경에서 BB["LastHeardPosition"]이 (0,0,0)일 때
//        return Status.Failure → BT가 Idle로 폴백. 그래프로 원인 파악 불가.
//        → 해결: PB4DecisionAdapter가 Suspicious 진입 시 BB 직접 push.
//   Bug: stoppingDistance = EngageRange(5m) × 0.9 = 4.5m.
//        소리 위치까지 거리 0.8m < 4.5m → NavMesh "이미 도착" → 이동 없음.
//        → 해결: Target==null 시 stoppingDistance = 0.5f 고정.
// =============================================================================
using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Stalk",
    story: "[Self] stalks toward [Target] at [StalkSpeed] speed within [EngageRange]m",
    category: "pB-4/Attack",
    id: "pb4_stalk_action")]
public partial class StalkAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<Transform> Target;

    [Tooltip("접근 속도. 고블린=5.5, 오크=4.0, 스켈레톤=2.5")]
    [SerializeReference] public BlackboardVariable<float> StalkSpeed = new(4f);

    /// <summary>
    /// Stalk→CircleStrafe 전환 거리.
    /// ★ story의 [EngageRange]에 포함하여 BT 에디터에서 Blackboard 변수와 연결 가능.
    /// </summary>
    [Tooltip("이 거리 이내 진입 시 CircleStrafe로 전환. Blackboard의 EngageRange 변수에 연결할 것.")]
    [SerializeReference] public BlackboardVariable<float> EngageRange = new(5f);

    [SerializeReference] public BlackboardVariable<string> TerrainTags;
    [SerializeReference] public BlackboardVariable<Vector3> PredictedPosition;
    [SerializeReference] public BlackboardVariable<Vector3> LastHeardPosition;
    [SerializeReference] public BlackboardVariable<Vector3> LastSeenPosition;

    // ── [우회/Stuck 실패 신호] ──────────────────────────────────────────────────
    // StalkAction이 우회/Stuck으로 Failure 반환 시 이 BB 변수를 true로 설정.
    // PB4DecisionAdapter가 이 플래그를 읽어 쿨다운 타이머를 시작하고
    // 타이머가 끝날 때까지 UtilityWinner를 "Patrol"로 유지.
    // → Stalk 실패 후 즉시 Attack 재진입하는 깜빡임 방지.
    // BT 에디터에서 BB["StalkBlocked"]에 연결할 것.
    [Tooltip("우회/Stuck 실패 신호. BB[StalkBlocked]에 연결.")]
    [SerializeReference] public BlackboardVariable<bool> StalkBlockedOut = new(false);

    private NavMeshAgent _nav;

    // ── 예방 체크리스트 #7: debugLog 플래그 ─────────────────────────────────
    // true = 수색 모드 진입/목적지/도착 시점을 Console에 출력.
    // 파이프라인 진단 시 켜서 "SetDestination 호출됐는가"를 확인한다.
    // 검증 완료 후 false로 끄세요.
    [SerializeReference] public BlackboardVariable<bool> DebugStalk = new(false);

    // 내부: 마지막 목적지 (디버그용)
    private Vector3 _lastDestination;
    private bool _wasSearchMode;  // 이전 틱의 수색 모드 여부 (첫 진입 로그용)

    // ── Stuck + PathPartial 감지 ─────────────────────────────────────────────
    // Carve된 NavMesh Obstacle 등으로 목적지가 막히면 PathPartial 경로가 생성된다.
    // 몹이 벽 가장자리에서 벽을 계속 비비는 현상 방지용.
    //
    // 감지 방법:
    //   A. PathStatus == PathPartial  → 목적지가 NavMesh 밖이거나 잘려나간 경우
    //   B. velocity ≈ 0 이지만 remainingDistance > 0  → 경로는 있으나 이동 못하는 stuck
    //   두 조건 중 하나가 _stuckThreshold초 이상 지속되면 포기(Failure).
    private float _stuckTimer;
    private Vector3 _lastSetDestination;  // 마지막으로 SetDestination에 넘긴 위치
    private int _debugTickCounter;    // 상시 로그 도배 방지 (N틱에 1회)
    private const int DEBUG_TICK_INTERVAL = 10; // 10틱(약 0.5s)마다 상시 상태 로그
    private const float STUCK_THRESHOLD = 1.5f;  // 이 시간 이상 stuck이면 포기
    private const float MIN_VELOCITY = 0.1f;  // 이 미만이면 사실상 정지로 판단

    // ── 방법 1: 경로 이탈도 체크 (우회 감지) ─────────────────────────────────
    // NavMesh 경로 길이가 직선거리의 DETOUR_RATIO배 이상이면 "모르는 길 우회 중" 판단.
    // 예: 직선 10m인데 경로가 30m → 벽 너머 통로를 찾아 우회하는 것
    // 한계: 굴곡 지형에서 오탐 가능. DETOUR_RATIO 값으로 민감도 조절.
    private const float DETOUR_RATIO = 2.5f;   // 경로/직선 비율 임계값. 클수록 관대.
    // Target 있을 때는 추적이므로 우회 허용 (수색 모드 전용 체크).

    protected override Status OnStart()
    {
        if (Self.Value == null)
        {
            LogFailure("StalkAction: Self가 null입니다.");
            return Status.Failure;
        }
        _nav = Self.Value.GetComponent<NavMeshAgent>();
        if (_nav == null || !_nav.isOnNavMesh)
        {
            LogFailure("StalkAction: NavMeshAgent가 없거나 NavMesh 위에 없습니다.");
            return Status.Failure;
        }
        _lastDestination = Vector3.zero;
        _lastSetDestination = Vector3.zero;
        _wasSearchMode = false;
        _stuckTimer = 0f;
        _debugTickCounter = 0;
        if (DebugStalk.Value)
            Debug.Log(
                $"<color=#44CCFF>[Stalk▶OnStart]</color> {Self.Value?.name}: " +
                $"StalkAction 시작. " +
                $"hasTarget={Target.Value != null} " +
                $"LastHeard={LastHeardPosition.Value:F1} " +
                $"LastSeen={LastSeenPosition.Value:F1} " +
                $"EngageRange={EngageRange.Value:F1}m " +
                $"StalkSpeed={StalkSpeed.Value:F1}");
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_nav == null || !_nav.isOnNavMesh)
            return Status.Failure;

        float engageRange = EngageRange.Value;
        // 안전 가드: 0이하면 기본값 사용 (Blackboard 미연결 시 대비)
        if (engageRange <= 0f) engageRange = 5f;

        Vector3 destination;
        bool hasTarget = Target.Value != null;

        if (hasTarget)
        {
            Transform targetTransform = Target.Value;
            float dist = Vector3.Distance(Self.Value.transform.position, targetTransform.position);

            // EngageRange 이내 → CircleStrafe로 전환
            if (dist <= engageRange)
                return Status.Success;

            Vector3 predicted = PredictedPosition.Value;
            destination = predicted != Vector3.zero ? predicted : targetTransform.position;
        }
        else
        {
            // ── [Gate G-3] 소리/자취 수색 모드 (Target 없음) ──────────────────────
            // LastHeardPosition: AIPerceptionSystem이 소리 감지 시 기록한 위치
            //                    PB4DecisionAdapter가 BB에 직접 push
            // LastSeenPosition:  MemoryPerception이 마지막 목격 위치 기록
            Vector3 heard = LastHeardPosition.Value;
            Vector3 seen = LastSeenPosition.Value;

            if (heard != Vector3.zero) destination = heard;
            else if (seen != Vector3.zero) destination = seen;
            else return Status.Failure;
        }

        // 이동 속도 설정
        float speed = StalkSpeed.Value;
        string tags = TerrainTags.Value ?? "";
        if (tags.Contains("SpookyCave")) speed *= 1.3f;

        _nav.speed = speed;

        // ── stopping distance 분기 ────────────────────────────────────────────
        // Target 있음: EngageRange 바로 앞에서 멈춰 CircleStrafe로 전환
        // Target 없음(수색 모드): 소리/자취 위치 바로 앞까지 이동 (0.5m)
        //   기존 문제: engageRange(5m) × 0.9 = 4.5m를 stopping distance로 사용하면
        //              소리 위치가 4.5m 이내에 있을 때 NavMesh가 "이미 도착"으로 판단,
        //              이동하지 않고 StalkAction이 Running만 유지됨.
        //   수정: 수색 모드에서는 0.5m로 고정해 실제로 소리 위치까지 걸어감.
        //   (예방 체크리스트 #6 — stoppingDistance vs 목적지 거리 확인 필수)
        if (hasTarget)
            _nav.stoppingDistance = engageRange * 0.9f;
        else
            _nav.stoppingDistance = 0.5f;

        // ── SetDestination 호출 전략 ─────────────────────────────────────────────
        // 추적 모드(hasTarget): 타겟이 움직이므로 매 틱 갱신. 단 0.3m 이상 변할 때만.
        // 수색 모드(!hasTarget): 목적지 고정. 0.5m 이상 변할 때만 재호출.
        //   (수색 모드에서만 DETOUR 체크를 하므로 pathPending 안정이 필요)
        float _destThreshold = hasTarget ? 0.3f : 0.5f;
        if (Vector3.Distance(destination, _lastSetDestination) > _destThreshold)
        {
            _nav.SetDestination(destination);
            _lastSetDestination = destination;
        }

        // ── 방법 1: 경로 이탈도 체크 — 추적/수색 모드 모두 적용 ─────────────────
        // [수정] 기존에 !hasTarget(수색 모드)에서만 체크했으나,
        //        로그에서 추적 모드(hasTarget=true)에서도 9.1x 우회가 발생함 확인.
        //        → hasTarget 조건 제거, 두 모드 모두 체크.
        //
        // 감지: 경로 길이 > 직선거리 × DETOUR_RATIO → Failure → Patrol 복귀.
        if (!_nav.pathPending
            && _nav.pathStatus == NavMeshPathStatus.PathComplete)
        {
            float straightDist = Vector3.Distance(
                Self.Value.transform.position, destination);
            float pathLen = CalculatePathLength(_nav.path);

            if (straightDist > 0.5f && pathLen > straightDist * DETOUR_RATIO)
            {
                if (DebugStalk.Value)
                    Debug.LogWarning(
                        $"<color=#FF9944>[Stalk▶DETOUR]</color> {Self.Value?.name}: " +
                        $"우회 경로 감지 ({(hasTarget ? "추적" : "수색")} 모드). " +
                        $"직선={straightDist:F1}m 경로={pathLen:F1}m " +
                        $"비율={pathLen / straightDist:F1}x (임계={DETOUR_RATIO}x). " +
                        $"Failure → Patrol 복귀.");
                _nav.ResetPath();

                // ── Attack 분기 전체 탈출 ────────────────────────────────────────
                // UtilityWinner = "Patrol" → Observer Abort 즉시 발동
                // → Try In Order에서 CircleStrafe/Strike로 넘어가지 않음
                // BT 에디터에서 UtilityWinnerOut 포트를 BB["UtilityWinner"]에 연결 필요.
                // StalkBlocked = true → PB4DecisionAdapter가 쿨다운 시작
                // Adapter가 쿨다운 동안 UtilityWinner="Patrol" 유지
                // → 0.5s 틱마다 Attack으로 다시 뒤집히는 깜빡임 방지
                if (StalkBlockedOut != null)
                    StalkBlockedOut.Value = true;

                if (DebugStalk.Value)
                    Debug.LogWarning(
                        $"<color=#FF9944>[Stalk▶DETOUR_EXIT]</color> {Self.Value?.name}: " +
                        $"BB[StalkBlocked]=true → Adapter 쿨다운 시작. " +
                        $"쿨다운 동안 Attack 재진입 차단.");

                return Status.Failure;
            }
        }

        // ── PathPartial + Stuck 감지 (벽 비빔 방지) ──────────────────────────────
        // Carve된 NavMesh Obstacle이 목적지를 막으면 PathStatus가 PathPartial이 된다.
        // 이 경우 몹은 벽 가장자리까지 가서 계속 벽을 비빈다.
        // 해결: PathPartial이거나 velocity≈0(stuck) 상태가 STUCK_THRESHOLD초 지속되면
        //       Failure를 반환해 Patrol 분기로 복귀한다.
        bool isPathPartial = !_nav.pathPending
                          && _nav.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathPartial;
        bool isStuck = !_nav.pathPending
                          && _nav.remainingDistance > _nav.stoppingDistance
                          && _nav.velocity.magnitude < MIN_VELOCITY;

        if (isPathPartial || isStuck)
        {
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer >= STUCK_THRESHOLD)
            {
                if (DebugStalk.Value)
                    Debug.LogWarning(
                        $"<color=#FF4455>[Stalk▶STUCK]</color> {Self.Value?.name}: " +
                        $"{(isPathPartial ? "PathPartial — 목적지가 Carve된 벽으로 막힘" : "Stuck — 이동 불가")}. " +
                        $"remainingDist={_nav.remainingDistance:F1}m vel={_nav.velocity.magnitude:F2}m/s. " +
                        $"Failure 반환 → Patrol 복귀.");
                _nav.ResetPath(); // 벽 비빔 즉시 중단
                if (StalkBlockedOut != null)
                    StalkBlockedOut.Value = true;
                if (DebugStalk.Value)
                    Debug.LogWarning(
                        $"<color=#FF4455>[Stalk▶STUCK_EXIT]</color> {Self.Value?.name}: " +
                        $"BB[StalkBlocked]=true → Adapter 쿨다운 시작.");
                return Status.Failure;
            }
        }
        else
        {
            // 정상 이동 중이면 타이머 리셋
            _stuckTimer = 0f;
        }

        // ── 강화 디버그 로그 ─────────────────────────────────────────────────────
        if (DebugStalk.Value)
        {
            bool isSearchMode = !hasTarget;
            _debugTickCounter++;

            // ① 수색 모드 첫 진입
            if (isSearchMode && !_wasSearchMode)
                Debug.Log(
                    $"<color=#44CCFF>[Stalk▶SEARCH_START]</color> {Self.Value?.name}\n" +
                    $"  목적지={destination:F1}  stoppingDist={_nav.stoppingDistance:F1}m\n" +
                    $"  LastHeard={LastHeardPosition.Value:F1}  LastSeen={LastSeenPosition.Value:F1}\n" +
                    $"  pathStatus={_nav.pathStatus}  pathPending={_nav.pathPending}  hasPath={_nav.hasPath}");

            // ② 타겟 발견 → 수색 종료
            if (!isSearchMode && _wasSearchMode)
                Debug.Log(
                    $"<color=#44FF88>[Stalk▶TARGET_FOUND]</color> {Self.Value?.name}: " +
                    $"수색 → 추적 전환. Target={Target.Value?.name ?? "null"}");

            // ③ 매 N틱마다 상세 상태 (도배 방지)
            if (_debugTickCounter % DEBUG_TICK_INTERVAL == 0)
            {
                float straight = Vector3.Distance(Self.Value.transform.position, destination);
                float pathLen = CalculatePathLength(_nav.path);
                float ratio = straight > 0.1f ? pathLen / straight : 0f;

                // pathStatus 색상
                string psColor = _nav.pathStatus == NavMeshPathStatus.PathComplete ? "#44FF88"
                               : _nav.pathStatus == NavMeshPathStatus.PathPartial ? "#FFDD44"
                               : "#FF4455";
                string psLabel = $"<color={psColor}>{_nav.pathStatus}</color>";

                // 우회 비율 색상
                string ratioColor = ratio > DETOUR_RATIO ? "#FF4455"
                                  : ratio > 1.5f ? "#FFDD44"
                                  : "#44FF88";
                string ratioLabel = ratio > 0.1f
                    ? $"<color={ratioColor}>{ratio:F1}x</color> (임계={DETOUR_RATIO}x)"
                    : "계산중";

                Debug.Log(
                    $"<color=#88DDFF>[Stalk▶STATUS]</color> {Self.Value?.name}\n" +
                    $"  모드={(isSearchMode ? "<color=#44CCFF>수색</color>" : "<color=#44FF88>추적</color>")}  " +
                    $"목적지={destination:F1}\n" +
                    $"  pathStatus={psLabel}  pathPending={_nav.pathPending}  hasPath={_nav.hasPath}\n" +
                    $"  직선={straight:F1}m  경로={pathLen:F1}m  우회비율={ratioLabel}\n" +
                    $"  remaining={_nav.remainingDistance:F1}m  vel={_nav.velocity.magnitude:F2}m/s  " +
                    $"stuckTimer={_stuckTimer:F1}s\n" +
                    $"  lastSetDest={_lastSetDestination:F1}  stoppingDist={_nav.stoppingDistance:F1}m");
            }

            _lastDestination = destination;
            _wasSearchMode = isSearchMode;
        }

        return Status.Running;
    }

    protected override void OnEnd()
    {
        if (DebugStalk.Value && _wasSearchMode)
            Debug.Log(
                $"<color=#FF9944>[Stalk▶SEARCH_END]</color> {Self.Value?.name}: " +
                $"수색 종료. 마지막 목적지={_lastDestination:F1}");
        if (_nav != null) _nav.stoppingDistance = 0f; // 다음 Action을 위해 초기화
    }

    // ── CalculatePathLength: NavMesh 경로의 실제 길이 계산 ───────────────────
    // path.corners 배열의 연속 꼭짓점 사이 거리를 합산한다.
    // NavMesh.CalculatePath()로 생성된 경로의 길이를 직선거리와 비교하는 데 사용.
    private static float CalculatePathLength(UnityEngine.AI.NavMeshPath path)
    {
        if (path == null || path.corners.Length < 2) return 0f;
        float len = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return len;
    }
}