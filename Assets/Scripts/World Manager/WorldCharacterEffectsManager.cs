// ══════════════════════════════════════════════════════════════════════════════
//  WorldCharacterEffectsManager.cs  (기존 파일 최소 확장)
//  계층  : [L2 Router/Policy] — 프로젝트 전역 VFX 에셋 저장소
//
//  변경 요약:
//  ┌──────────────────────────────────────────────────────────────────────┐
//  │  이전 (기존 유지)              추가 항목                              │
//  │  bloodSplatterVFX             vfxRegistry  (HitSparkVFXRegistry SO) │
//  │  hitsparkVFX (폴백용 유지)    gpuSparkPrefab (ParrySparkGPUManager) │
//  └──────────────────────────────────────────────────────────────────────┘
//
//  hitsparkVFX 는 vfxRegistry 미등록 이벤트 타입의 폴백으로 유지합니다.
//  새로운 타격 타입은 vfxRegistry 에 등록하여 SO 드리븐으로 처리합니다.
// ══════════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using UnityEngine;

public class WorldCharacterEffectsManager : MonoBehaviour
{
    public static WorldCharacterEffectsManager Instance { get; private set; }

    // ── 기존 필드 완전 보존 ───────────────────────────────────────────────────
    [Header("VFX (기존)")]
    [Tooltip("피 튀김 이펙트 프리팹 (폴백)")]
    public GameObject bloodSplatterVFX;

    [Tooltip("HitSpark 이펙트 프리팹 (vfxRegistry 미등록 타입 폴백)")]
    public GameObject hitsparkVFX;

    // ── 신규: SO 드리븐 HitSpark 지원 ────────────────────────────────────────
    [Header("VFX Registry (SO-Driven)")]
    [Tooltip("AnimationEventType → HitSparkVFXData 전역 레지스트리. " +
             "CharacterEffectsManager 들이 이 레퍼런스를 공유합니다.")]
    public HitSparkVFXRegistry vfxRegistry;

    [Tooltip("GPU 스파크 파티클 매니저 프리팹 (ParrySparkGPUManager 가 붙어있는 프리팹). " +
             "CharacterEffectsManager.gpuSparkPrefab 의 전역 폴백으로도 사용됩니다.")]
    public ParrySparkGPUManager gpuSparkPrefab;

    // ── 기존 필드 완전 보존 ───────────────────────────────────────────────────
    [Header("Damage")]
    public TakeDamageEffect takeDamageEffect;

    [SerializeField] private List<InstantCharacterEffect> instantEffects;

    // ══════════════════════════════════════════════════════════════════════════
    //  생명주기 (기존 로직 완전 보존)
    // ══════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
        GenerateEffectIDs();

        // 신규: vfxRegistry 미할당 경고
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (vfxRegistry == null)
        {
            Debug.LogWarning(
                "[WorldCharacterEffectsManager] vfxRegistry 가 null 입니다. " +
                "HitSparkVFXData SO 드리븐 기능이 비활성화됩니다. " +
                "인스펙터에서 HitSparkVFXRegistry 에셋을 연결하세요.");
        }
        if (gpuSparkPrefab == null)
        {
            Debug.LogWarning(
                "[WorldCharacterEffectsManager] gpuSparkPrefab 이 null 입니다. " +
                "GPU 스파크 VFX 가 재생되지 않습니다.");
        }
#endif
    }

    private void GenerateEffectIDs()
    {
        if (instantEffects == null) return;
        for (int i = 0; i < instantEffects.Count; i++)
            instantEffects[i].instantEffectID = i;
    }
}
