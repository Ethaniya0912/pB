/*
    ================================================================================
    [File]          MultiLayerCharacter.shader
    [Role]          Bone (캐릭터) - 플레이어 및 NPC용 셰이더
    [Version]       3.0 (Ambient Light & SH 통합)
    [Last Updated]  2026.02.12
    [Description]   
        - 환경광 누락으로 인한 완전한 블랙아웃 현상을 방지합니다.
    ================================================================================
*/

Shader "Dreamcore/MultiLayerCharacter"
{
    Properties
    {
        [Header(Base)]
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _NormalMap("Normal", 2D) = "bump" {}
        
        [Header(Dreamcore Bone)]
        _RampTex("Ramp", 2D) = "white" {}
        _RimColor("Rim Light Color", Color) = (1, 0.5, 0.2, 1)
        _RimPower("Rim Power", Range(0.1, 10.0)) = 3.0
        _RimIntensity("Rim Intensity", Range(0, 5)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 300

        Pass
        {
            Name "CharacterLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_RampTex); SAMPLER(sampler_RampTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _RimColor;
                float _RimPower;
                float _RimIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : NORMAL;
                float3 viewDirWS : TEXCOORD3;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                float3 normalWS = normalize(input.normalWS); 
                float3 viewDir = normalize(input.viewDirWS);

                // [Fix 1] 환경광 (Ambient Light / SH) 계산
                float3 ambient = SampleSH(normalWS) * albedo.rgb;

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float3 rampColor = SAMPLE_TEXTURE2D(_RampTex, sampler_RampTex, float2(NdotL, 0.5)).rgb;

                float NdotV = saturate(dot(normalWS, viewDir));
                float rim = pow(max(0.001, 1.0 - NdotV), _RimPower) * _RimIntensity;
                float3 rimEmission = _RimColor.rgb * rim;

                // [Fix 2] 합성 시 ambient 추가
                float3 diffuse = albedo.rgb * rampColor * mainLight.color * mainLight.shadowAttenuation;
                float3 finalColor = ambient + diffuse + rimEmission;

                return float4(finalColor, albedo.a);
            }
            ENDHLSL
        }
        
        // ShadowCaster Pass는 이전과 동일
        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            
            Varyings vert(Attributes input) {
                Varyings output; UNITY_SETUP_INSTANCE_ID(input);
                // [Fix] URP 범용 호환성을 위해 직관적인 TransformObjectToHClip 함수로 교체하여 에러 해결
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            half4 frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}