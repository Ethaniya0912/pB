// =============================================================================
// SubBiomeResolver.cs  |  pB-4 Project — Week 3
// Layer  : L3 Domain (지형)
// Namespace: TDA.PB4.Terrain
//
// 역할:
//   MetaScenarioGenerator가 생성한 노드별 환경 파라미터(humidity/temperature/elevation)를
//   SubBiomeSO 목록과 매칭하여 서브바이옴을 결정한다.
//   예: 흙동굴(DirtCave) 내에서 humidity>0.7이면 '버섯정원', <0.3이면 '건조토사'.
//
// 연계:
//   - MetaScenarioGenerator: 노드별 환경 파라미터 제공 (Week 3)
//   - CaveEcosystemManager: 결정된 서브바이옴에 따라 프롭/식생 배치 (Week 5)
//   - BiomeSDF_ProfileSO: 서브바이옴별 SDF 파라미터 미세 조정 (Week 5)
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Terrain
{
    /// <summary>
    /// 노드의 환경 파라미터. MetaScenarioGenerator가 생성.
    /// </summary>
    [Serializable]
    public struct EnvironmentParams
    {
        [Tooltip("습도 (0.0=극건조, 1.0=물에 잠김). " +
                 "높으면 버섯/이끼/수류 서브바이옴, 낮으면 건조/사막 서브바이옴")]
        [Range(0f, 1f)] public float humidity;

        [Tooltip("온도 (0.0=극저온, 1.0=용암 인접). " +
                 "높으면 용암/황천 서브바이옴, 낮으면 얼음/수정 서브바이옴")]
        [Range(0f, 1f)] public float temperature;

        [Tooltip("고도 (0.0=최저층, 1.0=최상층). " +
                 "높으면 절벽/협곡 상부, 낮으면 수렁/지하수")]
        [Range(0f, 1f)] public float elevation;
    }

    /// <summary>
    /// 서브바이옴 정의. 환경 조건 범위 + 적용될 시각적/게임적 특성.
    /// </summary>
    [Serializable]
    public class SubBiomeDefinition
    {
        [Tooltip("서브바이옴 이름. 예: MushroomGarden, DrySoil, Oasis, DryCrag")]
        public string subBiomeName;

        [Tooltip("부모 바이옴 타입. 이 바이옴 내에서만 이 서브바이옴이 출현 가능")]
        public TDA.PB4.Data.BiomeType parentBiome;

        [Header("━━━ 환경 조건 범위 ━━━━━━━━━━━━━━━━")]
        [Range(0f, 1f)] public float minHumidity = 0f;
        [Range(0f, 1f)] public float maxHumidity = 1f;
        [Range(0f, 1f)] public float minTemperature = 0f;
        [Range(0f, 1f)] public float maxTemperature = 1f;
        [Range(0f, 1f)] public float minElevation = 0f;
        [Range(0f, 1f)] public float maxElevation = 1f;

        [Header("━━━ 매칭 우선순위 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("조건이 겹치는 경우 높은 우선순위가 선택됨")]
        public int priority = 0;

        /// <summary>주어진 환경 파라미터가 이 서브바이옴의 조건 범위에 맞는지 검사.</summary>
        public bool Matches(EnvironmentParams env)
        {
            return env.humidity >= minHumidity && env.humidity <= maxHumidity
                && env.temperature >= minTemperature && env.temperature <= maxTemperature
                && env.elevation >= minElevation && env.elevation <= maxElevation;
        }
    }

    public class SubBiomeResolver : MonoBehaviour
    {
        [Header("━━━ 서브바이옴 정의 목록 ━━━━━━━━━━━━━━")]
        [Tooltip("서브바이옴 정의 목록. 비어있으면 Awake()에서 기본 6종이 자동 생성. " +
                 "각 항목은 부모 바이옴 + 환경 조건 범위 + 우선순위로 구성.")]
        [SerializeField] private List<SubBiomeDefinition> definitions = new List<SubBiomeDefinition>();

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("매칭 과정을 Console에 출력")]
        public bool debugLog = false;

        private void Awake()
        {
            if (definitions.Count == 0) GenerateDefaults();
        }

        private void GenerateDefaults()
        {
            // 흙동굴 서브바이옴
            definitions.Add(new SubBiomeDefinition {
                subBiomeName = "MushroomGarden", parentBiome = TDA.PB4.Data.BiomeType.DirtCave,
                minHumidity = 0.6f, maxHumidity = 1f, priority = 1
            });
            definitions.Add(new SubBiomeDefinition {
                subBiomeName = "DrySoil", parentBiome = TDA.PB4.Data.BiomeType.DirtCave,
                minHumidity = 0f, maxHumidity = 0.4f, priority = 1
            });
            // 협곡 서브바이옴
            definitions.Add(new SubBiomeDefinition {
                subBiomeName = "Oasis", parentBiome = TDA.PB4.Data.BiomeType.Canyon,
                minHumidity = 0.5f, maxHumidity = 1f, minElevation = 0f, maxElevation = 0.4f, priority = 2
            });
            definitions.Add(new SubBiomeDefinition {
                subBiomeName = "DryCrag", parentBiome = TDA.PB4.Data.BiomeType.Canyon,
                minHumidity = 0f, maxHumidity = 0.5f, minElevation = 0.4f, maxElevation = 1f, priority = 1
            });
            // 수정동굴 서브바이옴
            definitions.Add(new SubBiomeDefinition {
                subBiomeName = "GlowCrystal", parentBiome = TDA.PB4.Data.BiomeType.Crystal,
                minTemperature = 0f, maxTemperature = 0.4f, priority = 1
            });
            // 용암동굴 서브바이옴
            definitions.Add(new SubBiomeDefinition {
                subBiomeName = "MoltenFlow", parentBiome = TDA.PB4.Data.BiomeType.Lava,
                minTemperature = 0.7f, maxTemperature = 1f, priority = 1
            });
        }

        /// <summary>
        /// 주어진 환경 파라미터와 부모 바이옴에서 가장 적합한 서브바이옴을 결정.
        /// </summary>
        public string ResolveSubBiome(TDA.PB4.Data.BiomeType parentBiome, EnvironmentParams env)
        {
            SubBiomeDefinition best = null;

            foreach (var def in definitions)
            {
                if (def.parentBiome != parentBiome) continue;
                if (!def.Matches(env)) continue;
                if (best == null || def.priority > best.priority)
                    best = def;
            }

            string result = best != null ? best.subBiomeName : "Default";
            if (debugLog)
                Debug.Log($"[SubBiome] {parentBiome} + H={env.humidity:F2}/T={env.temperature:F2}/E={env.elevation:F2} \u2192 {result}");
            return result;
        }
    }
}
