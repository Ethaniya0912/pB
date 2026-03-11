Shader "CaveSystem/CaveDreamcoreTerrain"
{
    Properties
    {
        [Header(Debug Tools)]
        [KeywordEnum(None, Normals, Shadows, Occlusion, IndirectGI, Roughness, Specular)] _DebugView ("Debug View", Float) = 0

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

        [Header(Dreamcore PBR and Bodycam Settings)]
        _MetallicScale ("Metallic Scale", Range(0,1)) = 1.0
        _OcclusionScale ("Occlusion Scale", Range(0,1)) = 1.0
        
        [Space(5)]
        [Header(Specular Breakup and HDR)]
        _SpecularToggle ("Enable Specular", Float) = 1.0
        
        // [2단계를 위해 남겨둠] 현재는 비활성화 상태입니다.
        _RoughnessContrast ("Roughness Contrast (Breakup)", Range(0.1, 3.0)) = 1.5
        _SpecularHDROverdrive ("Specular HDR Overdrive", Range(1.0, 20.0)) = 5.0
        
        _ReflectionToggle ("Enable Reflection", Float) = 1.0
        
        [Header(Lighting and Soft Shading)]
        _RampTex ("Lighting Ramp (Optional)", 2D) = "white" {}
        _WrapDiffuse ("Light Wrap (Midtone)", Range(0.0, 0.5)) = 0.2
        
        [Header(Custom Cave Point Light Falloff)]
        _CaveLightFalloff ("Soft Falloff Power", Range(0.001, 1.0)) = 0.05
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

            // 디버그 뷰 매크로 (Roughness, Specular 포함)
            #pragma multi_compile _DEBUGVIEW_NONE _DEBUGVIEW_NORMALS _DEBUGVIEW_SHADOWS _DEBUGVIEW_OCCLUSION _DEBUGVIEW_INDIRECTGI _DEBUGVIEW_ROUGHNESS _DEBUGVIEW_SPECULAR

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
            
            // 새로운 바디캠 라이팅 프로퍼티
            float _RoughnessContrast;
            float _SpecularHDROverdrive;
            float _WrapDiffuse;
            float _CaveLightFalloff;

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
            // [1단계: 소프트 쉐이딩 도입] 넓은 중간톤(Midtone) 확보를 위한 Wrapped Soft Shading
            // =================================================================================
            half3 CalculateSoftDiffuse(float NdotL, float shadowAtten, float distanceAtten, half3 lightColor) 
            {
                // Wrap Diffuse 적용: 빛이 90도에서 칼같이 끊기지 않고 바위 곡면을 부드럽게 타고 넘어가게 만듭니다.
                // _WrapDiffuse 슬라이더(0.0 ~ 0.5)로 조절 가능합니다.
                float wrappedNdotL = saturate((NdotL + _WrapDiffuse) / (1.0 + _WrapDiffuse));
                
                float finalAtten = shadowAtten * distanceAtten;
                half3 rampColor = SAMPLE_TEXTURE2D(_RampTex, sampler_RampTex, float2(wrappedNdotL, 0.5)).rgb;
                
                return lightColor * (wrappedNdotL * finalAtten) * rampColor;
            }

            // =================================================================================
            // [1단계: 기본 스페큘러 유지] HDR 브레이크업은 다음 단계를 위해 주석 처리됨
            // =================================================================================
            half3 CalculateSpecular(float3 lightDir, float3 viewDirWS, float3 worldNormal, float smoothness, float3 albedo, float metallic, float3 lightColor, float distanceAtten, float shadowAtten, float NdotL)
            {
                float3 halfDir = normalize(lightDir + viewDirWS);
                float NdotH = saturate(dot(worldNormal, halfDir));
                
                float specPower = exp2(10.0 * smoothness + 1.0);
                float spec = pow(NdotH, specPower);
                
                /* [2단계를 위해 임시 비활성화]
                float microShadow = saturate(NdotL * 4.0); 
                float3 hdrSpecular = spec * _SpecularHDROverdrive;
                return hdrSpecular * lerp(0.04, albedo, metallic) * lightColor * distanceAtten * shadowAtten * microShadow;
                */

                // 기본 스페큘러 반환 (현실적인 역제곱 감쇠 유지)
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
                
                GetCaveSurfaceData(
                    input.positionWS, worldNormal, viewDirWS,
                    _Tiling, _HeightScale, _NormalScale,
                    _EnableSafePom, _EnablePomFading, _PomFadeStart, _PomFadeEnd,
                    sampledAlbedo, triNormal, MOHR
                );
                
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
                
                // =================================================================================
                // [1단계: 원본 러프니스 유지] 다음 단계에서 브레이크업 적용을 위해 준비
                // =================================================================================
                float baseRoughness = MOHR.a;
                float actualRoughness = baseRoughness;
                float smoothness = 1.0 - actualRoughness;

                // [디버깅] 노멀 뷰
                #if _DEBUGVIEW_NORMALS
                    return half4(worldNormal * 0.5 + 0.5, 1.0);
                #endif

                // [디버깅] 오클루전(AO) 뷰
                #if _DEBUGVIEW_OCCLUSION
                    return half4(finalOcclusion.xxx, 1.0);
                #endif
                
                // [디버깅] 러프니스 뷰
                #if _DEBUGVIEW_ROUGHNESS
                    return half4(actualRoughness.xxx, 1.0);
                #endif

                sampledAlbedo *= finalOcclusion;
                float3 resultColor = float3(0,0,0);
                
                // 스페큘러 합산 변수 (디버깅용)
                float3 totalSpecular = float3(0,0,0);

                half4 shadowMask = half4(1, 1, 1, 1);
                #if defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON)
                    shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
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
                
                // [디버깅] 섀도우 뷰
                #if _DEBUGVIEW_SHADOWS
                    return half4(mainLight.shadowAttenuation.xxx, 1.0);
                #endif
                
                float3 mainLightDiffuse = CalculateSoftDiffuse(mainNdotL, mainLight.shadowAttenuation, mainLight.distanceAttenuation, mainLight.color);
                resultColor += sampledAlbedo * mainLightDiffuse;

                if (_SpecularToggle > 0.5) 
                {
                    float3 spec = CalculateSpecular(mainLight.direction, viewDirWS, worldNormal, smoothness, sampledAlbedo, finalMetallic, mainLight.color, mainLight.distanceAttenuation, mainLight.shadowAttenuation, mainNdotL);
                    resultColor += spec;
                    totalSpecular += spec;
                }

                // ---------------------------------------------------------------------------------
                // 추가 라이트 (포인트/스팟) - Forward+ 호환 완벽 적용 및 소프트 확산
                // ---------------------------------------------------------------------------------
                #if defined(_ADDITIONAL_LIGHTS) || defined(_FORWARD_PLUS)
                uint pixelLightCount = GetAdditionalLightsCount();
                
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                    
                    // [수정 핵심] Forward+에서 0을 뱉던 버퍼 배열 의존성을 제거하고 URP 물리 감쇠를 재구성
                    // _CaveLightFalloff 슬라이더값을 Power 값으로 리맵핑하여 넓은 빛 확산을 만들어냅니다.
                    float falloffPower = max(0.1, _CaveLightFalloff * 10.0);
                    float finalDistAtten = pow(saturate(light.distanceAttenuation), falloffPower);
                    
                    float NdotL = dot(worldNormal, light.direction);

                    // 소프트 디퓨즈 연산 호출
                    float3 directLightDiffuse = CalculateSoftDiffuse(NdotL, light.shadowAttenuation, finalDistAtten, light.color);
                    resultColor += sampledAlbedo * directLightDiffuse;
                    
                    if (_SpecularToggle > 0.5) 
                    {
                        float3 spec = CalculateSpecular(light.direction, viewDirWS, worldNormal, smoothness, sampledAlbedo, finalMetallic, light.color, finalDistAtten, light.shadowAttenuation, NdotL);
                        resultColor += spec;
                        totalSpecular += spec;
                    }
                LIGHT_LOOP_END
                #endif

                // [디버깅] 스페큘러 뷰
                #if _DEBUGVIEW_SPECULAR
                    return half4(totalSpecular, 1.0);
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