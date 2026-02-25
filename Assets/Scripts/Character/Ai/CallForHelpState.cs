using UnityEngine;

[CreateAssetMenu(menuName = "AI/States/Call For Help")]
public class CallForHelpState : AIState
{
    [Header("Next State")]
    public PursueTargetState pursueTargetState;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        AICharacterCombatManager combatInfo = aiCharacter.aiCharacterCombatManager;

        if (combatInfo.currentTarget == null) return SwitchState(aiCharacter, pursueTargetState); // 수정됨: SwitchState 사용

        AICharacterManager targetAlly = combatInfo.targetAllyToCall;

        if (targetAlly == null)
        {
            combatInfo.hasCalledForHelp = true;
            return SwitchState(aiCharacter, pursueTargetState); // 수정됨: SwitchState 사용
        }

        aiCharacter.navMeshAgent.SetDestination(targetAlly.transform.position);
        aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, 2f, true);

        Vector3 directionToAlly = targetAlly.transform.position - aiCharacter.transform.position;
        directionToAlly.y = 0;
        if (directionToAlly != Vector3.zero)
        {
            aiCharacter.transform.rotation = Quaternion.Slerp(aiCharacter.transform.rotation, Quaternion.LookRotation(directionToAlly), Time.deltaTime * 10f);
        }

        float distanceToAlly = Vector3.Distance(aiCharacter.transform.position, targetAlly.transform.position);
        if (distanceToAlly <= 5f)
        {
            targetAlly.aiCharacterCombatManager.currentTarget = combatInfo.currentTarget;
            combatInfo.hasCalledForHelp = true;
            return SwitchState(aiCharacter, pursueTargetState); // 수정됨: SwitchState 사용
        }

        return this;
    }

    // 수정됨: 지원군 타겟 변수를 리셋하여 재사용 시 오염 방지
    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);
        aiCharacterManager.aiCharacterCombatManager.targetAllyToCall = null;
        aiCharacterManager.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
    }
}