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
            return SwitchState(aiCharacter, pursueTargetState);
        }

        // =========================================================================================
        // 🚨 [상태 불일치(Ghost Action) 안전망]
        // 전투가 끝난 직후 Patrol로 넘어왔는데 플래그가 안 풀려있으면 평생 굳어버리는 현상 방지
        // =========================================================================================
        if (aiCharacter.isPerformingAction)
        {
            if (aiCharacter.animator != null && aiCharacter.animator.layerCount > 1)
            {
                AnimatorStateInfo actionStateInfo = aiCharacter.animator.GetCurrentAnimatorStateInfo(1);
                if (!aiCharacter.animator.IsInTransition(1) &&
                   (actionStateInfo.IsName("Empty State") || actionStateInfo.IsName("Empty")))
                {
                    aiCharacter.isPerformingAction = false;
                    combatInfo.DebugLog("<color=red>[Fail-safe]</color> Patrol 중 Ghost Action 감지! 강제 해제합니다.");
                }
            }

            // 액션 중이면 이동 로직을 타지 않고 대기
            return this;
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
                            return SwitchState(aiCharacter, callForHelpState);
                        }
                    }

                    return SwitchState(aiCharacter, pursueTargetState);
                }
            }
        }

        // 2. 패트롤 로직 (공유 SO 대신 개별 매니저의 변수 사용)
        if (combatInfo.isWandering)
        {
            if (aiCharacter.navMeshAgent != null && aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
            {
                // =========================================================================================
                // 🚨 [버그 픽스] 경로 탐색 실패 시 무한 대기 버그 해결
                // 목적지가 벽 안쪽이거나 갈 수 없는 곳이면 런닝머신을 타며 영원히 굳어버립니다.
                // =========================================================================================
                if (!aiCharacter.navMeshAgent.pathPending &&
                    (aiCharacter.navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathPartial ||
                     aiCharacter.navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid))
                {
                    combatInfo.DebugLog("<color=orange>순찰 경로가 막혔습니다. 새로운 경로를 탐색합니다.</color>");
                    combatInfo.isWandering = false;
                    aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
                    combatInfo.nextWanderTime = Time.time + 1.0f; // 1초 뒤 바로 재탐색
                    return this;
                }

                // 경로 계산이 끝났고(pathPending == false), 목적지에 도착했다면
                if (!aiCharacter.navMeshAgent.pathPending && aiCharacter.navMeshAgent.remainingDistance <= aiCharacter.navMeshAgent.stoppingDistance)
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

    // 상태 전환 시 걷기 모션과 방황 플래그를 정지시켜 백지화
    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);
        aiCharacterManager.aiCharacterCombatManager.isWandering = false;
        aiCharacterManager.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
    }
}