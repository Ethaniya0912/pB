// =============================================================================
// UtilityActionConfigSO.cs  |  pB-4 Week 2 — Day 1 T1.2 (수정)
// 역할: 5종 행동 유틸리티 + 성격 모디파이어를 외부화한 .asset 라이브러리.
//       UtilityMasterFormula.cs 내부의 ActionUtilityConfig, PersonalityModifierRule 타입을 재사용.
//       (타입 중복 방지: 기존 TDA.PB4.AI.* 타입 직접 참조)
// 사용처: UtilityMasterFormula.SetConfig(this) 호출 → 내부 필드 치환.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI;  // ActionUtilityConfig, PersonalityModifierRule 사용

namespace TDA.PB4.Data
{
    [CreateAssetMenu(fileName = "NewActionConfig",
                     menuName = "pB-4/Humanoid/Action Config Library")]
    public class UtilityActionConfigSO : ScriptableObject
    {
        [Tooltip("5종 행동 정의. Day 2 T2.2에서 입력.\n" +
                 "UtilityMasterFormula.cs의 ActionUtilityConfig와 동일한 구조.")]
        public List<ActionUtilityConfig> actions = new List<ActionUtilityConfig>();

        [Tooltip("성격 태그-행동 모디파이어. 10개 이상 권장.\n" +
                 "UtilityMasterFormula.cs의 PersonalityModifierRule과 동일한 구조.")]
        public List<PersonalityModifierRule> modifiers = new List<PersonalityModifierRule>();

        /// <summary>이름으로 행동 검색.</summary>
        public ActionUtilityConfig FindAction(string actionId)
        {
            foreach (var a in actions)
            {
                if (a.actionId == actionId) return a;
            }
            return null;
        }

        /// <summary>특정 태그에 대한 모든 모디파이어 찾기.</summary>
        public IEnumerable<PersonalityModifierRule> FindModifiersForTag(string tagName)
        {
            foreach (var m in modifiers)
            {
                if (m.tagName == tagName) yield return m;
            }
        }

        private void OnValidate()
        {
            foreach (var a in actions)
            {
                if (string.IsNullOrEmpty(a.actionId))
                    Debug.LogWarning($"[{name}] actionId 비어있음");
            }
            foreach (var m in modifiers)
            {
                if (string.IsNullOrEmpty(m.tagName))
                    Debug.LogWarning($"[{name}] modifier tagName 비어있음");
            }
        }

        public string GetSummary() => $"[{name}] {actions.Count}개 행동, {modifiers.Count}개 모디파이어";
    }
}
