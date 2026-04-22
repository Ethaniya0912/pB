// =============================================================================
// PersonalityTagResolver.cs  |  pB-4 Project — Week 2 (Day 2 T2.3 통합본)
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   PersonalityMatrix 5축 값의 조합에서 성격 태그를 확률적으로 발현시킨다.
//   예: 낮은 자제력(control<0.3) + 낮은 우호성(agreeable<0.3) = '탐욕적' 태그.
//   15종 태그 중 조건을 만족하는 태그가 확률적으로 부여되어,
//   같은 성격 NPC라도 매번 약간 다른 태그 조합을 가진다.
//
//   발현된 태그는 UtilityMasterFormula의 PersonalityModifierRule과 연동되어
//   유틸리티 점수를 보정한다.
//
// 사용법:
//   HumanoidAIBrain이 Awake() 시 ResolveTagsFromPersonality()를 호출하여
//   초기 태그를 부여. 이후 성격 피봇팅(PivotPersonality) 발생 시 재평가.
//
// [통합 이력]
//   - Day 1 T1.4: SetRules/externalRuleLibrary 추가 (당시 partial class + 별도 Patch 파일)
//   - Day 2 T2.3: Awake에서 GenerateDefault15Rules 호출 제거. SO 강제, fallback 없음.
//   - Day 2 T2.3 통합: PersonalityTagResolver_Week2Patch.cs를 이 파일에 흡수. partial 제거.
//     * 이유: T2.3에서 이미 원본을 크게 수정하므로 Patch 분리 명분 소멸.
//     * 결과: 솔루션 탐색기에 파일 1개. partial 키워드 불필요.
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Data;

namespace TDA.PB4.AI
{
    /// <summary>
    /// 단일 성격 태그의 발현 조건을 정의하는 규칙.
    /// Inspector에서 15종을 정의하고, 각 규칙의 조건을 세밀하게 조정한다.
    /// </summary>
    [Serializable]
    public class TagEmergenceRule
    {
        [Tooltip("태그 이름. 예: Brave, Coward, Glutton, Stoic, Reckless, Cautious 등. " +
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
        [Tooltip("탐욕 관련 유틸리티 지수 보정. 예: Glutton → greedK=2.5")]
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
                 "T2.3 적용 후: externalRuleLibrary 또는 Bootstrapper.defaultTagRules가 " +
                 "SetRules로 주입. 빈 상태로 Play하면 에러 출력.")]
        [SerializeField] private List<TagEmergenceRule> rules = new List<TagEmergenceRule>();

        [Header("━━━ 현재 발현된 태그 (Read Only) ━━━━━━")]
        [Tooltip("마지막 ResolveTagsFromPersonality() 호출 결과. " +
                 "Play 모드에서 자동으로 채워진다. 직접 수정하지 말 것.")]
        [SerializeField] private List<string> activeTags = new List<string>();

        /// <summary>외부에서 읽기 전용으로 현재 활성 태그에 접근.</summary>
        public IReadOnlyList<string> ActiveTags => activeTags;

        [Header("━━━ Week 2 T1.4 외부 규칙 (선택) ━━━━")]
        [Tooltip("PersonalityTagRuleSO .asset. 지정되면 Awake 시 자동 로드. " +
                 "비어있으면 Bootstrapper가 SetRules로 주입.")]
        [SerializeField] private PersonalityTagRuleSO externalRuleLibrary;

        [Header("━━━ Debug ━━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("태그 발현 과정을 Console에 출력. 테스트 시에만 켠다.")]
        public bool debugLog = false;

        // ==================================================================
        // 초기화 (T2.3 적용: GenerateDefault15Rules/MakeRule 완전 제거)
        // ==================================================================
        private void Awake()
        {
            // [Week 2 T2.3 개정] 기본 15종 하드코딩 제거.
            //
            // 우선순위:
            //   1순위: externalRuleLibrary (Inspector 할당)
            //   2순위: Bootstrapper가 나중에 SetRules 호출 (Awake 이후)
            //   3순위: 둘 다 없으면 rules 빈 상태 → 에러

            if (externalRuleLibrary != null)
            {
                LoadRulesFromSO(externalRuleLibrary);
            }
            else if (rules.Count == 0)
            {
                StartCoroutine(VerifyRulesLoadedAfterFrame());
            }
        }

        private IEnumerator VerifyRulesLoadedAfterFrame()
        {
            yield return null;
            if (rules.Count == 0)
            {
                Debug.LogError($"[TagResolver] {name}: rules 미로드. " +
                               $"Inspector의 externalRuleLibrary 또는 " +
                               $"HumanoidBootstrapper.defaultTagRules 할당 필요.");
            }
        }

        // ==================================================================
        // 외부 규칙 주입 API (Bootstrapper 및 수동 호출용)
        // ==================================================================

        /// <summary>외부 SO로부터 rules 로드. Bootstrapper가 호출.</summary>
        public void SetRules(PersonalityTagRuleSO library)
        {
            if (library == null)
            {
                Debug.LogError($"[TagResolver] {name}: SetRules에 null 전달");
                return;
            }

            externalRuleLibrary = library;
            LoadRulesFromSO(library);
        }

        /// <summary>SO → 내부 필드 복사. Awake와 SetRules 양쪽에서 호출됨.</summary>
        private void LoadRulesFromSO(PersonalityTagRuleSO library)
        {
            rules.Clear();
            rules.AddRange(library.rules);

            Debug.Log($"[TagResolver] {name}: {library.rules.Count}개 규칙 로드됨 " +
                      $"(from {library.name})");
        }

        /// <summary>현재 규칙 요약 반환. Dashboard 등 디버그 용도.</summary>
        public string GetRulesSummary()
        {
            return $"[TagResolver] rules={rules.Count}" +
                   (externalRuleLibrary != null ? $", SO={externalRuleLibrary.name}" : ", hardcoded");
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
                        Debug.Log($"[TagResolver] {name}: ✅ '{rule.tagName}' 발현 (roll={roll:F2} <= prob={rule.emergenceProbability:F2})");
                }
                else if (debugLog)
                {
                    Debug.Log($"[TagResolver] {name}: ❌ '{rule.tagName}' 미발현 (roll={roll:F2} > prob={rule.emergenceProbability:F2})");
                }
            }

            if (debugLog)
                Debug.Log($"[TagResolver] {name}: 최종 태그 [{string.Join(", ", activeTags)}] ({activeTags.Count}개)");

            return activeTags;
        }
    }
}
