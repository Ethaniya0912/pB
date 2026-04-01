// =============================================================================
// TraumaSystem.cs  |  pB-4 Project — Week 4
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   NPC의 트라우마 3단계 진화를 관리한다.
//   
//   Stage 1: Acute Shock (1~3 탐험)
//     - Stability -0.5, Agreeable -0.2 즉각 적용
//     - 특정 트리거(동굴, 어둠 등)에서 공포 발작
//
//   Stage 2: Crossroads (극복 or 악화 분기)
//     - 플레이어의 행동에 따라 회복 또는 악화
//     - 회복: Stability 서서히 회복, 트라우마 해소
//     - 악화: 재앙 아키타입(Week 7)으로 전이
//
//   Stage 3: Permanent Scarring (앵커 포인트 영구 고착)
//     - anchorPersonality가 영구 변경됨
//     - 더 이상 회복 불가. 성격이 트라우마 이후 상태로 고정
//
//   신뢰도 × 트라우마 결합:
//     실제_공포 = 트라우마_공포 × (1 - TrustMatrix.GetTraumaCancellation())
//     플레이어를 신뢰할수록 트라우마 공포가 상쇄됨
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Data;

namespace TDA.PB4.AI
{
    public enum TraumaStage
    {
        /// <summary>트라우마 없음. 정상 상태.</summary>
        None,
        /// <summary>급성 충격. 1~3 탐험 지속. Stability/Agreeable 급감.</summary>
        AcuteShock,
        /// <summary>갈림길. 회복하거나 악화할 수 있는 분기점.</summary>
        Crossroads,
        /// <summary>영구 흉터. 앵커 포인트 고착. 회복 불가.</summary>
        PermanentScarring
    }

    public class TraumaSystem : MonoBehaviour
    {
        [Header("━━━ 현재 트라우마 상태 ━━━━━━━━━━━━━━━")]
        [Tooltip("현재 트라우마 단계 (Read Only). " +
                 "None → AcuteShock → Crossroads → PermanentScarring 순서로 진화.")]
        [SerializeField] private TraumaStage currentStage = TraumaStage.None;

        [Tooltip("활성화된 트라우마 SO (Read Only). " +
                 "InflictTrauma()로 설정됨.")]
        [SerializeField] private TraumaDataSO activeTrauma;

        [Tooltip("트라우마 발생 이후 경과된 탐험 횟수 (Read Only). " +
                 "AcuteShock은 1~3 탐험 지속.")]
        [SerializeField] private int explorationsSinceTrauma = 0;

        [Header("━━━ Acute Shock 설정 ━━━━━━━━━━━━━━━━")]
        [Tooltip("Acute Shock 단계에서 Stability에 적용되는 즉각 감소량. " +
                 "-0.5 = 트라우마 발생 직후 안정성이 0.5 급감. " +
                 "NPC가 갑자기 불안정해지며 도주/공포 행동이 크게 증가.")]
        [Range(-1f, 0f)]
        public float acuteStabilityDrop = -0.5f;

        [Tooltip("Acute Shock 단계에서 Agreeable에 적용되는 즉각 감소량. " +
                 "-0.2 = 타인에 대한 우호성이 0.2 감소. " +
                 "NPC가 비협조적이 되어 명령 거부율 상승.")]
        [Range(-1f, 0f)]
        public float acuteAgreeableDrop = -0.2f;

        [Tooltip("Acute Shock이 Crossroads로 전이되는 탐험 횟수. " +
                 "3 = 3번의 탐험(던전 방 3개) 후 갈림길 진입.")]
        [Range(1, 10)]
        public int acuteShockDuration = 3;

        [Header("━━━ Crossroads 설정 ━━━━━━━━━━━━━━━━━")]
        [Tooltip("회복 경로 진입 조건: 신뢰도가 이 값 이상이면 회복 시작. " +
                 "50 = Cooperation 이상이면 회복 가능.")]
        [Range(0f, 100f)]
        public float recoveryTrustThreshold = 50f;

        [Tooltip("회복 시 매 탐험당 Stability 회복량. " +
                 "+0.1 = 탐험 1회마다 안정성 0.1씩 회복.")]
        [Range(0f, 0.3f)]
        public float recoveryStabilityPerExploration = 0.1f;

        [Tooltip("악화 경로 진입 조건: 신뢰도가 이 값 미만이면 악화. " +
                 "20 = Hostility 근접 시 영구 흉터 확정.")]
        [Range(0f, 50f)]
        public float worsenTrustThreshold = 20f;

        [Header("━━━ 트리거 태그 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("이 지형 태그가 활성화되면 트라우마 트리거(공포 발작) 발생. " +
                 "예: 동굴 트라우마가 있는 NPC가 'SpookyCave' 태그 지형에 진입하면 fear 급등. " +
                 "비어있으면 TraumaDataSO의 keywords에서 자동 매칭.")]
        [SerializeField] private List<string> triggerTags = new List<string>();

        [Header("━━━ 공포 발작 설정 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("트리거 발동 시 fear에 가산되는 공포량. " +
                 "0.4 = 트리거 발동하면 fear가 0.4 급등.")]
        [Range(0f, 1f)]
        public float triggerFearBurst = 0.4f;

        [Header("━━━ 참조 ━━━━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("같은 오브젝트의 HumanoidAIBrain. 자동 탐색.")]
        [SerializeField] private HumanoidAIBrain brain;

        [Tooltip("같은 오브젝트의 TrustMatrix. 자동 탐색.")]
        [SerializeField] private TrustMatrix trustMatrix;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = false;

        private void Awake()
        {
            if (brain == null) brain = GetComponent<HumanoidAIBrain>();
            if (trustMatrix == null) trustMatrix = GetComponent<TrustMatrix>();
        }

        private void OnEnable()
        {
            EventBus.OnTerrainTagChanged += OnTerrainTagChanged;
        }

        private void OnDisable()
        {
            EventBus.OnTerrainTagChanged -= OnTerrainTagChanged;
        }

        // ==================================================================
        // 핵심 API: 트라우마 부여
        // ==================================================================

        /// <summary>
        /// NPC에게 트라우마를 부여한다. Acute Shock 단계로 진입.
        /// </summary>
        public void InflictTrauma(TraumaDataSO traumaSO)
        {
            if (traumaSO == null || currentStage != TraumaStage.None)
            {
                if (debugLog) Debug.Log($"[Trauma] 부여 실패: SO={(traumaSO != null ? traumaSO.traumaID : "null")}, stage={currentStage}");
                return;
            }

            activeTrauma = traumaSO;
            currentStage = TraumaStage.AcuteShock;
            explorationsSinceTrauma = 0;

            // Acute Shock 즉각 적용
            if (brain != null)
            {
                brain.PivotPersonality(new float[] { 0, acuteStabilityDrop, 0, acuteAgreeableDrop, 0 });
                brain.fear = Mathf.Clamp01(brain.fear + traumaSO.severity);
            }

            // 트리거 태그 설정
            if (triggerTags.Count == 0 && traumaSO.keywords != null)
                triggerTags.AddRange(traumaSO.keywords);

            if (debugLog)
                Debug.Log($"[Trauma] 트라우마 부여: {traumaSO.traumaID}, severity={traumaSO.severity:F2}. " +
                          $"Stability{acuteStabilityDrop:+0.0}, Agreeable{acuteAgreeableDrop:+0.0}");
        }

        // ==================================================================
        // 탐험 완료 시 호출 — 단계 진화
        // ==================================================================

        /// <summary>
        /// 탐험(던전 방) 1개 완료 시 호출. 트라우마 단계를 진화시킨다.
        /// ScenarioArcManager 또는 CaveManager가 청크 전환 시 호출.
        /// </summary>
        public void OnExplorationCompleted()
        {
            if (currentStage == TraumaStage.None || currentStage == TraumaStage.PermanentScarring) return;

            explorationsSinceTrauma++;

            if (currentStage == TraumaStage.AcuteShock)
            {
                if (explorationsSinceTrauma >= acuteShockDuration)
                {
                    currentStage = TraumaStage.Crossroads;
                    if (debugLog) Debug.Log($"[Trauma] AcuteShock → Crossroads (탐험 {explorationsSinceTrauma}회)");
                }
            }
            else if (currentStage == TraumaStage.Crossroads)
            {
                float trust = trustMatrix != null ? trustMatrix.CurrentTrust : 50f;

                if (trust >= recoveryTrustThreshold)
                {
                    // 회복 경로
                    if (brain != null)
                        brain.PivotPersonality(new float[] { 0, recoveryStabilityPerExploration, 0, 0, 0 });

                    // 완전 회복 검사: 원래 Stability에 근접하면 트라우마 해소
                    if (brain != null && brain.Personality.stability >= brain.GetAnchor().stability - 0.1f)
                    {
                        currentStage = TraumaStage.None;
                        activeTrauma = null;
                        if (debugLog) Debug.Log("[Trauma] 트라우마 해소! → None");
                    }
                    else if (debugLog)
                        Debug.Log($"[Trauma] 회복 중: Stability +{recoveryStabilityPerExploration:F2} (trust={trust:F1})");
                }
                else if (trust < worsenTrustThreshold)
                {
                    // 악화 경로 → 영구 흉터
                    currentStage = TraumaStage.PermanentScarring;

                    // 앵커 포인트 영구 고착
                    // PivotPersonality가 아닌 앵커 자체를 변경 (Week 7 구현 예정)
                    // 현재는 추가 Stability 하락으로 표현
                    if (brain != null)
                        brain.PivotPersonality(new float[] { 0, -0.3f, 0, -0.2f, 0.1f });

                    if (debugLog) Debug.Log($"[Trauma] 영구 흉터 확정! Crossroads → PermanentScarring (trust={trust:F1})");
                }
            }
        }

        // ==================================================================
        // 트리거 감지: 지형 태그 변경 시 공포 발작
        // ==================================================================
        private void OnTerrainTagChanged(System.Collections.Generic.IReadOnlyList<string> newTags)
        {
            if (currentStage == TraumaStage.None || newTags == null) return;

            foreach (var tag in newTags)
            {
                if (triggerTags.Contains(tag))
                {
                    // 신뢰도에 의한 공포 상쇄
                    float cancellation = trustMatrix != null ? trustMatrix.GetTraumaCancellation() : 0f;
                    float effectiveFear = triggerFearBurst * (1f - cancellation);

                    if (brain != null)
                        brain.fear = Mathf.Clamp01(brain.fear + effectiveFear);

                    if (debugLog)
                        Debug.Log($"[Trauma] 트리거 '{tag}' 발동! fear+{effectiveFear:F2} (원래={triggerFearBurst:F2}, 상쇄={cancellation:F2})");
                    break;
                }
            }
        }

        // ==================================================================
        // 외부 API
        // ==================================================================
        public TraumaStage CurrentStage => currentStage;
        public TraumaDataSO ActiveTrauma => activeTrauma;
        public int ExplorationsSinceTrauma => explorationsSinceTrauma;
    }
}
