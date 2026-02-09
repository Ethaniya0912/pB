#ifndef DREAMCORE_PBR_ALPHA_BLEND_INCLUDED
#define DREAMCORE_PBR_ALPHA_BLEND_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

#ifndef SHADERGRAPH_PREVIEW
    TEXTURE2D(_GlobalRampTex);
    SAMPLER(sampler_GlobalRampTex);
float _GlobalMadness;
float _GlobalRampGamma;
float _GlobalSteps;
#endif

/**
 * [수정된 로직] 알파 채널을 활용한 블렌딩 함수
 * LUT의 Alpha를 사용하여 '표준 조명'과 '램프 조명' 사이를 보간합니다.
 */
float3 ApplyDreamcoreRampWithAlpha(float3 normal, float3 lightDir, float shadowAtten, float distAtten, float3 lightColor, float3 albedo)
{
#ifdef SHADERGRAPH_PREVIEW
        return saturate(dot(normal, lightDir)) * lightColor;
#else
    float NdotL = saturate(dot(normal, lightDir));
    float totalInfluence = NdotL * distAtten * shadowAtten;
        
    float safeGamma = max(0.01, _GlobalRampGamma);
    float acceleratedInfluence = pow(totalInfluence, safeGamma);
        
    float quantizedLookup;
    if (_GlobalSteps > 0.1)
    {
        quantizedLookup = floor(acceleratedInfluence * _GlobalSteps) / _GlobalSteps;
    }
    else
    {
        quantizedLookup = acceleratedInfluence;
    }
        
        // 1. RGBA 전체를 샘플링합니다.
    float2 rampUV = float2(quantizedLookup, _GlobalMadness);
    float4 rampSample = SAMPLE_TEXTURE2D_LOD(_GlobalRampTex, sampler_GlobalRampTex, rampUV, 0);
        
    float3 rampRGB = rampSample.rgb;
    float rampAlpha = rampSample.a; // LUT 텍스처의 알파값 추출
        
        // 2. 표준 라이팅(Standard NdotL)과 램프 라이팅을 준비합니다.
    float3 standardLight = NdotL * distAtten * shadowAtten * lightColor;
    float3 rampLight = rampRGB * lightColor;
        
        // 3. 알파값을 가중치로 사용하여 블렌딩합니다.
        // Alpha가 0이면 원래 텍스처+표준조명 느낌, 1이면 강한 램프 LUT 조명 느낌
    return lerp(standardLight, rampLight, rampAlpha);
#endif
}

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
        FinalColor = Albedo;
#else
    float3x3 tbn = float3x3(Tangent, Bitangent, NormalWS);
    float3 worldNormal = normalize(mul(NormalTS, tbn));
    float3 viewDir = normalize(_WorldSpaceCameraPos - WorldPos);
    float3 resultColor = float3(0, 0, 0);
        
        // --- [A] 메인 라이트 ---
    float4 shadowCoord = TransformWorldToShadowCoord(WorldPos);
    Light mainLight = GetMainLight(shadowCoord);
        
        // Albedo를 함수 인자로 전달하여 내부에서 블렌딩에 사용
    resultColor += Albedo * ApplyDreamcoreRampWithAlpha(worldNormal, mainLight.direction, mainLight.shadowAttenuation, mainLight.distanceAttenuation, mainLight.color, Albedo);

        // --- [B] 추가 라이트 (포인트 라이트 등) ---
#if defined(_ADDITIONAL_LIGHTS)
            uint pixelLightCount = GetAdditionalLightsCount();
            for (uint i = 0u; i < pixelLightCount; ++i)
            {
                Light light = GetAdditionalLight(i, WorldPos, shadowCoord);
                resultColor += Albedo * ApplyDreamcoreRampWithAlpha(worldNormal, light.direction, light.shadowAttenuation, light.distanceAttenuation, light.color, Albedo);
                
                if (SpecularToggle > 0.5)
                {
                    float3 halfDir = normalize(light.direction + viewDir);
                    float spec = pow(saturate(dot(worldNormal, halfDir)), max(1.0, Smoothness * 128.0));
                    resultColor += spec * Metallic * light.color * (light.distanceAttenuation * light.shadowAttenuation);
                }
            }
#endif

        // --- [C] 환경 반사 및 최종 합성 ---
    float perceptualRoughness = 1.0 - Smoothness;
    float3 reflectionColor = float3(0, 0, 0);
    if (ReflectionToggle > 0.5)
    {
        float3 reflectDir = reflect(-viewDir, worldNormal);
        reflectionColor = GlossyEnvironmentReflection(reflectDir, perceptualRoughness, Occlusion);
        reflectionColor *= lerp(float3(0.04, 0.04, 0.04), Albedo, Metallic);
    }

    FinalColor = (resultColor * Occlusion) + reflectionColor + Emission;
#endif
}

#endif