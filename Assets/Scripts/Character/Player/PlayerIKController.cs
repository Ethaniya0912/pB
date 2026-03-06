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

        [Header("Hybrid Turn Animation Thresholds")]
        [Tooltip("고개만으로 쳐다볼 수 있는 한계치입니다. 이 각도를 넘으면 발을 떼서 회전(Turn)합니다.")]
        [SerializeField] private float turnStepAngle = 30f;   // 90도 회전 발동 임계점
        [SerializeField] private float turnPivotAngle = 120f; // 180도 피벗 회전 발동 임계점

        // 턴 애니메이션 중복 실행 방지용 플래그
        private bool isTurning = false;

        // 성능 최적화를 위한 해시 캐싱 (GC 할당 제로)
        private readonly int turnLeft90Hash = Animator.StringToHash("Turn_Left_90");
        private readonly int turnRight90Hash = Animator.StringToHash("Turn_Right_90");
        private readonly int turnLeft180Hash = Animator.StringToHash("Turn_Left_180");
        private readonly int turnRight180Hash = Animator.StringToHash("Turn_Right_180");

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

            // [NGO 방어] 로컬 플레이어(IsOwner)의 화면에서만 이 물리적 시선 연산을 수행합니다.
            if (Application.isPlaying && player != null && player.IsOwner)
            {
                CalculateLookAtTarget();
                HandleHybridRotationLogic();
            }
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();

            // 플레이어는 특정 Transform(오브젝트)을 고정으로 바라보는게 아니라,
            // 카메라와 락온 상황에 따라 3D 좌표가 매 프레임 변하므로 headTarget의 위치를 직접 덮어씁니다.
            if (Application.isPlaying && player != null && player.IsOwner && targetLookWeight > 0.01f)
            {
                if (headTarget != null)
                {
                    headTarget.position = currentLookAtPosition;
                }
            }
        }

        /// <summary>
        /// 상황(락온 vs 자유시점)에 따라 캐릭터가 쳐다봐야 할 정확한 3D 월드 좌표를 산출합니다.
        /// </summary>
        private void CalculateLookAtTarget()
        {
            if (player.playerCamera == null || player.playerCamera.cameraObject == null) return;

            // 1. 락온(Lock-on) 상태: 적의 가슴이나 머리를 뚫어지게 쳐다봅니다.
            if (player.playerNetworkManager.isLockedOn.Value && player.playerCombatManager.currentTarget != null)
            {
                // 타겟의 중심점에 약간의 높이를 더해 자연스러운 눈맞춤을 유도
                currentLookAtPosition = player.playerCombatManager.currentTarget.transform.position + (Vector3.up * 1.2f);
            }
            // 2. 자유 시점(Free Look) 상태: 카메라가 향하는 방향을 주시
            else
            {
                Transform camTransform = player.playerCamera.cameraObject.transform;
                currentLookAtPosition = camTransform.position + (camTransform.forward * freeLookDistance);
            }
        }

        /// <summary>
        /// 델타 각도(Delta Angle)를 측정하여, 목뼈의 한계를 넘었을 때 
        /// 제자리 회전(Turn) 애니메이션을 발동시키고 IK 가중치를 끄는 두뇌 로직입니다.
        /// </summary>
        private void HandleHybridRotationLogic()
        {
            // 공격, 구르기, 피격 등 이미 다른 액션을 취하고 있다면 회전과 시선 추적을 강제 차단합니다.
            if (player.isPerformingAction)
            {
                targetLookWeight = 0f;
                isTurning = false;
                return;
            }

            // 캐릭터 정면(Forward)과 타겟 방향 사이의 평면(Y축 무시) 각도를 산출합니다.
            Vector3 directionToTarget = currentLookAtPosition - transform.position;
            directionToTarget.y = 0;

            // SignedAngle을 사용하여 좌/우 방향성(+/-)을 얻어냅니다.
            float signedAngle = Vector3.SignedAngle(transform.forward, directionToTarget, Vector3.up);
            float absAngle = Mathf.Abs(signedAngle);

            // [Phase 2 & 3] 목이 돌아가는 임계점(turnStepAngle, 30도)을 돌파했을 때
            if (absAngle > turnStepAngle && !isTurning)
            {
                isTurning = true; // 덜덜거림(Spamming) 방지 플래그 온

                // 💡 [치명적 버그 방어]
                // 턴 애니메이션 중에 목뼈(Rigging)가 타겟을 향해 다시 꺾이면 엑소시스트 버그가 생깁니다.
                // 부모 클래스의 targetLookWeight를 0으로 맞추면, 부모의 UpdateIKWeights가 자연스럽게(Lerp) IK를 꺼줍니다.
                targetLookWeight = 0f;

                // 각도의 크기에 따라 90도 스텝 턴을 할지, 180도 피벗 턴을 할지 분기합니다.
                if (absAngle >= turnPivotAngle)
                {
                    int animHash = signedAngle > 0 ? turnRight180Hash : turnLeft180Hash;
                    player.playerAnimationManager.PlayTargetAnimation(animHash, true, true, false, false);
                }
                else
                {
                    int animHash = signedAngle > 0 ? turnRight90Hash : turnLeft90Hash;
                    player.playerAnimationManager.PlayTargetAnimation(animHash, true, true, false, false);
                }
            }
            // [Phase 1] 히스테리시스(-5f) 적용: 각도가 다시 정면 근처(25도)로 들어올 때까지 턴 상태를 유지
            else if (absAngle < (turnStepAngle - 5f))
            {
                isTurning = false;
                targetLookWeight = 1.0f; // 턴이 끝났으므로 다시 IK 개입을 활성화합니다.
            }
        }
    }
}