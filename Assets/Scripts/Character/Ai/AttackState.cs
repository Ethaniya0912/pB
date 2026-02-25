using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class AIAttackAction
{
    public string attackAnimation;
    public AttackType attackType;
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
            return SwitchState(aiCharacter, combatStanceState); // 수정됨: SwitchState 사용

        if (aiCharacter.aiCharacterCombatManager.currentTarget == null)
            return SwitchState(aiCharacter, combatStanceState); // 수정됨: SwitchState 사용

        Vector3 direction = aiCharacter.aiCharacterCombatManager.currentTarget.transform.position - aiCharacter.transform.position;
        direction.y = 0;
        direction.Normalize();
        if (direction == Vector3.zero) direction = aiCharacter.transform.forward;
        aiCharacter.transform.rotation = Quaternion.LookRotation(direction);

        AIAttackAction chosenAttack = SelectAttack(aiCharacter);

        if (chosenAttack != null)
        {
            aiCharacter.aiCharacterCombatManager.nextAttackTime = Time.time + chosenAttack.attackCooldown;
            if (chosenAttack.isChargeAttack) aiCharacter.characterNetworkManager.isChargingAttack.Value = true;

            aiCharacter.characterAnimationManager.PlayTargetAttackActionAnimation(
                chosenAttack.attackType,
                chosenAttack.attackAnimation,
                true, true, false, false
            );
        }

        return SwitchState(aiCharacter, combatStanceState); // 수정됨: SwitchState 사용
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