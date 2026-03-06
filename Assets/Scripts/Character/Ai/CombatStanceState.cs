using TDA.Character.AI;
using UnityEngine;
using UnityEngine.TextCore.Text;

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
        {
            aiCharacter.aiCharacterCombatManager.DebugLog("타겟을 잃어버림 -> Pursue 상태로 전환");
            return SwitchState(aiCharacter, pursueTargetState);
        }

        if (aiCharacter.characterNetworkManager.currentHealth.Value <= fleeHealthThreshold)
        {
            aiCharacter.aiCharacterCombatManager.DebugLog("체력 부족! -> Flee 상태로 전환");
            return SwitchState(aiCharacter, fleeState);
        }

        aiCharacter.aiCharacterCombatManager.HandleDefensiveActions();

        if (aiCharacter.isPerformingAction)
        {
            aiCharacter.navMeshAgent.ResetPath();
            aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
            return this;
        }

        if (aiCharacter.characterNetworkManager.currentStamina.Value <= retreatStaminaThreshold)
        {
            aiCharacter.aiCharacterCombatManager.DebugLog("스테미나 부족 -> Retreat 상태로 전환");
            return SwitchState(aiCharacter, retreatState);
        }

        float distanceFromTarget = Vector3.Distance(aiCharacter.transform.position, aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

        if (distanceFromTarget > attackRange)
        {
            aiCharacter.aiCharacterCombatManager.DebugLog($"타겟이 공격 범위를 벗어남 (거리: {distanceFromTarget:F1}) -> Pursue 상태로 전환");
            return SwitchState(aiCharacter, pursueTargetState);
        }

        aiCharacter.aiCharacterCombatManager.HandleStrafingAroundTarget();

        if (!aiCharacter.isPerformingAction)
        {
            if (Time.time >= aiCharacter.aiCharacterCombatManager.nextAttackTime)
            {
                if (aiCharacter.aiCharacterCombatManager.IsAllyAttackingTarget())
                {
                    aiCharacter.aiCharacterCombatManager.DebugLog("아군이 공격 중이므로 대기합니다.");
                    aiCharacter.aiCharacterCombatManager.nextAttackTime = Time.time + Random.Range(1f, 2f);
                    return this;
                }

                aiCharacter.aiCharacterCombatManager.DebugLog("공격 쿨타임 완료 -> Attack 상태로 전환");
                return SwitchState(aiCharacter, attackState);
            }
        }

        return this;
    }

    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);

        // CombatStanceState 내부에서 추가로 ResetPath를 호출하는 곳이 있다면 동일하게 감싸줍니다.
        if (aiCharacterManager.navMeshAgent != null && aiCharacterManager.navMeshAgent.isActiveAndEnabled && aiCharacterManager.navMeshAgent.isOnNavMesh)
        {
            // aiCharacter.navMeshAgent.ResetPath();
        }
    }
}