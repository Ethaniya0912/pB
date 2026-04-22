// =============================================================================
// HumanoidBootstrapper.cs  |  pB-4 Week 2 — Day 1 T1.4 (v3: debugLog 일괄 제어)
// 역할: 씬 내 모든 HumanoidAIBrain의 초기화를 담당하는 DI 컨테이너.
//       1) 필요 컴포넌트 자동 부착 (5종)
//       2) 데이터 SO 주입 (TagRules, ActionConfig, Alignment, DialogueLibrary)
//       3) 컴포넌트 간 참조 연결 (Brain ↔ Formula/Resolver/Encoder/Trust/Trauma)
//       4) 초기 태그 발현
//       5) [v3] debugLog 자동 일괄 켬 (enableDebugLogOnBootstrap 옵션)
//       6) ProgressTracker에 완료 보고
// 실행 시점: Scene 로드 직후 (DefaultExecutionOrder(-100))
//
// [v3 변경 — Day 2 진단 편의 기능]
//   - enableDebugLogOnBootstrap 옵션 추가: 체크하면 BootstrapOne 시점에
//     Formula/Resolver/Trust/Trauma 4개 컴포넌트의 debugLog=true 자동 설정.
//   - ContextMenu "Toggle All DebugLog" 추가: Play 중 우클릭으로 즉시 전환.
//   - ContextMenu "Enable All DebugLog" / "Disable All DebugLog" 개별 제어.
// =============================================================================
using System.Linq;
using UnityEngine;
using TDA.PB4.AI;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Core;
using TDA.PB4.Data;
using TDA.PB4.Tooling;
using TDA.PB4.Interfaces.Core;

namespace TDA.PB4.Bootstrap
{
    /// <summary>Bootstrapper 로그 레벨. 개발 중에는 Info, 빌드는 Warn.</summary>
    public enum BootstrapLogLevel
    {
        None = 0,
        Error = 1,
        Warn = 2,
        Info = 3,
        Verbose = 4
    }

    /// <summary>Humanoid NPC 자동 초기화 및 의존성 주입 컨테이너.</summary>
    [DefaultExecutionOrder(-100)]
    public class HumanoidBootstrapper : MonoBehaviour
    {
        // ==================================================================
        // Inspector 필드
        // ==================================================================

        [Header("━━━ Logging ━━━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("로그 레벨. 개발 중 Info, 디버깅 중 Verbose, 빌드 Warn.")]
        public BootstrapLogLevel logLevel = BootstrapLogLevel.Info;

        [Tooltip("[v3] 부트스트랩 시 Formula/Resolver/Trust/Trauma 4개 컴포넌트의 " +
                 "debugLog=true를 자동 설정. Day 2 디버깅 편의용. " +
                 "빌드나 성능 테스트에서는 끄세요.")]
        public bool enableDebugLogOnBootstrap = false;

        [Header("━━━ 공유 데이터 SO (Day별 점진 연결) ━━")]

        [Tooltip("모든 Humanoid가 공유할 성격 태그 규칙. Day 2 T2.1에서 HumanoidTagRules.asset 드래그.")]
        public PersonalityTagRuleSO defaultTagRules;

        [Tooltip("행동 유틸리티 설정. Day 2 T2.2에서 HumanoidActionConfig.asset 드래그.")]
        public UtilityActionConfigSO defaultActionConfig;

        [Tooltip("4 진영 정의. 실제 사용은 Day 3 T3.2 (NPCAlignmentController 부착 시). " +
                 "Day 1에서는 선언만 되어 있음.")]
        public NPCAlignmentSO defaultAlignmentSO;

        [Tooltip("대사 라이브러리. 실제 사용은 Day 4 T4.5 (SpeechAssembler 부착 시). " +
                 "Day 1에서는 선언만 되어 있음.")]
        public DialogueLibrarySO defaultDialogueLibrary;

        [Header("━━━ 실행 조건 ━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("GameBlackboard 없어도 Bootstrap 계속할지. 단독 테스트 씬=true, 빌드=false.")]
        public bool allowStubFallback = true;

        [Tooltip("Play 중 이 컴포넌트를 비활성화해도 이미 부트스트랩된 NPC는 유지. false로 두세요.")]
        public bool destroyAfterBootstrap = false;

        [Header("━━━ 통계 (읽기 전용, Play 모드에서만 의미 있음) ━━")]

        [Tooltip("부트스트랩된 NPC 수 (Play only).")]
        [SerializeField] private int bootstrappedCount;

        [Tooltip("부트스트랩 소요 시간 (ms, Play only).")]
        [SerializeField] private float bootstrapDurationMs;

        [Tooltip("마지막 부트스트랩 실패 로그 (Play only, 텍스트가 길면 잘릴 수 있음).")]
        [SerializeField] private string lastFailureLog;

        // ==================================================================
        // 런타임 상태
        // ==================================================================

        private IProgressTracker progressTracker;

        // ==================================================================
        // Awake — 메인 진입점
        // ==================================================================

        private void Awake()
        {
            float startTime = Time.realtimeSinceStartup;

            var bb = GameBlackboard.Instance;
            if (bb == null)
            {
                if (!allowStubFallback)
                {
                    LogError($"GameBlackboard.Instance 부재. 빌드 코드는 GameBlackboard prefab 필수. " +
                             $"Scene hierarchy에 GameBlackboard GameObject 추가 요망.");
                    return;
                }
                LogWarn("GameBlackboard 없음 → Stub fallback (단독 테스트 모드)");
            }
            else
            {
                LogVerbose($"GameBlackboard OK (ActiveTerrainTags 수={bb.ActiveTerrainTags?.Count ?? 0})");
            }

            progressTracker = FindAnyObjectByType<Week2ProgressTracker>();
            if (progressTracker == null)
                LogVerbose("Week2ProgressTracker 미배치 - 보고 스킵 (T1.6 이후 자동 연결)");

            var brains = FindObjectsByType<HumanoidAIBrain>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID);

            if (brains.Length == 0)
            {
                LogWarn("HumanoidAIBrain이 씬에 0개. 테스트용 prefab 배치 확인.");
                return;
            }

            LogInfo($"{brains.Length}개 HumanoidAIBrain 부트스트랩 시작 " +
                    $"(debugLog 자동 켬={enableDebugLogOnBootstrap})");

            foreach (var brain in brains)
            {
                try
                {
                    BootstrapOne(brain, bb);
                    bootstrappedCount++;
                }
                catch (System.Exception e)
                {
                    lastFailureLog = $"{brain.name}: {e.Message}";
                    LogError($"{brain.name} 부트스트랩 실패: {e.Message}\n{e.StackTrace}");
                    progressTracker?.ReportEvent("BootstrapFailed", false, lastFailureLog);
                }
            }

            bootstrapDurationMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            LogInfo($"완료. {bootstrappedCount}/{brains.Length} 성공. 소요 {bootstrapDurationMs:F1}ms");

            progressTracker?.ReportEvent("BootstrapComplete", bootstrappedCount == brains.Length,
                $"{bootstrappedCount}/{brains.Length} in {bootstrapDurationMs:F0}ms");

            if (destroyAfterBootstrap)
            {
                LogVerbose("destroyAfterBootstrap=true → GameObject 제거");
                Destroy(gameObject);
            }
        }

        // ==================================================================
        // BootstrapOne — 각 NPC에 대한 4단계 주입
        // ==================================================================

        private void BootstrapOne(HumanoidAIBrain brain, GameBlackboard bb)
        {
            var go = brain.gameObject;
            LogVerbose($"{brain.name}: 부트스트랩 시작");

            // ═══ a) 컴포넌트 자동 부착 (없으면 추가) ═══════════════
            var formula = go.GetComponent<UtilityMasterFormula>()
                       ?? go.AddComponent<UtilityMasterFormula>();
            var resolver = go.GetComponent<PersonalityTagResolver>()
                        ?? go.AddComponent<PersonalityTagResolver>();
            var encoder = go.GetComponent<SituationVectorEncoder>()
                       ?? go.AddComponent<SituationVectorEncoder>();
            var trust = go.GetComponent<TrustMatrix>()
                     ?? go.AddComponent<TrustMatrix>();
            var trauma = go.GetComponent<TraumaSystem>()
                      ?? go.AddComponent<TraumaSystem>();

            LogVerbose($"{brain.name}: 5개 컴포넌트 부착 완료");

            // ═══ a-1) [v3] debugLog 일괄 설정 ═══════════════════════
            // enableDebugLogOnBootstrap=true 이면 4개 컴포넌트의 debugLog=true.
            // SituationVectorEncoder와 HumanoidAIBrain은 debugLog 필드 없음 → 대상 제외.
            if (enableDebugLogOnBootstrap)
            {
                formula.debugLog = true;
                resolver.debugLog = true;
                trust.debugLog = true;
                trauma.debugLog = true;
                LogVerbose($"{brain.name}: 4개 컴포넌트 debugLog 자동 ON");
            }

            // ═══ b) 데이터 SO 주입 (직접 메서드 호출, Reflection 없음) ═══
            if (defaultTagRules != null)
            {
                resolver.SetRules(defaultTagRules);
                LogVerbose($"{brain.name}: TagRules 주입 ({defaultTagRules.rules.Count}개 규칙)");
            }
            else
            {
                LogWarn($"{brain.name}: defaultTagRules 미지정 → Resolver는 하드코딩 fallback 사용");
            }

            if (defaultActionConfig != null)
            {
                formula.SetConfig(defaultActionConfig);
                LogVerbose($"{brain.name}: ActionConfig 주입 ({defaultActionConfig.actions.Count}개 행동)");
            }
            else
            {
                LogWarn($"{brain.name}: defaultActionConfig 미지정 → Formula는 하드코딩 fallback 사용");
            }

            // ═══ c) Brain 의존성 주입 ═══════════════════════════
            if (bb != null)
                brain.InjectBlackboard(bb);

            brain.InjectComponents(formula, resolver, encoder, trust, trauma);

            LogVerbose($"{brain.name}: Brain 의존성 주입 완료. IsStubFree={brain.IsStubFree()}");

            // ═══ d) 초기 태그 발현 ═══════════════════════════════
            resolver.ResolveTagsFromPersonality(brain.Personality);

            var activeTags = resolver.ActiveTags;
            var tagsString = activeTags.Count > 0 ? string.Join(",", activeTags) : "(없음)";
            LogInfo($"{brain.name}: 컴포넌트 5종 부착 + 초기 태그 [{tagsString}]");
        }

        // ==================================================================
        // 로그 레벨 기반 출력 헬퍼
        // ==================================================================

        private void LogInfo(string msg)
        {
            if (logLevel >= BootstrapLogLevel.Info) Debug.Log($"[Bootstrap] {msg}");
        }

        private void LogWarn(string msg)
        {
            if (logLevel >= BootstrapLogLevel.Warn) Debug.LogWarning($"[Bootstrap] {msg}");
        }

        private void LogError(string msg)
        {
            if (logLevel >= BootstrapLogLevel.Error) Debug.LogError($"[Bootstrap] {msg}");
        }

        private void LogVerbose(string msg)
        {
            if (logLevel >= BootstrapLogLevel.Verbose) Debug.Log($"[Bootstrap][V] {msg}");
        }

        // ==================================================================
        // [v3] DebugLog 일괄 제어 API + ContextMenu
        // ==================================================================

        /// <summary>
        /// 씬 내 모든 Humanoid NPC의 debugLog를 일괄 설정.
        /// Play 중 호출 가능. ContextMenu로도 접근.
        /// </summary>
        /// <param name="enable">true=켜기, false=끄기</param>
        /// <returns>영향받은 NPC 수</returns>
        public int SetAllDebugLog(bool enable)
        {
            var brains = FindObjectsByType<HumanoidAIBrain>(
                FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

            int count = 0;
            foreach (var brain in brains)
            {
                var go = brain.gameObject;
                var formula = go.GetComponent<UtilityMasterFormula>();
                var resolver = go.GetComponent<PersonalityTagResolver>();
                var trust = go.GetComponent<TrustMatrix>();
                var trauma = go.GetComponent<TraumaSystem>();

                if (formula != null) formula.debugLog = enable;
                if (resolver != null) resolver.debugLog = enable;
                if (trust != null) trust.debugLog = enable;
                if (trauma != null) trauma.debugLog = enable;
                count++;
            }

            Debug.Log($"[Bootstrap] DebugLog {(enable ? "ON" : "OFF")} — {count}개 NPC의 " +
                      $"Formula/Resolver/Trust/Trauma 적용.");
            return count;
        }

        /// <summary>[v3] 현재 debugLog 상태를 반전. 씬 첫 번째 Brain의 Formula.debugLog 기준.</summary>
        public void ToggleAllDebugLog()
        {
            var firstBrain = FindAnyObjectByType<HumanoidAIBrain>();
            if (firstBrain == null)
            {
                Debug.LogWarning("[Bootstrap] Toggle 대상 NPC 없음.");
                return;
            }
            var firstFormula = firstBrain.GetComponent<UtilityMasterFormula>();
            bool currentlyOn = firstFormula != null && firstFormula.debugLog;
            SetAllDebugLog(!currentlyOn);
        }

        [ContextMenu("Toggle All DebugLog")]
        private void DebugContextToggleAll() => ToggleAllDebugLog();

        [ContextMenu("Enable All DebugLog")]
        private void DebugContextEnableAll() => SetAllDebugLog(true);

        [ContextMenu("Disable All DebugLog")]
        private void DebugContextDisableAll() => SetAllDebugLog(false);

        // ==================================================================
        // 디버그 유틸리티
        // ==================================================================

        /// <summary>수동 재부트스트랩. Editor Context Menu에서 호출.</summary>
        [ContextMenu("Re-Bootstrap All NPCs")]
        public void ReBootstrapManual()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Bootstrap] Re-Bootstrap은 Play 모드에서만 동작");
                return;
            }
            bootstrappedCount = 0;
            Awake();
        }
    }
}
