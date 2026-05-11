// =============================================================================
// AIDomainInterfaces.cs  |  AI 도메인 내부 인터페이스
// Layer  : AI Domain Internal
// Namespace: TDA.PB4.Interfaces.Intelligence (AI 내부 도구) +
//            TDA.PB4.Interfaces (Wk3 Forward)
//
// 역할:
//   AI 도메인이 내부적으로 사용하는 인터페이스 모음.
//   Bridge 무관 — 도메인 내부 직접 호출.
//
// 수록:
//   [Wk0 — Intelligence namespace]
//     IUtilityScorer, IPersonalityEngine, IBTNodeExecutor, IFactionGroupPolicy
//   [Wk3 — Forward namespace]
//     IGroupAIInfo, IContextProvider, ContextSnapshot
//
// 이력:
//   2026-04-23 Wk0 — PB4Interfaces.cs 의 Intelligence namespace 부분
//   2026-05-03 Wk3 v4 — PB4_Wk3Interfaces.cs Forward 인터페이스
//   2026-05-11 정리 — AIDomainInterfaces.cs 로 통합 (Interfaces/Domain/)
//                    파일명에서 PB4 접두사 제거
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using CaveSystem;  // Wk3 v4 — Tier 2 의 enum 들 (ElementKind, NodeRole, *Tag)

// ============ Wk0 — AI 내부 도구 ============
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
}

// ============ Wk3 Forward — Group / Context ============
namespace TDA.PB4.Interfaces
{
    /// <summary>
    /// GroupAIManager 의 읽기 전용 정보 인터페이스.
    /// IGroupDashboard (Wk5) 의 부모. AIBridgeContracts.cs 참조.
    /// </summary>
    public interface IGroupAIInfo
    {
        /// <summary>그룹 사기 (0~1).</summary>
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
        ContextSnapshot Sample(Vector3 worldPos);
    }

    /// <summary>
    /// Context 시스템의 단일 스냅샷 (Wk3 v4 — Tier 2 우선 디렉션 반영, 16 필드).
    /// WorldAIContextManager.Sample() 반환 + BlackboardUpdater 입력.
    /// 자연 발현 박제: 모든 필드는 Tier 2 그래프 + JunctionTagLookupTable rule 자연 발현 결과.
    /// </summary>
    [System.Serializable]
    public struct ContextSnapshot
    {
        // ① 그래프 위치 (Tier 2 v4)
        public int nearestNodeIdx;
        public int nearestPassageIdx;
        public ElementKind nearestKind;

        // ② 4 카테고리 비트마스크 (Tier 2 ContextTagSet 60 bit)
        public uint identityTags;
        public uint stateTags;
        public uint eventTags;
        public uint potentialTags;

        // ③ Role (Tier 2)
        public NodeRole nodeRole;
        public PassageRole passageRole;

        // ④ 4 측정값 (v3 호환 — PassageProfile 에서 추출)
        public float slope;
        public float density;
        public float occlusion;
        public float pathWidth;

        // ⑤ 그룹 AI (v3 호환)
        public float groupMorale;
        public bool attackTokenAvailable;

        // ⑥ Phase 2~3 호환 다운샘플 (Phase 4 후 제거 예정)
        public List<string> terrainTags;

        // Helper 메서드 — 비트 검사 (v4 신규)
        public bool HasIdentity(IdentityTag tag) => (identityTags & (uint)tag) != 0;
        public bool HasState(StateTag tag) => (stateTags & (uint)tag) != 0;
        public bool HasEvent(EventTag tag) => (eventTags & (uint)tag) != 0;
        public bool HasPotential(PotentialTag tag) => (potentialTags & (uint)tag) != 0;
    }
}
