using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerManager : CharacterManager
{
    [HideInInspector]public PlayerAnimationManager playerAnimationManager;
    [HideInInspector]public PlayerLocomotionManager playerLocomotionManager;
    [HideInInspector]public PlayerNetworkManager playerNetworkManager;
    [HideInInspector]public PlayerStatsManager playerStatsManager;

    protected override void Awake()
    {
        base.Awake();

        // ĳ���͸Ŵ��� ���� �������̵��Ͽ� �÷��̾�Ư�� ��ɵ� �߰�.

        playerAnimationManager = GetComponent<PlayerAnimationManager>();
        playerLocomotionManager = GetComponent<PlayerLocomotionManager>();
        playerNetworkManager = GetComponent<PlayerNetworkManager>();
        playerStatsManager = GetComponent<PlayerStatsManager>();
    }

    protected override void Update()
    {
        base.Update();

        // Owner�϶��� ������ �� �ֵ��� ����.
        if (!IsOwner)
            return;

        // Handle Movement
        playerLocomotionManager.HandleAllMovement();

        // ���¹̳� ���� �Լ� ������Ʈ
        playerStatsManager.RegenerateStamina();
    }

    protected override void LateUpdate()
    {
        // �÷��̾ �����϶��� �Ҵ�, �ƴ� �� ����.
        if (!IsOwner) return;

        base.LateUpdate();

        PlayerCamera.Instance.HandleAllCameraActions();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Ŭ���̾�Ʈ�� ���� ������ �÷��̾� ������Ʈ�� ��
        if (IsOwner)
        {
            PlayerCamera.Instance.player = this;
            PlayerInputManager.Instance.player = this;
            WorldSaveGameManager.Instance.player = this;

            playerNetworkManager.currentStamina.OnValueChanged +=
                PlayerUIManager.Instance.playerUIHUDManager.SetNewStaminaValue;
            playerNetworkManager.currentStamina.OnValueChanged +=
                playerStatsManager.ResetStaminaRegenTimer;

            // ���� �ڵ�� ���̺�/�ε尡 �߰��Ǹ� ���õ� ������ ������ ��.
            playerNetworkManager.maxStamina.Value = 
                playerStatsManager.CalculateStaminaBasedOnEnduranceLevel(playerNetworkManager.endurance.Value);
            PlayerUIManager.Instance.playerUIHUDManager.SetMaxStaminaValue(playerNetworkManager.maxStamina.Value);
            playerNetworkManager.currentStamina.Value =
                playerStatsManager.CalculateStaminaBasedOnEnduranceLevel(playerNetworkManager.endurance.Value);
        }
    }

    public void SaveGameDataToCurrentCharacterData(ref CharacterSaveData currentCharacterSaveData)
    {
        currentCharacterSaveData.characterName = playerNetworkManager.characterName.Value.ToString();
        currentCharacterSaveData.yPosition = transform.position.y;
        currentCharacterSaveData.xPosition = transform.position.x;
        currentCharacterSaveData.zPosition = transform.position.z;
    }

    public void LoadGameDataFromCurrentCharacterData(ref CharacterSaveData currentCharacterSaveData)
    {
        playerNetworkManager.characterName.Value = currentCharacterSaveData.characterName;
        Vector3 myPosition = new Vector3(
            currentCharacterSaveData.xPosition,
            currentCharacterSaveData.yPosition,
            currentCharacterSaveData.zPosition
            );
        transform.position = myPosition;
    }
}
