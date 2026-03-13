using System.Collections;
using SG;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using TDA.Core.Events;

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
    /// 4. 깔때기(Funnel) 라우팅: 액션 실행 권한은 오직 L4(AnimationManager)만 가지도록 L3를 거쳐 위임합니다.
    /// </summary>
    public class PlayerManager : CharacterManager
    {
        #region [Variables] 디버그 및 도메인 의존성 (Dependencies)

        [Header("DEBUG MENU")]
        [SerializeField] bool respawnCharacter = false;
        [SerializeField] bool switchRightWeapon = false;

        [Header("Domain Dependencies (L3 - Muscle)")]
        [Tooltip("캐릭터에 부착된 모든 하위 도메인 매니저들을 캐싱하여 중앙에서 통제합니다.")]
        [HideInInspector] public PlayerAnimationManager playerAnimationManager;
        [HideInInspector] public PlayerLocomotionManager playerLocomotionManager;
        [HideInInspector] public PlayerNetworkManager playerNetworkManager;
        [HideInInspector] public PlayerStatsManager playerStatsManager;
        [HideInInspector] public PlayerInventoryManager playerInventoryManager;
        [HideInInspector] public PlayerEquipmentManager playerEquipmentManager;
        [HideInInspector] public PlayerCombatManager playerCombatManager;
        [HideInInspector] public PlayerInteractionManager playerInteractionManager;
        [HideInInspector] public PlayerEventManager playerEventManager;
        [HideInInspector] public PlayerDefenseManager playerDefenseManager;

        [Header("Event & Camera Dependencies (L4 - View)")]
        [HideInInspector] public PlayerCamera playerCamera;

        // [방어 시스템 P0-02] 양손/한손 방어 키 동시 입력 추적용 변수
        // (기획 의도: 우클릭과 Q키를 번갈아 누를 때 가드가 풀리는 답답함을 막기 위한 상태 추적기)
        [HideInInspector] public bool isHoldingCloseGrip = false;
        [HideInInspector] public bool isHoldingFarGrip = false;

        #endregion

        #region [Lifecycle] 초기화 및 프레임 업데이트

        protected override void Awake()
        {
            base.Awake();

            // 부모인 CharacterManager 위에 플레이어 특화 기능(도메인 매니저)들을 캐싱합니다.
            // 의존성 주입(Dependency Injection)의 루트 역할을 수행합니다.
            playerAnimationManager = GetComponent<PlayerAnimationManager>();
            playerLocomotionManager = GetComponent<PlayerLocomotionManager>();
            playerNetworkManager = GetComponent<PlayerNetworkManager>();
            playerStatsManager = GetComponent<PlayerStatsManager>();
            playerInventoryManager = GetComponent<PlayerInventoryManager>();
            playerEquipmentManager = GetComponent<PlayerEquipmentManager>();
            playerCombatManager = GetComponent<PlayerCombatManager>();
            playerInteractionManager = GetComponent<PlayerInteractionManager>();
            playerEventManager = GetComponent<PlayerEventManager>();
            playerDefenseManager = GetComponent<PlayerDefenseManager>();
        }

        protected override void Update()
        {
            base.Update();

            // [네트워크 방어막] 로컬 플레이어(Owner)일 때만 시스템 통제권을 갖습니다. 
            // 타 유저(클론)의 화면에서 내 코드가 실행되어 좌표가 꼬이는 것을 방지합니다.
            if (!IsOwner) return;

            // 상시 실행되어야 하는 도메인 로직 업데이트 (이동 물리 연산 및 자원 회복)
            playerLocomotionManager.HandleAllMovement();
            playerStatsManager.RegenerateStamina();
        }

        protected override void LateUpdate()
        {
            if (!IsOwner) return;
            base.LateUpdate();
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

            // 게임 도중 접속 시, 서버가 아니라면 기존 캐릭터(세이브) 데이터를 로드하여 내 상태를 즉시 동기화합니다.
            if (IsOwner && !IsServer)
            {
                LoadGameDataFromCurrentCharacterData(ref WorldSaveGameManager.Instance.currentCharacterData);
            }

            // 모든 네트워크 이벤트(UI 갱신 등) 구독을 명시적으로 실행합니다.
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
        /// 로컬 플레이어(IsOwner)가 접속했을 때 최초 1회 세팅해야 하는 시스템 참조를 초기화합니다.
        /// </summary>
        private void InitializeLocalPlayerSetup()
        {
            // 씬에 존재하는 로컬 플레이어용 메인 카메라를 찾아 자신과 바인딩합니다.
            playerCamera = FindObjectOfType<PlayerCamera>();
            if (playerCamera != null)
            {
                playerCamera.player = this;
            }

            // 전역 매니저들에게 현재 활성화된 로컬 플레이어가 누구인지 알립니다.
            PlayerInputManager.Instance.player = this;
            WorldSaveGameManager.Instance.player = this;
        }

        /// <summary>
        /// 다른 클라이언트가 접속했을 때 발생하는 콜백. (후발 접속자 동기화 처리)
        /// </summary>
        private void OnClientConnectedCallback(ulong clientID)
        {
            WorldGameSessionManager.Instance.AddPlayerToActivePlayerList(this);

            if (!IsServer && IsOwner)
            {
                // 내가 방금 접속했다면, 이미 방에 있던 다른 유저들의 무기/장비 외형을 내 화면에 갱신시킵니다.
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
        /// 파편화되기 쉬운 NetworkVariable OnValueChanged 이벤트들을 한 곳에 모아 관리하는 스위치 보드입니다.
        /// </summary>
        private void SubscribeNetworkEvents()
        {
            // [로컬 전용] 내 화면의 UI나 자원(Stamina) 타이머 등은 나에게만 보여야 합니다.
            if (IsOwner)
            {
                playerNetworkManager.vitality.OnValueChanged += playerNetworkManager.SetNewMaxHealthValue;
                playerNetworkManager.endurance.OnValueChanged += playerNetworkManager.SetNewMaxStaminaValue;

                playerNetworkManager.currentHealth.OnValueChanged += PlayerUIManager.Instance.playerUIHUDManager.SetNewHealthValue;
                playerNetworkManager.currentStamina.OnValueChanged += PlayerUIManager.Instance.playerUIHUDManager.SetNewStaminaValue;
                playerNetworkManager.currentStamina.OnValueChanged += playerStatsManager.ResetStaminaRegenTimer;
            }

            // [글로벌 공통] 타겟 락온 방향, 체력 0 도달(CheckHP), 장비 변경 등 타인도 알아야 하는 시각적 상태 변화
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

        #region [Input Routing] 전투, 방어 및 상호작용 (Combat, Defense & Interaction Gating)

        // =========================================================================================
        // 🚨 [방어 시스템 P0-02 연동] ShieldStance (우클릭 vs Q키) 스마트 라우팅 및 디버깅
        // =========================================================================================

        /// <summary>
        /// 우클릭 입력: 몸 가까이 밀착하여 확실하게 방어하는 스탠다드 가드 (CloseGrip)
        /// </summary>
        public void OnBlockInputReceived(bool isBlocking)
        {
            if (playerDefenseManager == null) return;

            // 🔎 [디버그] 입력값이 제대로 들어오는지 추적
            Debug.Log($"<color=white>[Input Tracker]</color> 마우스 우클릭(Block) 신호: {isBlocking} | 기존 우클릭 꾹:{isHoldingCloseGrip}, Q키 꾹:{isHoldingFarGrip}");

            isHoldingCloseGrip = isBlocking;

            if (isBlocking)
            {
                ShieldStance targetStance = isHoldingFarGrip ? ShieldStance.FarGrip : ShieldStance.CloseGrip;
                playerDefenseManager.StartDefense(GuardDirection.Top, targetStance);
            }
            else
            {
                if (isHoldingFarGrip)
                {
                    Debug.Log("<color=orange>[Input Tracker]</color> 우클릭을 뗐지만 Q키를 누르고 있어 가드를 풀지 않습니다.");
                    playerDefenseManager.StartDefense(GuardDirection.Top, ShieldStance.FarGrip);
                }
                else
                {
                    playerDefenseManager.StopDefense();
                }
            }
        }

        /// <summary>
        /// Q키 입력: 방패를 멀리 뻗어 견제하는 익스텐디드 가드 (FarGrip)
        /// </summary>
        public void OnExtendedGuardInputReceived(bool isGuarding)
        {
            if (playerDefenseManager == null) return;

            // 🔎 [디버그] 입력값이 제대로 들어오는지 추적
            Debug.Log($"<color=white>[Input Tracker]</color> 키보드 Q(Extended) 신호: {isGuarding} | 기존 우클릭 꾹:{isHoldingCloseGrip}, Q키 꾹:{isHoldingFarGrip}");

            isHoldingFarGrip = isGuarding;

            if (isGuarding)
            {
                playerDefenseManager.StartDefense(GuardDirection.Top, ShieldStance.FarGrip);
            }
            else
            {
                if (isHoldingCloseGrip)
                {
                    Debug.Log("<color=orange>[Input Tracker]</color> Q키를 뗐지만 우클릭을 누르고 있어 일반 방어로 복귀합니다.");
                    playerDefenseManager.StartDefense(GuardDirection.Top, ShieldStance.CloseGrip);
                }
                else
                {
                    playerDefenseManager.StopDefense();
                }
            }
        }
        // =========================================================================================

        internal void OnRBInputReceived()
        {
            // [검문 1순위: 상호작용] 물건을 들고 있을 땐 전투(공격)를 차단하고 물건 놓기를 우선 수행합니다.
            if (playerInteractionManager.currentlyHeldObject != null)
            {
                playerNetworkManager.SetCharacterActionHand(true);
                playerInteractionManager.OnRBInputReceived();
                return; // 도메인 배분 완료 후 즉시 종료
            }

            // [검문 2순위: 자원 상태] 스태미나가 없으면 허공에 칼질하는 것을 원천 차단합니다.
            if (playerNetworkManager.currentStamina.Value <= 0) return;

            // [검문 3순위: 액션 제어권] 사망했거나 이미 피격/공격 모션이 진행 중이라면 입력을 무시합니다.
            if (playerNetworkManager.isDead.Value || isPerformingAction) return;

            // [검문 4순위: 전역 상태] 인벤토리를 보고 있거나 컷신 중인지 확인합니다.
            if (WorldGameStateManager.Instance.IsCombatAllowed())
            {
                // [선제적 갱신(Pre-emptive Sync)]
                // 공격 연산에 들어가기 직전, 현재 사용할 무기 ID를 네트워크에 먼저 공표하여 멀티 데스싱크(Desync)를 방어합니다.
                if (playerInventoryManager.currentRightHandWeapon != null)
                {
                    playerNetworkManager.currentWeaponBeingUsed.Value = playerInventoryManager.currentRightHandWeapon.itemID;
                }

                playerNetworkManager.SetCharacterActionHand(true);

                // 모든 검문이 끝났으므로 전투 도메인에게 실제 액션 집행을 하달합니다.
                playerCombatManager.OnRBInputReceived();
            }
        }

        internal void OnRTInputReceived()
        {
            // [버그 수정] RT(우클릭)와 방어(Block) 입력이 겹쳐서 가드가 씹히는 현상 해결
            // 기존 강공격 발동 로직을 주석 처리하여, 우클릭 시 오직 '방어'만 실행되도록 만듭니다.
            /*
            if (playerNetworkManager.currentStamina.Value <= 0) return;
            if (playerNetworkManager.isDead.Value || isPerformingAction) return;

            if (WorldGameStateManager.Instance.IsCombatAllowed())
            {
                if (playerInventoryManager.currentRightHandWeapon != null)
                {
                    playerNetworkManager.currentWeaponBeingUsed.Value = playerInventoryManager.currentRightHandWeapon.itemID;
                }

                playerNetworkManager.SetCharacterActionHand(true);
                playerCombatManager.OnRTInputReceived();
            }
            */
            // 디버그 확인용 로그 남기기
            // Debug.Log("<color=gray>[Input]</color> 강공격(RT)이 방어(Block)를 위해 비활성화되었습니다.");
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
            // 상호작용 가능 구역인지 전역 매니저에서 판단
            if (WorldGameStateManager.Instance.IsInteractionAllowed())
            {
                playerInteractionManager.OnInteractionInputReceived();
            }
        }

        internal void OnAltInputReceived(bool isPressed)
        {
            // UI 레이어의 마우스 커서 표시/숨김 등 처리
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
                // [지능형 뷰 전이] 인벤토리를 보거나 벤치에 앉아있다가 WASD(이동)를 누르면, 
                // 수동으로 창을 닫을 필요 없이 즉시 Normal(게임플레이) 상태로 자동 복귀합니다.
                if (WorldGameStateManager.Instance.currentState == GameState.Inventory ||
                    WorldGameStateManager.Instance.currentState == GameState.Table)
                {
                    WorldGameStateManager.Instance.SetGamePlaySituation(GameState.Normal);
                }
            }

            // 정책 레이어(컷신 여부 등) 승인 후 이동 도메인으로 전달
            if (WorldGameStateManager.Instance.IsMovementAllowed())
            {
                playerLocomotionManager.OnMovementInputReceived(movementInput);
            }
        }

        internal void OnDodgeInputReceived()
        {
            // 1. 구르기 검문 (Gating)
            if (playerNetworkManager.isDead.Value || isPerformingAction) return;

            // 2. 자원 확인
            if (playerNetworkManager.currentStamina.Value <= 0) return;

            // 3. 도메인으로 위임
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
            // [정책 검문] 락온 상태가 아닐 경우 불필요한 레이캐스트 탐색 연산을 원천 봉쇄합니다.
            if (!playerNetworkManager.isLockedOn.Value) return;

            // 1. 시각적 전환 명령은 카메라로 배분 (부드러운 시선 이동)
            if (playerCamera != null)
            {
                playerCamera.SwitchLockOnTarget(direction);
            }

            // 2. 물리적/논리적 타겟 전환 명령은 전투 도메인으로 배분 (데이터 갱신)
            playerCombatManager.OnLockOnSwitchTargetInputReceived(direction);
        }

        #endregion

        #region [State Management] 생명주기 및 저장/불러오기 (Death & Persistence)

        public override IEnumerator ProcessDeathEvent(bool manuallySelectDeathAnimation = false)
        {
            // 사망 시 소울류 특유의 'YOU DIED' UI 팝업을 띄웁니다.
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

                // [최적화 적용] 부활 시 기본 자세(Empty)로 전이할 때 문자열이 아닌 Hash를 사용하여 GC(가비지 컬렉터) 생성을 방지합니다.
                playerAnimationManager.PlayTargetAnimation(AnimatorParameterHash.ActionState, false);
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

        /// <summary>
        /// 다른 유저가 내 화면에 스폰될 때(초기화 시), 해당 유저의 현재 락온 상태와 장비 상태를 강제로 읽어와 시각적 엇갈림을 맞춥니다.
        /// </summary>
        public void LoadOtherPlayerCharacterWhenJoiningServer()
        {
            playerNetworkManager.OnCurrentRightHandWeaponIDChange(0, playerNetworkManager.currentRightHandWeaponID.Value);
            playerNetworkManager.OnCurrentLeftHandWeaponIDChange(0, playerNetworkManager.currentLeftHandWeaponID.Value);

            if (playerNetworkManager.isLockedOn.Value)
            {
                playerNetworkManager.OnLockOnTargetIDChange(0, playerNetworkManager.currentTargetNetworkObjectID.Value);
            }
        }

        #endregion
    }
}