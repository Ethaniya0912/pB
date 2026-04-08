#ifndef CAVE_BIOME_MATH_INCLUDED
#define CAVE_BIOME_MATH_INCLUDED

// 공용 구조체 및 순수 수학 라이브러리 종속성
#include "CaveDataStructs.hlsl"
#include "CaveNoiseLibrary.hlsl"

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
            float karstNoise = fBm(float3(pos.x, pos.y * safeYComp, pos.z) * safeFreq, 4, 2.0, 0.5);
            // 바닥: baseSDF 유지(wallMask0=0). 벽: 최대 ±2.0m 요철(5.0→2.0)
            detailSDF += karstNoise * 2.0 * wallMask0;
            break;
        }
            
        case 1:
        {
            // [바닥자연화 Fix-1] wallMask 추가 + abs() 제거 + 진폭 축소
            //   기존: abs(fBm)*3.0 wallMask없이 전체 적용 → 바닥 ±5.25m 스파이크
            //   수정: wallMask로 바닥(normalY높음) 보호. 부호 있는 noise → 유기적 요철.
            //   진폭 1.2: 벽면에 ±1.2m 자연 요철 (5.25m에서 대폭 축소)

            // wallMask: abs(normalY)=0.2(수직벽)=1.0, abs(normalY)=0.65(수평바닥)=0.0
            // 50° 전환 구간 → 벽-바닥 경계 부드럽게
            float wallMask1 = smoothstep(0.65, 0.2, abs(normalY));

            // 도메인 워프 진폭 4.0→2.5 (바닥 안정성 향상)
            float3 warpedPos = pos + float3(snoise(pos * safeFreq * 0.5), 0,
                                            snoise(pos * safeFreq * 0.5 + 10.0)) * 2.5;

            // [FIX-NONVERTICAL v2] terraceSteps 복원 + yOscillation 병행
            //
            // 이전 FIX-NONVERTICAL의 오류:
            //   strataY = pos.y + yOscillation  ← terraceSteps 파라미터 완전 무시!
            //   → Inspector에서 terraceSteps=1 설정해도 case 1에서 효과 없음
            //
            // 근본 문제: yOscillation A=0.45 최솟값 구간에서
            //   ∂strataY/∂y_min = 1-0.45×2.2 = 0.01
            //   Y gradient = 0.3×0.01×1.2 = 0.003/m
            //   ATA[y][y]/ATA[x][x] = (0.003/0.33)² = 0.0001% → 수직!
            //
            // 수정: terraceSteps를 복원하고 yOscillation은 위상 변조용으로만 사용
            //   terraceSteps=1 → Y gradient (경계 4.0/m, 내부 0.36/m) 보장
            //   yOscillation  → XZ 위치마다 계단 위상 변조
            //                   규칙적 수평 줄무늬 → 자연스러운 물결 지층
            //
            // terraceSteps=1 + yOscillation 병행:
            //   내부 셀 Y gradient = 0.36 + 0.019 = 0.379/m
            //   ATA[y][y]/ATA[x][x] = 1.32 → 강한 비수직 ✓
            //   경계 셀 Y gradient = 4.0/m → 압도적 비수직 ✓

            // terraceSteps 복원
            float safeTerrace = max(p.terraceSteps, 0.001);
            float terracedY = floor(pos.y * safeTerrace) / safeTerrace;

            // yOscillation: terracedY에 추가하여 계단 위상을 XZ마다 변조
            // A=0.43 < 1/freq(=0.455) → 단조성 보장: ∂strataY/∂y_min = 0.054 > 0
            float yPhase = snoise2D(pos.xz * 0.13) * 4.7;
            float yOscillation = sin(pos.y * 2.2 + yPhase) * 0.43;
            float strataY = terracedY + yOscillation;  // terraceSteps + 위상 변조
            float3 strataPos = float3(warpedPos.x, strataY, warpedPos.z);

            // [핵심] abs() 제거: 부호 있는 noise → 올록볼록한 지층 요철
            float faultNoise = fBm(strataPos * safeFreq, 3, 2.0, 0.5);

            // wallMask: 벽면만 거칠게, 바닥은 baseSDF 유지
            detailSDF += faultNoise * 1.2 * wallMask1;
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
    }
    
    return detailSDF;
}

#endif // CAVE_BIOME_MATH_INCLUDED