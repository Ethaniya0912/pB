using UnityEngine;
using TDA.Character.AI;

[CreateAssetMenu(menuName = "AI/States/Retreat")]
public class RetreatState : AIState
{
    [Header("Next States")]
    public CombatStanceState combatStanceState;
    public PursueTargetState pursueTargetState;

    [Header("Retreat Settings")]
    public float safeDistance = 5f;
    public float recoveredStaminaThreshold = 50f;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        if (aiCharacter.aiCharacterCombatManager.currentTarget == null ||
            aiCharacter.aiCharacterCombatManager.currentTarget.characterNetworkManager.isDead.Value)
        {
            return SwitchState(aiCharacter, pursueTargetState); // 수정됨: SwitchState 사용
        }

        if (aiCharacter.characterNetworkManager.currentStamina.Value >= recoveredStaminaThreshold)
        {
            return SwitchState(aiCharacter, combatStanceState); // 수정됨: SwitchState 사용
        }

        aiCharacter.aiCharacterCombatManager.HandleDefensiveActions();
        if (aiCharacter.isPerformingAction)
        {
            aiCharacter.navMeshAgent.ResetPath();
            aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
            return this;
        }

        float distanceFromTarget = Vector3.Distance(aiCharacter.transform.position, aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

        Vector3 directionToTarget = aiCharacter.aiCharacterCombatManager.currentTarget.transform.position - aiCharacter.transform.position;
        directionToTarget.y = 0;
        directionToTarget.Normalize();
        if (directionToTarget == Vector3.zero) directionToTarget = aiCharacter.transform.forward;
        aiCharacter.transform.rotation = Quaternion.Slerp(aiCharacter.transform.rotation, Quaternion.LookRotation(directionToTarget), Time.deltaTime * 5f);

        if (distanceFromTarget < safeDistance)
        {
            Vector3 retreatDirection = -directionToTarget;
            Vector3 retreatPosition = aiCharacter.transform.position + (retreatDirection * 2f);
            aiCharacter.navMeshAgent.SetDestination(retreatPosition);
            aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, -0.5f, false);
        }
        else
        {
            aiCharacter.navMeshAgent.ResetPath();
            aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
        }

        return this;
    }

    // 수정됨: 뒷걸음질 애니메이션 값 초기화
    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);
        aiCharacterManager.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
    }
}