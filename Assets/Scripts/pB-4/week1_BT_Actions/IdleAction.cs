// =============================================================================
// IdleAction.cs  |  pB-4 커스텀 BT Action — 정지 대기 (폴백)
// 패키지  : Unity Behavior 1.0.15 (com.unity.behavior)
//
// 역할:
//   TryInOrder의 마지막 자식 (Guard 없음).
//   다른 모든 분기가 실패했을 때 실행되는 안전한 폴백입니다.
//   이동을 정지하고 대기합니다.
//   유틸리티가 새로운 행동을 선택하면 (UtilityWinner 변경)
//   Observer Abort 또는 다음 프레임 재평가로 즉시 전환됩니다.
//
// [버그 수정 - Bug 1]
//   기존: OnUpdate()가 Status.Running을 영구 반환
//   문제: Try In Order는 현재 실행 중인 자식이 완료(Success/Failure)될 때까지
//         다른 자식을 재평가하지 않는다. Running이 영구 반환되면
//         Try In Order가 완료되지 않아 On Start(Repeat=true)도 재시작되지 않고,
//         BB값이 변경되어도 Flee/Attack/Patrol 분기가 절대 실행되지 않는다.
//   수정: OnUpdate()에서 Status.Success를 반환하도록 변경.
//         → Try In Order 완료 → On Start(Repeat=true) 재시작
//         → Try In Order가 처음부터 조건을 재평가
//         → UtilityWinner가 변경된 경우 올바른 분기로 전환됨.
// =============================================================================
using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Idle",
    story: "[Self] idles in place",
    category: "pB-4",
    id: "pb4_idle_action")]
public partial class IdleAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    private NavMeshAgent _nav;

    protected override Status OnStart()
    {
        if (Self.Value == null)
        {
            LogFailure("IdleAction: Self가 null입니다.");
            return Status.Failure;
        }

        _nav = Self.Value.GetComponent<NavMeshAgent>();

        // 이동 중이면 정지
        if (_nav != null && _nav.isOnNavMesh && _nav.hasPath)
            _nav.ResetPath();

        return Status.Running;
    }

    /// <summary>
    /// [버그 수정 - Bug 1] Running → Success로 변경.
    ///
    /// 기존 동작:
    ///   Status.Running을 영구 반환 → Try In Order가 완료되지 않음
    ///   → On Start(Repeat=true)가 재시작되지 않음
    ///   → BB값(UtilityWinner)이 변경되어도 다른 분기가 절대 선택되지 않음
    ///
    /// 수정 후 동작:
    ///   Status.Success 반환 → Try In Order 완료
    ///   → On Start(Repeat=true)가 트리를 재시작
    ///   → Try In Order가 Attack/Flee/Patrol 조건을 처음부터 재평가
    ///   → UtilityWinner == "Flee"이면 Flee 분기가 정상 실행됨
    ///
    /// 주의: Idle 상태에서 1 tick(프레임)마다 Success를 반환하므로
    ///   Try In Order가 매 프레임 조건을 재평가한다.
    ///   연산 부담이 걱정된다면 OnStart()에 짧은 타이머(예: 0.1f)를
    ///   두고 타이머 완료 시 Success를 반환하는 방식도 가능하다.
    /// </summary>
    protected override Status OnUpdate()
    {
        // 이동이 다시 시작되었으면 정지
        if (_nav != null && _nav.isOnNavMesh && _nav.hasPath)
            _nav.ResetPath();

        // [Bug 1 수정] Running → Success
        // Try In Order가 완료되어 On Start(Repeat=true)가 트리를 재시작하고
        // Attack/Flee/Patrol 조건을 처음부터 재평가하도록 한다.
        return Status.Success;
    }
}