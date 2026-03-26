// =============================================================================
// AttackState.cs  |  TDA Project
// Layer  : L3 Domain — AI 공격 실행 FSM State
// 수정 이력:
//   P0 ⑤  AIAttackAction 구조체 : poiseDamage / enablePoiseDuringAttack 추가
//   P1 ⑥  Tick()               : isPoiseActive = enablePoiseDuringAttack 설정
//   P1 ⑦  RotateTowardsTarget  : transform.rotation 즉각 회전 → Quaternion.Slerp 교체
//   Fix   : CS1739 에러 조치 (PlayTargetActionFunnel 위치 매개변수 변경)
//   Fix   : CS1061 에러 조치 (PursueTargetState용 GetMaximumAttackRange() 복구)
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace TDA.Character.AI
{
    // =========================================================================
    // ⑤ AIAttackAction 구조체 — 포이즈 데미지 필드 추가
    // =========================================================================
    [System.Serializable]
    public struct AIAttackAction
    {
        // ── 기본 공격 정보 ────────────────────────────────────────────────────
        [Header("Animation")]
        [Tooltip("공격 애니메이션 ActionID (Enums.ActionID 값)")]
        public int attackActionID;

        [Header("Range & Angle")]
        [Tooltip("이 공격이 발동 가능한 최소 거리")]
        public float minimumAttackDistance;
        [Tooltip("이 공격이 발동 가능한 최대 거리")]
        public float maximumAttackDistance;
        [Tooltip("이 공격이 발동 가능한 최소 각도 (타겟 방향 기준)")]
        public float minimumAttackAngle;
        [Tooltip("이 공격이 발동 가능한 최대 각도 (타겟 방향 기준)")]
        public float maximumAttackAngle;

        // ── ⑤ 포이즈 연동 (신규) ───────────────────────────────────────────────
        [Header("Poise & Combat Feel  (P0 ⑤)")]
        [Tooltip("이 공격이 가하는 포이즈 데미지.\n" +
                 "TakeDamageEffect 에서 currentPoise 와 비교합니다.\n" +
                 "높을수록 경직(Guard_Break_Stun)을 유발하기 쉽습니다.")]
        public float poiseDamage;

        [Tooltip("True 이면 이 공격 모션 중 AI 의 포이즈가 유지됩니다.\n" +
                 "(강공격·차지 등 — 피격되어도 경직 없음)\n" +
                 "Tick() 에서 aiCharacter.isPoiseActive 를 true 로 설정합니다.")]
        public bool enablePoiseDuringAttack;

        // ── ⑦ 회전 설정 ──────────────────────────────────────────────────────
        [Header("Rotation  (P1 ⑦)")]
        [Tooltip("공격 중 타겟 방향 Slerp 회전 속도.\n" +
                 "0 이면 회전 없음. 권장: 3~8")]
        public float rotationSpeedDuringAttack;
    }

    // =========================================================================
    // AttackState — FSM State ScriptableObject
    // =========================================================================
    [CreateAssetMenu(menuName = "AI/States/Attack")]
    public class AttackState : AIState
    {
        // ─────────────────────────────────────────────────────────────────────
        // 인스펙터 연결
        // ─────────────────────────────────────────────────────────────────────
        [Header("Next States")]
        [Tooltip("공격 완료 후 복귀할 전투 대기 상태")]
        public CombatStanceState combatStanceState;

        [Header("Attack Action List")]
        [Tooltip("이 AI 가 가진 공격 패턴 목록. 거리·각도 조건에 맞는 것 중 무작위 선택.")]
        public List<AIAttackAction> attackActions = new List<AIAttackAction>();

        // ─────────────────────────────────────────────────────────────────────
        // 런타임 상태
        // ─────────────────────────────────────────────────────────────────────
        private AIAttackAction chosenAttack;
        private bool hasChosenAttack = false;
        private bool hasExecuted = false;

        // =====================================================================
        // Tick
        // =====================================================================
        public override AIState Tick(AICharacterManager aiCharacter)
        {
            if (!aiCharacter.IsServer) return this;

            // 사망 즉시 복귀
            if (aiCharacter.characterNetworkManager.isDead.Value)
                return SwitchState(aiCharacter, combatStanceState);

            // 타겟 유효성
            CharacterManager target = aiCharacter.aiCharacterCombatManager.currentTarget;
            if (target == null || target.characterNetworkManager.isDead.Value)
                return SwitchState(aiCharacter, combatStanceState);

            // 공격 선택 (최초 1회)
            if (!hasChosenAttack)
            {
                ChooseAttack(aiCharacter, target);
                if (!hasChosenAttack)
                    return SwitchState(aiCharacter, combatStanceState); // 유효 공격 없음
            }

            // 공격 실행 대기 중 (isPerformingAction == true)
            if (hasExecuted)
            {
                if (aiCharacter.isPerformingAction) return this; // 모션 완료 대기
                return SwitchState(aiCharacter, combatStanceState); // 모션 완료 → 복귀
            }

            // ⑦ 공격 실행 전 타겟 방향으로 Slerp 회전 (즉각 LookRotation 제거)
            RotateTowardsTarget(aiCharacter, target, chosenAttack.rotationSpeedDuringAttack);

            // ⑥ isPoiseActive 설정 — enablePoiseDuringAttack=true 이면 공격 중 포이즈 유지
            if (chosenAttack.enablePoiseDuringAttack)
                aiCharacter.isPoiseActive = true;

            // 이동 정지 (공격 중 NavMesh 이동 차단)
            if (aiCharacter.navMeshAgent.isActiveAndEnabled && aiCharacter.navMeshAgent.isOnNavMesh)
            {
                aiCharacter.navMeshAgent.ResetPath();
                aiCharacter.navMeshAgent.velocity = Vector3.zero;
            }

            // 공격 실행 (Funnel — IFrame / 데미지 이벤트 자동 처리)
            // [컴파일 에러 수정] CS1739: 명명된 인수 대신 위치 매개변수 사용
            aiCharacter.characterAnimationManager.PlayTargetActionFunnel(
                chosenAttack.attackActionID,
                true,   // isPerformingAction
                false); // canRotate

            // 쿨다운 설정
            aiCharacter.aiCharacterCombatManager.SetAttackCooldown();

            hasExecuted = true;
            return this; // 다음 프레임에서 모션 완료 대기
        }

        // =====================================================================
        // ⑦ 타겟 방향으로 Slerp 회전
        //    기존: transform.rotation = Quaternion.LookRotation(dir)  →  즉각 회전
        //    수정: Quaternion.Slerp 로 부드럽게 보간
        // =====================================================================
        private void RotateTowardsTarget(
            AICharacterManager aiCharacter,
            CharacterManager target,
            float rotSpeed)
        {
            if (rotSpeed <= 0f) return;
            if (!aiCharacter.canRotate) return;

            Vector3 dir = target.transform.position - aiCharacter.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;

            Quaternion targetRot = Quaternion.LookRotation(dir);

            // ⑦ Slerp (rotSpeed * deltaTime 로 보간 속도 제어)
            aiCharacter.transform.rotation = Quaternion.Slerp(
                aiCharacter.transform.rotation,
                targetRot,
                rotSpeed * Time.deltaTime);
        }

        // =====================================================================
        // 공격 선택 — 거리·각도 조건 충족 목록 중 무작위 선택
        // =====================================================================
        private void ChooseAttack(AICharacterManager aiCharacter, CharacterManager target)
        {
            float dist = Vector3.Distance(aiCharacter.transform.position, target.transform.position);
            float angle = Vector3.Angle(
                aiCharacter.transform.forward,
                (target.transform.position - aiCharacter.transform.position).normalized);

            var valid = new List<AIAttackAction>();
            foreach (var a in attackActions)
            {
                if (dist >= a.minimumAttackDistance && dist <= a.maximumAttackDistance
                 && angle >= a.minimumAttackAngle && angle <= a.maximumAttackAngle)
                    valid.Add(a);
            }

            if (valid.Count == 0) return;

            chosenAttack = valid[Random.Range(0, valid.Count)];
            hasChosenAttack = true;
        }

        // =====================================================================
        // ResetStateFlags
        // =====================================================================
        protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
        {
            base.ResetStateFlags(aiCharacterManager);
            hasChosenAttack = false;
            hasExecuted = false;

            // ⑥ 공격 종료 시 isPoiseActive 리셋
            aiCharacterManager.isPoiseActive = false;
        }

        // =====================================================================
        // [컴파일 에러 수정] CS1061: PursueTargetState에서 접근하는 최대 사거리 반환
        // =====================================================================
        public float GetMaximumAttackRange()
        {
            float maxRange = 0f;
            foreach (var action in attackActions)
            {
                if (action.maximumAttackDistance > maxRange)
                    maxRange = action.maximumAttackDistance;
            }
            return maxRange;
        }

        // =====================================================================
        // 유틸
        // =====================================================================
        private void DebugLog(string msg, AICharacterManager aiCharacter)
        {
#if UNITY_EDITOR
            Debug.Log($"<color=orange>[AttackState:{aiCharacter.name}]</color> {msg}");
#endif
        }
    }
}