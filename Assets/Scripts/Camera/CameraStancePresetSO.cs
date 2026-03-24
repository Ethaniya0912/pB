using UnityEngine;
using System;
using System.Collections.Generic;

namespace TDA.Cameras
{
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

    // =========================================================================================
    // 🚨 [신규 추가] 수직(상하) 시점 조작감 분기 Enum
    // =========================================================================================
    public enum CameraVerticalBehavior
    {
        ClassicPivot,           // [선택 3] 기존처럼 중심점을 기준으로 팽이 돌듯 회전
        ElevationOnly,          // [선택 1] 각도는 고정된 채 카메라 Y축 높이만 승강기처럼 위아래로 이동 (2.5D/퍼즐용)
        DynamicOverShoulder     // [선택 2] 바닥을 볼수록 카메라가 솟아올라 정수리 파고듬을 방지 (AAA급 숄더뷰)
    }

    [Serializable]
    public struct TargetWeightInfo
    {
        [Tooltip("화면 중심 계산에 포함할 타겟 식별자 (예: PlayerChest, LockedOnEnemyChest)")]
        public TargetIdentifier target;

        [Tooltip("이 타겟이 카메라 중심점에 미치는 영향력(가중치). 높을수록 카메라가 이 타겟 쪽으로 쏠립니다. (예: 보스 0.8, 플레이어 0.2)")]
        [Range(0f, 1f)] public float weight;
    }

    // =========================================================================================
    // 🚨 [신규 추가] 수직 조작감 디테일 파라미터 구조체
    // =========================================================================================
    [Serializable]
    public struct VerticalBehaviorData
    {
        [Tooltip("마우스를 위아래로 움직일 때 카메라가 반응하는 방식을 결정합니다.")]
        public CameraVerticalBehavior behaviorType;

        [Header("선택 1: Elevation Only Settings")]
        [Tooltip("마우스 수직 입력 시 카메라 높이(Y축)가 변하는 감도(속도)입니다.")]
        public float elevationSpeed;
        [Tooltip("마우스를 끝까지 올렸을 때 도달할 최대 Y축 오프셋 (예: +3.0m)")]
        public float maxElevationHeight;
        [Tooltip("마우스를 끝까지 내렸을 때 도달할 최소 Y축 오프셋 (예: +0.5m)")]
        public float minElevationHeight;
        [Tooltip("이 뷰를 사용할 때 강제 고정될 카메라의 Pitch 각도 (예: 25도 = 살짝 내려다봄)")]
        public float fixedPitchAngle;

        [Header("선택 2: Dynamic Over Shoulder Settings")]
        [Tooltip("카메라가 이 각도(양수 = 바닥을 내려다봄)에 도달할 때, 아래의 '최대 추가 높이'가 100% 적용됩니다. (예: 45도)")]
        public float pitchForMaxHeight;
        [Tooltip("바닥을 내려다볼 때, 기존 Base Y Offset에 동적으로 더해질 최대 추가 높이입니다. (예: +1.5m)")]
        public float maxDynamicHeight;
        [Tooltip("마우스를 빠르게 상하로 흔들 때, 카메라 높이가 덜컹거리지 않게 잡아주는 완충 시간(초)입니다. (예: 0.1s)")]
        public float heightSmoothTime;
    }

    [Serializable]
    public struct DynamicFramingData
    {
        [Header("Position Offset (X축)")]
        [Tooltip("좌측 게걸음(Strafe) 시 화면 좌측으로 치우칠 최대 거리 (예: -0.4)")]
        public float leftStrafeMaxOffset;

        [Tooltip("우측 게걸음(Strafe) 시 화면 우측/중앙으로 치우칠 최대 거리 (예: 0.4)")]
        public float rightStrafeMaxOffset;

        [Header("Hold Settings (정지 시 유지 여부)")]
        [Tooltip("좌측 이동을 멈췄을 때, 카메라 중심이 원점으로 돌아오지 않고 좌측 오프셋을 유지할지 여부")]
        public bool holdLeftStrafe;

        [Tooltip("우측 이동을 멈췄을 때, 카메라 중심이 원점으로 돌아오지 않고 우측 오프셋을 유지할지 여부")]
        public bool holdRightStrafe;

        [Header("Rotation Offset (Yaw 각도)")]
        [Tooltip("좌측 게걸음 시 카메라가 꺾일 추가 Y각도 (예: -15도)")]
        public float leftStrafeYaw;

        [Tooltip("우측 게걸음 시 카메라가 꺾일 추가 Y각도 (예: 10도)")]
        public float rightStrafeYaw;

        [Tooltip("다중 타겟 포커스(락온) 사용 시, 이 다이내믹 Yaw 각도를 얼마나 반영할지 결정합니다. (0 = 타겟팅 우선 / 1 = 앵글 꺾임 우선)")]
        [Range(0f, 1f)] public float dynamicYawWeight;

        [Header("Delay & Speed Settings (보간 속도)")]
        [Tooltip("좌측으로 카메라가 쏠릴 때의 반응 속도(지연 시간). 작을수록 빠름.")]
        public float leftFramingDelay;

        [Tooltip("우측으로 카메라가 쏠릴 때의 반응 속도(지연 시간).")]
        public float rightFramingDelay;

        [Tooltip("이동을 멈추고(Hold 해제 상태일 때) 원점(0)으로 복귀하는 속도. 스냅백 멀미를 막기 위해 보통 더 느리게(값 크게) 줍니다.")]
        public float centerReturnDelay;

        [Header("Auto Centering (전후방 직진 시 복귀)")]
        [Tooltip("앞이나 뒤로 이동할 때, 편향되어 있던 시야와 오프셋이 서서히 영점(가운데)으로 돌아오는데 걸리는 시간입니다. (예: 5.0초)")]
        public float forwardBackwardReturnTime;
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

        [Tooltip("이동할 때 상하좌우로 덜컹거리는 보빙(Bobbing) 강도입니다. (뛸 때 화면 진동의 주 원인)")]
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

    // =========================================================================================
    // 🚨 [고도화 방안 추가 구조체]
    // =========================================================================================

    [Serializable]
    public struct EdgePanningData
    {
        [Tooltip("락온 중일 때 마우스 조작 데드존을 적용하고 화면 끝에서만 앵글이 돌게 할 것인지 여부")]
        public bool useEdgePanning;

        [Tooltip("화면 끝 판정 임계값 (예: 0.1 이면 화면 가장자리 10% 영역 안에서만 카메라 회전 작동)")]
        [Range(0.01f, 0.5f)] public float edgePanThreshold;

        [Tooltip("락온 중 마우스/스틱 조작 시 무시할 입력의 크기 (일반 회전 억제용)")]
        public float lockOnInputDeadzone;

        // ── [v4.0 신규] 엣지 패닝 부드러움 & 복귀 파라미터 ──────────────────

        [Tooltip("[v4.0] 엣지 발동 중 카메라 입력이 부드럽게 보간되는 시간입니다. " +
                 "값이 클수록 시선 이동이 완만해집니다. (기본 0.12s, 문제4 대응)")]
        [Range(0.02f, 0.8f)] public float edgePanSmoothTime;

        [Tooltip("[v4.0] 마우스가 엣지 구역을 벗어났을 때 카메라가 원래 시점으로 " +
                 "천천히 복귀하는 데 걸리는 시간입니다. (기본 0.8s, 문제3 대응)")]
        [Range(0.1f, 3.0f)] public float edgePanReturnTime;
    }

    [Serializable]
    public struct DynamicInputModifierData
    {
        [Tooltip("이동량(Input moveAmount)에 따라 FOV와 Offset이 동적으로 변하는 기능을 사용할 것인지 여부")]
        public bool enableDynamicInputModifier;

        [Tooltip("입력 강도(X축: 0~1)에 따라 추가할 FOV 증감량(Y축)")]
        public AnimationCurve fovModifierCurve;

        [Tooltip("입력 강도(X축: 0~1)에 따라 카메라를 뒤로 당길(밀어낼) Z축 거리(Y축)")]
        public AnimationCurve offsetZModifierCurve;
    }

    [Serializable]
    public struct LockOnPenaltyData
    {
        [Tooltip("타겟 시야 이탈 시 자동 추적(Follow) 중단 및 페널티 활성화 여부")]
        public bool enableTargetEscapePenalty;

        [Tooltip("타겟을 화면 밖으로 판정할 뷰포트 마진 (0.0=완전끝단, 권장: 0.05~0.15)")]
        [Range(0f, 0.5f)] public float targetEscapeViewportThreshold;

        [Tooltip("True시 하드 보정(즉각 정렬), False시 소프트 보정(거리 비례 보간) 적용")]
        public bool useHardCorrection;

        [Tooltip("[소프트 보정] 타겟과의 '거리(X축)'에 따른 '회전 복귀 속도(Y축)' 가중치 커브")]
        public AnimationCurve softCorrectionDistanceCurve;

        [Tooltip("[A/D조작 페널티 극복] 유저 이동 입력에 의해 앵글이 꺾이며 회복되는 강도 (기본 1.0)")]
        public float strafeRecoveryWeight;

        // ── [Implementation Spec] 신규 파라미터 ─────────────────────────────

        [Tooltip("[소프트 보정] 타겟 화면 복귀 시 보간 기본 속도. 클수록 빠르게 보정됩니다. (기본 2.0)")]
        [Range(0.1f, 15f)] public float softRecoverySpeed;

        [Tooltip("[Hysteresis] 타겟이 화면 안으로 들어와도 이 시간(초) 동안 escape 상태를 유지합니다. 경계 펄럭임 방지용. (기본 0.25s)")]
        [Range(0f, 2f)] public float escapePersistTime;

        [Tooltip("[수직 복귀] 위아래 엣지패닝 후 이동 중 기본 Pitch(fixedPitchAngle)로 복귀하는 속도. 0이면 복귀 없음. (기본 1.0)")]
        [Range(0f, 5f)] public float pitchReturnSpeed;
        // ─────────────────────────────────────────────────────────────────────
    }


    // ============================================================
    // [Implementation Spec 신규] 다중 타겟 동적 프레이밍 파라미터
    // ============================================================
    [Serializable]
    public struct MultiTargetFramingData
    {
        [Tooltip("플레이어 기준 이 반경(m) 안에 있는 적만 동적 포커싱 가산 대상으로 포함합니다. (기본 12m)")]
        [Range(1f, 50f)] public float focusDetectionRadius;

        [Tooltip("뷰포트 안에 있는 주변 적 1명당 포커싱 중심에 기여하는 가중치. 락온 타겟 weight와 합산됩니다. (기본 0.2)")]
        [Range(0f, 1f)] public float proximityEnemyWeight;

        [Tooltip("포커싱 피벗 이동의 SmoothDamp 완충 시간. 클수록 부드럽고 느린 전환이 됩니다. (기본 0.35s)")]
        [Range(0.05f, 3f)] public float focusDampTime;

        [Tooltip("SO focusTargets 고정 타겟들의 weight 스케일 배수. 1.0이면 SO 설정값 그대로, 0이면 고정 타겟 무시. (기본 1.0)")]
        [Range(0f, 1f)] public float staticTargetWeightScale;
    }
    // ============================================================

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

        [Tooltip("카메라의 정지 시 기본 좌/우 회전 편향 각도입니다. 캐릭터 어깨 너머 뷰의 베이스 방향을 제어합니다.")]
        [Range(-45f, 45f)] public float baseYawOffset = 0f;

        [Tooltip("타겟(Pivot)을 기준으로 한 카메라의 상대적 오프셋입니다. (X: 좌우, Y: 높이, Z: 거리)")]
        public Vector3 baseOffset = new Vector3(0.5f, 1.5f, -2.5f);

        // =========================================================================================
        // 🚨 [신규 추가] 수직 조작감 데이터 할당
        // =========================================================================================
        [Header("수직(상하) 시점 조작 방식 (Vertical Behavior)")]
        public VerticalBehaviorData verticalBehavior = new VerticalBehaviorData
        {
            behaviorType = CameraVerticalBehavior.ClassicPivot, // 기본은 익숙한 팽이 궤도
            elevationSpeed = 5.0f,
            maxElevationHeight = 3.5f,
            minElevationHeight = 0.5f,
            fixedPitchAngle = 25.0f,
            pitchForMaxHeight = 45.0f, // 🚨 양수(+)로 수정 완료 (바닥을 보는 각도)
            maxDynamicHeight = 1.5f,
            heightSmoothTime = 0.15f
        };

        [Header("동적 프레이밍 (Dynamic Framing)")]
        [Tooltip("횡이동(Strafe) 시 이동 방향에 따라 카메라 중심이 자동으로 쏠리는(편향) 연출을 사용할지 여부입니다.")]
        public bool useDynamicFraming = false;

        // 🚨 [추가된 코드] 공격 시퀀스 등에서 직전 프레이밍 상태를 그대로 이어받을지 결정합니다.
        [Tooltip("이 옵션이 켜져 있으면 위/아래의 프레이밍 수치를 모두 무시하고, 이 스탠스에 돌입하기 직전의 꺾여있던 화면 구도를 그대로 얼려서(Freeze) 유지합니다.")]
        public bool inheritDynamicFraming = false;

        public DynamicFramingData dynamicFraming = new DynamicFramingData
        {
            leftStrafeMaxOffset = -0.4f,
            rightStrafeMaxOffset = 0.4f,
            holdLeftStrafe = false,
            holdRightStrafe = false,
            leftStrafeYaw = -15f,
            rightStrafeYaw = 10f,
            dynamicYawWeight = 1.0f,
            leftFramingDelay = 0.15f,
            rightFramingDelay = 0.15f,
            centerReturnDelay = 0.35f,
            forwardBackwardReturnTime = 5.0f
        };

        [Header("다중 타겟 포커싱 (Multi-Targeting)")]
        [Tooltip("화면에 담을 여러 타겟(플레이어, 보스 등)과 각 타겟별 개별 가중치(Weight)를 리스트로 관리합니다.")]
        public List<TargetWeightInfo> focusTargets = new List<TargetWeightInfo>();

        [Tooltip("다중 타겟 동적 프레이밍의 탐지 반경, 가중치, 보간 속도 파라미터 모음입니다.")]
        public MultiTargetFramingData multiTargetFraming = new MultiTargetFramingData
        {
            focusDetectionRadius = 12f,
            proximityEnemyWeight = 0.2f,
            focusDampTime = 0.35f,
            staticTargetWeightScale = 1.0f
        };

        // =========================================================================================
        // 🚨 [고도화 시스템 데이터 세팅] 
        // =========================================================================================

        [Header("락온 및 엣지 패닝 (Edge Panning)")]
        [Tooltip("락온 시 엣지 패닝 및 조작 데드존 기능 활성화 데이터")]
        public EdgePanningData edgePanningData = new EdgePanningData
        {
            useEdgePanning = false,
            edgePanThreshold = 0.1f,
            lockOnInputDeadzone = 0.5f,
            edgePanSmoothTime = 0.12f,
            edgePanReturnTime = 0.8f
        };

        [Header("시야 이탈 페널티 (Target Escape Penalty)")]
        [Tooltip("락온 타겟이 화면을 벗어날 때 발생하는 페널티 및 보정 데이터")]
        public LockOnPenaltyData lockOnPenaltyData = new LockOnPenaltyData
        {
            enableTargetEscapePenalty = false,
            targetEscapeViewportThreshold = 0.1f,
            useHardCorrection = false,
            strafeRecoveryWeight = 1.0f,
            softRecoverySpeed = 2.0f,
            escapePersistTime = 0.25f,
            pitchReturnSpeed = 1.0f
        };

        [Header("이동 입력 동기화 (Dynamic Input Modifier)")]
        [Tooltip("유저의 실제 컨트롤러/마우스 이동 입력 크기에 따라 FOV나 거리가 유동적으로 줌인/줌아웃되는 세팅")]
        public DynamicInputModifierData dynamicInputModifier;

        // =========================================================================================

        [Header("카메라 감각 피드백 (Camera Feel)")]
        [Tooltip("숨결이나 발걸음에 의해 카메라가 출렁이는 '바디캠(핸드헬드)' 효과 설정입니다.")]
        public HandheldNoiseData handheldEffect = new HandheldNoiseData
        {
            enableHandheldEffect = true,
            swayAmount = 0.5f,
            swaySpeed = 1.0f,
            bobbingAmount = 1.2f // 🚨 뛸 때 진동이 거슬리면 이 값을 0으로 내립니다.
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

        [Header("Debug Control (Safe-net)")]
        [Tooltip("디버그 모드가 켜져 있을 때, 이 스탠스로 전환이 완료되는 순간 유니티 에디터를 강제로 일시정지(Pause)합니다.")]
        public bool pauseOnApply = false;
    }
}