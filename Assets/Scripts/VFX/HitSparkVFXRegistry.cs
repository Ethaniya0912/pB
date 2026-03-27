// ══════════════════════════════════════════════════════════════════════════════
//  HitSparkVFXRegistry.cs
//  계층  : [Data/SO] — 순수 데이터 매핑 테이블
//  역할  : AnimationEventType(VFX 계열) → HitSparkVFXData 를 O(1) 조회합니다.
//          WorldCharacterEffectsManager 가 씬에 하나를 갖고 CharacterEffectsManager
//          들이 공유 참조합니다.
//
//  등록 방법:
//    Project 우클릭 → Create → TDA/VFX/HitSpark VFX Registry
//    각 Entry 의 eventType 을 지정하고 vfxData 슬롯에 HitSparkVFXData 에셋 연결.
// ══════════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "HitSparkVFXRegistry",
    menuName = "TDA/VFX/HitSpark VFX Registry",
    order = -1)]   // 메뉴 최상단 배치
public class HitSparkVFXRegistry : ScriptableObject
{
    // ─────────────────────────────────────────────────────────────────────────
    //  직렬화 구조
    // ─────────────────────────────────────────────────────────────────────────
    [System.Serializable]
    public struct VFXEntry
    {
        [Tooltip("Enums.cs 에 정의된 VFX 계열 AnimationEventType")]
        public AnimationEventType eventType;

        [Tooltip("이 이벤트에 대응하는 HitSparkVFXData 에셋")]
        public HitSparkVFXData vfxData;
    }

    [Header("AnimationEventType → HitSparkVFXData 매핑 테이블")]
    [SerializeField] private VFXEntry[] entries;

    // ─────────────────────────────────────────────────────────────────────────
    //  런타임 캐시 (SO 로드 후 첫 조회 시 빌드)
    // ─────────────────────────────────────────────────────────────────────────
    private Dictionary<AnimationEventType, HitSparkVFXData> _map;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// AnimationEventType 으로 HitSparkVFXData 를 O(1) 조회합니다.
    /// 매핑 없으면 null 반환 — 호출부에서 null 체크 필수.
    /// </summary>
    public HitSparkVFXData GetData(AnimationEventType eventType)
    {
        EnsureMapBuilt();
        _map.TryGetValue(eventType, out HitSparkVFXData data);
        return data;
    }

    /// <summary>
    /// HitSparkType 으로도 조회 가능한 오버로드.
    /// HitSparkVFXData.sparkType 필드와 비교합니다.
    /// </summary>
    public HitSparkVFXData GetDataBySparkType(HitSparkType sparkType)
    {
        EnsureMapBuilt();
        foreach (var kvp in _map)
        {
            if (kvp.Value != null && kvp.Value.sparkType == sparkType)
                return kvp.Value;
        }
        return null;
    }

    // ── 내부 ──────────────────────────────────────────────────────────────────

    // SO 리로드 대응 — 도메인 리로드 시 캐시 무효화
    private void OnEnable() => _map = null;

    private void EnsureMapBuilt()
    {
        if (_map != null) return;

        _map = new Dictionary<AnimationEventType, HitSparkVFXData>(
            entries?.Length ?? 0);

        if (entries == null) return;

        foreach (var e in entries)
        {
            if (e.vfxData == null) continue;

            if (!_map.TryAdd(e.eventType, e.vfxData))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning(
                    $"[HitSparkVFXRegistry] 중복 키: {e.eventType} " +
                    $"— 첫 번째 항목만 사용됩니다.", this);
#endif
            }
        }
    }

    // ── 에디터 헬퍼 ───────────────────────────────────────────────────────────
#if UNITY_EDITOR
    [ContextMenu("레지스트리 유효성 검증")]
    private void ValidateInEditor()
    {
        int errors = 0;
        var seen = new HashSet<AnimationEventType>();

        if (entries == null || entries.Length == 0)
        {
            Debug.LogError("[HitSparkVFXRegistry] entries 가 비어 있습니다.", this);
            return;
        }

        foreach (var e in entries)
        {
            if (e.vfxData == null)
            {
                Debug.LogWarning(
                    $"[HitSparkVFXRegistry] eventType={e.eventType} 의 vfxData 가 null 입니다.", this);
                errors++;
            }
            if (!seen.Add(e.eventType))
            {
                Debug.LogError(
                    $"[HitSparkVFXRegistry] 중복 키: {e.eventType}", this);
                errors++;
            }
        }

        Debug.Log(errors == 0
            ? $"[HitSparkVFXRegistry] ✅ 검증 통과 ({entries.Length}개 항목)"
            : $"[HitSparkVFXRegistry] ❌ {errors}개 문제 발견", this);
    }
#endif
}
