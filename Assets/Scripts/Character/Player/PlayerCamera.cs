using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TDA.World;
using TDA.Cameras; // [버그 수정] CameraStancePresetSO를 인식하기 위해 추가
using TDA.Core.Events; // 파라미터 해시 접근을 위해 추가

#if UNITY_EDITOR
using UnityEditor;
using System.Reflection;
#endif

namespace TDA.Character.Player
{
    public class PlayerCamera : MonoBehaviour
    {
        [Header("References")]
        public Camera cameraObject;
        public PlayerManager player;
        [SerializeField] private Transform cameraPivotTransform;

        private PlayerGestureManager playerGestureManager;

        // =========================================================================================
        // 🚨 [강화] 에디터 실시간 SO 렌더링 및 자동 동기화 (Live Update Auto-Sync)
        // =========================================================================================
        [Header("Live Editor Update (Debug)")]
        [Tooltip("체크 시 중앙 관제탑(WCM)에서 현재 활성화된 SO를 자동으로 가져와 아래 슬롯에 동기화합니다.")]
        public bool autoSyncLiveEditor = true;

        [Tooltip("체크 시 WCM의 보간(Lerp) 연산을 무시하고, 아래 슬롯의 SO 원본 수치를 화면에 강제로 즉시(Raw) 렌더링합니다. (수치 튜닝 시 필수)")]
        public bool forceRawSOValues = true;

        [Tooltip("현재 활성화된 2계층 시퀀스 SO (자동 동기화됨)")]
        public CameraSequencePresetSO liveEditSequenceSO;

        [Tooltip("현재 활성화된 1계층 스탠스 SO (자동 동기화됨. 이 에셋의 수치를 고치면 화면에 즉시 반영됩니다!)")]
        public CameraStancePresetSO liveEditStanceSO;

        [Header("Camera Distance Settings")]
        [Tooltip("평상시 카메라와 캐릭터 사이의 기본 거리입니다. 값이 클수록 카메라가 캐릭터 등 뒤로 멀어집니다. (권장: 2.5 ~ 4.0)")]
        [SerializeField] private float defaultCameraDistance = 2.5f;

        [Header("Rotation Settings")]
        [SerializeField] private float mouseSensitivity = 1.0f;
        [SerializeField] private float leftAndRightRotationSpeed = 220f;
        [SerializeField] private float upAndDownRotationSpeed = 220f;
        [SerializeField] private float minimumPivot = -30f;
        [SerializeField] private float maximumPivot = 60f;

        [Header("Camera Feel Settings (P3)")]
        [Tooltip("마우스 회전 시 카메라가 따라오는 부드러움(묵직함) 정도입니다. 값이 클수록 무겁게 끌려옵니다.")]
        [SerializeField] private float cameraRotationDampTime = 0.05f;

        // 내부 연산을 위한 타겟 각도 및 댐핑 속도 추적 변수
        private float currentTargetLeftAndRightAngle;
        private float currentTargetUpAndDownAngle;
        private float leftRightRotationVelocity;
        private float upDownRotationVelocity;

        [Header("Shoulder View Restrictions (P2)")]
        [Tooltip("카메라가 캐릭터 정면을 기준으로 좌우로 단독 회전할 수 있는 최대 한계각 (데드존)")]
        [SerializeField][Range(10f, 90f)] private float maxViewAngle = 60f;

        [Tooltip("캐릭터가 전진(이동)을 시작할 때, 카메라가 캐릭터의 등 뒤 정면으로 자동으로 부드럽게 돌아올지 여부")]
        [SerializeField] private bool enableAutoCenterOnMove = true;

        [Tooltip("대기(Idle) 상태에서 마우스 입력이 없을 때 카메라가 정면으로 복귀할지 여부")]
        [SerializeField] private bool enableAutoCenterOnIdle = true;

        [Tooltip("대기 상태에서 카메라가 정면으로 복귀하기 전 기다리는 시간(초)")]
        [SerializeField] private float idleAutoCenterDelay = 5.0f;

        [Tooltip("자동 정렬 시 카메라가 등 뒤로 복귀하는 속도")]
        [SerializeField] private float autoCenterSpeed = 2.0f;

        private float noInputTimer = 0f;

        [Header("Debug & Logs")]
        public bool showViewAngleGizmos = true;
        [Tooltip("체크 시 콘솔(Debug.Log)에 5초마다 카메라 좌표 연산의 핵심 인수들을 출력합니다.")]
        public bool showDebugLogs = true;

        // 🚨 [신규 추가] Z축 테스트용 충돌 무시 버튼
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
        [SerializeField][Range(1f, 60f)] private float targetCameraFPS = 24f;
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

        [Header("Handheld & Bodycam Effect (P3)")]
        [SerializeField] private float handheldSwayAmount = 0.5f;
        [SerializeField] private float handheldSwaySpeed = 1.0f;
        [SerializeField] private float movementBobbingAmount = 1.2f;
        [SerializeField] private float staminaExhaustionMultiplier = 2.5f;

        [Header("Collision & Follow (Anti-Clipping)")]
        [SerializeField] private float cameraSmoothSpeed = 0.1f;
        [SerializeField] private float cameraCollisionRadius = 0.25f;
        [SerializeField] private float minimumCollisionOffset = 0.2f;
        [SerializeField] private Vector2 bodyAvoidanceOffset = new Vector2(0.4f, 0.2f);
        [SerializeField] private float avoidanceThreshold = 1.2f;

        [SerializeField] private LayerMask collideWithLayers;
        [SerializeField] private LayerMask characterCollisionLayers;

        [Header("Camera Height Settings")]
        [SerializeField] private float unlockedCameraHeight = 1.65f;
        [SerializeField] private float lockedCameraHeight = 2.0f;

        private float currentPivotHeight;

        private float cameraHorizontalInput;
        public float CameraHorizontalInput => cameraHorizontalInput;
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

        // 동적 프레이밍 내부 연산 변수
        private float currentFramingOffsetX = 0f;
        private float framingVelocity = 0f;

        // 디버그 추적용 임시 변수
        private Vector3 dbgMassCenter;
        private Vector3 dbgDesiredPos;

        // =========================================================================================
        // 🚨 [완벽 해결] 불안정한 리플렉션을 삭제하고 관제탑 필드와 1:1로 직접 소통합니다.
        // 탐색 실패 메시지가 완전히 제거되며, 에디터 라이브 동기화를 100% 보장합니다.
        // =========================================================================================
        public void GetActiveCameraSOs(out CameraSequencePresetSO seqSO, out CameraStancePresetSO stanceSO)
        {
            seqSO = null;
            stanceSO = null;

            if (WorldCameraManager.Instance != null)
            {
                seqSO = WorldCameraManager.Instance.currentSequenceSO;
                stanceSO = WorldCameraManager.Instance.currentStanceSO;
            }
        }

        private void Awake()
        {
            if (cameraObject != null)
            {
                defaultFOV = cameraObject.fieldOfView;
                targetFOV = defaultFOV;
                defaultZPosition = -Mathf.Abs(defaultCameraDistance);
                cameraObject.nearClipPlane = 0.01f;
            }
            currentPivotHeight = unlockedCameraHeight;
        }

        private void Start()
        {
            DontDestroyOnLoad(gameObject);
            if (WorldCameraManager.Instance != null)
            {
                WorldCameraManager.Instance.SetLocalCamera(this);
            }

            continuousPosition = transform.position;
            continuousRotation = transform.rotation;
            if (cameraObject != null) continuousCameraLocalPos = cameraObject.transform.localPosition;
            if (cameraPivotTransform != null) continuousPivotLocalPos = cameraPivotTransform.localPosition;

            steppedPosition = continuousPosition;
            steppedRotation = continuousRotation;
            steppedCameraLocalPos = continuousCameraLocalPos;
            steppedPivotLocalPos = continuousPivotLocalPos;

            currentTargetLeftAndRightAngle = leftAndRightLookAngle = transform.eulerAngles.y;
            float pitch = transform.eulerAngles.x;
            if (pitch > 180) pitch -= 360;
            currentTargetUpAndDownAngle = upAndDownLookAngle = pitch;

            if (WorldGameStateManager.Instance != null) lastTrackedGameState = WorldGameStateManager.Instance.currentState;
        }

        private void LateUpdate()
        {
            if (player == null) return;

            // =====================================================================
            // 🚨 [라이브 에디터 자동 동기화 픽스] 현재 Active된 SO를 인스펙터 슬롯에 강제로 꽂아줍니다!
            // =====================================================================
#if UNITY_EDITOR
            if (autoSyncLiveEditor && WorldCameraManager.Instance != null)
            {
                liveEditSequenceSO = WorldCameraManager.Instance.currentSequenceSO;
                liveEditStanceSO = WorldCameraManager.Instance.currentStanceSO;
            }
#endif

            // 1. 프레임 디커플링 전처리 (이전 연속 좌표 복구)
            if (enableFrameDecoupling && !isFirstFrame)
            {
                transform.position = continuousPosition;
                transform.rotation = continuousRotation;
                if (cameraObject != null) cameraObject.transform.localPosition = continuousCameraLocalPos;
                if (cameraPivotTransform != null) cameraPivotTransform.localPosition = continuousPivotLocalPos;
            }

            // 2. 핵심 로직 실행
            HandleFollowTarget();

            Quaternion baseRotation = HandleRotation();
            baseRotation = HandleMagneticSoftLock(baseRotation);

            HandleCollision();
            ApplyFinalTransform(baseRotation);

            // =====================================================================
            // 3. 디버그 로그 출력 및 호출 원인(Caller) 스마트 분석
            // =====================================================================
            if (showDebugLogs && WorldCameraManager.Instance != null)
            {
                GameState currentGameState = WorldGameStateManager.Instance != null ? WorldGameStateManager.Instance.currentState : GameState.Normal;
                bool currentLockOn = player.playerNetworkManager != null && player.playerNetworkManager.isLockedOn.Value;
                int currentActionState = player.animator != null ? player.animator.GetInteger(AnimatorParameterHash.ActionState) : 0;
                bool currentSeqPlaying = WorldCameraManager.Instance.IsSequencePlaying;

                GetActiveCameraSOs(out var currentSeqSO, out var currentStanceSO);
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

#if UNITY_EDITOR
                    if (forceRawSOValues && liveEditStanceSO != null)
                    {
                        currentStanceName = $"<color=orange>[Live Edit 덮어쓰기 중!]</color> {liveEditStanceSO.name}";
                        soBaseOffset = liveEditStanceSO.baseOffset;
                    }
#endif
                    string targetName = currentLockOn && player.playerCombatManager.currentTarget != null ? player.playerCombatManager.currentTarget.name : "없음";

                    Debug.Log($"<color=cyan>[Camera Coord Debug]</color>\n" +
                              $"★ 현재 제어 상태 ➔ World: <color=yellow>{currentGameState}</color> | LockOn: <color=yellow>{currentLockOn}</color> (타겟: <color=yellow>{targetName}</color>) | ActionID: <color=yellow>{currentActionState}</color> | 시퀀스 재생중: <color=yellow>{currentSeqPlaying}</color>\n" +
                              $"▶ <b>최근 호출 원인(Caller):</b> <color=lime>{lastCallerReason}</color>\n" +
                              $"▶ <b>현재 사용중인 Sequence SO:</b> <color=lime>{currentSeqName}</color>\n" +
                              $"▶ <b>현재 사용중인 Stance SO:</b> <color=lime>{currentStanceName}</color>\n" +
                              $"1. SO Base Offset (적용값): {soBaseOffset}\n" +
                              $"2. 계산된 질량중심(Pivot): {dbgMassCenter}\n" +
                              $"3. 계산된 최종 타겟(DesiredPos): {dbgDesiredPos}\n" +
                              $"4. 메인 렌즈 Local Z(Collision): {targetZPosition:F2} (원인 물체: {lastCollisionObjectName})\n" +
                              $"※ Z값이 0보다 크면 렌즈가 몸 안으로 파묻힙니다. SO 에셋의 Z 오프셋을 음수로 설정하세요.");
                }
            }

            // 4. 프레임 디커플링 후처리
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
            else
            {
                isFirstFrame = true;
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

            // =====================================================================
            // 🚨 [실시간 SO 에디터 패치] Raw 값 다이렉트 덮어쓰기 적용
            // =====================================================================
            Vector3 baseOffset = WorldCameraManager.Instance.currentBaseOffset;
            var dynamicData = WorldCameraManager.Instance.currentDynamicFraming;

#if UNITY_EDITOR
            if (forceRawSOValues && liveEditStanceSO != null)
            {
                baseOffset = liveEditStanceSO.baseOffset;
                dynamicData = liveEditStanceSO.dynamicFraming;
            }
#endif

            float hInput = player.playerNetworkManager != null ? player.playerNetworkManager.animatorHorizontalMovement.Value : 0f;
            float targetFraming = 0f;

            if (hInput < -0.1f) targetFraming = dynamicData.leftStrafeMaxOffset * Mathf.Abs(hInput);
            else if (hInput > 0.1f) targetFraming = dynamicData.rightStrafeMaxOffset * Mathf.Abs(hInput);

            float delayTime = dynamicData.framingDelayTime > 0 ? dynamicData.framingDelayTime : 0.1f;
            currentFramingOffsetX = Mathf.SmoothDamp(currentFramingOffsetX, targetFraming, ref framingVelocity, delayTime);

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

        private Quaternion HandleRotation()
        {
            if (isContextualMode) return transform.rotation;

            if (playerGestureManager == null && player != null)
            {
                playerGestureManager = player.GetComponent<PlayerGestureManager>();
            }

            float currentHorizontal = cameraHorizontalInput;
            float currentVertical = cameraVerticalInput;

            if (playerGestureManager != null && playerGestureManager.IsDragging)
            {
                currentHorizontal = 0f;
                currentVertical = 0f;
            }

            currentTargetLeftAndRightAngle += (currentHorizontal * leftAndRightRotationSpeed * mouseSensitivity) * Time.deltaTime;

            if (Mathf.Abs(currentHorizontal) < 0.01f && Mathf.Abs(currentVertical) < 0.01f)
                noInputTimer += Time.deltaTime;
            else
                noInputTimer = 0f;

            float playerBodyYaw = player.transform.eulerAngles.y;
            float angleDifference = Mathf.DeltaAngle(playerBodyYaw, currentTargetLeftAndRightAngle);

            angleDifference = Mathf.Clamp(angleDifference, -maxViewAngle, maxViewAngle);

            bool isMoving = player.playerNetworkManager != null && player.playerNetworkManager.animatorMoveAmountMovement.Value > 0.1f;

            if (enableAutoCenterOnMove && isMoving)
            {
                if (Mathf.Abs(currentHorizontal) < 0.01f)
                    angleDifference = Mathf.Lerp(angleDifference, 0f, autoCenterSpeed * Time.deltaTime);
            }
            else if (enableAutoCenterOnIdle && !isMoving)
            {
                if (noInputTimer >= idleAutoCenterDelay)
                    angleDifference = Mathf.Lerp(angleDifference, 0f, (autoCenterSpeed * 0.3f) * Time.deltaTime);
            }

            currentTargetLeftAndRightAngle = playerBodyYaw + angleDifference;

            currentTargetUpAndDownAngle -= (currentVertical * upAndDownRotationSpeed * mouseSensitivity) * Time.deltaTime;
            currentTargetUpAndDownAngle = Mathf.Clamp(currentTargetUpAndDownAngle, minimumPivot, maximumPivot);

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

            currentTargetLeftAndRightAngle = leftAndRightLookAngle;
            currentTargetUpAndDownAngle = upAndDownLookAngle;

            return result;
        }

        private void HandleCollision()
        {
            float rawZ = WorldCameraManager.Instance != null ? WorldCameraManager.Instance.currentBaseOffset.z : -2.5f;

#if UNITY_EDITOR
            if (forceRawSOValues && liveEditStanceSO != null) rawZ = liveEditStanceSO.baseOffset.z;
#endif

            debugRawZ = rawZ;
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

            if (targetZPosition > -minimumCollisionOffset)
            {
                targetZPosition = -minimumCollisionOffset;
            }

            debugTargetZ = targetZPosition;

            float avoidanceFactor = 0f;
            if (targetZPosition > -avoidanceThreshold)
            {
                avoidanceFactor = 1f - (Mathf.Abs(targetZPosition) / avoidanceThreshold);
            }

            float targetX = bodyAvoidanceOffset.x * avoidanceFactor;
            float targetY = bodyAvoidanceOffset.y * avoidanceFactor;

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

#if UNITY_EDITOR
            if (forceRawSOValues && liveEditStanceSO != null)
            {
                targetFov = liveEditStanceSO.fov;
                handheld = liveEditStanceSO.handheldEffect;
                zTilt = liveEditStanceSO.zTilt;
            }
#endif

            cameraObject.fieldOfView = targetFov;

            float swayAmount = handheld.enableHandheldEffect ? handheld.swayAmount : 0f;
            float swaySpeed = handheld.enableHandheldEffect ? handheld.swaySpeed : 1f;
            float bobbingAmount = handheld.enableHandheldEffect ? handheld.bobbingAmount : 0f;

            float exhaustionFactor = 1.0f;
            float staminaPerc = 1f;
            if (player.playerNetworkManager != null && player.playerNetworkManager.maxStamina.Value > 0)
                staminaPerc = (float)player.playerNetworkManager.currentStamina.Value / player.playerNetworkManager.maxStamina.Value;

            if (staminaPerc < 0.3f)
                exhaustionFactor += (0.3f - staminaPerc) * staminaExhaustionMultiplier;

            float swayX = (Mathf.PerlinNoise(Time.time * swaySpeed, 0) - 0.5f) * swayAmount * exhaustionFactor * bodycamWeight;
            float swayY = (Mathf.PerlinNoise(0, Time.time * swaySpeed) - 0.5f) * swayAmount * exhaustionFactor * bodycamWeight;
            float swayZ = (Mathf.PerlinNoise(Time.time * swaySpeed, Time.time * swaySpeed) - 0.5f) * (swayAmount * 0.5f) * exhaustionFactor * bodycamWeight;

            float moveAmount = player.playerNetworkManager != null ? player.playerNetworkManager.animatorMoveAmountMovement.Value : 0f;
            float bobSpeed = 12f;
            float bobX = Mathf.Sin(Time.time * bobSpeed) * movementBobbingAmount * moveAmount * bodycamWeight;
            float bobY = Mathf.Abs(Mathf.Cos(Time.time * bobSpeed)) * movementBobbingAmount * moveAmount * bodycamWeight;

            Quaternion bodycamRot = Quaternion.Euler(swayX - bobY, swayY + bobX, swayZ + (bobX * 0.3f) + zTilt);

            Vector3 shakeOffset = Vector3.zero;
            if (shakeDuration > 0)
            {
                shakeOffset = UnityEngine.Random.insideUnitSphere * shakeIntensity;
                shakeDuration -= Time.deltaTime;
            }

            transform.rotation = baseRotation * bodycamRot;
            cameraPivotTransform.localPosition = new Vector3(0, currentPivotHeight, 0) + shakeOffset;
        }

        // =========================================================================================
        // 타겟팅 및 유틸리티
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
            float startH = currentPivotHeight;

            while (timer < duration)
            {
                timer += Time.deltaTime;
                float t = timer / duration;
                float targetH = (player.playerCombatManager.currentTarget != null) ? lockedCameraHeight : unlockedCameraHeight;

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

        public void AdjustCameraYaw(float angleOffset)
        {
            leftAndRightLookAngle += angleOffset;
            currentTargetLeftAndRightAngle += angleOffset;
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
        private bool showLiveEditor = true;

        // 라이브 에디팅 임시 변수 캐싱
        private CameraStancePresetSO lastLiveEditSO;
        private float editFov;
        private Vector3 editBaseOffset;
        private float editZTilt;
        private float editLeftStrafe, editRightStrafe, editFramingDelay;
        private bool editEnableHandheld;
        private float editSwayAmount, editSwaySpeed, editBobbingAmount;
        private bool editEnableShake;
        private float editShakeIntensity, editShakeDelay, editShakeDuration;

        private void SyncFromSO(CameraStancePresetSO so)
        {
            if (so == null) return;
            editFov = so.fov;
            editBaseOffset = so.baseOffset;
            editZTilt = so.zTilt;

            editLeftStrafe = so.dynamicFraming.leftStrafeMaxOffset;
            editRightStrafe = so.dynamicFraming.rightStrafeMaxOffset;
            editFramingDelay = so.dynamicFraming.framingDelayTime;

            editEnableHandheld = so.handheldEffect.enableHandheldEffect;
            editSwayAmount = so.handheldEffect.swayAmount;
            editSwaySpeed = so.handheldEffect.swaySpeed;
            editBobbingAmount = so.handheldEffect.bobbingAmount;

            editEnableShake = so.impactShake.enableShake;
            editShakeIntensity = so.impactShake.shakeIntensity;
            editShakeDelay = so.impactShake.shakeDelay;
            editShakeDuration = so.impactShake.maxDuration;
        }

        private void ApplyToSO(CameraStancePresetSO so)
        {
            if (so == null) return;

            // 실행 취소를 위해 기록
            Undo.RecordObject(so, "Live Edit Camera Stance");

            so.fov = editFov;
            so.baseOffset = editBaseOffset;
            so.zTilt = editZTilt;

            so.dynamicFraming.leftStrafeMaxOffset = editLeftStrafe;
            so.dynamicFraming.rightStrafeMaxOffset = editRightStrafe;
            so.dynamicFraming.framingDelayTime = editFramingDelay;

            so.handheldEffect.enableHandheldEffect = editEnableHandheld;
            so.handheldEffect.swayAmount = editSwayAmount;
            so.handheldEffect.swaySpeed = editSwaySpeed;
            so.handheldEffect.bobbingAmount = editBobbingAmount;

            so.impactShake.enableShake = editEnableShake;
            so.impactShake.shakeIntensity = editShakeIntensity;
            so.impactShake.shakeDelay = editShakeDelay;
            so.impactShake.maxDuration = editShakeDuration;

            // 변경 사항을 에셋에 영구 저장하도록 플래그 꽂기
            EditorUtility.SetDirty(so);
        }

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
            // 🛠️ [신규] SO 라이브 에디터 패널
            // =================================================================================
            EditorGUILayout.Space(15);

            if (pc.liveEditStanceSO != lastLiveEditSO)
            {
                lastLiveEditSO = pc.liveEditStanceSO;
                if (lastLiveEditSO != null) SyncFromSO(lastLiveEditSO);
            }

            showLiveEditor = EditorGUILayout.Foldout(showLiveEditor, "🛠️ SO 라이브 에디터 (Live Editor)", true, new GUIStyle(EditorStyles.foldoutHeader) { fontStyle = FontStyle.Bold });

            if (showLiveEditor)
            {
                EditorGUILayout.BeginVertical("box");
                if (pc.liveEditStanceSO == null)
                {
                    EditorGUILayout.HelpBox("PlayerCamera 컴포넌트의 'Live Edit Stance SO' 슬롯에 1계층 SO 에셋을 할당해야 라이브 에디터가 활성화됩니다.", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField($"현재 편집 중: {pc.liveEditStanceSO.name}", EditorStyles.boldLabel);
                    EditorGUILayout.Space(5);

                    // [렌즈 및 구도]
                    EditorGUILayout.LabelField("📷 렌즈 및 구도 (Lens & Framing)", EditorStyles.miniBoldLabel);
                    editFov = EditorGUILayout.Slider("FOV", editFov, 10f, 120f);
                    editBaseOffset = EditorGUILayout.Vector3Field("Base Offset", editBaseOffset);
                    editZTilt = EditorGUILayout.Slider("Z Tilt", editZTilt, -45f, 45f);
                    EditorGUILayout.Space(5);

                    // [다이내믹 프레이밍]
                    EditorGUILayout.LabelField("🏃 다이내믹 프레이밍 (Dynamic Framing)", EditorStyles.miniBoldLabel);
                    editLeftStrafe = EditorGUILayout.FloatField("Left Strafe Offset", editLeftStrafe);
                    editRightStrafe = EditorGUILayout.FloatField("Right Strafe Offset", editRightStrafe);
                    editFramingDelay = EditorGUILayout.FloatField("Framing Delay", editFramingDelay);
                    EditorGUILayout.Space(5);

                    // [핸드헬드]
                    EditorGUILayout.LabelField("📳 핸드헬드 및 보빙 (Handheld)", EditorStyles.miniBoldLabel);
                    editEnableHandheld = EditorGUILayout.Toggle("Enable Handheld", editEnableHandheld);
                    if (editEnableHandheld)
                    {
                        editSwayAmount = EditorGUILayout.FloatField("Sway Amount", editSwayAmount);
                        editSwaySpeed = EditorGUILayout.FloatField("Sway Speed", editSwaySpeed);
                        editBobbingAmount = EditorGUILayout.FloatField("Bobbing Amount", editBobbingAmount);
                    }
                    EditorGUILayout.Space(5);

                    // [쉐이크]
                    EditorGUILayout.LabelField("💥 카메라 쉐이크 (Impact Shake)", EditorStyles.miniBoldLabel);
                    editEnableShake = EditorGUILayout.Toggle("Enable Shake", editEnableShake);
                    if (editEnableShake)
                    {
                        editShakeIntensity = EditorGUILayout.FloatField("Shake Intensity", editShakeIntensity);
                        editShakeDelay = EditorGUILayout.FloatField("Shake Delay", editShakeDelay);
                        editShakeDuration = EditorGUILayout.FloatField("Max Duration", editShakeDuration);

                        // 쉐이크 테스트 버튼
                        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                        if (GUILayout.Button("💥 설정된 값으로 쉐이크 테스트!", GUILayout.Height(25)))
                        {
                            pc.Shake(editShakeIntensity, editShakeDuration);
                            Debug.Log($"<color=red>[Test]</color> 강도: {editShakeIntensity}, 시간: {editShakeDuration} 쉐이크 발동!");
                        }
                        GUI.backgroundColor = Color.white;
                    }

                    EditorGUILayout.Space(10);

                    // [적용 버튼]
                    GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                    if (GUILayout.Button("💾 변경사항 SO에 즉시 적용 (Apply to SO)", GUILayout.Height(30)))
                    {
                        ApplyToSO(pc.liveEditStanceSO);
                        Debug.Log($"<color=cyan>[Live Editor]</color> {pc.liveEditStanceSO.name} 에셋이 성공적으로 수정 및 적용되었습니다!");
                    }
                    GUI.backgroundColor = Color.white;
                }
                EditorGUILayout.EndVertical();
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
                        var stanceField = gestureMgr.GetType().GetField("currentStance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (stanceField != null) currentStance = stanceField.GetValue(gestureMgr).ToString();
                    }
                }
                catch { }
                DrawField("Weapon Stance", currentStance);

                EditorGUILayout.Space(5);

                EditorGUILayout.LabelField("🎥 Camera Transform & SO Data", subHeaderStyle);

                DrawField("최근 호출 원인 (Caller)", $"<color=lime>{pc.lastCallerReason}</color>", true);

                pc.GetActiveCameraSOs(out var currentSeqSO, out var currentStanceSO);

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
            var field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null) return (T)field.GetValue(target);
            return default;
        }
    }
#endif
}