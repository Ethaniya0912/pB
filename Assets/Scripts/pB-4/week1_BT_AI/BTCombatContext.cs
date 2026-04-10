// =============================================================================
// BTCombatContext.cs  |  pB-4 전투 런타임 상태 컴포넌트
// 계층    : L3 Domain — AI 런타임 데이터
// 네임스페이스: TDA.PB4.AI
//
// 역할:
//   전투 중 발생하는 런타임 인스턴스 데이터를 관리합니다.
//   periAngle(배회 각도), strafeTimer(배회 경과 시간), combatPhase(전투 단계)는
//   SO에 저장하면 모든 AI가 같은 값을 공유하는 오염이 발생합니다.
//   이 컴포넌트는 AI 개체별 인스턴스 데이터를 분리합니다.
//
// 사용:
//   CircleStrafeAction이 OnUpdate()에서 직접 참조하여 갱신합니다.
//   BT 에디터 Blackboard에도 동기화하여 에디터에서 모니터링 가능합니다.
//
// Inspector 표시:
//   currentPhase, periAngle, strafeTimer가 읽기 전용으로 표시됩니다.
//   Play 중 각 AI 개체의 전투 상태를 Inspector에서 확인할 수 있습니다.
// =============================================================================
using UnityEngine;

namespace TDA.PB4.AI
{
    /// <summary>
    /// Attack 3단계 전투의 런타임 인스턴스 데이터.
    /// AI 프리팹에 부착합니다. MobAIBrain.Awake()에서 자동 감지됩니다.
    /// </summary>
    public class BTCombatContext : MonoBehaviour
    {
        // =====================================================================
        // 전투 단계 열거형
        // =====================================================================

        /// <summary>
        /// Attack Sequence의 현재 단계.
        /// BT 에디터에서 Blackboard의 CombatPhase(int)와 동기화됩니다.
        /// </summary>
        public enum CombatPhase
        {
            /// <summary>전투 외 상태 (Patrol/Idle/Flee 등).</summary>
            None = 0,

            /// <summary>Stalk 단계: 타겟에게 접근 중.</summary>
            Stalk = 1,

            /// <summary>CircleStrafe 단계: 타겟 주위를 배회 중.</summary>
            CircleStrafe = 2,

            /// <summary>Strike 단계: 공격 모션 실행 중.</summary>
            Strike = 3,
        }

        // =====================================================================
        // 런타임 상태 (Inspector 읽기 전용)
        // =====================================================================

        [Header("━━━ 전투 런타임 상태 (읽기 전용) ━━━━━")]

        /// <summary>현재 전투 단계.</summary>
        [Tooltip("None=전투외, Stalk=접근, CircleStrafe=배회, Strike=공격")]
        [SerializeField] private CombatPhase currentPhase = CombatPhase.None;

        /// <summary>
        /// 배회 각도 (0~360+). CircleStrafeAction이 매 프레임 갱신합니다.
        /// 값이 계속 증가하면 AI가 타겟 주위를 빙빙 돌고 있는 것입니다.
        /// </summary>
        [Tooltip("현재 배회 궤도 각도. 계속 증가하면 정상 배회")]
        [SerializeField] private float periAngle;

        /// <summary>
        /// 배회 경과 시간 (초). CircleStrafeAction이 갱신합니다.
        /// strikeTriggerTime에 도달하면 Strike로 전환됩니다.
        /// </summary>
        [Tooltip("배회 경과 시간. triggerTime 도달 시 Strike 전환")]
        [SerializeField] private float strafeTimer;

        /// <summary>
        /// 전투 대상 참조. 전투 중인 타겟.
        /// </summary>
        [Tooltip("현재 전투 대상")]
        [SerializeField] private Transform combatTarget;

        // =====================================================================
        // Public API — Action 노드에서 호출
        // =====================================================================

        /// <summary>전투 단계를 설정합니다.</summary>
        public CombatPhase CurrentPhase
        {
            get => currentPhase;
            set => currentPhase = value;
        }

        /// <summary>배회 각도를 읽고/씁니다.</summary>
        public float PeriAngle
        {
            get => periAngle;
            set => periAngle = value;
        }

        /// <summary>배회 경과 시간을 읽고/씁니다.</summary>
        public float StrafeTimer
        {
            get => strafeTimer;
            set => strafeTimer = value;
        }

        /// <summary>전투 대상을 읽고/씁니다.</summary>
        public Transform CombatTarget
        {
            get => combatTarget;
            set => combatTarget = value;
        }

        /// <summary>
        /// 전투 상태를 초기화합니다.
        /// 전투 종료(Flee/Patrol 전환) 시 호출합니다.
        /// </summary>
        public void ResetCombatState()
        {
            currentPhase = CombatPhase.None;
            periAngle = 0f;
            strafeTimer = 0f;
            combatTarget = null;
        }

        /// <summary>
        /// Blackboard에 현재 상태를 동기화합니다.
        /// BT 에디터에서 모니터링할 수 있도록 매 프레임 호출됩니다.
        /// </summary>
        /// <param name="agent">BehaviorGraphAgent 참조</param>
        public void SyncToBlackboard(Unity.Behavior.BehaviorGraphAgent agent)
        {
            if (agent == null) return;
            // 에디터 모니터링용. 필수는 아니지만 디버깅에 유용.
            // agent.SetVariableValue("CombatPhase", (int)currentPhase);
            // agent.SetVariableValue("PeriAngle", periAngle);
            // agent.SetVariableValue("StrafeTimer", strafeTimer);
        }
    }
}
