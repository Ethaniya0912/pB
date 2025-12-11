using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AIState : ScriptableObject
{
    public virtual AIState Tick(AICharacterManager aiCharacter)
    {
        Debug.Log("We are running this State");

        // 플레이어를 찾는 로직 구현

        // 플레이어를 찾는다면, pursue target state 를 대신 반환.

        // 그게 아니라면, 계속해서 idle 상태를 반환.
        return this;
    }

    // 스테이트가 바뀔때마다, 해당 스테이트에 저장한 정보등을 리셋할 겸 스위치스테이트 활용
    protected virtual AIState SwitchState(AICharacterManager aiCharacter, AIState newState)
    {
        return this;
    }

    protected virtual void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        // 스테이트 플래그를 리셋해서 스테이트로 복귀시 다시 백지 상태가 되게 해줌.

    }
}
