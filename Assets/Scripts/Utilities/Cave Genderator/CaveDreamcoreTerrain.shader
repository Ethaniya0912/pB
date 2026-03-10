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
        // C# 코드 지우고 아래 한 줄 추가
        [Toggle] _EnableSafePom ("Enable Safe POM Mode", Float) = 1.0

        // [추가됨] 포인트 라이트 감쇄를 조절할 수 있는 슬라이더 노출
        [Header(Custom Lighting Controls)]
        _PointLightFalloff ("Point Light Attenuation Falloff", Range(0.1, 1.0)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        // =========================================================
        // PASS 1: DepthOnly 패스
        // =========================================================
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

        // =========================================================
        // PASS 2: DepthNormals 패스 (SSGI 작동을 위한 필수 패스 추가)
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
                float _HeightScale;
                float _NormalScale;
                float _EnablePomFading;
                float _PomFadeStart;
                float _PomFadeEnd;
                float _EnableSafePom;
            CBUFFER_END

            #include "CaveTriplanarSplat.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 albedo = 0; float3 normal = 0; float4 mohr = 0; 
                float3 posWS = input.positionWS;
                float3 normWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetCameraPositionWS() - posWS);

                // l-value const 에러를 막기 위한 지역 변수화
                float p_Tiling = _Tiling;
                float p_HeightScale = _HeightScale;
                float p_NormalScale = _NormalScale;
                float p_EnablePom = _EnablePomFading;
                float p_PomStart = _PomFadeStart;
                float p_PomEnd = _PomFadeEnd;

                GetCaveSurfaceData(
                    posWS, normWS, viewDirWS, 
                    p_Tiling, p_HeightScale, p_NormalScale, 
                    p_EnablePom, p_PomStart, p_PomEnd, 
                    albedo, normal, mohr
                );

                return half4(normalize(normal), 0.0);
            }
            ENDHLSL
        }
        
        // =========================================================
        // PASS 3: ShadowCaster 패스
        // =========================================================
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

        // =========================================================
        // PASS 4: UniversalForward 패스 (메인 렌더링)
        // =========================================================
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
                float _PointLightFalloff; // [추가됨] 감쇄 제어 변수
                float _EnableSafePom;
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

            float GetGeometricRoughness(float3 worldNormal, float aaSize)
            {
                if (aaSize <= 0.0) return 0.0;
                float3 normalDDX = ddx(worldNormal);
                float3 normalDDY = ddy(worldNormal);
                return aaSize * (length(normalDDX) + length(normalDDY));
            }

            float3 ApplyDreamcoreRampWithAlpha(float3 normal, float3 lightDir, float shadowAtten, float distAtten, float3 lightColor, float3 albedo)
            {
                float NdotL = saturate(dot(normal, lightDir));
                float totalInfluence = NdotL * distAtten * shadowAtten;
                
                float safeGamma = max(0.01, _GlobalRampGamma);
                float acceleratedInfluence = pow(max(0.0, totalInfluence), safeGamma);
                
                float quantizedLookup = (_GlobalSteps > 0.1) ? floor(acceleratedInfluence * _GlobalSteps) / _GlobalSteps : acceleratedInfluence;
                
                float2 rampUV = float2(quantizedLookup, _GlobalMadness);
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
                float3 posWS = input.positionWS;
                float3 normWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetCameraPositionWS() - posWS);
                
                float3 sampledAlbedo = 0; float3 worldNormal = 0; float4 sampledMask = 0;
                
                // 지역 변수 선언 (에러 방지)
                float p_Tiling = _Tiling;
                float p_HeightScale = _HeightScale;
                float p_NormalScale = _NormalScale;
                float p_EnablePom = _EnablePomFading;
                float p_PomStart = _PomFadeStart;
                float p_PomEnd = _PomFadeEnd;

                // 1. Triplanar 및 POM 데이터 추출
                GetCaveSurfaceData(
                    posWS, normWS, viewDirWS, 
                    p_Tiling, p_HeightScale, p_NormalScale,
                    p_EnablePom, p_PomStart, p_PomEnd,
                    sampledAlbedo, worldNormal, sampledMask
                );

                // 2. MOHR 데이터 처리
                float finalMetallic = saturate(sampledMask.r * _MetallicScale);
                float baseRoughness = sampledMask.a;
                float finalRoughness = saturate(baseRoughness * _RoughnessScale);
                
                float heightShadow = saturate(sampledMask.b + 0.2);
                float finalOcclusion = saturate(sampledMask.g * heightShadow * _OcclusionScale);

                float geoRough = GetGeometricRoughness(worldNormal, _SpecularAASize);
                float actualRoughness = max(finalRoughness, geoRough);
                float smoothness = 1.0 - actualRoughness;

                float3 resultColor = float3(0, 0, 0);
                float4 shadowCoord = TransformWorldToShadowCoord(posWS);

                // [추가됨] 런타임 환경의 칠흑 같은 암부를 방지하기 위한 기본 환경광(SH) 결합
                half3 bakedGI = SampleSH(worldNormal);
                resultColor += bakedGI * sampledAlbedo * finalOcclusion;

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

                // --- [B] 직접광: 추가 라이트 (포인트 라이트) ---
                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                for (uint i = 0u; i < pixelLightCount; ++i) 
                {
                    Light light = GetAdditionalLight(i, posWS, shadowCoord);
                    
                    // [핵심] 포인트 라이트의 거리 감쇄(Falloff) 곡선 조작
                    half customAtten = saturate(pow(light.distanceAttenuation, _PointLightFalloff));

                    // 조작된 customAtten을 Ramp 해석기에 전달
                    resultColor += sampledAlbedo * ApplyDreamcoreRampWithAlpha(worldNormal, light.direction, light.shadowAttenuation, customAtten, light.color, sampledAlbedo);
                    
                    if (_SpecularToggle > 0.5) 
                    {
                        float3 halfDir = normalize(light.direction + viewDirWS);
                        float NdotH = saturate(dot(worldNormal, halfDir));
                        float specPower = exp2(10.0 * smoothness + 1.0);
                        float spec = pow(NdotH, specPower);
                        
                        // 하이라이트(Specular)에도 동일하게 customAtten 적용
                        resultColor += spec * lerp(0.04, sampledAlbedo, finalMetallic) * light.color * (customAtten * light.shadowAttenuation);
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