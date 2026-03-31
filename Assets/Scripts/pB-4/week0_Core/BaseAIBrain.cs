// =============================================================================
// BaseAIBrain.cs  |  pB-4 Project — Week 0
// Layer  : L3 Domain (AI 공통 부모)
// Owner  : Person A
//
// 역할:
//   MobAIBrain과 HumanoidAIBrain의 공통 부모 추상 클래스.
//   생득 욕구 4종, 블랙보드 참조, 유틸리티 스코어러 참조, 그룹AI 참조를 포함.
//   Week 0에서 추상 메서드 시그니처 동결.
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
        // 인터페이스 참조 (Week 1에서 DI 또는 Inspector 연결)
        // ==================================================================
        protected IBlackboard blackboard;
        protected IUtilityScorer utilityScorer;

        // 그룹 AI 참조 (MobAI와 HumanoidAI 모두 동일하게 사용)
        [Header("Group AI")]
        [Tooltip("소속 그룹 AI 매니저. Inspector에서 연결하거나 런타임에 할당.")]
        public MonoBehaviour groupMindRef; // IGroupMind 인터페이스 구현체를 Inspector에서 할당

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
                    case "NarrowPath":    terrainFearBonus += 0.1f; break;
                    case "SpookyCave":    terrainFearBonus += 0.15f; break;
                    case "DeathTrap":     terrainFearBonus += 0.25f; break;
                    case "DifficultEscape": terrainFearBonus += 0.05f; break;
                }
            }

            fear = Mathf.Clamp01(fear + terrainFearBonus);
        }

        /// <summary>
        /// Stub 블랙보드를 기본 연결. Week 1 이후 실제 구현체로 교체.
        /// </summary>
        protected virtual void Awake()
        {
            if (blackboard == null)
                blackboard = new TDA.PB4.Stubs.StubBlackboard();
            if (utilityScorer == null)
                utilityScorer = new TDA.PB4.Stubs.StubUtilityScorer();
        }
    }
}
