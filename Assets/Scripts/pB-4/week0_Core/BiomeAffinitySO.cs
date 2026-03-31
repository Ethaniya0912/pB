using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(menuName = "pB4/Terrain/BiomeAffinity")]
    public class BiomeAffinitySO : ScriptableObject
    {
        public BiomeType biomeType;
        public List<FactionAffinity> factionAffinities = new List<FactionAffinity>();
        [Range(20f, 100f)] public float adjacencyBlendRadius = 50f;
        public List<string> exclusionTags = new List<string>();
    }

    public enum BiomeType { DirtCave, Waterway, Canyon, Crystal, Ruins, Lava }

    [Serializable]
    public struct FactionAffinity
    {
        public string factionId;
        [Range(0f, 1f)] public float baseAffinity;
        public DensityPreference densityPreference;
        public LightPreference lightPreference;
    }

    public enum DensityPreference { Sparse, Normal, Dense }
    public enum LightPreference { Dark, Dim, Any }
}
