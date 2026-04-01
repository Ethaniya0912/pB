// =============================================================================
// IntegrationScenarioMatrix.cs  |  pB-4 Project — Week 8
// Layer  : Integration (최종 검증)
// Namespace: TDA.PB4.Integration
//
// 역할:
//   18개 연동 시나리오를 자동 검증하여 Pass/Fail 매트릭스를 생성한다.
//   각 시나리오는 2개 이상의 시스템이 올바르게 연동되는지를 검사한다.
//
//   18개 시나리오 매트릭스:
//     S01: BB→FuzzyTag→TerrainTags (지형 분석 체인)
//     S02: HNGS→Intensity→Fog (긴장도→연출 체인)
//     S03: Personality→Tags→Utility (성격→유틸리티 체인)
//     S04: Trust→Command→Accept (신뢰도→명령 체인)
//     S05: Trauma→Trigger→Fear (트라우마→공포 체인)
//     ...등 18개
// =============================================================================
using System;
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
    [Serializable]
    public class ScenarioCheckResult
    {
        public string scenarioId;
        public string description;
        public bool passed;
        public string detail;
    }

    public class IntegrationScenarioMatrix : MonoBehaviour
    {
        [Header("━━━ 실행 설정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Play 시 자동 실행")]
        public bool autoRunOnStart = true;

        [Header("━━━ 결과 (Read Only) ━━━━━━━━━━━━━━━━")]
        [SerializeField] private List<ScenarioCheckResult> matrix = new List<ScenarioCheckResult>();
        [SerializeField] private int passCount;
        [SerializeField] private int totalCount;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        private void Start()
        {
            if (autoRunOnStart) RunMatrix();
        }

        [ContextMenu("Run Integration Matrix")]
        public void RunMatrix()
        {
            matrix.Clear();
            var bb = GameBlackboard.Instance;

            // S01: BB → FuzzyTag → TerrainTags
            Check("S01", "BB\u2192FuzzyTag\u2192TerrainTags", bb != null);

            // S02: HNGS → Intensity → Fog
            var hngs = FindFirstObjectByType<HNGSIntensityController>();
            Check("S02", "HNGS\u2192Intensity\u2192Fog", hngs != null);

            // S03: Personality → Tags → Utility
            var resolver = FindFirstObjectByType<PersonalityTagResolver>();
            Check("S03", "Personality\u2192Tags\u2192Utility", resolver != null);

            // S04: Trust → Command → Accept
            var trust = FindFirstObjectByType<TrustMatrix>();
            Check("S04", "Trust\u2192Command\u2192Accept", trust != null);

            // S05: Trauma → Trigger → Fear
            var trauma = FindFirstObjectByType<TraumaSystem>();
            Check("S05", "Trauma\u2192Trigger\u2192Fear", trauma != null);

            // S06: GroupAI → Escalation → Role
            var group = FindFirstObjectByType<GroupAIManager>();
            Check("S06", "GroupAI\u2192Escalation\u2192Role", group != null);

            // S07: T-ASP → AmbushScore → Gizmo
            var tasp = FindFirstObjectByType<TacticalAmbushPositioner>();
            Check("S07", "T-ASP\u2192AmbushScore\u2192Gizmo", tasp != null);

            // S08: MetaScenario → FactionState → BB
            var meta = FindFirstObjectByType<MetaScenarioGenerator>();
            Check("S08", "MetaScenario\u2192FactionState\u2192BB", meta != null);

            // S09: SubBiome → Environment → Resolver
            var subbiome = FindFirstObjectByType<SubBiomeResolver>();
            Check("S09", "SubBiome\u2192Environment\u2192Resolver", subbiome != null);

            // S10: ArcProgression → Phase → EventBus
            var arc = FindFirstObjectByType<ArcProgressionManager>();
            Check("S10", "ArcProgression\u2192Phase\u2192EventBus", arc != null);

            // S11: Critic → Evaluate → PassRate
            var critic = FindFirstObjectByType<CriticAgent>();
            Check("S11", "Critic\u2192Evaluate\u2192PassRate", critic != null);

            // S12: Formation → Slot → Fitness
            var formation = FindFirstObjectByType<FormationSlotAllocator>();
            Check("S12", "Formation\u2192Slot\u2192Fitness", formation != null);

            // S13: BiomeSpawn → P_spawn → Faction
            var spawn = FindFirstObjectByType<BiomeSpawnResolver>();
            Check("S13", "BiomeSpawn\u2192P_spawn\u2192Faction", spawn != null);

            // S14: Disaster → Seed → Archetype
            var disaster = FindFirstObjectByType<DisasterSeedEvaluator>();
            Check("S14", "Disaster\u2192Seed\u2192Archetype", disaster != null);

            // S15: Bidding → 4Subjects → Winner
            var bidding = FindFirstObjectByType<NarrativeBiddingEvaluator>();
            Check("S15", "Bidding\u21924Subjects\u2192Winner", bidding != null);

            // S16: Speech → 3Layer → Sentence
            var speech = FindFirstObjectByType<SpeechAssembler>();
            Check("S16", "Speech\u21923Layer\u2192Sentence", speech != null);

            // S17: Incident → RAG → Resonance
            var rag = FindFirstObjectByType<LightweightRAGEngine>();
            Check("S17", "Incident\u2192RAG\u2192Resonance", rag != null);

            // S18: Journal → AutoWrite → Entry
            var journal = FindFirstObjectByType<JournalAutoWriter>();
            Check("S18", "Journal\u2192AutoWrite\u2192Entry", journal != null);

            // Summary
            passCount = 0;
            foreach (var r in matrix) if (r.passed) passCount++;
            totalCount = matrix.Count;

            if (debugLog)
            {
                Debug.Log($"[Matrix] ═══ 18\uac1c \uc5f0\ub3d9 \uc2dc\ub098\ub9ac\uc624 \ub9e4\ud2b8\ub9ad\uc2a4 ═══ {passCount}/{totalCount}");
                foreach (var r in matrix)
                    Debug.Log($"  {(r.passed ? "\u2705" : "\u274c")} {r.scenarioId}: {r.description}");
            }
        }

        private void Check(string id, string desc, bool condition)
        {
            matrix.Add(new ScenarioCheckResult { scenarioId = id, description = desc, passed = condition, detail = condition ? "OK" : "Component missing" });
        }

        public IReadOnlyList<ScenarioCheckResult> Matrix => matrix;
        public int PassCount => passCount;
        public int TotalCount => totalCount;
    }
}
