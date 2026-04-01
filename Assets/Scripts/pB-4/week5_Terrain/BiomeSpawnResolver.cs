// =============================================================================
// BiomeSpawnResolver.cs  |  pB-4 Project — Week 5
// Layer  : L3 Domain (지형)
// Namespace: TDA.PB4.Terrain
//
// 역할:
//   바이옴별 팩션 스폰 확률을 산출한다.
//   P_spawn(faction, biome) = BiomeAffinity × FactionPopulationLevel × AdjacencyBlend × ScenarioModifier
//
// 연계:
//   - BiomeAffinitySO: 바이옴별 팩션 친화도 데이터
//   - GameBlackboard.activeFactionRegistry: 팩션 인구 수준
//   - MetaScenarioGenerator: ScenarioModifier 소스
//   - CaveSpawnerManager: P_spawn 결과로 실제 스폰 결정
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Data;
using PB4BiomeType = TDA.PB4.Data.BiomeType;

namespace TDA.PB4.Terrain
{
    /// <summary>개별 팩션의 스폰 확률 계산 결과.</summary>
    [Serializable]
    public class SpawnProbabilityResult
    {
        public string factionId;
        public float biomeAffinity;
        public float populationLevel;
        public float adjacencyBlend;
        public float scenarioModifier;
        public float finalProbability;
    }

    public class BiomeSpawnResolver : MonoBehaviour
    {
        [Header("━━━ 바이옴 친화도 SO ━━━━━━━━━━━━━━━━")]
        [Tooltip("바이옴별 팩션 친화도 데이터 목록. " +
                 "각 SO에는 biomeType, factionAffinities[], adjacencyBlendRadius가 정의되어 있다.")]
        [SerializeField] private List<BiomeAffinitySO> affinityProfiles = new List<BiomeAffinitySO>();

        [Header("━━━ 현재 바이옴 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("현재 플레이어가 위치한 바이옴 유형. " +
                 "SubBiomeResolver나 CaveManager에서 자동 설정될 예정. " +
                 "프로토타입에서는 Inspector 드롭다운으로 수동 선택.")]
        public PB4BiomeType currentBiome = PB4BiomeType.DirtCave;

        [Header("━━━ 시나리오 보정 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("시나리오 보정 계수 (0.0~2.0). " +
                 "1.0=보정 없음. >1.0=현재 아크가 해당 팩션 강화. <1.0=약화. " +
                 "ArcProgressionManager가 Phase에 따라 자동 설정할 예정. " +
                 "프로토타입에서는 슬라이더로 수동 조절.")]
        [Range(0f, 2f)]
        public float scenarioModifier = 1.0f;

        [Header("━━━ 인접 블렌드 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("인접 바이옴 영향력 반경 (미터). " +
                 "이 반경 내의 인접 바이옴이 스폰 확률에 영향. " +
                 "50 = 50m 내 다른 바이옴의 팩션도 약하게 출현 가능.")]
        [Range(10f, 100f)]
        public float blendRadius = 50f;

        [Tooltip("인접 바이옴의 영향력 가중치 (0~1). " +
                 "0.3 = 인접 바이옴이 30%만 영향.")]
        [Range(0f, 1f)]
        public float adjacencyWeight = 0.3f;

        [Header("━━━ 계산 결과 (Read Only) ━━━━━━━━━━━━")]
        [SerializeField] private List<SpawnProbabilityResult> lastResults = new List<SpawnProbabilityResult>();

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = false;

        // ==================================================================
        // 핵심 API
        // ==================================================================

        /// <summary>
        /// 현재 바이옴에서 모든 팩션의 스폰 확률을 산출.
        /// </summary>
        public List<SpawnProbabilityResult> CalculateSpawnProbabilities()
        {
            lastResults.Clear();
            var bb = GameBlackboard.Instance;

            // 현재 바이옴의 친화도 프로파일 찾기
            BiomeAffinitySO profile = null;
            foreach (var p in affinityProfiles)
                if (p != null && p.biomeType == currentBiome) { profile = p; break; }

            if (profile == null)
            {
                if (debugLog) Debug.Log($"[BiomeSpawn] {currentBiome}의 AffinitySO를 찾을 수 없음");
                return lastResults;
            }

            foreach (var fa in profile.factionAffinities)
            {
                float popLevel = 0.5f; // 기본값
                if (bb != null && bb.ActiveFactionRegistry != null &&
                    bb.ActiveFactionRegistry.TryGetValue(fa.factionId, out var state))
                    popLevel = state.populationLevel;

                float adjBlend = 1.0f - (adjacencyWeight * 0.5f); // 간이 인접 보정

                float prob = fa.baseAffinity * popLevel * adjBlend * scenarioModifier;
                prob = Mathf.Clamp01(prob);

                var result = new SpawnProbabilityResult
                {
                    factionId = fa.factionId,
                    biomeAffinity = fa.baseAffinity,
                    populationLevel = popLevel,
                    adjacencyBlend = adjBlend,
                    scenarioModifier = scenarioModifier,
                    finalProbability = prob
                };
                lastResults.Add(result);

                if (debugLog)
                    Debug.Log($"[BiomeSpawn] {fa.factionId}@{currentBiome}: " +
                              $"affinity={fa.baseAffinity:F2} × pop={popLevel:F2} × adj={adjBlend:F2} × scn={scenarioModifier:F2} = {prob:F3}");
            }

            return lastResults;
        }

        public IReadOnlyList<SpawnProbabilityResult> LastResults => lastResults;
    }
}