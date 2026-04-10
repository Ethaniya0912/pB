// =============================================================================
// HasTargetCondition.cs  |  pB-4 커스텀 BT Condition
// 패키지  : Unity Behavior 1.0.15 (com.unity.behavior)
//
// 역할:
//   Conditional Guard에 할당하여 HasTarget(bool)을 확인합니다.
//   Attack Guard에서 IsUtilityWinner AND HasTarget으로 조합합니다.
//
// 사용 예:
//   Attack Guard → Continue if: All Are True
//     → IsUtilityWinner(ActionName='Attack')
//     → HasTarget
//   → 둘 다 True일 때만 Attack Sequence 진입.
//
// Observer Abort와 함께 사용:
//   Attack 실행 중 타겟이 소실되면(HasTarget=false)
//   Observer Abort가 Attack Sequence를 즉시 중단합니다.
// =============================================================================
using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "HasTarget",
    story: "Has a valid [Target]",
    category: "pB-4/Conditions",
    id: "pb4_has_target_condition")]
public partial class HasTargetCondition : Condition
{
    /// <summary>
    /// 타겟 존재 여부. PB4DecisionAdapter가 0.5초마다 갱신.
    /// brain.currentTarget != null → true.
    /// </summary>
    [Tooltip("타겟이 존재하면 true")]
    [SerializeReference] public BlackboardVariable<bool> HasTarget;

    /// <summary>
    /// HasTarget == true이면 조건 충족.
    /// </summary>
    public override bool IsTrue()
    {
        return HasTarget.Value;
    }
}
