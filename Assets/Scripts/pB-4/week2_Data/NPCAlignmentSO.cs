// =============================================================================
// NPCAlignmentSO.cs  |  pB-4 Week 2 — Day 1 T1.2 (v2 개정)
// 역할: 4 진영 (Hostile/Neutral/Friendly/Companion) 정의 + 전이 임계 + 히스테리시스.
//       Q4 답변(실제 NPC가 4 진영으로 동적 전환)의 데이터 기반.
// 사용처: NPCAlignmentController (Day 3 T3.2)가 매 1초 평가.
//
// [v2 코드리뷰 개정]
//   - [AL1] OnValidate에서 경고에 더해 IsComplete() 메서드 추가. 런타임 검증에 사용.
//   - [AL2] FindMatchingDefinition의 순서 의존성 문서화 + tie-break 규칙 명시.
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    /// <summary>4 진영 enum. 인덱스가 IAlignmentProvider와 일치.</summary>
    public enum NPCAlignment { Hostile = 0, Neutral = 1, Friendly = 2, Companion = 3 }

    [Serializable]
    public class AlignmentDefinition
    {
        [Tooltip("이 정의가 적용되는 진영.")]
        public NPCAlignment alignment;

        [Header("━━━ 진영 진입 조건 ━━━━━━━━━━━━━━")]

        [Tooltip("진입 시 필요한 최소 trust. 예: Friendly=60.")]
        [Range(0f, 100f)] public float trustMin;

        [Tooltip("진입 시 허용되는 최대 trust. 예: Friendly=89.")]
        [Range(0f, 100f)] public float trustMax = 100f;

        [Tooltip("진입 시 필요한 최소 karma.")]
        [Range(-100f, 100f)] public float karmaMin = -100f;

        [Tooltip("진입 시 허용되는 최대 karma.")]
        [Range(-100f, 100f)] public float karmaMax = 100f;

        [Tooltip("이 태그가 모두 있어야 진입. 빈 배열=조건 없음.")]
        public string[] requiredTags;

        [Tooltip("이 태그가 하나라도 있으면 진입 차단.")]
        public string[] forbiddenTags;

        [Header("━━━ 히스테리시스 (진동 방지) ━━━━━━")]

        [Tooltip("이 진영에서 이탈할 때의 trust 임계. trustMin보다 낮게 설정해 진동 방지. 예: Friendly exit=55.")]
        [Range(0f, 100f)] public float trustExitMin;

        [Tooltip("이 진영에 들어온 후 최소 유지 시간 (초). 이 시간 전엔 이탈 차단.")]
        [Range(0f, 60f)] public float minHoldSeconds = 5f;

        [Header("━━━ 시각화 & 발화 ━━━━━━━━━━━━━━━")]

        [Tooltip("디버깅용 색상. Dashboard에서 사용.")]
        public Color debugColor = Color.white;

        [Tooltip("이 진영 기본 상태에서 사용할 DialogueEntry contextKey.")]
        public string defaultDialogueKey;
    }

    [CreateAssetMenu(fileName = "NewAlignmentDefinitions",
                     menuName = "pB-4/Humanoid/Alignment Definitions")]
    public class NPCAlignmentSO : ScriptableObject
    {
        [Tooltip("4 진영 정의. 정확히 4개여야 함 (Hostile/Neutral/Friendly/Companion).")]
        public List<AlignmentDefinition> alignments = new List<AlignmentDefinition>();

        /// <summary>검증: 4 진영이 모두 정의되었는지 + 히스테리시스 정상.</summary>
        private void OnValidate()
        {
            if (alignments.Count > 0 && alignments.Count != 4)
                Debug.LogWarning($"[{name}] 4 진영 정의가 정확히 4개가 아님 (현재 {alignments.Count}개)");

            foreach (var def in alignments)
            {
                if (def.trustMin > def.trustMax)
                    Debug.LogWarning($"[{name}] {def.alignment}: trustMin({def.trustMin}) > trustMax({def.trustMax})");

                if (def.trustExitMin > def.trustMin)
                    Debug.LogWarning($"[{name}] {def.alignment}: trustExitMin({def.trustExitMin})은 trustMin({def.trustMin})보다 작아야 히스테리시스가 작동함");

                if (def.karmaMin > def.karmaMax)
                    Debug.LogWarning($"[{name}] {def.alignment}: karmaMin > karmaMax");
            }
        }

        /// <summary>4 진영 전부 정의되었는지 확인. 런타임 안전 검증용.</summary>
        /// <remarks>
        /// [AL1] NPCAlignmentController가 Awake에서 호출하여 불완전 SO 조기 감지.
        /// </remarks>
        public bool IsComplete(out string reason)
        {
            if (alignments.Count != 4)
            {
                reason = $"alignments.Count={alignments.Count}, 4개 필요";
                return false;
            }

            var seen = new HashSet<NPCAlignment>();
            foreach (var def in alignments)
            {
                if (!seen.Add(def.alignment))
                {
                    reason = $"{def.alignment} 중복 정의";
                    return false;
                }
            }

            if (!seen.Contains(NPCAlignment.Hostile) || !seen.Contains(NPCAlignment.Neutral)
                || !seen.Contains(NPCAlignment.Friendly) || !seen.Contains(NPCAlignment.Companion))
            {
                reason = "4 진영 중 누락 있음";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>주어진 trust+karma로 적합한 진영을 찾음.</summary>
        /// <remarks>
        /// [AL2] List 순서대로 첫 매칭 반환 (linear search).
        /// 복수 진영이 조건을 만족하면 가장 먼저 정의된 것이 선택됨 → 
        /// Inspector에서 Hostile → Neutral → Friendly → Companion 순서로 정렬 권장.
        /// </remarks>
        /// <returns>일치하는 정의. 없으면 null.</returns>
        public AlignmentDefinition FindMatchingDefinition(float trust, float karma)
        {
            foreach (var def in alignments)
            {
                if (trust >= def.trustMin && trust <= def.trustMax
                    && karma >= def.karmaMin && karma <= def.karmaMax)
                {
                    return def;
                }
            }
            return null;
        }

        /// <summary>특정 진영의 정의 조회.</summary>
        public AlignmentDefinition GetDefinition(NPCAlignment alignment)
        {
            foreach (var def in alignments)
            {
                if (def.alignment == alignment) return def;
            }
            return null;
        }
    }
}
