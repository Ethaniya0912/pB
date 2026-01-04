using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

namespace SG
{
    /// <summary>
    /// 물리적으로 잡고 던질 수 있는 오브젝트 (예: 식재료, 도구)
    /// Dev A 작업 영역: 물리 동기화 및 Parenting + [Fix] 씬 관리
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GrabbableObject : InteractableEntity<GrabbableObject>
    {
        [Header("Item")]
        public Item itemData; // ItemSO를 인용, 쿠킹인터액터블에서 데이터 가져오기위함.

        [Header("Physics Settings")]
        private Rigidbody rb;
        private Collider col;

        [Header("Attachment Settings")]
        [Tooltip("캐릭터가 잡았을 때 부착될 뼈의 이름입니다.")]
        [SerializeField] private string handBoneName = "B-hand.R";

        // 현재 잡혀있는 상태인지 모든 클라이언트 동기화
        public NetworkVariable<bool> isHeld = new NetworkVariable<bool>(false);

        // 시각적 동기화를 위한 타겟 변수
        private Transform currentHandTarget;

        protected virtual void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
        }

        private void LateUpdate()
        {
            // NGO의 부모 제약(NetworkObject가 아닌 Transform을 부모로 설정 불가)을 우회하기 위해
            // 매 프레임 위치와 회전을 손 뼈대에 강제로 일치시킵니다.
            if (isHeld.Value && currentHandTarget != null)
            {
                transform.position = currentHandTarget.position;
                transform.rotation = currentHandTarget.rotation;
            }
        }

        public override void Interact(CharacterManager character)
        {
            if (isHeld.Value) return;

            // [조건 체크] 플레이어인 경우, 오른손이 언암(Unarmed) 상태인지 확인
            if (character is PlayerManager player)
            {
                // 현재 오른손 무기가 존재하고, 그 무기가 '맨손(Unarmed)' 아이템이 아니라면 잡기 불가
                if (player.playerInventoryManager.currentRightHandWeapon != null &&
                    player.playerInventoryManager.currentRightHandWeapon.itemID != WorldItemDatabase.Instance.unarmedWeapon.itemID)
                {
                    Debug.Log("[Grabbable] 무기를 든 상태에서는 잡을 수 없습니다. (빈손 필요)");
                    return;
                }
            }

            // 소유권자(내 캐릭터)만 서버에 잡기 요청 가능
            if (character.IsOwner)
            {
                RequestGrabServerRpc(character.NetworkObjectId);
                Debug.Log("[Grabbable] Grab request sent to server.");
            }
        }

        #region Object-Specific Network Logic (Architecture Exception)

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestGrabServerRpc(ulong characterNetworkId)
        {
            if (isHeld.Value) return;

            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(characterNetworkId, out NetworkObject characterNetObj))
            {
                // [NGO] 논리적 부모 설정 (네트워크 동기화용 - 소유권 및 씬 전환 따라가기)
                this.NetworkObject.TrySetParent(characterNetObj);

                isHeld.Value = true;

                // 시각적 부착 처리는 ClientRpc로 전파
                AttachToHandClientRpc(characterNetworkId);
            }

            Debug.Log("[Grabbable] Grabbed by character with NetworkId: " + characterNetworkId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestDropServerRpc(Vector3 dropDirection)
        {
            if (!isHeld.Value) return;

            // [NGO] 부모 관계 해제
            this.NetworkObject.TryRemoveParent();

            // [Fix] DontDestroyOnLoad 탈출 로직 (서버)
            // 플레이어(DDOL 씬)에서 떨어져 나온 후, 아이템을 현재 활성화된 인게임 씬으로 이동시킵니다.
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(gameObject, activeScene);
            }

            isHeld.Value = false;

            DetachFromHandClientRpc();

            // 던지는 힘 적용
            rb.AddForce(dropDirection * 5f, ForceMode.Impulse);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AttachToHandClientRpc(ulong characterNetworkId)
        {
            rb.isKinematic = true;
            col.enabled = false;

            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(characterNetworkId, out NetworkObject characterNetObj))
            {
                // 캐릭터 계층 구조 깊숙이 있는 손 뼈(B-hand.R)를 찾습니다.
                Transform handTransform = FindDeepChild(characterNetObj.transform, handBoneName);

                if (handTransform != null)
                {
                    currentHandTarget = handTransform;
                }
                else
                {
                    // 뼈를 못 찾으면 임시로 Root Transform을 따라가도록 설정
                    currentHandTarget = characterNetObj.transform;
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DetachFromHandClientRpc()
        {
            currentHandTarget = null;
            transform.SetParent(null); // 혹시 모를 부모 관계 해제

            // [Fix] DontDestroyOnLoad 탈출 로직 (클라이언트)
            // 클라이언트 사이드에서도 씬 이동을 확실하게 처리합니다.
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(gameObject, activeScene);
            }

            rb.isKinematic = false;
            col.enabled = true;
        }

        #endregion

        /// <summary>
        /// 계층 구조 깊은 곳에 있는 자식 Transform을 이름으로 찾습니다. (재귀 호출)
        /// </summary>
        private Transform FindDeepChild(Transform parent, string boneName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == boneName)
                    return child;

                Transform found = FindDeepChild(child, boneName);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}