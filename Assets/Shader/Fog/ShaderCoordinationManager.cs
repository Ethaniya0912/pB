using UnityEngine;

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
    }

    // 기존 기능: Update에서 LUT/Fog 등 정적 파라미터 동기화
    private void Update() => SyncGlobalVariables();

    // 신규: LateUpdate에서 VP 행렬 처리
    // LateUpdate를 사용하는 이유:
    //   카메라가 Update()에서 이동을 완료한 후 행렬을 캡처해야
    //   이전-현재 프레임 델타가 정확합니다.
    //   Update() 순서 의존성 없이 항상 "이 프레임의 최종 카메라 위치"를 보장합니다.
    private void LateUpdate()
    {
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
}