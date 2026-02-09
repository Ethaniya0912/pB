#ifndef DREAMCORE_PBR_EXTENDED_INCLUDED
#define DREAMCORE_PBR_EXTENDED_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

/**
 * [전역 변수 영역] 
 */
#ifndef SHADERGRAPH_PREVIEW
    TEXTURE2D(_GlobalRampTex);
    SAMPLER(sampler_GlobalRampTex);
    float _GlobalMadness;
    float _GlobalRampGamma;
    float _GlobalSteps;

    // 스캔라인 제어 변수 추가
    float _GlobalScanlineIntensity;
    float _GlobalScanlineDensity;
    float _GlobalScanlineSpeed;
#endif

/**
 * [핵심 로직] 드림코어 램프 해석기
 */
float3 ApplyDreamcoreRampShift(float3 normal, float3 lightDir, float shadowAtten, float distAtten, float3 lightColor)
{
    #ifdef SHADERGRAPH_PREVIEW
        return saturate(dot(normal, lightDir)) * lightColor;
    #else
        float NdotL = saturate(dot(normal, lightDir));
        float totalInfluence = saturate(NdotL * distAtten * shadowAtten);
        
        float safeGamma = max(0.01, _GlobalRampGamma);
        float acceleratedInfluence = pow(totalInfluence, safeGamma);
        
        float quantizedLookup = acceleratedInfluence;
        if (_GlobalSteps > 0.1) {
            quantizedLookup = floor(acceleratedInfluence * _GlobalSteps) / _GlobalSteps;
        }
        
        quantizedLookup = clamp(quantizedLookup, 0.01, 0.99);
        
        float2 rampUV = float2(quantizedLookup, _GlobalMadness);
        float3 rampColor = SAMPLE_TEXTURE2D_LOD(_GlobalRampTex, sampler_GlobalRampTex, rampUV, 0).rgb;
        
        return rampColor * lightColor;
    #endif
}

/**
 * [메인 함수] CalculateDreamcorePBR_float
 */
void CalculateDreamcorePBR_float(
    float3 Albedo,
    float3 NormalTS,
    float3 Tangent,
    float3 Bitangent,
    float3 NormalWS,
    float Metallic,
    float Smoothness,
    float Occlusion,
    float3 Emission,
    float3 WorldPos,
    float SpecularToggle,
    float ReflectionToggle,
    out float3 FinalColor
)
{
    FinalColor = float3(0, 0, 0);

    #ifdef SHADERGRAPH_PREVIEW
        FinalColor = Albedo * 0.5 + Emission;
    #else
        float3x3 tbn = float3x3(Tangent, Bitangent, NormalWS);
        float3 worldNormal = normalize(mul(NormalTS, tbn));
        float3 viewDir = normalize(_WorldSpaceCameraPos - WorldPos);
        float3 resultColor = float3(0, 0, 0);
        float perceptualRoughness = 1.0 - Smoothness;
        
        // --- [A] 메인 라이트 ---
        float4 shadowCoord = TransformWorldToShadowCoord(WorldPos);
        Light mainLight = GetMainLight(shadowCoord);
        resultColor += Albedo * ApplyDreamcoreRampShift(worldNormal, mainLight.direction, mainLight.shadowAttenuation, mainLight.distanceAttenuation, mainLight.color);

        // --- [B] 추가 라이트 ---
        uint pixelLightCount = GetAdditionalLightsCount();
        for (uint i = 0u; i < pixelLightCount; ++i)
        {
            Light light = GetAdditionalLight(i, WorldPos);
            resultColor += Albedo * ApplyDreamcoreRampShift(worldNormal, light.direction, light.shadowAttenuation, light.distanceAttenuation, light.color);
            
            if (SpecularToggle > 0.5)
            {
                float3 halfDir = normalize(light.direction + viewDir);
                float spec = pow(saturate(dot(worldNormal, halfDir)), max(1.0, Smoothness * 128.0));
                resultColor += spec * Metallic * light.color * light.distanceAttenuation * light.shadowAttenuation;
            }
        }

        // --- [C] 환경 반사 ---
        float3 reflectionColor = float3(0, 0, 0);
        if (ReflectionToggle > 0.5)
        {
            float3 reflectDir = reflect(-viewDir, worldNormal);
            reflectionColor = GlossyEnvironmentReflection(reflectDir, perceptualRoughness, Occlusion);
            reflectionColor *= lerp(float3(0.04, 0.04, 0.04), Albedo, Metallic);
        }

        // --- [D] 최종 합성 ---
        float3 combined = (resultColor * Occlusion) + reflectionColor + Emission;

        // --- [E] 스캔라인 효과 (Lens Distortion을 따라가도록 픽셀 기반 계산) ---
        // ComputeScreenPos를 통해 현재 픽셀의 화면 좌표를 가져옵니다.
        float4 screenPos = ComputeScreenPos(TransformWorldToHClip(WorldPos));
        float2 screenUV = screenPos.xy / screenPos.w;

        // 가로줄 무늬 계산 (Sin 파형)
        float scanline = sin(screenUV.y * _GlobalScanlineDensity + _Time.y * _GlobalScanlineSpeed);
        // Intensity에 따라 강도 조절 (1.0이면 완전 적용, 0.0이면 미적용)
        float scanlineValue = lerp(1.0, (scanline * 0.5 + 0.5), _GlobalScanlineIntensity);
        
        FinalColor = combined * scanlineValue;
    #endif
}

#endif