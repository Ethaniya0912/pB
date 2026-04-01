// =============================================================================
// NarrativeBiddingEvaluator.cs  |  pB-4 Project — Week 4
// Layer  : L3 Domain (시나리오)
// Namespace: TDA.PB4.Scenario
//
// 역할:
//   4대 아크 주체(환경/세력/업보/내부)가 각자의 입찰 점수를 제출하고,
//   최고 입찰자가 다음 아크의 주제를 결정하는 시스템.
//
//   환경 주체: Bid = (W_enclosure×EI) + (W_light×(1-L)) + (W_dist×(1/D_exit))
//   세력 주체: Bid = Σ(factionInfluence × dominanceBonus)
//   업보 주체: Bid = Σ(karmaWeight × moralAccumulation)
//   내부 주체: Bid = traumaSeverity × personalityDeviation
//
// 연계:
//   - ArcProgressionManager: 낙찰 결과에 따라 다음 Phase 주제 결정
//   - GameBlackboard: 지형 태그, 팩션 상태, 긴장도 데이터 소스
//   - TrustMatrix/TraumaSystem: 내부 주체의 입찰 데이터 소스
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.AI;

namespace TDA.PB4.Scenario
{
    /// <summary>개별 주체의 입찰 결과.</summary>
    [Serializable]
    public class BidResult
    {
        public string subjectName;
        public float bidScore;
        public string proposedTheme;
    }

    public class NarrativeBiddingEvaluator : MonoBehaviour
    {
        [Header("━━━ 환경 주체 가중치 ━━━━━━━━━━━━━━━━")]
        [Tooltip("폐쇄도(EI)의 가중치. 좁고 어두운 곳이 환경 서사를 유발.")]
        [Range(0f, 1f)] public float W_enclosure = 0.4f;
        [Tooltip("빛 부족(1-Light)의 가중치.")]
        [Range(0f, 1f)] public float W_light = 0.3f;
        [Tooltip("출구 거리(1/D_exit)의 가중치. 출구가 멀수록 환경 서사 강화.")]
        [Range(0f, 1f)] public float W_distance = 0.3f;

        [Header("━━━ 세력 주체 설정 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("dominant 팩션의 입찰 보너스 배수.")]
        [Range(1f, 3f)] public float dominanceBonusMultiplier = 1.5f;

        [Header("━━━ 업보 주체 설정 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("누적 도덕값. DilemmaNode 선택 시 가산/감산됨. " +
                 "양수=선한 행동 누적, 음수=악한 행동 누적. " +
                 "절대값이 클수록 업보 주체의 입찰이 강해짐.")]
        public float karmaAccumulation = 0f;
        [Tooltip("업보 입찰 가중치.")]
        [Range(0f, 2f)] public float karmaWeight = 1.0f;

        [Header("━━━ 내부 주체 참조 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("트라우마 시스템 참조. 비어있으면 씬에서 자동 탐색.")]
        public TraumaSystem traumaRef;
        [Tooltip("신뢰도 시스템 참조. 비어있으면 씬에서 자동 탐색.")]
        public TrustMatrix trustRef;

        [Header("━━━ 결과 (Read Only) ━━━━━━━━━━━━━━━━")]
        [SerializeField] private List<BidResult> lastBids = new List<BidResult>();
        [SerializeField] private string lastWinner = "";

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        private void Awake()
        {
            if (traumaRef == null) traumaRef = FindFirstObjectByType<TraumaSystem>();
            if (trustRef == null) trustRef = FindFirstObjectByType<TrustMatrix>();
        }

        // ==================================================================
        // 핵심 API: 4대 주체 입찰 실행
        // ==================================================================

        /// <summary>4대 주체의 입찰을 실행하고 최고 입찰자를 반환.</summary>
        public BidResult RunBidding()
        {
            lastBids.Clear();
            var bb = GameBlackboard.Instance;

            // 1. 환경 주체
            float envBid = 0f;
            if (bb != null)
            {
                float intensity = bb.GlobalIntensityScore;
                float enclosure = intensity; // 근사: 긴장도 ≈ 폐쇄도
                float darkness = intensity * 0.8f;
                float distanceFactor = intensity * 0.5f;
                envBid = (W_enclosure * enclosure) + (W_light * darkness) + (W_distance * distanceFactor);
            }
            lastBids.Add(new BidResult { subjectName = "환경", bidScore = envBid, proposedTheme = "동굴 공포/환경 위협" });

            // 2. 세력 주체
            float factionBid = 0f;
            if (bb != null && bb.ActiveFactionRegistry != null)
            {
                foreach (var kv in bb.ActiveFactionRegistry)
                {
                    float influence = kv.Value.populationLevel;
                    if (kv.Value.isDominant) influence *= dominanceBonusMultiplier;
                    factionBid += influence;
                }
                factionBid /= Mathf.Max(1, bb.ActiveFactionRegistry.Count); // 평균
            }
            lastBids.Add(new BidResult { subjectName = "세력", bidScore = factionBid, proposedTheme = "팩션 갈등/영토 분쟁" });

            // 3. 업보 주체
            float karmaBid = Mathf.Abs(karmaAccumulation) * karmaWeight * 0.3f;
            string karmaTheme = karmaAccumulation >= 0 ? "속죄/정의" : "타락/복수";
            lastBids.Add(new BidResult { subjectName = "업보", bidScore = karmaBid, proposedTheme = karmaTheme });

            // 4. 내부 주체
            float internalBid = 0f;
            if (traumaRef != null && traumaRef.CurrentStage != TraumaStage.None)
                internalBid += 0.3f;
            if (trustRef != null && trustRef.CurrentTier == TrustTier.Hostility)
                internalBid += 0.4f;
            else if (trustRef != null && trustRef.CurrentTier == TrustTier.Doubt)
                internalBid += 0.2f;
            lastBids.Add(new BidResult { subjectName = "내부", bidScore = internalBid, proposedTheme = "배신/트라우마 극복" });

            // 최고 입찰자 결정
            BidResult winner = lastBids[0];
            foreach (var b in lastBids)
                if (b.bidScore > winner.bidScore) winner = b;
            lastWinner = winner.subjectName;

            if (debugLog)
            {
                Debug.Log("[Bidding] ═══ 4대 주체 입찰 결과 ═══");
                foreach (var b in lastBids)
                    Debug.Log($"  {(b.subjectName == lastWinner ? "★" : " ")} {b.subjectName}: {b.bidScore:F3} → {b.proposedTheme}");
                Debug.Log($"  낙찰: {lastWinner}");
            }

            return winner;
        }

        /// <summary>업보에 도덕값 가산. DilemmaNode 선택 결과에서 호출.</summary>
        public void AddKarma(float moral) { karmaAccumulation += moral; }

        public IReadOnlyList<BidResult> LastBids => lastBids;
        public string LastWinner => lastWinner;
    }
}
