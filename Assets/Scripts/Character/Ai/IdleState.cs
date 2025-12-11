using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "A.I/States/Idle")]
public class IdleState : AIState
{
    public override AIState Tick(AICharacterManager aICharacter)
    {
        if (aICharacter.characterCombatManager.currentTarget != null)
        {
            // PurSue Target State 반환
            Debug.Log("We have a target");
            return this;
        }
        else
        {
            // 해당 스테이트를 지속해서 반환, 계속해서 타겟 찾기 (타겟 찾기전까지 지속)
            aICharacter.aiCharacterCombatManager.FindATargetViaLineOfSight(aICharacter);
            Debug.Log("타겟이 없음, 계속해서 수색");
            return this;
        }

    }


}
