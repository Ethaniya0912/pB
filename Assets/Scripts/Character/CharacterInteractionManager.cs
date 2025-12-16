using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace SG
{
    /// <summary>
    /// [Rule 3] Component Separation: 상호작용 로직만 전담하는 매니저
    /// [Rule 2] Inheritance: NetworkBehaviour 상속
    /// </summary>
    public class CharacterInteractionManager : NetworkBehaviour
    {
        // [Rule 1] 중앙집중형 설계를 위해 CharacterManager 참조
        protected CharacterManager character;

        [Header("Interaction Settings")]
        [SerializeField] protected float interactionRange = 2.0f;
        [SerializeField] protected float sphereCastRadius = 0.3f;
        [SerializeField] protected LayerMask interactableLayer;

        [Header("Debug")]
        // [Rule 4] 필요시 NetworkVariable로 동기화할 수 있으나, 단순 감지는 로컬에서 처리
        [SerializeField] protected InteractableObject currentInteractableObject;

        protected virtual void Awake()
        {
            character = GetComponent<CharacterManager>();
        }

        /// <summary>
        /// 주변의 상호작용 가능한 오브젝트를 감지합니다.
        /// Player는 카메라 기준, AI는 눈 기준 등 origin이 다를 수 있어 매개변수로 받습니다.
        /// </summary>
        protected virtual void CheckForInteractableObject(Vector3 origin, Vector3 direction)
        {
            // Owner가 아니면 불필요한 연산 방지 (AI는 Server에서 돌므로 예외 처리 필요할 수 있음)
            if (!IsOwner && character.IsOwner) return;

            RaycastHit hit;
            // 물리 연산
            if (Physics.SphereCast(origin, sphereCastRadius, direction, out hit, interactionRange, interactableLayer))
            {
                InteractableObject interactable = hit.collider.GetComponent<InteractableObject>();

                if (interactable != null)
                {
                    currentInteractableObject = interactable;
                    //Debug.Log($"interactable obejct : {currentInteractableObject.name}");
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

        // [Rule 6] 확장성을 위해 가상 함수로 선언
        public virtual void Interact()
        {
            if (currentInteractableObject == null) { Debug.Log("Interactable Object is null"); return; }

            // 여기서 애니메이션이나 사운드 재생 등을 트리거 할 수 있음
            // character.characterAnimationManager.PlayTargetActionAnimation(...)

            // 실제 오브젝트와의 상호작용
            Debug.Log($"[Interaction] {character.name}가 {currentInteractableObject.name}과(와) 상호작용합니다.");
            currentInteractableObject.Interact(character);
        }

        protected virtual void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Vector3 origin = transform.position + Vector3.up * 1.5f; // 기본값
            Gizmos.DrawWireSphere(origin + transform.forward * interactionRange, sphereCastRadius);
        }
    }
}
