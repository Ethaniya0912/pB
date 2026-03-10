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
// 2. Triplanar 수학 및 샘플링 헬퍼 함수
// ==========================================================

// RNM (Reoriented Normal Mapping) 블렌딩 공식
// (l-value const 에러를 막기 위해 전달받은 파라미터를 직접 조작하지 않고 새 변수에 할당합니다)
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

// 루프 내부에서 밉맵 계산 에러(핑크색)를 막기 위한 명시적 LOD 샘플러
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

// 단순 덧셈(UDN)이 아닌 해석적 법선(worldNormal) 기반 RNM 회전 적용
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

// ==========================================================
// 3. 메인 데이터 추출 함수 (POM 및 MOHR 블렌딩)
// ==========================================================
void GetCaveSurfaceData(
    float3 worldPos, float3 worldNormal, float3 viewDirWS,
    float tiling, float heightScale, float normalScale,
    float enablePomFading, float pomFadeStart, float pomFadeEnd,
    out float3 outAlbedo, out float3 outNormal, out float4 outMOHR)
{
    float3 blendWeights = abs(worldNormal);
    blendWeights = pow(blendWeights, 4.0);
    blendWeights /= dot(blendWeights, (float3) 1.0);

    float3 samplePos = worldPos;

    // ==========================================================
    // [최적화 Opt 4: 거리 기반 POM 페이딩]
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
        
        // _EnableSafePom은 메인 셰이더의 CBUFFER에서 전달받아 사용합니다.
        if (_EnableSafePom > 0.5)
        {
            float maxSafeOffset = 0.015; // 모서리를 뚫지 않을 물리적 안전 한계선
            if (length(viewDirOffset) > maxSafeOffset)
                viewDirOffset = normalize(viewDirOffset) * maxSafeOffset;
        }

        float3 deltaPos = viewDirOffset / numSteps;
        float3 currentPos = worldPos;
        
        // 루프 내/외부의 높이맵 샘플링을 모두 LOD 전용 함수로 교체하여 밉맵 에러 원천 차단
        float heightFromTexture = SampleTriplanarLOD(_RockMask, sampler_RockMask, currentPos, blendWeights, tiling, 0.0).b;
        float prevHeight = heightFromTexture;

        [loop]
        for (int i = 0; i < 32; i++)
        {
            if (currentLayerDepth < 1.0 - heightFromTexture)
            {
                currentPos += deltaPos;
                heightFromTexture = SampleTriplanarLOD(_RockMask, sampler_RockMask, currentPos, blendWeights, tiling, 0.0).b;
                
                if (_EnableSafePom > 0.5 && abs(heightFromTexture - prevHeight) > 0.3)
                {
                    currentPos -= deltaPos;
                    heightFromTexture = prevHeight;
                    break;
                }

                prevHeight = heightFromTexture;
                currentLayerDepth += layerDepth;
            }
            else
                break;
        }

        float3 prevPos = currentPos - deltaPos;
        float prevHeightToCompare = SampleTriplanarLOD(_RockMask, sampler_RockMask, prevPos, blendWeights, tiling, 0.0).b;
        float weight = (heightFromTexture - (1.0 - currentLayerDepth)) /
                       (max(0.0001, (heightFromTexture - (1.0 - currentLayerDepth)) - (prevHeightToCompare - (1.0 - currentLayerDepth + layerDepth))));
        
        samplePos = lerp(currentPos, prevPos, weight);
    }
    // ==========================================================

    // 2. 텍스처 샘플링 (루프 밖이므로 기존 샘플러 사용 가능)
    float4 dirtAlbedo = SampleTriplanar(_DirtAlbedo, sampler_DirtAlbedo, samplePos, blendWeights, tiling);
    float4 rockAlbedo = SampleTriplanar(_RockAlbedo, sampler_RockAlbedo, samplePos, blendWeights, tiling);
    float4 mossAlbedo = SampleTriplanar(_MossAlbedo, sampler_MossAlbedo, samplePos, blendWeights, tiling);

    // 해석적 법선(worldNormal)이 결합된 RNM 노멀 텍스처 샘플링
    float3 dirtNormal = SampleTriplanarNormalRNM(_DirtNormal, sampler_DirtNormal, samplePos, worldNormal, blendWeights, tiling, normalScale);
    float3 rockNormal = SampleTriplanarNormalRNM(_RockNormal, sampler_RockNormal, samplePos, worldNormal, blendWeights, tiling, normalScale);
    float3 mossNormal = SampleTriplanarNormalRNM(_MossNormal, sampler_MossNormal, samplePos, worldNormal, blendWeights, tiling, normalScale);

    float4 dirtMOHR = SampleTriplanar(_DirtMask, sampler_DirtMask, samplePos, blendWeights, tiling);
    float4 rockMOHR = SampleTriplanar(_RockMask, sampler_RockMask, samplePos, blendWeights, tiling);
    float4 mossMOHR = SampleTriplanar(_MossMask, sampler_MossMask, samplePos, blendWeights, tiling);

    // 3. 지리적 조건 (Slope & Height) 기반 재질 블렌딩
    float slope = worldNormal.y;
    float mossWeight = saturate(smoothstep(0.5, 0.9, slope) + (mossMOHR.b * 0.3));
    float dirtWeight = saturate(smoothstep(0.5, 0.9, -slope) + (dirtMOHR.b * 0.3));
    float rockWeight = saturate(1.0 - (dirtWeight + mossWeight));

    float totalWeight = dirtWeight + mossWeight + rockWeight;
    dirtWeight /= totalWeight;
    mossWeight /= totalWeight;
    rockWeight /= totalWeight;

    // 4. 최종 출력 데이터 합성
    outAlbedo = (dirtAlbedo.rgb * dirtWeight) + (rockAlbedo.rgb * rockWeight) + (mossAlbedo.rgb * mossWeight);
    outNormal = normalize((dirtNormal * dirtWeight) + (rockNormal * rockWeight) + (mossNormal * mossWeight));
    outMOHR = (dirtMOHR * dirtWeight) + (rockMOHR * rockWeight) + (mossMOHR * mossWeight);
}

#endif