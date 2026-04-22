// =============================================================================
// HumanoidBootstrapper.cs  |  pB-4 Week 2 — Day 1 T1.4 (v2 개정)
// 역할: 씬 내 모든 HumanoidAIBrain의 초기화를 담당하는 DI 컨테이너.
//       1) 필요 컴포넌트 자동 부착 (5종)
//       2) 데이터 SO 주입 (TagRules, ActionConfig, Alignment, DialogueLibrary)
//       3) 컴포넌트 간 참조 연결 (Brain ↔ Formula/Resolver/Encoder/Trust/Trauma)
//       4) 초기 태그 발현
//       5) ProgressTracker에 완료 보고
// 실행 시점: Scene 로드 직후 (DefaultExecutionOrder(-100))
//
// [비판적 개선사항]
//   - Reflection 제거: resolver.GetType().GetMethod("SetRules") → 직접 호출
//   - TrustMatrix/TraumaSystem Day 1에서 부착 (매 틱 갱신은 Day 2 T2.4)
//   - LogLevel enum으로 로그 세밀 제어
//   - try-catch로 개별 NPC 실패 격리
//   - 부트스트랩 통계 자동 수집
//
// [v2 코드리뷰 개정]
//   - [BS1] InjectBlackboard 호출 타이밍 명시: 이미 Awake 완료한 Brain들의 blackboard를 덮어쓰기.
//   - [BS2] 통계 필드에 "Play only" 주석 추가 (HideInInspector 대안).
//   - [BS4] FindObjectsInactive.Exclude 동작 명시.
//   - [BS7] defaultDialogueLibrary / defaultAlignmentSO 주석에 Day별 연결 시점 명확화.
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
    /// <remarks>
    /// Scene에 하나만 배치. DefaultExecutionOrder(-100)으로 다른 MonoBehaviour보다 먼저 실행.
    /// 주입 순서: 컴포넌트 부착 → 데이터 SO 주입 → Brain 참조 주입 → 초기 태그 발현.
    ///
    /// [BS1] 주의: DefaultExecutionOrder(-100)은 "Bootstrapper의 Update가 다른 MonoBehaviour보다
    ///       먼저 호출" 을 보장할 뿐, 이미 씬에 존재하는 Brain들의 Awake는 이미 실행됨.
    ///       따라서 Bootstrapper.InjectBlackboard는 실제로 "덮어쓰기" 동작을 수행.
    ///       이것은 의도된 설계: 정합성 있는 Adapter 재생성 + verboseLogging 확인 용도.
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    public class HumanoidBootstrapper : MonoBehaviour
    {
        // ==================================================================
        // Inspector 필드
        // ==================================================================

        [Header("━━━ Logging ━━━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("로그 레벨. 개발 중 Info, 디버깅 중 Verbose, 빌드 Warn.")]
        public BootstrapLogLevel logLevel = BootstrapLogLevel.Info;

        [Header("━━━ 공유 데이터 SO (Day별 점진 연결) ━━")]

        [Tooltip("모든 Humanoid가 공유할 성격 태그 규칙. Day 2 T2.1에서 HumanoidTagRules.asset 드래그.")]
        public PersonalityTagRuleSO defaultTagRules;

        [Tooltip("행동 유틸리티 설정. Day 2 T2.2에서 HumanoidActionConfig.asset 드래그.")]
        public UtilityActionConfigSO defaultActionConfig;

        // [BS7] 아래 두 필드는 Day 1에 선언만 해두고 실사용은 Day 3/Day 4.
        //       Designer가 Bootstrap prefab에 한 번에 연결하면 이후 Day별 Task에서 개별 연결 불필요.
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

        // [BS2] 아래 3개 필드는 Play 모드에서만 의미 있음.
        //       Edit 모드에서는 직전 Play 세션의 마지막 값이 보일 수 있으므로 혼동 주의.
        //       HideInInspector는 사용하지 않음 — 디버그에 유용하므로 명시적 주석으로 처리.
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

            // 1. GameBlackboard 존재 확인
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

            // 2. ProgressTracker 참조 획득 (T1.6 이후 자동 연결)
            progressTracker = FindAnyObjectByType<Week2ProgressTracker>();
            if (progressTracker == null)
                LogVerbose("Week2ProgressTracker 미배치 - 보고 스킵 (T1.6 이후 자동 연결)");

            // 3. 씬 내 모든 HumanoidAIBrain 수집
            // [BS4] FindObjectsInactive.Exclude: 비활성 GameObject의 Brain은 제외.
            //       이후 SetActive(true)로 활성화된 Brain은 Bootstrap 대상 아님.
            //       런타임 활성화 Brain은 별도로 BootstrapOne(brain, bb) 직접 호출 또는 
            //       ReBootstrapManual로 전체 재부트스트랩 필요.
            var brains = FindObjectsByType<HumanoidAIBrain>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID);

            if (brains.Length == 0)
            {
                LogWarn("HumanoidAIBrain이 씬에 0개. 테스트용 prefab 배치 확인.");
                return;
            }

            LogInfo($"{brains.Length}개 HumanoidAIBrain 부트스트랩 시작");

            // 4. 각 Brain 부트스트랩 (개별 실패 격리)
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

            // 5. 완료 처리
            bootstrapDurationMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            LogInfo($"완료. {bootstrappedCount}/{brains.Length} 성공. 소요 {bootstrapDurationMs:F1}ms");

            progressTracker?.ReportEvent("BootstrapComplete", bootstrappedCount == brains.Length,
                $"{bootstrappedCount}/{brains.Length} in {bootstrapDurationMs:F0}ms");

            // 6. 정리 옵션
            if (destroyAfterBootstrap)
            {
                LogVerbose("destroyAfterBootstrap=true → GameObject 제거");
                Destroy(gameObject);
            }
        }

        // ==================================================================
        // BootstrapOne — 각 NPC에 대한 4단계 주입 (Day 1 핵심 로직)
        // ==================================================================

        /// <summary>단일 HumanoidAIBrain에 대한 전체 부트스트랩.</summary>
        /// <remarks>
        /// 실행 순서 엄수: a) 컴포넌트 부착 → b) SO 주입 → c) Brain 참조 → d) 초기 태그 발현.
        /// 이 순서가 바뀌면 null 참조 발생 가능.
        /// </remarks>
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

            // TrustMatrix, TraumaSystem도 Day 1에서 부착.
            // 이유: WK2_C11, WK2_C13 체크리스트가 이 컴포넌트 존재를 요구.
            // 매 틱 갱신 hook은 Day 2 T2.4에서 완성.
            var trust = go.GetComponent<TrustMatrix>()
                     ?? go.AddComponent<TrustMatrix>();
            var trauma = go.GetComponent<TraumaSystem>()
                      ?? go.AddComponent<TraumaSystem>();

            LogVerbose($"{brain.name}: 5개 컴포넌트 부착 완료");

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
            // [BS1] 이 시점에 brain.Awake()는 이미 완료됨.
            //       blackboard는 Brain의 Awake에서 GameBlackboard→Adapter로 이미 설정.
            //       InjectBlackboard 호출은 "명시적 덮어쓰기" (같은 GameBlackboard라면 무해).
            if (bb != null)
                brain.InjectBlackboard(bb);

            brain.InjectComponents(formula, resolver, encoder, trust, trauma);

            LogVerbose($"{brain.name}: Brain 의존성 주입 완료. IsStubFree={brain.IsStubFree()}");

            // ═══ d) 초기 태그 발현 ═══════════════════════════════
            // Personality 기반 태그를 계산하여 ActiveTags에 저장.
            // 이 시점에 호출하지 않으면 첫 UpdateDecision에서 빈 태그 리스트로 계산됨.
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
        // 디버그 유틸리티
        // ==================================================================

        /// <summary>수동 재부트스트랩. Editor Context Menu에서 호출.</summary>
        /// <remarks>
        /// [BS3] Awake()를 직접 호출 — Unity 관례는 아니지만 idempotent하므로 안전.
        /// BootstrapOne이 GetComponent+AddComponent 패턴이므로 중복 부착 없음.
        /// 런타임에 활성화된 신규 Brain도 이 메서드로 합쳐서 초기화 가능.
        /// </remarks>
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
