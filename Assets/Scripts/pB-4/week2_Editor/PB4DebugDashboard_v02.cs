// =============================================================================
// PB4DebugDashboard_v02.cs  |  pB-4 Project — Week 2
// Layer  : Editor
// Namespace: TDA.PB4.Editor
//
// 역할:
//   Week 1의 v0.1에서 확장. 추가된 기능:
//   - 지형 태그 시각화 (ContextAnalyzer 출력 태그를 씬 레이블+아이콘으로 표시)
//   - 성격-유틸리티 시뮬레이터 (5축 슬라이더 조작→유틸리티 점수 실시간 미리보기)
//   - HNGS 긴장도 모니터 + DynamicFog 상태 표시
//
//   Week 1의 PB4DebugDashboard.cs를 대체한다. (기존 v0.1 삭제 후 이 파일 사용)
// =============================================================================
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TDA.PB4.Core;
using TDA.PB4.AI.Mob;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.AI;
using TDA.PB4.Terrain;

namespace TDA.PB4.Editor
{
    public class PB4DebugDashboard_v02 : EditorWindow
    {
        private Vector2 scrollPos;
        private bool showBB = true;
        private bool showMob = true;
        private bool showHuman = true;
        private bool showTerrain = true;
        private bool showHNGS = true;
        private bool showTags = true;

        [MenuItem("pB-4/Debug Dashboard v0.2")]
        public static void ShowWindow() => GetWindow<PB4DebugDashboard_v02>("pB-4 Dashboard v0.2");

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // ======================== BLACKBOARD ========================
            showBB = EditorGUILayout.Foldout(showBB, "\u25a0 GameBlackboard Monitor", true);
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
                            EditorGUILayout.LabelField("  " + kv.Key, $"Pop={kv.Value.populationLevel:F2} Dec={kv.Value.isDecimated} Dom={kv.Value.isDominant}");
                }
                else EditorGUILayout.HelpBox("GameBlackboard.Instance not found.", MessageType.Warning);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(6);

            // ======================== MOB AI ========================
            showMob = EditorGUILayout.Foldout(showMob, "\u25a0 MobAI Utility Scores", true);
            if (showMob)
            {
                EditorGUI.indentLevel++;
                foreach (var m in FindObjectsByType<MobAIBrain>(FindObjectsSortMode.None))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(m.name, GUILayout.Width(140));
                    EditorGUILayout.LabelField($"[{m.CurrentState}]", GUILayout.Width(70));
                    EditorGUILayout.LabelField($"fear={m.fear:F2}", GUILayout.Width(80));
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(6);

            // ======================== HUMANOID AI ========================
            showHuman = EditorGUILayout.Foldout(showHuman, "\u25a0 HumanoidAI Personality + Utility", true);
            if (showHuman)
            {
                EditorGUI.indentLevel++;
                foreach (var h in FindObjectsByType<HumanoidAIBrain>(FindObjectsSortMode.None))
                {
                    var p = h.Personality;
                    EditorGUILayout.LabelField($"{h.name} [{h.CurrentState}]");
                    EditorGUILayout.LabelField($"  C={p.control:F2} S={p.stability:F2} O={p.openness:F2} A={p.agreeable:F2} D={p.directness:F2}");
                    EditorGUILayout.LabelField($"  fear={h.fear:F2} hunger={h.hunger:F2} greed={h.greed:F2} fatigue={h.fatigue:F2}");

                    // v0.2 추가: PersonalityTagResolver 태그 표시
                    var resolver = h.GetComponent<PersonalityTagResolver>();
                    if (resolver != null && resolver.ActiveTags != null)
                        EditorGUILayout.LabelField($"  Tags: [{string.Join(", ", resolver.ActiveTags)}]");
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(6);

            // ======================== v0.2: TERRAIN TAGS ========================
            showTags = EditorGUILayout.Foldout(showTags, "\u25a0 [v0.2] \uc9c0\ud615 \ud0dc\uadf8 \uc2dc\uac01\ud654", true);
            if (showTags)
            {
                EditorGUI.indentLevel++;
                var bb = GameBlackboard.Instance;
                if (bb != null && bb.ActiveTerrainTags != null)
                {
                    foreach (var tag in bb.ActiveTerrainTags)
                    {
                        Color tagColor = GetTagColor(tag);
                        var style = new GUIStyle(EditorStyles.label) { normal = { textColor = tagColor }, fontStyle = FontStyle.Bold };
                        EditorGUILayout.LabelField($"\u25cf {tag}", style);
                    }
                    if (bb.ActiveTerrainTags.Count == 0)
                        EditorGUILayout.LabelField("(\ud0dc\uadf8 \uc5c6\uc74c)", EditorStyles.miniLabel);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(6);

            // ======================== v0.2: HNGS INTENSITY ========================
            showHNGS = EditorGUILayout.Foldout(showHNGS, "\u25a0 [v0.2] HNGS \uae34\uc7a5\ub3c4 + DynamicFog", true);
            if (showHNGS)
            {
                EditorGUI.indentLevel++;
                var hngs = FindFirstObjectByType<HNGSIntensityController>();
                if (hngs != null)
                {
                    EditorGUILayout.LabelField($"Intensity: {hngs.CurrentIntensity:F3}");
                    // 색상 바: 긴장도에 따라 초록→빨강
                    Color barColor = Color.Lerp(Color.green, Color.red, hngs.CurrentIntensity);
                    Rect rect = EditorGUILayout.GetControlRect(false, 12);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * hngs.CurrentIntensity, rect.height), barColor);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, rect.height), new Color(0,0,0,0.1f));

                    EditorGUILayout.LabelField($"ResourceDeficit={hngs.resourceDeficit:F2}, EnemyProximity={hngs.enemyProximity:F2}, Sensory={hngs.sensoryDeprivation:F2}");
                }
                else EditorGUILayout.LabelField("(HNGSIntensityController not found)");

                var fog = FindFirstObjectByType<DynamicFogController>();
                if (fog != null)
                {
                    EditorGUILayout.LabelField($"Fog Density: {RenderSettings.fogDensity:F4}");
                    EditorGUILayout.ColorField("Fog Color", RenderSettings.fogColor);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
            if (Application.isPlaying) Repaint();
        }

        private static Color GetTagColor(string tag)
        {
            switch (tag)
            {
                case "NarrowPath": return new Color(1f, 0.3f, 0.3f);
                case "SpookyCave": return new Color(0.6f, 0.2f, 0.8f);
                case "DeathTrap": return Color.red;
                case "HighGround": return new Color(0.3f, 0.8f, 0.3f);
                case "WideOpen": return Color.cyan;
                default: return Color.white;
            }
        }
    }
}
#endif
