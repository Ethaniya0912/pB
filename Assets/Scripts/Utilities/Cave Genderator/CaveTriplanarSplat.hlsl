#ifndef CAVE_TRIPLANAR_SPLAT_INCLUDED
#define CAVE_TRIPLANAR_SPLAT_INCLUDED

// ==========================================================
// 1. 텍스처 및 샘플러 선언 (Albedo, Normal, MOHR Mask)
// ==========================================================
TEXTURE2D(_DirtAlbedo);     SAMPLER(sampler_DirtAlbedo);
TEXTURE2D(_RockAlbedo);     SAMPLER(sampler_RockAlbedo);
TEXTURE2D(_MossAlbedo);     SAMPLER(sampler_MossAlbedo);

TEXTURE2D(_DirtNormal);     SAMPLER(sampler_DirtNormal);
TEXTURE2D(_RockNormal);     SAMPLER(sampler_RockNormal);
TEXTURE2D(_MossNormal);     SAMPLER(sampler_MossNormal);

// Mask (MOHR: R=Metallic, G=Occlusion, B=Height, A=Roughness)
TEXTURE2D(_DirtMask);       SAMPLER(sampler_DirtMask);
TEXTURE2D(_RockMask);       SAMPLER(sampler_RockMask);
TEXTURE2D(_MossMask);       SAMPLER(sampler_MossMask);

// ==========================================================
// 2. Triplanar 샘플링 헬퍼 함수
// ==========================================================
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

// _NormalScale 파라미터를 받아 탄젠트 공간에서 노멀 강도를 조절합니다.
float3 SampleTriplanarNormal(TEXTURE2D_PARAM( tex, samp),
float3 worldPos, float3 blendWeights, float tiling, float normalScale)
{
float2 uvX = worldPos.zy * tiling;
float2 uvY = worldPos.xz * tiling;
float2 uvZ = worldPos.xy * tiling;

    // 월드 공간 축에 맞게 노멀 방향 보정 전 탄젠트 노멀 스케일링 적용
float3 tX = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uvX));
float3 tY = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uvY));
float3 tZ = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uvZ));

    tX.xy *=
normalScale;
    tY.xy *=
normalScale;
    tZ.xy *=
normalScale;

    tX = float3(0, tX.y, tX.x);
    tY = float3(tY.x, 0, tY.y);
    tZ = float3(tZ.x, tZ.y, 0);

    return normalize(tX * blendWeights.x + tY * blendWeights.y + tZ * blendWeights.z);
}

// ==========================================================
// 3. 메인 데이터 추출 함수 (POM 및 MOHR 블렌딩)
// ==========================================================
void GetCaveSurfaceData(
    float3 worldPos, float3 worldNormal, float3 viewDirWS,
    float tiling, float heightScale, float normalScale,
    float enablePomFading, float pomFadeStart, float pomFadeEnd,
    out float3 outAlbedo, out float3 outNormal, out float4 outMOHR)
{
    // 1. Triplanar 블렌딩 가중치 계산 (선명하게 섞기 위해 제곱 연산)
    float3 blendWeights = abs(worldNormal);
    blendWeights = pow(blendWeights, 4.0);
    blendWeights /= dot(blendWeights, (float3) 1.0);

    float3 samplePos = worldPos;

    // ==========================================================
    // [최적화 Opt 4: 거리 기반 POM 페이딩 토글 적용] 
    // ==========================================================
    float minSteps = 8.0;
    float maxSteps = 32.0;
    float NdotV = saturate(dot(worldNormal, viewDirWS));
    float numSteps = lerp(maxSteps, minSteps, NdotV);

    if (enablePomFading > 0.5)
    {
        float distToCam = length(GetCameraPositionWS() - worldPos);
        float fadeFactor = 1.0 - saturate((distToCam - pomFadeStart) / max(0.001, (pomFadeEnd - pomFadeStart)));
        numSteps *= fadeFactor;
    }

    UNITY_BRANCH

    if (numSteps >= 1.0)
    {
        float layerDepth = 1.0 / numSteps;
        float currentLayerDepth = 0.0;
        
        float3 viewDirOffset = -viewDirWS * (heightScale * 0.1) / max(0.1, NdotV);
        float3 deltaPos = viewDirOffset / numSteps;
        
        float3 currentPos = worldPos;
        float heightFromTexture = SampleTriplanar(_RockMask, sampler_RockMask, currentPos, blendWeights, tiling).b;

        [loop]
        for (int i = 0; i < 32; i++)
        {
            if (currentLayerDepth < 1.0 - heightFromTexture)
            {
                currentPos += deltaPos;
                heightFromTexture = SampleTriplanar(_RockMask, sampler_RockMask, currentPos, blendWeights, tiling).b;
                currentLayerDepth += layerDepth;
            }
            else
                break;
        }

        float3 prevPos = currentPos - deltaPos;
        float prevHeight = SampleTriplanar(_RockMask, sampler_RockMask, prevPos, blendWeights, tiling).b;
        float weight = (heightFromTexture - (1.0 - currentLayerDepth)) /
                       (max(0.0001, (heightFromTexture - (1.0 - currentLayerDepth)) - (prevHeight - (1.0 - currentLayerDepth + layerDepth))));
        
        samplePos = lerp(currentPos, prevPos, weight);
    }
    // ==========================================================

    // 2. 텍스처 샘플링 (Normal Scale 적용)
    float4 dirtAlbedo = SampleTriplanar(_DirtAlbedo, sampler_DirtAlbedo, samplePos, blendWeights, tiling);
    float4 rockAlbedo = SampleTriplanar(_RockAlbedo, sampler_RockAlbedo, samplePos, blendWeights, tiling);
    float4 mossAlbedo = SampleTriplanar(_MossAlbedo, sampler_MossAlbedo, samplePos, blendWeights, tiling);

    float3 dirtNormal = SampleTriplanarNormal(_DirtNormal, sampler_DirtNormal, samplePos, blendWeights, tiling, normalScale);
    float3 rockNormal = SampleTriplanarNormal(_RockNormal, sampler_RockNormal, samplePos, blendWeights, tiling, normalScale);
    float3 mossNormal = SampleTriplanarNormal(_MossNormal, sampler_MossNormal, samplePos, blendWeights, tiling, normalScale);

    float4 dirtMOHR = SampleTriplanar(_DirtMask, sampler_DirtMask, samplePos, blendWeights, tiling);
    float4 rockMOHR = SampleTriplanar(_RockMask, sampler_RockMask, samplePos, blendWeights, tiling);
    float4 mossMOHR = SampleTriplanar(_MossMask, sampler_MossMask, samplePos, blendWeights, tiling);

    // 3. 지리적 조건 (Slope & Height) 기반 재질 블렌딩
    float slope = worldNormal.y;
    
    // [🔥 피드백 수정] 바닥과 천장의 텍스처 배정을 뒤바꾸었습니다.
    // 바닥 (Slope가 +1.0 방향) = 이끼(Moss)
    float mossWeight = saturate(smoothstep(0.5, 0.9, slope) + (mossMOHR.b * 0.3));
    
    // 천장 (Slope가 -1.0 방향) = 흙(Dirt)
    float dirtWeight = saturate(smoothstep(0.5, 0.9, -slope) + (dirtMOHR.b * 0.3));
    
    // 벽면 (Slope가 0.0 방향) = 바위(Rock)
    float rockWeight = saturate(1.0 - (dirtWeight + mossWeight));

    // 정규화 (Weights 합이 1.0이 되도록)
    float totalWeight = dirtWeight + mossWeight + rockWeight;
    dirtWeight /= totalWeight;
    mossWeight /= totalWeight;
    rockWeight /= totalWeight;

    // 4. 최종 출력 데이터 합성
    outAlbedo = (dirtAlbedo.rgb * dirtWeight) + (rockAlbedo.rgb * rockWeight) + (mossAlbedo.rgb * mossWeight);
    
    float3 blendedLocalNormal = (dirtNormal * dirtWeight) + (rockNormal * rockWeight) + (mossNormal * mossWeight);
    outNormal = normalize(worldNormal + blendedLocalNormal);

    outMOHR = (dirtMOHR * dirtWeight) + (rockMOHR * rockWeight) + (mossMOHR * mossWeight);
}

#endif