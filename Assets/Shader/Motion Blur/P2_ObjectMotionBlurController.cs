using UnityEngine;
using TDA.Cameras;       // ICameraEffectReceiver, CameraEffectOverlayData
using TDA.Core.Events;   // AnimationEventType

/// <summary>
/// [우선순위 2 — ObjectMotionBlurController]
///
/// ■ 이 파일이 하는 일
///   1. 이전 프레임 모델 행렬(_PrevObjectToWorld)을 매 LateUpdate마다 셰이더에 전달.
///   2. AvatarAutoWeightBaker로부터 WeightBuffer 레퍼런스를 받아 함께 MPB에 포함.
///   3. BlurState에 따라 ShutterAngle 배율을 자동 조정 (P4 WeaponBlurStateController 연동).
///   4. [신규] ICameraEffectReceiver 구현 — BlurEventResponseSO 기반 데이터 드리븐 Pulse.
///
/// ■ WeightBuffer 관리 방식
///   GetPropertyBlock()은 ComputeBuffer를 복사하지 않습니다.
///   따라서 GetPropertyBlock → SetPropertyBlock 반복 시 WeightBuffer가 유실됩니다.
///   이 문제를 해결하기 위해 _weightBuffer 레퍼런스를 이 클래스가 보관하고,
///   매 LateUpdate에서 MPB를 직접 구성할 때 항상 포함시킵니다.
///
/// ■ 부착 위치
///   캐릭터 루트 오브젝트. SkinnedMeshRenderer가 자식에 있어도 자동 탐색합니다.
/// </summary>
public class ObjectMotionBlurController : MonoBehaviour, ICameraEffectReceiver
{
    // ── 블러 상태 열거형 ──────────────────────────────────────────
    public enum BlurState
    {
        Idle,        // 정적 대기 — ShutterMult 0.0
        Strafe,      // 이동 중   — ShutterMult 0.8
        Aim,         // 조준/예비 — ShutterMult 0.5
        Attack,      // 공격      — ShutterMult 2.0
        HeavyAttack  // 강공격    — ShutterMult 3.0
    }

    // ─────────────────────────────────────────────────────────────
    // Inspector
    // ─────────────────────────────────────────────────────────────
    [Header("Target Renderer")]
    [Tooltip("블러를 적용할 Renderer. 비워두면 자식에서 자동 탐색합니다.")]
    public Renderer targetRenderer;

    [Header("Shutter Settings")]
    [Tooltip("ShutterAngle/GlobalIntensity가 StretchAbs에 반영되려면 BlurState != Idle이어야 합니다.\n" +
             "Idle의 ShutterMult=0이라 stretch=0이 되기 때문입니다.\n" +
             "이 옵션을 켜면 Idle 상태에서도 ShutterMult=1로 강제해 테스트 가능합니다.")]
    public bool debugForceIdleStretch = false;

    [Tooltip("BlurState 전환 로그 출력 여부 (기본 OFF — 노이즈 감소).\n" +
             "디버깅 시에만 켜서 매 상태 변경마다 [P2_BlurCtrl] 로그를 받습니다.")]
    public bool verboseLogging = false;

    [Tooltip("셔터 앵글 (도). 180°=표준, 270~360°=강한 블러.")]
    [Range(1f, 360f)] public float shutterAngle = 180f;

    [Tooltip("목표 FPS. 60 고정 권장.")]
    [Range(24f, 120f)] public float targetFPS = 60f;

    [Tooltip("전역 블러 강도 배율. 0이면 전체 블러 무효화됩니다. 기본 1.0.")]
    [Range(0f, 2f)] public float globalIntensity = 1f;

    [Header("Blur Settings")]
    [Tooltip("로컬 블러 강도 배율. (전역 강도 × 이 값)")]
    [Range(0f, 2f)] public float blurIntensity = 1f;

    [Tooltip("최대 블러 길이 (월드 단위). 너무 크면 메시가 찢어져 보입니다.")]
    [Range(0f, 0.5f)] public float maxBlurLength = 0.25f;

    [Tooltip("정지 시에도 유지할 최소 블러. 0이면 팝인 발생 가능.\n" +
             "보스: 0.015  적: 0.005  플레이어: 0~0.003")]
    [Range(0f, 0.05f)] public float minBlurFloor = 0.002f;

    [Header("State (P4 WeaponBlurStateController 연동)")]
    [Tooltip("현재 블러 상태. 직접 설정하거나 WeaponBlurStateController가 자동 설정합니다.")]
    public BlurState currentState = BlurState.Idle;

    [Tooltip("락온 상태. LockOnBlurMediator(P3)가 설정합니다.")]
    public bool isLockedOn = false;

    [Tooltip("락온 중 공격 시 ShutterAngle 추가 배율.")]
    [Range(1f, 2f)] public float lockOnShutterMultiplier = 1.4f;

    [Header("Trailing Edge")]
    [Range(0f, 1f)] public float trailAttack = 0.35f;
    [Range(0f, 1f)] public float trailHeavyAttack = 0.50f;
    [Range(0f, 1f)] public float trailIdle = 0.00f;
    private float _smoothedGlobalTrail = 0f;

    [Header("Alpha Hysteresis")]
    [Range(0.05f, 0.5f)] public float hysteresisHigh = 0.15f;
    [Range(0.01f, 0.3f)] public float hysteresisLow = 0.05f;

    [Header("Velocity Smoothing")]
    [Range(0f, 0.98f)] public float smoothDecayIdle = 0.80f;
    [Range(0f, 0.98f)] public float smoothDecayStrafe = 0.88f;
    [Range(0.90f, 0.999f)] public float smoothDecayAttack = 0.96f;
    [Range(0.90f, 0.999f)] public float smoothDecayHeavy = 0.98f;

    // ═══════════════════════════════════════════════════════════════════════
    // [신규] ICameraEffectReceiver 연동 — BlurEventResponseSO 기반 Pulse 시스템
    //
    // 사용 방법:
    //   1. Create → TDA/Camera/Blur Event Response 로 SO 에셋 생성
    //   2. Inspector 의 blurEventResponse 슬롯에 드래그 연결
    //   3. SO 의 pulseDefinitions 리스트에서 이벤트 타입별 강도/시간 설정
    //
    // 동작 방식:
    //   CharacterEventManager → WorldCameraManager.BroadcastCameraEvent()
    //     → ICameraEffectReceiver.ReceiveCameraEffectEvent(eventType)
    //       → BlurEventResponseSO.GetPulse(eventType)
    //         → blurIntensity 를 Pulse 곡선으로 일시 증폭
    // ═══════════════════════════════════════════════════════════════════════
    [Header("ICameraEffectReceiver — Blur Event Response (SO-Driven)")]
    [Tooltip("이벤트 타입별 블러 Pulse 강도/시간을 정의하는 SO.\n" +
             "Create → TDA/Camera/Blur Event Response 로 생성하세요.\n" +
             "비워두면 ReceiveCameraEffectEvent() 가 블러 Pulse 없이 무시됩니다.")]
    [SerializeField] private BlurEventResponseSO blurEventResponse;

    [Header("Debug (ReadOnly) — Velocity & Stretch")]
    [SerializeField] private float _dbgRawSpeed = 0f;
    [SerializeField] private float _dbgSmoothedSpeed = 0f;
    [SerializeField] private float _dbgEffMult = 0f;
    [SerializeField] private float _dbgStretchEst = 0f;
    [SerializeField] private float _dbgExpTime = 0f;

    [Header("Debug (ReadOnly)")]
    [SerializeField] private bool _dbgWeightBufConnected;
    [SerializeField] private string _dbgRendererName = "None";
    [SerializeField] private string _dbgBlurState = "Idle";
    [SerializeField] private float _dbgShutterMult = 0.5f;
    [SerializeField] private float _dbgEffectiveMult = 0f;

    // ─────────────────────────────────────────────────────────────
    // 내부 변수
    // ─────────────────────────────────────────────────────────────
    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;
    private bool _isReady = false;
    private ComputeBuffer _weightBuffer;
    private ComputeBuffer _velocityBuffer;

    // ── Pulse 런타임 상태 ─────────────────────────────────────────
    // ReceiveCameraEffectEvent() 가 Pulse 를 트리거하면 LateUpdate 에서
    // 매 프레임 보간하여 blurIntensity 에 가산합니다.
    private float _pulseIntensity = 0f;   // 현재 Pulse 가산치 (0 ~ pulse.strength)
    private float _pulseTarget = 0f;   // 목표 강도
    private float _pulseBlendInRate = 0f;   // 상승 속도 (1/blendIn)
    private float _pulseDecayRate = 0f;   // 감쇠 속도 (1/decayTime)
    private float _pulseHoldTimer = 0f;   // 최대 강도 유지 카운트다운
    private bool _pulseDecaying = false;// 감쇠 중 여부

    private static readonly float[] StateShutterMultiplier =
    {
        0.0f,   // Idle
        0.8f,   // Strafe
        0.5f,   // Aim
        2.0f,   // Attack
        3.0f,   // HeavyAttack
    };

    // Shader Property IDs
    private static readonly int OMBShutterAngleID = Shader.PropertyToID("_ShutterAngle");
    private static readonly int OMBTargetFPSID = Shader.PropertyToID("_TargetFPS");
    private static readonly int OMBIntGlobalID = Shader.PropertyToID("_OMBIntensityGlobal");
    private static readonly int OMBVelocityID = Shader.PropertyToID("_OMBVelocityWS");
    private static readonly int OMBIntensityID = Shader.PropertyToID("_OMBIntensity");
    private static readonly int OMBMaxLengthID = Shader.PropertyToID("_OMBMaxLength");
    private static readonly int OMBMinBlurID = Shader.PropertyToID("_OMBMinBlur");
    private static readonly int OMBShutterMultID = Shader.PropertyToID("_OMBShutterMult");
    private static readonly int PropWeightBuf = Shader.PropertyToID("_BlurWeightBuffer");
    private static readonly int PropVelocityBuf = Shader.PropertyToID("_OMBVelocityBuffer");
    private static readonly int OMBHystHighID = Shader.PropertyToID("_OMBHysteresisHigh");
    private static readonly int OMBGlobalTrailID = Shader.PropertyToID("_OMBGlobalTrail");
    private static readonly int OMBFacingDirID = Shader.PropertyToID("_OMBFacingDir");
    private static readonly int OMBHystLowID = Shader.PropertyToID("_OMBHysteresisLow");

    private Vector3 _smoothedVelocity = Vector3.zero;
    private float _externalIntensityMult = 1.0f;
    private float _smoothedShutterMult = 0.5f;
    private Vector3 _prevPosition;
    private bool _hasPrevPos = false;

    // ─────────────────────────────────────────────────────────────
    // Unity 생명주기
    // ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
    }

    private void Start()
    {
        if (targetRenderer != null)
        {
            _renderer = targetRenderer;
        }
        else
        {
            _renderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (_renderer == null)
                _renderer = GetComponentInChildren<MeshRenderer>(true);
        }

        if (_renderer == null)
        {
            Debug.LogWarning(
                $"[ObjectMotionBlurController] '{name}': Renderer를 찾을 수 없습니다. " +
                "Target Renderer 슬롯에 직접 연결하세요.", this);
            return;
        }

        _isReady = true;
        _dbgRendererName = _renderer.name;
        Debug.Log($"[ObjectMotionBlurController] '{name}': Renderer 탐색 성공 → {_renderer.name}", this);

        _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;

        if (_weightBuffer != null)
        {
            _mpb.SetBuffer(PropWeightBuf, _weightBuffer);
            _renderer.SetPropertyBlock(_mpb);
        }
    }

    private void LateUpdate()
    {
        if (!_isReady || _renderer == null) return;

        // ── 루트 이동 벡터 계산 ───────────────────────────────────
        Vector3 currPosition = transform.position;
        Vector3 rawVelocity = _hasPrevPos ? (currPosition - _prevPosition) : Vector3.zero;
        _prevPosition = currPosition;
        _hasPrevPos = true;

        // ── velocity smoothing ───────────────────────────────────
        float decayFactor;
        switch (currentState)
        {
            case BlurState.HeavyAttack: decayFactor = smoothDecayHeavy; break;
            case BlurState.Attack: decayFactor = smoothDecayAttack; break;
            case BlurState.Aim: decayFactor = smoothDecayAttack; break;
            case BlurState.Strafe: decayFactor = smoothDecayStrafe; break;
            default: decayFactor = smoothDecayIdle; break;
        }
        if (rawVelocity.magnitude > 0.001f && _smoothedVelocity.magnitude < 0.001f)
            _smoothedVelocity = rawVelocity;
        else
            _smoothedVelocity = Vector3.Lerp(rawVelocity, _smoothedVelocity, decayFactor);
        Vector3 velocityWS = _smoothedVelocity;

        // ── ShutterMult 계산 ─────────────────────────────────────
        float shutterMult = StateShutterMultiplier[(int)currentState];
        if (debugForceIdleStretch && currentState == BlurState.Idle)
            shutterMult = 1.0f;
        if (isLockedOn && currentState >= BlurState.Attack)
            shutterMult *= lockOnShutterMultiplier;

        _smoothedShutterMult = Mathf.Lerp(shutterMult, _smoothedShutterMult, 0.92f);
        shutterMult = _smoothedShutterMult;

#if UNITY_EDITOR
        string prevState = _dbgBlurState;
        _dbgBlurState = currentState.ToString();
        _dbgShutterMult = StateShutterMultiplier[(int)currentState];
        _dbgEffectiveMult = shutterMult;
        if (prevState != _dbgBlurState && verboseLogging)
            Debug.Log($"<color=cyan>[P2_BlurCtrl]</color> BlurState: {prevState} → {_dbgBlurState} | Mult: {_dbgEffectiveMult:F2}×", this);
#endif

        // ══════════════════════════════════════════════════════════
        // [신규] Pulse 보간 처리
        // BlendIn → Hold → Decay 3단계로 blurIntensity 에 가산합니다.
        // ══════════════════════════════════════════════════════════
        float pulseAdd = TickPulse(Time.deltaTime);

        // ── MPB 구성 ─────────────────────────────────────────────
        _mpb.SetVector(OMBVelocityID, velocityWS);
        _mpb.SetFloat(OMBShutterAngleID, shutterAngle);
        _mpb.SetFloat(OMBTargetFPSID, targetFPS);
        _mpb.SetFloat(OMBIntGlobalID, globalIntensity);
        // pulseAdd 를 blurIntensity 에 더해서 셰이더로 전달
        _mpb.SetFloat(OMBIntensityID, (blurIntensity + pulseAdd) * _externalIntensityMult);
        _mpb.SetFloat(OMBHystHighID, hysteresisHigh);
        _mpb.SetFloat(OMBHystLowID, hysteresisLow);

        float targetTrail = currentState == BlurState.HeavyAttack ? trailHeavyAttack
                          : currentState == BlurState.Attack ? trailAttack
                          : trailIdle;
        _smoothedGlobalTrail = Mathf.Lerp(_smoothedGlobalTrail, targetTrail, Time.deltaTime * 8f);
        _mpb.SetFloat(OMBGlobalTrailID, _smoothedGlobalTrail);
        _mpb.SetVector(OMBFacingDirID, transform.forward);

        _dbgRawSpeed = rawVelocity.magnitude;
        _dbgSmoothedSpeed = _smoothedVelocity.magnitude;
        _dbgEffMult = shutterMult * (blurIntensity + pulseAdd) * _externalIntensityMult * globalIntensity;
        float expT = (shutterAngle / 360f) / Mathf.Max(targetFPS, 1f);
        _dbgExpTime = expT;
        _dbgStretchEst = _dbgSmoothedSpeed * targetFPS * expT * _dbgEffMult;

        _mpb.SetFloat(OMBMaxLengthID, maxBlurLength);
        _mpb.SetFloat(OMBMinBlurID, minBlurFloor);
        _mpb.SetFloat(OMBShutterMultID, shutterMult);

        if (_weightBuffer != null)
            _mpb.SetBuffer(PropWeightBuf, _weightBuffer);
        if (_velocityBuffer != null)
            _mpb.SetBuffer(PropVelocityBuf, _velocityBuffer);

        _renderer.SetPropertyBlock(_mpb);
        _dbgWeightBufConnected = (_weightBuffer != null);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ICameraEffectReceiver 구현
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AnimationEventType 을 수신하여 BlurEventResponseSO 에서 Pulse 수치를 조회합니다.
    ///
    /// 발행 경로:
    ///   CharacterEventManager.NotifyAnimationEvent(eventType)
    ///     → WorldCameraManager.BroadcastCameraEvent(eventType)
    ///       → ICameraEffectReceiver.ReceiveCameraEffectEvent(eventType)  ← 여기
    ///
    /// 또는 CharacterEffectsManager.HandleBlurStateTransition() 에서 직접 호출:
    ///   (blurController as ICameraEffectReceiver)?.ReceiveCameraEffectEvent(eventType)
    /// </summary>
    public void ReceiveCameraEffectEvent(AnimationEventType eventType)
    {
        if (blurEventResponse == null) return;
        if (!blurEventResponse.HasResponse(eventType)) return;

        var pulse = blurEventResponse.GetPulse(eventType);
        TriggerPulse(pulse.strength, pulse.blendIn, pulse.duration, pulse.decayTime);
    }

    /// <summary>
    /// CameraEffectOverlayData 의 blurStrengthDelta / blurDuration / blurDecayTime 을
    /// 직접 받아 Pulse 를 트리거합니다.
    ///
    /// 발행 경로:
    ///   WorldCameraManager.PlayOverlayEffect(overlayData)
    ///     → ICameraEffectReceiver.ReceiveOverlayEffect(overlayData)  ← 여기
    /// </summary>
    public void ReceiveOverlayEffect(CameraEffectOverlayData overlayData)
    {
        if (overlayData.blurStrengthDelta <= 0f) return;

        // CameraEffectOverlayData.blurDuration 은 BlendIn+Hold 합산으로 해석
        // blurDecayTime 은 감쇠 시간
        float blendIn = Mathf.Min(0.05f, overlayData.blurDuration * 0.3f);
        float hold = overlayData.blurDuration - blendIn;
        TriggerPulse(overlayData.blurStrengthDelta, blendIn, Mathf.Max(0f, hold), overlayData.blurDecayTime);
    }

    // ─────────────────────────────────────────────────────────────
    // Pulse 내부 구현
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Pulse 를 시작합니다. 기존 Pulse 가 진행 중이면 더 강한 값으로 override 합니다.
    /// </summary>
    private void TriggerPulse(float strength, float blendIn, float hold, float decayTime)
    {
        // 기존 Pulse 보다 약하면 무시 (연속 타격 시 강한 쪽 유지)
        if (strength < _pulseTarget * 0.5f) return;

        _pulseTarget = Mathf.Clamp01(strength);
        _pulseBlendInRate = blendIn > 0.001f ? _pulseTarget / blendIn : float.MaxValue;
        _pulseDecayRate = decayTime > 0.001f ? _pulseTarget / decayTime : float.MaxValue;
        _pulseHoldTimer = hold;
        _pulseDecaying = false;
    }

    /// <summary>
    /// 매 LateUpdate 에서 호출. Pulse 상태를 갱신하고 현재 가산치를 반환합니다.
    /// BlendIn → Hold → Decay 순서로 진행합니다.
    /// </summary>
    private float TickPulse(float dt)
    {
        if (_pulseTarget <= 0f && _pulseIntensity <= 0f) return 0f;

        if (!_pulseDecaying)
        {
            // ── BlendIn 단계: _pulseTarget 까지 상승 ─────────────
            if (_pulseIntensity < _pulseTarget)
            {
                _pulseIntensity = Mathf.MoveTowards(
                    _pulseIntensity, _pulseTarget, _pulseBlendInRate * dt);
            }
            else
            {
                // ── Hold 단계: 최대 강도 유지 ─────────────────────
                _pulseIntensity = _pulseTarget;
                _pulseHoldTimer -= dt;
                if (_pulseHoldTimer <= 0f)
                    _pulseDecaying = true;
            }
        }
        else
        {
            // ── Decay 단계: 0 까지 감쇠 ──────────────────────────
            _pulseIntensity = Mathf.MoveTowards(
                _pulseIntensity, 0f, _pulseDecayRate * dt);

            if (_pulseIntensity <= 0f)
            {
                _pulseIntensity = 0f;
                _pulseTarget = 0f;
                _pulseDecaying = false;
            }
        }

        return _pulseIntensity;
    }

    // ─────────────────────────────────────────────────────────────
    // 외부 API (기존 완전 보존)
    // ─────────────────────────────────────────────────────────────

    public void InjectVelocityBuffer(ComputeBuffer buffer)
    {
        _velocityBuffer = buffer;
        if (_isReady && _mpb != null && _velocityBuffer != null)
            _mpb.SetBuffer(PropVelocityBuf, _velocityBuffer);
    }

    public void InjectWeightBuffer(ComputeBuffer buffer)
    {
        if (buffer == null) return;
        _weightBuffer = buffer;

        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        _mpb.SetBuffer(PropWeightBuf, _weightBuffer);

        if (_renderer != null)
        {
            _renderer.SetPropertyBlock(_mpb);
            _isReady = true;
        }
    }

    public void SetExternalIntensityMult(float mult)
    {
        _externalIntensityMult = mult;
    }

    public void SetBlurState(BlurState state)
    {
        currentState = state;
    }

    public void SetLockOn(bool locked) { isLockedOn = locked; }

    public void SetTargetRenderer(Renderer r)
    {
        if (r == null) return;
        _renderer = r;
        _isReady = true;
        _dbgRendererName = r.name;
    }
}