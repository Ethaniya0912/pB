using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using Steamworks;
using CaveSystem.Multiplayer;

public class TitleScreenManager : MonoBehaviour
{
    public static TitleScreenManager Instance;

    // 네트워크 접속 형태를 구분하기 위한 Enum
    public enum NetworkConnectionType
    {
        Singleplayer,
        Host,
        Client
    }

    [Header("Network State")]
    public NetworkConnectionType currentConnectionType = NetworkConnectionType.Singleplayer;

    [Header("Press Start Screen")]
    [SerializeField] GameObject pressStartUI;
    [SerializeField] Button pressStartButton;

    [Header("Menus")]
    [SerializeField] GameObject titleScreenMainMenu;
    [SerializeField] GameObject titleScreenLoadMenu;
    [SerializeField] GameObject titleScreenMultiplayerMenu; // 멀티플레이어 메뉴 패널

    [Header("Buttons")]
    [SerializeField] Button mainMenuNewGameButton;
    [SerializeField] Button loadMenuReturnButton;
    [SerializeField] Button mainMenuLoadGameButton;
    [SerializeField] Button deleteCharacterPopUpConfirmButton;

    [Header("Multiplayer Buttons")]
    [SerializeField] Button mainMenuMultiplayerButton; // 메인 -> 멀티 메뉴 이동 버튼
    [SerializeField] Button multiplayerCreateRoomButton; // 방 만들기 (Host)
    [SerializeField] Button multiplayerJoinRoomButton;   // 참여하기 (Client)
    [SerializeField] Button multiplayerReturnButton;     // 멀티메뉴 -> 메인메뉴 뒤로 가기
    [SerializeField] Button loadMenuStartGameButton;     // 선택된 슬롯으로 게임 시작 버튼

    [Header("Pop Ups")]
    [SerializeField] GameObject noCharacterSlotsPopup;
    [SerializeField] Button noCharacterSlotsOkayButton;
    [SerializeField] GameObject deleteCharacterSlotPopUp;
    [SerializeField] GameObject waitingForInvitePopUp;   // 초대 대기 중 팝업
    [SerializeField] Button closeWaitingPopUpButton;

    [Header("Save Slot Buttons")]
    [SerializeField] Button[] saveSlotButtons = new Button[5]; // 0~4번 슬롯 버튼 배열

    [Header("Save Slots")]
    public CharacterSlots currentSelectedSlot = CharacterSlots.No_Slot;

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
        // 스크립트가 시작될 때 모든 버튼 이벤트를 일괄 등록합니다.
        RegisterButtonEvents();
    }

    /// <summary>
    /// 인스펙터 하드코딩 대신 코드로 버튼 이벤트를 바인딩합니다.
    /// </summary>
    private void RegisterButtonEvents()
    {
        if (pressStartButton != null) pressStartButton.onClick.AddListener(OpenMainMenuFromPressStart);

        if (mainMenuNewGameButton != null) mainMenuNewGameButton.onClick.AddListener(StartNewGame);
        if (mainMenuLoadGameButton != null) mainMenuLoadGameButton.onClick.AddListener(OpenLoadGameMenu);
        if (mainMenuMultiplayerButton != null) mainMenuMultiplayerButton.onClick.AddListener(OpenMultiplayerMenu);

        if (multiplayerCreateRoomButton != null) multiplayerCreateRoomButton.onClick.AddListener(StartCreateRoomFlow);
        if (multiplayerJoinRoomButton != null) multiplayerJoinRoomButton.onClick.AddListener(StartJoinRoomFlow);
        if (multiplayerReturnButton != null) multiplayerReturnButton.onClick.AddListener(CloseMultiplayerMenu);

        if (loadMenuReturnButton != null) loadMenuReturnButton.onClick.AddListener(CloseLoadGameMenu);

        // [핵심] loadMenuStartGameButton은 싱글/호스트 모드를 모두 처리하는 통합 함수에 연결합니다.
        // Title Screen Load Menu 와 Lobby Screen 의 Start Game 버튼은 역할이 다릅니다.
        // - Title Screen Load Menu → Start Game Button : 여기(AttemptToStartGameWithSelectedSlot)에 연결
        // - Lobby Screen → Host Controls → Start Game Button : LobbyUIManager.startGameButton에 연결 (별도)
        if (loadMenuStartGameButton != null) loadMenuStartGameButton.onClick.AddListener(AttemptToStartGameWithSelectedSlot);

        if (deleteCharacterPopUpConfirmButton != null) deleteCharacterPopUpConfirmButton.onClick.AddListener(DeleteCharacterSlot);
        if (noCharacterSlotsOkayButton != null) noCharacterSlotsOkayButton.onClick.AddListener(CloseNoFreeCharacterSlotsPopUp);
        if (closeWaitingPopUpButton != null) closeWaitingPopUpButton.onClick.AddListener(CloseWaitingForInvitePopUp);

        // 배열로 관리되는 세이브 슬롯 버튼들을 일괄 등록
        if (saveSlotButtons != null)
        {
            for (int i = 0; i < saveSlotButtons.Length; i++)
            {
                int slotIndex = i; // 람다 캡처를 위한 로컬 변수
                if (saveSlotButtons[slotIndex] != null)
                {
                    saveSlotButtons[slotIndex].onClick.AddListener(() =>
                    {
                        CharacterSlots slot = GetCharacterSlotFromIndex(slotIndex);
                        SelectCharacterSlot(slot);
                    });
                }
            }
        }

        Debug.Log("[TitleScreenManager][RegisterButtonEvents] ✔ 모든 버튼 이벤트 등록 완료.");
    }

    private void OnDestroy()
    {
        // 씬 파괴 시 메모리 누수 방지를 위한 리스너 일괄 해제
        if (pressStartButton != null) pressStartButton.onClick.RemoveAllListeners();
        if (mainMenuNewGameButton != null) mainMenuNewGameButton.onClick.RemoveAllListeners();
        if (mainMenuLoadGameButton != null) mainMenuLoadGameButton.onClick.RemoveAllListeners();
        if (mainMenuMultiplayerButton != null) mainMenuMultiplayerButton.onClick.RemoveAllListeners();
        if (multiplayerCreateRoomButton != null) multiplayerCreateRoomButton.onClick.RemoveAllListeners();
        if (multiplayerJoinRoomButton != null) multiplayerJoinRoomButton.onClick.RemoveAllListeners();
        if (multiplayerReturnButton != null) multiplayerReturnButton.onClick.RemoveAllListeners();
        if (loadMenuReturnButton != null) loadMenuReturnButton.onClick.RemoveAllListeners();
        if (loadMenuStartGameButton != null) loadMenuStartGameButton.onClick.RemoveAllListeners();
        if (deleteCharacterPopUpConfirmButton != null) deleteCharacterPopUpConfirmButton.onClick.RemoveAllListeners();
        if (noCharacterSlotsOkayButton != null) noCharacterSlotsOkayButton.onClick.RemoveAllListeners();
        if (closeWaitingPopUpButton != null) closeWaitingPopUpButton.onClick.RemoveAllListeners();

        if (saveSlotButtons != null)
        {
            foreach (var btn in saveSlotButtons)
            {
                if (btn != null) btn.onClick.RemoveAllListeners();
            }
        }
    }

    #region Main & Multi Menu Flow

    public void OpenMainMenuFromPressStart()
    {
        // Press Start UI 숨기고 메인 메뉴 열기
        if (pressStartUI != null) pressStartUI.SetActive(false);
        if (titleScreenMainMenu != null) titleScreenMainMenu.SetActive(true);
        if (mainMenuNewGameButton != null) mainMenuNewGameButton.Select();
    }

    public void OpenMultiplayerMenu()
    {
        // 메인메뉴 닫기
        titleScreenMainMenu.SetActive(false);
        // 멀티플레이어 메뉴 열기
        titleScreenMultiplayerMenu.SetActive(true);
        multiplayerCreateRoomButton.Select();
    }

    public void CloseMultiplayerMenu()
    {
        // 멀티플레이어 메뉴 닫기
        titleScreenMultiplayerMenu.SetActive(false);
        // 메인 메뉴 열기
        titleScreenMainMenu.SetActive(true);
        mainMenuMultiplayerButton.Select();
    }

    public void StartCreateRoomFlow()
    {
        // 호스트 모드로 설정
        currentConnectionType = NetworkConnectionType.Host;
        // 멀티플레이어 메뉴 닫고 캐릭터 로드 메뉴 열기 (방장용)
        titleScreenMultiplayerMenu.SetActive(false);
        titleScreenLoadMenu.SetActive(true);
        loadMenuReturnButton.Select();

        Debug.Log("[TitleScreenManager][StartCreateRoomFlow] ✔ Host 모드로 설정. 로드 메뉴 오픈. " +
                  "슬롯 선택 없이 Start Game을 누르면 자동으로 빈 슬롯을 생성합니다.");
    }

    public void StartJoinRoomFlow()
    {
        // 클라이언트 모드로 설정
        currentConnectionType = NetworkConnectionType.Client;
        titleScreenMultiplayerMenu.SetActive(false);

        // [수정된 부분] 초대 대기 팝업 대신 LobbyUIManager의 방 목록 창을 엽니다!
        if (LobbyUIManager.Instance != null)
        {
            LobbyUIManager.Instance.OpenRoomBrowser();
        }
        else
        {
            Debug.LogError("LobbyUIManager를 찾을 수 없습니다.");
        }
    }

    public void OpenLoadGameMenu()
    {
        // 메인 메뉴에서 Load를 누르면 기본적으로 싱글플레이어 모드로 진입
        currentConnectionType = NetworkConnectionType.Singleplayer;

        // 메인메뉴 닫기 (이전 코드의 중복 SetActive 오류 방지)
        titleScreenMainMenu.SetActive(false);
        titleScreenMultiplayerMenu.SetActive(false);

        // 로딩 메뉴 열기
        titleScreenLoadMenu.SetActive(true);

        // 리턴 슬롯을 찾고 자동 셀렉트하기.
        loadMenuReturnButton.Select();
    }

    public void CloseLoadGameMenu()
    {
        // 로드 메뉴 닫기.
        titleScreenLoadMenu.SetActive(false);

        // 진입 경로에 맞춰 이전 메뉴 열기.
        if (currentConnectionType == NetworkConnectionType.Singleplayer)
        {
            // 메인 메뉴 열기.
            titleScreenMainMenu.SetActive(true);
            // 로드 버튼 고르기.
            mainMenuLoadGameButton.Select();
        }
        else
        {
            // 멀티플레이어 메뉴 열기.
            titleScreenMultiplayerMenu.SetActive(true);
            multiplayerCreateRoomButton.Select();
        }
    }

    /// <summary>
    /// 로비로 진입할 때 캔버스 자체를 꺼버려서 배경 이미지까지 확실히 가려지게 합니다.
    /// </summary>
    public void HideTitleScreen()
    {
        Debug.Log("[TitleScreenManager] 타이틀 화면 UI 캔버스를 끕니다.");
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null) canvas.enabled = false;
    }

    /// <summary>
    /// 로비를 빠져나와 다시 메인 메뉴로 돌아올 때 캔버스를 켭니다.
    /// </summary>
    public void ShowTitleScreen()
    {
        Debug.Log("[TitleScreenManager] 타이틀 화면 UI를 다시 표시합니다.");
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null) canvas.enabled = true;

        // 초기 화면으로 복구
        if (waitingForInvitePopUp) waitingForInvitePopUp.SetActive(false);
        if (titleScreenLoadMenu) titleScreenLoadMenu.SetActive(false);
        if (titleScreenMultiplayerMenu) titleScreenMultiplayerMenu.SetActive(false);
        if (titleScreenMainMenu) titleScreenMainMenu.SetActive(true);
    }

    #endregion

    #region Game Start Logic

    public void StartNetworkAsHost()
    {
        // 기존 버튼 직접 연결용 예비 기능 (현재는 AttemptToStartGameWithSelectedSlot 로직으로 흡수됨)
        if (SteamLobbyManager.Instance != null)
        {
            SteamLobbyManager.Instance.StartHostWithLobby();
        }
        else
        {
            NetworkManager.Singleton.StartHost();
        }
    }

    public void StartNewGame()
    {
        currentConnectionType = NetworkConnectionType.Singleplayer;

        // 싱글플레이어라도 NGO에서는 Host 모드로 작동해야 캐릭터가 정상 스폰됩니다.
        if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.StartHost();
        }

        WorldSaveGameManager.Instance.AttemptToCreateNewGame();
    }

    /// <summary>
    /// Title Screen Load Menu의 Start Game 버튼에 연결된 핵심 함수.
    ///
    /// [싱글플레이어] currentSelectedSlot이 반드시 선택되어 있어야 합니다.
    ///   슬롯 미선택 시 경고 후 중단합니다.
    ///
    /// [호스트(멀티)] 슬롯이 선택되어 있으면 그 슬롯을 사용하고,
    ///   선택되어 있지 않으면 WorldSaveGameManager.PrepareOrCreateSlotForLobby()를 통해
    ///   자동으로 빈 슬롯을 생성하여 진행합니다.
    ///   즉, Create Room 후 슬롯 목록이 비어있어도 Start Game이 정상 동작합니다.
    /// </summary>
    public void AttemptToStartGameWithSelectedSlot()
    {
        // ── [DEBUG] 버튼 클릭 수신 확인 (항상 출력)
        Debug.Log($"[TitleScreenManager][AttemptToStartGameWithSelectedSlot] ▶ START GAME 버튼 클릭 감지됨. " +
                  $"currentConnectionType={currentConnectionType}, " +
                  $"currentSelectedSlot={currentSelectedSlot}");

        // ── [DEBUG] 의존 오브젝트 상태 확인
        if (WorldSaveGameManager.Instance == null)
        {
            Debug.LogError("[TitleScreenManager][AttemptToStartGameWithSelectedSlot] ✖ WorldSaveGameManager.Instance가 NULL입니다! " +
                           "씬에 WorldSaveGameManager 오브젝트가 있는지 확인하세요.");
            return;
        }

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[TitleScreenManager][AttemptToStartGameWithSelectedSlot] ✖ NetworkManager.Singleton이 NULL입니다!");
            return;
        }

        switch (currentConnectionType)
        {
            // ──────────────────────────────────────────────────────────────────
            // [싱글플레이어] 반드시 슬롯을 선택해야 진행 가능
            // ──────────────────────────────────────────────────────────────────
            case NetworkConnectionType.Singleplayer:

                Debug.Log("[TitleScreenManager][AttemptToStartGameWithSelectedSlot] [싱글플레이어] 경로 진입.");

                if (currentSelectedSlot == CharacterSlots.No_Slot)
                {
                    Debug.LogWarning("[TitleScreenManager][AttemptToStartGameWithSelectedSlot] ✖ [싱글] 슬롯 미선택 → 중단. " +
                                     "슬롯 버튼을 먼저 클릭하세요.");
                    return;
                }

                Debug.Log($"[TitleScreenManager][AttemptToStartGameWithSelectedSlot] ✔ [싱글] 슬롯 선택 확인: {currentSelectedSlot}");

                // 선택한 슬롯을 세이브 매니저에 알림
                WorldSaveGameManager.Instance.currentCharacterSlotBeingUsed = currentSelectedSlot;

                // 싱글플레이어도 호스트 권한으로 엔진 구동
                if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient)
                {
                    Debug.Log("[TitleScreenManager][AttemptToStartGameWithSelectedSlot] [싱글] NGO StartHost() 호출.");
                    NetworkManager.Singleton.StartHost();
                }
                else
                {
                    Debug.Log($"[TitleScreenManager][AttemptToStartGameWithSelectedSlot] [싱글] NGO 이미 실행 중. " +
                              $"IsServer={NetworkManager.Singleton.IsServer}, IsClient={NetworkManager.Singleton.IsClient}");
                }

                Debug.Log("[TitleScreenManager][AttemptToStartGameWithSelectedSlot] ✔ [싱글] WorldSaveGameManager.LoadGame() 호출.");
                // 해당 슬롯에 저장된 씬과 캐릭터 정보 즉시 로드
                WorldSaveGameManager.Instance.LoadGame();
                break;

            // ──────────────────────────────────────────────────────────────────
            // [호스트(멀티)] 슬롯이 없으면 자동 생성 후 로비 진입
            // ──────────────────────────────────────────────────────────────────
            case NetworkConnectionType.Host:

                Debug.Log("[TitleScreenManager][AttemptToStartGameWithSelectedSlot] [호스트] 경로 진입.");

                // 슬롯이 선택된 경우: 해당 슬롯을 세이브 매니저에 전달
                if (currentSelectedSlot != CharacterSlots.No_Slot)
                {
                    Debug.Log($"[TitleScreenManager][AttemptToStartGameWithSelectedSlot] ✔ [호스트] 선택된 슬롯 사용: {currentSelectedSlot}");
                    WorldSaveGameManager.Instance.currentCharacterSlotBeingUsed = currentSelectedSlot;
                    WorldSaveGameManager.Instance.currentCharacterData =
                        WorldSaveGameManager.Instance.GetCharacterDataForSlot(currentSelectedSlot);

                    Debug.Log($"[TitleScreenManager][AttemptToStartGameWithSelectedSlot] currentCharacterData=" +
                              $"{(WorldSaveGameManager.Instance.currentCharacterData == null ? "NULL (슬롯에 파일 없음 - 자동생성으로 폴백)" : "로드 성공")}");

                    // 슬롯은 선택됐지만 파일이 없는 경우(빈 슬롯 클릭) → 자동 생성으로 폴백
                    if (WorldSaveGameManager.Instance.currentCharacterData == null)
                    {
                        Debug.LogWarning("[TitleScreenManager][AttemptToStartGameWithSelectedSlot] ⚠ [호스트] 선택된 슬롯에 저장 파일이 없습니다. " +
                                         "PrepareOrCreateSlotForLobby()로 자동 생성합니다.");
                        bool fallbackReady = WorldSaveGameManager.Instance.PrepareOrCreateSlotForLobby();
                        if (!fallbackReady)
                        {
                            Debug.LogError("[TitleScreenManager][AttemptToStartGameWithSelectedSlot] ✖ [호스트] 자동 생성도 실패. 모든 슬롯이 꽉 찼습니다.");
                            DisplayNofreeCharacterSlotPopUp();
                            return;
                        }
                    }
                }
                // 슬롯이 선택되지 않은 경우: 자동으로 빈 슬롯 생성
                else
                {
                    Debug.Log("[TitleScreenManager][AttemptToStartGameWithSelectedSlot] [호스트] 슬롯 미선택 → PrepareOrCreateSlotForLobby() 자동 생성 시도.");

                    bool slotReady = WorldSaveGameManager.Instance.PrepareOrCreateSlotForLobby();

                    if (!slotReady)
                    {
                        Debug.LogError("[TitleScreenManager][AttemptToStartGameWithSelectedSlot] ✖ [호스트] PrepareOrCreateSlotForLobby() = false. " +
                                       "슬롯 01~05 모두 파일이 존재합니다. 팝업을 표시합니다.");
                        DisplayNofreeCharacterSlotPopUp();
                        return;
                    }

                    Debug.Log($"[TitleScreenManager][AttemptToStartGameWithSelectedSlot] ✔ [호스트] 자동 슬롯 생성 완료. " +
                              $"슬롯={WorldSaveGameManager.Instance.currentCharacterSlotBeingUsed}");
                }

                // 호스트는 스팀 로비 생성 후, 대기방 UI(LobbyUIManager)를 띄우기 위해 타이틀 화면을 가립니다.
                if (SteamLobbyManager.Instance != null)
                {
                    Debug.Log("[TitleScreenManager][AttemptToStartGameWithSelectedSlot] ✔ [호스트] HideTitleScreen() + StartHostWithLobby() 호출.");
                    HideTitleScreen();
                    SteamLobbyManager.Instance.StartHostWithLobby();
                }
                else
                {
                    Debug.LogError("[TitleScreenManager][AttemptToStartGameWithSelectedSlot] ✖ [호스트] SteamLobbyManager.Instance가 NULL입니다! " +
                                   "씬에 SteamLobbyManager 오브젝트가 있는지 확인하세요.");
                }
                break;

            // ──────────────────────────────────────────────────────────────────
            // [클라이언트] 이 버튼으로는 호출되지 않음 (방어 코드)
            // ──────────────────────────────────────────────────────────────────
            case NetworkConnectionType.Client:
                Debug.LogWarning("[TitleScreenManager][AttemptToStartGameWithSelectedSlot] ⚠ [클라이언트] 이 경로는 호출되지 않아야 합니다. 무시합니다.");
                break;
        }
    }

    #endregion

    #region Client Popups

    public void OpenWaitingForInvitePopUp()
    {
        waitingForInvitePopUp.SetActive(true);
        closeWaitingPopUpButton.Select();

        // 스팀 오버레이를 열어서 친구의 초대를 기다립니다.
        if (Steamworks.SteamClient.IsValid) SteamFriends.OpenOverlay("friends");
        else Debug.LogWarning("Steam API가 초기화되지 않아 오버레이를 열 수 없습니다.");
    }

    public void CloseWaitingForInvitePopUp()
    {
        waitingForInvitePopUp.SetActive(false);

        // 초대 대기를 취소하면 다시 멀티플레이어 메뉴로 돌아갑니다.
        titleScreenMultiplayerMenu.SetActive(true);
        multiplayerJoinRoomButton.Select();
    }

    #endregion

    #region Existing Save/Delete Features

    public void DisplayNofreeCharacterSlotPopUp()
    {
        noCharacterSlotsPopup.SetActive(true);
        noCharacterSlotsOkayButton.Select();
    }

    public void CloseNoFreeCharacterSlotsPopUp()
    {
        noCharacterSlotsPopup.SetActive(false);
        mainMenuNewGameButton.Select();
    }

    // Character Slots
    public void SelectCharacterSlot(CharacterSlots characterSlots)
    {
        currentSelectedSlot = characterSlots;
    }

    public void SelectNoSlot()
    {
        currentSelectedSlot = CharacterSlots.No_Slot;
    }

    // 슬롯 인덱스 번호를 enum으로 변환해주는 헬퍼 함수
    private CharacterSlots GetCharacterSlotFromIndex(int index)
    {
        switch (index)
        {
            case 0: return CharacterSlots.CharacterSlots_01;
            case 1: return CharacterSlots.CharacterSlots_02;
            case 2: return CharacterSlots.CharacterSlots_03;
            case 3: return CharacterSlots.CharacterSlots_04;
            case 4: return CharacterSlots.CharacterSlots_05;
            default: return CharacterSlots.No_Slot;
        }
    }

    public void AttemptToDeleteCharacterSlot()
    {
        if (currentSelectedSlot != CharacterSlots.No_Slot)
        {
            deleteCharacterSlotPopUp.SetActive(true);
            deleteCharacterPopUpConfirmButton.Select();
        }
    }

    public void DeleteCharacterSlot()
    {
        deleteCharacterSlotPopUp.SetActive(false);
        WorldSaveGameManager.Instance.DeleteGame(currentSelectedSlot);

        // 삭제 후 리프레시하기 위함으로 타이틀스크린로드메뉴를 비활성화/활성화
        titleScreenLoadMenu.SetActive(false);
        titleScreenLoadMenu.SetActive(true);

        loadMenuReturnButton.Select();
    }

    public void CloseDeleteCharacterPopUp()
    {
        deleteCharacterSlotPopUp.SetActive(false);
        loadMenuReturnButton.Select();
    }

    #endregion
}