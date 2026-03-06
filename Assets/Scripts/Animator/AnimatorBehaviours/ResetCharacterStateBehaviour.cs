using UnityEngine;
using TDA.Character;

namespace TDA.AnimatorBehaviours
{
    public class ResetCharacterStateBehaviour : StateMachineBehaviour
    {
        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            CharacterManager character = animator.GetComponentInParent<CharacterManager>();
            CharacterEventManager eventManager = animator.GetComponentInParent<CharacterEventManager>();

            if (character != null)
            {
                character.isPerformingAction = false;
                character.applyRootMotion = false;
                character.canRotate = true;
                character.canMove = true;

                if (character.characterLocomotionManager != null)
                {
                    character.characterLocomotionManager.isRolling = false;
                }
            }

            if (eventManager != null)
            {
                // [수정됨] int 해시가 아닌 글로벌 Enum을 송출하도록 변경
                eventManager.NotifyAnimationEvent(global::AnimationEventType.Action_Ended);
            }
        }
    }
}