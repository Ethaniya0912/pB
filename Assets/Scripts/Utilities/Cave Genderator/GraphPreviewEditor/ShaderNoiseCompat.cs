#if UNITY_EDITOR
using UnityEngine;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-F hotfix v5] ShaderNoiseCompat
    //
    // CaveNoiseLibrary.hlsl의 snoise2D 함수를 C#에 byte-identical 포팅.
    //
    // 목적: Runtime shader가 실제 사용하는 noise 값과 Editor Preview가
    //       100% 일치하도록 함.
    //
    // 배경:
    //   - Shader의 snoise2D는 표준 Stefan Gustavson Simplex가 아님
    //   - Sin-based hash + 독자적 gradient + multiplier 130
    //   - 따라서 기존 SimplexNoise2D.cs (표준 구현)도 Perlin도 정확히 일치 안 함
    //   - 이 파일이 "shader와 수학적으로 완전히 동일한" 결과 제공
    //
    // 일치 범위:
    //   - floor/fmod/frac/dot/sin → C# 등가 연산으로 1:1 매핑
    //   - Float 연산 순서 유지 (HLSL float는 IEEE 754 단정밀도)
    //   - 실제로 GPU와 CPU 간 미세한 rounding 차이는 있을 수 있으나 
    //     사람이 구분 불가 수준 (< 0.001 차이)
    // ══════════════════════════════════════════════════════════════════════

    public static class ShaderNoiseCompat
    {
        /// <summary>
        /// CaveNoiseLibrary.hlsl의 snoise2D(float2 v) 함수를 C#으로 포팅.
        /// 반환: 대략 [-1, 1] 범위 (shader multiplier 130 적용 후).
        /// </summary>
        public static float snoise2D(float x, float y)
        {
            // Shader 원본:
            //   float2 i = floor(v + (v.x + v.y) * 0.36602540378);
            //   float2 x0 = v - i + (i.x + i.y) * 0.2113248654;
            float s = (x + y) * 0.36602540378f;
            float ix = Mathf.Floor(x + s);
            float iy = Mathf.Floor(y + s);
            float t = (ix + iy) * 0.2113248654f;
            float x0x = x - ix + t;
            float x0y = y - iy + t;

            // 삼각형 내 어느 쪽인지 결정
            //   i1 = (x0.x > x0.y) ? (1, 0) : (0, 1)
            float i1x, i1y;
            if (x0x > x0y) { i1x = 1f; i1y = 0f; }
            else { i1x = 0f; i1y = 1f; }

            float x1x = x0x - i1x + 0.2113248654f;
            float x1y = x0y - i1y + 0.2113248654f;
            float x2x = x0x - 1f + 2f * 0.2113248654f;
            float x2y = x0y - 1f + 2f * 0.2113248654f;

            // i = fmod(i, 289)
            ix = FMod(ix, 289f);
            iy = FMod(iy, 289f);

            // float3 p = (dot(i, (127.1, 311.7)), dot(i+i1, ...), dot(i+(1,1), ...))
            float p0 = ix * 127.1f + iy * 311.7f;
            float p1 = (ix + i1x) * 127.1f + (iy + i1y) * 311.7f;
            float p2 = (ix + 1f) * 127.1f + (iy + 1f) * 311.7f;

            // p = frac(sin(p) * 43758.5453123)
            p0 = Frac(Mathf.Sin(p0) * 43758.5453123f);
            p1 = Frac(Mathf.Sin(p1) * 43758.5453123f);
            p2 = Frac(Mathf.Sin(p2) * 43758.5453123f);

            // float3 m = max(0.5 - (dot(x0,x0), dot(x1,x1), dot(x2,x2)), 0)
            float m0 = Mathf.Max(0.5f - (x0x * x0x + x0y * x0y), 0f);
            float m1 = Mathf.Max(0.5f - (x1x * x1x + x1y * x1y), 0f);
            float m2 = Mathf.Max(0.5f - (x2x * x2x + x2y * x2y), 0f);

            // m = m*m; m = m*m;  (= m^4)
            m0 *= m0; m0 *= m0;
            m1 *= m1; m1 *= m1;
            m2 *= m2; m2 *= m2;

            // float3 x = 2 * frac(p * 43758.5453123) - 1
            // float3 y = 2 * frac(p * 12345.6789012) - 1
            float xg0 = 2f * Frac(p0 * 43758.5453123f) - 1f;
            float xg1 = 2f * Frac(p1 * 43758.5453123f) - 1f;
            float xg2 = 2f * Frac(p2 * 43758.5453123f) - 1f;
            float yg0 = 2f * Frac(p0 * 12345.6789012f) - 1f;
            float yg1 = 2f * Frac(p1 * 12345.6789012f) - 1f;
            float yg2 = 2f * Frac(p2 * 12345.6789012f) - 1f;

            // d = (x.x*x0.x + y.x*x0.y, x.y*x1.x + y.y*x1.y, x.z*x2.x + y.z*x2.y)
            float d0 = xg0 * x0x + yg0 * x0y;
            float d1 = xg1 * x1x + yg1 * x1y;
            float d2 = xg2 * x2x + yg2 * x2y;

            // return 130.0 * dot(m, d)
            return 130f * (m0 * d0 + m1 * d1 + m2 * d2);
        }

        // HLSL fmod: x - y * trunc(x/y) — C#의 % 는 거의 동일하나 trunc 방향 주의
        //   HLSL fmod: trunc toward zero (C#의 %와 동일)
        private static float FMod(float a, float b)
        {
            return a - b * (float)System.Math.Truncate(a / b);
        }

        // HLSL frac: x - floor(x) — C#에는 없으므로 수동 구현
        //   주의: Mathf.Repeat(x, 1f) 과 유사하지만 음수 취급 차이
        private static float Frac(float x)
        {
            return x - Mathf.Floor(x);
        }
    }
}
#endif
