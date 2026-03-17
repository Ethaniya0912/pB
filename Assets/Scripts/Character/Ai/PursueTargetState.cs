using UnityEngine;
using TDA.Character.AI;

[CreateAssetMenu(menuName = "AI/States/Pursue Target")]
public class PursueTargetState : AIState
{
    [Header("Next States")]
    public CombatStanceState combatStanceState;
    public PatrolState patrolState; // 수정됨: Idle 대신 Patrol

    [Header("Pursue Settings")]
    public float attackRange = 2.5f;

    [Tooltip("추적 시 몸을 트는 회전 속도 (벽을 매끄럽게 타고 돕니다)")]
    public float pursueRotationSpeed = 8f;

    [Tooltip("뛰어서 추적할 때 초당 소모되는 스태미나량")]
    public float pursueStaminaCostPerSec = 5f;

    // [신규] 고립(Stuck) 감지용 타이머
    private float stuckTimer = 0f;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        if (aiCharacter.aiCharacterCombatManager.currentTarget == null ||
            aiCharacter.aiCharacterCombatManager.currentTarget.characterNetworkManager.isDead.Value)
        {
            return SwitchState(aiCharacter, patrolState); // 수정됨: SwitchState 사용
        }

        // =========================================================================================
        // 🚨 [버그 픽스 1] 1프레임 무한 루프(Flickering) 및 벽 돌진 현상 해결
        // 스태미나가 0일 때 CombatStance로 넘기면, 사거리가 안 닿아서 다시 Pursue로 튕겨내는 핑퐁이 발생합니다.
        // 무한 루프에 빠지면 NavMesh가 고장나서 벽으로 무지성 돌진하게 됩니다.
        // 해결: 스태미나가 없으면 상태를 넘기지 않고, '걷기(0.5f)'로 속도를 줄이며 스태미나를 채우게 합니다.
        // =========================================================================================
        bool isExhausted = aiCharacter.characterNetworkManager.currentStamina.Value <= 0;

        if (!isExhausted)
        {
            // 뛸 수 있을 때만 스태미나 소모
            aiCharacter.characterNetworkManager.currentStamina.Value -= pursueStaminaCostPerSec * Time.deltaTime;
        }

        // [근본 원인 해결] 에러 방어 로직 (Guard Clause)
        // AI가 스폰 직후 허공에 떠있거나 네비메시가 아직 로드되지 않았을 때 SetDestination을 호출하여 발생하는 에러를 원천 차단합니다.
        if (aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
        {
            aiCharacter.navMeshAgent.SetDestination(aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

            // =========================================================================================
            // 🧠 [지능 2] 벽 막힘 감지 및 우회/탐색 판단
            // 경로가 끊겼거나(벽 너머 밀폐 공간) 닿을 수 없을 때 런닝머신을 뛰지 않고 Patrol 상태로 돌아갑니다.
            // =========================================================================================
            if (aiCharacter.navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathPartial ||
                aiCharacter.navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer > 2.0f) // 2초 이상 막혀있으면 포기하고 주변 탐색
                {
                    aiCharacter.aiCharacterCombatManager.DebugLog("타겟으로 가는 길이 막혔습니다! Patrol(탐색) 상태로 전환합니다.");
                    stuckTimer = 0f;
                    return SwitchState(aiCharacter, patrolState);
                }
            }
            else
            {
                stuckTimer = 0f; // 정상 경로면 타이머 초기화
            }

            // =========================================================================================
            // 🧠 [지능 3] 회전(Steering) 처리 및 급커브 감속 (가장 중요!!!)
            // 단순히 앞만 보지 않고, NavMesh가 계산한 다음 코너(SteeringTarget)를 향해 몸을 능동적으로 틉니다.
            // =========================================================================================
            Vector3 targetDirection = aiCharacter.navMeshAgent.steeringTarget - aiCharacter.transform.position;
            targetDirection.y = 0;
            targetDirection.Normalize();

            if (targetDirection != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
                aiCharacter.transform.rotation = Quaternion.Slerp(aiCharacter.transform.rotation, targetRotation, pursueRotationSpeed * Time.deltaTime);
            }

            float angleToTarget = Vector3.Angle(aiCharacter.transform.forward, targetDirection);

            // 지치면 걷고, 급커브여도 걷고, 아니면 뜁니다.
            float moveSpeed = (isExhausted || angleToTarget > 45f) ? 0.5f : 1.0f;

            // 이동 파라미터 전달 (속도 동적 조절)
            aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, moveSpeed, false);
        }

        // =========================================================================================
        // 🚨 [버그 픽스 2] 사거리 자동 동기화
        // 인스펙터의 attackRange와 실제 공격 패턴의 MaximumDistance가 달라서 발생하는 핑퐁 버그 방지
        // =========================================================================================
        float currentAttackRange = attackRange;
        if (combatStanceState != null && combatStanceState.attackState != null)
        {
            float maxRange = combatStanceState.attackState.GetMaximumAttackRange();
            if (maxRange > 0f)
            {
                currentAttackRange = maxRange;
            }
        }

        float distanceFromTarget = Vector3.Distance(aiCharacter.transform.position, aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

        if (distanceFromTarget <= currentAttackRange)
        {
            return SwitchState(aiCharacter, combatStanceState); // 수정됨: SwitchState 사용
        }

        return this;
    }

    // 수정됨: 달리기 애니메이션 값을 정리하고 전환
    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);
        aiCharacterManager.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
        stuckTimer = 0f; // 상태 빠져나갈 때 타이머도 리셋
    }
}