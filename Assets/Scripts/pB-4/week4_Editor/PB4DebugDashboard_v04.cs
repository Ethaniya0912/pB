// =============================================================================
// PB4DebugDashboard_v04.cs  |  pB-4 Project — Week 4
// v0.4 추가: 신뢰도/트라우마 패널, 퍼지태그 소속도, Bidding 결과
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
    public class PB4DebugDashboard_v04 : EditorWindow
    {
        private Vector2 scrollPos;
        private bool showBB=true, showMob=true, showTrust=true, showTrauma=true, showFuzzy=true, showBidding=true, showGroup=true;

        [MenuItem("pB-4/Debug Dashboard v0.4")]
        public static void ShowWindow() => GetWindow<PB4DebugDashboard_v04>("pB-4 Dashboard v0.4");

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

            // MobAI
            showMob = EditorGUILayout.Foldout(showMob, "\u25a0 MobAI", true);
            if (showMob)
            {
                EditorGUI.indentLevel++;
                foreach (var m in FindObjectsByType<MobAIBrain>(FindObjectsSortMode.None))
                    EditorGUILayout.LabelField($"{m.name} [{m.CurrentState}] fear={m.fear:F2}");
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.4: Trust =======
            showTrust = EditorGUILayout.Foldout(showTrust, "\u25a0 [v0.4] \uc2e0\ub8b0\ub3c4 (TrustMatrix)", true);
            if (showTrust)
            {
                EditorGUI.indentLevel++;
                foreach (var t in FindObjectsByType<TrustMatrix>(FindObjectsSortMode.None))
                {
                    EditorGUILayout.LabelField($"{t.name}: {t.CurrentTrust:F1} [{t.CurrentTier}]");
                    Rect r = EditorGUILayout.GetControlRect(false, 14);
                    Color c = t.CurrentTier switch {
                        TrustTier.BlindTrust => Color.cyan,
                        TrustTier.Cooperation => Color.green,
                        TrustTier.Doubt => Color.yellow,
                        TrustTier.Hostility => Color.red,
                        _ => Color.gray
                    };
                    EditorGUI.DrawRect(new Rect(r.x, r.y, r.width * (t.CurrentTrust / 100f), r.height), c);
                    EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, r.height), new Color(0,0,0,0.1f));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.4: Trauma =======
            showTrauma = EditorGUILayout.Foldout(showTrauma, "\u25a0 [v0.4] \ud2b8\ub77c\uc6b0\ub9c8 (TraumaSystem)", true);
            if (showTrauma)
            {
                EditorGUI.indentLevel++;
                foreach (var tr in FindObjectsByType<TraumaSystem>(FindObjectsSortMode.None))
                {
                    string traumaName = tr.ActiveTrauma != null ? tr.ActiveTrauma.traumaID : "(none)";
                    EditorGUILayout.LabelField($"{tr.name}: [{tr.CurrentStage}] trauma={traumaName} exp={tr.ExplorationsSinceTrauma}");
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.4: Fuzzy Tags =======
            showFuzzy = EditorGUILayout.Foldout(showFuzzy, "\u25a0 [v0.4] \ud37c\uc9c0 \ud0dc\uadf8 (FuzzyTagConverter)", true);
            if (showFuzzy)
            {
                EditorGUI.indentLevel++;
                var fuzzy = FindFirstObjectByType<FuzzyTagConverter>();
                if (fuzzy != null && fuzzy.ActiveTags != null)
                {
                    foreach (var tag in fuzzy.ActiveTags)
                    {
                        Color c = tag switch {
                            "NarrowPath" => new Color(1,0.3f,0.3f),
                            "DifficultEscape" => new Color(1,0.6f,0.2f),
                            "HighGround" => new Color(0.3f,0.8f,0.3f),
                            "SpookyCave" => new Color(0.6f,0.2f,0.8f),
                            "DeathTrap" => Color.red,
                            _ => Color.white
                        };
                        var style = new GUIStyle(EditorStyles.label) { normal = { textColor = c }, fontStyle = FontStyle.Bold };
                        EditorGUILayout.LabelField($"\u25cf {tag}", style);
                    }
                    if (fuzzy.ActiveTags.Count == 0)
                        EditorGUILayout.LabelField("(\ud0dc\uadf8 \uc5c6\uc74c)");
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.4: Bidding =======
            showBidding = EditorGUILayout.Foldout(showBidding, "\u25a0 [v0.4] Narrative Bidding", true);
            if (showBidding)
            {
                EditorGUI.indentLevel++;
                var bidder = FindFirstObjectByType<NarrativeBiddingEvaluator>();
                if (bidder != null && bidder.LastBids != null)
                {
                    foreach (var b in bidder.LastBids)
                    {
                        string star = b.subjectName == bidder.LastWinner ? "\u2605" : " ";
                        EditorGUILayout.LabelField($"{star} {b.subjectName}: {b.bidScore:F3} \u2192 {b.proposedTheme}");
                    }
                }
                EditorGUI.indentLevel--;
            }

            // GroupAI (v0.3에서 이관)
            showGroup = EditorGUILayout.Foldout(showGroup, "\u25a0 GroupAI", true);
            if (showGroup)
            {
                EditorGUI.indentLevel++;
                foreach (var g in FindObjectsByType<GroupAIManager>(FindObjectsSortMode.None))
                {
                    EditorGUILayout.LabelField($"{g.name}: Morale={g.CurrentMorale:F2} Esc={g.CurrentEscalationLevel} Token={g.ActiveTokenCount}/{g.MaxTokenCount}");
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
            if (Application.isPlaying) Repaint();
        }
    }
}
#endif
