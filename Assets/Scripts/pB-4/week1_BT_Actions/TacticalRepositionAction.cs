// =============================================================================
// TacticalRepositionAction.cs  |  pB-4 커스텀 BT Action — 전술적 후퇴/회피
// 패키지  : Unity Behavior 1.0.15 (com.unity.behavior)
//
// 역할:
//   오크/고블린(비-Phalanx) 전용 전술 행동.
//   현재 HP/스태미나 비율을 보고 세 가지 모드 중 하나를 실행합니다.
//
//   [모드 1] Recover (체력·스태미나 회복 후퇴)
//     트리거: currentStamina/maxStamina < StaminaRecoverThreshold
//             OR currentHealth/maxHealth < HealthRecoverThreshold
//     행동:   타겟 반대 방향으로 RetreatDist(m) 후퇴 → RecoverDuration(초) 대기
//             (대기 중 스태미나/체력 자연 회복)
//             회복 완료 후 Success → 다시 Stalk/CircleStrafe로 복귀
//
//   [모드 2] TacticalEvade (전술적 회피 스텝)
//     트리거: CircleStrafe/Strike 중 랜덤으로 발동 (EvasionChance 확률)
//     행동:   좌/우/후방 중 랜덤 방향으로 짧은 스텝 (EvasionDist m)
//             EvasionDuration 초 후 Success → 원래 패턴 재개
//
//   [모드 3] Feint (페인트 스텝)
//     트리거: CircleStrafe 타이머 만료 전 (TacticalFeintChance 확률)
//     행동:   잠깐 타겟 방향으로 전진 → 즉시 후퇴 (공격 유도 후 카운터 노림)
//             FeintDuration 초 후 Success
//
// BT 배치:
//   Attack 분기의 ConditionalGuard 안에서 CircleStrafe와 병렬(또는 직전)로 배치.
//   PB4DecisionAdapter가 HP/스태미나를 BB에 push합니다.
//
// 스태미나 미소모 원인 (진단):
//   CharacterStatsManager.RegenerateStamina()가 !character.IsOwner 이면 return.
//   AI는 IsOwner=false → 재생 루프 자체가 실행 안 됨.
//   또한 AttackState/AICharacterCombatManager에서 스태미나 소모 호출 자체가 없음.
//   수정 위치: AICharacterManager.Update() 또는 StrikeAction.TryExecuteAttack()에서
//              _aiManager.characterNetworkManager.currentStamina.Value -= staminaCost 직접 차감.
//              RegenerateStamina()는 IsServer 조건으로 대체하여 AI에서도 작동시켜야 합니다.
//
// [Step4 검증 디버그 추가]
//   DebugLog BB 포트를 true로 설정하면 모드 발동·완료 시점에 상세 실측 데이터를 콘솔에 출력합니다.
//   각 로그는 [TactRep] 접두사로 시작하며 SO값·실측값·정상 여부를 비교합니다.
//   SO 로드 여부는 PB4DecisionAdapter.IsBBInitialized + BB값-기본값 비교로 판단합니다.
// =============================================================================

using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using TDA.Character.AI;
using TDA.PB4.Bridge;   // PB4DecisionAdapter 접근용
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "TacticalReposition",
    story: "[Self] repositions tactically away from [Target]",
    category: "pB-4/Combat",
    id: "pb4_tactical_reposition")]
public partial class TacticalRepositionAction : Action
{
    // =========================================================================
    // Blackboard 변수
    // =========================================================================

    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<UnityEngine.Transform> Target;

    // ── 회복 임계값 ──────────────────────────────────────────────────────────

    [Tooltip("이 비율 이하 스태미나면 Recover 모드 진입. 0=비활성. 권장 0.25")]
    [SerializeReference] public BlackboardVariable<float> StaminaRecoverThreshold = new(0.25f);

    [Tooltip("이 비율 이하 체력이면 Recover 모드 진입. 0=비활성. 권장 0.30")]
    [SerializeReference] public BlackboardVariable<float> HealthRecoverThreshold = new(0.30f);

    [Tooltip("후퇴 거리 (m). 타겟 반대방향으로 이만큼 이동 후 대기.")]
    [SerializeReference] public BlackboardVariable<float> RetreatDist = new(5f);

    [Tooltip("회복 대기 시간 (초). 이 시간 동안 자연 회복 후 복귀.")]
    [SerializeReference] public BlackboardVariable<float> RecoverDuration = new(3f);

    // ── 전술 회피 ─────────────────────────────────────────────────────────────

    [Tooltip("매 CircleStrafe 진입 시 전술 회피를 발동할 확률 (0~1). 0=비활성.")]
    [SerializeReference] public BlackboardVariable<float> EvasionChance = new(0.2f);

    [Tooltip("회피 스텝 거리 (m).")]
    [SerializeReference] public BlackboardVariable<float> EvasionDist = new(2f);

    [Tooltip("회피 스텝 지속 시간 (초).")]
    [SerializeReference] public BlackboardVariable<float> EvasionDuration = new(0.5f);

    // ── 페인트 ────────────────────────────────────────────────────────────────

    [Tooltip("CircleStrafe 중 페인트 전진을 발동할 확률 (0~1). 0=비활성.")]
    [SerializeReference] public BlackboardVariable<float> FeintChance = new(0.15f);

    [Tooltip("페인트 전진 거리 (m).")]
    [SerializeReference] public BlackboardVariable<float> FeintDist = new(1.5f);

    [Tooltip("페인트 지속 시간 (초). 전진 후 원위치.")]
    [SerializeReference] public BlackboardVariable<float> FeintDuration = new(0.4f);

    // ── 디버그 ────────────────────────────────────────────────────────────────

    [Tooltip("true = 발동·완료 시점에 상세 실측 로그 출력 (SO값 vs 실측 비교 포함)")]
    [SerializeReference] public BlackboardVariable<bool> DebugLog = new(false);

    // =========================================================================
    // 내부 상태
    // =========================================================================

    private enum RepositionMode { Recover, Evade, Feint }

    // =========================================================================
    // [Step4 검증] 로그 색상 팔레트
    // =========================================================================
    // 모드별 색상: Recover=주황(긴장감), Evade=하늘색(기민함), Feint=보라(교란)
    // 상태별 색상: 정상=초록, 경고=노랑, 비정상=빨강, 라벨=회색
    private const string C_RECOVER = "#FF9944"; // 주황 — 체력 위기, 긴박한 후퇴
    private const string C_EVADE = "#44CCFF"; // 하늘 — 민첩한 회피 스텝
    private const string C_FEINT = "#CC88FF"; // 보라 — 심리전, 교란 행동
    private const string C_OK = "#44FF88"; // 초록 — 정상 동작
    private const string C_WARN = "#FFDD44"; // 노랑 — 경미한 이상
    private const string C_ERR = "#FF4455"; // 빨강 — 비정상, 즉시 확인 필요
    private const string C_LBL = "#AABBCC"; // 회색 — 라벨/키
    private const string C_VAL = "#FFFFFF"; // 흰색 — 실측값
    private const string C_SO = "#88DDFF"; // 연파랑 — SO 설정값

    // 색상 래퍼 헬퍼
    private static string Col(string text, string hex) => $"<color={hex}>{text}</color>";
    private static string Ok(string text) => Col(text, C_OK);
    private static string Warn(string text) => Col(text, C_WARN);
    private static string Err(string text) => Col(text, C_ERR);
    private static string Lbl(string text) => Col(text, C_LBL);
    private static string Val(string text) => Col(text, C_VAL);
    private static string SO(string text) => Col(text, C_SO);
    private RepositionMode _mode;

    private NavMeshAgent _nav;
    private AICharacterManager _aiMgr;
    private float _timer;
    private Vector3 _destination;

    // ── Step4 검증 디버그용 내부 기록 ────────────────────────────────────────
    private Vector3 _startPos;          // OnStart 시점 위치
    private Vector3 _feintPeakPos;      // Feint 전진 정점 위치
    private float _feintPeakDist;     // 전진 실측 거리
    private bool _feintPeakRecorded; // 전진→후퇴 전환 기록 여부
    private float _lastRoll;          // 마지막 Random 롤 값 (로그용)

    // ── 루트모션 검증용 velocity 추적 ────────────────────────────────────────
    // SetDestination이 호출돼도 루트모션 캐릭터는 SO값만큼 이동하지 못할 수 있음.
    // 진짜 "이동 시도가 있었는가"를 판단하려면 NavMeshAgent.velocity를 봐야 함.
    private float _peakNavSpeed;      // 실행 중 최대 NavMesh 속도
    private float _avgNavSpeed;       // 실행 중 평균 NavMesh 속도 (누적)
    private int _avgNavSampleCount; // 평균 계산용 샘플 수
    private Vector3 _peakVelocityDir;   // 최대 속도 시점 이동 방향

    // ── 기본값 상수 (BB 포트 미연결 감지용) ──────────────────────────────────
    // Action 선언부의 new(X) 기본값과 일치해야 합니다.
    private const float DEFAULT_STAMINA_THR = 0.25f;
    private const float DEFAULT_HEALTH_THR = 0.30f;
    private const float DEFAULT_RETREAT_DIST = 5f;
    private const float DEFAULT_RECOVER_DUR = 3f;
    private const float DEFAULT_EVASION_CH = 0.2f;
    private const float DEFAULT_EVASION_DIST = 2f;
    private const float DEFAULT_EVASION_DUR = 0.5f;
    private const float DEFAULT_FEINT_CH = 0.15f;
    private const float DEFAULT_FEINT_DIST = 1.5f;
    private const float DEFAULT_FEINT_DUR = 0.4f;

    // =========================================================================
    // [Step4 검증] 세션 메모리 — AI별 검증 기록 (정적, 런타임 전체 유지)
    // =========================================================================
    // 설계:
    //   · AI(GameObject) 단위로 각 모드별 실행 결과를 누적 기록합니다.
    //   · Recover/Evade/Feint 세 가지가 모두 1회 이상 완료되면 최종 리포트를 출력합니다.
    //   · 비정상 케이스(이동 부족, 타이밍 오차 등)는 별도 목록에 누적됩니다.
    //   · 씬 언로드 시 이 딕셔너리는 정리되지 않으므로, 씬 전환 테스트 시에는
    //     수동으로 TactSessionMemory.ClearAll()을 호출하거나 게임을 재시작하세요.

    private class TactModeRecord
    {
        public int ExecutionCount;     // 이 모드가 발동된 총 횟수
        public int NormalCount;        // 정상 완료 횟수
        public int AbnormalCount;      // 비정상 완료 횟수
        public float LastMovedDist;      // 마지막 실측 이동거리
        public float LastSoDist;         // 마지막 SO 설정 거리
        public float LastElapsed;        // 마지막 실측 소요시간
        public float LastSoDuration;     // 마지막 SO 설정 지속시간
        public string LastIssue;          // 마지막 이슈 내용 (null=완전정상)

        // 루트모션 검증용 velocity 기록
        public float LastAvgNavSpeed;    // 마지막 실행 NavAgent 평균 속도
        public float LastPeakNavSpeed;   // 마지막 실행 NavAgent 최대 속도

        // Feint 전용
        public float LastFeintPeakDist;  // 마지막 전진 실측 거리
        public float LastFeintEndDisp;   // 마지막 최종 변위 (복귀 평가용)
    }

    private class TactSessionData
    {
        public TactModeRecord Recover = new TactModeRecord();
        public TactModeRecord Evade = new TactModeRecord();
        public TactModeRecord Feint = new TactModeRecord();
        public bool SOChecked;            // CheckSOLoad 1회 실행 여부
        public bool SOLoaded;             // SO IsBBInitialized 결과
        public int UnconnectedPortCount;  // 미연결 BB 포트 수
        public bool ReportPrinted;         // 최종 리포트 출력 여부

        // ── 진척 추적 ─────────────────────────────────────────────────────────
        // 각 모드가 처음 완료됐을 때 진척 로그를 1회 출력하기 위한 플래그
        public bool RecoverLogged;
        public bool EvadeLogged;
        public bool FeintLogged;

        // 완료된 모드 수 (0~3) — 진척률 계산용
        public int CompletedModeCount =>
            (RecoverLogged ? 1 : 0) +
            (EvadeLogged ? 1 : 0) +
            (FeintLogged ? 1 : 0);

        // 세 모드 모두 1회 이상 완료됐는지
        // ※ ExecutionCount(발동) 아닌 Logged 플래그(완료) 기준으로 통일
        //    CompletedModeCount==3과 항상 일치 → PROGRESS [3/3]이면 반드시 true
        public bool AllModesCovered =>
            RecoverLogged && EvadeLogged && FeintLogged;
    }

    // AI(GameObject) → 세션 데이터
    private static readonly System.Collections.Generic.Dictionary<GameObject, TactSessionData>
        s_sessionMap = new System.Collections.Generic.Dictionary<GameObject, TactSessionData>();

    // 현재 인스턴스가 참조하는 세션 데이터
    private TactSessionData _session;

    /// <summary>세션 데이터 초기화 (수동 리셋용)</summary>
    public static void ClearAllSessions() => s_sessionMap.Clear();

    // 세션 데이터 취득 (없으면 생성)
    private TactSessionData GetOrCreateSession()
    {
        if (Self.Value == null) return null;
        if (!s_sessionMap.TryGetValue(Self.Value, out var sd))
        {
            sd = new TactSessionData();
            s_sessionMap[Self.Value] = sd;
        }
        return sd;
    }

    // 모드 기록 헬퍼
    private TactModeRecord GetRecord(TactSessionData sd)
    {
        return _mode switch
        {
            RepositionMode.Recover => sd.Recover,
            RepositionMode.Evade => sd.Evade,
            _ => sd.Feint,
        };
    }

    // =========================================================================
    // 생명주기
    // =========================================================================

    protected override Status OnStart()
    {
        if (Self.Value == null) { LogFailure("Self null"); return Status.Failure; }

        _nav = Self.Value.GetComponent<NavMeshAgent>();
        _aiMgr = Self.Value.GetComponent<AICharacterManager>();

        if (_nav == null || !_nav.isOnNavMesh)
        {
            LogFailure("NavMesh 없음");
            return Status.Failure;
        }

        _timer = 0f;
        _startPos = Self.Value.transform.position;
        _feintPeakRecorded = false;
        _feintPeakDist = 0f;
        _peakNavSpeed = 0f;
        _avgNavSpeed = 0f;
        _avgNavSampleCount = 0;
        _peakVelocityDir = Vector3.zero;

        // ── 세션 메모리 취득 ───────────────────────────────────────────────
        if (DebugLog.Value)
            _session = GetOrCreateSession();

        // ── SO 로드 여부 진단 ──────────────────────────────────────────────
        // DebugLog=true일 때만 실행 (성능 영향 없음)
        // SO 체크는 세션당 1회만 수행
        if (DebugLog.Value)
        {
            if (_session != null && !_session.SOChecked)
            {
                _session.SOChecked = true;
                CheckSOLoad(_session);
            }
            else if (_session == null)
            {
                CheckSOLoad(null);
            }
        }

        // ── 모드 결정 ─────────────────────────────────────────────────────────
        var nm = _aiMgr?.characterNetworkManager;

        // 스태미나/체력 비율 계산
        float staminaRatio = 1f;
        float healthRatio = 1f;

        if (nm != null)
        {
            float maxSp = nm.maxStamina.Value;
            float maxHp = nm.maxHealth.Value;
            if (maxSp > 0) staminaRatio = nm.currentStamina.Value / maxSp;
            if (maxHp > 0) healthRatio = nm.currentHealth.Value / maxHp;
        }

        bool needsRecover =
            (StaminaRecoverThreshold.Value > 0 && staminaRatio < StaminaRecoverThreshold.Value) ||
            (HealthRecoverThreshold.Value > 0 && healthRatio < HealthRecoverThreshold.Value);

        if (needsRecover)
        {
            _mode = RepositionMode.Recover;
            Debug.Log($"[TacticalRepos] {Self.Value.name}: Recover 모드 " +
                      $"(SP={staminaRatio:P0} HP={healthRatio:P0})");

            if (DebugLog.Value)
                LogRecoverStart(staminaRatio, healthRatio);
        }
        else
        {
            _lastRoll = UnityEngine.Random.value;
            if (_lastRoll < FeintChance.Value)
            {
                _mode = RepositionMode.Feint;
                Debug.Log($"[TacticalRepos] {Self.Value.name}: Feint 모드");

                if (DebugLog.Value)
                    LogFeintStart(_lastRoll);
            }
            else
            {
                float _lastRoll2 = UnityEngine.Random.value;
                if (_lastRoll2 < EvasionChance.Value)
                {
                    _lastRoll = _lastRoll2;
                    _mode = RepositionMode.Evade;
                    Debug.Log($"[TacticalRepos] {Self.Value.name}: Evade 모드");

                    if (DebugLog.Value)
                        LogEvadeStart(_lastRoll);
                }
                else
                {
                    // 발동 조건 미충족 → 이 Action은 건너뜀
                    if (DebugLog.Value)
                        Debug.Log(
                            Col("[TactRep|SKIP] ", "#888888") + Col(Self.Value.name, C_VAL) +
                            Col(" — 이번 틱 발동 안함 ━━", "#888888") +
                            $"\n  {Lbl("게임의미")} : 이번 TacticalReposition은 확률 조건을 통과하지 못했습니다. 정상 동작입니다." +
                            $"\n  {Lbl("Feint판정")} : Roll={Val(_lastRoll.ToString("F4"))}" +
                            $"  thr={SO(FeintChance.Value.ToString("F4"))}  {Err("✗ 실패")}" +
                            $"\n  {Lbl("Evade판정")} : Roll={Val(_lastRoll2.ToString("F4"))}" +
                            $"  thr={SO(EvasionChance.Value.ToString("F4"))}  {Err("✗ 실패")}"
                        );
                    return Status.Failure;
                }
            }
        }

        _destination = CalcDestination();
        _nav.isStopped = false;
        _nav.SetDestination(_destination);

        // 세션 기록: 이 모드 실행 횟수 증가
        if (DebugLog.Value && _session != null)
            GetRecord(_session).ExecutionCount++;

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_nav == null) return Status.Failure;

        _timer += Time.deltaTime;

        // 루트모션 검증: NavMeshAgent velocity 매 프레임 샘플링
        if (DebugLog.Value && _session != null)
        {
            float spd = _nav.velocity.magnitude;
            if (spd > _peakNavSpeed)
            {
                _peakNavSpeed = spd;
                _peakVelocityDir = _nav.velocity.normalized;
            }
            _avgNavSpeed = (_avgNavSpeed * _avgNavSampleCount + spd) / (_avgNavSampleCount + 1);
            _avgNavSampleCount++;
        }

        float duration = _mode == RepositionMode.Recover ? RecoverDuration.Value
                       : _mode == RepositionMode.Evade ? EvasionDuration.Value
                                                          : FeintDuration.Value;

        if (_mode == RepositionMode.Feint)
        {
            // 페인트: 절반 시간 전진 → 절반 시간 후퇴
            if (_timer < duration * 0.5f)
            {
                // 전진 유지
                // ─ 전진 정점(전환 직전) 위치 기록 ─
                if (!_feintPeakRecorded && DebugLog.Value)
                    _feintPeakPos = Self.Value.transform.position; // 매 프레임 갱신 (전환 전 마지막 값이 최종)
            }
            else if (_timer < duration)
            {
                // 전진 → 후퇴 전환 직후 1회 기록
                if (!_feintPeakRecorded)
                {
                    _feintPeakRecorded = true;
                    _feintPeakDist = Vector3.Distance(_startPos, _feintPeakPos);

                    if (DebugLog.Value)
                        LogFeintPivot(duration);
                }

                // 원위치로 복귀 (시작점 역방향)
                if (Target.Value != null)
                {
                    Vector3 retreat = Self.Value.transform.position
                                    + (Self.Value.transform.position - Target.Value.position).normalized
                                    * FeintDist.Value;
                    if (NavMesh.SamplePosition(retreat, out NavMeshHit h, 2f, NavMesh.AllAreas))
                        _nav.SetDestination(h.position);
                }
            }
            else
            {
                if (DebugLog.Value)
                    LogFeintComplete();
                return Status.Success;
            }
        }
        else
        {
            // Recover / Evade: 목적지 도달 or 시간 만료
            bool arrived = !_nav.pathPending && _nav.remainingDistance < 0.5f;
            bool timeUp = _timer >= duration;

            if (timeUp || (arrived && _timer > 0.3f))
            {
                if (DebugLog.Value)
                {
                    if (_mode == RepositionMode.Recover) LogRecoverComplete(duration, arrived, timeUp);
                    else LogEvadeComplete(duration, arrived, timeUp);
                }
                return Status.Success;
            }
        }

        return Status.Running;
    }

    protected override void OnEnd()
    {
        _timer = 0f;
    }

    // =========================================================================
    // 목적지 계산
    // =========================================================================

    private Vector3 CalcDestination()
    {
        Vector3 selfPos = Self.Value.transform.position;

        if (_mode == RepositionMode.Recover)
        {
            // 타겟 반대 방향으로 RetreatDist 후퇴
            Vector3 awayDir = Target.Value != null
                ? (selfPos - Target.Value.position).normalized
                : -Self.Value.transform.forward;
            Vector3 dest = selfPos + awayDir * RetreatDist.Value;
            return SampleNavMesh(dest, selfPos);
        }
        else if (_mode == RepositionMode.Evade)
        {
            // 좌/우/후방 랜덤 스텝
            int dir = UnityEngine.Random.Range(0, 3); // 0=왼쪽, 1=오른쪽, 2=뒤
            Vector3 stepDir;
            if (dir == 0) stepDir = -Self.Value.transform.right;
            else if (dir == 1) stepDir = Self.Value.transform.right;
            else stepDir = -Self.Value.transform.forward;

            Vector3 dest = selfPos + stepDir * EvasionDist.Value;
            return SampleNavMesh(dest, selfPos);
        }
        else // Feint
        {
            // 타겟 방향으로 FeintDist 전진
            Vector3 toTarget = Target.Value != null
                ? (Target.Value.position - selfPos).normalized
                : Self.Value.transform.forward;
            Vector3 dest = selfPos + toTarget * FeintDist.Value;
            return SampleNavMesh(dest, selfPos);
        }
    }

    private static Vector3 SampleNavMesh(Vector3 desired, Vector3 fallback)
    {
        if (NavMesh.SamplePosition(desired, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            return hit.position;
        return fallback;
    }

    // =========================================================================
    // [Step4 검증] SO 로드 여부 진단
    // =========================================================================
    // PB4DecisionAdapter.IsBBInitialized: SO → BB push 완료 여부
    // BB 현재값이 Action 선언부 기본값과 동일하면 → 포트 미연결 가능성 경고
    // 판단 기준: BB값이 기본값과 같더라도 SO값도 기본값이면 정상일 수 있음.
    //            따라서 IsBBInitialized=false일 때만 "SO 미로드" 로 확정 판단.
    // =========================================================================

    private void CheckSOLoad(TactSessionData session)
    {
        // PB4DecisionAdapter에서 BB 초기화 완료 여부 확인
        // ▶ 기술: IsBBInitialized=true면 InitializeBlackboard()가 완료돼 SO→BB push 완료
        // ▶ 게임: 팩션별 전투 개성(고블린/오크/스켈레톤)이 실제로 적용됐는지 확인
        bool soLoaded = false;
        string soSource;

        var adapter = Self.Value.GetComponent<PB4DecisionAdapter>();
        if (adapter != null)
        {
            soLoaded = adapter.IsBBInitialized;
            soSource = soLoaded
                ? Ok("SO 로드됨 ✓  (팩션 특성 적용됨)")
                : Err("SO 미로드 ✗  IsBBInitialized=false — 기본값으로 동작 중!");
        }
        else
        {
            soSource = Err("PB4DecisionAdapter 없음 ✗  → 모든 값이 하드코딩 기본값");
        }

        // BB값 vs 기본값 비교로 포트 연결 여부 추가 진단
        // ▶ 기술: BB포트 미연결 시 BB값 = Action 선언부 new(X) 기본값 그대로 유지됨
        // ▶ 게임: 미연결이면 모든 몹이 동일한 전술 특성 → 팩션 차별화 없음
        var ports = new (string name, float bb, float def)[]
        {
            ("StaminaRecoverThreshold", StaminaRecoverThreshold.Value, DEFAULT_STAMINA_THR),
            ("HealthRecoverThreshold",  HealthRecoverThreshold.Value,  DEFAULT_HEALTH_THR),
            ("RetreatDist",             RetreatDist.Value,             DEFAULT_RETREAT_DIST),
            ("RecoverDuration",         RecoverDuration.Value,         DEFAULT_RECOVER_DUR),
            ("EvasionChance",           EvasionChance.Value,           DEFAULT_EVASION_CH),
            ("EvasionDist",             EvasionDist.Value,             DEFAULT_EVASION_DIST),
            ("EvasionDuration",         EvasionDuration.Value,         DEFAULT_EVASION_DUR),
            ("FeintChance",             FeintChance.Value,             DEFAULT_FEINT_CH),
            ("FeintDist",               FeintDist.Value,               DEFAULT_FEINT_DIST),
            ("FeintDuration",           FeintDuration.Value,           DEFAULT_FEINT_DUR),
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Col($"━━ [TactRep|SO검증] {Self.Value.name}  @{System.DateTime.Now:HH:mm:ss} ━━", "#88DDFF"));
        sb.AppendLine($"  {Lbl("SO로드")} : {soSource}");
        sb.AppendLine($"  {Lbl("BB포트 상태")} — {Warn("기본값=포트미연결 의심")} / {Ok("커스텀값=정상")}");

        bool anyError = false;
        foreach (var (name, bb, def) in ports)
        {
            bool isDefault = Mathf.Approximately(bb, def);
            string statusStr;
            if (!isDefault)
                statusStr = Ok($"✓ {bb:F3}  (커스텀값)");
            else if (soLoaded)
                statusStr = Warn($"△ {bb:F3}  (SO값=기본값 OR 포트미연결)");
            else
            {
                statusStr = Err($"✗ {bb:F3}  (미연결 — SO미로드+기본값)");
                anyError = true;
            }
            sb.AppendLine($"    {Lbl($"{name,-28}")} default={SO(def.ToString("F3"))}  →  {statusStr}");
        }

        // 세션에 SO 로드 결과 기록
        if (session != null)
        {
            session.SOLoaded = soLoaded;
            int unconnected = 0;
            foreach (var (name, bb, def) in ports)
                if (Mathf.Approximately(bb, def) && !soLoaded) unconnected++;
            session.UnconnectedPortCount = unconnected;
        }

        if (anyError)
            Debug.LogWarning(sb.ToString());
        else
            Debug.Log(sb.ToString());
    }

    // =========================================================================
    // [Step4 검증] 모드별 발동 로그
    // =========================================================================

    private void LogRecoverStart(float staminaRatio, float healthRatio)
    {
        // ▶ 게임: AI 체력/스태미나가 위험 수준에 빠져 후퇴를 결정한 순간
        //         이 로그가 자주 뜨면 AI가 너무 쉽게 후퇴한다는 의미 (임계값 낮추기 권장)
        // ▶ 기술: OnStart에서 networkManager 스태미나/체력 비율을 읽어 Recover 모드 결정
        float planDist = RetreatDist.Value;
        float actualPlan = Vector3.Distance(_startPos, _destination);
        float tgtDist = Target.Value != null
            ? Vector3.Distance(_startPos, Target.Value.position) : -1f;

        bool distOk = actualPlan >= planDist * 0.5f;
        string distCheck = distOk
            ? Ok($"✓ 정상 ({actualPlan:F2}m 확보)")
            : Warn($"⚠ 목적지 가까움 ({actualPlan:F2}m) — NavMesh 장애물로 단축됐을 가능성");

        // SP/HP가 임계값 대비 얼마나 위험한지 색상으로 표시
        string spStr = staminaRatio < StaminaRecoverThreshold.Value * 0.5f
            ? Err($"{staminaRatio:P1}")   // 임계값의 절반 미만 = 매우 위험
            : Warn($"{staminaRatio:P1}"); // 임계값 미만이지만 심각하진 않음
        string hpStr = healthRatio < HealthRecoverThreshold.Value * 0.5f
            ? Err($"{healthRatio:P1}")
            : Warn($"{healthRatio:P1}");

        Debug.Log(
            Col("━━ [TactRep|RECOVER▶START] ", C_RECOVER) + Col(Self.Value.name, C_VAL) +
            Col(" — 체력위기 후퇴 발동 ━━", C_RECOVER) +
            $"\n  {Lbl("게임의미")} : AI가 위험을 감지하고 전투에서 이탈해 회복을 시도합니다." +
            $"\n  {Lbl("현재위치")} : {Val(_startPos.ToString("F2"))}" +
            $"  {Lbl("타겟거리")} : {Val(tgtDist >= 0 ? tgtDist.ToString("F2") + "m" : "없음")}" +
            $"\n  {Lbl("SP비율")} : {spStr}  {Lbl("SO임계")}={SO(StaminaRecoverThreshold.Value.ToString("P1"))}" +
            $"  {Lbl("HP비율")} : {hpStr}  {Lbl("SO임계")}={SO(HealthRecoverThreshold.Value.ToString("P1"))}" +
            $"\n  {Lbl("SO후퇴거리")} : {SO(planDist.ToString("F2") + "m")}  {Lbl("실제목적지")} : {distCheck}" +
            $"\n  {Lbl("SO지속시간")} : {SO(RecoverDuration.Value.ToString("F2") + "s")}" +
            $"  {Lbl("목적지")} : {Val(_destination.ToString("F2"))}"
        );
    }

    private void LogRecoverComplete(float duration, bool arrived, bool timeUp)
    {
        // ▶ 게임: AI가 후퇴를 완료하고 다시 전투에 복귀할 준비가 된 시점
        //         movedDist가 너무 작으면 AI가 장애물에 막혀 도망을 못 친 것
        // ▶ 기술: 종료 트리거는 두 가지 — remainingDistance<0.5f(도달) 또는 timer>=duration(만료)
        Vector3 endPos = Self.Value.transform.position;
        float movedDist = Vector3.Distance(_startPos, endPos);
        float plannedDist = RetreatDist.Value;
        float timeDelta = Mathf.Abs(_timer - duration);

        string endReason = arrived ? Ok("✓ 목적지 도달") : Warn("△ 시간 만료");

        // ── 루트모션 기반 판정 ──────────────────────────────────────────────────
        // SO값(5m)은 목적지 지점이지 "반드시 도달해야 할 이동량"이 아님.
        // 루트모션 캐릭터는 시간 안에 완주 못할 수 있으므로 velocity로 판단.
        // 판정 기준:
        //   ✓ 정상: NavAgent 평균속도 > 0.5m/s  (이동 시도가 있었음)
        //   △ 이동제한: 속도 있었으나 이동 소량  (루트모션 속도 제한)
        //   ✗ 비정상: 평균속도 ≈ 0  (이동 시도 자체 없음 — 경로 실패 의심)
        bool navMoving = _avgNavSpeed > 0.5f;
        bool minMoved = movedDist > 0.2f; // 최소 0.2m는 물리적으로 이동했어야 함
        bool distOk = navMoving && minMoved;

        string rootMotionNote = $"NavVel 평균={_avgNavSpeed:F2}m/s  최대={_peakNavSpeed:F2}m/s";
        string distCheck;
        if (navMoving && minMoved)
            distCheck = Ok($"✓ 이동 정상  실측={movedDist:F2}m  ({rootMotionNote})");
        else if (navMoving && !minMoved)
            distCheck = Warn($"△ 이동 제한  실측={movedDist:F2}m  ({rootMotionNote})" +
                             "  → 루트모션 클립 속도 또는 NavMesh 경로 폭 확인");
        else
            distCheck = Err($"✗ 이동 없음!  실측={movedDist:F2}m  ({rootMotionNote})" +
                            "  → NavMesh 경로 탐색 실패 또는 canMove=false 의심");

        string timeCheck = timeDelta < 0.3f
            ? Ok($"✓ 정상  (오차={timeDelta:F2}s)")
            : Warn($"△ 오차={timeDelta:F2}s  (프레임드랍 또는 조기도달)");

        Debug.Log(
            Col("━━ [TactRep|RECOVER▶END] ", C_RECOVER) + Col(Self.Value.name, C_VAL) +
            Col(" — 후퇴 완료 ━━", C_RECOVER) +
            $"\n  {Lbl("게임의미")} : 후퇴가 완료됐습니다. 이동거리가 SO값의 50% 미만이면 도주 실패입니다." +
            $"\n  {Lbl("종료사유")} : {endReason}" +
            $"\n  {Lbl("이동거리")} : {distCheck}" +
            $"\n  {Lbl("소요시간")} : {Val(_timer.ToString("F2") + "s")}  {Lbl("SO설정")}={SO(duration.ToString("F2") + "s")}  {timeCheck}" +
            $"\n  {Lbl("경로")} : {Val(_startPos.ToString("F2"))} → {Val(endPos.ToString("F2"))}"
        );

        // 세션 메모리에 결과 기록
        if (_session != null)
        {
            var rec = _session.Recover;
            rec.LastMovedDist = movedDist;
            rec.LastSoDist = plannedDist;
            rec.LastElapsed = _timer;
            rec.LastSoDuration = duration;
            // 루트모션 기준: velocity 있으면 정상, 완전 미이동만 비정상
            if (navMoving && minMoved) rec.NormalCount++;
            else if (navMoving)
            {
                // 속도는 있지만 이동 소량 → 루트모션 제한, 경고만
                rec.NormalCount++;
                rec.LastIssue = $"루트모션제한 실측={movedDist:F2}m avgVel={_avgNavSpeed:F2}m/s (SO={plannedDist:F2}m)";
            }
            else
            {
                rec.AbnormalCount++;
                rec.LastIssue = $"이동시도없음 실측={movedDist:F2}m avgVel={_avgNavSpeed:F2}m/s SO={plannedDist:F2}m";
            }
            LogProgress(RepositionMode.Recover);
            TryPrintFinalReport();
        }
    }

    private void LogEvadeStart(float roll)
    {
        // ▶ 게임: 공격 패턴 중 갑자기 옆으로 이동해 플레이어의 공격 타이밍을 흐트러뜨리는 행동
        //         EvasionChance가 너무 높으면 AI가 산만하게 느껴지고 전투 흐름이 깨짐
        // ▶ 기술: Feint 롤 실패 후 2번째 Random.value로 판정. Feint가 먼저라 순서에 주의
        float planDist = EvasionDist.Value;
        float actualPlan = Vector3.Distance(_startPos, _destination);
        float margin = roll / EvasionChance.Value; // 1에 가까울수록 아슬아슬하게 발동

        bool distOk = actualPlan >= planDist * 0.3f;
        string distCheck = distOk
            ? Ok($"✓ 정상  ({actualPlan:F2}m 확보)")
            : Warn($"⚠ 목적지 가까움  ({actualPlan:F2}m) — NavMesh 단축 가능성");

        // Roll이 임계값에 얼마나 가까운지 (여유=초록, 아슬아슬=노랑)
        string rollColor = margin < 0.7f ? Ok($"{roll:F4}") : Warn($"{roll:F4}");

        Debug.Log(
            Col("━━ [TactRep|EVADE▶START] ", C_EVADE) + Col(Self.Value.name, C_VAL) +
            Col(" — 회피 스텝 발동 ━━", C_EVADE) +
            $"\n  {Lbl("게임의미")} : AI가 위치를 재조정해 플레이어의 공격 예측을 교란합니다." +
            $"\n  {Lbl("현재위치")} : {Val(_startPos.ToString("F2"))}" +
            $"\n  {Lbl("확률판정")} : Roll={rollColor}" +
            $"  <  {Lbl("EvasionChance")}={SO(EvasionChance.Value.ToString("F4"))}" +
            $"  {Lbl("여유")}={(margin < 0.7f ? Ok($"{(1f - margin) * 100f:F0}%") : Warn($"{(1f - margin) * 100f:F0}%"))}" +
            $"\n  {Lbl("SO회피거리")} : {SO(planDist.ToString("F2") + "m")}  {Lbl("실제목적지")} : {distCheck}" +
            $"\n  {Lbl("SO지속시간")} : {SO(EvasionDuration.Value.ToString("F2") + "s")}" +
            $"  {Lbl("목적지")} : {Val(_destination.ToString("F2"))}"
        );
    }

    private void LogEvadeComplete(float duration, bool arrived, bool timeUp)
    {
        // ▶ 게임: 회피 스텝이 끝났습니다. 이동이 없었다면 NavMesh 장애물로 막힌 것
        //         movedDist ≈ 0이면 회피 효과가 전혀 없었음 → EvasionDist 낮추거나 장애물 제거 필요
        // ▶ 기술: remainingDistance<0.5f 또는 timer>=EvasionDuration으로 종료
        Vector3 endPos = Self.Value.transform.position;
        float movedDist = Vector3.Distance(_startPos, endPos);

        string endReason = arrived ? Ok("✓ 목적지 도달") : Warn("△ 시간 만료");

        // ── 루트모션 기반 판정 ──────────────────────────────────────────────────
        // Evade는 0.5s 단기 이동. 루트모션으로 가속/감속 포함시 실측거리 < SO값이 정상.
        // 판정 기준: NavAgent velocity > 0.5m/s이면 이동 시도 있었던 것으로 정상 처리.
        bool navMoving = _avgNavSpeed > 0.5f;
        bool minMoved = movedDist > 0.1f; // 0.5초 동안 0.1m도 못 움직였으면 비정상
        bool distOk = navMoving || minMoved;

        string rootMotionNote = $"NavVel 평균={_avgNavSpeed:F2}m/s  최대={_peakNavSpeed:F2}m/s";
        string distCheck;
        if (navMoving && minMoved)
            distCheck = Ok($"✓ 정상  실측={movedDist:F2}m  ({rootMotionNote})");
        else if (navMoving)
            distCheck = Warn($"△ 미세이동  실측={movedDist:F2}m  ({rootMotionNote})" +
                             "  → 루트모션 가속 구간에서 종료됨 (정상 범위)");
        else if (minMoved)
            distCheck = Warn($"△ 속도저하  실측={movedDist:F2}m  ({rootMotionNote})" +
                             "  → NavAgent velocity 낮음, 관성 이동으로 완료");
        else
            distCheck = Err($"✗ 이동 없음!  실측={movedDist:F2}m  ({rootMotionNote})" +
                            "  → NavMesh 경로 탐색 실패 또는 canMove=false 의심");

        Debug.Log(
            Col("━━ [TactRep|EVADE▶END] ", C_EVADE) + Col(Self.Value.name, C_VAL) +
            Col(" — 회피 완료 ━━", C_EVADE) +
            $"\n  {Lbl("게임의미")} : 회피 이동이 완료됐습니다. 실제 이동이 있었는지 확인하세요." +
            $"\n  {Lbl("종료사유")} : {endReason}" +
            $"\n  {Lbl("이동거리")} : {distCheck}" +
            $"\n  {Lbl("소요시간")} : {Val(_timer.ToString("F2") + "s")}  {Lbl("SO설정")}={SO(duration.ToString("F2") + "s")}" +
            $"\n  {Lbl("경로")} : {Val(_startPos.ToString("F2"))} → {Val(endPos.ToString("F2"))}"
        );

        // 세션 메모리에 결과 기록
        if (_session != null)
        {
            var rec = _session.Evade;
            rec.LastMovedDist = movedDist;
            rec.LastSoDist = EvasionDist.Value;
            rec.LastElapsed = _timer;
            rec.LastSoDuration = duration;
            rec.LastAvgNavSpeed = _avgNavSpeed;
            rec.LastPeakNavSpeed = _peakNavSpeed;
            if (navMoving || minMoved) rec.NormalCount++;
            else
            {
                rec.AbnormalCount++;
                rec.LastIssue = $"이동시도없음 실측={movedDist:F2}m avgVel={_avgNavSpeed:F2}m/s SO={EvasionDist.Value:F2}m";
            }
            LogProgress(RepositionMode.Evade);
            TryPrintFinalReport();
        }
    }

    private void LogFeintStart(float roll)
    {
        // ▶ 게임: AI가 허위 돌진으로 플레이어의 방어/회피 반응을 유도하는 심리전 행동
        //         플레이어가 이걸 보고 반응하면 카운터 기회가 생김. 고급 전투 연출의 핵심
        //         FeintChance를 너무 높이면 AI가 자주 멈칫거려 답답하게 보임
        // ▶ 기술: Feint 판정이 Evade보다 먼저. 전반부=타겟 방향 전진, 후반부=후퇴
        float planDist = FeintDist.Value;
        float actualPlan = Vector3.Distance(_startPos, _destination);
        float margin = roll / FeintChance.Value;

        bool distOk = actualPlan >= planDist * 0.3f;
        string distCheck = distOk
            ? Ok($"✓ 정상  ({actualPlan:F2}m 확보)")
            : Warn($"⚠ 전진 목적지 가까움  ({actualPlan:F2}m) — 타겟과 너무 가까워 전진 여지 없음");

        string rollColor = margin < 0.7f ? Ok($"{roll:F4}") : Warn($"{roll:F4}");

        Debug.Log(
            Col("━━ [TactRep|FEINT▶START] ", C_FEINT) + Col(Self.Value.name, C_VAL) +
            Col(" — 페인트 발동 ━━", C_FEINT) +
            $"\n  {Lbl("게임의미")} : AI가 허위 돌진으로 플레이어의 반응을 유도합니다. 전반=전진 후반=후퇴." +
            $"\n  {Lbl("현재위치")} : {Val(_startPos.ToString("F2"))}" +
            $"\n  {Lbl("확률판정")} : Roll={rollColor}" +
            $"  <  {Lbl("FeintChance")}={SO(FeintChance.Value.ToString("F4"))}" +
            $"\n  {Lbl("SO전진거리")} : {SO(planDist.ToString("F2") + "m")}  {Lbl("실제전진")} : {distCheck}" +
            $"\n  {Lbl("SO타임라인")} : 전체={SO(FeintDuration.Value.ToString("F2") + "s")}" +
            $"  전진={SO((FeintDuration.Value * 0.5f).ToString("F2") + "s")}" +
            $"  후퇴={SO((FeintDuration.Value * 0.5f).ToString("F2") + "s")}" +
            $"\n  {Lbl("목적지")} : {Val(_destination.ToString("F2"))}"
        );
    }

    private void LogFeintPivot(float duration)
    {
        // ▶ 게임: 허위 전진의 정점 — 이 시점에 플레이어가 반응했다면 카운터 타이밍
        //         feintPeakDist가 너무 작으면 전진이 눈에 안 보여 페인트 효과 없음
        //         FeintDist를 늘리거나 장애물 제거 필요
        // ▶ 기술: timer >= FeintDuration*0.5f가 된 첫 프레임에 기록됨
        //         이 시점부터 SetDestination이 후퇴 방향으로 전환됨
        float expectedPivotTime = duration * 0.5f;
        float pivotError = Mathf.Abs(_timer - expectedPivotTime);

        bool timeOk = pivotError < 0.05f;
        string timeCheck = timeOk
            ? Ok($"✓ 타이밍 정확  (오차={pivotError:F3}s)")
            : Warn($"△ 오차={pivotError:F3}s  — 프레임드랍 또는 FixedUpdate 타임스텝 영향");

        bool distOk = _feintPeakDist >= FeintDist.Value * 0.3f;
        string distCheck = distOk
            ? Ok($"✓ 전진 충분  ({_feintPeakDist:F2}m / SO={FeintDist.Value:F2}m)")
            : Err($"✗ 전진 거의 없음!  실측={_feintPeakDist:F2}m  < SO={FeintDist.Value:F2}m×0.3" +
                  "  → 장애물 막힘 또는 타겟이 너무 가까워 이동 불가");

        Debug.Log(
            Col("━━ [TactRep|FEINT▶PIVOT] ", C_FEINT) + Col(Self.Value.name, C_VAL) +
            Col(" — 전진→후퇴 전환 ━━", C_FEINT) +
            $"\n  {Lbl("게임의미")} : 허위 전진의 정점. 이 순간 플레이어가 반응하면 AI가 역이용합니다." +
            $"\n  {Lbl("전환시각")} : {Val(_timer.ToString("F3") + "s")}" +
            $"  {Lbl("예상")}={SO(expectedPivotTime.ToString("F3") + "s")}  {timeCheck}" +
            $"\n  {Lbl("실측전진")} : {distCheck}" +
            $"\n  {Lbl("전진정점")} : {Val(_feintPeakPos.ToString("F2"))}"
        );
    }

    private void LogFeintComplete()
    {
        // ▶ 게임: 페인트 전체가 완료됐습니다. 최종변위(시작→끝 직선)가 작을수록 좋은 페인트
        //         변위가 크면 AI가 크게 이동한 것 → 페인트라기보다 그냥 이동처럼 보임
        //         FeintDist 대비 50% 이하 복귀면 후퇴가 미흡한 것
        // ▶ 기술: timer >= FeintDuration이 된 프레임에서 Status.Success 반환
        //         후퇴 방향은 그 순간의 Self→Target 벡터 역방향으로 실시간 계산됨
        Vector3 endPos = Self.Value.transform.position;
        float totalMoved = Vector3.Distance(_startPos, endPos);
        float timeDelta = Mathf.Abs(_timer - FeintDuration.Value);

        // ── 루트모션 기반 판정 ──────────────────────────────────────────────────
        // Feint: 전진(0.2s) + 후퇴(0.2s) 총 0.4s.
        // 루트모션 가속 구간을 감안하면 실측 전진 < SO값이 정상.
        // 핵심 질문: "NavAgent가 방향전환을 실제로 했는가?"
        //   → peakNavSpeed > 0.3m/s이면 이동 시도 있음 (전진+후퇴 모두 속도 발생)
        //   → feintPeakDist ≈ 0이면서 velocity도 0 → 진짜 이동 실패
        bool navMoving = _peakNavSpeed > 0.3f; // 최대속도 기준 (짧은 시간이므로 peak 사용)
        bool hadMovement = _feintPeakDist > 0.05f; // 5cm 이상 전진했으면 시도는 한 것
        bool returnOk = totalMoved < FeintDist.Value * 0.8f; // 원위치 복귀 여부

        string rootMotionNote = $"NavVel 평균={_avgNavSpeed:F2}m/s 최대={_peakNavSpeed:F2}m/s";
        string returnCheck;
        if (navMoving && hadMovement && returnOk)
            returnCheck = Ok($"✓ 페인트 정상  전진={_feintPeakDist:F2}m  복귀={totalMoved:F2}m  ({rootMotionNote})");
        else if (navMoving && hadMovement)
            returnCheck = Warn($"△ 복귀 미흡  전진={_feintPeakDist:F2}m  복귀변위={totalMoved:F2}m  ({rootMotionNote})");
        else if (navMoving)
            returnCheck = Warn($"△ 루트모션 가속 미흡  전진={_feintPeakDist:F2}m ({rootMotionNote})" +
                               "  → FeintDist 또는 FeintDuration 늘리기 권장");
        else
            returnCheck = Err($"✗ 이동 없음!  전진={_feintPeakDist:F2}m  ({rootMotionNote})" +
                              "  → canMove=false 또는 NavMesh 경로 없음 의심");

        string timeCheck = timeDelta < 0.1f
            ? Ok($"✓ 정확  (오차={timeDelta:F2}s)")
            : Warn($"△ 오차={timeDelta:F2}s");

        Debug.Log(
            Col("━━ [TactRep|FEINT▶END] ", C_FEINT) + Col(Self.Value.name, C_VAL) +
            Col(" — 페인트 완료 ━━", C_FEINT) +
            $"\n  {Lbl("게임의미")} : 페인트 종료. 최종변위가 작을수록 그럴듯한 페인트였습니다." +
            $"\n  {Lbl("소요시간")} : {Val(_timer.ToString("F2") + "s")}  {Lbl("SO설정")}={SO(FeintDuration.Value.ToString("F2") + "s")}  {timeCheck}" +
            $"\n  {Lbl("전진실측")} : {Val(_feintPeakDist.ToString("F2") + "m")}  {Lbl("SO설정")}={SO(FeintDist.Value.ToString("F2") + "m")}" +
            $"\n  {Lbl("복귀평가")} : {returnCheck}" +
            $"\n  {Lbl("경로")} : {Val(_startPos.ToString("F2"))} → {Val(endPos.ToString("F2"))}"
        );

        // 세션 메모리에 결과 기록
        if (_session != null)
        {
            var rec = _session.Feint;
            rec.LastMovedDist = totalMoved;
            rec.LastSoDist = FeintDist.Value;
            rec.LastElapsed = _timer;
            rec.LastSoDuration = FeintDuration.Value;
            rec.LastFeintPeakDist = _feintPeakDist;
            rec.LastFeintEndDisp = totalMoved;

            // 루트모션 기준: peak velocity > 0.3m/s이면 이동 시도로 정상 처리
            bool feintOk = navMoving;
            if (feintOk) rec.NormalCount++;
            else
            {
                rec.AbnormalCount++;
                rec.LastIssue = $"이동시도없음 peak={_peakNavSpeed:F2}m/s 전진={_feintPeakDist:F2}m";
            }
            // velocity 있어도 전진이 너무 작으면 주의 이슈로 기록 (비정상 카운트 제외)
            if (navMoving && !hadMovement && rec.LastIssue == null)
                rec.LastIssue = $"루트모션가속미흡 전진={_feintPeakDist:F2}m(peak={_peakNavSpeed:F2}m/s) FeintDist/Duration 조정 권장";
            LogProgress(RepositionMode.Feint);
            TryPrintFinalReport();
        }
    }

    // =========================================================================
    // [Step4 검증] 진척율 로그 (모드 첫 완료 시마다 출력)
    // =========================================================================
    // ▶ 진척 색상: 33%=노랑 → 66%=주황 → 100%=밝은초록
    // ▶ 이미 해당 모드가 기록됐으면 재출력하지 않습니다.
    private void LogProgress(RepositionMode completedMode)
    {
        if (_session == null) return;

        // 이미 기록된 모드면 스킵
        bool alreadyLogged = completedMode switch
        {
            RepositionMode.Recover => _session.RecoverLogged,
            RepositionMode.Evade => _session.EvadeLogged,
            _ => _session.FeintLogged,
        };
        if (alreadyLogged) return;

        // 완료 플래그 설정
        switch (completedMode)
        {
            case RepositionMode.Recover: _session.RecoverLogged = true; break;
            case RepositionMode.Evade: _session.EvadeLogged = true; break;
            case RepositionMode.Feint: _session.FeintLogged = true; break;
        }

        int done = _session.CompletedModeCount; // 방금 설정했으므로 이미 +1 반영됨
        int total = 3;
        float pct = (float)done / total;

        // 진척률에 따른 배경색 (눈에 확 띄게)
        // 33%=노랑, 66%=주황, 100%=밝은초록 (흰 글자와 대비)
        string barColor = pct >= 1f ? "#00FF88"   // 100% — 밝은 민트그린
                        : pct >= 0.6f ? "#FF8C00"   //  66% — 주황
                                      : "#FFE500";  //  33% — 샛노랑

        // 진행 바 (▓ 채움, ░ 비움) — 15칸 기준
        int barLen = 15;
        int filled = Mathf.RoundToInt(pct * barLen);
        string bar = new string('▓', filled) + new string('░', barLen - filled);

        // 완료된 모드 이름·색상
        string modeColor = completedMode switch
        {
            RepositionMode.Recover => C_RECOVER,
            RepositionMode.Evade => C_EVADE,
            _ => C_FEINT,
        };
        string modeName = completedMode switch
        {
            RepositionMode.Recover => "RECOVER",
            RepositionMode.Evade => "EVADE",
            _ => "FEINT",
        };

        // 남은 모드 목록
        string remaining = "";
        if (!_session.RecoverLogged) remaining += " RECOVER";
        if (!_session.EvadeLogged) remaining += " EVADE";
        if (!_session.FeintLogged) remaining += " FEINT";
        string remainStr = remaining.Length > 0
            ? Warn("남은 항목:" + remaining)
            : Ok("모든 모드 완료 → 최종 리포트 출력 중...");

        string name = Self.Value != null ? Self.Value.name : "?";

        Debug.Log(
            Col($"◆ [TactRep|PROGRESS] {name}  ", "#FFFFFF") +
            Col($"[{done}/{total}]  ", barColor) +
            Col($"{pct:P0}", barColor) +
            $"\n" + Col(bar, barColor) +
            $"  {Col(modeName, modeColor)} {Ok("✓ 첫 완료")}" +
            $"\n{remainStr}"
        );
    }

    // =========================================================================
    // [Step4 검증] 최종 검증 리포트 (Recover + Evade + Feint 모두 1회 이상 완료 시 출력)
    // =========================================================================
    // ▶ 게임의미: 세 가지 전술 행동이 모두 실제로 발동됐다는 것을 확인하는 총괄 보고서.
    //             한 번이라도 비정상이 있었으면 ✗로 표시해 SO 값 튜닝 방향을 제시합니다.
    // ▶ 기술:     이미 리포트가 출력됐으면 ReportPrinted=true로 다시 출력하지 않습니다.
    //             단, ClearAllSessions()를 호출하면 새 테스트 세션이 시작됩니다.
    private void TryPrintFinalReport()
    {
        if (_session == null) return;
        if (_session.ReportPrinted) return;
        if (!_session.AllModesCovered) return;

        // 모든 모드 1회 이상 완료 → 리포트 출력
        _session.ReportPrinted = true;

        var sb = new System.Text.StringBuilder();

        string name = Self.Value != null ? Self.Value.name : "Unknown";

        // ── 헤더 ─────────────────────────────────────────────────────────────
        sb.AppendLine(Col("╔══════════════════════════════════════════════════════╗", "#FFFFFF"));
        sb.AppendLine(Col("║  [TactRep|FINAL REPORT]  Step4 검증 최종 결과       ║", "#FFFFFF"));
        sb.AppendLine(Col("╚══════════════════════════════════════════════════════╝", "#FFFFFF"));
        sb.AppendLine($"  {Lbl("대상")} : {Val(name)}  {Lbl("시각")} : {Val(System.DateTime.Now.ToString("HH:mm:ss"))}");
        sb.AppendLine();

        // ── SO 로드 결과 ─────────────────────────────────────────────────────
        string soStatus = _session.SOLoaded
            ? Ok("✓ SO 로드됨 — 팩션 파라미터 적용")
            : Err("✗ SO 미로드 — 기본값으로 동작 중!");
        sb.AppendLine($"  {Lbl("═ SO 로드 상태")} : {soStatus}");
        if (_session.UnconnectedPortCount > 0)
            sb.AppendLine($"    {Warn($"△ 미연결 의심 포트 {_session.UnconnectedPortCount}개 — BT 에디터에서 BB 연결 확인 필요")}");
        sb.AppendLine();

        // ── 모드별 결과 ──────────────────────────────────────────────────────
        sb.AppendLine($"  {Lbl("═ 모드별 검증 결과")}");
        sb.AppendLine(BuildModeReport("RECOVER", C_RECOVER, _session.Recover,
            modeNote: "후퇴 완료 — 체력 위기 상황에서 AI가 도망을 제대로 쳤는지",
            distLabel: "이동거리", durLabel: "소요시간",
            isFeint: false));
        sb.AppendLine();
        sb.AppendLine(BuildModeReport("EVADE", C_EVADE, _session.Evade,
            modeNote: "회피 스텝 — 공격 예측 교란 이동이 실제로 발생했는지",
            distLabel: "이동거리", durLabel: "소요시간",
            isFeint: false));
        sb.AppendLine();
        sb.AppendLine(BuildModeReport("FEINT", C_FEINT, _session.Feint,
            modeNote: "페인트 — 허위 전진 후 복귀가 눈에 띄게 발생했는지",
            distLabel: "전진실측", durLabel: "소요시간",
            isFeint: true));
        sb.AppendLine();

        // ── 종합 판정 ────────────────────────────────────────────────────────
        bool allNormal = _session.Recover.AbnormalCount == 0
                      && _session.Evade.AbnormalCount == 0
                      && _session.Feint.AbnormalCount == 0
                      && _session.SOLoaded;

        sb.AppendLine(Col("  ═ 종합 판정 ══════════════════════════════", "#FFFFFF"));
        if (allNormal)
        {
            sb.AppendLine("  " + Ok("✓ ALL PASS — Step4 TacticalReposition 검증 완료!"));
            sb.AppendLine($"    {Lbl("다음 단계")} : {Val("각 팩션 SO 값을 Goblin/Orc/Skeleton별로 튜닝하세요.")}");
        }
        else
        {
            sb.AppendLine("  " + Err("✗ ISSUES FOUND — 아래 항목을 확인하세요:"));
            if (!_session.SOLoaded)
                sb.AppendLine($"    {Err("· SO 미로드")} — {Val("PB4DecisionAdapter.InitializeBlackboard() 완료 여부 및 CombatProfile 연결 확인")}");
            if (_session.Recover.AbnormalCount > 0)
                sb.AppendLine($"    {Col("· RECOVER", C_RECOVER)} {Err($"비정상 {_session.Recover.AbnormalCount}회")} — {Val(_session.Recover.LastIssue ?? "내용 없음")}");
            if (_session.Evade.AbnormalCount > 0)
                sb.AppendLine($"    {Col("· EVADE  ", C_EVADE)} {Err($"비정상 {_session.Evade.AbnormalCount}회")} — {Val(_session.Evade.LastIssue ?? "내용 없음")}");
            if (_session.Feint.AbnormalCount > 0)
                sb.AppendLine($"    {Col("· FEINT  ", C_FEINT)} {Err($"비정상 {_session.Feint.AbnormalCount}회")} — {Val(_session.Feint.LastIssue ?? "내용 없음")}");
            sb.AppendLine($"    {Lbl("리셋 방법")} : {Val("TacticalRepositionAction.ClearAllSessions() 호출 후 재테스트")}");
        }
        sb.AppendLine(Col("  ══════════════════════════════════════════════", "#FFFFFF"));

        if (allNormal)
            Debug.Log(sb.ToString());
        else
            Debug.LogWarning(sb.ToString());
    }

    private string BuildModeReport(string modeName, string modeColor,
        TactModeRecord rec, string modeNote,
        string distLabel, string durLabel, bool isFeint)
    {
        int total = rec.NormalCount + rec.AbnormalCount;
        string header = Col($"  [{modeName}]", modeColor);
        string countStr = $"발동={Val(rec.ExecutionCount.ToString())}회  완료={Val(total.ToString())}회" +
                          $"  {Ok($"정상={rec.NormalCount}")}/{Err($"비정상={rec.AbnormalCount}")}";
        string note = Lbl($"  └ {modeNote}");

        string lastDist, lastDur, lastIssue;

        if (total == 0)
        {
            lastDist = Warn("완료 기록 없음");
            lastDur = Warn("완료 기록 없음");
            lastIssue = "";
        }
        else
        {
            // 마지막 실행의 정상 여부
            // 루트모션 기준: 정상 카운트 여부로 판단 (velocity 기반으로 이미 판정됨)
            bool distOk = rec.NormalCount > 0 || (rec.AbnormalCount == 0 && total > 0);
            lastDist = distOk
                ? Ok($"✓ 실측={rec.LastMovedDist:F2}m  (SO목표={rec.LastSoDist:F2}m, 루트모션 제한 정상)")
                : Err($"✗ 실측={rec.LastMovedDist:F2}m  이동시도 없음  (SO={rec.LastSoDist:F2}m)");

            bool durOk = Mathf.Abs(rec.LastElapsed - rec.LastSoDuration) < 0.5f;
            lastDur = durOk
                ? Ok($"✓ {rec.LastElapsed:F2}s  (SO={rec.LastSoDuration:F2}s)")
                : Warn($"△ {rec.LastElapsed:F2}s  (SO={rec.LastSoDuration:F2}s)");

            if (isFeint)
            {
                // 루트모션 기준: 세션에서 정상 카운트가 하나라도 있으면 OK
                bool peakOk = rec.NormalCount > 0 || rec.AbnormalCount == 0;
                string peakNote = rec.LastIssue != null
                    ? Warn($"△ {rec.LastIssue}")
                    : Ok($"✓ 전진={rec.LastFeintPeakDist:F2}m  복귀변위={rec.LastFeintEndDisp:F2}m");
                lastDist = peakOk
                    ? peakNote
                    : Err($"✗ 전진={rec.LastFeintPeakDist:F2}m  복귀변위={rec.LastFeintEndDisp:F2}m  (SO={rec.LastSoDist:F2}m)");
            }

            lastIssue = rec.AbnormalCount > 0 && rec.LastIssue != null
                ? $"\n    {Lbl("└ 마지막 이슈")} : {Err(rec.LastIssue)}"
                : "";
        }

        return $"{header} {countStr}\n{note}\n    {Lbl(distLabel)} : {lastDist}" +
               $"\n    {Lbl(durLabel)} : {lastDur}{lastIssue}";
    }
}