using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(menuName = "pB4/Scenario/VectorLibrary")]
    public class VectorLibrarySO : ScriptableObject
    {
        [Header("200종 의미 원소 벡터 (768차원)")]
        public List<SemanticElement> elements = new List<SemanticElement>();
    }

    [Serializable]
    public struct SemanticElement
    {
        public string elementName;
        public float[] vector;
    }
}
