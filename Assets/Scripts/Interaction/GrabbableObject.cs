using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

namespace SG
{
    /// <summary>
    /// 물리적으로 잡고 던질 수 있는 오브젝트 (예: 식재료, 도구)
    /// Dev A 작업 영역: 물리 동기화 및 Parenting + [Fix] 씬 관리 + [Step 7] Robust Snapping Logic
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GrabbableObject : InteractableEntity<GrabbableObject>
    {
        [Header("Physics Settings")]
        private Rigidbody rb;
        private Collider col;

        [Header("Hightlight")]
        private Renderer _renderer;
        private MaterialPropertyBlock _propBlock;
        // 셰이더 그래프의 변수 이름 (Reference 이름과 일치해야 함)
        private readonly string _intensityName = "_Intensity";

        [Header("Attachment Settings")]
        [Tooltip("캐릭터가 잡았을 때 부착될 뼈의 이름입니다.")]
        [SerializeField] private string handBoneName = "Weapon Instantiation Slot";

        // 현재 잡혀있는 상태인지 모든 클라이언트 동기화
        public NetworkVariable<bool> isHeld = new NetworkVariable<bool>(false);

        // 시각적 동기화를 위한 타겟 변수
        private Transform currentHandTarget;

        [Header("IK Settings")]
        // Dev B가 프리팹에서 설정해야 할 손잡이 위치
        public Transform gripPoint;

        protected virtual void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            // 만약 gripPoint를 설정 안 했으면 자기 자신을 가리키도록 예외처리
            if (gripPoint == null) gripPoint = transform;
        }

        private void LateUpdate()
        {
            // NGO의 부모 제약(NetworkObject가 아닌 Transform을 부모로 설정 불가)을 우회하기 위해
            // 매 프레임 위치와 회전을 손 뼈대에 강제로 일치시킵니다.
            if (isHeld.Value && currentHandTarget != null)
            {
                // [Step 7 수정] 개선된 Snapping 로직 (Scale & Hierarchy Safe)

                // 1. GripPoint의 Root 기준 로컬 회전값 계산 (계층 구조 무관)
                //    현재 물체의 회전값과 GripPoint 회전값의 차이를 구함
                Quaternion relativeRotation = Quaternion.Inverse(transform.rotation) * gripPoint.rotation;

                // 2. 목표 회전값 적용
                //    HandTarget의 회전에서 위에서 구한 차이만큼 역으로 돌려줌
                Quaternion targetRotation = currentHandTarget.rotation * Quaternion.Inverse(relativeRotation);
                transform.rotation = targetRotation;

                // 3. GripPoint의 Root 기준 로컬 위치값 계산 (Scale 포함)
                //    InverseTransformPoint는 현재 스케일이 반영된 로컬 좌표를 반환함
                Vector3 relativePosition = transform.InverseTransformPoint(gripPoint.position);

                // 4. 목표 위치값 적용
                //    Root 위치 = HandTarget 위치 - (새로운 회전 * (로컬 위치 * 스케일))
                //    TransformVector를 사용하여 로컬 벡터를 월드 벡터로 변환 (회전 & 스케일 적용)
                //    *주의: transform.rotation을 이미 targetRotation으로 바꿨으므로 TransformVector가 올바르게 작동함
                Vector3 worldOffset = transform.TransformVector(relativePosition);
                transform.position = currentHandTarget.position - worldOffset;
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

        /// <summary>
        /// 하이라이트 효과 설정
        /// </summary>
        public void SetHighlight(float value)
        {
            Debug.Log("[Grabbable] SetHighlight called with value: " + value);
            // 현재 블록을 가져와서 값 수정 후 다시 적용
            _renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(_intensityName, value);
            _renderer.SetPropertyBlock(_propBlock);
        }
    }
}