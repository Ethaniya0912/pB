using UnityEngine;
using Unity.Netcode;


namespace SG
{
    /// <summary>
    /// 문(Door) 상호작용 예시입니다.
    /// 별도의 Content ID 없이, 문 자체의 상태(Open/Close)만 관리합니다.
    /// </summary>
    public class DoorInteractable : InteractableEntity<DoorInteractable>
    {
        [Header("Door State")]
        [SerializeField] private bool isOpen;
        [SerializeField] private Animator doorAnimator;


        // 동기화를 위해 NetworkVariable 사용 권장
        // private NetworkVariable<bool> netIsOpen = new NetworkVariable<bool>(false);


        public override void Interact(CharacterManager character)
        {
            Debug.Log($"[Door] 문({interactableID}) 상호작용 시도");
            ToggleDoorRpc();
        }

        // [변경점 1] ServerRpc(RequireOwnership = false) -> Rpc(SendTo.Server, ...)
        // InvokePermission.Everyone : 소유자가 아니더라도(아무나) 이 RPC를 서버로 보낼 수 있음
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)] // NGO 2.0 대응
        private void ToggleDoorRpc()
        {
            // 1. 서버에서 상태 변경
            isOpen = !isOpen;

            // 2. 결과 전파 (모든 클라이언트에게)
            ToggleDoorClientRpc(isOpen);
        }

        // [변경점 2] ClientRpc -> Rpc(SendTo.ClientsAndHost)
        // SendTo.ClientsAndHost : 서버(호스트)를 포함한 모든 클라이언트에게 실행
        [Rpc(SendTo.ClientsAndHost)]
        private void ToggleDoorClientRpc(bool openState)
        {
            // 3. 클라이언트에서 시각적 업데이트
            isOpen = openState;

            if (doorAnimator != null)
            {
                doorAnimator.SetBool("IsOpen", isOpen);
            }
            else
            {
                Debug.Log($"문이 {(isOpen ? "열렸습니다" : "닫혔습니다")}.");
            }
        }

        // [저장 시스템 연동]
        /*public override bool GetSaveData()
        {
            return isOpen;
        }

        public override void LoadSaveData(bool savedState)
        {
            isOpen = savedState;
            if (doorAnimator != null)
            {
                doorAnimator.SetBool("IsOpen", isOpen);
            }
        }*/
    }
}
