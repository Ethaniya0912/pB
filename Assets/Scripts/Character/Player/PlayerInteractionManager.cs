using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;
using TDA.Character.Player;

namespace SG
{
    /// <summary>
    /// 플레이어의 상호작용(아이템 줍기, 문 열기 등)을 관리하는 매니저입니다.
    /// 에러 방지를 위해 네트워크 상태 및 오브젝트 유효성 검사 로직이 강화되었습니다.
    /// [최적화] 프로파일러 분석 결과에 따라 GetComponent 부하 및 GC 할당을 최소화하도록 재설계되었습니다.
    /// </summary>
    public class PlayerInteractionManager : CharacterInteractionManager
    {
        private PlayerManager player;

        [Header("Held Object")]
        public InteractableItem currentlyHeldObject;

        [Header("Interaction Settings")]
        [SerializeField][Range(0f, 1f)] private float viewThreshold = 0.5f; // 약 60도(전방 부채꼴)

        // [최적화] 매 프레임 배열 할당(GC 생성)을 방지하기 위한 정적 배열 캐싱
        private Collider[] _interactableColliders = new Collider[10];

        // [최적화] Update 및 반복 호출에서 GetComponent 비용을 제거하기 위한 참조 캐싱
        private Inventory3DRaycaster _inventoryRaycaster;
        private Animator _playerAnimator;

        // IK 자연스러운 처리를 위한 코루틴 변수
        private Coroutine grabIKCoroutine;

        protected override void Awake()
        {
            base.Awake();
            player = GetComponent<PlayerManager>();

            _playerAnimator = GetComponentInChildren<Animator>();
            _inventoryRaycaster = GetComponentInChildren<Inventory3DRaycaster>();

            if (player == null)
            {
                Debug.LogError($"<color=red>[PlayerInteraction] {gameObject.name}에서 PlayerManager를 찾을 수 없습니다!</color>");
            }
            else
            {
                Debug.Log($"[PVFM] 초기화 완료. 대상: {gameObject.name}");
            }
        }

        private void Update()
        {
            if (!IsOwner) return;
            HandleInteraction();
        }

        internal void OnRBInputReceived()
        {
            if (currentlyHeldObject != null)
            {
                ReleaseGrabbedObject();
                return;
            }
        }

        private void HandleInteraction()
        {
            CheckForInteractableObject();
        }

        public override void Interact()
        {
            if (currentInteractableObject == null)
            {
                Debug.LogWarning("[PlayerInteraction] 상호작용 대상이 존재하지 않습니다.");
                return;
            }

            if (currentInteractableObject.TryGetComponent<NetworkObject>(out var networkObject))
            {
                if (networkObject.IsSpawned)
                {
                    ExecuteInteractionSequence();
                }
                else
                {
                    Debug.LogWarning($"[PlayerInteraction] 대상 {currentInteractableObject.name}이 아직 네트워크 스폰 상태가 아닙니다.");
                }
            }
            else
            {
                ExecuteInteractionSequence();
                Debug.Log($"[PlayerInteraction] {currentInteractableObject.name} (로컬 전용) 상호작용 시도.");
            }
        }

        private void ExecuteInteractionSequence()
        {
            if (currentInteractableObject is InteractableItem grabbable)
            {
                if (grabbable.isHeld.Value)
                {
                    Debug.Log("[PlayerInteraction] 이미 다른 사람이 잡고 있는 물체입니다.");
                    return;
                }

                // IK 로직 시작 (손을 물체로 뻗고, 닿으면 상호작용 실행)
                if (grabIKCoroutine != null) StopCoroutine(grabIKCoroutine);
                grabIKCoroutine = StartCoroutine(HandleGrabIKProcess(grabbable));
            }
            else
            {
                // 일반 상호작용 (문, 레버 등)
                currentInteractableObject.Interact(player);
                Debug.Log("[PlayerInteraction] 상호작용을 실행했습니다.");
            }
        }

        private IEnumerator HandleGrabIKProcess(InteractableItem targetItem)
        {
            if (targetItem == null || player == null) yield break;

            Debug.Log($"<color=cyan>[PlayerInteraction] IK 잡기 프로세스 시작: {targetItem.interactableName}</color>");

            // A. IK 대상 설정 (손 & 시선)
            if (player.characterIKController != null)
            {
                player.characterIKController.SetHandIKTarget(targetItem.gripPoint);
                player.characterIKController.SetLookTarget(targetItem.transform);
            }

            Animator animator = _playerAnimator;
            Transform rightHandBone = animator ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;

            float timer = 0f;
            float maxWaitTime = 3.0f;
            float grabThreshold = 0.1f;
            // [최적화] 거리 체크시 부하가 적은 sqrMagnitude 사용을 위한 임계값 제곱
            float grabThresholdSqr = grabThreshold * grabThreshold;
            bool hasGrabbed = false;

            // B. 3초가 지나거나 손이 닿을 때까지 반복 체크
            while (timer < maxWaitTime)
            {
                timer += Time.deltaTime;

                if (targetItem == null) break;

                if (rightHandBone != null && targetItem.gripPoint != null)
                {
                    // [최적화] Vector3.Distance 대신 sqrMagnitude 사용
                    float distanceSqr = (rightHandBone.position - targetItem.gripPoint.position).sqrMagnitude;

                    if (distanceSqr <= grabThresholdSqr)
                    {
                        hasGrabbed = true;
                        break;
                    }
                }
                else
                {
                    break;
                }

                if (!IsTargetInView(targetItem.transform)) break;

                yield return null;
            }

            // C. 손이 충분히 가까워졌다면 실제 상호작용 수행
            if (hasGrabbed && targetItem != null)
            {
                targetItem.Interact(player);

                if (rightHandBone != null)
                {
                    targetItem.transform.SetParent(rightHandBone);

                    Quaternion inverseGripRot = Quaternion.Inverse(targetItem.gripPoint.localRotation);
                    targetItem.transform.localRotation = inverseGripRot;

                    Vector3 offsetPos = targetItem.gripPoint.localPosition;
                    targetItem.transform.localPosition = -(targetItem.transform.localRotation * offsetPos);
                }

                currentlyHeldObject = targetItem;
                Debug.Log($"<color=white>[PlayerInteraction] {targetItem.interactableName}을(를) 손에 고정했습니다.</color>");
            }
            else
            {
                Debug.LogWarning("[PlayerInteraction] 아이템 잡기 실패 (시간 초과 또는 시야 이탈)");
            }

            // D. 상호작용 종료 후 IK 해제 및 상태 정리
            if (player.characterIKController != null)
            {
                player.characterIKController.SetHandIKTarget(null);
                player.characterIKController.SetLookTarget(null);
            }

            currentInteractableObject = null;
        }

        public void ReleaseGrabbedObject()
        {
            if (currentlyHeldObject != null)
            {
                Vector3 dropDirection = transform.forward;
                if (Camera.main != null)
                {
                    dropDirection = Camera.main.transform.forward;
                }
                else if (player.playerCamera != null)
                {
                    dropDirection = player.playerCamera.transform.forward;
                }

                currentlyHeldObject.RequestDropServerRpc(dropDirection);
                Debug.Log($"[PlayerInteraction] {currentlyHeldObject.interactableName}을(를) 놓았습니다.");

                currentlyHeldObject = null;
            }
        }

        private void CheckForInteractableObject()
        {
            int numColliders = Physics.OverlapSphereNonAlloc(transform.position, interactionRange, _interactableColliders, interactableLayer);

            InteractableObject closestInteractable = null;
            float closestDistanceSqr = float.MaxValue; // [최적화] 거리비교 제곱연산 사용

            for (int i = 0; i < numColliders; i++)
            {
                Collider collider = _interactableColliders[i];
                if (collider == null) continue;
                if (collider.transform.root == transform.root) continue;

                if (collider.TryGetComponent<InteractableObject>(out var interactable))
                {
                    if (interactable == currentlyHeldObject) continue;
                    if (!IsTargetInView(interactable.transform)) continue;

                    // [최적화] Vector3.Distance 대신 sqrMagnitude 사용
                    float distanceSqr = (transform.position - interactable.transform.position).sqrMagnitude;
                    if (distanceSqr < closestDistanceSqr)
                    {
                        closestDistanceSqr = distanceSqr;
                        closestInteractable = interactable;
                    }
                }
            }

            if (closestInteractable != currentInteractableObject)
            {
                if (currentInteractableObject is InteractableItem prevItem)
                {
                    prevItem.SetHighlight(0.0f);
                }

                currentInteractableObject = closestInteractable;

                if (currentInteractableObject is InteractableItem newItem)
                {
                    newItem.SetHighlight(1.0f);
                }
            }
        }

        private bool IsTargetInView(Transform target)
        {
            if (target == null) return false;

            Vector3 directionToTarget = (target.position - transform.position).normalized;
            float dot = Vector3.Dot(transform.forward, directionToTarget); // 방향 벡터가 정규화되었으므로 바로 Dot
            return dot >= viewThreshold;
        }

        protected override void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Vector3 leftRay = Quaternion.AngleAxis(-60, Vector3.up) * transform.forward;
            Vector3 rightRay = Quaternion.AngleAxis(60, Vector3.up) * transform.forward;
            Gizmos.DrawRay(transform.position, leftRay * interactionRange);
            Gizmos.DrawRay(transform.position, rightRay * interactionRange);
            Gizmos.DrawWireSphere(transform.position, interactionRange);
        }

        internal void OnInteractionInputReceived()
        {
            Interact();
        }

        internal void OnAltInputReceived(bool isPressed)
        {
            if (isPressed)
            {
                PlayerUIManager.Instance.ToggleCursor(true);
            }
            else
            {
                if (player != null && player.playerInventoryManager != null && !player.playerInventoryManager.isInventoryOpen)
                {
                    if (_inventoryRaycaster != null && !_inventoryRaycaster.GetIsDragging())
                    {
                        PlayerUIManager.Instance.ToggleCursor(false);
                    }
                }
            }
        }
    }
}