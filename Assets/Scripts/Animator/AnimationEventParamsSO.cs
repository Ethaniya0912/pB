using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.Cameras; // [신규] 카메라 시퀀스 SO 참조를 위해 추가

namespace TDA.Character.Animation
{
    // =========================================================================================
    // [Phase1 신규] AnimationEventPoint
    //
    // 애니메이션 진행 시점(triggerTime)에 이벤트를 발행하는 타임라인 포인트.
    // overlayData 필드가 추가되었으나 useOverlay = false(기본값)이므로
    // 기존 모든 AnimationEventParamsSO 에셋은 변경 없이 그대로 동작합니다. (하위 호환 완벽)
    //
    // overlayData 사용 방법:
    //   1. useOverlay = true 체크
    //   2. overlayData에 원하는 FOV/블러/쉐이크 값 입력
    //   3. BaseActionBehaviour가 triggerTime 통과 시 WorldCameraManager.PlayOverlayEffect() 호출 [Phase2]
    // =========================================================================================
    [Serializable]
    public struct AnimationEventPoint
    {
        [Tooltip("이벤트가 발생할 애니메이션의 진행 시점 (0.0 = 시작, 1.0 = 끝)")]
        [Range(0f, 1f)]
        public float triggerTime;

        [Tooltip("발생시킬 이벤트의 종류 (마우스 드롭다운으로 선택하여 오타 원천 차단)")]
        public global::AnimationEventType eventType;

        // =========================================================================================
        // [Phase1 신규] 카메라/블러 오버레이 연동 필드
        // useOverlay = false(기본값)면 기존 동작과 100% 동일합니다.
        // =========================================================================================

        [Tooltip("[Phase1 신규] 이 이벤트 발행 시 카메라/블러 오버레이를 함께 적용할지 여부.\n" +
                 "false(기본값)이면 기존 동작과 완전히 동일합니다.")]
        public bool useOverlay;

        [Tooltip("[Phase1 신규] useOverlay = true일 때 WorldCameraManager.PlayOverlayEffect()로 전달할 오버레이 데이터.\n" +
                 "FOV 델타, 블러 강도, 쉐이크 등을 선택적으로 설정하세요. (0값은 해당 효과 없음)")]
        public TDA.Cameras.CameraEffectOverlayData overlayData;
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

        // =========================================================================================
        // 🚨 [허브 A 연동] 애니메이션과 1:1로 매핑되는 카메라 연출 시퀀스 데이터
        // =========================================================================================
        [Header("Camera Directing (SO Data-Driven)")]
        [Tooltip("이 애니메이션 액션이 시작될 때 재생할 2계층 카메라 시퀀스 에셋을 연결하세요. (예: 강공격 줌인 연출)")]
        public CameraSequencePresetSO cameraSequence;

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