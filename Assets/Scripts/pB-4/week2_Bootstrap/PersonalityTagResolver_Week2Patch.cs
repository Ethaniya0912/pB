// =============================================================================
// PersonalityTagResolver_Week2Patch.cs  |  pB-4 Week 2 — Day 1 T1.4 패치
// 역할: 기존 PersonalityTagResolver.cs에 SetRules 메서드 추가 (Bootstrapper DI용).
//
// ⚠ 동작 조건 (원본 파일 수정 필요):
//   (1) PersonalityTagResolver.cs 95번째 줄:
//        기존: public class PersonalityTagResolver : MonoBehaviour
//        변경: public partial class PersonalityTagResolver : MonoBehaviour
//
//   (2) PersonalityTagResolver.cs 101번째 줄:
//        기존: [SerializeField] private List<TagEmergenceRule> rules = ...
//        변경: [SerializeField] internal List<TagEmergenceRule> rules = ...
//        (partial class에서 같은 필드에 접근하기 위함)
//
//   (3) 본 파일을 Scripts/pB-4/week2_AI/ 폴더에 추가
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Data;

namespace TDA.PB4.AI
{
    public partial class PersonalityTagResolver
    {
        [Header("━━━ Week 2 T1.4 외부 규칙 (선택) ━━━━")]

        [Tooltip("PersonalityTagRuleSO .asset. 지정되면 SetRules로 Awake 시 하드코딩 덮어쓰기.")]
        [SerializeField] private PersonalityTagRuleSO externalRuleLibrary;

        /// <summary>외부 SO로부터 rules 로드. Bootstrapper가 호출.</summary>
        /// <remarks>
        /// 주의: 원본 rules 필드 internal 접근 필요.
        /// </remarks>
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

        private void LoadRulesFromSO(PersonalityTagRuleSO library)
        {
            rules.Clear();
            rules.AddRange(library.rules);

            Debug.Log($"[TagResolver] {name}: {library.rules.Count}개 규칙 로드됨 " +
                      $"(from {library.name})");
        }

        /// <summary>현재 규칙 요약 반환.</summary>
        public string GetRulesSummary()
        {
            return $"[TagResolver] rules={rules.Count}" +
                   (externalRuleLibrary != null ? $", SO={externalRuleLibrary.name}" : ", hardcoded");
        }
    }
}
