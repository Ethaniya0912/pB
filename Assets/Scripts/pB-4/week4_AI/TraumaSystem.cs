// =============================================================================
// TraumaSystem.cs  |  pB-4 Project — Week 4 (Day 2 T2.4 매뉴얼 v3 사양 적용)
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   NPC의 트라우마 3단계 진화를 관리한다.
//
//   Stage 0: None (정상)
//   Stage 1: AcuteShock (1~3 탐험)
//     - Stability -0.5, Agreeable -0.2 즉각 적용
//     - 특정 트리거(동굴, 어둠 등)에서 공포 발작
//     - fear multiplier 1.5x
//   Stage 2: Crossroads (극복 or 악화 분기)
//     - 신뢰도 ≥ recoveryTrustThreshold: 회복 경로
//     - 신뢰도 < worsenTrustThreshold: 영구 흉터 확정
//     - fear multiplier 1.3x
//   Stage 3: PermanentScarring (앵커 영구 고착)
//     - anchorPersonality 영구 변경
//     - fear multiplier 1.2x (낮음 — 적응함)
//
//   신뢰도 × 트라우마 결합:
//     실제_공포 = 트라우마_공포 × (1 - TrustMatrix.GetTraumaCancellation())
//
// [Day 2 T2.4 v3 변경 요약]
//   1) ITraumaProvider 인터페이스 구현 (3 메서드)
//   2) ApplyShock(intensity, causeId) 어댑터 메서드 (기존 InflictTrauma의 intensity 기반 호출)
//   3) GetFearMultiplier() Stage별 매핑 추가 (None=1.0, Acute=1.5, Cross=1.3, Perm=1.2)
//   4) CurrentStageIndex 프로퍼티 (int 반환)
//   5) 기존 고급 로직 모두 보존 (3단계 진화, OnExplorationCompleted, 트리거 발작 등)
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Data;
using TDA.PB4.Interfaces.Intelligence;

namespace TDA.PB4.AI
{
    /// <summary>
    /// 트라우마 단계.
    /// [매뉴얼 사양] 인덱스 순서: 0=None, 1=AcuteShock, 2=Crossroads, 3=PermanentScarring.
    /// </summary>
    public enum TraumaStage
    {
        /// <summary>0. 트라우마 없음. 정상 상태.</summary>
        None = 0,
        /// <summary>1. 급성 충격. 1~3 탐험 지속. Stability/Agreeable 급감.</summary>
        AcuteShock = 1,
        /// <summary>2. 갈림길. 회복하거나 악화할 수 있는 분기점.</summary>
        Crossroads = 2,
        /// <summary>3. 영구 흉터. 앵커 포인트 고착. 회복 불가.</summary>
        PermanentScarring = 3
    }

    public class TraumaSystem : MonoBehaviour, ITraumaProvider
    {
        [Header("━━━ 현재 트라우마 상태 ━━━━━━━━━━━━━━━")]
        [Tooltip("현재 트라우마 단계 (Read Only). " +
                 "None → AcuteShock → Crossroads → PermanentScarring 순서로 진화.")]
        [SerializeField] private TraumaStage currentStage = TraumaStage.None;

        [Tooltip("활성화된 트라우마 SO (Read Only). " +
                 "InflictTrauma()로 설정됨. ApplyShock()는 임시 SO 생성.")]
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
                 "-0.2 = 타인에 대한 우호성이 0.2 감소.")]
        [Range(-1f, 0f)]
        public float acuteAgreeableDrop = -0.2f;

        [Tooltip("Acute Shock이 Crossroads로 전이되는 탐험 횟수. " +
                 "3 = 3번의 탐험(던전 방 3개) 후 갈림길 진입.")]
        [Range(1, 10)]
        public int acuteShockDuration = 3;

        [Header("━━━ Crossroads 설정 ━━━━━━━━━━━━━━━━━")]
        [Tooltip("회복 경로 진입 조건: 신뢰도가 이 값 이상이면 회복 시작.")]
        [Range(0f, 100f)]
        public float recoveryTrustThreshold = 50f;

        [Tooltip("회복 시 매 탐험당 Stability 회복량.")]
        [Range(0f, 0.3f)]
        public float recoveryStabilityPerExploration = 0.1f;

        [Tooltip("악화 경로 진입 조건: 신뢰도가 이 값 미만이면 악화.")]
        [Range(0f, 50f)]
        public float worsenTrustThreshold = 20f;

        [Header("━━━ 트리거 태그 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("이 지형 태그가 활성화되면 트라우마 트리거(공포 발작) 발생. " +
                 "비어있으면 TraumaDataSO의 keywords에서 자동 매칭.")]
        [SerializeField] private List<string> triggerTags = new List<string>();

        [Header("━━━ 공포 발작 설정 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("트리거 발동 시 fear에 가산되는 공포량. " +
                 "0.4 = 트리거 발동하면 fear가 0.4 급등.")]
        [Range(0f, 1f)]
        public float triggerFearBurst = 0.4f;

        [Header("━━━ Stage별 fear multiplier ━━━━━━━━")]
        [Tooltip("[ITraumaProvider.GetFearMultiplier()] 단계별 fear 가중치. " +
                 "HumanoidAIBrain.BuildNeedsDictionary가 fear에 곱한다.")]
        [Range(1f, 2f)] public float multiplierNone = 1.0f;
        [Range(1f, 2f)] public float multiplierAcuteShock = 1.5f;
        [Range(1f, 2f)] public float multiplierCrossroads = 1.3f;
        [Range(1f, 2f)] public float multiplierPermanent = 1.2f;

        [Header("━━━ 자동 회복 (시간 기반) ━━━━━━━━━━")]
        [Tooltip("[D4 v3+] AcuteShock 자동 회복 시간 (초). " +
                 "이 시간 경과 후 explorations 무관하게 None으로 자동 회복. " +
                 "0 = 비활성 (OnExplorationCompleted 호출에만 의존, 사용자 기존 사양). " +
                 "15 = v3 매뉴얼 권장값.")]
        [Range(0f, 60f)] public float autoRecoverySeconds = 0f;

        // D4 시간 기반 회복용 타임스탬프
        private float acuteShockEnterTime = 0f;

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
        // [D4 v3+] 시간 기반 자동 회복 (autoRecoverySeconds > 0 일 때만 활성)
        // ==================================================================
        private void Update()
        {
            // OnExplorationCompleted 이벤트가 안 들어와도 시간 기반으로 회복.
            // autoRecoverySeconds=0 이면 비활성 (사용자 기존 사양: 이벤트 기반만).
            if (autoRecoverySeconds > 0f
                && currentStage == TraumaStage.AcuteShock
                && Time.time - acuteShockEnterTime >= autoRecoverySeconds)
            {
                currentStage = TraumaStage.None;
                activeTrauma = null;
                explorationsSinceTrauma = 0;
                // [중요 이벤트] 항상 출력
                Debug.Log($"[Trauma] {name}: Stage 전이 AcuteShock → None (자동 회복, 경과 {autoRecoverySeconds:F0}s)");
            }
        }

        // ==================================================================
        // 핵심 API: 트라우마 부여 (기존 본격 API)
        // ==================================================================

        /// <summary>
        /// NPC에게 트라우마를 부여한다. Acute Shock 단계로 진입.
        /// 풍부한 정보(SO 기반): 트리거 키워드, severity, traumaID 등.
        /// </summary>
        public void InflictTrauma(TraumaDataSO traumaSO)
        {
            if (traumaSO == null || currentStage != TraumaStage.None)
            {
                if (debugLog)
                    Debug.Log($"[Trauma] {name}: 부여 실패: SO={(traumaSO != null ? traumaSO.traumaID : "null")}, stage={currentStage}");
                return;
            }

            activeTrauma = traumaSO;
            currentStage = TraumaStage.AcuteShock;
            explorationsSinceTrauma = 0;
            acuteShockEnterTime = Time.time;  // [D4] 시간 기반 자동 회복용

            // Acute Shock 즉각 적용
            if (brain != null)
            {
                brain.PivotPersonality(new float[] { 0, acuteStabilityDrop, 0, acuteAgreeableDrop, 0 });
                brain.fear = Mathf.Clamp01(brain.fear + traumaSO.severity);
            }

            // 트리거 태그 설정
            if (triggerTags.Count == 0 && traumaSO.keywords != null)
                triggerTags.AddRange(traumaSO.keywords);

            // [중요 이벤트] 트라우마 부여는 debugLog 무관 항상 출력 (검증/디버깅 가시성)
            Debug.Log($"[Trauma] {name}: 트라우마 부여: {traumaSO.traumaID}, severity={traumaSO.severity:F2}. " +
                      $"Stability{acuteStabilityDrop:+0.0;-0.0;0.0}, Agreeable{acuteAgreeableDrop:+0.0;-0.0;0.0}");
        }

        // ==================================================================
        // ITraumaProvider 인터페이스 구현 (Day 2 T2.4)
        // ==================================================================

        /// <summary>
        /// [ITraumaProvider] 현재 단계 인덱스. 0=None, 3=PermanentScarring.
        /// </summary>
        public int CurrentStageIndex => (int)currentStage;

        /// <summary>
        /// [ITraumaProvider] 트라우마 충격 부여. intensity 0.6+ 이면 AcuteShock 트리거.
        /// </summary>
        /// <param name="intensity">충격 강도 0~1.</param>
        /// <param name="causeId">원인 ID (예: "fell_in_lava", "ally_died").</param>
        /// <remarks>
        /// 이 메서드는 InflictTrauma의 어댑터. 풍부한 SO 정보 없이 호출 시,
        /// intensity와 causeId만으로 임시 TraumaDataSO를 생성하여 InflictTrauma 호출.
        /// 디자이너가 사전 정의한 SO를 사용하려면 InflictTrauma(traumaSO) 직접 호출.
        /// </remarks>
        public void ApplyShock(float intensity, string causeId)
        {
            if (currentStage != TraumaStage.None)
            {
                if (debugLog)
                    Debug.Log($"[Trauma] {name}: ApplyShock 무시 (이미 stage={currentStage})");
                return;
            }

            // intensity 0.6 미만이면 AcuteShock 진입 안 함 (인터페이스 명세)
            if (intensity < 0.6f)
            {
                // 그래도 fear는 약간 증가 (subliminal shock)
                if (brain != null)
                    brain.fear = Mathf.Clamp01(brain.fear + intensity * 0.3f);
                if (debugLog)
                    Debug.Log($"[Trauma] {name}: ApplyShock intensity={intensity:F2} < 0.6 → fear+{intensity * 0.3f:F2}만 (Stage 변화 없음)");
                return;
            }

            // 임시 TraumaDataSO 생성 (런타임 인스턴스, 저장 안 됨)
            var tempSO = ScriptableObject.CreateInstance<TraumaDataSO>();
            tempSO.traumaID = causeId ?? "unknown";
            tempSO.severity = intensity;
            tempSO.keywords = new System.Collections.Generic.List<string>();  // 트리거 키워드는 SO 직접 호출 시에만

            InflictTrauma(tempSO);
        }

        /// <summary>
        /// [ITraumaProvider] 현재 단계에 따른 fear 가중치.
        /// HumanoidAIBrain.BuildNeedsDictionary가 fear에 곱하여 사용.
        /// </summary>
        public float GetFearMultiplier()
        {
            return currentStage switch
            {
                TraumaStage.None => multiplierNone,
                TraumaStage.AcuteShock => multiplierAcuteShock,
                TraumaStage.Crossroads => multiplierCrossroads,
                TraumaStage.PermanentScarring => multiplierPermanent,
                _ => 1.0f
            };
        }

        // ==================================================================
        // 탐험 완료 시 호출 — 단계 진화 (기존 보존)
        // ==================================================================

        /// <summary>
        /// 탐험(던전 방) 1개 완료 시 호출. 트라우마 단계를 진화시킨다.
        /// ScenarioArcManager 또는 CaveManager가 청크 전환 시 호출.
        /// </summary>
        public void OnExplorationCompleted()
        {
            if (currentStage == TraumaStage.None || currentStage == TraumaStage.PermanentScarring)
                return;

            explorationsSinceTrauma++;

            if (currentStage == TraumaStage.AcuteShock)
            {
                if (explorationsSinceTrauma >= acuteShockDuration)
                {
                    currentStage = TraumaStage.Crossroads;
                    // [중요 이벤트] Stage 전이는 항상 출력
                    Debug.Log($"[Trauma] {name}: Stage 전이 AcuteShock → Crossroads (탐험 {explorationsSinceTrauma}회)");
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
                        // [중요 이벤트] 항상 출력
                        Debug.Log($"[Trauma] {name}: Stage 전이 Crossroads → None (트라우마 해소!)");
                    }
                    else if (debugLog)
                        Debug.Log($"[Trauma] {name}: 회복 중: Stability +{recoveryStabilityPerExploration:F2} (trust={trust:F1})");
                }
                else if (trust < worsenTrustThreshold)
                {
                    // 악화 경로 → 영구 흉터
                    currentStage = TraumaStage.PermanentScarring;

                    if (brain != null)
                        brain.PivotPersonality(new float[] { 0, -0.3f, 0, -0.2f, 0.1f });

                    // [중요 이벤트] 항상 출력
                    Debug.Log($"[Trauma] {name}: Stage 전이 Crossroads → PermanentScarring (영구 흉터 확정! trust={trust:F1})");
                }
            }
        }

        // ==================================================================
        // 트리거 감지: 지형 태그 변경 시 공포 발작 (기존 보존)
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
                        Debug.Log($"[Trauma] {name}: 트리거 '{tag}' 발동! fear+{effectiveFear:F2} (원래={triggerFearBurst:F2}, 상쇄={cancellation:F2})");
                    break;
                }
            }
        }

        // ==================================================================
        // 외부 API (기존 보존)
        // ==================================================================

        public TraumaStage CurrentStage => currentStage;
        public TraumaDataSO ActiveTrauma => activeTrauma;
        public int ExplorationsSinceTrauma => explorationsSinceTrauma;

        // ==================================================================
        // 디버그용 Context Menu
        // ==================================================================

        [ContextMenu("Inflict Trauma (intensity 0.8)")]
        private void DebugInflict() => ApplyShock(0.8f, "ContextMenu_Test");

        [ContextMenu("Force Crossroads")]
        private void DebugCrossroads()
        {
            if (currentStage == TraumaStage.AcuteShock)
            {
                explorationsSinceTrauma = acuteShockDuration;
                OnExplorationCompleted();
            }
        }

        [ContextMenu("Reset to None")]
        private void DebugReset()
        {
            currentStage = TraumaStage.None;
            activeTrauma = null;
            explorationsSinceTrauma = 0;
            triggerTags.Clear();
        }
    }
}
