using UnityEngine;
using UnityEngine.Animations.Rigging;
using TDA.Core.Events; // AnimationEventType(Enum) 라우팅을 위해 추가

namespace TDA.Character
{
    /// <summary>
    /// [L3 Domain] 캐릭터(Player, AI 공통)의 Animation Rigging 뼈대 제어를 담당하는 베이스 클래스입니다.
    /// 목표 타겟(Transform)을 할당받으면 부드럽게 IK 가중치를 조절하여 관절을 꺾어줍니다.
    /// [방어 로직 포함] 2D 평면 각도 추출을 통해 등 뒤를 볼 때 목이 꺾이는 엑소시스트 버그를 방지합니다.
    /// </summary>
    [ExecuteAlways]
    public class CharacterIKController : MonoBehaviour
    {
        protected CharacterManager character;

        [Header("Rigging Components")]
        [SerializeField] protected RigBuilder rigBuilder;
        [SerializeField] protected TwoBoneIKConstraint rightHandIK;
        [SerializeField] protected MultiAimConstraint headLookIK;

        [Header("IK Targets")]
        [SerializeField] protected Transform rightHandTarget;
        [SerializeField] protected Transform headTarget;

        [Header("Settings & Damping (Task 5, 8)")]
        [Tooltip("도달할 때까지 걸리는 댐핑 시간 (값이 클수록 부드럽고 묵직함)")]
        [SerializeField] protected float handIKDampTime = 0.1f;
        [SerializeField] protected float lookIKDampTime = 0.15f;
        [SerializeField] protected float handRotationSmoothSpeed = 20f;

        [Header("IK Constraints (Task 4 방어)")]
        [Tooltip("이 각도를 넘어가면 시선 IK를 포기합니다 (엑소시스트 버그 방지)")]
        [SerializeField] protected float maxLookAngle = 80f;
        [Tooltip("IK 사용 시 손목이 비정상적으로 꺾인다면 이 값을 조절해 보정하세요.")]
        [SerializeField] protected Vector3 handRotationOffset;

        [Header("Debug")]
        [SerializeField] protected bool debugMode = false;
        [Range(0, 1)][SerializeField] protected float debugHandWeight;
        [Range(0, 1)][SerializeField] protected float debugLookWeight;

        // =========================================================================================
        // [신규 추가] Kinematic Footstep Tracking (P4)
        // =========================================================================================
        [Header("Kinematic Footstep Tracking (P4)")]
        [Tooltip("캐릭터의 왼쪽 발 뼈대(Transform)를 연결하세요. (빈칸이면 발소리 추적 기능이 꺼집니다)")]
        [SerializeField] protected Transform leftFootBone;
        [SerializeField] protected Transform rightFootBone;

        [Tooltip("발이 Root보다 이 높이(m) 이하로 내려와야 땅에 닿은 것으로 간주합니다. (자동 보정 시 베이스라인으로 쓰임)")]
        [SerializeField] protected float groundHeightThreshold = 0.15f;

        [Tooltip("발이 땅에서 떨어졌다고 판단하여 다음 발소리를 준비할 최소 높이입니다.")]
        [SerializeField] protected float liftHeightThreshold = 0.25f;

        [Tooltip("발의 수직/수평 이동 속도가 이 값 이하로 떨어져야 완전히 '디딘 것'으로 판정합니다.")]
        [SerializeField] protected float plantVelocityThreshold = 0.5f;

        [Tooltip("동일한 발에서 연달아 소리가 나는 것을 막기 위한 쿨타임(초)입니다.")]
        [SerializeField] protected float stepCooldown = 0.2f;

        // =========================================================================================
        // [스마트 자동 보정] Dynamic Footstep Auto-Calibration & Fail-Safe
        // =========================================================================================
        [Header("Dynamic Auto-Calibration")]
        [Tooltip("걷기, 뛰기, 게걸음 등 상황에 맞춰 발의 높이 진폭(Amplitude)을 자동 분석하고 임계값을 실시간으로 보정합니다.")]
        [SerializeField] protected bool enableAutoCalibration = true;
        [Tooltip("자동 보정된 임계값과 발자국이 트리거된 정확한 원인을 콘솔에 출력합니다.")]
        [SerializeField] protected bool debugCalibrationLogs = true;

        // 양발의 동적 주기(Cycle) 트래킹 변수들
        protected float leftLocalMinY = 0f, leftLocalMaxY = 0.5f;
        protected float rightLocalMinY = 0f, rightLocalMaxY = 0.5f;
        protected float leftLastYVel = 0f, rightLastYVel = 0f;
        protected float leftPhaseTimer = 0f, rightPhaseTimer = 0f; // 최대 보폭 시간(Fail-safe) 추적용

        // --- 내부 상태 추적 변수 ---
        protected Vector3 lastLeftFootPos;
        protected Vector3 lastRightFootPos;
        protected bool isLeftFootPlanted = false;
        protected bool isRightFootPlanted = false;
        protected float lastLeftStepTime = 0f;
        protected float lastRightStepTime = 0f;
        // =========================================================================================

        // 내부 제어 변수 (자식 클래스인 Player/AIIKController에서 제어)
        public float targetHandWeight = 0f;
        public float targetLookWeight = 0f;

        protected Transform currentHandTargetTransform;
        protected Transform currentLookTargetTransform;

        // [SmoothDamp] 내부 속도 참조 변수 (블렌드 트리 댐핑과 동일한 원리)
        protected float handWeightVelocity;
        protected float lookWeightVelocity;

        protected virtual void Awake()
        {
            if (Application.isPlaying)
            {
                character = GetComponent<CharacterManager>();
            }

            if (rigBuilder == null) rigBuilder = GetComponent<RigBuilder>();

            if (headLookIK != null && headTarget != null)
            {
                var data = headLookIK.data.sourceObjects;
                if (data.Count == 0)
                {
                    data.Add(new WeightedTransform(headTarget, 1f));
                    headLookIK.data.sourceObjects = data;
                    if (rigBuilder != null) rigBuilder.Build();
                }
            }
        }

        protected virtual void Start()
        {
            // 게임 시작 첫 프레임에 양발의 초기 위치를 캐싱하여, 시작하자마자 거대한 속도값이 튀는 현상 방지
            if (leftFootBone != null) lastLeftFootPos = leftFootBone.position;
            if (rightFootBone != null) lastRightFootPos = rightFootBone.position;
        }

        protected virtual void Update()
        {
            UpdateIKWeights();
        }

        protected virtual void LateUpdate()
        {
            if (!Application.isPlaying && debugMode) return;

            // 1. 기존 머리/손 IK 적용
            ApplyIKTransforms();

            // 2. [신규 추가] 키네마틱 발소리 추적 실행 (뼈대 이동이 모두 끝난 LateUpdate가 가장 이상적)
            TrackKinematicFootsteps();
        }

        protected virtual void ApplyIKTransforms()
        {
            if (currentHandTargetTransform != null && rightHandIK.weight > 0.01f)
            {
                rightHandTarget.position = currentHandTargetTransform.position;

                Quaternion targetRot = currentHandTargetTransform.rotation * Quaternion.Euler(handRotationOffset);
                rightHandTarget.rotation = Quaternion.Slerp(
                    rightHandTarget.rotation,
                    targetRot,
                    Time.deltaTime * handRotationSmoothSpeed
                );
            }

            if (currentLookTargetTransform != null && headLookIK.weight > 0.01f)
            {
                headTarget.position = currentLookTargetTransform.position;
            }
        }

        /// <summary>
        /// [핵심] 현재 IK 가중치를 목표 가중치로 SmoothDamp 보간합니다.
        /// 각도 계산 로직을 통해 타겟이 등 뒤로 가면 가중치를 강제로 빼서 목이 꺾이는 현상을 막습니다.
        /// </summary>
        protected virtual void UpdateIKWeights()
        {
            if (debugMode)
            {
                if (rightHandIK != null) rightHandIK.weight = debugHandWeight;
                if (headLookIK != null) headLookIK.weight = debugLookWeight;
                return;
            }

            if (!Application.isPlaying) return;

            // 1. 시선 IK 각도 추출 및 방어 로직 (Task 4 & 엑소시스트 버그 방지)
            float actualLookWeightTarget = targetLookWeight;

            if (currentLookTargetTransform != null && targetLookWeight > 0f)
            {
                // XZ 2D 평면 투영 (Y축 무시)
                Vector3 dirToTarget = currentLookTargetTransform.position - transform.position;
                dirToTarget.y = 0f;

                Vector3 forward = transform.forward;
                forward.y = 0f;

                // 정규화된 각도 추출
                float angle = Vector3.Angle(forward, dirToTarget.normalized);

                // 설정된 한계 각도(maxLookAngle)를 벗어나면 시선 IK 포기
                if (angle > maxLookAngle)
                {
                    actualLookWeightTarget = 0f;
                }
            }

            // 2. 가중치 댐핑 적용 (Task 5 & 8)
            // 기존 Lerp 대신 SmoothDamp를 사용하여 애니메이터 블렌드트리와 동일한 관성/댐핑 효과 부여
            if (rightHandIK != null)
            {
                rightHandIK.weight = Mathf.SmoothDamp(
                    rightHandIK.weight,
                    targetHandWeight,
                    ref handWeightVelocity,
                    handIKDampTime
                );
            }

            if (headLookIK != null)
            {
                headLookIK.weight = Mathf.SmoothDamp(
                    headLookIK.weight,
                    actualLookWeightTarget,
                    ref lookWeightVelocity,
                    lookIKDampTime
                );
            }
        }

        public virtual void SetHandIKTarget(Transform targetTransform)
        {
            if (targetTransform != null)
            {
                currentHandTargetTransform = targetTransform;
                targetHandWeight = 1f;
            }
            else
            {
                currentHandTargetTransform = null;
                targetHandWeight = 0f;
            }
        }

        public virtual void SetLookTarget(Transform target)
        {
            if (target != null)
            {
                currentLookTargetTransform = target;
                targetLookWeight = 1f;
            }
            else
            {
                currentLookTargetTransform = null;
                targetLookWeight = 0f;
            }
        }

        // =========================================================================================
        // [신규] 모션 워핑 및 블렌드 트리의 영향을 받지 않는, 순수 3D 좌표 기반의 키네마틱 발소리 추적 코어
        // =========================================================================================
        protected virtual void TrackKinematicFootsteps()
        {
            // 발뼈가 할당되지 않았거나, 캐릭터/이벤트 매니저가 없다면 기능 비활성화
            if (leftFootBone == null || rightFootBone == null || character == null || character.characterEventManager == null) return;

            // 공중에 떠있거나(Jump) 구르기(Roll) 중일 때는 발소리 추적 무시
            if (character.characterLocomotionManager != null &&
                (!character.characterController.isGrounded || character.characterLocomotionManager.isRolling))
            {
                isLeftFootPlanted = false;
                isRightFootPlanted = false;
                lastLeftFootPos = leftFootBone.position;
                lastRightFootPos = rightFootBone.position;

                // 공중에서는 주기 타이머 리셋
                leftPhaseTimer = 0f;
                rightPhaseTimer = 0f;
                return;
            }

            // 가만히 서있을 때(Idle)는 불필요한 발소리 추적 연산을 중지합니다.
            if (character.characterNetworkManager != null && character.characterNetworkManager.animatorMoveAmountMovement.Value <= 0.05f)
            {
                lastLeftFootPos = leftFootBone.position;
                lastRightFootPos = rightFootBone.position;
                leftPhaseTimer = 0f;
                rightPhaseTimer = 0f;
                return;
            }

            // =========================================================================================
            // 🚨 [추가된 로직] 전투(락온) 중 게걸음(Strafe)을 칠 때는 질질 끄는 특수 사운드 이벤트로 교체합니다.
            // =========================================================================================
            bool isStrafing = false;
            if (character.characterNetworkManager != null)
            {
                isStrafing = character.characterNetworkManager.isLockedOn.Value;
            }

            global::AnimationEventType leftFootEvent = isStrafing ? global::AnimationEventType.PlayFootstep_Drag_L : global::AnimationEventType.PlayFootstep_L;
            global::AnimationEventType rightFootEvent = isStrafing ? global::AnimationEventType.PlayFootstep_Drag_R : global::AnimationEventType.PlayFootstep_R;

            // 양발 개별 스마트 추적 실행 (Auto-Calibration 변수들을 ref로 전달하여 상태를 유지합니다)
            ProcessFootstep(leftFootBone, ref lastLeftFootPos, ref isLeftFootPlanted, ref lastLeftStepTime, leftFootEvent, isStrafing,
                            ref leftLocalMinY, ref leftLocalMaxY, ref leftLastYVel, ref leftPhaseTimer);

            ProcessFootstep(rightFootBone, ref lastRightFootPos, ref isRightFootPlanted, ref lastRightStepTime, rightFootEvent, isStrafing,
                            ref rightLocalMinY, ref rightLocalMaxY, ref rightLastYVel, ref rightPhaseTimer);
        }

        private void ProcessFootstep(Transform foot, ref Vector3 lastPos, ref bool isPlanted, ref float lastStepTime, global::AnimationEventType eventType, bool isStrafing,
                                     ref float localMinY, ref float localMaxY, ref float lastYVel, ref float phaseTimer)
        {
            Vector3 currentPos = foot.position;

            // 1. 속도 계산 (이동량을 델타타임으로 나눔)
            Vector3 velocity = (currentPos - lastPos) / Time.deltaTime;
            float yVelocity = velocity.y;

            // 2. Root 기준 상대적 높이 계산 (Y축)
            float relativeHeight = currentPos.y - transform.position.y;

            // =========================================================================================
            // [스마트 자동 보정] 진폭(Amplitude) 실시간 분석
            // 애니메이션(걷기, 뛰기, 게걸음)에 따라 발의 최고점/최저점이 계속 달라지므로 이를 추적합니다.
            // =========================================================================================
            if (enableAutoCalibration)
            {
                if (relativeHeight < localMinY) localMinY = relativeHeight;
                if (relativeHeight > localMaxY) localMaxY = relativeHeight;

                // 새로운 애니메이션 궤적에 적응하기 위해 min/max 값을 시간이 지남에 따라 서서히 중앙(현재값)으로 감쇠시킵니다.
                localMinY = Mathf.Lerp(localMinY, relativeHeight, Time.deltaTime * 0.5f);
                localMaxY = Mathf.Lerp(localMaxY, relativeHeight, Time.deltaTime * 0.5f);
            }

            float amplitude = localMaxY - localMinY;

            // 보정된 동적 임계값 도출 (자동 보정이 꺼져있으면 인스펙터의 수동 고정값을 사용)
            float currentGroundThresh = enableAutoCalibration ? localMinY + (amplitude * 0.25f) : groundHeightThreshold;
            float currentLiftThresh = enableAutoCalibration ? localMinY + (amplitude * 0.5f) : liftHeightThreshold;

            // 뛰고 있거나(moveAmount > 0.5) 게걸음(isStrafing) 중일 때는 발이 바닥을 스치듯 빠르게 지나가므로 속도 허용치를 넓혀줍니다.
            float moveAmount = character.characterNetworkManager != null ? character.characterNetworkManager.animatorMoveAmountMovement.Value : 0f;
            float speedMod = (moveAmount > 0.5f || isStrafing) ? 2.5f : 1.0f;
            float currentVelThresh = plantVelocityThreshold * speedMod;

            // [방어 로직] 최대 보폭 시간(Fail-safe). 이동 속도에 따라 한 걸음에 걸리는 최대 시간을 계산합니다.
            float maxStrideTime = Mathf.Clamp(0.9f / speedMod, 0.35f, 1.2f);
            phaseTimer += Time.deltaTime;

            bool isPlantedThisFrame = false;
            string triggerReason = "";

            // 3. 발을 들어 올림 (Lift) 감지 -> 다음 발소리를 내기 위해 장전(Reset)
            if (isPlanted && relativeHeight > currentLiftThresh)
            {
                isPlanted = false;
            }

            // 4. 땅에 디딤 (Plant) 복합 감지 로직
            if (!isPlanted)
            {
                // [조건 A] 표준 키네마틱: 보정된 최저점 근처에 도달했고, 속도가 현저히 줄어들었을 때
                if (relativeHeight <= currentGroundThresh && velocity.magnitude <= currentVelThresh)
                {
                    isPlantedThisFrame = true;
                    triggerReason = "Standard Kinematic";
                }
                // [조건 B] 위상 역전(Inflection Point): 발이 아래로 향하다가(음수) 다시 위로 튀어오르는(양수) 최하단 '변곡점'을 통과했을 때
                else if (lastYVel < -0.05f && yVelocity >= -0.01f && relativeHeight < currentGroundThresh + 0.1f)
                {
                    isPlantedThisFrame = true;
                    triggerReason = "Inflection (Phase Reversal)";
                }
                // [조건 C] 절대 방어망(Fail-safe): 조건 A, B를 아슬아슬하게 비껴가서 발소리가 안 나는 '씹힘 버그'를 막습니다.
                // 발이 충분히 아래쪽에 있는데, 한 걸음을 내디뎌야 할 최대 한계 시간(maxStrideTime)을 초과했다면 강제로 밟은 것으로 처리합니다.
                else if (phaseTimer > maxStrideTime && relativeHeight <= currentGroundThresh + (amplitude * 0.4f))
                {
                    isPlantedThisFrame = true;
                    triggerReason = "Fail-safe (Max Stride Time Hit)";
                }
            }

            // 5. 조건이 충족되었다면 쿨타임 검사 후 브로드캐스트
            if (isPlantedThisFrame && Time.time >= lastStepTime + stepCooldown)
            {
                // [안전한 Enum 라우팅] 이벤트 매니저에게 발소리 이벤트를 던져주면, SoundManager가 받아서 소리를 냅니다.
                character.characterEventManager.NotifyAnimationEvent(eventType, $"Kinematic ({triggerReason})");

                isPlanted = true;
                lastStepTime = Time.time;
                phaseTimer = 0f; // 다음 걸음을 위해 주기 타이머 리셋

                // 자동 보정 상태 출력
                if (enableAutoCalibration && debugCalibrationLogs)
                {
                    Debug.Log($"<color=lime>[Auto-Calib]</color> {foot.name} 밟음! | 사유: <color=white>{triggerReason}</color>\n" +
                              $"진폭(Amp): {amplitude:F3} | 보정 임계값: {currentGroundThresh:F3} | 현재높이: {relativeHeight:F3} | 속도: {velocity.magnitude:F3}");
                }
            }

            // 6. 다음 프레임 연산을 위해 좌표 및 속도 캐싱
            lastPos = currentPos;
            lastYVel = yVelocity;
        }
    }
}