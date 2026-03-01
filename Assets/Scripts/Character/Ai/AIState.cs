using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AIState : ScriptableObject
{
    public virtual AIState Tick(AICharacterManager aiCharacter)
    {
        // 커스텀 디버그 로그 활용 (자식 클래스 이름 출력)
        aiCharacter.aiCharacterCombatManager.DebugLog($"현재 실행 중인 상태: {this.GetType().Name}");

        // 플레이어를 찾는 로직 구현
        // 플레이어를 찾는다면, pursue target state 를 대신 반환.
        // 그게 아니라면, 계속해서 idle 상태를 반환.
        return this;
    }

    // 스테이트가 바뀔 때마다, 현재 스테이트를 청소(Reset)하고 새로운 스테이트를 반환
    protected virtual AIState SwitchState(AICharacterManager aiCharacter, AIState newState)
    {
        aiCharacter.aiCharacterCombatManager.DebugLog($"상태 전환: <color=yellow>{this.GetType().Name} -> {newState.GetType().Name}</color>");
        ResetStateFlags(aiCharacter); // 현재 스테이트의 찌꺼기를 지워 백지 상태로 만듦
        return newState;
    }

    // 스테이트 플래그를 리셋해서 스테이트로 복귀 시 다시 백지 상태가 되게 해줌
    protected virtual void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        // 기본적으로 네비메시 에이전트의 경로를 초기화하여 이전 상태의 이동 명령이 남지 않도록 함
        if (aiCharacterManager.navMeshAgent != null && aiCharacterManager.navMeshAgent.enabled)
        {
            aiCharacterManager.navMeshAgent.ResetPath();
        }
    }
}