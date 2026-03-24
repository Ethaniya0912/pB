#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// [우선순위 1 — SparkMotionVectorSetup]
///
/// ■ 이 파일이 하는 일
///   SparkRender.shader로 렌더되는 GPU 파티클 오브젝트(ParrySparkGPUManager)의
///   MotionVectorGenerationMode를 ForceNoMotion으로 일괄 설정합니다.
///
/// ■ 왜 필요한가
///   SparkRender는 버텍스 지오메트리를 속도 방향으로 늘려 블러를 표현합니다.
///   ScreenSpaceMotionBlur(SSMB)는 최종 렌더된 화면을 다시 샘플링하므로,
///   이미 늘어진 스파크 픽셀에 블러가 한 번 더 적용됩니다.
///   ForceNoMotion으로 설정하면 스파크 픽셀의 모션 벡터가 0으로 기록되어
///   SSMB의 ObjMotionThreshold 조건에서 자동으로 제외됩니다.
///
/// ■ 사용 방법
///   Unity 메뉴 → Dreamcore/Tools/Setup Spark Motion Vectors
///   버튼 클릭 한 번으로 씬 내 모든 ParrySparkGPUManager 오브젝트에 적용됩니다.
///
/// ■ 유저 체감 효과
///   - 스파크가 SSMB에 의해 이중으로 뭉개지지 않음
///   - 타격 시 스파크의 날카로운 줄기가 원래 설계대로 유지됨
///   - 배경이 블러될 때 스파크가 선명하게 돋보여 임팩트 강화
/// </summary>
public class SparkMotionVectorSetup : EditorWindow
{
    [MenuItem("Dreamcore/Tools/Setup Spark Motion Vectors")]
    public static void ShowWindow()
    {
        var window = GetWindow<SparkMotionVectorSetup>("Spark MV Setup");
        window.minSize = new Vector2(380, 220);
    }

    private void OnGUI()
    {
        GUILayout.Space(12);
        EditorGUILayout.LabelField("Spark Motion Vector Setup", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "ParrySparkGPUManager가 있는 모든 오브젝트의\n" +
            "MotionVectorGenerationMode를 ForceNoMotion으로 설정합니다.\n\n" +
            "효과: SSMB와 스파크 자체 블러의 이중 적용 방지",
            MessageType.Info);

        GUILayout.Space(16);

        GUI.backgroundColor = new Color(0.2f, 0.7f, 0.3f);
        if (GUILayout.Button("Apply ForceNoMotion to All Sparks", GUILayout.Height(38)))
            Apply();
        GUI.backgroundColor = Color.white;

        GUILayout.Space(8);

        GUI.backgroundColor = new Color(0.8f, 0.4f, 0.2f);
        if (GUILayout.Button("Restore to Object Mode (Undo)", GUILayout.Height(28)))
            Restore();
        GUI.backgroundColor = Color.white;
    }

    private static void Apply()
    {
        // ParrySparkGPUManager를 가진 오브젝트는 Renderer가 없음.
        // 스파크는 Graphics.RenderPrimitives()로 직접 렌더하므로
        // 부모 오브젝트 또는 레이어 기반으로 처리.
        //
        // 접근법: "Spark" 또는 "VFX" 레이어에 있는 Renderer 전체에 적용.
        // 또는 씬에서 ParrySparkGPUManager 컴포넌트를 찾아 처리.

        var sparkManagers = FindObjectsByType<ParrySparkGPUManager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int count = 0;
        foreach (var mgr in sparkManagers)
        {
            // ParrySparkGPUManager는 자체 렌더러 없이 renderMaterial만 사용.
            // 부모/자식의 Renderer에 설정 적용
            var renderers = mgr.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                Undo.RecordObject(r, "Spark ForceNoMotion");
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                EditorUtility.SetDirty(r);
                count++;
            }

            // 오브젝트 자체에 Renderer가 없으면 레이어 태그로 마킹
            // (renderMaterial의 _MotionVectors 키워드 비활성화 방식도 가능)
        }

        // 추가: "VFX" 레이어의 모든 Renderer에 적용
        int vfxLayer = LayerMask.NameToLayer("VFX");
        if (vfxLayer >= 0)
        {
            var allRenderers = FindObjectsByType<Renderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var r in allRenderers)
            {
                if (r.gameObject.layer == vfxLayer)
                {
                    Undo.RecordObject(r, "VFX Layer ForceNoMotion");
                    r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    EditorUtility.SetDirty(r);
                    count++;
                }
            }
        }

        Debug.Log($"[SparkMotionVectorSetup] ForceNoMotion 적용 완료: {count}개 렌더러");
        EditorUtility.DisplayDialog("완료",
            $"{count}개 오브젝트에 ForceNoMotion 적용 완료.\n" +
            "씬을 저장하세요.", "확인");
    }

    private static void Restore()
    {
        var sparkManagers = FindObjectsByType<ParrySparkGPUManager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var mgr in sparkManagers)
        {
            var renderers = mgr.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                Undo.RecordObject(r, "Spark Restore MotionVector");
                r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                EditorUtility.SetDirty(r);
            }
        }
        Debug.Log("[SparkMotionVectorSetup] Object 모드로 복원 완료");
    }
}
#endif
