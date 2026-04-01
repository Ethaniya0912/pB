// =============================================================================
// MetaScenarioGenerator.cs  |  pB-4 Project — Week 3
// Layer  : L3 Domain (시나리오)
// Namespace: TDA.PB4.Scenario
//
// 역할:
//   월드 초기 상태를 결정하는 메타 시나리오 엔진.
//   시드(seed)를 기반으로 팩션별 세계 상태(populationLevel, isDecimated, isDominant)와
//   노드별 환경 파라미터(humidity/temperature/elevation)를 생성.
//
//   이 결과가 SubBiomeResolver(서브바이옴 결정), BiomeSpawnResolver(팩션 스폰 확률),
//   ArcProgressionManager(아크 주제 선정)에 입력된다.
//
// 연계:
//   - GameBlackboard.activeFactionRegistry: 생성된 팩션 상태를 게시
//   - SubBiomeResolver: 노드별 환경 파라미터를 받아 서브바이옴 결정
//   - EventBus.OnFactionStateChanged: 팩션 상태 변경 시 발행
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Terrain;

namespace TDA.PB4.Scenario
{
    /// <summary>
    /// 팩션의 초기 세계 상태 설정.
    /// </summary>
    [Serializable]
    public class FactionInitConfig
    {
        [Tooltip("팩션 ID. GameBlackboard.activeFactionRegistry의 키로 사용.")]
        public string factionId;

        [Tooltip("기본 인구 수준 범위 — 최소. 시드에 따라 이 범위 내에서 결정됨.")]
        [Range(0f, 1f)] public float minPopulation = 0.3f;

        [Tooltip("기본 인구 수준 범위 — 최대.")]
        [Range(0f, 1f)] public float maxPopulation = 0.8f;

        [Tooltip("반 멸망(isDecimated) 판정 임계값. " +
                 "populationLevel이 이 값 미만이면 isDecimated=true.")]
        [Range(0f, 0.5f)] public float decimatedThreshold = 0.3f;

        [Tooltip("번성(isDominant) 판정 임계값. " +
                 "populationLevel이 이 값 이상이면 isDominant=true. " +
                 "isDominant인 팩션은 인접 바이옴까지 영향력 확장.")]
        [Range(0.5f, 1f)] public float dominantThreshold = 0.7f;
    }

    public class MetaScenarioGenerator : MonoBehaviour
    {
        [Header("━━━ 월드 시드 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("월드 생성 시드. 같은 시드 = 같은 월드 초기 상태. " +
                 "0이면 Time.time 기반 랜덤 시드를 자동 생성. " +
                 "CaveManager의 biomeSettings.seed와 동일하게 맞추면 지형/시나리오 동기화.")]
        public int worldSeed = 0;

        [Header("━━━ 팩션 초기 설정 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("각 팩션의 초기 세계 상태 설정. " +
                 "비어있으면 Awake()에서 고블린/오크/스켈레톤 3종이 자동 생성.")]
        [SerializeField] private List<FactionInitConfig> factionConfigs = new List<FactionInitConfig>();

        [Header("━━━ 노드 환경 파라미터 설정 ━━━━━━━━━━━")]
        [Tooltip("생성할 환경 노드 수. 0이면 CaveNodeGraphBuilder의 노드 수를 따름.")]
        public int nodeCount = 20;

        [Tooltip("습도 범위")]
        [Range(0f, 1f)] public float humidityMin = 0.1f;
        [Range(0f, 1f)] public float humidityMax = 0.9f;

        [Tooltip("온도 범위")]
        [Range(0f, 1f)] public float temperatureMin = 0.2f;
        [Range(0f, 1f)] public float temperatureMax = 0.8f;

        [Header("━━━ 생성 결과 (Read Only) ━━━━━━━━━━━━")]
        [Tooltip("생성된 팩션별 세계 상태")]
        [SerializeField] private List<string> generatedFactionSummary = new List<string>();

        [Tooltip("생성된 노드별 환경 파라미터")]
        [SerializeField] private List<EnvironmentParams> generatedNodeParams = new List<EnvironmentParams>();

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = false;

        private System.Random rng;

        private void Awake()
        {
            if (factionConfigs.Count == 0)
            {
                factionConfigs.Add(new FactionInitConfig { factionId = "goblin", minPopulation = 0.4f, maxPopulation = 0.8f });
                factionConfigs.Add(new FactionInitConfig { factionId = "orc", minPopulation = 0.3f, maxPopulation = 0.7f });
                factionConfigs.Add(new FactionInitConfig { factionId = "skeleton", minPopulation = 0.5f, maxPopulation = 0.9f });
            }
        }

        // ==================================================================
        // 핵심 API: 월드 초기 상태 생성
        // ==================================================================

        /// <summary>
        /// 시드를 기반으로 팩션 세계 상태 + 노드 환경 파라미터를 생성하고
        /// GameBlackboard에 게시한다.
        /// CaveManager.Start() 또는 로비에서 호출.
        /// </summary>
        public void GenerateWorldState()
        {
            if (worldSeed == 0) worldSeed = Mathf.Abs((int)(Time.realtimeSinceStartup * 1000));
            rng = new System.Random(worldSeed);

            GenerateFactionStates();
            GenerateNodeEnvironments();

            if (debugLog)
                Debug.Log($"[MetaScenario] 월드 생성 완료: seed={worldSeed}, 팩션={factionConfigs.Count}, 노드={nodeCount}");
        }

        // ==================================================================
        // 팩션 세계 상태 생성
        // ==================================================================
        private void GenerateFactionStates()
        {
            generatedFactionSummary.Clear();
            var bb = GameBlackboard.Instance;

            foreach (var config in factionConfigs)
            {
                float pop = Lerp(config.minPopulation, config.maxPopulation, (float)rng.NextDouble());
                bool decimated = pop < config.decimatedThreshold;
                bool dominant = pop >= config.dominantThreshold;

                var state = new FactionWorldState
                {
                    populationLevel = pop,
                    isDecimated = decimated,
                    isDominant = dominant
                };

                // 블랙보드 게시
                if (bb != null)
                    bb.SetFactionState(config.factionId, state);

                string summary = $"{config.factionId}: pop={pop:F2}, decimated={decimated}, dominant={dominant}";
                generatedFactionSummary.Add(summary);

                if (debugLog) Debug.Log($"[MetaScenario] {summary}");
            }
        }

        // ==================================================================
        // 노드 환경 파라미터 생성
        // ==================================================================
        private void GenerateNodeEnvironments()
        {
            generatedNodeParams.Clear();

            for (int i = 0; i < nodeCount; i++)
            {
                var env = new EnvironmentParams
                {
                    humidity = Lerp(humidityMin, humidityMax, (float)rng.NextDouble()),
                    temperature = Lerp(temperatureMin, temperatureMax, (float)rng.NextDouble()),
                    elevation = (float)rng.NextDouble()
                };
                generatedNodeParams.Add(env);
            }

            if (debugLog)
                Debug.Log($"[MetaScenario] 환경 파라미터 {generatedNodeParams.Count}개 생성");
        }

        private float Lerp(float a, float b, float t) => a + (b - a) * t;

        // ==================================================================
        // 외부 API
        // ==================================================================
        /// <summary>생성된 노드 환경 파라미터 목록.</summary>
        public IReadOnlyList<EnvironmentParams> NodeParams => generatedNodeParams;
        /// <summary>생성된 팩션 요약 목록.</summary>
        public IReadOnlyList<string> FactionSummary => generatedFactionSummary;
    }
}
