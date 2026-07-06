// =============================================================================
// Week2ProgressWindow.cs  |  pB-4 Week 2 — Day 5 T5.1
// 역할: Window > pB-4 > Week 2 Progress 메뉴로 진척률을 시각화하는 EditorWindow.
//       Week2ProgressTracker SO + ChecklistSO 데이터를 실시간 표시 + .md 자동 출력.
//
// [기능]
//   1) 전체 진척률 게이지 (예: 26/26, 100%)
//   2) Day별 항목 그룹 (Day 1~4 + 외부 의존)
//   3) 항목별 상태 색상 표시 (Passed=초록 / Failed=빨강 / NotChecked=회색 / ExternalDep=파랑)
//   4) "Re-Evaluate" 버튼 → Tracker.EvaluateAll() 호출
//   5) "Export to Markdown" 버튼 → Reports/auto/Week2_Progress_*.md 출력
//   6) 실패 항목 클릭 시 verify 표현식 + 마지막 메시지 표시
//
// [Day 5 T5.1 산출물]
//   - 새 파일 (Editor 전용 — Editor 폴더에 배치)
// =============================================================================

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TDA.PB4.Tooling;

namespace TDA.PB4.Tooling.Editor
{
    public class Week2ProgressWindow : EditorWindow
    {
        // ────────────────────────────────────────────────────────────────
        // 메뉴 등록
        // ────────────────────────────────────────────────────────────────
        [MenuItem("Window/pB-4/Week 2 Progress", priority = 10)]
        public static void ShowWindow()
        {
            var win = GetWindow<Week2ProgressWindow>("Week 2 Progress");
            win.minSize = new Vector2(620, 480);
            win.Show();
        }

        // ────────────────────────────────────────────────────────────────
        // 상태
        // ────────────────────────────────────────────────────────────────
        private Vector2 _scroll;
        private Week2ChecklistSO _checklistCache;
        private Week2ProgressTracker _trackerCache;
        private string _filterText = "";
        private bool _showOnlyFailed = false;
        private bool _showOnlyNotChecked = false;
        private double _lastRefreshTime;

        // ────────────────────────────────────────────────────────────────
        // 색상 (Edit 모드 / Play 모드 모두 작동)
        // ────────────────────────────────────────────────────────────────
        private static readonly Color C_PASS = new Color(0.55f, 0.85f, 0.45f, 1f);
        private static readonly Color C_FAIL = new Color(0.95f, 0.40f, 0.40f, 1f);
        private static readonly Color C_NOT_CHECKED = new Color(0.65f, 0.65f, 0.65f, 1f);
        private static readonly Color C_EXTERNAL = new Color(0.40f, 0.65f, 0.95f, 1f);
        private static readonly Color C_BG_HEADER = new Color(0.20f, 0.20f, 0.20f, 0.5f);
        private static readonly Color C_BG_DAY = new Color(0.15f, 0.15f, 0.15f, 0.3f);

        // ────────────────────────────────────────────────────────────────
        // 라이프사이클
        // ────────────────────────────────────────────────────────────────
        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            RefreshCaches();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            // 자동 재평가: Play 모드에서만 1초마다
            if (!Application.isPlaying) return;
            if (_trackerCache == null) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRefreshTime < 1.0) return;
            _lastRefreshTime = now;
            Repaint();
        }

        private void RefreshCaches()
        {
            // 씬에서 Tracker 찾기
            _trackerCache = FindAnyObjectByType<Week2ProgressTracker>();

            // Tracker가 있으면 SO 가져오기
            if (_trackerCache != null)
            {
                var so = new SerializedObject(_trackerCache);
                var prop = so.FindProperty("checklist");
                if (prop != null && prop.objectReferenceValue is Week2ChecklistSO ck)
                {
                    _checklistCache = ck;
                    return;
                }
            }

            // Tracker 없으면 SO 직접 검색
            string[] guids = AssetDatabase.FindAssets("t:Week2ChecklistSO");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _checklistCache = AssetDatabase.LoadAssetAtPath<Week2ChecklistSO>(path);
            }
        }

        // ────────────────────────────────────────────────────────────────
        // GUI
        // ────────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            EditorGUILayout.Space(6);

            DrawHeader();
            EditorGUILayout.Space(8);

            if (_checklistCache == null)
            {
                EditorGUILayout.HelpBox(
                    "Week2ChecklistSO 자산이 없습니다.\n" +
                    "Project 창에서 'Week2Checklist.asset'을 만들거나, 씬에 Week2ProgressTracker를 배치하고 Inspector에서 SO를 할당하세요.",
                    MessageType.Warning);
                if (GUILayout.Button("Refresh", GUILayout.Width(120)))
                    RefreshCaches();
                return;
            }

            DrawProgressBar();
            EditorGUILayout.Space(6);

            DrawToolbar();
            EditorGUILayout.Space(4);

            DrawFilterBar();
            EditorGUILayout.Space(4);

            DrawItemList();

            EditorGUILayout.Space(8);
            DrawFooter();
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("pB-4 Week 2 Progress", EditorStyles.largeLabel);
            GUILayout.FlexibleSpace();

            string statusText = _trackerCache != null
                ? (Application.isPlaying ? "Play (실시간)" : "Edit (정적)")
                : "Tracker 없음";
            GUILayout.Label(statusText, EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }

        private void DrawProgressBar()
        {
            int passed = _checklistCache.PassedCount;
            int total = _checklistCache.RelevantTotal;
            float ratio = _checklistCache.ProgressRatio;

            // 큰 텍스트
            var bigStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
            var subStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };

            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label($"{passed} / {total}", bigStyle, GUILayout.Height(36));
            GUILayout.Label($"{ratio:P0} 통과", subStyle);

            // 게이지 바
            Rect r = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
            r.x += 8; r.width -= 16;
            EditorGUI.DrawRect(r, new Color(0.15f, 0.15f, 0.15f, 0.5f));
            Rect filled = new Rect(r.x, r.y, r.width * ratio, r.height);
            EditorGUI.DrawRect(filled, ratio >= 1.0f ? C_PASS : new Color(0.35f, 0.65f, 0.95f));
            GUILayout.EndVertical();
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(_trackerCache == null && !Application.isPlaying))
            {
                if (GUILayout.Button("Re-Evaluate", GUILayout.Height(24), GUILayout.Width(120)))
                {
                    if (_trackerCache != null)
                        _trackerCache.ReEvaluateNow();
                    Repaint();
                }
            }

            using (new EditorGUI.DisabledScope(_trackerCache == null))
            {
                if (GUILayout.Button("Export to Markdown", GUILayout.Height(24), GUILayout.Width(160)))
                {
                    _trackerCache.ExportToMarkdown();
                }
            }

            if (GUILayout.Button("Refresh", GUILayout.Height(24), GUILayout.Width(80)))
            {
                RefreshCaches();
                Repaint();
            }

            GUILayout.FlexibleSpace();

            if (_trackerCache == null)
                GUILayout.Label("(Tracker 미배치 — Re-Evaluate 사용 불가)", EditorStyles.miniLabel);

            GUILayout.EndHorizontal();
        }

        private void DrawFilterBar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("필터:", EditorStyles.miniLabel, GUILayout.Width(40));
            _filterText = GUILayout.TextField(_filterText ?? "", EditorStyles.toolbarTextField, GUILayout.MinWidth(100));

            GUILayout.Space(8);
            _showOnlyFailed = GUILayout.Toggle(_showOnlyFailed, "실패만", EditorStyles.toolbarButton);
            _showOnlyNotChecked = GUILayout.Toggle(_showOnlyNotChecked, "미평가만", EditorStyles.toolbarButton);

            GUILayout.FlexibleSpace();

            int total = _checklistCache.items.Count;
            int passed = _checklistCache.PassedCount;
            GUILayout.Label($"총 {total}개 ({passed} PASS)", EditorStyles.miniLabel);

            GUILayout.EndHorizontal();
        }

        private void DrawItemList()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // Day별 그룹
            var byDay = new SortedDictionary<string, List<ChecklistItem>>();
            foreach (var item in _checklistCache.items)
            {
                string day = string.IsNullOrEmpty(item.assignedDay) ? "기타" : item.assignedDay;
                if (!byDay.ContainsKey(day)) byDay[day] = new List<ChecklistItem>();
                byDay[day].Add(item);
            }

            foreach (var kv in byDay)
            {
                var dayItems = kv.Value;
                dayItems.Sort((a, b) => string.Compare(a.id, b.id, System.StringComparison.Ordinal));

                int passInDay = 0;
                foreach (var i in dayItems)
                    if (i.runtimeStatus == CheckStatus.Passed) passInDay++;

                // Day 헤더
                Rect dayHeaderRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(dayHeaderRect, C_BG_DAY);
                Rect dayLabelRect = new Rect(dayHeaderRect.x + 8, dayHeaderRect.y + 2, dayHeaderRect.width - 16, dayHeaderRect.height - 4);
                EditorGUI.LabelField(dayLabelRect,
                    $"Day {kv.Key}    ({passInDay} / {dayItems.Count} 통과)",
                    EditorStyles.boldLabel);

                foreach (var item in dayItems)
                {
                    if (!ShouldDisplay(item)) continue;
                    DrawItemRow(item);
                }

                EditorGUILayout.Space(4);
            }

            EditorGUILayout.EndScrollView();
        }

        private bool ShouldDisplay(ChecklistItem item)
        {
            if (_showOnlyFailed && item.runtimeStatus != CheckStatus.Failed) return false;
            if (_showOnlyNotChecked && item.runtimeStatus != CheckStatus.NotChecked) return false;
            if (!string.IsNullOrEmpty(_filterText))
            {
                string f = _filterText.ToLowerInvariant();
                if (!item.id.ToLowerInvariant().Contains(f) &&
                    !item.title.ToLowerInvariant().Contains(f))
                    return false;
            }
            return true;
        }

        private void DrawItemRow(ChecklistItem item)
        {
            Rect row = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));

            // 좌측 상태 색상 띠
            Rect statusBar = new Rect(row.x, row.y, 4, row.height);
            Color barColor = item.runtimeStatus switch
            {
                CheckStatus.Passed => C_PASS,
                CheckStatus.Failed => C_FAIL,
                CheckStatus.NotChecked => C_NOT_CHECKED,
                CheckStatus.ExternalDep => C_EXTERNAL,
                _ => C_NOT_CHECKED
            };
            EditorGUI.DrawRect(statusBar, barColor);

            // ID + Title
            Rect idRect = new Rect(row.x + 12, row.y + 2, 80, 16);
            Rect titleRect = new Rect(row.x + 100, row.y + 2, row.width - 110, 16);
            Rect detailRect = new Rect(row.x + 100, row.y + 18, row.width - 110, 14);

            var idStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            var titleStyle = new GUIStyle(EditorStyles.label) { fontSize = 12 };
            var detailStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false };

            EditorGUI.LabelField(idRect, item.id, idStyle);
            EditorGUI.LabelField(titleRect, item.title, titleStyle);

            string detailText = item.runtimeStatus switch
            {
                CheckStatus.Failed => $"실패: {Trim(item.lastLogMessage, 80)}",
                CheckStatus.Passed => $"통과: {Trim(item.lastLogMessage, 80)}",
                CheckStatus.NotChecked => $"미평가 — {item.verifyMethod}",
                CheckStatus.ExternalDep => "외부 의존성 (진척률 계산 제외)",
                _ => item.verifyMethod
            };
            EditorGUI.LabelField(detailRect, detailText, detailStyle);

            // 분리선
            Rect divider = new Rect(row.x, row.y + row.height - 1, row.width, 1);
            EditorGUI.DrawRect(divider, new Color(0, 0, 0, 0.15f));
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            if (s.Length <= max) return s;
            return s.Substring(0, max - 3) + "...";
        }

        private void DrawFooter()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Tip: Play 모드에서 매 1초 자동 갱신. 'Export to Markdown'으로 Reports/ 폴더에 .md 출력.",
                EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }
    }
}
#endif
