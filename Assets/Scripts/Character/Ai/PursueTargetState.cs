using UnityEngine;
using TDA.Character.AI;

/// <summary>
/// [L3 Domain — PursueTargetState]
/// 책임: 타겟 추적 판단, NavMeshAgent 속도 결정.
/// Animator 파라미터 직접 제어 금지 — AICharacterLocomotionManager에 위임.
/// </summary>
[CreateAssetMenu(menuName = "AI/States/Pursue Target")]
public class PursueTargetState : AIState
{
    [Header("Next States")]
    public CombatStanceState combatStanceState;
    public PatrolState patrolState;

    [Header("Pursue Settings")]
    public float attackRange = 2.5f;
    public float pursueRotationSpeed = 8f;
    public float pursueStaminaCostPerSec = 5f;

    [Header("Locomotion Speeds")]
    [Tooltip("달리기 NavMeshAgent 속도. Blend Tree Run 임계값과 맞추세요.")]
    public float pursueRunSpeed = 5.5f;
    [Tooltip("탈진 또는 급커브 시 NavMeshAgent 속도.")]
    public float pursueWalkSpeed = 2.5f;

    private float stuckTimer = 0f;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        if (aiCharacter.aiCharacterCombatManager.currentTarget == null ||
            aiCharacter.aiCharacterCombatManager.currentTarget.characterNetworkManager.isDead.Value)
            return SwitchState(aiCharacter, patrolState);

        // 스태미나 소모
        bool isExhausted = aiCharacter.characterNetworkManager.currentStamina.Value <= 0;
        if (!isExhausted)
            aiCharacter.characterNetworkManager.currentStamina.Value -= pursueStaminaCostPerSec * Time.deltaTime;

        if (aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
        {
            aiCharacter.navMeshAgent.SetDestination(aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

            // 경로 막힘 감지
            bool pathBlocked =
                aiCharacter.navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathPartial ||
                aiCharacter.navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid;

            if (pathBlocked)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer > 2.0f)
                {
                    stuckTimer = 0f;
                    return SwitchState(aiCharacter, patrolState);
                }
            }
            else
            {
                stuckTimer = 0f;
            }

            // SteeringTarget(다음 코너) 기준 회전 처리
            // [주의] 이 회전은 NavMeshAgent 코너 추적용입니다.
            // AICharacterLocomotionManager.HandleRotation()은 desiredVelocity 기준이고,
            // 여기서는 Pursue 전용 pursueRotationSpeed를 적용하므로 별도 처리합니다.
            Vector3 steerDir = aiCharacter.navMeshAgent.steeringTarget - aiCharacter.transform.position;
            steerDir.y = 0f;
            steerDir.Normalize();

            if (steerDir != Vector3.zero)
            {
                aiCharacter.transform.rotation = Quaternion.Slerp(
                    aiCharacter.transform.rotation,
                    Quaternion.LookRotation(steerDir),
                    pursueRotationSpeed * Time.deltaTime);
            }

            float angleToTarget = Vector3.Angle(aiCharacter.transform.forward, steerDir);

            // [변경] Animator 직접 조작 제거.
            // navMeshAgent.speed를 설정하면 LocomotionManager.SyncAnimatorParameters()가
            // desiredVelocity/speed 비율로 Vertical 파라미터를 자동 계산합니다.
            bool shouldWalk = isExhausted || angleToTarget > 45f;
            aiCharacter.navMeshAgent.speed = shouldWalk ? pursueWalkSpeed : pursueRunSpeed;
        }

        // 사거리 자동 동기화
        float currentAttackRange = attackRange;

        // =====================================================================
        // [CS1061 컴파일 에러 조치] AttackState.GetMaximumAttackRange() 메서드 누락 대비
        // 코드 단 한 줄도 삭제하지 않기 위해 원본 코드를 주석으로 안전하게 보존하고,
        // AICharacterCombatManager.maximumAttackRange를 참조하도록 우회 적용합니다.
        // =====================================================================
        /* if (combatStanceState?.attackState != null)
        {
            float maxRange = combatStanceState.attackState.GetMaximumAttackRange();
            if (maxRange > 0f) currentAttackRange = maxRange;
        }
        */

        if (aiCharacter.aiCharacterCombatManager != null)
        {
            float maxRange = aiCharacter.aiCharacterCombatManager.maximumAttackRange;
            if (maxRange > 0f) currentAttackRange = maxRange;
        }
        // =====================================================================

        float dist = Vector3.Distance(
            aiCharacter.transform.position,
            aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

        if (dist <= currentAttackRange)
            return SwitchState(aiCharacter, combatStanceState);

        return this;
    }

    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);
        stuckTimer = 0f;
        // [변경] Animator 직접 조작 제거.
    }
}