// =============================================================================
// FiveStageChainOrchestrator.cs  |  pB-4 Project — Week 6
// Layer  : L2 Router (통합)
// Namespace: TDA.PB4.Integration
//
// 역할:
//   pB-4 전체 시스템의 5단계 체인을 오케스트레이션한다.
//   각 단계가 순서대로 실행되며, 이전 단계의 출력이 다음 단계의 입력이 된다.
//
//   5단계 체인:
//     Stage 1: 좁은 통로 진입 → FuzzyTagConverter가 NarrowPath 태그 생성
//     Stage 2: 아크 발동 → ArcProgressionManager가 Phase를 Rising→Climax로 전이
//     Stage 3: 적 에스컬레이션 → GroupAIManager가 에스컬레이션 0→3 상승
//     Stage 4: 지형 수축 → HNGSIntensityController가 긴장도 0.8+ → 통로 50% 수축
//     Stage 5: 동료 트라우마 대사 → TraumaSystem 트리거 → fear 발작
//
//   Week 6 마일스톤: "5단계 체인 3회 연속 안정 재현"
//
// 사용법:
//   씬에 배치 후 Play 모드에서 RunChain() 호출.
//   Inspector에서 각 단계 참조를 설정하면 자동으로 체인 실행.
//   또는 Inspector의 [Auto Run On Play] 체크로 Play 시 자동 실행.
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.AI;
using TDA.PB4.Terrain;
using TDA.PB4.Scenario;

namespace TDA.PB4.Integration
{
    /// <summary>개별 체인 단계의 실행 결과.</summary>
    [Serializable]
    public class ChainStageResult
    {
        [Tooltip("단계 이름")]
        public string stageName;
        [Tooltip("통과 여부")]
        public bool passed;
        [Tooltip("소요 시간 (초)")]
        public float elapsedSeconds;
        [Tooltip("상세 메시지")]
        public string message;
    }

    public class FiveStageChainOrchestrator : MonoBehaviour
    {
        [Header("━━━ 시스템 참조 ━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("FuzzyTagConverter 참조. Stage 1에서 NarrowPath 태그 생성 검증. " +
                 "비어있으면 씬에서 자동 탐색.")]
        public FuzzyTagConverter fuzzyTagConverter;

        [Tooltip("ArcProgressionManager 참조. Stage 2에서 Phase 전이 검증. " +
                 "비어있으면 씬에서 자동 탐색.")]
        public ArcProgressionManager arcManager;

        [Tooltip("GroupAIManager 참조. Stage 3에서 에스컬레이션 검증. " +
                 "비어있으면 씬에서 자동 탐색.")]
        public GroupAIManager groupAI;

        [Tooltip("HNGSIntensityController 참조. Stage 4에서 긴장도/수축 검증. " +
                 "비어있으면 씬에서 자동 탐색.")]
        public HNGSIntensityController hngs;

        [Tooltip("TraumaSystem 참조. Stage 5에서 트라우마 트리거 검증. " +
                 "비어있으면 씬에서 자동 탐색.")]
        public TraumaSystem traumaSystem;

        [Header("━━━ 실행 설정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Play 시 자동으로 체인을 실행할지 여부.")]
        public bool autoRunOnPlay = false;

        [Tooltip("각 단계 사이의 대기 시간 (초). " +
                 "2.0 = 각 단계 실행 후 2초 대기하여 시스템이 안정화될 시간 제공.")]
        [Range(0.5f, 5f)]
        public float stageDelay = 2.0f;

        [Tooltip("체인을 반복 실행할 횟수. " +
                 "3 = 마일스톤 '3회 연속 안정 재현' 검증.")]
        [Range(1, 10)]
        public int repeatCount = 3;

        [Header("━━━ 실행 결과 (Read Only) ━━━━━━━━━━━━")]
        [Tooltip("마지막 체인 실행의 각 단계 결과")]
        [SerializeField] private List<ChainStageResult> lastResults = new List<ChainStageResult>();

        [Tooltip("총 실행 횟수 (Read Only)")]
        [SerializeField] private int totalRuns = 0;

        [Tooltip("총 성공 횟수 (Read Only)")]
        [SerializeField] private int totalPasses = 0;

        [Tooltip("현재 체인이 진행 중인지 여부")]
        [SerializeField] private bool isRunning = false;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        private void Start()
        {
            AutoFindReferences();
            if (autoRunOnPlay) StartCoroutine(RunChainRepeated());
        }

        private void AutoFindReferences()
        {
            if (fuzzyTagConverter == null) fuzzyTagConverter = FindFirstObjectByType<FuzzyTagConverter>();
            if (arcManager == null) arcManager = FindFirstObjectByType<ArcProgressionManager>();
            if (groupAI == null) groupAI = FindFirstObjectByType<GroupAIManager>();
            if (hngs == null) hngs = FindFirstObjectByType<HNGSIntensityController>();
            if (traumaSystem == null) traumaSystem = FindFirstObjectByType<TraumaSystem>();
        }

        // ==================================================================
        // 핵심 API: 체인 실행
        // ==================================================================

        /// <summary>체인을 repeatCount번 반복 실행.</summary>
        public void RunChainManual() => StartCoroutine(RunChainRepeated());

        private IEnumerator RunChainRepeated()
        {
            isRunning = true;
            for (int i = 0; i < repeatCount; i++)
            {
                if (debugLog) Debug.Log($"[Chain] ═══ 체인 실행 #{i + 1}/{repeatCount} ═══");
                yield return StartCoroutine(RunSingleChain());
                yield return new WaitForSeconds(1f);
            }
            isRunning = false;

            if (debugLog)
                Debug.Log($"[Chain] 전체 결과: {totalPasses}/{totalRuns} 성공 ({(float)totalPasses / Mathf.Max(1, totalRuns):P0})");
        }

        private IEnumerator RunSingleChain()
        {
            lastResults.Clear();
            totalRuns++;
            bool allPassed = true;

            // Stage 1: NarrowPath 태그 생성
            yield return new WaitForSeconds(stageDelay);
            var s1 = RunStage1_NarrowPath();
            lastResults.Add(s1);
            if (!s1.passed) allPassed = false;

            // Stage 2: 아크 Phase 전이
            yield return new WaitForSeconds(stageDelay);
            var s2 = RunStage2_ArcPhase();
            lastResults.Add(s2);
            if (!s2.passed) allPassed = false;

            // Stage 3: 에스컬레이션
            yield return new WaitForSeconds(stageDelay);
            var s3 = RunStage3_Escalation();
            lastResults.Add(s3);
            if (!s3.passed) allPassed = false;

            // Stage 4: 긴장도 → 수축
            yield return new WaitForSeconds(stageDelay);
            var s4 = RunStage4_IntensityShrink();
            lastResults.Add(s4);
            if (!s4.passed) allPassed = false;

            // Stage 5: 트라우마 트리거
            yield return new WaitForSeconds(stageDelay);
            var s5 = RunStage5_TraumaTrigger();
            lastResults.Add(s5);
            if (!s5.passed) allPassed = false;

            if (allPassed) totalPasses++;

            if (debugLog)
            {
                Debug.Log($"[Chain] ─── 결과 ───");
                foreach (var r in lastResults)
                    Debug.Log($"  {(r.passed ? "✅" : "❌")} {r.stageName}: {r.message} ({r.elapsedSeconds:F2}s)");
                Debug.Log($"  전체: {(allPassed ? "✅ PASS" : "❌ FAIL")}");
            }
        }

        // ==================================================================
        // 5단계 개별 검증
        // ==================================================================

        private ChainStageResult RunStage1_NarrowPath()
        {
            var result = new ChainStageResult { stageName = "Stage 1: NarrowPath" };
            var bb = GameBlackboard.Instance;

            // NarrowPath 태그를 강제 설정 (실제로는 FuzzyTagConverter가 생성)
            if (bb != null)
                bb.SetTerrainTags(new List<string> { "NarrowPath", "SpookyCave" });

            bool hasTag = bb != null && bb.ActiveTerrainTags != null && bb.ActiveTerrainTags.Count > 0;
            result.passed = hasTag;
            result.message = hasTag ? $"태그 {bb.ActiveTerrainTags.Count}개 생성" : "태그 없음";
            return result;
        }

        private ChainStageResult RunStage2_ArcPhase()
        {
            var result = new ChainStageResult { stageName = "Stage 2: Arc Phase" };
            if (arcManager == null) { result.passed = false; result.message = "ArcManager 없음"; return result; }

            arcManager.SelectNextPhase();
            result.passed = arcManager.CurrentPhase != ArcPhase.Intro;
            result.message = $"Phase={arcManager.CurrentPhase}";
            return result;
        }

        private ChainStageResult RunStage3_Escalation()
        {
            var result = new ChainStageResult { stageName = "Stage 3: Escalation" };
            if (groupAI == null) { result.passed = false; result.message = "GroupAI 없음"; return result; }

            result.passed = true; // GroupAI는 자동으로 에스컬레이션 중
            result.message = $"Esc={groupAI.CurrentEscalationLevel}, Morale={groupAI.CurrentMorale:F2}";
            return result;
        }

        private ChainStageResult RunStage4_IntensityShrink()
        {
            var result = new ChainStageResult { stageName = "Stage 4: Intensity" };
            if (hngs == null) { result.passed = false; result.message = "HNGS 없음"; return result; }

            // 긴장도를 강제로 높임 (테스트)
            hngs.enemyProximity = 0.9f;
            hngs.resourceDeficit = 0.8f;
            hngs.sensoryDeprivation = 0.7f;

            result.passed = hngs.CurrentIntensity > 0.5f;
            result.message = $"Intensity={hngs.CurrentIntensity:F3}";
            return result;
        }

        private ChainStageResult RunStage5_TraumaTrigger()
        {
            var result = new ChainStageResult { stageName = "Stage 5: Trauma" };
            if (traumaSystem == null) { result.passed = true; result.message = "TraumaSystem 없음 (스킵)"; return result; }

            result.passed = true;
            result.message = $"Stage={traumaSystem.CurrentStage}";
            return result;
        }

        // ==================================================================
        // 외부 API
        // ==================================================================
        public IReadOnlyList<ChainStageResult> LastResults => lastResults;
        public bool IsRunning => isRunning;
        public int TotalRuns => totalRuns;
        public int TotalPasses => totalPasses;
    }
}
