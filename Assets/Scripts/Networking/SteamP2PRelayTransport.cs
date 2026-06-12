using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

using Unity.Netcode;
using UnityEngine;

using Steamworks;
using Steamworks.Data;

public class SteamP2PRelayTransport : NetworkTransport
{
    /// <summary>
    /// For clients, this is the steam id we want to connect to.
    /// </summary>
    public ulong serverId = 0;

    /// <summary>
    /// Enables additional debug logs.
    /// </summary>
    public bool debug = false;

    class ClientCallbacks : IConnectionManager
    {
        SteamP2PRelayTransport transport;

        // [Step 1 / P0-1] 1KB 고정 버퍼 제거 — 메시지 실제 크기만큼 정확 할당 방식으로 전환.
        // 고정 버퍼 재사용 시 NGO 내부에서 비동기 소비될 경우의 데이터 경합도 함께 제거된다.
        ArraySegment<byte> emptyPayload = new ArraySegment<byte>();

        public ClientCallbacks(SteamP2PRelayTransport transport)
        {
            this.transport = transport;
        }

        /// <summary>
        /// We started connecting to this guy
        /// </summary>
        public void OnConnecting(ConnectionInfo info)
        {
            Debug.Log("ClientCallbacks: OnConnecting");
        }

        /// <summary>
        /// Called when the connection is fully connected and can start being communicated with
        /// </summary>
        public void OnConnected(ConnectionInfo info)
        {
            Debug.Log("ClientCallbacks: OnConnected");
            transport.InvokeOnTransportEvent(NetworkEvent.Connect, transport.ServerClientId, emptyPayload, Time.realtimeSinceStartup);
        }

        /// <summary>
        /// We got disconnected
        /// </summary>
        public void OnDisconnected(ConnectionInfo info)
        {
            Debug.Log("ClientCallbacks: OnDisconnected");
            NetDiag.NetDiagnostics.Event("TRANSPORT-RAW", $"Client.OnDisconnected endReason={info.EndReason}"); // [Step 0 계측]
            transport.InvokeOnTransportEvent(NetworkEvent.Disconnect, transport.ServerClientId, emptyPayload, Time.realtimeSinceStartup);
        }

        /// <summary>
        /// Received a message
        /// </summary>
        public unsafe void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            // [Step 0 / P2-4 채널화] 패킷당 로그는 NETCODE_DEBUG 전용 + 카운터 집계 (M9).
#if NETCODE_DEBUG
            Debug.Log("ClientCallbacks: OnMessage");
#endif
            NetDiag.NetDiagnostics.IncrementCounter("transport.recv.client");
            NetDiag.NetDiagnostics.AddCounter("transport.recv.client.bytes", size);

            // [Step 1 / P0-1] 메시지 크기만큼 정확 할당 — 대형 메시지(씬 이벤트·NetworkList
            // 초기 스냅샷) 잘림/Assert 제거. 크기 상한은 Steam 송신 한계(512KB)가 자연 상한.
            byte[] payload = new byte[size];
            Marshal.Copy(data, payload, 0, size);

            transport.InvokeOnTransportEvent(NetworkEvent.Data, transport.ServerClientId, new ArraySegment<byte>(payload, 0, size), Time.realtimeSinceStartup);
        }
    }

    class ServerCallbacks : ISocketManager
    {
        SteamP2PRelayTransport transport;

        // [Step 1 / P0-1] 1KB 고정 버퍼 제거 — 클라이언트 경로와 동일하게 정확 할당으로 통일.
        ArraySegment<byte> emptyPayload = new ArraySegment<byte>();

        public ServerCallbacks(SteamP2PRelayTransport transport)
        {
            Debug.Log("Instantiating ServerCallbacks");
            this.transport = transport;
        }

        /// <summary>
        /// Must call Accept or Close on the connection within a second or so
        /// </summary>
        public void OnConnecting(Connection connection, ConnectionInfo info)
        {
            Debug.Log("ServerCallbacks: OnConnecting");
            connection.Accept();
        }

        /// <summary>
        /// Called when the connection is fully connected and can start being communicated with
        /// </summary>
        public void OnConnected(Connection connection, ConnectionInfo info)
        {
            Debug.Log("ServerCallbacks: OnConnected");
            transport.InvokeOnTransportEvent(NetworkEvent.Connect, connection.Id, emptyPayload, Time.realtimeSinceStartup);
        }

        /// <summary>
        /// Called when the connection leaves. Must call Close on the connection
        /// </summary>
        public void OnDisconnected(Connection connection, ConnectionInfo info)
        {
            Debug.Log("ServerCallbacks: OnDisconnected");
            NetDiag.NetDiagnostics.Event("TRANSPORT-RAW", $"Server.OnDisconnected conn={connection.Id} endReason={info.EndReason}");
            connection.Close();
            // [Step 1 / P0-2] Connect → Disconnect 수정. 기존에는 클라이언트 이탈이 NGO에
            // '신규 접속'으로 보고되어 유령 클라이언트·ready맵 미정리·복귀 로직 미동작을 유발했다.
            // events.csv 에서 본 행 직후 TRANSPORT-EVT Disconnect 가 짝으로 기록되어야 정상 (M3).
            transport.InvokeOnTransportEvent(NetworkEvent.Disconnect, connection.Id, emptyPayload, Time.realtimeSinceStartup);
        }

        /// <summary>
        /// Received a message from a connection
        /// </summary>
        public void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            // [Step 0 / P2-4 채널화] 패킷당 로그는 NETCODE_DEBUG 전용 + 카운터 집계 (M9).
#if NETCODE_DEBUG
            Debug.Log("ServerCallbacks: OnMessage");
#endif
            NetDiag.NetDiagnostics.IncrementCounter("transport.recv.server");
            NetDiag.NetDiagnostics.AddCounter("transport.recv.server.bytes", size);

            // [Step 1 / P0-1] 정확 할당 — 기존에는 크기 검사조차 없이(TODO 방치) 1KB 버퍼에
            // Marshal.Copy 하여 대형 메시지가 즉시 손상되었다. 클라 경로와 동일 로직으로 통일.
            byte[] payload = new byte[size];
            Marshal.Copy(data, payload, 0, size);

            transport.InvokeOnTransportEvent(NetworkEvent.Data, connection.Id, new ArraySegment<byte>(payload, 0, size), Time.realtimeSinceStartup);
        }
    }

    private bool isClient = false;

    private SocketManager socketManager = null;
    private ConnectionManager clientConnection = null;

    /// <summary>
    /// A constant `clientId` that represents the server
    /// When this value is found in methods such as `Send`, it should be treated as a placeholder that means "the server"
    /// </summary>
    override public ulong ServerClientId { get => 0; }

    /// <summary>
    /// Send a payload to the specified clientId, data and channelName.
    /// </summary>
    /// <param name="clientId">The clientId to send to</param>
    /// <param name="payload">The data to send</param>
    /// <param name="networkDelivery">The delivery type (QoS) to send data with</param>
    public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
    {
        // [Step 0 계측] 송신량 집계 (M6 보조 지표)
        NetDiag.NetDiagnostics.IncrementCounter("transport.send");
        NetDiag.NetDiagnostics.AddCounter("transport.send.bytes", payload.Count);

        SendType sendType = CastToSendType(networkDelivery);

        if (isClient)
        {
            clientConnection?.Connection.SendMessage(payload.Array, payload.Offset, payload.Count, sendType);
        }
        else
        {
            if (socketManager == null) return;

            foreach (var connection in socketManager.Connected)
            {
                if (connection.Id != clientId) continue;

                connection.SendMessage(payload.Array, payload.Offset, payload.Count, sendType);
            }
        }
    }

    /// <summary>
    /// Polls for incoming events, with an extra output parameter to report the precise time the event was received.
    /// </summary>
    /// <param name="clientId">The clientId this event is for</param>
    /// <param name="payload">The incoming data payload</param>
    /// <param name="receiveTime">The time the event was received, as reported by Time.realtimeSinceStartup.</param>
    /// <returns>Returns the event type</returns>
    override public NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
    {
        clientId = 0;
        receiveTime = Time.realtimeSinceStartup;
        payload = ArraySegment<byte>.Empty;

        return NetworkEvent.Nothing;
    }

    /// <summary>
    /// Connects client to the server
    /// </summary>
    override public bool StartClient()
    {
        try
        {
            clientConnection =
                Steamworks.SteamNetworkingSockets.ConnectRelay<ConnectionManager>(serverId, 0);
            clientConnection.Interface = new ClientCallbacks(this);
            isClient = true;

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("StartClient failed with exception " + e);
            return false;
        }
    }

    /// <summary>
    /// Starts to listening for incoming clients
    /// </summary>
    override public bool StartServer()
    {
        try
        {
            socketManager = Steamworks.SteamNetworkingSockets.CreateRelaySocket<SocketManager>(0);
            socketManager.Interface = new ServerCallbacks(this);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("StartServer failed with exception " + e);
            return false;
        }
    }

    /// <summary>
    /// Disconnects a client from the server
    /// </summary>
    /// <param name="clientId">The clientId to disconnect</param>
    override public void DisconnectRemoteClient(ulong clientId)
    {
        Debug.Log("DisconnectRemoteClient.");

        if (socketManager == null) return;

        foreach (var connection in socketManager.Connected)
        {
            if (connection.Id != clientId) continue;

            connection.Close();
        }
    }

    /// <summary>
    /// Disconnects the local client from the server
    /// </summary>
    override public void DisconnectLocalClient()
    {
        Debug.Log("DisconnectLocalClient.");
        clientConnection?.Close();
    }

    /// <summary>
    /// Gets the round trip time for a specific client. This method is optional
    /// </summary>
    /// <param name="clientId">The clientId to get the RTT from</param>
    /// <returns>Returns the round trip time in milliseconds </returns>
    override public ulong GetCurrentRtt(ulong clientId)
    {
        // [Step 1 / P0-4] 항상 0 반환 → Steam 연결 상태의 실측 Ping(ms) 반환으로 교체.
        // NGO 시간 동기화·버퍼링 품질과 RNSM HUD의 RTT 칸(M1)이 이 값을 사용한다.
        try
        {
            if (isClient)
            {
                // 클라이언트는 서버와의 단일 연결만 보유 (clientId == ServerClientId)
                if (clientConnection != null)
                    return (ulong)Mathf.Max(0, clientConnection.Connection.QuickStatus().Ping);
            }
            else if (socketManager != null)
            {
                foreach (var connection in socketManager.Connected)
                {
                    if (connection.Id != clientId) continue;
                    return (ulong)Mathf.Max(0, connection.QuickStatus().Ping);
                }
            }
        }
        catch
        {
            // 연결 해제 직후 등 상태 조회 실패 시 0 (NGO 규약상 안전값)
        }
        return 0;
    }

    /// <summary>
    /// Shuts down the transport
    /// </summary>
    override public void Shutdown()
    {
        Debug.Log("Shutdown.");
        // [Step 1 / P1-1] Steam API 전체 종료(SteamClient.Shutdown) 금지 — 소켓/연결만 정리.
        // Steam API의 초기화·종료 수명은 SteamClient 컴포넌트가 단독 소유한다.
        // 기존에는 방 퇴장(NetworkManager.Shutdown → Transport.Shutdown)마다 Steam API 전체가
        // 꺼져 재호스팅이 불안정했다 (M8).
        NetDiag.NetDiagnostics.Event("TRANSPORT-RAW", "Transport.Shutdown — 소켓/연결만 정리 (P1-1 수정)");

        try { clientConnection?.Close(); } catch { }
        try { socketManager?.Close(); } catch { }
        clientConnection = null;
        socketManager = null;
        isClient = false;
    }


    // 방 퇴장 시 NullReferenceException 에러 해결(소켓이 닫히면서 Receive()에서 발생하는 에러)
    void LateUpdate()
    {
        // 스팀 API가 꺼졌거나 네트워크가 닫혀있다면 수신 시도를 아예 하지 않음
        if (!Steamworks.SteamClient.IsValid || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return;

        try
        {
            socketManager?.Receive();
            clientConnection?.Receive();
        }
        catch (System.NullReferenceException)
        {
            // 방을 나갈 때 소켓이 닫히면서 발생하는 정상적인 Null 에러이므로 무시 (콘솔 도배 방지)
        }
    }

    // =========================================================================
    // [Step 1 / P1-2] NGO NetworkDelivery ↔ Steam SendType 매핑표 (교정판)
    // ─────────────────────────────────────────────────────────────────────────
    // SteamNetworkingSockets에는 '순서 보장 + 비신뢰(UnreliableSequenced)' 옵션이 없다.
    // (Unreliable 메시지는 드롭 가능 + 순서 역전 수신 가능)
    // 기존 매핑은 UnreliableSequenced → Unreliable 로 두어 순서 의존 메시지의
    // 역전 수신을 허용했다. 교정: 순서 보장이 의미의 핵심이므로 Reliable 로 승격한다.
    //
    //  NGO NetworkDelivery            →  Steam SendType   근거
    //  Unreliable                     →  Unreliable       의미 일치
    //  UnreliableSequenced            →  Reliable         ★승격 (Steam에 sequenced-unreliable 부재)
    //  Reliable                       →  Reliable         Steam reliable은 순서도 보장
    //  ReliableSequenced              →  Reliable         〃
    //  ReliableFragmentedSequenced    →  Reliable         Steam은 메시지당 512KB까지 네이티브 지원
    // =========================================================================
    static SendType CastToSendType(NetworkDelivery networkDelivery)
    {
        switch (networkDelivery)
        {
            case NetworkDelivery.Unreliable:
                return SendType.Unreliable;

            case NetworkDelivery.UnreliableSequenced:
                // [P1-2 교정] 순서 비보장 매핑 → Reliable 승격
                return SendType.Reliable;

            case NetworkDelivery.Reliable:
            case NetworkDelivery.ReliableSequenced:
            case NetworkDelivery.ReliableFragmentedSequenced:
                return SendType.Reliable;

            default:
                return SendType.Reliable; // 알 수 없는 신규 값은 안전 측(신뢰)으로
        }
    }

    void DebugOutput(NetDebugOutput type, string text)
    {
        Debug.Log(text);
    }

    public override void Initialize(NetworkManager networkManager = null)
    {
        Debug.Log("Initialize.");
        Steamworks.SteamNetworkingUtils.InitRelayNetworkAccess();

        if (debug)
        {
            SteamNetworkingUtils.DebugLevel = NetDebugOutput.Debug;
            SteamNetworkingUtils.OnDebugOutput += DebugOutput;
        }
    }
}
