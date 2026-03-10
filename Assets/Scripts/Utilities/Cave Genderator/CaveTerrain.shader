Shader "CaveSystem/CaveTerrain"
{
    Properties
    {
        [Header(Textures RGB Albedo and A Roughness)]
        _DirtAlbedo ("Dirt Albedo", 2D) = "white" {}
        _RockAlbedo ("Rock Albedo", 2D) = "grey" {}
        _MossAlbedo ("Moss Albedo", 2D) = "green" {}
        
        [Header(Normals)]
        _DirtNormal ("Dirt Normal", 2D) = "bump" {}
        _RockNormal ("Rock Normal", 2D) = "bump" {}

        [Header(Settings)]
        _Tiling ("Triplanar Tiling", Float) = 0.1
        _WaterLevel ("Water Level", Float) = -10.0
        _WetHeight ("Wet Height Margin", Float) = 2.0
        _WetDarken ("Wet Darken Multiplier", Range(0,1)) = 0.5

        [Header(Custom Lighting)]
        _PointLightFalloff ("Point Light Attenuation Falloff", Range(0.1, 1.0)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        // =========================================================
        // PASS 1: Universal Forward (메인 라이팅 패스)
        // =========================================================
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            // URP 기본 키워드
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            
            // 프로세듀럴 지형(런타임)이므로 베이크 라이트맵 키워드는 생략합니다.

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Tiling;
                float _WaterLevel;
                float _WetHeight;
                float _WetDarken;
                float _PointLightFalloff;
            CBUFFER_END

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
                float3 albedo = 0;
                float3 normal = 0;
                float4 mohr = 0; 

                // [에러 해결 1단계] input 데이터를 수정 가능한 지역 변수로 빼냅니다.
                float3 posWS = input.positionWS;
                float3 normWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetCameraPositionWS() - posWS);

                // [에러 해결 2단계] _Tiling 및 숫자 리터럴들로 인해 발생하는 const 에러 차단
                // GetCaveSurfaceData 함수가 inout을 요구하더라도 에러가 발생하지 않도록
                // 모든 상수를 수정 가능한 지역 변수(Local Variable)로 선언하여 전달합니다.
                float p_Tiling = _Tiling;
                float p_HeightScale = 0.05;
                float p_NormalScale = 1.0;
                float p_EnablePom = 0.0;
                float p_PomStart = 15.0;
                float p_PomEnd = 25.0;

                GetCaveSurfaceData(
                    posWS, normWS, viewDirWS, 
                    p_Tiling, p_HeightScale, p_NormalScale, p_EnablePom, p_PomStart, p_PomEnd, 
                    albedo, normal, mohr
                );

                // 데이터 확정
                normWS = normalize(normal);
                float metallic = saturate(mohr.r);
                float smoothness = 1.0 - saturate(mohr.a);
                float occlusion = saturate(mohr.g);

                // 런타임 지형을 위해 스카이박스/글로벌 앰비언트(SH)를 직접 샘플링합니다.
                half3 bakedGI = SampleSH(normWS);

                BRDFData brdfData;
                InitializeBRDFData(albedo, metallic, float3(0,0,0), smoothness, 1.0, brdfData);

                // 1. 간접광(GI) 연산
                half3 finalColor = GlobalIllumination(brdfData, bakedGI, occlusion, posWS, normWS, viewDirWS);

                float4 shadowCoord = TransformWorldToShadowCoord(posWS);

                // 2. 메인 라이트 연산
                Light mainLight = GetMainLight(shadowCoord);
                finalColor += LightingPhysicallyBased(brdfData, mainLight.color, mainLight.direction, 
                                                      mainLight.distanceAttenuation * mainLight.shadowAttenuation, 
                                                      normWS, viewDirWS);

                // 3. 포인트(추가) 라이트 연산 및 감쇄 곡선 해킹
                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                for (uint i = 0u; i < pixelLightCount; ++i)
                {
                    Light light = GetAdditionalLight(i, posWS, shadowCoord);
                    half customAtten = saturate(pow(light.distanceAttenuation, _PointLightFalloff));
                    finalColor += LightingPhysicallyBased(brdfData, light.color, light.direction, 
                                                          customAtten * light.shadowAttenuation, 
                                                          normWS, viewDirWS);
                }
                #endif

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // =========================================================
        // PASS 2: DepthNormals (SSGI 작동 필수)
        // =========================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            CBUFFER_START(UnityPerMaterial)
                float _Tiling;
                float _WaterLevel;
                float _WetHeight;
                float _WetDarken;
                float _PointLightFalloff;
            CBUFFER_END

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
                float3 albedo = 0;
                float3 normal = 0;
                float4 mohr = 0; 

                float3 posWS = input.positionWS;
                float3 normWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetCameraPositionWS() - posWS);

                // [에러 해결] DepthNormals 패스에서도 동일하게 로컬 변수화 처리
                float p_Tiling = _Tiling;
                float p_HeightScale = 0.05;
                float p_NormalScale = 1.0;
                float p_EnablePom = 0.0;
                float p_PomStart = 15.0;
                float p_PomEnd = 25.0;

                GetCaveSurfaceData(
                    posWS, normWS, viewDirWS, 
                    p_Tiling, p_HeightScale, p_NormalScale, p_EnablePom, p_PomStart, p_PomEnd, 
                    albedo, normal, mohr
                );

                return half4(normalize(normal), 0.0);
            }
            ENDHLSL
        }

        // =========================================================
        // PASS 3: DepthOnly
        // =========================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            half4 frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        // =========================================================
        // PASS 4: ShadowCaster
        // =========================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            float3 _LightDirection;

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                return output;
            }
            half4 frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}