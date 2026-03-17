using UnityEngine;
using TDA.Character;

namespace TDA.AnimatorBehaviours
{
    public class ResetCharacterStateBehaviour : StateMachineBehaviour
    {
        override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // 🚨 [치명적 버그 방어] 하단 레이어(Base)에서 턴(Turn) 애니메이션이 재생/전환 중이라면,
            // 루트 모션이 꺼져서 찔끔 돌고 멈추는 스케이팅 버그를 막기 위해 초기화를 강제로 무시합니다!
            AnimatorStateInfo baseState = animator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo nextBaseState = animator.GetNextAnimatorStateInfo(0);

            if (baseState.IsName("Turn_Left_90") || baseState.IsName("Turn_Right_90") ||
                baseState.IsName("Turn_Left_180") || baseState.IsName("Turn_Right_180") ||
                nextBaseState.IsName("Turn_Left_90") || nextBaseState.IsName("Turn_Right_90") ||
                nextBaseState.IsName("Turn_Left_180") || nextBaseState.IsName("Turn_Right_180"))
            {
                return; // 턴 중에는 아무것도 건드리지 않고 즉시 빠져나감
            }

            CharacterManager character = animator.GetComponentInParent<CharacterManager>();
            CharacterEventManager eventManager = animator.GetComponentInParent<CharacterEventManager>();

            if (character != null)
            {
                character.isPerformingAction = false;
                character.animator.applyRootMotion = false; // Locomotion 스크립트에게 주도권 반환
                character.canRotate = true;
                character.canMove = true;

                if (character.characterLocomotionManager != null)
                {
                    character.characterLocomotionManager.isRolling = false;
                }

                character.animator.SetInteger("ActionState", 0);
                character.animator.SetFloat("turnAngle", 0f);
            }

            if (eventManager != null)
            {
                eventManager.NotifyAnimationEvent(global::AnimationEventType.Action_Ended);
            }
        }
    }
}