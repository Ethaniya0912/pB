using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerInputManager : MonoBehaviour
{
    public static PlayerInputManager Instance {  get; private set; }
    public PlayerManager player;
    // 목표에 대한 단계를 생각하자.
    // 1. 조이스틱의 밸류를 읽을 수 있는 방법을 찾자.
    // 2. 해당 값에 기반하여 캐릭터를 움직일 수 있도록 하자.

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
        if(playerControls != null)
        {
            playerControls.Disable();
        }
    }

    // arg0은 올드씬, arg1은 뉴씬
    private void OnSceneChange(Scene oldScene,  Scene newScene)
    {
        Debug.Log(WorldSaveGameManager.Instance.GetWorldSceneIndex());
        // 만약 우리가 월드씬을 로딩한다면, 플레이어 컨트롤를 활성화합니다.
        if (newScene.buildIndex == WorldSaveGameManager.Instance.GetWorldSceneIndex())
        {
            Instance.enabled = true;

            if(playerControls != null)
            {
                playerControls.Enable();
            }   
        }
        // 그렇지 않을 경우 메인메뉴에 존재한다는 뜻이며, 플레이어 컨트롤을 비활성화함.
        // 캐릭터 크리에이션 중에 캐릭터가 움직이지 않게 하기 위함.
        else
        {
            Instance.enabled = false;

            if(playerControls != null)
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

            // 액션
            playerControls.PlayerMoveMent.Movement.performed += i => movementInput = i.ReadValue<Vector2>();
            playerControls.PlayerAction.Dodge.performed += i => dodgeInput = true;
            playerControls.PlayerAction.SwitchRightWeapon.performed += i => switch_Right_Weapon_Input = true;
            playerControls.PlayerAction.SwitchLeftWeapon.performed += i => switch_Left_Weapon_Input = true;


            // 범퍼와 트리거
            playerControls.PlayerAction.RB.performed += i => RB_Input = true;
            playerControls.PlayerAction.RT.performed += i => RT_Input = true;
            playerControls.PlayerAction.HoldRT.performed += i => Hold_RT_Input = true;
            playerControls.PlayerAction.HoldRT.canceled += i => Hold_RT_Input = false;


            // 락온
            playerControls.PlayerAction.LockOn.performed += i => lockOn_Input = true;
            playerControls.PlayerAction.SeekLeftLockOnTarget.performed += i => lockOn_Left_Input = true;
            playerControls.PlayerAction.SeekRightLockOnTarget.performed += i => lockOn_Right_Input = true;
        }

        playerControls.Enable();
    }

    private void OnDestroy()
    {
        // 해당 오브젝트 파괴시, 이벤트 비구독.
        SceneManager.activeSceneChanged -= OnSceneChange;
    }

    // 윈도우가 아웃포커스되면 조종이 안됨.
    private void OnApplicationFocus(bool focus)
    {
        if (enabled)
        {
            if (focus)
            {
                playerControls.Enable();
            }
            else
            {
                playerControls.Disable();
            }
        }
    }

    private void Update()
    {
        HandleAllInputs();
    }
    
    private void HandleAllInputs()
    {
        HandleMovementInput();
        HandleCameraMovementInput();
        HandleDodgeInput();
        HandleRBInput();
        HandleRTInput();
        HandleHoldRTInput();
        HandleLockOnInput();
        HandleLockOnSwitchTargetInput();
        HandleSwitchRightWeaponInput();
        HandleSwitchLeftWeaponInput();
    }

    // 락온
    private void HandleLockOnInput()
    {
        // 타겟이 죽었는지 체크
        if (player.playerNetworkManager.isLockedOn.Value)
        {
            // 상대가 파괴되어 null 일 것 대비
            if (player.playerCombatManager.currentTarget == null)
                return;
            
            // 현재 타겟이 사망? (언락)
            if (player.playerCombatManager.currentTarget.characterNetworkManager.isDead.Value)
            {
                player.playerNetworkManager.isLockedOn.Value = false;
            }

            // 새로운 타겟을 찾기.

            // 코루틴이 동시에 여러개 진행 중첩이 되지 않도록 보장.
            if (lockOnCoroutine != null)
                StopCoroutine(lockOnCoroutine);

            lockOnCoroutine = StartCoroutine(PlayerCamera.Instance.WaitThenFindNewTarget());
            
        }

        if (lockOn_Input && player.playerNetworkManager.isLockedOn.Value)
        {
            Debug.Log($"{ lockOn_Input}, lockOn_Input && player.playerNetworkManager.isLockedOn.Value");
            lockOn_Input = false;
            PlayerCamera.Instance.ClearLockOnTargets();
            player.playerNetworkManager.isLockedOn.Value = false;
            // 이미 락온? (언락)
            return;
        }

        if (lockOn_Input && !player.playerNetworkManager.isLockedOn.Value)
        {
            Debug.Log($"{lockOn_Input}, lockOn_Input && !player.playerNetworkManager.isLockedOn.Value");
            lockOn_Input = false;
            // 락온

            // 레인지 무기 사용중이라면 락온 안함.

            PlayerCamera.Instance.HandleLocatingLockOnTargets();

            if (PlayerCamera.Instance.nearestLockOnTarget != null)
            {
                player.playerCombatManager.SetTarget(PlayerCamera.Instance.nearestLockOnTarget);
                // 가장 가까운 타겟이 널이 아니면 현재 대상으로 락온
                player.playerNetworkManager.isLockedOn.Value = true;
            }
        }
    }

    private void HandleLockOnSwitchTargetInput()
    {
        if (lockOn_Left_Input)
        {
            // 초기화
            lockOn_Left_Input = false;

            // 이미 락온 됫다면 실행.
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
            // 초기화
            lockOn_Right_Input = false;

            // 이미 락온 됫다면 실행.
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

    // 움직임관련

    private void HandleMovementInput()
    {
        verticalInput = movementInput.y;
        horizontalInput = movementInput.x;

        // 숫자의 절대값을 반환 (음수 없이 양수로만 반환시키기)
        moveAmount = Mathf.Clamp01(Mathf.Abs(verticalInput) + Mathf.Abs(horizontalInput));

        // 값을 clamp 해줘서 0,0.5,1로 고정되게 함.
        if (moveAmount <= 0.5 && moveAmount > 0)
        {
            //걷고있다는 인디케이터
            moveAmount = 0.5f;
        }
        else if (moveAmount >0.5 && moveAmount <= 1)
        {
            // 달리기 인디케이터
            moveAmount = 1;
        }

        if (player == null)
            return;

        // 수평에 0만 전달하는 이유는 락온 하지 않을 시 앞으로만 가게 하려고 함.
        if (!player.playerNetworkManager.isLockedOn.Value || player.playerNetworkManager.isSprinting.Value)
        {
            player.playerAnimationManager.UpdateAnimatorMovementParameters(0, moveAmount, player.playerNetworkManager.isSprinting.Value);
        }
        else
        {
            player.playerAnimationManager.UpdateAnimatorMovementParameters(horizontalInput, verticalInput, player.playerNetworkManager.isSprinting.Value);
        }

        // 수평에 0 말고 다른 것도 전달, 락온 한 상태.
    }

    private void HandleCameraMovementInput()
    {
        cameraVerticalInput = cameraInput.y;
        cameraHorizontalInput = cameraInput.x;
    }

    // 액션관련
    private void HandleDodgeInput()
    {
        if (dodgeInput == true)
        {
            // 닷지인풋이 트루일 경우, 다시 false 로 만들어 두번 함수가 실행되지않게함.
            dodgeInput = false;

            // TD : 미래에 UI가 활성화시 실행되지 않게해줌.

            // 닷지를 퍼폼하기.
            player.playerLocomotionManager.AttemptToPerformDodge();
        }
    }

    private void HandleRBInput()
    {
        if (RB_Input)
        {
            RB_Input = false;

            // TD : UI창이 열려있다면, 리턴하고 아무것도 안함.

            player.playerNetworkManager.SetCharacterActionHand(true); // RB_input 들어오면 항상 참.

            // TD : 양손이라면 양손 액션 사용

            player.playerCombatManager.PerformWeaponBasedAction
            (player.playerInventoryManager.currentRightHandWeapon.oh_RB_Action,
            player.playerInventoryManager.currentRightHandWeapon
            );
        }
    }

    private void HandleRTInput()
    {
        if (RT_Input)
        {
            RT_Input = false;

            // TD : UI창이 열려있다면, 리턴하고 아무것도 안함.

            player.playerNetworkManager.SetCharacterActionHand(true); // RB_input 들어오면 항상 참.

            // TD : 양손이라면 양손 액션 사용

            player.playerCombatManager.PerformWeaponBasedAction
            (player.playerInventoryManager.currentRightHandWeapon.oh_RT_Action,
            player.playerInventoryManager.currentRightHandWeapon
            );
        }
    }

    private void HandleHoldRTInput()
    {
        // 홀드/차지는 액션이 해당을 실행할 수 있는가(공격)일때만 체크함.
        if (player.isPerformingAction)
        {
            if (player.playerNetworkManager.isUsingRightHand.Value)
            {
                //차징 어택시 다른 유저도 애니메이션을 봐야함.
                player.playerNetworkManager.isChargingAttack.Value = Hold_RT_Input;
            }
        }
    }

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
}
