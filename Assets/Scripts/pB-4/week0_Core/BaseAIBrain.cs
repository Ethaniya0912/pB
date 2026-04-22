// =============================================================================
// BaseAIBrain.cs  |  pB-4 Project — Week 0 → Week 2 Day 1 T1.3 수정 (v2 개정)
// Layer  : L3 Domain (AI 공통 부모)
// Owner  : Person A
//
// 역할:
//   MobAIBrain과 HumanoidAIBrain의 공통 부모 추상 클래스.
//   생득 욕구 4종, 블랙보드 참조, 유틸리티 스코어러 참조, 그룹AI 참조를 포함.
//
// [Week 2 Day 1 T1.3 수정 요약]
//   1) Awake에서 Stub 직접 생성 → null-coalesce 패턴 전환:
//        외부 주입 → GameBlackboard.Instance → Stub fallback (순위)
//   2) InjectBlackboard(GameBlackboard) public 메서드 추가
//        → Bootstrapper가 Brain.Awake 이후에도 명시적 덮어쓰기 가능
//   3) IsStubFree() public 메서드 추가
//        → WK2_C22 체크리스트가 호출하여 Stub 사용 여부 판단
//   4) verboseLogging 필드 추가 (Inspector 토글)
//   5) utilityScorer 필드 유지 (Week 1 MobAIBrain 호환성)
//
// [v2 코드리뷰 개정]
//   - [B1] InjectBlackboard 주석의 "1순위" 표현을 "명시적 덮어쓰기"로 정정.
//          Unity lifecycle 상 Brain.Awake 완료 후에 호출되므로 실제로는 override 동작.
//   - [B4] InitializeBlackboard를 private → protected virtual 로 변경.
//          자식 클래스가 blackboard 초기화 전략을 커스터마이즈할 수 있도록.
//   - [B5] GetBlackboardDebugInfo 반환 문자열 개선 (타입명 포함).
//   - [B7] utilityScorer 필드 주석 명확화 — MobAIBrain 호환성 명시.
// =============================================================================
using UnityEngine;
using TDA.PB4.Interfaces.Core;
using TDA.PB4.Interfaces.Intelligence;

namespace TDA.PB4.AI
{
    /// <summary>
    /// 모든 AI 개체의 공통 부모. MobAIBrain과 HumanoidAIBrain이 상속.
    /// </summary>
    public abstract class BaseAIBrain : MonoBehaviour
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
        /// MobAI: 단순 fear/attack/flee 비교
        /// HumanoidAI: Master Formula 기반 복합 계산
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
        /// </summary>
        protected virtual void UpdateFearFromTerrain()
        {
            if (blackboard == null) return;

            var tags = blackboard.GetActiveTerrainTags();
            if (tags == null) return;

            float terrainFearBonus = 0f;
            foreach (var tag in tags)
            {
                switch (tag)
                {
                    case "NarrowPath": terrainFearBonus += 0.1f; break;
                    case "SpookyCave": terrainFearBonus += 0.15f; break;
                    case "DeathTrap": terrainFearBonus += 0.25f; break;
                    case "DifficultEscape": terrainFearBonus += 0.05f; break;
                }
            }

            fear = Mathf.Clamp01(fear + terrainFearBonus);
        }

        // ==================================================================
        // [Week 2 Day 1 T1.3 수정] null-coalesce 패턴 적용 Awake
        // ==================================================================

        /// <summary>
        /// null-coalesce 패턴: 외부 주입 → GameBlackboard.Instance → Stub fallback
        /// </summary>
        /// <remarks>
        /// [B6] 자식 클래스가 override 시 반드시 base.Awake()를 첫 줄에서 호출해야 함.
        /// 자식이 base.Awake를 누락하면 blackboard가 null로 유지되어 UpdateFearFromTerrain이 no-op.
        /// </remarks>
        protected virtual void Awake()
        {
            InitializeBlackboard();

            // utilityScorer는 자식 클래스(MobAIBrain)가 자체 Awake에서 할당.
            // BaseAIBrain에서는 더 이상 Stub 생성하지 않음 (Humanoid 경로에는 불필요).
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
            // 1순위: 이미 누군가 주입했으면 (예: 자식 클래스가 base.Awake 이전에 설정)
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
        // [Week 2 Day 1 T1.3 추가] DI 주입 API
        // ==================================================================

        /// <summary>외부에서 Blackboard 주입. Bootstrapper.Awake에서 호출.</summary>
        /// <remarks>
        /// [B1] Unity lifecycle 상 이 메서드는 Brain.Awake 이후에 호출됨.
        ///      즉 blackboard 필드는 이미 2/3순위 경로로 설정된 상태.
        ///      이 메서드의 실제 의미는 "명시적 덮어쓰기".
        ///      Bootstrapper가 Adapter 재생성을 통해 참조를 갱신하는 역할.
        ///      초기 2순위 경로(Awake)와 3순위 덮어쓰기(여기)는 같은 Adapter를 만들지만
        ///      Bootstrapper가 별도 GameBlackboard를 쓰고 싶을 때 필요.
        /// </remarks>
        public void InjectBlackboard(TDA.PB4.Core.GameBlackboard gb)
        {
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
    }
}
