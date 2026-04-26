// =============================================================================
// SpeechTriggerContext.cs  |  pB-4 Week 0 Core / Events
// Layer  : Core / Events (EventBus 페이로드)
// Owner  : Person A
//
// 역할:
//   EventBus.RaiseSpeechTrigger 호출 시 함께 전달되는 컨텍스트.
//   Dispatcher / Assembler / Renderer가 공통으로 참조.
//
// 위치 결정 (2026-04-23 리뷰):
//   이 struct는 EventBus의 페이로드이므로 Week 0 Core에 소속.
//   Week 2 AI.Speech namespace에서 Core를 참조하는 방향으로 통일.
//   → Assembly Definition 도입 시 순환 참조 회피.
//
// 4계층 원칙:
//   - [Rule 1] Cross-Domain 금지 — Assembler는 KarmaDirector/TrustMatrix를
//     직접 참조하지 않고 이 struct만 받음.
//   - struct이므로 GC 할당 없음 (이벤트 발행 시 경량).
//
// Week 7 확장:
//   - PersonalityMatrix 필드 추가하여 5축 유클리드 매칭에 사용.
//   - TraumaStage 필드 추가하여 Glitch 발화 트리거.
// =============================================================================
using TDA.PB4.Data;                   // NPCAlignment
using TDA.PB4.Interfaces.Narrative;   // AlignmentChangeReason

namespace TDA.PB4.Core.Events
{
    /// <summary>SpeechTrigger 발행 시 수반되는 컨텍스트. Core 레이어 소속.</summary>
    public struct SpeechTriggerContext
    {
        /// <summary>발화 주체 NPC 식별자 (GameObject.name 사용).</summary>
        public string characterId;

        /// <summary>대화 대상 플레이어 ID (0 = 로컬 플레이어 / 미지정).</summary>
        public ulong targetPlayerId;

        /// <summary>현재 진영 (전이 후 값).</summary>
        public NPCAlignment currentAlignment;

        /// <summary>직전 진영 (전이 전 값).</summary>
        public NPCAlignment previousAlignment;

        /// <summary>전이 사유 (KarmaTrigger / PivotResolution / Manual 등).</summary>
        public AlignmentChangeReason reason;

        public override string ToString()
            => $"SpeechCtx({characterId}, {previousAlignment}→{currentAlignment}, {reason})";
    }
}
