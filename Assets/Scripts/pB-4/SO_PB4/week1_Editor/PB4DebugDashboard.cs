#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TDA.PB4.Core;
using TDA.PB4.AI.Mob;
using TDA.PB4.AI.Humanoid;

namespace TDA.PB4.Editor
{
    public class PB4DebugDashboard : EditorWindow
    {
        private Vector2 scrollPos;
        private bool showBB = true, showMob = true, showHuman = true;

        [MenuItem("pB-4/Debug Dashboard v0.1")]
        public static void ShowWindow() => GetWindow<PB4DebugDashboard>("pB-4 Dashboard");

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            showBB = EditorGUILayout.Foldout(showBB, "GameBlackboard Monitor", true);
            if (showBB)
            {
                EditorGUI.indentLevel++;
                var bb = GameBlackboard.Instance;
                if (bb != null)
                {
                    EditorGUILayout.LabelField("TerrainTags:", bb.ActiveTerrainTags != null ? string.Join(", ", bb.ActiveTerrainTags) : "(null)");
                    EditorGUILayout.Slider("Intensity", bb.GlobalIntensityScore, 0f, 1f);
                    EditorGUILayout.LabelField("CharStats:", bb.CurrentCharacterStats?.Count + " entries");
                    if (bb.ActiveFactionRegistry != null)
                        foreach (var kv in bb.ActiveFactionRegistry)
                            EditorGUILayout.LabelField($"  {kv.Key}", $"Pop={kv.Value.populationLevel:F2} Dec={kv.Value.isDecimated} Dom={kv.Value.isDominant}");
                }
                else EditorGUILayout.HelpBox("GameBlackboard.Instance not found.", MessageType.Warning);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(8);

            showMob = EditorGUILayout.Foldout(showMob, "MobAI Utility Scores", true);
            if (showMob)
            {
                EditorGUI.indentLevel++;
                foreach (var m in FindObjectsByType<MobAIBrain>(FindObjectsSortMode.None))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(m.name, GUILayout.Width(120));
                    EditorGUILayout.LabelField($"[{m.CurrentState}]", GUILayout.Width(80));
                    EditorGUILayout.LabelField($"fear={m.fear:F2}", GUILayout.Width(80));
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(8);

            showHuman = EditorGUILayout.Foldout(showHuman, "HumanoidAI Personality + Utility", true);
            if (showHuman)
            {
                EditorGUI.indentLevel++;
                foreach (var h in FindObjectsByType<HumanoidAIBrain>(FindObjectsSortMode.None))
                {
                    var p = h.Personality;
                    EditorGUILayout.LabelField($"{h.name} [{h.CurrentState}]");
                    EditorGUILayout.LabelField($"  C={p.control:F2} S={p.stability:F2} O={p.openness:F2} A={p.agreeable:F2} D={p.directness:F2}");
                    EditorGUILayout.LabelField($"  fear={h.fear:F2} hunger={h.hunger:F2} greed={h.greed:F2} fatigue={h.fatigue:F2}");
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
            if (Application.isPlaying) Repaint();
        }
    }
}
#endif
