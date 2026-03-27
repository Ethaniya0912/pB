// ══════════════════════════════════════════════════════════════════════════════
//  HitSparkVFXData.cs
//  계층  : [Data/SO] — 순수 데이터 보관소, 로직 없음
//  역할  : ParrySparkGPUManager 에 주입될 모든 파라미터를 한 곳에 정의합니다.
//          기획자는 이 SO 에셋만 편집하면 됩니다.
//
//  참조  : AnimationEventType(Enums.cs), HitSparkType(이 파일)
//  의존성: 없음 (순수 데이터)
// ══════════════════════════════════════════════════════════════════════════════
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  SparkLayerConfig — 기존 ParrySparkGPUManager 의 SparkLayerConfig 를
//  SO 필드로 재정의. 구조체 레이아웃은 완전히 동일합니다.
//  (기존 코드와의 호환성을 위해 SO 에서 이 값을 읽어 GPUManager 에 그대로 넘깁니다)
// ─────────────────────────────────────────────────────────────────────────────
[System.Serializable]
public struct HitSparkLayerConfig
{
    [Header("Count & Life")]
    [Range(0, 1000)]
    public int count;
    public float lifeMin;
    public float lifeMax;

    [Header("Speed")]
    [Range(0f, 1f)]
    public float slowFraction;
    public float speedMin;
    public float speedMax;

    [Header("Particle Size")]
    public float sizeWidth;
    public float sizeHeight;
    [Range(0f, 1f)]
    public float sizeVariance;

    [Header("Spread Shape")]
    [Range(0f, 1f)]
    public float spreadAmount;
    public bool useCameraPlaneSpread;
}

[CreateAssetMenu(
    fileName = "HitSparkVFX_Default",
    menuName = "TDA/VFX/HitSpark VFX Data",
    order = 0)]
public class HitSparkVFXData : ScriptableObject
{
    // ── Identity ─────────────────────────────────────────────────────────────
    [Header("── Identity ─────────────────────────────────────────────")]
    [Tooltip("이 데이터 에셋이 대응하는 스파크 종류 (HitSparkVFXRegistry 와 연계)")]
    public HitSparkType sparkType = HitSparkType.Default;

    // ── GPU Runtime Assets ────────────────────────────────────────────────────
    [Header("── GPU Runtime Assets ──────────────────────────────────")]
    [Tooltip("물리 시뮬레이션 Compute Shader (SparkPhysics.compute / UpdateSparks 커널)")]
    public ComputeShader physicsComputeShader;

    [Tooltip("렌더링 Material (SparkRender_Procedural.shader 기반, 인스턴스 복제됨)")]
    public Material sparkMaterial;

    // ── Buffer ────────────────────────────────────────────────────────────────
    [Header("── Buffer ───────────────────────────────────────────────")]
    [Tooltip("GPU 파티클 버퍼 최대 용량. 3개 레이어 count 합산보다 커야 합니다.")]
    [Min(1)]
    public int maxSparks = 2000;

    // ── Burst Direction ───────────────────────────────────────────────────────
    [Header("── Burst Direction ─────────────────────────────────────")]
    [Tooltip("위 방향 편향 (-0.5~1). 양수일수록 스파크가 위로 솟구칩니다.")]
    [Range(-0.5f, 1f)]
    public float upwardBias = 0.15f;

    // ── Layer Configs ─────────────────────────────────────────────────────────
    [Header("── Layer 1 : Primary Streaks ─────────────────────────")]
    public HitSparkLayerConfig primaryLayer;

    [Header("── Layer 2 : Secondary Sparks ────────────────────────")]
    public HitSparkLayerConfig secondaryLayer;

    [Header("── Layer 3 : Single Beam ───────────────────────────")]
    public bool enableTertiaryLayer = true;
    public HitSparkLayerConfig tertiaryLayer;

    // ── Impact Light ──────────────────────────────────────────────────────────
    [Header("── Impact Light ─────────────────────────────────────")]
    [Tooltip("타격 지점에 Point Light 를 잠깐 발생시킬지 여부")]
    public bool enableImpactLight = true;
    [Tooltip("광원 색상 — 주황-빨강 계열 권장")]
    public Color impactLightColor = new Color(1f, 0.35f, 0.05f);
    [Tooltip("광원 범위 (m)")]
    public float impactLightRange = 4f;
    [Tooltip("최대 광원 세기")]
    public float impactLightIntensity = 12f;
    [Tooltip("광원이 0으로 fade out 되는 시간 (초)")]
    public float impactLightFadeTime = 0.25f;
    [Tooltip("Fade 곡선 지수 (1=선형, 4=급격한 지수 감쇠)")]
    [Range(1f, 10f)]
    public float impactLightFadeExponent = 4f;

    // ── Auto Destroy ──────────────────────────────────────────────────────────
    [Header("── Auto Destroy ────────────────────────────────────")]
    [Tooltip("스파크 오브젝트를 자동 파괴할 시간 (초). 레이어 최대 수명보다 여유 있게 설정.")]
    public float autoDestroyTime = 2.5f;

    // ─────────────────────────────────────────────────────────────────────────
    //  Static Presets — 에디터/스크립트에서 기본값 생성 시 사용
    // ─────────────────────────────────────────────────────────────────────────

    public static HitSparkLayerConfig DefaultPrimaryLayer => new HitSparkLayerConfig
    {
        count = 50,
        lifeMin = 0.4f,
        lifeMax = 0.9f,
        slowFraction = 0.70f,
        speedMin = 4f,
        speedMax = 12f,
        sizeWidth = 0.012f,
        sizeHeight = 0.06f,
        sizeVariance = 0.6f,
        spreadAmount = 0.35f,
        useCameraPlaneSpread = true,
    };

    public static HitSparkLayerConfig DefaultSecondaryLayer => new HitSparkLayerConfig
    {
        count = 350,
        lifeMin = 0.15f,
        lifeMax = 0.55f,
        slowFraction = 0.70f,
        speedMin = 1.5f,
        speedMax = 8f,
        sizeWidth = 0.018f,
        sizeHeight = 0.018f,
        sizeVariance = 0.5f,
        spreadAmount = 0.85f,
        useCameraPlaneSpread = true,
    };

    public static HitSparkLayerConfig DefaultTertiaryLayer => new HitSparkLayerConfig
    {
        count = 1,
        lifeMin = 0.5f,
        lifeMax = 0.8f,
        slowFraction = 0f,
        speedMin = 6f,
        speedMax = 6f,
        sizeWidth = 0.008f,
        sizeHeight = 0.30f,
        sizeVariance = 0f,
        spreadAmount = 0f,
        useCameraPlaneSpread = false,
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  에디터 헬퍼
    // ─────────────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
    [ContextMenu("기본값으로 레이어 초기화 (Default Preset)")]
    private void ResetToDefaults()
    {
        primaryLayer = DefaultPrimaryLayer;
        secondaryLayer = DefaultSecondaryLayer;
        tertiaryLayer = DefaultTertiaryLayer;
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[HitSparkVFXData] '{name}' 레이어를 기본값으로 초기화했습니다.");
    }

    private void OnValidate()
    {
        int layerTotal = primaryLayer.count + secondaryLayer.count
            + (enableTertiaryLayer ? tertiaryLayer.count : 0);

        if (maxSparks < layerTotal)
        {
            Debug.LogWarning(
                $"[HitSparkVFXData] '{name}': maxSparks({maxSparks}) < " +
                $"레이어 count 합산({layerTotal}). maxSparks를 늘리세요.");
        }
    }
#endif
}
