Shader "CaveSystem/CaveDreamcoreTerrain"
{
    Properties
    {
        [Header(Debug Tools)]
        [KeywordEnum(None, Normals, Shadows, Occlusion, IndirectGI, Roughness, Specular, Height)] _DebugView ("Debug View", Float) = 0

        [Header(Triplanar Biome Textures)]
        _Tiling ("Triplanar Tiling", Float) = 0.2
        // [DIAG-FIX] HeightScale 0.05→0.15
        // 이전값 0.05(5cm)에서는 POM Self-Shadow threshold가 거의 트리거되지 않음
        // 0.15(15cm)부터 일반 록 텍스처에서 self-shadow가 가시적으로 작동
        // 0.20 이상은 POM 아티팩트(실루엣 끊김) 발생 가능 → 0.12~0.18 권장
        _HeightScale ("Height Scale (POM)", Float) = 0.15
        // [DIAG-FIX] NormalScale 1.0→1.5
        // DC Normal Baker 출력과 메쉬 버텍스 노멀이 동일 SDF 출처이므로
        // baseCorrectedNormal이 실질적으로 메쉬 노멀과 거의 동일함
        // 텍스처 범프의 라이팅 기여를 높이려면 normalScale을 직접 증가시키는 것이 효과적
        _NormalScale ("Normal Scale", Float) = 1.5
        
        [Header(POM Settings)]
        [Toggle] _EnableSafePom ("Enable Safe POM", Float) = 1.0
        [Toggle] _EnablePomFading ("Enable POM Fading", Float) = 1.0
        _PomFadeStart ("POM Fade Start", Float) = 10.0
        _PomFadeEnd ("POM Fade End", Float) = 30.0
        
        [Header(Dirt Height Mask)]
        _DirtFadeStart ("Dirt Fade Start Y", Float) = 0.0
        _DirtFadeEnd   ("Dirt Fade End Y",   Float) = 4.0

        [Header(DC SubVoxel Normal Maps)]
        [NoScaleOffset][Normal] _DCNormalMap_X ("DC Normal X", 2D) = "bump" {}
        [NoScaleOffset][Normal] _DCNormalMap_Y ("DC Normal Y", 2D) = "bump" {}
        [NoScaleOffset][Normal] _DCNormalMap_Z ("DC Normal Z", 2D) = "bump" {}
        // [TORCH] dcNormalStrength 0.35→0.7
        // 횃불 환경에서 DC Normal을 1순위 법선으로 사용
        // 맵 미할당 시 0.0으로 설정할 것
        _DCNormalStrength ("DC Normal Strength", Range(0,1)) = 0.7
        // [TORCH] dcNormalAmplify 신규 추가
        // Gram-Schmidt 각도 증폭 배율: DC Normal의 접선 편차를 N배로 증폭
        // 1.0 = lerp와 동일, 2.0~3.0 = 포인트라이트 NdotL 편차 실효화
        // DC Normal 편차가 클수록 낮게, 작을수록 높게 설정
        _DCNormalAmplify ("DC Normal Amplify (Point Light Contrast)", Range(1.0, 5.0)) = 3.0

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
        // 메인 라이트(디렉셔널)용 wrap — 동굴에서 거의 미사용
        _WrapDiffuse ("Main Light Wrap (Midtone)", Range(0.0, 0.5)) = 0.2
        // [TORCH] 포인트라이트 전용 wrap
        // 0.0 = 날카로운 terminator (화염 조명 최적) / 0.1~0.2 = 부드러운 전환
        _PointLightWrap ("Point Light Wrap (Torch Contrast)", Range(0.0, 0.3)) = 0.0
        
        [Header(Custom Cave Point Light Falloff)]
        _CaveLightFalloff ("Soft Falloff Power", Range(0.001, 1.0)) = 0.05
        
        [Header(POM Self Shadow)]
        [Toggle] _EnablePomShadow ("Enable POM Self Shadow", Float) = 1.0
        _PomShadowSteps ("Shadow Ray Steps (int)", Range(4, 16)) = 8
        // [DIAG-FIX] ShadowStrength 0.7 유지, HeightScale 증가 후 재조정 권장
        // HeightScale=0.08일 때 self-shadow가 거의 트리거 안 됨 (진단 확인)
        // HeightScale=0.15로 변경 후 이 값이 너무 강하면 0.4~0.5로 낮출 것
        _PomShadowStrength ("Shadow Strength", Range(0.0, 1.0)) = 0.7
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
            // [FIX-SHADOWCOORD] URP가 필요한 경우 vertex 보간 shadow coord를 자동 활성화
            #pragma multi_compile _ REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR
            
            // 베이킹 및 섀도우마스크 처리용 필수 매크로
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog

            // 디버그 뷰 매크로 (Height 포함)
            #pragma multi_compile _DEBUGVIEW_NONE _DEBUGVIEW_NORMALS _DEBUGVIEW_SHADOWS _DEBUGVIEW_OCCLUSION _DEBUGVIEW_INDIRECTGI _DEBUGVIEW_ROUGHNESS _DEBUGVIEW_SPECULAR _DEBUGVIEW_HEIGHT

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
            float _DirtFadeStart;
            float _DirtFadeEnd;
            float _DCNormalStrength;
            float _DCNormalAmplify;     // [TORCH] Gram-Schmidt 각도 증폭 배율

            float _MetallicScale;
            float _OcclusionScale;
            float _SpecularToggle;
            float _ReflectionToggle;
            
            // 새로운 바디캠 라이팅 프로퍼티
            float _RoughnessContrast;
            float _SpecularHDROverdrive;
            float _WrapDiffuse;
            float _PointLightWrap;      // [TORCH] 포인트라이트 전용 wrap
            float _CaveLightFalloff;
            float _EnablePomShadow;
            float _PomShadowSteps;
            float _PomShadowStrength;

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
                float4 positionCS       : SV_POSITION;
                float3 positionWS       : TEXCOORD0;
                float3 normalWS         : TEXCOORD1;
                // [FIX-SHADOWCOORD] shadowCoord 제거: per-fragment 계산으로 전환
                // GetShadowCoord(vertexInput) 버텍스 보간은 _MAIN_LIGHT_SHADOWS_SCREEN 활성 시
                // screen-space shadow를 positionCS(NDC) 기반으로 계산하지 못해 부정확.
                // fragment에서 TransformWorldToShadowCoord 또는 GetShadowCoord를 직접 사용.
                float2 lightmapUV       : TEXCOORD3;
                float3 blendWeightsVS   : TEXCOORD4;
            };

            // =================================================================================
            // [TORCH] 소프트 쉐이딩 — wrapAmount 파라미터화
            // 메인 라이트: _WrapDiffuse (동굴에서 거의 미사용)
            // 포인트라이트: _PointLightWrap = 0.0 → 횃불 날카로운 terminator
            // =================================================================================
            half3 CalculateSoftDiffuse(float NdotL, float shadowAtten, float distanceAtten, half3 lightColor, float wrapAmount) 
            {
                float wrappedNdotL = saturate((NdotL + wrapAmount) / (1.0 + wrapAmount));
                
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
                // [FIX-SHADOWCOORD] GetShadowCoord 제거 → frag에서 per-pixel 계산

                // [V3-FIX] blendWeights를 vertex에서 계산
                {
                    float3 n = abs(output.normalWS);
                    float3 bw = n * n * n * n; // pow4
                    output.blendWeightsVS = bw;
                }

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

                // [FIX-SHADOWCOORD] Forward+ URP 6 screen-space shadow 최적화
                // _MAIN_LIGHT_SHADOWS_SCREEN 활성 시 SV_POSITION 기반 UV가 가장 정확.
                // TransformWorldToShadowCoord(positionWS)는 world→clip 재연산 오차 발생.
                // GetNormalizedScreenSpaceUV(positionCS)는 래스터라이저 보간값 직접 사용.
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    float4 shadowCoord = float4(0,0,0,0); // (Varyings에 없으므로 미사용)
                #elif defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = float4(GetNormalizedScreenSpaceUV(input.positionCS), 0, 0);
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    float4 shadowCoord = float4(0, 0, 0, 1);
                #endif

                // [V3-FIX] 보간된 blendWeights 재정규화
                // vertex에서 pow4(abs(n))로 계산 후 보간 → 합≠1이 될 수 있으므로 재정규화
                float3 blendWeightsVS = input.blendWeightsVS;
                float bwSum = dot(blendWeightsVS, (float3)1.0);
                blendWeightsVS = bwSum < 0.0001 ? float3(0, 1, 0) : (blendWeightsVS / bwSum);

                float3 sampledAlbedo = float3(1,1,1);
                float3 triNormal = worldNormal;
                float4 MOHR = float4(0,1,0,1);
                float3 pomSamplePos = input.positionWS; // [POM-SHADOW] 변위 위치
                
                GetCaveSurfaceData(
                    input.positionWS, worldNormal, viewDirWS,
                    blendWeightsVS,
                    _Tiling, _HeightScale, _NormalScale,
                    _EnableSafePom, _EnablePomFading, _PomFadeStart, _PomFadeEnd,
                    _DirtFadeStart, _DirtFadeEnd,
                    _DCNormalStrength, _DCNormalAmplify,
                    sampledAlbedo, triNormal, MOHR, pomSamplePos
                );
                
                worldNormal = normalize(triNormal);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = worldNormal;
                inputData.viewDirectionWS = viewDirWS;
                inputData.shadowCoord = shadowCoord;
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

                // [디버깅] Height 뷰 — _RockMask.b 높이 채널 분포 확인용
                // 전체 흰색(1.0) = 높이 데이터 없음 → POM Self-Shadow 미작동 원인
                // 회색 그라데이션 = 정상 높이 데이터
                #if _DEBUGVIEW_HEIGHT
                    float dbgH = SampleTriplanar(_RockMask, sampler_RockMask, input.positionWS, blendWeightsVS, _Tiling).b;
                    return half4(dbgH.xxx, 1.0);
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
                // 메인 라이트 (디렉셔널) — 동굴 환경에서 거의 미사용
                // GetMainLight(InputData, shadowMask) 오버로드는 이 URP 버전에 없음 → 3파라미터 유지
                // shadowCoord는 이미 위에서 _MAIN_LIGHT_SHADOWS_SCREEN 분기로 정확히 계산됨
                // ---------------------------------------------------------------------------------
                Light mainLight = GetMainLight(shadowCoord, input.positionWS, shadowMask);
                float mainNdotL = dot(worldNormal, mainLight.direction);
                
                // [디버깅] 섀도우 뷰
                #if _DEBUGVIEW_SHADOWS
                    return half4(mainLight.shadowAttenuation.xxx, 1.0);
                #endif
                
                float3 mainLightDiffuse = CalculateSoftDiffuse(mainNdotL, mainLight.shadowAttenuation, mainLight.distanceAttenuation, mainLight.color, _WrapDiffuse);
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
                    
                    float falloffPower = max(0.1, _CaveLightFalloff * 10.0);
                    float finalDistAtten = pow(saturate(light.distanceAttenuation), falloffPower);
                    
                    float NdotL = dot(worldNormal, light.direction);

                    // [POM-SHADOW] 포인트라이트별 POM 자기 그림자
                    // samplePos에서 light.direction으로 레이를 트레이스해 마이크로 차폐 계산
                    // steps는 int로 변환 (clamp 4~16)
                    float pomShadowFactor = 1.0;
                    if (_EnablePomShadow > 0.5 && NdotL > 0.0)
                    {
                        int shadowSteps = clamp((int)_PomShadowSteps, 4, 16);
                        float rawShadow = ComputePOMSelfShadow(
                            pomSamplePos, light.direction, worldNormal,
                            blendWeightsVS, _Tiling, _HeightScale, shadowSteps);
                        // _PomShadowStrength로 그림자 강도 조절 (1=최대, 0=무효)
                        pomShadowFactor = lerp(1.0, rawShadow, _PomShadowStrength);
                    }

                    float3 directLightDiffuse = CalculateSoftDiffuse(NdotL, light.shadowAttenuation, finalDistAtten, light.color, _PointLightWrap);
                    resultColor += sampledAlbedo * directLightDiffuse * pomShadowFactor;
                    
                    if (_SpecularToggle > 0.5) 
                    {
                        float3 spec = CalculateSpecular(light.direction, viewDirWS, worldNormal, smoothness, sampledAlbedo, finalMetallic, light.color, finalDistAtten, light.shadowAttenuation, NdotL);
                        resultColor += spec * pomShadowFactor;
                        totalSpecular += spec * pomShadowFactor;
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
            float _DirtFadeStart;
            float _DirtFadeEnd;
            float _DCNormalStrength;
            float _DCNormalAmplify;     // [TORCH] ForwardLit과 동기화

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
                float3 blendWeightsVS : TEXCOORD2; // [V3-FIX]
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                // [FIX-BLENDWEIGHTS-CONSISTENCY] pow8 → pow4: ForwardLit 패스와 동일하게 통일
                // 기존 pow8은 ForwardLit(pow4)보다 훨씬 날카롭게 축 전환 → SSAO/DepthNormals 불일치
                float3 n = abs(output.normalWS);
                output.blendWeightsVS = n * n * n * n; // pow4 (ForwardLit과 동일)
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_TARGET
            {
                float3 worldNormal = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                
                // [V3-FIX] 재정규화
                float3 blendWeightsVS = input.blendWeightsVS;
                float bwSum = dot(blendWeightsVS, (float3)1.0);
                blendWeightsVS = bwSum < 0.0001 ? float3(0, 1, 0) : (blendWeightsVS / bwSum);

                float3 sampledAlbedo = float3(1,1,1);
                float3 triNormal = worldNormal;
                float4 MOHR = float4(0,1,0,1);
                float3 dummySamplePos = input.positionWS; // DepthNormals는 POM shadow 불필요
                
                GetCaveSurfaceData(
                    input.positionWS, worldNormal, viewDirWS,
                    blendWeightsVS,
                    _Tiling, _HeightScale, _NormalScale,
                    _EnableSafePom, _EnablePomFading, _PomFadeStart, _PomFadeEnd,
                    _DirtFadeStart, _DirtFadeEnd,
                    _DCNormalStrength, _DCNormalAmplify,
                    sampledAlbedo, triNormal, MOHR, dummySamplePos
                );
                worldNormal = normalize(triNormal);

                return half4(worldNormal, 0.0);
            }
            ENDHLSL
        }
    }
}