// =============================================================================
// AICharacterManager.cs  |  TDA Project
// Layer  : L2 Router — AI 캐릭터 최상위 매니저
// 수정 이력:
//   P1 ⑩  aiCharacterExecutionManager 자동 등록 (AddComponent 폴백 포함)
//          characterExecutionManager 업캐스팅 등록
//          OnPoiseBreak() 신규 추가
//   P1 ⑭  isPoiseActive bool 추가 (강공격 중 포이즈 유지 플래그)
//   Fix    navMeshAgent 프로퍼티 복구 및 UnityEngine.AI 네임스페이스 추가
//   Fix    CS1061 에러 조치 (ResetPoiseRecoveryTimer 누락 우회)
//   Phase 5 (FSM 탈락, 설계 문서 M3):
//     - usePB4BehaviorTree 플래그 제거
//     - currentState / defaultState 필드 제거
//     - currentState.Tick(this) FSM 루프 제거
//     - Start()에서 defaultState 초기화 제거
//     - Update()는 base.Update() + NavMesh 보호만 수행
//     - MobAIBrain 자동 감지 코드 제거
//     - BehaviorGraphAgent(BT)가 모든 AI 의사결정을 담당
// =============================================================================
using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI; // 컴파일 에러 해결을 위해 추가

namespace TDA.Character.AI
{
    public class AICharacterManager : CharacterManager
    {
        // ─────────────────────────────────────────────────────────────────────
        // AI 전용 컴포넌트 참조
        // ─────────────────────────────────────────────────────────────────────
        [HideInInspector] public AICharacterCombatManager aiCharacterCombatManager;
        [HideInInspector] public AICharacterLocomotionManager aiCharacterLocomotionManager;
        [HideInInspector] public AICharacterAnimationManager aiCharacterAnimationManager;

        // AI 전용 도메인 매니저 — 부모(CharacterManager)의 Character* 필드에 업캐스팅 등록
        [HideInInspector] public AICharacterNetworkManager aiCharacterNetworkManager;
        [HideInInspector] public AICharacterSoundFxManager aiCharacterSoundFxManager;
        [HideInInspector] public AICharacterEffectsManager aiCharacterEffectsManager;
        [HideInInspector] public AICharacterInventoryManager aiCharacterInventoryManager;
        [HideInInspector] public AICharacterIKController aiCharacterIKController;

        // 컴파일 에러(CS1061) 해결을 위해 복구된 NavMeshAgent 참조
        public NavMeshAgent navMeshAgent;

        // ── ⑩ aiCharacterExecutionManager (신규 등록) ──────────────────────────
        [HideInInspector] public AICharacterExecutionManager aiCharacterExecutionManager;
        // ─────────────────────────────────────────────────────────────────────

        // ── GroggyState I-08 버그 수정 ──────────────────────────────────────
        // GroggyState의 런타임 상태를 AICharacterManager 인스턴스에 저장하여
        // SO 공유 오염을 방지한다. (여러 AI가 같은 GroggyState SO를 공유해도
        // 각자의 groggyTimer/hasEnteredGroggy가 독립적으로 동작)
        [HideInInspector] public bool hasEnteredGroggy = false;
        [HideInInspector] public float groggyTimer = 0f;
        // ─────────────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────────────
        // [Phase 5 FSM 탈락] 제거된 필드 목록:
        //   public AIState currentState    — BT 가 상태를 직접 관리하므로 불필요
        //   public AIState defaultState    — BT On Start(Repeat) 가 시작점 담당
        //   public bool usePB4BehaviorTree — BT 전용으로 확정, 플래그 불필요
        //
        // 설계 문서 M3:
        //   "usePB4BehaviorTree 분기 삭제. currentState.Tick() 삭제.
        //    currentState/defaultState 필드 제거.
        //    Update()는 base.Update() + BT 에이전트가 자동 실행하므로 별도 Tick 불필요."
        // ─────────────────────────────────────────────────────────────────────

        // ── ⑭ 포이즈 유지 플래그 ─────────────────────────────────────────────
        // [중복 제거] isPoiseActive 는 CharacterManager(부모)에 이미 선언되어 있습니다.
        // 자식 클래스에서 재선언하면 Unity 직렬화 에러가 발생합니다.
        // AttackState / GroggyState 에서 aiCharacter.isPoiseActive 로 그대로 접근합니다.
        // ─────────────────────────────────────────────────────────────────────


        // =====================================================================
        // Awake
        // =====================================================================
        protected override void Awake()
        {
            base.Awake();

            aiCharacterCombatManager = GetComponent<AICharacterCombatManager>();
            aiCharacterLocomotionManager = GetComponent<AICharacterLocomotionManager>();
            aiCharacterAnimationManager = GetComponent<AICharacterAnimationManager>();

            // characterAnimationManager(부모 필드)에 업캐스팅 등록
            characterAnimationManager = aiCharacterAnimationManager;

            // AI 전용 도메인 매니저 캐싱 + 부모 필드 업캐스팅
            aiCharacterNetworkManager = GetComponent<AICharacterNetworkManager>();
            aiCharacterSoundFxManager = GetComponent<AICharacterSoundFxManager>();
            aiCharacterEffectsManager = GetComponent<AICharacterEffectsManager>();
            aiCharacterInventoryManager = GetComponent<AICharacterInventoryManager>();
            aiCharacterIKController = GetComponent<AICharacterIKController>();

            // 부모(CharacterManager) 필드에 업캐스팅 등록 — 공통 로직이 AI 전용 구현을 호출
            if (aiCharacterNetworkManager != null) characterNetworkManager = aiCharacterNetworkManager;
            if (aiCharacterSoundFxManager != null) characterSoundFxManager = aiCharacterSoundFxManager;
            if (aiCharacterEffectsManager != null) characterEffectsManager = aiCharacterEffectsManager;
            if (aiCharacterInventoryManager != null) characterInventoryManager = aiCharacterInventoryManager;
            if (aiCharacterIKController != null) characterIKController = aiCharacterIKController;

            // NavMeshAgent 컴포넌트 초기화
            navMeshAgent = GetComponent<NavMeshAgent>();

            // ⑩ aiCharacterExecutionManager 등록 ----------------------------------------
            aiCharacterExecutionManager = GetComponent<AICharacterExecutionManager>();
            if (aiCharacterExecutionManager == null)
                aiCharacterExecutionManager = gameObject.AddComponent<AICharacterExecutionManager>();

            // CharacterManager.characterExecutionManager 에 업캐스팅 등록
            characterExecutionManager = aiCharacterExecutionManager;
            // ------------------------------------------------------------------

            // [Phase 5 FSM 탈락]
            // 기존: MobAIBrain/PB4DecisionAdapter 감지 후 usePB4BehaviorTree = true
            // 제거: 플래그 자체가 없어짐. BehaviorGraphAgent 가 항상 AI 의사결정 담당.
        }

        // =====================================================================
        // Start
        // =====================================================================
        protected override void Start()
        {
            base.Start();

            // [Phase 5 FSM 탈락]
            // 기존: if (defaultState != null) currentState = defaultState;
            // 제거: BT On Start(Repeat=true) 가 트리 진입점을 담당하므로 불필요.
        }

        // =====================================================================
        // Update
        // =====================================================================
        // [버그 수정] private void Update() → public override void Update()
        // 기존 private 선언은 CharacterManager.Update() 를 'new' 로 숨겨
        // 다형성이 깨지고 base.Update() 가 이중 호출되는 문제가 있었습니다.
        //
        // [Phase 5 FSM 탈락]
        // 기존: currentState.Tick(this) — FSM 루프
        // 제거: BT Tick 은 BehaviorGraphAgent.Update() 가 담당.
        //        이 Update() 는 NavMesh 인프라 보호만 수행합니다.
        //
        // [NavMesh 인프라 보호]
        // RootMotion 아키텍처:
        //   NavMeshAgent.updatePosition = false 강제
        //   → OnAnimatorMove() 가 CharacterController.Move(deltaPosition) 으로 이동
        //   → AICharacterLocomotionManager 가 nextPosition 동기화
        // =====================================================================
        public override void Update()
        {
            base.Update();

            if (!IsServer) return;

            // [Phase 5 FSM 탈락] currentState.Tick() 제거
            // BehaviorGraphAgent.Update() → BT Tick() → Action 노드들이 NavMesh 제어

            // ── NavMesh 인프라 보호 (RootMotion 아키텍처) ──
            // I-03: updatePosition=false 강제 — RootMotion 이 위치를 갱신하므로
            // NavMeshAgent 의 자동 위치 갱신을 비활성화합니다.
            // PB4DecisionAdapter.ProtectNavMeshInfra() 와 중복이지만,
            // BT Action 이 NavMesh 를 직접 제어할 때도 보호가 필요합니다.
            if (navMeshAgent != null && navMeshAgent.updatePosition)
                navMeshAgent.updatePosition = false;
        }

        // =====================================================================
        // ⑩ OnPoiseBreak — 포이즈 파괴 이벤트 수신
        //
        //  TakeDamageEffect.PlayDirectionalBasedDamagedAnimation() 에서
        //  poiseIsBroken = true 가 확정된 직후 호출됩니다.
        //
        //  역할:
        //   - 포이즈 회복 타이머 강제 리셋
        //   - GroggyState 전환은 GroggyState 자체가 isPerformingAction/타이머로 관리.
        //     (이 메서드에서 직접 조작하지 않습니다 — 계층 아키텍처 준수)
        //
        //  IsServer 체크 필수 (NGO 서버 권위형).
        // =====================================================================
        public void OnPoiseBreak()
        {
            if (!IsServer) return;

            // [P1-1 주석 해제] CharacterStatsManager.ResetPoiseRecoveryTimer() 구현 완료 후 활성화
            characterStatsManager?.ResetPoiseRecoveryTimer();

            // [4계층 아키텍처 준수] L2 Router 는 이벤트 신호만 허공에 던집니다.
            // AICharacterSoundFxManager / AICharacterEffectsManager 가
            // OnAnimationEventReceived(Groggy_Enter) 를 수신하여 자율적으로 반응합니다.
            characterEventManager?.NotifyAnimationEvent(
                AnimationEventType.Groggy_Enter, "OnPoiseBreak");

            DebugLog("[OnPoiseBreak] 포이즈 파괴 → Groggy_Enter 이벤트 발송");
        }

        // =====================================================================
        // 유틸
        // =====================================================================
        private void DebugLog(string msg)
        {
#if UNITY_EDITOR
            Debug.Log($"<color=yellow>[AICharacterManager:{name}]</color> {msg}");
#endif
        }
    }
}