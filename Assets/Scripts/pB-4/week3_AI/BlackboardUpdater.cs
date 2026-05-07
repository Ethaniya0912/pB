// ─────────────────────────────────────────────────────────────────────
// pB-4 Week 3 — STEP 2: BlackboardUpdater (v4 — Tier 2 우선 + 13 BB 변수)
// ─────────────────────────────────────────────────────────────────────
// 갱신: 2026-05-03 v4 — Tier 2 우선 디렉션 반영
//   v3.4 → v4 변경 사항:
//     [폐기] [SerializeField] private ContextManager _context — Inspector 참조
//     [추가] WorldAIContextManager.Instance 직접 사용 (싱글톤 패턴)
//     [확장] BB 변수 7 → 13 (+6 신규: nearestNodeIdx, nearestPassageIdx,
//                              identityTags, stateTags, eventTags, potentialTags)
//     [박제] uint → int 캐스팅 박제 — § 8.5.3 가이드 v2 참조
//     [보존] GameBlackboard.SetTerrainTags() 동시 게시 (Phase 4 까지 다리)
//     [보존] 디버그 옵션 3종 (Verbose / 단축키 / ContextMenu)
//     [보존] 10Hz tick 갱신 + Tick 분산 OFFSET_DELAY
//
// 위치: Scripts/pB-4/week3_AI/BlackboardUpdater.cs
//
// ★ uint → int 캐스팅 박제 (BG editor BB 미지원 우회):
//   배경: BG editor BB Type 드롭다운에 uint 없음 (Boolean/Color/Double/Float/Integer/String 만)
//   해법: int 캐스팅으로 게시. 비트 31 (MSB) 사용 시 BG editor 에 음수 표시되지만 동작 무관.
//   안전 마진: 현재 모든 카테고리 16 비트 미만 사용 (IDENTITY 15 / STATE 16 / EVENT 14 / POTENTIAL 15)
//             → 부호 비트 (31) 까지 16 비트 이상 빈 공간 → 음수 변환 가능성 0.
//   BT 노드 사용 시: (uint)bb.GetInt("IdentityTags") 로 다시 uint 캐스팅 후 비트 비교.
//   상세: pB4_Week3_AI_가이드_v2 § 8.5.3
// ─────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;
using Unity.Behavior;
using TDA.PB4.Interfaces;
using TDA.PB4.AI.Context;     // ★ v4 — WorldAIContextManager.Instance 사용
using TDA.PB4.Core;            // GameBlackboard 직접 게시

namespace TDA.PB4.AI.Context
{
    /// <summary>
    /// 10Hz tick으로 WorldAIContextManager → BG editor BB 변수 13개 갱신 + 디버그 로그.
    /// ★ v4 — 싱글톤 사용으로 Inspector 필드 1 개 제거 (NPC Prefab 셋업 단순화).
    /// </summary>
    [RequireComponent(typeof(BehaviorGraphAgent))]
    public class BlackboardUpdater : MonoBehaviour
    {
        // ═════════════════════════════════════════════════════════════
        // 매직 넘버 const
        // ═════════════════════════════════════════════════════════════
        private const float TICK_INTERVAL = 0.1f;        // 10Hz
        private const float OFFSET_DELAY = 0.05f;        // Tick 분산
        private const float DEFAULT_LOG_INTERVAL = 1.0f; // 1초마다 로그

        // ═════════════════════════════════════════════════════════════
        // BB 변수명 const — 오타 방지 (HumanoidAI_BehaviorGraph 와 정확 매치 필수)
        // ═════════════════════════════════════════════════════════════
        // ── v3 호환 7 변수 (보존) ───────────────────────────────────
        private const string BB_TERRAIN_SLOPE = "U_TerrainSlope";
        private const string BB_TERRAIN_DENSITY = "U_TerrainDensity";
        private const string BB_TERRAIN_OCCLUSION = "U_TerrainOcclusion";
        private const string BB_PATH_WIDTH = "U_PathWidth";
        private const string BB_TERRAIN_TAGS = "ActiveTerrainTags";
        private const string BB_GROUP_MORALE = "U_GroupMorale";
        private const string BB_TOKEN_AVAILABLE = "AttackTokenAvailable";

        // ── ★ v4 신규 6 변수 (Tier 2) ───────────────────────────────
        private const string BB_NEAREST_NODE_IDX = "NearestNodeIdx";
        private const string BB_NEAREST_PASSAGE_IDX = "NearestPassageIdx";
        private const string BB_IDENTITY_TAGS = "IdentityTags";
        private const string BB_STATE_TAGS = "StateTags";
        private const string BB_EVENT_TAGS = "EventTags";
        private const string BB_POTENTIAL_TAGS = "PotentialTags";

        // ═════════════════════════════════════════════════════════════
        // 의존성 (★ v4 — Inspector 필드 _context 폐기)
        // ═════════════════════════════════════════════════════════════
        [Header("의존성 (Inspector 할당)")]
        [SerializeField] private BehaviorGraphAgent _agent;
        // ★ v4 폐기: [SerializeField] private ContextManager _context;
        //    → WorldAIContextManager.Instance 직접 사용 (NPC Prefab 100 개 셋업 부담 ↓)

        [Header("디버그 — 주기적 로그")]
        [Tooltip("ON → Console에 N초마다 13 BB 변수 전체 출력")]
        [SerializeField] private bool _verboseLogging = false;

        [Tooltip("Verbose Logging 주기 (초). 기본 1초.")]
        [SerializeField] private float _logIntervalSeconds = DEFAULT_LOG_INTERVAL;

        [Header("디버그 — 즉시 로그")]
        [Tooltip("Play 중 본 키를 누르면 즉시 13 BB 변수 1회 출력")]
        [SerializeField] private KeyCode _instantLogKey = KeyCode.F1;

        // ─────────────────────────────────────────────────────────────
        // 내부 상태
        // ─────────────────────────────────────────────────────────────
        private float _lastTick;
        private float _lastLogTime;
        private int _tickCount = 0;

        // ═════════════════════════════════════════════════════════════
        // Unity 라이프사이클
        // ═════════════════════════════════════════════════════════════
        private void Start()
        {
            _lastTick = Time.time + OFFSET_DELAY;
            _lastLogTime = Time.time;

            if (_agent == null) _agent = GetComponent<BehaviorGraphAgent>();
        }

        private void Update()
        {
            // ── BB 변수 10Hz 갱신 ──────────────────────────────────
            if (Time.time - _lastTick >= TICK_INTERVAL)
            {
                _lastTick = Time.time;
                _tickCount++;
                UpdateBlackboardVariables();
            }

            // ── 즉시 로그 (단축키) ──────────────────────────────────
            if (Input.GetKeyDown(_instantLogKey))
            {
                LogCurrentSnapshot("[INSTANT-KEY]");
            }

            // ── 주기적 로그 ─────────────────────────────────────────
            if (_verboseLogging && Time.time - _lastLogTime >= _logIntervalSeconds)
            {
                _lastLogTime = Time.time;
                LogCurrentSnapshot("[VERBOSE]");
            }
        }

        // ═════════════════════════════════════════════════════════════
        // BB 변수 13 종 갱신 (★ v4)
        // ═════════════════════════════════════════════════════════════
        /// <summary>
        /// BB 변수 13개 갱신 (실제 동작 로직).
        ///
        /// ★ v4 변경:
        ///   - WorldAIContextManager.Instance 직접 사용 (싱글톤)
        ///   - 신규 6 변수 게시: 그래프 idx 2 + 4 카테고리 비트마스크
        ///   - uint → int 캐스팅 (BG editor BB 미지원 우회)
        ///
        /// ★ GameBlackboard.SetTerrainTags() 동시 게시 보존:
        ///   Brain.UpdateFearFromTerrain() 이 GameBlackboard 어댑터 경로 읽음.
        ///   Phase 4 마이그레이션 (60 bit 직접 활용) 후 제거 가능.
        /// </summary>
        private void UpdateBlackboardVariables()
        {
            // ── 사전 검증 ──────────────────────────────────────────
            var ctx = WorldAIContextManager.Instance;
            if (ctx == null || !ctx.IsInjected) return;
            if (_agent == null) return;

            ContextSnapshot snap = ctx.Sample(transform.position);

            // ── (1) BG editor BB 게시 — v3 호환 7 변수 ─────────────
            _agent.SetVariableValue(BB_TERRAIN_SLOPE, snap.slope);
            _agent.SetVariableValue(BB_TERRAIN_DENSITY, snap.density);
            _agent.SetVariableValue(BB_TERRAIN_OCCLUSION, snap.occlusion);
            _agent.SetVariableValue(BB_PATH_WIDTH, snap.pathWidth);
            _agent.SetVariableValue(BB_TERRAIN_TAGS, snap.terrainTags);
            _agent.SetVariableValue(BB_GROUP_MORALE, snap.groupMorale);
            _agent.SetVariableValue(BB_TOKEN_AVAILABLE, snap.attackTokenAvailable);

            // ── (2) ★ v4 신규 6 변수 게시 (Tier 2 비트마스크) ──────
            //   uint → int 캐스팅 박제:
            //     - BG editor BB 가 uint 미지원 → int 로 캐스팅
            //     - 안전 마진: 모든 카테고리 16 비트 미만 사용 → 음수 변환 가능성 0
            //     - BT 노드에서 사용 시 (uint)bb.GetInt("IdentityTags") 로 재 캐스팅
            //     - 상세: pB4_Week3_AI_가이드_v2 § 8.5.3
            _agent.SetVariableValue(BB_NEAREST_NODE_IDX, snap.nearestNodeIdx);    // 이미 int
            _agent.SetVariableValue(BB_NEAREST_PASSAGE_IDX, snap.nearestPassageIdx); // 이미 int
            _agent.SetVariableValue(BB_IDENTITY_TAGS, (int)snap.identityTags); // ★ uint → int 캐스팅
            _agent.SetVariableValue(BB_STATE_TAGS, (int)snap.stateTags);    // ★
            _agent.SetVariableValue(BB_EVENT_TAGS, (int)snap.eventTags);    // ★
            _agent.SetVariableValue(BB_POTENTIAL_TAGS, (int)snap.potentialTags);// ★

            // ── (3) GameBlackboard 동시 게시 (Phase 4 까지 다리) ───
            //   목적: BaseAIBrain.UpdateFearFromTerrain() 이 읽는 어댑터 경로.
            //   Wk0 동결 스키마: GameBlackboard.activeTerrainTags (List<string>).
            //   부재 시 무동작 (단독 Play 모드 폴백).
            var gameBb = GameBlackboard.Instance;
            if (gameBb != null && snap.terrainTags != null)
            {
                gameBb.SetTerrainTags(new List<string>(snap.terrainTags));
            }
        }

        // ═════════════════════════════════════════════════════════════
        // 디버그 로그 — 13 변수 전체 출력
        // ═════════════════════════════════════════════════════════════
        [ContextMenu("Log Current Snapshot")]
        public void LogCurrentSnapshotMenu()
        {
            LogCurrentSnapshot("[MENU]");
        }

        private void LogCurrentSnapshot(string prefix)
        {
            var ctx = WorldAIContextManager.Instance;
            if (ctx == null)
            {
                Debug.LogWarning($"{prefix} [BlackboardUpdater] WorldAIContextManager.Instance == null");
                return;
            }
            if (!ctx.IsInjected)
            {
                Debug.LogWarning($"{prefix} [BlackboardUpdater] WorldAIContextManager.Inject 미완료");
                return;
            }

            ContextSnapshot snap = ctx.Sample(transform.position);
            Vector3 pos = transform.position;

            string tagsStr = (snap.terrainTags != null && snap.terrainTags.Count > 0)
                ? string.Join(",", snap.terrainTags)
                : "(empty)";

            Debug.Log(
                $"{prefix} [BB Snapshot] tick #{_tickCount}\n" +
                $"  Position: ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})\n" +
                $"  ─── ★ Tier 2 그래프 위치 (★ v4 신규) ───────\n" +
                $"  NearestNodeIdx:        {snap.nearestNodeIdx}\n" +
                $"  NearestPassageIdx:     {snap.nearestPassageIdx}\n" +
                $"  NearestKind:           {snap.nearestKind}\n" +
                $"  ─── ★ 4 카테고리 비트마스크 (★ v4 신규) ──\n" +
                $"  IdentityTags:          0x{snap.identityTags:X8} (int={(int)snap.identityTags})\n" +
                $"  StateTags:             0x{snap.stateTags:X8} (int={(int)snap.stateTags})\n" +
                $"  EventTags:             0x{snap.eventTags:X8} (int={(int)snap.eventTags})\n" +
                $"  PotentialTags:         0x{snap.potentialTags:X8} (int={(int)snap.potentialTags})\n" +
                $"  ─── Role (★ v4 신규) ────────────────────\n" +
                $"  NodeRole:              {snap.nodeRole}\n" +
                $"  PassageRole:           {snap.passageRole}\n" +
                $"  ─── 지형 4 지표 ─────────────────\n" +
                $"  U_TerrainSlope:        {snap.slope:F3}\n" +
                $"  U_TerrainDensity:      {snap.density:F3}\n" +
                $"  U_TerrainOcclusion:    {snap.occlusion:F3}\n" +
                $"  U_PathWidth:           {snap.pathWidth:F3}m\n" +
                $"  ─── 자연 발현 태그 (Phase 4 호환) ────────\n" +
                $"  ActiveTerrainTags:     [{tagsStr}]\n" +
                $"  ─── 그룹 상태 ───────────────────\n" +
                $"  U_GroupMorale:         {snap.groupMorale:F3}\n" +
                $"  AttackTokenAvailable:  {snap.attackTokenAvailable}"
            );
        }

        /// <summary>
        /// 컴팩트 1줄 로그 (반복 출력 시 가독성).
        /// </summary>
        [ContextMenu("Log Current Snapshot (Compact)")]
        public void LogCurrentSnapshotCompact()
        {
            var ctx = WorldAIContextManager.Instance;
            if (ctx == null || !ctx.IsInjected)
            {
                Debug.LogWarning("[BB] WorldAIContextManager 미준비");
                return;
            }

            ContextSnapshot snap = ctx.Sample(transform.position);
            Vector3 pos = transform.position;
            string tagsStr = (snap.terrainTags != null && snap.terrainTags.Count > 0)
                ? string.Join(",", snap.terrainTags)
                : "-";

            Debug.Log(
                $"[BB] pos=({pos.x:F1},{pos.z:F1}) " +
                $"node={snap.nearestNodeIdx} edge={snap.nearestPassageIdx} " +
                $"I=0x{snap.identityTags:X4} S=0x{snap.stateTags:X4} " +
                $"E=0x{snap.eventTags:X4} P=0x{snap.potentialTags:X4} " +
                $"slope={snap.slope:F2} width={snap.pathWidth:F2} " +
                $"morale={snap.groupMorale:F2} token={snap.attackTokenAvailable}"
            );
        }
    }
}