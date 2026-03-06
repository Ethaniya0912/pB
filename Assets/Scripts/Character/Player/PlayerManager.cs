using System.Collections;
using SG;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TDA.Character.Player
{
    /// <summary>
    /// [L2 Router] 플레이어 캐릭터의 최종 의사결정권자 및 중앙 관제탑 (Gatekeeper).
    /// 
    /// [아키텍처 설계 철학]
    /// 1. 중앙 검문소: Input(L1)에서 넘어온 맹목적인 신호를 WorldGameState(정책)와 대조하여 필터링합니다.
    /// 2. 선제적 갱신(Pre-emptive Sync): 실행이 확정된 명령을 하위 도메인(L3)으로 배분하기 직전에, 
    ///    네트워크 변수를 먼저 갱신하여 멀티플레이 환경에서의 지연(Latency)을 시각적으로 최소화합니다.
    /// 3. 의존성 통제: 모든 도메인 매니저는 이 클래스 아래에 묶이며, 도메인끼리의 수평적 직접 호출을 금지합니다.
    /// </summary>
    public class PlayerManager : CharacterManager
    {
        #region [Variables] 디버그 및 도메인 의존성 (Dependencies)

        [Header("DEBUG MENU")]
        [SerializeField] bool respawnCharacter = false;
        [SerializeField] bool switchRightWeapon = false;

        [Header("Domain Dependencies (L3 - Muscle)")]
        [HideInInspector] public PlayerAnimationManager playerAnimationManager;
        [HideInInspector] public PlayerLocomotionManager playerLocomotionManager;
        [HideInInspector] public PlayerNetworkManager playerNetworkManager;
        [HideInInspector] public PlayerStatsManager playerStatsManager;
        [HideInInspector] public PlayerInventoryManager playerInventoryManager;
        [HideInInspector] public PlayerEquipmentManager playerEquipmentManager;
        [HideInInspector] public PlayerCombatManager playerCombatManager;
        [HideInInspector] public PlayerInteractionManager playerInteractionManager;

        [Header("Event & Camera Dependencies (L4 - View)")]
        // [P2] 싱글턴 삭제 방침에 따라 씬 내 로컬 카메라의 참조를 런타임에 동적으로 주입받아 캐싱합니다.
        [HideInInspector] public PlayerCamera playerCamera;

        #endregion

        #region [Lifecycle] 초기화 및 프레임 업데이트

        protected override void Awake()
        {
            base.Awake();

            // 부모인 CharacterManager 위에 플레이어 특화 기능(도메인 매니저)들을 캐싱합니다.
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

            // 로컬 플레이어(Owner)일 때만 시스템 통제권을 갖습니다. 타 유저(클론)의 로직 실행을 방지합니다.
            if (!IsOwner) return;

            // 상시 실행 도메인 로직 업데이트
            playerLocomotionManager.HandleAllMovement();
            playerStatsManager.RegenerateStamina();
        }

        protected override void LateUpdate()
        {
            // 플레이어가 오너일때만 해당, 아닐 시 리턴.
            if (!IsOwner) return;

            base.LateUpdate();

            // 🔥 [삭제] 이 부분 삭제! PlayerCamera가 자신의 LateUpdate에서 스스로 연산합니다.
            // if (playerCamera != null) { playerCamera.HandleAllCameraActions(); }
        }

        #endregion

        #region [Network Lifecycle] NGO 스폰 및 초기화 세팅

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;

            if (IsOwner)
            {
                InitializeLocalPlayerSetup();
            }

            // 게임 도중 접속 시, 서버가 아니라면 기존 캐릭터 데이터를 로드하여 내 상태를 동기화합니다.
            if (IsOwner && !IsServer)
            {
                LoadGameDataFromCurrentCharacterData(ref WorldSaveGameManager.Instance.currentCharacterData);
            }

            // 모든 네트워크 이벤트 구독을 명시적으로 실행합니다.
            SubscribeNetworkEvents();
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnectedCallback;

            // 파괴 시점 메모리 릭(Memory Leak) 방지를 위해 구독을 일괄 해제합니다.
            UnsubscribeNetworkEvents();
        }

        /// <summary>
        /// 로컬 플레이어(IsOwner)가 접속했을 때 최초 1회 세팅해야 하는 UI 및 카메라 참조를 초기화합니다.
        /// </summary>
        private void InitializeLocalPlayerSetup()
        {
            // [P2] 씬에서 내 로컬 카메라를 찾아 주입 (싱글턴 충돌 방지)
            playerCamera = FindObjectOfType<PlayerCamera>();
            if (playerCamera != null)
            {
                playerCamera.player = this;
                // 추후 필요 시: playerCamera.SetInventoryPivot(playerInventoryManager.inventoryCameraPivot);
            }

            PlayerInputManager.Instance.player = this;
            WorldSaveGameManager.Instance.player = this;
        }

        private void OnClientConnectedCallback(ulong clientID)
        {
            // 현 게임 세션에 활동하는 플레이어 리스트에 자신을 등록합니다.
            WorldGameSessionManager.Instance.AddPlayerToActivePlayerList(this);

            // 도중에 참가한 유저일 경우, 이미 접속해 있던 기존 유저들의 외형/무기 상태를 내 화면에 동기화합니다.
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

        #endregion

        #region [Network Events] 스위치 보드 (상태 변화 구독/해제 관리)

        /// <summary>
        /// 흩어져 있던 NetworkVariable OnValueChanged 구독 로직을 하나로 모아 가독성을 높인 스위치 보드입니다.
        /// </summary>
        private void SubscribeNetworkEvents()
        {
            // [로컬 오너 전용] 내 화면의 UI와 직결된 이벤트만 구독합니다.
            if (IsOwner)
            {
                playerNetworkManager.vitality.OnValueChanged += playerNetworkManager.SetNewMaxHealthValue;
                playerNetworkManager.endurance.OnValueChanged += playerNetworkManager.SetNewMaxStaminaValue;

                playerNetworkManager.currentHealth.OnValueChanged += PlayerUIManager.Instance.playerUIHUDManager.SetNewHealthValue;
                playerNetworkManager.currentStamina.OnValueChanged += PlayerUIManager.Instance.playerUIHUDManager.SetNewStaminaValue;
                playerNetworkManager.currentStamina.OnValueChanged += playerStatsManager.ResetStaminaRegenTimer;
            }

            // [글로벌 공통] 모든 클라이언트가 알아야 하는 시각적 상태(무기 변경, 락온 등)를 구독합니다.
            playerNetworkManager.isLockedOn.OnValueChanged += playerNetworkManager.OnIsLockedOnChange;
            playerNetworkManager.currentHealth.OnValueChanged += playerNetworkManager.CheckHP;
            playerNetworkManager.currentRightHandWeaponID.OnValueChanged += playerNetworkManager.OnCurrentRightHandWeaponIDChange;
            playerNetworkManager.currentLeftHandWeaponID.OnValueChanged += playerNetworkManager.OnCurrentLeftHandWeaponIDChange;
            playerNetworkManager.currentWeaponBeingUsed.OnValueChanged += playerNetworkManager.OnCurrentWeaponBeingUsedIDChange;
            playerNetworkManager.isChargingAttack.OnValueChanged += playerNetworkManager.OnIsChargingAttackChanged;
        }

        private void UnsubscribeNetworkEvents()
        {
            if (IsOwner)
            {
                playerNetworkManager.vitality.OnValueChanged -= playerNetworkManager.SetNewMaxHealthValue;
                playerNetworkManager.endurance.OnValueChanged -= playerNetworkManager.SetNewMaxStaminaValue;

                if (PlayerUIManager.Instance != null && PlayerUIManager.Instance.playerUIHUDManager != null)
                {
                    playerNetworkManager.currentHealth.OnValueChanged -= PlayerUIManager.Instance.playerUIHUDManager.SetNewHealthValue;
                    playerNetworkManager.currentStamina.OnValueChanged -= PlayerUIManager.Instance.playerUIHUDManager.SetNewStaminaValue;
                }
                playerNetworkManager.currentStamina.OnValueChanged -= playerStatsManager.ResetStaminaRegenTimer;
            }

            playerNetworkManager.isLockedOn.OnValueChanged -= playerNetworkManager.OnIsLockedOnChange;
            playerNetworkManager.currentHealth.OnValueChanged -= playerNetworkManager.CheckHP;
            playerNetworkManager.currentRightHandWeaponID.OnValueChanged -= playerNetworkManager.OnCurrentRightHandWeaponIDChange;
            playerNetworkManager.currentLeftHandWeaponID.OnValueChanged -= playerNetworkManager.OnCurrentLeftHandWeaponIDChange;
            playerNetworkManager.currentWeaponBeingUsed.OnValueChanged -= playerNetworkManager.OnCurrentWeaponBeingUsedIDChange;
            playerNetworkManager.isChargingAttack.OnValueChanged -= playerNetworkManager.OnIsChargingAttackChanged;
        }

        #endregion

        #region [Input Routing] 전투 및 상호작용 (Combat & Interaction Gating)

        internal void OnRBInputReceived()
        {
            // [검문 1순위] 상호작용 (물건을 들고 있을 땐 전투(공격)를 차단하고 놓기 우선 수행)
            if (playerInteractionManager.currentlyHeldObject != null)
            {
                playerNetworkManager.SetCharacterActionHand(true);
                playerInteractionManager.OnRBInputReceived();
                return; // 도메인 배분 완료 후 즉시 종료
            }

            // [검문 2순위] 시스템 상태 검사 (스태미나 부족 시 허공 칼질 차단)
            if (playerNetworkManager.currentStamina.Value <= 0)
            {
#if UNITY_EDITOR
                Debug.Log("<color=red>[Gatekeeper]</color> RB_Input Blocked: Insufficient Stamina (스태미나 부족)");
#endif
                return;
            }

            // [검문 3순위] 게임 전역 정책 검사 (인벤토리를 보고 있거나, 컷신 중인지?)
            if (WorldGameStateManager.Instance.IsCombatAllowed())
            {
                // 👉 [선제적 갱신(Pre-emptive Sync)]
                // 공격 연산에 들어가기 직전, 현재 공격에 사용할 무기 ID를 네트워크에 먼저 공표하여 데스싱크를 방어합니다.
                if (playerInventoryManager.currentRightHandWeapon != null)
                {
                    playerNetworkManager.currentWeaponBeingUsed.Value = playerInventoryManager.currentRightHandWeapon.itemID;
                }

                playerNetworkManager.SetCharacterActionHand(true);

                // 모든 검문과 갱신이 끝났으므로, 전투 도메인에게 실제 액션 집행을 하달합니다.
                playerCombatManager.OnRBInputReceived();
            }
        }

        internal void OnRTInputReceived()
        {
            if (playerNetworkManager.currentStamina.Value <= 0)
            {
#if UNITY_EDITOR
                Debug.Log("<color=red>[Gatekeeper]</color> RT_Input Blocked: Insufficient Stamina (강공격 취소)");
#endif
                return;
            }

            if (WorldGameStateManager.Instance.IsCombatAllowed())
            {
                if (playerInventoryManager.currentRightHandWeapon != null)
                {
                    playerNetworkManager.currentWeaponBeingUsed.Value = playerInventoryManager.currentRightHandWeapon.itemID;
                }

                playerNetworkManager.SetCharacterActionHand(true);
                playerCombatManager.OnRTInputReceived();
            }
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
            }
        }

        internal void OnInteractionInputReceived()
        {
            if (WorldGameStateManager.Instance.IsInteractionAllowed())
            {
                playerInteractionManager.OnInteractionInputReceived();

                // [자동 전이 로직 확장부] 상호작용 결과로 물건을 들게 되었다면 Inventory 상태(포커스)로 전환 연동
                // if (playerInteractionManager.isHoldingObject) ...
            }
        }

        internal void OnAltInputReceived(bool isPressed)
        {
            playerInteractionManager.OnAltInputReceived(isPressed);
        }

        internal void OnInventoryInputReceived()
        {
            playerInventoryManager.OnInventoryInputReceived();
        }

        #endregion

        #region [Input Routing] 이동, 카메라 및 타겟팅 (Locomotion & Vision)

        public void OnMovementInputReceived(Vector2 movementInput)
        {
            if (movementInput.sqrMagnitude > 0)
            {
                // [지능형 뷰 전이] 인벤토리를 보거나 테이블에 앉아있다가 이동 키를 누르면 자동으로 창을 닫고 Normal 상태로 복귀
                if (WorldGameStateManager.Instance.currentState == GameState.Inventory ||
                    WorldGameStateManager.Instance.currentState == GameState.Table)
                {
                    WorldGameStateManager.Instance.SetGamePlaySituation(GameState.Normal);
                }
            }

            // 정책 레이어 승인 후 이동 도메인으로 전달
            if (WorldGameStateManager.Instance.IsMovementAllowed())
            {
                playerLocomotionManager.OnMovementInputReceived(movementInput);
            }
        }

        internal void OnDodgeInputReceived()
        {
            playerLocomotionManager.OnDodgeInputReceived();
        }

        internal void OnCameraInputReceived(Vector2 cameraInput)
        {
            if (playerCamera != null)
            {
                playerCamera.OnCameraInputReceived(cameraInput.x, cameraInput.y);
            }
        }

        internal void OnLockOnInputReceived()
        {
            playerCombatManager.OnLockOnInputReceived();
        }

        internal void OnLockOnSwitchTargetInputReceived(LockOnDirection direction)
        {
            // [정책 검문] 락온 상태가 아닐 경우 불필요한 탐색 연산을 원천 봉쇄합니다.
            if (!playerNetworkManager.isLockedOn.Value) return;

            // 1. 시각적 전환 명령은 카메라로 배분 (부드러운 시선 이동)
            if (playerCamera != null)
            {
                playerCamera.SwitchLockOnTarget(direction);
            }

            // 2. 물리적/논리적 타겟 전환 명령은 전투 도메인으로 배분 (데이터 변경)
            playerCombatManager.OnLockOnSwitchTargetInputReceived(direction);
        }

        #endregion

        #region [State Management] 생명주기 및 저장/불러오기 (Death & Persistence)

        public override IEnumerator ProcessDeathEvent(bool manuallySelectDeathAnimation = false)
        {
            if (IsOwner && PlayerUIManager.Instance != null)
            {
                PlayerUIManager.Instance.playerUIPopUpManager.SendYouDiedPopUp();
            }
            return base.ProcessDeathEvent(manuallySelectDeathAnimation);
        }

        public override void ReviveCharacter()
        {
            base.ReviveCharacter();

            if (IsOwner)
            {
                playerNetworkManager.isDead.Value = false;
                playerNetworkManager.currentHealth.Value = playerNetworkManager.maxHealth.Value;
                playerNetworkManager.currentStamina.Value = playerNetworkManager.maxStamina.Value;

                // 부활 시 기본 자세(Empty)로 즉시 전환
                playerAnimationManager.PlayTargetAnimation(Animator.StringToHash("Empty"), false);
            }
        }

        public void SaveGameDataToCurrentCharacterData(ref CharacterSaveData currentCharacterSaveData)
        {
            currentCharacterSaveData.sceneIndex = SceneManager.GetActiveScene().buildIndex;
            currentCharacterSaveData.characterName = playerNetworkManager.characterName.Value.ToString();

            currentCharacterSaveData.xPosition = transform.position.x;
            currentCharacterSaveData.yPosition = transform.position.y;
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

            playerNetworkManager.maxHealth.Value = playerStatsManager.CalculateHealthBasedOnVitalityLevel(playerNetworkManager.vitality.Value);
            playerNetworkManager.maxStamina.Value = playerStatsManager.CalculateStaminaBasedOnEnduranceLevel(playerNetworkManager.endurance.Value);

            if (PlayerUIManager.Instance != null)
                PlayerUIManager.Instance.playerUIHUDManager.SetMaxStaminaValue(playerNetworkManager.maxStamina.Value);

            playerNetworkManager.currentHealth.Value = playerStatsManager.CalculateHealthBasedOnVitalityLevel(playerNetworkManager.vitality.Value);
            playerNetworkManager.currentStamina.Value = playerStatsManager.CalculateStaminaBasedOnEnduranceLevel(playerNetworkManager.endurance.Value);
        }

        public void LoadOtherPlayerCharacterWhenJoiningServer()
        {
            // 후발 접속자를 위해 기존 유저들의 무기 및 아머 동기화 강제 실행
            playerNetworkManager.OnCurrentRightHandWeaponIDChange(0, playerNetworkManager.currentRightHandWeaponID.Value);
            playerNetworkManager.OnCurrentLeftHandWeaponIDChange(0, playerNetworkManager.currentLeftHandWeaponID.Value);

            // 락온 타겟 강제 동기화
            if (playerNetworkManager.isLockedOn.Value)
            {
                playerNetworkManager.OnLockOnTargetIDChange(0, playerNetworkManager.currentTargetNetworkObjectID.Value);
            }
        }

        #endregion
    }
}