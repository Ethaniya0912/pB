using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(menuName = "pB4/AI/TraumaData")]
    public class TraumaDataSO : ScriptableObject
    {
        public string traumaID;
        public string triggerKey;
        public List<string> keywords = new List<string>();
        public string vfxID;
        [Range(0f, 1f)] public float severity = 0.5f;
        [Range(0f, 1f)] public float recoveryRate = 0.1f;
        public string behavioralEffect;
        public List<string> phobias = new List<string>();
    }
}
