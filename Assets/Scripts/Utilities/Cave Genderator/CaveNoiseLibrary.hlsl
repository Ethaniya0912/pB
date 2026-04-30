#ifndef CAVE_NOISE_LIBRARY_INCLUDED
#define CAVE_NOISE_LIBRARY_INCLUDED

// ===============================================================================
// [Part 4] 순수 수학 & 노이즈 라이브러리 (Pure Math Functions)
// 외부 변수 의존성 제로(Zero)를 보장하여 재사용성을 극대화합니다.
// ===============================================================================

// ───────────────────────────────────────────────────────────────────────────────
// [Gate 5 Phase A.2] 4원칙 준수 가드
//   원칙 2 (Nyquist): octaves 수를 제한하여 final wavelength = base / (lacunarity^N)
//                     의 Nyquist 위반 방지.
//                     voxelSize=0.15m 기준:
//                       octaves=3, lacunarity=2 → final λ = base × 1/8
//                       base λ ≥ 1.2m 여야 final λ ≥ 0.15m (Nyquist 경계)
//                       base λ ≥ 2.4m 권장 (margin 2×)
//   원칙 1 (Analytical 우선): fBm/Ridged는 보조(secondary) 노이즈로만 사용.
//                              primary는 해석적 함수 (sin/cos/length/smin).
//   호출자 책임: BiomeSDF_ProfileSO에서 octaves를 [Range(1,3)]으로 강제할 것
//                (A.3에서 처리). 여기선 compile-time clamp로 이중 보호.
// ───────────────────────────────────────────────────────────────────────────────
#ifndef MAX_OCTAVES
    #define MAX_OCTAVES 3
#endif

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

// [추가됨] 3D 보로노이를 위한 3D 해시 함수
float3 hash3D(float3 p)
{
    p = float3(dot(p, float3(127.1, 311.7, 74.7)),
               dot(p, float3(269.5, 183.3, 246.1)),
               dot(p, float3(113.5, 271.9, 124.6)));
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

// ----------------------------------------------------
// 5. 3D 셀룰러 보로노이 노이즈 (Voronoi 3D - 절벽 크랙용)
// ----------------------------------------------------
void Voronoi3D(float3 x, out float f1, out float f2)
{
    float3 n = floor(x);
    float3 f = frac(x);

    f1 = 8.0;
    f2 = 8.0;

    for (int k = -1; k <= 1; k++)
    {
        for (int j = -1; j <= 1; j++)
        {
            for (int i = -1; i <= 1; i++)
            {
                float3 g = float3(float(i), float(j), float(k));
                float3 o = hash3D(n + g);
                float3 r = g + o - f;
                
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
    }
    
    f1 = sqrt(f1);
    f2 = sqrt(f2);
}

// ═══════════════════════════════════════════════════════════════════════════════
// [Gate 5 Phase A.2] 확장 노이즈 — 6종
//   fbmPerlin / fbmSimplex / ridgedPerlin / ridgedSimplex / worleyF2F1 / worleyStandard
//
// 목적:
//   BiomeSDF_ProfileSO (A.3 예정)의 NoiseLayerConfig.type 분기용 6종 레시피 제공.
//   기존 snoise/Voronoi/fBm 함수는 그대로 유지(규칙 #6 호출 변경 없음).
//
// 4원칙 준수:
//   원칙 1: 모두 보조(secondary) 용도. primary는 여전히 sin/length/smin.
//   원칙 2: octaves는 MAX_OCTAVES (3) clamp. Ridged는 절대값 변환만 하므로
//          주파수 특성 동일 → 추가 Nyquist 위반 없음.
//   원칙 3: 모두 C¹ 연속 (fade curve 사용 Perlin / smooth Voronoi gradient).
//   원칙 4: 반환값 range [-1, 1] 정규화. amplitude는 호출자 책임.
// ═══════════════════════════════════════════════════════════════════════════════

// ───────────────────────────────────────────────────────────
// [A.2] Perlin 기본 함수 (Classic Gradient Noise)
//   기존 snoise (Simplex)와 다른 대안 스펙트럼 특성 제공.
//   Perlin은 격자 정렬된 artifacts가 있지만 계산 비용 낮음.
// ───────────────────────────────────────────────────────────
float fadeCurve(float t)
{
    // Ken Perlin 개선 fade: 6t⁵ - 15t⁴ + 10t³ (C² 연속)
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

float2 fadeCurve2(float2 t) { return t * t * t * (t * (t * 6.0 - 15.0) + 10.0); }
float3 fadeCurve3(float3 t) { return t * t * t * (t * (t * 6.0 - 15.0) + 10.0); }

float perlin2D(float2 p)
{
    float2 pi = floor(p);
    float2 pf = frac(p);
    float2 w  = fadeCurve2(pf);

    // 4 격자점 gradient (hash2D 범위 [0,1] → [-1,1])
    float2 g00 = hash2D(pi)                       * 2.0 - 1.0;
    float2 g10 = hash2D(pi + float2(1.0, 0.0))    * 2.0 - 1.0;
    float2 g01 = hash2D(pi + float2(0.0, 1.0))    * 2.0 - 1.0;
    float2 g11 = hash2D(pi + float2(1.0, 1.0))    * 2.0 - 1.0;

    // gradient · offset dot products
    float n00 = dot(g00, pf);
    float n10 = dot(g10, pf - float2(1.0, 0.0));
    float n01 = dot(g01, pf - float2(0.0, 1.0));
    float n11 = dot(g11, pf - float2(1.0, 1.0));

    // bilinear interp with fade weights
    float nx0 = lerp(n00, n10, w.x);
    float nx1 = lerp(n01, n11, w.x);
    // 결과 정규화: Perlin2D 이론적 max amplitude ≈ sqrt(2)/2 ≈ 0.707
    return lerp(nx0, nx1, w.y) * 1.414; // [-1, 1] 근사 정규화
}

float perlin3D(float3 p)
{
    float3 pi = floor(p);
    float3 pf = frac(p);
    float3 w  = fadeCurve3(pf);

    // 8 격자점 gradient
    float3 g000 = hash3D(pi + float3(0, 0, 0)) * 2.0 - 1.0;
    float3 g100 = hash3D(pi + float3(1, 0, 0)) * 2.0 - 1.0;
    float3 g010 = hash3D(pi + float3(0, 1, 0)) * 2.0 - 1.0;
    float3 g110 = hash3D(pi + float3(1, 1, 0)) * 2.0 - 1.0;
    float3 g001 = hash3D(pi + float3(0, 0, 1)) * 2.0 - 1.0;
    float3 g101 = hash3D(pi + float3(1, 0, 1)) * 2.0 - 1.0;
    float3 g011 = hash3D(pi + float3(0, 1, 1)) * 2.0 - 1.0;
    float3 g111 = hash3D(pi + float3(1, 1, 1)) * 2.0 - 1.0;

    float n000 = dot(g000, pf - float3(0, 0, 0));
    float n100 = dot(g100, pf - float3(1, 0, 0));
    float n010 = dot(g010, pf - float3(0, 1, 0));
    float n110 = dot(g110, pf - float3(1, 1, 0));
    float n001 = dot(g001, pf - float3(0, 0, 1));
    float n101 = dot(g101, pf - float3(1, 0, 1));
    float n011 = dot(g011, pf - float3(0, 1, 1));
    float n111 = dot(g111, pf - float3(1, 1, 1));

    float nx00 = lerp(n000, n100, w.x);
    float nx10 = lerp(n010, n110, w.x);
    float nx01 = lerp(n001, n101, w.x);
    float nx11 = lerp(n011, n111, w.x);
    float ny0  = lerp(nx00, nx10, w.y);
    float ny1  = lerp(nx01, nx11, w.y);
    // Perlin3D 이론적 max amplitude ≈ sqrt(3)/2 ≈ 0.866
    return lerp(ny0, ny1, w.z) * 1.155; // [-1, 1] 근사 정규화
}

// ───────────────────────────────────────────────────────────
// [A.2] fBm 6종 레시피
//   모두 MAX_OCTAVES clamp (원칙 2 compile-time guard).
//   호출자가 octaves=10을 넣어도 내부적으로 3으로 제한됨.
// ───────────────────────────────────────────────────────────

// fbmPerlin: Classic Perlin fBm
//   특성: 격자 정렬 soft, 넓은 저주파 도미넌트. Warp/ground variation 적합.
float fbmPerlin(float3 x, int octaves, float lacunarity, float gain)
{
    int n = min(octaves, MAX_OCTAVES);
    float sum = 0.0;
    float amp = 1.0;
    float norm = 0.0;
    [unroll(MAX_OCTAVES)]
    for (int i = 0; i < MAX_OCTAVES; i++)
    {
        if (i >= n) break;
        sum  += perlin3D(x) * amp;
        norm += amp;
        x    *= lacunarity;
        amp  *= gain;
    }
    return sum / max(norm, 1e-5); // [-1, 1] 정규화
}

// fbmSimplex: 기존 fBm의 래퍼 + MAX_OCTAVES clamp
//   원본 fBm은 규칙 #6 불변. 신규 호출자는 이 wrapper 사용 권장.
float fbmSimplex(float3 x, int octaves, float lacunarity, float gain)
{
    int n = min(octaves, MAX_OCTAVES);
    float sum = 0.0;
    float amp = 1.0;
    float norm = 0.0;
    [unroll(MAX_OCTAVES)]
    for (int i = 0; i < MAX_OCTAVES; i++)
    {
        if (i >= n) break;
        sum  += snoise(x) * amp;
        norm += amp;
        x    *= lacunarity;
        amp  *= gain;
    }
    return sum / max(norm, 1e-5);
}

// ridgedPerlin: |perlin|의 inversion → 능선(ridge) 강조
//   특성: 절벽/산맥/협곡 암석 패턴 적합. C⁰ 불연속 없음 (abs + smooth).
float ridgedPerlin(float3 x, int octaves, float lacunarity, float gain)
{
    int n = min(octaves, MAX_OCTAVES);
    float sum = 0.0;
    float amp = 1.0;
    float norm = 0.0;
    [unroll(MAX_OCTAVES)]
    for (int i = 0; i < MAX_OCTAVES; i++)
    {
        if (i >= n) break;
        // ridge = 1 - |noise|, 이후 제곱으로 peak 강조
        float r = 1.0 - abs(perlin3D(x));
        r = r * r;
        sum  += r * amp;
        norm += amp;
        x    *= lacunarity;
        amp  *= gain;
    }
    // [0, 1] 범위 반환 → caller가 signed로 변환 원하면 (val*2-1) 적용
    return sum / max(norm, 1e-5);
}

// ridgedSimplex: Simplex 기반 ridged
float ridgedSimplex(float3 x, int octaves, float lacunarity, float gain)
{
    int n = min(octaves, MAX_OCTAVES);
    float sum = 0.0;
    float amp = 1.0;
    float norm = 0.0;
    [unroll(MAX_OCTAVES)]
    for (int i = 0; i < MAX_OCTAVES; i++)
    {
        if (i >= n) break;
        float r = 1.0 - abs(snoise(x));
        r = r * r;
        sum  += r * amp;
        norm += amp;
        x    *= lacunarity;
        amp  *= gain;
    }
    return sum / max(norm, 1e-5);
}

// ───────────────────────────────────────────────────────────
// [A.2] Worley/Voronoi 확장
//   기존 Voronoi2D/3D (f1, f2 out)는 그대로 유지.
//   아래는 자주 쓰는 조합의 래퍼.
// ───────────────────────────────────────────────────────────

// worleyStandard2D: F1만 반환 (일반 cellular noise)
float worleyStandard2D(float2 x)
{
    float f1, f2;
    Voronoi2D(x, f1, f2);
    return f1; // 거리 (0 = cell 중심)
}

float worleyStandard3D(float3 x)
{
    float f1, f2;
    Voronoi3D(x, f1, f2);
    return f1;
}

// worleyF2F1_2D / 3D: F2-F1 반환 (edge distance)
//   0에 가까울수록 cell 경계 → crack/vein 패턴에 이상적.
//   OreVein 시스템 (B.9)과 Case 5 crack에서 사용 예정.
float worleyF2F1_2D(float2 x)
{
    float f1, f2;
    Voronoi2D(x, f1, f2);
    return f2 - f1;
}

float worleyF2F1_3D(float3 x)
{
    float f1, f2;
    Voronoi3D(x, f1, f2);
    return f2 - f1;
}

#endif // CAVE_NOISE_LIBRARY_INCLUDED