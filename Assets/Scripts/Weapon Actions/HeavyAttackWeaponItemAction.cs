using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.Character;
using TDA.Items.WeaponItemActions;
using TDA.Character.Player;

namespace TDA.Items.WeaponItemActions
{
    [CreateAssetMenu(menuName = "Character Actions/Weapon Actions/Heavy Attack Action")]
    public class HeavyAttackWeaponItemAction : WeaponItemAction
    {
        [SerializeField] string heavy_Attack_01 = "Main_Heavy_Attack_01"; // 메인=주손,오른손

        public override void AttemptToPerformAction(PlayerManager playerPerformingAction, WeaponItem weaponPerformingAction)
        {
            base.AttemptToPerformAction(playerPerformingAction, weaponPerformingAction);

            // 중단할 요소 체크.
            if (!playerPerformingAction.IsOwner)
                return;

            if (playerPerformingAction.playerNetworkManager.currentStamina.Value <= 0)
                return;

            PerformHeavyAttack(playerPerformingAction, weaponPerformingAction);
        }

        private void PerformHeavyAttack(PlayerManager playerPerformingAction, WeaponItem weaponPerformingAction)
        {
            if (playerPerformingAction.playerNetworkManager.isUsingRightHand.Value)
            {
                playerPerformingAction.playerAnimationManager.PlayTargetAttackActionAnimation(
                    global::AttackType.HeavyAttack01,
                    Animator.StringToHash(heavy_Attack_01),
                    true
                );

                // [버그 수정] 강공격 실행 시에도 스태미나가 차감되도록 함수를 호출해줍니다.
                playerPerformingAction.playerCombatManager.DrainStaminaBasedOnAttack();
            }
            if (playerPerformingAction.playerNetworkManager.isUsingLeftHand.Value)
            {

            }
        }
    }
}