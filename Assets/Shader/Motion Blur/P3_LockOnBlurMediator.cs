using UnityEngine;
using System.Collections;

/// <summary>
/// [우선순위 3 — LockOnBlurMediator]
///
/// ■ 이 파일이 하는 일
///   락온 상태 변화를 감지하여 SSMB(스크린스페이스 블러)와
///   OMB(오브젝트 블러) 강도를 적절히 보간합니다.
///
/// ■ 보간 비대칭 설계의 기술적 이유
///   락온 진입(느린 보간 0.4초): 갑작스러운 배경 선명화를 방지.
///     인간 시각계는 급격한 블러 변화를 '화면 이상'으로 인식합니다.
///     느린 보간으로 자연스럽게 카메라가 목표를 고정하는 느낌을 전달합니다.
///
///   락온 중 이동 반응(빠른 보간 0.08초): 즉각적인 기동감 전달.
///     이동 시 방향성 잔상이 즉각 발생해야 속도감이 유지됩니다.
///     느린 반응은 이동과 시각 효과 사이의 '지연감'을 만들어 조작감을 해칩니다.
///
///   락온 중 공격 시 OMB 배율 강화(×1.4):
///     락온은 SSMB를 억제하므로 전체적인 블러 '예산'이 줄어듭니다.
///     OMB를 강화하여 공격 타격감을 보상합니다.
///
/// ■ ShaderCoordinationManager와의 연동
///   이 스크립트는 Shader.SetGlobalFloat으로 _SSMBIntensityScale을 주입합니다.
///   ShaderCoordinationManager는 이 값을 _SSMBIntensity에 곱하여 최종 강도를 결정합니다.
///   직접 ShaderCoordinationManager를 수정하지 않고 독립적으로 작동합니다.
///
/// ■ 유저 체감 효과
///   - 락온 시: 배경이 부드럽게 조용해지며 전투 집중도 상승
///   - 이동 시: 방향성 잔상이 즉각 발생하여 기동감 유지
///   - 공격 시: 락온 중에도 무기 스윙이 강렬하게 표현
/// </summary>
public class LockOnBlurMediator : MonoBehaviour
{
    public static LockOnBlurMediator Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────
    [Header("Lock-On SSMB Attenuation")]
    [Tooltip("락온 중 SSMB 강도 배율 (0~1). 낮을수록 배경 블러가 약해집니다.")]
    [Range(0f, 1f)] public float lockOnSSMBScale = 0.25f;

    [Tooltip("락온 진입 시 보간 시간 (초). 느릴수록 부드럽습니다.")]
    [Range(0.1f, 1f)] public float lockOnFadeInDuration = 0.4f;

    [Tooltip("락온 해제 시 복귀 보간 시간 (초).")]
    [Range(0.1f, 1f)] public float lockOffFadeOutDuration = 0.5f;

    [Header("Lock-On Move Response")]
    [Tooltip("락온 중 이동 감지 시 SSMB 복귀 배율.")]
    [Range(0f, 1f)] public float lockOnMoveSSMBScale = 0.7f;

    [Tooltip("이동 감지 시 SSMB 활성화 보간 시간 (빠를수록 즉각적).")]
    [Range(0.01f, 0.3f)] public float moveFadeInDuration = 0.08f;

    [Tooltip("이동 종료 시 락온 기본값 복귀 보간 시간.")]
    [Range(0.1f, 0.5f)] public float moveStopFadeOutDuration = 0.2f;

    [Tooltip("이동으로 판단하는 속도 임계값 (m/s).")]
    [Range(0.1f, 3f)] public float moveSpeedThreshold = 0.5f;

    [Header("Dependencies")]
    [Tooltip("락온 대상 오브젝트들의 ObjectMotionBlurController 목록.\n" +
             "락온 시 자동으로 isLockedOn=true 설정됩니다.")]
    public ObjectMotionBlurController[] trackedControllers;

    [Tooltip("플레이어의 Rigidbody 또는 CharacterController (이동 속도 감지용).")]
    public Rigidbody playerRigidbody;
    public CharacterController playerCharacterController;

    // ─────────────────────────────────────────────────────────────
    // 내부 상태
    private bool _isLockedOn = false;
    private bool _isMoving   = false;
    private float _currentSSMBScale = 1f;
    private float _targetSSMBScale  = 1f;
    private float _lerpSpeed = 1f;

    // Shader Property IDs
    private static readonly int SSMBScaleID = Shader.PropertyToID("_SSMBIntensityScale");

    // ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void Update()
    {
        // 이동 속도 감지
        float speed = GetPlayerSpeed();
        bool moving = speed > moveSpeedThreshold;

        if (_isLockedOn)
        {
            if (moving && !_isMoving)
            {
                // 락온 중 이동 시작 → SSMB 빠르게 활성화
                _targetSSMBScale = lockOnMoveSSMBScale;
                _lerpSpeed = 1f / moveFadeInDuration;
                _isMoving = true;
            }
            else if (!moving && _isMoving)
            {
                // 이동 종료 → 락온 기본값으로 천천히 복귀
                _targetSSMBScale = lockOnSSMBScale;
                _lerpSpeed = 1f / moveStopFadeOutDuration;
                _isMoving = false;
            }
        }

        // 부드러운 보간
        _currentSSMBScale = Mathf.MoveTowards(
            _currentSSMBScale, _targetSSMBScale,
            _lerpSpeed * Time.deltaTime);

        // 전역 파라미터 주입
        Shader.SetGlobalFloat(SSMBScaleID, _currentSSMBScale);
    }

    // ── 외부 API ──────────────────────────────────────────────────

    /// <summary>
    /// 락온 시스템에서 호출. 락온 진입 시 true, 해제 시 false.
    /// </summary>
    public void SetLockOn(bool locked, ObjectMotionBlurController[] targets = null)
    {
        _isLockedOn = locked;
        _isMoving   = false;

        if (locked)
        {
            // 락온 진입 — SSMB 느리게 감소
            _targetSSMBScale = lockOnSSMBScale;
            _lerpSpeed = 1f / lockOnFadeInDuration;

            // 대상 OMB에 락온 상태 전달
            var controllers = targets ?? trackedControllers;
            if (controllers != null)
                foreach (var c in controllers)
                    if (c != null) c.SetLockOn(true);
        }
        else
        {
            // 락온 해제 — SSMB 천천히 복귀
            _targetSSMBScale = 1f;
            _lerpSpeed = 1f / lockOffFadeOutDuration;

            if (trackedControllers != null)
                foreach (var c in trackedControllers)
                    if (c != null) c.SetLockOn(false);
        }
    }

    // ─────────────────────────────────────────────────────────────
    private float GetPlayerSpeed()
    {
        if (playerRigidbody != null)
            return playerRigidbody.linearVelocity.magnitude;
        if (playerCharacterController != null)
            return playerCharacterController.velocity.magnitude;
        return 0f;
    }
}
