using SG;
using Unity.Netcode;
using UnityEngine;

// [Dev B] 상호작용 로직 수정
// InteractableEntity<T>를 상속받아 프로젝트의 상호작용 구조를 준수합니다.
public class CookingStationInteractable : InteractableEntity<CookingStationInteractable>
{
    [Header("References")]
    [SerializeField] private CookingStation cookingStation; // Dev A의 로직 스크립트 연결

    // InteractableEntity가 InteractableObject를 상속받고 있다면, 
    // 여기서 Interact를 오버라이드하여 구체적인 행동을 정의합니다.
    public override void Interact(CharacterManager player)
    {
        // 주의: 부모의 Interact가 추상 메서드라면 base.Interact(player)를 호출하지 않습니다.
        // 만약 InteractableEntity에 공통 로직(로그, 이벤트 등)이 구현되어 있다면 base.Interact(player)가 필요할 수도 있지만,
        // "Cannot call abstract base member" 에러가 떴으므로 호출하지 않는 것이 맞습니다.

        // 현재 조리대의 상태 확인
        CookingState state = cookingStation.currentCookingState.Value;

        // 1. 조리대가 비어있을 때 -> 아이템 올리기
        if (state == CookingState.Empty)
        {
            // [Todo] 플레이어의 손에 든 아이템 확인 로직
            // var item = player.inventory.currentItem;
            // if (item != null) cookingStation.PlaceItemServerRpc(item.id);

            Debug.Log("[Client] 조리대에 아이템 배치를 시도합니다.");

            // 테스트를 위해 임의의 아이템 ID(0)를 보낸다고 가정
            cookingStation.PlaceItemServerRpc(0);
        }
        // 2. 조리가 완료되었거나 탔을 때 -> 아이템 회수
        else if (state == CookingState.Cooked || state == CookingState.Burnt)
        {
            cookingStation.PickUpItemServerRpc();
            Debug.Log("[Client] 서버에 완성품 회수 요청 보냄");
        }

        // 3. 조리 중(Cooking)일 때는 아무 행동도 하지 않음 (또는 취소 로직)
    }

    // InteractableEntity<T> 패턴을 따르므로, 필요하다면 OnNetworkSpawn에서 
    // 이 오브젝트를 인터랙션 매니저에 등록하는 로직이 부모 클래스에 있을 수 있습니다.
    // 별도의 추가 구현 없이 부모의 라이프사이클을 따릅니다.
}