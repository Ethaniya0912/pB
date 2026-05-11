// =============================================================================
// PB4DecisionAdapter.cs  |  pB×pC 통합 — Week 1 (Bridge)
// Layer  : L2 Router (유틸리티 AI ↔ BehaviorGraph Blackboard 브릿지)
// 패키지  : Unity Behavior 1.0.15 (com.unity.behavior)
//
// 역할:
//   ① MobAIBrain/HumanoidAIBrain의 유틸리티 계산 결과를
//     BehaviorGraphAgent의 Blackboard에 기록합니다.
//   ② BT Action 노드들이 Blackboard를 읽어서 NavMesh 이동/공격/도주를 실행합니다.
//
// =============================================================================
// BB 소유권 테이블 (예방 체크리스트 #2)
// 이 컴포넌트가 Write하는 BB 변수와 Read하는 컴포넌트 목록.
// 새 BB 변수 추가 시 반드시 이 테이블을 갱신할 것.
//
// BB["UtilityWinner"]      Writer: UpdateBlackboard() 매 0.5s
//                           +Override: Suspicious 수색 모드 → "Attack" 강제 (Gate G-3)
//                           Reader: BT Pass If 조건 노드
// BB["HasTarget"]          Writer: UpdateBlackboard()
//                           Reader: BT 조건 (현재 삭제됨), StalkAction 내부 폴백 판단
// BB["Target"]             Writer: UpdateBlackboard() — null 포함 항상 기록 (Fix-B)
//                           Reader: StalkAction, CircleStrafeAction, StrikeAction
// BB["Fear"]               Writer: UpdateBlackboard()
//                           Reader: BT Pass If Flee 조건, FleeDuelAction
// BB["LastHeardPosition"]  Writer: (원래) SoundPerception
//                           +Writer: UpdateBlackboard() Suspicious 수색 모드 직접 push
//                           ※ AIPerceptionSystem.lastKnownPosition(내부 필드)과 별개!
//                           Reader: StalkAction (Target==null 폴백)
// BB["LastSeenPosition"]   Writer: MemoryPerception
//                           Reader: StalkAction (Target==null && LastHeard==zero 폴백)
// BB["PredictedPosition"]  Writer: PredictionPerception
//                           Reader: StalkAction (Target!=null && Predicted!=zero 우선)
// BB[SO 파라미터들]         Writer: InitializeBlackboard() 1회 (Step1~4)
//                           Reader: 각 BT Action 노드 (StalkSpeed, EngageRange, ...)
//
// externallyTicked 사용 규칙 (예방 체크리스트 #3):
//   externallyTicked=true → Brain 이중 틱 방지. BT는 0.5s마다만 자체 갱신.
//   규칙 A: BB 변경 후 BT Observer Abort가 즉시 필요하면 btAgent.Update() 명시 호출.
//           현재 적용: SoundSearch▶START 첫 진입 시 btAgent.Update() 강제 호출.
//   규칙 B: ForceSyncBB()는 BB 값 복사 역할만. BT 재평가는 별도 틱이 필요.
//   규칙 C: externallyTicked 변경 시 Observer Abort 동작 재검증 필수.
//
// 단계별 검증 가이드 (예방 체크리스트 #8):
//   단계1: [SoundSearch▶INIT]  — Start() 시 AIPerceptionSystem 캐싱 성공 여부
//   단계2: [SoundSearch▶START] — Suspicious 감지 → winner="Attack" 오버라이드
//   단계3: [SoundSearch▶POS]   — BB["LastHeardPosition"] push 성공
//   단계4: [SoundSearch▶TICK]  — btAgent.Update() 강제 호출 (Observer 발동)
//   단계5: [SoundSearch▶ACTIVE]— 수색 모드 유지 중 (5회에 1회)
//   단계6: [SoundSearch▶END]   — 타겟 발견 or Unaware 복귀
//   단계7: [PerceptionInject]  — SO → AIPerceptionSystem 주입 완료
//   → 각 단계 로그가 순서대로 보이면 파이프라인 정상.
// =============================================================================
// Blackboard 갱신 목록 (설계 문서 E2 기준):
//   Self (GameObject)          — BehaviorGraphAgent 자동 설정
//   Target (Transform)         — brain.currentTarget
//   UtilityWinner (string)     — "Attack"/"Flee"/"Patrol"/"Idle"
//   HasTarget (bool)           — brain.currentTarget != null
//   Fear (float)               — brain.fear
//   FactionPolicyType (string)        — "Swarm"/"Duel"/"Phalanx" (Start 1회)
//   StalkSpeed (float)         — combatProfile.stalkSpeed
//   EngageRange (float)        — combatProfile.engageRange
//   OrbitRadius (float)        — combatProfile.orbitRadius
//   StrafeAngularSpeed (float) — combatProfile.strafeAngularSpeed
//   StrikeTriggerTime (float)  — combatProfile.strikeTriggerTime
//   FleeSprintSpeed (float)    — combatProfile.fleeSprintSpeed
//   TerrainTags (string)       — TODO: GameBlackboard 연동
//   AttackStateConfig          — pursueState→combatStanceState→attackState 자동 설정
//   LastHeardPosition / LastSeenPosition / PredictedPosition — Perception 별도 설정
//
// NavMesh 인프라 보호 (I-01~I-08 해결):
//   updatePosition=false 강제
//   canMove/canRotate 조건부 복원 (isPerformingAction 중에는 건드리지 않음)
//
// [수정 이력]
//   Phase 5 (FSM 탈락): FSM 관련 코드 전체 제거. BB 브릿지 코드만 유지.
//   Fix-A (Bug②): externallyTicked=true — Brain 이중 틱 방지.
//   Fix-B (Bug⑤): Target null 항상 기록 — Stale reference 방지.
//   Fix-C (Bug①③⑥): SetBB<T>() Inspector 실시간 표시 완전 구현.
//   Fix-D (Bug⑦): SetBB<T>() 3단계 중복 제거.
//   Fix-E (Bug): isPerformingAction 강제 리셋 제거 — 공격 모션 보호.
//   Phase 2 Patch (BB 0값 해결):
//     [PATCH-1] Awake() → IEnumerator Start() — Graph 초기화 대기
//     [PATCH-2] combatProfile null 시 안전한 기본값 SetBB 적용
//     [PATCH-3] DiagnoseBlackboard() 컨텍스트 메뉴 추가
//     [PATCH-4] Update() 지연 초기화 블록 → 단순 가드로 교체
//   Fix-F (타이밍 버그 — FleeDuelAction Freeze):
//     ForceSyncBB() 공개 메서드 추가.
//     BT Action 노드가 brain 상태를 직접 변경한 직후 0.5s 틱 대기 없이
//     BB 를 즉시 동기화할 수 있도록 함.
//     FleeDuelAction.OnStart() 에서 호출 → Orc 정지 현상 해결.
//   Fix-G (Bug A/B/C — Flee→Attack 트랜지션 불가):
//     Bug A: FireFearBoost/FireFearReset 이 UpdateDecision 후 ForceSyncBB 를 호출하지 않아
//            BB 가 최대 0.5초 동안 구버전 상태를 유지 → BT 가 즉시 반응하지 못함.
//            → PerceptionProbeWindow.FireFearBoost/FireFearReset 에서 ForceSyncBB 즉시 호출.
//     Bug B: ForceSyncBB() 내부의 UpdateUtilityDecision() 이중 호출.
//            → UpdateUtilityDecision() 제거. 호출 측이 미리 UpdateDecision 완료해야 함.
//     Bug C: ProtectNavMeshInfra() 의 updatePosition=false 로 NavMesh 위치 desync.
//            StalkAction.OnStart() 의 isOnNavMesh 체크 실패 → Attack 분기 즉시 Failure.
//            → FleeDuelAction.OnStart() 에서 nextPosition 재동기화 추가.
//   Fix-H (Bug 1/2/3/4 — 4개 복합 버그):
//     Bug 1: CircleStrafeAction 이 타이머만 보고 Strike 전환 → 9.5m 허공 칼질.
//            → AttackRange BB push 추가 (SetBB("AttackRange", combatProfile.attackRange)).
//     Bug 2: FleeDuelAction 이 EngageRange 밖에서도 즉시 fear 리셋 → 10m+ 돌진.
//            → EngageRange BB 변수 추가 + OnUpdate() 접근 로직 추가.
//     Bug 3/4: PanicChainRadius/Multiplier 미push → FleeSwarmAction 기본값(5/2.0) 사용
//              → Orc 패닉 전파 cascade + speed=0 Freeze + Attack↔Flee 진동.
//              → SetBB("PanicChainRadius"), SetBB("PanicChainMultiplier") 추가.
//              → FactionPolicyType 설정 후 read-back 검증 추가 (Swarm fallback 조기 탐지).
//
// 네임스페이스: TDA.PB4.Bridge
// =============================================================================
using System.Collections;                                          // [PATCH-1] IEnumerator
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using Unity.Behavior;
using Unity.Behavior.GraphFramework;
using TDA.Character.AI;
using TDA.PB4.AI;
using TDA.PB4.AI.Mob;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.AI.Perception;     // AIPerceptionSystem, AwarenessState — Gate G-3 청각 수색 연동
using TDA.PB4.Data;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TDA.PB4.Bridge
{
    public class PB4DecisionAdapter : MonoBehaviour
    {
        // =====================================================================
        // Inspector — pB-4 AI 참조
        // =====================================================================

        [Header("━━━ pB-4 AI 참조 ━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("pB-4 MobAI. 같은 GameObject에 있으면 자동 탐색.")]
        [SerializeField] private MobAIBrain mobBrain;

        [Tooltip("pB-4 HumanoidAI. 같은 GameObject에 있으면 자동 탐색.")]
        [SerializeField] private HumanoidAIBrain humanoidBrain;

        // =====================================================================
        // Inspector — 기존 AI 참조
        // =====================================================================

        [Header("━━━ 기존 AI 참조 ━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("기존 AICharacterManager. 자동 탐색.")]
        [SerializeField] private AICharacterManager aiManager;

        // =====================================================================
        // Inspector — Behavior 1.0.15 BT 연동
        // =====================================================================

        [Header("━━━ Behavior Graph (1.0.15) ━━━━━━━━━")]

        [Tooltip("BehaviorGraphAgent. 같은 GameObject에 있으면 자동 탐색.")]
        [SerializeField] private BehaviorGraphAgent btAgent;

        [Tooltip("팩션 전투 프로파일 SO. Start에서 BB에 파라미터 복사.\n미할당 시 안전 기본값이 설정되지만 실제 전투 파라미터와 다를 수 있습니다.")]
        [SerializeField] private FactionCombatProfileSO combatProfile;

        [Tooltip("FactionPolicyType을 SO 이름 감지 대신 직접 지정합니다.\n비워두면 combatProfile SO 이름에서 자동 감지.\nSwarm / Duel / Phalanx 중 하나를 입력하세요.\nSO 이름에 팩션명이 없어서 잘못 감지될 때 사용하세요.")]
        [SerializeField] private string policyTypeOverride = "";

        // =====================================================================
        // Inspector — FSM 참조 (AttackState 체인 접근용)
        // =====================================================================

        [Header("━━━ AttackState 연결 (Strike 공격 체인) ━━")]

        [Tooltip("pursueState → combatStanceState → attackState 체인으로 공격 액션 취득.\nStrikeAction이 공격 애니메이션 ID를 읽기 위해 사용합니다.")]
        [SerializeField] private PursueTargetState pursueState;

        // =====================================================================
        // Inspector — 업데이트 설정
        // =====================================================================

        [Header("━━━ 업데이트 설정 ━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("유틸리티 계산 + BB 갱신 주기 (초).")]
        [Range(0.1f, 2f)]
        [SerializeField] private float updateInterval = 0.5f;

        [Tooltip("fear 값에 따라 NavMesh 이동 속도를 baseSpeed~maxSpeed 사이에서 보간합니다.")]
        [SerializeField] private bool syncSpeedWithFear = true;

        [Tooltip("fear=0일 때의 NavMesh 이동 속도.")]
        [SerializeField] private float baseSpeed = 3.5f;

        [Tooltip("fear=1일 때의 NavMesh 이동 속도 (도주 시 최대 속도).")]
        [SerializeField] private float maxSpeed = 6.0f;

        // =====================================================================
        // Inspector — 읽기 전용 (런타임 상태 확인용)
        // =====================================================================

        [Header("━━━ 읽기 전용 (런타임 상태) ━━━━━━━━━")]

        [Tooltip("마지막으로 BB에 기록한 UtilityWinner 상태.")]
        [SerializeField] private string lastPB4State = "None";

        /// <summary>[v3.4] 마지막으로 BB에 기록한 UtilityWinner 상태 (read-only). 모니터링용.</summary>
        public string LastPB4State => lastPB4State;

        // =====================================================================
        // Inspector — 디버그
        // =====================================================================

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("BB 갱신 로그를 Console에 출력합니다. 성능에 영향을 주므로 릴리즈 전 끄세요.")]
        [SerializeField] private bool debugLog = true;

        [Tooltip("청각 수색 모드 진입/해제 로그 출력. Gate G-3 검증용. true = [SoundSearch] 로그 활성. 검증 완료 후 false로 끄세요.")]
        [SerializeField] private bool debugSoundSearch = true;

        [Tooltip("SO→AIPerceptionSystem 인지 파라미터 주입 로그. true = [PerceptionInject] 로그 활성 — SO값이 올바르게 주입됐는지 콘솔로 확인 가능. 검증 완료 후 false로 끄세요.")]
        [SerializeField] private bool debugPerceptionInject = true;

        /// <summary>[v3.4 §5 Stage 2.1] 전략적 후퇴 명령 박제 verbose 로그.</summary>
        [Tooltip("ON → OrderedRetreat 트리거 시 [Adapter▶TacticalRetreat] 박제 박제.")]
        [SerializeField] private bool verboseTacticalRetreat = true;

        [Tooltip("SceneView에 현재 상태를 텍스트로 표시합니다.")]
        [SerializeField] private bool showGizmo = true;

        // =====================================================================
        // 내부 상태
        // =====================================================================

        /// <summary>BB 초기화 완료 여부. IEnumerator Start()가 true로 설정합니다.</summary>
        private bool _bbInitialized = false;

        /// <summary>[Gate G-3] 청각 수색 ACTIVE 로그 빈도 조절용 카운터 (5회에 1회 출력)</summary>
        private int _soundSearchLogCounter = 0;

        // ── Stalk 실패 쿨다운 (깜빡임 방지) ──────────────────────────────────────
        // StalkAction이 우회/Stuck으로 실패하면 BB["StalkBlocked"]=true 신호를 보냄.
        // Adapter가 이 신호를 받아 _stalkBlockedTimer를 시작하고
        // 타이머가 끝날 때까지 UtilityWinner를 강제로 "Patrol"로 유지.
        // 문제: 타이머 없이 UtilityWinner만 Patrol로 바꾸면
        //       0.5s 후 Adapter 틱에서 다시 "Attack"으로 뒤집혀 깜빡임 발생.
        private float _stalkBlockedTimer = 0f;
        private const float STALK_BLOCKED_DURATION = 5f; // 5초간 Attack 재진입 차단

        /// <summary>
        /// [Gate G-3] AIPerceptionSystem 캐시.
        /// Start()에서 1회 GetComponent — UpdateBlackboard() 매 틱 호출 비용 절감.
        /// AIPerceptionSystem이 없는 AI(청각 미사용)는 null → 수색 모드 미진입.
        /// </summary>
        private AIPerceptionSystem _perceptionSystem;

        /// <summary>FleeSwarmAction 등 외부에서 BB 초기화 완료 여부를 확인할 때 사용.</summary>
        public bool IsBBInitialized => _bbInitialized;

        /// <summary>
        /// [Fix-H] DetectPolicyType() 으로 감지된 팩션 정책 타입을 캐시합니다.
        /// FleeSwarmAction 등 BB 변수 연결 없이 정책을 읽어야 하는 코드에서 사용.
        /// BB "FactionPolicyType" 변수가 BT 에디터에서 연결되지 않아도 신뢰할 수 있음.
        /// InitializeBlackboard() 완료 전에는 기본값 Swarm 을 반환합니다.
        /// </summary>
        public FactionPolicyType DetectedPolicyType { get; private set; } = FactionPolicyType.Swarm;

        /// <summary>Update 경과 타이머. updateInterval마다 유틸리티 계산·BB 갱신을 수행합니다.</summary>
        private float _timer;

        /// <summary>BB 상세 로그 카운터. 첫 3회 이후에는 상태 변경 시에만 출력합니다.</summary>
        private int _bbDebugCount;

        // =====================================================================
        // [PATCH-1]  Awake() 제거 → IEnumerator Start() 로 교체
        //
        // 기존 문제:
        //   Awake()에서 InitializeBlackboard()를 호출하면
        //   BehaviorGraphAgent.Graph가 아직 Init()되지 않은 상태이므로
        //   SetVariableValue()가 조용히 실패하여 BB 값이 0/false/빈값으로 남습니다.
        //
        // 수정:
        //   IEnumerator Start()로 전환하여 BehaviorGraphAgent.Graph 초기화를
        //   최대 kMaxWaitFrames 프레임 대기 후 InitializeBlackboard()를 호출합니다.
        // =====================================================================

        /// <summary>
        /// 코루틴 Start. 컴포넌트를 자동 탐색하고, BehaviorGraphAgent Graph 초기화를
        /// 기다린 후 Blackboard에 초기값을 설정합니다.
        /// </summary>
        private IEnumerator Start()
        {
            // ── 컴포넌트 자동 탐색 ────────────────────────────────────────────
            if (mobBrain == null) mobBrain = GetComponent<MobAIBrain>();
            if (humanoidBrain == null) humanoidBrain = GetComponent<HumanoidAIBrain>();
            if (aiManager == null) aiManager = GetComponent<AICharacterManager>();
            if (btAgent == null) btAgent = GetComponent<BehaviorGraphAgent>();

            // 필수 컴포넌트 검증
            if (aiManager == null)
            {
                Debug.LogError(
                    $"[PB4Adapter] {name}: AICharacterManager를 찾을 수 없습니다.\n" +
                    "같은 GameObject에 컴포넌트가 있는지 확인하세요.");
                enabled = false;
                yield break;
            }

            if (mobBrain == null && humanoidBrain == null)
            {
                Debug.LogError(
                    $"[PB4Adapter] {name}: MobAIBrain 또는 HumanoidAIBrain을 찾을 수 없습니다.\n" +
                    "같은 GameObject에 Brain 컴포넌트가 있는지 확인하세요.");
                enabled = false;
                yield break;
            }

            // ── [Fix-A] externallyTicked 설정 — Brain 이중 틱 방지 ────────────
            // MobAIBrain.Update()가 0.2초마다, PB4DecisionAdapter.Update()가 0.5초마다
            // 각각 UpdateDecision()을 호출하여 이중 호출이 발생하는 문제를 방지합니다.
            // externallyTicked = true 이면 Brain 자체 Update 틱이 스킵됩니다.
            if (mobBrain != null)
            {
                mobBrain.externallyTicked = true;
                if (debugLog)
                    Debug.Log($"[PB4Adapter] {name}: MobAIBrain.externallyTicked = true " +
                              "(Brain 자체 Update 틱 비활성화).");
            }
            if (humanoidBrain != null)
            {
                humanoidBrain.externallyTicked = true;
                if (debugLog)
                    Debug.Log($"[PB4Adapter] {name}: HumanoidAIBrain.externallyTicked = true " +
                              "(Brain 자체 Update 틱 비활성화).");
            }

            // ── BehaviorGraphAgent 존재 확인 ───────────────────────────────────
            if (btAgent == null)
            {
                Debug.LogWarning(
                    $"[PB4Adapter] {name}: BehaviorGraphAgent를 찾을 수 없습니다.\n" +
                    "Blackboard 브릿지가 비활성화됩니다.\n" +
                    "같은 GameObject에 BehaviorGraphAgent가 있고, " +
                    "BehaviorGraph 에셋이 할당되어 있는지 확인하세요.");
                _bbInitialized = true;  // [T2.4 hotfix] BT 없어도 Brain 틱은 가동 (UpdateDecision 호출 보장)
                yield break;
            }

            // ── BehaviorGraphAgent.Graph 초기화 대기 ──────────────────────────
            // BehaviorGraphAgent.Start()가 그래프를 Init()하므로
            // 최소 1프레임, 최대 kMaxWaitFrames 프레임을 기다립니다.
            const int kMaxWaitFrames = 10;

            for (int i = 0; i < kMaxWaitFrames; i++)
            {
                // BlackboardReference가 유효하면 Graph 초기화 완료로 판단
                if (btAgent.BlackboardReference?.Blackboard != null)
                {
                    if (debugLog)
                        Debug.Log(
                            $"[PB4Adapter] {name}: Graph 초기화 확인 ({i + 1}프레임 대기).");
                    break;
                }

                if (i == kMaxWaitFrames - 1)
                {
                    Debug.LogWarning(
                        $"[PB4Adapter] {name}: BehaviorGraphAgent.Graph가 " +
                        $"{kMaxWaitFrames}프레임 내에 초기화되지 않았습니다.\n" +
                        "BB 초기값 설정을 강제 시도합니다. " +
                        "BehaviorGraph 에셋이 올바르게 할당됐는지 확인하세요.");
                }

                yield return null; // 1프레임 대기
            }

            // ── BB 초기값 설정 ─────────────────────────────────────────────────
            InitializeBlackboard();

            // ── [Shared BB 진단] UtilityWinner, Fear 등이 Shared 변수인지 확인 ──────
            // 이 오류가 보이면: BT 에디터 Blackboard 패널에서
            // UtilityWinner / Fear / HasTarget / Target 의 [Shared] 토글을 해제하세요.
            // Shared 변수는 같은 BT 그래프를 쓰는 모든 에이전트가 공유합니다.
            // Orc_01이 "Flee"를 쓰면 Orc_02, Orc_03도 "Flee"로 읽히는 원인입니다.
            CheckSharedVariables();

            _bbInitialized = true;

            if (debugLog)
                Debug.Log($"[PB4Adapter] {name}: Start() 완료. BB 초기화 성공.");
        }

        // =====================================================================
        // BB 초기값 설정
        // =====================================================================

        /// <summary>
        /// Blackboard에 초기값을 설정합니다.
        /// FactionCombatProfileSO의 파라미터를 BB에 복사하고,
        /// FactionPolicyType을 자동 감지 또는 Inspector 직접 지정값으로 설정합니다.
        /// </summary>
        private void InitializeBlackboard()
        {
            // ── FactionPolicyType 감지 ────────────────────────────────────────────────
            // [Enum 전환] string → FactionPolicyType Enum (BT Switch 노드 호환)
            FactionPolicyType policyType = DetectPolicyType();
            DetectedPolicyType = policyType; // [Fix-H] 컴포넌트 직접 참조용 캐시

            Debug.Log(
                $"[PB4Adapter][POLICY] {name}: DetectedPolicyType={policyType}\n" +
                $"  combatProfile={(combatProfile != null ? combatProfile.name : "NULL")}\n" +
                $"  policyTypeOverride=\"{policyTypeOverride}\"\n" +
                $"  ★ Orc는 반드시 Duel이어야 합니다. Swarm이면 FleeSwarmAction 오실행 원인.");

            SetBB("FactionPolicyType", policyType);

            // ── [Bug 3&4 수정] FactionPolicyType 설정 검증 read-back ───────────────
            // SetBB() 가 조용히 실패하면 BB 에 Swarm(=0) 기본값이 남습니다.
            // 그 경우 Orc(Duel) 가 FleeSwarmAction 을 실행 → speed=0 Freeze + 패닉 전파.
            // 설정 직후 BB 를 다시 읽어 불일치 시 즉시 오류를 출력합니다.
            //
            // 실패 원인 1: BT Blackboard 에 "FactionPolicyType" 변수가 없음
            //   → BT 에디터에서 FactionPolicyType(enum) 타입 변수를 추가하세요.
            // 실패 원인 2: 변수 타입이 int/string 으로 잘못 생성됨
            //   → 변수를 삭제하고 FactionPolicyType(BlackboardEnum) 타입으로 재생성하세요.
            // 실패 원인 3: [BlackboardEnum] 어트리뷰트 누락
            //   → PolicyType.cs 의 FactionPolicyType enum 에 [BlackboardEnum] 이 있는지 확인.
            if (btAgent != null)
            {
                // [Bug 3&4 수정] FactionPolicyType 설정 검증 read-back
                // BlackboardReference?.GetVariable() 에서 null-conditional(?.) 로 인해
                // BlackboardReference 가 null 이면 out 변수가 할당되지 않아 CS0165 발생.
                // → 먼저 null 로 초기화한 뒤 GetVariable 을 호출합니다.
                BlackboardVariable<FactionPolicyType> policyVar = null;
                btAgent.BlackboardReference?.GetVariable("FactionPolicyType", out policyVar);

                if (policyVar == null)
                {
                    Debug.LogError(
                        $"<color=red>[PB4Adapter] {name}: ⛔ BB 'FactionPolicyType' 변수가 없거나 타입 불일치!</color>\n" +
                        "  → BT Blackboard 에 FactionPolicyType(enum) 타입 변수가 있는지 확인하세요.\n" +
                        "  → 변수 부재 시 Switch 노드 기본값(Swarm=0)으로 실행 → FleeSwarmAction 오실행.\n" +
                        "  → Bug 3(패닉 전파 + Freeze), Bug 4(Attack↔Flee 진동)의 원인이 됩니다.");
                }
                else if (policyVar.Value != policyType)
                {
                    Debug.LogError(
                        $"<color=red>[PB4Adapter] {name}: ⛔ FactionPolicyType 기록/읽기 불일치!</color>\n" +
                        $"  기록: {policyType}  읽기: {policyVar.Value}\n" +
                        "  → SetVariableValue() 실패. BB 변수 타입을 확인하세요.");
                }
                else if (debugLog)
                {
                    Debug.Log($"[PB4Adapter] {name}: FactionPolicyType={policyType} BB 검증 완료.");
                }
            }
            // ── [Gate G-3] AIPerceptionSystem 캐싱 ──────────────────────────────
            // 매 틱 GetComponent 비용을 피하기 위해 Start()에서 1회만 캐싱.
            // AIPerceptionSystem이 없는 AI는 null → 청각 수색 모드 미진입.
            _perceptionSystem = GetComponent<AIPerceptionSystem>();
            if (debugSoundSearch)
            {
                if (_perceptionSystem != null)
                    Debug.Log(
                        $"<color=#44CCFF>[SoundSearch▶INIT]</color> {name}: " +
                        $"AIPerceptionSystem 캐싱 완료. 청각 수색 모드 활성화 대기 중.");
                else
                    Debug.LogWarning(
                        $"<color=orange>[SoundSearch▶INIT]</color> {name}: " +
                        $"AIPerceptionSystem 없음. 청각 수색 모드 비활성. " +
                        $"AI 프리팹에 컴포넌트를 추가해야 소리에 반응합니다.");
            }

            SetBB("UtilityWinner", "Idle");
            SetBB("HasTarget", false);
            SetBB("Fear", 0f);
            SetBB("TerrainTags", "");

            // ── CombatProfile SO → BB 복사 ──────────────────────────────────────
            if (combatProfile != null)
            {
                SetBB("StalkSpeed", combatProfile.stalkSpeed);
                SetBB("EngageRange", combatProfile.engageRange);
                SetBB("OrbitRadius", combatProfile.orbitRadius);
                SetBB("StrafeAngularSpeed", combatProfile.strafeAngularSpeed);
                SetBB("StrikeTriggerTime", combatProfile.strikeTriggerTime);
                SetBB("FleeSprintSpeed", combatProfile.fleeSprintSpeed);
                SetBB("RetreatHpThreshold", combatProfile.retreatHpThreshold);  // [v3.4 §5 Stage 2]

                // ── [Bug 1 수정] AttackRange BB push 추가 ────────────────────────
                // CircleStrafeAction 이 StrikeTriggerTime 만료 후 dist <= AttackRange 조건을
                // 추가로 검사하기 위해 combatProfile.attackRange 를 BB 에 전달합니다.
                // 이전 코드: AttackRange 가 BB 에 push 되지 않아 CircleStrafeAction 이
                //            기본값(2.5m)을 사용하거나 BB 변수 자체가 없어 검사 불가.
                SetBB("AttackRange", combatProfile.attackRange);

                // ── [Bug 3&4 수정] PanicChain BB push 추가 ───────────────────────
                // FleeSwarmAction 이 BB 에서 PanicChainRadius / PanicChainMultiplier 를 읽습니다.
                // 이전 코드: 이 두 값이 BB 에 push 되지 않아 FleeSwarmAction 의 기본값
                //            (radius=5, multiplier=2.0)이 사용됐음.
                // Orc SO 에서 panicChainRadius=5, panicChainMultiplier=2 는 잘못된 값 —
                // FactionCombatProfileSO 주석 기준 Orc 는 radius=3, multiplier=0.5 이어야 합니다.
                // SO 에셋의 값을 수정하거나 이 push 를 통해 SO 값이 실제로 적용되도록 합니다.
                // ⚠ Duel 정책 오크는 FleeSwarmAction 을 실행하지 않으므로 이 값이
                //   실제로 사용되는 경우는 FactionPolicyType 감지 오류 상황입니다.
                //   BB 에 정확한 값을 넣어 두면 감지 오류 시에도 파급 범위를 최소화합니다.
                SetBB("PanicChainRadius", combatProfile.panicChainRadius);
                SetBB("PanicChainMultiplier", combatProfile.panicChainMultiplier);
                // ── Step 1: CircleStrafe 타이밍 파라미터 ──────────────────────────
                SetBB("CloseInMaxTime", combatProfile.closeInMaxTime);
                SetBB("FeintIntervalMin", combatProfile.feintIntervalMin);
                SetBB("FeintIntervalMax", combatProfile.feintIntervalMax);
                SetBB("GuardBreakReactionDelay", combatProfile.guardBreakReactionDelay);
                SetBB("PoiseAttackIntervalMin", combatProfile.poiseAttackIntervalMin);
                SetBB("PoiseAttackIntervalMax", combatProfile.poiseAttackIntervalMax);

                // ── Step 2: Flee 파라미터 ────────────────────────────────────────────
                SetBB("ForceAttackFear", combatProfile.forceAttackFear);
                SetBB("StuckCheckInterval", combatProfile.stuckCheckInterval);
                SetBB("StuckMoveThreshold", combatProfile.stuckMoveThreshold);
                SetBB("StuckGiveUpCount", combatProfile.stuckGiveUpCount);
                SetBB("FleeDuelInitialFear", combatProfile.fleeDuelInitialFear);
                SetBB("FleeDuelMaxInitialFear", combatProfile.fleeDuelMaxInitialFear);

                // ── Step 3: Strike + 지형 보정 ────────────────────────────────────────
                SetBB("AttackRotationSpeed", combatProfile.attackRotationSpeed);
                SetBB("AttackTimeout", combatProfile.attackTimeout);
                SetBB("NarrowPathOrbitMult", combatProfile.narrowPathOrbitMult);
                SetBB("NarrowPathStrikeTimeMult", combatProfile.narrowPathStrikeTimeMult);

                // ── Step 4: TacticalReposition ────────────────────────────────────────
                SetBB("StaminaRecoverThreshold", combatProfile.tacticalStaminaRecoverThreshold);
                SetBB("HealthRecoverThreshold", combatProfile.tacticalHealthRecoverThreshold);
                SetBB("RetreatDist", combatProfile.tacticalRetreatDist);
                SetBB("RecoverDuration", combatProfile.tacticalRecoverDuration);
                SetBB("EvasionChance", combatProfile.tacticalEvasionChance);
                SetBB("EvasionDist", combatProfile.tacticalEvasionDist);
                SetBB("EvasionDuration", combatProfile.tacticalEvasionDuration);
                SetBB("FeintChance", combatProfile.tacticalFeintChance);
                SetBB("FeintDist", combatProfile.tacticalFeintDist);
                SetBB("FeintDuration", combatProfile.tacticalFeintDuration);
            }
            else
            {
                // [PATCH-2] combatProfile 미할당 시 안전 기본값 적용
                // 기존: 경고만 출력하고 BB 변수가 0으로 남아 BT 노드 오작동
                // 수정: 일반적인 골격형 몹 기준의 기본값으로 BB를 채워 BT가 정상 동작하도록 함
                Debug.LogError(
                    $"[PB4Adapter] {name}: ⛔ FactionCombatProfileSO가 Inspector에서 미할당입니다!\n" +
                    "  → Inspector > Combat Profile 필드에 SO를 할당하세요.\n" +
                    "  → 예: Skeleton_T1_CombatProfile.asset\n" +
                    "  → 임시로 안전한 기본값을 적용합니다. 실제 전투 파라미터와 다를 수 있습니다.");

                SetBB("StalkSpeed", 3.5f);  // 접근 이동 속도
                SetBB("EngageRange", 5.0f);  // 교전 시작 거리
                SetBB("OrbitRadius", 3.0f);  // 선회 반경
                SetBB("StrafeAngularSpeed", 60.0f); // 선회 각속도 (도/초)
                SetBB("StrikeTriggerTime", 1.5f);  // 공격 트리거 시간 (초)
                SetBB("FleeSprintSpeed", 6.0f);  // 도주 최대 속도
                SetBB("AttackRange", 2.5f);       // [Bug 1 수정] 공격 사거리 기본값
                SetBB("PanicChainRadius", 0f);    // [Bug 3&4 수정] 패닉 전파 비활성 (안전)
                SetBB("PanicChainMultiplier", 0f);
            }

            // ── Step 5: SO → AIPerceptionSystem 인지 파라미터 주입 ───────────────
            // 설계 의도:
            //   FactionCombatProfileSO에 팩션별 인지 특성을 정의하고
            //   AIPerceptionSystem의 Inspector 하드코딩 값을 SO 값으로 덮어쓴다.
            //   이렇게 하면 고블린/오크/스켈레톤이 각기 다른 청각·시각 특성을 가진다.
            //
            // 주입 시점: InitializeBlackboard() — 게임 시작 시 1회.
            //   런타임 중 SO 값을 바꿔도 AIPerceptionSystem에 반영되지 않는다.
            //   재반영이 필요하면 ClearAllSessions() 후 재스폰.
            //
            // 주입 대상: AIPerceptionSystem 컴포넌트의 public 필드 직접 설정.
            //   BB가 아닌 컴포넌트 필드에 직접 쓰는 이유:
            //   AIPerceptionSystem은 BT Action이 아니므로 BB 포트가 없다.
            //   OnSoundHeard(), TickVision() 등 내부 메서드가 이 필드를 직접 읽는다.
            InjectPerceptionFromSO(combatProfile);

            // ── AttackState SO → BB 등록 ───────────────────────────────────────
            // StrikeAction이 AttackConfig BB 변수로 공격 애니메이션 ID를 읽습니다.
            // Blackboard에 "AttackStateConfig" (Object 타입) 변수를 등록해 두세요.
            var attackState = GetAttackState();
            if (attackState != null)
            {
                SetBB("AttackStateConfig", attackState);
            }
            else if (debugLog)
            {
                Debug.LogWarning(
                    $"[PB4Adapter] {name}: pursueState → combatStanceState → attackState " +
                    "체인 중 null 발견. StrikeAction이 AttackConfig BB를 읽을 수 없습니다.\n" +
                    "PursueTargetState SO의 combatStanceState/attackState 연결을 확인하세요.");
            }

            // ── CombatProfile ↔ FactionPolicyType 크로스 검증 ─────────────────────────
            // Goblin 프로파일을 Skeleton(Phalanx)에 잘못 적용하면
            // strikeTriggerTime이 Goblin 값(0.5초)으로 설정되어 비정상 전투 리듬이 발생합니다.
            if (combatProfile != null)
            {
                string soLower = combatProfile.name.ToLower();
                bool profileIsGoblin = soLower.Contains("goblin") || soLower.Contains("swarm");
                bool profileIsOrc = soLower.Contains("orc") || soLower.Contains("duel");
                bool profileIsSkeleton = soLower.Contains("skeleton") || soLower.Contains("phalanx");

                // [Enum 전환] 문자열 비교 → Enum 비교로 변경
                bool mismatch =
                    (policyType == FactionPolicyType.Phalanx && (profileIsGoblin || profileIsOrc)) ||
                    (policyType == FactionPolicyType.Duel && (profileIsGoblin || profileIsSkeleton)) ||
                    (policyType == FactionPolicyType.Swarm && (profileIsOrc || profileIsSkeleton));

                if (mismatch)
                    Debug.LogWarning(
                        $"<color=orange>[PB4Adapter] {name}: ⚠ CombatProfile/FactionPolicyType 불일치!</color>\n" +
                        $"  CombatProfile = '{combatProfile.name}'\n" +
                        $"  FactionPolicyType    = '{policyType}'\n" +
                        "  → 팩션에 맞는 CombatProfile SO를 할당하거나\n" +
                        "    Policy Type Override 필드를 수정하세요.\n" +
                        "  → 예: Skeleton → Skeleton_T1_CombatProfile + FactionPolicyType=Phalanx");
            }

            // ── AttackState attackActionID 사전 검증 ───────────────────────────
            // attackActionID가 Animator 전환 조건(ActionState)과 불일치하면
            // PlayTargetActionFunnel() 후 애니메이션이 재생되지 않아
            // isPerformingAction이 영구 true가 될 수 있습니다.
            var atkState = GetAttackState();
            if (atkState?.attackActions != null)
            {
                foreach (var action in atkState.attackActions)
                {
                    if (action.attackActionID <= 0)
                        Debug.LogWarning(
                            $"<color=orange>[PB4Adapter] {name}: ⚠ AttackState '{atkState.name}'의 " +
                            $"attackActionID={action.attackActionID}가 0 이하입니다.</color>\n" +
                            "  → Animator ActionState 전환 조건과 일치하는 양수값을 설정하세요.\n" +
                            "  → 예: Slash_Attack_Right 조건이 ActionState=2이면 attackActionID=2");
                }
            }

            if (debugLog)
                Debug.Log($"[PB4Adapter] {name}: BB 초기화 완료. FactionPolicyType={policyType}");
        }

        // =====================================================================
        // Update — 주기적 갱신
        // =====================================================================

        private void Update()
        {
            // [PATCH-4] 기존의 '첫 Update 지연 초기화' 블록을 단순 가드로 교체.
            // 초기화는 IEnumerator Start()가 담당합니다.
            if (!_bbInitialized) return;

            _timer += Time.deltaTime;
            if (_timer < updateInterval) return;
            _timer = 0f;

            UpdateUtilityDecision();
            SyncTarget();
            UpdateBlackboard();
            ProtectNavMeshInfra();
        }

        // =====================================================================
        // 유틸리티 계산
        // =====================================================================

        /// <summary>
        /// MobAIBrain 또는 HumanoidAIBrain의 UpdateDecision()을 호출합니다.
        /// 유틸리티 점수를 계산하여 CurrentState를 갱신합니다.
        /// </summary>
        private void UpdateUtilityDecision()
        {
            if (mobBrain != null) mobBrain.UpdateDecision();
            else if (humanoidBrain != null) humanoidBrain.UpdateDecision();
        }

        /// <summary>
        /// [타이밍 버그 수정] BB를 즉시 강제 동기화합니다.
        ///
        /// 배경:
        ///   UpdateBlackboard() 는 updateInterval(기본 0.5초)마다만 호출됩니다.
        ///   FleeDuelAction 처럼 BT Action 노드가 brain.fear / CurrentState 를 직접
        ///   변경한 뒤 즉시 BB 에 반영이 필요한 경우, 다음 틱까지 최대 0.5초 동안
        ///   BB UtilityWinner 가 이전 값(예: "Flee")으로 고정되어 BT 공전 루프가 발생함.
        ///
        /// 동작:
        ///   SyncTarget() + UpdateBlackboard() 를 즉시 실행하고
        ///   _timer 를 0으로 리셋하여 직후 중복 틱을 방지합니다.
        ///
        /// [Bug B 수정] UpdateUtilityDecision() 이중 호출 제거.
        ///   호출 전에 반드시 brain.UpdateDecision() 을 먼저 호출해야 합니다.
        ///   ForceSyncBB() 는 이미 계산 완료된 CurrentState 를 BB 에 복사하는 역할만 합니다.
        ///   이전 코드에서 UpdateUtilityDecision() 을 내부에서 재호출했으나,
        ///   이중 호출은 상태 변화 조건(newState != currentState)을 false 로 만들어
        ///   ExecuteBTNode() 를 누락시키는 부작용이 있으므로 제거합니다.
        ///
        /// 호출 시점 (올바른 순서):
        ///   1. brain.fear 조정
        ///   2. brain.UpdateDecision()       ← 상태 계산
        ///   3. adapter.ForceSyncBB()        ← BB 반영 (계산 없음)
        ///   예) FleeDuelAction.OnStart(), PerceptionProbeWindow.FireFearBoost()
        ///
        /// 주의:
        ///   _bbInitialized = false 인 Start() 코루틴 완료 이전에는 아무 동작도 하지 않습니다.
        /// </summary>
        public void ForceSyncBB()
        {
            if (!_bbInitialized) return;

            // [Bug B 수정] UpdateUtilityDecision() 제거.
            // 호출 측에서 이미 brain.UpdateDecision() 을 완료한 상태여야 합니다.
            // 이중 호출 시 두 번째 UpdateDecision 은 newState == currentState 이므로
            // ExecuteBTNode() 가 호출되지 않아 상태 전파가 불완전해집니다.

            // BB UtilityWinner / Fear / HasTarget / Target 즉시 갱신
            SyncTarget();
            UpdateBlackboard();

            // 직후 Update() 의 정규 틱이 중복 실행되지 않도록 타이머 리셋
            _timer = 0f;

            if (debugLog)
                Debug.Log($"[PB4Adapter] {name}: ForceSyncBB() 실행 — BB 즉시 동기화 완료.");
        }

        // =====================================================================
        // BB 갱신 (핵심 브릿지)
        // =====================================================================

        // =====================================================================
        // [Step 5] SO → AIPerceptionSystem 인지 파라미터 주입
        // =====================================================================
        /// <summary>
        /// FactionCombatProfileSO의 인지 파라미터를 AIPerceptionSystem에 주입합니다.
        /// InitializeBlackboard()에서 1회 호출됩니다.
        ///
        /// 왜 BB가 아닌 컴포넌트 직접 주입인가:
        ///   AIPerceptionSystem은 BT Action이 아닌 MonoBehaviour이므로 BB 포트가 없다.
        ///   OnSoundHeard(), TickVision() 등이 컴포넌트 필드를 직접 읽으므로
        ///   필드를 덮어쓰는 방식이 가장 간결하고 안전하다.
        /// </summary>
        private void InjectPerceptionFromSO(FactionCombatProfileSO profile)
        {
            var perception = GetComponent<AIPerceptionSystem>();
            if (perception == null)
            {
                if (debugPerceptionInject)
                    Debug.LogWarning(
                        $"<color=orange>[PerceptionInject] {name}: " +
                        $"AIPerceptionSystem 컴포넌트가 없습니다. 인지 파라미터 주입 스킵.</color>\n" +
                        "  → AI 프리팹에 AIPerceptionSystem 컴포넌트를 추가하세요.");
                return;
            }

            if (profile == null)
            {
                // SO 미할당 — Inspector 기본값 그대로 사용, 경고만 출력
                if (debugPerceptionInject)
                    Debug.LogWarning(
                        $"<color=orange>[PerceptionInject] {name}: " +
                        $"FactionCombatProfileSO 미할당. AIPerceptionSystem Inspector 기본값 유지.</color>\n" +
                        "  → 팩션별 인지 특성이 반영되지 않습니다.");
                return;
            }

            // ── 청각 파라미터 주입 ─────────────────────────────────────────────
            float prevHearingRange = perception.hearingRange;
            float prevSensitivity = perception.hearingSensitivity;
            float prevThreshold = perception.perceptionThreshold;
            float prevSuspicionDecay = perception.suspicionDecayTime;
            float prevAlertDecay = perception.alertDecayTime;
            float prevVisionRange = perception.visionRange;
            float prevVisionAngle = perception.visionAngle;
            float prevTraceRange = perception.traceDetectionRange;

            perception.hearingRange = profile.perceptionHearingRange;
            perception.hearingSensitivity = profile.perceptionHearingSensitivity;
            perception.perceptionThreshold = profile.perceptionThreshold;
            perception.suspicionDecayTime = profile.perceptionSuspicionDecayTime;
            perception.alertDecayTime = profile.perceptionAlertDecayTime;
            perception.visionRange = profile.perceptionVisionRange;
            perception.visionAngle = profile.perceptionVisionAngle;
            perception.traceDetectionRange = profile.perceptionTraceDetectionRange;

            // ── 검증 로그 ─────────────────────────────────────────────────────
            // debugPerceptionInject=true 시 SO값이 올바르게 주입됐는지 콘솔로 확인 가능.
            // 값이 Inspector 기본값과 같으면 △ 경고 (SO 미수정 의심).
            if (debugPerceptionInject)
            {
                bool hearingOk = !Mathf.Approximately(profile.perceptionHearingRange, 30f)
                                  || profile.perceptionHearingRange == 0f;
                bool sensitivityOk = !Mathf.Approximately(profile.perceptionHearingSensitivity, 0.5f);
                bool visionOk = !Mathf.Approximately(profile.perceptionVisionRange, 20f);

                string hearingCheck = hearingOk ? "<color=#44FF88>✓</color>" : "<color=#FFDD44>△ 기본값</color>";
                string sensitivityCheck = sensitivityOk ? "<color=#44FF88>✓</color>" : "<color=#FFDD44>△ 기본값</color>";
                string visionCheck = visionOk ? "<color=#44FF88>✓</color>" : "<color=#FFDD44>△ 기본값</color>";

                bool anyDefaultLeft = !hearingOk || !sensitivityOk || !visionOk;

                Debug.Log(
                    $"<color=#CC88FF>[PerceptionInject] {name} ← {profile.name}</color>\n" +
                    $"  <color=#AABBCC>청각범위</color>  : {prevHearingRange:F0}m → <color=#FFFFFF>{profile.perceptionHearingRange:F0}m</color> {hearingCheck}\n" +
                    $"  <color=#AABBCC>청각민감도</color>: {prevSensitivity:F2} → <color=#FFFFFF>{profile.perceptionHearingSensitivity:F2}</color> {sensitivityCheck}\n" +
                    $"  <color=#AABBCC>최소지각값</color>: {prevThreshold:F2} → <color=#FFFFFF>{profile.perceptionThreshold:F2}</color>\n" +
                    $"  <color=#AABBCC>Suspicion감쇄</color>: {prevSuspicionDecay:F0}s → <color=#FFFFFF>{profile.perceptionSuspicionDecayTime:F0}s</color>\n" +
                    $"  <color=#AABBCC>Alert감쇄</color> : {prevAlertDecay:F0}s → <color=#FFFFFF>{profile.perceptionAlertDecayTime:F0}s</color>\n" +
                    $"  <color=#AABBCC>시야범위</color>  : {prevVisionRange:F0}m → <color=#FFFFFF>{profile.perceptionVisionRange:F0}m</color> {visionCheck}\n" +
                    $"  <color=#AABBCC>시야각</color>    : {prevVisionAngle:F0}° → <color=#FFFFFF>{profile.perceptionVisionAngle:F0}°</color>\n" +
                    $"  <color=#AABBCC>자취범위</color>  : {prevTraceRange:F0}m → <color=#FFFFFF>{profile.perceptionTraceDetectionRange:F0}m</color>\n" +
                    (anyDefaultLeft
                        ? "  <color=#FFDD44>△ SO 인지 파라미터 일부가 기본값입니다. SO 에셋을 팩션에 맞게 수정하세요.</color>"
                        : "  <color=#44FF88>✓ 모든 인지 파라미터 주입 완료</color>")
                );
            }
        }

        /// <summary>
        /// 유틸리티 AI 결과를 BehaviorGraphAgent의 Blackboard에 기록합니다.
        /// Conditional Guard가 이 값을 읽어 BT 분기를 선택합니다.
        ///   UtilityWinner=="Attack" AND HasTarget==true → Attack 분기 통과
        /// 갱신 주기: updateInterval (기본 0.5초)
        /// </summary>
        private void UpdateBlackboard()
        {
            if (btAgent == null) return;

            // ── 유틸리티 결과 읽기 ──────────────────────────────────────────────
            string winner = "Idle";
            float fear = 0f;
            Transform target = null;

            if (mobBrain != null)
            {
                winner = mobBrain.CurrentState.ToString();
                fear = mobBrain.fear;
                target = mobBrain.currentTarget;
            }
            else if (humanoidBrain != null)
            {
                winner = humanoidBrain.CurrentState.ToString();
                fear = humanoidBrain.fear;
                target = humanoidBrain.currentTarget;
            }

            // ── [v3.3.8 §4 L1] Shared Target Fallback ──────────────────────────
            // 본인 currentTarget이 null이어도 같은 그룹의 다른 멤버가 발견한 적이
            // 있으면 sharedTarget으로 fallback. 이 BB["Target"] 값을 모든 BT 노드
            // (StalkAction, CircleStrafe 등)가 자연스럽게 활용 → 시야 밖 멤버도
            // 그룹 정보로 추격 시작 → 가까이 가면 본인 시야로 자체 currentTarget 획득.
            //
            // [v3.3.8 하이브리드 갱신] caller 전달 — 멤버별 캐시 (close/chain 정책 기반).
            if (target == null && mobBrain != null)
            {
                var grp = TDA.PB4.AI.GroupAIManager.FindGroupOwning(mobBrain);
                var shared = grp?.GetSharedTarget(mobBrain);
                if (shared != null)
                {
                    target = shared;
                }
            }

            // ── Stalk 실패 쿨다운 처리 ──────────────────────────────────────────────
            // BB["StalkBlocked"]=true 신호가 오면 쿨다운 타이머 시작.
            // 타이머 동안 winner를 "Patrol"로 강제 유지 → Attack 재진입 차단.
            // 쿨다운 종료 후 brain이 다시 Attack을 선택하면 자연스럽게 재진입.
            //
            // 구현: BB["StalkBlocked"] 변수를 직접 읽어 처리.
            //       StalkAction의 StalkBlockedOut 포트가 이 변수에 연결돼 있어야 함.
            var _stalkBlockedBB = btAgent.BlackboardReference;
            _stalkBlockedBB.GetVariable("StalkBlocked",
                out BlackboardVariable<bool> _stalkBlockedVar);

            if (_stalkBlockedVar != null && _stalkBlockedVar.Value)
            {
                // 신호 수신 → 타이머 시작, 신호 초기화
                _stalkBlockedTimer = STALK_BLOCKED_DURATION;
                _stalkBlockedVar.Value = false;
                if (debugLog)
                    Debug.Log(
                        $"<color=#FF9944>[Adapter▶STALK_BLOCKED]</color> {name}: " +
                        $"Stalk 실패 신호 수신. {STALK_BLOCKED_DURATION}s간 Attack 차단 시작.");
            }

            // ── Combat 진입 시 StalkBlocked 즉시 해제 ────────────────────────────
            // 문제: Stalk 우회 감지 → 쿨다운 5초 시작 → 그 5초 안에 타겟이 코앞와도
            //       winner="Patrol" 고정 → Attack 분기 진입 불가
            // 수정: 타겟이 생긴 순간(Combat 진입) 쿨다운을 즉시 취소.
            //       타겟이 있으면 쿨다운이 불필요하다.
            if (target != null && _stalkBlockedTimer > 0f)
            {
                if (debugLog)
                    Debug.Log(
                        $"<color=#44FF88>[Adapter▶STALK_UNBLOCKED_COMBAT]</color> {name}: " +
                        $"타겟 발견({(target != null ? target.name : "null")}) → " +
                        $"StalkBlocked 쿨다운 즉시 해제. 남은 시간={_stalkBlockedTimer:F1}s");
                _stalkBlockedTimer = 0f;
            }

            if (_stalkBlockedTimer > 0f)
            {
                _stalkBlockedTimer -= Time.deltaTime;
                winner = "Patrol";
                if (debugLog && _stalkBlockedTimer <= 0f)
                    Debug.Log(
                        $"<color=#44FF88>[Adapter▶STALK_UNBLOCKED]</color> {name}: " +
                        $"Stalk 차단 해제. Attack 재진입 허용.");
            }

            // ── [Gate G-3] Suspicious 상태를 BT Attack 분기에 전달 ──────────────────
            // 설계 의도 (기획서 v9 Gate G-3):
            //   소리/자취 감지 → Suspicious → 소리 위치로 수색 이동 → 발견 시 Combat
            //
            // 기존 문제:
            //   AIPerceptionSystem.Suspicious는 이 메서드에서 전혀 읽히지 않았다.
            //   타겟 없으면 winner가 항상 "Patrol"/"Idle" → Attack 분기 미진입
            //   → StalkAction의 LastHeardPosition 폴백 코드가 영원히 미실행
            //
            // 수정 내용:
            //   Suspicious 상태이고 타겟이 없을 때 winner를 "Attack"으로 오버라이드.
            //   HasTarget은 false 유지 → StalkAction이 LastHeardPosition 폴백 경로 사용.
            //   타겟이 생기면 (AIPerceptionSystem.Combat 전환 + currentTarget 설정)
            //   다음 UpdateBlackboard 사이클에서 원래 "Attack"+"HasTarget=true"로 복귀.
            // _perceptionSystem은 Start()에서 캐싱 (매 틱 GetComponent 비용 제거)
            bool _isSoundSearchMode = false;
            string _soundSearchReason = "";

            if (_perceptionSystem != null
                && _perceptionSystem.CurrentAwareness >= AwarenessState.Suspicious
                && target == null
                && winner != "Flee")  // 도주 중에는 소리 수색 모드 진입 안 함
            {
                _isSoundSearchMode = true;
                _soundSearchReason =
                    $"Awareness={_perceptionSystem.CurrentAwareness}" +
                    $" LastHeard={_perceptionSystem.LastKnownPosition:F1}";

                // winner 오버라이드 — Attack 분기 진입 허용
                // HasTarget=false이므로 StalkAction이 LastHeardPosition 폴백 사용
                winner = "Attack";

                // ── BB["LastHeardPosition"] 직접 갱신 ────────────────────────────
                // 문제: AIPerceptionSystem.lastKnownPosition(내부 필드)과
                //       BB["LastHeardPosition"](Blackboard 변수)은 별개로 관리된다.
                //       SoundPerception 컴포넌트가 없거나 BB 갱신이 누락되면
                //       StalkAction이 LastHeardPosition.Value == Vector3.zero 감지 →
                //       즉시 Status.Failure 반환 → BT가 Idle 분기로 폴백.
                // 수정: Adapter가 AIPerceptionSystem.LastKnownPosition을
                //       BB["LastHeardPosition"]에 직접 push.
                //       SoundPerception 미부착 환경에서도 StalkAction이 올바른
                //       목적지를 받을 수 있도록 보장.
                Vector3 _knownPos = _perceptionSystem.LastKnownPosition;
                if (_knownPos != Vector3.zero)
                {
                    SetBB("LastHeardPosition", _knownPos);
                    if (debugSoundSearch)
                        Debug.Log(
                            $"<color=#44CCFF>[SoundSearch▶POS]</color> {name}: " +
                            $"BB[LastHeardPosition] = <color=#FFE082>{_knownPos:F1}</color> " +
                            $"← AIPerceptionSystem.LastKnownPosition 직접 push");
                }
                else if (debugSoundSearch)
                    Debug.LogWarning(
                        $"<color=orange>[SoundSearch▶POS]</color> {name}: " +
                        $"LastKnownPosition == zero — StalkAction 목적지 없음!");
            }

            // ── BB 기록 ────────────────────────────────────────────────────────
            // [v3.4 §5 Stage 2.1] 전략적 후퇴 명령 — fear와 독립 BB push
            //   GroupAIManager.ReevaluateTacticalRetreat이 brain.OrderedRetreat=true 설정.
            //   여기서 BB[UtilityWinner]="Flee" 강제 → BT FleeDuel 분기 진입.
            //   Brain의 fear/CurrentState는 그대로 (의미 분리).
            bool orderedRetreat = (mobBrain != null && mobBrain.OrderedRetreat) ||
                                  (humanoidBrain != null && humanoidBrain.OrderedRetreat);
            if (orderedRetreat && winner != "Flee")
            {
                if (debugLog || verboseTacticalRetreat)
                    Debug.Log(
                        $"<color=#FFAA66>[Adapter▶TacticalRetreat]</color> {name}: " +
                        $"OrderedRetreat=true → BB[UtilityWinner] '{winner}' → 'Flee' 강제 push " +
                        $"(fear={fear:F2} 무관)");
                winner = "Flee";
            }

            SetBB("UtilityWinner", winner);
            SetBB("Fear", fear);
            SetBB("HasTarget", target != null);
            SetBB("OrderedRetreat", orderedRetreat);  // 디버깅/BT 검사용

            // [Fix-B] target이 null이어도 항상 기록 — Stale Transform 참조 방지
            // target이 파괴/이탈되어 null이 되어도 이전 프레임의 참조가 BB에 남으면
            // StalkAction.OnUpdate()에서 MissingReferenceException이 발생할 수 있습니다.
            SetBB("Target", target);

            // ── [Gate G-3] 청각 수색 모드 로그 (debugSoundSearch 플래그) ────────
            // 이 로그만 보면 소리 감지 → BT Attack 분기 진입 전 과정을 검증할 수 있다.
            //
            // 로그 종류:
            //   [SoundSearch▶START]  : Suspicious 감지 → Attack으로 오버라이드 진입
            //   [SoundSearch▶ACTIVE] : 수색 모드 유지 중 (0.5s마다 반복)
            //   [SoundSearch▶END]    : 타겟 발견 or Unaware 복귀로 수색 모드 해제
            if (debugSoundSearch)
            {
                bool _wasSearchMode = lastPB4State == "SoundSearch";

                if (_isSoundSearchMode && !_wasSearchMode)
                {
                    // 수색 모드 진입 (첫 프레임만 출력)
                    Debug.Log(
                        $"<color=#44CCFF>[SoundSearch▶START]</color> <color=#FFFFFF>{name}</color>\n" +
                        $"  <color=#AABBCC>원인</color> : <color=#FFE082>{_soundSearchReason}</color>\n" +
                        $"  <color=#AABBCC>동작</color> : <color=#69FF7A>UtilityWinner를 Attack으로 오버라이드 → StalkAction이 소리 위치로 이동 시작</color>\n" +
                        $"  <color=#AABBCC>게임의미</color> : <color=#FFFFFF>몹이 소리를 듣고 그쪽을 향해 걷기 시작함</color>");
                    lastPB4State = "SoundSearch";

                    // ── BT 즉시 재평가 강제 ─────────────────────────────────────────
                    // 문제: externallyTicked=true이면 BT는 Adapter의 0.5s 틱 때만 갱신된다.
                    //       BB winner="Attack"으로 바뀌어도 BT의 Conditional Observer가
                    //       즉시 감지하지 못해 Idle에 계속 머문다.
                    // 수정: 수색 모드 진입 첫 프레임에 BT를 강제로 한 번 더 틱해
                    //       "Abort On Lower Priority" Observer가 즉시 발동하도록 한다.
                    //       이후 프레임부터는 BT가 Attack 분기에서 Running이므로 추가 틱 불필요.
                    if (btAgent != null && btAgent.enabled)
                    {
                        btAgent.Update();
                        if (debugSoundSearch)
                            Debug.Log(
                                $"<color=#44CCFF>[SoundSearch▶TICK]</color> {name}: " +
                                $"BT 강제 틱 — Abort On Lower Priority 즉시 발동.");
                    }
                }
                else if (_isSoundSearchMode && _wasSearchMode)
                {
                    // 수색 모드 유지 — 매 갱신마다 출력하면 도배되므로 5회에 1회만
                    _soundSearchLogCounter = (_soundSearchLogCounter + 1) % 5;
                    if (_soundSearchLogCounter == 0)
                    {
                        Debug.Log(
                            $"<color=#44CCFF>[SoundSearch▶ACTIVE]</color> <color=#FFFFFF>{name}</color>" +
                            $"  <color=#AABBCC>Awareness</color>=<color=#FFE082>{_perceptionSystem?.CurrentAwareness}</color>" +
                            $"  <color=#AABBCC>LastKnown</color>=<color=#FFE082>{_perceptionSystem?.LastKnownPosition:F1}</color>" +
                            $"  <color=#AABBCC>Winner</color>=<color=#69FF7A>Attack(수색)</color>");
                    }
                }
                else if (!_isSoundSearchMode && _wasSearchMode)
                {
                    // 수색 모드 해제
                    string _exitReason = target != null
                        ? $"타겟 발견! ({target.name})"
                        : $"Awareness={_perceptionSystem?.CurrentAwareness} (Unaware 복귀 or 도주)";
                    Debug.Log(
                        $"<color=#FF9944>[SoundSearch▶END]</color> <color=#FFFFFF>{name}</color>\n" +
                        $"  <color=#AABBCC>해제 사유</color> : <color=#FFE082>{_exitReason}</color>\n" +
                        $"  <color=#AABBCC>게임의미</color> : <color=#FFFFFF>" +
                        (target != null
                            ? "플레이어를 발견해 정식 전투 모드로 전환됨"
                            : "수색 시간 초과 또는 도주로 수색 종료") +
                        "</color>");
                    // lastPB4State는 아래 일반 갱신 로그에서 winner로 업데이트됨
                }
            }

            // ── 기록 후 읽기 검증 (debugLog 활성 시) ──────────────────────────
            if (debugLog)
            {
                var bb = btAgent.BlackboardReference;
                bb.GetVariable("UtilityWinner", out BlackboardVariable<string> winnerVar);
                bb.GetVariable("Fear", out BlackboardVariable<float> fearVar);
                bb.GetVariable("HasTarget", out BlackboardVariable<bool> hasTgtVar);

                string readWinner = winnerVar?.Value ?? "NULL_VAR";
                float readFear = fearVar?.Value ?? -1f;
                bool readHasTgt = hasTgtVar?.Value ?? false;

                bool mismatch =
                    readWinner != winner ||
                    !Mathf.Approximately(readFear, fear) ||
                    readHasTgt != (target != null);

                if (mismatch)
                    Debug.LogError(
                        $"[PB4Adapter] {name}: BB 기록/읽기 불일치!\n" +
                        $"  UtilityWinner : wrote={winner}      read={readWinner}\n" +
                        $"  Fear          : wrote={fear:F3}     read={readFear:F3}\n" +
                        $"  HasTarget     : wrote={target != null} read={readHasTgt}");

                if (lastPB4State != winner || _bbDebugCount < 3)
                {
                    Debug.Log(
                        $"[PB4Adapter] {name}: BB갱신 " +
                        $"Winner={readWinner} Fear={readFear:F2} " +
                        $"HasTgt={readHasTgt} Tgt={(target != null ? target.name : "null")}");
                    _bbDebugCount++;
                }
            }

            // ── fear → NavMesh 속도 보간 ───────────────────────────────────────
            if (syncSpeedWithFear && aiManager.navMeshAgent != null)
                aiManager.navMeshAgent.speed = Mathf.Lerp(baseSpeed, maxSpeed, fear);

            // [Fix-E] canMove/canRotate 조건부 복원 — isPerformingAction 보호
            // 이전 코드에서 매 틱마다 isPerformingAction=false 강제 설정했던 문제를 수정.
            // StrikeAction이 공격 중(isPerformingAction=true)일 때는 이동 플래그를 건드리지 않습니다.
            bool isMovementState =
                winner == "Attack" || winner == "Flee" ||
                winner == "Patrol" || winner == "Move";

            if (isMovementState && !aiManager.isPerformingAction)
            {
                aiManager.canMove = true;
                aiManager.canRotate = true;
            }

            lastPB4State = winner;
        }

        // =====================================================================
        // NavMesh 인프라 보호
        // =====================================================================

        /// <summary>
        /// I-01~I-08 디버깅 세션에서 확정된 NavMesh 보호 로직.
        /// [I-03] updatePosition=false 강제 — RootMotion과 NavMesh 위치 갱신 충돌 방지.
        /// [I-06] RootMotion 비활성 시 nextPosition 폴백 동기화.
        /// </summary>
        private void ProtectNavMeshInfra()
        {
            var nav = aiManager.navMeshAgent;
            if (nav == null) return;

            // [I-03] RootMotion이 위치를 갱신하므로 NavMeshAgent의 자동 위치 갱신 비활성화
            if (nav.updatePosition)
                nav.updatePosition = false;

            if (!nav.isOnNavMesh) return;

            // [I-06] RootMotion 비활성 상태에서만 폴백으로 nextPosition 동기화
            // RootMotion 활성 시에는 OnAnimatorMove()에서 처리합니다.
            if (aiManager.animator != null && !aiManager.animator.applyRootMotion)
                nav.nextPosition = aiManager.transform.position;
        }

        // =====================================================================
        // 타겟 동기화
        // =====================================================================

        /// <summary>
        /// brain.currentTarget → AICharacterCombatManager.currentTarget 동기화.
        /// AICharacterCombatManager의 currentTarget이 있어야 기존 전투 판정(거리/CanAttack)이 동작합니다.
        /// </summary>
        private void SyncTarget()
        {
            Transform pb4Target = mobBrain?.currentTarget ?? humanoidBrain?.currentTarget;

            if (aiManager?.aiCharacterCombatManager == null) return;

            if (pb4Target != null)
            {
                var targetChar = pb4Target.GetComponent<CharacterManager>();
                if (targetChar != null)
                    aiManager.aiCharacterCombatManager.currentTarget = targetChar;
            }
            else
            {
                aiManager.aiCharacterCombatManager.currentTarget = null;
            }
        }

        // =====================================================================
        // BB 래퍼 — Graph 런타임 Blackboard 직접 접근
        // =====================================================================

        /// <summary>
        /// BehaviorGraphAgent의 런타임 Blackboard에 값을 설정합니다.
        /// SetVariableValue()로 BT 노드 실행용 BB에 기록하고,
        /// [Fix-C] Reflection으로 Inspector 표시용 m_BlackboardOverrides도 동기화합니다.
        /// </summary>
        /// <typeparam name="T">Blackboard 변수 타입.</typeparam>
        /// <param name="key">변수 이름.</param>
        /// <param name="value">설정할 값.</param>
        private void SetBB<T>(string key, T value)
        {
            if (btAgent == null) return;

            // ── [공유 BB 진단] BT 런타임이 Shared 변수를 읽는지 확인 ─────────────────
            // 로그에서 Orc_02 BT:Winner=Flee (Orc_01이 쓴 값) / pB4:Attack 불일치가 보이면
            // UtilityWinner 등이 Shared 변수임. 모든 오크가 같은 값을 읽음.
            // → BT 에디터에서 해당 변수의 Shared 체크를 해제해야 합니다.

            // 1단계: 인스턴스 레벨 override 먼저 시도 (Shared 변수 오염 차단)
            bool wroteInstance = TryWriteInstanceOverride(key, value);

            // 2단계: SetVariableValue (Shared 변수면 전체 오크에 전파됨)
            bool success = btAgent.SetVariableValue(key, value);
            if (!success && debugLog)
                Debug.LogWarning(
                    $"[PB4Adapter] {name}: SetVariableValue BB.{key}({typeof(T).Name}) 실패.\n" +
                    $"Blackboard에 '{key}' 변수가 정의되어 있는지 확인하세요.");

#if UNITY_EDITOR
            if (Application.isPlaying)
                SyncBlackboardOverrideForInspector(key, value);
#endif
        }

        /// <summary>
        /// [Shared BB 수정] 인스턴스 레벨 override를 직접 기록합니다.
        /// Shared 변수에 SetVariableValue를 쓰면 모든 에이전트에 전파되므로,
        /// 이 메서드로 per-agent 값을 먼저 설정하여 Shared 오염을 차단합니다.
        ///
        /// 작동 원리:
        ///   Unity Behavior는 GetVariable 시 m_BlackboardOverrides(인스턴스) 를
        ///   Shared BB보다 우선 읽습니다. 여기서 override 엔트리를 만들어두면
        ///   SetVariableValue가 Shared BB를 오염시켜도 이 에이전트는 자신의 값을 읽습니다.
        ///
        /// 실패 시 로그:
        ///   "[SHARED_BB] m_BlackboardOverrides 필드 없음" → Unity Behavior 버전 이슈.
        ///   → BT 에디터에서 해당 변수의 Shared 체크를 반드시 해제하세요.
        /// </summary>
        private bool TryWriteInstanceOverride<T>(string key, T value)
        {
            if (btAgent == null) return false;

            // GUID 획득
            if (!_bbGuidCache.TryGetValue(key, out var guid))
            {
                if (!btAgent.GetVariableID(key, out guid)) return false;
                _bbGuidCache[key] = guid;
            }

            // m_BlackboardOverrides 필드 최초 1회 탐색
            if (_bbOverridesField == null && !_bbOverrideFieldSearched)
            {
                _bbOverrideFieldSearched = true;
                _bbOverridesField = typeof(BehaviorGraphAgent).GetField(
                    "m_BlackboardOverrides",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (_bbOverridesField == null)
                {
                    // 필드명이 다를 수 있음 — 실제 필드명 열거해서 로그 출력
                    var allFields = typeof(BehaviorGraphAgent)
                        .GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
                    var matched = System.Array.FindAll(allFields,
                        f => f.Name.ToLower().Contains("blackboard") ||
                             f.Name.ToLower().Contains("override") ||
                             f.Name.ToLower().Contains("variable"));
                    var nameList = new string[matched.Length];
                    for (int i = 0; i < matched.Length; i++) nameList[i] = matched[i].Name;
                    var bbRelated = string.Join(", ", nameList);

                    Debug.LogError(
                        $"<color=red>[PB4Adapter][SHARED_BB] {name}: " +
                        $"m_BlackboardOverrides 필드를 찾지 못했습니다!\n" +
                        $"  Unity Behavior 버전({Application.unityVersion})에서 필드명이 변경됐을 수 있습니다.\n" +
                        $"  BehaviorGraphAgent 관련 필드: {(string.IsNullOrEmpty(bbRelated) ? "없음" : bbRelated)}\n" +
                        $"  → 인스턴스 override를 만들 수 없어 Shared BB 오염이 계속됩니다.\n" +
                        $"  → BT 에디터에서 UtilityWinner, Fear, HasTarget, Target 변수의\n" +
                        $"     [Shared] 체크를 해제하는 것이 근본 해결책입니다.</color>");
                }
                else if (debugLog)
                {
                    Debug.Log($"[PB4Adapter] {name}: m_BlackboardOverrides 필드 발견. 인스턴스 override 활성화.");
                }
            }

            if (_bbOverridesField == null) return false;

            var overrides = _bbOverridesField.GetValue(btAgent)
                as Dictionary<SerializableGUID, BlackboardVariable>;
            if (overrides == null) return false;

            if (overrides.TryGetValue(guid, out var existing))
            {
                existing.ObjectValue = value;
                return true;
            }

            if (!btAgent.GetVariable<T>(key, out var runtimeVar) || runtimeVar == null)
                return false;

            var newEntry = new BlackboardVariable<T>(value)
            {
                GUID = guid,
                Name = runtimeVar.Name,
            };
            overrides[guid] = newEntry;

            // List 동기화
            _bbListField ??= typeof(BehaviorGraphAgent).GetField(
                "m_BlackboardVariableOverridesList",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var list = _bbListField?.GetValue(btAgent) as List<BlackboardVariable>;
            if (list != null && !list.Exists(v => v != null && v.GUID == guid))
                list.Add(newEntry);

            return true;
        }

        /// <summary>TryWriteInstanceOverride의 GetField 탐색 완료 여부. 중복 탐색 방지.</summary>
        private bool _bbOverrideFieldSearched = false;

        // ── Reflection 캐시 (매 프레임 GetField 비용 방지) ──────────────────────
        private FieldInfo _bbOverridesField;
        private FieldInfo _bbListField;
        private readonly Dictionary<string, SerializableGUID> _bbGuidCache = new();

#if UNITY_EDITOR
        /// <summary>
        /// [Fix-C] m_BlackboardOverrides를 동기화하여 Inspector에 런타임 값을 실시간 표시합니다.
        /// </summary>
        private void SyncBlackboardOverrideForInspector<T>(string key, T value)
        {
            // GUID 캐시 조회 또는 신규 획득
            if (!_bbGuidCache.TryGetValue(key, out var guid))
            {
                if (!btAgent.GetVariableID(key, out guid)) return;
                _bbGuidCache[key] = guid;
            }

            // m_BlackboardOverrides 필드 획득 (1회 캐시)
            _bbOverridesField ??= typeof(BehaviorGraphAgent).GetField(
                "m_BlackboardOverrides",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (_bbOverridesField == null) return;

            var overrides = _bbOverridesField.GetValue(btAgent)
                as Dictionary<SerializableGUID, BlackboardVariable>;
            if (overrides == null) return;

            if (overrides.TryGetValue(guid, out var existing))
            {
                // 기존 엔트리 값 업데이트
                existing.ObjectValue = value;
            }
            else
            {
                // [Fix-③] 신규 엔트리 동적 생성
                // AddOverrideVariables()는 이름 갱신만 하고 신규 엔트리를 생성하지 않으므로
                // BlackboardVariable<T> 생성자로 직접 생성합니다.
                if (!btAgent.GetVariable<T>(key, out var runtimeVar) || runtimeVar == null)
                    return;

                var newEntry = new BlackboardVariable<T>(value)
                {
                    GUID = guid,
                    Name = runtimeVar.Name,
                };
                overrides[guid] = newEntry;

                // m_BlackboardVariableOverridesList 동기화 (직렬화 일관성)
                _bbListField ??= typeof(BehaviorGraphAgent).GetField(
                    "m_BlackboardVariableOverridesList",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var list = _bbListField?.GetValue(btAgent) as List<BlackboardVariable>;
                if (list != null && !list.Exists(v => v != null && v.GUID == guid))
                    list.Add(newEntry);

                if (debugLog)
                    Debug.Log(
                        $"[PB4Adapter] {name}: Inspector override 신규 엔트리 생성: " +
                        $"{key} ({typeof(T).Name})");
            }

            // [Fix-⑥] Inspector 강제 갱신 — SetDirty 없이는 패널이 다시 그려지지 않음
            EditorUtility.SetDirty(btAgent);
        }
#endif

        // =====================================================================
        // [Shared BB 진단] Shared 변수 탐지
        // =====================================================================

        /// <summary>
        /// UtilityWinner, Fear, HasTarget, Target이 Shared 변수인지 확인합니다.
        /// Shared이면 모든 NPC가 같은 값을 읽어 Flee↔Attack 진동이 발생합니다.
        ///
        /// 판정 원리 (DEBT-01 수정):
        ///   Unity Behavior 1.0.x에서 BlackboardVariable 객체의 런타임 타입을 확인합니다.
        ///   - BlackboardVariable&lt;T&gt;        → 일반(Per-Instance, 정상)
        ///   - SharedBlackboardVariable&lt;T&gt;  → Shared(공유, 문제)
        ///
        ///   이전 구현은 변수 이름만으로 판정하여 false positive 발생 (실제 [Shared] OFF인데
        ///   에러 출력). 이제 GetType().Name이 "SharedBlackboardVariable"로 시작하는지 확인합니다.
        ///
        /// 수정 방법(BT 에디터):
        ///   BT 에디터(MobAI_BehaviorGraph) → Blackboard 패널 →
        ///   해당 변수 선택 → 하단 [Shared] 토글 해제.
        /// </summary>
        private void CheckSharedVariables()
        {
            if (btAgent?.BlackboardReference?.Blackboard?.Variables == null) return;

            var perInstanceKeys = new[] { "UtilityWinner", "Fear", "HasTarget", "Target" };
            var seenKeys = new System.Collections.Generic.HashSet<string>();

            // 1) 그래프 BB의 모든 변수를 순회하며 타입 확인
            foreach (var v in btAgent.BlackboardReference.Blackboard.Variables)
            {
                if (v == null || string.IsNullOrEmpty(v.Name)) continue;
                if (System.Array.IndexOf(perInstanceKeys, v.Name) < 0) continue;

                seenKeys.Add(v.Name);

                // [DEBT-01 수정] 타입 기반 판정 — SharedBlackboardVariable<T>인지 확인
                // Unity Behavior 내부 타입이므로 typeof로 직접 참조 못 함 → Reflection 이름 비교
                string typeName = v.GetType().Name;
                bool isShared = typeName.StartsWith("SharedBlackboardVariable");

                if (isShared)
                {
                    Debug.LogError(
                        $"<color=red>[PB4Adapter][SHARED_BB] {name}: " +
                        $"'{v.Name}' 가 Shared(공유) 변수로 확인됨 (런타임 타입={typeName}).\n" +
                        $"  이 변수가 Shared이면 {name}이 쓴 값이 씬 내 모든 NPC에 전파됩니다.\n" +
                        $"  증상: Orc_01 공포 주입 → Orc_02/03 BT도 Flee 진입 → 전체 진동.\n" +
                        $"  ▶ 수정: BT 에디터(MobAI_BehaviorGraph) → Blackboard → '{v.Name}' 선택\n" +
                        $"           → 하단 [Shared] 토글 OFF\n" +
                        $"  이 수정 없이는 코드 레벨 픽스만으로 해결되지 않습니다.</color>");
                }
                else if (debugLog)
                {
                    Debug.Log($"[PB4Adapter][SHARED_BB] {name}: '{v.Name}' OK (Per-Instance, 타입={typeName}).");
                }
            }

            // 2) 그래프 BB에 변수 자체가 없는 경우 경고
            foreach (var key in perInstanceKeys)
            {
                if (!seenKeys.Contains(key))
                {
                    Debug.LogWarning(
                        $"[PB4Adapter][SHARED_BB] {name}: BB 변수 '{key}' 없음. " +
                        $"BT Blackboard에 추가하세요.");
                }
            }
        }

        // =====================================================================
        // FactionPolicyType 감지
        // =====================================================================

        /// <summary>
        /// 팩션의 FactionPolicyType(Swarm/Duel/Phalanx)을 감지합니다.
        /// 우선순위: Inspector 직접 지정 → GroupPolicy 컴포넌트(Week 3) → SO 이름 추론.
        /// [Enum 전환] 반환형 string → FactionPolicyType Enum (BT Switch 노드 호환)
        /// </summary>
        private FactionPolicyType DetectPolicyType()
        {
            // 1순위: Inspector 직접 지정
            if (!string.IsNullOrEmpty(policyTypeOverride))
            {
                string t = policyTypeOverride.Trim();
                // [Enum 전환] Enum.TryParse로 문자열→Enum 변환
                if (System.Enum.TryParse<FactionPolicyType>(t, true, out var parsed))
                    return parsed;
                Debug.LogWarning(
                    $"[PB4Adapter] {name}: policyTypeOverride='{t}'이 " +
                    "Swarm/Duel/Phalanx 중 하나가 아닙니다. SO 이름 감지로 폴백합니다.");
            }

            // 2순위: GroupPolicy 컴포넌트 자동 감지 (Week 3 이후 주석 해제)
            // if (GetComponent<PhalanxGroupPolicy>() != null) return FactionPolicyType.Phalanx;
            // if (GetComponent<DuelGroupPolicy>()    != null) return FactionPolicyType.Duel;
            // if (GetComponent<SwarmGroupPolicy>()   != null) return FactionPolicyType.Swarm;

            // 3순위: combatProfile SO 이름에서 추론
            if (combatProfile != null)
            {
                string n = combatProfile.name.ToLower();
                if (n.Contains("skeleton") || n.Contains("phalanx")) return FactionPolicyType.Phalanx;
                if (n.Contains("orc") || n.Contains("duel")) return FactionPolicyType.Duel;
                if (n.Contains("goblin") || n.Contains("swarm")) return FactionPolicyType.Swarm;

                Debug.LogWarning(
                    $"[PB4Adapter] {name}: SO 이름 '{combatProfile.name}'에서 팩션 감지 실패.\n" +
                    "Policy Type Override 필드에 직접 입력하거나 SO 이름에 팩션명을 포함시키세요.\n" +
                    "기본값 'Swarm'으로 설정됩니다.");
            }
            else
            {
                Debug.LogWarning(
                    $"[PB4Adapter] {name}: combatProfile 미할당. 기본값 'Swarm'으로 설정됩니다.");
            }

            return FactionPolicyType.Swarm;
        }

        // =====================================================================
        // 공개 API — BT Action 노드에서 접근
        // =====================================================================

        /// <summary>
        /// 이 AI의 AttackState SO를 반환합니다.
        /// pursueState → combatStanceState → attackState 체인으로 접근합니다.
        /// StrikeAction이 공격 애니메이션 ActionID를 읽기 위해 사용합니다.
        /// </summary>
        public AttackState GetAttackState() =>
            pursueState?.combatStanceState?.attackState;

        // =====================================================================
        // [PATCH-3]  DiagnoseBlackboard — 컨텍스트 메뉴 진단 도구
        // =====================================================================

#if UNITY_EDITOR
        /// <summary>
        /// BB 상태를 Console에 상세 출력합니다.
        /// 사용법: Inspector에서 PB4DecisionAdapter 컴포넌트 우클릭 → "Diagnose Blackboard"
        /// 플레이 모드에서만 유효합니다.
        /// </summary>
        [ContextMenu("Diagnose Blackboard")]
        private void DiagnoseBlackboard()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[PB4Adapter] DiagnoseBlackboard는 플레이 모드에서만 실행 가능합니다.");
                return;
            }

            if (btAgent == null)
            {
                Debug.LogError($"[PB4Adapter] {name}: btAgent가 null입니다.");
                return;
            }

            var bbRef = btAgent.BlackboardReference;
            if (bbRef?.Blackboard == null)
            {
                Debug.LogError(
                    $"[PB4Adapter] {name}: BlackboardReference 또는 Blackboard가 null입니다.\n" +
                    "BehaviorGraphAgent에 BehaviorGraph 에셋이 할당되어 있는지 확인하세요.");
                return;
            }

            Debug.Log($"[BB진단] ════ {name} 블랙보드 상태 ════");
            Debug.Log($"[BB진단] _bbInitialized : {_bbInitialized}");
            Debug.Log($"[BB진단] Graph          : {(btAgent.Graph != null ? "✓ 초기화됨" : "⚠ NULL")}");
            Debug.Log($"[BB진단] combatProfile  : {(combatProfile != null ? "✓ " + combatProfile.name : "⚠ NULL — Inspector 할당 필요")}");
            Debug.Log($"[BB진단] mobBrain       : {(mobBrain != null ? "✓ " + mobBrain.name : "없음")}");
            Debug.Log($"[BB진단] humanoidBrain  : {(humanoidBrain != null ? "✓ " + humanoidBrain.name : "없음")}");
            Debug.Log("[BB진단] ── 변수 목록 ──────────────────────");

            bool hasDefault = false;
            foreach (var v in bbRef.Blackboard.Variables)
            {
                string val = v.ObjectValue?.ToString() ?? "(null)";
                bool empty = v.ObjectValue == null
                            || val == "0" || val == "False"
                            || val == "0.000" || val == "";
                if (empty) hasDefault = true;
                Debug.Log(
                    $"[BB진단]   {v.Type.Name,-14} {v.Name,-22} = {val}" +
                    (empty ? "  ⚠ 기본/미설정값" : "  ✓"));
            }

            if (hasDefault)
                Debug.LogWarning(
                    "[BB진단] ⚠ 기본값 변수 있음.\n" +
                    "원인 1: combatProfile 미할당 → Inspector에서 SO 할당\n" +
                    "원인 2: Start 코루틴 아직 완료 안 됨 → 잠시 후 재확인\n" +
                    "원인 3: BT가 디버그 모드로 미연결 → BG Editor Debug 버튼 클릭");
            else
                Debug.Log("[BB진단] ✓ 모든 변수에 값이 정상 설정됨.");

            Debug.Log("[BB진단] ════════════════════════════════════");
        }
#endif

        // =====================================================================
        // Gizmo
        // =====================================================================

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmo || !Application.isPlaying) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = Color.green }
            };
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 2.5f,
                $"[BT:{lastPB4State}]",
                style);
        }
#endif
    }
}