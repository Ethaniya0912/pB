using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(menuName = "pB4/AI/MobFactionData")]
    public class MobFactionDataSO : ScriptableObject
    {
        public string factionId;
        public string factionName;
        [Header("I/H/O/A Parameters")]
        [Range(0, 10)] public int intelligence = 5;
        [Range(0, 10)] public int honor = 5;
        [Range(0, 10)] public int organization = 5;
        [Range(0, 10)] public int aggression = 5;
    }
}
