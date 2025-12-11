using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PursueTargetState : AIState
{
    public override AIState Tick(AICharacterManager aICharacter)
    {
        return base.Tick(aICharacter);

        // 우리가 액션을 퍼폼하는지 체크 (만약 그렇다면 액션 종료까지 뭘 하지 말것)
        
        // 타겟이 null 상태인지 체크, 타겟이 없다면 idle 상태로 돌아감.

        // 네브메쉬 에이전트가 활성화되어있는지 체크하고, 아니라면 활성화.

        // 범위 내 존재한다면 컴뱃 스테이트로 교체.

        // 만약 타겟에 도달할 수 없다면, 그리고 멀다면, 위치로 돌아감.

        // 타겟을 pursue 하라.

    }
}
