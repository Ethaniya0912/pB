/*
    ================================================================================
    [File]          MultiLayerIDMap.shader
    [Role]          Masking (마스킹) - Render Objects 피처용 오버라이드 셰이더
    [Version]       3.0 (SRP Batcher & Instancing 최적화)
    [Last Updated]  2026.02.12
    [Description]   
        - CBUFFER_START 블록을 추가하여 SRP Batcher 병목을 해결했습니다.
    ================================================================================
*/

Shader "Hidden/Dreamcore/MultiLayerIDMap"
{
    Properties
    {
        _IDColor("ID Color", Color) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite On
        ZTest LEqual
        Cull Back

        Pass
        {
            Name "IDMap_Write"
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // [Fix 1] SRP Batcher 호환성을 위한 CBUFFER 선언
            CBUFFER_START(UnityPerMaterial)
                float4 _IDColor;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return float4(_IDColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}