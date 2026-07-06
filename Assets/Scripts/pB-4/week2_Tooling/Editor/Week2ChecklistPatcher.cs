// =============================================================================
// Week2ChecklistPatcher.cs  |  pB-4 Week 2 Day 5 — Tracker 통합 보강
// 역할: T5.3 Mob 회귀 산출물을 Tracker가 검증할 수 있도록 체크리스트에 항목 추가.
//       이전 누락: T5.3 도구는 .md를 생성하지만, 그 .md의 존재/PASS를 Tracker가
//       모르는 상태였음. 본 스크립트가 누락된 고리를 보강.
//
// 사용:
//   메뉴 → pB-4 → Day 5 → Patch Checklist (T5.3 entries)
//   1회 실행하면 Week2Checklist.asset에 WK2_C35 (산출물 존재) +
//   WK2_C36 (PASS 마커 포함)이 추가됨. 멱등 — 이미 있으면 스킵.
//
// 추가 항목:
//   WK2_C35  FileExists:../Reports/Week2_Day5_T5_3_MobRegression.md   Code/Day5/auto
//   WK2_C36  FileContains:../Reports/...:종합 판정**: ✅ PASS         Runtime/Day5/auto
//
// 반영 후:
//   평가 가능 항목  29 → 31 (C32, C34는 그대로 ignoreInProgress=1)
//   현재 진척        28/29 → C33 + C35 + C36 모두 통과 시 31/31
// =============================================================================
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
using TDA.PB4.Tooling;

namespace TDA.PB4.Tooling.Editor
{
    public static class Week2ChecklistPatcher
    {
        private const string MENU_PATH = "Window/pB-4/Day 5/Patch Checklist (T5.3 entries)";
        private const string T53_REPORT_REL = "../Reports/auto/Week2_Day5_T5_3_MobRegression.md"; // 도구 자동 출력 규약(Reports/_index.md)
        private const string T53_PASS_MARKER = "종합 판정**: ✅ PASS";

        [MenuItem(MENU_PATH, priority = 60)]
        public static void Patch()
        {
            // 1. Week2Checklist.asset 검색
            var guids = AssetDatabase.FindAssets("t:Week2ChecklistSO");
            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog("Patcher",
                    "Week2ChecklistSO 자산을 찾을 수 없음. Data/SO/Checklist/ 위치 확인.",
                    "확인");
                return;
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var so = AssetDatabase.LoadAssetAtPath<Week2ChecklistSO>(path);
            if (so == null)
            {
                EditorUtility.DisplayDialog("Patcher",
                    $"자산 로드 실패: {path}", "확인");
                return;
            }

            // 2. 추가할 항목 정의
            var newEntries = new[]
            {
                new ChecklistItem
                {
                    id = "WK2_C35",
                    title = "T5.3 Mob 회귀 보고서 산출",
                    description = "Day 5 T5.3: MobRegressionRunner가 Reports/Week2_Day5_T5_3_MobRegression.md를 출력했는지 확인",
                    category = CheckCategory.Code,
                    verifyMethod = "FileExists:" + T53_REPORT_REL,
                    assignedDay = "5",
                    ignoreInProgress = false,
                },
                new ChecklistItem
                {
                    id = "WK2_C36",
                    title = "T5.3 Mob 회귀 종합 판정 PASS",
                    description = "Day 5 T5.3: 보고서 §1 통과 기준 매트릭스가 ✅ PASS로 마감되었는지 확인. " +
                                  "FAIL 시 Week 3 진입 전 회귀 우선 처리 필요.",
                    category = CheckCategory.Runtime,
                    verifyMethod = "FileContains:" + T53_REPORT_REL + ":" + T53_PASS_MARKER,
                    assignedDay = "5",
                    ignoreInProgress = false,
                },
            };

            // 3. 기존 ID와 중복 체크 (멱등)
            int added = 0;
            foreach (var entry in newEntries)
            {
                bool exists = so.items.Any(e => e.id == entry.id);
                if (exists)
                {
                    Debug.Log($"[Patcher] {entry.id} 이미 존재 — 스킵");
                    continue;
                }
                so.items.Add(entry);
                added++;
                Debug.Log($"[Patcher] {entry.id} 추가됨: {entry.title}");
            }

            if (added == 0)
            {
                EditorUtility.DisplayDialog("Patcher",
                    "추가할 항목 없음 — 이미 패치된 상태.\n\n" +
                    "Tracker 평가 가능 항목: 31개 (C35 + C36 포함)",
                    "확인");
                return;
            }

            // 4. 자산 저장
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Patcher",
                $"{added}개 항목 추가 완료.\n\n" +
                "다음 단계:\n" +
                "1. Week 2 Progress Window 열기 (Window > pB-4 > Week 2 Progress)\n" +
                "2. Play 진입 → Tracker 자동 재평가\n" +
                "3. C35 + C36이 PASS인지 확인\n\n" +
                "진척률: 30/31 → C33 + C35 + C36 모두 PASS 시 31/31",
                "확인");

            Selection.activeObject = so;
            EditorGUIUtility.PingObject(so);
        }

        // ==================================================================
        // 보조 메뉴: 현재 체크리스트 상태 조회 (디버그)
        // ==================================================================
        [MenuItem("Window/pB-4/Day 5/Inspect Checklist Counts", priority = 61)]
        public static void InspectCounts()
        {
            var guids = AssetDatabase.FindAssets("t:Week2ChecklistSO");
            if (guids.Length == 0) return;

            var so = AssetDatabase.LoadAssetAtPath<Week2ChecklistSO>(
                AssetDatabase.GUIDToAssetPath(guids[0]));

            int total = so.items.Count;
            int evaluable = so.items.Count(e => !e.ignoreInProgress);
            int manual = so.items.Count(e => e.ignoreInProgress);

            var byDay = so.items.GroupBy(e => string.IsNullOrEmpty(e.assignedDay) ? "(미지정)" : e.assignedDay)
                .OrderBy(g => g.Key)
                .Select(g => $"  Day {g.Key}: {g.Count()} (자동 {g.Count(e => !e.ignoreInProgress)}, 수동 {g.Count(e => e.ignoreInProgress)})");

            string msg =
                $"체크리스트 통계\n\n" +
                $"총 항목:    {total}\n" +
                $"자동 평가:  {evaluable}\n" +
                $"수동 마킹:  {manual}\n\n" +
                $"Day별 분포:\n{string.Join("\n", byDay)}";

            EditorUtility.DisplayDialog("Checklist Counts", msg, "확인");
        }
    }
}
#endif
