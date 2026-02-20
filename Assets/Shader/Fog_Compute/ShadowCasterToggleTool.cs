using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

/// <summary>
/// [Lead Architect Tool]
/// 씬에 존재하는 모든 MeshRenderer의 Cast Shadows 속성을 일괄적으로 켜고 끄는 에디터 툴입니다.
/// GPGPU 마이그레이션을 위해 필수적인 헬퍼 스크립트입니다.
/// (반드시 Assets/Editor 폴더 안에 위치해야 합니다.)
/// </summary>
public class ShadowCasterToggleTool : EditorWindow
{
    // 유니티 상단 메뉴에 버튼 추가
    [MenuItem("Dreamcore/Tools/Bulk Shadow Caster Toggle")]
    public static void ShowWindow()
    {
        // 윈도우 창 띄우기
        GetWindow<ShadowCasterToggleTool>("Shadow Caster Tool");
    }

    private void OnGUI()
    {
        GUILayout.Label("Bulk Shadow Caster Control", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("씬 내의 모든 MeshRenderer의 그림자 생성 속성을 일괄 변경합니다. 실행 후 Ctrl+Z(실행 취소)가 가능합니다.", MessageType.Info);

        EditorGUILayout.Space(10);

        // 끄기 버튼 (GPGPU 모드)
        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f); // 붉은색 톤
        if (GUILayout.Button("전체 끄기 (Turn OFF Cast Shadows)", GUILayout.Height(40)))
        {
            SetShadowCastingMode(ShadowCastingMode.Off);
        }

        EditorGUILayout.Space(5);

        // 켜기 버튼 (기존 래스터 모드 복구용)
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f); // 녹색 톤
        if (GUILayout.Button("전체 켜기 (Turn ON Cast Shadows)", GUILayout.Height(40)))
        {
            SetShadowCastingMode(ShadowCastingMode.On);
        }

        // 색상 초기화
        GUI.backgroundColor = Color.white;
    }

    private void SetShadowCastingMode(ShadowCastingMode mode)
    {
        // 씬 내의 모든 MeshRenderer 검색 (유니티 2023 이상 최신 API)
        MeshRenderer[] renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        int changedCount = 0;

        foreach (var renderer in renderers)
        {
            // 이미 목표 상태와 같다면 스킵
            if (renderer.shadowCastingMode == mode) continue;

            // [안전 장치] Ctrl+Z를 눌러 되돌릴 수 있도록 Undo 레코드 등록
            Undo.RecordObject(renderer, "Bulk Toggle Shadow Casting");

            renderer.shadowCastingMode = mode;

            // 씬에 변경사항이 생겼음을 엔진에 알림 (저장 시 반영되도록)
            EditorUtility.SetDirty(renderer);
            changedCount++;
        }

        // 결과 로그 출력
        Debug.Log($"<color=cyan>[ShadowCasterTool]</color> 총 <b>{changedCount}</b>개 MeshRenderer의 Cast Shadows 속성이 <b>{mode}</b>(으)로 변경되었습니다.");
    }
}