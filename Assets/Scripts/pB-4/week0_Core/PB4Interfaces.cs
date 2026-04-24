// =============================================================================
// PB4Interfaces.cs  |  pB-4 Project — Week 0 → v3 NGO 2.0 + Day 3 신규 인터페이스
// Layer  : Core (공유 계층)
// Owner  : Person A
//
// 역할:
//   3개 파트가 공유하는 인터페이스 선언. Week 0 시그니처 동결 원칙.
//
// [v3 개정 — 2026-04-23]
//   - [NGO-A] ITrustProvider에 ulong playerId 오버로드 추가 (N:M 관계 지원)
//             기존 시그니처(매개변수 없는 버전)는 유지하되, 멀티플레이어 환경에서는
//             새 오버로드를 사용. 단독 Play는 기존 메서드가 기본 playerId=0 반환.
//   - [NGO-B] IKarmaDirector → ulong playerId 기반 (개론서 §1.20 ServerCharacterRegistry 정합)
//             기존 string characterId도 호환 유지 (신규 프로젝트는 ulong 권장)
//   - [Day 3] ICommandFilter 신규 추가 (T3.5)
//   - [Day 3] KarmaTier enum 추가 (플레이어 전역 도덕, Saint/Neutral/Outlaw/Demon)
//   - [Day 3] CommandSeverity enum 추가
//   - [Day 3] CommandRequest struct 추가
// =============================================================================
using System;
using System.Collections.Generic;
using Unity.Netcode;
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

    /// <summary>Week 2 진척률 추적·보고 계약.</summary>
    public interface IProgressTracker
    {
        void ReportEvent(string checkId, bool passed, string detail);
        void ReportProgress(string milestoneId, float ratio);
        void EmitFinalReport();
    }
}

// ============ ENVIRONMENT ============
namespace TDA.PB4.Interfaces.Environment
{
    public interface ITerrainSampler
    {
        float SampleSlope(Vector3 worldPos);
        float SampleDensity(Vector3 worldPos);
        float SampleOcclusion(Vector3 worldPos);
        float SamplePathWidth(Vector3 worldPos);
    }

    public interface IContextAnalyzer
    {
        List<string> AnalyzeTerrain(Vector3 worldPos);
    }

    public interface IHNGSController
    {
        float GetIntensityAtNode(int nodeIndex);
        void RequestWidthModulation(int edgeIndex, float widthFactor);
    }

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
        float GetAxis(string axisName);
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
    // [NGO-A] ITrustProvider — v3 N:M 관계 확장
    // ═══════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// NPC의 플레이어에 대한 신뢰도를 조회·수정. 4 Tier 구조.
    /// Hostility(0~19) / Doubt(20~59) / Cooperation(60~89) / BlindTrust(90~100).
    /// [v3 NGO-A] N:M 관계 — 멀티플레이어 환경에서는 ulong playerId 오버로드 사용.
    /// 기존 API(매개변수 없음)는 단독 Play 또는 "현재 로컬 플레이어" 의미로 유지.
    /// </summary>
    public interface ITrustProvider
    {
        // ─── 기존 API (Day 2 호환) ─────────────────────────────────
        float GetNormalizedTrust();
        int GetCurrentTierIndex();
        void AddDelta(float amount, string reason);
        bool EvaluateCommand(float commandSeverity, float npcFear);

        // ─── [NGO-A] 신규 N:M API (Day 3~) ─────────────────────────
        /// <summary>[NGO-A] 특정 플레이어에 대한 정규화 신뢰도 0~1.</summary>
        float GetNormalizedTrust(ulong playerId);

        /// <summary>[NGO-A] 특정 플레이어에 대한 Tier 인덱스.</summary>
        int GetCurrentTierIndex(ulong playerId);

        /// <summary>[NGO-A] 특정 플레이어 관련 신뢰도 변화.</summary>
        void AddDelta(ulong playerId, float amount, string reason);

        /// <summary>[NGO-A] 특정 플레이어의 명령 수락 평가.</summary>
        bool EvaluateCommand(ulong playerId, float commandSeverity, float npcFear);
    }

    /// <summary>NPC의 트라우마 상태. 4 Stage: None/AcuteShock/Crossroads/PermanentScarring.</summary>
    public interface ITraumaProvider
    {
        int CurrentStageIndex { get; }
        void ApplyShock(float intensity, string causeId);
        float GetFearMultiplier();
    }

    /// <summary>NPC의 진영(Alignment) 상태 조회.</summary>
    /// <remarks>
    /// 4 Alignment: Hostile=0 / Neutral=1 / Friendly=2 / Companion=3.
    /// 매 1초 trust + karma 기반 동적 전이 평가 (NPCAlignmentController, Day 3 T3.2).
    /// </remarks>
    public interface IAlignmentProvider
    {
        int CurrentAlignmentIndex { get; }
        bool IsHostileTo(UnityEngine.GameObject other);
        event System.Action<int, int> OnAlignmentChanged;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // [Day 3 T3.5 신규] ICommandFilter — 명령 수락/거부 판정
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>명령 위험도 5단계.</summary>
    public enum CommandSeverity
    {
        Trivial = 0,     // 가만히 있어, 여기 있어 — 거의 무조건 수락
        Minor = 1,       // 따라와, 줍다 — 낮은 신뢰도 거부
        Moderate = 2,    // 공격해, 밀어내 — 중간 신뢰도 필요
        Severe = 3,      // 혼자 적 막아 — 높은 신뢰도 + Trust Companion 필요
        Suicidal = 4     // 폭탄 들고 돌격 — Blind Trust 외 거의 거부
    }

    /// <summary>명령 요청 구조체.</summary>
    [Serializable]
    public struct CommandRequest : INetworkSerializable
    {
        public ulong issuerPlayerId;          // 명령자 clientId
        public FixedCommandId commandId;      // 명령 종류
        public CommandSeverity severity;      // 위험도
        public Vector3 targetPosition;        // 좌표
        public ulong targetEntityId;          // 타겟 NetworkObjectId (0=없음)

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref issuerPlayerId);
            s.SerializeValue(ref commandId);
            // [NGO 호환] enum은 int로 캐스팅하여 직렬화 (안전)
            int sev = (int)severity;
            s.SerializeValue(ref sev);
            severity = (CommandSeverity)sev;
            s.SerializeValue(ref targetPosition);
            s.SerializeValue(ref targetEntityId);
        }
    }

    /// <summary>명령 ID (FixedString32으로 네트워크 전송).</summary>
    [Serializable]
    public struct FixedCommandId : INetworkSerializable, IEquatable<FixedCommandId>
    {
        public Unity.Collections.FixedString32Bytes value;

        public static FixedCommandId From(string s) =>
            new FixedCommandId { value = new Unity.Collections.FixedString32Bytes(s) };

        public override string ToString() => value.ToString();
        public bool Equals(FixedCommandId o) => value.Equals(o.value);

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            => s.SerializeValue(ref value);
    }

    /// <summary>명령 수락 판정 계약.</summary>
    /// <remarks>
    /// 구현체: CommandAcceptanceFilter (Day 3 T3.5).
    /// 수식: acceptance = Trust × (1 - Severity/4) × (1/FearMult) × Loyalty
    /// </remarks>
    public interface ICommandFilter
    {
        /// <summary>명령 수락 여부 결정. 서버 권한 검증.</summary>
        bool TryAccept(CommandRequest request, out float acceptance, out string reason);

        /// <summary>명령 수락 임계값. 기본 0.3.</summary>
        float AcceptanceThreshold { get; set; }
    }
}

// ============ NARRATIVE ============
namespace TDA.PB4.Interfaces.Narrative
{
    public interface IMemoryProvider
    {
        string RecallSimilarIncident(float[] situationVector, float minSimilarity);
    }

    /// <summary>결정적 사건 기록기.</summary>
    /// <remarks>
    /// [Day 3 T3.3] Vector 인덱싱 hook 추가 (Wk7 RAG 준비).
    /// Karma 변환 내재화: RecordIncident 시 KarmaDirector에 자동 반영.
    /// </remarks>
    public interface IIncidentRecorder
    {
        void RecordIncident(string incidentId, float intensityScore, string moralAlignment);

        /// <summary>[Day 3] Vector 인덱싱 enable hook. Wk7 RAG 연결용.</summary>
        void SetVectorIndexer(System.Action<string, float[]> indexer);
    }

    /// <summary>업보/카르마 방향자.</summary>
    /// <remarks>
    /// [v3 NGO-B] ulong playerId 기반. 개론서 §1.20 ServerCharacterRegistry 정합.
    /// 기존 string characterId API도 유지 (호환성, 내부에서 ulong.Parse 변환).
    /// </remarks>
    public interface IKarmaDirector
    {
        // ─── 기존 API (호환성 유지) ─────────────────────────────
        float GetKarmaScore(string characterId);
        void ApplyKarmaShift(string characterId, float delta);

        // ─── [NGO-B] 신규 ulong 기반 API ────────────────────────
        /// <summary>[NGO-B] playerId 기반 Karma 조회.</summary>
        float GetKarmaScore(ulong playerId);

        /// <summary>[NGO-B] playerId 기반 Karma 변화.</summary>
        void ApplyKarmaShift(ulong playerId, float delta, string reason);

        /// <summary>[Day 3 T3.1] 현재 Karma Tier.</summary>
        KarmaTier GetTier(ulong playerId);
    }

    /// <summary>[Day 3] 플레이어 전역 도덕 Tier.</summary>
    public enum KarmaTier
    {
        Demon = 0,      // karma < -90
        Outlaw = 1,     // -89 ~ -50
        Neutral = 2,    // -49 ~ 49
        Saint = 3       // 50 ~ 100
    }

    /// <summary>[Day 3] Karma 변화 사유.</summary>
    public enum KarmaChangeReason
    {
        Manual = 0,
        KilledCivilian = 1,
        KilledOutlaw = 2,
        RescuedCivilian = 3,
        RescuedAlly = 4,
        StolenItem = 5,
        BetrayedAlly = 6,
        SavedFromDeath = 7,
        DilemmaChoice = 8,
        NaturalDecay = 9
    }

    /// <summary>[Day 3] Alignment 전이 사유.</summary>
    public enum AlignmentChangeReason
    {
        Manual = 0,
        TrustTrigger = 1,       // Trust Tier 변화로 자동 전이
        KarmaTrigger = 2,       // Karma 변화로 자동 전이
        PivotResolution = 3,    // DilemmaPivot 통과로 강제 전이 (Hostile → Companion 등 영구 변화)
        ForcedByDesigner = 4
    }
}

// ============ PRESENTATION ============
namespace TDA.PB4.Interfaces.Presentation
{
    public interface ISpeechAssembler
    {
        string AssembleSpeech(float[] personalityVector, string contextTag);
    }

    public interface IDialogueRenderer
    {
        void ShowDialogue(string speakerName, string text, float duration);
        void HideDialogue();
    }
}
