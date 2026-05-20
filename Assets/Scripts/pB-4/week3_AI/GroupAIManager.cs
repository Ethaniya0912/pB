// =============================================================================
// GroupAIManager.cs  |  pB-4 Project — Week 3 / Wk5 A12 통합 완성본
// Layer  : L2 Router (AI)
// Namespace: TDA.PB4.AI
//
// [v3.13 Wk5 B-3 — 2026-05-14] IGroupCommandReceiver 본체 구현 (★ B-3 의제)
//   ★ 인터페이스 추가: : MonoBehaviour, IGroupAIInfo, IGroupDashboard, IGroupCommandReceiver
//   ★ 본 파일 내 region 5 종 신규 (파일 말미, 총 약 360 줄):
//     - region B-3 Fields           — _activeCommands 리스트 + 컬러 상수
//     - region B-3 Interface        — IssueCommand / CancelCommand / GetActiveCommands 3 메서드
//     - region B-3 Command Handlers — 6 종 CommandType 분기 (FocusFire/SplitFire/Disengage/Mark/Cover/Lure)
//     - region B-3 Expiration       — expiresAt 만료 명령 lazy 자동 정리
//     - region B-3 ContextMenu      — Debug 도구 4 종 ([B-3] Test FocusFire / Test Disengage / Print Active / Cancel All)
//   ★ using System.Linq 추가 (SelectTargetMembers(cmd).Count() 용)
//   ★ partial 키워드 유지 — GroupAIManager.Debug.cs 짝 보존
//   ★ ★ Hotfix1: 이름 충돌 해결 — TDA.PB4.AI.CommandType (CommandDirective Wk5_AI / 4 종) 와
//     TDA.PB4.Interfaces.CommandType (B-3 / 6 종) 충돌. GroupAIManager 가 TDA.PB4.AI 안에
//     있어 동일 namespace 우선 해결로 CommandDirective 측 enum 이 선택됨 → CS0117 발생.
//     해법: using BridgeCommandType = TDA.PB4.Interfaces.CommandType; alias 1 줄 추가 +
//     B-3 region 안 8 곳 (switch case 6 + ContextMenu 2) 을 BridgeCommandType 으로 치환.
//   ★ Handover docx v1 의 Patch 2 (using TDA.PB4.Bridges) 는 적용 안 함
//     사유: 실제 namespace = TDA.PB4.Interfaces (line 58 already 존재)
//
// [v3.12 Wk5 B-2 Hotfix2 — 2026-05-14] V-Wk4-04 검증 ContextMenu 추가
//   ★ 신규 ContextMenu: ★ [V-Wk4-04] Compute Fear Modifier (Trust × Trauma)
//     모든 멤버 ComputeFearModifier(1.0, trust.CurrentTrust) 호출 + 결과 출력
//     개론서 §5.4.6 #4 통과 기준: Trust 90 + AcuteShock → fear 0.85 (trustBonus 0.45)
//
// [v3.11 Wk5 B-2 Hotfix — 2026-05-14] 컴포넌트 탐색 3 단계 fallback + 진단 ContextMenu
//   ★ TrustMatrix / TraumaSystem / AbandonRescueResolver 탐색 패턴 변경:
//     GetComponent → GetComponent ?? GetComponentInParent ?? GetComponentInChildren
//     사유: 사용자 측 prefab 본격 분리 부착 가능성 (root vs 자식 GameObject)
//     영향: 5 곳 (3 헬퍼 + Subscribe/Unsubscribe)
//   ★ 신규 ContextMenu: ★ [A12-Tracking] Diagnose Member Components
//     각 멤버의 Self / Parent / Children 3 위치 부착 여부 진단
//
// [v3.10 Wk5 B-2 — 2026-05-14] A12 Tracking 영역 통합 + 그룹 B 4 stub override
//   ★ 이전 별도 partial 파일 (GroupAIManager.A12Tracking.cs) 본체 통합
//   ★ partial 키워드는 유지 (GroupAIManager.Debug.cs 짝 보존)
//   ★ Wk4 컴포넌트 연동 4 stub:
//     - [#5]  AverageTrust_A12       = TrustMatrix.CurrentTrust (0~100) 평균
//     - [#6]  DoubtMemberCount_A12   = TrustMatrix.CurrentTier == Doubt 카운트
//     - [#10] TraumatizedRatio_A12   = TraumaSystem.CurrentStageIndex > 0 비율
//     - [#11] AbandonEventCount_A12  = AbandonRescueResolver.OnSituationResolved 구독
//   ★ RegisterMember / UnregisterMember 안 AbandonResolver 동적 구독 / 해제
//
// [v3.9 Wk5 B-1 — 2026-05-14] A12 Tracking 영역 (별도 partial 도입 후 v3.10 본체 통합)
//   ★ 그룹 A 자체 추적 3 stub:
//     - [#3] ConsecutiveDeaths_A12  — 5 초 윈도우 안 연속 사망 카운터
//     - [#8] EnemyAdvantage_A12     — _knownTargets / 살아있는 멤버 비율
//     - [#9] RetreatCount_A12       — OnTacticalBehaviorChanged QuickRetreat/StrategicWithdraw 누적
//
// [v3.6 A12 완성 + SetFactionDefinition — 2026-05-12]
//   ★ A12 영역 본체 통합 (partial 폐기 / 단일 클래스):
//     - _factionConfig (FactionThresholdConfigSO) 필드
//     - 11 protected virtual property (Wk5 본격 본체 override)
//     - CheckFactionThresholds() + CheckFactionOne()
//     - OnFactionThresholdsViolated / OnTacticalBehaviorChanged 이벤트
//     - 2 ContextMenu ([A12] Print Faction Thresholds / [A12] Force Test Violations)
//   ★ FactionDefinitionSO 통합 (Wk5 본격 진입):
//     - SetFactionDefinition(FactionDefinitionSO, MonoBehaviour) — 단일 호출로 정책 + 임계 자동
//     - SetFactionConfig(FactionThresholdConfigSO) — 임계만 부착 (fallback)
//   ★ partial 키워드 제거 — 단일 클래스
//   ★ 이름 충돌 회피 — Faction* prefix (본체 A7 GroupThresholdType 와 별개):
//     - FactionThresholdType (11 종) / FactionThresholdViolation
//     - OnFactionThresholdsViolated / GetActiveFactionViolations()
//     - [A12] Print Faction Thresholds
// =============================================================================
using System;
using System.Collections.Generic;
using System.Linq;               // ★ v3.13 B-3 — SelectTargetMembers(cmd).Count() 용
using System.Text;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Data;              // ★ v3.6 — FactionDefinitionSO
using TDA.PB4.AI.Mob;
using TDA.PB4.AI.Humanoid;      // ★ v3.7 — HumanoidAIBrain (type check 용)
using TDA.PB4.Interfaces;        // IGroupAIInfo
using BridgeCommandType = TDA.PB4.Interfaces.CommandType;  // ★ v3.13 B-3 — TDA.PB4.AI.CommandType (CommandDirective Wk5) 와 이름 충돌 회피
using TDA.PB4.AI.Social;         // ★ Wk5 B-2 — AbandonRescueResolver / AbandonRescueOutcome (자식 namespace)
using TDA.Character.AI;          // [v3.3.7] AICharacterManager — 굳음 진단용
using UnityEngine.SceneManagement;  // ★ A7 — GetCurrentSceneId
namespace TDA.PB4.AI
{
    /// <summary>
    /// 개별 몹의 GroupAI 상태를 추적하는 데이터.
    /// </summary>
    [Serializable]
    public class GroupMemberState
    {
        [Tooltip("이 몹의 MobAIBrain 참조")]
        public BaseAIBrain brain;   // ★ v3.7 — MobAIBrain → BaseAIBrain (Humanoid + Mob 공통)
        [Tooltip("현재 할당된 역할.")]
        public GroupRole currentRole = GroupRole.Stalker;
        [Tooltip("[표시 전용/편집 금지] 공격 토큰 보유 여부.")]
        public bool hasAttackToken = false;
        [Tooltip("[표시 전용/편집 금지] 마지막 공격 이후 경과 시간(초).")]
        public float timeSinceLastAttack = 0f;
        [Tooltip("[표시 전용/진단용] 공격 누적 횟수.")]
        public int attackCount = 0;
    }
    public enum GroupRole { Stalker, Disruptor, Striker }
    public partial class GroupAIManager : MonoBehaviour, IGroupAIInfo, IGroupDashboard, IGroupCommandReceiver  // ★ v3.13 B-3 — IGroupCommandReceiver 추가
    {
        // ═══════════════════════════════════════════════════════════════════
        // ★ A7 IGroupDashboard — 필드
        // ═══════════════════════════════════════════════════════════════════

        [Header("━━━ Dashboard (A7) ━━━━━━━━━━━━━━━━━")]
        [SerializeField] private int sequenceNum = 0;
        [SerializeField] private int maxPopulationFallback = 10;
        private int reinforcementCount = 0;
        private int casualtyCount = 0;
        private int defeatsAtCurrentLocation = 0;
        private Vector3? lastSafePosition = null;
        private readonly Dictionary<GroupThresholdType, GroupThresholdState> _activeThresholds
            = new Dictionary<GroupThresholdType, GroupThresholdState>();
        private float _lastThresholdCheckTime = 0f;
        private const float THRESHOLD_CHECK_INTERVAL = 0.5f;

        // ═══════════════════════════════════════════════════════════════════
        // ★ A12 Faction Threshold (v3.6 통합)
        // ═══════════════════════════════════════════════════════════════════

        [Header("━━━ Faction Threshold (A12) ━━━━━━━━━━━━━")]

        [Tooltip("펙션 별 11 임계 SO. WorldAISpawnManager 가 SetFactionDefinition 으로 자동 부착 권장. " +
                 "수동 설정 시 Inspector 드래그.")]
        [SerializeField] private FactionThresholdConfigSO _factionConfig;

        [Tooltip("[A12] 디버그 로그 — 임계 검사 시 출력.")]
        [SerializeField] private bool _a12DebugLog = false;

        private readonly List<FactionThresholdViolation> _activeFactionViolations
            = new List<FactionThresholdViolation>();
        private FactionTacticalBehavior _currentTacticalBehavior = FactionTacticalBehavior.Hold;

        // ═══════════════════════════════════════════════════════════════════

        [Header("━━━ 팩션 정책 ━━━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private FactionGroupPolicySO policySO;
        [SerializeField] private MonoBehaviour policyImplementor;
        [Header("━━━ 그룹 멤버 ━━━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private List<GroupMemberState> members = new List<GroupMemberState>();
        [Header("━━━ 공격 토큰 시스템 ━━━━━━━━━━━━━━━━━")]
        [Range(0.1f, 3f)] public float tokenMultiplier = 1.2f;
        [Range(0.5f, 5f)] public float tokenReassignInterval = 2.0f;
        [Header("━━━ 사기/에스컬레이션 (런타임 자동 갱신) ━━")]
        [Range(0f, 1f)][SerializeField] private float currentMorale = 1.0f;
        [SerializeField] private int currentEscalationLevel = 0;
        [SerializeField] private float combatElapsedTime = 0f;
        [Header("━━━ 포위 진척도 (Phalanx용) ━━━━━━━━━━")]
        [Range(0f, 1f)] public float encircleProgress = 0f;
        [Header("━━━ Shared Target (§4 L2) ━━━━━━")]
        [SerializeField] private List<TargetSlot> _knownTargets = new();
        private const float DEFAULT_SHARED_CLOSE_RADIUS = 15f;
        private const float DEFAULT_SHARED_CHAIN_MAX = 50f;
        private const float DEFAULT_SHARED_TIMEOUT = 8f;

        [Serializable]
        public class TargetSlot
        {
            public Transform target;
            public Vector3 lastKnownPosition;
            public float firstSpotTime;
            public float lastSeenTime;
            public MobAIBrain firstSpotter;
            public List<MobAIBrain> awareMembers = new();
        }

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = false;
        public bool verboseTokenLog = false;
        public bool logFrameTimers = false;

        [Header("━━━ Faction Safe Zone (§5 L1) ━━━━━━")]
        [SerializeField] private Vector3 factionSafeZone;
        [SerializeField] private bool hasFactionSafeZone = false;
        public Vector3 FactionSafeZone => factionSafeZone;
        public bool HasFactionSafeZone => hasFactionSafeZone;

        public void SetFactionSafeZone(Vector3 pos)
        {
            factionSafeZone = pos;
            hasFactionSafeZone = true;
            PushEvent("Lifecycle", $"FactionSafeZone 설정: ({pos.x:F1}, {pos.y:F1}, {pos.z:F1})", EVT_LIFECYCLE);
        }

        [Header("━━━ Editor Auto Focus ━━━━━━━━━━")]
        public FocusCameraMode focusMode = FocusCameraMode.None;
        [SerializeField] private BaseAIBrain lastTokenWinner;
        public BaseAIBrain LastTokenWinner => lastTokenWinner;

        public enum FocusCameraMode
        {
            [UnityEngine.InspectorName("None")] None,
            [UnityEngine.InspectorName("1: AI 정면")] AIFront,
            [UnityEngine.InspectorName("2: AI+타겟 탑뷰")] AIPlusTargetTop,
            [UnityEngine.InspectorName("3: AI 숄더 (거리 고정)")] AIShoulderFixed,
            [UnityEngine.InspectorName("4: 그룹 탑뷰")] GroupTop,
            [UnityEngine.InspectorName("5: 그룹 숄더 → 타겟")] GroupShoulderToTarget,
        }

        [SerializeField] private int maxTokenCount = 0;
        private Interfaces.Intelligence.IFactionGroupPolicy policy;
        private float tokenTimer = 0f;
        private float moraleTimer = 0f;

        // 로그 색상
        private const string COL_TOKEN = "#FFD54F";
        private const string COL_ROLE = "#4DD0E1";
        private const string COL_MORALE = "#FF8A65";
        private const string COL_ESCAL = "#BA68C8";
        private const string COL_MEMBER = "#81C784";
        private const string COL_ATTACK = "#FFB74D";
        private const string COL_LIFECYCLE = "#90A4AE";
        private const string COL_A12 = "#FF6677";   // ★ A12 — 빨강

        // ==================================================================
        // ★ v3.2 IGroupAIInfo 구현
        // ==================================================================
        public float GetMorale() => currentMorale;
        public bool HasAvailableToken() => activeTokenCount > 0;

        // ═══════════════════════════════════════════════════════════════════
        // ★ A7 IGroupDashboard 구현 (12 메서드)
        // ═══════════════════════════════════════════════════════════════════

        #region IGroupDashboard — Identity / Population / State / Location

        public string GetGroupId() => gameObject.name;
        public string GetFactionId() => policySO != null ? policySO.factionId : "";
        public int GetSequenceNum() => sequenceNum;

        public int GetPopulation()
        {
            int alive = 0;
            for (int i = 0; i < members.Count; i++)
                if (members[i] != null && members[i].brain != null) alive++;
            return alive;
        }

        public int GetMaxPopulation() => maxPopulationFallback;
        public int GetReinforcementCount() => reinforcementCount;
        public int GetCasualtyCount() => casualtyCount;
        public int GetEscalationLevel() => currentEscalationLevel;
        public float GetCombatElapsedTime() => combatElapsedTime;

        public Vector3 GetCenterPosition()
        {
            int alive = 0;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m != null && m.brain != null)
                {
                    sum += m.brain.transform.position;
                    alive++;
                }
            }
            return alive > 0 ? sum / alive : transform.position;
        }

        public string GetCurrentSceneId() => SceneManager.GetActiveScene().name;
        public int GetCurrentCaveNodeIdx() => -1;

        public float GetTerritoryRadius()
        {
            int alive = GetPopulation();
            if (alive <= 1) return 0f;
            Vector3 center = GetCenterPosition();
            float maxSqDist = 0f;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m != null && m.brain != null)
                {
                    float sq = (m.brain.transform.position - center).sqrMagnitude;
                    if (sq > maxSqDist) maxSqDist = sq;
                }
            }
            return Mathf.Sqrt(maxSqDist);
        }

        #endregion

        #region IGroupDashboard — Needs / Learning / Thresholds

        public float GetHungerLevel() => 0f;
        public float GetReproductionUrge() => 0f;
        public float GetTerritoryPressure() => 0f;
        public int GetDefeatsAtCurrentLocation() => defeatsAtCurrentLocation;
        public Vector3? GetLastSafePosition() => lastSafePosition;

        public event Action<GroupThresholdEvent> OnThresholdCrossed;

        public GroupThresholdState[] GetActiveThresholds()
        {
            var result = new GroupThresholdState[_activeThresholds.Count];
            int i = 0;
            foreach (var kv in _activeThresholds) result[i++] = kv.Value;
            return result;
        }

        private void CheckAllThresholds()
        {
            CheckMoraleThreshold();
            CheckPopulationThresholds();
            CheckFactionThresholds();   // ★ v3.5 A12 통합
            // TODO Wk5+ — 나머지 7 종 (Territory / Leader / Split / Merge / Reproduction / Starvation / PopulationOverflow)
        }

        private void CheckMoraleThreshold()
        {
            const float MORALE_COLLAPSE = 0.30f;
            const float MORALE_RECOVERED = 0.60f;
            bool isLow = currentMorale < MORALE_COLLAPSE;
            bool wasActive = _activeThresholds.ContainsKey(GroupThresholdType.MoraleCollapse);

            if (isLow && !wasActive)
            {
                _activeThresholds[GroupThresholdType.MoraleCollapse] = new GroupThresholdState
                {
                    type = GroupThresholdType.MoraleCollapse,
                    isActive = true,
                    enteredAt = Time.time,
                    currentValue = currentMorale
                };
                FireThresholdEvent(GroupThresholdType.MoraleCollapse, currentMorale, MORALE_COLLAPSE);
            }
            else if (currentMorale >= MORALE_RECOVERED && wasActive)
            {
                _activeThresholds.Remove(GroupThresholdType.MoraleCollapse);
                FireThresholdEvent(GroupThresholdType.MoraleRecovered, currentMorale, MORALE_RECOVERED);
            }
        }

        private void CheckPopulationThresholds()
        {
            int pop = GetPopulation();
            int max = GetMaxPopulation();

            bool isWiped = pop == 0;
            bool wasWiped = _activeThresholds.ContainsKey(GroupThresholdType.GroupWiped);
            if (isWiped && !wasWiped && reinforcementCount > 0)
            {
                _activeThresholds[GroupThresholdType.GroupWiped] = new GroupThresholdState
                {
                    type = GroupThresholdType.GroupWiped,
                    isActive = true,
                    enteredAt = Time.time,
                    currentValue = 0f
                };
                FireThresholdEvent(GroupThresholdType.GroupWiped, 0f, 0f);
                return;
            }

            bool isCritical = pop > 0 && (float)pop / max < 0.2f;
            bool wasCritical = _activeThresholds.ContainsKey(GroupThresholdType.PopulationCritical);

            if (isCritical && !wasCritical)
            {
                _activeThresholds[GroupThresholdType.PopulationCritical] = new GroupThresholdState
                {
                    type = GroupThresholdType.PopulationCritical,
                    isActive = true,
                    enteredAt = Time.time,
                    currentValue = pop
                };
                FireThresholdEvent(GroupThresholdType.PopulationCritical, pop, max * 0.2f);
            }
            else if (!isCritical && wasCritical)
            {
                _activeThresholds.Remove(GroupThresholdType.PopulationCritical);
            }
        }

        private void FireThresholdEvent(GroupThresholdType type, float value, float threshold)
        {
            var evt = new GroupThresholdEvent
            {
                groupId = GetGroupId(),
                type = type,
                gameTime = Time.time,
                value = value,
                threshold = threshold,
                position = GetCenterPosition(),
                caveNodeIdx = GetCurrentCaveNodeIdx(),
            };
            OnThresholdCrossed?.Invoke(evt);
            Log(COL_MORALE, "A7-Threshold", $"{type} — value={value:F2} threshold={threshold:F2}");
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════════
        // ★ A12 Faction Threshold 영역 (v3.6 본체 통합)
        // ═══════════════════════════════════════════════════════════════════

        #region A12 — Faction Threshold System

        /// <summary>[A12] 11 펙션 임계 위반 발생 시 — 위반 목록 전달.</summary>
        public event Action<IReadOnlyList<FactionThresholdViolation>> OnFactionThresholdsViolated;

        /// <summary>[A12] 펙션 전술 행동 변경 시 — (old, new).</summary>
        public event Action<FactionTacticalBehavior, FactionTacticalBehavior> OnTacticalBehaviorChanged;

        /// <summary>[A12] 현재 활성 펙션 위반 목록 (read-only).</summary>
        public IReadOnlyList<FactionThresholdViolation> GetActiveFactionViolations()
            => _activeFactionViolations;

        /// <summary>[A12] 현재 전술 행동.</summary>
        public FactionTacticalBehavior CurrentTacticalBehavior => _currentTacticalBehavior;

        /// <summary>[A12] 펙션 ID (FactionThresholdConfigSO 의 factionID).</summary>
        public string FactionID_A12 => _factionConfig != null ? _factionConfig.factionID : "Unknown";

        /// <summary>[A12] 임계 SO 직접 접근 (read-only).</summary>
        public FactionThresholdConfigSO FactionConfig => _factionConfig;

        /// <summary>
        /// [A12] 11 펙션 임계 검사 — CheckAllThresholds() 안에서 자동 호출 (0.5초 주기).
        /// </summary>
        public void CheckFactionThresholds()
        {
            if (_factionConfig == null)
            {
                if (_a12DebugLog)
                    LogW($"[A12] _factionConfig 없음 — 펙션 임계 검사 스킵");
                return;
            }

            _activeFactionViolations.Clear();

            CheckFactionOne(FactionThresholdType.MinPopulation, CurrentPopulation_A12);
            CheckFactionOne(FactionThresholdType.MaxCasualties, TotalCasualties_A12);
            CheckFactionOne(FactionThresholdType.MaxConsecutiveDeaths, ConsecutiveDeaths_A12);
            CheckFactionOne(FactionThresholdType.MinAverageMorale, AverageMorale_A12);
            CheckFactionOne(FactionThresholdType.MinAverageTrust, AverageTrust_A12);
            CheckFactionOne(FactionThresholdType.MaxDoubtCount, DoubtMemberCount_A12);
            CheckFactionOne(FactionThresholdType.MinTerritoryControl, TerritoryControl_A12);
            CheckFactionOne(FactionThresholdType.MaxEnemyAdvantage, EnemyAdvantage_A12);
            CheckFactionOne(FactionThresholdType.MaxRetreatCount, RetreatCount_A12);
            CheckFactionOne(FactionThresholdType.MaxTraumatizedRatio, TraumatizedRatio_A12);
            CheckFactionOne(FactionThresholdType.MaxAbandonCount, AbandonEventCount_A12);

            if (_activeFactionViolations.Count > 0)
            {
                var oldBehavior = _currentTacticalBehavior;
                _currentTacticalBehavior = _factionConfig.DetermineBehavior(_activeFactionViolations);

                if (_a12DebugLog)
                {
                    Log(COL_A12, "A12-Threshold",
                        $"펙션 임계 위반 <b>{_activeFactionViolations.Count}</b> 건 → 전술 <b>{_currentTacticalBehavior}</b>");
                    foreach (var v in _activeFactionViolations)
                        Log(COL_A12, "A12-Detail", $"  · {v}");
                }

                OnFactionThresholdsViolated?.Invoke(_activeFactionViolations);

                if (oldBehavior != _currentTacticalBehavior)
                {
                    OnTacticalBehaviorChanged?.Invoke(oldBehavior, _currentTacticalBehavior);
                    PushEvent("Combat",
                        $"전술 행동 변경: {oldBehavior} → {_currentTacticalBehavior}",
                        EVT_COMBAT);
                }
            }
        }

        /// <summary>[A12] 1 종 펙션 임계 검사 헬퍼.</summary>
        private void CheckFactionOne(FactionThresholdType type, float currentValue)
        {
            if (!_factionConfig.IsEnabled(type)) return;

            if (_factionConfig.IsViolation(type, currentValue))
            {
                _activeFactionViolations.Add(new FactionThresholdViolation(
                    type, currentValue, _factionConfig.GetThresholdValue(type)));
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // [A12] 11 protected virtual property — Wk5 본격 본체 override
        // ═══════════════════════════════════════════════════════════════════
        // 일부는 즉시 본체 데이터 위임 / 일부는 Wk5 본격 진입 시 override.

        /// <summary>[A12] 현재 인구 수 — 본체 GetPopulation() 위임.</summary>
        protected virtual int CurrentPopulation_A12 => GetPopulation();

        /// <summary>[A12] 누적 전사 — 본체 GetCasualtyCount() 위임.</summary>
        protected virtual int TotalCasualties_A12 => GetCasualtyCount();

        /// <summary>[A12] 연속 전사 수 — Wk5 B-1 A12Tracking partial 위임.</summary>
        protected virtual int ConsecutiveDeaths_A12 => GetConsecutiveDeaths_A12();

        /// <summary>[A12] 평균 사기 — 본체 currentMorale 위임.</summary>
        protected virtual float AverageMorale_A12 => currentMorale;

        /// <summary>[A12] 평균 Trust — Wk5 B-2 TrustMatrix 연동 (CurrentTrust 0~100 평균).</summary>
        protected virtual float AverageTrust_A12 => GetAverageTrust_A12();

        /// <summary>[A12] Doubt 단계 멤버 수 — Wk5 B-2 TrustTier == Doubt 카운트.</summary>
        protected virtual int DoubtMemberCount_A12 => GetDoubtMemberCount_A12();

        /// <summary>[A12] 영토 통제율 — Wk5 P-07 연동 (현재 stub).</summary>
        protected virtual float TerritoryControl_A12 => 1.0f;

        /// <summary>[A12] 적 우세 비율 — Wk5 B-1 A12Tracking partial 위임.</summary>
        protected virtual float EnemyAdvantage_A12 => GetEnemyAdvantage_A12();

        /// <summary>[A12] 후퇴 누적 수 — Wk5 B-1 A12Tracking partial 위임.</summary>
        protected virtual int RetreatCount_A12 => GetRetreatCount_A12();

        /// <summary>[A12] 트라우마 멤버 비율 — Wk5 B-2 TraumaSystem.CurrentStageIndex 본격 비율.</summary>
        protected virtual float TraumatizedRatio_A12 => GetTraumatizedRatio_A12();

        /// <summary>[A12] 유기 사건 누적 — Wk5 B-2 AbandonRescueResolver.OnSituationResolved 구독.</summary>
        protected virtual int AbandonEventCount_A12 => GetAbandonEventCount_A12();

        #endregion

        // ═══════════════════════════════════════════════════════════════════
        // ★ A7 ContextMenu 진단
        // ═══════════════════════════════════════════════════════════════════

        #region IGroupDashboard — Debug ContextMenu

        [ContextMenu("[A7] Print Dashboard Snapshot")]
        private void PrintDashboardSnapshot()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("═══ Dashboard Snapshot ═══");
            sb.AppendLine($"  GroupId            = {GetGroupId()}");
            sb.AppendLine($"  FactionId          = {GetFactionId()}");
            sb.AppendLine($"  SequenceNum        = {GetSequenceNum()}");
            sb.AppendLine($"  Population         = {GetPopulation()} / {GetMaxPopulation()}");
            sb.AppendLine($"  Reinforcement      = {GetReinforcementCount()}");
            sb.AppendLine($"  Casualty           = {GetCasualtyCount()}");
            sb.AppendLine($"  Morale             = {GetMorale():F2}");
            sb.AppendLine($"  EscalationLevel    = {GetEscalationLevel()}");
            sb.AppendLine($"  CombatElapsedTime  = {GetCombatElapsedTime():F1}s");
            sb.AppendLine($"  CenterPosition     = {GetCenterPosition()}");
            sb.AppendLine($"  CaveNodeIdx        = {GetCurrentCaveNodeIdx()}");
            sb.AppendLine($"  TerritoryRadius    = {GetTerritoryRadius():F1}");
            sb.AppendLine($"  SceneId            = {GetCurrentSceneId()}");
            sb.AppendLine($"  Defeats@Location   = {GetDefeatsAtCurrentLocation()}");
            sb.AppendLine($"  LastSafePosition   = {(GetLastSafePosition().HasValue ? GetLastSafePosition().Value.ToString() : "<null>")}");
            sb.AppendLine($"  Active Thresholds  = {_activeThresholds.Count}");
            foreach (var kv in _activeThresholds)
                sb.AppendLine($"    - {kv.Key}: value={kv.Value.currentValue:F2} enteredAt={kv.Value.enteredAt:F1}s");
            Debug.Log(sb.ToString(), this);
        }

        [ContextMenu("[A7] Trigger Morale Collapse (Test)")]
        private void TriggerMoraleCollapseForTest()
        {
            currentMorale = 0.15f;
            Log(COL_MORALE, "A7-Test", "currentMorale = 0.15 — 다음 임계 검사 시 MoraleCollapse 발행 예정");
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════════
        // ★ A12 ContextMenu 진단 (v3.6)
        // ═══════════════════════════════════════════════════════════════════

        #region A12 — Debug ContextMenu

        [ContextMenu("[A12] Print Faction Thresholds")]
        private void DebugPrintFactionThresholds()
        {
            if (_factionConfig == null)
            {
                Debug.LogWarning($"[A12] {name}: _factionConfig 없음", this);
                return;
            }

            CheckFactionThresholds();

            int enabledCount = 0;
            string enabledList = "";
            foreach (FactionThresholdType t in System.Enum.GetValues(typeof(FactionThresholdType)))
            {
                if (_factionConfig.IsEnabled(t))
                {
                    enabledCount++;
                    enabledList += (enabledList.Length > 0 ? ", " : "") + t;
                }
            }

            string violationsText = _activeFactionViolations.Count > 0
                ? string.Join("\n  ", _activeFactionViolations.ConvertAll(v => v.ToString()))
                : "<없음>";

            Debug.Log($"═══ A12 Faction Threshold State ═══\n" +
                      $"  Group              = {name}\n" +
                      $"  Faction            = {_factionConfig.factionID} ({_factionConfig.factionName})\n" +
                      $"  Enabled Thresholds = {enabledCount}/11\n" +
                      $"    [{enabledList}]\n" +
                      $"  Active Violations  = {_activeFactionViolations.Count}\n" +
                      $"  {violationsText}\n" +
                      $"  Tactical Behavior  = {_currentTacticalBehavior}", this);
        }

        [ContextMenu("[A12] Force Test Violations")]
        private void DebugForceFactionViolations()
        {
            if (_factionConfig == null)
            {
                Debug.LogWarning($"[A12] {name}: _factionConfig 없음", this);
                return;
            }

            _activeFactionViolations.Clear();
            _activeFactionViolations.Add(new FactionThresholdViolation(
                FactionThresholdType.MinPopulation, 5f, _factionConfig.minPopulation));
            _activeFactionViolations.Add(new FactionThresholdViolation(
                FactionThresholdType.MaxCasualties, 30f, _factionConfig.maxCasualties));

            var oldBehavior = _currentTacticalBehavior;
            _currentTacticalBehavior = _factionConfig.DetermineBehavior(_activeFactionViolations);

            OnFactionThresholdsViolated?.Invoke(_activeFactionViolations);
            if (oldBehavior != _currentTacticalBehavior)
                OnTacticalBehaviorChanged?.Invoke(oldBehavior, _currentTacticalBehavior);

            Debug.Log($"[A12 Sim] {name}: 강제 위반 2 건 → 전술 <b>{_currentTacticalBehavior}</b>", this);
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════════
        // ★ A12 Tracking — Wk5 B-1 + B-2 통합 본체 영역
        // ═══════════════════════════════════════════════════════════════════
        //
        // B-1 (그룹 A — 본체 자체 추적 3 종):
        //   [#3] ConsecutiveDeaths_A12  — 5 초 윈도우 안 연속 사망 카운터
        //   [#8] EnemyAdvantage_A12     — _knownTargets / 살아있는 멤버 비율
        //   [#9] RetreatCount_A12       — OnTacticalBehaviorChanged 구독 누적
        //
        // B-2 (그룹 B — Wk4 컴포넌트 연동 4 종):
        //   [#5]  AverageTrust_A12      — TrustMatrix.CurrentTrust (0~100) 평균
        //   [#6]  DoubtMemberCount_A12  — TrustMatrix.CurrentTier == Doubt 카운트
        //   [#10] TraumatizedRatio_A12  — TraumaSystem.CurrentStageIndex > 0 비율
        //   [#11] AbandonEventCount_A12 — AbandonRescueResolver.OnSituationResolved 구독
        // ═══════════════════════════════════════════════════════════════════

        #region A12 Tracking — Fields (B-1 + B-2)

        [Header("━━━ A12 Tracking (B-1 + B-2) ━━━━━━━━━")]

        [Tooltip("[A12 #3 / B-1] 연속 사망 카운터 — 윈도우 안 사망 누적, 초과 시 1 로 리셋. " +
                 "Read Only — HandleMemberDeath_A12 자동 갱신.")]
        [SerializeField] private int _consecutiveDeaths = 0;

        [Tooltip("[A12 #3 / B-1] 연속 사망 시간 윈도우 (초). 기본 5 초.")]
        [Range(1f, 30f)][SerializeField] private float _consecutiveDeathWindow = 5f;

        [Tooltip("[A12 #3 / B-1] 마지막 사망 발생 Time.time. Read Only.")]
        [SerializeField] private float _lastDeathTime = -999f;

        [Tooltip("[A12 #9 / B-1] 후퇴 누적 카운터 — QuickRetreat / StrategicWithdraw 전환 시 +1. " +
                 "Read Only — OnTacticalBehaviorChanged 자동 구독.")]
        [SerializeField] private int _retreatCount = 0;

        [Tooltip("[A12 #8 / B-1] EnemyAdvantage 계산 시 우방 수 하한. 우방 0 명 = ∞ 위험 → 본 값으로 분모 클램프. 기본 1.")]
        [Range(1, 10)][SerializeField] private int _enemyAdvantageMinAllies = 1;

        [Tooltip("[A12 #11 / B-2] 유기 사건 누적 카운터 — AbandonRescueResolver Abandon 발현 시 +1. " +
                 "Read Only — OnSituationResolved 자동 구독.")]
        [SerializeField] private int _abandonEventCount = 0;

        [Tooltip("[A12 #5 / B-2] 그룹에 TrustMatrix 부착 멤버 0 명 시 fallback Trust 값 (0~100).")]
        [Range(0f, 100f)][SerializeField] private float _averageTrustFallback = 60f;

        #endregion

        #region A12 Tracking — Getters (delegation targets for 7 stub property)

        /// <summary>
        /// [A12-B1 #3] 연속 사망 카운터.
        /// _lastDeathTime 으로부터 _consecutiveDeathWindow 초 경과 시 자동 0 반환.
        /// </summary>
        protected int GetConsecutiveDeaths_A12()
        {
            if (Time.time - _lastDeathTime > _consecutiveDeathWindow)
                _consecutiveDeaths = 0;
            return _consecutiveDeaths;
        }

        /// <summary>
        /// [A12-B1 #8] 적 우세 비율 = _knownTargets.Count / max(살아있는 멤버, _enemyAdvantageMinAllies).
        /// FactionThresholdConfigSO.maxEnemyAdvantage (기본 2.5) 와 비교 → 초과 시 strategic retreat.
        /// </summary>
        protected float GetEnemyAdvantage_A12()
        {
            int enemies = (_knownTargets != null) ? _knownTargets.Count : 0;
            int allies = CountAliveAllies_A12();
            int denom = Mathf.Max(allies, _enemyAdvantageMinAllies);
            return (float)enemies / denom;
        }

        /// <summary>
        /// [A12-B1 #9] 후퇴 누적 카운터.
        /// FactionThresholdConfigSO.maxRetreatCount (기본 5) 와 비교 → 초과 시 full collapse.
        /// </summary>
        protected int GetRetreatCount_A12() => _retreatCount;

        /// <summary>
        /// [A12-B2 #5] 그룹 멤버 중 TrustMatrix 부착자의 CurrentTrust (0~100) 평균.
        /// 부착자 0 명 시 _averageTrustFallback (기본 60f) 반환.
        /// ⚠ NormalizedTrust (0~1) 사용 X — FactionThresholdConfigSO.minAverageTrust (0~100) 정합.
        /// </summary>
        protected float GetAverageTrust_A12()
        {
            if (members == null || members.Count == 0) return _averageTrustFallback;
            float sum = 0f; int n = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var brain = members[i]?.brain;
                if (brain == null) continue;
                // ★ v3.11 Hotfix: 3 위치 fallback (Self → Parent → Children)
                var trust = brain.GetComponent<TrustMatrix>()
                            ?? brain.GetComponentInParent<TrustMatrix>()
                            ?? brain.GetComponentInChildren<TrustMatrix>();
                if (trust == null) continue;
                sum += trust.CurrentTrust;
                n++;
            }
            return n > 0 ? sum / n : _averageTrustFallback;
        }

        /// <summary>
        /// [A12-B2 #6] Doubt 단계 멤버 카운트.
        /// TrustMatrix.CurrentTier == TrustTier.Doubt 인 멤버 수.
        /// FactionThresholdConfigSO.maxDoubtCount (기본 5) 와 비교 → 초과 시 internal 분열.
        /// </summary>
        protected int GetDoubtMemberCount_A12()
        {
            if (members == null) return 0;
            int n = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var brain = members[i]?.brain;
                if (brain == null) continue;
                // ★ v3.11 Hotfix: 3 위치 fallback
                var trust = brain.GetComponent<TrustMatrix>()
                            ?? brain.GetComponentInParent<TrustMatrix>()
                            ?? brain.GetComponentInChildren<TrustMatrix>();
                if (trust != null && trust.CurrentTier == TrustTier.Doubt) n++;
            }
            return n;
        }

        /// <summary>
        /// [A12-B2 #10] 트라우마 멤버 비율 = (CurrentStageIndex > 0 멤버) / 전체 멤버 (0~1).
        /// FactionThresholdConfigSO.maxTraumatizedRatio (기본 0.4) 와 비교 → 초과 시 group cohesion 붕괴.
        /// </summary>
        protected float GetTraumatizedRatio_A12()
        {
            if (members == null || members.Count == 0) return 0f;
            int traumatized = 0, total = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var brain = members[i]?.brain;
                if (brain == null) continue;
                total++;
                // ★ v3.11 Hotfix: 3 위치 fallback
                var trauma = brain.GetComponent<TraumaSystem>()
                             ?? brain.GetComponentInParent<TraumaSystem>()
                             ?? brain.GetComponentInChildren<TraumaSystem>();
                if (trauma != null && trauma.CurrentStageIndex > 0) traumatized++;
            }
            return total > 0 ? (float)traumatized / total : 0f;
        }

        /// <summary>
        /// [A12-B2 #11] 유기 사건 누적 — AbandonRescueResolver Abandon 발현 시 +1.
        /// HandleAbandonResolved_A12 가 OnSituationResolved 구독 → outcome == Abandon 시 _abandonEventCount++.
        /// ⚠ AbandonRescueResolver = HumanoidAIBrain 전용 (수동 부착). Mob 그룹 = 항상 0 가능.
        /// </summary>
        protected int GetAbandonEventCount_A12() => _abandonEventCount;

        #endregion

        #region A12 Tracking — Triggers + Subscriptions

        /// <summary>
        /// [A12 #3 트리거] UnregisterMember 안 호출 — 윈도우 안 연속 사망 카운터 갱신.
        /// </summary>
        internal void HandleMemberDeath_A12(BaseAIBrain deadBrain)
        {
            float now = Time.time;
            if (now - _lastDeathTime <= _consecutiveDeathWindow)
                _consecutiveDeaths++;
            else
                _consecutiveDeaths = 1;
            _lastDeathTime = now;

            if (_a12DebugLog)
            {
                Log(COL_A12, "A12-Track",
                    $"연속 사망 카운터: <b>{_consecutiveDeaths}</b> " +
                    $"(윈도우 {_consecutiveDeathWindow:F1}s, " +
                    $"dead={(deadBrain != null ? deadBrain.name : "<null>")})");
            }
        }

        /// <summary>
        /// [A12 #9 트리거] OnTacticalBehaviorChanged event 구독 핸들러.
        /// QuickRetreat / StrategicWithdraw 전환 = 후퇴 1 회 누적.
        /// </summary>
        private void HandleTacticalChanged_A12(FactionTacticalBehavior oldB, FactionTacticalBehavior newB)
        {
            if (newB == FactionTacticalBehavior.QuickRetreat ||
                newB == FactionTacticalBehavior.StrategicWithdraw)
            {
                _retreatCount++;
                if (_a12DebugLog)
                {
                    Log(COL_A12, "A12-Track",
                        $"후퇴 누적: <b>{_retreatCount}</b> ({oldB} → {newB})");
                }
            }
        }

        /// <summary>
        /// [A12 #11 트리거] AbandonRescueResolver.OnSituationResolved 구독 핸들러.
        /// outcome == Abandon 발현 시 _abandonEventCount++.
        /// </summary>
        private void HandleAbandonResolved_A12(AbandonRescueOutcome outcome, string allyId)
        {
            if (outcome != AbandonRescueOutcome.Abandon) return;
            _abandonEventCount++;
            if (_a12DebugLog)
            {
                Log(COL_A12, "A12-Track",
                    $"유기 사건 누적: <b>{_abandonEventCount}</b> (allyId={allyId})");
            }
        }

        /// <summary>
        /// [A12 init] Awake 안 호출 — 이벤트 구독 + 카운터 초기화 + 멤버 AbandonResolver 일괄 구독.
        /// </summary>
        internal void OnA12TrackingInit()
        {
            // OnTacticalBehaviorChanged 구독 (중복 방지 -= 후 += 패턴)
            OnTacticalBehaviorChanged -= HandleTacticalChanged_A12;
            OnTacticalBehaviorChanged += HandleTacticalChanged_A12;

            // 카운터 초기화
            _consecutiveDeaths = 0;
            _retreatCount = 0;
            _lastDeathTime = -999f;
            _abandonEventCount = 0;

            // 현재 등록된 모든 멤버의 AbandonResolver 일괄 구독 (B-2)
            SubscribeAllAbandonResolvers_A12();

            if (_a12DebugLog)
                Log(COL_A12, "A12-Track", "추적 초기화 + 이벤트 구독 완료.");
        }

        /// <summary>
        /// [B-2] 현재 등록된 모든 멤버의 AbandonRescueResolver 일괄 구독.
        /// OnA12TrackingInit 안 호출 — 재컴파일 / 그룹 재생성 시에도 안전.
        /// </summary>
        private void SubscribeAllAbandonResolvers_A12()
        {
            if (members == null) return;
            for (int i = 0; i < members.Count; i++)
            {
                var brain = members[i]?.brain;
                if (brain == null) continue;
                SubscribeAbandonResolver_A12(brain);
            }
        }

        /// <summary>
        /// [B-2] 1 멤버의 AbandonResolver 구독 — RegisterMember 안 호출.
        /// 멤버 미부착 / Mob (AbandonResolver 부착 X) 시 자동 스킵.
        /// </summary>
        private void SubscribeAbandonResolver_A12(BaseAIBrain brain)
        {
            if (brain == null) return;
            // ★ v3.11 Hotfix: 3 위치 fallback
            var resolver = brain.GetComponent<AbandonRescueResolver>()
                           ?? brain.GetComponentInParent<AbandonRescueResolver>()
                           ?? brain.GetComponentInChildren<AbandonRescueResolver>();
            if (resolver == null) return;   // 미부착 = 스킵 (정상)
            resolver.OnSituationResolved -= HandleAbandonResolved_A12;   // 중복 방지
            resolver.OnSituationResolved += HandleAbandonResolved_A12;
        }

        /// <summary>
        /// [B-2] 1 멤버의 AbandonResolver 해제 — UnregisterMember 안 (members.RemoveAt 전) 호출.
        /// 메모리 누수 방지.
        /// </summary>
        private void UnsubscribeAbandonResolver_A12(BaseAIBrain brain)
        {
            if (brain == null) return;
            // ★ v3.11 Hotfix: 3 위치 fallback
            var resolver = brain.GetComponent<AbandonRescueResolver>()
                           ?? brain.GetComponentInParent<AbandonRescueResolver>()
                           ?? brain.GetComponentInChildren<AbandonRescueResolver>();
            if (resolver == null) return;
            resolver.OnSituationResolved -= HandleAbandonResolved_A12;
        }

        #endregion

        #region A12 Tracking — ContextMenu (진단 + 테스트)

        [ContextMenu("★ [A12-Tracking] Print Counters")]
        private void DebugPrintA12Counters()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("═══ A12 Tracking Counters (B-1 + B-2) ═══");
            sb.AppendLine($"  Group              = {gameObject.name}");
            sb.AppendLine($"  Faction            = " +
                          $"{(_factionConfig != null ? $"{_factionConfig.factionID} ({_factionConfig.factionName})" : "<no config>")}");
            sb.AppendLine($"  Members            = {(members != null ? members.Count : 0)} 명");
            sb.AppendLine();
            sb.AppendLine("  ── 그룹 A (B-1) — 본체 자체 추적");
            sb.AppendLine($"  [#3] ConsecutiveDeaths = {GetConsecutiveDeaths_A12()} " +
                          $"(raw={_consecutiveDeaths}, " +
                          $"lastDeathΔ={(Time.time - _lastDeathTime):F2}s / window {_consecutiveDeathWindow:F1}s)");
            sb.AppendLine($"  [#8] EnemyAdvantage    = {GetEnemyAdvantage_A12():F2} " +
                          $"(enemies={(_knownTargets != null ? _knownTargets.Count : 0)} / " +
                          $"allies={CountAliveAllies_A12()}, minAllies={_enemyAdvantageMinAllies})");
            sb.AppendLine($"  [#9] RetreatCount      = {_retreatCount}");
            sb.AppendLine();
            sb.AppendLine("  ── 그룹 B (B-2) — Wk4 컴포넌트 연동");
            sb.AppendLine($"  [#5]  AverageTrust       = {GetAverageTrust_A12():F1} (TrustMatrix 부착 멤버 평균, fallback={_averageTrustFallback:F0})");
            sb.AppendLine($"  [#6]  DoubtMemberCount  = {GetDoubtMemberCount_A12()} (TrustTier == Doubt)");
            sb.AppendLine($"  [#10] TraumatizedRatio  = {GetTraumatizedRatio_A12():F3} (CurrentStageIndex > 0 비율)");
            sb.AppendLine($"  [#11] AbandonEventCount = {_abandonEventCount} (Abandon outcome 누적)");
            sb.AppendLine("═══════════════════════════════════════");
            Debug.Log(sb.ToString(), this);
        }

        [ContextMenu("★ [A12-Tracking] Reset Counters")]
        private void DebugResetA12Counters()
        {
            int prevDeaths = _consecutiveDeaths;
            int prevRetreat = _retreatCount;
            int prevAbandon = _abandonEventCount;
            _consecutiveDeaths = 0;
            _retreatCount = 0;
            _lastDeathTime = -999f;
            _abandonEventCount = 0;
            Debug.Log($"<color=#FFAA88>[A12-Track]</color> {gameObject.name}: " +
                      $"deaths {prevDeaths}→0, retreats {prevRetreat}→0, abandons {prevAbandon}→0",
                      this);
        }

        [ContextMenu("★ [A12-Tracking] Force +1 Death (Test)")]
        private void DebugForceOneDeath()
        {
            HandleMemberDeath_A12(null);
            Debug.Log($"<color=#FF6677>[A12-Track]</color> {gameObject.name}: " +
                      $"강제 사망 1 회 → ConsecutiveDeaths={_consecutiveDeaths}",
                      this);
        }

        [ContextMenu("★ [A12-Tracking] Simulate Abandon Event (Test)")]
        private void DebugSimulateAbandonEvent()
        {
            HandleAbandonResolved_A12(AbandonRescueOutcome.Abandon, "TestAlly_Sim");
            Debug.Log($"<color=#CC79A7>[A12-Track]</color> {gameObject.name}: " +
                      $"강제 Abandon 1 회 → AbandonEventCount={_abandonEventCount}",
                      this);
        }

        [ContextMenu("★ [A12-Tracking] Diagnose Member Components")]
        private void DebugDiagnoseMemberComponents()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"═══ Member Component Diagnostics ({(members?.Count ?? 0)} 명) ═══");
            if (members == null || members.Count == 0)
            {
                sb.AppendLine("  (members == null or empty)");
                Debug.LogWarning(sb.ToString(), this);
                return;
            }

            int trustFoundTotal = 0, traumaFoundTotal = 0, abandonFoundTotal = 0;

            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m == null || m.brain == null)
                {
                    sb.AppendLine($"  [{i}] (null entry)");
                    continue;
                }

                var brain = m.brain;

                // 3 위치 탐색
                var trustSelf = brain.GetComponent<TrustMatrix>();
                var trustParent = brain.GetComponentInParent<TrustMatrix>();
                var trustChild = brain.GetComponentInChildren<TrustMatrix>();
                var traumaSelf = brain.GetComponent<TraumaSystem>();
                var traumaParent = brain.GetComponentInParent<TraumaSystem>();
                var traumaChild = brain.GetComponentInChildren<TraumaSystem>();
                var abandonSelf = brain.GetComponent<AbandonRescueResolver>();
                var abandonParent = brain.GetComponentInParent<AbandonRescueResolver>();
                var abandonChild = brain.GetComponentInChildren<AbandonRescueResolver>();

                var trust = trustSelf ?? trustParent ?? trustChild;
                var trauma = traumaSelf ?? traumaParent ?? traumaChild;
                var abandon = abandonSelf ?? abandonParent ?? abandonChild;

                if (trust != null) trustFoundTotal++;
                if (trauma != null) traumaFoundTotal++;
                if (abandon != null) abandonFoundTotal++;

                sb.AppendLine($"  [{i}] '{brain.gameObject.name}' (type={brain.GetType().Name})");
                sb.AppendLine($"        TrustMatrix       : Self={(trustSelf != null)}  Parent={(trustParent != null)}  Children={(trustChild != null)}");
                sb.AppendLine($"        TraumaSystem      : Self={(traumaSelf != null)}  Parent={(traumaParent != null)}  Children={(traumaChild != null)}");
                sb.AppendLine($"        AbandonRescue     : Self={(abandonSelf != null)}  Parent={(abandonParent != null)}  Children={(abandonChild != null)}");

                if (trust != null)
                    sb.AppendLine($"        → Trust   found on '{trust.gameObject.name}'   : CurrentTrust={trust.CurrentTrust:F1} / Tier={trust.CurrentTier}");
                if (trauma != null)
                    sb.AppendLine($"        → Trauma  found on '{trauma.gameObject.name}'  : CurrentStageIndex={trauma.CurrentStageIndex} ({trauma.CurrentStage})");
                if (abandon != null)
                    sb.AppendLine($"        → Abandon found on '{abandon.gameObject.name}' : (event subscribed)");
            }

            sb.AppendLine();
            sb.AppendLine($"  ── 종합: TrustMatrix {trustFoundTotal}/{members.Count}  TraumaSystem {traumaFoundTotal}/{members.Count}  AbandonRescue {abandonFoundTotal}/{members.Count}");
            sb.AppendLine("═══════════════════════════════════════");
            Debug.Log(sb.ToString(), this);
        }

        [ContextMenu("★ [V-Wk4-04] Compute Fear Modifier (Trust × Trauma)")]
        private void DebugComputeFearModifierVWk4_04()
        {
            if (members == null || members.Count == 0)
            {
                Debug.LogWarning("[V-Wk4-04] members empty", this);
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"═══ V-Wk4-04 — Trust × Trauma Fear Modifier ({members.Count} 명) ═══");
            sb.AppendLine($"  수식: adjusted = originalFear(1.0) × baseModifier - trustBonus");
            sb.AppendLine($"        baseModifier: None=1.0 / AcuteShock=1.3 / Crossroads=1.5 / Permanent=1.8");
            sb.AppendLine($"        trustBonus = (CurrentTrust / 100) × 0.5");
            sb.AppendLine();
            sb.AppendLine($"  ── 개론서 §5.4.6 #4 통과 기준 ──");
            sb.AppendLine($"     Trust 90 + AcuteShock → expected fear = 1.3 - 0.45 = 0.85");
            sb.AppendLine($"     ('fear -0.45' = Trust 상쇄량 = trustBonus 본 값)");
            sb.AppendLine();

            int passCount = 0, applicableCount = 0;

            for (int i = 0; i < members.Count; i++)
            {
                var brain = members[i]?.brain;
                if (brain == null) continue;

                var trust = brain.GetComponent<TrustMatrix>()
                            ?? brain.GetComponentInParent<TrustMatrix>()
                            ?? brain.GetComponentInChildren<TrustMatrix>();
                var trauma = brain.GetComponent<TraumaSystem>()
                             ?? brain.GetComponentInParent<TraumaSystem>()
                             ?? brain.GetComponentInChildren<TraumaSystem>();

                if (trauma == null)
                {
                    sb.AppendLine($"  [{i}] '{brain.gameObject.name}' — TraumaSystem 미부착 (스킵)");
                    continue;
                }

                float trustVal = trust != null ? trust.CurrentTrust : 0f;
                float originalFear = 1.0f;
                float result = trauma.ComputeFearModifier(originalFear, trustVal);
                float trustBonusCalc = (trustVal / 100f) * 0.5f;
                float baseModifier;
                switch (trauma.CurrentStage)
                {
                    case TraumaStage.AcuteShock: baseModifier = 1.3f; break;
                    case TraumaStage.Crossroads: baseModifier = 1.5f; break;
                    case TraumaStage.PermanentScarring: baseModifier = 1.8f; break;
                    default: baseModifier = 1.0f; break;
                }
                float adjustedRaw = originalFear * baseModifier - trustBonusCalc;
                float delta = result - originalFear;

                sb.AppendLine($"  [{i}] '{brain.gameObject.name}'");
                sb.AppendLine($"        Trust              = {trustVal:F1} ({(trust != null ? trust.CurrentTier.ToString() : "n/a")})");
                sb.AppendLine($"        TraumaStage        = {trauma.CurrentStage} (baseModifier = {baseModifier:F1})");
                sb.AppendLine($"        trustBonus         = ({trustVal:F0}/100)×0.5 = {trustBonusCalc:F3}");
                sb.AppendLine($"        adjusted raw       = {originalFear:F1}×{baseModifier:F1} - {trustBonusCalc:F3} = {adjustedRaw:F3}");
                sb.AppendLine($"        → fear (clamp01)   = {result:F3}   (Δ = {(delta >= 0 ? "+" : "")}{delta:F3})");

                // §5.4.6 #4 통과 기준 (Trust ≈ 90 + AcuteShock → fear ≈ 0.85)
                if (trust != null && trauma.CurrentStage == TraumaStage.AcuteShock)
                {
                    // expected = 1.3 - (trust/100)×0.5 (clamp 안 가정)
                    float expected = Mathf.Clamp01(1.3f - trustBonusCalc);
                    float tolerance = 0.05f;
                    bool pass = Mathf.Abs(result - expected) < tolerance;
                    applicableCount++;
                    if (pass) passCount++;
                    sb.AppendLine($"        ★ V-Wk4-04 검증: expected={expected:F3} / actual={result:F3} → {(pass ? "✅ 통과" : "❌ 미통과")}");
                }
            }

            sb.AppendLine();
            if (applicableCount > 0)
                sb.AppendLine($"  ── 종합: AcuteShock 멤버 {applicableCount} 명 중 {passCount} 명 통과");
            else
                sb.AppendLine($"  ── 종합: AcuteShock 진입 멤버 0 명 — TraumaSystem DebugTriggerAcute 호출 필요");
            sb.AppendLine("═══════════════════════════════════════");
            Debug.Log(sb.ToString(), this);
        }

        [ContextMenu("★ [A12-Tracking] Re-subscribe All AbandonResolvers (Test)")]
        private void DebugResubscribeAllAbandon()
        {
            SubscribeAllAbandonResolvers_A12();
            int subscribed = 0;
            if (members != null)
                for (int i = 0; i < members.Count; i++)
                    if (members[i]?.brain != null &&
                        members[i].brain.GetComponent<AbandonRescueResolver>() != null) subscribed++;
            Debug.Log($"<color=#56B4E9>[A12-Track]</color> {gameObject.name}: " +
                      $"AbandonResolver 일괄 재구독 — {subscribed} / {(members?.Count ?? 0)} 멤버",
                      this);
        }

        #endregion

        #region A12 Tracking — Private Helpers

        /// <summary>살아있는 (brain != null) 멤버 수.</summary>
        private int CountAliveAllies_A12()
        {
            if (members == null) return 0;
            int n = 0;
            for (int i = 0; i < members.Count; i++)
                if (members[i] != null && members[i].brain != null) n++;
            return n;
        }

        #endregion

        // ==================================================================
        // Lifecycle
        // ==================================================================
        private void Awake()
        {
            if (policyImplementor != null)
                policy = policyImplementor as Interfaces.Intelligence.IFactionGroupPolicy;
            if (policy == null)
                policy = GetComponentInChildren<Interfaces.Intelligence.IFactionGroupPolicy>();
            if (policy == null)
                LogW($"IFactionGroupPolicy 구현체를 찾을 수 없습니다.");
            else
                Log(COL_LIFECYCLE, "Awake", $"정책 바인딩 완료: {policy.GetType().Name}");

            OnA12TrackingInit();   // ★ Wk5 B-1 A12-Tracking init
        }

        public void OnEnable()
        {
            if (_groupSpawnTime <= 0f) _groupSpawnTime = Time.time;
            PushEvent("Lifecycle", "그룹 OnEnable", new Color(0.85f, 0.85f, 0.85f));

            if (policy == null)
            {
                if (policyImplementor != null)
                    policy = policyImplementor as Interfaces.Intelligence.IFactionGroupPolicy;
                if (policy == null)
                    policy = GetComponentInChildren<Interfaces.Intelligence.IFactionGroupPolicy>();
                if (policy == null)
                    LogW("OnEnable 시점에도 IFactionGroupPolicy 구현체 미발견.");
                else
                    Log(COL_LIFECYCLE, "OnEnable", $"정책 지연 바인딩: {policy.GetType().Name}");
            }
        }

        private void Update()
        {
            if (members.Count == 0) return;
            combatElapsedTime += Time.deltaTime;

            if (Time.time - _lastThresholdCheckTime >= THRESHOLD_CHECK_INTERVAL)
            {
                _lastThresholdCheckTime = Time.time;
                CheckAllThresholds();
            }

            moraleTimer += Time.deltaTime;
            if (moraleTimer >= 1.0f)
            {
                moraleTimer = 0f;
                UpdateMorale();
                UpdateEscalation();
            }

            tokenTimer += Time.deltaTime;
            if (tokenTimer >= tokenReassignInterval)
            {
                tokenTimer = 0f;
                ReassignAttackTokens();
            }

            _sharedTargetReevalTimer += Time.deltaTime;
            if (_sharedTargetReevalTimer >= SHARED_TARGET_REEVAL_INTERVAL)
            {
                _sharedTargetReevalTimer = 0f;
                ReevaluateSharedTargets();
                ReevaluateFactionSafeZone();
                ReevaluateTacticalRetreat();
            }

            foreach (var m in members)
            {
                if (m.brain != null)
                    m.timeSinceLastAttack += Time.deltaTime;
            }

            if (debugLog && logFrameTimers)
            {
                Log(COL_LIFECYCLE, "Tick",
                    $"tokenTimer={tokenTimer:F2}/{tokenReassignInterval:F2} " +
                    $"moraleTimer={moraleTimer:F2}/1.00 " +
                    $"combat={combatElapsedTime:F1}s members={members.Count}");
            }

#if UNITY_EDITOR
            if (focusMode != FocusCameraMode.None && lastTokenWinner != null && lastTokenWinner.transform != null)
            {
                var sv = UnityEditor.SceneView.lastActiveSceneView;
                if (sv != null) UpdateFocusCamera(sv);
            }
#endif
        }

#if UNITY_EDITOR
        private void UpdateFocusCamera(UnityEditor.SceneView sv)
        {
            Vector3 winnerPos = lastTokenWinner.transform.position + Vector3.up * 1.5f;
            // ★ v3.7 — MobAIBrain 만 currentTarget 멤버 보유 (Humanoid 별도 디자인)
            var winnerMob = lastTokenWinner as MobAIBrain;
            Transform tgt = winnerMob != null ? winnerMob.currentTarget : null;
            bool hasTarget = tgt != null;
            Vector3 targetPos = hasTarget ? tgt.position + Vector3.up * 1.0f : Vector3.zero;
            switch (focusMode)
            {
                case FocusCameraMode.AIFront: FocusAIFront(sv, winnerPos); break;
                case FocusCameraMode.AIPlusTargetTop: if (hasTarget) FocusAIPlusTargetTop(sv, winnerPos, targetPos); else FocusAIFront(sv, winnerPos); break;
                case FocusCameraMode.AIShoulderFixed: if (hasTarget) FocusAIShoulderFixed(sv, winnerPos, targetPos); else FocusAIFront(sv, winnerPos); break;
                case FocusCameraMode.GroupTop: FocusGroupTop(sv); break;
                case FocusCameraMode.GroupShoulderToTarget: if (hasTarget) FocusGroupShoulderToTarget(sv, targetPos); else FocusGroupTop(sv); break;
            }
        }

        private void FocusAIFront(UnityEditor.SceneView sv, Vector3 aiPos)
        {
            Vector3 aiForward = lastTokenWinner.transform.forward;
            Vector3 camPos = aiPos + aiForward * 3f + Vector3.up * 0.5f;
            float size = Vector3.Distance(camPos, aiPos);
            Quaternion rot = Quaternion.LookRotation(aiPos - camPos);
            sv.LookAt(aiPos, rot, size, false, false);
        }

        private void FocusAIPlusTargetTop(UnityEditor.SceneView sv, Vector3 winnerPos, Vector3 targetPos)
        {
            Vector3 mid = (winnerPos + targetPos) * 0.5f;
            float distance = Vector3.Distance(winnerPos, targetPos);
            float size = Mathf.Max(distance * 0.7f + 4f, 6f);
            Quaternion rot = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            sv.LookAt(mid, rot, size, false, false);
        }

        private void FocusAIShoulderFixed(UnityEditor.SceneView sv, Vector3 winnerPos, Vector3 targetPos)
        {
            Vector3 dir = targetPos - winnerPos;
            float distance = dir.magnitude;
            if (distance < 0.1f) { FocusAIFront(sv, winnerPos); return; }
            dir /= distance;
            const float FIXED_BACK = 3f;
            const float FIXED_UP = 1.5f;
            Vector3 camPos = winnerPos - dir * FIXED_BACK + Vector3.up * FIXED_UP;
            float size = Vector3.Distance(camPos, targetPos);
            Quaternion rot = Quaternion.LookRotation(targetPos - camPos);
            sv.LookAt(targetPos, rot, size, false, false);
        }

        private void FocusGroupTop(UnityEditor.SceneView sv)
        {
            Bounds bounds;
            if (!ComputeGroupBounds(out bounds)) return;
            float size = Mathf.Max(bounds.size.magnitude * 0.6f + 3f, 6f);
            Quaternion rot = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            sv.LookAt(bounds.center, rot, size, false, false);
        }

        private void FocusGroupShoulderToTarget(UnityEditor.SceneView sv, Vector3 targetPos)
        {
            Bounds bounds;
            if (!ComputeGroupBounds(out bounds)) return;
            Vector3 groupCenter = bounds.center + Vector3.up * 1.5f;
            Vector3 dir = targetPos - groupCenter;
            float distance = dir.magnitude;
            if (distance < 0.1f) { FocusGroupTop(sv); return; }
            dir /= distance;
            float backOffset = Mathf.Max(bounds.size.magnitude * 0.4f, 3f);
            float upOffset = Mathf.Max(bounds.size.magnitude * 0.2f, 1f);
            Vector3 camPos = groupCenter - dir * backOffset + Vector3.up * upOffset;
            float size = Vector3.Distance(camPos, targetPos);
            Quaternion rot = Quaternion.LookRotation(targetPos - camPos);
            sv.LookAt(targetPos, rot, size, false, false);
        }

        private bool ComputeGroupBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            bool first = true;
            foreach (var m in members)
            {
                if (m == null || m.brain == null) continue;
                if (first) { bounds = new Bounds(m.brain.transform.position, Vector3.one * 1f); first = false; }
                else bounds.Encapsulate(m.brain.transform.position);
            }
            return !first;
        }
#endif

        private float _sharedTargetReevalTimer = 0f;
        private const float SHARED_TARGET_REEVAL_INTERVAL = 1.0f;

        private void ReevaluateSharedTargets()
        {
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.brain == null) continue;
                // ★ v3.7 — Mob 만 sharedTarget 재전파 (Humanoid 은 currentTarget 별도 디자인)
                var mob = m.brain as MobAIBrain;
                if (mob == null || mob.currentTarget == null) continue;
                NotifyTargetSpotted(mob, mob.currentTarget, silent: true);
            }
        }

        private void ReevaluateFactionSafeZone()
        {
            if (!hasFactionSafeZone) return;
            float closeRadius = policySO != null ? policySO.sharedTargetCloseRadius : DEFAULT_SHARED_CLOSE_RADIUS;
            int syncedCount = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.brain == null) continue;
                if (HasFactionSafeZoneInMemory(m.brain)) continue;
                Vector3 myPos = m.brain.transform.position;
                bool eligible = false;
                string reason = "";
                float distToZone = Vector3.Distance(myPos, factionSafeZone);
                if (distToZone <= closeRadius)
                {
                    eligible = true;
                    reason = $"self ({distToZone:F1}m ≤ {closeRadius:F0}m)";
                }
                if (!eligible)
                {
                    for (int j = 0; j < members.Count; j++)
                    {
                        if (j == i) continue;
                        var other = members[j];
                        if (other.brain == null) continue;
                        if (!HasFactionSafeZoneInMemory(other.brain)) continue;
                        float dist = Vector3.Distance(myPos, other.brain.transform.position);
                        if (dist <= closeRadius)
                        {
                            eligible = true;
                            reason = $"via {other.brain.name} ({dist:F1}m ≤ {closeRadius:F0}m)";
                            break;
                        }
                    }
                }
                if (eligible)
                {
                    m.brain.RememberSafePosition(factionSafeZone);
                    syncedCount++;
                    PushEvent("Sense", $"{m.brain.name} 펙션 SafeZone 메모리 자동 동기화 ({reason})", EVT_SENSE);
                }
            }
            if (syncedCount > 0 && verboseTokenLog)
                Log(COL_TOKEN, "SafeZone:Reeval", $"{syncedCount}명 펙션 SafeZone 자동 동기화");
        }

        private bool HasFactionSafeZoneInMemory(BaseAIBrain brain)
        {
            if (!hasFactionSafeZone || brain == null) return true;
            foreach (var pos in brain.LastSafePositions)
                if (Vector3.Distance(pos, factionSafeZone) < 1f) return true;
            return false;
        }

        private void ReevaluateTacticalRetreat()
        {
            if (policySO == null || !policySO.orderedRetreatEnabled) return;
            float hpThreshold = policySO.orderedRetreatHpThreshold;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.brain == null) continue;
                var aiMgr = m.brain.GetComponent<TDA.Character.AI.AICharacterManager>();
                var nm = aiMgr?.characterNetworkManager;
                if (nm == null) continue;
                float maxHp = nm.maxHealth.Value;
                if (maxHp <= 0f) continue;
                float hpRatio = Mathf.Clamp01(nm.currentHealth.Value / maxHp);
                bool shouldRetreat = hpRatio < hpThreshold;
                bool wasOrdered = m.brain.OrderedRetreat;
                if (shouldRetreat && !wasOrdered)
                {
                    m.brain.OrderedRetreat = true;
                    Log(COL_TOKEN, "TacticalRetreat", $"{m.brain.name}: HP={hpRatio:P0} < {hpThreshold:P0} → 후퇴 명령");
                    PushEvent("Combat", $"{m.brain.name} 후퇴 명령 (HP {hpRatio:P0})", EVT_COMBAT);
                }
                else if (!shouldRetreat && wasOrdered)
                {
                    m.brain.OrderedRetreat = false;
                    Log(COL_TOKEN, "TacticalRetreat", $"{m.brain.name}: HP={hpRatio:P0} 회복 → 후퇴 해제");
                    PushEvent("Combat", $"{m.brain.name} 후퇴 해제 (HP {hpRatio:P0})", EVT_COMBAT);
                }
            }
        }

        // ==================================================================
        // 공격 토큰 시스템 (v3.4 보존)
        // ==================================================================
        private void ReassignAttackTokens()
        {
            // ★ D-11 (Wk5 D1) — 신규 AttackToken (partial) 측 활성 시 legacy 차단.
            // enableAttackTokenManager=true (기본) 시 신규 RequestToken/ReturnToken 측 본격.
            // 비활성 시 본 legacy 측 fallback 측 작동 — 기존 동작 보존.
            if (enableAttackTokenManager) return;

            int prevMax = maxTokenCount;
            int prevActive = activeTokenCount;
            bool useOverride = policySO != null && policySO.tokenOverrideEnabled;
            if (useOverride)
                maxTokenCount = Mathf.Max(0, Mathf.FloorToInt(policySO.tokenOverrideThreshold));
            else
                maxTokenCount = Mathf.FloorToInt(members.Count * tokenMultiplier + 1);

            var prevTokenStates = new bool[members.Count];
            for (int i = 0; i < members.Count; i++)
                prevTokenStates[i] = members[i].hasAttackToken;

            foreach (var m in members) m.hasAttackToken = false;
            activeTokenCount = 0;

            var eligible = new List<GroupMemberState>();
            int ineligibleCount = 0;
            var ineligibleReasons = new StringBuilder(64);
            foreach (var m in members)
            {
                string reason;
                if (IsAttackEligible(m, out reason)) eligible.Add(m);
                else
                {
                    ineligibleCount++;
                    if (ineligibleReasons.Length > 0) ineligibleReasons.Append(", ");
                    string nm = m.brain != null ? m.brain.gameObject.name : "<null>";
                    ineligibleReasons.Append($"{nm}({reason})");
                }
            }

            eligible.Sort((a, b) => b.timeSinceLastAttack.CompareTo(a.timeSinceLastAttack));

            var awarded = new List<GroupMemberState>();
            for (int i = 0; i < eligible.Count && activeTokenCount < maxTokenCount; i++)
            {
                eligible[i].hasAttackToken = true;
                activeTokenCount++;
                awarded.Add(eligible[i]);
            }

            if (ineligibleCount > 0)
                Log(COL_TOKEN, "Tokens", $"⚠ 자격 미달 {ineligibleCount}/{members.Count}명 → {ineligibleReasons}");

            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                bool prev = prevTokenStates[i];
                bool now = m.hasAttackToken;
                if (prev == now) continue;
                string nm = m.brain != null ? m.brain.gameObject.name : "<null>";
                if (now)
                {
                    Log(COL_TOKEN, "Token+", $"<b>{nm}</b> 토큰 획득 (대기 {m.timeSinceLastAttack:F2}s, atkCnt={m.attackCount})");
                    PushEvent("Token", $"{nm} 토큰 획득 (atk#={m.attackCount})", EVT_TOKEN);
                    lastTokenWinner = m.brain;
                }
                else
                {
                    Log(COL_TOKEN, "Token-", $"<b>{nm}</b> 토큰 회수 (atkCnt={m.attackCount})");
                    PushEvent("Token", $"{nm} 토큰 회수", EVT_TOKEN);
                }
            }

            string awardedNames = awarded.Count == 0 ? "없음" :
                string.Join(", ", awarded.ConvertAll(a => a.brain != null ? a.brain.gameObject.name : "<null>"));

            Log(COL_TOKEN, "Tokens",
                $"재분배 → 토큰 보유자: <b>{awardedNames}</b> " +
                $"[{activeTokenCount}/{maxTokenCount} 활성, 멤버 {members.Count}명, " +
                (useOverride ? $"OVERRIDE={policySO.tokenOverrideThreshold:F1}" : $"mult={tokenMultiplier:F2}") +
                $", 직전 {prevActive}/{prevMax}]");

            if (debugLog && verboseTokenLog)
            {
                var sb = new StringBuilder(256);
                sb.AppendLine($"<color={COL_TOKEN}>[GroupAI:Tokens] 정렬·할당 상세</color>");
                if (useOverride)
                    sb.AppendLine($"  공식: maxToken = max(0, floor({policySO.tokenOverrideThreshold:F2})) = {maxTokenCount}  <b>[SO OVERRIDE]</b>");
                else
                    sb.AppendLine($"  공식: maxToken = floor({members.Count} × {tokenMultiplier:F2} + 1) = {maxTokenCount}");
                sb.AppendLine($"  ┌────┬──────────────────────────┬──────────┬─────────┬───────┬───────┬────────┐");
                sb.AppendLine($"  │ #  │ name                     │ tSinceAtk│ atkCnt  │ role  │ 자격  │ token  │");
                sb.AppendLine($"  ├────┼──────────────────────────┼──────────┼─────────┼───────┼───────┼────────┤");
                var displayList = new List<GroupMemberState>(eligible);
                foreach (var m in members)
                    if (!eligible.Contains(m)) displayList.Add(m);
                for (int i = 0; i < displayList.Count; i++)
                {
                    var m = displayList[i];
                    string nm = m.brain != null ? m.brain.gameObject.name : "<null>";
                    if (nm.Length > 24) nm = nm.Substring(0, 24);
                    string elig;
                    if (eligible.Contains(m)) elig = "  ✔  ";
                    else { IsAttackEligible(m, out var r); elig = r.Length > 5 ? r.Substring(0, 5) : r.PadRight(5); }
                    sb.AppendLine($"  │ {i,2} │ {nm,-24} │ {m.timeSinceLastAttack,8:F2} │ {m.attackCount,7} │ {ShortRole(m.currentRole),-5} │ {elig} │ {(m.hasAttackToken ? "  ✔   " : "      ")} │");
                }
                sb.AppendLine($"  └────┴──────────────────────────┴──────────┴─────────┴───────┴───────┴────────┘");
                Debug.Log(sb.ToString(), this);
            }
        }

        private bool IsAttackEligible(GroupMemberState m, out string reason)
        {
            if (m.brain == null) { reason = "null"; return false; }
            if (!m.brain.gameObject.activeInHierarchy) { reason = "inact"; return false; }

            // ★ v3.7 — MobAIBrain 특정 멤버 검사 (Mob 만 적용)
            var mob = m.brain as MobAIBrain;
            if (mob != null)
            {
                if (mob.CurrentState == MobBTState.Flee) { reason = "flee"; return false; }
            }
            // Humanoid 는 본 검사 스킵 (Wk5+ Humanoid 의 도주 상태 검사 별도 디자인)

            var charMgr = m.brain.GetComponent<CharacterManager>();
            if (charMgr != null && charMgr.characterNetworkManager != null
                && charMgr.characterNetworkManager.isDead != null
                && charMgr.characterNetworkManager.isDead.Value)
            { reason = "dead"; return false; }

            var aiMgr = m.brain.GetComponent<AICharacterManager>();
            if (aiMgr != null && aiMgr.isPerformingAction && !aiMgr.canMove)
            { reason = "stuck"; return false; }

            // ★ v3.7 — Mob 의 currentTarget 검사 (Humanoid 은 currentTarget 필드 다를 가능성 — 스킵)
            if (mob != null)
            {
                if (mob.currentTarget == null && GetSharedTarget(mob) == null)
                { reason = "noTgt"; return false; }
            }

            reason = "";
            return true;
        }

        // ==================================================================
        // Shared Target L2 (v3.4 보존)
        // ==================================================================
        public Transform GetSharedTarget(MobAIBrain caller)
        {
            if (caller == null) return null;
            float timeout = policySO != null ? policySO.sharedTargetTimeout : DEFAULT_SHARED_TIMEOUT;
            Transform best = null;
            float bestDist = float.MaxValue;
            Vector3 callerPos = caller.transform.position;
            for (int i = _knownTargets.Count - 1; i >= 0; i--)
            {
                var slot = _knownTargets[i];
                if (slot.target == null || Time.time - slot.lastSeenTime > timeout
                    || !slot.target.gameObject.activeInHierarchy)
                {
                    string targetName = slot.target != null ? slot.target.name : "(null)";
                    string reason = slot.target == null ? "destroyed"
                                  : !slot.target.gameObject.activeInHierarchy ? "inactive" : "timeout";
                    _knownTargets.RemoveAt(i);
                    PushEvent("Sense", $"KnownTarget 제거: {targetName} ({reason})", EVT_SENSE);
                    continue;
                }
                if (!slot.awareMembers.Contains(caller)) continue;
                float dist = Vector3.Distance(callerPos, slot.target.position);
                if (dist < bestDist) { best = slot.target; bestDist = dist; }
            }
            return best;
        }

        public void NotifyTargetSpotted(MobAIBrain spotter, Transform target, bool silent = false)
        {
            if (target == null || spotter == null) return;
            float closeRadius = policySO != null ? policySO.sharedTargetCloseRadius : DEFAULT_SHARED_CLOSE_RADIUS;
            float chainMax = policySO != null ? policySO.sharedTargetChainMaxRadius : DEFAULT_SHARED_CHAIN_MAX;
            LayerMask obstacles = policySO != null ? policySO.sharedTargetSightObstacleLayers : ~0;
            Vector3 spotterPos = spotter.transform.position + Vector3.up * 0.5f;
            TargetSlot slot = FindOrCreateTargetSlot(target, spotter);
            slot.lastKnownPosition = target.position;
            slot.lastSeenTime = Time.time;
            if (!slot.awareMembers.Contains(spotter)) slot.awareMembers.Add(spotter);
            int closeCount = 0, chainCount = 0, blockedCount = 0, outOfRangeCount = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.brain == null) continue;
                // ★ v3.7 — Mob 만 sharedTarget awareMembers 등록 (Humanoid currentTarget 별도 디자인)
                var memberMob = m.brain as MobAIBrain;
                if (memberMob == null || memberMob == spotter) continue;

                Vector3 memberPos = m.brain.transform.position + Vector3.up * 0.5f;
                float dist = Vector3.Distance(spotterPos, memberPos);
                if (dist <= closeRadius)
                {
                    if (!slot.awareMembers.Contains(memberMob)) slot.awareMembers.Add(memberMob);
                    closeCount++;
                }
                else if (dist <= chainMax && chainMax > 0)
                {
                    Vector3 dir = memberPos - spotterPos;
                    bool blocked = IsLineOfSightBlocked(spotterPos, dir, spotter, memberMob, obstacles);
                    if (!blocked)
                    {
                        if (!slot.awareMembers.Contains(memberMob)) slot.awareMembers.Add(memberMob);
                        chainCount++;
                    }
                    else { slot.awareMembers.Remove(memberMob); blockedCount++; }
                }
                else { slot.awareMembers.Remove(memberMob); outOfRangeCount++; }
            }
            string summary = $"<b>{spotter.gameObject.name}</b> → <b>{target.name}</b> 인지. " +
                             $"전파: 즉시={closeCount} / 체인={chainCount} / 차단={blockedCount} / 범위외={outOfRangeCount}";
            if (!silent) Log(COL_TOKEN, "Sense", summary);
            else if (verboseTokenLog) Log(COL_TOKEN, "Sense:Reeval", summary);
        }

        private TargetSlot FindOrCreateTargetSlot(Transform target, MobAIBrain firstSpotter)
        {
            for (int i = 0; i < _knownTargets.Count; i++)
                if (_knownTargets[i].target == target) return _knownTargets[i];
            var slot = new TargetSlot
            {
                target = target,
                firstSpotter = firstSpotter,
                firstSpotTime = Time.time,
                lastSeenTime = Time.time,
                lastKnownPosition = target.position,
                awareMembers = new List<MobAIBrain>(),
            };
            _knownTargets.Add(slot);
            PushEvent("Sense", $"KnownTarget 추가: {target.name} (spotter={firstSpotter?.name ?? "?"})", EVT_SENSE);
            return slot;
        }

        private static bool IsLineOfSightBlocked(Vector3 spotterPos, Vector3 dirToMember,
                                                  MobAIBrain spotter, MobAIBrain member, LayerMask obstacles)
        {
            float dist = dirToMember.magnitude;
            if (dist <= 0.01f) return false;
            var hits = Physics.RaycastAll(spotterPos, dirToMember.normalized, dist, obstacles);
            foreach (var hit in hits)
            {
                if (spotter != null && (hit.transform == spotter.transform || hit.transform.IsChildOf(spotter.transform))) continue;
                if (member != null && (hit.transform == member.transform || hit.transform.IsChildOf(member.transform))) continue;
                return true;
            }
            return false;
        }

        [ContextMenu("★ Wk3 Test/Print Known Targets")]
        private void DebugPrintKnownTargets()
        {
            if (_knownTargets.Count == 0)
            {
                Debug.Log($"[GroupAI:{name}] Known Targets: 비어있음");
                return;
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<color=#88FFAA>[GroupAI:{name}]</color> Known Targets ({_knownTargets.Count}):");
            for (int i = 0; i < _knownTargets.Count; i++)
            {
                var s = _knownTargets[i];
                string targetName = s.target != null ? s.target.name : "<null>";
                string awareNames = s.awareMembers.Count == 0 ? "<none>" :
                    string.Join(", ", s.awareMembers.ConvertAll(m => m != null ? m.name : "<null>"));
                sb.AppendLine($"  [{i}] {targetName}  lastSeen={Time.time - s.lastSeenTime:F1}s ago  aware=[{awareNames}]");
            }
            Debug.Log(sb.ToString());
        }

        // ==================================================================
        // 사기 / 에스컬레이션 / 역할 (v3.4 보존)
        // ==================================================================
        private void UpdateMorale()
        {
            if (policy == null) { Log(COL_MORALE, "Morale", "policy=null"); return; }
            float recentLoss = 0f;
            int deadCount = 0, fleeCount = 0;
            foreach (var m in members)
            {
                if (m.brain == null) { recentLoss += 0.2f; deadCount++; continue; }
                // ★ v3.7 — Mob 만 CurrentState 검사 (Humanoid Flee 검출 별도 디자인)
                var mob = m.brain as MobAIBrain;
                if (mob != null && mob.CurrentState == MobBTState.Flee)
                { recentLoss += 0.1f; fleeCount++; }
            }
            float prev = currentMorale;
            float newMorale = policy.EvaluateMorale(currentMorale, recentLoss, 1.0f);
            currentMorale = Mathf.Clamp01(newMorale);
            string moraleLabel = MoraleLabel(currentMorale);
            string changeIndicator = Mathf.Approximately(prev, currentMorale) ? "변화 없음"
                : currentMorale > prev ? $"▲ +{(currentMorale - prev):F3}"
                : $"▼ -{(prev - currentMorale):F3}";
            Log(COL_MORALE, "Morale",
                $"평가: {prev:F3} → <b>{currentMorale:F3}</b> ({moraleLabel}, {changeIndicator}) " +
                $"| 손실={recentLoss:F2} = 사망 {deadCount}×0.2 + 도주 {fleeCount}×0.1");
            if (currentMorale < 0.2f)
            {
                Log(COL_MORALE, "Morale", $"<b>패닉 체인 발동</b> (사기 {currentMorale:F3} < 0.2)");
                foreach (var m in members)
                    if (m.brain != null) m.brain.fear = Mathf.Min(1f, m.brain.fear + 0.3f);
                policy.HandlePanicChain(-1, policySO != null ? policySO.panicChainMultiplier : 1f);
            }
        }

        private void UpdateEscalation()
        {
            // ★ D-10 (Wk5 D1) — 신규 Escalation Tier 시스템 측 단일 진입.
            // legacy currentEscalationLevel (int 0~3) 측 신규 currentEscalationTier
            // (EscalationTier enum None/Alert/Critical/Collapse) 측 동기 — 호환 보존.
            int prevLevel = currentEscalationLevel;
            UpdateEscalation_Tick();
            int newLevel = (int)currentEscalationTier;

            if (newLevel != prevLevel)
            {
                currentEscalationLevel = newLevel;
                string factionId = policySO != null ? policySO.factionId : "unknown";
                EventBus.RaiseEscalationTriggered(factionId, currentEscalationLevel);
                Log(COL_ESCAL, "Escal",
                    $"<b>레벨 {prevLevel} → {currentEscalationLevel}</b> (faction={factionId}, " +
                    $"tier={currentEscalationTier})");
                UpdateRoleDistribution();
            }
        }

        private void UpdateRoleDistribution()
        {
            int count = members.Count;
            if (count == 0) return;
            int strikerCount, disruptorCount, stalkerCount;
            switch (currentEscalationLevel)
            {
                case 0:
                    stalkerCount = Mathf.Max(2, count * 2 / 3);
                    disruptorCount = Mathf.Max(0, count - stalkerCount);
                    strikerCount = 0;
                    break;
                case 1:
                    stalkerCount = Mathf.Max(1, count / 4);
                    disruptorCount = Mathf.Max(2, count / 2);
                    strikerCount = count - stalkerCount - disruptorCount;
                    break;
                case 2:
                    stalkerCount = 1;
                    disruptorCount = Mathf.Max(1, count / 4);
                    strikerCount = count - stalkerCount - disruptorCount;
                    break;
                case 3:
                default:
                    stalkerCount = 0; disruptorCount = 0; strikerCount = count;
                    break;
            }
            var prevRoles = new GroupRole[count];
            for (int i = 0; i < count; i++) prevRoles[i] = members[i].currentRole;
            int idx = 0;
            for (int i = 0; i < stalkerCount && idx < count; i++, idx++) members[idx].currentRole = GroupRole.Stalker;
            for (int i = 0; i < disruptorCount && idx < count; i++, idx++) members[idx].currentRole = GroupRole.Disruptor;
            for (; idx < count; idx++) members[idx].currentRole = GroupRole.Striker;
            for (int i = 0; i < count; i++)
            {
                var m = members[i];
                if (prevRoles[i] == m.currentRole) continue;
                string nm = m.brain != null ? m.brain.gameObject.name : "<null>";
                Log(COL_ROLE, "Role*", $"<b>{nm}</b> 역할: {prevRoles[i]} → <b>{m.currentRole}</b> (Esc={currentEscalationLevel})");
                PushEvent("Role", $"{nm}: {prevRoles[i]} → {m.currentRole}", EVT_ROLE);
            }
            Log(COL_ROLE, "Role", $"분배 → Stk={stalkerCount} Dsr={disruptorCount} Str={strikerCount} (Esc={currentEscalationLevel})");
        }

        // ==================================================================
        // 외부 API
        // ==================================================================
        // ═══════════════════════════════════════════════════════════════════
        // ★ v3.7 — BaseAIBrain 일반화 (Humanoid + Mob 모두 등록 가능)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// [v3.7] 그룹에 멤버 등록. Mob + Humanoid 모두 받음.
        /// 떠돌이 개체는 GroupAIManager 미부착 가능 (WorldAISpawnManager.isWandering=true).
        /// </summary>
        public void RegisterMember(BaseAIBrain brain)
        {
            if (brain == null) { LogW("RegisterMember(null) — 무시."); return; }
            members.Add(new GroupMemberState { brain = brain, currentRole = GroupRole.Stalker });
            reinforcementCount++;
            brain.groupMindRef = this;
            brain.RememberSafePosition(brain.transform.position);
            SubscribeAbandonResolver_A12(brain);   // ★ Wk5 B-2 — AbandonResolver 동적 구독
            if (!hasFactionSafeZone) SetFactionSafeZone(brain.transform.position);
            string brainTypeTag = brain is MobAIBrain ? "Mob" : (brain is TDA.PB4.AI.Humanoid.HumanoidAIBrain ? "Humanoid" : "Other");
            Log(COL_MEMBER, "Member", $"등록: {brain.name} ({brainTypeTag}) (총 {members.Count}명)");
            PushEvent("Membership", $"{brain.name} ({brainTypeTag}) 등록 (총 {members.Count}명)", EVT_MEMBERSHIP);
        }

        // ═══════════════════════════════════════════════════════════════════
        // ★ v3.6 — FactionDefinitionSO 통합 API
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// [v3.6] 펙션 정의 SO 단일 적용 — WorldAISpawnManager 가 그룹 결성 직후 호출.
        /// 본 한 호출로 펙션의 모든 정보 자동 부착:
        ///   - GroupPolicy (정책 SO)
        ///   - FactionThresholdConfig (A12 임계 SO)
        ///   - (Wk5+) FactionCulture / FactionTier / 등 추가 가능
        /// </summary>
        public void SetFactionDefinition(FactionDefinitionSO factionDef, MonoBehaviour implementor = null)
        {
            if (factionDef == null) { LogW("SetFactionDefinition(null) — 무시"); return; }

            // 1. 그룹 정책 적용
            SetPolicy(factionDef.GroupPolicy, implementor);

            // 2. ★ A12 임계 SO 자동 부착
            if (factionDef.Threshold != null)
            {
                _factionConfig = factionDef.Threshold;
                Log(COL_A12, "A12-FactionDef",
                    $"임계 SO 자동 부착: {factionDef.Threshold.factionID} ({factionDef.Threshold.factionName})");
            }
            else
                LogW($"SetFactionDefinition: factionDef.Threshold == null (펙션 {factionDef.FactionId})");

            // 3. (Wk5+ 확장 슬롯) — FactionCulture / FactionTier / 등
            // TODO Wk5+: factionDef.Culture / factionDef.Tier 활용

            Log(COL_LIFECYCLE, "FactionDef",
                $"펙션 정의 적용: {factionDef.FactionId} " +
                $"(policy={factionDef.GroupPolicy?.name ?? "null"}, " +
                $"threshold={factionDef.Threshold?.name ?? "null"})");

            PushEvent("Lifecycle", $"펙션 정의 적용: {factionDef.FactionId}", EVT_LIFECYCLE);
        }

        /// <summary>
        /// [v3.6] FactionThresholdConfig 단일 설정 — SetFactionDefinition 미사용 시 fallback.
        /// </summary>
        public void SetFactionConfig(FactionThresholdConfigSO config)
        {
            _factionConfig = config;
            Log(COL_A12, "A12-Config",
                $"임계 SO 직접 설정: {(config != null ? $"{config.factionID} ({config.factionName})" : "null")}");
        }

        public void SetPolicy(FactionGroupPolicySO so, MonoBehaviour implementor = null)
        {
            policySO = so;
            if (implementor != null)
            {
                policyImplementor = implementor;
                if (implementor is Interfaces.Intelligence.IFactionGroupPolicy ip) policy = ip;
                if (so != null)
                {
                    var fld = implementor.GetType().GetField("policySO",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (fld != null && fld.FieldType == typeof(FactionGroupPolicySO))
                        fld.SetValue(implementor, so);
                }
            }
            Log(COL_MORALE, "Policy",
                $"SetPolicy: SO={(so != null ? so.name : "null")} impl={(implementor != null ? implementor.GetType().Name : "null")}");
            PushEvent("Lifecycle",
                $"정책 적용: {(so != null ? so.name : "null")} / {(implementor != null ? implementor.GetType().Name : "null")}",
                EVT_LIFECYCLE);
        }

        /// <summary>[v3.7] 멤버 해제 — BaseAIBrain (Mob + Humanoid).</summary>
        public bool UnregisterMember(BaseAIBrain brain)
        {
            if (brain == null) return false;
            for (int i = members.Count - 1; i >= 0; i--)
            {
                if (members[i].brain == brain)
                {
                    UnsubscribeAbandonResolver_A12(brain);   // ★ Wk5 B-2 — AbandonResolver 해제 (제거 전)
                    members.RemoveAt(i);
                    casualtyCount++;
                    HandleMemberDeath_A12(brain);   // ★ Wk5 B-1 A12-Tracking hook
                    if (brain.groupMindRef == this) brain.groupMindRef = null;
                    Log(COL_MEMBER, "Member", $"해제: {brain.name} (남은 {members.Count}명)");
                    PushEvent("Membership", $"{brain.name} 해제 (남은 {members.Count}명)", EVT_MEMBERSHIP);
                    return true;
                }
            }
            return false;
        }

        [ContextMenu("★ Wk3 Test/Auto-Register All BaseAIBrain in Scene")]
        private void DebugAutoRegisterAll()
        {
            int registered = 0, skipped = 0;
            // ★ v3.7 — Mob + Humanoid 모두 검색
            var allBrains = UnityEngine.Object.FindObjectsByType<BaseAIBrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var brain in allBrains)
            {
                bool already = false;
                for (int i = 0; i < members.Count; i++)
                    if (members[i].brain == brain) { already = true; break; }
                if (already) { skipped++; continue; }
                RegisterMember(brain);
                registered++;
            }
            Debug.Log($"<color=#88FF88>[GroupAI:AutoRegister]</color> {gameObject.name}: 신규 {registered}명, 중복 {skipped}명 → 총 {members.Count}명");
        }

        [ContextMenu("★ Wk3 Test/Clear All Members")]
        private void DebugClearAllMembers()
        {
            int count = members.Count;
            members.Clear();
            Debug.Log($"<color=#FFAA88>[GroupAI:Clear]</color> {gameObject.name}: {count}명 → 0명");
        }

        [ContextMenu("★ Wk3 Test/Print Members")]
        private void DebugPrintMembers()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== {gameObject.name} Members ({members.Count}명) ===");
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.brain == null) { sb.AppendLine($"  [{i}] <null brain>"); continue; }
                string brainType = m.brain is MobAIBrain ? "Mob" : (m.brain is TDA.PB4.AI.Humanoid.HumanoidAIBrain ? "Humanoid" : "?");
                var mob = m.brain as MobAIBrain;
                string mobInfo = mob != null ? $"hasTgt={(mob.currentTarget != null)}" : "(Humanoid)";
                sb.AppendLine($"  [{i}] {m.brain.name} ({brainType}) | role={m.currentRole} | fear={m.brain.fear:F2} | tok={m.hasAttackToken} | {mobInfo}");
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>[v3.7] 멤버 토큰 보유 여부 — BaseAIBrain (Mob + Humanoid).</summary>
        public bool MemberHasAttackToken(BaseAIBrain brain)
        {
            if (brain == null) return false;
            for (int i = 0; i < members.Count; i++)
                if (members[i].brain == brain) return members[i].hasAttackToken;
            return false;
        }

        /// <summary>[v3.7] brain 소속 그룹 검색 — BaseAIBrain (Mob + Humanoid).</summary>
        public static GroupAIManager FindGroupOwning(BaseAIBrain brain)
        {
            if (brain == null) return null;
            var all = UnityEngine.Object.FindObjectsByType<GroupAIManager>(FindObjectsSortMode.None);
            for (int g = 0; g < all.Length; g++)
            {
                var memberList = all[g].members;
                for (int i = 0; i < memberList.Count; i++)
                    if (memberList[i].brain == brain) return all[g];
            }
            return null;
        }

        /// <summary>[v3.7] 공격 통지 — BaseAIBrain (Mob + Humanoid).</summary>
        public bool NotifyMemberAttacked(BaseAIBrain brain)
        {
            if (brain == null) { LogW("NotifyMemberAttacked(null) — 무시."); return false; }
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].brain == brain)
                {
                    float prevTime = members[i].timeSinceLastAttack;
                    int prevCount = members[i].attackCount;
                    members[i].timeSinceLastAttack = 0f;
                    members[i].attackCount++;
                    Log(COL_ATTACK, "TimerReset", $"<b>{brain.name}</b> 타이머: {prevTime:F2}s → 0.00s");
                    Log(COL_ATTACK, "AtkCnt+", $"<b>{brain.name}</b> atkCnt: {prevCount} → <b>{members[i].attackCount}</b>");
                    return true;
                }
            }
            LogW($"NotifyMemberAttacked({brain.name}) — 그룹 멤버에 없음.");
            return false;
        }

        public void ResetAllAttackTimers()
        {
            int loggedCount = 0;
            foreach (var m in members)
            {
                float prev = m.timeSinceLastAttack;
                m.timeSinceLastAttack = 0f;
                if (prev > 0.01f)
                {
                    string nm = m.brain != null ? m.brain.gameObject.name : "<null>";
                    Log(COL_ATTACK, "TimerReset", $"<b>{nm}</b> 일괄 리셋: {prev:F2}s → 0.00s");
                    loggedCount++;
                }
            }
            Log(COL_ATTACK, "Reset", $"모든 타이머 = 0 (총 {members.Count}명, 의미 리셋={loggedCount}명)");
        }

        // ==================================================================
        // Public read-only properties
        // ==================================================================
        public float CurrentMorale => currentMorale;
        public int CurrentEscalationLevel => currentEscalationLevel;
        public int MaxTokenCount => maxTokenCount;
        public IReadOnlyList<GroupMemberState> Members => members;
        public int MemberCount => members.Count;
        public IReadOnlyList<TargetSlot> KnownTargets => _knownTargets;
        public float CombatElapsedTime => combatElapsedTime;
        public FactionGroupPolicySO PolicySO => policySO;
        public MonoBehaviour PolicyImplementor => policyImplementor;
        public float TokenReassignTimeRemaining => Mathf.Max(0f, tokenReassignInterval - tokenTimer);

        public int ActiveMemberCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < members.Count; i++)
                    if (members[i] != null && members[i].brain != null) count++;
                return count;
            }
        }

        // ==================================================================
        // [v3.4] 이벤트 히스토리
        // ==================================================================
        [System.Serializable]
        public struct GroupEvent
        {
            public float relativeTime;
            public string category;
            public string message;
            public Color tagColor;
        }

        private List<GroupEvent> _eventHistory = new List<GroupEvent>();
        private const int MAX_EVENT_HISTORY = 50;
        private float _groupSpawnTime = 0f;

        public IReadOnlyList<GroupEvent> EventHistory => _eventHistory;
        public float GroupAge => _groupSpawnTime > 0f ? Time.time - _groupSpawnTime : 0f;

        private void PushEvent(string category, string message, Color tagColor)
        {
            _eventHistory.Add(new GroupEvent
            {
                relativeTime = GroupAge,
                category = category,
                message = message,
                tagColor = tagColor,
            });
            if (_eventHistory.Count > MAX_EVENT_HISTORY) _eventHistory.RemoveAt(0);
        }

        private static readonly Color EVT_LIFECYCLE = new Color(0.85f, 0.85f, 0.85f);
        private static readonly Color EVT_MEMBERSHIP = new Color(0.5f, 1.0f, 0.5f);
        private static readonly Color EVT_TOKEN = new Color(1.0f, 0.85f, 0.3f);
        private static readonly Color EVT_ROLE = new Color(0.85f, 0.5f, 1.0f);
        private static readonly Color EVT_SENSE = new Color(0.4f, 0.85f, 1.0f);
        private static readonly Color EVT_COMBAT = new Color(1.0f, 0.4f, 0.4f);

        [ContextMenu("★ Wk3 Test/Clear Event History")]
        private void ClearEventHistory()
        {
            _eventHistory.Clear();
            Debug.Log($"[GroupAI:{name}] Event History 초기화됨.");
        }

        // ==================================================================
        // 로깅 헬퍼
        // ==================================================================
        private void Log(string colorHex, string category, string msg)
        {
            if (!debugLog) return;
            Debug.Log($"<color={colorHex}>[GroupAI:{category}]</color> {msg}", this);
        }

        private void LogW(string msg)
        {
            Debug.LogWarning($"[GroupAI:WARN] {msg}", this);
        }

        private static string ShortRole(GroupRole r) => r switch
        {
            GroupRole.Stalker => "Stk",
            GroupRole.Disruptor => "Dsr",
            GroupRole.Striker => "Str",
            _ => "?"
        };

        private static string MoraleLabel(float v)
        {
            if (v >= 0.95f) return "만전";
            if (v >= 0.7f) return "양호";
            if (v >= 0.4f) return "위축";
            if (v >= 0.2f) return "동요";
            return "패닉";
        }

        // ═══════════════════════════════════════════════════════════════════
        // ★ v3.13 B-3 — IGroupCommandReceiver 본체 구현 (시나리오 → AI 단방향 명령 채널)
        //
        //   의제 ID  : B-3 (Wk5_Backlog #3)
        //   호출 경로 : ScenarioArcManager → GroupCommandIssuer
        //               → WorldAIBridgeManager → GroupAIManager.IssueCommand ★ 본 영역
        //   인터페이스 정의: Bridges&Interfaces/Bridges/Contracts/AIBridgeContracts.cs (line 122-128)
        //
        //   ★ 이름 충돌 회피 ★
        //   CommandType 은 본 코드베이스 2 곳에 동명 정의:
        //     1. TDA.PB4.AI.CommandType        (Scripts/pB-4/week5_AI/CommandDirective.cs) — 4 종 (HoldFormation/Scatter/Phalanx/Retreat)
        //     2. TDA.PB4.Interfaces.CommandType (Bridges&Interfaces/.../AIBridgeContracts.cs) — 6 종 (B-3 대상)
        //   GroupAIManager 는 TDA.PB4.AI 안에 있어 동일 namespace 우선 해결 규칙 적용
        //   → 본 region 안 CommandType 참조는 BridgeCommandType (TDA.PB4.Interfaces.CommandType
        //     의 alias, 본 파일 using 영역 정의) 으로 명시.
        //
        //   region 구조 5 종 (총 약 360 줄):
        //     1. B-3 Fields            — _activeCommands 리스트 + 컬러 상수
        //     2. B-3 Interface         — IGroupCommandReceiver 3 메서드
        //     3. B-3 Command Handlers  — 6 종 BridgeCommandType 분기 + 보조 헬퍼
        //     4. B-3 Expiration        — expiresAt 만료 명령 lazy 자동 정리
        //     5. B-3 ContextMenu       — Debug 도구 4 종
        //
        //   본격 동작 영역 (B-3 시점 즉시 본격):
        //     - FocusFire  : 전 멤버 currentTarget 강제 변경 → BT 자율 Stalk/Strike 진행
        //     - Disengage  : 전 멤버 fear=1.0 강제 → BT 효용 평가 시 Flee 자연 선택 (★ 최상위)
        //
        //   scaffolding 영역 (B-3 시점 = 큐 / 분기 / 타겟 위임만, 후속 의제 통합 시점에 보강):
        //     - SplitFire  : 균등 분배 본격 본체 → D 의제 (FormationSlotAllocator) 통합 후
        //     - Mark       : Strike 차단 본격 본체 → BT Action 측 hook 보강 필요 (Wk6+)
        //     - Cover      : Vanguard 슬롯 가중치 본격 본체 → D 의제 통합 후
        //     - Lure       : 노출 위치 BT 액션 본격 본체 → Wk6+ 신규 BT Action 의제
        // ═══════════════════════════════════════════════════════════════════

        #region B-3 Fields

        /// <summary>활성 분대 명령 큐. IssueCommand 추가 / Cancel / 만료 시 제거.</summary>
        private readonly List<SquadCommand> _activeCommands = new List<SquadCommand>();

        /// <summary>B-3 로그 컬러 (시안 — 명령 채널 식별).</summary>
        private const string COL_COMMAND = "#26C6DA";

        /// <summary>B-3 이벤트 태그 컬러 (PushEvent 용).</summary>
        private static readonly Color EVT_COMMAND = new Color(0.15f, 0.78f, 0.85f);

        #endregion

        #region B-3 Interface — IGroupCommandReceiver 3 메서드

        /// <summary>
        /// 분대 명령 발행. 6 종 CommandType 별 핸들러 분기 호출.
        /// 진입 시점에 expiresAt 만료된 기존 명령 자동 정리 (lazy expiry).
        /// </summary>
        public void IssueCommand(SquadCommand cmd)
        {
            if (cmd == null) { LogW("[B-3] IssueCommand: cmd == null. skip."); return; }

            // lazy expiry — 새 명령 진입 시점에 만료 정리
            PurgeExpiredCommands();

            // 발행 시각 (시나리오 측이 안 채웠으면 본 시점 기준)
            if (cmd.gameTime <= 0f) cmd.gameTime = Time.time;

            _activeCommands.Add(cmd);

            // 6 종 분기
            switch (cmd.type)
            {
                case BridgeCommandType.FocusFire: HandleFocusFire(cmd); break;
                case BridgeCommandType.SplitFire: HandleSplitFire(cmd); break;
                case BridgeCommandType.Disengage: HandleDisengage(cmd); break;
                case BridgeCommandType.Mark: HandleMark(cmd); break;
                case BridgeCommandType.Cover: HandleCover(cmd); break;
                case BridgeCommandType.Lure: HandleLure(cmd); break;
                default:
                    LogW($"[B-3] IssueCommand: 알 수 없는 CommandType {cmd.type}. 큐만 등록.");
                    break;
            }

            string tgt = cmd.targetReference != null ? cmd.targetReference.name : "<none>";
            Log(COL_COMMAND, "B-3",
                $"IssueCommand <b>{cmd.type}</b> id={cmd.commandId:N} " +
                $"target={tgt} members={cmd.targetMemberIds.Count} t={cmd.gameTime:F2}");
        }

        /// <summary>commandId 기준 명령 취소. _activeCommands 에서 제거. 성공 시 true.</summary>
        public bool CancelCommand(Guid commandId)
        {
            int removed = _activeCommands.RemoveAll(c => c.commandId == commandId);
            if (removed > 0)
            {
                Log(COL_COMMAND, "B-3", $"CancelCommand id={commandId:N} → {removed} 건 제거");
                return true;
            }
            return false;
        }

        /// <summary>현재 활성 명령 전수 반환. 호출 시점 만료 정리 적용 (lazy expiry).</summary>
        public SquadCommand[] GetActiveCommands()
        {
            PurgeExpiredCommands();
            return _activeCommands.ToArray();
        }

        #endregion

        #region B-3 Command Handlers — 6 종 CommandType 분기 + 보조 헬퍼

        /// <summary>FocusFire — 전 멤버 currentTarget 을 cmd.targetReference 로 강제 변경.</summary>
        private void HandleFocusFire(SquadCommand cmd)
        {
            if (cmd.targetReference == null)
            {
                LogW("[B-3] FocusFire: targetReference == null. skip.");
                return;
            }

            // [Hotfix7 / N-23 후속] 본 그룹 멤버 측 target 차단 — 동료 측 공격 의제 영역
            //   외부 시나리오팀 측 호출 시점 검수 — DebugIssueTestFocusFire 측 fallback 외에도
            //   본격 명령 호출 시 자체 검수 영역 영역.
            if (IsGroupMember(cmd.targetReference))
            {
                LogW($"[B-3] FocusFire: target '{cmd.targetReference.name}' 가 본 그룹 멤버. 거부.");
                return;
            }

            int applied = 0;
            foreach (var m in SelectTargetMembers(cmd))
            {
                if (m?.brain == null) continue;
                SetBrainTarget(m.brain, cmd.targetReference);
                applied++;
            }

            Log(COL_COMMAND, "B-3",
                $"FocusFire → {applied} 명 타겟 <b>{cmd.targetReference.name}</b> 강제 변경");
            PushEvent("B-3", $"FocusFire — {applied} 명 / {cmd.targetReference.name}", EVT_COMMAND);
        }

        /// <summary>
        /// SplitFire — 균등 분배. parameters["targetCount"] = 분할 타겟 수.
        /// B-3 시점 = scaffolding (primary 타겟만 적용). D 의제 (FormationSlotAllocator) 통합 후 본격 보강.
        /// </summary>
        private void HandleSplitFire(SquadCommand cmd)
        {
            int splitCount = (cmd.parameters != null && cmd.parameters.TryGetValue("targetCount", out var c))
                ? Mathf.Max(1, Mathf.RoundToInt(c)) : 1;

            int applied = 0;
            if (cmd.targetReference != null)
            {
                foreach (var m in SelectTargetMembers(cmd))
                {
                    if (m?.brain == null) continue;
                    SetBrainTarget(m.brain, cmd.targetReference);
                    applied++;
                }
            }

            Log(COL_COMMAND, "B-3",
                $"SplitFire scaffolding (split={splitCount}) → {applied} 명 primary 적용 " +
                $"(★ D 의제 통합 후 균등 분배 본격 본체 보강)");
        }

        /// <summary>
        /// Disengage — 전 멤버 fear 강제 1.0 → 효용 평가 시 Flee 자연 선택. ★ 최상위 우선순위.
        /// 팩션별 분기 (FleeSwarm/FleeDuel/FleePhalanx) 는 기존 BT 가 자율 선택.
        /// </summary>
        private void HandleDisengage(SquadCommand cmd)
        {
            int applied = 0;
            foreach (var m in SelectTargetMembers(cmd))
            {
                if (m?.brain == null) continue;
                // BaseAIBrain.fear 강제 — 효용 평가에서 Flee 자연 선택
                m.brain.fear = 1.0f;
                applied++;
            }

            Log(COL_COMMAND, "B-3",
                $"<b>Disengage</b> → {applied} 명 fear=1.0 강제 (Flee BT 액션 자연 발동)");
            PushEvent("B-3", $"Disengage 발행 — {applied} 명 후퇴", new Color(1.0f, 0.4f, 0.4f));
        }

        /// <summary>
        /// Mark — 추적만, 공격 차단. B-3 시점 = 타겟 설정만 적용 (scaffolding).
        /// Strike 차단 본격 본체는 BT Action 측 hook 보강 필요 (Wk6+).
        /// </summary>
        private void HandleMark(SquadCommand cmd)
        {
            if (cmd.targetReference == null)
            {
                LogW("[B-3] Mark: targetReference == null. skip.");
                return;
            }

            int applied = 0;
            foreach (var m in SelectTargetMembers(cmd))
            {
                if (m?.brain == null) continue;
                SetBrainTarget(m.brain, cmd.targetReference);
                applied++;
            }

            Log(COL_COMMAND, "B-3",
                $"Mark scaffolding → {applied} 명 타겟 {cmd.targetReference.name} 추적 " +
                $"(★ Strike 차단 본격 본체 = BT Action hook 보강 필요)");
        }

        /// <summary>
        /// Cover — 특정 아군 보호. B-3 시점 = 큐 등록만 (scaffolding).
        /// 본격 본체 = D 의제 (FormationSlotAllocator 11 슬롯) 통합 후 Vanguard 슬롯 가중치 보강.
        /// </summary>
        private void HandleCover(SquadCommand cmd)
        {
            int memberCount = SelectTargetMembers(cmd).Count();
            string tgt = cmd.targetReference != null ? cmd.targetReference.name : "<none>";
            Log(COL_COMMAND, "B-3",
                $"Cover scaffolding → 보호 대상 {tgt} / 엄호 멤버 {memberCount} 명 " +
                $"(★ D 의제 통합 후 Vanguard 슬롯 가중치 보강)");
        }

        /// <summary>
        /// Lure — 지정 멤버 미끼. B-3 시점 = 큐 등록만 (scaffolding).
        /// 본격 본체 = 노출 위치 점유 BT 액션 신규 (Wk6+).
        /// </summary>
        private void HandleLure(SquadCommand cmd)
        {
            int memberCount = SelectTargetMembers(cmd).Count();
            string tgt = cmd.targetReference != null ? cmd.targetReference.name : "<none>";
            Log(COL_COMMAND, "B-3",
                $"Lure scaffolding → 미끼 멤버 {memberCount} 명 / 유인 대상 {tgt} " +
                $"(★ 노출 위치 BT 액션 본격 본체 보강 필요)");
        }

        // ── 보조 헬퍼 ─────────────────────────────────────────────────────

        /// <summary>
        /// targetMemberIds 비면 전원, 채우면 brain.gameObject.name 매칭 멤버만 반환.
        /// null 멤버 / null brain 자동 제외.
        /// </summary>
        private IEnumerable<GroupMemberState> SelectTargetMembers(SquadCommand cmd)
        {
            if (members == null) yield break;

            bool selectAll = (cmd.targetMemberIds == null || cmd.targetMemberIds.Count == 0);
            foreach (var m in members)
            {
                if (m == null || m.brain == null) continue;
                if (selectAll || cmd.targetMemberIds.Contains(m.brain.gameObject.name))
                    yield return m;
            }
        }

        /// <summary>
        /// BaseAIBrain 의 currentTarget 설정. HumanoidAIBrain / MobAIBrain 자식 클래스별 분기.
        /// ※ currentTarget 은 base 가 아닌 자식 클래스 각자 정의 → type check 필수.
        /// </summary>
        private void SetBrainTarget(BaseAIBrain brain, Transform target)
        {
            if (brain == null) return;
            switch (brain)
            {
                case HumanoidAIBrain h: h.currentTarget = target; break;
                case MobAIBrain m: m.currentTarget = target; break;
                default:
                    LogW($"[B-3] SetBrainTarget: 미지원 brain 타입 {brain.GetType().Name}");
                    break;
            }
        }

        #endregion

        #region B-3 Expiration — expiresAt 만료 명령 lazy 자동 정리

        /// <summary>
        /// expiresAt 도래 명령 _activeCommands 에서 제거.
        /// IssueCommand / GetActiveCommands / ContextMenu 진입 시점에 호출 (lazy expiry).
        /// expiresAt == null → 영구. Time.time &gt;= expiresAt → 만료.
        /// </summary>
        private void PurgeExpiredCommands()
        {
            float now = Time.time;
            int removed = _activeCommands.RemoveAll(c =>
                c.expiresAt.HasValue && now >= c.expiresAt.Value);
            if (removed > 0)
                Log(COL_COMMAND, "B-3", $"PurgeExpiredCommands → {removed} 건 자동 정리 (t={now:F2})");
        }

        #endregion

        #region B-3 ContextMenu — Debug 도구 4 종

        [ContextMenu("★ [B-3] Issue Test FocusFire")]
        private void DebugIssueTestFocusFire()
        {
            // [Hotfix7 / N-23 후속] target 자료 결정 — 본 그룹 멤버 측 차단
            //
            // 문제 영역:
            //   기존 fallback 측 첫 살아있는 멤버 측 currentTarget 무조건 채택.
            //   해당 멤버 측 currentTarget 자료가 같은 그룹 멤버 (또는 자기 자신) 측면일 때
            //   전 멤버 측 SetBrainTarget(자기 동료) → 멤버들이 동료 측 공격 진입 → 허공 공격.
            //
            // 정정:
            //   _knownTargets[0] 측 우선 + 본 그룹 멤버 측 자료 차단 (방어).
            //   fallback 측 멤버 측 currentTarget — 본 그룹 멤버 측 자료 제외 + 다음 멤버 측 시도.
            //   모든 fallback 측 본 그룹 멤버 자료뿐 → 본격 적 없음 → 시뮬 불가.
            Transform t = null;

            // 1) _knownTargets[0] 측 우선 — 본 그룹 멤버 차단
            if (_knownTargets != null && _knownTargets.Count > 0
                && _knownTargets[0].target != null
                && !IsGroupMember(_knownTargets[0].target))
            {
                t = _knownTargets[0].target;
            }

            // 2) fallback 측 멤버 측 currentTarget 추출 — 본 그룹 멤버 차단
            if (t == null && members != null)
            {
                foreach (var m in members)
                {
                    if (m?.brain == null) continue;
                    Transform candidate = ExtractCurrentTarget(m.brain);
                    if (candidate == null) continue;
                    if (IsGroupMember(candidate)) continue;   // ★ 본 그룹 멤버 차단
                    t = candidate;
                    break;
                }
            }

            if (t == null)
            {
                LogW("[B-3] Test FocusFire: 본격 적 자료 없음 " +
                     "(_knownTargets 비어 있음 + 멤버 측 currentTarget 모두 본 그룹 멤버 측면). 시뮬 불가.");
                return;
            }

            IssueCommand(new SquadCommand { type = BridgeCommandType.FocusFire, targetReference = t });
        }

        [ContextMenu("★ [B-3] Issue Test Disengage")]
        private void DebugIssueTestDisengage()
        {
            IssueCommand(new SquadCommand { type = BridgeCommandType.Disengage });
        }

        [ContextMenu("★ [B-3] Print Active Commands")]
        private void DebugPrintActiveCommands()
        {
            PurgeExpiredCommands();
            if (_activeCommands.Count == 0)
            {
                Debug.Log($"[GroupAI:B-3 {name}] Active Commands = 0 종", this);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"═══ [B-3] Active Commands ({_activeCommands.Count} 종) — {name} ═══");
            for (int i = 0; i < _activeCommands.Count; i++)
            {
                var c = _activeCommands[i];
                string exp = c.expiresAt.HasValue ? $"{c.expiresAt.Value:F2}" : "영구";
                string tgt = c.targetReference != null ? c.targetReference.name : "<none>";
                // [W5-B1] members 표시 — targetMemberIds 빈 list = 전 멤버 fallback 시그널
                //   기존: 0 자료 표시 → 적용 멤버 수 0 인 인상 (실제는 SelectTargetMembers 측 전 멤버 fallback)
                //   정합: 빈 list 시 "전 멤버 (N)" 표시 — 실제 영향 멤버 수 명시
                int specifiedCount = c.targetMemberIds != null ? c.targetMemberIds.Count : 0;
                int liveMemberCount = 0;
                if (members != null)
                {
                    foreach (var m in members)
                        if (m?.brain != null) liveMemberCount++;
                }
                string memDisp = specifiedCount == 0
                    ? $"전 멤버 ({liveMemberCount})"
                    : specifiedCount.ToString();
                sb.AppendLine($"  [{i}] {c.type} id={c.commandId:N}");
                sb.AppendLine($"       target={tgt}  members={memDisp}  gameTime={c.gameTime:F2}  exp={exp}");
            }
            sb.AppendLine("═══════════════════════════════════════════════");
            Debug.Log(sb.ToString(), this);
        }

        [ContextMenu("★ [B-3] Cancel All Commands")]
        private void DebugCancelAllCommands()
        {
            int n = _activeCommands.Count;
            _activeCommands.Clear();
            Debug.Log($"[GroupAI:B-3 {name}] Cancel All Commands → {n} 건 제거", this);
        }

        // ── ContextMenu 보조 헬퍼 ─────────────────────────────────────────

        /// <summary>BaseAIBrain 의 현재 currentTarget 읽기 (테스트 ContextMenu 용).</summary>
        private Transform ExtractCurrentTarget(BaseAIBrain brain)
        {
            return brain switch
            {
                HumanoidAIBrain h => h.currentTarget,
                MobAIBrain m => m.currentTarget,
                _ => null,
            };
        }

        /// <summary>
        /// [Hotfix7 / N-23 후속] target Transform 자료가 본 그룹 멤버 측면 검수.
        ///
        /// 본격 정합:
        ///   DebugIssueTestFocusFire 측 fallback / HandleFocusFire 측 호출 가드 양쪽에서 사용.
        ///   본 그룹 멤버 측 자료 → 동료 측 공격 의제 영역 차단.
        ///
        /// 본 시점 — Transform 참조 또는 GameObject 측 자료 매칭. 둘 다 검수 (안전 영역).
        /// </summary>
        private bool IsGroupMember(Transform candidate)
        {
            if (candidate == null || members == null) return false;
            foreach (var m in members)
            {
                if (m?.brain == null) continue;
                if (m.brain.transform == candidate) return true;
                if (m.brain.gameObject == candidate.gameObject) return true;
            }
            return false;
        }

        #endregion
    }
}