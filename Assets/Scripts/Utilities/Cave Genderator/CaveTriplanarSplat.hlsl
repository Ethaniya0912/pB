#ifndef CAVE_TRIPLANAR_SPLAT_INCLUDED
#define CAVE_TRIPLANAR_SPLAT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_DirtAlbedo);     SAMPLER(sampler_DirtAlbedo);
TEXTURE2D(_RockAlbedo);     SAMPLER(sampler_RockAlbedo);
TEXTURE2D(_MossAlbedo);     SAMPLER(sampler_MossAlbedo);

TEXTURE2D(_DirtNormal);     SAMPLER(sampler_DirtNormal);
TEXTURE2D(_RockNormal);     SAMPLER(sampler_RockNormal);
TEXTURE2D(_MossNormal);     SAMPLER(sampler_MossNormal);

TEXTURE2D(_DirtMask);       SAMPLER(sampler_DirtMask);
TEXTURE2D(_RockMask);       SAMPLER(sampler_RockMask);
TEXTURE2D(_MossMask);       SAMPLER(sampler_MossMask);

TEXTURE2D(_DCNormalMap_X);  SAMPLER(sampler_DCNormalMap_X);
TEXTURE2D(_DCNormalMap_Y);  SAMPLER(sampler_DCNormalMap_Y);
TEXTURE2D(_DCNormalMap_Z);  SAMPLER(sampler_DCNormalMap_Z);

// RNM Blend
float3 RNMBlend(float3 n1, float3 n2)
{
    float3 rn1 = n1 + float3(0, 0, 1);
    float3 rn2 = n2 * float3(-1, -1, 1);
    return rn1 * dot(rn1, rn2) / max(rn1.z, 0.00001) - rn2;
}

// [P0.5-C] Hash2D — CaveNormalBaker v5 Hash2D와 비트 동일 (절대 규칙 #11)
// enableBakerV5=OFF 시 이 함수는 호출되지 않음 (DC UV가 원본 경로 사용)
float2 Hash2D_Shader(float seed)
{
    float2 p = float2(seed, seed * 1.37);
    p = frac(p * float2(443.897, 441.423));
    p += dot(p, p + 19.19);
    return frac(float2(p.x * p.y, p.x + p.y));
}

float _BandHeight;  // [P0.5-C] Y-Offset 대역 높이. enableBakerV5=OFF 시 미사용

float4 SampleTriplanar(TEXTURE2D_PARAM( tex, samp),
float3 worldPos, float3 blendWeights, float tiling)
{
float2 uvX = worldPos.zy * tiling;
float2 uvY = worldPos.xz * tiling;
float2 uvZ = worldPos.xy * tiling;

float4 colX = SAMPLE_TEXTURE2D(tex, samp, uvX);
float4 colY = SAMPLE_TEXTURE2D(tex, samp, uvY);
float4 colZ = SAMPLE_TEXTURE2D(tex, samp, uvZ);

    return colX * blendWeights.x + colY * blendWeights.y + colZ * blendWeights.
z;
}

float4 SampleTriplanarLOD(TEXTURE2D_PARAM( tex, samp),
float3 worldPos, float3 blendWeights, float tiling, float lod)
{
float2 uvX = worldPos.zy * tiling;
float2 uvY = worldPos.xz * tiling;
float2 uvZ = worldPos.xy * tiling;

float4 colX = SAMPLE_TEXTURE2D_LOD(tex, samp, uvX, lod);
float4 colY = SAMPLE_TEXTURE2D_LOD(tex, samp, uvY, lod);
float4 colZ = SAMPLE_TEXTURE2D_LOD(tex, samp, uvZ, lod);

    return colX * blendWeights.x + colY * blendWeights.y + colZ * blendWeights.
z;
}

float3 SampleTriplanarNormalRNM(TEXTURE2D_PARAM( tex, samp),
float3 worldPos, float3 worldNormal, float3 blendWeights, float tiling, float normalScale)
{
float2 uvX = worldPos.zy * tiling;
float2 uvY = worldPos.xz * tiling;
float2 uvZ = worldPos.xy * tiling;

float3 tX = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uvX));
float3 tY = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uvY));
float3 tZ = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uvZ));

    tX.xy *=
normalScale;
    tY.xy *=
normalScale;
    tZ.xy *=
normalScale;
    // [FIX-NSCALE] normalScale 후 단위 벡터 복원 — 미호출 시 범프 강도 ~48% 손실
    tX = normalize(tX);
    tY = normalize(tY);
    tZ = normalize(tZ);

float3 baseNormalX = float3(worldNormal.z, worldNormal.y, worldNormal.x);
float3 blendX = RNMBlend(baseNormalX, tX);
float3 finalX = float3(blendX.z, blendX.y, blendX.x);

float3 baseNormalY = float3(worldNormal.x, worldNormal.z, worldNormal.y);
float3 blendY = RNMBlend(baseNormalY, tY);
float3 finalY = float3(blendY.x, blendY.z, blendY.y);

float3 baseNormalZ = float3(worldNormal.x, worldNormal.y, worldNormal.z);
float3 blendZ = RNMBlend(baseNormalZ, tZ);
float3 finalZ = float3(blendZ.x, blendZ.y, blendZ.z);

    return normalize(finalX * blendWeights.x + finalY * blendWeights.y + finalZ * blendWeights.z);
}

// [🔥 핵심 동기화] 정확히 13개의 파라미터를 받는 규격으로 확정합니다.
// 전역 변수 충돌(undeclared identifier)을 막기 위해 모든 옵션을 매개변수로 받습니다.
void GetCaveSurfaceData(
    float3 worldPos, float3 worldNormal, float3 viewDirWS,
    float tiling, float heightScale, float normalScale,
    float enableSafePom, float enablePomFading, float pomFadeStart, float pomFadeEnd,
    float dirtFadeStart, float dirtFadeEnd,
    float dcNormalStrength,
    float3 perVertexPerturbation, // [Phase 2-5] Per-Vertex 편차 (0,0,0)=미사용
    out float3 outAlbedo, out float3 outNormal, out float4 outMOHR)
{
    float3 safeNormal = length(worldNormal) < 0.001 ? float3(0.01, 1.0, 0.01) : normalize(worldNormal);

    // ── [L1] Per-Vertex 편차 적용 (DC 곡면 보정) ──────────────────────
    // perturbation = subvoxelN - meshN (DCMeshBuilder에서 저장)
    // meshN은 GPU가 부드럽게 보간 → perturbation은 작은 보정만 → 격자 급변 완화
    float3 correctedNormal = safeNormal;
    if (dot(perVertexPerturbation, perVertexPerturbation) > 0.0001)
    {
        correctedNormal = normalize(safeNormal + perVertexPerturbation * dcNormalStrength);
    }

    // ── blendWeights는 L1 보정된 법선 기준 ──────────────────────────
    float3 blendWeights = abs(correctedNormal);
    blendWeights = pow(blendWeights, 4.0);
    float sumWeights = dot(blendWeights, (float3) 1.0);
    blendWeights = sumWeights < 0.0001 ? float3(0, 1, 0) : (blendWeights / sumWeights);

    float3 samplePos = worldPos;

    float minSteps = 16.0;
    float maxSteps = 64.0;
    float NdotV = saturate(dot(safeNormal, viewDirWS));
    float numSteps = lerp(maxSteps, minSteps, NdotV);

    if (enablePomFading > 0.5)
    {
        float distToCam = length(_WorldSpaceCameraPos.xyz - worldPos);
        float fadeFactor = 1.0 - saturate((distToCam - pomFadeStart) / max(0.001, (pomFadeEnd - pomFadeStart)));
        numSteps *= fadeFactor;
    }

    UNITY_BRANCH

    if (numSteps >= 1.0)
    {
        float layerDepth = 1.0 / numSteps;
        float currentLayerDepth = 0.0;
        
        float3 viewDirOffset = -viewDirWS * heightScale / max(0.3, NdotV);
        
        if (enableSafePom > 0.5)
        {
            float maxSafeOffset = heightScale * 1.5;
            if (length(viewDirOffset) > maxSafeOffset)
                viewDirOffset = normalize(viewDirOffset) * maxSafeOffset;
        }

        float3 deltaPos = viewDirOffset / numSteps;
        float3 currentPos = worldPos;
        
        float heightFromTexture = SampleTriplanarLOD(_RockMask, sampler_RockMask, currentPos, blendWeights, tiling, 0.0).b;
        float prevHeight = heightFromTexture;

        [loop]
        for (int i = 0; i < 32; i++)
        {
            if (currentLayerDepth < 1.0 - heightFromTexture)
            {
                currentPos += deltaPos;
                heightFromTexture = SampleTriplanarLOD(_RockMask, sampler_RockMask, currentPos, blendWeights, tiling, 0.0).b;
                prevHeight = heightFromTexture;
                currentLayerDepth += layerDepth;
            }
            else
            {
                break;
            }
        }

        float3 prevPos = currentPos - deltaPos;
        float prevHeightToCompare = SampleTriplanarLOD(_RockMask, sampler_RockMask, prevPos, blendWeights, tiling, 0.0).b;
        float weight = (heightFromTexture - (1.0 - currentLayerDepth)) /
                       (max(0.0001, (heightFromTexture - (1.0 - currentLayerDepth)) - (prevHeightToCompare - (1.0 - currentLayerDepth + layerDepth))));
        
        samplePos = lerp(currentPos, prevPos, weight);
    }

    float4 dirtAlbedo = SampleTriplanar(_DirtAlbedo, sampler_DirtAlbedo, samplePos, blendWeights, tiling);
    float4 rockAlbedo = SampleTriplanar(_RockAlbedo, sampler_RockAlbedo, samplePos, blendWeights, tiling);
    float4 mossAlbedo = SampleTriplanar(_MossAlbedo, sampler_MossAlbedo, samplePos, blendWeights, tiling);

    // ── [L2] 텍스처 법선 — L1 보정된 법선 기준으로 샘플링 ─────────────
    float3 dirtNormal = SampleTriplanarNormalRNM(_DirtNormal, sampler_DirtNormal, samplePos, correctedNormal, blendWeights, tiling, normalScale);
    float3 rockNormal = SampleTriplanarNormalRNM(_RockNormal, sampler_RockNormal, samplePos, correctedNormal, blendWeights, tiling, normalScale);
    float3 mossNormal = SampleTriplanarNormalRNM(_MossNormal, sampler_MossNormal, samplePos, correctedNormal, blendWeights, tiling, normalScale);

    float4 dirtMOHR = SampleTriplanar(_DirtMask, sampler_DirtMask, samplePos, blendWeights, tiling);
    float4 rockMOHR = SampleTriplanar(_RockMask, sampler_RockMask, samplePos, blendWeights, tiling);
    float4 mossMOHR = SampleTriplanar(_MossMask, sampler_MossMask, samplePos, blendWeights, tiling);

    float slope = safeNormal.y;
    float mossWeight = saturate(smoothstep(0.5, 0.9, slope) + (mossMOHR.b * 0.3));
    float dirtWeight = saturate(smoothstep(0.5, 0.9, -slope) + (dirtMOHR.b * 0.3));
    float heightMask = 1.0 - saturate((worldPos.y - dirtFadeStart) / max(0.001, dirtFadeEnd - dirtFadeStart));
    dirtWeight *= heightMask;
    float rockWeight = saturate(1.0 - (dirtWeight + mossWeight));

    float totalWeight = dirtWeight + mossWeight + rockWeight;
    dirtWeight /= totalWeight;
    mossWeight /= totalWeight;
    rockWeight /= totalWeight;

    outAlbedo = (dirtAlbedo.rgb * dirtWeight) + (rockAlbedo.rgb * rockWeight) + (mossAlbedo.rgb * mossWeight);
    outNormal = normalize((dirtNormal * dirtWeight) + (rockNormal * rockWeight) + (mossNormal * mossWeight));

    UNITY_BRANCH
    // [L1 텍스처 폴백] Per-Vertex 편차 미사용 시에만 텍스처 DC 보정 적용
    // Per-Vertex가 활성 → L1은 이미 위에서 correctedNormal로 처리됨 → 이중 적용 방지
    if (dcNormalStrength > 0.001 && dot(perVertexPerturbation, perVertexPerturbation) < 0.0001)
    {
        // [P0.5-C] _BandHeight > 0.1 → v5 Y-Offset UV, 아니면 원본 UV (비트 동일)
        float2 dcUvX, dcUvY, dcUvZ;
        if (_BandHeight > 0.1)
        {
            float yBand = floor(worldPos.y / _BandHeight);
            float2 yOffXZ = Hash2D_Shader(yBand) * 0.4;
            float2 yOffZY = Hash2D_Shader(yBand + 100.0) * 0.4;
            float2 yOffXY = Hash2D_Shader(yBand + 200.0) * 0.4;
            dcUvX = (samplePos.zy + yOffZY) * tiling;
            dcUvY = (samplePos.xz + yOffXZ) * tiling;
            dcUvZ = (samplePos.xy + yOffXY) * tiling;
        }
        else
        {
            // 원본 UV (enableBakerV5=OFF → _BandHeight=0)
            dcUvX = samplePos.zy * tiling;
            dcUvY = samplePos.xz * tiling;
            dcUvZ = samplePos.xy * tiling;
        }
        float3 dcX = UnpackNormal(SAMPLE_TEXTURE2D(_DCNormalMap_X, sampler_DCNormalMap_X, dcUvX));
        float3 dcY = UnpackNormal(SAMPLE_TEXTURE2D(_DCNormalMap_Y, sampler_DCNormalMap_Y, dcUvY));
        float3 dcZ = UnpackNormal(SAMPLE_TEXTURE2D(_DCNormalMap_Z, sampler_DCNormalMap_Z, dcUvZ));
        // [FIX-DCSWIZZLE] RNMBlend 후 투영공간→월드공간 역변환 (SampleTriplanarNormalRNM과 동일)
        // 원본: 역변환 누락 → dcNormalStrength 비례 Y축 늘어짐 아티팩트
        float3 rawX = RNMBlend(float3(outNormal.zy, abs(outNormal.x)), dcX);
        float3 rawY = RNMBlend(float3(outNormal.xz, abs(outNormal.y)), dcY);
        float3 rawZ = RNMBlend(float3(outNormal.xy, abs(outNormal.z)), dcZ);
        float3 blendX_dc = float3(rawX.z, rawX.y, rawX.x);  // X투영→월드
        float3 blendY_dc = float3(rawY.x, rawY.z, rawY.y);  // Y투영→월드
        float3 blendZ_dc = float3(rawZ.x, rawZ.y, rawZ.z);  // Z투영→월드 (항등)
        float3 dcNormal  = normalize(blendX_dc * blendWeights.x + blendY_dc * blendWeights.y + blendZ_dc * blendWeights.z);
        outNormal = normalize(lerp(outNormal, dcNormal, dcNormalStrength));
    }
    outMOHR = (dirtMOHR * dirtWeight) + (rockMOHR * rockWeight) + (mossMOHR * mossWeight);
}
#endif