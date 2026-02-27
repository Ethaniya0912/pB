#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using CaveSystem;
using System.Reflection;

namespace CaveSystem.Editor
{
    /// <summary>
    /// 에디터 씬 뷰(Scene View)에서 마우스를 지형에 가져다 대면 
    /// 해당 청크의 인덱스와 생성 상태를 실시간으로 보여주는 커스텀 디버깅 툴입니다.
    /// 단축키: Ctrl + G (또는 Cmd + G) 로 켜고 끌 수 있습니다.
    /// </summary>
    [InitializeOnLoad]
    public static class CaveChunkSceneProbe
    {
        private static bool isEnabled = false;

        static CaveChunkSceneProbe()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        [MenuItem("Cave System/Toggle Scene Probe %g")]
        public static void ToggleProbe()
        {
            isEnabled = !isEnabled;
            Debug.Log($"<color=cyan>[Cave Probe]</color> 마우스 프로브가 <b>{(isEnabled ? "켜졌습니다" : "꺼졌습니다")}</b>.");
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!isEnabled || !Application.isPlaying) return;

            CaveManager caveManager = CaveManager.Instance;
            if (caveManager == null || caveManager.chunkManager == null) return;

            Event e = Event.current;
            Vector2 mousePos = e.mousePosition;

            // DPI 스케일링 보정
            float ppp = EditorGUIUtility.pixelsPerPoint;
            mousePos.y = sceneView.camera.pixelHeight - mousePos.y * ppp;
            mousePos.x *= ppp;

            Ray ray = sceneView.camera.ScreenPointToRay(mousePos);

            // Chunk를 판별하기 위해 Raycast 실행
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                float chunkSize = caveManager.chunkManager.ChunkSize * caveManager.chunkManager.VoxelSize;

                // 마우스가 닿은 곳의 논리적 청크 좌표 계산
                Vector3Int chunkPos = new Vector3Int(
                    Mathf.FloorToInt(hit.point.x / chunkSize),
                    Mathf.FloorToInt(hit.point.y / chunkSize),
                    Mathf.FloorToInt(hit.point.z / chunkSize)
                );

                Vector3 center = new Vector3(chunkPos.x, chunkPos.y, chunkPos.z) * chunkSize + Vector3.one * chunkSize * 0.5f;

                // 1. 씬 뷰에 청크 바운딩 박스 그리기
                Handles.color = Color.cyan;
                Handles.DrawWireCube(center, Vector3.one * chunkSize);

                // 2. 마우스 커서 옆에 정보 툴팁 표시
                Handles.BeginGUI();

                // UI 배경 패널
                Rect tooltipRect = new Rect(e.mousePosition.x + 20, e.mousePosition.y + 20, 260, 90);
                EditorGUI.DrawRect(tooltipRect, new Color(0.1f, 0.1f, 0.15f, 0.9f));
                GUI.Box(tooltipRect, GUIContent.none, EditorStyles.helpBox);

                GUILayout.BeginArea(new Rect(tooltipRect.x + 10, tooltipRect.y + 10, tooltipRect.width - 20, tooltipRect.height - 20));

                GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.cyan } };
                GUIStyle infoStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.white } };

                GUILayout.Label($"Target Chunk: {chunkPos}", titleStyle);
                GUILayout.Label($"World Hit: {hit.point.x:F1}, {hit.point.y:F1}, {hit.point.z:F1}", infoStyle);

                // 리플렉션을 통해 CaveChunkManager의 private Dictionary 상태 읽기
                var field = typeof(CaveChunkManager).GetField("activeChunks", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (field != null)
                {
                    var dict = field.GetValue(caveManager.chunkManager) as System.Collections.IDictionary;
                    if (dict != null && dict.Contains(chunkPos))
                    {
                        var ctx = dict[chunkPos];
                        var stateField = ctx.GetType().GetField("State");
                        GUILayout.Label($"Status: <color=#00FF00><b>{stateField?.GetValue(ctx)}</b></color>", infoStyle);
                    }
                    else
                    {
                        GUILayout.Label("Status: <color=yellow>Culled / Inactive</color>", infoStyle);
                    }
                }

                GUILayout.EndArea();
                Handles.EndGUI();

                // 마우스 클릭 시 콘솔에 상세 정보 출력
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    Debug.Log($"<color=cyan>[Probe Info]</color> Chunk <b>{chunkPos}</b> | Hit Position: {hit.point} | Object: {hit.collider.gameObject.name}");
                    e.Use(); // 클릭 이벤트 소비 (에디터 선택 방지)
                }

                // 부드러운 UI 갱신을 위해 씬 뷰 리페인트 강제
                sceneView.Repaint();
            }
        }
    }
}
#endif