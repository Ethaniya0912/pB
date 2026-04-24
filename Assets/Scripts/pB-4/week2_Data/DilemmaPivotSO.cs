// =============================================================================
// DilemmaPivotSO.cs  |  pB-4 Project — Week 2 Day 3 T3.4 신규
// Layer  : L3 Domain (Data)
// Namespace: TDA.PB4.Data
//
// 역할:
//   Trauma의 Crossroads 단계에서 등장하는 Pivot 선택 정의.
//   플레이어/NPC가 선택지 중 하나를 고르면 NPC의 Anchor가 영구 변경됨.
//
// [Day 3 T3.4 설계]
//   - DilemmaChoice와 별개 (DilemmaChoice는 시나리오 아크용)
//   - DilemmaPivotSO는 NPC 내면의 분기점 (Trauma 극복/악화/변절)
//   - 각 Choice는 5축 Anchor 변화 + Alignment 강제 전이 + Karma 증감
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI.Humanoid;

namespace TDA.PB4.Data
{
    /// <summary>Pivot 트리거 단계.</summary>
    public enum PivotTriggerStage
    {
        /// <summary>Crossroads 진입 시.</summary>
        TraumaCrossroads = 0,
        /// <summary>KarmaTier 전이 시.</summary>
        KarmaTierShift = 1,
        /// <summary>수동 호출 (디버그/시나리오).</summary>
        Manual = 2
    }

    [Serializable]
    public class PivotChoice
    {
        [Tooltip("선택지 ID. 예: 'abandon_friend', 'save_friend'.")]
        public string choiceId;

        [Tooltip("UI 표시 텍스트.")]
        [TextArea(2, 4)]
        public string displayText;

        [Header("━━━ Anchor 5축 변화 (영구) ━━━━━━━━━")]

        [Tooltip("Control 델타. -1 ~ +1.")]
        [Range(-1f, 1f)] public float controlDelta;

        [Tooltip("Stability 델타.")]
        [Range(-1f, 1f)] public float stabilityDelta;

        [Tooltip("Openness 델타.")]
        [Range(-1f, 1f)] public float opennessDelta;

        [Tooltip("Agreeable 델타.")]
        [Range(-1f, 1f)] public float agreeableDelta;

        [Tooltip("Directness 델타.")]
        [Range(-1f, 1f)] public float directnessDelta;

        [Header("━━━ Alignment 강제 전이 (선택) ━━━━━")]

        [Tooltip("true이면 이 선택 후 NPCAlignment를 forcedAlignment로 전이.")]
        public bool forceAlignmentTransition = false;

        [Tooltip("강제 전이 대상 Alignment.")]
        public NPCAlignment forcedAlignment = NPCAlignment.Neutral;

        [Header("━━━ Karma 영향 (플레이어) ━━━━━━━━━")]

        [Tooltip("플레이어 Karma에 누적. +: 선한 선택, -: 악한 선택.")]
        [Range(-30f, 30f)] public float karmaDelta = 0f;

        [Tooltip("이 Pivot의 도덕 정렬. -1 (악) ~ +1 (선).")]
        [Range(-1f, 1f)] public float moralAlignment = 0f;

        [Header("━━━ 후속 효과 ━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("결과 태그 (Legacy Tag). 이후 아크에 영향.")]
        public List<string> resultTags = new List<string>();

        [Tooltip("SpeechAssembler 키. 선택 직후 발화.")]
        public string dialogueKey;

        /// <summary>PersonalityMatrix 델타 배열로 반환.</summary>
        public float[] ToPersonalityDelta()
        {
            return new float[] { controlDelta, stabilityDelta, opennessDelta, agreeableDelta, directnessDelta };
        }
    }

    [CreateAssetMenu(fileName = "NewDilemmaPivot",
                     menuName = "pB-4/Humanoid/Dilemma Pivot")]
    public class DilemmaPivotSO : ScriptableObject
    {
        [Tooltip("이 Pivot의 고유 ID.")]
        public string pivotId;

        [Tooltip("시나리오 문구. 예: 동료가 위험에 처했다. 어떻게 하겠는가?")]
        [TextArea(3, 6)] public string scenario;

        [Tooltip("발동 조건 단계.")]
        public PivotTriggerStage triggerStage = PivotTriggerStage.TraumaCrossroads;

        [Tooltip("선택지 목록 (2~4개).")]
        public List<PivotChoice> choices = new List<PivotChoice>();

        /// <summary>주어진 choiceId의 선택 정의 조회.</summary>
        public PivotChoice GetChoice(string choiceId)
        {
            foreach (var c in choices)
                if (c.choiceId == choiceId) return c;
            return null;
        }

        private void OnValidate()
        {
            if (choices.Count < 2 || choices.Count > 4)
                Debug.LogWarning($"[{name}] Pivot choices는 2~4개 권장 (현재 {choices.Count}개)");
        }
    }
}
