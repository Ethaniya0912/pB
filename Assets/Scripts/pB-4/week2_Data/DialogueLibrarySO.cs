// =============================================================================
// DialogueLibrarySO.cs  |  pB-4 Week 2 — Day 1 T1.2 (v2 개정)
// 역할: 태그 조건 기반 대사 풀. SpeechAssembler(Day 4)가 contextKey로 필터링 후 가중 랜덤.
//
// [v2 코드리뷰 개정]
//   - [D2] using System.Linq 추가 — IReadOnlyCollection<string>.Contains() 확장 메서드 사용.
//          이전 버전은 컴파일 에러 발생.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TDA.PB4.Data
{
    [Serializable]
    public class DialogueEntry
    {
        [Tooltip("컨텍스트 키. 예: 'enter_narrow_path', 'alignment_shift_to_friendly'.")]
        public string contextKey;

        [Tooltip("이 태그들이 모두 있어야 선택 후보.")]
        public string[] requiredTags;

        [Tooltip("이 태그가 하나라도 있으면 제외.")]
        public string[] forbiddenTags;

        [Tooltip("이 진영에서만 발화. anyAlignment=true면 무시.")]
        public NPCAlignment alignment = NPCAlignment.Neutral;

        [Tooltip("모든 진영에서 발화 가능.")]
        public bool anyAlignment = true;

        [Tooltip("대사 텍스트. 한국어 UTF-8.")]
        [TextArea(1, 3)] public string text;

        [Tooltip("가중치. 높을수록 선택 확률 증가.")]
        [Range(0f, 1f)] public float weight = 1f;
    }

    [CreateAssetMenu(fileName = "NewDialogueLibrary",
                     menuName = "pB-4/Humanoid/Dialogue Library")]
    public class DialogueLibrarySO : ScriptableObject
    {
        [Tooltip("대사 엔트리 목록. Day 2~4에서 100개+ 입력.")]
        public List<DialogueEntry> entries = new List<DialogueEntry>();

        /// <summary>컨텍스트 + 태그 + 진영 조건을 만족하는 엔트리 필터링.</summary>
        public List<DialogueEntry> FindCandidates(
            string contextKey,
            IReadOnlyCollection<string> activeTags,
            NPCAlignment currentAlignment)
        {
            var result = new List<DialogueEntry>();
            foreach (var e in entries)
            {
                if (e.contextKey != contextKey) continue;
                if (!e.anyAlignment && e.alignment != currentAlignment) continue;

                // required 태그 모두 있는지
                bool allRequired = true;
                if (e.requiredTags != null)
                {
                    foreach (var t in e.requiredTags)
                    {
                        // [D2] System.Linq.Contains 확장 메서드
                        if (!activeTags.Contains(t)) { allRequired = false; break; }
                    }
                }
                if (!allRequired) continue;

                // forbidden 태그 하나라도 있는지
                bool anyForbidden = false;
                if (e.forbiddenTags != null)
                {
                    foreach (var t in e.forbiddenTags)
                    {
                        if (activeTags.Contains(t)) { anyForbidden = true; break; }
                    }
                }
                if (anyForbidden) continue;

                result.Add(e);
            }
            return result;
        }

        /// <summary>후보 중 가중 랜덤 선택.</summary>
        public DialogueEntry PickWeightedRandom(List<DialogueEntry> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;

            float totalWeight = 0f;
            foreach (var c in candidates) totalWeight += Mathf.Max(0f, c.weight);

            if (totalWeight <= 0f) return candidates[0];

            float pick = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;

            foreach (var c in candidates)
            {
                cumulative += Mathf.Max(0f, c.weight);
                if (pick <= cumulative) return c;
            }

            // [D1] 부동소수점 누적 오차로 위 루프가 모두 skip될 수 있어 최종 fallback.
            return candidates[candidates.Count - 1];
        }
    }
}
