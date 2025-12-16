using UnityEngine;
using Unity.Netcode;


namespace SG
{
    /// <summary>
    /// 바닥에 떨어진 아이템(루팅)을 처리하는 클래스입니다.
    /// "인스턴스 ID(이 오브젝트)"와 "데이터 ID(아이템 정보)"를 모두 가집니다.
    /// </summary>
    public class PickUpItemInteractable : InteractableEntity<PickUpItemInteractable>
    {
        [Header("Item Data (Content ID)")]
        [Tooltip("실제 인벤토리에 들어갈 아이템 정보입니다.")]
        // ItemScriptableObject는 프로젝트에 정의된 아이템 데이터 클래스라고 가정합니다.
        [SerializeField] protected Item itemData;

        // 만약 SO가 아니라 ID만 쓴다면:
        // [SerializeField] protected int itemID;


        public override void Interact(CharacterManager character)
        {
            // 1. 상호작용 유효성 검사 (거리가 너무 멀거나, 이미 누군가 줍는 중인지 등)
            // if (!CanInteract(character)) return;


            // 2. 인벤토리에 아이템 추가
            // 여기서 '무엇을' 주웠는지는 itemData(데이터 ID)가 결정합니다.
            Debug.Log($"[PickUp] {character.name}가 {itemData.itemName}을(를) 획득했습니다.");



            // character.characterInventoryManager.AddItem(itemData);
            // 3. 필드 오브젝트 삭제 요청
            // 여기서 '어떤 오브젝트'가 사라지는지는 interactableID(인스턴스 ID)가 결정합니다.
            // 클라이언트가 직접 Despawn할 수 없으므로 ServerRpc 호출
            PickUpItemServerRpc(character.NetworkObjectId);
        }

        //[ServerRpc(RequireOwnership = false)]
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)] // NGO 2.0 대응
        private void PickUpItemServerRpc(ulong characterNetworkID)
        {
            // 서버 측 검증 (거리가 유효한지 등)

            // 아이템 획득 알림 전파 (필요시)
            // PickUpItemClientRpc(characterNetworkID);


            // 네트워크 오브젝트 파괴 (모든 클라이언트에서 사라짐)
            GetComponent<NetworkObject>().Despawn();
        }
    }
}
