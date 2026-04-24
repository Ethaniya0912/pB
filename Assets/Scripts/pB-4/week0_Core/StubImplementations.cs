// =============================================================================
// StubImplementations.cs  |  pB-4 Project — Week 0
//                         |  v3 NGO 2.0 개정 — 2026-04-23
// Layer  : Core (공유 계층)
// Owner  : Person A
//
// 역할:
//   모든 인터페이스에 대해 고정값을 반환하는 Stub 구현체.
//   각 파트가 다른 파트 완성 여부와 무관하게 에디터 런타임을 독립적으로 실행 가능.
//   Week 1 이후 실제 구현체가 완성되면 DI 컨테이너에서 교체.
//
// [v3 NGO 개정]
//   - IIncidentRecorder: SetVectorIndexer(Action<string, float[]>) 추가
//   - IKarmaDirector: N:M API 3개 추가 (GetKarmaScore/ApplyKarmaShift/GetTier — ulong 오버로드)
//   - 기존 string characterId API는 호환을 위해 유지 (ulong → string.ToString로 브릿지)
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Interfaces.Core;
using TDA.PB4.Interfaces.Environment;
using TDA.PB4.Interfaces.Intelligence;
using TDA.PB4.Interfaces.Narrative;
using TDA.PB4.Interfaces.Presentation;

namespace TDA.PB4.Stubs
{
    // ======================== CORE ========================
    public class StubBlackboard : IBlackboard
    {
        public IReadOnlyList<string> GetActiveTerrainTags() => new List<string> { "Generic" };
        public float GetGlobalIntensity() => 0.3f;
        public float GetCharacterStat(string key) => 0.5f;
    }

    public class StubNarrativeVectorLib : INarrativeVectorLib
    {
        public float[] GetVector(string elementName) => new float[768];
        public float CosineSimilarity(float[] a, float[] b) => 0f;
    }

    // ======================== ENVIRONMENT ========================
    public class StubTerrainSampler : ITerrainSampler
    {
        public float SampleSlope(Vector3 worldPos) => 15f;    // 15도 기본 경사
        public float SampleDensity(Vector3 worldPos) => 0.5f;
        public float SampleOcclusion(Vector3 worldPos) => 0.4f;
        public float SamplePathWidth(Vector3 worldPos) => 3.0f; // 3m 기본 통로 폭
    }

    public class StubContextAnalyzer : IContextAnalyzer
    {
        public List<string> AnalyzeTerrain(Vector3 worldPos)
            => new List<string> { "GenericCave" };
    }

    public class StubHNGSController : IHNGSController
    {
        public float GetIntensityAtNode(int nodeIndex) => 0.3f;
        public void RequestWidthModulation(int edgeIndex, float widthFactor) { }
    }

    public class StubBiomeSpawnResolver : IBiomeSpawnResolver
    {
        public float CalculateSpawnProbability(string factionId, int biomeType, Vector3 worldPos)
            => 0.1f;
    }

    // ======================== INTELLIGENCE ========================
    public class StubUtilityScorer : IUtilityScorer
    {
        public float ScoreAction(string actionId, Dictionary<string, float> context) => 0.5f;
    }

    public class StubPersonalityEngine : IPersonalityEngine
    {
        public float GetAxis(string axisName) => 0.5f;
        public void PivotPersonality(float[] delta) { }
    }

    public class StubBTNodeExecutor : IBTNodeExecutor
    {
        public void ExecuteNode(string nodeId) { }
        public bool IsNodeRunning(string nodeId) => false;
    }

    public class StubFactionGroupPolicy : IFactionGroupPolicy
    {
        public float EvaluateMorale(float currentMorale, float individualLoss, float connectionWeight)
            => Mathf.Max(0f, currentMorale - individualLoss * connectionWeight);
        public int DecideEscalation(float elapsedTime, float threatLevel, float encircleProgress) => 0;
        public void AssignRoles(List<int> memberIds) { }
        public void HandlePanicChain(int fleeingMemberId, float panicMultiplier) { }
    }

    // ======================== NARRATIVE ========================
    public class StubMemoryProvider : IMemoryProvider
    {
        public string RecallSimilarIncident(float[] situationVector, float minSimilarity) => null;
    }

    /// <summary>[v3] IIncidentRecorder Stub — Vector 인덱싱 hook 추가.</summary>
    public class StubIncidentRecorder : IIncidentRecorder
    {
        public void RecordIncident(string incidentId, float intensityScore, string moralAlignment)
            => Debug.Log($"[Stub] Incident recorded: {incidentId}");

        // [v3 NGO-신규] Wk7 RAG 연결 준비용 Vector 인덱싱 hook
        public void SetVectorIndexer(Action<string, float[]> indexer)
        {
            // Stub: hook 주입 요청만 로깅. 실제 인덱싱은 실제 IncidentRecorder에서 수행.
            Debug.Log("[Stub] SetVectorIndexer 호출됨 (Stub 구현에서는 무시)");
        }
    }

    /// <summary>[v3] IKarmaDirector Stub — N:M API 3개 추가.</summary>
    public class StubKarmaDirector : IKarmaDirector
    {
        // 기존 string characterId API (Week 0 초기 호환용) 유지
        public float GetKarmaScore(string characterId) => 0f;
        public void ApplyKarmaShift(string characterId, float delta) { }

        // [v3 NGO-신규] ulong playerId API (Day 3 T3.1)
        public float GetKarmaScore(ulong playerId) => 0f;

        public void ApplyKarmaShift(ulong playerId, float delta, string reason)
        {
            // Stub: 값 유지 + 로깅만
            Debug.Log($"[Stub] Karma shift: p{playerId}, delta={delta:+0.#;-0.#;0} ({reason})");
        }

        public KarmaTier GetTier(ulong playerId) => KarmaTier.Neutral;
    }

    // ======================== PRESENTATION ========================
    public class StubSpeechAssembler : ISpeechAssembler
    {
        public string AssembleSpeech(float[] personalityVector, string contextTag)
            => "[Stub] 대사 준비 중...";
    }

    public class StubDialogueRenderer : IDialogueRenderer
    {
        public void ShowDialogue(string speakerName, string text, float duration)
            => Debug.Log($"[Stub Dialogue] {speakerName}: {text}");
        public void HideDialogue() { }
    }
}