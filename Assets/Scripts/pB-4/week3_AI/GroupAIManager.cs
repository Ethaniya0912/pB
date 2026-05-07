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
//   3. 사기 관리: 팩션별 EvaluateMorale()을 주기적으로 호출.
//
// 연계:
//   - MobAIBrain: GroupAI가 결정한 역할/토큰을 개별 몹에 전달
//   - EventBus.OnEscalationTriggered: 에스컬레이션 레벨 변경 시 발행
//   - FactionGroupPolicySO: 팩션별 정책 파라미터 참조
//
// [v3.2 D3 갱신 — 2026-04-29]
//   ★ IGroupAIInfo (TDA.PB4.Interfaces) 인터페이스 구현 추가.
//
// [v3.3 진단 강화 — 2026-05-06]
//   ★ Bug 1: timeSinceLastAttack 미초기화 문제 수정
//     - NotifyMemberAttacked(MobAIBrain) public API 추가.
//     - GroupMemberState.attackCount 진단 필드 추가.
//
//   ★ Bug 2: activeTokenCount/maxTokenCount 인스펙터 편집 무효화 문제
//     - 본래 의도는 read-only 상태 표시. 2초마다 ReassignAttackTokens()이 덮어쓰므로
//       인스펙터 편집은 무효 (다음 재분배 시점에 사라짐).
//     - 해결: Tooltip에 "편집 금지 — 자동 덮어쓰기" 명시.
//       (Unity 기본 인스펙터에는 read-only 표시 기능이 없으므로 문서화로 대체)
//
//   ★ Bug 3: 로그 미흡 → 디버깅 강화
//     - debugLog 플래그로 모든 진단 로그 일괄 ON/OFF.
//     - verboseTokenLog: 토큰 재분배 시 정렬·할당 표 출력.
//     - logFrameTimers: 매 프레임 타이머 누적 (단발용).
//     - 모든 직접 Debug.Log를 Log()/LogW() 헬퍼로 대체.
//
// [v3.3.2 — 2026-05-06]
//   ★ 외부 의존 일체 제거. 본 .cs 파일 단독으로 동작.
//     커스텀 에디터, 어트리뷰트, 드로어 모두 사용 안 함.
//     read-only 필드는 Tooltip으로만 안내 (Unity 기본 동작 그대로 보이지만 편집 금지).
//
// [v3.3.3 SO 토큰 오버라이드 활성화 — 2026-05-06]
//   ★ FactionGroupPolicySO의 dead code였던 tokenOverrideEnabled / tokenOverrideThreshold
//     를 ReassignAttackTokens()에서 실제로 소비하도록 배선.
//   ★ 의미론: tokenOverrideEnabled=true → maxTokenCount = floor(threshold)
//             (= 그 그룹이 발행할 수 있는 최대 토큰 수의 상한값. tokenMultiplier·members.Count 무시)
//   ★ 팩션별 활용 예:
//       - Phalanx(스켈레톤): enabled=true, threshold=N → 편성 무관 고정 N대 동시 공격
//       - Duel(오크): enabled=true, threshold=1 → 1:1 명예 결투 강제
//       - Swarm(고블린): enabled=false → 기존 공식 그대로 (혼란스러운 군집)
//   ★ 로그도 OVERRIDE 분기를 표시하도록 업데이트.
//
// [v3.3.4 캐릭터별 이벤트 로깅 — 2026-05-06]
//   ★ 5가지 이벤트를 캐릭터별 단일 로그로 추적 (각 이벤트당 1줄, 캐릭터 이름 포함):
//       (1) Token+      : 토큰 획득 — ReassignAttackTokens에서 hasAttackToken=false→true diff
//       (2) Token-      : 토큰 회수 — hasAttackToken=true→false diff
//       (3) Role*       : 역할 변경 — UpdateRoleDistribution에서 currentRole diff
//       (4) TimerReset  : 공격 타이머 리셋 — NotifyMemberAttacked / ResetAllAttackTimers
//       (5) AtkCnt+     : 공격 카운트 증가 — NotifyMemberAttacked의 attackCount++ 분리 로그
//   ★ 모두 debugLog 플래그로 일괄 ON/OFF (verboseTokenLog 무관).
//   ★ diff 기반이라 변화 없는 멤버는 자동으로 로그 생략 → 콘솔 노이즈 최소화.
//
// [v3.3.5 요약 로그 가독성 개선 — 2026-05-06]
//   ★ Tokens 요약 로그에 토큰 보유자 이름 명시.
//     diff 로그(Token+/Token-)는 변화가 있을 때만 출력되므로 같은 멤버가 계속 보유 시
//     누가 가지고 있는지 추적 불가했던 문제 해결.
//   ★ Morale 로그를 자기 설명적인 한글 형식으로 재작성:
//       - 사기 수치 → 한글 레이블 (만전/양호/위축/동요/패닉)
//       - 변화 표시 (▲ 상승 / ▼ 하락 / 변화 없음)
//       - 손실 페널티 산식 인라인 표기 (사망 N명×0.2 + 도주 N명×0.1)
//
// [v3.3.6 외부 토큰 게이트 API — 2026-05-06]
//   ★ StrikeAction 등 외부 BT 노드가 토큰 시스템과 연동할 수 있도록 2개 API 추가:
//       - public bool MemberHasAttackToken(MobAIBrain brain) : 토큰 보유 여부 조회
//       - public static GroupAIManager FindGroupOwning(MobAIBrain) : brain → 소속 그룹 검색
//   ★ 동기 — 인스펙터 검증으로 토큰 시스템과 StrikeAction이 미배선 상태 발견:
//       (Combat 256s, atkCnt=0, tSinceAtk=256s for all 3 members
//        = NotifyMemberAttacked 호출 0회 = 토큰 시스템 사실상 무동작)
//   ★ StrikeAction 측에서는 OnStart에서 1회 캐싱 후 사용. 매 프레임 호출 금지.
//   ★ 그룹 미소속 mob은 _groupAI=null → 토큰 검사 자동 스킵 → 기존 동작 유지.
//
// [v3.3.7 토큰 자격 검사 (P0) — 2026-05-07]
//   ★ 동기 — Issues Report v3 §3 P0 발견: ReassignAttackTokens가 brain==null만 검사.
//       굳음/도주/inactive/사망 멤버에게 토큰 부여 → 그룹 마비 가능.
//   ★ 신규 — IsAttackEligible(GroupMemberState m) 메서드:
//       토큰 부여 자격 5종 검사 (brain null / inactive / dead / Flee / 굳음).
//   ★ 변경 — ReassignAttackTokens:
//       sorted 정렬 전 eligible 필터링 → 자격 미달 멤버는 토큰 후보에서 제외.
//       자격 미달 카운트를 로그에 출력하여 진단 가능.
//   ★ 보류 — currentTarget == null / CurrentState == Idle 검사:
//       §4 Shared Target 패치 시 sharedTarget OR 조건으로 함께 추가 예정.
//       현재는 시야 부족만으로 자격 박탈하면 그룹 전체 토큰 후보 0이 될 위험.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Data;
using TDA.PB4.AI.Mob;
using TDA.PB4.Interfaces;       // IGroupAIInfo
using TDA.Character.AI;          // [v3.3.7] AICharacterManager — 굳음 진단용

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

        [Tooltip("[표시 전용/편집 금지] 현재 공격 토큰 보유 여부. " +
                 "ReassignAttackTokens()가 매 주기 자동 갱신.")]
        public bool hasAttackToken = false;

        [Tooltip("[표시 전용/편집 금지] 마지막 공격 이후 경과 시간(초). " +
                 "토큰 할당 우선순위에 사용. " +
                 "GroupAIManager.NotifyMemberAttacked() 호출 시 0으로 리셋된다. " +
                 "인스펙터에서 직접 편집해도 다음 프레임에 +Time.deltaTime으로 갱신됨.")]
        public float timeSinceLastAttack = 0f;

        // ---------------- v3.3 진단 필드 ----------------
        [Tooltip("[표시 전용/진단용] 공격 누적 횟수. NotifyMemberAttacked 호출 시 +1. " +
                 "0으로 머물러 있으면 → 공격 노티피케이션 배선이 누락된 상태. " +
                 "인스펙터에서 수동 편집해도 다음 통지 시 +1로 덮어써짐.")]
        public int attackCount = 0;
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
    /// ★ v3.2: IGroupAIInfo 인터페이스 구현.
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
                 "실제 토큰 수 = floor(멤버 수 × 이 값 + 1). " +
                 "1.2 = 5명 그룹이면 최대 7명 동시 공격 가능. " +
                 "0.5 = 5명 그룹이면 최대 3명만 공격. 나머지는 포위/방해. " +
                 "[v3.3.3] policySO.tokenOverrideEnabled=true이면 이 값 무시됨 (SO threshold가 우선).")]
        [Range(0.1f, 3f)]
        public float tokenMultiplier = 1.2f;

        [Tooltip("토큰 재할당 주기 (초). 이 간격마다 토큰이 순환 재분배됨.")]
        [Range(0.5f, 5f)]
        public float tokenReassignInterval = 2.0f;

        [Header("━━━ 사기/에스컬레이션 (런타임 자동 갱신) ━━")]
        [Tooltip("[표시 전용/편집 금지] 현재 그룹 사기 (0.0=전원 도주, 1.0=최고 사기). " +
                 "EvaluateMorale()로 매초 자동 갱신. " +
                 "인스펙터에서 편집해도 다음 1초 주기에 정책 계산값으로 덮어써짐.")]
        [Range(0f, 1f)]
        [SerializeField] private float currentMorale = 1.0f;

        [Tooltip("[표시 전용/편집 금지] 현재 에스컬레이션 레벨 " +
                 "(0=관찰, 1=약점포착, 2=포위압착, 3=전면학살). " +
                 "DecideEscalation()으로 매초 자동 갱신.")]
        [SerializeField] private int currentEscalationLevel = 0;

        [Tooltip("[표시 전용/편집 금지] 전투 시작 이후 경과 시간(초). " +
                 "Update()에서 매 프레임 +Time.deltaTime.")]
        [SerializeField] private float combatElapsedTime = 0f;

        [Header("━━━ 포위 진척도 (Phalanx용) ━━━━━━━━━━")]
        [Tooltip("스켈레톤 Phalanx 전용. 포위 완성도 (0.0~1.0). " +
                 "1.0이면 포위 완성 → EncircleGated 에스컬레이션 발동.")]
        [Range(0f, 1f)]
        public float encircleProgress = 0f;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("[v3.3] 마스터 토글. 본 클래스의 모든 진단 로그를 일괄 ON/OFF. " +
                 "OFF 상태에서는 Warning/Error를 제외한 모든 로그가 억제된다.")]
        public bool debugLog = false;

        [Tooltip("[v3.3] 토큰 재분배의 멤버별 상세 표 출력 (debugLog=true일 때만 동작). " +
                 "정렬된 우선순위 / 토큰 보유 / 공격 누적 / 마지막 공격 후 경과 시간을 모두 출력.")]
        public bool verboseTokenLog = false;

        [Tooltip("[v3.3] 매 프레임 타이머 누적값 출력 (debugLog=true일 때만). " +
                 "성능 영향 큼. 토큰 주기/사기 주기 디버깅 시에만 단발로 켤 것.")]
        public bool logFrameTimers = false;

        [Tooltip("[표시 전용/편집 금지] 현재 활성 공격 토큰 수. " +
                 "ReassignAttackTokens()가 매 tokenReassignInterval 주기에 자동 갱신. " +
                 "인스펙터에서 편집해도 다음 재분배 시점(최대 tokenReassignInterval초 이내)에 자동 덮어쓰기됨. " +
                 "이 필드는 진단용 표시일 뿐, 직접 수정 의도 없음.")]
        [SerializeField] private int activeTokenCount = 0;

        [Tooltip("[표시 전용/편집 금지] 최대 공격 토큰 수 (= 발행 가능한 최대치). " +
                 "기본 공식: floor(members.Count × tokenMultiplier + 1). " +
                 "[v3.3.3] policySO.tokenOverrideEnabled=true이면 floor(policySO.tokenOverrideThreshold)로 강제 (상한). " +
                 "ReassignAttackTokens()가 매 주기 자동 재계산. " +
                 "인스펙터에서 편집해도 다음 재분배 시점에 자동 덮어쓰기됨.")]
        [SerializeField] private int maxTokenCount = 0;

        // 내부 참조
        private Interfaces.Intelligence.IFactionGroupPolicy policy;
        private float tokenTimer = 0f;
        private float moraleTimer = 0f;

        // 로그 카테고리 색상 (Unity Console rich-text)
        private const string COL_TOKEN = "#FFD54F"; // 노랑
        private const string COL_ROLE = "#4DD0E1"; // 시안
        private const string COL_MORALE = "#FF8A65"; // 주황
        private const string COL_ESCAL = "#BA68C8"; // 보라
        private const string COL_MEMBER = "#81C784"; // 초록
        private const string COL_ATTACK = "#FFB74D"; // 황색
        private const string COL_LIFECYCLE = "#90A4AE"; // 회색

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
        /// </summary>
        public bool HasAvailableToken() => activeTokenCount > 0;

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
                LogW($"IFactionGroupPolicy 구현체를 찾을 수 없습니다. " +
                     $"SwarmGroupPolicy/DuelGroupPolicy/PhalanxGroupPolicy 중 하나를 부착하세요.");
            else
                Log(COL_LIFECYCLE, "Awake",
                    $"정책 바인딩 완료: {policy.GetType().Name} " +
                    $"(SO={(policySO != null ? policySO.name : "<null>")})");
        }

        public void OnEnable()
        {
            // IFactionGroupPolicy 구현체 Awake에서 탐색 실패시 재시도
            if (policy == null)
            {
                if (policyImplementor != null)
                    policy = policyImplementor as Interfaces.Intelligence.IFactionGroupPolicy;

                if (policy == null)
                    policy = GetComponentInChildren<Interfaces.Intelligence.IFactionGroupPolicy>();

                if (policy == null)
                    LogW("OnEnable 시점에도 IFactionGroupPolicy 구현체 미발견.");
                else
                    Log(COL_LIFECYCLE, "OnEnable",
                        $"정책 지연 바인딩: {policy.GetType().Name}");
            }
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

            if (debugLog && logFrameTimers)
            {
                Log(COL_LIFECYCLE, "Tick",
                    $"tokenTimer={tokenTimer:F2}/{tokenReassignInterval:F2} " +
                    $"moraleTimer={moraleTimer:F2}/1.00 " +
                    $"combat={combatElapsedTime:F1}s members={members.Count}");
            }
        }

        // ==================================================================
        // 공격 토큰 시스템
        // 기본 공식: max_active_tokens = floor(ally_count × tokenMultiplier + 1)
        //
        // [v3.3.3] SO 오버라이드:
        //   policySO.tokenOverrideEnabled = true 인 경우
        //   maxTokenCount = floor(policySO.tokenOverrideThreshold) 로 강제 (최대 발행량 상한).
        //   tokenMultiplier 와 members.Count 영향 무시.
        //   팩션별 시나리오:
        //     - Phalanx(스켈레톤): 고정 N대 동시 공격
        //     - Duel(오크): threshold=1 → 1:1 명예 결투
        //     - Swarm(고블린): enabled=false → 기본 공식 사용
        //
        // 토큰 할당 우선순위: timeSinceLastAttack 내림차순 (오래 대기한 몹 우선)
        // ==================================================================
        private void ReassignAttackTokens()
        {
            int prevMax = maxTokenCount;
            int prevActive = activeTokenCount;

            // [v3.3.3] SO 오버라이드 분기
            // tokenOverrideEnabled=true → SO의 threshold가 발행 가능 최대 토큰 수.
            // 음수 방지를 위해 Mathf.Max(0, …) 적용.
            bool useOverride = policySO != null && policySO.tokenOverrideEnabled;
            if (useOverride)
            {
                maxTokenCount = Mathf.Max(0, Mathf.FloorToInt(policySO.tokenOverrideThreshold));
            }
            else
            {
                maxTokenCount = Mathf.FloorToInt(members.Count * tokenMultiplier + 1);
            }

            // [v3.3.4] 토큰 변경 감지를 위해 이전 상태 캡처 (멤버 인덱스와 1:1 대응)
            var prevTokenStates = new bool[members.Count];
            for (int i = 0; i < members.Count; i++)
                prevTokenStates[i] = members[i].hasAttackToken;

            // 모든 토큰 회수
            foreach (var m in members) m.hasAttackToken = false;
            activeTokenCount = 0;

            // [v3.3.7] 자격 검사 — 토큰 줄 수 있는 멤버만 후보로 분리
            //   §3 P0 패치: brain==null 만 검사하던 기존 로직 → 5종 자격 검사로 확장.
            //   사망/도주/굳음/inactive 멤버에게 토큰 부여하던 마비 시나리오 차단.
            //   currentTarget==null 검사는 §4 sharedTarget 패치 시 추가 예정.
            var eligible = new List<GroupMemberState>();
            int ineligibleCount = 0;
            var ineligibleReasons = new StringBuilder(64);
            foreach (var m in members)
            {
                string reason;
                if (IsAttackEligible(m, out reason))
                {
                    eligible.Add(m);
                }
                else
                {
                    ineligibleCount++;
                    if (ineligibleReasons.Length > 0) ineligibleReasons.Append(", ");
                    string nm = m.brain != null ? m.brain.gameObject.name : "<null>";
                    ineligibleReasons.Append($"{nm}({reason})");
                }
            }

            // 우선순위 정렬: timeSinceLastAttack 내림차순 (오래 대기한 몹 우선)
            eligible.Sort((a, b) => b.timeSinceLastAttack.CompareTo(a.timeSinceLastAttack));

            // 토큰 할당 — 자격 있는 후보만
            var awarded = new List<GroupMemberState>();
            for (int i = 0; i < eligible.Count && activeTokenCount < maxTokenCount; i++)
            {
                eligible[i].hasAttackToken = true;
                activeTokenCount++;
                awarded.Add(eligible[i]);
            }

            // [v3.3.7] 자격 미달 진단 로그
            if (ineligibleCount > 0)
            {
                Log(COL_TOKEN, "Tokens",
                    $"⚠ 자격 미달 {ineligibleCount}/{members.Count}명 (토큰 후보 제외) → " +
                    ineligibleReasons.ToString());
            }

            // [v3.3.4] 캐릭터별 토큰 변경 로그 (이전 vs 현재 diff)
            // sorted[i]는 members[j]와 동일한 인스턴스 참조이므로
            // members 인덱스 기준으로 diff하면 정확한 변경 감지 가능.
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                bool prev = prevTokenStates[i];
                bool now = m.hasAttackToken;
                if (prev == now) continue;  // 변화 없음 → 로그 생략

                string nm = m.brain != null ? m.brain.gameObject.name : "<null>";
                if (now)
                {
                    Log(COL_TOKEN, "Token+",
                        $"<b>{nm}</b> 토큰 획득 " +
                        $"(대기시간 {m.timeSinceLastAttack:F2}s, role={m.currentRole}, atkCnt={m.attackCount})");
                }
                else
                {
                    Log(COL_TOKEN, "Token-",
                        $"<b>{nm}</b> 토큰 회수 " +
                        $"(role={m.currentRole}, atkCnt={m.attackCount})");
                }
            }

            // ── 표준 요약 로그 ──
            // [v3.3.5] 토큰 보유자 이름을 콤마 구분 문자열로 변환하여 로그에 포함.
            // diff 로그(Token+/Token-)는 변화가 있을 때만 출력되므로,
            // 같은 멤버가 계속 토큰을 보유하는 경우 누가 가지고 있는지 알 수 없는 문제 해결.
            string awardedNames;
            if (awarded.Count == 0)
            {
                awardedNames = "없음";
            }
            else
            {
                var sbNames = new StringBuilder(64);
                for (int i = 0; i < awarded.Count; i++)
                {
                    if (i > 0) sbNames.Append(", ");
                    sbNames.Append(awarded[i].brain != null
                        ? awarded[i].brain.gameObject.name
                        : "<null>");
                }
                awardedNames = sbNames.ToString();
            }

            Log(COL_TOKEN, "Tokens",
                $"재분배 완료 → 현재 토큰 보유자: <b>{awardedNames}</b> " +
                $"[{activeTokenCount}/{maxTokenCount} 활성, 멤버 {members.Count}명, " +
                (useOverride
                    ? $"SO OVERRIDE threshold={policySO.tokenOverrideThreshold:F1}"
                    : $"mult={tokenMultiplier:F2}") +
                $", 직전 {prevActive}/{prevMax}]");

            // ── 상세 표 (verboseTokenLog) ──
            if (debugLog && verboseTokenLog)
            {
                var sb = new StringBuilder(256);
                sb.AppendLine($"<color={COL_TOKEN}>[GroupAI:Tokens] 정렬·할당 상세</color>");
                if (useOverride)
                {
                    sb.AppendLine($"  공식: maxToken = max(0, floor(SO.tokenOverrideThreshold)) = max(0, floor({policySO.tokenOverrideThreshold:F2})) = {maxTokenCount}  <b>[SO OVERRIDE]</b>");
                    sb.AppendLine($"  ※ tokenMultiplier({tokenMultiplier:F2}) 와 members.Count({members.Count}) 무시됨");
                }
                else
                {
                    sb.AppendLine($"  공식: maxToken = floor({members.Count} × {tokenMultiplier:F2} + 1) = {maxTokenCount}");
                }
                sb.AppendLine($"  우선순위: timeSinceLastAttack 내림차순 (자격 있는 멤버 중)");
                sb.AppendLine($"  ┌────┬──────────────────────────┬──────────┬─────────┬───────┬───────┬────────┐");
                sb.AppendLine($"  │ #  │ name                     │ tSinceAtk│ atkCnt  │ role  │ 자격  │ token  │");
                sb.AppendLine($"  ├────┼──────────────────────────┼──────────┼─────────┼───────┼───────┼────────┤");
                // [v3.3.7] members 전체 표시 (자격 미달 포함). 정렬은 자격 있는 것만 timeSinceLastAttack.
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
                if (awarded.Count == 0)
                {
                    sb.AppendLine($"  → 토큰 수령자 없음 (브레인이 모두 null이거나 maxTokenCount=0)");
                }
                else
                {
                    sb.Append($"  → 토큰 수령자({awarded.Count}): ");
                    for (int i = 0; i < awarded.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(awarded[i].brain != null ? awarded[i].brain.gameObject.name : "<null>");
                    }
                }
                Debug.Log(sb.ToString(), this);
            }
        }

        // ==================================================================
        // [v3.3.7] 토큰 자격 검사 (P0)
        // 토큰을 부여해도 즉시 공격할 수 없는 상태의 멤버를 후보에서 제외.
        //
        // 검사 5종 (현재):
        //   1. brain == null                          (멤버 인스턴스 사망/GC)
        //   2. !gameObject.activeInHierarchy          (비활성, 풀에 회수됨)
        //   3. characterNetworkManager.isDead         (NetworkVariable 기반 사망)
        //   4. brain.CurrentState == MobBTState.Flee  (도주 중)
        //   5. AICharacterManager.isPerformingAction
        //         && !AICharacterManager.canMove      (피격 중 굳음)
        //
        // 검사 보류 2종 (§4 Shared Target 패치 시 추가 예정):
        //   6. brain.currentTarget == null            (타겟 없음 — sharedTarget OR 검사로 보강)
        //   7. brain.CurrentState == MobBTState.Idle  (유휴 — 위와 동일 사유)
        //
        // out 파라미터 reason: 자격 미달 사유 약어 (로그용).
        // ==================================================================
        private bool IsAttackEligible(GroupMemberState m, out string reason)
        {
            if (m.brain == null)
            {
                reason = "null"; return false;
            }
            if (!m.brain.gameObject.activeInHierarchy)
            {
                reason = "inact"; return false;
            }

            // BT 상태 체크 — 도주 중인 멤버는 토큰 자격 없음
            if (m.brain.CurrentState == MobBTState.Flee)
            {
                reason = "flee"; return false;
            }

            // 사망 체크 (NetworkVariable). characterNetworkManager 미존재 시 스킵.
            var charMgr = m.brain.GetComponent<CharacterManager>();
            if (charMgr != null
                && charMgr.characterNetworkManager != null
                && charMgr.characterNetworkManager.isDead != null
                && charMgr.characterNetworkManager.isDead.Value)
            {
                reason = "dead"; return false;
            }

            // 굳음 체크 — 피격 액션 중이고 이동 불가 상태 (이전 굳음 진단 케이스)
            var aiMgr = m.brain.GetComponent<AICharacterManager>();
            if (aiMgr != null && aiMgr.isPerformingAction && !aiMgr.canMove)
            {
                reason = "stuck"; return false;
            }

            reason = "";
            return true;
        }

        // ==================================================================
        // 사기 관리: IFactionGroupPolicy.EvaluateMorale() 호출
        // ==================================================================
        private void UpdateMorale()
        {
            if (policy == null)
            {
                Log(COL_MORALE, "Morale", "policy=null → UpdateMorale 스킵");
                return;
            }

            // 가장 최근 사망/도주한 멤버의 손실 계수
            float recentLoss = 0f;
            int deadCount = 0, fleeCount = 0;
            foreach (var m in members)
            {
                if (m.brain == null) { recentLoss += 0.2f; deadCount++; continue; }
                if (m.brain.CurrentState == MobBTState.Flee) { recentLoss += 0.1f; fleeCount++; }
            }

            float prev = currentMorale;
            float newMorale = policy.EvaluateMorale(currentMorale, recentLoss, 1.0f);
            currentMorale = Mathf.Clamp01(newMorale);

            // [v3.3.5] 사기 로그 가독성 개선
            //   - 사기 값을 한글 레이블로 해석 (만전/양호/위축/동요/패닉)
            //   - 변화 표시 (▲ 상승 / ▼ 하락 / 변화 없음)
            //   - 손실 페널티 산식을 인라인으로 표기 (사망 N명×0.2 + 도주 N명×0.1)
            string moraleLabel = MoraleLabel(currentMorale);
            string changeIndicator;
            if (Mathf.Approximately(prev, currentMorale))
                changeIndicator = "변화 없음";
            else if (currentMorale > prev)
                changeIndicator = $"▲ 상승 +{(currentMorale - prev):F3}";
            else
                changeIndicator = $"▼ 하락 -{(prev - currentMorale):F3}";

            Log(COL_MORALE, "Morale",
                $"그룹 사기 평가: {prev:F3} → <b>{currentMorale:F3}</b> " +
                $"({moraleLabel}, {changeIndicator}) " +
                $"| 손실 페널티 입력값=<b>{recentLoss:F2}</b> " +
                $"= 사망 {deadCount}명 × 0.2 + 도주 {fleeCount}명 × 0.1 " +
                $"| 사기 1.0=만전, 0.0=패주, <0.2시 패닉 체인 발동");

            // 사기 0.2 미만 → 전원 도주 패닉 체인
            if (currentMorale < 0.2f)
            {
                Log(COL_MORALE, "Morale",
                    $"<b>패닉 체인 발동</b> (사기 {currentMorale:F3} < 0.2). 멤버 fear +0.3");
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
                int prev = currentEscalationLevel;
                currentEscalationLevel = newLevel;
                string factionId = policySO != null ? policySO.factionId : "unknown";
                EventBus.RaiseEscalationTriggered(factionId, currentEscalationLevel);

                Log(COL_ESCAL, "Escal",
                    $"<b>레벨 변경 {prev} → {currentEscalationLevel}</b> " +
                    $"(faction={factionId}, threat={threatLevel:F2}, " +
                    $"combat={combatElapsedTime:F1}s, encircle={encircleProgress:F2})");

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

            // [v3.3.4] 역할 변경 감지를 위해 이전 역할 캡처 (멤버 인덱스와 1:1 대응)
            var prevRoles = new GroupRole[count];
            for (int i = 0; i < count; i++) prevRoles[i] = members[i].currentRole;

            int idx = 0;
            for (int i = 0; i < stalkerCount && idx < count; i++, idx++)
                members[idx].currentRole = GroupRole.Stalker;
            for (int i = 0; i < disruptorCount && idx < count; i++, idx++)
                members[idx].currentRole = GroupRole.Disruptor;
            for (; idx < count; idx++)
                members[idx].currentRole = GroupRole.Striker;

            // [v3.3.4] 캐릭터별 역할 변경 로그 (이전 vs 현재 diff)
            for (int i = 0; i < count; i++)
            {
                var m = members[i];
                if (prevRoles[i] == m.currentRole) continue;  // 변화 없음 → 로그 생략

                string nm = m.brain != null ? m.brain.gameObject.name : "<null>";
                Log(COL_ROLE, "Role*",
                    $"<b>{nm}</b> 역할 변경: {prevRoles[i]} → <b>{m.currentRole}</b> " +
                    $"(Esc={currentEscalationLevel})");
            }

            Log(COL_ROLE, "Role",
                $"분배 → Stalker={stalkerCount} Disruptor={disruptorCount} Striker={strikerCount} " +
                $"(Esc={currentEscalationLevel}, members={count})");

            if (debugLog && verboseTokenLog)
            {
                var sb = new StringBuilder(128);
                sb.AppendLine($"<color={COL_ROLE}>[GroupAI:Role] 멤버별 역할</color>");
                for (int i = 0; i < members.Count; i++)
                {
                    var m = members[i];
                    string nm = m.brain != null ? m.brain.gameObject.name : "<null>";
                    sb.AppendLine($"  [{i,2}] {nm,-24} → {m.currentRole}");
                }
                Debug.Log(sb.ToString(), this);
            }
        }

        // ==================================================================
        // 외부 API
        // ==================================================================

        /// <summary>그룹에 멤버를 런타임 등록.</summary>
        public void RegisterMember(MobAIBrain brain)
        {
            if (brain == null)
            {
                LogW("RegisterMember(null) 호출됨 — 무시.");
                return;
            }

            members.Add(new GroupMemberState { brain = brain, currentRole = GroupRole.Stalker });
            Log(COL_MEMBER, "Member",
                $"등록: {brain.name} (총 {members.Count}명, 역할=Stalker 초기값)");
        }

        /// <summary>
        /// [v3.3.6 NEW] 해당 brain이 현재 공격 토큰을 보유하고 있는지 확인.
        /// StrikeAction 등 외부 BT 노드가 공격 게이트로 사용.
        ///
        /// 사용 예 (StrikeAction.OnStart):
        ///   if (groupAI != null &amp;&amp; brain != null &amp;&amp; !groupAI.MemberHasAttackToken(brain))
        ///       return Status.Failure;  // 토큰 없음 → 공격 포기, 다른 멤버에게 양보
        /// </summary>
        /// <returns>토큰 보유 시 true. brain이 멤버에 없거나 null이면 false.</returns>
        public bool MemberHasAttackToken(MobAIBrain brain)
        {
            if (brain == null) return false;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].brain == brain) return members[i].hasAttackToken;
            }
            return false;
        }

        /// <summary>
        /// [v3.3.6 NEW] 씬에서 특정 brain을 멤버로 가진 GroupAIManager를 검색.
        /// 비용: FindObjectsByType 1회 + 멤버 리스트 순회. 매 프레임 호출 금지.
        /// 일반적으로 BT 노드의 OnStart에서 한 번만 호출 후 필드에 캐싱하여 재사용.
        ///
        /// 사용 예 (StrikeAction):
        ///   if (!_groupAILookedUp) {
        ///       _groupAI = GroupAIManager.FindGroupOwning(_brain);
        ///       _groupAILookedUp = true;
        ///   }
        /// </summary>
        /// <returns>해당 brain을 멤버로 가진 GroupAIManager. 없으면 null (= 해당 mob은 그룹 미소속).</returns>
        public static GroupAIManager FindGroupOwning(MobAIBrain brain)
        {
            if (brain == null) return null;
            var all = UnityEngine.Object.FindObjectsByType<GroupAIManager>(FindObjectsSortMode.None);
            for (int g = 0; g < all.Length; g++)
            {
                var memberList = all[g].members;
                for (int i = 0; i < memberList.Count; i++)
                {
                    if (memberList[i].brain == brain) return all[g];
                }
            }
            return null;
        }

        /// <summary>
        /// [v3.3 NEW] 멤버가 실제로 공격을 수행했음을 통지받는다.
        ///   - timeSinceLastAttack을 0으로 리셋 → 다음 토큰 재분배에서 우선순위 강등.
        ///   - attackCount +1.
        ///   - 다음 토큰 재분배까지 hasAttackToken 유지.
        ///
        /// 호출 위치 가이드:
        ///   - 공격 모션이 실제로 트리거된 직후 (StrikeAction.TryExecuteAttack 성공 시)
        ///   - 또는 hit 판정 직후
        ///   - 어느 쪽을 선택해도 일관되게만 적용할 것.
        ///
        /// 예시 (StrikeAction.TryExecuteAttack 끝부분):
        ///   var groupAI = Self.Value.GetComponentInParent&lt;GroupAIManager&gt;();
        ///   var brain   = Self.Value.GetComponent&lt;MobAIBrain&gt;();
        ///   if (groupAI != null &amp;&amp; brain != null) groupAI.NotifyMemberAttacked(brain);
        /// </summary>
        /// <returns>해당 brain이 그룹 멤버에 속해있어 정상 처리되었으면 true.</returns>
        public bool NotifyMemberAttacked(MobAIBrain brain)
        {
            if (brain == null)
            {
                LogW("NotifyMemberAttacked(null) — 무시.");
                return false;
            }

            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].brain == brain)
                {
                    float prevTime = members[i].timeSinceLastAttack;
                    int prevCount = members[i].attackCount;
                    members[i].timeSinceLastAttack = 0f;
                    members[i].attackCount++;

                    // [v3.3.4] 캐릭터별 분리 로그 — 타이머 리셋
                    Log(COL_ATTACK, "TimerReset",
                        $"<b>{brain.name}</b> 공격 타이머 리셋: " +
                        $"{prevTime:F2}s → 0.00s " +
                        $"(공격 발생으로 timeSinceLastAttack 초기화 → 다음 토큰 재분배에서 우선순위 강등)");

                    // [v3.3.4] 캐릭터별 분리 로그 — 공격 카운트 증가
                    Log(COL_ATTACK, "AtkCnt+",
                        $"<b>{brain.name}</b> 공격 카운트 증가: " +
                        $"{prevCount} → <b>{members[i].attackCount}</b> " +
                        $"(누적 공격 횟수)");

                    // 종합 상태 로그 (기존 — 컨텍스트 파악용)
                    Log(COL_ATTACK, "Attack",
                        $"{brain.name} 공격 통지 종합 → " +
                        $"hadToken={members[i].hasAttackToken}, " +
                        $"role={members[i].currentRole}");
                    return true;
                }
            }

            // 멤버에 없으면 진단용 경고 (배선 누락 가능성)
            LogW($"NotifyMemberAttacked({brain.name}) — 그룹 멤버에 없음. " +
                 $"RegisterMember 호출 누락 또는 잘못된 GroupAIManager 참조 가능성.");
            return false;
        }

        /// <summary>
        /// [v3.3 NEW] 디버깅 보조: 모든 멤버의 timeSinceLastAttack을 0으로 리셋.
        /// 토큰 재분배 흐름을 깨끗한 상태에서 다시 관찰하고 싶을 때 사용.
        /// </summary>
        public void ResetAllAttackTimers()
        {
            // [v3.3.4] 캐릭터별 리셋 로그 — 0.01s 이상 누적된 멤버만 출력 (의미 있는 리셋만)
            int loggedCount = 0;
            foreach (var m in members)
            {
                float prev = m.timeSinceLastAttack;
                m.timeSinceLastAttack = 0f;
                if (prev > 0.01f)
                {
                    string nm = m.brain != null ? m.brain.gameObject.name : "<null>";
                    Log(COL_ATTACK, "TimerReset",
                        $"<b>{nm}</b> 타이머 일괄 리셋: {prev:F2}s → 0.00s (ResetAllAttackTimers 호출)");
                    loggedCount++;
                }
            }
            Log(COL_ATTACK, "Reset",
                $"모든 timeSinceLastAttack = 0 (총 {members.Count}명, 의미 있는 리셋={loggedCount}명)");
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

        // ==================================================================
        // [v3.3] 로깅 헬퍼
        // 모든 진단 로그는 debugLog 플래그를 거친다.
        // Warning은 항상 출력 (LogW).
        // ==================================================================

        /// <summary>표준 로그. debugLog=true일 때만 출력. 카테고리 색상 적용.</summary>
        private void Log(string colorHex, string category, string msg)
        {
            if (!debugLog) return;
            // 예) <color=#FFD54F>[GroupAI:Tokens]</color> 재분배 → active=3/4 ...
            Debug.Log($"<color={colorHex}>[GroupAI:{category}]</color> {msg}", this);
        }

        /// <summary>경고 로그. debugLog 무관하게 항상 출력.</summary>
        private void LogW(string msg)
        {
            Debug.LogWarning($"[GroupAI:WARN] {msg}", this);
        }

        /// <summary>역할 enum 단축 표기 (verbose 표 정렬용).</summary>
        private static string ShortRole(GroupRole r) => r switch
        {
            GroupRole.Stalker => "Stk",
            GroupRole.Disruptor => "Dsr",
            GroupRole.Striker => "Str",
            _ => "?"
        };

        /// <summary>
        /// [v3.3.5] 사기 수치를 한글 레이블로 변환.
        /// 1.0 = 만전, 0.7+ = 양호, 0.4+ = 위축, 0.2+ = 동요, &lt;0.2 = 패닉(체인 발동)
        /// </summary>
        private static string MoraleLabel(float v)
        {
            if (v >= 0.95f) return "만전";
            if (v >= 0.7f) return "양호";
            if (v >= 0.4f) return "위축";
            if (v >= 0.2f) return "동요";
            return "패닉";
        }
    }
}