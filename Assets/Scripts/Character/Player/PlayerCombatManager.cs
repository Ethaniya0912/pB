using System;
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

    [Header("Lock On Input")]
    [SerializeField] bool lockOn_Input;
    [SerializeField] bool lockOn_Left_Input;
    [SerializeField] bool lockOn_Right_Input;
    private Coroutine lockOnCoroutine;

    protected override void Awake()
    {
        base.Awake();

        player = GetComponent<PlayerManager>();
    }

    internal void OnRBInputReceived()
    {
        PerformWeaponBasedAction
        (player.playerInventoryManager.currentRightHandWeapon.oh_RB_Action,
        player.playerInventoryManager.currentRightHandWeapon
        );
    }

    internal void OnLockOnInputReceived()
    {
        // 타겟이 죽었는지 체크
        if (player.playerNetworkManager.isLockedOn.Value)
        {
            // 상대가 파괴되어 null 일 것 대비
            if (currentTarget == null)
                return;

            // 현재 타겟이 사망? (언락)
            if (currentTarget.characterNetworkManager.isDead.Value)
            {
                player.playerNetworkManager.isLockedOn.Value = false;
            }

            // 새로운 타겟을 찾기.

            // 코루틴이 동시에 여러개 진행 중첩이 되지 않도록 보장.
            if (lockOnCoroutine != null)
                StopCoroutine(lockOnCoroutine);

            lockOnCoroutine = StartCoroutine(player.playerCamera.WaitThenFindNewTarget());

        }

        if (player.playerNetworkManager.isLockedOn.Value)
        {
            Debug.Log($"player.playerNetworkManager.isLockedOn.Value");
            player.playerCamera.ClearLockOnTargets();
            player.playerNetworkManager.isLockedOn.Value = false;
            // 이미 락온? (언락)
            return;
        }

        if (!player.playerNetworkManager.isLockedOn.Value)
        {
            Debug.Log($"lockOn_Input && !player.playerNetworkManager.isLockedOn.Value");
            // 락온

            // 레인지 무기 사용중이라면 락온 안함.

            player.playerCamera.HandleLocatingLockOnTargets();

            if (player.playerCamera.nearestLockOnTarget != null)
            {
                SetTarget(player.playerCamera.nearestLockOnTarget);
                // 가장 가까운 타겟이 널이 아니면 현재 대상으로 락온
                player.playerNetworkManager.isLockedOn.Value = true;
            }
        }
    }

    internal void OnLockOnSwitchTargetInputReceived(LockOnDirection direction)
    {
        if (direction == LockOnDirection.Left)
        {

            // 이미 락온 됫다면 실행.
            if (player.playerNetworkManager.isLockedOn.Value)
            {
                player.playerCamera.HandleLocatingLockOnTargets();

                if (player.playerCamera.leftLockOnTarget != null)
                {
                    SetTarget(player.playerCamera.leftLockOnTarget);
                }
            }
        }

        if (direction == LockOnDirection.Right)
        {
            // 이미 락온 됫다면 실행.
            if (player.playerNetworkManager.isLockedOn.Value)
            {
                player.playerCamera.HandleLocatingLockOnTargets();

                if (player.playerCamera.rightLockOnTarget != null)
                {
                    SetTarget(player.playerCamera.rightLockOnTarget);
                }
            }
        }
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
            player.playerCamera.SetLockCameraHeight();
        }
    }

    internal void OnRTInputReceived()
    {
        PerformWeaponBasedAction
        (player.playerInventoryManager.currentRightHandWeapon.oh_RT_Action,
        player.playerInventoryManager.currentRightHandWeapon
        );
    }

}
