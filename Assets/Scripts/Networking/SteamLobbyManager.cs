using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;
using CaveSystem.Multiplayer;

/* * [확장된 SteamLobbyManager - 소켓 충돌 방지 및 멀티플레이어 안전성 강화 버전]
 * * 역할:
 * 1. Facepunch.Steamworks를 이용한 스팀 로비 생성 및 관리
 * 2. NGO(Netcode for GameObjects)의 SteamP2PRelayTransport와 연동하여 호스트/클라이언트 가동
 * 3. 이전 세션의 찌꺼기 소켓으로 인한 'Invalid Socket' 및 'Vport 0' 충돌 에러 방어
 * 4. 씬 전환 및 연결 끊김 시 안전한 타이틀 화면 복구 로직 수행
 */

public class SteamLobbyManager : MonoBehaviour
{
    // --- 싱글톤 인스턴스 ---
    public static SteamLobbyManager Instance { get; private set; }

    [Header("Debug Settings")]
    [Tooltip("체크하면 연결 과정의 상세한 로그를 콘솔에 출력합니다.")]
    [SerializeField] private bool showDebugLogs = true;

    // 로비 데이터 식별을 위한 고유 키 값
    private const string HostAddressKey = "HostAddress";
    private const string LobbyNameKey = "LobbyName";

    // 전 세계 480(Spacewar) 앱 유저들과 우리 게임을 구분하기 위한 식별자
    public const string GameIdKey = "GameUniqueId";
    public const string GameIdValue = "PennutButterProject";

    [Header("Lobby Settings")]
    [SerializeField] private int maxPlayers = 4; // 최대 접속 인원

    // 현재 내가 들어가 있는 로비 정보 (Nullable)
    public Lobby? CurrentLobby { get; private set; }

    // NGO와 연동된 Transport 컴포넌트 참조
    private SteamP2PRelayTransport transport;

    // 상태 제어용 플래그
    private bool isDisconnecting = false; // 중복 셧다운 방지
    private bool isQuitting = false;      // 앱 종료 중 여부
    private bool isCreatingRoom = false;  // 방 생성 프로세스 중복 실행 방지

    // --- 디버깅용 래퍼 함수 (콘솔 가독성 향상) ---
    private void Log(string message)
    {
        if (showDebugLogs) Debug.Log($"<color=#42f5bf>[SteamLobbyManager]</color> {message}");
    }

    private void LogError(string message)
    {
        if (showDebugLogs) Debug.LogError($"<color=red>[SteamLobbyManager ERROR]</color> {message}");
    }

    private void LogWarning(string message)
    {
        if (showDebugLogs) Debug.LogWarning($"<color=yellow>[SteamLobbyManager WARNING]</color> {message}");
    }
    // --------------------------

    private void Awake()
    {
        // 싱글톤 패턴 적용: 오브젝트 영속성 유지
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Log("Awake: 싱글톤 인스턴스 생성 및 DontDestroyOnLoad 적용 완료");
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // 런타임에 Transport 컴포넌트 캐싱
        transport = GetComponent<SteamP2PRelayTransport>();
        if (transport == null)
        {
            transport = FindFirstObjectByType<SteamP2PRelayTransport>();
        }
    }

    private void Start()
    {
        // NGO 네트워크 끊김 이벤트 구독 (호스트 종료 또는 나 자신의 튕김 감지)
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void Update()
    {
        // 스팀 API의 콜백을 매 프레임 실행하여 이벤트를 수신합니다.
        if (Steamworks.SteamClient.IsValid)
        {
            Steamworks.SteamClient.RunCallbacks();
        }
    }

    private void OnEnable()
    {
        if (Instance != this) return;

        // 스팀 매치메이킹 이벤트 구독
        SteamMatchmaking.OnLobbyCreated += OnLobbyCreated;
        SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
        // [수정] OnMemberJoined -> OnLobbyMemberJoined 로 수정하여 컴파일 에러 해결
        SteamMatchmaking.OnLobbyMemberJoined += HandleMemberJoined;
    }

    private void OnDisable()
    {
        if (Instance != this) return;

        // 이벤트 구독 해제
        SteamMatchmaking.OnLobbyCreated -= OnLobbyCreated;
        SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined -= HandleMemberJoined;
    }

    private void OnDestroy()
    {
        // NGO 이벤트 구독 해제
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    // 람다식 대신 메서드로 분리하여 OnDisable에서 안전하게 구독 해제 가능하게 변경
    private void HandleMemberJoined(Lobby lobby, Friend friend)
    {
        Log($"[Lobby] 새로운 플레이어 입장: {friend.Name}");
    }

    /// <summary>
    /// NGO 네트워크 연결이 끊어졌을 때 호출되는 콜백입니다.
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;

        // 서버(Host)가 닫혔거나, 나 자신(로컬 클라이언트)의 연결이 끊긴 경우
        if (clientId == NetworkManager.ServerClientId || clientId == NetworkManager.Singleton.LocalClientId)
        {
            LogWarning("네트워크 연결이 종료되었습니다. 타이틀 화면으로 복귀합니다.");
            RevertToTitleScreen();
        }
    }

    #region 🏠 Lobby & Host Actions

    /// <summary>
    /// [핵심 로직] 이전 세션의 소켓 찌꺼기를 청소하고 안전하게 호스트를 시작합니다.
    /// </summary>
    public async void StartHostWithLobby()
    {
        if (isCreatingRoom) return;
        isCreatingRoom = true;

        Log("🚀 호스트 가동 및 소켓 정밀 클린업 프로세스를 시작합니다...");

        if (!Steamworks.SteamClient.IsValid)
        {
            LogError("Steam API가 유효하지 않습니다. 스팀 실행 여부를 확인하세요.");
            isCreatingRoom = false;
            return;
        }

        if (NetworkManager.Singleton == null)
        {
            LogError("씬에 NetworkManager가 존재하지 않습니다.");
            isCreatingRoom = false;
            return;
        }

        // [방어 코드] 'Invalid Socket' 에러 방지를 위해 이전 리소스 강제 해제
        if (NetworkManager.Singleton.IsListening || CurrentLobby.HasValue)
        {
            LogWarning("⚠️ 활성화된 세션 감지됨. 1.5초간 강제 초기화를 진행합니다...");

            if (CurrentLobby.HasValue) CurrentLobby.Value.Leave();
            CurrentLobby = null;

            NetworkManager.Singleton.Shutdown();

            // Steam API가 내부 소켓을 완전히 닫을 때까지 기다려줍니다. (중요)
            await Task.Delay(1500);
            Log("✅ 소켓 클린업 완료. 새로운 로비를 생성합니다.");
        }

        Log($"SteamMatchmaking.CreateLobbyAsync 호출 중... (최대 {maxPlayers}명)");

        try
        {
            var lobbyOpt = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);

            if (!lobbyOpt.HasValue)
            {
                LogError("로비 생성 비동기 작업 실패 (Steam 응답 없음)");
                RevertToTitleScreen();
                isCreatingRoom = false;
                return;
            }

            // 로비 데이터 설정
            var lobby = lobbyOpt.Value;
            lobby.SetPublic();
            lobby.SetJoinable(true);
            lobby.SetData(HostAddressKey, Steamworks.SteamClient.SteamId.ToString());
            lobby.SetData(LobbyNameKey, $"{Steamworks.SteamClient.Name}의 탐험대");
            lobby.SetData(GameIdKey, GameIdValue);
            CurrentLobby = lobby;

            Log($"Steam 로비 생성 완료 (ID: {lobby.Id}). NGO 호스트 시작을 시도합니다...");

            // NGO 호스트 가동
            if (NetworkManager.Singleton.StartHost())
            {
                Log("✨ NGO 호스트 가동 성공! 대기방 UI를 활성화합니다.");
                if (LobbyUIManager.Instance != null)
                {
                    LobbyUIManager.Instance.OpenLobbyRoom(lobby);
                }
            }
            else
            {
                LogError("NGO 호스트 시작 실패 (StartHost returned false)");
                RevertToTitleScreen();
            }
        }
        catch (System.Exception e)
        {
            LogError($"호스트 생성 중 치명적 예외 발생: {e.Message}");
            RevertToTitleScreen();
        }
        finally
        {
            isCreatingRoom = false;
        }
    }

    /// <summary>
    /// [LobbyUIManager 등에서 호출] 특정 로비에 접속을 시도합니다.
    /// </summary>
    public async void JoinLobby(Lobby lobby)
    {
        Log($"로비 {lobby.Id}에 접속 시도 중...");
        var result = await lobby.Join();

        if (result != RoomEnter.Success)
        {
            LogError($"로비 접속 실패: {result}");
        }
    }

    #endregion

    #region 📡 Internal Callbacks

    private void OnLobbyCreated(Result result, Lobby lobby)
    {
        if (result != Result.OK)
        {
            LogError($"OnLobbyCreated 콜백 에러: {result}");
            return;
        }
        // 우리 게임만 검색되도록 태그 주입
        lobby.SetData("GameID", "TDA");
        Log("OnLobbyCreated: 검색용 식별자 데이터 주입 완료.");
    }

    private void OnLobbyEntered(Lobby lobby)
    {
        CurrentLobby = lobby;

        // 호스트는 이미 위에서 처리가 끝났으므로 클라이언트 접속 로직 중복 방지
        if (TitleScreenManager.Instance != null &&
            TitleScreenManager.Instance.currentConnectionType == TitleScreenManager.NetworkConnectionType.Host)
        {
            return;
        }

        Log("클라이언트로 로비 입장 완료. 네트워크 연결을 시작합니다.");
        if (LobbyUIManager.Instance != null)
        {
            LobbyUIManager.Instance.OpenLobbyRoom(lobby);
        }

        // 호스트의 스팀 ID를 추출하여 Transport에 주입
        string hostSteamIDStr = lobby.GetData(HostAddressKey);
        if (ulong.TryParse(hostSteamIDStr, out ulong hostSteamID))
        {
            if (transport != null)
            {
                transport.serverId = hostSteamID;
            }

            Log($"NGO 클라이언트 시작: 호스트 ID {hostSteamID}");
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.StartClient();
            }
        }
        else
        {
            LogError("로비 데이터에서 호스트의 SteamId를 파싱하는 데 실패했습니다.");
        }
    }

    #endregion

    #region 🔄 Cleanup & Revert

    /// <summary>
    /// 모든 네트워크 자원을 해제하고 초기 타이틀 화면으로 안전하게 복귀합니다.
    /// </summary>
    private void RevertToTitleScreen()
    {
        if (isDisconnecting) return;
        isDisconnecting = true;

        LogWarning("RevertToTitleScreen: 모든 연결을 해제하고 초기 상태로 복구합니다.");

        // 스팀 로비 퇴장
        if (Steamworks.SteamClient.IsValid && CurrentLobby.HasValue)
        {
            CurrentLobby.Value.Leave();
        }
        CurrentLobby = null;

        // NGO 네트워크 종료
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        // 앱 종료 중이 아닐 때만 씬 및 UI 복구
        if (!isQuitting)
        {
            // 게임 씬(1번 이후)에 있다면 타이틀 씬(0번)으로 로드
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex != 0)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(0);
            }
            else if (TitleScreenManager.Instance != null)
            {
                // 이미 타이틀 씬이라면 대기방 UI 등을 끄고 메인 메뉴 표시
                TitleScreenManager.Instance.ShowTitleScreen();
            }
        }

        isDisconnecting = false;
    }

    /// <summary>
    /// 외부 UI에서 호출하여 방을 나가거나 세션을 종료합니다.
    /// </summary>
    public void LeaveLobby()
    {
        Log("로비 퇴장 요청 수신.");
        RevertToTitleScreen();
    }

    private void OnApplicationQuit()
    {
        isQuitting = true;
        LeaveLobby();
    }

    #endregion
}