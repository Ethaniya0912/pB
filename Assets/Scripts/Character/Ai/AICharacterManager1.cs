using TDA.Character;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

// 기존 CharacterLocomotionManager를 상속받아 프로젝트의 아키텍처 일관성을 유지합니다.
public class AICharacterLocomotionManager : CharacterLocomotionManager
{
    private AICharacterManager aiCharacterManager;
    private NavMeshAgent navMeshAgent;

    [Header("AI Locomotion Settings")]
    public float rotationSpeed = 5f;

    protected override void Awake()
    {
        // 부모 클래스(CharacterLocomotionManager)의 Awake 호출을 보장
        base.Awake();

        aiCharacterManager = GetComponent<AICharacterManager>();
        navMeshAgent = GetComponentInChildren<NavMeshAgent>();

        // [단계 1] NavMeshAgent 제어권 분리
        // NavMeshAgent가 자체적으로 Transform을 이동/회전시키는 것을 차단합니다.
        if (navMeshAgent != null)
        {
            navMeshAgent.updatePosition = false;
            navMeshAgent.updateRotation = false;
        }
    }

    /// <summary>
    /// AI State (Patrol, Pursue 등)의 Update에서 매 프레임 호출되어야 하는 함수입니다.
    /// </summary>
    public void HandleAILocomotion()
    {
        if (aiCharacterManager == null || navMeshAgent == null) return;

        // [수정됨] 프로젝트에 맞게 사망 체크 변수로 수정하세요. (네트워크 변수일 경우 isDead.Value 일 수 있습니다)
        // if (aiCharacterManager.characterNetworkManager.isDead.Value) return; 
        // if (aiCharacterManager.characterStatsManager.isDead) return;

        HandleMove();
        HandleRotation();
    }

    private void HandleMove()
    {
        // 경로가 없거나 이동할 필요가 없을 때는 애니메이션 파라미터를 0으로 초기화
        if (!navMeshAgent.hasPath)
        {
            // [수정됨] 커스텀 함수 대신 Unity 내장 Animator 기능 직접 호출로 에러 방지
            if (aiCharacterManager.animator != null)
            {
                aiCharacterManager.animator.SetFloat("Horizontal", 0f, 0.1f, Time.deltaTime);
                aiCharacterManager.animator.SetFloat("Vertical", 0f, 0.1f, Time.deltaTime);
            }
            return;
        }

        // [단계 2] 목표 속도 추출 및 애니메이터 파라미터 변환
        // 1. 에이전트가 계산한 월드 기준 목표 속도 (Desired Velocity)
        Vector3 targetVelocity = navMeshAgent.desiredVelocity;

        // 2. 월드 속도를 캐릭터의 로컬 기준 속도(Local Velocity)로 변환
        Vector3 localVelocity = transform.InverseTransformDirection(targetVelocity);

        // 3. 현재 NavMeshAgent의 설정된 속도를 기준으로 파라미터 정규화 (0.0 ~ 1.0)
        float vertical = localVelocity.z / navMeshAgent.speed;
        float horizontal = localVelocity.x / navMeshAgent.speed;

        // 4. Animator Blend Tree에 값 전달
        // [수정됨] UpdateAnimatorValues 대신 Unity 내장 API 사용. (추후 AnimatorParameterHash가 있다면 그것으로 대체 권장)
        if (aiCharacterManager.animator != null)
        {
            aiCharacterManager.animator.SetFloat("Horizontal", horizontal, 0.1f, Time.deltaTime);
            aiCharacterManager.animator.SetFloat("Vertical", vertical, 0.1f, Time.deltaTime);
        }

        // 참고: 만약 CharacterAnimationManager에 전용 함수가 있다면 아래와 같이 사용하세요.
        // aiCharacterManager.characterAnimationManager.UpdateAnimatorMovementParameters(horizontal, vertical, false);
    }

    private void HandleRotation()
    {
        // [단계 3] 자연스러운 회전(Rotation) 처리
        // 목표 속도가 일정 수치 이상일 때만 방향을 회전시킵니다.
        if (navMeshAgent.desiredVelocity.sqrMagnitude > 0.1f)
        {
            Vector3 targetDirection = navMeshAgent.desiredVelocity;
            targetDirection.y = 0; // 수직(위아래) 회전 방지

            Quaternion targetRotation = Quaternion.LookRotation(targetDirection);

            // Slerp를 이용해 부드럽게 목표 방향으로 회전
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }
    }

    private void OnAnimatorMove()
    {
        // [단계 4] 루트 모션 물리 적용
        // 애니메이터의 루트 모션이 활성화(applyRootMotion == true)되어 있을 때 자동으로 호출됩니다.
        if (aiCharacterManager != null && aiCharacterManager.animator != null)
        {
            if (aiCharacterManager.animator.applyRootMotion)
            {
                // 애니메이션에 의해 계산된 이번 프레임의 이동량
                Vector3 velocity = aiCharacterManager.animator.deltaPosition;

                // (프로젝트 설정에 따라) CharacterController 컴포넌트를 사용해 충돌 처리를 유지하며 이동
                CharacterController characterController = GetComponent<CharacterController>();
                if (characterController != null)
                {
                    characterController.Move(velocity);
                }
                else
                {
                    // CharacterController가 없는 경우 직접 Transform 이동
                    transform.position += velocity;
                }

                // NavMeshAgent가 루트 모션으로 이동된 캐릭터의 실제 위치를 잃어버리지 않도록 동기화
                if (navMeshAgent != null)
                {
                    navMeshAgent.nextPosition = transform.position;
                }
            }
        }
    }
}