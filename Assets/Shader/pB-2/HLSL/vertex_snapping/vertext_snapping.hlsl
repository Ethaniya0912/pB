#ifndef RETRO_VERTEX_SNAPPING_INCLUDED
#define RETRO_VERTEX_SNAPPING_INCLUDED

/**
 * Tech Spec 01: Vertex Snapping & Strategic Jittering (Fixed for Shader Graph)
 * * @param posOS       : 오브젝트 공간의 버텍스 좌표 (Position in Object Space - float3)
 * @param res         : 스냅할 가상 해상도 (예: 320, 240)
 * @param intensity   : 지터링 강도 (0 ~ 1)
 * @param distAtten   : 거리에 따른 떨림 가중치
 * @param nearGuard   : 근거리 모델 보호 임계값
 * @param animSpeed   : 정지 상태에서의 미세 떨림 속도
 * @param outPos      : 결과 좌표 (Object Space - float3)
 */
void ApplyVertexSnapping_float(
    float3 posOS, 
    float2 res, 
    float intensity, 
    float distAtten, 
    float nearGuard, 
    float animSpeed, 
    out float3 outPos)
{
    // 1. Object Space -> Homogeneous Clip Space 변환
    // 셰이더 그래프의 입출력 타입(float3) 문제를 해결하기 위해 내부에서 4차원 변환을 수행합니다.
    float4 posCS = TransformObjectToHClip(posOS);
    
    // 2. 카메라로부터의 거리 계산 (Clip Space W 활용)
    float dist = posCS.w;
    
    // 3. 근거리 보호 로직
    float guardFactor = smoothstep(0.0, nearGuard, dist);
    
    // 4. 시간 기반 미세 떨림 오프셋
    float timeOffset = animSpeed > 0 ? sin(_Time.y * animSpeed * 10.0) * 0.02 : 0;
    
    // 5. 최종 강도 결합
    float finalIntensity = (intensity + timeOffset) * (1.0 + dist * distAtten) * guardFactor;
    
    // 6. 스냅 수행
    if (finalIntensity > 0.001) 
    {
        float2 snapGrid = res.xy / max(0.01, finalIntensity);
        posCS.xy = floor(posCS.xy * snapGrid) / snapGrid;
    }

    // 7. Clip Space를 다시 Object Space로 복원 (마스터 스택 연결용)
    // 셰이더 그래프 마스터 스택의 Position(Object Space) 슬롯에 바로 꽂을 수 있게 변환합니다.
    float4 worldPos = mul(UNITY_MATRIX_I_VP, posCS);
    worldPos /= worldPos.w;
    outPos = mul(UNITY_MATRIX_I_M, worldPos).xyz;
}

#endif