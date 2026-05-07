// ─────────────────────────────────────────────────────────────────────
// pB-4 Week 3 — Wk3 신규 Forward Interfaces (v4 — Tier 2 우선 디렉션 반영)
// ─────────────────────────────────────────────────────────────────────
// 갱신: 2026-05-03 v4 — Tier 2 인도분 (IJunctionContextAnalyzer + ContextTagSet 60 bit) 활용
//   v3 → v4 변경 사항:
//     [확장] ContextSnapshot 7 필드 → 16 필드
//     [추가] 그래프 위치 3 필드 (nearestNodeIdx / nearestPassageIdx / nearestKind)
//     [추가] 4 카테고리 비트마스크 (identityTags / stateTags / eventTags / potentialTags)
//     [추가] Role 2 필드 (nodeRole / passageRole)
//     [추가] Helper 메서드 4종 (HasIdentity / HasState / HasEvent / HasPotential)
//     [보존] terrainTags (List<string>) — Phase 2~3 호환 다운샘플, Phase 4 후 제거 예정
//     [보존] slope / density / occlusion / pathWidth — PassageProfile 에서 추출하여 채움
//     [보존] groupMorale / attackTokenAvailable — IGroupAIInfo 그대로
//
//   ★ Wk0 동결 부분 해제 (v11 개론서 § 0.5 박제):
//     ITerrainSampler / IContextAnalyzer 폐기 → IJunctionContextAnalyzer (Tier 2) 우선
//
// 위치: Scripts/pB-4/Interfaces/PB4_Wk3Interfaces.cs
//
// ★ 본 파일은 PB4Interfaces.cs (Wk0 핵심 동결)와 별개. ContextSnapshot 만 확장.
//   기존 인터페이스 (ITerrainSampler 등)는 PB4Interfaces.cs 그대로 보존 (history).
//
// 의존성:
//   - CaveSystem 네임스페이스 (Tier 2 인도분):
//     ElementKind, NodeRole, PassageRole, IdentityTag, StateTag, EventTag, PotentialTag
// ─────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;
using CaveSystem;  // ★ v4 — Tier 2 의 enum 들 (ElementKind, NodeRole, *Tag)

namespace TDA.PB4.Interfaces
{
    /// <summary>
    /// GroupAIManager의 읽기 전용 정보 인터페이스.
    /// D3 PM 작성된 GroupAIManager 가 본 인터페이스 구현 (v3.3 패치).
    /// WorldAIContextManager 가 본 인터페이스를 통해 그룹 상태 조회.
    /// </summary>
    public interface IGroupAIInfo
    {
        /// <summary>그룹 사기 (0~1). 1.0 = 만전, 0.0 = 패주.</summary>
        float GetMorale();

        /// <summary>공격 토큰 보유 가능 여부.</summary>
        bool HasAvailableToken();
    }

    /// <summary>
    /// WorldAIContextManager 가 외부에 노출하는 인터페이스.
    /// BlackboardUpdater + 향후 다른 소비자가 본 인터페이스 통해 접근.
    /// </summary>
    public interface IContextProvider
    {
        /// <summary>지정 좌표의 컨텍스트 스냅샷 반환 (v4 기준 16 필드).</summary>
        ContextSnapshot Sample(Vector3 worldPos);
    }

    /// <summary>
    /// Context 시스템의 단일 스냅샷 (★ v4 — Tier 2 우선 디렉션 반영, 16 필드).
    /// WorldAIContextManager.Sample() 반환 + BlackboardUpdater 입력.
    ///
    /// ★ 자연 발현 박제: 모든 필드는 Tier 2 그래프 + JunctionTagLookupTable rule 자연 발현 결과.
    ///
    /// ★ Phase 2~3 호환 전략:
    ///   terrainTags (List&lt;string&gt;) 는 Phase 4 BaseAIBrain.UpdateFearFromTerrain
    ///   마이그레이션 전까지 보존. WorldAIContextManager 가 60 bit ContextTagSet
    ///   → 5 단순 string 으로 다운샘플하여 채움. Phase 4 후 제거 가능.
    /// </summary>
    [System.Serializable]
    public struct ContextSnapshot
    {
        // ═════════════════════════════════════════════════════════════
        // ① 그래프 위치 (★ v4 신규 — Tier 2)
        // ═════════════════════════════════════════════════════════════
        /// <summary>가장 가까운 노드 인덱스 (-1 = 없음).</summary>
        public int nearestNodeIdx;

        /// <summary>가장 가까운 Passage(엣지) 인덱스 (-1 = 없음).</summary>
        public int nearestPassageIdx;

        /// <summary>가장 가까운 그래프 요소의 종류 (Node / Passage / Region / Crossover).</summary>
        public ElementKind nearestKind;

        // ═════════════════════════════════════════════════════════════
        // ② 4 카테고리 비트마스크 (★ v4 신규 — Tier 2 ContextTagSet 60 bit)
        // ═════════════════════════════════════════════════════════════
        /// <summary>
        /// IDENTITY (15 bit) — Role 미러 (BOSS / AMBUSH / NEXUS / FRONTIER 등).
        /// 비트 검사: HasIdentity(IdentityTag.IDENTITY_BOSS).
        /// </summary>
        public uint identityTags;

        /// <summary>
        /// STATE (16 bit) — 현재 지형 상태 (NARROW / GRAND / ELEVATED / CRITICAL_RISK 등).
        /// 비트 검사: HasState(StateTag.STATE_NARROW).
        /// </summary>
        public uint stateTags;

        /// <summary>
        /// EVENT (14 bit) — 추적 이벤트 (REPOSITIONED / CHAMBER_LOCKED / ROLE_CHANGED 등).
        /// 비트 검사: HasEvent(EventTag.EVENT_REPOSITIONED).
        /// </summary>
        public uint eventTags;

        /// <summary>
        /// POTENTIAL (15 bit) — 잠재 가능성 (AMBUSH / FALL_HAZARD / SACRED_VENUE 등).
        /// 비트 검사: HasPotential(PotentialTag.POTENTIAL_AMBUSH).
        /// </summary>
        public uint potentialTags;

        // ═════════════════════════════════════════════════════════════
        // ③ Role (★ v4 신규 — Tier 2)
        // ═════════════════════════════════════════════════════════════
        /// <summary>가장 가까운 노드의 narrative 역할 (Spawn / Boss / Treasure / Hub / Nexus).</summary>
        public NodeRole nodeRole;

        /// <summary>가장 가까운 Passage 의 narrative 역할 (Connector / Approach / Hazard / Vista 등).</summary>
        public PassageRole passageRole;

        // ═════════════════════════════════════════════════════════════
        // ④ 4 측정값 (v3 호환 — PassageProfile 에서 추출)
        //   ITerrainSampler 폐기로 인해 Tier 2 의 PassageProfile 에서 간접 추출.
        //   slope     ← profile.ySlope
        //   pathWidth ← profile.minLocalWidth
        //   occlusion ← profile.risk.blendRisk
        //   density   ← OverlapSphere 보조 (또는 nodeDegree 정규화)
        // ═════════════════════════════════════════════════════════════
        /// <summary>0~1 (지형 기울기, PassageProfile.ySlope 추출).</summary>
        public float slope;

        /// <summary>0~1 (주변 밀도, OverlapSphere 보조).</summary>
        public float density;

        /// <summary>0~1 (시야 차폐도, PassageProfile.risk.blendRisk).</summary>
        public float occlusion;

        /// <summary>m 단위 (경로 폭, PassageProfile.minLocalWidth).</summary>
        public float pathWidth;

        // ═════════════════════════════════════════════════════════════
        // ⑤ 그룹 AI (v3 호환 — 변경 없음)
        // ═════════════════════════════════════════════════════════════
        /// <summary>0~1 (그룹 사기, 그룹 미존재 시 1.0).</summary>
        public float groupMorale;

        /// <summary>공격 토큰 보유 (그룹 미존재 시 true).</summary>
        public bool attackTokenAvailable;

        // ═════════════════════════════════════════════════════════════
        // ⑥ Phase 2~3 호환 다운샘플 (★ Phase 4 BaseAIBrain 마이그레이션 후 제거 예정)
        //   WorldAIContextManager 가 60 bit ContextTagSet → 단순 5 string 다운샘플:
        //     STATE_NARROW       → "Narrow"
        //     STATE_GRAND/OPEN   → "Open"
        //     STATE_DENSE        → "Dense"
        //     STATE_ISOLATED     → "Sparse"
        //     STATE_ELEVATED     → "HighGround"
        //   GameBlackboard.SetTerrainTags() 가 본 List 를 받아 BaseAIBrain 에 전달.
        // ═════════════════════════════════════════════════════════════
        /// <summary>★ v3 호환 다운샘플 — Phase 4 후 제거 예정.</summary>
        public List<string> terrainTags;

        // ═════════════════════════════════════════════════════════════
        // Helper 메서드 — 비트 검사 (★ v4 신규)
        // ═════════════════════════════════════════════════════════════
        public bool HasIdentity(IdentityTag tag) => (identityTags & (uint)tag) != 0;
        public bool HasState(StateTag tag) => (stateTags & (uint)tag) != 0;
        public bool HasEvent(EventTag tag) => (eventTags & (uint)tag) != 0;
        public bool HasPotential(PotentialTag tag) => (potentialTags & (uint)tag) != 0;
    }
}