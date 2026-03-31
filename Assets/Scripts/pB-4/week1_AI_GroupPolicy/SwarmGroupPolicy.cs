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
        private const float TIME_GATE_INTERVAL = 30f;

        public float EvaluateMorale(float currentMorale, float individualLoss, float connectionWeight)
        {
            float moraleDrop = individualLoss * connectionWeight * panicMultiplier;
            float newMorale = Mathf.Max(0f, currentMorale - moraleDrop);
            return newMorale;
        }

        public int DecideEscalation(float elapsedTime, float threatLevel, float encircleProgress)
        {
            int timeEscalation = Mathf.Min(3, Mathf.FloorToInt(elapsedTime / TIME_GATE_INTERVAL));
            return timeEscalation;
        }

        public void AssignRoles(List<int> memberIds)
        {
            // Swarm: 역할 분리 최소. 모두 공격/도주를 개별 유틸리티로 결정
        }

        public void HandlePanicChain(int fleeingMemberId, float panicMultiplier)
        {
            float fearIncrease = 0.2f * panicMultiplier;
            Debug.Log($"[Swarm] 패닉 체인 발동: MemberId={fleeingMemberId}, fearIncrease={fearIncrease}. 전령 파견 대기.");
        }
    }
}
