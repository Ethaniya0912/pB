using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(menuName = "pB4/Scenario/SpeechLibrary")]
    public class SpeechLibrarySO : ScriptableObject
    {
        public List<SpeechLayerEntry> coreLayers = new List<SpeechLayerEntry>();
        public List<SpeechLayerEntry> textureLayers = new List<SpeechLayerEntry>();
        public List<SpeechLayerEntry> individualLayers = new List<SpeechLayerEntry>();
    }

    [Serializable]
    public struct SpeechLayerEntry
    {
        public string tag;
        public List<string> wordPool;
    }
}
