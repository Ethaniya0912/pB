using UnityEngine;
using TDA.Character;

namespace TDA.Character.Player
{
    /// <summary>
    /// [L3 Domain] 플레이어 전용 IK 및 시선 제어기입니다.
    /// 1프레임 레이스 컨디션 캔슬 버그 및 Layer 타겟팅 미스 버그가 완벽히 수정된 버전입니다.
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

        [Header("Logical State Names (SO Funnel 연동)")]
        [Tooltip("CharacterAnimationSetSO에 등록된 Turn 애니메이션 이름과 정확히 일치해야 합니다.")]
        [SerializeField] private string turnLeft90StateName = "Turn_Left_90";
        [SerializeField] private string turnRight90StateName = "Turn_Right_90";
        [SerializeField] private string turnLeft180StateName = "Turn_Left_180";
        [SerializeField] private string turnRight180StateName = "Turn_Right_180";

        [Tooltip("턴 취소 시 강제로 복귀할 기본 로코모션 노드의 이름입니다.")]
        [SerializeField] private string locomotionStateName = "Locomotion 1H";

        [Header("Debug & Monitor")]
        [Tooltip("현재 IK 컨트롤러가 턴 애니메이션을 관장하고 있는지 여부를 나타냅니다.")]
        [SerializeField] private bool isTurningDebug = false;

        private float currentTurnDirection = 0f;

        [Tooltip("현재 애니메이터로 전송 중인 최종 턴 각도입니다.")]
        [SerializeField] private float currentTurnAngle;

        [Tooltip("물리적으로 계산된 순수 시선 각도 차이 (몸통 기준)")]
        [SerializeField] private float rawAngleDifference;

        private float spawnStabilizeTimer = 0.5f;

        // 🚨 [버그 수정] 턴 트리거 직후 애니메이터가 반응하기 전까지의 1프레임 캔슬을 막는 타이머
        private float turnTriggerTimer = 0f;

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
                if (spawnStabilizeTimer > 0f)
                {
                    spawnStabilizeTimer -= Time.deltaTime;
                }

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
            // 타이머 갱신 (캔슬 방어막)
            if (turnTriggerTimer > 0f)
            {
                turnTriggerTimer -= Time.deltaTime;
            }

            // 1. 무조건 실제 각도부터 먼저 계산합니다.
            Vector3 directionToTarget = currentLookAtPosition - transform.position;
            directionToTarget.y = 0f;

            Vector3 forward = transform.forward;
            forward.y = 0f;

            float signedAngle = Vector3.SignedAngle(forward, directionToTarget, Vector3.up);
            float absAngle = Mathf.Abs(signedAngle);

            rawAngleDifference = signedAngle;

            // 애니메이터 진짜 상태 읽기
            bool isPlayingTurnAnim = false;
            bool isTransitioningToTurn = false;

            if (player.animator != null)
            {
                // 스크린샷 확인 결과 턴 애니메이션은 Base Layer (Index 0)에 있습니다.
                AnimatorStateInfo state = player.animator.GetCurrentAnimatorStateInfo(0);
                AnimatorStateInfo nextState = player.animator.GetNextAnimatorStateInfo(0);

                if (state.IsName(turnLeft90StateName) || state.IsName(turnRight90StateName) ||
                    state.IsName(turnLeft180StateName) || state.IsName(turnRight180StateName))
                {
                    isPlayingTurnAnim = true;
                }

                if (nextState.IsName(turnLeft90StateName) || nextState.IsName(turnRight90StateName) ||
                    nextState.IsName(turnLeft180StateName) || nextState.IsName(turnRight180StateName))
                {
                    isTransitioningToTurn = true;
                }
            }

            if (isPlayingTurnAnim || isTransitioningToTurn)
            {
                isTurningDebug = true;
            }

            // =========================================================================================
            // 🚨 [버그 수정 1] 턴 종료 후 상태 초기화 (Race Condition 완벽 차단)
            // 턴을 트리거한 직후(turnTriggerTimer > 0)에는 애니메이터가 아직 상태를 바꾸지 못했더라도 
            // 억지로 강제 초기화(Root Motion 압수)를 하는 것을 철벽 방어합니다.
            // =========================================================================================
            if (turnTriggerTimer <= 0f && !isPlayingTurnAnim && !isTransitioningToTurn)
            {
                if (isTurningDebug)
                {
                    isTurningDebug = false;
                    currentTurnDirection = 0f;
                }

                if (player.isPerformingAction)
                {
                    AnimatorStateInfo actionState = player.animator.GetCurrentAnimatorStateInfo(1);
                    if (actionState.IsName("Empty State") || actionState.IsName("Empty"))
                    {
                        player.isPerformingAction = false;
                        if (player.animator != null) player.animator.applyRootMotion = false;
                    }
                }

                targetLookWeight = 1.0f; // 평상시 IK 복구
            }

            // =========================================================================================
            // 2. 액션 가드
            // =========================================================================================
            if (player.isPerformingAction && !isPlayingTurnAnim && !isTransitioningToTurn && turnTriggerTimer <= 0f)
            {
                targetLookWeight = 0f;
                currentTurnAngle = 0f;

                if (player.animator != null)
                {
                    player.animator.SetFloat("turnAngle", currentTurnAngle);
                }
                return;
            }

            // 3. 방어 중 예외 처리 (제자리 비비기 연출)
            if (player.playerDefenseManager != null && player.playerDefenseManager.isDefending)
            {
                isTurningDebug = false;
                currentTurnDirection = 0f;
                currentTurnAngle = 0f;

                if (player.animator != null)
                {
                    player.animator.SetFloat("turnAngle", 0f);
                }

                if (absAngle > 10f && directionToTarget != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(directionToTarget.normalized);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 3f * Time.deltaTime);
                }

                return;
            }

            // 4. 이동 및 캔슬 로직 (Cancel Guard)
            if (isPlayingTurnAnim || isTransitioningToTurn)
            {
                bool shouldCancel = false;

                if (player.playerNetworkManager != null && player.playerNetworkManager.animatorMoveAmountMovement.Value > 0.1f)
                {
                    shouldCancel = true;
                }
                else if (currentTurnDirection != 0f && Mathf.Sign(signedAngle) != currentTurnDirection)
                {
                    if (absAngle > turnStepAngle)
                    {
                        shouldCancel = true;
                    }
                }

                if (shouldCancel)
                {
                    isTurningDebug = false;
                    currentTurnDirection = 0f;
                    targetLookWeight = 1.0f;

                    player.isPerformingAction = false;
                    if (player.animator != null)
                    {
                        player.animator.applyRootMotion = false;
                        player.animator.SetFloat("turnAngle", 0f);
                        player.animator.CrossFade(locomotionStateName, 0.1f);
                    }
                    return;
                }
            }

            // 5. 이동 중 제자리 턴 발생 방어막 (Movement Guard)
            if (player.playerNetworkManager != null && player.playerNetworkManager.animatorMoveAmountMovement.Value > 0.1f)
            {
                isTurningDebug = false;
                if (player.animator != null)
                {
                    player.animator.SetFloat("turnAngle", 0f);
                }
                return;
            }

            // 6. 턴 파라미터 상시 업데이트
            currentTurnAngle = signedAngle;

            if (player.animator != null)
            {
                player.animator.SetFloat("turnAngle", currentTurnAngle);
            }

            // =========================================================================================
            // 7. 🚨 [버그 수정 2] 턴 애니메이션 명시적 타겟팅 발동
            // =========================================================================================
            // [신규] isPerformingAction 추가 차단
            // 2번 액션 가드는 turnTriggerTimer > 0 이면 통과할 수 있고,
            // 4번 Cancel 로직이 isPerformingAction = false 로 잘못 초기화하는 경우도 있어
            // 공격 중 턴 발동이 뚫릴 수 있습니다.
            // 7번 발동 시점에도 명시적으로 isPerformingAction 을 차단합니다.
            if (absAngle > turnStepAngle && !isPlayingTurnAnim && !isTransitioningToTurn && spawnStabilizeTimer <= 0f && turnTriggerTimer <= 0f && !player.isPerformingAction)
            {
                isTurningDebug = true;
                currentTurnDirection = Mathf.Sign(signedAngle);
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

                // Action Layer(1)를 타겟팅하는 PlayTargetAction 대신, 
                // 스크린샷과 일치하는 Base Layer(0)에 직접 턴 애니메이션을 명시적으로 꽂아 넣습니다!
                if (player.animator != null)
                {
                    player.animator.CrossFade("Empty", 0.1f, 1); // 혹시 모를 상체 액션 레이어 초기화
                    player.animator.CrossFade(targetAnimState, 0.1f, 0); // 🚨 Layer 0 (Base Layer) 강제 타겟팅 재생
                }

                player.isPerformingAction = true;
                if (player.animator != null)
                {
                    player.animator.applyRootMotion = true;
                }

                // 🚨 0.2초 동안 위의 Cleanup 로직이 절대로 Root Motion을 압수하지 못하게 쉴드를 칩니다.
                turnTriggerTimer = 0.2f;

                Debug.Log($"<color=cyan>[PlayerIKController]</color> 턴 애니메이션 발동! (각도: {absAngle:F1}도) - Race Condition Shield 켜짐");
            }
            else if ((isPlayingTurnAnim || isTransitioningToTurn) && absAngle < (turnStepAngle - turnHysteresisOffset))
            {
                isTurningDebug = false;
                currentTurnDirection = 0f;
            }
        }
    }
}