Shader "Hidden/Dreamcore/ScreenSpaceMotionBlur"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}

        [Header(Debug)]
        [KeywordEnum(Off, MotionVector, BlurVector, DepthMask, BlurStrength, FinalBlur)]
        _DebugMode ("Debug Mode", Float) = 0

        [Header(Blur Settings)]
        // expTime 공식 제거 — mv가 이미 1프레임 이동량이므로 직접 스케일 사용
        // 대조군 기준: 1~5 범위에서 충분한 강도
        [Range(0, 10)] _BlurScale ("Blur Scale (주요 강도 — 1~5 권장)", Float) = 3.0
        [Range(4, 16)] _SSMBSamples ("Samples (샘플 수)", Float) = 12
        [Range(0, 0.1)] _MaxBlurUV ("Max Blur UV (최대 블러 범위)", Float) = 0.04

        [Header(Radial Weight)]
        // 직진 이동 시 화면 엣지일수록 블러가 길어지는 방사형 가중치
        // 0 = 균일 블러, 2~4 = 대조군과 유사한 엣지 강조
        [Range(0, 6)] _RadialScale ("Radial Scale (방사형 강도)", Float) = 2.0

        [Header(Depth Mask)]
        // [FIX-1] 기본값 0.0 → 6.0. 0이면 초기화 시점에 전체 블러 취약점.
        [Range(0, 10)] _SSBlurDepthNear ("Depth Near (블러 제외 거리 m)", Float) = 6.0
        [Range(0.1, 5)] _SSBlurDepthFade ("Depth Fade Range (전환 범위 m)", Float) = 2.0

        [Header(Object Motion Separation)]
        // 이 값보다 큰 모션 벡터 픽셀 = 오브젝트 이동으로 간주 → 스크린스페이스 제외
        // 0.003~0.008 권장. 0이면 분리 없음
        [Range(0, 0.05)] _ObjMotionThreshold ("Object Motion Threshold (오브젝트 분리)", Float) = 0.005
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "ScreenSpaceMotionBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _DEBUGMODE_OFF _DEBUGMODE_MOTIONVECTOR _DEBUGMODE_BLURVECTOR _DEBUGMODE_DEPTHMASK _DEBUGMODE_BLURSTRENGTH _DEBUGMODE_FINALBLUR

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BlitTexture);        SAMPLER(sampler_BlitTexture);
            TEXTURE2D(_MotionVectorTexture); SAMPLER(sampler_MotionVectorTexture);

            // ── [ROOT CAUSE FIX] 머티리얼 파라미터를 CBUFFER로 격리 ──────────────
            // 진단 결과: _SSBlurDepthNear 글로벌값 = 0 (초기화 안 됨)
            // 원인: CBUFFER 없이 plain float 선언 → Unity가 글로벌값(0)을 우선 읽음
            //       → 머티리얼의 _SSBlurDepthNear=6 이 완전히 무시됨
            //       → depthMask=1.0 (전체 블러) → 캐릭터 보호 실패
            // 수정: CBUFFER_START(UnityPerMaterial) 로 격리
            //       → 글로벌 Shader.SetGlobalFloat 가 덮어쓸 수 없음
            //       → 머티리얼 값(6m)을 항상 정확히 읽음
            CBUFFER_START(UnityPerMaterial)
                float _BlurScale;
                float _SSMBSamples;
                float _MaxBlurUV;
                float _RadialScale;
                float _SSBlurDepthNear;   // 머티리얼 격리 — 글로벌 override 차단
                float _SSBlurDepthFade;   // 머티리얼 격리 — 글로벌 override 차단
                float _ObjMotionThreshold;
            CBUFFER_END

            // 전역 파라미터 (ShaderCoordinationManager 주입 — 의도적으로 글로벌)
            // 락온 시 배경 블러 강도 배율 (1.0=정상, 0.25=락온 중 억제)
            // UpdateLockOnBlur()에서 Shader.SetGlobalFloat("_SSMBIntensityScale") 주입
            float _SSMBIntensityScale;
            // P5: 거리별 블러 배율 (P5_DistanceBlurCurveInjector 주입)
            float _SSMBDistanceScale;
            // P7: Budget 배율 (P7_BlurBudgetManager 주입)
            float _SSMBBudgetScale;

            struct Attributes { uint vertexID : SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 texcoord : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                output.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
                #endif
                output.texcoord = uv;
                return output;
            }

            float3 VisualizeVector(float2 v, float scale)
            {
                float2 s = v * scale;
                float3 c = float3(0,0,0);
                c.r = max(0, s.x); c.b = max(0,-s.x); c.g = max(0, s.y);
                if (s.y < 0) { c.r += max(0,-s.y); c.b += max(0,-s.y); }
                return saturate(c);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // ── 1. 모션 벡터 읽기 ──────────────────────────────────────
                float2 mv    = SAMPLE_TEXTURE2D(_MotionVectorTexture,
                                   sampler_MotionVectorTexture, uv).rg;
                float  mvLen = length(mv);

                // ── Debug: 모션벡터 원시값 ─────────────────────────────────
                #if defined(_DEBUGMODE_MOTIONVECTOR)
                return float4(VisualizeVector(mv, 100.0), 1.0);
                #endif

                // ── 2. 오브젝트 이동 분리 ──────────────────────────────────
                // 모션 벡터가 임계값 이상 = 오브젝트가 크게 움직이는 픽셀
                // → 스크린스페이스 블러 제외 (Phase 3 오브젝트 블러가 담당)
                float4 original = SAMPLE_TEXTURE2D_LOD(
                    _BlitTexture, sampler_BlitTexture, uv, 0);
                if (_ObjMotionThreshold > 0.0 && mvLen > _ObjMotionThreshold)
                    return original;

                // ── 3. 블러 벡터 계산 — expTime 없이 직접 스케일 ──────────
                // _SSMBIntensityScale: 락온 시 배경 블러 억제 (SCM 주입)
                // 기본값 1.0 — CBUFFER 밖 전역이므로 주입 안 되면 자동으로 1.0
                float  intensityScale = max(_SSMBIntensityScale, 0.0);
                // 전역값이 0으로 초기화될 경우 폴백 (미주입 방지)
                if (intensityScale < 0.001) intensityScale = 1.0;
                // P5 거리 배율, P7 Budget 배율 적용 (미주입 시 0 → 1.0 폴백)
                float distScale   = max(_SSMBDistanceScale,  0.001) < 0.001 ? 1.0 : max(_SSMBDistanceScale,  0.0);
                float budgetScale = max(_SSMBBudgetScale,    0.001) < 0.001 ? 1.0 : max(_SSMBBudgetScale,    0.0);
                if (distScale   < 0.001) distScale   = 1.0;
                if (budgetScale < 0.001) budgetScale = 1.0;
                float2 blurVec = mv * _BlurScale * intensityScale * distScale * budgetScale;

                // ── 4. 방사형 가중치 적용 ──────────────────────────────────
                // 직진 이동 시 화면 중심=소실점, 엣지일수록 실제 시각적 이동이 크므로
                // 모션 벡터와 중심→엣지 방향이 일치하는 픽셀에 추가 가중치 부여
                float2 toEdge     = uv - float2(0.5, 0.5);
                float  edgeDist   = length(toEdge);

                // mvNorm: 모션 방향, edgeNorm: 중심→이 픽셀 방향
                float2 mvNorm   = (mvLen > 0.0001) ? mv / mvLen   : float2(0,0);
                float2 edgeNorm = (edgeDist > 0.001) ? toEdge / edgeDist : float2(0,0);

                // 두 방향이 같을 때(직진 이동 시 엣지) radialDot=1, 수직이면 0
                float radialDot    = saturate(dot(mvNorm, edgeNorm));
                float radialWeight = lerp(1.0, 1.0 + edgeDist * _RadialScale, radialDot);
                blurVec           *= radialWeight;

                // 최대 범위 클램프
                float maxUV = max(_MaxBlurUV, 0.001);
                blurVec = clamp(blurVec, -maxUV, maxUV);

                // ── Debug: 블러벡터 ────────────────────────────────────────
                #if defined(_DEBUGMODE_BLURVECTOR)
                return float4(VisualizeVector(blurVec, 1.0 / maxUV), 1.0);
                #endif

                // ── 5. 깊이 마스크 ─────────────────────────────────────────
                // [FIX-3] Near < 0.01 폴백: 1.0(전체 블러) → 0.0(전체 보호)
                // CBUFFER 수정(FIX-2)으로 정상 경로에서는 이 분기에 진입하지 않음.
                // 방어용 폴백: 혹시라도 Near≈0이 들어오면 블러 대신 보호.
                float linearDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float depthMask   = (_SSBlurDepthNear < 0.01)
                    ? 0.0  // [FIX-3] Near≈0 → 전체 보호 (이전: 1.0 = 전체 블러)
                    : saturate((linearDepth - _SSBlurDepthNear) / max(_SSBlurDepthFade, 0.1));

                // ── Debug: 깊이마스크 ──────────────────────────────────────
                #if defined(_DEBUGMODE_DEPTHMASK)
                float boundary = abs(linearDepth - _SSBlurDepthNear) < 0.2 ? 1.0 : 0.0;
                return float4(depthMask + boundary, depthMask, depthMask, 1.0);
                #endif

                // ── 6. 최종 블러 강도 ──────────────────────────────────────
                float blurStrength = saturate(length(blurVec) / maxUV) * depthMask;

                // ── Debug: 블러강도 ────────────────────────────────────────
                #if defined(_DEBUGMODE_BLURSTRENGTH)
                // 카메라 움직일때 화면에 주황~흰색이 보이면 정상
                return float4(blurStrength, blurStrength * 0.5, 0.0, 1.0);
                #endif

                if (blurStrength < 0.005)
                    return original;

                // ── 7. N샘플 중앙 대칭 누적 ───────────────────────────────
                int    N   = (int)clamp(_SSMBSamples, 4.0, 16.0);
                float4 acc = float4(0, 0, 0, 0);
                for (int s = 0; s < N; s++)
                {
                    float  t        = (float)s / max((float)(N-1), 1.0) - 0.5;
                    float2 sampleUV = uv + blurVec * t;
                    acc += SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_BlitTexture, sampleUV, 0);
                }
                acc /= (float)N;

                // ── Debug: 블러결과만 ──────────────────────────────────────
                #if defined(_DEBUGMODE_FINALBLUR)
                return acc;
                #endif

                // ── 8. 최종 혼합 ───────────────────────────────────────────
                return lerp(original, acc, blurStrength);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
