// ─────────────────────────────────────────────────────────────────────
// pB-4 Week 3 D2 3차 — STEP 3-2: ContextAnalyzerStub (v3)
// ─────────────────────────────────────────────────────────────────────
// 작성: 2026-04-28 (D2 3차 STEP 3, Stub 2/6, v3)
// 위치: Scripts/pB-4/week3_AI/Stubs/ContextAnalyzerStub.cs
//
// ★★★ 자연 발현 핵심 ★★★
//   ❌ forceTagOverride = "Narrow" 같은 강제 주입 사용 안 함
//   ✅ TerrainSamplerStub.SamplePathWidth 호출 후 width<1.5 시
//      자동으로 "Narrow" 태그 자연 반환
//
// PB4Interfaces.cs 동결 시그니처 (TDA.PB4.Interfaces.Environment):
//   IContextAnalyzer.AnalyzeTerrain(Vector3) → List<string>
//
// 5종 태그: Narrow / Open / Dense / Sparse / HighGround
// ─────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Interfaces.Environment;  // ITerrainSampler + IContextAnalyzer 박제 위치

namespace TDA.PB4.AI.Stubs
{
    /// <summary>
    /// 지형 태그 5종 자연 발현 Stub.
    /// SamplePathWidth/SampleDensity 결과 → 태그 자연 결정.
    /// </summary>
    public class ContextAnalyzerStub : IContextAnalyzer
    {
        // ── 매직 넘버 금지: 임계값 const 박제 ──────────────────────
        private const float NARROW_THRESHOLD = 1.5f;     // m (폭 < 1.5 = Narrow)
        private const float OPEN_THRESHOLD = 4.0f;       // m (폭 > 4.0 = Open)
        private const float DENSE_THRESHOLD = 0.7f;      // 0~1 (밀도 > 0.7 = Dense)
        private const float SPARSE_THRESHOLD = 0.3f;     // 0~1 (밀도 < 0.3 = Sparse)
        private const float HIGH_GROUND_DELTA = 1.5f;    // m (주변 평균 + 1.5m = High)
        private const float SURROUNDING_RADIUS = 5f;     // m (주변 평균 계산 반경)
        private const float RAY_HEIGHT = 100f;
        private const float RAY_DISTANCE = 200f;

        // ── 태그 const 박제 (오타 방지) ─────────────────────────────
        private const string TAG_NARROW = "Narrow";
        private const string TAG_OPEN = "Open";
        private const string TAG_DENSE = "Dense";
        private const string TAG_SPARSE = "Sparse";
        private const string TAG_HIGH_GROUND = "HighGround";

        private readonly ITerrainSampler _terrain;

        /// <summary>
        /// 생성자 주입: TerrainSampler 의존 (자체 width 계산).
        /// Bootstrapper에서 같은 TerrainSamplerStub 인스턴스 전달.
        /// </summary>
        public ContextAnalyzerStub(ITerrainSampler terrain)
        {
            _terrain = terrain;
        }

        /// <summary>
        /// ★ 자연 발현 메서드 ★
        /// width / density / 고도 측정 후 자연 태그 결정.
        /// 강제 주입 일체 사용 안 함 (DEBT-21 교훈).
        /// </summary>
        public List<string> AnalyzeTerrain(Vector3 worldPos)
        {
            var tags = new List<string>();

            // ① pathWidth 기반 (Narrow / Open) ────────────────────
            float width = _terrain.SamplePathWidth(worldPos);
            if (width < NARROW_THRESHOLD) tags.Add(TAG_NARROW);
            else if (width > OPEN_THRESHOLD) tags.Add(TAG_OPEN);

            // ② density 기반 (Dense / Sparse) ─────────────────────
            float density = _terrain.SampleDensity(worldPos);
            if (density > DENSE_THRESHOLD) tags.Add(TAG_DENSE);
            else if (density < SPARSE_THRESHOLD) tags.Add(TAG_SPARSE);

            // ③ 고도 기반 (HighGround) ────────────────────────────
            if (IsHighGround(worldPos)) tags.Add(TAG_HIGH_GROUND);

            return tags;
        }

        /// <summary>
        /// 주변 평균 좌표보다 1.5m+ 높으면 HighGround.
        /// 5m 반경 4 방향 raycast로 평균 고도 계산.
        /// </summary>
        private bool IsHighGround(Vector3 worldPos)
        {
            Vector3[] dirs = {
                Vector3.forward, Vector3.back,
                Vector3.left, Vector3.right
            };

            float sumY = 0f;
            int count = 0;

            foreach (var dir in dirs)
            {
                Vector3 sample = worldPos + dir * SURROUNDING_RADIUS;
                if (Physics.Raycast(sample + Vector3.up * RAY_HEIGHT, Vector3.down,
                                    out RaycastHit hit, RAY_DISTANCE))
                {
                    sumY += hit.point.y;
                    count++;
                }
            }

            if (count == 0) return false;
            float avgY = sumY / count;
            return worldPos.y > avgY + HIGH_GROUND_DELTA;
        }
    }
}
