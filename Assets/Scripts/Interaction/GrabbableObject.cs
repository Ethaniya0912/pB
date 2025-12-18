using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement; // [New] 씬 관리를 위해 추가

namespace SG
{
    /// <summary>
    /// 물리적으로 잡고 던질 수 있는 오브젝트 (예: 식재료, 도구)
    /// Dev A 작업 영역: 물리 동기화 및 Parenting + [Fix] 씬 관리
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GrabbableObject : InteractableEntity<GrabbableObject>
    {
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
            if (isHeld.Value && currentHandTarget != null)
            {
                transform.position = currentHandTarget.position;
                transform.rotation = currentHandTarget.rotation;
            }
        }

        public override void Interact(CharacterManager character)
        {
            if (isHeld.Value) return;

            if (character.IsOwner)
            {
                RequestGrabServerRpc(character.NetworkObjectId);
            }
        }

        #region Object-Specific Network Logic (Architecture Exception)

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestGrabServerRpc(ulong characterNetworkId)
        {
            if (isHeld.Value) return;

            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(characterNetworkId, out NetworkObject characterNetObj))
            {
                // [NGO] 논리적 부모 설정
                this.NetworkObject.TrySetParent(characterNetObj);

                isHeld.Value = true;

                AttachToHandClientRpc(characterNetworkId);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestDropServerRpc(Vector3 dropDirection)
        {
            if (!isHeld.Value) return;

            // [NGO] 부모 관계 해제
            this.NetworkObject.TryRemoveParent();

            // [Fix] DontDestroyOnLoad 탈출 로직 (서버)
            // 부모가 해제된 후, 아이템을 현재 활성화된 씬(인게임 씬)으로 강제 이동시킵니다.
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(gameObject, activeScene);
            }

            isHeld.Value = false;

            DetachFromHandClientRpc();

            rb.AddForce(dropDirection * 5f, ForceMode.Impulse);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AttachToHandClientRpc(ulong characterNetworkId)
        {
            rb.isKinematic = true;
            col.enabled = false;

            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(characterNetworkId, out NetworkObject characterNetObj))
            {
                Transform handTransform = FindDeepChild(characterNetObj.transform, handBoneName);

                if (handTransform != null)
                {
                    currentHandTarget = handTransform;
                }
                else
                {
                    currentHandTarget = characterNetObj.transform;
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DetachFromHandClientRpc()
        {
            currentHandTarget = null;
            transform.SetParent(null);

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