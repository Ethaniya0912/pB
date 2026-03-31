// =============================================================================
// EventBus.cs  |  pB-4 Project — Week 0
// Layer  : Core (공유 계층)
// Owner  : Person A
//
// 역할:
//   3개 파트 간 느슨한 결합(Loose Coupling)을 위한 중앙 이벤트 버스.
//   송신자는 이벤트를 '발행'만 하고, 수신자는 '구독'만 한다.
//   Week 0에서 6종 이벤트 서명을 동결.
//
// 아키텍처 규약:
//   - Static 이벤트로 구현 (씬 전환 시에도 유지)
//   - 모든 구독자는 OnEnable/OnDisable에서 반드시 짝(Pair)을 맞춰 구독/해제
//   - L3 Domain에서 직접 다른 Domain을 호출하는 대신 이 버스를 사용
// =============================================================================
using System;
using System.Collections.Generic;

namespace TDA.PB4.Core
{
    /// <summary>
    /// pB-4 전역 이벤트 버스. Week 0 동결 — 이벤트 서명 변경 금지.
    /// </summary>
    public static class EventBus
    {
        // ==================================================================
        // [1] 지형 태그 변경 — 지형 파트가 발행, AI/시나리오가 구독
        // ==================================================================
        public static event Action<IReadOnlyList<string>> OnTerrainTagChanged;
        public static void RaiseTerrainTagChanged(IReadOnlyList<string> tags)
            => OnTerrainTagChanged?.Invoke(tags);

        // ==================================================================
        // [2] 아크 Phase 변경 — 시나리오 파트가 발행, 지형/AI가 구독
        // ==================================================================
        public static event Action<string /*arcId*/, int /*phaseIndex*/> OnArcPhaseChanged;
        public static void RaiseArcPhaseChanged(string arcId, int phaseIndex)
            => OnArcPhaseChanged?.Invoke(arcId, phaseIndex);

        // ==================================================================
        // [3] 사건 기록 — 시나리오 파트가 발행, AI(기억)/지형(고착화)가 구독
        // ==================================================================
        public static event Action<string /*incidentId*/> OnIncidentRecorded;
        public static void RaiseIncidentRecorded(string incidentId)
            => OnIncidentRecorded?.Invoke(incidentId);

        // ==================================================================
        // [4] 에스컬레이션 트리거 — 시나리오가 발행, AI(GroupAI)가 구독
        // ==================================================================
        public static event Action<string /*factionId*/, int /*escalationLevel*/> OnEscalationTriggered;
        public static void RaiseEscalationTriggered(string factionId, int escalationLevel)
            => OnEscalationTriggered?.Invoke(factionId, escalationLevel);

        // ==================================================================
        // [5] 팩션 상태 변경 — 시나리오/AI가 발행, SFX/지형이 구독
        // ==================================================================
        public static event Action<string /*factionId*/, FactionWorldState> OnFactionStateChanged;
        public static void RaiseFactionStateChanged(string factionId, FactionWorldState state)
            => OnFactionStateChanged?.Invoke(factionId, state);

        // ==================================================================
        // [6] 바이옴 스폰 완료 — 지형이 발행, AI(스포너)가 구독
        // ==================================================================
        public static event Action<int /*chunkIndex*/> OnBiomeSpawnCompleted;
        public static void RaiseBiomeSpawnCompleted(int chunkIndex)
            => OnBiomeSpawnCompleted?.Invoke(chunkIndex);

        // ==================================================================
        // 씬 전환 시 모든 구독 강제 해제 (안전망)
        // ==================================================================
        public static void ClearAll()
        {
            OnTerrainTagChanged = null;
            OnArcPhaseChanged = null;
            OnIncidentRecorded = null;
            OnEscalationTriggered = null;
            OnFactionStateChanged = null;
            OnBiomeSpawnCompleted = null;
        }
    }
}
