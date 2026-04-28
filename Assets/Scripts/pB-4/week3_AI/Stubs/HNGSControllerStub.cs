// ─────────────────────────────────────────────────────────────────────
// pB-4 Week 3 D2 3차 — STEP 3-4: HNGSControllerStub (v3)
// ─────────────────────────────────────────────────────────────────────
// 작성: 2026-04-28 (D2 3차 STEP 3, Stub 4/6, v3)
// 위치: Scripts/pB-4/week3_AI/Stubs/HNGSControllerStub.cs
//
// 목적: HNGS (Heightened Narrative Generation System) 강도 측정 폴백.
//       Wk5+ 시나리오 본격 활성화 전까지 Stub.
//
// PB4Interfaces.cs 동결 시그니처 (TDA.PB4.Interfaces.Environment):
//   IHNGSController.GetIntensityAtNode(int nodeIndex)         ← int!
//   IHNGSController.RequestWidthModulation(int edgeIndex, float widthFactor)
//
// ★ 자연 발현: nodeIndex 해시 → 0.3 ~ 0.7 변동 (deterministic).
// ─────────────────────────────────────────────────────────────────────

using UnityEngine;
using TDA.PB4.Interfaces.Environment;  // IHNGSController 박제 위치

namespace TDA.PB4.AI.Stubs
{
    /// <summary>
    /// HNGS 강도 측정 Stub. nodeIndex 기반 자연 변동.
    /// </summary>
    public class HNGSControllerStub : IHNGSController
    {
        // ── 매직 넘버 금지 const 박제 ───────────────────────────────
        private const float NOISE_SCALE = 0.08f;
        private const float MIN_INTENSITY = 0.3f;
        private const float MAX_INTENSITY = 0.7f;

        /// <summary>
        /// nodeIndex 기반 PerlinNoise → 자연 강도 (0.3~0.7).
        /// 같은 nodeIndex는 같은 결과 (deterministic).
        /// </summary>
        public float GetIntensityAtNode(int nodeIndex)
        {
            // nodeIndex를 PerlinNoise 입력으로 변환 (deterministic)
            float noise = Mathf.PerlinNoise(
                nodeIndex * NOISE_SCALE,
                nodeIndex * NOISE_SCALE * 1.3f
            );
            return Mathf.Lerp(MIN_INTENSITY, MAX_INTENSITY, noise);
        }

        /// <summary>
        /// 폭 변조 요청 박제 (Wk5+ 본격 활성화 시 사용).
        /// 현재 Stub은 noop (호출만 받음).
        /// </summary>
        public void RequestWidthModulation(int edgeIndex, float widthFactor)
        {
            // Wk3 시점: noop. Wk5+ Real 도착 시 실제 폭 변조 처리.
        }
    }
}
