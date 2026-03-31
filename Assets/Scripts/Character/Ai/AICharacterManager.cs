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
//
// 버그 수정 이력 (CodeReview v1.0):
//   [I-04 수정] Update()에 characterStatsManager.RegeneratePoise() 호출 추가
//              → 포이즈 파괴 후 점진 회복이 동작하지 않던 버그 수정
//   [I-08 수정] GroggyState SO 인스턴스 공유 타이머 오염 해소
//              groggyTimer / hasEnteredGroggy 를 AICharacterManager 인스턴스 필드로 이관
//              → 여러 AI가 동일 GroggyState SO를 공유해도 타이머가 독립적으로 동작
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

        // ── ⑩ aiCharacterExecutionManager (신규 등록) ─────────────────────────────────
        [HideInInspector] public AICharacterExecutionManager aiCharacterExecutionManager;
        // ─────────────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────────────
        // FSM
        // ─────────────────────────────────────────────────────────────────────
        [Header("AI State Machine")]
        [Tooltip("현재 실행 중인 AI State (런타임 표시용)")]
        public AIState currentState;

        [Tooltip("게임 시작 시 진입할 기본 State SO")]
        public AIState defaultState;

        // ─────────────────────────────────────────────────────────────────────
        // AI 이동·회전 플래그
        // ─────────────────────────────────────────────────────────────────────
        // [버그 수정] canMove / canRotate 는 CharacterManager 에 이미 선언되어 있습니다.
        // 여기서 재선언하면 'new' 키워드로 부모 필드를 숨겨 다형성이 깨집니다.
        // (AICharacterLocomotionManager 가 character.canMove 로 접근할 때
        //  CharacterManager.canMove 를 읽어야 하므로 중복 선언을 제거합니다.)
        //
        // 사용법 변경 없음 — 기존 코드에서 aiCharacter.canMove = false; 그대로 사용합니다.
        // [Header("AI Movement Flags")] 는 Inspector 표시용으로 CharacterManager 쪽 필드에 이미 있습니다.

        // ── ⑭ 포이즈 유지 플래그 ─────────────────────────────────────────────
        // [중복 제거] isPoiseActive 는 CharacterManager(부모)에 이미 선언되어 있습니다.
        // 자식 클래스에서 재선언하면 Unity 직렬화 에러가 발생합니다.
        // AttackState / GroggyState 에서 aiCharacter.isPoiseActive 로 그대로 접근합니다.
        // ─────────────────────────────────────────────────────────────────────

        // =====================================================================
        // [I-08 수정] GroggyState 타이머 — SO 인스턴스 공유 오염 해소
        //
        // 문제:
        //   GroggyState 는 ScriptableObject 이므로 여러 AI 가 동일한 SO 인스턴스를 공유합니다.
        //   기존에 SO 클래스 내부 private 필드(groggyTimer, hasEnteredGroggy)로 런타임 상태를
        //   보관하면, AI-A 가 그로기에 진입하면서 hasEnteredGroggy = true 로 바꾸면
        //   AI-B 가 GroggyState.Tick() 에 진입할 때 이미 true 여서 EnterGroggy() 를 건너뜁니다.
        //
        // 해결:
        //   런타임 상태(타이머 / 진입 여부)를 SO 대신 AICharacterManager 인스턴스 필드로 이관합니다.
        //   GroggyState.Tick() 에서 aiCharacter.groggyTimer / aiCharacter.hasEnteredGroggy 를
        //   직접 읽고 씁니다. 각 AI 오브젝트가 독립된 값을 유지합니다.
        //
        // [HideInInspector] — 인스펙터 노출 불필요(런타임 전용), 직렬화는 필요(MonoBehaviour)
        // =====================================================================
        [HideInInspector] public float groggyTimer = 0f;
        [HideInInspector] public bool hasEnteredGroggy = false;


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
            // (CharacterExecutionManager 가 aiCharacterExecutionManager / PlayerExecutionManager 의 공통 부모)
            characterExecutionManager = aiCharacterExecutionManager;
            // ------------------------------------------------------------------
        }

        // =====================================================================
        // Start
        // =====================================================================
        protected override void Start()
        {
            base.Start();

            if (defaultState != null)
                currentState = defaultState;
        }

        // =====================================================================
        // Update — FSM Tick (서버 전용)
        // =====================================================================
        // [버그 수정] private void Update() → public override void Update()
        // 기존 private 선언은 CharacterManager.Update() 를 'new' 로 숨겨
        // 다형성이 깨지고 base.Update() 가 이중 호출되는 문제가 있었습니다.
        public override void Update()
        {
            base.Update();
            if (!IsServer) return;
            if (currentState == null) return;

            // [I-04 수정] 포이즈 점진 회복 — 메서드가 구현되어 있었으나 호출이 누락된 버그 수정
            // RegenerateStamina() 와 동일한 패턴으로 매 프레임 호출합니다.
            // IsOwner 체크는 RegeneratePoise() 내부에서 수행하므로 여기서는 불필요합니다.
            characterStatsManager?.RegeneratePoise();

            currentState = currentState.Tick(this);
        }

        // =====================================================================
        // ⑩ OnPoiseBreak — 포이즈 파괴 이벤트 수신
        //
        //  TakeDamageEffect.PlayDirectionalBasedDamagedAnimation() 에서
        //  poiseIsBroken = true 가 확정된 직후 호출됩니다.
        //
        //  역할:
        //   - 포이즈 회복 타이머 강제 리셋
        //   - FSM 의 GroggyState 전환은 CombatStanceState.Tick() 의
        //     currentPoise.Value <= 0 조건이 자동으로 처리합니다.
        //     (이 메서드에서 직접 FSM 을 조작하지 않습니다 — 계층 아키텍처 준수)
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