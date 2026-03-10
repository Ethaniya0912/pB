using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TDA.World;

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

        [Header("Rotation Settings")]
        [SerializeField] private float mouseSensitivity = 1.0f;
        [SerializeField] private float leftAndRightRotationSpeed = 220f;
        [SerializeField] private float upAndDownRotationSpeed = 220f;
        [SerializeField] private float minimumPivot = -30f;
        [SerializeField] private float maximumPivot = 60f;

        // =========================================================================================
        // [신규 추가] 숄더뷰 제한 및 오토 센터링 (명세서 적용)
        // =========================================================================================
        [Header("Shoulder View Restrictions (P2)")]
        [Tooltip("카메라가 캐릭터 정면을 기준으로 좌우로 단독 회전할 수 있는 최대 한계각 (데드존). 사람의 자연스러운 시야각인 50~60도를 권장합니다.")]
        [SerializeField][Range(10f, 90f)] private float maxViewAngle = 60f;

        [Tooltip("캐릭터가 전진(이동)을 시작할 때, 카메라가 캐릭터의 등 뒤 정면으로 자동으로 부드럽게 돌아올지 여부입니다.")]
        [SerializeField] private bool enableAutoCenterOnMove = true;

        [Tooltip("자동 정렬 시 카메라가 등 뒤로 복귀하는 속도입니다. 값이 클수록 빠르게 정렬됩니다.")]
        [SerializeField] private float autoCenterSpeed = 2.0f;

        [Header("Debug & Gizmos")]
        [Tooltip("씬 뷰에서 카메라의 허용 시야각 부채꼴을 시각적으로 렌더링할지 여부입니다.")]
        [SerializeField] private bool showViewAngleGizmos = true;
        // =========================================================================================

        [Header("Soft Magnetic Lock (P2)")]
        [SerializeField] private Vector2 deadzoneRadius = new Vector2(0.25f, 0.25f);
        [SerializeField] private float magneticPullSpeed = 5.0f;

        [Header("Procedural Bodycam Noise (P2)")]
        [SerializeField] private float noiseFrequency = 1.5f;
        [SerializeField] private float noiseAmplitude = 0.3f;
        [SerializeField] private float staminaExhaustionMultiplier = 2.5f;

        [Header("Collision & Follow")]
        [SerializeField] private float cameraSmoothSpeed = 0.1f;
        [SerializeField] private float cameraCollisionRadius = 0.2f;
        [SerializeField] private LayerMask collideWithLayers;
        [SerializeField] private float unlockedCameraHeight = 1.65f;
        [SerializeField] private float lockedCameraHeight = 2.0f;

        private float cameraHorizontalInput;
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

        private void Awake()
        {
            if (cameraObject != null)
            {
                defaultFOV = cameraObject.fieldOfView;
                targetFOV = defaultFOV;
                defaultZPosition = cameraObject.transform.localPosition.z;
            }
        }

        private void Start()
        {
            DontDestroyOnLoad(gameObject);
            if (WorldCameraManager.Instance != null)
            {
                WorldCameraManager.Instance.SetLocalCamera(this);
            }
        }

        private void LateUpdate()
        {
            if (player == null) return;

            HandleFollowTarget();

            // 1. 순수 입력 기반 숄더뷰 회전 (클램핑 포함)
            Quaternion baseRotation = HandleRotation();

            // 2. 네트워크 락온 오버라이드 (자석 효과 우선)
            baseRotation = HandleMagneticSoftLock(baseRotation);

            HandleCollision();
            ApplyFinalTransform(baseRotation);
        }

        public void OnCameraInputReceived(float x, float y)
        {
            cameraHorizontalInput = x;
            cameraVerticalInput = y;
        }

        private void HandleFollowTarget()
        {
            if (isContextualMode && currentFocusTarget != null)
            {
                Vector3 desiredPos = currentFocusTarget.position + (currentFocusTarget.rotation * currentOffset);
                transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * currentLerpSpeed);
            }
            else
            {
                transform.position = Vector3.SmoothDamp(transform.position, player.transform.position, ref cameraVelocity, cameraSmoothSpeed);
            }
        }

        /// <summary>
        /// [핵심 수정: 숄더뷰 마스터 로직]
        /// 무한정 돌아가는 3인칭 자유 시점을 통제하고, 몸통 각도에 종속된 데드존 클램핑을 적용합니다.
        /// </summary>
        private Quaternion HandleRotation()
        {
            // 인벤토리 등 시스템 뷰일 때는 숄더뷰 락을 무시
            if (isContextualMode) return transform.rotation;

            // [로직 1] 마우스 입력을 임시 변수에 누적하여 가상의 타겟 각도(Yaw) 산출
            float targetYaw = leftAndRightLookAngle + (cameraHorizontalInput * leftAndRightRotationSpeed * mouseSensitivity) * Time.deltaTime;

            // [로직 2] 오일러 각도 정규화 및 차이 계산 (Mathf.DeltaAngle 필수)
            float playerBodyYaw = player.transform.eulerAngles.y;
            float angleDifference = Mathf.DeltaAngle(playerBodyYaw, targetYaw); // -180 ~ 180도 사이로 안전하게 정규화

            // [로직 3] 시야각 클램핑 (Deadzone 락) - 숄더뷰의 답답함과 묵직함 연출
            // 이로 인해 마우스를 60도 이상 돌려도 카메라는 멈추고, 캐릭터의 몸(Transform)이 같이 돌아야만 뒤를 볼 수 있습니다.
            angleDifference = Mathf.Clamp(angleDifference, -maxViewAngle, maxViewAngle);

            // [로직 4] 오토 센터링 (Auto-Centering) 개입
            // 캐릭터가 앞으로 걸어가는데 시야가 삐딱할 경우, 서서히 등 뒤 정면으로 앵글을 회복시킵니다.
            if (enableAutoCenterOnMove && player.playerNetworkManager != null && player.playerNetworkManager.animatorMoveAmountMovement.Value > 0.1f)
            {
                // 유저가 마우스 조작을 멈췄을 때만 개입하여 조작감을 방해하지 않음
                if (Mathf.Abs(cameraHorizontalInput) < 0.01f)
                {
                    angleDifference = Mathf.Lerp(angleDifference, 0f, autoCenterSpeed * Time.deltaTime);
                }
            }

            // [로직 5] 클램핑과 보간이 끝난 최종 각도를 캐릭터의 정면 각도에 결합하여 적용
            leftAndRightLookAngle = playerBodyYaw + angleDifference;

            // 상하(Pitch) 회전은 기존 로직 유지 (안전하게 Clamp 적용)
            upAndDownLookAngle -= (cameraVerticalInput * upAndDownRotationSpeed * mouseSensitivity) * Time.deltaTime;
            upAndDownLookAngle = Mathf.Clamp(upAndDownLookAngle, minimumPivot, maximumPivot);

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

            return result;
        }

        private void HandleCollision()
        {
            targetZPosition = defaultZPosition;
            RaycastHit hit;
            Vector3 dir = cameraObject.transform.position - cameraPivotTransform.position;
            dir.Normalize();

            if (Physics.SphereCast(cameraPivotTransform.position, cameraCollisionRadius, dir, out hit, Mathf.Abs(targetZPosition), collideWithLayers))
            {
                float dist = Vector3.Distance(cameraPivotTransform.position, hit.point);
                targetZPosition = -(dist - cameraCollisionRadius);
            }

            if (Mathf.Abs(targetZPosition) < cameraCollisionRadius)
            {
                targetZPosition = -cameraCollisionRadius;
            }

            float currentZ = Mathf.Lerp(cameraObject.transform.localPosition.z, targetZPosition, 0.2f);
            cameraObject.transform.localPosition = new Vector3(0, 0, currentZ);
        }

        private void ApplyFinalTransform(Quaternion baseRotation)
        {
            cameraObject.fieldOfView = Mathf.Lerp(cameraObject.fieldOfView, targetFOV, Time.deltaTime * currentLerpSpeed);

            float staminaPerc = 1f;
            if (player.playerNetworkManager != null && player.playerNetworkManager.maxStamina.Value > 0)
                staminaPerc = (float)player.playerNetworkManager.currentStamina.Value / player.playerNetworkManager.maxStamina.Value;

            float dynamicAmplitude = noiseAmplitude * bodycamWeight;
            if (staminaPerc < 0.3f)
                dynamicAmplitude *= (1.0f + (0.3f - staminaPerc) * staminaExhaustionMultiplier);

            float noiseX = (Mathf.PerlinNoise(Time.time * noiseFrequency, 0) - 0.5f) * dynamicAmplitude;
            float noiseY = (Mathf.PerlinNoise(0, Time.time * noiseFrequency) - 0.5f) * dynamicAmplitude;
            Quaternion noiseRot = Quaternion.Euler(noiseX, noiseY, 0);

            Vector3 shakeOffset = Vector3.zero;
            if (shakeDuration > 0)
            {
                shakeOffset = UnityEngine.Random.insideUnitSphere * shakeIntensity;
                shakeDuration -= Time.deltaTime;
            }

            transform.rotation = baseRotation * noiseRot;
            cameraPivotTransform.localPosition = new Vector3(0, cameraPivotTransform.localPosition.y, 0) + shakeOffset;
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
            float startH = cameraPivotTransform.localPosition.y;

            while (timer < duration)
            {
                timer += Time.deltaTime;
                float t = timer / duration;
                float targetH = (player.playerCombatManager.currentTarget != null) ? lockedCameraHeight : unlockedCameraHeight;
                float currentH = Mathf.Lerp(startH, targetH, t);
                cameraPivotTransform.localPosition = new Vector3(0, currentH, 0);
                yield return null;
            }
            cameraPivotTransform.localPosition = new Vector3(0, (player.playerCombatManager.currentTarget != null) ? lockedCameraHeight : unlockedCameraHeight, 0);
        }

        public void SetContextualFocus(Transform target, CameraStancePreset preset)
        {
            currentFocusTarget = target;
            targetFOV = preset.fov;
            currentLerpSpeed = preset.lerpSpeed;
            currentOffset = preset.customOffset;
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
}