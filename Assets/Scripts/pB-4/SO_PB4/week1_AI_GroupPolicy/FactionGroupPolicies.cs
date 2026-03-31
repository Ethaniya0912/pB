// =============================================================================
// FactionGroupPolicies.cs  |  pB-4 Project — Week 1
// Layer  : L3 Domain (AI)
// Owner  : Person A
//
// 역할:
//   IFactionGroupPolicy의 3종 구현체.
//   GroupAIManager(50줄 코디네이터)가 팩션별로 이 구현체를 디스패치하여
//   완전히 다른 군집 행동을 구현하면서도 God Script가 되지 않도록 한다.
//
//   SwarmGroupPolicy (고블린): 패닉 전파 2배, 시간 기반 에스컬레이션, 전령 파견
//   DuelGroupPolicy (오크): 명예 결투, 비겁 감지 시 All Striker, 사기 저하 적음
//   PhalanxGroupPolicy (스켈레톤): 사기 저하 0, 포위 완성 후 동시 전진, 무감정
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Interfaces.Intelligence;
using TDA.PB4.Data;

namespace TDA.PB4.AI.GroupPolicies
{
    // ====================================================================
    // 고블린: Swarm/Chaos — 혼란스러운 집단, 패닉이 빠르게 전파
    // ====================================================================
    public class SwarmGroupPolicy : MonoBehaviour, IFactionGroupPolicy
    {
        [Header("Swarm Parameters")]
        [SerializeField] private FactionGroupPolicySO policySO;

        private float panicMultiplier => policySO != null ? policySO.panicChainMultiplier : 2.0f;
        private float escalationTimer = 0f;
        private const float TIME_GATE_INTERVAL = 30f; // 30초마다 자동 에스컬레이션

        /// <summary>
        /// 사기 평가. 고블린은 동료 사망 시 패닉이 2배 속도로 전파된다.
        /// 하나가 도망치면 주변 5m 이내 동료 모두에게 fear가 전파.
        /// </summary>
        public float EvaluateMorale(float currentMorale, float individualLoss, float connectionWeight)
        {
            // 패닉 전파: 개체 손실 × 연결 가중치 × 패닉 배수(2.0)
            float moraleDrop = individualLoss * connectionWeight * panicMultiplier;
            float newMorale = Mathf.Max(0f, currentMorale - moraleDrop);

            // 사기가 0.2 미만이면 전체 도주 트리거
            return newMorale;
        }

        /// <summary>
        /// 에스컬레이션 결정. TimeGated: 30초마다 자동 위협 레벨 상승.
        /// 고블린은 시간이 지나면 자연스럽게 더 난폭해진다.
        /// </summary>
        public int DecideEscalation(float elapsedTime, float threatLevel, float encircleProgress)
        {
            // 시간 기반: 30초마다 1단계씩 상승 (최대 3)
            int timeEscalation = Mathf.Min(3, Mathf.FloorToInt(elapsedTime / TIME_GATE_INTERVAL));
            return timeEscalation;
        }

        /// <summary>
        /// 역할 할당. Swarm은 명확한 역할 분리 없이 혼란스럽게 행동.
        /// 모두 Striker로 설정하되, fear가 높은 개체는 자동으로 Flee.
        /// </summary>
        public void AssignRoles(List<int> memberIds)
        {
            // Swarm: 역할 분리 최소. 모두 공격/도주를 개별 유틸리티로 결정
            // GroupAIManager가 이 결과를 각 MobAIBrain에 반영
        }

        /// <summary>
        /// 패닉 체인. 한 마리가 도망치면 주변에 fear 전파.
        /// panicMultiplier=2.0이므로 전파 속도가 2배.
        /// 전파 범위: 리더 없으면 5m, 임시 리더 있으면 8m.
        /// </summary>
        public void HandlePanicChain(int fleeingMemberId, float panicMultiplier)
        {
            // 전파 반경 내 동료에게 fear 증가
            float fearIncrease = 0.2f * panicMultiplier;
            // TODO: GroupAIManager가 반경 내 동료 목록을 제공하면 각각에 fear 적용
            // 또한 OnRetreat 시 전령(Messenger) 태그 부여: 도망한 개체가 근처 주둔지로 달려가
            // 일정 시간 후 더 강한 Tier의 지원군을 데려옴
            Debug.Log($"[Swarm] 패닉 체인 발동: MemberId={fleeingMemberId}, fearIncrease={fearIncrease}. 전령 파견 대기.");
        }
    }

    // ====================================================================
    // 오크: Duel/Warrior_Pride — 명예 결투, 비겁한 수 감지
    // ====================================================================
    public class DuelGroupPolicy : MonoBehaviour, IFactionGroupPolicy
    {
        [Header("Duel Parameters")]
        [SerializeField] private FactionGroupPolicySO policySO;

        private bool duelDeclared = false;
        private int duelTargetId = -1;
        private bool cowardDetected = false;

        /// <summary>
        /// 사기 평가. 오크는 명예를 중시하여 사기 저하가 매우 느리다.
        /// panicChainMultiplier=0.5이므로 패닉 전파가 절반 속도.
        /// 단, 비겁한 수(아이템 사용, 뒤치기 등) 감지 시 분노로 전환.
        /// </summary>
        public float EvaluateMorale(float currentMorale, float individualLoss, float connectionWeight)
        {
            float panicMul = policySO != null ? policySO.panicChainMultiplier : 0.5f;
            float moraleDrop = individualLoss * connectionWeight * panicMul;

            // 비겁 감지 시 사기가 오히려 상승 (분노)
            if (cowardDetected)
            {
                return Mathf.Min(1.0f, currentMorale + 0.3f);
            }

            return Mathf.Max(0f, currentMorale - moraleDrop);
        }

        /// <summary>
        /// 에스컬레이션 결정. ThreatGated: 위협 수준이 임계치를 넘으면 에스컬레이션.
        /// 비겁한 수 감지 시 즉시 최대 에스컬레이션(All Striker).
        /// </summary>
        public int DecideEscalation(float elapsedTime, float threatLevel, float encircleProgress)
        {
            if (cowardDetected) return 3; // 비겁 감지 → 전원 All Striker

            // 위협 기반: threatLevel 0.3 이상마다 1단계
            return Mathf.Min(3, Mathf.FloorToInt(threatLevel / 0.3f));
        }

        /// <summary>
        /// 역할 할당. 결투 선포: 가장 강한 개체가 Duel Challenger로 나서고,
        /// 나머지는 Observer(관전)로 대기. 비겁 감지 시 전원 Striker 전환.
        /// </summary>
        public void AssignRoles(List<int> memberIds)
        {
            if (cowardDetected)
            {
                // 전원 Striker 모드: 모든 역할 제거, 즉시 전면 공격
                Debug.Log("[Duel] 비겁한 수 감지! 전원 All Striker 전환.");
                return;
            }

            if (!duelDeclared && memberIds.Count > 0)
            {
                // 첫 번째 개체가 결투 선포 (TODO: 가장 강한 개체 선정)
                duelTargetId = memberIds[0];
                duelDeclared = true;
                Debug.Log($"[Duel] 결투 선포: ChallengerId={duelTargetId}");
            }
        }

        /// <summary>
        /// 패닉 체인. 오크는 거의 도망치지 않으나, 도망 시에도 전파가 느림.
        /// panicChainMultiplier=0.5.
        /// </summary>
        public void HandlePanicChain(int fleeingMemberId, float panicMultiplier)
        {
            float fearIncrease = 0.1f * panicMultiplier; // 절반 속도
            Debug.Log($"[Duel] 동료 도주 감지: 명예 실추. 나머지 사기 미세 하락. fearIncrease={fearIncrease}");
        }

        // 외부에서 비겁 감지 시 호출
        public void NotifyCowardDetected() { cowardDetected = true; }
        public void ResetDuel() { duelDeclared = false; duelTargetId = -1; cowardDetected = false; }
    }

    // ====================================================================
    // 스켈레톤: Phalanx/Relentless — 무감정, 완벽한 포위 후 동시 전진
    // ====================================================================
    public class PhalanxGroupPolicy : MonoBehaviour, IFactionGroupPolicy
    {
        [Header("Phalanx Parameters")]
        [SerializeField] private FactionGroupPolicySO policySO;

        private float encircleProgress = 0f; // 0.0~1.0 포위 진척도

        /// <summary>
        /// 사기 평가. 스켈레톤은 사기 개념이 없다.
        /// panicChainMultiplier=0.0이므로 패닉 전파가 완전히 차단된다.
        /// 동료가 파괴되어도 나머지는 무감각하게 계속 전진한다.
        /// </summary>
        public float EvaluateMorale(float currentMorale, float individualLoss, float connectionWeight)
        {
            // 사기 저하 0. 항상 현재 사기 유지.
            return currentMorale;
        }

        /// <summary>
        /// 에스컬레이션 결정. EncircleGated: 포위 진척도가 100%에 도달해야만 에스컬레이션.
        /// 포위가 완성되기 전까지는 위협 레벨 0 유지 (천천히 조여옴).
        /// </summary>
        public int DecideEscalation(float elapsedTime, float threatLevel, float encircleProgress)
        {
            this.encircleProgress = encircleProgress;

            if (encircleProgress >= 1.0f) return 3; // 포위 완성 → 전원 동시 전진
            if (encircleProgress >= 0.7f) return 1; // 70% 이상 → 포위 유지 경고
            return 0; // 포위 진행 중 → 위협 없음
        }

        /// <summary>
        /// 역할 할당. 등간격 포위 배치.
        /// 각 개체를 타겟 주위 원형으로 배치하여 포위망 형성.
        /// </summary>
        public void AssignRoles(List<int> memberIds)
        {
            int count = memberIds.Count;
            if (count == 0) return;

            // 등간격 포위: 360°를 인원수로 나눔
            float angleStep = 360f / count;
            for (int i = 0; i < count; i++)
            {
                float angle = angleStep * i;
                // TODO: 각 개체에 목표 포위 각도를 할당하여 이동 명령
                // memberIds[i]에 대해 angle 방향으로 이동
            }

            // 포위 진척도 계산: 모든 개체가 목표 위치에 도달한 비율
            // TODO: 실제 위치와 목표 위치의 거리 기반 진척도 계산
        }

        /// <summary>
        /// 패닉 체인. 스켈레톤은 패닉이 없다.
        /// panicChainMultiplier=0.0. 아무 일도 일어나지 않는다.
        /// </summary>
        public void HandlePanicChain(int fleeingMemberId, float panicMultiplier)
        {
            // 스켈레톤은 도망하지 않으며, 도망하더라도 전파되지 않음.
            // 파괴된 개체의 포위 슬롯을 인접 개체가 자동으로 메움.
        }

        public float GetEncircleProgress() => encircleProgress;
    }
}
