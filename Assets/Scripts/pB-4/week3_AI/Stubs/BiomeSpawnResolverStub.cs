// ─────────────────────────────────────────────────────────────────────
// pB-4 Week 3 D2 3차 — STEP 3-5: BiomeSpawnResolverStub (v3)
// ─────────────────────────────────────────────────────────────────────
// 작성: 2026-04-28 (D2 3차 STEP 3, Stub 5/6, v3)
// 위치: Scripts/pB-4/week3_AI/Stubs/BiomeSpawnResolverStub.cs
//
// 목적: 팩션 스폰 확률 계산 Stub.
//       Wk5 완성 수식: P_spawn = BiomeAffinity × FactionPopulationLevel
//                                × AdjacencyBlend × ScenarioModifier
//       Wk3 D2-D4 시점에는 자연 변동만 박제.
//
// PB4Interfaces.cs 동결 시그니처 (TDA.PB4.Interfaces.Environment):
//   IBiomeSpawnResolver.CalculateSpawnProbability(string factionId, int biomeType, Vector3 worldPos)
// ─────────────────────────────────────────────────────────────────────

using UnityEngine;
using TDA.PB4.Interfaces.Environment;  // IBiomeSpawnResolver 박제 위치

namespace TDA.PB4.AI.Stubs
{
    /// <summary>
    /// 팩션 스폰 확률 Stub. 팩션 + biomeType + 좌표 기반 자연 변동.
    /// </summary>
    public class BiomeSpawnResolverStub : IBiomeSpawnResolver
    {
        // ── 매직 넘버 금지 const 박제 ───────────────────────────────
        private const float MIN_PROB = 0.2f;
        private const float MAX_PROB = 0.8f;
        private const float NOISE_SCALE = 0.07f;

        /// <summary>
        /// 팩션 ID 해시 + 좌표 → 자연 변동 확률 (0.2~0.8).
        /// 같은 (팩션, biomeType, 좌표) 조합은 같은 결과 (deterministic).
        /// </summary>
        public float CalculateSpawnProbability(
            string factionId,
            int biomeType,
            Vector3 worldPos)
        {
            // 팩션 ID 해시를 좌표 offset으로 변환 (팩션별 다른 결과)
            int hash = factionId?.GetHashCode() ?? 0;
            float factionOffset = (Mathf.Abs(hash) % 1000) * 0.001f;  // 0.0 ~ 0.999

            float noise = Mathf.PerlinNoise(
                worldPos.x * NOISE_SCALE + factionOffset,
                worldPos.z * NOISE_SCALE + biomeType * 0.1f
            );

            return Mathf.Lerp(MIN_PROB, MAX_PROB, noise);
        }
    }
}
