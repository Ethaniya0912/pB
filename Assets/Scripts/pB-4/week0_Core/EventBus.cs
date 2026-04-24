// =============================================================================
// EventBus.cs  |  pB-4 Project — Week 0 (Day 2 T2.4 v3 Errata F6) → v3 Day 3 확장
// Layer  : Core (공유 계층)
// Owner  : Person A
//
// 역할:
//   3개 파트 간 느슨한 결합을 위한 중앙 이벤트 버스.
//
// 아키텍처 규약:
//   - Static 이벤트 (씬 전환 시에도 유지)
//   - 모든 구독자는 OnEnable/OnDisable에서 짝을 맞춰 구독/해제
//   - L3 Domain에서 직접 다른 Domain 호출 대신 이 버스를 사용
//
// [Day 2 T2.4 v3 변경 — Errata F6]
//   - [9] OnTrustTierChanged 추가
//
// [Day 3 v4 확장 — 2026-04-23]
//   - [10] OnKarmaChanged (playerId, oldKarma, newKarma, reason)
//   - [11] OnKarmaTierChanged (playerId, oldTier, newTier)
//   - [12] OnAlignmentChanged (npcId, oldAlignment, newAlignment, reason)
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Interfaces.Narrative;  // [Day 3] KarmaTier, KarmaChangeReason, AlignmentChangeReason

namespace TDA.PB4.Core
{
    /// <summary>pB-4 전역 이벤트 버스. Week 0 동결 — 이벤트 서명 변경 금지.</summary>
    public static class EventBus
    {
        // ==================================================================
        // [1] 지형 태그 변경
        // ==================================================================
        public static event Action<IReadOnlyList<string>> OnTerrainTagChanged;
        public static void RaiseTerrainTagChanged(IReadOnlyList<string> tags)
            => OnTerrainTagChanged?.Invoke(tags);

        // ==================================================================
        // [2] 아크 Phase 변경
        // ==================================================================
        public static event Action<string /*arcId*/, int /*phaseIndex*/> OnArcPhaseChanged;
        public static void RaiseArcPhaseChanged(string arcId, int phaseIndex)
            => OnArcPhaseChanged?.Invoke(arcId, phaseIndex);

        // ==================================================================
        // [3] 사건 기록
        // ==================================================================
        public static event Action<string /*incidentId*/> OnIncidentRecorded;
        public static void RaiseIncidentRecorded(string incidentId)
            => OnIncidentRecorded?.Invoke(incidentId);

        // ==================================================================
        // [4] 에스컬레이션 트리거
        // ==================================================================
        public static event Action<string /*factionId*/, int /*escalationLevel*/> OnEscalationTriggered;
        public static void RaiseEscalationTriggered(string factionId, int escalationLevel)
            => OnEscalationTriggered?.Invoke(factionId, escalationLevel);

        // ==================================================================
        // [5] 팩션 상태 변경
        // ==================================================================
        public static event Action<string /*factionId*/, FactionWorldState> OnFactionStateChanged;
        public static void RaiseFactionStateChanged(string factionId, FactionWorldState state)
            => OnFactionStateChanged?.Invoke(factionId, state);

        // ==================================================================
        // [6] 바이옴 스폰 완료
        // ==================================================================
        public static event Action<int /*chunkIndex*/> OnBiomeSpawnCompleted;
        public static void RaiseBiomeSpawnCompleted(int chunkIndex)
            => OnBiomeSpawnCompleted?.Invoke(chunkIndex);

        // ==================================================================
        // [7] 소리 이벤트
        // ==================================================================
        public static event Action<Vector3, TDA.PB4.AI.Perception.SoundType, float> OnSoundEmitted;
        public static void RaiseSoundEmitted(
            Vector3 position,
            TDA.PB4.AI.Perception.SoundType type,
            float volume)
            => OnSoundEmitted?.Invoke(position, type, volume);

        // ==================================================================
        // [8] 팩션 감지 이벤트
        // ==================================================================
        public static event Action<Transform> OnFactionDetectedPlayer;
        public static void RaiseFactionDetectedPlayer(Transform detectedTransform)
            => OnFactionDetectedPlayer?.Invoke(detectedTransform);

        // ==================================================================
        // [9] [Errata F6] Trust Tier 전이
        // 인자: npcId, oldTier (0=Hostility, 3=BlindTrust), newTier
        // ==================================================================
        public static event Action<string /*npcId*/, int /*oldTier*/, int /*newTier*/> OnTrustTierChanged;
        public static void RaiseTrustTierChanged(string npcId, int oldTier, int newTier)
            => OnTrustTierChanged?.Invoke(npcId, oldTier, newTier);

        // ==================================================================
        // [10] [Day 3 T3.1 신규] Karma 값 변화
        // 발행: KarmaDirector.ChangeKarma
        // 구독: NPCAlignmentController (전이 평가), Dashboard (UI 갱신)
        // 인자: playerId, oldKarma, newKarma, reason
        // ==================================================================
        public static event Action<ulong /*playerId*/, float /*oldKarma*/, float /*newKarma*/, KarmaChangeReason /*reason*/> OnKarmaChanged;
        public static void RaiseKarmaChanged(ulong playerId, float oldKarma, float newKarma, KarmaChangeReason reason)
            => OnKarmaChanged?.Invoke(playerId, oldKarma, newKarma, reason);

        // ==================================================================
        // [11] [Day 3 T3.1 신규] Karma Tier 전이 (Saint/Neutral/Outlaw/Demon)
        // 발행: KarmaDirector 임계값 통과 시
        // 구독: 모든 Civic NPC가 진영 재평가, 보스 이벤트 트리거 (Day 5)
        // 인자: playerId, oldTier, newTier
        // ==================================================================
        public static event Action<ulong /*playerId*/, KarmaTier /*oldTier*/, KarmaTier /*newTier*/> OnKarmaTierChanged;
        public static void RaiseKarmaTierChanged(ulong playerId, KarmaTier oldTier, KarmaTier newTier)
            => OnKarmaTierChanged?.Invoke(playerId, oldTier, newTier);

        // ==================================================================
        // [12] [Day 3 T3.2 신규] NPCAlignment 전이 (Hostile/Neutral/Friendly/Companion)
        // 발행: NPCAlignmentController.TryTransition
        // 구독: SpeechAssembler (트리거 라인), BT (분기 재평가), Dashboard
        // 인자: npcId, oldAlignment, newAlignment, reason
        // ==================================================================
        public static event Action<string /*npcId*/, int /*oldAlign*/, int /*newAlign*/, AlignmentChangeReason /*reason*/> OnAlignmentChanged;
        public static void RaiseAlignmentChanged(string npcId, int oldAlignment, int newAlignment, AlignmentChangeReason reason)
            => OnAlignmentChanged?.Invoke(npcId, oldAlignment, newAlignment, reason);

        // ==================================================================
        // 씬 전환 시 모든 구독 강제 해제
        // ==================================================================
        public static void ClearAll()
        {
            OnTerrainTagChanged = null;
            OnArcPhaseChanged = null;
            OnIncidentRecorded = null;
            OnEscalationTriggered = null;
            OnFactionStateChanged = null;
            OnBiomeSpawnCompleted = null;
            OnSoundEmitted = null;
            OnFactionDetectedPlayer = null;
            OnTrustTierChanged = null;      // [Errata F6]
            OnKarmaChanged = null;           // [Day 3]
            OnKarmaTierChanged = null;       // [Day 3]
            OnAlignmentChanged = null;       // [Day 3]
        }
    }
}
