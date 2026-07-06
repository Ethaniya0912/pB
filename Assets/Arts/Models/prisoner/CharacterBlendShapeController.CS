using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터 루트(프리팹 최상단)에 부착.
/// 몸 + 옷(하위의 모든 SkinnedMeshRenderer)의 블렌드셰이프를 슬라이더 하나로 동시 조절.
/// ExecuteAlways라 플레이 모드가 아니어도 에디터에서 바로 확인 가능.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class CharacterBlendShapeController : MonoBehaviour
{
    [Header("체형 (0 ~ 100)")]
    [Range(0f, 100f)] public float fat;
    [Range(0f, 100f)] public float muscular;

    [Header("대상 렌더러 (비우면 자식에서 자동 수집)")]
    public List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>();

    [Header("옵션")]
    [Tooltip("추가로 동기화할 커스텀 키 이름 (예: sim_drape)")]
    public List<SyncKey> extraKeys = new List<SyncKey>();

    [System.Serializable]
    public struct SyncKey
    {
        public string name;
        [Range(0f, 100f)] public float value;
    }

    // 렌더러별 { 키이름 → 인덱스 } 캐시
    readonly Dictionary<SkinnedMeshRenderer, Dictionary<string, int>> _cache
        = new Dictionary<SkinnedMeshRenderer, Dictionary<string, int>>();

    void OnEnable()   { CollectRenderers(); }
    void OnValidate() { CollectRenderers(); Apply(); }
    void LateUpdate() { Apply(); }

    /// <summary>renderers가 비어 있으면 자식 전체에서 자동 수집.</summary>
    public void CollectRenderers()
    {
        if (renderers == null) renderers = new List<SkinnedMeshRenderer>();
        renderers.RemoveAll(r => r == null);
        if (renderers.Count == 0)
            renderers.AddRange(GetComponentsInChildren<SkinnedMeshRenderer>(true));
        _cache.Clear();
    }

    void Apply()
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            var smr = renderers[i];
            if (smr == null || smr.sharedMesh == null) continue;

            SetWeight(smr, "fat", fat);
            SetWeight(smr, "muscular", muscular);

            for (int k = 0; k < extraKeys.Count; k++)
                if (!string.IsNullOrEmpty(extraKeys[k].name))
                    SetWeight(smr, extraKeys[k].name, extraKeys[k].value);
        }
    }

    void SetWeight(SkinnedMeshRenderer smr, string keyName, float value)
    {
        if (!_cache.TryGetValue(smr, out var map))
        {
            map = new Dictionary<string, int>();
            var mesh = smr.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
                map[mesh.GetBlendShapeName(i)] = i;
            _cache[smr] = map;
        }
        // 해당 키가 없는 렌더러(예: 키 없는 소품)는 조용히 건너뜀
        if (map.TryGetValue(keyName, out int idx))
            smr.SetBlendShapeWeight(idx, value);
    }

    /// <summary>코드에서 체형을 바꿀 때 사용. 예: controller.SetBody(80f, 0f);</summary>
    public void SetBody(float fatValue, float muscularValue)
    {
        fat = Mathf.Clamp(fatValue, 0f, 100f);
        muscular = Mathf.Clamp(muscularValue, 0f, 100f);
        Apply();
    }
}
