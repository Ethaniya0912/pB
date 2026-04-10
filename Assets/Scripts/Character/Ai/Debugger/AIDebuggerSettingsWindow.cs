// =============================================================================
// AIDebuggerSettingsWindow.cs  |  AI 디버거 LOD 설정 EditorWindow
//
// 접근 방법:
//   · 단축키 Ctrl+Shift+L (Play 중 AIPerceptionDebugger.Update에서 열기)
//   · 메뉴: pB-4 Debug → AI Debugger Settings
//
// 역할:
//   AIDebuggerSettings SO의 모든 필드를 슬라이더/토글로 편집한다.
//   설정 변경은 즉시 반영된다 (SO static ref 직접 참조).
//   SO가 없으면 자동 생성 후 저장한다 (Assets/Editor/Debugger/ 경로).
//
// 구조:
//   ├ LOD 거리 슬라이더 (3개)
//   ├ 패널 표시항목 토글 (Foldout 그룹)
//   ├ GL 3D 오버레이 토글 (Foldout 그룹)
//   ├ 신규 기능 토글 (BT 그래프 / 캐릭터 패널)
//   ├ 겹침 전략 선택
//   └ 프리셋 버튼 (Developer / Performance / Presentation)
// =============================================================================

#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;

namespace TDA.PB4.Diagnostics
{
    /// <summary>
    /// AI 디버거 설정 EditorWindow.
    /// Ctrl+Shift+L 단축키 또는 pB-4 Debug 메뉴로 열 수 있다.
    /// </summary>
    public class AIDebuggerSettingsWindow : EditorWindow
    {
        // ── 메뉴 진입점 ──────────────────────────────────────────────────────

        // 단축키 없음 — 런타임에서 Ctrl+Shift+L (AIDebugKeys.LOD_WINDOW)로 열림
        // Unity 내장 Ctrl+Shift+L (Generate Lighting)과의 충돌을 방지하기 위해 제거.
        [MenuItem("pB-4 Debug/AI Debugger Settings", priority = 10)]
        public static void Open()
        {
            var win = GetWindow<AIDebuggerSettingsWindow>(false, "AI Debugger Settings");
            win.minSize = new Vector2(320f, 480f);
            win.Show();
        }

        // ── Foldout 상태 ──────────────────────────────────────────────────────

        private bool _foldPanel = true;   // 패널 표시 항목 그룹
        private bool _foldGL = true;   // GL 오버레이 항목 그룹
        private bool _foldNew = true;   // 신규 기능 그룹
        private bool _foldOverlap = true;  // 겹침 전략 그룹
        private bool _foldMobAvoid = true; // 몹 가림 방지 그룹

        // ── 스크롤 위치 ──────────────────────────────────────────────────────

        private Vector2 _scroll;

        // ── 스타일 캐시 ──────────────────────────────────────────────────────

        private GUIStyle _headerStyle;
        private GUIStyle _sectionStyle;
        private bool _stylesReady;

        // ── 렌더링 ──────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_stylesReady) InitStyles();

            // Settings SO 가져오기 (없으면 자동 생성)
            var s = EnsureSettings();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // ── 헤더 ──
            EditorGUILayout.Space(6);
            GUILayout.Label("AI Debugger Settings", _headerStyle);
            DrawSeparator();

            // ── 단축키 안내 ──
            EditorGUILayout.HelpBox(AIDebugKeys.HelpText, MessageType.Info);
            EditorGUILayout.Space(4);

            // ── LOD 거리 슬라이더 ──
            GUILayout.Label("LOD 거리 임계값", _sectionStyle);

            EditorGUI.BeginChangeCheck();
            s.lodIconDist = EditorGUILayout.Slider(new GUIContent("Icon Only (m)",
                "이 거리 이상: Winner 아이콘 원만 표시. 패널 없음."), s.lodIconDist, 10f, 120f);
            s.lodHeaderDist = EditorGUILayout.Slider(new GUIContent("Header Only (m)",
                "이 거리 이상: 이름 + Winner 헤더만 표시."), s.lodHeaderDist, 5f, 80f);
            s.lodSummaryDist = EditorGUILayout.Slider(new GUIContent("Summary (m)",
                "이 거리 이상: Awareness + Fear 바 추가 표시."), s.lodSummaryDist, 2f, 60f);
            s.lodFullDist = EditorGUILayout.Slider(new GUIContent("Full Info (m)",
                "이 거리 이하: BB 변수 + Target + Group 전체 표시."), s.lodFullDist, 1f, 40f);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(s);

            EditorGUILayout.Space(4);
            s.baseFontSize = EditorGUILayout.IntSlider(new GUIContent("기본 폰트 크기",
                "패널 기본 폰트 크기(pt). LOD에 따라 자동 축소됨."), s.baseFontSize, 8, 16);
            s.fontSizeHeader = EditorGUILayout.IntSlider("헤더 폰트 크기", s.fontSizeHeader, 0, 18);
            s.fontSizeBody = EditorGUILayout.IntSlider("본문 폰트 크기", s.fontSizeBody, 0, 18);
            s.fontSizeBB = EditorGUILayout.IntSlider("BB 값 폰트 크기", s.fontSizeBB, 0, 18);
            s.fontSizeBTNode = EditorGUILayout.IntSlider("BT 노드 폰트 크기", s.fontSizeBTNode, 0, 14);
            s.fontSizeCharPanel = EditorGUILayout.IntSlider("캐릭터 패널 폰트", s.fontSizeCharPanel, 0, 18);

            DrawSeparator();

            // ── 패널 표시 항목 ──
            _foldPanel = EditorGUILayout.Foldout(_foldPanel, "패널 표시 항목", true, _sectionStyle);
            if (_foldPanel)
            {
                EditorGUI.indentLevel++;
                DrawToggle(s, ref s.showBBVariables, "Blackboard 변수", "PolicyType, EngageRange, StalkSpeed 등 BB 변수 섹션");
                DrawToggle(s, ref s.showTargetInfo, "타겟 정보", "TARGET 이름 + 거리");
                DrawToggle(s, ref s.showMemoryInfo, "메모리 마커", "LastSeen / LastHeard 거리");
                DrawToggle(s, ref s.showGroupInfo, "그룹 정보", "근접 동료 AI 수");
                DrawToggle(s, ref s.showActionStatus, "Action 상태", "⚡ isPerformingAction 표시");
                DrawToggle(s, ref s.showUtilityBars, "Utility 점수 바", "Attack/Flee/Patrol/Idle 4종 바 (Scene View 전용)");
                EditorGUI.indentLevel--;
            }

            DrawSeparator();

            // ── GL 3D 오버레이 ──
            _foldGL = EditorGUILayout.Foldout(_foldGL, "GL 3D 오버레이 (F3)", true, _sectionStyle);
            if (_foldGL)
            {
                EditorGUI.indentLevel++;
                DrawToggle(s, ref s.showVisionCone, "시야각 콘", "Awareness 상태별 색상 부채꼴");
                DrawToggle(s, ref s.showHearingCircle, "청각 반경 원", "hearingRange 기반 수평 원");
                DrawToggle(s, ref s.showEngageRing, "EngageRange 링", "전투 진입 반경 링");
                DrawToggle(s, ref s.showNavPath, "NavMesh 경로", "경로 꺾임선 (Partial=주황)");
                DrawToggle(s, ref s.showTargetLine, "타겟 연결선", "AI → Target 빨간 라인");
                DrawToggle(s, ref s.showGroupLines, "그룹 연결선", "AI ↔ 동료 시안 점선");
                DrawToggle(s, ref s.showMemoryMarkers, "메모리 마커", "LastSeen/LastHeard × 크로스 마커");
                EditorGUI.indentLevel--;
            }

            DrawSeparator();

            // ── 신규 기능 ──
            _foldNew = EditorGUILayout.Foldout(_foldNew, "신규 기능 (v4.0)", true, _sectionStyle);
            if (_foldNew)
            {
                EditorGUI.indentLevel++;
                // 공통 파라미터 (모든 패널에 적용)
                s.theme = (AIDebuggerSettings.BTTheme)EditorGUILayout.EnumPopup(
                    new GUIContent("테마 (공통)", "Dark/Light 테마 전체 적용"), s.theme);
                s.panelCornerRadius = EditorGUILayout.IntSlider("모서리 반경 (px)", s.panelCornerRadius, 0, 20);
                s.panelBorderWidth = EditorGUILayout.IntSlider("테두리 두께 (px)", s.panelBorderWidth, 1, 8);
                s.panelOpacity = EditorGUILayout.Slider("전체 패널 불투명도", s.panelOpacity, 0f, 1f);
                // 개별 투명도 (req #8)
                s.nodeOpacity = EditorGUILayout.Slider("노드 불투명도", s.nodeOpacity, 0f, 1f);
                s.lineOpacity = EditorGUILayout.Slider("스플라인 불투명도", s.lineOpacity, 0f, 1f);
                s.bubbleOpacity = EditorGUILayout.Slider("버블 불투명도", s.bubbleOpacity, 0f, 1f);
                // 버블 속도 모드 (req #6)
                s.bubbleSpeedMode = (AIDebuggerSettings.BubbleSpeedMode)EditorGUILayout.EnumPopup("버블 속도 모드", s.bubbleSpeedMode);
                s.bubbleSpeed = EditorGUILayout.Slider("버블 기본 속도", s.bubbleSpeed, 0.1f, 2.0f);
                s.bubbleCountPer100px = EditorGUILayout.IntSlider("버블 개수(/100px)", s.bubbleCountPer100px, 1, 8);
                DrawToggle(s, ref s.showGroupPanel, "그룹 AI 패널", "그룹원 상태 목록 패널 ON/OFF.");
                DrawToggle(s, ref s.showBTGraph, "BT 그래프 패널", "Ctrl+Shift+B로 크기 순환. Scene View/Game View 공용.");
                DrawToggle(s, ref s.showBTConditionValues, "컨디션 노드 BB값",
                    "컨디션 노드 아래 Blackboard 변수 현재값 ON/OFF.");
                DrawToggle(s, ref s.showCharacterPanel, "캐릭터 상태 패널", "Ctrl+Shift+C로 토글. HP/STA/NavMesh/Animator 표시.");

                EditorGUI.BeginChangeCheck();
                s.btGraphDisplayMode = (AIDebuggerSettings.BTGraphDisplayMode)EditorGUILayout.EnumPopup(
                    new GUIContent("BT 표시 형식", "NodeGraph=시각 노드 그래프 / TextTree=텍스트 계층형 목록"),
                    s.btGraphDisplayMode);

                s.theme = (AIDebuggerSettings.BTTheme)EditorGUILayout.EnumPopup(
                    new GUIContent("BT 테마", "Dark=어두운 배경 / Light=밝은 배경"), s.theme);
                s.panelOpacity = EditorGUILayout.Slider(
                    new GUIContent("패널 불투명도 (공통)", "0.3=반투명 ~ 1.0=완전불투명"), s.panelOpacity, 0.3f, 1.0f);
                s.btBubbleSize = EditorGUILayout.Slider(
                    new GUIContent("버블 크기", "버블 플로우 점 크기 (0.5=작게~8=크게)"), s.btBubbleSize, 0.5f, 8.0f);
                s.btGraphScale = EditorGUILayout.Slider(
                    new GUIContent("BT 그래프 스케일", "0.4=40%(작게) ~ 1.5=150%(크게). 화면 밖 삐져나올 때 줄임."),
                    s.btGraphScale, 0.4f, 1.5f);

                s.btGraphAnchor = (AIDebuggerSettings.BTGraphAnchor)EditorGUILayout.EnumPopup(
                    new GUIContent("BT 위치 앵커", "Auto=자동(공간 부족 시 좌측) / Right=항상 오른쪽 / Left=항상 왼쪽"),
                    s.btGraphAnchor);

                s.btNodeVGap = EditorGUILayout.Slider(
                    new GUIContent("BT 수직 간격 (px)", "노드 수직 간격. 클수록 화살표 곡선 여유 증가."),
                    s.btNodeVGap, 16f, 80f);
                s.btNodeHGap = EditorGUILayout.Slider(
                    new GUIContent("BT 수평 간격 (px)", "형제 노드 수평 간격."),
                    s.btNodeHGap, 4f, 40f);

                // ── 노드 크기 ────────────────────────────────────────────
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("── BT 노드 크기 ──", EditorStyles.boldLabel);
                s.btNodeWidth = EditorGUILayout.Slider(
                    new GUIContent("노드 너비 (px)", "BT 그래프 노드 박스 너비."),
                    s.btNodeWidth, 80f, 400f);
                s.btNodeHeight = EditorGUILayout.Slider(
                    new GUIContent("노드 높이 (px)", "BT 그래프 노드 박스 높이."),
                    s.btNodeHeight, 14f, 80f);

                // ── 거리 스케일 ──────────────────────────────────────────
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("── 거리 스케일 (GUI.matrix) ──", EditorStyles.boldLabel);
                s.useMatrixScale = EditorGUILayout.Toggle(
                    new GUIContent("Matrix 스케일 ON", "ON: 거리에 따라 패널 전체를 스케일. 텍스트 품질 유지."),
                    s.useMatrixScale);
                using (new EditorGUI.DisabledScope(!s.useMatrixScale))
                {
                    s.scaleRefDist = EditorGUILayout.Slider(
                        new GUIContent("기준 거리 (m)", "이 거리에서 scale=1.0. 가까울수록 크게 보임."),
                        s.scaleRefDist, 2f, 20f);
                    s.scaleExp = EditorGUILayout.Slider(
                        new GUIContent("감쇠 지수", "1=선형, 1.2=완만, 2=급격 축소."),
                        s.scaleExp, 0.5f, 2.5f);
                    s.scaleMin = EditorGUILayout.Slider(
                        new GUIContent("최소 scale (숨김 기준)", "이 값 이하이면 패널 대신 아이콘만 표시."),
                        s.scaleMin, 0.15f, 0.7f);
                }

                // ── 스냅 위치 ────────────────────────────────────────────
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("── 패널 스냅 위치 ──", EditorStyles.boldLabel);
                s.snapSide = (AIDebuggerSettings.PanelSnapSide)EditorGUILayout.EnumPopup(
                    new GUIContent("스냅 방향", "Auto: 화면 여백 기준 자동 선택."),
                    s.snapSide);
                s.snapOffsetX = EditorGUILayout.Slider(
                    new GUIContent("수평 여백 (px)", "캐릭터 중심에서 패널까지 수평 간격."),
                    s.snapOffsetX, 0f, 100f);
                s.snapOffsetY = EditorGUILayout.Slider(
                    new GUIContent("수직 오프셋 (px)", "양수=아래, 음수=위."),
                    s.snapOffsetY, -200f, 200f);
                s.snapWorldY = EditorGUILayout.Slider(
                    new GUIContent("기준점 높이 (m)", "3D 스냅 기준 높이 오프셋 (월드 단위)."),
                    s.snapWorldY, 0f, 4f);

                // ── 연결선 ───────────────────────────────────────────────
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("── 패널 연결선 ──", EditorStyles.boldLabel);
                s.showConnectorLine = EditorGUILayout.Toggle(
                    new GUIContent("연결선 표시", "패널 ↔ 캐릭터 연결선 표시 여부."),
                    s.showConnectorLine);
                using (new EditorGUI.DisabledScope(!s.showConnectorLine))
                {
                    s.connectorWidth = EditorGUILayout.Slider(
                        new GUIContent("선 두께 (px)", "연결선 두께."),
                        s.connectorWidth, 0.5f, 4f);
                    s.connectorColor = EditorGUILayout.ColorField(
                        new GUIContent("선 색상"), s.connectorColor);
                    s.connectorDash = EditorGUILayout.Slider(
                        new GUIContent("점선 간격 (px)", "0=실선, 값이 클수록 점선 간격 넓음."),
                        s.connectorDash, 0f, 16f);
                    s.connectorDotR = EditorGUILayout.Slider(
                        new GUIContent("끝 마커 크기 (px)", "캐릭터 위치 원형 마커 반지름. 0=없음."),
                        s.connectorDotR, 0f, 8f);
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("── 색상 커스터마이징 ──", EditorStyles.boldLabel);
                bool showColors = EditorGUILayout.Foldout(
                    EditorPrefs.GetBool("BT_COLOR_FOLD", false), "노드/선/텍스트 색상");
                EditorPrefs.SetBool("BT_COLOR_FOLD", showColors);
                if (showColors)
                {
                    EditorGUI.indentLevel++;
                    bool _isLightMode = s.theme == AIDebuggerSettings.BTTheme.Light;
                    EditorGUILayout.HelpBox(
                        _isLightMode ? "Light 테마 색상 설정" : "Dark 테마 색상 설정",
                        MessageType.Info);

                    // ── 공통 패널 색상 (테마별) ──
                    if (_isLightMode)
                    {
                        s.panelBgColorLight = EditorGUILayout.ColorField("패널 배경 (Light)", s.panelBgColorLight);
                        s.panelBdColorLight = EditorGUILayout.ColorField("패널 테두리 (Light)", s.panelBdColorLight);
                    }
                    else
                    {
                        s.panelBgColor = EditorGUILayout.ColorField("패널 배경 (Dark)", s.panelBgColor);
                        s.panelBdColor = EditorGUILayout.ColorField("패널 테두리 (Dark)", s.panelBdColor);
                    }

                    EditorGUILayout.Space(4);
                    // ── BT 노드 색상 ──
                    if (!_isLightMode)
                    {
                        s.btColorNodeDefault = EditorGUILayout.ColorField("노드 기본 배경", s.btColorNodeDefault);
                        s.btColorNodeComposite = EditorGUILayout.ColorField("복합(Composite)", s.btColorNodeComposite);
                        s.btColorNodeRunning = EditorGUILayout.ColorField("실행중 배경", s.btColorNodeRunning);
                        s.btColorNodeSuccess = EditorGUILayout.ColorField("성공 배경", s.btColorNodeSuccess);
                        s.btColorNodeFailure = EditorGUILayout.ColorField("실패 배경", s.btColorNodeFailure);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Light 모드: 노드 색상 자동 파스텔 적용됨", MessageType.None);
                    }
                    s.btColorLineActive = EditorGUILayout.ColorField("활성 연결선", s.btColorLineActive);
                    s.btColorLineInactive = EditorGUILayout.ColorField("비활성 연결선", s.btColorLineInactive);
                    s.btColorBubble = EditorGUILayout.ColorField("버블 색상", s.btColorBubble);
                    s.btColorTextNormal = EditorGUILayout.ColorField("텍스트 기본", s.btColorTextNormal);
                    s.btColorTextRunning = EditorGUILayout.ColorField("텍스트 실행중", s.btColorTextRunning);
                    s.btColorTextBBKey = EditorGUILayout.ColorField("BB 변수명 색상", s.btColorTextBBKey);
                    s.btColorTextBBValue = EditorGUILayout.ColorField("BB 값 색상", s.btColorTextBBValue);
                    EditorGUI.indentLevel--;
                }
                if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(s);
                EditorGUI.indentLevel--;
            }

            DrawSeparator();

            // ── 겹침 전략 ──
            _foldOverlap = EditorGUILayout.Foldout(_foldOverlap, "다중 AI 겹침 전략", true, _sectionStyle);
            if (_foldOverlap)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();

                s.overlapMode = (AIDebuggerSettings.OverlapMode)EditorGUILayout.EnumPopup(
                    new GUIContent("Overlap Mode",
                        "None = 처리 안 함\n" +
                        "GridJitter = Y축 순차 밀어내기\n" +
                        "FocusOnly = F2 핀된 AI만 표시, 나머지 숨김\n" +
                        "SeparateForce = X·Y 반발력으로 자연스럽게 분산\n" +
                        "SnapSide = 캐릭터 화면 위치 기반 좌·우 스냅 + Y 분리\n" +
                        "Auto = SnapSide + Y 밀기 혼합 (권장)"),
                    s.overlapMode);

                // GridJitter 전용
                if (s.overlapMode == AIDebuggerSettings.OverlapMode.GridJitter)
                    s.jitterCellWidth = EditorGUILayout.Slider(
                        new GUIContent("Jitter Cell Width (px)", "셀 기준 너비. 작을수록 더 세밀하게 분리."),
                        s.jitterCellWidth, 60f, 300f);

                // SeparateForce 전용
                if (s.overlapMode == AIDebuggerSettings.OverlapMode.SeparateForce)
                {
                    s.separateForceStrength = EditorGUILayout.Slider(
                        new GUIContent("반발 강도", "0.1=약하게 ~ 1.0=강하게 밀어냄"),
                        s.separateForceStrength, 0.1f, 1.0f);
                    s.separateForceIterations = EditorGUILayout.IntSlider(
                        new GUIContent("반복 횟수", "많을수록 겹침 해소 정확도↑, 성능↓"),
                        s.separateForceIterations, 1, 10);
                }

                // FocusOnly 안내
                if (s.overlapMode == AIDebuggerSettings.OverlapMode.FocusOnly)
                    EditorGUILayout.HelpBox(
                        "F2 키로 핀(📌)된 AI 패널만 표시됩니다.\n" +
                        "핀된 AI가 없으면 모든 패널이 숨겨집니다.",
                        MessageType.Info);

                if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(s);
                EditorGUI.indentLevel--;
            }

            DrawSeparator();

            // ── 몹 가림 방지 ──
            _foldMobAvoid = EditorGUILayout.Foldout(_foldMobAvoid, "몹 가림 방지", true, _sectionStyle);
            if (_foldMobAvoid)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();

                s.avoidMobOcclusion = EditorGUILayout.Toggle(
                    new GUIContent("가림 방지 ON",
                        "패널이 해당 AI 캐릭터의 화면 예상 영역을 가리지 않도록 바깥으로 밀어냄.\n" +
                        "Game View · Scene View 모두 적용."),
                    s.avoidMobOcclusion);

                using (new EditorGUI.DisabledScope(!s.avoidMobOcclusion))
                {
                    s.mobExclusionW = EditorGUILayout.Slider(
                        new GUIContent("회피 너비 (px)",
                            "캐릭터 화면 점유 예상 너비.\n카메라가 가까울수록 키우세요."),
                        s.mobExclusionW, 30f, 500f);

                    s.mobExclusionH = EditorGUILayout.Slider(
                        new GUIContent("회피 높이 (px)",
                            "캐릭터 화면 점유 예상 높이.\n캐릭터가 크게 보일 때 키우세요."),
                        s.mobExclusionH, 30f, 700f);

                    EditorGUILayout.HelpBox(
                        "패널 그룹이 캐릭터 영역과 겹칠 때 바깥으로 밀어냅니다.\n" +
                        "기준점: 캐릭터 허리 높이(snapWorldY) 기준 중앙 Rect.\n" +
                        "SnapWorldY와 함께 조절하면 더 정확합니다.",
                        MessageType.None);
                }

                if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(s);
                EditorGUI.indentLevel--;
            }

            DrawSeparator();

            // ── 프리셋 ──
            GUILayout.Label("프리셋", _sectionStyle);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(new GUIContent("개발자 기본", "전 항목 ON. 개발 중 사용.")))
            { s.ApplyPreset(AIDebuggerSettings.Preset.Developer); EditorUtility.SetDirty(s); }

            if (GUILayout.Button(new GUIContent("성능 최적화", "GL/BT/캐릭터 패널 OFF. 프로파일링 시 사용.")))
            { s.ApplyPreset(AIDebuggerSettings.Preset.Performance); EditorUtility.SetDirty(s); }

            if (GUILayout.Button(new GUIContent("발표용", "핵심 시각 요소만. 큰 폰트. 겹침 최소화.")))
            { s.ApplyPreset(AIDebuggerSettings.Preset.Presentation); EditorUtility.SetDirty(s); }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // ── 저장 / 초기화 ──
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("저장", GUILayout.Height(28)))
            {
                EditorUtility.SetDirty(s);
                AssetDatabase.SaveAssets();
                Debug.Log("[AIDebuggerSettings] 저장 완료.");
            }
            if (GUILayout.Button("기본값 복원", GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("기본값 복원",
                    "AI 디버거 설정을 개발자 기본값으로 초기화합니다.", "확인", "취소"))
                {
                    s.ApplyPreset(AIDebuggerSettings.Preset.Developer);
                    EditorUtility.SetDirty(s);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();

            // 변경 시 자동 저장 (Auto-save on dirty)
            if (GUI.changed)
                EditorUtility.SetDirty(s);
        }

        // ── 헬퍼 ─────────────────────────────────────────────────────────────

        /// <summary>GUIStyle을 초기화한다 (lazy).</summary>
        private void InitStyles()
        {
            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft,
            };
            _sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
            };
            _stylesReady = true;
        }

        /// <summary>수평 구분선을 그린다.</summary>
        private static void DrawSeparator()
        {
            EditorGUILayout.Space(4);
            Rect r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, new Color(0.3f, 0.5f, 0.7f, 0.4f));
            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// ref bool 필드에 대한 토글을 그리고 변경 시 SetDirty를 호출한다.
        /// </summary>
        private static void DrawToggle(AIDebuggerSettings s, ref bool field, string label, string tooltip)
        {
            EditorGUI.BeginChangeCheck();
            field = EditorGUILayout.Toggle(new GUIContent(label, tooltip), field);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(s);
        }

        /// <summary>
        /// Settings SO를 가져온다. 없으면 Assets/Editor/Debugger/ 에 자동 생성한다.
        /// </summary>
        private static AIDebuggerSettings EnsureSettings()
        {
            var s = AIDebuggerSettings.Get();
            if (s != null) return s;

            // 자동 생성
            s = CreateInstance<AIDebuggerSettings>();
            const string dir = "Assets/Editor/Debugger";
            const string path = dir + "/AIDebuggerSettings.asset";

            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            AssetDatabase.CreateAsset(s, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AIDebuggerSettings] 자동 생성: {path}");
            return s;
        }
    }
}

#endif  // UNITY_EDITOR