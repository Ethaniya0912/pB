using UnityEngine;

[ExecuteInEditMode]
public class ShaderCoordinationManager : MonoBehaviour
{
    [Header("1. Aesthetic Coordination (LUT & Lighting)")]
    [Tooltip("256x1 포인트 필터링된 LUT (Wrap: Clamp 권장)")]
    public Texture2D globalRampTexture;
    [Range(0f, 1f)] public float globalMadness = 0f;
    [Range(0.1f, 5f)] public float globalRampGamma = 0.8f;
    [Range(1f, 128f)] public float globalQuantizeSteps = 8f;

    [Header("2. Atmospheric Coordination (Fog & Rays)")]
    [Range(0f, 0.5f)] public float fogDensity = 0.08f;
    [Range(12, 128)] public int raySteps = 64; // [V95] 품질을 위해 기본값 상향
    public float maxDistance = 100f; // [V95] 거리 확장
    [Range(1f, 8f)] public float lightQuantization = 4.0f;

    // [V95 핵심] 허공을 채워줄 기본 안개 색상 (절대 검은색으로 두지 마세요!)
    [ColorUsage(false, true)] // HDR Color 사용
    public Color fogAmbientColor = new Color(0.05f, 0.05f, 0.1f, 1.0f);

    public Texture2D blueNoise;

    [Header("3. Visual Paradox Coordination (Edge & Bleed)")]
    [Range(0f, 1f)] public float bloomThreshold = 0.7f;
    [Range(0.1f, 10f)] public float edgeProtectionIntensity = 5.0f;
    public Color bleedColor = new Color(1, 0.8f, 0.6f, 1);

    // Shader IDs
    private static readonly int RampTexID = Shader.PropertyToID("_GlobalRampTex");
    private static readonly int MadnessID = Shader.PropertyToID("_GlobalMadness");
    private static readonly int GammaID = Shader.PropertyToID("_GlobalRampGamma");
    private static readonly int StepsID = Shader.PropertyToID("_GlobalSteps");

    private static readonly int FogDensityID = Shader.PropertyToID("_FogDensity");
    private static readonly int StepCountID = Shader.PropertyToID("_StepCount");
    private static readonly int MaxDistID = Shader.PropertyToID("_MaxDist");
    private static readonly int QuantizationID = Shader.PropertyToID("_Quantization");
    private static readonly int AmbientID = Shader.PropertyToID("_GlobalAmbientColor"); // [V95]
    private static readonly int NoiseTexID = Shader.PropertyToID("_NoiseTex");

    private static readonly int ThresholdID = Shader.PropertyToID("_Threshold");
    private static readonly int EdgeStrengthID = Shader.PropertyToID("_EdgeStrength");
    private static readonly int BleedColorID = Shader.PropertyToID("_BleedColor");

    void Update() => SyncGlobalVariables();
    void OnValidate() => SyncGlobalVariables();

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
        Shader.SetGlobalColor(AmbientID, fogAmbientColor); // [V95] Sync

        if (blueNoise != null) Shader.SetGlobalTexture(NoiseTexID, blueNoise);

        Shader.SetGlobalFloat(ThresholdID, bloomThreshold);
        Shader.SetGlobalFloat(EdgeStrengthID, edgeProtectionIntensity);
        Shader.SetGlobalColor(BleedColorID, bleedColor);
    }
}