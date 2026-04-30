// =============================================================================
// GroupAIManager.cs  |  pB-4 Project — Week 3
// Layer  : L2 Router (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   팩션별 군집 AI의 코디네이터. 이 클래스 자체는 50줄 이내의 얇은 디스패처로,
//   실제 팩션별 로직(사기/에스컬레이션/역할전이)은 IFactionGroupPolicy 구현체에 위임.
//   새 팩션을 추가해도 이 클래스는 수정하지 않는다.
//
//   핵심 시스템:
//   1. 공격 토큰: max_active_tokens = (ally_count × 1.2 + 1). 순환 대기.
//      한 번에 모든 몹이 공격하는 것을 방지. 나머지는 위협/포위/방해 행동.
//   2. 역할 전이: Stalker(감시) → Disruptor(방해) → Striker(공격).
//      roleTransitionRules가 팩션별로 다르게 동작.
//   3. 사기 관리: 팩션별 EvaluateMorale()을 주기적으로 호출.
//
// 연계:
//   - MobAIBrain: GroupAI가 결정한 역할/토큰을 개별 몹에 전달
//   - EventBus.OnEscalationTriggered: 에스컬레이션 레벨 변경 시 발행
//   - FactionGroupPolicySO: 팩션별 정책 파라미터 참조
//
// [v3.2 D3 갱신 — 2026-04-29]
//   ★ IGroupAIInfo (TDA.PB4.Interfaces) 인터페이스 구현 추가:
//     - 기존 currentMorale (private SerializeField) → GetMorale() 노출
//     - 기존 activeTokenCount > 0 → HasAvailableToken() 노출
//     ContextManager.Sample()가 본 인터페이스 통해 그룹 상태 조회.
//     Week3Bootstrapper.FindGroupAIInfo()가 자동 발견 (FindObjectsOfType
//     순회 시 IGroupAIInfo 캐스팅 성공).
//
//   ★ 기존 로직은 변경 없음 (Wk1 동결 영역 보존).
//     기존 외부 API (CurrentMorale, ActiveTokenCount 등 read-only 프로퍼티)도 그대로 유지.
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Data;
using TDA.PB4.AI.Mob;
using TDA.PB4.Interfaces;  // ★ v3.2 추가: IGroupAIInfo

namespace TDA.PB4.AI
{
    /// <summary>
    /// 개별 몹의 GroupAI 상태를 추적하는 데이터.
    /// </summary>
    [Serializable]
    public class GroupMemberState
    {
        [Tooltip("이 몹의 MobAIBrain 참조")]
        public MobAIBrain brain;

        [Tooltip("현재 할당된 역할. Stalker(감시)→Disruptor(방해)→Striker(공격)")]
        public GroupRole currentRole = GroupRole.Stalker;

        [Tooltip("현재 공격 토큰을 보유하고 있는지 여부. " +
                 "true이면 공격 가능, false이면 위협/포위만 수행")]
        public bool hasAttackToken = false;

        [Tooltip("마지막 공격 이후 경과 시간. 토큰 할당 우선순위에 사용")]
        public float timeSinceLastAttack = 0f;
    }

    public enum GroupRole
    {
        /// <summary>원거리에서 플레이어를 추적/감시. 2~3명 배정.</summary>
        Stalker,
        /// <summary>중거리에서 시야 차단/경로 방해. 3~4명 배정.</summary>
        Disruptor,
        /// <summary>근거리 직접 공격. 나머지 전원.</summary>
        Striker
    }

    /// <summary>
    /// ★ v3.2: IGroupAIInfo 인터페이스 구현 추가.
    ///   - GetMorale()           : ContextManager가 그룹 사기 조회
    ///   - HasAvailableToken()   : ContextManager가 공격 토큰 가용성 조회
    /// </summary>
    public class GroupAIManager : MonoBehaviour, IGroupAIInfo
    {
        [Header("━━━ 팩션 정책 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("이 그룹이 사용하는 팩션 정책 SO. " +
                 "panicChainMultiplier, escalationMode, formationTemplate 등을 정의. " +
                 "이 SO에 따라 SwarmGroupPolicy/DuelGroupPolicy/PhalanxGroupPolicy 중 하나가 사용됨.")]
        [SerializeField] private FactionGroupPolicySO policySO;

        [Tooltip("이 그룹의 IFactionGroupPolicy 구현체. " +
                 "같은 GameObject 또는 자식 오브젝트에서 자동 탐색. " +
                 "못 찾으면 Inspector에서 직접 할당.")]
        [SerializeField] private MonoBehaviour policyImplementor;

        [Header("━━━ 그룹 멤버 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("이 그룹에 속한 몹 목록. Inspector에서 직접 할당하거나 " +
                 "RegisterMember()로 런타임에 추가.")]
        [SerializeField] private List<GroupMemberState> members = new List<GroupMemberState>();

        [Header("━━━ 공격 토큰 시스템 ━━━━━━━━━━━━━━━━━")]
        [Tooltip("동시에 공격 가능한 최대 인원 배수. " +
                 "실제 토큰 수 = (멤버 수 × 이 값 + 1). " +
                 "1.2 = 5명 그룹이면 최대 7명 동시 공격 가능. " +
                 "0.5 = 5명 그룹이면 최대 3명만 공격. 나머지는 포위/방해.")]
        [Range(0.1f, 3f)]
        public float tokenMultiplier = 1.2f;

        [Tooltip("토큰 재할당 주기 (초). 이 간격마다 토큰이 순환 재분배됨.")]
        [Range(0.5f, 5f)]
        public float tokenReassignInterval = 2.0f;

        [Header("━━━ 사기/에스컬레이션 ━━━━━━━━━━━━━━━━")]
        [Tooltip("현재 그룹 사기 (0.0=전원 도주, 1.0=최고 사기). " +
                 "EvaluateMorale()로 갱신됨. Read Only.")]
        [Range(0f, 1f)]
        [SerializeField] private float currentMorale = 1.0f;

        [Tooltip("현재 에스컬레이션 레벨 (0=관찰, 1=약점포착, 2=포위압착, 3=전면학살). " +
                 "DecideEscalation()으로 갱신됨. Read Only.")]
        [SerializeField] private int currentEscalationLevel = 0;

        [Tooltip("전투 시작 이후 경과 시간. TimeGated 에스컬레이션에 사용.")]
        [SerializeField] private float combatElapsedTime = 0f;

        [Header("━━━ 포위 진척도 (Phalanx용) ━━━━━━━━━━")]
        [Tooltip("스켈레톤 Phalanx 전용. 포위 완성도 (0.0~1.0). " +
                 "1.0이면 포위 완성 → EncircleGated 에스컬레이션 발동.")]
        [Range(0f, 1f)]
        public float encircleProgress = 0f;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("GroupAI 의사결정 과정을 Console에 출력")]
        public bool debugLog = false;

        [Tooltip("현재 활성 공격 토큰 수 (Read Only)")]
        [SerializeField] private int activeTokenCount = 0;

        [Tooltip("최대 공격 토큰 수 (Read Only)")]
        [SerializeField] private int maxTokenCount = 0;

        // 내부 참조
        private Interfaces.Intelligence.IFactionGroupPolicy policy;
        private float tokenTimer = 0f;
        private float moraleTimer = 0f;

        // ==================================================================
        // ★ v3.2 IGroupAIInfo 구현 — D3 통합 검증용 (2 메서드)
        // ContextManager.Sample()이 본 메서드들 호출.
        // ==================================================================

        /// <summary>
        /// [IGroupAIInfo] 그룹 사기 (0~1).
        /// 1.0 = 만전, 0.0 = 패주.
        /// 빈 그룹 (멤버 0명)도 currentMorale 초기값 1.0 반환 → 안전.
        /// </summary>
        public float GetMorale() => currentMorale;

        /// <summary>
        /// [IGroupAIInfo] 공격 토큰 가용성.
        /// activeTokenCount > 0 → true (현재 어떤 멤버가 토큰 보유).
        /// 빈 그룹은 activeTokenCount=0 → false.
        /// 단, D3 단위 검증 시 멤버 등록 전에는 false 반환됨에 주의.
        /// (멤버 등록 후 ReassignAttackTokens()이 1회 실행되어야 토큰 발급)
        /// </summary>
        public bool HasAvailableToken() => activeTokenCount > 0;

        // ==================================================================
        // Lifecycle
        // ==================================================================
        private void Awake()
        {
            // IFactionGroupPolicy 구현체 탐색
            if (policyImplementor != null)
                policy = policyImplementor as Interfaces.Intelligence.IFactionGroupPolicy;

            if (policy == null)
            {
                // 같은 오브젝트 또는 자식에서 탐색
                policy = GetComponentInChildren<Interfaces.Intelligence.IFactionGroupPolicy>();
            }

            if (policy == null && debugLog)
                Debug.LogWarning("[GroupAI] IFactionGroupPolicy 구현체를 찾을 수 없습니다. " +
                                 "SwarmGroupPolicy/DuelGroupPolicy/PhalanxGroupPolicy 중 하나를 부착하세요.");
        }

        private void Update()
        {
            if (members.Count == 0) return;

            combatElapsedTime += Time.deltaTime;

            // 사기 평가 (1초마다)
            moraleTimer += Time.deltaTime;
            if (moraleTimer >= 1.0f)
            {
                moraleTimer = 0f;
                UpdateMorale();
                UpdateEscalation();
            }

            // 토큰 재분배
            tokenTimer += Time.deltaTime;
            if (tokenTimer >= tokenReassignInterval)
            {
                tokenTimer = 0f;
                ReassignAttackTokens();
            }

            // 멤버 타이머 갱신
            foreach (var m in members)
            {
                if (m.brain != null)
                    m.timeSinceLastAttack += Time.deltaTime;
            }
        }

        // ==================================================================
        // 공격 토큰 시스템
        // max_active_tokens = (ally_count × tokenMultiplier + 1)
        // 토큰 할당 우선순위: 거리(가까울수록) > 마지막 공격 이후 시간(길수록) 
        // ==================================================================
        private void ReassignAttackTokens()
        {
            maxTokenCount = Mathf.FloorToInt(members.Count * tokenMultiplier + 1);

            // 모든 토큰 회수
            foreach (var m in members) m.hasAttackToken = false;
            activeTokenCount = 0;

            // 우선순위 정렬: timeSinceLastAttack 내림차순 (오래 대기한 몹 우선)
            var sorted = new List<GroupMemberState>(members);
            sorted.Sort((a, b) => b.timeSinceLastAttack.CompareTo(a.timeSinceLastAttack));

            // 토큰 할당
            for (int i = 0; i < sorted.Count && activeTokenCount < maxTokenCount; i++)
            {
                if (sorted[i].brain == null) continue;
                sorted[i].hasAttackToken = true;
                activeTokenCount++;
            }

            if (debugLog)
                Debug.Log($"[GroupAI] 토큰 재분배: {activeTokenCount}/{maxTokenCount} (멤버={members.Count})");
        }

        // ==================================================================
        // 사기 관리: IFactionGroupPolicy.EvaluateMorale() 호출
        // ==================================================================
        private void UpdateMorale()
        {
            if (policy == null) return;

            // 가장 최근 사망/도주한 멤버의 손실 계수
            float recentLoss = 0f;
            foreach (var m in members)
            {
                if (m.brain == null) { recentLoss += 0.2f; continue; } // 사망한 멤버
                if (m.brain.CurrentState == MobBTState.Flee) recentLoss += 0.1f;
            }

            float newMorale = policy.EvaluateMorale(currentMorale, recentLoss, 1.0f);
            currentMorale = Mathf.Clamp01(newMorale);

            // 사기 0.2 미만 → 전원 도주 패닉 체인
            if (currentMorale < 0.2f)
            {
                foreach (var m in members)
                {
                    if (m.brain != null)
                        m.brain.fear = Mathf.Min(1f, m.brain.fear + 0.3f);
                }
                policy.HandlePanicChain(-1, policySO != null ? policySO.panicChainMultiplier : 1f);
            }
        }

        // ==================================================================
        // 에스컬레이션: IFactionGroupPolicy.DecideEscalation() 호출
        // ==================================================================
        private void UpdateEscalation()
        {
            if (policy == null) return;

            float threatLevel = 1f - currentMorale; // 사기 낮을수록 위협 높음
            int newLevel = policy.DecideEscalation(combatElapsedTime, threatLevel, encircleProgress);

            if (newLevel != currentEscalationLevel)
            {
                currentEscalationLevel = newLevel;
                string factionId = policySO != null ? policySO.factionId : "unknown";
                EventBus.RaiseEscalationTriggered(factionId, currentEscalationLevel);

                if (debugLog)
                    Debug.Log($"[GroupAI] 에스컬레이션 레벨 변경: {currentEscalationLevel} (팩션={factionId})");

                // 역할 전이: 레벨에 따라 Stalker→Disruptor→Striker 비율 변경
                UpdateRoleDistribution();
            }
        }

        // ==================================================================
        // 역할 전이: Stalker(감시) → Disruptor(방해) → Striker(공격)
        // 에스컬레이션 레벨에 따라 비율 자동 조정
        // ==================================================================
        private void UpdateRoleDistribution()
        {
            int count = members.Count;
            if (count == 0) return;

            int strikerCount, disruptorCount, stalkerCount;

            switch (currentEscalationLevel)
            {
                case 0: // 관찰: Stalker 위주
                    stalkerCount = Mathf.Max(2, count * 2 / 3);
                    disruptorCount = Mathf.Max(0, count - stalkerCount);
                    strikerCount = 0;
                    break;
                case 1: // 약점 포착: Disruptor 증가
                    stalkerCount = Mathf.Max(1, count / 4);
                    disruptorCount = Mathf.Max(2, count / 2);
                    strikerCount = count - stalkerCount - disruptorCount;
                    break;
                case 2: // 포위 압착: Striker 증가
                    stalkerCount = 1;
                    disruptorCount = Mathf.Max(1, count / 4);
                    strikerCount = count - stalkerCount - disruptorCount;
                    break;
                case 3: // 전면 학살: 전원 Striker
                default:
                    stalkerCount = 0; disruptorCount = 0;
                    strikerCount = count;
                    break;
            }

            int idx = 0;
            for (int i = 0; i < stalkerCount && idx < count; i++, idx++)
                members[idx].currentRole = GroupRole.Stalker;
            for (int i = 0; i < disruptorCount && idx < count; i++, idx++)
                members[idx].currentRole = GroupRole.Disruptor;
            for (; idx < count; idx++)
                members[idx].currentRole = GroupRole.Striker;

            if (debugLog)
                Debug.Log($"[GroupAI] 역할 분배: Stalker={stalkerCount}, Disruptor={disruptorCount}, Striker={strikerCount} (Esc={currentEscalationLevel})");
        }

        // ==================================================================
        // 외부 API
        // ==================================================================

        /// <summary>그룹에 멤버를 런타임 등록.</summary>
        public void RegisterMember(MobAIBrain brain)
        {
            members.Add(new GroupMemberState { brain = brain, currentRole = GroupRole.Stalker });
            if (debugLog) Debug.Log($"[GroupAI] 멤버 등록: {brain.name} (총 {members.Count}명)");
        }

        /// <summary>현재 사기. 외부에서 읽기 전용.</summary>
        public float CurrentMorale => currentMorale;
        /// <summary>현재 에스컬레이션 레벨.</summary>
        public int CurrentEscalationLevel => currentEscalationLevel;
        /// <summary>활성 토큰 수.</summary>
        public int ActiveTokenCount => activeTokenCount;
        /// <summary>최대 토큰 수.</summary>
        public int MaxTokenCount => maxTokenCount;
        /// <summary>멤버 목록.</summary>
        public IReadOnlyList<GroupMemberState> Members => members;
    }
}
