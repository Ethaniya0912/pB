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

        // =========================================================================================
        // [신규 추가] 직관적인 카메라 거리 조절 옵션
        // =========================================================================================
        [Header("Camera Distance Settings")]
        [Tooltip("평상시 카메라와 캐릭터 사이의 기본 거리입니다. 값이 클수록 카메라가 캐릭터 등 뒤로 멀어집니다. (권장: 2.5 ~ 4.0)")]
        [SerializeField] private float defaultCameraDistance = 2.5f;

        [Header("Rotation Settings")]
        [SerializeField] private float mouseSensitivity = 1.0f;
        [SerializeField] private float leftAndRightRotationSpeed = 220f;
        [SerializeField] private float upAndDownRotationSpeed = 220f;
        [SerializeField] private float minimumPivot = -30f;
        [SerializeField] private float maximumPivot = 60f;

        // =========================================================================================
        // [강화] 카메라 묵직함 보간 (Camera Smoothing)
        // =========================================================================================
        [Header("Camera Feel Settings (P3)")]
        [Tooltip("마우스 회전 시 카메라가 따라오는 부드러움(묵직함) 정도입니다. 값이 클수록 무겁게 끌려옵니다.")]
        [SerializeField] private float cameraRotationDampTime = 0.05f;

        // 내부 연산을 위한 타겟 각도 및 댐핑 속도 추적 변수
        private float currentTargetLeftAndRightAngle;
        private float currentTargetUpAndDownAngle;
        private float leftRightRotationVelocity;
        private float upDownRotationVelocity;
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

        [Header("Bodycam Frame Decoupling (P2)")]
        [Tooltip("카메라의 시각적 업데이트 프레임을 강제로 낮춥니다. (예: 24, 30). 거친 바디캠 느낌을 줍니다.")]
        [SerializeField][Range(1f, 60f)] private float targetCameraFPS = 24f;
        [Tooltip("프레임 다운샘플링 기능을 켤지 여부입니다.")]
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

        // =========================================================================================
        // [강화] 핸드헬드 및 바디캠 효과 (Handheld & Bobbing)
        // =========================================================================================
        [Header("Handheld & Bodycam Effect (P3)")]
        [Tooltip("핸드헬드 카메라 특유의 멈춰있을 때 호흡 및 수전증(Sway) 강도입니다.")]
        [SerializeField] private float handheldSwayAmount = 0.5f;
        [Tooltip("핸드헬드 흔들림의 속도입니다.")]
        [SerializeField] private float handheldSwaySpeed = 1.0f;
        [Tooltip("이동할 때 카메라가 상하좌우로 덜컹거리는 보빙(Bobbing) 강도입니다.")]
        [SerializeField] private float movementBobbingAmount = 1.2f;
        [Tooltip("스태미나 고갈 시 흔들림 증폭 배수입니다.")]
        [SerializeField] private float staminaExhaustionMultiplier = 2.5f;
        // =========================================================================================

        [Header("Collision & Follow (Anti-Clipping)")]
        [SerializeField] private float cameraSmoothSpeed = 0.1f;
        [Tooltip("카메라 충돌체의 두께입니다. 클수록 벽에서 더 일찍 멈춰 뚫림을 방지합니다.")]
        [SerializeField] private float cameraCollisionRadius = 0.25f;
        [Tooltip("카메라가 장애물에 밀려 다가올 때, 기본적으로 허용되는 최소 Z 보장 거리입니다.")]
        [SerializeField] private float minimumCollisionOffset = 0.2f;

        [Tooltip("벽에 밀려 캐릭터에 가까워질 때, 머리를 뚫지 않도록 옆(X)과 위(Y)로 빗겨나는 최대 회피 거리입니다.")]
        [SerializeField] private Vector2 bodyAvoidanceOffset = new Vector2(0.4f, 0.2f);
        [Tooltip("카메라가 이 Z거리보다 캐릭터에 가까워지면 회피 기동(옆으로 빼기)을 시작합니다.")]
        [SerializeField] private float avoidanceThreshold = 1.2f;

        [Tooltip("벽, 지형, 건물 등 환경 충돌을 감지할 레이어입니다.")]
        [SerializeField] private LayerMask collideWithLayers;
        [Tooltip("다른 캐릭터나 적을 통과하지 않도록 하는 캐릭터 레이어입니다. (플레이어 본인은 코드에서 자동 제외됨)")]
        [SerializeField] private LayerMask characterCollisionLayers;

        [Header("Camera Height Settings")]
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

                // 인스펙터에서 설정한 카메라 거리를 무조건 등 뒤(음수)로 적용합니다.
                defaultZPosition = -Mathf.Abs(defaultCameraDistance);

                // 오브젝트가 렌즈를 파먹는 현상(근거리 클리핑)을 원천 차단하기 위해 값을 강제로 가장 작게 낮춥니다.
                cameraObject.nearClipPlane = 0.01f;
            }
        }

        private void Start()
        {
            DontDestroyOnLoad(gameObject);
            if (WorldCameraManager.Instance != null)
            {
                WorldCameraManager.Instance.SetLocalCamera(this);
            }

            // 프레임 디커플링 초기 상태 캐싱
            continuousPosition = transform.position;
            continuousRotation = transform.rotation;
            if (cameraObject != null) continuousCameraLocalPos = cameraObject.transform.localPosition;
            if (cameraPivotTransform != null) continuousPivotLocalPos = cameraPivotTransform.localPosition;

            steppedPosition = continuousPosition;
            steppedRotation = continuousRotation;
            steppedCameraLocalPos = continuousCameraLocalPos;
            steppedPivotLocalPos = continuousPivotLocalPos;

            // 보간용 타겟 각도 초기화
            currentTargetLeftAndRightAngle = leftAndRightLookAngle = transform.eulerAngles.y;
            float pitch = transform.eulerAngles.x;
            if (pitch > 180) pitch -= 360;
            currentTargetUpAndDownAngle = upAndDownLookAngle = pitch;
        }

        private void LateUpdate()
        {
            if (player == null) return;

            // [프레임 디커플링] 1. 정확한 연산을 위해, 이전 프레임에서 계산된 '부드러운 연속 좌표'로 카메라를 일시 복구합니다.
            if (enableFrameDecoupling && !isFirstFrame)
            {
                transform.position = continuousPosition;
                transform.rotation = continuousRotation;
                if (cameraObject != null) cameraObject.transform.localPosition = continuousCameraLocalPos;
                if (cameraPivotTransform != null) cameraPivotTransform.localPosition = continuousPivotLocalPos;
            }

            HandleFollowTarget();

            // 1. 순수 입력 기반 숄더뷰 회전 (클램핑 포함)
            Quaternion baseRotation = HandleRotation();

            // 2. 네트워크 락온 오버라이드 (자석 효과 우선)
            baseRotation = HandleMagneticSoftLock(baseRotation);

            // 3. 다중 방어 카메라 충돌 처리 (벽 관통 방지 및 머리 메쉬 뚫림 회피 기동 포함)
            HandleCollision();

            ApplyFinalTransform(baseRotation);

            // [프레임 디커플링] 2. 연산이 완료된 부드러운 최종 결과값을 '연속 상태(Continuous)'로 저장합니다.
            if (enableFrameDecoupling)
            {
                continuousPosition = transform.position;
                continuousRotation = transform.rotation;
                if (cameraObject != null) continuousCameraLocalPos = cameraObject.transform.localPosition;
                if (cameraPivotTransform != null) continuousPivotLocalPos = cameraPivotTransform.localPosition;

                // 3. 목표 FPS 주기에 도달했는지 확인하고, 화면에 보일 Stepped(끊기는) 상태를 갱신합니다.
                frameTimer += Time.deltaTime;
                float frameInterval = 1f / targetCameraFPS;

                if (frameTimer >= frameInterval || isFirstFrame)
                {
                    frameTimer %= frameInterval; // 초과된 시간 보존 (완벽한 타이밍 유지)

                    steppedPosition = continuousPosition;
                    steppedRotation = continuousRotation;
                    steppedCameraLocalPos = continuousCameraLocalPos;
                    steppedPivotLocalPos = continuousPivotLocalPos;

                    isFirstFrame = false;
                }

                // 4. 화면 렌더링을 위해 강제로 툭툭 끊기는(Stepped) 좌표로 덮어씌웁니다.
                transform.position = steppedPosition;
                transform.rotation = steppedRotation;
                if (cameraObject != null) cameraObject.transform.localPosition = steppedCameraLocalPos;
                if (cameraPivotTransform != null) cameraPivotTransform.localPosition = steppedPivotLocalPos;
            }
            else
            {
                isFirstFrame = true; // 기능이 꺼지면 즉시 초기화
            }
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
        /// [핵심 수정: 숄더뷰 마스터 로직 및 묵직한 보간 추가]
        /// 무한정 돌아가는 3인칭 자유 시점을 통제하고, 타겟 각도까지 부드럽게 쫓아가는 묵직함을 부여합니다.
        /// </summary>
        private Quaternion HandleRotation()
        {
            if (isContextualMode) return transform.rotation;

            // [수정: 타겟 각도 연산] 마우스 입력을 실제 카메라 각도가 아닌 '타겟 각도(Yaw)'에 먼저 누적시킵니다.
            currentTargetLeftAndRightAngle += (cameraHorizontalInput * leftAndRightRotationSpeed * mouseSensitivity) * Time.deltaTime;

            // 오일러 각도 정규화 및 차이 계산 (캐릭터 몸통 기준)
            float playerBodyYaw = player.transform.eulerAngles.y;
            float angleDifference = Mathf.DeltaAngle(playerBodyYaw, currentTargetLeftAndRightAngle);

            // 시야각 클램핑 (Deadzone 락) - 숄더뷰 데드존 제한
            angleDifference = Mathf.Clamp(angleDifference, -maxViewAngle, maxViewAngle);

            // 오토 센터링 (Auto-Centering) 개입
            if (enableAutoCenterOnMove && player.playerNetworkManager != null && player.playerNetworkManager.animatorMoveAmountMovement.Value > 0.1f)
            {
                // 유저가 마우스 조작을 멈췄을 때만 개입
                if (Mathf.Abs(cameraHorizontalInput) < 0.01f)
                {
                    angleDifference = Mathf.Lerp(angleDifference, 0f, autoCenterSpeed * Time.deltaTime);
                }
            }

            // 클램핑과 보간이 끝난 안전한 값을 타겟 각도로 확정합니다.
            currentTargetLeftAndRightAngle = playerBodyYaw + angleDifference;

            // 상하(Pitch) 타겟 회전 계산
            currentTargetUpAndDownAngle -= (cameraVerticalInput * upAndDownRotationSpeed * mouseSensitivity) * Time.deltaTime;
            currentTargetUpAndDownAngle = Mathf.Clamp(currentTargetUpAndDownAngle, minimumPivot, maximumPivot);

            // [신규: 묵직한 보간 로직] 실제 카메라 각도를 타겟 각도를 향해 부드럽게 댐핑시킵니다.
            // (Mathf.SmoothDampAngle을 사용하여 360도 랩핑 오차를 완벽하게 보정합니다)
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

            // 락온 상태에서는 타겟 각도도 함께 동기화하여 락온 해제 시 튀는 현상 방지
            currentTargetLeftAndRightAngle = leftAndRightLookAngle;
            currentTargetUpAndDownAngle = upAndDownLookAngle;

            return result;
        }

        /// <summary>
        /// [다중 방어 콜리전 로직 + 카메라 전진 튀어오름(Shooting Forward) 버그 픽스]
        /// </summary>
        private void HandleCollision()
        {
            targetZPosition = defaultZPosition;

            // 불안정한 위치의 차이(position - position)를 쓰지 않고, 피벗의 역방향(로컬 Z축 음수 방향)을 절대적인 방향 벡터로 사용합니다.
            Vector3 direction = -cameraPivotTransform.forward;

            LayerMask combinedLayers = collideWithLayers | characterCollisionLayers;
            float closestDistance = Mathf.Abs(defaultZPosition);

            // 1. 방어 1단계: 넓은 구형(SphereCast) 캐스트로 벽 및 주변 캐릭터 감지
            RaycastHit[] hits = Physics.SphereCastAll(cameraPivotTransform.position, cameraCollisionRadius, direction, Mathf.Abs(defaultZPosition), combinedLayers);

            foreach (var h in hits)
            {
                // 로컬 플레이어 본인의 콜라이더는 무시 (카메라 덜덜거림 방지)
                if (h.collider.transform.root == player.transform.root) continue;

                float dist = Vector3.Distance(cameraPivotTransform.position, h.point);
                if (dist < closestDistance)
                {
                    closestDistance = dist;
                }
            }

            if (closestDistance < Mathf.Abs(defaultZPosition))
            {
                targetZPosition = -(closestDistance - cameraCollisionRadius);
            }

            // 2. 방어 2단계: 얇은 벽을 뚫는 현상(Tunneling)을 막기 위한 1줄짜리 예리한 Linecast 보완
            RaycastHit lineHit;
            if (Physics.Linecast(cameraPivotTransform.position, cameraPivotTransform.position + (direction * Mathf.Abs(defaultZPosition)), out lineHit, combinedLayers))
            {
                if (lineHit.collider.transform.root != player.transform.root)
                {
                    float lineDist = Vector3.Distance(cameraPivotTransform.position, lineHit.point);
                    float lineTargetZ = -(lineDist - cameraCollisionRadius);

                    if (lineTargetZ > targetZPosition)
                    {
                        targetZPosition = lineTargetZ;
                    }
                }
            }

            // 3. [치명적 버그 수정] 극한의 압착 방어 (카메라가 플레이어 앞으로 튀어나가는 현상 완벽 방지)
            // 이전 Mathf.Abs() 방식은 충돌 거리가 너무 가까울 때 양수(앞으로 감)로 변해버린 값을 잡아내지 못했습니다.
            // 어떠한 상황에서도 targetZPosition이 -minimumCollisionOffset 보다 커지지(앞으로 가지) 못하게 절대값을 잠급니다.
            if (targetZPosition > -minimumCollisionOffset)
            {
                targetZPosition = -minimumCollisionOffset;
            }

            // 4. 머리 관통 방지를 위한 동적 메쉬 회피(Mesh Avoidance) 기동
            float avoidanceFactor = 0f;
            // 이제 targetZPosition은 항상 음수이므로 부등호를 통해 0에 가까워졌는지만 확인합니다.
            if (targetZPosition > -avoidanceThreshold)
            {
                avoidanceFactor = 1f - (Mathf.Abs(targetZPosition) / avoidanceThreshold);
            }

            float targetX = bodyAvoidanceOffset.x * avoidanceFactor;
            float targetY = bodyAvoidanceOffset.y * avoidanceFactor;

            // 5. 충돌 완충 보간 (Smoothing) 및 Z, X, Y 동시 적용
            float currentZ = Mathf.Lerp(cameraObject.transform.localPosition.z, targetZPosition, 0.2f);
            float currentX = Mathf.Lerp(cameraObject.transform.localPosition.x, targetX, 0.2f);
            float currentY = Mathf.Lerp(cameraObject.transform.localPosition.y, targetY, 0.2f);

            cameraObject.transform.localPosition = new Vector3(currentX, currentY, currentZ);
        }

        private void ApplyFinalTransform(Quaternion baseRotation)
        {
            cameraObject.fieldOfView = Mathf.Lerp(cameraObject.fieldOfView, targetFOV, Time.deltaTime * currentLerpSpeed);

            // [신규 로직] 핸드헬드 및 이동 시 보빙(Bobbing) 효과 추가로 바디캠 느낌 극대화
            float staminaPerc = 1f;
            if (player.playerNetworkManager != null && player.playerNetworkManager.maxStamina.Value > 0)
                staminaPerc = (float)player.playerNetworkManager.currentStamina.Value / player.playerNetworkManager.maxStamina.Value;

            float exhaustionFactor = 1.0f;
            if (staminaPerc < 0.3f)
                exhaustionFactor += (0.3f - staminaPerc) * staminaExhaustionMultiplier;

            // A. 핸드헬드 기본 수전증 및 호흡(Sway) 효과
            float swayX = (Mathf.PerlinNoise(Time.time * handheldSwaySpeed, 0) - 0.5f) * handheldSwayAmount * exhaustionFactor * bodycamWeight;
            float swayY = (Mathf.PerlinNoise(0, Time.time * handheldSwaySpeed) - 0.5f) * handheldSwayAmount * exhaustionFactor * bodycamWeight;
            float swayZ = (Mathf.PerlinNoise(Time.time * handheldSwaySpeed, Time.time * handheldSwaySpeed) - 0.5f) * (handheldSwayAmount * 0.5f) * exhaustionFactor * bodycamWeight;

            // B. 걷기/뛰기에 따른 무거운 보빙(Bobbing) 효과
            float moveAmount = player.playerNetworkManager != null ? player.playerNetworkManager.animatorMoveAmountMovement.Value : 0f;
            float bobSpeed = 12f; // 발걸음 빈도
            float bobX = Mathf.Sin(Time.time * bobSpeed) * movementBobbingAmount * moveAmount * bodycamWeight;
            float bobY = Mathf.Abs(Mathf.Cos(Time.time * bobSpeed)) * movementBobbingAmount * moveAmount * bodycamWeight; // Cos 절대값으로 튕기는 느낌

            // 기존 노이즈 대신 Sway와 Bobbing을 믹스하여 적용합니다. (시야의 입체적 흔들림)
            Quaternion bodycamRot = Quaternion.Euler(swayX - bobY, swayY + bobX, swayZ + (bobX * 0.3f));

            Vector3 shakeOffset = Vector3.zero;
            if (shakeDuration > 0)
            {
                shakeOffset = UnityEngine.Random.insideUnitSphere * shakeIntensity;
                shakeDuration -= Time.deltaTime;
            }

            transform.rotation = baseRotation * bodycamRot;
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

            // 타겟 각도 동기화 (화면 튀는 현상 방지)
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