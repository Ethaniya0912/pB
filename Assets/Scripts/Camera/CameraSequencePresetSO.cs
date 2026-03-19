using UnityEngine;
using System;
using System.Collections.Generic;

namespace TDA.Cameras
{
    [Serializable]
    public struct SequenceStep
    {
        [Tooltip("이 스텝에서 카메라가 취해야 할 목표 구도 및 연출 데이터 (1계층 SO)")]
        public CameraStancePresetSO targetStance;

        [Tooltip("이전 스탠스(또는 기본 뷰)에서 현재 타겟 스탠스로 넘어올 때 걸리는 보간(이동) 시간 (초 단위)")]
        public float blendDuration;

        [Tooltip("보간 속도 곡선. 서서히 빨라지다 느려지는(Ease In-Out) 등 카메라 워킹의 텐션을 결정합니다.")]
        public AnimationCurve blendCurve;

        [Tooltip("현재 스탠스에 완전히 도달한 후, 다음 스텝으로 넘어가기 전까지 머무르는 유지 시간 (초 단위)")]
        public float holdDuration;
    }

    /// <summary>
    /// [2계층 SO] 여러 개의 CameraStancePresetSO(1계층)와 타이밍(시간) 정보를 엮어 만든 동적 연출 타임라인입니다.
    /// 스크립트를 통한 코루틴 하드코딩 없이, 이 에셋 하나로 컷씬, 처형 모션, 피격, 인벤토리 오픈 등 완벽한 카메라 시퀀스를 설계합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCameraSequence", menuName = "TDA/Camera/2-Tier Sequence Preset")]
    public class CameraSequencePresetSO : ScriptableObject
    {
        [Header("시퀀스 타임라인 (Sequence Steps)")]
        [Tooltip("위에서 아래로(Index 0부터) 순차적으로 실행될 카메라 컷(Step)들의 목록입니다.")]
        public List<SequenceStep> steps = new List<SequenceStep>();

        [Header("시퀀스 예외 규칙 (Sequence Rules)")]
        [Tooltip("시퀀스 연출 도중, 플레이어가 이동(WASD)이나 공격 입력을 시도하면 즉시 이 연출을 캔슬하고 기본 시점으로 돌아갈지 여부입니다. (예: 인벤토리 오픈)")]
        public bool canBeInterruptedByInput = false;

        [Tooltip("시퀀스 연출 도중, 플레이어가 데미지를 입으면(피격) 연출을 강제로 중단하고 전투 시점으로 복귀할지 여부입니다. (일반적으로 True 권장)")]
        public bool canBeInterruptedByDamage = true;

        [Header("복귀 정책 (Restore Settings)")]
        [Tooltip("시퀀스 타임라인의 모든 스텝이 종료된 후, 원래의 인게임 숄더뷰(기본 카메라 상태) 시점으로 자동 복귀할지 여부입니다.")]
        public bool restoreToDefaultStanceOnFinish = true;

        [Tooltip("자동 복귀 시, 원래 시점까지 스르륵 돌아가는 데 걸리는 보간 시간(초)입니다.")]
        public float restoreBlendDuration = 1.0f;

        // =========================================================================================
        // [추가/보완] Editor 편의성 (기본 커브 세팅)
        // 새 스텝을 추가할 때 AnimationCurve가 텅 비어있어 직선(Linear)으로 딱딱하게 이동하는 것을
        // 방지하기 위해 데이터 유효성을 검사합니다.
        // =========================================================================================
#if UNITY_EDITOR
        private void OnValidate()
        {
            if (steps != null)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    SequenceStep step = steps[i];
                    // 커브가 비어있거나 키프레임이 없다면 부드러운 기본 Ease-In-Out 커브로 초기화해 줍니다.
                    if (step.blendCurve == null || step.blendCurve.length == 0)
                    {
                        step.blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
                        steps[i] = step;
                    }
                }
            }
        }
#endif
    }
}