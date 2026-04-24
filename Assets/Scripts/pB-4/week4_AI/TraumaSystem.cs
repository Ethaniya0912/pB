// =============================================================================
// TraumaSystem.cs  |  pB-4 Project — Week 4 (Day 2 T2.4) → v3 NGO 2.0
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   NPC의 트라우마 4단계 진화 관리.
//   Stage 0: None / 1: AcuteShock / 2: Crossroads / 3: PermanentScarring
//
//   Trauma는 NPC 내면 상태이므로 플레이어별 분리 불필요 (1개 값 유지).
//
// [Day 2 T2.4 v3 변경]
//   1) ITraumaProvider 3 멤버 구현
//   2) ApplyShock 어댑터
//   3) GetFearMultiplier Stage별
//
// [v3 NGO 2.0 개정 — 2026-04-23]
//   - [NGO-1] MonoBehaviour → NetworkBehaviour
//   - [NGO-2] Awake + OnNetworkSpawn 듀얼
//   - [NGO-3] InflictTrauma/ApplyShock은 서버 권한 (ServerRpc로 변환)
//   - [NGO-4] NetworkVariable<TraumaStage> currentStage (전 클라 동기화)
//   - [NGO-5] 단독 Play 호환 (NetworkManager 비활성 시 기존 동작)
// =============================================================================
using System;
using System.Collections.Generic;
using Unity.Netcode;                            // [NGO-1]
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.AI.Humanoid;
using TDA.PB4.Data;
using TDA.PB4.Interfaces.Intelligence;

namespace TDA.PB4.AI
{
    /// <summary>트라우마 4단계. 0=None ~ 3=PermanentScarring.</summary>
    public enum TraumaStage
    {
        None = 0,
        AcuteShock = 1,
        Crossroads = 2,
        PermanentScarring = 3
    }

    public class TraumaSystem : NetworkBehaviour, ITraumaProvider       // [NGO-1]
    {
        [Header("━━━ 현재 트라우마 상태 ━━━━━━━━━━━━━━━")]
        [Tooltip("현재 트라우마 단계. None → AcuteShock → Crossroads → PermanentScarring.")]
        [SerializeField] private TraumaStage currentStage = TraumaStage.None;

        [Tooltip("활성화된 트라우마 SO (Read Only).")]
        [SerializeField] private TraumaDataSO activeTrauma;

        [Tooltip("트라우마 발생 이후 경과 탐험 횟수.")]
        [SerializeField] private int explorationsSinceTrauma = 0;

        [Header("━━━ Acute Shock 설정 ━━━━━━━━━━━━━━━━")]
        [Range(-1f, 0f)] public float acuteStabilityDrop = -0.5f;
        [Range(-1f, 0f)] public float acuteAgreeableDrop = -0.2f;
        [Range(1, 10)]   public int acuteShockDuration = 3;

        [Header("━━━ Crossroads 설정 ━━━━━━━━━━━━━━━━━")]
        [Range(0f, 100f)] public float recoveryTrustThreshold = 50f;
        [Range(0f, 0.3f)] public float recoveryStabilityPerExploration = 0.1f;
        [Range(0f, 50f)]  public float worsenTrustThreshold = 20f;

        [Header("━━━ 트리거 태그 ━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private List<string> triggerTags = new List<string>();

        [Header("━━━ 공포 발작 설정 ━━━━━━━━━━━━━━━━━━")]
        [Range(0f, 1f)] public float triggerFearBurst = 0.4f;

        [Header("━━━ Stage별 fear multiplier ━━━━━━━━")]
        [Range(1f, 2f)] public float multiplierNone = 1.0f;
        [Range(1f, 2f)] public float multiplierAcuteShock = 1.5f;
        [Range(1f, 2f)] public float multiplierCrossroads = 1.3f;
        [Range(1f, 2f)] public float multiplierPermanent = 1.2f;

        [Header("━━━ 자동 회복 (시간 기반) ━━━━━━━━━━")]
        [Range(0f, 60f)] public float autoRecoverySeconds = 0f;
        private float acuteShockEnterTime = 0f;

        [Header("━━━ 참조 ━━━━━━━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private HumanoidAIBrain brain;
        [SerializeField] private TrustMatrix trustMatrix;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = false;

        // ==================================================================
        // [NGO-4] NetworkVariable 동기화
        // ==================================================================

        /// <summary>[NGO-4] Stage 동기화. 서버 쓰기, 전 클라 읽기.</summary>
        private NetworkVariable<TraumaStage> netStage = new NetworkVariable<TraumaStage>(
            value: TraumaStage.None,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        // ==================================================================
        // [v3 NGO-2] 이중 초기화
        // ==================================================================

        private void Awake()
        {
            if (brain == null) brain = GetComponent<HumanoidAIBrain>();
            if (trustMatrix == null) trustMatrix = GetComponent<TrustMatrix>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // [NGO-4] Stage 변경 감시 (클라에서 SerializeField 동기화용)
            netStage.OnValueChanged += OnNetStageChanged;

            if (IsServer)
                netStage.Value = currentStage;
        }

        public override void OnNetworkDespawn()
        {
            netStage.OnValueChanged -= OnNetStageChanged;
            base.OnNetworkDespawn();
        }

        /// <summary>[NGO-5] NetworkVariable 변경 → SerializeField 동기화 (Inspector 표시).</summary>
        private void OnNetStageChanged(TraumaStage prev, TraumaStage next)
        {
            if (!IsServer)   // 서버는 이미 currentStage 변경함
            {
                currentStage = next;
                if (debugLog)
                    Debug.Log($"[Trauma/Client] {name}: Stage 수신 {prev} → {next}");
            }
        }

        private bool IsNetworkActive =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private bool HasAuthority() => !IsNetworkActive || IsServer;

        private void OnEnable()
        {
            EventBus.OnTerrainTagChanged += OnTerrainTagChanged;
        }

        private void OnDisable()
        {
            EventBus.OnTerrainTagChanged -= OnTerrainTagChanged;
        }

        // ==================================================================
        // Update — 시간 기반 자동 회복
        // ==================================================================
        private void Update()
        {
            if (!HasAuthority()) return;   // [NGO-3] 서버만 진행

            if (autoRecoverySeconds > 0f
                && currentStage == TraumaStage.AcuteShock
                && Time.time - acuteShockEnterTime >= autoRecoverySeconds)
            {
                SetStage(TraumaStage.None);
                activeTrauma = null;
                explorationsSinceTrauma = 0;
                Debug.Log($"[Trauma] {name}: Stage 전이 AcuteShock → None (자동 회복, 경과 {autoRecoverySeconds:F0}s)");
            }
        }

        /// <summary>[NGO] Stage 설정 헬퍼 — currentStage + netStage 양쪽 갱신.</summary>
        private void SetStage(TraumaStage newStage)
        {
            currentStage = newStage;
            if (IsNetworkActive && IsServer)
                netStage.Value = newStage;
        }

        // ==================================================================
        // 핵심 API: 트라우마 부여
        // ==================================================================

        /// <summary>NPC에게 트라우마 부여. [NGO-3] 서버 권한.</summary>
        public void InflictTrauma(TraumaDataSO traumaSO)
        {
            if (!HasAuthority())
            {
                // 클라이언트에서 호출 시 ServerRpc로 위임 — 단, TraumaDataSO는 전송 불가
                // 대신 traumaID + severity만 전송
                if (traumaSO != null)
                    InflictTraumaServerRpc(traumaSO.traumaID ?? "unknown", traumaSO.severity);
                return;
            }

            if (traumaSO == null || currentStage != TraumaStage.None)
            {
                if (debugLog)
                    Debug.Log($"[Trauma] {name}: 부여 실패: SO={(traumaSO != null ? traumaSO.traumaID : "null")}, stage={currentStage}");
                return;
            }

            activeTrauma = traumaSO;
            SetStage(TraumaStage.AcuteShock);
            explorationsSinceTrauma = 0;
            acuteShockEnterTime = Time.time;

            // Acute Shock 즉각 적용
            if (brain != null)
            {
                brain.PivotPersonality(new float[] { 0, acuteStabilityDrop, 0, acuteAgreeableDrop, 0 });
                brain.fear = Mathf.Clamp01(brain.fear + traumaSO.severity);
            }

            if (triggerTags.Count == 0 && traumaSO.keywords != null)
                triggerTags.AddRange(traumaSO.keywords);

            Debug.Log($"[Trauma] {name}: 트라우마 부여: {traumaSO.traumaID}, severity={traumaSO.severity:F2}. " +
                      $"Stability{acuteStabilityDrop:+0.0;-0.0;0.0}, Agreeable{acuteAgreeableDrop:+0.0;-0.0;0.0}");
        }

        /// <summary>[Rpc(SendTo.Server)] 클라이언트 → 서버 trauma 부여 요청.</summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void InflictTraumaServerRpc(string traumaId, float severity)
        {
            if (currentStage != TraumaStage.None) return;

            // 서버에서 임시 SO 생성
            var tempSO = ScriptableObject.CreateInstance<TraumaDataSO>();
            tempSO.traumaID = traumaId;
            tempSO.severity = severity;
            tempSO.keywords = new List<string>();

            InflictTrauma(tempSO);
        }

        // ==================================================================
        // ITraumaProvider 구현
        // ==================================================================

        public int CurrentStageIndex => (int)currentStage;

        /// <summary>[ITraumaProvider] 트라우마 충격 부여.</summary>
        public void ApplyShock(float intensity, string causeId)
        {
            if (!HasAuthority())
            {
                ApplyShockServerRpc(intensity, causeId ?? "unknown");
                return;
            }

            if (currentStage != TraumaStage.None)
            {
                if (debugLog)
                    Debug.Log($"[Trauma] {name}: ApplyShock 무시 (이미 stage={currentStage})");
                return;
            }

            if (intensity < 0.6f)
            {
                if (brain != null)
                    brain.fear = Mathf.Clamp01(brain.fear + intensity * 0.3f);
                if (debugLog)
                    Debug.Log($"[Trauma] {name}: ApplyShock intensity={intensity:F2} < 0.6 → fear+{intensity * 0.3f:F2}만 (Stage 변화 없음)");
                return;
            }

            var tempSO = ScriptableObject.CreateInstance<TraumaDataSO>();
            tempSO.traumaID = causeId ?? "unknown";
            tempSO.severity = intensity;
            tempSO.keywords = new List<string>();

            InflictTrauma(tempSO);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void ApplyShockServerRpc(float intensity, string causeId)
        {
            ApplyShock(intensity, causeId);
        }

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
        // 탐험 완료 시 단계 진화
        // ==================================================================

        /// <summary>[NGO-3] 서버 권한. 클라 호출 시 ServerRpc로 위임.</summary>
        public void OnExplorationCompleted()
        {
            if (!HasAuthority())
            {
                OnExplorationCompletedServerRpc();
                return;
            }

            if (currentStage == TraumaStage.None || currentStage == TraumaStage.PermanentScarring)
                return;

            explorationsSinceTrauma++;

            if (currentStage == TraumaStage.AcuteShock)
            {
                if (explorationsSinceTrauma >= acuteShockDuration)
                {
                    SetStage(TraumaStage.Crossroads);
                    Debug.Log($"[Trauma] {name}: Stage 전이 AcuteShock → Crossroads (탐험 {explorationsSinceTrauma}회)");
                }
            }
            else if (currentStage == TraumaStage.Crossroads)
            {
                float trust = trustMatrix != null ? trustMatrix.CurrentTrust : 50f;

                if (trust >= recoveryTrustThreshold)
                {
                    if (brain != null)
                        brain.PivotPersonality(new float[] { 0, recoveryStabilityPerExploration, 0, 0, 0 });

                    if (brain != null && brain.Personality.stability >= brain.GetAnchor().stability - 0.1f)
                    {
                        SetStage(TraumaStage.None);
                        activeTrauma = null;
                        Debug.Log($"[Trauma] {name}: Stage 전이 Crossroads → None (트라우마 해소!)");
                    }
                    else if (debugLog)
                        Debug.Log($"[Trauma] {name}: 회복 중: Stability +{recoveryStabilityPerExploration:F2} (trust={trust:F1})");
                }
                else if (trust < worsenTrustThreshold)
                {
                    SetStage(TraumaStage.PermanentScarring);

                    if (brain != null)
                        brain.PivotPersonality(new float[] { 0, -0.3f, 0, -0.2f, 0.1f });

                    Debug.Log($"[Trauma] {name}: Stage 전이 Crossroads → PermanentScarring (영구 흉터 확정! trust={trust:F1})");
                }
            }
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void OnExplorationCompletedServerRpc() => OnExplorationCompleted();

        // ==================================================================
        // 트리거 감지
        // ==================================================================

        private void OnTerrainTagChanged(IReadOnlyList<string> newTags)
        {
            if (!HasAuthority()) return;   // [NGO-3] 서버만 반응
            if (currentStage == TraumaStage.None || newTags == null) return;

            foreach (var tag in newTags)
            {
                if (triggerTags.Contains(tag))
                {
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
        // 외부 API
        // ==================================================================

        public TraumaStage CurrentStage => currentStage;
        public TraumaDataSO ActiveTrauma => activeTrauma;
        public int ExplorationsSinceTrauma => explorationsSinceTrauma;

        // ==================================================================
        // Context Menu
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
            SetStage(TraumaStage.None);
            activeTrauma = null;
            explorationsSinceTrauma = 0;
            triggerTags.Clear();
        }

        // ==================================================================
        // [Day 3 Pivot 연동] Crossroads 해소 API (서버 권한)
        // ==================================================================

        /// <summary>
        /// Crossroads 단계의 Trauma를 해소. DilemmaPivotResolver가 Pivot 선택 후 호출.
        /// [NGO-3] 서버 권한. 클라 호출 시 ServerRpc 위임.
        /// </summary>
        public void ResolveCrossroadsByPivot(bool recovery)
        {
            if (!HasAuthority())
            {
                ResolveCrossroadsByPivotServerRpc(recovery);
                return;
            }

            if (currentStage != TraumaStage.Crossroads)
            {
                if (debugLog)
                    Debug.LogWarning($"[Trauma] {name}: ResolveCrossroadsByPivot 호출 but stage={currentStage}. 무시.");
                return;
            }

            if (recovery)
            {
                SetStage(TraumaStage.None);
                activeTrauma = null;
                explorationsSinceTrauma = 0;
                Debug.Log($"[Trauma] {name}: Pivot Resolution → None (회복)");
            }
            else
            {
                SetStage(TraumaStage.PermanentScarring);
                if (brain != null)
                    brain.PivotPersonality(new float[] { 0, -0.3f, 0, -0.2f, 0.1f });
                Debug.Log($"[Trauma] {name}: Pivot Resolution → PermanentScarring (악화)");
            }
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void ResolveCrossroadsByPivotServerRpc(bool recovery)
        {
            ResolveCrossroadsByPivot(recovery);
        }
    }
}
