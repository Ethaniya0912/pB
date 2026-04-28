// ─────────────────────────────────────────────────────────────────────
// pB-4 Week 3 D2 3차 — STEP 3-1: TerrainSamplerStub (v3)
// ─────────────────────────────────────────────────────────────────────
// 작성: 2026-04-28 (D2 3차 STEP 3, Stub 1/6, v3)
// 위치: Scripts/pB-4/week3_AI/Stubs/TerrainSamplerStub.cs
//
// ★★★ 자연 발현 패턴 (D1 DEBT-21 교훈 적용) ★★★
//
//   ❌ v1 패턴 (잘못): 고정값 반환 (slope=0, pathWidth=2.0)
//      → Real 전환 시 동작 차이 발생 위험
//      → forceTagOverride 같은 우회 메커니즘 유발
//
//   ✅ v3 패턴 (현재): 좌표 기반 자연 계산 (PerlinNoise + OverlapSphere)
//      → Real도 같은 인터페이스 + 같은 패턴 → 전환 시 동작 일치
//      → 강제 주입 일체 사용 안 함
//
// PB4Interfaces.cs 동결 시그니처 (TDA.PB4.Interfaces.Environment):
//   ITerrainSampler — 4 메서드 (SampleSlope/Density/Occlusion/PathWidth)
// ─────────────────────────────────────────────────────────────────────

using UnityEngine;
using System.Linq;
using TDA.PB4.Interfaces.Environment;  // ITerrainSampler 박제 위치

namespace TDA.PB4.AI.Stubs
{
    /// <summary>
    /// 지형 4 지표 자연 발현 Stub.
    /// PerlinNoise (slope/density) + Physics.OverlapSphere (pathWidth/occlusion).
    /// </summary>
    public class TerrainSamplerStub : ITerrainSampler
    {
        // ── 매직 넘버 금지: 자연 계산 상수 const 박제 ──────────────
        private const float NOISE_SCALE_SLOPE = 0.1f;
        private const float NOISE_SCALE_DENSITY = 0.05f;
        private const float DENSITY_BASE = 0.3f;
        private const float OVERLAP_RADIUS = 5f;
        private const float OPEN_WIDTH = 5.0f;
        private const float MIN_WIDTH = 0.5f;
        private const string TERRAIN_LAYER = "Terrain";

        /// <summary>
        /// 좌표 기반 PerlinNoise → 자연 기울기 (0~1).
        /// 같은 좌표는 같은 결과 (deterministic).
        /// </summary>
        public float SampleSlope(Vector3 worldPos)
        {
            return Mathf.Clamp01(
                Mathf.PerlinNoise(worldPos.x * NOISE_SCALE_SLOPE,
                                   worldPos.z * NOISE_SCALE_SLOPE)
            );
        }

        /// <summary>
        /// 좌표 기반 PerlinNoise → 자연 밀도 (0~1).
        /// base 0.3 + Perlin 변동.
        /// </summary>
        public float SampleDensity(Vector3 worldPos)
        {
            return Mathf.Clamp01(
                DENSITY_BASE +
                Mathf.PerlinNoise(worldPos.x * NOISE_SCALE_DENSITY, 0)
            );
        }

        /// <summary>
        /// 자연 발현: pathWidth가 좁을수록 시야 차폐 증가.
        /// width=0.5 → occlusion=0.9, width=5.0 → occlusion=0.0
        /// </summary>
        public float SampleOcclusion(Vector3 worldPos)
        {
            float w = SamplePathWidth(worldPos);
            return Mathf.Clamp01(1.0f - w / OPEN_WIDTH);
        }

        /// <summary>
        /// ★ 자연 발현 핵심 메서드 ★
        /// Physics.OverlapSphere로 주변 콜라이더 자연 측정.
        /// 좁은 통로 (Cylinder × 2개 간격 1.2m) 진입 시 width=1.2 자연 반환.
        /// 콜라이더 없는 개방 공간은 5.0m (최대값).
        /// </summary>
        public float SamplePathWidth(Vector3 worldPos)
        {
            int layerMask = LayerMask.GetMask(TERRAIN_LAYER);
            var hits = Physics.OverlapSphere(worldPos, OVERLAP_RADIUS, layerMask);

            if (hits.Length == 0) return OPEN_WIDTH;  // 콜라이더 없음 = 개방

            // 가장 가까운 벽까지의 거리 × 2 = 경로 폭 (양쪽 합)
            float minDist = hits.Min(h => Vector3.Distance(worldPos, h.ClosestPoint(worldPos)));
            return Mathf.Clamp(minDist * 2f, MIN_WIDTH, OPEN_WIDTH);
        }
    }
}
