// =============================================================================
// CircleStrafeAction.cs  |  pB-4 커스텀 BT Action — 타겟 주위 배회
// 패키지  : Unity Behavior 1.0.15 (com.unity.behavior)
//
// 역할:
//   타겟을 중심으로 원형 궤도를 배회합니다.
//   periAngle이 매 프레임 증가하며 궤도 위치가 회전합니다.
//   strafeTimer가 StrikeTriggerTime에 도달하면 Success(→Strike 전환).
//
// 지형 변조:
//   NarrowPath → orbitRadius × 0.4, strikeTriggerTime × 0.5
//   (좁은 통로에서 배회 불가 → 빠른 공격 전환)
//
// 안전장치:
//   NavMesh.SamplePosition 실패 → 즉시 Success (Strike 강제 전환)
//   → 벽 끼임 방지 [위험 R1 대응]
//
// [버그 수정 — Bug 1: 9.5m에서 허공에 칼질]
//   기존 문제:
//     _strafeTimer >= StrikeTriggerTime 조건만 만족하면 실제 거리에 관계없이
//     즉시 Strike로 전환했음. Stalk이 EngageRange(10m) 경계(예: dist=9.5m)에서
//     Success를 반환하면 CircleStrafe가 진입 즉시 타이머를 시작하고,
//     2초 후 dist=9.5m 상태에서 Strike를 실행 → 허공 칼질 발생.
//     또한 AttackRange가 BB에 push되지 않아 ActionID 별
//     minimumAttackDistance/maximumAttackDistance 검증도 무의미했음.
//
//   수정:
//     AttackRange BlackboardVariable 추가.
//     타이머 만료 시 dist <= AttackRange 조건을 동시에 검사.
//     거리가 아직 멀면 배회를 유지하면서 orbitRadius를 점진적으로 축소해
//     타겟에게 접근. CloseInMaxTime.Value 초 이내에도 AttackRange에 도달 못하면
//     Strike 강제 전환(기존 NavMesh 밖 처리와 동일 안전장치).
//     → PB4DecisionAdapter.InitializeBlackboard()에서
//        SetBB("AttackRange", combatProfile.attackRange) 추가 필요.
// =============================================================================
using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "CircleStrafe",
    story: "[Self] circles around [Target] with [OrbitRadius] radius",
    category: "pB-4/Attack",
    id: "pb4_circlestrafe_action")]
public partial class CircleStrafeAction : Action
{
    // =========================================================================
    // Blackboard 변수
    // =========================================================================

    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<Transform> Target;

    /// <summary>배회 반경 (m). 고블린=3, 오크=4, 스켈레톤=5.</summary>
    [Tooltip("타겟 중심 배회 반경. NarrowPath 시 ×0.4로 축소")]
    [SerializeReference] public BlackboardVariable<float> OrbitRadius = new(4f);

    /// <summary>배회 각속도 (°/s). 고블린=80, 오크=40, 스켈레톤=20.</summary>
    [Tooltip("초당 회전 각도. 클수록 빠르게 배회")]
    [SerializeReference] public BlackboardVariable<float> StrafeAngularSpeed = new(40f);

    /// <summary>배회→공격 전환 대기 시간 (초). 고블린=0.5, 오크=2.0, 스켈레톤=∞.</summary>
    [Tooltip("이 시간 경과 후 Strike로 전환. Infinity=포위 완성까지 대기")]
    [SerializeReference] public BlackboardVariable<float> StrikeTriggerTime = new(2f);

    /// <summary>
    /// 공격 사거리 (m). StrikeTriggerTime 만료 후 이 거리 이하일 때만 Strike 전환.
    /// PB4DecisionAdapter 가 combatProfile.attackRange 를 BB 에 push 합니다.
    ///
    /// [Bug 1 수정] StrikeTriggerTime 만료만으로는 Strike 로 전환하지 않습니다.
    /// dist <= AttackRange 가 동시에 성립해야 StrikeAction 이 실행됩니다.
    /// 성립하지 않으면 orbitRadius 를 점진적으로 줄여 접근을 계속합니다.
    /// </summary>
    [Tooltip("공격 유효 사거리(m). StrikeTriggerTime 만료 후 이 거리 이하여야 Strike 전환.\n" +
             "PB4DecisionAdapter → BB AttackRange → 이 변수로 연결하세요.")]
    [SerializeReference] public BlackboardVariable<float> AttackRange = new(2.5f);

    /// <summary>현재 지형 태그. NarrowPath 변조에 사용.</summary>
    [SerializeReference] public BlackboardVariable<string> TerrainTags;

    [SerializeReference] public BlackboardVariable<float> NarrowPathOrbitMult = new(0.4f);
    [SerializeReference] public BlackboardVariable<float> NarrowPathStrikeTimeMult = new(0.5f);

    // =========================================================================
    // 내부 상태
    // =========================================================================
    private NavMeshAgent _nav;
    private Transform _selfTransform;

    /// <summary>배회 각도 (0~360). 매 프레임 angularSpeed×dt만큼 증가.</summary>
    private float _periAngle;

    /// <summary>배회 경과 시간. triggerTime 도달 시 Strike 전환.</summary>
    private float _strafeTimer;

    /// <summary>
    /// [Bug 1 수정] StrikeTriggerTime 만료 후 AttackRange 미도달 시
    /// orbitRadius 를 줄이며 접근하는 시간. 이 값 초과 시 Strike 강제 전환.
    /// </summary>
    private float _closeInTimer;

    /// <summary>orbitRadius 축소 접근의 최대 허용 시간 (초).</summary>
    [SerializeReference] public BlackboardVariable<float> CloseInMaxTime = new(3f);

    /// <summary>
    /// 궤도 회전 방향. +1=반시계, -1=시계.
    /// OnStart에서 랜덤 결정. 페인트 시 반전.
    /// </summary>
    private float _orbitDir = 1f;

    /// <summary>페인트(방향 전환)까지 남은 시간. 만료 시 _orbitDir 반전.</summary>
    private float _feintTimer;

    /// <summary>페인트 최소 간격 (초).</summary>
    [SerializeReference] public BlackboardVariable<float> FeintIntervalMin = new(2f);

    /// <summary>페인트 최대 간격 (초).</summary>
    [SerializeReference] public BlackboardVariable<float> FeintIntervalMax = new(5f);

    /// <summary>
    /// [Bug 2 수정] OnStart에서 계산된 실제 Strike 전환 대기 시간.
    /// StrikeTriggerTime ± 랜덤 지터를 적용해 반복성을 제거합니다.
    /// </summary>
    private float _actualTriggerTime;

    /// <summary>가드 반응: 직전 틱의 타겟 가드 상태 캐시.</summary>
    private bool _wasTargetGuarding;

    /// <summary>가드 반응: 타겟의 CharacterManager 캐시 (OnStart 1회 획득).</summary>
    private CharacterManager _targetCharMgr;

    /// <summary>가드 해제 후 공격까지의 짧은 반응 딜레이 (초).</summary>
    [SerializeReference] public BlackboardVariable<float> GuardBreakReactionDelay = new(0.25f);

    /// <summary>
    /// 가드 중 포이즈 공격 타이머.
    /// 타겟이 가드를 유지하는 시간을 누적합니다.
    /// </summary>
    private float _guardAttackTimer;

    /// <summary>
    /// 다음 포이즈 공격까지 대기할 시간 (초).
    /// OnStart와 포이즈 공격 직후 Random.Range로 재계산됩니다.
    /// </summary>
    private float _nextPoiseAttackInterval;

    /// <summary>포이즈 공격 최소 대기 (초).</summary>
    [SerializeReference] public BlackboardVariable<float> PoiseAttackIntervalMin = new(2.5f);

    /// <summary>포이즈 공격 최대 대기 (초).</summary>
    [SerializeReference] public BlackboardVariable<float> PoiseAttackIntervalMax = new(5.5f);

    // =========================================================================
    // 생명주기
    // =========================================================================

    protected override Status OnStart()
    {
        if (Self.Value == null || Target.Value == null)
        {
            LogFailure("CircleStrafeAction: Self 또는 Target이 null입니다.");
            return Status.Failure;
        }

        _nav = Self.Value.GetComponent<NavMeshAgent>();
        _selfTransform = Self.Value.transform;

        if (_nav == null || !_nav.isOnNavMesh)
        {
            LogFailure("CircleStrafeAction: NavMeshAgent가 없거나 NavMesh 밖입니다.");
            return Status.Failure;
        }

        // 시작 시 현재 각도를 타겟→자신 방향에서 계산 (연속적 궤도)
        Vector3 toSelf = _selfTransform.position - Target.Value.position;
        toSelf.y = 0f;
        _periAngle = Mathf.Atan2(toSelf.z, toSelf.x) * Mathf.Rad2Deg;
        _strafeTimer = 0f;
        _closeInTimer = 0f;  // [Bug 1 수정] 접근 타이머 초기화

        // 가드 반응: 타겟 CharacterManager 캐시
        _targetCharMgr = Target.Value != null
            ? Target.Value.GetComponent<CharacterManager>()
            : null;
        _wasTargetGuarding = false;
        _guardAttackTimer = 0f;
        _nextPoiseAttackInterval = UnityEngine.Random.Range(PoiseAttackIntervalMin.Value, PoiseAttackIntervalMax.Value);

        // [Bug 2 수정] Strike 전환 타이밍에 랜덤 지터 적용
        // StrikeTriggerTime(2초) ± jitter → 패턴 예측 불가
        // jitter 범위: -0.4초 ~ +1.2초 (하한은 최솟값 0.5초 보장)
        float jitter = UnityEngine.Random.Range(-0.4f, 1.2f);
        _actualTriggerTime = Mathf.Max(0.5f, StrikeTriggerTime.Value + jitter);

        // 궤도 방향 랜덤 결정 + 페인트 타이머 초기화
        _orbitDir = UnityEngine.Random.value > 0.5f ? 1f : -1f;
        _feintTimer = UnityEngine.Random.Range(FeintIntervalMin.Value, FeintIntervalMax.Value);

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_nav == null || !_nav.isOnNavMesh || Target.Value == null)
            return Status.Failure;

        Vector3 targetPos = Target.Value.position;
        float dist = Vector3.Distance(_selfTransform.position, targetPos);
        float attackRange = AttackRange.Value > 0f ? AttackRange.Value : 2.5f;

        // ── 지형 변조 ──────────────────────────────────────────────────────────
        float radius = OrbitRadius.Value;
        float triggerTime = _actualTriggerTime;   // 랜덤 지터 적용된 값
        string tags = TerrainTags.Value ?? "";
        if (tags.Contains("NarrowPath"))
        {
            radius *= NarrowPathOrbitMult.Value;
            triggerTime *= NarrowPathStrikeTimeMult.Value;
        }

        // ── 타이머 누적 ────────────────────────────────────────────────────────
        _strafeTimer += Time.deltaTime;

        // ── 가드 반응 ────────────────────────────────────────────────────────────
        bool isTargetGuarding = _targetCharMgr != null
            && _targetCharMgr.characterDefenseManager != null
            && _targetCharMgr.characterDefenseManager.isDefending;

        if (isTargetGuarding)
        {
            _guardAttackTimer += Time.deltaTime;
            if (_guardAttackTimer >= _nextPoiseAttackInterval)
            {
                _actualTriggerTime = _strafeTimer + GuardBreakReactionDelay.Value;
                _guardAttackTimer = 0f;
                _nextPoiseAttackInterval = UnityEngine.Random.Range(PoiseAttackIntervalMin.Value, PoiseAttackIntervalMax.Value);
                Debug.Log($"[CircleStrafe] {Self.Value.name}: 포이즈 공격 시도. " +
                          $"다음 포이즈 간격={_nextPoiseAttackInterval:F1}s");
            }
            else
            {
                _actualTriggerTime = _strafeTimer + 1.0f;
            }
        }
        else if (_wasTargetGuarding)
        {
            _actualTriggerTime = _strafeTimer + GuardBreakReactionDelay.Value;
            _guardAttackTimer = 0f;
            _nextPoiseAttackInterval = UnityEngine.Random.Range(PoiseAttackIntervalMin.Value, PoiseAttackIntervalMax.Value);
            Debug.Log($"[CircleStrafe] {Self.Value.name}: 가드 해제 → {GuardBreakReactionDelay.Value}s 후 Strike.");
        }
        _wasTargetGuarding = isTargetGuarding;
        // triggerTime 재동기화 (가드 반응이 _actualTriggerTime을 변경했을 수 있음)
        triggerTime = _actualTriggerTime;

        // ── Strike 전환 판정 + closeIn radius 계산 ──────────────────────────────
        // [핵심 수정] radius 축소를 SetDestination 호출 전에 계산합니다.
        // 기존 코드는 SetDestination 후에 radius를 수정하여 실제 이동에 반영되지 않았음.
        bool inCloseIn = false;
        if (_strafeTimer >= triggerTime)
        {
            if (dist <= attackRange)
                return Status.Success;  // ✅ AttackRange 도달 → Strike

            // AttackRange 밖 → closeIn 진입
            inCloseIn = true;
            _closeInTimer += Time.deltaTime;

            if (_closeInTimer >= CloseInMaxTime.Value)
            {
                // closeIn 최대 시간 초과: dist를 재확인하고 그래도 멀면 Failure
                // (Failure → BT Sequence 재시작 → Stalk→CircleStrafe 재진입)
                // 기존: 무조건 Success → StrikeAction에서 멍때림
                Debug.LogWarning($"[BT] {Self.Value.name}: closeIn {CloseInMaxTime.Value}s 초과 " +
                                 $"(dist={dist:F1}m). CircleStrafe 재시작.");
                return Status.Failure;
            }

            // closeIn: orbitRadius를 0으로 수렴시켜 타겟 방향으로 직접 접근.
            // periAngle 회전을 멈춰 사선 이동을 최소화하고 직진 접근을 극대화.
            radius *= Mathf.Clamp01(1f - _closeInTimer / CloseInMaxTime.Value);
        }

        // ── 페인트 (궤도 방향 전환) ────────────────────────────────────────────
        _feintTimer -= Time.deltaTime;
        if (_feintTimer <= 0f && !inCloseIn)
        {
            _orbitDir *= -1f;
            _feintTimer = UnityEngine.Random.Range(FeintIntervalMin.Value, FeintIntervalMax.Value);
#if UNITY_EDITOR
            Debug.Log($"[CS] {Self.Value.name}: 페인트 전환 dir={_orbitDir:+0;-0} nextTimer={_feintTimer:F2}s (min={FeintIntervalMin.Value} max={FeintIntervalMax.Value})");
#endif
        }

        // ── 이동 방향 계산 (접선+방사 블렌드) ───────────────────────────────────
        // 핵심 아이디어: 목적지를 '궤도 원 위의 점'이 아니라
        // '접선(측면) + 방사(타겟 방향)의 블렌드' 방향으로 설정합니다.
        //
        // 결과:
        //   dist >> orbitRadius : 방사 성분 ↑ → 타겟 방향 접근 (비스듬히)
        //   dist ≈ orbitRadius  : 접선 성분 ↑ → 측면 이동 (선회)
        //   dist < orbitRadius  : 순수 접선  → 완전 측면 (이탈 방지)
        //
        // 이를 통해 오크는 멀리서 비스듬히 접근하다가
        // 궤도 반경에 도달하면 자연스럽게 측면으로 전환됩니다.

        Vector3 selfPos = _selfTransform.position;
        Vector3 toTarget = (targetPos - selfPos);
        toTarget.y = 0f;
        float distFlat = toTarget.magnitude;

        Vector3 destPos;
        if (distFlat > 0.1f)
        {
            Vector3 toTargetNorm = toTarget / distFlat;

            // 방사 성분: 타겟 방향 (dist > orbitRadius일 때 사용)
            Vector3 radial = toTargetNorm;

            // 접선 성분: 타겟→자신 방향의 90° 회전 (_orbitDir=+1 반시계, -1 시계)
            Vector3 tangent = new Vector3(
                -toTargetNorm.z * _orbitDir,
                0f,
                 toTargetNorm.x * _orbitDir);

            // 블렌드 비율: orbit_r/dist를 접선 비율로
            // dist=3m(orbit), orbitRatio=1 → 완전 접선
            // dist=8m,        orbitRatio=0.375 → 접선 37% + 방사 63%
            float orbitRatio = Mathf.Clamp01(radius / distFlat);
            Vector3 blendDir = (tangent * orbitRatio + radial * (1f - orbitRatio)).normalized;

            // inCloseIn 시에는 방사 성분을 더 강화 (직진 접근)
            if (inCloseIn)
            {
                float closeT = Mathf.Clamp01(_closeInTimer / CloseInMaxTime.Value);
                blendDir = Vector3.Lerp(blendDir, toTargetNorm, closeT * 0.6f).normalized;
            }

            // NavMesh 위에서 이동 가능한 목적지 계산
            // LOOK_AHEAD: 오크 이동 속도 × 0.5초 앞의 목적지
            float lookAhead = Mathf.Max(radius, distFlat - radius + 1f);
            destPos = selfPos + blendDir * lookAhead;
        }
        else
        {
            // 타겟에 너무 가까움 → 접선 방향으로만 이동
            Vector3 tangent90 = new Vector3(-_orbitDir, 0f, 0f);
            destPos = selfPos + tangent90 * radius;
        }

        // periAngle은 시각적 연속성을 위해 계속 갱신 (이제 실제 이동에는 미사용)
        _periAngle += StrafeAngularSpeed.Value * _orbitDir * Time.deltaTime;

        if (NavMesh.SamplePosition(destPos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            _nav.SetDestination(hit.position);
        }
        else
        {
            // NavMesh 밖이면 타겟 방향 직접 접근
            _nav.SetDestination(targetPos);
        }

        // ── 타겟 방향 회전 ──────────────────────────────────────────────────────
        Vector3 lookDir = targetPos - _selfTransform.position;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            _selfTransform.rotation = Quaternion.Slerp(
                _selfTransform.rotation, targetRot, Time.deltaTime * 5f);
        }

        return Status.Running;
    }

    protected override void OnEnd()
    {
        _strafeTimer = 0f;
        _closeInTimer = 0f;  // [Bug 1 수정] 접근 타이머도 초기화
        _actualTriggerTime = 0f;
        _wasTargetGuarding = false;
        _targetCharMgr = null;
        _guardAttackTimer = 0f;
        _feintTimer = 0f;
        // periAngle은 유지 (다음 CircleStrafe에서 궤도 연속)
    }
}