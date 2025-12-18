using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace SG
{
    public class PlayerInteractionManager : CharacterInteractionManager
    {
        PlayerManager player;

        // 현재 플레이어가 손에 잡고 있는 아이템을 기억하는 변수
        [Header("Held Object")]
        public GrabbableObject currentlyHeldObject;

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
                // 물건을 들고 있을 때도 상호작용이 필요한 경우(예: 문 열기)를 위해 유지하되,
                // 기획에 따라 여기서 return을 해서 감지를 끌 수도 있습니다.
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
                        // 1. 체크를 먼저 수행 (Host 문제 해결)
                        // Interact()를 호출하면 Host에서는 즉시 isHeld가 true가 되므로,
                        // 호출하기 전에 미리 "잡혀있지 않음"을 확인해야 합니다.
                        if (grabbable.isHeld.Value)
                        {
                            Debug.Log("[PlayerInteraction] 이미 다른 사람이 잡고 있는 물체입니다.");
                            return;
                        }

                        // 2. 상호작용(잡기 요청) 실행
                        currentInteractableObject.Interact(player);

                        // 3. 내 손에 등록 (성공했다고 가정하고 할당)
                        currentlyHeldObject = grabbable;

                        // 잡았으니 감지된 대상에서는 비워줌
                        currentInteractableObject = null;
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

        // 외부(InputManager)에서 호출할 "놓기" 함수
        public void ReleaseGrabbedObject()
        {
            if (currentlyHeldObject != null)
            {
                // 카메라 정면 방향으로 던지기 위한 벡터 계산
                Vector3 dropDirection = transform.forward;
                if (Camera.main != null)
                {
                    dropDirection = Camera.main.transform.forward;
                }
                else if (PlayerCamera.Instance != null)
                {
                    dropDirection = PlayerCamera.Instance.transform.forward;
                }

                // 서버에 놓기(던지기) 요청
                currentlyHeldObject.RequestDropServerRpc(dropDirection);

                Debug.Log($"[PlayerInteraction] {currentlyHeldObject.interactableName}을(를) 놓았습니다.");

                // 내 손 목록에서 제거
                currentlyHeldObject = null;
            }
        }

        private void CheckForInteractableObject()
        {
            Vector3 origin;
            Vector3 direction;

            Camera targetCamera = Camera.main;

            if (PlayerCamera.Instance != null)
            {
                Camera ptrCam = PlayerCamera.Instance.GetComponent<Camera>();
                if (ptrCam != null) targetCamera = ptrCam;
            }

            if (targetCamera != null)
            {
                Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
                origin = ray.origin;
                direction = ray.direction;
            }
            else
            {
                origin = transform.position + Vector3.up * 1.5f;
                direction = transform.forward;
            }

            RaycastHit hit;
            if (Physics.Raycast(origin, direction, out hit, interactionRange, interactableLayer))
            {
                InteractableObject interactable = hit.collider.GetComponent<InteractableObject>();
                // 내가 들고 있는 물건은 다시 감지되지 않도록 제외
                if (interactable != null && interactable != currentlyHeldObject)
                {
                    currentInteractableObject = interactable;
                }
                else
                {
                    currentInteractableObject = null;
                }
            }
            else
            {
                currentInteractableObject = null;
            }
        }

        protected override void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            if (Camera.main != null)
            {
                Vector3 origin = Camera.main.transform.position;
                Vector3 direction = Camera.main.transform.forward;
                Vector3 endPosition = origin + direction * interactionRange;
                Gizmos.DrawLine(origin, endPosition);
            }
        }
    }
}