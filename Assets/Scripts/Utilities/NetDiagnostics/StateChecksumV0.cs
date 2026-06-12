using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace NetDiag
{
    /// <summary>
    /// [Step 0 · 계측] StateChecksum v0 — M11(체크섬 불일치 검출력) 골격.
    /// 실행계획 v1.1 §0.A.2-3 · EnvFlagRegistry 설계서 §5 Step 7의 선행 구현.
    ///
    /// 동작:
    ///  · 클라이언트: 30초 주기로 {월드시드, 자기 플레이어 인벤토리} FNV-1a 64 해시를 계산해
    ///    CustomMessagingManager 네임드 메시지(≈20B)로 서버에 보고.
    ///    → NetworkObject·씬 배치가 전혀 필요 없는 무침습 RPC 경로이며,
    ///      P0-1 버퍼 1KB 제약과도 무관하게 지금 즉시 동작 가능.
    ///  · 서버: 같은 클라이언트의 서버 측 복제본으로 동일 해시를 재계산해 비교.
    ///    불일치 시 Debug.LogError + events.csv CHECKSUM MISMATCH 행 + 카운터.
    ///
    /// v0 해시 범위: SyncedWorldSeed + 플레이어 인벤토리(NetworkList<InventoryItem> 전 필드).
    /// Step 5에서 EnvFlag 셋 해시·문 상태 등으로 확장한다 (flagSetHash 소비자 ④로 직결).
    /// </summary>
    public class StateChecksumV0 : MonoBehaviour
    {
        const string MsgName = "NETDIAG_CHECKSUM_V0";
        const float IntervalSeconds = 30f;

        float nextReportAt;
        NetworkManager registeredNm;

        void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                registeredNm = null;
                return;
            }

            // 세션(재호스팅 포함)마다 서버 핸들러 재등록
            if (nm != registeredNm)
            {
                registeredNm = nm;
                if (nm.IsServer && nm.CustomMessagingManager != null)
                {
                    nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgName, OnChecksumReport);
                    NetDiagnostics.Event("CHECKSUM", "server handler registered");
                }
            }

            if (Time.unscaledTime < nextReportAt) return;
            nextReportAt = Time.unscaledTime + IntervalSeconds;

            // 호스트 자기 보고는 무의미(항상 일치) — 순수 클라이언트만 보고
            if (nm.IsServer) return;
            ReportToServer(nm);
        }

        // -------------------------------------------------------------------
        // 클라이언트: 보고
        // -------------------------------------------------------------------
        void ReportToServer(NetworkManager nm)
        {
            try
            {
                ComputeLocalState(nm, nm.LocalClientId, out ulong hash, out int seed, out int invCount);

                using var writer = new FastBufferWriter(20, Allocator.Temp);
                writer.WriteValueSafe(hash);
                writer.WriteValueSafe(seed);
                writer.WriteValueSafe(invCount);

                nm.CustomMessagingManager.SendNamedMessage(
                    MsgName, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);

                NetDiagnostics.IncrementCounter("checksum.report.sent");
                NetDiagnostics.Event("CHECKSUM", $"sent hash={hash:X16} seed={seed} invCount={invCount}");
            }
            catch (Exception e)
            {
                NetDiagnostics.Event("CHECKSUM", $"send failed: {e.Message}");
            }
        }

        // -------------------------------------------------------------------
        // 서버: 수신·재계산·비교
        // -------------------------------------------------------------------
        void OnChecksumReport(ulong senderClientId, FastBufferReader reader)
        {
            try
            {
                reader.ReadValueSafe(out ulong clientHash);
                reader.ReadValueSafe(out int clientSeed);
                reader.ReadValueSafe(out int clientInvCount);

                var nm = NetworkManager.Singleton;
                ComputeLocalState(nm, senderClientId, out ulong serverHash, out int serverSeed, out int serverInvCount);

                bool match = clientHash == serverHash;
                NetDiagnostics.IncrementCounter(match ? "checksum.compare.match" : "checksum.compare.MISMATCH");

                const string header = "realtime,serverTime,clientId,match,clientHash,serverHash,clientSeed,serverSeed,clientInv,serverInv";
                NetDiagnostics.Row("checksum.csv", header,
                    $"{Time.realtimeSinceStartup:F3},{NetDiagnostics.ServerTime():F3},{senderClientId},{match}," +
                    $"{clientHash:X16},{serverHash:X16},{clientSeed},{serverSeed},{clientInvCount},{serverInvCount}");

                if (!match)
                {
                    // 시드/인벤 보조값으로 "어느 범위가 어긋났는지" 1차 분리 진단
                    string detail = clientSeed != serverSeed ? "SEED divergence"
                                  : clientInvCount != serverInvCount ? "INVENTORY count divergence"
                                  : "INVENTORY content divergence";
                    Debug.LogError($"<color=#E74C3C>[NetDiag/DESYNC]</color> 클라이언트 {senderClientId} 상태 체크섬 불일치! ({detail}) " +
                                   $"client={clientHash:X16} server={serverHash:X16}");
                    NetDiagnostics.Event("CHECKSUM", $"MISMATCH client={senderClientId} detail={detail}");
                }
            }
            catch (Exception e)
            {
                NetDiagnostics.Event("CHECKSUM", $"server compare failed: {e.Message}");
            }
        }

        // -------------------------------------------------------------------
        // 해시 계산 — 양측이 반드시 같은 코드 경로를 사용
        // -------------------------------------------------------------------
        static void ComputeLocalState(NetworkManager nm, ulong forClientId, out ulong hash, out int seed, out int invCount)
        {
            const ulong FnvOffset = 1469598103934665603UL;
            const ulong FnvPrime = 1099511628211UL;
            hash = FnvOffset;
            seed = 0;
            invCount = 0;

            // 1) 월드 시드 (TerrainSyncNetworkManager — 없는 씬이면 0으로 합산)
            var terrain = CaveSystem.Multiplayer.TerrainSyncNetworkManager.Instance;
            if (terrain != null) seed = terrain.SyncedWorldSeed.Value;
            hash = Mix(hash, (ulong)(uint)seed, FnvPrime);

            // 2) 해당 클라이언트의 플레이어 인벤토리 (Server-Write NetworkList — 복제본 일치가 기대값)
            NetworkObject playerObj = null;
            if (forClientId == nm.LocalClientId && nm.LocalClient != null)
                playerObj = nm.LocalClient.PlayerObject;
            else if (nm.IsServer && nm.ConnectedClients.TryGetValue(forClientId, out var cc))
                playerObj = cc.PlayerObject;

            var inv = playerObj != null ? playerObj.GetComponentInChildren<CharacterInventoryManager>() : null;
            if (inv != null)
            {
                var items = inv.GetInventoryItems();
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        invCount++;
                        hash = Mix(hash, (ulong)(uint)item.itemID, FnvPrime);
                        hash = Mix(hash, (ulong)(uint)item.gridPositionX, FnvPrime);
                        hash = Mix(hash, (ulong)(uint)item.gridPositionY, FnvPrime);
                        hash = Mix(hash, (ulong)(uint)Mathf.RoundToInt(item.rotationAngle), FnvPrime);
                        hash = Mix(hash, (ulong)(uint)item.stackCount, FnvPrime);
                    }
                }
            }
        }

        static ulong Mix(ulong h, ulong v, ulong prime) => (h ^ v) * prime;
    }
}
