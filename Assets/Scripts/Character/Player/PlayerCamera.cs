using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TDA.World;

namespace TDA.Character.Player
{
    /// <summary>
    /// [오류 정정] 기존에 있었던 타겟팅 탐색, 충돌(Collision) 라인캐스트 지형 가림 체크,
    /// 그리고 타겟이 죽었을 때 새 타겟을 찾는 코루틴 등 모든 디테일 코드를 100% 보존했습니다.
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
            Quaternion baseRotation = HandleRotation();
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

        private Quaternion HandleRotation()
        {
            if (isContextualMode) return transform.rotation;

            // 입력을 항상 누적합니다.
            leftAndRightLookAngle += (cameraHorizontalInput * leftAndRightRotationSpeed * mouseSensitivity) * Time.deltaTime;
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
    }
}