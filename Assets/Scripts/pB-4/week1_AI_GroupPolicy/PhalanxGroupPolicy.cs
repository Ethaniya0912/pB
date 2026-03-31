using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Interfaces.Intelligence;
using TDA.PB4.Data;

namespace TDA.PB4.AI.GroupPolicies
{
    // ====================================================================
    // 스켈레톤: Phalanx/Relentless — 무감정, 완벽한 포위 후 동시 전진
    // ====================================================================
    public class PhalanxGroupPolicy : MonoBehaviour, IFactionGroupPolicy
    {
        [Header("Phalanx Parameters")]
        [SerializeField] private FactionGroupPolicySO policySO;

        private float encircleProgress = 0f;

        public float EvaluateMorale(float currentMorale, float individualLoss, float connectionWeight)
        {
            return currentMorale; // 사기 저하 0
        }

        public int DecideEscalation(float elapsedTime, float threatLevel, float encircleProgress)
        {
            this.encircleProgress = encircleProgress;
            if (encircleProgress >= 1.0f) return 3;
            if (encircleProgress >= 0.7f) return 1;
            return 0;
        }

        public void AssignRoles(List<int> memberIds)
        {
            int count = memberIds.Count;
            if (count == 0) return;
            float angleStep = 360f / count;
            for (int i = 0; i < count; i++)
            {
                float angle = angleStep * i;
                // TODO: 각 개체에 목표 포위 각도 할당
            }
        }

        public void HandlePanicChain(int fleeingMemberId, float panicMultiplier)
        {
            // 스켈레톤은 패닉이 없다
        }

        public float GetEncircleProgress() => encircleProgress;
    }
}
