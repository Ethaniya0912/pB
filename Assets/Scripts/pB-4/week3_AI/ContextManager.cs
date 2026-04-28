// ─────────────────────────────────────────────────────────────────────
// pB-4 Week 3 D2 3차 — STEP 1: ContextManager (v3)
// ─────────────────────────────────────────────────────────────────────
// 작성: 2026-04-28 (D2 3차 STEP 1, v3 — using 분기 namespace 박제)
// 위치: Scripts/pB-4/week3_AI/ContextManager.cs
//
// 코딩 표준:
//   §3.4 L4 Coordination 계층 (MonoBehaviour, NetworkBehaviour 아님)
//   §4.5 골든룰 4: NV 갱신은 L2 Router 한정 → 본 클래스는 NV 미사용
//   매직 넘버 금지: const 박제
//
// 의존성 (4종, Inject 메서드로 주입):
//   ① ITerrainSampler   (TDA.PB4.Interfaces.Environment)
//   ② IContextAnalyzer  (TDA.PB4.Interfaces.Environment)
//   ③ IIncidentRecorder (TDA.PB4.Interfaces.Narrative) — RecordIncident만, 읽기 X
//   ④ IGroupAIInfo      (TDA.PB4.Interfaces) — D3 PM GroupAIManager 작성 시 구현
//
// ★ 자연 발현 박제 (D1 DEBT-21 교훈):
//   forceTagOverride 같은 강제 주입 메커니즘 일체 사용 안 함.
//
// ★ Wk0 동결 IIncidentRecorder는 read 메서드 없음 → ContextSnapshot 7 필드.
//   Wk5+ 시그니처 통일 협의 완료 후 recentIncidents 필드 추가 박제.
// ─────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Interfaces;
using TDA.PB4.Interfaces.Environment;
using TDA.PB4.Interfaces.Narrative;

namespace TDA.PB4.AI.Context
{
    /// <summary>
    /// Wk3 데이터 파이프라인 1~2단.
    /// 4 의존성 주입 후 Sample(worldPos) → ContextSnapshot 7 필드 반환.
    /// </summary>
    public class ContextManager : MonoBehaviour, IContextProvider
    {
        // ── 매직 넘버 금지: 그룹 미존재 시 기본값 const 박제 ────────
        private const float DEFAULT_MORALE_NO_GROUP = 1.0f;
        private const bool DEFAULT_TOKEN_NO_GROUP = true;

        // ── 4 의존성 (Inject 메서드로 주입, 직접 SerializeField X) ──
        private ITerrainSampler _terrain;
        private IContextAnalyzer _analyzer;
        private IIncidentRecorder _recorder;   // RecordIncident 호출용 (Wk5+ 활용)
        private IGroupAIInfo _group;            // 선택적 (D3 PM 도착 전 null 가능)

        private bool _isInjected = false;

        [Header("디버깅")]
        [SerializeField] private bool _verboseLogging = false;

        public bool IsInjected => _isInjected;

        /// <summary>
        /// 4 의존성 주입. Week3Bootstrapper.Awake()에서 호출.
        /// group은 선택적 (D3 PM GroupAIManager 작성 전 null 가능).
        /// </summary>
        public void Inject(
            ITerrainSampler terrain,
            IContextAnalyzer analyzer,
            IIncidentRecorder recorder,
            IGroupAIInfo group)
        {
            _terrain = terrain;
            _analyzer = analyzer;
            _recorder = recorder;
            _group = group;  // null 허용
            _isInjected = true;

            Debug.Log("[ContextManager] Inject 완료: " +
                      $"terrain={terrain != null}, analyzer={analyzer != null}, " +
                      $"recorder={recorder != null}, group={group != null}");
        }

        /// <summary>
        /// 지정 좌표의 컨텍스트 스냅샷 반환 (7 필드).
        /// BlackboardUpdater가 0.1초마다 호출 (10Hz).
        /// </summary>
        public ContextSnapshot Sample(Vector3 worldPos)
        {
            if (!_isInjected)
            {
                Debug.LogWarning("[ContextManager] Inject 미완료 상태에서 Sample 호출");
                return CreateEmptySnapshot();
            }

            return new ContextSnapshot
            {
                // ── 지형 4 지표 (자연 발현, 좌표 기반 계산) ──────────
                slope = _terrain.SampleSlope(worldPos),
                density = _terrain.SampleDensity(worldPos),
                occlusion = _terrain.SampleOcclusion(worldPos),
                pathWidth = _terrain.SamplePathWidth(worldPos),

                // ── 지형 태그 (자연 발현, width<1.5 → "Narrow" 등) ──
                terrainTags = _analyzer.AnalyzeTerrain(worldPos),

                // ── 그룹 상태 (GroupAIManager, 선택적) ──────────────
                groupMorale = _group != null
                    ? _group.GetMorale()
                    : DEFAULT_MORALE_NO_GROUP,

                attackTokenAvailable = _group != null
                    ? _group.HasAvailableToken()
                    : DEFAULT_TOKEN_NO_GROUP
            };
        }

        /// <summary>
        /// IIncidentRecorder 사건 기록 헬퍼 (외부에서 호출 가능).
        /// Wk0 동결 시그니처: (incidentId, intensityScore, moralAlignment).
        /// </summary>
        public void RecordIncident(string incidentId, float intensityScore, string moralAlignment)
        {
            if (_recorder == null) return;
            _recorder.RecordIncident(incidentId, intensityScore, moralAlignment);
        }

        /// <summary>
        /// Inject 미완료 시 반환할 기본 스냅샷.
        /// NRE 방지 (모든 필드 안전한 기본값).
        /// </summary>
        private ContextSnapshot CreateEmptySnapshot()
        {
            return new ContextSnapshot
            {
                slope = 0f,
                density = 0.5f,
                occlusion = 0f,
                pathWidth = 5.0f,
                terrainTags = new List<string>(),
                groupMorale = DEFAULT_MORALE_NO_GROUP,
                attackTokenAvailable = DEFAULT_TOKEN_NO_GROUP
            };
        }

        [ContextMenu("Log Current Snapshot")]
        public void LogCurrentSnapshotMenu()
        {
            if (!_isInjected)
            {
                Debug.LogWarning("[ContextManager] Inject 미완료");
                return;
            }

            var snap = Sample(transform.position);
            Vector3 pos = transform.position;
            string tagsStr = (snap.terrainTags != null && snap.terrainTags.Count > 0)
                ? string.Join(",", snap.terrainTags)
                : "(empty)";

            Debug.Log(
                $"[ContextManager Snapshot] @ ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})\n" +
                $"  slope={snap.slope:F3} density={snap.density:F3} " +
                $"occlusion={snap.occlusion:F3} width={snap.pathWidth:F3}m\n" +
                $"  tags=[{tagsStr}]\n" +
                $"  morale={snap.groupMorale:F3} token={snap.attackTokenAvailable}"
            );
        }

        public void LogCurrentSnapshot(Vector3 worldPos)
        {
            if (!_verboseLogging) return;
            var snap = Sample(worldPos);
            Debug.Log($"[ContextManager] @ {worldPos}: " +
                      $"slope={snap.slope:F2}, width={snap.pathWidth:F2}, " +
                      $"tags=[{string.Join(",", snap.terrainTags)}], " +
                      $"morale={snap.groupMorale:F2}");
        }
    }
}