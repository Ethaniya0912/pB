#ifndef DREAMCORE_PBR_EXTENDED_INCLUDED
#define DREAMCORE_PBR_EXTENDED_INCLUDED

// 유니티 URP 표준 라이브러리 포함
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

/**
 * [전역 변수 영역] 
 * C# 컨트롤러에서 주입하는 램프 LUT 관련 변수들입니다.
 */
#ifndef SHADERGRAPH_PREVIEW
    TEXTURE2D(_GlobalRampTex);
    SAMPLER(sampler_GlobalRampTex);
    float _GlobalMadness;     // LUT의 V 좌표 (Y축)
    float _GlobalRampGamma;   // 조명 대비 감마
    float _GlobalSteps;       // 계단 현상 단계 수
#endif

/**
 * [핵심 로직] 드림코어 램프 해석기 (색상 변이 모드)
 * 단순 밝기 조절이 아니라, 광량에 따라 LUT의 UV 좌표를 옮겨 색상을 바꿉니다.
 */
float3 ApplyDreamcoreRampShift(float3 normal, float3 lightDir, float shadowAtten, float distAtten, float3 lightColor)
{
    #ifdef SHADERGRAPH_PREVIEW
        return saturate(dot(normal, lightDir)) * lightColor;
    #else
        // 1. 빛의 총 영향력 계산 (각도 * 거리감쇄 * 그림자)
        // 이 값이 낮아질수록(멀어질수록) LUT의 왼쪽(어두운 색상 영역)을 참조하게 됩니다.
        float NdotL = saturate(dot(normal, lightDir));
        float totalInfluence = NdotL * distAtten * shadowAtten;
        
        // 2. 감마 보정으로 대비 조절
        float safeGamma = max(0.01, _GlobalRampGamma);
        float acceleratedInfluence = pow(totalInfluence, safeGamma);
        
        // 3. 계단 현상(Posterization) 적용
        float quantizedLookup;
        if (_GlobalSteps > 0.1) {
            quantizedLookup = floor(acceleratedInfluence * _GlobalSteps) / _GlobalSteps;
        } else {
            quantizedLookup = acceleratedInfluence;
        }
        
        // 4. LUT 샘플링 (U: 광량 기반, V: 매드니스 수치 기반)
        float2 rampUV = float2(quantizedLookup, _GlobalMadness);
        float3 rampColor = SAMPLE_TEXTURE2D_LOD(_GlobalRampTex, sampler_GlobalRampTex, rampUV, 0).rgb;
        
        // 5. 조명 색상과 합성
        return rampColor * lightColor;
    #endif
}

/**
 * [메인 함수] CalculateDreamcorePBR_float
 * Shader Graph의 Custom Function 노드에서 호출됩니다.
 */
void CalculateDreamcorePBR_float(
    float3 Albedo,
    float3 NormalTS,        // 노멀맵 (Tangent Space)
    float3 Tangent,         // World Tangent
    float3 Bitangent,       // World Bitangent
    float3 NormalWS,        // World Normal
    float Metallic,         
    float Smoothness,       
    float Occlusion,        
    float3 Emission,
    float3 WorldPos,
    float SpecularToggle,   // 1.0이면 활성화
    float ReflectionToggle, // 1.0이면 활성화
    out float3 FinalColor
)
{
    FinalColor = float3(0, 0, 0);

    #ifdef SHADERGRAPH_PREVIEW
        FinalColor = Albedo;
    #else
        // 1. 노멀 데이터 처리 (노멀맵 강도 및 변환)
        float3x3 tbn = float3x3(Tangent, Bitangent, NormalWS);
        float3 worldNormal = normalize(mul(NormalTS, tbn));
        
        float3 viewDir = normalize(_WorldSpaceCameraPos - WorldPos);
        float3 resultColor = float3(0, 0, 0);
        float perceptualRoughness = 1.0 - Smoothness;
        
        // --- [A] 메인 라이트 조명 계산 ---
        float4 shadowCoord = TransformWorldToShadowCoord(WorldPos);
        Light mainLight = GetMainLight(shadowCoord);
        
        // 램프 적용 (색상 변이 포함)
        float3 mainDiffuse = Albedo * ApplyDreamcoreRampShift(worldNormal, mainLight.direction, mainLight.shadowAttenuation, mainLight.distanceAttenuation, mainLight.color);
        resultColor += mainDiffuse;

        // --- [B] 추가 라이트 조명 계산 (포인트 라이트 등) ---
        #if defined(_ADDITIONAL_LIGHTS)
            uint pixelLightCount = GetAdditionalLightsCount();
            for (uint i = 0u; i < pixelLightCount; ++i)
            {
                Light light = GetAdditionalLight(i, WorldPos);
                
                // 디퓨즈(램프)
                float3 addDiffuse = Albedo * ApplyDreamcoreRampShift(worldNormal, light.direction, light.shadowAttenuation, light.distanceAttenuation, light.color);
                resultColor += addDiffuse;
                
                // 스펙큘러 하이라이트 (금속광)
                if (SpecularToggle > 0.5)
                {
                    float3 halfDir = normalize(light.direction + viewDir);
                    float spec = pow(saturate(dot(worldNormal, halfDir)), max(1.0, Smoothness * 128.0));
                    resultColor += spec * Metallic * light.color * (light.distanceAttenuation * light.shadowAttenuation);
                }
            }
        #endif

        // --- [C] 환경 반사 (Environment Reflections) ---
        float3 reflectionColor = float3(0, 0, 0);
        if (ReflectionToggle > 0.5)
        {
            float3 reflectDir = reflect(-viewDir, worldNormal);
            // IBL (Image Based Lighting) 샘플링
            reflectionColor = GlossyEnvironmentReflection(reflectDir, perceptualRoughness, Occlusion);
            // 메탈릭 수치에 따른 반사색 보간
            reflectionColor *= lerp(float3(0.04, 0.04, 0.04), Albedo, Metallic);
        }

        // --- [D] 최종 합성 ---
        // 조명 결과에 오클루전 적용 후 반사광과 에미시브 합산
        float3 finalLighting = (resultColor * Occlusion) + reflectionColor;
        FinalColor = finalLighting + Emission;
    #endif
}

#endif
