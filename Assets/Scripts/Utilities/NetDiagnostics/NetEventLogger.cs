using System;
using Unity.Netcode;
using UnityEngine;

namespace NetDiag
{
    /// <summary>
    /// [Step 0 · 계측] NetEventLogger — M3(끊김 이벤트 정합) · M8(재호스팅) 측정기.
    /// 실행계획 v1.1 §0.A.2-1.
    ///
    /// 기록 대상 (전부 구독만 — 게임 로직 무침습):
    ///  · NetworkManager.OnClientConnectedCallback / OnClientDisconnectCallback (NGO 계층)
    ///  · NetworkManager.OnServerStarted / OnServerStopped / OnClientStarted / OnClientStopped
    ///  · NetworkTransport.OnTransportEvent (전송 계층 원시 이벤트 — Connect/Disconnect)
    ///
    /// P0-2 베이스라인 증거 수집 원리:
    ///  ServerCallbacks.OnDisconnected(전송 수명주기 로그, events.csv의 TRANSPORT-RAW 행)와
    ///  그 직후 OnTransportEvent가 발화하는 NetworkEvent(이 로거의 TRANSPORT-EVT 행)를 대조하면,
    ///  "물리적 끊김 → NGO에는 Connect로 보고" 라는 오발화가 타임라인에 그대로 남는다.
    ///  Data 이벤트는 행 단위로 기록하지 않고 카운터로만 집계한다 (플러딩 방지).
    /// </summary>
    public class NetEventLogger : MonoBehaviour
    {
        NetworkManager hookedNm;
        NetworkTransport hookedTransport;

        void Update()
        {
            var nm = NetworkManager.Singleton;

            // NetworkManager 인스턴스 교체(재호스팅·씬 전환) 추적
            if (nm != hookedNm)
            {
                Unhook();
                if (nm != null) HookNetworkManager(nm);
            }

            // Transport는 NetworkManager보다 늦게 준비될 수 있어 별도 추적
            if (nm != null)
            {
                var tr = nm.NetworkConfig != null ? nm.NetworkConfig.NetworkTransport : null;
                if (tr != hookedTransport)
                {
                    UnhookTransport();
                    if (tr != null) HookTransport(tr);
                }
            }
        }

        void OnDestroy() => Unhook();

        // -------------------------------------------------------------------
        void HookNetworkManager(NetworkManager nm)
        {
            hookedNm = nm;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            nm.OnServerStarted += OnServerStarted;
            nm.OnServerStopped += OnServerStopped;
            nm.OnClientStarted += OnClientStarted;
            nm.OnClientStopped += OnClientStopped;
            NetDiagnostics.Event("NGO", "NetEventLogger hooked NetworkManager");
        }

        void HookTransport(NetworkTransport tr)
        {
            hookedTransport = tr;
            tr.OnTransportEvent += OnTransportEvent;
            NetDiagnostics.Event("TRANSPORT-EVT", $"NetEventLogger hooked transport ({tr.GetType().Name})");
        }

        void Unhook()
        {
            if (hookedNm != null)
            {
                try
                {
                    hookedNm.OnClientConnectedCallback -= OnClientConnected;
                    hookedNm.OnClientDisconnectCallback -= OnClientDisconnected;
                    hookedNm.OnServerStarted -= OnServerStarted;
                    hookedNm.OnServerStopped -= OnServerStopped;
                    hookedNm.OnClientStarted -= OnClientStarted;
                    hookedNm.OnClientStopped -= OnClientStopped;
                }
                catch { }
                hookedNm = null;
            }
            UnhookTransport();
        }

        void UnhookTransport()
        {
            if (hookedTransport != null)
            {
                try { hookedTransport.OnTransportEvent -= OnTransportEvent; } catch { }
                hookedTransport = null;
            }
        }

        // -------------------------------------------------------------------
        // 전송 계층 원시 이벤트 — P0-2 오발화가 그대로 기록되는 지점
        // -------------------------------------------------------------------
        void OnTransportEvent(NetworkEvent eventType, ulong clientId, ArraySegment<byte> payload, float receiveTime)
        {
            try
            {
                if (eventType == NetworkEvent.Data)
                {
                    // 데이터 이벤트는 카운터로만 (행 기록 시 플러딩)
                    NetDiagnostics.IncrementCounter("transport.evt.data");
                    NetDiagnostics.AddCounter("transport.evt.data.bytes", payload.Count);
                    return;
                }

                NetDiagnostics.IncrementCounter($"transport.evt.{eventType}");
                NetDiagnostics.Event("TRANSPORT-EVT", $"{eventType} clientId={clientId} payload={payload.Count}B recvTime={receiveTime:F3}");
            }
            catch { /* 계측 핸들러는 절대 예외를 전파하지 않는다 */ }
        }

        // -------------------------------------------------------------------
        // NGO 계층 이벤트
        // -------------------------------------------------------------------
        void OnClientConnected(ulong clientId)
        {
            NetDiagnostics.IncrementCounter("ngo.clientConnected");
            NetDiagnostics.Event("NGO", $"OnClientConnectedCallback clientId={clientId} " +
                $"connectedCount={(NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer ? NetworkManager.Singleton.ConnectedClients.Count : -1)}");
        }

        void OnClientDisconnected(ulong clientId)
        {
            NetDiagnostics.IncrementCounter("ngo.clientDisconnected");
            string reason = "";
            try { reason = NetworkManager.Singleton != null ? NetworkManager.Singleton.DisconnectReason : ""; } catch { }
            NetDiagnostics.Event("NGO", $"OnClientDisconnectCallback clientId={clientId} reason=\"{reason}\"");
        }

        void OnServerStarted()
        {
            NetDiagnostics.IncrementCounter("ngo.serverStarted");
            NetDiagnostics.Event("NGO", "OnServerStarted");
        }

        void OnServerStopped(bool wasHost)
        {
            NetDiagnostics.IncrementCounter("ngo.serverStopped");
            NetDiagnostics.Event("NGO", $"OnServerStopped wasHost={wasHost}");
        }

        void OnClientStarted()
        {
            NetDiagnostics.Event("NGO", "OnClientStarted");
        }

        void OnClientStopped(bool wasHost)
        {
            NetDiagnostics.Event("NGO", $"OnClientStopped wasHost={wasHost}");
        }
    }
}
