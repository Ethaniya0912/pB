using UnityEngine;
using Unity.Netcode;


namespace SG
{
    /// <summary>
    /// NPC와의 상호작용(대화, 상점 등)을 처리하는 클래스입니다.
    /// </summary>
    public class NPCInteractable : InteractableEntity<NPCInteractable>
    {
        [Header("NPC Data (Content ID)")]
        [Tooltip("대화 테이블이나 퀘스트 ID를 조회할 때 사용하는 NPC 고유 번호입니다.")]
        [SerializeField] protected int npcID;


        [Header("NPC State")]
        [Tooltip("NPC가 현재 대화 가능한 상태인지 여부")]
        [SerializeField] private bool isBusy;


        public override void Interact(CharacterManager character)
        {
            // 플레이어(Owner)만 UI를 띄워야 함
            if (!character.IsOwner) return;


            if (isBusy)
            {
                Debug.Log("NPC가 바쁩니다.");
                return;
            }


            // 대화 시작
            // interactableID는 "내 앞의 이 NPC 오브젝트"를 의미하고
            // npcID는 "상인 철수"라는 데이터를 의미합니다.
            Debug.Log($"[NPC] NPC({npcID})와 대화를 시작합니다.");

            // UI 매니저 연동 예시
            // character.playerUIManager.OpenDialogueWindow(npcID);

            // 필요 시 서버에 "나 대화 중이야"라고 알려서 다른 사람이 말 못 걸게 할 수 있음
            SetBusyStateServerRpc(true);
        }


        [ServerRpc(RequireOwnership = false)]
        public void SetBusyStateServerRpc(bool state)
        {
            isBusy = state;
            // 다른 클라이언트들에게도 바쁜 상태 동기화 (NetworkVariable을 쓰는 것이 더 좋을 수 있음)
        }
    }
}
