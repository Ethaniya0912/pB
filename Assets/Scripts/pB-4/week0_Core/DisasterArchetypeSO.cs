using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(menuName = "pB4/Scenario/DisasterArchetype")]
    public class DisasterArchetypeSO : ScriptableObject
    {
        public string archetypeId;
        public string archetypeName;
        public string behaviorPattern;
        public string dialogueTemplate;
    }
}
