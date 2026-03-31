// =============================================================================
// HNGSIntensityController.cs  |  pB-4 Project — Week 2
// Layer  : L3 Domain (지형)
// Namespace: TDA.PB4.Terrain
//
// 역할:
//   HNGS(Hybrid Narrative-Gameplay Skeleton) 긴장도 컨트롤러.
//   긴장도(T)를 실시간 계산하여 GameBlackboard.globalIntensityScore에 게시하고,
//   ProceduralSplineMesh의 intensity 파라미터에 연결하여 통로를 물리적으로 수축시킨다.
//
//   긴장도 수식:
//     T = (W_r × ResourceDeficit) + (W_e × EnemyProximity) + (W_s × SensoryDeprivation)
//
//   ResourceDeficit  = 1.0 - (현재 자원 / 최대 자원). 자원이 부족할수록 긴장
//   EnemyProximity   = 1.0 - (최근접 적 거리 / 탐지 범위). 적이 가까울수록 긴장
//   SensoryDeprivation = 폐쇄도(EI). 공간이 좁을수록 감각 차단 → 긴장
//
//   Week 0 마일스톤: "HNGS 긴장도 0.8 이상 시 통로 너비 50% 이하 수축 확인"
// =============================================================================
using UnityEngine;
using TDA.PB4.Core;

namespace TDA.PB4.Terrain
{
    public class HNGSIntensityController : MonoBehaviour
    {
        [Header("━━━ 긴장도 수식 가중치 ━━━━━━━━━━━━━━")]
        [Tooltip("자원 부족(ResourceDeficit)의 가중치. " +
                 "높을수록 체력/탄약 부족 시 긴장도가 빠르게 상승한다. " +
                 "기본 0.3 = 자원 부족이 전체 긴장도의 30%를 차지.")]
        [Range(0f, 1f)] public float W_resource = 0.3f;

        [Tooltip("적 근접도(EnemyProximity)의 가중치. " +
                 "높을수록 적이 가까이 있을 때 긴장도가 빠르게 상승한다. " +
                 "기본 0.4 = 적 근접이 전체 긴장도의 40%를 차지.")]
        [Range(0f, 1f)] public float W_enemy = 0.4f;

        [Tooltip("감각 차단(SensoryDeprivation)의 가중치. " +
                 "높을수록 좁은 공간에서 긴장도가 빠르게 상승한다. " +
                 "기본 0.3 = 폐쇄도가 전체 긴장도의 30%를 차지.")]
        [Range(0f, 1f)] public float W_sensory = 0.3f;

        [Header("━━━ 입력 소스 ━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("자원 부족도 수동 오버라이드 (0.0=자원 충분, 1.0=완전 부족). " +
                 "실제 게임에서는 플레이어의 HP/탄약에서 자동 계산되어야 하지만, " +
                 "프로토타입에서는 Inspector 슬라이더로 직접 조절하여 테스트한다.")]
        [Range(0f, 1f)] public float resourceDeficit = 0.3f;

        [Tooltip("적 근접도 수동 오버라이드 (0.0=적 없음, 1.0=바로 앞). " +
                 "실제 게임에서는 주변 적 탐색으로 자동 계산되어야 하지만, " +
                 "프로토타입에서는 슬라이더로 직접 조절.")]
        [Range(0f, 1f)] public float enemyProximity = 0.0f;

        [Tooltip("감각 차단도 수동 오버라이드 (0.0=넓은 공간, 1.0=밀폐). " +
                 "Week 1 TerrainSampler.SampleOcclusion()의 값이 여기에 연결될 예정. " +
                 "프로토타입에서는 슬라이더로 직접 조절.")]
        [Range(0f, 1f)] public float sensoryDeprivation = 0.0f;

        [Header("━━━ 통로 수축 연동 (ProceduralSplineMesh) ━━━")]
        [Tooltip("긴장도가 이 값을 넘으면 통로 수축이 시작된다. " +
                 "0.5 = 긴장도 50% 이상부터 통로가 좁아지기 시작.")]
        [Range(0f, 1f)] public float shrinkStartThreshold = 0.5f;

        [Tooltip("긴장도가 이 값에 도달하면 최대 수축. " +
                 "Week 0 마일스톤: 0.8 이상 시 50% 이하 수축.")]
        [Range(0f, 1f)] public float shrinkMaxThreshold = 0.8f;

        [Tooltip("연결할 ProceduralSplineMesh. 비어있으면 자동 탐색.")]
        public ProceduralSplineMesh targetSplineMesh;

        [Header("━━━ 계산 결과 (Read Only) ━━━━━━━━━━━━")]
        [Tooltip("현재 계산된 긴장도 T (0.0~1.0). Play 모드에서 자동 갱신.")]
        [SerializeField] private float currentIntensity;

        [Tooltip("현재 통로 수축 정도 (0.0=수축 없음, 1.0=최대 수축). " +
                 "ProceduralSplineMesh.GenerateMesh()의 intensity 파라미터로 전달됨.")]
        [SerializeField] private float currentShrinkAmount;

        /// <summary>외부에서 읽기 전용으로 현재 긴장도에 접근.</summary>
        public float CurrentIntensity => currentIntensity;

        private float updateTimer = 0f;
        private const float UPDATE_INTERVAL = 0.5f;

        private void Update()
        {
            updateTimer += Time.deltaTime;
            if (updateTimer >= UPDATE_INTERVAL)
            {
                updateTimer = 0f;
                CalculateIntensity();
                PublishToBlackboard();
                ApplyTunnelShrink();
            }
        }

        // ==================================================================
        // 긴장도 계산
        // T = (W_r × ResourceDeficit) + (W_e × EnemyProximity) + (W_s × SensoryDeprivation)
        // ==================================================================
        private void CalculateIntensity()
        {
            currentIntensity = (W_resource * resourceDeficit)
                             + (W_enemy * enemyProximity)
                             + (W_sensory * sensoryDeprivation);
            currentIntensity = Mathf.Clamp01(currentIntensity);
        }

        // ==================================================================
        // 블랙보드 게시
        // DynamicFogController가 이 값을 구독하여 안개 색상/농도 변경
        // ==================================================================
        private void PublishToBlackboard()
        {
            var bb = GameBlackboard.Instance;
            if (bb != null)
                bb.SetGlobalIntensity(currentIntensity);
        }

        // ==================================================================
        // 통로 수축 적용
        // shrinkStartThreshold~shrinkMaxThreshold 구간에서 0→1로 보간
        // ==================================================================
        private void ApplyTunnelShrink()
        {
            if (currentIntensity <= shrinkStartThreshold)
            {
                currentShrinkAmount = 0f;
            }
            else
            {
                float range = Mathf.Max(0.01f, shrinkMaxThreshold - shrinkStartThreshold);
                currentShrinkAmount = Mathf.Clamp01((currentIntensity - shrinkStartThreshold) / range);
            }

            // ProceduralSplineMesh에 intensity 전달
            // (실제 메쉬 재생성은 SplineMesh 측에서 필요 시 수행)
            if (targetSplineMesh != null)
            {
                // SplineMesh의 intensity를 갱신하여 다음 GenerateMesh 호출 시 반영
                // 현재 ProceduralSplineMesh는 외부에서 GenerateMesh(points, intensity)를 호출해야 함
                // TODO: Week 3에서 자동 메쉬 갱신 트리거 구현
            }
        }
    }
}
