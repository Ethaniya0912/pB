using UnityEngine;

/// <summary>
/// [Derived Class] 플레이어의 이동 상태(정지/보행/질주)를 해석하여 
/// ShaderCoordinationManager를 통해 시각적 피드백을 단계별로 송출합니다.
/// </summary>
public class PlayerVisualFeedbackManager : CharacterVisualFeedbackManager
{
    private PlayerManager _player;
    // [최적화] 매 프레임 Instance 호출을 피하기 위해 입력 매니저 캐싱
    private PlayerInputManager _inputManager;

    [Header("Step Response Settings")]
    [Range(0f, 1f)]
    [Tooltip("질주 상태로 판정할 임계값입니다. 이 값보다 커야 질주 전용 효과(빠른 펄스, 색수차 왜곡 등)가 발동합니다.")]
    public float sprintThreshold = 0.8f;

    [Range(0.1f, 15f)]
    [Tooltip("가속하거나 이동을 시작할 때 시야가 좁아지는 속도(응답성)입니다. 값이 높을수록 즉각적으로 반응합니다.")]
    public float accelerationResponse = 10f;

    [Range(0.1f, 10f)]
    [Tooltip("멈췄을 때 좁아졌던 시야가 다시 원래대로 회복되는 속도입니다. 낮을수록 여운이 오래 남습니다.")]
    public float recoveryResponse = 1.2f;

    [Header("Rhythm (Pulse)")]
    [Range(0.1f, 10f)]
    [Tooltip("보행 시 시야가 일렁이는 기본 주파수입니다. 숨소리나 느린 발걸음을 표현합니다.")]
    public float walkPulseFreq = 2.0f;

    [Range(0.1f, 20f)]
    [Tooltip("질주 시 심박수가 빨라지듯 시야가 일렁이는 주파수입니다.")]
    public float sprintPulseFreq = 5.0f;

    [Range(0f, 0.5f)]
    [Tooltip("시야가 일렁이는 진동의 최대 강도입니다. 값이 크면 화면이 크게 흔들립니다.")]
    public float pulseIntensity = 0.05f;

    [Header("Optimization & Anti-Ghosting")]
    [Range(0f, 0.1f)]
    [Tooltip("이 값보다 낮은 속도 인자는 완전한 0으로 간주하여 정지 시 잔상 및 떨림을 방지합니다.")]
    public float deadzoneThreshold = 0.005f;

    [Tooltip("정지 상태(Input=0)일 때 보간을 무시하고 강제로 수치를 0으로 만드는 속도 배율입니다.")]
    public float idleCutoffSpeed = 2.0f;

    [Header("Performance Control")]
    [Tooltip("체크 시 매 프레임 디버그 로그를 출력합니다. 프로파일러에서 확인된 CPU 부하의 주범이므로 평소에는 끄세요.")]
    public bool showDebugLogs = false;

    private float _smoothedSpeedFactor;

    protected override void Awake()
    {
        // 1. 부모 클래스의 Awake 호출 (CharacterManager 참조 획득)
        base.Awake();

        // PlayerManager 참조 획득
        _player = GetComponent<PlayerManager>();

        // [최적화] 인스턴스를 미리 확보하여 Update에서의 부하 감소
        _inputManager = PlayerInputManager.Instance;

        // [최적화] Awake 단계에서 PlayerManager가 없는 경우를 미리 체크하여 Update에서의 불필요한 GetComponent 에러 방지
        if (_player == null)
        {
            Debug.LogError($"[PVFM] PlayerManager를 찾을 수 없습니다! {gameObject.name}에 스크립트 누락 여부를 확인하세요.");
        }
    }

    protected override void Update()
    {
        // 2. 참조 유효성 및 네트워크 소유권 체크
        if (_player == null) return;

        if (!_player.IsOwner)
        {
            // 본인 캐릭터가 아니면 연산을 수행하지 않음
            return;
        }

        // 3. 부모 클래스의 기본 속도 계산 로직 실행 (currentSpeedFactor 갱신)
        base.Update();

        // 4. 플레이어 특화 시각 반응 처리
        HandlePlayerVisualResponse();
    }

    /// <summary>
    /// 플레이어의 입력값과 물리 속도를 조합하여 최종적인 시각 피드백 수치를 계산합니다.
    /// </summary>
    private void HandlePlayerVisualResponse()
    {
        // [최적화] 캐싱된 입력 매니저 사용 및 null 체크
        if (_inputManager == null)
        {
            _inputManager = PlayerInputManager.Instance;
            if (_inputManager == null) return;
        }

        // 1. 실제 물리 속도(currentSpeedFactor)와 조작 의도(moveAmount) 결합
        float inputAmount = _inputManager.moveAmount;
        float rawTarget = currentSpeedFactor * inputAmount;

        // 2. 비선형 매핑 (보행과 질주의 시각적 격차 형성)
        float targetFactor = (inputAmount > 0.6f) ? rawTarget : rawTarget * 0.3f;

        // 3. 템포럴 보간 (가속/감속 상황에 따른 부드러운 전이)
        float lerpSpeed = targetFactor > _smoothedSpeedFactor ? accelerationResponse : recoveryResponse;

        // 정지 상태(IDLE) 진입 시에는 더 빠르게 수치를 떨어뜨려 잔상 발생 시간을 단축
        if (inputAmount < 0.01f) lerpSpeed *= idleCutoffSpeed;

        _smoothedSpeedFactor = Mathf.Lerp(_smoothedSpeedFactor, targetFactor, Time.deltaTime * lerpSpeed);

        // [핵심 해결책] 데드존 처리
        if (_smoothedSpeedFactor < deadzoneThreshold)
        {
            _smoothedSpeedFactor = 0f;
        }

        // 4. 동적 리듬 생성
        float pulse = 0f;
        if (_smoothedSpeedFactor > 0f)
        {
            bool isSprintingState = _smoothedSpeedFactor > sprintThreshold;
            float currentFreq = isSprintingState ? sprintPulseFreq : walkPulseFreq;
            pulse = Mathf.Sin(Time.time * currentFreq) * pulseIntensity * _smoothedSpeedFactor;
        }

        // 5. [최적화] 로그 출력을 플래그로 제어 (프로파일러에서 확인된 부하 해결)
        if (showDebugLogs)
        {
            string stateName = (inputAmount > 0.6f) ? "질주" : (inputAmount > 0.1f) ? "보행" : "정지";
            Debug.Log($"[PVFM {stateName}] 수치: {_smoothedSpeedFactor:F3} | 입력: {inputAmount:F1}");
        }

        // 6. ShaderCoordinationManager 또는 전역 셰이더 변수로 데이터 송출
        if (ShaderCoordinationManager.Instance != null)
        {
            ShaderCoordinationManager.Instance.UpdatePlayerSpeedFeedback(_smoothedSpeedFactor, pulse);
        }
        else
        {
            // 중앙 매니저가 없을 경우 폴백
            Shader.SetGlobalFloat("_GlobalSpeedFactor", _smoothedSpeedFactor);
            Shader.SetGlobalFloat("_GlobalMovementPulse", pulse);
            // [수정] 에러를 일으켰던 _GlobalMipBias 대신 셰이더와 약속된 _VFXMipBias 사용
            Shader.SetGlobalFloat("_VFXMipBias", Mathf.Lerp(0f, -2.0f, _smoothedSpeedFactor));
        }
    }
}