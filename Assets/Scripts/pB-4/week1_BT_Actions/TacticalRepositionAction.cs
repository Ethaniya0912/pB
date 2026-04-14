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
// =============================================================================

using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using TDA.Character.AI;
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

    // =========================================================================
    // 내부 상태
    // =========================================================================

    private enum RepositionMode { Recover, Evade, Feint }
    private RepositionMode _mode;

    private NavMeshAgent _nav;
    private AICharacterManager _aiMgr;
    private float _timer;
    private Vector3 _destination;

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
            if (maxHp > 0) healthRatio  = nm.currentHealth.Value  / maxHp;
        }

        bool needsRecover =
            (StaminaRecoverThreshold.Value > 0 && staminaRatio < StaminaRecoverThreshold.Value) ||
            (HealthRecoverThreshold.Value  > 0 && healthRatio  < HealthRecoverThreshold.Value);

        if (needsRecover)
        {
            _mode = RepositionMode.Recover;
            Debug.Log($"[TacticalRepos] {Self.Value.name}: Recover 모드 " +
                      $"(SP={staminaRatio:P0} HP={healthRatio:P0})");
        }
        else if (UnityEngine.Random.value < FeintChance.Value)
        {
            _mode = RepositionMode.Feint;
            Debug.Log($"[TacticalRepos] {Self.Value.name}: Feint 모드");
        }
        else if (UnityEngine.Random.value < EvasionChance.Value)
        {
            _mode = RepositionMode.Evade;
            Debug.Log($"[TacticalRepos] {Self.Value.name}: Evade 모드");
        }
        else
        {
            // 발동 조건 미충족 → 이 Action은 건너뜀
            return Status.Failure;
        }

        _destination = CalcDestination();
        _nav.isStopped = false;
        _nav.SetDestination(_destination);

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_nav == null) return Status.Failure;

        _timer += Time.deltaTime;

        float duration = _mode == RepositionMode.Recover  ? RecoverDuration.Value
                       : _mode == RepositionMode.Evade    ? EvasionDuration.Value
                                                           : FeintDuration.Value;

        if (_mode == RepositionMode.Feint)
        {
            // 페인트: 절반 시간 전진 → 절반 시간 후퇴
            if (_timer < duration * 0.5f)
            {
                // 전진 유지
            }
            else if (_timer < duration)
            {
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
                return Status.Success;
            }
        }
        else
        {
            // Recover / Evade: 목적지 도달 or 시간 만료
            bool arrived = !_nav.pathPending && _nav.remainingDistance < 0.5f;
            if (_timer >= duration || (arrived && _timer > 0.3f))
                return Status.Success;
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
            if (dir == 0)      stepDir = -Self.Value.transform.right;
            else if (dir == 1) stepDir =  Self.Value.transform.right;
            else               stepDir = -Self.Value.transform.forward;

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
}
