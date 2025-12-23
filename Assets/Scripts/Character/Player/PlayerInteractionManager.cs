using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace SG
{
    public class PlayerInteractionManager : CharacterInteractionManager
    {
        PlayerManager player;

        [Header("Held Object")]
        public GrabbableObject currentlyHeldObject;

        [Header("Interaction Settings")]
        [SerializeField][Range(0f, 1f)] float viewThreshold = 0.5f; // [Step 7] 0.5는 약 60도(전방 부채꼴)

        // [Step 7] IK 자연스러운 처리를 위한 코루틴 변수
        private Coroutine grabIKCoroutine;

        protected override void Awake()
        {
            base.Awake();
            player = GetComponent<PlayerManager>();
        }

        private void Update()
        {
            if (!IsOwner) return;

            HandleInteraction();
        }

        private void HandleInteraction()
        {
            // 잡고 있는 물건이 없을 때만 새로운 물건 탐색
            if (currentlyHeldObject == null)
            {
                CheckForInteractableObject();
            }
            else
            {
                // 물건을 들고 있을 때도 상호작용이 필요한 경우 유지
                CheckForInteractableObject();
            }
        }

        public override void Interact()
        {
            if (currentInteractableObject != null)
            {
                if (currentInteractableObject.GetComponent<NetworkObject>().IsSpawned)
                {
                    // [Fix] GrabbableObject인 경우 로직 분리
                    if (currentInteractableObject is GrabbableObject grabbable)
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
            }
        }

        // [Step 7 수정] IK와 애니메이션 싱크를 맞추는 코루틴
        private IEnumerator HandleGrabIKProcess(GrabbableObject targetItem)
        {
            Debug.Log("[PlayerInteraction] IK로 잡기 프로세스를 시작합니다.");

            // A. IK 대상 설정 (손 & 시선) - 머리가 오브젝트를 바라보도록 설정
            player.characterIKController.SetHandIKTarget(targetItem.gripPoint);
            player.characterIKController.SetLookTarget(targetItem.transform);

            // 오른손 트랜스폼 가져오기
            Animator animator = player.GetComponentInChildren<Animator>();
            Transform rightHandBone = animator ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;

            float timer = 0f;
            float maxWaitTime = 3.0f; // f초 지나면 그랩 실패 처리
            float grabThreshold = 0.1f; // 손과 아이템 간 거리 임계값
            bool hasGrabbed = false;

            // B. 3초가 지나거나 손이 닿을 때까지 반복
            while (timer < maxWaitTime)
            {
                timer += Time.deltaTime;

                if (rightHandBone != null && targetItem != null)
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
                    break;
                }

                if (!IsTargetInView(targetItem.transform)) break; // 시야에서 벗어나면 중단

                yield return null;
            }

            // C. 손이 닿았고 아이템이 유효하다면 실제 상호작용(잡기) 수행
            if (hasGrabbed && targetItem != null)
            {
                targetItem.Interact(player);

                // [수정] 2. 잡았을 때 물체가 딱 손에 잡히도록 각도랑 위치 맞추기 (Snap)
                if (rightHandBone != null)
                {
                    // A. 일단 물리적인 부모 설정 (로컬 상호작용 시각적 처리를 위해)
                    // (주의: 실제 네트워크상의 Parent 처리는 GrabbableObject나 NetworkTransform에서 처리되겠지만, 
                    //  여기서는 시각적 스냅핑을 위해 로컬에서 즉시 보정함)
                    targetItem.transform.SetParent(rightHandBone);

                    // B. GripPoint를 기준으로 역계산하여 위치/회전 보정
                    // 목표: GripPoint가 HandBone의 (0,0,0) 위치와 (0,0,0) 회전에 오도록 함

                    // GripPoint의 로컬 회전의 역(Inverse)을 구함
                    Quaternion inverseGripRot = Quaternion.Inverse(targetItem.gripPoint.localRotation);
                    targetItem.transform.localRotation = inverseGripRot;

                    // GripPoint가 이동한 만큼의 역방향으로 아이템 본체를 이동
                    Vector3 offsetPos = targetItem.gripPoint.localPosition;
                    // 회전된 오프셋을 적용하여 이동
                    targetItem.transform.localPosition = -(targetItem.transform.localRotation * offsetPos);
                }

                currentlyHeldObject = targetItem;
                Debug.Log($"[PlayerInteraction] {targetItem.interactableName}을(를) 잡았습니다.");
            }

            // D. 상호작용 종료 후 IK 해제
            player.characterIKController.SetHandIKTarget(null);
            player.characterIKController.SetLookTarget(null);

            // 감지된 대상에서는 비워줌
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
                else if (PlayerCamera.Instance != null)
                {
                    dropDirection = PlayerCamera.Instance.transform.forward;
                }

                currentlyHeldObject.RequestDropServerRpc(dropDirection);
                Debug.Log($"[PlayerInteraction] {currentlyHeldObject.interactableName}을(를) 놓았습니다.");

                currentlyHeldObject = null;
            }
        }

        private void CheckForInteractableObject()
        {
            // UI가 켜져있거나 다른 행동 중일 땐 검사 안함
            // TD : 현재 isInteracting 플래그가 없으므로 주석처리
            // if (player.isInteracting) return;

            // [수정] Raycast 대신 OverlapSphere 사용 (시야각 계산을 위해 주변 물체 모두 검색)
            Collider[] colliders = Physics.OverlapSphere(transform.position, interactionRange, interactableLayer);

            InteractableObject closestInteractable = null;
            float closestDistance = float.MaxValue;

            foreach (var collider in colliders)
            {
                InteractableObject interactable = collider.GetComponent<InteractableObject>();
                if (interactable != null)
                {
                    // 내가 들고 있는 물건은 제외
                    if (interactable == currentlyHeldObject) continue;

                    // [수정] 3. 캐릭터 몸통 전방에 있는 물체만 상호작용 (시야각 체크)
                    if (!IsTargetInView(interactable.transform)) continue;

                    float distance = Vector3.Distance(transform.position, interactable.transform.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestInteractable = interactable;
                    }
                }
            }

            if (closestInteractable != currentInteractableObject) // 변경된 경우에만
            {
                currentInteractableObject?.transform.GetComponent<GrabbableObject>()?.SetHighlight(0.0f); // 이전 오브젝트 하이라이트 해제

                currentInteractableObject = closestInteractable; // 최신 오브젝트로 갱신

                currentInteractableObject?.transform.GetComponent<GrabbableObject>()?.SetHighlight(1.0f); // 새 오브젝트 하이라이트 설정

                // TODO: UI 업데이트 (E키 표시 등)

            }

        }

        // [수정] 전방 감지 헬퍼 함수
        private bool IsTargetInView(Transform target)
        {
            Vector3 directionToTarget = (target.position - transform.position).normalized;
            // 내적(Dot) 계산: 1이면 정면, 0이면 90도 옆, -1이면 뒤
            // Y축(높이) 차이는 무시하고 평면상에서 각도를 잴 수도 있음. 필요시 directionToTarget.y = 0 처리.
            float dot = Vector3.Dot(transform.forward, directionToTarget);
            return dot >= viewThreshold;
        }

        protected override void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            // 시야각(FOV) 기즈모 그리기 (간략화)
            Vector3 leftRay = Quaternion.AngleAxis(-60, Vector3.up) * transform.forward;
            Vector3 rightRay = Quaternion.AngleAxis(60, Vector3.up) * transform.forward;
            Gizmos.DrawRay(transform.position, leftRay * interactionRange);
            Gizmos.DrawRay(transform.position, rightRay * interactionRange);
            Gizmos.DrawWireSphere(transform.position, interactionRange);
        }
    }
}