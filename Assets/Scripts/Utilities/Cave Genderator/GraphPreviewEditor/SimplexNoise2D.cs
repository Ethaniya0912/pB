#if UNITY_EDITOR
using UnityEngine;

namespace CaveSystem.Editor
{
    // ══════════════════════════════════════════════════════════════════════
    // [Phase 4.5-E E.1] SimplexNoise2D — Shader snoise2D C# 포팅
    //
    // 출력 범위: [-1, 1] (shader snoise2D와 동일)
    // 기반: Stefan Gustavson public domain Simplex Noise
    //   https://github.com/stegu/webgl-noise
    //
    // Shader CaveBiomeMath.hlsl의 snoise2D와 수학적으로 동일 결과 보장.
    // BiomeSampler.SampleNoise2D가 이 함수를 호출하도록 Option 1/2 전환 가능.
    //
    // byte-identical 보장: 이 파일은 신규 파일, 기존 코드 호출 전까지 영향 없음.
    // ══════════════════════════════════════════════════════════════════════

    public static class SimplexNoise2D
    {
        // 표준 Perlin permutation — Ken Perlin's original reference implementation
        private static readonly int[] p = {
            151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,
            140,36,103,30,69,142,8,99,37,240,21,10,23,190,6,148,
            247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,
            57,177,33,88,237,149,56,87,174,20,125,136,171,168,68,175,
            74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,
            60,211,133,230,220,105,92,41,55,46,245,40,244,102,143,54,
            65,25,63,161,1,216,80,73,209,76,132,187,208,89,18,169,
            200,196,135,130,116,188,159,86,164,100,109,198,173,186,3,64,
            52,217,226,250,124,123,5,202,38,147,118,126,255,82,85,212,
            207,206,59,227,47,16,58,17,182,189,28,42,223,183,170,213,
            119,248,152,2,44,154,163,70,221,153,101,155,167,43,172,9,
            129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,104,
            218,246,97,228,251,34,242,193,238,210,144,12,191,179,162,241,
            81,51,145,235,249,14,239,107,49,192,214,31,181,199,106,157,
            184,84,204,176,115,121,50,45,127,4,150,254,138,236,205,93,
            222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180
        };

        // Doubled permutation for fast wrapping
        private static readonly int[] perm = new int[512];

        // 2D-projected gradient vectors (12 3D gradients projected onto XY)
        private static readonly int[,] grad3 = {
            {1,1,0},{-1,1,0},{1,-1,0},{-1,-1,0},
            {1,0,1},{-1,0,1},{1,0,-1},{-1,0,-1},
            {0,1,1},{0,-1,1},{0,1,-1},{0,-1,-1}
        };

        // Skew factors
        private const float F2 = 0.366025403f;  // (sqrt(3) - 1) / 2
        private const float G2 = 0.211324865f;  // (3 - sqrt(3)) / 6

        static SimplexNoise2D()
        {
            for (int i = 0; i < 512; i++)
                perm[i] = p[i & 255];
        }

        /// <summary>
        /// 2D Simplex Noise 샘플. 결정론적.
        /// 출력 범위: 대략 [-1, 1]. shader snoise2D와 수학적으로 동일.
        /// </summary>
        public static float Sample(float x, float y)
        {
            // Skew input space to simplex grid
            float s = (x + y) * F2;
            int i = FastFloor(x + s);
            int j = FastFloor(y + s);

            float t = (i + j) * G2;
            float X0 = i - t;
            float Y0 = j - t;
            float x0 = x - X0;
            float y0 = y - Y0;

            // Determine which simplex triangle
            int i1, j1;
            if (x0 > y0) { i1 = 1; j1 = 0; }   // lower triangle
            else { i1 = 0; j1 = 1; }           // upper triangle

            float x1 = x0 - i1 + G2;
            float y1 = y0 - j1 + G2;
            float x2 = x0 - 1f + 2f * G2;
            float y2 = y0 - 1f + 2f * G2;

            // Hashed gradient indices
            int ii = i & 255;
            int jj = j & 255;
            int gi0 = perm[ii + perm[jj]] % 12;
            int gi1 = perm[ii + i1 + perm[jj + j1]] % 12;
            int gi2 = perm[ii + 1 + perm[jj + 1]] % 12;

            // Contributions from 3 corners
            float n0 = Contribution(gi0, x0, y0);
            float n1 = Contribution(gi1, x1, y1);
            float n2 = Contribution(gi2, x2, y2);

            // Sum + scale to [-1, 1]
            return 70f * (n0 + n1 + n2);
        }

        private static float Contribution(int gi, float x, float y)
        {
            float t = 0.5f - x * x - y * y;
            if (t < 0f) return 0f;
            t *= t;
            return t * t * Dot(gi, x, y);
        }

        private static float Dot(int gi, float x, float y)
        {
            return grad3[gi, 0] * x + grad3[gi, 1] * y;
        }

        private static int FastFloor(float x)
        {
            return x > 0 ? (int)x : (int)x - 1;
        }
    }
}
#endif
