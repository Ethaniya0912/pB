using System;
using System.Collections;
using System.Collections.Generic;
using TDA.Character.Player;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [P2 - Locomotion Domain] 플레이어의 물리적 이동 및 회전을 제어하는 최상위 매니저입니다.
/// 
/// [드림코어(Dreamcore) & 리미널 스페이스 아키텍처 설계 철학]
/// 1. 사실적 존재감 (Physical Presence): 
///    - 드림코어 스타일의 기괴한 현실감을 극대화하기 위해 발의 접지력(Root Motion)을 적극 활용합니다.
///    - 스케이팅 현상을 배제하여 플레이어가 공간의 '무게감'을 느끼도록 설계되었습니다.
/// 
/// 2. 하이브리드 제어 (Task 1-10 확장): 
///    - Locomotion: useRootMotionForLocomotion 옵션을 통해 애니메이션 기반 이동을 선택할 수 있습니다.
///    - Action: 공격, 회피 등은 항상 루트 모션을 사용하여 물리적 개연성을 확보합니다.
/// 
/// 3. Procedural Turn & Movement Guard:
///    - 루트 모션이 활성화된 상태에서는 스크립트의 간섭을 완전히 차단하여 물리 연산 중첩을 방지합니다.
/// </summary>
public class PlayerLocomotionManager : CharacterLocomotionManager
{
    private PlayerManager player;

    [Header("Input Values (Synced)")]
    [HideInInspector] public float verticalMovement;
    [HideInInspector] public float horizontalMovement;
    [HideInInspector] public float moveAmount;

    [Header("Locomotion Mode Settings")]
    [Tooltip("드림코어 특유의 사실성을 위해 걷기/뛰기에도 루트 모션을 적용할지 여부입니다. (true 권장)")]
    [SerializeField] private bool useRootMotionForLocomotion = true;

    [Header("Movement Settings (Fallback)")]
    [Tooltip("루트 모션을 사용하지 않을 때 적용되는 스크립트 기반 속도입니다.")]
    private Vector3 moveDirection;
    private Vector3 targetRotationDirection;
    [SerializeField] float walkingSpeed = 2.2f;
    [SerializeField] float runningSpeed = 5.5f;
    [SerializeField] float rotationSpeed = 15f;
    [SerializeField] int dodgeStaminaCost = 10;

    [Header("Dodge & Roll")]
    private Vector3 rollDirection;

    [Header("Gravity & Airborne System (Task 4, 7)")]
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float groundedGravity = -5f;
    [SerializeField] private float jumpForwardSpeed = 4f;
    [SerializeField] private float inAirControl = 2f;

    private Vector3 yVelocity;       // 수직 중력 가속도
    private Vector3 inAirDirection; // 공중 수평 관성

    protected override void Awake()
    {
        base.Awake();
        player = GetComponent<PlayerManager>();
    }

    protected override void Update()
    {
        base.Update();

        if (player.IsOwner)
        {
            // [네트워크 동기화] 현재 입력값을 네트워크 변수에 기록합니다.
            player.characterNetworkManager.animatorVerticalMovement.Value = verticalMovement;
            player.characterNetworkManager.animatorHorizontalMovement.Value = horizontalMovement;
            player.characterNetworkManager.animatorMoveAmountMovement.Value = moveAmount;

            // [루트 모션 동적 스위칭] 
            // 현재 상태와 설정값에 따라 애니메이터의 루트 모션 적용 여부를 실시간으로 결정합니다.
            player.animator.applyRootMotion = DetermineApplyRootMotion();

            // isLockedOn 파라미터 동기화
            player.animator.SetBool("isLockedOn", player.playerNetworkManager.isLockedOn.Value);
        }
        else
        {
            verticalMovement = player.characterNetworkManager.animatorVerticalMovement.Value;
            horizontalMovement = player.characterNetworkManager.animatorHorizontalMovement.Value;
            moveAmount = player.characterNetworkManager.animatorMoveAmountMovement.Value;

            UpdateRemotePlayerAnimations();
        }
    }

    /// <summary>
    /// 현재 캐릭터의 상태를 분석하여 루트 모션 적용 여부를 반환합니다.
    /// </summary>
    private bool DetermineApplyRootMotion()
    {
        // 1. 공격, 구르기, 피격 등 특수 액션 중에는 무조건 루트 모션 사용 (물리적 개연성)
        if (player.isPerformingAction) return true;

        // 2. 이동 중일 때: 사용자가 설정한 로코모션 모드에 따릅니다.
        if (moveAmount > 0) return useRootMotionForLocomotion;

        // 3. 정지 상태(Idle): 루트 모션을 꺼서 애니메이션의 미세한 노이즈로 인한 위치 이탈을 방지합니다.
        return false;
    }

    /// <summary>
    /// 물리 업데이트 루틴. PlayerManager의 FixedUpdate 또는 Update에서 호출됩니다.
    /// </summary>
    public void HandleAllMovement()
    {
        if (!player.IsOwner) return;

        // 중력 연산은 루트 모션 여부와 상관없이 항상 CharacterController가 제어해야 합니다.
        HandleAirborneAndGravity();

        // 지상 이동 처리 (루트 모션 가드 포함)
        HandleGroundedMovement();

        // 회전 처리 (루트 모션 가드 포함)
        HandleRotation();
    }

    // =========================================================================================
    // [입력 수신 핸들러] PlayerManager에서 호출됨
    // =========================================================================================

    /// <summary>
    /// PlayerInputManager로부터 받은 이동 입력을 처리합니다. (Task 8 댐핑 및 스냅핑 포함)
    /// </summary>
    public void OnMovementInputReceived(Vector2 movementInput)
    {
        if (!player.IsOwner) return;

        verticalMovement = movementInput.y;
        horizontalMovement = movementInput.x;

        // 입력을 절대값 기반으로 변환하여 MoveAmount 산출 (0, 0.5, 1 스냅핑)
        moveAmount = Mathf.Clamp01(Mathf.Abs(verticalMovement) + Mathf.Abs(horizontalMovement));

        if (moveAmount <= 0.5f && moveAmount > 0)
        {
            moveAmount = 0.5f; // 걷기 구간
        }
        else if (moveAmount > 0.5f)
        {
            moveAmount = 1.0f; // 달리기 구간
        }

        // 애니메이터 파라미터 업데이트 (Task 8: Damping 적용)
        bool isSprinting = player.playerNetworkManager.isSprinting.Value;

        // 변경: 락온 여부와 상관없이 언제나 자유롭게 8방향 걷기/뛰기가 가능하도록 RAW 입력값을 꽂습니다!
        player.playerAnimationManager.UpdateAnimatorMovementParameters(horizontalMovement, verticalMovement, isSprinting);
    }

    /// <summary>
    /// 회피/구르기 입력 수신 시 실행됩니다.
    /// </summary>
    internal void OnDodgeInputReceived()
    {
        if (!player.IsOwner) return;
        AttemptToPerformDodge();
    }

    // =========================================================================================
    // [물리 연산 로직 & 파라미터 동기화]
    // =========================================================================================

    private void HandleAirborneAndGravity()
    {
        bool isGrounded = player.characterController.isGrounded;

        if (isGrounded)
        {
            if (yVelocity.y < 0)
            {
                yVelocity.y = groundedGravity;
            }
            inAirDirection = Vector3.zero;
        }
        else
        {
            if (inAirDirection == Vector3.zero && moveAmount > 0)
            {
                float speed = moveAmount > 0.5f ? runningSpeed : walkingSpeed;
                inAirDirection = moveDirection * speed;
            }

            Vector3 targetAirDir = Vector3.zero;
            if (player.playerCamera != null)
            {
                targetAirDir = player.playerCamera.transform.forward * verticalMovement + player.playerCamera.transform.right * horizontalMovement;
                targetAirDir.y = 0;
                targetAirDir.Normalize();
            }

            inAirDirection = Vector3.Lerp(inAirDirection, targetAirDir * jumpForwardSpeed, Time.deltaTime * inAirControl);
            player.characterController.Move(inAirDirection * Time.deltaTime);
        }

        yVelocity.y += gravity * Time.deltaTime;
        player.characterController.Move(yVelocity * Time.deltaTime);

        // [여기에 추가!] 땅에 닿아있는지, 그리고 얼마나 빠르게 떨어지는지 애니메이터에 쏴줍니다.
        if (player.animator != null)
        {
            player.animator.SetBool("isGrounded", isGrounded);
            player.animator.SetFloat("verticalVelocity", yVelocity.y);
        }
    }

    private void HandleGroundedMovement()
    {
        if (!player.canMove) return;
        if (player.playerCamera == null) return;
        if (!player.characterController.isGrounded) return;

        // [Task 10 - Root Motion Guard 확장] 
        if (player.animator.applyRootMotion) return;

        moveDirection = player.playerCamera.transform.forward * verticalMovement + player.playerCamera.transform.right * horizontalMovement;
        moveDirection.Normalize();
        moveDirection.y = 0;

        float currentSpeed = moveAmount > 0.5f ? runningSpeed : walkingSpeed;
        player.characterController.Move(moveDirection * currentSpeed * Time.deltaTime);
    }

    private void HandleRotation()
    {
        if (player.playerNetworkManager.isDead.Value) return;
        if (!player.canRotate) return;
        if (player.playerCamera == null) return;

        if (player.animator.applyRootMotion) return;

        if (player.playerNetworkManager.isLockedOn.Value)
        {
            HandleLockedOnRotation();
        }
        else
        {
            HandleStandardRotation();
        }
    }

    private void HandleStandardRotation()
    {
        if (moveAmount > 0)
        {
            targetRotationDirection = Vector3.zero;
            targetRotationDirection = player.playerCamera.cameraObject.transform.forward * verticalMovement;
            targetRotationDirection += player.playerCamera.cameraObject.transform.right * horizontalMovement;
            targetRotationDirection.Normalize();
            targetRotationDirection.y = 0;

            if (targetRotationDirection == Vector3.zero) targetRotationDirection = transform.forward;

            Quaternion newRotation = Quaternion.LookRotation(targetRotationDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, newRotation, rotationSpeed * Time.deltaTime);
        }
    }

    private void HandleLockedOnRotation()
    {
        if (player.playerNetworkManager.isSprinting.Value || isRolling)
        {
            HandleStandardRotation();
            return;
        }

        if (player.playerCombatManager.currentTarget == null) return;

        Vector3 targetDirection = player.playerCombatManager.currentTarget.transform.position - transform.position;
        targetDirection.y = 0;
        targetDirection.Normalize();

        Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    public void AttemptToPerformJump()
    {
        if (player.isPerformingAction) return;
        if (player.playerNetworkManager.currentStamina.Value <= 0) return;
        if (!player.characterController.isGrounded) return;

        player.playerAnimationManager.PlayTargetAnimation(Animator.StringToHash("Jump_Start"), false, false);
        yVelocity.y = Mathf.Sqrt(1.5f * -2f * gravity);

        if (moveAmount > 0 && player.playerCamera != null)
        {
            Vector3 jumpDir = player.playerCamera.transform.forward * verticalMovement + player.playerCamera.transform.right * horizontalMovement;
            jumpDir.y = 0;
            inAirDirection = jumpDir.normalized * (moveAmount > 0.5f ? runningSpeed : walkingSpeed);
        }
    }

    public void AttemptToPerformDodge()
    {
        if (player.isPerformingAction) return;
        if (player.playerNetworkManager.currentStamina.Value <= 0) return;

        if (moveAmount > 0)
        {
            rollDirection = player.playerCamera.cameraObject.transform.forward * verticalMovement + player.playerCamera.cameraObject.transform.right * horizontalMovement;
            rollDirection.y = 0;
            rollDirection.Normalize();

            transform.rotation = Quaternion.LookRotation(rollDirection);

            player.playerAnimationManager.PlayTargetAnimation(Animator.StringToHash("Roll_forward_01"), true, true);
            isRolling = true;
        }
        else
        {
            player.playerAnimationManager.PlayTargetAnimation(Animator.StringToHash("Back_Step_01"), true, true);
        }

        player.playerNetworkManager.currentStamina.Value -= dodgeStaminaCost;
    }

    private void UpdateRemotePlayerAnimations()
    {
        bool isSprinting = player.playerNetworkManager.isSprinting.Value;

        // [수정: 리모트 플레이어(다른 유저)의 애니메이션도 락온 제한 없이 완벽한 8방향으로 동기화합니다.]
        player.playerAnimationManager.UpdateAnimatorMovementParameters(horizontalMovement, verticalMovement, isSprinting);
    }
}