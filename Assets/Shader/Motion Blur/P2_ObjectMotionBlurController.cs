using UnityEngine;

/// <summary>
/// [우선순위 2 — ObjectMotionBlurController]
///
/// ■ 이 파일이 하는 일
///   1. 이전 프레임 모델 행렬(_PrevObjectToWorld)을 매 LateUpdate마다 셰이더에 전달.
///   2. AvatarAutoWeightBaker로부터 WeightBuffer 레퍼런스를 받아 함께 MPB에 포함.
///   3. BlurState에 따라 ShutterAngle 배율을 자동 조정 (P4 WeaponBlurStateController 연동).
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
public class ObjectMotionBlurController : MonoBehaviour
{
    // ── 블러 상태 열거형 ──────────────────────────────────────────
    public enum BlurState
    {
        Idle,        // 정적 대기 — ShutterMult 0.5
        Strafe,      // 이동 중   — ShutterMult 1.0
        Aim,         // 조준/예비 — ShutterMult 0.67
        Attack,      // 공격      — ShutterMult 1.5
        HeavyAttack  // 강공격    — ShutterMult 2.0
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
    public bool debugForceIdleStretch = false;  // Idle에서도 stretch 강제 (테스트용)

    [Header("Shutter Settings")]
    [Tooltip("셔터 앵글 (도). 180°=표준, 270~360°=강한 블러.\n" +
             "ShaderCoordinationManager 미사용 시 이 값이 직접 적용됩니다.")]
    [Range(1f, 360f)] public float shutterAngle = 180f;

    [Tooltip("목표 FPS. 60 고정 권장.")]
    [Range(24f, 120f)] public float targetFPS = 60f;

    [Tooltip("전역 블러 강도 배율. 0이면 전체 블러 무효화됩니다. 기본 1.0.")]
    [Range(0f, 2f)] public float globalIntensity = 1f;

    [Header("Blur Settings")]
    [Tooltip("로컬 블러 강도 배율. (전역 강도 × 이 값)")]
    [Range(0f, 2f)] public float blurIntensity = 1f;

    [Tooltip("최대 블러 길이 (월드 단위). 너무 크면 메시가 찢어져 보입니다.")]
    [Range(0f, 0.5f)] public float maxBlurLength = 0.25f; // GVB 활성화 후 강한 스윙 커버 — 기존 0.04 → 0.25

    [Tooltip("정지 시에도 유지할 최소 블러. 0이면 팝인 발생 가능.\n" +
             "보스: 0.015  적: 0.005  플레이어: 0~0.003")]
    [Range(0f, 0.05f)] public float minBlurFloor = 0.002f;  // 팝인 최소화 — 기존 0.005 → 0.002

    [Header("State (P4 WeaponBlurStateController 연동)")]
    [Tooltip("현재 블러 상태. 직접 설정하거나 WeaponBlurStateController가 자동 설정합니다.")]
    public BlurState currentState = BlurState.Idle;

    [Tooltip("락온 상태. LockOnBlurMediator(P3)가 설정합니다.")]
    public bool isLockedOn = false;

    [Tooltip("락온 중 공격 시 ShutterAngle 추가 배율.")]
    [Range(1f, 2f)] public float lockOnShutterMultiplier = 1.4f;

    [Header("Alpha Hysteresis")]
    [Tooltip("반투명 시작 임계. 이 값 이상이어야 반투명 시작.\n낮출수록 작은 움직임에도 반투명. 기본 0.15.")]
    [Range(0.05f, 0.5f)] public float hysteresisHigh = 0.15f;

    [Tooltip("불투명 복귀 임계. 이 값 미만이어야 불투명 복귀.\n두 값 사이 구간에서는 상태 유지 → 깜빡임 차단.")]
    [Range(0.01f, 0.3f)] public float hysteresisLow  = 0.05f;

    [Header("Velocity Smoothing")]
    [Tooltip("이동/달리기 중 velocity 감쇠 계수. 0=즉시, 0.85=7프레임, 0.95=20프레임.")]
    [Range(0f, 0.98f)] public float smoothDecayIdle   = 0.80f;  // Idle 복귀 시

    [Tooltip("Attack 상태 진입 시 velocity 상승 속도 (빠를수록 즉각 반응).")]
    [Range(0f, 0.98f)] public float smoothDecayStrafe = 0.88f;  // Strafe 이동 중

    [Tooltip("공격/강공격 중 velocity 유지 계수. 높을수록 동작 내내 블러 유지됨.")]
    [Range(0.90f, 0.999f)] public float smoothDecayAttack = 0.96f;  // Attack/Aim 중

    [Tooltip("강공격 중 velocity 유지 계수. 0.98=약 50프레임 유지.")]
    [Range(0.90f, 0.999f)] public float smoothDecayHeavy = 0.98f;  // HeavyAttack 중

    [Header("Debug (ReadOnly) — Velocity & Stretch")]
    [Tooltip("현재 프레임 루트 이동 속도 m/frame. 0이면 정지.")]
    [SerializeField] private float _dbgRawSpeed      = 0f;
    [Tooltip("Smoothing 후 속도. smoothDecay 값에 따라 소멸 속도 다름.")]
    [SerializeField] private float _dbgSmoothedSpeed = 0f;
    [Tooltip("현재 ShutterMult × Intensity × GlobalIntensity. 0이면 stretch=0.")]
    [SerializeField] private float _dbgEffMult       = 0f;
    [Tooltip("예상 stretchLen (m). = smoothedSpeed × FPS × expTime × effMult.")]
    [SerializeField] private float _dbgStretchEst    = 0f;
    [Tooltip("현재 expTime = ShutterAngle/360/FPS.")]
    [SerializeField] private float _dbgExpTime       = 0f;

    [Header("Debug (ReadOnly)")]
    [SerializeField] private bool   _dbgWeightBufConnected;
    [SerializeField] private string _dbgRendererName   = "None";
    [SerializeField] private string _dbgBlurState      = "Idle";
    [SerializeField] private float  _dbgShutterMult    = 0.5f;
    [SerializeField] private float  _dbgEffectiveMult  = 0f;   // ShutterMult × LockOn 보정 최종값

    // ─────────────────────────────────────────────────────────────
    // 내부 변수
    // ─────────────────────────────────────────────────────────────
    private Renderer             _renderer;
    private MaterialPropertyBlock _mpb;


    private bool                 _isReady  = false;
    private ComputeBuffer        _weightBuffer;
    private ComputeBuffer        _velocityBuffer;

    private static readonly float[] StateShutterMultiplier =
    {
        0.0f,   // Idle        — 정지 시 블러 없음 (팝인 방지는 minBlurFloor 담당)
        0.8f,   // Strafe      — 이동 중 약한 잔상
        0.5f,   // Aim         — 예비동작 집중, 블러 감소
        2.0f,   // Attack      — 공격 강한 잔상 (기존 1.5 → 2.0)
        3.0f,   // HeavyAttack — 강공격 최대 잔상 (기존 2.0 → 3.0)
    };

    // Shader Property IDs
    private static readonly int OMBShutterAngleID = Shader.PropertyToID("_ShutterAngle");
    private static readonly int OMBTargetFPSID    = Shader.PropertyToID("_TargetFPS");
    private static readonly int OMBIntGlobalID    = Shader.PropertyToID("_OMBIntensityGlobal");
    private static readonly int OMBVelocityID     = Shader.PropertyToID("_OMBVelocityWS");
    private static readonly int OMBIntensityID   = Shader.PropertyToID("_OMBIntensity");
    private static readonly int OMBMaxLengthID   = Shader.PropertyToID("_OMBMaxLength");
    private static readonly int OMBMinBlurID     = Shader.PropertyToID("_OMBMinBlur");
    private static readonly int OMBShutterMultID = Shader.PropertyToID("_OMBShutterMult");
    private static readonly int PropWeightBuf    = Shader.PropertyToID("_BlurWeightBuffer");
    private static readonly int PropVelocityBuf    = Shader.PropertyToID("_OMBVelocityBuffer");
    private static readonly int OMBHystHighID      = Shader.PropertyToID("_OMBHysteresisHigh");
    private static readonly int OMBHystLowID       = Shader.PropertyToID("_OMBHysteresisLow");

    // [Fix-9 v2] Transparent 고정값 처리로 변경 — runtime 전환 불필요

    // 루트 위치 추적 (속도 계산용)
    private Vector3 _smoothedVelocity     = Vector3.zero;
    private float   _externalIntensityMult = 1.0f;  // P4가 주입하는 Attack 배율
    private float   _smoothedShutterMult = 0.5f;   // ShutterMult 보간 캐시 // velocity smoothing 캐시
    private Vector3 _prevPosition;
    private bool    _hasPrevPos = false;

    // ─────────────────────────────────────────────────────────────
    // Unity 생명주기
    // ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        // MPB는 Awake에서 반드시 초기화 — InjectWeightBuffer가 Awake 중 호출될 수 있음
        _mpb = new MaterialPropertyBlock();

        // Renderer 탐색은 Start로 위임 (자식 오브젝트가 아직 활성화 안 됐을 수 있음)
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

        // UNITY_PREV_MATRIX_M이 채워지려면 MotionVectorGenerationMode.Object 필요
        // ForceNoMotion이면 prev=curr이 되어 vel=0, 블러 없음
        _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;

        // Awake 중 InjectWeightBuffer가 이미 호출됐을 경우 지금 적용
        if (_weightBuffer != null)
        {
            _mpb.SetBuffer(PropWeightBuf, _weightBuffer);
            _renderer.SetPropertyBlock(_mpb);
        }
    }

    private void LateUpdate()
    {
        if (!_isReady || _renderer == null) return;

        // 루트 오브젝트의 이동 벡터 계산 (1프레임 월드 이동량)
        Vector3 currPosition = transform.position;
        Vector3 rawVelocity  = _hasPrevPos ? (currPosition - _prevPosition) : Vector3.zero;
        _prevPosition = currPosition;
        _hasPrevPos   = true;

        // ── velocity smoothing (BlurState별 decay 분리) ──────────────
        // 공격 중: decay 거의 없음 → 동작 내내 블러 유지
        // Idle: 빠르게 소멸
        // 비대칭 처리 제거 → 단순 Lerp 통일 (꿀렁 방지)
        float decayFactor;
        switch (currentState)
        {
            case BlurState.HeavyAttack: decayFactor = smoothDecayHeavy;  break;
            case BlurState.Attack:      decayFactor = smoothDecayAttack; break;
            case BlurState.Aim:         decayFactor = smoothDecayAttack; break;
            case BlurState.Strafe:      decayFactor = smoothDecayStrafe; break;
            default:                    decayFactor = smoothDecayIdle;   break;
        }
        // 단순 Lerp: rawVelocity 방향으로 수렴, decay 속도는 BlurState에 따라 결정
        // rawVelocity가 생기는 순간 smoothed를 즉시 동기화
        // → deadzone 경계를 한 번에 넘어 반복 깜빡임 방지
        if (rawVelocity.magnitude > 0.001f && _smoothedVelocity.magnitude < 0.001f)
            _smoothedVelocity = rawVelocity;
        else
            _smoothedVelocity = Vector3.Lerp(rawVelocity, _smoothedVelocity, decayFactor);
        Vector3 velocityWS = _smoothedVelocity;

        float shutterMult = StateShutterMultiplier[(int)currentState];
        // 디버그: Idle에서도 stretch 강제 (ShutterAngle/Intensity 변화 테스트용)
        if (debugForceIdleStretch && currentState == BlurState.Idle)
            shutterMult = 1.0f;
        if (isLockedOn && currentState >= BlurState.Attack)
            shutterMult *= lockOnShutterMultiplier;

        // [깜빡임 방지] ShutterMult를 Lerp로 보간 — 급격한 BlurState 전환 시 alpha 점프 방지
        // Lerp factor 높임: 0.75 → 0.92
        // 더 느린 전환 → BlurState 전환 시 stretchLen이 deadzone을 천천히 통과
        // → alpha가 1.0 → <1.0 경계를 빠르게 넘어가 깜빡임 최소화
        _smoothedShutterMult = Mathf.Lerp(shutterMult, _smoothedShutterMult, 0.92f);
        shutterMult = _smoothedShutterMult;

#if UNITY_EDITOR
        // 에디터 전용 디버그 (string alloc → 런타임 GC 방지)
        string prevState = _dbgBlurState;
        _dbgBlurState     = currentState.ToString();
        _dbgShutterMult   = StateShutterMultiplier[(int)currentState];
        _dbgEffectiveMult = shutterMult;
        if (prevState != _dbgBlurState)
            Debug.Log($"<color=cyan>[P2_BlurCtrl]</color> BlurState: {prevState} → {_dbgBlurState} | Mult: {_dbgEffectiveMult:F2}×", this);
#endif

        // MPB 구성
        _mpb.SetVector(OMBVelocityID,     velocityWS);
        _mpb.SetFloat(OMBShutterAngleID,  shutterAngle);
        _mpb.SetFloat(OMBTargetFPSID,     targetFPS);
        _mpb.SetFloat(OMBIntGlobalID,     globalIntensity);
        // P4가 공격 중 _externalIntensityMult를 주입해 블러 강도를 최대 10배까지 증폭
        _mpb.SetFloat(OMBIntensityID,     blurIntensity * _externalIntensityMult);
        _mpb.SetFloat(OMBHystHighID,      hysteresisHigh);
        _mpb.SetFloat(OMBHystLowID,       hysteresisLow);

        // ── 디버그 갱신 (velocity smoothing 시각화) ──────────────
        _dbgRawSpeed      = rawVelocity.magnitude;
        _dbgSmoothedSpeed = _smoothedVelocity.magnitude;
        _dbgEffMult       = shutterMult * blurIntensity * _externalIntensityMult * globalIntensity;
        float _dbgET      = (shutterAngle / 360f) / Mathf.Max(targetFPS, 1f);
        _dbgExpTime       = _dbgET;
        _dbgStretchEst    = _dbgSmoothedSpeed * targetFPS * _dbgET * _dbgEffMult;
        _mpb.SetFloat(OMBMaxLengthID,     maxBlurLength);
        _mpb.SetFloat(OMBMinBlurID,       minBlurFloor);
        _mpb.SetFloat(OMBShutterMultID,   shutterMult);

        if (_weightBuffer != null)
            _mpb.SetBuffer(PropWeightBuf, _weightBuffer);
        if (_velocityBuffer != null)
            _mpb.SetBuffer(PropVelocityBuf, _velocityBuffer);

        _renderer.SetPropertyBlock(_mpb);

        _dbgWeightBufConnected = (_weightBuffer != null);
    }

    // ── 외부 API ──────────────────────────────────────────────────

    /// <summary>
    /// AvatarAutoWeightBaker.Awake()에서 호출됩니다.
    /// WeightBuffer 레퍼런스를 보관하고 Renderer가 준비됐으면 즉시 MPB에 적용합니다.
    /// </summary>
    // OMBSkinningCacheManager.Start()에서 호출됩니다.
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

        // MPB가 아직 초기화 안 됐으면 지금 초기화
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        _mpb.SetBuffer(PropWeightBuf, _weightBuffer);

        // Renderer가 이미 준비됐으면 즉시 적용
        // 아직이면 Start()에서 처리됨
        if (_renderer != null)
        {
            _renderer.SetPropertyBlock(_mpb);
            _isReady = true;
        }
    }

    /// <summary>P4_WeaponBlurStateController가 Attack 중 배율을 주입합니다.</summary>
    public void SetExternalIntensityMult(float mult)
    {
        _externalIntensityMult = mult;
    }

    public void SetBlurState(BlurState state)
    {
        currentState = state;
        // [Fix-9 v2] Blend/ZWrite가 셰이더 고정값(Transparent)으로 처리됨
        // alpha 계산은 셰이더 vert에서 speed 기반으로 자동 처리
        // → runtime material 전환 불필요, ApplyTransparentMode 제거
    }

    public void SetLockOn(bool locked)         { isLockedOn = locked; }

    public void SetTargetRenderer(Renderer r)
    {
        if (r == null) return;
        _renderer    = r;
        _isReady     = true;
        _dbgRendererName = r.name;
    }
}
