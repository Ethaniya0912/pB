using UnityEngine;

/// <summary>
/// [DepthBlur 전체 글로벌 주입기]
///
/// 문제 근본 원인:
///   DepthBlur.shader의 모든 변수가 CBUFFER 없이 선언됨
///   → Unity가 Material 값을 무시하고 Global 값(=0, 미초기화)을 읽음
///
///   _MidDist=0, _FarDist=0 일 때 셰이더 내부:
///     normalizedDist = saturate((depth-0)/(0-0)) = saturate(+∞) = 1.0
///     → mixWeight=1.0 (모든 원경 픽셀 100% 최대 블러)
///     → _NearSampleCutoffRatio=0 → 캐릭터 픽셀 커트오프 없음
///     → 배경 픽셀의 DreamBlur 커널에 캐릭터 픽셀 포함
///     → 캐릭터 주변 배경에 캐릭터 색상 혼입 → 시각적 "캐릭터 블러"
///
/// 해결: 모든 DepthBlur 파라미터를 Material에서 읽어 Global로 주입
///
/// 사용법:
///   DontDestroyOnLoad 오브젝트(ShaderCoordinationManager 등)에 추가
///   depthBlurMaterial 슬롯에 DepthBlur.mat 연결
///   Script Execution Order: +600 (모든 시스템보다 나중에 실행)
/// </summary>
[DefaultExecutionOrder(600)]
public class DepthBlurGlobalInjector : MonoBehaviour
{
    [Header("DepthBlur Material (필수 연결)")]
    public Material depthBlurMaterial;

    [Header("SSMB Material (선택 — 이미 Feature에서 주입 중이면 불필요)")]
    public Material ssmbMaterial;

    // ─── Shader Property ID 캐시 ───────────────────────────────
    // Distance Blur
    static readonly int P_NearDist   = Shader.PropertyToID("_NearDist");
    static readonly int P_MidDist    = Shader.PropertyToID("_MidDist");
    static readonly int P_FarDist    = Shader.PropertyToID("_FarDist");
    static readonly int P_MidBlur    = Shader.PropertyToID("_MidBlurSize");
    static readonly int P_FarBlur    = Shader.PropertyToID("_FarBlurSize");
    static readonly int P_BlurExp    = Shader.PropertyToID("_BlurExponent");
    static readonly int P_CenterBlur = Shader.PropertyToID("_CenterBlurScale");
    // Artifact Correction
    static readonly int P_DepthThr   = Shader.PropertyToID("_DepthThreshold");
    static readonly int P_Jitter     = Shader.PropertyToID("_JitterIntensity");
    static readonly int P_Cutoff     = Shader.PropertyToID("_NearSampleCutoffRatio");
    // Pixel / Dream
    static readonly int P_PixScale   = Shader.PropertyToID("_PixelScale");
    static readonly int P_FarPixScale = Shader.PropertyToID("_FarPixelScale");
    static readonly int P_PixCurve   = Shader.PropertyToID("_PixelCurve");
    static readonly int P_Moire      = Shader.PropertyToID("_MoireReduction");
    static readonly int P_DreamHaze  = Shader.PropertyToID("_DreamHaze");
    static readonly int P_DreamChroma = Shader.PropertyToID("_DreamChroma");
    // Mipmap
    static readonly int P_IdleMip    = Shader.PropertyToID("_IdleMipmapRange");
    static readonly int P_SprintMip  = Shader.PropertyToID("_SprintMipmapRange");
    // SSMB
    static readonly int P_SSNear     = Shader.PropertyToID("_SSBlurDepthNear");
    static readonly int P_SSFade     = Shader.PropertyToID("_SSBlurDepthFade");

    void Awake()  => InjectAll();
    void LateUpdate() => InjectAll();

    void InjectAll()
    {
        if (depthBlurMaterial != null) InjectDepthBlur();
        if (ssmbMaterial      != null) InjectSSMB();
    }

    void InjectDepthBlur()
    {
        var m = depthBlurMaterial;

        // ── Distance Blur ───────────────────────────────────────
        // _MidDist=0 & _FarDist=0 → division-by-zero → mixWeight=1.0 → 전체 블러
        Shader.SetGlobalFloat(P_NearDist,   m.GetFloat(P_NearDist));    // mat=20
        Shader.SetGlobalFloat(P_MidDist,    m.GetFloat(P_MidDist));     // mat=40 ★핵심
        Shader.SetGlobalFloat(P_FarDist,    m.GetFloat(P_FarDist));     // mat=50 ★핵심
        Shader.SetGlobalFloat(P_MidBlur,    m.GetFloat(P_MidBlur));     // mat=8
        Shader.SetGlobalFloat(P_FarBlur,    m.GetFloat(P_FarBlur));     // mat=10
        Shader.SetGlobalFloat(P_BlurExp,    m.GetFloat(P_BlurExp));     // mat=1
        Shader.SetGlobalFloat(P_CenterBlur, m.GetFloat(P_CenterBlur));  // mat=0.2

        // ── Artifact Correction ──────────────────────────────────
        // _NearSampleCutoffRatio=0 → 캐릭터 픽셀 cutoff 없음 → bleeding ★핵심
        // _DepthThreshold=0 → depth-aware 가중치 없음 → 캐릭터 weight=1.0 ★핵심
        Shader.SetGlobalFloat(P_DepthThr, m.GetFloat(P_DepthThr));  // mat=0.1
        Shader.SetGlobalFloat(P_Jitter,   m.GetFloat(P_Jitter));     // mat=0
        Shader.SetGlobalFloat(P_Cutoff,   m.GetFloat(P_Cutoff));     // mat=0.5 ★핵심

        // ── Pixel / Dream ────────────────────────────────────────
        Shader.SetGlobalFloat(P_PixScale,    m.GetFloat(P_PixScale));
        Shader.SetGlobalFloat(P_FarPixScale, m.GetFloat(P_FarPixScale));
        Shader.SetGlobalFloat(P_PixCurve,    m.GetFloat(P_PixCurve));
        Shader.SetGlobalFloat(P_Moire,       m.GetFloat(P_Moire));
        Shader.SetGlobalFloat(P_DreamHaze,   m.GetFloat(P_DreamHaze));
        Shader.SetGlobalFloat(P_DreamChroma, m.GetFloat(P_DreamChroma));

        // ── Mipmap ──────────────────────────────────────────────
        Shader.SetGlobalFloat(P_IdleMip,   m.GetFloat(P_IdleMip));
        Shader.SetGlobalFloat(P_SprintMip, m.GetFloat(P_SprintMip));
    }

    void InjectSSMB()
    {
        var m = ssmbMaterial;
        float near = m.GetFloat(P_SSNear);
        float fade = m.GetFloat(P_SSFade);
        Shader.SetGlobalFloat(P_SSNear, near > 0.01f ? near : 6f);
        Shader.SetGlobalFloat(P_SSFade, fade > 0.01f ? fade : 2f);
    }

#if UNITY_EDITOR
    void OnGUI()
    {
        float mid  = Shader.GetGlobalFloat(P_MidDist);
        float far  = Shader.GetGlobalFloat(P_FarDist);
        float cut  = Shader.GetGlobalFloat(P_Cutoff);
        float dThr = Shader.GetGlobalFloat(P_DepthThr);

        bool ok = mid > 1f && far > 1f && cut > 0.01f && dThr > 0.001f;
        var s = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = ok ? FontStyle.Normal : FontStyle.Bold,
            normal = { textColor = ok ? Color.green : Color.red }
        };
        GUI.Label(new Rect(10, Screen.height - 52, 600, 20),
            $"[DBInjector] MidDist={mid:F0}  FarDist={far:F0}  Cutoff={cut:F2}  DepthThr={dThr:F2}", s);
        if (!ok)
            GUI.Label(new Rect(10, Screen.height - 32, 600, 20),
                "★ 미초기화 글로벌 감지 — depthBlurMaterial 연결 확인!", s);
    }
#endif
}
