using UnityEngine;
using Unity.Multiplayer.Tools.NetStats;
using Unity.Multiplayer.Tools.NetStatsMonitor;
using Unity.Multiplayer.Tools.MetricTypes;

namespace NetDiag
{
    /// <summary>
    /// [Step 0 · 계측] RNSM(Runtime Net Stats Monitor) HUD 런타임 부착.
    /// 실행계획 v1.1 §0.A.1 — RTT(M1)·송수신 바이트(M6 보조)를 인게임 HUD로 표시한다.
    /// 게임 로직 무침습: NetDiagnosticsBootstrap 이 만든 숨은 GO 에만 부착된다.
    ///
    /// RNSM 의 RTT 는 NGO 가 transport.GetCurrentRtt(P0-4 구현됨)로부터 채운다.
    /// 단일 에디터(loopback)에서는 0 근처가 정상 — NetSim(F8) 활성 시 주입 지연이 추종되는지가
    /// A1·A2·P0-4 통합 교차 검증점이다.
    /// </summary>
    public class RnsmHud : MonoBehaviour
    {
        public RuntimeNetStatsMonitor Monitor { get; private set; }

        NetStatsMonitorConfiguration _config;

        void Awake()
        {
            Monitor = gameObject.AddComponent<RuntimeNetStatsMonitor>();
            _config = BuildConfiguration();
            Monitor.Configuration = _config;
            Monitor.ApplyConfiguration();
        }

        void OnDestroy()
        {
            // 런타임 생성 config(SO, DontSave)는 GO 파괴·플레이 종료로 정리되지 않는다 — 누수 방지.
            if (_config != null) DestroyImmediate(_config);
        }

        void Update()
        {
            // F6 = HUD 표시 토글 (F8=NetSim, F9=경계 스윕, F10=soak 과 충돌 없음).
            if (Input.GetKeyDown(KeyCode.F6) && Monitor != null)
                Monitor.Visible = !Monitor.Visible;
        }

        static NetStatsMonitorConfiguration BuildConfiguration()
        {
            var config = ScriptableObject.CreateInstance<NetStatsMonitorConfiguration>();
            config.hideFlags = HideFlags.DontSave;

            config.DisplayElements.Add(Counter("RTT (ms)", DirectedMetricType.RttToServer));
            config.DisplayElements.Add(Counter("Sent", DirectedMetricType.TotalBytesSent));
            config.DisplayElements.Add(Counter("Recv", DirectedMetricType.TotalBytesReceived));

            config.OnConfigurationModified();
            return config;
        }

        static DisplayElementConfiguration Counter(string label, DirectedMetricType metric)
        {
            var el = new DisplayElementConfiguration
            {
                Type = DisplayElementType.Counter,
                Label = label,
            };
            el.Stats.Add(MetricId.Create(metric));
            return el;
        }
    }
}
