// =============================================================================
// PB4DebugDashboard_v08.cs  |  pB-4 Project — Week 8 (최종)
// v0.8: 스트레스 테스트 + SO 파이프라인 + 통합 매트릭스 + 벤치마크
// =============================================================================
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TDA.PB4.Core;
using TDA.PB4.Integration;
using TDA.PB4.Scenario;

namespace TDA.PB4.Editor
{
    public class PB4DebugDashboard_v08 : EditorWindow
    {
        private Vector2 scrollPos;
        private bool showStress=true, showMatrix=true, showBenchmark=true, showMasterDB=true, showBB=true;

        [MenuItem("pB-4/Debug Dashboard v0.8 (Final)")]
        public static void ShowWindow() => GetWindow<PB4DebugDashboard_v08>("pB-4 Final Dashboard");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("pB-4 \ud504\ub85c\ud1a0\ud0c0\uc785 \ucd5c\uc885 \ub300\uc2dc\ubcf4\ub4dc", EditorStyles.boldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // BB
            showBB = EditorGUILayout.Foldout(showBB, "\u25a0 GameBlackboard", true);
            if (showBB)
            {
                EditorGUI.indentLevel++;
                var bb = GameBlackboard.Instance;
                if (bb != null) EditorGUILayout.Slider("Intensity", bb.GlobalIntensityScore, 0f, 1f);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.8: Stress Test =======
            showStress = EditorGUILayout.Foldout(showStress, "\u25a0 [v0.8] \uc2a4\ud2b8\ub808\uc2a4 \ud14c\uc2a4\ud2b8", true);
            if (showStress)
            {
                EditorGUI.indentLevel++;
                var stress = FindFirstObjectByType<StressTestRunner>();
                if (stress != null)
                {
                    EditorGUILayout.LabelField($"Running: {stress.IsRunning} | Test #{stress.CurrentTestIndex}");
                    EditorGUILayout.LabelField($"Pass Rate: {stress.OverallPassRate:P0}", EditorStyles.boldLabel);
                    foreach (var r in stress.Results)
                    {
                        Color c = r.passed ? Color.green : Color.red;
                        var s = new GUIStyle(EditorStyles.label){normal={textColor=c}};
                        EditorGUILayout.LabelField($"  {(r.passed?"\u2705":"\u274c")} {r.testName}: {r.measuredValue:F1}{r.unit}", s);
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.8: Integration Matrix =======
            showMatrix = EditorGUILayout.Foldout(showMatrix, "\u25a0 [v0.8] 18\uac1c \uc5f0\ub3d9 \ub9e4\ud2b8\ub9ad\uc2a4", true);
            if (showMatrix)
            {
                EditorGUI.indentLevel++;
                var mat = FindFirstObjectByType<IntegrationScenarioMatrix>();
                if (mat != null)
                {
                    EditorGUILayout.LabelField($"{mat.PassCount}/{mat.TotalCount} \ud1b5\uacfc", EditorStyles.boldLabel);
                    foreach (var r in mat.Matrix)
                    {
                        Color c = r.passed ? Color.green : Color.red;
                        var s = new GUIStyle(EditorStyles.miniLabel){normal={textColor=c}};
                        EditorGUILayout.LabelField($"  {(r.passed?"\u2705":"\u274c")} {r.scenarioId}: {r.description}", s);
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.8: Benchmark =======
            showBenchmark = EditorGUILayout.Foldout(showBenchmark, "\u25a0 [v0.8] \ud30c\uc774\ud504\ub77c\uc778 \ubca4\uce58\ub9c8\ud06c", true);
            if (showBenchmark)
            {
                EditorGUI.indentLevel++;
                var bench = FindFirstObjectByType<PipelineBenchmark>();
                if (bench != null)
                {
                    foreach (var r in bench.Results)
                    {
                        Color c = r.passed ? Color.green : Color.red;
                        var s = new GUIStyle(EditorStyles.label){normal={textColor=c}};
                        EditorGUILayout.LabelField($"  {(r.passed?"\u2705":"\u274c")} {r.benchmarkName}: {r.measuredValue:F1}{r.unit}", s);
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.8: MasterDB =======
            showMasterDB = EditorGUILayout.Foldout(showMasterDB, "\u25a0 [v0.8] MasterDatabase", true);
            if (showMasterDB)
            {
                EditorGUI.indentLevel++;
                var db = FindFirstObjectByType<MasterDatabaseSO>();
                if (db != null)
                {
                    EditorGUILayout.LabelField($"Indexed: {db.IndexedArcCount} arcs | API calls: {db.ApiCallCount}");
                    EditorGUILayout.LabelField($"Last search: {db.LastSearchTimeMs:F2}ms");
                    foreach (var r in db.LastResults)
                        EditorGUILayout.LabelField($"  {r.arcName}: sim={r.similarity:F3}");
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
            if (Application.isPlaying) Repaint();
        }
    }
}
#endif
