using UnityEngine;

namespace CaveSystem
{
    // ══════════════════════════════════════════════════════════════════════
    // [Gate 5 Phase A.6] CardinalBiomeBiasSO
    //
    // 목적:
    //   월드 동서남북 + 상하 방위에 따라 biome 분포 bias 적용.
    //   예) 북쪽은 카르스트 우세, 남쪽은 캐니언 우세, 하층은 퇴적암.
    //
    // 기존 (MacroBiomeScale 노이즈만):
    //   완전 등방성 랜덤 분포. 방향성 없음.
    //
    // A.6 개선:
    //   ┌──────────────────────────────────────┐
    //   │ Biome[0] 가중: +0.5 × dot(worldXZ, N방위) │
    //   │ Biome[1] 가중: +0.5 × dot(worldXZ, S방위) │
    //   └──────────────────────────────────────┘
    //   노이즈 기반 blend에 더해져 biome 분포에 방향성 부여.
    //
    // 현재 A.6 구현 수준 (Phase 3):
    //   - SO 신규 + CaveBiomeSettings에 optional slot 추가
    //   - shader 소비는 아직 안 함 (골격만)
    //   - 실제 bias 적용은 Phase 5 B.1~.15 시기에 통합 예정
    //
    // 규칙 #6 byte-identical:
    //   SO 없으면 기존 MacroBiome 노이즈 경로 (현재 동작).
    //   SO 할당 시에만 bias 적용 (향후 shader 구현).
    // ══════════════════════════════════════════════════════════════════════

    [CreateAssetMenu(fileName = "NewCardinalBias", menuName = "Cave System/Cardinal Biome Bias", order = 12)]
    public class CardinalBiomeBiasSO : ScriptableObject
    {
        [Header("Identity")]
        public string biasName = "Default Cardinal Bias";

        // ═══════════════════════════════════════
        // Per-biome directional bias
        //   각 biome이 선호하는 방위 (월드 좌표 기준)
        //   magnitude 0 = 무방향 (등방성)
        //   magnitude 1 = 강한 방위 편향
        // ═══════════════════════════════════════
        [Header("Per-Biome Cardinal Bias")]
        [Tooltip("각 biome 인덱스에 대한 방향 bias. " +
                 "length는 CaveBiomeSettings.globalBiomes와 일치시켜야 함. " +
                 "Vector3 x=동서(+동), y=상하(+위), z=남북(+북). " +
                 "magnitude는 0(등방성)~1(강한 편향). " +
                 "권장값 예: Karst=(0,+0.5,+0.5) 북상층, Canyon=(0,-0.5,0) 하층.")]
        public Vector3[] biasVectors = new Vector3[0];

        [Tooltip("전역 bias 강도. 0=무효, 1=풀 적용.")]
        [Range(0f, 1f)]
        public float biasStrength = 0.3f;

        [Header("Distance Falloff")]
        [Tooltip("bias가 작용하는 최대 거리 (m). 이 이상 떨어지면 bias 영향 약화.")]
        [Range(50f, 2000f)]
        public float maxInfluenceDistance = 500f;

        [Tooltip("거리 falloff curve. X=normalized distance, Y=bias multiplier.")]
        public AnimationCurve distanceFalloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        // ═══════════════════════════════════════
        // Editor Event
        // ═══════════════════════════════════════
        public static event System.Action OnBiasModified;

        private void OnValidate()
        {
            if (Application.isPlaying) OnBiasModified?.Invoke();
        }
    }
}
