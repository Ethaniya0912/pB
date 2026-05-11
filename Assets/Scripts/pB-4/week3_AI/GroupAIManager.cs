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
//
// [v3.3.8 Shared Target L1 (P1, §4) — 2026-05-07]
//   ★ 동기 — Issues Report v7 §4 발견: AI가 토큰 받았으나 currentTarget=null이라
//       가만히 있는 케이스. "옆 동료는 싸우는데 본인은 시야 밖". §3 P0 패치의 보완.
//   ★ 신규 — sharedTarget + sharedTargetTimeout 필드:
//       그룹 멤버 중 1명이라도 적을 발견하면 그룹 차원에서 8초간 공유.
//   ★ 신규 — GetSharedTarget() 메서드:
//       유효성 검사 (null / 비활성 / 시간 초과) 후 반환.
//   ★ 신규 — NotifyTargetSpotted(MobAIBrain spotter, Transform target):
//       MobAIBrain의 인지 검사 후 1줄로 그룹에 통보.
//   ★ 변경 — IsAttackEligible:
//       6번째 검사 활성 — currentTarget == null && GetSharedTarget() == null → "noTgt".
//       이제 시야 부족 + 그룹 정보도 없는 멤버만 자격 미달.
//   ★ 사용 — StalkAction에서 currentTarget==null 시 GetSharedTarget() fallback 권장.
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

        [Header("━━━ Shared Target (§4 L1+L2, v3.3.8 / v3.4 Multi-target) ━━━━━━")]
        [Tooltip("[v3.4 L2] 그룹이 알고 있는 모든 적의 리스트. 각 슬롯은 하나의 적 + 인지 중인 멤버 목록 보유. " +
                 "L1: 단일 sharedTarget per-member  →  L2: multi-target per-group + per-member awareness. " +
                 "각 멤버는 GetSharedTarget(caller)로 자기 위치 기준 가장 가까운 알려진 적을 받음. " +
                 "런타임 read-only. policySO 파라미터로 close/chain/timeout 조정.")]
        [SerializeField]
        private List<TargetSlot> _knownTargets = new();

        // 디폴트 파라미터 (policySO 없을 때 fallback)
        private const float DEFAULT_SHARED_CLOSE_RADIUS = 15f;
        private const float DEFAULT_SHARED_CHAIN_MAX = 50f;
        private const float DEFAULT_SHARED_TIMEOUT = 8f;

        /// <summary>
        /// [v3.4 L2] 그룹이 인지하는 단일 적 슬롯. 적 1개당 1슬롯.
        /// awareMembers: close/chain 전파로 이 적을 알게 된 멤버 목록.
        /// </summary>
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
        [Tooltip("[v3.3] 마스터 토글. 본 클래스의 모든 진단 로그를 일괄 ON/OFF. " +
                 "OFF 상태에서는 Warning/Error를 제외한 모든 로그가 억제된다.")]
        public bool debugLog = false;
        [Tooltip("[v3.3] 토큰 재분배의 멤버별 상세 표 출력 (debugLog=true일 때만 동작). " +
                 "정렬된 우선순위 / 토큰 보유 / 공격 누적 / 마지막 공격 후 경과 시간을 모두 출력.")]
        public bool verboseTokenLog = false;

        [Tooltip("[v3.3] 매 프레임 타이머 누적값 출력 (debugLog=true일 때만). " +
                 "성능 영향 큼. 토큰 주기/사기 주기 디버깅 시에만 단발로 켤 것.")]
        public bool logFrameTimers = false;

        [Header("━━━ Faction Safe Zone (§5 L1, v3.4) ━━━━━━")]
        [Tooltip("[v3.4 §5 L1] 펙션 공유 안전지대 위치. 그룹 차원에서 1곳 (Wk5+ 다수 확장 예정). " +
                 "도주 시 멤버 개인 메모리와 합쳐서 가까운 곳 우선 사용. " +
                 "OnEnable 시점에 그룹 중심 자동 기록.")]
        [SerializeField] private Vector3 factionSafeZone;

        [Tooltip("[표시 전용] 펙션 공유 안전지대가 설정됐는지 여부.")]
        [SerializeField] private bool hasFactionSafeZone = false;

        /// <summary>[v3.4 §5 L1] 펙션 공유 안전지대 (read-only).</summary>
        public Vector3 FactionSafeZone => factionSafeZone;
        /// <summary>[v3.4 §5 L1] 펙션 안전지대 설정됐는지 (read-only).</summary>
        public bool HasFactionSafeZone => hasFactionSafeZone;

        /// <summary>
        /// [v3.4 §5 L1] 펙션 공유 안전지대 갱신. 외부에서 호출 가능.
        /// (예: 평화 상태 멤버 평균 위치, Cave 매복지 등 — Stage 2+에서 정교화)
        /// </summary>
        public void SetFactionSafeZone(Vector3 pos)
        {
            factionSafeZone = pos;
            hasFactionSafeZone = true;
            PushEvent("Lifecycle", $"FactionSafeZone 설정: ({pos.x:F1}, {pos.y:F1}, {pos.z:F1})", EVT_LIFECYCLE);
        }

        [Header("━━━ Editor Auto Focus (v3.4) ━━━━━━━━━━")]
        [Tooltip("[Editor 전용] SceneView 카메라 자동 추적 모드. 토큰을 가장 최근에 획득한 멤버를 기준점으로 사용. " +
                 "None=비활성. 모드별 동작은 코드 주석 참조.")]
        public FocusCameraMode focusMode = FocusCameraMode.None;

        [Tooltip("[표시 전용] 가장 최근에 토큰을 획득한 멤버. AutoFocus 추적 대상.")]
        [SerializeField] private MobAIBrain lastTokenWinner;

        /// <summary>[v3.4] 가장 최근에 토큰을 획득한 멤버 (read-only).</summary>
        public MobAIBrain LastTokenWinner => lastTokenWinner;

        /// <summary>
        /// [v3.4] SceneView 카메라 자동 포커즈 모드.
        /// 1: AI 정면 — AI의 정면이 카메라 향함 (카메라가 AI 앞에 위치)
        /// 2: AI + 타겟 탑뷰 — 두 캐릭터 중간 위에서 직각 내려다봄
        /// 3: AI 숄더(거리 고정) — AI 등 너머로 타겟. 타겟 멀어져도 카메라↔AI 거리 유지
        /// 4: 그룹 탑뷰 — 그룹 전체를 위에서 내려다봄
        /// 5: 그룹 숄더→타겟 — 그룹 중심 너머로 타겟 향함 (그룹의 OTS 구도)
        /// </summary>
        public enum FocusCameraMode
        {
            [UnityEngine.InspectorName("None")]
            None,
            [UnityEngine.InspectorName("1: AI 정면")]
            AIFront,
            [UnityEngine.InspectorName("2: AI+타겟 탑뷰")]
            AIPlusTargetTop,
            [UnityEngine.InspectorName("3: AI 숄더 (거리 고정)")]
            AIShoulderFixed,
            [UnityEngine.InspectorName("4: 그룹 탑뷰")]
            GroupTop,
            [UnityEngine.InspectorName("5: 그룹 숄더 → 타겟")]
            GroupShoulderToTarget,
        }

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
            // [v3.4] 그룹 생성 시점 기록 — 이벤트 히스토리 상대시간 기준
            if (_groupSpawnTime <= 0f) _groupSpawnTime = Time.time;
            PushEvent("Lifecycle", "그룹 OnEnable", new Color(0.85f, 0.85f, 0.85f));

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

            // [v3.3.8 §4 L1] sharedTarget 주기적 재전파 (1초마다)
            //   목적: 멤버 위치 변동 / 새 spotter 발견 / 차단 해제 등을 자동 추적.
            //   spotter (currentTarget 보유 멤버) 기준으로 NotifyTargetSpotted를
            //   silent 모드로 재호출 → 다른 멤버 캐시 갱신.
            _sharedTargetReevalTimer += Time.deltaTime;
            if (_sharedTargetReevalTimer >= SHARED_TARGET_REEVAL_INTERVAL)
            {
                _sharedTargetReevalTimer = 0f;
                ReevaluateSharedTargets();
                ReevaluateFactionSafeZone();   // [v3.4 §5 L1] SafeZone 자동 동기화
                ReevaluateTacticalRetreat();   // [v3.4 §5 Stage 2.1] 전략적 후퇴 명령
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

#if UNITY_EDITOR
            // [v3.4] Editor 전용 — Auto Focus SceneView 카메라
            //   focusMode에 따라 5가지 구도 중 선택. lastTokenWinner를 기준점으로 사용.
            //   매 frame 위치 추적 → 멤버/타겟 이동에 따라 카메라도 이동.
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
            Transform tgt = lastTokenWinner.currentTarget;
            bool hasTarget = tgt != null;
            Vector3 targetPos = hasTarget ? tgt.position + Vector3.up * 1.0f : Vector3.zero;

            switch (focusMode)
            {
                case FocusCameraMode.AIFront:
                    FocusAIFront(sv, winnerPos);
                    break;

                case FocusCameraMode.AIPlusTargetTop:
                    if (hasTarget) FocusAIPlusTargetTop(sv, winnerPos, targetPos);
                    else FocusAIFront(sv, winnerPos);
                    break;

                case FocusCameraMode.AIShoulderFixed:
                    if (hasTarget) FocusAIShoulderFixed(sv, winnerPos, targetPos);
                    else FocusAIFront(sv, winnerPos);
                    break;

                case FocusCameraMode.GroupTop:
                    FocusGroupTop(sv);
                    break;

                case FocusCameraMode.GroupShoulderToTarget:
                    if (hasTarget) FocusGroupShoulderToTarget(sv, targetPos);
                    else FocusGroupTop(sv);
                    break;
            }
        }

        // 옵션 1 — AI 정면이 카메라 향함. 카메라는 AI의 forward 방향에 위치.
        private void FocusAIFront(UnityEditor.SceneView sv, Vector3 aiPos)
        {
            Vector3 aiForward = lastTokenWinner.transform.forward;
            Vector3 camPos = aiPos + aiForward * 3f + Vector3.up * 0.5f;
            float size = Vector3.Distance(camPos, aiPos);
            Quaternion rot = Quaternion.LookRotation(aiPos - camPos);
            sv.LookAt(aiPos, rot, size, false, false);
        }

        // 옵션 2 — AI + Target 탑뷰. 두 캐릭터 중간 위에서 수직 내려다봄.
        private void FocusAIPlusTargetTop(UnityEditor.SceneView sv, Vector3 winnerPos, Vector3 targetPos)
        {
            Vector3 mid = (winnerPos + targetPos) * 0.5f;
            float distance = Vector3.Distance(winnerPos, targetPos);
            float size = Mathf.Max(distance * 0.7f + 4f, 6f);
            Quaternion rot = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            sv.LookAt(mid, rot, size, false, false);
        }

        // 옵션 3 — AI OTS 고정 거리. 타겟 멀어져도 카메라↔AI 거리 일정.
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

        // 옵션 4 — 그룹 전체 탑뷰. 모든 멤버 Bounds.center 위에서 수직 내려다봄.
        private void FocusGroupTop(UnityEditor.SceneView sv)
        {
            Bounds bounds;
            if (!ComputeGroupBounds(out bounds)) return;

            float size = Mathf.Max(bounds.size.magnitude * 0.6f + 3f, 6f);
            Quaternion rot = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            sv.LookAt(bounds.center, rot, size, false, false);
        }

        // 옵션 5 — 그룹 중심 OTS 구도로 타겟 향함. 그룹의 단체 시점.
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
                if (first)
                {
                    bounds = new Bounds(m.brain.transform.position, Vector3.one * 1f);
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(m.brain.transform.position);
                }
            }
            return !first;
        }
#endif

        private float _sharedTargetReevalTimer = 0f;
        private const float SHARED_TARGET_REEVAL_INTERVAL = 1.0f;

        /// <summary>
        /// [v3.3.8 §4 L1] 1초마다 호출. currentTarget을 가진 멤버를 spotter로 보고
        /// NotifyTargetSpotted를 silent 모드로 재호출 → 멤버 위치 변동 추적 + 캐시 갱신.
        /// </summary>
        private void ReevaluateSharedTargets()
        {
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.brain == null) continue;
                if (m.brain.currentTarget == null) continue;
                // 자체 currentTarget 보유 = "spotter" 역할 → 같은 타겟을 다시 전파
                NotifyTargetSpotted(m.brain, m.brain.currentTarget, silent: true);
            }
        }

        /// <summary>
        /// [v3.4 §5 L1] 1초마다 호출. 펙션 SafeZone을 가까운 멤버에게 자동 동기화.
        ///   sharedTarget의 close 전파 모델과 동일 — closeRadius 내 멤버 자동 받음.
        ///
        /// 두 가지 조건 (OR):
        ///   1. 본인이 factionSafeZone 자체에 close 범위 내
        ///   2. 정보 보유한 다른 멤버에 close 범위 내 (그룹 응집성 통한 전파)
        ///
        /// 메모리 동기화 후 ContextMenu Clear → 다음 사이클에서 자동 복구.
        /// </summary>
        private void ReevaluateFactionSafeZone()
        {
            if (!hasFactionSafeZone) return;

            float closeRadius = policySO != null
                ? policySO.sharedTargetCloseRadius
                : DEFAULT_SHARED_CLOSE_RADIUS;

            int syncedCount = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.brain == null) continue;
                if (HasFactionSafeZoneInMemory(m.brain)) continue;  // 이미 보유

                Vector3 myPos = m.brain.transform.position;
                bool eligible = false;
                string reason = "";

                // 조건 1: 본인이 factionSafeZone 자체에 close 범위 내
                float distToZone = Vector3.Distance(myPos, factionSafeZone);
                if (distToZone <= closeRadius)
                {
                    eligible = true;
                    reason = $"self ({distToZone:F1}m ≤ {closeRadius:F0}m)";
                }

                // 조건 2: 정보 보유 다른 멤버 close 범위 내
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
                    PushEvent("Sense",
                        $"{m.brain.name} 펙션 SafeZone 메모리 자동 동기화 ({reason})",
                        EVT_SENSE);
                }
            }

            if (syncedCount > 0 && verboseTokenLog)
                Log(COL_TOKEN, "SafeZone:Reeval", $"{syncedCount}명 펙션 SafeZone 자동 동기화");
        }

        /// <summary>멤버가 factionSafeZone을 메모리에 보유했는지 검사 (1m 오차 허용).</summary>
        private bool HasFactionSafeZoneInMemory(BaseAIBrain brain)
        {
            if (!hasFactionSafeZone || brain == null) return true;
            foreach (var pos in brain.LastSafePositions)
            {
                if (Vector3.Distance(pos, factionSafeZone) < 1f) return true;
            }
            return false;
        }

        // ==================================================================
        // [v3.4 §5 L1 Stage 2.1] ReevaluateTacticalRetreat
        //   1초 주기 호출. 펙션 정책 ON일 때 멤버 HP 검사 → 후퇴 명령 발행.
        //   fear (본능)와 독립 — Orc 같은 전사 펙션의 합리적 후퇴 트리거.
        //
        //   조건 충족 → brain.OrderedRetreat = true
        //   회복 시 → brain.OrderedRetreat = false (자동 해제)
        //
        //   PB4DecisionAdapter가 BB[UtilityWinner]="Flee" 강제 push → BT FleeDuel 진입
        //   → InitiateRetreat (Stage 2 코드 재사용) → 안전지대 이동
        // ==================================================================
        private void ReevaluateTacticalRetreat()
        {
            // 정책 ON 검사 (펙션별)
            if (policySO == null || !policySO.orderedRetreatEnabled) return;

            float hpThreshold = policySO.orderedRetreatHpThreshold;

            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.brain == null) continue;

                // HP 비율 계산 — AICharacterManager.characterNetworkManager 패턴
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
                    // 후퇴 명령 발행
                    m.brain.OrderedRetreat = true;
                    Log(COL_TOKEN, "TacticalRetreat",
                        $"{m.brain.name}: HP={hpRatio:P0} < {hpThreshold:P0} → 후퇴 명령 발행 (전략적)");
                    PushEvent("Combat",
                        $"{m.brain.name} 후퇴 명령 (HP {hpRatio:P0} < {hpThreshold:P0})",
                        EVT_COMBAT);
                }
                else if (!shouldRetreat && wasOrdered)
                {
                    // 회복됨 — 명령 해제
                    m.brain.OrderedRetreat = false;
                    Log(COL_TOKEN, "TacticalRetreat",
                        $"{m.brain.name}: HP={hpRatio:P0} 회복 → 후퇴 명령 해제 (재합류)");
                    PushEvent("Combat",
                        $"{m.brain.name} 후퇴 해제 (HP {hpRatio:P0} 회복)",
                        EVT_COMBAT);
                }
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
                    PushEvent("Token", $"{nm} 토큰 획득 (atk#={m.attackCount}, wait={m.timeSinceLastAttack:F1}s)", EVT_TOKEN);
                    // [v3.4] 가장 최근 winner 갱신 — 카메라 포커즈 추적용
                    lastTokenWinner = m.brain;
                }
                else
                {
                    Log(COL_TOKEN, "Token-",
                        $"<b>{nm}</b> 토큰 회수 " +
                        $"(role={m.currentRole}, atkCnt={m.attackCount})");
                    PushEvent("Token", $"{nm} 토큰 회수 (atk#={m.attackCount})", EVT_TOKEN);
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

            // [v3.3.8] §4 L1 — 본인 시야 + 그룹 공유 타겟 모두 없으면 자격 미달
            //   "공격할 대상이 어디 있는지 모르는" 멤버에게 토큰 부여 무의미.
            //   StalkAction이 sharedTarget으로 fallback하므로 둘 다 null일 때만 탈락.
            if (m.brain.currentTarget == null && GetSharedTarget(m.brain) == null)
            {
                reason = "noTgt"; return false;
            }

            reason = "";
            return true;
        }

        // ==================================================================
        // [v3.4 §4 L2 Multi-target] Shared Target — 그룹 차원 multi-target 큐
        //
        // 호출 흐름:
        //   1. AIPerceptionSystem이 인지 후 NotifyTargetSpotted(spotter, target)
        //   2. GroupAIManager가 정책 SO에서 close/chain 파라미터 로드
        //   3. target에 대한 TargetSlot을 찾거나 신규 추가 → lastKnownPosition/lastSeenTime 갱신
        //   4. 각 멤버에 대해:
        //      a. spotter 본인 → awareMembers에 무조건 포함
        //      b. 거리 ≤ close radius → awareMembers에 추가
        //      c. close < 거리 ≤ chain max → 시야 체인 검사 후 추가/제거
        //      d. chain max 외 → awareMembers에서 즉시 제거
        //   5. Update에서 timeout 만료된 슬롯 자동 제거
        //   6. GetSharedTarget(caller) → caller가 awareMembers에 포함된 슬롯 중
        //      caller 위치 기준 가장 가까운 적 반환
        //
        // L1 → L2 전환:
        //   L1: 멤버별 단일 target 캐시 (caller 1개 → target 1개)
        //   L2: 그룹 multi-target + 멤버별 awareness (caller 1개 → target N개 인지)
        //   효과: 적이 여러 명일 때 멤버가 자기 위치에 적합한 적 자동 선택
        // ==================================================================

        /// <summary>
        /// [v3.4 L2] caller가 인지한 적들 중 caller 위치 기준 가장 가까운 적 반환.
        /// 적 없으면 null. 만료/null/비활성 슬롯은 자동 정리.
        /// </summary>
        public Transform GetSharedTarget(MobAIBrain caller)
        {
            if (caller == null) return null;

            float timeout = policySO != null ? policySO.sharedTargetTimeout : DEFAULT_SHARED_TIMEOUT;

            Transform best = null;
            float bestDist = float.MaxValue;
            Vector3 callerPos = caller.transform.position;

            // 만료/null 슬롯 정리하면서 caller 인지 슬롯 검색
            for (int i = _knownTargets.Count - 1; i >= 0; i--)
            {
                var slot = _knownTargets[i];
                if (slot.target == null ||
                    Time.time - slot.lastSeenTime > timeout ||
                    !slot.target.gameObject.activeInHierarchy)
                {
                    string targetName = slot.target != null ? slot.target.name : "(null)";
                    string reason = slot.target == null ? "destroyed"
                                  : !slot.target.gameObject.activeInHierarchy ? "inactive"
                                  : "timeout";
                    _knownTargets.RemoveAt(i);
                    PushEvent("Sense", $"KnownTarget 제거: {targetName} ({reason})", EVT_SENSE);
                    continue;
                }

                if (!slot.awareMembers.Contains(caller)) continue;

                float dist = Vector3.Distance(callerPos, slot.target.position);
                if (dist < bestDist)
                {
                    best = slot.target;
                    bestDist = dist;
                }
            }
            return best;
        }

        /// <summary>
        /// [v3.4 L2] 멤버가 적을 발견했을 때 그룹에 통보.
        /// TargetSlot 갱신 + close/chain 전파 → 멤버별 awareMembers 갱신.
        /// silent=true: 주기적 재전파 호출 (Update의 ReevaluateSharedTargets 통해).
        /// </summary>
        public void NotifyTargetSpotted(MobAIBrain spotter, Transform target, bool silent = false)
        {
            if (target == null || spotter == null) return;

            float closeRadius = policySO != null ? policySO.sharedTargetCloseRadius : DEFAULT_SHARED_CLOSE_RADIUS;
            float chainMax = policySO != null ? policySO.sharedTargetChainMaxRadius : DEFAULT_SHARED_CHAIN_MAX;
            LayerMask obstacles = policySO != null ? policySO.sharedTargetSightObstacleLayers : ~0;

            Vector3 spotterPos = spotter.transform.position + Vector3.up * 0.5f;

            // 1. target 슬롯 찾기 / 추가 + spotter 정보 갱신
            TargetSlot slot = FindOrCreateTargetSlot(target, spotter);
            slot.lastKnownPosition = target.position;
            slot.lastSeenTime = Time.time;
            if (!slot.awareMembers.Contains(spotter)) slot.awareMembers.Add(spotter);

            // 2. 다른 멤버에게 전파 — 하이브리드 (close/chain/blocked/outOfRange)
            int closeCount = 0, chainCount = 0, blockedCount = 0, outOfRangeCount = 0;
            var detailLog = new System.Text.StringBuilder();

            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.brain == null || m.brain == spotter) continue;

                Vector3 memberPos = m.brain.transform.position + Vector3.up * 0.5f;
                float dist = Vector3.Distance(spotterPos, memberPos);

                if (dist <= closeRadius)
                {
                    if (!slot.awareMembers.Contains(m.brain)) slot.awareMembers.Add(m.brain);
                    closeCount++;
                    if (verboseTokenLog) detailLog.Append($"  ✔ close {m.brain.name} ({dist:F1}m)\n");
                }
                else if (dist <= chainMax && chainMax > 0)
                {
                    Vector3 dir = memberPos - spotterPos;
                    bool blocked = IsLineOfSightBlocked(spotterPos, dir, spotter, m.brain, obstacles);
                    if (!blocked)
                    {
                        if (!slot.awareMembers.Contains(m.brain)) slot.awareMembers.Add(m.brain);
                        chainCount++;
                        if (verboseTokenLog) detailLog.Append($"  ✔ chain {m.brain.name} ({dist:F1}m)\n");
                    }
                    else
                    {
                        // 차단 → 해당 멤버는 이 슬롯의 awareness에서 제거
                        slot.awareMembers.Remove(m.brain);
                        blockedCount++;
                        if (verboseTokenLog) detailLog.Append($"  ✗ blocked {m.brain.name} ({dist:F1}m, 시야 차단 → awareness 제거)\n");
                    }
                }
                else
                {
                    // 범위 밖 → awareness 제거
                    slot.awareMembers.Remove(m.brain);
                    outOfRangeCount++;
                    if (verboseTokenLog) detailLog.Append($"  ✗ outOfRange {m.brain.name} ({dist:F1}m > {chainMax:F0}m → awareness 제거)\n");
                }
            }

            string summary = $"<b>{spotter.gameObject.name}</b> → <b>{target.name}</b> 인지. " +
                             $"전파: 즉시={closeCount} (≤{closeRadius:F0}m) / 체인={chainCount} / " +
                             $"차단={blockedCount} / 범위외={outOfRangeCount} | 슬롯={_knownTargets.Count}개 알려짐";
            if (!silent)
            {
                Log(COL_TOKEN, "Sense", summary);
            }
            else if (verboseTokenLog)
            {
                Log(COL_TOKEN, "Sense:Reeval", summary);
            }

            if (verboseTokenLog && detailLog.Length > 0)
            {
                Debug.Log($"<color=#88FFAA>[GroupAI:Sense:Detail]</color>{(silent ? "[Reeval]" : "")}\n{detailLog}");
            }
        }

        /// <summary>
        /// 같은 target에 대한 슬롯이 있으면 반환, 없으면 신규 생성.
        /// </summary>
        private TargetSlot FindOrCreateTargetSlot(Transform target, MobAIBrain firstSpotter)
        {
            for (int i = 0; i < _knownTargets.Count; i++)
            {
                if (_knownTargets[i].target == target)
                    return _knownTargets[i];
            }
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
            PushEvent("Sense",
                $"KnownTarget 추가: {target.name} (spotter={firstSpotter?.name ?? "?"})",
                EVT_SENSE);
            return slot;
        }

        /// <summary>
        /// [v3.3.8 §4 L1] Spotter → Member 시야 직선이 obstacles로 차단됐는지 검사.
        /// spotter 자신, member 자신, 그들의 자식 콜라이더는 무시 (캐릭터끼리 안 막음).
        /// 지형/벽만 진짜 차단으로 인식.
        /// </summary>
        private static bool IsLineOfSightBlocked(Vector3 spotterPos, Vector3 dirToMember,
                                                  MobAIBrain spotter, MobAIBrain member,
                                                  LayerMask obstacles)
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
                PushEvent("Role", $"{nm}: {prevRoles[i]} → {m.currentRole} (Esc={currentEscalationLevel})", EVT_ROLE);
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

            // [v3.3.8] 양방향 연결 — BaseAIBrain.groupMindRef에 본 매니저 자동 할당.
            brain.groupMindRef = this;

            // [v3.4 §5 L1] spawn 위치를 안전지대 메모리에 자동 기록
            //   각 멤버는 자기 spawn 시점 위치를 첫 번째 안전지대로 기억.
            //   추후 Cave 태그/평화 위치 등 다른 시점도 추가 (Stage 2+).
            brain.RememberSafePosition(brain.transform.position);

            // [v3.4 §5 L1] 펙션 공유 안전지대 자동 설정 — 첫 멤버 spawn 위치 사용
            //   Wk5+에서 평균 위치/Cave 태그/디자인 명세 기반 정교화 예정.
            if (!hasFactionSafeZone)
            {
                SetFactionSafeZone(brain.transform.position);
            }

            Log(COL_MEMBER, "Member",
                $"등록: {brain.name} (총 {members.Count}명, 역할=Stalker 초기값) + groupMindRef 양방향 + spawn safe zone 기록");
            PushEvent("Membership", $"{brain.name} 등록 (Stalker, 총 {members.Count}명)", EVT_MEMBERSHIP);
        }

        /// <summary>
        /// [v3.3.8] WorldAISpawnManager가 자동 생성한 그룹에 정책 SO 할당.
        /// 그룹 결성 직후 호출. 사기 시스템 동작에 필수.
        /// </summary>
        public void SetPolicy(FactionGroupPolicySO so, MonoBehaviour implementor = null)
        {
            policySO = so;
            if (implementor != null)
            {
                policyImplementor = implementor;
                if (implementor is Interfaces.Intelligence.IFactionGroupPolicy ip)
                    policy = ip;

                // [v3.4] implementor의 policySO 필드도 자동 주입 (Reflection)
                //   *GroupPolicy 구현체들은 모두 [SerializeField] private FactionGroupPolicySO policySO
                //   필드를 가짐. Inspector에서 수동 할당하지 않아도 자동 sync.
                if (so != null)
                {
                    var fld = implementor.GetType().GetField("policySO",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    if (fld != null && fld.FieldType == typeof(FactionGroupPolicySO))
                    {
                        fld.SetValue(implementor, so);
                    }
                }
            }
            Log(COL_MORALE, "Policy",
                $"SetPolicy: SO={(so != null ? so.name : "null")} " +
                $"impl={(implementor != null ? implementor.GetType().Name : "null")}");
            PushEvent("Lifecycle",
                $"정책 적용: {(so != null ? so.name : "null")} / {(implementor != null ? implementor.GetType().Name : "null")}",
                EVT_LIFECYCLE);
        }

        /// <summary>
        /// [v3.3.8] 멤버 해제. 사망/이탈/씬 전환 시 호출.
        /// 정식 통합(Wk5) 전까지 ContextMenu에서도 호출.
        /// </summary>
        public bool UnregisterMember(MobAIBrain brain)
        {
            if (brain == null) return false;
            for (int i = members.Count - 1; i >= 0; i--)
            {
                if (members[i].brain == brain)
                {
                    members.RemoveAt(i);
                    // [v3.3.8] 양방향 해제 — brain.groupMindRef도 정리
                    if (brain.groupMindRef == this)
                        brain.groupMindRef = null;
                    Log(COL_MEMBER, "Member",
                        $"해제: {brain.name} (남은 {members.Count}명)");
                    PushEvent("Membership", $"{brain.name} 해제 (남은 {members.Count}명)", EVT_MEMBERSHIP);
                    return true;
                }
            }
            return false;
        }

        // ==================================================================
        // [v3.3.8 Wk3 검증용] 자동 등록 ContextMenu
        //
        // 동기: 현재 RegisterMember를 호출하는 진입점이 없음.
        //       정식 통합은 Wk5 §11 SpawnRequest 도입 시 WorldAISpawnManager가
        //       OnAllCharactersSpawned 핸들러에서 자동 결성 예정.
        //       Wk3 검증 단계에서는 사용자가 Respawn 후 본 메뉴 1회 클릭으로
        //       씬의 모든 활성 MobAIBrain을 자동 등록.
        // ==================================================================

        /// <summary>
        /// 씬 + DontDestroyOnLoad의 모든 활성 MobAIBrain을 검색하여
        /// 본 GroupAIManager에 자동 등록. 중복 등록은 자동 스킵.
        /// </summary>
        [ContextMenu("★ Wk3 Test/Auto-Register All MobAIBrain in Scene")]
        private void DebugAutoRegisterAll()
        {
            int registered = 0;
            int skipped = 0;
            var allBrains = UnityEngine.Object.FindObjectsByType<MobAIBrain>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (var brain in allBrains)
            {
                // 중복 검사
                bool already = false;
                for (int i = 0; i < members.Count; i++)
                {
                    if (members[i].brain == brain) { already = true; break; }
                }
                if (already) { skipped++; continue; }

                RegisterMember(brain);
                registered++;
            }

            Debug.Log(
                $"<color=#88FF88>[GroupAI:AutoRegister]</color> {gameObject.name}: " +
                $"신규 {registered}명 등록, 중복 {skipped}명 스킵 → 총 {members.Count}명");
        }

        /// <summary>
        /// 모든 멤버 해제. Respawn 직전 또는 다른 그룹에 재배치 시 사용.
        /// </summary>
        [ContextMenu("★ Wk3 Test/Clear All Members")]
        private void DebugClearAllMembers()
        {
            int count = members.Count;
            members.Clear();
            Debug.Log($"<color=#FFAA88>[GroupAI:Clear]</color> {gameObject.name}: " +
                      $"{count}명 모두 해제 → 멤버 0명");
        }

        /// <summary>
        /// 멤버 리스트 진단. 인스펙터에서 Members 펼치지 않고도 콘솔에 한눈에.
        /// </summary>
        [ContextMenu("★ Wk3 Test/Print Members")]
        private void DebugPrintMembers()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== {gameObject.name} Members ({members.Count}명) ===");
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m.brain == null) { sb.AppendLine($"  [{i}] <null brain>"); continue; }
                sb.AppendLine($"  [{i}] {m.brain.name} | role={m.currentRole} | " +
                              $"fear={m.brain.fear:F2} | tok={m.hasAttackToken} | " +
                              $"hasTgt={(m.brain.currentTarget != null)}");
            }
            Debug.Log(sb.ToString());
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
        /// <summary>[v3.3.8] 멤버 수 (read-only).</summary>
        public int MemberCount => members.Count;
        /// <summary>[v3.4 §4 L2] 그룹이 인지하는 적 슬롯 목록 (read-only).</summary>
        public IReadOnlyList<TargetSlot> KnownTargets => _knownTargets;
        /// <summary>[v3.4] 전투 경과 시간 (초).</summary>
        public float CombatElapsedTime => combatElapsedTime;
        /// <summary>[v3.4] 정책 SO (read-only). 모니터링용.</summary>
        public FactionGroupPolicySO PolicySO => policySO;
        /// <summary>[v3.4] 정책 구현체 (read-only). 모니터링용.</summary>
        public MonoBehaviour PolicyImplementor => policyImplementor;
        /// <summary>[v3.4] 다음 토큰 재분배까지 남은 시간 (초).</summary>
        public float TokenReassignTimeRemaining => Mathf.Max(0f, tokenReassignInterval - tokenTimer);
        /// <summary>[v3.4] brain != null인 살아있는 멤버 수.</summary>
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
        // [v3.4] 이벤트 히스토리 — Inspector Live Monitoring 용
        //   목적: 그룹 활동 주요 이벤트 시간 순서 박제
        //   관통 정보: 멤버 등록/해제, 토큰 변동, Role 전환, KnownTarget 추가/제거
        //   Stage 2 (별도 시스템 통합 시점): 공격 Dmg, 죽음, 그룹 전환
        // ==================================================================
        [System.Serializable]
        public struct GroupEvent
        {
            public float relativeTime;  // 그룹 OnEnable 후 경과 (초)
            public string category;     // Lifecycle / Membership / Token / Role / Sense
            public string message;
            public Color tagColor;
        }

        private List<GroupEvent> _eventHistory = new List<GroupEvent>();
        private const int MAX_EVENT_HISTORY = 50;
        private float _groupSpawnTime = 0f;

        /// <summary>[v3.4] 이벤트 히스토리 (read-only). 모니터링용.</summary>
        public IReadOnlyList<GroupEvent> EventHistory => _eventHistory;

        /// <summary>[v3.4] 그룹 생성 후 경과 시간.</summary>
        public float GroupAge => _groupSpawnTime > 0f ? Time.time - _groupSpawnTime : 0f;

        /// <summary>
        /// [v3.4] 이벤트 추가. ring buffer 형태로 cap 50.
        /// </summary>
        private void PushEvent(string category, string message, Color tagColor)
        {
            _eventHistory.Add(new GroupEvent
            {
                relativeTime = GroupAge,
                category = category,
                message = message,
                tagColor = tagColor,
            });
            if (_eventHistory.Count > MAX_EVENT_HISTORY)
                _eventHistory.RemoveAt(0);
        }

        // 카테고리별 색상 (private constants)
        private static readonly Color EVT_LIFECYCLE = new Color(0.85f, 0.85f, 0.85f);
        private static readonly Color EVT_MEMBERSHIP = new Color(0.5f, 1.0f, 0.5f);
        private static readonly Color EVT_TOKEN = new Color(1.0f, 0.85f, 0.3f);
        private static readonly Color EVT_ROLE = new Color(0.85f, 0.5f, 1.0f);
        private static readonly Color EVT_SENSE = new Color(0.4f, 0.85f, 1.0f);
        private static readonly Color EVT_COMBAT = new Color(1.0f, 0.4f, 0.4f);

        /// <summary>[v3.4] 이벤트 히스토리 초기화 (ContextMenu).</summary>
        [ContextMenu("★ Wk3 Test/Clear Event History")]
        private void ClearEventHistory()
        {
            _eventHistory.Clear();
            Debug.Log($"[GroupAI:{name}] Event History 초기화됨.");
        }

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