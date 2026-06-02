// =============================================================================
// FactionSpawnControlWindow.cs (★ E-07 v2.2 — CS1529 정정 + 4 level-up)
// Custom EditorWindow — Spawn/Despawn + NavMesh 시각 + 클릭-스폰 + 멤버 박스
// Layer: Editor Only (Assets/.../Editor/ 측 배치 자료)
// Namespace: TDA.PB4.EditorTools
// =============================================================================
//
// v2.2 변경 (v2.1 측):
//   - Level-up 1: EditorPrefs 측 설정 영속 (재기동 측 유지)
//   - Level-up 2: NavMesh sample radius 슬라이더 (1m ~ 10m)
//   - Level-up 3: 선택 그룹 측 멤버 목록 + Frame 버튼 (Scene zoom)
//   - Level-up 4: 전체 Faction 상태 Dump (Console)
//   - 용어 정리: 금지 단어 일괄 제거
//
// 사용:
//   pB-4 → Faction Spawn Control (Ctrl+Shift+F)
//   1. 좌측 Faction 선택
//   2. Count + Persistent + NavMesh Snap 정합
//   3. [▶ Begin Placement] → 미니맵 측 NavMesh 위 좌클릭 → 자동 스폰
//   4. 미니맵 측 그룹 박스 클릭 → 우측 패널 측 갱신 (멤버 목록 포함)
//   5. 우측 Force 버튼 측 상태 제어
// =============================================================================

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using TDA.PB4.AI;
using TDA.PB4.Faction;
using TDA.PB4.Faction.Tags;
using TDA.PB4.Data;

namespace TDA.PB4.EditorTools
{
    public class FactionSpawnControlWindow : EditorWindow
    {
        // ──────────────────────────────────────────────────────────────────
        // 메뉴 진입
        // ──────────────────────────────────────────────────────────────────

        [MenuItem("pB-4/Faction Spawn Control %#f")]   // Ctrl+Shift+F
        public static void Open()
        {
            var w = GetWindow<FactionSpawnControlWindow>("Faction Spawn");
            w.minSize = new Vector2(1000, 650);
        }

        // ──────────────────────────────────────────────────────────────────
        // EditorPrefs 키 (Level-up 1)
        // ──────────────────────────────────────────────────────────────────

        private const string PREFS = "TDA.PB4.FactionSpawn.";

        // ──────────────────────────────────────────────────────────────────
        // 상태
        // ──────────────────────────────────────────────────────────────────

        // 선택 상태
        private string _selectedFactionId = "";
        private GroupAIManager _selectedGroup;

        // 스폰 설정
        private int _spawnCount = 3;

        // 배치 모드
        private bool _placementMode = false;
        private bool _placementPersistent = false;
        private Vector3 _lastHoverWorldPos = Vector3.zero;
        private bool _lastHoverOnNavMesh = false;

        // NavMesh sample radius (Level-up 2)
        private float _navMeshSampleRadius = 3f;

        // 스크롤
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;

        // 미니맵
        private Rect _mapRect;
        private Bounds _mapBounds = new Bounds(Vector3.zero, new Vector3(40, 0, 40));
        private bool _mapAutoFit = true;

        // NavMesh 캐시
        private NavMeshTriangulation _navMeshTri;
        private bool _navMeshCached = false;
        private double _navMeshCacheTime = -1;
        private const double NAV_MESH_CACHE_TTL = 3.0;
        private bool _showNavMesh = true;

        // 색상
        private static readonly Color[] _factionColors = {
            new Color(1.00f, 0.55f, 0.55f),
            new Color(0.55f, 0.85f, 1.00f),
            new Color(1.00f, 0.85f, 0.40f),
            new Color(0.65f, 1.00f, 0.55f),
            new Color(1.00f, 0.55f, 1.00f),
            new Color(0.55f, 0.55f, 1.00f),
        };
        private static readonly Color COL_NAVMESH = new Color(0.30f, 0.85f, 0.55f, 0.18f);
        private static readonly Color COL_NAVMESH_EDGE = new Color(0.20f, 0.60f, 0.35f, 0.35f);
        private static readonly Color COL_PLACEMENT_VALID = new Color(0.30f, 1.00f, 0.30f, 0.85f);
        private static readonly Color COL_PLACEMENT_INVALID = new Color(1.00f, 0.40f, 0.40f, 0.85f);

        // ──────────────────────────────────────────────────────────────────
        // 라이프사이클
        // ──────────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            LoadPrefs();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            SavePrefs();
        }

        private void OnEditorUpdate()
        {
            if (Application.isPlaying || _placementMode) Repaint();
        }

        // ──────────────────────────────────────────────────────────────────
        // EditorPrefs 측 영속 (Level-up 1)
        // ──────────────────────────────────────────────────────────────────

        private void LoadPrefs()
        {
            _spawnCount = EditorPrefs.GetInt(PREFS + "spawnCount", 3);
            _placementPersistent = EditorPrefs.GetBool(PREFS + "persistent", false);
            _showNavMesh = EditorPrefs.GetBool(PREFS + "showNavMesh", true);
            _mapAutoFit = EditorPrefs.GetBool(PREFS + "mapAutoFit", true);
            _navMeshSampleRadius = EditorPrefs.GetFloat(PREFS + "navMeshRadius", 3f);
            _selectedFactionId = EditorPrefs.GetString(PREFS + "selectedFaction", "");
        }

        private void SavePrefs()
        {
            EditorPrefs.SetInt(PREFS + "spawnCount", _spawnCount);
            EditorPrefs.SetBool(PREFS + "persistent", _placementPersistent);
            EditorPrefs.SetBool(PREFS + "showNavMesh", _showNavMesh);
            EditorPrefs.SetBool(PREFS + "mapAutoFit", _mapAutoFit);
            EditorPrefs.SetFloat(PREFS + "navMeshRadius", _navMeshSampleRadius);
            EditorPrefs.SetString(PREFS + "selectedFaction", _selectedFactionId ?? "");
        }

        // ──────────────────────────────────────────────────────────────────
        // OnGUI — 3 컬럼 레이아웃
        // ──────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EnsureNavMeshCache();

            DrawHeader();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeftPanel();
                DrawCenterPanel();
                DrawRightPanel();
            }

            if (_placementMode && Event.current.type == EventType.MouseMove) Repaint();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool isPlaying = Application.isPlaying;

                GUILayout.Label("Faction Spawn Control", EditorStyles.boldLabel, GUILayout.Width(170));
                GUILayout.Label(isPlaying ? "● PLAY" : "○ Edit Mode",
                    isPlaying ? EditorStyles.miniBoldLabel : EditorStyles.miniLabel,
                    GUILayout.Width(75));

                if (_placementMode)
                {
                    var origColor = GUI.contentColor;
                    GUI.contentColor = new Color(1, 0.85f, 0.4f);
                    GUILayout.Label("◆ PLACEMENT MODE — 미니맵 클릭", EditorStyles.miniBoldLabel, GUILayout.Width(260));
                    GUI.contentColor = origColor;
                }

                GUILayout.FlexibleSpace();

                _showNavMesh = GUILayout.Toggle(_showNavMesh, "Show NavMesh", EditorStyles.toolbarButton, GUILayout.Width(105));
                _mapAutoFit = GUILayout.Toggle(_mapAutoFit, "Auto-Fit Map", EditorStyles.toolbarButton, GUILayout.Width(95));

                if (GUILayout.Button("Dump All State", EditorStyles.toolbarButton, GUILayout.Width(105)))
                {
                    DumpAllFactionState();
                }

                if (GUILayout.Button("Refresh NavMesh", EditorStyles.toolbarButton, GUILayout.Width(110)))
                {
                    _navMeshCached = false;
                    EnsureNavMeshCache();
                }

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(65)))
                {
                    Repaint();
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // LEFT — Faction 목록 + 배치 모드 + Despawn
        // ══════════════════════════════════════════════════════════════════

        private void DrawLeftPanel()
        {
            using (new EditorGUILayout.VerticalScope("box", GUILayout.Width(230)))
            {
                EditorGUILayout.LabelField("Faction Definitions", EditorStyles.boldLabel);

                using (var sv = new EditorGUILayout.ScrollViewScope(_leftScroll, GUILayout.Height(140)))
                {
                    _leftScroll = sv.scrollPosition;
                    DrawFactionList();
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Spawn (Click on Map)", EditorStyles.boldLabel);

                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    EditorGUI.BeginChangeCheck();
                    _spawnCount = EditorGUILayout.IntSlider("Count", _spawnCount, 1, 12);
                    _placementPersistent = EditorGUILayout.Toggle(
                        new GUIContent("Persistent", "체크 시 1 회 스폰 후 모드 유지 (연속 배치)"),
                        _placementPersistent);
                    _navMeshSampleRadius = EditorGUILayout.Slider(
                        new GUIContent("NavMesh Snap (m)", "클릭 위치 측 NavMesh snap 반경. 좁은 통로 측 작게, 넓은 영역 측 크게."),
                        _navMeshSampleRadius, 0.5f, 10f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        SavePrefs();  // 즉시 저장
                    }

                    EditorGUILayout.Space();

                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_selectedFactionId)))
                    {
                        var btnStyle = new GUIStyle(GUI.skin.button) { fixedHeight = 36, fontStyle = FontStyle.Bold };
                        var origBg = GUI.backgroundColor;

                        if (_placementMode)
                        {
                            GUI.backgroundColor = new Color(1.4f, 0.9f, 0.4f);
                            if (GUILayout.Button("◆ END Placement (ESC)", btnStyle))
                                EndPlacementMode();
                        }
                        else
                        {
                            GUI.backgroundColor = new Color(0.6f, 1.4f, 0.7f);
                            var label = string.IsNullOrEmpty(_selectedFactionId)
                                ? "Select Faction first"
                                : $"▶ Begin Placement\n{_selectedFactionId} × {_spawnCount}";
                            if (GUILayout.Button(label, btnStyle))
                                BeginPlacementMode();
                        }

                        GUI.backgroundColor = origBg;
                    }

                    if (_placementMode)
                    {
                        EditorGUILayout.HelpBox(
                            "미니맵 측 NavMesh (녹색) 위 좌클릭 → 스폰.\nESC 또는 위 버튼 측 종료.",
                            MessageType.Info);
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Despawn", EditorStyles.boldLabel);

                using (new EditorGUI.DisabledScope(!Application.isPlaying || _selectedGroup == null))
                {
                    var btnText = _selectedGroup != null
                        ? $"✕ Despawn '{_selectedGroup.gameObject.name}'"
                        : "Select group on map";
                    if (GUILayout.Button(btnText, GUILayout.Height(26)))
                        DoDespawnSelected();
                }

                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    var allGroups = FindAllGroups();
                    using (new EditorGUI.DisabledScope(allGroups.Count == 0))
                    {
                        if (GUILayout.Button($"✕✕ Despawn All ({allGroups.Count})", GUILayout.Height(22)))
                            DoDespawnAll();
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("NavMesh Info", EditorStyles.boldLabel);
                if (_navMeshCached && _navMeshTri.indices.Length > 0)
                {
                    EditorGUILayout.LabelField($"  Vertices : {_navMeshTri.vertices.Length}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"  Triangles: {_navMeshTri.indices.Length / 3}", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.HelpBox("NavMesh 측 미생성. Window > AI > Navigation 측 Bake.", MessageType.Warning);
                }
            }
        }

        private void DrawFactionList()
        {
            var allIds = GetAllRegisteredFactionIds();
            if (allIds.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    Application.isPlaying ? "등록된 Faction 없음." : "Play 진입 필요.",
                    MessageType.Info);
                return;
            }

            foreach (var id in allIds)
            {
                var isSel = (id == _selectedFactionId);
                var bgColor = isSel ? new Color(0.35f, 0.55f, 0.85f, 0.6f) : Color.clear;
                var rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.label, GUILayout.Height(22));
                EditorGUI.DrawRect(rect, bgColor);

                EditorGUI.DrawRect(new Rect(rect.x + 4, rect.y + 6, 10, 10), GetFactionColor(id));
                GUI.Label(new Rect(rect.x + 20, rect.y, rect.width - 20, rect.height),
                    id, isSel ? EditorStyles.boldLabel : EditorStyles.label);

                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    _selectedFactionId = id;
                    SavePrefs();
                    Event.current.Use();
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // CENTER — 2D 탑뷰 미니맵
        // ══════════════════════════════════════════════════════════════════

        private void DrawCenterPanel()
        {
            using (new EditorGUILayout.VerticalScope("box", GUILayout.ExpandWidth(true)))
            {
                EditorGUILayout.LabelField("Top-Down 2D Map (XZ Plane)", EditorStyles.boldLabel);

                _mapRect = GUILayoutUtility.GetRect(0, 9999, 0, 9999,
                    GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

                EditorGUI.DrawRect(_mapRect, new Color(0.13f, 0.14f, 0.16f));

                ComputeMapBounds();

                DrawMapGrid();
                if (_showNavMesh) DrawNavMesh();
                DrawOriginCrosshair();

                if (Application.isPlaying)
                {
                    var allGroups = FindAllGroups();
                    foreach (var grp in allGroups)
                    {
                        if (grp == null) continue;
                        DrawGroupBoundingBox(grp);
                    }

                    if (allGroups.Count == 0 && (!_navMeshCached || _navMeshTri.indices.Length == 0))
                    {
                        GUI.Label(new Rect(_mapRect.x + 20, _mapRect.y + 20, 500, 20),
                            "NavMesh + 그룹 없음. Bake 후 Play 진입.", EditorStyles.miniLabel);
                    }
                }
                else
                {
                    GUI.Label(new Rect(_mapRect.x + 20, _mapRect.y + 20, 400, 20),
                        "Play 진입 후 그룹 시각.", EditorStyles.miniLabel);
                }

                HandlePlacementInput();
                DrawMapLegend();
            }
        }

        private void DrawMapGrid()
        {
            var dim = new Color(0.20f, 0.21f, 0.23f);
            for (int i = 1; i < 4; i++)
            {
                float t = i / 4f;
                float x = _mapRect.x + _mapRect.width * t;
                float y = _mapRect.y + _mapRect.height * t;
                EditorGUI.DrawRect(new Rect(x, _mapRect.y, 1, _mapRect.height), dim);
                EditorGUI.DrawRect(new Rect(_mapRect.x, y, _mapRect.width, 1), dim);
            }
        }

        private void DrawOriginCrosshair()
        {
            if (_mapBounds.size.x <= 0) return;
            var origin = WorldToMap(Vector3.zero);
            if (!_mapRect.Contains(origin)) return;

            var hilite = new Color(0.5f, 0.7f, 0.4f, 0.5f);
            EditorGUI.DrawRect(new Rect(origin.x - 1, _mapRect.y, 2, _mapRect.height), hilite);
            EditorGUI.DrawRect(new Rect(_mapRect.x, origin.y - 1, _mapRect.width, 2), hilite);
        }

        private void DrawNavMesh()
        {
            if (!_navMeshCached || _navMeshTri.indices.Length == 0) return;

            Handles.BeginGUI();

            Handles.color = COL_NAVMESH;
            var v = _navMeshTri.vertices;
            var idx = _navMeshTri.indices;
            for (int i = 0; i + 2 < idx.Length; i += 3)
            {
                var p0 = WorldToMap(v[idx[i]]);
                var p1 = WorldToMap(v[idx[i + 1]]);
                var p2 = WorldToMap(v[idx[i + 2]]);
                if (IsTriangleOutsideMap(p0, p1, p2)) continue;
                Handles.DrawAAConvexPolygon(p0, p1, p2);
            }

            Handles.color = COL_NAVMESH_EDGE;
            for (int i = 0; i + 2 < idx.Length; i += 3)
            {
                var p0 = WorldToMap(v[idx[i]]);
                var p1 = WorldToMap(v[idx[i + 1]]);
                var p2 = WorldToMap(v[idx[i + 2]]);
                if (IsTriangleOutsideMap(p0, p1, p2)) continue;
                Handles.DrawLine(p0, p1);
                Handles.DrawLine(p1, p2);
                Handles.DrawLine(p2, p0);
            }

            Handles.EndGUI();
        }

        private bool IsTriangleOutsideMap(Vector2 p0, Vector2 p1, Vector2 p2)
        {
            float minX = Mathf.Min(p0.x, p1.x, p2.x);
            float maxX = Mathf.Max(p0.x, p1.x, p2.x);
            float minY = Mathf.Min(p0.y, p1.y, p2.y);
            float maxY = Mathf.Max(p0.y, p1.y, p2.y);
            return maxX < _mapRect.x || minX > _mapRect.xMax ||
                   maxY < _mapRect.y || minY > _mapRect.yMax;
        }

        private void ComputeMapBounds()
        {
            if (!_mapAutoFit && _mapBounds.size.x > 0) return;

            var pts = new List<Vector3>();

            if (_navMeshCached && _navMeshTri.vertices != null)
            {
                foreach (var v in _navMeshTri.vertices) pts.Add(v);
            }

            if (Application.isPlaying)
            {
                foreach (var g in FindAllGroups())
                {
                    if (g == null) continue;
                    foreach (var m in g.Members)
                    {
                        if (m?.brain != null) pts.Add(m.brain.transform.position);
                    }
                }
            }

            if (pts.Count == 0)
            {
                _mapBounds = new Bounds(Vector3.zero, new Vector3(40, 0, 40));
                return;
            }

            var min = pts[0]; var max = pts[0];
            foreach (var p in pts) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }

            min -= new Vector3(5, 0, 5);
            max += new Vector3(5, 0, 5);

            var size = max - min;
            if (size.x < 20) { float c = (min.x + max.x) * 0.5f; min.x = c - 10; max.x = c + 10; }
            if (size.z < 20) { float c = (min.z + max.z) * 0.5f; min.z = c - 10; max.z = c + 10; }

            _mapBounds = new Bounds((min + max) * 0.5f, max - min);
        }

        private Vector2 WorldToMap(Vector3 worldPos)
        {
            if (_mapBounds.size.x <= 0 || _mapBounds.size.z <= 0)
                return new Vector2(_mapRect.center.x, _mapRect.center.y);

            float u = (worldPos.x - _mapBounds.min.x) / _mapBounds.size.x;
            float v = (worldPos.z - _mapBounds.min.z) / _mapBounds.size.z;
            float x = _mapRect.x + u * _mapRect.width;
            float y = _mapRect.y + (1f - v) * _mapRect.height;
            return new Vector2(x, y);
        }

        private Vector3 MapToWorld(Vector2 screenPos)
        {
            if (_mapBounds.size.x <= 0 || _mapBounds.size.z <= 0) return Vector3.zero;

            float u = (screenPos.x - _mapRect.x) / _mapRect.width;
            float v = 1f - (screenPos.y - _mapRect.y) / _mapRect.height;
            return new Vector3(
                _mapBounds.min.x + u * _mapBounds.size.x,
                0f,
                _mapBounds.min.z + v * _mapBounds.size.z
            );
        }

        // ──────────────────────────────────────────────────────────────────
        // 그룹 박스 측 멤버 좌표 기준
        // ──────────────────────────────────────────────────────────────────

        private void DrawGroupBoundingBox(GroupAIManager grp)
        {
            var factionId = ResolveFactionIdSafe(grp);
            var color = GetFactionColor(factionId);
            var isSel = (grp == _selectedGroup);

            var positions = new List<Vector3>();
            foreach (var m in grp.Members)
            {
                if (m?.brain != null) positions.Add(m.brain.transform.position);
            }

            if (positions.Count == 0)
            {
                positions.Add(grp.transform.position);
            }

            var min = positions[0]; var max = positions[0];
            foreach (var p in positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }

            var pad = new Vector3(1.5f, 0, 1.5f);
            min -= pad; max += pad;

            var sTopLeft = WorldToMap(new Vector3(min.x, 0, max.z));
            var sBotRight = WorldToMap(new Vector3(max.x, 0, min.z));
            var boxRect = Rect.MinMaxRect(sTopLeft.x, sTopLeft.y, sBotRight.x, sBotRight.y);

            var fillColor = new Color(color.r, color.g, color.b, isSel ? 0.18f : 0.10f);
            EditorGUI.DrawRect(boxRect, fillColor);

            var outlineColor = new Color(color.r, color.g, color.b, isSel ? 1.0f : 0.85f);
            int t = isSel ? 2 : 1;
            EditorGUI.DrawRect(new Rect(boxRect.x, boxRect.y, boxRect.width, t), outlineColor);
            EditorGUI.DrawRect(new Rect(boxRect.x, boxRect.yMax - t, boxRect.width, t), outlineColor);
            EditorGUI.DrawRect(new Rect(boxRect.x, boxRect.y, t, boxRect.height), outlineColor);
            EditorGUI.DrawRect(new Rect(boxRect.xMax - t, boxRect.y, t, boxRect.height), outlineColor);

            var labelText = $"{grp.gameObject.name} [F:{factionId}] ({grp.MemberCount})";
            var labelStyle = new GUIStyle(EditorStyles.miniBoldLabel);
            labelStyle.normal.textColor = outlineColor;
            var labelRect = new Rect(boxRect.x, boxRect.y - 16, 250, 14);

            var labelBg = new Color(0, 0, 0, 0.55f);
            var labelBgRect = new Rect(boxRect.x - 2, boxRect.y - 17, 250, 16);
            EditorGUI.DrawRect(labelBgRect, labelBg);
            GUI.Label(labelRect, labelText, labelStyle);

            foreach (var p in positions)
            {
                var sp = WorldToMap(p);
                EditorGUI.DrawRect(new Rect(sp.x - 3, sp.y - 3, 6, 6), color);
            }

            if (!_placementMode && Event.current.type == EventType.MouseDown &&
                boxRect.Contains(Event.current.mousePosition))
            {
                _selectedGroup = grp;
                _selectedFactionId = factionId;
                SavePrefs();
                Event.current.Use();
                Repaint();
            }
        }

        private void DrawMapLegend()
        {
            var w = 200f;
            var legendRect = new Rect(_mapRect.xMax - w - 8, _mapRect.y + 8, w, 46);
            EditorGUI.DrawRect(legendRect, new Color(0, 0, 0, 0.55f));
            GUI.Label(new Rect(legendRect.x + 6, legendRect.y + 2, w - 12, 14),
                $"Bounds: {_mapBounds.size.x:F0}m × {_mapBounds.size.z:F0}m", EditorStyles.miniLabel);
            GUI.Label(new Rect(legendRect.x + 6, legendRect.y + 16, w - 12, 14),
                $"Origin: ({_mapBounds.center.x:F0}, {_mapBounds.center.z:F0})", EditorStyles.miniLabel);
            GUI.Label(new Rect(legendRect.x + 6, legendRect.y + 30, w - 12, 14),
                $"NavMesh tri: {(_navMeshCached ? _navMeshTri.indices.Length / 3 : 0)}", EditorStyles.miniLabel);
        }

        // ══════════════════════════════════════════════════════════════════
        // 배치 모드
        // ══════════════════════════════════════════════════════════════════

        private void BeginPlacementMode()
        {
            if (string.IsNullOrEmpty(_selectedFactionId))
            {
                EditorUtility.DisplayDialog("Placement", "Faction 측 먼저 선택.", "OK");
                return;
            }
            _placementMode = true;
            Repaint();
        }

        private void EndPlacementMode()
        {
            _placementMode = false;
            Repaint();
        }

        private void HandlePlacementInput()
        {
            if (!_placementMode) return;

            var e = Event.current;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                EndPlacementMode();
                e.Use();
                return;
            }

            if (!_mapRect.Contains(e.mousePosition)) return;

            var worldPos = MapToWorld(e.mousePosition);
            bool onNav = NavMesh.SamplePosition(worldPos, out var navHit, _navMeshSampleRadius, NavMesh.AllAreas);
            _lastHoverWorldPos = onNav ? navHit.position : worldPos;
            _lastHoverOnNavMesh = onNav;

            var cursorColor = onNav ? COL_PLACEMENT_VALID : COL_PLACEMENT_INVALID;
            var cursorScreenPos = onNav ? WorldToMap(navHit.position) : e.mousePosition;

            DrawCrossMarker(cursorScreenPos, 12, cursorColor);

            for (int i = 1; i < _spawnCount; i++)
            {
                float angle = (i / (float)_spawnCount) * Mathf.PI * 2f;
                var offsetWorld = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 2f;
                var memberSP = WorldToMap(_lastHoverWorldPos + offsetWorld);
                EditorGUI.DrawRect(new Rect(memberSP.x - 2, memberSP.y - 2, 4, 4),
                    new Color(cursorColor.r, cursorColor.g, cursorColor.b, 0.5f));
            }

            var labelText = onNav
                ? $"✓ NavMesh OK\n{_selectedFactionId} ×{_spawnCount}\n@({navHit.position.x:F1},{navHit.position.z:F1})"
                : $"✗ NavMesh 외부 (snap {_navMeshSampleRadius:F1}m)\n@({worldPos.x:F1},{worldPos.z:F1})";
            var labelStyle = new GUIStyle(EditorStyles.miniBoldLabel);
            labelStyle.normal.textColor = onNav ? Color.green : new Color(1, 0.4f, 0.4f);
            var labelRect = new Rect(cursorScreenPos.x + 16, cursorScreenPos.y - 20, 240, 50);
            EditorGUI.DrawRect(new Rect(labelRect.x - 2, labelRect.y - 2, labelRect.width, labelRect.height + 4),
                new Color(0, 0, 0, 0.65f));
            GUI.Label(labelRect, labelText, labelStyle);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (onNav)
                {
                    DoSpawnAt(navHit.position);
                    if (!_placementPersistent) EndPlacementMode();
                }
                else
                {
                    Debug.LogWarning(
                        $"<color=#FFAA66>[FactionSpawnControl]</color> NavMesh 외부 — 스폰 취소 " +
                        $"(snap radius {_navMeshSampleRadius:F1}m) @({worldPos.x:F1},{worldPos.z:F1})");
                }
                e.Use();
            }
        }

        private void DrawCrossMarker(Vector2 center, float size, Color color)
        {
            int t = 2;
            EditorGUI.DrawRect(new Rect(center.x - size, center.y - t / 2f, size * 2, t), color);
            EditorGUI.DrawRect(new Rect(center.x - t / 2f, center.y - size, t, size * 2), color);
            EditorGUI.DrawRect(new Rect(center.x - 3, center.y - 3, 6, 6), color);
        }

        // ══════════════════════════════════════════════════════════════════
        // RIGHT — 상세 정보 + 멤버 목록 + Force 버튼
        // ══════════════════════════════════════════════════════════════════

        private void DrawRightPanel()
        {
            using (new EditorGUILayout.VerticalScope("box", GUILayout.Width(320)))
            {
                EditorGUILayout.LabelField("Selected Faction", EditorStyles.boldLabel);

                if (string.IsNullOrEmpty(_selectedFactionId))
                {
                    EditorGUILayout.HelpBox("Faction 측 선택 (좌측 list 또는 미니맵 측 그룹 박스 클릭)", MessageType.Info);
                    return;
                }

                using (var sv = new EditorGUILayout.ScrollViewScope(_rightScroll))
                {
                    _rightScroll = sv.scrollPosition;

                    DrawSelectedFactionInfo();
                    EditorGUILayout.Space();
                    DrawSelectedGroupInfo();
                    EditorGUILayout.Space();
                    DrawStateControlButtons();
                }
            }
        }

        private void DrawSelectedFactionInfo()
        {
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge == null) { EditorGUILayout.LabelField("Bridge unavailable"); return; }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.DrawRect(GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14)),
                                   GetFactionColor(_selectedFactionId));
                EditorGUILayout.LabelField($"factionId = {_selectedFactionId}", EditorStyles.boldLabel);
            }

            var snap = bridge.GetSnapshot(_selectedFactionId);
            if (!snap.IsValid)
            {
                EditorGUILayout.HelpBox("Faction 측 미등록 상태", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField($"Mood     : 0x{snap.MoodBits:X8}");
            EditorGUILayout.LabelField($"Tactical : 0x{snap.TacticalBits:X8}");
            EditorGUILayout.LabelField($"Lifecycle: 0x{snap.LifecycleBits:X8}");
            EditorGUILayout.LabelField($"Relation : 0x{snap.RelationBits:X8}");
        }

        // Level-up 3: 멤버 목록 + Frame 버튼
        private void DrawSelectedGroupInfo()
        {
            if (_selectedGroup == null)
            {
                EditorGUILayout.LabelField("Group: (none — 미니맵 측 박스 클릭)", EditorStyles.miniLabel);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Group: {_selectedGroup.gameObject.name}", EditorStyles.boldLabel);
                if (GUILayout.Button("Select GO", EditorStyles.miniButton, GUILayout.Width(70)))
                {
                    Selection.activeGameObject = _selectedGroup.gameObject;
                    EditorGUIUtility.PingObject(_selectedGroup.gameObject);
                }
            }
            EditorGUILayout.LabelField($"  Members  : {_selectedGroup.MemberCount}");
            EditorGUILayout.LabelField($"  factionId: '{ResolveFactionIdSafe(_selectedGroup)}'");

            // 멤버 목록 + Frame 버튼
            if (_selectedGroup.MemberCount > 0)
            {
                EditorGUILayout.LabelField("  Members:", EditorStyles.miniBoldLabel);
                int idx = 0;
                foreach (var m in _selectedGroup.Members)
                {
                    if (m?.brain == null) continue;
                    var pos = m.brain.transform.position;
                    var brainGO = m.brain.gameObject;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("→", EditorStyles.miniButton, GUILayout.Width(22)))
                        {
                            // Scene 측 Frame
                            Selection.activeGameObject = brainGO;
                            var sv = SceneView.lastActiveSceneView;
                            if (sv != null) sv.FrameSelected();
                            EditorGUIUtility.PingObject(brainGO);
                        }
                        EditorGUILayout.LabelField(
                            $"#{idx} {brainGO.name} @({pos.x:F1},{pos.z:F1})",
                            EditorStyles.miniLabel);
                    }
                    idx++;
                    if (idx >= 12) break;  // 최대 12 측 표시
                }
            }
        }

        private void DrawStateControlButtons()
        {
            using (new EditorGUI.DisabledScope(!Application.isPlaying || string.IsNullOrEmpty(_selectedFactionId)))
            {
                EditorGUILayout.LabelField("Force State (validMask 우회)", EditorStyles.boldLabel);

                EditorGUILayout.LabelField("Mood:", EditorStyles.miniLabel);
                DrawForceButtonRow<FactionMoodTag>(new[] {
                    FactionMoodTag.MOOD_PANIC,
                    FactionMoodTag.MOOD_AGGRESSIVE,
                    FactionMoodTag.MOOD_CALM,
                });

                EditorGUILayout.LabelField("Tactical:", EditorStyles.miniLabel);
                DrawForceButtonRow<FactionTacticalTag>(new[] {
                    FactionTacticalTag.TACTICAL_DEFENSIVE,
                    FactionTacticalTag.TACTICAL_RETREATING,
                });

                EditorGUILayout.LabelField("Lifecycle:", EditorStyles.miniLabel);
                DrawForceButtonRow<FactionLifecycleTag>(new[] {
                    FactionLifecycleTag.LIFECYCLE_EMERGING,
                    FactionLifecycleTag.LIFECYCLE_ACTIVE,
                    FactionLifecycleTag.LIFECYCLE_THREATENED,
                    FactionLifecycleTag.LIFECYCLE_DEFEATED,
                    FactionLifecycleTag.LIFECYCLE_EXTINCT,
                });

                EditorGUILayout.LabelField("Relation:", EditorStyles.miniLabel);
                DrawForceButtonRow<FactionRelationTag>(new[] {
                    FactionRelationTag.RELATION_PLAYER_HOSTILE,
                    FactionRelationTag.RELATION_ALLIED,
                });

                EditorGUILayout.Space();

                if (GUILayout.Button("Reset to LIFECYCLE_ACTIVE", GUILayout.Height(22)))
                {
                    var mgr = WorldFactionStateManager.Instance;
                    if (mgr != null) mgr.ResetAllState();
                }
            }
        }

        private void DrawForceButtonRow<T>(T[] tags) where T : Enum
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var tag in tags)
                {
                    string label = tag.ToString().Replace("MOOD_", "").Replace("TACTICAL_", "")
                        .Replace("LIFECYCLE_", "").Replace("RELATION_", "");

                    bool isOn = IsTagCurrentlyOn(tag);
                    var origColor = GUI.backgroundColor;
                    if (isOn) GUI.backgroundColor = new Color(1.2f, 1.2f, 0.7f);

                    if (GUILayout.Button(isOn ? $"✓ {label}" : label, GUILayout.MinWidth(50), GUILayout.Height(22)))
                    {
                        DoForceToggle(tag, !isOn);
                    }

                    GUI.backgroundColor = origColor;
                }
            }
        }

        private bool IsTagCurrentlyOn<T>(T tag) where T : Enum
        {
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge == null || string.IsNullOrEmpty(_selectedFactionId)) return false;

            var snap = bridge.GetSnapshot(_selectedFactionId);
            if (!snap.IsValid) return false;

            uint bit = Convert.ToUInt32(tag);

            // 'is' 패턴 측 사용 (switch 측 discard 패턴 측 호환 자료 회피)
            if (tag is FactionMoodTag) return (snap.MoodBits & bit) != 0u;
            if (tag is FactionTacticalTag) return (snap.TacticalBits & bit) != 0u;
            if (tag is FactionLifecycleTag) return (snap.LifecycleBits & bit) != 0u;
            if (tag is FactionRelationTag) return (snap.RelationBits & bit) != 0u;
            return false;
        }

        // ══════════════════════════════════════════════════════════════════
        // 액션
        // ══════════════════════════════════════════════════════════════════

        private void DoSpawnAt(Vector3 worldPos)
        {
            var spawnMgr = UnityEngine.Object.FindFirstObjectByType<WorldAISpawnManager>();
            if (spawnMgr == null)
            {
                EditorUtility.DisplayDialog("Spawn 실패", "WorldAISpawnManager 측 미발견.", "OK");
                return;
            }

            var req = new SpawnRequest
            {
                factionId = _selectedFactionId,
                memberCount = _spawnCount,
                locationStrategy = SpawnLocationStrategy.Explicit,
                explicitPosition = worldPos,
                hasExplicitPosition = true,
                reason = SpawnReason.EditorClick,
                priority = 0.5f
            };

            if (!spawnMgr.CanAcceptRequest(req, out var failReason))
            {
                Debug.LogError(
                    $"<color=#FF5555>[FactionSpawnControl]</color> Spawn 거절 — {failReason}\n" +
                    $"  factionId={_selectedFactionId} / count={_spawnCount} / pos=({worldPos.x:F1},{worldPos.z:F1})");
                EditorUtility.DisplayDialog("Spawn 거절", failReason, "OK");
                return;
            }

            var response = spawnMgr.ExecuteSpawnRequest(req);
            Debug.Log(
                $"<color=#88FF88>[FactionSpawnControl]</color> Spawn 호출 @({worldPos.x:F1},{worldPos.z:F1}) — " +
                $"{_selectedFactionId} × {_spawnCount} / result={response?.result}");
        }

        private void DoDespawnSelected()
        {
            if (_selectedGroup == null) return;

            var grpName = _selectedGroup.gameObject.name;
            int memberCount = 0;

            foreach (var m in _selectedGroup.Members)
            {
                if (m?.brain != null && m.brain.gameObject != null)
                {
                    UnityEngine.Object.Destroy(m.brain.gameObject);
                    memberCount++;
                }
            }
            UnityEngine.Object.Destroy(_selectedGroup.gameObject);
            _selectedGroup = null;

            Debug.Log($"<color=#FFAA66>[FactionSpawnControl]</color> Despawn — {grpName} (멤버 {memberCount} 정리)");
        }

        private void DoDespawnAll()
        {
            var allGroups = FindAllGroups();
            int totalMembers = 0, totalGroups = 0;

            foreach (var grp in allGroups)
            {
                if (grp == null) continue;
                foreach (var m in grp.Members)
                {
                    if (m?.brain != null && m.brain.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(m.brain.gameObject);
                        totalMembers++;
                    }
                }
                UnityEngine.Object.Destroy(grp.gameObject);
                totalGroups++;
            }

            _selectedGroup = null;
            Debug.Log($"<color=#FFAA66>[FactionSpawnControl]</color> Despawn All — {totalGroups} 그룹 / {totalMembers} 멤버 정리");
        }

        private void DoForceToggle<T>(T tag, bool on) where T : Enum
        {
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge == null) return;

            // 'is' 패턴 측 사용
            if (tag is FactionMoodTag mood) bridge.ForceMood(_selectedFactionId, mood, on);
            else if (tag is FactionTacticalTag tac) bridge.ForceTactical(_selectedFactionId, tac, on);
            else if (tag is FactionLifecycleTag lc) bridge.ForceLifecycle(_selectedFactionId, lc, on);
            else if (tag is FactionRelationTag rel) bridge.ForceRelation(_selectedFactionId, rel, on);
        }

        // Level-up 4: 전체 Faction 상태 Dump
        private void DumpAllFactionState()
        {
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge == null)
            {
                Debug.LogWarning("[FactionSpawnControl] Bridge 측 미발견 — Play 진입 필요.");
                return;
            }

            var ids = bridge.GetAllFactionIds();
            if (ids == null || ids.Count == 0)
            {
                Debug.LogWarning("[FactionSpawnControl] 등록된 Faction 0.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<color=#88CCFF>━━━ Faction State Dump ━━━</color>");
            sb.AppendLine($"  등록 측 Faction: {ids.Count} 종");
            sb.AppendLine();

            foreach (var id in ids)
            {
                var snap = bridge.GetSnapshot(id);
                if (!snap.IsValid)
                {
                    sb.AppendLine($"  [{id}] (invalid snapshot)");
                    continue;
                }
                sb.AppendLine($"  [{id}]");
                sb.AppendLine($"    Mood     = 0x{snap.MoodBits:X8}");
                sb.AppendLine($"    Tactical = 0x{snap.TacticalBits:X8}");
                sb.AppendLine($"    Lifecycle= 0x{snap.LifecycleBits:X8}");
                sb.AppendLine($"    Relation = 0x{snap.RelationBits:X8}");
            }

            var allGroups = FindAllGroups();
            sb.AppendLine();
            sb.AppendLine($"  활성 그룹: {allGroups.Count} 개");
            foreach (var g in allGroups)
            {
                if (g == null) continue;
                sb.AppendLine($"    {g.gameObject.name} [F:{ResolveFactionIdSafe(g)}] members={g.MemberCount}");
            }

            Debug.Log(sb.ToString());
        }

        // ══════════════════════════════════════════════════════════════════
        // 유틸
        // ══════════════════════════════════════════════════════════════════

        private void EnsureNavMeshCache()
        {
            var now = EditorApplication.timeSinceStartup;
            if (_navMeshCached && (now - _navMeshCacheTime) < NAV_MESH_CACHE_TTL) return;

            _navMeshTri = NavMesh.CalculateTriangulation();
            _navMeshCached = true;
            _navMeshCacheTime = now;
        }

        private List<string> GetAllRegisteredFactionIds()
        {
            var result = new List<string>();
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge == null) return result;

            var ids = bridge.GetAllFactionIds();
            if (ids != null) result.AddRange(ids);
            return result;
        }

        private List<GroupAIManager> FindAllGroups()
        {
            if (!Application.isPlaying) return new List<GroupAIManager>();

            return UnityEngine.Object.FindObjectsByType<GroupAIManager>(FindObjectsSortMode.None)
                .Where(g => g != null && g.gameObject.name.StartsWith("Group_"))
                .ToList();
        }

        private static string ResolveFactionIdSafe(GroupAIManager grp)
        {
            if (grp == null) return "";

            var fromPolicy = grp.GetFactionId();
            if (!string.IsNullOrEmpty(fromPolicy)) return fromPolicy;

            var name = grp.gameObject.name;
            const string prefix = "Group_";
            if (name.StartsWith(prefix))
            {
                var rest = name.Substring(prefix.Length);
                var ui = rest.IndexOf('_');
                if (ui > 0) return rest.Substring(0, ui);
            }
            return "";
        }

        private Color GetFactionColor(string factionId)
        {
            if (string.IsNullOrEmpty(factionId)) return Color.gray;
            int hash = Mathf.Abs(factionId.GetHashCode());
            return _factionColors[hash % _factionColors.Length];
        }
    }
}
#endif