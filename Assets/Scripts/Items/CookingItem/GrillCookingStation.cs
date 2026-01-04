using UnityEngine;
using Unity.Netcode;
using SG;
using System;
using NUnit.Framework;
using System.Collections.Generic;

public class GrillCookingStation : CookingStation
{
    public override CookingStationType StationType => CookingStationType.Grill;
    [Header("Grill Settings")]
    [SerializeField] private float cookTimePerSide = 5.0f;
    [SerializeField] private float burnTime = 3.0f;
    [SerializeField] private Transform grillSnapPoint;

    [Header("Double-Sided Progress")]
    public NetworkVariable<float> bottomProgress = new NetworkVariable<float>(0f);
    public NetworkVariable<float> topProgress = new NetworkVariable<float>(0f);
    public NetworkVariable<float> totalBurnProgress = new NetworkVariable<float>(0f);

    // 현재 석쇠 위에 올라간 물리 오브젝트의 NetworkObjectId (0이면 없음)
    public NetworkVariable<ulong> targetItemNetworkObjectId = new NetworkVariable<ulong>(0);
    private int currentItemID;

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
    public void RegisterItemServerRpc(ulong networkObjectId, int itemID)
    {
        // 이미 아이템이 있다면 무시
        if (targetItemNetworkObjectId.Value != 0) return;

        targetItemNetworkObjectId.Value = networkObjectId;
        currentItemID = itemID;

        // 부모의 PlaceItem 로직을 호출하여 내부 상태를 'Raw'로 변경하고 진행도를 초기화
        base.PlaceItemServerRpc(itemID);
        Debug.Log($"[Grill] 아이템 등록 완료: {networkObjectId}");
    }

    // [Server] 아이템을 집어가서 리셋할 때 호출
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void UnregisterItemServerRpc()
    {
        Debug.Log("[Grill] 아이템 등록 해제됨");
        ResetCookingState();
    }

    public override void OnItemPlaced(GrabbableObject grabbable)
    {
        if (!IsServer) return; // 서버가 아니면 리턴.

        // 1. 물리적 고정 수행
        SnapItem(grabbable.transform, grabbable.GetComponent<Rigidbody>());

        // 2. 서버 로직 등록
        RegisterItemServerRpc(grabbable.NetworkObjectId, grabbable.itemData.itemID);
    }

    private void SnapItem(Transform targetTransform, Rigidbody targetRb)
    {
        // 위치 이동
        if (grillSnapPoint != null)
        {
            targetTransform.position = grillSnapPoint.position;
            targetTransform.rotation = grillSnapPoint.rotation;
        }
        else // 스냅 포인트가 없으면 자동 포지션 설정.
        {
            targetTransform.position += Vector3.up * 0.2f;
        }

        // 물리력 끄기 (고정)
        if (targetRb != null)
        {
            targetRb.linearVelocity = Vector3.zero;
            targetRb.angularVelocity = Vector3.zero;
            targetRb.isKinematic = true;
        }
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
                // 왜 리셋되었는지 로그 출력 (버그 추적용)
                Debug.Log($"[Grill] 조리 중단 및 리셋: ID({targetItemNetworkObjectId.Value}), Valid({IsItemValid()})");
                ResetCookingState();
            }
            return;
        }

        // 3. 온도 체크 (50도 이상이어야 조리 시작)
        if (currentHeatSource.CurrentTemperature.Value < 50f) return;

        //// 4. 조리 진행 (2단계: 굽기 -> 타기) 레거시코드
        //switch (currentCookingState.Value)
        //{
        //    case CookingState.Raw:
        //        currentCookingState.Value = CookingState.Cooking;
        //        break;

        //    case CookingState.Cooking:
        //        // 0.0 -> 1.0 (익는 중)
        //        float newCookProgress = cookingProgress.Value + (Time.deltaTime / cookTime);
        //        if (newCookProgress >= 1.0f)
        //        {
        //            cookingProgress.Value = 0f; // 다음 단계(타기)를 위해 0으로 리셋
        //            currentCookingState.Value = CookingState.Cooked;
        //        }
        //        else
        //        {
        //            cookingProgress.Value = newCookProgress;
        //        }
        //        break;

        //    case CookingState.Cooked:
        //        // 0.0 -> 1.0 (타는 중)
        //        float newBurnProgress = cookingProgress.Value + (Time.deltaTime / burnTime);
        //        if (newBurnProgress >= 1.0f)
        //        {
        //            currentCookingState.Value = CookingState.Burnt;
        //            cookingProgress.Value = 1.0f; // 꽉 찬 상태 유지
        //        }
        //        else
        //        {
        //            cookingProgress.Value = newBurnProgress;
        //        }
        //        break;
        //} 

        bool isBottomDown = IsBottomFacingDown();
        float deltaTime = Time.deltaTime;

        UpdateSideCooking(isBottomDown, deltaTime);
        CheckCompletion();
    }
    private bool IsBottomFacingDown()
    {
        if(NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetItemNetworkObjectId.Value, out var netObj))
        {
            // 뒤집기 판정 (Up 벡터 기반)
            float dot = Vector3.Dot(netObj.transform.up, Vector3.up);
            return dot > 0f;
        }
        return true;
    }

    private void UpdateSideCooking(bool isBottomDown, float dt)
    {
        if (isBottomDown)
        {
            if (bottomProgress.Value < 1.0f)
                bottomProgress.Value = Mathf.Min(1.0f, bottomProgress.Value + (dt / cookTimePerSide));
            else
                totalBurnProgress.Value += (dt / burnTime);
        }
        else
        {
            if (topProgress.Value < 1.0f)
                topProgress.Value = Mathf.Min(1.0f, topProgress.Value + (dt / cookTimePerSide));
            else
                totalBurnProgress.Value += (dt / burnTime);
        }

        // 전체 진행도 동기화 (UI용)
        cookingProgress.Value = (bottomProgress.Value + topProgress.Value) / 2f;
    }

    private void CheckCompletion()
    {
        // 1. 타버림 판정 (안탓을때만 실행)
        if (totalBurnProgress.Value >= 1.0f && currentCookingState.Value != CookingState.Burnt)
        {
            currentCookingState.Value = CookingState.Burnt;
            ReplaceItem(false); // 탄 아이템으로 교체
            return;
        }

        // 2. 완성 판정 (이미 완성된 상태가 아닐 때만 실행)
        if (bottomProgress.Value >= 1.0f && topProgress.Value >= 1.0f && currentCookingState.Value == CookingState.Cooking)
        {
            currentCookingState.Value = CookingState.Cooked;
            ReplaceItem(true); // 완성 아이템으로 교체
        }
    }

    private void ReplaceItem(bool isSuccess)
    {
        // 레시피 찾기.
        Item rawItem = WorldItemDatabase.Instance.GetItemByID(currentItemID);
        List<Item> ingredients = new List<Item> { rawItem };
        CookingRecipeSO recipe = WorldItemDatabase.Instance.GetRecipeByIngredients(ingredients, StationType);
        
        // 결과물 결정 (성공 시 resultItem, 실패 시 burntItem)
        Item nextItem = isSuccess ? recipe?.resultItem : recipe?.burntItem;

        if (nextItem == null || nextItem.itemModel == null)
        {
            Debug.LogWarning("[Grill] 교체할 아이템 데이터가 레시피에 없습니다.");
            return;
        }

        Vector3 lastPos = Vector3.zero;
        Quaternion lastRot = Quaternion.identity;

        // 기존 오브젝트 정보 저장 후 제거
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetItemNetworkObjectId.Value, out var oldObj))
        {
            lastPos = oldObj.transform.position;
            lastRot = oldObj.transform.rotation;
            oldObj.Despawn();
        }

        // 새 오브젝트 생성 및 스폰
        GameObject newObj = Instantiate(nextItem.itemModel, lastPos, lastRot);
        NetworkObject netObj = newObj.GetComponent<NetworkObject>();
        netObj.Spawn();

        // 물리 고정 및 데이터 연결
        if (newObj.TryGetComponent(out GrabbableObject grabbable))
        {
            grabbable.itemData = nextItem;
            SnapItem(newObj.transform, newObj.GetComponent<Rigidbody>());
        }

        // ID 및 현재 아이템 정보 갱신
        targetItemNetworkObjectId.Value = netObj.NetworkObjectId;
        currentItemID = nextItem.itemID;

        // 성공시 진행도 보정
        if (isSuccess)
        {
            bottomProgress.Value = 1.0f;
            topProgress.Value = 1.0f;
            totalBurnProgress.Value = 0f;
        }

        Debug.Log($"[Grill] 아이템 교체 완료 : {nextItem.itemName} (성공여부 : {isSuccess}");
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
        bottomProgress.Value = 0f;
        topProgress.Value = 0f;
        totalBurnProgress.Value = 0f;
        targetItemNetworkObjectId.Value = 0;
        currentItemID = -1;
    }
}