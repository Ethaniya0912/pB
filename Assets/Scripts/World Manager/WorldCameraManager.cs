using System.Collections;
using UnityEngine;
using TDA.Cameras;
using TDA.Character.Player;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TDA.World
{
    /// <summary>
    /// [중앙 관제탑] 1계층/2계층 SO 카메라 데이터를 파싱하여 코루틴 기반으로 
    /// 물리적 카메라 보간(Interpolation) 연산을 실행하고 관리하는 싱글턴 매니저입니다.
    /// </summary>
    public class WorldCameraManager : MonoBehaviour
    {
        public static WorldCameraManager Instance { get; private set; }

        [Header("Local Camera Reference")]
        [Tooltip("현재 씬에 존재하는 로컬 플레이어의 숄더뷰 카메라")]
        public PlayerCamera localPlayerCamera;

        [Header("Default Rest Stance")]
        [Tooltip("시퀀스 연출이 모두 끝났을 때 돌아갈 가장 기본적인 탐험/일상 스탠스 SO")]
        public CameraStancePresetSO defaultRestStance;

        [Header("State Tracking (OSD 탐색 대상)")]
        public CameraSequencePresetSO currentSequenceSO;
        public CameraStancePresetSO currentStanceSO;

        // =========================================================================================
        // [Data-Driven] 실시간 렌더링 변수 (PlayerCamera가 이 값들을 읽어가서 렌더링합니다)
        // =========================================================================================
        [HideInInspector] public float currentFOV = 60f;
        [HideInInspector] public float currentZTilt = 0f;
        [HideInInspector] public Vector3 currentBaseOffset = new Vector3(0.5f, 1.5f, -2.5f);

        [HideInInspector] public float currentBaseYawOffset = 0f;

        // 🚨 [Tracking Window] 멀미 방지용 실시간 추적 가중치 프로퍼티
        public float CurrentTrackingWeight { get; private set; } = 1f;

        // =========================================================================================
        // 🚨 [v3.0 고도화] 액션 댐핑 오버라이드 (Action Damping Override) 프로퍼티
        // 스윙 중 발생하는 1프레임 단위의 덜덜거림을 10kg 스테디캠처럼 짓눌러 억제합니다.
        // =========================================================================================
        public float CurrentPositionDamping { get; private set; } = 0.1f;
        public float CurrentRotationDamping { get; private set; } = 0.1f;

        // 🚨 [신규 추가] 수직(상하) 시점 조작 방식 데이터
        [HideInInspector] public VerticalBehaviorData currentVerticalBehavior;

        [HideInInspector] public DynamicFramingData currentDynamicFraming;
        [HideInInspector] public HandheldNoiseData currentHandheldEffect;

        // 다중 타겟 포커스 (POI) 정보
        [HideInInspector] public Transform[] currentFocusTargets;
        [HideInInspector] public float currentTargetBiasWeight;

        // 코루틴 추적 및 상태
        private Coroutine activeSequenceCoroutine;
        public bool IsSequencePlaying { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            if (defaultRestStance != null)
            {
                ApplyStanceInstantly(defaultRestStance);
            }
        }

        public void SetLocalCamera(PlayerCamera camera)
        {
            localPlayerCamera = camera;

            // 카메라가 세팅될 때 진행 중인 연출이 없다면 기본 스탠스로 초기화
            if (defaultRestStance != null && !IsSequencePlaying)
            {
                currentStanceSO = defaultRestStance;
                ApplyStanceInstantly(defaultRestStance);
            }
        }

        // =========================================================================================
        // [핵심 라우팅] 2계층 시퀀스 연출 재생
        // =========================================================================================
        public void PlayCameraSequence(CameraSequencePresetSO sequenceSO)
        {
            if (sequenceSO == null) return;

            // [우선순위 지배 (Last Call Wins)] 
            if (activeSequenceCoroutine != null)
            {
                StopCoroutine(activeSequenceCoroutine);
            }

            currentSequenceSO = sequenceSO;
            IsSequencePlaying = true;
            Debug.Log($"<color=magenta>[WorldCameraManager]</color> 🎬 새로운 카메라 시퀀스 재생 시작: <b>{sequenceSO.name}</b>");

#if UNITY_EDITOR
            if (sequenceSO.pauseOnApply)
            {
                TDA.Character.Player.PlayerCamera localCam = FindFirstObjectByType<TDA.Character.Player.PlayerCamera>();
                if (localCam != null && localCam.showDebugLogs)
                {
                    Debug.Log($"<color=red>[Debug Pause Triggered]</color> <b>{sequenceSO.name}</b> 시퀀스 재생이 시작되어 게임을 강제로 일시정지합니다!");
                    EditorApplication.isPaused = true;
                }
            }
#endif

            activeSequenceCoroutine = StartCoroutine(CameraSequenceRoutine(sequenceSO));
        }

        public void StopSequenceAndRestore(float overrideBlendTime = 0.5f)
        {
            if (activeSequenceCoroutine != null)
            {
                StopCoroutine(activeSequenceCoroutine);
            }

            Debug.Log($"<color=orange>[WorldCameraManager]</color> ⚠️ 연출 중단! 기본 스탠스로 강제 복귀합니다.");

            if (defaultRestStance != null)
            {
                currentStanceSO = defaultRestStance;

                SequenceStep restoreStep = new SequenceStep
                {
                    targetStance = defaultRestStance,
                    blendDuration = overrideBlendTime,
                    blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
                    trackingStartTime = 0f,
                    trackingEndTime = 1f,
                    trackingWeightCurve = AnimationCurve.Constant(0f, 1f, 1f),
                    actionPositionDamping = 0.1f, // 안전망 초기화
                    actionRotationDamping = 0.1f  // 안전망 초기화
                };

                activeSequenceCoroutine = StartCoroutine(BlendToStanceRoutine(restoreStep));
            }
            else
            {
                IsSequencePlaying = false;
                currentSequenceSO = null;
                CurrentPositionDamping = 0.1f;
                CurrentRotationDamping = 0.1f;
            }
        }

        private IEnumerator CameraSequenceRoutine(CameraSequencePresetSO sequence)
        {
            if (sequence.steps != null && sequence.steps.Count > 0)
            {
                for (int i = 0; i < sequence.steps.Count; i++)
                {
                    SequenceStep step = sequence.steps[i];

                    if (step.targetStance == null) continue;

                    currentStanceSO = step.targetStance;

                    // 1. [Blend] 
                    if (step.blendDuration > 0)
                    {
                        yield return StartCoroutine(BlendToStanceRoutine(step));
                    }
                    else
                    {
                        ApplyStanceInstantly(step.targetStance);
                    }

                    // 2. [Impact Shake] 
                    if (step.targetStance.impactShake.enableShake && localPlayerCamera != null)
                    {
                        if (step.targetStance.impactShake.shakeDelay > 0)
                        {
                            StartCoroutine(DelayedShakeRoutine(step.targetStance.impactShake));
                        }
                        else
                        {
                            localPlayerCamera.Shake(step.targetStance.impactShake.shakeIntensity, step.targetStance.impactShake.maxDuration);
                        }
                    }

                    // 3. [Hold] 
                    if (step.holdDuration > 0)
                    {
                        yield return new WaitForSeconds(step.holdDuration);
                    }
                }
            }

            if (sequence.restoreToDefaultStanceOnFinish && defaultRestStance != null)
            {
                currentStanceSO = defaultRestStance;
                if (sequence.restoreBlendDuration > 0)
                {
                    SequenceStep restoreStep = new SequenceStep
                    {
                        targetStance = defaultRestStance,
                        blendDuration = sequence.restoreBlendDuration,
                        blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
                        trackingStartTime = 0f,
                        trackingEndTime = 1f,
                        trackingWeightCurve = AnimationCurve.Constant(0f, 1f, 1f),
                        actionPositionDamping = 0.1f, // 안전망 초기화
                        actionRotationDamping = 0.1f  // 안전망 초기화
                    };

                    yield return StartCoroutine(BlendToStanceRoutine(restoreStep));
                }
                else
                {
                    ApplyStanceInstantly(defaultRestStance);
                }
            }

            IsSequencePlaying = false;
            currentSequenceSO = null;
            activeSequenceCoroutine = null;

            // 시퀀스가 완전히 종료되었으므로 액션 댐핑 오버라이드 값을 초기화합니다.
            CurrentPositionDamping = 0.1f;
            CurrentRotationDamping = 0.1f;
        }

        private IEnumerator DelayedShakeRoutine(CameraShakeData shakeData)
        {
            yield return new WaitForSeconds(shakeData.shakeDelay);
            if (localPlayerCamera != null)
            {
                localPlayerCamera.Shake(shakeData.shakeIntensity, shakeData.maxDuration);
            }
        }

        /// <summary>
        /// 🚨 [1순위 Core Physics] 프레임 단위 보간 연산 (비선형 커브 및 추적 구간 제어)
        /// </summary>
        private IEnumerator BlendToStanceRoutine(SequenceStep step)
        {
            float timer = 0f;
            float totalDuration = step.blendDuration + step.holdDuration;
            CameraStancePresetSO targetStance = step.targetStance;

            float startFOV = currentFOV;
            float startZTilt = currentZTilt;
            Vector3 startBaseOffset = currentBaseOffset;
            float startBaseYawOffset = currentBaseYawOffset;

            float targetFOV = targetStance.fov;
            float targetZTilt = targetStance.zTilt;
            Vector3 targetBaseOffset = targetStance.baseOffset;
            float targetBaseYawOffsetGoal = targetStance.baseYawOffset;

            // 구조체(동적 프레이밍 등)와 배열 데이터는 보간 중 이질감을 막기 위해 즉시 덮어씁니다.
            currentDynamicFraming = targetStance.dynamicFraming;
            currentHandheldEffect = targetStance.handheldEffect;

            // 🚨 [신규] 수직 시점 조작 데이터 즉시 덮어쓰기
            currentVerticalBehavior = targetStance.verticalBehavior;

            // 🚨 [v3.0 고도화] 2계층 SO 스텝에 정의된 액션 댐핑 오버라이드 추출 및 방출
            // PlayerCamera.cs는 이 값을 가져가서 애니메이션 덜덜거림을 억제합니다.
            CurrentPositionDamping = step.actionPositionDamping > 0f ? step.actionPositionDamping : 0.1f;
            CurrentRotationDamping = step.actionRotationDamping > 0f ? step.actionRotationDamping : 0.1f;

            if (targetStance.focusTargets != null && targetStance.focusTargets.Count > 0)
            {
                currentTargetBiasWeight = targetStance.focusTargets[targetStance.focusTargets.Count - 1].weight;
            }

            while (timer < totalDuration)
            {
                timer += Time.deltaTime;

                float normTime = Mathf.Clamp01(timer / totalDuration);

                // =========================================================================================
                // 🚨 [Tracking Window] 멀미 방지 가중치 계산 (시간 도메인 매핑)
                // =========================================================================================
                if (normTime >= step.trackingStartTime && normTime <= step.trackingEndTime)
                {
                    if (step.trackingWeightCurve != null && step.trackingWeightCurve.length > 0)
                        CurrentTrackingWeight = step.trackingWeightCurve.Evaluate(normTime);
                    else
                        CurrentTrackingWeight = 1f;
                }
                else
                {
                    CurrentTrackingWeight = 0f;
                }

                // 렌즈 및 구도 보간 연산은 blendDuration 동안에만 진행합니다.
                if (timer <= step.blendDuration && step.blendDuration > 0f)
                {
                    float blendNormTime = Mathf.Clamp01(timer / step.blendDuration);
                    float curveT = (step.blendCurve != null && step.blendCurve.length > 0) ? step.blendCurve.Evaluate(blendNormTime) : blendNormTime;

                    currentFOV = Mathf.LerpUnclamped(startFOV, targetFOV, curveT);
                    currentZTilt = Mathf.LerpUnclamped(startZTilt, targetZTilt, curveT);
                    currentBaseOffset = Vector3.LerpUnclamped(startBaseOffset, targetBaseOffset, curveT);
                    currentBaseYawOffset = Mathf.LerpUnclamped(startBaseYawOffset, targetBaseYawOffsetGoal, curveT);
                }

                yield return null;
            }

            // 시퀀스/스텝 종료 시 가중치 원복
            CurrentTrackingWeight = 1f;
            ApplyStanceInstantly(targetStance);
        }

        private void ApplyStanceInstantly(CameraStancePresetSO stance)
        {
            if (stance == null) return;

            currentFOV = stance.fov;
            currentZTilt = stance.zTilt;
            currentBaseOffset = stance.baseOffset;
            currentBaseYawOffset = stance.baseYawOffset;

            CurrentTrackingWeight = 1f;

            currentDynamicFraming = stance.dynamicFraming;
            currentHandheldEffect = stance.handheldEffect;

            // 🚨 [신규] 수직 시점 조작 데이터 즉시 덮어쓰기
            currentVerticalBehavior = stance.verticalBehavior;

            if (stance.focusTargets != null && stance.focusTargets.Count > 0)
            {
                currentTargetBiasWeight = stance.focusTargets[stance.focusTargets.Count - 1].weight;
            }

#if UNITY_EDITOR
            if (stance.pauseOnApply)
            {
                TDA.Character.Player.PlayerCamera localCam = FindFirstObjectByType<TDA.Character.Player.PlayerCamera>();
                if (localCam != null && localCam.showDebugLogs)
                {
                    Debug.Log($"<color=red>[Debug Pause Triggered]</color> <b>{stance.name}</b> 스탠스에 완벽히 도달하여 게임을 강제로 일시정지합니다! (인스펙터 수치를 확인하세요)");
                    EditorApplication.isPaused = true;
                }
            }
#endif
        }

        public void ApplyCameraShake(float intensity, float duration)
        {
            if (localPlayerCamera != null)
            {
                localPlayerCamera.GetType().GetMethod("Shake")?.Invoke(localPlayerCamera, new object[] { intensity, duration });
            }
        }
    }

#if UNITY_EDITOR
    // =========================================================================================
    // 🚨 SO 라이브 에디터 (Live Editor) 관제탑 편입
    // =========================================================================================
    [CustomEditor(typeof(WorldCameraManager))]
    public class WorldCameraManagerEditor : Editor
    {
        private bool showLiveEditor = true;

        // 라이브 에디팅 임시 변수 캐싱
        private CameraStancePresetSO lastLiveEditSO;
        private float editFov;
        private Vector3 editBaseOffset;
        private float editZTilt;
        private float editBaseYawOffset;

        // [신규] 수직 조작 방식 변수 캐싱
        private CameraVerticalBehavior editBehaviorType;
        private float editElevationSpeed;
        private float editMaxElevationHeight;
        private float editMinElevationHeight;
        private float editFixedPitchAngle;
        private float editPitchForMaxHeight;
        private float editMaxDynamicHeight;
        private float editHeightSmoothTime;

        private float editLeftStrafe, editRightStrafe;
        private bool editHoldLeft, editHoldRight;
        private float editLeftDelay, editRightDelay, editCenterDelay;

        private float editLeftStrafeYaw, editRightStrafeYaw;
        private float editDynamicYawWeight;
        private float editForwardBackwardReturnTime;

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
            editBaseYawOffset = so.baseYawOffset;

            // [신규] 수직 조작 방식 동기화
            editBehaviorType = so.verticalBehavior.behaviorType;
            editElevationSpeed = so.verticalBehavior.elevationSpeed;
            editMaxElevationHeight = so.verticalBehavior.maxElevationHeight;
            editMinElevationHeight = so.verticalBehavior.minElevationHeight;
            editFixedPitchAngle = so.verticalBehavior.fixedPitchAngle;
            editPitchForMaxHeight = so.verticalBehavior.pitchForMaxHeight;
            editMaxDynamicHeight = so.verticalBehavior.maxDynamicHeight;
            editHeightSmoothTime = so.verticalBehavior.heightSmoothTime;

            editLeftStrafe = so.dynamicFraming.leftStrafeMaxOffset;
            editRightStrafe = so.dynamicFraming.rightStrafeMaxOffset;
            editHoldLeft = so.dynamicFraming.holdLeftStrafe;
            editHoldRight = so.dynamicFraming.holdRightStrafe;
            editLeftDelay = so.dynamicFraming.leftFramingDelay;
            editRightDelay = so.dynamicFraming.rightFramingDelay;
            editCenterDelay = so.dynamicFraming.centerReturnDelay;

            editLeftStrafeYaw = so.dynamicFraming.leftStrafeYaw;
            editRightStrafeYaw = so.dynamicFraming.rightStrafeYaw;
            editDynamicYawWeight = so.dynamicFraming.dynamicYawWeight;
            editForwardBackwardReturnTime = so.dynamicFraming.forwardBackwardReturnTime;

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

            Undo.RecordObject(so, "Live Edit Camera Stance");

            so.fov = editFov;
            so.baseOffset = editBaseOffset;
            so.zTilt = editZTilt;
            so.baseYawOffset = editBaseYawOffset;

            // [신규] 수직 조작 방식 덮어쓰기
            so.verticalBehavior.behaviorType = editBehaviorType;
            so.verticalBehavior.elevationSpeed = editElevationSpeed;
            so.verticalBehavior.maxElevationHeight = editMaxElevationHeight;
            so.verticalBehavior.minElevationHeight = editMinElevationHeight;
            so.verticalBehavior.fixedPitchAngle = editFixedPitchAngle;
            so.verticalBehavior.pitchForMaxHeight = editPitchForMaxHeight;
            so.verticalBehavior.maxDynamicHeight = editMaxDynamicHeight;
            so.verticalBehavior.heightSmoothTime = editHeightSmoothTime;

            so.dynamicFraming.leftStrafeMaxOffset = editLeftStrafe;
            so.dynamicFraming.rightStrafeMaxOffset = editRightStrafe;
            so.dynamicFraming.holdLeftStrafe = editHoldLeft;
            so.dynamicFraming.holdRightStrafe = editHoldRight;
            so.dynamicFraming.leftFramingDelay = editLeftDelay;
            so.dynamicFraming.rightFramingDelay = editRightDelay;
            so.dynamicFraming.centerReturnDelay = editCenterDelay;

            so.dynamicFraming.leftStrafeYaw = editLeftStrafeYaw;
            so.dynamicFraming.rightStrafeYaw = editRightStrafeYaw;
            so.dynamicFraming.dynamicYawWeight = editDynamicYawWeight;
            so.dynamicFraming.forwardBackwardReturnTime = editForwardBackwardReturnTime;

            so.handheldEffect.enableHandheldEffect = editEnableHandheld;
            so.handheldEffect.swayAmount = editSwayAmount;
            so.handheldEffect.swaySpeed = editSwaySpeed;
            so.handheldEffect.bobbingAmount = editBobbingAmount;

            so.impactShake.enableShake = editEnableShake;
            so.impactShake.shakeIntensity = editShakeIntensity;
            so.impactShake.shakeDelay = editShakeDelay;
            so.impactShake.maxDuration = editShakeDuration;

            EditorUtility.SetDirty(so);
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            WorldCameraManager wcm = (WorldCameraManager)target;

            if (!Application.isPlaying || wcm.currentStanceSO == null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox("▶ 게임을 실행(Play)하면 현재 활성화된 스탠스 SO를 실시간으로 렌더링하고 조작할 수 있는 라이브 에디터가 활성화됩니다.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(15);

            if (wcm.currentStanceSO != lastLiveEditSO)
            {
                lastLiveEditSO = wcm.currentStanceSO;
                if (lastLiveEditSO != null) SyncFromSO(lastLiveEditSO);
            }

            showLiveEditor = EditorGUILayout.Foldout(showLiveEditor, "🛠️ SO 라이브 에디터 (관제탑 제어)", true, new GUIStyle(EditorStyles.foldoutHeader) { fontStyle = FontStyle.Bold });

            if (showLiveEditor)
            {
                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.LabelField($"현재 실시간 렌더링 중인 SO: {wcm.currentStanceSO.name}", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                // [렌즈 및 구도]
                EditorGUILayout.LabelField("📷 렌즈 및 구도 (Lens & Framing)", EditorStyles.miniBoldLabel);
                editFov = EditorGUILayout.Slider("FOV", editFov, 10f, 120f);
                editBaseOffset = EditorGUILayout.Vector3Field("Base Offset", editBaseOffset);
                editZTilt = EditorGUILayout.Slider("Z Tilt", editZTilt, -45f, 45f);
                editBaseYawOffset = EditorGUILayout.Slider("Base Yaw Offset (기본 좌우 각도)", editBaseYawOffset, -45f, 45f);
                EditorGUILayout.Space(5);

                // 🚨 [신규] 수직 시점 조작 패널
                EditorGUILayout.LabelField("↕️ 수직(상하) 시점 조작 (Vertical Behavior)", EditorStyles.miniBoldLabel);
                editBehaviorType = (CameraVerticalBehavior)EditorGUILayout.EnumPopup("Behavior Type", editBehaviorType);

                if (editBehaviorType == CameraVerticalBehavior.ElevationOnly)
                {
                    editElevationSpeed = EditorGUILayout.FloatField("Elevation Speed", editElevationSpeed);
                    editMaxElevationHeight = EditorGUILayout.FloatField("Max Elevation Height", editMaxElevationHeight);
                    editMinElevationHeight = EditorGUILayout.FloatField("Min Elevation Height", editMinElevationHeight);
                    editFixedPitchAngle = EditorGUILayout.FloatField("Fixed Pitch Angle", editFixedPitchAngle);
                }
                else if (editBehaviorType == CameraVerticalBehavior.DynamicOverShoulder)
                {
                    editPitchForMaxHeight = EditorGUILayout.FloatField("Pitch For Max Height", editPitchForMaxHeight);
                    editMaxDynamicHeight = EditorGUILayout.FloatField("Max Dynamic Height", editMaxDynamicHeight);
                    editHeightSmoothTime = EditorGUILayout.FloatField("Height Smooth Time", editHeightSmoothTime);
                }
                EditorGUILayout.Space(5);

                // [다이내믹 프레이밍]
                EditorGUILayout.LabelField("🏃 다이내믹 프레이밍 (Dynamic Framing)", EditorStyles.miniBoldLabel);

                EditorGUILayout.LabelField("Position Offset (X축)", EditorStyles.miniLabel);
                EditorGUILayout.BeginHorizontal();
                editLeftStrafe = EditorGUILayout.FloatField("Left Max Offset", editLeftStrafe);
                editRightStrafe = EditorGUILayout.FloatField("Right Max Offset", editRightStrafe);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                editHoldLeft = EditorGUILayout.Toggle("Hold Left", editHoldLeft);
                editHoldRight = EditorGUILayout.Toggle("Hold Right", editHoldRight);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Rotation Offset (Yaw 각도)", EditorStyles.miniLabel);
                EditorGUILayout.BeginHorizontal();
                editLeftStrafeYaw = EditorGUILayout.FloatField("Left Yaw Angle", editLeftStrafeYaw);
                editRightStrafeYaw = EditorGUILayout.FloatField("Right Yaw Angle", editRightStrafeYaw);
                EditorGUILayout.EndHorizontal();

                editDynamicYawWeight = EditorGUILayout.Slider("Yaw Weight (락온 개입률)", editDynamicYawWeight, 0f, 1f);

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Delay & Speed", EditorStyles.miniLabel);
                editLeftDelay = EditorGUILayout.FloatField("Left Speed (Delay)", editLeftDelay);
                editRightDelay = EditorGUILayout.FloatField("Right Speed (Delay)", editRightDelay);
                editCenterDelay = EditorGUILayout.FloatField("Center Return Speed", editCenterDelay);

                editForwardBackwardReturnTime = EditorGUILayout.FloatField("Return Time (전후진 복귀)", editForwardBackwardReturnTime);
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

                    GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("💥 설정된 값으로 쉐이크 테스트!", GUILayout.Height(25)))
                    {
                        wcm.ApplyCameraShake(editShakeIntensity, editShakeDuration);
                        Debug.Log($"<color=red>[Test]</color> 강도: {editShakeIntensity}, 시간: {editShakeDuration} 쉐이크 발동!");
                    }
                    GUI.backgroundColor = Color.white;
                }

                EditorGUILayout.Space(10);

                // [적용 버튼]
                GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                if (GUILayout.Button("💾 변경사항 SO에 즉시 덮어쓰기 (렌더링 동기화)", GUILayout.Height(30)))
                {
                    ApplyToSO(wcm.currentStanceSO);

                    var m_ApplyStanceMethod = typeof(WorldCameraManager).GetMethod("ApplyStanceInstantly", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (m_ApplyStanceMethod != null)
                    {
                        m_ApplyStanceMethod.Invoke(wcm, new object[] { wcm.currentStanceSO });
                    }

                    Debug.Log($"<color=cyan>[Live Editor]</color> 관제탑 데이터가 갱신되어 {wcm.currentStanceSO.name} 에셋의 수치가 화면에 즉시 적용되었습니다!");
                }
                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndVertical();
            }

            Repaint();
        }
    }
#endif
}