using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
}
