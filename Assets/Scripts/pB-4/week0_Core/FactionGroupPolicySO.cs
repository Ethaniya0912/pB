using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(menuName = "pB4/AI/FactionGroupPolicy")]
    public class FactionGroupPolicySO : ScriptableObject
    {
        public string factionId;
        public float panicChainMultiplier = 1.0f;
        public EscalationMode escalationMode = EscalationMode.TimeGated;
        public float leaderInfluenceRadius = 15f;
        public MessengerPolicy messengerPolicy = MessengerPolicy.OnRetreat;
        public FormationTemplate formationTemplate = FormationTemplate.Swarm;
        public bool tokenOverrideEnabled = false;
        public float tokenOverrideThreshold = 0f;
        public List<RoleTransitionRule> roleTransitionRules = new List<RoleTransitionRule>();

        // ════════════════════════════════════════════════════════════════
        // [v3.3.8 §4 L1] Shared Target 전파 정책 — 하이브리드 모델
        //
        // 가까운 거리: close radius 내 멤버는 무조건 전파 (텔레파시 추상화)
        // 먼 거리: chain max radius 내 멤버는 시야 체인 검사 후 전파
        //   - spotter → member 직선상에 sightObstacleLayers와 충돌 없으면 전파
        //   - 충돌하면 차단 (벽 뒤 멤버 등은 알 수 없음)
        // chain max radius 외: 전파 안 함
        //
        // 펙션별 디자인 의도:
        //   Skeleton (조직 8): close 30m + chain 80m — 마법적 연결, 강력
        //   Orc (조직 5):     close 15m + chain 40m — 외침 + 시야
        //   Goblin (조직 3):  close 10m + chain 25m — 좁은 무리
        // ════════════════════════════════════════════════════════════════

        [Header("Shared Target Propagation (§4 L1)")]
        [Tooltip("[Shared Target] 이 반경 내 같은 그룹 멤버는 시야 무관하게 즉시 전파. " +
                 "단위: 미터. 기본 15m.")]
        public float sharedTargetCloseRadius = 15f;

        [Tooltip("[Shared Target] 이 반경 내 멤버는 시야 체인이 닿으면 전파. " +
                 "close radius 외 + chain max 내 멤버에게 적용. " +
                 "0 또는 음수면 시야 체인 비활성 (close 외 멤버 전파 안 함). 기본 50m.")]
        public float sharedTargetChainMaxRadius = 50f;

        [Tooltip("[Shared Target] 시야 체인 검사 시 차단 Layer. " +
                 "기본 = 모든 Layer (Default 등 장애물 포함). " +
                 "지형/벽만 차단하려면 Default Layer만 선택.")]
        public LayerMask sharedTargetSightObstacleLayers = ~0;

        [Tooltip("[Shared Target] 정보 유효 시간(초). 이 시간 지나면 폐기. 기본 8초.")]
        public float sharedTargetTimeout = 8f;

        // =====================================================================
        // [v3.4 §5 L1 Stage 2.1] Tactical Retreat — 그룹 차원 전략적 후퇴 명령
        //   fear(본능)와 별개. GroupAIManager가 1초 주기로 멤버 검사 후 명령.
        //
        //   펙션별 권장:
        //     Orc:      enabled=true,  hpThreshold=0.3 (HP 30% 미만 후퇴)
        //     Goblin:   enabled=false (fear로 충분 — 공포심 도주)
        //     Skeleton: enabled=false (후퇴 없음)
        // =====================================================================
        [Header("━━━ Tactical Retreat (§5 L1 Stage 2.1) ━━━")]

        [Tooltip("그룹 차원 전략적 후퇴 활성화. ON이면 GroupAIManager가 멤버 HP 모니터링 후 후퇴 명령 발행. " +
                 "fear와 별개의 합리적 후퇴.")]
        public bool orderedRetreatEnabled = false;

        [Range(0f, 1f)]
        [Tooltip("그룹이 멤버에게 후퇴 명령 발행하는 HP 비율 임계치. " +
                 "FactionCombatProfileSO.retreatHpThreshold(노드 분기용)와 별개 — 그룹 차원 명령용. " +
                 "오크=0.3 권장.")]
        public float orderedRetreatHpThreshold = 0.3f;
    }

    public enum EscalationMode { TimeGated, ThreatGated, EncircleGated }
    public enum MessengerPolicy { None, OnRetreat, Always }
    public enum FormationTemplate { Swarm, Duel, Phalanx, Custom }

    [Serializable]
    public struct RoleTransitionRule
    {
        public string fromRole;
        public string toRole;
        public float triggerThreshold;
    }
}