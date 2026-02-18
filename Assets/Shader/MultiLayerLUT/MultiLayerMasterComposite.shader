/*
    ================================================================================
    [File]          MultiLayerMasterComposite.shader
    [Role]          Flesh (살) - 드림코어 최종 합성 (Standard v17.0)
    [Version]       17.0 (Skybox Infinity Guard & TAA Zero-Point ★)
    [Last Updated]  2026.02.12
    [Description]   
        - [Fixed] 스카이박스(무한대)에서 LinearEyeDepth가 Infinity를 뱉어 TAA를 파괴하는 현상을 해결했습니다.
        - [Fixed] HDR Color Format 옵션이 없는 환경을 대비해 FP16/B10G11 링잉 현상을 수학적으로 방어합니다.
        - [Fixed] UV 연산 시 발생할 수 있는 미세한 오프셋 오차를 제거했습니다.
    ================================================================================
*/

Shader "Hidden/Dreamcore/MultiLayerMasterComposite"
{
    Properties
    {
        [NoScaleOffset] _BlitTexture("Source", 2D) = "white" {}
        _DreamcoreIDMap_Internal("Internal ID Map", 2D) = "black" {}
        _DefaultNeutralLUT("Neutral LUT", 2D) = "white" {}

        [Header(Environment LUTs)]
        _WorldLUT_Near("World LUT (Near)", 2D) = "white" {}
        _WorldLUT_Far("World LUT (Far)", 2D) = "white" {}

        [Header(Entity LUTs)]
        _HostileLUT("Hostile LUT", 2D) = "white" {}
        _POILUT("POI LUT", 2D) = "white" {}
        
        _DistanceLimit("Near/Far Threshold", Float) = 15.0
        _TransitionFactor("Effect Intensity", Range(0,1)) = 1.0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

    struct Varyings {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    TEXTURE2D_X(_BlitTexture); 
    TEXTURE2D(_DreamcoreIDMap_Internal);
    TEXTURE2D(_DefaultNeutralLUT);
    TEXTURE2D(_WorldLUT_Near);
    TEXTURE2D(_WorldLUT_Far);
    TEXTURE2D(_HostileLUT);
    TEXTURE2D(_POILUT);

    float4 _BlitScaleBias; 
    float _DistanceLimit, _TransitionFactor;

    // [New] 무결성 검사 함수: NaN, Inf, 음수값을 모두 소거하여 TAA를 보호합니다.
    float3 FilterInfinity(float3 c) {
        bool isBad = any(isnan(c)) || any(isinf(c));
        return isBad ? float3(0,0,0) : clamp(c, 0.0, 65504.0);
    }

    float3 SampleLUTSafe(TEXTURE2D_PARAM(tex, smp), float3 c, float3 sceneCol) {
        float2 dims;
        tex.GetDimensions(dims.x, dims.y);
        if (dims.x <= 16) return sceneCol;

        // 인덱스가 정확히 0 또는 1이 되어 텍스처 경계를 넘지 않도록 마진 적용
        float3 coords = clamp(c * 0.94 + 0.03, 0.001, 0.999); 
        float3 res = float3(
            SAMPLE_TEXTURE2D(tex, smp, float2(coords.r, 0.5)).r,
            SAMPLE_TEXTURE2D(tex, smp, float2(coords.g, 0.5)).g,
            SAMPLE_TEXTURE2D(tex, smp, float2(coords.b, 0.5)).b
        );
        return FilterInfinity(res);
    }

    Varyings vert(uint vertexID : SV_VertexID)
    {
        Varyings output;
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.uv = GetFullScreenTriangleTexCoord(vertexID);
        output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
        return output;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // [Step 1] 정확한 UV 보정 및 샘플링
                float2 uv = input.uv;
                float3 rawScene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float3 sceneCol = FilterInfinity(rawScene);
                
                // [Step 2] 깊이값 Infinity 가드 (무지개 제거의 핵심)
                float rawDepth = SampleSceneDepth(uv);
                
                // Unity 6의 Reverse-Z 환경에서 0은 Far(무한대)입니다.
                // 이 값이 LinearEyeDepth로 들어가면 Infinity가 되어 TAA를 오염시킵니다.
                #if UNITY_REVERSED_Z
                    // 0에 너무 가깝지 않게 강제 오프셋 (Far plane 붕괴 방지)
                    rawDepth = max(0.000001, rawDepth);
                #else
                    // 1에 너무 가깝지 않게 강제 오프셋
                    rawDepth = min(0.999999, rawDepth);
                #endif
                
                float depth = LinearEyeDepth(rawDepth, _ZBufferParams);
                
                // 만약 계산 결과가 여전히 비정상적이라면 안전 거리로 고정
                if(isnan(depth) || isinf(depth) || depth > 5000.0) depth = _DistanceLimit * 2.5;

                float distFactor = saturate(depth / max(0.01, _DistanceLimit));

                // [Step 3] ID Map 샘플링
                float4 idMask = SAMPLE_TEXTURE2D(_DreamcoreIDMap_Internal, sampler_LinearClamp, uv);
                if (any(isnan(idMask))) idMask = 0;

                float3 finalColor = sceneCol;

                // [Step 4] 레이어 합성 로직 (NaN 전파 차단)
                if(idMask.r > 0.6) { 
                    finalColor = sceneCol; 
                }
                else if(idMask.g > 0.6 && idMask.r < 0.4) { 
                    finalColor = SampleLUTSafe(TEXTURE2D_ARGS(_HostileLUT, sampler_LinearClamp), sceneCol, sceneCol);
                }
                else if(idMask.b > 0.6 && idMask.r < 0.4) { 
                    finalColor = SampleLUTSafe(TEXTURE2D_ARGS(_POILUT, sampler_LinearClamp), sceneCol, sceneCol);
                }
                else { 
                    float3 worldNear = SampleLUTSafe(TEXTURE2D_ARGS(_WorldLUT_Near, sampler_LinearClamp), sceneCol, sceneCol);
                    float3 worldFar = SampleLUTSafe(TEXTURE2D_ARGS(_WorldLUT_Far, sampler_LinearClamp), sceneCol, sceneCol);
                    finalColor = lerp(worldNear, worldFar, distFactor);
                }

                // [Step 5] 최종 출력 (TAA 피드백 루프 완전 파쇄)
                float3 result = lerp(sceneCol, FilterInfinity(finalColor), saturate(_TransitionFactor));
                return float4(FilterInfinity(result), 1.0);
            }
            ENDHLSL
        }
    }
}