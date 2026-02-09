using UnityEngine;

/**
 * [Phase 2: Fog Zone Controller - Debug Enhanced]
 * 씬의 Cube 위치를 기반으로 셰이더 전역 구역 좌표를 업데이트합니다.
 * 디버깅 로그 및 상태 감시 기능이 추가되었습니다.
 */
[ExecuteAlways]
public class FogZoneController : MonoBehaviour
{
    [Header("Zone Settings")]
    [Range(0.01f, 5f)] public float edgeSoftness = 0.5f;

    [Header("Debug Tools")]
    public bool debugLogging = false;
    [ReadOnly] public Vector3 currentMin;
    [ReadOnly] public Vector3 currentMax;

    private static readonly int ZoneMinID = Shader.PropertyToID("_FogZoneMin");
    private static readonly int ZoneMaxID = Shader.PropertyToID("_FogZoneMax");
    private static readonly int EdgeSoftID = Shader.PropertyToID("_FogZoneEdgeSoftness");

    private void OnEnable()
    {
        if (debugLogging) Debug.Log($"[FogZone] '{name}' 가 활성화되어 전역 좌표를 제어합니다.");
    }

    void Update() => UpdateZoneProperties();

    private void UpdateZoneProperties()
    {
        Vector3 pos = transform.position;
        Vector3 scale = transform.lossyScale;

        // 유효성 검사: 스케일이 0이면 안개가 그려지지 않음
        if (scale.x <= 0 || scale.y <= 0 || scale.z <= 0)
        {
            if (Application.isPlaying) Debug.LogWarning($"[FogZone] '{name}'의 Scale이 0 이하입니다! 안개가 보이지 않습니다.");
            return;
        }

        // AABB 계산
        currentMin = pos - (scale * 0.5f);
        currentMax = pos + (scale * 0.5f);

        // GPU 전역 데이터 주입
        Shader.SetGlobalVector(ZoneMinID, currentMin);
        Shader.SetGlobalVector(ZoneMaxID, currentMax);
        Shader.SetGlobalFloat(EdgeSoftID, edgeSoftness);

        if (debugLogging && transform.hasChanged)
        {
            Debug.Log($"[FogZone] {name} Sync: Min {currentMin}, Max {currentMax}");
            transform.hasChanged = false;
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0, 1, 1, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, Vector3.one);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }
}

// 단순 읽기 전용 인스펙터 속성
public class ReadOnlyAttribute : PropertyAttribute { }
[UnityEditor.CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
public class ReadOnlyDrawer : UnityEditor.PropertyDrawer
{
    public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
    {
        GUI.enabled = false;
        UnityEditor.EditorGUI.PropertyField(position, property, label);
        GUI.enabled = true;
    }
}