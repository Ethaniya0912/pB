using System;
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
    /// [AAA 숄더뷰 물리 엔진 코어 v3.7] 
    /// 1. 다이내믹 프레이밍 상속 논리 통합 (Use Dynamic Framing 활성화 시에도 Inherit 동결 100% 보장)
    /// 2. 다이내믹 프레이밍 상속 우선순위 교정 (A/D 입력 씹힘 방어 및 자동 상속)
    /// 3. 180도 래핑(Wrap-around)으로 인한 화면 반전 및 점프 버그 완벽 방어
    /// 4. 마우스 드래깅 현상 제거 및 Zero-Lag 액션 댐핑 오버라이드 구축
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

        private float currentDynamicHeight = 0f;
        private float dynamicHeightVelocity = 0f;

        private float collisionZVelocity;
        private float collisionXVelocity;
        private float collisionYVelocity;

        private Vector3 dbgMassCenter;
        private Vector3 dbgDesiredPos;
        #endregion

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
        }

        private void Start()
        {
            DontDestroyOnLoad(gameObject);
            if (WorldCameraManager.Instance != null)
            {
                WorldCameraManager.Instance.SetLocalCamera(this);
            }

            if (player != null) smoothedPlayerY = player.transform.position.y;

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

            // 2. 핵심 물리 연산 4단계
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
        public void OnCameraInputReceived(float x, float y)
        {
            cameraHorizontalInput = x;
            cameraVerticalInput = y;
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
            // 🚨 [패치 핵심] 다이내믹 프레이밍 보간 연산 (통합 및 상속 논리 개선)
            // =========================================================================================
            CameraStancePresetSO activeStance = WorldCameraManager.Instance.currentStanceSO;

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
            currentFramingOffsetX = Mathf.SmoothDamp(currentFramingOffsetX, targetFraming, ref framingVelocity, currentDelayTime);
            currentFramingYawOffset = Mathf.SmoothDampAngle(currentFramingYawOffset, targetFramingYaw, ref framingYawVelocity, currentYawDelayTime);

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
                if (vBehavior.behaviorType == CameraVerticalBehavior.ElevationOnly)
                {
                    float t = Mathf.InverseLerp(minimumPivot, maximumPivot, upAndDownLookAngle);
                    targetVerticalOffset = Mathf.Lerp(vBehavior.maxElevationHeight, vBehavior.minElevationHeight, t);
                    heightSmoothTime = 1f / Mathf.Max(vBehavior.elevationSpeed, 0.1f);
                }
                else if (vBehavior.behaviorType == CameraVerticalBehavior.DynamicOverShoulder)
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
            // 캐릭터 몸통 축(Player Right) -> 카메라 시선 축(Camera Right)으로 오르빗 기준 변경
            // =========================================================================================
            Quaternion cameraYawRot = Quaternion.Euler(0, leftAndRightLookAngle, 0);
            Vector3 cameraRight = cameraYawRot * Vector3.right;

            Vector3 desiredPos = massCenter + (cameraRight * (baseOffset.x + currentFramingOffsetX));
            desiredPos.y += currentDynamicHeight;

            // 타겟(POI) 포커스 개입
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

            // =========================================================================================
            // Action Damping Override (위치)
            // =========================================================================================
            float currentPosDamp = cameraSmoothSpeed;
            if (WorldCameraManager.Instance != null && WorldCameraManager.Instance.IsSequencePlaying)
            {
                currentPosDamp = WorldCameraManager.Instance.CurrentPositionDamping;
            }

            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref cameraVelocity, currentPosDamp);
        }

        private Quaternion HandleRotation()
        {
            if (isContextualMode) return transform.rotation;

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
                if (activeStanceZero != null && activeStanceZero.verticalBehavior.behaviorType == CameraVerticalBehavior.ElevationOnly)
                    fixedPitchWeightZero = activeStanceZero.verticalBehavior.fixedPitchAngle;

                return Quaternion.Euler(fixedPitchWeightZero, leftAndRightLookAngle, 0);
            }

            float playerBodyYaw = player.transform.eulerAngles.y;

            // 1. 현재 캐릭터 몸통과 카메라가 얼마나 벌어져 있는지(차이)를 안전하게 산출합니다.
            float currentDiff = Mathf.DeltaAngle(playerBodyYaw, leftAndRightLookAngle);

            // 2. 이 '차이 공간' 안에서 유저의 마우스 입력을 더합니다. 
            currentDiff += mouseYaw;

            // 3. 멀미 방어 구간일 때는 한계각을 180도(자유 시점)까지 넓혀줍니다.
            // 단, 180도 정점에서의 기하학적 버그(Singularity)를 막기 위해 최대 179.9f로 하드코딩 락을 겁니다.
            float effectiveMaxViewAngle = Mathf.Lerp(179.9f, maxViewAngle, trackingWeight);

            // 4. 차이값이 한계선을 절대 벗어나지 못하도록 철벽 방어를 세웁니다! (탈선 불가)
            currentDiff = Mathf.Clamp(currentDiff, -effectiveMaxViewAngle, effectiveMaxViewAngle);

            // Auto Centering 로직 (이 역시 '차이 공간' 내부에서 안전하게 0으로 수렴시킵니다)
            if (Mathf.Abs(currentHorizontal) < 0.01f && Mathf.Abs(currentVertical) < 0.01f) noInputTimer += Time.deltaTime;
            else noInputTimer = 0f;

            bool isMoving = player.playerNetworkManager != null && player.playerNetworkManager.animatorMoveAmountMovement.Value > 0.1f;

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

            // 5. 철벽을 거친 '안전한 차이값'을 다시 실제 글로벌 앵글로 변환해 줍니다.
            leftAndRightLookAngle = playerBodyYaw + currentDiff;

            return Quaternion.Euler(upAndDownLookAngle, leftAndRightLookAngle, 0);
        }

        private Quaternion ApplyDynamicYawAndClamp(Quaternion currentBaseRotation)
        {
            CameraStancePresetSO activeStance = WorldCameraManager.Instance?.currentStanceSO;
            float actualPitch = upAndDownLookAngle;

            if (activeStance != null && activeStance.verticalBehavior.behaviorType == CameraVerticalBehavior.ElevationOnly)
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
            return Quaternion.Euler(actualPitch, playerBodyYaw + totalDiff, 0);
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

            Vector3 dir = targetPoint.position - transform.position;

            dir.y = Mathf.Clamp(dir.y, -dir.magnitude * 0.5f, dir.magnitude * 0.5f);
            Quaternion targetRot = Quaternion.LookRotation(dir.normalized);

            float targetPitch = targetRot.eulerAngles.x;
            if (targetPitch > 180f) targetPitch -= 360f;
            float targetYaw = targetRot.eulerAngles.y;

            float trackingWeight = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.CurrentTrackingWeight : 1f;
            float slerpAmount = pullStrength * magneticPullSpeed * trackingWeight * Time.deltaTime;

            leftAndRightLookAngle = Mathf.LerpAngle(leftAndRightLookAngle, targetYaw, slerpAmount);
            upAndDownLookAngle = Mathf.Lerp(upAndDownLookAngle, targetPitch, slerpAmount);

            upAndDownLookAngle = Mathf.Clamp(upAndDownLookAngle, minimumPivot, maximumPivot);

            float playerBodyYaw = player.transform.eulerAngles.y;
            float effectiveMaxViewAngle = Mathf.Lerp(179.9f, maxViewAngle, trackingWeight); // 179.9f 하드코딩 락 적용

            float angleDiff = Mathf.DeltaAngle(playerBodyYaw, leftAndRightLookAngle);
            angleDiff = Mathf.Clamp(angleDiff, -effectiveMaxViewAngle, effectiveMaxViewAngle);

            leftAndRightLookAngle = playerBodyYaw + angleDiff;

            return Quaternion.Euler(upAndDownLookAngle, leftAndRightLookAngle, 0);
        }

        private void HandleCollision()
        {
            float rawZ = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.currentBaseOffset.z : -2.5f;

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

            float targetFov = WorldCameraManager.Instance.currentFOV;
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
            float bobX = Mathf.Sin(Time.time * bobSpeed) * movementBobbingAmount * moveAmount * bodycamWeight;
            float bobY = Mathf.Abs(Mathf.Cos(Time.time * bobSpeed)) * movementBobbingAmount * moveAmount * bodycamWeight;

            Quaternion bodycamRot = Quaternion.Euler(
                swayX - bobY,
                swayY + bobX,
                swayZ + (bobX * 0.3f) + zTilt
            );

            Vector3 shakeOffset = Vector3.zero;
            if (shakeDuration > 0)
            {
                shakeOffset = UnityEngine.Random.insideUnitSphere * shakeIntensity;
                shakeDuration -= Time.deltaTime;
            }

            transform.rotation = baseRotation * bodycamRot;
            cameraPivotTransform.localPosition = shakeOffset;
        }
        #endregion

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

        public void Shake(float intensity, float duration)
        {
            shakeIntensity = intensity;
            shakeDuration = duration;
        }

        public void SetBodycamWeight(float weight)
        {
            bodycamWeight = Mathf.Clamp01(weight);
        }

        public void AdjustCameraYaw(float angleOffset)
        {
            leftAndRightLookAngle += angleOffset;
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
        #endregion
    }
}