// =============================================================================
// IsFleeCondition.cs  |  단일 기능 테스트용 커스텀 Condition
// Variable Comparison 대신 사용하여 BB 값을 직접 읽는지 확인
// BT 에디터에서: Assign Condition → "IsFlee" 검색
// =============================================================================
using System;
using Unity.Behavior;
using UnityEngine;

[Serializable, Unity.Properties.GeneratePropertyBag]
[Condition(name: "IsFlee",
    story: "[UtilityWinner] equals Flee",
    category: "pB-4/Conditions",
    id: "bb_test_isflee_001")]
public partial class IsFleeCondition : Condition
{
    [SerializeReference] public BlackboardVariable<string> UtilityWinner;

    public override bool IsTrue()
    {
        string val = UtilityWinner?.Value;
        Debug.Log($"[IsFleeCondition] UtilityWinner.Value='{val}' (ref null={UtilityWinner == null})");
        return string.Equals(val, "Flee", StringComparison.OrdinalIgnoreCase);
    }
}
