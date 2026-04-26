// =============================================================================
// SpeechTestWindow.cs  |  pB-4 Week 2 Day 4 — Speech 시스템 통합 디버그 도구
// Layer  : Editor (UnityEditor 전용, 빌드 타겟에서 자동 제외)
// Owner  : Person A
//
// 역할:
//   Window/pB-4/Speech Test Lab 메뉴로 접근. 씬에서 발화 시스템을
//   대화식으로 테스트:
//     - 4 진영 강제 전이 (안정화 시간 무시)
//     - 8 trigger ID 직접 발화 (검문 우회 옵션)
//     - 검문 상태 라이브 모니터링
//     - 다국어 강제 (Korean/English/Japanese)
//
// 사용법:
//   Unity 메뉴 → pB-4 → Speech Test Lab
//   대상 NPC를 Hierarchy에서 선택
//   버튼 클릭으로 즉시 발화/전이
//
// Production:
//   #if UNITY_EDITOR로 감싸 빌드 시 자동 제외.
// =============================================================================
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TDA.PB4.AI;
using TDA.PB4.Core.Events;
using TDA.PB4.Data;
using TDA.PB4.Interfaces.Narrative;
// [모호성 회피] Day 4 Speech 클래스만 alias로 명시 (Week 7 SpeechAssembler와 충돌 방지)
using SpeechAssembler = TDA.PB4.AI.Speech.SpeechAssembler;
using SpeechDispatcher = TDA.PB4.AI.Speech.SpeechDispatcher;
using DialogueRenderer = TDA.PB4.AI.Speech.DialogueRenderer;

namespace TDA.PB4.Tooling.Editor
{
    public class SpeechTestWindow : EditorWindow
    {
        private GameObject targetNPC;
        private SpeechDispatcher dispatcher;
        private SpeechAssembler assembler;
        private DialogueRenderer renderer;
        private NPCAlignmentController alignment;

        private string customTriggerId = "friendly_default";
        private SystemLanguage forceLanguage = SystemLanguage.Unknown;
        private bool bypassFilters = false;

        // [Tone 직접 테스트용] DialogueRenderer.Render(line, tone) 직접 호출
        private string customToneText = "이것은 톤 테스트 대사입니다.";
        private TDA.PB4.AI.Speech.SpeechTone selectedTone = TDA.PB4.AI.Speech.SpeechTone.Neutral;

        private Vector2 scrollPos;

        [MenuItem("pB-4/Speech Test Lab")]
        public static void ShowWindow()
        {
            var window = GetWindow<SpeechTestWindow>("Speech Test Lab");
            window.minSize = new Vector2(380, 600);
        }

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // === 헤더 ===
            EditorGUILayout.LabelField("pB-4 Speech 시스템 디버그", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "씬에서 NPC를 선택한 뒤 아래 버튼으로 발화/전이를 강제로 트리거합니다.\n" +
                "Play 모드 전용. 단독 Play / Host 권한에서만 동작.",
                MessageType.Info);
            EditorGUILayout.Space(8);

            // === 대상 NPC 선택 ===
            EditorGUILayout.LabelField("━━━ 대상 NPC ━━━", EditorStyles.boldLabel);
            var newTarget = (GameObject)EditorGUILayout.ObjectField(
                "Target NPC", targetNPC, typeof(GameObject), true);

            if (newTarget != targetNPC)
            {
                targetNPC = newTarget;
                ResolveComponents();
            }

            // 선택된 NPC 자동 사용
            if (GUILayout.Button("선택된 GameObject로 자동 설정"))
            {
                if (Selection.activeGameObject != null)
                {
                    targetNPC = Selection.activeGameObject;
                    ResolveComponents();
                }
            }

            if (targetNPC == null)
            {
                EditorGUILayout.HelpBox("대상 NPC를 지정하세요. Hierarchy에서 NPC를 클릭한 뒤 위 버튼을 누르세요.", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            // === 컴포넌트 상태 ===
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("━━━ 컴포넌트 상태 ━━━", EditorStyles.boldLabel);
            DrawComponentStatus("SpeechDispatcher", dispatcher != null);
            DrawComponentStatus("SpeechAssembler", assembler != null);
            DrawComponentStatus("DialogueRenderer", renderer != null);
            DrawComponentStatus("NPCAlignmentController", alignment != null);

            if (dispatcher == null || assembler == null || renderer == null)
            {
                EditorGUILayout.HelpBox("Speech 컴포넌트가 부족합니다. HumanoidBootstrapper가 정상 부착했는지 확인하세요.", MessageType.Error);
            }

            // === 진영 강제 전이 ===
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("━━━ 4 진영 강제 전이 ━━━", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "안정화 시간(5초)을 무시하고 즉시 전이.\n전이 시 'XXX_default' 발화도 자동 발행됩니다.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(alignment == null || !Application.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Hostile", GUILayout.Height(28)))
                    alignment.DebugForceTransition(NPCAlignment.Hostile);
                if (GUILayout.Button("Neutral", GUILayout.Height(28)))
                    alignment.DebugForceTransition(NPCAlignment.Neutral);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Friendly", GUILayout.Height(28)))
                    alignment.DebugForceTransition(NPCAlignment.Friendly);
                if (GUILayout.Button("Companion", GUILayout.Height(28)))
                    alignment.DebugForceTransition(NPCAlignment.Companion);
                EditorGUILayout.EndHorizontal();
            }

            // === 직접 발화 (8 trigger) ===
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("━━━ 직접 발화 (검문 적용) ━━━", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(dispatcher == null || !Application.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("hostile_default", GUILayout.Height(24)))
                    dispatcher.DebugForceSpeech("hostile_default");
                if (GUILayout.Button("neutral_default", GUILayout.Height(24)))
                    dispatcher.DebugForceSpeech("neutral_default");
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("friendly_default", GUILayout.Height(24)))
                    dispatcher.DebugForceSpeech("friendly_default");
                if (GUILayout.Button("companion_default", GUILayout.Height(24)))
                    dispatcher.DebugForceSpeech("companion_default");
                EditorGUILayout.EndHorizontal();
            }

            // === 커스텀 트리거 + 검문 우회 ===
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("━━━ 커스텀 트리거 ━━━", EditorStyles.boldLabel);
            customTriggerId = EditorGUILayout.TextField("Trigger ID", customTriggerId);
            bypassFilters = EditorGUILayout.Toggle("모든 검문 우회 (디버그)", bypassFilters);

            using (new EditorGUI.DisabledScope(dispatcher == null || !Application.isPlaying))
            {
                if (GUILayout.Button($"발화 (bypass={bypassFilters})", GUILayout.Height(28)))
                    dispatcher.DebugForceSpeech(customTriggerId, bypassFilters);
            }

            // === Tone 직접 테스트 (Renderer만 호출, Assembler 우회) ===
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("━━━ Tone 직접 테스트 ━━━", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "DialogueRenderer를 직접 호출하여 Tone별 시각 스타일만 검증.\n" +
                "Assembler를 우회하므로 SO/Variant 매칭과 무관.\n" +
                "DialoguePreset_Warm/Cold/Neutral/Aggressive/Anxious의 색상/페이드 등을 비교할 때 사용.",
                MessageType.None);

            customToneText = EditorGUILayout.TextField("테스트 텍스트", customToneText);

            using (new EditorGUI.DisabledScope(renderer == null || !Application.isPlaying))
            {
                // 5개 Tone 버튼
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Warm", GUILayout.Height(28)))
                    renderer.Render(customToneText, TDA.PB4.AI.Speech.SpeechTone.Warm);
                if (GUILayout.Button("Cold", GUILayout.Height(28)))
                    renderer.Render(customToneText, TDA.PB4.AI.Speech.SpeechTone.Cold);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Neutral", GUILayout.Height(28)))
                    renderer.Render(customToneText, TDA.PB4.AI.Speech.SpeechTone.Neutral);
                if (GUILayout.Button("Aggressive", GUILayout.Height(28)))
                    renderer.Render(customToneText, TDA.PB4.AI.Speech.SpeechTone.Aggressive);
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("Anxious", GUILayout.Height(28)))
                    renderer.Render(customToneText, TDA.PB4.AI.Speech.SpeechTone.Anxious);

                // 커스텀 Tone 드롭다운
                EditorGUILayout.Space(4);
                selectedTone = (TDA.PB4.AI.Speech.SpeechTone)EditorGUILayout.EnumPopup("선택 Tone", selectedTone);
                if (GUILayout.Button($"선택 Tone({selectedTone})으로 발화", GUILayout.Height(24)))
                    renderer.Render(customToneText, selectedTone);
            }

            // === 다국어 강제 ===
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("━━━ 다국어 테스트 ━━━", EditorStyles.boldLabel);
            forceLanguage = (SystemLanguage)EditorGUILayout.EnumPopup("Force Language", forceLanguage);

            using (new EditorGUI.DisabledScope(assembler == null || !Application.isPlaying))
            {
                if (GUILayout.Button("Assembler에 강제 언어 적용 (다음 발화부터)"))
                {
                    var field = typeof(SpeechAssembler).GetField("forceLanguage",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        field.SetValue(assembler, forceLanguage);
                        Debug.Log($"<color=#FF8C00>[SpeechTestWindow]</color> {assembler.name}: forceLanguage = {forceLanguage}", assembler);
                    }
                }
            }

            // === 검문 상태 진단 ===
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("━━━ 진단 ━━━", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("SpeechDispatcher 검문 상태 출력", GUILayout.Height(24)))
                    dispatcher?.DispatcherDebugDiagnose();

                if (GUILayout.Button("NPCAlignmentController 평가 상태 출력", GUILayout.Height(24)))
                    alignment?.DebugDiagnoseEvaluation();
            }

            // === 시스템 정보 (참고) ===
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("━━━ 시스템 정보 ━━━", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "진영(NPCAlignment)과 Tone(SpeechTone)은 enum 정의로 하드코딩됨.\n" +
                "임계치/색상/대사키는 SO(NPCAlignmentSO, DialoguePresetSO)에서 데이터 기반.\n" +
                $"  진영: {string.Join(", ", System.Enum.GetNames(typeof(TDA.PB4.Data.NPCAlignment)))}\n" +
                $"  Tone: {string.Join(", ", System.Enum.GetNames(typeof(TDA.PB4.AI.Speech.SpeechTone)))}",
                MessageType.Info);

            // === 라이브 정보 ===
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("━━━ 라이브 ━━━", EditorStyles.boldLabel);
            if (Application.isPlaying && alignment != null)
            {
                EditorGUILayout.LabelField($"현재 진영", alignment.CurrentAlignment.ToString());
            }
            if (Application.isPlaying && assembler != null)
            {
                EditorGUILayout.LabelField($"마지막 Tone", assembler.LastTone.ToString());
            }

            // 자동 갱신
            if (Application.isPlaying)
                Repaint();

            EditorGUILayout.EndScrollView();
        }

        private void ResolveComponents()
        {
            if (targetNPC == null)
            {
                dispatcher = null;
                assembler = null;
                renderer = null;
                alignment = null;
                return;
            }
            dispatcher = targetNPC.GetComponent<SpeechDispatcher>();
            assembler = targetNPC.GetComponent<SpeechAssembler>();
            renderer = targetNPC.GetComponent<DialogueRenderer>();
            alignment = targetNPC.GetComponent<NPCAlignmentController>();
        }

        private void DrawComponentStatus(string name, bool present)
        {
            var rect = EditorGUILayout.GetControlRect();
            var color = present ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.9f, 0.4f, 0.4f);
            string mark = present ? "✅" : "❌";
            EditorGUI.LabelField(rect, $"{mark} {name}", new GUIStyle(EditorStyles.label) { normal = { textColor = color } });
        }
    }
}
#endif