using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerCombatManager : CharacterCombatManager
{
    PlayerManager player;

    public WeaponItem currentWeaponBeingUsed;

    [Header("Flags")]
    public bool canComboWithMainHandWeapon = false;

    protected override void Awake()
    {
        base.Awake();

        player = GetComponent<PlayerManager>();
    }

    public void PerformWeaponBasedAction(WeaponItemAction weaponAction, WeaponItem weaponPerformingAction)
    {
        if (!player.IsOwner)
            return;

        if (player.IsOwner)
        {
            // 액션 수행하기.
            weaponAction.AttemptToPerformAction(player, weaponPerformingAction);

            // 수행한 액션을 서버에 알리고, 그 후 서버가 다른 클라이언트에게 수행한 액션을 보여줌
            player.playerNetworkManager.NotifyTheServerOfWeaponActionServerRpc
                (NetworkManager.Singleton.LocalClientId, weaponAction.actionID,
                weaponPerformingAction.itemID);
        }
    }

    public virtual  void DrainStaminaBasedOnAttack()
    {
        if (!player.IsOwner)
            return;

        if (currentWeaponBeingUsed == null) 
            return;

        float staminaDeducted = 0;

        switch (currentAttackType)
        {
            case AttackType.LightAttack01:
                staminaDeducted = currentWeaponBeingUsed.baseStaminaCost * currentWeaponBeingUsed.lightAttackStaminaCostMultiplier;
                break;
            default:
                break;
        }

        player.playerNetworkManager.currentStamina.Value -= Mathf.RoundToInt(staminaDeducted);
    }

    public override void SetTarget(CharacterManager newTarget)
    {
        base.SetTarget(newTarget);

        // 로컬플레이어가 하고 잇다면
        if (player.IsOwner)
        {
            PlayerCamera.Instance.SetLockCameraHeight();
        }
    }


}
