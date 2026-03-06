using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.Character; // AttackType 및 매니저 참조용
using TDA.Character.Player;

namespace TDA.Items.WeaponItemActions
{
    [CreateAssetMenu(menuName = "Character Actions/Weapon Actions/Light Attack Action")]
    public class LightAttackWeaponItemAction : WeaponItemAction
    {
        [Header("Light Attack Animations")]
        [SerializeField] string light_Attack_01 = "Main_Light_Attack_01";
        [SerializeField] string light_Attack_02 = "Main_Light_Attack_02";

        public override void AttemptToPerformAction(PlayerManager playerPerformingAction, WeaponItem weaponPerformingAction)
        {
            base.AttemptToPerformAction(playerPerformingAction, weaponPerformingAction);

            // 1. 소유권 및 스태미나 기본 검문
            if (!playerPerformingAction.IsOwner) return;
            if (playerPerformingAction.playerNetworkManager.currentStamina.Value <= 0) return;

            // 2. 콤보 공격 판정 (가장 중요)
            // 캐릭터가 현재 액션 중이고, 이벤트 방송국에서 콤보 허용(ComboEnable) 신호를 보내 플래그가 true가 되었을 때
            if (playerPerformingAction.isPerformingAction && playerPerformingAction.playerCombatManager.canComboWithMainHandWeapon)
            {
                // 콤보를 소모하므로 다시 창을 닫아줍니다. (다단 히트 방지)
                playerPerformingAction.playerCombatManager.canComboWithMainHandWeapon = false;
                PerformLightAttackCombo(playerPerformingAction, weaponPerformingAction);
            }
            // 3. 일반 첫 타격 판정
            // 아무 행동도 하고 있지 않을 때 RB를 누르면 1타가 나갑니다.
            else if (!playerPerformingAction.isPerformingAction)
            {
                PerformLightAttack(playerPerformingAction, weaponPerformingAction);
            }
        }

        private void PerformLightAttack(PlayerManager playerPerformingAction, WeaponItem weaponPerformingAction)
        {
            // 주무기(오른손)를 사용 중일 때 1타 애니메이션 실행
            if (playerPerformingAction.playerNetworkManager.isUsingRightHand.Value)
            {
                playerPerformingAction.playerAnimationManager.PlayTargetAttackActionAnimation(
                    global::AttackType.LightAttack01,
                    Animator.StringToHash(light_Attack_01),
                    true
                );

                // [버그 수정] 애니메이션 재생을 명령한 직후 스태미나 차감 함수를 반드시 찔러줍니다.
                playerPerformingAction.playerCombatManager.DrainStaminaBasedOnAttack();
            }
            // 보조무기(왼손) 로직이 필요하다면 여기에 추가
            else if (playerPerformingAction.playerNetworkManager.isUsingLeftHand.Value)
            {

            }
        }

        private void PerformLightAttackCombo(PlayerManager playerPerformingAction, WeaponItem weaponPerformingAction)
        {
            if (playerPerformingAction.playerNetworkManager.isUsingRightHand.Value)
            {
                // 기존의 문자열(String) 비교를 폐기하고, Animator.StringToHash()를 활용한 해시(Int) 비교로 콤보를 전이합니다.
                // 직전에 실행된 애니메이션 해시가 "1타" 였다면 -> "2타"를 실행하라!
                if (playerPerformingAction.characterCombatManager.lastAttackAnimationPerformedHash == Animator.StringToHash(light_Attack_01))
                {
                    playerPerformingAction.playerAnimationManager.PlayTargetAttackActionAnimation(
                        global::AttackType.LightAttack02,
                        Animator.StringToHash(light_Attack_02),
                        true
                    );

                    // [버그 수정] 콤보 발동 시에도 스태미나를 차감하도록 함수를 호출합니다.
                    playerPerformingAction.playerCombatManager.DrainStaminaBasedOnAttack();
                }
            }
        }
    }
}