// =============================================================================
// CombatStanceState.cs  |  TDA Project
// Layer  : L3 Domain — AI 전투 대기 FSM State
// 수정 이력:
//   P0 ⑧  groggyState 필드 추가
//          Tick() 상단에 currentPoise.Value <= 0 → GroggyState 전환 로직 추가
// =============================================================================
using UnityEngine;
using UnityEngine.AI;

namespace TDA.Character.AI
{
    [CreateAssetMenu(menuName = "AI/States/CombatStance")]
    public class CombatStanceState : AIState
    {
        // ─────────────────────────────────────────────────────────────────────
        // 다음 상태 참조 (인스펙터에서 SO 연결)
        // ─────────────────────────────────────────────────────────────────────
        [Header("Next States")]
        [Tooltip("공격 범위 내 진입 시 전환")]
        public AttackState attackState;

        [Tooltip("타겟이 너무 멀 때 전환")]
        public PursueTargetState pursueTargetState;

        [Tooltip("타겟 소실 시 전환")]
        public IdleState idleState;

        // ── ⑧ 그로기 전환 (신규) ──────────────────────────────────────────────
        [Header("Groggy Transition  (P0 ⑧)")]
        [Tooltip("[P0 ⑧] 포이즈가 파괴(currentPoise ≤ 0)되었을 때 전환할 그로기 상태.\n" +
                 "인스펙터에서 GroggyState SO 를 반드시 연결하세요.\n" +
                 "연결하지 않으면 포이즈 파괴 전환이 발생하지 않습니다.")]
        public GroggyState groggyState;
        // ─────────────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────────────
        // 전투 설정
        // ─────────────────────────────────────────────────────────────────────
        [Header("Combat Behaviour")]
        [Tooltip("이 거리 이하일 때 공격 시도")]
        public float attackRange = 2.5f;

        [Tooltip("이 거리 초과 시 PursueTargetState 또는 IdleState 로 전환")]
        public float maximumChaseRange = 20f;

        [Tooltip("스트레이핑 활성화 여부\n" +
                 "true : HandleStrafingAroundTarget() 호출\n" +
                 "false: pursueTargetState 로 직접 이동")]
        public bool enableStrafing = true;

        [Tooltip("방어 행동 활성화 여부\n" +
                 "true : HandleDefensiveActions() 로 위빙/패링/방어 시도")]
        public bool enableDefensiveActions = true;

        // =====================================================================
        // Tick
        // =====================================================================
        public override AIState Tick(AICharacterManager aiCharacter)
        {
            if (!aiCharacter.IsServer) return this;

            // ── 사망 ─────────────────────────────────────────────────────────
            if (aiCharacter.characterNetworkManager.isDead.Value)
                return SwitchState(aiCharacter, idleState);

            // ── 타겟 유효성 ──────────────────────────────────────────────────
            CharacterManager target = aiCharacter.aiCharacterCombatManager.currentTarget;
            if (target == null || target.characterNetworkManager.isDead.Value)
            {
                aiCharacter.aiCharacterCombatManager.ClearTarget();
                return SwitchState(aiCharacter, idleState);
            }

            // ── Ghost Action 안전망 ───────────────────────────────────────────
            ClearGhostActionIfStuck(aiCharacter);

            // ── ⑧ 포이즈 파괴 감지 → GroggyState 전환 ──────────────────────
            //    currentPoise.Value 가 0 이하이면 즉시 GroggyState 로 전환.
            //    groggyState 가 null 이면 전환하지 않음 (인스펙터 미연결 방지).
            if (groggyState != null
                && aiCharacter.characterNetworkManager.currentPoise.Value <= 0f)
            {
                return SwitchState(aiCharacter, groggyState);
            }
            // ─────────────────────────────────────────────────────────────────

            // ── 액션 중이면 다른 로직 스킵 ──────────────────────────────────
            if (aiCharacter.isPerformingAction) return this;

            float dist = Vector3.Distance(
                aiCharacter.transform.position,
                target.transform.position);

            // ── 추격 범위 초과 ────────────────────────────────────────────────
            if (dist > maximumChaseRange)
            {
                aiCharacter.aiCharacterCombatManager.ClearTarget();
                return SwitchState(aiCharacter, idleState);
            }

            // ── 공격 범위 내 + 쿨다운 완료 → AttackState ────────────────────
            if (dist <= attackRange && aiCharacter.aiCharacterCombatManager.CanAttack())
            {
                if (aiCharacter.navMeshAgent.isOnNavMesh)
                    aiCharacter.navMeshAgent.ResetPath();
                return SwitchState(aiCharacter, attackState);
            }

            // ── 방어 행동 ─────────────────────────────────────────────────────
            if (enableDefensiveActions)
                aiCharacter.aiCharacterCombatManager.HandleDefensiveActions(dist);

            // ── 스트레이핑 or 추격 ────────────────────────────────────────────
            if (enableStrafing)
            {
                aiCharacter.aiCharacterCombatManager.HandleStrafingAroundTarget();
            }
            else if (pursueTargetState != null && dist > attackRange)
            {
                return SwitchState(aiCharacter, pursueTargetState);
            }

            // 공격 쿨다운 타이머 감소
            aiCharacter.aiCharacterCombatManager.HandleAttackTimer();

            return this;
        }

        // =====================================================================
        // Ghost Action 안전망
        // 애니메이터가 Empty 상태로 고착됐을 때 isPerformingAction 을 강제 해제합니다.
        // =====================================================================
        private void ClearGhostActionIfStuck(AICharacterManager aiCharacter)
        {
            if (!aiCharacter.isPerformingAction) return;

            var info = aiCharacter.animator.GetCurrentAnimatorStateInfo(0);
            bool isEmptyState = info.IsName("Empty") || info.IsName("Base Layer.Empty");

            if (isEmptyState)
            {
                aiCharacter.isPerformingAction = false;
                aiCharacter.canMove = true;
                aiCharacter.canRotate = true;
            }
        }

        // =====================================================================
        // ResetStateFlags
        // =====================================================================
        protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
        {
            base.ResetStateFlags(aiCharacterManager);
        }
    }
}