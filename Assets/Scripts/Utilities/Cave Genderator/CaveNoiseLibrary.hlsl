#ifndef CAVE_NOISE_LIBRARY_INCLUDED
#define CAVE_NOISE_LIBRARY_INCLUDED

// ===============================================================================
// [Part 4] 순수 수학 & 노이즈 라이브러리 (Pure Math Functions)
// 외부 변수 의존성 제로(Zero)를 보장하여 재사용성을 극대화합니다.
// ===============================================================================

// ----------------------------------------------------
// 1. 고속 의사 난수 생성기 (Hash Functions)
// ----------------------------------------------------
float hash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float2 hash2D(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453123);
}

// ----------------------------------------------------
// 2. 3D & 2D Simplex Noise (방향 아티팩트 감소 노이즈)
// ----------------------------------------------------
float snoise(float3 x)
{
    float3 p = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f); // Smoothstep 보간
    float n = p.x + p.y * 57.0 + 113.0 * p.z;
    return lerp(lerp(lerp(hash(n), hash(n + 1.0), f.x),
                   lerp(hash(n + 57.0), hash(n + 58.0), f.x), f.y),
               lerp(lerp(hash(n + 113.0), hash(n + 114.0), f.x),
                   lerp(hash(n + 170.0), hash(n + 171.0), f.x), f.y), f.z);
}

float snoise2D(float2 v)
{
    float2 i = floor(v + (v.x + v.y) * 0.36602540378);
    float2 x0 = v - i + (i.x + i.y) * 0.2113248654;
    float2 i1 = (x0.x > x0.y) ? float2(1.0, 0.0) : float2(0.0, 1.0);
    float2 x1 = x0 - i1 + 0.2113248654;
    float2 x2 = x0 - 1.0 + 2.0 * 0.2113248654;

    i = float2(fmod(i.x, 289.0), fmod(i.y, 289.0));
    float3 p = float3(
        dot(i, float2(127.1, 311.7)),
        dot(i + i1, float2(127.1, 311.7)),
        dot(i + float2(1.0, 1.0), float2(127.1, 311.7))
    );
    p = frac(sin(p) * 43758.5453123);
    float3 m = max(0.5 - float3(dot(x0, x0), dot(x1, x1), dot(x2, x2)), 0.0);
    m = m * m;
    m = m * m;
    float3 x = 2.0 * frac(p * 43758.5453123) - 1.0;
    float3 y = 2.0 * frac(p * 12345.6789012) - 1.0;
    float3 d = float3(x.x * x0.x + y.x * x0.y, x.y * x1.x + y.y * x1.y, x.z * x2.x + y.z * x2.y);
    return 130.0 * dot(m, d);
}

// ----------------------------------------------------
// 3. 프랙탈 브라운 운동 (fBm - Fractal Brownian Motion)
// ----------------------------------------------------
float fBm(float3 x, int octaves, float lacunarity, float gain)
{
    float sum = 0.0;
    float amp = 1.0;
    for (int i = 0; i < octaves; i++)
    {
        sum += snoise(x) * amp;
        x *= lacunarity;
        amp *= gain;
    }
    return sum;
}

// ----------------------------------------------------
// 4. 셀룰러 보로노이 노이즈 (Voronoi 2D)
// ----------------------------------------------------
void Voronoi2D(float2 x, out float f1, out float f2)
{
    float2 n = floor(x);
    float2 f = frac(x);

    f1 = 8.0;
    f2 = 8.0;

    for (int j = -1; j <= 1; j++)
    {
        for (int i = -1; i <= 1; i++)
        {
            float2 g = float2(float(i), float(j));
            float2 o = hash2D(n + g);
            float2 r = g + o - f;
            
            // 유클리디안(Euclidean) 거리 제곱
            float d = dot(r, r);

            if (d < f1)
            {
                f2 = f1;
                f1 = d;
            }
            else if (d < f2)
            {
                f2 = d;
            }
        }
    }
    
    // 최종 거리값 반환
    f1 = sqrt(f1);
    f2 = sqrt(f2);
}

#endif // CAVE_NOISE_LIBRARY_INCLUDED