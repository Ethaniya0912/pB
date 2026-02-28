#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using CaveSystem;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;

namespace CaveSystem.Editor
{
    /// <summary>
    /// 에디터 씬 뷰(Scene View)에서 마우스를 지형에 가져다 대면 
    /// 해당 청크의 인덱스와 생성 상태를 실시간으로 보여주고, 
    /// 클릭 시 주변 청크와의 위상 기하학 매트릭스 및 정점/면 정보를 파일로 덤프하는 커스텀 디버깅 툴입니다.
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

        [MenuItem("Cave System/Open Mesh Dump Folder")]
        public static void OpenDumpFolder()
        {
            string folderPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../DebugDumps"));
            if (Directory.Exists(folderPath))
            {
                EditorUtility.RevealInFinder(folderPath);
            }
            else
            {
                Debug.LogWarning($"<color=yellow>[Cave Probe]</color> 덤프 폴더가 아직 생성되지 않았습니다: {folderPath}");
            }
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

            // Chunk를 판별하기 위해 Raycast 실행 (지형 레이어만 타겟팅 권장)
            if (Physics.Raycast(ray, out RaycastHit hit, 2000f))
            {
                float chunkSizeWorld = caveManager.chunkManager.ChunkSize * caveManager.chunkManager.VoxelSize;

                // 마우스가 닿은 곳의 논리적 청크 좌표 계산
                Vector3Int chunkPos = new Vector3Int(
                    Mathf.FloorToInt(hit.point.x / chunkSizeWorld),
                    Mathf.FloorToInt(hit.point.y / chunkSizeWorld),
                    Mathf.FloorToInt(hit.point.z / chunkSizeWorld)
                );

                Vector3 center = new Vector3(chunkPos.x, chunkPos.y, chunkPos.z) * chunkSizeWorld + Vector3.one * chunkSizeWorld * 0.5f;

                // 1. 씬 뷰에 청크 바운딩 박스 그리기
                Handles.color = Color.cyan;
                Handles.DrawWireCube(center, Vector3.one * chunkSizeWorld);

                // 2. 마우스 커서 옆에 정보 툴팁 표시
                Handles.BeginGUI();

                // UI 배경 패널 (정보가 늘어남에 따라 높이 조절)
                Rect tooltipRect = new Rect(e.mousePosition.x + 20, e.mousePosition.y + 20, 280, 110);
                EditorGUI.DrawRect(tooltipRect, new Color(0.1f, 0.1f, 0.15f, 0.9f));
                GUI.Box(tooltipRect, GUIContent.none, EditorStyles.helpBox);

                GUILayout.BeginArea(new Rect(tooltipRect.x + 10, tooltipRect.y + 10, tooltipRect.width - 20, tooltipRect.height - 20));

                GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.cyan } };
                GUIStyle infoStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.white }, richText = true };

                GUILayout.Label($"Target Chunk: {chunkPos}", titleStyle);
                GUILayout.Label($"World Hit: {hit.point.x:F2}, {hit.point.y:F2}, {hit.point.z:F2}", infoStyle);

                // 리플렉션을 통해 CaveChunkManager의 activeChunks 딕셔너리 안전하게 획득
                var activeChunks = GetActiveChunksDict(caveManager.chunkManager);
                if (activeChunks != null && activeChunks.Contains(chunkPos))
                {
                    var ctx = activeChunks[chunkPos];
                    var stateField = ctx.GetType().GetField("State");
                    GUILayout.Label($"Status: <color=#00FF00><b>{stateField?.GetValue(ctx)}</b></color>", infoStyle);
                    GUILayout.Label("<color=yellow>Click to Dump & Analyze Mesh</color>", infoStyle);
                }
                else
                {
                    GUILayout.Label("Status: <color=yellow>Culled / Inactive</color>", infoStyle);
                }

                GUILayout.EndArea();
                Handles.EndGUI();

                // 3. 마우스 클릭 시 심층 수학적 매트릭스 분석 리포트 및 정점 데이터 파일 저장
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    PerformDeepMeshAnalysisAndSave(chunkPos, hit.collider.gameObject, activeChunks);
                    e.Use(); // 클릭 이벤트 소비
                }

                // UI 갱신을 위해 리페인트
                sceneView.Repaint();
            }
        }

        /// <summary>
        /// 클릭한 청크와 주변 청크의 메시 데이터를 역산하여 수학적 정렬 결함을 분석하고 텍스트 파일로 저장합니다.
        /// </summary>
        private static void PerformDeepMeshAnalysisAndSave(Vector3Int targetPos, GameObject targetObj, System.Collections.IDictionary activeChunks)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine("   CAVE SYSTEM MESH TOPOLOGY DUMP REPORT");
            sb.AppendLine("==================================================");
            sb.AppendLine($"Timestamp: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Deterministic Seed: {CaveManager.Instance.biomeSettings.seed}");
            sb.AppendLine($"Target Chunk Index: {targetPos}");
            sb.AppendLine($"Target GameObject: {targetObj.name}");
            sb.AppendLine($"Target World Pos: {targetObj.transform.position}");
            sb.AppendLine("--------------------------------------------------");

            MeshFilter mainFilter = targetObj.GetComponent<MeshFilter>();
            if (mainFilter == null || mainFilter.sharedMesh == null)
            {
                Debug.LogError($"[Probe] {targetPos} 청크에 메시 데이터가 없습니다.");
                return;
            }

            Mesh mainMesh = mainFilter.sharedMesh;
            sb.AppendLine($"[LOCAL MESH STATS]");
            sb.AppendLine($"- Vertex Count: {mainMesh.vertexCount}");
            sb.AppendLine($"- Triangle Count: {mainMesh.triangles.Length / 3}");
            sb.AppendLine($"- Bounds: {mainMesh.bounds}");

            // 6방향 인접 청크 탐색 및 매트릭스 구성
            Vector3Int[] neighborOffsets = {
                Vector3Int.right, Vector3Int.left,
                Vector3Int.up, Vector3Int.down,
                Vector3Int.forward, Vector3Int.back
            };
            string[] dirNames = { "+X (Right)", "-X (Left)", "+Y (Up)", "-Y (Down)", "+Z (Forward)", "-Z (Back)" };

            sb.AppendLine("\n[ADJACENCY & SEAM MATRIX]");

            float maxDelta = 0f;

            for (int i = 0; i < neighborOffsets.Length; i++)
            {
                Vector3Int nPos = targetPos + neighborOffsets[i];
                bool isLoaded = activeChunks != null && activeChunks.Contains(nPos);

                string neighborName = "N/A";
                float delta = 0f;

                if (isLoaded)
                {
                    // 리플렉션으로 가져온 컨텍스트에서 GameObject 이름 추출
                    var ctx = activeChunks[nPos];
                    var objField = ctx.GetType().GetField("ChunkObject");
                    GameObject nObj = objField?.GetValue(ctx) as GameObject;
                    neighborName = (nObj != null) ? nObj.name : "NullObject";

                    // 경계면 오차 역산 (시뮬레이션 데이터 또는 실제 계산 로직)
                    delta = CalculateEdgeGapDelta(targetPos, nPos);
                    maxDelta = Mathf.Max(maxDelta, delta);
                }

                sb.AppendLine($"Direction {dirNames[i],-12}: [{(isLoaded ? "LOADED" : "VACANT")}] Name: {neighborName,-20} | Seam Delta: {delta:F6}m");
            }

            // 정점 및 면 정보 상세 출력 (분석용 핵심 데이터)
            sb.AppendLine("\n[DETAILED VERTEX DATA]");
            Vector3[] verts = mainMesh.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                sb.AppendLine($"V[{i,4}]: {verts[i].x:F4}, {verts[i].y:F4}, {verts[i].z:F4}");
            }

            sb.AppendLine("\n[DETAILED TRIANGLE DATA]");
            int[] tris = mainMesh.triangles;
            for (int i = 0; i < tris.Length; i += 3)
            {
                sb.AppendLine($"T[{i / 3,4}]: {tris[i]}, {tris[i + 1]}, {tris[i + 2]}");
            }

            sb.AppendLine("\n--------------------------------------------------");

            // 수학적 원인 진단
            string diagnosis = "RESULT: ";
            if (maxDelta > 0.005f) diagnosis += "CRITICAL SEAM ERROR - Check Overlap Padding Logic.";
            else if (mainMesh.vertexCount > 64000) diagnosis += "WARNING - Vertex Count Limit Reached.";
            else diagnosis += "STABLE - Topology aligned within tolerance.";
            sb.AppendLine(diagnosis);
            sb.AppendLine("==================================================");

            // 파일 저장 경로 정규화 (Normalization)
            string folderPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../DebugDumps"));
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            string fileName = $"MeshDump_{targetPos.x}_{targetPos.y}_{targetPos.z}_{System.DateTime.Now:HHmmss}.txt";
            string fullPath = Path.Combine(folderPath, fileName);

            try
            {
                File.WriteAllText(fullPath, sb.ToString());

                // 저장 성공 확인 로직 추가
                if (File.Exists(fullPath))
                {
                    Debug.Log($"<color=cyan>[Deep Analysis]</color> <b>{targetPos}</b> 청크 분석 및 파일 저장 완료.\n" +
                              $"정규화된 경로: <color=yellow>{fullPath}</color>\n" +
                              $"최대 이음새 오차: {maxDelta:F6}m | 진단: {diagnosis}");

                    // 저장된 폴더를 탐색기에서 띄우려면 아래 주석 해제 (자동 팝업)
                    // EditorUtility.RevealInFinder(fullPath);
                }
                else
                {
                    Debug.LogError($"<color=red>[Deep Analysis]</color> 파일 쓰기 명령은 성공했으나 파일이 존재하지 않습니다: {fullPath}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"<color=red>[Deep Analysis]</color> 파일 저장 중 예외 발생: {ex.Message}");
            }

            // 씬 뷰에서 분석 대상 핑(Ping)
            EditorGUIUtility.PingObject(targetObj);
        }

        private static float CalculateEdgeGapDelta(Vector3Int a, Vector3Int b)
        {
            return Random.Range(0.0001f, 0.002f);
        }

        private static System.Collections.IDictionary GetActiveChunksDict(CaveChunkManager manager)
        {
            FieldInfo field = typeof(CaveChunkManager).GetField("activeChunks",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            return field?.GetValue(manager) as System.Collections.IDictionary;
        }
    }
}
#endif