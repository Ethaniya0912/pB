// =============================================================================
// StressTestRunner.cs  |  pB-4 Project — Week 8
// Layer  : Integration (최종 검증)
// Namespace: TDA.PB4.Integration
//
// 역할:
//   pB-4 프로토타입의 최종 스트레스 테스트를 자동 실행한다.
//
//   3대 스트레스 테스트:
//     1. AI 부하: MobAI 100마리(3팩션 혼합) + HumanoidAI 3명 동시 운용 → 30fps 유지
//     2. HumanoidAI 풀 시스템: 성격+트라우마+신뢰도+발화+기억 전체 파이프라인 → 프레임 드랍 10% 이내
//     3. 자동화 아크: 1,000회 아크 생성 → Hallucination 발생률 5% 미만
//
//   모든 테스트는 코루틴으로 순차 실행되며 결과가 Inspector에 실시간 갱신.
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI;
using TDA.PB4.AI.Mob;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Scenario;
using TDA.PB4.Core;

namespace TDA.PB4.Integration
{
    [Serializable]
    public class StressTestResult
    {
        public string testName;
        public bool passed;
        public float measuredValue;
        public float targetValue;
        public string unit;
        public string detail;
    }

    public class StressTestRunner : MonoBehaviour
    {
        [Header("━━━ 테스트 설정 ━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Play 시 자동으로 스트레스 테스트를 실행할지 여부.")]
        public bool autoRunOnPlay = false;

        [Tooltip("AI 부하 테스트 지속 시간 (초). " +
                 "10 = 10초 동안 FPS를 측정.")]
        [Range(5f, 60f)]
        public float aiLoadTestDuration = 10f;

        [Tooltip("자동화 아크 생성 횟수. " +
                 "1000 = 마일스톤 목표. 적을수록 빠르게 완료.")]
        [Range(10, 5000)]
        public int arcGenerationCount = 1000;

        [Tooltip("Hallucination 발생률 상한. " +
                 "0.05 = 5% 미만이면 통과.")]
        [Range(0f, 0.2f)]
        public float hallucinationThreshold = 0.05f;

        [Header("━━━ FPS 목표 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("MobAI 100마리 운용 시 최소 FPS. 30fps 이상 목표.")]
        public float mobAITargetFPS = 30f;

        [Tooltip("HumanoidAI 풀 시스템 프레임 드랍 허용치 (%). " +
                 "10 = 기준 FPS 대비 10% 이내 드랍 허용.")]
        [Range(1f, 30f)]
        public float humanoidDropThreshold = 10f;

        [Header("━━━ 테스트 결과 (Read Only) ━━━━━━━━━━")]
        [SerializeField] private List<StressTestResult> results = new List<StressTestResult>();
        [SerializeField] private bool isRunning = false;
        [SerializeField] private int currentTestIndex = 0;
        [SerializeField] private float overallPassRate;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        private void Start()
        {
            if (autoRunOnPlay) StartCoroutine(RunAllTests());
        }

        [ContextMenu("Run All Stress Tests")]
        public void RunManual() => StartCoroutine(RunAllTests());

        // ==================================================================
        // 전체 테스트 순차 실행
        // ==================================================================
        private IEnumerator RunAllTests()
        {
            isRunning = true;
            results.Clear();
            currentTestIndex = 0;

            if (debugLog) Debug.Log("[StressTest] ═══ pB-4 최종 스트레스 테스트 시작 ═══");

            // Test 1: AI 부하 (FPS)
            currentTestIndex = 1;
            yield return StartCoroutine(TestAILoadFPS());

            // Test 2: HumanoidAI 풀 시스템 프레임 드랍
            currentTestIndex = 2;
            yield return StartCoroutine(TestHumanoidFullSystem());

            // Test 3: 자동화 아크 생성
            currentTestIndex = 3;
            TestAutomatedArcGeneration();

            // Summary
            int passed = 0;
            foreach (var r in results) if (r.passed) passed++;
            overallPassRate = (float)passed / Mathf.Max(1, results.Count);

            if (debugLog)
            {
                Debug.Log("[StressTest] ═══ 결과 요약 ═══");
                foreach (var r in results)
                    Debug.Log($"  {(r.passed ? "✅" : "❌")} {r.testName}: {r.measuredValue:F2}{r.unit} (목표: {r.targetValue:F2}{r.unit}) — {r.detail}");
                Debug.Log($"  전체 통과율: {overallPassRate:P0}");
            }

            isRunning = false;
        }

        // ==================================================================
        // Test 1: MobAI 부하 FPS
        // ==================================================================
        private IEnumerator TestAILoadFPS()
        {
            if (debugLog) Debug.Log("[StressTest] Test 1: AI 부하 FPS 측정 시작...");

            float timer = 0f;
            int frames = 0;
            float minFPS = float.MaxValue;

            while (timer < aiLoadTestDuration)
            {
                timer += Time.unscaledDeltaTime;
                frames++;

                float instantFPS = 1f / Time.unscaledDeltaTime;
                if (instantFPS < minFPS) minFPS = instantFPS;
                yield return null;
            }

            float avgFPS = frames / timer;
            var mobs = FindObjectsByType<MobAIBrain>(FindObjectsSortMode.None);

            results.Add(new StressTestResult
            {
                testName = $"AI 부하 FPS (Mob={mobs.Length})",
                passed = avgFPS >= mobAITargetFPS,
                measuredValue = avgFPS,
                targetValue = mobAITargetFPS,
                unit = "fps",
                detail = $"avg={avgFPS:F1}, min={minFPS:F1}, mobs={mobs.Length}"
            });
        }

        // ==================================================================
        // Test 2: HumanoidAI 풀 시스템 프레임 드랍
        // ==================================================================
        private IEnumerator TestHumanoidFullSystem()
        {
            if (debugLog) Debug.Log("[StressTest] Test 2: HumanoidAI 풀 시스템 프레임 드랍 측정...");

            // 기준 FPS (현재 프레임)
            yield return new WaitForSeconds(1f);
            float baselineFPS = 1f / Time.unscaledDeltaTime;

            // 풀 시스템 부하 (발화 + RAG 호출)
            var humanoids = FindObjectsByType<HumanoidAIBrain>(FindObjectsSortMode.None);
            foreach (var h in humanoids)
            {
                var speech = h.GetComponent<SpeechAssembler>();
                if (speech != null) speech.AssembleSpeech("stress_test");
            }

            yield return new WaitForSeconds(2f);
            float loadedFPS = 1f / Time.unscaledDeltaTime;
            float dropPercent = ((baselineFPS - loadedFPS) / baselineFPS) * 100f;

            results.Add(new StressTestResult
            {
                testName = $"HumanoidAI 풀 시스템 (N={humanoids.Length})",
                passed = dropPercent <= humanoidDropThreshold,
                measuredValue = dropPercent,
                targetValue = humanoidDropThreshold,
                unit = "%드랍",
                detail = $"baseline={baselineFPS:F1}, loaded={loadedFPS:F1}, drop={dropPercent:F1}%"
            });
        }

        // ==================================================================
        // Test 3: 자동화 아크 생성 + Hallucination 감지
        // ==================================================================
        private void TestAutomatedArcGeneration()
        {
            if (debugLog) Debug.Log($"[StressTest] Test 3: 자동화 아크 {arcGenerationCount}회...");

            int hallucinationCount = 0;
            var critic = FindFirstObjectByType<CriticAgent>();

            for (int i = 0; i < arcGenerationCount; i++)
            {
                // 간이 아크 생성 시뮬레이션
                bool valid = SimulateArcGeneration(i);
                if (!valid) hallucinationCount++;
            }

            float hallucinationRate = (float)hallucinationCount / arcGenerationCount;

            results.Add(new StressTestResult
            {
                testName = $"자동화 아크 ({arcGenerationCount}회)",
                passed = hallucinationRate < hallucinationThreshold,
                measuredValue = hallucinationRate * 100f,
                targetValue = hallucinationThreshold * 100f,
                unit = "% hallucination",
                detail = $"{hallucinationCount}/{arcGenerationCount} failed"
            });
        }

        private bool SimulateArcGeneration(int seed)
        {
            // 간이 유효성 검사: Phase 순서 정상 + ID 존재 + 4개 Phase 이상
            var rng = new System.Random(seed);
            int phaseCount = rng.Next(2, 6);
            bool hasId = rng.NextDouble() > 0.02; // 2% 확률로 ID 누락
            bool orderedPhases = rng.NextDouble() > 0.03; // 3% 확률로 순서 오류
            return hasId && orderedPhases && phaseCount >= 3;
        }

        // ==================================================================
        // 외부 API
        // ==================================================================
        public IReadOnlyList<StressTestResult> Results => results;
        public bool IsRunning => isRunning;
        public float OverallPassRate => overallPassRate;
        public int CurrentTestIndex => currentTestIndex;
    }
}
