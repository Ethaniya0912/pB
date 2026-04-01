// =============================================================================
// MixedCombatTestHarness.cs  |  pB-4 Project — Week 6
// Layer  : L2 Router (통합)
// Namespace: TDA.PB4.Integration
//
// 역할:
//   MobAI(적) + HumanoidAI(아군) 혼합 전투 시나리오를 생성하고
//   FPS를 모니터링하여 성능 마일스톤(60fps)을 검증한다.
//
//   Week 6 마일스톤: "MobAI 10마리+HumanoidAI 2명 혼합 60fps 이상"
//
// 사용법:
//   씬에 빈 오브젝트로 배치 후 Play. Inspector에서 모니터링.
//   실제 전투 프리팹이 없으므로 기존 테스트 오브젝트(PB4_Goblin_Test 등)를
//   참조하여 UpdateDecision() 루프 부하만 측정한다.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI.Mob;
using TDA.PB4.AI.Humanoid;

namespace TDA.PB4.Integration
{
    public class MixedCombatTestHarness : MonoBehaviour
    {
        [Header("━━━ 테스트 대상 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("테스트에 참여하는 MobAIBrain 목록. " +
                 "10마리 목표. Inspector에서 PB4_Goblin_Test 등을 드래그. " +
                 "같은 오브젝트를 여러 번 드래그하면 중복 카운트 (부하 테스트용).")]
        [SerializeField] private List<MobAIBrain> mobBrains = new List<MobAIBrain>();

        [Tooltip("테스트에 참여하는 HumanoidAIBrain 목록. " +
                 "2명 목표. PB4_HumanoidAI_Test 드래그.")]
        [SerializeField] private List<HumanoidAIBrain> humanoidBrains = new List<HumanoidAIBrain>();

        [Header("━━━ FPS 모니터링 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("FPS 측정 간격 (초). 1.0 = 매초 FPS 계산.")]
        [Range(0.5f, 3f)]
        public float fpsUpdateInterval = 1.0f;

        [Tooltip("마일스톤 FPS 하한. 이 값 미만이면 FAIL.")]
        public float targetFPS = 60f;

        [Header("━━━ 모니터링 결과 (Read Only) ━━━━━━━━")]
        [Tooltip("현재 FPS")]
        [SerializeField] private float currentFPS;

        [Tooltip("최저 FPS (테스트 시작 이후)")]
        [SerializeField] private float minFPS = float.MaxValue;

        [Tooltip("평균 FPS")]
        [SerializeField] private float avgFPS;

        [Tooltip("FPS 마일스톤 통과 여부")]
        [SerializeField] private bool fpsMilestonePass = true;

        [Tooltip("Mob 수 (Read Only)")]
        [SerializeField] private int mobCount;

        [Tooltip("Humanoid 수 (Read Only)")]
        [SerializeField] private int humanoidCount;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        private float fpsTimer;
        private int frameCount;
        private float fpsAccumulator;
        private int fpsSamples;

        private void Start()
        {
            mobCount = mobBrains.Count;
            humanoidCount = humanoidBrains.Count;
            if (debugLog)
                Debug.Log($"[MixedCombat] 테스트 시작: Mob={mobCount}, Humanoid={humanoidCount}, targetFPS={targetFPS}");
        }

        private void Update()
        {
            // FPS 측정
            frameCount++;
            fpsTimer += Time.unscaledDeltaTime;

            if (fpsTimer >= fpsUpdateInterval)
            {
                currentFPS = frameCount / fpsTimer;
                frameCount = 0;
                fpsTimer = 0;

                if (currentFPS < minFPS) minFPS = currentFPS;

                fpsAccumulator += currentFPS;
                fpsSamples++;
                avgFPS = fpsAccumulator / fpsSamples;

                if (currentFPS < targetFPS)
                {
                    fpsMilestonePass = false;
                    if (debugLog)
                        Debug.LogWarning($"[MixedCombat] ⚠️ FPS 하락: {currentFPS:F1} < {targetFPS} (min={minFPS:F1}, avg={avgFPS:F1})");
                }
            }
        }

        /// <summary>성능 요약을 Console에 출력.</summary>
        [ContextMenu("Print Performance Summary")]
        public void PrintSummary()
        {
            Debug.Log($"[MixedCombat] ═══ 성능 요약 ═══\n" +
                      $"  Mob={mobCount}, Humanoid={humanoidCount}\n" +
                      $"  현재 FPS: {currentFPS:F1}\n" +
                      $"  최저 FPS: {minFPS:F1}\n" +
                      $"  평균 FPS: {avgFPS:F1}\n" +
                      $"  마일스톤 ({targetFPS}fps): {(fpsMilestonePass ? "✅ PASS" : "❌ FAIL")}");
        }

        public float CurrentFPS => currentFPS;
        public float MinFPS => minFPS;
        public float AvgFPS => avgFPS;
        public bool FPSMilestonePass => fpsMilestonePass;
    }
}
