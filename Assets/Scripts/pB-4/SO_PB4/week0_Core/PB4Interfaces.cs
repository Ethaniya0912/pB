// =============================================================================
// PB4Interfaces.cs  |  pB-4 Project — Week 0
// Layer  : Core (공유 계층)
// Owner  : Person A
//
// 역할:
//   3개 파트가 공유하는 15종 인터페이스 전체 선언.
//   Week 0에서 시그니처 동결. 이후 메서드 추가는 가능하나 기존 시그니처 변경 금지.
//
// 네임스페이스 분류:
//   PB4.Interfaces.Environment  — 지형 관련
//   PB4.Interfaces.Intelligence — AI 관련
//   PB4.Interfaces.Narrative    — 시나리오 관련
//   PB4.Interfaces.Presentation — 발화/UI 관련
//   PB4.Interfaces.Core         — 공통
// =============================================================================
using System.Collections.Generic;
using UnityEngine;

// ============ CORE ============
namespace TDA.PB4.Interfaces.Core
{
    /// <summary>중앙 블랙보드 접근 인터페이스.</summary>
    public interface IBlackboard
    {
        IReadOnlyList<string> GetActiveTerrainTags();
        float GetGlobalIntensity();
        float GetCharacterStat(string key);
    }

    /// <summary>사전 베이킹된 의미 원소 벡터 라이브러리 접근.</summary>
    public interface INarrativeVectorLib
    {
        float[] GetVector(string elementName);
        float CosineSimilarity(float[] a, float[] b);
    }
}

// ============ ENVIRONMENT ============
namespace TDA.PB4.Interfaces.Environment
{
    /// <summary>지형 표면에서 물리적 특성(경사도, 밀도, 폐쇄도, 통로폭) 샘플링.</summary>
    public interface ITerrainSampler
    {
        float SampleSlope(Vector3 worldPos);
        float SampleDensity(Vector3 worldPos);
        float SampleOcclusion(Vector3 worldPos);
        float SamplePathWidth(Vector3 worldPos);
    }

    /// <summary>물리적 특성을 퍼지 태그로 변환하여 블랙보드에 게시.</summary>
    public interface IContextAnalyzer
    {
        List<string> AnalyzeTerrain(Vector3 worldPos);
    }

    /// <summary>HNGS 하이브리드 스켈레톤 모델 제어.</summary>
    public interface IHNGSController
    {
        float GetIntensityAtNode(int nodeIndex);
        void RequestWidthModulation(int edgeIndex, float widthFactor);
    }

    /// <summary>바이옴별 팩션 스폰 확률 산출.</summary>
    public interface IBiomeSpawnResolver
    {
        float CalculateSpawnProbability(string factionId, int biomeType, Vector3 worldPos);
    }
}

// ============ INTELLIGENCE ============
namespace TDA.PB4.Interfaces.Intelligence
{
    /// <summary>유틸리티 AI 점수 산출.</summary>
    public interface IUtilityScorer
    {
        float ScoreAction(string actionId, Dictionary<string, float> context);
    }

    /// <summary>성격 5축 엔진 접근.</summary>
    public interface IPersonalityEngine
    {
        float GetAxis(string axisName); // control, stability, openness, agreeable, directness
        void PivotPersonality(float[] delta);
    }

    /// <summary>행동 트리(BT) 노드 실행.</summary>
    public interface IBTNodeExecutor
    {
        void ExecuteNode(string nodeId);
        bool IsNodeRunning(string nodeId);
    }

    /// <summary>팩션별 군집 정책 인터페이스.</summary>
    public interface IFactionGroupPolicy
    {
        float EvaluateMorale(float currentMorale, float individualLoss, float connectionWeight);
        int DecideEscalation(float elapsedTime, float threatLevel, float encircleProgress);
        void AssignRoles(List<int> memberIds);
        void HandlePanicChain(int fleeingMemberId, float panicMultiplier);
    }
}

// ============ NARRATIVE ============
namespace TDA.PB4.Interfaces.Narrative
{
    /// <summary>AI 기억 시스템 접근.</summary>
    public interface IMemoryProvider
    {
        string RecallSimilarIncident(float[] situationVector, float minSimilarity);
    }

    /// <summary>결정적 사건 기록기.</summary>
    public interface IIncidentRecorder
    {
        void RecordIncident(string incidentId, float intensityScore, string moralAlignment);
    }

    /// <summary>업보/카르마 방향자.</summary>
    public interface IKarmaDirector
    {
        float GetKarmaScore(string characterId);
        void ApplyKarmaShift(string characterId, float delta);
    }
}

// ============ PRESENTATION ============
namespace TDA.PB4.Interfaces.Presentation
{
    /// <summary>발화 시스템 3단계 레이어 조합.</summary>
    public interface ISpeechAssembler
    {
        string AssembleSpeech(float[] personalityVector, string contextTag);
    }

    /// <summary>다이얼로그 UI 렌더링.</summary>
    public interface IDialogueRenderer
    {
        void ShowDialogue(string speakerName, string text, float duration);
        void HideDialogue();
    }
}
