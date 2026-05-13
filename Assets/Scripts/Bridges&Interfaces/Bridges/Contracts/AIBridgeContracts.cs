// =============================================================================
// AIBridgeContracts.cs  |  AI 도메인 Bridge 협업 계약
// Layer  : L2 Application / Domain Boundary
// Namespace: TDA.PB4.Interfaces
//
// 역할:
//   AI 도메인 Bridge (WorldAIBridgeManager) 와 외부 도메인 (Scenario 등) 간의
//   협업 계약을 정의. 본 인터페이스들은 Bridge 의 위임 대상이 구현해야 하는 시그니처.
//
// 통신 채널:
//   IGroupDashboard         : AI → 시나리오 (read-only)
//   ISpawnRequestReceiver   : 시나리오 → AI (스폰 명령)
//   IGroupCommandReceiver   : 시나리오 → AI (분대 명령, Wk5+)
//
//   ★ Wk4 추가 (2026-05-12):
//   DilemmaOutcome / DilemmaPath : AI → 시나리오 (DilemmaPivot 분기 결과)
//
// 이력:
//   2026-04-XX 최초 — PB4_Wk5Interfaces.cs 로 작성
//   2026-05-11 이전 — AIBridgeContracts.cs 로 폴더 이동 (Bridges&Interfaces/Bridges/Contracts/)
//                    파일명에서 PB4_Wk5 접두사 제거
//   2026-05-12 — Wk4 본격 진행 결과 통합 (DilemmaOutcome / DilemmaPath 추가)
//                CommandType (분대 명령) 그대로 유지 — Wk4 의 ScenarioCommandType 와는 별도 영역
//
// 참조: Issues Report v7 §11, §13.4 / Wk4 의제 종합 v2 §2.6
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI;

namespace TDA.PB4.Interfaces
{
    // ═════════════════════════════════════════════════════════════════════
    // ISpawnRequestReceiver — 시나리오 → AI 스폰 명령 수신
    //
    // 구현체: WorldAISpawnManager
    // 호출자: WorldAIBridgeManager (Bridge 가 위임) ← MetaScenarioGenerator (Wk5+)
    //
    // 동기/비동기 패턴:
    //   - ExecuteSpawnRequest = 동기 (즉시 응답, Coroutine은 백그라운드)
    //   - QueueRequest = 비동기 (큐 적재, 순차 처리)
    //   - 일반적으로 단일 스폰 = ExecuteSpawnRequest, 다중 = QueueRequest
    // ═════════════════════════════════════════════════════════════════════
    public interface ISpawnRequestReceiver
    {
        /// <summary>
        /// 사전 검증. NavMesh / 한도 / prefab 매핑 / executeBeforeTime 체크.
        /// 큐에 적재하지 않고 호출 가능 여부만 빠르게 확인할 때 사용.
        /// </summary>
        bool CanAcceptRequest(SpawnRequest req, out string failReason);

        /// <summary>
        /// 동기 실행. Coroutine 내부 시작 후 응답 즉시 반환.
        /// 응답의 spawnedMemberIds는 Coroutine 완료 후 채워짐 (이벤트 패턴).
        /// 사전 검증 실패 시 SpawnResult.Failed* 반환.
        /// </summary>
        SpawnRequestResponse ExecuteSpawnRequest(SpawnRequest req);

        /// <summary>
        /// 비동기 큐 적재. priority 기준 정렬 후 순차 처리.
        /// 큐 적재 자체는 즉시 (응답 없음). 처리 결과는 OnSpawnRequestCompleted 이벤트.
        /// </summary>
        void QueueRequest(SpawnRequest req);

        /// <summary>
        /// 큐에 적재된 요청 취소. 이미 처리 시작된 요청은 취소 불가 (false 반환).
        /// </summary>
        bool CancelRequest(Guid requestId);

        /// <summary>현재 큐에 대기 중인 요청 수.</summary>
        int GetQueuedCount();

        /// <summary>비동기 처리 완료 이벤트. QueueRequest 결과 알림용.</summary>
        event Action<SpawnRequestResponse> OnSpawnRequestCompleted;
    }

    // ═════════════════════════════════════════════════════════════════════
    // IGroupDashboard — AI → 시나리오 read-only 인터페이스
    //
    // 구현체: GroupAIManager
    // 호출자: WorldAIBridgeManager (Bridge 가 위임) ← MetaScenarioGenerator 폴링
    //
    // ★ Wk5 합의 회의 후 메서드 13개 시그니처 동결. 현재는 시그니처만 정의.
    // 본체 구현은 GroupAIManager가 Wk5에 추가.
    // ═════════════════════════════════════════════════════════════════════
    public interface IGroupDashboard : IGroupAIInfo
    {
        // ── 식별 ──────────────────────────────
        string GetGroupId();
        string GetFactionId();
        int GetSequenceNum();

        // ── 인구 ──────────────────────────────
        int GetPopulation();
        int GetMaxPopulation();
        int GetReinforcementCount();
        int GetCasualtyCount();

        // ── 상태 ──────────────────────────────
        // GetMorale은 IGroupAIInfo에서 상속
        int GetEscalationLevel();
        float GetCombatElapsedTime();

        // ── 위치/영토 ─────────────────────────
        Vector3 GetCenterPosition();
        int GetCurrentCaveNodeIdx();
        float GetTerritoryRadius();
        string GetCurrentSceneId();

        // ── 욕구 (Wk5+ stub) ──────────────────
        float GetHungerLevel();
        float GetReproductionUrge();
        float GetTerritoryPressure();

        // ── 학습/이력 ─────────────────────────
        int GetDefeatsAtCurrentLocation();
        Vector3? GetLastSafePosition();

        // ── 임계 이벤트 ───────────────────────
        GroupThresholdState[] GetActiveThresholds();
        event Action<GroupThresholdEvent> OnThresholdCrossed;
    }

    // ═════════════════════════════════════════════════════════════════════
    // IGroupCommandReceiver — 시나리오 → AI 분대 명령 (Wk5+)
    //
    // 구현체: GroupAIManager (Wk5+ 분대장 개념 도입 후)
    // 호출자: WorldAIBridgeManager (Bridge 가 위임) ← GroupCommandIssuer
    //
    // ★ §4.7 L4 Tactical Allocation 발동 시 활용.
    // ═════════════════════════════════════════════════════════════════════
    public interface IGroupCommandReceiver
    {
        void IssueCommand(SquadCommand cmd);
        bool CancelCommand(Guid commandId);
        SquadCommand[] GetActiveCommands();
    }

    // ─────────────────────────────────────────────────────────────────────
    // GroupThreshold — IGroupDashboard 부속 데이터 모델
    // ─────────────────────────────────────────────────────────────────────
    public enum GroupThresholdType
    {
        PopulationOverflow,
        PopulationCritical,
        MoraleCollapse,
        MoraleRecovered,
        TerritoryShrunk,
        ReproductionPeak,
        StarvationCrisis,
        LeaderKilled,
        GroupSplit,
        GroupMerged,
        GroupWiped,
    }

    [Serializable]
    public class GroupThresholdEvent
    {
        public Guid eventId = Guid.NewGuid();
        public string groupId;
        public GroupThresholdType type;
        public float gameTime;
        public float value;
        public float threshold;
        public Vector3 position;
        public int caveNodeIdx = -1;
        public Dictionary<string, float> context = new();
    }

    [Serializable]
    public class GroupThresholdState
    {
        public GroupThresholdType type;
        public bool isActive;
        public float enteredAt;
        public float currentValue;
    }

    // ─────────────────────────────────────────────────────────────────────
    // SquadCommand — IGroupCommandReceiver 부속 (Wk5+ 분대 명령)
    // ─────────────────────────────────────────────────────────────────────
    public enum CommandType
    {
        FocusFire,    // 전원 한 타겟에 집중
        SplitFire,    // 분할 타겟
        Disengage,    // 후퇴 명령
        Mark,         // 추적만, 공격 안 함
        Cover,        // 엄호
        Lure,         // 유인
    }

    [Serializable]
    public class SquadCommand
    {
        public Guid commandId = Guid.NewGuid();
        public CommandType type;
        public Transform targetReference;
        public List<string> targetMemberIds = new();
        public float gameTime;
        public float? expiresAt;
        public Dictionary<string, float> parameters = new();
    }

    // ═════════════════════════════════════════════════════════════════════
    // ★ Wk4 추가 (2026-05-12) — DilemmaPivot 분기 결과
    //
    // 통신 방향: AI → 시나리오 (반환 자료)
    // 호출자  : HumanoidAIBrain.Dilemma.cs (EvaluateDilemma 반환)
    // 수신자  : 시나리오 측 ArcProgressionManager.HandleDilemmaResult
    //
    // 시나리오 측이 본 결과로 아크 분기 / Karma 변동 / 후속 시나리오 결정.
    // 본 자료는 AI 파트가 정의 (시나리오팀 합류 후 시그니처 합의 가능).
    //
    // 관련 의제: pB4_ScenarioTeamAgenda_v1.docx §4 (DilemmaOutcome 결과 반영)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// DilemmaPivot 노드 평가 결과. AI 파트 → 시나리오 파트 반환.
    /// </summary>
    [Serializable]
    public struct DilemmaOutcome
    {
        /// <summary>분기 경로 (Comply / PartialRefuse / StrongRefuse).</summary>
        public DilemmaPath path;

        /// <summary>최종 AcceptanceScore (0~1).</summary>
        public float acceptanceScore;

        /// <summary>분기 결정 시 NPC 의 Trust 단계 (TrustMatrix.TrustTier 의 int).</summary>
        public int trustTier;

        /// <summary>Karma 변동량 (시나리오 측에 반환 — KarmaDirector.ChangeKarma 호출 권장).</summary>
        public float karmaDelta;

        /// <summary>Trust 변동량 (AI 측 자체 적용 — TrustMatrix.AdjustTrust 처리됨).</summary>
        public float trustDelta;

        /// <summary>분기 이유 (디버그).</summary>
        public string reason;
    }

    /// <summary>DilemmaPivot 분기 경로 — 3 종.</summary>
    public enum DilemmaPath
    {
        /// <summary>Path A — 명령 그대로 수행.</summary>
        Comply = 0,

        /// <summary>Path B — 일부 거부 / 변형 수행.</summary>
        PartialRefuse = 1,

        /// <summary>Path C — 강한 반대 / 시나리오 분기.</summary>
        StrongRefuse = 2
    }
}
