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
 * [LobbyUIManager - 통합 고도화 버전 (디버깅 강화)]
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

    // [중요] 우리 게임만의 고유한 검색 키값
    private const string GAME_IDENTIFIER_KEY = "GameID";
    private const string GAME_IDENTIFIER_VALUE = "TDA";

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

        // NGO 전역 연결 끊김 감지
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += GlobalDisconnectHandler;
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= GlobalDisconnectHandler;
        }
    }

    private void GlobalDisconnectHandler(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            LogError($"🚨 [치명적 오류] 로컬 클라이언트(나)의 NGO 네트워크 연결이 끊어졌거나 Host 시작에 실패했습니다! (ClientID: {clientId})");
        }
        else
        {
            LogWarning($"⚠️ 유저 연결 끊김 감지 (Global): ClientID {clientId}");
        }
    }

    public override void OnNetworkSpawn()
    {
        Log($"🌐 [OnNetworkSpawn] 네트워크 오브젝트 스폰 완료! IsServer: {IsServer}, IsClient: {IsClient}, LocalID: {NetworkManager.Singleton.LocalClientId}");

        syncedWorldSeed.OnValueChanged += OnWorldSeedChanged;
        readyCount.OnValueChanged += (oldVal, newVal) => UpdatePartyStatusUI();
        totalExpectedCount.OnValueChanged += (oldVal, newVal) => UpdatePartyStatusUI();

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientJoined;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientLeft;

            Log("👑 내가 호스트(서버)입니다. 월드 시드 발급을 시도합니다.");
            int newSeed = Random.Range(100000, 999999);
            syncedWorldSeed.Value = newSeed;
            totalExpectedCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;

            OnWorldSeedChanged(0, newSeed);
        }

        if (CaveManager.Instance != null)
        {
            CaveManager.Instance.OnPregenProgressUpdated += OnBakingProgressCallback;
        }
        else
        {
            LogWarning("⚠️ CaveManager.Instance를 찾을 수 없습니다! 지형 생성 이벤트를 구독하지 못했습니다.");
        }

        SteamMatchmaking.OnLobbyMemberJoined += OnMemberChanged;
        SteamMatchmaking.OnLobbyMemberDisconnected += OnMemberChanged;
        SteamMatchmaking.OnLobbyMemberLeave += OnMemberChanged;
    }

    public override void OnNetworkDespawn()
    {
        LogWarning("💥 [OnNetworkDespawn] 네트워크 오브젝트가 씬에서 디스폰(제거)되었습니다.");

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
        Log($"👋 [OnClientJoined] 신규 유저 접속 성공: ClientID {clientId}");
        totalExpectedCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;

        if (!clientBakingReadyMap.ContainsKey(clientId))
            clientBakingReadyMap.Add(clientId, false);

        CheckStartCondition();
    }

    private void OnClientLeft(ulong clientId)
    {
        if (!IsServer) return;
        LogWarning($"🚪 [OnClientLeft] 유저 퇴장: ClientID {clientId}");
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
        if (roomBrowserPanel != null) roomBrowserPanel.SetActive(true);
        if (lobbyRoomPanel != null) lobbyRoomPanel.SetActive(false);
        RefreshRoomList();
    }

    public void CloseRoomBrowser()
    {
        if (roomBrowserPanel != null) roomBrowserPanel.SetActive(false);
        var titleManager = FindFirstObjectByType<TitleScreenManager>();
        if (titleManager != null) titleManager.ShowTitleScreen();
    }

    public async void RefreshRoomList()
    {
        Log("🔄 방 목록 새로고침 중...");
        if (roomListContent == null) return;
        foreach (Transform child in roomListContent) Destroy(child.gameObject);

        var lobbies = await SteamMatchmaking.LobbyList
                    .WithKeyValue(GAME_IDENTIFIER_KEY, GAME_IDENTIFIER_VALUE)
                    .WithMaxResults(100)
                    .RequestAsync();

        if (lobbies != null && roomListItemPrefab != null)
        {
            Log($"✅ {lobbies.Count()}개의 조건에 맞는 방을 찾았습니다.");
            foreach (var lobby in lobbies)
            {
                GameObject item = Instantiate(roomListItemPrefab, roomListContent);
                item.transform.localScale = Vector3.one;

                TextMeshProUGUI text = item.GetComponentInChildren<TextMeshProUGUI>();
                Button joinBtn = item.GetComponentInChildren<Button>();

                if (text != null)
                    text.text = $"{lobby.GetData("LobbyName")} ({lobby.MemberCount}/{lobby.MaxMembers})";

                if (joinBtn != null)
                    joinBtn.onClick.AddListener(async () => {
                        Log($"👉 방 참가 시도: {lobby.GetData("LobbyName")}");
                        await lobby.Join();
                    });
            }
        }
        else
        {
            Log("📭 개설된 방이 없습니다.");
        }
    }

    #endregion

    #region Lobby Room Logic (대기방)

    public void OpenLobbyRoom(Lobby currentLobby)
    {
        Log($"🏠 대기방(Lobby Room) 패널 활성화. 로비 이름: {currentLobby.GetData("LobbyName")}");
        if (roomBrowserPanel != null) roomBrowserPanel.SetActive(false);
        if (lobbyRoomPanel != null) lobbyRoomPanel.SetActive(true);

        if (lobbyTitleText) lobbyTitleText.text = currentLobby.GetData("LobbyName") ?? "대기방";

        RefreshPlayerList(currentLobby);

        if (NetworkManager.Singleton.IsConnectedClient)
        {
            if (startGameButton) startGameButton.gameObject.SetActive(IsServer);
            if (inviteFriendButton) inviteFriendButton.gameObject.SetActive(true);
        }
        else
        {
            LogError("🚨 [OpenLobbyRoom] 스팀 로비는 열렸으나 NGO 클라이언트가 연결되지 않은 상태입니다!");
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
            LogWarning("⚠️ Steam API가 유효하지 않아 친구 초대를 열 수 없습니다.");
    }

    private void LeaveLobby()
    {
        LogWarning("🏃 LeaveLobby() 함수가 호출되었습니다. 대기방을 퇴장하고 메인 타이틀로 강제 귀환합니다.");
        var lobbyManager = FindFirstObjectByType<SteamLobbyManager>();
        if (lobbyManager != null) lobbyManager.LeaveLobby();

        if (lobbyRoomPanel != null) lobbyRoomPanel.SetActive(false);
        var titleManager = FindFirstObjectByType<TitleScreenManager>();
        if (titleManager != null) titleManager.ShowTitleScreen();
    }

    #endregion

    #region 🌐 Phase 5: Terrain Synchronization

    private void OnWorldSeedChanged(int oldSeed, int newSeed)
    {
        if (newSeed == 0) return;
        Log($"🌱 월드 시드 동기화 감지됨: {newSeed} (Old: {oldSeed}). 지형 사전 생성을 시작합니다.");
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
            Log("✅ 로컬 지형 베이킹 100% 완료! 서버로 Ready 신호를 보냅니다.");
            isLocalBakingComplete = true;

            // 기존 LobbyUI 상태 갱신
            NotifyTerrainReadyServerRpc();

            // TerrainSyncNetworkManager 에도 완료 알림 전송!
            if (CaveSystem.Multiplayer.TerrainSyncNetworkManager.Instance != null && NetworkManager.Singleton != null)
            {
                CaveSystem.Multiplayer.TerrainSyncNetworkManager.Instance.ReportTerrainReadyServerRpc(NetworkManager.Singleton.LocalClientId);
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void NotifyTerrainReadyServerRpc(RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        Log($"📨 [Server] 클라이언트 {clientId}로부터 지형 준비 완료 신호 수신.");

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

        if (readyCount.Value >= total && total > 0)
        {
            Log("✨ 모든 파티원의 지형 준비가 완료되었습니다! Start Game 버튼을 활성화합니다.");
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
                LogWarning($"⏱️ 60초 타임아웃 경과! 강제로 Start Game 버튼을 활성화합니다.");
                if (startGameButton) startGameButton.interactable = true;
                if (partyStatusSummaryText) partyStatusSummaryText.text += " <color=red>(타임아웃 발생)</color>";
                timeoutTimer = -9999f;
            }
        }
    }

    // [🔥 핵심 수정] 세이브 슬롯의 데이터를 읽어서 NGO 씬 로더로 씬 전환 수행
    private void OnStartGameClicked()
    {
        if (!NetworkManager.Singleton.IsHost) return;

        Log("호스트가 게임을 시작합니다! 연결된 모든 클라이언트를 본 게임 씬으로 동기화합니다.");

        // 세이브 데이터가 꼬였을 때를 대비한 기본 씬 이름
        string targetSceneName = "Scene_World_01";

        try
        {
            // WorldSaveGameManager에서 현재 선택된 세이브 데이터 추출
            if (WorldSaveGameManager.Instance != null && WorldSaveGameManager.Instance.currentCharacterData != null)
            {
                int savedSceneIndex = WorldSaveGameManager.Instance.currentCharacterData.sceneIndex;

                // 씬 인덱스가 유효한지 검사 (보통 0번은 메인 로비)
                if (savedSceneIndex > 0 && savedSceneIndex < UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings)
                {
                    // Build Index를 기반으로 실제 씬 이름(string)을 추출
                    string scenePath = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(savedSceneIndex);

                    if (!string.IsNullOrEmpty(scenePath))
                    {
                        targetSceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
                        Log($"📂 세이브 슬롯 데이터 읽기 성공! 타겟 씬: {targetSceneName} (BuildIndex: {savedSceneIndex})");
                    }
                }
                else
                {
                    LogWarning($"⚠️ 유효하지 않은 씬 인덱스({savedSceneIndex})가 감지되어 기본 씬({targetSceneName})으로 대체합니다.");
                }
            }
            else
            {
                LogWarning("⚠️ WorldSaveGameManager 또는 CharacterData가 없어 기본 씬으로 진행합니다.");
            }
        }
        catch (System.Exception e)
        {
            LogError($"🚨 세이브 슬롯 씬 추출 중 오류 발생 (기본 씬으로 강제 진행): {e.Message}");
        }

        // 에러를 유발하는 LoadGame() 대신 NGO 공식 SceneManager를 사용하여 네트워크 씬 로드 수행
        NetworkManager.Singleton.SceneManager.LoadScene(targetSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    #endregion

    private void Log(string m) { if (showDebugLogs) Debug.Log($"[LobbyUIManager] {m}"); }
    private void LogWarning(string m) { if (showDebugLogs) Debug.LogWarning($"[LobbyUIManager] {m}"); }
    private void LogError(string m) { if (showDebugLogs) Debug.LogError($"[LobbyUIManager] {m}"); }
}