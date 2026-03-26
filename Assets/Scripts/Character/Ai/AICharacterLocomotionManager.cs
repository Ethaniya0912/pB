// =============================================================================
// AICharacterLocomotionManager.cs  |  TDA Project
// Layer  : L3 Domain — AI 이동 물리 엔진
//
// 역할:
//   NavMeshAgent (경로 계산 전담) 와 Animator RootMotion (실제 이동 전담) 을
//   통합하여 AI 캐릭터의 물리적 이동을 처리합니다.
//
// 아키텍처 규약:
//   ① CharacterController.Move() 는 OnAnimatorMove() 에서만 호출합니다.
//      NavMeshAgent.updatePosition = false 로 고정 — 경로만 계산합니다.
//   ② NGO 서버 권위형 : Update / OnAnimatorMove 모두 IsServer 게이트 확인.
//   ③ SyncAnimatorParameters() : navMeshAgent.velocity → Animator 파라미터 동기화
//      (Animator 직접 조작 금지 — SetFloat/SetBool 은 이 메서드에서만 허용).
//
// 연동:
//   AICharacterManager.Awake() 에서 GetComponent<AICharacterLocomotionManager>() 로
//   자동 참조됩니다.
// =============================================================================
using TDA.Core.Events;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace TDA.Character.AI
{
    /// <summary>
    /// [L3 Domain] AI 전용 이동 엔진입니다.
    /// NavMeshAgent 경로 계산과 Animator RootMotion 실제 이동을 단일 책임으로 통합합니다.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class AICharacterLocomotionManager : CharacterLocomotionManager
    {
        // =====================================================================
        // 내부 참조
        // =====================================================================
        private AICharacterManager aiCharacter;
        private NavMeshAgent navMeshAgent;

        // =====================================================================
        // 이동 설정
        // =====================================================================
        [Header("AI Locomotion Settings")]
        [Tooltip("NavMeshAgent 의 이동 속도가 이 값 이하일 때 Idle 로 판정합니다.")]
        [SerializeField] private float idleVelocityThreshold = 0.1f;

        [Tooltip("Animator moveAmount 파라미터 보간 속도 (낮을수록 부드럽습니다).")]
        [SerializeField] private float animatorSmoothTime = 0.15f;

        // =====================================================================
        // Unity 생명주기
        // =====================================================================
        protected override void Awake()
        {
            base.Awake();

            aiCharacter = GetComponent<AICharacterManager>();
            navMeshAgent = GetComponent<NavMeshAgent>();

            // ──────────────────────────────────────────────────────────────────
            // [핵심 규약] NavMeshAgent 는 경로 계산만 담당합니다.
            // 실제 위치 이동은 OnAnimatorMove() → CharacterController.Move() 가 담당합니다.
            // ──────────────────────────────────────────────────────────────────
            if (navMeshAgent != null)
            {
                navMeshAgent.updatePosition = false; // RootMotion이 위치를 갱신
                navMeshAgent.updateRotation = false; // 회전은 FSM State 또는 RootMotion이 처리
            }
        }

        // =====================================================================
        // Update — Animator 파라미터 동기화 (서버 전용)
        // =====================================================================
        protected override void Update()
        {
            // [NGO 규약] 서버에서만 Animator 파라미터를 구동합니다.
            if (!aiCharacter.IsServer) return;

            base.Update();

            SyncAnimatorParameters();
        }

        // =====================================================================
        // OnAnimatorMove — RootMotion 단일 이동 처리 (서버 전용)
        // =====================================================================
        private void OnAnimatorMove()
        {
            // [NGO 규약] 서버에서만 물리 이동을 집행합니다.
            if (!aiCharacter.IsServer) return;
            if (aiCharacter.characterController == null) return;
            if (!aiCharacter.characterController.enabled) return;

            // [이동 허가 검문]
            if (!aiCharacter.canMove) return;

            // [RootMotion 우선 처리]
            if (aiCharacter.animator.applyRootMotion)
            {
                // RootMotion 이 켜져 있으면 Animator 델타를 그대로 적용합니다.
                // NavMeshAgent 의 nextPosition 도 함께 갱신하여 에이전트와 싱크를 맞춥니다.
                Vector3 rootMotionDelta = aiCharacter.animator.deltaPosition;
                aiCharacter.characterController.Move(rootMotionDelta);

                if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
                    navMeshAgent.nextPosition = aiCharacter.transform.position;

                return;
            }

            // [NavMesh 경로 기반 이동 폴백]
            // RootMotion 이 꺼져 있을 때만 NavMeshAgent 속도 벡터로 이동합니다.
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                Vector3 velocity = navMeshAgent.desiredVelocity;
                velocity.y = 0f;

                // 중력 적용
                HandleAirborneAndGravity(velocity.normalized);

                // 수평 이동
                aiCharacter.characterController.Move(velocity * Time.deltaTime);

                // 에이전트 위치 동기화
                navMeshAgent.nextPosition = aiCharacter.transform.position;
            }
        }

        // =====================================================================
        // SyncAnimatorParameters — Animator 파라미터 동기화
        // Animator 직접 조작이 허용되는 유일한 메서드입니다.
        // =====================================================================
        private void SyncAnimatorParameters()
        {
            if (aiCharacter.animator == null) return;
            if (navMeshAgent == null || !navMeshAgent.isActiveAndEnabled) return;

            // NavMeshAgent 속도 크기 → moveAmount (0~1)
            float speed = navMeshAgent.velocity.magnitude;
            float moveAmount = Mathf.Clamp01(speed / Mathf.Max(navMeshAgent.speed, 0.01f));

            // 부드러운 보간으로 애니메이터 파라미터 갱신
            float current = aiCharacter.animator.GetFloat(AnimatorParameterHash.moveAmount);
            float smoothed = Mathf.Lerp(current, moveAmount, Time.deltaTime / animatorSmoothTime);

            aiCharacter.animator.SetFloat(AnimatorParameterHash.moveAmount, smoothed);
        }
    }
}