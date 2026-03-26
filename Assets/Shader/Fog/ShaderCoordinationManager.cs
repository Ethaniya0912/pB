using UnityEngine;
using TDA.Character.Player;
using TDA.Character; // CharacterManager (락온 타겟 접근)

/// <summary>
/// [Global Manager — Dreamcore v2.0]
/// 모든 셰이더의 전역 변수(Global Variables)를 총괄 관리하는 방송국 역할.
/// 기존 기능(LUT/Fog/Speed) + 신규 모션 블러 인프라(VP 행렬, 셔터앵글)를 통합합니다.
///
/// [Phase 0 추가 사항]
/// - Header "5. Motion Blur Infrastructure" 블록 추가
/// - LateUpdate() 에서 VP 행렬 델타 계산 및 전역 주입
/// - _ShutterAngle, _TargetFPS 전역 파라미터 주입
/// - SyncGlobalVariables()는 기존 Update()에서만 호출 (에디터 OnValidate 포함)
/// - VP 행렬은 LateUpdate()에서 별도 처리 (카메라 이동 완료 후 캡처 보장)
/// </summary>
[ExecuteInEditMode]
public class ShaderCoordinationManager : MonoBehaviour
{
    public static ShaderCoordinationManager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────
    // [기존] Header 1 ~ 4  (수정 없음)
    // ─────────────────────────────────────────────────────────────
    [Header("1. Aesthetic Coordination (LUT & Lighting)")]
    [Tooltip("256x1 포인트 필터링된 LUT 텍스처입니다.")]
    public Texture2D globalRampTexture;
    [Tooltip("화면의 색조 왜곡 및 광기 효과 강도입니다.")]
    [Range(0f, 1f)] public float globalMadness = 0f;
    [Tooltip("Ramp 텍스처의 감마 보정값입니다.")]
    [Range(0.1f, 5f)] public float globalRampGamma = 0.8f;
    [Tooltip("색상 양자화(포스터라이즈) 단계 수입니다.")]
    [Range(1f, 128f)] public float globalQuantizeSteps = 8f;

    [Header("2. Atmospheric Coordination (Fog & Rays)")]
    [Tooltip("대기 중 안개 농도입니다.")]
    [Range(0f, 0.5f)] public float fogDensity = 0.08f;
    [Tooltip("라이트 샤프트 계산을 위한 샘플링 단계 수입니다.")]
    [Range(12, 128)] public int raySteps = 64;
    [Tooltip("안개 및 가시거리의 최대 한계선입니다.")]
    public float maxDistance = 100f;
    [Tooltip("빛의 계단 현상(Quantization) 강도입니다.")]
    [Range(1f, 8f)] public float lightQuantization = 4.0f;
    [Tooltip("안개의 기본 환경색입니다.")]
    [ColorUsage(false, true)]
    public Color fogAmbientColor = new Color(0.05f, 0.05f, 0.1f, 1.0f);
    [Tooltip("디더링 및 노이즈 연산을 위한 블루 노이즈 텍스처입니다.")]
    public Texture2D blueNoise;

    [Header("3. Visual Paradox Coordination (Edge & Bleed)")]
    [Tooltip("블룸 효과가 시작될 밝기 임계점입니다.")]
    [Range(0f, 1f)] public float bloomThreshold = 0.7f;
    [Tooltip("외곽선 및 경계면 보호 강도입니다.")]
    [Range(0.1f, 10f)] public float edgeProtectionIntensity = 5.0f;
    [Tooltip("색번짐(Bleed) 효과에 사용될 강조 색상입니다.")]
    public Color bleedColor = new Color(1, 0.8f, 0.6f, 1);

    [Header("4. Speed Feedback State (Runtime Only)")]
    [SerializeField, Tooltip("현재 플레이어의 속도 비율 (0~1)")]
    private float currentSpeedFactor;
    [SerializeField, Tooltip("현재 플레이어의 물리적 호흡/진동 값")]
    private float currentMovementPulse;

    // ─────────────────────────────────────────────────────────────
    // [신규] Header 5 — Motion Blur Infrastructure (Phase 0)
    // ─────────────────────────────────────────────────────────────
    [Header("5. Motion Blur Infrastructure (Phase 0)")]

    [Tooltip(
        "셔터앵글 (도 단위). 영화적 표준 = 180. 빠른 게임감 = 90~120.\n" +
        "공식: 노출시간 = ShutterAngle / 360 / FPS\n" +
        "이 값이 커질수록 블러가 강해지고, 작아질수록 선명해집니다.")]
    [Range(45f, 360f)]
    public float shutterAngle = 180f;

    [Tooltip(
        "스크린스페이스 모션 블러 마스터 강도 (0=꺼짐, 1=풀 강도).\n" +
        "셔터앵글이 물리적 길이를 결정하고, 이 값은 그 위의 아티스트 조절 스케일입니다.")]
    [Range(0f, 2f)]
    public float ssMotionBlurIntensity = 1f;

    [Tooltip(
        "오브젝트 모션 블러 마스터 강도 (0=꺼짐, 1=풀 강도).\n" +
        "개별 오브젝트 ObjectMotionBlurController.cs에도 로컬 강도가 있습니다.")]
    [Range(0f, 2f)]
    public float objectMotionBlurIntensity = 1f;

    [Tooltip(
        "스크린스페이스 블러 깊이 컷오프 (미터 단위).\n" +
        "이 거리보다 가까운 픽셀은 스크린스페이스 블러에서 제외됩니다 (오브젝트 블러가 담당).\n" +
        "권장값: 2~5. 너무 크면 배경 블러가 약해집니다.")]
    [Range(0.5f, 10f)]
    public float ssBlurDepthCutoff = 2.5f;

    // ─────────────────────────────────────────────────────────────
    // [기존] Shader Property IDs (수정 없음)
    // ─────────────────────────────────────────────────────────────
    private static readonly int RampTexID = Shader.PropertyToID("_GlobalRampTex");
    private static readonly int MadnessID = Shader.PropertyToID("_GlobalMadness");
    private static readonly int GammaID = Shader.PropertyToID("_GlobalRampGamma");
    private static readonly int StepsID = Shader.PropertyToID("_GlobalSteps");
    private static readonly int FogDensityID = Shader.PropertyToID("_FogDensity");
    private static readonly int StepCountID = Shader.PropertyToID("_StepCount");
    private static readonly int MaxDistID = Shader.PropertyToID("_MaxDist");
    private static readonly int QuantizationID = Shader.PropertyToID("_Quantization");
    private static readonly int AmbientID = Shader.PropertyToID("_GlobalAmbientColor");
    private static readonly int NoiseTexID = Shader.PropertyToID("_NoiseTex");
    private static readonly int ThresholdID = Shader.PropertyToID("_Threshold");
    private static readonly int EdgeStrengthID = Shader.PropertyToID("_EdgeStrength");
    private static readonly int BleedColorID = Shader.PropertyToID("_BleedColor");
    private static readonly int SpeedFactorID = Shader.PropertyToID("_GlobalSpeedFactor");
    private static readonly int MovementPulseID = Shader.PropertyToID("_GlobalMovementPulse");
    private static readonly int MipBiasID = Shader.PropertyToID("_VFXMipBias");

    // ─────────────────────────────────────────────────────────────
    // [신규] Motion Blur Shader Property IDs (Phase 0)
    //
    // 이름을 상수로 관리하는 이유:
    //   셰이더와 C# 양쪽에서 동일한 문자열을 사용해야 하는데,
    //   PropertyToID()는 해시 조회이므로 런타임 비용이 0에 가깝습니다.
    //   문자열 오타로 인한 버그를 컴파일 단계에서 막지는 못하지만,
    //   한 곳에서만 수정하면 전체에 반영되므로 유지보수가 용이합니다.
    // ─────────────────────────────────────────────────────────────
    private static readonly int ShutterAngleID = Shader.PropertyToID("_ShutterAngle");
    private static readonly int TargetFPSID = Shader.PropertyToID("_TargetFPS");
    private static readonly int PrevVPMatrixID = Shader.PropertyToID("_PrevVPMatrix");
    private static readonly int CurrVPMatrixID = Shader.PropertyToID("_CurrVPMatrix");
    private static readonly int SSMBIntensityID = Shader.PropertyToID("_SSMBIntensity");
    private static readonly int OMBIntensityGlobalID = Shader.PropertyToID("_OMBIntensityGlobal");
    private static readonly int SSBlurDepthCutoffID = Shader.PropertyToID("_SSBlurDepthCutoff");

    // ─────────────────────────────────────────────────────────────
    // VP 행렬 캐시 (LateUpdate에서 관리)
    // ─────────────────────────────────────────────────────────────
    private Matrix4x4 _prevVPMatrix;
    private bool _hasPrevVP = false;

    // ─────────────────────────────────────────────────────────────
    // Unity 생명주기
    // ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            DestroyImmediate(gameObject);
        }

        // Rigidbody/CharacterController 자동 탐색
        // 우선순위: 1) Inspector 직접 연결 → 2) "Player" 태그 → 3) "Player" 레이어
        // 씬 시작 시 플레이어가 없으면 경고만 출력 — Update에서 재탐색
        if (playerRigidbody == null && playerCharacterController == null)
        {
            TryFindPlayer();
        }
    }

    // 기존 기능: Update에서 LUT/Fog 등 정적 파라미터 동기화
    private void Update()
    {
        // 플레이어가 아직 없으면 매 초마다 재탐색 (런타임 스폰 대응)
        if (playerRigidbody == null && playerCharacterController == null)
        {
            _playerSearchTimer -= Time.deltaTime;
            if (_playerSearchTimer <= 0f)
            {
                TryFindPlayer();
                _playerSearchTimer = 1f; // 1초마다 재탐색
            }
        }

        PollLockOnAnimator();
        SyncGlobalVariables();
        UpdateLockOnBlur();
    }

    private float _playerSearchTimer = 0f;

    /// <summary>
    /// 씬에서 플레이어를 탐색합니다.
    /// 탐색 우선순위: Tag "Player" → Layer "Player" + Rigidbody
    /// Awake와 Update(1초마다)에서 호출됩니다.
    /// </summary>
    private void TryFindPlayer()
    {
        GameObject playerObj = null;

        // Layer 기반 탐색 (Tag 미사용 — Inspector에서 레이어 이름 설정)
        int playerLayer = LayerMask.NameToLayer(playerLayerName);
        if (playerLayer >= 0)
        {
            foreach (var go in FindObjectsByType<GameObject>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (go.layer == playerLayer &&
                    (go.GetComponent<Rigidbody>() != null ||
                     go.GetComponent<CharacterController>() != null))
                {
                    playerObj = go;
                    break;
                }
            }
        }
        else
        {
            Debug.LogWarning($"[SCM] '{playerLayerName}' 레이어를 찾을 수 없습니다. " +
                             "Inspector의 Player Layer Name을 확인하세요.");
        }

        if (playerObj != null)
        {
            // 멀티플레이어: IsOwner인 로컬 플레이어만 추적
            // IsOwner가 아닌 플레이어를 추적하면 다른 클라이언트의 이동이 SSMB에 반영됨
            var netObj = playerObj.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null && !netObj.IsOwner)
            {
                // 씬의 모든 NetworkObject 중 IsOwner인 것 탐색
                foreach (var no in FindObjectsByType<Unity.Netcode.NetworkObject>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (no.IsOwner && no.GetComponent<Rigidbody>() != null)
                    {
                        playerObj = no.gameObject;
                        break;
                    }
                }
            }

            playerRigidbody           = playerObj.GetComponent<Rigidbody>();
            playerCharacterController = playerObj.GetComponent<CharacterController>();

            // PlayerNetworkManager 자동 탐색 (락온 직접 읽기용)
            if (playerNetworkManager == null)
            {
                // TDA.Character.Player 네임스페이스의 PlayerNetworkManager
                var pnm = playerObj.GetComponent<TDA.Character.Player.PlayerNetworkManager>();
                if (pnm != null)
                {
                    playerNetworkManager = pnm;
                    _animSearched = false; // 재탐색 허용
                }
            }

            _dbgPlayerName = playerObj.name;
            Debug.Log($"[SCM] Player 탐색 성공: {playerObj.name}" +
                      $" | PNM: {(playerNetworkManager != null ? "연결됨" : "없음")}");
        }
        // 탐색 실패 시 조용히 넘어감 — 다음 Update 주기에 재시도
    }

    // 신규: LateUpdate에서 VP 행렬 처리
    // LateUpdate를 사용하는 이유:
    //   카메라가 Update()에서 이동을 완료한 후 행렬을 캡처해야
    //   이전-현재 프레임 델타가 정확합니다.
    //   Update() 순서 의존성 없이 항상 "이 프레임의 최종 카메라 위치"를 보장합니다.
    private void LateUpdate()
    {
        _speedCached = false; // 매 프레임 속도 캐시 초기화
        SyncMotionBlurInfrastructure();
    }

    // 에디터에서 Inspector 값 변경 시 즉시 반영
    private void OnValidate() => SyncGlobalVariables();

    // ─────────────────────────────────────────────────────────────
    // 기존 Public API (변경 없음)
    // ─────────────────────────────────────────────────────────────
    public void UpdatePlayerSpeedFeedback(float speedFactor, float pulse)
    {
        currentSpeedFactor = speedFactor;
        currentMovementPulse = pulse;
    }

    // ─────────────────────────────────────────────────────────────
    // [기존] SyncGlobalVariables (수정 없음 — 건드리지 마세요)
    // ─────────────────────────────────────────────────────────────
    private void SyncGlobalVariables()
    {
        if (globalRampTexture != null)
        {
            Shader.SetGlobalTexture(RampTexID, globalRampTexture);
            Shader.SetGlobalFloat(MadnessID, globalMadness);
            Shader.SetGlobalFloat(GammaID, globalRampGamma);
            Shader.SetGlobalFloat(StepsID, globalQuantizeSteps);
        }

        Shader.SetGlobalFloat(FogDensityID, fogDensity);
        Shader.SetGlobalFloat(StepCountID, (float)raySteps);
        Shader.SetGlobalFloat(MaxDistID, maxDistance);
        Shader.SetGlobalFloat(QuantizationID, lightQuantization);
        Shader.SetGlobalColor(AmbientID, fogAmbientColor);

        if (blueNoise != null)
            Shader.SetGlobalTexture(NoiseTexID, blueNoise);

        Shader.SetGlobalFloat(ThresholdID, bloomThreshold);
        Shader.SetGlobalFloat(EdgeStrengthID, edgeProtectionIntensity);
        Shader.SetGlobalColor(BleedColorID, bleedColor);

        Shader.SetGlobalFloat(SpeedFactorID, currentSpeedFactor);
        Shader.SetGlobalFloat(MovementPulseID, currentMovementPulse);
        Shader.SetGlobalFloat(MipBiasID, Mathf.Lerp(0f, -2.0f, currentSpeedFactor));

        // [Phase 0 추가] 정적 모션 블러 파라미터 (변하지 않는 값)
        Shader.SetGlobalFloat(ShutterAngleID, shutterAngle);
        Shader.SetGlobalFloat(SSMBIntensityID, ssMotionBlurIntensity);
        Shader.SetGlobalFloat(OMBIntensityGlobalID, objectMotionBlurIntensity);
        Shader.SetGlobalFloat(SSBlurDepthCutoffID, ssBlurDepthCutoff);
    }

    // ─────────────────────────────────────────────────────────────
    // [신규] SyncMotionBlurInfrastructure — LateUpdate 전용
    //
    // 설계 근거:
    //   VP 행렬(View-Projection Matrix)은 카메라의 월드 위치·회전·FOV를 하나의
    //   행렬로 인코딩합니다. 현재 프레임과 이전 프레임의 VP 행렬 차이를 셰이더에
    //   전달하면, 각 픽셀이 화면상에서 얼마나 이동했는지(모션 벡터)를 GPU에서
    //   직접 역산할 수 있습니다. 이것이 URP 내장 _MotionVectorTexture와 같은
    //   원리이며, 추가적인 뎁스 샘플 없이 카메라 이동 성분만 순수 추출하는
    //   가장 정확한 방법입니다.
    //
    //   _TargetFPS를 매 프레임 실측 deltaTime으로 계산하는 이유:
    //   Application.targetFrameRate는 목표값이고 실제 프레임 레이트가 아닙니다.
    //   셔터앵글 공식(노출시간 = ShutterAngle/360/FPS)에서 FPS가 부정확하면
    //   블러 길이가 의도와 다르게 나옵니다. 1/deltaTime이 실제 FPS에 가장 근접합니다.
    // ─────────────────────────────────────────────────────────────
    private void SyncMotionBlurInfrastructure()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // 현재 프레임의 View-Projection 행렬 계산
        // GL.GetGPUProjectionMatrix: OpenGL/Vulkan/Metal 등 플랫폼별
        // NDC 차이를 자동 보정해주는 URP 권장 방식
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
        Matrix4x4 view = cam.worldToCameraMatrix;
        Matrix4x4 currVP = proj * view;

        // 첫 프레임은 이전 행렬이 없으므로 현재 행렬로 초기화 (블러 없음)
        if (!_hasPrevVP)
        {
            _prevVPMatrix = currVP;
            _hasPrevVP = true;
        }

        Shader.SetGlobalMatrix(PrevVPMatrixID, _prevVPMatrix);
        Shader.SetGlobalMatrix(CurrVPMatrixID, currVP);

        // 실측 FPS (deltaTime 기반) — 60fps 캡 적용으로 첫 프레임 폭주 방지
        float safeDeltaTime = Mathf.Max(Time.deltaTime, 1f / 200f);
        Shader.SetGlobalFloat(TargetFPSID, 1f / safeDeltaTime);

        // 다음 프레임을 위해 현재 행렬 저장
        _prevVPMatrix = currVP;
    }

    // ─────────────────────────────────────────────────────────────
    // [Phase 3 추가] Lock-On Blur 인프라
    // ─────────────────────────────────────────────────────────────
    [Header("6. Lock-On Blur (Phase 3)")]
    [Range(0f, 1f)] public float lockOnSSMBScale = 0.25f;
    [Range(0.1f, 1f)] public float lockOnFadeIn = 0.4f;
    [Range(0.1f, 1f)] public float lockOnFadeOut = 0.5f;
    [Range(0f, 1f)] public float lockOnMoveSSMBScale = 0.7f;
    [Range(0.01f, 0.3f)] public float moveFadeIn = 0.08f;
    [Range(0.1f, 0.5f)] public float moveStopFadeOut = 0.2f;
    [Range(0.1f, 3f)] public float moveSpeedThreshold = 0.5f;

    [Tooltip("플레이어 오브젝트가 속한 레이어 이름. Tag 대신 레이어로 탐색합니다.")]
    public string playerLayerName = "Player";

    [Tooltip("비워두면 Start()에서 씬의 Rigidbody/CharacterController를 자동 탐색합니다.")]
    public Rigidbody playerRigidbody;
    public CharacterController playerCharacterController;

    [Header("6. Lock-On Debug (ReadOnly — Inspector에서 확인)")]
    [SerializeField] private bool _dbgIsLockedOn;
    [SerializeField] private string _dbgLockOnSource = "None";  // 어떤 경로로 락온 감지했는지
    [SerializeField] private float _dbgCurrentSSMBScale = 1f;
    [SerializeField] private float _dbgPlayerSpeed;
    [SerializeField] private string _dbgPlayerName     = "None";
    [SerializeField] private string _dbgLockOnTargetName = "None"; // 현재 락온 타겟 이름

    private bool _isLockedOn = false;
    private bool _isMoving = false;
    private float _currentSSMBScale = 1f;
    private float _targetSSMBScale = 1f;
    private float _ssmbLerpSpeed = 1f;

    private static readonly int SSMBScaleID      = Shader.PropertyToID("_SSMBIntensityScale");
    // DepthBlur 보호 반경 — 락온 시 NearDist를 lockOnGizmoRadius로 설정
    private static readonly int NearDistID              = Shader.PropertyToID("_NearDist");
    private static readonly int LockOnActiveID          = Shader.PropertyToID("_LockOnActive");
    private static readonly int LockOnPlayerPosID       = Shader.PropertyToID("_LockOnPlayerPosWS");
    private static readonly int LockOnTargetPosID       = Shader.PropertyToID("_LockOnTargetPosWS");
    private static readonly int PlayerProtectRadiusID   = Shader.PropertyToID("_PlayerProtectRadius");
    private static readonly int TargetProtectRadiusID   = Shader.PropertyToID("_TargetProtectRadius");

    [Header("Lock-On Source")]
    [Tooltip("직접 연결: PlayerNetworkManager (isLockedOn.Value 직접 읽기)\n" +
             "비워두면 Animator 파라미터 폴백 사용.")]
    public PlayerNetworkManager playerNetworkManager;

    [Tooltip("Animator 폴백용 파라미터 이름. playerNetworkManager 미연결 시 사용.")]
    public string lockOnAnimParamName = "isLockedOn";

    // Animator 폴백 관련
    public Animator playerAnimator;
    private bool _animSearched   = false;
    private bool _hasLockOnParam = false;

    // 락온 시스템에서 호출
    /// <summary>
    /// 외부(PlayerCombatManager 등)에서 락온 타겟 Transform을 주입합니다.
    /// 락온 시 이 타겟 주변 targetProtectRadius 이내는 DepthBlur에서 제외됩니다.
    /// </summary>
    public void SetLockOnTarget(Transform target) { lockOnTarget = target; }

    public void SetLockOn(bool locked)
    {
        _isLockedOn = locked;
        _isMoving = false;
        _targetSSMBScale = locked ? lockOnSSMBScale : 1f;
        _ssmbLerpSpeed = 1f / (locked ? lockOnFadeIn : lockOnFadeOut);
    }

    private void UpdateLockOnBlur()
    {
        if (_isLockedOn)
        {
            float speed = GetPlayerSpeed();
            bool moving = speed > moveSpeedThreshold;

            if (moving && !_isMoving)
            {
                _targetSSMBScale = lockOnMoveSSMBScale;
                _ssmbLerpSpeed = 1f / moveFadeIn;
                _isMoving = true;
            }
            else if (!moving && _isMoving)
            {
                _targetSSMBScale = lockOnSSMBScale;
                _ssmbLerpSpeed = 1f / moveStopFadeOut;
                _isMoving = false;
            }
        }

        _currentSSMBScale = Mathf.MoveTowards(
            _currentSSMBScale, _targetSSMBScale,
            _ssmbLerpSpeed * Time.deltaTime);

        Shader.SetGlobalFloat(SSMBScaleID, _currentSSMBScale);

        // 락온 시 DepthBlur 보호 반경 주입
        // lockOnGizmoRadius 이내 = NearDist로 설정 → 거리 블러 없음
        // 락온 해제 시 원래 NearDist(7.0)로 복귀
        if (_isLockedOn)
        {
            // 플레이어 보호 반경 → NearDist
            Shader.SetGlobalFloat(NearDistID,            playerProtectRadius);
            Shader.SetGlobalFloat(LockOnActiveID,        1f);
            Shader.SetGlobalFloat(PlayerProtectRadiusID, playerProtectRadius);
            Shader.SetGlobalFloat(TargetProtectRadiusID, targetProtectRadius);

            // 플레이어 월드 위치
            Vector3 playerPos = playerRigidbody != null
                ? playerRigidbody.transform.position
                : (playerCharacterController != null
                    ? playerCharacterController.transform.position
                    : Vector3.zero);
            Shader.SetGlobalVector(LockOnPlayerPosID, playerPos);

            // 타겟 월드 위치
            Vector3 targetPos = lockOnTarget != null ? lockOnTarget.position : playerPos;
            Shader.SetGlobalVector(LockOnTargetPosID, targetPos);
        }
        else
        {
            Shader.SetGlobalFloat(NearDistID,     defaultNearDist);
            Shader.SetGlobalFloat(LockOnActiveID, 0f);
        }

        // ── Inspector 디버그 값 동기화 ──────────────────────────
        _dbgIsLockedOn       = _isLockedOn;
        _dbgCurrentSSMBScale = _currentSSMBScale;
        _dbgPlayerSpeed      = GetPlayerSpeed();
    }

    private Vector3 _prevPlayerPos;
    private bool    _hasPrevPlayerPos = false;

    private float _cachedSpeed   = 0f;
    private bool  _speedCached   = false;
    private Vector3 _prevPlrPos;
    private bool    _hasPrevPlrPos = false;

    private float GetPlayerSpeed()
    {
        if (_speedCached) return _cachedSpeed;
        _speedCached = true;

        if (playerRigidbody != null)
        {
            float rbSpeed = playerRigidbody.linearVelocity.magnitude;
            if (rbSpeed > 0.001f) { _cachedSpeed = rbSpeed; return rbSpeed; }
        }

        // 2. CharacterController velocity
        if (playerCharacterController != null)
        {
            float ccSpeed = playerCharacterController.velocity.magnitude;
            if (ccSpeed > 0.001f) return ccSpeed;
        }

        // 3. Transform 위치 델타 폴백
        // Kinematic Rigidbody / 커스텀 이동 방식 모두 대응
        Transform playerTransform = playerCharacterController != null
            ? playerCharacterController.transform
            : (playerRigidbody != null ? playerRigidbody.transform : null);

        if (playerTransform != null)
        {
            Vector3 cur = playerTransform.position;
            if (_hasPrevPlrPos)
            {
                float spd = Mathf.Min(
                    Vector3.Distance(cur, _prevPlrPos) / Mathf.Max(Time.deltaTime, 0.01f),
                    50f);
                _prevPlrPos  = cur;
                _cachedSpeed = spd;
                return spd;
            }
            _prevPlrPos    = cur;
            _hasPrevPlrPos = true;
        }

        _cachedSpeed = 0f;
        return 0f;
    }

    private void PollLockOnAnimator()
    {
        // ── Lock-On Target 자동 탐색 ─────────────────────────────
        // PlayerCombatManager.currentTarget은 부모클래스(CharacterCombatManager)에 있음
        // Reflection으로 접근하거나 PlayerManager를 통해 접근
        if (_isLockedOn && lockOnTarget == null && playerNetworkManager != null)
        {
            // playerNetworkManager가 있으면 같은 오브젝트에 PlayerManager가 있음
            var pm = playerNetworkManager.GetComponent<TDA.Character.Player.PlayerManager>();
            if (pm != null)
            {
                // CharacterCombatManager.currentTarget 접근
                var combatMgr = pm.playerCombatManager;
                if (combatMgr != null)
                {
                    // currentTarget은 CharacterCombatManager 베이스에 있으므로 reflection
                    var field = combatMgr.GetType().GetField("currentTarget",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.FlattenHierarchy);
                    if (field != null)
                    {
                        var target = field.GetValue(combatMgr) as UnityEngine.MonoBehaviour;
                        if (target != null)
                        {
                            lockOnTarget = target.transform;
                            _dbgLockOnSource += " | Target:" + target.name;
                        }
                    }
                }
            }
        }
        // 락온 해제 시 타겟 초기화
        if (!_isLockedOn && lockOnTarget != null)
            lockOnTarget = null;

        // ── 경로 1: PlayerNetworkManager.isLockedOn.Value 직접 읽기 (권장) ──
        // PlayerNetworkManager를 Inspector에서 연결하면 이 경로 사용.
        // 네트워크 변수 값을 직접 읽으므로 Animator 파라미터 동기화 지연 없음.
        if (playerNetworkManager != null)
        {
            bool locked = playerNetworkManager.isLockedOn.Value;
            if (locked != _isLockedOn)
            {
                SetLockOn(locked);
                _dbgLockOnSource = locked ? "PNM.isLockedOn=true" : "PNM.isLockedOn=false";
            }
            else
            {
                _dbgLockOnSource = locked ? "PNM (locked)" : "PNM (unlocked)";
            }
            return;
        }

        // ── 경로 2: Animator 파라미터 폴백 ──────────────────────────
        // playerNetworkManager 미연결 시 Animator bool 파라미터 폴링.
        if (!_animSearched)
        {
            _animSearched = true;
            if (playerAnimator == null && playerRigidbody != null)
                playerAnimator = playerRigidbody.GetComponentInChildren<Animator>();
            if (playerAnimator == null && playerCharacterController != null)
                playerAnimator = playerCharacterController.GetComponentInChildren<Animator>();

            if (playerAnimator != null)
            {
                foreach (var param in playerAnimator.parameters)
                {
                    if (param.name == lockOnAnimParamName)
                    { _hasLockOnParam = true; break; }
                }
            }
        }

        if (playerAnimator != null && _hasLockOnParam)
        {
            bool locked = playerAnimator.GetBool(lockOnAnimParamName);
            if (locked != _isLockedOn)
                SetLockOn(locked);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 기즈모 — 씬 뷰에서 락온 SSMB 영향 범위 시각화
    // ─────────────────────────────────────────────────────────────
    [Header("7. Lock-On Depth Blur Protection")]
    [Tooltip("락온된 타겟 Transform. PlayerCombatManager 등에서 런타임에 SetLockOnTarget()으로 주입하거나\n" +
             "Inspector에서 직접 연결합니다.")]
    public Transform lockOnTarget;

    [Tooltip("플레이어 주변 보호 반경 (m). 이 반경 이내는 DepthBlur 없음.")]
    [Range(0.5f, 10f)] public float playerProtectRadius = 1.5f;

    [Tooltip("타겟 주변 보호 반경 (m). 타겟과의 거리가 이 이내면 DepthBlur 없음.")]
    [Range(0.5f, 10f)] public float targetProtectRadius = 1.5f;

    [Header("7. Lock-On Depth Blur Protection")]
    [Tooltip("락온 해제 시 복원할 DepthBlur NearDist 기본값.\n" +
             "DepthBlur 머티리얼의 Near Distance 값과 일치시켜야 합니다.")]
    public float defaultNearDist = 7f;

    [Header("7. Lock-On Gizmo (Scene View)")]
    [Tooltip("씬 뷰에서 락온 SSMB 영향 범위 기즈모를 표시합니다.")]
    public bool showLockOnGizmo = true;

    [Tooltip("기즈모 원의 반지름 (월드 단위). 락온 시 SSMB가 억제되는 체감 범위 표시용.")]
    [Range(1f, 30f)] public float lockOnGizmoRadius = 8f;

    [Tooltip("기즈모 색상 — 락온 중")]
    public Color gizmoColorLocked   = new Color(1f, 0.3f, 0.0f, 0.5f);
    [Tooltip("기즈모 색상 — 락온 해제")]
    public Color gizmoColorUnlocked = new Color(0f, 0.8f, 1f, 0.2f);

    private void OnDrawGizmos()
    {
        if (!showLockOnGizmo) return;

        Vector3 center = transform.position;
        if (playerRigidbody               != null) center = playerRigidbody.transform.position;
        else if (playerCharacterController != null) center = playerCharacterController.transform.position;

        bool locked = Application.isPlaying ? _isLockedOn : false;

        if (locked)
        {
            // ── 보호 가시 반경 (내원) ────────────────────────────
            // 이 반경 이내는 DepthBlur가 적용되지 않음 (NearDist = lockOnGizmoRadius)
            // 기즈모 크기 = 실제 블러 보호 반경과 1:1 연동
            Gizmos.color = gizmoColorLocked;
            DrawGizmoCircle(center, lockOnGizmoRadius, Vector3.up);
            DrawGizmoCircle(center, lockOnGizmoRadius, Vector3.forward);

            // ── 외부 블러 시작 경계 (외원, 내원의 1.5배) ─────────
            // 보호 반경 바깥부터 DepthBlur가 시작되는 시각적 가이드
            Color outerColor = gizmoColorLocked;
            outerColor.a *= 0.3f;
            Gizmos.color = outerColor;
            DrawGizmoCircle(center, lockOnGizmoRadius * 1.5f, Vector3.up);
        }
        else
        {
            // 락온 해제: 기본 NearDist(defaultNearDist) 범위 표시
            Gizmos.color = gizmoColorUnlocked;
            DrawGizmoCircle(center, defaultNearDist, Vector3.up);
        }

#if UNITY_EDITOR
        if (Application.isPlaying && locked)
        {
            UnityEditor.Handles.color = gizmoColorLocked;
            UnityEditor.Handles.Label(
                center + Vector3.up * (lockOnGizmoRadius + 1.5f),
                $"[Lock-On]  보호반경:{lockOnGizmoRadius:F1}m  SSMB x{_currentSSMBScale:F2}  Speed:{_cachedSpeed:F2}");
        }
        else if (Application.isPlaying)
        {
            UnityEditor.Handles.color = gizmoColorUnlocked;
            UnityEditor.Handles.Label(
                center + Vector3.up * (defaultNearDist + 1f),
                $"[Unlocked]  NearDist:{defaultNearDist:F1}m  SSMB x{_currentSSMBScale:F2}");
        }
        else
        {
            // 에디터 정지 상태에서는 기본값 표시
            UnityEditor.Handles.color = Color.cyan;
            UnityEditor.Handles.Label(
                center + Vector3.up * (defaultNearDist + 1f),
                $"[Edit Mode]  기본 NearDist:{defaultNearDist:F1}m | 락온 보호반경:{lockOnGizmoRadius:F1}m");
        }
#endif
    }

    private void DrawGizmoCircle(Vector3 center, float radius, Vector3 normal)
    {
        Vector3 tangent = Vector3.Cross(normal, Vector3.forward);
        if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(normal, Vector3.right);
        tangent.Normalize();

        const int SEGMENTS = 40;
        float angleStep = 360f / SEGMENTS;
        Vector3 prev = center + Quaternion.AngleAxis(0f, normal) * tangent * radius;
        for (int i = 1; i <= SEGMENTS; i++)
        {
            Vector3 next = center + Quaternion.AngleAxis(angleStep * i, normal) * tangent * radius;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}