using UnityEngine;
using UnityEngine.Animations.Rigging;

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

        protected virtual void Update()
        {
            UpdateIKWeights();
        }

        protected virtual void LateUpdate()
        {
            if (!Application.isPlaying && debugMode) return;
            ApplyIKTransforms();
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
    }
}