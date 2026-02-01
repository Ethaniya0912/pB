#ifndef RETRO_HYBRID_DITHERING_INCLUDED
#define RETRO_HYBRID_DITHERING_INCLUDED

/**
 * Tech Spec 09: Hybrid Dithering (Banded Specular & Lighting)
 * @param InColor     : 디더링을 적용할 입력 색상 (float3)
 * @param Steps        : 색상 단계 (Posterization 레벨)
 * @param DitherStrength : 디더링 강도 (0~1)
 * @param ScreenPos    : 화면 좌표 (Screen Position - float4)
 * @param OutColor     : 최종 디더링된 결과물 (float3)
 */
void ApplyHybridDithering_float(
    float3 InColor, 
    float Steps, 
    float DitherStrength, 
    float4 ScreenPos, 
    out float3 OutColor)
{
    // 1. Bayer 4x4 Matrix 정의 (컴파일 에러 방지를 위해 값을 직접 입력)
    // 중괄호 직후 / 16.0 연산이 문법 오류를 일으키므로 미리 계산된 값을 넣거나 배열 형식을 사용합니다.
    static const float bayer4x4[4][4] = {
        {0.0000, 0.5000, 0.1250, 0.6250},
        {0.7500, 0.2500, 0.8750, 0.3750},
        {0.1875, 0.6875, 0.0625, 0.5625},
        {0.9375, 0.4375, 0.8125, 0.3125}
    };

    // 2. 화면 좌표를 이용한 픽셀 인덱싱
    // 원근 보정된 좌표를 위해 ScreenPos.xy / ScreenPos.w 를 사용하거나 
    // 단순 스크린 좌표인 경우 ScreenPos.xy를 사용합니다.
    float2 screenUV = ScreenPos.xy / max(0.001, ScreenPos.w);
    uint2 pixelPos = uint2(screenUV * _ScreenParams.xy) % 4;
    float threshold = bayer4x4[pixelPos.x][pixelPos.y];

    // 3. 하이브리드 양자화 로직
    float levels = max(1.0, Steps);
    float3 dithered = InColor * levels;
    
    // 디더링 임계값 적용 (중심값을 0.5로 보정하여 밝기 변화 최소화)
    float offset = (threshold - 0.5) * DitherStrength;
    dithered += offset;

    // 4. 정수화 및 정규화 (Banding 구현)
    OutColor = floor(dithered + 0.5) / levels;
}

#endif