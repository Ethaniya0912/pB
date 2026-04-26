// =============================================================================
// IsUtilityWinnerCondition.cs  |  pB-4 커스텀 BT Condition
// 패키지  : Unity Behavior 1.0.15 (com.unity.behavior)
//
// 역할:
//   Conditional Guard에 할당하여 UtilityWinner 문자열을 비교합니다.
//   UtilityWinner == ActionName이면 True → 해당 분기 실행.
//
// 사용 예:
//   Attack Guard → IsUtilityWinner(ActionName='Attack')
//   Flee Guard   → IsUtilityWinner(ActionName='Flee')
//   Patrol Guard → IsUtilityWinner(ActionName='Patrol')
//
// Conditional Guard Inspector에서:
//   Assign Condition → pB-4/Conditions → IsUtilityWinner
//   ActionName 필드에 'Attack'/'Flee'/'Patrol' 입력
//
// 참고:
//   Behavior 1.0.15에서 Condition은 Condition 클래스를 상속합니다.
//   OnStart()에서 true/false를 반환합니다.
// =============================================================================
using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

[Serializable, GeneratePropertyBag]
[Condition(
    name: "IsUtilityWinner",
    story: "UtilityWinner equals [ActionName]",
    category: "pB-4/Conditions",
    id: "pb4_is_utility_winner_condition")]
public partial class IsUtilityWinnerCondition : Condition
{
    /// <summary>
    /// 비교할 행동 이름. Inspector에서 'Attack', 'Flee', 'Patrol' 중 하나 입력.
    /// Blackboard 변수로 연결하거나 직접 문자열 입력 가능.
    /// </summary>
    [Tooltip("비교 대상: 'Attack', 'Flee', 'Patrol' 중 하나")]
    [SerializeReference] public BlackboardVariable<string> ActionName;

    /// <summary>
    /// 유틸리티 AI가 선택한 현재 승자.
    /// PB4DecisionAdapter가 0.5초마다 Blackboard에 갱신합니다.
    /// </summary>
    [SerializeReference] public BlackboardVariable<string> UtilityWinner;

    /// <summary>
    /// UtilityWinner == ActionName이면 true.
    /// Conditional Guard는 이 결과로 분기 진입 여부를 결정합니다.
    /// </summary>
    public override bool IsTrue()
    {
        if (UtilityWinner.Value == null || ActionName.Value == null)
            return false;

        return string.Equals(UtilityWinner.Value, ActionName.Value,
            StringComparison.OrdinalIgnoreCase);
    }
}