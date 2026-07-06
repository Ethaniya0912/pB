// =============================================================================
// Week2ProgressTracker.cs  |  pB-4 Week 2 — Day 1 T1.6 (v3 NGO 패턴 추가)
// 역할: Week2Checklist의 항목을 자동 평가하여 Console + 진척률 보고.
//       Day 5 T5.1에서 EditorWindow UI + .md 자동 출력 추가.
// 요구사항 #8 (진척률 가시화)의 1/5 단계.
//
// [9종 검증 패턴]
//   1) HasComponent:TypeName       — 씬 내 해당 타입 컴포넌트 1개+ 존재
//   2) NoActiveStubs               — 모든 Brain이 IsStubFree() true
//   3) FileExists:RelativePath     — Assets/ 하위 파일 존재
//   4) BBVectorNonZero             — GameBlackboard.ActiveSituationVector에 non-zero 요소
//   5) EventSubscribed:EventName   — EventBus의 이벤트에 구독자 1+ (Reflection)
//   6) TagsCount:Min               — Resolver 평균 ActiveTags 수 >= Min
//   7) [v3 신규] InheritsFrom:Child:Parent — 자식 클래스의 부모 상속 확인
//   8) [v3 신규] HasNetworkObject   — 씬 내 NetworkObject 컴포넌트 존재
//   9) [v3 신규] TypeCount:TypeName:Min — 씬 내 해당 타입 개수 >= Min
//
// [v3 NGO 추가 — 2026-04-23]
//   - WK2_C26 (NetworkBehaviour 상속 확인) — InheritsFrom 패턴 사용
//   - WK2_C27 (NetworkVariable writePerm) — 파일 내용 grep 기반 (FileContains 신규)
//
// [v2 코드리뷰 개정 — 기존]
//   - [T1] CheckEventSubscribers 의 "On" 접두사 중복 로직 명확화.
//   - [T3] FindTypeByName에 Dictionary 캐시 추가.
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

                // [개선] 실패 + 미평가 항목 자동 출력 (진척률 변화 시)
                LogIncompleteItems();

                lastReportedPassed = checklist.PassedCount;
            }
        }

        // ==================================================================
        // 미완료 항목 자동 출력 (실패/미평가 항목 한 번에 표시)
        // ==================================================================

        /// <summary>진척률 변화 시 호출. 실패/NotChecked 항목을 Warn 레벨로 표시.</summary>
        private void LogIncompleteItems()
        {
            if (checklist == null) return;

            var failed = new List<string>();
            var notChecked = new List<string>();

            foreach (var item in checklist.items)
            {
                if (item.ignoreInProgress) continue;

                switch (item.runtimeStatus)
                {
                    case CheckStatus.Failed:
                        failed.Add($"  ✗ [{item.id}] {item.title}  →  {item.lastLogMessage}");
                        break;
                    case CheckStatus.NotChecked:
                        notChecked.Add($"  ? [{item.id}] {item.title}");
                        break;
                }
            }

            if (failed.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"✗ 실패 항목 ({failed.Count}건):");
                foreach (var line in failed) sb.AppendLine(line);
                Log(TrackerLogLevel.Warn, sb.ToString().TrimEnd());
            }

            if (notChecked.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"? 미평가 항목 ({notChecked.Count}건):");
                foreach (var line in notChecked) sb.AppendLine(line);
                Log(TrackerLogLevel.Info, sb.ToString().TrimEnd());
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

            // [v3 신규] Pattern 7: InheritsFrom:ChildType:ParentType
            //   목적: WK2_C26 — NetworkBehaviour 상속 자동 확인
            //   사용 예: "InheritsFrom:HumanoidAIBrain:NetworkBehaviour"
            //   로직: Reflection으로 Child 타입 찾아 .BaseType 체인 올라가며 Parent 탐색
            if (expr.StartsWith("InheritsFrom:"))
            {
                var args = expr.Substring("InheritsFrom:".Length).Split(':');
                if (args.Length != 2)
                {
                    detail = $"형식 오류 (InheritsFrom:Child:Parent): {expr}";
                    return false;
                }
                var childType = FindTypeByName(args[0].Trim());
                var parentType = FindTypeByName(args[1].Trim());
                if (childType == null) { detail = $"Child 타입 {args[0]} 미발견"; return false; }
                if (parentType == null) { detail = $"Parent 타입 {args[1]} 미발견"; return false; }
                bool inherits = parentType.IsAssignableFrom(childType);
                detail = inherits
                    ? $"{childType.Name}이 {parentType.Name}을 상속 ✓"
                    : $"{childType.Name}은 {parentType.Name}을 상속하지 않음 ✗";
                return inherits;
            }

            // [v3 신규] Pattern 8: HasNetworkObject
            //   목적: 씬에 NetworkObject 컴포넌트 존재 (NGO 2.0 필수 배치 확인)
            //   사용 예: "HasNetworkObject"
            if (expr == "HasNetworkObject")
            {
                var noType = FindTypeByName("NetworkObject");
                if (noType == null)
                {
                    detail = "NetworkObject 타입 미발견 (NGO 패키지 설치 확인)";
                    return false;
                }
                var obj = UnityEngine.Object.FindAnyObjectByType(noType);
                detail = obj != null
                    ? $"NetworkObject 발견 ({obj.name})"
                    : "씬에 NetworkObject 0개 — NPC prefab의 NetworkObject 부착 확인 필요";
                return obj != null;
            }

            // [v3 신규] Pattern 9: TypeCount:TypeName:Min
            //   목적: 씬 내 특정 타입 개수가 Min 이상
            //   사용 예: "TypeCount:NetworkBehaviour:5" — NetworkBehaviour 5개+ 확인
            if (expr.StartsWith("TypeCount:"))
            {
                var args = expr.Substring("TypeCount:".Length).Split(':');
                if (args.Length != 2 || !int.TryParse(args[1].Trim(), out int minCount))
                {
                    detail = $"형식 오류 (TypeCount:TypeName:Min): {expr}";
                    return false;
                }
                var type = FindTypeByName(args[0].Trim());
                if (type == null) { detail = $"타입 {args[0]} 미발견"; return false; }
                var objs = UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None);
                detail = $"{args[0]} {objs.Length}개 (기준 {minCount}개+)";
                return objs.Length >= minCount;
            }

            // [v3 신규] Pattern 10: FileContains:Path:Text
            //   목적: 파일 내용에 특정 문자열 포함 — WK2_C27 (writePerm: Server) 용
            //   사용 예: "FileContains:Scripts/pB-4/week4_AI/TrustMatrix.cs:writePerm: NetworkVariableWritePermission.Server"
            //   주의: Assets/ 기준 상대경로. Text에 ':' 포함 시 그대로 전달됨.
            if (expr.StartsWith("FileContains:"))
            {
                var rest = expr.Substring("FileContains:".Length);
                int colonIdx = rest.IndexOf(':');
                if (colonIdx < 0)
                {
                    detail = $"형식 오류 (FileContains:Path:Text): {expr}";
                    return false;
                }
                var relPath = rest.Substring(0, colonIdx).Trim();
                var searchText = rest.Substring(colonIdx + 1);
                var fullPath = Path.Combine(Application.dataPath, relPath);
                if (!File.Exists(fullPath))
                {
                    detail = $"{relPath} 파일 없음";
                    return false;
                }
                try
                {
                    var content = File.ReadAllText(fullPath);
                    bool contains = content.Contains(searchText);
                    detail = contains
                        ? $"{relPath}에 '{searchText.Substring(0, Math.Min(40, searchText.Length))}' 포함 ✓"
                        : $"{relPath}에 '{searchText.Substring(0, Math.Min(40, searchText.Length))}' 없음";
                    return contains;
                }
                catch (Exception e)
                {
                    detail = $"파일 읽기 오류: {e.Message}";
                    return false;
                }
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

            // [Day 5 T5.1] Console 요약 + .md 파일 자동 출력
            Log(TrackerLogLevel.Info,
                $"═══ 최종 보고서 ═══ {checklist.PassedCount}/{checklist.RelevantTotal} " +
                $"({checklist.ProgressRatio:P0})");

            ExportToMarkdown();
        }

        // ==================================================================
        // [Day 5 T5.1] Markdown 보고서 자동 출력
        // ==================================================================

        /// <summary>
        /// 체크리스트 결과를 Markdown 파일로 출력.
        /// 출력 위치: Assets/../Reports/auto/Week2_Progress_{yyyyMMdd_HHmmss}.md (도구 자동 출력은 Reports/auto — Reports/_index.md 규약)
        /// </summary>
        [ContextMenu("Export to Markdown")]
        public void ExportToMarkdown()
        {
            if (checklist == null)
            {
                Log(TrackerLogLevel.Warn, "체크리스트 SO 미할당. .md 출력 스킵.");
                return;
            }

            EvaluateAll();

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string reportsDir = Path.Combine(projectRoot, "Reports", "auto");
            if (!Directory.Exists(reportsDir))
                Directory.CreateDirectory(reportsDir);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = $"Week2_Progress_{timestamp}.md";
            string filepath = Path.Combine(reportsDir, filename);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# pB-4 Week 2 진척률 보고서");
            sb.AppendLine();
            sb.AppendLine($"**생성 일시**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"**진척률**: {checklist.PassedCount}/{checklist.RelevantTotal} ({checklist.ProgressRatio:P0})");
            sb.AppendLine();

            // 상태별 카운트
            int passedCount = 0, failedCount = 0, notCheckedCount = 0, externalDepCount = 0;
            foreach (var item in checklist.items)
            {
                switch (item.runtimeStatus)
                {
                    case CheckStatus.Passed: passedCount++; break;
                    case CheckStatus.Failed: failedCount++; break;
                    case CheckStatus.NotChecked: notCheckedCount++; break;
                    case CheckStatus.ExternalDep: externalDepCount++; break;
                }
            }

            sb.AppendLine("## 요약");
            sb.AppendLine();
            sb.AppendLine($"- 통과: {passedCount}건");
            sb.AppendLine($"- 실패: {failedCount}건");
            sb.AppendLine($"- 미평가: {notCheckedCount}건");
            sb.AppendLine($"- 외부 의존: {externalDepCount}건");
            sb.AppendLine();

            // Day별 그룹
            sb.AppendLine("## 항목별 결과 (Day별 정렬)");
            sb.AppendLine();
            sb.AppendLine("| ID | Day | 상태 | 항목 | 검증 방법 | 마지막 메시지 |");
            sb.AppendLine("|------|:---:|:---:|------|------|------|");

            var sortedItems = new List<ChecklistItem>(checklist.items);
            sortedItems.Sort((a, b) => {
                int cmp = string.Compare(a.assignedDay, b.assignedDay, StringComparison.Ordinal);
                if (cmp != 0) return cmp;
                return string.Compare(a.id, b.id, StringComparison.Ordinal);
            });

            foreach (var item in sortedItems)
            {
                string statusIcon = item.runtimeStatus switch
                {
                    CheckStatus.Passed => "✅",
                    CheckStatus.Failed => "❌",
                    CheckStatus.NotChecked => "⏳",
                    CheckStatus.ExternalDep => "🔗",
                    _ => "?"
                };
                string day = string.IsNullOrEmpty(item.assignedDay) ? "-" : item.assignedDay;
                string lastMsg = string.IsNullOrEmpty(item.lastLogMessage) ? "-" : item.lastLogMessage.Replace("|", "\\|").Replace("\n", " ");
                if (lastMsg.Length > 80) lastMsg = lastMsg.Substring(0, 77) + "...";
                sb.AppendLine($"| {item.id} | {day} | {statusIcon} | {item.title} | `{item.verifyMethod}` | {lastMsg} |");
            }

            sb.AppendLine();

            // 실패 항목 상세
            if (failedCount > 0)
            {
                sb.AppendLine("## 실패 항목 상세");
                sb.AppendLine();
                foreach (var item in checklist.items)
                {
                    if (item.runtimeStatus != CheckStatus.Failed) continue;
                    sb.AppendLine($"### [{item.id}] {item.title}");
                    sb.AppendLine($"- 검증 방법: `{item.verifyMethod}`");
                    sb.AppendLine($"- 실패 사유: {item.lastLogMessage}");
                    sb.AppendLine($"- Day: {item.assignedDay}");
                    sb.AppendLine();
                }
            }

            // 미평가 항목
            if (notCheckedCount > 0)
            {
                sb.AppendLine("## 미평가 항목");
                sb.AppendLine();
                foreach (var item in checklist.items)
                {
                    if (item.runtimeStatus != CheckStatus.NotChecked) continue;
                    sb.AppendLine($"- [{item.id}] {item.title} (Day {item.assignedDay})");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("> 본 보고서는 Window > pB-4 > Week 2 Progress 또는 Tracker.ExportToMarkdown ContextMenu에서 자동 생성됩니다.");

            try
            {
                File.WriteAllText(filepath, sb.ToString());
                Log(TrackerLogLevel.Info,
                    $"Markdown 보고서 출력 완료: {filepath}");
            }
            catch (Exception e)
            {
                Log(TrackerLogLevel.Error,
                    $"Markdown 출력 실패: {e.Message}");
            }
        }

        /// <summary>디버그: 현재 모든 항목 상태를 상태별로 그룹화하여 Console 덤프.</summary>
        [ContextMenu("Dump All Items")]
        public void DumpAllItems()
        {
            if (checklist == null)
            {
                Debug.LogWarning("[Tracker] Checklist 미할당");
                return;
            }

            var passed = new List<ChecklistItem>();
            var failed = new List<ChecklistItem>();
            var notChecked = new List<ChecklistItem>();
            var externalDep = new List<ChecklistItem>();

            foreach (var item in checklist.items)
            {
                switch (item.runtimeStatus)
                {
                    case CheckStatus.Passed: passed.Add(item); break;
                    case CheckStatus.Failed: failed.Add(item); break;
                    case CheckStatus.ExternalDep: externalDep.Add(item); break;
                    default: notChecked.Add(item); break;
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"╔══ Week 2 Checklist Dump ({DateTime.Now:HH:mm:ss}) ══");
            sb.AppendLine($"║ 진척률: {checklist.PassedCount}/{checklist.RelevantTotal} ({checklist.ProgressRatio:P0})");
            sb.AppendLine($"║ 통과 {passed.Count}  |  실패 {failed.Count}  |  미평가 {notChecked.Count}  |  외부의존 {externalDep.Count}");
            sb.AppendLine("╠══════════════════════════════════════════════");

            if (failed.Count > 0)
            {
                sb.AppendLine($"║  ✗ 실패 ({failed.Count}건):");
                foreach (var item in failed)
                {
                    sb.AppendLine($"║    [{item.id}] {item.title}");
                    sb.AppendLine($"║        verifyMethod: {item.verifyMethod}");
                    sb.AppendLine($"║        사유:         {item.lastLogMessage}");
                    sb.AppendLine($"║        assignedDay:  {item.assignedDay}  /  lastCheck: {item.lastCheckTime}");
                }
                sb.AppendLine("╠══════════════════════════════════════════════");
            }

            if (notChecked.Count > 0)
            {
                sb.AppendLine($"║  ? 미평가 ({notChecked.Count}건):");
                foreach (var item in notChecked)
                    sb.AppendLine($"║    [{item.id}] {item.title}");
                sb.AppendLine("╠══════════════════════════════════════════════");
            }

            if (externalDep.Count > 0)
            {
                sb.AppendLine($"║  ⊖ 외부의존 ({externalDep.Count}건, 분모 제외):");
                foreach (var item in externalDep)
                    sb.AppendLine($"║    [{item.id}] {item.title}");
                sb.AppendLine("╠══════════════════════════════════════════════");
            }

            if (passed.Count > 0)
            {
                sb.AppendLine($"║  ✓ 통과 ({passed.Count}건):");
                foreach (var item in passed)
                    sb.AppendLine($"║    [{item.id}] {item.title}");
            }

            sb.AppendLine("╚══════════════════════════════════════════════");
            Debug.Log(sb.ToString());
        }

        /// <summary>디버그: 실패/미평가 항목만 Console 덤프.</summary>
        [ContextMenu("Dump Failed Only")]
        public void DumpFailedOnly()
        {
            if (checklist == null)
            {
                Debug.LogWarning("[Tracker] Checklist 미할당");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"╔══ 미완료 항목 Dump ({DateTime.Now:HH:mm:ss}) ══");
            sb.AppendLine($"║ 진척률: {checklist.PassedCount}/{checklist.RelevantTotal} ({checklist.ProgressRatio:P0})");
            sb.AppendLine("╠══════════════════════════════════════════════");

            int failCount = 0, notCheckedCount = 0;
            foreach (var item in checklist.items)
            {
                if (item.runtimeStatus == CheckStatus.Failed)
                {
                    failCount++;
                    sb.AppendLine($"║  ✗ [{item.id}] {item.title}");
                    sb.AppendLine($"║      verify: {item.verifyMethod}");
                    sb.AppendLine($"║      reason: {item.lastLogMessage}");
                }
                else if (item.runtimeStatus == CheckStatus.NotChecked)
                {
                    notCheckedCount++;
                    sb.AppendLine($"║  ? [{item.id}] {item.title}  (평가되지 않음)");
                }
            }

            if (failCount == 0 && notCheckedCount == 0)
                sb.AppendLine("║  🎉 완료! 모든 항목 통과.");
            else
                sb.AppendLine($"║ 요약: 실패 {failCount}건, 미평가 {notCheckedCount}건");

            sb.AppendLine("╚══════════════════════════════════════════════");
            Debug.Log(sb.ToString());
        }

        /// <summary>즉시 재평가 (autoEvaluateInterval 대기 없이).</summary>
        [ContextMenu("Re-Evaluate Now")]
        public void ReEvaluateNow()
        {
            Debug.Log("[Tracker] 수동 재평가 시작...");
            EvaluateAll();
        }
    }
}