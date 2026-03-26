using UnityEngine;
using TDA.Character.AI;

/// <summary>
/// [L3 Domain — PatrolState]
/// 책임: 타겟 탐지 판단, NavMeshAgent 목적지/속도 설정.
/// Animator 파라미터 직접 제어 금지 — AICharacterLocomotionManager.SyncAnimatorParameters()에 위임.
/// </summary>
[CreateAssetMenu(menuName = "AI/States/Patrol")]
public class PatrolState : AIState
{
    [Header("Next States")]
    public PursueTargetState pursueTargetState;
    public CallForHelpState callForHelpState;

    [Header("Detection Settings")]
    [Tooltip("타겟(플레이어)을 감지할 레이어 마스크")]
    public LayerMask detectionLayer;
    public float detectionRadius = 15f;
    public float minimumDetectionAngle = -50f;
    public float maximumDetectionAngle = 50f;

    [Header("Locomotion Speed")]
    [Tooltip("순찰 걷기 NavMeshAgent 속도. Blend Tree Walk 임계값과 맞춰 설정하세요.")]
    public float patrolWalkSpeed = 2.5f;

    [Header("Debug")]
    public bool showDetectionLogs = false;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        AICharacterCombatManager combatInfo = aiCharacter.aiCharacterCombatManager;

        // 아군이 타겟을 알려준 경우 즉시 추적
        if (combatInfo.currentTarget != null)
            return SwitchState(aiCharacter, pursueTargetState);

        // Ghost Action 안전망
        if (aiCharacter.isPerformingAction)
        {
            if (aiCharacter.animator != null && aiCharacter.animator.layerCount > 1)
            {
                AnimatorStateInfo info = aiCharacter.animator.GetCurrentAnimatorStateInfo(1);
                if (!aiCharacter.animator.IsInTransition(1) &&
                   (info.IsName("Empty State") || info.IsName("Empty")))
                {
                    aiCharacter.isPerformingAction = false;
                    if (showDetectionLogs) combatInfo.DebugLog("<color=red>[Fail-safe]</color> Ghost Action 해제.");
                }
            }
            return this;
        }

        // ================================================================================
        // [L3 판단] 타겟 탐지
        // Raycast/OverlapSphere는 L3 Domain의 물리 연산이므로 State에서 수행합니다.
        // ================================================================================
        if (detectionLayer.value == 0 && showDetectionLogs)
            Debug.LogWarning($"[AI] {aiCharacter.name} — DetectionLayer 미설정");

        Collider[] colliders = Physics.OverlapSphere(aiCharacter.transform.position, detectionRadius, detectionLayer);

        foreach (var col in colliders)
        {
            CharacterManager target = col.GetComponentInParent<CharacterManager>();
            if (target == null || target == aiCharacter) continue;

            if (!WorldUtilityManager.Instance.CanIDamageThisTarget(aiCharacter.characterGroup, target.characterGroup))
            {
                if (showDetectionLogs) combatInfo.DebugLog($"아군({target.name}) 무시");
                continue;
            }

            Vector3 dir = target.transform.position - aiCharacter.transform.position;
            dir.y = 0f;
            float angle = Vector3.SignedAngle(aiCharacter.transform.forward, dir.normalized, Vector3.up);

            if (angle < minimumDetectionAngle || angle > maximumDetectionAngle)
            {
                if (showDetectionLogs) combatInfo.DebugLog($"{target.name} 시야각({angle:F1}도) 벗어남");
                continue;
            }

            // 벽 관통 여부 확인
            int enviro = WorldUtilityManager.Instance.GetEnviroLayers();
            Vector3 eye = aiCharacter.transform.position + Vector3.up * 1.5f;
            Vector3 targetEye = target.transform.position + Vector3.up * 1.5f;
            if (Physics.Linecast(eye, targetEye, out RaycastHit hit, enviro))
            {
                if (hit.transform.root != aiCharacter.transform.root && hit.transform.root != target.transform.root)
                {
                    if (showDetectionLogs) combatInfo.DebugLog($"{target.name} — 벽({hit.collider.name})에 막힘");
                    continue;
                }
            }

            combatInfo.currentTarget = target;

            if (combatInfo.aiIntelligenceLevel >= combatInfo.intelligenceToCallHelp && !combatInfo.hasCalledForHelp)
            {
                AICharacterManager ally = combatInfo.FindNearestPeacefulAlly();
                if (ally != null)
                {
                    combatInfo.targetAllyToCall = ally;
                    return SwitchState(aiCharacter, callForHelpState);
                }
            }

            return SwitchState(aiCharacter, pursueTargetState);
        }

        // ================================================================================
        // [L3 판단] 순찰 로직
        // State의 역할: 목적지와 속도를 NavMeshAgent에 지정하는 것.
        // Animator 파라미터 제어는 AICharacterLocomotionManager에 위임합니다.
        // ================================================================================
        if (combatInfo.isWandering)
        {
            if (aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
            {
                bool pathBlocked =
                    !aiCharacter.navMeshAgent.pathPending &&
                    (aiCharacter.navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathPartial ||
                     aiCharacter.navMeshAgent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid);

                if (pathBlocked)
                {
                    combatInfo.isWandering = false;
                    // [변경] Animator 직접 조작 제거.
                    // navMeshAgent.ResetPath()로 hasPath=false가 되면
                    // AICharacterLocomotionManager.SyncAnimatorParameters()가 자동으로 0으로 수렴시킵니다.
                    aiCharacter.navMeshAgent.ResetPath();
                    combatInfo.nextWanderTime = Time.time + 1.0f;
                    return this;
                }

                bool arrived =
                    !aiCharacter.navMeshAgent.pathPending &&
                    aiCharacter.navMeshAgent.remainingDistance <= aiCharacter.navMeshAgent.stoppingDistance;

                if (arrived)
                {
                    combatInfo.isWandering = false;
                    // [변경] Animator 직접 조작 제거. 경로 소멸 → LocomotionManager가 자동 처리.
                    combatInfo.nextWanderTime = Time.time + Random.Range(combatInfo.wanderIntervalMin, combatInfo.wanderIntervalMax);
                }
            }
        }
        else if (Time.time >= combatInfo.nextWanderTime)
        {
            Vector3 destination;

            if (combatInfo.aiIntelligenceLevel >= 7 && combatInfo.patrolWaypoints != null && combatInfo.patrolWaypoints.Length > 0)
            {
                destination = combatInfo.patrolWaypoints[combatInfo.currentWaypointIndex].position;
                combatInfo.currentWaypointIndex = (combatInfo.currentWaypointIndex + 1) % combatInfo.patrolWaypoints.Length;
            }
            else
            {
                destination = combatInfo.GetWanderLocation();
            }

            if (aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
            {
                // [변경] State는 목적지와 속도만 지정합니다.
                // LocomotionManager가 desiredVelocity/speed 비율로 Blend Tree 파라미터를 자동 계산합니다.
                aiCharacter.navMeshAgent.speed = patrolWalkSpeed;
                aiCharacter.navMeshAgent.SetDestination(destination);
                combatInfo.isWandering = true;
            }
        }

        return this;
    }

    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager); // NavMesh 경로 초기화 + applyRootMotion = false
        aiCharacterManager.aiCharacterCombatManager.isWandering = false;
        // [변경] Animator 직접 조작 제거. base 처리로 충분합니다.
    }
}
