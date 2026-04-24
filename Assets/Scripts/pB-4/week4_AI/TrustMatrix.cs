// =============================================================================
// TrustMatrix.cs  |  pB-4 Project — Week 4 (Day 2 T2.4) → v3 NGO 2.0 N:M 재설계
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   NPC의 플레이어에 대한 신뢰도 관리. 신뢰도(0.0~100.0)가 4단계 임계점을 넘나들며
//   NPC의 행동이 극적으로 변함.
//
//   4단계 임계점:
//     [0] Hostility   (0~19):   독자 행동/도망
//     [1] Doubt       (20~49):  위험한 명령 거부
//     [2] Cooperation (50~89):  합리적 명령 복종
//     [3] BlindTrust  (90~100): 자기 희생도 감수
//
// [Day 2 T2.4 v3 변경]
//   1) ITrustProvider 4 메서드 구현
//   2) TrustTier enum: Hostility=0 ... BlindTrust=3
//   3) [Errata F6] EventBus.RaiseTrustTierChanged
//
// [v3 NGO 2.0 N:M 재설계 — 2026-04-23]
//   - [NGO-1] MonoBehaviour → NetworkBehaviour
//   - [NGO-2] Awake + OnNetworkSpawn 듀얼 (단독 Play + Network 호환)
//   - [NGO-3] ChangeTrust(playerId, ...)는 서버 권한 ServerRpc
//   - [NGO-4] N:M 구조 — Dictionary<ulong, float> trustByPlayer
//   - [NGO-5] 단독 Play에서는 기존 currentTrust float 유지 (Dashboard 드래그 호환)
//             playerId=0을 "기본 로컬 플레이어"로 간주
//   - [NGO-6] ITrustProvider 기존 4 메서드는 기본 playerId 사용. 신규 4 메서드는 ulong 매개변수
// =============================================================================
using System;
using System.Collections.Generic;
using Unity.Netcode;                                   // [NGO-1]
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Interfaces.Intelligence;

namespace TDA.PB4.AI
{
    /// <summary>신뢰도 4단계.</summary>
    public enum TrustTier
    {
        Hostility = 0,
        Doubt = 1,
        Cooperation = 2,
        BlindTrust = 3
    }

    /// <summary>신뢰도 변화 원인 유형.</summary>
    public enum TrustChangeReason
    {
        TacticalAlignment,       // +15
        SharedSurvival,          // +20
        ResourceSharing,         // +10
        TacticalBetrayal,        // -40
        IncompetenceExposure,    // -5
        RescueAction,            // +40
        AbandonAction,           // -70
        CoverAndRetreat,         // -25
        Manual
    }

    public class TrustMatrix : NetworkBehaviour, ITrustProvider              // [NGO-1]
    {
        // ==================================================================
        // [NGO-5] 단독 Play + Inspector 호환성 필드
        // 기본 플레이어 = playerId 0. Dashboard 드래그는 이 값을 직접 수정.
        // ==================================================================
        [Header("━━━ 신뢰도 현재 상태 (기본 플레이어) ━━━━━")]
        [Tooltip("기본 플레이어(clientId=0)의 신뢰도. 단독 Play 및 Dashboard 호환용.")]
        [SerializeField, Range(0f, 100f)] private float currentTrust = 60f;

        [Tooltip("현재 신뢰도 단계 (Read Only).")]
        [SerializeField] private TrustTier currentTier = TrustTier.Cooperation;

        [Header("━━━ 4단계 임계값 ━━━━━━━━━━━━━━━━━━━")]
        [Range(70f, 100f)] public float blindTrustThreshold = 90f;
        [Range(30f, 89f)]  public float cooperationThreshold = 50f;
        [Range(5f, 49f)]   public float doubtThreshold = 20f;

        [Header("━━━ 명령 복종 수식 가중치 ━━━━━━━━━━━━")]
        [Range(0f, 1f)] public float trustWeight = 0.6f;
        [Range(0f, 1f)] public float severityWeight = 0.4f;
        [Range(0f, 1f)] public float commandAcceptanceThreshold = 0f;

        [Header("━━━ 트라우마 연동 ━━━━━━━━━━━━━━━━━━━")]
        [Range(0f, 1f)] public float traumaCancellationRate = 0.7f;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = false;
        [SerializeField] private float lastAcceptanceScore;
        [SerializeField] private bool lastCommandAccepted;

        // ==================================================================
        // [NGO-4] N:M 구조 — Dictionary<ulong, float> 플레이어별 trust
        //
        // 단독 Play: trustByPlayer가 비어있고 currentTrust 필드만 사용.
        // Network Play: 서버가 trustByPlayer 관리 + ClientRpc 전파.
        // ==================================================================
        private readonly Dictionary<ulong, float> trustByPlayer = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, TrustTier> tierByPlayer = new Dictionary<ulong, TrustTier>();

        // ==================================================================
        // 이벤트
        // ==================================================================
        /// <summary>로컬 Tier 전이 이벤트. 인자: (oldTierIndex, newTierIndex). 기본 플레이어 기준.</summary>
        public event System.Action<int, int> OnTierChanged;

        /// <summary>[v3] N:M용 Tier 전이 이벤트. 인자: (playerId, oldTierIndex, newTierIndex).</summary>
        public event System.Action<ulong, int, int> OnTierChangedPerPlayer;

        // ==================================================================
        // [v3 NGO-2] 이중 초기화
        // ==================================================================

        private void Awake()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                // 단독 Play: 기본 플레이어 엔트리 생성
                trustByPlayer[0] = currentTrust;
                tierByPlayer[0] = EvaluateTier(currentTrust);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                // 서버: 현재 접속한 모든 플레이어에 대한 기본 trust 초기화
                EnsurePlayerEntry(0);
                if (NetworkManager.Singleton != null)
                {
                    foreach (var cid in NetworkManager.Singleton.ConnectedClientsIds)
                        EnsurePlayerEntry(cid);
                }
            }
        }

        /// <summary>[NGO] 특정 플레이어의 trust 엔트리 보장.</summary>
        private void EnsurePlayerEntry(ulong playerId)
        {
            if (!trustByPlayer.ContainsKey(playerId))
            {
                trustByPlayer[playerId] = currentTrust;        // 초기값 = Inspector
                tierByPlayer[playerId] = EvaluateTier(currentTrust);
            }
        }

        /// <summary>[v3 NGO 헬퍼] 결정 권한 체크.</summary>
        private bool HasAuthority()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                return true;
            return IsServer;
        }

        private bool IsNetworkActive =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        // ==================================================================
        // 신뢰도 변화 API (기존 ChangeTrust는 기본 플레이어 사용)
        // ==================================================================

        /// <summary>기본 플레이어(clientId=0)의 신뢰도 변화. 단독 Play + 호환용.</summary>
        public void ChangeTrust(float delta, TrustChangeReason reason)
        {
            ChangeTrust(0, delta, reason);
        }

        /// <summary>[NGO] 특정 플레이어의 신뢰도 변화. 서버 권한.</summary>
        public void ChangeTrust(ulong playerId, float delta, TrustChangeReason reason)
        {
            // [NGO-3] 서버 권한 가드
            if (IsNetworkActive && !IsServer)
            {
                // 클라이언트에서 호출 시 ServerRpc로 위임
                ChangeTrustServerRpc(playerId, delta, reason);
                return;
            }

            ApplyTrustChange(playerId, delta, reason);
        }

        /// <summary>[Rpc(SendTo.Server)] 클라이언트가 서버에 trust 변경 요청.</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void ChangeTrustServerRpc(ulong playerId, float delta, TrustChangeReason reason)
        {
            ApplyTrustChange(playerId, delta, reason);
        }

        /// <summary>서버에서 실제 trust 변경 적용 + 전파.</summary>
        private void ApplyTrustChange(ulong playerId, float delta, TrustChangeReason reason)
        {
            EnsurePlayerEntry(playerId);

            float oldTrust = trustByPlayer[playerId];
            TrustTier oldTier = tierByPlayer[playerId];

            float newTrust = Mathf.Clamp(oldTrust + delta, 0f, 100f);
            TrustTier newTier = EvaluateTier(newTrust);

            trustByPlayer[playerId] = newTrust;
            tierByPlayer[playerId] = newTier;

            // 기본 플레이어인 경우 Inspector 필드도 동기화 (Dashboard 호환)
            if (playerId == 0)
            {
                currentTrust = newTrust;
                currentTier = newTier;
            }

            if (debugLog)
                Debug.Log($"[Trust] {name} [p{playerId}]: {reason}: {oldTrust:F1} → {newTrust:F1} ({delta:+0.#;-0.#;0}) Tier: {oldTier} → {newTier}");

            // Tier 전이
            if (oldTier != newTier)
            {
                Debug.Log($"[Trust] {name} [p{playerId}]: ⚠ 단계 변경! {oldTier} → {newTier} (reason={reason})");

                int oldIdx = (int)oldTier;
                int newIdx = (int)newTier;

                // 로컬 이벤트 (기본 플레이어만 OnTierChanged 발행 — Dashboard 호환)
                if (playerId == 0) OnTierChanged?.Invoke(oldIdx, newIdx);

                // N:M 이벤트 (모든 플레이어)
                OnTierChangedPerPlayer?.Invoke(playerId, oldIdx, newIdx);

                // 전역 EventBus
                EventBus.RaiseTrustTierChanged(name, oldIdx, newIdx);

                // FactionWorldState
                EventBus.RaiseFactionStateChanged("player_trust",
                    new FactionWorldState
                    {
                        populationLevel = newTrust / 100f,
                        isDominant = newTier == TrustTier.BlindTrust
                    });

                // [NGO] Tier 전이 시 모든 클라에 전파
                if (IsNetworkActive && IsServer)
                    NotifyTierChangedClientRpc(playerId, oldIdx, newIdx, newTrust);
            }
            else if (IsNetworkActive && IsServer)
            {
                // Tier 변화 없어도 trust 값은 전파 (Dashboard 슬라이더 동기화)
                NotifyTrustValueClientRpc(playerId, newTrust);
            }
        }

        /// <summary>[Rpc(SendTo.NotServer)] 서버 → 전 클라에 Tier 변경 전파.</summary>
        [Rpc(SendTo.NotServer)]
        private void NotifyTierChangedClientRpc(ulong playerId, int oldIdx, int newIdx, float newTrust)
        {
            if (IsServer) return;   // 서버는 이미 적용됨

            // 클라: 로컬 캐시 갱신 (이벤트는 서버 측에서만 발행)
            trustByPlayer[playerId] = newTrust;
            tierByPlayer[playerId] = (TrustTier)newIdx;

            if (playerId == 0)
            {
                currentTrust = newTrust;
                currentTier = (TrustTier)newIdx;
            }

            if (debugLog)
                Debug.Log($"[Trust/Client] {name} [p{playerId}]: Tier 변경 수신 → {(TrustTier)newIdx}");
        }

        /// <summary>[Rpc(SendTo.NotServer)] 서버 → 전 클라에 trust 값만 전파.</summary>
        [Rpc(SendTo.NotServer)]
        private void NotifyTrustValueClientRpc(ulong playerId, float newTrust)
        {
            if (IsServer) return;

            trustByPlayer[playerId] = newTrust;
            tierByPlayer[playerId] = EvaluateTier(newTrust);

            if (playerId == 0)
            {
                currentTrust = newTrust;
                currentTier = tierByPlayer[playerId];
            }
        }

        /// <summary>사전 정의된 트리거별 신뢰도 변화 (기본 플레이어).</summary>
        public void ApplyTrustTrigger(TrustChangeReason reason)
        {
            ApplyTrustTrigger(0, reason);
        }

        /// <summary>[NGO] 특정 플레이어의 사전 정의 트리거.</summary>
        public void ApplyTrustTrigger(ulong playerId, TrustChangeReason reason)
        {
            float delta = reason switch
            {
                TrustChangeReason.TacticalAlignment => 15f,
                TrustChangeReason.SharedSurvival => 20f,
                TrustChangeReason.ResourceSharing => 10f,
                TrustChangeReason.TacticalBetrayal => -40f,
                TrustChangeReason.IncompetenceExposure => -5f,
                TrustChangeReason.RescueAction => 40f,
                TrustChangeReason.AbandonAction => -70f,
                TrustChangeReason.CoverAndRetreat => -25f,
                _ => 0f
            };
            ChangeTrust(playerId, delta, reason);
        }

        // ==================================================================
        // ITrustProvider 구현 — 기존 API (기본 플레이어)
        // ==================================================================

        public float GetNormalizedTrust()
        {
            float t = trustByPlayer.ContainsKey(0) ? trustByPlayer[0] : currentTrust;
            return t / 100f;
        }

        public int GetCurrentTierIndex()
        {
            return tierByPlayer.ContainsKey(0) ? (int)tierByPlayer[0] : (int)currentTier;
        }

        public void AddDelta(float amount, string reason)
        {
            if (!System.Enum.TryParse<TrustChangeReason>(reason, out var reasonEnum))
                reasonEnum = TrustChangeReason.Manual;
            ChangeTrust(0, amount, reasonEnum);
        }

        public bool EvaluateCommand(float commandSeverity, float npcFear)
            => EvaluateCommand(0, commandSeverity, npcFear);

        // ==================================================================
        // [NGO-6] ITrustProvider 신규 N:M API
        // ==================================================================

        public float GetNormalizedTrust(ulong playerId)
        {
            EnsurePlayerEntry(playerId);
            return trustByPlayer[playerId] / 100f;
        }

        public int GetCurrentTierIndex(ulong playerId)
        {
            EnsurePlayerEntry(playerId);
            return (int)tierByPlayer[playerId];
        }

        public void AddDelta(ulong playerId, float amount, string reason)
        {
            if (!System.Enum.TryParse<TrustChangeReason>(reason, out var reasonEnum))
                reasonEnum = TrustChangeReason.Manual;
            ChangeTrust(playerId, amount, reasonEnum);
        }

        public bool EvaluateCommand(ulong playerId, float commandSeverity, float npcFear)
        {
            EnsurePlayerEntry(playerId);
            float normalizedTrust = trustByPlayer[playerId] / 100f;
            float severityFactor = 1f - Mathf.Clamp01(commandSeverity);

            float score = (normalizedTrust * trustWeight)
                        + (severityFactor * severityWeight)
                        - Mathf.Clamp01(npcFear);

            bool accepted = score > commandAcceptanceThreshold;

            if (playerId == 0)
            {
                lastAcceptanceScore = score;
                lastCommandAccepted = accepted;
            }

            if (debugLog)
                Debug.Log($"[Trust] {name} [p{playerId}]: 명령 판정: Accept={score:F3} " +
                          $"(trust={normalizedTrust:F2}×{trustWeight} + sev={severityFactor:F2}×{severityWeight} - fear={npcFear:F2}) " +
                          $"→ {(accepted ? "✅수락" : "❌거부")}");

            return accepted;
        }

        // ==================================================================
        // 트라우마 연동
        // ==================================================================

        public float GetTraumaCancellation()
        {
            float t = trustByPlayer.ContainsKey(0) ? trustByPlayer[0] : currentTrust;
            return (t / 100f) * traumaCancellationRate;
        }

        // ==================================================================
        // 유틸리티
        // ==================================================================

        private TrustTier EvaluateTier(float trust)
        {
            if (trust >= blindTrustThreshold) return TrustTier.BlindTrust;
            if (trust >= cooperationThreshold) return TrustTier.Cooperation;
            if (trust >= doubtThreshold) return TrustTier.Doubt;
            return TrustTier.Hostility;
        }

        /// <summary>기본 플레이어의 현재 신뢰도.</summary>
        public float CurrentTrust
        {
            get => trustByPlayer.ContainsKey(0) ? trustByPlayer[0] : currentTrust;
        }

        /// <summary>기본 플레이어의 현재 단계.</summary>
        public TrustTier CurrentTier
        {
            get => tierByPlayer.ContainsKey(0) ? tierByPlayer[0] : currentTier;
        }

        /// <summary>[NGO] 모든 플레이어의 trust 스냅샷 (Dashboard 디버그용).</summary>
        public IReadOnlyDictionary<ulong, float> AllTrustValues => trustByPlayer;

        // ==================================================================
        // Inspector Context Menu (기본 플레이어 기준)
        // ==================================================================

        [ContextMenu("Set to BlindTrust (95)")]
        private void DebugSetBlindTrust() => ChangeTrust(0, 95f - CurrentTrust, TrustChangeReason.Manual);

        [ContextMenu("Set to Cooperation (60)")]
        private void DebugSetCooperation() => ChangeTrust(0, 60f - CurrentTrust, TrustChangeReason.Manual);

        [ContextMenu("Set to Doubt (30)")]
        private void DebugSetDoubt() => ChangeTrust(0, 30f - CurrentTrust, TrustChangeReason.Manual);

        [ContextMenu("Set to Hostility (10)")]
        private void DebugSetHostility() => ChangeTrust(0, 10f - CurrentTrust, TrustChangeReason.Manual);

        /// <summary>OnValidate에서 currentTier 자동 계산.</summary>
        /// <remarks>
        /// [주의] Edit 모드 전용. Play 중에 Inspector 슬라이더로 currentTrust 조작 시
        /// trustByPlayer Dict는 갱신 안 됨. Play 중 값 변경은 ContextMenu의
        /// DebugSet* 메서드 또는 ChangeTrust() API 사용 권장.
        /// </remarks>
        private void OnValidate()
        {
            currentTier = EvaluateTier(currentTrust);
        }
    }
}
