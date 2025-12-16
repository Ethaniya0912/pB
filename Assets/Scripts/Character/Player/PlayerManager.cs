using System.Collections;
using System.Collections.Generic;
using SG;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerManager : CharacterManager
{
    [Header("DEBUG MENU")]
    [SerializeField] bool respawnCharacter = false;
    [SerializeField] bool switchRightWeapon = false;

    [HideInInspector]public PlayerAnimationManager playerAnimationManager;
    [HideInInspector]public PlayerLocomotionManager playerLocomotionManager;
    [HideInInspector]public PlayerNetworkManager playerNetworkManager;
    [HideInInspector]public PlayerStatsManager playerStatsManager;
    [HideInInspector]public PlayerInventoryManager playerInventoryManager;
    [HideInInspector]public PlayerEquipmentManager playerEquipmentManager;
    [HideInInspector]public PlayerCombatManager playerCombatManager;
    [HideInInspector]public PlayerInteractionManager playerInteractionManager;

    protected override void Awake()
    {
        base.Awake();

        // 캐릭터매니저 위에 오버라이드하여 플레이어특정 기능들 추가.

        playerAnimationManager = GetComponent<PlayerAnimationManager>();
        playerLocomotionManager = GetComponent<PlayerLocomotionManager>();
        playerNetworkManager = GetComponent<PlayerNetworkManager>();
        playerStatsManager = GetComponent<PlayerStatsManager>();
        playerInventoryManager = GetComponent<PlayerInventoryManager>();
        playerEquipmentManager = GetComponent<PlayerEquipmentManager>();
        playerCombatManager = GetComponent<PlayerCombatManager>();
        playerInteractionManager = GetComponent<PlayerInteractionManager>();
    }

    protected override void Update()
    {
        base.Update();

        // Owner일때만 조종할 수 있도록 해줌.
        if (!IsOwner)
            return;

        // Handle Movement
        playerLocomotionManager.HandleAllMovement();

        // 스태미나 리젠 함수 업데이트
        playerStatsManager.RegenerateStamina();;
    }

    protected override void LateUpdate()
    {
        // 플레이어가 오너일때만 해당, 아닐 시 리턴.
        if (!IsOwner) return;

        base.LateUpdate();

        PlayerCamera.Instance.HandleAllCameraActions();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;

        // 클라이언트에 의해 소유된 플레이어 오브젝트일 시
        if (IsOwner)
        {
            PlayerCamera.Instance.player = this;
            PlayerInputManager.Instance.player = this;
            WorldSaveGameManager.Instance.player = this;

            // 바이탈리티나 엔듀런스가 변화시 맥스헬스/스태미나 정해주기.
            playerNetworkManager.vitality.OnValueChanged +=
                playerNetworkManager.SetNewMaxHealthValue;
            playerNetworkManager.endurance.OnValueChanged +=
                playerNetworkManager.SetNewMaxStaminaValue;

            // 현재 체력이나 스태미나 변화시 UI 스탯바에 변화를 줌.
            playerNetworkManager.currentHealth.OnValueChanged +=
                PlayerUIManager.Instance.playerUIHUDManager.SetNewHealthValue;
            playerNetworkManager.currentStamina.OnValueChanged +=
                PlayerUIManager.Instance.playerUIHUDManager.SetNewStaminaValue;
            playerNetworkManager.currentStamina.OnValueChanged +=
                playerStatsManager.ResetStaminaRegenTimer;

        }

        // 락온 (오너가 아니여도 작동해야함)
        playerNetworkManager.isLockedOn.OnValueChanged += playerNetworkManager.OnIsLockedOnChange;

        // 스탯
        playerNetworkManager.currentHealth.OnValueChanged += playerNetworkManager.CheckHP;

        // 장비
        playerNetworkManager.currentRightHandWeaponID.OnValueChanged += playerNetworkManager.OnCurrentRightHandWeaponIDChange;
        playerNetworkManager.currentLeftHandWeaponID.OnValueChanged += playerNetworkManager.OnCurrentLeftHandWeaponIDChange;
        playerNetworkManager.currentWeaponBeingUsed.OnValueChanged += playerNetworkManager.OnCurrentWeaponBeingUsedIDChange;

        // 플래그
        playerNetworkManager.isChargingAttack.OnValueChanged += playerNetworkManager.OnIsChargingAttackChanged;

        // 접속시 우리가 캐릭터의 오너지만 서버가 아닐 경우 캐릭터 데이터를 새로 인스턴스한 캐릭터로 리로드
        if (IsOwner && !IsServer)
        {
            LoadGameDataFromCurrentCharacterData(ref WorldSaveGameManager.Instance.currentCharacterData);
        }

    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnectedCallback;

        // 클라이언트에 의해 소유된 플레이어 오브젝트일 시
        if (IsOwner)
        {
            // 바이탈리티나 엔듀런스가 변화시 맥스헬스/스태미나 정해주기.
            playerNetworkManager.vitality.OnValueChanged -=
                playerNetworkManager.SetNewMaxHealthValue;
            playerNetworkManager.endurance.OnValueChanged -=
                playerNetworkManager.SetNewMaxStaminaValue;

            // 현재 체력이나 스태미나 변화시 UI 스탯바에 변화를 줌.
            playerNetworkManager.currentHealth.OnValueChanged -=
                PlayerUIManager.Instance.playerUIHUDManager.SetNewHealthValue;
            playerNetworkManager.currentStamina.OnValueChanged -=
                PlayerUIManager.Instance.playerUIHUDManager.SetNewStaminaValue;
            playerNetworkManager.currentStamina.OnValueChanged -=
                playerStatsManager.ResetStaminaRegenTimer;

        }

        // 락온 (오너가 아니여도 작동해야함)
        playerNetworkManager.isLockedOn.OnValueChanged -= playerNetworkManager.OnIsLockedOnChange;

        // 스탯
        playerNetworkManager.currentHealth.OnValueChanged -= playerNetworkManager.CheckHP;

        // 장비
        playerNetworkManager.currentRightHandWeaponID.OnValueChanged -= playerNetworkManager.OnCurrentRightHandWeaponIDChange;
        playerNetworkManager.currentLeftHandWeaponID.OnValueChanged -= playerNetworkManager.OnCurrentLeftHandWeaponIDChange;
        playerNetworkManager.currentWeaponBeingUsed.OnValueChanged -= playerNetworkManager.OnCurrentWeaponBeingUsedIDChange;

        // 플래그
        playerNetworkManager.isChargingAttack.OnValueChanged -= playerNetworkManager.OnIsChargingAttackChanged;

    }

    private void OnClientConnectedCallback(ulong clientID)
    {
        // 유저가 접속하면 이벤트에 붙일 것, OnNetworkSpawn에.
        // 현 게임세션에 활동하는 플레이어 리스트를 키핑함.
        WorldGameSessionManager.Instance.AddPlayerToActivePlayerList(this);

        // 만약 우리가 서버라면, 호스트이며, 다른 유저를 싱크할 필요가 없음(나중에 접속할일x)
        // 도중에 참가한 유저일때만 싱크할 필요성이 있음.
        if(!IsServer && IsOwner)
        {
            foreach(var player in WorldGameSessionManager.Instance.players)
            {
                if(player != this)
                {
                    player.LoadOtherPlayerCharacterWhenJoiningServer();
                }
            }
        }
    }

    public override IEnumerator ProcessDeathEvent(bool manuallySelectDeathAnimation = false)
    {
        if (IsOwner)
        {
            PlayerUIManager.Instance.playerUIPopUpManager.SendYouDiedPopUp();
        }
        return base.ProcessDeathEvent(manuallySelectDeathAnimation);

        // 유저들이 살아 있는지 체크하고, 모두 사망 시 캐릭터 리스폰.
    }

    public override void ReviveCharacter()
    {
        base.ReviveCharacter();

        if (IsOwner)
        {
            playerNetworkManager.isDead.Value = false;
            playerNetworkManager.currentHealth.Value = playerNetworkManager.maxHealth.Value;
            playerNetworkManager.currentStamina.Value = playerNetworkManager.maxStamina.Value;
            // 포커스 포인츠도 복수

            // 부활 이펙트
            playerAnimationManager.PlayTargetAnimation("Empty", false);
        }
    }

    public void SaveGameDataToCurrentCharacterData(ref CharacterSaveData currentCharacterSaveData)
    {
        currentCharacterSaveData.sceneIndex = SceneManager.GetActiveScene().buildIndex;
        currentCharacterSaveData.characterName = playerNetworkManager.characterName.Value.ToString();
        currentCharacterSaveData.yPosition = transform.position.y;
        currentCharacterSaveData.xPosition = transform.position.x;
        currentCharacterSaveData.zPosition = transform.position.z;

        currentCharacterSaveData.currentHealth = playerNetworkManager.currentHealth.Value;
        currentCharacterSaveData.currentStamina = playerNetworkManager.currentStamina.Value;

        currentCharacterSaveData.vitality = playerNetworkManager.vitality.Value;
        currentCharacterSaveData.endurance = playerNetworkManager.endurance.Value;
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

        playerNetworkManager.vitality.Value = currentCharacterSaveData.vitality;
        playerNetworkManager.endurance.Value = currentCharacterSaveData.endurance;
        
        // 관련 코드는 세이빙/로드가 추가되면 관련된 곳으로 옮길 것.
        playerNetworkManager.maxHealth.Value =
            playerStatsManager.CalculateHealthBasedOnVitalityLevel(playerNetworkManager.vitality.Value);
        playerNetworkManager.maxStamina.Value = 
            playerStatsManager.CalculateStaminaBasedOnEnduranceLevel(playerNetworkManager.endurance.Value);
        PlayerUIManager.Instance.playerUIHUDManager.SetMaxStaminaValue(playerNetworkManager.maxStamina.Value);
        playerNetworkManager.currentHealth.Value =
            playerStatsManager.CalculateHealthBasedOnVitalityLevel(playerNetworkManager.vitality.Value); 
        playerNetworkManager.currentStamina.Value =
            playerStatsManager.CalculateStaminaBasedOnEnduranceLevel(playerNetworkManager.endurance.Value);
    }

    public void LoadOtherPlayerCharacterWhenJoiningServer()
    {
        // 무기 싱크(나중에 아머나 캐릭터커마라면 수염등이 있음)
        playerNetworkManager.OnCurrentRightHandWeaponIDChange(0, playerNetworkManager.currentRightHandWeaponID.Value);
        playerNetworkManager.OnCurrentLeftHandWeaponIDChange(0, playerNetworkManager.currentLeftHandWeaponID.Value);

        // 아머 싱크

        // 락온 타겟 싱크
        if (playerNetworkManager.isLockedOn.Value)
        {
            playerNetworkManager.OnLockOnTargetIDChange(0, playerNetworkManager.currentTargetNetworkObjectID.Value);
        }
    }

    // 나중에 디버깅은 지움.
    
}
