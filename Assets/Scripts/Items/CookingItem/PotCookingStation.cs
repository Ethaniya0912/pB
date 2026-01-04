using SG;
using UnityEngine;

public class PotCookingStation : CookingStation
{
    public override CookingStationType StationType => CookingStationType.Pot;
    [SerializeField] private float boilTime = 10.0f;
    protected override void HandleCookingLogic()
    {
        // 열원이 없거나 켜져있지 않으면 진행 불가
        if (currentHeatSource == null || !currentHeatSource.IsTurnedOn.Value) return;

        // 물이 끓는 온도 체크 (예: 100도)
        if (currentHeatSource.CurrentTemperature.Value < 100f) return;

        switch (currentCookingState.Value)
        {
            case CookingState.Raw:
                // 온도가 충분하면 끓기 시작
                currentCookingState.Value = CookingState.Cooking; // Boiling
                break;

            case CookingState.Cooking:
                // 진행도 증가
                cookingProgress.Value += Time.deltaTime / boilTime;
                if (cookingProgress.Value >= 1.0f)
                {
                    currentCookingState.Value = CookingState.Cooked; // Soup Finished
                    // TODO: 결과물 아이템으로 데이터 교체 (예: 고기 -> 고기스튜)
                }
                break;

            // 냄비는 일반적으로 계속 끓여도 타지 않고 보온되거나 쫄아드는 로직 (여기서는 완료 상태 유지)
            case CookingState.Cooked:
                break;
        }
    }

    public override void OnItemPlaced(GrabbableObject grabbable)
    {
        throw new System.NotImplementedException();
    }
}