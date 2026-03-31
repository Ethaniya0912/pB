using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(menuName = "pB4/Scenario/ArchetypeMap")]
    public class ArchetypeMapSO : ScriptableObject
    {
        public List<ArchetypeMapping> mappings = new List<ArchetypeMapping>();
    }

    [Serializable]
    public struct ArchetypeMapping
    {
        public string personalityTagCombination;
        public string archetypeId;
        [Range(0f, 1f)] public float probability;
    }
}
