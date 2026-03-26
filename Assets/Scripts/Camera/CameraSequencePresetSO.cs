using UnityEngine;
using System;
using System.Collections.Generic;

namespace TDA.Cameras
{

    // =========================================================================================
    // [QTE 시스템] CameraQTEPhaseData — QTE 단계별 카메라·입력·성공/실패 데이터
    // CharacterQTEManager.StartQTE(), PlayerExecutionManager, PlayerQTEManager에서 사용됩니다.
    // =========================================================================================
    [Serializable]
    public class CameraQTEPhaseData
    {
        [Tooltip("이 단계에서 재생할 카메라 시퀀스 SO입니다. null이면 카메라 변화 없음.")]
        public CameraSequencePresetSO cameraSequence;

        [Tooltip("플레이어 입력 허용 시간(초). 0 이하이면 입력 창 없이 자동 진행합니다.")]
        public float inputWindowDuration = 2.0f;

        [Tooltip("이 단계에서 사용할 입력 키코드입니다. (None이면 PlayerQTEManager의 기본 키 사용)")]
        public KeyCode inputKey = KeyCode.None;

        [Tooltip("이 단계 성공 시 재생할 히트스탑 강도입니다. (0이면 없음)")]
        public float successHitstopIntensity = 0f;

        [Tooltip("이 단계 실패 시 플레이어에게 가할 데미지 배율입니다. (1=100%)")]
        public float failureDamageMultiplier = 1.0f;

        [Tooltip("이 단계에서 AI가 역공격하는 ActionID입니다. (-1이면 없음)")]
        public int failureCounterAttackID = -1;
    }

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

        // =========================================================================================
        // 🚨 [0순위 Data] 멀미 방지용 추적 구간(Tracking Window) 변수 추가
        // =========================================================================================
        [Header("Tracking Window (멀미 방지)")]
        [Tooltip("이 스텝에서 카메라가 캐릭터의 회전/타겟을 따라가기 시작할 애니메이션 진행도 (0.0~1.0)")]
        [Range(0f, 1f)] public float trackingStartTime;

        [Tooltip("카메라 추적을 종료하고 시야를 고정할 진행도 (0.0~1.0)")]
        [Range(0f, 1f)] public float trackingEndTime;

        [Tooltip("추적 가중치 곡선 (시선이 급격하게 튀는 것을 막고 부드럽게 개입하기 위해 사용)")]
        public AnimationCurve trackingWeightCurve;

        // =========================================================================================
        // 🚨 [v3.0 고도화] 액션 댐핑 오버라이드 (Action Damping Override)
        // 360도 스윙이나 타격 순간 등에서 발생하는 High-frequency 덜덜거림을 억제합니다.
        // =========================================================================================
        [Header("Action Damping Override (액션 묵직함 제어)")]
        [Tooltip("액션 중 카메라의 '위치'가 덜덜거리는 것을 막는 지연 시간입니다. 값이 클수록 위치가 묵직하게 따라갑니다. (기본: 0.1, 추천: 0.2~0.3)")]
        public float actionPositionDamping;

        [Tooltip("액션 중 카메라의 '앵글'이 덜덜거리는 것을 막는 지연 시간입니다. 값이 클수록 10kg 스테디캠을 단 것처럼 화면 회전이 무거워집니다. (기본: 0.1, 추천: 0.2~0.3)")]
        public float actionRotationDamping;
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
        // 🚨 [Phase 3 고도화] 2. 연출/동작 종료 후 이전 시점(카메라 앵글)으로 복귀
        // =========================================================================================
        [Tooltip("시퀀스 진입 직전의 절대 앵글(Pitch, Yaw)을 기억했다가 종료 시 원래 각도로 되돌립니다. (공격 후 엉뚱한 곳을 바라보는 시점 틀어짐 방지)")]
        public bool restorePreviousAngle = false;

        [Tooltip("restorePreviousAngle 사용 시, 이전 각도로 복귀하는 보간 속도입니다. 클수록 빠르고 작을수록 부드럽습니다. (기본 3.0 → 약 0.8초에 95% 수렴)")]
        [Range(0.5f, 15f)] public float restoreAngleSpeed = 3.0f;

        // =========================================================================================
        // [v4.4 신규] 공격 후 복귀 구도 — 프레이밍 오프셋 복원
        // =========================================================================================
        [Tooltip("[v4.4] 시퀀스 진입 직전의 Dynamic Framing X 오프셋(숄더뷰 구도)을 기억했다가\n" +
                 "종료 시 그 값으로 부드럽게 복원합니다.\n" +
                 "공격 후 돌아왔을 때 공격 전의 구도(예: 플레이어 좌측 구도)가 유지됩니다.")]
        public bool restorePreviousFraming = false;

        [Tooltip("[v4.4] 프레이밍 복원 시 보간 시간(초)입니다.\n" +
                 "0이면 즉시 스냅, 0.2~0.5이면 자연스럽게 슬라이드됩니다. (기본 0.3s)")]
        [Range(0f, 2f)] public float restoreFramingBlendTime = 0.3f;

        [Tooltip("[v4.4] restoreToDefaultStanceOnFinish=true 복귀 시 발생하는\n" +
                 "FOV/baseOffset/baseYawOffset 플리커 문제를 해결합니다.\n" +
                 "true이면 시퀀스 진입 직전의 렌즈·구도 값을 저장해두었다가\n" +
                 "defaultRestStance 복귀 Blend의 시작값으로 사용합니다.\n" +
                 "Seq_LockOn처럼 restoreToDefaultStanceOnFinish=true인 시퀀스에 권장합니다.")]
        public bool restorePreviousStanceValues = false;

        [Header("Debug Control (Safe-net)")]
        [Tooltip("디버그 모드일 때, 이 시퀀스가 시작되는 순간 유니티 에디터를 강제로 일시정지(Pause)합니다.")]
        public bool pauseOnApply = false;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (steps != null)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    SequenceStep step = steps[i];

                    if (step.blendCurve == null || step.blendCurve.length == 0)
                        step.blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

                    if (step.trackingWeightCurve == null || step.trackingWeightCurve.length == 0)
                    {
                        step.trackingWeightCurve = AnimationCurve.Constant(0f, 1f, 1f);
                        step.trackingEndTime = 1f;
                    }

                    if (step.actionPositionDamping <= 0f) step.actionPositionDamping = 0.1f;
                    if (step.actionRotationDamping <= 0f) step.actionRotationDamping = 0.1f;

                    steps[i] = step;
                }
            }
        }
#endif
    }
}