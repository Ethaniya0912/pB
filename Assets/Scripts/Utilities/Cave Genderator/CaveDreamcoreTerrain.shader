Shader "CaveSystem/CaveDreamcoreTerrain"
{
    Properties
    {
        [Header(Triplanar Biome Textures)]
        _Tiling ("Triplanar Tiling", Float) = 0.2
        
        [Space(10)]
        [NoScaleOffset] _DirtAlbedo ("Dirt Albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _DirtNormal ("Dirt Normal", 2D) = "bump" {}
        [NoScaleOffset] _DirtMask ("Dirt MOHR Mask (R:Met G:Occ B:Hgt A:Rgh)", 2D) = "white" {}

        [Space(10)]
        [NoScaleOffset] _RockAlbedo ("Rock Albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _RockNormal ("Rock Normal", 2D) = "bump" {}
        [NoScaleOffset] _RockMask ("Rock MOHR Mask", 2D) = "white" {}

        [Space(10)]
        [NoScaleOffset] _MossAlbedo ("Moss Albedo", 2D) = "white" {}
        [NoScaleOffset][Normal] _MossNormal ("Moss Normal", 2D) = "bump" {}
        [NoScaleOffset] _MossMask ("Moss MOHR Mask", 2D) = "white" {}

        // [수정] 기존 DreamcorePBR과 동일한 파라미터 노출 (Occlusion Scale 제한 해제)
        [Header(Dreamcore PBR Controls)]
        _MetallicScale ("Metallic Scale", Range(0,1)) = 1.0
        _OcclusionScale ("Occlusion Scale", Float) = 1.0
        _RoughnessScale ("Roughness Scale", Range(0,5)) = 1.0
        _HeightScale ("POM Height Scale", Float) = 0.05
        _NormalScale ("Normal Scale", Float) = 1.0
        
        [Toggle] _SpecularToggle ("Enable Specular", Float) = 1.0
        [Toggle] _ReflectionToggle ("Enable Reflection", Float) = 1.0
        _SpecularAASize ("Specular AA Size", Float) = 0.05

        [Header(Global Lighting is controlled by ShaderCoordinationManager)]
        [HideInInspector] _Dummy ("Dummy", Float) = 0

        [Header(Optimization)]
        [Toggle] _EnablePomFading ("Enable POM Fading (Opt 4)", Float) = 1.0
        _PomFadeStart ("POM Fade Start (m)", Float) = 15.0
        _PomFadeEnd ("POM Fade End (m)", Float) = 25.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        // DepthOnly 패스 (Opt 3 호환용)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            half4 DepthOnlyFragment(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
        
        // ShadowCaster 패스
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            struct Varyings { float4 positionCS : SV_POSITION; };
            float3 _LightDirection;

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                return output;
            }
            half4 ShadowPassFragment(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        // 메인 렌더 패스 (Dreamcore PBR 완벽 이식)
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // [🔥 핵심 수정] Properties 블록 밖에서 전역(Global) 변수로 선언해야 매니저의 방송을 정상적으로 수신합니다.
            TEXTURE2D(_GlobalRampTex); SAMPLER(sampler_GlobalRampTex);
            float _GlobalMadness;
            float _GlobalRampGamma;
            float _GlobalSteps;

            CBUFFER_START(UnityPerMaterial)
                float _Tiling;
                float _MetallicScale;
                float _OcclusionScale;
                float _RoughnessScale;
                float _HeightScale;
                float _NormalScale;
                
                float _SpecularToggle;
                float _ReflectionToggle;
                float _SpecularAASize;
                
                float _EnablePomFading;
                float _PomFadeStart;
                float _PomFadeEnd;
            CBUFFER_END

            // Triplanar 샘플링 함수 포함
            #include "CaveTriplanarSplat.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            // [모듈 1] Geometry Specular AA (기존 코드 이식)
            float GetGeometricRoughness(float3 worldNormal, float aaSize)
            {
                if (aaSize <= 0.0) return 0.0;
                float3 normalDDX = ddx(worldNormal);
                float3 normalDDY = ddy(worldNormal);
                return aaSize * (length(normalDDX) + length(normalDDY));
            }

            // [모듈 2] 알파 블렌딩 램프 해석기 (기존 코드 이식)
            float3 ApplyDreamcoreRampWithAlpha(float3 normal, float3 lightDir, float shadowAtten, float distAtten, float3 lightColor, float3 albedo)
            {
                float NdotL = saturate(dot(normal, lightDir));
                float totalInfluence = NdotL * distAtten * shadowAtten;
                
                float safeGamma = max(0.01, _GlobalRampGamma);
                float acceleratedInfluence = pow(max(0.0, totalInfluence), safeGamma);
                
                float quantizedLookup = (_GlobalSteps > 0.1) ? floor(acceleratedInfluence * _GlobalSteps) / _GlobalSteps : acceleratedInfluence;
                
                float2 rampUV = float2(quantizedLookup, _GlobalMadness);
                // RampTex에서 빛의 컬러를 결정
                float4 rampSample = SAMPLE_TEXTURE2D_LOD(_GlobalRampTex, sampler_GlobalRampTex, rampUV, 0);
                
                return lerp(NdotL * distAtten * shadowAtten * lightColor, rampSample.rgb * lightColor, rampSample.a);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);
                
                float3 sampledAlbedo, worldNormal;
                float4 sampledMask;
                
                // 1. Triplanar 및 POM을 통한 표면 데이터 추출
                GetCaveSurfaceData(
                    input.positionWS, normalize(input.normalWS), viewDirWS, 
                    _Tiling, _HeightScale, _NormalScale,
                    _EnablePomFading, _PomFadeStart, _PomFadeEnd,
                    sampledAlbedo, worldNormal, sampledMask
                );

                // 2. MOHR 데이터 처리 (DreamcorePBR과 완전히 동일)
                float finalMetallic = saturate(sampledMask.r * _MetallicScale);
                float baseRoughness = sampledMask.a;
                float finalRoughness = saturate(baseRoughness * _RoughnessScale);
                
                float heightShadow = saturate(sampledMask.b + 0.2);
                float finalOcclusion = saturate(sampledMask.g * heightShadow * _OcclusionScale);

                // 3. Roughness 및 Smoothness 결정
                float geoRough = GetGeometricRoughness(worldNormal, _SpecularAASize);
                float actualRoughness = max(finalRoughness, geoRough);
                float smoothness = 1.0 - actualRoughness;

                float3 resultColor = float3(0, 0, 0);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);

                // --- [A] 직접광: 메인 라이트 ---
                Light mainLight = GetMainLight(shadowCoord);
                resultColor += sampledAlbedo * ApplyDreamcoreRampWithAlpha(worldNormal, mainLight.direction, mainLight.shadowAttenuation, mainLight.distanceAttenuation, mainLight.color, sampledAlbedo);

                if (_SpecularToggle > 0.5)
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
                    Light light = GetAdditionalLight(i, input.positionWS, shadowCoord);
                    resultColor += sampledAlbedo * ApplyDreamcoreRampWithAlpha(worldNormal, light.direction, light.shadowAttenuation, light.distanceAttenuation, light.color, sampledAlbedo);
                    
                    if (_SpecularToggle > 0.5) 
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
                if (_ReflectionToggle > 0.5)
                {
                    float3 reflectDir = reflect(-viewDirWS, worldNormal);
                    reflectionColor = GlossyEnvironmentReflection(reflectDir, actualRoughness, finalOcclusion);
                    reflectionColor *= lerp(float3(0.04, 0.04, 0.04), sampledAlbedo, finalMetallic);
                }

                // 최종 합성
                float3 finalRGB = (resultColor * finalOcclusion) + reflectionColor;
                return half4(finalRGB, 1.0);
            }
            ENDHLSL
        }
    }
}