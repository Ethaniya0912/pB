// =============================================================================
// CommandAcceptanceResolver.cs  |  pB-4 Project — Week 2 Day 3 T3.5 / N-23 격상
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   플레이어 명령을 NPC 가 수락 / 거부 판정 + 수락 시 다중 분기 후속 처리.
//
//   1. 수락 판정 (기존 Filter 책임):
//      acceptance = Trust × (1 - Severity/4) × (1/FearMult) × Loyalty
//      Loyalty: NPCAlignment 기반 (Companion=1.0, Friendly=0.8, Neutral=0.5, Hostile=0.1)
//
//   2. 수락 후 분기 후속 처리 (★ N-23 신규 — Resolver 책임):
//      - CommandDirective 생성 + 캐시 (ActiveDirective)
//      - commandId → BT winner 매핑 (StandardPlayerCommandIds 어휘)
//      - 통계 갱신 (acceptedCount / lastCommandId)
//      - OnCommandAccepted 이벤트 발행 (시나리오 / UI 측 추적)
//      - ActiveDirective expiresAt 자동 만료
//
// [Day 3 T3.5 → N-23 격상 사유]
//   "비비기" 동작 (Ranger 접근만 / Strike 없음) 영역 추적 결과:
//   HumanoidAIBrain 측 u_followcommand 가 평시에도 항상 발현 → 자동 winner 진입 →
//   BT FollowCommand 분기 (stalks only) 진입 → Strike 없음.
//   해결: SelectNewState 측 FollowCommand 자동 후보 제외 + Resolver 측 ActiveDirective
//        → PB4DecisionAdapter Override Gate G-4 측 BT winner 강제 진입 (명령 시점만).
//
// [DilemmaPivotResolver 본보기 패턴 정합]
//   ResolvePivotCandidate (선출) + ApplyChoice (5 영역 분기) ↔
//   TryAccept (판정) + Apply (4 영역 분기)
//
// 이력:
//   2026-04-25 Day 3 T3.5 — CommandAcceptanceFilter.cs 신규
//   2026-05-14 N-23 격상 — Filter → Resolver. Apply 측 ActiveDirective + 이벤트 신규.
// =============================================================================
using System;
using Unity.Netcode;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Data;
using TDA.PB4.Interfaces.Intelligence;

// [Hotfix2 / 이름 충돌 해소]
// `TDA.PB4.AI.CommandDirective` (Wk5_AI/CommandDirective.cs / MonoBehaviour 클래스) vs
// `TDA.PB4.Interfaces.Intelligence.CommandDirective` (본 의제 / struct) 가 동시 존재.
// `namespace TDA.PB4.AI` 안에서 `CommandDirective` 단순 참조 시 같은 namespace 우선 규칙으로
// MonoBehaviour 클래스가 선택되어 인터페이스 측 struct 와 타입 불일치 (CS0738) 발생.
// B-3 의 `CommandType` 충돌 (Hotfix1) 과 동일 패턴 — alias 로 본 의제 측만 명시 참조.
using BridgeCommandDirective = TDA.PB4.Interfaces.Intelligence.CommandDirective;

namespace TDA.PB4.AI
{
    public class CommandAcceptanceResolver : NetworkBehaviour, ICommandResolver
    {
        // ═══════════════════════════════════════════════════════════════════
        // [region 1] Fields & References
        // ═══════════════════════════════════════════════════════════════════

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

        [Header("━━━ [N-23 신규] Resolver ━━━━━━━━━━━")]

        [Tooltip("명령 수락 후 directive 활성 지속 시간 (초). 기본 30 초.")]
        [Range(1f, 300f)] public float defaultCommandDuration = 30f;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]

        public bool debugLog = true;

        [SerializeField] private float lastAcceptance;
        [SerializeField] private bool lastAccepted;
        [SerializeField] private string lastReason;

        // [N-23 신규] Resolver 측 통계
        [SerializeField] private int acceptedCount;
        [SerializeField] private int rejectedCount;
        [SerializeField] private string lastCommandId;
        [SerializeField] private string lastBtWinner;

        // [N-23 신규] ActiveDirective 캐시
        [SerializeField] private bool _hasActiveDirective;
        [SerializeField] private BridgeCommandDirective _activeDirective;

        // [N-23 신규] OnCommandAccepted 이벤트
        public event Action<CommandRequest, BridgeCommandDirective> OnCommandAccepted;

        // ==================================================================
        // Lifecycle
        // ==================================================================

        private void Awake()
        {
            if (trustMatrix == null) trustMatrix = GetComponent<TrustMatrix>();
            if (alignmentController == null) alignmentController = GetComponent<NPCAlignmentController>();
            if (traumaSystem == null) traumaSystem = GetComponent<TraumaSystem>();
        }

        private void Update()
        {
            // [N-23 신규] ActiveDirective 만료 자동 정리
            if (_hasActiveDirective && Time.time >= _activeDirective.expiresAt)
            {
                if (debugLog)
                    Debug.Log($"[CmdResolver] {name}: '{_activeDirective.commandId}' directive 만료 자동 정리 " +
                              $"(만료 시각={_activeDirective.expiresAt:F1}, 현재={Time.time:F1})");
                _hasActiveDirective = false;
                _activeDirective = default;
            }
        }

        private bool IsNetworkActive =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private bool HasAuthority() => !IsNetworkActive || IsServer;

        // ═══════════════════════════════════════════════════════════════════
        // [region 2] Acceptance Judgment — ICommandResolver 구현
        // ═══════════════════════════════════════════════════════════════════

        public float AcceptanceThreshold
        {
            get => acceptanceThreshold;
            set => acceptanceThreshold = Mathf.Clamp01(value);
        }

        public bool HasActiveDirective => _hasActiveDirective && Time.time < _activeDirective.expiresAt;

        public BridgeCommandDirective ActiveDirective => HasActiveDirective ? _activeDirective : default;

        public void ClearActiveDirective()
        {
            if (!_hasActiveDirective) return;
            if (debugLog)
                Debug.Log($"[CmdResolver] {name}: '{_activeDirective.commandId}' directive 외부 해제");
            _hasActiveDirective = false;
            _activeDirective = default;
        }

        /// <summary>명령 수락 판정. 서버 권한 검증. 수락 시 Apply 자동 호출.</summary>
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
                rejectedCount++;

                if (debugLog)
                    Debug.Log($"[CmdResolver] {name}: '{request.commandId}' (Sev:{request.severity}) → 거부 [acc={acceptance:F2} < {acceptanceThreshold:F2}, 사유: {reason}]");

                return false;
            }

            reason = "Accepted";
            lastAcceptance = acceptance;
            lastAccepted = true;
            lastReason = reason;

            // ★ N-23 — 수락 시 Apply 자동 호출 → ActiveDirective 갱신
            Apply(request);

            if (debugLog)
                Debug.Log($"[CmdResolver] {name}: '{request.commandId}' (Sev:{request.severity}) → 수락 [acc={acceptance:F2}] → BTWinner='{lastBtWinner}'");

            return true;
        }

        /// <summary>Acceptance 수식: Trust × (1-Severity/4) × (1/FearMult) × Loyalty</summary>
        /// <remarks>
        /// Trust 가 per-issuer 자료 (GetNormalizedTrust(playerId)) — multiplayer 환경에서
        /// player 별 acceptance 차별화 영역. u_followcommand 는 global 이라 자리 대체 X.
        /// </remarks>
        private float ComputeAcceptance(CommandRequest request)
        {
            // Trust (0~1) — per-issuer
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
                Debug.Log($"[CmdResolver] {name}: acc = trust({trust:F2}) × sevPenalty({severityPenalty:F2}) × " +
                          $"fearPenalty({fearPenalty:F2}) × loyalty({loyalty:F2}) = {acceptance:F3}");

            return Mathf.Clamp01(acceptance);
        }

        private float GetLoyaltyForAlignment()
        {
            if (alignmentController == null) return 0.5f;

            return alignmentController.CurrentAlignment switch
            {
                NPCAlignment.Hostile => loyaltyHostile,
                NPCAlignment.Neutral => loyaltyNeutral,
                NPCAlignment.Friendly => loyaltyFriendly,
                NPCAlignment.Companion => loyaltyCompanion,
                _ => 0.5f
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // [region 3] ★ Resolver — Apply (수락 후 다중 분기 후속) — N-23 신규
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>수락 후 다중 분기 후속 처리. DilemmaPivotResolver.ApplyChoice 본보기 정합.</summary>
        private void Apply(CommandRequest request)
        {
            // ① BT winner 매핑
            string btWinner = MapCommandIdToBTWinner(request.commandId);
            lastBtWinner = btWinner ?? "(미정의)";

            // ② CommandDirective 생성 + 캐시 (BridgeCommandDirective alias = TDA.PB4.Interfaces.Intelligence.CommandDirective)
            _activeDirective = new BridgeCommandDirective
            {
                commandId = request.commandId,
                btWinner = btWinner,
                targetEntityId = request.targetEntityId,
                targetPosition = request.targetPosition,
                expiresAt = Time.time + defaultCommandDuration,
            };
            _hasActiveDirective = true;

            // ③ 통계 갱신
            acceptedCount++;
            lastCommandId = request.commandId.ToString();

            // ④ 이벤트 발행 (시나리오 / UI 측 추적)
            try
            {
                OnCommandAccepted?.Invoke(request, _activeDirective);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CmdResolver] {name}: OnCommandAccepted 핸들러 예외: {e.Message}");
            }

            if (debugLog)
                Debug.Log($"[CmdResolver] {name}: Apply 완료 — '{request.commandId}' → BTWinner='{btWinner ?? "(미정의)"}', " +
                          $"만료 {defaultCommandDuration:F0}s 후 ({_activeDirective.expiresAt:F1})");
        }

        /// <summary>commandId → BT winner 매핑. 미정의 commandId → null (BT 분기 진입 안 함).</summary>
        /// <remarks>
        /// 본 매핑 vocabulary = StandardPlayerCommandIds 정적 상수 모음.
        /// 시나리오팀 합의 후 PlayerCommandType enum 격상 (Wk6+ 의제).
        /// </remarks>
        private static string MapCommandIdToBTWinner(FixedCommandId id)
        {
            string s = id.ToString();
            switch (s)
            {
                case StandardPlayerCommandIds.Attack: return "Attack";
                case StandardPlayerCommandIds.Wait: return "Idle";
                case StandardPlayerCommandIds.Hold: return "Idle";
                case StandardPlayerCommandIds.Track: return "FollowCommand";  // BT Follow 분기 (stalks only) — 추격 의미
                case StandardPlayerCommandIds.Retreat: return "Flee";
                case StandardPlayerCommandIds.Patrol: return "Patrol";
                // 후속: "disrupt" / "harass" — BT 신규 분기 합의 후 추가
                default: return null;  // 미정의 commandId
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // [region 4] ContextMenu & 디버그
        // ═══════════════════════════════════════════════════════════════════

        [ContextMenu("Debug/Test Command (Moderate Attack)")]
        private void DebugTestCmdAttack()
        {
            var req = new CommandRequest
            {
                issuerPlayerId = 0,
                commandId = FixedCommandId.From(StandardPlayerCommandIds.Attack),
                severity = CommandSeverity.Moderate,
                targetPosition = transform.position,
            };
            TryAccept(req, out float acc, out string reason);
        }

        [ContextMenu("Debug/Test Command (Trivial Wait)")]
        private void DebugTestCmdWait()
        {
            var req = new CommandRequest
            {
                issuerPlayerId = 0,
                commandId = FixedCommandId.From(StandardPlayerCommandIds.Wait),
                severity = CommandSeverity.Trivial,
                targetPosition = transform.position,
            };
            TryAccept(req, out float acc, out string reason);
        }

        [ContextMenu("★ [N-23] Print Active Directive")]
        private void DebugPrintActiveDirective()
        {
            if (!HasActiveDirective)
            {
                Debug.Log($"[CmdResolver] {name}: 활성 directive 없음 " +
                          $"(통계: 수락 {acceptedCount} / 거부 {rejectedCount} / 마지막 '{lastCommandId}' → '{lastBtWinner}')");
                return;
            }
            Debug.Log($"[CmdResolver] {name}: ActiveDirective\n" +
                      $"  commandId={_activeDirective.commandId}\n" +
                      $"  btWinner={_activeDirective.btWinner}\n" +
                      $"  targetEntityId={_activeDirective.targetEntityId}\n" +
                      $"  expiresAt={_activeDirective.expiresAt:F1} (현재 {Time.time:F1} / 남은 {_activeDirective.expiresAt - Time.time:F1}s)\n" +
                      $"  통계: 수락 {acceptedCount} / 거부 {rejectedCount}");
        }

        [ContextMenu("★ [N-23] Clear Active Directive")]
        private void DebugClearActiveDirective() => ClearActiveDirective();
    }
}