using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace SG
{
    // [ExecuteAlways]를 통해 에디터에서도 테스트 가능
    [ExecuteAlways]
    public class CharacterIKController : MonoBehaviour
    {
        CharacterManager character;

        [Header("Rigging Components")]
        [SerializeField] protected RigBuilder rigBuilder;
        [SerializeField] protected TwoBoneIKConstraint rightHandIK;
        [SerializeField] protected MultiAimConstraint headLookIK;

        [Header("IK Targets")]
        [SerializeField] protected Transform rightHandTarget;
        [SerializeField] protected Transform headTarget;

        [Header("Settings")]
        [SerializeField] float handIKSmoothSpeed = 10f;
        [SerializeField] float handRotationSmoothSpeed = 20f;
        [SerializeField] float lookIKSmoothSpeed = 5f;

        [Header("IK Corrections")]
        [Tooltip("IK 사용 시 손목이 비정상적으로 꺾인다면 이 값을 조절해 보정하세요. (예: 90, 0, 0 등)")]
        [SerializeField] Vector3 handRotationOffset;

        [Header("Debug")]
        [SerializeField] bool debugMode = false;
        [Range(0, 1)][SerializeField] float debugHandWeight;
        [Range(0, 1)][SerializeField] float debugLookWeight;

        // 내부 제어 변수
        protected float targetHandWeight = 0f;
        protected float targetLookWeight = 0f;
        protected Transform currentHandTargetTransform;
        protected Transform currentLookTargetTransform;

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

            // 실제 IK 타겟 위치/회전 갱신
            if (currentHandTargetTransform != null && targetHandWeight > 0.01f)
            {
                rightHandTarget.position = currentHandTargetTransform.position;

                // [수정] 오프셋을 적용한 최종 회전값 계산
                // GripPoint의 회전에 오프셋을 더해 손목이 올바른 방향을 보게 함
                Quaternion targetRot = currentHandTargetTransform.rotation * Quaternion.Euler(handRotationOffset);

                rightHandTarget.rotation = Quaternion.Slerp(
                    rightHandTarget.rotation,
                    targetRot,
                    Time.deltaTime * handRotationSmoothSpeed
                );
            }

            // 시선 타겟 위치 갱신
            if (currentLookTargetTransform != null && targetLookWeight > 0.01f)
            {
                headTarget.position = currentLookTargetTransform.position;
            }
        }

        private void UpdateIKWeights()
        {
            if (debugMode)
            {
                if (rightHandIK != null) rightHandIK.weight = debugHandWeight;
                if (headLookIK != null) headLookIK.weight = debugLookWeight;
                return;
            }

            if (!Application.isPlaying) return;

            if (rightHandIK != null)
            {
                rightHandIK.weight = Mathf.Lerp(rightHandIK.weight, targetHandWeight, Time.deltaTime * handIKSmoothSpeed);
            }

            if (headLookIK != null)
            {
                headLookIK.weight = Mathf.Lerp(headLookIK.weight, targetLookWeight, Time.deltaTime * lookIKSmoothSpeed);
            }
        }

        public void SetHandIKTarget(Transform targetTransform)
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

        public void SetLookTarget(Transform target)
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