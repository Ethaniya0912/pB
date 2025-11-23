using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
        if (player.IsOwner)
        {
            player.characterNetworkManager.animatorVerticalMovement.Value = verticalMovement;
            player.characterNetworkManager.animatorHorizontalMovement.Value = horizontalMovement;
            player.characterNetworkManager.animatorMoveAmountMovement.Value = moveAmount;
        }
        else
        {
            verticalMovement = player.characterNetworkManager.animatorVerticalMovement.Value;
            horizontalMovement = player.characterNetworkManager.animatorHorizontalMovement.Value;
            moveAmount = player.characterNetworkManager.animatorMoveAmountMovement.Value;

            // 락온 안됫을 시, move amount 전달.
            player.playerAnimationManager.UpdateAnimatorMovementParameters(0, moveAmount);

            // 락온 되었을 시, 수평/수직값 전달.
        }
    }

    public void HandleAllMovement()
    {
        // 땅위 움직임
        HandleGroundedMovement();
        HandleRotation();
        // 공중 움직임.
    }

    private void GetMovementValues()
    {
        verticalMovement = PlayerInputManager.Instance.verticalInput;
        horizontalMovement = PlayerInputManager.Instance.horizontalInput;
        moveAmount = PlayerInputManager.Instance.moveAmount;

        // Clamp the Movements
    }

    private void HandleGroundedMovement()
    {
        if (!player.canMove)
            return;
        GetMovementValues();

        // 움직임은 카메라 방향과 인풋에 따라 결정됨.
        moveDirection = PlayerCamera.Instance.transform.forward * verticalMovement;
        moveDirection = moveDirection + PlayerCamera.Instance.transform.right * horizontalMovement;
        moveDirection.Normalize();
        moveDirection.y = 0;

        if (PlayerInputManager.Instance.moveAmount > 0.5f)
        {
            player.characterController.Move(moveDirection * runningSpeed * Time.deltaTime);
        }
        else if (PlayerInputManager.Instance.moveAmount <= 0.5f)
        {
            player.characterController.Move(moveDirection * walkingSpeed * Time.deltaTime);
        }
    }

    private void HandleRotation()
    {
        if (!player.canRotate)
            return;
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

    public void AttemptToPerformDodge()
    {
        if (player.isPerformingAction)
            return;

        if (player.playerNetworkManager.currentStamina.Value <= 0)
            return;

        // 움직이던 도중 dodge 실행 시 roll 실행
        if(PlayerInputManager.Instance.moveAmount > 0)
        {
            rollDirection = 
                PlayerCamera.Instance.cameraObject.transform.forward * 
                PlayerInputManager.Instance.verticalInput;
            rollDirection +=
                PlayerCamera.Instance.cameraObject.transform.right *
                PlayerInputManager.Instance.horizontalInput;

            // y 값 없이 좌우로만.
            rollDirection.y = 0;
            rollDirection.Normalize();
            // roll의 로테이션을 가져오기(roll 하기 원하는 방향으로)
            Quaternion playerRotation = Quaternion.LookRotation(rollDirection);
            // 플레이어에게 해당 로테이션 적용해주기.
            player.transform.rotation = playerRotation;

            // 롤 애니메이션을 실행한다.
            player.playerAnimationManager.PlayTargetAnimation("Roll_forward_01", true);

            // 스태미나 값을 제해준다.
            player.playerNetworkManager.currentStamina.Value -= dodgeStaminaCost;
        }
        // 정적일 경우 백스텝 실행
        else
        {
            // 백스텝 애니메이션 실행
        }
    }
}
