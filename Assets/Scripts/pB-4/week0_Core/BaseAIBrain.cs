// =============================================================================
// BaseAIBrain.cs  |  pB-4 Project — Week 0 → Week 2 Day 1 T1.3 → v3 NGO 2.0 대응
// Layer  : L3 Domain (AI 공통 부모)
// Owner  : Person A
//
// 역할:
//   MobAIBrain과 HumanoidAIBrain의 공통 부모 추상 클래스.
//   생득 욕구 4종, 블랙보드 참조, 유틸리티 스코어러 참조, 그룹AI 참조를 포함.
//
// [Week 2 Day 1 T1.3 수정 요약 — v2]
//   1) Awake에서 Stub 직접 생성 → null-coalesce 패턴 전환:
//        외부 주입 → GameBlackboard.Instance → Stub fallback (순위)
//   2) InjectBlackboard(GameBlackboard) public 메서드 추가
//   3) IsStubFree() public 메서드 추가
//   4) verboseLogging 필드 추가 (Inspector 토글)
//   5) utilityScorer 필드 유지 (Week 1 MobAIBrain 호환성)
//
// [v2 코드리뷰 개정]
//   - [B1] InjectBlackboard 주석의 "1순위" 표현을 "명시적 덮어쓰기"로 정정.
//   - [B4] InitializeBlackboard를 private → protected virtual 로 변경.
//   - [B5] GetBlackboardDebugInfo 반환 문자열 개선 (타입명 포함).
//   - [B7] utilityScorer 필드 주석 명확화 — MobAIBrain 호환성 명시.
//
// [v3 NGO 2.0 개정 — 2026-04-23]
//   - [NGO-1] MonoBehaviour → NetworkBehaviour 상속 (개론서 §1.17 L3 Domain)
//   - [NGO-2] Awake() → OnNetworkSpawn() 이관 (NetworkObject 스폰 순서 보장)
//   - [NGO-3] InjectBlackboard()는 서버 권한 (NetworkManager 활성 시)
//   - [NGO-4] 단독 Play 호환: NetworkManager 비활성 시 Awake 경로 유지 (회귀 0건)
//   - MobAIBrain 등 자식 클래스는 base.OnNetworkSpawn() 호출 필수.
// =============================================================================
using Unity.Netcode;                              // [NGO-1]
using UnityEngine;
using TDA.PB4.Interfaces.Core;
using TDA.PB4.Interfaces.Intelligence;

namespace TDA.PB4.AI
{
    /// <summary>
    /// 모든 AI 개체의 공통 부모. MobAIBrain과 HumanoidAIBrain이 상속.
    /// [NGO-1] NetworkBehaviour 상속. IsServer 가드로 서버 권한 의사결정.
    /// </summary>
    public abstract class BaseAIBrain : NetworkBehaviour     // [NGO-1]
    {
        // ==================================================================
        // 공통 생득 욕구 4종 (0.0 ~ 1.0)
        // ==================================================================
        [Header("Survival Drives (생득 욕구)")]
        [Range(0f, 1f)] public float fear = 0.3f;
        [Range(0f, 1f)] public float hunger = 0.2f;
        [Range(0f, 1f)] public float greed = 0.1f;
        [Range(0f, 1f)] public float fatigue = 0.1f;

        // ==================================================================
        // 인터페이스 참조 (Week 2 T1.3: null-coalesce 패턴 적용)
        // ==================================================================
        protected IBlackboard blackboard;

        /// <summary>
        /// 유틸리티 스코어러. MobAIBrain이 Week 1에서 StubUtilityScorer로 사용.
        /// </summary>
        /// <remarks>
        /// [B7] HumanoidAIBrain은 Week 2부터 UtilityMasterFormula 컴포넌트를 직접 사용하므로 미사용.
        /// 그러나 MobAIBrain(Week 1 완성본)이 내부에서 이 필드를 참조하므로 필드 자체는 유지.
        /// MobAIBrain은 자체 Awake에서 this.utilityScorer = new StubUtilityScorer() 로 초기화해야 함.
        /// 이 BaseAIBrain.Awake에서는 더 이상 생성하지 않음 (Humanoid에 불필요하므로).
        /// </remarks>
        protected IUtilityScorer utilityScorer;

        // 그룹 AI 참조 (MobAI와 HumanoidAI 모두 동일하게 사용)
        [Header("Group AI")]
        [Tooltip("소속 그룹 AI 매니저. Inspector에서 연결하거나 런타임에 할당. " +
                 "MonoBehaviour로 받되 자식 클래스가 IGroupMind로 캐스트하여 사용.")]
        public MonoBehaviour groupMindRef;

        // ==================================================================
        // [Week 2 Day 1 T1.3 추가] Debug 토글
        // ==================================================================
        [Header("Week 2 Debug")]
        [Tooltip("상세 로그 출력 여부. 개발 중 true, 빌드 시 false.")]
        [SerializeField] protected bool verboseLogging = false;

        // ==================================================================
        // 추상 메서드 — Week 0 동결
        // ==================================================================

        /// <summary>
        /// 매 틱마다 호출. 유틸리티 점수를 계산하고 최적 행동을 결정.
        /// [NGO-3] 자식 클래스의 Update()에서 IsServer 가드 후 호출.
        /// </summary>
        public abstract void UpdateDecision();

        /// <summary>
        /// 결정된 행동 목표(goalId)에 대한 BT 노드를 실행.
        /// </summary>
        public abstract void ExecuteBTNode(string goalId);

        // ==================================================================
        // 공통 유틸리티 헬퍼
        // ==================================================================

        /// <summary>
        /// 현재 위치의 지형 태그를 블랙보드에서 읽어 공포 수치에 반영.
        /// Narrow, Dark 등의 태그가 있으면 fear 증가.
        /// 
        /// [v3.5 D3 갱신 — 2026-04-29 R6 박제용 보너스 강화]
        /// ★ v3.4 → v3.5 변경:
        ///   Wk3 단순 태그 보너스 값을 R6(Winner=FleeAction 전이) 박제 가능한
        ///   수준으로 상향 조정. Stub 환경 fear 임계값 정합.
        ///     - Narrow:     0.10 → 0.30 (+0.20)
        ///     - Dense:      0.05 → 0.15 (+0.10)
        ///     - HighGround: -0.05 → -0.10 (안전감 강화)
        ///   계산: targetFear = 0.20 + (Narrow 0.30 + Dense 0.15) = 0.65
        ///         → fear 0.65는 일반적 Flee 임계값 (0.5+) 초과
        ///         → Winner=FleeAction 전이 박제 가능
        ///
        /// [v3.4 D3 갱신 — 2026-04-29]
        /// ★ Wk3 단순 5종 태그 case 추가 (비파괴적 변경, Wk0 동결 보존):
        ///     - Wk3 D3 시점 ContextAnalyzerStub이 게시하는 단순 태그 5종
        ///     - Wk5+ FuzzyTagConverter 복합 태그는 기존 누적 동작 그대로 보존
        ///     - Wk3 표준은 개론서 v9 §1.20 GameBlackboard 스키마 동결 시점에 도입
        /// ★ Wk3 태그는 매 틱 발현되므로 누적 방지 (목표값 동작),
        ///   Wk5+ 태그는 한 시점 발현 가정으로 기존 누적 동작 유지.
        /// </summary>
        protected virtual void UpdateFearFromTerrain()
        {
            if (blackboard == null) return;

            var tags = blackboard.GetActiveTerrainTags();
            if (tags == null) return;

            // ── Wk5+ 복합 태그 누적 (기존 동작 그대로) ─────────────
            float legacyBonus = 0f;
            // ── Wk3 단순 태그 목표값 (★ v3.4 신규) ─────────────────
            float wk3Bonus = 0f;
            bool hasWk3Tag = false;

            foreach (var tag in tags)
            {
                switch (tag)
                {
                    // Wk5+ 복합 태그 (FuzzyTagConverter, 기존 동결)
                    case "NarrowPath":      legacyBonus += 0.1f; break;
                    case "SpookyCave":      legacyBonus += 0.15f; break;
                    case "DeathTrap":       legacyBonus += 0.25f; break;
                    case "DifficultEscape": legacyBonus += 0.05f; break;
                    // ★ v3.5: Wk3 단순 태그 (ContextAnalyzerStub) — R6 박제용 강화
                    case "Narrow":          wk3Bonus += 0.30f; hasWk3Tag = true; break;
                    case "Dense":           wk3Bonus += 0.15f; hasWk3Tag = true; break;
                    case "HighGround":      wk3Bonus -= 0.10f; hasWk3Tag = true; break; // 고지대 = 안전감 강화
                    case "Open":            hasWk3Tag = true; break; // 중립
                    case "Sparse":          hasWk3Tag = true; break; // 중립
                }
            }

            // Wk5+ 누적 (기존 동작 보존)
            if (legacyBonus != 0f)
            {
                fear = Mathf.Clamp01(fear + legacyBonus);
            }

            // ★ v3.4: Wk3 태그는 목표값 동작 (매 틱 누적 방지)
            if (hasWk3Tag)
            {
                const float FEAR_BASE = 0.20f; // Inspector 기본값
                float targetFear = Mathf.Clamp01(FEAR_BASE + wk3Bonus);
                if (fear < targetFear)
                {
                    fear = targetFear;
                }
            }
        }

        // ==================================================================
        // [Week 2 Day 1 T1.3 수정 + v3 NGO-2] null-coalesce 패턴 Awake/OnNetworkSpawn
        // ==================================================================

        /// <summary>
        /// [NGO-4] 단독 Play 호환성. NetworkManager가 없거나 비활성이면 Awake에서 초기화.
        /// NetworkManager 활성 시 Awake는 스킵하고 OnNetworkSpawn에서 처리.
        /// </summary>
        /// <remarks>
        /// [B6] 자식 클래스가 override 시 반드시 base.Awake()를 첫 줄에서 호출해야 함.
        /// </remarks>
        protected virtual void Awake()
        {
            // [NGO-4] NetworkManager 비활성 → 기존 Day 1~2 동작 (Awake에서 초기화)
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                InitializeBlackboard();
            }
            // NetworkManager 활성 시: OnNetworkSpawn이 처리. Awake는 no-op.
        }

        /// <summary>
        /// [NGO-2] NetworkObject 스폰 완료 시 호출. 서버/호스트/클라이언트 모두.
        /// </summary>
        /// <remarks>
        /// 자식 클래스(HumanoidAIBrain, MobAIBrain)가 override 시 base.OnNetworkSpawn() 필수.
        /// Unity lifecycle: Awake → (if spawn) OnNetworkSpawn → Start → Update.
        /// 단독 Play 시에는 OnNetworkSpawn 호출 안 됨 → Awake에서 InitializeBlackboard 수행.
        /// </remarks>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            InitializeBlackboard();
        }

        /// <summary>
        /// 3단계 Blackboard 초기화 전략.
        /// </summary>
        /// <remarks>
        /// [B4] private → protected virtual 로 변경.
        /// 자식 클래스가 커스텀 Blackboard 소스를 사용하려면 override 가능.
        /// </remarks>
        protected virtual void InitializeBlackboard()
        {
            // 1순위: 이미 누군가 주입했으면 (예: 자식 클래스가 base 이전에 설정)
            if (blackboard != null)
            {
                if (verboseLogging)
                    Debug.Log($"[{GetType().Name}] {name}: Blackboard 이미 주입됨 ({blackboard.GetType().Name})");
                return;
            }

            // 2순위: GameBlackboard.Instance 있으면 Adapter로 감싸서 사용
            var gb = TDA.PB4.Core.GameBlackboard.Instance;
            if (gb != null)
            {
                blackboard = new TDA.PB4.AI.GameBlackboardAdapter(gb);
                if (verboseLogging)
                    Debug.Log($"[{GetType().Name}] {name}: GameBlackboard → Adapter 경로");
                return;
            }

            // 3순위: 둘 다 없으면 Stub fallback (단독 테스트 씬)
            blackboard = new TDA.PB4.Stubs.StubBlackboard();
            Debug.LogWarning($"[{GetType().Name}] {name}: GameBlackboard 부재 → Stub fallback. " +
                             $"실게임 씬에서 발생 시 GameBlackboard prefab 배치 확인 필요.");
        }

        // ==================================================================
        // [Week 2 Day 1 T1.3 추가 + v3 NGO-3] DI 주입 API
        // ==================================================================

        /// <summary>외부에서 Blackboard 주입. Bootstrapper.OnNetworkSpawn에서 호출.</summary>
        /// <remarks>
        /// [B1] Unity lifecycle 상 이 메서드는 Brain.Awake/OnNetworkSpawn 이후에 호출됨.
        ///      즉 blackboard 필드는 이미 2/3순위 경로로 설정된 상태.
        ///      이 메서드의 실제 의미는 "명시적 덮어쓰기".
        /// [NGO-3] NetworkManager 활성 시 서버만 호출 가능 (Bootstrapper가 서버 전용).
        ///         클라이언트에서 직접 호출 시 경고 후 무시.
        /// </remarks>
        public void InjectBlackboard(TDA.PB4.Core.GameBlackboard gb)
        {
            // [NGO-3] 네트워크 활성 + 클라이언트에서 호출 시 거부
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer)
            {
                Debug.LogWarning($"[{GetType().Name}] {name}: InjectBlackboard는 서버 전용. 클라이언트 호출 무시.");
                return;
            }

            if (gb == null)
            {
                Debug.LogError($"[{GetType().Name}] {name}: InjectBlackboard에 null 전달");
                return;
            }
            blackboard = new TDA.PB4.AI.GameBlackboardAdapter(gb);
            if (verboseLogging)
                Debug.Log($"[{GetType().Name}] {name}: Blackboard 외부 주입 완료 (덮어쓰기)");
        }

        /// <summary>현재 Brain이 Stub 없이 동작 중인지.</summary>
        /// <remarks>WK2_C22 체크리스트가 사용.</remarks>
        public bool IsStubFree()
        {
            return blackboard != null && !(blackboard is TDA.PB4.Stubs.StubBlackboard);
        }

        /// <summary>디버그용 현재 Blackboard 상태 덤프.</summary>
        /// <remarks>
        /// [B5] 타입명 + blackboard.ToString() 조합.
        /// Adapter/Stub이 ToString()을 override하면 실제 상태 정보 포함.
        /// </remarks>
        public string GetBlackboardDebugInfo()
        {
            if (blackboard == null) return "null";
            return $"{blackboard.GetType().Name}: {blackboard.ToString()}";
        }

        // ==================================================================
        // [v3 NGO 헬퍼] 서버 권한 체크 유틸리티
        // ==================================================================

        /// <summary>
        /// 현재 이 NetworkBehaviour가 의사결정 실행 권한이 있는지.
        /// - NetworkManager 비활성 (단독 Play) → true
        /// - NetworkManager 활성 + 서버/호스트 → true
        /// - NetworkManager 활성 + 클라이언트 → false
        /// </summary>
        protected bool HasAuthority()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                return true;   // 단독 Play 호환
            return IsServer;
        }
    }
}
