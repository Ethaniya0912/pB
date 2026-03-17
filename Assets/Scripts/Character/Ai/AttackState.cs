using System.Collections.Generic;
using UnityEngine;
using TDA.Character; // AttackType을 인식하기 위해 네임스페이스 추가
using TDA.Character.AI;

[System.Serializable]
public class AIAttackAction
{
    [Header("New Architecture (P0-03)")]
    [Tooltip("최신 Funnel 패턴용 ActionState ID입니다. 0이 아니면 이 값을 최우선으로 사용하여 애니메이션을 재생합니다.")]
    public int actionStateID = 0;

    [Header("Legacy Architecture")]
    public string attackAnimation;
    public AttackType attackType;

    [Header("Attack Settings")]
    public float minimumDistance = 0f;
    public float maximumDistance = 2.5f;
    public float weight = 10f;
    public float attackCooldown = 2f;
    public bool isComboAttack = false;
    public bool isChargeAttack = false;
}

[CreateAssetMenu(menuName = "AI/States/Attack")]
public class AttackState : AIState
{
    [Header("Next State")]
    public CombatStanceState combatStanceState;

    [Header("Available Attacks")]
    public AIAttackAction[] aiAttacks;

    public override AIState Tick(AICharacterManager aiCharacter)
    {
        if (aiCharacter.isPerformingAction)
            return SwitchState(aiCharacter, combatStanceState);

        if (aiCharacter.aiCharacterCombatManager.currentTarget == null)
            return SwitchState(aiCharacter, combatStanceState);

        Vector3 direction = aiCharacter.aiCharacterCombatManager.currentTarget.transform.position - aiCharacter.transform.position;
        direction.y = 0;
        direction.Normalize();
        if (direction == Vector3.zero) direction = aiCharacter.transform.forward;
        aiCharacter.transform.rotation = Quaternion.LookRotation(direction);

        AIAttackAction chosenAttack = SelectAttack(aiCharacter);

        if (chosenAttack != null)
        {
            aiCharacter.aiCharacterCombatManager.DebugLog($"⚔️ 공격 실행: {(chosenAttack.actionStateID != 0 ? "ActionID " + chosenAttack.actionStateID : chosenAttack.attackAnimation)}");

            aiCharacter.aiCharacterCombatManager.nextAttackTime = Time.time + chosenAttack.attackCooldown;
            if (chosenAttack.isChargeAttack) aiCharacter.characterNetworkManager.isChargingAttack.Value = true;

            // [최신 구조 동기화] ActionState 기반의 Funnel 패턴을 우선 사용하고, 0일 경우 기존 Legacy(문자열 Hash) 패턴을 사용합니다.
            if (chosenAttack.actionStateID != 0)
            {
                aiCharacter.characterCombatManager.currentAttackType = chosenAttack.attackType; // 데미지 연산용 타입 명시
                aiCharacter.characterAnimationManager.PlayTargetActionFunnel(chosenAttack.actionStateID, true, true, false, false);
            }
            else
            {
                aiCharacter.characterAnimationManager.PlayTargetAttackActionAnimation(
                    chosenAttack.attackType,
                    Animator.StringToHash(chosenAttack.attackAnimation),
                    true, true, false, false
                );
            }
        }
        else
        {
            aiCharacter.aiCharacterCombatManager.DebugLog("⚠️ 사용 가능한 공격을 찾지 못했습니다.");
        }

        return SwitchState(aiCharacter, combatStanceState);
    }

    private AIAttackAction SelectAttack(AICharacterManager aiCharacter)
    {
        float distanceFromTarget = Vector3.Distance(aiCharacter.transform.position, aiCharacter.aiCharacterCombatManager.currentTarget.transform.position);
        List<AIAttackAction> possibleAttacks = new List<AIAttackAction>();
        float totalWeight = 0;

        foreach (var attack in aiAttacks)
        {
            if (distanceFromTarget >= attack.minimumDistance && distanceFromTarget <= attack.maximumDistance)
            {
                possibleAttacks.Add(attack);
                totalWeight += attack.weight;
            }
        }

        if (possibleAttacks.Count == 0) return null;

        float randomValue = Random.Range(0, totalWeight);
        float currentWeight = 0;

        foreach (var attack in possibleAttacks)
        {
            currentWeight += attack.weight;
            if (randomValue <= currentWeight) return attack;
        }

        return possibleAttacks[0];
    }
}