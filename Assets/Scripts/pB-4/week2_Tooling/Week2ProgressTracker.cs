// =============================================================================
// Week2ProgressTracker.cs  |  pB-4 Week 2 — Day 1 T1.6 (v2 개정)
// 역할: Week2Checklist의 25항목을 자동 평가하여 Console + 진척률 보고.
//       Day 5 T5.1에서 EditorWindow UI + .md 자동 출력 추가.
// 요구사항 #8 (진척률 가시화)의 1/5 단계.
//
// [6종 검증 패턴]
//   1) HasComponent:TypeName       — 씬 내 해당 타입 컴포넌트 1개+ 존재
//   2) NoActiveStubs               — 모든 Brain이 IsStubFree() true
//   3) FileExists:RelativePath     — Assets/ 하위 파일 존재
//   4) BBVectorNonZero             — GameBlackboard.ActiveSituationVector에 non-zero 요소
//   5) EventSubscribed:EventName   — EventBus의 이벤트에 구독자 1+ (Reflection)
//   6) TagsCount:Min               — Resolver 평균 ActiveTags 수 >= Min
//
// [v2 코드리뷰 개정]
//   - [T1] CheckEventSubscribers 의 "On" 접두사 중복 로직 명확화.
//          EventBus 규약: event는 반드시 "On" prefix (OnKarmaShift 등).
//          verifyMethod의 "EventSubscribed:OnXxx" 또는 "EventSubscribed:Xxx" 모두 지원.
//   - [T3] FindTypeByName에 Dictionary 캐시 추가.
//          autoEvaluateInterval 5초 × 25항목마다 전체 어셈블리 순회 → 캐시 후 O(1).
//   - [T5] Reflection 기반 검증의 IL2CPP 주의사항 주석 추가.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using TDA.PB4.AI;
using TDA.PB4.Core;
using TDA.PB4.Interfaces.Core;

namespace TDA.PB4.Tooling
{
    /// <summary>Tracker 로그 레벨.</summary>
    public enum TrackerLogLevel
    {
        None = 0,
        Error = 1,
        Warn = 2,
        Info = 3,
        Verbose = 4
    }

    /// <summary>Week 2 진척률 추적 싱글톤.</summary>
    /// <remarks>
    /// Bootstrapper가 ReportEvent로 보고 + 자체 autoEvaluate가 주기적 평가.
    /// 구현: IProgressTracker 인터페이스.
    ///
    /// [T5] 주의: 이 Tracker는 Reflection을 사용하므로 IL2CPP 빌드에서 type stripping에
    /// 취약. Editor 전용 검증 용도로 사용 권장. 또는 link.xml에 보존 설정 필요.
    /// </remarks>
    public class Week2ProgressTracker : MonoBehaviour, IProgressTracker
    {
        /// <summary>싱글톤 참조. 필요 시 외부 코드가 즉시 접근.</summary>
        public static Week2ProgressTracker Instance { get; private set; }

        // ==================================================================
        // Inspector 필드
        // ==================================================================

        [Header("━━━ 데이터 ━━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("평가 대상 체크리스트. Week2Checklist.asset 드래그.")]
        [SerializeField] private Week2ChecklistSO checklist;

        [Header("━━━ 로깅 (요구사항 #8) ━━━━━━━━━━━")]

        [Tooltip("로그 레벨. Info=진척률만, Verbose=각 항목 평가 과정.")]
        public TrackerLogLevel logLevel = TrackerLogLevel.Info;

        [Header("━━━ 자동 평가 ━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("자동 평가 주기 (초). 0 = 수동 평가만.")]
        [Range(0f, 30f)] public float autoEvaluateInterval = 5f;

        [Tooltip("진척률이 변했을 때만 로그 출력 (스팸 방지).")]
        public bool onlyLogOnChange = true;

        // ==================================================================
        // 런타임 상태
        // ==================================================================

        private float lastEvalTime;
        private int lastReportedPassed = -1;

        // [T3] 타입 캐시: FindTypeByName 결과를 저장하여 반복 호출 성능 향상.
        //      autoEvaluateInterval=5초 × 25항목 × 각 항목마다 전체 어셈블리 순회 → 
        //      Dictionary 캐시 후 O(1) 조회.
        private static readonly Dictionary<string, Type> TypeNameCache = new Dictionary<string, Type>();

        // ==================================================================
        // Lifecycle
        // ==================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Log(TrackerLogLevel.Warn, $"복수의 Tracker 인스턴스 감지. {name} destroy.");
                Destroy(this);
                return;
            }
            Instance = this;

            if (checklist == null)
            {
                Log(TrackerLogLevel.Error, "ChecklistSO 미할당. Inspector에서 드래그 필요.");
            }
            else
            {
                // 재시작마다 런타임 상태 초기화 (이전 Play 결과 제거)
                checklist.ResetRuntimeStatus();
                Log(TrackerLogLevel.Info, $"초기화 완료 (체크리스트 {checklist.items.Count}항목)");
            }
        }

        private void Update()
        {
            if (autoEvaluateInterval <= 0f) return;
            if (Time.unscaledTime - lastEvalTime < autoEvaluateInterval) return;

            lastEvalTime = Time.unscaledTime;
            EvaluateAll();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ==================================================================
        // 전체 평가
        // ==================================================================

        /// <summary>수동 전체 평가. Context menu 또는 외부 호출.</summary>
        [ContextMenu("Evaluate All Now")]
        public void EvaluateAll()
        {
            if (checklist == null) return;

            foreach (var item in checklist.items)
            {
                try
                {
                    bool result = EvaluateExpression(item.verifyMethod, out string detail);
                    var newStatus = item.ignoreInProgress
                        ? CheckStatus.ExternalDep
                        : (result ? CheckStatus.Passed : CheckStatus.Failed);

                    if (newStatus != item.runtimeStatus)
                    {
                        item.runtimeStatus = newStatus;
                        item.lastCheckTime = DateTime.Now.ToString("HH:mm:ss");
                        item.lastLogMessage = detail;

                        Log(TrackerLogLevel.Verbose,
                            $"[{item.id}] {item.title}: {newStatus} ({detail})");
                    }
                }
                catch (Exception e)
                {
                    item.runtimeStatus = CheckStatus.Failed;
                    item.lastLogMessage = $"예외: {e.Message}";
                    Log(TrackerLogLevel.Warn, $"[{item.id}] 평가 예외: {e.Message}");
                }
            }

            // 진척률 변화 시에만 요약 로그 (또는 onlyLogOnChange=false면 매번)
            if (!onlyLogOnChange || checklist.PassedCount != lastReportedPassed)
            {
                Log(TrackerLogLevel.Info,
                    $"진척률: {checklist.PassedCount}/{checklist.RelevantTotal} " +
                    $"({checklist.ProgressRatio:P0})");
                lastReportedPassed = checklist.PassedCount;
            }
        }

        // ==================================================================
        // verifyMethod 표현식 파서 (6종 패턴)
        // ==================================================================

        /// <summary>체크 항목의 verifyMethod 문자열을 파싱하여 bool 반환.</summary>
        /// <param name="expr">예: 'HasComponent:TrustMatrix', 'NoActiveStubs'.</param>
        /// <param name="detail">검증 결과 상세 설명.</param>
        private bool EvaluateExpression(string expr, out string detail)
        {
            detail = "";
            if (string.IsNullOrEmpty(expr))
            {
                detail = "verifyMethod 비어있음";
                return false;
            }

            // Pattern 1: HasComponent:TypeName
            if (expr.StartsWith("HasComponent:"))
            {
                var typeName = expr.Substring("HasComponent:".Length).Trim();
                var type = FindTypeByName(typeName);
                if (type == null)
                {
                    detail = $"타입 {typeName} 미발견";
                    return false;
                }
                var obj = UnityEngine.Object.FindAnyObjectByType(type);
                detail = obj != null ? $"{typeName} 발견 ({obj.name})" : $"{typeName} 씬에 0개";
                return obj != null;
            }

            // Pattern 2: NoActiveStubs
            if (expr == "NoActiveStubs")
            {
                var brains = UnityEngine.Object.FindObjectsByType<BaseAIBrain>(
                    FindObjectsSortMode.None);
                if (brains.Length == 0)
                {
                    detail = "씬에 Brain 0개 (평가 불가)";
                    return false;
                }
                int stubCount = brains.Count(b => !b.IsStubFree());
                detail = $"{brains.Length}개 중 {stubCount}개 stub 사용";
                return stubCount == 0;
            }

            // Pattern 3: FileExists:RelativePath (Assets/ 기준)
            if (expr.StartsWith("FileExists:"))
            {
                var relPath = expr.Substring("FileExists:".Length).Trim();
                var fullPath = Path.Combine(Application.dataPath, relPath);
                bool exists = File.Exists(fullPath);
                detail = exists ? $"{relPath} 존재" : $"{relPath} 없음";
                return exists;
            }

            // Pattern 4: BBVectorNonZero
            if (expr == "BBVectorNonZero")
            {
                var bb = GameBlackboard.Instance;
                if (bb == null) { detail = "GameBlackboard 없음"; return false; }
                var vec = bb.ActiveSituationVector;
                if (vec == null || vec.Length == 0) { detail = "SitVector 미생성"; return false; }
                bool nonZero = vec.Any(v => Mathf.Abs(v) > 0.001f);
                detail = nonZero
                    ? $"{vec.Count(v => Mathf.Abs(v) > 0.001f)}/{vec.Length} 요소 > 0"
                    : $"{vec.Length}개 모두 0";
                return nonZero;
            }

            // Pattern 5: EventSubscribed:EventName
            if (expr.StartsWith("EventSubscribed:"))
            {
                var evName = expr.Substring("EventSubscribed:".Length).Trim();
                bool hasSub = CheckEventSubscribers(evName);
                detail = hasSub ? $"{evName} 구독자 있음" : $"{evName} 구독자 없음";
                return hasSub;
            }

            // Pattern 6: TagsCount:Min
            if (expr.StartsWith("TagsCount:"))
            {
                var minStr = expr.Substring("TagsCount:".Length).Trim();
                if (!int.TryParse(minStr, out int minCount))
                {
                    detail = $"파싱 실패: {minStr}";
                    return false;
                }
                var resolvers = UnityEngine.Object.FindObjectsByType<PersonalityTagResolver>(
                    FindObjectsSortMode.None);
                if (resolvers.Length == 0) { detail = "Resolver 0개"; return false; }
                float avgTags = (float)resolvers.Average(r => r.ActiveTags.Count);
                detail = $"평균 {avgTags:F1}개 (기준 {minCount})";
                return avgTags >= minCount;
            }

            // 미지의 패턴
            detail = $"미지의 패턴: {expr}";
            Log(TrackerLogLevel.Warn, detail);
            return false;
        }

        // ==================================================================
        // Reflection 헬퍼
        // ==================================================================

        /// <summary>이름으로 로드된 모든 어셈블리에서 타입 검색. [T3] 캐시 적용.</summary>
        /// <remarks>
        /// 첫 호출 시 AppDomain 전체 순회 → 캐시 저장.
        /// 미발견 시에는 캐시에 null도 저장하지 않음 (어셈블리 재로드 가능성 대비).
        /// </remarks>
        private Type FindTypeByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // 캐시 조회
            if (TypeNameCache.TryGetValue(name, out var cached))
                return cached;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == name || t.FullName == name)
                        {
                            TypeNameCache[name] = t;
                            return t;
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // 일부 어셈블리가 로드 불가 시 스킵
                    continue;
                }
            }
            return null;
        }

        /// <summary>EventBus의 static event에 구독자 있는지 Reflection으로 확인.</summary>
        /// <remarks>
        /// [T1] verifyMethod에서 "EventSubscribed:OnKarmaShift" 또는 "EventSubscribed:KarmaShift" 모두 지원.
        /// EventBus 규약: 모든 event는 "On" prefix ("OnKarmaShift", "OnTrustTierChanged" 등).
        /// 1차 조회: 입력값 그대로 (evName).
        /// 2차 조회: "On" prefix가 없으면 추가하여 재시도.
        /// </remarks>
        private bool CheckEventSubscribers(string evName)
        {
            try
            {
                var busType = FindTypeByName("EventBus");
                if (busType == null) return false;

                // 1차: 입력값 그대로
                var field = busType.GetField(evName, BindingFlags.Static | BindingFlags.NonPublic);

                // 2차: "On" prefix 없으면 추가 시도 (이미 있으면 1차에서 찾음)
                if (field == null && !evName.StartsWith("On"))
                {
                    field = busType.GetField("On" + evName, BindingFlags.Static | BindingFlags.NonPublic);
                }

                if (field == null) return false;
                var del = field.GetValue(null) as Delegate;
                return del != null && del.GetInvocationList().Length > 0;
            }
            catch
            {
                return false;
            }
        }

        // ==================================================================
        // 로깅
        // ==================================================================

        private void Log(TrackerLogLevel level, string msg)
        {
            if (logLevel < level) return;
            switch (level)
            {
                case TrackerLogLevel.Error: Debug.LogError($"[Tracker] {msg}"); break;
                case TrackerLogLevel.Warn: Debug.LogWarning($"[Tracker] {msg}"); break;
                default: Debug.Log($"[Tracker] {msg}"); break;
            }
        }

        // ==================================================================
        // IProgressTracker 구현
        // ==================================================================

        public void ReportEvent(string checkId, bool passed, string detail)
        {
            if (checklist == null) return;

            var item = checklist.FindById(checkId);
            if (item == null)
            {
                Log(TrackerLogLevel.Verbose, $"ReportEvent: {checkId} 미지의 ID");
                return;
            }

            item.runtimeStatus = passed ? CheckStatus.Passed : CheckStatus.Failed;
            item.lastLogMessage = detail;
            item.lastCheckTime = DateTime.Now.ToString("HH:mm:ss");

            Log(TrackerLogLevel.Verbose, $"{checkId}: {(passed ? "PASS" : "FAIL")} - {detail}");
        }

        public void ReportProgress(string milestoneId, float ratio)
        {
            Log(TrackerLogLevel.Info, $"Milestone {milestoneId}: {ratio:P0}");
        }

        public void EmitFinalReport()
        {
            EvaluateAll();
            if (checklist == null) return;

            // Day 1 v0: Console 요약만.
            // Day 5 T5.1에서 .md 파일 출력 + EditorWindow UI 추가.
            Log(TrackerLogLevel.Info,
                $"═══ 최종 보고서 ═══ {checklist.PassedCount}/{checklist.RelevantTotal} " +
                $"({checklist.ProgressRatio:P0})");
        }

        /// <summary>디버그: 현재 모든 항목 상태 Console 덤프.</summary>
        [ContextMenu("Dump All Items")]
        public void DumpAllItems()
        {
            if (checklist == null)
            {
                Debug.LogWarning("[Tracker] Checklist 미할당");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"═══ Week 2 Checklist Dump ({DateTime.Now:HH:mm:ss}) ═══");
            foreach (var item in checklist.items)
            {
                sb.AppendLine($"  [{item.id}] {item.runtimeStatus,-11} | {item.title} | {item.lastLogMessage}");
            }
            sb.AppendLine($"Total: {checklist.PassedCount}/{checklist.RelevantTotal} ({checklist.ProgressRatio:P0})");
            Debug.Log(sb.ToString());
        }
    }
}
