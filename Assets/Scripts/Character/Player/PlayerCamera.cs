using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 네임스페이스 제거로 다른 클래스에서의 접근성 확보
public class PlayerCamera : MonoBehaviour
{
    public static PlayerCamera Instance { get; private set; }

    public Camera cameraObject;
    public PlayerManager player;
    [SerializeField] Transform cameraPivotTransform;

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

    [Header("Inventory Mode")]
    [SerializeField] private Transform inventoryPivot; // 캐릭터 우측 앞 대각선 위치 (인벤토리용 피벗)
    [SerializeField] private float transitionSpeed = 10f;
    private bool isInInventoryMode = false;

    private void Awake()
    {
        // 싱글턴
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        DontDestroyOnLoad(gameObject);
        cameraZPosition = cameraObject.transform.localPosition.z;
    }

    public void HandleAllCameraActions()
    {
        if (player != null)
        {
            if (isInInventoryMode)
            {
                // 인벤토리 모드일 때는 쿼터뷰 트랜지션만 수행
                HandleInventoryCameraTransition();
            }
            else
            {
                // 일반 모드일 때의 카메라 로직
                HandleFollowTarget();

                // [수정] Alt 키를 누르고 있을 때는 카메라 회전(Rotation)을 스킵하여 시점 고정
                if (PlayerInputManager.Instance.alt_Input)
                {
                    HandleCollision(); // 콜라이더 처리는 유지하여 벽 뚫림 방지
                    return;
                }

                HandleRotation();
                HandleCollision();
            }
        }
    }

    // 인벤토리 모드 토글 (외부에서 PlayerCamera.Instance.ToggleInventoryCamera로 호출)
    public void ToggleInventoryCamera(bool enabled)
    {
        isInInventoryMode = enabled;

        if (isInInventoryMode)
        {
            // 인벤토리 활성화 시 마우스 커서 해제
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            // 게임 모드 복귀 시 마우스 고정
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // 일반 모드로 돌아올 때 현재 카메라 앵글이 튀지 않도록 보정
            leftAndRightLookAngle = transform.eulerAngles.y;
            upAndDownLookAngle = cameraPivotTransform.localRotation.eulerAngles.x;
        }
    }

    private void HandleInventoryCameraTransition()
    {
        if (inventoryPivot == null) return;

        // 가방이 잘 보이도록 미리 설정된 대각선 피벗으로 부드럽게 이동 및 회전
        transform.position = Vector3.Lerp(transform.position, inventoryPivot.position, Time.deltaTime * transitionSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, inventoryPivot.rotation, Time.deltaTime * transitionSpeed);

        // 인벤토리 모드에서는 피벗 로테이션도 초기화
        cameraPivotTransform.localRotation = Quaternion.Slerp(cameraPivotTransform.localRotation, Quaternion.identity, Time.deltaTime * transitionSpeed);
    }

    private void HandleFollowTarget()
    {
        Vector3 targetCameraPosition = Vector3.SmoothDamp(
            transform.position,
            player.transform.position,
            ref cameraVelocity,
            cameraSmoothSpeed * Time.deltaTime
            );
        transform.position = targetCameraPosition;
    }

    private void HandleRotation()
    {
        if (player.playerNetworkManager.isLockedOn.Value)
        {
            Vector3 rotationDirection = player.playerCombatManager.currentTarget.characterCombatManager.lockOnTransform.position - transform.position;
            rotationDirection.Normalize();
            rotationDirection.y = 0;

            Quaternion targetRotation = Quaternion.LookRotation(rotationDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, lockOnTargetFollowSpeed);

            rotationDirection = player.playerCombatManager.currentTarget.characterCombatManager.lockOnTransform.position - cameraPivotTransform.position;
            rotationDirection.Normalize();

            targetRotation = Quaternion.LookRotation(rotationDirection);
            cameraPivotTransform.transform.rotation = Quaternion.Slerp(cameraPivotTransform.rotation, targetRotation, lockOnTargetFollowSpeed);

            leftAndRightLookAngle = transform.eulerAngles.y;
            upAndDownLookAngle = transform.eulerAngles.x;
        }
        else
        {
            leftAndRightLookAngle += (PlayerInputManager.Instance.cameraHorizontalInput * leftAndRightRotationSpeed) * Time.deltaTime;
            upAndDownLookAngle -= (PlayerInputManager.Instance.cameraVerticalInput * upAndDownRotationSpeed) * Time.deltaTime;
            upAndDownLookAngle = Mathf.Clamp(upAndDownLookAngle, minimumPivot, maximumPivot);

            Vector3 cameraRotation = Vector3.zero;
            Quaternion targetRotation;

            cameraRotation.y = leftAndRightLookAngle;
            targetRotation = Quaternion.Euler(cameraRotation);
            transform.rotation = targetRotation;

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
        Vector3 direction = cameraObject.transform.position - cameraPivotTransform.position;
        direction.Normalize();

        if (Physics.SphereCast(cameraPivotTransform.position, cameraCollisionRadius, direction, out hit, Mathf.Abs(targetCameraZPosition), collideWithLayers))
        {
            float distanceFromHitObject = Vector3.Distance(cameraPivotTransform.position, hit.point);
            targetCameraZPosition = -(distanceFromHitObject - cameraCollisionRadius);
        }

        if (Mathf.Abs(targetCameraZPosition) < cameraCollisionRadius)
        {
            targetCameraZPosition = -cameraCollisionRadius;
        }

        cameraObjectPosition.z = Mathf.Lerp(cameraObject.transform.localPosition.z, targetCameraZPosition, 0.2f);
        cameraObject.transform.localPosition = cameraObjectPosition;
    }

    public void HandleLocatingLockOnTargets()
    {
        float shortestDistance = Mathf.Infinity;
        float shortestDistanceOfRightTarget = Mathf.Infinity;
        float shortestDistantOfLeftTarget = -Mathf.Infinity;

        Collider[] colliders = Physics.OverlapSphere(player.transform.position, lockOnRadius, WorldUtilityManager.Instance.GetCharacterLayers());

        for (int i = 0; i < colliders.Length; i++)
        {
            CharacterManager lockOnTarget = colliders[i].GetComponent<CharacterManager>();

            if (lockOnTarget != null)
            {
                Vector3 lockOnTargetsDirection = lockOnTarget.transform.position - player.transform.position;
                float viewableAngle = Vector3.Angle(lockOnTargetsDirection, cameraObject.transform.forward);

                if (lockOnTarget.characterNetworkManager.isDead.Value) continue;
                if (lockOnTarget.transform.root == player.transform.root) continue;

                if (viewableAngle > minimumViewableAngle && viewableAngle < maximumViewableAngle)
                {
                    RaycastHit hit;
                    if (Physics.Linecast(player.playerCombatManager.lockOnTransform.position, lockOnTarget.characterCombatManager.lockOnTransform.position, out hit, WorldUtilityManager.Instance.GetEnviroLayers()))
                    {
                        continue;
                    }
                    else
                    {
                        availableTarget.Add(lockOnTarget);
                    }
                }
            }
        }

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

                if (player.playerNetworkManager.isLockedOn.Value)
                {
                    Vector3 relativeEnemyPosition = player.transform.InverseTransformPoint(availableTarget[k].transform.position);
                    var distanceFromLeftTarget = relativeEnemyPosition.x;
                    var distanceFromRightTarget = relativeEnemyPosition.x;

                    if (availableTarget[k] == player.playerCombatManager.currentTarget) continue;

                    if (relativeEnemyPosition.x <= 0.00 && distanceFromLeftTarget > shortestDistantOfLeftTarget)
                    {
                        shortestDistantOfLeftTarget = distanceFromLeftTarget;
                        leftLockOnTarget = availableTarget[k];
                    }
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

    public void SetLockCameraHeight()
    {
        if (cameraLockOnHeightCoroutine != null) StopCoroutine(cameraLockOnHeightCoroutine);
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
        while (player.isPerformingAction) yield return null;
        ClearLockOnTargets();
        HandleLocatingLockOnTargets();

        if (nearestLockOnTarget != null)
        {
            player.playerCombatManager.SetTarget(nearestLockOnTarget);
            player.playerNetworkManager.isLockedOn.Value = true;
        }
    }

    public IEnumerator SetCameraHeight()
    {
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
                    cameraPivotTransform.transform.localPosition = Vector3.SmoothDamp(cameraPivotTransform.transform.localPosition, newLockedCameraHeight, ref velocity, setCameraHeightSpeed);
                }
                else
                {
                    cameraPivotTransform.transform.localPosition = Vector3.SmoothDamp(cameraPivotTransform.transform.localPosition, newUnlockedCameraHeight, ref velocity, setCameraHeightSpeed);
                }
            }
            yield return null;
        }
    }

    public void SetInventoryPivot(Transform pivot)
    {
        inventoryPivot = pivot;
    }
}