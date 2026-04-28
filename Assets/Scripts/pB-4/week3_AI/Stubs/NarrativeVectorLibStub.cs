// ─────────────────────────────────────────────────────────────────────
// pB-4 Week 3 D2 3차 — STEP 3-6: NarrativeVectorLibStub (v3)
// ─────────────────────────────────────────────────────────────────────
// 작성: 2026-04-28 (D2 3차 STEP 3, Stub 6/6, v3)
// 위치: Scripts/pB-4/week3_AI/Stubs/NarrativeVectorLibStub.cs
//
// 목적: 의미 사전 RAG 벡터 라이브러리 Stub.
//       Wk7+ 시나리오팀 Real 도착 (200종 사전 + 임베딩 데이터).
//       Wk3 D2-D6 시점에는 키 해시 → deterministic 벡터.
//
// PB4Interfaces.cs 동결 시그니처 (TDA.PB4.Interfaces.Core):
//   INarrativeVectorLib.GetVector(string elementName) → float[]
//   INarrativeVectorLib.CosineSimilarity(float[] a, float[] b) → float
//
// ★ HasKey 메서드 미박제 (Wk0 동결 시그니처에 없음).
// ─────────────────────────────────────────────────────────────────────

using UnityEngine;
using TDA.PB4.Interfaces.Core;  // INarrativeVectorLib 박제 위치 (Core)

namespace TDA.PB4.AI.Stubs
{
    /// <summary>
    /// 의미 벡터 라이브러리 Stub.
    /// 키 해시 기반 deterministic 768차원 벡터 + 코사인 유사도.
    /// </summary>
    public class NarrativeVectorLibStub : INarrativeVectorLib
    {
        // ── 매직 넘버 금지 const 박제 ───────────────────────────────
        private const int VECTOR_DIM = 768;
        private const float VECTOR_RANGE = 0.5f;       // -0.5 ~ +0.5
        private const float ZERO_MAGNITUDE_EPS = 1e-6f;

        /// <summary>
        /// 키 → 768차원 deterministic 벡터.
        /// 같은 키는 항상 같은 벡터 (재현 가능).
        /// 다른 키는 다른 벡터 (해시 기반 변동).
        /// </summary>
        public float[] GetVector(string elementName)
        {
            var vec = new float[VECTOR_DIM];
            if (string.IsNullOrEmpty(elementName)) return vec;  // 영벡터

            // 키 해시를 시드로 사용 → deterministic
            var random = new System.Random(elementName.GetHashCode());

            for (int i = 0; i < VECTOR_DIM; i++)
            {
                vec[i] = ((float)random.NextDouble() - 0.5f) * 2f * VECTOR_RANGE;
            }

            // L2 정규화 (코사인 유사도 정확도 향상)
            return Normalize(vec);
        }

        /// <summary>
        /// 코사인 유사도 (-1 ~ +1).
        /// 두 벡터의 차원이 다르면 0 반환.
        /// </summary>
        public float CosineSimilarity(float[] a, float[] b)
        {
            if (a == null || b == null) return 0f;
            if (a.Length != b.Length) return 0f;

            float dot = 0f;
            float magA = 0f;
            float magB = 0f;

            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }

            magA = Mathf.Sqrt(magA);
            magB = Mathf.Sqrt(magB);

            if (magA < ZERO_MAGNITUDE_EPS || magB < ZERO_MAGNITUDE_EPS) return 0f;
            return dot / (magA * magB);
        }

        /// <summary>
        /// L2 정규화: 벡터 크기를 1로 만듦.
        /// </summary>
        private float[] Normalize(float[] vec)
        {
            float sumSq = 0f;
            for (int i = 0; i < vec.Length; i++) sumSq += vec[i] * vec[i];
            float magnitude = Mathf.Sqrt(sumSq);
            if (magnitude < ZERO_MAGNITUDE_EPS) return vec;  // 영벡터 보호

            for (int i = 0; i < vec.Length; i++) vec[i] /= magnitude;
            return vec;
        }
    }
}
