// =============================================================================
// FuzzyTagConverter.cs  |  pB-4 Project — Week 4
// Layer  : L3 Domain (지형)
// Namespace: TDA.PB4.Terrain
//
// 역할:
//   TerrainSampler의 원시 수치를 퍼지 로직으로 서사적 태그로 변환한다.
//   게임 디자이너가 "PathWidth<2m이고 Density>High면 NarrowPath"
//   같은 직관적 규칙을 작성하고, 퍼지 멤버십 함수가 0~1 소속도를 계산.
//
//   5종 태그 변환 규칙:
//     NarrowPath:       PathWidth<2m AND Density>High
//     DifficultEscape:  Slope>30° AND Roughness>High
//     HighGround:       Height>Avg AND Occlusion<Low
//     SpookyCave:       LightLevel<0.3 AND EchoIntensity>High
//     DeathTrap:        NarrowPath + DifficultEscape (복합)
//
// 연계:
//   - TerrainSampler: SampleSlope/SampleDensity/SampleOcclusion/SamplePathWidth 원시값
//   - GameBlackboard: 변환된 태그를 SetTerrainTags()로 게시
//   - DynamicFogController: 태그 변경 이벤트(OnTerrainTagChanged)를 수신
//   - TraumaSystem: 트라우마 트리거 태그 매칭
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;

namespace TDA.PB4.Terrain
{
    public class FuzzyTagConverter : MonoBehaviour
    {
        [Header("━━━ TerrainSampler 참조 ━━━━━━━━━━━━━")]
        [Tooltip("지형 원시 데이터를 제공하는 TerrainSampler. " +
                 "비어있으면 씬에서 자동 탐색.")]
        [SerializeField] private TerrainSampler terrainSampler;

        [Tooltip("태그를 평가할 월드 위치의 기준. " +
                 "플레이어의 Transform. 비어있으면 Camera.main 사용.")]
        public Transform evaluationTarget;

        [Header("━━━ NarrowPath 규칙 ━━━━━━━━━━━━━━━━")]
        [Tooltip("이 값 이하의 PathWidth이면 '좁음' 소속도가 1.0. " +
                 "2.0m = 한 사람 겨우 지나갈 폭.")]
        [Range(0.5f, 5f)]
        public float narrowPathThreshold = 2.0f;

        [Tooltip("이 값 이상의 Density이면 '밀집' 소속도가 1.0. " +
                 "0.6 = 주변 60% 이상이 벽으로 둘러싸임.")]
        [Range(0.1f, 1f)]
        public float highDensityThreshold = 0.6f;

        [Header("━━━ DifficultEscape 규칙 ━━━━━━━━━━━━━")]
        [Tooltip("이 값 이상의 경사각이면 '험준' 소속도가 1.0. " +
                 "30° = 걸어 올라가기 어려운 경사.")]
        [Range(10f, 60f)]
        public float steepSlopeThreshold = 30f;

        [Header("━━━ HighGround 규칙 ━━━━━━━━━━━━━━━━")]
        [Tooltip("주변 대비 이 높이차 이상이면 '고지대' 소속도가 1.0. " +
                 "3.0m = 플레이어보다 3m 이상 높은 위치.")]
        [Range(1f, 10f)]
        public float highGroundHeightDiff = 3.0f;

        [Tooltip("이 값 이하의 Occlusion이면 '탁 트임' 소속도가 1.0. " +
                 "0.3 = 시야가 넓게 열려있음.")]
        [Range(0f, 0.5f)]
        public float lowOcclusionThreshold = 0.3f;

        [Header("━━━ SpookyCave 규칙 ━━━━━━━━━━━━━━━━")]
        [Tooltip("이 값 이하의 Occlusion 반전(= 어두움 수치)이면 '어두움' 소속도가 1.0. " +
                 "0.7 = Occlusion 0.7 이상이면 어둡다고 판정.")]
        [Range(0.3f, 1f)]
        public float darknessOcclusionThreshold = 0.7f;

        [Header("━━━ 복합 태그 최소 소속도 ━━━━━━━━━━━━")]
        [Tooltip("퍼지 소속도가 이 값 이상이면 태그를 활성화. " +
                 "0.5 = 소속도 50% 이상이면 태그 부여.")]
        [Range(0.1f, 0.9f)]
        public float activationThreshold = 0.5f;

        [Header("━━━ 평가 주기 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("태그 재평가 간격 (초). " +
                 "0.5 = 0.5초마다 지형을 재분석하여 태그 갱신.")]
        [Range(0.1f, 2f)]
        public float evaluationInterval = 0.5f;

        [Header("━━━ 현재 활성 태그 (Read Only) ━━━━━━━")]
        [SerializeField] private List<string> activeTags = new List<string>();

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = false;

        private float evalTimer = 0f;

        private void Awake()
        {
            if (terrainSampler == null)
                terrainSampler = FindFirstObjectByType<TerrainSampler>();
        }

        private void Update()
        {
            evalTimer += Time.deltaTime;
            if (evalTimer >= evaluationInterval)
            {
                evalTimer = 0f;
                EvaluateAndPublish();
            }
        }

        // ==================================================================
        // 퍼지 로직 태그 변환
        // ==================================================================
        private void EvaluateAndPublish()
        {
            Vector3 pos = GetEvalPosition();
            activeTags.Clear();

            if (terrainSampler == null) return;

            float pathWidth = terrainSampler.SamplePathWidth(pos);
            float density = terrainSampler.SampleDensity(pos);
            float slope = terrainSampler.SampleSlope(pos);
            float occlusion = terrainSampler.SampleOcclusion(pos);

            // ─── NarrowPath: PathWidth < narrow AND Density > high ───
            float narrowMembership = FuzzyBelow(pathWidth, narrowPathThreshold, narrowPathThreshold * 2f);
            float denseMembership = FuzzyAbove(density, highDensityThreshold * 0.5f, highDensityThreshold);
            float narrowScore = Mathf.Min(narrowMembership, denseMembership); // AND = Min
            if (narrowScore >= activationThreshold) activeTags.Add("NarrowPath");

            // ─── DifficultEscape: Slope > steep ───
            float steepMembership = FuzzyAbove(slope, steepSlopeThreshold * 0.7f, steepSlopeThreshold);
            if (steepMembership >= activationThreshold) activeTags.Add("DifficultEscape");

            // ─── HighGround: Height > avg AND Occlusion < low ───
            // 높이차는 TerrainSampler에서 직접 제공하지 않으므로 Y 좌표 근사
            float heightScore = FuzzyAbove(pos.y, highGroundHeightDiff, highGroundHeightDiff * 2f);
            float openScore = FuzzyBelow(occlusion, lowOcclusionThreshold, lowOcclusionThreshold * 2f);
            float highGroundScore = Mathf.Min(heightScore, openScore);
            if (highGroundScore >= activationThreshold) activeTags.Add("HighGround");

            // ─── SpookyCave: Occlusion > high (어두움) ───
            float darkMembership = FuzzyAbove(occlusion, darknessOcclusionThreshold * 0.7f, darknessOcclusionThreshold);
            if (darkMembership >= activationThreshold) activeTags.Add("SpookyCave");

            // ─── DeathTrap: NarrowPath + DifficultEscape (복합) ───
            if (activeTags.Contains("NarrowPath") && activeTags.Contains("DifficultEscape"))
                activeTags.Add("DeathTrap");

            // 블랙보드 게시
            var bb = GameBlackboard.Instance;
            if (bb != null) bb.SetTerrainTags(activeTags);

            if (debugLog)
                Debug.Log($"[FuzzyTag] pos={pos:F1} | path={pathWidth:F1}m dens={density:F2} slope={slope:F1}° occ={occlusion:F2} → [{string.Join(",", activeTags)}]");
        }

        // ==================================================================
        // 퍼지 멤버십 함수
        // ==================================================================

        /// <summary>값이 threshold 이하일수록 소속도가 높아지는 함수. Below=작을수록 1.</summary>
        private float FuzzyBelow(float value, float fullMembership, float zeroMembership)
        {
            if (value <= fullMembership) return 1f;
            if (value >= zeroMembership) return 0f;
            return 1f - (value - fullMembership) / (zeroMembership - fullMembership);
        }

        /// <summary>값이 threshold 이상일수록 소속도가 높아지는 함수. Above=클수록 1.</summary>
        private float FuzzyAbove(float value, float zeroMembership, float fullMembership)
        {
            if (value >= fullMembership) return 1f;
            if (value <= zeroMembership) return 0f;
            return (value - zeroMembership) / (fullMembership - zeroMembership);
        }

        private Vector3 GetEvalPosition()
        {
            if (evaluationTarget != null) return evaluationTarget.position;
            if (Camera.main != null) return Camera.main.transform.position;
            return Vector3.zero;
        }

        /// <summary>현재 활성 태그 목록.</summary>
        public IReadOnlyList<string> ActiveTags => activeTags;
    }
}
