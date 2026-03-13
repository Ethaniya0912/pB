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

        // [신규] 현재 회전 방향을 기억하여 역방향 조작 시 캔슬하기 위한 변수 (1 = 우회전, -1 = 좌회전)
        private float currentTurnDirection = 0f;

        [Tooltip("현재 애니메이터로 전송 중인 최종 턴 각도입니다.")]
        [SerializeField] private float currentTurnAngle;

        [Tooltip("물리적으로 계산된 순수 시선 각도 차이 (몸통 기준)")]
        [SerializeField] private float rawAngleDifference;

        // [버그 픽스] 스폰 직후 카메라 불안정으로 인한 유령 턴(Phantom Turn) 방지용 타이머
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
                // 스폰 직후 0.5초 동안 안정화 타이머를 차감합니다.
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
            // 1. 무조건 실제 각도부터 먼저 계산합니다. (파라미터 업데이트를 위해)
            Vector3 directionToTarget = currentLookAtPosition - transform.position;
            directionToTarget.y = 0f;

            Vector3 forward = transform.forward;
            forward.y = 0f;

            float signedAngle = Vector3.SignedAngle(forward, directionToTarget, Vector3.up);
            float absAngle = Mathf.Abs(signedAngle);

            rawAngleDifference = signedAngle;

            // 2. [액션 가드] 공격 등 진짜 액션 중일 때는 파라미터를 0으로 세탁하고 빠져나갑니다.
            if (player.isPerformingAction && !isTurningDebug)
            {
                targetLookWeight = 0f;
                currentTurnAngle = 0f;

                if (player.animator != null)
                {
                    player.animator.SetFloat("turnAngle", currentTurnAngle);
                }
                return;
            }

            // =========================================================================================
            // 🚨 [신규 추가: 방어 중 턴 애니메이션 재생 방지] 
            // 방어 자세(Guard_BlendTree)를 굳건히 유지해야 하므로, 턴 애니메이션으로 강제 전이되는 것을 막습니다.
            // =========================================================================================
            if (player.playerDefenseManager != null && player.playerDefenseManager.isDefending)
            {
                isTurningDebug = false;
                currentTurnDirection = 0f;
                currentTurnAngle = 0f;

                if (player.animator != null)
                {
                    // 턴 애니메이션이 트리거되지 않도록 애니메이터 파라미터도 강제로 0으로 잠급니다.
                    player.animator.SetFloat("turnAngle", 0f);
                }

                // 애니메이션(Turn_90) 대신, 방어 중에는 제자리에서도 카메라가 보는 방향으로 
                // 몸통 전체를 묵직하게 스크립트로 회전(Slerp) 시킵니다.
                // (카메라 데드존에 턱 걸렸을 때 마우스를 억지로 더 밀면 발을 비비며 돌아가는 연출)
                if (absAngle > 10f && directionToTarget != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(directionToTarget.normalized);
                    // 방어 중이므로 일반 턴보다 느리고 묵직한 속도(3f)로 몸을 돌립니다.
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 3f * Time.deltaTime);
                }

                return; // 아래에 있는 Turn 애니메이션 강제 CrossFade 로직을 완전히 무시하고 빠져나갑니다!
            }
            // =========================================================================================

            // 🚨 [기존 픽스] 이동 및 반대 방향 조작 시 턴 즉시 캔슬 (Immediate Cancel)
            if (isTurningDebug)
            {
                bool shouldCancel = false;

                // A. 턴 도중 W, A, S, D 이동 키를 누른 경우 (기다리지 않고 즉시 걷기)
                if (player.playerNetworkManager != null && player.playerNetworkManager.animatorMoveAmountMovement.Value > 0.1f)
                {
                    shouldCancel = true;
                }
                // B. 턴 도중 마우스를 반대편으로 휙 돌려버린 경우 (역방향 턴을 위해 캔슬)
                else if (currentTurnDirection != 0f && Mathf.Sign(signedAngle) != currentTurnDirection)
                {
                    // 중심을 넘어서 반대쪽 임계치까지 마우스를 돌렸을 때
                    if (absAngle > turnStepAngle)
                    {
                        shouldCancel = true;
                    }
                }

                // 취소 조건 달성 시 물리 락(Lock)을 강제로 해방합니다.
                if (shouldCancel)
                {
                    isTurningDebug = false;
                    currentTurnDirection = 0f;
                    targetLookWeight = 1.0f; // 시선 IK 즉시 복구

                    player.isPerformingAction = false;
                    if (player.animator != null)
                    {
                        player.animator.applyRootMotion = false; // 이동 스크립트에게 주도권 반환
                        player.animator.SetFloat("turnAngle", 0f);

                        // 현재 재생 중인 턴 애니메이션을 박살 내고 0.1초 만에 Locomotion으로 트랜지션
                        player.animator.CrossFade(locomotionStateName, 0.1f);
                    }
                    return; // 아래의 턴 로직 건너뜀
                }
            }

            // 🚨 [치명적 버그 픽스] 이동 중 제자리 턴 발생 방어막 (Movement Guard)
            // 유저가 W(전진)를 눌러 이동 중일 때는 몸통이 회전하더라도 제자리 턴을 강제로 막습니다.
            if (player.playerNetworkManager != null && player.playerNetworkManager.animatorMoveAmountMovement.Value > 0.1f)
            {
                isTurningDebug = false;
                if (player.animator != null)
                {
                    // 이동 중일 때는 애니메이터의 turnAngle 파라미터도 0으로 밀어버려 불필요한 블렌딩을 막습니다.
                    player.animator.SetFloat("turnAngle", 0f);
                }
                return; // 정지 상태 전용 턴 애니메이션 발동 로직을 건너뜁니다!
            }

            // 3. 턴 방어 및 무한 루프 캔슬 로직
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

            // 4. 턴 애니메이션 발동 (정지 상태일 때만 여기까지 도달함)
            // [버그 픽스] 스폰 직후 0.5초 동안은 카메라가 안정화되는 시기이므로 턴 애니메이션을 강제로 막습니다.
            if (absAngle > turnStepAngle && !isTurningDebug && spawnStabilizeTimer <= 0f)
            {
                isTurningDebug = true;
                currentTurnDirection = Mathf.Sign(signedAngle); // 내가 어느 쪽으로 도는지 기억
                targetLookWeight = 0f; // 엑소시스트 방어 트리거 (목 꺾임 방지)

                string targetAnimState = "";

                if (absAngle >= turnPivotAngle)
                {
                    targetAnimState = signedAngle > 0 ? turnRight180StateName : turnLeft180StateName;
                }
                else
                {
                    targetAnimState = signedAngle > 0 ? turnRight90StateName : turnLeft90StateName;
                }

                // SO 연동 구조: 상태 이름(String)만 넘겨 Funnel로 처리합니다!
                player.playerAnimationManager.PlayTargetAction(targetAnimState);

                Debug.Log($"<color=cyan>[PlayerIKController]</color> 턴 애니메이션 발동! (각도: {absAngle:F1}도)");
            }
            else if (isTurningDebug && absAngle < (turnStepAngle - turnHysteresisOffset))
            {
                // 몸통 회전이 끝나고 카메라와의 시야 오차가 안정권에 들어오면 턴을 자연스럽게 해제합니다.
                isTurningDebug = false;
                currentTurnDirection = 0f;
                targetLookWeight = 1.0f; // 다시 IK 시선 개입을 시작
            }
        }
    }
}