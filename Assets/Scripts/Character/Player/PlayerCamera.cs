using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class PlayerCamera : MonoBehaviour
{
    // [수정사항] 멀티플레이어 환경(NGO 2.0) 충돌 방지를 위해 PlayerCamera의 싱글턴(Instance) 속성을 삭제 및 주석 처리합니다.
    // 로컬 카메라의 접근은 WorldCameraManager.Instance를 통해 안전하게 참조가 관리됩니다.
    // public static PlayerCamera Instance { get; private set; }

    [Header("References")]
    public Camera cameraObject;
    public PlayerManager player;
    [SerializeField] Transform cameraPivotTransform;

    // 조작 데이터 (라우터로부터 수신)
    private float cameraHorizontalInput;
    private float cameraVerticalInput;

    // 카메라 퍼포먼스 수정용
    [Header("Camera Setting")]
    private float cameraSmoothSpeed = 5.0f; // 숫자가 클수록 카메라가 포지션에 도달하는 시간증가
    [SerializeField] float leftAndRightRotationSpeed = 22;
    [SerializeField] float upAndDownRotationSpeed = 44;
    [SerializeField] float minimumPivot = -30; // 아래로 볼 수있는 최저값
    [SerializeField] float maximumPivot = 60; // 위로 볼 수 있는 최고값
    [SerializeField] float cameraCollisionRadius = 0.2f;
    [SerializeField] LayerMask collideWithLayers;

    // 카메라 값 용
    [Header("Camera Values")]
    private Vector3 cameraVelocity;
    private Vector3 cameraObjectPosition; // 카메라 콜리션을 위한 밸류(콜리션시 카메라를 해당 포지션으로 이동)
    [SerializeField] float leftAndRightLookAngle;
    [SerializeField] float upAndDownLookAngle;
    private float cameraZPosition; // 카메라 콜리션을 위한 밸류
    private float targetCameraZPosition;  // 카메라 콜리션을 위한 밸류

    [Header("Lock On")]
    [SerializeField] private float lockOnRadius = 20;
    [SerializeField] float minimumViewableAngle = -50;
    [SerializeField] float maximumViewableAngle = 50;
    //[SerializeField] float maximumLockOnDistance = 20;
    [SerializeField] float lockOnTargetFollowSpeed = 0.2f;
    [SerializeField] float setCameraHeightSpeed = 1;
    [SerializeField] float unlockedCameraHeight = 1.65f;
    [SerializeField] float lockedCameraHeight = 2.0f;
    private Coroutine cameraLockOnHeightCoroutine;
    private List<CharacterManager> availableTarget = new List<CharacterManager>();
    public CharacterManager nearestLockOnTarget;
    public CharacterManager leftLockOnTarget;
    public CharacterManager rightLockOnTarget;

    [Header("Dynamic Contextual Settings")]
    private Transform currentFocusTarget;
    private Vector3 currentOffset;
    private float targetFOV;
    private float defaultFOV = 60;
    private float currentLerpSpeed = 5f;
    private bool isContexualMode = false;

    [Header("Effects")]
    private float shakeIntensity = 0f;
    private float shakeDuration = 0f;
    private float bodycamWeight = 0f; // 바디캠 스타일 노이즈 가중치.

    private void Awake()
    {
        // [수정사항] 싱글턴 오용으로 인한 멀티플레이어 환경 버그를 차단하기 위해 Awake 내 할당 로직을 제거(주석 처리)합니다.
        /*
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
        */

        if (cameraObject != null)
        {
            defaultFOV = cameraObject.fieldOfView;

            targetFOV = defaultFOV;
        }
    }

    private void Start()
    {
        DontDestroyOnLoad(gameObject);

        if (cameraObject != null)
        {
            cameraZPosition = cameraObject.transform.localPosition.z;
        }

        // 월드 매니저가 존재한다면 자신을 등록
        if (WorldCameraManager.Instance != null)
        {
            WorldCameraManager.Instance.RegisterLocalCamera(this);
        }
    }
    internal void OnCameraInputReceived(float x, float y)
    {
        cameraHorizontalInput = x;
        cameraVerticalInput = y;
    }

    public void HandleAllCameraActions()
    {
        if (player != null)
        {
            HandleFollowTarget();
            HandleRotation();
            HandleCollision();
            HandleEffects();
        }
        // 유저 따라오기
        // 플레이어 주변 로테이션
        // 오브젝트와 충돌(통과x)
    }

    private void HandleEffects()
    {
        // FOV 업데이트
        if (cameraObject != null)
        {
            cameraObject.fieldOfView = Mathf.Lerp(cameraObject.fieldOfView, targetFOV, Time.deltaTime * currentLerpSpeed);
        }

        // 셰이크 처리
        // [수정사항 - 코멘트 추가] 향후 셰이크와 콜리전 로직이 프레임 내에서 덮어쓰기 경쟁을 하는 것을 완전히 방지하려면,
        // cameraObjectPosition (콜리전 위치)를 다루는 Transform 뎁스와, Shake 전용 Transform 뎁스를 하이어라키에서 물리적으로 분리하는 것이 좋습니다.
        if (shakeDuration > 0)
        {
            cameraObject.transform.localPosition = cameraObjectPosition + (UnityEngine.Random.insideUnitSphere * shakeIntensity);
            shakeDuration -= Time.deltaTime;
        }

        else
        {
            cameraObject.transform.localPosition = cameraObjectPosition;
        }

        // 바디캠 스타일 노이즈 (추후 펄린 노이즈나 바이캠웨이트 이용해서 구현 가능)
    }

    private void HandleFollowTarget()
    {
        if (isContexualMode && currentFocusTarget != null)
        {
            // [상황별 포커싱] 타겟의 회전값을 반영한 오프셋 좌표로 이동
            Vector3 desiredPosition = currentFocusTarget.position + (currentFocusTarget.rotation * currentOffset);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * currentLerpSpeed);
        }

        else
        {
            Vector3 targetCameraPosition = Vector3.SmoothDamp(
                transform.position,
                player.transform.position,
                ref cameraVelocity,
                cameraSmoothSpeed * Time.deltaTime
                );
            transform.position = targetCameraPosition;
        }
    }

    private void HandleRotation()
    {
        if (isContexualMode && currentFocusTarget != null)
        {
            // [상황별 포커싱] 타겟 오브젝트를 위에서 아래로 바라보도록
            // 현재 카메라 위치에서 타겟오브젝트를 향하는 벡터
            Vector3 direction = (currentFocusTarget.position - transform.position).normalized;

            if (direction != Vector3.zero)
            {
                // 타겟을 향해 비스듬히 아래를 보는 회전값 생성
                Quaternion targetRotation = Quaternion.LookRotation(direction);

                // transform.rotation(카메라 전체)과 피봇 로테이션을 부드럽게 수렴시킴
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * currentLerpSpeed);

                // 피봇이 따로 놀지 않도록 정면(0,0,0)으로 서서히 정렬
                cameraPivotTransform.localRotation = Quaternion.Slerp(cameraPivotTransform.localRotation, Quaternion.identity, Time.deltaTime * currentLerpSpeed);
            }
        }

        else if (player.playerNetworkManager.isLockedOn.Value)
        {
            // 해당 게임 오브젝트를 로테이트함
            Vector3 rotationDirection = player.playerCombatManager.currentTarget.characterCombatManager.lockOnTransform.position - transform.position;
            rotationDirection.Normalize();
            rotationDirection.y = 0;

            Quaternion targetRotation = Quaternion.LookRotation(rotationDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, lockOnTargetFollowSpeed);

            // 해당 피봇 오브젝트를 로테이트함.
            rotationDirection = player.playerCombatManager.currentTarget.characterCombatManager.lockOnTransform.position - cameraPivotTransform.position;
            rotationDirection.Normalize();

            targetRotation = Quaternion.LookRotation(rotationDirection);
            cameraPivotTransform.transform.rotation = Quaternion.Slerp(cameraPivotTransform.rotation, targetRotation, lockOnTargetFollowSpeed);

            // 우리의 로테이션을 룩앵글로 세이브, 언락했을대 너무 스냅 하지 않도록.
            leftAndRightLookAngle = transform.eulerAngles.y;
            upAndDownLookAngle = transform.eulerAngles.x;
        }
        else
        {
            // 락을 할 시, 타겟에 로테이션을 고정.
            // 그렇지않을 경우 일반적이게 로테이트

            // 카메라수평인풋값에따라 leftAndRightLookAngle이 바뀌게.
            leftAndRightLookAngle += (cameraHorizontalInput * leftAndRightRotationSpeed) * Time.deltaTime;
            // 카메라수직인풋값에 따라 위아래 전환.
            upAndDownLookAngle -= (cameraVerticalInput * upAndDownRotationSpeed) * Time.deltaTime;
            // 최소최대값의 앵글을 클램프해줌.
            upAndDownLookAngle = Mathf.Clamp(upAndDownLookAngle, minimumPivot, maximumPivot);

            Vector3 cameraRotation = Vector3.zero;
            Quaternion targetRotation;

            // 게임오브젝트를 좌우로 로테이션함
            cameraRotation.y = leftAndRightLookAngle;
            targetRotation = Quaternion.Euler(cameraRotation);
            transform.rotation = targetRotation;

            // 게임오브젝트 위아래로 로테이션
            cameraRotation = Vector3.zero;
            cameraRotation.x = upAndDownLookAngle;
            targetRotation = Quaternion.Euler(cameraRotation);
            cameraPivotTransform.localRotation = targetRotation;
        }

    }

    private void HandleCollision()
    {
        targetCameraZPosition = cameraZPosition;
        RaycastHit hit;
        // 콜리션의 방향 체크
        Vector3 direction = cameraObject.transform.position - cameraPivotTransform.position;
        direction.Normalize();

        // 우리가 원하는 방향에 오브젝트가 있는지 체크한다.
        if (Physics.SphereCast(
            cameraPivotTransform.position,
            cameraCollisionRadius,
            direction,
            out hit,
            Mathf.Abs(targetCameraZPosition),
            collideWithLayers))
        {
            // 만약 장애물이 있다면, 거리를 구한다.
            float distanceFromHitObject = Vector3.Distance(cameraPivotTransform.position, hit.point);
            // 그 이후 타겟 Z 포지션으로 따라다니도록 값을 같게 해준다.
            targetCameraZPosition = -(distanceFromHitObject - cameraCollisionRadius);
        }

        // 만약 타켓 포지션이 콜리션 범위보다 좁다면, 쿨리션 범위만큼 뺀다(뒤로 물러나게 함)
        if (Mathf.Abs(targetCameraZPosition) < cameraCollisionRadius)
        {
            targetCameraZPosition = -cameraCollisionRadius;
        }

        // 마지막으로 시간값 0.2f를 활용하여 마지막 포지션으로 lerp를 적용해준다.
        cameraObjectPosition.z = Mathf.Lerp(cameraObject.transform.localPosition.z, targetCameraZPosition, 0.2f);
        cameraObject.transform.localPosition = cameraObjectPosition;
    }

    public void HandleLocatingLockOnTargets()
    {
        float shortestDistance = Mathf.Infinity; // 타겟이 얼마나 근처에 있는지 정함..
        float shortestDistanceOfRightTarget = Mathf.Infinity; // 한 axis 로부터 우측 최단 타겟(-)
        float shortestDistantOfLeftTarget = -Mathf.Infinity; // 한 axis 로부터 좌측 최단 타겟(-)   

        // TD : 레이어 마스크 사용
        Collider[] colliders = Physics.OverlapSphere(
            player.transform.position,
            lockOnRadius,
            WorldUtilityManager.Instance.GetCharacterLayers());

        for (int i = 0; i < colliders.Length; i++)
        {
            CharacterManager lockOnTarget = colliders[i].GetComponent<CharacterManager>();

            if (lockOnTarget != null)
            {
                // FOV 내 있는지 체크
                Vector3 lockOnTargetsDirection = lockOnTarget.transform.position - player.transform.position;
                float distanceFromTarget = Vector3.Distance(player.transform.position, lockOnTarget.transform.position);
                float viewableAngle = Vector3.Angle(lockOnTargetsDirection, cameraObject.transform.forward);

                // 타겟이 죽은 상태면 포문 계속 진행.
                if (lockOnTarget.characterNetworkManager.isDead.Value)
                    continue;

                // 타겟을 자신으로 잡앗을 시, 무시하고 다음 타겟 진행.
                if (lockOnTarget.transform.root == player.transform.root)
                    continue;

                /* // 타겟이 사거리 바깥일 시, 다음 타겟 진행.
                                if (distanceFromTarget > maximumLockOnDistance)
                                    continue;*/

                // 타겟이 FOV 바깥에 있거나 환경에 의해 블럭된다면, 다음 포텐셜 타겟으로.
                if (viewableAngle > minimumViewableAngle && viewableAngle < maximumViewableAngle)
                {
                    RaycastHit hit;

                    // TD : 환경 레이어 전용 레이어마스크 추가.
                    if (Physics.Linecast(
                        player.playerCombatManager.lockOnTransform.position,
                        lockOnTarget.characterCombatManager.lockOnTransform.position,
                        out hit,
                        WorldUtilityManager.Instance.GetEnviroLayers()))
                    {
                        // 환경레이어에서 무언가 닿았을 시, 타겟에 락온 불가.
                        continue;
                    }
                    else
                    {
                        // 그렇지 않다면, 포텐셜 타겟리스트에 추가.
                        availableTarget.Add(lockOnTarget);
                    }
                }
            }
        }

        // 위 availabletarget을 전부 정리 후, 어떤 것이 첫번째가 될지 정함.
        for (int k = 0; k < availableTarget.Count; k++)
        {
            if (availableTarget[k] != null)
            {
                float distanceFromTarget = Vector3.Distance(player.transform.position, availableTarget[k].transform.position);

                if (distanceFromTarget < shortestDistance)
                {
                    shortestDistance = distanceFromTarget;
                    nearestLockOnTarget = availableTarget[k];
                }

                // 만약 타겟을 찾던 중 이미 락온 됫다면, 가장 근처의 좌우 타겟 찾기
                if (player.playerNetworkManager.isLockedOn.Value)
                {
                    Vector3 relativeEnemyPosition = player.transform.InverseTransformPoint(availableTarget[k].transform.position);

                    var distanceFromLeftTarget = relativeEnemyPosition.x;
                    var distanceFromRightTarget = relativeEnemyPosition.x;

                    // 존재하는 타겟이 현재 타겟이면 무시하고 다음으로 진행.
                    if (availableTarget[k] == player.playerCombatManager.currentTarget)
                        continue;

                    // 타겟의 좌측을 체크
                    if (relativeEnemyPosition.x <= 0.00 && distanceFromLeftTarget > shortestDistantOfLeftTarget)
                    {
                        shortestDistantOfLeftTarget = distanceFromLeftTarget;
                        leftLockOnTarget = availableTarget[k];
                    }
                    // 타겟의 우측을 체크
                    else if (relativeEnemyPosition.x >= 0.00 && distanceFromRightTarget < shortestDistanceOfRightTarget)
                    {
                        shortestDistanceOfRightTarget = distanceFromRightTarget;
                        rightLockOnTarget = availableTarget[k];
                    }
                }
            }
            else
            {
                ClearLockOnTargets();
                player.playerNetworkManager.isLockedOn.Value = false;
            }
        }
    }

    // [수정사항] 라우터(PlayerManager)로부터 수신한 방향(Enum) 데이터를 바탕으로 도메인 레벨에서 물리적인 타겟을 전환합니다.
    // 기존에는 탐색(HandleLocatingLockOnTargets)만 하고 전환하는 '실행 함수'가 없었습니다.
    public void SwitchLockOnTarget(LockOnDirection direction)
    {
        if (direction == LockOnDirection.Left && leftLockOnTarget != null)
        {
            player.playerCombatManager.SetTarget(leftLockOnTarget);
        }
        else if (direction == LockOnDirection.Right && rightLockOnTarget != null)
        {
            player.playerCombatManager.SetTarget(rightLockOnTarget);
        }
        // 타겟이 없는 경우(null) 무시함으로써 자연스러운 게임 플레이 흐름을 유지합니다.
    }

    public void SetLockCameraHeight()
    {
        if (cameraLockOnHeightCoroutine != null)
        {
            StopCoroutine(cameraLockOnHeightCoroutine);
        }

        cameraLockOnHeightCoroutine = StartCoroutine(SetCameraHeight());
    }

    public void ClearLockOnTargets()
    {
        nearestLockOnTarget = null;
        leftLockOnTarget = null;
        rightLockOnTarget = null;
        availableTarget.Clear();
    }

    public IEnumerator WaitThenFindNewTarget()
    {
        // 당신이 하는 액션이 끝나길 기다린 후, 만약 현타겟이 죽엇다면
        // 새타겟을 nearest 타겟으로 지정.
        while (player.isPerformingAction)
        {
            // 아무고토 하지마라
            yield return null;
        }

        ClearLockOnTargets();
        HandleLocatingLockOnTargets();

        if (nearestLockOnTarget != null)
        {
            player.playerCombatManager.SetTarget(nearestLockOnTarget);
            player.playerNetworkManager.isLockedOn.Value = true;
        }

        yield return null;
    }

    public IEnumerator SetCameraHeight()
    {
        // 락온플래그가 바뀔때마다 불러와짐.
        // 코루틴이 끝나기까지 걸리는 시간.
        float duration = 1;
        float timer = 0;

        Vector3 velocity = Vector3.zero;
        Vector3 newLockedCameraHeight = new Vector3(cameraPivotTransform.transform.localPosition.x, lockedCameraHeight);
        Vector3 newUnlockedCameraHeight = new Vector3(cameraPivotTransform.transform.localPosition.x, unlockedCameraHeight);

        while (timer < duration)
        {
            timer += Time.deltaTime;

            if (player != null)
            {
                if (player.playerCombatManager.currentTarget != null)
                {
                    cameraPivotTransform.transform.localPosition =
                        Vector3.SmoothDamp(cameraPivotTransform.transform.localPosition, newLockedCameraHeight, ref velocity, setCameraHeightSpeed);
                    cameraPivotTransform.transform.localRotation =
                        Quaternion.Slerp(cameraPivotTransform.transform.localRotation, Quaternion.Euler(0, 0, 0), lockOnTargetFollowSpeed);
                }
                else
                {
                    // 타겟이 없는데 잡혓다면, 원래 언락지점으로 회귀
                    cameraPivotTransform.transform.localPosition =
                        Vector3.SmoothDamp(cameraPivotTransform.transform.localPosition, newUnlockedCameraHeight, ref velocity, setCameraHeightSpeed);
                }
            }
            yield return null;
        }

        if (player != null)
        {
            // 발생하면 안되지만, 만약 발생한다면 스냅시켜줌
            if (player.playerCombatManager.currentTarget != null)
            {
                cameraPivotTransform.transform.localPosition =
                    newLockedCameraHeight;
                cameraPivotTransform.transform.localRotation =
                    Quaternion.Euler(0, 0, 0);
            }
            else
            {
                cameraPivotTransform.transform.localPosition = newUnlockedCameraHeight;
            }
        }

        yield return null;
    }

    #region API for WorldCameraManager (Called from Manager)
    /// <summary>
    /// 외부 시스템에서 특정 오브젝트를 주시하도록 명령할 때 호출됩니다.
    /// </summary>

    // [수정사항] 매개변수를 프리셋 구조체로 통합 적용
    internal void SetContextualFocus(Transform target, CameraStancePreset preset)
    {
        currentFocusTarget = target;

        targetFOV = preset.fov;

        currentLerpSpeed = preset.lerpSpeed;

        currentOffset = preset.customOffset;

        isContexualMode = true;
    }

    internal void ClearContextualFocus()
    {
        isContexualMode = false;

        currentFocusTarget = null;

        targetFOV = defaultFOV;

        // [수정사항] 시점 스냅(튀는 현상) 방지 로직 추가
        leftAndRightLookAngle = transform.eulerAngles.y;
        upAndDownLookAngle = transform.eulerAngles.x;

        // 다음 프레임부터 HandleFollowTarget이 다시 플레이어를 쫓음.
    }

    internal void Shake(float intensity, float duration)
    {
        shakeIntensity = intensity;

        shakeDuration = duration;
    }

    internal void SetBodycamWeight(float weight)
    {
        bodycamWeight = weight;
    }


    #endregion
}