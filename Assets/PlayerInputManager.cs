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


    [Header("Player Movement Input")]
    [SerializeField] Vector2 movementInput;
    public float verticalInput;
    public float horizontalInput;
    public float moveAmount;

    [Header("Player Actioion Input")]
    [SerializeField] bool dodgeInput;

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
    }

    // arg0은 올드씬, arg1은 뉴씬
    private void OnSceneChange(Scene oldScene,  Scene newScene)
    {
        Debug.Log(WorldSaveGameManager.Instance.GetWorldSceneIndex());
        // 만약 우리가 월드씬을 로딩한다면, 플레이어 컨트롤를 활성화합니다.
        if (newScene.buildIndex == WorldSaveGameManager.Instance.GetWorldSceneIndex())
        {
            Instance.enabled = true;
        }
        // 그렇지 않을 경우 메인메뉴에 존재한다는 뜻이며, 플레이어 컨트롤을 비활성화함.
        // 캐릭터 크리에이션 중에 캐릭터가 움직이지 않게 하기 위함.
        else
        {
            Instance.enabled = false;
        }
    }

    private void OnEnable()
    {
        if (playerControls == null)
        {
            playerControls = new PlayerControls();

            playerControls.PlayerMoveMent.Movement.performed += i => movementInput = i.ReadValue<Vector2>();
            playerControls.PlayerAction.Dodge.performed += i => dodgeInput = true;
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
        player.playerAnimationManager.UpdateAnimatorMovementParameters(0, moveAmount);

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
}
