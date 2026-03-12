using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TDA.Character.Player; // PlayerManager 참조를 위해 추가

/// <summary>
/// [Layer 1: Input Layer]
/// InputAction Editor에서 생성된 PlayerControls를 기반으로 하드웨어 입력을 수집합니다.
/// 이 클래스는 어떠한 게임 로직(타겟팅, 공격 판정 등)도 직접 수행하지 않습니다.
/// 오직 발생한 '사건(Event)'을 해당 도메인 매니저(Combat, Locomotion 등)에게 전달하는 책임만 집니다.
/// 모든 함수 호출은 'On___InputReceived' 형식을 준수하여 통일성을 확보합니다.
/// </summary>

public class PlayerInputManager : MonoBehaviour
{
    public static PlayerInputManager Instance { get; private set; }
    public PlayerManager player;

    PlayerControls playerControls;

    [Header("Movement Signal Data")]
    public Vector2 movementInput;
    public float verticalInput;
    public float horizontalInput;
    public float moveAmount;

    [Header("Camera Signal Data")]
    public Vector2 cameraInput;


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

    // arg0은 올드씬, arg1은 뉴씬
    private void OnSceneChange(Scene oldScene, Scene newScene)
    {
        Debug.Log(WorldSaveGameManager.Instance.GetWorldSceneIndex());
        // 만약 우리가 월드씬을 로딩한다면, 플레이어 컨트롤를 활성화합니다.
        //if (newScene.buildIndex == WorldSaveGameManager.Instance.GetWorldSceneIndex())
        if (WorldSaveGameManager.Instance.IsWorldScene(newScene.buildIndex))
        {
            Instance.enabled = true;

            if (playerControls != null)
            {
                playerControls.Enable();
            }
        }
        // 그렇지 않을 경우 메인메뉴에 존재한다는 뜻이며, 플레이어 컨트롤을 비활성화함.
        // 캐릭터 크리에이션 중에 캐릭터가 움직이지 않게 하기 위함.
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

            // 모든 신호는 중앙 라우터인 PlayerManager의 On___InputReceived로 집결.
            playerControls.PlayerMoveMent.Movement.performed += i => movementInput = i.ReadValue<Vector2>();
            playerControls.PlayerCamera.Movement.performed += i => cameraInput = i.ReadValue<Vector2>();

            // 단일 액션 배포 (Trigger 기반) 
            playerControls.PlayerAction.Dodge.performed += i => player.OnDodgeInputReceived();
            playerControls.PlayerAction.SwitchLeftWeapon.performed += i => player.OnSwitchWeaponInputReceived(SwithchWeaponSide.Left);
            playerControls.PlayerAction.SwitchRightWeapon.performed += i => player.OnSwitchWeaponInputReceived(SwithchWeaponSide.Right);
            //playerControls.PlayerAction.SwitchRightWeapon.performed += i => switch_Right_Weapon_Input = true; 나중에 변경.
            //playerControls.PlayerAction.SwitchLeftWeapon.performed += i => switch_Left_Weapon_Input = true;


            // RB 버튼 등은 다중 기능 수행 -> 라우터(PlayerManager)에신호.
            playerControls.PlayerAction.RB.performed += i => player.OnRBInputReceived();
            playerControls.PlayerAction.RT.performed += i => player.OnRTInputReceived();

            //playerControls.PlayerAction.HoldRT.performed += i => Hold_RT_Input = true;
            //playerControls.PlayerAction.HoldRT.canceled += i => Hold_RT_Input = false;


            // 락온
            playerControls.PlayerAction.LockOn.performed += i => player.OnLockOnInputReceived();
            playerControls.PlayerAction.SeekLeftLockOnTarget.performed += i => player.OnLockOnSwitchTargetInputReceived(LockOnDirection.Left);
            playerControls.PlayerAction.SeekRightLockOnTarget.performed += i => player.OnLockOnSwitchTargetInputReceived(LockOnDirection.Right);
            //playerControls.PlayerAction.SeekLeftLockOnTarget.performed += i => lockOn_Left_Input = true;
            //playerControls.PlayerAction.SeekRightLockOnTarget.performed += i => lockOn_Right_Input = true;

            // 인터렉션
            playerControls.PlayerAction.Interaction.performed += i => player.OnInteractionInputReceived();

            // 남규 추가
            playerControls.PlayerAction.Inventory.performed += i => player.OnInventoryInputReceived();

            // 남규 추가
            // [수정] Alt 입력: 누를 때(true)와 뗄 때(false)를 모두 전달
            playerControls.UI.Alt.performed += i => player.OnAltInputReceived(true);
            playerControls.UI.Alt.canceled += i => player.OnAltInputReceived(false);
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
        //HandleAllInputs();
        if (player == null || !player.IsOwner) return;

        // 연속적인 데이터를 라우터로 전달.
        player.OnMovementInputReceived(movementInput);
        player.OnCameraInputReceived(cameraInput);

        // 260218 남규 추가, 이동 블러 체크용
        HandleMovementInput();
    }

    // 260218 남규 주석해제, 이동 블러 체크용
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
        else if (moveAmount > 0.5 && moveAmount <= 1)
        {
            // 달리기 인디케이터
            moveAmount = 1;
        }

        if (player == null)
            return;
    }
}