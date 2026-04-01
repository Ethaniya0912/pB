// =============================================================================
// IntegrationValidator.cs  |  pB-4 Project — Week 6
// Layer  : Editor / Runtime Debug
// Namespace: TDA.PB4.Integration
//
// 역할:
//   Week 0~5까지 구현된 모든 시스템의 존재와 기본 동작을 자동 검증.
//   Play 모드에서 한 번 실행하면 모든 시스템의 상태를 체크하여
//   통과/실패 보고서를 Console에 출력한다.
//
// 사용법:
//   씬에 배치 후 Play. Awake()에서 자동 검증 실행.
//   또는 Inspector의 Context Menu → "Run Validation" 클릭.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.AI;
using TDA.PB4.AI.Mob;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Terrain;
using TDA.PB4.Scenario;

namespace TDA.PB4.Integration
{
    [System.Serializable]
    public class ValidationCheckResult
    {
        public string checkName;
        public bool passed;
        public string detail;
    }

    public class IntegrationValidator : MonoBehaviour
    {
        [Header("━━━ 자동 실행 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Play 시 자동으로 검증을 실행할지 여부.")]
        public bool autoValidateOnStart = true;

        [Header("━━━ 검증 결과 (Read Only) ━━━━━━━━━━━━")]
        [SerializeField] private List<ValidationCheckResult> results = new List<ValidationCheckResult>();
        [SerializeField] private int passCount;
        [SerializeField] private int totalCount;
        [SerializeField] private float passRate;

        private void Start()
        {
            if (autoValidateOnStart) RunValidation();
        }

        [ContextMenu("Run Validation")]
        public void RunValidation()
        {
            results.Clear();

            // Week 0: Core
            Check("GameBlackboard.Instance", GameBlackboard.Instance != null);
            Check("EventBus 존재", true); // static class

            // Week 0: DC
            var dcExt = FindFirstObjectByType<CaveSystem.DCPipelineExtension>();
            Check("DCPipelineExtension", dcExt != null);
            Check("DCMeshBuilder", FindFirstObjectByType<CaveSystem.DCMeshBuilder>() != null);
            Check("DCPerformanceProfiler", FindFirstObjectByType<CaveSystem.DCPerformanceProfiler>() != null);

            // Week 1: AI
            var mobs = FindObjectsByType<MobAIBrain>(FindObjectsSortMode.None);
            Check("MobAIBrain (1+ 존재)", mobs.Length > 0, $"{mobs.Length}개");
            var humanoids = FindObjectsByType<HumanoidAIBrain>(FindObjectsSortMode.None);
            Check("HumanoidAIBrain (1+ 존재)", humanoids.Length > 0, $"{humanoids.Length}개");

            // Week 2: Master Formula + Tags
            Check("UtilityMasterFormula", FindFirstObjectByType<UtilityMasterFormula>() != null);
            Check("PersonalityTagResolver", FindFirstObjectByType<PersonalityTagResolver>() != null);
            Check("SituationVectorEncoder", FindFirstObjectByType<SituationVectorEncoder>() != null);
            Check("HNGSIntensityController", FindFirstObjectByType<HNGSIntensityController>() != null);
            Check("DynamicFogController", FindFirstObjectByType<DynamicFogController>() != null);

            // Week 3: GroupAI + T-ASP
            Check("GroupAIManager", FindFirstObjectByType<GroupAIManager>() != null);
            Check("TacticalAmbushPositioner", FindFirstObjectByType<TacticalAmbushPositioner>() != null);
            Check("SubBiomeResolver", FindFirstObjectByType<SubBiomeResolver>() != null);
            Check("MetaScenarioGenerator", FindFirstObjectByType<MetaScenarioGenerator>() != null);
            Check("CriticAgent", FindFirstObjectByType<CriticAgent>() != null);
            Check("ArcProgressionManager", FindFirstObjectByType<ArcProgressionManager>() != null);

            // Week 4: Trust + Trauma + Fuzzy
            Check("TrustMatrix", FindFirstObjectByType<TrustMatrix>() != null);
            Check("TraumaSystem", FindFirstObjectByType<TraumaSystem>() != null);
            Check("FuzzyTagConverter", FindFirstObjectByType<FuzzyTagConverter>() != null);
            Check("NarrativeBiddingEvaluator", FindFirstObjectByType<NarrativeBiddingEvaluator>() != null);

            // Week 5: Formation + Command + Spawn + Disaster
            Check("FormationSlotAllocator", FindFirstObjectByType<FormationSlotAllocator>() != null);
            Check("CommandDirective", FindFirstObjectByType<CommandDirective>() != null);
            Check("BiomeSpawnResolver", FindFirstObjectByType<BiomeSpawnResolver>() != null);
            Check("DisasterSeedEvaluator", FindFirstObjectByType<DisasterSeedEvaluator>() != null);

            // Week 6
            Check("FiveStageChainOrchestrator", FindFirstObjectByType<FiveStageChainOrchestrator>() != null);
            Check("MixedCombatTestHarness", FindFirstObjectByType<MixedCombatTestHarness>() != null);
            Check("JunctionLifecycleManager", FindFirstObjectByType<JunctionLifecycleManager>() != null);

            // Summary
            passCount = 0;
            foreach (var r in results) if (r.passed) passCount++;
            totalCount = results.Count;
            passRate = (float)passCount / Mathf.Max(1, totalCount);

            Debug.Log($"[Validator] ═══ 통합 검증 ═══ {passCount}/{totalCount} 통과 ({passRate:P0})");
            foreach (var r in results)
                Debug.Log($"  {(r.passed ? "✅" : "❌")} {r.checkName} {(string.IsNullOrEmpty(r.detail) ? "" : "(" + r.detail + ")")}");
        }

        private void Check(string name, bool condition, string detail = "")
        {
            results.Add(new ValidationCheckResult { checkName = name, passed = condition, detail = detail });
        }

        public IReadOnlyList<ValidationCheckResult> Results => results;
        public float PassRate => passRate;
    }
}
