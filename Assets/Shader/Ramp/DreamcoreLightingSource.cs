using UnityEngine;

/**
 * Dreamcore Lighting Source Controller
 * 이 스크립트는 씬의 전역 변수(Ramp, Madness, Gamma 등)를 제어합니다.
 * 감마 범위를 0.1부터 시작하도록 수정하여 광원의 확산 범위를 조절할 수 있게 했습니다.
 */ 
[ExecuteInEditMode]
public class DreamcoreLightingSource : MonoBehaviour
{
    [Header("Global Ramp Settings")]
    [Tooltip("256x1 크기의 포인트 필터링된 램프 LUT를 할당하세요.")]
    public Texture2D globalRampTexture;

    [Header("Atmosphere Control")]
    [Range(0f, 1f)]
    [Tooltip("0은 안정 상태, 1은 극단적인 색채 변질을 나타냅니다.")]
    public float globalMadness = 0f;

    [Header("Lighting Physics")]
    [Range(0.1f, 5f)] // 최소값을 0.1로 하향 조정하여 광원 확산 범위를 넓힐 수 있게 함
    [Tooltip("1보다 낮으면 광원이 넓게 퍼지고, 1보다 높으면 광원이 좁고 선명하게 맺힙니다.")]
    public float globalRampGamma = 1.0f;

    [Range(1f, 128f)]
    public float globalQuantizeSteps = 5f;

    // 셰이더 프로퍼티 ID 캐싱
    private static readonly int RampTexID = Shader.PropertyToID("_GlobalRampTex");
    private static readonly int MadnessID = Shader.PropertyToID("_GlobalMadness");
    private static readonly int GammaID = Shader.PropertyToID("_GlobalRampGamma");
    private static readonly int StepsID = Shader.PropertyToID("_GlobalSteps");

    void Update()
    {
        if (globalRampTexture == null) return;

        // GPU 전역 메모리에 데이터 주입
        Shader.SetGlobalTexture(RampTexID, globalRampTexture);
        Shader.SetGlobalFloat(MadnessID, globalMadness);
        Shader.SetGlobalFloat(GammaID, globalRampGamma);
        Shader.SetGlobalFloat(StepsID, globalQuantizeSteps);
    }
}