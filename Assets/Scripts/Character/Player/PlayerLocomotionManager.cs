using System;
using System.Collections;
using System.Collections.Generic;
using TDA.Character;
using TDA.Character.Player;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [P2 - Locomotion Domain] 플레이어의 물리적 이동 및 회전을 제어하는 최상위 매니저입니다.
/// </summary>
public class PlayerLocomotionManager : CharacterLocomotionManager
{
    private PlayerManager player;

    // =========================================================================================
    // [상속 중복 에러 해결] 
    // 아래 변수들은 부모 클래스(CharacterLocomotionManager)에 이미 존재하는 변수들입니다.
    // 자식에서 동일한 이름으로 재선언하면 유니티 직렬화 충돌(Serialization Error)이 발생하므로 제거(주석 처리)했습니다.
    // 
    // ※ 만약 접근 불가(Protection Level) 에러가 발생한다면, CharacterLocomotionManager.cs에서 
    // 해당 변수들을 'private' 대신 'protected'나 'public'으로 수정해주세요!
    // =========================================================================================

    /* --- 부모 클래스에 존재하는 변수들 (제거됨) ---
    public float verticalMovement;
    public float horizontalMovement;
    public float moveAmount;
    private bool useRootMotionForLocomotion;
    private Vector3 moveDirection;
    float walkingSpeed;
    float runningSpeed;
    float rotationSpeed;
    int dodgeStaminaCost;
    float gravity;
    float groundedGravity;
    float jumpForwardSpeed;
    float inAirControl;
    private Vector3 yVelocity;
    private Vector3 inAirDirection;
    public bool isRotationLockedByEvent;
    --------------------------------------------- */

    // [신규] 애니메이션 시각적 보간 전용 변수 (부모에 없는 플레이어 전용 변수이므로 유지)
    [HideInInspector] public float animVerticalMovement;
    [HideInInspector] public float animHorizontalMovement;

    [Header("Movement Settings (Player Specific)")]
    private Vector3 targetRotationDirection;
    private Vector3 rollDirection;

    [Header("Steering & Shoulder View Settings")]
    [Tooltip("마우스 스티어링(몸 기울임) 시 민감도를 조절합니다. 값이 클수록 덜 기울어집니다. (권장: 30 ~ 70)")]
    [SerializeField] private float steeringSensitivity = 50f;

    [Tooltip("S키(후진)를 누른 상태에서 마우스를 돌릴 때, 캐릭터가 좌우로 기울어지는 방향(Horizontal)을 반전시킬지 여부입니다.")]
    [SerializeField] private bool invertSteeringOnBackward = true;

    [Tooltip("숄더뷰 전용: S키(후진)를 눌러도 캐릭터가 뒤돌아보지 않고 항상 카메라 정면을 주시하며 뒷걸음질 치게 만듭니다. (턴 애니메이션 오작동 방지)")]
    [SerializeField] private bool alwaysFaceCameraForward = true;

    [Header("Environment & Obstacle Checks")]
    [Tooltip("경사로/턱에서 isGrounded가 풀려 주춤거리는 것을 방지하기 위한 보정 거리입니다.")]
    [SerializeField] private float groundCheckLeniency = 0.2f;
    [Tooltip("벽 앞에서 제자리 뛰기를 방지하기 위한 전방 장애물 감지 거리입니다. (캐릭터 반지름 + 알파)")]
    [SerializeField] private float obstacleCheckDistance = 0.5f;

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
            // 물리 입력이 아닌 "시각적으로 보정된 애니메이션 값"을 서버로 넘겨 다른 사람들도 자연스러운 걷기를 보게 합니다.
            player.characterNetworkManager.animatorVerticalMovement.Value = animVerticalMovement;
            player.characterNetworkManager.animatorHorizontalMovement.Value = animHorizontalMovement;
            player.characterNetworkManager.animatorMoveAmountMovement.Value = moveAmount;

            player.animator.applyRootMotion = DetermineApplyRootMotion();
            player.animator.SetBool("isLockedOn", player.playerNetworkManager.isLockedOn.Value);
        }
        else
        {
            animVerticalMovement = player.characterNetworkManager.animatorVerticalMovement.Value;
            animHorizontalMovement = player.characterNetworkManager.animatorHorizontalMovement.Value;
            moveAmount = player.characterNetworkManager.animatorMoveAmountMovement.Value;

            UpdateRemotePlayerAnimations();
        }
    }

    private bool DetermineApplyRootMotion()
    {
        if (player.isPerformingAction) return true;
        // useRootMotionForLocomotion은 부모 클래스의 값을 가져와 사용합니다.
        if (moveAmount > 0) return useRootMotionForLocomotion;
        return false;
    }

    public void HandleAllMovement()
    {
        if (!player.IsOwner) return;

        HandleAirborneAndGravity();
        HandleGroundedMovement();
        HandleRotation();

        // ── Motion Blur: 이동 상태 반영 ───────────────────────────────────
        // isPerformingAction 중에는 UpdateLocomotionBlur 내부에서 skip 되므로
        // 공격/피격 중 Attack 블러가 보존됩니다.
        if (player.characterEffectsManager is TDA.Character.Player.PlayerEffectsManager pem)
            pem.UpdateLocomotionBlur(moveAmount, player.isPerformingAction);
    }

    public void OnMovementInputReceived(Vector2 movementInput)
    {
        if (!player.IsOwner) return;

        // 1. 물리적 방향을 결정하는 순수 입력값은 건드리지 않고 보존합니다. (나선형 버그 방지)
        verticalMovement = movementInput.y;
        horizontalMovement = movementInput.x;

        moveAmount = Mathf.Clamp01(Mathf.Abs(verticalMovement) + Mathf.Abs(horizontalMovement));

        if (moveAmount <= 0.5f && moveAmount > 0) { moveAmount = 0.5f; }
        else if (moveAmount > 0.5f) { moveAmount = 1.0f; }

        // =========================================================================================
        // 🚨 [버그 수정 2] 벽 충돌 시 제자리 뛰기(Wall Running) 현상 방지
        // =========================================================================================
        Vector3 intendedDir = Vector3.zero;
        if (player.playerCamera != null)
        {
            // 플레이어가 가고자 하는 절대적인 방향(World Space)을 도출합니다.
            intendedDir = player.playerCamera.cameraObject.transform.forward * verticalMovement
                        + player.playerCamera.cameraObject.transform.right * horizontalMovement;
            intendedDir.y = 0;
            intendedDir.Normalize();
        }

        if (moveAmount > 0 && intendedDir != Vector3.zero)
        {
            Vector3 origin = transform.position + Vector3.up * 1.0f; // 가슴 높이
            Vector3 rightOffset = transform.right * 0.25f; // 어깨 너비 보정

            // 정면, 좌측, 우측 세 줄기의 레이캐스트로 벽을 면밀히 감지합니다. (모서리까지 완벽히 캐치)
            // 콜라이더가 자기 자신(Player)이 아닌 오브젝트와 부딪혔을 때만 장애물로 판단합니다.
            bool hitCenter = Physics.Raycast(origin, intendedDir, out RaycastHit cHit, obstacleCheckDistance) && !cHit.collider.isTrigger && cHit.collider.transform.root != transform.root;
            bool hitLeft = Physics.Raycast(origin - rightOffset, intendedDir, out RaycastHit lHit, obstacleCheckDistance) && !lHit.collider.isTrigger && lHit.collider.transform.root != transform.root;
            bool hitRight = Physics.Raycast(origin + rightOffset, intendedDir, out RaycastHit rHit, obstacleCheckDistance) && !rHit.collider.isTrigger && rHit.collider.transform.root != transform.root;

            if (hitCenter || hitLeft || hitRight)
            {
                // 코앞이 벽으로 막혀있다면 물리/애니메이션 이동 입력을 강제로 0(대기)으로 만듭니다.
                moveAmount = 0f;
                verticalMovement = 0f;
                horizontalMovement = 0f;
            }
        }
        // =========================================================================================

        bool isSprinting = player.playerNetworkManager.isSprinting.Value;

        // =========================================================================================
        // 🚨 [AAA급 마우스 스티어링 (Mouse Delta Leaning)] 덜덜거림(Jittering) 및 민감도 완벽 해결
        // =========================================================================================
        // A/D 키 입력이 없고(직진/후진 중) 락온이 아닐 때만 마우스 조작에 따라 몸을 기울입니다.
        if (!player.playerNetworkManager.isLockedOn.Value && moveAmount > 0.1f && Mathf.Abs(horizontalMovement) < 0.1f)
        {
            // 데이터의 원천인 PlayerInputManager에서 직접 마우스 픽셀 델타를 가져옵니다.
            float mouseDeltaX = PlayerInputManager.Instance.cameraInput.x;

            float steeringValue = 0f;

            // 마우스의 미세한 떨림(노이즈)은 무시하도록 데드존 적용 (픽셀 단위이므로 0.1f 이상으로 넉넉히 줌)
            if (Mathf.Abs(mouseDeltaX) > 0.1f)
            {
                // 인스펙터에서 설정한 steeringSensitivity 값을 사용하여 스케일링합니다.
                steeringValue = Mathf.Clamp(mouseDeltaX / steeringSensitivity, -1f, 1f);
            }

            // [요청 반영] S키(후진) 입력 시 스티어링 애니메이션의 좌/우 방향을 반전시킵니다.
            if (invertSteeringOnBackward && verticalMovement < 0)
            {
                steeringValue = -steeringValue;
            }

            // 기존 animHorizontalMovement를 유지하면서 마우스 델타에 따라 부드럽게 섞어줍니다.
            // [조작감 개선] 훅 넘어가는 답답함을 없애고 빠릿하게 복귀하도록 보간 속도를 3f -> 6f 로 상향했습니다.
            animHorizontalMovement = Mathf.Lerp(animHorizontalMovement, steeringValue, 6f * Time.deltaTime);

            // Lerp 강제 0 수렴 로직 (방지턱 제거 후 복귀를 더 깔끔하게 하기 위해 오차범위 좁힘)
            if (steeringValue == 0f && Mathf.Abs(animHorizontalMovement) < 0.05f)
            {
                animHorizontalMovement = 0f;
            }

            animVerticalMovement = verticalMovement;
        }
        else
        {
            // 마우스를 이용한 스티어링이 아닐 때(수동 A,D 입력 등)는 순수 물리 입력값을 씁니다.
            animHorizontalMovement = horizontalMovement;
            animVerticalMovement = verticalMovement;
        }
        // =========================================================================================

        // 최종적으로 시각 보정이 완료된 anim 변수들만 애니메이터로 보냅니다.
        player.playerAnimationManager.UpdateAnimatorMovementParameters(animHorizontalMovement, animVerticalMovement, isSprinting);
    }

    internal void OnDodgeInputReceived()
    {
        if (!player.IsOwner) return;
        AttemptToPerformDodge();
    }

    private void HandleAirborneAndGravity()
    {
        bool isGrounded = player.characterController.isGrounded;

        // =========================================================================================
        // 🚨 [버그 수정 1] 턱/경사로 스케이팅(주춤거림) 방지 - 하드웨어 Coyote Time
        // =========================================================================================
        if (!isGrounded)
        {
            // 발끝보다 살짝 위(0.1f)에서 아래로 레이캐스트를 쏴서 바닥이 지정된 Leniency(유예 거리) 내에 있다면
            // 공중에 뜬 것이 아니라 지면의 굴곡을 타고 넘는 것으로 간주하여 강제로 접지(Grounded) 판정을 내립니다.
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundCheckLeniency + 0.1f))
            {
                if (!hit.collider.isTrigger && hit.collider.transform.root != transform.root)
                {
                    isGrounded = true;
                }
            }
        }
        // =========================================================================================

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

        // [추락 한계 방어]
        yVelocity.y = Mathf.Max(yVelocity.y, -50f);
        player.characterController.Move(yVelocity * Time.deltaTime);

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

        // 루트 모션 가드 (중첩 이동 방지)
        if (player.animator.applyRootMotion) return;

        // 물리 이동은 보정되지 않은 '순수 입력값'만 사용하여 직진성을 완벽히 유지합니다.
        moveDirection = player.playerCamera.transform.forward * verticalMovement + player.playerCamera.transform.right * horizontalMovement;
        moveDirection.Normalize();
        moveDirection.y = 0;

        float currentSpeed = moveAmount > 0.5f ? runningSpeed : walkingSpeed;
        player.characterController.Move(moveDirection * currentSpeed * Time.deltaTime);
    }

    /// <summary>
    /// [핵심 수정: 동적 지연 회전 및 SO 플래그 연동]
    /// </summary>
    private void HandleRotation()
    {
        if (player.playerNetworkManager.isDead.Value) return;

        // 1. 구르기 중에는 강제 개입 불가
        if (isRolling) return;

        // 2. 액션 중(공격 등)일 때는 SO 데이터와 Enum 이벤트의 허가를 받아야 함
        if (player.isPerformingAction)
        {
            if (!player.canRotate) return; // SO에서 회전을 금지했는가?
            if (isRotationLockedByEvent) return; // Enum 이벤트로 일시 락이 걸렸는가?
        }

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

            // [버그 수정] S키 연타 시 몸이 180도 뒤집혀서 Turn 애니메이션이 오작동하는 현상 방지
            if (alwaysFaceCameraForward)
            {
                // 숄더뷰 모드: 앞/뒤 어디로 걷든 몸은 무조건 카메라가 바라보는 정면을 유지합니다.
                targetRotationDirection = player.playerCamera.cameraObject.transform.forward;
            }
            else
            {
                // 기존 모드: S키를 누르면 몸을 180도 돌려서 카메라를 바라봅니다.
                targetRotationDirection = player.playerCamera.cameraObject.transform.forward * verticalMovement;
                targetRotationDirection += player.playerCamera.cameraObject.transform.right * horizontalMovement;
            }

            targetRotationDirection.y = 0;
            targetRotationDirection.Normalize();

            if (targetRotationDirection == Vector3.zero) targetRotationDirection = transform.forward;

            // [동적 지연 회전 (Dynamic Delayed Turn)]
            float angleDiff = Vector3.Angle(transform.forward, targetRotationDirection);
            float currentRotationSpeed = rotationSpeed;

            // 고개를 45도 이상 돌리고 이동하려 할 때 회전 속도를 30%로 둔탁하게 깎아버립니다.
            if (angleDiff > 45f && moveAmount > 0.1f)
            {
                currentRotationSpeed = rotationSpeed * 0.3f;
            }

            Quaternion newRotation = Quaternion.LookRotation(targetRotationDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, newRotation, currentRotationSpeed * Time.deltaTime);
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

        // 전투 상태(락온)에서는 적을 향해 민첩하게 조준해야 하므로 회전 속도를 증폭
        float combatRotationSpeed = rotationSpeed * 1.5f;

        Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, combatRotationSpeed * Time.deltaTime);
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

            // 구르기 애니메이션 실행 (기존 레거시 유지 또는 향후 Funnel 교체 가능)
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
        // 다른 클라이언트들에게도 시각적으로 보정된(기울어진) 값을 넘겨줍니다.
        player.playerAnimationManager.UpdateAnimatorMovementParameters(animHorizontalMovement, animVerticalMovement, isSprinting);
    }
}