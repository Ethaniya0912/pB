using TDA.Character;
using TDA.Character.AI;
using UnityEngine;

/// <summary>
/// [L3 Domain — FleeState]
/// 책임: 도주 목적지 판단 및 NavMeshAgent 목적지/속도 설정.
/// Animator 파라미터 직접 조작 금지 — LocomotionManager에 위임.
/// </summary>
[CreateAssetMenu(menuName = "AI/States/Flee")]
public class FleeState : AIState
{
    [Header("Next States")]
    public PatrolState patrolState;
    public CombatStanceState combatStanceState;

    [Header("Flee Settings")]
    public float safeDistance = 20f;
    public float corneredDistance = 8f;

    [Header("Locomotion Speed")]
    [Tooltip("전력질주 NavMeshAgent 속도. Blend Tree Sprint 임계값과 맞추세요.")]
    public float fleeSprintSpeed = 6.0f;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        if (aiCharacter.aiCharacterCombatManager.currentTarget == null ||
            aiCharacter.aiCharacterCombatManager.currentTarget.characterNetworkManager.isDead.Value)
            return SwitchState(aiCharacter, patrolState);

        float distToTarget = Vector3.Distance(
            aiCharacter.transform.position,
            aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

        if (distToTarget > safeDistance)
            return SwitchState(aiCharacter, patrolState);

        // 도주 목적지 계산
        Vector3 fleePosition = CalculateFleePosition(aiCharacter);

        if (aiCharacter.navMeshAgent != null && aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
        {
            // [변경] navMeshAgent.speed로 전력질주 표현.
            // LocomotionManager.SyncAnimatorParameters()가 isSprinting 판단을 자동 처리합니다.
            aiCharacter.navMeshAgent.speed = fleeSprintSpeed;
            aiCharacter.navMeshAgent.SetDestination(fleePosition);

            // 막다른 길 판별
            if (!aiCharacter.navMeshAgent.pathPending)
            {
                if (aiCharacter.navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathPartial ||
                    aiCharacter.navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid)
                {
                    float distEndToTarget = Vector3.Distance(
                        aiCharacter.navMeshAgent.pathEndPosition,
                        aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

                    if (distEndToTarget < corneredDistance && combatStanceState != null)
                    {
                        aiCharacter.aiCharacterCombatManager.DebugLog("막다른 길 → 반격 (CombatStance)");
                        return SwitchState(aiCharacter, combatStanceState);
                    }
                }
            }
        }

        // 도주 방향 주시 회전
        Vector3 lookDir = fleePosition - aiCharacter.transform.position;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.01f)
        {
            aiCharacter.transform.rotation = Quaternion.Slerp(
                aiCharacter.transform.rotation,
                Quaternion.LookRotation(lookDir),
                Time.deltaTime * 10f);
        }

        return this;
    }

    private Vector3 CalculateFleePosition(AICharacterManager aiCharacter)
    {
        AICharacterCombatManager combatInfo = aiCharacter.aiCharacterCombatManager;

        // 지능이 높으면 아군 쪽으로 도주
        if (combatInfo.aiIntelligenceLevel >= combatInfo.intelligenceToCallHelp)
        {
            AICharacterManager ally = combatInfo.FindNearestPeacefulAlly();
            if (ally != null)
            {
                if (Vector3.Distance(aiCharacter.transform.position, ally.transform.position) < 5f)
                {
                    ally.aiCharacterCombatManager.currentTarget = combatInfo.currentTarget;
                    combatInfo.DebugLog("아군에게 도움 요청");
                }
                return ally.transform.position;
            }
        }

        // 타겟 반대 방향으로 도주
        Vector3 awayDir = aiCharacter.transform.position - combatInfo.currentTarget.transform.position;
        awayDir.y = 0f;
        awayDir.Normalize();

        Vector3 desired = aiCharacter.transform.position + awayDir * safeDistance;

        if (UnityEngine.AI.NavMesh.SamplePosition(desired, out UnityEngine.AI.NavMeshHit hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            return hit.position;

        return desired;
    }

    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);
        // [변경] UpdateAnimatorMovementParameters(0,0) 제거.
        // base.ResetStateFlags()의 navMeshAgent.ResetPath() + applyRootMotion=false로 충분합니다.
        // LocomotionManager가 다음 프레임에 파라미터를 0으로 수렴시킵니다.
    }
}
