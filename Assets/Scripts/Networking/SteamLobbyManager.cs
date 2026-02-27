using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;
using CaveSystem.Multiplayer;

/* * [확장된 SteamLobbyManager - 끊김 감지 및 씬 복구 추가]
 * 이 스크립트는 Facepunch.Steamworks와 UnityNetcodeSteamP2PRelayTransport를 
 * 사용하는 환경에 최적화되었으며, NGO와의 원활한 연동 및 상세한 로비 관리를 지원합니다.
 */

public class SteamLobbyManager : MonoBehaviour
{
    // --- 싱글톤 인스턴스 ---
    public static SteamLobbyManager Instance { get; private set; }

    [Header("Debug Settings")]
    [Tooltip("체크하면 연결 과정의 상세한 로그를 콘솔에 출력합니다.")]
    [SerializeField] private bool showDebugLogs = true;

    // 로비 데이터 식별을 위한 키 값
    private const string HostAddressKey = "HostAddress";
    private const string LobbyNameKey = "LobbyName";

    // 전 세계 480(Spacewar) 앱 유저들과 우리 게임을 구분하기 위한 고유 식별 키
    public const string GameIdKey = "GameUniqueId";
    public const string GameIdValue = "PennutButterProject";

    [Header("Lobby Settings")]
    [SerializeField] private int maxPlayers = 4; // 최대 접속 인원

    // 현재 내가 들어가 있는 로비 정보 (Nullable)
    public Lobby? CurrentLobby { get; private set; }

    // NGO와 연동된 Transport 컴포넌트 참조
    private SteamP2PRelayTransport transport;

    // 중복 셧다운 및 씬 로드 방지용 플래그
    private bool isDisconnecting = false;
    private bool isQuitting = false;

    // --- 디버깅용 래퍼 함수 ---
    private void Log(string message)
    {
        if (showDebugLogs) Debug.Log($"<color=#42f5bf>[SteamLobbyManager]</color> {message}");
    }

    private void LogError(string message)
    {
        if (showDebugLogs) Debug.LogError($"[SteamLobbyManager ERROR] {message}");
    }
    private void LogWarning(string message)
    {
        if (showDebugLogs) Debug.LogWarning($"[SteamLobbyManager WARNING] {message}");
    }
    // --------------------------

    private void Awake()
    {
        // 싱글톤 패턴 적용
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // 씬이 넘어가도 로비 매니저가 파괴되지 않도록 유지
            Log("Awake: 싱글톤 인스턴스 생성 및 DontDestroyOnLoad 적용 완료");
        }
        else
        {
            Log("Awake: 이미 존재하는 인스턴스가 있어 중복 생성된 객체를 파괴합니다.");
            Destroy(gameObject);
            return;
        }

        Log("Awake: 트랜스포트 컴포넌트 캐싱 시작");
        // 런타임에 Transport 컴포넌트 캐싱
        transport = GetComponent<SteamP2PRelayTransport>();
        if (transport == null)
        {
            transport = FindFirstObjectByType<SteamP2PRelayTransport>();
        }
    }

    private void Start()
    {
        // [추가됨] NGO 네트워크 끊김 이벤트 구독 (호스트 강제 종료 감지)
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void Update()
    {
        if (Steamworks.SteamClient.IsValid)
        {
            Steamworks.SteamClient.RunCallbacks();
        }
    }

    private void OnEnable()
    {
        if (Instance != this) return;

        Log("OnEnable: 스팀 네트워크 이벤트 콜백 구독을 시작합니다.");
        SteamMatchmaking.OnLobbyCreated += OnLobbyCreated;
        SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
        SteamMatchmaking.OnLobbyInvite += OnLobbyInvite;
        SteamMatchmaking.OnLobbyMemberJoined += OnMemberJoined;
        SteamMatchmaking.OnLobbyMemberDisconnected += OnMemberLeft;
    }

    private void OnDisable()
    {
        if (Instance != this) return;

        Log("OnDisable: 이벤트 콜백 구독을 해제합니다.");
        SteamMatchmaking.OnLobbyCreated -= OnLobbyCreated;
        SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
        SteamMatchmaking.OnLobbyInvite -= OnLobbyInvite;
        SteamMatchmaking.OnLobbyMemberJoined -= OnMemberJoined;
        SteamMatchmaking.OnLobbyMemberDisconnected -= OnMemberLeft;
    }

    private void OnDestroy()
    {
        // [추가됨] NGO 이벤트 구독 해제
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    // [핵심 추가] 연결이 끊어졌을 때 호출되는 콜백
    private void OnClientDisconnected(ulong clientId)
    {
        // 서버(Host)가 닫혀서 끊겼거나(0번), 나 자신(로컬 클라이언트)이 튕겼을 때
        if (clientId == NetworkManager.ServerClientId || clientId == NetworkManager.Singleton.LocalClientId)
        {
            LogWarning("호스트와의 연결이 끊어졌거나 네트워크가 종료되었습니다. 메인 화면으로 돌아갑니다.");
            RevertToTitleScreen();
        }
    }

    #region Lobby Callbacks

    // 로비가 생성된 직후 호출되는 콜백
    private void OnLobbyCreated(Result result, Lobby lobby)
    {
        if (result != Result.OK)
        {
            LogError($"로비 생성 실패: {result}");
            return;
        }

        Log($"로비 생성 성공! ID: {lobby.Id}");
    }

    // 실제로 로비에 입장(나 혹은 타인)했을 때 호출되는 콜백
    private void OnLobbyEntered(Lobby lobby)
    {
        CurrentLobby = lobby;
        Log($"로비 입장 완료: {lobby.GetData(LobbyNameKey)} (ID: {lobby.Id})");

        // 호스트가 방을 만들 때도 이 함수가 호출되므로 호스트 모드라면 클라이언트 로직 무시
        if (TitleScreenManager.Instance != null &&
            TitleScreenManager.Instance.currentConnectionType == TitleScreenManager.NetworkConnectionType.Host)
        {
            Log("현재 사용자는 호스트이므로 OnLobbyEntered의 클라이언트 접속 로직을 건너뜁니다.");
            return;
        }

        // 클라이언트 자격으로 초대 수락 또는 방 목록을 통해 들어온 경우 UI 전환
        Log("클라이언트 자격으로 로비에 접속했습니다. 대기방 UI를 엽니다.");
        if (TitleScreenManager.Instance != null) TitleScreenManager.Instance.HideTitleScreen();
        if (LobbyUIManager.Instance != null) LobbyUIManager.Instance.OpenLobbyRoom(lobby);

        // 클라이언트 연결 로직 (NGO Client 시작)
        if (!NetworkManager.Singleton.IsHost)
        {
            string hostSteamIDStr = lobby.GetData(HostAddressKey);
            Log($"로비 메타데이터에서 호스트 ID 추출 시도... 결과: {hostSteamIDStr}");

            if (ulong.TryParse(hostSteamIDStr, out ulong hostSteamID))
            {
                if (transport != null)
                {
                    transport.serverId = hostSteamID;
                }

                Log("NGO 클라이언트 시작을 시도합니다...");
                try
                {
                    if (NetworkManager.Singleton.StartClient())
                    {
                        Log($"호스트({hostSteamID})에게 클라이언트 접속 시도 중...");
                    }
                    else
                    {
                        LogError("NGO 클라이언트 시작에 실패했습니다.");
                        RevertToTitleScreen();
                    }
                }
                catch (System.Exception e)
                {
                    LogError($"NGO 클라이언트 시작 중 예외 발생: {e.Message}");
                    RevertToTitleScreen();
                }
            }
            else
            {
                LogError("로비 데이터에서 호스트 SteamID를 찾을 수 없습니다. 파싱 실패.");
                RevertToTitleScreen();
            }
        }
    }

    private void OnLobbyInvite(Friend friend, Lobby lobby)
    {
        Log($"{friend.Name}님이 게임에 초대했습니다. 로비 ID: {lobby.Id}");
    }

    private void OnMemberJoined(Lobby lobby, Friend friend)
    {
        Log($"새로운 플레이어 입장: {friend.Name}");
    }

    private void OnMemberLeft(Lobby lobby, Friend friend)
    {
        Log($"플레이어 퇴장: {friend.Name}");
    }

    #endregion

    #region UI Actions (Called by UI Managers' code)

    // [TitleScreenManager 등에서 호출] 호스트 시작 및 로비 생성 로직
    public async void StartHostWithLobby()
    {
        Log("호스트 시작 및 로비 생성을 시도합니다...");

        if (!Steamworks.SteamClient.IsValid)
        {
            LogError("Steam API가 초기화되지 않았습니다. Steam이 실행 중인지 확인하세요.");
            RevertToTitleScreen();
            return;
        }

        // [방어 코드] 이전 실행의 찌꺼기 네트워크 세션이 있다면 강제 종료
        if (NetworkManager.Singleton.IsListening)
        {
            LogWarning("이미 활성화된 네트워크 세션이 발견되었습니다. 강제 셧다운을 진행합니다...");
            NetworkManager.Singleton.Shutdown();
            await Task.Delay(1000);
        }

        Log($"SteamMatchmaking.CreateLobbyAsync 호출 중... (최대 {maxPlayers}명)");

        var lobbyOpt = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);

        if (!lobbyOpt.HasValue)
        {
            LogError("로비 생성 비동기 작업 실패 (응답 없음)");
            RevertToTitleScreen();
            return;
        }

        CurrentLobby = lobbyOpt.Value;
        CurrentLobby?.SetPublic();
        CurrentLobby?.SetJoinable(true);
        CurrentLobby?.SetData(HostAddressKey, Steamworks.SteamClient.SteamId.ToString());
        CurrentLobby?.SetData(LobbyNameKey, $"{Steamworks.SteamClient.Name}의 대전 게임");
        CurrentLobby?.SetData(GameIdKey, GameIdValue);

        Log($"Steam 로비 생성 완료. (호스트 ID: {Steamworks.SteamClient.SteamId})");
        Log("NGO 호스트 가동을 시도합니다...");

        try
        {
            if (NetworkManager.Singleton.StartHost())
            {
                Log("NGO 호스트 가동 성공! 대기방 UI를 엽니다.");

                if (LobbyUIManager.Instance != null && CurrentLobby.HasValue)
                {
                    LobbyUIManager.Instance.OpenLobbyRoom(CurrentLobby.Value);
                }
                else
                {
                    LogError("LobbyUIManager가 씬에 없거나 할당되지 않아 패널을 켤 수 없습니다!");
                }
            }
            else
            {
                LogError("NGO 호스트 시작 실패 (NetworkManager.StartHost 반환값: false)");
                RevertToTitleScreen();
            }
        }
        catch (System.Exception e)
        {
            LogError($"NGO 호스트 시작 중 예외 발생: {e.Message}");
            LogError("❗ [Invalid Socket 에러 발생 시] 유니티 에디터를 완전히 종료하고 다시 시작해야 해결됩니다.");
            RevertToTitleScreen();
        }
    }

    // [LobbyUIManager의 방 목록에서 호출] 특정 로비에 접속을 시도하는 public 메서드
    public async void JoinLobby(Lobby lobby)
    {
        Log($"로비 {lobby.Id}에 접속을 시도합니다...");

        var result = await lobby.Join();

        if (result != RoomEnter.Success)
        {
            LogError($"로비 접속 실패: {result}");
            return;
        }
    }

    // [개선됨] 씬 복구 및 안전한 셧다운을 총괄하는 함수
    private void RevertToTitleScreen()
    {
        // 무한 루프 셧다운 방지
        if (isDisconnecting) return;
        isDisconnecting = true;

        LogWarning("에러 복구/연결 종료 로직 가동: 자원을 해제하고 타이틀 화면으로 돌아갑니다.");

        // Steam 관련 자원 해제
        if (Steamworks.SteamClient.IsValid)
        {
            CurrentLobby?.Leave();
        }
        CurrentLobby = null;

        // NGO 네트워크 셧다운
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        // 앱이 강제 종료되는 중이 아니라면 씬(화면) 복구 시도
        if (!isQuitting)
        {
            // 현재 씬이 메인 메뉴(0번)가 아니라 게임 씬에 있다면 강제로 0번 씬 로드
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex != 0)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(0);
            }
            else
            {
                // 이미 메인 화면 씬이라면 UI만 복구 (대기방 UI 등 끄기)
                if (TitleScreenManager.Instance != null)
                {
                    TitleScreenManager.Instance.ShowTitleScreen();
                }
            }
        }

        isDisconnecting = false;
    }

    // [LobbyUIManager 등에서 호출] 로비 퇴장 및 세션 종료
    public void LeaveLobby()
    {
        Log("로비 퇴장을 요청받았습니다. 안전하게 셧다운을 시도합니다.");
        RevertToTitleScreen();
    }

    private void OnApplicationQuit()
    {
        // 앱이 강제 종료될 때 씬 이동 에러를 막기 위한 플래그
        isQuitting = true;
        LeaveLobby();
    }

    #endregion
}