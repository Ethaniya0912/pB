using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SG
{
    /// <summary>
    /// [Rule 2] PlayerManager(Derived)에 부착될 구체적인 구현체
    /// </summary>
    public class PlayerInteractionManager : CharacterInteractionManager
    {
        PlayerManager player;

        protected override void Awake()
        {
            base.Awake();
            // [Rule 1] 다운캐스팅하여 구체적인 PlayerManager 기능 접근
            player = GetComponent<PlayerManager>();
        }

        private void Update()
        {
            // [Rule 4-3-1] Owner Permission: 내 캐릭터의 입력은 내가 처리
            if (!IsOwner) return;

            HandleInteraction();
        }

        private void HandleInteraction()
        {
            // 1. 감지 로직 (부모 메서드 활용)
            // 플레이어는 카메라가 보는 방향 혹은 캐릭터 정면 등 시점 처리가 중요
            Vector3 origin = player.transform.position;
            origin.y += PlayerCamera.Instance != null ? 0 : 1.5f; // 카메라 핸들러 유무에 따른 예외처리 예시
            Vector3 direction = player.transform.forward; // 또는 Camera.main.transform.forward

            CheckForInteractableObject();

            // 2. UI 처리 (플레이어 전용)
            // if (currentInteractableObject != null)
            //     player.playerUIManager.ShowInteractionPopup(true);
            // else
            //     player.playerUIManager.ShowInteractionPopup(false);

            // 3. 입력 처리
            // PlayerInputManager에서 상호작용 입력 감지함
        }
        /// <summary>
        /// [플레이어 전용 감지 로직]
        /// 카메라의 위치와 정면 방향을 계산하여 부모의 감지 로직(SphereCast)을 호출합니다.
        /// </summary>
        private void CheckForInteractableObject()
        {
            Vector3 origin;
            Vector3 direction;

            // 1. 레이 발사 원점과 방향 계산
            // CameraHandler가 있다면 그곳의 transform을, 없다면 메인 카메라를 사용
            // (보통 TPS 게임에서는 Camera Object의 위치에서 쏘는 것이 정확합니다)
            if (PlayerCamera.Instance != null)
            {
                origin = PlayerCamera.Instance.transform.position;
                direction = PlayerCamera.Instance.transform.forward;
            }
            else if (Camera.main != null)
            {
                origin = Camera.main.transform.position;
                direction = Camera.main.transform.forward;
            }
            else
            {
                // 카메라를 못 찾았을 경우 캐릭터 눈 위치 등으로 대체
                origin = transform.position + Vector3.up * 1.5f;
                direction = transform.forward;
            }

            // 2. 부모 클래스(CharacterInteractionManager)의 물리 연산 메서드 호출
            // 이 메서드가 Physics.SphereCast를 수행하고 currentInteractableObject 변수를 갱신해줍니다.
            base.CheckForInteractableObject(origin, direction);
        }

        // 디버그용 기즈모: 플레이어는 카메라 기준으로 쏘기 때문에 
        // 씬 뷰에서 확인하기 쉽도록 카메라 위치에서 선을 그려줍니다.
        protected override void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;

            Vector3 origin = Vector3.zero;
            Vector3 direction = Vector3.forward;

            if (Camera.main != null)
            {
                origin = Camera.main.transform.position;
                direction = Camera.main.transform.forward;
            }
            else
            {
                origin = transform.position + Vector3.up * 1.5f;
                direction = transform.forward;
            }

            // SphereCast의 범위와 크기를 시각화
            Vector3 endPosition = origin + direction * interactionRange;
            Gizmos.DrawWireSphere(endPosition, sphereCastRadius);
            Gizmos.DrawLine(origin, endPosition);
        }
    }
}
