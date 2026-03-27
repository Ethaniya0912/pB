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

    // =========================================================================================
    // [v4.4 신규] 몸통 Yaw 추적 제어 데이터
    // 공격 루트모션처럼 캐릭터 몸통이 급격히 회전할 때
    // 카메라가 얼마나, 얼마나 부드럽게 따라갈지를 제어합니다.
    // =========================================================================================
    // =========================================================================================
    // [v4.4] BodyYawTrackingData — 공격 중 카메라 고정 제어
    // bodyYawFollowWeight=0 + lockOnActionStart=true 조합이 핵심 전투 카메라 설정입니다.
    // =========================================================================================
    [Serializable]
    public struct BodyYawTrackingData
    {
        [Tooltip("캐릭터 몸통 Yaw 회전을 카메라에 반영하는 비율입니다.\n" +
                 "1.0 = 즉각 100% 추적 (기본 동작)\n" +
                 "0.0 = 완전 차단 (공격 루트모션이 카메라에 전달되지 않음, 어지러움 방지)\n" +
                 "권장 전투 설정: 0.0")]
        [Range(0f, 1f)] public float bodyYawFollowWeight;

        [Tooltip("몸통 Yaw를 따라갈 때 SmoothDamp 보간 시간(초)입니다.\n" +
                 "0 = 즉각 / 0.1~0.3 = 부드럽게 흡수\n" +
                 "bodyYawFollowWeight=0이면 이 값은 무관합니다.")]
        [Range(0f, 1f)] public float bodyYawFollowSmoothTime;

        // ── [v4.4 신규] 공격 시작 시 카메라 각도 고정 ─────────────────────────────

        [Tooltip("[v4.4] 이 Stance가 공격 Stance일 때, isPerformingAction=true 진입 순간의\n" +
                 "카메라 각도(Yaw/Pitch)를 스냅샷으로 저장하고 공격이 끝날 때까지 유지합니다.\n" +
                 "루트모션 회전, FocusPivot, Magnetic 등 모든 외부 시스템의 각도 개입을 차단합니다.\n" +
                 "비락온/락온 모두 작동합니다. 공격 스탠스에는 true를 권장합니다.\n" +
                 "기본값: false (기존 동작 유지)")]
        public bool lockCameraOnActionStart;

        [Tooltip("[v4.4] lockCameraOnActionStart=true일 때 마우스/스틱 입력을 얼마나 허용할지입니다.\n" +
                 "0.0 = 완전 고정 (마우스로도 카메라 이동 불가)\n" +
                 "1.0 = 마우스 입력 전량 허용 (고정 기준점만 스냅샷, 이후 자유 회전)\n" +
                 "권장: 0.3~0.5 (약간의 시선 조작은 허용하되 루트모션 흔들림은 차단)")]
        [Range(0f, 1f)] public float lockCameraMouseInfluence;
    }
    // =========================================================================================

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

        // ── [v4.4 신규] 락온 진입 시 초기 프레이밍 ─────────────────────────────────

        [Tooltip("[v4.4] 락온 진입 순간 카메라가 자동으로 잡아주는 초기 X축 프레이밍 오프셋입니다.\n" +
                 "양수 = 플레이어가 화면 좌측, 타겟이 중앙~우측 구도 (기본 0 = 변경 없음)\n" +
                 "예: 0.6 이면 플레이어가 좌측으로 밀리고 우측에 공간이 생깁니다.")]
        public float lockOnInitialFramingOffset;

        [Tooltip("[v4.4] 락온 진입 시 초기 프레이밍이 목표값까지 도달하는 보간 시간(초)입니다.\n" +
                 "0이면 즉시 스냅, 0.3~0.6이면 자연스럽게 슬라이드 됩니다. (기본 0.3s)")]
        [Range(0f, 2f)] public float lockOnInitialFramingBlendTime;
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

        [Tooltip("이 거리(m) 이하에서는 타겟 이탈(ESCAPE) 판정을 건너뜁니다.\n" +
                 "몹이 아주 가까이 있을 때 lockOnTransform이 near clip 안쪽에 들어가\n" +
                 "잘못된 ESCAPE 판정이 발생해 카메라가 뱅글뱅글 도는 버그를 방지합니다.\n" +
                 "권장: 2~4m. 0이면 내부 기본값 3m 사용.")]
        [Range(0f, 10f)] public float escapeMinDistance;

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

    // =========================================================================================
    // [Phase1 신규] CameraEffectOverlayData
    //
    // SO 기본 카메라 값(FOV, Blur 등) 위에 이벤트 구간 동안 순간적으로 덮어씌우는 오버레이 데이터.
    //
    // 사용 위치:
    //   ① StanceEventPoint.overlayData   — Stance 유지 중 구간 진입 시 자동 적용
    //   ② AnimationEventPoint.overlayData — 애니메이션 이벤트 포인트 발행 시 함께 적용
    //
    // 처리 원칙:
    //   - SO 기본값은 항상 살아있음. 이 오버레이는 그 위에 더해지는 델타(Delta)값.
    //   - 구간 종료 후 WorldCameraManager가 blendOut 시간 동안 0으로 자동 보간 복귀.
    //   - 블러 강도 처리는 P2_ObjectMotionBlurController(ICameraEffectReceiver 구현)가 담당.
    //   - 하드코딩 금지. 모든 수치는 이 구조체 필드 또는 BlurEventResponseSO에서 관리.
    // =========================================================================================
    [Serializable]
    public struct CameraEffectOverlayData
    {
        [Header("FOV 오버레이 (기본 FOV에 더해지는 델타값)")]
        [Tooltip("현재 기본 FOV에 순간적으로 더할 오프셋입니다.\n" +
                 "음수 = 줌인 효과 (예: -5), 양수 = 줌아웃 효과.\n" +
                 "0이면 FOV 오버레이 없음.")]
        public float fovDelta;

        [Tooltip("fovDelta 목표값까지 도달하는 블렌드인 시간(초).\n" +
                 "0이면 즉시 스냅. (예: 0.05f = 타격 직후 빠른 줌인)")]
        [Range(0f, 1f)] public float fovBlendIn;

        [Tooltip("이벤트 구간 종료 후 기본 FOV로 복귀하는 블렌드아웃 시간(초).\n" +
                 "(예: 0.2f = 0.2초 동안 서서히 복귀)")]
        [Range(0f, 2f)] public float fovBlendOut;

        [Header("블러 오버레이 (블러 팀원 설정 영역)")]
        [Tooltip("기본 블러 강도에 더할 순간 가산치(0~1).\n" +
                 "P2_ObjectMotionBlurController가 BlurEventResponseSO와 함께 이 값을 참조합니다.\n" +
                 "0이면 블러 오버레이 없음. (예: 0.4f = 강한 타격 블러)")]
        [Range(0f, 1f)] public float blurStrengthDelta;

        [Tooltip("블러가 blurStrengthDelta 최대치에 도달하는 시간(초).\n" +
                 "(예: 0.05f = 타격 직후 빠른 블러 상승)")]
        [Range(0f, 0.5f)] public float blurDuration;

        [Tooltip("블러가 기본값으로 복귀하는 감쇠 시간(초).\n" +
                 "(예: 0.3f = 0.3초 동안 서서히 블러 해제)")]
        [Range(0f, 2f)] public float blurDecayTime;

        [Header("카메라 쉐이크 오버레이")]
        [Tooltip("이 이벤트와 함께 발생시킬 카메라 쉐이크 강도.\n" +
                 "0이면 쉐이크 없음. WorldCameraManager.ApplyCameraShake()로 처리됩니다.")]
        [Range(0f, 2f)] public float shakeIntensity;

        [Tooltip("카메라 쉐이크 지속 시간(초). shakeIntensity > 0일 때만 유효합니다.")]
        [Range(0f, 1f)] public float shakeDuration;
    }

    // =========================================================================================
    // [Phase1 신규] StanceEventPoint
    //
    // CameraStanceSO가 활성화된 동안 특정 시간 구간에 진입/퇴장 시
    // AnimationEventType을 발행하고 CameraEffectOverlayData를 적용하는 타임라인 포인트.
    //
    // 처리 주체: WorldCameraManager.TickStanceEventTimeline() [Phase2 구현 예정]
    //
    // 사용 예시:
    //   락온 Stance에서 0.1초 후 Hit_Confirmed 이벤트 발행 + FOV 줌인 오버레이
    //   → 블러 팀원은 BlurEventResponseSO에서 Hit_Confirmed Pulse 값만 채우면 됨
    //
    // 주의:
    //   - startTime = 0이면 Stance 진입 즉시 발행.
    //   - endTime = 0이면 단발성 이벤트 (구간 없음).
    //   - 동일 Stance 내 여러 StanceEventPoint 중복 등록 가능 (인덱스로 구분).
    // =========================================================================================
    [Serializable]
    public struct StanceEventPoint
    {
        [Tooltip("이 구간이 시작되는 Stance 경과 시간(초).\n" +
                 "0이면 Stance 진입 즉시 발행합니다.")]
        [Range(0f, 30f)] public float startTime;

        [Tooltip("이 구간이 종료되는 Stance 경과 시간(초).\n" +
                 "0이면 단발성 이벤트로 처리됩니다 (구간 없음).")]
        [Range(0f, 30f)] public float endTime;

        [Tooltip("구간 시작 시 CharacterEventManager를 통해 발행할 AnimationEventType.\n" +
                 "P2_ObjectMotionBlurController, PlayerEventManager 등이 수신합니다.")]
        public AnimationEventType onEnterEvent;

        [Tooltip("구간 종료 시 발행할 AnimationEventType.\n" +
                 "종료 이벤트가 필요 없으면 Action_Ended(4)로 설정하세요.")]
        public AnimationEventType onExitEvent;

        [Tooltip("구간 진입 시 WorldCameraManager.PlayOverlayEffect()로 적용할 오버레이 데이터.\n" +
                 "모든 값이 0이면 오버레이 없음. 블러/FOV만 선택적으로 채워도 됩니다.")]
        public CameraEffectOverlayData overlayData;
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
            edgePanReturnTime = 0.8f,
            lockOnInitialFramingOffset = 0f,    // [v4.4] 기본 0 = 진입 시 프레이밍 변경 없음
            lockOnInitialFramingBlendTime = 0.3f   // [v4.4] 0.3s 보간
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
            escapeMinDistance = 3.0f,
            pitchReturnSpeed = 1.0f
        };

        [Header("이동 입력 동기화 (Dynamic Input Modifier)")]
        [Tooltip("유저의 실제 컨트롤러/마우스 이동 입력 크기에 따라 FOV나 거리가 유동적으로 줌인/줌아웃되는 세팅")]
        public DynamicInputModifierData dynamicInputModifier;

        // =========================================================================================

        // =========================================================================================
        [Header("몸통 Yaw 추적 제어 (Body Yaw Tracking)")]
        [Tooltip("캐릭터 몸통 회전을 카메라에 반영하는 비율과 부드러움을 제어합니다.\n" +
                 "공격 루트모션으로 인한 어지러운 카메라 회전을 억제하는 데 사용합니다.")]
        public BodyYawTrackingData bodyYawTracking = new BodyYawTrackingData
        {
            bodyYawFollowWeight = 1.0f,  // 기본값: 100% 즉각 추적 (기존 동작 유지)
            bodyYawFollowSmoothTime = 0f,    // 기본값: 보간 없음
            lockCameraOnActionStart = false, // [v4.4] 기본값: 고정 비활성 (기존 동작 유지)
            lockCameraMouseInfluence = 0.5f   // [v4.4] 기본값: 마우스 50% 허용
        };
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

        // =========================================================================================
        // [Phase1 신규] 구간 이벤트 타임라인 (Stance Event Timeline)
        //
        // 이 Stance가 활성화된 동안 특정 시간 구간에 카메라/블러 이벤트를 발행합니다.
        //
        // 처리 주체: WorldCameraManager.TickStanceEventTimeline() [Phase2에서 구현 예정]
        //
        // 사용 가이드:
        //   - 블러 팀원: overlayData.blurStrengthDelta / blurDuration / blurDecayTime 설정
        //   - 카메라 팀원: overlayData.fovDelta / fovBlendIn / fovBlendOut 설정
        //   - onEnterEvent: Hit_Confirmed(88), Hit_From_Front(84) 등 이벤트 선택
        //   - startTime = 0, endTime = 0: Stance 진입 즉시 단발 이벤트 발행
        //
        // 주의: 이 타임라인은 ChangeCameraStance() 호출 시 WorldCameraManager가
        //        stanceElapsedTime을 0으로 리셋하며 자동으로 재시작됩니다. [Phase2]
        // =========================================================================================
        [Header("구간 이벤트 타임라인 (Stance Event Timeline)")]
        [Tooltip("이 Stance 유지 중 특정 시간 구간에 카메라/블러 이벤트를 발행합니다.\n" +
                 "비어있으면 기존 동작과 동일합니다. (하위 호환 완벽 보장)")]
        public List<StanceEventPoint> stanceEventTimeline = new List<StanceEventPoint>();
    }
}