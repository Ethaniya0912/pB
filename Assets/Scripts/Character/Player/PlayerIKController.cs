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
            // 🚨 [가장 중요한 순서 변경] 턴 종료 후 상태 초기화 (최우선 실행)
            // 아래의 Action Guard 때문에 안전망 코드가 무시당하는 치명적 렉(레이스 컨디션)을 해결하기 위해
            // 리셋 방어막을 함수 최상단으로 끌어올렸습니다!
            // =========================================================================================
            if (!isPlayingTurnAnim && !isTransitioningToTurn)
            {
                if (isTurningDebug)
                {
                    isTurningDebug = false;
                    currentTurnDirection = 0f;
                }

                // 턴 애니메이션이 끝났고, 아직 isPerformingAction이 true라면 묶인 발(Root Motion)을 즉시 풀어줍니다.
                if (player.isPerformingAction)
                {
                    AnimatorStateInfo actionState = player.animator.GetCurrentAnimatorStateInfo(1);
                    // 상단 레이어(Action)가 빈 상태(Empty)라면, 무기 공격 중이 아니라 '턴 때문에 걸린 락'이 확실하므로 해제!
                    if (actionState.IsName("Empty State") || actionState.IsName("Empty"))
                    {
                        player.isPerformingAction = false;
                        if (player.animator != null) player.animator.applyRootMotion = false;
                    }
                }

                targetLookWeight = 1.0f; // 평상시 IK 복구
            }

            // =========================================================================================
            // 2. [액션 가드] 공격 등 진짜 액션 중일 때는 파라미터를 0으로 세탁하고 빠져나갑니다.
            // 위에서 '가짜 액션 락(턴으로 인한 락)'이 해제되었으므로, 이제 평상시에는 이 가드에 갇히지 않고 무사통과합니다!
            // =========================================================================================
            if (player.isPerformingAction && !isPlayingTurnAnim && !isTransitioningToTurn)
            {
                targetLookWeight = 0f;
                currentTurnAngle = 0f;

                if (player.animator != null)
                {
                    player.animator.SetFloat("turnAngle", currentTurnAngle);
                }
                return; // 가드에 걸리면 즉시 종료
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

            // =========================================================================================
            // 6. 턴 파라미터 상시 업데이트
            // (위의 가드들을 무사히 통과했다면 실시간으로 각도를 꽂아 넣어줍니다!)
            // =========================================================================================
            currentTurnAngle = signedAngle;

            if (player.animator != null)
            {
                player.animator.SetFloat("turnAngle", currentTurnAngle);
            }

            // 7. 턴 애니메이션 발동
            if (absAngle > turnStepAngle && !isPlayingTurnAnim && !isTransitioningToTurn && spawnStabilizeTimer <= 0f)
            {
                isTurningDebug = true;
                currentTurnDirection = Mathf.Sign(signedAngle);
                targetLookWeight = 0f;

                if (player.animator != null)
                {
                    player.animator.CrossFade("Empty", 0.1f, 1);
                }

                string targetAnimState = "";

                if (absAngle >= turnPivotAngle)
                {
                    targetAnimState = signedAngle > 0 ? turnRight180StateName : turnLeft180StateName;
                }
                else
                {
                    targetAnimState = signedAngle > 0 ? turnRight90StateName : turnLeft90StateName;
                }

                player.playerAnimationManager.PlayTargetAction(targetAnimState);

                player.isPerformingAction = true;
                if (player.animator != null)
                {
                    player.animator.applyRootMotion = true;
                }

                Debug.Log($"<color=cyan>[PlayerIKController]</color> 턴 애니메이션 발동! (각도: {absAngle:F1}도)");
            }
            else if ((isPlayingTurnAnim || isTransitioningToTurn) && absAngle < (turnStepAngle - turnHysteresisOffset))
            {
                isTurningDebug = false;
                currentTurnDirection = 0f;
            }
        }
    }
}