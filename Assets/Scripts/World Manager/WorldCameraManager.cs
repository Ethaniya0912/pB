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
        private PlayerCamera localPlayerCamera;

        [Header("Default Rest Stance")]
        [Tooltip("시퀀스 연출이 모두 끝났을 때 돌아갈 가장 기본적인 탐험/일상 스탠스 SO")]
        public CameraStancePresetSO defaultRestStance;

        // =========================================================================================
        // [디버그 및 OSD 추적용 변수 복구] 
        // 이전 코드 병합 과정에서 누락되었던 추적용 변수들을 복구하여 OSD에 정상 출력되게 합니다!
        // =========================================================================================
        [Header("State Tracking (OSD 탐색 대상)")]
        public CameraSequencePresetSO currentSequenceSO;
        public CameraStancePresetSO currentStanceSO;

        // =========================================================================================
        // [Data-Driven] 실시간 렌더링 변수 (PlayerCamera가 이 값들을 읽어가서 렌더링합니다)
        // =========================================================================================
        [HideInInspector] public float currentFOV = 60f;
        [HideInInspector] public float currentZTilt = 0f;
        [HideInInspector] public Vector3 currentBaseOffset = new Vector3(0.5f, 1.5f, -2.5f);

        // 🚨 [1순위 Data] 로코모션 및 스탠스에서 렌더링할 런타임 YawOffset 및 추적 가중치(Weight) 선언
        [HideInInspector] public float currentYawOffset = 0f;
        [HideInInspector] public float currentTrackingWeight = 1f;

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
            // 기존에 재생 중이던 카메라 연출이 있다면 가차없이 끊어버리고 가장 최신 명령을 덮어씌웁니다.
            if (activeSequenceCoroutine != null)
            {
                StopCoroutine(activeSequenceCoroutine);
            }

            currentSequenceSO = sequenceSO;
            IsSequencePlaying = true;
            Debug.Log($"<color=magenta>[WorldCameraManager]</color> 🎬 새로운 카메라 시퀀스 재생 시작: <b>{sequenceSO.name}</b>");

            // =========================================================================================
            // 🚨 [0순위 Safe-net] 시퀀스 에셋의 Pause 트리거 검사
            // =========================================================================================
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

        /// <summary>
        /// 진행 중인 시퀀스를 강제로 중단하고 즉각(혹은 부드럽게) 기본 상태로 복구합니다.
        /// (피격 시 인터럽트 등에 사용)
        /// </summary>
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

                // 🚨 [1순위 리팩토링] SequenceStep 규격을 맞추어 임시 복귀 스텝을 생성합니다.
                SequenceStep restoreStep = new SequenceStep
                {
                    targetStance = defaultRestStance,
                    blendDuration = overrideBlendTime,
                    blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
                    trackingStartTime = 0f,
                    trackingEndTime = 1f,
                    trackingWeightCurve = AnimationCurve.Constant(0f, 1f, 1f)
                };

                activeSequenceCoroutine = StartCoroutine(BlendToStanceRoutine(restoreStep));
            }
            else
            {
                IsSequencePlaying = false;
                currentSequenceSO = null;
            }
        }

        // =========================================================================================
        // 타임라인 보간 파서 (Timeline Interpolation Parser)
        // =========================================================================================
        private IEnumerator CameraSequenceRoutine(CameraSequencePresetSO sequence)
        {
            if (sequence.steps != null && sequence.steps.Count > 0)
            {
                for (int i = 0; i < sequence.steps.Count; i++)
                {
                    SequenceStep step = sequence.steps[i];

                    if (step.targetStance == null) continue;

                    currentStanceSO = step.targetStance;

                    // 1. [Blend] 이전 컷에서 목표 스탠스 컷으로 스르륵 보간 이동합니다.
                    if (step.blendDuration > 0)
                    {
                        // 🚨 [1순위 리팩토링] 인자들을 개별로 넘기지 않고 SequenceStep 구조체를 통째로 전달합니다.
                        yield return StartCoroutine(BlendToStanceRoutine(step));
                    }
                    else
                    {
                        // 컷 전환 (Zero Duration) 이면 즉각 덮어씌움
                        ApplyStanceInstantly(step.targetStance);
                    }

                    // 2. [Impact Shake] 카메라 쉐이크(타격감)가 설정되어 있다면 스크립트 트리거 발동
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

                    // 3. [Hold] 컷이 완성된 후 명시된 시간 동안 유지하며 멈춥니다.
                    if (step.holdDuration > 0)
                    {
                        yield return new WaitForSeconds(step.holdDuration);
                    }
                }
            }

            // =========================================================================================
            // 모든 컷(Step) 연출이 끝났다면 복귀(Restore) 정책에 따릅니다.
            // =========================================================================================
            if (sequence.restoreToDefaultStanceOnFinish && defaultRestStance != null)
            {
                currentStanceSO = defaultRestStance;
                if (sequence.restoreBlendDuration > 0)
                {
                    // 🚨 [1순위 리팩토링] 복귀를 위한 가상의 SequenceStep 생성
                    SequenceStep restoreStep = new SequenceStep
                    {
                        targetStance = defaultRestStance,
                        blendDuration = sequence.restoreBlendDuration,
                        blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
                        trackingStartTime = 0f,
                        trackingEndTime = 1f,
                        trackingWeightCurve = AnimationCurve.Constant(0f, 1f, 1f)
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
        /// 전달받은 SequenceStep의 데이터를 바탕으로 비선형 보간과 트래킹 윈도우를 실시간으로 렌더링합니다.
        /// </summary>
        private IEnumerator BlendToStanceRoutine(SequenceStep step)
        {
            float timer = 0f;
            float duration = step.blendDuration;
            CameraStancePresetSO targetStance = step.targetStance;

            // 보간 시작점 스냅샷 캡처
            float startFOV = currentFOV;
            float startZTilt = currentZTilt;
            Vector3 startBaseOffset = currentBaseOffset;
            float startYawOffset = currentYawOffset; // 🚨 1순위: Yaw Offset 보간 시작점

            // 보간 목표점 설정
            float targetFOV = targetStance.fov;
            float targetZTilt = targetStance.zTilt;
            Vector3 targetBaseOffset = targetStance.baseOffset;
            float targetYawOffsetGoal = targetStance.yawOffset; // 🚨 1순위: Yaw Offset 목표점

            // 구조체(동적 프레이밍 등)와 배열 데이터는 보간 중 이질감을 막기 위해 시작 시점에 즉시 덮어씁니다.
            currentDynamicFraming = targetStance.dynamicFraming;
            currentHandheldEffect = targetStance.handheldEffect;

            // 포커싱 타겟 업데이트
            if (targetStance.focusTargets != null && targetStance.focusTargets.Count > 0)
            {
                // 실제 Transform 매핑 로직은 PlayerCamera나 별도 바인딩 클래스에서 타겟 식별자(Enum)를 통해 찾아옵니다.
                // 여기서는 가중치(Weight)만 즉시 주입합니다.
                currentTargetBiasWeight = targetStance.focusTargets[targetStance.focusTargets.Count - 1].weight;
            }

            // 프레임 단위 보간 연산 (Lerp)
            while (timer < duration)
            {
                timer += Time.deltaTime;

                // 🚨 정규화된 시간 (0.0 ~ 1.0) 도출
                float normalizedTime = Mathf.Clamp01(timer / duration);

                // 디자이너가 설정한 커브(Ease-In, Ease-Out 등)를 적용하여 유기적인 가속도를 만듭니다.
                float curveT = (step.blendCurve != null && step.blendCurve.length > 0) ? step.blendCurve.Evaluate(normalizedTime) : normalizedTime;

                // 🚨 1순위 적용: 비선형 커브 보간 적용 (LerpUnclamped를 사용하여 커브의 오버슈트/바운스 효과까지 완벽 지원합니다)
                currentFOV = Mathf.LerpUnclamped(startFOV, targetFOV, curveT);
                currentZTilt = Mathf.LerpUnclamped(startZTilt, targetZTilt, curveT);
                currentBaseOffset = Vector3.LerpUnclamped(startBaseOffset, targetBaseOffset, curveT);
                currentYawOffset = Mathf.LerpUnclamped(startYawOffset, targetYawOffsetGoal, curveT);

                // 🚨 1순위 적용: 멀미 방지를 위한 Tracking Window 가중치 추출
                if (normalizedTime >= step.trackingStartTime && normalizedTime <= step.trackingEndTime)
                {
                    if (step.trackingWeightCurve != null && step.trackingWeightCurve.length > 0)
                    {
                        // 커브에 명시된 가중치를 실시간으로 읽어와 PlayerCamera에 전달합니다.
                        currentTrackingWeight = step.trackingWeightCurve.Evaluate(normalizedTime);
                    }
                    else
                    {
                        currentTrackingWeight = 1f; // 커브가 없으면 기본적으로 100% 추적 허용
                    }
                }
                else
                {
                    // 추적 구간(Window)을 벗어났다면 시선을 강제로 고정(0)시킵니다.
                    currentTrackingWeight = 0f;
                }

                // (※ 이렇게 세팅된 current 변수들을 PlayerCamera.cs의 LateUpdate() 에서 매 프레임 읽어갑니다)
                yield return null;
            }

            // 오차 없는 완벽한 도착을 위해 마지막 프레임에 강제 스냅
            ApplyStanceInstantly(targetStance);
        }

        private void ApplyStanceInstantly(CameraStancePresetSO stance)
        {
            if (stance == null) return;

            currentFOV = stance.fov;
            currentZTilt = stance.zTilt;
            currentBaseOffset = stance.baseOffset;
            currentYawOffset = stance.yawOffset; // 🚨 1순위 적용

            // 🚨 즉각 적용(일반 상태 복귀 등) 시에는 기본적으로 시선 추적을 100% 허용합니다.
            currentTrackingWeight = 1f;

            currentDynamicFraming = stance.dynamicFraming;
            currentHandheldEffect = stance.handheldEffect;

            if (stance.focusTargets != null && stance.focusTargets.Count > 0)
            {
                currentTargetBiasWeight = stance.focusTargets[stance.focusTargets.Count - 1].weight;
            }

            // =========================================================================================
            // 🚨 [0순위 Safe-net] 디버그 모드 시 인스펙터 강제 정지 트리거
            // 목표 컷신/스탠스에 도착한 바로 그 1프레임에 엔진을 멈춰 수치를 편안하게 수정할 수 있게 합니다.
            // =========================================================================================
#if UNITY_EDITOR
            if (stance.pauseOnApply)
            {
                TDA.Character.Player.PlayerCamera localCam = FindFirstObjectByType<TDA.Character.Player.PlayerCamera>();
                if (localCam != null && localCam.showDebugLogs) // 플레이어 카메라의 디버그 허용 여부 체크
                {
                    Debug.Log($"<color=red>[Debug Pause Triggered]</color> <b>{stance.name}</b> 스탠스에 완벽히 도달하여 게임을 강제로 일시정지합니다! (인스펙터 수치를 확인하세요)");
                    EditorApplication.isPaused = true;
                }
            }
#endif
        }

        // =========================================================================================
        // 외부 스크립트용 헬퍼 유틸리티 (기존 API 하위 호환 및 연동 편의성)
        // =========================================================================================

        /// <summary>
        /// PlayerEventManager 등 외부에서 Enum(CameraShake_Heavy 등)으로 인해 단발성 흔들림을 주입해야 할 때 사용합니다.
        /// </summary>
        public void ApplyCameraShake(float intensity, float duration)
        {
            if (localPlayerCamera != null)
            {
                localPlayerCamera.Shake(intensity, duration);
            }
        }
    }

#if UNITY_EDITOR
    // =========================================================================================
    // 🚨 [아키텍처 복원] SO 라이브 에디터 (Live Editor) 관제탑 편입
    // PlayerCamera(렌더러)에 있던 땜빵식 SO 직접 덮어쓰기 기능을, 
    // 데이터 흐름의 원천(Source)인 WorldCameraManager로 완벽하게 복원하여 단방향 아키텍처를 지킵니다.
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
        private float editYawOffset;

        private float editLeftStrafe, editRightStrafe, editFramingDelay;
        private bool editHoldLeft, editHoldRight;
        private float editLeftDelay, editRightDelay, editCenterDelay;

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
            editYawOffset = so.yawOffset;

            editLeftStrafe = so.dynamicFraming.leftStrafeMaxOffset;
            editRightStrafe = so.dynamicFraming.rightStrafeMaxOffset;
            editHoldLeft = so.dynamicFraming.holdLeftStrafe;
            editHoldRight = so.dynamicFraming.holdRightStrafe;
            editLeftDelay = so.dynamicFraming.leftFramingDelay;
            editRightDelay = so.dynamicFraming.rightFramingDelay;
            editCenterDelay = so.dynamicFraming.centerReturnDelay;

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
            so.yawOffset = editYawOffset;

            so.dynamicFraming.leftStrafeMaxOffset = editLeftStrafe;
            so.dynamicFraming.rightStrafeMaxOffset = editRightStrafe;
            so.dynamicFraming.holdLeftStrafe = editHoldLeft;
            so.dynamicFraming.holdRightStrafe = editHoldRight;
            so.dynamicFraming.leftFramingDelay = editLeftDelay;
            so.dynamicFraming.rightFramingDelay = editRightDelay;
            so.dynamicFraming.centerReturnDelay = editCenterDelay;

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
                editYawOffset = EditorGUILayout.Slider("Yaw Offset (좌우 각도)", editYawOffset, -45f, 45f);
                EditorGUILayout.Space(5);

                // [다이내믹 프레이밍]
                EditorGUILayout.LabelField("🏃 다이내믹 프레이밍 (Dynamic Framing)", EditorStyles.miniBoldLabel);

                EditorGUILayout.BeginHorizontal();
                editLeftStrafe = EditorGUILayout.FloatField("Left Max Offset", editLeftStrafe);
                editRightStrafe = EditorGUILayout.FloatField("Right Max Offset", editRightStrafe);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                editHoldLeft = EditorGUILayout.Toggle("Hold Left", editHoldLeft);
                editHoldRight = EditorGUILayout.Toggle("Hold Right", editHoldRight);
                EditorGUILayout.EndHorizontal();

                editLeftDelay = EditorGUILayout.FloatField("Left Speed (Delay)", editLeftDelay);
                editRightDelay = EditorGUILayout.FloatField("Right Speed (Delay)", editRightDelay);
                editCenterDelay = EditorGUILayout.FloatField("Center Return Speed", editCenterDelay);

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

                    // 🚨 [핵심] 아키텍처 복원: 관제탑의 런타임 변수에 바뀐 SO 값을 즉시 다시 꽂아넣습니다!
                    // PlayerCamera는 아무것도 모른 채 이 관제탑의 변수만 바라보게 됩니다.
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