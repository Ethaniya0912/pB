Shader "CaveSystem/CaveUndergroundWater"
{
    Properties
    {
        _BaseColor ("Water Surface Color", Color) = (0.1, 0.4, 0.5, 0.8)
        _DeepColor ("Deep Water Color", Color) = (0.01, 0.1, 0.2, 1.0)
        _AbsorptionFactor ("Depth Absorption (Beer's Law)", Float) = 0.5
        
        _WaveNormal ("Wave Normal Map", 2D) = "bump" {}
        _WaveSpeed ("Wave Speed", Vector) = (0.5, 0.2, -0.3, 0.4)
        _WaveScale ("Wave Scale", Float) = 0.1
        _Distortion ("Refraction Distortion", Float) = 0.05
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" }
        LOD 300

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // URP 17 깊이 및 불투명 텍스처 접근용 (SampleSceneDepth 포함)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _DeepColor;
                float _AbsorptionFactor;
                float4 _WaveSpeed;
                float _WaveScale;
                float _Distortion;
            CBUFFER_END

            TEXTURE2D(_WaveNormal); SAMPLER(sampler_WaveNormal);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.screenPos = ComputeScreenPos(vertexInput.positionCS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 화면 공간 좌표 정규화
                float2 screenUV = input.screenPos.xy / input.screenPos.w;

                // 1. 잔물결 애니메이션 (이중 교차 샘플링)
                float2 uv1 = input.positionWS.xz * _WaveScale + _Time.y * _WaveSpeed.xy;
                float2 uv2 = input.positionWS.xz * _WaveScale * 1.5 + _Time.y * _WaveSpeed.zw;
                
                float3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_WaveNormal, sampler_WaveNormal, uv1));
                float3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_WaveNormal, sampler_WaveNormal, uv2));
                float3 waveNormal = normalize(n1 + n2);

                // 2. 굴절 (Refraction)
                float2 distortedUV = screenUV + waveNormal.xy * _Distortion;
                half3 refractionColor = SampleSceneColor(distortedUV); // URP 내부 함수

                // 3. 깊이 기반 빛 감쇄 (Beer-Lambert Law)
                // URP 17 표준 SampleSceneDepth 사용 (cameraDepthTarget 직접 접근 배제)
                float rawDepth = SampleSceneDepth(distortedUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterEyeDepth = input.screenPos.w; // 현재 픽셀의 깊이
                
                float waterDepth = max(0.0, sceneEyeDepth - waterEyeDepth);

                // Beer's Law 공식: I = I0 * e^(-absorption * distance)
                float transmittance = exp(-waterDepth * _AbsorptionFactor);
                
                // 물 속으로 깊어질수록 DeepColor로 블렌딩
                half3 waterVolumeColor = lerp(_DeepColor.rgb, _BaseColor.rgb, transmittance);

                // 최종 색상: 굴절된 배경에 물 볼륨 색상을 곱하고, 수면 반사광(BaseColor)을 더함
                half3 finalColor = refractionColor * waterVolumeColor + (_BaseColor.rgb * 0.2);
                
                // 얕은 곳은 투명도를 낮춤
                half alpha = saturate((waterDepth * 2.0) + _BaseColor.a);

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}