Shader "CaveSystem/CaveDreamcoreTerrain"
{
    Properties
    {
        [Header(Debug Tools)]
        [KeywordEnum(None, Normals, Shadows, Occlusion, IndirectGI)] _DebugView ("Debug View", Float) = 0

        [Header(Triplanar Biome Textures)]
        _Tiling ("Triplanar Tiling", Float) = 0.2
        _HeightScale ("Height Scale (POM)", Float) = 0.05
        _NormalScale ("Normal Scale", Float) = 1.0
        
        [Header(POM Settings)]
        [Toggle] _EnableSafePom ("Enable Safe POM", Float) = 1.0
        [Toggle] _EnablePomFading ("Enable POM Fading", Float) = 1.0
        _PomFadeStart ("POM Fade Start", Float) = 10.0
        _PomFadeEnd ("POM Fade End", Float) = 30.0
        
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

        [Header(Dreamcore PBR Controls)]
        _MetallicScale ("Metallic Scale", Range(0,1)) = 1.0
        _OcclusionScale ("Occlusion Scale", Range(0,1)) = 1.0
        _SpecularToggle ("Enable Specular", Float) = 1.0
        _ReflectionToggle ("Enable Reflection", Float) = 1.0
        
        [Header(Lighting and Soft Shading)]
        _RampTex ("Lighting Ramp (Optional)", 2D) = "white" {}
        
        [Header(Custom Cave Point Light Falloff)]
        _CaveLightFalloff ("Linear Falloff (Distance)", Range(0.001, 1.0)) = 0.05
        _CaveLightQuad ("Quadratic Falloff", Range(0.0001, 0.1)) = 0.005
    }

    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque" 
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry"
        }

        // =========================================================
        // 1. 메인 라이팅 패스 (ForwardLit)
        // =========================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // URP 라이팅 및 그림자 필수 매크로 
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS 
            
            // 베이킹 및 섀도우마스크 처리용 필수 매크로
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog

            // 디버그 뷰 매크로
            #pragma multi_compile _DEBUGVIEW_NONE _DEBUGVIEW_NORMALS _DEBUGVIEW_SHADOWS _DEBUGVIEW_OCCLUSION _DEBUGVIEW_INDIRECTGI

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CaveTriplanarSplat.hlsl"

            float _Tiling;
            float _HeightScale;
            float _NormalScale;
            float _EnableSafePom;
            float _EnablePomFading;
            float _PomFadeStart;
            float _PomFadeEnd;

            float _MetallicScale;
            float _OcclusionScale;
            float _SpecularToggle;
            float _ReflectionToggle;
            float _CaveLightFalloff;
            float _CaveLightQuad;

            TEXTURE2D(_RampTex);
            SAMPLER(sampler_RampTex);

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 lightmapUV   : TEXCOORD1; 
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float4 shadowCoord  : TEXCOORD3;
                float2 lightmapUV   : TEXCOORD4; 
            };

            // =================================================================================
            // [수정 완료] 퀀터사이즈(끊김) 공식을 버리고 부드러운 소프트 쉐이딩 함수로 교체
            // 램프 텍스처를 할당하지 않으면 기본 소프트 쉐이딩(Lambert)으로 작동합니다.
            // =================================================================================
            half3 CalculateSoftDiffuse(float NdotL, float shadowAtten, float distanceAtten, half3 lightColor) 
            {
                // 부드러운 빛의 확산(Lambert)
                float diffuse = saturate(NdotL);
                float finalAtten = shadowAtten * distanceAtten;
                
                // 램프 이미지가 비어있을 경우("white") 1.0을 반환하므로 결과에 영향이 없습니다.
                half3 rampColor = SAMPLE_TEXTURE2D(_RampTex, sampler_RampTex, float2(diffuse, 0.5)).rgb;
                
                // 부드러운 쉐이딩 계산 반환
                return lightColor * (diffuse * finalAtten) * rampColor;
            }

            half3 CalculateSpecular(float3 lightDir, float3 viewDirWS, float3 worldNormal, float smoothness, float3 albedo, float metallic, float3 lightColor, float distanceAtten, float shadowAtten, float NdotL)
            {
                float3 halfDir = normalize(lightDir + viewDirWS);
                float NdotH = saturate(dot(worldNormal, halfDir));
                float specPower = exp2(10.0 * smoothness + 1.0);
                float spec = pow(NdotH, specPower);
                
                return spec * lerp(0.04, albedo, metallic) * lightColor * distanceAtten * shadowAtten * saturate(NdotL);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.shadowCoord = GetShadowCoord(vertexInput);

                #if defined(LIGHTMAP_ON)
                    output.lightmapUV = input.lightmapUV * unity_LightmapST.xy + unity_LightmapST.zw;
                #else
                    output.lightmapUV = float2(0,0);
                #endif

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 worldNormal = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                float3 sampledAlbedo = float3(1,1,1);
                float3 triNormal = worldNormal;
                float4 MOHR = float4(0,1,0,1); 
                
                // 트라이플래너 데이터 수신 (각 텍스처 개별 노멀맵 및 속성 병합)
                GetCaveSurfaceData(
                    input.positionWS, worldNormal, viewDirWS,
                    _Tiling, _HeightScale, _NormalScale,
                    _EnableSafePom, _EnablePomFading, _PomFadeStart, _PomFadeEnd,
                    sampledAlbedo, triNormal, MOHR
                );
                
                // 병합된 정교한 노멀을 라이팅 기준 노멀로 오버라이드 (개별 노멀맵 완벽 작동)
                worldNormal = normalize(triNormal);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = worldNormal;
                inputData.viewDirectionWS = viewDirWS;
                inputData.shadowCoord = input.shadowCoord;
                #if defined(_FORWARD_PLUS)
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                #endif

                float finalMetallic = MOHR.r * _MetallicScale;
                float finalOcclusion = lerp(1.0, MOHR.g, _OcclusionScale);
                float actualRoughness = MOHR.a;
                float smoothness = 1.0 - actualRoughness;

                // [디버깅] 노멀 확인 뷰
                #if _DEBUGVIEW_NORMALS
                    return half4(worldNormal * 0.5 + 0.5, 1.0);
                #endif

                // [디버깅] 오클루전(AO) 뷰
                #if _DEBUGVIEW_OCCLUSION
                    return half4(finalOcclusion.xxx, 1.0);
                #endif

                sampledAlbedo *= finalOcclusion;
                float3 resultColor = float3(0,0,0);

                // 라이트맵 및 섀도우마스크 샘플링
                half4 shadowMask = half4(1, 1, 1, 1);
                #if defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON)
                    shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
                #elif defined(SHADOWS_SHADOWMASK)
                    shadowMask = unity_ProbesOcclusion;
                #endif

                #if defined(LIGHTMAP_ON)
                    float3 indirectDiffuse = SampleLightmap(input.lightmapUV, worldNormal);
                #else
                    float3 indirectDiffuse = SampleSH(worldNormal);
                #endif
                
                // [디버깅] 간접광(GI) 뷰
                #if _DEBUGVIEW_INDIRECTGI
                    return half4(indirectDiffuse * sampledAlbedo, 1.0);
                #endif
                
                indirectDiffuse *= sampledAlbedo;
                resultColor += indirectDiffuse;

                // ---------------------------------------------------------------------------------
                // 메인 라이트 (디렉셔널)
                // ---------------------------------------------------------------------------------
                Light mainLight = GetMainLight(input.shadowCoord, input.positionWS, shadowMask);
                float mainNdotL = dot(worldNormal, mainLight.direction);
                
                // [디버깅] 메인 그림자 뷰
                #if _DEBUGVIEW_SHADOWS
                    return half4(mainLight.shadowAttenuation.xxx, 1.0);
                #endif
                
                float3 mainLightDiffuse = CalculateSoftDiffuse(mainNdotL, mainLight.shadowAttenuation, mainLight.distanceAttenuation, mainLight.color);
                resultColor += sampledAlbedo * mainLightDiffuse;

                if (_SpecularToggle > 0.5) 
                {
                    resultColor += CalculateSpecular(mainLight.direction, viewDirWS, worldNormal, smoothness, sampledAlbedo, finalMetallic, mainLight.color, mainLight.distanceAttenuation, mainLight.shadowAttenuation, mainNdotL);
                }

                // ---------------------------------------------------------------------------------
                // 추가 라이트 (포인트/스팟)
                // ---------------------------------------------------------------------------------
                #if defined(_ADDITIONAL_LIGHTS) || defined(_FORWARD_PLUS)
                uint pixelLightCount = GetAdditionalLightsCount();
                
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                    
                    float3 lightPos = _AdditionalLightsPosition[lightIndex].xyz;
                    float3 lightVec = lightPos - input.positionWS;
                    float distance = length(lightVec);
                    float isLocalLight = _AdditionalLightsPosition[lightIndex].w;
                    
                    float customDistAtten = 1.0 / (1.0 + (_CaveLightFalloff * distance) + (_CaveLightQuad * (distance * distance)));
                    
                    float softEdgeFade = saturate(light.distanceAttenuation * 20.0);
                    float finalDistAtten = lerp(light.distanceAttenuation, customDistAtten * softEdgeFade, isLocalLight);
                    
                    float NdotL = dot(worldNormal, light.direction);

                    float3 directLightDiffuse = CalculateSoftDiffuse(NdotL, light.shadowAttenuation, finalDistAtten, light.color);
                    resultColor += sampledAlbedo * directLightDiffuse;
                    
                    if (_SpecularToggle > 0.5) 
                    {
                        resultColor += CalculateSpecular(light.direction, viewDirWS, worldNormal, smoothness, sampledAlbedo, finalMetallic, light.color, finalDistAtten, light.shadowAttenuation, NdotL);
                    }
                LIGHT_LOOP_END
                #endif

                float3 reflectionColor = float3(0, 0, 0);
                if (_ReflectionToggle > 0.5)
                {
                    float3 reflectDir = reflect(-viewDirWS, worldNormal);
                    reflectionColor = GlossyEnvironmentReflection(reflectDir, actualRoughness, 1.0) * finalOcclusion;
                    reflectionColor *= lerp(float3(0.04, 0.04, 0.04), sampledAlbedo, finalMetallic);
                }

                float3 finalRGB = resultColor + reflectionColor;
                
                float fogFactor = ComputeFogFactor(input.positionCS.z);
                finalRGB = MixFog(finalRGB, fogFactor);

                return half4(finalRGB, 1.0);
            }
            ENDHLSL
        }

        // =========================================================
        // 2. 그림자 캐스팅 패스 (ShadowCaster)
        // =========================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_SHADOW_MAP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // [수정 완료] URP 엔진의 전달 규격과 맞추기 위해 정밀도를 float4로 상향 (버퍼 밀림 및 그림자 증발 방지)
            float4 _LightDirection;
            float4 _LightPosition;

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
            };

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                #if _CASTING_PUNCTUAL_SHADOW_MAP
                    float3 lightDirectionWS = normalize(_LightPosition.xyz - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection.xyz;
                #endif
                
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }

        // =========================================================
        // 3. 깊이 전용 패스 (DepthOnly)
        // =========================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }

        // =========================================================
        // 4. 깊이 및 노멀 패스 (DepthNormals) - SSGI 연산의 핵심
        // =========================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CaveTriplanarSplat.hlsl"

            float _Tiling;
            float _HeightScale;
            float _NormalScale;
            float _EnableSafePom;
            float _EnablePomFading;
            float _PomFadeStart;
            float _PomFadeEnd;

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_TARGET
            {
                float3 worldNormal = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                
                float3 sampledAlbedo = float3(1,1,1);
                float3 triNormal = worldNormal;
                float4 MOHR = float4(0,1,0,1);
                
                GetCaveSurfaceData(
                    input.positionWS, worldNormal, viewDirWS,
                    _Tiling, _HeightScale, _NormalScale,
                    _EnableSafePom, _EnablePomFading, _PomFadeStart, _PomFadeEnd,
                    sampledAlbedo, triNormal, MOHR
                );
                worldNormal = normalize(triNormal);

                return half4(worldNormal, 0.0);
            }
            ENDHLSL
        }
    }
}