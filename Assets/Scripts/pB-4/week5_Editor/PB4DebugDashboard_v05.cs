// =============================================================================
// PB4DebugDashboard_v05.cs  |  pB-4 Project — Week 5
// v0.5: Formation + SpawnProbability + DisasterSeed panels
// =============================================================================
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TDA.PB4.Core;
using TDA.PB4.AI;
using TDA.PB4.AI.Mob;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Terrain;
using TDA.PB4.Scenario;

namespace TDA.PB4.Editor
{
    public class PB4DebugDashboard_v05 : EditorWindow
    {
        private Vector2 scrollPos;
        private bool showBB=true, showTrust=true, showFormation=true, showSpawn=true, showDisaster=true, showGroup=true;

        [MenuItem("pB-4/Debug Dashboard v0.5")]
        public static void ShowWindow() => GetWindow<PB4DebugDashboard_v05>("pB-4 Dashboard v0.5");

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

            // Trust (v0.4)
            showTrust = EditorGUILayout.Foldout(showTrust, "\u25a0 Trust + Trauma", true);
            if (showTrust)
            {
                EditorGUI.indentLevel++;
                foreach (var t in FindObjectsByType<TrustMatrix>(FindObjectsSortMode.None))
                {
                    Color c = t.CurrentTier switch { TrustTier.BlindTrust=>Color.cyan, TrustTier.Cooperation=>Color.green, TrustTier.Doubt=>Color.yellow, _=>Color.red };
                    EditorGUILayout.LabelField($"{t.name}: {t.CurrentTrust:F1} [{t.CurrentTier}]");
                    Rect r = EditorGUILayout.GetControlRect(false,10);
                    EditorGUI.DrawRect(new Rect(r.x,r.y,r.width*(t.CurrentTrust/100f),r.height), c);
                }
                foreach (var tr in FindObjectsByType<TraumaSystem>(FindObjectsSortMode.None))
                    EditorGUILayout.LabelField($"  Trauma: [{tr.CurrentStage}] exp={tr.ExplorationsSinceTrauma}");
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.5: Formation =======
            showFormation = EditorGUILayout.Foldout(showFormation, "\u25a0 [v0.5] \uc804\ud22c \ub300\ud615", true);
            if (showFormation)
            {
                EditorGUI.indentLevel++;
                foreach (var f in FindObjectsByType<FormationSlotAllocator>(FindObjectsSortMode.None))
                {
                    EditorGUILayout.LabelField($"{f.name}", EditorStyles.boldLabel);
                    foreach (var s in f.Slots)
                    {
                        string npc = s.assignedNPC != null ? s.assignedNPC.name : "(empty)";
                        EditorGUILayout.LabelField($"  [{s.slotType}] {npc} (fit={s.fitnessScore:F1})");
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.5: Spawn Probability =======
            showSpawn = EditorGUILayout.Foldout(showSpawn, "\u25a0 [v0.5] Biome Spawn \ud655\ub960", true);
            if (showSpawn)
            {
                EditorGUI.indentLevel++;
                var resolver = FindFirstObjectByType<BiomeSpawnResolver>();
                if (resolver != null)
                {
                    foreach (var r in resolver.LastResults)
                    {
                        Rect bar = EditorGUILayout.GetControlRect(false, 14);
                        EditorGUI.DrawRect(new Rect(bar.x,bar.y,bar.width*r.finalProbability,bar.height), Color.Lerp(Color.gray, Color.green, r.finalProbability));
                        EditorGUI.LabelField(bar, $"  {r.factionId}: {r.finalProbability:P0} (aff={r.biomeAffinity:F2}\u00d7pop={r.populationLevel:F2})");
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.5: Disaster Seed =======
            showDisaster = EditorGUILayout.Foldout(showDisaster, "\u25a0 [v0.5] \uc7ac\uc559\uc758 \uc528\uc557", true);
            if (showDisaster)
            {
                EditorGUI.indentLevel++;
                var eval = FindFirstObjectByType<DisasterSeedEvaluator>();
                if (eval != null && eval.LastResult != null)
                {
                    var r = eval.LastResult;
                    Color c = r.isDisaster ? Color.red : Color.green;
                    var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = c } };
                    EditorGUILayout.LabelField($"{r.npcName}: {(r.isDisaster ? "\u2620\ufe0f DISASTER" : "\u2728 CATHARSIS")} ({r.finalProbability:F2})", style);
                    EditorGUILayout.LabelField($"  archetype={r.archetype}");
                    EditorGUILayout.LabelField($"  base={r.baseProbability:F2} stab={r.stabilityMod:F2} trust={r.trustMod:F2} heal={r.healMod:F2}");
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // GroupAI (v0.3)
            showGroup = EditorGUILayout.Foldout(showGroup, "\u25a0 GroupAI", true);
            if (showGroup)
            {
                EditorGUI.indentLevel++;
                foreach (var g in FindObjectsByType<GroupAIManager>(FindObjectsSortMode.None))
                    EditorGUILayout.LabelField($"{g.name}: Morale={g.CurrentMorale:F2} Esc={g.CurrentEscalationLevel} Token={g.ActiveTokenCount}/{g.MaxTokenCount}");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
            if (Application.isPlaying) Repaint();
        }
    }
}
#endif
