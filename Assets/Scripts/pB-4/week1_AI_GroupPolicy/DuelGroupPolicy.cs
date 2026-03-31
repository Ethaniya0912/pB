using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Interfaces.Intelligence;
using TDA.PB4.Data;

namespace TDA.PB4.AI.GroupPolicies
{
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

        public float EvaluateMorale(float currentMorale, float individualLoss, float connectionWeight)
        {
            float panicMul = policySO != null ? policySO.panicChainMultiplier : 0.5f;
            float moraleDrop = individualLoss * connectionWeight * panicMul;
            if (cowardDetected)
                return Mathf.Min(1.0f, currentMorale + 0.3f);
            return Mathf.Max(0f, currentMorale - moraleDrop);
        }

        public int DecideEscalation(float elapsedTime, float threatLevel, float encircleProgress)
        {
            if (cowardDetected) return 3;
            return Mathf.Min(3, Mathf.FloorToInt(threatLevel / 0.3f));
        }

        public void AssignRoles(List<int> memberIds)
        {
            if (cowardDetected)
            {
                Debug.Log("[Duel] 비겁한 수 감지! 전원 All Striker 전환.");
                return;
            }
            if (!duelDeclared && memberIds.Count > 0)
            {
                duelTargetId = memberIds[0];
                duelDeclared = true;
                Debug.Log($"[Duel] 결투 선포: ChallengerId={duelTargetId}");
            }
        }

        public void HandlePanicChain(int fleeingMemberId, float panicMultiplier)
        {
            float fearIncrease = 0.1f * panicMultiplier;
            Debug.Log($"[Duel] 동료 도주 감지: 명예 실추. fearIncrease={fearIncrease}");
        }

        public void NotifyCowardDetected() { cowardDetected = true; }
        public void ResetDuel() { duelDeclared = false; duelTargetId = -1; cowardDetected = false; }
    }
}
