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

    // ═══════════════════════════════════════════════════════════════════════════
    // 추가 ② — namespace TDA.PB4.Interfaces.Core 에 추가
    // ═══════════════════════════════════════════════════════════════════════════
    /// <summary>Week 2 진척률 추적·보고 계약.</summary>
    /// <remarks>
    /// 구현체: Week2ProgressTracker.cs (Day 1 T1.6).
    /// 호출자: HumanoidBootstrapper, 각 BootstrapOne 완료 시 ReportEvent 호출.
    /// 요구사항 #8 (진척률 가시화)의 핵심 계약.
    /// </remarks>
    public interface IProgressTracker
    {
        /// <summary>체크 항목 결과 보고. checkId는 Week2Checklist의 id와 매칭.</summary>
        /// <param name="checkId">예: "WK2_C01".</param>
        /// <param name="passed">true=통과, false=실패.</param>
        /// <param name="detail">실패 이유 또는 로그 메시지.</param>
        void ReportEvent(string checkId, bool passed, string detail);

        /// <summary>마일스톤 진척률 보고. 여러 체크의 합산.</summary>
        void ReportProgress(string milestoneId, float ratio);

        /// <summary>최종 .md 보고서 생성. Day 5 T5.1에서 전체 구현.</summary>
        void EmitFinalReport();
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

    // ═══════════════════════════════════════════════════════════════════════════
    // 추가 ① — namespace TDA.PB4.Interfaces.Intelligence 에 추가
    // ═══════════════════════════════════════════════════════════════════════════
    /// <summary>NPC의 플레이어에 대한 신뢰도를 조회·수정하는 계약.</summary>
    /// <remarks>
    /// Trust 값은 내부적으로 0~100 범위. 외부 조회는 정규화 0~1 사용.
    /// 4 Tier: Hostility(0~19) / Doubt(20~59) / Cooperation(60~89) / BlindTrust(90~100).
    /// Tier 인덱스: 0=Hostility, 1=Doubt, 2=Cooperation, 3=BlindTrust.
    /// 구현체: TrustMatrix.cs (Day 2 T2.4에서 인터페이스 구현 추가).
    /// </remarks>
    public interface ITrustProvider
    {
        /// <summary>0(적대)~1(맹신) 정규화 신뢰도.</summary>
        float GetNormalizedTrust();

        /// <summary>현재 Tier 인덱스. 0=Hostility, 1=Doubt, 2=Cooperation, 3=BlindTrust.</summary>
        int GetCurrentTierIndex();

        /// <summary>신뢰도에 델타 적용. 최종값은 [0,100]으로 클램핑.</summary>
        /// <param name="amount">-100~+100.</param>
        /// <param name="reason">TrustChangeReason enum의 문자열 또는 "Manual".</param>
        void AddDelta(float amount, string reason);

        /// <summary>명령 수락 여부 평가. AcceptanceScore=(Trust×0.6)+(Severity×0.4)-Fear.</summary>
        bool EvaluateCommand(float commandSeverity, float npcFear);
    }

    /// <summary>NPC의 트라우마 상태를 조회·수정하는 계약.</summary>
    /// <remarks>
    /// 3 Stage: None=0 / AcuteShock=1 / Crossroads=2 / PermanentScarring=3.
    /// PermanentScarring 도달 시 성격 5축 앵커값 변경 (영구).
    /// 구현체: TraumaSystem.cs (Day 2 T2.4에서 인터페이스 구현 추가).
    /// </remarks>
    public interface ITraumaProvider
    {
        /// <summary>현재 단계. 0=None, 3=PermanentScarring.</summary>
        int CurrentStageIndex { get; }

        /// <summary>트라우마 충격 부여. intensity 0.6+ 이면 AcuteShock 트리거.</summary>
        void ApplyShock(float intensity, string causeId);

        /// <summary>현재 단계에 따른 fear 가중치. 1.0=정상, 1.5=AcuteShock 시.</summary>
        float GetFearMultiplier();
    }

    /// <summary>NPC의 진영(Alignment) 상태 조회 계약.</summary>
    /// <remarks>
    /// 4 Alignment: Hostile=0 / Neutral=1 / Friendly=2 / Companion=3.
    /// 매 1초 trust + karma 기반 동적 전이 평가 (NPCAlignmentController, Day 3 T3.2).
    /// </remarks>
    public interface IAlignmentProvider
    {
        /// <summary>현재 Alignment 인덱스. 0=Hostile, 3=Companion.</summary>
        int CurrentAlignmentIndex { get; }

        /// <summary>대상 오브젝트에 적대적인지 판정. BT 그래프에서 사용.</summary>
        bool IsHostileTo(UnityEngine.GameObject other);

        /// <summary>Alignment 전이 이벤트. 인자: (old index, new index).</summary>
        event System.Action<int, int> OnAlignmentChanged;
    }

    /*
    } // namespace TDA.PB4.Interfaces.Intelligence
    */
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
