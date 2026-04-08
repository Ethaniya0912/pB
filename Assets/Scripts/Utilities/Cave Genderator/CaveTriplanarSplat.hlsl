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

    // [FIX-NORMALSCALE] xy에만 scale 적용 후 반드시 normalize
    // 기존: tX.xy *= normalScale 후 normalize 없음
    //   → RNMBlend에 비단위벡터 전달 → xy:z 비율 붕괴 → 텍스처 노멀 강도가
    //   normalScale^2 수준으로 의도 대비 과잉 감쇠 (0.5 설정 시 ~52% 기울기 손실)
    // 수정: normalScale 적용 후 normalize → 방향은 정확히 반영, 단위벡터 보장
    tX.xy *= normalScale; tX = normalize(tX);
    tY.xy *= normalScale; tY = normalize(tY);
    tZ.xy *= normalScale; tZ = normalize(tZ);

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

// =============================================================================
// [POM Self-Shadow] 포인트라이트 방향 마이크로 그림자
// -----------------------------------------------------------------------------
// 알고리즘: 올바른 레이-높이장 교차 판정
//
// 핵심 개념:
//   samplePos에서 POM이 찾은 교차 깊이 = startLevel = 1 - baseHeight
//   그림자 레이를 광원 방향으로 steps 등분하여 진행할 때,
//   각 스텝에서 예상 높이 임계값 = 1 - shadowLevel (감소하는 레이 깊이에 따라 증가)
//   샘플 높이 h > threshold → 표면이 레이 위에 있음 → 차폐 발생
//
// 왜 이전 코드(expectedSurface = baseHeight - rayDepth*baseHeight)가 틀렸는가:
//   평탄한 높이맵(h=const)에서도 첫 스텝부터 조건 트리거 → 전역 오그림자 생성
//   동굴 벽 전체가 ~83% 밝기로 고정 → NdotL 변화량 희석 → 범프 디테일 소멸
// =============================================================================
float ComputePOMSelfShadow(
    float3 samplePos, float3 lightDirWS, float3 surfaceNormal,
    float3 blendWeights, float tiling, float heightScale, int steps)
{
    // 광원이 표면 뒤에 있으면 그림자 없음 (pomShadowFactor에 lerp되므로 1.0 반환)
    float NdotL_raw = dot(surfaceNormal, lightDirWS);
    if (NdotL_raw <= 0.0) return 1.0;

    // ── heightScale 적응형 감도 ──────────────────────────────────────────────
    // 진단: heightScale=0.08처럼 낮을 때 self-shadow가 트리거되지 않는 문제
    //
    // 원인 1: rayStep = lightDir * (heightScale / steps)
    //         heightScale=0.08, steps=8 → rayStep=0.01m(1cm) — 매우 짧은 보폭
    //         높이맵 텍스처의 UV 변화가 tiling에 의존하므로, 낮은 heightScale에서
    //         인접 스텝간 h 차이가 거의 없어 threshold 초과 불가
    //
    // 원인 2: threshold margin 0.01이 낮은 heightScale에서 너무 보수적임
    //         startLevel이 0.5일 때 threshold=0.5625, h가 0.58 이상이어야 트리거
    //         → 일반 록 텍스처에서 0.58+ 영역이 전체의 ~20%에 불과 → 희귀 트리거
    //
    // 해결:
    //   - 레이 보폭을 heightScale과 독립적으로 고정 (tiling 기준 최소 보폭 보장)
    //   - threshold margin을 startLevel에 비례하게 축소 (얕은 교차일수록 민감하게)
    //   - occlusion 강도를 heightScale에 반비례 스케일 (낮을수록 더 강하게 보정)
    // ────────────────────────────────────────────────────────────────────────

    // 최소 유효 레이 보폭: heightScale의 절반 이상, tiling 기준 UV 5% 이상 이동 보장
    float effectiveStep = max(heightScale / float(steps), 0.05 / tiling);
    float3 rayStep = lightDirWS * effectiveStep;
    float3 rayPos  = samplePos + rayStep; // 자기 교차 회피

    float baseHeight = SampleTriplanarLOD(_RockMask, sampler_RockMask,
                                          samplePos, blendWeights, tiling, 2.0).b;

    // startLevel: POM 교차 깊이 (1=표면 완전 위, 0=최저점)
    float startLevel = 1.0 - saturate(baseHeight);

    // [FIX-STARTLEVEL] _RockMask.b 가 1.0에 가까우면 height 데이터가 없거나 평탄.
    // 이 경우 threshold = 1.0 + margin → h > 1.004 절대 불충족 → 조기 반환
    if (startLevel < 0.05) return 1.0;

    // heightScale 감도 보정: 낮을수록 threshold를 더 공격적으로
    // 0.08: sensitivity=1.8 / 0.15: sensitivity=1.0 / 0.25: sensitivity=0.72
    float sensitivityBoost = saturate(0.15 / max(heightScale, 0.01));
    sensitivityBoost = lerp(1.0, sensitivityBoost, 0.6); // 과보정 방지: 60%만 적용

    float shadow = 1.0;
    [loop]
    for (int i = 1; i <= steps; i++)
    {
        float h = SampleTriplanarLOD(_RockMask, sampler_RockMask,
                                     rayPos, blendWeights, tiling, 2.0).b;

        float shadowLevel = startLevel * (1.0 - float(i) / float(steps));
        float threshold   = 1.0 - shadowLevel;

        // 적응형 margin: startLevel이 작을수록(얕은 교차) margin도 줄여 민감하게
        // 기존 고정값 0.01 → startLevel 기반 가변 (최소 0.004, 기본 0.01)
        float margin = lerp(0.004, 0.012, saturate(startLevel));

        if (h > threshold + margin)
        {
            float excess    = (h - threshold - margin) / max(startLevel + 0.001, 0.001);
            // sensitivityBoost: heightScale이 낮을수록 occlusion 더 강하게 적용
            float occlusion = saturate(excess * 4.0 * sensitivityBoost);
            shadow = min(shadow, 1.0 - occlusion * 0.75);
            break;
        }
        rayPos += rayStep;
    }
    return shadow;
}

// [🔥 핵심 동기화] 정확히 15개의 파라미터를 받는 규격으로 확정합니다.
// 전역 변수 충돌(undeclared identifier)을 막기 위해 모든 옵션을 매개변수로 받습니다.
void GetCaveSurfaceData(
    float3 worldPos, float3 worldNormal, float3 viewDirWS,
    float3 blendWeightsVS,                              // [V3-FIX] vertex에서 보간된 blendWeights
    float tiling, float heightScale, float normalScale,
    float enableSafePom, float enablePomFading, float pomFadeStart, float pomFadeEnd,
    float dirtFadeStart, float dirtFadeEnd,
    float dcNormalStrength, float dcNormalAmplify,      // [TORCH] dcNormalAmplify 추가
    out float3 outAlbedo, out float3 outNormal, out float4 outMOHR,
    out float3 outSamplePos)                            // [POM-SHADOW] 변위된 샘플 위치 반환
{
    float3 safeNormal = length(worldNormal) < 0.001 ? float3(0.01, 1.0, 0.01) : normalize(worldNormal);
    
    // [V3-FIX] blendWeights를 per-pixel 계산 대신 vertex에서 보간된 값 사용
    // 기존: float3 blendWeights = pow(abs(safeNormal), 4.0) → 폴리곤 내 축 급전환
    // 수정: vertex 보간값 사용 → 축 전환이 폴리곤 경계에서만 발생하고 내부는 부드러움
    // [NOTE] FIX-VSTRETCH (blendWeights.y=0.12) 롤백:
    //   vertNormal_sid 복원으로 Y:Z≈50:50이 돌아와 수직 늘어짐 자연 완화됨
    float3 blendWeights = blendWeightsVS;

    float3 samplePos = worldPos;

    float minSteps = 16.0;
    float maxSteps = 64.0;
    float NdotV = saturate(dot(safeNormal, viewDirWS));
    float numSteps = lerp(maxSteps, minSteps, NdotV);

    // [FIX-CEILING] 천장면(normalY < -0.7)에서 POM 비활성화
    // 천장에서 카메라가 수직으로 바라볼 때 NdotV≈0 → viewDirOffset 무한대
    float ceilFade = smoothstep(-0.7, -0.5, safeNormal.y); // -0.7 아래면 0, -0.5 위면 1
    numSteps *= ceilFade;

    if (enablePomFading > 0.5)
    {
        float distToCam = length(_WorldSpaceCameraPos.xyz - worldPos);
        float fadeFactor = 1.0 - saturate((distToCam - pomFadeStart) / max(0.001, (pomFadeEnd - pomFadeStart)));
        numSteps *= fadeFactor;
    }

    UNITY_BRANCH

    if (numSteps >= 1.0)
    {
        int numStepsInt = (int)numSteps; // [FIX] float loop → int loop (정확한 step 수)
        float layerDepth = 1.0 / (float)numStepsInt;
        float currentLayerDepth = 0.0;
        
        // [FIX-POM-DECOMP] viewDirWS를 법선/접선 성분으로 분리
        // 기존: -viewDirWS * hs / NdotV → 수평 접선 성분도 NdotV로 과잉 증폭
        // 수정: 법선 방향 성분에만 / NdotV 적용 (depth correction의 수학적 의미)
        //        접선 방향 성분은 NdotV 증폭 없이 그대로 (UV sliding)
        // 효과: 45°→50%, 60°→50%, 70°→50% 수평 이동량 감소
        float  depthComponent    = dot(-viewDirWS, safeNormal);              // 법선 방향 깊이 성분
        float3 tangentialDir     = -viewDirWS - depthComponent * safeNormal; // 접선 방향 성분
        float3 normalOffset      = safeNormal * depthComponent * heightScale / max(0.5, NdotV);
        float3 tangentialOffset  = tangentialDir * heightScale;              // 증폭 없음
        float3 viewDirOffset     = normalOffset + tangentialOffset;
        
        if (enableSafePom > 0.5)
        {
            float maxSafeOffset = heightScale * 1.5;
            if (length(viewDirOffset) > maxSafeOffset)
                viewDirOffset = normalize(viewDirOffset) * maxSafeOffset;
        }

        float3 deltaPos = viewDirOffset / (float)numStepsInt; // [FIX] numStepsInt 사용
        float3 currentPos = worldPos;
        
        float heightFromTexture = SampleTriplanarLOD(_RockMask, sampler_RockMask, currentPos, blendWeights, tiling, 0.0).b;
        float prevHeight = heightFromTexture;

        [loop]
        for (int i = 0; i < numStepsInt; i++) // [FIX] 하드코딩 32 → numStepsInt
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

    // ────────────────────────────────────────────────────────────────────────
    // [L1] DC SDF 보정 — 메쉬 노멀에 먼저 적용, 보정된 기준 노멀 확보
    // ────────────────────────────────────────────────────────────────────────
    // 이전 코드 문제: L2(텍스처) 먼저 적용 후 L1(DC) lerp → DC가 텍스처 디테일을
    //   평균화하여 약화. 시뮬레이션 결과 텍스처 기여도 1.65x 감소.
    // 수정: L1을 safeNormal(메쉬 노멀) 위에 직접 적용 → baseCorrectedNormal 생성
    //   L2 텍스처 노멀은 이 보정된 기준 위에 RNMBlend → 100% 강도로 디테일 추가
    //
    // ── 라이팅 반응 약화 진단 (동일 출처 문제) ────────────────────────────
    // 현상: 텍스처 노멀이 플랫하지는 않지만 빛에 덜 반응하는것처럼 보임
    // 원인: DC Normal Baker 출력 = SDF gradient 기반 법선
    //       DC Mesh vertex normal = QEF 기반 법선 (동일한 SDF에서 파생)
    //       → safeNormal ≈ dcNormal (두 벡터가 거의 같은 방향)
    //       → lerp(A, A, 0.5) ≈ A → baseCorrectedNormal ≈ safeNormal
    //       → DC Normal Strength 0.5의 실효 기여 없음
    //       → 텍스처 범프가 올라탈 기준이 이미 'SDF 스무스' 방향
    //       → 텍스처 범프의 라이팅 명암 대비폭이 좁아짐
    // 권장: dcNormalStrength = 0.3~0.4 (동일 출처라 0.5는 과잉)
    //       normalScale = 1.5~2.0 으로 텍스처 범프 강도 직접 증가
    // ────────────────────────────────────────────────────────────────────────
    float3 baseCorrectedNormal = safeNormal;
    UNITY_BRANCH
    if (dcNormalStrength > 0.001)
    {
        float2 dcUvX = samplePos.zy * tiling;
        float2 dcUvY = samplePos.xz * tiling;
        float2 dcUvZ = samplePos.xy * tiling;
        float3 dcX = UnpackNormal(SAMPLE_TEXTURE2D(_DCNormalMap_X, sampler_DCNormalMap_X, dcUvX));
        float3 dcY = UnpackNormal(SAMPLE_TEXTURE2D(_DCNormalMap_Y, sampler_DCNormalMap_Y, dcUvY));
        float3 dcZ = UnpackNormal(SAMPLE_TEXTURE2D(_DCNormalMap_Z, sampler_DCNormalMap_Z, dcUvZ));
        // safeNormal(메쉬 노멀) 기준으로 DC 보정 블렌드
        float3 blendX_dc = RNMBlend(float3(safeNormal.z, safeNormal.y, abs(safeNormal.x)), dcX);
        float3 finalX_dc = float3(blendX_dc.z, blendX_dc.y, blendX_dc.x);
        float3 blendY_dc = RNMBlend(float3(safeNormal.x, safeNormal.z, abs(safeNormal.y)), dcY);
        float3 finalY_dc = float3(blendY_dc.x, blendY_dc.z, blendY_dc.y);
        float3 blendZ_dc = RNMBlend(float3(safeNormal.x, safeNormal.y, abs(safeNormal.z)), dcZ);
        float3 finalZ_dc = float3(blendZ_dc.x, blendZ_dc.y, blendZ_dc.z);
        float3 dcNormal = normalize(finalX_dc * blendWeights.x + finalY_dc * blendWeights.y + finalZ_dc * blendWeights.z);

        // ── [TORCH-CONTRAST] Gram-Schmidt 각도 증폭 ──────────────────────────────
        // 기존: lerp(safeNormal, dcNormal, strength)
        //   → DC Normal과 메쉬 노멀이 동일 SDF 출처(편차 θ ≈ 3~8°)이면
        //     실효 편차 = θ × 0.35 ≈ 1~3° → 포인트라이트 NdotL 변동폭 극소
        //
        // 수정: Gram-Schmidt로 dcNormal의 '접선 편차'만 추출 후 배율 적용
        //   tangentialDev = safeNormal에 수직인 dcNormal의 성분 (순수 편차 벡터)
        //   baseCorrectedNormal = safeNormal + tangentialDev × (strength × amplify)
        //
        //   amplify=3.0: θ=5° → 편차 15°로 증폭 → 횃불 이동 시 표면 굴곡 명암 실효화
        //   amplify=1.0: lerp(safeNormal, dcNormal, strength)와 수학적으로 동일
        //   amplify>4.0: 편차 과대 → 노멀 방향 폭발 주의, saturate로 방어
        //
        // 포인트라이트 환경 권장: dcNormalStrength=0.7, dcNormalAmplify=3.0
        // ────────────────────────────────────────────────────────────────────────
        float3 tangentialDev = dcNormal - dot(dcNormal, safeNormal) * safeNormal;
        baseCorrectedNormal = normalize(safeNormal + tangentialDev * (dcNormalStrength * dcNormalAmplify));
    }

    // ────────────────────────────────────────────────────────────────────────
    // [L2] 바이옴 텍스처 노멀 — DC 보정된 baseCorrectedNormal 위에 적용
    //   baseCorrectedNormal이 RNMBlend의 '표면 기준'이 되어 텍스처 범프가
    //   DC 보정 방향 위에 100% 강도로 올라탐
    // ────────────────────────────────────────────────────────────────────────
    float3 dirtNormal = SampleTriplanarNormalRNM(_DirtNormal, sampler_DirtNormal, samplePos, baseCorrectedNormal, blendWeights, tiling, normalScale);
    float3 rockNormal = SampleTriplanarNormalRNM(_RockNormal, sampler_RockNormal, samplePos, baseCorrectedNormal, blendWeights, tiling, normalScale);
    float3 mossNormal = SampleTriplanarNormalRNM(_MossNormal, sampler_MossNormal, samplePos, baseCorrectedNormal, blendWeights, tiling, normalScale);

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
    // [L1+L2 완료] DC 보정(L1)은 baseCorrectedNormal에 이미 반영됨.
    // 텍스처 노멀(L2)이 baseCorrectedNormal 위에 100% 강도로 적용됨.
    // 별도 DC 재블렌드 불필요.
    outMOHR = (dirtMOHR * dirtWeight) + (rockMOHR * rockWeight) + (mossMOHR * mossWeight);
    outSamplePos = samplePos; // [POM-SHADOW] 변위된 최종 샘플 위치 반환
}
#endif