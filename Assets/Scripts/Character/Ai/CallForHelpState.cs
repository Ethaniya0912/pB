using UnityEngine;
using TDA.Character.AI;

/// <summary>
/// [L3 Domain — CallForHelpState]
/// 책임: 아군 위치로의 이동 판단 및 NavMeshAgent 목적지/속도 설정.
/// Animator 파라미터 직접 조작 금지.
/// </summary>
[CreateAssetMenu(menuName = "AI/States/Call For Help")]
public class CallForHelpState : AIState
{
    [Header("Next State")]
    public PursueTargetState pursueTargetState;

    [Header("Locomotion Speed")]
    [Tooltip("아군에게 달려가는 NavMeshAgent 속도.")]
    public float helpSprintSpeed = 5.5f;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        AICharacterCombatManager combatInfo = aiCharacter.aiCharacterCombatManager;

        if (combatInfo.currentTarget == null)
            return SwitchState(aiCharacter, pursueTargetState);

        AICharacterManager targetAlly = combatInfo.targetAllyToCall;

        if (targetAlly == null)
        {
            combatInfo.hasCalledForHelp = true;
            return SwitchState(aiCharacter, pursueTargetState);
        }

        // [변경] navMeshAgent.speed 설정으로 Sprint 표현.
        // LocomotionManager.SyncAnimatorParameters()가 isSprinting 판단을 자동 처리합니다.
        if (aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
        {
            aiCharacter.navMeshAgent.speed = helpSprintSpeed;
            aiCharacter.navMeshAgent.SetDestination(targetAlly.transform.position);
        }

        // 아군 주시 회전 — 이동 방향이 있으므로 LocomotionManager.HandleRotation()에 위임해도 되지만,
        // 명시적으로 아군 방향을 바라보는 것이 의도이므로 직접 처리합니다.
        Vector3 dirToAlly = targetAlly.transform.position - aiCharacter.transform.position;
        dirToAlly.y = 0f;
        if (dirToAlly.sqrMagnitude > 0.01f)
        {
            aiCharacter.transform.rotation = Quaternion.Slerp(
                aiCharacter.transform.rotation,
                Quaternion.LookRotation(dirToAlly),
                Time.deltaTime * 10f);
        }

        float distToAlly = Vector3.Distance(aiCharacter.transform.position, targetAlly.transform.position);
        if (distToAlly <= 5f)
        {
            targetAlly.aiCharacterCombatManager.currentTarget = combatInfo.currentTarget;
            combatInfo.hasCalledForHelp = true;
            return SwitchState(aiCharacter, pursueTargetState);
        }

        return this;
    }

    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);
        aiCharacterManager.aiCharacterCombatManager.targetAllyToCall = null;
        // [변경] UpdateAnimatorMovementParameters(0,0) 제거.
    }
}
