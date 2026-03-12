using UnityEngine;
using TDA.Character;

namespace TDA.AnimatorBehaviours
{
    /// <summary>
    /// [안전망] 애니메이터의 상태(State)에 부착하여, 해당 상태로 진입할 때 
    /// 캐릭터의 모든 액션 제한 플래그를 깔끔하게 초기화(세탁)하는 클래스입니다.
    /// (주로 Idle, Locomotion, Empty State 등 기본 상태 노드에 부착합니다)
    /// </summary>
    public class ResetCharacterStateBehaviour : StateMachineBehaviour
    {
        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            CharacterManager character = animator.GetComponentInParent<CharacterManager>();
            CharacterEventManager eventManager = animator.GetComponentInParent<CharacterEventManager>();

            if (character != null)
            {
                // 1. 유령 액션 상태 해제 및 물리 락 해제
                character.isPerformingAction = false;
                character.animator.applyRootMotion = false; // Locomotion 스크립트에게 주도권 반환
                character.canRotate = true;
                character.canMove = true;

                if (character.characterLocomotionManager != null)
                {
                    character.characterLocomotionManager.isRolling = false;
                }

                // 2. [추가된 치명적 버그 방어망] 파라미터 강제 초기화
                // (이전 액션의 흔적이 남아 무한 루프나 턴 버그가 생기는 것을 방지)
                character.animator.SetInteger("actionIndex", 0);
                character.animator.SetFloat("turnAngle", 0f);
            }

            if (eventManager != null)
            {
                // [P4 규격] int 해시가 아닌 글로벌 Enum을 송출하도록 변경
                eventManager.NotifyAnimationEvent(global::AnimationEventType.Action_Ended);
            }
        }
    }
}