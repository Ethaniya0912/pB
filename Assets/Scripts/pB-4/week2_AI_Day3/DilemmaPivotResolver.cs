// =============================================================================
// DilemmaPivotResolver.cs  |  pB-4 Project — Week 2 Day 3 T3.4 신규
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   NPC의 Pivot 선택 처리. Anchor 영구 재설정 + Alignment 강제 전이 + Karma 누적.
//
// [Day 3 T3.4 설계]
//   - Trauma.Crossroads 단계 진입 시 ResolvePivotCandidate 호출
//   - PivotChoice 선택 후 ApplyChoice로 영구 변화 적용
//   - HumanoidAIBrain.SetAnchor 호출 (Anchor 재설정)
//   - NPCAlignmentController.TryTransition(PivotResolution) (진영 강제)
//   - KarmaDirector.ChangeKarma (플레이어 Karma 누적)
//
// [v3 NGO]
//   - 서버 권한 (ServerRpc 진입점)
//   - 네트워크 동기화된 NPC 상태 갱신
// =============================================================================
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Data;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Director;
using TDA.PB4.Interfaces.Narrative;

namespace TDA.PB4.AI
{
    public class DilemmaPivotResolver : NetworkBehaviour
    {
        [Header("━━━ 참조 ━━━━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("같은 GameObject의 HumanoidAIBrain.")]
        [SerializeField] private HumanoidAIBrain brain;

        [Tooltip("같은 GameObject의 NPCAlignmentController.")]
        [SerializeField] private NPCAlignmentController alignmentController;

        [Tooltip("같은 GameObject의 TraumaSystem.")]
        [SerializeField] private TraumaSystem traumaSystem;

        [Header("━━━ Pivot 라이브러리 ━━━━━━━━━━━━━━━")]

        [Tooltip("이 NPC가 겪을 수 있는 Pivot 정의 목록.")]
        public List<DilemmaPivotSO> availablePivots = new List<DilemmaPivotSO>();

        [Header("━━━ 진행 상태 (Read Only) ━━━━━━━━━")]

        [SerializeField] private string lastResolvedPivotId;
        [SerializeField] private string lastChoiceId;
        [SerializeField] private int totalPivotsResolved = 0;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]

        public bool debugLog = true;

        // ==================================================================
        // Lifecycle
        // ==================================================================

        private void Awake()
        {
            if (brain == null) brain = GetComponent<HumanoidAIBrain>();
            if (alignmentController == null) alignmentController = GetComponent<NPCAlignmentController>();
            if (traumaSystem == null) traumaSystem = GetComponent<TraumaSystem>();
        }

        private void OnEnable()
        {
            EventBus.OnTrustTierChanged += OnTrustTierChanged;
        }

        private void OnDisable()
        {
            EventBus.OnTrustTierChanged -= OnTrustTierChanged;
        }

        private bool IsNetworkActive =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private bool HasAuthority() => !IsNetworkActive || IsServer;

        // ==================================================================
        // Pivot 트리거 (Trauma Crossroads 진입 등에서 호출)
        // ==================================================================

        /// <summary>현재 상황에 맞는 Pivot 후보를 반환. null=없음.</summary>
        public DilemmaPivotSO ResolvePivotCandidate(PivotTriggerStage stage)
        {
            foreach (var pivot in availablePivots)
            {
                if (pivot == null) continue;
                if (pivot.triggerStage == stage)
                    return pivot;   // 첫 매칭 반환
            }
            return null;
        }

        /// <summary>Pivot 선택 적용. 서버 권한.</summary>
        public bool ApplyChoice(DilemmaPivotSO pivot, string choiceId, ulong issuerPlayerId = 0)
        {
            if (!HasAuthority())
            {
                ApplyChoiceServerRpc(pivot.pivotId, choiceId, issuerPlayerId);
                return false;
            }

            if (pivot == null)
            {
                Debug.LogError($"[Pivot] {name}: pivot null");
                return false;
            }

            var choice = pivot.GetChoice(choiceId);
            if (choice == null)
            {
                Debug.LogError($"[Pivot] {name}: choiceId '{choiceId}' not found in {pivot.pivotId}");
                return false;
            }

            // 1. Anchor 재설정 (영구 변화)
            if (brain != null)
            {
                var oldAnchor = brain.GetAnchor();
                var newAnchor = new PersonalityMatrix
                {
                    control   = Mathf.Clamp01(oldAnchor.control   + choice.controlDelta),
                    stability = Mathf.Clamp01(oldAnchor.stability + choice.stabilityDelta),
                    openness  = Mathf.Clamp01(oldAnchor.openness  + choice.opennessDelta),
                    agreeable = Mathf.Clamp01(oldAnchor.agreeable + choice.agreeableDelta),
                    directness= Mathf.Clamp01(oldAnchor.directness+ choice.directnessDelta),
                };
                brain.SetAnchor(newAnchor);

                Debug.Log($"[Pivot] {name}: Anchor 재설정 ({pivot.pivotId}:{choiceId}) " +
                          $"C{choice.controlDelta:+0.0;-0.0;0} S{choice.stabilityDelta:+0.0;-0.0;0} " +
                          $"O{choice.opennessDelta:+0.0;-0.0;0} A{choice.agreeableDelta:+0.0;-0.0;0} " +
                          $"D{choice.directnessDelta:+0.0;-0.0;0}");
            }

            // 2. Alignment 강제 전이
            if (choice.forceAlignmentTransition && alignmentController != null)
            {
                alignmentController.TryTransition(choice.forcedAlignment, AlignmentChangeReason.PivotResolution);
                Debug.Log($"[Pivot] {name}: Alignment 강제 전이 → {choice.forcedAlignment}");
            }

            // 3. Karma 누적 (플레이어)
            if (Mathf.Abs(choice.karmaDelta) > 0.01f && KarmaDirector.Instance != null)
            {
                KarmaDirector.Instance.ChangeKarma(issuerPlayerId, choice.karmaDelta, KarmaChangeReason.DilemmaChoice);
            }

            // 4. Trauma 해소 (Crossroads 단계에서 선택 시 → None 또는 PermanentScarring)
            if (traumaSystem != null && traumaSystem.CurrentStage == TraumaStage.Crossroads)
            {
                // moralAlignment 양수 → 회복(None), 음수 → 악화(PermanentScarring)
                bool recovery = choice.moralAlignment >= 0f;
                traumaSystem.ResolveCrossroadsByPivot(recovery);
                Debug.Log($"[Pivot] {name}: Crossroads 해소 → {(recovery ? "회복" : "악화")}");
            }

            // 5. 통계
            lastResolvedPivotId = pivot.pivotId;
            lastChoiceId = choiceId;
            totalPivotsResolved++;

            Debug.Log($"[Pivot] {name}: ApplyChoice 완료 ({pivot.pivotId}:{choiceId}, moral={choice.moralAlignment:F2})");
            return true;
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void ApplyChoiceServerRpc(string pivotId, string choiceId, ulong issuerPlayerId)
        {
            var pivot = availablePivots.Find(p => p != null && p.pivotId == pivotId);
            if (pivot == null)
            {
                Debug.LogError($"[Pivot] ServerRpc: pivotId '{pivotId}' not in availablePivots");
                return;
            }
            ApplyChoice(pivot, choiceId, issuerPlayerId);
        }

        // ==================================================================
        // 이벤트 핸들러
        // ==================================================================

        private void OnTrustTierChanged(string npcId, int oldTier, int newTier)
        {
            if (!HasAuthority()) return;
            if (npcId != name) return;

            // Trust Hostility 진입 시 Pivot 트리거 (선택)
            if (newTier == 0)   // Hostility
            {
                if (debugLog)
                    Debug.Log($"[Pivot] {name}: Trust Hostility 진입 감지 (Pivot 후보 체크 가능)");
            }
        }

        // ==================================================================
        // Context Menu (디버그)
        // ==================================================================

        [ContextMenu("Debug/List Available Pivots")]
        private void DebugListPivots()
        {
            foreach (var p in availablePivots)
            {
                if (p == null) continue;
                Debug.Log($"[Pivot] {p.pivotId}: {p.choices.Count}개 choice");
            }
        }
    }
}
