using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class PlayerCamera : MonoBehaviour
{
    public static PlayerCamera Instance { get; private set; }

    public Camera cameraObject;
    public PlayerManager player;
    [SerializeField] Transform cameraPivotTransform;

    // 카메라 퍼포먼스 수정용
    [Header("Camera Setting")]
    private float cameraSmoothSpeed = 1.0f; // 숫자가 클수록 카메라가 포지션에 도달하는 시간증가
    [SerializeField] float leftAndRightRotationSpeed = 220;
    [SerializeField] float upAndDownRotationSpeed = 220;
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
    }

    public void HandleAllCameraActions()
    {
        if (player != null)
        {
            HandleFollowTarget();
            HandleRotation();
            HandleCollision();
        }
        // 유저 따라오기
        // 플레이어 주변 로테이션
        // 오브젝트와 충돌(통과x)
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
            leftAndRightLookAngle += (PlayerInputManager.Instance.cameraHorizontalInput * leftAndRightRotationSpeed) * Time.deltaTime;
            // 카메라수직인풋값에 따라 위아래 전환.
            upAndDownLookAngle -= (PlayerInputManager.Instance.cameraVerticalInput * upAndDownRotationSpeed) * Time.deltaTime;
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
        Vector3 direction = cameraObject.transform.forward - cameraPivotTransform.position;
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

/*                // 타겟이 사거리 바깥일 시, 다음 타겟 진행.
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
                        Quaternion.Slerp(cameraPivotTransform.transform.localRotation, Quaternion.Euler(0,0,0), lockOnTargetFollowSpeed);
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
}
