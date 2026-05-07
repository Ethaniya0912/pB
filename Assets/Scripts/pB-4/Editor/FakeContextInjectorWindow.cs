// ─────────────────────────────────────────────────────────────────────
// pB-4 — FakeContextInjectorWindow (★ Phase 5 — 가짜 환경 마킹 도구)
// ─────────────────────────────────────────────────────────────────────
// 갱신: 2026-05-04 v1.1 — UX 픽스
//   v1 → v1.1 변경 사항:
//     [픽스] 클릭이 cave chunk 선택으로 빠지는 문제 해결
//            → HandleUtility.AddDefaultControl(controlId) 패턴 적용
//            → 배치 모드 ON 일 때만 우선권 등록 (OFF 일 때는 평소대로 cave 선택)
//     [픽스] 윈도우 스크롤 부재 → OnGUI 외곽에 BeginScrollView 추가
//     [추가] MouseUp 시 hotControl 놓기 (drag 잔상 제거)
//
// v1 (2026-05-04) — 신설
//   목적: 마우스로 씬에 가짜 ContextTagSet 영역 배치, NPC 인지 강제 검증
//
// 위치: Assets/Scripts/Editor/FakeContextInjectorWindow.cs
//   ★ Editor 폴더 안에 둘 것 — UnityEditor namespace 사용
//
// 사용법:
//   1) Window > pB-4 > Fake Context Injector 메뉴 선택
//   2) 영역 형태 (Sphere / Line / Box) 선택
//   3) 프리셋 또는 정밀 60 비트 토글
//   4) [배치 모드 ON] 클릭 → SceneView 좌클릭 → 영역 추가
//   5) 등록된 영역 리스트에서 편집 / 삭제 / 토글
//
// 의존:
//   WorldAIContextManager (런타임 인스턴스 필요 — Play 중에만 영역 적용)
//   FakeContextRegion / FakeRegionShape / FakeOverrideMode (같은 namespace)
//   CaveSystem.IdentityTag / StateTag / EventTag / PotentialTag
// ─────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TDA.PB4.AI.Context;
using CaveSystem;

namespace TDA.PB4.Editor
{
    public class FakeContextInjectorWindow : EditorWindow
    {
        // ═════════════════════════════════════════════════════════════
        // 메뉴 등록
        // ═════════════════════════════════════════════════════════════

        [MenuItem("Window/pB-4/★ Fake Context Injector")]
        public static void Open()
        {
            var win = GetWindow<FakeContextInjectorWindow>(false, "Fake Context Injector", true);
            win.minSize = new Vector2(420, 600);
            win.Show();
        }

        // ═════════════════════════════════════════════════════════════
        // 상태 — 현재 편집 중인 영역 설정
        // ═════════════════════════════════════════════════════════════

        // 배치 모드
        private bool _placementMode = false;
        private Vector3 _hoverWorldPos;
        private bool _hoverValid = false;

        // 영역 형태
        private FakeRegionShape _shape = FakeRegionShape.Sphere;

        // Sphere 파라미터
        private float _sphereRadius = 5.0f;

        // Line 파라미터
        private float _lineLength = 8.0f;
        private float _lineWidth = 3.0f;
        private Vector3 _lineDirection = Vector3.forward;

        // Box 파라미터
        private Vector3 _boxSize = new Vector3(6f, 3f, 6f);

        // 적용 방식
        private FakeOverrideMode _overrideMode = FakeOverrideMode.OR;

        // 비트마스크 (편집 중)
        private uint _editIdentity;
        private uint _editState;
        private uint _editEvent;
        private uint _editPotential;

        // 정밀 토글 패널 펼침
        private bool _showIdentity = false;
        private bool _showState = true;
        private bool _showEvent = false;
        private bool _showPotential = true;

        // 영역 리스트 스크롤
        private Vector2 _listScroll;

        // ★ v1.1 — 윈도우 전체 스크롤
        private Vector2 _windowScroll;

        // 영역 ID 자동 카운터
        private int _nextRegionCounter = 1;

        // ═════════════════════════════════════════════════════════════
        // 프리셋 정의
        // ═════════════════════════════════════════════════════════════

        private struct Preset
        {
            public string label;
            public string tooltip;
            public uint identity;
            public uint state;
            public uint eventTags;
            public uint potential;
        }

        private static readonly Preset[] PRESETS = new[]
        {
            new Preset
            {
                label = "🗡️ 매복지 (Ambush)",
                tooltip = "IDENTITY_AMBUSH + STATE_NARROW + POTENTIAL_AMBUSH + POTENTIAL_BOTTLENECK\n→ fear +0.40 +0.30 +0.40 +0.30 = +1.40",
                identity = (uint)IdentityTag.IDENTITY_AMBUSH,
                state = (uint)StateTag.STATE_NARROW,
                eventTags = 0,
                potential = (uint)PotentialTag.POTENTIAL_AMBUSH | (uint)PotentialTag.POTENTIAL_BOTTLENECK,
            },
            new Preset
            {
                label = "🏔️ 협소 통로 (Narrow)",
                tooltip = "STATE_NARROW + STATE_ENCLOSED\n→ fear +0.30 +0.20 = +0.50",
                identity = 0,
                state = (uint)StateTag.STATE_NARROW | (uint)StateTag.STATE_ENCLOSED,
                eventTags = 0,
                potential = 0,
            },
            new Preset
            {
                label = "👹 Boss 방 (Boss Arena)",
                tooltip = "IDENTITY_BOSS + IDENTITY_BOSS_ARENA + STATE_GRAND + POTENTIAL_SACRED_VENUE\n→ fear +0.30 + (-0.10) + ... = +Major",
                identity = (uint)IdentityTag.IDENTITY_BOSS | (uint)IdentityTag.IDENTITY_BOSS_ARENA,
                state = (uint)StateTag.STATE_GRAND,
                eventTags = 0,
                potential = (uint)PotentialTag.POTENTIAL_SACRED_VENUE,
            },
            new Preset
            {
                label = "⚠️ 위험 지역 (Critical Risk)",
                tooltip = "STATE_CRITICAL_RISK + IDENTITY_HAZARD + POTENTIAL_FALL_HAZARD\n→ fear +0.40 +0.25 +0.35 = +1.00",
                identity = (uint)IdentityTag.IDENTITY_HAZARD,
                state = (uint)StateTag.STATE_CRITICAL_RISK,
                eventTags = 0,
                potential = (uint)PotentialTag.POTENTIAL_FALL_HAZARD,
            },
            new Preset
            {
                label = "🛡️ 안전 허브 (Safe Hub)",
                tooltip = "IDENTITY_HUB + IDENTITY_SPAWN + STATE_OPEN\n→ fear -0.05 + (-0.10) + (-0.05) = -0.20",
                identity = (uint)IdentityTag.IDENTITY_HUB | (uint)IdentityTag.IDENTITY_SPAWN,
                state = (uint)StateTag.STATE_OPEN,
                eventTags = 0,
                potential = 0,
            },
            new Preset
            {
                label = "🌟 전망점 (Vantage)",
                tooltip = "STATE_ELEVATED + STATE_GRAND + POTENTIAL_VANTAGE_POINT\n→ fear -0.10 + (-0.10) + (-0.15) = -0.35",
                identity = 0,
                state = (uint)StateTag.STATE_ELEVATED | (uint)StateTag.STATE_GRAND,
                eventTags = 0,
                potential = (uint)PotentialTag.POTENTIAL_VANTAGE_POINT,
            },
            new Preset
            {
                label = "🌀 분기점 (Y-Fork)",
                tooltip = "IDENTITY_DECISION + IDENTITY_MAJOR_JUNCTION + POTENTIAL_MULTI_PATH_CHOICE\n→ 의사결정 노드",
                identity = (uint)IdentityTag.IDENTITY_DECISION | (uint)IdentityTag.IDENTITY_MAJOR_JUNCTION,
                state = 0,
                eventTags = 0,
                potential = (uint)PotentialTag.POTENTIAL_MULTI_PATH_CHOICE,
            },
            new Preset
            {
                label = "💀 환경 트랩 (Trap)",
                tooltip = "POTENTIAL_ENVIRONMENTAL_TRAP + POTENTIAL_FALL_HAZARD + STATE_DEEP\n→ fear +0.25 +0.35 +0.10 = +0.70",
                identity = 0,
                state = (uint)StateTag.STATE_DEEP,
                eventTags = 0,
                potential = (uint)PotentialTag.POTENTIAL_ENVIRONMENTAL_TRAP | (uint)PotentialTag.POTENTIAL_FALL_HAZARD,
            },
        };

        // ═════════════════════════════════════════════════════════════
        // OnEnable / OnDisable
        // ═════════════════════════════════════════════════════════════

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        // ═════════════════════════════════════════════════════════════
        // OnGUI — 윈도우 메인 UI
        // ═════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            _windowScroll = EditorGUILayout.BeginScrollView(_windowScroll);

            DrawHeader();
            EditorGUILayout.Space();

            DrawShapeSection();
            EditorGUILayout.Space();

            DrawOverrideModeSection();
            EditorGUILayout.Space();

            DrawPresetsSection();
            EditorGUILayout.Space();

            DrawBitmaskSection();
            EditorGUILayout.Space();

            DrawPlacementSection();
            EditorGUILayout.Space();

            DrawRegionListSection();

            EditorGUILayout.EndScrollView();
        }

        // ─────────────────────────────────────────────────────────────
        // 헤더 — 상태 + 안내
        // ─────────────────────────────────────────────────────────────

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("★ Fake Context Injector — Phase 5 시각 검증",
                EditorStyles.boldLabel);

            var ctx = WorldAIContextManager.Instance;
            if (ctx == null)
            {
                EditorGUILayout.HelpBox(
                    "WorldAIContextManager.Instance == null\n" +
                    "Play 모드에서 메뉴 씬 → 본 씬 진입 후 사용 가능.",
                    MessageType.Warning);
            }
            else
            {
                int n = ctx.FakeRegionCount;
                string status = ctx.IsInjected ? "✓ Injected" : "⚠ NotInjected";
                EditorGUILayout.LabelField($"Status: {status}    Active Regions: {n}",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────────────────────
        // 영역 형태
        // ─────────────────────────────────────────────────────────────

        private void DrawShapeSection()
        {
            EditorGUILayout.LabelField("영역 형태", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            DrawShapeButton(FakeRegionShape.Sphere, "🔵 Sphere");
            DrawShapeButton(FakeRegionShape.Line, "▬ Line");
            DrawShapeButton(FakeRegionShape.Box, "⬛ Box");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            switch (_shape)
            {
                case FakeRegionShape.Sphere:
                    _sphereRadius = EditorGUILayout.Slider("Radius (m)", _sphereRadius, 0.5f, 50f);
                    break;

                case FakeRegionShape.Line:
                    _lineLength = EditorGUILayout.Slider("Length (m)", _lineLength, 1f, 100f);
                    _lineWidth = EditorGUILayout.Slider("Width (m)", _lineWidth, 0.5f, 30f);
                    _lineDirection = EditorGUILayout.Vector3Field("Direction", _lineDirection);
                    if (GUILayout.Button("정규화 + Y평면 수평화", GUILayout.Height(20)))
                    {
                        _lineDirection.y = 0f;
                        if (_lineDirection.sqrMagnitude < 0.0001f) _lineDirection = Vector3.forward;
                        _lineDirection.Normalize();
                    }
                    break;

                case FakeRegionShape.Box:
                    _boxSize = EditorGUILayout.Vector3Field("Size (m)", _boxSize);
                    _boxSize.x = Mathf.Max(0.5f, _boxSize.x);
                    _boxSize.y = Mathf.Max(0.5f, _boxSize.y);
                    _boxSize.z = Mathf.Max(0.5f, _boxSize.z);
                    break;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawShapeButton(FakeRegionShape shape, string label)
        {
            GUI.backgroundColor = (_shape == shape) ? new Color(0.4f, 0.8f, 1f) : Color.white;
            if (GUILayout.Button(label, GUILayout.Height(30)))
            {
                _shape = shape;
            }
            GUI.backgroundColor = Color.white;
        }

        // ─────────────────────────────────────────────────────────────
        // OverrideMode
        // ─────────────────────────────────────────────────────────────

        private void DrawOverrideModeSection()
        {
            EditorGUILayout.LabelField("적용 방식", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = (_overrideMode == FakeOverrideMode.OR)
                ? new Color(0.3f, 0.9f, 1f) : Color.white;
            if (GUILayout.Button("OR (자연 + 가짜)", GUILayout.Height(26)))
                _overrideMode = FakeOverrideMode.OR;

            GUI.backgroundColor = (_overrideMode == FakeOverrideMode.Replace)
                ? new Color(1f, 0.4f, 0.8f) : Color.white;
            if (GUILayout.Button("Replace (자연 무시)", GUILayout.Height(26)))
                _overrideMode = FakeOverrideMode.Replace;

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                _overrideMode == FakeOverrideMode.OR
                    ? "→ 자연 발현 비트 + 가짜 비트 누적 (보통 사용)"
                    : "→ 자연 무시, 가짜 비트만 적용 (격리 검증)",
                EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────────────────────
        // 프리셋
        // ─────────────────────────────────────────────────────────────

        private void DrawPresetsSection()
        {
            EditorGUILayout.LabelField("프리셋 (8 개) — 클릭 시 비트마스크 일괄 적용",
                EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            int perRow = 2;
            for (int i = 0; i < PRESETS.Length; i += perRow)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = 0; j < perRow && i + j < PRESETS.Length; j++)
                {
                    var p = PRESETS[i + j];
                    var content = new GUIContent(p.label, p.tooltip);
                    if (GUILayout.Button(content, GUILayout.Height(28)))
                    {
                        _editIdentity = p.identity;
                        _editState = p.state;
                        _editEvent = p.eventTags;
                        _editPotential = p.potential;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All Clear", GUILayout.Height(20)))
            {
                _editIdentity = _editState = _editEvent = _editPotential = 0;
            }
            EditorGUILayout.LabelField("← 토글 모두 해제", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────────────────────
        // 정밀 비트마스크 토글 (4 카테고리 60 비트)
        // ─────────────────────────────────────────────────────────────

        private void DrawBitmaskSection()
        {
            EditorGUILayout.LabelField("정밀 비트마스크 (60 비트 개별 토글)",
                EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawBitsFoldout(ref _showIdentity,
                $"IDENTITY (14 비트, 0x{_editIdentity:X4})",
                ref _editIdentity, IDENTITY_LABELS);

            DrawBitsFoldout(ref _showState,
                $"STATE (16 비트, 0x{_editState:X4})",
                ref _editState, STATE_LABELS);

            DrawBitsFoldout(ref _showEvent,
                $"EVENT (14 비트, 0x{_editEvent:X4})",
                ref _editEvent, EVENT_LABELS);

            DrawBitsFoldout(ref _showPotential,
                $"POTENTIAL (15 비트, 0x{_editPotential:X4})",
                ref _editPotential, POTENTIAL_LABELS);

            EditorGUILayout.EndVertical();
        }

        private void DrawBitsFoldout(ref bool foldout, string title, ref uint mask, string[] labels)
        {
            foldout = EditorGUILayout.Foldout(foldout, title, true);
            if (!foldout) return;

            EditorGUI.indentLevel++;
            int perRow = 2;
            for (int i = 0; i < labels.Length; i += perRow)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = 0; j < perRow && i + j < labels.Length; j++)
                {
                    int bit = i + j;
                    uint flag = 1u << bit;
                    bool was = (mask & flag) != 0;
                    bool now = EditorGUILayout.ToggleLeft(labels[bit], was, GUILayout.Width(180));
                    if (now != was)
                    {
                        if (now) mask |= flag;
                        else mask &= ~flag;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        // ─────────────────────────────────────────────────────────────
        // 배치 모드 ON/OFF + 즉시 추가
        // ─────────────────────────────────────────────────────────────

        private void DrawPlacementSection()
        {
            EditorGUILayout.LabelField("배치", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 배치 모드 토글
            GUI.backgroundColor = _placementMode ? new Color(1f, 0.6f, 0.3f) : Color.white;
            string btnLabel = _placementMode
                ? "★ 배치 모드 ON — SceneView 좌클릭으로 영역 추가 (ESC: 해제)"
                : "배치 모드 OFF — 클릭하여 활성";
            if (GUILayout.Button(btnLabel, GUILayout.Height(30)))
            {
                _placementMode = !_placementMode;
                if (_placementMode) Focus();
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(2);

            // 즉시 추가 (수동 좌표)
            if (GUILayout.Button("현재 설정으로 (0, 0, 0) 에 즉시 추가", GUILayout.Height(22)))
            {
                AddRegionAt(Vector3.zero);
            }

            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────────────────────
        // 영역 리스트
        // ─────────────────────────────────────────────────────────────

        private void DrawRegionListSection()
        {
            var ctx = WorldAIContextManager.Instance;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("등록된 영역",
                EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            if (ctx != null && ctx.FakeRegionCount > 0)
            {
                if (GUILayout.Button("Clear All", GUILayout.Width(80)))
                {
                    if (EditorUtility.DisplayDialog(
                            "Clear All Fake Regions",
                            $"등록된 {ctx.FakeRegionCount} 개 영역을 모두 제거하시겠습니까?",
                            "Yes", "Cancel"))
                    {
                        ctx.ClearAllFakeRegions();
                        SceneView.RepaintAll();
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinHeight(120));

            if (ctx == null)
            {
                EditorGUILayout.LabelField("(Manager 인스턴스 없음 — Play 진입 후 표시)",
                    EditorStyles.miniLabel);
            }
            else if (ctx.FakeRegionCount == 0)
            {
                EditorGUILayout.LabelField("(아직 영역 없음 — 배치 모드로 추가하세요)",
                    EditorStyles.miniLabel);
            }
            else
            {
                _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.MaxHeight(200));

                var regions = ctx.GetFakeRegions();
                string toRemove = null;

                for (int i = 0; i < regions.Count; i++)
                {
                    var r = regions[i];
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                    // 토글
                    bool was = r.active;
                    bool now = EditorGUILayout.Toggle(was, GUILayout.Width(20));
                    if (now != was)
                    {
                        var modified = r;
                        modified.active = now;
                        ctx.AddFakeRegion(modified);   // 같은 id 면 갱신
                        SceneView.RepaintAll();
                    }

                    // 정보
                    string shapeStr = r.shape switch
                    {
                        FakeRegionShape.Sphere => $"Sphere r={r.radius:F1}",
                        FakeRegionShape.Box => $"Box {r.size.x:F0}x{r.size.y:F0}x{r.size.z:F0}",
                        FakeRegionShape.Line => $"Line L={r.length:F0} W={r.width:F0}",
                        _ => "?"
                    };
                    string modeStr = r.overrideMode == FakeOverrideMode.Replace ? "REP" : "OR";
                    string label = $"[{i:00}] {r.id}\n  {shapeStr} | {modeStr} @ ({r.center.x:F1},{r.center.y:F1},{r.center.z:F1})";
                    EditorGUILayout.LabelField(label, GUILayout.MinHeight(34));

                    // Focus 버튼
                    if (GUILayout.Button("👁", GUILayout.Width(28), GUILayout.Height(34)))
                    {
                        SceneView.lastActiveSceneView?.LookAt(r.center);
                    }

                    // 삭제
                    GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("✕", GUILayout.Width(28), GUILayout.Height(34)))
                    {
                        toRemove = r.id;
                    }
                    GUI.backgroundColor = Color.white;

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();

                if (toRemove != null)
                {
                    ctx.RemoveFakeRegion(toRemove);
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.EndVertical();
        }

        // ═════════════════════════════════════════════════════════════
        // SceneView GUI — 마우스 인터랙션 + 미리보기
        // ═════════════════════════════════════════════════════════════

        private void OnSceneGUI(SceneView sv)
        {
            // 마우스 → 월드 좌표
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            _hoverValid = Physics.Raycast(ray, out RaycastHit hit, 1000f);
            if (_hoverValid) _hoverWorldPos = hit.point;
            else
            {
                // 충돌 없을 때는 카메라에서 일정 거리 앞 평면
                Plane ground = new Plane(Vector3.up, Vector3.zero);
                if (ground.Raycast(ray, out float dist))
                {
                    _hoverWorldPos = ray.GetPoint(dist);
                    _hoverValid = true;
                }
            }

            // 배치 모드일 때만 미리보기 + 클릭 처리
            if (!_placementMode) return;

            // ★ v1.1 — 클릭 우선권 확보 (cave chunk 선택 차단)
            // GetControlID + AddDefaultControl 패턴으로 다른 모든 selectable 보다 우선
            int controlId = GUIUtility.GetControlID("FakeContextInjector".GetHashCode(), FocusType.Passive);

            if (Event.current.type == EventType.Layout)
            {
                // ★ 핵심 — 배치 모드 ON 일 때만 우선권 등록
                // OFF 일 때는 이 줄을 안 타니까 평소처럼 cave chunk 선택 정상 작동
                HandleUtility.AddDefaultControl(controlId);
            }

            // ESC → 배치 모드 해제
            if (Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Escape)
            {
                _placementMode = false;
                Event.current.Use();
                Repaint();
                sv.Repaint();
                return;
            }

            if (_hoverValid)
            {
                DrawPreviewGizmo(_hoverWorldPos);

                // 라벨
                Handles.Label(
                    _hoverWorldPos + Vector3.up * 0.5f,
                    $"[클릭하여 배치] {_shape} | {_overrideMode}\n" +
                    $"I=0x{_editIdentity:X4} S=0x{_editState:X4} " +
                    $"E=0x{_editEvent:X4} P=0x{_editPotential:X4}");
            }

            // 좌클릭 → 영역 추가 (Ctrl/Alt/Shift 없을 때만)
            // ★ v1.1 — HandleUtility.nearestControl == controlId 면 우리 control 이 hit
            var ev = Event.current;
            if (ev.type == EventType.MouseDown
                && ev.button == 0
                && !ev.control
                && !ev.alt
                && !ev.shift
                && _hoverValid
                && HandleUtility.nearestControl == controlId)
            {
                GUIUtility.hotControl = controlId;   // 클릭 잡기
                AddRegionAt(_hoverWorldPos);
                ev.Use();
                sv.Repaint();
                Repaint();
            }

            // ★ v1.1 — MouseUp 시 hotControl 놓기 (drag 처리 안 하므로 즉시)
            if (ev.type == EventType.MouseUp
                && ev.button == 0
                && GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                ev.Use();
            }

            // 마우스 움직임 / 드래그 시 SceneView Repaint (미리보기 따라가기)
            if (ev.type == EventType.MouseMove
                || ev.type == EventType.MouseDrag)
            {
                sv.Repaint();
            }
        }

        private void DrawPreviewGizmo(Vector3 pos)
        {
            Color c = _overrideMode == FakeOverrideMode.Replace
                ? new Color(1f, 0.4f, 0.8f, 0.5f)
                : new Color(0.3f, 0.9f, 1f, 0.5f);
            Handles.color = c;

            switch (_shape)
            {
                case FakeRegionShape.Sphere:
                    Handles.DrawWireDisc(pos, Vector3.up, _sphereRadius);
                    Handles.SphereHandleCap(0, pos, Quaternion.identity, _sphereRadius * 0.15f, EventType.Repaint);
                    break;

                case FakeRegionShape.Box:
                    {
                        Vector3 h = _boxSize * 0.5f;
                        Vector3[] pts = new[]
                        {
                        pos + new Vector3(-h.x, -h.y, -h.z), pos + new Vector3( h.x, -h.y, -h.z),
                        pos + new Vector3( h.x, -h.y, -h.z), pos + new Vector3( h.x, -h.y,  h.z),
                        pos + new Vector3( h.x, -h.y,  h.z), pos + new Vector3(-h.x, -h.y,  h.z),
                        pos + new Vector3(-h.x, -h.y,  h.z), pos + new Vector3(-h.x, -h.y, -h.z),
                        // 상단
                        pos + new Vector3(-h.x,  h.y, -h.z), pos + new Vector3( h.x,  h.y, -h.z),
                        pos + new Vector3( h.x,  h.y, -h.z), pos + new Vector3( h.x,  h.y,  h.z),
                        pos + new Vector3( h.x,  h.y,  h.z), pos + new Vector3(-h.x,  h.y,  h.z),
                        pos + new Vector3(-h.x,  h.y,  h.z), pos + new Vector3(-h.x,  h.y, -h.z),
                        // 수직 4 모서리
                        pos + new Vector3(-h.x, -h.y, -h.z), pos + new Vector3(-h.x,  h.y, -h.z),
                        pos + new Vector3( h.x, -h.y, -h.z), pos + new Vector3( h.x,  h.y, -h.z),
                        pos + new Vector3( h.x, -h.y,  h.z), pos + new Vector3( h.x,  h.y,  h.z),
                        pos + new Vector3(-h.x, -h.y,  h.z), pos + new Vector3(-h.x,  h.y,  h.z),
                    };
                        Handles.DrawLines(pts);
                        break;
                    }

                case FakeRegionShape.Line:
                    {
                        Vector3 dir = _lineDirection.sqrMagnitude > 0.0001f
                            ? _lineDirection.normalized
                            : Vector3.forward;
                        Vector3 a = pos - dir * (_lineLength * 0.5f);
                        Vector3 b = pos + dir * (_lineLength * 0.5f);
                        Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized * (_lineWidth * 0.5f);
                        Handles.DrawLine(a + perp, b + perp);
                        Handles.DrawLine(a - perp, b - perp);
                        Handles.DrawLine(a + perp, a - perp);
                        Handles.DrawLine(b + perp, b - perp);
                        Handles.DrawLine(a, b);
                        break;
                    }
            }
        }

        // ═════════════════════════════════════════════════════════════
        // AddRegionAt — 현재 설정으로 영역 생성 + 등록
        // ═════════════════════════════════════════════════════════════

        private void AddRegionAt(Vector3 pos)
        {
            var ctx = WorldAIContextManager.Instance;
            if (ctx == null)
            {
                Debug.LogWarning("[FakeContextInjector] WorldAIContextManager.Instance 없음 — Play 진입 필요");
                return;
            }

            string id = $"FakeRegion_{_nextRegionCounter:00}";
            _nextRegionCounter++;

            var region = new FakeContextRegion
            {
                id = id,
                active = true,
                shape = _shape,
                center = pos,
                radius = _sphereRadius,
                size = _boxSize,
                direction = _lineDirection.sqrMagnitude > 0.0001f ? _lineDirection.normalized : Vector3.forward,
                length = _lineLength,
                width = _lineWidth,
                identityTags = _editIdentity,
                stateTags = _editState,
                eventTags = _editEvent,
                potentialTags = _editPotential,
                overrideMode = _overrideMode,
            };

            ctx.AddFakeRegion(region);
            Debug.Log($"[FakeContextInjector] '{id}' 추가 ({_shape} @ {pos}, " +
                      $"I=0x{_editIdentity:X4} S=0x{_editState:X4} " +
                      $"E=0x{_editEvent:X4} P=0x{_editPotential:X4}, mode={_overrideMode})");

            Repaint();
            SceneView.RepaintAll();
        }

        // ═════════════════════════════════════════════════════════════
        // 비트 라벨 정의
        // ═════════════════════════════════════════════════════════════

        private static readonly string[] IDENTITY_LABELS = new[]
        {
            "SPAWN",            // 0
            "BOSS",             // 1
            "TREASURE",         // 2
            "NEXUS",            // 3
            "HUB",              // 4
            "APPROACH",         // 5
            "ESCAPE",           // 6
            "HAZARD",           // 7
            "BOSS_ARENA",       // 8
            "FRONTIER",         // 9
            "DECISION",         // 10
            "AMBUSH",           // 11
            "MAJOR_JUNCTION",   // 12
            "NOMAD",            // 13
        };

        private static readonly string[] STATE_LABELS = new[]
        {
            "NARROW (5~7m)",    // 0
            "STANDARD (7~10m)", // 1
            "WIDE (10~15m)",    // 2
            "GRAND (>15m)",     // 3
            "VERTICAL",         // 4
            "HORIZONTAL",       // 5
            "DEEP",             // 6
            "ELEVATED",         // 7
            "BOUNDARY",         // 8
            "MULTIBIOME",       // 9
            "DENSE",            // 10
            "ISOLATED",         // 11
            "ENCLOSED",         // 12
            "OPEN",             // 13
            "CURVED",           // 14
            "CRITICAL_RISK",    // 15
        };

        private static readonly string[] EVENT_LABELS = new[]
        {
            "REPOSITIONED",     // 0
            "DESPERATELY_PLACED", // 1
            "CHAMBER_LOCKED",   // 2
            "RADIUS_REDUCED",   // 3
            "DELTA_SPLIT",      // 4
            "GUARD_ACTIVE",     // 5
            "G2_EXPANDED",      // 6
            "M5_OFFSET_LARGE",  // 7
            "CROSSOVER_DETECTED", // 8
            "ROLE_CHANGED",     // 9
            "BIOME_QUOTA_FAIL", // 10
            "PASS2_RESOLVED",   // 11
            "CLUSTER_DETECTED", // 12
            "SUB5M_VIOLATION",  // 13
        };

        private static readonly string[] POTENTIAL_LABELS = new[]
        {
            "AMBUSH",                 // 0
            "HIDDEN_LOOT",            // 1
            "FALL_HAZARD",            // 2
            "RUSH_DOWN",              // 3
            "BOTTLENECK",             // 4
            "VANTAGE_POINT",          // 5
            "ECHO_CHAMBER",           // 6
            "SACRED_VENUE",           // 7
            "ENVIRONMENTAL_TRAP",     // 8
            "RITUAL_SITE",            // 9
            "CHASE_SCENE",            // 10
            "COVER_DENSE",            // 11
            "SOUNDSCAPE_WIND",        // 12
            "LOST_PLAYER",            // 13
            "MULTI_PATH_CHOICE",      // 14
        };
    }
}
#endif