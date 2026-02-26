using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;

/* * [확장된 SteamLobbyManager - 싱글톤 적용 및 예외 처리 강화]
 * 이 스크립트는 Facepunch.Steamworks와 UnityNetcodeSteamP2PRelayTransport를 
 * 사용하는 환경에 최적화되었으며, NGO와의 원활한 연동 및 상세한 로비 관리를 지원합니다.
 * * [중요 연동 흐름]
 * 1. 호스트: StartHostWithLobby() 호출 -> 로비 생성 -> NGO 호스트 시작 -> Lobby Room UI 오픈
 * 2. 클라이언트: TitleScreen에서 Join Room 클릭 -> LobbyUIManager.OpenRoomBrowser() 호출 (UI 레이어)
 * 3. 클라이언트: Browser에서 방 선택 -> JoinLobby(lobby) 호출 -> OnLobbyEntered 콜백 -> NGO 클라이언트 시작 -> Lobby Room UI 오픈
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

    // [추가됨] 전 세계 480(Spacewar) 앱 유저들과 우리 게임을 구분하기 위한 고유 식별 키
    public const string GameIdKey = "GameUniqueId";
    public const string GameIdValue = "PennutButterProject";

    [Header("Lobby Settings")]
    [SerializeField] private int maxPlayers = 4; // 최대 접속 인원

    // 현재 내가 들어가 있는 로비 정보 (Nullable)
    public Lobby? CurrentLobby { get; private set; }

    // NGO와 연동된 Transport 컴포넌트 참조
    private SteamP2PRelayTransport transport;

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

    private void Update()
    {
        /* * Facepunch 라이브러리는 명시적으로 매 프레임 콜백을 호출해야 합니다.
         * SteamClient가 유효하지 않은 셧다운 상태에서는 호출을 건너뛰어 예외를 방지합니다.
         */
        if (Steamworks.SteamClient.IsValid)
        {
            Steamworks.SteamClient.RunCallbacks();
        }
    }

    private void OnEnable()
    {
        // Instance가 this일 때만 이벤트 구독
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
                    // 트랜스포트의 대상 주소를 호스트의 SteamID로 설정
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

        // [추가됨] 우리 게임 전용 방임을 명시하는 필터용 데이터 세팅
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

        // 성공 시 OnLobbyEntered 콜백이 자동으로 호출되어 이후 과정을 처리합니다.
    }

    // 에러 발생 시 UI를 원상복구하고 로비를 빠져나오는 안전망 함수
    private void RevertToTitleScreen()
    {
        LogWarning("에러 복구 로직 가동: 로비를 파괴하고 타이틀 화면으로 돌아갑니다.");

        // Steam 관련 자원 해제
        if (Steamworks.SteamClient.IsValid)
        {
            CurrentLobby?.Leave();
        }
        CurrentLobby = null;

        // UI 복구
        if (TitleScreenManager.Instance != null)
        {
            TitleScreenManager.Instance.ShowTitleScreen();
        }
    }

    // [LobbyUIManager 등에서 호출] 로비 퇴장 및 세션 종료
    public void LeaveLobby()
    {
        Log("로비 퇴장 및 NGO 셧다운을 시도합니다.");

        // [중요] 1. 먼저 NGO 네트워크부터 셧다운하여 트랜스포트의 패킷 수신 작업을 중단시킵니다.
        // Singleton이 null일 수도 있으므로 안전하게 접근합니다.
        var networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }

        // [중요] 2. 그 다음 Steam 로비를 떠납니다. 
        // SteamClient 자체가 이미 종료되었을 경우(OnApplicationQuit 등) 예외를 피하기 위해 IsValid를 확인합니다.
        if (Steamworks.SteamClient.IsValid)
        {
            CurrentLobby?.Leave();
        }
        CurrentLobby = null;

        Log("로비에서 퇴장했습니다.");
    }

    private void OnApplicationQuit()
    {
        // 앱이 강제 종료될 때도 안전한 셧다운 시퀀스를 따릅니다.
        LeaveLobby();
    }

    #endregion
}