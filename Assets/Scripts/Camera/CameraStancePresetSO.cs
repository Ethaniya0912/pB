using UnityEngine;
using System;
using System.Collections.Generic;

namespace TDA.Cameras
{
    // =========================================================================================
    // [추가 사항] 다중 타겟 포커싱을 위한 타겟 식별자 Enum
    // (설계서에 '별도 Enum'으로 명시된 부분을 코드가 즉시 작동할 수 있도록 구체화하여 추가했습니다)
    // =========================================================================================
    public enum TargetIdentifier
    {
        PlayerRoot,         // 플레이어의 발끝
        PlayerChest,        // 플레이어의 가슴 (질량 중심)
        PlayerWeaponTip,    // 플레이어의 무기 끝 (피니시 연출 등)
        PlayerShield,       // 플레이어의 방패 (패링 연출 등)
        LockedOnEnemyRoot,  // 현재 락온된 적의 발끝
        LockedOnEnemyChest, // 현재 락온된 적의 가슴 (주요 포커스)
        InteractableObject  // 현재 상호작용 중인 오브젝트 (문, 레버 등)
    }

    [Serializable]
    public struct TargetWeightInfo
    {
        [Tooltip("화면 중심 계산에 포함할 타겟 식별자 (예: PlayerChest, LockedOnEnemyChest)")]
        public TargetIdentifier target;

        [Tooltip("이 타겟이 카메라 중심점에 미치는 영향력(가중치). 높을수록 카메라가 이 타겟 쪽으로 쏠립니다. (예: 보스 0.8, 플레이어 0.2)")]
        [Range(0f, 1f)] public float weight;
    }

    [Serializable]
    public struct DynamicFramingData
    {
        [Tooltip("좌측 게걸음(Strafe) 시 화면 좌측으로 치우칠 최대 거리 (예: -0.5)")]
        public float leftStrafeMaxOffset;

        [Tooltip("우측 게걸음(Strafe) 시 화면 우측/중앙으로 치우칠 최대 거리 (예: 0.2)")]
        public float rightStrafeMaxOffset;

        [Header("Hold Settings (정지 시 유지 여부)")]
        [Tooltip("좌측 이동을 멈췄을 때, 카메라 중심이 원점으로 돌아오지 않고 좌측 오프셋을 유지할지 여부")]
        public bool holdLeftStrafe;

        [Tooltip("우측 이동을 멈췄을 때, 카메라 중심이 원점으로 돌아오지 않고 우측 오프셋을 유지할지 여부")]
        public bool holdRightStrafe;

        [Header("Delay & Speed Settings (보간 속도)")]
        [Tooltip("좌측으로 카메라가 쏠릴 때의 반응 속도(지연 시간). 작을수록 빠름.")]
        public float leftFramingDelay;

        [Tooltip("우측으로 카메라가 쏠릴 때의 반응 속도(지연 시간).")]
        public float rightFramingDelay;

        [Tooltip("이동을 멈추고(Hold 해제 상태일 때) 원점(0)으로 복귀하는 속도. 스냅백 멀미를 막기 위해 보통 더 느리게(값 크게) 줍니다.")]
        public float centerReturnDelay;
    }

    [Serializable]
    public struct HandheldNoiseData
    {
        [Tooltip("핸드헬드(바디캠) 흔들림 효과를 적용할지 여부입니다. 끄면 삼각대에 고정한 것처럼 딱딱해집니다.")]
        public bool enableHandheldEffect;

        [Tooltip("가만히 있을 때 숨쉬듯 흔들리는 수전증(Sway) 강도입니다.")]
        public float swayAmount;

        [Tooltip("흔들림의 속도 (호흡 빈도)입니다.")]
        public float swaySpeed;

        [Tooltip("이동할 때 상하좌우로 덜컹거리는 보빙(Bobbing) 강도입니다.")]
        public float bobbingAmount;
    }

    [Serializable]
    public struct CameraShakeData
    {
        [Tooltip("이 스탠스 돌입 시 단발성 카메라 진동(Shake)을 발생시킬지 여부입니다.")]
        public bool enableShake;

        [Tooltip("진동 강도 (폭발, 타격, 넉백의 묵직함을 결정합니다)")]
        public float shakeIntensity;

        [Tooltip("스탠스 진입 후 진동이 시작되기까지의 대기 시간(초)입니다. 칼이 바닥에 닿는 정확한 순간에 맞추기 위해 사용합니다.")]
        public float shakeDelay;

        [Tooltip("진동이 지속되는 최대 시간 (초)입니다.")]
        public float maxDuration;
    }

    /// <summary>
    /// [1계층 SO] 특정 찰나(Shot)에 카메라가 머물러야 할 시각적, 공간적, 감각적 설정값을 담는 순수 데이터 컨테이너입니다.
    /// 스크립트 내부 하드코딩을 탈피하여, 이 에셋 하나만 넘겨주면 카메라가 즉각적으로 해당 구도를 렌더링합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCameraStance", menuName = "TDA/Camera/1-Tier Stance Preset")]
    public class CameraStancePresetSO : ScriptableObject
    {
        [Header("렌즈 및 구도 (Lens & Framing)")]
        [Tooltip("카메라의 시야각 (FOV). 줌인/줌아웃의 심리적 거리감을 결정합니다.")]
        [Range(10f, 120f)] public float fov = 60f;

        [Tooltip("Z축 기울기 (Dutch Angle). 불안감, 광기, 타격의 충격을 시각화할 때 화면을 비스듬하게 꺾습니다.")]
        [Range(-45f, 45f)] public float zTilt = 0f;

        // =========================================================================================
        // 🚨 [0순위 Data] 좌우 앵글 보정(Yaw Offset) 그릇 추가
        // =========================================================================================
        [Tooltip("카메라의 기본 좌/우 회전 편향 각도입니다. 캐릭터 어깨 너머 뷰의 방향성과 역동성을 제어합니다.")]
        [Range(-45f, 45f)] public float yawOffset = 0f;

        [Tooltip("타겟(Pivot)을 기준으로 한 카메라의 상대적 오프셋입니다. (X: 좌우, Y: 높이, Z: 거리)")]
        public Vector3 baseOffset = new Vector3(0.5f, 1.5f, -2.5f);

        [Header("동적 프레이밍 (Dynamic Framing)")]
        [Tooltip("횡이동(Strafe) 시 이동 방향에 따라 카메라 중심이 자동으로 쏠리는(편향) 연출을 사용할지 여부입니다.")]
        public bool useDynamicFraming = false;
        public DynamicFramingData dynamicFraming = new DynamicFramingData
        {
            leftStrafeMaxOffset = -0.4f,
            rightStrafeMaxOffset = 0.4f,
            holdLeftStrafe = false,
            holdRightStrafe = false,
            leftFramingDelay = 0.15f,
            rightFramingDelay = 0.15f,
            centerReturnDelay = 0.35f
        };

        [Header("다중 타겟 포커싱 (Multi-Targeting)")]
        [Tooltip("화면에 담을 여러 타겟(플레이어, 보스 등)과 각 타겟별 개별 가중치(Weight)를 리스트로 관리합니다.")]
        public List<TargetWeightInfo> focusTargets = new List<TargetWeightInfo>();

        [Header("카메라 감각 피드백 (Camera Feel)")]
        [Tooltip("숨결이나 발걸음에 의해 카메라가 출렁이는 '바디캠(핸드헬드)' 효과 설정입니다.")]
        public HandheldNoiseData handheldEffect = new HandheldNoiseData
        {
            enableHandheldEffect = true,
            swayAmount = 0.5f,
            swaySpeed = 1.0f,
            bobbingAmount = 1.2f
        };

        [Tooltip("폭발, 타격, 지진 등 특정 시점에 단발성으로 터지는 충격(Shake) 효과 설정입니다.")]
        public CameraShakeData impactShake = new CameraShakeData
        {
            enableShake = false,
            shakeIntensity = 0.5f,
            shakeDelay = 0f,
            maxDuration = 0.3f
        };

        [Header("시각 효과 덮어쓰기 (Post-Processing Overrides)")]
        [Tooltip("이 스탠스가 유지되는 동안 활성화될 심도(Depth of Field)의 초점 거리입니다.")]
        public float dofFocusDistance = 2.0f;

        [Tooltip("이 스탠스 특유의 화면 외곽 어두워짐(Vignette) 강도입니다. 피격이나 공포 연출 시 올립니다.")]
        [Range(0f, 1f)] public float vignetteIntensity = 0.3f;

        // =========================================================================================
        // 🚨 [0순위 Safe-net] 디버그 일시정지(Pause) 방어막 데이터 추가
        // =========================================================================================
        [Header("Debug Control (Safe-net)")]
        [Tooltip("디버그 모드가 켜져 있을 때, 이 스탠스로 전환이 완료되는 순간 유니티 에디터를 강제로 일시정지(Pause)합니다.")]
        public bool pauseOnApply = false;
    }
}