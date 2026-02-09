#ifndef VOLUMETRIC_FOG_CORE_INCLUDED
#define VOLUMETRIC_FOG_CORE_INCLUDED

#include "VolumetricFog_Input.hlsl"

// 밴딩 방지용 노이즈
float GetJitter(float2 uv, float2 screenParams)
{
    float2 noiseUV = uv * (screenParams / 256.0);
    return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV).r;
}

// 조명 합산 (Ambient + Point/Spot Lights)
float3 CalculateAllLights(float3 pos)
{
    float3 totalLight = float3(0, 0, 0);
    
    // [Void Fix] 조명이 닿지 않는 공간을 채워주는 베이스 색상
    totalLight += _AmbientColor.rgb;

    int count = min(_CustomLightCount, 64);
    for (int i = 0; i < count; i++)
    {
        float4 lightData = _CustomLightPosRange[i];
        float3 lightPos = lightData.xyz;
        float invRangeSqr = lightData.w;
        float3 lightCol = _CustomLightColor[i].rgb;

        float3 toLight = lightPos - pos;
        float distSqr = dot(toLight, toLight);
        
        // 거리 감쇠 (폭발 방지 공식)
        float attenuation = 1.0 / (1.0 + distSqr);
        float rangeFactor = distSqr * invRangeSqr;
        float rangeCutoff = saturate(1.0 - (rangeFactor * rangeFactor));
        rangeCutoff *= rangeCutoff;
        
        totalLight += lightCol * attenuation * rangeCutoff;
    }
    return totalLight;
}
#endif