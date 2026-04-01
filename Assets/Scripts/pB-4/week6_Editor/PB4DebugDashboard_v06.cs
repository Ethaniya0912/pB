// =============================================================================
// PB4DebugDashboard_v06.cs  |  pB-4 Project — Week 6
// v0.6: 5단계 체인 모니터 + FPS + 정크션 + 통합 검증
// =============================================================================
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TDA.PB4.Core;
using TDA.PB4.AI;
using TDA.PB4.AI.Mob;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Terrain;
using TDA.PB4.Integration;

namespace TDA.PB4.Editor
{
    public class PB4DebugDashboard_v06 : EditorWindow
    {
        private Vector2 scrollPos;
        private bool showChain=true, showFPS=true, showJunction=true, showValidator=true, showBB=true, showGroup=true;

        [MenuItem("pB-4/Debug Dashboard v0.6")]
        public static void ShowWindow() => GetWindow<PB4DebugDashboard_v06>("pB-4 Dashboard v0.6");

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // BB
            showBB = EditorGUILayout.Foldout(showBB, "\u25a0 GameBlackboard", true);
            if (showBB)
            {
                EditorGUI.indentLevel++;
                var bb = GameBlackboard.Instance;
                if (bb != null)
                {
                    EditorGUILayout.LabelField("Tags:", bb.ActiveTerrainTags != null ? string.Join(", ", bb.ActiveTerrainTags) : "-");
                    EditorGUILayout.Slider("Intensity", bb.GlobalIntensityScore, 0f, 1f);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.6: 5-Stage Chain =======
            showChain = EditorGUILayout.Foldout(showChain, "\u25a0 [v0.6] 5\ub2e8\uacc4 \uccb4\uc778", true);
            if (showChain)
            {
                EditorGUI.indentLevel++;
                var chain = FindFirstObjectByType<FiveStageChainOrchestrator>();
                if (chain != null)
                {
                    EditorGUILayout.LabelField($"Runs: {chain.TotalPasses}/{chain.TotalRuns} | Running: {chain.IsRunning}");
                    foreach (var r in chain.LastResults)
                    {
                        Color c = r.passed ? Color.green : Color.red;
                        var style = new GUIStyle(EditorStyles.label) { normal = { textColor = c } };
                        EditorGUILayout.LabelField($"  {(r.passed ? "\u2705" : "\u274c")} {r.stageName}: {r.message}", style);
                    }
                    if (chain.LastResults.Count == 0) EditorGUILayout.LabelField("  (\ubbf8\uc2e4\ud589)");
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.6: FPS =======
            showFPS = EditorGUILayout.Foldout(showFPS, "\u25a0 [v0.6] FPS \ubaa8\ub2c8\ud130", true);
            if (showFPS)
            {
                EditorGUI.indentLevel++;
                var combat = FindFirstObjectByType<MixedCombatTestHarness>();
                if (combat != null)
                {
                    Color fpsColor = combat.CurrentFPS >= 60 ? Color.green : Color.red;
                    var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = fpsColor } };
                    EditorGUILayout.LabelField($"FPS: {combat.CurrentFPS:F1} (min={combat.MinFPS:F1}, avg={combat.AvgFPS:F1})", style);
                    EditorGUILayout.LabelField($"  Mob={combat.transform.GetComponent<MixedCombatTestHarness>()?.name}");
                    EditorGUILayout.LabelField($"  Milestone (60fps): {(combat.FPSMilestonePass ? "\u2705 PASS" : "\u274c FAIL")}");
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.6: Junction =======
            showJunction = EditorGUILayout.Foldout(showJunction, "\u25a0 [v0.6] \uc815\ud06c\uc158 \ub77c\uc774\ud504\uc0ac\uc774\ud074", true);
            if (showJunction)
            {
                EditorGUI.indentLevel++;
                foreach (var j in FindObjectsByType<JunctionLifecycleManager>(FindObjectsSortMode.None))
                {
                    Color c = j.CurrentStage switch {
                        JunctionStage.Dormant => Color.gray,
                        JunctionStage.Reservation => Color.yellow,
                        JunctionStage.Spawning => new Color(1,0.5f,0),
                        JunctionStage.Fixation => Color.cyan,
                        _ => Color.white
                    };
                    var style = new GUIStyle(EditorStyles.label) { normal = { textColor = c }, fontStyle = FontStyle.Bold };
                    EditorGUILayout.LabelField($"{j.name}: [{j.CurrentStage}] dist={j.CurrentDistance:F1}m", style);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.6: Validator =======
            showValidator = EditorGUILayout.Foldout(showValidator, "\u25a0 [v0.6] \ud1b5\ud569 \uac80\uc99d", true);
            if (showValidator)
            {
                EditorGUI.indentLevel++;
                var validator = FindFirstObjectByType<IntegrationValidator>();
                if (validator != null)
                {
                    EditorGUILayout.LabelField($"Pass Rate: {validator.PassRate:P0}");
                    foreach (var r in validator.Results)
                    {
                        Color c = r.passed ? Color.green : Color.red;
                        var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = c } };
                        EditorGUILayout.LabelField($"  {(r.passed ? "\u2705" : "\u274c")} {r.checkName}", style);
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // GroupAI
            showGroup = EditorGUILayout.Foldout(showGroup, "\u25a0 GroupAI", true);
            if (showGroup)
            {
                EditorGUI.indentLevel++;
                foreach (var g in FindObjectsByType<GroupAIManager>(FindObjectsSortMode.None))
                    EditorGUILayout.LabelField($"{g.name}: Morale={g.CurrentMorale:F2} Esc={g.CurrentEscalationLevel}");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
            if (Application.isPlaying) Repaint();
        }
    }
}
#endif
