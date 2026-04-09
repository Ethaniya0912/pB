// =============================================================================
// Dreamcore/ObjectMotionBlur_CS
// =============================================================================
// 수정 이력:
//   [Fix-1] _FORWARD_PLUS deprecated 제거 → _CLUSTER_LIGHT_LOOP 단독 선언
//   [Fix-2] Additional Lights 루프: Forward+는 LIGHT_LOOP_BEGIN/END,
//           그 외(_ADDITIONAL_LIGHTS)는 수동 루프로 분리
//           → "loop only executes for 0 iteration" 경고 제거
//   [Fix-3] blurDir division-by-zero: [branch] 힌트 + safe normalize 헬퍼로 통일
//           → "floating point division by zero" 경고 제거 (217, 519, 616번)
//   [Fix-4] 이동 중 산발적 검정 패치:
//           Forward+ 경로에서 SV_POSITION(오프셋됨)이 아닌
//           positionCSOriginal(오프셋 전)으로 클러스터 조회 — 기존 유지
//           + [branch] 추가로 컴파일러 정적 분석 경고 억제
//   [Fix-5 REVERTED] inputData.positionCS = positionCSOriginal 유지
//           Screen Space Shadow 미사용 환경에서 SV_POSITION 교체는 역효과
//           positionCSOriginal(Clip Space)가 Forward+ 클러스터 조회에 더 안전
//   [Fix-6 FINAL] GVB velocity 완전 활성화:
//           GVB curr-prev는 오브젝트 로컬 공간 delta → TransformObjectToWorldDir로 월드 변환
//           vel = _OMBVelocityWS(루트 이동, 월드) + TransformObjectToWorldDir(GVB delta, 로컬→월드)
//           제자리 공격/회전에서 버텍스별 실제 스킨닝 속도 반영
//           velocity 디버그 모드에서 제자리 공격 시 팔/손 색상 확인으로 검증 완료
//   [Fix-8] maxBlurLength 0.20 → 0.25: GVB 활성화 후 강한 스윙(speed≈0.2m/f) 커버
//   [Fix-9] Transparent Alpha Fade 추가:
//           Idle 외 모든 BlurState에서 투명 렌더 활성 가능
//           blurProgress(finalStretch/maxLen) 기반으로 alpha 감소
//           boneWeight > 0 부위만 alpha 감소 — 몸통/루트는 항상 불투명
//           _OMB_TRANSPARENT 키워드로 P2가 BlurState != Idle 시 활성화
//           _OMBFadeExponent: alpha 감소 커브 (0.7 권장)
//           _OMBDirStrength: 이동 방향 앞쪽 버텍스 추가 투명도 (0.7 권장)
//   [Fix-10] Debug Visualize에 AlphaFade 모드 추가:
//           각 버텍스 예상 alpha를 씬에서 색상으로 확인
//           흰=선명(alpha 1.0), 녹=0.7, 노랑=0.5, 빨강=0.3이하
//   [Fix-7] minBlur 팝인 + blurDir 폴백 방향 번쩍임 수정:
//           speed가 임계값을 막 넘는 순간 minBlur가 즉시 full 적용되어 팝인 발생
//           → speedRamp(0~1)로 speed에 비례하여 선형 증가하도록 수정
//           → speed=0 근처에서 finalStretch=0 보장, 급격한 오프셋 팝인 제거
//           ShadowCaster, DepthOnly 패스도 동일하게 적용 (메시 분리 방지)
// =============================================================================
Shader "Dreamcore/ObjectMotionBlur_CS"
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
        [KeywordEnum(Off, Weight, Velocity, BlurAmount, StretchAbs, StretchRatio, AlphaFade)]
        _OMBDebug       ("Debug Visualize", Float) = 0
        // Off           = 정상 렌더 (투명 모드 활성 시 실제 alpha 적용)
        // Weight        = 가중치 흑백 (흰=1.0, 검=0.0)
        // Velocity      = GVB 속도 벡터 방향 RGB
        // BlurAmount    = 최종 늘어남 (주황색, maxBlurLength 기준 정규화)
        // StretchAbs    = 절대 늘어남 cm 단위 시각화
        //                 (검정=0cm, 파랑=2cm, 녹색=8cm, 노랑=16cm, 흰색=25cm+)
        // StretchRatio  = maxBlurLength 대비 비율 시각화 0%~100%+
        //                 (검정=0%, 파랑=25%, 녹색=50%, 노랑=75%, 흰색=100%, 빨강=초과)
        // AlphaFade     = [Fix-10] 예상 alpha 시각화 (투명 적용 전 미리 확인)
        //                 흰색=1.0(선명), 녹색≈0.7, 노랑≈0.5, 빨강≈0.3이하(뭉개짐)
        //                 → _OMBFadeExponent/_OMBDirStrength 수치 조절 시 즉시 피드백

        // ── [Fix-9] Transparent Alpha Fade 파라미터 ──────────────────
        [Header(Transparent Alpha Fade)]
        [Toggle(_OMB_TRANSPARENT)]
        _OMBTransparent  ("Enable Alpha Fade", Float) = 0

        // 블렌드 고정값 하드코딩 — ZWrite Off, Blend SrcAlpha OneMinusSrcAlpha
        // (런타임 변경 불필요)

        // 0.7=이동 시작부터 빠르게 흐릿, 1.0=선형, 2.0=나중에 급격히
        _OMBFadeExponent ("Fade Exponent", Range(0.3, 3.0)) = 0.5
        // 방향성 블러 강도. 0=방향 무관, 1.0=이동 앞쪽 완전 투명
        _OMBDirStrength  ("Dir Fade Strength", Range(0.0, 1.0)) = 0.7
        // 이 값 이하 boneWeight 버텍스는 항상 alpha=1 (몸통 보호)
        _OMBFadeWeightThreshold ("Fade Min Weight", Range(0.0, 0.5)) = 0.05
        // Hysteresis 임계값 — 깜빡임 방지
        // High: 이 값 초과해야 반투명 시작 (낮출수록 빨리 반투명)
        // Low:  이 값 미만이어야 불투명 복귀 (높일수록 늦게 복귀)
        _OMBHysteresisHigh ("Hysteresis High", Range(0.05, 0.5)) = 0.15
        _OMBHysteresisLow  ("Hysteresis Low",  Range(0.01, 0.3)) = 0.05

        [Header(Trailing Edge Smear)]
        [Toggle(_OMB_TRAILING)]
        _OMBTrailing         ("Enable Trailing Edge",  Float)          = 0
        _OMBTrailFadeExp     ("Trail Fade Exponent",   Range(0.5, 5.0))= 2.0
        _OMBTaperStrength    ("Taper Strength",        Range(0.0, 0.5))= 0.1
        _OMBGlobalTrail      ("Global Trail Influence", Range(0.0, 1.0))= 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
        }
        LOD 300

        // =================================================================
        // Pass 1: Forward Lit
        // =================================================================
        Pass
        {
            Name "ObjectMotionBlurForward"
            Tags { "LightMode" = "UniversalForward" }

            Stencil
            {
                // Ref=10: 캐릭터 OMB 셰이더 전용 Stencil 마커
                // 같은 캐릭터의 다른 Opaque SMR도 이 셰이더를 사용하거나
                // 동일한 Stencil Ref를 기록하면 검은 투명화 문제가 해소됨
                Ref   10
                Comp  Always
                Pass  Replace
                ZFail Replace
            }

            Cull  Back
            ZTest LEqual
            // Cull Back 유지: Cull Off 하면 뒷면 overdraw로 성능 저하
            // 검은 투명화는 alpha deadzone으로 억제
            // runtime material property 변경(ZWrite [변수])은 URP SRP Batcher와 충돌
            // → Blend/ZWrite를 고정값으로 하드코딩
            // alpha=1.0이면 불투명과 시각적으로 동일 → 정지 시 차이 없음
            // alpha<1.0이면 배경이 비쳐 블러리한 효과 발생
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma shader_feature_local _OMB_ENABLED
            #pragma shader_feature_local _OMBDEBUG_OFF _OMBDEBUG_WEIGHT _OMBDEBUG_VELOCITY _OMBDEBUG_BLURAMOUNT _OMBDEBUG_STRETCHABS _OMBDEBUG_STRETCHRATIO _OMBDEBUG_ALPHAFADE
            #pragma shader_feature_local _OMB_TRANSPARENT
            #pragma shader_feature_local _OMB_TRAILING

            #pragma multi_compile_instancing
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            // _CLUSTER_LIGHT_LOOP: URP 6.x Forward+ 클러스터 경로
            // (_FORWARD_PLUS는 deprecated되어 제거)
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

            // ── 텍스처 ──────────────────────────────────────────────────
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_OcclusionMap);     SAMPLER(sampler_OcclusionMap);

            // ── 머티리얼 프로퍼티 ────────────────────────────────────────
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _BumpScale;
                float  _Metallic;
                float  _Smoothness;
                float  _OcclusionStrength;
                // 모션 블러 핵심 파라미터 (P2_ObjectMotionBlurController MPB 주입)
                float  _ShutterAngle;       // ShaderCoordinationManager 대체: P2 Inspector 직접 설정
                float  _TargetFPS;
                float  _OMBIntensityGlobal; // 기본값 1.0 — 0이면 전체 블러 무효화
                float  _OMBIntensity;
                float  _OMBMaxLength;
                float  _OMBMinBlur;
                float  _OMBShutterMult;
                float3 _OMBVelocityWS;
                float  _OMBBudgetScale;
                // [Fix-9] Transparent Alpha Fade 파라미터
                float  _OMBFadeExponent;
                float  _OMBDirStrength;
                float  _OMBFadeWeightThreshold;
                // [p8] Hysteresis: 깜빡임 방지
                float  _OMBHysteresisHigh;
                float  _OMBHysteresisLow;
                // Trailing Edge Smear
                float  _OMBTrailFadeExp;
                float  _OMBTaperStrength;
                float  _OMBGlobalTrail;
                float3 _OMBFacingDir;
            CBUFFER_END

            // ── 모션 블러 파라미터 (전부 CBUFFER에서 MPB로 주입) ──────────

            #if defined(_OMB_ENABLED)
            StructuredBuffer<float3> _OMBVelocityBuffer;
            StructuredBuffer<float>  _BlurWeightBuffer;
            #endif

            // ── 구조체 ───────────────────────────────────────────────────
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                uint   vertexID   : SV_VertexID;
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
                float  dbgBlurAmt        : TEXCOORD6;   // finalStretch (클램프 후)
                float3 dbgVelocity       : TEXCOORD7;
                float  dbgStretchRaw     : TEXCOORD8;   // stretchLen (클램프 전, unclamped)
                float  dbgSpeed          : TEXCOORD9;   // speed (m/frame)
                float  dbgAlphaFade      : TEXCOORD10;  // 예상 alpha (AlphaFade 디버그용)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // [Fix-3] safe normalize 헬퍼 — division-by-zero 경고 제거
            float3 SafeNormVel(float3 v, float speed)
            {
                // [branch]: GPU에게 실제 분기 실행을 지시하여
                // 컴파일러가 speed==0 경로에서 v/speed를 정적 평가하지 않게 함
                [branch]
                if (speed > 0.001)
                    return v / speed;
                else
                    return float3(0, 1, 0);
            }

            // ── 버텍스 셰이더 ────────────────────────────────────────────
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);

                // [Fix-6 FINAL] GVB velocity 완전 활성화
                //
                // velocity 구성:
                //   A. _OMBVelocityWS: P2가 루트 Transform.position 델타로 주입 (월드 공간)
                //      → 달리기/이동 시 캐릭터 전체의 이동 방향
                //
                //   B. _OMBVelocityBuffer[vid]: GVB curr-prev delta (로컬 공간)
                //      → OMBSkinningCacheManager가 GetVertexBuffer() curr-prev로 계산
                //      → 팔 스윙, 회전 등 제자리 동작의 버텍스별 실제 이동
                //      → GVB는 오브젝트 로컬 공간 버텍스 포지션을 반환하므로
                //         curr(로컬) - prev(로컬) = 로컬 공간 delta
                //      → TransformObjectToWorldDir()로 로컬→월드 변환 필수
                //
                // 검증 근거:
                //   velocity 디버그 모드에서 제자리 공격 시 팔/손에 색상 발생 확인
                //   → GVB가 정상 작동하고 버텍스별 속도를 포함하고 있음이 확정됨
                float3 vel = _OMBVelocityWS;
                #if defined(_OMB_ENABLED)
                    // GVB delta를 로컬→월드 변환 후 합산
                    // TransformObjectToWorldDir: UNITY_MATRIX_M의 3x3(회전+스케일) 적용
                    // 이동 성분은 방향벡터 변환에 포함되지 않으므로 이중 계산 없음
                    vel += TransformObjectToWorldDir(_OMBVelocityBuffer[input.vertexID]);
                #endif

                // 속도 스파이크 클램프
                float velLen = length(vel);
                float maxVel = max(_OMBMaxLength * 2.0, 0.01);
                [branch]
                if (velLen > maxVel)
                    vel = vel * (maxVel / velLen);

                float  speed   = length(vel);
                // [Fix-3] SafeNormVel로 division-by-zero 경고 제거
                float3 blurDir = SafeNormVel(vel, speed);

                // 셔터앵글 블러 길이
                float safeShutter   = max(_ShutterAngle, 1.0);
                float safeFPS       = max(_TargetFPS, 1.0);
                float expTime       = (safeShutter / 360.0) / safeFPS;
                // P7 Budget 배율 (미주입 시 0 → 1.0 폴백)
                float ombBudget     = (_OMBBudgetScale > 0.001) ? _OMBBudgetScale : 1.0;
                float effectiveMult = _OMBShutterMult * _OMBIntensity * _OMBIntensityGlobal * ombBudget;
                float stretchLen    = speed * safeFPS * expTime * effectiveMult;
                float maxLen        = max(_OMBMaxLength, 0.001);
                stretchLen          = min(stretchLen, maxLen);

                // 가중치
                float boneWeight = 1.0;
                #if defined(_OMB_ENABLED)
                    // [팽창 억제] boneWeight 비선형 감쇠: 중간 가중치 부위 stretch 억제
                    // 끝단(w=1.0)은 거의 변화 없고, 중간(w=0.5)은 약 30% 감소
                    boneWeight = pow(saturate(_BlurWeightBuffer[input.vertexID]), 1.5);
                #endif

                // 최종 블러 길이 (정지 시 0)
                float finalStretch = 0.0;
                [branch]
                if (speed > 0.001)
                {
                    finalStretch = stretchLen * boneWeight;

                    // [Fix-7] minBlur 팝인 방지 — speedRamp로 선형 증가
                    // speed가 임계값을 막 넘는 순간 minBlur full 적용 시
                    // 버텍스가 갑자기 오프셋되어 Forward+ 클러스터 경계 이탈 → 검정 번쩍임
                    // speedRamp: speed가 maxLength의 10%에 도달할 때 1.0이 되는 선형 램프
                    // → speed 0 근처에서 minBlur도 0에 수렴, 급격한 팝인 제거
                    float speedRamp  = saturate(speed / max(_OMBMaxLength * 0.1, 0.0001));
                    float minStretch = _OMBMinBlur * boneWeight * speedRamp;
                    finalStretch     = max(finalStretch, minStretch);
                }

                // ── Trailing Edge Smear (조합 A) ────────────────────────
                // trailWeight: 0=leading(앞면,이동방향), 1=trailing(뒷면,잔상)
                // smoothstep(0,-0.5,dot): dot>0→0.0, dot<-0.5→1.0, 사이는 부드러운 전환
                float3 normalWSVert = GetVertexNormalInputs(input.normalOS, input.tangentOS).normalWS;

                float3 finalPosWS = posWS;
                #if defined(_OMB_ENABLED)
                    #if defined(_OMB_TRAILING)
                        // trailing 판별: 이동 반대 방향을 향하는 면
                        float  dotNBlur    = dot(normalWSVert, blurDir);
                        float  trailNormal = smoothstep(0.0, -0.5, dotNBlur);

                        // boneWeight 기반 global trail:
                        // _OMBGlobalTrail > 0이면 boneWeight에 비례한 최소 trailing 보장
                        // 팔끝(w=1.0): 항상 강한 trailing
                        // 어깨(w=0.6): 약한 trailing
                        // 몸통(w=0.2): 아주 약한 trailing
                        // P2에서 BlurState.Attack 시 0.3~0.5 주입 권장
                        float  trailGlobal = boneWeight * _OMBGlobalTrail;
                        float  trailWeight = max(trailNormal, trailGlobal);

                        // vel=0 버텍스(몸통 등)는 캐릭터 facing 방향으로 폴백
                        float3 effectiveBlurDir = (speed > 0.001)
                            ? blurDir
                            : normalize(_OMBFacingDir + float3(0,0.001,0));

                        // GlobalTrail 기반 최소 stretch 보장
                        float globalStretch    = boneWeight * _OMBGlobalTrail * _OMBMaxLength * 0.5;
                        float effectiveStretch = max(finalStretch, globalStretch);

                        // ── 버텍스 오프셋: trailNormal만 사용 ────────────────
                        // trailWeight(trailNormal + trailGlobal)를 오프셋에 쓰면
                        // GlobalTrail > 0일 때 leading face도 이동 → 원본 위치 소실
                        // → smear는 노멀 기반 trailNormal만 사용해 leading 보호
                        float3 smearOffset = -effectiveBlurDir * effectiveStretch * trailNormal;

                        float  blurProg    = effectiveStretch / max(_OMBMaxLength, 0.001);
                        float  taper       = trailNormal * blurProg * _OMBTaperStrength;
                        smearOffset       -= normalWSVert * taper;

                        finalPosWS = posWS + smearOffset;
                    #else
                        // 기존 방식: 이동 방향으로 밀기 (Toggle Off 시)
                        finalPosWS = posWS + blurDir * finalStretch;
                        float trailWeight  = 0.0; // alpha 계산용 더미
                    #endif
                #else
                    float trailWeight = 0.0;
                #endif

                output.positionCS         = TransformWorldToHClip(finalPosWS);
                output.positionCSOriginal = TransformWorldToHClip(posWS);
                output.uv                 = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionWS         = posWS;

                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS  = normalInput.normalWS;
                output.tangentWS = float4(normalInput.tangentWS,
                                          input.tangentOS.w * GetOddNegativeScale());

                output.dbgWeight      = boneWeight;
                output.dbgBlurAmt     = finalStretch;        // 클램프+가중치 적용 후 최종값
                // GVB idle noise 제거: speed < 0.002 (idle deadzone) 이면 vel=0으로 처리
                float3 velDbg = (speed > 0.002) ? vel : float3(0,0,0);
                output.dbgVelocity    = velDbg * 8.0;
                output.dbgStretchRaw  = stretchLen;          // 클램프 전 순수 stretchLen
                output.dbgSpeed       = speed;               // m/frame 단위 속도

                // ── Alpha Fade 계산 (Trailing Edge 기반) ────────────────
                // blurProgress: finalStretch 기반 (속도 기반 실제 stretch)
                // globalStretch는 smear 오프셋에만 사용, alpha 계산은 분리
                float blurProgress = finalStretch / max(_OMBMaxLength, 0.001);
                float blurAlpha    = 1.0;

                #if defined(_OMB_TRAILING)
                    // Soft Alpha Ramp: trailing일수록 투명
                    // trailWeight=0(leading) → alpha=1 (불투명 유지)
                    // trailWeight=1(trailing 끝) + blurProgress=1 → alpha=0 (사라짐)
                    // localFade: 속도 기반 trailing (팔처럼 빠른 부위)
                    // globalFade: GlobalTrail 직접 적용 (전신, 속도 무관)
                    // 두 항을 max()로 합산 → blurProgress=0인 몸통도 GlobalTrail만큼 trailing 발생
                    float localFade  = trailNormal * blurProgress;
                    // globalFade: boneWeight 비례 전신 trailing 보조
                    // 0.5로 스케일 → GlobalTrail=1이어도 최대 fadeVal=0.5 (완전 투명 방지)
                    float globalFade = boneWeight * _OMBGlobalTrail * 0.5;
                    float trailFade  = max(localFade, globalFade);

                    [branch]
                    if (trailFade > 0.02 && boneWeight > _OMBFadeWeightThreshold)
                    {
                        float fadeVal = pow(saturate(trailFade), _OMBTrailFadeExp);
                        blurAlpha     = 1.0 - fadeVal;
                    }
                #else
                    // 기존 Hysteresis 방식 유지 (Toggle Off 시)
                    [branch]
                    if (blurProgress > _OMBHysteresisHigh && boneWeight > _OMBFadeWeightThreshold)
                    {
                        float globalFade = 1.0 - pow(max(blurProgress - _OMBHysteresisHigh, 0.0),
                                                     max(_OMBFadeExponent, 0.1));
                        float3 pivotWS   = TransformObjectToWorld(float3(0, 0, 0));
                        float3 toVertex  = posWS - pivotWS;
                        float  dirDot    = (speed > 0.001 && length(toVertex) > 0.001)
                                           ? saturate(dot(normalize(toVertex), blurDir)) : 0.0;
                        float  dirFade   = 1.0 - dirDot * blurProgress * _OMBDirStrength;
                        blurAlpha        = saturate(globalFade * dirFade);
                    }
                #endif

                output.dbgAlphaFade = blurAlpha;

                return output;
            }

            // ── 프래그먼트 셰이더 ────────────────────────────────────────
            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 텍스처 샘플링
                float4 baseMapSample  = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                float4 albedo         = baseMapSample * _BaseColor;
                float4 metallicSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, input.uv);
                float  metallic       = metallicSample.r * _Metallic;
                float  smoothness     = metallicSample.a * _Smoothness;
                float  occlusion      = lerp(1.0,
                    SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, input.uv).g,
                    _OcclusionStrength);

                // 노멀맵
                float4   normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);
                float3   normalTS     = UnpackNormalScale(normalSample, _BumpScale);
                float3   bitangentWS  = cross(input.normalWS, input.tangentWS.xyz) * input.tangentWS.w;
                float3x3 TBN          = float3x3(input.tangentWS.xyz, bitangentWS, input.normalWS);
                float3   normalWS     = normalize(mul(normalTS, TBN));

                // ── 디버그 모드 ─────────────────────────────────────────
                #if defined(_OMBDEBUG_WEIGHT)
                    return float4(input.dbgWeight, input.dbgWeight, input.dbgWeight, 1.0);
                #endif

                #if defined(_OMBDEBUG_VELOCITY)
                    return float4(saturate(abs(input.dbgVelocity)), 1.0);
                #endif

                #if defined(_OMBDEBUG_BLURAMOUNT)
                    // ×3 증폭: 낮은 stretch 값도 시각적으로 확인 가능
                    // 정지=검정, 느린이동=주황, 빠른이동/공격=밝은노랑
                    float blurViz = saturate(input.dbgBlurAmt / max(_OMBMaxLength, 0.001) * 3.0);
                    return float4(blurViz, blurViz * 0.5, 0.0, 1.0);
                #endif

                #if defined(_OMBDEBUG_STRETCHABS)
                    // ── StretchAbs: 실제 늘어남 절대값 시각화 ────────────────
                    // finalStretch(m)를 cm 단위로 환산하여 색상으로 표현
                    // 색상 기준 (boneWeight 반영된 최종값):
                    //   검정  = 0cm   (블러 없음)
                    //   파랑  = 2cm   (미세한 잔상)
                    //   녹색  = 8cm   (가시적 잔상)
                    //   노랑  = 16cm  (뚜렷한 잔상)
                    //   흰색  = 25cm+ (최대 강도)
                    //
                    // 읽는 법:
                    //   - 손끝/무기가 노랑~흰색이면 레퍼런스 수준 블러
                    //   - 전신이 녹색이면 중간 강도
                    //   - 전신이 파랑이면 ShutterMult 또는 speed 부족
                    float stretchCm = input.dbgBlurAmt * 100.0; // m → cm
                    // 0~25cm 범위를 0~1로 매핑
                    float t = saturate(stretchCm / 25.0);
                    // 색상 램프: 검정→파랑→녹색→노랑→흰색
                    float3 absColor;
                    if (t < 0.08)       // 0~2cm: 검정→파랑
                        absColor = float3(0, 0, t / 0.08);
                    else if (t < 0.32)  // 2~8cm: 파랑→녹색
                        absColor = float3(0, (t-0.08)/0.24, 1.0-(t-0.08)/0.24);
                    else if (t < 0.64)  // 8~16cm: 녹색→노랑
                        absColor = float3((t-0.32)/0.32, 1.0, 0);
                    else                // 16~25cm+: 노랑→흰색
                        absColor = float3(1.0, 1.0, saturate((t-0.64)/0.36));
                    return float4(absColor, 1.0);
                #endif

                #if defined(_OMBDEBUG_ALPHAFADE)
                    // ── AlphaFade: 예상 alpha 시각화 ─────────────────────────
                    // 투명 렌더 적용 전 '어느 부위가 얼마나 흐릿해질지' 미리 확인
                    // 색상 기준:
                    //   흰색  = alpha 1.0 (선명, 정지/몸통)
                    //   녹색  = alpha 0.7 (약한 흐릿함)
                    //   노랑  = alpha 0.5 (중간 흐릿함)
                    //   주황  = alpha 0.3 (강한 흐릿함)
                    //   빨강  = alpha 0.1 이하 (거의 완전 투명)
                    float a = input.dbgAlphaFade;
                    float3 alphaColor;
                    if      (a > 0.85)  alphaColor = float3(a, a, a);           // 흰색 계열
                    else if (a > 0.65)  alphaColor = float3(0, a, 0);           // 녹색
                    else if (a > 0.45)  alphaColor = float3(a, a, 0);           // 노랑
                    else if (a > 0.25)  alphaColor = float3(a, a*0.4, 0);       // 주황
                    else                alphaColor = float3(0.8, 0, 0);         // 빨강
                    return float4(alphaColor, 1.0);
                #endif

                #if defined(_OMBDEBUG_STRETCHRATIO)
                    // ── StretchRatio: maxBlurLength 대비 활용 비율 시각화 ─────
                    // finalStretch / maxBlurLength = 0%~100%
                    // 파라미터(ShutterAngle, ShutterMult, maxBlurLength)를 바꿀 때
                    // 상한 대비 얼마나 활용하는지 즉시 확인
                    //
                    // 색상 기준:
                    //   검정  = 0%    (블러 없음)
                    //   파랑  = 25%   (상한의 1/4 사용)
                    //   녹색  = 50%   (상한의 절반 사용)
                    //   노랑  = 75%   (상한의 3/4 사용)
                    //   흰색  = 100%  (maxBlurLength 상한 도달)
                    //   빨강  = 100%+ (클램프에 걸림 — stretchRaw > maxBlurLength)
                    //
                    // 읽는 법:
                    //   - 빨강 부위: maxBlurLength를 더 올리면 추가 늘어남 가능
                    //   - 흰색 부위: 현재 설정으로 최대 활용 중
                    //   - 파랑 이하: ShutterMult 또는 ShutterAngle 올릴 여유 있음
                    float ratio = input.dbgBlurAmt / max(_OMBMaxLength, 0.001);
                    // stretchRaw > maxBlurLength면 클램프에 걸린 것 → 빨강으로 표시
                    bool isClamped = (input.dbgStretchRaw > _OMBMaxLength * 1.05);
                    float3 ratioColor;
                    if (isClamped)
                    {
                        // 빨강: 클램프 초과 (maxBlurLength 올리면 더 늘어남)
                        ratioColor = float3(1.0, 0.0, 0.0);
                    }
                    else if (ratio < 0.25)      // 0~25%: 검정→파랑
                        ratioColor = float3(0, 0, ratio/0.25);
                    else if (ratio < 0.50)      // 25~50%: 파랑→녹색
                        ratioColor = float3(0, (ratio-0.25)/0.25, 1.0-(ratio-0.25)/0.25);
                    else if (ratio < 0.75)      // 50~75%: 녹색→노랑
                        ratioColor = float3((ratio-0.50)/0.25, 1.0, 0);
                    else                        // 75~100%: 노랑→흰색
                        ratioColor = float3(1.0, 1.0, saturate((ratio-0.75)/0.25));
                    return float4(ratioColor, 1.0);
                #endif

                // ── 라이팅 ──────────────────────────────────────────────
                float3 posWS3     = input.positionWS;
                float3 viewDirWS3 = GetWorldSpaceNormalizeViewDir(posWS3);

                InputData inputData = (InputData)0;
                inputData.positionWS      = posWS3;
                // positionCSOriginal: 오프셋 전 위치 기준
                // → Forward+ 클러스터 조회가 오프셋과 무관하게 정확해짐
                // → 이동 중 산발적 검정 패치 방지
                inputData.positionCS      = input.positionCSOriginal;
                inputData.normalWS        = normalWS;
                inputData.viewDirectionWS = viewDirWS3;

                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    inputData.shadowCoord = TransformWorldToShadowCoord(posWS3);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 1);
                #endif

                #if defined(_CLUSTER_LIGHT_LOOP)
                    inputData.normalizedScreenSpaceUV =
                        GetNormalizedScreenSpaceUV(input.positionCSOriginal);
                #endif

                half4  shadowMask3      = half4(1, 1, 1, 1);
                float3 indirectDiffuse3 = SampleSH(normalWS);
                float3 result3          = indirectDiffuse3 * albedo.rgb;

                // 메인 라이트
                Light mainLight3  = GetMainLight(inputData.shadowCoord, posWS3, shadowMask3);
                float mainNdotL3  = saturate(dot(normalWS, mainLight3.direction));
                result3 += albedo.rgb * mainLight3.color
                         * mainNdotL3
                         * mainLight3.distanceAttenuation
                         * mainLight3.shadowAttenuation;

                // 스페큘러
                [branch]
                if (smoothness > 0.01)
                {
                    float3 mh  = normalize(mainLight3.direction + viewDirWS3);
                    float  mnh = saturate(dot(normalWS, mh));
                    float  msp = pow(mnh, exp2(10.0 * smoothness + 1.0));
                    result3 += msp * lerp(float3(0.04, 0.04, 0.04), albedo.rgb, metallic)
                             * mainLight3.color
                             * mainLight3.distanceAttenuation
                             * mainLight3.shadowAttenuation;
                }

                // ── Additional Lights ───────────────────────────────────
                // [Fix-2] Forward+와 일반 Additional Lights 경로 분리
                //
                // Forward+: LIGHT_LOOP_BEGIN/END 매크로 사용
                //   → 클러스터 버퍼를 올바르게 조회
                //   → GetAdditionalLightsCount()는 Forward+에서 항상 0이므로
                //      수동 루프를 쓰면 "0 iteration" 경고 + 라이트 누락 발생
                //
                // 일반 (_ADDITIONAL_LIGHTS): 수동 루프 유지
                //   → GetAdditionalLightsCount()가 유효한 수를 반환
                #if defined(_CLUSTER_LIGHT_LOOP)
                {
                    LIGHT_LOOP_BEGIN(GetAdditionalLightsCount())
                        Light addLight = GetAdditionalLight(lightIndex, posWS3, shadowMask3);
                        float NdotL    = saturate(dot(normalWS, addLight.direction));
                        result3 += albedo.rgb * addLight.color
                                 * NdotL
                                 * addLight.distanceAttenuation;
                        [branch]
                        if (smoothness > 0.01)
                        {
                            float3 h  = normalize(addLight.direction + viewDirWS3);
                            float  nh = saturate(dot(normalWS, h));
                            float  sp = pow(nh, exp2(10.0 * smoothness + 1.0));
                            result3  += sp * lerp(float3(0.04, 0.04, 0.04), albedo.rgb, metallic)
                                      * addLight.color * addLight.distanceAttenuation;
                        }
                    LIGHT_LOOP_END
                }
                #elif defined(_ADDITIONAL_LIGHTS)
                {
                    uint addCount = GetAdditionalLightsCount();
                    for (uint li = 0u; li < addCount; ++li)
                    {
                        Light addLight = GetAdditionalLight(li, posWS3, shadowMask3);
                        float NdotL    = saturate(dot(normalWS, addLight.direction));
                        result3 += albedo.rgb * addLight.color
                                 * NdotL
                                 * addLight.distanceAttenuation;
                        [branch]
                        if (smoothness > 0.01)
                        {
                            float3 h  = normalize(addLight.direction + viewDirWS3);
                            float  nh = saturate(dot(normalWS, h));
                            float  sp = pow(nh, exp2(10.0 * smoothness + 1.0));
                            result3  += sp * lerp(float3(0.04, 0.04, 0.04), albedo.rgb, metallic)
                                      * addLight.color * addLight.distanceAttenuation;
                        }
                    }
                }
                #endif

                result3 *= occlusion;

                float  fogFactor3  = ComputeFogFactor(input.positionCSOriginal.z);

                // ── Soft Alpha Ramp (조합 A) ─────────────────────────────
                // _OMB_TRAILING On: trailWeight 기반 blurAlpha → smoothstep으로 소프트닝
                //   leading(앞): blurAlpha=1.0 → softAlpha=1.0 (완전 불투명)
                //   trailing 끝: blurAlpha=0.0 → softAlpha=0.0 (배경으로 녹아듦)
                //   0.05~0.25 전환 구간: 부드러운 배경 혼합
                // _OMB_TRAILING Off: albedo.a 유지 (기존 방식)
                #if defined(_OMB_TRAILING)
                    float softAlpha = smoothstep(0.0, 0.25, input.dbgAlphaFade);
                    float4 finalColor = float4(result3, softAlpha);
                #else
                    float4 finalColor = float4(result3, albedo.a);
                #endif

                finalColor.rgb = MixFog(finalColor.rgb, fogFactor3);
                return finalColor;
            }
            ENDHLSL
        }

        // =================================================================
        // Pass 2: ShadowCaster
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
            #pragma shader_feature_local _OMB_TRAILING  // [FIX-TRAILING]
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _ShutterAngle;
                float  _TargetFPS;
                float  _OMBIntensityGlobal;
                float  _OMBIntensity;
                float  _OMBMaxLength;
                float  _OMBMinBlur;
                float  _OMBShutterMult;
                float3 _OMBVelocityWS;
                float  _OMBFadeExponent;
                float  _OMBDirStrength;
                float  _OMBFadeWeightThreshold;
                float  _OMBHysteresisHigh;
                float  _OMBHysteresisLow;
                float  _OMBTrailFadeExp;
                float  _OMBTaperStrength;
                float  _OMBGlobalTrail;
                float3 _OMBFacingDir;
            CBUFFER_END

            #if defined(_OMB_ENABLED)
            StructuredBuffer<float3> _OMBVelocityBuffer;
            StructuredBuffer<float>  _BlurWeightBuffer;
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

            // [Fix-3] safe normalize 헬퍼 (ShadowCaster 패스용)
            float3 SafeNormVelS(float3 v, float speed)
            {
                [branch]
                if (speed > 0.001)
                    return v / speed;
                else
                    return float3(0, 1, 0);
            }

            ShadowVaryings vertShadow(ShadowAttributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                // [Fix-6 FINAL] 루트 이동 + GVB 버텍스 delta 합산
                float3 vel = _OMBVelocityWS;
                #if defined(_OMB_ENABLED)
                    vel += TransformObjectToWorldDir(_OMBVelocityBuffer[input.vertexID]);
                #endif

                float  speed   = length(vel);
                // [Fix-3]
                float3 blurDir = SafeNormVelS(vel, speed);

                float safeFPS = max(_TargetFPS, 1.0);
                float expTime = (max(_ShutterAngle, 1.0) / 360.0) / safeFPS;
                float mult    = _OMBShutterMult * _OMBIntensity * _OMBIntensityGlobal;
                float stretch = 0.0;
                [branch]
                if (speed > 0.001)
                    stretch = min(speed * safeFPS * expTime * mult, max(_OMBMaxLength, 0.001));

                float boneWeight = 1.0;
                #if defined(_OMB_ENABLED)
                    boneWeight = saturate(_BlurWeightBuffer[input.vertexID]);
                #endif

                // [Fix-7] ShadowCaster도 동일한 speedRamp 적용 (Forward와 메시 분리 방지)
                float finalStretchS = 0.0;
                [branch]
                if (speed > 0.001)
                {
                    finalStretchS        = stretch * boneWeight;
                    float speedRampS     = saturate(speed / max(_OMBMaxLength * 0.1, 0.0001));
                    float minStretchS    = _OMBMinBlur * boneWeight * speedRampS;
                    finalStretchS        = max(finalStretchS, minStretchS);
                }

                #if defined(_OMB_ENABLED)
                    // [FIX-TRAILING] Pass 1과 동일한 trailing 로직:
                    // trailing face(이동 반대쪽)만 뒤로 밀어냄 → Shadow Depth 정렬
                    #if defined(_OMB_TRAILING)
                        float3 normalWS_S    = TransformObjectToWorldNormal(input.normalOS);
                        float  dotNBlur_S    = dot(normalWS_S, blurDir);
                        float  trailNormal_S = smoothstep(0.0, -0.5, dotNBlur_S);
                        posWS += -blurDir * finalStretchS * trailNormal_S;
                    #else
                        posWS += blurDir * finalStretchS;
                    #endif
                #endif

                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float  bias     = max(0.005 * (1.0 - dot(normalWS, lightDir)), 0.0005);
                posWS          += normalWS * bias;

                float4 posCS = TransformWorldToHClip(posWS);
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
        // Pass 3: DepthOnly
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
            #pragma shader_feature_local _OMB_TRAILING  // [FIX-TRAILING]
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _ShutterAngle;
                float  _TargetFPS;
                float  _OMBIntensityGlobal;
                float  _OMBIntensity;
                float  _OMBMaxLength;
                float  _OMBMinBlur;
                float  _OMBShutterMult;
                float3 _OMBVelocityWS;
                float  _OMBFadeExponent;
                float  _OMBDirStrength;
                float  _OMBFadeWeightThreshold;
                float  _OMBHysteresisHigh;
                float  _OMBHysteresisLow;
                float  _OMBTrailFadeExp;
                float  _OMBTaperStrength;
                float  _OMBGlobalTrail;
                float3 _OMBFacingDir;
            CBUFFER_END

            #if defined(_OMB_ENABLED)
            StructuredBuffer<float3> _OMBVelocityBuffer;
            StructuredBuffer<float>  _BlurWeightBuffer;
            #endif

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;      // [FIX-TRAILING] trailing 방향 판별용
                uint   vertexID   : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // [Fix-3] safe normalize 헬퍼 (DepthOnly 패스용)
            float3 SafeNormVelD(float3 v, float speed)
            {
                [branch]
                if (speed > 0.001)
                    return v / speed;
                else
                    return float3(0, 1, 0);
            }

            DepthVaryings vertDepth(DepthAttributes input)
            {
                DepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                // [Fix-6 FINAL] 루트 이동 + GVB 버텍스 delta 합산
                float3 vel = _OMBVelocityWS;
                #if defined(_OMB_ENABLED)
                    vel += TransformObjectToWorldDir(_OMBVelocityBuffer[input.vertexID]);
                #endif

                float  speed   = length(vel);
                // [Fix-3]
                float3 blurDir = SafeNormVelD(vel, speed);

                float safeFPS = max(_TargetFPS, 1.0);
                float expTime = (max(_ShutterAngle, 1.0) / 360.0) / safeFPS;
                float mult    = _OMBShutterMult * _OMBIntensity * _OMBIntensityGlobal;
                float stretch = 0.0;
                [branch]
                if (speed > 0.001)
                    stretch = min(speed * safeFPS * expTime * mult, max(_OMBMaxLength, 0.001));

                float boneWeight = 1.0;
                #if defined(_OMB_ENABLED)
                    boneWeight = saturate(_BlurWeightBuffer[input.vertexID]);
                #endif

                // [Fix-7] DepthOnly도 동일한 speedRamp 적용
                float finalStretchD = 0.0;
                [branch]
                if (speed > 0.001)
                {
                    finalStretchD        = stretch * boneWeight;
                    float speedRampD     = saturate(speed / max(_OMBMaxLength * 0.1, 0.0001));
                    float minStretchD    = _OMBMinBlur * boneWeight * speedRampD;
                    finalStretchD        = max(finalStretchD, minStretchD);
                }

                #if defined(_OMB_ENABLED)
                    // [FIX-TRAILING] Pass 1과 동일한 trailing 로직:
                    // trailing face(이동 반대쪽)만 뒤로 밀어냄 → Depth Buffer 정렬
                    #if defined(_OMB_TRAILING)
                        float3 normalWS_D    = TransformObjectToWorldNormal(input.normalOS);
                        float  dotNBlur_D    = dot(normalWS_D, blurDir);
                        float  trailNormal_D = smoothstep(0.0, -0.5, dotNBlur_D);
                        posWS += -blurDir * finalStretchD * trailNormal_D;
                    #else
                        posWS += blurDir * finalStretchD;
                    #endif
                #endif

                output.positionCS = TransformWorldToHClip(posWS);
                return output;
            }

            half4 fragDepth(DepthVaryings input) : SV_Target { return 0; }
            ENDHLSL
        }

        // =================================================================
        // Pass 4: MotionVectors
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

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _ShutterAngle;
                float  _TargetFPS;
                float  _OMBIntensityGlobal;
                float  _OMBMaxLength;
                float  _OMBShutterMult;
                float  _OMBTrailFadeExp;
                float  _OMBTaperStrength;
                float  _OMBGlobalTrail;
                float3 _OMBFacingDir;
                float3 _OMBVelocityWS;
                float  _OMBHysteresisHigh;
                float  _OMBHysteresisLow;
            CBUFFER_END

            #if defined(_OMB_ENABLED)
            StructuredBuffer<float3> _OMBVelocityBuffer;
            #endif

            struct MotionAttributes
            {
                float4 positionOS : POSITION;
                uint   vertexID   : SV_VertexID;
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

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                // [Fix-6 FINAL] 루트 이동 + GVB 버텍스 delta 합산
                float3 velWS = _OMBVelocityWS;
                #if defined(_OMB_ENABLED)
                    velWS -= TransformObjectToWorldDir(_OMBVelocityBuffer[input.vertexID]);
                #endif
                float3 posPrevWS = posWS - velWS;

                output.positionCS     = TransformWorldToHClip(posWS);
                output.positionCSCurr = output.positionCS;
                output.positionCSPrev = TransformWorldToHClip(posPrevWS);
                return output;
            }

            float4 fragMotion(MotionVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 curr = input.positionCSCurr.xy / input.positionCSCurr.w;
                float2 prev = input.positionCSPrev.xy / input.positionCSPrev.w;
                return float4((curr - prev) * 0.5, 0, 0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
