using UnityEngine;
using TDA.Character;

namespace TDA.Character.Player
{
    /// <summary>
    /// [L3 Domain] 플레이어 전용 IK 및 시선 제어기입니다.
    /// 베이스 클래스(CharacterIKController)의 리깅 시스템을 상속받아 활용하며, 
    /// 락온/자유시점에 따른 동적 시선 처리와 하이브리드 회전(턴 애니메이션) 로직을 통합 관리합니다.
    /// </summary>
    public class PlayerIKController : CharacterIKController
    {
        private PlayerManager player;

        [Header("Dynamic Look-At Settings")]
        [Tooltip("카메라 자유 시점일 때, 캐릭터가 주시할 카메라 전방의 가상 거리입니다.")]
        [SerializeField] private float freeLookDistance = 15f;
        private Vector3 currentLookAtPosition;

        [Header("Hybrid Turn Animation Thresholds (P2)")]
        [Tooltip("고개가 꺾이는 한계각. 이 각도를 넘으면 발을 떼서 제자리 회전(Turn) 애니메이션을 재생합니다.")]
        [SerializeField][Range(30f, 90f)] private float turnStepAngle = 55f;

        [Tooltip("턴 상태에서 빠져나와 다시 일상 상태(IK 복구)로 돌아가기 위한 히스테리시스 오프셋입니다.")]
        [SerializeField][Range(5f, 30f)] private float turnHysteresisOffset = 15f;

        [Tooltip("180도 피벗 회전 발동 임계점입니다.")]
        [SerializeField][Range(90f, 180f)] private float turnPivotAngle = 120f;

        [Header("Logical State Names (SO 연동)")]
        [Tooltip("CharacterAnimationSetSO에 등록된 Turn 애니메이션 이름과 정확히 일치해야 합니다.")]
        [SerializeField] private string turnLeft90StateName = "Turn_Left_90";
        [SerializeField] private string turnRight90StateName = "Turn_Right_90";
        [SerializeField] private string turnLeft180StateName = "Turn_Left_180";
        [SerializeField] private string turnRight180StateName = "Turn_Right_180";

        [Header("Debug & Monitor")]
        [Tooltip("현재 IK 컨트롤러가 턴 애니메이션을 관장하고 있는지 여부를 나타냅니다.")]
        [SerializeField] private bool isTurningDebug = false;

        // [신규 추가] 애니메이터 파라미터 인스펙터 노출용 변수
        [Tooltip("현재 애니메이터로 전송 중인 최종 턴 각도입니다.")]
        [SerializeField] private float currentTurnAngle;

        [Tooltip("물리적으로 계산된 순수 시선 각도 차이 (몸통 기준)")]
        [SerializeField] private float rawAngleDifference;

        protected override void Awake()
        {
            base.Awake();
            if (Application.isPlaying)
            {
                player = GetComponent<PlayerManager>();
            }
        }

        protected override void Update()
        {
            base.Update();

            if (Application.isPlaying && player != null && player.IsOwner)
            {
                CalculateLookAtTarget();
                HandleHybridRotationLogic();
            }
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();

            if (Application.isPlaying && player != null && player.IsOwner)
            {
                if (player.playerCamera == null) return;

                if (targetLookWeight > 0.01f && headTarget != null)
                {
                    headTarget.position = currentLookAtPosition;
                }
            }
        }

        private void CalculateLookAtTarget()
        {
            if (player.playerCamera == null || player.playerCamera.cameraObject == null) return;

            if (player.playerNetworkManager.isLockedOn.Value && player.playerCombatManager.currentTarget != null)
            {
                currentLookAtPosition = player.playerCombatManager.currentTarget.transform.position + (Vector3.up * 1.2f);
            }
            else
            {
                Transform camTransform = player.playerCamera.cameraObject.transform;
                currentLookAtPosition = camTransform.position + (camTransform.forward * freeLookDistance);
            }
        }

        private void HandleHybridRotationLogic()
        {
            // 1. 무조건 실제 각도부터 먼저 계산합니다. (파라미터 업데이트를 위해)
            Vector3 directionToTarget = currentLookAtPosition - transform.position;
            directionToTarget.y = 0f;

            Vector3 forward = transform.forward;
            forward.y = 0f;

            float signedAngle = Vector3.SignedAngle(forward, directionToTarget, Vector3.up);
            float absAngle = Mathf.Abs(signedAngle);

            // 실시간 각도 디버깅 표시
            rawAngleDifference = signedAngle;

            // 2. [무한 루프의 주범 해결!] 액션 중일 때는 파라미터를 0으로 '먼저' 세탁하고 빠져나갑니다.
            if (player.isPerformingAction)
            {
                targetLookWeight = 0f;
                isTurningDebug = false;
                currentTurnAngle = 0f; // 강제 0 초기화

                if (player.animator != null)
                {
                    player.animator.SetFloat("turnAngle", currentTurnAngle);
                }

                player.isPerformingAction = false;
                return; // 애니메이터 파라미터를 안전하게 리셋한 뒤에 종료합니다.
            }

            // 3. 일반적인 턴 방어 로직
            if (isTurningDebug)
            {
                currentTurnAngle = 0f;
            }
            else
            {
                currentTurnAngle = signedAngle;
            }

            if (player.animator != null)
            {
                player.animator.SetFloat("turnAngle", currentTurnAngle);
            }

            // 4. 턴 애니메이션 발동
            if (absAngle > turnStepAngle && !isTurningDebug)
            {
                isTurningDebug = true;
                targetLookWeight = 0f;

                string targetAnimState = "";

                if (absAngle >= turnPivotAngle)
                {
                    targetAnimState = signedAngle > 0 ? turnRight180StateName : turnLeft180StateName;
                }
                else
                {
                    targetAnimState = signedAngle > 0 ? turnRight90StateName : turnLeft90StateName;
                }

                int animHash = Animator.StringToHash(targetAnimState);
                player.playerAnimationManager.PlayTargetAnimation(animHash, true, true, false, false);

                Debug.Log($"<color=cyan>[PlayerIKController]</color> 턴 애니메이션 발동! (각도: {absAngle:F1}도)");
            }
            else if (absAngle < (turnStepAngle - turnHysteresisOffset))
            {
                // 몸통 회전이 끝나고 카메라와의 시야 오차가 안정권에 들어오면 턴을 해제합니다.
                isTurningDebug = false;
                targetLookWeight = 1.0f; // 다시 IK 시선 개입을 시작
            }
        }
    }
}