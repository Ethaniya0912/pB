using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// AI 캐릭터의 전투 전술(탐지, 방어, 맴돌기 등)과 개별 런타임 상태를 관리하는 매니저 클래스입니다.
/// </summary>
public class AICharacterCombatManager : CharacterCombatManager
{
    private AICharacterManager aiCharacter;

    [Header("Debug Settings")]
    [Tooltip("체크 시 이 몬스터의 AI 상태 로그를 콘솔에 출력합니다.")]
    public bool showDebugLogs = true;

    [Header("AI Intelligence & Combat Skill")]
    [Tooltip("1~10: 지능 레벨. 7 이상이면 동료와 포위망 구축 및 지원군을 요청합니다.")]
    [Range(1, 10)] public int aiIntelligenceLevel = 5;
    [Tooltip("1~10: 전투 레벨. 높을수록 방어, 패링, 회피 확률과 반응 속도가 빨라집니다.")]
    [Range(1, 10)] public int combatLevel = 5;

    [Header("Patrol & Wander Settings")]
    public float wanderRadius = 10f;
    public float wanderIntervalMin = 3f;
    public float wanderIntervalMax = 7f;
    [Range(0, 100)] public float groupUpChance = 40f;

    [Header("Patrol Waypoints (High Intelligence)")]
    public Transform[] patrolWaypoints;
    [HideInInspector] public int currentWaypointIndex = 0;

    [Header("Reinforcement Settings")]
    public float callForHelpRadius = 30f;
    public int intelligenceToCallHelp = 8;
    [HideInInspector] public bool hasCalledForHelp = false;

    // 수정됨: ScriptableObject 최적화 원칙 반영 (개별 런타임 데이터는 매니저가 보관)
    [Header("State Execution Data (Runtime - Do Not Touch)")]
    [HideInInspector] public bool isWandering = false;
    [HideInInspector] public float nextWanderTime = 0f;
    [HideInInspector] public AICharacterManager targetAllyToCall = null;
    [HideInInspector] public float nextAttackTime = 0f;

    [Header("Detection Settings")]
    public float detectionRadius = 15f;
    public float minimumDetectionAngle = -50f;
    public float maximumDetectionAngle = 50f;

    [Header("Defense Tactics")]
    [Range(0, 100)] public float baseParryChance = 20f;
    [Range(0, 100)] public float baseBlockChance = 30f;
    [Range(0, 100)] public float baseEvadeChance = 20f;
    public float defensiveActionCooldown = 2f;
    private float nextDefensiveActionTime;

    [Header("Strafing Tactics (Circling)")]
    public float strafeSpeed = 2f;
    public float strafeDirectionChangeTime = 3f;
    private float nextStrafeChangeTime;
    private int strafeDirection = 1;

    protected override void Awake()
    {
        base.Awake();
        aiCharacter = GetComponent<AICharacterManager>();
    }

    /// <summary>
    /// [🔥 추가] AI 전용 커스텀 디버그 함수
    /// 에디터에서 이 오브젝트를 선택했을 때만 로그를 띄워 로그 폭탄을 방지합니다.
    /// </summary>
    public void DebugLog(string message)
    {
        if (!showDebugLogs) return;

#if UNITY_EDITOR
        // 씬/하이라키에서 현재 선택된 오브젝트가 이 몹일 때만 출력
        if (Selection.activeGameObject != this.gameObject) return;
#endif

        Debug.Log($"<color=#32CD32>[{gameObject.name}]</color> {message}");
    }

    public void HandleDefensiveActions()
    {
        if (currentTarget == null) return;
        if (aiCharacter.isPerformingAction) return;

        if (currentTarget.isPerformingAction)
        {
            if (Time.time < nextDefensiveActionTime) return;

            float levelModifier = combatLevel / 10f;
            float actualEvadeChance = baseEvadeChance * levelModifier;
            float actualParryChance = baseParryChance * levelModifier;
            float actualBlockChance = baseBlockChance * levelModifier;

            float randomDice = Random.Range(0f, 100f);

            if (randomDice < actualEvadeChance) PerformEvade();
            else if (randomDice < actualEvadeChance + actualParryChance) PerformParry();
            else if (randomDice < actualEvadeChance + actualParryChance + actualBlockChance) PerformBlock();

            float actualCooldown = defensiveActionCooldown * (1f + (1f - levelModifier));
            nextDefensiveActionTime = Time.time + actualCooldown;
        }
    }

    private void PerformEvade()
    {
        Vector3 attackerForward = currentTarget.transform.forward;
        float dotRight = Vector3.Dot(transform.right, attackerForward);
        string evadeAnimation = "Dodge_Back";

        float randomDirection = Random.value;
        if (randomDirection < 0.6f)
        {
            if (dotRight > 0) evadeAnimation = "Dodge_Right";
            else evadeAnimation = "Dodge_Left";
        }

        DebugLog($"방어 행동: 회피 ({evadeAnimation})");
        aiCharacter.characterAnimationManager.PlayTargetAnimation(evadeAnimation, true);
    }

    private void PerformParry()
    {
        DebugLog("방어 행동: 패링 (Parry_01)");
        aiCharacter.characterAnimationManager.PlayTargetAnimation("Parry_01", true);
    }

    private void PerformBlock()
    {
        DebugLog("방어 행동: 방어 (Block_Start)");
        aiCharacter.characterAnimationManager.PlayTargetAnimation("Block_Start", true);
    }

    public void HandleStrafingAroundTarget()
    {
        if (currentTarget == null) return;

        if (aiIntelligenceLevel >= 7) CalculateFlankingDirection();
        else
        {
            if (Time.time > nextStrafeChangeTime)
            {
                strafeDirection = Random.value > 0.5f ? 1 : -1;
                nextStrafeChangeTime = Time.time + Random.Range(2f, strafeDirectionChangeTime);
                DebugLog($"포위 방향 변경: {(strafeDirection == 1 ? "우측" : "좌측")} 맴돌기");
            }
        }

        Vector3 targetDirection = currentTarget.transform.position - transform.position;
        targetDirection.y = 0;
        targetDirection.Normalize();

        Vector3 crossDirection = Vector3.Cross(targetDirection, Vector3.up).normalized;
        Vector3 strafeVector = crossDirection * strafeDirection;

        Vector3 targetDestination = transform.position + (strafeVector * strafeSpeed);
        aiCharacter.navMeshAgent.SetDestination(targetDestination);

        aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(strafeDirection * 0.5f, 0, false);
        Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
    }

    private void CalculateFlankingDirection()
    {
        Collider[] colliders = Physics.OverlapSphere(transform.position, 10f, WorldUtilityManager.Instance.GetCharacterLayers());
        AICharacterManager nearestAlly = null;
        float closestDistance = Mathf.Infinity;

        foreach (var collider in colliders)
        {
            AICharacterManager ally = collider.GetComponent<AICharacterManager>();
            if (ally != null && ally != aiCharacter && ally.characterGroup == aiCharacter.characterGroup)
            {
                if (ally.aiCharacterCombatManager.currentTarget == currentTarget)
                {
                    float distance = Vector3.Distance(transform.position, ally.transform.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        nearestAlly = ally;
                    }
                }
            }
        }

        if (nearestAlly != null)
        {
            Vector3 targetDirection = (currentTarget.transform.position - transform.position).normalized;
            Vector3 rightDir = Vector3.Cross(targetDirection, Vector3.up).normalized;
            Vector3 dirToAlly = (nearestAlly.transform.position - transform.position).normalized;
            float dot = Vector3.Dot(rightDir, dirToAlly);

            int oldDirection = strafeDirection;
            strafeDirection = dot > 0 ? -1 : 1;

            if (oldDirection != strafeDirection)
                DebugLog("전술적 행동: 아군과 겹치지 않게 포위망을 폅니다.");
        }
        else
        {
            if (Time.time > nextStrafeChangeTime)
            {
                strafeDirection = Random.value > 0.5f ? 1 : -1;
                nextStrafeChangeTime = Time.time + Random.Range(2f, strafeDirectionChangeTime);
            }
        }
    }

    public bool IsAllyAttackingTarget()
    {
        if (currentTarget == null) return false;
        Collider[] colliders = Physics.OverlapSphere(transform.position, 10f, WorldUtilityManager.Instance.GetCharacterLayers());
        foreach (var collider in colliders)
        {
            AICharacterManager ally = collider.GetComponent<AICharacterManager>();
            if (ally != null && ally != aiCharacter && ally.characterGroup == aiCharacter.characterGroup)
            {
                if (ally.aiCharacterCombatManager.currentTarget == currentTarget && ally.isPerformingAction) return true;
            }
        }
        return false;
    }

    public AICharacterManager FindNearestPeacefulAlly()
    {
        Collider[] colliders = Physics.OverlapSphere(transform.position, callForHelpRadius, WorldUtilityManager.Instance.GetCharacterLayers());
        AICharacterManager nearestAlly = null;
        float closestDistance = Mathf.Infinity;

        foreach (var collider in colliders)
        {
            AICharacterManager ally = collider.GetComponent<AICharacterManager>();
            if (ally != null && ally != aiCharacter && ally.characterGroup == aiCharacter.characterGroup)
            {
                if (ally.aiCharacterCombatManager.currentTarget == null)
                {
                    float distance = Vector3.Distance(transform.position, ally.transform.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        nearestAlly = ally;
                    }
                }
            }
        }
        return nearestAlly;
    }

    public Vector3 GetWanderLocation()
    {
        Vector3 targetLocation = transform.position;
        if (Random.Range(0f, 100f) < groupUpChance)
        {
            AICharacterManager randomAlly = FindNearestPeacefulAlly();
            if (randomAlly != null) targetLocation = randomAlly.transform.position;
        }

        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius;
        randomDirection += targetLocation;

        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(randomDirection, out hit, wanderRadius, 1)) return hit.position;
        return transform.position;
    }
}