using UnityEngine;

namespace NetDiag
{
    /// <summary>
    /// [Step 0 · 계측] 네트워크 프로파일 프리셋(PROF-G/A/B)과 런타임 토글.
    /// 실행계획 v1.1 §2 네트워크 프로파일을 코드 프리셋으로 보유하고, F8 키로 활성 프로파일을
    /// 순환시켜 <see cref="SteamP2PRelayTransport"/> 수신 경로의 지연/지터 주입을 제어한다.
    ///
    /// 손실(loss)은 주입하지 않는다 — 커스텀 Steam transport의 OnMessage는 Steam 신뢰성 계층
    /// 이후 위치라, 여기서 reliable 메시지를 드롭하면 영구 유실되어 NGO 세션이 깨진다.
    /// PROF-A/B의 손실률(2~5%)은 OS레벨 Clumsy로 측정 시 보완한다(계획 §2 대안).
    /// </summary>
    public readonly struct NetSimPreset
    {
        public readonly string Name;
        public readonly int RttMs;
        public readonly int JitterMs;

        public NetSimPreset(string name, int rttMs, int jitterMs)
        {
            Name = name;
            RttMs = rttMs;
            JitterMs = jitterMs;
        }

        /// 수신 경로 일방(one-way) 지연 = RTT/2.
        public int OneWayDelayMs => RttMs / 2;
    }

    public static class NetSimProfiles
    {
        public static readonly NetSimPreset Off = new NetSimPreset("OFF", 0, 0);
        public static readonly NetSimPreset G = new NetSimPreset("PROF-G", 30, 0);
        public static readonly NetSimPreset A = new NetSimPreset("PROF-A", 150, 30);
        public static readonly NetSimPreset B = new NetSimPreset("PROF-B", 250, 60);

        public static NetSimPreset Active { get; private set; } = Off;

        /// 지연 또는 지터가 있으면 활성(OFF면 false → transport 즉시 전달).
        public static bool Enabled => Active.RttMs > 0 || Active.JitterMs > 0;

        public static void Set(NetSimPreset p)
        {
            Active = p;
            // 측정 결과 파일의 PROF-X 표기 근거 + events 타임라인 기록.
            NetDiagnostics.Event("NETSIM", $"profile={p.Name} rtt={p.RttMs} jitter={p.JitterMs}");
        }

        /// Off → G → A → B → Off 순환.
        public static NetSimPreset Cycle()
        {
            NetSimPreset next;
            if (Active.Name == Off.Name) next = G;
            else if (Active.Name == G.Name) next = A;
            else if (Active.Name == A.Name) next = B;
            else next = Off;
            Set(next);
            return next;
        }

        /// <summary>
        /// 패킷 release 시각을 계산한다. 지연+지터를 더하되, 직전 release 이상으로 클램프해
        /// FIFO 단조 증가를 보장(재정렬 방지 → sequenced/reliable 순서 보존).
        /// </summary>
        /// <param name="now">현재 realtimeSinceStartup</param>
        /// <param name="lastReleaseAt">직전 패킷 release 시각</param>
        public static float ComputeReleaseTime(float now, float lastReleaseAt)
        {
            float delay = Active.OneWayDelayMs / 1000f;
            if (Active.JitterMs > 0)
            {
                float j = Active.JitterMs / 1000f;
                delay += Random.Range(-j * 0.5f, j * 0.5f);
            }
            if (delay < 0f) delay = 0f;
            return Mathf.Max(now + delay, lastReleaseAt);
        }
    }

    // NetSimController(F8 토글 MonoBehaviour)는 NetSimController.cs 로 분리 —
    // MonoBehaviour 는 파일명=클래스명이어야 MonoScript 바인딩이 생겨 도메인 리로드를 생존한다.
}
