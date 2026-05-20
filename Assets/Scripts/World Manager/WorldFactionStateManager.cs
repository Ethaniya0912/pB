// ============================================================================
// WorldFactionStateManager.cs
// Faction 의 런타임 4 Tag 상태 보관 매니저
// 영역: 사이버 (싱글톤 매니저)
// 항목: P-02
// ============================================================================
//
// 책임:
//   - FactionDefinitionSO 등록 (Inspector)
//   - Dictionary<string, FactionStateBits> 보관 (런타임 상태)
//   - SetXxx / ForceXxx 두 메서드 분리 (DefaultMask 검사 / 우회)
//   - MetaScenarioGenerator 결과 수신 + Mapper 통한 4 Tag 변환
//   - OnFactionStateChanged 이벤트 발행
//   - FactionStateSnapshot 외부 노출 (readonly DTO)
//
// 어댑테이션 포인트:
//   - MetaScenarioGenerator 의 결과 수신 방식:
//     a) EventBus 구독 (Awake) — 사용자 측 EventBus 인프라
//     b) MetaScenarioGenerator 가 직접 ApplyMetaScenarioUpdate(...) 호출
//   본 매니저는 두 방식 모두 지원 — public ApplyMetaScenarioUpdate() 제공.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TDA.PB4.Data;
using TDA.PB4.Interfaces;
using TDA.PB4.Faction.Tags;

namespace TDA.PB4.Faction
{
    public class WorldFactionStateManager : MonoBehaviour,
        IFactionStateProvider,
        IFactionStateCommandReceiver
    {
        // ────────────────────────────────────────
        // Singleton
        // ────────────────────────────────────────

        public static WorldFactionStateManager Instance { get; private set; }

        // ────────────────────────────────────────
        // Inspector — 등록 SO
        // ────────────────────────────────────────

        [Header("━━━ Faction 등록 (Inspector) ━━━")]

        [Tooltip("씬 진입 시 등록할 Faction Definition 목록. Awake 에 Dictionary 로 변환.")]
        [SerializeField]
        private List<FactionDefinitionSO> _factionDefinitions =
            new List<FactionDefinitionSO>();

        [Header("━━━ 디버그 ━━━")]

        [SerializeField] private bool _verboseLog = false;

        // ════════════════════════════════════════════════════════
        // ★ v1.2 — 통계 카운터 (E-03 / S2 신규)
        // ════════════════════════════════════════════════════════
        // 본 카운터는 E-06 Diagnostics + E-10 Dashboard 진입 자료.
        // E-06 진입 시 FactionStateTagCategory enum (Mood / Tactical / Lifecycle /
        // Relation) 측 4 값 정합 — 본 시점 측 const int 4 측 정합 (동일 순서).
        // ════════════════════════════════════════════════════════

        private const int CATEGORY_MOOD = 0;
        private const int CATEGORY_TACTICAL = 1;
        private const int CATEGORY_LIFECYCLE = 2;
        private const int CATEGORY_RELATION = 3;
        private const int CATEGORY_COUNT = 4;

        // 총 카운터
        private int _setCallCount;
        private int _forceCallCount;
        private int _maskViolationCount;
        private int _mapperApplyCount;

        // 카테고리별 카운터 (int[4] — 인덱스 = CATEGORY_*)
        private readonly int[] _setCallsByCategory = new int[CATEGORY_COUNT];
        private readonly int[] _forceCallsByCategory = new int[CATEGORY_COUNT];
        private readonly int[] _maskViolationsByCategory = new int[CATEGORY_COUNT];

        // 외부 read-only property (E-06 + E-10 진입 자료)
        public int SetCallCount => _setCallCount;
        public int ForceCallCount => _forceCallCount;
        public int MaskViolationCount => _maskViolationCount;
        public int MapperApplyCount => _mapperApplyCount;

        public int GetSetCallsByCategory(int categoryIndex)
            => (categoryIndex >= 0 && categoryIndex < CATEGORY_COUNT)
                ? _setCallsByCategory[categoryIndex] : 0;

        public int GetForceCallsByCategory(int categoryIndex)
            => (categoryIndex >= 0 && categoryIndex < CATEGORY_COUNT)
                ? _forceCallsByCategory[categoryIndex] : 0;

        public int GetMaskViolationsByCategory(int categoryIndex)
            => (categoryIndex >= 0 && categoryIndex < CATEGORY_COUNT)
                ? _maskViolationsByCategory[categoryIndex] : 0;

        // ────────────────────────────────────────
        // 런타임 상태 (private)
        // ────────────────────────────────────────

        private Dictionary<string, FactionDefinitionSO> _definitionMap;
        private Dictionary<string, FactionStateBits> _runtimeStates;

        // ────────────────────────────────────────
        // 이벤트
        // ────────────────────────────────────────

        public event Action<FactionStateChangeEventArgs> OnFactionStateChanged;

        // ────────────────────────────────────────
        // Awake
        // ────────────────────────────────────────

        private void Awake()
        {
            // Singleton
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[WorldFactionStateManager] 중복 인스턴스 — 본 게임오브젝트 파괴");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // 초기화
            _definitionMap = new Dictionary<string, FactionDefinitionSO>();
            _runtimeStates = new Dictionary<string, FactionStateBits>();

            foreach (var def in _factionDefinitions)
            {
                if (def == null) continue;
                if (string.IsNullOrEmpty(def.FactionId))
                {
                    Debug.LogWarning($"[WorldFactionStateManager] FactionId 비어있음 — {def.name} 건너뜀");
                    continue;
                }
                if (_definitionMap.ContainsKey(def.FactionId))
                {
                    Debug.LogWarning($"[WorldFactionStateManager] factionId 중복: {def.FactionId} — 건너뜀");
                    continue;
                }
                _definitionMap[def.FactionId] = def;
                _runtimeStates[def.FactionId] = default;  // 0 비트 초기값
            }

            if (_verboseLog)
                Debug.Log($"[WorldFactionStateManager] {_definitionMap.Count} Faction 등록");

            // ── 어댑테이션 포인트: EventBus 구독
            // 사용자 측 EventBus 인프라가 있다면 다음 코드 활성화:
            // EventBus.OnFactionStateChanged += HandleMetaScenarioFactionUpdate;
            // (시그니처 확인 필요 — 가정: Action<string, FactionWorldState>)
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // ── 어댑테이션 포인트: EventBus 구독 해제
            // EventBus.OnFactionStateChanged -= HandleMetaScenarioFactionUpdate;
        }

        // ────────────────────────────────────────
        // MetaScenarioGenerator → 매니저 진입점
        // ────────────────────────────────────────

        /// <summary>
        /// MetaScenarioGenerator 또는 시드 결정 후 호출.
        /// FactionWorldState 를 받아 4 Tag 비트마스크로 변환 + Dictionary 갱신 + 이벤트 발행.
        /// 
        /// 호출 방법 2 가지:
        ///   ① 직접 호출 — MetaScenarioGenerator.GenerateFactionStates() 안에서
        ///   ② EventBus 콜백 — Awake 에서 EventBus 구독 (사용자 측 인프라)
        /// </summary>
        public void ApplyMetaScenarioUpdate(string factionId, FactionWorldState worldState)
        {
            _mapperApplyCount++;

            if (!_definitionMap.TryGetValue(factionId, out var def))
            {
                Debug.LogWarning(
                    $"[WorldFactionStateManager] {factionId} 의 FactionDefinitionSO 없음");
                return;
            }

            // Mapper 통한 4 Tag 변환 (DefaultMask + explicit 비트 처리)
            var newBits = FactionWorldStateMapper.ToTagBits(worldState, def);

            var oldBits = _runtimeStates.TryGetValue(factionId, out var b) ? b : default;
            _runtimeStates[factionId] = newBits;

            // 이벤트 발행
            RaiseStateChanged(factionId, oldBits, newBits);

            if (_verboseLog)
                Debug.Log($"[WorldFactionStateManager] {factionId} MetaScenarioUpdate " +
                          $"M=0x{newBits.moodBits:X8} T=0x{newBits.tacticalBits:X8} " +
                          $"L=0x{newBits.lifecycleBits:X8} R=0x{newBits.relationBits:X8}");
        }

        // ════════════════════════════════════════════════════════
        // IFactionStateProvider 구현
        // ════════════════════════════════════════════════════════

        public FactionStateSnapshot GetSnapshot(string factionId)
        {
            if (!_runtimeStates.TryGetValue(factionId, out var bits))
                return default;  // IsValid = false

            return new FactionStateSnapshot(factionId, bits, Time.time);
        }

        public bool HasFaction(string factionId) =>
            _runtimeStates.ContainsKey(factionId);

        public IReadOnlyList<string> GetAllFactionIds() =>
            _runtimeStates.Keys.ToList();

        // ════════════════════════════════════════════════════════
        // IFactionStateCommandReceiver — SetXxx (DefaultMask 검사)
        // ════════════════════════════════════════════════════════

        public bool SetMood(string factionId, FactionMoodTag tag, bool on)
        {
            _setCallCount++;
            _setCallsByCategory[CATEGORY_MOOD]++;

            if (!ValidateRequest(factionId, out var def, out var state)) return false;

            // ★ DefaultMask 검사 — AI 자율 변경은 디자이너 의도 보호
            if (def.DefaultMask != null && ((uint)tag & def.DefaultMask.ValidMoodMask) == 0u)
            {
                LogMaskViolation(factionId, "Mood", tag.ToString());
                return false;
            }

            ApplyMoodInternal(factionId, state, tag, on);
            return true;
        }

        public bool SetTactical(string factionId, FactionTacticalTag tag, bool on)
        {
            _setCallCount++;
            _setCallsByCategory[CATEGORY_TACTICAL]++;

            if (!ValidateRequest(factionId, out var def, out var state)) return false;

            if (def.DefaultMask != null && ((uint)tag & def.DefaultMask.ValidTacticalMask) == 0u)
            {
                LogMaskViolation(factionId, "Tactical", tag.ToString());
                return false;
            }

            ApplyTacticalInternal(factionId, state, tag, on);
            return true;
        }

        public bool SetLifecycle(string factionId, FactionLifecycleTag tag, bool on)
        {
            _setCallCount++;
            _setCallsByCategory[CATEGORY_LIFECYCLE]++;

            if (!ValidateRequest(factionId, out var def, out var state)) return false;

            if (def.DefaultMask != null && ((uint)tag & def.DefaultMask.ValidLifecycleMask) == 0u)
            {
                LogMaskViolation(factionId, "Lifecycle", tag.ToString());
                return false;
            }

            ApplyLifecycleInternal(factionId, state, tag, on);
            return true;
        }

        public bool SetRelation(string factionId, FactionRelationTag tag, bool on)
        {
            _setCallCount++;
            _setCallsByCategory[CATEGORY_RELATION]++;

            if (!ValidateRequest(factionId, out var def, out var state)) return false;

            if (def.DefaultMask != null && ((uint)tag & def.DefaultMask.ValidRelationMask) == 0u)
            {
                LogMaskViolation(factionId, "Relation", tag.ToString());
                return false;
            }

            ApplyRelationInternal(factionId, state, tag, on);
            return true;
        }

        // ════════════════════════════════════════════════════════
        // IFactionStateCommandReceiver — ForceXxx (DefaultMask 우회)
        // ════════════════════════════════════════════════════════

        public void ForceMood(string factionId, FactionMoodTag tag, bool on)
        {
            _forceCallCount++;
            _forceCallsByCategory[CATEGORY_MOOD]++;

            if (!ValidateRequest(factionId, out var def, out var state)) return;
            ApplyMoodInternal(factionId, state, tag, on);
        }

        public void ForceTactical(string factionId, FactionTacticalTag tag, bool on)
        {
            _forceCallCount++;
            _forceCallsByCategory[CATEGORY_TACTICAL]++;

            if (!ValidateRequest(factionId, out var def, out var state)) return;
            ApplyTacticalInternal(factionId, state, tag, on);
        }

        public void ForceLifecycle(string factionId, FactionLifecycleTag tag, bool on)
        {
            _forceCallCount++;
            _forceCallsByCategory[CATEGORY_LIFECYCLE]++;

            if (!ValidateRequest(factionId, out var def, out var state)) return;
            ApplyLifecycleInternal(factionId, state, tag, on);
        }

        public void ForceRelation(string factionId, FactionRelationTag tag, bool on)
        {
            _forceCallCount++;
            _forceCallsByCategory[CATEGORY_RELATION]++;

            if (!ValidateRequest(factionId, out var def, out var state)) return;
            ApplyRelationInternal(factionId, state, tag, on);
        }

        // ════════════════════════════════════════════════════════
        // 내부 헬퍼 — Apply
        // ════════════════════════════════════════════════════════

        private bool ValidateRequest(
            string factionId,
            out FactionDefinitionSO def,
            out FactionStateBits state)
        {
            def = null;
            state = default;

            if (string.IsNullOrEmpty(factionId)) return false;
            if (!_definitionMap.TryGetValue(factionId, out def)) return false;
            if (!_runtimeStates.TryGetValue(factionId, out state)) return false;

            return true;
        }

        private void ApplyMoodInternal(
            string factionId, FactionStateBits state,
            FactionMoodTag tag, bool on)
        {
            var oldBits = state;
            if (on) state.moodBits |= (uint)tag;
            else state.moodBits &= ~(uint)tag;

            if (state.moodBits == oldBits.moodBits) return;  // 변경 없음

            _runtimeStates[factionId] = state;
            RaiseStateChanged(factionId, oldBits, state);
        }

        private void ApplyTacticalInternal(
            string factionId, FactionStateBits state,
            FactionTacticalTag tag, bool on)
        {
            var oldBits = state;
            if (on) state.tacticalBits |= (uint)tag;
            else state.tacticalBits &= ~(uint)tag;

            if (state.tacticalBits == oldBits.tacticalBits) return;

            _runtimeStates[factionId] = state;
            RaiseStateChanged(factionId, oldBits, state);
        }

        private void ApplyLifecycleInternal(
            string factionId, FactionStateBits state,
            FactionLifecycleTag tag, bool on)
        {
            var oldBits = state;
            if (on) state.lifecycleBits |= (uint)tag;
            else state.lifecycleBits &= ~(uint)tag;

            if (state.lifecycleBits == oldBits.lifecycleBits) return;

            _runtimeStates[factionId] = state;
            RaiseStateChanged(factionId, oldBits, state);
        }

        private void ApplyRelationInternal(
            string factionId, FactionStateBits state,
            FactionRelationTag tag, bool on)
        {
            var oldBits = state;
            if (on) state.relationBits |= (uint)tag;
            else state.relationBits &= ~(uint)tag;

            if (state.relationBits == oldBits.relationBits) return;

            _runtimeStates[factionId] = state;
            RaiseStateChanged(factionId, oldBits, state);
        }

        // ────────────────────────────────────────
        // 이벤트 발행
        // ────────────────────────────────────────

        private void RaiseStateChanged(
            string factionId,
            FactionStateBits before,
            FactionStateBits after)
        {
            OnFactionStateChanged?.Invoke(new FactionStateChangeEventArgs
            {
                factionId = factionId,
                bitsBefore = before,
                bitsAfter = after,
                timestamp = Time.time
            });
        }

        // ────────────────────────────────────────
        // 로깅
        // ────────────────────────────────────────

        private void LogMaskViolation(string factionId, string category, string tag)
        {
            _maskViolationCount++;
            int idx = CategoryStringToIndex(category);
            if (idx >= 0) _maskViolationsByCategory[idx]++;

            Debug.LogWarning(
                $"[WorldFactionStateManager] {factionId} 는 {category}.{tag} 가질 수 없음 " +
                $"(DefaultMask 위반). 의도된 override 라면 Force{category} 사용.");
        }

        // ────────────────────────────────────────
        // 진단 (ContextMenu)
        // ────────────────────────────────────────

        [ContextMenu("Print Faction State Diagnostics")]
        private void PrintDiagnostics()
        {
            Debug.Log($"[WorldFactionStateManager] Registered {_runtimeStates?.Count ?? 0} Faction(s)");
            if (_runtimeStates == null) return;

            foreach (var kvp in _runtimeStates)
            {
                var b = kvp.Value;
                Debug.Log($"  {kvp.Key}: " +
                          $"M=0x{b.moodBits:X8} T=0x{b.tacticalBits:X8} " +
                          $"L=0x{b.lifecycleBits:X8} R=0x{b.relationBits:X8}");
            }
        }

        // ════════════════════════════════════════════════════════
        // ★ v1.2 — Diagnostics ContextMenu (E-03 / S2 신규)
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// 통계 출력 — Set / Force / MaskViolation / MapperApply 측 누적 + 카테고리별 분기.
        /// E-06 측 본격 FactionBridgeDiagnostics 측 진입 자료.
        /// </summary>
        [ContextMenu("Print Validation Stats")]
        public void PrintValidationStats()
        {
            Debug.Log(
                $"[WorldFactionStateManager] === Validation Stats ===\n" +
                $"  Set        : Total={_setCallCount}\n" +
                $"    Mood={_setCallsByCategory[CATEGORY_MOOD]} / " +
                $"Tactical={_setCallsByCategory[CATEGORY_TACTICAL]} / " +
                $"Lifecycle={_setCallsByCategory[CATEGORY_LIFECYCLE]} / " +
                $"Relation={_setCallsByCategory[CATEGORY_RELATION]}\n" +
                $"  Force      : Total={_forceCallCount}\n" +
                $"    Mood={_forceCallsByCategory[CATEGORY_MOOD]} / " +
                $"Tactical={_forceCallsByCategory[CATEGORY_TACTICAL]} / " +
                $"Lifecycle={_forceCallsByCategory[CATEGORY_LIFECYCLE]} / " +
                $"Relation={_forceCallsByCategory[CATEGORY_RELATION]}\n" +
                $"  MaskViolation: Total={_maskViolationCount}\n" +
                $"    Mood={_maskViolationsByCategory[CATEGORY_MOOD]} / " +
                $"Tactical={_maskViolationsByCategory[CATEGORY_TACTICAL]} / " +
                $"Lifecycle={_maskViolationsByCategory[CATEGORY_LIFECYCLE]} / " +
                $"Relation={_maskViolationsByCategory[CATEGORY_RELATION]}\n" +
                $"  MapperApply: Total={_mapperApplyCount}");
        }

        /// <summary>
        /// 통계 카운터 0 초기화 (총 + 카테고리별). 런타임 상태는 보존.
        /// </summary>
        [ContextMenu("Reset Stats")]
        public void ResetStats()
        {
            _setCallCount = 0;
            _forceCallCount = 0;
            _maskViolationCount = 0;
            _mapperApplyCount = 0;

            for (int i = 0; i < CATEGORY_COUNT; i++)
            {
                _setCallsByCategory[i] = 0;
                _forceCallsByCategory[i] = 0;
                _maskViolationsByCategory[i] = 0;
            }

            Debug.Log("[WorldFactionStateManager] Stats reset.");
        }

        /// <summary>
        /// 모든 Faction 측 런타임 상태 0 초기화 + OnFactionStateChanged 이벤트 발행.
        /// 디버그 / 테스트 측 reset 도구. 통계 카운터는 보존.
        /// </summary>
        [ContextMenu("Reset All State")]
        public void ResetAllState()
        {
            if (_runtimeStates == null) return;

            var ids = _runtimeStates.Keys.ToList();
            foreach (var id in ids)
            {
                var oldBits = _runtimeStates[id];
                if (oldBits.moodBits == 0u && oldBits.tacticalBits == 0u &&
                    oldBits.lifecycleBits == 0u && oldBits.relationBits == 0u)
                    continue;  // 이미 0 — 이벤트 발행 회피

                _runtimeStates[id] = default;
                RaiseStateChanged(id, oldBits, default);
            }

            Debug.Log($"[WorldFactionStateManager] All {ids.Count} Faction(s) state reset.");
        }

        // ────────────────────────────────────────
        // 카테고리 인덱스 변환 헬퍼 (LogMaskViolation 측 사용)
        // ────────────────────────────────────────

        private static int CategoryStringToIndex(string category)
        {
            switch (category)
            {
                case "Mood": return CATEGORY_MOOD;
                case "Tactical": return CATEGORY_TACTICAL;
                case "Lifecycle": return CATEGORY_LIFECYCLE;
                case "Relation": return CATEGORY_RELATION;
                default: return -1;
            }
        }
    }
}