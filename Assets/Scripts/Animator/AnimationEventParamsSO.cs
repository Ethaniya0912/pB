using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.Character.Animation
{
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
    /// [P1] 단일 애니메이션 모션의 시각적 원본과 타임라인 데이터를 캡슐화한 1차 데이터 SO
    /// (반드시 파일 이름이 AnimationEventParamsSO.cs 여야 합니다)
    /// </summary>
    [CreateAssetMenu(fileName = "NewAnimationEventParams", menuName = "TDA/Animation/Event Params")]
    public class AnimationEventParamsSO : ScriptableObject
    {
        [Header("Visual Asset")]
        public AnimationClip targetClip;

        [Header("Action State Flags (Data-Driven Control)")]
        public bool applyRootMotion = true;
        public bool isPerformingAction = true;
        public bool canRotate = false;
        public bool canMove = false;

        [Header("Event Timeline (P1 & P4)")]
        public List<AnimationEventPoint> eventPoints = new List<AnimationEventPoint>();

        [Header("Motion Warping Settings (P3)")]
        [Range(0f, 1f)] public float warpStartTime = 0.1f;
        [Range(0f, 1f)] public float warpEndTime = 0.4f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (warpStartTime > warpEndTime)
            {
                Debug.LogWarning($"<color=yellow>[Logic Warning]</color> {name}: 워핑 시작 시간이 종료 시간보다 늦습니다.");
            }
        }
#endif
    }
}