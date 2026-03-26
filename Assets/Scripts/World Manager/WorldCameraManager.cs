using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.Cameras;
using TDA.Character.Player;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TDA.World
{
    /// <summary>
    /// [히스토리 데이터를 담을 구조체]
    /// 에디터 및 런타임에서 최근 발생한 카메라 SO 전환 내역을 추적하기 위해 사용됩니다.
    /// </summary>
    [System.Serializable]
    public struct CameraStateRecord
    {
        public string timestamp;
        public string callerEvent;
        // 🚨 이름(string) 대신 실제 SO 레퍼런스를 담아 에디터에서 즉시 포커싱할 수 있게 합니다.
        public CameraSequencePresetSO seqSO;
        public CameraStancePresetSO stanceSO;
    }

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

        // 🚨 [Phase 4 고도화] 조이스틱 입력 강도(moveAmount)에 따른 동적 수치 변동값 (실시간 더하기 용도)
        [HideInInspector] public float dynamicFOVOffset = 0f;
        [HideInInspector] public float dynamicZOffset = 0f;

        // 🚨 [Phase 3 고도화] 연출 진입 전 이전 앵글(Pitch, Yaw) 스냅샷 저장 데이터
        [HideInInspector] public float storedPitch;
        [HideInInspector] public float storedYaw;
        [HideInInspector] public bool shouldRestorePreviousAngle = false;

        // [v4.4 신규] 연출 진입 전 Dynamic Framing X 오프셋 스냅샷 저장 데이터
        // PlayCameraSequence에서 저장, CameraSequenceRoutine 종료 시 SetFramingOffset으로 복원
        [HideInInspector] public float storedFramingOffsetX = 0f;
        [HideInInspector] public bool shouldRestorePreviousFraming = false;

        // [v4.4 신규] 연출 진입 전 렌즈/구도 스냅샷 저장 데이터
        // restorePreviousStanceValues=true인 시퀀스에서 사용.
        // 시퀀스 종료 후 BlendToStanceRoutine의 startFOV/startBaseYawOffset 등이
        // defaultRestStance 값으로 보간되어 화면에 보이는 문제를 해결합니다.
        [HideInInspector] public float storedFOV = 60f;
        [HideInInspector] public float storedBaseYawOffset = 0f;
        [HideInInspector] public Vector3 storedBaseOffset = new Vector3(0.5f, 1.5f, -2.5f);
        [HideInInspector] public bool shouldRestorePreviousStanceValues = false;

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

        // 🚨 [Phase 5 고도화] 락온 시야 이탈 페널티 및 보정 데이터
        [HideInInspector] public LockOnPenaltyData currentLockOnPenaltyData;

        // 다중 타겟 포커스 (POI) 정보
        [HideInInspector] public Transform[] currentFocusTargets;
        [HideInInspector] public float currentTargetBiasWeight;

        // =========================================================================================
        // 🚨 [신규] 시각적 디버그 툴 플래그 (Visual Debug Flags)
        // =========================================================================================
        [Header("Visual Debugging (On-Screen)")]
        public bool showEdgePanningGizmo = false;
        public bool showTargetEscapeGizmo = false;
        public bool showPredictiveAngleGizmo = false;
        public bool showSOInfoOnScreen = false;

        // 코루틴 추적 및 상태
        private Coroutine activeSequenceCoroutine;
        public bool IsSequencePlaying { get; private set; }

        // =====================================================================
        // 🔹 히스토리 트래킹 데이터
        // =====================================================================
        [HideInInspector]
        public List<CameraStateRecord> historyList = new List<CameraStateRecord>();

        [Header("History Tracking")]
        [Tooltip("메모리 낭비를 막기 위해 유지할 최대 히스토리 개수")]
        [SerializeField] private int maxHistoryCount = 15;



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
                ApplyStanceInstantly(defaultRestStance, "Game Start");
            }
        }

        private void Update()
        {
            // 🚨 [Phase 4 고도화] 인풋(조이스틱 이동량) 기반 동적 수치 개입 실시간 연산
            if (currentStanceSO != null && currentStanceSO.dynamicInputModifier.enableDynamicInputModifier)
            {
                // PlayerInputManager.Instance.moveAmount (이동 강도 0~1) 값을 참조
                float moveAmount = PlayerInputManager.Instance != null ? PlayerInputManager.Instance.moveAmount : 0f;

                // SO 커브에 값을 대입(Evaluate)하여 결과 산출
                dynamicFOVOffset = currentStanceSO.dynamicInputModifier.fovModifierCurve.Evaluate(moveAmount);
                dynamicZOffset = currentStanceSO.dynamicInputModifier.offsetZModifierCurve.Evaluate(moveAmount);
            }
            else
            {
                // 사용하지 않을 경우 오프셋을 0으로 초기화
                dynamicFOVOffset = 0f;
                dynamicZOffset = 0f;
            }

            // [Implementation Spec] 매 프레임 FocusTargets를 씬 Transform으로 갱신
            // TargetIdentifier enum → 실제 씬 Transform 변환 (요구사항 2)
            ResolveFocusTargetsToWorld();
        }

        public void SetLocalCamera(PlayerCamera camera)
        {
            localPlayerCamera = camera;

            // 카메라가 세팅될 때 진행 중인 연출이 없다면 기본 스탠스로 초기화
            if (defaultRestStance != null && !IsSequencePlaying)
            {
                currentStanceSO = defaultRestStance;
                ApplyStanceInstantly(defaultRestStance, "Set Local Camera");
            }
        }

        // =====================================================================
        // 🔹 히스토리 기록 로직
        // =====================================================================
        /// <summary>
        /// 상태 전환 시 호출하여 관제탑에 기록을 남기는 함수
        /// </summary>
        // =============================================================================
        // [Implementation Spec 신규] 다중 타겟 FocusTargets 해석기 — 요구사항 2
        // SO의 TargetIdentifier enum을 씬의 실제 Transform으로 변환합니다.
        // Update()에서 매 프레임 호출됩니다.
        // =============================================================================
        private void ResolveFocusTargetsToWorld()
        {
            if (currentStanceSO == null
                || currentStanceSO.focusTargets == null
                || currentStanceSO.focusTargets.Count == 0)
            {
                currentFocusTargets = null;
                return;
            }

            // 배열 크기가 다를 때만 재할당 (GC 압력 최소화)
            if (currentFocusTargets == null
                || currentFocusTargets.Length != currentStanceSO.focusTargets.Count)
            {
                currentFocusTargets = new Transform[currentStanceSO.focusTargets.Count];
            }

            for (int i = 0; i < currentStanceSO.focusTargets.Count; i++)
            {
                currentFocusTargets[i] = ResolveIdentifier(currentStanceSO.focusTargets[i].target);
            }
        }

        /// <summary>
        /// TargetIdentifier enum → 씬 실제 Transform 변환
        /// </summary>
        private Transform ResolveIdentifier(TargetIdentifier id)
        {
            if (localPlayerCamera == null) return null;
            TDA.Character.Player.PlayerManager playerMgr = localPlayerCamera.player;
            if (playerMgr == null) return null;

            switch (id)
            {
                case TargetIdentifier.PlayerRoot:
                    return playerMgr.transform;

                case TargetIdentifier.PlayerChest:
                    // lockOnTransform이 있으면 그것을, 없으면 루트
                    return playerMgr.playerCombatManager?.lockOnTransform
                           ?? playerMgr.transform;

                case TargetIdentifier.PlayerWeaponTip:
                    // 무기 끝 Transform이 있으면 반환, 없으면 루트
                    return playerMgr.transform; // TODO: weaponTipTransform 레퍼런스 확보 후 교체

                case TargetIdentifier.PlayerShield:
                    return playerMgr.transform; // TODO: shieldTransform 레퍼런스 확보 후 교체

                case TargetIdentifier.LockedOnEnemyRoot:
                    return playerMgr.playerCombatManager?.currentTarget?.transform;

                case TargetIdentifier.LockedOnEnemyChest:
                    return playerMgr.playerCombatManager?.currentTarget
                           ?.characterCombatManager?.lockOnTransform;

                case TargetIdentifier.InteractableObject:
                    // 현재 상호작용 오브젝트 — 향후 InteractionManager 구현 후 교체
                    return null;

                default:
                    return null;
            }
        }
        // =============================================================================

        public void RecordCameraState(string caller, CameraSequencePresetSO seqSO, CameraStancePresetSO stanceSO)
        {
            CameraStateRecord record = new CameraStateRecord
            {
                timestamp = System.DateTime.Now.ToString("HH:mm:ss.fff"),
                callerEvent = caller,
                seqSO = seqSO,
                stanceSO = stanceSO
            };

            // 최신 기록이 맨 위(0번 인덱스)로 오도록 삽입
            historyList.Insert(0, record);

            // 최대 개수를 초과하면 가장 오래된 기록 삭제
            if (historyList.Count > maxHistoryCount)
            {
                historyList.RemoveAt(historyList.Count - 1);
            }
        }

        // =========================================================================================
        // [외부 스탠스 강제 전환] (PlayerCombatManager 등에서 호출 가능)
        // =========================================================================================
        public void ChangeCameraStance(CameraStancePresetSO newStance, string callerName = "Unknown")
        {
            if (newStance == null) return;

            currentStanceSO = newStance;
            ApplyStanceInstantly(newStance, callerName);
        }

        // =========================================================================================
        // [핵심 라우팅] 2계층 시퀀스 연출 재생
        // =========================================================================================
        public void PlayCameraSequence(CameraSequencePresetSO sequenceSO, string callerName = "Unknown")
        {
            if (sequenceSO == null) return;

            // [v3.9 Fix] 동일 시퀀스가 이미 재생 중이면 재시작 방지
            // BaseActionBehaviour가 공격 애니메이션마다 Seq_LockOn을 반복 호출할 때
            // 매번 BlendToStanceRoutine이 재시작되며 velocity가 누적/폭발하는 현상 차단
            if (IsSequencePlaying && currentSequenceSO == sequenceSO)
            {
                // 완전히 동일한 시퀀스 재호출 → 무시
                return;
            }

            // [우선순위 지배 (Last Call Wins)]
            if (activeSequenceCoroutine != null)
            {
                StopCoroutine(activeSequenceCoroutine);
                // [v3.9 Fix] 이전 시퀀스 중단 시 velocity 리셋 (중단 직후 새 시퀀스 시작 전)
                if (localPlayerCamera != null) localPlayerCamera.ResetVelocities();
            }

            // [Implementation Spec] 시퀀스 진입 시 이전 각도 스냅샷 캐싱 (복귀용)
            // [요구사항 4] Reflection 제거 → GetCurrentAngles() public 메서드 직접 호출
            if (sequenceSO.restorePreviousAngle && localPlayerCamera != null)
            {
                var angles = localPlayerCamera.GetCurrentAngles();
                storedYaw = angles.yaw;
                storedPitch = angles.pitch;
                shouldRestorePreviousAngle = true;
            }
            else
            {
                shouldRestorePreviousAngle = false;
            }

            // [v4.4 신규] 시퀀스 진입 시 Dynamic Framing X 오프셋 스냅샷 저장
            if (sequenceSO.restorePreviousFraming && localPlayerCamera != null)
            {
                storedFramingOffsetX = localPlayerCamera.GetCurrentFramingOffset();
                shouldRestorePreviousFraming = true;
                Debug.Log($"[WorldCameraManager] 프레이밍 스냅샷 저장: {storedFramingOffsetX:F3}");
            }
            else
            {
                shouldRestorePreviousFraming = false;
            }

            // [v4.4 신규] 시퀀스 진입 시 렌즈·구도 값 스냅샷 저장
            // restorePreviousStanceValues=true이면 현재 FOV/baseOffset/baseYawOffset을 저장.
            // 시퀀스 종료 후 defaultRestStance로 복귀하는 BlendToStanceRoutine의
            // 시작값(startFOV 등)을 이 스냅샷으로 교체하여 화면 플리커를 방지합니다.
            if (sequenceSO.restorePreviousStanceValues)
            {
                storedFOV = currentFOV;
                storedBaseOffset = currentBaseOffset;
                storedBaseYawOffset = currentBaseYawOffset;
                shouldRestorePreviousStanceValues = true;
                Debug.Log($"[WorldCameraManager] 렌즈·구도 스냅샷 저장: FOV={storedFOV:F1} YawOff={storedBaseYawOffset:F1}");
            }
            else
            {
                shouldRestorePreviousStanceValues = false;
            }

            currentSequenceSO = sequenceSO;
            IsSequencePlaying = true;
            Debug.Log($"<color=magenta>[WorldCameraManager]</color> 🎬 새로운 카메라 시퀀스 재생 시작: <b>{sequenceSO.name}</b>");

            // 히스토리에 실제 SO 레퍼런스를 전달
            RecordCameraState(callerName, sequenceSO, currentStanceSO);

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

        public void StopSequenceAndRestore(float overrideBlendTime = 0.5f, string callerName = "StopSequenceForce")
        {
            if (activeSequenceCoroutine != null)
            {
                StopCoroutine(activeSequenceCoroutine);
            }

            Debug.Log($"<color=orange>[WorldCameraManager]</color> ⚠️ 연출 중단! 기본 스탠스로 강제 복귀합니다.");

            if (defaultRestStance != null)
            {
                currentStanceSO = defaultRestStance;

                // 히스토리 기록
                RecordCameraState(callerName, null, defaultRestStance);

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
                        ApplyStanceInstantly(step.targetStance, "Sequence Routine");
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
                    ApplyStanceInstantly(defaultRestStance, "Sequence Restore");
                }
            }

            // =================================================================
            // [v4.4] 시퀀스 종료 후 프레이밍 오프셋 복원
            // restorePreviousFraming=true이고 스냅샷이 저장된 경우에만 복원합니다.
            // SetFramingOffset(blendTime>0)을 호출하면 PlayerCamera.HandleFollowTarget이
            // 매 프레임 SmoothStep으로 부드럽게 목표값까지 보간합니다.
            // =================================================================
            if (shouldRestorePreviousFraming && localPlayerCamera != null)
            {
                float blendT = (currentSequenceSO != null)
                    ? currentSequenceSO.restoreFramingBlendTime
                    : 0.3f;
                // blendT를 sequence에서 읽어야 하는데 이미 null이 될 수 있으므로
                // 아래에서 null 처리 후 복원
                blendT = sequence.restoreFramingBlendTime;
                localPlayerCamera.SetFramingOffset(storedFramingOffsetX, blendT);
                shouldRestorePreviousFraming = false;
                Debug.Log($"[WorldCameraManager] 프레이밍 복원 요청: {storedFramingOffsetX:F3} (blend:{blendT:F2}s)");
            }
            // =================================================================

            IsSequencePlaying = false;
            currentSequenceSO = null;
            activeSequenceCoroutine = null;

            // 시퀀스가 완전히 종료되었으므로 액션 댐핑 오버라이드 및 상태를 초기화합니다.
            CurrentPositionDamping = 0.1f;
            CurrentRotationDamping = 0.1f;
            shouldRestorePreviousAngle = false;
            shouldRestorePreviousFraming = false;
            shouldRestorePreviousStanceValues = false;
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
            // [v3.9] 보간 시작 직전 velocity 리셋 — 이전 velocity 누적으로 인한 발산 방지
            // [v4.4] shouldRestorePreviousFraming=true이면 framing 수치를 보존 (공격 후 구도 유지)
            if (localPlayerCamera != null)
            {
                localPlayerCamera.ResetVelocities(preserveFraming: shouldRestorePreviousFraming);
            }

            float timer = 0f;
            float totalDuration = step.blendDuration + step.holdDuration;
            CameraStancePresetSO targetStance = step.targetStance;

            // [v4.4] shouldRestorePreviousStanceValues=true이면 startFOV 등을 스냅샷 값으로 교체.
            // defaultRestStance로 복귀하는 BlendToStanceRoutine에서 currentFOV(=Stance_Combat)에서
            // defaultRestStance.fov로 보간되는 대신, 스냅샷(시퀀스 진입 전 값)에서
            // targetStance.fov로 보간하여 화면에 불필요한 FOV/YawOffset 변화가 생기지 않게 합니다.
            float startFOV = shouldRestorePreviousStanceValues ? storedFOV : currentFOV;
            float startZTilt = currentZTilt;
            Vector3 startBaseOffset = shouldRestorePreviousStanceValues ? storedBaseOffset : currentBaseOffset;
            float startBaseYawOffset = shouldRestorePreviousStanceValues ? storedBaseYawOffset : currentBaseYawOffset;

            // 스냅샷을 사용했으면 현재 렌더 값도 즉시 스냅샷으로 덮어써서 시각적 점프를 방지합니다.
            if (shouldRestorePreviousStanceValues)
            {
                currentFOV = storedFOV;
                currentBaseOffset = storedBaseOffset;
                currentBaseYawOffset = storedBaseYawOffset;
                shouldRestorePreviousStanceValues = false; // 한 번만 적용
            }

            float targetFOV = targetStance.fov;
            float targetZTilt = targetStance.zTilt;
            Vector3 targetBaseOffset = targetStance.baseOffset;
            float targetBaseYawOffsetGoal = targetStance.baseYawOffset;

            // 구조체(동적 프레이밍 등)와 배열 데이터는 보간 중 이질감을 막기 위해 즉시 덮어씁니다.
            currentDynamicFraming = targetStance.dynamicFraming;
            currentHandheldEffect = targetStance.handheldEffect;

            // 🚨 [신규] 수직 시점 조작 및 페널티 데이터 즉시 덮어쓰기
            currentVerticalBehavior = targetStance.verticalBehavior;
            currentLockOnPenaltyData = targetStance.lockOnPenaltyData;

            // 🚨 [v3.0 고도화] 2계층 SO 스텝에 정의된 액션 댐핑 오버라이드 추출 및 방출
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

                    // [v3.9 안전장치] 발산 감지 시 강제 스냅
                    if (float.IsNaN(currentBaseYawOffset) || Mathf.Abs(currentBaseYawOffset) > 360f)
                    {
                        currentBaseYawOffset = targetBaseYawOffsetGoal;
                        Debug.LogWarning("[WorldCameraManager] currentBaseYawOffset 발산 감지 — 강제 스냅");
                    }
                }

                yield return null;
            }

            // 시퀀스/스텝 종료 시 가중치 원복
            CurrentTrackingWeight = 1f;
            ApplyStanceInstantly(targetStance, "Blend Finish");
        }

        private void ApplyStanceInstantly(CameraStancePresetSO stance, string callerName = "Unknown")
        {
            if (stance == null) return;

            currentFOV = stance.fov;
            currentZTilt = stance.zTilt;
            currentBaseOffset = stance.baseOffset;
            currentBaseYawOffset = stance.baseYawOffset;

            CurrentTrackingWeight = 1f;

            currentDynamicFraming = stance.dynamicFraming;
            currentHandheldEffect = stance.handheldEffect;

            currentVerticalBehavior = stance.verticalBehavior;
            currentLockOnPenaltyData = stance.lockOnPenaltyData; // 🚨 [Phase 5 고도화] 덮어쓰기

            if (stance.focusTargets != null && stance.focusTargets.Count > 0)
            {
                currentTargetBiasWeight = stance.focusTargets[stance.focusTargets.Count - 1].weight;
            }

            // [v3.9 핵심 버그 수정] SmoothDamp velocity 전부 리셋
            // 락온 진입처럼 카메라 상태가 급격히 바뀔 때 이전에 쌓인 cameraVelocity /
            // framingVelocity가 새 desiredPos 방향과 충돌하여 카메라가 수천만m 날아가는
            // 폭발(SmoothDamp divergence) 현상을 원천 차단합니다.
            // [v4.4] shouldRestorePreviousFraming=true이면 framing 수치를 보존
            if (localPlayerCamera != null)
            {
                localPlayerCamera.ResetVelocities(preserveFraming: shouldRestorePreviousFraming);
            }

            // 상태 즉시 적용 시 히스토리 기록 (객체 레퍼런스 전달)
            RecordCameraState(callerName, currentSequenceSO, stance);

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

        // =========================================================================================
        // 🚨 [신규] 온스크린 시각적 디버그 드로잉 (On-Screen Debug UI)
        // =========================================================================================
        private void OnGUI()
        {
            if (!Application.isPlaying) return;

            // 1. 현재 적용 중인 SO 정보 텍스트 (상단 왼쪽)
            if (showSOInfoOnScreen)
            {
                GUIStyle textStyle = new GUIStyle(GUI.skin.label);
                textStyle.fontSize = 14;
                textStyle.fontStyle = FontStyle.Bold;
                textStyle.normal.textColor = Color.cyan;

                string seqName = currentSequenceSO != null ? currentSequenceSO.name : "None";
                string stanceName = currentStanceSO != null ? currentStanceSO.name : "None";

                GUILayout.BeginArea(new Rect(10, 10, 500, 100));
                GUILayout.Label($"🎬 Sequence: {seqName}", textStyle);
                GUILayout.Label($"📸 Stance: {stanceName}", textStyle);
                GUILayout.EndArea();
            }

            // 2. 엣지 패닝 범위 가이드 (빨간색 투명 박스)
            if (showEdgePanningGizmo && currentStanceSO != null)
            {
                float threshold = currentStanceSO.edgePanningData.edgePanThreshold;
                DrawThresholdRect(threshold, new Color(1f, 0f, 0f, 0.15f), "Edge Panning Area");
            }

            // 3. 타겟 이탈 페널티 범위 가이드 (노란색 투명 박스)
            if (showTargetEscapeGizmo && currentStanceSO != null)
            {
                float threshold = currentStanceSO.lockOnPenaltyData.targetEscapeViewportThreshold;
                DrawThresholdRect(threshold, new Color(1f, 1f, 0f, 0.15f), "Target Escape Boundary");
            }
        }

        private void DrawThresholdRect(float threshold, Color color, string label)
        {
            float sw = Screen.width;
            float sh = Screen.height;
            float tw = sw * threshold;
            float th = sh * threshold;

            GUI.color = color;
            // 상하좌우 엣지 박스 그리기
            GUI.DrawTexture(new Rect(0, 0, tw, sh), Texture2D.whiteTexture);           // Left
            GUI.DrawTexture(new Rect(sw - tw, 0, tw, sh), Texture2D.whiteTexture);      // Right
            GUI.DrawTexture(new Rect(0, 0, sw, th), Texture2D.whiteTexture);           // Top
            GUI.DrawTexture(new Rect(0, sh - th, sw, th), Texture2D.whiteTexture);      // Bottom
            GUI.color = Color.white;
        }

        // =========================================================================================
        // 🚨 [신규] 카메라 방향 및 목표 예측 기즈모 (Scene View Only)
        // =========================================================================================
        private void OnDrawGizmos()
        {
            if (!showPredictiveAngleGizmo || localPlayerCamera == null) return;

            // 카메라가 가고자 하는 위치(Desired Position)와 시선 방향을 기즈모로 표시
            // PlayerCamera 내부의 desiredPos 필드에 접근 (Reflection)
            Vector3 desiredPos = (Vector3)(localPlayerCamera.GetType().GetField("dbgDesiredPos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(localPlayerCamera) ?? localPlayerCamera.transform.position);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(desiredPos, 0.3f);
            Gizmos.DrawLine(localPlayerCamera.transform.position, desiredPos);

            // 카메라 전방 시선 방향 예측 (Frustum 형태)
            Gizmos.matrix = Matrix4x4.TRS(desiredPos, localPlayerCamera.transform.rotation, Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero, currentFOV, 2f, 0.1f, 1.0f);
        }

        // =====================================================================
        // QTE (Quick Time Event) 카메라 제어
        // CharacterQTEManager에서 StartQTE(phases) 호출 → 카메라 연출 분기
        // =====================================================================

        /// <summary>
        /// QTE를 시작하고 첫 번째 단계의 카메라 시퀀스를 재생합니다.
        /// CharacterQTEManager.StartQTE()에서 호출됩니다.
        /// </summary>
        public void StartQTE(System.Collections.Generic.List<TDA.Cameras.CameraQTEPhaseData> phases)
        {
            if (phases == null || phases.Count == 0) return;
            var firstPhase = phases[0];
            if (firstPhase.cameraSequence != null)
                PlayCameraSequence(firstPhase.cameraSequence, "QTE_Start");
        }

        /// <summary>
        /// 현재 QTE 단계의 성공/실패를 처리하고 다음 단계 카메라를 재생합니다.
        /// CharacterQTEManager.AdvanceToPhase()에서 호출됩니다.
        /// </summary>
        public void ResolveQTEPhase(bool success)
        {
            if (!success)
                StopSequenceAndRestore(0.3f, "QTE_Fail");
        }

        /// <summary>
        /// QTE를 완전히 종료하고 카메라를 기본 상태로 복귀시킵니다.
        /// CharacterQTEManager.CleanUpQTE()에서 호출됩니다.
        /// </summary>
        public void EndQTE(bool success)
        {
            StopSequenceAndRestore(success ? 0.5f : 0.2f, success ? "QTE_Success" : "QTE_Fail");
        }


    }

#if UNITY_EDITOR
    // =========================================================================================
    // 🚨 SO 라이브 에디터 (Live Editor) 관제탑 편입 및 히스토리 뷰어
    // =========================================================================================
    [CustomEditor(typeof(WorldCameraManager))]
    public class WorldCameraManagerEditor : Editor
    {
        private bool showLiveEditor = true;
        private bool showHistoryPanel = true;

        private Vector2 historyScrollPos;

        // 라이브 에디팅 임시 변수 캐싱
        private CameraStancePresetSO lastLiveEditSO;
        private float editFov;
        private Vector3 editBaseOffset;
        private float editZTilt;
        private float editBaseYawOffset;

        // 컴파일 에러 해결 (CS0266, CS0117): 명시적 네임스페이스 지정
        private TDA.Cameras.CameraVerticalBehavior editBehaviorType;
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

        // 🚨 [Phase 5 고도화] 라이브 에디터용 시야 이탈 페널티 캐싱
        private bool editEnableTargetEscapePenalty;
        private float editTargetEscapeViewportThreshold;
        private bool editUseHardCorrection;
        private AnimationCurve editSoftCorrectionDistanceCurve;
        private float editStrafeRecoveryWeight;

        private void SyncFromSO(CameraStancePresetSO so)
        {
            if (so == null) return;
            editFov = so.fov;
            editBaseOffset = so.baseOffset;
            editZTilt = so.zTilt;
            editBaseYawOffset = so.baseYawOffset;

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

            // 🚨 [Phase 5 고도화] 동기화
            editEnableTargetEscapePenalty = so.lockOnPenaltyData.enableTargetEscapePenalty;
            editTargetEscapeViewportThreshold = so.lockOnPenaltyData.targetEscapeViewportThreshold;
            editUseHardCorrection = so.lockOnPenaltyData.useHardCorrection;
            editSoftCorrectionDistanceCurve = so.lockOnPenaltyData.softCorrectionDistanceCurve;
            editStrafeRecoveryWeight = so.lockOnPenaltyData.strafeRecoveryWeight;
        }

        private void ApplyToSO(CameraStancePresetSO so)
        {
            if (so == null) return;

            Undo.RecordObject(so, "Live Edit Camera Stance");

            so.fov = editFov;
            so.baseOffset = editBaseOffset;
            so.zTilt = editZTilt;
            so.baseYawOffset = editBaseYawOffset;

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

            // 🚨 [Phase 5 고도화] 적용
            so.lockOnPenaltyData.enableTargetEscapePenalty = editEnableTargetEscapePenalty;
            so.lockOnPenaltyData.targetEscapeViewportThreshold = editTargetEscapeViewportThreshold;
            so.lockOnPenaltyData.useHardCorrection = editUseHardCorrection;
            so.lockOnPenaltyData.softCorrectionDistanceCurve = editSoftCorrectionDistanceCurve;
            so.lockOnPenaltyData.strafeRecoveryWeight = editStrafeRecoveryWeight;

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

                // [수직 시점 조작 패널]
                EditorGUILayout.LabelField("↕️ 수직(상하) 시점 조작 (Vertical Behavior)", EditorStyles.miniBoldLabel);
                // 컴파일 에러 해결 (CS0266): 명시적 네임스페이스 캐스팅
                editBehaviorType = (TDA.Cameras.CameraVerticalBehavior)EditorGUILayout.EnumPopup("Behavior Type", editBehaviorType);

                // 컴파일 에러 해결: Enum 비교 시 명시적 네임스페이스 사용
                if (editBehaviorType == TDA.Cameras.CameraVerticalBehavior.ElevationOnly)
                {
                    editElevationSpeed = EditorGUILayout.FloatField("Elevation Speed", editElevationSpeed);
                    editMaxElevationHeight = EditorGUILayout.FloatField("Max Elevation Height", editMaxElevationHeight);
                    editMinElevationHeight = EditorGUILayout.FloatField("Min Elevation Height", editMinElevationHeight);
                    editFixedPitchAngle = EditorGUILayout.FloatField("Fixed Pitch Angle", editFixedPitchAngle);
                }
                else if (editBehaviorType == TDA.Cameras.CameraVerticalBehavior.DynamicOverShoulder)
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

                // 🚨 [Phase 5 고도화] 시야 이탈 페널티 에디터 추가
                EditorGUILayout.LabelField("⚠️ 시야 이탈 페널티 (Target Escape Penalty)", EditorStyles.miniBoldLabel);
                editEnableTargetEscapePenalty = EditorGUILayout.Toggle("Enable Penalty", editEnableTargetEscapePenalty);

                if (editEnableTargetEscapePenalty)
                {
                    EditorGUI.indentLevel++;
                    editTargetEscapeViewportThreshold = EditorGUILayout.Slider("Escape Threshold", editTargetEscapeViewportThreshold, 0f, 0.5f);
                    editUseHardCorrection = EditorGUILayout.Toggle("Use Hard Correction", editUseHardCorrection);

                    if (!editUseHardCorrection)
                    {
                        editSoftCorrectionDistanceCurve = EditorGUILayout.CurveField("Soft Correction Curve", editSoftCorrectionDistanceCurve);
                    }

                    editStrafeRecoveryWeight = EditorGUILayout.FloatField("Strafe Recovery Weight", editStrafeRecoveryWeight);
                    EditorGUI.indentLevel--;
                }
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
                        m_ApplyStanceMethod.Invoke(wcm, new object[] { wcm.currentStanceSO, "Live Editor Update" });
                    }

                    Debug.Log($"<color=cyan>[Live Editor]</color> 관제탑 데이터가 갱신되어 {wcm.currentStanceSO.name} 에셋의 수치가 화면에 즉시 적용되었습니다!");
                }
                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndVertical();
            }

            // =================================================================================
            // 📜 3. 카메라 상태 변경 히스토리 패널 (버튼화 적용)
            // =================================================================================
            EditorGUILayout.Space(10);
            showHistoryPanel = EditorGUILayout.Foldout(showHistoryPanel, "📜 카메라 상태 변경 히스토리 (Stance & Seq)", true, new GUIStyle(EditorStyles.foldoutHeader) { fontStyle = FontStyle.Bold });

            if (showHistoryPanel)
            {
                EditorGUILayout.BeginVertical("box");

                // 테이블 헤더
                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.LabelField("발생 시간", EditorStyles.boldLabel, GUILayout.Width(85));
                EditorGUILayout.LabelField("호출 스크립트/이벤트명", EditorStyles.boldLabel, GUILayout.Width(180));
                EditorGUILayout.LabelField("Seq SO명 (클릭하여 열기)", EditorStyles.boldLabel, GUILayout.Width(150));
                EditorGUILayout.LabelField("Stance SO명 (클릭하여 열기)", EditorStyles.boldLabel, GUILayout.Width(150));
                EditorGUILayout.EndHorizontal();

                // 데이터 검증 및 출력
                if (wcm.historyList == null || wcm.historyList.Count == 0)
                {
                    EditorGUILayout.HelpBox("기록된 SO 변경 히스토리가 없습니다. (WorldCameraManager.RecordCameraState 호출 시 자동 기록됨)", MessageType.Info);
                }
                else
                {
                    // 클릭 가능한 버튼 스타일 세팅
                    GUIStyle linkBtnStyle = new GUIStyle(GUI.skin.button);
                    linkBtnStyle.alignment = TextAnchor.MiddleLeft;
                    linkBtnStyle.fontSize = 11;

                    // 스크롤 뷰 지원
                    historyScrollPos = EditorGUILayout.BeginScrollView(historyScrollPos, GUILayout.Height(220));

                    for (int i = 0; i < wcm.historyList.Count; i++)
                    {
                        var record = wcm.historyList[i];

                        // 지브라스트라이핑(교차 배경색)
                        Color bgColor = i % 2 == 0 ? new Color(0.2f, 0.2f, 0.2f, 0.3f) : new Color(0.3f, 0.3f, 0.3f, 0.1f);
                        GUI.backgroundColor = bgColor;
                        EditorGUILayout.BeginHorizontal("helpbox");
                        GUI.backgroundColor = Color.white;

                        // 데이터 출력
                        EditorGUILayout.LabelField(record.timestamp, GUILayout.Width(85));
                        EditorGUILayout.LabelField(record.callerEvent, GUILayout.Width(180));

                        // 🚨 Seq SO 버튼 처리
                        string seqName = record.seqSO != null ? record.seqSO.name : "-";
                        if (GUILayout.Button(seqName, linkBtnStyle, GUILayout.Width(150)))
                        {
                            if (record.seqSO != null)
                            {
                                EditorGUIUtility.PingObject(record.seqSO); // 프로젝트 창에서 반짝임 효과
                                Selection.activeObject = record.seqSO;     // 인스펙터에 즉시 선택
                            }
                        }

                        // 🚨 Stance SO 버튼 처리
                        string stanceName = record.stanceSO != null ? record.stanceSO.name : "-";
                        if (GUILayout.Button(stanceName, linkBtnStyle, GUILayout.Width(150)))
                        {
                            if (record.stanceSO != null)
                            {
                                EditorGUIUtility.PingObject(record.stanceSO); // 프로젝트 창에서 반짝임 효과
                                Selection.activeObject = record.stanceSO;     // 인스펙터에 즉시 선택
                            }
                        }

                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.EndScrollView();

                    EditorGUILayout.Space(5);

                    // 히스토리 초기화
                    if (GUILayout.Button("히스토리 지우기", GUILayout.Height(25)))
                    {
                        wcm.historyList.Clear();
                        EditorUtility.SetDirty(wcm);
                    }
                }
                EditorGUILayout.EndVertical();
            }

            Repaint();
        }
    }
#endif
}