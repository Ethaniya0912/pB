using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NetDiag
{
    /// <summary>
    /// [Step 0 · 계측 기반] 네트워크 진단 코어.
    /// 실행계획 v1.1 §0.A.2 — 게임 로직 무침습 계측의 공용 출력부.
    ///
    /// 역할:
    ///  · 세션별 출력 폴더 생성 (persistentDataPath/NetDiagnostics/{timestamp}_pid{N})
    ///  · CSV 라인 기록 (events.csv / verdicts.csv / checksum.csv)
    ///  · 카운터 집계 (M9 패킷 로그 카운터, 전송 송수신량, R6 권위 경로 카운터 등)
    ///  · 종료 시 counters.csv 덤프
    ///
    /// 주의: 이 클래스는 어떤 게임 상태도 변경하지 않는다. 기록 실패는 조용히 무시한다.
    /// </summary>
    public static class NetDiagnostics
    {
        /// <summary>전역 스위치. 릴리즈 출하 시 false 권장 (Step 0~5 측정 기간에는 true 유지).</summary>
        public static bool Enabled = true;

        static string sessionDir;
        static readonly Dictionary<string, long> counters = new Dictionary<string, long>();
        static readonly Dictionary<string, StreamWriter> writers = new Dictionary<string, StreamWriter>();
        static readonly object ioLock = new object();
        static bool initialized;
        static bool quitHooked;

        public static string SessionDir
        {
            get { EnsureInit(); return sessionDir; }
        }

        static void EnsureInit()
        {
            if (initialized) return;
            initialized = true;

            try
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                sessionDir = Path.Combine(Application.persistentDataPath, "NetDiagnostics", $"{stamp}_pid{pid}");
                Directory.CreateDirectory(sessionDir);
                Debug.Log($"<color=#3498DB>[NetDiag]</color> 세션 출력 폴더: {sessionDir}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetDiag] 세션 폴더 생성 실패 — 기록 비활성화: {e.Message}");
                Enabled = false;
            }

            if (!quitHooked)
            {
                quitHooked = true;
                Application.quitting += OnQuit;
            }
        }

        static StreamWriter GetWriter(string fileName, string header)
        {
            if (writers.TryGetValue(fileName, out var w)) return w;

            try
            {
                string path = Path.Combine(sessionDir, fileName);
                bool isNew = !File.Exists(path);
                var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                w = new StreamWriter(stream) { AutoFlush = true };
                if (isNew && !string.IsNullOrEmpty(header)) w.WriteLine(header);
                writers[fileName] = w;
                return w;
            }
            catch
            {
                return null;
            }
        }

        // ---------------------------------------------------------------------
        // 공통 컨텍스트 헬퍼
        // ---------------------------------------------------------------------

        /// <summary>현재 머신의 네트워크 역할 (HOST / SERVER / CLIENT / OFFLINE).</summary>
        public static string Role()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return "OFFLINE";
            if (nm.IsHost) return "HOST";
            if (nm.IsServer) return "SERVER";
            return "CLIENT";
        }

        /// <summary>NGO 서버시각 (미접속 시 -1).</summary>
        public static double ServerTime()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return -1;
            return nm.ServerTime.Time;
        }

        public static ulong LocalClientId()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm != null ? nm.LocalClientId : 0;
        }

        // ---------------------------------------------------------------------
        // 이벤트 타임라인 (events.csv) — NetEventLogger / Transport 수명주기가 사용
        // ---------------------------------------------------------------------

        const string EventsHeader = "realtime,frame,serverTime,role,localClient,category,message";

        /// <summary>타임라인 이벤트 1건 기록. category 예: TRANSPORT / NGO / CHECKSUM / ECHO / SOAK.</summary>
        public static void Event(string category, string message)
        {
            if (!Enabled) return;
            EnsureInit();

            try
            {
                lock (ioLock)
                {
                    var w = GetWriter("events.csv", EventsHeader);
                    w?.WriteLine(
                        $"{Time.realtimeSinceStartup:F3},{Time.frameCount},{ServerTime():F3}," +
                        $"{Role()},{LocalClientId()},{category},\"{Sanitize(message)}\"");
                }
            }
            catch { /* 계측은 게임을 방해하지 않는다 */ }
        }

        /// <summary>임의 CSV 파일에 1행 기록 (헤더는 최초 1회).</summary>
        public static void Row(string fileName, string header, string line)
        {
            if (!Enabled) return;
            EnsureInit();

            try
            {
                lock (ioLock)
                {
                    var w = GetWriter(fileName, header);
                    w?.WriteLine(line);
                }
            }
            catch { }
        }

        static string Sanitize(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace('"', '\'').Replace('\n', ' ').Replace('\r', ' ');

        // ---------------------------------------------------------------------
        // 카운터 (M9 · 송수신량 · R6 권위 경로 등)
        // ---------------------------------------------------------------------

        public static void IncrementCounter(string name) => AddCounter(name, 1);

        public static void AddCounter(string name, long delta)
        {
            if (!Enabled) return;
            lock (counters)
            {
                counters.TryGetValue(name, out long v);
                counters[name] = v + delta;
            }
        }

        public static long GetCounter(string name)
        {
            lock (counters)
            {
                return counters.TryGetValue(name, out long v) ? v : 0;
            }
        }

        /// <summary>현재 카운터 스냅샷 (soak 하네스의 주기 샘플링용).</summary>
        public static Dictionary<string, long> SnapshotCounters()
        {
            lock (counters)
            {
                return new Dictionary<string, long>(counters);
            }
        }

        /// <summary>카운터 전량을 counters.csv 로 덤프 (종료 시 자동 + soak 종료 시 수동).</summary>
        public static void DumpCounters(string suffix = "final")
        {
            if (!Enabled || !initialized) return;

            try
            {
                lock (ioLock)
                {
                    string path = Path.Combine(sessionDir, $"counters_{suffix}.csv");
                    using var w = new StreamWriter(path, false);
                    w.WriteLine("counter,value");
                    lock (counters)
                    {
                        foreach (var kv in counters)
                            w.WriteLine($"{kv.Key},{kv.Value}");
                    }
                }
            }
            catch { }
        }

        static void OnQuit()
        {
            try
            {
                Event("SYS", "Application.quitting");
                DumpCounters();
                lock (ioLock)
                {
                    foreach (var w in writers.Values) w?.Dispose();
                    writers.Clear();
                }
            }
            catch { }
        }
    }
}
