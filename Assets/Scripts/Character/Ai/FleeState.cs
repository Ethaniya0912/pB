using UnityEngine;

[CreateAssetMenu(menuName = "AI/States/Flee")]
public class FleeState : AIState
{
    [Header("Next State")]
    public PatrolState patrolState; // 수정됨: Idle 대신 Patrol

    [Header("Flee Settings")]
    public float safeDistance = 20f;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        if (aiCharacter.aiCharacterCombatManager.currentTarget == null ||
            aiCharacter.aiCharacterCombatManager.currentTarget.characterNetworkManager.isDead.Value)
        {
            return SwitchState(aiCharacter, patrolState); // 수정됨: SwitchState 사용
        }

        float distanceFromTarget = Vector3.Distance(aiCharacter.transform.position, aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

        if (distanceFromTarget > safeDistance)
        {
            return SwitchState(aiCharacter, patrolState); // 수정됨: SwitchState 사용
        }

        Vector3 fleePosition = Vector3.zero;
        AICharacterCombatManager combatInfo = aiCharacter.aiCharacterCombatManager;

        if (combatInfo.aiIntelligenceLevel >= combatInfo.intelligenceToCallHelp)
        {
            AICharacterManager ally = combatInfo.FindNearestPeacefulAlly();
            if (ally != null)
            {
                fleePosition = ally.transform.position;
                if (Vector3.Distance(aiCharacter.transform.position, ally.transform.position) < 5f)
                {
                    ally.aiCharacterCombatManager.currentTarget = combatInfo.currentTarget;
                }
            }
        }

        if (fleePosition == Vector3.zero)
        {
            Vector3 directionAwayFromTarget = aiCharacter.transform.position - aiCharacter.aiCharacterCombatManager.currentTarget.transform.position;
            directionAwayFromTarget.y = 0;
            directionAwayFromTarget.Normalize();
            fleePosition = aiCharacter.transform.position + (directionAwayFromTarget * safeDistance);
        }

        aiCharacter.navMeshAgent.SetDestination(fleePosition);

        Vector3 lookDirection = fleePosition - aiCharacter.transform.position;
        lookDirection.y = 0;
        if (lookDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            aiCharacter.transform.rotation = Quaternion.Slerp(aiCharacter.transform.rotation, targetRotation, Time.deltaTime * 10f);
        }

        aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, 2f, true);

        return this;
    }

    // 수정됨: 전력질주 모션 초기화
    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);
        aiCharacterManager.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
    }
}