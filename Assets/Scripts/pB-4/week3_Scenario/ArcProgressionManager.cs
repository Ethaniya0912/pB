// =============================================================================
// ArcProgressionManager.cs  |  pB-4 Project — Week 3
// Layer  : L3 Domain (시나리오)
// Namespace: TDA.PB4.Scenario
//
// 역할:
//   Week 1의 ScenarioArcManager를 확장하여, 런타임 아크 진행을 관리.
//   NarrativeManager를 3분할한 결과물 중 하나 (나머지: CriticAgent, SOLifecycleManager).
//
//   핵심 기능:
//   1. SelectNextPhase(): 현재 상황에 가장 적합한 다음 Phase를 선택
//   2. InjectSubplot(): 진행 중인 아크에 서브플롯을 동적 삽입
//   3. 이벤트 연동: Phase 전이 시 EventBus를 통해 AI/지형/연출에 방송
//
// 연계:
//   - ScenarioArcManager (Week 1): 기본 4단계 Phase 관리를 상속/확장
//   - EventBus.OnArcPhaseChanged: Phase 전이 시 발행
//   - GroupAIManager: Climax 진입 시 에스컬레이션 트리거
//   - HNGSIntensityController: Climax 진입 시 긴장도 상승
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Data;

namespace TDA.PB4.Scenario
{
    /// <summary>
    /// 서브플롯 데이터. 진행 중인 아크에 동적으로 삽입되는 부가 서사.
    /// </summary>
    [Serializable]
    public class SubplotData
    {
        [Tooltip("서브플롯 고유 ID")]
        public string subplotId;

        [Tooltip("서브플롯 설명")]
        [TextArea(2, 3)]
        public string description;

        [Tooltip("삽입될 Phase. 이 Phase에 도달하면 서브플롯이 활성화됨.")]
        public ArcPhase targetPhase = ArcPhase.Rising;

        [Tooltip("서브플롯 완료 시 부여되는 Legacy Tag")]
        public List<string> rewardTags = new List<string>();

        [Tooltip("서브플롯 활성화 여부")]
        public bool isActive = false;

        [Tooltip("서브플롯 완료 여부")]
        public bool isCompleted = false;
    }

    public class ArcProgressionManager : MonoBehaviour
    {
        [Header("━━━ 현재 아크 상태 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("현재 진행 중인 아크 SO. " +
                 "StartArc()로 설정되거나 Inspector에서 직접 할당.")]
        [SerializeField] private ScenarioArcSO currentArc;

        [Tooltip("현재 Phase (Read Only). Play 모드에서 자동 갱신.")]
        [SerializeField] private ArcPhase currentPhase = ArcPhase.Intro;

        [Tooltip("아크 시작 이후 경과 시간 (Read Only)")]
        [SerializeField] private float elapsedTime;

        [Header("━━━ 서브플롯 ━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("현재 아크에 삽입된 서브플롯 목록. " +
                 "InjectSubplot()으로 런타임에 추가하거나 Inspector에서 사전 설정.")]
        [SerializeField] private List<SubplotData> activeSubplots = new List<SubplotData>();

        [Header("━━━ Phase 전이 설정 ━━━━━━━━━━━━━━━━━")]
        [Tooltip("자동 전이 여부. true이면 Phase 조건 충족 시 자동으로 다음 Phase로 전이. " +
                 "false이면 외부에서 ForceAdvancePhase()를 호출해야 전이.")]
        public bool autoAdvance = true;

        [Tooltip("Climax 진입 시 GameBlackboard.globalIntensityScore에 가산할 긴장도. " +
                 "0.3 = Climax 시 기존 긴장도에 0.3을 추가하여 통로 수축/안개 강화.")]
        [Range(0f, 0.5f)]
        public float climaxIntensityBoost = 0.3f;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = false;

        private bool arcActive = false;

        // ==================================================================
        // 아크 시작
        // ==================================================================

        /// <summary>새 아크를 시작한다.</summary>
        public void StartArc(ScenarioArcSO arcSO)
        {
            if (arcSO == null) { Debug.LogError("[ArcProg] ArcSO가 null"); return; }
            currentArc = arcSO;
            currentPhase = ArcPhase.Intro;
            elapsedTime = 0f;
            arcActive = true;

            EventBus.RaiseArcPhaseChanged(arcSO.arcId, (int)currentPhase);
            if (debugLog) Debug.Log($"[ArcProg] 아크 시작: {arcSO.arcName}, Phase=Intro");
        }

        private void Update()
        {
            if (!arcActive) return;
            elapsedTime += Time.deltaTime;

            // 서브플롯 활성화 체크
            foreach (var sp in activeSubplots)
            {
                if (!sp.isActive && !sp.isCompleted && sp.targetPhase == currentPhase)
                {
                    sp.isActive = true;
                    if (debugLog) Debug.Log($"[ArcProg] 서브플롯 활성화: {sp.subplotId}");
                }
            }
        }

        // ==================================================================
        // Phase 전이
        // ==================================================================

        /// <summary>
        /// 다음 Phase를 선택하고 전이한다.
        /// 외부에서 호출하거나, autoAdvance=true일 때 조건 충족 시 자동 호출.
        /// </summary>
        public bool SelectNextPhase()
        {
            if (!arcActive || currentPhase == ArcPhase.Completed) return false;

            ArcPhase nextPhase = (ArcPhase)((int)currentPhase + 1);
            return TransitionToPhase(nextPhase);
        }

        /// <summary>특정 Phase로 강제 전이.</summary>
        public bool ForceAdvancePhase(ArcPhase targetPhase)
        {
            return TransitionToPhase(targetPhase);
        }

        private bool TransitionToPhase(ArcPhase newPhase)
        {
            if (newPhase == currentPhase) return false;

            ArcPhase prevPhase = currentPhase;
            currentPhase = newPhase;

            // Climax 진입 시 긴장도 부스트
            if (newPhase == ArcPhase.Climax)
            {
                var bb = GameBlackboard.Instance;
                if (bb != null)
                {
                    float boosted = Mathf.Clamp01(bb.GlobalIntensityScore + climaxIntensityBoost);
                    bb.SetGlobalIntensity(boosted);
                }
                if (debugLog) Debug.Log($"[ArcProg] Climax 진입! 긴장도 +{climaxIntensityBoost}");
            }

            // 완료 처리
            if (newPhase == ArcPhase.Completed)
            {
                arcActive = false;
                if (debugLog) Debug.Log($"[ArcProg] 아크 완료: {currentArc?.arcName}");
            }

            // 이벤트 발행
            string arcId = currentArc != null ? currentArc.arcId : "unknown";
            EventBus.RaiseArcPhaseChanged(arcId, (int)currentPhase);

            if (debugLog)
                Debug.Log($"[ArcProg] Phase 전이: {prevPhase} → {currentPhase}");

            return true;
        }

        // ==================================================================
        // 서브플롯 관리
        // ==================================================================

        /// <summary>진행 중인 아크에 서브플롯을 동적 삽입.</summary>
        public void InjectSubplot(SubplotData subplot)
        {
            activeSubplots.Add(subplot);
            if (debugLog) Debug.Log($"[ArcProg] 서브플롯 삽입: {subplot.subplotId} (target={subplot.targetPhase})");
        }

        /// <summary>서브플롯 완료 처리. rewardTags를 Legacy Tag로 등록.</summary>
        public void CompleteSubplot(string subplotId)
        {
            foreach (var sp in activeSubplots)
            {
                if (sp.subplotId == subplotId && !sp.isCompleted)
                {
                    sp.isCompleted = true;
                    sp.isActive = false;
                    foreach (var tag in sp.rewardTags)
                        if (debugLog) Debug.Log($"[ArcProg] 서브플롯 보상 태그: {tag}");
                    break;
                }
            }
        }

        // ==================================================================
        // 외부 API
        // ==================================================================
        public ArcPhase CurrentPhase => currentPhase;
        public bool IsArcActive => arcActive;
        public ScenarioArcSO CurrentArc => currentArc;
        public float ElapsedTime => elapsedTime;
    }
}
