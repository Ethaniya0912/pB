// =============================================================================
// CircleStrafeAction.cs  |  pB-4 커스텀 BT Action — 타겟 주위 배회 (5 패턴 시스템)
// 패키지  : Unity Behavior 1.0.15 (com.unity.behavior)
//
// 역할 (★ N-23 옵션 C 단계 2 갱신):
//   타겟 주위에서 5 패턴 (CircleLeft / CircleRight / Backstep / Hold / Approach)
//   중 하나를 dwellTime (3~6초) 동안 유지한 후 weighted random 으로 재선출.
//   사용자 요구 ("좌측 / 우측 / 뒤쪽 신중하게") 정합.
//
//   strafeTimer가 StrikeTriggerTime에 도달 + dist ≤ AttackRange 시 Success(→Strike 전환).
//
// 5 패턴 정의:
//   CircleLeft   — 반시계 접선 이동 (_orbitDir = +1)
//   CircleRight  — 시계 접선 이동 (_orbitDir = -1)
//   Backstep     — target 반대 방향 후퇴 (BackstepDistance, 기본 4m)
//   Hold         — 정지 신중 대기 (NavMesh.isStopped=true, 적 응시 회전 유지)
//   Approach     — target 방향 단일 step 접근 (ApproachStep, 기본 2m)
//
// 확률 분포 (default):
//   CircleLeft 25% / CircleRight 25% / Backstep 20% / Hold 20% / Approach 10%
//   FactionCombatProfile SO 측 종족 / 성격 차별화는 G-1 후속 의제.
//
// 지형 변조:
//   NarrowPath → orbitRadius × 0.4, strikeTriggerTime × 0.5
//   (좁은 통로에서 배회 불가 → 빠른 공격 전환)
//
// 안전장치:
//   NavMesh.SamplePosition 실패 → 즉시 Success (Strike 강제 전환)
//   → 벽 끼임 방지 [위험 R1 대응]
//
// 이력:
//   [Bug 1 수정] StrikeTriggerTime 만료 → AttackRange 검사 추가
//   [Bug 2 수정] StrikeTriggerTime 측 랜덤 지터 (반복성 제거)
//   [N-23 / Hotfix3 / 옵션 C 단계 1] 진동 해소 ("앞뒤 까딱까딱"):
//     (1) target 측 근거리 fallback — world-X 축 → target 기준 접선 정정
//     (2) destPos hysteresis (0.5m 미만 변동은 SetDestination 호출 안 함)
//     (3) NavMeshAgent stoppingDistance 보장 (0.3m)
//   [N-23 / 옵션 C 단계 2] 5 패턴 시스템 본격 (좌 / 우 / 뒤 / 정지 / 접근):
//     - StrafePattern enum 5 종 신규
//     - 5 확률 BlackboardVariable (Inspector 노출 — SO 통합은 G-1 후속)
//     - 패턴 dwellTime (3~6초) — 신중 거동 본격
//     - 기존 _feintTimer / FeintInterval → dwellTimer 측 통합 (페인트 = 재선출 영역)
//   [N-23 / Hotfix4] 패턴 dwellTimer 측 BT 사이클 가로지름 정합:
//     - 문제: CircleStrafe 1 사이클 (1.6~3.2초) < dwellTime (3~6초) →
//             매 OnStart 측 신규 SelectPattern 호출 → 사이클마다 패턴 변경 →
//             "신중함" 영역 약화 + [CS] 패턴 전환 OnUpdate 로그 0 줄.
//     - 정정: OnEnd 측 _dwellTimer / _currentPattern 보존 (0 리셋 제거).
//             OnStart 측 _dwellTimer ≤ 0 일 때만 신규 패턴 선출.
//             진입 시점 로그 추가 (신규 / 지속 구분).
// =============================================================================
using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

// =============================================================================
// [N-23 / 옵션 C 단계 2] StrafePattern — 5 패턴 시스템
// =============================================================================
/// <summary>
/// 배회 패턴 5 종 — 사용자 요구 ("좌측 / 우측 / 뒤쪽 신중하게") 정합.
/// 매 dwellTime (3~6초) 만료 시 weighted random 측 신규 패턴 선출.
/// </summary>
public enum StrafePattern
{
    CircleLeft = 0,   // 반시계 접선 이동 (_orbitDir = +1)
    CircleRight = 1,   // 시계 접선 이동 (_orbitDir = -1)
    Backstep = 2,   // 후퇴 — target 반대 방향
    Hold = 3,   // 정지 — 신중 대기 (적 응시)
    Approach = 4,   // 접근 — target 직진 step
}

[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "CircleStrafe",
    story: "[Self] circles around [Target] with [OrbitRadius] radius",
    category: "pB-4/Attack",
    id: "pb4_circlestrafe_action")]
public partial class CircleStrafeAction : Action
{
    // =========================================================================
    // Blackboard 변수
    // =========================================================================

    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<Transform> Target;

    /// <summary>배회 반경 (m). 고블린=3, 오크=4, 스켈레톤=5.</summary>
    [Tooltip("타겟 중심 배회 반경. NarrowPath 시 ×0.4로 축소")]
    [SerializeReference] public BlackboardVariable<float> OrbitRadius = new(4f);

    /// <summary>배회 각속도 (°/s). 고블린=80, 오크=40, 스켈레톤=20.</summary>
    [Tooltip("초당 회전 각도. 클수록 빠르게 배회")]
    [SerializeReference] public BlackboardVariable<float> StrafeAngularSpeed = new(40f);

    /// <summary>배회→공격 전환 대기 시간 (초). 고블린=0.5, 오크=2.0, 스켈레톤=∞.</summary>
    [Tooltip("이 시간 경과 후 Strike로 전환. Infinity=포위 완성까지 대기")]
    [SerializeReference] public BlackboardVariable<float> StrikeTriggerTime = new(2f);

    /// <summary>
    /// 공격 사거리 (m). StrikeTriggerTime 만료 후 이 거리 이하일 때만 Strike 전환.
    /// PB4DecisionAdapter 가 combatProfile.attackRange 를 BB 에 push 합니다.
    ///
    /// [Bug 1 수정] StrikeTriggerTime 만료만으로는 Strike 로 전환하지 않습니다.
    /// dist <= AttackRange 가 동시에 성립해야 StrikeAction 이 실행됩니다.
    /// 성립하지 않으면 orbitRadius 를 점진적으로 줄여 접근을 계속합니다.
    /// </summary>
    [Tooltip("공격 유효 사거리(m). StrikeTriggerTime 만료 후 이 거리 이하여야 Strike 전환.\n" +
             "PB4DecisionAdapter → BB AttackRange → 이 변수로 연결하세요.")]
    [SerializeReference] public BlackboardVariable<float> AttackRange = new(2.5f);

    /// <summary>현재 지형 태그. NarrowPath 변조에 사용.</summary>
    [SerializeReference] public BlackboardVariable<string> TerrainTags;

    [SerializeReference] public BlackboardVariable<float> NarrowPathOrbitMult = new(0.4f);
    [SerializeReference] public BlackboardVariable<float> NarrowPathStrikeTimeMult = new(0.5f);

    // =========================================================================
    // 내부 상태
    // =========================================================================
    private NavMeshAgent _nav;
    private Transform _selfTransform;

    /// <summary>배회 각도 (0~360). 매 프레임 angularSpeed×dt만큼 증가.</summary>
    private float _periAngle;

    /// <summary>배회 경과 시간. triggerTime 도달 시 Strike 전환.</summary>
    private float _strafeTimer;

    /// <summary>
    /// [Bug 1 수정] StrikeTriggerTime 만료 후 AttackRange 미도달 시
    /// orbitRadius 를 줄이며 접근하는 시간. 이 값 초과 시 Strike 강제 전환.
    /// </summary>
    private float _closeInTimer;

    /// <summary>orbitRadius 축소 접근의 최대 허용 시간 (초).</summary>
    [SerializeReference] public BlackboardVariable<float> CloseInMaxTime = new(3f);

    // ─────────────────────────────────────────────────────────────────────────
    // [Hotfix3 / N-23 후속] 진동 해소 — destPos hysteresis
    //
    // 문제 영역:
    //   매 프레임 destPos 가 살짝 변동 (~10cm) → SetDestination 매 프레임 호출 →
    //   NavMeshAgent 가 path 계산을 매 프레임 재시작 → 도착 전 새 destPos 측 회전 →
    //   미세 진동 ("까딱까딱") 발생.
    //
    // 정합:
    //   직전 SetDestination 위치 캐시. 신규 destPos 가 직전 위치와 ≥ HYST 거리일 때만
    //   SetDestination 호출. 그 외는 NavMeshAgent 가 직전 destPos 측 자연 진행.
    //   결과: 매 프레임 미세 변동 흡수 + 본격 방향 변경만 반영.
    private Vector3 _lastSetDestPos;
    private bool _hasSetDestPos;
    private const float DEST_POS_HYSTERESIS = 0.5f;     // 0.5m 미만 변동은 무시
    private const float NAV_STOPPING_DISTANCE = 0.3f;   // 도착 직전 미세 진동 방지

    // ─────────────────────────────────────────────────────────────────────────
    // [N-23 / 옵션 C 단계 2] StrafePattern 내부 상태
    //
    // 거동:
    //   OnStart 측 _currentPattern 선출 + _dwellTimer 시작.
    //   매 OnUpdate 측 _dwellTimer 감소. 만료 시 신규 패턴 재선출.
    //   _currentPattern 따라 destPos 계산 (5 분기).
    private StrafePattern _currentPattern;
    private float _dwellTimer;
    private Vector3 _patternStartPos;   // BACKSTEP 시작 시 self 위치 캐시 (도착 영역 정합)

    /// <summary>
    /// 궤도 회전 방향. +1=반시계 (CircleLeft), -1=시계 (CircleRight).
    /// 패턴 진입 시 자동 설정 — CircleLeft → +1 / CircleRight → -1.
    /// </summary>
    private float _orbitDir = 1f;

    // ─────────────────────────────────────────────────────────────────────────
    // [N-23 / 옵션 C 단계 2] StrafePattern 5 패턴 시스템
    //
    // 사용자 요구 ("좌측 / 우측 / 뒤쪽 신중하게") 정합:
    //   매 dwellTime (3~6초) 만료 시 weighted random 으로 신규 패턴 선출.
    //   기존 _feintTimer (단순 좌↔우 반전) → 5 패턴 + dwellTime 시스템으로 교체.
    // ─────────────────────────────────────────────────────────────────────────

    [Tooltip("CircleLeft (반시계 접선) 패턴 선택 가중치. 기본 0.25.")]
    [SerializeReference] public BlackboardVariable<float> ProbCircleLeft = new(0.25f);

    [Tooltip("CircleRight (시계 접선) 패턴 선택 가중치. 기본 0.25.")]
    [SerializeReference] public BlackboardVariable<float> ProbCircleRight = new(0.25f);

    [Tooltip("Backstep (후퇴) 패턴 선택 가중치. 기본 0.20. ↑ 신중 / 방어형 캐릭터.")]
    [SerializeReference] public BlackboardVariable<float> ProbBackstep = new(0.20f);

    [Tooltip("Hold (정지 신중 대기) 패턴 선택 가중치. 기본 0.20. ↑ 신중 / 침착 캐릭터.")]
    [SerializeReference] public BlackboardVariable<float> ProbHold = new(0.20f);

    [Tooltip("Approach (접근 step) 패턴 선택 가중치. 기본 0.10. ↑ 공격적 캐릭터.")]
    [SerializeReference] public BlackboardVariable<float> ProbApproach = new(0.10f);

    [Tooltip("패턴 유지 최소 시간 (초). 기본 3.0 — 신중한 거동 영역.")]
    [SerializeReference] public BlackboardVariable<float> DwellTimeMin = new(3f);

    [Tooltip("패턴 유지 최대 시간 (초). 기본 6.0.")]
    [SerializeReference] public BlackboardVariable<float> DwellTimeMax = new(6f);

    [Tooltip("Backstep 패턴 측 후퇴 거리 (m). 기본 4.0.")]
    [SerializeReference] public BlackboardVariable<float> BackstepDistance = new(4f);

    [Tooltip("Approach 패턴 측 접근 step 거리 (m). 기본 2.0.")]
    [SerializeReference] public BlackboardVariable<float> ApproachStep = new(2f);

    /// <summary>
    /// [Bug 2 수정] OnStart에서 계산된 실제 Strike 전환 대기 시간.
    /// StrikeTriggerTime ± 랜덤 지터를 적용해 반복성을 제거합니다.
    /// </summary>
    private float _actualTriggerTime;

    /// <summary>가드 반응: 직전 틱의 타겟 가드 상태 캐시.</summary>
    private bool _wasTargetGuarding;

    /// <summary>가드 반응: 타겟의 CharacterManager 캐시 (OnStart 1회 획득).</summary>
    private CharacterManager _targetCharMgr;

    /// <summary>가드 해제 후 공격까지의 짧은 반응 딜레이 (초).</summary>
    [SerializeReference] public BlackboardVariable<float> GuardBreakReactionDelay = new(0.25f);

    /// <summary>
    /// 가드 중 포이즈 공격 타이머.
    /// 타겟이 가드를 유지하는 시간을 누적합니다.
    /// </summary>
    private float _guardAttackTimer;

    /// <summary>
    /// 다음 포이즈 공격까지 대기할 시간 (초).
    /// OnStart와 포이즈 공격 직후 Random.Range로 재계산됩니다.
    /// </summary>
    private float _nextPoiseAttackInterval;

    /// <summary>포이즈 공격 최소 대기 (초).</summary>
    [SerializeReference] public BlackboardVariable<float> PoiseAttackIntervalMin = new(2.5f);

    /// <summary>포이즈 공격 최대 대기 (초).</summary>
    [SerializeReference] public BlackboardVariable<float> PoiseAttackIntervalMax = new(5.5f);

    // =========================================================================
    // 생명주기
    // =========================================================================

    protected override Status OnStart()
    {
        if (Self.Value == null || Target.Value == null)
        {
            LogFailure("CircleStrafeAction: Self 또는 Target이 null입니다.");
            return Status.Failure;
        }

        _nav = Self.Value.GetComponent<NavMeshAgent>();
        _selfTransform = Self.Value.transform;

        if (_nav == null || !_nav.isOnNavMesh)
        {
            LogFailure("CircleStrafeAction: NavMeshAgent가 없거나 NavMesh 밖입니다.");
            return Status.Failure;
        }

        // 시작 시 현재 각도를 타겟→자신 방향에서 계산 (연속적 궤도)
        Vector3 toSelf = _selfTransform.position - Target.Value.position;
        toSelf.y = 0f;
        _periAngle = Mathf.Atan2(toSelf.z, toSelf.x) * Mathf.Rad2Deg;
        _strafeTimer = 0f;
        _closeInTimer = 0f;  // [Bug 1 수정] 접근 타이머 초기화

        // 가드 반응: 타겟 CharacterManager 캐시
        _targetCharMgr = Target.Value != null
            ? Target.Value.GetComponent<CharacterManager>()
            : null;
        _wasTargetGuarding = false;
        _guardAttackTimer = 0f;
        _nextPoiseAttackInterval = UnityEngine.Random.Range(PoiseAttackIntervalMin.Value, PoiseAttackIntervalMax.Value);

        // [Bug 2 수정] Strike 전환 타이밍에 랜덤 지터 적용
        // StrikeTriggerTime(2초) ± jitter → 패턴 예측 불가
        // jitter 범위: -0.4초 ~ +1.2초 (하한은 최솟값 0.5초 보장)
        float jitter = UnityEngine.Random.Range(-0.4f, 1.2f);
        _actualTriggerTime = Mathf.Max(0.5f, StrikeTriggerTime.Value + jitter);

        // [N-23 / 옵션 C 단계 2 / Hotfix4] 패턴 진입 — dwellTimer 보존 정합
        //
        // 영역 정합:
        //   CircleStrafe 1 사이클 (≈ 1.6~3.2초) < dwellTime (3~6초) →
        //   매 OnStart 측 SelectPattern() 호출 시 패턴 잦은 변경 → "신중함" 약화.
        //
        // 해결:
        //   _dwellTimer 측 BT 사이클 가로질러 보존. 만료 (≤ 0) 일 때만 신규 패턴 선출.
        //   _dwellTimer > 0 = 직전 사이클 측 패턴 / 자료 지속 진입.
        bool isNewPattern = _dwellTimer <= 0f;
        if (isNewPattern)
        {
            _currentPattern = SelectPattern();
            _dwellTimer = UnityEngine.Random.Range(DwellTimeMin.Value, DwellTimeMax.Value);
        }
        ApplyPatternEntry(_currentPattern);

#if UNITY_EDITOR
        if (isNewPattern)
        {
            Debug.Log($"[CS] {Self.Value.name}: 패턴 진입 (신규) → {_currentPattern}, " +
                      $"dwellTimer={_dwellTimer:F2}s");
        }
        else
        {
            Debug.Log($"[CS] {Self.Value.name}: 패턴 진입 (지속) → {_currentPattern}, " +
                      $"dwellTimer={_dwellTimer:F2}s 남음");
        }
#endif

        // [Hotfix3 / N-23 후속] 진동 해소 — destPos hysteresis + stopping distance
        _lastSetDestPos = _selfTransform.position;
        _hasSetDestPos = false;
        // stoppingDistance 보장 — 0 (default) 일 때 도착 직전 미세 진동 발생
        // OrbitRadius / AttackRange 와 무관 — 도착 위치 자체의 진동 차단 자료
        if (_nav.stoppingDistance < NAV_STOPPING_DISTANCE)
            _nav.stoppingDistance = NAV_STOPPING_DISTANCE;

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_nav == null || !_nav.isOnNavMesh || Target.Value == null)
            return Status.Failure;

        Vector3 targetPos = Target.Value.position;
        float dist = Vector3.Distance(_selfTransform.position, targetPos);
        float attackRange = AttackRange.Value > 0f ? AttackRange.Value : 2.5f;

        // ── 지형 변조 ──────────────────────────────────────────────────────────
        float radius = OrbitRadius.Value;
        float triggerTime = _actualTriggerTime;   // 랜덤 지터 적용된 값
        string tags = TerrainTags.Value ?? "";
        if (tags.Contains("NarrowPath"))
        {
            radius *= NarrowPathOrbitMult.Value;
            triggerTime *= NarrowPathStrikeTimeMult.Value;
        }

        // ── 타이머 누적 ────────────────────────────────────────────────────────
        _strafeTimer += Time.deltaTime;

        // ── 가드 반응 ────────────────────────────────────────────────────────────
        bool isTargetGuarding = _targetCharMgr != null
            && _targetCharMgr.characterDefenseManager != null
            && _targetCharMgr.characterDefenseManager.isDefending;

        if (isTargetGuarding)
        {
            _guardAttackTimer += Time.deltaTime;
            if (_guardAttackTimer >= _nextPoiseAttackInterval)
            {
                _actualTriggerTime = _strafeTimer + GuardBreakReactionDelay.Value;
                _guardAttackTimer = 0f;
                _nextPoiseAttackInterval = UnityEngine.Random.Range(PoiseAttackIntervalMin.Value, PoiseAttackIntervalMax.Value);
                Debug.Log($"[CircleStrafe] {Self.Value.name}: 포이즈 공격 시도. " +
                          $"다음 포이즈 간격={_nextPoiseAttackInterval:F1}s");
            }
            else
            {
                _actualTriggerTime = _strafeTimer + 1.0f;
            }
        }
        else if (_wasTargetGuarding)
        {
            _actualTriggerTime = _strafeTimer + GuardBreakReactionDelay.Value;
            _guardAttackTimer = 0f;
            _nextPoiseAttackInterval = UnityEngine.Random.Range(PoiseAttackIntervalMin.Value, PoiseAttackIntervalMax.Value);
            Debug.Log($"[CircleStrafe] {Self.Value.name}: 가드 해제 → {GuardBreakReactionDelay.Value}s 후 Strike.");
        }
        _wasTargetGuarding = isTargetGuarding;
        // triggerTime 재동기화 (가드 반응이 _actualTriggerTime을 변경했을 수 있음)
        triggerTime = _actualTriggerTime;

        // ── Strike 전환 판정 + closeIn radius 계산 ──────────────────────────────
        // [핵심 수정] radius 축소를 SetDestination 호출 전에 계산합니다.
        // 기존 코드는 SetDestination 후에 radius를 수정하여 실제 이동에 반영되지 않았음.
        bool inCloseIn = false;
        if (_strafeTimer >= triggerTime)
        {
            if (dist <= attackRange)
                return Status.Success;  // ✅ AttackRange 도달 → Strike

            // AttackRange 밖 → closeIn 진입
            inCloseIn = true;
            _closeInTimer += Time.deltaTime;

            if (_closeInTimer >= CloseInMaxTime.Value)
            {
                // closeIn 최대 시간 초과: dist를 재확인하고 그래도 멀면 Failure
                // (Failure → BT Sequence 재시작 → Stalk→CircleStrafe 재진입)
                // 기존: 무조건 Success → StrikeAction에서 멍때림
                Debug.LogWarning($"[BT] {Self.Value.name}: closeIn {CloseInMaxTime.Value}s 초과 " +
                                 $"(dist={dist:F1}m). CircleStrafe 재시작.");
                return Status.Failure;
            }

            // closeIn: orbitRadius를 0으로 수렴시켜 타겟 방향으로 직접 접근.
            // periAngle 회전을 멈춰 사선 이동을 최소화하고 직진 접근을 극대화.
            radius *= Mathf.Clamp01(1f - _closeInTimer / CloseInMaxTime.Value);
        }

        // ── [N-23 / 옵션 C 단계 2] 패턴 dwellTimer + 재선출 ───────────────────
        // 매 dwellTime (3~6초) 만료 시 weighted random 측 신규 패턴 선출.
        // closeIn 모드 진입 시는 패턴 재선출 안 함 (직진 Strike 진입 영역 우선).
        if (!inCloseIn)
        {
            _dwellTimer -= Time.deltaTime;
            if (_dwellTimer <= 0f)
            {
                StrafePattern previousPattern = _currentPattern;
                _currentPattern = SelectPattern();
                _dwellTimer = UnityEngine.Random.Range(DwellTimeMin.Value, DwellTimeMax.Value);
                ApplyPatternEntry(_currentPattern);
#if UNITY_EDITOR
                Debug.Log($"[CS] {Self.Value.name}: 패턴 전환 {previousPattern} → {_currentPattern}, " +
                          $"dwellTimer={_dwellTimer:F2}s");
#endif
            }
        }

        // ── [N-23 / 옵션 C 단계 2] 패턴별 destPos 계산 ───────────────────────
        // 5 패턴 분기 — CircleLeft / CircleRight / Backstep / Hold / Approach.
        // closeIn 진입 시는 CircleLeft / CircleRight 측 본격 접근 영역으로 강제 fallback.
        Vector3 selfPos = _selfTransform.position;
        Vector3 toTarget = (targetPos - selfPos);
        toTarget.y = 0f;
        float distFlat = toTarget.magnitude;

        StrafePattern activePattern = inCloseIn
            ? (_orbitDir > 0f ? StrafePattern.CircleLeft : StrafePattern.CircleRight)  // closeIn → 접근 본격
            : _currentPattern;

        Vector3 destPos = ComputeDestPosForPattern(
            activePattern, selfPos, toTarget, distFlat, radius, inCloseIn);

        // periAngle 측 시각적 연속성 — Circle 패턴 진행 시만 갱신
        if (activePattern == StrafePattern.CircleLeft || activePattern == StrafePattern.CircleRight)
        {
            _periAngle += StrafeAngularSpeed.Value * _orbitDir * Time.deltaTime;
        }

        // Hold 패턴 측 — SetDestination 호출 자체 안 함 (isStopped=true 측 정합)
        if (activePattern == StrafePattern.Hold)
        {
            // 적 응시 회전만 유지 (line 측 본격 회전 영역 측에서 처리)
            // destPos 미적용 — return 안 함, 회전 영역 계속 진행
        }
        else if (NavMesh.SamplePosition(destPos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            // [Hotfix3 / N-23 후속] 진동 해소 — destPos hysteresis
            //   신규 destPos 가 직전 SetDestination 위치 측 0.5m 이상 차이일 때만 갱신.
            //   매 프레임 미세 변동은 NavMeshAgent 가 직전 destPos 측 자연 도착 진행.
            float destDelta = _hasSetDestPos
                ? Vector3.Distance(hit.position, _lastSetDestPos)
                : float.MaxValue;

            if (destDelta >= DEST_POS_HYSTERESIS)
            {
                _nav.SetDestination(hit.position);
                _lastSetDestPos = hit.position;
                _hasSetDestPos = true;
            }
            // else: 직전 destPos 유지 — NavMeshAgent 자연 진행
        }
        else
        {
            // NavMesh 밖이면 타겟 방향 직접 접근
            _nav.SetDestination(targetPos);
            _lastSetDestPos = targetPos;
            _hasSetDestPos = true;
        }

        // ── 타겟 방향 회전 ──────────────────────────────────────────────────────
        Vector3 lookDir = targetPos - _selfTransform.position;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            _selfTransform.rotation = Quaternion.Slerp(
                _selfTransform.rotation, targetRot, Time.deltaTime * 5f);
        }

        return Status.Running;
    }

    protected override void OnEnd()
    {
        _strafeTimer = 0f;
        _closeInTimer = 0f;  // [Bug 1 수정] 접근 타이머도 초기화
        _actualTriggerTime = 0f;
        _wasTargetGuarding = false;
        _targetCharMgr = null;
        _guardAttackTimer = 0f;
        _hasSetDestPos = false;   // [Hotfix3] hysteresis 상태 정리

        // [Hotfix4] _dwellTimer / _currentPattern 측 보존 — CircleStrafe 사이클 가로질러
        //   BT 사이클 (Strike 진입 → Stalk → CircleStrafe 재진입) 사이 dwellTime 지속.
        //   다음 OnStart 측 _dwellTimer > 0 이면 패턴 지속 진입, ≤ 0 이면 신규 선출.
        //   결과: "신중함" 영역 본격 (3~6초 동안 한 패턴 유지, BT 사이클 가로질러).
        // _dwellTimer 측 보존 (0 으로 리셋 안 함)
        // _currentPattern 측 보존

        // periAngle 측 시각 연속성 유지 (다음 CircleStrafe 진입 시 궤도 자연 정합)

        // Hold 패턴 진입 시 isStopped=true 였을 수 있음 → 정상 영역 복귀
        if (_nav != null && _nav.isOnNavMesh)
            _nav.isStopped = false;
    }

    // =========================================================================
    // [N-23 / 옵션 C 단계 2] StrafePattern 시스템 — 신규 메서드 4 종
    // =========================================================================

    /// <summary>
    /// 5 패턴 측 weighted random 선출.
    /// 가중치 모두 0 일 경우 CircleLeft fallback.
    /// </summary>
    private StrafePattern SelectPattern()
    {
        float wL = Mathf.Max(0f, ProbCircleLeft.Value);
        float wR = Mathf.Max(0f, ProbCircleRight.Value);
        float wB = Mathf.Max(0f, ProbBackstep.Value);
        float wH = Mathf.Max(0f, ProbHold.Value);
        float wA = Mathf.Max(0f, ProbApproach.Value);
        float total = wL + wR + wB + wH + wA;

        if (total <= 0f) return StrafePattern.CircleLeft;  // fallback

        float roll = UnityEngine.Random.value * total;
        if (roll < wL) return StrafePattern.CircleLeft;
        roll -= wL;
        if (roll < wR) return StrafePattern.CircleRight;
        roll -= wR;
        if (roll < wB) return StrafePattern.Backstep;
        roll -= wB;
        if (roll < wH) return StrafePattern.Hold;
        return StrafePattern.Approach;
    }

    /// <summary>
    /// 패턴 진입 시점 본격 처리 — _orbitDir 설정 + NavMesh 측 정지 / 정합 자료.
    /// </summary>
    private void ApplyPatternEntry(StrafePattern pattern)
    {
        switch (pattern)
        {
            case StrafePattern.CircleLeft:
                _orbitDir = +1f;
                if (_nav != null && _nav.isOnNavMesh) _nav.isStopped = false;
                break;

            case StrafePattern.CircleRight:
                _orbitDir = -1f;
                if (_nav != null && _nav.isOnNavMesh) _nav.isStopped = false;
                break;

            case StrafePattern.Backstep:
            case StrafePattern.Approach:
                if (_nav != null && _nav.isOnNavMesh) _nav.isStopped = false;
                _patternStartPos = _selfTransform != null ? _selfTransform.position : Vector3.zero;
                _hasSetDestPos = false;   // 신규 destPos 즉시 적용
                break;

            case StrafePattern.Hold:
                // 정지 — 신중 대기 영역
                if (_nav != null && _nav.isOnNavMesh)
                {
                    _nav.isStopped = true;
                    _nav.ResetPath();
                }
                _hasSetDestPos = false;
                break;
        }
    }

    /// <summary>
    /// 5 패턴 측 destPos 계산 본문. 패턴별 단일 분기.
    /// </summary>
    private Vector3 ComputeDestPosForPattern(
        StrafePattern pattern, Vector3 selfPos, Vector3 toTarget,
        float distFlat, float radius, bool inCloseIn)
    {
        switch (pattern)
        {
            case StrafePattern.CircleLeft:
            case StrafePattern.CircleRight:
                return ComputeCircleDestPos(selfPos, toTarget, distFlat, radius, inCloseIn);

            case StrafePattern.Backstep:
                return ComputeBackstepDestPos(selfPos, toTarget, distFlat);

            case StrafePattern.Hold:
                return selfPos;  // 현재 위치 — Hold 측 SetDestination 호출 안 함 (OnUpdate 측 분기)

            case StrafePattern.Approach:
                return ComputeApproachDestPos(selfPos, toTarget, distFlat);

            default:
                return selfPos;
        }
    }

    /// <summary>Circle 패턴 측 접선+방사 블렌드 계산 (기존 Hotfix3 정합).</summary>
    private Vector3 ComputeCircleDestPos(
        Vector3 selfPos, Vector3 toTarget, float distFlat, float radius, bool inCloseIn)
    {
        if (distFlat > 0.1f)
        {
            Vector3 toTargetNorm = toTarget / distFlat;

            // 방사 성분: 타겟 방향
            Vector3 radial = toTargetNorm;

            // 접선 성분: 타겟→자신 방향의 90° 회전 (_orbitDir=+1 반시계, -1 시계)
            Vector3 tangent = new Vector3(
                -toTargetNorm.z * _orbitDir,
                0f,
                 toTargetNorm.x * _orbitDir);

            // 블렌드 비율: orbit_r/dist
            float orbitRatio = Mathf.Clamp01(radius / distFlat);
            Vector3 blendDir = (tangent * orbitRatio + radial * (1f - orbitRatio)).normalized;

            if (inCloseIn)
            {
                float closeT = Mathf.Clamp01(_closeInTimer / CloseInMaxTime.Value);
                blendDir = Vector3.Lerp(blendDir, toTargetNorm, closeT * 0.6f).normalized;
            }

            float lookAhead = Mathf.Max(radius, distFlat - radius + 1f);
            return selfPos + blendDir * lookAhead;
        }

        // 타겟에 너무 가까움 — fallback (Hotfix3 정합)
        Vector3 toTargetNormClose = (-_selfTransform.forward);  // self 정면 반대 (안전 fallback)
        Vector3 tangent90 = new Vector3(
            -toTargetNormClose.z * _orbitDir,
            0f,
             toTargetNormClose.x * _orbitDir);
        return selfPos + tangent90 * radius;
    }

    /// <summary>Backstep 패턴 — target 반대 방향 후퇴.</summary>
    private Vector3 ComputeBackstepDestPos(Vector3 selfPos, Vector3 toTarget, float distFlat)
    {
        if (distFlat <= 0.1f)
        {
            // 타겟이 자신과 같은 위치 — self.forward 반대 방향
            return selfPos - _selfTransform.forward * BackstepDistance.Value;
        }
        Vector3 awayFromTarget = -(toTarget / distFlat);
        return selfPos + awayFromTarget * BackstepDistance.Value;
    }

    /// <summary>Approach 패턴 — target 방향 단일 step 접근.</summary>
    private Vector3 ComputeApproachDestPos(Vector3 selfPos, Vector3 toTarget, float distFlat)
    {
        if (distFlat <= 0.1f) return selfPos;
        Vector3 towardsTarget = (toTarget / distFlat);
        return selfPos + towardsTarget * ApproachStep.Value;
    }
}