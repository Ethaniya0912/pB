using UnityEngine;
using TDA.Character.AI;

/// <summary>
/// [L3 Domain — RetreatState]
/// 책임: 스태미나 회복을 위한 후퇴 판단 및 NavMeshAgent 속도 설정.
/// 뒷걸음 표현: navMeshAgent.speed를 음수가 아닌 별도 뒤쪽 목적지 설정 + 낮은 속도로 처리.
/// Animator 파라미터 직접 조작 금지.
/// </summary>
[CreateAssetMenu(menuName = "AI/States/Retreat")]
public class RetreatState : AIState
{
    [Header("Next States")]
    public CombatStanceState combatStanceState;
    public PursueTargetState pursueTargetState;

    [Header("Retreat Settings")]
    public float safeDistance = 5f;
    public float recoveredStaminaThreshold = 50f;

    [Header("Locomotion Speed")]
    [Tooltip("후퇴(뒷걸음) NavMeshAgent 속도.")]
    public float retreatSpeed = 2.0f;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        if (aiCharacter.aiCharacterCombatManager.currentTarget == null ||
            aiCharacter.aiCharacterCombatManager.currentTarget.characterNetworkManager.isDead.Value)
            return SwitchState(aiCharacter, pursueTargetState);

        if (aiCharacter.characterNetworkManager.currentStamina.Value >= recoveredStaminaThreshold)
            return SwitchState(aiCharacter, combatStanceState);

        aiCharacter.aiCharacterCombatManager.HandleDefensiveActions();

        // 액션 중에는 이동 정지 — NavMesh 경로만 초기화
        if (aiCharacter.isPerformingAction)
        {
            if (aiCharacter.navMeshAgent != null && aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
                aiCharacter.navMeshAgent.ResetPath();
            // [변경] UpdateAnimatorMovementParameters(0,0) 제거.
            return this;
        }

        // 타겟을 향해 주시 회전 (후퇴 중에도 적을 바라봐야 함)
        Vector3 dirToTarget = aiCharacter.aiCharacterCombatManager.currentTarget.transform.position - aiCharacter.transform.position;
        dirToTarget.y = 0f;
        dirToTarget.Normalize();
        if (dirToTarget == Vector3.zero) dirToTarget = aiCharacter.transform.forward;

        aiCharacter.transform.rotation = Quaternion.Slerp(
            aiCharacter.transform.rotation,
            Quaternion.LookRotation(dirToTarget),
            Time.deltaTime * 5f);

        float distToTarget = Vector3.Distance(aiCharacter.transform.position, aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

        if (distToTarget < safeDistance)
        {
            // 타겟 반대 방향(뒤쪽)으로 후퇴 목적지 설정
            Vector3 retreatPos = aiCharacter.transform.position + (-dirToTarget * 2f);

            if (aiCharacter.navMeshAgent != null && aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
            {
                // [변경] navMeshAgent.speed를 낮게 설정하여 Blend Tree에서 Walk(-0.5) 표현을 유도합니다.
                // 이전 코드의 UpdateAnimatorMovementParameters(0, -0.5f)를 직접 호출하는 대신,
                // speed 기반 정규화로 LocomotionManager가 자동 처리합니다.
                // 주의: 뒷걸음 애니메이션(-Vertical)은 localVelocity.z가 음수가 되어야 합니다.
                // 후퇴 방향(transform 뒤쪽)으로 목적지를 설정하면 desiredVelocity가 뒤쪽을 향하여
                // InverseTransformDirection 결과 localVelocity.z < 0이 됩니다.
                aiCharacter.navMeshAgent.speed = retreatSpeed;
                aiCharacter.navMeshAgent.SetDestination(retreatPos);
            }
        }
        else
        {
            // 충분히 물러섰으면 제자리 대기
            if (aiCharacter.navMeshAgent != null && aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
                aiCharacter.navMeshAgent.ResetPath();
            // 경로 없음 → LocomotionManager가 자동으로 파라미터 0으로 수렴
        }

        return this;
    }

    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);
        // [변경] UpdateAnimatorMovementParameters(0,0) 제거.
    }
}
