// ─────────────────────────────────────────────────────────────────────
// pB-4 Week 3 D3 — STEP 2: BlackboardUpdater (v3.4 — GameBlackboard 게시 추가)
// ─────────────────────────────────────────────────────────────────────
// 갱신: 2026-04-29 v3.4 — D3 AM R5/R6 회로 차단 해소
//   ★ v3.1 → v3.4 변경 사항:
//     [추가] GameBlackboard.Instance.SetTerrainTags() 동시 게시
//     [사유] BaseAIBrain.UpdateFearFromTerrain()이
//            blackboard.GetActiveTerrainTags() = GameBlackboard.ActiveTerrainTags
//            (어댑터 경로)를 읽어 fear 계산.
//            BlackboardUpdater가 BG editor BB만 갱신하면 Brain은 빈 태그를 봄.
//            → fear 변동 안 됨 → Winner=Idle/FollowCommand 진동
//     [동작] 양쪽 동시 게시:
//            (1) BG editor BB.ActiveTerrainTags  ← Patrol/공격 노드용
//            (2) GameBlackboard.activeTerrainTags ← Brain.fear 회로용
//
//   디버그 옵션 3종 (v3.1 동일):
//     1. Verbose Logging (주기적): 매 N초마다 7 BB 변수 전체 출력
//     2. F1 단축키: Play 중 F1 누르면 즉시 1회 출력
//     3. ContextMenu: Inspector 우클릭 → "Log Current Snapshot"으로 즉시 출력
//
// 위치: Scripts/pB-4/week3_AI/BlackboardUpdater.cs
// ─────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;
using Unity.Behavior;
using TDA.PB4.Interfaces;
using TDA.PB4.Core;     // ★ v3.4: GameBlackboard 직접 게시

namespace TDA.PB4.AI.Context
{
    /// <summary>
    /// 10Hz tick으로 ContextManager → BG editor BB 변수 7개 갱신 + 디버그 로그.
    /// </summary>
    [RequireComponent(typeof(BehaviorGraphAgent))]
    public class BlackboardUpdater : MonoBehaviour
    {
        // ── 매직 넘버 금지: const 박제 ─────────────────────────────
        private const float TICK_INTERVAL = 0.1f;       // 10Hz
        private const float OFFSET_DELAY = 0.05f;       // Tick 분산
        private const float DEFAULT_LOG_INTERVAL = 1.0f; // 1초마다 로그

        // ── BB 변수명 const (오타 방지) ─────────────────────────────
        private const string BB_TERRAIN_SLOPE = "U_TerrainSlope";
        private const string BB_TERRAIN_DENSITY = "U_TerrainDensity";
        private const string BB_TERRAIN_OCCLUSION = "U_TerrainOcclusion";
        private const string BB_PATH_WIDTH = "U_PathWidth";
        private const string BB_TERRAIN_TAGS = "ActiveTerrainTags";
        private const string BB_GROUP_MORALE = "U_GroupMorale";
        private const string BB_TOKEN_AVAILABLE = "AttackTokenAvailable";

        [Header("의존성 (Inspector 할당)")]
        [SerializeField] private BehaviorGraphAgent _agent;
        [SerializeField] private ContextManager _context;

        [Header("디버그 — 주기적 로그")]
        [Tooltip("ON → Console에 N초마다 7 BB 변수 전체 출력")]
        [SerializeField] private bool _verboseLogging = false;

        [Tooltip("Verbose Logging 주기 (초). 기본 1초.")]
        [SerializeField] private float _logIntervalSeconds = DEFAULT_LOG_INTERVAL;

        [Header("디버그 — 즉시 로그")]
        [Tooltip("Play 중 본 키를 누르면 즉시 7 BB 변수 1회 출력")]
        [SerializeField] private KeyCode _instantLogKey = KeyCode.F1;

        private float _lastTick;
        private float _lastLogTime;
        private int _tickCount = 0;

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

        /// <summary>
        /// BB 변수 7개 갱신 (실제 동작 로직).
        /// ★ v3.4: GameBlackboard.SetTerrainTags() 동시 게시 추가.
        ///         Brain.UpdateFearFromTerrain()이 GameBlackboard 어댑터 경로를
        ///         읽으므로 양쪽 동시 게시 필요.
        /// </summary>
        private void UpdateBlackboardVariables()
        {
            if (_context == null || !_context.IsInjected) return;
            if (_agent == null) return;

            ContextSnapshot snap = _context.Sample(transform.position);

            // ── (1) BG editor BB 게시 ──────────────────────────────
            _agent.SetVariableValue(BB_TERRAIN_SLOPE, snap.slope);
            _agent.SetVariableValue(BB_TERRAIN_DENSITY, snap.density);
            _agent.SetVariableValue(BB_TERRAIN_OCCLUSION, snap.occlusion);
            _agent.SetVariableValue(BB_PATH_WIDTH, snap.pathWidth);
            _agent.SetVariableValue(BB_TERRAIN_TAGS, snap.terrainTags);
            _agent.SetVariableValue(BB_GROUP_MORALE, snap.groupMorale);
            _agent.SetVariableValue(BB_TOKEN_AVAILABLE, snap.attackTokenAvailable);

            // ── (2) ★ v3.4: GameBlackboard 동시 게시 ─────────────
            //   목적: BaseAIBrain.UpdateFearFromTerrain()이 읽는 경로.
            //   Wk0 동결 스키마: GameBlackboard.activeTerrainTags (List<string>).
            //   부재 시 무동작 (단독 Play 모드 폴백).
            var gameBb = GameBlackboard.Instance;
            if (gameBb != null && snap.terrainTags != null)
            {
                // Brain이 IReadOnlyList<string>로 읽으므로 List 그대로 전달.
                gameBb.SetTerrainTags(new List<string>(snap.terrainTags));
            }
        }

        /// <summary>
        /// 현재 위치의 7 BB 변수 전체를 Console에 출력.
        /// 단축키 / 주기적 / Inspector 우클릭에서 호출.
        /// </summary>
        [ContextMenu("Log Current Snapshot")]
        public void LogCurrentSnapshotMenu()
        {
            LogCurrentSnapshot("[MENU]");
        }

        private void LogCurrentSnapshot(string prefix)
        {
            if (_context == null)
            {
                Debug.LogWarning($"{prefix} [BlackboardUpdater] ContextManager 미할당");
                return;
            }

            if (!_context.IsInjected)
            {
                Debug.LogWarning($"{prefix} [BlackboardUpdater] ContextManager.Inject 미완료");
                return;
            }

            ContextSnapshot snap = _context.Sample(transform.position);
            Vector3 pos = transform.position;

            string tagsStr = (snap.terrainTags != null && snap.terrainTags.Count > 0)
                ? string.Join(",", snap.terrainTags)
                : "(empty)";

            // 한눈에 보기 쉬운 형식
            Debug.Log(
                $"{prefix} [BB Snapshot] tick #{_tickCount}\n" +
                $"  Position: ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})\n" +
                $"  ─── 지형 4 지표 ─────────────────\n" +
                $"  U_TerrainSlope:        {snap.slope:F3}\n" +
                $"  U_TerrainDensity:      {snap.density:F3}\n" +
                $"  U_TerrainOcclusion:    {snap.occlusion:F3}\n" +
                $"  U_PathWidth:           {snap.pathWidth:F3}m\n" +
                $"  ─── 자연 발현 태그 ──────────────\n" +
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
            if (_context == null || !_context.IsInjected)
            {
                Debug.LogWarning("[BB] ContextManager 미준비");
                return;
            }

            ContextSnapshot snap = _context.Sample(transform.position);
            Vector3 pos = transform.position;
            string tagsStr = (snap.terrainTags != null && snap.terrainTags.Count > 0)
                ? string.Join(",", snap.terrainTags)
                : "-";

            Debug.Log(
                $"[BB] pos=({pos.x:F1},{pos.z:F1}) " +
                $"slope={snap.slope:F2} density={snap.density:F2} " +
                $"occ={snap.occlusion:F2} width={snap.pathWidth:F2} " +
                $"tags=[{tagsStr}] morale={snap.groupMorale:F2} " +
                $"token={snap.attackTokenAvailable}"
            );
        }
    }
}