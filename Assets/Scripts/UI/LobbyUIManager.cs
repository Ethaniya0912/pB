using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;
using TMPro;

/*
 * [LobbyUIManager]
 * 방 목록 검색, 대기방 UI, 스팀 친구 초대, 방장 권한 제어 및 
 * 백그라운드 프로세듀럴 지형 생성까지 모두 관리하는 통합 로비 매니저입니다.
 */
public class LobbyUIManager : MonoBehaviour
{
    public static LobbyUIManager Instance;

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

    [Header("Procedural Generation UI (지형 생성 인터페이스)")]
    [SerializeField] private GameObject generationProgressPanel;
    [SerializeField] private TextMeshProUGUI generationStatusText;
    [SerializeField] private Slider generationProgressBar;

    private bool isTerrainReady = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        // 버튼 이벤트 바인딩
        if (refreshRoomsButton) refreshRoomsButton.onClick.AddListener(RefreshRoomList);
        if (closeBrowserButton) closeBrowserButton.onClick.AddListener(() => roomBrowserPanel.SetActive(false));

        if (startGameButton) startGameButton.onClick.AddListener(OnStartGameClicked);
        if (inviteFriendButton) inviteFriendButton.onClick.AddListener(InviteFriends);
        if (leaveLobbyButton) leaveLobbyButton.onClick.AddListener(LeaveLobby);

        // 이벤트 콜백 구독 (스팀 멤버 변동 시 UI 업데이트)
        SteamMatchmaking.OnLobbyMemberJoined += OnMemberChanged;
        SteamMatchmaking.OnLobbyMemberDisconnected += OnMemberChanged;
        SteamMatchmaking.OnLobbyMemberLeave += OnMemberChanged;
    }

    private void OnDestroy()
    {
        SteamMatchmaking.OnLobbyMemberJoined -= OnMemberChanged;
        SteamMatchmaking.OnLobbyMemberDisconnected -= OnMemberChanged;
        SteamMatchmaking.OnLobbyMemberLeave -= OnMemberChanged;
    }

    #region Room Browser (방 목록 보기)

    public void OpenRoomBrowser()
    {
        roomBrowserPanel.SetActive(true);
        lobbyRoomPanel.SetActive(false);
        RefreshRoomList();
    }

    public async void RefreshRoomList()
    {
        // 기존 리스트 초기화
        foreach (Transform child in roomListContent) Destroy(child.gameObject);

        // 스팀 매치메이킹을 통해 최대 10개의 로비를 검색
        var lobbies = await SteamMatchmaking.LobbyList.WithMaxResults(10).RequestAsync();

        if (lobbies != null)
        {
            foreach (var lobby in lobbies)
            {
                // UI 프리팹 생성 후 데이터 바인딩 로직
                GameObject item = Instantiate(roomListItemPrefab, roomListContent);
                TextMeshProUGUI text = item.GetComponentInChildren<TextMeshProUGUI>();
                Button joinBtn = item.GetComponentInChildren<Button>();

                if (text != null)
                    text.text = $"{lobby.GetData("LobbyName")} ({lobby.MemberCount}/{lobby.MaxMembers})";

                if (joinBtn != null)
                {
                    joinBtn.onClick.AddListener(() => JoinSelectedLobby(lobby));
                }
            }
        }
    }

    private async void JoinSelectedLobby(Lobby lobby)
    {
        roomBrowserPanel.SetActive(false);
        await lobby.Join(); // 스팀 로비 접속 시도 -> 성공 시 SteamLobbyManager.OnLobbyEntered 트리거됨
    }

    #endregion

    #region Lobby Room (대기방 로직)

    // SteamLobbyManager에서 방 생성 또는 입장이 완료되었을 때 호출합니다.
    public void OpenLobbyRoom(Lobby currentLobby)
    {
        roomBrowserPanel.SetActive(false);
        lobbyRoomPanel.SetActive(true);

        if (lobbyTitleText)
            lobbyTitleText.text = currentLobby.GetData("LobbyName") ?? "대기방";

        RefreshPlayerList(currentLobby);

        // 호스트 권한에 따른 UI 활성화
        if (NetworkManager.Singleton.IsHost)
        {
            startGameButton.gameObject.SetActive(true);
            inviteFriendButton.gameObject.SetActive(true);

            // 호스트가 방을 만들면 즉시 백그라운드 지형 생성을 시작합니다.
            StartCoroutine(BackgroundTerrainGenerationRoutine());
        }
        else
        {
            // 클라이언트는 게임 시작 버튼과 지형 생성 UI를 볼 필요가 없습니다 (또는 대기 상태만 표시).
            startGameButton.gameObject.SetActive(false);
            if (generationProgressPanel) generationProgressPanel.SetActive(false);
        }
    }

    private void OnMemberChanged(Lobby lobby, Friend friend)
    {
        // 누군가 들어오거나 나갈 때 UI 갱신
        if (lobbyRoomPanel.activeSelf)
        {
            RefreshPlayerList(lobby);
        }
    }

    private void RefreshPlayerList(Lobby lobby)
    {
        foreach (Transform child in playerListContent) Destroy(child.gameObject);

        foreach (Friend member in lobby.Members)
        {
            GameObject item = Instantiate(playerListItemPrefab, playerListContent);
            TextMeshProUGUI text = item.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                text.text = member.Name;
                if (member.Id == lobby.Owner.Id) text.text += " <color=yellow>[방장]</color>";
            }
        }
    }

    private void InviteFriends()
    {
        // 스팀 오버레이를 열어 친구를 현재 로비로 즉시 초대
        if (SteamClient.IsValid)
        {
            SteamFriends.OpenOverlay("friends");
        }
    }

    private void LeaveLobby()
    {
        // 로비 퇴장 로직 (SteamLobbyManager의 LeaveLobby 호출 등)
        lobbyRoomPanel.SetActive(false);
        // 메인 메뉴 UI로 복귀 처리
    }

    #endregion

    #region Game Start & Background Generation

    // 호스트가 '게임 시작' 버튼을 눌렀을 때 실행 (다 같이 씬 로드)
    private void OnStartGameClicked()
    {
        if (!NetworkManager.Singleton.IsHost) return;

        // 지형 생성이 끝나지 않았다면 시작 불가
        if (!isTerrainReady)
        {
            Debug.LogWarning("아직 지형 생성 작업이 완료되지 않았습니다!");
            return;
        }

        Debug.Log("[Lobby] 호스트가 게임을 시작합니다. 모든 클라이언트를 게임 씬으로 동기화합니다.");

        // NGO 2.0의 SceneManager를 사용하여 접속한 모든 유저를 동시에 "GameScene"으로 이동시킵니다.
        // * "GameScene" 부분은 Build Settings에 등록된 실제 게임 씬 이름으로 변경하세요.
        NetworkManager.Singleton.SceneManager.LoadScene("GameScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    // 백그라운드 지형 생성 시뮬레이션 인터페이스
    private IEnumerator BackgroundTerrainGenerationRoutine()
    {
        isTerrainReady = false;

        if (generationProgressPanel) generationProgressPanel.SetActive(true);
        if (generationStatusText) generationStatusText.text = "월드 지형 데이터 베이킹 중...";
        if (generationProgressBar) generationProgressBar.value = 0f;
        if (startGameButton) startGameButton.interactable = false; // 완료 전까지 시작 버튼 비활성화

        // TODO: 실제 프로세듀럴 지형 생성 코드를 여기에 연결합니다.
        // (예: 노이즈 연산, 청크 생성, 네비메쉬 굽기 등)

        float progress = 0f;
        while (progress < 1f)
        {
            // 백그라운드 진행률 시뮬레이션 (3초)
            progress += Time.deltaTime / 3.0f;
            if (generationProgressBar) generationProgressBar.value = progress;
            yield return null;
        }

        // 지형 생성 완료 처리
        isTerrainReady = true;

        if (generationStatusText) generationStatusText.text = "지형 생성 완료! 플레이어 입장 대기 중...";
        if (startGameButton) startGameButton.interactable = true; // 시작 버튼 활성화

        Debug.Log("[Lobby] 호스트 백그라운드 지형 생성 완료. 게임 시작이 가능합니다.");
    }

    #endregion
}