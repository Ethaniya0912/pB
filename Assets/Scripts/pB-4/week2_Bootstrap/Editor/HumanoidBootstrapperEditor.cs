// =============================================================================
// HumanoidBootstrapper.cs  |  pB-4 Week 2 — Day 1 T1.4
//                          |  v2 개정: NGO 2.0 + 4계층 L2 Router 대응
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
        // [NGO-2] 라이프사이클 — Awake 제거, OnNetworkSpawn으로 이관
        // ═════════════════════════════════════════════════════════════════════

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

            RunBootstrap();
        }

        public override void OnNetworkDespawn()
        {
            // [NGO-4] 이벤트 구독 해제 (씬 전환 유령 객체 방지 — 개론서 §1.17 원칙 ③)
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientLateConnect;
            }
            base.OnNetworkDespawn();
        }

        // [NGO-4] 새 클라이언트 접속 시 기존 NPC는 이미 부트스트랩됨.
        //          서버가 추후 NetworkObject.Spawn(npcPrefab)으로 스폰한 Brain만 추적.
        private void OnClientLateConnect(ulong clientId)
        {
            if (!IsServer) return;
            RescanAndBootstrapNew();
        }

        /// <summary>씬을 재스캔하여 부트스트랩 안 된 새 Brain만 처리.</summary>
        private void RescanAndBootstrapNew()
        {
            var brains = FindObjectsByType<HumanoidAIBrain>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID);

            var bb = GameBlackboard.Instance;
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
            var bb = GameBlackboard.Instance;
            if (bb == null)
            {
                if (!allowStubFallback)
                {
                    LogError($"GameBlackboard.Instance 부재. 빌드 코드는 GameBlackboard prefab 필수. " +
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
            if (bb != null)
                brain.InjectBlackboard(bb);
            brain.InjectComponents(formula, resolver, encoder, trust, trauma);

            LogVerbose($"{brain.name}: Brain 의존성 주입 완료. IsStubFree={brain.IsStubFree()}");

            // ═══ d) 초기 태그 발현 ═══════════════════════════════
            // Personality 기반 태그를 계산하여 ActiveTags에 저장.
            // 이 시점에 호출하지 않으면 첫 UpdateDecision에서 빈 태그 리스트로 계산됨.
            // 서버에서만 실행 (ActiveTags는 NetworkList로 자동 동기화 — Resolver v2)
            resolver.ResolveTagsFromPersonality(brain.Personality);

            var activeTags = resolver.ActiveTags;
            var tagsString = activeTags.Count > 0 ? string.Join(",", activeTags) : "(없음)";
            LogInfo($"{brain.name}: 컴포넌트 5종 부착 + 초기 태그 [{tagsString}]");
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