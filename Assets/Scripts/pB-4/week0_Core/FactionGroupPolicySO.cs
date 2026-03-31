using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(menuName = "pB4/AI/FactionGroupPolicy")]
    public class FactionGroupPolicySO : ScriptableObject
    {
        public string factionId;
        public float panicChainMultiplier = 1.0f;
        public EscalationMode escalationMode = EscalationMode.TimeGated;
        public float leaderInfluenceRadius = 15f;
        public MessengerPolicy messengerPolicy = MessengerPolicy.OnRetreat;
        public FormationTemplate formationTemplate = FormationTemplate.Swarm;
        public bool tokenOverrideEnabled = false;
        public float tokenOverrideThreshold = 0f;
        public List<RoleTransitionRule> roleTransitionRules = new List<RoleTransitionRule>();
    }

    public enum EscalationMode { TimeGated, ThreatGated, EncircleGated }
    public enum MessengerPolicy { None, OnRetreat, Always }
    public enum FormationTemplate { Swarm, Duel, Phalanx, Custom }

    [Serializable]
    public struct RoleTransitionRule
    {
        public string fromRole;
        public string toRole;
        public float triggerThreshold;
    }
}
