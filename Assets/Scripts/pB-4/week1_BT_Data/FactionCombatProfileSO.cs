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
    }
}
