// =============================================================================
// NPCAlignmentController.cs  |  pB-4 Project — Week 2 Day 3 T3.2 신규
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   NPC의 현재 Alignment(Hostile/Neutral/Friendly/Companion) 동적 전이.
//   매 1초 평가 + Trust/Karma 변화 이벤트 구독 하이브리드.
//   NPCAlignmentSO를 참조하여 조건 판정.
//
// [Day 3 T3.2 설계]
//   - NetworkBehaviour (서버 권한 TryTransition)
//   - 매 1초 폴링 (계획문서 §B.3 명세)
//   - EventBus 구독 (OnTrustTierChanged, OnKarmaTierChanged) — 즉각 반응
//   - SpeechAssembler 통합 (선택적, null safe)
//   - Hostile → Companion 직접 전이 차단 (PivotResolution만 허용)
//
// [v3 NGO]
//   - NetworkVariable<int> netAlignment (int로 enum 캐스팅)
//   - TryTransition은 서버 권한 + ClientRpc 알림
// =============================================================================
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Data;
using TDA.PB4.Director;
using TDA.PB4.Interfaces.Intelligence;
using TDA.PB4.Interfaces.Narrative;

namespace TDA.PB4.AI
{
    public class NPCAlignmentController : NetworkBehaviour, IAlignmentProvider
    {
        [Header("━━━ Alignment 설정 ━━━━━━━━━━━━━━━")]

        [Tooltip("이 NPC의 4 진영 정의 SO. Bootstrapper가 주입 또는 Inspector 수동.")]
        [SerializeField] private NPCAlignmentSO alignmentDefinitionSO;

        [Tooltip("현재 Alignment (Read Only).")]
        [SerializeField] private NPCAlignment currentAlignment = NPCAlignment.Neutral;

        [Tooltip("초기 Alignment (Pivot Reset용).")]
        [SerializeField] private NPCAlignment originalAlignment = NPCAlignment.Neutral;

        [Header("━━━ 평가 주기 ━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("매 n초마다 Alignment 재평가.")]
        [Range(0.5f, 5f)] public float evaluationInterval = 1.0f;

        [Tooltip("현재 Alignment 진입 시각 (히스테리시스용).")]
        [SerializeField] private float alignmentEnterTime;

        [Header("━━━ Trust/Karma 연동 ━━━━━━━━━━━━━━")]

        [Tooltip("플레이어 ID (기본 0). 참조 시 이 ID의 Trust/Karma 확인.")]
        public ulong targetPlayerId = 0;

        [Tooltip("TrustMatrix 참조 (자동 탐색).")]
        [SerializeField] private TrustMatrix trustMatrix;

        [Header("━━━ SpeechAssembler 통합 (선택) ━━━━")]

        [Tooltip("전이 시 defaultDialogueKey로 발화 (Day 4 연결).")]
        public bool enableSpeechOnTransition = true;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]

        public bool debugLog = true;

        // ==================================================================
        // 네트워크 동기화
        // ==================================================================

        private NetworkVariable<int> netAlignment = new NetworkVariable<int>(
            value: (int)NPCAlignment.Neutral,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        private float evalTimer = 0f;

        // ==================================================================
        // 이벤트 (IAlignmentProvider)
        // ==================================================================

        public event System.Action<int, int> OnAlignmentChanged;

        public int CurrentAlignmentIndex => (int)currentAlignment;
        public NPCAlignment CurrentAlignment => currentAlignment;

        // ==================================================================
        // Lifecycle
        // ==================================================================

        private void Awake()
        {
            if (trustMatrix == null) trustMatrix = GetComponent<TrustMatrix>();
            originalAlignment = currentAlignment;
            alignmentEnterTime = Time.time;
        }

        // [검증 수정] 이벤트 구독 중복 방지 플래그
        private bool eventSubscribed = false;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            netAlignment.OnValueChanged += OnNetAlignmentChanged;

            if (IsServer)
                netAlignment.Value = (int)currentAlignment;

            // Trust/Karma 이벤트 구독 (서버만)
            if (HasAuthority())
                SubscribeEvents();
        }

        public override void OnNetworkDespawn()
        {
            netAlignment.OnValueChanged -= OnNetAlignmentChanged;

            if (HasAuthority())
                UnsubscribeEvents();

            base.OnNetworkDespawn();
        }

        private void OnEnable()
        {
            // 단독 Play 시 Awake 이후 여기서 구독 (NetworkManager 비활성 확정 시에만)
            // OnNetworkSpawn이 호출된다면 거기서 구독하므로 여기서는 스킵
            if (!IsNetworkActive && !eventSubscribed)
                SubscribeEvents();
        }

        private void OnDisable()
        {
            if (!IsNetworkActive && eventSubscribed)
                UnsubscribeEvents();
        }

        private void SubscribeEvents()
        {
            if (eventSubscribed) return;
            EventBus.OnKarmaTierChanged += OnKarmaTierChanged;
            if (trustMatrix != null)
                trustMatrix.OnTierChangedPerPlayer += OnTrustTierChangedPerPlayer;
            eventSubscribed = true;
        }

        private void UnsubscribeEvents()
        {
            if (!eventSubscribed) return;
            EventBus.OnKarmaTierChanged -= OnKarmaTierChanged;
            if (trustMatrix != null)
                trustMatrix.OnTierChangedPerPlayer -= OnTrustTierChangedPerPlayer;
            eventSubscribed = false;
        }

        private void OnNetAlignmentChanged(int prev, int next)
        {
            if (IsServer) return;
            currentAlignment = (NPCAlignment)next;
            OnAlignmentChanged?.Invoke(prev, next);
        }

        private void Update()
        {
            if (!HasAuthority()) return;

            evalTimer += Time.deltaTime;
            if (evalTimer >= evaluationInterval)
            {
                evalTimer = 0f;
                EvaluateAndTransition();
            }
        }

        private bool IsNetworkActive =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private bool HasAuthority() => !IsNetworkActive || IsServer;

        // ==================================================================
        // 이벤트 핸들러 (서버 전용)
        // ==================================================================

        private void OnKarmaTierChanged(ulong playerId, KarmaTier oldTier, KarmaTier newTier)
        {
            if (playerId != targetPlayerId) return;
            if (debugLog)
                Debug.Log($"[Alignment] {name}: Karma Tier 변화 수신 (p{playerId}: {oldTier} → {newTier}) — 즉시 재평가");

            EvaluateAndTransition();
        }

        private void OnTrustTierChangedPerPlayer(ulong playerId, int oldTier, int newTier)
        {
            if (playerId != targetPlayerId) return;
            if (debugLog)
                Debug.Log($"[Alignment] {name}: Trust Tier 변화 수신 (p{playerId}: {oldTier} → {newTier}) — 즉시 재평가");

            EvaluateAndTransition();
        }

        // ==================================================================
        // 핵심: Alignment 평가 + 전이
        // ==================================================================

        // [v3 수정] SO null 경고 스팸 방지
        private bool warnedSONull = false;

        /// <summary>Trust + Karma 기반으로 적합한 Alignment 선택 후 전이.</summary>
        public void EvaluateAndTransition()
        {
            if (!HasAuthority()) return;
            if (alignmentDefinitionSO == null)
            {
                if (debugLog && !warnedSONull)
                {
                    Debug.LogWarning($"[Alignment] {name}: alignmentDefinitionSO null. 평가 스킵. " +
                                     $"(Inspector에서 NPCAlignmentSO .asset을 주입하세요. 경고는 1회만 표시.)");
                    warnedSONull = true;
                }
                return;
            }

            // [옵션 B] TrustMatrix 미부착 시 fallback을 0으로 (보수적). 
            // 기존 50f는 자동으로 Friendly 진영 임계치 통과시키는 부작용 있었음.
            float trust;
            if (trustMatrix != null)
            {
                trust = trustMatrix.GetNormalizedTrust(targetPlayerId) * 100f;
            }
            else
            {
                trust = 0f;
                if (debugLog)
                    Debug.LogWarning($"[Alignment] {name}: TrustMatrix 미부착 — trust=0으로 보수적 평가. " +
                                     $"플레이어 없는 환경에서 자동 진영 전이가 차단됩니다.");
            }
            float karma = KarmaDirector.Instance != null ? KarmaDirector.Instance.GetKarmaScore(targetPlayerId) : 0f;

            var def = alignmentDefinitionSO.FindMatchingDefinition(trust, karma);
            if (def == null) return;

            // 태그 조건 체크 (간단: 생략 또는 로컬 tagResolver에서 확인 필요)
            // 여기서는 trust/karma만으로 판정

            if (def.alignment == currentAlignment) return;   // 이미 해당 진영

            // 히스테리시스: 최소 유지 시간
            var currentDef = alignmentDefinitionSO.GetDefinition(currentAlignment);
            if (currentDef != null && Time.time - alignmentEnterTime < currentDef.minHoldSeconds)
            {
                if (debugLog)
                    Debug.Log($"[Alignment] {name}: 최소 유지시간 미달 ({Time.time - alignmentEnterTime:F1}s < {currentDef.minHoldSeconds}s). 전이 스킵.");
                return;
            }

            // 자동 전이 (Hostile ← Companion 직접 전이 시 PivotResolution 필요)
            var reason = AlignmentChangeReason.KarmaTrigger;
            TryTransition(def.alignment, reason, def);
        }

        /// <summary>Alignment 전이 시도. 서버 권한.</summary>
        public bool TryTransition(NPCAlignment target, AlignmentChangeReason reason, AlignmentDefinition targetDef = null)
        {
            if (!HasAuthority())
            {
                TryTransitionServerRpc(target, reason);
                return false;
            }

            if (target == currentAlignment) return false;

            // Hostile → Companion 직접 전이 차단 (Pivot만 가능)
            if (currentAlignment == NPCAlignment.Hostile &&
                target == NPCAlignment.Companion &&
                reason != AlignmentChangeReason.PivotResolution)
            {
                if (debugLog)
                    Debug.Log($"[Alignment] {name}: Hostile → Companion 직접 전이 차단 (Pivot 필요)");
                return false;
            }

            var oldAlignment = currentAlignment;
            currentAlignment = target;
            alignmentEnterTime = Time.time;

            if (IsNetworkActive && IsServer)
                netAlignment.Value = (int)target;

            Debug.Log($"[Alignment] {name}: {oldAlignment} → {target} (reason: {reason})");

            OnAlignmentChanged?.Invoke((int)oldAlignment, (int)target);
            EventBus.RaiseAlignmentChanged(name, (int)oldAlignment, (int)target, reason);

            // SpeechAssembler 통합 (Day 4 T4.1 완성)
            if (enableSpeechOnTransition && targetDef != null && !string.IsNullOrEmpty(targetDef.defaultDialogueKey))
            {
                if (debugLog)
                    Debug.Log($"[Alignment] {name}: SpeechTrigger '{targetDef.defaultDialogueKey}' (Day 4 EventBus)");

                // [Day 4 수정] EventBus로 SpeechTrigger 발행 → SpeechDispatcher가 수신
                //              Context는 Core.Events namespace에 있음 (계층 역류 방지)
                EventBus.RaiseSpeechTrigger(
                    targetDef.defaultDialogueKey,
                    new TDA.PB4.Core.Events.SpeechTriggerContext
                    {
                        characterId = name,
                        targetPlayerId = 0UL,  // TODO: 실제 대상 플레이어 전달 (Week 7에서 확장)
                        currentAlignment = target,
                        previousAlignment = oldAlignment,
                        reason = reason
                    });
            }

            return true;
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void TryTransitionServerRpc(NPCAlignment target, AlignmentChangeReason reason)
        {
            TryTransition(target, reason);
        }

        // ==================================================================
        // IAlignmentProvider 구현
        // ==================================================================

        public bool IsHostileTo(GameObject other)
        {
            // 간단: Hostile alignment면 모든 플레이어에게 적대
            // 고도화: other의 team/faction 비교
            if (currentAlignment == NPCAlignment.Hostile) return true;
            return false;
        }

        // ==================================================================
        // 외부 API
        // ==================================================================

        /// <summary>Bootstrapper가 SO 주입.</summary>
        public void SetAlignmentSO(NPCAlignmentSO so)
        {
            if (so == null)
            {
                Debug.LogError($"[Alignment] {name}: SetAlignmentSO null.");
                return;
            }

            if (!so.IsComplete(out string reason))
            {
                Debug.LogError($"[Alignment] {name}: AlignmentSO 불완전 — {reason}");
                return;
            }

            alignmentDefinitionSO = so;
            warnedSONull = false;   // SO 주입되었으니 경고 플래그 리셋
            if (debugLog)
                Debug.Log($"[Alignment] {name}: AlignmentSO 주입됨 (4 진영)");
        }

        // ==================================================================
        // Context Menu
        // ==================================================================

        [ContextMenu("Debug/Force Companion")]
        private void DebugForceCompanion() => TryTransition(NPCAlignment.Companion, AlignmentChangeReason.Manual);

        [ContextMenu("Debug/Force Friendly")]
        private void DebugForceFriendly() => TryTransition(NPCAlignment.Friendly, AlignmentChangeReason.Manual);

        [ContextMenu("Debug/Force Neutral")]
        private void DebugForceNeutral() => TryTransition(NPCAlignment.Neutral, AlignmentChangeReason.Manual);

        [ContextMenu("Debug/Force Hostile")]
        private void DebugForceHostile() => TryTransition(NPCAlignment.Hostile, AlignmentChangeReason.Manual);

        [ContextMenu("Debug/Reset to Original")]
        private void DebugReset() => TryTransition(originalAlignment, AlignmentChangeReason.Manual);

        // ================================================================
        // [Day 4 추가] 평가 진단 + 안정화 시간 우회 강제 전이
        // ================================================================

        [ContextMenu("Debug/평가 상태 진단")]
        public void DebugDiagnoseEvaluation()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<color=#FF8C00>[Alignment.DEBUG]</color> {name}: 평가 상태 진단");

            if (trustMatrix != null)
            {
                float trust = trustMatrix.GetNormalizedTrust(targetPlayerId) * 100f;
                sb.AppendLine($"  TrustMatrix: 부착됨, trust(p{targetPlayerId})={trust:F1}");
            }
            else
            {
                sb.AppendLine($"  TrustMatrix: 미부착 -> fallback trust=0 (보수)");
            }

            if (KarmaDirector.Instance != null)
            {
                float karma = KarmaDirector.Instance.GetKarmaScore(targetPlayerId);
                sb.AppendLine($"  KarmaDirector: 활성, karma(p{targetPlayerId})={karma:F1}");
            }
            else
            {
                sb.AppendLine($"  KarmaDirector: 비활성 -> fallback karma=0");
            }

            sb.AppendLine($"  현재 진영: {currentAlignment}");
            sb.AppendLine($"  유지 시간: {Time.time - alignmentEnterTime:F1}s");

            float simTrust = trustMatrix != null ? trustMatrix.GetNormalizedTrust(targetPlayerId) * 100f : 0f;
            float simKarma = KarmaDirector.Instance != null ? KarmaDirector.Instance.GetKarmaScore(targetPlayerId) : 0f;
            var matched = alignmentDefinitionSO != null
                        ? alignmentDefinitionSO.FindMatchingDefinition(simTrust, simKarma)
                        : null;
            if (matched != null)
                sb.AppendLine($"  매칭 결과: {matched.alignment} (trust={simTrust:F1}, karma={simKarma:F1})");
            else
                sb.AppendLine($"  매칭 결과: 없음");

            Debug.Log(sb.ToString(), this);
        }

        [ContextMenu("Debug/Hold 무시 + Friendly 강제")]
        public void DebugForceFriendlyBypassHold() => DebugForceTransition(NPCAlignment.Friendly);

        [ContextMenu("Debug/Hold 무시 + Companion 강제")]
        public void DebugForceCompanionBypassHold() => DebugForceTransition(NPCAlignment.Companion);

        [ContextMenu("Debug/Hold 무시 + Hostile 강제")]
        public void DebugForceHostileBypassHold() => DebugForceTransition(NPCAlignment.Hostile);

        [ContextMenu("Debug/Hold 무시 + Neutral 강제")]
        public void DebugForceNeutralBypassHold() => DebugForceTransition(NPCAlignment.Neutral);

        /// <summary>
        /// 5초 최소 유지 시간을 무시하고 즉시 전이.
        /// 기존 DebugForce*는 안정화 시간에 막혀 동작 안 할 수 있음.
        /// SpeechTestWindow에서 호출.
        /// </summary>
        public void DebugForceTransition(NPCAlignment target)
        {
            Debug.Log($"<color=#FF8C00>[Alignment.DEBUG]</color> {name}: 강제 전이 {currentAlignment} -> {target} (Hold 무시)", this);

            // 안정화 시간 우회 — alignmentEnterTime을 과거로 설정
            alignmentEnterTime = Time.time - 1000f;

            var def = alignmentDefinitionSO != null
                    ? alignmentDefinitionSO.GetDefinition(target)
                    : null;
            TryTransition(target, AlignmentChangeReason.Manual, def);
        }
    }
}