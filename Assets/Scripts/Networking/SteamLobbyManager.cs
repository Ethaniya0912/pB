using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Generic;

/* * [확장된 SteamLobbyManager - 싱글톤 적용]
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

    [Header("Lobby Settings")]
    [SerializeField] private int maxPlayers = 4; // 최대 접속 인원

    // 현재 내가 들어가 있는 로비 정보 (Nullable)
    public Lobby? CurrentLobby { get; private set; }

    // NGO와 연동된 Transport 컴포넌트 참조
    private SteamP2PRelayTransport transport;

    // --- 디버깅용 래퍼 함수 ---
    private void Log(string message)
    {
        if (showDebugLogs) Debug.Log($"[SteamLobbyManager] {message}");
    }

    private void LogError(string message)
    {
        if (showDebugLogs) Debug.LogError($"[SteamLobbyManager] {message}");
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
        /* * [중요] Facepunch 라이브러리는 수동으로 콜백을 호출해야 합니다.
         * 트랜스포트의 SteamClient 컴포넌트와 이름이 겹칠 경우를 대비해 
         * Steamworks.SteamClient로 명시적 선언을 유지합니다.
         */
        if (Steamworks.SteamClient.IsValid)
        {
            Steamworks.SteamClient.RunCallbacks();
        }
    }

    private void OnEnable()
    {
        // Instance가 this일 때만 이벤트 구독 (중복 객체에서 이벤트 여러번 구독되는 것 방지)
        if (Instance != this) return;

        Log("OnEnable: 이벤트 콜백 구독을 시작합니다.");
        // SteamMatchmaking 이벤트 구독: 로비의 생성, 입장, 초대 등을 감지합니다.
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
        // 오브젝트 파괴 시 이벤트 구독 해제 (메모리 누수 방지)
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

        // 만약 내가 호스트가 아니라면(클라이언트라면), 로비 데이터에서 호스트 주소를 읽어옵니다.
        if (!NetworkManager.Singleton.IsHost)
        {
            string hostSteamIDStr = lobby.GetData(HostAddressKey);
            Log($"로비 메타데이터에서 호스트 ID 추출 시도... 결과: {hostSteamIDStr}");

            if (ulong.TryParse(hostSteamIDStr, out ulong hostSteamID))
            {
                // 트랜스포트의 대상 주소를 호스트의 SteamID로 설정합니다.
                // 주의: SteamP2PRelayTransport의 실제 연결 변수명(예: TargetSteamID)을 확인하세요.
                // transport.TargetSteamID = hostSteamID; 

                // NGO 클라이언트 가동
                Log("NGO 클라이언트 시작을 시도합니다...");
                if (NetworkManager.Singleton.StartClient())
                {
                    Log($"호스트({hostSteamID})에게 클라이언트 접속 시도 중...");
                }
                else
                {
                    LogError("NGO 클라이언트 시작에 실패했습니다.");
                }
            }
            else
            {
                LogError("로비 데이터에서 호스트 SteamID를 찾을 수 없습니다. 파싱 실패.");
            }
        }
        else
        {
            Log("현재 인스턴스가 호스트이므로 클라이언트 접속 로직을 건너뜁니다.");
        }
    }

    // 친구로부터 초대를 받았을 때 호출되는 콜백
    private void OnLobbyInvite(Friend friend, Lobby lobby)
    {
        Log($"{friend.Name}님이 게임에 초대했습니다. 로비 ID: {lobby.Id}");
        // 여기에 초대를 수락할지 묻는 UI 팝업을 띄우는 로직을 추가할 수 있습니다.
        // 예: SteamMatchmaking.JoinLobbyAsync(lobby.Id);
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

    #region UI Buttons Actions

    // [UI 버튼 등에서 호출용] 호스트 시작 및 로비 생성 로직
    public async void StartHostWithLobby()
    {
        Log("호스트 시작 및 로비 생성을 시도합니다...");

        if (!Steamworks.SteamClient.IsValid)
        {
            LogError("Steam API가 초기화되지 않았습니다. Steam이 실행 중인지 확인하세요.");
            return;
        }

        Log($"SteamMatchmaking.CreateLobbyAsync 호출 중... (최대 {maxPlayers}명)");

        // 최대 플레이어 수에 맞춰 비동기 로비 생성
        var lobbyOpt = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);

        if (!lobbyOpt.HasValue)
        {
            LogError("로비 생성 비동기 작업 실패 (응답 없음)");
            return;
        }

        CurrentLobby = lobbyOpt.Value;

        // 로비 속성 설정
        CurrentLobby?.SetPublic(); // 검색 가능하게 설정
        CurrentLobby?.SetJoinable(true); // 참여 가능하게 설정

        // 로비 메타데이터 설정 (다른 유저가 주소를 알 수 있게 함)
        CurrentLobby?.SetData(HostAddressKey, Steamworks.SteamClient.SteamId.ToString());
        CurrentLobby?.SetData(LobbyNameKey, $"{Steamworks.SteamClient.Name}의 대전 게임");

        Log($"Steam 로비 생성 완료. 메타데이터 설정 완료. (호스트 ID: {Steamworks.SteamClient.SteamId})");

        Log("NGO 호스트 가동을 시도합니다...");
        // NGO의 Host 가동 (서버 기능 시작)
        if (NetworkManager.Singleton.StartHost())
        {
            Log("NGO 호스트가 가동되었습니다. 이제 플레이어를 기다립니다.");
        }
        else
        {
            LogError("NGO 호스트 시작 실패");
        }
    }

    // 로비 떠나기 기능 (필요 시 호출)
    public void LeaveLobby()
    {
        Log("로비 퇴장 및 NGO 셧다운을 시도합니다.");
        CurrentLobby?.Leave();
        CurrentLobby = null;

        if (NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        Log("로비에서 퇴장했습니다.");
    }

    #endregion
}