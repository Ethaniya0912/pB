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

        // ── [개선 P-1] 락온 전용 스탠스 오버라이드 ──────────────────────────────────────
        // 락온(isLockedOn = true) 상태에서 이 액션이 실행될 때,
        // cameraSequence(Seq) 전체를 재생하는 대신 이 Stance 만 단독으로 교체합니다.
        //
        // 사용 목적:
        //   Seq_LockOn 이 재생 중인 락온 상태에서 공격하면, 기존에는 Seq_LightSlash 가 시작되며
        //   Seq_LockOn 컨텍스트를 완전히 이탈했습니다. lockOnStanceOverride 를 설정하면
        //   Seq_LockOn 내부에서 Stance 만 교체하는 깔끔한 흐름을 유지할 수 있습니다.
        //
        // 설정 방법:
        //   1. lockOnStanceOverride 슬롯에 공격 시 전환될 Stance SO 연결
        //      (예: Stance_Light_Impact_SO)
        //   2. 락온 중 공격 시 ChangeCameraStance() 만 호출됩니다.
        //   3. null 이면 기존 cameraSequence 로 폴백 (하위 호환 100%)
        [Tooltip("[개선 P-1] 락온(isLockedOn=true) 상태에서 이 액션 실행 시,\n" +
                 "cameraSequence(Seq) 전체를 재생하는 대신 이 Stance 만 단독 교체합니다.\n\n" +
                 "null 이면 기존 cameraSequence 로 폴백합니다. (하위 호환)\n\n" +
                 "예: Stance_Light_Impact_SO 연결 → 락온 공격 시 Seq_LockOn 컨텍스트를 유지하면서\n" +
                 "    Stance_Light_Impact 로만 전환. 공격 종료 후 Seq_LockOn 이 자연스럽게 이어집니다.")]
        public TDA.Cameras.CameraStancePresetSO lockOnStanceOverride;

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