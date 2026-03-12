using UnityEngine;
using TDA.Core.Events;

namespace TDA.Character
{
    /// <summary>
    /// [L3 - Base Locomotion Domain] 모든 캐릭터(플레이어, 몬스터, NPC)가 공유하는 물리적 이동 및 중력 엔진입니다.
    /// 플레이어 입력이나 카메라 관련 로직은 절대 포함되지 않으며, 
    /// 오직 100% 루트 모션(Root Motion)을 최우선으로 신뢰하고 스케이팅 방지 로직만을 집행합니다.
    /// </summary>
    public class CharacterLocomotionManager : MonoBehaviour
    {
        protected CharacterManager character;

        // [에러 수정] Instance 싱글턴이 아닌 로컬 컴포넌트로 캐싱하여 이벤트망을 구축합니다.
        protected CharacterEventManager characterEventManager;

        [Header("Locomotion Input (Synced)")]
        [HideInInspector] public float verticalMovement;
        [HideInInspector] public float horizontalMovement;
        [HideInInspector] public float moveAmount;

        [Header("Locomotion Mode Settings")]
        [Tooltip("드림코어 특유의 사실성을 위해 걷기/뛰기에도 루트 모션을 적용할지 여부입니다. (true 권장)")]
        [SerializeField] protected bool useRootMotionForLocomotion = true;

        [Header("Movement Settings (Airborne & Fallback)")]
        [Tooltip("루트 모션을 사용할 수 없는 상태(공중 등)에서 적용되는 스크립트 기반 보조 속도입니다.")]
        protected Vector3 moveDirection;
        [SerializeField] protected float walkingSpeed = 2.2f;
        [SerializeField] protected float runningSpeed = 5.5f;
        [SerializeField] protected float rotationSpeed = 15f;

        // [에러 수정] 하위 플레이어 클래스에서 사용하는 스태미나 소모량을 부모 클래스로 복구했습니다.
        [SerializeField] protected int dodgeStaminaCost = 10;

        [Header("Gravity & Airborne System")]
        [SerializeField] protected float gravity = -20f;
        [SerializeField] protected float groundedGravity = -5f;
        [SerializeField] protected float jumpForwardSpeed = 4f;
        [SerializeField] protected float inAirControl = 2f;

        protected Vector3 yVelocity;       // 수직 중력 가속도
        protected Vector3 inAirDirection;  // 공중 수평 관성

        [HideInInspector] public bool isRolling;

        // [에러 수정] 이벤트에 의한 강제 회전 잠금 플래그를 복구했습니다.
        [HideInInspector] public bool isRotationLockedByEvent;

        protected virtual void Awake()
        {
            character = GetComponent<CharacterManager>();
            characterEventManager = GetComponentInChildren<CharacterEventManager>();
        }

        #region [L4 통신망 연결] 애니메이션 타임라인 기반 이동/회전 잠금
        protected virtual void OnEnable()
        {
            if (characterEventManager != null)
            {
                characterEventManager.OnAnimationEventTriggered += HandleAnimationEvents;
            }
        }

        protected virtual void OnDisable()
        {
            if (characterEventManager != null)
            {
                characterEventManager.OnAnimationEventTriggered -= HandleAnimationEvents;
            }
        }

        /// <summary>
        /// 애니메이션 타임라인에서 송출된 이벤트를 수신하여 스케이팅 버그를 원천 차단합니다.
        /// </summary>
        private void HandleAnimationEvents(global::AnimationEventType eventType)
        {
            if (character == null) return;

            switch (eventType)
            {
                case global::AnimationEventType.Lock_Movement:
                    character.canMove = false;
                    break;
                case global::AnimationEventType.Unlock_Movement:
                    character.canMove = true;
                    break;
                case global::AnimationEventType.Lock_Rotation:
                    character.canRotate = false;
                    isRotationLockedByEvent = true;
                    break;
                case global::AnimationEventType.Unlock_Rotation:
                    character.canRotate = true;
                    isRotationLockedByEvent = false;
                    break;
            }
        }
        #endregion

        protected virtual void Update()
        {
            // Owner(주체)일 경우에만 매 프레임 루트 모션 통제권을 갱신합니다.
            if (character.IsOwner)
            {
                character.animator.applyRootMotion = DetermineApplyRootMotion();
            }
        }

        protected virtual bool DetermineApplyRootMotion()
        {
            // 1. 공격, 피격, 구르기 등 액션 수행 중에는 물리적 개연성을 위해 100% 루트모션 강제
            if (character.isPerformingAction) return true;

            // 2. 이동 중일 때 기획 설정값 따름
            if (moveAmount > 0) return useRootMotionForLocomotion;

            // 3. 완전히 멈춰있을 때 미세한 노이즈로 옆으로 미끄러지는 것을 방지
            return false;
        }

        // =========================================================================================
        // [범용 물리 연산 코어]
        // =========================================================================================

        protected void HandleAirborneAndGravity(Vector3 targetAirDir)
        {
            bool isGrounded = character.characterController.isGrounded;

            if (isGrounded)
            {
                if (yVelocity.y < 0) yVelocity.y = groundedGravity;
                inAirDirection = Vector3.zero;
            }
            else
            {
                // 공중에서는 루트 모션이 작동할 수 없으므로 스크립트로 관성을 제어합니다.
                if (inAirDirection == Vector3.zero && moveAmount > 0)
                {
                    float speed = moveAmount > 0.5f ? runningSpeed : walkingSpeed;
                    inAirDirection = moveDirection * speed;
                }

                inAirDirection = Vector3.Lerp(inAirDirection, targetAirDir * jumpForwardSpeed, Time.deltaTime * inAirControl);
                character.characterController.Move(inAirDirection * Time.deltaTime);
            }

            yVelocity.y += gravity * Time.deltaTime;
            character.characterController.Move(yVelocity * Time.deltaTime);

            // GC 스파이크 방지: AnimatorParameterHash 참조
            if (character.animator != null)
            {
                character.animator.SetBool(AnimatorParameterHash.isGrounded, isGrounded);
                character.animator.SetFloat(AnimatorParameterHash.verticalVelocity, yVelocity.y);
            }
        }

        protected void HandleGroundedMovement()
        {
            if (!character.canMove) return;
            if (!character.characterController.isGrounded) return;

            // [핵심 아키텍처: Root Motion Guard]
            // 루트 모션이 켜져 있다면, 스크립트 이동(Move) 간섭을 차단하여 속도 중첩을 방지합니다!
            // 이 가드가 없으면 캐릭터가 스케이팅을 타게 됩니다.
            if (character.animator.applyRootMotion) return;

            // Fallback: 루트 모션이 꺼져있을 때만 작동
            float currentSpeed = moveAmount > 0.5f ? runningSpeed : walkingSpeed;
            character.characterController.Move(moveDirection * currentSpeed * Time.deltaTime);
        }

        protected void HandleRotation(Vector3 targetRotDir, bool isLockedOn)
        {
            // [에러 수정] character.isDead가 아니라 네트워크 매니저를 통한 플래그 검사로 변경합니다.
            if (character.characterNetworkManager != null && character.characterNetworkManager.isDead.Value) return;
            if (!character.canRotate) return;

            // 회전 역시 루트 모션이 켜져있다면 애니메이션의 물리적 회전을 100% 신뢰합니다.
            if (character.animator.applyRootMotion) return;

            // [에러 수정] 달리기 상태 역시 네트워크 매니저에서 가져옵니다.
            bool isSprinting = character.characterNetworkManager != null && character.characterNetworkManager.isSprinting.Value;

            if (isLockedOn && !isSprinting && !isRolling)
            {
                // 락온 타겟이 존재하면 강제로 타겟을 바라보게 합니다.
                if (character.characterCombatManager != null && character.characterCombatManager.currentTarget != null)
                {
                    Vector3 dir = character.characterCombatManager.currentTarget.transform.position - transform.position;
                    dir.y = 0;
                    dir.Normalize();

                    if (dir != Vector3.zero)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(dir);
                        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
                    }
                }
            }
            else
            {
                ExecuteStandardRotation(targetRotDir);
            }
        }

        private void ExecuteStandardRotation(Vector3 targetRotDir)
        {
            if (moveAmount > 0)
            {
                targetRotDir.y = 0;
                if (targetRotDir == Vector3.zero) targetRotDir = transform.forward;

                Quaternion newRotation = Quaternion.LookRotation(targetRotDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, newRotation, rotationSpeed * Time.deltaTime);
            }
        }
    }
}