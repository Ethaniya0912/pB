using UnityEngine;

[CreateAssetMenu(menuName = "AI/States/Pursue Target")]
public class PursueTargetState : AIState
{
    [Header("Next States")]
    public CombatStanceState combatStanceState;
    public PatrolState patrolState; // 수정됨: Idle 대신 Patrol

    [Header("Pursue Settings")]
    public float attackRange = 2.5f;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        if (aiCharacter.aiCharacterCombatManager.currentTarget == null ||
            aiCharacter.aiCharacterCombatManager.currentTarget.characterNetworkManager.isDead.Value)
        {
            return SwitchState(aiCharacter, patrolState); // 수정됨: SwitchState 사용
        }

        aiCharacter.navMeshAgent.SetDestination(aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);
        aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, 1f, false);

        float distanceFromTarget = Vector3.Distance(aiCharacter.transform.position, aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

        if (distanceFromTarget <= attackRange)
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
    }
}