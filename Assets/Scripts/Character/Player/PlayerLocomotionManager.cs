using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode; // NetworkBehaviour 기능을 위해 추가

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

    protected override void Awake()
    {
        base.Awake();
        player = GetComponent<PlayerManager>();
    }

    protected override void Update()
    {
        base.Update();

        // [수정] 본인(Owner)일 경우에만 인풋 값을 네트워크 변수에 동기화합니다.
        if (player.IsOwner)
        {
            player.characterNetworkManager.animatorVerticalMovement.Value = verticalMovement;
            player.characterNetworkManager.animatorHorizontalMovement.Value = horizontalMovement;
            player.characterNetworkManager.animatorMoveAmountMovement.Value = moveAmount;
        }
        else
        {
            // [수정] 타인(Remote Player)일 경우 네트워크에서 받은 값을 로컬 변수에 적용합니다.
            verticalMovement = player.characterNetworkManager.animatorVerticalMovement.Value;
            horizontalMovement = player.characterNetworkManager.animatorHorizontalMovement.Value;
            moveAmount = player.characterNetworkManager.animatorMoveAmountMovement.Value;

            // [수정] 타인의 애니메이션 파라미터를 업데이트합니다. 
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
        // [수정] 본인이 아닌 경우(타인)는 직접 이동/회전 로직을 계산하지 않습니다.
        // 타인의 움직임은 NetworkTransform 컴포넌트를 통해 자동으로 동기화됩니다.
        if (!player.IsOwner) return;

        // 땅위 움직임
        HandleGroundedMovement();
        HandleRotation();
        // 공중 움직임.
    }

    public void OnMovementInputReceived(Vector2 movementInput)
    {
        // [수정] 본인이 아닐 경우 인풋 처리를 무시합니다.
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

        // [수정] 카메라 인스턴스가 있는지 안전 점검 후 방향 계산
        if (PlayerCamera.Instance == null) return;

        // 움직임은 카메라 방향과 인풋에 따라 결정됨.
        moveDirection = PlayerCamera.Instance.transform.forward * verticalMovement;
        moveDirection = moveDirection + PlayerCamera.Instance.transform.right * horizontalMovement;
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
        if (PlayerCamera.Instance == null) return;

        if (player.playerNetworkManager.isLockedOn.Value)
        {
            // 스프린팅 하는 동안 타겟기준 좌우로 움직이지 않고 자유롭게 움직임.
            if (player.playerNetworkManager.isSprinting.Value || player.playerLocomotionManager.isRolling)
            {
                Vector3 targetDirection = Vector3.zero;
                targetDirection = PlayerCamera.Instance.cameraObject.transform.forward * verticalMovement;
                targetDirection += PlayerCamera.Instance.cameraObject.transform.right * horizontalMovement;
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
            targetRotationDirection = PlayerCamera.Instance.cameraObject.transform.forward * verticalMovement;
            targetRotationDirection = targetRotationDirection + PlayerCamera.Instance.cameraObject.transform.right * horizontalMovement;
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
        if (PlayerCamera.Instance == null) return;

        // 움직이던 도중 dodge 실행 시 roll 실행
        if (moveAmount > 0)
        {
            rollDirection = PlayerCamera.Instance.cameraObject.transform.forward * verticalMovement;
            rollDirection += PlayerCamera.Instance.cameraObject.transform.right * horizontalMovement;

            // y 값 없이 좌우로만.
            rollDirection.y = 0;
            rollDirection.Normalize();

            // roll의 로테이션을 가져오기(roll 하기 원하는 방향으로)
            Quaternion playerRotation = Quaternion.LookRotation(rollDirection);
            // 플레이어에게 해당 로테이션 적용해주기.
            player.transform.rotation = playerRotation;

            // 롤 애니메이션을 실행한다.
            player.playerAnimationManager.PlayTargetAnimation("Roll_forward_01", true);
            player.playerLocomotionManager.isRolling = true;

            // 스태미나 값을 제해준다. (서버 권한에 유의)
            player.playerNetworkManager.currentStamina.Value -= dodgeStaminaCost;
        }
        // 정적일 경우 백스텝 실행
        else
        {
            // 백스텝 애니메이션 실행
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