using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(menuName = "pB4/AI/FactionTier")]
    public class FactionTierSO : ScriptableObject
    {
        public string factionId;
        [Range(1, 5)] public int tier = 1;
        public List<UnitEntry> unitComposition = new List<UnitEntry>();
        public LeaderConfig leaderConfig;
        public FactionGroupPolicySO groupPolicyOverride;

        [Header("Survival Drive Modifiers")]
        public float fearOverride = -1f;
        public float greedOverride = -1f;

        [Header("BT Override")]
        public string mobBTOverrideId;
    }

    [Serializable]
    public struct UnitEntry
    {
        public GameObject prefabRef;
        [Range(0f, 1f)] public float spawnWeight;
        public int minCount;
        public int maxCount;
    }

    [Serializable]
    public struct LeaderConfig
    {
        public bool hasLeader;
        public GameObject leaderPrefab;
        public float leaderAuthorityW;
        public float leaderCommandRadius;
        public BTComplexity leaderBTComplexity;
    }

    public enum BTComplexity { Simple, Advanced, Elite }
}
