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

// 다항식 부드러운 최솟값 (방과 통로의 용접, 종유석의 융합용)
float smin(float a, float b, float k)
{
    if (k <= 0.0001)
        return min(a, b);
    float h = saturate(0.5 + 0.5 * (b - a) / k);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

// 다항식 부드러운 최댓값 (바닥 평탄화 퇴적, 싱크홀의 모래시계 절삭용)
float smax(float a, float b, float k)
{
    if (k <= 0.0001)
        return max(a, b);
    float h = saturate(0.5 + 0.5 * (a - b) / k);
    return lerp(b, a, h) + k * h * (1.0 - h);
}

// 통로(Edge) 조각용 캡슐 거리 함수
float sdCapsule(float3 p, float3 a, float3 b, float r)
{
    float3 pa = p - a, ba = b - a;
    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 0.000001)); // 0으로 나누기 방지
    return length(pa - ba * h) - r;
}

// ----------------------------------------------------
// 2. 다중 지대(Biome) 형태 분기 라우터 (Dual SDF 적용 대상)
// ----------------------------------------------------
// 메인 커널에서 넘겨준 noiseType에 따라 완전히 다른 기하학을 반환합니다.
float ApplyBiomeDetail(int noiseType, float3 pos, float baseSDF, BiomeParamData p)
{
    float detailSDF = baseSDF;
    
    // Division by Zero 및 치명적 오류 차단을 위한 하드웨어 안전망
    float safeFreq = max(p.noiseFrequency, 0.001);
    float safeYComp = max(p.yCompression, 0.001);
    
    switch (noiseType)
    {
        case 0:
            // [Case 0: 석회암 (Karst Limestone)]
            // Y축을 압착/팽창하여 3D 노이즈를 늘어뜨리고 (종유석 형태 유도) 베이스에 덧바릅니다.
            float karstNoise = fBm(float3(pos.x, pos.y * safeYComp, pos.z) * safeFreq, 4, 2.0, 0.5);
            detailSDF += karstNoise * 5.0;
            break;
            
        case 1:
            // [Case 1: 단층 압착 동굴 (Fault-line Compressed Cave)]
            // Y축 좌표를 모듈로 연산하여 테라스(계단) 층리를 만들고 날카롭게 찢습니다.
            float safeTerrace = max(p.terraceSteps, 0.001);
            float terracedY = floor(pos.y * safeTerrace) / safeTerrace;
            
            // 공간을 뒤트는 강한 도메인 워핑(Domain Warping)
            float3 warpedPos = pos + float3(snoise(pos * safeFreq * 0.5), 0, snoise(pos * safeFreq * 0.5 + 10.0)) * 4.0;
            float3 strataPos = float3(warpedPos.x, terracedY, warpedPos.z);
            
            // 릿지드 멀티프랙탈(Ridged) 방식: 절댓값을 씌워 뾰족한 날을 파냅니다.
            float faultNoise = abs(fBm(strataPos * safeFreq, 3, 2.0, 0.5));
            detailSDF -= faultNoise * 3.0;
            break;

        case 2:
            // [Case 2: 현무암 주상절리 (Columnar Basalt Jointing)]
            // 2D 보로노이 셀 거리를 역산하여 유기성을 억제한 수직 육각 기둥 숲을 만듭니다.
            float f1, f2;
            Voronoi2D(pos.xz * safeFreq, f1, f2);
            
            // 셀의 중심점이 가장 볼록하게 튀어나오도록 1.0 - F1 적용
            float columnSDF = 1.0 - f1;
            
            // 해시를 이용한 각 기둥별 높낮이 양자화
            float heightOffset = hash2D(floor(pos.xz * safeFreq)) * 5.0;
            
            // 둥근 융합을 배제하고 수학적 max로 뼈대를 예리하게 파냅니다.
            detailSDF = max(detailSDF, -(columnSDF * 2.0 + heightOffset));
            break;
            
        case 3:
            // [Case 3: 리미널 스페이스 - 무한 반복의 인공 수로]
            // 모듈로(fmod) 공간 반복 연산을 통해 단 1개의 기둥 연산으로 무한의 숲을 구축합니다.
            float spacing = 15.0; // 15m 간격으로 기둥 도열
            float2 repeatXZ = fmod(abs(pos.xz) + spacing * 0.5, spacing) - spacing * 0.5;
            
            // 반경 2m짜리 무한 도열 실린더
            float cylinderSDF = length(repeatXZ) - 2.0;
            
            // 기둥 표면에 가로 줄눈(벽돌 패턴) 각인
            float brickPattern = abs(sin(pos.y * 10.0)) * 0.1;
            
            detailSDF = max(detailSDF, -(cylinderSDF - brickPattern));
            break;
    }
    
    return detailSDF;
}

#endif // CAVE_BIOME_MATH_INCLUDED