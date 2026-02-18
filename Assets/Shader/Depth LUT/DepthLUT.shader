Shader "Hidden/Dreamcore/EnvironmentLUT_Ultimate"
{
    Properties
    {
        // 렌더 오브젝트에서는 _BlitTexture 대신 _CameraOpaqueTexture를 직접 사용하므로 이 프로퍼티는 보조용으로 둡니다.
        [HideInInspector] _MainTex("Source Texture", 2D) = "white" {}
        
        [Header(Debug Settings)]
        _DebugMode("Debug Mode (0:LUT, 1:Orig, 2:Depth, 3:Mask, 5:Blue)", Float) = 0
        
        [Header(LUT Settings)]
        _WarmLUT("Warm LUT (Near)", 3D) = "white" {}
        _ColdLUT("Cold LUT (Far)", 3D) = "white" {}
        _EffectIntensity("Effect Intensity", Range(0, 1)) = 1.0 
        _LUTSize("LUT Size", Float) = 32.0
        
        [Header(Black Point Protection)]
        _BlackPreservation("Black Point Preservation", Range(0, 1)) = 0.5
        
        [Header(Distance Settings)]
        _DistStart("Distance Start", Float) = 0.0
        _DistRange("Distance Range", Float) = 20.0
        _LogIntensity("Log Intensity", Float) = 9.0
        _FarSaturation("Far Saturation", Float) = 0.4
    }

    SubShader
    {
        // 중요: 스텐실을 사용하는 레이어이므로 Transparent 큐에서 그려지도록 설정
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        LOD 100
        ZTest Always ZWrite Off Cull Off
        // 블렌딩 설정을 추가하여 기존 화면과 자연스럽게 합성되도록 합니다.
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "DreamcoreEnvLUTPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Blit.hlsl 대신 아래 라이브러리들을 사용합니다.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            // 렌더 오브젝트에서 Vert 함수를 직접 정의해야 할 수 있으므로 간단하게 작성합니다.
            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            TEXTURE3D(_WarmLUT);
            SAMPLER(sampler_WarmLUT);
            TEXTURE3D(_ColdLUT);
            SAMPLER(sampler_ColdLUT);
            
            float _DebugMode, _DistStart, _DistRange, _LogIntensity, _FarSaturation, _EffectIntensity, _LUTSize, _BlackPreservation;

            Varyings Vert(Attributes input) {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            float3 GetLUTCoords(float3 color) {
                float scale = (_LUTSize - 1.0) / _LUTSize;
                float offset = 1.0 / (2.0 * _LUTSize);
                return color * scale + offset;
            }

            float3 AdjustSaturation(float3 color, float saturation) {
                float luma = Luminance(color);
                return lerp(luma.xxx, color, saturation);
            }

            half4 Frag(Varyings input) : SV_Target {
                // 화면 좌표로부터 UV 추출 (Render Objects 대응)
                float2 uv = input.screenPos.xy / input.screenPos.w;
                
                // _BlitTexture 대신 URP 기본 제공 함수로 현재 화면 색상을 가져옵니다.
                float3 screenColor = SampleSceneColor(uv);

                if (_DebugMode > 4.5) return half4(0, 0, 1, 1);

                float rawDepth = SampleSceneDepth(uv);
                // 유효하지 않은 깊이값 처리
                if (rawDepth <= 0.00001) return half4(screenColor, 1.0);

                float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float distNorm = saturate((linearDepth - _DistStart) / _DistRange);
                float dreamFactor = saturate(log10(distNorm * _LogIntensity + 1.0));

                // Debug Modes
                if (_DebugMode > 1.5 && _DebugMode < 2.5) return half4((linearDepth / 50.0).xxx, 1.0); 
                if (_DebugMode > 2.5 && _DebugMode < 3.5) return half4(dreamFactor.xxx, 1.0);        
                if (_DebugMode > 0.5 && _DebugMode < 1.5) return half4(screenColor, 1.0);            

                // --- 컬러 연산 ---
                float3 lutInput = saturate(LinearToSRGB(screenColor));
                float3 lutCoords = GetLUTCoords(lutInput);

                // 3D LUT 샘플링
                float3 nearColor = _WarmLUT.Sample(sampler_LinearClamp, lutCoords).rgb;
                float3 farColor  = _ColdLUT.Sample(sampler_LinearClamp, lutCoords).rgb;

                float3 gradedColor = lerp(nearColor, farColor, dreamFactor);
                gradedColor = SRGBToLinear(gradedColor);
                gradedColor = AdjustSaturation(gradedColor, lerp(1.0, _FarSaturation, dreamFactor));

                // --- 암부 보호 로직 ---
                float sceneLuma = Luminance(screenColor);
                float blackMask = saturate(sceneLuma * lerp(100.0, 1.0, _BlackPreservation));
                float finalIntensity = _EffectIntensity * blackMask;

                float3 finalColor = lerp(screenColor, gradedColor, finalIntensity);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
