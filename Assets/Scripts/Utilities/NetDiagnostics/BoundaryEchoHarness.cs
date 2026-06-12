using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace NetDiag
{
    /// <summary>
    /// [Step 0 · 계측] 경계값 송신 테스트 하니스 — M2(대형 메시지 수신 성공률) 측정기.
    /// 실행계획 v1.1 §3 M2 · §1.B(P0-1 검증, R10 단편화 경계 포함).
    ///
    /// 사용법: 클라이언트로 접속한 상태에서 F9.
    ///  · 512 / 1023 / 1024 / 1025 / 1300(MTU 부근) / 4096 / 16384 / 65536 바이트 페이로드를
    ///    순서대로 서버에 송신(REQ). 서버는 수신 페이로드의 FNV-1a 해시를 재계산해 회신(ACK).
    ///  · 클라이언트는 송신 해시 == 서버 재계산 해시 → PASS, 3초 무응답 → TIMEOUT,
    ///    해시 불일치 → CORRUPT 로 기록한다.
    ///
    /// 베이스라인 기대치(P0-1, 버퍼 1024B): 1024B 초과 크기에서 손상/Assert/접속 단절.
    ///  → 그 실패 기록 자체가 Step 1 After와 대비할 증거다. 실패로 세션이 끊겨도
    ///    직전까지의 결과가 echo.csv 에 남는다 (크기 오름차순 진행 이유).
    /// </summary>
    public class BoundaryEchoHarness : MonoBehaviour
    {
        const string ReqMsg = "NETDIAG_ECHO_REQ";
        const string AckMsg = "NETDIAG_ECHO_ACK";
        const float TimeoutSeconds = 3f;
        static readonly int[] Sizes = { 512, 1023, 1024, 1025, 1300, 4096, 16384, 65536 };
        const string CsvHeader = "realtime,testId,size,result,sentHash,ackHash";

        NetworkManager registeredNm;
        readonly Dictionary<int, PendingTest> pending = new Dictionary<int, PendingTest>();
        int nextTestId = 1;
        bool running;

        class PendingTest
        {
            public int size;
            public ulong sentHash;
            public bool acked;
            public bool ok;
            public ulong ackHash;
        }

        void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                registeredNm = null;
                return;
            }

            if (nm != registeredNm)
            {
                registeredNm = nm;
                if (nm.CustomMessagingManager != null)
                {
                    nm.CustomMessagingManager.RegisterNamedMessageHandler(ReqMsg, OnEchoRequest);  // 서버 측
                    nm.CustomMessagingManager.RegisterNamedMessageHandler(AckMsg, OnEchoAck);      // 클라 측
                }
            }

            if (Input.GetKeyDown(KeyCode.F9) && !running)
            {
                if (nm.IsServer && !nm.IsHost)
                    return;
                if (nm.IsHost)
                {
                    Debug.LogWarning("[NetDiag/Echo] 호스트 루프백은 전송 계층을 타지 않아 의미가 없습니다. 클라이언트 머신에서 F9를 누르세요.");
                    return;
                }
                StartCoroutine(RunSweep(nm));
            }
        }

        IEnumerator RunSweep(NetworkManager nm)
        {
            running = true;
            NetDiagnostics.Event("ECHO", $"sweep start sizes=[{string.Join(",", Sizes)}]");
            Debug.Log("<color=#3498DB>[NetDiag/Echo]</color> M2 경계값 스윕 시작 — 결과는 echo.csv");

            foreach (int size in Sizes)
            {
                int testId = nextTestId++;
                var test = new PendingTest { size = size };
                pending[testId] = test;

                if (!SendRequest(nm, testId, size, test))
                {
                    Record(testId, test, "SEND_FAIL");
                    continue;
                }

                float deadline = Time.unscaledTime + TimeoutSeconds;
                while (!test.acked && Time.unscaledTime < deadline)
                    yield return null;

                string result = !test.acked ? "TIMEOUT" : (test.ok ? "PASS" : "CORRUPT");
                Record(testId, test, result);

                // 세션이 죽었으면 잔여 크기는 의미 없음 — 즉시 종료 (실패 임계 기록됨)
                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                {
                    NetDiagnostics.Event("ECHO", $"sweep aborted — session lost after size={size}");
                    break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }

            NetDiagnostics.Event("ECHO", "sweep end");
            Debug.Log("<color=#3498DB>[NetDiag/Echo]</color> 스윕 종료");
            running = false;
        }

        bool SendRequest(NetworkManager nm, int testId, int size, PendingTest test)
        {
            try
            {
                byte[] payload = new byte[size];
                for (int i = 0; i < size; i++) payload[i] = (byte)(i * 31 + testId);
                test.sentHash = Fnv(payload);

                using var writer = new FastBufferWriter(size + 32, Allocator.Temp, size + 32);
                writer.WriteValueSafe(testId);
                writer.WriteValueSafe(size);
                writer.WriteValueSafe(test.sentHash);
                writer.WriteBytesSafe(payload);

                nm.CustomMessagingManager.SendNamedMessage(
                    ReqMsg, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableFragmentedSequenced);

                NetDiagnostics.Event("ECHO", $"REQ sent testId={testId} size={size} hash={test.sentHash:X16}");
                return true;
            }
            catch (System.Exception e)
            {
                NetDiagnostics.Event("ECHO", $"REQ send failed testId={testId} size={size}: {e.Message}");
                return false;
            }
        }

        // 서버 측: 수신 페이로드 재해시 → 회신
        void OnEchoRequest(ulong senderClientId, FastBufferReader reader)
        {
            try
            {
                reader.ReadValueSafe(out int testId);
                reader.ReadValueSafe(out int size);
                reader.ReadValueSafe(out ulong sentHash);

                byte[] payload = new byte[size];
                reader.ReadBytesSafe(ref payload, size);
                ulong recvHash = Fnv(payload);

                NetDiagnostics.Event("ECHO", $"REQ recv testId={testId} size={size} hashMatch={recvHash == sentHash}");

                var nm = NetworkManager.Singleton;
                using var writer = new FastBufferWriter(32, Allocator.Temp);
                writer.WriteValueSafe(testId);
                writer.WriteValueSafe(recvHash);
                nm.CustomMessagingManager.SendNamedMessage(AckMsg, senderClientId, writer, NetworkDelivery.Reliable);
            }
            catch (System.Exception e)
            {
                // 손상된 페이로드의 역직렬화 실패도 그 자체가 M2 증거
                NetDiagnostics.Event("ECHO", $"REQ handler exception (M2 evidence): {e.GetType().Name} {e.Message}");
            }
        }

        // 클라 측: 회신 대조
        void OnEchoAck(ulong senderClientId, FastBufferReader reader)
        {
            try
            {
                reader.ReadValueSafe(out int testId);
                reader.ReadValueSafe(out ulong ackHash);
                if (pending.TryGetValue(testId, out var test))
                {
                    test.acked = true;
                    test.ackHash = ackHash;
                    test.ok = ackHash == test.sentHash;
                }
            }
            catch (System.Exception e)
            {
                NetDiagnostics.Event("ECHO", $"ACK handler exception: {e.Message}");
            }
        }

        void Record(int testId, PendingTest test, string result)
        {
            pending.Remove(testId);
            NetDiagnostics.IncrementCounter($"echo.{result}");
            NetDiagnostics.Row("echo.csv", CsvHeader,
                $"{Time.realtimeSinceStartup:F3},{testId},{test.size},{result},{test.sentHash:X16},{test.ackHash:X16}");
            Debug.Log($"[NetDiag/Echo] size={test.size} → {result}");
        }

        static ulong Fnv(byte[] data)
        {
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < data.Length; i++) h = (h ^ data[i]) * 1099511628211UL;
            return h;
        }
    }
}
