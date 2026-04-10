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

        [Tooltip("팩션 전투 프로파일 SO. Start에서 BB에 파라미터 복사.\n"
               + "미할당 시 안전 기본값이 설정되지만 실제 전투 파라미터와 다를 수 있습니다.")]
        [SerializeField] private FactionCombatProfileSO combatProfile;

        [Tooltip("FactionPolicyType을 SO 이름 감지 대신 직접 지정합니다.\n"
               + "비워두면 combatProfile SO 이름에서 자동 감지.\n"
               + "Swarm / Duel / Phalanx 중 하나를 입력하세요.\n"
               + "SO 이름에 팩션명이 없어서 잘못 감지될 때 사용하세요.")]
        [SerializeField] private string policyTypeOverride = "";

        // =====================================================================
        // Inspector — FSM 참조 (AttackState 체인 접근용)
        // =====================================================================

        [Header("━━━ AttackState 연결 (Strike 공격 체인) ━━")]

        [Tooltip("pursueState → combatStanceState → attackState 체인으로 공격 액션 취득.\n"
               + "StrikeAction이 공격 애니메이션 ID를 읽기 위해 사용합니다.")]
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

        // =====================================================================
        // Inspector — 디버그
        // =====================================================================

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("BB 갱신 로그를 Console에 출력합니다. 성능에 영향을 주므로 릴리즈 전 끄세요.")]
        [SerializeField] private bool debugLog = true;

        [Tooltip("SceneView에 현재 상태를 텍스트로 표시합니다.")]
        [SerializeField] private bool showGizmo = true;

        // =====================================================================
        // 내부 상태
        // =====================================================================

        /// <summary>BB 초기화 완료 여부. IEnumerator Start()가 true로 설정합니다.</summary>
        private bool _bbInitialized = false;

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

            SetBB("FactionPolicyType", policyType);
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
            }

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
        ///   UpdateUtilityDecision() → UpdateBlackboard() 를 즉시 실행하고
        ///   _timer 를 0으로 리셋하여 직후 중복 틱을 방지합니다.
        ///
        /// 호출 시점:
        ///   BT Action 노드의 OnStart() 말미에서 호출. 예) FleeDuelAction.OnStart().
        ///
        /// 주의:
        ///   _bbInitialized = false 인 Start() 코루틴 완료 이전에는 아무 동작도 하지 않습니다.
        /// </summary>
        public void ForceSyncBB()
        {
            if (!_bbInitialized) return;

            // brain.UpdateDecision() 재호출 포함 — CurrentState 최신값 보장
            UpdateUtilityDecision();

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

            // ── BB 기록 ────────────────────────────────────────────────────────
            SetBB("UtilityWinner", winner);
            SetBB("Fear", fear);
            SetBB("HasTarget", target != null);

            // [Fix-B] target이 null이어도 항상 기록 — Stale Transform 참조 방지
            // target이 파괴/이탈되어 null이 되어도 이전 프레임의 참조가 BB에 남으면
            // StalkAction.OnUpdate()에서 MissingReferenceException이 발생할 수 있습니다.
            SetBB("Target", target);

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
                        $"HasTgt={readHasTgt} Tgt={target?.name ?? "null"}");
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

            // 1단계: BT 노드 실행용 BB에 기록
            bool success = btAgent.SetVariableValue(key, value);
            if (!success && debugLog)
                Debug.LogWarning(
                    $"[PB4Adapter] {name}: SetVariableValue BB.{key}({typeof(T).Name}) 실패.\n" +
                    $"Blackboard에 '{key}' 변수가 정의되어 있는지 확인하세요.");

            // 2단계: [Fix-C] Inspector 실시간 표시용 m_BlackboardOverrides 동기화
#if UNITY_EDITOR
            if (Application.isPlaying)
                SyncBlackboardOverrideForInspector(key, value);
#endif
        }

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