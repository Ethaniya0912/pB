using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI 캐릭터의 전투 전술(탐지, 방어, 맴돌기 등)과 개별 런타임 상태를 관리하는 매니저 클래스입니다.
/// </summary>
public class AICharacterCombatManager : CharacterCombatManager
{
    private AICharacterManager aiCharacter;

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
    // 여러 몬스터가 동일한 State SO를 공유하므로, State 내부에 변수를 두면 값이 꼬이는 버그 발생
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
        aiCharacter.characterAnimationManager.PlayTargetAnimation(evadeAnimation, true);
    }

    private void PerformParry()
    {
        aiCharacter.characterAnimationManager.PlayTargetAnimation("Parry_01", true);
    }

    private void PerformBlock()
    {
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
            strafeDirection = dot > 0 ? -1 : 1;
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