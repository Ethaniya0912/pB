using UnityEngine;
using TDA.Character.AI;

[CreateAssetMenu(menuName = "AI/States/Patrol")]
public class PatrolState : AIState
{
    [Header("Next States")]
    public PursueTargetState pursueTargetState;
    public CallForHelpState callForHelpState;

    [Header("Detection Settings")]
    public LayerMask detectionLayer;
    public float detectionRadius = 15f;
    public float minimumDetectionAngle = -50f;
    public float maximumDetectionAngle = 50f;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        AICharacterCombatManager combatInfo = aiCharacter.aiCharacterCombatManager;

        // 0. 누군가(아군)가 내게 적의 존재를 알렸다면 즉시 추적 상태로!
        if (combatInfo.currentTarget != null)
        {
            return SwitchState(aiCharacter, pursueTargetState); // 수정됨: SwitchState 사용
        }

        // 1. 타겟 탐지 로직
        Collider[] colliders = Physics.OverlapSphere(aiCharacter.transform.position, detectionRadius, detectionLayer);
        foreach (var collider in colliders)
        {
            CharacterManager targetCharacter = collider.transform.GetComponent<CharacterManager>();

            if (targetCharacter != null && WorldUtilityManager.Instance.CanIDamageThisTarget(aiCharacter.characterGroup, targetCharacter.characterGroup))
            {
                Vector3 targetDirection = targetCharacter.transform.position - aiCharacter.transform.position;
                float viewableAngle = Vector3.Angle(targetDirection, aiCharacter.transform.forward);

                if (viewableAngle > minimumDetectionAngle && viewableAngle < maximumDetectionAngle)
                {
                    combatInfo.currentTarget = targetCharacter;

                    // 지원군 판단
                    if (combatInfo.aiIntelligenceLevel >= combatInfo.intelligenceToCallHelp && !combatInfo.hasCalledForHelp)
                    {
                        AICharacterManager allyToCall = combatInfo.FindNearestPeacefulAlly();
                        if (allyToCall != null)
                        {
                            combatInfo.targetAllyToCall = allyToCall; // 매니저에 타겟 저장 (SO 오염 방지)
                            return SwitchState(aiCharacter, callForHelpState); // 수정됨: SwitchState 사용
                        }
                    }

                    return SwitchState(aiCharacter, pursueTargetState); // 수정됨: SwitchState 사용
                }
            }
        }

        // 2. 패트롤 로직 (공유 SO 대신 개별 매니저의 변수 사용)
        if (combatInfo.isWandering)
        {
            // 🚨 [에러 방어 안전망] 에이전트가 살아서 맵(NavMesh) 위에 안착했을 때만 목적지까지의 거리를 묻습니다!
            if (aiCharacter.navMeshAgent != null && aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
            {
                if (aiCharacter.navMeshAgent.remainingDistance <= aiCharacter.navMeshAgent.stoppingDistance)
                {
                    combatInfo.isWandering = false;
                    aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);

                    float waitTime = Random.Range(combatInfo.wanderIntervalMin, combatInfo.wanderIntervalMax);
                    combatInfo.nextWanderTime = Time.time + waitTime;
                }
            }
        }
        else
        {
            if (Time.time >= combatInfo.nextWanderTime)
            {
                Vector3 wanderTarget = Vector3.zero;

                if (combatInfo.aiIntelligenceLevel >= 7 && combatInfo.patrolWaypoints != null && combatInfo.patrolWaypoints.Length > 0)
                {
                    wanderTarget = combatInfo.patrolWaypoints[combatInfo.currentWaypointIndex].position;
                    combatInfo.currentWaypointIndex++;
                    if (combatInfo.currentWaypointIndex >= combatInfo.patrolWaypoints.Length) combatInfo.currentWaypointIndex = 0;
                }
                else
                {
                    wanderTarget = combatInfo.GetWanderLocation();
                }

                // 🚨 [에러 방어 안전망] 목표 지점을 설정할 때도 에이전트가 땅에 있는지 확인합니다!
                if (aiCharacter.navMeshAgent != null && aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
                {
                    aiCharacter.navMeshAgent.SetDestination(wanderTarget);
                    aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0.5f, false);
                    combatInfo.isWandering = true;
                }
            }
        }

        return this;
    }

    // 수정됨: 상태 전환 시 걷기 모션과 방황 플래그를 정지시켜 백지화
    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);
        aiCharacterManager.aiCharacterCombatManager.isWandering = false;
        aiCharacterManager.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
    }
}