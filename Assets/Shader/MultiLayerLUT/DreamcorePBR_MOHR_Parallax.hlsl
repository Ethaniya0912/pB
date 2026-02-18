/*
    ================================================================================
    [File]          DreamcorePBR_MOHR_Parallax.hlsl
    [Role]          Shader Graph Custom Function - 드림코어 PBR 및 POM 통합 로직
    [Version]       3.1 (Local Parameterization for Independent Testing)
    [Description]   
        - 기존의 전역 변수(Global Variable) 의존성을 완전히 제거했습니다.
        - RampTex, Steps, Gamma, Madness를 개별 파라미터로 받아, 기존 시스템에
          영향을 주지 않고 머티리얼 단위에서 안전하게 테스트할 수 있습니다.
    ================================================================================
*/

#ifndef DREAMCORE_PBR_MOHR_PARALLAX_INCLUDED
#define DREAMCORE_PBR_MOHR_PARALLAX_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
 * [모듈 2] 알파 블렌딩 램프 해석기 (수정됨: 로컬 파라미터 주입)
 * - 전역 변수를 쓰지 않고, 호출부에서 모든 필요 데이터를 전달받습니다.
 */
float3 ApplyDreamcoreRampWithAlpha(
    float3 normal, float3 lightDir, float shadowAtten, float distAtten, float3 lightColor, float3 albedo,
    UnityTexture2D rampTex, UnitySamplerState ss, float steps, float gamma, float madness)
{
#ifdef SHADERGRAPH_PREVIEW
    return saturate(dot(normal, lightDir)) * lightColor;
#else
    float NdotL = saturate(dot(normal, lightDir));
    
    // ShadowAtten을 양자화 곡선에서 제외하여 그림자 지글거림 방지
    float totalInfluence = NdotL * distAtten;
    
    float safeGamma = max(0.01, gamma);
    float acceleratedInfluence = pow(max(0.0, totalInfluence), safeGamma);
    
    // 로컬 스텝 수치를 기반으로 양자화 계산
    float quantizedLookup = (steps > 0.1) ? floor(acceleratedInfluence * steps) / steps : acceleratedInfluence;
    
    float2 rampUV = float2(quantizedLookup, madness);
    
    // 전달받은 텍스처와 샘플러를 사용하여 안전하게 렌더링
    float4 rampSample = SAMPLE_TEXTURE2D_LOD(rampTex, ss, rampUV, 0);
    
    // 원래의 물리 기반 계산값
    float3 originalPBR = NdotL * distAtten * shadowAtten * lightColor;
    
    // 램프 텍스처가 적용된 색채 (그림자 감쇄는 마지막에 곱해줌)
    float3 rampedColor = rampSample.rgb * lightColor * shadowAtten;
    
    return lerp(originalPBR, rampedColor, rampSample.a);
#endif
}

/**
 * [메인 함수] CalculateDreamcorePBR_float
 * Shader Graph의 Custom Function 노드에서 이 시그니처와 동일하게 입력을 구성하세요.
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
    
    // --- 새로 추가된 로컬 파라미터 영역 (테스트용 독립 제어) ---
    UnityTexture2D LocalRampTex,
    float LocalRampSteps,
    float LocalRampGamma,
    float LocalMadness,
    
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

    // 2. POM (Parallax Occlusion Mapping)
    float minSteps = 8.0;
    float maxSteps = 32.0;
    float numSteps = lerp(maxSteps, minSteps, saturate(abs(viewDirTS.z)));
    float layerDepth = 1.0 / numSteps;
    float currentLayerDepth = 0.0;
    
    float safeHeightScale = HeightScale * 0.1;
    float2 P = viewDirTS.xy * safeHeightScale / (max(0.001, viewDirTS.z + 0.05));
    float2 deltaUV = P / numSteps;
    
    float2 currentUV = UV;
    float heightFromTexture = SAMPLE_TEXTURE2D_LOD(MaskTex, SS, currentUV, 0).b;
    
    [loop]
    for (int i = 0; i < 32; i++)
    {
        if (currentLayerDepth < 1.0 - heightFromTexture)
        {
            currentUV -= deltaUV;
            heightFromTexture = SAMPLE_TEXTURE2D_LOD(MaskTex, SS, currentUV, 0).b;
            currentLayerDepth += layerDepth;
        }
        else
            break;
    }
    
    float2 prevUV = currentUV + deltaUV;
    float prevHeight = SAMPLE_TEXTURE2D_LOD(MaskTex, SS, prevUV, 0).b;
    
    float weight = (heightFromTexture - (1.0 - currentLayerDepth)) /
                   (max(0.0001, (heightFromTexture - (1.0 - currentLayerDepth)) - (prevHeight - (1.0 - currentLayerDepth + layerDepth))));
    float2 parallaxUV = lerp(currentUV, prevUV, weight);

    // 3. 텍스처 샘플링
    float3 sampledAlbedo = SAMPLE_TEXTURE2D(AlbedoTex, SS, parallaxUV).rgb;
    float4 rawNormal = SAMPLE_TEXTURE2D(NormalTex, SS, parallaxUV);
    float3 sampledNormalTS = UnpackNormal(rawNormal);
    sampledNormalTS.xy *= NormalScale;
    sampledNormalTS = normalize(sampledNormalTS);
    
    float4 sampledMask = SAMPLE_TEXTURE2D(MaskTex, SS, parallaxUV);

    // 4. MOHR 데이터 처리
    float finalMetallic = saturate(sampledMask.r * MetallicScale);
    float baseRoughness = sampledMask.a;
    float finalRoughness = saturate(baseRoughness * RoughnessScale);
    
    float heightShadow = saturate(sampledMask.b + 0.2);
    float finalOcclusion = saturate(sampledMask.g * heightShadow * OcclusionScale);

    // 5. 월드 노멀 확정
    float3 worldNormal = normalize(T * sampledNormalTS.x + B * sampledNormalTS.y + N * sampledNormalTS.z);

    // 6. Roughness 및 Smoothness 결정
    float geoRough = GetGeometricRoughness(worldNormal, SpecularAASize);
    float actualRoughness = max(finalRoughness, geoRough);
    float smoothness = 1.0 - actualRoughness;

    float3 resultColor = float3(0, 0, 0);
    
    // 환경광 주입 (블랙아웃 방지)
    float3 ambient = SampleSH(worldNormal) * sampledAlbedo;
    resultColor += ambient;

    float4 shadowCoord = TransformWorldToShadowCoord(WorldPos);
    
    // --- [A] 직접광: 메인 라이트 ---
    Light mainLight = GetMainLight(shadowCoord);
    resultColor += sampledAlbedo * ApplyDreamcoreRampWithAlpha(
        worldNormal, mainLight.direction, mainLight.shadowAttenuation, mainLight.distanceAttenuation, mainLight.color, sampledAlbedo,
        LocalRampTex, SS, LocalRampSteps, LocalRampGamma, LocalMadness // 로컬 파라미터 전달
    );

    if (SpecularToggle > 0.5)
    {
        float3 halfDir = normalize(mainLight.direction + viewDirWS);
        float NdotH = saturate(dot(worldNormal, halfDir));
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
        resultColor += sampledAlbedo * ApplyDreamcoreRampWithAlpha(
            worldNormal, light.direction, light.shadowAttenuation, light.distanceAttenuation, light.color, sampledAlbedo,
            LocalRampTex, SS, LocalRampSteps, LocalRampGamma, LocalMadness // 로컬 파라미터 전달
        );
        
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