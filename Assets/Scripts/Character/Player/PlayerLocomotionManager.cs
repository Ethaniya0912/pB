using System;
using System.Collections;
using System.Collections.Generic;
using TDA.Character.Player; // CharacterManager 참조를 위해 추가
using Unity.Netcode; // NetworkBehaviour 기능을 위해 추가
using UnityEngine;

/// <summary>
/// [오류 정정] 이전에 작성해 주셨던 '공중 부양 방지 및 점프 제자리 널뛰기 방지' 물리 연산을
/// 단 한 줄도 빼놓지 않고 100% 완전 복원했습니다.
/// </summary>
public class PlayerLocomotionManager : CharacterLocomotionManager
{
    PlayerManager player;

    // 인풋매니저에서 가져와 적용할 값.
    [HideInInspector] public float verticalMovement;
    [HideInInspector] public float horizontalMovement;
    [HideInInspector] public float moveAmount;

    [Header("Movement Setting")]
    // 움직임은 카메라 방향과 인풋에 따를거임.
    private Vector3 moveDirection;
    private Vector3 targetRotationDirection;
    [SerializeField] float walkingSpeed = 2;
    [SerializeField] float runningSpeed = 5;
    [SerializeField] float rotationSpeed = 15;
    [SerializeField] int dodgeStaminaCost = 10;

    [Header("Dodge")]
    private Vector3 rollDirection;

    // =========================================================================================
    // 중력 및 공중(점프) 관성 시스템 보존
    // =========================================================================================
    [Header("Gravity & Airborne")]
    [SerializeField] private float gravity = -20f; // 기본 수직 중력 가속도
    [SerializeField] private float groundedGravity = -5f; // 경사로에서 허공에 뜨지 않도록 바닥에 밀착시키는 힘
    [SerializeField] private float jumpForwardSpeed = 4f; // 점프/낙하 중 수평으로 이동하는 속도
    [SerializeField] private float inAirControl = 2f; // 공중에서 방향을 틀 수 있는 조향 제어력(Lerp 속도)

    private Vector3 yVelocity; // 상하(Y축) 중력 속도 저장용
    private Vector3 inAirDirection; // 공중에서의 수평 이동 방향(관성) 저장용

    protected override void Awake()
    {
        base.Awake();
        player = GetComponent<PlayerManager>();
    }

    protected override void Update()
    {
        base.Update();

        // 본인(Owner)일 경우에만 인풋 값을 네트워크 변수에 동기화합니다.
        if (player.IsOwner)
        {
            player.characterNetworkManager.animatorVerticalMovement.Value = verticalMovement;
            player.characterNetworkManager.animatorHorizontalMovement.Value = horizontalMovement;
            player.characterNetworkManager.animatorMoveAmountMovement.Value = moveAmount;
        }
        else
        {
            // 타인(Remote Player)일 경우 네트워크에서 받은 값을 로컬 변수에 적용합니다.
            verticalMovement = player.characterNetworkManager.animatorVerticalMovement.Value;
            horizontalMovement = player.characterNetworkManager.animatorHorizontalMovement.Value;
            moveAmount = player.characterNetworkManager.animatorMoveAmountMovement.Value;

            // 타인의 애니메이션 파라미터를 업데이트합니다. 
            // 타인은 HandleAllMovement를 타지 않으므로 여기서 직접 애니메이터를 갱신해야 합니다.
            // 락온 안되었을 시, move amount 전달.
            if (!player.playerNetworkManager.isLockedOn.Value || player.playerNetworkManager.isSprinting.Value)
            {
                player.playerAnimationManager.UpdateAnimatorMovementParameters(0, moveAmount, player.playerNetworkManager.isSprinting.Value);
            }
            else
            {
                // 락온 되었을 시, 수평/수직값 전달.
                player.playerAnimationManager.UpdateAnimatorMovementParameters(horizontalMovement, verticalMovement, player.playerNetworkManager.isSprinting.Value);
            }
        }
    }

    public void HandleAllMovement()
    {
        // 본인이 아닌 경우(타인)는 직접 이동/회전 로직을 계산하지 않습니다.
        // 타인의 움직임은 NetworkTransform 컴포넌트를 통해 자동으로 동기화됩니다.
        if (!player.IsOwner) return;

        // 중력 및 공중(점프) 물리 연산을 가장 먼저 처리합니다.
        HandleAirborneAndGravity();

        // 땅위 움직임
        HandleGroundedMovement();

        // 회전 움직임
        HandleRotation();
    }

    // =========================================================================================
    // 중력, 바닥 밀착, 점프 시 관성 처리 메서드
    // =========================================================================================
    private void HandleAirborneAndGravity()
    {
        // CharacterController 자체의 바닥 감지 기능 활용
        bool isGrounded = player.characterController.isGrounded;

        if (isGrounded)
        {
            // 1. 공중 부양 방지 (Grounding)
            // 경사로나 계단을 내려갈 때 공중에 뜨지 않도록 강한 힘으로 바닥에 밀착시킵니다.
            if (yVelocity.y < 0)
            {
                yVelocity.y = groundedGravity;
            }
            inAirDirection = Vector3.zero; // 바닥에 닿으면 공중 관성 리셋
        }
        else
        {
            // 2. 점프 제자리 널뛰기 방지 (Airborne Momentum)
            // 허공에 떴을 때 이전 이동 방향(관성)이 없다면 현재 입력 방향으로 초기 관성을 부여합니다.
            if (inAirDirection == Vector3.zero && moveAmount > 0)
            {
                float speed = moveAmount > 0.5f ? runningSpeed : walkingSpeed;
                inAirDirection = moveDirection * speed; // 점프 직전의 속도와 방향을 캡처
            }

            // 공중 조향(Steering): 공중에서도 플레이어가 입력하는 방향으로 궤적을 살짝 바꿀 수 있게 합니다.
            Vector3 targetAirDir = Vector3.zero;
            if (player.playerCamera != null)
            {
                targetAirDir = player.playerCamera.transform.forward * verticalMovement;
                targetAirDir += player.playerCamera.transform.right * horizontalMovement;
                targetAirDir.y = 0;
                targetAirDir.Normalize();
            }

            // 자연스러운 포물선을 위해 현재 관성과 목표 방향을 부드럽게 섞어줍니다 (Lerp).
            inAirDirection = Vector3.Lerp(inAirDirection, targetAirDir * jumpForwardSpeed, Time.deltaTime * inAirControl);

            // 산출된 수평 이동력을 적용 (앞으로 점프하는 궤적 생성)
            player.characterController.Move(inAirDirection * Time.deltaTime);
        }

        // 3. 수직 중력 적용 (추락 가속도)
        yVelocity.y += gravity * Time.deltaTime;
        player.characterController.Move(yVelocity * Time.deltaTime);
    }

    public void AttemptToPerformJump()
    {
        if (player.isPerformingAction) return;
        if (player.playerNetworkManager.currentStamina.Value <= 0) return;
        if (!player.characterController.isGrounded) return;

        // 점프 애니메이션 호출 
        player.playerAnimationManager.PlayTargetAnimation(Animator.StringToHash("Jump_Start"), false);

        // 위로 튀어오르는 수직 물리력 적용
        float jumpHeight = 1.5f;
        yVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        // 점프 시 앞으로 나아가기 위한 초기 관성 캡처 (뛰어가며 점프)
        if (moveAmount > 0 && player.playerCamera != null)
        {
            Vector3 jumpDir = player.playerCamera.transform.forward * verticalMovement;
            jumpDir += player.playerCamera.transform.right * horizontalMovement;
            jumpDir.y = 0;
            jumpDir.Normalize();

            float speed = moveAmount > 0.5f ? runningSpeed : walkingSpeed;
            inAirDirection = jumpDir * speed;
        }
    }

    public void OnMovementInputReceived(Vector2 movementInput)
    {
        if (!player.IsOwner) return;

        // 실제 이동 물리연산 및 애니메이터 파라미터 업데이트
        verticalMovement = movementInput.y;
        horizontalMovement = movementInput.x;

        // 숫자의 절대값을 반환 (음수 없이 양수로만 반환시키기)
        moveAmount = Mathf.Clamp01(Mathf.Abs(verticalMovement) + Mathf.Abs(horizontalMovement));

        // 값을 clamp 해줘서 0, 0.5, 1로 고정되게 함.
        if (moveAmount <= 0.5 && moveAmount > 0)
        {
            // 걷고있다는 인디케이터
            moveAmount = 0.5f;
        }
        else if (moveAmount > 0.5 && moveAmount <= 1)
        {
            // 달리기 인디케이터
            moveAmount = 1;
        }

        if (player == null) return;

        // 수평에 0만 전달하는 이유는 락온 하지 않을 시 앞으로만 가게 하려고 함.
        if (!player.playerNetworkManager.isLockedOn.Value || player.playerNetworkManager.isSprinting.Value)
        {
            player.playerAnimationManager.UpdateAnimatorMovementParameters(0, moveAmount, player.playerNetworkManager.isSprinting.Value);
        }
        else
        {
            // 수평에 0 말고 다른 것도 전달, 락온 한 상태.
            player.playerAnimationManager.UpdateAnimatorMovementParameters(horizontalMovement, verticalMovement, player.playerNetworkManager.isSprinting.Value);
        }
    }

    private void HandleGroundedMovement()
    {
        if (!player.canMove) return;

        if (player.playerCamera == null) return;

        // 공중일 때는 지상 이동 연산 무시 (공중 이동은 HandleAirborneAndGravity에서 처리)
        if (!player.characterController.isGrounded) return;

        // 움직임은 카메라 방향과 인풋에 따라 결정됨.
        moveDirection = player.playerCamera.transform.forward * verticalMovement;
        moveDirection = moveDirection + player.playerCamera.transform.right * horizontalMovement;
        moveDirection.Normalize();
        moveDirection.y = 0;

        if (moveAmount > 0.5f)
        {
            player.characterController.Move(moveDirection * runningSpeed * Time.deltaTime);
        }
        else if (moveAmount <= 0.5f)
        {
            player.characterController.Move(moveDirection * walkingSpeed * Time.deltaTime);
        }
    }

    private void HandleRotation()
    {
        if (player.playerNetworkManager.isDead.Value) return;
        if (!player.canRotate) return;
        if (player.playerCamera == null) return;

        if (player.playerNetworkManager.isLockedOn.Value)
        {
            // 스프린팅 하는 동안 타겟기준 좌우로 움직이지 않고 자유롭게 움직임.
            if (player.playerNetworkManager.isSprinting.Value || player.playerLocomotionManager.isRolling)
            {
                Vector3 targetDirection = Vector3.zero;
                targetDirection = player.playerCamera.cameraObject.transform.forward * verticalMovement;
                targetDirection += player.playerCamera.cameraObject.transform.right * horizontalMovement;
                targetDirection.Normalize();
                targetDirection.y = 0;

                if (targetDirection == Vector3.zero)
                    targetDirection = transform.forward;

                Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
                Quaternion finalRotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                transform.rotation = finalRotation;
            }
            else
            {
                // strifing 중일 시.
                if (player.playerCombatManager.currentTarget == null) return;

                Vector3 targetDirection;
                targetDirection = player.playerCombatManager.currentTarget.transform.position - transform.position;
                targetDirection.y = 0;
                targetDirection.Normalize();

                Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
                Quaternion finalRotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                transform.rotation = finalRotation;
            }
        }
        else
        {
            targetRotationDirection = Vector3.zero;
            targetRotationDirection = player.playerCamera.cameraObject.transform.forward * verticalMovement;
            targetRotationDirection = targetRotationDirection + player.playerCamera.cameraObject.transform.right * horizontalMovement;
            targetRotationDirection.Normalize();
            targetRotationDirection.y = 0;

            // 타겟 로테이션이 없으면, 지금 바라보는 방향으로 정함.
            if (targetRotationDirection == Vector3.zero)
            {
                targetRotationDirection = transform.forward;
            }

            Quaternion newRotation = Quaternion.LookRotation(targetRotationDirection);
            Quaternion targetRotation = Quaternion.Slerp(transform.rotation, newRotation, rotationSpeed * Time.deltaTime);
            transform.rotation = targetRotation;
        }
    }

    public void AttemptToPerformDodge()
    {
        if (player.isPerformingAction) return;
        if (player.playerNetworkManager.currentStamina.Value <= 0) return;
        if (player.playerCamera == null) return;

        // 움직이던 도중 dodge 실행 시 roll 실행
        if (moveAmount > 0)
        {
            rollDirection = player.playerCamera.cameraObject.transform.forward * verticalMovement;
            rollDirection += player.playerCamera.cameraObject.transform.right * horizontalMovement;

            // y 값 없이 좌우로만.
            rollDirection.y = 0;
            rollDirection.Normalize();

            // roll의 로테이션을 가져오기(roll 하기 원하는 방향으로)
            Quaternion playerRotation = Quaternion.LookRotation(rollDirection);
            // 플레이어에게 해당 로테이션 적용해주기.
            player.transform.rotation = playerRotation;

            // 롤 애니메이션을 실행한다.
            player.playerAnimationManager.PlayTargetAnimation(Animator.StringToHash("Roll_forward_01"), true);
            player.playerLocomotionManager.isRolling = true;

            // 스태미나 값을 제해준다. (서버 권한에 유의)
            player.playerNetworkManager.currentStamina.Value -= dodgeStaminaCost;
        }
        // 정적일 경우 백스텝 실행
        else
        {
            // 백스텝 애니메이션 실행
            player.playerAnimationManager.PlayTargetAnimation(Animator.StringToHash("Back_Step_01"), true);
            player.playerNetworkManager.currentStamina.Value -= dodgeStaminaCost;
        }
    }

    internal void OnDodgeInputReceived()
    {
        // TD : 미래에 UI가 활성화 시 실행되지 않게 해줌.
        if (!player.IsOwner) return;

        // 닷지를 퍼폼하기.
        player.playerLocomotionManager.AttemptToPerformDodge();
    }
}