using UnityEngine;
using TDA.Character.AI;

[CreateAssetMenu(menuName = "AI/States/Patrol")]
public class PatrolState : AIState
{
    [Header("Next States")]
    public PursueTargetState pursueTargetState;
    public CallForHelpState callForHelpState;

    [Header("Detection Settings")]
    [Tooltip("타겟(플레이어)을 감지할 레이어 마스크를 반드시 지정하세요! (예: Character)")]
    public LayerMask detectionLayer;
    public float detectionRadius = 15f;
    public float minimumDetectionAngle = -50f;
    public float maximumDetectionAngle = 50f;

    [Header("Debug")]
    [Tooltip("체크 시 몬스터가 왜 플레이어를 감지하지 못하는지 콘솔에 상세한 이유를 출력합니다.")]
    public bool showDetectionLogs = false;

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
                    if (showDetectionLogs) combatInfo.DebugLog("<color=red>[Fail-safe]</color> Patrol 중 Ghost Action 감지! 강제 해제합니다.");
                }
            }
            return this;
        }

        // =========================================================================================
        // 👁️ 1. 타겟 탐지 로직 (FOV 및 시야각 완벽 보정)
        // =========================================================================================
        if (detectionLayer.value == 0 && showDetectionLogs)
        {
            Debug.LogWarning($"<color=red>[AI 에러]</color> {aiCharacter.name}의 PatrolState에 Detection Layer가 설정되지 않았습니다! 인스펙터에서 플레이어의 레이어를 꼭 체크하세요.");
        }

        Collider[] colliders = Physics.OverlapSphere(aiCharacter.transform.position, detectionRadius, detectionLayer);

        foreach (var collider in colliders)
        {
            CharacterManager targetCharacter = collider.transform.GetComponent<CharacterManager>();

            if (targetCharacter != null && targetCharacter != aiCharacter)
            {
                // 피아 식별 (팀 확인)
                if (WorldUtilityManager.Instance.CanIDamageThisTarget(aiCharacter.characterGroup, targetCharacter.characterGroup))
                {
                    // [버그 수정] Y축(높낮이) 차이 때문에 시야각 판정이 꼬이는 것을 막기 위해 XZ 평면으로 뭉개버립니다.
                    Vector3 targetDirection = targetCharacter.transform.position - aiCharacter.transform.position;
                    targetDirection.y = 0f;
                    Vector3 forward = aiCharacter.transform.forward;
                    forward.y = 0f;

                    // SignedAngle을 사용하여 좌(-)/우(+) 시야 각도를 정확히 추출합니다.
                    float viewableAngle = Vector3.SignedAngle(forward, targetDirection.normalized, Vector3.up);

                    if (viewableAngle >= minimumDetectionAngle && viewableAngle <= maximumDetectionAngle)
                    {
                        // [신규 추가] 벽 관통 감지 방지 (Line of Sight)
                        // 플레이어가 시야각 안에 있더라도, 벽 뒤에 숨어있다면 감지하지 못하게 합니다.
                        int enviroLayers = WorldUtilityManager.Instance.GetEnviroLayers();
                        Vector3 eyePosition = aiCharacter.transform.position + Vector3.up * 1.5f;
                        Vector3 targetEyePosition = targetCharacter.transform.position + Vector3.up * 1.5f;

                        if (Physics.Linecast(eyePosition, targetEyePosition, enviroLayers))
                        {
                            if (showDetectionLogs) combatInfo.DebugLog($"타겟({targetCharacter.name})이 시야각 내에 있지만 <color=orange>벽에 가려져 있습니다.</color>");
                            continue; // 벽에 막혔으므로 다음 타겟 검사
                        }

                        if (showDetectionLogs) combatInfo.DebugLog($"<color=lime>타겟({targetCharacter.name}) 발견!</color> 추적을 시작합니다.");
                        combatInfo.currentTarget = targetCharacter;

                        // 지원군 판단
                        if (combatInfo.aiIntelligenceLevel >= combatInfo.intelligenceToCallHelp && !combatInfo.hasCalledForHelp)
                        {
                            AICharacterManager allyToCall = combatInfo.FindNearestPeacefulAlly();
                            if (allyToCall != null)
                            {
                                combatInfo.targetAllyToCall = allyToCall;
                                return SwitchState(aiCharacter, callForHelpState);
                            }
                        }

                        return SwitchState(aiCharacter, pursueTargetState);
                    }
                    else if (showDetectionLogs)
                    {
                        combatInfo.DebugLog($"타겟({targetCharacter.name})이 거리에 있지만 <color=orange>시야각({viewableAngle:F1}도)</color>을 벗어났습니다. (요구사항: {minimumDetectionAngle}도 ~ {maximumDetectionAngle}도)");
                    }
                }
                else if (showDetectionLogs)
                {
                    combatInfo.DebugLog($"탐지된 대상({targetCharacter.name})은 공격할 수 없는 아군/동일그룹({targetCharacter.characterGroup})입니다.");
                }
            }
        }

        // =========================================================================================
        // 🚶 2. 패트롤 로직 (경로 막힘 픽스 복구 완료)
        // =========================================================================================
        if (combatInfo.isWandering)
        {
            if (aiCharacter.navMeshAgent != null && aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
            {
                // [이전 버그 픽스 복구] 목적지가 벽 안쪽이거나 갈 수 없는 곳이면 영원히 런닝머신을 타는 현상 해결
                if (!aiCharacter.navMeshAgent.pathPending &&
                    (aiCharacter.navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathPartial ||
                     aiCharacter.navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid))
                {
                    if (showDetectionLogs) combatInfo.DebugLog("<color=orange>순찰 경로가 막혔습니다. 탐색을 포기합니다.</color>");
                    combatInfo.isWandering = false;
                    aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
                    combatInfo.nextWanderTime = Time.time + 1.0f; // 1초 뒤 바로 재탐색
                    return this;
                }

                // 경로 계산이 끝났고 목적지에 도착했다면
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

    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);
        aiCharacterManager.aiCharacterCombatManager.isWandering = false;
        aiCharacterManager.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
    }
}