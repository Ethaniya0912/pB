using UnityEngine;
using Unity.Netcode;

public class GrillCookingStation : CookingStation
{
    [Header("Grill Settings")]
    [SerializeField] private float cookTime = 5.0f;
    [SerializeField] private float burnTime = 3.0f;

    // 현재 석쇠 위에 올라간 물리 오브젝트의 NetworkObjectId (0이면 없음)
    public NetworkVariable<ulong> targetItemNetworkObjectId = new NetworkVariable<ulong>(0);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            targetItemNetworkObjectId.Value = 0;
        }
    }

    // [Server] 외부(Interactable)에서 물리 아이템이 감지되었을 때 호출
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RegisterPhysicalItemServerRpc(ulong networkObjectId, int itemID)
    {
        // 이미 아이템이 있다면 무시
        if (targetItemNetworkObjectId.Value != 0) return;

        targetItemNetworkObjectId.Value = networkObjectId;

        // 부모의 PlaceItem 로직을 호출하여 내부 상태를 'Raw'로 변경하고 진행도를 초기화
        base.PlaceItemServerRpc(itemID);
    }

    // [Server] 아이템을 집어가서 리셋할 때 호출
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void UnregisterItemServerRpc()
    {
        ResetCookingState();
    }

    protected override void HandleCookingLogic()
    {
        // 1. 기본 체크: 열원이 없거나 꺼져있으면 로직 중단
        if (currentHeatSource == null || !currentHeatSource.IsTurnedOn.Value) return;

        // 2. 아이템 유효성 체크
        // 등록된 아이템이 없거나, 물리적으로 사라졌다면(Despawn 등) 상태 리셋
        if (targetItemNetworkObjectId.Value == 0 || !IsItemValid())
        {
            if (currentCookingState.Value != CookingState.Empty)
            {
                ResetCookingState();
            }
            return;
        }

        // 3. 온도 체크 (50도 이상이어야 조리 시작)
        if (currentHeatSource.CurrentTemperature.Value < 50f) return;

        // 4. 조리 진행 (2단계: 굽기 -> 타기)
        switch (currentCookingState.Value)
        {
            case CookingState.Raw:
                currentCookingState.Value = CookingState.Cooking;
                break;

            case CookingState.Cooking:
                // 0.0 -> 1.0 (익는 중)
                float newCookProgress = cookingProgress.Value + (Time.deltaTime / cookTime);
                if (newCookProgress >= 1.0f)
                {
                    cookingProgress.Value = 0f; // 다음 단계(타기)를 위해 0으로 리셋
                    currentCookingState.Value = CookingState.Cooked;
                }
                else
                {
                    cookingProgress.Value = newCookProgress;
                }
                break;

            case CookingState.Cooked:
                // 0.0 -> 1.0 (타는 중)
                float newBurnProgress = cookingProgress.Value + (Time.deltaTime / burnTime);
                if (newBurnProgress >= 1.0f)
                {
                    currentCookingState.Value = CookingState.Burnt;
                    cookingProgress.Value = 1.0f; // 꽉 찬 상태 유지
                }
                else
                {
                    cookingProgress.Value = newBurnProgress;
                }
                break;
        }
    }

    private bool IsItemValid()
    {
        // NetworkManager가 해당 ID의 오브젝트를 관리하고 있는지 확인
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetItemNetworkObjectId.Value, out var netObj))
        {
            // [옵션] 거리가 너무 멀어졌는지 체크 (Interactable의 OnTriggerExit으로 대체 가능하지만 안전장치로 둠)
            if (Vector3.Distance(transform.position, netObj.transform.position) > 2.0f)
                return false;

            return true;
        }
        return false;
    }

    private void ResetCookingState()
    {
        currentCookingState.Value = CookingState.Empty;
        cookingProgress.Value = 0f;
        targetItemNetworkObjectId.Value = 0;
    }
}