using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

namespace CaveSystem.Multiplayer
{
    /// <summary>
    /// [Phase 5] Unity 6.3 / NGO 2.0 기반 지형 데이터 동기화 매니저.
    /// 월드 시드 공유, 난입 유저 대응, 클라이언트별 준비 상태(Ready)를 네트워크 변수로 관리합니다.
    /// </summary>
    public class TerrainSyncNetworkManager : NetworkBehaviour
    {
        public static TerrainSyncNetworkManager Instance { get; private set; }

        // --- [NGO 2.0 NetworkVariables] ---
        // Seed: 호스트가 결정하며, 모든 유저가 읽기 가능. 난입(Late-join) 유저도 접속 시 자동으로 현재 값을 수신함.
        public NetworkVariable<int> SyncedWorldSeed = new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // SpawnPos: 확정된 시작 지점을 동기화하여 플레이어의 위치 일관성 유지.
        public NetworkVariable<Vector3> SyncedSpawnPosition = new NetworkVariable<Vector3>(Vector3.zero,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // 준비된 인원 수: UI 표시용 (NetworkVariable을 사용하여 전체 클라이언트가 즉시 확인 가능)
        public NetworkVariable<int> ReadyClientCount = new NetworkVariable<int>(0);

        // --- [Server-Side Only Data] ---
        // 각 클라이언트 ID별 지형 굽기 완료 여부를 추적하는 리스트 (서버 메모리에만 저장)
        private HashSet<ulong> readyClients = new HashSet<ulong>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // [에러 수정] 씬 전환 시 파괴되지 않도록 영속성 부여
            DontDestroyOnLoad(this.gameObject);
        }

        public override void OnNetworkSpawn()
        {
            // [에러 수정] NGO가 씬을 전환할 때 이 NetworkObject를 파괴하지 않도록 강제 설정
            if (NetworkObject != null)
            {
                NetworkObject.DestroyWithScene = false;
            }

            if (IsServer)
            {
                // 서버(호스트)는 새로운 클라이언트가 접속하거나 나갈 때 상태를 갱신해야 함
                NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;

                // 초기 시드 생성 (아직 설정되지 않은 경우)
                if (SyncedWorldSeed.Value == 0)
                {
                    GenerateNewWorldSeed();
                }
            }

            Log($"지형 동기화 서비스 시작 (Local ID: {NetworkManager.Singleton.LocalClientId})");
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        #region 🛠️ Host Control Logic (Server Side)

        /// <summary>
        /// 새로운 월드 시드를 생성하고 모든 유저에게 전파합니다.
        /// </summary>
        public void GenerateNewWorldSeed()
        {
            if (!IsServer) return;

            int newSeed = Random.Range(100000, 999999);
            SyncedWorldSeed.Value = newSeed;

            // 시드가 바뀌면 모든 클라이언트의 준비 상태를 초기화
            readyClients.Clear();
            ReadyClientCount.Value = 0;

            Log($"새로운 월드 시드 발급 완료: {newSeed}");
        }

        /// <summary>
        /// 새로운 유저가 접속하면 서버 사이드 준비 맵을 업데이트합니다.
        /// </summary>
        private void HandleClientConnected(ulong clientId)
        {
            if (!IsServer) return;
            Log($"[Server] 클라이언트 {clientId} 로비 입장. 지형 동기화를 대기합니다.");
            UpdateReadyStatus();
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;
            if (readyClients.Contains(clientId))
            {
                readyClients.Remove(clientId);
            }
            UpdateReadyStatus();
        }

        #endregion

        #region 📡 Synchronization Logic (RPCs)

        /// <summary>
        /// [ServerRpc] 클라이언트가 자신의 GPU로 지형 굽기를 마쳤을 때 서버에 호출합니다.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void ReportTerrainReadyServerRpc(ulong clientId)
        {
            if (!IsServer) return;

            if (!readyClients.Contains(clientId))
            {
                readyClients.Add(clientId);
                Log($"[Server] 클라이언트 {clientId} 지형 프리베이킹 완료.");
                UpdateReadyStatus();
            }
        }

        /// <summary>
        /// 현재 준비된 인원 수와 전체 인원 수를 비교하여 게임 시작 가능 여부를 UI 매니저에 알립니다.
        /// </summary>
        private void UpdateReadyStatus()
        {
            if (!IsServer) return;

            int totalClients = NetworkManager.Singleton.ConnectedClients.Count;
            ReadyClientCount.Value = readyClients.Count;

            // 모든 유저가 준비되었는지 확인 (Interlock)
            bool allReady = (readyClients.Count >= totalClients);

            // LobbyUIManager 등 외부에서 UI 갱신을 원할 때 참조합니다.
        }

        #endregion

        private void Log(string msg) => Debug.Log($"<color=#2ECC71>[TerrainSync]</color> {msg}");
    }
}