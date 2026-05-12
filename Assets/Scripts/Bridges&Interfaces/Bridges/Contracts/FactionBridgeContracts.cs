// =============================================================================
// FactionBridgeContracts.cs  |  Faction 도메인 Bridge 협업 계약
// Layer  : L2 Application / Domain Boundary
// Namespace: TDA.PB4.Interfaces
//
// 역할:
//   Faction 도메인 Bridge (WorldFactionBridgeManager) 와 외부 도메인
//   (Scenario / AI / UI) 간의 협업 계약을 정의. 본 인터페이스들은 Bridge 의
//   위임 대상이 구현해야 하는 시그니처.
//
// 통신 채널:
//   IFactionStateProvider        : 외부 → Faction (조회 + 변경 알림 event)
//   IFactionStateCommandReceiver : 외부 → Faction (Set / Force 변경)
//
// 이력:
//   2026-05-11 — P-02 v1.1 — pB-4/week5_Faction/Interfaces/ 에 작성
//   2026-05-11 — P-02 v1.2 — Bridge v13 표준 폴더 (Bridges/Contracts/) 로 이동
//                            + 두 파일 통합 (AI/Terrain BridgeContracts 패턴 정합)
//                            + namespace 변경: TDA.PB4.Faction.Interfaces → TDA.PB4.Interfaces
//
// 참조: pB-4 Bridges & Interfaces 통합 가이드 v1.2 §7
// =============================================================================
using System;
using System.Collections.Generic;
using TDA.PB4.Faction;        // FactionStateSnapshot, FactionStateChangeEventArgs (도메인 DTO)
using TDA.PB4.Faction.Tags;   // 4 Tag enum (Mood / Tactical / Lifecycle / Relation)

namespace TDA.PB4.Interfaces
{
    // ═════════════════════════════════════════════════════════════════════
    // IFactionStateProvider — 외부 → Faction 조회 + 변경 알림
    //
    // 구현체: WorldFactionStateManager (TDA.PB4.Faction)
    // 호출자: WorldFactionBridgeManager (Bridge 가 위임) ← 외부 도메인 전체
    //
    // 패턴:
    //   - 조회 메서드 3 개 (GetSnapshot / HasFaction / GetAllFactionIds)
    //   - 변경 알림 event 1 개 (Bridge 가 add/remove 패턴으로 위임)
    //
    // ★ event 포함 — 사용자 측 IGroupDashboard / IAsyncSpawnRequestReceiver 패턴 정합
    // ═════════════════════════════════════════════════════════════════════
    public interface IFactionStateProvider
    {
        /// <summary>
        /// 본 시점의 Faction 상태 스냅샷 반환.
        /// 등록되지 않은 factionId 면 default (IsValid = false) 반환.
        /// </summary>
        FactionStateSnapshot GetSnapshot(string factionId);
        
        /// <summary> 등록 여부 검사 </summary>
        bool HasFaction(string factionId);
        
        /// <summary> 등록된 모든 factionId 목록 </summary>
        IReadOnlyList<string> GetAllFactionIds();
        
        /// <summary>
        /// Faction 상태 변경 이벤트.
        /// Bridge 가 본 event 를 add/remove 패턴으로 위임.
        /// 외부 도메인 (GroupAIManager / Audio / VFX 등) 가 구독.
        /// </summary>
        event Action<FactionStateChangeEventArgs> OnFactionStateChanged;
    }

    // ═════════════════════════════════════════════════════════════════════
    // IFactionStateCommandReceiver — 외부 → Faction 변경 명령
    //
    // 구현체: WorldFactionStateManager (TDA.PB4.Faction)
    // 호출자: WorldFactionBridgeManager (Bridge 가 위임) ← AI 자율 / Scenario
    //
    // SetXxx vs ForceXxx 분리:
    //   - SetXxx — DefaultMask 검사 (AI 자율 변경, 디자이너 의도 보호)
    //   - ForceXxx — DefaultMask 우회 (시나리오 명시 override)
    //
    // 4 Tag (Mood / Tactical / Lifecycle / Relation) 각각 Set/Force = 총 8 메서드.
    // ═════════════════════════════════════════════════════════════════════
    public interface IFactionStateCommandReceiver
    {
        // ────────────────────────────────────────
        // AI 자율 변경 — DefaultMask 검사
        // 위반 시 false 반환 + 경고 로그
        // ────────────────────────────────────────
        
        bool SetMood     (string factionId, FactionMoodTag      tag, bool on);
        bool SetTactical (string factionId, FactionTacticalTag  tag, bool on);
        bool SetLifecycle(string factionId, FactionLifecycleTag tag, bool on);
        bool SetRelation (string factionId, FactionRelationTag  tag, bool on);
        
        // ────────────────────────────────────────
        // 시나리오 명시 — DefaultMask 우회
        // 호출자가 명시적으로 책임 가짐
        // ────────────────────────────────────────
        
        void ForceMood     (string factionId, FactionMoodTag      tag, bool on);
        void ForceTactical (string factionId, FactionTacticalTag  tag, bool on);
        void ForceLifecycle(string factionId, FactionLifecycleTag tag, bool on);
        void ForceRelation (string factionId, FactionRelationTag  tag, bool on);
    }
}
