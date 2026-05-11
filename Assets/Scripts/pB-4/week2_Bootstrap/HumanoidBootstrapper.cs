// =============================================================================
// HumanoidBootstrapper.cs  |  pB-4 Week 2 — Day 1 T1.4
//                          |  v2 개정: NGO 2.0 + 4계층 L2 Router 대응
//                          |  v3 개정: [DEBT-13] AlignmentSO 컴포넌트 전파 누락 패치
//                          |  ★ v4 개정 (Wk3 Phase 5): WorldAISpawnManager 이벤트 구독 추가
// -----------------------------------------------------------------------------
// 역할: 씬 내 모든 HumanoidAIBrain의 초기화를 담당하는 DI 컨테이너.
//       1) 필요 컴포넌트 자동 부착 (5종)
//       2) 데이터 SO 주입 (TagRules, ActionConfig, Alignment, DialogueLibrary)
//       3) 컴포넌트 간 참조 연결 (Brain ↔ Formula/Resolver/Encoder/Trust/Trauma)
//       4) 초기 태그 발현
//       5) ProgressTracker에 완료 보고
// 실행 시점: NetworkObject 스폰 완료 후 (OnNetworkSpawn). 서버만 실행.
//
// 계층: L2 Router (개론서 §1.17) — NetworkVariable 갱신 + Domain 배분.
//      IsServer 가드로 서버 권한 보장. 클라는 NPC 스폰 후 서버가 동기화한
//      NetworkVariable을 수신.
//
// v1 → v2 변경:
//   [NGO-1] MonoBehaviour → NetworkBehaviour 상속
//   [NGO-2] Awake() → OnNetworkSpawn() 이관 (NetworkObject 스폰 순서 보장)
//   [NGO-3] BootstrapOne은 IsServer 게이트 (치팅 방지)
//   [NGO-4] NetworkManager.OnClientConnectedCallback 구독 — Late-Spawn NPC 대응
//
// v2 → v3 변경:
//   [DEBT-13] BootstrapOne() b) 섹션에 alignment.SetAlignmentSO(defaultAlignmentSO)
//             호출 추가. 기존 코드는 NPCAlignmentController를 AddComponent만 하고
//             SO를 전파하지 않아 EvaluateAndTransition()이 매 1초 null 가드에 걸려
//             평가 스킵 → Alignment 전이 불가 → Speech trigger 미발사 (DEBT-12 동반).
//             부수효과: DEBT-12 자동 해결.
//             Refs: pB4_Week2_DEBT_Manifest_Table.docx (DEBT-13)
//                   Reports/DEBT_13_Bootstrapper_OneLine_Patch.md
//
// ★ v3 → v4 변경 (Wk3 Phase 5 — 2026-05-04):
//   [WK3-LATESPAWN] 기존 OnClientLateConnect (새 클라이언트 접속 시) 만으로는
//                   WorldAISpawnManager 가 호스트에서 NPC 스폰하는 케이스 미처리.
//                   해결: WorldAISpawnManager.OnAllCharactersSpawned 이벤트 구독 →
//                         스폰 완료 시 RescanAndBootstrapNew 자동 호출.
//                   증상: Skeleton 스폰됐으나 utilityFormula/tagResolver=null
//                         → HumanoidAIBrain 의 Week 1 fallback 매 틱 트리거
//                         → currentState=Unknown 빨간색 표시.
//                   해결 후: 11 개 컴포넌트 + SO 자동 부착 → Personality 5축 활성.
// =============================================================================
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;                        // [NGO-1]
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
    public enum BootstrapLogLevel { None = 0, Error = 1, Warn = 2, Info = 3, Verbose = 4 }

    [DefaultExecutionOrder(-100)]
    public class HumanoidBootstrapper : NetworkBehaviour     // [NGO-1] 변경
    {
        // ───────────── Inspector 필드 (v1과 동일) ─────────────
        [Header("━━━ Logging ━━━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("로그 레벨. 개발 중 Info, 디버깅 중 Verbose, 빌드 Warn.")]
        public BootstrapLogLevel logLevel = BootstrapLogLevel.Info;

        [Header("━━━ 공유 데이터 SO (Day 2에서 채움) ━━━")]

        [Tooltip("모든 Humanoid가 공유할 성격 태그 규칙. Day 2 T2.1에서 HumanoidTagRules.asset 드래그.")]
        public PersonalityTagRuleSO defaultTagRules;

        [Tooltip("행동 유틸리티 설정. Day 2 T2.2에서 HumanoidActionConfig.asset 드래그.")]
        public UtilityActionConfigSO defaultActionConfig;

        [Tooltip("4 진영 정의. Day 3 T3.2에서 DefaultAlignments.asset 드래그.")]
        public NPCAlignmentSO defaultAlignmentSO;

        [Tooltip("대사 라이브러리. Day 4 T4.6에서 입력.")]
        public DialogueLibrarySO defaultDialogueLibrary;

        [Tooltip("Speech Bubble prefab. Day 4 T4.6에서 Assets/Prefabs/UI/SpeechBubble 드래그.")]
        public GameObject speechBubblePrefab;

        [Header("━━━ 실행 조건 ━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("GameBlackboard 없어도 Bootstrap 계속할지. 단독 테스트 씬=true, 빌드=false.")]
        public bool allowStubFallback = true;

        [Tooltip("Play 중 이 컴포넌트를 비활성화해도 이미 부트스트랩된 NPC는 유지. false로 두세요.")]
        public bool destroyAfterBootstrap = false;

        // ★ v4 (Wk3 Phase 5) — Personality 무작위 적용
        [Header("━━━ ★ Phase 5 — 개체 다양화 ━━━━━━━━━")]

        [Tooltip("ON → 부트스트랩 시 각 NPC 의 Personality 5축을 무작위로 재설정. " +
                 "같은 prefab 5 마리도 다른 행동 시각 검증 가능. " +
                 "OFF → prefab 의 personality 값 그대로 사용 (5 마리 모두 동일).")]
        public bool randomizePersonality = false;

        [Header("━━━ 통계 (읽기 전용, 디버그용) ━━━━━━━")]

        [Tooltip("부트스트랩된 NPC 수.")]
        [SerializeField, HideInInspector] private int bootstrappedCount;

        [Tooltip("부트스트랩 소요 시간 (ms).")]
        [SerializeField, HideInInspector] private float bootstrapDurationMs;

        private IProgressTracker progressTracker;

        // [NGO-4] Late-Spawn NPC 추적용 — OnNetworkSpawn 시점에 씬에 없던 Brain은
        //         이후 서버에서 NetworkObject.Spawn() 호출되며 등장. 중복 부착 방지.
        private readonly HashSet<int> bootstrappedBrainIds = new();

        // ═════════════════════════════════════════════════════════════════════
        // [NGO-2] 라이프사이클 — Awake + OnNetworkSpawn 듀얼 (단독 Play 호환)
        // ═════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            // [단독 Play] NetworkManager 비활성 시: Awake에서 즉시 Bootstrap
            // NetworkManager 활성 시: OnNetworkSpawn에서 Bootstrap (서버만)
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                LogInfo("단독 Play 감지 (NetworkManager 비활성) — Awake에서 즉시 부트스트랩.");
                RunBootstrap();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // [NGO-3] 서버 권한 가드 — 클라이언트에서는 부트스트랩 실행 금지
            if (!IsServer)
            {
                LogVerbose("클라이언트: 부트스트랩 스킵 (NetworkVariable 동기화로 수신)");
                return;
            }

            // [NGO-4] Late-Spawn NPC 대응: 런타임 중 NetworkObject 스폰 시 자동 부착
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientLateConnect;
            }

            // ★ v4 (Wk3 Phase 5) — WorldAISpawnManager 의 NPC 스폰 사이클 종료 이벤트 구독.
            //   목적: OnClientLateConnect 와 별개 트리거 — "호스트의 NPC 스폰" 케이스 처리.
            //   v3 까지: WorldAISpawnManager 가 NPC 스폰해도 부트스트랩 안 됨 (Late-Spawn 누락 버그).
            //   v4 부터: OnAllCharactersSpawned → RescanAndBootstrapNew 자동 호출.
            WorldAISpawnManager.OnAllCharactersSpawned += OnNPCsSpawnedByWorldAISpawnManager;

            RunBootstrap();
        }

        public override void OnNetworkDespawn()
        {
            // [NGO-4] 이벤트 구독 해제 (씬 전환 유령 객체 방지 — 개론서 §1.17 원칙 ③)
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientLateConnect;
            }

            // ★ v4 (Wk3 Phase 5) — WorldAISpawnManager 이벤트 구독 해제 (씬 전환 유령 방지)
            WorldAISpawnManager.OnAllCharactersSpawned -= OnNPCsSpawnedByWorldAISpawnManager;

            base.OnNetworkDespawn();
        }

        // [NGO-4] 새 클라이언트 접속 시 기존 NPC는 이미 부트스트랩됨.
        //          서버가 추후 NetworkObject.Spawn(npcPrefab)으로 스폰한 Brain만 추적.
        private void OnClientLateConnect(ulong clientId)
        {
            if (!IsServer) return;
            RescanAndBootstrapNew();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ★ v4 (Wk3 Phase 5) — WorldAISpawnManager 이벤트 핸들러
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ v4 — WorldAISpawnManager 가 NPC 스폰 사이클 완료 시 트리거.
        /// 새로 스폰된 NPC 들에 11 개 컴포넌트 자동 부착 + SO 데이터 주입.
        ///
        /// ★ 호출 흐름:
        ///   WorldAISpawnManager.SpawnAllCharactersRoutine 끝부분
        ///   → OnAllCharactersSpawned?.Invoke(newlySpawned)
        ///   → 본 메서드 호출
        ///   → BootstrapNewlySpawnedAfterDelay 코루틴 시작 (1 프레임 대기)
        ///   → RescanAndBootstrapNew() — 미부트스트랩 Brain 만 처리 (중복 방지)
        ///
        /// ★ 보장: bootstrappedBrainIds HashSet 이 중복 처리 방지.
        ///        WorldAISpawnManager 가 같은 NPC 를 두 번 이벤트 발행해도 안전.
        /// </summary>
        private void OnNPCsSpawnedByWorldAISpawnManager(List<GameObject> newlySpawned)
        {
            if (!IsServer) return;

            LogInfo($"WorldAISpawnManager 스폰 이벤트 수신 — {newlySpawned?.Count ?? 0}개 NPC 부트스트랩 시작");
            StartCoroutine(BootstrapNewlySpawnedAfterDelay());
        }

        /// <summary>★ v4 — 1 프레임 대기 후 RescanAndBootstrapNew 호출.</summary>
        /// <remarks>
        /// WorldAISpawnManager 가 이미 POST_SPAWN_DELAY_SEC (0.1초) 대기 후 이벤트 발행하므로
        /// 보통 즉시 OK. 안전 마진으로 한 번 더 대기.
        /// </remarks>
        private System.Collections.IEnumerator BootstrapNewlySpawnedAfterDelay()
        {
            yield return null;  // 한 프레임 대기 (Awake / OnNetworkSpawn 처리 보장)
            RescanAndBootstrapNew();
        }

        /// <summary>씬을 재스캔하여 부트스트랩 안 된 새 Brain만 처리.</summary>
        private void RescanAndBootstrapNew()
        {
            var brains = FindObjectsByType<HumanoidAIBrain>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID);

            var bb = GameBlackboard.Instance ?? FindAnyObjectByType<GameBlackboard>();
            int newCount = 0;

            foreach (var brain in brains)
            {
                int id = brain.GetInstanceID();
                if (bootstrappedBrainIds.Contains(id)) continue;

                try
                {
                    BootstrapOne(brain, bb);
                    bootstrappedBrainIds.Add(id);
                    bootstrappedCount++;
                    newCount++;
                }
                catch (System.Exception e)
                {
                    LogError($"{brain.name} Late-Bootstrap 실패: {e.Message}");
                }
            }

            if (newCount > 0)
                LogInfo($"Late-Bootstrap: +{newCount} NPC (누적 {bootstrappedCount})");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 메인 부트스트랩 로직 (v1의 Awake 본문을 메서드로 분리)
        // ═════════════════════════════════════════════════════════════════════

        private void RunBootstrap()
        {
            float startTime = Time.realtimeSinceStartup;

            // 1. GameBlackboard 존재 확인
            //    [v3 수정] Instance 대신 FindAnyObjectByType 사용.
            //    이유: Bootstrapper(DefaultExecutionOrder -100)가 GameBlackboard.Awake보다 먼저 실행되면
            //          Instance가 아직 null. FindAnyObjectByType은 Awake 실행 여부 무관하게 씬 객체를 찾음.
            var bb = GameBlackboard.Instance;
            if (bb == null)
                bb = FindAnyObjectByType<GameBlackboard>();

            if (bb == null)
            {
                if (!allowStubFallback)
                {
                    LogError($"GameBlackboard 부재. 빌드 코드는 GameBlackboard prefab 필수. " +
                             $"Scene hierarchy 확인 후 GameBlackboard GameObject 추가 요망.");
                    return;
                }
                LogWarn("GameBlackboard 없음 → Stub fallback (단독 테스트 모드)");
            }
            else
            {
                LogVerbose($"GameBlackboard OK (ActiveTerrainTags 수={bb.ActiveTerrainTags?.Count ?? 0})");
            }

            // 2. ProgressTracker 참조 획득 (T1.6에서 구현체 생성 후 자동 참조)
            progressTracker = FindAnyObjectByType<Week2ProgressTracker>();
            if (progressTracker == null)
                LogVerbose("Week2ProgressTracker 미배치 - 보고 스킵 (T1.6 이후 자동 연결)");

            // 3. 씬 내 모든 HumanoidAIBrain 수집
            var brains = FindObjectsByType<HumanoidAIBrain>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID);

            if (brains.Length == 0)
            {
                LogWarn("HumanoidAIBrain이 씬에 0개. 테스트용 prefab 배치 확인.");
                return;
            }

            LogInfo($"{brains.Length}개 HumanoidAIBrain 부트스트랩 시작 (서버 권한)");

            // 4. 각 Brain 부트스트랩
            foreach (var brain in brains)
            {
                try
                {
                    BootstrapOne(brain, bb);
                    bootstrappedBrainIds.Add(brain.GetInstanceID());  // [NGO-4]
                    bootstrappedCount++;
                }
                catch (System.Exception e)
                {
                    LogError($"{brain.name} 부트스트랩 실패: {e.Message}\n{e.StackTrace}");
                    progressTracker?.ReportEvent("BootstrapFailed", false, $"{brain.name}: {e.Message}");
                }
            }

            // 5. 완료 처리
            bootstrapDurationMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            LogInfo($"완료. {bootstrappedCount}/{brains.Length} 성공. 소요 {bootstrapDurationMs:F1}ms");

            progressTracker?.ReportEvent("BootstrapComplete", true,
                $"{bootstrappedCount}/{brains.Length} in {bootstrapDurationMs:F0}ms");

            // 6. 정리 옵션
            if (destroyAfterBootstrap)
            {
                LogVerbose("destroyAfterBootstrap=true → GameObject 제거");
                Destroy(gameObject);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // BootstrapOne — v1과 동일 (NGO 변경 없음. Brain의 InjectComponents만 호출)
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>단일 HumanoidAIBrain에 대한 전체 부트스트랩.</summary>
        /// <remarks>
        /// 실행 순서가 중요. 다음 순서 엄수:
        ///   a) 컴포넌트 부착 → b) SO 주입 → c) Brain에 참조 주입 → d) 초기 태그 발현.
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

            // TrustMatrix, TraumaSystem도 Day 1에서 부착 (매 틱 갱신은 Day 2 T2.4).
            var trust = go.GetComponent<TrustMatrix>()
                     ?? go.AddComponent<TrustMatrix>();
            var trauma = go.GetComponent<TraumaSystem>()
                      ?? go.AddComponent<TraumaSystem>();

            // [v3 Day 3] NGO 2.0 신규 컴포넌트 자동 부착
            //   NPCAlignmentController: 4 진영 동적 전이 (매 1초 + 이벤트)
            //   DilemmaPivotResolver: Anchor 재설정 + Alignment 강제
            //   CommandAcceptanceFilter: 플레이어 명령 수락 판정
            var alignment = go.GetComponent<NPCAlignmentController>()
                         ?? go.AddComponent<NPCAlignmentController>();
            var pivotResolver = go.GetComponent<DilemmaPivotResolver>()
                             ?? go.AddComponent<DilemmaPivotResolver>();
            var cmdFilter = go.GetComponent<CommandAcceptanceFilter>()
                         ?? go.AddComponent<CommandAcceptanceFilter>();

            // ═══ Day 4 T4.1: Speech 파이프라인 (3 컴포넌트) ═══
            //   SpeechAssembler (L3): Template 매칭 + 플레이스홀더 치환
            //   DialogueRenderer (L4): 말풍선 UI 표시
            //   SpeechDispatcher  (L2): EventBus 구독 + 검문 → 위 두 개 호출
            var speechAssembler = go.GetComponent<TDA.PB4.AI.Speech.SpeechAssembler>()
                               ?? go.AddComponent<TDA.PB4.AI.Speech.SpeechAssembler>();
            var dialogueRenderer = go.GetComponent<TDA.PB4.AI.Speech.DialogueRenderer>()
                                ?? go.AddComponent<TDA.PB4.AI.Speech.DialogueRenderer>();
            var speechDispatcher = go.GetComponent<TDA.PB4.AI.Speech.SpeechDispatcher>()
                                ?? go.AddComponent<TDA.PB4.AI.Speech.SpeechDispatcher>();

            LogVerbose($"{brain.name}: 5개 + Day 3 3개 + Day 4 3개 = 11개 컴포넌트 부착 완료");

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

            // ═══ [DEBT-13 패치] AlignmentSO 컴포넌트 전파 (Day 5 r2 누락분) ═══
            //   기존 v2: NPCAlignmentController를 AddComponent만 하고 SO는 미전파.
            //   결과: EvaluateAndTransition()이 매 1초 alignmentDefinitionSO null 가드에
            //         걸려 평가 스킵 → Hostile/Friendly/Companion 전이 불가
            //         → Speech trigger 미발사 (DEBT-12 동반).
            //   수정: defaultTagRules / defaultActionConfig와 동일한 패턴으로
            //         alignment.SetAlignmentSO(defaultAlignmentSO) 호출.
            //         SetAlignmentSO 내부에 null + IsComplete 검증 포함.
            //   부수효과: DEBT-12 자동 해결, T5.2 v5.x 워치독 우회 코드 제거 가능.
            if (defaultAlignmentSO != null)
            {
                alignment.SetAlignmentSO(defaultAlignmentSO);
                LogVerbose($"{brain.name}: AlignmentSO 주입 (4 진영)");
            }
            else
            {
                LogWarn($"{brain.name}: defaultAlignmentSO 미지정 → Alignment 평가 스킵 (Hostile/Friendly/Companion 전이 불가)");
            }

            // ═══ c) Brain 의존성 주입 ═══════════════════════════
            if (bb != null)
                brain.InjectBlackboard(bb);

            // ★ v4 (Wk3 Phase 5) — Personality 무작위 적용 (개체 다양화)
            //   ON 일 때 각 NPC 의 Personality 5축을 무작위로 재설정 → 같은 prefab 도 다른 행동
            if (randomizePersonality)
            {
                var randomPersonality = PersonalityMatrix.Random();
                brain.SetPersonality(randomPersonality);
                LogInfo($"{brain.name}: Personality 무작위 적용 — " +
                        $"stability={randomPersonality.stability:F2}, " +
                        $"openness={randomPersonality.openness:F2}, " +
                        $"agreeable={randomPersonality.agreeable:F2}, " +
                        $"control={randomPersonality.control:F2}, " +
                        $"directness={randomPersonality.directness:F2}");
            }

            brain.InjectComponents(formula, resolver, encoder, trust, trauma);

            LogVerbose($"{brain.name}: Brain 의존성 주입 완료. IsStubFree={brain.IsStubFree()}");

            // ═══ d) 초기 태그 발현 ═══════════════════════════════
            // Personality 기반 태그를 계산하여 ActiveTags에 저장.
            // 이 시점에 호출하지 않으면 첫 UpdateDecision에서 빈 태그 리스트로 계산됨.
            // 서버에서만 실행 (ActiveTags는 NetworkList로 자동 동기화 — Resolver v2)
            resolver.ResolveTagsFromPersonality(brain.Personality);

            var activeTags = resolver.ActiveTags;
            var tagsString = activeTags.Count > 0 ? string.Join(",", activeTags) : "(없음)";
            LogInfo($"{brain.name}: 컴포넌트 8종 부착 (Day 1-2: 5종 + Day 3: 3종) + 초기 태그 [{tagsString}]");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 로깅 헬퍼 (v1과 동일)
        // ═════════════════════════════════════════════════════════════════════
        private void LogInfo(string msg) { if (logLevel >= BootstrapLogLevel.Info) Debug.Log($"[Bootstrap] {msg}"); }
        private void LogWarn(string msg) { if (logLevel >= BootstrapLogLevel.Warn) Debug.LogWarning($"[Bootstrap] {msg}"); }
        private void LogError(string msg) { if (logLevel >= BootstrapLogLevel.Error) Debug.LogError($"[Bootstrap] {msg}"); }
        private void LogVerbose(string msg) { if (logLevel >= BootstrapLogLevel.Verbose) Debug.Log($"[Bootstrap][V] {msg}"); }

        // ═════════════════════════════════════════════════════════════════════
        // [v3 복원] debugLog 일괄 제어 (Editor 스크립트 + ContextMenu 호출)
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 씬 내 모든 Humanoid NPC의 debugLog를 일괄 설정.
        /// Play 중 호출 가능. ContextMenu 및 HumanoidBootstrapperEditor에서 접근.
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

        /// <summary>현재 debugLog 상태를 반전. 씬 첫 번째 Brain의 Formula.debugLog 기준.</summary>
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
    }
}