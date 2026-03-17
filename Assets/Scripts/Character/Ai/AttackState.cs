using System.Collections.Generic;
using UnityEngine;
using TDA.Character;
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

    public float GetMaximumAttackRange()
    {
        float maxRange = 0f;
        if (aiAttacks != null)
        {
            foreach (var attack in aiAttacks)
            {
                if (attack.maximumDistance > maxRange)
                {
                    maxRange = attack.maximumDistance;
                }
            }
        }
        return maxRange;
    }

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
            aiCharacter.aiCharacterCombatManager.nextAttackTime = Time.time + chosenAttack.attackCooldown;
            if (chosenAttack.isChargeAttack) aiCharacter.characterNetworkManager.isChargingAttack.Value = true;

            // =========================================================================================
            // 🚨 [심층 디버깅] 애니메이터가 명령을 무시하는지 추적합니다.
            // =========================================================================================
            if (chosenAttack.actionStateID != 0)
            {
                aiCharacter.characterCombatManager.currentAttackType = chosenAttack.attackType;
                Debug.Log($"<color=orange>[Attack Trigger]</color> {aiCharacter.gameObject.name} ➔ ActionState 파라미터를 <color=white>{chosenAttack.actionStateID}</color>(으)로 변경하고 onAction 방아쇠를 당깁니다!");

                aiCharacter.characterAnimationManager.PlayTargetActionFunnel(chosenAttack.actionStateID, true, true, false, false);
            }
            else
            {
                Debug.Log($"<color=orange>[Attack Trigger]</color> {aiCharacter.gameObject.name} ➔ 레거시 모션 <color=white>'{chosenAttack.attackAnimation}'</color> CrossFade 재생을 시도합니다.");

                aiCharacter.characterAnimationManager.PlayTargetAttackActionAnimation(
                    chosenAttack.attackType,
                    Animator.StringToHash(chosenAttack.attackAnimation),
                    true, true, false, false
                );
            }

            // 방아쇠를 당긴 직후 상태 체크 (여기서 true가 안 나오면 AnimationManager가 고장난 것입니다)
            Debug.Log($"<color=yellow>[State Check]</color> 방아쇠를 당긴 직후 isPerformingAction 상태: <color=lime>{aiCharacter.isPerformingAction}</color>");
        }
        else
        {
            aiCharacter.aiCharacterCombatManager.DebugLog("⚠️ 사거리 내에 사용 가능한 공격 패턴(AIAttackAction)이 없습니다!");
        }

        // 🚨 이 FSM 설계에서는 Attack State가 1프레임만에 끝나고 CombatStance로 돌아가는 것이 100% '정상'입니다.
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