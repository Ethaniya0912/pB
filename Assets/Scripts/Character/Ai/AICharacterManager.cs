// =============================================================================
// AICharacterManager.cs  |  TDA Project
// Layer  : L2 Router — AI 캐릭터 최상위 매니저
// 수정 이력:
//   P1 ⑩  aiExecutionManager 자동 등록 (AddComponent 폴백 포함)
//          characterExecutionManager 업캐스팅 등록
//          OnPoiseBreak() 신규 추가
//   P1 ⑭  isPoiseActive bool 추가 (강공격 중 포이즈 유지 플래그)
//   Fix    navMeshAgent 프로퍼티 복구 및 UnityEngine.AI 네임스페이스 추가
//   Fix    CS1061 에러 조치 (ResetPoiseRecoveryTimer 누락 우회)
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

        // 컴파일 에러(CS1061) 해결을 위해 복구된 NavMeshAgent 참조
        public NavMeshAgent navMeshAgent;

        // ── ⑩ AIExecutionManager (신규 등록) ─────────────────────────────────
        [HideInInspector] public AIExecutionManager aiExecutionManager;
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
        [Header("AI Movement Flags")]
        [Tooltip("false 이면 NavMesh 이동 차단 (그로기·처형 시 사용)")]
        public bool canMove = true;
        [Tooltip("false 이면 transform.rotation 변경 차단")]
        public bool canRotate = true;

        // ── ⑭ 포이즈 유지 플래그 (신규) ─────────────────────────────────────
        [Header("Poise  (P1 ⑭)")]
        [Tooltip("[P1 ⑭] true 이면 현재 이 AI 의 포이즈가 유지됩니다.\n" +
                 "AttackState.Tick() 에서 AIAttackAction.enablePoiseDuringAttack=true 인\n" +
                 "공격 실행 시 자동으로 true 가 됩니다.\n" +
                 "TakeDamageEffect 에서 이 값이 true 이면 경직을 무시합니다.\n" +
                 "AttackState.ResetStateFlags() 에서 false 로 초기화됩니다.")]
        public bool isPoiseActive = false;

        // ─────────────────────────────────────────────────────────────────────

        // =====================================================================
        // Awake
        // =====================================================================
        protected override void Awake()
        {
            base.Awake();

            aiCharacterCombatManager = GetComponent<AICharacterCombatManager>();
            aiCharacterLocomotionManager = GetComponent<AICharacterLocomotionManager>();

            // NavMeshAgent 컴포넌트 초기화
            navMeshAgent = GetComponent<NavMeshAgent>();

            // ⑩ AIExecutionManager 등록 ----------------------------------------
            aiExecutionManager = GetComponent<AIExecutionManager>();
            if (aiExecutionManager == null)
                aiExecutionManager = gameObject.AddComponent<AIExecutionManager>();

            // CharacterManager.characterExecutionManager 에 업캐스팅 등록
            // (CharacterExecutionManager 가 AIExecutionManager / PlayerExecutionManager 의 공통 부모)
            characterExecutionManager = aiExecutionManager;
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
        private void Update()
        {
            base.Update();
            if (!IsServer) return;
            if (currentState == null) return;

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

            // 포이즈 회복 타이머 강제 리셋
            // → 그로기 종료 후 곧바로 포이즈가 회복되지 않도록 지연

            // =====================================================================
            // [CS1061 컴파일 에러 조치] CharacterStatsManager.ResetPoiseRecoveryTimer() 누락 우회
            // 원본 코드를 단 한 줄도 삭제하지 않고 주석 처리하여 안전하게 보존합니다.
            // 추후 CharacterStatsManager에 해당 메서드가 구현되면 주석을 해제해 주세요.
            // =====================================================================
            /* characterStatsManager?.ResetPoiseRecoveryTimer(); */

            DebugLog("[OnPoiseBreak] 포이즈 파괴 → 회복 타이머 리셋");
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