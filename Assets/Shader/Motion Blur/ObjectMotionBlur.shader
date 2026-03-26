// =============================================================================
// Dreamcore/ObjectMotionBlur
// =============================================================================
// 오브젝트 버텍스 셔터앵글 모션 블러 셰이더
//
// ■ 연결 구조
//   ShaderCoordinationManager  → _ShutterAngle, _TargetFPS, _OMBIntensityGlobal (전역)
//   ObjectMotionBlurController → _PrevObjectToWorld, _OMBIntensity, _OMBMaxLength,
//                                 _OMBMinBlur, _OMBShutterMult         (MPB, 인스턴스별)
//   AvatarAutoWeightBaker      → _BlurWeightBuffer                    (MPB, 인스턴스별)
//
// ■ 원리 요약
//   버텍스 셰이더에서 현재/이전 프레임 위치 차이(속도 벡터)를 계산하고,
//   셔터앵글 공식(노출시간 = ShutterAngle/360/FPS)으로 블러 길이를 결정한 뒤,
//   버텍스를 속도 방향으로 오프셋하여 메시 자체를 늘립니다.
//   AvatarAutoWeightBaker의 가중치 버퍼로 부위별 강도를 차등 적용합니다.
// =============================================================================
Shader "Dreamcore/ObjectMotionBlur"
{
    Properties
    {
        // ── 기본 텍스처 ───────────────────────────────────────────────
        _BaseMap        ("Base Map (Albedo)", 2D)         = "white" {}
        _BaseColor      ("Base Color", Color)             = (1,1,1,1)
        _BumpMap        ("Normal Map", 2D)                = "bump" {}
        _BumpScale      ("Normal Scale", Range(0,2))      = 1.0
        _MetallicGlossMap("Metallic (R) Smoothness (A)", 2D) = "white" {}
        _Metallic       ("Metallic",   Range(0,1))        = 0.0
        _Smoothness     ("Smoothness", Range(0,1))        = 0.5
        _OcclusionMap   ("Occlusion",  2D)                = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0,1)) = 1.0

        // ── 모션 블러 설정 ────────────────────────────────────────────
        [Header(Motion Blur Settings)]

        [Toggle(_OMB_ENABLED)]
        _OMBEnabled     ("Enable Object Motion Blur", Float) = 1

        // ── 디버그 ────────────────────────────────────────────────────
        [Header(Debug)]
        [KeywordEnum(Off, Weight, Velocity, BlurAmount)]
        _OMBDebug       ("Debug Visualize", Float) = 0
        // Off      = 정상 렌더
        // Weight   = 가중치 시각화 (흰=강, 검=약)
        // Velocity = 속도 벡터 방향 시각화 (RG=XY, B=Z)
        // BlurAmount = 최종 블러 양 시각화
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Geometry"
        }
        LOD 300

        // =================================================================
        // Pass 1: Forward Lit — 실제 렌더링 + 버텍스 모션 블러
        // =================================================================
        Pass
        {
            Name "ObjectMotionBlurForward"
            Tags { "LightMode" = "UniversalForward" }

            // 스텐실: DreamcoreStencilRegistry.ObjectMotionBlur = 10
            // 이 값이 기록된 픽셀은 SSMB에서 제외됩니다 (이중 블러 방지)
            Stencil
            {
                Ref   10
                Comp  Always
                Pass  Replace
            }

            Cull  Back
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // 셰이더 기능 토글
            #pragma shader_feature_local _OMB_ENABLED
            #pragma multi_compile _OMBDEBUG_OFF _OMBDEBUG_WEIGHT _OMBDEBUG_VELOCITY _OMBDEBUG_BLURAMOUNT

            // URP 필수 include — CaveDreamcoreTerrain.shader 동일 패턴
            #pragma multi_compile_instancing
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            // Forward+ 핵심: 이 pragma 없으면 LIGHT_LOOP_BEGIN/END 매크로가
            // Forward+ 클러스터 경로를 타지 않아 포인트 라이트가 사라짐
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

            // =============================================================
            // 섹션 A: 텍스처 및 샘플러 선언
            // _BaseMap / _BumpMap 은 SurfaceInput.hlsl 에서 이미 선언됨 — 중복 금지
            // 커스텀 텍스처만 추가로 선언
            // =============================================================
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_OcclusionMap);     SAMPLER(sampler_OcclusionMap);

            // =============================================================
            // 섹션 B: CBUFFER — 머티리얼 프로퍼티
            // =============================================================
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _BumpScale;
                float  _Metallic;
                float  _Smoothness;
                float  _OcclusionStrength;
            CBUFFER_END

            // =============================================================
            // 섹션 C: 모션 블러 파라미터
            //
            // 속도 계산 전략:
            //   UNITY_PREV_MATRIX_M은 이 URP 버전에서 신뢰할 수 없으므로
            //   C#(ObjectMotionBlurController)에서 루트 Transform의 이동 벡터를
            //   _OMBVelocityWS로 직접 주입합니다.
            //
            //   블러 방향 = 오브젝트 이동 방향
            //   블러 강도 = AvatarAutoWeightBaker 가중치로 부위별 차등 적용
            //   (손끝/발끝 강, 루트/척추 약)
            // =============================================================
            float _ShutterAngle;
            float _TargetFPS;
            float _OMBIntensityGlobal;

            // ObjectMotionBlurController → MPB 주입
            float3 _OMBVelocityWS;   // 루트의 1프레임 이동 벡터 (월드 공간)
            float  _OMBIntensity;
            float  _OMBMaxLength;
            float  _OMBMinBlur;
            float  _OMBShutterMult;

            // AvatarAutoWeightBaker → MPB 주입
            #if defined(_OMB_ENABLED)
            StructuredBuffer<float> _BlurWeightBuffer;
            #endif

            // =============================================================
            // 섹션 D: 버텍스/프래그먼트 구조체
            // =============================================================
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 uv           : TEXCOORD0;
                float2 uv2          : TEXCOORD1;  // 라이트맵 UV
                uint   vertexID     : SV_VertexID; // 가중치 버퍼 인덱스
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS        : SV_POSITION;
                float2 uv                : TEXCOORD0;
                float3 positionWS        : TEXCOORD1;
                float3 normalWS          : TEXCOORD2;
                float4 tangentWS         : TEXCOORD3;
                float4 positionCSOriginal: TEXCOORD4;
                float  dbgWeight         : TEXCOORD5;
                float  dbgBlurAmt        : TEXCOORD6;
                float3 dbgVelocity       : TEXCOORD7;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // =============================================================
            // 섹션 E: 버텍스 셰이더
            //
            // 핵심 로직:
            //   1. 현재/이전 월드 위치로 속도 벡터 계산
            //   2. 셔터앵글 공식으로 노출 시간 → 블러 길이 계산
            //   3. 가중치 버퍼에서 이 버텍스의 블러 강도 읽기
            //   4. 버텍스를 속도 방향으로 오프셋 (메시 늘리기)
            // =============================================================
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // ── E-1. 현재 월드 위치 ───────────────────────────────
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);

                // ── E-2/E-3. 속도 벡터 ───────────────────────────────
                float3 vel     = _OMBVelocityWS;

                // 속도 스파이크 방지: 방향 전환 시 1프레임 동안 극단적으로 큰 값이
                // 들어와 버텍스가 과도하게 오프셋되어 번쩍임 발생.
                // 최대 허용 속도를 maxBlurLength 기준으로 클램프.
                float  velLen   = length(vel);
                float  maxVel   = max(_OMBMaxLength * 2.0, 0.01); // maxLen의 2배로 여유
                if (velLen > maxVel)
                    vel = vel * (maxVel / velLen);

                float  speed   = length(vel);
                float3 blurDir = speed > 0.001 ? vel / speed : float3(0, 1, 0);

                // ── E-4. 셔터앵글 블러 길이 계산 ─────────────────────
                // 물리 공식: 노출시간 = ShutterAngle(도) / 360 / FPS
                // 블러길이(월드) = 속도(m/frame) × 노출시간(초)
                //
                // 여기서 speed는 이미 1프레임 이동량(m/frame)이므로
                // expTime을 곱하면: m/frame × 1/FPS = m/frame²
                // 이를 보정하기 위해 FPS를 한 번 더 곱합니다.
                // 결과: stretchLen = speed × (ShutterAngle/360) — 단순하고 직관적
                float safeShutter = max(_ShutterAngle, 1.0);
                float safeFPS     = max(_TargetFPS, 1.0);
                float expTime     = (safeShutter / 360.0) / safeFPS;

                // _OMBShutterMult: 상태별 배율 (ObjectMotionBlurController 주입)
                // Idle=0.5배, Strafe=1.0배, Attack=1.5배, HeavyAttack=2.0배
                float effectiveMult = _OMBShutterMult * _OMBIntensity * _OMBIntensityGlobal;
                float stretchLen    = speed * safeFPS * expTime * effectiveMult;

                // 최대 길이 클램프 (메시 찢어짐 방지)
                float maxLen   = max(_OMBMaxLength, 0.001);
                stretchLen     = min(stretchLen, maxLen);

                // ── E-5. 버텍스 가중치 읽기 ──────────────────────────
                // AvatarAutoWeightBaker가 베이킹한 float 배열
                // 0.0 = 루트/척추 (블러 없음)
                // 1.0 = 손끝/발끝 (최대 블러)
                // 버퍼가 없거나 인덱스 초과 시 기본값 1.0 (전체 블러)
                float boneWeight = 1.0;
                #if defined(_OMB_ENABLED)
                    // 버퍼 바운드 체크는 GPU 드라이버가 처리
                    // AvatarAutoWeightBaker 미부착 시 버퍼가 1원소 폴백 버퍼로 대체됨
                    boneWeight = _BlurWeightBuffer[input.vertexID];
                    boneWeight = saturate(boneWeight);
                #endif

                // ── E-6. 최종 블러 길이 계산 ─────────────────────────
                // 핵심 수정: speed가 0(정지)이면 버텍스 오프셋도 0이어야 함
                // minBlurFloor는 '블러 강도 시각화'에만 사용하고
                // 실제 버텍스 오프셋에는 사용하지 않음
                //
                // 이전 방식의 문제:
                //   finalStretch = max(stretchLen × boneWeight, minBlur × boneWeight)
                //   → 정지 시 finalStretch = minBlur × boneWeight (0이 아님!)
                //   → blurDir = (0,1,0) 폴백 방향으로 버텍스가 위로 오프셋
                //   → 메시 변형 → 라이팅 기준 틀어짐 → 검정으로 보임
                //
                // 수정 방식:
                //   speed가 임계값 이상일 때만 버텍스 오프셋 적용
                //   정지 시 오프셋 = 0 → 메시 변형 없음 → 라이팅 정상
                float finalStretch = 0.0;
                if (speed > 0.001)
                {
                    finalStretch = stretchLen * boneWeight;
                    // 최소 블러: 이동 중일 때만 하한값 보장 (정지 시엔 0)
                    float minStretch = _OMBMinBlur * boneWeight;
                    finalStretch = max(finalStretch, minStretch);
                }

                // ── E-7. 버텍스 오프셋 적용 ──────────────────────────
                // 정지 시 finalStretch=0 이므로 posWS 그대로 → 메시 변형 없음
                float3 finalPosWS = posWS;
                #if defined(_OMB_ENABLED)
                    finalPosWS = posWS + blurDir * finalStretch;
                #endif

                // ── E-8. 일반 변환 ───────────────────────────────────
                output.positionCS         = TransformWorldToHClip(finalPosWS);
                // 원래 위치의 클립 좌표 — Forward+ 조명 클러스터 조회 정확도 보장
                // positionCS(오프셋)로 GetNormalizedScreenSpaceUV를 하면 잘못된
                // 타일/클러스터를 조회하여 포인트 라이트가 사라지는 문제 발생
                output.positionCSOriginal = TransformWorldToHClip(posWS);
                output.uv                 = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionWS         = posWS;

                // 노멀/탄젠트 변환 — 원래 오브젝트 공간 기준 유지
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS   = normalInput.normalWS;
                output.tangentWS  = float4(normalInput.tangentWS, input.tangentOS.w *
                                   GetOddNegativeScale());

                // shadowCoord는 프래그먼트에서 재계산 — 버텍스 오프셋과 독립

                // 디버그 데이터 전달
                output.dbgWeight   = boneWeight;
                output.dbgBlurAmt  = finalStretch;
                output.dbgVelocity = vel * 10.0; // 시각화를 위해 증폭

                return output;
            }

            // =============================================================
            // 섹션 F: 프래그먼트 셰이더
            //
            // PBR 라이팅 처리 + 디버그 모드 시각화
            // =============================================================
            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // ── F-1. 텍스처 샘플링 ───────────────────────────────
                float4 baseMapSample  = SAMPLE_TEXTURE2D(_BaseMap,  sampler_BaseMap,  input.uv);
                float4 albedo         = baseMapSample * _BaseColor;

                float4 metallicSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, input.uv);
                float  metallic       = metallicSample.r * _Metallic;
                float  smoothness     = metallicSample.a * _Smoothness;

                float  occlusion      = lerp(1.0, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, input.uv).g, _OcclusionStrength);

                // ── F-2. 노멀맵 처리 ──────────────────────────────────
                float4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);
                float3 normalTS     = UnpackNormalScale(normalSample, _BumpScale);
                float3 bitangentWS  = cross(input.normalWS, input.tangentWS.xyz) * input.tangentWS.w;
                float3x3 TBN        = float3x3(input.tangentWS.xyz, bitangentWS, input.normalWS);
                float3 normalWS     = normalize(mul(normalTS, TBN));

                // ── F-3. 디버그 모드 ──────────────────────────────────
                // Weight: 버텍스 가중치를 흑백으로 시각화
                //   흰색 = 가중치 1.0 (손끝/발끝, 최대 블러)
                //   검정 = 가중치 0.0 (루트/척추, 블러 없음)
                #if defined(_OMBDEBUG_WEIGHT)
                    return float4(input.dbgWeight, input.dbgWeight, input.dbgWeight, 1.0);
                #endif

                // Velocity: 속도 벡터를 RGB로 시각화
                //   R=X(좌우), G=Y(상하), B=Z(전후)
                //   정지 시 검정, 이동 시 방향에 따른 색상
                #if defined(_OMBDEBUG_VELOCITY)
                    float3 velViz = saturate(abs(input.dbgVelocity));
                    return float4(velViz, 1.0);
                #endif

                // BlurAmount: 최종 블러 적용량 시각화
                //   밝을수록 이 버텍스에 강한 블러가 적용됨
                //   정지 또는 가중치=0이면 검정
                #if defined(_OMBDEBUG_BLURAMOUNT)
                    float blurViz = saturate(input.dbgBlurAmt / max(_OMBMaxLength, 0.001));
                    return float4(blurViz, blurViz * 0.5, 0.0, 1.0); // 주황색 계열
                #endif

                // ── F-4. 직접 라이팅 (CaveDreamcoreTerrain 패턴) ─────
                // CaveDreamcoreTerrain.shader와 동일한 구조:
                //   1. #pragma multi_compile _ _FORWARD_PLUS 추가
                //   2. InputData에 positionCS + normalizedScreenSpaceUV 세팅
                //   3. LIGHT_LOOP_BEGIN/END 매크로로 Forward+ 클러스터 조회
                //
                // 핵심: _FORWARD_PLUS pragma 없이 GetAdditionalLight()만 호출하면
                // Forward+ 렌더링에서 클러스터 경로를 타지 않아 포인트 라이트가 0을 반환함.

                float3 posWS3     = input.positionWS;
                float3 viewDirWS3 = GetWorldSpaceNormalizeViewDir(posWS3);

                // CaveDreamcoreTerrain과 동일: InputData 세팅
                InputData inputData = (InputData)0;
                inputData.positionWS        = posWS3;
                // positionCS: SV_POSITION 사용 (CaveDreamcoreTerrain과 동일)
                // TEXCOORD 복사본을 positionCS에 넣으면 컴파일 에러 발생
                // ★ 핵심 수정: positionCS는 반드시 오프셋 전 원래 위치 기준
                // LIGHT_LOOP_BEGIN 매크로가 inputData.positionCS로 normalizedScreenSpaceUV를
                // 내부 재계산함. 오프셋된 SV_POSITION(input.positionCS)을 넣으면
                // Forward+ 클러스터 조회가 틀려 포인트 라이트 = 0 → 검정 발생
                // positionCSOriginal = TransformWorldToHClip(posWS) = 오프셋 전 위치
                inputData.positionCS        = input.positionCSOriginal;
                inputData.normalWS          = normalWS;
                inputData.viewDirectionWS   = viewDirWS3;

                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    inputData.shadowCoord = TransformWorldToShadowCoord(posWS3);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 1);
                #endif

                #if defined(_FORWARD_PLUS)
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCSOriginal);
                #endif

                half4 shadowMask3 = half4(1, 1, 1, 1);

                // 앰비언트
                float3 indirectDiffuse3 = SampleSH(normalWS);
                float3 result3 = indirectDiffuse3 * albedo.rgb;

                // 메인 라이트
                Light mainLight3 = GetMainLight(inputData.shadowCoord, posWS3, shadowMask3);
                float mainNdotL3 = saturate(dot(normalWS, mainLight3.direction));
                result3 += albedo.rgb * mainLight3.color
                         * mainNdotL3
                         * mainLight3.distanceAttenuation
                         * mainLight3.shadowAttenuation;

                // 스페큘러
                if (smoothness > 0.01)
                {
                    float3 mh = normalize(mainLight3.direction + viewDirWS3);
                    float  mnh = saturate(dot(normalWS, mh));
                    float  msp = pow(mnh, exp2(10.0 * smoothness + 1.0));
                    result3 += msp * lerp(float3(0.04,0.04,0.04), albedo.rgb, metallic)
                             * mainLight3.color * mainLight3.distanceAttenuation * mainLight3.shadowAttenuation;
                }

                // ── Additional Lights: positionWS 직접 루프 ─────────
                // LIGHT_LOOP_BEGIN 완전 제거 — normalizedScreenSpaceUV/클러스터 조회 없음
                // GetAdditionalLight(i, posWS)는 월드 거리 기반 → 버텍스 오프셋 무관
                // Forward+ 포함 모든 경로에서 정확하게 작동
                #if defined(_ADDITIONAL_LIGHTS) || defined(_FORWARD_PLUS)
                {
                    uint addCount = GetAdditionalLightsCount();
                    for (uint li = 0u; li < addCount; ++li)
                    {
                        Light addLight = GetAdditionalLight(li, posWS3, shadowMask3);
                        float NdotL    = saturate(dot(normalWS, addLight.direction));
                        result3 += albedo.rgb * addLight.color
                                 * NdotL
                                 * addLight.distanceAttenuation;

                        if (smoothness > 0.01)
                        {
                            float3 h  = normalize(addLight.direction + viewDirWS3);
                            float  nh = saturate(dot(normalWS, h));
                            float  sp = pow(nh, exp2(10.0 * smoothness + 1.0));
                            result3  += sp * lerp(float3(0.04,0.04,0.04), albedo.rgb, metallic)
                                      * addLight.color * addLight.distanceAttenuation;
                        }
                    }
                }
                #endif

                result3 *= occlusion;

                float fogFactor3  = ComputeFogFactor(input.positionCSOriginal.z);
                float4 finalColor = float4(result3, albedo.a);

                // ── F-5. 포그 적용 ────────────────────────────────────
                finalColor.rgb = MixFog(finalColor.rgb, fogFactor3);

                return finalColor;
            }
            ENDHLSL
        }

        // =================================================================
        // Pass 2: ShadowCaster — 그림자 생성
        // (블러된 버텍스 위치에서 그림자를 드리우려면 이 패스도 동일한 오프셋 적용)
        // =================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest  LEqual
            ColorMask 0
            Cull   Back

            HLSLPROGRAM
            #pragma vertex   vertShadow
            #pragma fragment fragShadow
            #pragma shader_feature_local _OMB_ENABLED
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float _ShutterAngle;
            float _TargetFPS;
            float _OMBIntensityGlobal;
            float3 _OMBVelocityWS;
            float    _OMBIntensity;
            float    _OMBMaxLength;
            float    _OMBShutterMult;

            #if defined(_OMB_ENABLED)
            StructuredBuffer<float> _BlurWeightBuffer;
            #endif

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                uint   vertexID   : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings vertShadow(ShadowAttributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS  = TransformObjectToWorld(input.positionOS.xyz);
                float3 vel    = _OMBVelocityWS;
                float  speed  = length(vel);
                float3 blurDir = speed > 0.001 ? vel / speed : float3(0,1,0);

                float safeFPS   = max(_TargetFPS, 1.0);
                float expTime   = (max(_ShutterAngle, 1.0) / 360.0) / safeFPS;
                float mult      = _OMBShutterMult * _OMBIntensity * _OMBIntensityGlobal;
                float stretch   = (speed > 0.001) ? speed * safeFPS * expTime * mult : 0.0;
                stretch = min(stretch, max(_OMBMaxLength, 0.001));

                float boneWeight = 1.0;
                #if defined(_OMB_ENABLED)
                    boneWeight = saturate(_BlurWeightBuffer[input.vertexID]);
                #endif

                #if defined(_OMB_ENABLED)
                    posWS += blurDir * stretch * boneWeight;
                #endif

                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                // ApplyShadowBias: URP 버전에 따라 누락될 수 있으므로 수동 구현
                // 표면 노멀 방향으로 미세하게 오프셋하여 셀프 섀도 아티팩트 방지
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float  bias     = max(0.005 * (1.0 - dot(normalWS, lightDir)), 0.0005);
                posWS          += normalWS * bias;

                float4 posCS    = TransformWorldToHClip(posWS);

                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = posCS;
                return output;
            }

            half4 fragShadow(ShadowVaryings input) : SV_Target { return 0; }
            ENDHLSL
        }

        // =================================================================
        // Pass 3: DepthOnly — 뎁스 프리패스
        // =================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vertDepth
            #pragma fragment fragDepth
            #pragma shader_feature_local _OMB_ENABLED
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _ShutterAngle;
            float _TargetFPS;
            float _OMBIntensityGlobal;
            float3 _OMBVelocityWS;
            float    _OMBIntensity;
            float    _OMBMaxLength;
            float    _OMBShutterMult;

            #if defined(_OMB_ENABLED)
            StructuredBuffer<float> _BlurWeightBuffer;
            #endif

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                uint   vertexID   : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings vertDepth(DepthAttributes input)
            {
                DepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS  = TransformObjectToWorld(input.positionOS.xyz);
                float3 vel    = _OMBVelocityWS;
                float  speed  = length(vel);
                float3 blurDir = speed > 0.001 ? vel / speed : float3(0,1,0);

                float safeFPS   = max(_TargetFPS, 1.0);
                float expTime   = (max(_ShutterAngle, 1.0) / 360.0) / safeFPS;
                float mult      = _OMBShutterMult * _OMBIntensity * _OMBIntensityGlobal;
                float stretch   = (speed > 0.001) ? speed * safeFPS * expTime * mult : 0.0;
                stretch = min(stretch, max(_OMBMaxLength, 0.001));

                float boneWeight = 1.0;
                #if defined(_OMB_ENABLED)
                    boneWeight = saturate(_BlurWeightBuffer[input.vertexID]);
                #endif

                #if defined(_OMB_ENABLED)
                    posWS += blurDir * stretch * boneWeight;
                #endif

                output.positionCS = TransformWorldToHClip(posWS);
                return output;
            }

            half4 fragDepth(DepthVaryings input) : SV_Target { return 0; }
            ENDHLSL
        }

        // =================================================================
        // Pass 4: MotionVectors — URP 내장 모션 벡터 버퍼 기록
        // SSMB가 이 버퍼를 읽어 배경 블러를 계산합니다
        // =================================================================
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            ColorMask RG
            ZTest  LEqual
            ZWrite Off
            Cull   Back

            HLSLPROGRAM
            #pragma vertex   vertMotion
            #pragma fragment fragMotion
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float3 _OMBVelocityWS;

            struct MotionAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct MotionVaryings
            {
                float4 positionCS     : SV_POSITION;
                float4 positionCSCurr : TEXCOORD0;
                float4 positionCSPrev : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            MotionVaryings vertMotion(MotionAttributes input)
            {
                MotionVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS     = TransformObjectToWorld(input.positionOS.xyz);
                // 이전 위치 = 현재 위치 - 이동 벡터
                float3 posPrevWS = posWS - _OMBVelocityWS;

                output.positionCS     = TransformWorldToHClip(posWS);
                output.positionCSCurr = output.positionCS;
                output.positionCSPrev = TransformWorldToHClip(posPrevWS);
                return output;
            }

            float4 fragMotion(MotionVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // 현재/이전 NDC 좌표 차이 = 모션 벡터
                float2 curr = input.positionCSCurr.xy / input.positionCSCurr.w;
                float2 prev = input.positionCSPrev.xy / input.positionCSPrev.w;
                return float4((curr - prev) * 0.5, 0, 0);
            }
            ENDHLSL
        }
    }

    // 셰이더 컴파일 실패 시 URP Lit으로 폴백
    FallBack "Universal Render Pipeline/Lit"
}
