// =============================================================================
// AIDebuggerSettings.cs  |  pB-4 AI 디버거 LOD · 표시항목 설정 SO
//
// 배치:
//   Assets/Editor/Debugger/AIDebuggerSettings.asset
//   Editor 폴더 → 빌드 미포함. 에디터 전용.
//
// 역할:
//   AIPerceptionDebugger가 읽는 모든 거리 임계값과 표시 토글을 저장.
//   AIDebuggerSettingsWindow (Ctrl+Shift+L)에서 Inspector UI 제공.
//   설정 변경은 즉시 반영(static ref 직접 참조 → 캐시 없음).
//
// 사용 방법:
//   AIDebuggerSettings.Instance.showBBVariables  // 런타임에서 읽기
//   AIDebuggerSettings.Get()                     // 없으면 기본값 객체 반환
// =============================================================================

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TDA.PB4.Diagnostics
{
    /// <summary>
    /// AI 디버거 설정 ScriptableObject.
    /// Editor 폴더에 배치되어 빌드에 포함되지 않는다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "AIDebuggerSettings",
        menuName = "pB-4 Debug/AI Debugger Settings",
        order = 10)]
    public class AIDebuggerSettings : ScriptableObject
    {
        // ── 싱글턴 참조 (static) ─────────────────────────────────────────────

        /// <summary>
        /// static 싱글턴 참조.
        /// AIPerceptionDebugger가 매 프레임 이 필드를 직접 읽으므로 캐시 비용 없음.
        /// </summary>
        public static AIDebuggerSettings Instance { get; private set; }

        /// <summary>
        /// Instance가 null이면 기본값을 가진 임시 객체를 반환.
        /// 어떤 경우에도 NullReferenceException이 발생하지 않도록 보장한다.
        /// </summary>
        public static AIDebuggerSettings Get()
        {
            if (Instance != null) return Instance;
#if UNITY_EDITOR
            // Editor 폴더 내 Asset 자동 탐색
            string[] guids = AssetDatabase.FindAssets("t:AIDebuggerSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                Instance = AssetDatabase.LoadAssetAtPath<AIDebuggerSettings>(path);
            }
            if (Instance == null)
            {
                // 없으면 임시 기본값 반환 (저장 안 됨)
                Instance = CreateInstance<AIDebuggerSettings>();
            }
#endif
            return Instance;
        }

        // ── LOD 거리 임계값 ──────────────────────────────────────────────────

        [Header("── LOD 거리 임계값 ──")]
        [Tooltip("이 거리 이상: Winner 아이콘(색상 원)만 표시. 패널 없음.")]
        [Range(10f, 100f)] public float lodIconDist = 40f;

        [Tooltip("이 거리 이상: 헤더만 표시 (이름 + Winner).")]
        [Range(5f, 60f)] public float lodHeaderDist = 25f;

        [Tooltip("이 거리 이상: 요약 표시 (Awareness + Fear 바 추가).")]
        [Range(2f, 40f)] public float lodSummaryDist = 14f;

        [Tooltip("이 거리 이하: 전체 정보 표시 (BB 변수 + Target + Group 등 포함).")]
        [Range(1f, 20f)] public float lodFullDist = 8f;

        // ── 패널 표시 항목 토글 ──────────────────────────────────────────────

        [Header("── 패널 표시 항목 ──")]

        [Tooltip("Blackboard 변수 섹션 표시 (PolicyType, EngageRange, StalkSpeed 등).")]
        public bool showBBVariables = true;

        [Tooltip("TARGET 섹션 표시 (타겟 이름 + 거리).")]
        public bool showTargetInfo = true;

        [Tooltip("MEMORY 섹션 표시 (LastSeen / LastHeard 거리).")]
        public bool showMemoryInfo = true;

        [Tooltip("GROUP 섹션 표시 (근접 동료 AI 수).")]
        public bool showGroupInfo = true;

        [Tooltip("⚡ isPerformingAction 표시.")]
        public bool showActionStatus = true;

        [Tooltip("Utility 점수 바 표시 (Attack / Flee / Patrol / Idle). Scene View 전용.")]
        public bool showUtilityBars = true;

        // ── GL 3D 오버레이 항목 ──────────────────────────────────────────────

        [Header("── GL 3D 오버레이 (Game View + Scene View) ──")]

        [Tooltip("시야각 콘 (Awareness 상태별 색상).")]
        public bool showVisionCone = true;

        [Tooltip("청각 반경 원 (hearingRange 기준).")]
        public bool showHearingCircle = true;

        [Tooltip("전투 진입 반경 링 (EngageRange BB 변수 기준).")]
        public bool showEngageRing = true;

        [Tooltip("NavMesh 경로 (PathComplete=청록, PathPartial=주황).")]
        public bool showNavPath = true;

        [Tooltip("타겟 연결선 (AI → currentTarget 빨간 라인).")]
        public bool showTargetLine = true;

        [Tooltip("그룹 멤버 연결선 (AI ↔ 동료 시안 점선).")]
        public bool showGroupLines = true;

        [Tooltip("LastSeen / LastHeard 크로스 마커.")]
        public bool showMemoryMarkers = true;

        // ── 신규 기능 토글 ────────────────────────────────────────────────────

        [Header("── 신규 기능 (F-1 / F-3) ──")]

        [Tooltip("BT 그래프 패널 표시 여부. Ctrl+Shift+B로 크기 순환.")]
        public bool showBTGraph = true;

        /// <summary>BT 그래프 표시 모드. NodeGraph=시각 그래프, TextTree=텍스트 계층형.</summary>
        public enum BTGraphDisplayMode { NodeGraph, TextTree }

        /// <summary>BT 그래프 위치. Auto=화면 끝 감지 시 자동 좌측 이동.</summary>
        public enum BTGraphAnchor { Auto, Right, Left }

        [Tooltip("BT 그래프 표시 형식. NodeGraph=시각 노드 그래프, TextTree=텍스트 트리 계층.")]
        public BTGraphDisplayMode btGraphDisplayMode = BTGraphDisplayMode.NodeGraph;

        [Tooltip("BT 그래프 전체 스케일 (0.4=40% ~ 1.5=150%). 화면이 작을 때 줄임.")]
        [Range(0.4f, 1.5f)] public float btGraphScale = 0.85f;

        [Tooltip("그래프 위치. Auto=오른쪽 공간 부족 시 왼쪽 자동 이동, Right/Left=고정.")]
        public BTGraphAnchor btGraphAnchor = BTGraphAnchor.Auto;

        /// <summary>컨디션 노드 Blackboard 변수 현재값 표시.</summary>
        [Tooltip("BT 그래프 컨디션 노드 아래에 연결된 BB 변수 현재값 표시 (ON/OFF).")]
        public bool showBTConditionValues = true;

        // ── BT 그래프 테마 & 외관 옵션 ─────────────────────────────────
        /// <summary>BT 그래프 테마.</summary>
        public enum BTTheme { Dark, Light }

        // btTheme, btPanelOpacity → 공통 파라미터(theme, panelOpacity)로 통합됨

        [Tooltip("버블 플로우 점 크기 (0.5=작게 ~ 8.0=크게)")]
        [Range(0.5f, 8.0f)] public float btBubbleSize = 3.8f;

        // ── 색상 커스터마이징 ──────────────────────────────────────────
        [Header("── BT 노드 배경색 ──")]
        public Color btColorNodeDefault = new Color(0.10f, 0.12f, 0.18f, 1f);
        public Color btColorNodeComposite = new Color(0.08f, 0.11f, 0.22f, 1f);
        public Color btColorNodeRunning = new Color(0.20f, 0.15f, 0.04f, 1f);
        public Color btColorNodeSuccess = new Color(0.06f, 0.18f, 0.09f, 1f);
        public Color btColorNodeFailure = new Color(0.22f, 0.06f, 0.06f, 1f);
        public Color btColorNodeWaiting = new Color(0.06f, 0.10f, 0.22f, 1f);

        [Header("── BT 선/화살표 색상 ──")]
        public Color btColorLineActive = new Color(1.00f, 0.88f, 0.08f, 0.95f);
        public Color btColorLineInactive = new Color(0.22f, 0.42f, 0.62f, 0.50f);
        public Color btColorBubble = new Color(1.00f, 0.97f, 0.55f, 0.85f);

        [Header("── BT 텍스트 색상 ──")]
        public Color btColorTextNormal = new Color(0.88f, 0.88f, 0.88f, 1f);
        public Color btColorTextRunning = new Color(1.00f, 0.95f, 0.35f, 1f);
        public Color btColorTextBBKey = new Color(0.55f, 0.88f, 1.00f, 1f);
        public Color btColorTextBBValue = new Color(1.00f, 0.88f, 0.55f, 1f);
        public Color btColorTextHeader = new Color(0.40f, 0.85f, 1.00f, 1f);

        /// <summary>노드 수직 간격 (px). 클수록 베지어 곡선 여유 증가.</summary>
        [Tooltip("BT 노드 수직 간격 (px). 값이 클수록 화살표 곡선 공간 증가.")]
        [Range(16f, 80f)] public float btNodeVGap = 36f;

        /// <summary>형제 노드 수평 간격 (px).</summary>
        [Tooltip("BT 노드 수평 간격 (px). 형제 노드 사이 여백.")]
        [Range(4f, 40f)] public float btNodeHGap = 10f;

        [Header("BT 노드 크기 (px)")]
        [Tooltip("노드 박스 너비. 넓을수록 텍스트가 더 잘 보임.")]
        [Range(80f, 400f)] public float btNodeWidth = 128f;
        [Tooltip("노드 박스 높이.")]
        [Range(14f, 80f)] public float btNodeHeight = 20f;

        // ── GUI.matrix 거리 스케일 ─────────────────────────────────────
        [Header("거리 스케일 (GUI.matrix)")]
        [Tooltip("ON: 거리에 따라 패널 전체를 matrix 스케일. LOD 대체.")]
        public bool useMatrixScale = true;
        [Tooltip("이 거리에서 scale=1.0 (기준 거리). 단위 m.")]
        [Range(2f, 20f)] public float scaleRefDist = 6f;
        [Tooltip("감쇠 지수. 1=선형, 1.2=완만, 2=급격 축소.")]
        [Range(0.5f, 2.5f)] public float scaleExp = 1.0f;  // 1.0=원근 정확(3D 고정 느낌)
        [Tooltip("최소 scale. 이 값 이하이면 패널 숨김.")]
        [Range(0.15f, 0.7f)] public float scaleMin = 0.35f;

        // ── 캐릭터 스냅 위치 ───────────────────────────────────────────
        public enum PanelSnapSide { Auto, Left, Right }
        [Header("패널 스냅 위치")]
        [Tooltip("Auto: 화면 여백 기준 자동 선택.")]
        public PanelSnapSide snapSide = PanelSnapSide.Auto;
        [Tooltip("캐릭터 중심에서 패널까지 수평 여백 (px, unscaled).")]
        [Range(0f, 100f)] public float snapOffsetX = 16f;
        [Tooltip("수직 오프셋 (px, unscaled). 양수=아래, 음수=위.")]
        [Range(-200f, 200f)] public float snapOffsetY = 0f;
        [Tooltip("3D 스냅 기준점 높이 오프셋 (월드 단위).")]
        [Range(0f, 4f)] public float snapWorldY = 0.5f;  // 허리 높이 기준

        // ── 연결선 ────────────────────────────────────────────────────
        [Header("패널 연결선")]
        [Tooltip("패널 ↔ 캐릭터 연결선 표시 여부.")]
        public bool showConnectorLine = true;
        [Tooltip("연결선 두께 (px).")]
        [Range(0.5f, 4f)] public float connectorWidth = 1.5f;
        [Tooltip("연결선 색상.")]
        public Color connectorColor = new Color(0.40f, 0.80f, 1.00f, 0.65f);
        [Tooltip("점선 간격. 0=실선.")]
        [Range(0f, 16f)] public float connectorDash = 4f;
        [Tooltip("연결선 끝 원형 마커 반지름 (px). 0=없음.")]
        [Range(0f, 8f)] public float connectorDotR = 3f;

        [Tooltip("캐릭터 상태 패널 표시 여부. Ctrl+Shift+C로 토글.")]
        public bool showCharacterPanel = false;

        // ── 다중 AI 겹침 전략 ────────────────────────────────────────────────

        // NOTE: [Header]는 필드(field)에만 사용 가능. enum 선언 위에 붙이면 CS0592 발생.
        //       enum을 먼저 선언하고 [Header]는 그 아래 실제 필드에 부착한다.

        /// <summary>패널 겹침 처리 방식 선택.</summary>
        public enum OverlapMode
        {
            /// <summary>겹침 처리 안 함. 성능 최우선.</summary>
            None,

            /// <summary>Grid Cell 기반 Y축 밀어내기 (기본).</summary>
            GridJitter,

            /// <summary>핀된 AI만 전체 표시, 나머지 숨김.</summary>
            FocusOnly,

            /// <summary>반발력(Spring Force) 기반 자연스러운 분산 배치.</summary>
            SeparateForce,

            /// <summary>캐릭터의 화면 위치 기반으로 좌·우 중 여백이 많은 쪽에 스냅.</summary>
            SnapSide,

            /// <summary>Auto: SnapSide로 좌/우 결정 후 여전히 겹치면 Y축 밀어내기 추가.</summary>
            Auto
        }

        [Header("── 다중 AI 겹침 전략 ──")]
        [Tooltip("None=처리안함 / GridJitter=Y밀기(기본) / SeparateForce=반발력 / SnapSide=좌우스냅 / Auto=혼합")]
        public OverlapMode overlapMode = OverlapMode.Auto;

        [Tooltip("GridJitter에서 같은 셀에 묶이는 기준 너비(px).")]
        [Range(60f, 300f)] public float jitterCellWidth = 140f;

        [Header("── SeparateForce / SnapSide 설정 ──")]
        [Tooltip("SeparateForce 반발 강도 (0.1=약 ~ 1.0=강)")]
        [Range(0.1f, 1.0f)] public float separateForceStrength = 0.5f;
        [Tooltip("SeparateForce 반복 횟수 (많을수록 정확하나 성능 주의)")]
        [Range(1, 10)] public int separateForceIterations = 4;

        // ── 몹 가림 방지 ─────────────────────────────────────────────────────
        [Header("── 몹 가림 방지 ──")]
        [Tooltip("ON: 패널이 해당 AI 캐릭터의 화면 예상 영역을 가리지 않도록 바깥으로 밀어냄.\n" +
                 "Game View · Scene View 모두 적용.")]
        public bool avoidMobOcclusion = true;

        [Tooltip("캐릭터 화면 점유 예상 너비 (px). 클수록 더 넓은 영역을 회피합니다.\n" +
                 "캐릭터가 크게 보일 때 늘리세요.")]
        [Range(30f, 500f)] public float mobExclusionW = 100f;

        [Tooltip("캐릭터 화면 점유 예상 높이 (px). 클수록 더 넓은 영역을 회피합니다.\n" +
                 "카메라가 가까울 때 늘리세요.")]
        [Range(30f, 700f)] public float mobExclusionH = 220f;

        // ── 패널 스타일 ────────────────────────────────────────────────────────

        [Header("── 패널 스타일 ──")]

        [Tooltip("기본 폰트 크기 (pt). LOD에 따라 자동 축소됨.")]
        [Range(8, 16)] public int baseFontSize = 11;

        [Header("── 부분별 폰트 크기 ──")]
        [Tooltip("메인 패널 헤더 폰트 (0=baseFontSize 사용)")]
        [Range(0, 18)] public int fontSizeHeader = 0;
        [Tooltip("메인 패널 본문 폰트 (0=baseFontSize 사용)")]
        [Range(0, 18)] public int fontSizeBody = 0;
        [Tooltip("BB 값 표시 폰트 (0=baseFontSize-1 사용)")]
        [Range(0, 18)] public int fontSizeBB = 0;
        [Tooltip("BT 그래프 노드 레이블 폰트 (0=자동)")]
        [Range(0, 14)] public int fontSizeBTNode = 0;
        [Tooltip("캐릭터 패널 폰트 (0=baseFontSize-1 사용)")]
        [Range(0, 18)] public int fontSizeCharPanel = 0;

        // ── 프리셋 (빠른 설정) ────────────────────────────────────────────────

        // NOTE: [Header]는 필드에만 유효. enum 선언 위에 붙이면 CS0592 발생.
        //       Preset enum은 ApplyPreset()의 파라미터 타입으로만 사용되므로
        //       대응 필드가 없다. 헤더는 주석으로 대체한다.

        /// <summary>인스펙터 버튼으로 호출하는 빠른 프리셋 목록.</summary>
        public enum Preset
        {
            /// <summary>개발자 기본: 전 항목 ON.</summary>
            Developer,

            /// <summary>성능 최적화: GL/BT그래프/캐릭터 패널 OFF.</summary>
            Performance,

            /// <summary>발표용: 핵심 정보만, 겹침 최소화, 폰트 크게.</summary>
            Presentation
        }

        // ══════════════════════════════════════════════════════════════════
        //  공통 테마 & 패널 파라미터
        // ══════════════════════════════════════════════════════════════════

        [Header("── 공통 테마 ──")]
        [Tooltip("Dark=어두운 배경, Light=밝은 배경 (전체 패널에 적용)")]
        public BTTheme theme = BTTheme.Dark;

        // ── 패널 레이아웃 & 위치 ──────────────────────────────────────────
        // NOTE: enum은 [Header] 위에 선언해야 CS0592 오류 방지
        public enum CharPanelAnchor { RightOfMain, BelowMain, Custom }
        public enum GroupPanelAnchor { BelowChar, BelowMain, Custom }

        [Header("── 공통 패널 파라미터 ──")]

        [Header("── 패널 레이아웃 ──")]
        [Tooltip("캐릭터 패널 기준 위치 (RightOfMain=주 패널 우측, BelowMain=아래, Custom=자유 드래그)")]
        public CharPanelAnchor charPanelAnchor = CharPanelAnchor.RightOfMain;
        [Tooltip("그룹 AI 패널 기준 위치")]
        public GroupPanelAnchor groupPanelAnchor = GroupPanelAnchor.BelowChar;

        [Header("── 패널 커스텀 드래그 오프셋 (Custom 모드 시 적용) ──")]
        public Vector2 mainPanelOffset = Vector2.zero;
        public Vector2 charPanelOffset = Vector2.zero;
        public Vector2 groupPanelOffset = Vector2.zero;
        public Vector2 btPanelOffset = Vector2.zero;

        [Tooltip("모든 패널의 모서리 반경 (px, 0=사각형, 20=완전 라운드)")]
        [Range(0, 20)] public int panelCornerRadius = 2;
        [Tooltip("모든 패널 불투명도")]
        [Range(0f, 1f)] public float panelOpacity = 0.90f;

        [Tooltip("모든 패널 테두리 두께 (px, 1~8)")]
        [Range(1, 8)] public int panelBorderWidth = 2;

        [Header("── Dark 테마 패널 색상 ──")]
        public Color panelBgColor = new Color(0.07f, 0.08f, 0.11f, 1f);
        public Color panelBdColor = new Color(0.28f, 0.76f, 1.00f, 1f);

        [Header("── Light 테마 패널 색상 ──")]
        public Color panelBgColorLight = new Color(0.88f, 0.91f, 0.95f, 1f);
        public Color panelBdColorLight = new Color(0.22f, 0.50f, 0.85f, 1f);

        // ══════════════════════════════════════════════════════════════════
        //  개별 투명도 옵션 (req #8)
        // ══════════════════════════════════════════════════════════════════

        [Header("── 투명도 개별 설정 ──")]
        [Tooltip("노드 박스 투명도")]
        [Range(0f, 1f)] public float nodeOpacity = 0.92f;
        [Tooltip("스플라인 + 화살표 투명도")]
        [Range(0f, 1f)] public float lineOpacity = 0.85f;
        [Tooltip("버블 투명도")]
        [Range(0f, 1f)] public float bubbleOpacity = 0.85f;

        // ══════════════════════════════════════════════════════════════════
        //  버블 플로우 옵션 (req #6)
        // ══════════════════════════════════════════════════════════════════

        public enum BubbleSpeedMode
        {
            /// <summary>방식1: 고정 속도 (스플라인 길이 무관)</summary>
            Fixed,
            /// <summary>방식2: 길이 보정 — 모든 선에서 동일 시각 속도</summary>
            Normalized,
            /// <summary>방식3: 트리 깊이 기반 가속 — On Start 느림, 말단 빠름</summary>
            Accelerate,
            /// <summary>방식4: 같은 선 내에서 시작 느리고 끝 빠름 (EaseIn)</summary>
            EaseIn,
        }

        [Header("── 버블 플로우 ──")]
        [Tooltip("버블 이동 속도 모드")]
        public BubbleSpeedMode bubbleSpeedMode = BubbleSpeedMode.Fixed;
        [Tooltip("기본 버블 이동 속도 (0.2=느림 ~ 2.0=빠름)")]
        [Range(0.1f, 2.0f)] public float bubbleSpeed = 0.65f;
        [Tooltip("스플라인 100px 당 버블 개수 (1~8)")]
        [Range(1, 8)] public int bubbleCountPer100px = 4;

        // ══════════════════════════════════════════════════════════════════
        //  그룹 AI 패널 (req #10)
        // ══════════════════════════════════════════════════════════════════

        [Header("── 그룹 AI 패널 ──")]
        [Tooltip("그룹 AI 정보 패널 표시 여부")]
        public bool showGroupPanel = true;

        // Light/Dark 테마별 색상 저장 (req #12)
        // EditorPrefs 키를 통해 에디터에서 기억

        /// <summary>선택한 프리셋을 적용한다.</summary>

        public void ApplyPreset(Preset preset)
        {
            switch (preset)
            {
                case Preset.Developer:
                    // 전체 ON, 기본값 복원
                    showBBVariables = showTargetInfo = showMemoryInfo = true;
                    showGroupInfo = showActionStatus = showUtilityBars = true;
                    showVisionCone = showHearingCircle = showEngageRing = true;
                    showNavPath = showTargetLine = showGroupLines = true;
                    showMemoryMarkers = showBTGraph = true;
                    showCharacterPanel = false;
                    overlapMode = OverlapMode.GridJitter;
                    baseFontSize = 11;
                    break;

                case Preset.Performance:
                    // GL / BT / 캐릭터 패널 OFF. 핵심만 ON.
                    showBBVariables = showTargetInfo = showActionStatus = true;
                    showVisionCone = showHearingCircle = false;
                    showEngageRing = showNavPath = showTargetLine = false;
                    showGroupLines = showMemoryMarkers = false;
                    showBTGraph = showCharacterPanel = false;
                    showUtilityBars = false;
                    overlapMode = OverlapMode.GridJitter;
                    baseFontSize = 9;
                    break;

                case Preset.Presentation:
                    // 시야/청각 ON, BT 숨김, 큰 폰트, 겹침 최소화
                    showBBVariables = true; showTargetInfo = true;
                    showMemoryInfo = false; showGroupInfo = false;
                    showVisionCone = true; showHearingCircle = true;
                    showEngageRing = true; showNavPath = false;
                    showTargetLine = true; showGroupLines = false;
                    showMemoryMarkers = false;
                    showBTGraph = false; showCharacterPanel = false;
                    overlapMode = OverlapMode.FocusOnly;
                    baseFontSize = 13;
                    break;
            }

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        // ── Unity 생명주기 ────────────────────────────────────────────────────

        private void OnEnable()
        {
            // SO가 로드될 때 싱글턴으로 등록
            Instance = this;
        }
    }
}