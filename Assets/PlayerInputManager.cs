using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 플레이어의 모든 입력을 관리하며, 인벤토리 상태에 따라 액션을 제어합니다.
/// </summary>
public class PlayerInputManager : MonoBehaviour
{
    public static PlayerInputManager Instance { get; private set; }
    public PlayerManager player;

    PlayerControls playerControls;

    [Header("Camera Movement Input")]
    [SerializeField] Vector2 cameraInput;
    public float cameraVerticalInput;
    public float cameraHorizontalInput;

    [Header("Lock On Input")]
    [SerializeField] bool lockOn_Input;
    [SerializeField] bool lockOn_Left_Input;
    [SerializeField] bool lockOn_Right_Input;
    private Coroutine lockOnCoroutine;

    [Header("Player Movement Input")]
    [SerializeField] Vector2 movementInput;
    public float verticalInput;
    public float horizontalInput;
    public float moveAmount;

    [Header("Player Action Input")]
    [SerializeField] bool dodgeInput;
    [SerializeField] bool RB_Input = false;
    [SerializeField] bool RT_Input = false;
    [SerializeField] bool Hold_RT_Input = false;
    [SerializeField] bool switch_Right_Weapon_Input = false;
    [SerializeField] bool switch_Left_Weapon_Input = false;
    [SerializeField] bool interaction_Input = false;
    [SerializeField] bool inventory_Input = false; // [New] 인벤토리 입력 플래그
    public bool alt_Input = false;                // [New] Alt 키 상태 플래그

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // DontDestroyOnLoad가 인스턴스를 비활성화 하기 전 로드되도록 해야함.
        DontDestroyOnLoad(gameObject);

        // 씬이 바뀌면 해당 로직을 돌리기.
        SceneManager.activeSceneChanged += OnSceneChange;

        Instance.enabled = false;
        if (playerControls != null)
        {
            playerControls.Disable();
        }
    }

    private void OnSceneChange(Scene oldScene, Scene newScene)
    {
        // 월드 씬일 때만 입력을 활성화하여 캐릭터 크리에이션 등에서의 오작동 방지
        if (WorldSaveGameManager.Instance.IsWorldScene(newScene.buildIndex))
        {
            Instance.enabled = true;

            if (playerControls != null)
            {
                playerControls.Enable();
            }
        }
        else
        {
            Instance.enabled = false;

            if (playerControls != null)
            {
                playerControls.Disable();
            }
        }
    }

    private void OnEnable()
    {
        if (playerControls == null)
        {
            playerControls = new PlayerControls();

            // 이동 및 카메라
            playerControls.PlayerMoveMent.Movement.performed += i => movementInput = i.ReadValue<Vector2>();
            playerControls.PlayerCamera.Movement.performed += i => cameraInput = i.ReadValue<Vector2>();

            // 액션
            playerControls.PlayerAction.Dodge.performed += i => dodgeInput = true;
            playerControls.PlayerAction.SwitchRightWeapon.performed += i => switch_Right_Weapon_Input = true;
            playerControls.PlayerAction.SwitchLeftWeapon.performed += i => switch_Left_Weapon_Input = true;

            // 공격 (범퍼와 트리거)
            playerControls.PlayerAction.RB.performed += i => RB_Input = true;
            playerControls.PlayerAction.RT.performed += i => RT_Input = true;
            playerControls.PlayerAction.HoldRT.performed += i => Hold_RT_Input = true;
            playerControls.PlayerAction.HoldRT.canceled += i => Hold_RT_Input = false;

            // 락온
            playerControls.PlayerAction.LockOn.performed += i => lockOn_Input = true;
            playerControls.PlayerAction.SeekLeftLockOnTarget.performed += i => lockOn_Left_Input = true;
            playerControls.PlayerAction.SeekRightLockOnTarget.performed += i => lockOn_Right_Input = true;

            // 인터렉션 및 인벤토리
            playerControls.PlayerAction.Interaction.performed += i => interaction_Input = true;
            playerControls.PlayerAction.Inventory.performed += i => inventory_Input = true; // [New] 'I' 키 연동
        }

        playerControls.Enable();
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChange;
    }

    private void OnApplicationFocus(bool focus)
    {
        if (enabled)
        {
            if (focus) playerControls.Enable();
            else playerControls.Disable();
        }
    }

    private void Update()
    {
        if (player == null) return;
        HandleAllInputs();
        HandleCursorState(); // [New] 마우스 커서 상태 제어 로직 추가
    }

    private void HandleAllInputs()
    {
        // 1. 기본 움직임 및 카메라 입력은 항상 처리 (인벤토리 자동 닫기 감지를 위해)
        HandleMovementInput();
        HandleCameraMovementInput();

        // 2. 인벤토리 토글 입력 처리
        HandleInventoryInput();

        // [New] Alt 키 입력 실시간 감지
        alt_Input = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        // 3. 인벤토리가 열려 있다면 UI 조작 모드이므로 게임 액션(공격, 닷지 등) 차단
        if (player.playerInventoryManager.isInventoryOpen)
        {
            // 인벤토리 상태에서는 아래 액션 인풋들을 초기화하고 리턴함
            dodgeInput = false;
            RB_Input = false;
            RT_Input = false;
            lockOn_Input = false;
            interaction_Input = false;
            return;
        }

        // 4. 일반 게임 상태에서의 액션 처리
        HandleDodgeInput();
        HandleRBInput();
        HandleRTInput();
        HandleHoldRTInput();
        HandleLockOnInput();
        HandleLockOnSwitchTargetInput();
        HandleSwitchRightWeaponInput();
        HandleSwitchLeftWeaponInput();
        HandleInteractionInput();
    }

    /// <summary>
    /// 인벤토리 활성화 상태 혹은 Alt 키 입력 여부에 따라 마우스 커서의 잠금 및 가시성을 제어합니다.
    /// </summary>
    private void HandleCursorState()
    {
        if (player.playerInventoryManager.isInventoryOpen || alt_Input)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            // 가방이나 아이템을 드래그 중인 경우에는 커서를 유지하고, 그 외에는 잠금 처리
            var raycaster = player.GetComponentInChildren<Inventory3DRaycaster>();
            if (raycaster != null && !raycaster.GetIsDragging())
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    #region Inventory Input
    private void HandleInventoryInput()
    {
        if (inventory_Input)
        {
            inventory_Input = false;
            player.playerInventoryManager.ToggleInventory();
        }
    }
    #endregion

    #region Locomotion & Camera
    private void HandleMovementInput()
    {
        verticalInput = movementInput.y;
        horizontalInput = movementInput.x;

        moveAmount = Mathf.Clamp01(Mathf.Abs(verticalInput) + Mathf.Abs(horizontalInput));

        if (moveAmount <= 0.5f && moveAmount > 0) moveAmount = 0.5f;
        else if (moveAmount > 0.5f && moveAmount <= 1) moveAmount = 1;

        if (player == null) return;

        if (!player.playerNetworkManager.isLockedOn.Value || player.playerNetworkManager.isSprinting.Value)
        {
            player.playerAnimationManager.UpdateAnimatorMovementParameters(0, moveAmount, player.playerNetworkManager.isSprinting.Value);
        }
        else
        {
            player.playerAnimationManager.UpdateAnimatorMovementParameters(horizontalInput, verticalInput, player.playerNetworkManager.isSprinting.Value);
        }
    }

    private void HandleCameraMovementInput()
    {
        cameraVerticalInput = cameraInput.y;
        cameraHorizontalInput = cameraInput.x;
    }
    #endregion

    #region Action Inputs (Dodge, Combat, Interact)
    private void HandleDodgeInput()
    {
        if (dodgeInput)
        {
            dodgeInput = false;
            player.playerLocomotionManager.AttemptToPerformDodge();
        }
    }

    private void HandleRBInput()
    {
        if (RB_Input)
        {
            RB_Input = false;

            player.playerNetworkManager.SetCharacterActionHand(true);

            if (player.playerInteractionManager.currentlyHeldObject != null)
            {
                player.playerInteractionManager.ReleaseGrabbedObject();
                return;
            }

            player.playerCombatManager.PerformWeaponBasedAction(
                player.playerInventoryManager.currentRightHandWeapon.oh_RB_Action,
                player.playerInventoryManager.currentRightHandWeapon);
        }
    }

    private void HandleRTInput()
    {
        if (RT_Input)
        {
            RT_Input = false;
            player.playerNetworkManager.SetCharacterActionHand(true);

            player.playerCombatManager.PerformWeaponBasedAction(
                player.playerInventoryManager.currentRightHandWeapon.oh_RT_Action,
                player.playerInventoryManager.currentRightHandWeapon);
        }
    }

    private void HandleHoldRTInput()
    {
        if (player.isPerformingAction)
        {
            if (player.playerNetworkManager.isUsingRightHand.Value)
            {
                player.playerNetworkManager.isChargingAttack.Value = Hold_RT_Input;
            }
        }
    }

    private void HandleInteractionInput()
    {
        if (interaction_Input)
        {
            interaction_Input = false;
            player.playerInteractionManager.Interact();
        }
    }
    #endregion

    #region Weapon Switching
    private void HandleSwitchRightWeaponInput()
    {
        if (switch_Right_Weapon_Input)
        {
            switch_Right_Weapon_Input = false;
            player.playerEquipmentManager.SwitchRightWeapon();
        }
    }

    private void HandleSwitchLeftWeaponInput()
    {
        if (switch_Left_Weapon_Input)
        {
            switch_Left_Weapon_Input = false;
            player.playerEquipmentManager.SwitchLeftWeapon();
        }
    }
    #endregion

    #region Lock On
    private void HandleLockOnInput()
    {
        if (player.playerNetworkManager.isLockedOn.Value)
        {
            if (player.playerCombatManager.currentTarget == null) return;

            if (player.playerCombatManager.currentTarget.characterNetworkManager.isDead.Value)
            {
                player.playerNetworkManager.isLockedOn.Value = false;
            }

            if (lockOnCoroutine != null) StopCoroutine(lockOnCoroutine);
            lockOnCoroutine = StartCoroutine(PlayerCamera.Instance.WaitThenFindNewTarget());
        }

        if (lockOn_Input && player.playerNetworkManager.isLockedOn.Value)
        {
            lockOn_Input = false;
            PlayerCamera.Instance.ClearLockOnTargets();
            player.playerNetworkManager.isLockedOn.Value = false;
            return;
        }

        if (lockOn_Input && !player.playerNetworkManager.isLockedOn.Value)
        {
            lockOn_Input = false;
            PlayerCamera.Instance.HandleLocatingLockOnTargets();

            if (PlayerCamera.Instance.nearestLockOnTarget != null)
            {
                player.playerCombatManager.SetTarget(PlayerCamera.Instance.nearestLockOnTarget);
                player.playerNetworkManager.isLockedOn.Value = true;
            }
        }
    }

    private void HandleLockOnSwitchTargetInput()
    {
        if (lockOn_Left_Input)
        {
            lockOn_Left_Input = false;
            if (player.playerNetworkManager.isLockedOn.Value)
            {
                PlayerCamera.Instance.HandleLocatingLockOnTargets();
                if (PlayerCamera.Instance.leftLockOnTarget != null)
                {
                    player.playerCombatManager.SetTarget(PlayerCamera.Instance.leftLockOnTarget);
                }
            }
        }

        if (lockOn_Right_Input)
        {
            lockOn_Right_Input = false;
            if (player.playerNetworkManager.isLockedOn.Value)
            {
                PlayerCamera.Instance.HandleLocatingLockOnTargets();
                if (PlayerCamera.Instance.rightLockOnTarget != null)
                {
                    player.playerCombatManager.SetTarget(PlayerCamera.Instance.rightLockOnTarget);
                }
            }
        }
    }
    #endregion
}