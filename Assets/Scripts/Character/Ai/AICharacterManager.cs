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
//   v1.1 (굳음 진단 — 2026-05-06)
//     - StuckDetection 추가:
//       isPerformingAction이 임계 시간 이상 true 유지 시 자동으로 진단 로그 출력.
//       canMove/canRotate/ActionState/모든 Animator 레이어의 현재 클립과 normalized time을
//       박제하여 어느 레이어의 어느 상태에서 stuck됐는지 즉시 식별 가능.
//       원인: Action Override 레이어 등 상위 레이어가 Stagger/Hit 상태에서 Empty로
//             트랜지션 못하면 ResetCharacterStateBehaviour가 차단되어 canMove=false 영구 고정.
//       해결책: 진단 로그가 가리키는 레이어/클립의 Animator 트랜지션을 직접 수정.
// =============================================================================
using System.Text;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI; // 컴파일 에러 해결을 위해 추가
using TDA.Core.Events; // [v1.1] AnimatorParameterHash (굳음 진단용)

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


        // ── [v1.1] 굳음 진단 (Stuck Detection) ──────────────────────────────
        [Header("━━━ 굳음 진단 (Stuck Detection) ━━━━━")]
        [Tooltip("isPerformingAction이 임계 시간 이상 true 유지 시 진단 로그 출력. " +
                 "Animator Controller의 트랜지션 누락(예: Stagger → Empty)으로 인한 굳음을 식별. " +
                 "canMove/canRotate/ActionState/모든 레이어의 현재 클립을 박제하여 출력.")]
        public bool stuckDetectionEnabled = true;

        [Tooltip("이 시간(초) 이상 isPerformingAction=true이면 굳음으로 간주. " +
                 "정상 공격 모션은 보통 1-2초 이내. 권장 3-5초.")]
        [Range(1f, 10f)]
        public float stuckDetectionThreshold = 4f;

        [Tooltip("굳음 상태에서 진단 로그 반복 출력 간격(초). " +
                 "0이면 1회만 출력. 권장 2초.")]
        [Range(0f, 10f)]
        public float stuckRepeatInterval = 2f;

        // 내부 상태 (런타임 자동 갱신)
        private float _stuckTimer = 0f;
        private float _stuckLastLogTime = 0f;
        private bool _stuckReported = false;
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

            // ── AI 스태미나 재생 ───────────────────────────────────────────────────
            // CharacterStatsManager.RegenerateStamina()는 !IsOwner → AI에서 작동 안 함.
            // 네트워크 세션 유무와 무관하게 서버 또는 에디터 단독 실행 모두 지원합니다.
            RegenerateAIStamina();

            // ── [v1.1] 굳음 진단 ──────────────────────────────────────────────
            // isPerformingAction이 임계 시간 이상 true로 유지되면 진단 로그 출력.
            // 네트워크 활성 시 서버 전용, 오프라인 시 항상 동작.
            UpdateStuckDetection();

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

        // =====================================================================
        // [v1.1] 굳음 진단 (Stuck Detection)
        //
        // 동작 매트릭스:
        //   isPerformingAction == true → _stuckTimer 누적
        //     → 임계치 초과 시 LogStuckDiagnostic() 출력 (옵션에 따라 반복)
        //   isPerformingAction == false → 타이머 0 리셋
        //     → 직전에 stuck 보고된 적 있으면 1회 회복 로그
        //
        // 네트워크 게이트:
        //   온라인 모드(NetworkManager 활성) → 서버 전용
        //   오프라인 모드 → 항상 동작
        // =====================================================================
        private void UpdateStuckDetection()
        {
            if (!stuckDetectionEnabled) return;

            // 네트워크 활성 시 서버만 진단 (canMove 등이 서버 권위형 상태이므로)
            bool netActive = Unity.Netcode.NetworkManager.Singleton != null &&
                             Unity.Netcode.NetworkManager.Singleton.IsListening;
            if (netActive && !IsServer) return;

            if (isPerformingAction)
            {
                _stuckTimer += Time.deltaTime;

                if (_stuckTimer >= stuckDetectionThreshold)
                {
                    float now = Time.time;
                    bool shouldLog = !_stuckReported
                        || (stuckRepeatInterval > 0f && now - _stuckLastLogTime >= stuckRepeatInterval);

                    if (shouldLog)
                    {
                        LogStuckDiagnostic();
                        _stuckReported = true;
                        _stuckLastLogTime = now;
                    }
                }
            }
            else
            {
                if (_stuckReported)
                {
                    // 굳음 → 정상 복귀: 회복 로그 1회 출력 후 상태 초기화
                    Debug.Log(
                        $"<color=#81C784>[Stuck:{name}]</color> 회복됨 — " +
                        $"isPerformingAction이 false로 돌아옴. 굳음 지속 시간: {_stuckTimer:F1}s",
                        this);
                }
                _stuckTimer = 0f;
                _stuckReported = false;
            }
        }

        /// <summary>
        /// 굳음 발생 시 진단 정보를 박제하여 콘솔에 출력.
        /// canMove/canRotate/isPerformingAction 플래그, ActionState 파라미터,
        /// 모든 Animator 레이어의 현재 클립과 normalized time을 포함.
        /// 의심 가는 레이어(상위 레이어가 Empty/Stance_Hold가 아닌 경우)에는 ← 표시.
        /// </summary>
        private void LogStuckDiagnostic()
        {
            var sb = new StringBuilder(640);

            sb.AppendLine(
                $"<color=#FF8A65>[Stuck:{name}]</color> " +
                $"<b>isPerformingAction이 {_stuckTimer:F1}s 동안 true 유지 — 굳음 감지</b>");

            sb.AppendLine(
                $"  플래그: canMove=<b>{canMove}</b>, canRotate=<b>{canRotate}</b>, " +
                $"isPerformingAction=<b>{isPerformingAction}</b>");

            if (animator != null)
            {
                int actionState = animator.GetInteger(AnimatorParameterHash.ActionState);
                string actionStateNote = (actionState == 0)
                    ? "(0=정상)"
                    : "(0이 아님 → ResetCharacterStateBehaviour의 layer 0 게이트가 차단됨)";
                sb.AppendLine($"  Animator: ActionState=<b>{actionState}</b> {actionStateNote}");

                sb.AppendLine($"  레이어별 현재 상태:");
                int layerCount = animator.layerCount;
                for (int i = 0; i < layerCount; i++)
                {
                    string layerName = animator.GetLayerName(i);
                    float layerWeight = animator.GetLayerWeight(i);
                    var stateInfo = animator.GetCurrentAnimatorStateInfo(i);

                    string clipName = "?";
                    var clips = animator.GetCurrentAnimatorClipInfo(i);
                    if (clips.Length > 0 && clips[0].clip != null)
                        clipName = clips[0].clip.name;

                    // 의심 표시: 상위 레이어(i>0)가 Empty/Stance_Hold가 아니면 잔류 의심
                    string suspectFlag = "";
                    if (i > 0 && layerWeight > 0.01f
                        && clipName != "Empty"
                        && !clipName.StartsWith("Stance_Hold"))
                    {
                        suspectFlag = "  <b>← 의심 (Empty/Stance_Hold가 아닌 상위 레이어 잔류)</b>";
                    }

                    sb.AppendLine(
                        $"    [{i}] {layerName,-22} weight={layerWeight:F2} " +
                        $"clip=\"{clipName}\" normTime={(stateInfo.normalizedTime % 1f):F2} " +
                        $"loop={stateInfo.loop}{suspectFlag}");
                }
            }
            else
            {
                sb.AppendLine($"  Animator: <null>");
            }

            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled)
            {
                Vector3 dv = navMeshAgent.desiredVelocity;
                sb.AppendLine(
                    $"  NavAgent: desiredVel=({dv.x:F2}, {dv.z:F2}) mag={dv.magnitude:F2} " +
                    $"({(dv.magnitude > 0.1f ? "BT 살아있음 — 가고 싶어함" : "BT가 멈춤 신호")})");
            }

            sb.AppendLine($"  진단 가이드:");
            sb.AppendLine($"    1) ActionState ≠ 0 이면 → ResetCharacterStateBehaviour 게이트가 차단됨");
            sb.AppendLine($"    2) 의심 표시된 레이어가 있으면 → 그 레이어의 현재 클립이 Empty로 트랜지션 못함");
            sb.AppendLine($"    3) Animator Controller에서 해당 레이어의 트랜지션 검증 필요:");
            sb.AppendLine($"       (현재 상태) → Stance_Hold 또는 Empty로의 Exit 트랜지션 존재 확인");
            sb.AppendLine($"    4) 트랜지션 추가 시 권장 설정: Has Exit Time=true, Exit Time≈0.95, Duration=0.1");

            Debug.LogWarning(sb.ToString(), this);
        }

        // =====================================================================
        // AI 스태미나 재생 (IsServer 전용)
        // =====================================================================

        // ── 스태미나 재생 파라미터 ───────────────────────────────────────────
        private float _staminaRegenTimer = 0f;
        private float _staminaTickTimer = 0f;
        private float _staminaDebugTimer = 0f;  // 스태미나 상태 주기 출력용

        [Header("AI Stamina Regen (Server)")]
        [Tooltip("스태미나 재생 시작까지 대기 시간 (초).")]
        public float aiStaminaRegenDelay = 2f;
        [Tooltip("0.1초마다 재생되는 스태미나 양.")]
        public int aiStaminaRegenAmount = 2;
        [Tooltip("공격 1회당 소모되는 스태미나 양. 0=소모 없음.")]
        public int aiStaminaDrainPerAttack = 15;

        [Header("AI Stamina Debug")]
        [Tooltip("체크하면 스태미나 소모/재생/차단 사유를 Console에 출력합니다.")]
        public bool debugStamina = false;
        [Tooltip("debugStamina=true일 때 현재 스태미나 상태를 출력하는 주기 (초).")]
        public float debugStaminaInterval = 1f;

        private void RegenerateAIStamina()
        {
            if (characterNetworkManager == null)
            {
                if (debugStamina)
                    Debug.LogWarning($"<color=orange>[Stamina] {name}: characterNetworkManager == null → 재생 불가</color>");
                return;
            }

            // ── 주기적 상태 출력 ───────────────────────────────────────────
            if (debugStamina)
            {
                _staminaDebugTimer += Time.deltaTime;
                if (_staminaDebugTimer >= debugStaminaInterval)
                {
                    _staminaDebugTimer = 0f;
                    float cur = characterNetworkManager.currentStamina.Value;
                    float max = characterNetworkManager.maxStamina.Value;
                    Debug.Log(
                        $"<color=cyan>[Stamina] {name}</color>\n" +
                        $"  IsServer={IsServer}  IsOwner={IsOwner}\n" +
                        $"  currentSP={cur:F0} / maxSP={max:F0}  ({(max > 0 ? cur / max : 0):P0})\n" +
                        $"  regenTimer={_staminaRegenTimer:F2}s / delay={aiStaminaRegenDelay:F1}s\n" +
                        $"  isPerformingAction={isPerformingAction}\n" +
                        $"  drainPerAttack={aiStaminaDrainPerAttack}");
                }
            }

            // NGO 활성 시 서버만, 비활성(에디터 단독 테스트) 시 무조건 실행
            bool _regenNetworked = Unity.Netcode.NetworkManager.Singleton != null
                                && Unity.Netcode.NetworkManager.Singleton.IsListening;
            if (_regenNetworked && !IsServer) return;

            // 행동 중에는 재생 타이머 리셋
            if (isPerformingAction)
            {
                if (debugStamina && _staminaRegenTimer > 0f)
                    Debug.Log($"<color=orange>[Stamina] {name}: isPerformingAction=true → 재생 타이머 리셋</color>");
                _staminaRegenTimer = 0f;
                _staminaTickTimer = 0f;
                return;
            }

            _staminaRegenTimer += Time.deltaTime;
            if (_staminaRegenTimer < aiStaminaRegenDelay) return;

            float spCur = characterNetworkManager.currentStamina.Value;
            float spMax = characterNetworkManager.maxStamina.Value;
            if (spCur >= spMax)
            {
                if (debugStamina)
                    Debug.Log($"<color=grey>[Stamina] {name}: 스태미나 만충 ({spCur:F0}/{spMax:F0})</color>");
                return;
            }

            _staminaTickTimer += Time.deltaTime;
            if (_staminaTickTimer < 0.1f) return;

            _staminaTickTimer = 0f;
            float before = spCur;
            characterNetworkManager.currentStamina.Value =
                Mathf.Min(spMax, spCur + aiStaminaRegenAmount);

            if (debugStamina)
                Debug.Log($"<color=lime>[Stamina] {name}: 재생 +{aiStaminaRegenAmount} → {before:F0} → {characterNetworkManager.currentStamina.Value:F0}/{spMax:F0}</color>");
        }

        /// <summary>
        /// 공격 실행 시 스태미나를 소모합니다.
        /// StrikeAction.TryExecuteAttack()에서 공격 확정 직후 호출하세요.
        /// </summary>
        public void DrainStaminaForAttack()
        {
            if (characterNetworkManager == null)
            {
                if (debugStamina)
                    Debug.LogWarning($"<color=red>[Stamina] {name}: DrainStaminaForAttack — characterNetworkManager == null!</color>");
                return;
            }

            // NGO 활성 시 서버만, 비활성(에디터 단독 테스트) 시 무조건 실행
            bool _isNetworked = Unity.Netcode.NetworkManager.Singleton != null
                             && Unity.Netcode.NetworkManager.Singleton.IsListening;
            if (_isNetworked && !IsServer)
            {
                if (debugStamina)
                    Debug.LogWarning($"<color=red>[Stamina] {name}: DrainStaminaForAttack — IsServer=false (NGO 활성). 소모 불가.\n" +
                                     "  Host로 실행하세요. 에디터 단독 실행 시 NGO를 Start하지 않으면 이 경로입니다.</color>");
                return;
            }

            if (aiStaminaDrainPerAttack <= 0)
            {
                if (debugStamina)
                    Debug.Log($"<color=grey>[Stamina] {name}: DrainStaminaForAttack — drainPerAttack={aiStaminaDrainPerAttack}, 소모 없음.</color>");
                return;
            }

            float before = characterNetworkManager.currentStamina.Value;
            characterNetworkManager.currentStamina.Value = Mathf.Max(0f, before - aiStaminaDrainPerAttack);
            float after = characterNetworkManager.currentStamina.Value;

            // 재생 딜레이 리셋
            _staminaRegenTimer = 0f;

            Debug.Log(
                $"<color=red>[Stamina] {name}: 공격 소모 -{aiStaminaDrainPerAttack}</color>\n" +
                $"  {before:F0} → {after:F0} / {characterNetworkManager.maxStamina.Value:F0}\n" +
                $"  재생 딜레이 리셋 ({aiStaminaRegenDelay:F1}s 후 재생 재개)");
        }
        /// <summary>Inspector 컨텍스트 메뉴 또는 코드에서 호출해 스태미나 상태를 즉시 출력합니다.</summary>
        [ContextMenu("Log Stamina Status")]
        public void LogStaminaStatus()
        {
            bool _isNetworked = Unity.Netcode.NetworkManager.Singleton != null
                             && Unity.Netcode.NetworkManager.Singleton.IsListening;
            if (characterNetworkManager == null)
            {
                Debug.LogWarning($"[Stamina Check] {name}: characterNetworkManager == null");
                return;
            }
            float cur = characterNetworkManager.currentStamina.Value;
            float max = characterNetworkManager.maxStamina.Value;
            float hp = characterNetworkManager.currentHealth.Value;
            float maxHp = characterNetworkManager.maxHealth.Value;
            Debug.Log(
                $"<color=cyan>[Stamina Check] {name}</color>\n" +
                $"  SP: {cur:F0} / {max:F0}  ({(max > 0 ? cur / max : 0f):P0})\n" +
                $"  HP: {hp:F0} / {maxHp:F0}  ({(maxHp > 0 ? hp / maxHp : 0f):P0})\n" +
                $"  IsServer={IsServer}  IsOwner={IsOwner}  NGO={_isNetworked}\n" +
                $"  isPerformingAction={isPerformingAction}\n" +
                $"  regenDelay={aiStaminaRegenDelay:F1}s  regenAmount={aiStaminaRegenAmount}  " +
                $"drainPerAttack={aiStaminaDrainPerAttack}\n" +
                $"  debugStamina={debugStamina}");
        }
    }
}