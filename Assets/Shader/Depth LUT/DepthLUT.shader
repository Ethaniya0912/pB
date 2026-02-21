Shader "Hidden/Dreamcore/EnvironmentLUT_Ultimate"
{
    Properties
    {
        // 렌더 오브젝트에서는 _BlitTexture 대신 _CameraOpaqueTexture를 직접 사용하므로 이 프로퍼티는 보조용으로 둡니다.
        [HideInInspector] _MainTex("Source Texture", 2D) = "white" {}
        
        [Header(Debug Settings)]
        // [수정] 디버그 모드 4번(모션 벡터 확인)과 5번(속도 마스크 확인)을 추가했습니다.
        // [수정 전] _DebugMode("Debug Mode (0:LUT, 1:Orig, 2:Depth, 3:Mask, 5:Blue)", Float) = 0
        _DebugMode("Debug Mode (0:LUT, 1:Orig, 2:Depth, 3:LUT Mask, 4:Motion Map, 5:Speed Mask)", Float) = 0
        
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

        [Header(Motion Blur Effects)]
        // [수정] 에러를 유발하는 Tooltip 속성을 모두 삭제했습니다.
        // [추가] 모션 블러 전체 기능을 완전히 끄고 켤 수 있는 마스터 토글을 추가했습니다.
        [Toggle(_ENABLE_MOTION_BLUR)] _EnableMotionBlur("Enable Motion Blur (Master Switch)", Float) = 1 
        
        _BlurType("Blur Type (0:None, 1:Dither, 2:Directional, 3:Pixelate)", Float) = 0 
        _BlurStrength("Blur Strength", Range(0, 5)) = 1.0
        _BlurDownsample("Downsample Size (Pixelate)", Range(1, 32)) = 8.0
        
        [Header(Auto Motion Detection)]
        // [수정] 툴팁 삭제
        [Toggle(_USE_MOTION_MAP)] _UseMotionMap("Use Motion Vector Map", Float) = 1
        _MotionSensitivity("Motion Sensitivity", Range(0, 500)) = 150.0
        
        [Header(Manual Override)]
        // [수정] 툴팁 삭제
        _EnemySpeedFactor("Manual Speed Factor", Range(0, 1)) = 0.0
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
            
            // 토글 매크로 선언
            #pragma multi_compile_local _ _USE_MOTION_MAP
            // [추가] 모션 블러 마스터 토글용 매크로 선언
            #pragma multi_compile_local _ _ENABLE_MOTION_BLUR 
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

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
            
            // 엔진 내장 모션 벡터 텍스처 선언
            TEXTURE2D_X(_MotionVectorTexture);
            SAMPLER(sampler_MotionVectorTexture);
            
            float _DebugMode, _DistStart, _DistRange, _LogIntensity, _FarSaturation, _EffectIntensity, _LUTSize, _BlackPreservation;
            float _BlurType, _BlurStrength, _BlurDownsample;
            float _UseMotionMap, _MotionSensitivity, _EnemySpeedFactor;
            float _EnableMotionBlur; // [추가] 마스터 토글 변수

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
                float2 originalUV = input.screenPos.xy / input.screenPos.w;
                float2 uv = originalUV;
                
                // ---------------------------------------------------------
                // [오토 모션 디텍트] 픽셀 단위 속도 자체 판독
                // ---------------------------------------------------------
                float currentSpeedFactor = _EnemySpeedFactor;
                float2 motionVec = float2(0.0, 0.0); // [추가] 디버깅을 위해 스코프 밖으로 이동
                float autoSpeed = 0.0;               // [추가] 디버깅을 위해 스코프 밖으로 이동
                
                #if defined(_USE_MOTION_MAP)
                    // 현재 픽셀이 화면 공간에서 이동한 방향과 거리(벡터)를 읽어옵니다.
                    motionVec = SAMPLE_TEXTURE2D_X(_MotionVectorTexture, sampler_MotionVectorTexture, originalUV).rg;
                    
                    // 벡터의 길이(이동량)에 민감도를 곱해 0~1 사이의 속도 인자로 변환합니다.
                    autoSpeed = saturate(length(motionVec) * _MotionSensitivity);
                    
                    // 수동 조작값과 자동 계산값 중 더 높은 것을 사용합니다.
                    currentSpeedFactor = max(currentSpeedFactor, autoSpeed);
                #endif
                
                // ---------------------------------------------------------
                // [추가] 신규 디버그 모드 시각화 연산
                // ---------------------------------------------------------
                // Debug Mode 4: 모션 벡터 맵 자체를 화면에 출력 (안 움직이면 회색 0.5, 움직이면 RG 색상 발현)
                // 눈으로 보기 쉽게 모션 벡터 값에 50배를 곱하여 렌더링합니다.
                if (_DebugMode > 3.5 && _DebugMode < 4.5) return half4(motionVec.x * 50.0 + 0.5, motionVec.y * 50.0 + 0.5, 0.0, 1.0);
                
                // Debug Mode 5: 셰이더가 인식한 현재 픽셀의 속도(Speed Factor)를 출력 (가만히 있으면 검정, 빠르면 흰색)
                if (_DebugMode > 4.5 && _DebugMode < 5.5) return half4(currentSpeedFactor.xxx, 1.0);
                // ---------------------------------------------------------

                // 속도가 빠를수록 강해지는 최종 블러 강도
                float activeBlurStrength = _BlurStrength * currentSpeedFactor;
                float3 screenColor = float3(0, 0, 0);

                // [수정] 마스터 토글 분기 추가 (기능 끄기)
                #if defined(_ENABLE_MOTION_BLUR)
                    if (_BlurType < 0.5 || currentSpeedFactor <= 0.001)
                    {
                        // Type 0: None (기존 원본 로직)
                        screenColor = SampleSceneColor(uv);
                    }
                    else if (_BlurType < 1.5)
                    {
                        // Type 1: Dither Blur
                        float noiseX = frac(sin(dot(input.positionCS.xy + _Time.y * 100.0, float2(12.9898, 78.233))) * 43758.5453) - 0.5;
                        float noiseY = frac(sin(dot(input.positionCS.xy - _Time.y * 100.0, float2(39.346, 11.135))) * 43758.5453) - 0.5;
                        
                        float2 jitteredUV = uv + float2(noiseX, noiseY) * activeBlurStrength * 0.05;
                        screenColor = SampleSceneColor(jitteredUV);
                    }
                    else if (_BlurType < 2.5)
                    {
                        // Type 2: Fake Motion Blur
                        int samples = 5;
                        float2 blurDir = float2(0.0, 1.0) * activeBlurStrength * 0.03; 
                        
                        for(int i = 0; i < samples; i++)
                        {
                            float offset = ((float)i / (float)(samples - 1)) - 0.5;
                            screenColor += SampleSceneColor(uv + blurDir * offset);
                        }
                        screenColor /= (float)samples;
                    }
                    else
                    {
                        // Type 3: Pixelate
                        float2 pixelSize = max(1.0, _BlurDownsample * currentSpeedFactor) / _ScreenParams.xy;
                        float2 pixelatedUV = floor(uv / pixelSize) * pixelSize + (pixelSize * 0.5);
                        screenColor = SampleSceneColor(pixelatedUV);
                    }
                #else
                    // [추가] 마스터 토글이 꺼져있을 경우 무조건 원본 출력
                    screenColor = SampleSceneColor(uv);
                #endif

                // 기존 디버그 모드 유지 (에러 발생하던 5번 이상 방어 코드는 위로 통합 제거됨)
                // [수정 전] if (_DebugMode > 4.5) return half4(0, 0, 1, 1);

                float rawDepth = SampleSceneDepth(originalUV);
                
                if (rawDepth <= 0.00001) return half4(screenColor, 1.0);

                float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float distNorm = saturate((linearDepth - _DistStart) / _DistRange);
                float dreamFactor = saturate(log10(distNorm * _LogIntensity + 1.0));

                if (_DebugMode > 1.5 && _DebugMode < 2.5) return half4((linearDepth / 50.0).xxx, 1.0); 
                if (_DebugMode > 2.5 && _DebugMode < 3.5) return half4(dreamFactor.xxx, 1.0);        
                if (_DebugMode > 0.5 && _DebugMode < 1.5) return half4(screenColor, 1.0);            

                // --- 컬러 연산 ---
                float3 lutInput = saturate(LinearToSRGB(screenColor));
                float3 lutCoords = GetLUTCoords(lutInput);

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