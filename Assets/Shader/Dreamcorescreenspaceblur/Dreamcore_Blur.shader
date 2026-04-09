// ============================================================
// Dreamcore_Blur.shader
// SSMB (Screen Space Motion Blur) + DepthBlur 통합 셰이더
//
// 이전 구조:
//   ScreenSpaceMotionBlur.shader → Before Rendering PostProcessing
//   DepthBlur.shader             → After Rendering PostProcessing
//   문제: 각각 CBUFFER 없음 → 글로벌 override 취약 → 캐릭터 블러
//
// 통합 구조:
//   Dreamcore_Blur.shader  → Before Rendering PostProcessing 1패스
//   장점:
//     - CBUFFER_START(UnityPerMaterial)로 모든 파라미터 격리
//     - 머티리얼 1개, RenderFeature 1개, 주입기 1개
//     - 실행 순서 충돌 없음 (순서가 하나밖에 없음)
//     - Phase 3 방향성 블러 추가 준비됨
// ============================================================
Shader "Hidden/Dreamcore/Blur"
{
    Properties
    {
        // ── [Section A] SSMB — 카메라 모션 블러 ──────────────────────────
        [Header(SSMB Debug)]
        [KeywordEnum(Off, MotionVector, BlurVector, DepthMask, BlurStrength, FinalBlur, DistanceBlur)]
        _DebugMode ("Debug Mode", Float) = 0

        [Header(SSMB Blur Settings)]
        [Range(0, 10)]   _BlurScale         ("Blur Scale (1~5 권장)", Float) = 3.0
        [Range(4, 16)]   _SSMBSamples       ("Samples", Float) = 12
        [Range(0, 0.1)]  _MaxBlurUV         ("Max Blur UV", Float) = 0.04
        [Range(0, 6)]    _RadialScale        ("Radial Scale", Float) = 2.0

        [Header(SSMB Depth Mask)]
        // [FIX] CBUFFER로 격리 — 글로벌 override 차단됨
        // 캐릭터 보호: 카메라-캐릭터 거리(~2.9m) < DepthNear(6m) → depthMask=0
        [Range(0, 10)]   _SSBlurDepthNear   ("Depth Near (m)", Float) = 6.0
        [Range(0.1, 5)]  _SSBlurDepthFade   ("Depth Fade Range (m)", Float) = 2.0

        [Header(SSMB Object Motion Separation)]
        [Range(0, 0.05)] _ObjMotionThreshold ("Object Motion Threshold", Float) = 0.005

        // ── [Section B] Distance Blur — 원경 블러 ────────────────────────
        [Header(Distance Blur Feature Toggles)]
        [Toggle(_USE_DISTORTION)]    _UseDistortion   ("Enable Distortion",    Float) = 1
        [Toggle(_USE_CHROMA)]        _UseChroma        ("Enable Speed Chroma",  Float) = 1
        [Toggle(_USE_MOTION_BLUR)]   _UseMotionBlur    ("Enable Motion Blur",   Float) = 0
        [Toggle(_USE_MIPMAP)]        _UseMipmap        ("Enable Mipmap",        Float) = 1
        [Toggle(_USE_DISTANCE_BLUR)] _UseDistanceBlur  ("Enable Distance Blur", Float) = 1

        [Header(Distance Zones)]
        // [FIX] CBUFFER 격리 — _MidDist=0/_FarDist=0 division-by-zero 버그 방지
        _NearDist ("Near Distance (m)", Float) = 20.0
        _MidDist  ("Mid Distance (m)",  Float) = 40.0
        _FarDist  ("Far Distance (m)",  Float) = 50.0

        [Header(Distance Blur Intensity)]
        [KeywordEnum(Gaussian, Disk, Smooth, Pixel, Dream)] _BlurType ("Blur Method", Float) = 4
        _MidBlurSize     ("Mid Blur Size",          Range(0.0, 20.0)) = 8.0
        _FarBlurSize     ("Far Blur Size",           Range(0.0, 30.0)) = 10.0
        _BlurExponent    ("Distance Curve Exponent", Range(1.0, 5.0))  = 1.0
        _CenterBlurScale ("Center Blur Strength",    Range(0.0, 1.0))  = 0.2

        [Header(Distance Blur Artifact Correction)]
        // [FIX] CBUFFER 격리 — _DepthThreshold=0/_NearSampleCutoffRatio=0 버그 방지
        [KeywordEnum(None, SNN, Kuwahara, Median)] _FilterType ("Filter Method", Float) = 0
        _DepthThreshold        ("Depth Awareness",        Range(0.1, 10.0)) = 0.1
        _JitterIntensity       ("Smooth Jitter",          Range(0.0, 1.0))  = 0.0
        _NearSampleCutoffRatio ("Near Sample Cutoff Ratio", Range(0.1, 0.9)) = 0.5

        [Header(Dreamcore Settings)]
        _DreamHaze   ("Dream Haze",      Range(0.0, 1.0)) = 0.114
        _DreamChroma ("Dream Chromatic", Range(0.0, 5.0)) = 0.48

        [Header(Pixel and Mosaic Settings)]
        _PixelScale    ("Near Pixel Scale", Range(1.0, 15.0)) = 1.0
        _FarPixelScale ("Far Pixel Scale",  Range(1.0, 50.0)) = 1.0
        _PixelCurve    ("Pixel Curve",      Range(0.1, 5.0))  = 0.1
        _MoireReduction("Moire Reduction",  Range(0.0, 2.0))  = 0.012
        [Toggle(_LOCK_GRID)]   _LockGrid   ("Lock Grid",   Float) = 0
        [Toggle(_MOSAIC_MODE)] _MosaicMode ("Mosaic Mode", Float) = 0

        [Header(LockOn Focus Blur)]
        _PlayerProtectRadius ("Player Protect Radius (m)", Range(0.5, 10.0)) = 1.5
        _TargetProtectRadius ("Target Protect Radius (m)", Range(0.5, 10.0)) = 1.5
        _FocusBlurStrength   ("Focus Blur Strength",       Range(0.0, 5.0))  = 1.5
        _FocusBlurFalloff    ("Focus Blur Falloff",        Range(0.5, 5.0))  = 2.0

        // ── [Section C] Speed Response ────────────────────────────────────
        [Header(Speed Response)]
        _VignetteIntensity    ("Vignette Intensity",  Range(0.0, 1.0))  = 1.0
        _VignetteRadiusShrink ("Vignette Shrink",     Range(0.0, 0.5))  = 0.5
        _SpeedChromaIntensity ("Speed Chroma",        Range(0.0, 10.0)) = 10.0

        [Header(Lens Distortion)]
        _IdleLensDistortion  ("Idle Distortion",       Range(-1.0, 1.0)) = 0.0
        _SpeedLensDistortion ("Speed Distortion Boost",Range(-1.0, 1.0)) = 0.1
        _ZoomAutoFitScale    ("Zoom Auto Fit",         Range(0.0, 1.0))  = 0.359

        [Header(Peripheral Motion Blur)]
        _PeripheralMotionBlur ("Motion Blur Intensity", Range(0.0, 0.2)) = 0.2

        [Header(Mipmap Control)]
        _IdleMipmapRange      ("Idle Mip Range",   Range(0.0, 1.0))   = 0.105
        _SprintMipmapRange    ("Sprint Mip Range", Range(0.0, 1.0))   = 0.256
        _MipDebugLineThickness("Debug Line Width", Range(0.001, 0.05)) = 0.0049
        [Toggle(_MIP_DEBUG)] _MipDebug ("Enable Mipmap Debug", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "DreamcoreBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // SSMB Debug
            #pragma multi_compile _DEBUGMODE_OFF _DEBUGMODE_MOTIONVECTOR _DEBUGMODE_BLURVECTOR _DEBUGMODE_DEPTHMASK _DEBUGMODE_BLURSTRENGTH _DEBUGMODE_FINALBLUR _DEBUGMODE_DISTANCEBLUR

            // Distance Blur Feature Toggles
            #pragma shader_feature_local _USE_DISTORTION
            #pragma shader_feature_local _USE_CHROMA
            #pragma shader_feature_local _USE_MOTION_BLUR
            #pragma shader_feature_local _USE_MIPMAP
            #pragma shader_feature_local _USE_DISTANCE_BLUR

            // Distance Blur Type / Filter
            #pragma multi_compile _BLURTYPE_GAUSSIAN _BLURTYPE_DISK _BLURTYPE_SMOOTH _BLURTYPE_PIXEL _BLURTYPE_DREAM
            #pragma multi_compile _FILTERTYPE_NONE _FILTERTYPE_SNN _FILTERTYPE_KUWAHARA _FILTERTYPE_MEDIAN
            #pragma shader_feature_local _LOCK_GRID
            #pragma shader_feature_local _MOSAIC_MODE
            #pragma shader_feature_local _MIP_DEBUG

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // ── 텍스처 ───────────────────────────────────────────────────────
            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            float4 _BlitTexture_TexelSize;

            TEXTURE2D(_MotionVectorTexture);
            SAMPLER(sampler_MotionVectorTexture);

            // ── [CBUFFER] 모든 머티리얼 파라미터 격리 ────────────────────────
            // 이전 버그: CBUFFER 없음 → Blitter MPB 우선순위 = Global > Material
            //           → Global=0 (미초기화) 읽힘 → 캐릭터 블러, division-by-zero 등
            // 수정: UnityPerMaterial CBUFFER → 글로벌 override 원천 차단
            CBUFFER_START(UnityPerMaterial)
                // SSMB
                float _BlurScale;
                float _SSMBSamples;
                float _MaxBlurUV;
                float _RadialScale;
                float _SSBlurDepthNear;
                float _SSBlurDepthFade;
                float _ObjMotionThreshold;

                // Distance Blur - Zones
                float _NearDist;
                float _MidDist;
                float _FarDist;

                // Distance Blur - Intensity
                float _MidBlurSize;
                float _FarBlurSize;
                float _BlurExponent;
                float _CenterBlurScale;

                // Distance Blur - Artifact Correction
                float _DepthThreshold;
                float _JitterIntensity;
                float _NearSampleCutoffRatio;

                // Distance Blur - Pixel/Dream
                float _PixelScale;
                float _FarPixelScale;
                float _PixelCurve;
                float _MoireReduction;
                float _DreamHaze;
                float _DreamChroma;

                // Lock-On Focus Blur
                float _PlayerProtectRadius;
                float _TargetProtectRadius;
                float _FocusBlurStrength;
                float _FocusBlurFalloff;

                // Speed Response
                float _VignetteIntensity;
                float _VignetteRadiusShrink;
                float _SpeedChromaIntensity;

                // Lens Distortion
                float _IdleLensDistortion;
                float _SpeedLensDistortion;
                float _ZoomAutoFitScale;

                // Peripheral Motion Blur
                float _PeripheralMotionBlur;

                // Mipmap
                float _IdleMipmapRange;
                float _SprintMipmapRange;
                float _MipDebugLineThickness;
            CBUFFER_END

            // ── 글로벌 파라미터 (SCM 의도적 주입 — CBUFFER 밖) ───────────────
            float _SSMBIntensityScale;   // 락온 시 SSMB 강도 배율
            float _SSMBDistanceScale;    // P5 거리별 블러 배율
            float _SSMBBudgetScale;      // P7 Budget 배율
            float _GlobalSpeedFactor;    // SCM 주입 — 속도 기반 효과 강도
            float _GlobalMovementPulse;  // SCM 주입 — 이동 펄스
            float _LockOnActive;         // SCM 주입 — 락온 여부
            float _VFXMipBias;           // VFXBlur 시스템 주입

            // ── 구조체 ───────────────────────────────────────────────────────
            struct Attributes { uint vertexID : SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord   : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ── 유틸리티 ─────────────────────────────────────────────────────
            float3 VisualizeVector(float2 v, float scale)
            {
                float2 s = v * scale;
                float3 c = 0;
                c.r = max(0, s.x); c.b = max(0, -s.x); c.g = max(0, s.y);
                if (s.y < 0) { c.r += max(0, -s.y); c.b += max(0, -s.y); }
                return saturate(c);
            }

            float2 ApplyLensDistortion(float2 uv, float intensity, float zoomScale)
            {
                float2 delta = uv - 0.5;
                float distSq = dot(delta, delta);
                float autoZoom = 1.0 / (1.0 + abs(intensity) * zoomScale);
                return 0.5 + delta * (1.0 + distSq * intensity) * autoZoom;
            }

            float CustomIGN(float2 pix)
            {
                float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
                return frac(magic.z * frac(dot(pix, magic.xy)));
            }

            // ── Distance Blur GetWeight ───────────────────────────────────────
            float GetWeight(float centerDepth, float sampleDepth)
            {
                float depthDiff = abs(centerDepth - sampleDepth);
                return 1.0 / (1.0 + depthDiff * _DepthThreshold);
            }

            // ── Distance Blur 커널들 ──────────────────────────────────────────
            float4 ApplySNN(float2 uv, float4 centerColor)
            {
                float4 sum = centerColor; float totalWeight = 1.0;
                float2 ts = _BlitTexture_TexelSize.xy;
                float2 offsets[4] = { float2(-1,-1), float2(0,-1), float2(1,-1), float2(-1,0) };
                [unroll]
                for (int i = 0; i < 4; i++) {
                    float4 c1 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv + offsets[i]*ts, 0);
                    float4 c2 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv - offsets[i]*ts, 0);
                    sum += (dot(c1-centerColor,c1-centerColor) < dot(c2-centerColor,c2-centerColor)) ? c1 : c2;
                    totalWeight += 1.0;
                }
                return sum / totalWeight;
            }

            float4 GetGaussian(float2 uv, float strength, float centerDepth)
            {
                float4 color = 0; float totalWeight = 0;
                float2 ts = _BlitTexture_TexelSize.xy * strength;
                float gW[9] = { 0.0625, 0.125, 0.0625, 0.125, 0.25, 0.125, 0.0625, 0.125, 0.0625 };
                float2 off[9] = { float2(-1,-1),float2(0,-1),float2(1,-1),float2(-1,0),float2(0,0),float2(1,0),float2(-1,1),float2(0,1),float2(1,1) };
                float cutoff = centerDepth * _NearSampleCutoffRatio;
                [unroll]
                for (int i = 0; i < 9; i++) {
                    float2 sUV = uv + off[i]*ts;
                    float sD = LinearEyeDepth(SampleSceneDepth(sUV), _ZBufferParams);
                    if (sD < cutoff) continue;
                    float w = gW[i] * GetWeight(centerDepth, sD);
                    color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV, 0) * w;
                    totalWeight += w;
                }
                return color / max(totalWeight, 0.001);
            }

            float4 GetDisk(float2 uv, float strength, float centerDepth)
            {
                float4 color = 0; float totalWeight = 0;
                float2 ts = _BlitTexture_TexelSize.xy * strength;
                float2 off[13] = { float2(0,0),float2(0,1),float2(0,-1),float2(1,0),float2(-1,0),float2(0.7,0.7),float2(-0.7,0.7),float2(0.7,-0.7),float2(-0.7,-0.7),float2(0,2),float2(0,-2),float2(2,0),float2(-2,0) };
                float cutoff = centerDepth * _NearSampleCutoffRatio;
                [unroll]
                for (int i = 0; i < 13; i++) {
                    float2 sUV = uv + off[i]*ts;
                    float sD = LinearEyeDepth(SampleSceneDepth(sUV), _ZBufferParams);
                    if (sD < cutoff) continue;
                    float w = GetWeight(centerDepth, sD);
                    color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV, 0) * w;
                    totalWeight += w;
                }
                return color / max(totalWeight, 0.001);
            }

            float4 GetSmooth(float2 uv, float strength, float2 screenPos, float centerDepth)
            {
                float4 color = 0; float totalWeight = 0;
                float noise = CustomIGN(screenPos) * 6.283185;
                float cosN = cos(noise)*strength; float sinN = sin(noise)*strength;
                float2x2 rot = float2x2(cosN,-sinN,sinN,cosN);
                float2 off[8] = { float2(1,0),float2(-1,0),float2(0,1),float2(0,-1),float2(0.5,0.5),float2(-0.5,0.5),float2(0.5,-0.5),float2(-0.5,-0.5) };
                float cutoff = centerDepth * _NearSampleCutoffRatio;
                [unroll]
                for (int i = 0; i < 8; i++) {
                    float2 sUV = uv + mul(rot, off[i]) * _BlitTexture_TexelSize.xy;
                    float sD = LinearEyeDepth(SampleSceneDepth(sUV), _ZBufferParams);
                    if (sD < cutoff) continue;
                    float w = GetWeight(centerDepth, sD);
                    color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV, 0) * w;
                    totalWeight += w;
                }
                return color / max(totalWeight, 0.001);
            }

            float4 GetDreamBlur(float2 uv, float strength, float centerDepth)
            {
                float4 color = 0; float totalWeight = 0;
                float2 ts = _BlitTexture_TexelSize.xy * strength;
                float2 chromaOff = _BlitTexture_TexelSize.xy * _DreamChroma * strength;
                float2 off[8] = { float2(0,1),float2(0,-1),float2(1,0),float2(-1,0),float2(0.7,0.7),float2(-0.7,0.7),float2(0.7,-0.7),float2(-0.7,-0.7) };
                float cutoff = centerDepth * _NearSampleCutoffRatio;
                [unroll]
                for (int i = 0; i < 8; i++) {
                    float2 sUV = uv + off[i]*ts;
                    float sD = LinearEyeDepth(SampleSceneDepth(sUV), _ZBufferParams);
                    if (sD < cutoff) continue;
                    float w = GetWeight(centerDepth, sD);
                    float r = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV + chromaOff, 0).r;
                    float g = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV, 0).g;
                    float b = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV - chromaOff, 0).b;
                    color += float4(r,g,b,1) * w; totalWeight += w;
                }
                float4 fb = color / max(totalWeight, 0.001);
                float bri = max(fb.r, max(fb.g, fb.b));
                float4 hazy = fb + fb * bri * _DreamHaze * 2.0;
                return lerp(hazy, float4(1,1,1,1), _DreamHaze * bri * 0.4);
            }

            float4 GetPixelBlur(float2 uv, float strength, float centerDepth, float2 screenPos, float normalizedDistance)
            {
                float currentPixelScale = lerp(_PixelScale, _FarPixelScale, pow(normalizedDistance, _PixelCurve));
                float2 coordBase, grid;
                #if defined(_LOCK_GRID)
                    coordBase = uv * _ScreenParams.xy; grid = max(1.0, currentPixelScale);
                #else
                    coordBase = uv; grid = max(_BlitTexture_TexelSize.xy, _BlitTexture_TexelSize.xy * currentPixelScale);
                #endif
                float2 snappedBase = (floor(coordBase/grid+0.00001)*grid)+(grid*0.5);
                float noise = CustomIGN(snappedBase);
                float angle = noise * 6.283185 * _MoireReduction;
                float2x2 rotMat = float2x2(cos(angle),-sin(angle),sin(angle),cos(angle));
                float4 color = 0; float totalWeight = 0;
                float cutoff = centerDepth * _NearSampleCutoffRatio;
                float2 subOff[4] = { float2(-0.25,-0.25),float2(0.25,-0.25),float2(-0.25,0.25),float2(0.25,0.25) };
                [unroll] for (int s=0; s<4; s++) {
                    float2 jp = coordBase + subOff[s]*grid;
                    float2 qp = (floor(jp/grid+0.00001)*grid)+(grid*0.5);
                    [unroll] for (int x=-1; x<=1; x++) [unroll] for (int y=-1; y<=1; y++) {
                        float2 nOff = mul(rotMat, float2(x,y))*grid*strength;
                        float2 fUV;
                        #if defined(_LOCK_GRID)
                            fUV = (qp+nOff)/_ScreenParams.xy;
                        #else
                            fUV = qp+nOff;
                        #endif
                        float sD = LinearEyeDepth(SampleSceneDepth(fUV), _ZBufferParams);
                        if (sD < cutoff) continue;
                        float w = GetWeight(centerDepth, sD);
                        color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, fUV, 0) * w;
                        totalWeight += w;
                    }
                }
                return color / max(totalWeight, 0.001);
            }

            // ── Vertex ───────────────────────────────────────────────────────
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
                output.texcoord  = uv;
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            // ── Fragment ─────────────────────────────────────────────────────
            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // ════════════════════════════════════════════════════════
                // STEP 1: Distortion (Distance Blur 파트)
                // ════════════════════════════════════════════════════════
                float2 uv = input.texcoord;
                #if defined(_USE_DISTORTION)
                    float distIntensity = _IdleLensDistortion + _SpeedLensDistortion * _GlobalSpeedFactor;
                    uv = ApplyLensDistortion(uv, distIntensity, _ZoomAutoFitScale);
                #endif

                float2 toCenter     = uv - 0.5;
                float  distFromCenter = length(toCenter);

                // ════════════════════════════════════════════════════════
                // STEP 2: Mipmap
                // ════════════════════════════════════════════════════════
                float currentMipRange = lerp(_IdleMipmapRange, _SprintMipmapRange, _GlobalSpeedFactor);
                float mipMask = smoothstep(currentMipRange + 0.05, currentMipRange, distFromCenter);
                float mipLevel = 0.0;
                #if defined(_USE_MIPMAP)
                    mipLevel = max(0.0, abs(_VFXMipBias) * mipMask);
                    // [FIX] 근거리 픽셀 mip 블러 방지 — 캐릭터 mip 블러 패치
                    float _earlyD = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                    if (_earlyD < _NearDist) mipLevel = 0.0;
                #endif

                // ════════════════════════════════════════════════════════
                // STEP 3: Vignette
                // ════════════════════════════════════════════════════════
                float dynamicRadius = 0.5 - (_GlobalSpeedFactor * _VignetteRadiusShrink) + _GlobalMovementPulse;
                float vignetteMask  = smoothstep(dynamicRadius, dynamicRadius + 0.4, distFromCenter);
                float vignetteValue = 1.0 - (vignetteMask * _VignetteIntensity * _GlobalSpeedFactor);

                // ════════════════════════════════════════════════════════
                // STEP 4: 기본 색상 샘플링 (Speed Chroma / Peripheral MB)
                // ════════════════════════════════════════════════════════
                float4 color = 0;

                float motionBlurAmount = 0;
                #if defined(_USE_MOTION_BLUR)
                    motionBlurAmount = _PeripheralMotionBlur * _GlobalSpeedFactor * vignetteMask;
                #endif

                if (motionBlurAmount > 0.001)
                {
                    [unroll]
                    for (int i = 0; i < 4; i++) {
                        float2 mOff = toCenter * (motionBlurAmount * float(i) / 3.0);
                        color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv - mOff, mipLevel);
                    }
                    color /= 4.0;
                }
                else
                {
                    #if defined(_USE_CHROMA)
                        if (_GlobalSpeedFactor > 0.1 && vignetteMask > 0.01) {
                            float sc = _SpeedChromaIntensity * _GlobalSpeedFactor * vignetteMask;
                            float2 cOff = toCenter * sc * _BlitTexture_TexelSize.xy;
                            float r = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv + cOff, mipLevel).r;
                            float g = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv,        mipLevel).g;
                            float b = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv - cOff, mipLevel).b;
                            color = float4(r, g, b, 1.0);
                        } else {
                            color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv, mipLevel);
                        }
                    #else
                        color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv, mipLevel);
                    #endif
                }

                // ════════════════════════════════════════════════════════
                // STEP 5: SSMB — Screen Space Motion Blur
                // ════════════════════════════════════════════════════════
                float4 original = color;

                float2 mv    = SAMPLE_TEXTURE2D(_MotionVectorTexture, sampler_MotionVectorTexture, input.texcoord).rg;
                float  mvLen = length(mv);

                // Debug: Motion Vector
                #if defined(_DEBUGMODE_MOTIONVECTOR)
                    return float4(VisualizeVector(mv, 100.0), 1.0);
                #endif

                // Object Motion Separation: OMB 픽셀은 SSMB 스킵
                bool skipSSMB = (_ObjMotionThreshold > 0.0 && mvLen > _ObjMotionThreshold);
                if (!skipSSMB)
                {
                    // SSMB Intensity
                    float intensityScale = _SSMBIntensityScale < 0.001 ? 1.0 : max(_SSMBIntensityScale, 0.0);
                    float distScale      = _SSMBDistanceScale  < 0.001 ? 1.0 : max(_SSMBDistanceScale, 0.0);
                    float budgetScale    = _SSMBBudgetScale     < 0.001 ? 1.0 : max(_SSMBBudgetScale, 0.0);

                    float2 blurVec = mv * _BlurScale * intensityScale * distScale * budgetScale;

                    // Radial Weight
                    float2 toEdge    = input.texcoord - 0.5;
                    float  edgeDist  = length(toEdge);
                    float2 mvNorm    = mvLen > 0.0001 ? mv / mvLen     : float2(0,0);
                    float2 edgeNorm  = edgeDist > 0.001 ? toEdge / edgeDist : float2(0,0);
                    float  radialDot = saturate(dot(mvNorm, edgeNorm));
                    blurVec *= lerp(1.0, 1.0 + edgeDist * _RadialScale, radialDot);

                    float maxUV = max(_MaxBlurUV, 0.001);
                    blurVec = clamp(blurVec, -maxUV, maxUV);

                    #if defined(_DEBUGMODE_BLURVECTOR)
                        return float4(VisualizeVector(blurVec, 1.0 / maxUV), 1.0);
                    #endif

                    // SSMB Depth Mask — CBUFFER로 격리됨: Material 값 정확히 읽힘
                    float linearDepthSSMB = LinearEyeDepth(SampleSceneDepth(input.texcoord), _ZBufferParams);
                    float depthMask = (_SSBlurDepthNear < 0.01)
                        ? 0.0
                        : saturate((linearDepthSSMB - _SSBlurDepthNear) / max(_SSBlurDepthFade, 0.1));

                    #if defined(_DEBUGMODE_DEPTHMASK)
                        float boundary = abs(linearDepthSSMB - _SSBlurDepthNear) < 0.2 ? 1.0 : 0.0;
                        return float4(depthMask + boundary, depthMask, depthMask, 1.0);
                    #endif

                    float blurStrength = saturate(length(blurVec) / maxUV) * depthMask;

                    #if defined(_DEBUGMODE_BLURSTRENGTH)
                        return float4(blurStrength, blurStrength * 0.5, 0.0, 1.0);
                    #endif

                    if (blurStrength >= 0.005)
                    {
                        int N = (int)clamp(_SSMBSamples, 4.0, 16.0);
                        float4 acc = 0;
                        for (int s = 0; s < N; s++) {
                            float  t  = (float)s / max((float)(N-1), 1.0) - 0.5;
                            acc += SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_BlitTexture, uv + blurVec * t, 0);
                        }
                        acc /= (float)N;

                        #if defined(_DEBUGMODE_FINALBLUR)
                            return acc;
                        #endif

                        color = lerp(original, acc, blurStrength);
                    }
                }

                // ════════════════════════════════════════════════════════
                // STEP 6: Distance Blur
                // ════════════════════════════════════════════════════════
                // ── Distance Blur Section ──
                {
                    // Filters (optional pre-pass)
                    #if defined(_FILTERTYPE_SNN)
                        color = ApplySNN(uv, color);
                    #endif

                    // Mip Debug
                    #if defined(_MIP_DEBUG)
                        float distToEdge = abs(distFromCenter - currentMipRange);
                        float lineVis = smoothstep(_MipDebugLineThickness + 0.005, _MipDebugLineThickness, distToEdge);
                        float biasAlpha = saturate(abs(_VFXMipBias) / 2.0);
                        color.rgb = lerp(color.rgb, float3(0.0, 0.4, 1.0), lineVis * biasAlpha * 10.0);
                    #endif

                    #if defined(_USE_DISTANCE_BLUR)
                        float depth = SampleSceneDepth(uv);
                        float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
                        float strength = 0; float mixWeight = 0; float normalizedDist = 0;

                        // 거리 구간별 블러 강도
                        if (linearDepth < _NearDist) {
                            strength = 0; mixWeight = 0;
                        }
                        else if (linearDepth < _MidDist) {
                            normalizedDist = saturate((linearDepth - _NearDist) / max(_MidDist - _NearDist, 0.001));
                            mixWeight = pow(normalizedDist, _BlurExponent);
                            strength  = mixWeight * _MidBlurSize;
                        }
                        else {
                            normalizedDist = saturate((linearDepth - _MidDist) / max(_FarDist - _MidDist, 0.001));
                            float farMix  = pow(normalizedDist, _BlurExponent);
                            mixWeight = farMix;
                            strength  = lerp(_MidBlurSize, _FarBlurSize, farMix);
                        }

                        // Near Protect Mask
                        float nearProtectMask = smoothstep(_NearDist - 0.5, _NearDist + 0.5, linearDepth);
                        mixWeight *= nearProtectMask;
                        strength  *= nearProtectMask;

                        // Center Blur Suppress
                        float centerSuppress = lerp(_CenterBlurScale, 1.0, 1.0 - mipMask);
                        strength  *= centerSuppress;
                        mixWeight *= centerSuppress;

                        // Speed boost
                        strength *= (1.0 + vignetteMask * _GlobalSpeedFactor * 1.5);

                        // Lock-On Focus Blur
                        if (_LockOnActive > 0.5) {
                            float playerProtect  = max(_PlayerProtectRadius, 0.1);
                            float targetProtect  = max(_TargetProtectRadius, 0.1);
                            float protectedDepth = min(playerProtect, targetProtect);
                            if (linearDepth > protectedDepth && linearDepth < _MidDist) {
                                float focusFactor   = pow(saturate((linearDepth - protectedDepth) / max(_MidDist - protectedDepth, 0.1)), _FocusBlurFalloff);
                                strength  = max(strength, focusFactor * _FocusBlurStrength);
                                mixWeight = max(mixWeight, focusFactor * saturate(_FocusBlurStrength / 5.0));
                            }
                        }

                        #if defined(_DEBUGMODE_DISTANCEBLUR)
                            return float4(mixWeight, mixWeight * 0.5, 0, 1);
                        #endif

                        // Distance Blur 커널 적용
                        if (mixWeight > 0.001)
                        {
                            float4 blurredColor;
                            #if defined(_BLURTYPE_GAUSSIAN)
                                blurredColor = GetGaussian(uv, strength, linearDepth);
                            #elif defined(_BLURTYPE_DISK)
                                blurredColor = GetDisk(uv, strength, linearDepth);
                            #elif defined(_BLURTYPE_SMOOTH)
                                blurredColor = GetSmooth(uv, strength, input.positionCS.xy, linearDepth);
                            #elif defined(_BLURTYPE_PIXEL)
                                blurredColor = GetPixelBlur(uv, strength, linearDepth, input.positionCS.xy, normalizedDist);
                            #else // DREAM
                                blurredColor = GetDreamBlur(uv, strength, linearDepth);
                            #endif

                            color = lerp(color, blurredColor, mixWeight);
                        }
                    #endif
                }

                // ════════════════════════════════════════════════════════
                // STEP 7: Vignette 적용 후 최종 출력
                // ════════════════════════════════════════════════════════
                color.rgb *= vignetteValue;
                return color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
