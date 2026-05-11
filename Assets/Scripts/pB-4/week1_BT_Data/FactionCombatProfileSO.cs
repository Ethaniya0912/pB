// =============================================================================
// FactionCombatProfileSO.cs  |  pB-4 팩션별 전투 리듬 파라미터
// 계층    : L3 Domain — AI 데이터
// 네임스페이스: TDA.PB4.Data
//
// 역할:
//   팩션×Tier별 전투 파라미터를 ScriptableObject로 관리합니다.
//   코드 수정 없이 Inspector에서 숫자만 바꾸면 전투 리듬이 변합니다.
//
// 인스턴스 예정:
//   Goblin_T1_CombatProfile.asset  — 빠른 돌진 + 짧은 배회 + 즉시 공격
//   Orc_T1_CombatProfile.asset     — 신중 접근 + 긴 배회 + 타이밍 공격
//   Skeleton_T1_CombatProfile.asset — 느린 포위 + 포위 완성 시 동시 공격
//
// Blackboard 연동:
//   PB4DecisionAdapter.Awake()에서 각 필드를
//   BehaviorGraphAgent.SetVariableValue()로 BB에 복사합니다.
//   예: agent.SetVariableValue("StalkSpeed", combatProfile.stalkSpeed);
//
// 생성 방법:
//   Project 창 → Create → pB-4 → Faction Combat Profile
// =============================================================================
using UnityEngine;

namespace TDA.PB4.Data
{
    /// <summary>
    /// 팩션×Tier별 전투 리듬 파라미터 SO.
    /// Attack 3단계(Stalk/CircleStrafe/Strike)와 Flee 행동의 타이밍을 정의합니다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewCombatProfile",
        menuName = "pB-4/Faction Combat Profile",
        order = 0)]
    public class FactionCombatProfileSO : ScriptableObject
    {
        // =====================================================================
        // Attack — Stalk (접근)
        // =====================================================================

        [Header("━━━ Stalk (접근) ━━━━━━━━━━━━━━━━━")]

        /// <summary>접근 속도 (m/s). 이 속도로 타겟에게 다가갑니다.</summary>
        [Tooltip("고블린=5.5, 오크=4.0, 스켈레톤=2.5")]
        public float stalkSpeed = 4f;

        /// <summary>Stalk→CircleStrafe 전환 거리 (m). 이 거리 이내 진입 시 배회 시작.</summary>
        [Tooltip("고블린=12, 오크=10, 스켈레톤=15")]
        public float engageRange = 10f;

        // =====================================================================
        // Attack — CircleStrafe (배회)
        // =====================================================================

        [Header("━━━ CircleStrafe (배회) ━━━━━━━━━━━━")]

        /// <summary>공격 사거리 (m). 이 거리 이내에서 Strike 가능.</summary>
        [Tooltip("실제 공격이 닿는 거리")]
        public float attackRange = 2.5f;

        /// <summary>배회 반경 (m). 타겟 중심 원형 궤도의 반지름.</summary>
        [Tooltip("고블린=3, 오크=4, 스켈레톤=5(포위)")]
        public float orbitRadius = 4f;

        /// <summary>배회 각속도 (°/s). 클수록 빠르게 배회.</summary>
        [Tooltip("고블린=80, 오크=40, 스켈레톤=20")]
        public float strafeAngularSpeed = 40f;

        /// <summary>배회→Strike 전환 대기 시간 (초). 이 시간 경과 후 공격 시작.</summary>
        [Tooltip("고블린=0.5, 오크=2.0, 스켈레톤=Infinity(포위 완성까지)")]
        public float strikeTriggerTime = 2f;

        // =====================================================================
        // Flee (도주)
        // =====================================================================

        [Header("━━━ Flee (도주) ━━━━━━━━━━━━━━━━━━")]

        /// <summary>도주 전력질주 속도 (m/s). 0이면 도주 불가 팩션.</summary>
        [Tooltip("고블린=6.0, 오크=0(반격), 스켈레톤=0(도주불가)")]
        public float fleeSprintSpeed = 6f;

        /// <summary>패닉 전파 범위 (m). OverlapSphere 범위.</summary>
        [Tooltip("고블린=5(넓은 전파), 오크=3(좁은 전파), 스켈레톤=0(전파 없음)")]
        public float panicChainRadius = 5f;

        /// <summary>패닉 전파 배율. 주변 동료의 fear에 이 값을 곱해서 추가.</summary>
        [Tooltip("고블린=2.0(강한 전파), 오크=0.5(약한), 스켈레톤=0")]
        public float panicChainMultiplier = 2f;

        /// <summary>
        /// [v3.4 §5 L1 Stage 2] 후퇴 트리거 HP 임계치 (0.0 ~ 1.0).
        /// 현재 HP / max HP 비율이 이 값 미만이면 안전지대로 후퇴.
        /// 0이면 후퇴 없음 (예: 스켈레톤 - 죽을 때까지 싸움).
        /// FleeDuelAction(오크): HP 정상이면 반격, HP &lt; threshold면 후퇴 분기.
        /// FleeSwarmAction(고블린): HP 무관 (Stage 3에서 검토).
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("후퇴 트리거 HP 임계치 (0~1). 오크=0.3, 고블린=0.5, 스켈레톤=0(후퇴X)")]
        public float retreatHpThreshold = 0.3f;

        // =====================================================================
        // 지형 보정 (fear 가산)
        // =====================================================================

        [Header("━━━ 지형 Fear 보정 ━━━━━━━━━━━━━━━")]

        /// <summary>NarrowPath 지형에서 fear 가산값.</summary>
        [Tooltip("좁은 통로에서 fear 증가. 고블린=0.15")]
        public float fearBonusNarrowPath = 0.15f;

        /// <summary>DeathTrap 지형에서 fear 가산값.</summary>
        [Tooltip("막다른 길에서 fear 급증. 고블린=0.25")]
        public float fearBonusDeathTrap = 0.25f;

        /// <summary>SpookyCave 지형에서 fear 가산값.</summary>
        [Tooltip("으스스한 동굴에서 fear 소폭 증가. 고블린=0.10")]
        public float fearBonusSpookyCave = 0.10f;

        // =====================================================================
        // Step 1: CircleStrafe 타이밍 파라미터
        // =====================================================================

        [Header("━━━ CircleStrafe 타이밍 ━━━━━━━━━━━━━━━━")]

        /// <summary>CloseIn 단계 최대 시간 (초). 기본 3f</summary>
        [Tooltip("CloseIn 최대 시간. 기본 3f")]
        public float closeInMaxTime = 3f;

        /// <summary>페인트 최소 간격 (초). 기본 2f</summary>
        [Tooltip("페인트 최소 간격. 기본 2f")]
        public float feintIntervalMin = 2f;

        /// <summary>페인트 최대 간격 (초). 기본 5f</summary>
        [Tooltip("페인트 최대 간격. 기본 5f")]
        public float feintIntervalMax = 5f;

        /// <summary>가드 브레이크 반응 딜레이 (초). 기본 0.25f</summary>
        [Tooltip("가드 브레이크 반응 딜레이. 기본 0.25f")]
        public float guardBreakReactionDelay = 0.25f;

        /// <summary>체력 공격 최소 간격 (초). 기본 2.5f</summary>
        [Tooltip("체력 공격 최소 간격. 기본 2.5f")]
        public float poiseAttackIntervalMin = 2.5f;

        /// <summary>체력 공격 최대 간격 (초). 기본 5.5f</summary>
        [Tooltip("체력 공격 최대 간격. 기본 5.5f")]
        public float poiseAttackIntervalMax = 5.5f;

        // =====================================================================
        // Step 2: Flee 파라미터
        // =====================================================================

        [Header("━━━ Flee Stuck 감지 ━━━━━━━━━━━━━━━━━━")]

        /// <summary>Stuck GiveUp 시 brain.fear를 이 값으로 낮춰 Attack 강제 전환.</summary>
        [Tooltip("Stuck 포기 후 Attack으로 강제 전환할 fear 값. 기본 0.15f\n※ 팩션 FleeThreshold보다 반드시 낮게 설정")]
        [Range(0f, 0.3f)]
        public float forceAttackFear = 0.15f;


        /// <summary>Stuck 체크 주기 (초). 기본 0.5f</summary>
        [Tooltip("FleeSwarm: 위치 비교 주기. 기본 0.5f")]
        public float stuckCheckInterval = 0.5f;

        /// <summary>Stuck 이동 임계값 (m). 기본 0.3f</summary>
        [Tooltip("FleeSwarm: 이 거리 미만이면 stuck 판정. 기본 0.3f")]
        public float stuckMoveThreshold = 0.3f;

        /// <summary>Stuck 포기 횟수. 기본 4</summary>
        [Tooltip("FleeSwarm: 이 횟수 이상 stuck이면 Failure. 기본 4 (1=즉시, 10=끈질김)")]
        [Range(1, 10)]
        public int stuckGiveUpCount = 4;

        [Header("━━━ FleeDuel 반격 ━━━━━━━━━━━━━━━━━━━━")]

        /// <summary>반격 진입 시 초기 fear 값. 기본 0.25f</summary>
        [Tooltip("FleeDuel: 반격 전환 시 brain.fear 설정값. 기본 0.25f")]
        public float fleeDuelInitialFear = 0.25f;

        /// <summary>InitialFear 상한값. 기본 0.28f</summary>
        [Tooltip("FleeDuel: InitialFear 초과 시 강제 보정 상한. 기본 0.28f")]
        public float fleeDuelMaxInitialFear = 0.28f;

        // =====================================================================
        // Step 3: Strike + 지형 보정 멀티플라이어
        // =====================================================================

        [Header("━━━ Strike 파라미터 ━━━━━━━━━━━━━━━━━━━")]

        /// <summary>공격 회전 속도. 기본 8f</summary>
        [Tooltip("StrikeAction: 타겟 방향 회전 속도. 기본 8f")]
        public float attackRotationSpeed = 8f;

        /// <summary>공격 타임아웃 (초). 기본 3f</summary>
        [Tooltip("StrikeAction: 공격 모션 최대 대기 시간. 기본 3f")]
        public float attackTimeout = 3f;

        [Header("━━━ 지형 배율 보정 ━━━━━━━━━━━━━━━━━━━━")]

        /// <summary>NarrowPath 궤도 반경 배율. 기본 0.4f</summary>
        [Tooltip("좁은 통로에서 orbitRadius × 이 값. 기본 0.4f")]
        public float narrowPathOrbitMult = 0.4f;

        /// <summary>NarrowPath StrikeTriggerTime 배율. 기본 0.5f</summary>
        [Tooltip("좁은 통로에서 strikeTriggerTime × 이 값. 기본 0.5f")]
        public float narrowPathStrikeTimeMult = 0.5f;

        // =====================================================================
        // Step 4: TacticalReposition 파라미터
        // =====================================================================

        [Header("━━━ TacticalReposition ━━━━━━━━━━━━━━━━━")]

        [Tooltip("스태미나 회복 임계값. 이하면 Recover 모드. 기본 0.25f")]
        public float tacticalStaminaRecoverThreshold = 0.25f;

        [Tooltip("체력 회복 임계값. 이하면 Recover 모드. 기본 0.30f")]
        public float tacticalHealthRecoverThreshold = 0.30f;

        [Tooltip("후퇴 거리 (m). 기본 5f")]
        public float tacticalRetreatDist = 5f;

        [Tooltip("회복 대기 시간 (초). 기본 3f")]
        public float tacticalRecoverDuration = 3f;

        [Tooltip("회피 발동 확률 (0~1). 기본 0.2f")]
        public float tacticalEvasionChance = 0.2f;

        [Tooltip("회피 이동 거리 (m). 기본 2f")]
        public float tacticalEvasionDist = 2f;

        [Tooltip("회피 지속 시간 (초). 기본 0.5f")]
        public float tacticalEvasionDuration = 0.5f;

        [Tooltip("페인트 발동 확률 (0~1). 기본 0.15f")]
        public float tacticalFeintChance = 0.15f;

        [Tooltip("페인트 이동 거리 (m). 기본 1.5f")]
        public float tacticalFeintDist = 1.5f;

        [Tooltip("페인트 지속 시간 (초). 기본 0.4f")]
        public float tacticalFeintDuration = 0.4f;

        // =====================================================================
        // Step 5: 인지 특성 (Perception) 파라미터
        // =====================================================================
        // 설계 의도:
        //   팩션별 인지 특성을 SO로 정의해 AIPerceptionSystem에 주입한다.
        //   PB4DecisionAdapter.InitializeBlackboard()가 게임 시작 시 1회 push.
        //
        //   팩션별 특성 예시:
        //     고블린 — 청각 예민(hearingRange=35), 시야 넓음(visionAngle=150), 기억 짧음(suspicionDecay=5)
        //     오크   — 청각 둔감(hearingRange=20), 시야 좁음(visionAngle=90), 기억 길음(suspicionDecay=12)
        //     스켈레톤 — 청각 없음(hearingRange=0), 시야 작음(visionRange=10), 자취 무감지(traceRange=3)
        //
        //   실제 수치는 플레이테스트 기반으로 조정할 것.
        //   0으로 설정하면 해당 감각 비활성화.

        [Header("━━━ 인지 특성 (Perception) ━━━━━━━━━━━━━━")]

        [Tooltip("청각 감지 최대 범위(m). 0=청각없음. 고블린=35 / 오크=20 / 스켈레톤=0")]
        [Range(0f, 60f)]
        public float perceptionHearingRange = 30f;

        [Tooltip("청각 민감도(0~1). 높을수록 작은 소리도 감지. 고블린=0.7 / 오크=0.3 / 스켈레톤=0.0")]
        [Range(0f, 1f)]
        public float perceptionHearingSensitivity = 0.5f;

        [Tooltip("최소 지각값(0~1). 이 이하 무시. 낮을수록 예민. 고블린=0.1 / 오크=0.3 / 스켈레톤=1.0")]
        [Range(0.01f, 1f)]
        public float perceptionThreshold = 0.2f;

        [Tooltip("Suspicious->Unaware 감쇄 시간(초). 짧을수록 금방 잊음. 고블린=5 / 오크=12 / 스켈레톤=4")]
        [Range(1f, 30f)]
        public float perceptionSuspicionDecayTime = 8f;

        [Tooltip("Alert->Suspicious 감쇄 시간(초). 짧을수록 전투 전환 느림. 고블린=3 / 오크=6 / 스켈레톤=4")]
        [Range(1f, 20f)]
        public float perceptionAlertDecayTime = 5f;

        [Tooltip("시각 감지 최대 범위(m). 고블린=15 / 오크=25 / 스켈레톤=10")]
        [Range(5f, 50f)]
        public float perceptionVisionRange = 20f;

        [Tooltip("시야각(도). 전방 기준 좌우 합산. 고블린=150 / 오크=90 / 스켈레톤=60")]
        [Range(30f, 180f)]
        public float perceptionVisionAngle = 120f;

        [Tooltip("자취 마커 감지 범위(m). 0=무감지. 고블린=15 / 오크=8 / 스켈레톤=3")]
        [Range(0f, 30f)]
        public float perceptionTraceDetectionRange = 10f;

        [Tooltip("소리 수색 포기 거리(m). 이 거리 이내 도달 후 타겟 미발견이면 Patrol 복귀. 고블린=1.0 / 오크=2.0")]
        [Range(0.5f, 5f)]
        public float perceptionSoundSearchGiveUpDist = 1.5f;
    }
}