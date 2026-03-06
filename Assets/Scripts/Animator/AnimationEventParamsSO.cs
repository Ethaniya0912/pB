using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.Character.Animation
{
    /// <summary>
    /// [P4] 시간과 Enum 이벤트를 묶어주는 순수 데이터 캡슐입니다.
    /// 문자열과 ISerializationCallbackReceiver를 완전히 제거하여 최고 수준의 가벼움과 안전성을 확보했습니다.
    /// </summary>
    [Serializable]
    public struct AnimationEventPoint
    {
        [Tooltip("이벤트가 발생할 애니메이션의 진행 시점 (0.0 = 시작, 1.0 = 끝)")]
        [Range(0f, 1f)]
        public float triggerTime;

        [Tooltip("발생시킬 이벤트의 종류 (마우스 드롭다운으로 선택하여 오타 원천 차단)")]
        public global::AnimationEventType eventType;
    }

    /// <summary>
    /// [P1 & P3] 단일 애니메이션 모션의 시각적 원본과 타임라인 데이터를 캡슐화한 1차 데이터 SO입니다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewAnimationEventParams", menuName = "TDA/Animation/Event Params")]
    public class AnimationEventParamsSO : ScriptableObject
    {
        [Header("Visual Asset")]
        public AnimationClip targetClip;

        [Header("Event Timeline (P1 & P4)")]
        [Tooltip("이 모션이 재생되는 동안 발생할 모든 이벤트를 여기에 추가하세요.")]
        public List<AnimationEventPoint> eventPoints = new List<AnimationEventPoint>();

        [Header("Motion Warping Settings (P3)")]
        [Tooltip("캐릭터가 적을 향해 미끄러져 들어가는(MatchTarget) 예비 동작 허용 구간입니다.")]
        [Range(0f, 1f)] public float warpStartTime = 0.1f;
        [Range(0f, 1f)] public float warpEndTime = 0.4f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (warpStartTime > warpEndTime)
            {
                Debug.LogWarning($"<color=yellow>[Logic Warning]</color> {name}: 워핑 시작 시간({warpStartTime})이 종료 시간({warpEndTime})보다 늦습니다.");
            }
        }
#endif
    }
}