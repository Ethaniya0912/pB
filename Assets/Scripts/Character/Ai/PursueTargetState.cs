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

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        if (aiCharacter.aiCharacterCombatManager.currentTarget == null ||
            aiCharacter.aiCharacterCombatManager.currentTarget.characterNetworkManager.isDead.Value)
        {
            return SwitchState(aiCharacter, patrolState); // 수정됨: SwitchState 사용
        }

        // [근본 원인 해결] 에러 방어 로직 (Guard Clause)
        // AI가 스폰 직후 허공에 떠있거나 네비메시가 아직 로드되지 않았을 때 SetDestination을 호출하여 발생하는 에러를 원천 차단합니다.
        if (aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
        {
            aiCharacter.navMeshAgent.SetDestination(aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);
        }

        // 이동 파라미터는 에이전트가 땅에 닿기 전이라도 애니메이션 재생을 위해 전달합니다.
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