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

    /// <summary>현재 지형 태그. NarrowPath 변조에 사용.</summary>
    [SerializeReference] public BlackboardVariable<string> TerrainTags;

    // =========================================================================
    // 내부 상태
    // =========================================================================
    private NavMeshAgent _nav;
    private Transform _selfTransform;

    /// <summary>배회 각도 (0~360). 매 프레임 angularSpeed×dt만큼 증가.</summary>
    private float _periAngle;

    /// <summary>배회 경과 시간. triggerTime 도달 시 Strike 전환.</summary>
    private float _strafeTimer;

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

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_nav == null || !_nav.isOnNavMesh || Target.Value == null)
            return Status.Failure;

        // ── 지형 변조 적용 ──
        float radius = OrbitRadius.Value;
        float triggerTime = StrikeTriggerTime.Value;
        string tags = TerrainTags.Value ?? "";

        if (tags.Contains("NarrowPath"))
        {
            radius *= 0.4f;        // 좁은 통로: 반경 60% 축소
            triggerTime *= 0.5f;    // 배회 시간 50% 단축 → 빠른 Strike
        }

        // ── periAngle 갱신 ──
        _periAngle += StrafeAngularSpeed.Value * Time.deltaTime;
        _strafeTimer += Time.deltaTime;

        // ── 궤도 위치 계산 ──
        // 타겟 위치를 중심으로 periAngle 방향, radius 거리에 목적지 설정
        Vector3 targetPos = Target.Value.position;
        float rad = _periAngle * Mathf.Deg2Rad;
        Vector3 orbitOffset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;
        Vector3 orbitPos = targetPos + orbitOffset;

        // ── NavMesh 유효성 확인 ──
        // SamplePosition으로 가장 가까운 NavMesh 위치를 찾음 (3m 범위)
        if (NavMesh.SamplePosition(orbitPos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            _nav.SetDestination(hit.position);
        }
        else
        {
            // NavMesh 밖 → 벽 끼임 방지. 즉시 Strike로 강제 전환 [R1 대응]
            Debug.LogWarning($"[BT] {Self.Value.name}: CircleStrafe orbit({orbitPos})이 NavMesh 밖. Strike 강제 전환.");
            return Status.Success;
        }

        // ── 타겟 방향 회전 (배회 중에도 타겟을 바라봄) ──
        Vector3 lookDir = targetPos - _selfTransform.position;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            _selfTransform.rotation = Quaternion.Slerp(_selfTransform.rotation, targetRot, Time.deltaTime * 5f);
        }

        // ── 타이머 만료 → Strike 전환 ──
        if (_strafeTimer >= triggerTime)
            return Status.Success;

        return Status.Running;
    }

    protected override void OnEnd()
    {
        _strafeTimer = 0f;
        // periAngle은 유지 (다음 CircleStrafe에서 궤도 연속)
    }
}