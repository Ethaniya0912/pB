using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;

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
            // 1. 부모 클래스의 Awake 호출 (기본 컴포넌트 획득)
            base.Awake();

            // PlayerManager 참조 획득
            player = GetComponent<PlayerManager>();

            // [최적화] 자주 사용되는 자식 컴포넌트들을 Awake에서 미리 캐싱합니다.
            _playerAnimator = GetComponentInChildren<Animator>();
            _inventoryRaycaster = GetComponentInChildren<Inventory3DRaycaster>();

            // [디버그] 초기화 확인 로그
            if (player == null)
            {
                Debug.LogError($"<color=red>[PlayerInteraction] {gameObject.name}에서 PlayerManager를 찾을 수 없습니다!</color>");
            }
            else
            {
                // [기존 주석 유지] 초기화 완료 로그
                Debug.Log($"[PVFM] 초기화 완료. 대상: {gameObject.name}");
            }
        }

        private void Update()
        {
            // 2. 네트워크 소유권 체크
            if (!IsOwner) return;

            // 상호작용 루틴 실행
            HandleInteraction();
        }

        /// <summary>
        /// 던지기/내려놓기 입력(RB) 처리
        /// </summary>
        internal void OnRBInputReceived()
        {
            // 1. 들고 있는 물건이 있다면 -> 놓기(던지기)
            if (currentlyHeldObject != null)
            {
                ReleaseGrabbedObject();
                // 물건을 던질 때는 무기 공격을 실행하지 않고 리턴
                return;
            }
        }

        private void HandleInteraction()
        {
            // [기존 로직 유지] 잡고 있는 물건 유무와 상관없이 주변 상호작용 대상 탐색
            CheckForInteractableObject();
        }

        /// <summary>
        /// 실제 상호작용 키(E)를 눌렀을 때 호출됩니다.
        /// </summary>
        public override void Interact()
        {
            if (currentInteractableObject == null)
            {
                Debug.LogWarning("[PlayerInteraction] 상호작용 대상이 존재하지 않습니다.");
                return;
            }

            // [보완] NetworkObject를 안전하게 가져와서 체크 (기존 Line 52 에러 원인 해결)
            // [최적화] TryGetComponent를 사용하여 불필요한 Null 에러 메시지 생성 방지
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
                // NetworkObject가 없는 로컬 오브젝트인 경우에도 일단 실행은 가능하도록 처리
                ExecuteInteractionSequence();
                Debug.Log($"[PlayerInteraction] {currentInteractableObject.name} (로컬 전용) 상호작용 시도.");
            }
        }

        /// <summary>
        /// 오브젝트 타입에 따른 실제 상호작용 분기 로직입니다.
        /// </summary>
        // TD : 남규할일 - 아래 함수 내용 기존과 비교해서 수정할 것
        private void ExecuteInteractionSequence()
        {
            // [Fix] GrabbableObject인 경우 로직 분리
            if (currentInteractableObject is InteractableItem grabbable)
            {
                if (grabbable.isHeld.Value)
                {
                    Debug.Log("[PlayerInteraction] 이미 다른 사람이 잡고 있는 물체입니다.");
                    return;
                }

                // TD : 애니메이션 재생(나중에 있으면 추가)
                //player.playerAnimationManager.PlayTargetAnimation("PickUp_Stand", true);

                // IK 로직 시작 (손을 물체로 뻗고, 닿으면 상호작용 실행)
                if (grabIKCoroutine != null) StopCoroutine(grabIKCoroutine);
                grabIKCoroutine = StartCoroutine(HandleGrabIKProcess(grabbable));
            }
            else
            {
                // 4. 일반 상호작용 (문, 레버 등)
                currentInteractableObject.Interact(player);
                Debug.Log("[PlayerInteraction] 상호작용을 실행했습니다.");
            }
        }

        // [Step 7 수정] IK와 애니메이션 싱크를 맞추는 코루틴
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

            // [최적화] 캐싱된 애니메이터 정보를 사용하여 매번 컴포넌트를 찾는 비용을 제거했습니다.
            Animator animator = _playerAnimator;
            Transform rightHandBone = animator ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;

            float timer = 0f;
            float maxWaitTime = 3.0f;  // 3초 지나면 실패 처리
            float grabThreshold = 0.1f; // 손과 아이템 간 거리 임계값
            bool hasGrabbed = false;

            // B. 3초가 지나거나 손이 닿을 때까지 반복 체크
            while (timer < maxWaitTime)
            {
                timer += Time.deltaTime;

                // 예기치 않은 오브젝트 파괴 대비
                if (targetItem == null) break;

                if (rightHandBone != null && targetItem.gripPoint != null)
                {
                    float distance = Vector3.Distance(rightHandBone.position, targetItem.gripPoint.position);

                    if (distance <= grabThreshold)
                    {
                        hasGrabbed = true;
                        break;
                    }
                }
                else
                {
                    // 본이나 앵커가 없으면 즉시 중단
                    break;
                }

                // 타겟이 시야에서 완전히 벗어나면 중단
                if (!IsTargetInView(targetItem.transform)) break;

                yield return null;
            }

            // C. 손이 충분히 가까워졌다면 실제 상호작용 수행
            if (hasGrabbed && targetItem != null)
            {
                targetItem.Interact(player);

                // [스냅 로직 유지] 잡았을 때 물체가 손바닥에 밀착되도록 보정
                if (rightHandBone != null)
                {
                    targetItem.transform.SetParent(rightHandBone);

                    // GripPoint 기준으로 역계산하여 위치/회전 보정
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

        /// <summary>
        /// 현재 들고 있는 물건을 놓습니다.
        /// </summary>
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

        /// <summary>
        /// 주변의 상호작용 가능한 오브젝트를 탐색하여 가장 가까운 대상을 설정합니다.
        /// </summary>
        private void CheckForInteractableObject()
        {
            // [최적화] Raycast 대신 OverlapSphereNonAlloc을 사용하여 매 프레임 발생하는 가비지 컬렉션(GC)을 방지합니다.
            int numColliders = Physics.OverlapSphereNonAlloc(transform.position, interactionRange, _interactableColliders, interactableLayer);

            InteractableObject closestInteractable = null;
            float closestDistance = float.MaxValue;

            for (int i = 0; i < numColliders; i++)
            {
                Collider collider = _interactableColliders[i];
                if (collider == null) continue;

                // [추가] 자기 자신의 뼈대나 콜라이더인 경우 무시 (루트 오브젝트 비교)
                if (collider.transform.root == transform.root) continue;

                // [최적화] GetComponent 대신 TryGetComponent를 사용하여, 컴포넌트가 없는 배경 물체에서 발생하는 내부 에러 부하를 완전히 제거했습니다.
                if (collider.TryGetComponent<InteractableObject>(out var interactable))
                {
                    // 현재 손에 든 물건은 탐색에서 제외
                    if (interactable == currentlyHeldObject) continue;

                    // 캐릭터 몸통 전방 시야각 내에 있는 물체만 선별
                    if (!IsTargetInView(interactable.transform)) continue;

                    float distance = Vector3.Distance(transform.position, interactable.transform.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestInteractable = interactable;
                    }
                }
                // [수정] 스팸 로그 방지를 위해 else 구문의 Debug.Log 제거
            }

            // 최신 오브젝트로 갱신 (변경 시 UI 업데이트 등을 위한 뼈대 유지)
            if (closestInteractable != currentInteractableObject)
            {
                // [최적화] 패턴 매칭(is)을 활용하여 불필요한 GetComponent 호출을 제거했습니다.
                if (currentInteractableObject is InteractableItem prevItem)
                {
                    prevItem.SetHighlight(0.0f); // 이전 오브젝트 하이라이트 해제
                }

                currentInteractableObject = closestInteractable; // 최신 오브젝트로 갱신

                if (currentInteractableObject is InteractableItem newItem)
                {
                    newItem.SetHighlight(1.0f); // 새 오브젝트 하이라이트 설정
                }
            }
        }

        /// <summary>
        /// 대상이 캐릭터 정면 시야각 안에 있는지 판정하는 헬퍼 함수입니다.
        /// </summary>
        private bool IsTargetInView(Transform target)
        {
            if (target == null) return false;

            Vector3 directionToTarget = (target.position - transform.position).normalized;
            float dot = Vector3.Dot(transform.forward, directionToTarget.normalized);
            return dot >= viewThreshold;
        }

        protected override void OnDrawGizmosSelected()
        {
            // 기즈모를 통해 상호작용 범위와 시야각 시각화
            Gizmos.color = Color.green;
            Vector3 leftRay = Quaternion.AngleAxis(-60, Vector3.up) * transform.forward;
            Vector3 rightRay = Quaternion.AngleAxis(60, Vector3.up) * transform.forward;
            Gizmos.DrawRay(transform.position, leftRay * interactionRange);
            Gizmos.DrawRay(transform.position, rightRay * interactionRange);
            Gizmos.DrawWireSphere(transform.position, interactionRange);
        }

        internal void OnInteractionInputReceived()
        {
            Debug.Log("interaction_Input true");
            Interact();
        }

        // [수정] Alt 입력 처리: 상태에 따라 커서 토글
        internal void OnAltInputReceived(bool isPressed)
        {
            if (isPressed)
            {
                // Alt를 누르고 있는 동안 커서 활성화
                PlayerUIManager.Instance.ToggleCursor(true);
            }
            else
            {
                // Alt를 뗐을 때, 인벤토리가 닫혀있다면 커서 숨김
                if (player != null && player.playerInventoryManager != null && !player.playerInventoryManager.isInventoryOpen)
                {
                    // [최적화] 캐싱된 _inventoryRaycaster를 사용하여 매번 GetComponentInChildren을 호출하던 부하를 제거했습니다.
                    if (_inventoryRaycaster != null && !_inventoryRaycaster.GetIsDragging())
                    {
                        PlayerUIManager.Instance.ToggleCursor(false);
                    }
                }
            }
        }
    }
}