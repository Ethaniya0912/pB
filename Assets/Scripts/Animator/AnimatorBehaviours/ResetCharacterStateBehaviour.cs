using UnityEngine;
using TDA.Character;
using TDA.Core.Events; // 해시 시스템 연동

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
            // =========================================================================
            // 🚨 [진짜 근본 원인 해결: 궁극의 ActionState 방어막]
            // 유니티에서 Trigger 파라미터(onAction)는 GetBool()로 읽으면 항상 false를 반환합니다.
            // 대신, 방아쇠를 당길 때 같이 들어가는 'ActionState(정수)' 파라미터를 감시하여,
            // 이 값이 0이 아니라면 액션이 예약/진행 중인 것으로 간주하고 하체(Base) 레이어의 유령 리셋을 원천 차단합니다!
            // =========================================================================
            if (layerIndex == 0) // 하체(Base Layer)가 상태를 변경하려 할 때만 검사
            {
                if (animator.GetInteger(AnimatorParameterHash.ActionState) != 0)
                {
                    return; // 공격/회피가 진행 중이므로 하체의 강제 리셋을 무시하고 튕겨냅니다!
                }
            }

            // 1. 기존의 Turn 애니메이션 무한 루프 방어 로직
            AnimatorStateInfo baseState = animator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo nextBaseState = animator.GetNextAnimatorStateInfo(0);

            if (baseState.IsName("Turn_Left_90") || baseState.IsName("Turn_Right_90") ||
                baseState.IsName("Turn_Left_180") || baseState.IsName("Turn_Right_180") ||
                nextBaseState.IsName("Turn_Left_90") || nextBaseState.IsName("Turn_Right_90") ||
                nextBaseState.IsName("Turn_Left_180") || nextBaseState.IsName("Turn_Right_180"))
            {
                return;
            }

            // 2. 상위 레이어 활성 상태 정밀 검사 (이중 안전망)
            if (layerIndex == 0)
            {
                bool isUpperLayerActive = false;

                for (int i = 1; i < animator.layerCount; i++)
                {
                    AnimatorStateInfo upperState = animator.GetCurrentAnimatorStateInfo(i);
                    AnimatorStateInfo nextUpperState = animator.GetNextAnimatorStateInfo(i);

                    bool isStateEmpty = upperState.IsName("Empty State") || upperState.IsName("Empty") || upperState.IsName("Upperbody_Empty")
                                        || upperState.IsName("Stance_Hold_Left") || upperState.IsName("Stance_Hold_Right");
                    bool isNextStateEmpty = nextUpperState.fullPathHash == 0 || nextUpperState.IsName("Empty State") || nextUpperState.IsName("Empty") || nextUpperState.IsName("Upperbody_Empty")
                                        || nextUpperState.IsName("Stance_Hold_Left") || nextUpperState.IsName("Stance_Hold_Right");

                    if (!isStateEmpty || !isNextStateEmpty)
                    {
                        isUpperLayerActive = true;
                        break;
                    }
                }

                if (isUpperLayerActive)
                {
                    return;
                }
            }

            CharacterManager character = animator.GetComponentInParent<CharacterManager>();
            CharacterEventManager eventManager = animator.GetComponentInParent<CharacterEventManager>();

            // 3. AI 몬스터 굳음(Freeze) 방지
            if (character != null && !(character is TDA.Character.Player.PlayerManager))
            {
                if (layerIndex > 0 && (stateInfo.IsName("Stance_Hold_Left") || stateInfo.IsName("Stance_Hold_Right")))
                {
                    animator.CrossFade("Empty", 0.1f, layerIndex);
                }
            }

            bool wasPerformingAction = false;

            if (character != null)
            {
                wasPerformingAction = character.isPerformingAction;

                // 유령 액션 상태 해제 및 물리 락 해제
                character.isPerformingAction = false;
                character.animator.applyRootMotion = false; // Locomotion 스크립트에게 주도권 반환
                character.canRotate = true;
                character.canMove = true;

                if (character.characterLocomotionManager != null)
                {
                    character.characterLocomotionManager.isRolling = false;
                }

                // 다음 액션을 위해 해시(Hash) 시스템을 사용하여 파라미터 깔끔하게 초기화
                character.animator.SetInteger(AnimatorParameterHash.ActionState, 0);
                character.animator.SetFloat(AnimatorParameterHash.turnAngle, 0f);
            }

            // 로그 스팸을 막고 액션이 끝났을 때만 이벤트를 날림
            if (eventManager != null && wasPerformingAction)
            {
                eventManager.NotifyAnimationEvent(global::AnimationEventType.Action_Ended, "ResetCharacterStateBehaviour");
            }
        }
    }
}