// =============================================================================
// MobAIBrain.cs  |  pB-4 Project — Week 1
// Layer  : L3 Domain (AI)
// Owner  : Person A
//
// 역할:
//   BaseAIBrain을 상속하여 몹(비인간형 적)의 의사결정 루프를 구현.
//   HumanoidAI와 달리 성격 5축/트라우마/신뢰도 없이,
//   팩션 파라미터(I/H/O/A)와 생득 욕구 4종만으로 행동을 결정한다.
//   수십 마리가 동시에 존재하므로 연산량을 최소화.
//
// 의사결정 수식:
//   u_attack = (1.0 - fear) × aggressionThreshold × factionAggression/10
//   u_flee   = fear × fleeThreshold
//   u_idle   = (1.0 - hunger) × (1.0 - fatigue) × 0.3
//   u_patrol = (1.0 - fear) × (1.0 - fatigue) × 0.2
//   Winner-takes-all → BT 노드 전환
//
// BT 구조:
//   Root → Selector[ATTACK, FLEE, PATROL, IDLE]
//   각 노드는 유틸리티 점수 최고인 것만 실행
//
// 아키텍처:
//   - L2 Router(AICharacterManager)가 UpdateDecision() 호출 시점을 결정
//   - 이 클래스는 순수 L3 Domain: 수치 계산과 BT 실행만 담당
//   - VFX/SFX 직접 호출 절대 금지 (4계층 규칙 준수)
//
// [버그 수정 - Bug 2]
//   기존: MobAIBrain.Update()가 0.2초마다 UpdateDecision()을 자체 호출하고
//         PB4DecisionAdapter.Update()도 0.5초마다 mobBrain.UpdateDecision()을
//         별도로 호출하여 이중 실행 발생.
//   문제: 두 타이머가 비동기적으로 currentState를 갱신하여
//         Adapter가 BB에 기록하는 시점과 Brain이 계산을 완료한 시점이 어긋남.
//         특히 PB4DecisionAdapter가 useBehaviorGraph=true 모드일 때
//         Brain의 자체 Update가 불필요하게 연산을 중복 수행함.
//   수정: externallyTicked 플래그 추가.
//         PB4DecisionAdapter가 Brain을 직접 틱할 때 이 플래그를 true로 설정하면
//         Brain의 자체 Update()가 스킵되어 이중 호출이 방지된다.
//         기존 단독 사용(Adapter 없이) 시에는 false 유지로 기존 동작 그대로.
// =============================================================================
using UnityEngine;
using TDA.PB4.AI;
using TDA.PB4.Data;
using TDA.PB4.Core;

namespace TDA.PB4.AI.Mob
{
    public enum MobBTState { Idle, Patrol, Attack, Flee }

    public class MobAIBrain : BaseAIBrain
    {
        // ==================================================================
        // 팩션 데이터 참조 (Inspector에서 할당)
        // ==================================================================
        [Header("Faction Data")]
        [Tooltip("소속 팩션의 I/H/O/A 파라미터")]
        [SerializeField] private MobFactionDataSO factionData;

        [Tooltip("현재 Tier 데이터 (유닛 구성/리더 설정)")]
        [SerializeField] private FactionTierSO tierData;

        // ==================================================================
        // 유틸리티 임계값 (SO 또는 Inspector 조정)
        // ==================================================================
        [Header("Utility Thresholds")]
        [Range(0f, 1f)] public float aggressionThreshold = 0.5f;
        [Range(0f, 1f)] public float fleeThreshold = 0.8f;

        // ==================================================================
        // 유틸리티 점수 (디버그 표시용 public)
        // ==================================================================
        [Header("Debug — Utility Scores (Read Only)")]
        [SerializeField] private float u_attack;
        [SerializeField] private float u_flee;
        [SerializeField] private float u_idle;
        [SerializeField] private float u_patrol;

        // ==================================================================
        // 현재 BT 상태
        // ==================================================================
        [Header("BT State")]
        [SerializeField] private MobBTState currentState = MobBTState.Idle;
        public MobBTState CurrentState => currentState;

        // 타겟 참조 (L2 Router가 할당)
        [HideInInspector] public Transform currentTarget;

        // Tick 간격 (매 프레임 대신 0.2초마다)
        private float decisionTimer = 0f;
        private const float DECISION_INTERVAL = 0.2f;

        // ==================================================================
        // [버그 수정 - Bug 2] 외부 틱 제어 플래그
        // ==================================================================
        /// <summary>
        /// [Bug 2 수정] PB4DecisionAdapter가 이 Brain을 직접 틱할 때 true로 설정.
        ///
        /// true일 때: MobAIBrain.Update()의 자체 UpdateDecision() 호출이 스킵됨.
        ///            PB4DecisionAdapter가 UpdateDecision() 호출 시점을 단독 관리.
        ///            → 이중 호출 방지, 타이머 충돌 제거.
        ///
        /// false일 때(기본값): 기존 동작 유지.
        ///            MobAIBrain이 DECISION_INTERVAL(0.2초)마다 자체 틱.
        ///            Adapter 없이 Brain을 단독으로 사용하는 경우에 해당.
        ///
        /// PB4DecisionAdapter.Awake() 또는 Start()에서
        ///   mobBrain.externallyTicked = true;
        /// 로 설정할 것.
        /// </summary>
        [HideInInspector] public bool externallyTicked = false;

        // ==================================================================
        // Lifecycle
        // ==================================================================
        protected override void Awake()
        {
            base.Awake(); // Stub 블랙보드/유틸리티 연결

            // 팩션 데이터에서 생득 욕구 초기값 설정
            if (factionData != null)
            {
                // 공격성이 높은 팩션은 기본 fear가 낮음
                fear = Mathf.Clamp01(1.0f - factionData.aggression / 10f);
                greed = Mathf.Clamp01(factionData.intelligence / 10f * 0.5f);
            }

            // Tier 데이터에서 생득 욕구 오버라이드 적용
            if (tierData != null)
            {
                if (tierData.fearOverride >= 0f) fear = tierData.fearOverride;
                if (tierData.greedOverride >= 0f) greed = tierData.greedOverride;
            }
        }

        private void Update()
        {
            // [Bug 2 수정] PB4DecisionAdapter가 외부에서 틱을 관리하는 경우
            // Brain의 자체 Update()를 스킵하여 UpdateDecision() 이중 호출을 방지한다.
            // externallyTicked = true이면 Adapter가 UpdateDecision() 호출 시점을 단독 관리.
            if (externallyTicked) return;

            decisionTimer += Time.deltaTime;
            if (decisionTimer >= DECISION_INTERVAL)
            {
                decisionTimer = 0f;
                UpdateFearFromTerrain(); // 부모 클래스의 지형 기반 공포 보정
                UpdateDecision();
            }
        }

        // ==================================================================
        // 유틸리티 계산 (매 틱)
        // ==================================================================
        public override void UpdateDecision()
        {
            float factionAggNorm = factionData != null ? factionData.aggression / 10f : 0.5f;

            // 유틸리티 점수 산출
            u_attack = (1.0f - fear) * aggressionThreshold * factionAggNorm;
            u_flee = fear * fleeThreshold;
            u_idle = (1.0f - hunger) * (1.0f - fatigue) * 0.3f;
            u_patrol = (1.0f - fear) * (1.0f - fatigue) * 0.2f;

            // 타겟이 없으면 공격 불가
            if (currentTarget == null) u_attack = 0f;

            // Winner-takes-all
            float maxU = Mathf.Max(u_attack, Mathf.Max(u_flee, Mathf.Max(u_idle, u_patrol)));

            MobBTState newState;
            if (maxU == u_attack) newState = MobBTState.Attack;
            else if (maxU == u_flee) newState = MobBTState.Flee;
            else if (maxU == u_patrol) newState = MobBTState.Patrol;
            else newState = MobBTState.Idle;

            if (newState != currentState)
            {
                Debug.Log(
                    $"[MobAIBrain][STATE] {name}: {currentState} → {newState}\n" +
                    $"  fear={fear:F3} u_attack={u_attack:F3} u_flee={u_flee:F3} " +
                    $"u_idle={u_idle:F3} u_patrol={u_patrol:F3}\n" +
                    $"  target={(currentTarget != null ? currentTarget.name : "null")}\n" +
                    $"  ★ Attack→Flee 반복 시 HandlePanicChain이 fear를 계속 올리는 중");
                currentState = newState;
                ExecuteBTNode(currentState.ToString());
            }
        }

        // ==================================================================
        // BT 노드 실행
        // ==================================================================
        public override void ExecuteBTNode(string goalId)
        {
            switch (goalId)
            {
                case "Attack":
                    ExecuteAttack();
                    break;
                case "Flee":
                    ExecuteFlee();
                    break;
                case "Patrol":
                    ExecutePatrol();
                    break;
                case "Idle":
                default:
                    ExecuteIdle();
                    break;
            }
        }

        private void ExecuteAttack()
        {
            // L3 Domain: 이동 방향 계산만 수행. 실제 애니메이션/전투는 L2가 처리
            if (currentTarget == null) return;
            // TODO: Week 3에서 GroupAIManager의 공격 토큰 시스템과 연동
            // 현재는 타겟 방향으로 이동하는 기초 로직만
        }

        private void ExecuteFlee()
        {
            // 타겟 반대 방향으로 이동
            if (currentTarget != null)
            {
                Vector3 fleeDir = (transform.position - currentTarget.position).normalized;
                // TODO: NavMesh 기반 이동으로 교체
            }

            // 팩션별 도망 행동 분기: GroupPolicy가 HandlePanicChain() 호출
            // SwarmGroupPolicy: 주변 동료에게 패닉 전파
            // DuelGroupPolicy: 도망하지 않고 끝까지 싸움 (fleeThreshold가 극히 높음)
            // PhalanxGroupPolicy: 도망 불가 (panicChainMultiplier=0)
        }

        private void ExecutePatrol()
        {
            // 랜덤 웨이포인트 또는 NodeData 기반 순찰
            // TODO: CaveNodeGraphBuilder.Instance.nodesData에서 인접 노드 탐색
        }

        private void ExecuteIdle()
        {
            // 대기 상태. 주변 감지만 수행
        }
    }
}