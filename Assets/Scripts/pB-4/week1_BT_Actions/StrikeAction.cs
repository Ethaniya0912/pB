// =============================================================================
// StrikeAction.cs  |  pB-4 커스텀 BT Action — 공격 실행
// 패키지  : Unity Behavior 1.0.15 (com.unity.behavior)
// 카테고리: pB-4/Attack
//
// 설계 문서 A3 (작업 목록 보고서) 구현:
//   "[Self] strikes at [Target]"
//   ResetPath→정지 → 공격 액션 실행 → Poise 연동
//   Success(완료→Sequence 반복) / Running(모션 진행중)
//
// 역할:
//   이동을 정지하고 공격 모션을 실행합니다.
//   AttackState SO 의 attackActions 목록에서 거리/각도 조건에 맞는
//   공격을 선택하여 PlayTargetActionFunnel() 로 애니메이션을 재생합니다.
//   애니메이션 완료(isPerformingAction=false) 시 Success 를 반환하여
//   Sequence 가 StalkAction 부터 다시 시작합니다.
//
// [버그 수정] TODO 스텁 완성
//   기존: PlayTargetActionFunnel() 호출 전체가 // TODO 주석 처리.
//         Debug.Log 만 출력하고 애니메이션 미재생.
//   수정: AICharacterManager.characterAnimationManager.PlayTargetActionFunnel()
//         호출로 기존 FSM 공격 파이프라인을 그대로 활용합니다.
//
// [연동 구조]
//   PlayTargetActionFunnel(attackActionID, isPerformingAction=true, canMove=false)
//     → Animator: ActionState = attackActionID, onAction trigger
//     → 공격 애니메이션 재생
//     → 애니메이션 종료 → Idle/Locomotion 상태로 전환
//     → ResetCharacterStateBehaviour.OnStateEnter()
//         → isPerformingAction = false, canMove = true, ActionState = 0
//   StrikeAction.OnUpdate():
//     → isPerformingAction == false 감지 → Status.Success 반환
//
// [AttackState 접근 방법 — 우선순위]
//   1. BB 변수 "AttackStateConfig" (PB4DecisionAdapter 가 Awake 에서 자동 설정)
//      BT 에디터에서 AttackConfig 필드를 "AttackStateConfig" BB 변수에 연결하세요.
//   2. BlackboardVariable<AttackState> AttackConfig 에 직접 SO 설정 (수동)
//   3. PB4DecisionAdapter.GetAttackState() 런타임 탐색 (1,2 모두 null 일 때 폴백)
//
// [PB4DecisionAdapter [Fix-E] 연동]
//   UpdateBlackboard() 에서 isPerformingAction = false 강제 리셋이 제거됨.
//   isPerformingAction 은 이 Action 과 ResetCharacterStateBehaviour 가 단독 관리.
//
// 배치: Assets/Scripts/pB-4/week1_BT_Actions/StrikeAction.cs
// =============================================================================
using System;
using System.Collections.Generic;
using TDA.Character.AI;   // AICharacterManager, AttackState, AIAttackAction
using TDA.Core.Events;
using TDA.PB4.Bridge;     // PB4DecisionAdapter
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Strike",
    story: "[Self] strikes at [Target]",
    category: "pB-4/Attack",
    id: "pb4_strike_action")]
public partial class StrikeAction : Action
{
    // =========================================================================
    // Blackboard 변수
    // =========================================================================

    /// <summary>이 AI 의 GameObject. Blackboard 의 Self 변수와 연결.</summary>
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    /// <summary>공격 대상 Transform. Blackboard 의 Target 변수와 연결.</summary>
    [SerializeReference] public BlackboardVariable<Transform> Target;

    /// <summary>
    /// 공격 액션 목록이 정의된 AttackState ScriptableObject.
    ///
    /// [설정 방법 — 우선순위]
    ///   1. PB4DecisionAdapter 가 Awake 에서 "AttackStateConfig" BB 변수를 자동 설정합니다.
    ///      BT 에디터에서 이 필드를 "AttackStateConfig" BB 변수에 연결하세요. (권장)
    ///   2. 수동: BT 에디터에서 이 필드에 해당 AI 의 AttackState SO 를 직접 설정.
    ///   3. 둘 다 null 이면 PB4DecisionAdapter.GetAttackState() 런타임 탐색으로 폴백.
    ///
    /// Blackboard 에 "AttackStateConfig" (Object 타입) 변수를 추가하고
    /// 이 필드를 그 변수에 연결하면 설정 완료입니다.
    /// attackActions[i].attackActionID 가 Animator ActionState 파라미터 값과
    /// 일치해야 공격 애니메이션이 재생됩니다.
    /// </summary>
    [Tooltip("AttackState SO. 'AttackStateConfig' BB 변수에 연결하거나 직접 SO 를 설정.")]
    [SerializeReference] public BlackboardVariable<AttackState> AttackConfig;

    /// <summary>
    /// 공격 전 타겟 방향으로 Slerp 회전하는 속도.
    /// 0 이면 회전 없음. 권장: 5~10.
    /// </summary>
    [Tooltip("공격 전 타겟 방향 Slerp 회전 속도. 0=회전 없음. 권장 5~10.")]
    [SerializeReference] public BlackboardVariable<float> AttackRotationSpeed = new(8f);

    // =========================================================================
    // 런타임 내부 상태
    // =========================================================================

    private NavMeshAgent _nav;
    private AICharacterManager _aiManager;

    /// <summary>공격 트리거가 이번 실행에서 이미 발동되었는지.</summary>
    private bool _attackTriggered;

    // ── 폴백 타임아웃 ──
    // AttackState 없거나 ActionID 불일치로 애니메이션이 재생되지 않을 때
    // isPerformingAction=true 가 영구 유지되는 교착 상태를 방지합니다.
    private float _fallbackTimer = 0f;

    /// <summary>
    /// 이 값(초) 초과 시 공격 모션 완료를 기다리지 않고 강제 Success 반환.
    /// 대부분의 공격 모션은 1.5초 이내이므로 3초는 충분한 여유입니다.
    /// [진단됨] attackActionID ≠ Animator ActionState 조건 → 애니메이션 미재생
    ///          → ResetCharacterStateBehaviour 미동작 → isPerformingAction 영구 true
    ///          → 이 타임아웃이 없으면 스켈레톤이 플레이어 앞에서 영구 멍때림.
    /// </summary>
    private const float ATTACK_TIMEOUT = 3.0f;

    // =========================================================================
    // 생명주기
    // =========================================================================

    protected override Status OnStart()
    {
        if (Self.Value == null)
        {
            LogFailure("StrikeAction: Self 가 null 입니다.");
            return Status.Failure;
        }

        _aiManager = Self.Value.GetComponent<AICharacterManager>();
        if (_aiManager == null)
        {
            LogFailure($"StrikeAction: {Self.Value.name} 에서 AICharacterManager 를 찾을 수 없습니다.");
            return Status.Failure;
        }

        _nav = Self.Value.GetComponent<NavMeshAgent>();
        _attackTriggered = false;
        _fallbackTimer = 0f;   // 타임아웃 타이머 초기화

        // ── 이동 완전 정지 (설계 문서 A3: ResetPath→정지) ──
        if (_nav != null && _nav.isActiveAndEnabled && _nav.isOnNavMesh)
        {
            _nav.ResetPath();
            _nav.velocity = Vector3.zero;
        }

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (_aiManager == null) return Status.Failure;

        // ── 공격 트리거 1회 ──
        if (!_attackTriggered)
        {
            // 공격 전 타겟 방향으로 Slerp 회전
            if (Target.Value != null && AttackRotationSpeed.Value > 0f)
                RotateTowardsTarget();

            // AttackState 취득 및 PlayTargetActionFunnel 호출
            bool executed = TryExecuteAttack();

            if (!executed)
            {
                // ── 폴백: 1.5초 더미 대기 후 Success ──
                Debug.LogWarning(
                    $"[StrikeAction] {Self.Value.name}: AttackState 를 찾을 수 없습니다.\n" +
                    "해결 방법:\n" +
                    "  1. PB4DecisionAdapter Inspector → PursueState → CombatStanceState → AttackState 필드 연결 확인\n" +
                    "  2. BT 에디터에서 Blackboard 에 'AttackStateConfig'(Object 타입) 변수 추가\n" +
                    "     → AttackConfig 필드를 해당 변수에 연결\n" +
                    "  3. 또는 AttackConfig 에 AttackState SO 를 직접 설정\n" +
                    "현재: 1.5초 더미 대기 후 Success 반환(애니메이션 없음).");
                _aiManager.isPerformingAction = true;
                _aiManager.canMove = false;
            }

            _attackTriggered = true;
        }

        // ── 공격 모션 완료 대기 ──
        // ResetCharacterStateBehaviour 가 Idle/Locomotion 진입 시
        // isPerformingAction = false, canMove = true, ActionState = 0 을 자동 설정합니다.
        // [Fix-E] PB4DecisionAdapter 가 더 이상 isPerformingAction 을 강제 리셋하지 않음.
        if (!_aiManager.isPerformingAction)
        {
            // 공격 완료 → Success → Sequence 가 StalkAction 부터 재시작 (설계 문서 A3)
            return Status.Success;
        }

        // ── 타임아웃 안전망 ──
        // [진단됨] attackActionID ≠ Animator ActionState 조건값 이면:
        //   PlayTargetActionFunnel() 은 호출됐지만 애니메이션 전환이 발생하지 않음
        //   → ResetCharacterStateBehaviour 가 실행되지 않음
        //   → isPerformingAction 이 영구 true → 플레이어 앞에서 멍때림
        // ATTACK_TIMEOUT(3초) 이내에 isPerformingAction 이 false 가 되지 않으면
        // 강제로 플래그를 복원하고 Success 를 반환합니다.
        _fallbackTimer += Time.deltaTime;
        if (_fallbackTimer >= ATTACK_TIMEOUT)
        {
            int _curAS = _aiManager.animator != null
                ? _aiManager.animator.GetInteger(AnimatorParameterHash.ActionState) : -1;
            string _curClip = _aiManager.animator != null
                ? _aiManager.animator.GetCurrentAnimatorStateInfo(1).shortNameHash.ToString()
                : "null";

            Debug.LogWarning(
                "[StrikeAction] " + Self.Value.name + ": " + ATTACK_TIMEOUT + "s 타임아웃! 공격 모션 미완료.\n" +
                "원인 가능성:\n" +
                "  1. attackActionID 와 Animator ActionState 전환 조건값 불일치\n" +
                "     AttackState SO attackActionID 를 Animator 조건 ActionState 값으로 맞추세요\n" +
                "  2. ResetCharacterStateBehaviour 가 Idle/Locomotion 상태에 미부착\n" +
                "  3. attackActions 거리/각도 조건이 실제 거리와 불일치\n" +
                "  현재 Animator ActionState=" + _curAS + "  Layer1 hash=" + _curClip);

            // 강제 복원
            _aiManager.isPerformingAction = false;
            _aiManager.canMove = true;
            _aiManager.canRotate = true;
            _aiManager.isPoiseActive = false;
            _aiManager.animator?.SetInteger(Animator.StringToHash("ActionState"), 0);

            return Status.Success;
        }

        return Status.Running;
    }

    protected override void OnEnd()
    {
        _attackTriggered = false;
        _fallbackTimer = 0f;

        // ── 비정상 종료 안전망 (Observer Abort 등으로 즉각 중단될 때) ──
        // ResetCharacterStateBehaviour 가 보통 처리하지만,
        // Abort 로 강제 종료될 경우 수동으로 플래그를 복원합니다.
        if (_aiManager != null && _aiManager.isPerformingAction)
        {
            _aiManager.isPerformingAction = false;
            _aiManager.canMove = true;
            _aiManager.canRotate = true;

            // ActionState = 0 → Animator 가 Idle/Locomotion 으로 복귀
            _aiManager.animator?.SetInteger(
                Animator.StringToHash("ActionState"), 0);

            // Poise 비활성화
            _aiManager.isPoiseActive = false;
        }
    }

    // =========================================================================
    // 공격 실행
    // =========================================================================

    /// <summary>
    /// AttackState SO 의 attackActions 에서 유효한 공격을 선택하고
    /// characterAnimationManager.PlayTargetActionFunnel() 로 공격 애니메이션을 실행합니다.
    ///
    /// 기존 FSM AttackState.Tick() 의 공격 실행 로직과 동일한 파이프라인 사용.
    /// </summary>
    /// <returns>공격 애니메이션이 정상 실행되었으면 true.</returns>
    private bool TryExecuteAttack()
    {
        // ── AttackState 취득 ──
        AttackState attackState = ResolveAttackState();
        if (attackState == null || attackState.attackActions == null || attackState.attackActions.Count == 0)
            return false;

        // ── 거리/각도 조건에 맞는 공격 목록 추출 ──
        float dist = Target.Value != null
            ? Vector3.Distance(Self.Value.transform.position, Target.Value.position)
            : float.MaxValue;

        float angle = Target.Value != null
            ? Vector3.Angle(
                Self.Value.transform.forward,
                (Target.Value.position - Self.Value.transform.position).normalized)
            : 0f;

        var validAttacks = new List<AIAttackAction>();
        foreach (var attack in attackState.attackActions)
        {
            bool distOk = dist >= attack.minimumAttackDistance
                        && dist <= attack.maximumAttackDistance;
            bool angleOk = angle >= attack.minimumAttackAngle
                        && angle <= attack.maximumAttackAngle;
            if (distOk && angleOk)
                validAttacks.Add(attack);
        }

        // ── 유효 공격 없을 때 상세 경고 ──
        // [진단됨] ConditionalGuard 가 EngageRange 이내를 보장하지만
        // AttackAction 의 minimumAttackDistance/maximumAttackDistance 와 실제 거리가
        // 다를 수 있습니다 (예: 고블린 공격 범위가 스켈레톤에 그대로 적용된 경우).
        if (validAttacks.Count == 0)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[StrikeAction] {Self.Value.name}: 현재 거리/각도에 유효한 공격이 없습니다.");
            sb.AppendLine($"  현재 dist={dist:F1}m  angle={angle:F0}°");
            sb.AppendLine($"  AttackState '{attackState.name}' 의 attackActions 목록:");
            foreach (var a in attackState.attackActions)
                sb.AppendLine($"    ActionID={a.attackActionID}  " +
                              $"dist=[{a.minimumAttackDistance:F1}~{a.maximumAttackDistance:F1}m]  " +
                              $"angle=[{a.minimumAttackAngle:F0}~{a.maximumAttackAngle:F0}°]");
            sb.AppendLine("  → 전체 목록에서 랜덤 선택으로 폴백합니다.");
            Debug.LogWarning(sb.ToString());
        }

        // 조건 충족 공격 없으면 전체 목록에서 랜덤 폴백
        // (거리/각도 조건 미충족이더라도 Sequence가 이미 이 노드까지 도달했으므로 시도)
        AIAttackAction chosen = validAttacks.Count > 0
            ? validAttacks[UnityEngine.Random.Range(0, validAttacks.Count)]
            : attackState.attackActions[UnityEngine.Random.Range(0, attackState.attackActions.Count)];

        // ── Poise 연동 (설계 문서 A3) ──
        // enablePoiseDuringAttack=true 이면 공격 모션 중 피격되어도 경직 없음 (강공격 등)
        if (chosen.enablePoiseDuringAttack)
            _aiManager.isPoiseActive = true;

        // ── 이동 차단 ──
        _aiManager.isPerformingAction = true;
        _aiManager.canMove = false;
        _aiManager.canRotate = false;

        if (_nav != null && _nav.isActiveAndEnabled && _nav.isOnNavMesh)
        {
            _nav.ResetPath();
            _nav.velocity = Vector3.zero;
        }

        // ── 공격 쿨다운 설정 ──
        // 다음 공격까지 minimumTimeBetweenAttacks ~ maximumTimeBetweenAttacks 초 대기
        _aiManager.aiCharacterCombatManager.SetAttackCooldown();

        // ── PlayTargetActionFunnel 호출 (핵심) ──
        // CharacterAnimationManager.PlayTargetActionFunnel():
        //   character.animator.SetInteger(ActionState, targetActionIndex)
        //   character.animator.SetTrigger(onAction)
        //   character.isPerformingAction = isPerformAction
        // ResetCharacterStateBehaviour 가 애니메이션 종료 시
        //   isPerformingAction = false, ActionState = 0 자동 리셋
        _aiManager.characterAnimationManager.PlayTargetActionFunnel(
            chosen.attackActionID,  // Animator ActionState 파라미터 값 (Enums.ActionID)
            true,                   // isPerformingAction = true
            false,                  // applyRootMotion (공격 중 루트모션 비활성화)
            false,                  // canRotate
            false);                 // canMove

        Debug.Log($"[BT] {Self.Value.name}: Strike! 공격 모션 실행. " +
                  $"ActionID={chosen.attackActionID} dist={dist:F1}m angle={angle:F0}° " +
                  $"(대상: {Target.Value?.name ?? "null"})");

        return true;
    }

    /// <summary>
    /// AttackState 를 우선순위에 따라 취득합니다.
    ///   1순위: AttackConfig BB 변수 (PB4DecisionAdapter 자동 설정 or 수동 연결)
    ///   2순위: PB4DecisionAdapter.GetAttackState() 런타임 탐색
    /// </summary>
    private AttackState ResolveAttackState()
    {
        // ── 1순위: AttackConfig BB 변수 ──
        // PB4DecisionAdapter.InitializeBlackboard() 에서 "AttackStateConfig" BB 변수를
        // pursueState→combatStanceState→attackState 체인으로 자동 설정합니다.
        // BT 에디터에서 AttackConfig 필드를 "AttackStateConfig" BB 변수에 연결하세요.
        if (AttackConfig?.Value != null)
            return AttackConfig.Value;

        // ── 2순위: PB4DecisionAdapter 런타임 탐색 ──
        // AttackConfig 미연결이거나 BB 변수가 null 일 때 폴백.
        // GetAttackState() = pursueState?.combatStanceState?.attackState
        var adapter = Self.Value.GetComponent<PB4DecisionAdapter>();
        if (adapter != null)
        {
            var resolved = adapter.GetAttackState();
            if (resolved != null) return resolved;
        }

        return null;
    }

    // =========================================================================
    // 타겟 방향 Slerp 회전
    // =========================================================================

    /// <summary>
    /// 공격 전 타겟 방향으로 Slerp 회전합니다.
    /// 기존 FSM AttackState.RotateTowardsTarget() 과 동일한 패턴.
    /// </summary>
    private void RotateTowardsTarget()
    {
        if (Target.Value == null) return;

        Vector3 dir = Target.Value.position - Self.Value.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir);
        Self.Value.transform.rotation = Quaternion.Slerp(
            Self.Value.transform.rotation,
            targetRot,
            AttackRotationSpeed.Value * Time.deltaTime);
    }
}