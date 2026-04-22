// =============================================================================
// PersonalityMatrixSO.cs  |  pB-4 Week 2 — Day 1 T1.2
// 역할: NPC 성격 5축(Control/Stability/Openness/Agreeable/Directness)의 템플릿.
//       Designer가 .asset 인스턴스로 6종 프리셋 생성 (Coward/Brave/Glutton/Stoic/Reckless/Loyal).
// 사용처: HumanoidArchetypeSO의 personality 필드, HumanoidAIBrain 초기화 시.
// 확장 계획: Week 6에서 Trauma Scarring 발생 시 런타임으로 값 변경 (앵커 수정).
// =============================================================================
using UnityEngine;
using TDA.PB4.AI.Humanoid;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(fileName = "NewPersonalityMatrix",
                     menuName = "pB-4/Humanoid/Personality Matrix")]
    public class PersonalityMatrixSO : ScriptableObject
    {
        [Header("━━━ 성격 5축 (0~1 범위) ━━━━━━━━━━")]

        [Tooltip("자제력. 높으면 충동 억제, 낮으면 즉각 반응. Greedy/Reckless 태그 발현에 영향.")]
        [Range(0f, 1f)] public float control = 0.5f;

        [Tooltip("안정성. 높으면 트라우마 저항+공포 감소, 낮으면 Coward/Anxious 태그 발현.")]
        [Range(0f, 1f)] public float stability = 0.5f;

        [Tooltip("개방성. 높으면 새로운 경험(Reckless), 낮으면 안전 선호(Cautious).")]
        [Range(0f, 1f)] public float openness = 0.5f;

        [Tooltip("우호성. 높으면 협력적(Loyal/Altruist), 낮으면 이기적(Selfish).")]
        [Range(0f, 1f)] public float agreeable = 0.5f;

        [Tooltip("직설성. 높으면 단도직입(Blunt), 낮으면 완곡.")]
        [Range(0f, 1f)] public float directness = 0.5f;

        [Header("━━━ 메타 정보 ━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("프리셋 이름. 예: 'Coward', 'Brave'. 로그와 디버깅에 사용.")]
        public string presetName = "Default";

        [Tooltip("Designer용 설명. 이 성격의 행동 패턴 특징.")]
        [TextArea(2, 4)] public string description;

        /// <summary>SO 값을 PersonalityMatrix 구조체로 변환.</summary>
        /// <remarks>HumanoidAIBrain.personality 필드에 주입할 때 사용.</remarks>
        public PersonalityMatrix ToMatrix()
        {
            return new PersonalityMatrix
            {
                control = this.control,
                stability = this.stability,
                openness = this.openness,
                agreeable = this.agreeable,
                directness = this.directness,
            };
        }

        /// <summary>디버그용 요약 문자열.</summary>
        public override string ToString()
        {
            return $"[{presetName}] C={control:F2} S={stability:F2} " +
                   $"O={openness:F2} A={agreeable:F2} D={directness:F2}";
        }

        /// <summary>값 검증. Designer 저장 시 자동 호출.</summary>
        private void OnValidate()
        {
            control = Mathf.Clamp01(control);
            stability = Mathf.Clamp01(stability);
            openness = Mathf.Clamp01(openness);
            agreeable = Mathf.Clamp01(agreeable);
            directness = Mathf.Clamp01(directness);
        }
    }
}
