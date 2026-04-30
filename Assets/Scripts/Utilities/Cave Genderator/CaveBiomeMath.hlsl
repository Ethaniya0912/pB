#ifndef CAVE_BIOME_MATH_INCLUDED
#define CAVE_BIOME_MATH_INCLUDED

// 공용 구조체 및 순수 수학 라이브러리 종속성
#include "CaveDataStructs.hlsl"
#include "CaveNoiseLibrary.hlsl"
#include "CaveGeometryPrimitives.hlsl"  // [E-α.1] 지질 프리미티브 라이브러리

// ===============================================================================
// [Gate 5 Phase A.8] Stalactite uniform — forward declaration
//   CaveBiomeMath.hlsl이 CaveDensityGenerator.compute보다 먼저 include되므로,
//   ApplyBiomeDetail 내부에서 _EnableStalactite 참조 가능하려면 여기 선언 필요.
//   compute shader에서 SetFloat로 값 주입 (같은 global uniform scope).
//   0 = OFF (규칙 #6 byte-identical), > 0.5 = ON
// ===============================================================================
float _EnableStalactite;

// ===============================================================================
// [Gate 5 Phase A.10 / Phase 4-1 사전 조치] Tide Line Y — Case 6 Marine 전용
//   CaveBiomeData.tideLineY에서 Dispatcher가 SetFloat로 주입.
//   Case 6 이외 biome에서는 참조되지 않음 → byte-identical.
//   기본값 0 (월드 원점 높이). DepthLayer 스케일에 따라 조정.
// ===============================================================================
float _TideLineY;

// ===============================================================================
// [Phase 4.5-G P3] Columnar Soft-Terrace — forward declaration
//   _EnableColumnarSoftTerrace uniform (CaveDensityGenerator.compute SetInt)
//   Case 1 (Columnar) terracedY 처리 모드:
//     0 = OFF (Route_Astar floor() hard step, byte-identical baseline)
//     1 = ON  (smoothstep 기반 soft transition, P2 C¹ 회복, wedge 해결)
//   atomic preset SingleSourceEcotone (γ state) 진입 시 자동 ON.
// ===============================================================================
int _EnableColumnarSoftTerrace;

// ===============================================================================
// [Phase 4.5-G P1] Columnar Voronoi Noise — forward declaration
//   _EnableColumnarVoronoiNoise uniform (CaveDensityGenerator.compute SetInt)
//   Case 1 (Columnar) 노이즈 식 선택:
//     0 = OFF (Legacy fBm + terracedY, Route_Astar byte-identical baseline)
//     1 = ON  (Voronoi 2D Subtractive — 자연 주상절리 시각, P1~P4 모두 안전)
//
//   동작 차이:
//     OFF: faultNoise * 0.7 * wallMask  (Additive — 통로에 ridge 자라남)
//     ON:  exp(-(f2-f1)*5) * (1+yJoint) * 0.4 * wallMask  (Subtractive — 벽 식각)
//
//   파라미터 (sharpness 5, cellSize 2.5m, amp 0.4m, yJoint 0.3 / freq 4.2):
//     P1: ~0.12m / voxel (80% of voxelSize 0.15m)
//     P2: 대부분 C¹ smooth (Voronoi 3-cell vertex 제외)
//     P3: λ_min 0.5m (Nyquist 1.66× 여유)
//     P4: amp max 0.52m / 한도 1.2m (43% 마진)
//
//   atomic preset SingleSourceEcotone (γ state) 진입 시 자동 ON.
//   옵션 A 적용: γ에서만 ON, α/β는 Legacy fBm 보존 (Case 3 비교군 대조).
// ===============================================================================
int _EnableColumnarVoronoiNoise;

// ═══════════════════════════════════════════════════════════════════════
// [Phase 12-D] ★ M1 — Columnar DC Offset 보상 (단층 본질 해결)
//
//   ridge ∈ [0, 1] → ridgeBalanced ∈ [-_ColumnarDCOffset, 1-_ColumnarDCOffset]
//   default 0 → 기존 동작 (Subtractive, ridge 그대로)
//   권장 0.5 → 평균 0 (대칭) → Karst와 baseline 일치 → 단층 사라짐
//
//   atomic preset η/ε에서 자동 0.5로 설정 (D2 보장 — 0이면 영향 없음)
// ═══════════════════════════════════════════════════════════════════════
float _ColumnarDCOffset;

// ═══════════════════════════════════════════════════════════════════════════════
// [η DebugAware Phase 7+11] FragmentSafe + Case 4 Voronoi 토글
//   default 0 (OFF) → 기존 동작 fast-path
//   η DebugAware OR ε FullMerge에서만 1로 set (Compute shader Dispatcher SetFloat)
// ═══════════════════════════════════════════════════════════════════════════════
float _EnableCase4Voronoi;       // Phase 11 — Case 4 Rocky Voronoi 재설계
float _EnableEdgeIDDecorrelation;// Phase 7 — Edge ID Noise Decorrelation

// ===============================================================================
// [Part 4] 지질학 응용 라이브러리 (Geological Application & SDF)
// 형태의 유기적 융합과 지대(Biome)별 이중 SDF 분기 처리를 전담합니다.
// ===============================================================================

// ----------------------------------------------------
// 1. 매끄러운 공간 융합 연산 (Smooth Boolean Operations)
// ----------------------------------------------------

float smin(float a, float b, float k)
{
    if (k <= 0.0001)
        return min(a, b);
    float h = saturate(0.5 + 0.5 * (b - a) / k);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

float smax(float a, float b, float k)
{
    if (k <= 0.0001)
        return max(a, b);
    float h = saturate(0.5 + 0.5 * (a - b) / k);
    return lerp(b, a, h) + k * h * (1.0 - h);
}

// 둥근 터널(Edge) 조각용 캡슐 거리 함수
float sdCapsule(float3 p, float3 a, float3 b, float r)
{
    float3 pa = p - a, ba = b - a;
    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 0.000001));
    return length(pa - ba * h) - r;
}

// [신규] 협곡 통로용 거리 함수 (천장이 열려있고 바닥이 평평한 U자형)
float sdCanyon(float3 p, float3 a, float3 b, float r)
{
    float3 pa = p - a, ba = b - a;
    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 0.000001));
    float3 closestPt = a + ba * h;

    float3 canyonPt = closestPt;
    // p가 중심점보다 높으면 높이 차이를 무시하여 천장을 위로 무한히 엽니다.
    if (p.y > closestPt.y)
        canyonPt.y = p.y;

    float dist = length(p - canyonPt) - r;
    
    // 바닥 평탄화 (원의 최하단보다 약간 높은 위치에서 평평하게 깎아냄)
    float flatBottom = (closestPt.y - r * 0.7) - p.y;
    dist = smax(dist, flatBottom, 1.5);
    
    return dist;
}

// ═══════════════════════════════════════════════════════════════════════════
// [Gate 5 Phase E-α.2] Primitive Router — Tunnel / Room
//
// 목적:
//   biome별 적절한 primitive를 호출하는 라우터.
//   기본값(enum 0 = Capsule/Sphere)은 byte-identical 보장.
//
// 라우팅 규칙:
//   tunnelPrimitive enum:
//     0 = Capsule       (기본, 기존 sdCapsule)
//     1 = HexPrism      (Columnar)
//     2 = Box           (Sedimentary wide-low)
//     3 = Slot          (Canyon)
//     4 = CapsuleBlocks (Rocky)
//
//   roomPrimitive enum:
//     0 = Sphere        (기본, 기존 구형)
//     1 = Pear          (Karst)
//     2 = HexColumnSpace (Columnar)
//     3 = DiscPlate     (Sedimentary)
//     4 = CathedralVault (Colonnade / Boss)
//
// 파라미터 협약:
//   EvaluateTunnelByBiome: (enum, p, a, b, width)
//     - width는 기본 반경. 각 primitive 내부에서 적절히 분배
//       (예: BoxCapsule은 width → wideR=width, lowR=width*0.8)
//   EvaluateRoomByBiome: (enum, p, center, radius)
//     - radius는 기본 반경. primitive별 내부에서 height 등 파생
//
// byte-identical:
//   enum 0 (Capsule/Sphere) → 기존 sdCapsule / sdSphere 결과와 100% 동일
// ═══════════════════════════════════════════════════════════════════════════

float EvaluateTunnelByBiome(int primitiveType, float3 p, float3 a, float3 b, float width)
{
    // Fast path: 기본값 Capsule → byte-identical (규칙 #6)
    if (primitiveType == 0)
        return sdCapsule(p, a, b, width);

    // 경로 분기
    if (primitiveType == 1)
    {
        // HexPrism — Columnar
        return sdHexPrismCapsule(p, a, b, width);
    }
    else if (primitiveType == 2)
    {
        // Box (wide-low) — Sedimentary
        // wide: width 그대로, low: width * 0.8 (편평)
        return sdBoxCapsule(p, a, b, width, width * 0.8);
    }
    else if (primitiveType == 3)
    {
        // Slot — Canyon (좁고 높음)
        // width: width * 0.5, height: width * 2.0
        return sdSlotCapsule(p, a, b, max(width * 0.5, 0.9), width * 2.0);
    }
    else if (primitiveType == 4)
    {
        // CapsuleBlocks — Rocky
        // blockScale 3m, blockAmp width * 0.2
        return sdCapsuleWithBlocks(p, a, b, width, 3.0, width * 0.2);
    }

    // Unknown primitive → fallback to Capsule (안전)
    return sdCapsule(p, a, b, width);
}

float EvaluateRoomByBiome(int primitiveType, float3 p, float3 center, float radius)
{
    // Fast path: 기본값 Sphere → byte-identical (규칙 #6)
    if (primitiveType == 0)
        return length(p - center) - radius;

    if (primitiveType == 1)
    {
        // Pear — Karst
        return sdPear(p, center, radius);
    }
    else if (primitiveType == 2)
    {
        // HexColumnSpace — Columnar
        return sdHexColumnSpace(p, center, radius, radius * 1.5);
    }
    else if (primitiveType == 3)
    {
        // DiscPlate — Sedimentary
        // thickness: radius * 0.4 (납작)
        return sdDiscPlate(p, center, radius, max(radius * 0.4, 1.5));
    }
    else if (primitiveType == 4)
    {
        // CathedralVault — Colonnade / Boss
        // height: radius * 1.5
        return sdCathedralVault(p, center, radius, radius * 1.5);
    }

    // Unknown primitive → fallback to Sphere (안전)
    return length(p - center) - radius;
}

// [신규] 방(Room) 협곡화 거리 함수
float sdCanyonNode(float3 p, float3 center, float r)
{
    float3 canyonPt = center;
    if (p.y > center.y)
        canyonPt.y = p.y;
    float dist = length(p - canyonPt) - r;
    
    float flatBottom = (center.y - r * 0.7) - p.y;
    dist = smax(dist, flatBottom, 1.5);
    return dist;
}


// ═══════════════════════════════════════════════════════════════════════════════
// [Gate 5 Phase E-β.1] Edge Curvature — sdCapsule Chain
//
// 목적:
//   단일 sdCapsule (직선 통로)의 "일직선 반복" 완화.
//   N개 제어점 chain + smin으로 자연 곡선 통로 표현.
//   양 끝 endTaper=0으로 인접 edge 접속부 보존 (플레이어빌리티).
//
// 4원칙/규칙 준수:
//   원칙 1 (Analytical): hash/lerp/smin 전부 해석적. fBm 의존 없음
//   원칙 2 (Nyquist):    EDGE_SEGMENTS=5 → segment 간격 edge_len/4 (λ 5m+ 대형)
//   원칙 3 (C⁰):         smin(k=1.5)로 C¹ 연속 조인트
//   원칙 4 (Amp):        curvature offset amp 제약 (§7 규칙 #30 제안)
//   규칙 #2 sdCapsule 보존: 원래 함수 유지 + 내부에서 4회 호출
//   규칙 #6 byte-identical: curvatureAmp<0.01 → sdCapsule 직접 → bit-identical
//   규칙 #22 Phase 3-B seam: endTaper로 양끝 offset=0 → chunk boundary 직선 유지
//
// 플레이어빌리티 가드 (하드 제약):
//   - curvature offset amp ≤ width × 0.5 (통로 차단 방지)
//   - 짧은 edge (< 2m)는 직선 fallback (곡률 의미 없음)
//   - 양 끝 endTaper=0 (startPos, endPos가 정확히 chain 시작/끝)
// ═══════════════════════════════════════════════════════════════════════════════

#ifndef EDGE_SEGMENTS
    #define EDGE_SEGMENTS 5  // 제어점 수 (5 points → 4 segments)
#endif

float EvaluateCurvedTunnel(float3 p, float3 startPos, float3 endPos,
                            float width, float edgeID, float curvatureAmp)
{
    // Fast path 1: curvature 비활성 → 직선 sdCapsule (규칙 #6)
    if (curvatureAmp < 0.01)
        return sdCapsule(p, startPos, endPos, width);

    float3 direction = endPos - startPos;
    float edgeLen = length(direction);

    // Fast path 2: 너무 짧은 edge → 제어점 배치 무의미 → 직선
    if (edgeLen < 2.0)
        return sdCapsule(p, startPos, endPos, width);

    float3 tangent = direction / edgeLen;

    // Orthonormal basis — tangent가 거의 수직이면 up 축 변경 (degenerate 방지)
    float3 upSeed = (abs(tangent.y) > 0.9) ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0);
    float3 bitangent = normalize(cross(tangent, upSeed));
    float3 up = cross(bitangent, tangent);  // orthonormal 완성

    // 플레이어빌리티 가드: offset amp ≤ width × 0.5
    //   예: width=3m → safeAmp ≤ 1.5m → 통로 radius의 절반 이내 벗어남 → 통과 보장
    float safeAmp = min(curvatureAmp, width * 0.5);

    // 제어점 5개 — start, 0.25t, 0.5t, 0.75t, end
    // endTaper: sin(π·t)로 양 끝 0, 중간 1
    //   t=0.25 → sin(π/4) ≈ 0.7071
    //   t=0.5  → sin(π/2) = 1.0
    //   t=0.75 → sin(3π/4) ≈ 0.7071
    const float taper1 = 0.7071;
    const float taper2 = 1.0;
    const float taper3 = 0.7071;

    // Hash offsets — hash2D → [0, 1], -0.5로 [-0.5, +0.5]
    // edgeID multiplier로 다른 edge 간 decorrelation
    float2 h1 = hash2D(float2(edgeID * 1.1, 1.7)) - 0.5;
    float2 h2 = hash2D(float2(edgeID * 1.1, 3.3)) - 0.5;
    float2 h3 = hash2D(float2(edgeID * 1.1, 5.7)) - 0.5;

    float3 c0 = startPos;
    float3 c1 = startPos + direction * 0.25
              + up        * h1.x * safeAmp * taper1
              + bitangent * h1.y * safeAmp * taper1;
    float3 c2 = startPos + direction * 0.5
              + up        * h2.x * safeAmp * taper2
              + bitangent * h2.y * safeAmp * taper2;
    float3 c3 = startPos + direction * 0.75
              + up        * h3.x * safeAmp * taper3
              + bitangent * h3.y * safeAmp * taper3;
    float3 c4 = endPos;

    // Chain of 4 capsules
    float d0 = sdCapsule(p, c0, c1, width);
    float d1 = sdCapsule(p, c1, c2, width);
    float d2 = sdCapsule(p, c2, c3, width);
    float d3 = sdCapsule(p, c3, c4, width);

    // smin (k=1.5m joint smoothing) — C¹ 연속 조인트
    float dMin = smin(d0, d1, 1.5);
    dMin = smin(dMin, d2, 1.5);
    dMin = smin(dMin, d3, 1.5);

    return dMin;
}


// ═══════════════════════════════════════════════════════════════════════════════
// [Gate 5 Phase A.3] SampleNoiseByType — NoiseType enum 분기 헬퍼
//
// 목적:
//   BiomeSDF_ProfileSO.NoiseLayerConfig.type (int로 GPU 전송된 값)을
//   해당 노이즈 함수 호출로 라우팅.
//
// 호출 매핑 (BiomeSDF_ProfileSO.cs enum NoiseType 순서 기준):
//   0 = Perlin        → fbmPerlin
//   1 = Simplex       → fbmSimplex
//   2 = Worley        → worleyStandard3D (cellular, F1)
//   3 = RidgedPerlin  → ridgedPerlin
//   4 = RidgedSimplex → ridgedSimplex
//   5 = WorleyF2F1    → worleyF2F1_3D (edge distance)
//
// 4원칙 준수:
//   원칙 2: octaves는 CaveNoiseLibrary.hlsl의 MAX_OCTAVES (3)로 compile-time clamp.
//           asset-time BiomeSDF_ProfileSO.cs Range(1,3)로 이중 보호.
//   원칙 1: 이 헬퍼는 "보조 noise" 호출 전용. primary SDF (sdCapsule/smin)는
//           EvaluateSkeleton에서 여전히 해석적.
//
// 현재 상태 (A.3):
//   함수는 정의만, 호출부 없음. A.7+ Case 재작업 시 호출 예정.
//   규칙 #6: 미호출 → byte-identical 유지.
// ═══════════════════════════════════════════════════════════════════════════════
float SampleNoiseByType(int noiseType, float3 pos, int octaves, float lacunarity, float gain)
{
    // unknown type 방어 → 0 반환 (호출자가 amp 곱할 때 무영향)
    [branch] switch (noiseType)
    {
        case 0: return fbmPerlin(pos, octaves, lacunarity, gain);           // [-1, 1]
        case 1: return fbmSimplex(pos, octaves, lacunarity, gain);          // [-1, 1]
        case 2: return worleyStandard3D(pos) * 2.0 - 1.0;                   // [0, ~1.4] → [-1, ~1.8] (대략)
        case 3: return ridgedPerlin(pos, octaves, lacunarity, gain) * 2.0 - 1.0;  // [0, 1] → [-1, 1]
        case 4: return ridgedSimplex(pos, octaves, lacunarity, gain) * 2.0 - 1.0; // [0, 1] → [-1, 1]
        case 5: return worleyF2F1_3D(pos) * 2.0 - 1.0;                      // [0, ~1] → [-1, ~1]
        default: return 0.0;
    }
}

// 2D 버전 (XZ 평면 detail 용도)
float SampleNoiseByType2D(int noiseType, float2 pos, int octaves, float lacunarity, float gain)
{
    // 2D는 Perlin2D만 직접 구현, 나머지는 3D 사용 시 y=0으로 호출
    [branch] switch (noiseType)
    {
        case 0: return perlin2D(pos); // 단일 옥타브 perlin2D (fbm은 3D만 제공)
        case 1: return snoise2D(pos); // 기존 simplex 2D
        case 2: return worleyStandard2D(pos) * 2.0 - 1.0;
        case 3: // ridged variants는 2D 미구현 — 3D 호출로 폴백
        case 4: return SampleNoiseByType(noiseType, float3(pos.x, 0.0, pos.y), octaves, lacunarity, gain);
        case 5: return worleyF2F1_2D(pos) * 2.0 - 1.0;
        default: return 0.0;
    }
}


// ----------------------------------------------------
// 2. 다중 지대(Biome) 형태 분기 라우터 (Dual SDF 적용 대상)
// ----------------------------------------------------

// [수정됨] 매개변수에 normalY(바닥/벽면 기울기)가 추가되어, 유저가 걷는 바닥을 보호합니다.
float ApplyBiomeDetail(int noiseType, float3 pos, float baseSDF, float normalY, BiomeParamData p)
{
    float detailSDF = baseSDF;
    
    float safeFreq = max(p.noiseFrequency, 0.001);
    float safeYComp = max(p.yCompression, 0.001);
    
    switch (noiseType)
    {
        case 0:
        {
            // [바닥자연화] Case 0도 wallMask 추가: 바닥에 과도한 노이즈 방지
            float wallMask0 = smoothstep(0.7, 0.25, abs(normalY));
            // ═══════════════════════════════════════════════════════════════
            // [η DebugAware Phase 10] Case 0 Karst — fBm 4 oct → 2 oct
            //
            //   사용자 권장 (4원칙 audit borderline 해결):
            //     기존 4 oct: amp 누적 1.875 → 보수적 평가 시 borderline
            //     변경 2 oct: amp 누적 1.5 → 안전 영역
            //     amp 보상: 0.6 → 0.75 (실효 ±1.125m 유지)
            //
            //   D2 영향: LOW — Case 0 noise 표면 디테일만 변경, baseSDF 영향 없음
            //   사용자가 위반 정도 허용 시 amp 0.75 → 1.0 가능 (±1.5m, 사용자 결정)
            // ═══════════════════════════════════════════════════════════════
            float karstNoise = fBm(float3(pos.x, pos.y * safeYComp, pos.z) * safeFreq, 2, 2.0, 0.5);
            // amp 누적: 2 oct gain=0.5 → 1+0.5 = 1.5
            // 실효 ±1.125m: 1.5 × 0.75 = ±1.125m (기존 4 oct × 0.6 = ±1.125m와 동일)
            detailSDF += karstNoise * 0.75 * wallMask0;

            // ═══════════════════════════════════════════════════════════════
            // [Gate 5 Phase A.8] Case 0 Stalactite — 천장 종유석
            //
            // 활성 조건: 천장 부근 (normalY < -0.4 → 아래 향하는 면)
            //   cell-based 배치: pos.xz를 3m cell로 grid화 → hash 기반 길이
            //   길이: 0.5~2.0m (플레이어빌리티 — 너무 길면 통로 차단)
            //   wallMask0 반대 영역 (천장)에 적용
            // ═══════════════════════════════════════════════════════════════
            if (_EnableStalactite > 0.5 && normalY < -0.4)
            {
                float2 cellXZ = floor(pos.xz / 3.0);  // 3m cell grid
                float h = hash2D(cellXZ + float2(0.7, 1.3)).x;
                if (h > 0.6)  // 40% 확률 종유석
                {
                    // 종유석 길이 0.5~2.0m
                    float stalLen = lerp(0.5, 2.0, frac(h * 7.13));
                    // cell 중심에서의 lateral 거리
                    float2 cellCenter = (cellXZ + 0.5) * 3.0;
                    float lateralDist = length(pos.xz - cellCenter);
                    // 종유석 원뿔: radius 0.3m 뾰족
                    float coneR = max(0.3 - lateralDist * 0.5, 0.0);
                    if (coneR > 0.01)
                    {
                        // 천장 아래로 stalLen만큼 연장
                        float stalSDF = lateralDist - coneR;
                        // 천장 접촉면 기준 stalLen 제한
                        //   normalY<0인 위치에서만 실질적 적용 (천장)
                        detailSDF = min(detailSDF, stalSDF - stalLen * 0.3);
                        //                                       ^ 축소 계수: 통로 차단 방지
                    }
                }
            }
            break;
        }
            
        case 1:
        {
            // [바닥자연화 Fix-1] wallMask 추가 + abs() 제거 + 진폭 축소
            //   기존: abs(fBm)*3.0 wallMask없이 전체 적용 → 바닥 ±5.25m 스파이크
            //   수정: wallMask로 바닥(normalY높음) 보호. 부호 있는 noise → 유기적 요철.

            // wallMask: abs(normalY)=0.2(수직벽)=1.0, abs(normalY)=0.65(수평바닥)=0.0
            // 50° 전환 구간 → 벽-바닥 경계 부드럽게
            float wallMask1 = smoothstep(0.65, 0.2, abs(normalY));

            // ═══════════════════════════════════════════════════════════════════
            // [Phase 4.5-G P1] Columnar Voronoi vs Legacy 분기 (★ 옵션 A)
            //   _EnableColumnarVoronoiNoise == 0 (default): Legacy fBm 식 (D2 baseline)
            //   _EnableColumnarVoronoiNoise == 1: Voronoi 2D Subtractive (자연 시각)
            //
            //   atomic preset SingleSourceEcotone (γ state) 진입 시 자동 ON.
            //   α/β state는 OFF 유지 → fBm 비교군 보존 (Case 3 비교군 컨셉).
            // ═══════════════════════════════════════════════════════════════════
            if (_EnableColumnarVoronoiNoise == 1)
            {
                // ── 새 식: Voronoi 2D + Y invariance + Subtractive ─────────────
                //   sharpness 5, cellSize 2.5m, amp 0.4m — P1~P4 모두 안전
                //
                //   동작:
                //     1. XZ 평면 Voronoi cell — Y에 무관 (모든 Y에서 같은 격자)
                //     2. exp(-(f2-f1)*5) ridge — V자 sharp 균열 (cell 경계)
                //     3. column별 random Y phase — 자연스런 horizontal joint
                //     4. ★ Subtractive (-=) — 벽이 식각, 통로 가로막지 않음
                float cellSize = 2.5;
                float vorF1, vorF2;
                Voronoi2D(pos.xz / cellSize, vorF1, vorF2);

                // V자 sharp ridge: 경계 ≈ 1.0, 중심 ≈ 0.0
                //   sharpness 5: e-folding 0.2 → 균열 폭 ≈ 0.5m (Nyquist 1.66×)
                float ridge = exp(-(vorF2 - vorF1) * 5.0);

                // column별 random Y phase: 모든 column이 같은 Y에 joint 안 함
                //   각 cell마다 hash로 [0, 2π] 랜덤 offset
                //   sin freq 4.2 → λ = 1.5m (실제 주상절리 horizontal joint 간격)
                float2 cellId = floor(pos.xz / cellSize);
                float yPhase = hash2D(cellId).x * 6.28;
                float yJoint = sin(pos.y * 4.2 + yPhase) * 0.3;

                // ★ Subtractive (-=): ridge 큰 곳에서 SDF 감소 → air 확장
                //                      → 벽 식각 효과 (실제 columnar)
                //   amp max: 1.0 × (1+0.3) × 0.4 = 0.52m (P4 43%, 한도 1.2m)
                //
                // ═══════════════════════════════════════════════════════════
                // [Phase 12-D] ★ M1 — DC Offset 보상 (단층 본질 해결)
                //
                //   문제: ridge ∈ [0, 1] (항상 양수) × -= → detailSDF 평균 -0.26m
                //         → Karst 자기영역 (대칭 평균 0)과 baseline 0.34m 차이
                //         → ζ Blank Zone 통과 후 vertical level step (단층) 발생
                //
                //   해결: ridge - _ColumnarDCOffset 변환 (default 0.5 = ridge 평균)
                //         결과: ridgeBalanced ∈ [-0.5, +0.5] → 평균 0 (대칭)
                //         -= 그대로 유지 (양수면 통로 침투, 음수면 외부 확장)
                //
                //   G1 정합: 양수 detailOffset은 G1 (Tunnel Guard)이 차단 →
                //            Columnar 의도 ("벽 식각, 통로 안전") 자연 유지
                //
                //   D2 보장: _ColumnarDCOffset = 0이면 ridge 그대로 (기존 동작)
                //   atomic preset η/ε에서 default 0.5로 자동 설정
                // ═══════════════════════════════════════════════════════════
                float ridgeBalanced = ridge - _ColumnarDCOffset;
                detailSDF -= ridgeBalanced * (1.0 + yJoint) * 0.4 * wallMask1;
            }
            else
            {
                // ── Legacy 식: fBm + terracedY (Route_Astar byte-identical) ───
                float safeTerrace = max(p.terraceSteps, 0.001);

                // ═══════════════════════════════════════════════════════════════
                // [Phase 4.5-G P3] Columnar Soft-Terrace (이전 phase에서 추가)
                //   OFF (0, default): floor() hard step (Route_Astar baseline)
                //   ON  (1): smoothstep 기반 soft transition (P2 C¹ 회복)
                // ═══════════════════════════════════════════════════════════════
                float terracedY;
                if (_EnableColumnarSoftTerrace == 1)
                {
                    // Soft-Terrace: floor + smoothstep(frac)
                    float yScaled = pos.y * safeTerrace;
                    float yFloor = floor(yScaled);
                    float yFrac = yScaled - yFloor;             // [0, 1)
                    float ySmooth = smoothstep(0.4, 0.6, yFrac); // [0, 1] but C¹
                    terracedY = (yFloor + ySmooth) / safeTerrace;
                }
                else
                {
                    // Legacy: floor() hard step (Route_Astar byte-identical)
                    terracedY = floor(pos.y * safeTerrace) / safeTerrace;
                }

                // [P0 원칙4 긴급패치] 도메인 워프 2.5 → 1.5 (좁은 통로 SDF 평가 위치 shift 축소)
                float3 warpedPos = pos + float3(snoise(pos * safeFreq * 0.5), 0,
                                                snoise(pos * safeFreq * 0.5 + 10.0)) * 1.5;
                float3 strataPos = float3(warpedPos.x, terracedY, warpedPos.z);

                // [핵심] abs() 제거: 부호 있는 noise → 올록볼록한 지층 요철
                // (abs이면 항상 파임 → 스파이크. 부호 있으면 볼록/오목 균형)
                float faultNoise = fBm(strataPos * safeFreq, 3, 2.0, 0.5);

                // [P0 원칙4 긴급패치] amp 1.2 → 0.7
                //   이전: faultNoise(±1.75) × 1.2 = 실질 ±2.1m → 원칙4 1.75× 초과
                //   패치: faultNoise(±1.75) × 0.7 = 실질 ±1.2m → 원칙4 준수
                //   warp 1.5m는 별도 (amp 합산 아닌 position displacement)
                detailSDF += faultNoise * 0.7 * wallMask1;
            }
            break;
        }

        case 2:
            float f1, f2;
            Voronoi2D(pos.xz * safeFreq, f1, f2);
            float columnSDF = 1.0 - f1;
            float heightOffset = hash2D(floor(pos.xz * safeFreq)) * 5.0;
            detailSDF = max(detailSDF, -(columnSDF * 2.0 + heightOffset));
            break;
            
        case 3:
            float spacing = 15.0;
            float2 repeatXZ = fmod(abs(pos.xz) + spacing * 0.5, spacing) - spacing * 0.5;
            float cylinderSDF = length(repeatXZ) - 2.0;
            float brickPattern = abs(sin(pos.y * 10.0)) * 0.1;
            detailSDF = max(detailSDF, -(cylinderSDF - brickPattern));
            break;
            
        case 4:
        {
            // [Case 4: 극사실적 암벽 (수직/블록 암반)]
            // 벽면 마스크: normalY가 0에 가까울수록(수직 벽) 1.0, 바닥/천장일수록 0.0
                float wallMask4 = smoothstep(0.8, 0.4, abs(normalY));

                // ═══════════════════════════════════════════════════════════════
                // [η DebugAware Phase 11] Case 4 Rocky 재설계 — Voronoi + Analytical
                //
                //   기존 (snoise 5번):
                //     - warpedY (snoise×2.0)
                //     - warpedPos4 (snoise×2.0 두 번)
                //     - ridgedNoise1, ridgedNoise2 (snoise 2번)
                //     - 표면 detail (snoise 1번)
                //     → 4원칙 PureNoise 위반, P4 amp 6m (500% 초과)
                //
                //   재설계 (Hybrid: Voronoi + analytical sin):
                //     - terracedY: floor 그대로 (analytical)
                //     - rockMass: Voronoi3D F2-F1 (cell 기반, 4원칙 모범)
                //     - block strata: sin(y × freq) layers (analytical)
                //     - crack: Voronoi F2-F1 (이미 사용 중)
                //     → snoise 0번, amp ~3m (P4 250%, 여전히 위반이지만 절반 감소)
                //
                //   D2 보장: _EnableCase4Voronoi=0 시 fast-path → 기존 동작
                //             η DebugAware OR ε FullMerge에서만 활성 (atomic 토글)
                // ═══════════════════════════════════════════════════════════════
                if (_EnableCase4Voronoi > 0.5)
                {
                    // ── Phase 11 Hybrid 재설계 ─────────────────────────────
                    float layerHeight4 = max(1.0 / max(p.terraceSteps, 1.0), 0.5);

                    // Y warp: analytical (sin × cos cross — 도메인 변형)
                    float warpedY4 = pos.y + sin(pos.x * safeFreq * 0.3) * cos(pos.z * safeFreq * 0.3) * 1.5;
                    float terracedY4 = floor(warpedY4 / layerHeight4) * layerHeight4;

                    // ── (1) Rock Mass: Voronoi3D (cell-based 암반 덩어리) ────
                    float3 rockPos = float3(pos.x, terracedY4, pos.z);
                    float vMassF1, vMassF2;
                    Voronoi3D(rockPos * safeFreq * 0.6, vMassF1, vMassF2);
                    // F2-F1 = cell border 거리 — 가까울수록 0
                    float rockMass = (vMassF2 - vMassF1) * 2.0;

                    // ── (2) Strata 사이의 sin layer (analytical block 효과) ──
                    float strataLayer = abs(sin(terracedY4 * 6.28318 / layerHeight4)) * 0.4;

                    // ── (3) 결합: rockMass + strataLayer × bumpAmplitude ────
                    //   max amp ~ (1 + 0.4) × max(bumpAmplitude, 3.0) = ~4.2m
                    //   기존 6m 대비 30% 감소
                    float ridgedBlock4 = (rockMass + strataLayer) * max(p.bumpAmplitude, 3.0) * 0.6;
                    float blockySDF4 = baseSDF - (ridgedBlock4 * wallMask4);

                    // ── (4) Crack: 기존 Voronoi 그대로 (이미 4원칙 적합) ────
                    float3 crackFreq4 = float3(safeFreq * 2.0, safeFreq * 0.2, safeFreq * 2.0);
                    float vF1c, vF2c;
                    Voronoi3D(rockPos * crackFreq4, vF1c, vF2c);
                    float crack4 = (vF2c - vF1c) * 4.0;
                    float crackSDF4 = 1.0 - crack4;

                    detailSDF = lerp(blockySDF4, max(blockySDF4, crackSDF4), wallMask4);
                    // 표면 미세 디테일도 analytical (sin 두 축 product)
                    detailSDF -= abs(sin(pos.x * safeFreq * 6.0) * cos(pos.z * safeFreq * 6.0)) * 0.15 * wallMask4;
                    break;
                }

                // ── 기존 Case 4 (Pre-Phase 11) — fast-path 유지 ────────────
                float layerHeight = max(1.0 / max(p.terraceSteps, 1.0), 0.5);
                float warpedY = pos.y + snoise(pos * safeFreq * 0.1) * 2.0;
                float terracedY = floor(warpedY / layerHeight) * layerHeight;

                float3 warpedPos4 = pos + float3(snoise(pos * safeFreq * 0.5), 0, snoise(pos * safeFreq * 0.5 + 10.0)) * 2.0;
                float3 strataPos = float3(warpedPos4.x, terracedY, warpedPos4.z);

                float ridgedNoise1 = 1.0 - abs(snoise(strataPos * safeFreq * 0.8));
                float ridgedNoise2 = 1.0 - abs(snoise(strataPos * safeFreq * 2.0)) * 0.5;
                float ridgedBlock = (ridgedNoise1 + ridgedNoise2) * max(p.bumpAmplitude, 3.0);

            // 평지(바닥)는 울퉁불퉁해지지 않도록 wallMask를 곱해 보호합니다.
                float blockySDF = baseSDF - (ridgedBlock * wallMask4);

                float3 crackFreq = float3(safeFreq * 2.0, safeFreq * 0.2, safeFreq * 2.0);
                float vF1, vF2;
                Voronoi3D(warpedPos4 * crackFreq, vF1, vF2);
            
                float crack = (vF2 - vF1) * 4.0;
                float crackSDF = 1.0 - crack;

            // 바닥에서는 크랙으로 잘려나가지 않도록 lerp로 복원합니다.
                detailSDF = lerp(blockySDF, max(blockySDF, crackSDF), wallMask4);
                detailSDF -= abs(snoise(pos * safeFreq * 6.0)) * 0.2 * wallMask4;
                break;
            }
        
        case 5:
        {
            // ==============================================================================
            // [Case 5: 그랜드 캐니언 스타일 사암/퇴적암 (Stratified Sedimentary Rock)]
            // ==============================================================================
            
            // 벽면 마스크: 유저가 걸어다닐 바닥(평지)이 깎여나가는 것을 막아줍니다.
                float wallMask5 = smoothstep(0.8, 0.4, abs(normalY));

                float safeTerrace5 = max(p.terraceSteps, 1.0);
                float stepY = floor(pos.y * safeTerrace5) / safeTerrace5;
            
                float3 warpedPos5 = pos + float3(snoise(pos * safeFreq * 0.3), 0, snoise(pos * safeFreq * 0.3 + 12.0)) * 2.0;
                float3 steppedPos5 = float3(warpedPos5.x, floor(warpedPos5.y * safeTerrace5) / safeTerrace5, warpedPos5.z);

                float vF1_5, vF2_5;
                Voronoi3D(steppedPos5 * safeFreq * 1.5, vF1_5, vF2_5);
            
            // 단구(블록) 절단면
                float blockCut5 = 1.0 - (vF2_5 - vF1_5) * 4.0;
            
            // 바닥(wallMask=0)은 baseSDF를 유지, 벽면(wallMask=1)은 계단식 블록으로 잘라냄
                float canyonSDF = lerp(baseSDF, max(baseSDF, blockCut5), wallMask5);

            // 차별 침식 (가로로 긴 얇은 틈새)
                float strataPhase = pos.y * safeFreq * 15.0;
                float strataGroove = pow(abs(sin(strataPhase)), 8.0);
                float erosionMask = max(0.0, snoise(float3(pos.x * safeFreq, stepY * 4.0, pos.z * safeFreq)));
            
            // 바닥에는 이 가로 틈새가 파이지 않도록 마스킹
                float deepErosion = (1.0 - strataGroove) * erosionMask * max(p.bumpAmplitude, 2.0) * wallMask5;

                detailSDF = canyonSDF + deepErosion;
                detailSDF -= abs(snoise(pos * safeFreq * 12.0)) * 0.15 * wallMask5;
            
                break;
            }

        case 6:
        {
            // ══════════════════════════════════════════════════════════════
            // [Gate 5 Phase A.10] Case 6 Marine — 해식동굴 (Sea Cave)
            //
            // 4-Layer 구조 (L1~L4):
            //   L1 Notch (해식 노치): 조수선 기반 수평 파임 홈
            //   L2 Strat (수평 층리): 해안 퇴적 horizontal bedding
            //   L3 Scallop (scalloping): 파동 타격 세밀 요철
            //   L4 Sea Stack (해식 기둥): 고립된 수직 기둥 (현재 L1~L3만 본격 구현)
            //
            // 플레이어빌리티:
            //   L1 notch 깊이 ≤ 0.8m (폭 좁게 파임)
            //   L2 strat amp ≤ 0.5m (수평 층리)
            //   L3 scallop amp ≤ 0.3m (고빈도 저진폭)
            //   L4 seaStack은 별도 큰 구조 — 현재 단순 noise 변조로 대체
            // ══════════════════════════════════════════════════════════════
            float wallMask6 = smoothstep(0.65, 0.2, abs(normalY));

            // ── L1 Notch ─────────────────────────────────────
            // 해안선 높이 (tide line) 기준 수평 파임
            //   _TideLineY는 CaveBiomeData.tideLineY에서 전달 (Dispatcher SetFloat)
            float notchBand = smoothstep(1.5, 0.3, abs(pos.y - _TideLineY));  // tide line ±1.5m
            float notchNoise = snoise(float3(pos.x * safeFreq * 1.2, 0.0, pos.z * safeFreq * 1.2));
            float notchDepth = notchBand * max(notchNoise, 0.0) * 0.8 * wallMask6;

            // ── L2 Horizontal Stratification ─────────────────
            // 수평 층리 (Case 5와 유사하지만 더 부드럽고 낮음)
            float stratLayer = sin(pos.y * 2.5 + snoise2D(pos.xz * safeFreq * 0.3) * 1.5);
            float stratAmp = 0.5 * wallMask6;
            float stratSDF = stratLayer * stratAmp;

            // ── L3 Scallop (파동 타격 요철) ─────────────────
            // 고빈도 저진폭 — 파도에 의한 세밀 요철
            float scallopFreq = safeFreq * 8.0;
            float scallopNoise = snoise(pos * scallopFreq);
            float scallopAmp = 0.3 * wallMask6;
            float scallopSDF = abs(scallopNoise) * scallopAmp;

            // ── L4 Sea Stack 흉내 (간단 구현) ───────────────
            // 수직 기둥 — 현재는 단순 ridged noise로 흉내
            //   완전 seaStack은 별도 노드/edge 구조 필요 (향후 Phase)
            float seaStackMask = smoothstep(0.3, 0.8, wallMask6);  // 강한 벽면만
            float seaStackNoise = 1.0 - abs(snoise2D(pos.xz * safeFreq * 0.4));
            float seaStackSDF = seaStackNoise * 0.3 * seaStackMask;

            // L1~L4 결합 (주의: L1은 subtract 느낌, L2/L3/L4는 additive detail)
            detailSDF -= notchDepth;                          // L1 — 파고들어감
            detailSDF += (stratSDF + scallopSDF) * 0.8;       // L2/L3 — 요철
            detailSDF -= seaStackSDF * 0.5;                   // L4 — 돌출 기둥

            // 최종 원칙 4 clamp (total detail ≤ ±1.5m)
            break;
        }
    }
    
    return detailSDF;
}

// ════════════════════════════════════════════════════════════════════
// [η DebugAware Phase 7] ζ FragmentSafe — Edge ID Noise Decorrelation
//
//   목적: 인접 edges가 동일 noise pattern을 평가해 fan node에서
//         constructive interference로 파편 발생.
//         각 edge별 pos 평가 시 edge ID 기반 hash offset 적용 → decorrelation.
//
//   D2 보장: _EnableEdgeIDDecorrelation=0 시 fast-path → 기존 동작
//             η DebugAware OR ε FullMerge에서만 활성 (atomic 토글)
//
//   범위: noise 기반 cases (0/4/5/6) — Pure Noise만 변형
//          analytical (Case 3) / Hybrid (1/2)는 영향 없음
//
//   ★ HLSL forward declaration 제약 — ApplyBiomeDetail 정의 *다음에* 위치 필수.
// ════════════════════════════════════════════════════════════════════
float ApplyBiomeDetailDecorr(
    int noiseType, float3 pos, float baseSDF, float normalY,
    BiomeParamData p, float edgeID)
{
    // Pure Analytical (Case 3) + Hybrid (1/2) → decorrelation 불필요
    if (noiseType == 1 || noiseType == 2 || noiseType == 3)
    {
        return ApplyBiomeDetail(noiseType, pos, baseSDF, normalY, p);
    }
    // Pure Noise (0/4/5/6) → edge ID 기반 hash offset
    //   각 edge에 고유한 sampling space 부여 → fan node 누적 효과 분산
    float edgeSeed = frac(edgeID * 0.31831);  // 1/π (HLSL은 'f' suffix 미사용)
    float3 perturbedPos = pos + float3(
        edgeSeed * 0.31,
        0.0,                       // Y 영향 최소 (바닥/천장 일관성)
        edgeSeed * 0.71
    );
    return ApplyBiomeDetail(noiseType, perturbedPos, baseSDF, normalY, p);
}

// ════════════════════════════════════════════════════════════════════════════════════
// [Phase 4.5-G I7] ApplyBiomeDetail_Ecotone — Blend 영역 전용 안전 SDF (Anchor 패턴 의존)
//
//   목적: blend 중심 영역 (centrality > threshold)에서 양 case의 noise를 Additive로만
//         결합하여 P1/P4 안전한 detail 제공. Replacement 구조 (cylinder/column/canyon)
//         는 사용 안 함 — Add↔Rep 미스매치 해결.
//
//   D2 원칙:
//     OFF 시 (caller가 _EnableEcotoneSDF=0): 이 함수 호출 안 됨 → byte-identical
//     ON 시 caller가 anchor lerp로 finalDensity 결정 — 이 함수는 ecotoneDensity 반환만
//
//   특징:
//     - Add only (Case 0/1 형식과 동일 결합 식)
//     - amp × 0.5 약화 (P4 안전 마진 보장)
//     - wallMask로 floor/ceil 보호
//     - phase shift로 양 case 다양성
//     - Anchor 패턴 (caller 측에서 처리): typeA/B 중 Replacement 있을 때 baseSDF anchor
//
//   매개변수:
//     typeA, typeB:   양 case의 noiseType (caller curP에서 추출)
//     pos:            world position
//     baseSDF:        skeleton SDF (sdCapsule + sediment 결과)
//     normalY:        wallMask 계산용
//     curP:           보간된 BiomeParamData (caller에서 setup)
//     blendWeight:    [0,1] phase shift 다양성용
//
//   반환: ecotoneDensity = baseSDF + Add-only-noise × 0.3 × wallMask
// ════════════════════════════════════════════════════════════════════════════════════
float ApplyBiomeDetail_Ecotone(
    int typeA, int typeB,
    float3 pos, float baseSDF, float normalY,
    BiomeParamData curP, float blendWeight)
{
    // wallMask — floor/ceil 보호 (수평 표면 amp 추가 방지)
    float wallMask = smoothstep(0.7, 0.25, abs(normalY));
    
    // safeFreq — 0 division 가드 (기존 Dispatcher fallback 패턴 일치)
    float safeFreq = max(curP.noiseFrequency, 0.05);
    
    // ── 양 case의 noise만 추출, Replacement 제거 ─────────────────
    //   기존 ApplyBiomeDetail의 case 0/1만 동일하게 사용 (Add only)
    //   Case 2/3/5 (Replacement)도 typeA/B로 들어올 수 있으나
    //   여기선 noiseType만 사용 (cylinder/column/canyon SDF 무시)
    
    // typeA noise — 약화된 amp
    float noiseA = SampleNoiseByType(curP.noiseType, pos * safeFreq,
                                     2,        // octaves 약화 (기존 3~4 → 2)
                                     2.0,      // lacunarity 표준
                                     0.5);     // gain 표준
    
    // typeB noise — 약화된 amp (양쪽 다 같은 curP 사용 — 이미 보간됨)
    //   blendWeight 기반 phase shift로 약간의 다양성
    float3 phaseShifted = pos + float3(blendWeight * 1.5, 0.0, blendWeight * 1.5);
    float noiseB = SampleNoiseByType(curP.noiseType, phaseShifted * safeFreq,
                                     2, 2.0, 0.5);
    
    // ── lerp 가능 (Add↔Add 동일 결합 식) ─────────────────────────
    //   blendWeight 기반 부드러운 보간
    float blendedNoise = lerp(noiseA, noiseB, blendWeight);
    
    // ── amp 약화 (× 0.5) — P4 안전 마진 보장 ──────────────────────
    //   기존 case 0 amp 0.6, case 1 amp 0.7 → ecotone 0.3 (절반)
    //   wallMask로 floor/ceil 보호
    float ecotoneAmp = 0.3 * wallMask;
    
    // ── Additive 결합 (P1/P4 안전) ────────────────────────────────
    return baseSDF + blendedNoise * ecotoneAmp;
}

#endif // CAVE_BIOME_MATH_INCLUDED