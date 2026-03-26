using System;
// =============================================================================
// PlayerCamera.cs — 단일 파일 버전 (호환성 유지용)
//
// ⚠️  이 파일은 분리된 partial class 버전과 함께 사용 시 충돌합니다.
//     Unity 프로젝트에서는 아래 4개 파일만 사용하고 이 파일은 삭제하세요:
//
//     PlayerCameraCore.cs     — 변수, 생명주기, 공개 API
//     PlayerCameraRotation.cs — 각도 연산 (HandleRotation, Magnetic, Snapshot)
//     PlayerCameraFollow.cs   — 위치 연산 (DynamicFraming, Collision, Bodycam)
//     PlayerCameraInput.cs    — 입력/엣지패닝/타겟팅/OnGUI 디버그
// =============================================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TDA.World;
using TDA.Cameras;
using TDA.Core.Events;

namespace TDA.Character.Player
{
    /// <summary>
    /// [AAA 숄더뷰 물리 엔진 코어 v3.9 — 공격 중 카메라 꺾임 수정] 
    /// 1. 다이내믹 프레이밍 상속 논리 통합 (Use Dynamic Framing 활성화 시에도 Inherit 동결 100% 보장)
    /// 2. 다이내믹 프레이밍 상속 우선순위 교정 (A/D 입력 씹힘 방어 및 자동 상속)
    /// 3. 180도 래핑(Wrap-around)으로 인한 화면 반전 및 점프 버그 완벽 방어
    /// 4. 마우스 드래깅 현상 제거 및 Zero-Lag 액션 댐핑 오버라이드 구축
    /// 5. [신규] 락온 시 엣지 패닝 데드존 및 뷰포트 이탈 페널티/보정 시스템 통합
    /// </summary>
    public class PlayerCamera : MonoBehaviour
    {
        #region [Variables] 1. References & Base Settings
        [Header("References")]
        [Tooltip("실제 렌더링을 담당하는 Unity Main Camera 객체입니다.")]
        public Camera cameraObject;
        [Tooltip("카메라가 추적할 플레이어 매니저입니다.")]
        public PlayerManager player;
        [Tooltip("상하(Pitch) 회전 및 쉐이크 흔들림이 적용되는 부모 피벗 객체입니다.")]
        [SerializeField] private Transform cameraPivotTransform;

        private PlayerGestureManager playerGestureManager;

        [Header("Camera Distance Settings")]
        [Tooltip("기본 카메라와 캐릭터 사이의 Z축(깊이) 거리입니다. 충돌 시 이 거리보다 가까워집니다.")]
        [SerializeField] private float defaultCameraDistance = 2.5f;

        [Header("Rotation Settings")]
        [Tooltip("마우스 조작 시 화면이 돌아가는 전체 민감도입니다. (권장: 1.0)")]
        [SerializeField] private float mouseSensitivity = 1.0f;
        [Tooltip("좌우(Yaw) 회전 기본 속도입니다.")]
        [SerializeField] private float leftAndRightRotationSpeed = 220f;
        [Tooltip("상하(Pitch) 회전 기본 속도입니다.")]
        [SerializeField] private float upAndDownRotationSpeed = 220f;
        [Tooltip("카메라가 하늘을 볼 수 있는 최소(올려다보기) 각도 한계입니다. (-90도 금지, -30도 권장)")]
        [SerializeField] private float minimumPivot = -30f;
        [Tooltip("카메라가 바닥을 볼 수 있는 최대(내려다보기) 각도 한계입니다. (90도 금지, 60도 권장)")]
        [SerializeField] private float maximumPivot = 60f;

        [Header("Camera Feel Settings")]
        [Tooltip("평상시 마우스 이외의 회전(오토센터링, 락온)이 발생할 때 카메라가 부드럽게 따라가는 완충 시간입니다.")]
        [SerializeField] private float cameraRotationDampTime = 0.05f;

        // 내부 제어 변수 (과거 SmoothDamp의 잔재였던 Target 변수 제거로 최적화됨)
        private float leftAndRightLookAngle;
        private float upAndDownLookAngle;
        #endregion

        #region [Variables] 2. View Restrictions & Auto Centering
        [Header("Shoulder View Restrictions")]
        [Tooltip("카메라가 캐릭터 정면을 기준으로 좌우로 돌아갈 수 있는 최대 시야각입니다. (절대 방어벽)")]
        [SerializeField][Range(10f, 90f)] private float maxViewAngle = 60f;
        [Tooltip("캐릭터가 이동할 때 카메라가 서서히 등 뒤(정면)로 자동 정렬될지 여부입니다.")]
        [SerializeField] private bool enableAutoCenterOnMove = true;
        [Tooltip("캐릭터가 가만히 서 있을 때 시간이 지나면 카메라가 정면으로 정렬될지 여부입니다.")]
        [SerializeField] private bool enableAutoCenterOnIdle = true;
        [Tooltip("가만히 서 있을 때 자동 정렬이 시작되기까지의 대기 시간(초)입니다.")]
        [SerializeField] private float idleAutoCenterDelay = 5.0f;
        [Tooltip("카메라가 정면으로 정렬되는 복귀 속도입니다.")]
        [SerializeField] private float autoCenterSpeed = 2.0f;

        private float noInputTimer = 0f;
        #endregion

        #region [Variables] 3. Debug & State Tracking
        [Header("Debug & Gizmos")]
        [Tooltip("에디터 씬 뷰에서 카메라의 좌우 시야각 한계(Max View Angle)를 빨간 선으로 표시합니다.")]
        [SerializeField] private bool showViewAngleGizmos = true;
        [Tooltip("호출 원인 및 트러블슈팅을 위한 콘솔 로그를 활성화합니다.")]
        public bool showDebugLogs = true;
        [Tooltip("개발 테스트용으로, 카메라가 벽을 뚫고 지나가도록 충돌체를 무시합니다.")]
        public bool bypassCollisionForDebug = false;

        // Caller Analysis Tracking
        private GameState lastTrackedGameState;
        private bool lastTrackedLockOn;
        private int lastTrackedActionState;
        private bool lastTrackedSequencePlaying;
        private string lastTrackedSeqName = "None";
        private string lastTrackedStanceName = "None";
        [HideInInspector] public string lastCallerReason = "System Init (최초 실행)";

        // Collision Debug
        [HideInInspector] public float debugRawZ = 0f;
        [HideInInspector] public float debugTargetZ = 0f;
        [HideInInspector] public float debugActualZ = 0f;
        [HideInInspector] public string lastCollisionObjectName = "None";

        // ── [v3.9] 락온 / 엣지패닝 / 프레이밍 실시간 디버그 상태 ──────────────────
        [Header("Runtime Debug State (Read-Only)")]
        [HideInInspector] public bool dbg_isLockedOn = false;
        [HideInInspector] public bool dbg_isTargetEscaped = false;
        [HideInInspector] public bool dbg_isMagneticActive = false;
        [HideInInspector] public float dbg_magneticPullStrength = 0f;
        [HideInInspector] public bool dbg_isStrafeRight = false;
        [HideInInspector] public bool dbg_yawFrozen = false;
        [HideInInspector] public float dbg_leftAndRightLookAngle = 0f;
        [HideInInspector] public float dbg_finalRenderYaw = 0f;
        [HideInInspector] public float dbg_playerBodyYaw = 0f;
        [HideInInspector] public float dbg_bodyToCamDiff = 0f;  // 카메라-몸통 사이 각도
        [HideInInspector] public float dbg_currentFramingOffsetX = 0f;
        [HideInInspector] public float dbg_currentFramingYawOffset = 0f;
        [HideInInspector] public float dbg_trackingWeight = 0f;
        [HideInInspector] public float dbg_noInputTimer = 0f;
        [HideInInspector] public Vector2 dbg_mouseViewport = Vector2.zero; // 0~1 화면 내 커서 위치
        [HideInInspector] public bool dbg_mouseAtEdgeX = false;
        [HideInInspector] public bool dbg_mouseAtEdgeY = false;
        [HideInInspector] public float dbg_edgeThreshold = 0.08f;
        [HideInInspector] public Vector3 dbg_targetViewportPos = Vector3.zero; // 타겟의 화면 내 뷰포트 좌표
        [HideInInspector] public string dbg_lockOnState = "UNLOCKED";   // 상태 문자열
        [HideInInspector] public string dbg_framingState = "IDLE";
        [HideInInspector] public string dbg_magneticState = "INACTIVE";
        [HideInInspector] public float dbg_targetDist = 0f;
        [HideInInspector] public float dbg_softCorrectionSpeed = 0f;
        // [v4.0] 엣지패닝 전용 디버그
        [HideInInspector] public bool dbg_edgePanActive = false;
        [HideInInspector] public float dbg_edgePanReturnProgress = 0f;  // 0~1 복귀 진행도
        [HideInInspector] public float dbg_smoothedEdgeInputX = 0f;
        [HideInInspector] public float dbg_smoothedEdgeInputY = 0f;
        // ─────────────────────────────────────────────────────────────────────────
        #endregion

        #region [Variables] 4. Frame Decoupling & Bodycam
        [Header("Bodycam Frame Decoupling")]
        [Tooltip("바디캠 특유의 프레임 끊김 현상을 연출하기 위한 목표 프레임(FPS)입니다.")]
        [SerializeField][Range(1f, 60f)] private float targetCameraFPS = 24f;
        [Tooltip("프레임 디커플링 연출을 활성화합니다. (끄면 게임 프레임과 완벽 동기화)")]
        [SerializeField] private bool enableFrameDecoupling = true;
        private float frameTimer = 0f;

        private Vector3 continuousPosition;
        private Quaternion continuousRotation;
        private Vector3 continuousCameraLocalPos;
        private Vector3 continuousPivotLocalPos;

        private Vector3 steppedPosition;
        private Quaternion steppedRotation;
        private Vector3 steppedCameraLocalPos;
        private Vector3 steppedPivotLocalPos;
        private bool isFirstFrame = true;

        [Header("Handheld & Bodycam Effect")]
        [Tooltip("스태미나가 바닥났을 때 숨결(수전증)이 거칠어지는 배수입니다.")]
        [SerializeField] private float staminaExhaustionMultiplier = 2.5f;
        [Tooltip("뛸 때 화면이 쿵쾅거리는 상하 진동 폭입니다.")]
        [SerializeField] private float movementBobbingAmount = 1.2f;
        #endregion

        #region [Variables] 5. Collision & Focus Settings
        [Header("Soft Magnetic Lock")]
        [Tooltip("락온 시 화면 정중앙에서 타겟이 이 반경 안에 있으면 카메라가 억지로 따라가지 않는 여유 공간(데드존)입니다.")]
        [SerializeField] private Vector2 deadzoneRadius = new Vector2(0.25f, 0.25f);
        [Tooltip("데드존을 벗어났을 때 타겟을 향해 자석처럼 끌어당기는 힘의 속도입니다.")]
        [SerializeField] private float magneticPullSpeed = 5.0f;

        [Header("Collision & Follow (Anti-Clipping)")]
        [Tooltip("카메라가 목표 위치로 이동할 때의 부드러운 감속 텐션입니다.")]
        [SerializeField] private float cameraSmoothSpeed = 0.1f;
        [Tooltip("카메라 렌즈의 충돌체 크기(반경)입니다. 벽에 파묻히지 않게 보호합니다.")]
        [SerializeField] private float cameraCollisionRadius = 0.25f;
        [Tooltip("벽에 완전히 밀착했을 때 카메라와 캐릭터 사이의 허용된 최소 간격입니다.")]
        [SerializeField] private float minimumCollisionOffset = 0.2f;
        [Tooltip("벽에 의해 카메라가 앞으로 밀릴 때, 캐릭터 머리를 뚫지 않게 옆으로 비켜주는 회피 공간 오프셋입니다.")]
        [SerializeField] private Vector2 bodyAvoidanceOffset = new Vector2(0.4f, 0.2f);
        [Tooltip("카메라가 이 수치보다 더 앞으로 밀려나면 머리 회피 기동을 시작합니다.")]
        [SerializeField] private float avoidanceThreshold = 1.2f;
        [Tooltip("카메라가 충돌을 감지할 환경 레이어입니다. (벽, 바닥)")]
        [SerializeField] private LayerMask collideWithLayers;
        [Tooltip("카메라가 무시하지 않고 피해야 할 캐릭터 레이어입니다.")]
        [SerializeField] private LayerMask characterCollisionLayers;

        private float cameraHorizontalInput;
        public float CameraHorizontalInput => cameraHorizontalInput;
        private float cameraVerticalInput;
        private Vector3 cameraVelocity;

        private float defaultFOV = 60f;
        private float targetFOV;
        private float currentLerpSpeed = 5f;
        private float targetZPosition;

        private bool isContextualMode = false;
        private Transform currentFocusTarget;
        private Vector3 currentOffset;
        private float bodycamWeight = 0.1f;
        private float shakeIntensity = 0f;
        private float shakeDuration = 0f;
        private float shakeMaxDuration = 0.3f;   // [v3.9] Perlin Shake 감쇠용 최대 지속시간
        private float shakeTimeOffset = 0f;       // [v3.9] Perlin Shake 시드

        // ── [Implementation Spec] 신규 변수 ─────────────────────────────────

        // [요구사항 1] Hysteresis 상태 추적
        private bool isEscapedStable = false;   // 단순 threshold 대신 Hysteresis가 적용된 안정적 이탈 상태
        private float escapeHoldTimer = 0f;      // 타겟 재진입 후 escaped 유지 카운트다운 타이머

        // [요구사항 2] 다중 타겟 포커싱 — Yaw 방향 보정 방식으로 재설계 (v4.4)
        // 기존: desiredPos(카메라 위치)를 finalCenter(적 중간 위치)로 SmoothDamp
        //       → 카메라가 플레이어에서 떨어지고 매 프레임 리셋되며 충돌 → 4.3m/s 진동
        // 신규: leftAndRightLookAngle(Yaw)만 finalCenter 방향으로 SmoothDamp
        //       → 위치는 건드리지 않고 바라보는 방향만 조정 → 루프 없음
        private Vector3 focusPivotVelocity = Vector3.zero; // 레거시 (더 이상 desiredPos 이동에 쓰지 않음)
        private float focusYawVelocity = 0f;           // [v4.4] Yaw 보정 SmoothDamp velocity

        // ── [v4.1] MagneticSoftLock 진동 방지용 변수 ─────────────────────────────
        // 원인 1: pullStrength가 데드존 경계 근처에서 0↔0.03으로 진동 → Hysteresis로 안정화
        // 원인 3: Lerp만으로는 감쇠 불가 → SmoothDamp velocity로 점진적 수렴
        private float magneticYawVelocity = 0f;  // Yaw SmoothDamp velocity
        private float magneticPitchVelocity = 0f;  // Pitch SmoothDamp velocity
        private bool isMagneticActive = false; // Hysteresis 안정 상태 플래그
        // pullStrength가 이 값을 넘으면 자석 ON (진입 threshold)
        private const float kMagEnterThreshold = 0.04f;
        // pullStrength가 이 값 아래로 내려가면 자석 OFF (탈출 threshold, 히스테리시스 구간)
        private const float kMagExitThreshold = 0.015f;
        // ─────────────────────────────────────────────────────────────────────────

        // ── [v4.0] 엣지 패닝 전용 상태 변수 (4가지 문제 대응) ────────────────
        // 문제 1+2: 엣지 발동 여부를 명확히 추적 → 비발동 시 입력 완전 차단
        private bool isEdgePanActive = false;  // 현재 엣지 패닝 발동 중 여부
        private bool isEdgeReturning = false;  // 엣지 해제 후 복귀 진행 중 플래그 (HandleRotation 협조용)
        // ── [v4.2 버그 수정] 게임 시작 시 복귀 오작동 방지 ─────────────────────────────
        // 한 번이라도 엣지에 진입했다가 나온 경우에만 복귀를 허용.
        // 초기값 false → 엣지 진입(false→true) 시 true로 설정.
        // 이 플래그 없이는 게임 시작 직후 edgePanReturnTimer(0) < returnTime(0.8s) 조건이
        // 참이 되어 isEdgeReturning=true가 되고, edgePanReturnYaw/Pitch=0을 목표로
        // 카메라를 당겨서 걸을 때 위아래 떨림이 발생함.
        private bool hadEdgePanEverActive = false;

        // ── [버그수정] 가상 커서 위치 추적 (CursorLockMode.Locked 대응) ─────────────
        // 문제: CursorLockMode.Locked 상태에서 Input.mousePosition은 화면 중앙 고정값을 반환.
        //       게임뷰 포커스 시 dbg_mouseViewport가 항상 (0.5, 0.5)로 고정되는 원인.
        // 해결: Input.GetAxis("Mouse X/Y") 델타를 매 프레임 누적하여
        //       커서 잠금 여부와 무관하게 실제 마우스 이동을 반영하는 가상 위치를 계산.
        //       커서 잠금 해제 상태(Esc)에서는 실제 Input.mousePosition을 사용.
        private Vector2 virtualCursorPos = new Vector2(0.5f, 0.5f); // 0~1 뷰포트 좌표 (초기: 중앙)
        private float virtualCursorSensX = 0.002f; // GetAxis 누적 감도 X (픽셀→뷰포트 비율)
        private float virtualCursorSensY = 0.002f; // GetAxis 누적 감도 Y
        // ─────────────────────────────────────────────────────────────────────────


        // ── [v4.4 신규] 락온 진입 초기 프레이밍 ─────────────────────────────────────
        private bool isLockOnInitialFramingActive = false; // 초기 프레이밍 진행 중 여부
        private float lockOnInitialFramingTimer = 0f;    // 진행 타이머

        // ── [v4.4 신규] 시퀀스 종료 후 프레이밍 복원 (restorePreviousFraming) ──────
        // WorldCameraManager.PlayCameraSequence()에서 스냅샷 저장,
        // CameraSequenceRoutine 종료 시 SetFramingOffset()으로 복원됩니다.
        private bool isPendingFramingRestore = false;  // 복원 대기 중 여부
        private float pendingFramingTarget = 0f;     // 복원 목표값
        private float pendingFramingBlendTime = 0.3f;   // 복원 보간 시간
        private float pendingFramingTimer = 0f;     // 복원 진행 타이머
        private float pendingFramingStartValue = 0f;     // 복원 시작값 (보간 기준)

        // 문제 3: 엣지 해제 후 카메라가 원래 시점으로 부드럽게 복귀
        private float edgePanReturnTimer = 0f;     // 복귀 진행 타이머 (0=완료)
        private float edgePanReturnYaw = 0f;     // 복귀 목표 Yaw (엣지 진입 시 스냅샷)
        private float edgePanReturnPitch = 0f;     // 복귀 목표 Pitch
        private float edgePanReturnVelYaw = 0f;     // SmoothDamp velocity (Yaw)
        private float edgePanReturnVelPitch = 0f;     // SmoothDamp velocity (Pitch)

        // 문제 4: 엣지 발동 중 입력을 부드럽게 보간 (갑작스러운 시선 이동 방지)
        private float smoothedEdgeInputX = 0f;     // 보간된 X 입력
        private float smoothedEdgeInputY = 0f;     // 보간된 Y 입력
        private float edgePanInputVelX = 0f;     // SmoothDamp velocity X
        private float edgePanInputVelY = 0f;     // SmoothDamp velocity Y
        // ─────────────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────────────
        #endregion

        #region [Variables] 6. Targeting System
        [Header("Targeting & Search")]
        [Tooltip("락온 대상을 찾을 때 검색하는 구체(Sphere)의 최대 반경입니다.")]
        [SerializeField] private float lockOnRadius = 20f;
        [Tooltip("화면 중앙을 기준으로 몇 도 이내의 적을 타겟으로 잡을지 결정하는 탐색 화각입니다.")]
        [SerializeField] private float maximumViewableAngle = 50f;

        [HideInInspector] public CharacterManager nearestLockOnTarget;
        [HideInInspector] public CharacterManager leftLockOnTarget;
        [HideInInspector] public CharacterManager rightLockOnTarget;
        private List<CharacterManager> availableTargets = new List<CharacterManager>();
        #endregion

        #region [Variables] 7. Dynamic Framing & Suspension Core
        private CameraStancePresetSO currentStance;

        private float currentFramingOffsetX = 0f;
        private float framingVelocity = 0f;
        private float lastTargetFraming = 0f;

        private float currentFramingYawOffset = 0f;
        private float framingYawVelocity = 0f;
        private float lastTargetFramingYaw = 0f;

        private float smoothedPlayerY;
        private float playerYVelocity;

        // ── [v4.4 신규] 몸통 Yaw 부드러운 추적용 변수 ───────────────────────────────
        private float smoothedPlayerBodyYaw = 0f;   // SmoothDamp 적용된 playerBodyYaw
        private float smoothedPlayerBodyYawVelocity = 0f;   // SmoothDamp velocity ref

        // ── [v4.4 신규] 공격 시작 순간 카메라 각도 스냅샷 ───────────────────────────
        // isPerformingAction false→true 전환 시 leftAndRightLookAngle을 저장.
        // 공격 중 HandleRotation이 이 값으로 카메라를 고정하여
        // 루트모션 회전/FocusPivot/Magnetic 등 모든 외부 개입을 차단합니다.
        private float actionStartYaw = 0f;    // 공격 시작 시 Yaw 스냅샷
        private float actionStartPitch = 0f;    // 공격 시작 시 Pitch 스냅샷
        private bool wasPerformingAction = false; // 이전 프레임 상태 (전환 감지)

        private float currentDynamicHeight = 0f;
        private float dynamicHeightVelocity = 0f;

        private float collisionZVelocity;
        private float collisionXVelocity;
        private float collisionYVelocity;

        private Vector3 dbgMassCenter;
        private Vector3 dbgDesiredPos;
        #endregion

        // =====================================================================
        // [v3.9] CurrentActionLayerNormTime
        // WorldCameraManager.BlendToStanceRoutine이 Seq 내부 타이머 대신
        // 실제 애니메이션 진행도를 기준으로 TrackingWeight를 계산할 수 있도록
        // 외부에 공개하는 프로퍼티입니다.
        // 단발성 공격 애니메이션이므로 % 1f 연산 제거 — 재생 완료 시 1.0 고정.
        // =====================================================================
        public float CurrentActionLayerNormTime
        {
            get
            {
                if (player == null || player.animator == null || player.animator.layerCount <= 1)
                    return 0f;

                AnimatorStateInfo stateInfo = player.animator.IsInTransition(1)
                    ? player.animator.GetNextAnimatorStateInfo(1)
                    : player.animator.GetCurrentAnimatorStateInfo(1);

                return Mathf.Clamp01(stateInfo.normalizedTime);
            }
        }

        #region Unity Lifecycle (Awake, Start, LateUpdate)
        private void Awake()
        {
            if (cameraObject != null)
            {
                defaultFOV = cameraObject.fieldOfView;
                targetFOV = defaultFOV;
                targetZPosition = -Mathf.Abs(defaultCameraDistance);
                cameraObject.nearClipPlane = 0.01f;
            }

            // [v3.9] Perlin Shake 시드 무작위화 — 매 실행마다 다른 진동 패턴
            shakeTimeOffset = UnityEngine.Random.value * 100f;
        }

        private void Start()
        {
            DontDestroyOnLoad(gameObject);
            if (WorldCameraManager.Instance != null)
            {
                WorldCameraManager.Instance.SetLocalCamera(this);
            }

            if (player != null)
            {
                smoothedPlayerY = player.transform.position.y;
                smoothedPlayerBodyYaw = player.transform.eulerAngles.y; // [v4.4] 초기값 스냅
            }

            continuousPosition = transform.position;
            continuousRotation = transform.rotation;
            if (cameraObject != null) continuousCameraLocalPos = cameraObject.transform.localPosition;
            if (cameraPivotTransform != null) continuousPivotLocalPos = cameraPivotTransform.localPosition;

            steppedPosition = continuousPosition;
            steppedRotation = continuousRotation;
            steppedCameraLocalPos = continuousCameraLocalPos;
            steppedPivotLocalPos = continuousPivotLocalPos;

            leftAndRightLookAngle = transform.eulerAngles.y;
            float pitch = transform.eulerAngles.x;
            if (pitch > 180) pitch -= 360;
            upAndDownLookAngle = pitch;

            if (WorldGameStateManager.Instance != null) lastTrackedGameState = WorldGameStateManager.Instance.currentState;
        }

        public void UpdateStanceData(CameraStancePresetSO newStance)
        {
            currentStance = newStance;
            if (cameraObject != null && WorldCameraManager.Instance != null)
            {
                cameraObject.fieldOfView = WorldCameraManager.Instance.currentFOV;
            }
        }

        private void LateUpdate()
        {
            if (player == null) return;

            // 1. 프레임 디커플링 연산 전 복구
            if (enableFrameDecoupling && !isFirstFrame)
            {
                transform.position = continuousPosition;
                transform.rotation = continuousRotation;
                if (cameraObject != null) cameraObject.transform.localPosition = continuousCameraLocalPos;
                if (cameraPivotTransform != null) cameraPivotTransform.localPosition = continuousPivotLocalPos;
            }

            // 2. 핵심 물리 연산 5단계 (v3.9 / v4.0)
            // [v3.9 Debug] 락온 상태 매 프레임 갱신
            dbg_isLockedOn = player.playerNetworkManager != null && player.playerNetworkManager.isLockedOn.Value;

            // [v4.0 Debug] 엣지패닝 상태 매 프레임 갱신
            dbg_edgePanActive = isEdgePanActive;
            dbg_smoothedEdgeInputX = smoothedEdgeInputX;
            dbg_smoothedEdgeInputY = smoothedEdgeInputY;
            if (WorldCameraManager.Instance?.currentStanceSO != null)
            {
                float epRT = WorldCameraManager.Instance.currentStanceSO.edgePanningData.edgePanReturnTime;
                dbg_edgePanReturnProgress = epRT > 0f ? Mathf.Clamp01(edgePanReturnTimer / epRT) : 1f;
            }
            if (!dbg_isLockedOn)
            {
                dbg_lockOnState = "UNLOCKED";
                dbg_isTargetEscaped = false;
                dbg_isMagneticActive = false;
                dbg_magneticPullStrength = 0f;
                dbg_magneticState = "INACTIVE";
            }

            UpdateVirtualCursor();
            HandleFollowTarget();
            Quaternion baseRotation = HandleRotation();
            baseRotation = HandleMagneticSoftLock(baseRotation);
            baseRotation = ApplyDynamicYawAndClamp(baseRotation);
            HandleCollision();
            ApplyFinalTransform(baseRotation);

            // 3. 디버그 로그 및 호출 원인 스마트 분석
            if (showDebugLogs && WorldCameraManager.Instance != null)
            {
                GameState currentGameState = WorldGameStateManager.Instance != null ? WorldGameStateManager.Instance.currentState : GameState.Normal;
                bool currentLockOn = player.playerNetworkManager != null && player.playerNetworkManager.isLockedOn.Value;
                int currentActionState = player.animator != null ? player.animator.GetInteger(AnimatorParameterHash.ActionState) : 0;
                bool currentSeqPlaying = WorldCameraManager.Instance.IsSequencePlaying;

                CameraSequencePresetSO currentSeqSO = WorldCameraManager.Instance.currentSequenceSO;
                CameraStancePresetSO currentStanceSO = WorldCameraManager.Instance.currentStanceSO;

                string currentSeqName = currentSeqSO != null ? currentSeqSO.name : "None";
                string currentStanceName = currentStanceSO != null ? currentStanceSO.name : "None";

                bool soChanged = (currentSeqName != lastTrackedSeqName) || (currentStanceName != lastTrackedStanceName);
                bool stateChanged = (currentGameState != lastTrackedGameState) || (currentLockOn != lastTrackedLockOn) || (currentActionState != lastTrackedActionState);

                if (stateChanged || soChanged || (currentSeqPlaying && !lastTrackedSequencePlaying))
                {
                    string newReason = "";
                    if (currentActionState != lastTrackedActionState && currentActionState != 0) newReason = $"액션 트리거 (ID: {currentActionState})";
                    else if (currentLockOn != lastTrackedLockOn) newReason = currentLockOn ? "락온 활성화" : "락온 해제";
                    else if (currentGameState != lastTrackedGameState) newReason = $"월드 상태 변경 ➔ {currentGameState}";
                    else if (currentSeqPlaying && !lastTrackedSequencePlaying) newReason = "시퀀스 스크립트 호출";
                    else if (soChanged) newReason = currentSeqPlaying ? "시퀀스 스텝 진행" : "스탠스 즉각 적용";

                    if (!string.IsNullOrEmpty(newReason)) lastCallerReason = newReason;

                    lastTrackedGameState = currentGameState;
                    lastTrackedLockOn = currentLockOn;
                    lastTrackedActionState = currentActionState;
                    lastTrackedSeqName = currentSeqName;
                    lastTrackedStanceName = currentStanceName;
                }
                lastTrackedSequencePlaying = currentSeqPlaying;
            }

            // 4. 프레임 디커플링 (Stepped Update)
            if (enableFrameDecoupling)
            {
                continuousPosition = transform.position;
                continuousRotation = transform.rotation;
                if (cameraObject != null) continuousCameraLocalPos = cameraObject.transform.localPosition;
                if (cameraPivotTransform != null) continuousPivotLocalPos = cameraPivotTransform.localPosition;

                frameTimer += Time.deltaTime;
                float frameInterval = 1f / targetCameraFPS;

                if (frameTimer >= frameInterval || isFirstFrame)
                {
                    frameTimer %= frameInterval;
                    steppedPosition = continuousPosition;
                    steppedRotation = continuousRotation;
                    steppedCameraLocalPos = continuousCameraLocalPos;
                    steppedPivotLocalPos = continuousPivotLocalPos;
                    isFirstFrame = false;
                }

                transform.position = steppedPosition;
                transform.rotation = steppedRotation;
                if (cameraObject != null) cameraObject.transform.localPosition = steppedCameraLocalPos;
                if (cameraPivotTransform != null) cameraPivotTransform.localPosition = steppedPivotLocalPos;
            }
            else isFirstFrame = true;
        }
        #endregion

        #region Input & Interaction
        /// <summary>
        /// 중앙 라우터(PlayerManager)를 통해 들어온 순수 마우스/스틱 조작값을 받아 
        /// 엣지 패닝 및 페널티 보정 시스템을 연산한 후 적용합니다.
        /// </summary>
        public void OnCameraInputReceived(float x, float y)
        {
            float finalMouseX = x;
            float finalMouseY = y;

            if (WorldCameraManager.Instance != null && WorldCameraManager.Instance.currentStanceSO != null)
            {
                CameraStancePresetSO stance = WorldCameraManager.Instance.currentStanceSO;
                bool isLockedOn = player != null && player.playerNetworkManager != null && player.playerNetworkManager.isLockedOn.Value;

                if (isLockedOn)
                {
                    Vector3 targetPos = Vector3.zero;

                    // =========================================================================================
                    // [Implementation Spec 수정] 요구사항 1 — Hysteresis 이탈 판정
                    // 단일 threshold 대신 진입(escThr)/탈출(relaxThr) 이중 threshold로
                    // 경계 펄럭임(oscillation)을 방지합니다.
                    // =========================================================================================
                    if (stance.lockOnPenaltyData.enableTargetEscapePenalty
                        && player.playerCombatManager != null
                        && player.playerCombatManager.currentTarget != null)
                    {
                        targetPos = player.playerCombatManager.currentTarget
                                        .characterCombatManager.lockOnTransform.position;

                        Vector3 viewportPos = cameraObject.WorldToViewportPoint(targetPos);
                        float escThr = stance.lockOnPenaltyData.targetEscapeViewportThreshold;
                        float relaxThr = escThr * 0.5f; // 탈출 threshold — 진입보다 작아 Hysteresis 구간 형성

                        // ─── 이탈 판정 ────────────────────────────────────────────────────
                        bool rawEscaped = viewportPos.z < 0f
                            || viewportPos.x < escThr || viewportPos.x > (1f - escThr)
                            || viewportPos.y < escThr || viewportPos.y > (1f - escThr);

                        if (rawEscaped)
                        {
                            // 이탈 → 즉시 escaped 상태 진입 + 타이머 재설정
                            isEscapedStable = true;
                            escapeHoldTimer = Mathf.Max(stance.lockOnPenaltyData.escapePersistTime, 0f);
                        }
                        else if (isEscapedStable)
                        {
                            // 타겟이 화면 안 relaxThr 구간으로 복귀했을 때만 타이머 감산
                            bool inRelaxZone = viewportPos.x > relaxThr && viewportPos.x < (1f - relaxThr)
                                            && viewportPos.y > relaxThr && viewportPos.y < (1f - relaxThr);
                            if (inRelaxZone)
                                escapeHoldTimer -= Time.deltaTime;
                            if (escapeHoldTimer <= 0f)
                                isEscapedStable = false;
                        }
                        // ─────────────────────────────────────────────────────────────────
                    }
                    else
                    {
                        // penalty 비활성화 시 항상 escaped 아님
                        isEscapedStable = false;
                        escapeHoldTimer = 0f;
                    }

                    // ─── 페널티 상황: 소프트/하드 보정 ───────────────────────────────────
                    if (isEscapedStable && targetPos != Vector3.zero)
                    {
                        // [버그수정] 가상 커서 사용 (CursorLock 대응)
                        Vector2 mouseViewport = virtualCursorPos;
                        float edgeThreshold = stance.edgePanningData.edgePanThreshold;

                        // 유저가 커서를 화면 엣지로 가져가 능동 복귀를 시도하는지 감지
                        bool isTryingToRecover = mouseViewport.x <= edgeThreshold
                                              || mouseViewport.x >= (1f - edgeThreshold)
                                              || mouseViewport.y <= edgeThreshold
                                              || mouseViewport.y >= (1f - edgeThreshold);

                        if (isTryingToRecover)
                        {
                            Vector3 dirToTarget = (targetPos - transform.position).normalized;
                            Quaternion lookRot = Quaternion.LookRotation(dirToTarget);
                            float targetYaw = lookRot.eulerAngles.y;
                            float targetPitch = lookRot.eulerAngles.x;
                            if (targetPitch > 180f) targetPitch -= 360f;

                            if (stance.lockOnPenaltyData.useHardCorrection)
                            {
                                // 하드 보정: 빠른 일직선 정렬 (속도 15 고정)
                                leftAndRightLookAngle = Mathf.LerpAngle(leftAndRightLookAngle, targetYaw, Time.deltaTime * 15f);
                                upAndDownLookAngle = Mathf.Lerp(upAndDownLookAngle, targetPitch, Time.deltaTime * 15f);
                            }
                            else
                            {
                                // [수정] 소프트 보정 속도 → SO softRecoverySpeed 파라미터 사용 (하드코딩 5f 제거)
                                float dist = Vector3.Distance(transform.position, targetPos);
                                float speedMult = (stance.lockOnPenaltyData.softCorrectionDistanceCurve != null
                                                   && stance.lockOnPenaltyData.softCorrectionDistanceCurve.length > 0)
                                                  ? stance.lockOnPenaltyData.softCorrectionDistanceCurve.Evaluate(dist)
                                                  : 1.0f;

                                // softRecoverySpeed: SO 파라미터 (기본 2.0)
                                float baseSpeed = Mathf.Max(stance.lockOnPenaltyData.softRecoverySpeed, 0.1f);

                                leftAndRightLookAngle = Mathf.LerpAngle(leftAndRightLookAngle, targetYaw, Time.deltaTime * baseSpeed * speedMult);
                                upAndDownLookAngle = Mathf.Lerp(upAndDownLookAngle, targetPitch, Time.deltaTime * baseSpeed * speedMult);
                            }

                            // 보정 중 유저 마우스 델타 무시
                            finalMouseX = 0f;
                            finalMouseY = 0f;
                        }
                        // 커서가 중앙 영역 → 시선 자율권 보장 (마우스 입력 그대로 반영)
                    }
                    // =========================================================================================
                    // [v4.0 완전 재설계] 락온 엣지 패닝 — 4가지 문제 동시 대응
                    //
                    // 문제 1+2: 마우스가 엣지 구역에 있을 때만 카메라 이동. 비발동 시 입력 완전 차단.
                    // 문제 3:   엣지 구역 벗어났을 때 카메라가 진입 전 시점으로 부드럽게 복귀.
                    // 문제 4:   엣지 발동 중 입력을 SmoothDamp로 보간하여 시선 이동을 완만하게.
                    //
                    // [v4.4 문제 1 수정] useEdgePanning=false인 Stance에서도 락온 시 입력을 완전 차단.
                    // 기존: useEdgePanning=false → else if 블록 자체가 실행 안 됨 → 마우스 입력 그대로 통과.
                    // 수정: 락온 상태이면 useEdgePanning=false여도 기본적으로 입력 차단.
                    //       useEdgePanning=true인 경우에만 엣지 구역에서 선택적 회전 허용.
                    // =========================================================================================
                    else if (!stance.edgePanningData.useEdgePanning)
                    {
                        // useEdgePanning 꺼짐 + 락온 상태 → 마우스 입력 완전 차단
                        // (락온 중 카메라는 Magnetic/AutoCenter가 제어하므로 유저 입력 불필요)
                        finalMouseX = 0f;
                        finalMouseY = 0f;
                    }
                    else if (stance.edgePanningData.useEdgePanning)
                    {
                        // [v4.4] 제스처 입력(IsDragging) 중에는 마우스 커서 위치를 화면 중앙으로 고정.
                        bool isGestureDragging = playerGestureManager != null && playerGestureManager.IsDragging;
                        // [버그수정] 커서 잠금 상태에서도 실제 마우스 이동 반영 — 가상 커서 사용
                        Vector2 mouseViewport = isGestureDragging
                            ? new Vector2(0.5f, 0.5f)
                            : virtualCursorPos;
                        float edgeThr = stance.edgePanningData.edgePanThreshold;
                        float smoothTime = Mathf.Max(stance.edgePanningData.edgePanSmoothTime, 0.02f);
                        float returnTime = Mathf.Max(stance.edgePanningData.edgePanReturnTime, 0.1f);

                        // ── 엣지 발동 여부 판정 ─────────────────────────────────────────────
                        // [v4.2] 마우스 화면 안/밖 처리 원칙:
                        //
                        //  ① 화면 안 일반구역 (0+thr ~ 1-thr):
                        //     → 엣지 비발동, 복귀 허용
                        //
                        //  ② 화면 안 엣지구역 (0~thr 또는 1-thr~1):
                        //     → 엣지 발동 (ACTIVE)
                        //
                        //  ③ 화면 밖 (x<0, x>1, y<0, y>1):
                        //     → 해당 축 방향 엣지가 발동된 것으로 간주 (ACTIVE 유지)
                        //     → "마우스를 더 세게 밀었을 뿐" = 여전히 이동 의도 있음
                        //     → 이 경우 복귀하면 안 됨!
                        //
                        //  단, 게임 시작 직후(hadEdgePanEverActive=false)에 화면 밖인 경우는
                        //  엣지 진입 이력이 없으므로 활성화하지 않음 (초기 오작동 방지)

                        bool mouseOutLeft = mouseViewport.x < 0f;
                        bool mouseOutRight = mouseViewport.x > 1f;
                        bool mouseOutDown = mouseViewport.y < 0f;
                        bool mouseOutUp = mouseViewport.y > 1f;
                        bool mouseOutside = mouseOutLeft || mouseOutRight || mouseOutDown || mouseOutUp;

                        // 화면 밖일 때: 이전에 엣지 진입 이력이 있으면 해당 방향 엣지 유지
                        // (초기 오작동 방지: hadEdgePanEverActive가 false이면 화면 밖도 비활성)
                        bool isAtEdgeX, isAtEdgeY;
                        if (mouseOutside && hadEdgePanEverActive)
                        {
                            // 화면 밖 = 해당 방향 엣지 발동 유지
                            isAtEdgeX = mouseOutLeft || mouseOutRight;
                            isAtEdgeY = mouseOutDown || mouseOutUp;
                            // 화면 밖인 경우 X/Y 중 실제로 넘어간 축만 true
                            // (예: x=1.08 → isAtEdgeX=true, isAtEdgeY=false)
                        }
                        else
                        {
                            // 화면 안 또는 초기 상태: 일반 엣지 범위 체크
                            bool mouseInScreen = !mouseOutside;
                            isAtEdgeX = mouseInScreen && (mouseViewport.x <= edgeThr || mouseViewport.x >= (1f - edgeThr));
                            isAtEdgeY = mouseInScreen && (mouseViewport.y <= edgeThr || mouseViewport.y >= (1f - edgeThr));
                        }
                        bool newEdgeActive = isAtEdgeX || isAtEdgeY;

                        // ── 엣지 진입 직전 시점 스냅샷 ─────────────────────────────────────
                        // isEdgePanActive false → true 전환 순간 한 번만 저장
                        if (newEdgeActive && !isEdgePanActive)
                        {
                            edgePanReturnYaw = leftAndRightLookAngle;
                            edgePanReturnPitch = upAndDownLookAngle;
                            edgePanReturnTimer = 0f;
                            edgePanReturnVelYaw = 0f;
                            edgePanReturnVelPitch = 0f;
                            isEdgeReturning = false; // 엣지 재진입 시 복귀 취소
                            hadEdgePanEverActive = true;  // 최초 진입 기록 → 이후 복귀 허용
                        }

                        isEdgePanActive = newEdgeActive;

                        if (isEdgePanActive)
                        {
                            // ── [문제 2 핵심 수정] 엣지 발동 중 — 입력 허용 ────────────────
                            // X/Y 축 분리: 해당 축 엣지일 때만 입력 통과
                            float rawX = isAtEdgeX ? x : 0f;
                            float rawY = isAtEdgeY ? y : 0f;

                            // [수정B] deadzone을 매우 작게 고정 (0.05)
                            // 기존 lockOnInputDeadzone(기본 0.5)이 너무 커서
                            // 천천히 움직이는 마우스 입력을 전부 차단했던 문제 해결
                            const float kEdgeInputDeadzone = 0.02f;
                            if (Mathf.Abs(rawX) < kEdgeInputDeadzone) rawX = 0f;
                            if (Mathf.Abs(rawY) < kEdgeInputDeadzone) rawY = 0f;

                            // [문제 4] 입력을 SmoothDamp로 완만하게 보간
                            // 갑작스러운 마우스 스윕에도 카메라가 천천히 따라감
                            smoothedEdgeInputX = Mathf.SmoothDamp(
                                smoothedEdgeInputX, rawX, ref edgePanInputVelX, smoothTime, 999f);
                            smoothedEdgeInputY = Mathf.SmoothDamp(
                                smoothedEdgeInputY, rawY, ref edgePanInputVelY, smoothTime, 999f);

                            finalMouseX = smoothedEdgeInputX;
                            finalMouseY = smoothedEdgeInputY;

                            // 복귀 타이머/플래그 리셋 (발동 중에는 복귀 안 함)
                            edgePanReturnTimer = 0f;
                            isEdgeReturning = false;
                        }
                        else
                        {
                            // ── [문제 1] 엣지 비발동 — 입력 완전 차단 ──────────────────────
                            finalMouseX = 0f;
                            finalMouseY = 0f;

                            // 보간 입력도 0으로 수렴
                            smoothedEdgeInputX = Mathf.SmoothDamp(
                                smoothedEdgeInputX, 0f, ref edgePanInputVelX, smoothTime * 0.5f, 999f);
                            smoothedEdgeInputY = Mathf.SmoothDamp(
                                smoothedEdgeInputY, 0f, ref edgePanInputVelY, smoothTime * 0.5f, 999f);

                            // ── [문제 3] 복귀 — HandleRotation()에 위임 ──────────────────────
                            // hadEdgePanEverActive: 한 번이라도 엣지에 진입한 적이 있어야만
                            // 복귀 로직을 활성화합니다.
                            // 게임 시작 직후에는 이 플래그가 false이므로 복귀가 발동되지 않습니다.
                            if (hadEdgePanEverActive && edgePanReturnTimer < returnTime)
                            {
                                edgePanReturnTimer += Time.deltaTime;
                                isEdgeReturning = true;
                            }
                            else
                            {
                                isEdgeReturning = false;
                            }
                        }
                    }
                }
            }

            cameraHorizontalInput = finalMouseX;
            cameraVerticalInput = finalMouseY;

            // ── [버그수정] 엣지패닝 마우스 위치 기록 — 가상 커서 사용 ─────────────
            // CursorLockMode.Locked 상태에서도 실제 마우스 이동을 반영합니다.
            dbg_mouseViewport = virtualCursorPos;

            if (WorldCameraManager.Instance != null && WorldCameraManager.Instance.currentStanceSO != null)
            {
                CameraStancePresetSO dbgStance = WorldCameraManager.Instance.currentStanceSO;
                dbg_edgeThreshold = dbgStance.edgePanningData.edgePanThreshold;
                dbg_mouseAtEdgeX = dbg_mouseViewport.x <= dbg_edgeThreshold || dbg_mouseViewport.x >= (1f - dbg_edgeThreshold);
                dbg_mouseAtEdgeY = dbg_mouseViewport.y <= dbg_edgeThreshold || dbg_mouseViewport.y >= (1f - dbg_edgeThreshold);
            }
            // ──────────────────────────────────────────────────────────────────────
        }
        #endregion

        #region Core Physics (Follow, Rotation, Collision, FinalTransform)
        private void HandleFollowTarget()
        {
            if (WorldCameraManager.Instance == null) return;

            if (isContextualMode && currentFocusTarget != null)
            {
                Vector3 contextPos = currentFocusTarget.position + (currentFocusTarget.rotation * currentOffset);
                transform.position = Vector3.Lerp(transform.position, contextPos, Time.deltaTime * currentLerpSpeed);
                return;
            }

            Vector3 baseOffset = WorldCameraManager.Instance.currentBaseOffset;

            float hInput = player.playerNetworkManager != null ? player.playerNetworkManager.animatorHorizontalMovement.Value : 0f;
            float vInput = player.playerNetworkManager != null ? player.playerNetworkManager.animatorVerticalMovement.Value : 0f;

            float targetFraming = 0f;
            float targetFramingYaw = 0f;
            float currentDelayTime = 0.1f;
            float currentYawDelayTime = 0.1f;

            // =========================================================================================
            // [v4.4] 락온 진입 초기 프레이밍 처리
            // SO의 lockOnInitialFramingOffset 값으로 락온 진입 시 구도를 자동 설정합니다.
            // 예: lockOnInitialFramingOffset=0.6 → 플레이어 좌측, 타겟 중앙~우측 구도
            // =========================================================================================
            CameraStancePresetSO activeStance = WorldCameraManager.Instance.currentStanceSO;

            bool isLockedOnNow_framing = player.playerNetworkManager != null
                                      && player.playerNetworkManager.isLockedOn.Value;

            if (isLockedOnNow_framing && activeStance != null)
            {
                float initOffset = activeStance.edgePanningData.lockOnInitialFramingOffset;
                float blendTime = activeStance.edgePanningData.lockOnInitialFramingBlendTime;

                if (!isLockOnInitialFramingActive && Mathf.Abs(initOffset) > 0.001f)
                {
                    // 락온 진입 감지 → 초기 프레이밍 시작
                    isLockOnInitialFramingActive = true;
                    lockOnInitialFramingTimer = 0f;
                }

                if (isLockOnInitialFramingActive)
                {
                    if (blendTime < 0.001f)
                    {
                        // blendTime=0: 즉시 스냅
                        currentFramingOffsetX = initOffset;
                        framingVelocity = 0f;
                        isLockOnInitialFramingActive = false;
                    }
                    else
                    {
                        lockOnInitialFramingTimer += Time.deltaTime;
                        float t = Mathf.Clamp01(lockOnInitialFramingTimer / blendTime);
                        currentFramingOffsetX = Mathf.Lerp(currentFramingOffsetX, initOffset, t);
                        if (t >= 1f) isLockOnInitialFramingActive = false;
                    }
                }
            }
            else if (!isLockedOnNow_framing)
            {
                // 락온 해제 시 초기 프레이밍 상태 초기화
                isLockOnInitialFramingActive = false;
                lockOnInitialFramingTimer = 0f;
            }
            // =========================================================================================

            // =========================================================================================
            // [v4.4] 시퀀스 종료 후 프레이밍 오프셋 복원 처리 (restorePreviousFraming)
            //
            // WorldCameraManager가 CameraSequenceRoutine 종료 시 SetFramingOffset()을 호출하면
            // isPendingFramingRestore=true로 설정됩니다.
            // 이 블록이 매 프레임 pendingFramingTimer를 진행시켜 currentFramingOffsetX를
            // 저장된 스냅샷 값으로 부드럽게 복원합니다.
            //
            // 이 처리가 HandleFollowTarget 내부에 있는 이유:
            //   currentFramingOffsetX는 이 함수의 targetFraming 계산에서도 쓰이므로
            //   같은 스코프 안에서 처리해야 한 프레임 내 덮어쓰기 충돌을 피할 수 있습니다.
            // =========================================================================================
            if (isPendingFramingRestore)
            {
                pendingFramingTimer += Time.deltaTime;
                float t = Mathf.Clamp01(pendingFramingTimer / Mathf.Max(pendingFramingBlendTime, 0.001f));

                // SmoothStep으로 부드러운 가감속 (EaseInOut)
                float smoothT = t * t * (3f - 2f * t);
                currentFramingOffsetX = Mathf.Lerp(pendingFramingStartValue, pendingFramingTarget, smoothT);

                if (t >= 1f)
                {
                    currentFramingOffsetX = pendingFramingTarget;
                    framingVelocity = 0f;
                    isPendingFramingRestore = false;
                    if (showDebugLogs) Debug.Log($"[PlayerCamera] 프레이밍 복원 완료: {pendingFramingTarget:F3}");
                }
            }
            // =========================================================================================

            if (activeStance != null)
            {
                // 1. Use Dynamic Framing이 켜져있다면 A/D 입력을 받습니다.
                if (activeStance.useDynamicFraming)
                {
                    DynamicFramingData dynamicData = activeStance.dynamicFraming;

                    if (hInput < -0.1f) // 좌로 이동
                    {
                        targetFraming = dynamicData.leftStrafeMaxOffset * Mathf.Abs(hInput);
                        lastTargetFraming = -1f;
                        currentDelayTime = dynamicData.leftFramingDelay > 0 ? dynamicData.leftFramingDelay : 0.1f;

                        targetFramingYaw = dynamicData.leftStrafeYaw * Mathf.Abs(hInput);
                        lastTargetFramingYaw = -1f;
                        currentYawDelayTime = currentDelayTime;
                    }
                    else if (hInput > 0.1f) // 우로 이동
                    {
                        targetFraming = dynamicData.rightStrafeMaxOffset * Mathf.Abs(hInput);
                        lastTargetFraming = 1f;
                        currentDelayTime = dynamicData.rightFramingDelay > 0 ? dynamicData.rightFramingDelay : 0.1f;

                        targetFramingYaw = dynamicData.rightStrafeYaw * Mathf.Abs(hInput);
                        lastTargetFramingYaw = 1f;
                        currentYawDelayTime = currentDelayTime;
                    }
                    else if (Mathf.Abs(vInput) > 0.1f) // 직진 중 (Hold 해제 및 영점 복귀)
                    {
                        targetFraming = 0f;
                        lastTargetFraming = 0f;
                        currentDelayTime = dynamicData.centerReturnDelay > 0 ? dynamicData.centerReturnDelay : 0.3f;

                        targetFramingYaw = 0f;
                        lastTargetFramingYaw = 0f;
                        currentYawDelayTime = dynamicData.forwardBackwardReturnTime > 0 ? dynamicData.forwardBackwardReturnTime : 10f;
                    }
                    else // 멈췄을 때 (Idle)
                    {
                        // 🚨 [버그 완벽 수정] 
                        // 공격 스탠스(Impact)에 'Use Dynamic Framing'과 'Inherit'이 둘 다 켜져 있을 때, 
                        // Hold 옵션이 꺼져있다고 해서 0으로 돌아가버리는 현상을 막습니다.
                        // Inherit이 켜져있다면 무조건 현재 프레이밍을 최우선으로 동결(Freeze)시킵니다.
                        if (activeStance.inheritDynamicFraming)
                        {
                            targetFraming = currentFramingOffsetX;
                            targetFramingYaw = currentFramingYawOffset;
                            currentDelayTime = 0.1f;
                            currentYawDelayTime = 0.1f;
                        }
                        else
                        {
                            if (lastTargetFraming < 0f)
                            {
                                if (dynamicData.holdLeftStrafe) { targetFraming = currentFramingOffsetX; currentDelayTime = dynamicData.leftFramingDelay > 0 ? dynamicData.leftFramingDelay : 0.1f; }
                                else { targetFraming = 0f; currentDelayTime = dynamicData.centerReturnDelay > 0 ? dynamicData.centerReturnDelay : 0.3f; }
                            }
                            else if (lastTargetFraming > 0f)
                            {
                                if (dynamicData.holdRightStrafe) { targetFraming = currentFramingOffsetX; currentDelayTime = dynamicData.rightFramingDelay > 0 ? dynamicData.rightFramingDelay : 0.1f; }
                                else { targetFraming = 0f; currentDelayTime = dynamicData.centerReturnDelay > 0 ? dynamicData.centerReturnDelay : 0.3f; }
                            }

                            if (lastTargetFramingYaw < 0f)
                            {
                                if (dynamicData.holdLeftStrafe) { targetFramingYaw = currentFramingYawOffset; currentYawDelayTime = dynamicData.leftFramingDelay > 0 ? dynamicData.leftFramingDelay : 0.1f; }
                                else { targetFramingYaw = 0f; currentYawDelayTime = dynamicData.centerReturnDelay > 0 ? dynamicData.centerReturnDelay : 0.3f; }
                            }
                            else if (lastTargetFramingYaw > 0f)
                            {
                                if (dynamicData.holdRightStrafe) { targetFramingYaw = currentFramingYawOffset; currentYawDelayTime = dynamicData.rightFramingDelay > 0 ? dynamicData.rightFramingDelay : 0.1f; }
                                else { targetFramingYaw = 0f; currentYawDelayTime = dynamicData.centerReturnDelay > 0 ? dynamicData.centerReturnDelay : 0.3f; }
                            }
                        }
                    }

                    // =========================================================================================
                    // 🚨 [Phase 5 고도화] 락온 타겟 시야 이탈 시 A/D 키 페널티 극복 가중치 연산
                    // =========================================================================================
                    bool isLockedOn = player != null && player.playerNetworkManager != null && player.playerNetworkManager.isLockedOn.Value;
                    if (isLockedOn && activeStance.lockOnPenaltyData.enableTargetEscapePenalty && player.playerCombatManager != null && player.playerCombatManager.currentTarget != null)
                    {
                        Vector3 targetPos = player.playerCombatManager.currentTarget.characterCombatManager.lockOnTransform.position;
                        Vector3 viewportPos = cameraObject.WorldToViewportPoint(targetPos);
                        float escapeThreshold = activeStance.lockOnPenaltyData.targetEscapeViewportThreshold;

                        if (viewportPos.z < 0f || viewportPos.x < escapeThreshold || viewportPos.x > (1f - escapeThreshold) || viewportPos.y < escapeThreshold || viewportPos.y > (1f - escapeThreshold))
                        {
                            // 타겟 이탈 페널티 상태에서 유저가 A/D 이동으로 앵글 쟁취를 시도할 때 가중치(RecoveryWeight)를 곱해 반응성을 높입니다.
                            targetFraming *= activeStance.lockOnPenaltyData.strafeRecoveryWeight;
                            targetFramingYaw *= activeStance.lockOnPenaltyData.strafeRecoveryWeight;
                        }
                    }
                }
                // 2. Use Dynamic Framing이 꺼져있지만 Inherit 옵션이 켜져있다면?
                else if (activeStance.inheritDynamicFraming)
                {
                    targetFraming = currentFramingOffsetX;
                    targetFramingYaw = currentFramingYawOffset;
                    currentDelayTime = 0.1f;
                    currentYawDelayTime = 0.1f;
                }
                // 3. 둘 다 꺼져 있다면 중앙(0)으로 부드럽게 복귀시킵니다.
                else
                {
                    targetFraming = 0f; lastTargetFraming = 0f;
                    targetFramingYaw = 0f; lastTargetFramingYaw = 0f;
                    currentDelayTime = 0.3f;
                    currentYawDelayTime = 10f;
                }
            }
            else
            {
                targetFraming = 0f; lastTargetFraming = 0f;
                targetFramingYaw = 0f; lastTargetFramingYaw = 0f;
                currentDelayTime = 0.3f;
                currentYawDelayTime = 10f;
            }

            // 부드러운 스프링 감속(SmoothDamp) 처리
            // [v3.9 Fix] maxSpeed 파라미터로 velocity 폭발 원천 차단
            // Mathf.SmoothDamp(current, target, ref vel, time, maxSpeed)
            // maxSpeed: 초당 최대 이동 속도 상한 — 이 값 초과 시 velocity 자동 클램핑됨
            const float kMaxFramingSpeed = 8f;   // 초당 최대 X 오프셋 이동 속도 (m/s)
            const float kMaxFramingYawSpeed = 120f; // 초당 최대 Yaw 오프셋 변화 속도 (deg/s)

            currentFramingOffsetX = Mathf.SmoothDamp(
                currentFramingOffsetX, targetFraming,
                ref framingVelocity, Mathf.Max(currentDelayTime, 0.05f),
                kMaxFramingSpeed);

            currentFramingYawOffset = Mathf.SmoothDampAngle(
                currentFramingYawOffset, targetFramingYaw,
                ref framingYawVelocity, Mathf.Max(currentYawDelayTime, 0.05f),
                kMaxFramingYawSpeed);

            // [v3.9 안전장치] 발산 감지 후 강제 클램핑
            // 정상 범위를 벗어난 경우 즉시 0으로 스냅하고 velocity 제거
            if (float.IsNaN(currentFramingOffsetX) || Mathf.Abs(currentFramingOffsetX) > 20f)
            {
                currentFramingOffsetX = 0f;
                framingVelocity = 0f;
                Debug.LogWarning("[PlayerCamera] currentFramingOffsetX 발산 감지 — 강제 리셋");
            }
            if (float.IsNaN(currentFramingYawOffset) || Mathf.Abs(currentFramingYawOffset) > 180f)
            {
                currentFramingYawOffset = 0f;
                framingYawVelocity = 0f;
                Debug.LogWarning("[PlayerCamera] currentFramingYawOffset 발산 감지 — 강제 리셋");
            }

            // ── [v3.9 Debug] 프레이밍 상태 기록 ──────────────────────────────────
            dbg_currentFramingOffsetX = currentFramingOffsetX;
            dbg_currentFramingYawOffset = currentFramingYawOffset;

            if (player != null && player.playerNetworkManager != null)
            {
                float hDbg = player.playerNetworkManager.animatorHorizontalMovement.Value;
                if (hDbg < -0.1f) dbg_framingState = "STRAFE_LEFT";
                else if (hDbg > 0.1f) dbg_framingState = "STRAFE_RIGHT(D)";
                else dbg_framingState = "IDLE/HOLD";
            }
            // ──────────────────────────────────────────────────────────────────────

            // =========================================================================================
            // Y축 전용 짐벌(Gimbal) 서스펜션 도입
            // =========================================================================================
            float targetPlayerY = player.transform.position.y;
            smoothedPlayerY = Mathf.SmoothDamp(smoothedPlayerY, targetPlayerY, ref playerYVelocity, 0.15f);

            Vector3 massCenter = player.transform.position;
            massCenter.y = smoothedPlayerY; // 흔들림 없는 안전한 Y축 적용
            massCenter += (Vector3.up * baseOffset.y);
            dbgMassCenter = massCenter;

            // =========================================================================================
            // 수직(상하) 시점 조작감 세분화 동적 높이 보간
            // =========================================================================================
            float targetVerticalOffset = 0f;
            float heightSmoothTime = 0.1f;

            if (activeStance != null)
            {
                var vBehavior = activeStance.verticalBehavior;
                if (vBehavior.behaviorType == TDA.Cameras.CameraVerticalBehavior.ElevationOnly)
                {
                    float t = Mathf.InverseLerp(minimumPivot, maximumPivot, upAndDownLookAngle);
                    targetVerticalOffset = Mathf.Lerp(vBehavior.maxElevationHeight, vBehavior.minElevationHeight, t);
                    heightSmoothTime = 1f / Mathf.Max(vBehavior.elevationSpeed, 0.1f);
                }
                else if (vBehavior.behaviorType == TDA.Cameras.CameraVerticalBehavior.DynamicOverShoulder)
                {
                    if (Mathf.Abs(vBehavior.pitchForMaxHeight) > 0.1f)
                    {
                        float ratio = upAndDownLookAngle / vBehavior.pitchForMaxHeight;
                        ratio = Mathf.Clamp01(ratio);
                        targetVerticalOffset = ratio * vBehavior.maxDynamicHeight;
                    }
                    heightSmoothTime = vBehavior.heightSmoothTime > 0 ? vBehavior.heightSmoothTime : 0.15f;
                }
            }

            currentDynamicHeight = Mathf.SmoothDamp(currentDynamicHeight, targetVerticalOffset, ref dynamicHeightVelocity, heightSmoothTime);

            // =========================================================================================
            // 카메라 오르빗 위치(desiredPos) 계산
            //
            // [v4.3 루프A 차단] cameraRight를 leftAndRightLookAngle 기반으로 계산하면
            // HandleMagneticSoftLock이 leftAndRightLookAngle을 수정할 때마다
            // 다음 프레임 desiredPos가 바뀌어 FocusPivot velocity가 흔들리는
            // FocusPivot ↔ Magnetic 무한 피드백 루프가 발생합니다.
            //
            // 해결: cameraRight를 이 프레임의 leftAndRightLookAngle(Magnetic이 수정하기 전 값)로
            //       계산합니다. Magnetic의 각도 수정은 다음 프레임 ApplyDynamicYawAndClamp를
            //       통해 렌더에만 반영되므로 desiredPos가 매 프레임 안정적으로 유지됩니다.
            //       (이미 이 시점의 leftAndRightLookAngle = HandleRotation 직후 값 = 안정적)
            // =========================================================================================
            Quaternion cameraYawRot = Quaternion.Euler(0, leftAndRightLookAngle, 0);
            Vector3 cameraRight = cameraYawRot * Vector3.right;

            Vector3 desiredPos = massCenter + (cameraRight * (baseOffset.x + currentFramingOffsetX));
            desiredPos.y += currentDynamicHeight;

            // =============================================================================
            // [Implementation Spec] 다중 타겟 동적 프레이밍 — 요구사항 2
            // =============================================================================
            // 조건 1: SO focusTargets에 항목이 있음
            // 조건 2: WorldCameraManager가 Transform을 resolve 했음 (currentFocusTargets)
            // 조건 3: multiTargetFraming 파라미터 사용
            // 
            // 처리 순서:
            //   ① SO 고정 타겟(TargetIdentifier) → 가중 합산
            //   ② 플레이어 반경 + 뷰포트 안에 있는 주변 적 → 추가 가중 합산
            //   ③ 가중 평균 → SmoothDamp로 부드럽게 desiredPos 이동
            // =============================================================================
            if (activeStance != null
                && WorldCameraManager.Instance.currentFocusTargets != null
                && WorldCameraManager.Instance.currentFocusTargets.Length > 0)
            {
                var mf = activeStance.multiTargetFraming;
                var soFT = activeStance.focusTargets;

                Vector3 weightedCenter = Vector3.zero;
                float totalWeight = 0f;

                // ① SO 고정 타겟 가중 합산
                for (int fi = 0; fi < WorldCameraManager.Instance.currentFocusTargets.Length; fi++)
                {
                    Transform ft = WorldCameraManager.Instance.currentFocusTargets[fi];
                    if (ft == null) continue;

                    float w = (soFT != null && fi < soFT.Count)
                              ? soFT[fi].weight * mf.staticTargetWeightScale
                              : 0f;
                    if (w <= 0f) continue;

                    weightedCenter += ft.position * w;
                    totalWeight += w;
                }

                // ② 플레이어 반경 & 뷰포트 필터 통과 주변 적 가산
                if (mf.focusDetectionRadius > 0.01f && mf.proximityEnemyWeight > 0.001f)
                {
                    Collider[] nearby = Physics.OverlapSphere(
                        player.transform.position,
                        mf.focusDetectionRadius,
                        WorldUtilityManager.Instance.GetCharacterLayers());

                    foreach (Collider col in nearby)
                    {
                        CharacterManager cm = col.GetComponent<CharacterManager>();
                        if (cm == null) continue;
                        if (cm.transform.root == player.transform.root) continue;
                        if (cm.characterNetworkManager != null && cm.characterNetworkManager.isDead.Value) continue;

                        // 뷰포트 안에 있는지 확인
                        Vector3 vp = cameraObject.WorldToViewportPoint(cm.transform.position);
                        if (vp.z < 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;

                        weightedCenter += cm.transform.position * mf.proximityEnemyWeight;
                        totalWeight += mf.proximityEnemyWeight;
                    }
                }

                // ③ [v4.4 재설계] FocusPivot — 위치 이동 → Yaw 방향 보정으로 완전 변경
                //
                // 기존 방식의 근본 설계 버그:
                //   desiredPos(카메라 피벗 위치)를 finalCenter(적들의 월드 중간 지점)로 SmoothDamp
                //   → 카메라 위치가 플레이어에서 멀어짐
                //   → 다음 프레임 ①에서 desiredPos = massCenter + offset으로 다시 리셋
                //   → SmoothDamp target(적 중간)과 source(리셋된 플레이어 옆)가 충돌
                //   → velocity 4.3m/s가 영원히 수렴하지 않음 → 진동!
                //
                // 새 방식:
                //   finalCenter 방향으로 Yaw(수평 각도)만 SmoothDamp로 보정
                //   → 카메라 위치(transform.position, desiredPos)는 일절 건드리지 않음
                //   → Yaw가 수렴하면 focusYawVelocity → 0 → 완전 정지
                //   → 피드백 루프 없음
                if (totalWeight > 0.001f)
                {
                    Vector3 finalCenter = weightedCenter / totalWeight;
                    finalCenter.y = desiredPos.y; // Y축 제외 (수평 방향만)

                    // 카메라에서 finalCenter를 향하는 수평 방향 Yaw 계산
                    // transform.position 대신 desiredPos 기준 (이 프레임의 목표 위치)
                    Vector3 dirToCenter = finalCenter - desiredPos;
                    dirToCenter.y = 0f;

                    if (dirToCenter.sqrMagnitude > 0.01f) // 너무 가까우면 무시
                    {
                        float focusYawTarget = Mathf.Atan2(dirToCenter.x, dirToCenter.z) * Mathf.Rad2Deg;
                        float focusDampTime = Mathf.Max(mf.focusDampTime, 0.3f);

                        // ── [v4.4 수정] FocusPivot Yaw — 공격(isPerformingAction) 중 억제 ──
                        // 문제: 공격 루트모션으로 플레이어 위치가 이동하면 finalCenter 방향이
                        //       매 프레임 변하고, SmoothDampAngle이 leftAndRightLookAngle을
                        //       계속 당겨서 bodyYawFollowWeight=0이어도 카메라가 휘둘림.
                        // 수정: isPerformingAction=true이면 FocusPivot Yaw 수정을 억제하고
                        //       velocity만 부드럽게 감쇠합니다.
                        //       (bodyYawTracking 파라미터와 연동: weight가 낮을수록 더 강하게 억제)
                        CameraStancePresetSO stanceForFocusYaw = WorldCameraManager.Instance?.currentStanceSO;
                        float bodyW = stanceForFocusYaw != null
                            ? stanceForFocusYaw.bodyYawTracking.bodyYawFollowWeight : 1.0f;

                        // 공격 중이고 bodyWeight가 낮으면 FocusPivot Yaw도 억제
                        // (bodyWeight=1이면 기존 동작 유지, bodyWeight=0이면 완전 차단)
                        // lockCameraToSnapshot 상태에서는 FocusPivot도 완전 억제
                        // (HandleRotation에서 카메라를 스냅샷으로 덮어쓰므로
                        //  FocusPivot이 leftAndRightLookAngle을 수정해도 의미 없지만,
                        //  HandleFollowTarget→HandleRotation 순서상 여기서 차단하는 게 더 명확)
                        bool suppressFocusYaw = player.isPerformingAction && bodyW < 0.99f;

                        // Magnetic 비활성 + 억제 없음 + 수렴하지 않은 경우에만 수정
                        bool magneticOff = !isMagneticActive;
                        if (magneticOff && !suppressFocusYaw)
                        {
                            leftAndRightLookAngle = Mathf.SmoothDampAngle(
                                leftAndRightLookAngle, focusYawTarget,
                                ref focusYawVelocity, focusDampTime);
                        }
                        else
                        {
                            // Magnetic 활성 또는 공격 억제 중: velocity만 감쇠
                            float decayRate = suppressFocusYaw ? 8f : 5f; // 공격 중 더 빠르게 수렴
                            focusYawVelocity = Mathf.Lerp(focusYawVelocity, 0f, Time.deltaTime * decayRate);
                        }
                        // ────────────────────────────────────────────────────────────────
                    }
                    else
                    {
                        focusYawVelocity = Mathf.Lerp(focusYawVelocity, 0f, Time.deltaTime * 10f);
                    }

                    // 레거시 velocity 초기화 (위치 이동 방식 제거됨)
                    focusPivotVelocity = Vector3.zero;
                }
            }
            else
            {
                // focusTargets 없음 — velocity 유지 (갑작스러운 정지 방지)
                focusPivotVelocity = Vector3.zero;
            }
            // =============================================================================

            dbgDesiredPos = desiredPos;

            // =========================================================================================
            // Action Damping Override (위치)
            // =========================================================================================
            float currentPosDamp = cameraSmoothSpeed;
            if (WorldCameraManager.Instance != null && WorldCameraManager.Instance.IsSequencePlaying)
            {
                currentPosDamp = WorldCameraManager.Instance.CurrentPositionDamping;
            }

            // [v3.9 안전장치] desiredPos가 합리적 범위를 벗어나면 스냅 방지
            // player 위치 기준 100m 이상이면 발산으로 판단 → velocity 리셋 + 스냅
            if (Vector3.Distance(desiredPos, player.transform.position) > 100f
                || float.IsNaN(desiredPos.x) || float.IsNaN(desiredPos.y) || float.IsNaN(desiredPos.z))
            {
                Debug.LogWarning("[PlayerCamera] desiredPos 발산 감지 — 강제 위치 리셋");
                transform.position = desiredPos = player.transform.position + Vector3.up * 1.5f;
                cameraVelocity = Vector3.zero;
            }

            transform.position = Vector3.SmoothDamp(
                transform.position, desiredPos, ref cameraVelocity,
                Mathf.Max(currentPosDamp, 0.03f), 50f); // maxSpeed=50m/s 상한
        }

        private Quaternion HandleRotation()
        {
            if (isContextualMode) return transform.rotation;

            // =========================================================================================
            // [Implementation Spec 수정] 요구사항 4 — 연출/동작 종료 후 이전 시점 복귀
            // restoreAngleSpeed: Sequence SO 파라미터 (하드코딩 5f 제거)
            // 복귀 완료 조건: Yaw/Pitch 모두 1도 이내 수렴 시 자동 종료
            // =========================================================================================
            if (WorldCameraManager.Instance != null && WorldCameraManager.Instance.shouldRestorePreviousAngle)
            {
                // Sequence SO에서 복귀 속도 읽기 (없으면 기본 3.0)
                float restoreSpeed = (WorldCameraManager.Instance.currentSequenceSO != null
                                      ? WorldCameraManager.Instance.currentSequenceSO.restoreAngleSpeed
                                      : 3.0f) * Time.deltaTime;

                leftAndRightLookAngle = Mathf.LerpAngle(leftAndRightLookAngle, WorldCameraManager.Instance.storedYaw, restoreSpeed);
                upAndDownLookAngle = Mathf.LerpAngle(upAndDownLookAngle, WorldCameraManager.Instance.storedPitch, restoreSpeed);

                // 완료 감지: 1도 이내 수렴 시 shouldRestorePreviousAngle = false
                float yawDiff = Mathf.Abs(Mathf.DeltaAngle(leftAndRightLookAngle, WorldCameraManager.Instance.storedYaw));
                float pitchDiff = Mathf.Abs(upAndDownLookAngle - WorldCameraManager.Instance.storedPitch);
                if (yawDiff < 1.0f && pitchDiff < 1.0f)
                    WorldCameraManager.Instance.shouldRestorePreviousAngle = false;
            }
            // =========================================================================================

            float trackingWeight = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.CurrentTrackingWeight : 1f;

            if (playerGestureManager == null && player != null)
                playerGestureManager = player.GetComponent<PlayerGestureManager>();

            float currentHorizontal = cameraHorizontalInput;
            float currentVertical = cameraVerticalInput;

            if (playerGestureManager != null && playerGestureManager.IsDragging)
            {
                currentHorizontal = 0f;
                currentVertical = 0f;
            }

            float baseRotDamp = cameraRotationDampTime;
            if (WorldCameraManager.Instance != null && WorldCameraManager.Instance.IsSequencePlaying)
                baseRotDamp = WorldCameraManager.Instance.CurrentRotationDamping;

            float dampingMultiplier = Mathf.Clamp01(cameraRotationDampTime / Mathf.Max(baseRotDamp, 0.001f));

            float mouseYaw = currentHorizontal * leftAndRightRotationSpeed * mouseSensitivity * dampingMultiplier * Time.deltaTime;
            float mousePitch = currentVertical * upAndDownRotationSpeed * mouseSensitivity * dampingMultiplier * Time.deltaTime;

            // 1. 상하(Pitch) 앵글 직접 증감 및 클램프 (뒤집힘 원천 차단)
            upAndDownLookAngle -= mousePitch;
            upAndDownLookAngle = Mathf.Clamp(upAndDownLookAngle, minimumPivot, maximumPivot);

            if (trackingWeight <= 0.01f)
            {
                leftAndRightLookAngle += mouseYaw;
                if (leftAndRightLookAngle > 360f) leftAndRightLookAngle -= 360f;
                if (leftAndRightLookAngle < -360f) leftAndRightLookAngle += 360f;

                float fixedPitchWeightZero = upAndDownLookAngle;
                CameraStancePresetSO activeStanceZero = WorldCameraManager.Instance?.currentStanceSO;
                if (activeStanceZero != null && activeStanceZero.verticalBehavior.behaviorType == TDA.Cameras.CameraVerticalBehavior.ElevationOnly)
                    fixedPitchWeightZero = activeStanceZero.verticalBehavior.fixedPitchAngle;

                return Quaternion.Euler(fixedPitchWeightZero, leftAndRightLookAngle, 0);
            }

            // =========================================================================================
            // 🚨 [v3.9 수정] 공격 중 루트모션 허리 회전 → 카메라 급격히 꺾임 방지
            //
            // 원인: 베기 루트모션이 player.transform.rotation 을 직접 회전시키면
            //       playerBodyYaw 가 매 프레임 급변하고, 아래의
            //       leftAndRightLookAngle = playerBodyYaw + currentDiff 계산이
            //       그 변화량을 카메라에 그대로 누적한다.
            //
            // 해결: isPerformingAction=true 이고 락온이 아닌 경우에는
            //       playerBodyYaw 기준 재계산을 완전히 건너뛰고
            //       현재 leftAndRightLookAngle 을 유지하면서 마우스 입력만 반영한다.
            //       락온 시에는 타겟 추적이 우선이므로 이 블록을 건너뛴다.
            // =========================================================================================
            bool isLockedOnNow = player.playerNetworkManager != null
                                 && player.playerNetworkManager.isLockedOn.Value;

            // [v4.4 재설계] isPerformingAction && !isLockedOnNow 의 early return 제거.
            // 기존: 비락온 공격 → 마우스만 반영 후 early return
            //       → actionJustStarted/lockCameraToSnapshot 코드에 도달 불가 → 스냅샷 무효
            // 수정: 스냅샷 고정 로직(lockCameraToSnapshot)이 비락온/락온 모두 처리.
            //       bodyWeight=1인 Stance(기본 스탠스)에서 비락온 공격 시에는
            //       기존 동작(마우스만 반영)과 동일하게 effBodyYaw 경로를 통과.
            // 이 블록은 의도적으로 제거됨 — 아래 스냅샷 로직이 대체.

            // =====================================================================
            // [v3.9] 락온 D키 우측 이동 중 Yaw 동결 — 요구사항 2
            // 요청: "D키 시 카메라 이동은 정지하고 플레이어만 화면 좌→중앙으로 이동"
            // 원리: isStrafeRight=true 시 leftAndRightLookAngle 고정.
            //       HandleFollowTarget의 currentFramingOffsetX만 변해 피벗이 이동.
            //       → 카메라 방향은 고정, 플레이어만 화면 내 위치 이동 효과.
            // =====================================================================
            float hInputForStrafe = player.playerNetworkManager != null
                ? player.playerNetworkManager.animatorHorizontalMovement.Value : 0f;

            bool isStrafeRight = isLockedOnNow && hInputForStrafe > 0.1f;

            if (isStrafeRight)
            {
                // 마우스 입력만 허용 — playerBodyYaw 추적 / AutoCenter 완전 차단
                leftAndRightLookAngle += mouseYaw;
                if (leftAndRightLookAngle > 360f) leftAndRightLookAngle -= 360f;
                if (leftAndRightLookAngle < -360f) leftAndRightLookAngle += 360f;

                float fixedPitchStrafe = upAndDownLookAngle;
                CameraStancePresetSO stanceStrafe = WorldCameraManager.Instance?.currentStanceSO;
                if (stanceStrafe != null && stanceStrafe.verticalBehavior.behaviorType == TDA.Cameras.CameraVerticalBehavior.ElevationOnly)
                    fixedPitchStrafe = stanceStrafe.verticalBehavior.fixedPitchAngle;

                dbg_isStrafeRight = true; dbg_yawFrozen = true;
                return Quaternion.Euler(fixedPitchStrafe, leftAndRightLookAngle, 0);
            }
            // =====================================================================

            dbg_isStrafeRight = false;
            dbg_yawFrozen = false;

            float playerBodyYaw = player.transform.eulerAngles.y;

            // =========================================================================================
            // [v4.4 재설계] 공격 중 카메라 완전 고정 시스템
            //
            // SO 파라미터:
            //   bodyYawTracking.lockCameraOnActionStart — true이면 이 Stance에서 공격 중 고정
            //   bodyYawTracking.lockCameraMouseInfluence — 마우스 입력 허용 비율 (0=완전고정/1=자유)
            //   bodyYawTracking.bodyYawFollowWeight — 0이면 몸통 추적 차단 (고정 보조)
            //
            // 작동 조건: lockCameraOnActionStart=true + isPerformingAction=true
            //            비락온 / 락온 모두 동일하게 적용 (isLockedOnNow 무관)
            //
            // 고정 메커니즘:
            //   공격 시작(false→true 전환) 시 현재 Yaw/Pitch를 스냅샷으로 저장.
            //   공격 중 매 프레임 leftAndRightLookAngle을 스냅샷으로 덮어쓰고 early return.
            //   → FocusPivot/Magnetic/bodyYaw/AutoCenter 등 어떤 시스템도 이 이후로는 각도를 바꿀 수 없음.
            //   mouseInfluence 비율로 마우스/스틱 입력을 선택적으로 허용.
            // =========================================================================================
            CameraStancePresetSO stanceForBodyCheck = WorldCameraManager.Instance?.currentStanceSO;
            float bodyWeightCheck = stanceForBodyCheck != null
                ? stanceForBodyCheck.bodyYawTracking.bodyYawFollowWeight : 1.0f;
            bool lockOnStart = stanceForBodyCheck != null
                && stanceForBodyCheck.bodyYawTracking.lockCameraOnActionStart;
            float mouseInfluence = stanceForBodyCheck != null
                ? stanceForBodyCheck.bodyYawTracking.lockCameraMouseInfluence : 0.5f;

            // ── 공격 시작 감지: isPerformingAction false→true 전환 시 스냅샷 저장 ──
            // lockCameraOnActionStart=true인 Stance에서만 저장
            // (bodyWeight<1 조건은 lockOnStart가 더 명시적이므로 lockOnStart로 대체)
            bool actionJustStarted = player.isPerformingAction && !wasPerformingAction
                && (lockOnStart || bodyWeightCheck < 0.99f);
            if (actionJustStarted)
            {
                actionStartYaw = leftAndRightLookAngle;
                actionStartPitch = upAndDownLookAngle;
                if (showDebugLogs)
                    Debug.Log($"[PlayerCamera] 공격 시작 스냅샷 저장: Yaw={actionStartYaw:F1}° Pitch={actionStartPitch:F1}° lockOnStart={lockOnStart}");
            }
            wasPerformingAction = player.isPerformingAction;

            // ── 카메라 고정 실행 ─────────────────────────────────────────────────────
            // lockCameraOnActionStart=true이면 isLockedOnNow 무관하게 비락온/락온 모두 고정
            // bodyWeight<1 폴백: lockOnStart가 꺼져 있어도 weight<1이면 기존 동작 유지
            bool lockCameraToSnapshot = player.isPerformingAction
                && (lockOnStart || bodyWeightCheck < 0.99f);

            if (lockCameraToSnapshot)
            {
                // 스냅샷으로 덮어쓰기 — 이전 프레임에서 FocusPivot/Magnetic이 수정한 값을 무효화
                leftAndRightLookAngle = actionStartYaw;
                upAndDownLookAngle = Mathf.Clamp(actionStartPitch, minimumPivot, maximumPivot);

                // 마우스/스틱 입력은 mouseInfluence 비율로 허용
                float scaledMouseYaw = mouseYaw * mouseInfluence;
                float scaledMousePitch = mousePitch * mouseInfluence;
                leftAndRightLookAngle += scaledMouseYaw;
                upAndDownLookAngle -= scaledMousePitch;
                upAndDownLookAngle = Mathf.Clamp(upAndDownLookAngle, minimumPivot, maximumPivot);

                // 허용된 마우스 입력을 스냅샷에 누적 (다음 프레임 기준점 갱신)
                actionStartYaw = leftAndRightLookAngle;
                actionStartPitch = upAndDownLookAngle;

                CameraStancePresetSO activeStanceLock = WorldCameraManager.Instance?.currentStanceSO;
                float lockedPitch = upAndDownLookAngle;
                if (activeStanceLock != null
                    && activeStanceLock.verticalBehavior.behaviorType == TDA.Cameras.CameraVerticalBehavior.ElevationOnly)
                    lockedPitch = activeStanceLock.verticalBehavior.fixedPitchAngle;

                dbg_yawFrozen = true;
                return Quaternion.Euler(lockedPitch, leftAndRightLookAngle, 0);
            }
            // =========================================================================================

            // =========================================================================================
            // [v4.4 신규] 몸통 Yaw 추적 제어 — bodyYawFollowWeight / bodyYawFollowSmoothTime
            //
            // 문제: 락온 공격(루트모션) 중 playerBodyYaw가 급변하면 leftAndRightLookAngle도
            //       같이 급변하여 카메라가 어지럽게 회전합니다.
            //
            // 해결:
            //   1. SmoothDamp로 playerBodyYaw를 부드럽게 추적하는 smoothedPlayerBodyYaw 계산
            //      → bodyYawFollowSmoothTime이 클수록 느리게 따라감
            //   2. 실제 계산에 사용하는 effBodyYaw = Lerp(고정값, smoothed, weight)
            //      → bodyYawFollowWeight=0이면 몸통 회전을 완전히 무시
            //      → bodyYawFollowWeight=1이면 기존과 동일
            // =========================================================================================
            CameraStancePresetSO stanceForBodyYaw = WorldCameraManager.Instance?.currentStanceSO;
            float bodyWeight = stanceForBodyYaw != null
                ? stanceForBodyYaw.bodyYawTracking.bodyYawFollowWeight : 1.0f;
            float bodySmoothTime = stanceForBodyYaw != null
                ? stanceForBodyYaw.bodyYawTracking.bodyYawFollowSmoothTime : 0f;

            // [v4.4 버그 수정] smoothedBodyYaw 초기화 — weight가 낮을 때만
            // bodyWeight < 1이면 smoothedBodyYaw가 이전 값(구 각도)을 기준으로 보간 시작.
            // 공격 시작 직전 playerBodyYaw를 즉시 스냅하여 보간 시작점을 올바르게 설정합니다.
            // 이렇게 하면 weight>0일 때도 공격 시작 시 smoothedBodyYaw가 현재 몸통 위치에서 출발합니다.
            if (bodyWeight < 0.99f && !player.isPerformingAction)
            {
                // 공격이 아닌 동안은 실제 playerBodyYaw와 동기화 유지
                smoothedPlayerBodyYaw = playerBodyYaw;
                smoothedPlayerBodyYawVelocity = 0f;
            }

            // smoothedPlayerBodyYaw: SmoothDamp로 실제 몸통 Yaw를 추적 (공격 중에만 의미 있음)
            if (bodySmoothTime < 0.005f)
            {
                // smoothTime이 0에 가까우면 즉각 추적 (기존 동작, SmoothDamp 오버헤드 없음)
                smoothedPlayerBodyYaw = playerBodyYaw;
            }
            else
            {
                // 각도 랩어라운드(360↔0) 안전 처리를 위해 SmoothDampAngle 사용
                smoothedPlayerBodyYaw = Mathf.SmoothDampAngle(
                    smoothedPlayerBodyYaw, playerBodyYaw,
                    ref smoothedPlayerBodyYawVelocity, bodySmoothTime);
            }

            // effBodyYaw: weight=0이면 이전 프레임의 smoothedBodyYaw를 고정 (회전 차단)
            //             weight=1이면 smoothedBodyYaw 그대로 (기존 동작)
            float effBodyYaw = (bodyWeight >= 0.999f)
                ? smoothedPlayerBodyYaw
                : Mathf.LerpAngle(leftAndRightLookAngle, smoothedPlayerBodyYaw, bodyWeight);
            // =========================================================================================

            // 1. 현재 캐릭터 몸통과 카메라가 얼마나 벌어져 있는지(차이)를 안전하게 산출합니다.
            // effBodyYaw를 기준으로 사용하여 weight/smoothTime이 반영된 몸통 방향 기준으로 diff 계산
            float currentDiff = Mathf.DeltaAngle(effBodyYaw, leftAndRightLookAngle);

            // 2. 이 '차이 공간' 안에서 유저의 마우스 입력을 더합니다.
            currentDiff += mouseYaw;

            // 3. 멀미 방어 구간일 때는 한계각을 180도(자유 시점)까지 넓혀줍니다.
            // 단, 180도 정점에서의 기하학적 버그(Singularity)를 막기 위해 최대 179.9f로 하드코딩 락을 겁니다.
            float effectiveMaxViewAngle = Mathf.Lerp(179.9f, maxViewAngle, trackingWeight);

            // ── [v4.1 수정C] 엣지 패닝 발동 중 시야각 제한 완화 ──────────────────────────
            // 문제 2: 락온 상태에서 currentDiff가 이미 maxViewAngle에 가까우면
            // 추가 엣지 입력이 clamp에 막혀 카메라가 더 이상 회전하지 않는 현상 해결.
            // 엣지 패닝 발동 중에는 최대 시야각을 1.8배로 확장 (최대 179.9 이하 유지)
            if (isLockedOnNow && isEdgePanActive)
            {
                effectiveMaxViewAngle = Mathf.Min(effectiveMaxViewAngle * 1.8f, 179.9f);
            }
            // ─────────────────────────────────────────────────────────────────────────────

            // 4. 차이값이 한계선을 절대 벗어나지 못하도록 철벽 방어를 세웁니다! (탈선 불가)
            currentDiff = Mathf.Clamp(currentDiff, -effectiveMaxViewAngle, effectiveMaxViewAngle);

            // Auto Centering 로직 (이 역시 '차이 공간' 내부에서 안전하게 0으로 수렴시킵니다)
            if (Mathf.Abs(currentHorizontal) < 0.01f && Mathf.Abs(currentVertical) < 0.01f) noInputTimer += Time.deltaTime;
            else noInputTimer = 0f;

            bool isMoving = player.playerNetworkManager != null && player.playerNetworkManager.animatorMoveAmountMovement.Value > 0.1f;

            // ── [v4.3 루프B 차단] AutoCenter — 락온 + Magnetic ACTIVE 시 완전 차단 ──────────
            // [루프B] HandleRotation AutoCenter(각도→0)와 HandleMagneticSoftLock(각도→targetYaw)이
            // 매 프레임 서로 반대 방향으로 당겨 각도가 진동합니다.
            //
            // 조건 확장:
            //   - 기존: 락온 + useEdgePanning ON + 엣지 비발동
            //   - 신규: 락온 + Magnetic ACTIVE 상태이면 useEdgePanning 여부와 무관하게 차단
            //     → Magnetic이 이미 각도를 제어하므로 AutoCenter가 간섭하면 안 됩니다.
            CameraStancePresetSO stanceForAutoCenter = WorldCameraManager.Instance?.currentStanceSO;
            bool blockAutoCenter =
                // 기존 조건: 락온 + 엣지패닝 모드
                (isLockedOnNow
                 && stanceForAutoCenter != null
                 && stanceForAutoCenter.edgePanningData.useEdgePanning
                 && (!isEdgePanActive || isEdgeReturning))
                ||
                // 신규 조건: 락온 + Magnetic ACTIVE (루프B 차단 핵심)
                (isLockedOnNow && isMagneticActive);

            if (!blockAutoCenter)
            {
                if (enableAutoCenterOnMove && isMoving)
                {
                    if (Mathf.Abs(currentHorizontal) < 0.01f)
                    {
                        currentDiff = Mathf.Lerp(currentDiff, 0f, autoCenterSpeed * Time.deltaTime);
                    }
                }
                else if (enableAutoCenterOnIdle && !isMoving)
                {
                    if (noInputTimer >= idleAutoCenterDelay)
                    {
                        currentDiff = Mathf.Lerp(currentDiff, 0f, (autoCenterSpeed * 0.3f) * Time.deltaTime);
                    }
                }
            }
            // ─────────────────────────────────────────────────────────────────────────────

            // 5. 철벽을 거친 '안전한 차이값'을 다시 실제 글로벌 앵글로 변환해 줍니다.
            // effBodyYaw 기준으로 계산하여 weight/smoothTime이 반영됩니다.
            leftAndRightLookAngle = effBodyYaw + currentDiff;

            // ── [v4.1 수정E] 엣지 복귀 처리 — HandleRotation 단일 권위 ────────────────────
            // OnCameraInputReceived에서 각도를 직접 쓰면 이 줄(playerBodyYaw+currentDiff)이
            // 같은 프레임에 덮어쓰므로, 복귀 SmoothDamp를 여기서 처리합니다.
            // isEdgeReturning=true이고 returnTime이 남아있을 때만 동작합니다.
            if (isEdgeReturning)
            {
                float retTime = stanceForAutoCenter != null
                    ? Mathf.Max(stanceForAutoCenter.edgePanningData.edgePanReturnTime, 0.1f)
                    : 0.8f;
                float retDampTime = retTime * 0.45f; // SmoothDamp smoothTime = returnTime의 절반보다 약간 빠르게

                // Yaw 복귀
                leftAndRightLookAngle = Mathf.SmoothDampAngle(
                    leftAndRightLookAngle, edgePanReturnYaw,
                    ref edgePanReturnVelYaw, retDampTime, 180f);

                // Pitch 복귀
                upAndDownLookAngle = Mathf.SmoothDamp(
                    upAndDownLookAngle, edgePanReturnPitch,
                    ref edgePanReturnVelPitch, retDampTime, 180f);
                upAndDownLookAngle = Mathf.Clamp(upAndDownLookAngle, minimumPivot, maximumPivot);

                // 복귀 완료 판정 (1도, 0.5도 이내 수렴)
                if (Mathf.Abs(Mathf.DeltaAngle(leftAndRightLookAngle, edgePanReturnYaw)) < 1.0f
                    && Mathf.Abs(upAndDownLookAngle - edgePanReturnPitch) < 0.5f)
                {
                    isEdgeReturning = false;
                    edgePanReturnVelYaw = 0f;
                    edgePanReturnVelPitch = 0f;
                    if (showDebugLogs) Debug.Log("[PlayerCamera] EdgePan 복귀 완료");
                }
            }
            // ─────────────────────────────────────────────────────────────────────────────

            // =============================================================================
            // [Implementation Spec] 수직 Pitch 자동 복귀 — 요구사항 5
            // 위아래 엣지패닝 후 입력이 없고 이동 중이면 SO fixedPitchAngle로 서서히 복귀
            // pitchReturnSpeed = 0이면 비활성화 (복귀 없음)
            // =============================================================================
            CameraStancePresetSO activePitchStance = WorldCameraManager.Instance?.currentStanceSO;
            if (activePitchStance != null)
            {
                float pitchRetSpd = activePitchStance.lockOnPenaltyData.pitchReturnSpeed;
                if (pitchRetSpd > 0.001f && Mathf.Abs(currentVertical) < 0.01f && isMoving)
                {
                    float basePitch = activePitchStance.verticalBehavior.fixedPitchAngle;
                    upAndDownLookAngle = Mathf.Lerp(
                        upAndDownLookAngle,
                        basePitch,
                        pitchRetSpd * Time.deltaTime);
                    // Pitch 범위 보호
                    upAndDownLookAngle = Mathf.Clamp(upAndDownLookAngle, minimumPivot, maximumPivot);
                }
            }
            // =============================================================================

            // ── [v3.9 Debug] Rotation 상태 기록 ──────────────────────────────────
            dbg_leftAndRightLookAngle = leftAndRightLookAngle;
            dbg_playerBodyYaw = playerBodyYaw; // 실제 몸통 (원본)
            // effBodyYaw = weight/smooth 적용된 기준값 (OnGUI에서 확인 가능)
            dbg_bodyToCamDiff = currentDiff;
            dbg_trackingWeight = trackingWeight;
            dbg_noInputTimer = noInputTimer;
            // ──────────────────────────────────────────────────────────────────────

            return Quaternion.Euler(upAndDownLookAngle, leftAndRightLookAngle, 0);
        }

        private Quaternion ApplyDynamicYawAndClamp(Quaternion currentBaseRotation)
        {
            CameraStancePresetSO activeStance = WorldCameraManager.Instance?.currentStanceSO;
            float actualPitch = upAndDownLookAngle;

            if (activeStance != null && activeStance.verticalBehavior.behaviorType == TDA.Cameras.CameraVerticalBehavior.ElevationOnly)
            {
                actualPitch = activeStance.verticalBehavior.fixedPitchAngle;
            }

            float baseYawOffset = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.currentBaseYawOffset : 0f;
            float dynamicYawWeight = activeStance != null ? activeStance.dynamicFraming.dynamicYawWeight : 1f;

            // 다이내믹 프레이밍 오프셋 최종 산출
            float finalYawOffset = baseYawOffset + (currentFramingYawOffset * dynamicYawWeight);

            float playerBodyYaw = player.transform.eulerAngles.y;

            // 1. HandleRotation에서 보장된 안전한 렌더링 앵글의 차이값을 구합니다.
            float baseDiff = Mathf.DeltaAngle(playerBodyYaw, leftAndRightLookAngle);

            // 2. 다이내믹 프레이밍 오프셋을 이 차이에 더해줍니다.
            float totalDiff = baseDiff + finalYawOffset;

            float trackingWeight = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.CurrentTrackingWeight : 1f;
            float effectiveMaxViewAngle = Mathf.Lerp(179.9f, maxViewAngle, trackingWeight);

            // 3. 다이내믹 여유분(+15도)을 더하여 렌더링이 짤리지 않게 공간을 조금 더 늘려줍니다.
            float maxAllowedAngle = effectiveMaxViewAngle + 15f;

            // 4. 하지만 그 늘려준 공간조차도 180도 스냅 한계선(179.9)을 절대 넘지 못하게 최종 봉쇄합니다.
            maxAllowedAngle = Mathf.Min(maxAllowedAngle, 179.9f);

            // 5. 최종 클램프 적용
            totalDiff = Mathf.Clamp(totalDiff, -maxAllowedAngle, maxAllowedAngle);

            // 6. 안전하게 보호된 최종 글로벌 오일러 앵글 반환
            dbg_finalRenderYaw = playerBodyYaw + totalDiff; // [v3.9 Debug]
            return Quaternion.Euler(actualPitch, playerBodyYaw + totalDiff, 0);
        }

        private Quaternion HandleMagneticSoftLock(Quaternion currentRotation)
        {
            if (!player.playerNetworkManager.isLockedOn.Value || player.playerCombatManager.currentTarget == null)
            {
                dbg_isMagneticActive = false; dbg_magneticPullStrength = 0f;
                dbg_magneticState = "INACTIVE (no target)";
                return currentRotation;
            }

            Transform targetPoint = player.playerCombatManager.currentTarget.characterCombatManager.lockOnTransform;
            if (targetPoint == null) return currentRotation;

            // =====================================================================
            // [v3.9] 타겟 이탈 + enableTargetEscapePenalty 시 MagneticLock 차단
            // 이탈 시 자석 풀링을 끄고 OnCameraInputReceived()의 소프트 보정이 담당.
            // =====================================================================
            CameraStancePresetSO activeStanceForMag = WorldCameraManager.Instance?.currentStanceSO;
            if (activeStanceForMag != null && activeStanceForMag.lockOnPenaltyData.enableTargetEscapePenalty)
            {
                Vector3 checkVP = cameraObject.WorldToViewportPoint(targetPoint.position);
                float thr = activeStanceForMag.lockOnPenaltyData.targetEscapeViewportThreshold;

                // =====================================================================
                // [Implementation Spec] 좌/우 이탈 비대칭 처리 — 요구사항 3
                // 좌측 이탈: 자석 차단 → 유저에게 시선 자율권 부여
                // 우측 이탈: 자석 항상 ON → 타겟이 화면 우측 밖으로 나가면 자동 복귀
                // Y축 이탈: 좌측과 동일하게 자석 차단 (수직 자율권 보장)
                // =====================================================================
                bool isEscapedLeft = checkVP.z < 0f || checkVP.x < thr;
                bool isEscapedRight = checkVP.x > (1f - thr);
                bool isEscapedY = checkVP.y < thr || checkVP.y > (1f - thr);

                // 전체 이탈 여부 (디버그용)
                bool isEscapedForMag = isEscapedLeft || isEscapedRight || isEscapedY;

                // 디버그 타겟 이탈 상태 기록
                dbg_isTargetEscaped = isEscapedForMag;
                dbg_targetViewportPos = checkVP;
                dbg_lockOnState = isEscapedForMag ? "ESCAPED (penalty)" : "LOCKED_ON";

                if (player.playerCombatManager.currentTarget != null)
                    dbg_targetDist = Vector3.Distance(transform.position, targetPoint.position);

                // 좌측 이탈만 자석 차단 (우측 이탈은 자석 허용 — 자동 복귀)
                // isEscapedRight = true이면 이 블록을 건너뛰어 자석 풀링이 작동함
                if ((isEscapedLeft || isEscapedY) && !isEscapedRight)
                {
                    dbg_isMagneticActive = false;
                    dbg_magneticPullStrength = 0f;
                    dbg_magneticState = "BLOCKED (left/Y escape — user autonomy)";
                    return currentRotation;
                }
            }
            else
            {
                dbg_isTargetEscaped = false;
                dbg_lockOnState = "LOCKED_ON (no penalty)";
                if (activeStanceForMag != null && player.playerCombatManager.currentTarget != null)
                {
                    Vector3 checkVP2 = cameraObject.WorldToViewportPoint(targetPoint.position);
                    dbg_targetViewportPos = checkVP2;
                    dbg_targetDist = Vector3.Distance(transform.position, targetPoint.position);
                }
            }
            // =====================================================================

            Vector3 viewportPos = cameraObject.WorldToViewportPoint(targetPoint.position);
            Vector2 distanceFromCenter = new Vector2(
                Mathf.Abs(viewportPos.x - 0.5f),
                Mathf.Abs(viewportPos.y - 0.5f));

            // ── [v4.1 수정] Hysteresis 기반 자석 ON/OFF 판정 ──────────────────────────
            // 원인 3: pullStrength가 데드존 경계에서 0↔0.03으로 매 프레임 진동 → 떨림 발생
            // 수정: 진입 threshold(kMagEnterThreshold)보다 클 때만 ON이 되고,
            //       탈출 threshold(kMagExitThreshold) 아래로 내려가야 OFF가 되는
            //       Hysteresis 구조로 경계 근처 진동을 완전 차단합니다.
            float rawExceedX = Mathf.Max(0, distanceFromCenter.x - deadzoneRadius.x);
            float rawExceedY = Mathf.Max(0, distanceFromCenter.y - deadzoneRadius.y);
            float rawPullStrength = Mathf.Clamp01((rawExceedX + rawExceedY) * 2f);

            if (!isMagneticActive && rawPullStrength > kMagEnterThreshold)
                isMagneticActive = true;   // 진입: 충분히 밖으로 나갔을 때만 ON
            else if (isMagneticActive && rawPullStrength < kMagExitThreshold)
                isMagneticActive = false;  // 탈출: 충분히 안으로 들어왔을 때만 OFF

            float pullStrength = isMagneticActive ? rawPullStrength : 0f;
            // ─────────────────────────────────────────────────────────────────────

            dbg_isMagneticActive = isMagneticActive;
            dbg_magneticPullStrength = pullStrength;
            dbg_magneticState = isMagneticActive
                ? $"PULLING (str={pullStrength:F2})" : "IN_DEADZONE (hysteresis)";

            // 자석이 꺼진 상태면 velocity를 0으로 수렴 후 즉시 반환
            if (!isMagneticActive)
            {
                // velocity 잔상 제거 — 다음 ON 진입 시 관성 없이 시작
                magneticYawVelocity = 0f;
                magneticPitchVelocity = 0f;
                return currentRotation;
            }

            // ── [v4.1 수정] targetYaw/Pitch 계산 개선 ────────────────────────────────
            // 원인 2: dir.y를 Clamp한 뒤 LookRotation을 쓰면 Pitch가 경계에서 튀어서 진동
            // 수정: 수평 방향(XZ)으로 targetYaw를 먼저 계산하고,
            //       Pitch는 수직 각도를 직접 Atan2로 구해 Clamp합니다.
            Vector3 dirToTarget = targetPoint.position - transform.position;
            float horizDist = new Vector2(dirToTarget.x, dirToTarget.z).magnitude;

            // Yaw: 수평 방향 기준 (dir.y 영향 없음 → Pitch 진동과 완전 분리)
            float targetYaw = Mathf.Atan2(dirToTarget.x, dirToTarget.z) * Mathf.Rad2Deg;

            // Pitch: 수직 각도를 직접 계산 후 minimumPivot~maximumPivot으로 클램프
            float rawPitch = -Mathf.Atan2(dirToTarget.y, Mathf.Max(horizDist, 0.01f)) * Mathf.Rad2Deg;
            float targetPitch = Mathf.Clamp(rawPitch, minimumPivot, maximumPivot);
            // ─────────────────────────────────────────────────────────────────────

            float trackingWeight = WorldCameraManager.Instance != null
                ? WorldCameraManager.Instance.CurrentTrackingWeight : 1f;

            // ── [v4.0] 엣지 비발동 시 자석 억제 (유지) ───────────────────────────────
            CameraStancePresetSO stanceForMagPull = WorldCameraManager.Instance?.currentStanceSO;
            float edgePanMagMult = 1.0f;
            if (stanceForMagPull != null
                && stanceForMagPull.edgePanningData.useEdgePanning
                && !isEdgePanActive)
            {
                edgePanMagMult = 0.05f;
            }

            // ── [v4.1 수정] Lerp → SmoothDamp로 교체 ────────────────────────────────
            // 원인 3: Lerp는 진동을 감쇠하지 못함. SmoothDamp는 관성으로 자연스럽게 수렴.
            // smoothTime = 1 / (magneticPullSpeed * pullStrength) 의 역수 개념
            // pullStrength=1(최대 이탈)일 때 빠르게, 0.04(경계)일 때 천천히 당김
            float pullFactor = Mathf.Max(pullStrength, 0.001f);
            float magSmoothTime = 1f / Mathf.Max(magneticPullSpeed * pullFactor * trackingWeight * edgePanMagMult, 0.5f);
            // magSmoothTime: 0.2s(강한 당김) ~ 4s(약한 당김) 사이로 자동 조절

            leftAndRightLookAngle = Mathf.SmoothDampAngle(
                leftAndRightLookAngle, targetYaw,
                ref magneticYawVelocity, magSmoothTime);

            // ── [v4.1 수정] FocusPivot ACTIVE 시 Pitch 자석 개입 차단 ─────────────────
            // 원인 1: FocusPivot이 desiredPos를 밀면 dir이 변하고 targetPitch가 흔들림.
            //         이 진동이 Pitch 자석을 통해 upAndDownLookAngle에 전달되어 줌인/아웃처럼 보임.
            // 수정: FocusPivot이 빠르게 움직이는 동안은 Pitch 자석 개입을 차단합니다.
            //       Yaw는 좌우 추적에 필수이므로 유지합니다.
            // [v4.3] FocusPivot 이동 중 판정 임계값 조정
            // 기존 0.5(0.7m/s)는 너무 낮아 4.3m/s에서도 간헐적으로 Pitch 차단을 놓침.
            // 실제 안정 상태(desiredPos 수렴 완료)에서 velocity≈0이므로
            // 1.0(1.0m/s) 이상이면 이동 중으로 간주하여 확실히 차단합니다.
            // [v4.4] FocusPivot 이동 중 판정 — Yaw velocity 기반
            bool focusPivotMoving = Mathf.Abs(focusYawVelocity) > 2.0f;

            if (!focusPivotMoving)
            {
                upAndDownLookAngle = Mathf.SmoothDamp(
                    upAndDownLookAngle, targetPitch,
                    ref magneticPitchVelocity, magSmoothTime);
                upAndDownLookAngle = Mathf.Clamp(upAndDownLookAngle, minimumPivot, maximumPivot);
            }
            else
            {
                // FocusPivot 이동 중: Pitch velocity만 수렴 (각도는 고정)
                magneticPitchVelocity = Mathf.Lerp(magneticPitchVelocity, 0f, Time.deltaTime * 3f);
            }
            // ─────────────────────────────────────────────────────────────────────

            // [v4.3 루프C 차단] 이 함수 내부의 Yaw Clamp를 제거합니다.
            // HandleRotation에서 이미 currentDiff = Clamp(currentDiff, ±maxViewAngle)을 수행하고,
            // ApplyDynamicYawAndClamp에서 최종 렌더 Yaw를 다시 Clamp합니다.
            // 여기서 세 번째로 Clamp하면 각도가 매 프레임 playerBodyYaw 기준으로 재계산되면서
            // Magnetic의 SmoothDamp 결과가 덮어쓰여 잔류 오차가 발생합니다.
            // Clamp 책임은 ApplyDynamicYawAndClamp 단독으로 위임합니다.
            return Quaternion.Euler(upAndDownLookAngle, leftAndRightLookAngle, 0);
        }

        private void HandleCollision()
        {
            float rawZ = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.currentBaseOffset.z : -2.5f;

            // 🚨 [Phase 4 고도화] 이동량 조이스틱 입력 기반 동적 Z 오프셋 실시간 연산 적용
            if (WorldCameraManager.Instance != null)
            {
                rawZ += WorldCameraManager.Instance.dynamicZOffset;
            }

            debugRawZ = rawZ;
            float baseZ = -Mathf.Abs(rawZ);
            targetZPosition = baseZ;
            lastCollisionObjectName = "None";

            if (bypassCollisionForDebug)
            {
                lastCollisionObjectName = "[Bypass Collision]";
                float lSpeed = 15f * Time.deltaTime;
                float currZ = Mathf.Lerp(cameraObject.transform.localPosition.z, targetZPosition, lSpeed);
                float currX = Mathf.Lerp(cameraObject.transform.localPosition.x, 0f, lSpeed);
                float currY = Mathf.Lerp(cameraObject.transform.localPosition.y, 0f, lSpeed);
                cameraObject.transform.localPosition = new Vector3(currX, currY, currZ);
                debugActualZ = currZ;
                return;
            }

            Vector3 direction = -cameraPivotTransform.forward;
            LayerMask combinedLayers = collideWithLayers | characterCollisionLayers;
            float closestDistance = Mathf.Abs(baseZ);

            RaycastHit[] hits = Physics.SphereCastAll(cameraPivotTransform.position, cameraCollisionRadius, direction, Mathf.Abs(baseZ), combinedLayers);

            foreach (var h in hits)
            {
                if (h.collider.transform.root == player.transform.root) continue;

                float dist = Vector3.Distance(cameraPivotTransform.position, h.point);
                if (dist < closestDistance)
                {
                    closestDistance = dist;
                    lastCollisionObjectName = h.collider.gameObject.name;
                }
            }

            if (closestDistance < Mathf.Abs(baseZ))
            {
                targetZPosition = -(closestDistance - cameraCollisionRadius);
            }

            RaycastHit lineHit;
            if (Physics.Linecast(cameraPivotTransform.position, cameraPivotTransform.position + (direction * Mathf.Abs(baseZ)), out lineHit, combinedLayers))
            {
                if (lineHit.collider.transform.root != player.transform.root)
                {
                    float lineDist = Vector3.Distance(cameraPivotTransform.position, lineHit.point);
                    float lineTargetZ = -(lineDist - cameraCollisionRadius);
                    if (lineTargetZ > targetZPosition)
                    {
                        targetZPosition = lineTargetZ;
                        lastCollisionObjectName = lineHit.collider.gameObject.name + " (Linecast)";
                    }
                }
            }

            if (targetZPosition > -minimumCollisionOffset) targetZPosition = -minimumCollisionOffset;
            debugTargetZ = targetZPosition;

            float avoidanceFactor = 0f;
            if (targetZPosition > -avoidanceThreshold) avoidanceFactor = 1f - (Mathf.Abs(targetZPosition) / avoidanceThreshold);

            float currentXOffset = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.currentBaseOffset.x : 0.5f;
            currentXOffset += currentFramingOffsetX;
            float sideSign = currentXOffset >= 0f ? 1f : -1f;

            float targetX = bodyAvoidanceOffset.x * avoidanceFactor * sideSign;
            float targetY = bodyAvoidanceOffset.y * avoidanceFactor;

            float smoothTime = 0.08f;
            float currentZ = Mathf.SmoothDamp(cameraObject.transform.localPosition.z, targetZPosition, ref collisionZVelocity, smoothTime);
            float currentX = Mathf.SmoothDamp(cameraObject.transform.localPosition.x, targetX, ref collisionXVelocity, smoothTime);
            float currentY = Mathf.SmoothDamp(cameraObject.transform.localPosition.y, targetY, ref collisionYVelocity, smoothTime);

            cameraObject.transform.localPosition = new Vector3(currentX, currentY, currentZ);
            debugActualZ = currentZ;
        }

        private void ApplyFinalTransform(Quaternion baseRotation)
        {
            if (WorldCameraManager.Instance == null || cameraObject == null) return;

            // 🚨 [Phase 4 고도화] 이동량 조이스틱 입력 기반 동적 FOV 실시간 연산 적용
            float targetFov = WorldCameraManager.Instance.currentFOV + WorldCameraManager.Instance.dynamicFOVOffset;
            var handheld = WorldCameraManager.Instance.currentHandheldEffect;
            float zTilt = WorldCameraManager.Instance.currentZTilt;

            cameraObject.fieldOfView = targetFov;

            float staminaPerc = 1f;
            if (player.playerNetworkManager != null && player.playerNetworkManager.maxStamina.Value > 0)
                staminaPerc = (float)player.playerNetworkManager.currentStamina.Value / player.playerNetworkManager.maxStamina.Value;

            float exhaustionFactor = 1.0f;
            if (staminaPerc < 0.3f)
                exhaustionFactor += (0.3f - staminaPerc) * staminaExhaustionMultiplier;

            float swayAmount = handheld.enableHandheldEffect ? handheld.swayAmount : 0f;
            float swaySpeed = handheld.enableHandheldEffect ? handheld.swaySpeed : 1f;
            float bobbingAmount = handheld.enableHandheldEffect ? handheld.bobbingAmount : 0f;

            float swayX = (Mathf.PerlinNoise(Time.time * swaySpeed, 0) - 0.5f) * swayAmount * exhaustionFactor * bodycamWeight;
            float swayY = (Mathf.PerlinNoise(0, Time.time * swaySpeed) - 0.5f) * swayAmount * exhaustionFactor * bodycamWeight;
            float swayZ = (Mathf.PerlinNoise(Time.time * swaySpeed, Time.time * swaySpeed) - 0.5f) * (swayAmount * 0.5f) * exhaustionFactor * bodycamWeight;

            float moveAmount = player.playerNetworkManager != null ? player.playerNetworkManager.animatorMoveAmountMovement.Value : 0f;
            float bobSpeed = 12f;

            // ── [v4.1 버그 수정] Bobbing 계산 오류 2가지 ─────────────────────────────
            //
            // 버그 1 — bobY를 Pitch(x회전)에 빼고 있었음
            //   Quaternion.Euler(swayX - bobY, ...)에서 x = Pitch(상하 기울기)
            //   보빙은 "카메라 위치의 위아래 이동"이어야 하는데
            //   Pitch 회전으로 적용되면 "카메라가 발쪽을 바라보는 방향으로 꺾임"
            //   → 플레이어가 좌측에 있으면 Pitch+Yaw 합성으로 대각선 방향으로 꺾임 (증상 1)
            //   → 플레이어가 중앙에 있으면 발쪽으로 위아래로 꺾임 (증상 2)
            //
            // 버그 2 — Mathf.Abs(Mathf.Cos(...)) → 항상 양수(0~1)
            //   아래 방향으로만 당겨지고 위아래 대칭이 없음 → 발쪽 침강 현상
            //
            // 수정:
            //   bobX: Pitch 회전에서 제거 → 좌우 흔들림은 Y축 위치로
            //   bobY: cameraPivotTransform.localPosition의 Y 오프셋으로 이관
            //         (Pitch 회전 대신 카메라 피벗 위치가 위아래로 움직임)
            //   bobY 계산: Abs(Cos) → Sin 기반으로 교체하여 위아래 대칭 보빙
            // ─────────────────────────────────────────────────────────────────────────

            // [v4.2 버그 2 수정] bobX/bobY 모두 bobbingAmount(SO 핸드헬드 값)을 사용.
            // 기존에 movementBobbingAmount(SerializeField 고정값 1.2)를 직접 써서
            // enableHandheldEffect=false여도 Bobbing이 항상 발생하던 문제 수정.
            // bobbingAmount는 enableHandheldEffect=false이면 0이 들어오므로
            // 핸드헬드 꺼짐 시 bobX/bobY 모두 0이 됩니다.
            float bobX = Mathf.Sin(Time.time * bobSpeed)
                         * bobbingAmount * moveAmount * bodycamWeight;

            float bobY = Mathf.Sin(Time.time * bobSpeed * 2f)
                         * bobbingAmount * 0.5f * moveAmount * bodycamWeight;

            // bodycamRot: Pitch(X) 회전에서 bobY 완전 제거
            // swayX(핸드헬드 수전증 Pitch)만 남기고 bobY는 아래에서 위치로 처리
            Quaternion bodycamRot = Quaternion.Euler(
                swayX,                         // ← bobY 제거 (버그 수정)
                swayY + bobX * 0.5f,           // 좌우 흔들림은 소폭 Yaw에만
                swayZ + (bobX * 0.3f) + zTilt
            );

            // [v3.9] Perlin Noise 기반 Shake — 연속적이고 부드러운 진동, Decay 감쇠 적용
            Vector3 shakeOffset = Vector3.zero;
            if (shakeDuration > 0f)
            {
                float t = Time.time * 28f + shakeTimeOffset;
                float decayRatio = Mathf.Clamp01(shakeDuration / shakeMaxDuration);
                float currentIntensity = shakeIntensity * decayRatio;

                shakeOffset = new Vector3(
                    (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f * currentIntensity,
                    (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f * currentIntensity,
                    (Mathf.PerlinNoise(t * 0.7f, t * 1.3f) - 0.5f) * currentIntensity * 0.5f
                );

                shakeDuration -= Time.deltaTime;
                if (shakeDuration < 0f) shakeDuration = 0f;
            }

            transform.rotation = baseRotation * bodycamRot;

            // bobY를 cameraPivotTransform.localPosition.y 오프셋으로 적용
            // → 카메라 피벗 자체가 위아래로 이동 (Pitch 회전 없이 순수한 위치 보빙)
            // shakeOffset.y 와 합산하여 Shake와 Bobbing이 함께 작동
            Vector3 pivotOffset = shakeOffset;
            pivotOffset.y += bobY;
            cameraPivotTransform.localPosition = pivotOffset;
        }
        #endregion


        // =====================================================================
        // [버그수정] UpdateVirtualCursor — 커서 잠금 상태 대응 가상 커서 업데이트
        //
        // 원인: Unity에서 CursorLockMode.Locked이면 Input.mousePosition은 항상
        //       화면 중앙(Screen.width/2, Screen.height/2)을 반환합니다.
        //       게임뷰 포커스 = Locked 상태이므로 dbg_mouseViewport가 고정되었음.
        //
        // 해결:
        //   Locked 상태 → Input.GetAxis("Mouse X/Y") 델타를 virtualCursorPos에 누적
        //                 (델타는 Lock 상태에서도 실제 마우스 이동량을 정확히 반환)
        //   Unlocked 상태(Esc) → Input.mousePosition을 직접 뷰포트로 변환하여 동기화
        //                        (커서가 화면에 보이는 상태 = 실제 위치 그대로 사용)
        //
        // 결과: 게임뷰 포커스와 Esc 상태 모두에서 동일한 커서 위치 표시.
        // =====================================================================
        private void UpdateVirtualCursor()
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                // Locked: 델타 누적 방식으로 가상 위치 갱신
                float deltaX = Input.GetAxis("Mouse X");
                float deltaY = Input.GetAxis("Mouse Y");

                // 화면 해상도 기준 감도 보정 (해상도가 달라도 일정한 속도 유지)
                float sensX = virtualCursorSensX * (1920f / Mathf.Max(Screen.width, 1f));
                float sensY = virtualCursorSensY * (1080f / Mathf.Max(Screen.height, 1f));

                virtualCursorPos.x += deltaX * sensX;
                virtualCursorPos.y += deltaY * sensY;

                // 0~1 범위로 클램프 (화면 안으로 제한)
                virtualCursorPos.x = Mathf.Clamp01(virtualCursorPos.x);
                virtualCursorPos.y = Mathf.Clamp01(virtualCursorPos.y);
            }
            else
            {
                // Unlocked(Esc): 실제 마우스 위치를 뷰포트로 변환하여 동기화
                if (cameraObject != null)
                {
                    Vector3 vp = cameraObject.ScreenToViewportPoint(Input.mousePosition);
                    virtualCursorPos.x = vp.x;
                    virtualCursorPos.y = vp.y;
                }
            }
        }

        #region Targeting & Utilities
        public void HandleLocatingLockOnTargets()
        {
            float shortestDistance = Mathf.Infinity;
            float shortestDistanceOfRightTarget = Mathf.Infinity;
            float shortestDistantOfLeftTarget = -Mathf.Infinity;

            ClearLockOnTargets();

            Collider[] colliders = Physics.OverlapSphere(player.transform.position, lockOnRadius, WorldUtilityManager.Instance.GetCharacterLayers());

            for (int i = 0; i < colliders.Length; i++)
            {
                CharacterManager target = colliders[i].GetComponent<CharacterManager>();
                if (target == null || target.characterNetworkManager.isDead.Value || target.transform.root == player.transform.root) continue;
                if (target.characterCombatManager?.lockOnTransform == null) continue;

                Vector3 dirToTarget = target.transform.position - player.transform.position;
                dirToTarget.y = 0;
                Vector3 camForward = cameraObject.transform.forward;
                camForward.y = 0;

                float angle = Vector3.Angle(dirToTarget.normalized, camForward.normalized);

                if (angle <= maximumViewableAngle)
                {
                    if (Physics.Linecast(player.playerCombatManager.lockOnTransform.position, target.characterCombatManager.lockOnTransform.position, out RaycastHit hit, WorldUtilityManager.Instance.GetEnviroLayers()))
                        continue;

                    availableTargets.Add(target);
                }
            }

            foreach (var target in availableTargets)
            {
                float dist = Vector3.Distance(player.transform.position, target.transform.position);
                if (dist < shortestDistance)
                {
                    shortestDistance = dist;
                    nearestLockOnTarget = target;
                }

                if (player.playerNetworkManager.isLockedOn.Value)
                {
                    Vector3 relativePos = player.transform.InverseTransformPoint(target.transform.position);
                    if (target == player.playerCombatManager.currentTarget) continue;

                    if (relativePos.x <= 0 && relativePos.x > shortestDistantOfLeftTarget)
                    {
                        shortestDistantOfLeftTarget = relativePos.x;
                        leftLockOnTarget = target;
                    }
                    else if (relativePos.x >= 0 && relativePos.x < shortestDistanceOfRightTarget)
                    {
                        shortestDistanceOfRightTarget = relativePos.x;
                        rightLockOnTarget = target;
                    }
                }
            }
        }

        public void SwitchLockOnTarget(LockOnDirection direction)
        {
            if (direction == LockOnDirection.Left && leftLockOnTarget != null)
                player.playerCombatManager.SetTarget(leftLockOnTarget);
            else if (direction == LockOnDirection.Right && rightLockOnTarget != null)
                player.playerCombatManager.SetTarget(rightLockOnTarget);
        }

        public void ClearLockOnTargets()
        {
            nearestLockOnTarget = null;
            leftLockOnTarget = null;
            rightLockOnTarget = null;
            availableTargets.Clear();
        }

        public IEnumerator WaitThenFindNewTarget()
        {
            while (player.isPerformingAction)
            {
                yield return null;
            }

            ClearLockOnTargets();
            HandleLocatingLockOnTargets();

            if (nearestLockOnTarget != null)
            {
                player.playerCombatManager.SetTarget(nearestLockOnTarget);
                player.playerNetworkManager.isLockedOn.Value = true;
            }
        }

        public void SetLockOnTarget(Transform target)
        {
            player.playerCombatManager.SetTarget(target?.GetComponent<CharacterManager>());
        }

        public void SetContextualFocus(Transform target, CameraStancePresetSO preset)
        {
            currentFocusTarget = target;
            targetFOV = preset.fov;
            currentLerpSpeed = 5f;
            currentOffset = preset.baseOffset;
            isContextualMode = true;
        }

        public void ClearContextualFocus()
        {
            isContextualMode = false;
            currentFocusTarget = null;
            targetFOV = defaultFOV;

            leftAndRightLookAngle = transform.eulerAngles.y;

            float pitch = transform.eulerAngles.x;
            if (pitch > 180) pitch -= 360;
            upAndDownLookAngle = pitch;
        }

        /// <summary>[v3.9] Shake — intensity=0, duration=0으로 호출하면 즉시 초기화됩니다.</summary>
        public void Shake(float intensity, float duration)
        {
            shakeIntensity = intensity;
            shakeDuration = duration;
            shakeMaxDuration = Mathf.Max(duration, 0.001f); // 감쇠 곡선 계산용
        }

        public void SetBodycamWeight(float weight)
        {
            bodycamWeight = Mathf.Clamp01(weight);
        }

        // =====================================================================
        // [Implementation Spec 신규] GetCurrentAngles / SetAngles
        // WorldCameraManager가 Reflection 없이 직접 각도에 접근합니다.
        // =====================================================================

        /// <summary>
        /// 현재 카메라의 Yaw(좌우) / Pitch(상하) 각도를 반환합니다.
        /// WorldCameraManager.PlayCameraSequence()의 각도 스냅샷 저장에 사용합니다.
        /// </summary>
        public (float yaw, float pitch) GetCurrentAngles()
            => (leftAndRightLookAngle, upAndDownLookAngle);

        /// <summary>
        /// 카메라의 Yaw/Pitch 각도를 직접 설정합니다.
        /// 시퀀스 복귀·텔레포트·스냅 등에 사용합니다.
        /// </summary>
        public void SetAngles(float yaw, float pitch)
        {
            leftAndRightLookAngle = yaw;
            upAndDownLookAngle = Mathf.Clamp(pitch, minimumPivot, maximumPivot);
        }
        // =====================================================================

        // =========================================================================================
        // [v4.4 신규] 프레이밍 오프셋 외부 접근 메서드 — restorePreviousFraming 지원
        // =========================================================================================

        /// <summary>
        /// 현재 Dynamic Framing X 오프셋을 반환합니다.
        /// WorldCameraManager.PlayCameraSequence()에서 시퀀스 진입 시 스냅샷 저장에 사용됩니다.
        /// </summary>
        public float GetCurrentFramingOffset() => currentFramingOffsetX;

        /// <summary>
        /// Dynamic Framing X 오프셋을 지정값으로 복원합니다.
        /// blendTime=0이면 즉시 스냅.
        /// blendTime>0이면 HandleFollowTarget 내부에서 매 프레임 Lerp 보간합니다.
        /// </summary>
        public void SetFramingOffset(float offsetX, float blendTime = 0f)
        {
            if (blendTime < 0.001f)
            {
                currentFramingOffsetX = offsetX;
                framingVelocity = 0f;
                isPendingFramingRestore = false;
            }
            else
            {
                pendingFramingTarget = offsetX;
                pendingFramingBlendTime = blendTime;
                pendingFramingStartValue = currentFramingOffsetX;
                pendingFramingTimer = 0f;
                isPendingFramingRestore = true;
            }
        }
        // =========================================================================================

        /// <summary>
        /// [v3.9 수정] 공격(isPerformingAction) 중에는 카메라 Yaw 보정을 차단합니다.
        /// <br/>OnAnimatorMove()의 Turn 보정이 HandleRotation()의 bodyYaw 동결과 함께
        /// 이중으로 카메라를 꺾는 현상(루트모션 회전의 160% 전달)을 방지합니다.
        /// </summary>
        public void AdjustCameraYaw(float angleOffset)
        {
            // [v3.9] 공격 중 호출 차단 — isPerformingAction 동안 HandleRotation()이
            // 이미 bodyYaw 추적을 멈췄으므로 여기서 추가 누적하면 이중 꺾임이 발생한다.
            if (player != null && player.isPerformingAction) return;

            leftAndRightLookAngle += angleOffset;
        }

        /// <summary>
        /// [v3.9] 카메라 스탠스/시퀀스 전환 시 SmoothDamp velocity를 전부 초기화합니다.
        ///
        /// 문제: 락온 진입처럼 카메라 상태가 급격히 바뀔 때,
        ///       이전 프레임까지 누적된 cameraVelocity / framingVelocity 가
        ///       새로운 desiredPos 방향과 충돌하며 SmoothDamp가 폭발(5천만m 이탈)합니다.
        ///
        /// 해결: 전환 직전 velocity 전부 Vector3/float.zero 리셋.
        ///       WorldCameraManager.ApplyStanceInstantly() 에서 호출합니다.
        /// </summary>
        /// <summary>
        /// SmoothDamp velocity를 전부 초기화합니다.
        /// preserveFraming=true이면 currentFramingOffsetX를 초기화하지 않습니다.
        /// 시퀀스 복귀 시 공격 전 구도를 유지하기 위해 WorldCameraManager가 이 옵션을 사용합니다.
        /// </summary>
        public void ResetVelocities(bool preserveFraming = false)
        {
            // velocity ref 전부 초기화
            cameraVelocity = Vector3.zero;
            framingVelocity = 0f;
            focusPivotVelocity = Vector3.zero;
            focusYawVelocity = 0f;  // [v4.4]
            smoothedPlayerBodyYawVelocity = 0f; // [v4.4] 몸통 추적 velocity 리셋
            framingYawVelocity = 0f;
            playerYVelocity = 0f;
            dynamicHeightVelocity = 0f;
            collisionZVelocity = 0f;
            collisionXVelocity = 0f;
            collisionYVelocity = 0f;

            // [v3.9 Fix] framing 상태값도 중립으로 리셋
            // velocity만 0으로 해도 currentFramingOffsetX/YawOffset이 극단값에 있으면
            // 다음 SmoothDamp에서 초기 속도가 폭발하므로 상태값도 함께 초기화
            // [v4.4] preserveFraming=true이면 framing 수치를 유지 (공격 후 구도 복원용)
            // preserveFraming=false(기본)이면 기존과 동일하게 0으로 초기화
            if (!preserveFraming)
            {
                currentFramingOffsetX = 0f;
                currentFramingYawOffset = 0f;
                lastTargetFraming = 0f;
                lastTargetFramingYaw = 0f;
            }
            currentDynamicHeight = 0f;

            // [v4.0] 엣지 패닝 전용 velocity 및 상태 리셋
            isEdgePanActive = false;
            isEdgeReturning = false;
            hadEdgePanEverActive = false; // 락온 해제·스탠스 전환 시 복귀 이력 초기화
            isLockOnInitialFramingActive = false;
            lockOnInitialFramingTimer = 0f;
            isPendingFramingRestore = false; // [v4.4] 강제 리셋 시 복원 대기도 취소
            wasPerformingAction = false; // [v4.4] 공격 상태 추적 초기화
            isMagneticActive = false;
            magneticYawVelocity = 0f;
            magneticPitchVelocity = 0f;
            edgePanReturnTimer = 0f;
            edgePanReturnVelYaw = 0f;
            edgePanReturnVelPitch = 0f;
            smoothedEdgeInputX = 0f;
            smoothedEdgeInputY = 0f;
            edgePanInputVelX = 0f;
            edgePanInputVelY = 0f;
            virtualCursorPos = new Vector2(0.5f, 0.5f); // 가상 커서 중앙으로 리셋
        }

        private void OnDrawGizmos()
        {
            if (!showViewAngleGizmos || player == null) return;

            Gizmos.color = new Color(0.2f, 0.6f, 1.0f, 0.3f);
            Vector3 pivotPosition = cameraPivotTransform != null ? cameraPivotTransform.position : transform.position;

            Vector3 forward = player.transform.forward;
            Vector3 leftBoundary = Quaternion.Euler(0, -maxViewAngle, 0) * forward;
            Vector3 rightBoundary = Quaternion.Euler(0, maxViewAngle, 0) * forward;

            Gizmos.DrawRay(pivotPosition, forward * 5f);
            Gizmos.color = Color.red;
            Gizmos.DrawRay(pivotPosition, leftBoundary * 5f);
            Gizmos.DrawRay(pivotPosition, rightBoundary * 5f);
        }

        // =====================================================================
        // [v3.9] 온스크린 디버그 오버레이 (Game View 내 실시간 텍스트 정보)
        // showDebugLogs = true 일 때만 표시됩니다.
        // =====================================================================
        private void OnGUI()
        {
            if (!showDebugLogs || player == null) return;

            // ── 패널 스타일 ───────────────────────────────────────────────────
            GUIStyle panelBg = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(2, 2, new Color(0.05f, 0.05f, 0.12f, 0.82f)) }
            };
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.3f, 0.85f, 1f) }
            };
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            GUIStyle warnStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.4f, 0.3f) }
            };
            GUIStyle okStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.3f, 1f, 0.45f) }
            };
            GUIStyle neutralStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(1f, 0.85f, 0.25f) }
            };

            float panelW = 360f;
            float panelX = Screen.width - panelW - 8f;
            float y = 8f;
            float lineH = 19f; // 한글 폰트 높이 보정 (기존 16f → 밑부분 잘림)
            float sectionGap = 6f;

            // ── [1] 락온 & 카메라 앵글 ────────────────────────────────────────
            float blockH = lineH * 8 + sectionGap * 2 + 22f;
            GUI.Box(new Rect(panelX - 4, y - 4, panelW + 8, blockH), "", panelBg);

            GUI.Label(new Rect(panelX, y, panelW, lineH), "🎥  락온 & 카메라 앵글", headerStyle); y += lineH + 2;

            string lockStr = dbg_isLockedOn ? "  ✅ LOCKED ON" : "  ⬜ UNLOCKED";
            GUI.Label(new Rect(panelX, y, panelW, lineH), lockStr,
                dbg_isLockedOn ? okStyle : labelStyle); y += lineH;

            GUI.Label(new Rect(panelX, y, panelW, lineH),
                $"  상태: {dbg_lockOnState}", dbg_isTargetEscaped ? warnStyle : labelStyle); y += lineH;

            GUI.Label(new Rect(panelX, y, panelW, lineH),
                $"  Camera Yaw:  {dbg_leftAndRightLookAngle:F1}°   FinalRender: {dbg_finalRenderYaw:F1}°", labelStyle); y += lineH;

            GUI.Label(new Rect(panelX, y, panelW, lineH),
                $"  Player Body: {dbg_playerBodyYaw:F1}°   Cam-Body Δ: {dbg_bodyToCamDiff:F1}°", labelStyle); y += lineH;
            // [v4.4] 몸통 추적 설정 표시
            var stanceForDbgBody = WorldCameraManager.Instance?.currentStanceSO;
            if (stanceForDbgBody != null)
            {
                float dbgW = stanceForDbgBody.bodyYawTracking.bodyYawFollowWeight;
                float dbgST = stanceForDbgBody.bodyYawTracking.bodyYawFollowSmoothTime;
                bool lockOnStartDbg = stanceForDbgBody.bodyYawTracking.lockCameraOnActionStart;
                float mouseInflDbg = stanceForDbgBody.bodyYawTracking.lockCameraMouseInfluence;
                bool isActuallyLocked = player != null && player.isPerformingAction
                    && (lockOnStartDbg || dbgW < 0.99f);
                if (dbgW < 0.999f || dbgST > 0.005f || lockOnStartDbg)
                {
                    string bodyLockStr = isActuallyLocked
                        ? $"  🔒 카메라 고정 (snap={actionStartYaw:F1}°  mouse×{mouseInflDbg:F2})"
                        : (lockOnStartDbg ? "  ⚡ lockOnStart=ON (대기 중)" : "");
                    GUI.Label(new Rect(panelX, y, panelW, lineH),
                        $"  BodyYaw: w={dbgW:F2} sm={dbgST:F2}s lockStart={lockOnStartDbg}{bodyLockStr}",
                        warnStyle); y += lineH;
                }
            }

            GUI.Label(new Rect(panelX, y, panelW, lineH),
                $"  TrackingWeight: {dbg_trackingWeight:F3}   NoInputTimer: {dbg_noInputTimer:F1}s", labelStyle); y += lineH;

            string yawFrozenStr = dbg_yawFrozen ? "  🔒 YAW FROZEN" : "  🔓 Yaw Free";
            string strafeStr = dbg_isStrafeRight ? " | D키 Strafe 중" : "";
            GUI.Label(new Rect(panelX, y, panelW, lineH),
                yawFrozenStr + strafeStr,
                dbg_yawFrozen ? neutralStyle : labelStyle); y += lineH;

            if (dbg_isLockedOn && player.playerCombatManager?.currentTarget != null)
            {
                GUI.Label(new Rect(panelX, y, panelW, lineH),
                    $"  타겟 거리: {dbg_targetDist:F1}m   뷰포트XY: ({dbg_targetViewportPos.x:F2}, {dbg_targetViewportPos.y:F2})",
                    dbg_isTargetEscaped ? warnStyle : okStyle); y += lineH;
            }

            y += sectionGap;

            // ── [2] 자석 소프트락 상태 ────────────────────────────────────────
            blockH = lineH * 3 + sectionGap + 22f;
            GUI.Box(new Rect(panelX - 4, y - 4, panelW + 8, blockH), "", panelBg);
            GUI.Label(new Rect(panelX, y, panelW, lineH), "🧲  MagneticSoftLock", headerStyle); y += lineH + 2;
            GUI.Label(new Rect(panelX, y, panelW, lineH),
                $"  {dbg_magneticState}",
                dbg_isMagneticActive ? neutralStyle : labelStyle); y += lineH;
            GUI.Label(new Rect(panelX, y, panelW, lineH),
                $"  PullStrength: {dbg_magneticPullStrength:F3}" +
                $"  YawVel:{magneticYawVelocity:F2}  PitchVel:{magneticPitchVelocity:F2}",
                labelStyle); y += lineH;
            bool fpMoving = focusPivotVelocity.sqrMagnitude > 0.5f;
            if (fpMoving)
            {
                GUI.Label(new Rect(panelX, y, panelW, lineH),
                    "  ⚠ FocusPivot 이동 중 — Pitch 자석 차단",
                    warnStyle); y += lineH;
            }
            y += sectionGap;

            // ── [3] 다이내믹 프레이밍 ─────────────────────────────────────────
            blockH = lineH * 7 + sectionGap + 22f;
            GUI.Box(new Rect(panelX - 4, y - 4, panelW + 8, blockH), "", panelBg);
            GUI.Label(new Rect(panelX, y, panelW, lineH), "🖼️  다이내믹 프레이밍 & 포커싱", headerStyle); y += lineH + 2;
            GUI.Label(new Rect(panelX, y, panelW, lineH),
                $"  framingState: {dbg_framingState}", labelStyle); y += lineH;
            GUI.Label(new Rect(panelX, y, panelW, lineH),
                $"  offsetX: {dbg_currentFramingOffsetX:F3}   yawOffset: {dbg_currentFramingYawOffset:F2}°", labelStyle); y += lineH;

            if (WorldCameraManager.Instance != null)
            {
                Vector3 baseOff = WorldCameraManager.Instance.currentBaseOffset;
                float totalX = baseOff.x + dbg_currentFramingOffsetX;
                GUI.Label(new Rect(panelX, y, panelW, lineH),
                    $"  baseOff.x:{baseOff.x:F2} + framing:{dbg_currentFramingOffsetX:F3} = {totalX:F3}", labelStyle); y += lineH;
            }

            // [Implementation Spec] Hysteresis 이탈 상태
            string hysteresisStr = isEscapedStable
                ? $"  Hysteresis: ESCAPED (hold:{escapeHoldTimer:F2}s)"
                : "  Hysteresis: IN_BOUNDS";
            GUI.Label(new Rect(panelX, y, panelW, lineH),
                hysteresisStr,
                isEscapedStable ? warnStyle : labelStyle); y += lineH;

            // [Implementation Spec] 다중 타겟 포커싱 상태
            // [v4.4] FocusPivot은 이제 Yaw velocity 기준으로 표시
            bool focusYawActive = Mathf.Abs(focusYawVelocity) > 0.1f;
            string focusStr = focusYawActive
                ? $"  FocusPivot: ACTIVE (yawVel:{focusYawVelocity:F2}°/s)"
                : "  FocusPivot: IDLE";
            GUI.Label(new Rect(panelX, y, panelW, lineH), focusStr, labelStyle); y += lineH;

            // [Implementation Spec] 락온 중 FocusTargets 해석 개수
            if (WorldCameraManager.Instance != null)
            {
                int ftCount = WorldCameraManager.Instance.currentFocusTargets != null
                              ? WorldCameraManager.Instance.currentFocusTargets.Length : 0;
                GUI.Label(new Rect(panelX, y, panelW, lineH),
                    $"  ResolvedFocusTargets: {ftCount}개", labelStyle); y += lineH;
            }
            y += sectionGap;

            // ── [4] 엣지패닝 — 마우스 위치 시각화 ─────────────────────────────
            blockH = lineH * 4 + 80f + sectionGap + 22f;
            GUI.Box(new Rect(panelX - 4, y - 4, panelW + 8, blockH), "", panelBg);
            GUI.Label(new Rect(panelX, y, panelW, lineH), "🖱️  엣지패닝 — 마우스 위치", headerStyle); y += lineH + 2;
            GUI.Label(new Rect(panelX, y, panelW, lineH),
                $"  마우스 뷰포트: ({dbg_mouseViewport.x:F3}, {dbg_mouseViewport.y:F3})   EdgeThreshold: {dbg_edgeThreshold:F3}", labelStyle); y += lineH;

            string edgeXStr = dbg_mouseAtEdgeX ? "  EdgeX: ✅ 발동" : "  EdgeX: ── 비발동";
            string edgeYStr = dbg_mouseAtEdgeY ? "  EdgeY: ✅ 발동" : "  EdgeY: ── 비발동";
            GUI.Label(new Rect(panelX, y, panelW, lineH),
                edgeXStr + "   " + edgeYStr,
                (dbg_mouseAtEdgeX || dbg_mouseAtEdgeY) ? okStyle : labelStyle); y += lineH;

            // [v4.0] 엣지패닝 상태 추가 디버그
            string epActiveStr = isEdgePanActive
                ? "  ▶ EdgePan ACTIVE  (카메라 이동 중)"
                : isEdgeReturning
                    ? "  ↩ EdgePan RETURNING (원래 시점 복귀 중)"
                    : hadEdgePanEverActive
                        ? "  ■ EdgePan INACTIVE (잠금 / 진입 이력 있음)"
                        : "  ■ EdgePan INACTIVE (잠금 / 미진입)";
            GUI.Label(new Rect(panelX, y, panelW, lineH), epActiveStr,
                isEdgePanActive ? okStyle : isEdgeReturning ? warnStyle : neutralStyle); y += lineH;

            // 복귀중 표시: isEdgeReturning 플래그에만 의존 (타이머 조건 이중 체크 제거)
            // 이전에 타이머 조건(!isEdgePanActive && timer < returnTime)을 따로 썼더니
            // isEdgeReturning=false여도 타이머가 남아있으면 항상 "복귀 중" 표시됐던 문제 수정
            if (isEdgeReturning)
            {
                float epRT = WorldCameraManager.Instance?.currentStanceSO?.edgePanningData.edgePanReturnTime ?? 0.8f;
                GUI.Label(new Rect(panelX, y, panelW, lineH),
                    $"  ↩ 복귀 중... ({edgePanReturnTimer:F2}s / {epRT:F2}s)",
                    warnStyle); y += lineH;
            }
            GUI.Label(new Rect(panelX, y, panelW, lineH),
                $"  smoothedInput: ({smoothedEdgeInputX:F3}, {smoothedEdgeInputY:F3})", labelStyle); y += lineH;

            // 미니 마우스 위치 레이더 (72×72 px)
            float radarSize = 72f;
            float radarX = panelX;
            float radarY = y + 2f;
            GUI.Box(new Rect(radarX, radarY, radarSize, radarSize), "");

            // 외곽 엣지 영역 표시 (threshold 비율로)
            float thPx = dbg_edgeThreshold * radarSize;
            GUI.color = new Color(1f, 0.3f, 0.3f, 0.25f);
            GUI.DrawTexture(new Rect(radarX, radarY, radarSize, thPx), Texture2D.whiteTexture);         // 상단
            GUI.DrawTexture(new Rect(radarX, radarY + radarSize - thPx, radarSize, thPx), Texture2D.whiteTexture); // 하단
            GUI.DrawTexture(new Rect(radarX, radarY, thPx, radarSize), Texture2D.whiteTexture);         // 좌측
            GUI.DrawTexture(new Rect(radarX + radarSize - thPx, radarY, thPx, radarSize), Texture2D.whiteTexture); // 우측
            GUI.color = Color.white;

            // 마우스 커서 도트
            float dotX = radarX + dbg_mouseViewport.x * radarSize - 3f;
            float dotY = radarY + (1f - dbg_mouseViewport.y) * radarSize - 3f; // Y축 반전
            GUI.color = (dbg_mouseAtEdgeX || dbg_mouseAtEdgeY) ? Color.green : Color.cyan;
            GUI.DrawTexture(new Rect(dotX, dotY, 6f, 6f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(radarX + radarSize + 6f, radarY, 200f, lineH * 4f),
                "■ 빨간영역: 엣지 발동 구간\n● 도트: 현재 커서 위치\n(초록: 엣지 발동 중)\n(청색: 일반 구역)",
                new GUIStyle(labelStyle) { wordWrap = true });

            y += radarSize + 8f + sectionGap;

            // ── [5] 충돌 & Z-Distance ─────────────────────────────────────────
            blockH = lineH * 4 + sectionGap + 22f;
            GUI.Box(new Rect(panelX - 4, y - 4, panelW + 8, blockH), "", panelBg);
            GUI.Label(new Rect(panelX, y, panelW, lineH), "📏  카메라 Z 거리 & 충돌", headerStyle); y += lineH + 2;
            GUI.Label(new Rect(panelX, y, panelW, lineH),
                $"  Raw: {debugRawZ:F3}   Target: {debugTargetZ:F3}   Actual: {debugActualZ:F3}", labelStyle); y += lineH;
            GUI.Label(new Rect(panelX, y, panelW, lineH),
                $"  충돌 오브젝트: {lastCollisionObjectName}", labelStyle); y += lineH;
            GUI.Label(new Rect(panelX, y, panelW, lineH),
                $"  Caller: {lastCallerReason}", labelStyle); y += lineH;
            y += sectionGap;

            // ── [6] SO 정보 ───────────────────────────────────────────────────
            if (WorldCameraManager.Instance != null)
            {
                blockH = lineH * 5 + sectionGap + 22f;
                GUI.Box(new Rect(panelX - 4, y - 4, panelW + 8, blockH), "", panelBg);
                GUI.Label(new Rect(panelX, y, panelW, lineH), "📋  현재 SO 파라미터", headerStyle); y += lineH + 2;

                var wm = WorldCameraManager.Instance;
                string seqName = wm.currentSequenceSO != null ? wm.currentSequenceSO.name : "None";
                string stanceName = wm.currentStanceSO != null ? wm.currentStanceSO.name : "None";
                GUI.Label(new Rect(panelX, y, panelW, lineH), $"  Sequence: {seqName}", labelStyle); y += lineH;
                GUI.Label(new Rect(panelX, y, panelW, lineH), $"  Stance:   {stanceName}", labelStyle); y += lineH;
                GUI.Label(new Rect(panelX, y, panelW, lineH),
                    $"  FOV: {wm.currentFOV:F1}°   baseOffset: ({wm.currentBaseOffset.x:F2}, {wm.currentBaseOffset.y:F2}, {wm.currentBaseOffset.z:F2})", labelStyle); y += lineH;
                GUI.Label(new Rect(panelX, y, panelW, lineH),
                    $"  baseYawOffset: {wm.currentBaseYawOffset:F1}°   TrackingW: {wm.CurrentTrackingWeight:F3}", labelStyle); y += lineH;
                GUI.Label(new Rect(panelX, y, panelW, lineH),
                    $"  SeqPlaying: {wm.IsSequencePlaying}   RestoreAngle: {wm.shouldRestorePreviousAngle}", labelStyle); y += lineH;
            }
        }

        // 단색 텍스처 생성 유틸리티
        private static Texture2D MakeTex(int w, int h, Color col)
        {
            Color[] pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            Texture2D tex = new Texture2D(w, h);
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }
        // =====================================================================
        #endregion
    }
}