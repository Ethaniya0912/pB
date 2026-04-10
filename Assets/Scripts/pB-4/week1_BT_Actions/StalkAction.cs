// =============================================================================
// StalkAction.cs  |  pB-4 커스텀 BT Action — 타겟 접근
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

    private NavMeshAgent _nav;

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
        _nav.stoppingDistance = engageRange * 0.9f; // NavMesh가 EngageRange 바로 앞에서 멈추도록
        _nav.SetDestination(destination);

        return Status.Running;
    }

    protected override void OnEnd()
    {
        if (_nav != null) _nav.stoppingDistance = 0f; // 다음 Action을 위해 초기화
    }
}