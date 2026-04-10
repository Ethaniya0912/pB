// =============================================================================
// AIPerceptionDebuggerEditor.cs  |  AIPerceptionDebugger CustomEditor
//
// 역할:
//   ① Inspector 상단에 단축키 안내 HelpBox 표시
//   ② 런타임 상태(모드, 핀, GL) ReadOnly 필드로 실시간 표시
//   ③ 기본 Inspector 프로퍼티 렌더링 유지
//
// 단축키 동기화:
//   AIDebugKeys.HelpText를 참조하므로 AIDebugKeys.cs를 수정하면
//   이 파일을 건드리지 않아도 Inspector가 자동으로 갱신된다.
//
// 배치:
//   Assets/Editor/Debugger/AIPerceptionDebuggerEditor.cs
//   (Editor 폴더 → 빌드 미포함)
// =============================================================================

#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Reflection;

namespace TDA.PB4.Diagnostics
{
    /// <summary>
    /// AIPerceptionDebugger 전용 CustomEditor.
    /// Inspector에 단축키 안내와 런타임 상태를 표시한다.
    /// </summary>
    // NOTE: 'Editor'를 단독으로 쓰면 프로젝트 내 Editor 네임스페이스와 충돌해
    //       CS0118이 발생한다. UnityEditor.Editor로 명시적으로 지정한다.
    [CustomEditor(typeof(AIPerceptionDebugger))]
    public class AIPerceptionDebuggerEditor : UnityEditor.Editor
    {
        // ── Reflection 캐시 (private static field 읽기) ──────────────────────

        /// <summary>AIPerceptionDebugger._debugMode (DebugMode enum, static)</summary>
        private static FieldInfo s_debugModeField;

        /// <summary>AIPerceptionDebugger._glOverlay (bool, static)</summary>
        private static FieldInfo s_glOverlayField;

        /// <summary>AIPerceptionDebugger._pinnedAI (GameObject, static)</summary>
        private static FieldInfo s_pinnedField;

        /// <summary>AIPerceptionDebugger._btGraphSize (int, static)</summary>
        private static FieldInfo s_btGraphSizeField;

        /// <summary>AIPerceptionDebugger._charPanelVisible (bool, static)</summary>
        private static FieldInfo s_charPanelField;

        // ── GUIStyle 캐시 ──────────────────────────────────────────────────────

        private GUIStyle _badgeStyle;
        private bool _stylesReady;

        // ── 런타임 상태 색상 ──────────────────────────────────────────────────

        private static readonly Color COLOR_ON = new Color(0.28f, 0.85f, 0.45f, 1f);  // 활성 (초록)
        private static readonly Color COLOR_OFF = new Color(0.75f, 0.28f, 0.28f, 1f);  // 비활성 (빨강)
        private static readonly Color COLOR_MODE = new Color(0.28f, 0.65f, 1.00f, 1f);  // 모드 표시 (파랑)

        // ── 초기화 ────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            CacheReflectionFields();
        }

        /// <summary>static private 필드 FieldInfo를 한 번만 캐시한다.</summary>
        private static void CacheReflectionFields()
        {
            if (s_debugModeField != null) return;  // 이미 캐시됨

            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var t = typeof(AIPerceptionDebugger);

            s_debugModeField = t.GetField("_debugMode", flags);
            s_glOverlayField = t.GetField("_glOverlay", flags);
            s_pinnedField = t.GetField("_pinnedAI", flags);
            s_btGraphSizeField = t.GetField("_btGraphSize", flags);
            s_charPanelField = t.GetField("_charPanelVisible", flags);
        }

        // ── Inspector 렌더링 ──────────────────────────────────────────────────

        public override void OnInspectorGUI()
        {
            if (!_stylesReady) InitStyles();

            // ── 단축키 안내 HelpBox ──
            // AIDebugKeys.HelpText를 참조 — 단축키 변경 시 자동 동기화
            EditorGUILayout.HelpBox(AIDebugKeys.HelpText, MessageType.Info);

            EditorGUILayout.Space(4);

            // ── 런타임 상태 표시 (Play Mode에서만 의미 있음) ──
            if (Application.isPlaying)
            {
                DrawRuntimeState();
                EditorGUILayout.Space(4);
                // 런타임 중에는 매 프레임 갱신
                Repaint();
            }
            else
            {
                EditorGUILayout.HelpBox("Play Mode 진입 시 런타임 상태가 여기에 표시됩니다.", MessageType.None);
            }

            // ── 기본 Inspector 프로퍼티 ──
            EditorGUILayout.Space(4);
            DrawDefaultInspector();

            // ── Settings 바로가기 버튼 ──
            EditorGUILayout.Space(6);
            if (GUILayout.Button("LOD 설정 창 열기  (Ctrl+Shift+L)", GUILayout.Height(26)))
                AIDebuggerSettingsWindow.Open();
        }

        /// <summary>런타임 static 상태를 Reflection으로 읽어 ReadOnly 필드로 표시한다.</summary>
        private void DrawRuntimeState()
        {
            EditorGUILayout.LabelField("─ 런타임 상태 (Read Only) ─", EditorStyles.boldLabel);

            // DebugMode
            string mode = "---";
            if (s_debugModeField != null)
                mode = s_debugModeField.GetValue(null)?.ToString() ?? "---";
            DrawReadOnlyField("Debug Mode", mode);

            // GL Overlay
            bool glOn = s_glOverlayField != null && (bool)(s_glOverlayField.GetValue(null) ?? false);
            DrawColoredBool("GL 3D 오버레이 (F3)", glOn);

            // Pinned AI
            var pinned = s_pinnedField?.GetValue(null) as GameObject;
            DrawReadOnlyField("Pinned AI (F2)", pinned != null ? pinned.name : "없음 (핀 해제 상태)");

            // BT Graph Size
            int btSize = s_btGraphSizeField != null ? (int)(s_btGraphSizeField.GetValue(null) ?? 0) : 0;
            string btSizeStr = btSize switch { 0 => "전체 (Full)", 1 => "축소 (Compact)", _ => "숨김 (Hidden)" };
            DrawReadOnlyField("BT 그래프 크기 (Ctrl+Shift+B)", btSizeStr);

            // Character Panel
            bool charOn = s_charPanelField != null && (bool)(s_charPanelField.GetValue(null) ?? false);
            DrawColoredBool("캐릭터 패널 (Ctrl+Shift+C)", charOn);
        }

        // ── 헬퍼 ─────────────────────────────────────────────────────────────

        /// <summary>문자열 ReadOnly 필드를 그린다.</summary>
        private static void DrawReadOnlyField(string label, string value)
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(label, value);
            EditorGUI.EndDisabledGroup();
        }

        /// <summary>bool 상태를 ON/OFF 색상 배지로 그린다.</summary>
        private void DrawColoredBool(string label, bool value)
        {
            EditorGUILayout.BeginHorizontal();

            // 라벨
            EditorGUILayout.PrefixLabel(label);

            // 색상 배지
            Color prev = GUI.color;
            GUI.color = value ? COLOR_ON : COLOR_OFF;
            GUILayout.Label(value ? "● ON" : "● OFF", _badgeStyle, GUILayout.Width(60f));
            GUI.color = prev;

            EditorGUILayout.EndHorizontal();
        }

        private void InitStyles()
        {
            _badgeStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
            };
            _stylesReady = true;
        }
    }
}

#endif  // UNITY_EDITOR