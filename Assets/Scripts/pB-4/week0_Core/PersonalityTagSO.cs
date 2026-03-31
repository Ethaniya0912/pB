using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(menuName = "pB4/AI/PersonalityTag")]
    public class PersonalityTagSO : ScriptableObject
    {
        public string tagName;
        [Range(0f, 1f)] public float minControl;
        [Range(0f, 1f)] public float maxControl;
        public float greedK = 1.0f;
        public float fearMultiplier = 1.0f;
        public string requirementDescription;
        [Range(0f, 1f)] public float spawnProbability = 0.5f;
        public float utilityModifier = 0f;
    }
}
