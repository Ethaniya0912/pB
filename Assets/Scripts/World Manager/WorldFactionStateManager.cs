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
        [SerializeField] private List<FactionDefinitionSO> _factionDefinitions =
            new List<FactionDefinitionSO>();
        
        [Header("━━━ 디버그 ━━━")]
        
        [SerializeField] private bool _verboseLog = false;
        
        // ────────────────────────────────────────
        // 런타임 상태 (private)
        // ────────────────────────────────────────
        
        private Dictionary<string, FactionDefinitionSO> _definitionMap;
        private Dictionary<string, FactionStateBits>     _runtimeStates;
        
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
            if (!ValidateRequest(factionId, out var def, out var state)) return;
            ApplyMoodInternal(factionId, state, tag, on);
        }
        
        public void ForceTactical(string factionId, FactionTacticalTag tag, bool on)
        {
            if (!ValidateRequest(factionId, out var def, out var state)) return;
            ApplyTacticalInternal(factionId, state, tag, on);
        }
        
        public void ForceLifecycle(string factionId, FactionLifecycleTag tag, bool on)
        {
            if (!ValidateRequest(factionId, out var def, out var state)) return;
            ApplyLifecycleInternal(factionId, state, tag, on);
        }
        
        public void ForceRelation(string factionId, FactionRelationTag tag, bool on)
        {
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
            if (on) state.moodBits |=  (uint)tag;
            else    state.moodBits &= ~(uint)tag;
            
            if (state.moodBits == oldBits.moodBits) return;  // 변경 없음
            
            _runtimeStates[factionId] = state;
            RaiseStateChanged(factionId, oldBits, state);
        }
        
        private void ApplyTacticalInternal(
            string factionId, FactionStateBits state,
            FactionTacticalTag tag, bool on)
        {
            var oldBits = state;
            if (on) state.tacticalBits |=  (uint)tag;
            else    state.tacticalBits &= ~(uint)tag;
            
            if (state.tacticalBits == oldBits.tacticalBits) return;
            
            _runtimeStates[factionId] = state;
            RaiseStateChanged(factionId, oldBits, state);
        }
        
        private void ApplyLifecycleInternal(
            string factionId, FactionStateBits state,
            FactionLifecycleTag tag, bool on)
        {
            var oldBits = state;
            if (on) state.lifecycleBits |=  (uint)tag;
            else    state.lifecycleBits &= ~(uint)tag;
            
            if (state.lifecycleBits == oldBits.lifecycleBits) return;
            
            _runtimeStates[factionId] = state;
            RaiseStateChanged(factionId, oldBits, state);
        }
        
        private void ApplyRelationInternal(
            string factionId, FactionStateBits state,
            FactionRelationTag tag, bool on)
        {
            var oldBits = state;
            if (on) state.relationBits |=  (uint)tag;
            else    state.relationBits &= ~(uint)tag;
            
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
                factionId  = factionId,
                bitsBefore = before,
                bitsAfter  = after,
                timestamp  = Time.time
            });
        }
        
        // ────────────────────────────────────────
        // 로깅
        // ────────────────────────────────────────
        
        private void LogMaskViolation(string factionId, string category, string tag)
        {
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
    }
}
