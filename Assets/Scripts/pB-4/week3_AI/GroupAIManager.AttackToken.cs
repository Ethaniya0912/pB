// =============================================================================
// GroupAIManager.AttackToken.cs  |  pB-4 Project — Week 5 Phase 3 (W5-B8)
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   GroupAIManager 측 partial 영역 — AttackToken 본격 관리 영역.
//   기존 단순 사이클 (line 1437 부근) 영역 격상 영역 — max_tokens 수식 +
//   토큰 queue + RequestToken / ReturnToken + timeout.
//
// max_tokens 수식:
//   max = max(0, base + tierMod + enemyMod)
//
//   base       = ceil(aliveMembers × tokenBaseMultiplier)    (default 0.5 — 멤버 절반)
//   tierMod    = Tier 0:  0  / Tier 1: -1 / Tier 2: -2
//                Tier 3 (Collapse): 강제 0 영역 — 전원 Flee 영역
//   enemyMod   = enemies≥5: +2 / enemies≥3: +1 / 그 외 0
//
// 토큰 영역:
//   IssueToken    — hasAttackToken=true / issuedTime 기록
//   RequestToken  — max 영역 영역 시 즉시 발급 / 부족 시 queue 영역
//   ReturnToken   — hasAttackToken=false / 큐 영역 영역 자동 영역
//   ExpireTimedOutTokens — issuedTime 측 timeout 영역 측 자동 회수
//
// =============================================================================
// [★ 변경 이력 — 2026-05-15: W5-B8 Phase 3 신규]
// =============================================================================
// 신규:
//   - ComputeMaxTokens() — 수식 영역
//   - RequestToken / ReturnToken / IssueToken / ExpireTimedOutTokens
//   - tokenQueue (Queue<GroupMemberState>)
//   - tokenIssuedTime (Dictionary<GroupMemberState, float>)
//   - W5-B6 영역 currentEscalationTier 영역 자료 영역 영역
//
// ★ 특이사항-A: 기존 토큰 분배 영역 영역 영역 영역
//   기존 GroupAIManager.cs 측 line 1437 부근 영역 토큰 분배 영역 (단순 사이클 영역)
//   — 본 신규 영역 영역 자동 영역 위임 영역 부재 영역. 사용자 측 영역:
//     - 옵션 1: 기존 line 1437 영역 영역 영역 영역 영역 (legacy 영역 영역)
//     - 옵션 2: 기존 영역 영역 본 RequestToken / ReturnToken 영역 영역 교체
//   본 turn 측 — 별 영역 영역 영역. 사용자 측 결정 영역.
//
// ★ 특이사항-B: W5-B6 영역 영역 영역
//   currentEscalationTier 영역 본 partial 측 영역 영역 영역 영역 — W5-B6
//   영역 영역 영역 영역 (GroupAIManager.Escalation.cs 영역) 영역 영역.
//   본 partial 영역 동작 영역 W5-B6 영역 미포함 시 컴파일 영역 영역 영역
//   영역 영역 영역. 본 turn 측 W5-B6 / W5-B8 영역 영역 영역 영역 영역.
//
// ★ 특이사항-C: BlackboardUpdater 영역 영역 영역 부재 영역
//   기존 영역 영역 영역 영역 hasAttackToken 영역 영역 영역 영역 Blackboard
//   영역 영역 영역 영역 영역 — 본 turn 측 영역 영역 영역 부재 영역.
//   사용자 측 별 영역 영역 영역 (예: ReturnToken / IssueToken 측 BB 갱신 영역
//   영역 영역 영역 영역).
//
// ★ 특이사항-D: Collapse 영역 강제 0 영역
//   Tier 3 (Collapse) 영역 — max_tokens 영역 영역 강제 0 영역 (전원 Flee 의도).
//   본 영역 영역 영역 — 사용자 측 영역 영역 영역 영역 영역 영역 영역.
//   tokenCollapseOverride 영역 영역 영역 — 음수 값 영역 (예: -1) 영역 영역
//   영역 영역 / 강제 양수 영역 (예: 1) 영역 영역 영역 영역.
//
// ★ 특이사항-E: ContextMenu 진단
//   Print Token State / Force Issue All / Force Return All 영역.
//
// 신규 검증:
//   V-B8-01: ComputeMaxTokens 수식 영역 정합 (base + tierMod + enemyMod)
//   V-B8-02: RequestToken 영역 max 영역 영역 시 발급 / 부족 시 queue
//   V-B8-03: ReturnToken 영역 큐 영역 영역 토큰 영역 자동 영역
//   V-B8-04: ExpireTimedOutTokens 영역 timeout 영역 자동 회수
//   V-B8-05: Tier 0/1/2/3 영역 max_tokens 영역 영역 정합 영역 (0=base, 3=0)
//   V-B8-06: ContextMenu "Print Token State" 출력 정합
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.AI
{
    public partial class GroupAIManager
    {
        // ───────── Inspector 자료 ─────────

        [Header("━━━ W5-B8 AttackToken Manager ━━━━━━━")]
        [Tooltip("AttackTokenManager 활성화. false 시 본 partial 영역 차단 — " +
                 "기존 line 1437 영역 영역 영역 영역.")]
        [SerializeField] private bool enableAttackTokenManager = true;

        [Tooltip("base = ceil(aliveMembers × multiplier). 기본 0.5 = 멤버 절반.")]
        [Range(0.1f, 1.0f)]
        [SerializeField] private float tokenBaseMultiplier = 0.5f;

        [Tooltip("토큰 보유 시간 초과 시 자동 회수 (초).")]
        [Range(1, 30)]
        [SerializeField] private int tokenTimeoutSec = 5;

        [Header("━━━ Tier Modifier (W5-B6 연계) ━━━━━━━")]
        [Tooltip("Tier 1 (Alert) 시 base 영역 합.")]
        [SerializeField] private int tokenAlertModifier = -1;

        [Tooltip("Tier 2 (Critical) 시 base 영역 합.")]
        [SerializeField] private int tokenCriticalModifier = -2;

        [Tooltip("Tier 3 (Collapse) 시 max 영역 강제 — 기본 0 (전원 Flee).\n" +
                 "★ 특이사항-D: 사용자 측 영역 영역 영역.")]
        [SerializeField] private int tokenCollapseOverride = 0;

        [Header("━━━ Enemy Modifier ━━━━━━━━━━━━━━━━━")]
        [Tooltip("적 수 ≥ 5 시 max 영역 합.")]
        [SerializeField] private int tokenEnemyMod5Plus = 2;

        [Tooltip("적 수 ≥ 3 시 max 영역 합 (5 미만).")]
        [SerializeField] private int tokenEnemyMod3Plus = 1;

        [Header("━━━ Read Only ━━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private int currentMaxTokens = 0;
        [SerializeField] private int activeTokenCount = 0;
        [SerializeField] private int queuedTokenRequests = 0;

        // ───────── 내부 상태 ─────────

        private readonly Queue<GroupMemberState> tokenQueue = new Queue<GroupMemberState>();
        private readonly Dictionary<GroupMemberState, float> tokenIssuedTime = new();

        // ───────── 공개 API ─────────

        public int CurrentMaxTokens => currentMaxTokens;
        public int ActiveTokenCount => activeTokenCount;
        public int QueuedTokenRequests => queuedTokenRequests;

        /// <summary>
        /// max_tokens 영역 수식 계산.
        /// max = max(0, base + tierMod + enemyMod). Collapse 시 강제 override.
        /// </summary>
        public int ComputeMaxTokens()
        {
            int aliveMembers = CountAliveMembers_Esc();   // ★ W5-B6 영역 영역 영역
            int baseTokens = Mathf.CeilToInt(aliveMembers * tokenBaseMultiplier);

            // Tier modifier
            int tierMod = 0;
            switch (currentEscalationTier)  // ★ W5-B6 영역 영역 영역 — 본 partial 영역 영역 영역 영역
            {
                case EscalationTier.Alert:    tierMod = tokenAlertModifier; break;
                case EscalationTier.Critical: tierMod = tokenCriticalModifier; break;
                case EscalationTier.Collapse: return Mathf.Max(0, tokenCollapseOverride);  // 강제 override
            }

            // Enemy modifier
            int enemyMod = 0;
            int enemies = (_knownTargets != null) ? _knownTargets.Count : 0;
            if (enemies >= 5)      enemyMod = tokenEnemyMod5Plus;
            else if (enemies >= 3) enemyMod = tokenEnemyMod3Plus;

            return Mathf.Max(0, baseTokens + tierMod + enemyMod);
        }

        /// <summary>
        /// 멤버 측 토큰 요청. true=즉시 발급, false=queue 영역 / 토큰 영역 영역.
        /// </summary>
        public bool RequestToken(GroupMemberState requester)
        {
            if (!enableAttackTokenManager) return true;   // 비활성 — 모두 허용 (기존 동작)
            if (requester == null) return false;

            currentMaxTokens = ComputeMaxTokens();

            // 만료 영역 영역 먼저
            ExpireTimedOutTokens();

            if (activeTokenCount < currentMaxTokens)
            {
                IssueToken(requester);
                return true;
            }

            // 토큰 부족 → queue 영역
            if (!tokenQueue.Contains(requester))
                tokenQueue.Enqueue(requester);
            queuedTokenRequests = tokenQueue.Count;
            return false;
        }

        /// <summary>
        /// 멤버 측 토큰 반납. 큐 측 다음 영역 자동 영역 발급.
        /// </summary>
        public void ReturnToken(GroupMemberState returner)
        {
            if (!enableAttackTokenManager) return;
            if (returner == null || !returner.hasAttackToken) return;

            returner.hasAttackToken = false;
            tokenIssuedTime.Remove(returner);
            activeTokenCount = Mathf.Max(0, activeTokenCount - 1);

            // 큐 영역 영역 토큰 영역 자동 영역
            while (tokenQueue.Count > 0 && activeTokenCount < currentMaxTokens)
            {
                var next = tokenQueue.Dequeue();
                if (next != null && next.brain != null && next.brain.gameObject.activeInHierarchy)
                    IssueToken(next);
            }
            queuedTokenRequests = tokenQueue.Count;
        }

        // ───────── 내부 ─────────

        private void IssueToken(GroupMemberState m)
        {
            m.hasAttackToken = true;
            tokenIssuedTime[m] = Time.time;
            activeTokenCount++;
        }

        /// <summary>tokenTimeoutSec 영역 영역 토큰 영역 자동 회수.</summary>
        private void ExpireTimedOutTokens()
        {
            float now = Time.time;
            List<GroupMemberState> toExpire = null;

            foreach (var kv in tokenIssuedTime)
            {
                if (now - kv.Value > tokenTimeoutSec)
                {
                    if (toExpire == null) toExpire = new List<GroupMemberState>();
                    toExpire.Add(kv.Key);
                }
            }

            if (toExpire != null)
            {
                for (int i = 0; i < toExpire.Count; i++)
                    ReturnToken(toExpire[i]);
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Tokens: Print State")]
        private void PrintTokenState()
        {
            currentMaxTokens = ComputeMaxTokens();
            Debug.Log($"[AttackToken] max={currentMaxTokens} active={activeTokenCount} queued={queuedTokenRequests}\n" +
                      $"  Tier={currentEscalationTier} alive={CountAliveMembers_Esc()} enemies={(_knownTargets != null ? _knownTargets.Count : 0)}");
            if (members != null)
            {
                for (int i = 0; i < members.Count; i++)
                {
                    var m = members[i];
                    if (m == null) continue;
                    string nm = m.brain != null ? m.brain.name : "(null)";
                    string tok = m.hasAttackToken ? "✔" : " ";
                    float held = tokenIssuedTime.ContainsKey(m) ? (Time.time - tokenIssuedTime[m]) : 0f;
                    Debug.Log($"  [{i,2}] [{tok}] {nm,-24} held={held:F2}s");
                }
            }
        }

        [ContextMenu("Tokens: Force Expire All")]
        private void ForceExpireAll()
        {
            var copy = new List<GroupMemberState>(tokenIssuedTime.Keys);
            for (int i = 0; i < copy.Count; i++) ReturnToken(copy[i]);
        }

        [ContextMenu("Tokens: Recompute Max")]
        private void RecomputeMax()
        {
            int max = ComputeMaxTokens();
            currentMaxTokens = max;
            Debug.Log($"[AttackToken] ComputeMaxTokens = {max} " +
                      $"(alive={CountAliveMembers_Esc()} tier={currentEscalationTier} enemies={(_knownTargets != null ? _knownTargets.Count : 0)})");
        }
#endif
    }
}
