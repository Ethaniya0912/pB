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
    }
}
