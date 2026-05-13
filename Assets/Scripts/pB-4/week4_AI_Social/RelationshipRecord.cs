// ═══════════════════════════════════════════════════════════════════════════════
// RelationshipRecord.cs (v1.0)
// ═══════════════════════════════════════════════════════════════════════════════
// AI 내부 자료 — NPC 간 관계 1 건. NamedNPCSnapshot 의 relationships 목록에 포함.
// Wk4-4 AbandonRescue 결과 + Wk2 Karma 결합으로 변동.
//
// ─── 변경 이력 ────────────────────────────────────────────────────────────────
//
// [2026-05-12 v1.0] — WkSocialContracts.cs 에서 분리 이전
//   분리 이유: 본 자료 = AI 내부 사용 (다른 파트 노출 X)
//              Contracts 폴더가 아닌 AI 도메인 안 (week4_AI_Social) 위치 적절.
//
// ─── 기본 정보 ────────────────────────────────────────────────────────────────
//
// 위치     : Assets/Scripts/pB-4/week4_AI_Social/RelationshipRecord.cs
// namespace: TDA.PB4.AI.Social (AI 도메인 내부)
// 호출자   : HumanoidAIBrain / AbandonRescueResolver / SaveLoad (NamedNPCSnapshot)
// 버전     : v1.0 (2026-05-12)
// 기반     : Wk4 의제 종합 v2 / 세이브/로드 시스템 v1 §5.3
// ═══════════════════════════════════════════════════════════════════════════════

using System;

namespace TDA.PB4.AI.Social
{
    // ═══════════════════════════════════════════════════════════════════
    // RelationshipRecord — NPC 간 관계
    // ═══════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// NPC 간 관계 1 건. NamedNPCSnapshot 의 relationships 목록에 포함.
    /// Wk4-4 AbandonRescue 결과 + Wk2 Karma 결합으로 변동.
    /// </summary>
    [Serializable]
    public struct RelationshipRecord
    {
        public string targetNpcId;          // 상대 NPC ID
        public RelationshipType type;
        public float strength;              // 0~1
        public string[] tags;               // "생명의 은인" / "비겁자" / "Betrayed" 등
        public float lastInteractionTime;
    }
    
    /// <summary>NPC 간 관계 종류 — 8 종.</summary>
    public enum RelationshipType
    {
        Stranger = 0,
        Acquaintance = 1,
        Friend = 2,
        Trusted = 3,
        Lover = 4,
        Rival = 5,
        Enemy = 6,
        Betrayer = 7        // 배신자 / 유기자
    }
}
