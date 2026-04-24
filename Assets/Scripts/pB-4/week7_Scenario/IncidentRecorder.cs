// =============================================================================
// IncidentRecorder.cs  |  pB-4 Project — Week 7 → Week 2 Day 3 T3.3 재배선 v2
// Layer  : L3 Domain (시나리오)
// Namespace: TDA.PB4.Scenario
//
// 역할:
//   결정적 게임 사건을 벡터 스냅샷 + 메타데이터로 기록.
//   LightweightRAGEngine(Wk7)이 이 기록을 유사도 검색에 사용.
//
// [Day 3 T3.3 재배선]
//   - IIncidentRecorder 인터페이스 구현 (기존 RecordIncident + 인터페이스 시그니처)
//   - EventBus 구독 (6개 이벤트): OnTrustTierChanged, OnFactionStateChanged, OnDilemmaResolved 등
//   - Karma 변환 내재화: 사건 → KarmaDirector.ChangeKarma 자동 연동
//   - Vector 인덱싱 hook (Wk7 RAG 연결 준비, SetVectorIndexer)
//
// [v3 NGO]
//   - NetworkBehaviour (서버 권한 기록)
//   - ServerRpc 진입점
// =============================================================================
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Director;
using TDA.PB4.Interfaces.Narrative;

namespace TDA.PB4.Scenario
{
    [Serializable]
    public class IncidentSnapshot
    {
        [Tooltip("사건 고유 ID. 예: 'incident_goblin_ambush_003'")]
        public string incidentId;

        [Tooltip("사건 강도 (0.0~1.0).")]
        public float intensityScore;

        [Tooltip("도덕적 성향 (-1.0~+1.0).")]
        public float moralAlignment;

        [Tooltip("사건 발생 시 활성 지형 태그.")]
        public List<string> terrainTags = new List<string>();

        [Tooltip("사건 발생 시 전역 긴장도.")]
        public float globalIntensity;

        [Tooltip("요약 문장.")]
        public string summaryProse;

        [Tooltip("관련 대사 앵커.")]
        public string dialogueAnchor;

        [Tooltip("사건 발생 시간 (게임 내 초).")]
        public float gameTimestamp;

        [Tooltip("768차원 상황 벡터. null이면 기록 시점 BB 자동 복사.")]
        public float[] situationVector;

        [Tooltip("[Day 3] 관련 플레이어 ID (Karma 연결용).")]
        public ulong issuerPlayerId;
    }

    public class IncidentRecorder : NetworkBehaviour, IIncidentRecorder
    {
        public static IncidentRecorder Instance { get; private set; }

        [Header("━━━ 기록 저장소 ━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private List<IncidentSnapshot> records = new List<IncidentSnapshot>();

        [Header("━━━ 자동 기록 설정 ━━━━━━━━━━━━━━━━━━")]
        [Range(0f, 1f)] public float autoRecordThreshold = 0.3f;
        [Range(10, 500)] public int maxRecords = 100;

        [Header("━━━ [Day 3] Karma 변환 설정 ━━━━━━━━")]

        [Tooltip("Trust Tier 변화 → Karma 영향. Hostility 진입 시 자동 Karma 감소.")]
        public bool enableKarmaConversion = true;

        [Tooltip("Trust Hostility 진입 시 Karma delta.")]
        [Range(-30f, 0f)] public float trustHostilityKarmaDelta = -5f;

        [Tooltip("Trust BlindTrust 진입 시 Karma delta.")]
        [Range(0f, 30f)] public float trustBlindTrustKarmaDelta = 5f;

        [Header("━━━ [Day 3] Vector 인덱싱 hook ━━━━━━")]

        /// <summary>Wk7 RAG 연결용 (indexer 주입).</summary>
        private Action<string, float[]> vectorIndexer;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        [SerializeField] private int totalRecorded = 0;

        // ==================================================================
        // Lifecycle
        // ==================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            // [검증 수정] Singleton dangling reference 방지
            if (Instance == this) Instance = null;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // EventBus 구독 (서버만)
            if (HasAuthority())
                SubscribeEventBus();
        }

        public override void OnNetworkDespawn()
        {
            if (HasAuthority())
                UnsubscribeEventBus();
            base.OnNetworkDespawn();
        }

        private void OnEnable()
        {
            // 단독 Play (NetworkManager 비활성) 호환
            if (!IsNetworkActive)
                SubscribeEventBus();
        }

        private void OnDisable()
        {
            if (!IsNetworkActive)
                UnsubscribeEventBus();
        }

        private bool subscribed = false;

        private void SubscribeEventBus()
        {
            if (subscribed) return;
            EventBus.OnTrustTierChanged += OnTrustTierChanged;
            EventBus.OnFactionStateChanged += OnFactionStateChanged;
            EventBus.OnIncidentRecorded += OnIncidentRecordedEvent;
            EventBus.OnAlignmentChanged += OnAlignmentChanged;
            subscribed = true;
        }

        private void UnsubscribeEventBus()
        {
            if (!subscribed) return;
            EventBus.OnTrustTierChanged -= OnTrustTierChanged;
            EventBus.OnFactionStateChanged -= OnFactionStateChanged;
            EventBus.OnIncidentRecorded -= OnIncidentRecordedEvent;
            EventBus.OnAlignmentChanged -= OnAlignmentChanged;
            subscribed = false;
        }

        private bool IsNetworkActive =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private bool HasAuthority() => !IsNetworkActive || IsServer;

        // ==================================================================
        // IIncidentRecorder 인터페이스 구현
        // ==================================================================

        public void RecordIncident(string incidentId, float intensityScore, string moralAlignment)
        {
            // 인터페이스용 어댑터: string moral을 float로 변환
            float moralFloat = 0f;
            if (float.TryParse(moralAlignment, out var parsed)) moralFloat = parsed;

            RecordIncidentInternal(incidentId, intensityScore, moralFloat, "", "", 0);
        }

        public void SetVectorIndexer(Action<string, float[]> indexer)
        {
            vectorIndexer = indexer;
            if (debugLog)
                Debug.Log($"[Incident] Vector indexer 설정됨");
        }

        // ==================================================================
        // 핵심 API: 상세 사건 기록 (서버 권한)
        // ==================================================================

        /// <summary>상세 정보 있는 사건 기록.</summary>
        public void RecordIncident(string id, float intensity, float moral, string summary, string dialogueAnchor = "", ulong issuerPlayerId = 0)
        {
            if (!HasAuthority())
            {
                RecordIncidentServerRpc(id, intensity, moral, summary, dialogueAnchor, issuerPlayerId);
                return;
            }

            RecordIncidentInternal(id, intensity, moral, summary, dialogueAnchor, issuerPlayerId);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RecordIncidentServerRpc(string id, float intensity, float moral, string summary, string dialogueAnchor, ulong issuerPlayerId)
        {
            RecordIncidentInternal(id, intensity, moral, summary, dialogueAnchor, issuerPlayerId);
        }

        private void RecordIncidentInternal(string id, float intensity, float moral, string summary, string dialogueAnchor, ulong issuerPlayerId)
        {
            if (intensity < autoRecordThreshold) return;

            var bb = GameBlackboard.Instance;
            var snapshot = new IncidentSnapshot
            {
                incidentId = id,
                intensityScore = intensity,
                moralAlignment = moral,
                terrainTags = bb != null && bb.ActiveTerrainTags != null
                    ? new List<string>(bb.ActiveTerrainTags)
                    : new List<string>(),
                globalIntensity = bb != null ? bb.GlobalIntensityScore : 0f,
                summaryProse = summary,
                dialogueAnchor = dialogueAnchor,
                gameTimestamp = Time.time,
                situationVector = bb != null ? (float[])bb.ActiveSituationVector.Clone() : null,
                issuerPlayerId = issuerPlayerId
            };

            records.Add(snapshot);
            totalRecorded++;

            while (records.Count > maxRecords) records.RemoveAt(0);

            // [Day 3] Vector 인덱싱 hook
            if (vectorIndexer != null && snapshot.situationVector != null)
                vectorIndexer.Invoke(id, snapshot.situationVector);

            // [Day 3] Karma 변환 (moralAlignment 기반)
            if (enableKarmaConversion && Mathf.Abs(moral) > 0.1f && KarmaDirector.Instance != null)
            {
                float karmaDelta = moral * 10f;   // moral -1.0 ~ +1.0 → karma -10 ~ +10
                KarmaDirector.Instance.ChangeKarma(issuerPlayerId, karmaDelta, KarmaChangeReason.DilemmaChoice);
            }

            // EventBus 발행 (다른 구독자에게 알림)
            EventBus.RaiseIncidentRecorded(id);

            if (debugLog)
                Debug.Log($"[Incident] 기록: {id} (intensity={intensity:F2}, moral={moral:F2}) '{summary}'");
        }

        // ==================================================================
        // EventBus 핸들러 (서버 자동 기록)
        // ==================================================================

        private void OnTrustTierChanged(string npcId, int oldTier, int newTier)
        {
            if (!HasAuthority()) return;

            if (newTier == 0)   // Hostility 진입
            {
                RecordIncidentInternal(
                    $"trust_hostility_{npcId}_{Time.time:F0}",
                    0.6f,
                    -0.4f,
                    $"{npcId}가 적대적으로 변했다.",
                    "trust_broken",
                    0);

                if (enableKarmaConversion && KarmaDirector.Instance != null)
                    KarmaDirector.Instance.ChangeKarma(0, trustHostilityKarmaDelta, KarmaChangeReason.BetrayedAlly);
            }
            else if (newTier == 3)   // BlindTrust 진입
            {
                RecordIncidentInternal(
                    $"trust_blindtrust_{npcId}_{Time.time:F0}",
                    0.7f,
                    0.5f,
                    $"{npcId}가 절대 신뢰를 보낸다.",
                    "trust_blind",
                    0);

                if (enableKarmaConversion && KarmaDirector.Instance != null)
                    KarmaDirector.Instance.ChangeKarma(0, trustBlindTrustKarmaDelta, KarmaChangeReason.RescuedAlly);
            }
        }

        private void OnFactionStateChanged(string factionId, FactionWorldState state)
        {
            if (!HasAuthority()) return;
            // 주요 팩션 멸종 등은 큰 사건
            if (state.populationLevel <= 0.1f)
            {
                RecordIncidentInternal(
                    $"faction_decimated_{factionId}_{Time.time:F0}",
                    0.8f,
                    -0.3f,
                    $"팩션 '{factionId}' 거의 전멸.",
                    "faction_fallen",
                    0);
            }
        }

        private void OnIncidentRecordedEvent(string incidentId)
        {
            // 다른 곳에서 발행한 사건 — 중복 방지 체크 가능
        }

        private void OnAlignmentChanged(string npcId, int oldAlign, int newAlign, AlignmentChangeReason reason)
        {
            if (!HasAuthority()) return;

            if (newAlign == 0 && oldAlign >= 2)   // Friendly/Companion → Hostile
            {
                RecordIncidentInternal(
                    $"alignment_fallen_{npcId}_{Time.time:F0}",
                    0.7f,
                    -0.5f,
                    $"{npcId}가 적으로 돌아섰다.",
                    "ally_lost",
                    0);
            }
            else if (newAlign == 3 && oldAlign <= 1)   // Hostile/Neutral → Companion
            {
                RecordIncidentInternal(
                    $"alignment_bonded_{npcId}_{Time.time:F0}",
                    0.8f,
                    0.4f,
                    $"{npcId}가 동료가 되었다.",
                    "new_bond",
                    0);
            }
        }

        // ==================================================================
        // JSON 직렬화
        // ==================================================================

        public string SerializeToJSON()
        {
            return JsonUtility.ToJson(new IncidentRecordWrapper { incidents = records }, true);
        }

        public void DeserializeFromJSON(string json)
        {
            var wrapper = JsonUtility.FromJson<IncidentRecordWrapper>(json);
            if (wrapper != null && wrapper.incidents != null)
                records = wrapper.incidents;
        }

        public IReadOnlyList<IncidentSnapshot> Records => records;

        [Serializable]
        private class IncidentRecordWrapper { public List<IncidentSnapshot> incidents; }

        // ==================================================================
        // Context Menu
        // ==================================================================

        [ContextMenu("Debug/Record Test Incident")]
        private void DebugRecord()
        {
            RecordIncident("test_" + Time.time.ToString("F0"), 0.5f, 0f, "Debug 테스트 사건", "debug");
        }

        [ContextMenu("Debug/Dump Records")]
        private void DebugDump()
        {
            Debug.Log($"[Incident] 총 {records.Count}건 기록됨");
            foreach (var r in records)
                Debug.Log($"  - {r.incidentId}: intensity={r.intensityScore:F2}, moral={r.moralAlignment:F2}");
        }
    }
}
