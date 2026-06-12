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

        // TODO: Increase buffer size.
        byte[] buffer = new byte[1024];
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
            // [Step 0 / P2-4 채널화] 패킷당 로그를 조건부 컴파일 + 카운터로 전환 (M9 계측기).
            // 동작 불변 — 버퍼/Assert/Copy 로직은 Step 1(P0-1)에서 수정한다.
#if NETCODE_DEBUG
            Debug.Log("ClientCallbacks: OnMessage");
#endif
            NetDiag.NetDiagnostics.IncrementCounter("transport.recv.client");
            NetDiag.NetDiagnostics.AddCounter("transport.recv.client.bytes", size);
            if (size > buffer.Length) NetDiag.NetDiagnostics.IncrementCounter("transport.recv.client.oversize"); // M2 증거
            Debug.Assert(size <= buffer.Length, "Message size exceeds the max buffer length");

            Marshal.Copy(data, buffer, 0, size);

            transport.InvokeOnTransportEvent(NetworkEvent.Data, transport.ServerClientId, new ArraySegment<byte>(buffer, 0, size), Time.realtimeSinceStartup);
        }
    }

    class ServerCallbacks : ISocketManager
    {
        SteamP2PRelayTransport transport;

        // TODO: Increase buffer size.
        byte[] buffer = new byte[1024];
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
            // [Step 0 계측] 이 직후 발화되는 NetworkEvent가 'Connect'인 것이 P0-2 결함 —
            // events.csv 에서 본 행(TRANSPORT-RAW Disconnected)과 다음 행(TRANSPORT-EVT Connect)의
            // 짝이 베이스라인 증거가 된다. 수정은 Step 1에서 수행 (여기서는 기록만).
            NetDiag.NetDiagnostics.Event("TRANSPORT-RAW", $"Server.OnDisconnected conn={connection.Id} endReason={info.EndReason}");
            connection.Close();
            transport.InvokeOnTransportEvent(NetworkEvent.Connect, connection.Id, emptyPayload, Time.realtimeSinceStartup);
        }

        /// <summary>
        /// Received a message from a connection
        /// </summary>
        public void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            // [Step 0 / P2-4 채널화] 패킷당 로그를 조건부 컴파일 + 카운터로 전환 (M9 계측기).
            // 동작 불변 — 크기 검사 부재(TODO)는 Step 1(P0-1)에서 수정한다.
#if NETCODE_DEBUG
            Debug.Log("ServerCallbacks: OnMessage");
#endif
            NetDiag.NetDiagnostics.IncrementCounter("transport.recv.server");
            NetDiag.NetDiagnostics.AddCounter("transport.recv.server.bytes", size);
            if (size > buffer.Length) NetDiag.NetDiagnostics.IncrementCounter("transport.recv.server.oversize"); // M2 증거

            // TODO: Assert that size <= buffer size
            Marshal.Copy(data, buffer, 0, size);

            transport.InvokeOnTransportEvent(NetworkEvent.Data, connection.Id, new ArraySegment<byte>(buffer, 0, size), Time.realtimeSinceStartup);
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
    override public ulong GetCurrentRtt(ulong clientId) { return 0; }

    /// <summary>
    /// Shuts down the transport
    /// </summary>
    override public void Shutdown()
    {
        Debug.Log("Shutdown.");
        // [Step 0 계측] P1-1 베이스라인: Transport.Shutdown이 Steam API 전체를 종료하는 현행 동작 기록.
        // 수정(소켓만 정리)은 Step 1에서 수행.
        NetDiag.NetDiagnostics.Event("TRANSPORT-RAW", "Transport.Shutdown → SteamClient.Shutdown() 호출 (P1-1 현행)");
        Steamworks.SteamClient.Shutdown();
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

    static SendType CastToSendType(NetworkDelivery networkDelivery)
    {
        // TODO: This mapping might need to be revised.
        SendType sendType = SendType.Unreliable;

        switch (networkDelivery)
        {
            /// <summary>
            /// Unreliable message
            /// </summary>
            case NetworkDelivery.Unreliable:
                break;
            /// <summary>
            /// Unreliable with sequencing
            /// </summary>
            case NetworkDelivery.UnreliableSequenced:
                break;
            /// <summary>
            /// Reliable message
            /// </summary>
            case NetworkDelivery.Reliable:
                sendType |= SendType.Reliable;
                break;
            /// <summary>
            /// Reliable message where messages are guaranteed to be in the right order
            /// </summary>
            case NetworkDelivery.ReliableSequenced:
                sendType |= SendType.Reliable;
                break;
            /// <summary>
            /// A reliable message with guaranteed order with fragmentation support
            /// </summary>
            case NetworkDelivery.ReliableFragmentedSequenced:
                sendType |= SendType.Reliable;
                break;
            default:
                break;
        }

        return sendType;
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
