// =============================================================================
// PB4DebugDashboard_v07.cs  |  pB-4 Project — Week 7
// v0.7: 발화 디버그 + RAG 검색 + 수첩 기록
// =============================================================================
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TDA.PB4.Core;
using TDA.PB4.AI;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Scenario;

namespace TDA.PB4.Editor
{
    public class PB4DebugDashboard_v07 : EditorWindow
    {
        private Vector2 scrollPos;
        private bool showBB=true, showSpeech=true, showRAG=true, showJournal=true, showIncident=true;

        [MenuItem("pB-4/Debug Dashboard v0.7")]
        public static void ShowWindow() => GetWindow<PB4DebugDashboard_v07>("pB-4 Dashboard v0.7");

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

            // ======= v0.7: Speech =======
            showSpeech = EditorGUILayout.Foldout(showSpeech, "\u25a0 [v0.7] \ubc1c\ud654 \uc2dc\uc2a4\ud15c", true);
            if (showSpeech)
            {
                EditorGUI.indentLevel++;
                foreach (var sa in FindObjectsByType<SpeechAssembler>(FindObjectsSortMode.None))
                {
                    EditorGUILayout.LabelField($"{sa.name} bucket={sa.CurrentBucketIndex}", EditorStyles.boldLabel);
                    var r = sa.LastResult;
                    if (r != null)
                    {
                        EditorGUILayout.LabelField($"  Core: [{r.coreLayer}]");
                        EditorGUILayout.LabelField($"  Texture: [{r.textureLayer}]");
                        EditorGUILayout.LabelField($"  Individual: [{r.individualLayer}]");
                        var style = new GUIStyle(EditorStyles.wordWrappedLabel) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.2f, 0.8f, 1f) } };
                        EditorGUILayout.LabelField($"  \u201c{r.fullSentence}\u201d", style);
                    }
                    else EditorGUILayout.LabelField("  (\ubc1c\ud654 \uc5c6\uc74c)");
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.7: RAG =======
            showRAG = EditorGUILayout.Foldout(showRAG, "\u25a0 [v0.7] RAG \uac80\uc0c9", true);
            if (showRAG)
            {
                EditorGUI.indentLevel++;
                var rag = FindFirstObjectByType<LightweightRAGEngine>();
                if (rag != null)
                {
                    EditorGUILayout.LabelField($"Search Time: {rag.LastSearchTimeMs:F2}ms");
                    foreach (var r in rag.LastResults)
                    {
                        Color c = r.resonanceType switch { "DejaVu"=>Color.red, "Resonance"=>Color.yellow, _=>Color.gray };
                        var style = new GUIStyle(EditorStyles.label) { normal = { textColor = c } };
                        EditorGUILayout.LabelField($"  [{r.resonanceType}] {r.incidentId}: {r.cosineSimilarity:F3}", style);
                    }
                    if (rag.LastResults.Count == 0) EditorGUILayout.LabelField("  (\uac80\uc0c9 \uacb0\uacfc \uc5c6\uc74c)");
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.7: Incident Records =======
            showIncident = EditorGUILayout.Foldout(showIncident, "\u25a0 [v0.7] \uc0ac\uac74 \uae30\ub85d", true);
            if (showIncident)
            {
                EditorGUI.indentLevel++;
                var recorder = FindFirstObjectByType<IncidentRecorder>();
                if (recorder != null)
                {
                    EditorGUILayout.LabelField($"Records: {recorder.Records.Count}");
                    int show = Mathf.Min(5, recorder.Records.Count);
                    for (int i = recorder.Records.Count - 1; i >= recorder.Records.Count - show && i >= 0; i--)
                    {
                        var r = recorder.Records[i];
                        EditorGUILayout.LabelField($"  [{r.incidentId}] int={r.intensityScore:F2} moral={r.moralAlignment:F1}");
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.7: Journal =======
            showJournal = EditorGUILayout.Foldout(showJournal, "\u25a0 [v0.7] \uc218\ucca9", true);
            if (showJournal)
            {
                EditorGUI.indentLevel++;
                var journal = FindFirstObjectByType<JournalAutoWriter>();
                if (journal != null)
                {
                    EditorGUILayout.LabelField($"Entries: {journal.Entries.Count}");
                    foreach (var e in journal.Entries)
                    {
                        var style = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = new Color(1f, 0.9f, 0.6f) } };
                        EditorGUILayout.LabelField($"  [{e.narratorName}] {e.content}", style);
                    }
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
            if (Application.isPlaying) Repaint();
        }
    }
}
#endif
