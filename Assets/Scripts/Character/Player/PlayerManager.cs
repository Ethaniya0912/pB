using System;
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

    [HideInInspector] public PlayerAnimationManager playerAnimationManager;
    [HideInInspector] public PlayerLocomotionManager playerLocomotionManager;
    [HideInInspector] public PlayerNetworkManager playerNetworkManager;
    [HideInInspector] public PlayerStatsManager playerStatsManager;
    [HideInInspector] public PlayerInventoryManager playerInventoryManager;
    [HideInInspector] public PlayerEquipmentManager playerEquipmentManager;
    [HideInInspector] public PlayerCombatManager playerCombatManager;
    [HideInInspector] public PlayerInteractionManager playerInteractionManager;

    // [수정사항] PlayerCamera 싱글턴 삭제 방침에 따라 참조용 변수 추가
    [HideInInspector] public PlayerCamera playerCamera;

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
        playerStatsManager.RegenerateStamina(); ;
    }

    protected override void LateUpdate()
    {
        // 플레이어가 오너일때만 해당, 아닐 시 리턴.
        if (!IsOwner) return;

        base.LateUpdate();

        // [수정사항] player.playerCamera 싱글턴 사용을 지양하고 캐싱된 참조(playerCamera) 사용
        // 기존 코드: player.playerCamera.HandleAllCameraActions();
        if (playerCamera != null)
        {
            playerCamera.HandleAllCameraActions();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;

        // 클라이언트에 의해 소유된 플레이어 오브젝트일 시
        if (IsOwner)
        {
            // [수정사항] 현재 씬의 PlayerCamera를 찾아 캐싱 (싱글턴 대체)
            playerCamera = FindObjectOfType<PlayerCamera>();

            // [수정사항] 싱글턴(Instance) 참조를 변수 참조(playerCamera)로 변경
            // 기존 코드: player.playerCamera.player = this;
            if (playerCamera != null)
            {
                playerCamera.player = this;
            }

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

        // 플레이어 스폰 시 Player Camera에 InventoryPivot 설정
        // [수정사항] player.playerCamera 널 체크를 playerCamera로 변경
        // 기존 코드: if (IsOwner && player.playerCamera != null)
        if (IsOwner && playerCamera != null)
        {
            // TD : 남규할일 - 인벤토리 피벗 관련 코드 추가 필요.
            // player.playerCamera.SetInventoryPivot(playerInventoryManager.inventoryCameraPivot);
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
        if (!IsServer && IsOwner)
        {
            foreach (var player in WorldGameSessionManager.Instance.players)
            {
                if (player != this)
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

    #region Routing & Gating & Auto-Transition Logic
    public void OnMovementInputReceived(Vector2 movementInput)
    {
        if (movementInput.sqrMagnitude > 0)
        {
            if (WorldGameStateManager.Instance.currentState == GameState.Inventory ||
                WorldGameStateManager.Instance.currentState == GameState.Table)
            {
                WorldGameStateManager.Instance.SetGamePlaySituation(GameState.Normal);
            }
        }

        // 정책 확인 후 도메인으로 전달.
        if (WorldGameStateManager.Instance.IsMovementAllowed())
        {
            playerLocomotionManager.OnMovementInputReceived(movementInput);
        }
    }

    internal void OnCameraInputReceived(Vector2 cameraInput)
    {
        // [수정사항] player.playerCamera 싱글턴 사용을 playerCamera 참조로 변경
        // 기존 코드:
        // if (player.playerCamera != null)
        // {
        //     player.playerCamera.OnCameraInputReceived(cameraInput.x, cameraInput.y);
        // }
        if (playerCamera != null)
        {
            playerCamera.OnCameraInputReceived(cameraInput.x, cameraInput.y);
        }
    }

    internal void OnDodgeInputReceived()
    {
        playerLocomotionManager.OnDodgeInputReceived();
    }

    internal void OnRBInputReceived()
    {
        // [수정사항 코멘트] 
        // 기존 작성하신 코드가 이미 '실행 확정 시점의 정책 주입' 아키텍처를 완벽히 준수하고 있습니다!
        // 가드 로직을 통과한 순간에만 SetCharacterActionHand(true)를 주입하고 있습니다.

        // 1순위 : 상호작용 (물건 소지 시 '공격' 보다 '놓기' 우선)
        if (playerInteractionManager.currentlyHeldObject != null)
        {
            playerNetworkManager.SetCharacterActionHand(true); // RB_input 들어오면 항상 참.
            playerInteractionManager.OnRBInputReceived();
            return;
        }

        // 2순위 : 전투

        if (WorldGameStateManager.Instance.IsCombatAllowed())
        {
            playerNetworkManager.SetCharacterActionHand(true); // RB_input 들어오면 항상 참.
            playerCombatManager.OnRBInputReceived();
        }
    }

    internal void OnRTInputReceived()
    {
        // [수정사항 코멘트] 이 함수 역시 구조가 이미 완벽합니다.
        if (WorldGameStateManager.Instance.IsCombatAllowed())
        {
            playerNetworkManager.SetCharacterActionHand(true); // RT_input 들어오면 항상 참.
            playerCombatManager.OnRTInputReceived();
        }
    }
/*
    // [수정사항] 대칭되는 완벽한 구성을 위해 왼손 액션(LB, LT) 라우팅 함수를 추가했습니다.
    internal void OnLBInputReceived()
    {
        if (WorldGameStateManager.Instance.IsCombatAllowed())
        {
            // 왼손 동작이므로 false 주입
            playerNetworkManager.SetCharacterActionHand(false);
            playerCombatManager.OnLBInputReceived();
        }
    }

    // [수정사항] 대칭되는 완벽한 구성을 위해 왼손 액션(LB, LT) 라우팅 함수를 추가했습니다.
    internal void OnLTInputReceived()
    {
        if (WorldGameStateManager.Instance.IsCombatAllowed())
        {
            // 왼손 동작이므로 false 주입
            playerNetworkManager.SetCharacterActionHand(false);
            playerCombatManager.OnLTInputReceived();
        }
    }
*/
    internal void OnInteractionInputReceived()
    {
        if (WorldGameStateManager.Instance.IsInteractionAllowed())
        {
            playerInteractionManager.OnInteractionInputReceived();

            // [자동 전이] 상호작용 결과로 물건을 들게 되었다면 Inventory 상태(포커스)로 전환
            // TD : 남규가 아래 코드를 활용하여 가방 인터액션 - 인벤토리 상태등으로 만들거나 하는 등 하면 됨.
            //if (playerInteractionManager.손에 아이템을 든 상태에서 가방도 열어 넣으려는 상태())
            //{
            //    WorldGameStateManager.Instance.SetGamePlaySituation(GameState.Inventory, playerInteractionManager.currentlyHeldObject.transform);
            //}            
        }
    }

    internal void OnLockOnInputReceived()
    {
        playerCombatManager.OnLockOnInputReceived();
    }

    internal void OnLockOnSwitchTargetInputReceived(LockOnDirection direction)
    {
        // [수정사항] 단일 콜백 통합 방식 적용 (보고서 반영)
        // 락온 상태가 아닐 경우 연산 원천 봉쇄 (중앙 정책 검문)
        if (!playerNetworkManager.isLockedOn.Value)
            return;

        // 상황 검증이 끝났으므로, 물리적 탐색과 실행을 담당하는 카메라 계층으로 데이터(의도) 배분
        if (playerCamera != null)
        {
            playerCamera.SwitchLockOnTarget(direction);
        }

        // 기존 코드: 컴뱃 매니저로 전달하던 부분은 원본 보존 규칙에 따라 남겨두었습니다.
        // (타겟 탐색 및 스위칭 로직이 카메라로 완전 이관되었다면 이 줄은 추후 삭제하셔도 무방합니다)
        playerCombatManager.OnLockOnSwitchTargetInputReceived(direction);
    }

    internal void OnSwitchWeaponInputReceived(SwithchWeaponSide value)
    {
        switch (value)
        {
            case SwithchWeaponSide.Left:
                playerEquipmentManager.SwitchLeftWeapon();
                break;
            case SwithchWeaponSide.Right:
                playerEquipmentManager.SwitchRightWeapon();
                break;
            default:
                break;
        }
    }

    internal void OnInventoryInputReceived()
    {
        playerInventoryManager.OnInventoryInputReceived();
    }

    internal void OnAltInputReceived(bool isPressed)
    {
        playerInteractionManager.OnAltInputReceived(isPressed);
    }
    #endregion
}