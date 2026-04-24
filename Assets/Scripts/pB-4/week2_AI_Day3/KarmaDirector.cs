// =============================================================================
// KarmaDirector.cs  |  pB-4 Project — Week 5 선반영 / Week 2 Day 3 T3.1 신규
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.Director
//
// 역할:
//   플레이어의 전역 도덕(Karma) 누적 + 조회 + Tier 전이 발행.
//   Saint / Neutral / Outlaw / Demon 4 Tier.
//
//   구조: Dictionary<ulong playerId, float karma> — 플레이어별 Karma
//        (개론서 §1.20 ServerCharacterRegistry 정합)
//
// [Day 3 T3.1 신규]
//   - NetworkBehaviour (서버 권한)
//   - Dictionary<ulong, NetworkVariable<float>>는 NGO 제약이므로
//     서버 내부 Dict + ClientRpc 전파 패턴 사용
//   - Natural Decay: 5초마다 ±0.05 (Saint/Demon → 0으로 느리게 회복)
//   - EventBus 발행: OnKarmaChanged, OnKarmaTierChanged
//
// 사용법:
//   KarmaDirector.Instance.ChangeKarma(playerId, -10f, KilledCivilian)
// =============================================================================
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Interfaces.Narrative;

namespace TDA.PB4.Director
{
    /// <summary>플레이어 전역 Karma 관리자. 씬에 1개. 서버 권한.</summary>
    public class KarmaDirector : NetworkBehaviour, IKarmaDirector
    {
        // ==================================================================
        // Singleton 패턴 (네트워크 환경에서도 단일 인스턴스)
        // ==================================================================
        public static KarmaDirector Instance { get; private set; }

        [Header("━━━ Karma 설정 ━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("새 플레이어 기본 Karma.")]
        [Range(-100f, 100f)]
        public float initialKarma = 0f;

        [Tooltip("Karma 최솟값.")]
        public float minKarma = -100f;

        [Tooltip("Karma 최댓값.")]
        public float maxKarma = 100f;

        [Header("━━━ Tier 임계값 ━━━━━━━━━━━━━━━━━━")]

        [Tooltip("Saint 진입 임계. 이 값 이상이면 Saint.")]
        [Range(0f, 100f)] public float saintThreshold = 50f;

        [Tooltip("Outlaw 진입 임계. 이 값 이하이면 Outlaw.")]
        [Range(-100f, 0f)] public float outlawThreshold = -50f;

        [Tooltip("Demon 진입 임계. 이 값 이하이면 Demon.")]
        [Range(-100f, 0f)] public float demonThreshold = -90f;

        [Header("━━━ Natural Decay ━━━━━━━━━━━━━━━━")]

        [Tooltip("자연 회복 활성화 여부.")]
        public bool enableNaturalDecay = true;

        [Tooltip("회복 간격 (초).")]
        [Range(1f, 30f)] public float decayInterval = 5f;

        [Tooltip("회복 속도. Saint(+)는 감소, Demon(-)는 증가. 양쪽 다 0으로 수렴.")]
        [Range(0f, 2f)] public float decayRate = 0.05f;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]

        public bool debugLog = true;

        [Tooltip("디버그: 현재 모든 플레이어의 Karma 표시 (Read Only).")]
        [SerializeField] private List<string> debugKarmaDump = new List<string>();

        // ==================================================================
        // 서버 측 저장소 (서버만 쓰기, ClientRpc로 전파)
        // ==================================================================
        private readonly Dictionary<ulong, float> karmaByPlayer = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, KarmaTier> tierByPlayer = new Dictionary<ulong, KarmaTier>();

        // 클라이언트 측 캐시 (읽기 전용, ClientRpc로 동기화)
        private readonly Dictionary<ulong, float> clientKarmaCache = new Dictionary<ulong, float>();

        private float decayTimer = 0f;

        // ==================================================================
        // Lifecycle
        // ==================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[KarmaDirector] 중복 인스턴스 발견. 이 GameObject 제거.");
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            // [검증 수정] Singleton dangling reference 방지
            if (Instance == this) Instance = null;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer && NetworkManager.Singleton != null)
            {
                // 현재 접속 클라들에 대해 초기 Karma 세팅
                foreach (var cid in NetworkManager.Singleton.ConnectedClientsIds)
                    EnsurePlayerEntry(cid);

                // 호스트도 clientId 0으로 기본 엔트리
                EnsurePlayerEntry(0);
            }

            if (debugLog)
                Debug.Log($"[Karma] KarmaDirector OnNetworkSpawn (IsServer={IsServer})");
        }

        private void Update()
        {
            if (!HasAuthority()) return;
            if (!enableNaturalDecay) return;

            decayTimer += Time.deltaTime;
            if (decayTimer >= decayInterval)
            {
                decayTimer = 0f;
                ApplyNaturalDecay();
            }
        }

        private bool HasAuthority()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                return true;
            return IsServer;
        }

        private bool IsNetworkActive =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        // ==================================================================
        // 플레이어 엔트리 관리
        // ==================================================================

        private void EnsurePlayerEntry(ulong playerId)
        {
            if (!karmaByPlayer.ContainsKey(playerId))
            {
                karmaByPlayer[playerId] = initialKarma;
                tierByPlayer[playerId] = ComputeTier(initialKarma);
            }
        }

        // ==================================================================
        // Natural Decay
        // ==================================================================

        private void ApplyNaturalDecay()
        {
            foreach (var playerId in new List<ulong>(karmaByPlayer.Keys))
            {
                float k = karmaByPlayer[playerId];
                if (Mathf.Abs(k) < decayRate) continue;

                float delta = k > 0 ? -decayRate : decayRate;
                ChangeKarma(playerId, delta, KarmaChangeReason.NaturalDecay);
            }
        }

        // ==================================================================
        // Public API — Karma 변화
        // ==================================================================

        /// <summary>Karma 변화. 서버 권한.</summary>
        public void ChangeKarma(ulong playerId, float delta, KarmaChangeReason reason)
        {
            if (IsNetworkActive && !IsServer)
            {
                ChangeKarmaServerRpc(playerId, delta, reason);
                return;
            }

            ApplyKarmaChange(playerId, delta, reason);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void ChangeKarmaServerRpc(ulong playerId, float delta, KarmaChangeReason reason)
        {
            ApplyKarmaChange(playerId, delta, reason);
        }

        private void ApplyKarmaChange(ulong playerId, float delta, KarmaChangeReason reason)
        {
            EnsurePlayerEntry(playerId);

            float oldKarma = karmaByPlayer[playerId];
            KarmaTier oldTier = tierByPlayer[playerId];

            float newKarma = Mathf.Clamp(oldKarma + delta, minKarma, maxKarma);
            KarmaTier newTier = ComputeTier(newKarma);

            karmaByPlayer[playerId] = newKarma;
            tierByPlayer[playerId] = newTier;

            if (debugLog)
                Debug.Log($"[Karma] p{playerId}: {oldKarma:F1} → {newKarma:F1} (delta {delta:+0.#;-0.#;0}, reason: {reason})");

            EventBus.RaiseKarmaChanged(playerId, oldKarma, newKarma, reason);

            if (oldTier != newTier)
            {
                Debug.Log($"[Karma] p{playerId}: Tier 전이 {oldTier} → {newTier} (karma {newKarma:F1})");
                EventBus.RaiseKarmaTierChanged(playerId, oldTier, newTier);
            }

            // 전 클라 전파
            if (IsNetworkActive && IsServer)
                NotifyKarmaChangedClientRpc(playerId, oldKarma, newKarma, (int)newTier, (int)reason);

            UpdateDebugDump();
        }

        [Rpc(SendTo.NotServer)]
        private void NotifyKarmaChangedClientRpc(ulong playerId, float oldKarma, float newKarma, int newTierIdx, int reasonIdx)
        {
            if (IsServer) return;   // 서버는 이미 적용됨

            clientKarmaCache[playerId] = newKarma;

            // 클라에서도 EventBus 발행 (Dashboard 등 클라 측 구독자용)
            var reason = (KarmaChangeReason)reasonIdx;
            EventBus.RaiseKarmaChanged(playerId, oldKarma, newKarma, reason);

            if (debugLog)
                Debug.Log($"[Karma/Client] p{playerId}: {oldKarma:F1} → {newKarma:F1} (Tier: {(KarmaTier)newTierIdx})");
        }

        // ==================================================================
        // Public API — Karma 조회
        // ==================================================================

        /// <summary>[IKarmaDirector] playerId로 Karma 조회.</summary>
        public float GetKarmaScore(ulong playerId)
        {
            if (IsNetworkActive && !IsServer)
            {
                return clientKarmaCache.ContainsKey(playerId) ? clientKarmaCache[playerId] : 0f;
            }

            EnsurePlayerEntry(playerId);
            return karmaByPlayer[playerId];
        }

        /// <summary>[IKarmaDirector] Karma 증감.</summary>
        public void ApplyKarmaShift(ulong playerId, float delta, string reason)
        {
            if (!System.Enum.TryParse<KarmaChangeReason>(reason, out var enumReason))
                enumReason = KarmaChangeReason.Manual;
            ChangeKarma(playerId, delta, enumReason);
        }

        /// <summary>[IKarmaDirector] 현재 Tier.</summary>
        public KarmaTier GetTier(ulong playerId)
        {
            if (IsNetworkActive && !IsServer)
            {
                float k = clientKarmaCache.ContainsKey(playerId) ? clientKarmaCache[playerId] : 0f;
                return ComputeTier(k);
            }

            EnsurePlayerEntry(playerId);
            return tierByPlayer[playerId];
        }

        // ==================================================================
        // 기존 IKarmaDirector string characterId API (호환)
        // ==================================================================

        public float GetKarmaScore(string characterId)
        {
            if (ulong.TryParse(characterId, out ulong playerId))
                return GetKarmaScore(playerId);
            return 0f;
        }

        public void ApplyKarmaShift(string characterId, float delta)
        {
            if (ulong.TryParse(characterId, out ulong playerId))
                ChangeKarma(playerId, delta, KarmaChangeReason.Manual);
        }

        // ==================================================================
        // 유틸리티
        // ==================================================================

        private KarmaTier ComputeTier(float karma)
        {
            if (karma <= demonThreshold) return KarmaTier.Demon;
            if (karma <= outlawThreshold) return KarmaTier.Outlaw;
            if (karma >= saintThreshold) return KarmaTier.Saint;
            return KarmaTier.Neutral;
        }

        private void UpdateDebugDump()
        {
            debugKarmaDump.Clear();
            foreach (var kv in karmaByPlayer)
                debugKarmaDump.Add($"p{kv.Key}: {kv.Value:F1} ({tierByPlayer[kv.Key]})");
        }

        public IReadOnlyDictionary<ulong, float> AllKarmaValues => karmaByPlayer;

        // ==================================================================
        // Context Menu (기본 플레이어 = 0)
        // ==================================================================

        [ContextMenu("Debug/Set Saint (+80)")]
        private void DebugSetSaint() => ChangeKarma(0, 80f - GetKarmaScore(0), KarmaChangeReason.Manual);

        [ContextMenu("Debug/Set Neutral (0)")]
        private void DebugSetNeutral() => ChangeKarma(0, -GetKarmaScore(0), KarmaChangeReason.Manual);

        [ContextMenu("Debug/Set Outlaw (-70)")]
        private void DebugSetOutlaw() => ChangeKarma(0, -70f - GetKarmaScore(0), KarmaChangeReason.Manual);

        [ContextMenu("Debug/Set Demon (-95)")]
        private void DebugSetDemon() => ChangeKarma(0, -95f - GetKarmaScore(0), KarmaChangeReason.Manual);

        [ContextMenu("Debug/Dump All Karma")]
        private void DebugDumpAll()
        {
            UpdateDebugDump();
            foreach (var line in debugKarmaDump)
                Debug.Log($"[Karma] {line}");
        }
    }
}
