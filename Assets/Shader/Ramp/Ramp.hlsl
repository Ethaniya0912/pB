#ifndef DREAMCORE_PBR_MOHR_PARALLAX_INCLUDED
#define DREAMCORE_PBR_MOHR_PARALLAX_INCLUDED

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

// [최적화 Opt 4: 거리 기반 POM 페이딩 토글 변수 선언]
// Shader Graph의 Blackboard에 아래 변수들을 추가해 주세요.
// _EnablePomFading (Boolean/Float), _PomFadeStart (Float), _PomFadeEnd (Float)
// float _EnablePomFading;
// float _PomFadeStart;
// float _PomFadeEnd;
// [최적화 Opt 4: 거리 기반 POM 페이딩 토글 변수 선언 끝]
#endif

/**
 * [모듈 1] Geometry Specular AA
 */
float GetGeometricRoughness(float3 worldNormal, float aaSize)
{
    if (aaSize <= 0.0)
        return 0.0;
    float3 normalDDX = ddx(worldNormal);
    float3 normalDDY = ddy(worldNormal);
    return aaSize * (length(normalDDX) + length(normalDDY));
}

/**
 * [모듈 2] 알파 블렌딩 램프 해석기
 */
float3 ApplyDreamcoreRampWithAlpha(float3 normal, float3 lightDir, float shadowAtten, float distAtten, float3 lightColor, float3 albedo)
{
#ifdef SHADERGRAPH_PREVIEW
    return saturate(dot(normal, lightDir)) * lightColor;
#else
    float NdotL = saturate(dot(normal, lightDir));
    float totalInfluence = NdotL * distAtten * shadowAtten;
    
    float safeGamma = max(0.01, _GlobalRampGamma);
    float acceleratedInfluence = pow(max(0.0, totalInfluence), safeGamma);
    
    float quantizedLookup = (_GlobalSteps > 0.1) ? floor(acceleratedInfluence * _GlobalSteps) / _GlobalSteps : acceleratedInfluence;
    
    float2 rampUV = float2(quantizedLookup, _GlobalMadness);
    float4 rampSample = SAMPLE_TEXTURE2D_LOD(_GlobalRampTex, sampler_GlobalRampTex, rampUV, 0);
    
    return lerp(NdotL * distAtten * shadowAtten * lightColor, rampSample.rgb * lightColor, rampSample.a);
#endif
}

/**
 * [메인 함수] CalculateDreamcorePBR_float
 */
void CalculateDreamcorePBR_float(
    float2 UV,
    UnityTexture2D AlbedoTex,
    UnityTexture2D NormalTex,
    UnityTexture2D MaskTex,
    UnitySamplerState SS,
    
    float3 TangentWS,
    float3 BitangentWS,
    float3 NormalWS,
    
    float MetallicScale,
    float OcclusionScale,
    float RoughnessScale,
    float HeightScale,
    float NormalScale,
    
    float3 Emission,
    float3 WorldPos,
    float SpecularToggle,
    float ReflectionToggle,
    float SpecularAASize,
    out float3 FinalColor
)
{
    FinalColor = float3(0, 0, 0);

#ifdef SHADERGRAPH_PREVIEW
    FinalColor = float3(0.5, 0.5, 0.5);
#else
    // 1. TBN 및 시선 방향 처리
    float3 T = normalize(TangentWS);
    float3 B = normalize(BitangentWS);
    float3 N = normalize(NormalWS);
    float3x3 worldToTangent = float3x3(T, B, N);
    
    float3 viewDirWS = normalize(_WorldSpaceCameraPos - WorldPos);
    float3 viewDirTS = mul(worldToTangent, viewDirWS);

    // [최적화 Opt 4: 거리 기반 POM 페이딩 토글 적용]
    // 2. POM (Parallax Occlusion Mapping)
    float minSteps = 8.0;
    float maxSteps = 32.0;
    float numSteps = lerp(maxSteps, minSteps, saturate(abs(viewDirTS.z)));
    
    // 거리 기반 페이딩 연산
    if (_EnablePomFading > 0.5)
    {
        float distToCam = length(_WorldSpaceCameraPos - WorldPos);
        float fadeFactor = 1.0 - saturate((distToCam - _PomFadeStart) / max(0.001, (_PomFadeEnd - _PomFadeStart)));
        numSteps *= fadeFactor;
    }
    
    // Bypass용 기본 UV 초기화
    float2 parallaxUV = UV;
    
    // numSteps가 1.0 미만이면 무거운 루프를 완전히 건너뜁니다 (Bypass)
    UNITY_BRANCH

    if (numSteps >= 1.0)
    {
        float layerDepth = 1.0 / numSteps;
        float currentLayerDepth = 0.0;
        
        float safeHeightScale = HeightScale * 0.1;
        float2 P = viewDirTS.xy * safeHeightScale / (max(0.001, viewDirTS.z + 0.05));
        float2 deltaUV = P / numSteps;
        
        float2 currentUV = UV;
        float heightFromTexture = SAMPLE_TEXTURE2D(MaskTex, SS, currentUV).b;
        
        [loop]
        for (int i = 0; i < 32; i++)
        {
            if (currentLayerDepth < 1.0 - heightFromTexture)
            {
                currentUV -= deltaUV;
                heightFromTexture = SAMPLE_TEXTURE2D(MaskTex, SS, currentUV).b;
                currentLayerDepth += layerDepth;
            }
            else
                break;
        }
        
        float2 prevUV = currentUV + deltaUV;
        float weight = (heightFromTexture - (1.0 - currentLayerDepth)) / (max(0.0001, (heightFromTexture - (1.0 - currentLayerDepth)) - (SAMPLE_TEXTURE2D(MaskTex, SS, prevUV).b - (1.0 - currentLayerDepth + layerDepth))));
        parallaxUV = lerp(currentUV, prevUV, weight);
    }
    // [최적화 Opt 4: 거리 기반 POM 페이딩 토글 적용 끝]

    // 3. 텍스처 샘플링
    float3 sampledAlbedo = SAMPLE_TEXTURE2D(AlbedoTex, SS, parallaxUV).rgb;
    float4 rawNormal = SAMPLE_TEXTURE2D(NormalTex, SS, parallaxUV);
    float3 sampledNormalTS = UnpackNormal(rawNormal);
    sampledNormalTS.xy *= NormalScale;
    sampledNormalTS = normalize(sampledNormalTS);
    
    float4 sampledMask = SAMPLE_TEXTURE2D(MaskTex, SS, parallaxUV);

    // 4. MOHR 데이터 처리
    float finalMetallic = saturate(sampledMask.r * MetallicScale);
    
    // [Roughness 개선] 수치가 0.4일 때 5.17배를 곱하면 1.0으로 고정됩니다.
    // 슬라이더 조절이 더 민감하게 반응하도록 saturate를 나중에 적용합니다.
    float baseRoughness = sampledMask.a;
    float finalRoughness = saturate(baseRoughness * RoughnessScale);
    
    float heightShadow = saturate(sampledMask.b + 0.2);
    float finalOcclusion = saturate(sampledMask.g * heightShadow * OcclusionScale);

    // 5. 월드 노멀 확정
    float3 worldNormal = normalize(T * sampledNormalTS.x + B * sampledNormalTS.y + N * sampledNormalTS.z);

    // 6. Roughness 및 Smoothness 결정
    float geoRough = GetGeometricRoughness(worldNormal, SpecularAASize);
    float actualRoughness = max(finalRoughness, geoRough);
    // Smoothness가 0에 가까우면 광택이 아예 사라집니다.
    float smoothness = 1.0 - actualRoughness;

    float3 resultColor = float3(0, 0, 0);
    float4 shadowCoord = TransformWorldToShadowCoord(WorldPos);
    
    // --- [A] 직접광: 메인 라이트 ---
    Light mainLight = GetMainLight(shadowCoord);
    resultColor += sampledAlbedo * ApplyDreamcoreRampWithAlpha(worldNormal, mainLight.direction, mainLight.shadowAttenuation, mainLight.distanceAttenuation, mainLight.color, sampledAlbedo);

    if (SpecularToggle > 0.5)
    {
        float3 halfDir = normalize(mainLight.direction + viewDirWS);
        float NdotH = saturate(dot(worldNormal, halfDir));
        // smoothness가 0.6(Roughness 0.4)일 때 하이라이트가 적절히 맺힙니다.
        float specPower = exp2(10.0 * smoothness + 1.0);
        float spec = pow(NdotH, specPower);
        resultColor += spec * lerp(0.04, sampledAlbedo, finalMetallic) * mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation);
    }

    // --- [B] 직접광: 추가 라이트 ---
#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    for (uint i = 0u; i < pixelLightCount; ++i) 
    {
        Light light = GetAdditionalLight(i, WorldPos, shadowCoord);
        resultColor += sampledAlbedo * ApplyDreamcoreRampWithAlpha(worldNormal, light.direction, light.shadowAttenuation, light.distanceAttenuation, light.color, sampledAlbedo);
        
        if (SpecularToggle > 0.5) 
        {
            float3 halfDir = normalize(light.direction + viewDirWS);
            float NdotH = saturate(dot(worldNormal, halfDir));
            float specPower = exp2(10.0 * smoothness + 1.0);
            float spec = pow(NdotH, specPower);
            resultColor += spec * lerp(0.04, sampledAlbedo, finalMetallic) * light.color * (light.distanceAttenuation * light.shadowAttenuation);
        }
    }
#endif

    // --- [C] 환경 반사 (IBL) ---
    float3 reflectionColor = float3(0, 0, 0);
    if (ReflectionToggle > 0.5)
    {
        float3 reflectDir = reflect(-viewDirWS, worldNormal);
        reflectionColor = GlossyEnvironmentReflection(reflectDir, actualRoughness, finalOcclusion);
        reflectionColor *= lerp(float3(0.04, 0.04, 0.04), sampledAlbedo, finalMetallic);
    }

    FinalColor = (resultColor * finalOcclusion) + reflectionColor + Emission;
#endif
}

#endif