using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Chacter Actions/Weapon Actions/Light Attack Action")]
public class LightAttackWeaponItemAction : WeaponItemAction
{
    [SerializeField] string light_Attack_01 = "Main_Light_Attack_01"; // 메인=주손,오른손
    [SerializeField] string light_Attack_02 = "Main_Light_Attack_02"; // 메인=주손,오른손
    public override void AttemptToPerformAction(PlayerManager playerPerformingAction, WeaponItem weaponPerformingAction)
    {
        base.AttemptToPerformAction(playerPerformingAction, weaponPerformingAction);

        // 중단할 요소 체크.
        if (!playerPerformingAction.IsOwner)
            return;

        if (playerPerformingAction.playerNetworkManager.currentStamina.Value <= 0 ) 
            return;

        //if (!playerPerformingAction.isGrounded)
        //    return;

        PerformLightAttack(playerPerformingAction, weaponPerformingAction);
    }

    private void PerformLightAttack(PlayerManager playerPerformingAction, WeaponItem weaponPerformingAction)
    {
        // 우리가 현재 공격하고 있다면, 그리고 콤보중이라면, 콤보 공격 퍼폼.
        if (playerPerformingAction.playerCombatManager.canComboWithMainHandWeapon && playerPerformingAction.isPerformingAction)
        {
            playerPerformingAction.playerCombatManager.canComboWithMainHandWeapon = false;

            // 이전 공격에 따른 공격을 수행.
            if (playerPerformingAction.characterCombatManager.lastAttackAnimationPerformed == light_Attack_01)
            {
                playerPerformingAction.playerAnimationManager.PlayTargetAttackActionAnimation(AttackType.LightAttack02, light_Attack_02, true);
            }
            else
            {
                playerPerformingAction.playerAnimationManager.PlayTargetAttackActionAnimation(AttackType.LightAttack01, light_Attack_01, true);
            }
        }
        // 아니라면, 일반 공격 수행. isPerformingAction을 써 롤등 다른 액션이 수행되지않게 해주자.
        else if (!playerPerformingAction.isPerformingAction)
        {
            playerPerformingAction.playerAnimationManager.PlayTargetAttackActionAnimation(AttackType.LightAttack01, light_Attack_01, true);
        }
    }
}
