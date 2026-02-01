#ifndef RETRO_COLOR_QUANTIZATION_INCLUDED
#define RETRO_COLOR_QUANTIZATION_INCLUDED

/**
 * L4: Color - Color Quantization 로직 구현
 * * @param In          : 입력 색상 (float3)
 * @param Steps       : 채널당 색상 단계 (float - 예: 32.0은 5비트 컬러)
 * @param Out         : 양자화된 출력 색상 (float3)
 */
void ApplyColorQuantization_float(
    float3 In, 
    float Steps, 
    out float3 Out)
{
    // 0~1 범위를 Steps 단계로 나누어 소수점 아래를 버림(floor) 처리한 후 다시 정규화합니다.
    // 이는 하드웨어가 부동소수점 데이터를 특정 비트 수의 정수로 저장할 때 발생하는 정밀도 소실을 시뮬레이션합니다.
    
    float levels = max(1.0, Steps); // 0으로 나누기 방지
    Out = floor(In * levels) / levels;
}

#endif