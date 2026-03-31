// =============================================================================
// TerrainSampler.cs  |  pB-4 Project — Week 1
// Layer  : L3 Domain (지형)
// Owner  : Person B
//
// 역할:
//   ITerrainSampler 실제 구현체. Physics.Raycast 기반으로
//   지형 표면에서 4가지 물리적 특성을 추출한다.
//
//   SampleSlope: 평균 경사도 (θ_avg = (1/N) × Σ arccos(n_i · up))
//   SampleDensity: 밀도 (주변 복셀 밀도 평균)
//   SampleOcclusion: 폐쇄도 (EI = 1.0 - (Σd_j / k × MaxRange))
//   SamplePathWidth: 통로 폭 (양쪽 벽면까지의 Raycast 거리 합)
//
// 연계:
//   Week 4의 FuzzyTagConverter가 이 원시 수치를 퍼지 태그로 변환.
//   태그가 블랙보드에 게시되면 AI의 fear 보정에 사용.
//
// 아키텍처:
//   이 클래스는 순수 L3 Domain: 물리 Raycast와 수학 연산만 수행.
//   결과 해석(태그 변환)은 IContextAnalyzer가 담당 (SRP 분리).
// =============================================================================
using UnityEngine;
using TDA.PB4.Interfaces.Environment;

namespace TDA.PB4.Terrain
{
    public class TerrainSampler : MonoBehaviour, ITerrainSampler
    {
        [Header("Sampling Configuration")]
        [Tooltip("경사도 측정용 Raycast 횟수")]
        [SerializeField] private int slopeSampleCount = 8;

        [Tooltip("폐쇄도 측정용 Raycast 횟수")]
        [SerializeField] private int occlusionRayCount = 12;

        [Tooltip("최대 Raycast 거리")]
        [SerializeField] private float maxRayRange = 50f;

        [Tooltip("통로 폭 측정 시 최대 탐지 거리")]
        [SerializeField] private float maxPathWidth = 20f;

        [Tooltip("지형 레이어 마스크")]
        [SerializeField] private LayerMask terrainLayer = ~0;

        // ==================================================================
        // SampleSlope: 평균 경사도 (도 단위)
        // θ_avg = (1/N) × Σ θ_i, where θ_i = arccos(n_i · up)
        //
        // 주변 N개 지점에서 아래로 Raycast → 히트 표면의 노말과 Vector3.up의
        // 내적으로 경사각을 구하고 평균낸다. 평탄한 바닥=0°, 수직 벽면=90°.
        // AI: 30° 이상이면 DifficultEscape 태그 후보.
        // ==================================================================
        public float SampleSlope(Vector3 worldPos)
        {
            float totalAngle = 0f;
            int validHits = 0;

            for (int i = 0; i < slopeSampleCount; i++)
            {
                // 주변 원형 분포 + 아래 방향 Raycast
                float angle = (360f / slopeSampleCount) * i * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.5f;
                Vector3 origin = worldPos + offset + Vector3.up * 2f;

                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 10f, terrainLayer))
                {
                    float dot = Vector3.Dot(hit.normal, Vector3.up);
                    float slopeAngle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
                    totalAngle += slopeAngle;
                    validHits++;
                }
            }

            return validHits > 0 ? totalAngle / validHits : 0f;
        }

        // ==================================================================
        // SampleDensity: 밀도 (0.0~1.0)
        // 주변 아래로 Raycast하여 히트 거리가 짧을수록 밀도가 높다고 판정.
        // '빽빽한' 지형(좁은 통로, 돌기 많음) vs '탁 트인' 지형(넓은 방).
        // ==================================================================
        public float SampleDensity(Vector3 worldPos)
        {
            float totalDist = 0f;
            int validHits = 0;

            // 8방향 수평 Raycast
            for (int i = 0; i < 8; i++)
            {
                float angle = (360f / 8) * i * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                if (Physics.Raycast(worldPos + Vector3.up, dir, out RaycastHit hit, maxRayRange, terrainLayer))
                {
                    totalDist += hit.distance;
                    validHits++;
                }
                else
                {
                    totalDist += maxRayRange;
                    validHits++;
                }
            }

            float avgDist = totalDist / Mathf.Max(1, validHits);
            // 거리가 짧을수록 밀도가 높음 (반비례)
            return 1.0f - Mathf.Clamp01(avgDist / maxRayRange);
        }

        // ==================================================================
        // SampleOcclusion: 폐쇄도 — Enclosure Index (0.0~1.0)
        // EI = 1.0 - (Σ d_j) / (k × MaxRange)
        // k개의 방사형 Ray를 쏘아, 벽에 막히는 비율이 높을수록 폐쇄도가 높다.
        // EI=1.0: 완전 밀폐, EI=0.0: 탁 트인 공간.
        // T-ASP가 EI>0.7 구간을 매복 적합지로 판정.
        // ==================================================================
        public float SampleOcclusion(Vector3 worldPos)
        {
            float totalNormDist = 0f;

            for (int i = 0; i < occlusionRayCount; i++)
            {
                // 반구형 분포 (위/옆 포함)
                float phi = (360f / occlusionRayCount) * i * Mathf.Deg2Rad;
                float theta = Mathf.PI * 0.25f * (i % 3); // 0°, 45°, 90° 고도
                Vector3 dir = new Vector3(
                    Mathf.Cos(phi) * Mathf.Cos(theta),
                    Mathf.Sin(theta),
                    Mathf.Sin(phi) * Mathf.Cos(theta)
                );

                if (Physics.Raycast(worldPos + Vector3.up, dir, out RaycastHit hit, maxRayRange, terrainLayer))
                {
                    totalNormDist += hit.distance / maxRayRange;
                }
                else
                {
                    totalNormDist += 1.0f; // 벽에 안 막힘 = 개방
                }
            }

            float avgNormDist = totalNormDist / occlusionRayCount;
            return 1.0f - avgNormDist; // 반전: 가까울수록(막힐수록) 폐쇄도 높음
        }

        // ==================================================================
        // SamplePathWidth: 통로 폭 (미터 단위)
        // 현재 위치에서 좌/우로 Raycast하여 양쪽 벽면까지의 거리 합.
        // 2m 미만이면 NarrowPath 태그 후보.
        // ==================================================================
        public float SamplePathWidth(Vector3 worldPos)
        {
            Vector3 origin = worldPos + Vector3.up;
            float leftDist = maxPathWidth;
            float rightDist = maxPathWidth;

            // 카메라 또는 이동 방향의 수직 방향으로 측정
            // 단순화: transform.right 사용 (호출자가 방향을 설정)
            Vector3 right = Vector3.right; // TODO: 통로 방향에 수직인 벡터로 교체

            if (Physics.Raycast(origin, right, out RaycastHit hitR, maxPathWidth, terrainLayer))
                rightDist = hitR.distance;

            if (Physics.Raycast(origin, -right, out RaycastHit hitL, maxPathWidth, terrainLayer))
                leftDist = hitL.distance;

            return leftDist + rightDist;
        }
    }
}
