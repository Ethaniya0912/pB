using UnityEngine;
using Unity.Netcode;

namespace SG
{
    /// <summary>
    /// NetworkTransform 대신 사용하는 최적화된 아이템 동기화 클래스입니다.
    /// 움직임이 멈추면 데이터 전송을 중단하여 대역폭을 절약합니다.
    /// </summary>
    public class OptimizedNetworkItem : NetworkBehaviour
    {
        // 위치와 회전값을 동기화하는 변수 (기본적으로 Late Joiner 동기화 지원)
        private readonly NetworkVariable<Vector3> netPosition = new NetworkVariable<Vector3>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server // 서버만 위치를 쓸 수 있음
        );

        private readonly NetworkVariable<Quaternion> netRotation = new NetworkVariable<Quaternion>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        [Header("Settings")]
        [SerializeField] private float movementThreshold = 0.05f; // 이보다 적게 움직이면 동기화 안 함 (떨림 방지)
        [SerializeField] private float lerpSpeed = 10f; // 클라이언트 보간 속도

        private Rigidbody rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // 클라이언트라면 물리 연산 끄기 (서버 위치를 따라가야 하므로)
            // 단, 아이템 획득 등을 위해 Trigger나 Collider는 켜둘 수 있음
            if (!IsServer && rb != null)
            {
                rb.isKinematic = true;
            }
        }

        private void FixedUpdate()
        {
            // 위치 업데이트 권한은 오직 서버에게만 있음
            if (IsServer)
            {
                UpdateServerState();
            }
        }

        private void Update()
        {
            // 클라이언트는 서버가 보내준 값으로 부드럽게 이동
            if (!IsServer)
            {
                UpdateClientState();
            }
        }

        // [Server] 물리가 적용된 실제 위치를 변수에 담음
        private void UpdateServerState()
        {
            // 현재 저장된 네트워크 위치와 실제 위치의 차이가 임계값보다 클 때만 업데이트
            if (Vector3.Distance(transform.position, netPosition.Value) > movementThreshold)
            {
                netPosition.Value = transform.position;
                netRotation.Value = transform.rotation;
            }
            // 움직임이 멈추면(Sleep), 이 조건문이 false가 되어 아무런 패킷도 보내지 않음 -> 최적화 핵심!
        }

        // [Client] 변수(netPosition)가 바뀌면 그쪽으로 이동
        private void UpdateClientState()
        {
            // 순간이동(Teleport) 방지를 위해 Lerp 사용
            transform.position = Vector3.Lerp(transform.position, netPosition.Value, Time.deltaTime * lerpSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, netRotation.Value, Time.deltaTime * lerpSpeed);
        }
    }
}