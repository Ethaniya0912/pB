using UnityEngine;
using static Unity.Netcode.Components.AttachableBehaviour;

[CreateAssetMenu(menuName = "AI/States/Combat Stance")]
public class CombatStanceState : AIState
{
    [Header("Next States")]
    public AttackState attackState;
    public PursueTargetState pursueTargetState;
    public FleeState fleeState;
    public RetreatState retreatState;

    [Header("Combat Settings")]
    public float attackRange = 2.5f;
    public int fleeHealthThreshold = 20;
    public float retreatStaminaThreshold = 10f;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        if (aiCharacter.aiCharacterCombatManager.currentTarget == null)
            return SwitchState(aiCharacter, pursueTargetState); // 수정됨: SwitchState 사용

        if (aiCharacter.characterNetworkManager.currentHealth.Value <= fleeHealthThreshold)
            return SwitchState(aiCharacter, fleeState); // 수정됨: SwitchState 사용

        aiCharacter.aiCharacterCombatManager.HandleDefensiveActions();

        if (aiCharacter.isPerformingAction)
        {
            aiCharacter.navMeshAgent.ResetPath();
            aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
            return this;
        }

        if (aiCharacter.characterNetworkManager.currentStamina.Value <= retreatStaminaThreshold)
            return SwitchState(aiCharacter, retreatState); // 수정됨: SwitchState 사용

        float distanceFromTarget = Vector3.Distance(aiCharacter.transform.position, aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

        if (distanceFromTarget > attackRange)
            return SwitchState(aiCharacter, pursueTargetState); // 수정됨: SwitchState 사용

        aiCharacter.aiCharacterCombatManager.HandleStrafingAroundTarget();

        if (!aiCharacter.isPerformingAction)
        {
            if (Time.time >= aiCharacter.aiCharacterCombatManager.nextAttackTime)
            {
                if (aiCharacter.aiCharacterCombatManager.IsAllyAttackingTarget())
                {
                    aiCharacter.aiCharacterCombatManager.nextAttackTime = Time.time + Random.Range(1f, 2f);
                    return this;
                }
                return SwitchState(aiCharacter, attackState); // 수정됨: SwitchState 사용
            }
        }

        return this;
    }

    // 수정됨: 맴돌기 움직임을 멈추고 백지화
    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);
        aiCharacterManager.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
    }
}