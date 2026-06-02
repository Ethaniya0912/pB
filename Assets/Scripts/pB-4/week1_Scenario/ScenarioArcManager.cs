// =============================================================================
// ScenarioArcManager.cs  |  pB-4 Project — Week 1
// Layer  : L3 Domain (시나리오)
// Owner  : Person A
//
// 역할:
//   시나리오 아크의 4단계 Phase(Intro→Rising→Climax→Settlement)를 관리.
//   Week 3에서 ArcProgressionManager로 분할되기 전의 기초 구현체.
//   PlotTile 시스템으로 아크 내 세부 이벤트를 모듈화한다.
//
// 연계:
//   EventBus.RaiseArcPhaseChanged() 로 Phase 전이를 방송
//   Week 3: ArcProgressionManager + CriticAgent + SOLifecycleManager로 3분할
//   Week 4: NarrativeBiddingEvaluator가 다음 아크 방향 결정
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Data;
using TDA.PB4.Faction;            // ★ E-07 (S2) — WorldFactionBridgeManager
using TDA.PB4.Faction.Tags;       // ★ E-07 (S2) — FactionLifecycleTag

namespace TDA.PB4.Scenario
{
    public enum ArcPhase { Intro = 0, Rising = 1, Climax = 2, Settlement = 3, Completed = 4 }

    /// <summary>
    /// 플롯 조각 (Plot Tile): 아크 내 세부 이벤트의 최소 단위.
    /// [오프닝 조각]→[전개 조각 A/B/C]→[절정 조각] 형태로 조합.
    /// </summary>
    [Serializable]
    public class PlotTile
    {
        public string tileId;
        public string description;
        public ArcPhase belongsToPhase;
        public List<string> requiredTags = new List<string>();   // 발동 조건 태그
        public List<string> resultTags = new List<string>();     // 완료 시 부여 태그
        public bool isCompleted;

        /// <summary>현재 블랙보드 태그와 requiredTags가 모두 일치하는지 확인.</summary>
        public bool CanActivate(IReadOnlyList<string> activeTags)
        {
            if (requiredTags.Count == 0) return true;
            foreach (var req in requiredTags)
            {
                bool found = false;
                for (int i = 0; i < activeTags.Count; i++)
                    if (activeTags[i] == req) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }
    }

    public class ScenarioArcManager : MonoBehaviour
    {
        [Header("Current Arc")]
        [SerializeField] private ScenarioArcSO currentArcSO;
        [SerializeField] private ArcPhase currentPhase = ArcPhase.Intro;
        public ArcPhase CurrentPhase => currentPhase;

        [Header("Plot Tiles")]
        [SerializeField] private List<PlotTile> plotTiles = new List<PlotTile>();

        // 아크 시작 시간 (에스컬레이션 타이머용)
        private float arcStartTime;
        private bool arcActive = false;

        // ==================================================================
        // 아크 시작/종료
        // ==================================================================
        public void StartArc(ScenarioArcSO arcSO)
        {
            if (arcSO == null)
            {
                Debug.LogError("[ScenarioArcManager] ArcSO가 null입니다.");
                return;
            }

            currentArcSO = arcSO;
            currentPhase = ArcPhase.Intro;
            arcStartTime = Time.time;
            arcActive = true;

            // 아크의 Phase 데이터에서 PlotTile 생성
            plotTiles.Clear();
            if (currentArcSO.phases != null)
            {
                foreach (var phase in currentArcSO.phases)
                {
                    plotTiles.Add(new PlotTile
                    {
                        tileId = $"{arcSO.arcId}_{phase.phaseName}",
                        description = phase.narrativeDescription,
                        belongsToPhase = (ArcPhase)phase.phaseIndex,
                        requiredTags = phase.triggerTags ?? new List<string>(),
                        isCompleted = false
                    });
                }
            }

            EventBus.RaiseArcPhaseChanged(currentArcSO.arcId, (int)currentPhase);
            Debug.Log($"[ScenarioArc] 아크 시작: {arcSO.arcName}, Phase: {currentPhase}");
        }

        /// <summary>
        /// 다음 Phase로 전이. 조건: 현재 Phase의 모든 PlotTile이 완료됨.
        /// </summary>
        public bool TryAdvancePhase()
        {
            if (!arcActive || currentPhase == ArcPhase.Completed) return false;

            // 현재 Phase의 모든 PlotTile 완료 여부 확인
            bool allComplete = true;
            foreach (var tile in plotTiles)
            {
                if (tile.belongsToPhase == currentPhase && !tile.isCompleted)
                {
                    allComplete = false;
                    break;
                }
            }

            if (!allComplete) return false;

            // Phase 전이
            currentPhase = (ArcPhase)((int)currentPhase + 1);

            if (currentPhase == ArcPhase.Completed)
            {
                arcActive = false;
                Debug.Log($"[ScenarioArc] 아크 완료: {currentArcSO.arcName}");
            }
            else
            {
                Debug.Log($"[ScenarioArc] Phase 전이: {currentArcSO.arcName} → {currentPhase}");
            }

            EventBus.RaiseArcPhaseChanged(currentArcSO.arcId, (int)currentPhase);
            return true;
        }

        /// <summary>
        /// PlotTile 완료 처리. resultTags를 블랙보드에 게시.
        /// </summary>
        public void CompleteTile(string tileId)
        {
            foreach (var tile in plotTiles)
            {
                if (tile.tileId == tileId && !tile.isCompleted)
                {
                    tile.isCompleted = true;

                    // resultTags를 Legacy Tag로 등록
                    // TODO: Week 4에서 NarrativeBiddingEvaluator와 연동
                    foreach (var tag in tile.resultTags)
                    {
                        Debug.Log($"[ScenarioArc] Legacy Tag 등록: {tag}");
                    }

                    // 자동 Phase 전이 시도
                    TryAdvancePhase();
                    return;
                }
            }
        }

        /// <summary>현재 Phase에서 활성화 가능한 PlotTile 목록 반환.</summary>
        public List<PlotTile> GetActivatableTiles(IReadOnlyList<string> currentTags)
        {
            var result = new List<PlotTile>();
            foreach (var tile in plotTiles)
            {
                if (tile.belongsToPhase == currentPhase && !tile.isCompleted && tile.CanActivate(currentTags))
                    result.Add(tile);
            }
            return result;
        }

        // ==================================================================
        // 디버그
        // ==================================================================
        public float GetElapsedTime() => arcActive ? Time.time - arcStartTime : 0f;
        public bool IsArcActive => arcActive;
        public ScenarioArcSO GetCurrentArcSO() => currentArcSO;
        
        // ==================================================================
        // ★ E-07 (S2) — Faction Lifecycle 분기 연계
        // ==================================================================
        // 본 영역:
        //   WorldFactionBridgeManager.OnFactionStateChanged 구독 + Lifecycle 분기
        //   - Emerging (LIFECYCLE_EMERGING) → FactionDefinition.SpeechTriggerIdOnEnter 호출
        //   - Decline (LIFECYCLE_THREATENED / DEFEATED / EXTINCT) → 우울 시나리오 분기
        //     + FactionDefinition.SpeechTriggerIdOnDecline 호출
        //   본 구독은 Bridge 패턴 정합 — StateManager 직접 의존 0.
        
        private void OnEnable()
        {
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge != null)
            {
                bridge.OnFactionStateChanged += HandleFactionStateChanged;
            }
        }
        
        private void OnDisable()
        {
            var bridge = WorldFactionBridgeManager.Instance;
            if (bridge != null)
            {
                bridge.OnFactionStateChanged -= HandleFactionStateChanged;
            }
        }
        
        /// <summary>
        /// OnFactionStateChanged 진입 핸들러 — Lifecycle 측 본격 분기.
        /// 본 핸들러는 Lifecycle 비트 변화 (이전 → 신규) 만 점검.
        /// </summary>
        private void HandleFactionStateChanged(FactionStateChangeEventArgs args)
        {
            string factionId = args.factionId;
            
            // 본 핸들러는 Lifecycle 변화만 점검 — 다른 3 카테고리는 무시
            if (!args.LifecycleChanged) return;
            
            // 새로 ON 비트 = 신규 진입 (Helper 사용)
            uint addedBits = args.LifecycleBitsTurnedOn;
            if (addedBits == 0u) return;  // 제거만 발생 — 진입 분기 없음
            
            // FactionDefinition 측 발화 트리거 조회
            var def = LookupFactionDefinition(factionId);
            
            // ── Emerging 진입 분기
            if ((addedBits & (uint)FactionLifecycleTag.LIFECYCLE_EMERGING) != 0u)
            {
                HandleLifecycleEmerging(factionId, def);
            }
            
            // ── Decline 진입 분기 (THREATENED / DEFEATED / EXTINCT)
            uint declineBits =
                (uint)FactionLifecycleTag.LIFECYCLE_THREATENED |
                (uint)FactionLifecycleTag.LIFECYCLE_DEFEATED |
                (uint)FactionLifecycleTag.LIFECYCLE_EXTINCT;
            
            if ((addedBits & declineBits) != 0u)
            {
                HandleLifecycleDecline(factionId, def, addedBits & declineBits);
            }
        }
        
        /// <summary>
        /// Lifecycle Emerging 진입 — 신규 펙션 등장 시나리오.
        /// SpeechTriggerIdOnEnter 측 호출 + 등장 시나리오 분기 자료 박제.
        /// </summary>
        private void HandleLifecycleEmerging(string factionId, FactionDefinitionSO def)
        {
            string trigger = def?.SpeechTriggerIdOnEnter;
            Debug.Log(
                $"<color=#88FF88>[ScenarioArc / E-07]</color> Lifecycle Emerging — " +
                $"factionId={factionId} / speechTrigger={trigger ?? "(없음)"}");
            
            // TODO (E-07 진입 자료) — SpeechAssembler 측 trigger 호출
            // SpeechAssembler.Instance?.Dispatch(trigger);
            //
            // TODO (E-07 진입 자료) — 등장 시나리오 분기
            // 본 시점 측 PlotTile / Arc 측 본격 분기 자료 박제 (사용자 측 시나리오팀 협의 필요)
        }
        
        /// <summary>
        /// Lifecycle Decline 진입 — 쇠퇴 / 패배 / 멸종 시나리오.
        /// SpeechTriggerIdOnDecline 측 호출 + 우울 시나리오 분기 자료 박제.
        /// </summary>
        private void HandleLifecycleDecline(string factionId, FactionDefinitionSO def, uint declineFlag)
        {
            string trigger = def?.SpeechTriggerIdOnDecline;
            string declineName =
                ((declineFlag & (uint)FactionLifecycleTag.LIFECYCLE_EXTINCT) != 0u)   ? "EXTINCT" :
                ((declineFlag & (uint)FactionLifecycleTag.LIFECYCLE_DEFEATED) != 0u)  ? "DEFEATED" :
                                                                                       "THREATENED";
            
            Debug.Log(
                $"<color=#FFAA66>[ScenarioArc / E-07]</color> Lifecycle Decline ({declineName}) — " +
                $"factionId={factionId} / speechTrigger={trigger ?? "(없음)"}");
            
            // TODO (E-07 진입 자료) — SpeechAssembler 측 trigger 호출
            // SpeechAssembler.Instance?.Dispatch(trigger);
            //
            // TODO (E-07 진입 자료) — 우울 시나리오 분기
            // 본 시점 측 PlotTile / Arc 측 본격 분기 자료 박제 (사용자 측 시나리오팀 협의 필요)
        }
        
        /// <summary>
        /// FactionDefinitionSO 조회 — WorldFactionStateManager 의 public API 사용.
        /// SpeechTriggerIdOnEnter / OnDecline 조회용. 미등록 시 null.
        /// </summary>
        private static FactionDefinitionSO LookupFactionDefinition(string factionId)
        {
            return WorldFactionStateManager.Instance?.GetDefinition(factionId);
        }
    }
}
