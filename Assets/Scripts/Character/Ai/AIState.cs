using TDA.Character.AI;
using UnityEngine;

/// <summary>
/// [L3 Domain — State Base] 모든 AI 상태의 기반 클래스입니다.
/// 상태(State)는 오직 '판단'만 수행합니다.
/// 물리적 이동(transform.position), Animator 파라미터 직접 조작은 이 계층의 책임이 아닙니다.
/// </summary>
public class AIState : ScriptableObject
{
    public virtual AIState Tick(AICharacterManager aiCharacter)
    {
        aiCharacter.aiCharacterCombatManager.DebugLog($"현재 상태: {this.GetType().Name}");
        return this;
    }

    protected virtual AIState SwitchState(AICharacterManager aiCharacter, AIState newState)
    {
        aiCharacter.aiCharacterCombatManager.DebugLog($"상태 전환: <color=yellow>{this.GetType().Name} → {newState.GetType().Name}</color>");
        ResetStateFlags(aiCharacter);
        return newState;
    }

    /// <summary>
    /// 상태 전환 시 이전 상태의 찌꺼기를 정리합니다.
    /// 하위 State가 Override하여 추가 정리를 수행할 수 있습니다.
    /// </summary>
    protected virtual void ResetStateFlags(AICharacterManager aiCharacterManager)
    {
        // NavMesh 경로 초기화 (안전 조건 확인 후)
        if (aiCharacterManager.navMeshAgent != null
            && aiCharacterManager.navMeshAgent.isActiveAndEnabled
            && aiCharacterManager.navMeshAgent.isOnNavMesh)
        {
            aiCharacterManager.navMeshAgent.ResetPath();
        }

        // [변경] 상태 전환 타이밍에 applyRootMotion을 즉시 비활성화합니다.
        // 다음 프레임에 AICharacterLocomotionManager.Update()가 올바른 값으로 재설정합니다.
        if (aiCharacterManager.animator != null)
        {
            aiCharacterManager.animator.applyRootMotion = false;
        }
    }
}
