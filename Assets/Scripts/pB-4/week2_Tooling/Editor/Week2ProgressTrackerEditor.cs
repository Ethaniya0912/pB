// =============================================================================
// Week2ProgressTrackerEditor.cs  |  pB-4 Week 2 — Day 2 T2.7 편의 도구
// 배치: Assets/Scripts/pB-4/week2_Tooling/Editor/Week2ProgressTrackerEditor.cs
//       (반드시 "Editor" 폴더 안에)
//
// 역할: Week2ProgressTracker Inspector 상단에 3개 버튼 + 진척 요약 패널 추가.
//       Play 중에 ContextMenu 찾아 들어가지 않고 버튼 한 번으로 디버그.
// =============================================================================
using UnityEditor;
using UnityEngine;

namespace TDA.PB4.Tooling.Editor
{
    [CustomEditor(typeof(Week2ProgressTracker))]
    public class Week2ProgressTrackerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var tracker = (Week2ProgressTracker)target;

            DrawProgressPanel(tracker);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space(4);

            base.OnInspectorGUI();
        }

        private void DrawProgressPanel(Week2ProgressTracker tracker)
        {
            EditorGUILayout.BeginVertical("box");

            // 제목
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.3f, 0.6f, 1.0f) }
            };
            EditorGUILayout.LabelField("📊 Week 2 Progress Tracker", headerStyle);

            // 체크리스트 SO 획득 (리플렉션 - SerializedObject는 런타임 runtimeStatus 읽기 번거로움)
            var serializedChecklist = serializedObject.FindProperty("checklist");
            var checklistObj = serializedChecklist != null ? serializedChecklist.objectReferenceValue as Week2ChecklistSO : null;

            if (checklistObj == null)
            {
                EditorGUILayout.HelpBox("Checklist SO가 할당되지 않았습니다.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            // 진척률 요약
            int passed = 0, failed = 0, notChecked = 0, externalDep = 0;
            foreach (var item in checklistObj.items)
            {
                switch (item.runtimeStatus)
                {
                    case CheckStatus.Passed: passed++; break;
                    case CheckStatus.Failed: failed++; break;
                    case CheckStatus.ExternalDep: externalDep++; break;
                    default: notChecked++; break;
                }
            }

            int relevant = checklistObj.RelevantTotal;
            float ratio = relevant > 0 ? (float)passed / relevant : 0f;

            // 색상 배지
            Color ratioColor;
            if (ratio >= 1.0f) ratioColor = new Color(0.2f, 0.8f, 0.3f);        // 녹색
            else if (ratio >= 0.7f) ratioColor = new Color(0.9f, 0.7f, 0.2f);   // 노랑
            else ratioColor = new Color(0.9f, 0.4f, 0.3f);                     // 빨강

            var ratioStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ratioColor }
            };

            EditorGUILayout.LabelField(
                $"{passed} / {relevant}   ({ratio:P0})",
                ratioStyle, GUILayout.Height(24));

            // 진행 막대
            Rect barRect = GUILayoutUtility.GetRect(0, 8, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(barRect, new Color(0.2f, 0.2f, 0.2f));
            float fillWidth = barRect.width * ratio;
            EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, fillWidth, barRect.height), ratioColor);

            EditorGUILayout.Space(4);

            // 상태별 개수
            var statStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10
            };

            EditorGUILayout.BeginHorizontal();
            DrawStatBadge("✓ 통과", passed, new Color(0.2f, 0.8f, 0.3f));
            DrawStatBadge("✗ 실패", failed, new Color(0.9f, 0.3f, 0.3f));
            DrawStatBadge("? 미평가", notChecked, new Color(0.7f, 0.7f, 0.7f));
            DrawStatBadge("⊖ 외부", externalDep, new Color(0.5f, 0.5f, 0.8f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // ═══ 버튼 3개 ═══════════════════════════
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.5f, 0.8f, 1.0f);
            if (GUILayout.Button("🔄 Re-Evaluate", GUILayout.Height(26)))
            {
                if (Application.isPlaying)
                    tracker.ReEvaluateNow();
                else
                    Debug.LogWarning("[Tracker] Re-Evaluate는 Play 모드에서만 가능");
            }

            GUI.backgroundColor = new Color(0.6f, 0.9f, 0.5f);
            if (GUILayout.Button("📋 Dump All", GUILayout.Height(26)))
                tracker.DumpAllItems();

            GUI.backgroundColor = new Color(1.0f, 0.7f, 0.3f);
            if (GUILayout.Button("⚠ Failed Only", GUILayout.Height(26)))
                tracker.DumpFailedOnly();

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // 힌트
            if (!Application.isPlaying)
            {
                var hintStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
                    wordWrap = true
                };
                EditorGUILayout.LabelField("▸ Play 모드에서 실시간 평가. Re-Evaluate는 Play 필수.", hintStyle);
            }
            else
            {
                var hintStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.5f, 0.7f, 0.5f) },
                    wordWrap = true
                };
                EditorGUILayout.LabelField("▸ Dump All: 전체 상태  |  Failed Only: 실패 항목만  |  Re-Evaluate: 즉시 재평가", hintStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawStatBadge(string label, int count, Color color)
        {
            var bgColor = new Color(color.r * 0.25f, color.g * 0.25f, color.b * 0.25f);
            var rect = EditorGUILayout.BeginVertical(GUILayout.Height(34));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, bgColor);

            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = color },
                fontSize = 9
            };
            var countStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = color },
                fontSize = 13
            };

            GUILayout.Label(label, labelStyle, GUILayout.Height(12));
            GUILayout.Label(count.ToString(), countStyle, GUILayout.Height(16));

            EditorGUILayout.EndVertical();
        }
    }
}
