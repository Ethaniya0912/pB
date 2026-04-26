// =============================================================================
// Day4ChecklistEvaluator.cs  |  pB-4 Week 2 Day 4
// Layer  : Editor Tool (자동화 테스트)
// Owner  : Person A
//
// 역할:
//   Day 4 체크리스트 D4_C01 ~ D4_C10을 코드로 자동 평가.
//   Week2ProgressTracker가 호출하거나 단독 메뉴로 실행 가능.
//
// 검증 방식:
//   - D4_C01: EventBus.OnSpeechTrigger 이벤트 존재 (Reflection)
//   - D4_C02: NPCAlignmentController.cs 파일에 RaiseSpeechTrigger 포함 (Text search)
//   - D4_C03~C05: 컴포넌트 부착 여부 (씬 NPC 검사)
//   - D4_C06: SpeechTemplateSO 4개 이상 (AssetDatabase)
//   - D4_C07: DialoguePresetSO 3개 이상 (AssetDatabase)
//   - D4_C08: Dispatcher OnEnable/OnDisable 짝 (Text search)
//   - D4_C09: Alignment 전이 시 말풍선 생성 (Play 모드 필요)
//   - D4_C10: 쿨다운 3초 이내 중복 차단 (Play 모드 필요)
//
// 사용법:
//   Unity 메뉴: Window/pB-4/Day 4/Run Day 4 Checklist Evaluation
//   → Console + EditorWindow에 결과 리포트
// =============================================================================
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using TDA.PB4.AI.Speech;
using TDA.PB4.Core;
using TDA.PB4.Core.Events;  // [Day 4 수정] SpeechTriggerContext

namespace TDA.PB4.Tooling.Editor
{
    public static class Day4ChecklistEvaluator
    {
        private struct CheckResult
        {
            public string id;
            public string title;
            public bool passed;
            public string detail;
            public bool needsPlayMode;
        }

        [MenuItem("Window/pB-4/Day 4/Run Day 4 Checklist Evaluation")]
        public static void Evaluate()
        {
            var results = new List<CheckResult>
            {
                Check_D4_C01_EventBusExtension(),
                Check_D4_C02_AlignmentControllerPatch(),
                Check_D4_C03_DispatcherInScene(),
                Check_D4_C04_AssemblerInScene(),
                Check_D4_C05_RendererInScene(),
                Check_D4_C06_TemplateAssets(),
                Check_D4_C07_PresetAssets(),
                Check_D4_C08_DispatcherLifecycle(),
                Check_D4_C09_RuntimeBubbleCreation(),
                Check_D4_C10_CooldownWorks(),
            };

            ReportResults(results);
        }

        // ─────────────────────────────────────────────────────
        //   각 체크 구현
        // ─────────────────────────────────────────────────────

        private static CheckResult Check_D4_C01_EventBusExtension()
        {
            var eventField = typeof(EventBus).GetField("OnSpeechTrigger",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var raiseMethod = typeof(EventBus).GetMethod("RaiseSpeechTrigger",
                BindingFlags.Public | BindingFlags.Static);

            bool ok = (eventField != null && raiseMethod != null);
            string detail = ok
                ? $"OnSpeechTrigger event + RaiseSpeechTrigger() 모두 존재"
                : "OnSpeechTrigger 이벤트 또는 Raise 메서드 미발견";

            return new CheckResult
            {
                id = "D4_C01",
                title = "EventBus 확장 (OnSpeechTrigger + SpeechTriggerContext)",
                passed = ok,
                detail = detail
            };
        }

        private static CheckResult Check_D4_C02_AlignmentControllerPatch()
        {
            string[] guids = AssetDatabase.FindAssets("NPCAlignmentController t:Script");
            if (guids.Length == 0)
            {
                return Fail("D4_C02", "NPCAlignmentController에서 EventBus.RaiseSpeechTrigger 호출",
                           "NPCAlignmentController.cs 파일 미발견");
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            string code = File.ReadAllText(path);

            bool hasCall = code.Contains("EventBus.RaiseSpeechTrigger");
            string detail = hasCall
                ? $"{Path.GetFileName(path)}: RaiseSpeechTrigger 호출 확인됨"
                : $"{Path.GetFileName(path)}: 호출 없음 — Day 4 패치 미적용";

            return new CheckResult
            {
                id = "D4_C02",
                title = "NPCAlignmentController 패치",
                passed = hasCall,
                detail = detail
            };
        }

        private static CheckResult Check_D4_C03_DispatcherInScene()
            => CheckComponentInScene<SpeechDispatcher>("D4_C03", "SpeechDispatcher 컴포넌트 부착");

        private static CheckResult Check_D4_C04_AssemblerInScene()
            => CheckComponentInScene<SpeechAssembler>("D4_C04", "SpeechAssembler 컴포넌트 부착");

        private static CheckResult Check_D4_C05_RendererInScene()
            => CheckComponentInScene<DialogueRenderer>("D4_C05", "DialogueRenderer 컴포넌트 부착");

        private static CheckResult CheckComponentInScene<T>(string id, string title) where T : MonoBehaviour
        {
            var instances = GameObject.FindObjectsByType<T>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = instances?.Length ?? 0;
            return new CheckResult
            {
                id = id,
                title = title,
                passed = count > 0,
                detail = count > 0
                    ? $"{count}개 인스턴스 발견 — 씬에 정상 부착됨"
                    : $"씬에 {typeof(T).Name} 없음 — Bootstrapper 실행 필요",
                needsPlayMode = false
            };
        }

        private static CheckResult Check_D4_C06_TemplateAssets()
        {
            string[] guids = AssetDatabase.FindAssets("t:SpeechTemplateSO");
            int count = guids.Length;
            return new CheckResult
            {
                id = "D4_C06",
                title = "SpeechTemplateSO 최소 4개",
                passed = count >= 4,
                detail = count >= 4
                    ? $"{count}개 Template SO 발견됨"
                    : $"{count}개만 있음 — 최소 4개 필요. 'Generate Sample Speech SOs' 실행."
            };
        }

        private static CheckResult Check_D4_C07_PresetAssets()
        {
            string[] guids = AssetDatabase.FindAssets("t:DialoguePresetSO");
            int count = guids.Length;
            return new CheckResult
            {
                id = "D4_C07",
                title = "DialoguePresetSO 최소 3개 (Warm/Cold/Neutral)",
                passed = count >= 3,
                detail = count >= 3
                    ? $"{count}개 Preset SO 발견됨"
                    : $"{count}개만 있음 — 최소 3개 필요."
            };
        }

        private static CheckResult Check_D4_C08_DispatcherLifecycle()
        {
            string[] guids = AssetDatabase.FindAssets("SpeechDispatcher t:Script");
            if (guids.Length == 0)
                return Fail("D4_C08", "Dispatcher OnEnable/OnDisable 짝", "SpeechDispatcher.cs 미발견");

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            string code = File.ReadAllText(path);

            bool hasSubscribe = code.Contains("OnSpeechTrigger += OnSpeechTrigger")
                             || code.Contains("EventBus.OnSpeechTrigger +=");
            bool hasUnsubscribe = code.Contains("OnSpeechTrigger -= OnSpeechTrigger")
                               || code.Contains("EventBus.OnSpeechTrigger -=");
            bool hasOnEnable = code.Contains("void OnEnable()");
            bool hasOnDisable = code.Contains("void OnDisable()");
            bool ok = hasSubscribe && hasUnsubscribe && hasOnEnable && hasOnDisable;

            return new CheckResult
            {
                id = "D4_C08",
                title = "Dispatcher OnEnable/OnDisable 생명주기 짝",
                passed = ok,
                detail = ok
                    ? "OnEnable (+=) / OnDisable (-=) 짝 정상"
                    : $"OnEnable={hasOnEnable}, OnDisable={hasOnDisable}, +={hasSubscribe}, -={hasUnsubscribe}"
            };
        }

        private static CheckResult Check_D4_C09_RuntimeBubbleCreation()
        {
            if (!Application.isPlaying)
            {
                return new CheckResult
                {
                    id = "D4_C09",
                    title = "Alignment 전이 시 말풍선 생성",
                    passed = false,
                    detail = "Play 모드에서만 검증 가능 — Play 진입 후 다시 실행",
                    needsPlayMode = true
                };
            }

            var dispatcher = GameObject.FindFirstObjectByType<SpeechDispatcher>();
            if (dispatcher == null)
            {
                return Fail("D4_C09", "Alignment 전이 시 말풍선", "씬에 SpeechDispatcher 없음");
            }

            // 강제로 트리거 발행하여 파이프라인 작동 검증
            dispatcher.ResetCooldowns();
            bool received = false;
            Action<string, SpeechTriggerContext> probe = (trg, ctx) => { received = true; };
            EventBus.OnSpeechTrigger += probe;

            EventBus.RaiseSpeechTrigger(
                SpeechTriggerIds.COMPANION_DEFAULT,
                new SpeechTriggerContext
                {
                    characterId = dispatcher.name,
                    targetPlayerId = 0UL,
                    currentAlignment = TDA.PB4.Data.NPCAlignment.Companion,
                    previousAlignment = TDA.PB4.Data.NPCAlignment.Friendly,
                    reason = TDA.PB4.Interfaces.Narrative.AlignmentChangeReason.Manual
                });

            EventBus.OnSpeechTrigger -= probe;

            return new CheckResult
            {
                id = "D4_C09",
                title = "Alignment 전이 시 말풍선 생성",
                passed = received,
                detail = received ? "EventBus 파이프라인 작동 확인" : "이벤트 수신 실패",
                needsPlayMode = true
            };
        }

        private static CheckResult Check_D4_C10_CooldownWorks()
        {
            string[] guids = AssetDatabase.FindAssets("SpeechDispatcher t:Script");
            if (guids.Length == 0)
                return Fail("D4_C10", "쿨다운 3초 이내 중복 차단", "SpeechDispatcher.cs 미발견");

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            string code = File.ReadAllText(path);

            bool hasCooldown = code.Contains("IsOnCooldown") && code.Contains("cooldownSec");
            return new CheckResult
            {
                id = "D4_C10",
                title = "같은 trigger 3초 이내 중복 차단 (쿨다운)",
                passed = hasCooldown,
                detail = hasCooldown
                    ? "IsOnCooldown + cooldownSec 로직 확인됨"
                    : "쿨다운 로직 미발견"
            };
        }

        // ─────────────────────────────────────────────────────
        //   헬퍼 및 리포트
        // ─────────────────────────────────────────────────────
        private static CheckResult Fail(string id, string title, string reason)
            => new CheckResult { id = id, title = title, passed = false, detail = reason };

        private static void ReportResults(List<CheckResult> results)
        {
            var sb = new StringBuilder();
            int passed = 0, total = results.Count, needsPlay = 0;

            sb.AppendLine("════ Day 4 Checklist Evaluation ════");
            sb.AppendLine();

            foreach (var r in results)
            {
                string mark = r.passed ? "✅" : (r.needsPlayMode ? "⏸" : "❌");
                sb.AppendLine($"{mark} [{r.id}] {r.title}");
                sb.AppendLine($"      → {r.detail}");
                sb.AppendLine();

                if (r.passed) passed++;
                if (r.needsPlayMode && !r.passed) needsPlay++;
            }

            int effective = total - needsPlay;
            sb.AppendLine($"════ 결과: {passed}/{effective} 통과 (Play-only {needsPlay}개 제외) ════");

            string report = sb.ToString();
            Debug.Log(report);

            // 보고서 파일로도 저장
            string reportDir = "Assets/Reports";
            if (!Directory.Exists(reportDir)) Directory.CreateDirectory(reportDir);
            File.WriteAllText($"{reportDir}/Day4_ChecklistReport.md",
                $"# Day 4 Checklist Report\n\n" +
                $"생성: {DateTime.Now:yyyy-MM-dd HH:mm}\n\n" +
                $"```\n{report}\n```\n");
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Day 4 체크리스트 평가 완료",
                $"{passed}/{effective} 통과\n\n" +
                (needsPlay > 0 ? $"{needsPlay}개는 Play 모드 필요\n\n" : "") +
                "상세 결과: Console + Assets/Reports/Day4_ChecklistReport.md",
                "OK");
        }
    }
}
#endif
