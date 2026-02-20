using UnityEngine;

/// <summary>
/// [Global Manager] 모든 셰이더의 전역 변수(Global Variables)를 총괄 관리하는 방송국 역할을 합니다.
/// 플레이어의 피드백 데이터와 월드의 심미적 수치를 GPU로 송출합니다.
/// </summary>
[ExecuteInEditMode]
public class ShaderCoordinationManager : MonoBehaviour
{
    public static ShaderCoordinationManager Instance { get; private set; }

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
    [SerializeField, Tooltip("현재 플레이어의 속도 비율 (0~1)")] private float currentSpeedFactor;
    [SerializeField, Tooltip("현재 플레이어의 물리적 호흡/진동 값")] private float currentMovementPulse;

    // Shader IDs
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

    // [수정] Canvas 셰이더와 이름을 일치시켜 중복 정의 에러 해결
    private static readonly int MipBiasID = Shader.PropertyToID("_VFXMipBias");

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

    private void Update() => SyncGlobalVariables();
    private void OnValidate() => SyncGlobalVariables();

    /// <summary>
    /// 외부 피드백 매니저로부터 플레이어의 실시간 상태를 전달받습니다.
    /// </summary>
    public void UpdatePlayerSpeedFeedback(float speedFactor, float pulse)
    {
        currentSpeedFactor = speedFactor;
        currentMovementPulse = pulse;
    }

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

        if (blueNoise != null) Shader.SetGlobalTexture(NoiseTexID, blueNoise);

        Shader.SetGlobalFloat(ThresholdID, bloomThreshold);
        Shader.SetGlobalFloat(EdgeStrengthID, edgeProtectionIntensity);
        Shader.SetGlobalColor(BleedColorID, bleedColor);

        // 실시간 이동 피드백 데이터 송출
        Shader.SetGlobalFloat(SpeedFactorID, currentSpeedFactor);
        Shader.SetGlobalFloat(MovementPulseID, currentMovementPulse);

        // [수정] 밉맵 바이어스 값을 _VFXMipBias 이름으로 송출
        Shader.SetGlobalFloat(MipBiasID, Mathf.Lerp(0f, -2.0f, currentSpeedFactor));
    }
}