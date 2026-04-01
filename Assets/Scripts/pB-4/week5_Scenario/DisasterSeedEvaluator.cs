// =============================================================================
// DisasterSeedEvaluator.cs  |  pB-4 Project — Week 5
// Layer  : L3 Domain (시나리오)
// Namespace: TDA.PB4.Scenario
//
// 역할:
//   재앙의 씨앗 시스템. NPC별로 재앙 확률을 계산하여
//   Disaster(재앙) 또는 Catharsis(정화) 시퀀스로 분기한다.
//
//   확률 수식:
//     Probability = Base(30%) + (Stability × -20%) + (Trust × -30%) + (Campfire_Heal × -50%)
//
//   양수 → Disaster 시퀀스 (재앙 아키타입 적용)
//   음수 → Catharsis 시퀀스 (성격 회복/강화)
//
//   재앙 아키타입 4종:
//     SelfAbandon:     자기 유기. 모든 관계를 포기.
//     MadDevotion:     광적 헌신. 특정 대상에 집착.
//     CalculatedMalice: 통제된 광기. 냉정한 악의.
//     Stasis:          정신적 마비. 행동 불능.
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Data;

namespace TDA.PB4.Scenario
{
    public enum DisasterArchetype
    {
        /// <summary>자기 유기: 모든 관계/의무 포기. Agreeable→0.</summary>
        SelfAbandon,
        /// <summary>광적 헌신: 특정 대상에 비이성적 집착. Control→0.</summary>
        MadDevotion,
        /// <summary>통제된 광기: 냉정하게 계산된 악의. Stability+, Agreeable→0.</summary>
        CalculatedMalice,
        /// <summary>정신적 마비: 행동 불능. 모든 유틸리티 점수 0.</summary>
        Stasis
    }

    /// <summary>재앙 평가 결과.</summary>
    [Serializable]
    public class DisasterSeedResult
    {
        public string npcName;
        public float baseProbability;
        public float stabilityMod;
        public float trustMod;
        public float healMod;
        public float finalProbability;
        public bool isDisaster;
        public DisasterArchetype archetype;
    }

    public class DisasterSeedEvaluator : MonoBehaviour
    {
        [Header("━━━ 기본 확률 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("재앙의 기본 발생 확률 (0.3 = 30%). " +
                 "Stability와 Trust가 이 값을 낮추거나 높인다.")]
        [Range(0f, 1f)]
        public float baseProbability = 0.3f;

        [Header("━━━ 보정 계수 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Stability의 보정 계수. 음수 = 안정적일수록 재앙 확률 감소. " +
                 "-0.2 = Stability 1.0이면 확률 -20%.")]
        [Range(-0.5f, 0f)]
        public float stabilityModifier = -0.2f;

        [Tooltip("Trust(정규화)의 보정 계수. 음수 = 신뢰할수록 재앙 확률 감소. " +
                 "-0.3 = Trust 100이면 확률 -30%.")]
        [Range(-0.5f, 0f)]
        public float trustModifier = -0.3f;

        [Tooltip("모닥불 치유의 보정 계수. 캠프파이어에서 치유 받은 경우 적용. " +
                 "-0.5 = 치유 시 확률 -50%. " +
                 "현재 프로토타입에서는 수동 토글.")]
        [Range(-1f, 0f)]
        public float campfireHealModifier = -0.5f;

        [Tooltip("모닥불 치유를 받았는지 여부. 프로토타입에서 수동 토글. " +
                 "true이면 campfireHealModifier가 적용되어 재앙 확률 대폭 감소.")]
        public bool hasCampfireHeal = false;

        [Header("━━━ 아키타입 결정 규칙 ━━━━━━━━━━━━━━━")]
        [Tooltip("Stability가 이 값 미만이고 Agreeable이 낮으면 SelfAbandon. " +
                 "0.2 = Stability < 0.2이면 자기유기 후보.")]
        [Range(0f, 0.5f)]
        public float selfAbandonThreshold = 0.2f;

        [Tooltip("Control이 이 값 미만이면 MadDevotion. " +
                 "0.2 = Control < 0.2이면 광적 헌신 후보.")]
        [Range(0f, 0.5f)]
        public float madDevotionThreshold = 0.2f;

        [Header("━━━ 마지막 평가 결과 (Read Only) ━━━━━━")]
        [SerializeField] private DisasterSeedResult lastResult;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        // ==================================================================
        // 핵심 API
        // ==================================================================

        /// <summary>NPC의 재앙 확률을 평가하고 Disaster/Catharsis를 판정.</summary>
        public DisasterSeedResult Evaluate(HumanoidAIBrain npc)
        {
            if (npc == null) return null;
            var trust = npc.GetComponent<TrustMatrix>();
            float trustNorm = trust != null ? trust.GetNormalizedTrust() : 0.5f;
            float stability = npc.Personality.stability;

            float prob = baseProbability;
            float stabMod = stability * stabilityModifier;
            float trustMod = trustNorm * trustModifier;
            float healMod = hasCampfireHeal ? campfireHealModifier : 0f;

            prob += stabMod + trustMod + healMod;

            var result = new DisasterSeedResult
            {
                npcName = npc.name,
                baseProbability = baseProbability,
                stabilityMod = stabMod,
                trustMod = trustMod,
                healMod = healMod,
                finalProbability = prob,
                isDisaster = prob > 0f,
                archetype = DetermineArchetype(npc.Personality)
            };

            lastResult = result;

            if (debugLog)
            {
                Debug.Log($"[DisasterSeed] {npc.name}: base={baseProbability:F2} + stab={stabMod:F2} + trust={trustMod:F2} + heal={healMod:F2} = {prob:F2}");
                Debug.Log($"  → {(result.isDisaster ? "☠️ DISASTER" : "✨ CATHARSIS")} archetype={result.archetype}");
            }

            return result;
        }

        private DisasterArchetype DetermineArchetype(PersonalityMatrix p)
        {
            if (p.stability < selfAbandonThreshold && p.agreeable < 0.3f)
                return DisasterArchetype.SelfAbandon;
            if (p.control < madDevotionThreshold)
                return DisasterArchetype.MadDevotion;
            if (p.stability > 0.5f && p.agreeable < 0.2f)
                return DisasterArchetype.CalculatedMalice;
            return DisasterArchetype.Stasis;
        }

        public DisasterSeedResult LastResult => lastResult;
    }
}
