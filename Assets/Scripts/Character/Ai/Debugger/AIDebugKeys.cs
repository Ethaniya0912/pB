// =============================================================================
// AIDebugKeys.cs  |  pB-4 AI 디버거 단축키 상수 정의
//
// 목적:
//   AIPerceptionDebugger, AIPerceptionDebuggerEditor, AIDebuggerSettingsWindow
//   세 파일이 참조하는 단축키 상수를 한 곳에 모아 관리한다.
//   단축키가 변경될 경우 이 파일 1곳만 수정하면
//   Inspector HelpBox / Tooltip / 런타임 동작 모두 자동으로 동기화된다.
//
// 사용 예:
//   if (Input.GetKeyDown(AIDebugKeys.TOGGLE_F1)) { ... }
//   tooltip = AIDebugKeys.HELP_TEXT;  // Inspector 표시용 전체 안내 문자열
// =============================================================================

using UnityEngine;

namespace TDA.PB4.Diagnostics
{
    /// <summary>
    /// AIPerceptionDebugger 단축키 상수 집합.
    /// 모든 디버거 파일이 이 클래스를 공통 참조한다.
    /// </summary>
    public static class AIDebugKeys
    {
        // ── F키 단축키 ──────────────────────────────────────────────────────

        /// <summary>F1: 디버그 모드 순환 (Minimum → Standard → Focus → Off)</summary>
        public const KeyCode CYCLE_MODE = KeyCode.F1;

        /// <summary>F2: 현재 AI 핀 고정 / 해제 (선택 해제 후에도 전체 패널 유지)</summary>
        public const KeyCode PIN_AI = KeyCode.F2;

        /// <summary>F3: GL 3D 오버레이 ON/OFF (링·호·연결선)</summary>
        public const KeyCode TOGGLE_GL = KeyCode.F3;

        // ── Ctrl+Shift 단축키 ────────────────────────────────────────────────

        /// <summary>Ctrl+Shift+B: BT 그래프 패널 크기 순환 (전체→축소→숨김)</summary>
        public const KeyCode BT_GRAPH   = KeyCode.B;

        /// <summary>Ctrl+Shift+C: 캐릭터 상태 패널 ON/OFF</summary>
        public const KeyCode CHAR_PANEL  = KeyCode.C;

        /// <summary>Ctrl+Shift+L: LOD 설정 EditorWindow 열기</summary>
        public const KeyCode LOD_WINDOW  = KeyCode.L;

        // ── 수식 키 확인 헬퍼 ────────────────────────────────────────────────

        /// <summary>Ctrl+Shift+{key} 조합 감지 헬퍼.</summary>
        public static bool CtrlShift(KeyCode key)
            => (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            && (Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift))
            && Input.GetKeyDown(key);

        // ── Inspector / HelpBox 표시용 문자열 ───────────────────────────────

        /// <summary>
        /// Inspector CustomEditor HelpBox에 표시하는 전체 단축키 안내 문자열.
        /// AIDebugKeys 상수를 직접 삽입하므로 수정 시 자동 동기화된다.
        /// </summary>
        public static string HelpText =>
            $"[ AI Debugger 단축키 안내 ]\n" +
            $"\n" +
            $"{CYCLE_MODE}          ─ 디버그 모드 순환: Minimum → Standard → Focus → Off\n" +
            $"{PIN_AI}          ─ 현재 AI 핀 고정 / 해제  (📌)\n" +
            $"{TOGGLE_GL}          ─ GL 3D 오버레이 ON/OFF  (링·호·연결선)\n" +
            $"\n" +
            $"Ctrl+Shift+{BT_GRAPH}  ─ BT 그래프 패널 크기 순환 (전체 → 축소 → 숨김)\n" +
            $"Ctrl+Shift+{CHAR_PANEL}  ─ 캐릭터 상태 패널 ON/OFF\n" +
            $"Ctrl+Shift+{LOD_WINDOW}  ─ LOD 설정 창 열기\n" +
            $"\n" +
            $"[ 모드 표시: 패널 헤더 우측 (MIN)/(STD)/(FOC)/(OFF) ]\n" +
            $"[ F2 핀 AI는 거리·모드 무관 전체 패널 유지 + 📌 표시 ]";
    }
}
