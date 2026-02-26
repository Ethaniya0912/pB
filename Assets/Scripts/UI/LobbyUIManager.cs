using System.Collections;
using System.Collections.Generic;
using System.Linq; // 배열 변환(ToArray)을 위해 추가됨
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;
using TMPro;

/*
 * [LobbyUIManager - 상세 확장 버전]
 * 방 목록 검색(Room Browser), 대기방 UI, 스팀 친구 초대, 방장 권한 제어 및 
 * 백그라운드 프로세듀럴 지형 생성 인터페이스를 모두 관리하는 통합 로비 매니저입니다.
 */
public class LobbyUIManager : MonoBehaviour
{
    // --- 싱글톤 인스턴스 ---
    public static LobbyUIManager Instance;

    [Header("Debug Settings")]
    [Tooltip("체크하면 로비 UI의 상세한 동작 로그를 콘솔에 출력합니다.")]
    [SerializeField] private bool showDebugLogs = true;

    [Header("Panels")]
    [SerializeField] private GameObject roomBrowserPanel; // 방 목록 패널
    [SerializeField] private GameObject lobbyRoomPanel;   // 대기방 패널

    [Header("Room Browser UI (방 목록)")]
    [SerializeField] private Transform roomListContent;
    [SerializeField] private GameObject roomListItemPrefab; // 방 이름, 인원, 참여 버튼이 있는 프리팹
    [SerializeField] private Button refreshRoomsButton;
    [SerializeField] private Button closeBrowserButton;

    [Header("Lobby Room UI (대기방)")]
    [SerializeField] private TextMeshProUGUI lobbyTitleText;
    [SerializeField] private Transform playerListContent;
    [SerializeField] private GameObject playerListItemPrefab; // 접속자 이름 표시용 프리팹

    [Header("Host Controls (호스트 전용)")]
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button inviteFriendButton;
    [SerializeField] private Button leaveLobbyButton;

    [Header("Procedural Generation UI (지형 생성)")]
    [SerializeField] private GameObject generationProgressPanel;
    [SerializeField] private TextMeshProUGUI generationStatusText;
    [SerializeField] private Slider generationProgressBar;

    // 지형 생성이 완료되었는지 판별하는 플래그
    private bool isTerrainReady = false;

    // --- 디버깅용 래퍼 함수 ---
    private void Log(string message)
    {
        if (showDebugLogs) Debug.Log($"[LobbyUIManager] {message}");
    }

    private void LogError(string message)
    {
        if (showDebugLogs) Debug.LogError($"[LobbyUIManager] {message}");
    }
    private void LogWarning(string message)
    {
        if (showDebugLogs) Debug.LogWarning($"[LobbyUIManager] {message}");
    }
    // --------------------------

    private void Awake()
    {
        // 싱글톤 패턴 적용
        if (Instance == null)
        {
            Instance = this;
            Log("싱글톤 인스턴스 초기화 완료.");
        }
        else
        {
            LogWarning("이미 존재하는 인스턴스가 있어 중복 생성된 객체를 파괴합니다.");
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        Log("버튼 이벤트 바인딩을 시작합니다.");
        // 버튼 이벤트 일괄 등록
        if (refreshRoomsButton) refreshRoomsButton.onClick.AddListener(RefreshRoomList);
        if (closeBrowserButton) closeBrowserButton.onClick.AddListener(CloseRoomBrowser);

        if (startGameButton) startGameButton.onClick.AddListener(OnStartGameClicked);
        if (inviteFriendButton) inviteFriendButton.onClick.AddListener(InviteFriends);
        if (leaveLobbyButton) leaveLobbyButton.onClick.AddListener(LeaveLobby);
    }

    private void OnEnable()
    {
        if (Instance != this) return;

        Log("스팀 매치메이킹 이벤트(멤버 변동)를 구독합니다.");
        // 이벤트 콜백 구독 (스팀 멤버 변동 시 UI 업데이트)
        SteamMatchmaking.OnLobbyMemberJoined += OnMemberChanged;
        SteamMatchmaking.OnLobbyMemberDisconnected += OnMemberChanged;
        SteamMatchmaking.OnLobbyMemberLeave += OnMemberChanged;
    }

    private void OnDisable()
    {
        if (Instance != this) return;

        Log("스팀 매치메이킹 이벤트 구독을 해제합니다.");
        // 이벤트 콜백 구독 해제 (메모리 누수 방지)
        SteamMatchmaking.OnLobbyMemberJoined -= OnMemberChanged;
        SteamMatchmaking.OnLobbyMemberDisconnected -= OnMemberChanged;
        SteamMatchmaking.OnLobbyMemberLeave -= OnMemberChanged;
    }

    #region Room Browser Logic (방 목록 보기)

    public void OpenRoomBrowser()
    {
        Log("방 목록(Room Browser) 패널을 엽니다.");
        if (roomBrowserPanel != null) roomBrowserPanel.SetActive(true);
        if (lobbyRoomPanel != null) lobbyRoomPanel.SetActive(false);

        // 창을 열자마자 방 목록을 한 번 갱신합니다.
        RefreshRoomList();
    }

    private void CloseRoomBrowser()
    {
        Log("방 목록(Room Browser) 패널을 닫습니다.");
        if (roomBrowserPanel != null) roomBrowserPanel.SetActive(false);

        // 브라우저를 닫으면 다시 타이틀 화면으로 돌아가게 켭니다.
        if (TitleScreenManager.Instance != null) TitleScreenManager.Instance.ShowTitleScreen();
    }

    public async void RefreshRoomList()
    {
        Log("방 목록 새로고침을 요청합니다...");
        if (roomListContent == null)
        {
            LogError("Room List Content 오브젝트가 할당되지 않았습니다.");
            return;
        }

        // 기존 리스트 UI 초기화
        foreach (Transform child in roomListContent) Destroy(child.gameObject);

        // 스팀 매치메이킹을 통해 최대 10개의 로비를 비동기 검색
        var lobbies = await SteamMatchmaking.LobbyList.WithMaxResults(10).RequestAsync();

        if (lobbies != null && roomListItemPrefab != null)
        {
            Log($"총 {lobbies.Length}개의 방을 찾았습니다.");
            foreach (var lobby in lobbies)
            {
                // UI 프리팹 생성 후 데이터 바인딩 로직
                GameObject item = Instantiate(roomListItemPrefab, roomListContent);

                // UI 레이아웃 유지를 위한 트랜스포트 초기화 보강
                item.transform.localPosition = Vector3.zero;
                item.transform.localScale = Vector3.one;

                TextMeshProUGUI text = item.GetComponentInChildren<TextMeshProUGUI>();
                Button joinBtn = item.GetComponentInChildren<Button>();

                // 방 이름 및 현재 접속 인원 표시
                if (text != null)
                    text.text = $"{lobby.GetData("LobbyName")} ({lobby.MemberCount}/{lobby.MaxMembers})";

                // 해당 방으로 입장하는 버튼 이벤트 등록
                if (joinBtn != null)
                    joinBtn.onClick.AddListener(() => JoinSelectedLobby(lobby));
            }
        }
        else
        {
            LogWarning("검색된 방이 없거나, 방 목록 프리팹(roomListItemPrefab)이 할당되지 않았습니다.");
        }
    }

    private async void JoinSelectedLobby(Lobby lobby)
    {
        Log($"선택한 방({lobby.Id})에 접속을 시도합니다...");
        if (roomBrowserPanel != null) roomBrowserPanel.SetActive(false);

        // 스팀 로비 접속 시도 -> 성공 시 SteamLobbyManager.OnLobbyEntered 콜백이 알아서 작동합니다.
        await lobby.Join();
    }

    #endregion

    #region Lobby Room Logic (대기방)

    /// <summary>
    /// SteamLobbyManager에서 방 생성 또는 입장이 완료되었을 때 호출하여 대기방 UI를 띄웁니다.
    /// </summary>
    public void OpenLobbyRoom(Lobby currentLobby)
    {
        Log("대기방(Lobby Room) 패널 열기 시도 중...");

        if (roomBrowserPanel != null) roomBrowserPanel.SetActive(false);

        if (lobbyRoomPanel != null)
        {
            lobbyRoomPanel.SetActive(true);
            Log("대기방 패널(Lobby Room Panel) 활성화 완료!");
        }
        else
        {
            LogError("오류: lobbyRoomPanel이 인스펙터에 할당되지 않아 창을 켤 수 없습니다!");
            return;
        }

        // 방 제목 셋팅
        if (lobbyTitleText) lobbyTitleText.text = currentLobby.GetData("LobbyName") ?? "대기방";

        // 입장 직후 현재 접속자 목록 갱신
        RefreshPlayerList(currentLobby);

        // 호스트 권한에 따른 UI 활성화/비활성화
        if (NetworkManager.Singleton.IsHost)
        {
            Log("호스트 권한 확인됨. 게임 시작 버튼과 지형 생성 UI를 활성화합니다.");
            if (startGameButton) startGameButton.gameObject.SetActive(true);
            if (inviteFriendButton) inviteFriendButton.gameObject.SetActive(true);

            // 호스트가 방을 만들면 즉시 백그라운드 지형 생성을 시작합니다.
            StartCoroutine(BackgroundTerrainGenerationRoutine());
        }
        else
        {
            Log("클라이언트 권한 확인됨. 게임 시작 버튼과 지형 생성 UI를 비활성화합니다.");
            if (startGameButton) startGameButton.gameObject.SetActive(false);
            if (generationProgressPanel) generationProgressPanel.SetActive(false);
        }
    }

    private void OnMemberChanged(Lobby lobby, Friend friend)
    {
        // 누군가 들어오거나 나갈 때, 현재 대기방이 켜져있다면 UI를 즉시 갱신합니다.
        if (lobbyRoomPanel != null && lobbyRoomPanel.activeSelf)
        {
            Log($"멤버 변동 감지됨 ({friend.Name}). 접속자 목록을 갱신합니다.");
            RefreshPlayerList(lobby);
        }
    }

    private void RefreshPlayerList(Lobby lobby)
    {
        if (playerListContent == null || playerListItemPrefab == null)
        {
            LogWarning("Player List Content 또는 프리팹이 누락되어 리스트를 갱신할 수 없습니다.");
            return;
        }

        // 기존 리스트 청소
        foreach (Transform child in playerListContent) Destroy(child.gameObject);

        // 로비에 접속한 멤버들을 배열로 변환 (인덱스 접근 용이성)
        var members = lobby.Members.ToArray();

        // 방의 최대 인원 수만큼 프리팹을 미리 생성
        for (int i = 0; i < lobby.MaxMembers; i++)
        {
            GameObject item = Instantiate(playerListItemPrefab, playerListContent);

            // UI 레이아웃 유지를 위한 트랜스포트 초기화 보강 (중요)
            item.transform.localPosition = Vector3.zero;
            item.transform.localScale = Vector3.one;

            TextMeshProUGUI text = item.GetComponentInChildren<TextMeshProUGUI>();

            if (text != null)
            {
                // 인덱스가 현재 멤버 수보다 작으면 실제 유저 정보를 표시
                if (i < members.Length)
                {
                    Friend member = members[i];
                    text.text = member.Name;

                    // 방장(Owner)인 경우 노란색 태그 추가
                    if (member.Id == lobby.Owner.Id)
                        text.text += " <color=yellow>[방장]</color>";
                }
                else
                {
                    // 빈 자리는 대기 중 텍스트로 표시 (HTML 태그로 회색 적용)
                    text.text = "<color=#808080>참여자 대기 중...</color>";
                }
            }
        }
    }

    private void InviteFriends()
    {
        Log("친구 초대 오버레이를 엽니다.");
        // 이름 충돌 방지를 위해 명시적으로 Steamworks.SteamClient 호출
        if (Steamworks.SteamClient.IsValid)
        {
            SteamFriends.OpenOverlay("friends");
        }
        else
        {
            LogWarning("Steam API가 유효하지 않아 오버레이를 열 수 없습니다.");
        }
    }

    private void LeaveLobby()
    {
        Log("로비에서 퇴장합니다.");

        // SteamLobbyManager에 퇴장 로직 실행 위임
        if (SteamLobbyManager.Instance != null) SteamLobbyManager.Instance.LeaveLobby();

        // 대기방 패널 닫기
        if (lobbyRoomPanel != null) lobbyRoomPanel.SetActive(false);

        // 타이틀 화면 원상 복구
        if (TitleScreenManager.Instance != null) TitleScreenManager.Instance.ShowTitleScreen();
    }

    #endregion

    #region Game Start & Procedural Generation

    // 호스트가 '게임 시작' 버튼을 눌렀을 때 실행 (다 같이 씬 로드)
    private void OnStartGameClicked()
    {
        if (!NetworkManager.Singleton.IsHost) return;

        // 지형 생성이 끝나지 않았다면 시작 불가
        if (!isTerrainReady)
        {
            LogWarning("아직 지형 생성 작업이 완료되지 않았습니다! 잠시만 기다려주세요.");
            return;
        }

        Log("호스트가 게임을 시작합니다! 연결된 모든 클라이언트를 본 게임 씬으로 동기화합니다.");

        // ---------- 기존 코드 보존 (주석 처리) ----------
        // NGO 2.0의 SceneManager를 사용하여 접속한 모든 유저를 동시에 "GameScene"으로 이동시킵니다.
        // NetworkManager.Singleton.SceneManager.LoadScene("GameScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        // --------------------------------------------------

        // ---------- 새로 추가된 개선 코드 ----------
        // 방을 개설할 때 선택해 둔 Slot 정보를 기반으로 저장된 월드 씬과 캐릭터 데이터를 불러옵니다.
        if (WorldSaveGameManager.Instance != null)
        {
            Log($"선택된 슬롯({WorldSaveGameManager.Instance.currentCharacterSlotBeingUsed})의 세이브 데이터를 기반으로 씬을 로드합니다.");
            WorldSaveGameManager.Instance.LoadGame();
        }
        else
        {
            LogError("WorldSaveGameManager를 찾을 수 없습니다! 안전을 위해 기본 씬(GameScene) 로드를 시도합니다.");
            NetworkManager.Singleton.SceneManager.LoadScene("GameScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        // --------------------------------------------------
    }

    // 백그라운드 지형 생성 시뮬레이션 인터페이스
    private IEnumerator BackgroundTerrainGenerationRoutine()
    {
        Log("지형 데이터 백그라운드 생성을 시작합니다.");
        isTerrainReady = false;

        if (generationProgressPanel) generationProgressPanel.SetActive(true);
        if (generationStatusText) generationStatusText.text = "월드 지형 데이터 베이킹 중...";
        if (generationProgressBar) generationProgressBar.value = 0f;
        if (startGameButton) startGameButton.interactable = false; // 완료 전까지 시작 버튼 비활성화

        // TODO: 실제 프로세듀럴 지형 생성 코드를 여기에 연결합니다.

        float progress = 0f;
        while (progress < 1f)
        {
            progress += Time.deltaTime / 3.0f;
            if (generationProgressBar) generationProgressBar.value = progress;
            yield return null;
        }

        isTerrainReady = true;

        if (generationStatusText) generationStatusText.text = "지형 생성 완료! 플레이어 입장 대기 중...";
        if (startGameButton) startGameButton.interactable = true; // 시작 버튼 활성화

        Log("지형 생성 완료. 게임 시작이 가능합니다.");
    }

    #endregion
}