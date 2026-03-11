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

// RNM Blend
float3 RNMBlend(float3 n1, float3 n2)
{
    float3 rn1 = n1 + float3(0, 0, 1);
    float3 rn2 = n2 * float3(-1, -1, 1);
    return rn1 * dot(rn1, rn2) / max(rn1.z, 0.00001) - rn2;
}

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
    out float3 outAlbedo, out float3 outNormal, out float4 outMOHR)
{
    float3 safeNormal = length(worldNormal) < 0.001 ? float3(0.01, 1.0, 0.01) : normalize(worldNormal);
    
    float3 blendWeights = abs(safeNormal);
    blendWeights = pow(blendWeights, 4.0);
    float sumWeights = dot(blendWeights, (float3) 1.0);
    blendWeights = sumWeights < 0.0001 ? float3(0, 1, 0) : (blendWeights / sumWeights);

    float3 samplePos = worldPos;

    float minSteps = 8.0;
    float maxSteps = 32.0;
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

    float3 dirtNormal = SampleTriplanarNormalRNM(_DirtNormal, sampler_DirtNormal, samplePos, safeNormal, blendWeights, tiling, normalScale);
    float3 rockNormal = SampleTriplanarNormalRNM(_RockNormal, sampler_RockNormal, samplePos, safeNormal, blendWeights, tiling, normalScale);
    float3 mossNormal = SampleTriplanarNormalRNM(_MossNormal, sampler_MossNormal, samplePos, safeNormal, blendWeights, tiling, normalScale);

    float4 dirtMOHR = SampleTriplanar(_DirtMask, sampler_DirtMask, samplePos, blendWeights, tiling);
    float4 rockMOHR = SampleTriplanar(_RockMask, sampler_RockMask, samplePos, blendWeights, tiling);
    float4 mossMOHR = SampleTriplanar(_MossMask, sampler_MossMask, samplePos, blendWeights, tiling);

    float slope = safeNormal.y;
    float mossWeight = saturate(smoothstep(0.5, 0.9, slope) + (mossMOHR.b * 0.3));
    float dirtWeight = saturate(smoothstep(0.5, 0.9, -slope) + (dirtMOHR.b * 0.3));
    float rockWeight = saturate(1.0 - (dirtWeight + mossWeight));

    float totalWeight = dirtWeight + mossWeight + rockWeight;
    dirtWeight /= totalWeight;
    mossWeight /= totalWeight;
    rockWeight /= totalWeight;

    outAlbedo = (dirtAlbedo.rgb * dirtWeight) + (rockAlbedo.rgb * rockWeight) + (mossAlbedo.rgb * mossWeight);
    outNormal = normalize((dirtNormal * dirtWeight) + (rockNormal * rockWeight) + (mossNormal * mossWeight));
    outMOHR = (dirtMOHR * dirtWeight) + (rockMOHR * rockWeight) + (mossMOHR * mossWeight);
}
#endif