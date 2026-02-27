using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;
using TMPro;

// 지형 생성 시스템 참조를 위해 추가
using CaveSystem;

/*
 * [LobbyUIManager - 통합 고도화 버전]
 * 방 목록 검색, 대기방 UI, 스팀 친구 초대, 방장 권한 제어 및 
 * Phase 5 멀티플레이어 지형 사전 생성 동기화 로직을 모두 관리합니다.
 */
public class LobbyUIManager : NetworkBehaviour
{
    // --- 싱글톤 인스턴스 ---
    public static LobbyUIManager Instance;

    [Header("Debug Settings")]
    [SerializeField] private bool showDebugLogs = true;

    [Header("Panels")]
    [SerializeField] private GameObject roomBrowserPanel; // 방 목록 패널
    [SerializeField] private GameObject lobbyRoomPanel;   // 대기방 패널

    [Header("Room Browser UI (방 목록)")]
    [SerializeField] private Transform roomListContent;
    [SerializeField] private GameObject roomListItemPrefab;
    [SerializeField] private Button refreshRoomsButton;
    [SerializeField] private Button closeBrowserButton;

    [Header("Lobby Room UI (대기방)")]
    [SerializeField] private TextMeshProUGUI lobbyTitleText;
    [SerializeField] private Transform playerListContent;
    [SerializeField] private GameObject playerListItemPrefab;

    [Header("Host Controls (호스트 전용)")]
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button inviteFriendButton;
    [SerializeField] private Button leaveLobbyButton;

    [Header("Procedural Generation UI (지형 생성)")]
    [SerializeField] private GameObject generationProgressPanel;
    [SerializeField] private TextMeshProUGUI generationStatusText;
    [SerializeField] private Slider generationProgressBar;
    [SerializeField] private TextMeshProUGUI partyStatusSummaryText;

    // --- [NGO 2.0 동기화 변수] ---
    private NetworkVariable<int> syncedWorldSeed = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> readyCount = new NetworkVariable<int>(0);
    private NetworkVariable<int> totalExpectedCount = new NetworkVariable<int>(0);

    // 지형 생성이 완료되었는지 판별하는 플래그
    private bool isLocalBakingComplete = false;
    private Dictionary<ulong, bool> clientBakingReadyMap = new Dictionary<ulong, bool>();
    private float timeoutTimer = 0f;
    private const float START_TIMEOUT = 60f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Log("싱글톤 인스턴스 초기화 완료.");
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // 버튼 이벤트 바인딩
        if (refreshRoomsButton) refreshRoomsButton.onClick.AddListener(RefreshRoomList);
        if (closeBrowserButton) closeBrowserButton.onClick.AddListener(CloseRoomBrowser);
        if (startGameButton) startGameButton.onClick.AddListener(OnStartGameClicked);
        if (inviteFriendButton) inviteFriendButton.onClick.AddListener(InviteFriends);
        if (leaveLobbyButton) leaveLobbyButton.onClick.AddListener(LeaveLobby);
    }

    public override void OnNetworkSpawn()
    {
        // 시드 변동 감지 (난입 포함)
        syncedWorldSeed.OnValueChanged += OnWorldSeedChanged;
        // 인원 수 변동 감지 (UI 갱신용)
        readyCount.OnValueChanged += (oldVal, newVal) => UpdatePartyStatusUI();
        totalExpectedCount.OnValueChanged += (oldVal, newVal) => UpdatePartyStatusUI();

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientJoined;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientLeft;

            // 호스트가 방을 열면 즉시 시드 발급
            int newSeed = Random.Range(100000, 999999);
            syncedWorldSeed.Value = newSeed;
            totalExpectedCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;

            // 🔥 [추가된 핵심 로직] 
            // 호스트 본인은 OnValueChanged가 즉시 안 불릴 수 있으므로, 
            // 시드 발급 직후 스스로의 지형 생성 함수를 강제 트리거합니다!
            OnWorldSeedChanged(0, newSeed);
        }

        // CaveManager 진행도 이벤트 구독
        if (CaveManager.Instance != null)
        {
            CaveManager.Instance.OnPregenProgressUpdated += OnBakingProgressCallback;
        }

        SteamMatchmaking.OnLobbyMemberJoined += OnMemberChanged;
        SteamMatchmaking.OnLobbyMemberDisconnected += OnMemberChanged;
        SteamMatchmaking.OnLobbyMemberLeave += OnMemberChanged;

        Log($"네트워크 스폰 성공. 로컬 ID: {NetworkManager.Singleton.LocalClientId}");
    }

    public override void OnNetworkDespawn()
    {
        syncedWorldSeed.OnValueChanged -= OnWorldSeedChanged;
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientJoined;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientLeft;
        }
        if (CaveManager.Instance != null)
        {
            CaveManager.Instance.OnPregenProgressUpdated -= OnBakingProgressCallback;
        }

        SteamMatchmaking.OnLobbyMemberJoined -= OnMemberChanged;
        SteamMatchmaking.OnLobbyMemberDisconnected -= OnMemberChanged;
        SteamMatchmaking.OnLobbyMemberLeave -= OnMemberChanged;
    }

    #region Network Callbacks (접속 관리)

    private void OnClientJoined(ulong clientId)
    {
        if (!IsServer) return;
        Log($"신규 유저 접속: {clientId}");
        totalExpectedCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;

        if (!clientBakingReadyMap.ContainsKey(clientId))
            clientBakingReadyMap.Add(clientId, false);

        CheckStartCondition();
    }

    private void OnClientLeft(ulong clientId)
    {
        if (!IsServer) return;
        Log($"유저 퇴장: {clientId}");
        if (clientBakingReadyMap.ContainsKey(clientId))
            clientBakingReadyMap.Remove(clientId);

        totalExpectedCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;
        readyCount.Value = clientBakingReadyMap.Values.Count(v => v == true);
        CheckStartCondition();
    }

    #endregion

    #region Room Browser Logic (방 목록 보기)

    public void OpenRoomBrowser()
    {
        Log("방 목록(Room Browser) 패널을 엽니다.");
        if (roomBrowserPanel != null) roomBrowserPanel.SetActive(true);
        if (lobbyRoomPanel != null) lobbyRoomPanel.SetActive(false);
        RefreshRoomList();
    }

    public void CloseRoomBrowser()
    {
        Log("방 목록(Room Browser) 패널을 닫습니다.");
        if (roomBrowserPanel != null) roomBrowserPanel.SetActive(false);
        // 타이틀 매니저가 있다면 화면 복구
        var titleManager = GameObject.FindObjectOfType<TitleScreenManager>();
        if (titleManager != null) titleManager.ShowTitleScreen();
    }

    public async void RefreshRoomList()
    {
        Log("방 목록 새로고침 중...");
        if (roomListContent == null) return;
        foreach (Transform child in roomListContent) Destroy(child.gameObject);

        var lobbies = await SteamMatchmaking.LobbyList
                    .WithMaxResults(10)
                    .RequestAsync();

        if (lobbies != null && roomListItemPrefab != null)
        {
            foreach (var lobby in lobbies)
            {
                GameObject item = Instantiate(roomListItemPrefab, roomListContent);
                item.transform.localScale = Vector3.one;

                TextMeshProUGUI text = item.GetComponentInChildren<TextMeshProUGUI>();
                Button joinBtn = item.GetComponentInChildren<Button>();

                if (text != null)
                    text.text = $"{lobby.GetData("LobbyName")} ({lobby.MemberCount}/{lobby.MaxMembers})";

                if (joinBtn != null)
                    joinBtn.onClick.AddListener(async () => await lobby.Join());
            }
        }
    }

    #endregion

    #region Lobby Room Logic (대기방)

    public void OpenLobbyRoom(Lobby currentLobby)
    {
        Log("대기방(Lobby Room) 패널 활성화.");
        if (roomBrowserPanel != null) roomBrowserPanel.SetActive(false);
        if (lobbyRoomPanel != null) lobbyRoomPanel.SetActive(true);

        if (lobbyTitleText) lobbyTitleText.text = currentLobby.GetData("LobbyName") ?? "대기방";

        RefreshPlayerList(currentLobby);

        if (NetworkManager.Singleton.IsConnectedClient)
        {
            if (startGameButton) startGameButton.gameObject.SetActive(IsServer);
            if (inviteFriendButton) inviteFriendButton.gameObject.SetActive(true);
        }
    }

    private void OnMemberChanged(Lobby lobby, Friend friend)
    {
        if (lobbyRoomPanel != null && lobbyRoomPanel.activeSelf)
        {
            RefreshPlayerList(lobby);
        }
    }

    private void RefreshPlayerList(Lobby lobby)
    {
        if (playerListContent == null || playerListItemPrefab == null) return;
        foreach (Transform child in playerListContent) Destroy(child.gameObject);

        var members = lobby.Members.ToArray();
        for (int i = 0; i < lobby.MaxMembers; i++)
        {
            GameObject item = Instantiate(playerListItemPrefab, playerListContent);
            item.transform.localScale = Vector3.one;
            TextMeshProUGUI text = item.GetComponentInChildren<TextMeshProUGUI>();

            if (text != null)
            {
                if (i < members.Length)
                {
                    Friend member = members[i];
                    text.text = member.Name;
                    if (member.Id == lobby.Owner.Id) text.text += " <color=yellow>[방장]</color>";
                }
                else
                {
                    text.text = "<color=#808080>참여자 대기 중...</color>";
                }
            }
        }
    }

    private void InviteFriends()
    {
        if (Steamworks.SteamClient.IsValid)
            SteamFriends.OpenOverlay("friends");
        else
            LogWarning("Steam API가 유효하지 않습니다.");
    }

    private void LeaveLobby()
    {
        Log("로비 퇴장.");
        var lobbyManager = GameObject.FindObjectOfType<SteamLobbyManager>();
        if (lobbyManager != null) lobbyManager.LeaveLobby();

        if (lobbyRoomPanel != null) lobbyRoomPanel.SetActive(false);
        var titleManager = GameObject.FindObjectOfType<TitleScreenManager>();
        if (titleManager != null) titleManager.ShowTitleScreen();
    }

    #endregion

    #region 🌐 Phase 5: Terrain Synchronization

    private void OnWorldSeedChanged(int oldSeed, int newSeed)
    {
        if (newSeed == 0) return;
        if (generationProgressPanel) generationProgressPanel.SetActive(true);

        if (CaveManager.Instance != null)
        {
            CaveManager.Instance.StartLobbyPregeneration(newSeed);
        }
    }

    private void OnBakingProgressCallback(float progress, string status)
    {
        if (generationProgressBar) generationProgressBar.value = progress;
        if (generationStatusText) generationStatusText.text = status;

        if (progress >= 1.0f && !isLocalBakingComplete)
        {
            isLocalBakingComplete = true;
            NotifyTerrainReadyServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyTerrainReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!clientBakingReadyMap.ContainsKey(clientId))
            clientBakingReadyMap.Add(clientId, true);
        else
            clientBakingReadyMap[clientId] = true;

        readyCount.Value = clientBakingReadyMap.Values.Count(v => v == true);
        CheckStartCondition();
    }

    private void UpdatePartyStatusUI()
    {
        if (partyStatusSummaryText)
        {
            partyStatusSummaryText.text = $"지형 생성 현황: <color=yellow>{readyCount.Value} / {totalExpectedCount.Value}</color> 완료";
        }
    }

    private void CheckStartCondition()
    {
        if (!IsServer) return;
        int total = NetworkManager.Singleton.ConnectedClientsIds.Count;
        totalExpectedCount.Value = total;

        if (readyCount.Value >= total)
        {
            if (startGameButton) startGameButton.interactable = true;
        }
        else
        {
            if (startGameButton) startGameButton.interactable = false;
        }
    }

    private void Update()
    {
        if (IsServer && syncedWorldSeed.Value != 0 && readyCount.Value < totalExpectedCount.Value)
        {
            timeoutTimer += Time.deltaTime;
            if (timeoutTimer > START_TIMEOUT)
            {
                if (startGameButton) startGameButton.interactable = true;
                if (partyStatusSummaryText) partyStatusSummaryText.text += " <color=red>(타임아웃 발생)</color>";
                timeoutTimer = -9999f;
            }
        }
    }

    // 호스트가 '게임 시작' 버튼을 눌렀을 때 실행 (다 같이 씬 로드)
    private void OnStartGameClicked()
    {
        if (!NetworkManager.Singleton.IsHost) return;

        // 지형 생성이 끝나지 않았다면 시작 불가
        /*if (!isTerrainReady)
        {
            LogWarning("아직 지형 생성 작업이 완료되지 않았습니다! 잠시만 기다려주세요.");
            return;
        }*/

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


    #endregion

    private void Log(string m) { if (showDebugLogs) Debug.Log($"[LobbyUIManager] {m}"); }
    private void LogWarning(string m) { if (showDebugLogs) Debug.LogWarning($"[LobbyUIManager] {m}"); }
    private void LogError(string m) { if (showDebugLogs) Debug.LogError($"[LobbyUIManager] {m}"); }
}