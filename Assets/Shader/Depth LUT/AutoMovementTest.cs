using UnityEngine;
using System.Collections;

/// <summary>
/// 모션 블러 및 셰이더 테스트를 위해 오브젝트를 자동으로 움직이게 하는 스크립트입니다.
/// </summary>
public class AutoMovementTest : MonoBehaviour
{
    public enum MovementPattern
    {
        PingPong,   // 지정된 거리를 부드럽게 왔다갔다 함 (가속/감속 있음)
        Linear,     // 일정한 속도로 왔다갔다 함 (끝에서 튕기듯 방향 전환)
        Teleport    // A 지점에서 B 지점으로 순간이동 반복 (극단적인 모션 벡터 테스트용)
    }

    [Header("Movement Settings")]
    [Tooltip("이동 패턴을 선택합니다.")]
    public MovementPattern pattern = MovementPattern.PingPong;

    [Tooltip("이동할 방향과 최대 거리입니다. (예: X축으로 5만큼 이동 = 5, 0, 0)")]
    public Vector3 movementOffset = new Vector3(5f, 0f, 0f);

    [Tooltip("이동 속도입니다. 높을수록 빨리 움직입니다.")]
    [Range(0.1f, 20f)] public float speed = 2.0f;

    [Header("Delay Settings")]
    [Tooltip("왕복 후 끝 지점에서 대기하는 시간(초)입니다.")]
    public float delayAtEnds = 0.5f;

    // 내부 상태 변수
    private Vector3 _startPosition;
    private Vector3 _targetPosition;
    private bool _movingToTarget = true;
    private bool _isWaiting = false;
    private float _pingPongTime = 0f;

    void Start()
    {
        // 시작 위치 기록
        _startPosition = transform.position;
        _targetPosition = _startPosition + movementOffset;
    }

    void Update()
    {
        if (_isWaiting) return;

        switch (pattern)
        {
            case MovementPattern.PingPong:
                HandlePingPongMovement();
                break;
            case MovementPattern.Linear:
                HandleLinearMovement();
                break;
            case MovementPattern.Teleport:
                HandleTeleportMovement();
                break;
        }
    }

    private void HandlePingPongMovement()
    {
        // PingPong은 Mathf.PingPong을 사용하거나 Sin 곡선으로 부드럽게 처리
        _pingPongTime += Time.deltaTime * speed;

        // 0 ~ 1 사이를 왕복하는 값을 생성 (Sine 곡선 사용으로 부드러운 가감속)
        float t = (Mathf.Sin(_pingPongTime - Mathf.PI / 2f) + 1f) / 2f;

        transform.position = Vector3.Lerp(_startPosition, _targetPosition, t);

        // 끝점에 도달했을 때 딜레이 체크 로직 추가 (필요시)
        // 사인파 기반은 자체적으로 부드럽게 멈추므로 딜레이 처리가 약간 까다로울 수 있어
        // 여기서는 기본적으로 연속적인 부드러운 왕복만 수행합니다.
    }

    private void HandleLinearMovement()
    {
        Vector3 currentTarget = _movingToTarget ? _targetPosition : _startPosition;

        // MoveTowards로 일정한 속도로 이동
        transform.position = Vector3.MoveTowards(transform.position, currentTarget, speed * Time.deltaTime);

        // 목표 지점에 도달했는지 확인
        if (Vector3.Distance(transform.position, currentTarget) < 0.001f)
        {
            StartCoroutine(WaitAndReverse());
        }
    }

    private void HandleTeleportMovement()
    {
        Vector3 currentTarget = _movingToTarget ? _targetPosition : _startPosition;
        transform.position = currentTarget;
        StartCoroutine(WaitAndReverse());
    }

    private IEnumerator WaitAndReverse()
    {
        _isWaiting = true;

        // 방향 전환
        _movingToTarget = !_movingToTarget;

        if (delayAtEnds > 0f)
        {
            yield return new WaitForSeconds(delayAtEnds);
        }

        _isWaiting = false;
    }

    // 에디터 씬 뷰에서 이동 경로를 미리 보여주는 기즈모
    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) return;

        Vector3 start = transform.position;
        Vector3 end = start + movementOffset;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(start, end);
        Gizmos.DrawWireSphere(end, 0.5f);
    }
}