using UnityEngine;

[CreateAssetMenu(menuName = "AI/States/Flee")]
public class FleeState : AIState
{
    [Header("Next State")]
    public PatrolState patrolState; // 도망 성공 시 돌아갈 상태
    public CombatStanceState combatStanceState; // [🔥 추가] 막다른 길에 몰렸을 때 반격할 상태

    [Header("Flee Settings")]
    public float safeDistance = 20f;

    [Tooltip("도망갈 수 있는 최대 경로의 끝점이 타겟으로부터 이 거리보다 짧으면 막다른 길로 간주하고 반격합니다.")]
    public float corneredDistance = 8f; // [🔥 추가] 궁지 판별 거리 임계값

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        if (aiCharacter.aiCharacterCombatManager.currentTarget == null ||
            aiCharacter.aiCharacterCombatManager.currentTarget.characterNetworkManager.isDead.Value)
        {
            aiCharacter.aiCharacterCombatManager.DebugLog("타겟이 없거나 죽어서 Patrol 상태로 돌아갑니다.");
            return SwitchState(aiCharacter, patrolState);
        }

        float distanceFromTarget = Vector3.Distance(aiCharacter.transform.position, aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

        // 충분히 멀어지면 도망 성공
        if (distanceFromTarget > safeDistance)
        {
            aiCharacter.aiCharacterCombatManager.DebugLog("안전하게 도망쳤습니다. Patrol 상태로 돌아갑니다.");
            return SwitchState(aiCharacter, patrolState);
        }

        Vector3 fleePosition = Vector3.zero;
        AICharacterCombatManager combatInfo = aiCharacter.aiCharacterCombatManager;

        // 1. 지능이 높을 경우 주변 아군(지원군)을 찾아 그쪽으로 도망
        if (combatInfo.aiIntelligenceLevel >= combatInfo.intelligenceToCallHelp)
        {
            AICharacterManager ally = combatInfo.FindNearestPeacefulAlly();
            if (ally != null)
            {
                fleePosition = ally.transform.position;
                if (Vector3.Distance(aiCharacter.transform.position, ally.transform.position) < 5f)
                {
                    ally.aiCharacterCombatManager.currentTarget = combatInfo.currentTarget;
                    aiCharacter.aiCharacterCombatManager.DebugLog("아군에게 헬프를 요청했습니다!");
                }
            }
        }

        // 2. 아군이 없으면 타겟 반대 방향으로 도망
        if (fleePosition == Vector3.zero)
        {
            Vector3 directionAwayFromTarget = aiCharacter.transform.position - aiCharacter.aiCharacterCombatManager.currentTarget.transform.position;
            directionAwayFromTarget.y = 0;
            directionAwayFromTarget.Normalize();

            // 타겟 반대 방향으로 safeDistance만큼 떨어진 목표 지점 설정
            Vector3 desiredFleePos = aiCharacter.transform.position + (directionAwayFromTarget * safeDistance);

            // 해당 지점 근처에 갈 수 있는 NavMesh가 있는지 샘플링
            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(desiredFleePos, out hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            {
                fleePosition = hit.position;
            }
            else
            {
                fleePosition = desiredFleePos;
            }
        }

        aiCharacter.navMeshAgent.SetDestination(fleePosition);

        // ---------------------------------------------------------
        // [🔥 핵심 추가: 막다른 길(Cornered) 판별 로직]
        // ---------------------------------------------------------
        if (!aiCharacter.navMeshAgent.pathPending)
        {
            // 경로가 끊겨있거나(PathPartial) 아예 유효하지 않은(PathInvalid) 경우 = 벽에 막혔음
            if (aiCharacter.navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathPartial ||
                aiCharacter.navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid)
            {
                // 막힌 경로의 최종 도착 예정지와 플레이어(타겟) 간의 거리를 잽니다.
                float distFromEndToTarget = Vector3.Distance(aiCharacter.navMeshAgent.pathEndPosition, aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

                // 최대로 도망가봐야 플레이어와 가까운 상태(막다른 길)라면, 도망을 포기하고 반격합니다.
                if (distFromEndToTarget < corneredDistance)
                {
                    aiCharacter.aiCharacterCombatManager.DebugLog($"🚨 막다른 길에 몰렸습니다! (예상 도착지 여유 공간: {distFromEndToTarget:F1}m) 쥐가 고양이를 뭅니다.");
                    if (combatStanceState != null)
                    {
                        return SwitchState(aiCharacter, combatStanceState);
                    }
                }
            }
        }

        // 3. 이동 및 회전 처리
        Vector3 lookDirection = fleePosition - aiCharacter.transform.position;
        lookDirection.y = 0;
        if (lookDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            aiCharacter.transform.rotation = Quaternion.Slerp(aiCharacter.transform.rotation, targetRotation, Time.deltaTime * 10f);
        }

        // 도망가는 전력질주 모션 재생 (이동 수치 X:0, Y:2)
        aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, 2f, true);

        return this;
    }

    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);
        // 다른 상태로 넘어갈 때 뛰던 애니메이션 초기화
        aiCharacterManager.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
    }
}