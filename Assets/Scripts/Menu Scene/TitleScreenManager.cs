using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using Steamworks;

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
    [SerializeField] GameObject pressStartUI; // Press Start UI 그룹 (버튼을 포함하는 캔버스/패널)
    [SerializeField] Button pressStartButton; // 실제 Press Start 버튼

    [Header("Menus")]
    [SerializeField] GameObject titleScreenMainMenu;
    [SerializeField] GameObject titleScreenLoadMenu;
    [SerializeField] GameObject titleScreenMultiplayerMenu; // 멀티플레이어 메뉴 패널

    [Header("Main Menu Buttons")]
    [SerializeField] Button mainMenuNewGameButton;
    [SerializeField] Button mainMenuLoadGameButton;
    [SerializeField] Button mainMenuMultiplayerButton; // 메인 -> 멀티 메뉴 이동 버튼

    [Header("Multiplayer Menu Buttons")]
    [SerializeField] Button multiplayerCreateRoomButton; // 방 만들기 (Host)
    [SerializeField] Button multiplayerJoinRoomButton;   // 참여하기 (Client)
    [SerializeField] Button multiplayerReturnButton;     // 뒤로 가기

    [Header("Load Menu Buttons")]
    [SerializeField] Button loadMenuReturnButton;
    [SerializeField] Button deleteCharacterPopUpConfirmButton;
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
        // 0. Press Start 버튼
        if (pressStartButton != null) pressStartButton.onClick.AddListener(OpenMainMenuFromPressStart);

        // 1. 메인 메뉴 버튼들
        if (mainMenuNewGameButton != null) mainMenuNewGameButton.onClick.AddListener(StartNewGame);
        if (mainMenuLoadGameButton != null) mainMenuLoadGameButton.onClick.AddListener(OpenLoadGameMenu);
        if (mainMenuMultiplayerButton != null) mainMenuMultiplayerButton.onClick.AddListener(OpenMultiplayerMenu);

        // 2. 멀티플레이어 메뉴 버튼들
        if (multiplayerCreateRoomButton != null) multiplayerCreateRoomButton.onClick.AddListener(StartCreateRoomFlow);
        if (multiplayerJoinRoomButton != null) multiplayerJoinRoomButton.onClick.AddListener(StartJoinRoomFlow);
        if (multiplayerReturnButton != null) multiplayerReturnButton.onClick.AddListener(CloseMultiplayerMenu);

        // 3. 로드 메뉴 버튼들
        if (loadMenuReturnButton != null) loadMenuReturnButton.onClick.AddListener(CloseLoadGameMenu);
        if (loadMenuStartGameButton != null) loadMenuStartGameButton.onClick.AddListener(AttemptToStartGameWithSelectedSlot);

        // 4. 팝업 버튼들
        if (deleteCharacterPopUpConfirmButton != null) deleteCharacterPopUpConfirmButton.onClick.AddListener(DeleteCharacterSlot);
        if (noCharacterSlotsOkayButton != null) noCharacterSlotsOkayButton.onClick.AddListener(CloseNoFreeCharacterSlotsPopUp);
        if (closeWaitingPopUpButton != null) closeWaitingPopUpButton.onClick.AddListener(CloseWaitingForInvitePopUp);

        // 5. 캐릭터 슬롯 버튼들 (반복문으로 일괄 등록)
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
    }

    private void OnDestroy()
    {
        // 씬이 파괴될 때 이벤트 리스너를 해제하여 메모리 누수를 방지합니다. (권장 사항)
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
        if (pressStartUI != null) pressStartUI.SetActive(false);
        if (titleScreenMainMenu != null) titleScreenMainMenu.SetActive(true);
        if (mainMenuNewGameButton != null) mainMenuNewGameButton.Select();
    }

    public void OpenMultiplayerMenu()
    {
        titleScreenMainMenu.SetActive(false);
        titleScreenMultiplayerMenu.SetActive(true);
        multiplayerCreateRoomButton.Select();
    }

    public void CloseMultiplayerMenu()
    {
        titleScreenMultiplayerMenu.SetActive(false);
        titleScreenMainMenu.SetActive(true);
        mainMenuMultiplayerButton.Select();
    }

    public void StartCreateRoomFlow()
    {
        currentConnectionType = NetworkConnectionType.Host;
        titleScreenMultiplayerMenu.SetActive(false);
        titleScreenLoadMenu.SetActive(true); // 방을 파기 전 사용할 월드/캐릭터(호스트용) 선택
        loadMenuReturnButton.Select();
    }

    public void StartJoinRoomFlow()
    {
        currentConnectionType = NetworkConnectionType.Client;
        titleScreenMultiplayerMenu.SetActive(false);

        // LoadMenu(슬롯 선택)를 띄우지 않고 곧바로 초대 대기 팝업으로 넘어감
        OpenWaitingForInvitePopUp();
    }

    public void OpenLoadGameMenu()
    {
        // 메인 메뉴에서 Load를 누르면 기본적으로 싱글플레이어 모드로 진입
        currentConnectionType = NetworkConnectionType.Singleplayer;

        titleScreenMainMenu.SetActive(false);
        titleScreenMultiplayerMenu.SetActive(false);
        titleScreenLoadMenu.SetActive(true);

        loadMenuReturnButton.Select();
    }

    public void CloseLoadGameMenu()
    {
        titleScreenLoadMenu.SetActive(false);

        // 이전 메뉴(메인 또는 멀티메뉴)로 알맞게 돌아갑니다.
        if (currentConnectionType == NetworkConnectionType.Singleplayer)
        {
            titleScreenMainMenu.SetActive(true);
            mainMenuLoadGameButton.Select();
        }
        else
        {
            titleScreenMultiplayerMenu.SetActive(true);
            multiplayerCreateRoomButton.Select();
        }
    }

    /// <summary>
    /// 로비로 진입할 때 모든 타이틀 화면 UI를 깨끗하게 숨겨주는 함수입니다.
    /// LobbyUIManager 등 다른 스크립트에서 호출할 수 있습니다.
    /// </summary>
    public void HideTitleScreen()
    {
        if (pressStartUI) pressStartUI.SetActive(false);
        if (titleScreenMainMenu) titleScreenMainMenu.SetActive(false);
        if (titleScreenLoadMenu) titleScreenLoadMenu.SetActive(false);
        if (titleScreenMultiplayerMenu) titleScreenMultiplayerMenu.SetActive(false);
        if (waitingForInvitePopUp) waitingForInvitePopUp.SetActive(false);
    }

    #endregion

    #region Game Start Logic

    public void StartNetworkAsHost()
    {
        if (SteamLobbyManager.Instance != null)
        {
            SteamLobbyManager.Instance.StartHostWithLobby();
        }
        else
        {
            Debug.LogError("[에러] 씬에 SteamLobbyManager가 없습니다! 빈 오브젝트를 만들고 스크립트를 추가해주세요.");
        }
    }

    public void StartNewGame()
    {
        currentConnectionType = NetworkConnectionType.Singleplayer;

        // 싱글플레이어라도 NGO에서는 Host 모드로 작동해야 캐릭터가 스폰됩니다.
        if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.StartHost();
        }

        WorldSaveGameManager.Instance.AttemptToCreateNewGame();
    }

    // 로드 메뉴에서 '게임 시작' 버튼을 눌렀을 때 실행되는 핵심 로직
    public void AttemptToStartGameWithSelectedSlot()
    {
        if (currentSelectedSlot == CharacterSlots.No_Slot)
        {
            Debug.LogWarning("[TitleScreenManager] 캐릭터 슬롯이 선택되지 않았습니다. 슬롯을 먼저 클릭해주세요.");
            return;
        }

        // 1. 선택한 슬롯을 세이브 매니저에 알림
        WorldSaveGameManager.Instance.currentCharacterSlotBeingUsed = currentSelectedSlot;
        Debug.Log($"[TitleScreenManager] {currentConnectionType} 모드로 {currentSelectedSlot} 슬롯 접속 시작!");

        // 2. 연결 모드에 따라 게임 로드 및 씬 전환 수행
        switch (currentConnectionType)
        {
            case NetworkConnectionType.Singleplayer:
                if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient)
                {
                    NetworkManager.Singleton.StartHost();
                }

                WorldSaveGameManager.Instance.LoadGame(); // 싱글은 즉시 로드
                break;

            case NetworkConnectionType.Host:
                if (SteamLobbyManager.Instance != null)
                {
                    // [변경점] 씬을 로드하지 않고 로비 매니저에 권한을 넘깁니다.
                    SteamLobbyManager.Instance.StartHostWithLobby();

                    // 타이틀 화면 UI들을 숨겨서 LobbyUIManager의 대기방 패널이 보이게 합니다.
                    HideTitleScreen();
                }
                else
                {
                    Debug.LogError("[에러] SteamLobbyManager가 씬에 없습니다. 멀티플레이어 서버를 열 수 없습니다.");
                }
                break;

            case NetworkConnectionType.Client:
                Debug.LogWarning("[TitleScreenManager] 클라이언트는 슬롯 선택 없이 바로 접속해야 합니다.");
                break;
        }
    }

    #endregion

    #region Client Popups

    public void OpenWaitingForInvitePopUp()
    {
        waitingForInvitePopUp.SetActive(true);
        closeWaitingPopUpButton.Select();

        if (Steamworks.SteamClient.IsValid)
        {
            SteamFriends.OpenOverlay("friends");
        }
        else
        {
            Debug.LogWarning("Steam API가 초기화되지 않아 오버레이를 열 수 없습니다.");
        }
    }

    public void CloseWaitingForInvitePopUp()
    {
        waitingForInvitePopUp.SetActive(false);
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
        Debug.Log($"[TitleScreenManager] 슬롯이 선택되었습니다: {currentSelectedSlot}");
    }

    public void SelectNoSlot()
    {
        currentSelectedSlot = CharacterSlots.No_Slot;
    }

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