// =============================================================================
// PB4DebugDashboard_v03.cs  |  pB-4 Project — Week 3
// Layer  : Editor
// Namespace: TDA.PB4.Editor
//
// v0.3 추가 기능:
//   - GroupAI 사기/토큰 상태 모니터
//   - MetaScenario 팩션 상태 패널
//   - ArcProgression 현재 Phase 표시
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
    public class PB4DebugDashboard_v03 : EditorWindow
    {
        private Vector2 scrollPos;
        private bool showBB=true, showMob=true, showHuman=true, showGroup=true, showHNGS=true, showArc=true, showMeta=true;

        [MenuItem("pB-4/Debug Dashboard v0.3")]
        public static void ShowWindow() => GetWindow<PB4DebugDashboard_v03>("pB-4 Dashboard v0.3");

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // BB Monitor (v0.1부터)
            showBB = EditorGUILayout.Foldout(showBB, "\u25a0 GameBlackboard", true);
            if (showBB)
            {
                EditorGUI.indentLevel++;
                var bb = GameBlackboard.Instance;
                if (bb != null)
                {
                    EditorGUILayout.LabelField("Tags:", bb.ActiveTerrainTags != null ? string.Join(", ", bb.ActiveTerrainTags) : "-");
                    EditorGUILayout.Slider("Intensity", bb.GlobalIntensityScore, 0f, 1f);
                    if (bb.ActiveFactionRegistry != null)
                        foreach (var kv in bb.ActiveFactionRegistry)
                            EditorGUILayout.LabelField($"  {kv.Key}", $"Pop={kv.Value.populationLevel:F2}");
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // MobAI (v0.1부터)
            showMob = EditorGUILayout.Foldout(showMob, "\u25a0 MobAI", true);
            if (showMob)
            {
                EditorGUI.indentLevel++;
                foreach (var m in FindObjectsByType<MobAIBrain>(FindObjectsSortMode.None))
                    EditorGUILayout.LabelField($"{m.name} [{m.CurrentState}] fear={m.fear:F2}");
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // HumanoidAI (v0.1부터)
            showHuman = EditorGUILayout.Foldout(showHuman, "\u25a0 HumanoidAI", true);
            if (showHuman)
            {
                EditorGUI.indentLevel++;
                foreach (var h in FindObjectsByType<HumanoidAIBrain>(FindObjectsSortMode.None))
                {
                    var p = h.Personality;
                    EditorGUILayout.LabelField($"{h.name} [{h.CurrentState}] C={p.control:F1} S={p.stability:F1}");
                    var resolver = h.GetComponent<PersonalityTagResolver>();
                    if (resolver != null) EditorGUILayout.LabelField($"  Tags: [{string.Join(",", resolver.ActiveTags)}]");
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.3: GroupAI =======
            showGroup = EditorGUILayout.Foldout(showGroup, "\u25a0 [v0.3] GroupAI \uc0ac\uae30/\ud1a0\ud070", true);
            if (showGroup)
            {
                EditorGUI.indentLevel++;
                foreach (var g in FindObjectsByType<GroupAIManager>(FindObjectsSortMode.None))
                {
                    EditorGUILayout.LabelField($"{g.name}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"  Morale={g.CurrentMorale:F2} Esc={g.CurrentEscalationLevel} Token={g.ActiveTokenCount}/{g.MaxTokenCount}");

                    // 사기 색상 바
                    Rect r = EditorGUILayout.GetControlRect(false, 10);
                    EditorGUI.DrawRect(new Rect(r.x, r.y, r.width * g.CurrentMorale, r.height),
                        Color.Lerp(Color.red, Color.green, g.CurrentMorale));

                    // 멤버 역할 분포
                    int s=0,d=0,k=0;
                    foreach (var m in g.Members)
                    {
                        if (m.currentRole == GroupRole.Stalker) s++;
                        else if (m.currentRole == GroupRole.Disruptor) d++;
                        else k++;
                    }
                    EditorGUILayout.LabelField($"  Stalker={s} Disruptor={d} Striker={k}");
                }
                if (FindObjectsByType<GroupAIManager>(FindObjectsSortMode.None).Length == 0)
                    EditorGUILayout.LabelField("(GroupAIManager \uc5c6\uc74c)");
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.3: HNGS (v0.2에서 이관) =======
            showHNGS = EditorGUILayout.Foldout(showHNGS, "\u25a0 HNGS \uae34\uc7a5\ub3c4", true);
            if (showHNGS)
            {
                EditorGUI.indentLevel++;
                var hngs = FindFirstObjectByType<HNGSIntensityController>();
                if (hngs != null)
                {
                    Rect r = EditorGUILayout.GetControlRect(false, 12);
                    EditorGUI.DrawRect(new Rect(r.x, r.y, r.width * hngs.CurrentIntensity, r.height),
                        Color.Lerp(Color.green, Color.red, hngs.CurrentIntensity));
                    EditorGUILayout.LabelField($"Intensity={hngs.CurrentIntensity:F3} Fog={RenderSettings.fogDensity:F4}");
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.3: Arc Progression =======
            showArc = EditorGUILayout.Foldout(showArc, "\u25a0 [v0.3] Arc Progression", true);
            if (showArc)
            {
                EditorGUI.indentLevel++;
                var arc = FindFirstObjectByType<ArcProgressionManager>();
                if (arc != null)
                {
                    EditorGUILayout.LabelField($"Arc: {(arc.CurrentArc != null ? arc.CurrentArc.arcName : "(none)")}");
                    EditorGUILayout.LabelField($"Phase: {arc.CurrentPhase} ({arc.ElapsedTime:F1}s)");
                    EditorGUILayout.LabelField($"Active: {arc.IsArcActive}");
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // ======= v0.3: MetaScenario =======
            showMeta = EditorGUILayout.Foldout(showMeta, "\u25a0 [v0.3] MetaScenario", true);
            if (showMeta)
            {
                EditorGUI.indentLevel++;
                var meta = FindFirstObjectByType<MetaScenarioGenerator>();
                if (meta != null)
                {
                    foreach (var s in meta.FactionSummary)
                        EditorGUILayout.LabelField($"  {s}");
                    EditorGUILayout.LabelField($"  Nodes: {meta.NodeParams.Count}");
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
            if (Application.isPlaying) Repaint();
        }
    }
}
#endif
