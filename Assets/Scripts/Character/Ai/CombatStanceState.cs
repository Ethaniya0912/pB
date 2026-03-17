using TDA.Character;
using TDA.Character.AI;
using UnityEngine;

[CreateAssetMenu(menuName = "AI/States/Combat Stance")]
public class CombatStanceState : AIState
{
    [Header("Next States")]
    public AttackState attackState;
    public PursueTargetState pursueTargetState;
    public FleeState fleeState;
    public RetreatState retreatState;

    [Header("Combat Settings")]
    public float attackRange = 2.5f;
    public int fleeHealthThreshold = 20;
    public float retreatStaminaThreshold = 10f;

    // =========================================================================================
    // [신규 추가] 방어 시스템 (P0-02) 설정
    // =========================================================================================
    [Header("Defense Settings (P0-02)")]
    [Tooltip("이 상태에서 AI가 방패를 들고 방어를 시도할지 여부입니다.")]
    public bool canGuardInStance = true;
    [Tooltip("플레이어가 액션(공격 등)을 취할 때 가드를 올릴 확률입니다.")]
    [Range(0, 100)] public float guardReactionChance = 50f;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        if (aiCharacter.aiCharacterCombatManager.currentTarget == null)
        {
            aiCharacter.aiCharacterCombatManager.DebugLog("타겟을 잃어버림 -> Pursue 상태로 전환");
            return SwitchState(aiCharacter, pursueTargetState);
        }

        if (aiCharacter.characterNetworkManager.currentHealth.Value <= fleeHealthThreshold)
        {
            aiCharacter.aiCharacterCombatManager.DebugLog("체력 부족! -> Flee 상태로 전환");
            return SwitchState(aiCharacter, fleeState);
        }

        aiCharacter.aiCharacterCombatManager.HandleDefensiveActions();

        if (aiCharacter.isPerformingAction)
        {
            // 🚨 [치명적 버그 수정] 동굴(CaveManager)이 실시간 생성될 때, 
            // 몬스터가 NavMesh 밖(공중)에 있는데 ResetPath를 부르면 에러가 터지는 현상을 완벽 방어합니다.
            if (aiCharacter.navMeshAgent != null && aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
            {
                aiCharacter.navMeshAgent.ResetPath();
            }

            aiCharacter.characterAnimationManager.UpdateAnimatorMovementParameters(0, 0, false);
            return this;
        }

        if (aiCharacter.characterNetworkManager.currentStamina.Value <= retreatStaminaThreshold)
        {
            aiCharacter.aiCharacterCombatManager.DebugLog("스테미나 부족 -> Retreat 상태로 전환");
            return SwitchState(aiCharacter, retreatState);
        }

        float distanceFromTarget = Vector3.Distance(aiCharacter.transform.position, aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);

        if (distanceFromTarget > attackRange)
        {
            aiCharacter.aiCharacterCombatManager.DebugLog($"타겟이 공격 범위를 벗어남 (거리: {distanceFromTarget:F1}) -> Pursue 상태로 전환");
            return SwitchState(aiCharacter, pursueTargetState);
        }

        // =========================================================================================
        // 🛡️ [P0-02] AI 지능형 방어 로직 (AIDefenseManager 연동)
        // =========================================================================================
        if (canGuardInStance && aiCharacter.characterDefenseManager != null && aiCharacter.characterDefenseManager.currentDefendingItem != null)
        {
            CharacterManager target = aiCharacter.aiCharacterCombatManager.currentTarget;

            // 1. 타겟이 공격(isPerformingAction) 중이고, 내 가드가 내려가 있다면 확률적으로 가드 올리기
            if (target != null && target.isPerformingAction)
            {
                if (!aiCharacter.characterDefenseManager.isDefending)
                {
                    // 초당 확률로 보정하여 프레임률에 관계없이 일정한 확률로 방어 시도
                    if (Random.value < (guardReactionChance / 100f) * Time.deltaTime * 5f)
                    {
                        // 상단(Top) 기준 방어 시작
                        aiCharacter.characterDefenseManager.StartDefense(GuardDirection.Top);
                        aiCharacter.aiCharacterCombatManager.DebugLog("적의 공격 움직임 감지 -> 가드 올림!");
                    }
                }
            }
            // 2. 타겟이 멈췄거나, 내가 곧 공격할 타이밍이 되었다면 가드 내리기
            else
            {
                if (aiCharacter.characterDefenseManager.isDefending)
                {
                    // 공격 쿨타임이 거의 다 찼거나(0.5초 전), 적과 거리가 멀어지면 방패를 내림
                    if (Time.time >= aiCharacter.aiCharacterCombatManager.nextAttackTime - 0.5f || distanceFromTarget > attackRange)
                    {
                        aiCharacter.characterDefenseManager.StopDefense();
                        aiCharacter.aiCharacterCombatManager.DebugLog("공격 준비 완료 또는 거리 벌어짐 -> 가드 내림");
                    }
                }
            }
        }
        // =========================================================================================

        aiCharacter.aiCharacterCombatManager.HandleStrafingAroundTarget();

        if (!aiCharacter.isPerformingAction)
        {
            if (Time.time >= aiCharacter.aiCharacterCombatManager.nextAttackTime)
            {
                if (aiCharacter.aiCharacterCombatManager.IsAllyAttackingTarget())
                {
                    aiCharacter.aiCharacterCombatManager.DebugLog("아군이 공격 중이므로 대기합니다.");
                    aiCharacter.aiCharacterCombatManager.nextAttackTime = Time.time + Random.Range(1f, 2f);
                    return this;
                }

                aiCharacter.aiCharacterCombatManager.DebugLog("공격 쿨타임 완료 -> Attack 상태로 전환");
                return SwitchState(aiCharacter, attackState);
            }
        }

        return this;
    }

    protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        base.ResetStateFlags(aiCharacterManager);

        // =========================================================================================
        // [방어 시스템 P0-02 연동] 상태를 빠져나갈 때 가드를 강제로 해제하여 다음 행동(공격 등)을 방해하지 않게 합니다.
        // =========================================================================================
        if (aiCharacterManager.characterDefenseManager != null && aiCharacterManager.characterDefenseManager.isDefending)
        {
            aiCharacterManager.characterDefenseManager.StopDefense();
        }

        // [안전성 강화] 상태 전이 시 기존 목표 지점을 확실히 지워줍니다.
        if (aiCharacterManager.navMeshAgent != null && aiCharacterManager.navMeshAgent.isActiveAndEnabled && aiCharacterManager.navMeshAgent.isOnNavMesh)
        {
            aiCharacterManager.navMeshAgent.ResetPath();
        }
    }
}