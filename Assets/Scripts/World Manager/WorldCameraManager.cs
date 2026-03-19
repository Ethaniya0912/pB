using System.Collections;
using UnityEngine;
using TDA.Cameras;
using TDA.Character.Player;

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
                activeSequenceCoroutine = StartCoroutine(BlendToStanceRoutine(defaultRestStance, overrideBlendTime, AnimationCurve.EaseInOut(0, 0, 1, 1)));
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
                        yield return StartCoroutine(BlendToStanceRoutine(step.targetStance, step.blendDuration, step.blendCurve));
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
                    yield return StartCoroutine(BlendToStanceRoutine(defaultRestStance, sequence.restoreBlendDuration, AnimationCurve.EaseInOut(0, 0, 1, 1)));
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

        private IEnumerator BlendToStanceRoutine(CameraStancePresetSO targetStance, float duration, AnimationCurve curve)
        {
            float timer = 0f;

            // 보간 시작점 스냅샷 캡처
            float startFOV = currentFOV;
            float startZTilt = currentZTilt;
            Vector3 startBaseOffset = currentBaseOffset;

            // 보간 목표점 설정
            float targetFOV = targetStance.fov;
            float targetZTilt = targetStance.zTilt;
            Vector3 targetBaseOffset = targetStance.baseOffset;

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
                float t = Mathf.Clamp01(timer / duration);

                // 디자이너가 설정한 커브(Ease-In, Ease-Out 등)를 적용하여 유기적인 가속도를 만듭니다.
                float curveT = (curve != null && curve.length > 0) ? curve.Evaluate(t) : t;

                currentFOV = Mathf.Lerp(startFOV, targetFOV, curveT);
                currentZTilt = Mathf.Lerp(startZTilt, targetZTilt, curveT);
                currentBaseOffset = Vector3.Lerp(startBaseOffset, targetBaseOffset, curveT);

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

            currentDynamicFraming = stance.dynamicFraming;
            currentHandheldEffect = stance.handheldEffect;

            if (stance.focusTargets != null && stance.focusTargets.Count > 0)
            {
                currentTargetBiasWeight = stance.focusTargets[stance.focusTargets.Count - 1].weight;
            }
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
}