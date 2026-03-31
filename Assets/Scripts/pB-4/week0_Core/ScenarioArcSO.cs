using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(menuName = "pB4/Scenario/ScenarioArc")]
    public class ScenarioArcSO : ScriptableObject
    {
        public string arcId;
        public string arcName;
        public List<PhaseData> phases = new List<PhaseData>();
        public DilemmaData climaxDilemma;
    }

    [Serializable]
    public struct PhaseData
    {
        public string phaseName;
        public int phaseIndex;
        public string narrativeDescription;
        public List<string> triggerTags;
    }

    [Serializable]
    public struct DilemmaData
    {
        public string dilemmaId;
        public string choiceA;
        public string choiceB;
        public string consequenceA;
        public string consequenceB;
    }
}
