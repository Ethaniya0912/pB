// =============================================================================
// CommandAcceptanceFilter.cs  |  pB-4 Project — Week 2 Day 3 T3.5 신규
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   플레이어 명령을 NPC가 수락/거부할지 판정.
//   acceptance = Trust × (1 - Severity/4) × (1/FearMult) × Loyalty
//
//   Loyalty: NPCAlignment 기반 (Companion=1.0, Friendly=0.8, Neutral=0.5, Hostile=0.1)
//
// [Day 3 T3.5 설계]
//   - NetworkBehaviour (서버 권한 검증, 치팅 방지)
//   - CommandRequest struct 수락
//   - commandAcceptanceThreshold 슬라이더 (기본 0.3)
//   - 수락/거부 결과를 ClientRpc로 issuer에게 알림
// =============================================================================
using Unity.Netcode;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Data;
using TDA.PB4.Interfaces.Intelligence;

namespace TDA.PB4.AI
{
    public class CommandAcceptanceFilter : NetworkBehaviour, ICommandFilter
    {
        [Header("━━━ 참조 ━━━━━━━━━━━━━━━━━━━━━━━━")]

        [SerializeField] private TrustMatrix trustMatrix;
        [SerializeField] private NPCAlignmentController alignmentController;
        [SerializeField] private TraumaSystem traumaSystem;

        [Header("━━━ 수식 가중치 ━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("수락 판정 임계. 기본 0.3.")]
        [Range(0f, 1f)] public float acceptanceThreshold = 0.3f;

        [Tooltip("NPC의 fear 값 (BaseAIBrain의 fear 자동 조회).")]
        [SerializeField] private TDA.PB4.AI.Humanoid.HumanoidAIBrain brainRef;

        [Header("━━━ Alignment 기반 Loyalty ━━━━━━━━")]

        [Range(0f, 1f)] public float loyaltyHostile = 0.1f;
        [Range(0f, 1f)] public float loyaltyNeutral = 0.5f;
        [Range(0f, 1f)] public float loyaltyFriendly = 0.8f;
        [Range(0f, 1f)] public float loyaltyCompanion = 1.0f;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]

        public bool debugLog = true;

        [SerializeField] private float lastAcceptance;
        [SerializeField] private bool lastAccepted;
        [SerializeField] private string lastReason;

        // ==================================================================
        // Lifecycle
        // ==================================================================

        private void Awake()
        {
            if (trustMatrix == null) trustMatrix = GetComponent<TrustMatrix>();
            if (alignmentController == null) alignmentController = GetComponent<NPCAlignmentController>();
            if (traumaSystem == null) traumaSystem = GetComponent<TraumaSystem>();
        }

        private bool IsNetworkActive =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private bool HasAuthority() => !IsNetworkActive || IsServer;

        // ==================================================================
        // ICommandFilter 구현
        // ==================================================================

        public float AcceptanceThreshold
        {
            get => acceptanceThreshold;
            set => acceptanceThreshold = Mathf.Clamp01(value);
        }

        /// <summary>명령 수락 판정. 서버 권한 검증.</summary>
        public bool TryAccept(CommandRequest request, out float acceptance, out string reason)
        {
            // 서버 검증 (치팅 방지): 클라에서 호출 시 ServerRpc로 전환 권장
            if (!HasAuthority())
            {
                // 클라는 로컬 추정값만 반환 (실제 판정은 서버가 함)
                acceptance = ComputeAcceptance(request);
                reason = "ClientEstimate";
                return acceptance >= acceptanceThreshold;
            }

            acceptance = ComputeAcceptance(request);

            // 구체적 사유 판정
            if (acceptance < acceptanceThreshold)
            {
                // 거부 사유 분석
                if (trustMatrix != null && trustMatrix.GetNormalizedTrust(request.issuerPlayerId) < 0.3f)
                    reason = "LowTrust";
                else if (request.severity >= CommandSeverity.Severe)
                    reason = "TooDangerous";
                else if (alignmentController != null &&
                         alignmentController.CurrentAlignment == NPCAlignment.Hostile)
                    reason = "Hostile";
                else
                    reason = "InsufficientAcceptance";

                lastAcceptance = acceptance;
                lastAccepted = false;
                lastReason = reason;

                if (debugLog)
                    Debug.Log($"[CmdFilter] {name}: '{request.commandId}' (Sev:{request.severity}) → 거부 [acc={acceptance:F2} < {acceptanceThreshold:F2}, 사유: {reason}]");

                return false;
            }

            reason = "Accepted";
            lastAcceptance = acceptance;
            lastAccepted = true;
            lastReason = reason;

            if (debugLog)
                Debug.Log($"[CmdFilter] {name}: '{request.commandId}' (Sev:{request.severity}) → 수락 [acc={acceptance:F2}]");

            return true;
        }

        /// <summary>Acceptance 수식: Trust × (1-Severity/4) × (1/FearMult) × Loyalty</summary>
        private float ComputeAcceptance(CommandRequest request)
        {
            // Trust (0~1)
            float trust = trustMatrix != null
                ? trustMatrix.GetNormalizedTrust(request.issuerPlayerId)
                : 0.5f;

            // Severity 감쇠 (Trivial=0 → 1.0, Suicidal=4 → 0.0)
            float severityPenalty = 1f - ((int)request.severity / 4f);

            // Fear Multiplier (Trauma에 따라 1.0~1.5)
            float fearMult = traumaSystem != null
                ? ((ITraumaProvider)traumaSystem).GetFearMultiplier()
                : 1.0f;
            float fearPenalty = 1f / Mathf.Max(1f, fearMult);   // fearMult 1.5 → 0.67

            // Loyalty (Alignment 기반)
            float loyalty = GetLoyaltyForAlignment();

            float acceptance = trust * severityPenalty * fearPenalty * loyalty;

            if (debugLog)
                Debug.Log($"[CmdFilter] {name}: acc = trust({trust:F2}) × sevPenalty({severityPenalty:F2}) × " +
                          $"fearPenalty({fearPenalty:F2}) × loyalty({loyalty:F2}) = {acceptance:F3}");

            return Mathf.Clamp01(acceptance);
        }

        private float GetLoyaltyForAlignment()
        {
            if (alignmentController == null) return 0.5f;

            return alignmentController.CurrentAlignment switch
            {
                NPCAlignment.Hostile   => loyaltyHostile,
                NPCAlignment.Neutral   => loyaltyNeutral,
                NPCAlignment.Friendly  => loyaltyFriendly,
                NPCAlignment.Companion => loyaltyCompanion,
                _ => 0.5f
            };
        }

        // ==================================================================
        // Context Menu
        // ==================================================================

        [ContextMenu("Debug/Test Command (Moderate)")]
        private void DebugTestCmd()
        {
            var req = new CommandRequest
            {
                issuerPlayerId = 0,
                commandId = FixedCommandId.From("test_attack"),
                severity = CommandSeverity.Moderate,
                targetPosition = transform.position
            };
            TryAccept(req, out float acc, out string reason);
        }
    }
}
