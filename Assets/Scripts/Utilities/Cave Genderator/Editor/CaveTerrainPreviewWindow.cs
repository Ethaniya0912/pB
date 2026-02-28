#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;

namespace CaveSystem.Editor
{
    /// <summary>
    /// [Part 5 완성본] 씬 뷰에서 노드 그래프를 직접 설계하고 
    /// 실시간으로 지형 생성을 프리뷰(WYSIWYG)하는 고도화된 에디터 툴입니다.
    /// </summary>
    public class CaveTerrainPreviewWindow : EditorWindow
    {
        [Header("System References")]
        private ComputeShader densityShader;
        private ComputeShader marchingCubesShader;
        private CaveBiomeSettings biomeSettings;
        private Material previewMaterial;
        private CaveNodeGraphBuilder tempGraph;

        [Header("Preview Settings")]
        private int chunkSize = 32;
        private float voxelSize = 0.5f;
        private Vector3Int targetChunkPos = Vector3Int.zero; // [에러 수정: 누락된 변수 추가]
        private int currentDebugStage = 4;

        [Header("UI Control")]
        private int currentTab = 0;
        private readonly string[] tabNames = { "1. Graph Editor", "2. Shape & Biome", "3. Ecosystem", "4. Analytics & Presets" };

        // [에러 수정: 누락된 리스트 변수 추가]
        // 노드 그래프 조작을 위한 내부 편집용 데이터
        private List<NodeData> editableNodes = new List<NodeData>();
        private List<EdgeData> editableEdges = new List<EdgeData>();

        private int selectedNodeIndex = -1;
        private int linkingStartIndex = -1;
        private Vector3 currentMouseWorldPos;

        // GPU 메모리 상태
        private ComputeBuffer nodeBuffer;
        private ComputeBuffer edgeBuffer;
        private ComputeBuffer biomeBuffer;
        private ComputeBuffer voxelBuffer;
        private ComputeBuffer triangleBuffer;
        private ComputeBuffer oreBuffer;
        private ComputeBuffer triCountBuffer;

        // 렌더링 상태
        private Mesh previewMesh;
        private bool isAwaitingReadback = false;
        private AsyncGPUReadbackRequest readbackRequest;
        private CaveAnalyticsData currentAnalytics;

        [MenuItem("Cave System/Liminal Cave Previewer")]
        public static void ShowWindow()
        {
            var window = GetWindow<CaveTerrainPreviewWindow>("Cave Preview");
            window.minSize = new Vector2(500, 750);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += EditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += SafeReleaseComputeBuffers;

            previewMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            previewMesh.MarkDynamic();

            LoadDefaultAssets();
            SyncWithSceneGraph();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= EditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload -= SafeReleaseComputeBuffers;

            SafeReleaseComputeBuffers();
            if (previewMesh != null) DestroyImmediate(previewMesh);
        }

        private void LoadDefaultAssets()
        {
            // 기본 경로 에셋 로드 시도
            if (densityShader == null) densityShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Scripts/Utilities/Cave Genderator/CaveDensityGenerator.compute");
            if (marchingCubesShader == null) marchingCubesShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Scripts/Utilities/Cave Genderator/CaveMarchingCubes.compute");
            if (previewMaterial == null) previewMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/CavePreviewMat.mat");
            if (biomeSettings == null) biomeSettings = AssetDatabase.LoadAssetAtPath<CaveBiomeSettings>("Assets/Settings/CaveBiomeSettings.asset");

            tempGraph = FindFirstObjectByType<CaveNodeGraphBuilder>();
        }

        private void SyncWithSceneGraph()
        {
            if (tempGraph != null && tempGraph.nodesData != null)
            {
                editableNodes = new List<NodeData>(tempGraph.nodesData);
                editableEdges = new List<EdgeData>(tempGraph.edgesData);
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("🛠️ Advanced Cave WYSIWYG Editor", new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, alignment = TextAnchor.MiddleCenter });
            EditorGUILayout.Space(10);

            int prevTab = currentTab;
            currentTab = GUILayout.Toolbar(currentTab, tabNames, GUILayout.Height(30));
            if (prevTab != currentTab) SyncDebugStageWithTab();

            EditorGUILayout.Space(10);

            // 필수 참조 검사
            if (densityShader == null || marchingCubesShader == null || biomeSettings == null)
            {
                EditorGUILayout.HelpBox("에셋 참조가 누락되었습니다. 아래 슬롯에 할당해주세요.", MessageType.Error);
                densityShader = (ComputeShader)EditorGUILayout.ObjectField("Density CS", densityShader, typeof(ComputeShader), false);
                marchingCubesShader = (ComputeShader)EditorGUILayout.ObjectField("Marching Cubes CS", marchingCubesShader, typeof(ComputeShader), false);
                biomeSettings = (CaveBiomeSettings)EditorGUILayout.ObjectField("Biome Settings", biomeSettings, typeof(CaveBiomeSettings), false);
                previewMaterial = (Material)EditorGUILayout.ObjectField("Material", previewMaterial, typeof(Material), false);
                return;
            }

            // 스크롤 뷰 시작
            Vector2 scrollPos = Vector2.zero;
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            switch (currentTab)
            {
                case 0: DrawGraphEditorTab(); break;
                case 1: DrawShapeBiomeTab(); break;
                case 2: DrawEcosystemTab(); break;
                case 3: DrawAnalyticsTab(); break;
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);
            GUI.backgroundColor = isAwaitingReadback ? Color.gray : new Color(0.2f, 0.8f, 0.4f);
            if (GUILayout.Button(isAwaitingReadback ? "⏳ Processing GPU..." : "🚀 Force Generate Preview", GUILayout.Height(45)))
            {
                TriggerInstantPreview();
            }
            GUI.backgroundColor = Color.white;
        }

        private void SyncDebugStageWithTab()
        {
            switch (currentTab)
            {
                case 0: currentDebugStage = 0; break; // Raw Skeleton
                case 1: currentDebugStage = 3; break; // Biome Details
                case 2:
                case 3: currentDebugStage = 4; break; // Final Result
            }
            TriggerInstantPreview();
        }

        #region Tab Drawing

        private void DrawGraphEditorTab()
        {
            EditorGUILayout.LabelField("Dungeon Skeleton Design", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Shift+Click: 생성 | Delete: 삭제 | Ctrl+Drag: 연결", MessageType.Info);

            EditorGUILayout.Space(5);
            chunkSize = EditorGUILayout.IntSlider("Preview Resolution", chunkSize, 16, 64);
            voxelSize = EditorGUILayout.Slider("Voxel Accuracy", voxelSize, 0.25f, 1.0f);
            targetChunkPos = EditorGUILayout.Vector3IntField("Target Chunk Index", targetChunkPos);

            if (GUILayout.Button("Apply Edits to Scene Graph", GUILayout.Height(25)))
            {
                if (tempGraph != null)
                {
                    Undo.RecordObject(tempGraph, "Apply Preview Edits");
                    tempGraph.nodesData = new List<NodeData>(editableNodes);
                    tempGraph.edgesData = new List<EdgeData>(editableEdges);
                    EditorUtility.SetDirty(tempGraph);
                    Debug.Log("<color=green>[Cave Preview]</color> 씬 오브젝트로 데이터 전송 완료.");
                }
            }
        }

        private void DrawShapeBiomeTab()
        {
            EditorGUILayout.LabelField("Biome & Geometry Tunning", EditorStyles.boldLabel);
            if (biomeSettings.depthLayers.Count > 0)
            {
                var layer = biomeSettings.depthLayers[0];
                EditorGUI.BeginChangeCheck();
                layer.sdfSmoothness = EditorGUILayout.Slider("SDF Smoothness (smin)", layer.sdfSmoothness, 0.1f, 10f);
                layer.floorBlendRadius = EditorGUILayout.Slider("Floor Fillet (smax)", layer.floorBlendRadius, 0.1f, 10f);
                layer.noiseFrequency = EditorGUILayout.Slider("Noise Frequency", layer.noiseFrequency, 0.01f, 0.2f);
                if (EditorGUI.EndChangeCheck())
                {
                    biomeSettings.depthLayers[0] = layer;
                    EditorUtility.SetDirty(biomeSettings);
                    TriggerInstantPreview();
                }
            }
        }

        private void DrawEcosystemTab()
        {
            EditorGUILayout.LabelField("Ecosystem & Special Points", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("생성 완료 후 씬 뷰에서 기즈모 오버레이를 확인하세요.", MessageType.Info);
        }

        private void DrawAnalyticsTab()
        {
            EditorGUILayout.LabelField("📊 Generation Analytics", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Total Polygons:", $"{currentAnalytics.totalTriangles:N0}");
            EditorGUILayout.LabelField("Total Nodes:", $"{editableNodes.Count}");
            EditorGUILayout.LabelField("Total Edges:", $"{editableEdges.Count}");
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(20);
            if (GUILayout.Button("💾 Save Current Config as Preset Asset"))
            {
                SaveCurrentGraphAsPreset();
            }
        }

        private void SaveCurrentGraphAsPreset()
        {
            CavePresetData newPreset = ScriptableObject.CreateInstance<CavePresetData>();
            newPreset.savedNodes = new List<NodeData>(editableNodes);
            newPreset.savedEdges = new List<EdgeData>(editableEdges);
            newPreset.analyticsInfo = currentAnalytics;

            string path = EditorUtility.SaveFilePanelInProject("Save Cave Preset", "NewPreset", "asset", "프리셋 저장 경로 선택");
            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.CreateAsset(newPreset, path);
                AssetDatabase.SaveAssets();
            }
        }

        #endregion

        #region Scene Interaction

        private void OnSceneGUI(SceneView sceneView)
        {
            Event e = Event.current;
            bool hasChanged = false;

            // 마우스 월드 위치 추적
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit)) currentMouseWorldPos = hit.point;
            else currentMouseWorldPos = ray.origin + ray.direction * 20f;

            // 1. 노드 추가 (Shift + Click)
            if (currentTab == 0 && e.shift && e.type == EventType.MouseDown && e.button == 0)
            {
                editableNodes.Add(new NodeData { position = currentMouseWorldPos, radius = 10f, roomType = 0 });
                hasChanged = true;
                e.Use();
            }

            // 2. 노드 삭제 (Delete)
            if (currentTab == 0 && selectedNodeIndex != -1 && e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete)
            {
                Vector3 posToRemove = editableNodes[selectedNodeIndex].position;
                editableEdges.RemoveAll(edge => edge.startPos == posToRemove || edge.endPos == posToRemove);
                editableNodes.RemoveAt(selectedNodeIndex);
                selectedNodeIndex = -1;
                hasChanged = true;
                e.Use();
            }

            // 3. 노드 연결 (Ctrl + Drag)
            if (currentTab == 0 && e.control)
            {
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    linkingStartIndex = GetHoveredNodeIndex(e.mousePosition);
                    if (linkingStartIndex != -1) e.Use();
                }
                else if (e.type == EventType.MouseUp && e.button == 0 && linkingStartIndex != -1)
                {
                    int endIndex = GetHoveredNodeIndex(e.mousePosition);
                    if (endIndex != -1 && endIndex != linkingStartIndex)
                    {
                        editableEdges.Add(new EdgeData
                        {
                            startPos = editableNodes[linkingStartIndex].position,
                            endPos = editableNodes[endIndex].position,
                            width = 5f
                        });
                        hasChanged = true;
                    }
                    linkingStartIndex = -1;
                    e.Use();
                }
            }

            // 드래그 시 가이드 라인
            if (linkingStartIndex != -1)
            {
                Handles.color = Color.yellow;
                Handles.DrawDottedLine(editableNodes[linkingStartIndex].position, currentMouseWorldPos, 5f);
                sceneView.Repaint();
            }

            // 노드 및 엣지 핸들 그리기
            DrawSceneHandles(ref hasChanged);

            // 프리뷰 메쉬 렌더링
            if (previewMesh != null && previewMaterial != null)
            {
                Graphics.DrawMesh(previewMesh, Matrix4x4.identity, previewMaterial, 0, sceneView.camera);
            }

            if (hasChanged && !isAwaitingReadback) TriggerInstantPreview();
        }

        private void DrawSceneHandles(ref bool hasChanged)
        {
            // Edge 렌더링
            for (int i = 0; i < editableEdges.Count; i++)
            {
                Handles.color = new Color(0.3f, 0.5f, 1f, 0.6f);
                Handles.DrawAAPolyLine(6f, editableEdges[i].startPos, editableEdges[i].endPos);
            }

            // Node 핸들
            for (int i = 0; i < editableNodes.Count; i++)
            {
                NodeData node = editableNodes[i];
                Handles.color = (i == selectedNodeIndex) ? Color.yellow : Color.white;

                if (Handles.Button(node.position, Quaternion.identity, 1.5f, 2f, Handles.SphereHandleCap))
                {
                    selectedNodeIndex = i;
                    Repaint();
                }

                if (i == selectedNodeIndex)
                {
                    Vector3 newPos = Handles.PositionHandle(node.position, Quaternion.identity);
                    if (Vector3.Distance(newPos, node.position) > 0.05f)
                    {
                        UpdateConnectedEdges(node.position, newPos);
                        node.position = newPos;
                        editableNodes[i] = node;
                        hasChanged = true;
                    }

                    float newRadius = Handles.RadiusHandle(Quaternion.identity, node.position, node.radius);
                    if (Mathf.Abs(newRadius - node.radius) > 0.1f)
                    {
                        node.radius = newRadius;
                        editableNodes[i] = node;
                        hasChanged = true;
                    }
                }
            }
        }

        private void UpdateConnectedEdges(Vector3 oldPos, Vector3 newPos)
        {
            for (int i = 0; i < editableEdges.Count; i++)
            {
                var edge = editableEdges[i];
                if (edge.startPos == oldPos) edge.startPos = newPos;
                if (edge.endPos == oldPos) edge.endPos = newPos;
                editableEdges[i] = edge;
            }
        }

        private int GetHoveredNodeIndex(Vector2 mousePos)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePos);
            for (int i = 0; i < editableNodes.Count; i++)
            {
                float dist = Vector3.Cross(ray.direction, editableNodes[i].position - ray.origin).magnitude;
                if (dist < editableNodes[i].radius) return i;
            }
            return -1;
        }

        #endregion

        #region GPU Pipeline

        private void TriggerInstantPreview()
        {
            if (isAwaitingReadback || editableNodes.Count == 0) return;
            isAwaitingReadback = true;

            int pointsPerAxis = chunkSize + 1;
            int voxelCount = pointsPerAxis * pointsPerAxis * pointsPerAxis;

            // 버퍼 할당
            if (voxelBuffer == null || voxelBuffer.count != voxelCount)
            {
                SafeReleaseComputeBuffers();
                nodeBuffer = new ComputeBuffer(Mathf.Max(1, editableNodes.Count), Marshal.SizeOf(typeof(NodeData)));
                edgeBuffer = new ComputeBuffer(Mathf.Max(1, editableEdges.Count), Marshal.SizeOf(typeof(EdgeData)));
                biomeBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(BiomeParamData)));
                voxelBuffer = new ComputeBuffer(voxelCount, Marshal.SizeOf(typeof(CaveVoxel)));
                triangleBuffer = new ComputeBuffer(chunkSize * chunkSize * chunkSize * 5, Marshal.SizeOf(typeof(CaveTriangle)), ComputeBufferType.Append);
                triCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
            }

            nodeBuffer.SetData(editableNodes);
            if (editableEdges.Count > 0) edgeBuffer.SetData(editableEdges);

            // 바이옴 데이터 (첫 번째 레이어 기준)
            if (biomeSettings.depthLayers.Count > 0)
            {
                var layer = biomeSettings.depthLayers[0];
                BiomeParamData bData = new BiomeParamData
                {
                    noiseFrequency = layer.noiseFrequency,
                    sminStrength = layer.sdfSmoothness,
                    bumpAmplitude = layer.floorBumpAmplitude,
                    bumpFrequency = layer.floorBumpFrequency
                };
                biomeBuffer.SetData(new BiomeParamData[] { bData });
            }

            // Dispatch Density
            int kD = densityShader.FindKernel("GenerateDensity");
            Vector3 worldBase = (Vector3)targetChunkPos * (chunkSize * voxelSize);

            densityShader.SetBuffer(kD, "_VoxelBuffer", voxelBuffer);
            densityShader.SetBuffer(kD, "_NodeBuffer", nodeBuffer);
            densityShader.SetBuffer(kD, "_EdgeBuffer", edgeBuffer);
            densityShader.SetBuffer(kD, "_BiomeBuffer", biomeBuffer);
            densityShader.SetInt("_NodeCount", editableNodes.Count);
            densityShader.SetInt("_EdgeCount", editableEdges.Count);
            densityShader.SetInt("_BiomeCount", 1);
            densityShader.SetVector("_ChunkBasePosition", worldBase);
            densityShader.SetInt("_ChunkSize", chunkSize);
            densityShader.SetInt("_PointsPerAxis", pointsPerAxis);
            densityShader.SetFloat("_VoxelSize", voxelSize);
            densityShader.SetInt("_DebugStage", currentDebugStage);

            int group3D = Mathf.CeilToInt(pointsPerAxis / 8.0f);
            densityShader.Dispatch(kD, group3D, group3D, group3D);

            // Dispatch Marching Cubes
            int kM = marchingCubesShader.FindKernel("GenerateMesh");
            triangleBuffer.SetCounterValue(0);

            marchingCubesShader.SetBuffer(kM, "_VoxelBuffer", voxelBuffer);
            marchingCubesShader.SetBuffer(kM, "_TriangleBuffer", triangleBuffer);
            marchingCubesShader.SetVector("_ChunkBasePosition", worldBase);
            marchingCubesShader.SetInt("_ChunkSize", chunkSize);
            marchingCubesShader.SetInt("_PointsPerAxis", pointsPerAxis);
            marchingCubesShader.SetFloat("_VoxelSize", voxelSize);
            marchingCubesShader.SetFloat("_IsoLevel", 0.0f);

            int groupMC = Mathf.CeilToInt(chunkSize / 8.0f);
            marchingCubesShader.Dispatch(kM, groupMC, groupMC, groupMC);

            ComputeBuffer.CopyCount(triangleBuffer, triCountBuffer, 0);
            readbackRequest = AsyncGPUReadback.Request(triCountBuffer);
        }

        private void EditorUpdate()
        {
            if (isAwaitingReadback && readbackRequest.done)
            {
                isAwaitingReadback = false;
                if (readbackRequest.hasError) return;

                int triCount = readbackRequest.GetData<int>()[0];
                currentAnalytics.totalTriangles = triCount;

                if (triCount > 0)
                {
                    CaveTriangle[] triData = new CaveTriangle[triCount];
                    triangleBuffer.GetData(triData);
                    UpdateMesh(triData, triCount);
                }
                else previewMesh.Clear();

                Repaint();
                SceneView.RepaintAll();
            }
        }

        private void UpdateMesh(CaveTriangle[] triData, int triCount)
        {
            int vCount = triCount * 3;
            Vector3[] v = new Vector3[vCount];
            Vector3[] n = new Vector3[vCount];
            int[] tris = new int[vCount];

            for (int i = 0; i < triCount; i++)
            {
                v[i * 3 + 0] = triData[i].v0.position; n[i * 3 + 0] = triData[i].v0.normal;
                v[i * 3 + 1] = triData[i].v1.position; n[i * 3 + 1] = triData[i].v1.normal;
                v[i * 3 + 2] = triData[i].v2.position; n[i * 3 + 2] = triData[i].v2.normal;
                tris[i * 3 + 0] = i * 3 + 0; tris[i * 3 + 1] = i * 3 + 1; tris[i * 3 + 2] = i * 3 + 2;
            }

            previewMesh.Clear();
            previewMesh.SetVertices(v);
            previewMesh.SetNormals(n);
            previewMesh.SetIndices(tris, MeshTopology.Triangles, 0);
            previewMesh.RecalculateBounds();
        }

        private void SafeReleaseComputeBuffers()
        {
            if (isAwaitingReadback && !readbackRequest.done) readbackRequest.WaitForCompletion();

            nodeBuffer?.Release(); nodeBuffer = null;
            edgeBuffer?.Release(); edgeBuffer = null;
            biomeBuffer?.Release(); biomeBuffer = null;
            voxelBuffer?.Release(); voxelBuffer = null;
            triangleBuffer?.Release(); triangleBuffer = null;
            triCountBuffer?.Release(); triCountBuffer = null;
            oreBuffer?.Release(); oreBuffer = null;
        }

        #endregion
    }
}
#endif