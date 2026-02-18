Shader "Hidden/URP/DistanceBlurFullScreen"
{
    Properties
    {
        [Header(Distance Zones)]
        _NearDist ("Near Distance (Safe End)", Float) = 7.0
        _MidDist ("Mid Distance", Float) = 25.0
        _FarDist ("Far Distance", Float) = 50.0
        
        [Header(Blur Intensity)]
        [KeywordEnum(Gaussian, Disk, Smooth, Pixel, Dream)] _BlurType ("Blur Method", Float) = 3
        _MidBlurSize ("Mid Blur Size", Range(0.0, 20.0)) = 4.0
        _FarBlurSize ("Far Blur Size", Range(0.0, 30.0)) = 12.0
        _BlurExponent ("Distance Curve Exponent", Range(1.0, 5.0)) = 2.0

        [Header(Artifact Correction)]
        [KeywordEnum(None, SNN, Kuwahara, Median)] _FilterType ("Filter Method", Float) = 1
        _DepthThreshold ("Depth Awareness (Edge Fix)", Range(0.1, 10.0)) = 2.0
        _JitterIntensity ("Smooth Mode Jitter", Range(0.0, 1.0)) = 1.0
        
        [Header(Pixel and Mosaic Settings)]
        _PixelScale ("Near Pixel Scale", Range(1.0, 15.0)) = 4.0
        _FarPixelScale ("Far Pixel Scale", Range(1.0, 50.0)) = 15.0
        _PixelCurve ("Pixel Scale Curve", Range(0.1, 5.0)) = 1.0
        _MoireReduction ("Pixel Moire Reduction (AA)", Range(0.0, 2.0)) = 0.5
        [Toggle(_LOCK_GRID)] _LockGrid ("Lock Grid (Prevent Crawling)", Float) = 1
        [Toggle(_MOSAIC_MODE)] _MosaicMode ("Mosaic Mode (Blocky Color)", Float) = 0
        
        [Header(Dreamcore Settings)]
        _DreamHaze ("Dream Haze Intensity", Range(0.0, 1.0)) = 0.5
        _DreamChroma ("Dream Chromatic Offset", Range(0.0, 5.0)) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "DistanceBlurPass"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            
            #pragma multi_compile _BLURTYPE_GAUSSIAN _BLURTYPE_DISK _BLURTYPE_SMOOTH _BLURTYPE_PIXEL _BLURTYPE_DREAM
            #pragma multi_compile _FILTERTYPE_NONE _FILTERTYPE_SNN _FILTERTYPE_KUWAHARA _FILTERTYPE_MEDIAN
            #pragma shader_feature_local _LOCK_GRID
            #pragma shader_feature_local _MOSAIC_MODE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            
            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            float4 _BlitTexture_TexelSize;

            float _NearDist, _MidDist, _FarDist;
            float _MidBlurSize, _FarBlurSize;
            float _BlurExponent;
            float _DepthThreshold;
            float _JitterIntensity;
            float _PixelScale, _FarPixelScale, _PixelCurve;
            float _MoireReduction;
            float _DreamHaze, _DreamChroma;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float InterleavedGradientNoise(float2 pix)
            {
                float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
                return frac(magic.z * frac(dot(pix, magic.xy)));
            }

            float GetWeight(float centerDepth, float sampleDepth)
            {
                return 1.0 / (1.0 + max(0, centerDepth - sampleDepth) * _DepthThreshold);
            }

            // --- Filters (SNN, Kuwahara, Median) ---
            float4 ApplySNN(float2 uv, float4 centerColor)
            {
                float4 sum = centerColor;
                float totalWeight = 1.0;
                float2 texelSize = _BlitTexture_TexelSize.xy;
                float2 offsets[4] = { float2(-1,-1), float2(0,-1), float2(1,-1), float2(-1,0) };
                [unroll]
                for(int i = 0; i < 4; i++) {
                    float2 off = offsets[i] * texelSize;
                    float4 c1 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv + off, 0.0);
                    float4 c2 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, uv - off, 0.0);
                    float d1 = dot(c1 - centerColor, c1 - centerColor);
                    float d2 = dot(c2 - centerColor, c2 - centerColor);
                    sum += (d1 < d2) ? c1 : c2;
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

            // --- Blur Logic ---
            float4 GetGaussian(float2 uv, float strength, float centerDepth)
            {
                float4 color = 0; float totalWeight = 0;
                float2 texelSize = _BlitTexture_TexelSize.xy * strength;
                float gWeights[9] = { 0.0625, 0.125, 0.0625, 0.125, 0.25, 0.125, 0.0625, 0.125, 0.0625 };
                float2 offsets[9] = { float2(-1,-1), float2(0,-1), float2(1,-1), float2(-1,0), float2(0,0), float2(1,0), float2(-1,1), float2(0,1), float2(1,1) };
                [unroll]
                for(int i = 0; i < 9; i++) {
                    float2 sUV = uv + offsets[i] * texelSize;
                    float sDepth = LinearEyeDepth(SampleSceneDepth(sUV), _ZBufferParams);
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
                [unroll]
                for(int i = 0; i < 13; i++) {
                    float2 sUV = uv + offsets[i] * texelSize;
                    float sDepth = LinearEyeDepth(SampleSceneDepth(sUV), _ZBufferParams);
                    float w = GetWeight(centerDepth, sDepth);
                    color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV, 0.0) * w;
                    totalWeight += w;
                }
                return color / max(totalWeight, 0.001);
            }

            float4 GetSmooth(float2 uv, float strength, float2 screenPos, float centerDepth)
            {
                float4 color = 0; float totalWeight = 0;
                float noise = InterleavedGradientNoise(screenPos) * 6.283185;
                float cosN = cos(noise) * strength; float sinN = sin(noise) * strength;
                float2x2 rot = float2x2(cosN, -sinN, sinN, cosN);
                float2 offsets[8] = { float2(1,0), float2(-1,0), float2(0,1), float2(0,-1), float2(0.5,0.5), float2(-0.5,0.5), float2(0.5,-0.5), float2(-0.5,-0.5) };
                [unroll]
                for(int i = 0; i < 8; i++) {
                    float2 sUV = uv + mul(rot, offsets[i]) * _BlitTexture_TexelSize.xy;
                    float sDepth = LinearEyeDepth(SampleSceneDepth(sUV), _ZBufferParams);
                    float w = GetWeight(centerDepth, sDepth);
                    color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV, 0.0) * w;
                    totalWeight += w;
                }
                return color / max(totalWeight, 0.001);
            }

            // --- Enhanced Pixel (Mosaic) Blur Method ---
            float4 GetPixelBlur(float2 uv, float strength, float centerDepth, float2 screenPos, float normalizedDistance)
            {
                // Calculate dynamic pixel scale based on distance
                float currentPixelScale = lerp(_PixelScale, _FarPixelScale, pow(normalizedDistance, _PixelCurve));
                
                float2 coordBase;
                float2 grid;

                #if defined(_LOCK_GRID)
                    coordBase = uv * _ScreenParams.xy;
                    grid = max(1.0, currentPixelScale);
                #else
                    coordBase = uv;
                    grid = max(_BlitTexture_TexelSize.xy, _BlitTexture_TexelSize.xy * currentPixelScale);
                #endif

                // For a perfect lock or smooth UV transition, snap based on the calculated grid
                // Adding a small epsilon to prevent flickering at grid boundaries
                float2 snappedBase = (floor(coordBase / grid + 0.00001) * grid) + (grid * 0.5);
                
                // Block-consistent noise based on snapped position
                float noise = InterleavedGradientNoise(snappedBase); 
                float angle = noise * 6.283185 * _MoireReduction;
                float2x2 rotMat = float2x2(cos(angle), -sin(angle), sin(angle), cos(angle));
                
                float4 color = 0;
                float totalWeight = 0;

                #if defined(_MOSAIC_MODE)
                    // Mosaic Mode: Stable blocky colors
                    [unroll]
                    for(int x = -1; x <= 1; x++)
                    {
                        [unroll]
                        for(int y = -1; y <= 1; y++)
                        {
                            float2 neighborOffset = mul(rotMat, float2(x, y)) * grid * strength;
                            float2 finalUV;
                            #if defined(_LOCK_GRID)
                                finalUV = (snappedBase + neighborOffset) / _ScreenParams.xy;
                            #else
                                finalUV = snappedBase + neighborOffset;
                            #endif
                            
                            float sDepth = LinearEyeDepth(SampleSceneDepth(finalUV), _ZBufferParams);
                            float w = GetWeight(centerDepth, sDepth);
                            
                            color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, finalUV, 0.0) * w;
                            totalWeight += w;
                        }
                    }
                #else
                    // Filtered Pixel Blur (Scattered/Fuzzy)
                    float2 subOffsets[4] = { float2(-0.25,-0.25), float2(0.25,-0.25), float2(-0.25,0.25), float2(0.25,0.25) };
                    [unroll]
                    for(int s = 0; s < 4; s++)
                    {
                        float2 jitterPos = coordBase + subOffsets[s] * grid;
                        float2 qPoint = (floor(jitterPos / grid + 0.00001) * grid) + (grid * 0.5);

                        [unroll]
                        for(int x = -1; x <= 1; x++)
                        {
                            [unroll]
                            for(int y = -1; y <= 1; y++)
                            {
                                float2 neighborOffset = mul(rotMat, float2(x, y)) * grid * strength;
                                float2 finalUV;
                                #if defined(_LOCK_GRID)
                                    finalUV = (qPoint + neighborOffset) / _ScreenParams.xy;
                                #else
                                    finalUV = qPoint + neighborOffset;
                                #endif
                                float sDepth = LinearEyeDepth(SampleSceneDepth(finalUV), _ZBufferParams);
                                float w = GetWeight(centerDepth, sDepth);
                                color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, finalUV, 0.0) * w;
                                totalWeight += w;
                            }
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
                [unroll]
                for(int i = 0; i < 8; i++) {
                    float2 sUV = uv + offsets[i] * texelSize;
                    float sDepth = LinearEyeDepth(SampleSceneDepth(sUV), _ZBufferParams);
                    float w = GetWeight(centerDepth, sDepth);
                    float r = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV + chromaOffset, 0.0).r;
                    float g = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV, 0.0).g;
                    float b = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, sUV - chromaOffset, 0.0).b;
                    float4 sCol = float4(r, g, b, 1.0);
                    color += sCol * w;
                    totalWeight += w;
                }
                float4 finalBlur = color / max(totalWeight, 0.001);
                float brightness = max(finalBlur.r, max(finalBlur.g, finalBlur.b));
                float4 glow = finalBlur * brightness * _DreamHaze * 2.0;
                float4 hazyResult = finalBlur + glow;
                hazyResult = lerp(hazyResult, float4(1, 1, 1, 1), _DreamHaze * brightness * 0.4);
                return hazyResult;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float depth = SampleSceneDepth(input.uv);
                float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
                float2 screenPos = input.positionCS.xy;
                
                float4 baseColor;
                #if defined(_FILTERTYPE_SNN)
                    baseColor = ApplySNN(input.uv, SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, input.uv, 0.0));
                #elif defined(_FILTERTYPE_KUWAHARA)
                    baseColor = ApplyKuwahara(input.uv);
                #elif defined(_FILTERTYPE_MEDIAN)
                    baseColor = ApplyMedian(input.uv);
                #else
                    baseColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_BlitTexture, input.uv, 0.0);
                #endif

                float strength = 0; float mixWeight = 0;
                float normalizedDist = 0;

                if (linearDepth < _NearDist) return baseColor;
                else if (linearDepth < _MidDist) {
                    normalizedDist = saturate((linearDepth - _NearDist) / (_MidDist - _NearDist));
                    mixWeight = pow(normalizedDist, _BlurExponent);
                    strength = mixWeight * _MidBlurSize;
                }
                else {
                    normalizedDist = saturate((linearDepth - _MidDist) / (_FarDist - _MidDist));
                    float farMix = pow(normalizedDist, _BlurExponent);
                    mixWeight = 1.0;
                    strength = lerp(_MidBlurSize, _FarBlurSize, farMix);
                    // Total normalized distance for chunky calculation
                    normalizedDist = saturate((linearDepth - _NearDist) / (_FarDist - _NearDist));
                }

                float4 blurredColor;
                #if defined(_BLURTYPE_GAUSSIAN)
                    blurredColor = GetGaussian(input.uv, strength, linearDepth);
                #elif defined(_BLURTYPE_DISK)
                    blurredColor = GetDisk(input.uv, strength, linearDepth);
                #elif defined(_BLURTYPE_SMOOTH)
                    blurredColor = GetSmooth(input.uv, strength, screenPos, linearDepth);
                #elif defined(_BLURTYPE_PIXEL)
                    blurredColor = GetPixelBlur(input.uv, strength, linearDepth, screenPos, normalizedDist);
                #else // DREAM
                    blurredColor = GetDreamBlur(input.uv, strength, linearDepth);
                #endif

                return lerp(baseColor, blurredColor, mixWeight);
            }
            ENDHLSL
        }
    }
}