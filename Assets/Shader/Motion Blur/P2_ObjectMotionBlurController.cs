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

    [Header("Blur Settings")]
    [Tooltip("로컬 블러 강도 배율. (전역 강도 × 이 값)")]
    [Range(0f, 2f)] public float blurIntensity = 1f;

    [Tooltip("최대 블러 길이 (월드 단위). 너무 크면 메시가 찢어져 보입니다.")]
    [Range(0f, 0.1f)] public float maxBlurLength = 0.04f;

    [Tooltip("정지 시에도 유지할 최소 블러. 0이면 팝인 발생 가능.\n" +
             "보스: 0.015  적: 0.005  플레이어: 0~0.003")]
    [Range(0f, 0.05f)] public float minBlurFloor = 0.005f;

    [Header("State (P4 WeaponBlurStateController 연동)")]
    [Tooltip("현재 블러 상태. 직접 설정하거나 WeaponBlurStateController가 자동 설정합니다.")]
    public BlurState currentState = BlurState.Idle;

    [Tooltip("락온 상태. LockOnBlurMediator(P3)가 설정합니다.")]
    public bool isLockedOn = false;

    [Tooltip("락온 중 공격 시 ShutterAngle 추가 배율.")]
    [Range(1f, 2f)] public float lockOnShutterMultiplier = 1.4f;

    [Header("Debug (ReadOnly)")]
    [SerializeField] private bool   _dbgWeightBufConnected;
    [SerializeField] private string _dbgRendererName = "None";

    // ─────────────────────────────────────────────────────────────
    // 내부 변수
    // ─────────────────────────────────────────────────────────────
    private Renderer             _renderer;
    private MaterialPropertyBlock _mpb;
    private Matrix4x4            _prevModelMatrix;
    private bool                 _hasPrev  = false;
    private bool                 _isReady  = false;
    private ComputeBuffer        _weightBuffer;   // AvatarAutoWeightBaker 주입

    private static readonly float[] StateShutterMultiplier =
    {
        0.5f,   // Idle
        1.0f,   // Strafe
        0.67f,  // Aim
        1.5f,   // Attack
        2.0f,   // HeavyAttack
    };

    // Shader Property IDs
    private static readonly int PrevOTWID        = Shader.PropertyToID("_PrevObjectToWorld");
    private static readonly int OMBIntensityID   = Shader.PropertyToID("_OMBIntensity");
    private static readonly int OMBMaxLengthID   = Shader.PropertyToID("_OMBMaxLength");
    private static readonly int OMBMinBlurID     = Shader.PropertyToID("_OMBMinBlur");
    private static readonly int OMBShutterMultID = Shader.PropertyToID("_OMBShutterMult");
    private static readonly int PropWeightBuf    = Shader.PropertyToID("_BlurWeightBuffer");

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

        // Awake 중 InjectWeightBuffer가 이미 호출됐을 경우
        // Renderer가 없어서 SetPropertyBlock을 못했으므로 지금 적용
        if (_weightBuffer != null)
        {
            _mpb.SetBuffer(PropWeightBuf, _weightBuffer);
            _renderer.SetPropertyBlock(_mpb);
        }
    }

    private void LateUpdate()
    {
        if (!_isReady || _renderer == null) return;

        Matrix4x4 currMatrix = _renderer.localToWorldMatrix;

        // 첫 프레임: prev=curr 초기화 → vel=0 → 블러 없음 (자연스러운 시작)
        if (!_hasPrev)
        {
            _prevModelMatrix = currMatrix;
            _hasPrev = true;
            // 첫 프레임은 행렬만 초기화하고 MPB 설정은 다음 프레임부터
            // (WeightBuffer가 아직 주입 안 됐을 수 있으므로)
            return;
        }

        float shutterMult = StateShutterMultiplier[(int)currentState];
        if (isLockedOn && currentState >= BlurState.Attack)
            shutterMult *= lockOnShutterMultiplier;

        // ── MPB 직접 구성 ─────────────────────────────────────────
        // GetPropertyBlock을 사용하지 않는 이유:
        //   GetPropertyBlock은 ComputeBuffer를 복사하지 않습니다.
        //   따라서 _weightBuffer를 이 클래스가 보관하고 매 프레임 직접 포함합니다.
        _mpb.SetMatrix(PrevOTWID,       _prevModelMatrix);
        _mpb.SetFloat(OMBIntensityID,   blurIntensity);
        _mpb.SetFloat(OMBMaxLengthID,   maxBlurLength);
        _mpb.SetFloat(OMBMinBlurID,     minBlurFloor);
        _mpb.SetFloat(OMBShutterMultID, shutterMult);

        // WeightBuffer: null이어도 MPB 적용 (폴백값이 셰이더 Properties에 있음)
        if (_weightBuffer != null)
            _mpb.SetBuffer(PropWeightBuf, _weightBuffer);

        _renderer.SetPropertyBlock(_mpb);
        _prevModelMatrix = currMatrix;

        // 디버그 Inspector 갱신
        _dbgWeightBufConnected = (_weightBuffer != null);
    }

    // ── 외부 API ──────────────────────────────────────────────────

    /// <summary>
    /// AvatarAutoWeightBaker.Awake()에서 호출됩니다.
    /// WeightBuffer 레퍼런스를 보관하고 Renderer가 준비됐으면 즉시 MPB에 적용합니다.
    /// </summary>
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

    public void SetBlurState(BlurState state) { currentState = state; }
    public void SetLockOn(bool locked)         { isLockedOn = locked; }

    public void SetTargetRenderer(Renderer r)
    {
        if (r == null) return;
        _renderer    = r;
        _isReady     = true;
        _dbgRendererName = r.name;
    }
}
