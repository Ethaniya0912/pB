// =============================================================================
// PersonalityTagRuleSO.cs  |  pB-4 Week 2 — Day 1 T1.2 (수정)
// 역할: 15종 성격 태그 발현 규칙을 외부화한 .asset 라이브러리.
//       PersonalityTagResolver.cs 내부의 TagEmergenceRule 타입을 재사용.
//       (타입 중복 방지: 기존 TDA.PB4.AI.TagEmergenceRule 직접 참조)
// 사용처: PersonalityTagResolver.SetRules(this) 호출 → rules 필드 치환.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI;  // TagEmergenceRule 사용

namespace TDA.PB4.Data
{
    [CreateAssetMenu(fileName = "NewTagRuleLibrary",
                     menuName = "pB-4/Humanoid/Tag Rule Library")]
    public class PersonalityTagRuleSO : ScriptableObject
    {
        [Tooltip("15종 태그 규칙. Day 2 T2.1에서 전부 입력.\n" +
                 "PersonalityTagResolver.cs의 TagEmergenceRule과 동일한 구조.")]
        public List<TagEmergenceRule> rules = new List<TagEmergenceRule>();

        /// <summary>유효성 검사. Designer 저장 시 자동 호출.</summary>
        private void OnValidate()
        {
            foreach (var rule in rules)
            {
                if (string.IsNullOrEmpty(rule.tagName))
                    Debug.LogWarning($"[{name}] 태그 이름이 비어있는 규칙 발견");

                if (rule.emergenceProbability < 0f || rule.emergenceProbability > 1f)
                    Debug.LogWarning($"[{name}] {rule.tagName}: emergenceProbability 범위 초과 ({rule.emergenceProbability})");

                if (rule.minControl > rule.maxControl)
                    Debug.LogWarning($"[{name}] {rule.tagName}: minControl > maxControl");
                if (rule.minStability > rule.maxStability)
                    Debug.LogWarning($"[{name}] {rule.tagName}: minStability > maxStability");
            }
        }

        /// <summary>디버그 요약.</summary>
        public string GetSummary() => $"[{name}] {rules.Count}개 규칙";
    }
}
