// =============================================================================
// PersonalityTagResolver.cs  |  pB-4 Project — Week 2
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   PersonalityMatrix 5축 값의 조합에서 성격 태그를 확률적으로 발현시킨다.
//   예: 낮은 자제력(control<0.3) + 낮은 우호성(agreeable<0.3) = '탐욕적(Greedy)' 태그.
//   15종 태그 중 조건을 만족하는 태그가 확률적으로 부여되어,
//   같은 성격 NPC라도 매번 약간 다른 태그 조합을 가진다.
//
//   발현된 태그는 UtilityMasterFormula의 PersonalityModifierRule과 연동되어
//   유틸리티 점수를 보정한다. Week 7에서는 SpeechAssembler의 243버킷 분류에도 사용.
//
// 사용법:
//   HumanoidAIBrain이 Awake() 시 ResolveTagsFromPersonality()를 호출하여
//   초기 태그를 부여. 이후 성격 피봇팅(PivotPersonality) 발생 시 재평가.
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI.Humanoid;

namespace TDA.PB4.AI
{
    /// <summary>
    /// 단일 성격 태그의 발현 조건을 정의하는 규칙.
    /// Inspector에서 15종을 정의하고, 각 규칙의 조건을 세밀하게 조정한다.
    /// </summary>
    [Serializable]
    public class TagEmergenceRule
    {
        [Tooltip("태그 이름. 예: Brave, Coward, Greedy, Glutton, Reckless, Cautious 등. " +
                 "이 이름이 UtilityMasterFormula의 PersonalityModifierRule.tagName과 일치해야 한다.")]
        public string tagName;

        [Header("━━━ 발현 조건 (5축 범위) ━━━━━━━━━")]
        [Tooltip("자제력(Control) 최소값. 이 값 이상이어야 발현 후보. -1=조건 없음")]
        [Range(-1f, 1f)] public float minControl = -1f;
        [Tooltip("자제력(Control) 최대값. 이 값 이하여야 발현 후보. 2=조건 없음")]
        [Range(-1f, 2f)] public float maxControl = 2f;

        [Tooltip("안정성(Stability) 최소값. -1=조건 없음")]
        [Range(-1f, 1f)] public float minStability = -1f;
        [Tooltip("안정성(Stability) 최대값. 2=조건 없음")]
        [Range(-1f, 2f)] public float maxStability = 2f;

        [Tooltip("개방성(Openness) 최소값. -1=조건 없음")]
        [Range(-1f, 1f)] public float minOpenness = -1f;
        [Range(-1f, 2f)] public float maxOpenness = 2f;

        [Tooltip("우호성(Agreeable) 최소값. -1=조건 없음")]
        [Range(-1f, 1f)] public float minAgreeable = -1f;
        [Range(-1f, 2f)] public float maxAgreeable = 2f;

        [Tooltip("직설성(Directness) 최소값. -1=조건 없음")]
        [Range(-1f, 1f)] public float minDirectness = -1f;
        [Range(-1f, 2f)] public float maxDirectness = 2f;

        [Header("━━━ 발현 확률 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("조건을 만족했을 때 실제로 태그가 부여될 확률. " +
                 "1.0=100% 확정 부여. 0.5=50% 확률로 부여. " +
                 "같은 성격이라도 매번 다른 태그 조합을 만들기 위함.")]
        [Range(0f, 1f)] public float emergenceProbability = 0.7f;

        [Header("━━━ 유틸리티 보정 (빠른 설정) ━━━━━━")]
        [Tooltip("이 태그가 부여되었을 때 UtilityMasterFormula에 적용할 보정. " +
                 "예: Brave → fearMultiplier=0.5 (공포 절반). " +
                 "0이면 보정 없음. 상세 보정은 UtilityMasterFormula의 ModifierRules에서.")]
        public float fearMultiplier = 0f;
        [Tooltip("탐욕 관련 유틸리티 지수 보정. 예: Greedy → greedK=2.5")]
        public float greedKOverride = 0f;

        /// <summary>주어진 성격 5축이 이 태그의 발현 조건을 만족하는지 검사.</summary>
        public bool CheckCondition(PersonalityMatrix p)
        {
            if (minControl > -0.5f && p.control < minControl) return false;
            if (maxControl < 1.5f && p.control > maxControl) return false;
            if (minStability > -0.5f && p.stability < minStability) return false;
            if (maxStability < 1.5f && p.stability > maxStability) return false;
            if (minOpenness > -0.5f && p.openness < minOpenness) return false;
            if (maxOpenness < 1.5f && p.openness > maxOpenness) return false;
            if (minAgreeable > -0.5f && p.agreeable < minAgreeable) return false;
            if (maxAgreeable < 1.5f && p.agreeable > maxAgreeable) return false;
            if (minDirectness > -0.5f && p.directness < minDirectness) return false;
            if (maxDirectness < 1.5f && p.directness > maxDirectness) return false;
            return true;
        }
    }

    /// <summary>
    /// 성격 5축으로부터 태그를 자동 발현시키는 해석기.
    /// HumanoidAIBrain과 같은 GameObject에 부착.
    /// </summary>
    public class PersonalityTagResolver : MonoBehaviour
    {
        [Header("━━━ 태그 발현 규칙 (15종) ━━━━━━━━━━━━")]
        [Tooltip("15종 성격 태그의 발현 조건 목록. " +
                 "각 규칙은 5축 범위 조건 + 발현 확률로 구성된다. " +
                 "비어있으면 Awake()에서 기본 15종이 자동 생성된다.")]
        [SerializeField] private List<TagEmergenceRule> rules = new List<TagEmergenceRule>();

        [Header("━━━ 현재 발현된 태그 (Read Only) ━━━━━━")]
        [Tooltip("마지막 ResolveTagsFromPersonality() 호출 결과. " +
                 "Play 모드에서 자동으로 채워진다. 직접 수정하지 말 것.")]
        [SerializeField] private List<string> activeTags = new List<string>();

        /// <summary>외부에서 읽기 전용으로 현재 활성 태그에 접근.</summary>
        public IReadOnlyList<string> ActiveTags => activeTags;

        [Header("━━━ Debug ━━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("태그 발현 과정을 Console에 출력. 테스트 시에만 켠다.")]
        public bool debugLog = false;

        // ==================================================================
        // 기본 15종 태그 규칙 자동 생성
        // ==================================================================
        private void Awake()
        {
            if (rules.Count == 0) GenerateDefault15Rules();
        }

        private void GenerateDefault15Rules()
        {
            // 각 태그: (이름, 조건 설명, 5축 범위, 확률)
            rules.Add(MakeRule("Brave",       -1,2, 0.6f,2, -1,2, -1,2, -1,2, 0.7f));   // Stability 높음
            rules.Add(MakeRule("Coward",      -1,2, -1,0.3f, -1,2, -1,2, -1,2, 0.7f));  // Stability 낮음
            rules.Add(MakeRule("Greedy",      -1,0.3f, -1,2, -1,2, -1,0.3f, -1,2, 0.8f)); // Control↓ + Agreeable↓
            rules.Add(MakeRule("Glutton",     -1,0.4f, -1,2, -1,2, -1,2, -1,2, 0.6f));  // Control 낮음
            rules.Add(MakeRule("Reckless",    -1,0.3f, -1,0.4f, 0.6f,2, -1,2, -1,2, 0.7f)); // Control↓+Stability↓+Open↑
            rules.Add(MakeRule("Cautious",    0.6f,2, 0.5f,2, -1,0.4f, -1,2, -1,2, 0.7f)); // Control↑+Stability↑+Open↓
            rules.Add(MakeRule("Altruistic",  -1,2, -1,2, -1,2, 0.7f,2, -1,2, 0.6f));   // Agreeable 높음
            rules.Add(MakeRule("Selfish",     -1,2, -1,2, -1,2, -1,0.3f, -1,2, 0.7f));  // Agreeable 낮음
            rules.Add(MakeRule("Blunt",       -1,2, -1,2, -1,2, -1,2, 0.7f,2, 0.8f));   // Directness 높음
            rules.Add(MakeRule("Diplomatic",  -1,2, -1,2, -1,2, 0.5f,2, -1,0.3f, 0.7f)); // Agreeable↑+Direct↓
            rules.Add(MakeRule("Explorer",    -1,2, -1,2, 0.7f,2, -1,2, -1,2, 0.6f));   // Openness 높음
            rules.Add(MakeRule("Paranoid",    0.6f,2, -1,0.3f, -1,0.3f, -1,0.3f, -1,2, 0.5f)); // Ctrl↑+Stab↓+Open↓+Agr↓
            rules.Add(MakeRule("Stoic",       0.7f,2, 0.7f,2, -1,2, -1,2, -1,0.4f, 0.5f)); // Ctrl↑+Stab↑+Direct↓
            rules.Add(MakeRule("Impulsive",   -1,0.2f, -1,2, 0.5f,2, -1,2, 0.5f,2, 0.7f)); // Ctrl↓+Open↑+Direct↑
            rules.Add(MakeRule("Loyal",       -1,2, 0.5f,2, -1,2, 0.6f,2, -1,2, 0.6f));  // Stability↑+Agreeable↑
        }

        private TagEmergenceRule MakeRule(string name,
            float mnC,float mxC, float mnS,float mxS, float mnO,float mxO,
            float mnA,float mxA, float mnD,float mxD, float prob)
        {
            return new TagEmergenceRule {
                tagName=name, minControl=mnC, maxControl=mxC,
                minStability=mnS, maxStability=mxS, minOpenness=mnO, maxOpenness=mxO,
                minAgreeable=mnA, maxAgreeable=mxA, minDirectness=mnD, maxDirectness=mxD,
                emergenceProbability=prob
            };
        }

        // ==================================================================
        // 핵심 API: 성격으로부터 태그 발현
        // ==================================================================

        /// <summary>
        /// 주어진 PersonalityMatrix에서 태그를 확률적으로 발현시킨다.
        /// 기존 activeTags를 전부 교체한다.
        /// </summary>
        public List<string> ResolveTagsFromPersonality(PersonalityMatrix personality)
        {
            activeTags.Clear();

            foreach (var rule in rules)
            {
                if (!rule.CheckCondition(personality)) continue;

                // 확률적 발현
                float roll = UnityEngine.Random.Range(0f, 1f);
                if (roll <= rule.emergenceProbability)
                {
                    activeTags.Add(rule.tagName);
                    if (debugLog)
                        Debug.Log($"[TagResolver] \u2705 '{rule.tagName}' \ubc1c\ud604 (roll={roll:F2} <= prob={rule.emergenceProbability:F2})");
                }
                else if (debugLog)
                {
                    Debug.Log($"[TagResolver] \u274c '{rule.tagName}' \ubbf8\ubc1c\ud604 (roll={roll:F2} > prob={rule.emergenceProbability:F2})");
                }
            }

            if (debugLog)
                Debug.Log($"[TagResolver] \ucd5c\uc885 \ud0dc\uadf8: [{string.Join(", ", activeTags)}] ({activeTags.Count}\uac1c)");

            return activeTags;
        }
    }
}
