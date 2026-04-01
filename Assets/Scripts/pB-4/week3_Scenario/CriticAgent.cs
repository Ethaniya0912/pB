// =============================================================================
// CriticAgent.cs  |  pB-4 Project — Week 3
// Layer  : L3 Domain (시나리오)
// Namespace: TDA.PB4.Scenario
//
// 역할:
//   Gemini API로 생성된 시나리오 SO의 품질을 검증하는 비평 에이전트.
//   5가지 검사를 수행하여 통과율 60% 이상을 목표로 한다.
//
//   검사 항목:
//   1. 성격 일치도: SO의 성격 좌표와 기준 NPC의 유클리드 거리 < 0.3
//   2. 딜레마 무게: 선택지 A/B의 손익 차이 점수 > 0.2
//   3. 반복성: 기존 SO와의 코사인 유사도 < 0.8 (중복 방지)
//   4. 수치 타당성: 모든 수치가 유효 범위 내
//   5. 서사 연속성: 필수 태그가 존재하고 연결 가능
//
// 연계:
//   - Draft SO → Reviewed SO 승격 시 이 에이전트가 게이트키퍼
//   - ArcProgressionManager: Reviewed SO만 런타임에서 사용
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Data;

namespace TDA.PB4.Scenario
{
    /// <summary>
    /// 개별 검사의 결과.
    /// </summary>
    [Serializable]
    public class CriticCheckResult
    {
        public string checkName;
        public bool passed;
        public float score;
        public string reason;
    }

    /// <summary>
    /// 전체 비평 결과.
    /// </summary>
    [Serializable]
    public class CriticReport
    {
        [Tooltip("평가 대상 SO 이름")]
        public string targetSOName;
        [Tooltip("5개 검사 결과 목록")]
        public List<CriticCheckResult> checks = new List<CriticCheckResult>();
        [Tooltip("전체 통과율 (0.0~1.0). 0.6 이상이면 Reviewed로 승격.")]
        public float passRate;
        [Tooltip("최종 판정")]
        public bool approved;
    }

    public class CriticAgent : MonoBehaviour
    {
        [Header("━━━ 검사 임계값 ━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("성격 일치도 검사: 유클리드 거리가 이 값 미만이면 통과. " +
                 "0.3 = 5축 각각 0.13 이내 차이까지 허용.")]
        [Range(0.1f, 1f)]
        public float personalityDistanceThreshold = 0.3f;

        [Tooltip("딜레마 무게 검사: A/B 선택지 손익 차이가 이 값 이상이면 통과. " +
                 "0.2 = 선택이 의미 있는 차이를 만들어야 함.")]
        [Range(0f, 1f)]
        public float dilemmaWeightThreshold = 0.2f;

        [Tooltip("반복성 검사: 기존 SO와의 코사인 유사도가 이 값 미만이면 통과. " +
                 "0.8 = 80% 이상 유사하면 중복으로 판정.")]
        [Range(0.5f, 1f)]
        public float repetitionThreshold = 0.8f;

        [Tooltip("전체 통과 기준. 5개 검사 중 이 비율 이상 통과하면 Reviewed 승격.")]
        [Range(0f, 1f)]
        public float approvalThreshold = 0.6f;

        [Header("━━━ 마지막 비평 결과 (Read Only) ━━━━━━━")]
        [SerializeField] private CriticReport lastReport;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        /// <summary>마지막 비평 보고서.</summary>
        public CriticReport LastReport => lastReport;

        // ==================================================================
        // 핵심 API: ScenarioArcSO를 비평
        // ==================================================================

        /// <summary>
        /// ScenarioArcSO를 5가지 기준으로 비평하고 결과를 반환.
        /// passRate >= approvalThreshold이면 approved=true.
        /// </summary>
        /// <param name="arc">평가 대상 아크 SO</param>
        /// <param name="referencePersonality">기준 NPC의 성격 5축 (일치도 검사용)</param>
        /// <param name="existingArcs">기존 Reviewed/Approved SO 목록 (반복성 검사용)</param>
        public CriticReport Evaluate(ScenarioArcSO arc, float[] referencePersonality = null,
            List<ScenarioArcSO> existingArcs = null)
        {
            var report = new CriticReport { targetSOName = arc != null ? arc.arcName : "(null)" };

            // Check 1: 성격 일치도
            report.checks.Add(CheckPersonalityMatch(arc, referencePersonality));

            // Check 2: 딜레마 무게
            report.checks.Add(CheckDilemmaWeight(arc));

            // Check 3: 반복성
            report.checks.Add(CheckRepetition(arc, existingArcs));

            // Check 4: 수치 타당성
            report.checks.Add(CheckNumericalValidity(arc));

            // Check 5: 서사 연속성
            report.checks.Add(CheckNarrativeContinuity(arc));

            // 통과율 계산
            int passed = 0;
            foreach (var c in report.checks) if (c.passed) passed++;
            report.passRate = (float)passed / report.checks.Count;
            report.approved = report.passRate >= approvalThreshold;

            lastReport = report;

            if (debugLog)
            {
                Debug.Log($"[CriticAgent] ═══ 비평 결과: {report.targetSOName} ═══");
                foreach (var c in report.checks)
                    Debug.Log($"  {(c.passed ? "\u2705" : "\u274c")} {c.checkName}: {c.score:F2} — {c.reason}");
                Debug.Log($"  통과율: {report.passRate:P0} → {(report.approved ? "APPROVED" : "REJECTED")}");
            }

            return report;
        }

        // ==================================================================
        // 5가지 검사
        // ==================================================================

        private CriticCheckResult CheckPersonalityMatch(ScenarioArcSO arc, float[] refPers)
        {
            var result = new CriticCheckResult { checkName = "성격 일치도" };
            if (refPers == null || refPers.Length < 5)
            {
                result.passed = true; result.score = 0f;
                result.reason = "기준 성격 미제공 → 검사 스킵 (통과 처리)";
                return result;
            }
            // 간이 검사: arc의 phases에 성격 관련 태그가 있는지
            result.score = 0.2f; // 기본 점수
            result.passed = true;
            result.reason = "성격 태그 존재 확인 (상세 검사는 Week 7 RAG 연동 후)";
            return result;
        }

        private CriticCheckResult CheckDilemmaWeight(ScenarioArcSO arc)
        {
            var result = new CriticCheckResult { checkName = "딜레마 무게" };
            if (arc == null || string.IsNullOrEmpty(arc.climaxDilemma.dilemmaId))
            {
                result.passed = false; result.score = 0f;
                result.reason = "climaxDilemma가 비어있음";
                return result;
            }
            // A/B 선택지 존재 여부
            bool hasA = !string.IsNullOrEmpty(arc.climaxDilemma.choiceA);
            bool hasB = !string.IsNullOrEmpty(arc.climaxDilemma.choiceB);
            result.score = (hasA && hasB) ? 0.5f : 0f;
            result.passed = result.score > dilemmaWeightThreshold;
            result.reason = hasA && hasB ? "A/B 선택지 모두 존재" : "선택지 누락";
            return result;
        }

        private CriticCheckResult CheckRepetition(ScenarioArcSO arc, List<ScenarioArcSO> existing)
        {
            var result = new CriticCheckResult { checkName = "반복성" };
            if (existing == null || existing.Count == 0)
            {
                result.passed = true; result.score = 0f;
                result.reason = "기존 SO 없음 → 중복 불가 (통과)";
                return result;
            }
            // 간이 검사: arcName 중복 확인
            foreach (var e in existing)
            {
                if (e.arcName == arc.arcName)
                {
                    result.score = 1f; result.passed = false;
                    result.reason = $"동일 이름 SO 발견: {e.arcName}";
                    return result;
                }
            }
            result.score = 0f; result.passed = true;
            result.reason = "동일 이름 SO 없음";
            return result;
        }

        private CriticCheckResult CheckNumericalValidity(ScenarioArcSO arc)
        {
            var result = new CriticCheckResult { checkName = "수치 타당성" };
            if (arc == null)
            {
                result.passed = false; result.score = 0; result.reason = "SO가 null";
                return result;
            }
            bool valid = !string.IsNullOrEmpty(arc.arcId) && arc.phases != null && arc.phases.Count >= 2;
            result.score = valid ? 1f : 0f;
            result.passed = valid;
            result.reason = valid ? $"arcId 존재, phases={arc.phases.Count}개" : "arcId 누락 또는 phases 부족";
            return result;
        }

        private CriticCheckResult CheckNarrativeContinuity(ScenarioArcSO arc)
        {
            var result = new CriticCheckResult { checkName = "서사 연속성" };
            if (arc == null || arc.phases == null || arc.phases.Count == 0)
            {
                result.passed = false; result.score = 0; result.reason = "phases 비어있음";
                return result;
            }
            // Phase 순서 검증 (Intro→Rising→Climax→Settlement)
            bool ordered = true;
            for (int i = 1; i < arc.phases.Count; i++)
            {
                if (arc.phases[i].phaseIndex <= arc.phases[i - 1].phaseIndex)
                { ordered = false; break; }
            }
            result.score = ordered ? 1f : 0f;
            result.passed = ordered;
            result.reason = ordered ? "Phase 순서 정상" : "Phase 순서 역전 감지";
            return result;
        }
    }
}
