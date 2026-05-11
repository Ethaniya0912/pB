// =============================================================================
// NarrativeDomainInterfaces.cs  |  Narrative 도메인 내부 인터페이스
// Layer  : Narrative Domain Internal
// Namespace: TDA.PB4.Interfaces.Narrative (Wk0 유지)
//
// 역할:
//   Narrative 도메인 (Karma / Memory / Incident) 인터페이스 모음.
//   v3 NGO-B + Day 3 T3.1/T3.3 추가 반영.
//
// 수록:
//   [v3 NGO-B] IKarmaDirector (ulong playerId 기반)
//   [Wk0] IMemoryProvider, IIncidentRecorder
//   [Day 3 T3.1] KarmaTier (Saint/Neutral/Outlaw/Demon)
//   [Day 3] KarmaChangeReason, AlignmentChangeReason
//
// 이력:
//   2026-04-23 v3 — PB4Interfaces.cs Narrative namespace
//   2026-05-11 정리 — NarrativeDomainInterfaces.cs 로 분리
//                    파일명에서 PB4 접두사 제거
// =============================================================================

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
        float GetKarmaScore(ulong playerId);
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
