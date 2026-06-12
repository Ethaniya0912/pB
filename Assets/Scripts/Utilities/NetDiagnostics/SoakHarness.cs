using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace NetDiag
{
    /// <summary>
    /// [Step 0 · 계측] soak 하네스 v0 — SCN-07(30분 soak)의 수집 자동화.
    /// 실행계획 v1.1 §0.A.3-2. 완전 자동화(무인 실행·자동 판정)는 Step 5에서 고도화한다.
    ///
    /// 사용법: F10 = 시작/종료 토글.
    ///  · 가동 중 10초 주기로 카운터 스냅샷을 soak_samples.csv 에 기록.
    ///  · 종료(또는 앱 종료) 시 soak_summary.md 생성 — 끊김·체크섬 불일치·예외 카운트 요약.
    ///  · Debug.LogError/Exception 발생 횟수도 집계 (logMessageReceived 구독).
    /// </summary>
    public class SoakHarness : MonoBehaviour
    {
        const float SampleInterval = 10f;
        const string SamplesHeader = "elapsed,role,counterName,value";

        bool soaking;
        float startedAt;
        float nextSampleAt;
        Dictionary<string, long> startSnapshot;
        int errorCount, exceptionCount;

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!soaking) return;
            if (type == LogType.Error) errorCount++;
            else if (type == LogType.Exception) exceptionCount++;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10))
            {
                if (soaking) StopSoak("manual stop");
                else StartSoak();
            }

            if (!soaking) return;

            if (Time.unscaledTime >= nextSampleAt)
            {
                nextSampleAt = Time.unscaledTime + SampleInterval;
                Sample();
            }
        }

        void OnApplicationQuit()
        {
            if (soaking) StopSoak("application quit");
        }

        void StartSoak()
        {
            soaking = true;
            startedAt = Time.unscaledTime;
            nextSampleAt = startedAt;
            startSnapshot = NetDiagnostics.SnapshotCounters();
            errorCount = exceptionCount = 0;
            NetDiagnostics.Event("SOAK", "start");
            Debug.Log("<color=#3498DB>[NetDiag/Soak]</color> soak 시작 (F10으로 종료) — 10초 주기 샘플링");
        }

        void Sample()
        {
            float elapsed = Time.unscaledTime - startedAt;
            string role = NetDiagnostics.Role();
            foreach (var kv in NetDiagnostics.SnapshotCounters())
            {
                NetDiagnostics.Row("soak_samples.csv", SamplesHeader,
                    $"{elapsed:F0},{role},{kv.Key},{kv.Value}");
            }
        }

        void StopSoak(string reason)
        {
            soaking = false;
            float duration = Time.unscaledTime - startedAt;
            NetDiagnostics.Event("SOAK", $"stop ({reason}) duration={duration:F0}s");

            var end = NetDiagnostics.SnapshotCounters();
            var sb = new StringBuilder();
            sb.AppendLine("# Soak 요약 (하네스 v0 자동 생성)");
            sb.AppendLine();
            sb.AppendLine($"- 종료 사유: {reason}");
            sb.AppendLine($"- 가동 시간: {duration / 60f:F1}분");
            sb.AppendLine($"- 역할: {NetDiagnostics.Role()}");
            sb.AppendLine($"- soak 중 LogError: {errorCount}건 / Exception: {exceptionCount}건");
            sb.AppendLine();
            sb.AppendLine("## 핵심 판정 카운터 (soak 구간 증분)");
            sb.AppendLine();
            sb.AppendLine("| 카운터 | 증분 | 판정 메모 |");
            sb.AppendLine("|---|---|---|");
            AppendDelta(sb, end, "ngo.clientDisconnected", "0이어야 함 (의도된 종료 제외)");
            AppendDelta(sb, end, "transport.evt.Disconnect", "전송 계층 끊김");
            AppendDelta(sb, end, "transport.evt.Connect", "soak 중 증가 = 유령/오발화 의심 (P0-2)");
            AppendDelta(sb, end, "checksum.compare.MISMATCH", "0이어야 함 (디싱크)");
            AppendDelta(sb, end, "checksum.compare.match", "주기 보고 정상 여부 확인용");
            AppendDelta(sb, end, "verdict.hp_apply.attackerSide", "Step 2 이후 0이어야 함 (R6)");
            sb.AppendLine();
            sb.AppendLine("## 전체 카운터 (soak 구간 증분)");
            sb.AppendLine();
            sb.AppendLine("| 카운터 | 시작 | 종료 | 증분 |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var kv in end)
            {
                long start = startSnapshot != null && startSnapshot.TryGetValue(kv.Key, out long s) ? s : 0;
                sb.AppendLine($"| {kv.Key} | {start} | {kv.Value} | {kv.Value - start} |");
            }

            try
            {
                string path = Path.Combine(NetDiagnostics.SessionDir, "soak_summary.md");
                File.WriteAllText(path, sb.ToString());
                Debug.Log($"<color=#3498DB>[NetDiag/Soak]</color> 요약 저장: {path}");
            }
            catch { }

            NetDiagnostics.DumpCounters("soak");
        }

        void AppendDelta(StringBuilder sb, Dictionary<string, long> end, string key, string note)
        {
            long e = end.TryGetValue(key, out long v) ? v : 0;
            long s = startSnapshot != null && startSnapshot.TryGetValue(key, out long sv) ? sv : 0;
            sb.AppendLine($"| {key} | {e - s} | {note} |");
        }
    }
}
