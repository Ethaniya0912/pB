using UnityEngine;
public class GrillCookingStation : CookingStation
{
    [Header("Grill Settings")]
    [SerializeField] private float cookTime = 5.0f;
    [SerializeField] private float burnTime = 3.0f; // 조리 완료 후 타기까지 시간

    protected override void HandleCookingLogic()
    {
        // 열원이 없거나 켜져있지 않으면 진행 불가
        if (currentHeatSource == null || !currentHeatSource.IsTurnedOn.Value) return;

        // 적정 온도 체크
        if (currentHeatSource.CurrentTemperature.Value < 50f) return;

        switch (currentCookingState.Value)
        {
            case CookingState.Raw:
                // 조리 진행
                currentCookingState.Value = CookingState.Cooking;
                break;

            case CookingState.Cooking:
                // 진행도 증가
                cookingProgress.Value += Time.deltaTime / cookTime;
                if (cookingProgress.Value >= 1.0f)
                {
                    currentCookingState.Value = CookingState.Cooked;
                    cookingProgress.Value = 0f; // 타는 시간 측정을 위해 리셋
                    // TODO: 비주얼 변경 (구워진 고기 메시로 교체) ClientRpc 호출
                }
                break;

            case CookingState.Cooked:
                // 계속 방치하면 타버림
                cookingProgress.Value += Time.deltaTime / burnTime;
                if (cookingProgress.Value >= 1.0f)
                {
                    currentCookingState.Value = CookingState.Burnt;
                    // TODO: 비주얼 변경 (탄 고기) ClientRpc 호출
                }
                break;
        }
    }
}