#if UNITY_EDITOR
using UnityEngine;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-E E.2] PreviewSDFHelpers — SDF 기본 함수 C# 포팅
    //
    // Shader CaveBiomeMath.hlsl의 핵심 SDF 함수를 C#으로 재현:
    //   - sdCapsule (Inigo Quilez)
    //   - sdSphere
    //   - smin (exponential smooth min, k = smoothing strength)
    //
    // byte-identical 보장: 이 파일은 신규, 기존 shader/코드 영향 없음.
    // ══════════════════════════════════════════════════════════════════════

    public static class PreviewSDFHelpers
    {
        /// <summary>
        /// Capsule SDF — Iñigo Quilez formulation.
        /// p: sample point, a/b: capsule endpoints, r: radius.
        /// 결과 &lt;= 0 이면 p가 capsule 내부.
        /// </summary>
        public static float sdCapsule(Vector3 p, Vector3 a, Vector3 b, float r)
        {
            Vector3 pa = p - a;
            Vector3 ba = b - a;
            float baba = Vector3.Dot(ba, ba);
            if (baba < 1e-6f) return Vector3.Magnitude(pa) - r;
            float h = Mathf.Clamp01(Vector3.Dot(pa, ba) / baba);
            return Vector3.Magnitude(pa - ba * h) - r;
        }

        /// <summary>Sphere SDF — center c, radius r.</summary>
        public static float sdSphere(Vector3 p, Vector3 c, float r)
        {
            return Vector3.Distance(p, c) - r;
        }

        /// <summary>
        /// Polynomial smooth minimum — Iñigo Quilez 공식.
        /// k = smoothing strength (큰 값 = 더 부드러운 blending).
        /// smin(a, b, k) ≈ min(a, b) at far, smooth near intersections.
        /// </summary>
        public static float smin(float a, float b, float k)
        {
            if (k <= 1e-6f) return Mathf.Min(a, b);
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return Mathf.Min(a, b) - h * h * k * 0.25f;
        }

        /// <summary>
        /// [Phase 4.5-E E.3 support] Curvature-modulated capsule.
        /// Edge의 curvatureAmp가 크면 capsule을 sine-wave로 변조.
        /// Shader EvaluateCurvedTunnel의 간소화 버전.
        /// </summary>
        public static float sdCurvedCapsule(Vector3 p, Vector3 a, Vector3 b,
            float r, float curvatureAmp)
        {
            if (curvatureAmp < 0.01f)
                return sdCapsule(p, a, b, r);

            // Project p onto edge line → parametric t [0, 1]
            Vector3 ba = b - a;
            float baba = Vector3.Dot(ba, ba);
            if (baba < 1e-6f) return sdSphere(p, a, r);

            Vector3 pa = p - a;
            float t = Mathf.Clamp01(Vector3.Dot(pa, ba) / baba);

            // Curvature offset: sine wave in perpendicular direction
            //   amplitude = min(curvatureAmp, r * 0.5) — 안전 clamp (과도 변형 방지)
            float safeAmp = Mathf.Min(curvatureAmp, r * 0.5f);
            Vector3 dir = ba / Mathf.Sqrt(baba);
            // Perpendicular direction (Y-up 기본)
            Vector3 perp = Vector3.Cross(dir, Vector3.up);
            if (perp.sqrMagnitude < 0.01f)
                perp = Vector3.Cross(dir, Vector3.right);
            perp.Normalize();

            float wave = Mathf.Sin(t * Mathf.PI) * safeAmp;
            Vector3 center = a + ba * t + perp * wave;
            return Vector3.Distance(p, center) - r;
        }
    }
}
#endif
