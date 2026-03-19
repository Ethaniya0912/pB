using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TDA.World;
using TDA.Cameras; // CameraStancePresetSO 인식
using TDA.Core.Events; // 파라미터 해시 접근

#if UNITY_EDITOR
using UnityEditor;
using System.Reflection;
#endif

namespace TDA.Character.Player
{
    /// <summary>
    /// [오류 정정 및 숄더뷰 고도화] 기존의 타겟팅 탐색, 충돌 라인캐스트, NGO 방어 구조를 완벽히 보존하며
    /// 다크 판타지 특유의 답답하고 묵직한 숄더뷰 조작감(시야각 클램핑, 오토 센터링)을 추가로 구현했습니다.
    /// </summary>
    public class PlayerCamera : MonoBehaviour
    {
        [Header("References")]
        public Camera cameraObject;
        public PlayerManager player;
        [SerializeField] private Transform cameraPivotTransform;

        // [P0-03] 마우스 제스처 연동 캐싱
        private PlayerGestureManager playerGestureManager;

        [Header("Camera Distance Settings")]
        [Tooltip("평상시 카메라와 캐릭터 사이의 기본 거리입니다. 값이 클수록 카메라가 캐릭터 등 뒤로 멀어집니다. (권장: 2.5 ~ 4.0)")]
        [SerializeField] private float defaultCameraDistance = 2.5f;

        [Header("Rotation Settings")]
        [SerializeField] private float mouseSensitivity = 1.0f;
        [SerializeField] private float leftAndRightRotationSpeed = 220f;
        [SerializeField] private float upAndDownRotationSpeed = 220f;
        [SerializeField] private float minimumPivot = -30f;
        [SerializeField] private float maximumPivot = 60f;

        // =========================================================================================
        // [강화] 카메라 묵직함 보간 (Camera Smoothing)
        // =========================================================================================
        [Header("Camera Feel Settings (P3)")]
        [Tooltip("마우스 회전 시 카메라가 따라오는 부드러움(묵직함) 정도입니다. 값이 클수록 무겁게 끌려옵니다.")]
        [SerializeField] private float cameraRotationDampTime = 0.05f;

        // 내부 연산을 위한 타겟 각도 및 댐핑 속도 추적 변수
        private float currentTargetLeftAndRightAngle;
        private float currentTargetUpAndDownAngle;
        private float leftRightRotationVelocity;
        private float upDownRotationVelocity;
        // =========================================================================================

        [Header("Shoulder View Restrictions (P2)")]
        [Tooltip("카메라가 캐릭터 정면을 기준으로 좌우로 단독 회전할 수 있는 최대 한계각 (데드존). 사람의 자연스러운 시야각인 50~60도를 권장합니다.")]
        [SerializeField][Range(10f, 90f)] private float maxViewAngle = 60f;

        [Tooltip("캐릭터가 전진(이동)을 시작할 때, 카메라가 캐릭터의 등 뒤 정면으로 자동으로 부드럽게 돌아올지 여부입니다.")]
        [SerializeField] private bool enableAutoCenterOnMove = true;

        [Tooltip("대기(Idle) 상태에서 마우스 입력이 없을 때 카메라가 정면으로 복귀할지 여부입니다.")]
        [SerializeField] private bool enableAutoCenterOnIdle = true;

        [Tooltip("대기 상태에서 카메라가 정면으로 복귀하기 전 기다리는 시간(초)입니다.")]
        [SerializeField] private float idleAutoCenterDelay = 5.0f;

        [Tooltip("자동 정렬 시 카메라가 등 뒤로 복귀하는 속도입니다. 값이 클수록 빠르게 정렬됩니다.")]
        [SerializeField] private float autoCenterSpeed = 2.0f;

        private float noInputTimer = 0f;

        [Header("Debug & Gizmos")]
        [Tooltip("씬 뷰에서 카메라의 허용 시야각 부채꼴을 시각적으로 렌더링할지 여부입니다.")]
        [SerializeField] private bool showViewAngleGizmos = true;
        [Tooltip("체크 시 콘솔(Debug.Log)에 5초마다 카메라 좌표 연산의 핵심 인수들을 출력합니다.")]
        public bool showDebugLogs = true;

        [Tooltip("체크 시 카메라가 벽이나 바닥에 부딪히는 충돌 로직을 강제로 무시합니다. (Z축 거리 조절이 바닥에 막혀 안될 때 테스트용)")]
        public bool bypassCollisionForDebug = false;
        private float debugLogTimer = 0f;

        // =========================================================================================
        // [강화] SO 상태 추적 및 호출 원인(Caller) 분석용 변수들
        // =========================================================================================
        private GameState lastTrackedGameState;
        private bool lastTrackedLockOn;
        private int lastTrackedActionState;
        private bool lastTrackedSequencePlaying;
        private string lastTrackedSeqName = "None";
        private string lastTrackedStanceName = "None";

        [HideInInspector] public string lastCallerReason = "System Init (최초 실행)";

        // =========================================================================================
        // 🚨 [초정밀 디버깅] Z축 연산 추적 및 충돌체 식별 변수 (인스펙터 OSD 렌더링용)
        // =========================================================================================
        [HideInInspector] public float debugRawZ = 0f;
        [HideInInspector] public float debugTargetZ = 0f;
        [HideInInspector] public float debugActualZ = 0f;
        [HideInInspector] public string lastCollisionObjectName = "None";

        [Header("Bodycam Frame Decoupling (P2)")]
        [Tooltip("카메라의 시각적 업데이트 프레임을 강제로 낮춥니다. (예: 24, 30). 거친 바디캠 느낌을 줍니다.")]
        [SerializeField][Range(1f, 60f)] private float targetCameraFPS = 24f;
        [Tooltip("프레임 다운샘플링 기능을 켤지 여부입니다.")]
        [SerializeField] private bool enableFrameDecoupling = true;

        private float frameTimer = 0f;

        // 부드러운 백엔드 연산을 보존하기 위한 연속 상태(Continuous) 캐싱 변수
        private Vector3 continuousPosition;
        private Quaternion continuousRotation;
        private Vector3 continuousCameraLocalPos;
        private Vector3 continuousPivotLocalPos;

        // 유저 눈에 보일 툭툭 끊기는 프론트엔드(Stepped) 캐싱 변수
        private Vector3 steppedPosition;
        private Quaternion steppedRotation;
        private Vector3 steppedCameraLocalPos;
        private Vector3 steppedPivotLocalPos;
        private bool isFirstFrame = true;

        [Header("Soft Magnetic Lock (P2)")]
        [SerializeField] private Vector2 deadzoneRadius = new Vector2(0.25f, 0.25f);
        [SerializeField] private float magneticPullSpeed = 5.0f;

        // =========================================================================================
        // [강화] 핸드헬드 및 바디캠 효과 (Handheld & Bobbing)
        // =========================================================================================
        [Header("Handheld & Bodycam Effect (P3)")]
        [Tooltip("핸드헬드 카메라 특유의 멈춰있을 때 호흡 및 수전증(Sway) 강도입니다.")]
        [SerializeField] private float handheldSwayAmount = 0.5f;
        [Tooltip("핸드헬드 흔들림의 속도입니다.")]
        [SerializeField] private float handheldSwaySpeed = 1.0f;
        [Tooltip("이동할 때 카메라가 상하좌우로 덜컹거리는 보빙(Bobbing) 강도입니다.")]
        [SerializeField] private float movementBobbingAmount = 1.2f;
        [Tooltip("스태미나 고갈 시 흔들림 증폭 배수입니다.")]
        [SerializeField] private float staminaExhaustionMultiplier = 2.5f;
        // =========================================================================================

        [Header("Collision & Follow (Anti-Clipping)")]
        [SerializeField] private float cameraSmoothSpeed = 0.1f;
        [Tooltip("카메라 충돌체의 두께입니다. 클수록 벽에서 더 일찍 멈춰 뚫림을 방지합니다.")]
        [SerializeField] private float cameraCollisionRadius = 0.25f;
        [Tooltip("카메라가 장애물에 밀려 다가올 때, 기본적으로 허용되는 최소 Z 보장 거리입니다.")]
        [SerializeField] private float minimumCollisionOffset = 0.2f;

        [Tooltip("벽에 밀려 캐릭터에 가까워질 때, 머리를 뚫지 않도록 옆(X)과 위(Y)로 빗겨나는 최대 회피 거리입니다.")]
        [SerializeField] private Vector2 bodyAvoidanceOffset = new Vector2(0.4f, 0.2f);
        [Tooltip("카메라가 이 Z거리보다 캐릭터에 가까워지면 회피 기동(옆으로 빼기)을 시작합니다.")]
        [SerializeField] private float avoidanceThreshold = 1.2f;

        [Tooltip("벽, 지형, 건물 등 환경 충돌을 감지할 레이어입니다.")]
        [SerializeField] private LayerMask collideWithLayers;
        [Tooltip("다른 캐릭터나 적을 통과하지 않도록 하는 캐릭터 레이어입니다. (플레이어 본인은 코드에서 자동 제외됨)")]
        [SerializeField] private LayerMask characterCollisionLayers;

        [Header("Camera Height Settings")]
        [SerializeField] private float unlockedCameraHeight = 1.65f;
        [SerializeField] private float lockedCameraHeight = 2.0f;

        // [버그 수정] 흔들림 누적 방지용 베이스 높이 변수 추가
        private float currentPivotHeight;

        private float cameraHorizontalInput;
        public float CameraHorizontalInput => cameraHorizontalInput; // 외부(Locomotion)에서 마우스 델타를 읽기 위한 프로퍼티
        private float cameraVerticalInput;
        private float leftAndRightLookAngle;
        private float upAndDownLookAngle;
        private Vector3 cameraVelocity;

        private float defaultFOV = 60f;
        private float targetFOV;
        private float currentLerpSpeed = 5f;
        private float defaultZPosition;
        private float targetZPosition;

        private bool isContextualMode = false;
        private Transform currentFocusTarget;
        private Vector3 currentOffset;
        private float bodycamWeight = 0.1f;
        private float shakeIntensity = 0f;
        private float shakeDuration = 0f;

        [Header("Targeting & Search")]
        [SerializeField] private float lockOnRadius = 20f;
        [SerializeField] private float maximumViewableAngle = 50f;
        public CharacterManager nearestLockOnTarget;
        public CharacterManager leftLockOnTarget;
        public CharacterManager rightLockOnTarget;
        private List<CharacterManager> availableTargets = new List<CharacterManager>();
        private Coroutine cameraHeightCoroutine;

        // =========================================================================================
        // 🚨 [1순위 Core Physics] 동적 프레이밍 상태 유지를 위한 전역 변수
        // =========================================================================================
        private float currentFramingOffsetX = 0f;
        private float framingVelocity = 0f;
        private float lastTargetFraming = 0f; // 멈춰도 시야를 유지하기 위한 캐싱 변수

        private Vector3 dbgMassCenter;
        private Vector3 dbgDesiredPos;

        private void Awake()
        {
            if (cameraObject != null)
            {
                defaultFOV = cameraObject.fieldOfView;
                targetFOV = defaultFOV;

                // 인스펙터에서 설정한 카메라 거리를 무조건 등 뒤(음수)로 적용합니다.
                defaultZPosition = -Mathf.Abs(defaultCameraDistance);

                // 오브젝트가 렌즈를 파먹는 현상(근거리 클리핑)을 원천 차단하기 위해 값을 강제로 가장 작게 낮춥니다.
                cameraObject.nearClipPlane = 0.01f;
            }

            // 시작할 때 기준 높이를 초기화합니다.
            currentPivotHeight = unlockedCameraHeight;
        }

        private void Start()
        {
            DontDestroyOnLoad(gameObject);
            if (WorldCameraManager.Instance != null)
            {
                WorldCameraManager.Instance.SetLocalCamera(this);
            }

            // 프레임 디커플링 초기 상태 캐싱
            continuousPosition = transform.position;
            continuousRotation = transform.rotation;
            if (cameraObject != null) continuousCameraLocalPos = cameraObject.transform.localPosition;
            if (cameraPivotTransform != null) continuousPivotLocalPos = cameraPivotTransform.localPosition;

            steppedPosition = continuousPosition;
            steppedRotation = continuousRotation;
            steppedCameraLocalPos = continuousCameraLocalPos;
            steppedPivotLocalPos = continuousPivotLocalPos;

            // 보간용 타겟 각도 초기화
            currentTargetLeftAndRightAngle = leftAndRightLookAngle = transform.eulerAngles.y;
            float pitch = transform.eulerAngles.x;
            if (pitch > 180) pitch -= 360;
            currentTargetUpAndDownAngle = upAndDownLookAngle = pitch;

            if (WorldGameStateManager.Instance != null) lastTrackedGameState = WorldGameStateManager.Instance.currentState;
        }

        private void LateUpdate()
        {
            if (player == null) return;

            // [프레임 디커플링] 1. 정확한 연산을 위해, 이전 프레임에서 계산된 '부드러운 연속 좌표'로 카메라를 일시 복구합니다.
            if (enableFrameDecoupling && !isFirstFrame)
            {
                transform.position = continuousPosition;
                transform.rotation = continuousRotation;
                if (cameraObject != null) cameraObject.transform.localPosition = continuousCameraLocalPos;
                if (cameraPivotTransform != null) cameraPivotTransform.localPosition = continuousPivotLocalPos;
            }

            HandleFollowTarget();

            // 1. 순수 입력 기반 숄더뷰 회전 (클램핑 포함)
            Quaternion baseRotation = HandleRotation();

            // 2. 네트워크 락온 오버라이드 (자석 효과 우선)
            baseRotation = HandleMagneticSoftLock(baseRotation);

            // 3. 다중 방어 카메라 충돌 처리 (벽 관통 방지 및 머리 메쉬 뚫림 회피 기동 포함)
            HandleCollision();

            ApplyFinalTransform(baseRotation);

            // =====================================================================
            // 디버그 로그 출력 및 호출 원인(Caller) 스마트 분석
            // =====================================================================
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

                // 상태나 데이터 변화가 감지되었을 때 Caller 추적
                if (stateChanged || soChanged || (currentSeqPlaying && !lastTrackedSequencePlaying))
                {
                    string newReason = "";

                    // 우선순위에 따른 호출 원인(Caller) 판별
                    if (currentActionState != lastTrackedActionState && currentActionState != 0)
                    {
                        newReason = $"애니메이션 액션 트리거 (ActionID: {currentActionState})";
                    }
                    else if (currentLockOn != lastTrackedLockOn)
                    {
                        newReason = currentLockOn ? "락온(Lock-On) 활성화" : "락온(Lock-On) 해제";
                    }
                    else if (currentGameState != lastTrackedGameState)
                    {
                        newReason = $"월드 상태(GameState) 변경 ➔ {currentGameState}";
                    }
                    else if (currentSeqPlaying && !lastTrackedSequencePlaying)
                    {
                        newReason = "스크립트 직접 호출 (PlayCameraSequence)";
                    }
                    else if (soChanged)
                    {
                        if (currentSeqPlaying) newReason = "시퀀스 내부 타임라인 진행 (Next Step)";
                        else newReason = "스크립트 다이렉트 스탠스 적용 (ApplyStanceInstantly)";
                    }

                    if (!string.IsNullOrEmpty(newReason))
                    {
                        lastCallerReason = newReason;
                    }

                    // SO 변화가 실제로 있을 때만 (프레임 렉 스팸 방지) 로그를 한 번 띄워줍니다.
                    if (soChanged || (currentSeqPlaying && !lastTrackedSequencePlaying))
                    {
                        Debug.Log($"<color=magenta>[Camera SO Change Detected]</color>\n" +
                                  $"새로운 카메라 Sequence / Stance SO 데이터 덮어쓰기가 감지되었습니다!\n" +
                                  $"• 호출 원인(Caller): <color=lime>{lastCallerReason}</color>\n" +
                                  $"• World State: {lastTrackedGameState} ➔ <color=yellow>{currentGameState}</color>\n" +
                                  $"• Lock-On: {lastTrackedLockOn} ➔ <color=yellow>{currentLockOn}</color>\n" +
                                  $"• Action ID: {lastTrackedActionState} ➔ <color=yellow>{currentActionState}</color>\n" +
                                  $"• Sequence Playing: {currentSeqPlaying}");
                    }

                    lastTrackedGameState = currentGameState;
                    lastTrackedLockOn = currentLockOn;
                    lastTrackedActionState = currentActionState;
                    lastTrackedSeqName = currentSeqName;
                    lastTrackedStanceName = currentStanceName;
                }
                lastTrackedSequencePlaying = currentSeqPlaying;

                debugLogTimer += Time.deltaTime;
                if (debugLogTimer >= 5.0f)
                {
                    debugLogTimer = 0f;
                    Vector3 soBaseOffset = WorldCameraManager.Instance.currentBaseOffset;
                    float soYawOffset = WorldCameraManager.Instance.currentYawOffset;

                    string targetName = currentLockOn && player.playerCombatManager.currentTarget != null ? player.playerCombatManager.currentTarget.name : "없음";

                    Debug.Log($"<color=cyan>[Camera Coord Debug]</color>\n" +
                              $"★ 현재 제어 상태 ➔ World: <color=yellow>{currentGameState}</color> | LockOn: <color=yellow>{currentLockOn}</color> (타겟: <color=yellow>{targetName}</color>) | ActionID: <color=yellow>{currentActionState}</color> | 시퀀스 재생중: <color=yellow>{currentSeqPlaying}</color>\n" +
                              $"▶ <b>최근 호출 원인(Caller):</b> <color=lime>{lastCallerReason}</color>\n" +
                              $"▶ <b>현재 사용중인 Sequence SO:</b> <color=lime>{currentSeqName}</color>\n" +
                              $"▶ <b>현재 사용중인 Stance SO:</b> <color=lime>{currentStanceName}</color>\n" +
                              $"1. SO Base Offset (적용값): {soBaseOffset} | YawOffset: {soYawOffset}\n" +
                              $"2. 계산된 질량중심(Pivot): {dbgMassCenter}\n" +
                              $"3. 계산된 최종 타겟(DesiredPos): {dbgDesiredPos}\n" +
                              $"4. 메인 렌즈 Local Z(Collision): {targetZPosition:F2} (원인 물체: {lastCollisionObjectName})\n" +
                              $"※ Z값이 0보다 크면 렌즈가 몸 안으로 파묻힙니다. SO 에셋의 Z 오프셋을 음수로 설정하세요.");
                }
            }

            // [프레임 디커플링] 2. 연산이 완료된 부드러운 최종 결과값을 '연속 상태(Continuous)'로 저장합니다.
            if (enableFrameDecoupling)
            {
                continuousPosition = transform.position;
                continuousRotation = transform.rotation;
                if (cameraObject != null) continuousCameraLocalPos = cameraObject.transform.localPosition;
                if (cameraPivotTransform != null) continuousPivotLocalPos = cameraPivotTransform.localPosition;

                // 3. 목표 FPS 주기에 도달했는지 확인하고, 화면에 보일 Stepped(끊기는) 상태를 갱신합니다.
                frameTimer += Time.deltaTime;
                float frameInterval = 1f / targetCameraFPS;

                if (frameTimer >= frameInterval || isFirstFrame)
                {
                    frameTimer %= frameInterval; // 초과된 시간 보존 (완벽한 타이밍 유지)

                    steppedPosition = continuousPosition;
                    steppedRotation = continuousRotation;
                    steppedCameraLocalPos = continuousCameraLocalPos;
                    steppedPivotLocalPos = continuousPivotLocalPos;

                    isFirstFrame = false;
                }

                // 4. 화면 렌더링을 위해 강제로 툭툭 끊기는(Stepped) 좌표로 덮어씌웁니다.
                transform.position = steppedPosition;
                transform.rotation = steppedRotation;
                if (cameraObject != null) cameraObject.transform.localPosition = steppedCameraLocalPos;
                if (cameraPivotTransform != null) cameraPivotTransform.localPosition = steppedPivotLocalPos;
            }
            else
            {
                isFirstFrame = true; // 기능이 꺼지면 즉시 초기화
            }
        }

        public void OnCameraInputReceived(float x, float y)
        {
            cameraHorizontalInput = x;
            cameraVerticalInput = y;
        }

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
            var dynamicData = WorldCameraManager.Instance.currentDynamicFraming;

            float hInput = player.playerNetworkManager != null ? player.playerNetworkManager.animatorHorizontalMovement.Value : 0f;
            float targetFraming = 0f;
            float currentDelayTime = 0.1f; // 현재 적용될 댐핑 딜레이

            // =========================================================================================
            // 🚨 [1순위 Core Physics 적용] 비대칭 Strafe Hold 및 독립적 딜레이 제어
            // =========================================================================================
            // 1. 좌/우 이동 중일 때 (Target Framing & Delay 설정)
            if (hInput < -0.1f) // 좌측 게걸음
            {
                targetFraming = dynamicData.leftStrafeMaxOffset * Mathf.Abs(hInput);
                lastTargetFraming = dynamicData.leftStrafeMaxOffset; // 최근 이동 방향 '좌측' 캐싱
                currentDelayTime = dynamicData.leftFramingDelay > 0 ? dynamicData.leftFramingDelay : 0.1f;
            }
            else if (hInput > 0.1f) // 우측 게걸음
            {
                targetFraming = dynamicData.rightStrafeMaxOffset * Mathf.Abs(hInput);
                lastTargetFraming = dynamicData.rightStrafeMaxOffset; // 최근 이동 방향 '우측' 캐싱
                currentDelayTime = dynamicData.rightFramingDelay > 0 ? dynamicData.rightFramingDelay : 0.1f;
            }
            // 2. 이동을 멈췄을 때 (정지 상태 판단)
            else
            {
                if (lastTargetFraming < 0f) // 방금 전까지 '좌측'으로 이동 중이었다면?
                {
                    if (dynamicData.holdLeftStrafe)
                    {
                        targetFraming = lastTargetFraming; // 원점으로 안 가고 그대로 유지!
                        currentDelayTime = dynamicData.leftFramingDelay > 0 ? dynamicData.leftFramingDelay : 0.1f;
                    }
                    else
                    {
                        targetFraming = 0f; // 홀드 옵션이 꺼져있으니 원점 복귀
                        currentDelayTime = dynamicData.centerReturnDelay > 0 ? dynamicData.centerReturnDelay : 0.3f;
                    }
                }
                else if (lastTargetFraming > 0f) // 방금 전까지 '우측'으로 이동 중이었다면?
                {
                    if (dynamicData.holdRightStrafe)
                    {
                        targetFraming = lastTargetFraming; // 유지
                        currentDelayTime = dynamicData.rightFramingDelay > 0 ? dynamicData.rightFramingDelay : 0.1f;
                    }
                    else
                    {
                        targetFraming = 0f; // 원점 복귀
                        currentDelayTime = dynamicData.centerReturnDelay > 0 ? dynamicData.centerReturnDelay : 0.3f;
                    }
                }
            }

            // 3. 최종 보간 (SmoothDamp) 실행: 설정된 타겟과 딜레이를 향해 부드럽게 이동합니다.
            currentFramingOffsetX = Mathf.SmoothDamp(currentFramingOffsetX, targetFraming, ref framingVelocity, currentDelayTime);

            Vector3 massCenter = player.transform.position + (Vector3.up * baseOffset.y);
            dbgMassCenter = massCenter;

            // 여기서 Z는 무시되고 오직 X(좌우), Y(높이)만 사용하여 몸통 추적을 합니다. (Z는 Collision에서 처리)
            Vector3 desiredPos = massCenter + (player.transform.right * (baseOffset.x + currentFramingOffsetX));

            if (WorldCameraManager.Instance.currentFocusTargets != null && WorldCameraManager.Instance.currentFocusTargets.Length > 0)
            {
                Transform focusTarget = WorldCameraManager.Instance.currentFocusTargets[WorldCameraManager.Instance.currentFocusTargets.Length - 1];
                if (focusTarget != null)
                {
                    Vector3 poiCenter = Vector3.Lerp(desiredPos, focusTarget.position, WorldCameraManager.Instance.currentTargetBiasWeight);
                    desiredPos = Vector3.Lerp(desiredPos, poiCenter, Time.deltaTime * 5f);
                }
            }

            dbgDesiredPos = desiredPos;
            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref cameraVelocity, cameraSmoothSpeed);
        }

        /// <summary>
        /// [핵심 수정: 숄더뷰 마스터 로직 및 묵직한 보간 추가]
        /// 무한정 돌아가는 3인칭 자유 시점을 통제하고, 타겟 각도까지 부드럽게 쫓아가는 묵직함을 부여합니다.
        /// </summary>
        private Quaternion HandleRotation()
        {
            if (isContextualMode) return transform.rotation;

            // =========================================================================================
            // 🚨 [P0-03 연동] 마우스 제스처 공격 중 카메라 회전 잠금 (Interception)
            // 플레이어가 드래그로 궤적을 그리는 동안 화면이 휙휙 돌아가면 조준이 불가능하므로 시야를 고정합니다.
            // =========================================================================================
            if (playerGestureManager == null && player != null)
            {
                playerGestureManager = player.GetComponent<PlayerGestureManager>();
            }

            float currentHorizontal = cameraHorizontalInput;
            float currentVertical = cameraVerticalInput;

            // 드래그 중일 때는 마우스 델타 입력을 무시하여 카메라를 그 자리에 멈춰세웁니다.
            if (playerGestureManager != null && playerGestureManager.IsDragging)
            {
                currentHorizontal = 0f;
                currentVertical = 0f;
            }
            // =========================================================================================

            // 마우스 입력을 실제 카메라 각도가 아닌 '타겟 각도(Yaw)'에 먼저 누적시킵니다.
            currentTargetLeftAndRightAngle += (currentHorizontal * leftAndRightRotationSpeed * mouseSensitivity) * Time.deltaTime;

            // 마우스 입력이 없는 시간 측정
            if (Mathf.Abs(currentHorizontal) < 0.01f && Mathf.Abs(currentVertical) < 0.01f)
            {
                noInputTimer += Time.deltaTime;
            }
            else
            {
                noInputTimer = 0f;
            }

            // 오일러 각도 정규화 및 차이 계산 (캐릭터 몸통 기준)
            float playerBodyYaw = player.transform.eulerAngles.y;
            float angleDifference = Mathf.DeltaAngle(playerBodyYaw, currentTargetLeftAndRightAngle);

            // =========================================================================================
            // 🚨 [1순위 Core Physics 적용] 멀미 방지용 추적 가중치(Tracking Weight) 연동
            // 가중치가 0에 가까워질수록 클램핑 허용각을 180도(제한 없음)로 풀어서, 
            // 캐릭터가 크게 회전해도 카메라가 억지로 끌려가지 않고 제자리를 지키게 만듭니다.
            // =========================================================================================
            float trackingWeight = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.currentTrackingWeight : 1f;
            float effectiveMaxViewAngle = Mathf.Lerp(180f, maxViewAngle, trackingWeight);

            // 시야각 클램핑 (Deadzone 락) - 숄더뷰 데드존 제한
            angleDifference = Mathf.Clamp(angleDifference, -effectiveMaxViewAngle, effectiveMaxViewAngle);

            bool isMoving = player.playerNetworkManager != null && player.playerNetworkManager.animatorMoveAmountMovement.Value > 0.1f;

            // 오토 센터링 (Auto-Centering) 개입
            if (enableAutoCenterOnMove && isMoving)
            {
                // 유저가 이동 중 마우스 조작을 멈췄을 때 센터링 개입
                if (Mathf.Abs(currentHorizontal) < 0.01f)
                {
                    angleDifference = Mathf.Lerp(angleDifference, 0f, autoCenterSpeed * Time.deltaTime);
                }
            }
            else if (enableAutoCenterOnIdle && !isMoving)
            {
                // 대기 중 5초 이상 입력이 없을 때
                if (noInputTimer >= idleAutoCenterDelay)
                {
                    // 평소보다 느린 속도(0.3배)로 천천히 돌아가도록 센터링 개입
                    angleDifference = Mathf.Lerp(angleDifference, 0f, (autoCenterSpeed * 0.3f) * Time.deltaTime);
                }
            }

            // 클램핑과 보간이 끝난 안전한 값을 타겟 각도로 확정합니다.
            currentTargetLeftAndRightAngle = playerBodyYaw + angleDifference;

            // 상하(Pitch) 타겟 회전 계산
            currentTargetUpAndDownAngle -= (currentVertical * upAndDownRotationSpeed * mouseSensitivity) * Time.deltaTime;
            currentTargetUpAndDownAngle = Mathf.Clamp(currentTargetUpAndDownAngle, minimumPivot, maximumPivot);

            // 묵직한 보간 로직: 실제 카메라 각도를 타겟 각도를 향해 부드럽게 댐핑시킵니다.
            // (Mathf.SmoothDampAngle을 사용하여 360도 랩핑 오차를 완벽하게 보정합니다)
            leftAndRightLookAngle = Mathf.SmoothDampAngle(leftAndRightLookAngle, currentTargetLeftAndRightAngle, ref leftRightRotationVelocity, cameraRotationDampTime);
            upAndDownLookAngle = Mathf.SmoothDamp(upAndDownLookAngle, currentTargetUpAndDownAngle, ref upDownRotationVelocity, cameraRotationDampTime);

            return Quaternion.Euler(upAndDownLookAngle, leftAndRightLookAngle, 0);
        }

        private Quaternion HandleMagneticSoftLock(Quaternion currentRotation)
        {
            if (!player.playerNetworkManager.isLockedOn.Value || player.playerCombatManager.currentTarget == null)
                return currentRotation;

            Transform targetPoint = player.playerCombatManager.currentTarget.characterCombatManager.lockOnTransform;
            if (targetPoint == null) return currentRotation;

            Vector3 viewportPos = cameraObject.WorldToViewportPoint(targetPoint.position);
            Vector2 distanceFromCenter = new Vector2(Mathf.Abs(viewportPos.x - 0.5f), Mathf.Abs(viewportPos.y - 0.5f));

            float pullStrength = 0f;

            if (distanceFromCenter.x > deadzoneRadius.x || distanceFromCenter.y > deadzoneRadius.y)
            {
                float exceedX = Mathf.Max(0, distanceFromCenter.x - deadzoneRadius.x);
                float exceedY = Mathf.Max(0, distanceFromCenter.y - deadzoneRadius.y);
                pullStrength = Mathf.Clamp01((exceedX + exceedY) * 2f);
            }

            Vector3 dir = targetPoint.position - cameraPivotTransform.position;
            Quaternion targetRot = Quaternion.LookRotation(dir.normalized);

            Quaternion result = Quaternion.Slerp(currentRotation, targetRot, pullStrength * magneticPullSpeed * Time.deltaTime);

            Vector3 euler = result.eulerAngles;
            leftAndRightLookAngle = euler.y;
            float pitch = euler.x;
            if (pitch > 180) pitch -= 360;
            upAndDownLookAngle = pitch;

            // 락온 상태에서는 타겟 각도도 함께 동기화하여 락온 해제 시 튀는 현상 방지
            currentTargetLeftAndRightAngle = leftAndRightLookAngle;
            currentTargetUpAndDownAngle = upAndDownLookAngle;

            return result;
        }

        /// <summary>
        /// [다중 방어 콜리전 로직 + 카메라 전진 튀어오름(Shooting Forward) 버그 픽스]
        /// </summary>
        private void HandleCollision()
        {
            float rawZ = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.currentBaseOffset.z : -2.5f;

            debugRawZ = rawZ;

            // =========================================================================================
            // 🚨 [Z축 실시간 반영 픽스] 사용자가 인스펙터에 양수(233)를 쓰든 음수를 쓰든 무조건 마이너스를 곱해
            // 등 뒤로(음수) 물러나도록 수학적으로 '강제 보정'합니다!
            // =========================================================================================
            float baseZ = -Mathf.Abs(rawZ);
            targetZPosition = baseZ;
            lastCollisionObjectName = "None";

            // =========================================================================================
            // 🚨 [Z축 버그 원인 규명 및 회피 로직]
            // 하늘이나 바닥을 쳐다볼 때(Pitch가 크거나 작을 때) 카메라가 땅속으로 파고들어 충돌체에 즉시 막히는 현상을 해결하기 위해
            // 디버그 모드로 충돌 무시(Bypass)를 제공합니다. 이것을 켜보시면 Z축 거리가 즉각적으로 멀어지는 것을 확인할 수 있습니다!
            // =========================================================================================
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

            // 불안정한 위치의 차이(position - position)를 쓰지 않고, 피벗의 역방향(로컬 Z축 음수 방향)을 절대적인 방향 벡터로 사용합니다.
            Vector3 direction = -cameraPivotTransform.forward;

            LayerMask combinedLayers = collideWithLayers | characterCollisionLayers;
            float closestDistance = Mathf.Abs(baseZ);

            // 1. 방어 1단계: 넓은 구형(SphereCast) 캐스트로 벽 및 주변 캐릭터 감지
            RaycastHit[] hits = Physics.SphereCastAll(cameraPivotTransform.position, cameraCollisionRadius, direction, Mathf.Abs(baseZ), combinedLayers);

            foreach (var h in hits)
            {
                // 로컬 플레이어 본인의 콜라이더는 무시 (카메라 덜덜거림 방지)
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

            // 2. 방어 2단계: 얇은 벽을 뚫는 현상(Tunneling)을 막기 위한 1줄짜리 예리한 Linecast 보완
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

            // 3. [치명적 버그 수정] 극한의 압착 방어 (카메라가 플레이어 앞으로 튀어나가는 현상 완벽 방지)
            if (targetZPosition > -minimumCollisionOffset)
            {
                targetZPosition = -minimumCollisionOffset;
            }

            debugTargetZ = targetZPosition;

            // 4. 머리 관통 방지를 위한 동적 메쉬 회피(Mesh Avoidance) 기동
            float avoidanceFactor = 0f;
            // 이제 targetZPosition은 항상 음수이므로 부등호를 통해 0에 가까워졌는지만 확인합니다.
            if (targetZPosition > -avoidanceThreshold)
            {
                avoidanceFactor = 1f - (Mathf.Abs(targetZPosition) / avoidanceThreshold);
            }

            float targetX = bodyAvoidanceOffset.x * avoidanceFactor;
            float targetY = bodyAvoidanceOffset.y * avoidanceFactor;

            // 5. 충돌 완충 보간 (Smoothing) 및 Z, X, Y 동시 적용
            // 🚨 [Z축 보간 속도 픽스] Time.deltaTime을 곱해주어 프레임 속도에 구애받지 않고 
            // 인스펙터 값을 즉각적(실시간)으로 화면에 부드럽게 렌더링합니다.
            float lerpSpeed = 15f * Time.deltaTime;
            float currentZ = Mathf.Lerp(cameraObject.transform.localPosition.z, targetZPosition, lerpSpeed);
            float currentX = Mathf.Lerp(cameraObject.transform.localPosition.x, targetX, lerpSpeed);
            float currentY = Mathf.Lerp(cameraObject.transform.localPosition.y, targetY, lerpSpeed);

            cameraObject.transform.localPosition = new Vector3(currentX, currentY, currentZ);

            debugActualZ = currentZ;
        }

        private void ApplyFinalTransform(Quaternion baseRotation)
        {
            if (WorldCameraManager.Instance == null || cameraObject == null) return;

            float targetFov = WorldCameraManager.Instance.currentFOV;
            var handheld = WorldCameraManager.Instance.currentHandheldEffect;
            float zTilt = WorldCameraManager.Instance.currentZTilt;

            // 🚨 [1순위 Core Physics 적용] Yaw Offset 연동
            // 동적 프레이밍(Dynamic Framing)이나 특별한 스탠스(Stance)에서 지정한 좌/우 편향 각도를 Y축에 반영합니다.
            float yawOffset = WorldCameraManager.Instance.currentYawOffset;

            cameraObject.fieldOfView = targetFov; // FOV 적용

            // [신규 로직] 핸드헬드 및 이동 시 보빙(Bobbing) 효과 추가로 바디캠 느낌 극대화
            float staminaPerc = 1f;
            if (player.playerNetworkManager != null && player.playerNetworkManager.maxStamina.Value > 0)
                staminaPerc = (float)player.playerNetworkManager.currentStamina.Value / player.playerNetworkManager.maxStamina.Value;

            float exhaustionFactor = 1.0f;
            if (staminaPerc < 0.3f)
                exhaustionFactor += (0.3f - staminaPerc) * staminaExhaustionMultiplier;

            // A. 핸드헬드 기본 수전증 및 호흡(Sway) 효과
            float swayAmount = handheld.enableHandheldEffect ? handheld.swayAmount : 0f;
            float swaySpeed = handheld.enableHandheldEffect ? handheld.swaySpeed : 1f;
            float bobbingAmount = handheld.enableHandheldEffect ? handheld.bobbingAmount : 0f;

            float swayX = (Mathf.PerlinNoise(Time.time * swaySpeed, 0) - 0.5f) * swayAmount * exhaustionFactor * bodycamWeight;
            float swayY = (Mathf.PerlinNoise(0, Time.time * swaySpeed) - 0.5f) * swayAmount * exhaustionFactor * bodycamWeight;
            float swayZ = (Mathf.PerlinNoise(Time.time * swaySpeed, Time.time * swaySpeed) - 0.5f) * (swayAmount * 0.5f) * exhaustionFactor * bodycamWeight;

            // B. 걷기/뛰기에 따른 무거운 보빙(Bobbing) 효과
            float moveAmount = player.playerNetworkManager != null ? player.playerNetworkManager.animatorMoveAmountMovement.Value : 0f;
            float bobSpeed = 12f; // 발걸음 빈도
            float bobX = Mathf.Sin(Time.time * bobSpeed) * movementBobbingAmount * moveAmount * bodycamWeight;
            float bobY = Mathf.Abs(Mathf.Cos(Time.time * bobSpeed)) * movementBobbingAmount * moveAmount * bodycamWeight; // Cos 절대값으로 튕기는 느낌

            // 🚨 [1순위 연동] 기존 노이즈 대신 Sway와 Bobbing, 그리고 yawOffset을 믹스하여 적용합니다. (시야의 입체적 흔들림 및 방향 편향)
            Quaternion bodycamRot = Quaternion.Euler(swayX - bobY, swayY + bobX + yawOffset, swayZ + (bobX * 0.3f) + zTilt);

            Vector3 shakeOffset = Vector3.zero;
            if (shakeDuration > 0)
            {
                shakeOffset = UnityEngine.Random.insideUnitSphere * shakeIntensity;
                shakeDuration -= Time.deltaTime;
            }

            transform.rotation = baseRotation * bodycamRot;

            // [버그 수정] cameraPivotTransform.localPosition.y를 그대로 읽어오면 이전 프레임의 흔들림(shakeOffset.y)이 누적되어 좌표가 이탈합니다.
            // 항상 기준이 되는 'currentPivotHeight'에 흔들림을 일시적으로 더하도록 수정하여 원복 불량 현상을 완벽히 차단합니다.
            cameraPivotTransform.localPosition = new Vector3(0, currentPivotHeight, 0) + shakeOffset;
        }

        // =========================================================================================
        // 타겟팅 로직 (기존 기능 완벽 보존)
        // =========================================================================================

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
                    // 지형 가림 체크 (Linecast)
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
            SetLockCameraHeight();
        }

        public void SetLockCameraHeight()
        {
            if (cameraHeightCoroutine != null) StopCoroutine(cameraHeightCoroutine);
            cameraHeightCoroutine = StartCoroutine(SetCameraHeightRoutine());
        }

        private IEnumerator SetCameraHeightRoutine()
        {
            float duration = 0.5f;
            float timer = 0f;
            float startH = currentPivotHeight; // 시작 높이를 현재 기준 높이로 설정

            while (timer < duration)
            {
                timer += Time.deltaTime;
                float t = timer / duration;
                float targetH = (player.playerCombatManager.currentTarget != null) ? lockedCameraHeight : unlockedCameraHeight;

                // 직접 localPosition을 건드리지 않고 기준 높이(currentPivotHeight)만 부드럽게 갱신합니다.
                currentPivotHeight = Mathf.Lerp(startH, targetH, t);
                yield return null;
            }

            currentPivotHeight = (player.playerCombatManager.currentTarget != null) ? lockedCameraHeight : unlockedCameraHeight;
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

            // 타겟 각도 동기화 (화면 튀는 현상 방지)
            leftAndRightLookAngle = transform.eulerAngles.y;
            currentTargetLeftAndRightAngle = leftAndRightLookAngle;

            float pitch = transform.eulerAngles.x;
            if (pitch > 180) pitch -= 360;
            upAndDownLookAngle = pitch;
            currentTargetUpAndDownAngle = upAndDownLookAngle;
        }

        public void Shake(float intensity, float duration)
        {
            shakeIntensity = intensity;
            shakeDuration = duration;
        }

        public void SetBodycamWeight(float weight)
        {
            bodycamWeight = Mathf.Clamp01(weight);
        }

        // =========================================================================================
        // [버그 수정] 애니메이터 루트 모션 회전 보정용 메서드 추가
        // PlayerAnimationManager에서 턴(Turn) 애니메이션 재생 시 카메라가 엇나가는 것을 방지합니다.
        // =========================================================================================
        public void AdjustCameraYaw(float angleOffset)
        {
            leftAndRightLookAngle += angleOffset;
            currentTargetLeftAndRightAngle += angleOffset;
        }

        // =========================================================================================
        // [디버깅] 숄더뷰 데드존(시야각) 시각화 기즈모
        // =========================================================================================
        private void OnDrawGizmos()
        {
            if (!showViewAngleGizmos || player == null) return;

            Gizmos.color = new Color(0.2f, 0.6f, 1.0f, 0.3f); // 반투명한 파란색
            Vector3 pivotPosition = cameraPivotTransform != null ? cameraPivotTransform.position : transform.position;

            // 캐릭터의 정면 벡터
            Vector3 forward = player.transform.forward;

            // 좌우 한계선 벡터 계산
            Vector3 leftBoundary = Quaternion.Euler(0, -maxViewAngle, 0) * forward;
            Vector3 rightBoundary = Quaternion.Euler(0, maxViewAngle, 0) * forward;

            // 기즈모 그리기 (정면 가이드선)
            Gizmos.DrawRay(pivotPosition, forward * 5f);

            // 한계선은 빨간색으로 강조 표시하여 데드존을 시각화합니다.
            Gizmos.color = Color.red;
            Gizmos.DrawRay(pivotPosition, leftBoundary * 5f);
            Gizmos.DrawRay(pivotPosition, rightBoundary * 5f);
        }
    }

#if UNITY_EDITOR
    // =========================================================================================
    // [신규] 인게임 실시간 상태 모니터링을 위한 커스텀 에디터 (Custom Editor)
    // 🚨 [추가] SO 수치를 인스펙터에서 바로 수정하고 실시간(Live)으로 덮어쓰는 에디터를 구현했습니다.
    // =========================================================================================
    [CustomEditor(typeof(PlayerCamera))]
    public class PlayerCameraEditor : Editor
    {
        private bool showDebugPanel = true;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PlayerCamera pc = (PlayerCamera)target;

            if (!Application.isPlaying || pc.player == null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox("▶ 게임을 실행(Play)하면 이곳에 상세한 카메라 및 애니메이션 상태 모니터가 표시됩니다.", MessageType.Info);
                return;
            }

            // =================================================================================
            // 📊 [기존] 실시간 상태 모니터
            // =================================================================================
            EditorGUILayout.Space(15);

            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, normal = { textColor = new Color(0.4f, 0.8f, 1f) } };
            GUIStyle subHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.9f, 0.7f, 0.4f) } };

            showDebugPanel = EditorGUILayout.Foldout(showDebugPanel, "📊 실시간 상태 모니터 (Live Debug)", true, new GUIStyle(EditorStyles.foldoutHeader) { fontStyle = FontStyle.Bold });

            if (showDebugPanel)
            {
                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.LabelField("🌍 World & Player State", subHeaderStyle);
                DrawField("World State", WorldGameStateManager.Instance != null ? WorldGameStateManager.Instance.currentState.ToString() : "N/A");

                bool isLockedOn = pc.player.playerNetworkManager != null && pc.player.playerNetworkManager.isLockedOn.Value;
                string targetName = isLockedOn && pc.player.playerCombatManager.currentTarget != null ? pc.player.playerCombatManager.currentTarget.name : "None";
                DrawField("Lock-On Target", $"[{(isLockedOn ? "<color=lime>ON</color>" : "OFF")}] {targetName}", true);

                EditorGUILayout.Space(5);

                EditorGUILayout.LabelField("🏃 Animation & Action State", subHeaderStyle);
                int actionID = pc.player.animator != null ? pc.player.animator.GetInteger(AnimatorParameterHash.ActionState) : 0;
                DrawField("Action ID", actionID.ToString());
                DrawField("Is Performing Action", pc.player.isPerformingAction ? "<color=red>True</color>" : "False", true);

                string baseAnim = "N/A";
                string actionAnim = "N/A";
                if (pc.player.animator != null)
                {
                    if (pc.player.animator.layerCount > 0)
                    {
                        var bState = pc.player.animator.GetCurrentAnimatorClipInfo(0);
                        if (bState.Length > 0 && bState[0].clip != null) baseAnim = bState[0].clip.name;
                    }
                    if (pc.player.animator.layerCount > 1)
                    {
                        var aState = pc.player.animator.GetCurrentAnimatorClipInfo(1);
                        if (aState.Length > 0 && aState[0].clip != null) actionAnim = aState[0].clip.name;
                    }
                }
                DrawField("Base Layer Clip", baseAnim);
                DrawField("Action Layer Clip", actionAnim);

                string currentStance = "N/A";
                try
                {
                    var gestureMgr = pc.player.GetComponent<PlayerGestureManager>();
                    if (gestureMgr != null)
                    {
                        var stanceField = gestureMgr.GetType().GetField("currentStance", BindingFlags.Public | BindingFlags.Instance);
                        if (stanceField != null) currentStance = stanceField.GetValue(gestureMgr).ToString();
                    }
                }
                catch { }
                DrawField("Weapon Stance", currentStance);

                EditorGUILayout.Space(5);

                EditorGUILayout.LabelField("🎥 Camera Transform & SO Data", subHeaderStyle);

                // Caller 정보를 인스펙터에 명확하게 표시합니다.
                DrawField("최근 호출 원인 (Caller)", $"<color=lime>{pc.lastCallerReason}</color>", true);

                CameraSequencePresetSO currentSeqSO = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.currentSequenceSO : null;
                CameraStancePresetSO currentStanceSO = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.currentStanceSO : null;

                string currentSeqName = currentSeqSO != null ? currentSeqSO.name : "None (재생중 아님)";
                string currentStanceName = currentStanceSO != null ? currentStanceSO.name : "None (대기 스탠스 없음)";

                DrawField("Active Sequence SO", $"<color=cyan>{currentSeqName}</color>", true);
                DrawField("Active Stance SO", $"<color=cyan>{currentStanceName}</color>", true);

                if (pc.player.isPerformingAction && currentSeqSO != null && currentSeqSO.name.Contains("LockOn"))
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox("🚨 [원인 규명] 공격 중인데 LockOn 시퀀스가 재생되는 이유:\n" +
                        "현재 재생 중인 공격 애니메이션의 'AnimationEventParamsSO' 파일 인스펙터를 여세요.\n" +
                        "[Camera Sequence] 슬롯에 실수로 LockOn 에셋을 넣어두셨습니다! 이 슬롯을 비우거나 공격용 SO로 교체하세요.", MessageType.Error);
                }

                EditorGUILayout.Space();
                DrawField("Z축 원본값 (SO Data)", $"{pc.debugRawZ:F2}");

                if (pc.bypassCollisionForDebug)
                {
                    DrawField("🚨 충돌/압착 원인", $"<color=lime>[Bypass 활성화] 충돌 완전 무시됨</color>", true);
                    DrawField("충돌 보정된 목표 Z", $"<color=lime>{pc.debugTargetZ:F2}</color>", true);
                }
                else if (pc.lastCollisionObjectName != "None")
                {
                    DrawField("🚨 충돌/압착 원인", $"<color=orange>{pc.lastCollisionObjectName}</color>", true);
                    DrawField("충돌 보정된 목표 Z", $"<color=orange>{pc.debugTargetZ:F2}</color>", true);
                }
                else
                {
                    DrawField("충돌 보정된 목표 Z", $"{pc.debugTargetZ:F2}");
                }

                DrawField("실제 렌더링 Z (Child Z)", $"<color=lime>{pc.debugActualZ:F2}</color>", true);

                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("💡 [안내] Z축 거리는 최상위(Root) 오브젝트가 아닌 자식 'Main Camera'의 Local Z값을 움직입니다! 하이어라키에서 Main Camera를 클릭해보세요.\n\n※ Z값이 변하지 않는다면 카메라가 바닥(Floor)에 부딪힌 것입니다! 디버그용 'Bypass Collision' 체크박스를 켜고 Z값이 뒤로 멀어지는지 테스트해 보세요.", MessageType.Info);

                float fov = pc.cameraObject != null ? pc.cameraObject.fieldOfView : 0f;
                DrawField("Current FOV", fov.ToString("F1"));

                float yaw = GetPrivateField<float>(pc, "leftAndRightLookAngle");
                float pitch = GetPrivateField<float>(pc, "upAndDownLookAngle");
                DrawField("Camera Angle (Pitch/Yaw)", $"{pitch:F1} / {yaw:F1}");

                EditorGUILayout.EndVertical();

                Repaint();
            }
        }

        private void DrawField(string label, string value, bool richText = false)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(180));
            GUIStyle style = new GUIStyle(EditorStyles.label) { richText = richText, fontStyle = FontStyle.Bold };
            EditorGUILayout.LabelField(value, style);
            EditorGUILayout.EndHorizontal();
        }

        private T GetPrivateField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) return (T)field.GetValue(target);
            return default;
        }
    }
#endif
}