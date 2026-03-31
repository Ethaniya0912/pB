// =============================================================================
// PB4ScriptableObjects.cs  |  pB-4 Project — Week 0
// Layer  : Data/SO
// Owner  : Person A
//
// 역할:
//   11종 SO 스키마 필드 레이아웃 동결.
//   실제 데이터는 Week 1 이후 채우며, 이 파일은 구조만 정의.
//   기존 CaveBiomeData, CaveBiomeSettings와 충돌하지 않도록
//   PB4 네임스페이스에서 독립 관리. 기존 SO는 레거시 유지.
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    // ======================== AI SO ========================

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

    [CreateAssetMenu(menuName = "pB4/AI/FactionTier")]
    public class FactionTierSO : ScriptableObject
    {
        public string factionId;
        [Range(1, 5)] public int tier = 1;
        public List<UnitEntry> unitComposition = new List<UnitEntry>();
        public LeaderConfig leaderConfig;
        public FactionGroupPolicySO groupPolicyOverride;

        [Header("Survival Drive Modifiers")]
        public float fearOverride = -1f;   // -1 = 사용 안 함
        public float greedOverride = -1f;

        [Header("BT Override")]
        public string mobBTOverrideId;
    }

    [Serializable]
    public struct UnitEntry
    {
        public GameObject prefabRef;
        [Range(0f, 1f)] public float spawnWeight;
        public int minCount;
        public int maxCount;
    }

    [Serializable]
    public struct LeaderConfig
    {
        public bool hasLeader;
        public GameObject leaderPrefab;
        public float leaderAuthorityW;
        public float leaderCommandRadius;
        public BTComplexity leaderBTComplexity;
    }

    public enum BTComplexity { Simple, Advanced, Elite }

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

    // ======================== SCENARIO SO ========================

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
        public int phaseIndex; // 0=Intro, 1=Rising, 2=Climax, 3=Settlement
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
        public float[] vector; // 768차원
    }

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

    [CreateAssetMenu(menuName = "pB4/Scenario/DisasterArchetype")]
    public class DisasterArchetypeSO : ScriptableObject
    {
        public string archetypeId;
        public string archetypeName; // SelfAbandon, MadDevotion, CalculatedMalice, Stasis
        public string behaviorPattern;
        public string dialogueTemplate;
    }

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

    // ======================== TERRAIN/BIOME SO ========================

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
