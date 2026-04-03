Shader "Hidden/URP/DistanceBlurFullScreen"
{
    Properties
    {
        // SECTION 1: Feature Toggles
        [Header(Feature Toggles)]
        [Toggle(_USE_DISTORTION)]    _UseDistortion  ("Enable Distortion",    Float) = 1
        [Toggle(_USE_CHROMA)]        _UseChroma      ("Enable Speed Chroma",  Float) = 1
        [Toggle(_USE_MOTION_BLUR)]   _UseMotionBlur  ("Enable Motion Blur",   Float) = 1
        [Toggle(_USE_MIPMAP)]        _UseMipmap      ("Enable Mipmap",        Float) = 1
        [Toggle(_USE_DISTANCE_BLUR)] _UseDistanceBlur("Enable Distance Blur", Float) = 1

        // SECTION 2: Distance Blur Settings
        [Header(Distance Zones)]
        _NearDist ("Near Distance", Float) = 7.0
        _MidDist  ("Mid Distance",  Float) = 25.0
        _FarDist  ("Far Distance",  Float) = 50.0

        [Header(Blur Intensity)]
        [KeywordEnum(Gaussian, Disk, Smooth, Pixel, Dream)] _BlurType ("Blur Method", Float) = 3
        _MidBlurSize  ("Mid Blur Size",            Range(0.0, 20.0)) = 4.0
        _FarBlurSize  ("Far Blur Size",            Range(0.0, 30.0)) = 12.0
        _BlurExponent ("Distance Curve Exponent",  Range(1.0, 5.0))  = 2.0
        _CenterBlurScale ("Center Blur Strength",  Range(0.0, 1.0))  = 0.2

        [Header(Artifact Correction)]
        [KeywordEnum(None, SNN, Kuwahara, Median)] _FilterType ("Filter Method", Float) = 1
        _DepthThreshold      ("Depth Awareness",        Range(0.1, 10.0)) = 2.0
        _JitterIntensity     ("Smooth Jitter",          Range(0.0, 1.0))  = 1.0
        _NearSampleCutoffRatio ("Near Sample Cutoff Ratio", Range(0.1, 0.9)) = 0.5

        [Header(Pixel and Mosaic Settings)]
        _PixelScale    ("Near Pixel Scale",  Range(1.0, 15.0)) = 4.0
        _FarPixelScale ("Far Pixel Scale",   Range(1.0, 50.0)) = 15.0
        _PixelCurve    ("Pixel Curve",       Range(0.1, 5.0))  = 1.0
        _MoireReduction("Moire Reduction",   Range(0.0, 2.0))  = 0.5
        [Toggle(_LOCK_GRID)]   _LockGrid   ("Lock Grid",   Float) = 1
        [Toggle(_MOSAIC_MODE)] _MosaicMode ("Mosaic Mode", Float) = 0

        [Header(Dreamcore Settings)]
        _DreamHaze   ("Dream Haze",     Range(0.0, 1.0)) = 0.5
        _DreamChroma ("Dream Chromatic",Range(0.0, 5.0)) = 1.5

        [Header(LockOn Focus Blur)]
        _PlayerProtectRadius ("Player Protect Radius (m)", Range(0.5, 10.0)) = 1.5
        _TargetProtectRadius ("Target Protect Radius (m)", Range(0.5, 10.0)) = 1.5
        _FocusBlurStrength   ("Focus Blur Strength",       Range(0.0, 5.0))  = 1.5
        _FocusBlurFalloff    ("Focus Blur Falloff",        Range(0.5, 5.0))  = 2.0

        // SECTION 3: Speed Response
        [Header(Phase 2 Speed Response)]
        _VignetteIntensity   ("Vignette Intensity", Range(0.0, 1.0))  = 0.8
        _VignetteRadiusShrink("Vignette Shrink",    Range(0.0, 0.5))  = 0.2
        _SpeedChromaIntensity("Speed Chroma",       Range(0.0, 10.0)) = 4.0

        [Header(Phase 2 Lens Distortion)]
        _IdleLensDistortion ("Idle Distortion",       Range(-1.0, 1.0)) = -0.1
        _SpeedLensDistortion("Speed Distortion Boost",Range(-1.0, 1.0)) = -0.3
        _ZoomAutoFitScale   ("Zoom Auto Fit",         Range(0.0, 1.0))  = 0.25

        [Header(Phase 2 Motion Blur)]
        _PeripheralMotionBlur ("Motion Blur Intensity", Range(0.0, 0.2)) = 0.05

        [Header(Phase 2 Mipmap Control)]
        _IdleMipmapRange     ("Idle Mip Range",   Range(0.0, 1.0))   = 0.3
        _SprintMipmapRange   ("Sprint Mip Range", Range(0.0, 1.0))   = 0.7
        _MipDebugLineThickness("Debug Line Width",Range(0.001, 0.05)) = 0.01
        [Toggle(_MIP_DEBUG)] _MipDebug ("Enable Mipmap Debug", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "DistanceBlurPassV3"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            
            // Feature Toggles
            #pragma shader_feature_local _USE_DISTORTION
            #pragma shader_feature_local _USE_CHROMA
            #pragma shader_feature_local _USE_MOTION_BLUR
            #pragma shader_feature_local _USE_MIPMAP
            #pragma shader_feature_local _USE_DISTANCE_BLUR
            
            #pragma multi_compile _BLURTYPE_GAUSSIAN _BLURTYPE_DISK _BLURTYPE_SMOOTH _BLURTYPE_PIXEL _BLURTYPE_DREAM
            #pragma multi_compile _FILTERTYPE_NONE _FILTERTYPE_SNN _FILTERTYPE_KUWAHARA _FILTERTYPE_MEDIAN
            #pragma shader_feature_local _LOCK_GRID
            #pragma shader_feature_local _MOSAIC_MODE
            #pragma shader_feature_local _MIP_DEBUG

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            
            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            float4 _BlitTexture_TexelSize;

            float _NearDist, _MidDist, _FarDist;
            float _MidBlurSize, _FarBlurSize, _BlurExponent, _CenterBlurScale;
            float _DepthThreshold, _JitterIntensity;
            float _NearSampleCutoffRatio; // [FIX P2] 근거리 샘플 하드 배제 비율
            float _PixelScale, _FarPixelScale, _PixelCurve, _MoireReduction;
            float _DreamHaze, _DreamChroma;

            float _GlobalSpeedFactor;
            float _GlobalMovementPulse;
            // Lock-On Focus Blur — SCM이 Shader.SetGlobalFloat으로 주입
            float _LockOnActive;
            float _PlayerProtectRadius;
            float _TargetProtectRadius;
            float _FocusBlurStrength;
            float _FocusBlurFalloff;
            float _VignetteIntensity, _VignetteRadiusShrink, _SpeedChromaIntensity;
            
            float _IdleLensDistortion, _SpeedLensDistortion, _ZoomAutoFitScale;
            float _PeripheralMotionBlur;
            float _IdleMipmapRange, _SprintMipmapRange, _MipDebugLineThickness;

            float _VFXMipBias;        

            // GetWeight function at the top
            // [FIX P1] 단방향 → 양방향 깊이 보호.
            // 기존: max(0, center-sample) 로 인해 sample < center 일 때만 억제됨.
            //       → 원거리 픽셀(center=80m) 커널이 근거리 샘플(sample=2m)을 포함해도
            //         가중치가 감소하지 않아 캐릭터 색상이 배경 블러에 흡수되는 버그 발생.
            // 수정: abs(center-sample) 로 양방향 억제.
            //       → 깊이 차이가 클수록 방향에 관계없이 가중치를 0에 수렴시킴.
            float GetWeight(float centerDepth, float sampleDepth)
            {
                float depthDiff = abs(centerDepth - sampleDepth);
                return 1.0 / (1.0 + depthDiff * _DepthThreshold);
            }

            // Custom IGN function to avoid naming conflict with Unity Core.hlsl
            float CustomIGN(float2 pix)
            {
                float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
                return frac(magic.z * frac(dot(pix, magic.xy)));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float2 ApplyLensDistortionDynamic(float2 uv, float totalIntensity, float zoomScale)
            {
                float2 delta = uv - 0.5;
                float distSq = dot(delta, delta);
                float autoZoom = 1.0 / (1.0 + abs(totalIntensity) * zoomScale);
                float factor = (1.0 + distSq * totalIntensity) * autoZoom;
                return 0.5 + delta * factor;
            }

            // --- Blur Core logic ---
            float4 ApplySNN(float2 uv, float4 centerColor)
            {
                float4 sum = centerColor; float totalWeight = 1.0;
                float2 texelSize = _BlitTexture_TexelSize.xy;
                float2 offsets[4] = { float2(-1,-1), float2(0,-1), float2(1,-1), float2(-1,0) };
                [unroll]
                for(int i = 0; i < 4; i++) {
                    float2 off = offsets[i] * texelSize;
                    float4 c1 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv + off, 0.0);
                    float4 c2 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv - off, 0.0);
                    sum += (dot(c1 - centerColor, c1 - centerColor) < dot(c2 - centerColor, c2 - centerColor)) ? c1 : c2;
                    totalWeight += 1.0;
                }
                return sum / totalWeight;
            }

            float4 ApplyKuwahara(float2 uv)
            {
                float2 texelSize = _BlitTexture_TexelSize.xy;
                float4 m[4]; float3 s[4];
                for(int k=0; k<4; k++) { m[k] = 0; s[k] = 0; }
                float2 offsets[9] = { float2(-1,-1), float2(0,-1), float2(1,-1), float2(-1,0), float2(0,0), float2(1,0), float2(-1,1), float2(0,1), float2(1,1) };
                int regions[9] = { 0, 0, 1, 0, 0, 1, 2, 2, 3 };
                [unroll]
                for(int j=0; j<9; j++) {
                    float4 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv + offsets[j] * texelSize, 0.0);
                    int r = regions[j]; m[r] += c; s[r] += c.rgb * c.rgb;
                }
                float minVar = 1e10; float4 finalCol = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv, 0.0);
                [unroll]
                for(int l=0; l<4; l++) {
                    m[l] /= 2.25; s[l] = abs(s[l] / 2.25 - m[l].rgb * m[l].rgb);
                    float var = s[l].r + s[l].g + s[l].b;
                    if(var < minVar) { minVar = var; finalCol = m[l]; }
                }
                return finalCol;
            }

            float4 ApplyMedian(float2 uv)
            {
                float2 texelSize = _BlitTexture_TexelSize.xy;
                float4 c[9];
                [unroll]
                for(int i=0; i<3; i++)
                    for(int j=0; j<3; j++)
                        c[i*3+j] = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv + float2(i-1, j-1) * texelSize, 0.0);
                return (c[4] + c[3] + c[5]) / 3.0; 
            }

            float4 GetGaussian(float2 uv, float strength, float centerDepth)
            {
                float4 color = 0; float totalWeight = 0;
                float2 texelSize = _BlitTexture_TexelSize.xy * strength;
                float gWeights[9] = { 0.0625, 0.125, 0.0625, 0.125, 0.25, 0.125, 0.0625, 0.125, 0.0625 };
                float2 offsets[9] = { float2(-1,-1), float2(0,-1), float2(1,-1), float2(-1,0), float2(0,0), float2(1,0), float2(-1,1), float2(0,1), float2(1,1) };
                // [FIX P2] 근거리 하드 배제 임계값: centerDepth의 절반 미만 샘플은 커널에서 제외.
                // 원거리 배경 픽셀(80m)이 캐릭터 픽셀(2m)을 샘플링하면 cutoff=40m 미만이므로 skip.
                float cutoff = centerDepth * _NearSampleCutoffRatio;
                [unroll]
                for(int i = 0; i < 9; i++) {
                    float2 sUV = uv + offsets[i] * texelSize;
                    float sDepth = LinearEyeDepth(SampleSceneDepth(sUV), _ZBufferParams);
                    // [FIX P2] 근거리 샘플 조기 배제
                    if (sDepth < cutoff) continue;
                    float w = gWeights[i] * GetWeight(centerDepth, sDepth);
                    color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV, 0.0) * w;
                    totalWeight += w;
                }
                return color / max(totalWeight, 0.001);
            }

            float4 GetDisk(float2 uv, float strength, float centerDepth)
            {
                float4 color = 0; float totalWeight = 0;
                float2 texelSize = _BlitTexture_TexelSize.xy * strength;
                float2 offsets[13] = { float2(0,0), float2(0,1), float2(0,-1), float2(1,0), float2(-1,0), float2(0.7,0.7), float2(-0.7,0.7), float2(0.7,-0.7), float2(-0.7,-0.7), float2(0,2), float2(0,-2), float2(2,0), float2(-2,0) };
                // [FIX P2] 근거리 하드 배제 임계값
                float cutoff = centerDepth * _NearSampleCutoffRatio;
                [unroll]
                for(int i = 0; i < 13; i++) {
                    float2 sUV = uv + offsets[i] * texelSize;
                    float sDepth = LinearEyeDepth(SampleSceneDepth(sUV), _ZBufferParams);
                    // [FIX P2] 근거리 샘플 조기 배제
                    if (sDepth < cutoff) continue;
                    float w = GetWeight(centerDepth, sDepth);
                    color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV, 0.0) * w;
                    totalWeight += w;
                }
                return color / max(totalWeight, 0.001);
            }

            float4 GetSmooth(float2 uv, float strength, float2 screenPos, float centerDepth)
            {
                float4 color = 0; float totalWeight = 0;
                float noise = CustomIGN(screenPos) * 6.283185;
                float cosN = cos(noise) * strength; float sinN = sin(noise) * strength;
                float2x2 rot = float2x2(cosN, -sinN, sinN, cosN);
                float2 offsets[8] = { float2(1,0), float2(-1,0), float2(0,1), float2(0,-1), float2(0.5,0.5), float2(-0.5,0.5), float2(0.5,-0.5), float2(-0.5,-0.5) };
                // [FIX P2] 근거리 하드 배제 임계값
                float cutoff = centerDepth * _NearSampleCutoffRatio;
                [unroll]
                for(int i = 0; i < 8; i++) {
                    float2 sUV = uv + mul(rot, offsets[i]) * _BlitTexture_TexelSize.xy;
                    float sDepth = LinearEyeDepth(SampleSceneDepth(sUV), _ZBufferParams);
                    // [FIX P2] 근거리 샘플 조기 배제
                    if (sDepth < cutoff) continue;
                    float w = GetWeight(centerDepth, sDepth);
                    color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV, 0.0) * w;
                    totalWeight += w;
                }
                return color / max(totalWeight, 0.001);
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
                float2 snappedBase = (floor(coordBase / grid + 0.00001) * grid) + (grid * 0.5);
                float noise = CustomIGN(snappedBase); 
                float angle = noise * 6.283185 * _MoireReduction;
                float2x2 rotMat = float2x2(cos(angle), -sin(angle), sin(angle), cos(angle));
                float4 color = 0; float totalWeight = 0;
                // [FIX P2] 근거리 하드 배제 임계값 (GetPixelBlur 공통)
                float cutoff = centerDepth * _NearSampleCutoffRatio;

                #if defined(_MOSAIC_MODE)
                    [unroll] for(int x = -1; x <= 1; x++) [unroll] for(int y = -1; y <= 1; y++) {
                        float2 neighborOffset = mul(rotMat, float2(x, y)) * grid * strength;
                        float2 finalUV;
                        #if defined(_LOCK_GRID)
                            finalUV = (snappedBase + neighborOffset) / _ScreenParams.xy;
                        #else
                            finalUV = snappedBase + neighborOffset;
                        #endif
                        float sDepth = LinearEyeDepth(SampleSceneDepth(finalUV), _ZBufferParams);
                        // [FIX P2] 근거리 샘플 조기 배제
                        if (sDepth < cutoff) continue;
                        float w = GetWeight(centerDepth, sDepth);
                        color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, finalUV, 0.0) * w;
                        totalWeight += w;
                    }
                #else
                    float2 subOffsets[4] = { float2(-0.25,-0.25), float2(0.25,-0.25), float2(-0.25,0.25), float2(0.25,0.25) };
                    [unroll] for(int s = 0; s < 4; s++) {
                        float2 jitterPos = coordBase + subOffsets[s] * grid;
                        float2 qPoint = (floor(jitterPos / grid + 0.00001) * grid) + (grid * 0.5);
                        [unroll] for(int x = -1; x <= 1; x++) [unroll] for(int y = -1; y <= 1; y++) {
                            float2 neighborOffset = mul(rotMat, float2(x, y)) * grid * strength;
                            float2 finalUV;
                            #if defined(_LOCK_GRID)
                                finalUV = (qPoint + neighborOffset) / _ScreenParams.xy;
                            #else
                                finalUV = qPoint + neighborOffset;
                            #endif
                            float sDepth = LinearEyeDepth(SampleSceneDepth(finalUV), _ZBufferParams);
                            // [FIX P2] 근거리 샘플 조기 배제
                            if (sDepth < cutoff) continue;
                            float w = GetWeight(centerDepth, sDepth);
                            color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, finalUV, 0.0) * w;
                            totalWeight += w;
                        }
                    }
                #endif
                return color / max(totalWeight, 0.001);
            }

            float4 GetDreamBlur(float2 uv, float strength, float centerDepth)
            {
                float4 color = 0; float totalWeight = 0;
                float2 texelSize = _BlitTexture_TexelSize.xy * strength;
                float2 chromaOffset = _BlitTexture_TexelSize.xy * _DreamChroma * strength;
                float2 offsets[8] = { float2(0,1), float2(0,-1), float2(1,0), float2(-1,0), float2(0.7,0.7), float2(-0.7,0.7), float2(0.7,-0.7), float2(-0.7,-0.7) };
                // [FIX P2] 근거리 하드 배제 임계값
                float cutoff = centerDepth * _NearSampleCutoffRatio;
                [unroll]
                for(int i = 0; i < 8; i++) {
                    float2 sUV = uv + offsets[i] * texelSize;
                    float sDepth = LinearEyeDepth(SampleSceneDepth(sUV), _ZBufferParams);
                    // [FIX P2] 근거리 샘플 조기 배제
                    if (sDepth < cutoff) continue;
                    float w = GetWeight(centerDepth, sDepth);
                    float r = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV + chromaOffset, 0.0).r;
                    float g = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV, 0.0).g;
                    float b = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV - chromaOffset, 0.0).b;
                    color += float4(r, g, b, 1.0) * w; totalWeight += w;
                }
                float4 finalBlur = color / max(totalWeight, 0.001);
                float brightness = max(finalBlur.r, max(finalBlur.g, finalBlur.b));
                float4 hazyResult = finalBlur + finalBlur * brightness * _DreamHaze * 2.0;
                return lerp(hazyResult, float4(1, 1, 1, 1), _DreamHaze * brightness * 0.4);
            }

            // --- Main Frag ---
            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                
                // 1. Distortion Toggle
                #if defined(_USE_DISTORTION)
                    float totalDistIntensity = _IdleLensDistortion + (_SpeedLensDistortion * _GlobalSpeedFactor);
                    uv = ApplyLensDistortionDynamic(input.uv, totalDistIntensity, _ZoomAutoFitScale);
                #endif
                
                float2 toCenter = uv - 0.5;
                float distFromCenter = length(toCenter);

                // 2. Mipmap logic
                float currentMipRange = lerp(_IdleMipmapRange, _SprintMipmapRange, _GlobalSpeedFactor);
                float mipMask = smoothstep(currentMipRange + 0.05, currentMipRange, distFromCenter);
                
                float effectiveMipBias = 0.0;
                #if defined(_USE_MIPMAP)
                    effectiveMipBias = max(0.0, abs(_VFXMipBias) * mipMask);
                #endif
                float mipLevel = effectiveMipBias; 
                
                float4 color = 0;

                // 3. Vignette
                float dynamicRadius = 0.5 - (_GlobalSpeedFactor * _VignetteRadiusShrink) + _GlobalMovementPulse;
                float vignetteMask = smoothstep(dynamicRadius, dynamicRadius + 0.4, distFromCenter);
                float vignetteValue = 1.0 - (vignetteMask * _VignetteIntensity * _GlobalSpeedFactor);

                // 4. Sampling
                float motionBlurAmount = 0.0;
                #if defined(_USE_MOTION_BLUR)
                    motionBlurAmount = _PeripheralMotionBlur * _GlobalSpeedFactor * vignetteMask;
                #endif

                if(motionBlurAmount > 0.001)
                {
                    [unroll]
                    for(int i = 0; i < 4; i++)
                    {
                        float2 mOffset = toCenter * (motionBlurAmount * float(i) / 3.0);
                        color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv - mOffset, mipLevel);
                    }
                    color /= 4.0;
                }
                else
                {
                    #if defined(_USE_CHROMA)
                        if(_GlobalSpeedFactor > 0.1 && vignetteMask > 0.01)
                        {
                            float speedChroma = _SpeedChromaIntensity * _GlobalSpeedFactor * vignetteMask;
                            float2 chromaOff = toCenter * speedChroma * _BlitTexture_TexelSize.xy;
                            float r = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv + chromaOff, mipLevel).r;
                            float g = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv, mipLevel).g;
                            float b = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv - chromaOff, mipLevel).b;
                            color = float4(r, g, b, 1.0);
                        }
                        else
                        {
                            color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv, mipLevel);
                        }
                    #else
                        color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv, mipLevel);
                    #endif
                }

                // 5. Filters
                #if defined(_FILTERTYPE_SNN)
                    color = ApplySNN(uv, color);
                #elif defined(_FILTERTYPE_KUWAHARA)
                    color = ApplyKuwahara(uv);
                #elif defined(_FILTERTYPE_MEDIAN)
                    color = ApplyMedian(uv);
                #endif

                // 6. Debug View
                #if defined(_MIP_DEBUG)
                    float distToEdge = abs(distFromCenter - currentMipRange);
                    float lineVis = smoothstep(_MipDebugLineThickness + 0.005, _MipDebugLineThickness, distToEdge);
                    float biasAlpha = saturate(abs(_VFXMipBias) / 2.0); 
                    float3 debugLineColor = float3(0.0, 0.4, 1.0);
                    color.rgb = lerp(color.rgb, debugLineColor, lineVis * biasAlpha * 10.0);
                #endif

                // 7. Distance Blur Calculation
                float depth = SampleSceneDepth(uv);
                float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
                float strength = 0; float mixWeight = 0; float normalizedDist = 0;

                #if defined(_USE_DISTANCE_BLUR)
                    if (linearDepth < _NearDist) {
                        strength = 0; mixWeight = 0;
                    }
                    else if (linearDepth < _MidDist) {
                        normalizedDist = saturate((linearDepth - _NearDist) / (_MidDist - _NearDist));
                        mixWeight = pow(normalizedDist, _BlurExponent);
                        strength = mixWeight * _MidBlurSize;
                    }
                    else {
                        normalizedDist = saturate((linearDepth - _MidDist) / (_FarDist - _MidDist));
                        float farMix = pow(normalizedDist, _BlurExponent);
                        // [FIX MISSING] 기존 mixWeight=1.0 고정 → farMix로 수정.
                        // 이전 코드는 FarDist 구간 진입 즉시 100% 블러로 치환되어
                        // 원거리 블러가 근거리 오브젝트 색상을 100%로 덮어쓰는 문제 발생.
                        mixWeight = farMix;
                        strength = lerp(_MidBlurSize, _FarBlurSize, farMix);
                    }

                    // [FIX B] 방안B: nearProtectMask — 근거리 픽셀 mixWeight 강제 0.
                    // P2(cutoff)가 '배경 커널→캐릭터 샘플 흡수'를 막는다면,
                    // 방안B는 '캐릭터 픽셀 자체'가 실수로 블러 구간에 걸릴 때의 안전망.
                    // NearDist 경계를 0.5m 폭의 smoothstep으로 부드럽게 전환.
                    float nearProtectMask = smoothstep(_NearDist - 0.5, _NearDist + 0.5, linearDepth);
                    mixWeight *= nearProtectMask;
                    strength  *= nearProtectMask;

                    // Center Blur Suppress (Based on Mip Range)
                    float centerSuppress = lerp(_CenterBlurScale, 1.0, 1.0 - mipMask);
                    strength *= centerSuppress;
                    mixWeight *= centerSuppress;

                    strength *= (1.0 + (vignetteMask * _GlobalSpeedFactor * 1.5));

                    // ── Lock-On Focus Blur ────────────────────────────
                    // 락온 중: 플레이어/타겟 보호 반경 내부 → 블러 억제
                    //          보호 반경 외부 → 약한 상시 블러 추가
                    if (_LockOnActive > 0.5)
                    {
                        // 화면 중심(플레이어 기준)에서의 거리 계산
                        // _LockOnPlayerPosWS, _LockOnTargetPosWS는 SCM이 주입
                        // 뎁스 기반 선형 깊이를 보호 반경과 비교
                        float playerProtect = max(_PlayerProtectRadius, 0.1);
                        float targetProtect = max(_TargetProtectRadius, 0.1);

                        // 플레이어 보호: NearDist 이내면 무조건 블러 없음 (SCM이 NearDist=playerProtect 주입)
                        // 타겟 보호: 타겟 거리로부터 targetProtect 이내면 블러 억제
                        // 보호 반경 바깥 ~ MidDist 사이: 약한 focusBlur 추가
                        float protectedDepth = min(playerProtect, targetProtect);
                        if (linearDepth > protectedDepth && linearDepth < _MidDist)
                        {
                            // 보호 반경 외곽부터 MidDist까지 약한 상시 블러
                            float focusFactor = pow(
                                saturate((linearDepth - protectedDepth) / max(_MidDist - protectedDepth, 0.1)),
                                _FocusBlurFalloff);
                            float focusStrength  = focusFactor * _FocusBlurStrength;
                            strength   = max(strength, focusStrength);
                            mixWeight  = max(mixWeight, focusFactor * saturate(_FocusBlurStrength / 5.0));
                        }
                    }
                #endif

                // 8. Final Mix
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

                float4 finalResult = lerp(color, blurredColor, mixWeight);
                finalResult.rgb *= vignetteValue;

                return finalResult;
            }
            ENDHLSL
        }
    }
    CustomEditor "DistanceBlurShaderGUI"
}