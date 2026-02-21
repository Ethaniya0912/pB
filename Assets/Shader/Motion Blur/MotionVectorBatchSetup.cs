#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// [Lead Architect] 
/// 특정 레이어에 속한 모든 MeshRenderer와 SkinnedMeshRenderer의 
/// Motion Vector 옵션을 일괄 변경하는 커스텀 에디터 윈도우입니다.
/// </summary>
public class MotionVectorBatchSetup : EditorWindow
{
    private int selectedLayer = 0;
    private MotionVectorGenerationMode targetMode = MotionVectorGenerationMode.ForceNoMotion;

    [MenuItem("Dreamcore/Tools/Motion Vector Batch Setup")]
    public static void ShowWindow()
    {
        EditorWindow window = GetWindow<MotionVectorBatchSetup>("Motion Vector Setup");
        window.minSize = new Vector2(350, 200);
        window.maxSize = new Vector2(400, 250);
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        EditorGUILayout.LabelField("Motion Vector Batch Control", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("선택한 레이어의 모든 렌더러에 모션 벡터(Motion Vector) 설정을 강제로 적용합니다.\n(적군/특정 오브젝트 타겟팅 모션 블러 트릭용)", MessageType.Info);

        GUILayout.Space(15);

        // 레이어 선택 드롭다운 (유니티 내장 LayerMask 인터페이스 사용)
        selectedLayer = EditorGUILayout.LayerField("Target Layer", selectedLayer);

        GUILayout.Space(10);

        // 모션 벡터 모드 선택 드롭다운
        targetMode = (MotionVectorGenerationMode)EditorGUILayout.EnumPopup("Target Mode", targetMode);

        GUILayout.Space(20);

        // 일괄 적용 버튼
        GUI.backgroundColor = new Color(0.2f, 0.6f, 1f); // 파란색 버튼 강조
        if (GUILayout.Button("Apply to All in Layer", GUILayout.Height(35)))
        {
            ApplyMotionVectorSettings();
        }
        GUI.backgroundColor = Color.white;
    }

    private void ApplyMotionVectorSettings()
    {
        // 1. 씬에 있는 모든 렌더러 찾기 (비활성화된 오브젝트 포함)
        Renderer[] allRenderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int changedCount = 0;

        // Undo(실행 취소) 기록 시작
        Undo.RecordObjects(allRenderers, "Batch Motion Vector Setup");

        foreach (Renderer r in allRenderers)
        {
            // 대상 레이어와 일치하는지 확인
            if (r.gameObject.layer == selectedLayer)
            {
                // MeshRenderer와 SkinnedMeshRenderer만 타겟팅 (파티클 등은 제외)
                if (r is MeshRenderer || r is SkinnedMeshRenderer)
                {
                    if (r.motionVectorGenerationMode != targetMode)
                    {
                        r.motionVectorGenerationMode = targetMode;

                        // 변경된 오브젝트가 프리팹 인스턴스일 경우 변경점 기록(Dirty) 
                        EditorUtility.SetDirty(r);
                        changedCount++;
                    }
                }
            }
        }

        // 결과 알림
        if (changedCount > 0)
        {
            Debug.Log($"<b>[Motion Vector Setup]</b> {LayerMask.LayerToName(selectedLayer)} 레이어의 렌더러 {changedCount}개의 옵션이 {targetMode}(으)로 변경되었습니다.");

            // 에디터 상에서 씬 저장 필요성 알림
            if (!Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }
        }
        else
        {
            Debug.Log($"<b>[Motion Vector Setup]</b> 변경할 대상이 없거나 이미 모든 렌더러가 {targetMode} 상태입니다.");
        }
    }
}
#endif