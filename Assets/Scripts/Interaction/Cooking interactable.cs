using SG;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CookingStationInteractable : InteractableEntity<CookingStationInteractable>
{
    [Header("References")]
    [SerializeField] private CookingStation cookingStation;

    [Header("Grill Settings")]
    [SerializeField] private Transform grillSnapPoint; // 고기가 놓일 위치

    // [Server Only] 물리적 충돌 감지 (아이템 드롭)
    private void OnTriggerEnter(Collider other)
    {
        // 서버에서만 판정하여 중복 실행 방지
        if (!NetworkManager.Singleton.IsServer) return;

        // 1. GrabbableObject인지 확인
        if (other.TryGetComponent(out GrabbableObject grabbable))
        {
            // 누군가 잡고 있다면(아직 손에 들고 있다면) 무시
            if (grabbable.isHeld.Value) return;

            // 2. 이미 조리 중이면 추가 아이템 무시
            if (cookingStation.currentCookingState.Value != CookingState.Empty) return;

            // 3. 아이템 ID 식별 및 Item 객체 확보
            // GrabbaleObject 자체에 itemID가 없어 형제 컴포넌트에서 정보를 찾음
            int itemID = -1;
            Item itemObject = null;

            if (other.TryGetComponent(out Item itemComponent))
            {
                itemObject = itemComponent;
                itemID = itemComponent.itemID;
            }
            else if (other.TryGetComponent(out PickUpItemInteractable pickUp))
            {
                // PickupItemInteractable 에 있는 itemId로 WorldItemDatabase에서 Item 객체 검색
                itemID = pickUp.itemData.itemID;
                itemObject = WorldItemDatabase.Instance.GetItemByID(itemID); // DB에서 ID로 item 검색
            }

            // ID를 찾지 못했으면 중단
            if (itemObject == null) return;

            // 4. 식재료 ID 확인 (DB조회)
            // WorldItemDatabase의 GetRecipeByIngredients는 List<Item>을 요구
            List<Item> inputIngredients = new List<Item>();
            inputIngredients.Add(itemObject);

            // DB에서 레시피 검색(WorldItemDatabase.Instance)
            CookingRecipeSO recipe = WorldItemDatabase.Instance.GetRecipeByIngredients(inputIngredients);
            
            // 유효 레시피 없을 경우 널반환
            if (recipe == null)
            {
                Debug.Log("[CookingStation] 유효한 레시피가 없는 재료입니다.");
                return;
            }

            // 5. 타입별 분기 처리
            if (cookingStation is PotCookingStation)
            {
                // [냄비] 아이템을 파괴하고 내부 데이터로 변환
                grabbable.GetComponent<NetworkObject>().Despawn();
                cookingStation.PlaceItemServerRpc(itemID);
            }
            else if (cookingStation is GrillCookingStation grill)
            {
                // [석쇠] 아이템을 파괴하지 않고 위치 고정 및 등록
                SnapItemToGrill(grabbable);

                // 로직 스크립트에 "이 물체가 올라왔음"을 알림
                grill.RegisterPhysicalItemServerRpc(grabbable.NetworkObjectId, itemID);
            }
        }
    }

    // [Server Only] 아이템이 석쇠 범위를 벗어났을 때 (플레이어가 집어갔을 때)
    private void OnTriggerExit(Collider other)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (cookingStation is GrillCookingStation grill)
        {
            if (other.TryGetComponent(out GrabbableObject grabbable))
            {
                // 나가는 물체가 현재 등록된 물체라면 등록 해제
                if (grabbable.NetworkObjectId == grill.targetItemNetworkObjectId.Value)
                {
                    grill.UnregisterItemServerRpc();

                    // 물리력 복원 (다시 굴러갈 수 있게)
                    var rb = grabbable.GetComponent<Rigidbody>();
                    if (rb) rb.isKinematic = false;
                }
            }
        }
    }

    // 석쇠 위에 아이템을 예쁘게 고정하는 헬퍼 함수
    private void SnapItemToGrill(GrabbableObject grabbable)
    {
        // 위치 이동
        if (grillSnapPoint != null)
        {
            grabbable.transform.position = grillSnapPoint.position;
            grabbable.transform.rotation = grillSnapPoint.rotation;
        }
        else
        {
            grabbable.transform.position = transform.position + Vector3.up * 0.2f;
        }

        // 물리력 끄기 (고정)
        var rb = grabbable.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
    }

    // [Client] 플레이어가 'E' 키를 눌렀을 때의 상호작용
    public override void Interact(CharacterManager player)
    {
        // 냄비의 경우, 요리가 완성되면 'E'키로 회수
        if (cookingStation is PotCookingStation)
        {
            CookingState state = cookingStation.currentCookingState.Value;
            if (state == CookingState.Cooked || state == CookingState.Burnt)
            {
                cookingStation.PickUpItemServerRpc();
                // TODO: 플레이어 인벤토리에 결과물 추가 로직은 PickUpItemServerRpc 내부에서 처리
            }
        }

        // 석쇠의 경우, 플레이어가 직접 GrabbableObject를 잡으므로
        // 조리대 자체를 'E'키로 상호작용할 일은 거의 없음 (필요 시 추가)
    }
}