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
    // [🔥 픽스 반영] 자체 syncedWorldSeed 변수는 삭제하고 TerrainSyncNetworkManager의 시드를 직접 참조합니다.
    private NetworkVariable<int> readyCount = new NetworkVariable<int>(0);
    private NetworkVariable<int> totalExpectedCount = new NetworkVariable<int>(0);

    // 지형 생성이 완료되었는지 판별하는 플래그
    private bool isLocalBakingComplete = false;
    private Dictionary<ulong, bool> clientBakingReadyMap = new Dictionary<ulong, bool>();
    private float timeoutTimer = 0f;
    private const float START_TIMEOUT = 60f;

    // [복구] 우리 게임만의 고유한 검색 키값
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

        // [복구] NGO 전역 연결 끊김 감지
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

        // [🔥 핵심 픽스] 자체 변수 대신 TerrainSyncNetworkManager의 변수 구독
        if (CaveSystem.Multiplayer.TerrainSyncNetworkManager.Instance != null)
        {
            CaveSystem.Multiplayer.TerrainSyncNetworkManager.Instance.SyncedWorldSeed.OnValueChanged += OnWorldSeedChanged;

            // 방어 코드: 로비 UI 스폰 전에 이미 발급된 시드가 있다면 즉시 굽기 시작
            int currentSeed = CaveSystem.Multiplayer.TerrainSyncNetworkManager.Instance.SyncedWorldSeed.Value;
            if (currentSeed != 0)
            {
                Log($"[동기화] 스폰 시점에 이미 시드({currentSeed})가 발급되어 있습니다. 즉시 베이킹을 시작합니다.");
                OnWorldSeedChanged(0, currentSeed);
            }
        }
        else
        {
            LogError("TerrainSyncNetworkManager를 찾을 수 없습니다! 지형 동기화가 불가능합니다.");
        }

        readyCount.OnValueChanged += (oldVal, newVal) => UpdatePartyStatusUI();
        totalExpectedCount.OnValueChanged += (oldVal, newVal) => UpdatePartyStatusUI();

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientJoined;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientLeft;

            Log("👑 내가 호스트(서버)입니다. 접속 및 준비 릴레이 관리를 시작합니다.");
            totalExpectedCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;
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

        if (CaveSystem.Multiplayer.TerrainSyncNetworkManager.Instance != null)
        {
            CaveSystem.Multiplayer.TerrainSyncNetworkManager.Instance.SyncedWorldSeed.OnValueChanged -= OnWorldSeedChanged;
        }

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

        // [복구] 고유 식별자 키값 적용
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
        // [🔥 핵심 픽스] 자체 syncedWorldSeed 변수 대신 TerrainSyncNetworkManager 확인
        if (IsServer && CaveSystem.Multiplayer.TerrainSyncNetworkManager.Instance != null &&
            CaveSystem.Multiplayer.TerrainSyncNetworkManager.Instance.SyncedWorldSeed.Value != 0 &&
            readyCount.Value < totalExpectedCount.Value)
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

    // [수정됨] 세이브 슬롯의 데이터를 읽어서 NGO 씬 로더로 씬 전환 수행.
    // 슬롯이 없는 경우 PrepareOrCreateSlotForLobby()를 통해 자동으로 빈 슬롯을 생성합니다.
    private void OnStartGameClicked()
    {
        // ── [DEBUG] 버튼 클릭 자체가 수신되었는지 확인 (showDebugLogs 무관하게 항상 출력)
        Debug.Log("[LobbyUIManager][OnStartGameClicked] ▶ START GAME 버튼 클릭 감지됨.");

        // ── [DEBUG] NetworkManager 기본 상태 덤프
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[LobbyUIManager][OnStartGameClicked] ✖ NetworkManager.Singleton이 NULL입니다! NGO가 초기화되지 않았습니다.");
            return;
        }

        Debug.Log($"[LobbyUIManager][OnStartGameClicked] NetworkManager 상태 → " +
                  $"IsHost={NetworkManager.Singleton.IsHost}, " +
                  $"IsServer={NetworkManager.Singleton.IsServer}, " +
                  $"IsClient={NetworkManager.Singleton.IsClient}, " +
                  $"IsConnectedClient={NetworkManager.Singleton.IsConnectedClient}, " +
                  $"IsListening={NetworkManager.Singleton.IsListening}, " +
                  $"LocalClientId={NetworkManager.Singleton.LocalClientId}");

        // ── [GATE] 호스트만 게임 시작 가능
        if (!NetworkManager.Singleton.IsHost)
        {
            Debug.LogWarning("[LobbyUIManager][OnStartGameClicked] ✖ IsHost=false → 호스트가 아니므로 게임 시작 권한이 없습니다. 여기서 중단됩니다.");
            return;
        }

        Debug.Log("[LobbyUIManager][OnStartGameClicked] ✔ 호스트 권한 확인 완료.");

        // ── [DEBUG] startGameButton 상태 확인
        if (startGameButton == null)
        {
            Debug.LogWarning("[LobbyUIManager][OnStartGameClicked] ⚠ startGameButton 레퍼런스가 Inspector에 연결되지 않았습니다.");
        }
        else
        {
            Debug.Log($"[LobbyUIManager][OnStartGameClicked] startGameButton 상태 → " +
                      $"interactable={startGameButton.interactable}, " +
                      $"activeInHierarchy={startGameButton.gameObject.activeInHierarchy}");
        }

        // ── [STEP 1] 슬롯 준비 보장
        Debug.Log("[LobbyUIManager][OnStartGameClicked] [STEP 1] WorldSaveGameManager 슬롯 준비 시작...");

        if (WorldSaveGameManager.Instance == null)
        {
            Debug.LogError("[LobbyUIManager][OnStartGameClicked] ✖ [STEP 1] WorldSaveGameManager.Instance가 NULL입니다! " +
                           "씬에 WorldSaveGameManager 오브젝트가 존재하는지 확인하세요.");
            // 슬롯 없이 기본 씬으로 강행
            Debug.LogWarning("[LobbyUIManager][OnStartGameClicked] ⚠ 슬롯 준비를 건너뛰고 기본 씬으로 강행합니다.");
        }
        else
        {
            Debug.Log($"[LobbyUIManager][OnStartGameClicked] [STEP 1] WorldSaveGameManager 발견. " +
                      $"현재 슬롯={WorldSaveGameManager.Instance.currentCharacterSlotBeingUsed}, " +
                      $"currentCharacterData={(WorldSaveGameManager.Instance.currentCharacterData == null ? "NULL" : "존재함")}");

            bool slotReady = WorldSaveGameManager.Instance.PrepareOrCreateSlotForLobby();

            if (!slotReady)
            {
                // 모든 슬롯이 꽉 찬 경우 유저에게 팝업으로 알리고 진행을 차단합니다.
                Debug.LogError("[LobbyUIManager][OnStartGameClicked] ✖ [STEP 1] PrepareOrCreateSlotForLobby() = false → " +
                               "사용 가능한 캐릭터 슬롯이 없어 게임을 시작할 수 없습니다. 타이틀에서 슬롯을 정리해 주세요.");
                if (TitleScreenManager.Instance != null)
                    TitleScreenManager.Instance.DisplayNofreeCharacterSlotPopUp();
                else
                    Debug.LogError("[LobbyUIManager][OnStartGameClicked] ✖ TitleScreenManager.Instance도 NULL이어서 팝업을 띄울 수 없습니다.");
                return;
            }

            Debug.Log($"[LobbyUIManager][OnStartGameClicked] ✔ [STEP 1] 슬롯 준비 완료. " +
                      $"슬롯={WorldSaveGameManager.Instance.currentCharacterSlotBeingUsed}, " +
                      $"currentCharacterData={(WorldSaveGameManager.Instance.currentCharacterData == null ? "NULL" : "존재함")}");
        }

        // ── [STEP 2] 타겟 씬 이름 결정
        Debug.Log("[LobbyUIManager][OnStartGameClicked] [STEP 2] 타겟 씬 이름 결정 시작...");

        // 세이브 데이터가 꼬였을 때를 대비한 기본 씬 이름
        string targetSceneName = "Scene_World_01";

        try
        {
            // WorldSaveGameManager에서 현재 선택된 세이브 데이터 추출
            // PrepareOrCreateSlotForLobby() 호출 이후이므로 currentCharacterData가 보장됩니다.
            if (WorldSaveGameManager.Instance != null && WorldSaveGameManager.Instance.currentCharacterData != null)
            {
                int savedSceneIndex = WorldSaveGameManager.Instance.currentCharacterData.sceneIndex;
                Debug.Log($"[LobbyUIManager][OnStartGameClicked] [STEP 2] 세이브 데이터의 sceneIndex={savedSceneIndex}, " +
                          $"빌드 설정 총 씬 수={UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings}");

                // 씬 인덱스가 유효한지 검사 (보통 0번은 메인 로비)
                if (savedSceneIndex > 0 && savedSceneIndex < UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings)
                {
                    // Build Index를 기반으로 실제 씬 이름(string)을 추출
                    string scenePath = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(savedSceneIndex);
                    Debug.Log($"[LobbyUIManager][OnStartGameClicked] [STEP 2] 추출된 씬 경로: \"{scenePath}\"");

                    if (!string.IsNullOrEmpty(scenePath))
                    {
                        targetSceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
                        Debug.Log($"[LobbyUIManager][OnStartGameClicked] ✔ [STEP 2] 세이브 슬롯 씬 이름 확정: \"{targetSceneName}\" (BuildIndex: {savedSceneIndex})");
                    }
                    else
                    {
                        Debug.LogWarning($"[LobbyUIManager][OnStartGameClicked] ⚠ [STEP 2] 씬 경로가 빈 문자열입니다. 기본 씬(\"{targetSceneName}\")으로 대체합니다.");
                    }
                }
                else
                {
                    Debug.LogWarning($"[LobbyUIManager][OnStartGameClicked] ⚠ [STEP 2] sceneIndex={savedSceneIndex}가 유효 범위를 벗어났습니다. " +
                                     $"기본 씬(\"{targetSceneName}\")으로 대체합니다. " +
                                     $"(새로 만든 캐릭터는 sceneIndex=0이 정상 → 기본 씬으로 진행됩니다.)");
                }
            }
            else
            {
                Debug.LogWarning("[LobbyUIManager][OnStartGameClicked] ⚠ [STEP 2] WorldSaveGameManager 또는 currentCharacterData가 없어 기본 씬으로 진행합니다.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LobbyUIManager][OnStartGameClicked] ✖ [STEP 2] 씬 이름 결정 중 예외 발생 → {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            Debug.LogWarning($"[LobbyUIManager][OnStartGameClicked] ⚠ 예외 발생으로 기본 씬(\"{targetSceneName}\")으로 강행합니다.");
        }

        // ── [STEP 3] NGO 네트워크 씬 로드
        Debug.Log($"[LobbyUIManager][OnStartGameClicked] [STEP 3] NetworkManager.SceneManager.LoadScene(\"{targetSceneName}\") 호출 직전...");

        if (NetworkManager.Singleton.SceneManager == null)
        {
            Debug.LogError("[LobbyUIManager][OnStartGameClicked] ✖ [STEP 3] NetworkManager.SceneManager가 NULL입니다! 씬 로드 불가.");
            return;
        }

        try
        {
            var status = NetworkManager.Singleton.SceneManager.LoadScene(targetSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
            Debug.Log($"[LobbyUIManager][OnStartGameClicked] ✔ [STEP 3] LoadScene 호출 완료. 반환 상태={status} " +
                      $"(Success=0, SceneNotLoaded=1, InvalidSceneName=2, SceneEventInProgress=3, SystemError=4)");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LobbyUIManager][OnStartGameClicked] ✖ [STEP 3] LoadScene 호출 중 예외 발생 → {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
    }

    #endregion

    private void Log(string m) { if (showDebugLogs) Debug.Log($"[LobbyUIManager] {m}"); }
    private void LogWarning(string m) { if (showDebugLogs) Debug.LogWarning($"[LobbyUIManager] {m}"); }
    private void LogError(string m) { if (showDebugLogs) Debug.LogError($"[LobbyUIManager] {m}"); }
}