// =============================================================================
// GroggyState.cs  |  TDA Project
// Layer  : L3 Domain — AI 그로기(포이즈 파괴) FSM State
//
// 역할:
//   포이즈(currentPoise)가 0 이하가 되었을 때 CombatStanceState 가 전환해주는
//   그로기 상태입니다. 이 상태에서 플레이어는 처형(Execution)을 시도할 수 있습니다.
//
// 진입 조건 (CombatStanceState.Tick() 에서 판단):
//   aiCharacter.characterNetworkManager.currentPoise.Value <= 0f
//
// 탈출 조건:
//   1. groggyDuration 초 경과 → CombatStanceState 복귀 (포이즈 회복)
//   2. 처형 진행 중 (isBeingExecuted) → 타이머 일시정지 (처형 완료 시 사망 처리)
//   3. 사망 → CombatStanceState 복귀 (사망 처리는 ProcessDeathEvent 가 담당)
//
// 아키텍처 규약:
//   - Animator 직접 조작 금지 : PlayTargetActionFunnel() 경유 필수
//   - NGO 서버 권위형 : Tick() 내부 모든 상태 변경은 IsServer 게이트 확인
//   - FSM 단일 책임 : 이동/공격 금지 플래그만 설정, 실제 이동 차단은 AICharacterLocomotionManager 가 처리
//
// 버그 수정 이력 (CodeReview v1.0):
//   [I-08 수정] SO 인스턴스 공유 타이머 오염 해소
//     - 기존: groggyTimer / hasEnteredGroggy 를 SO 클래스 private 필드로 선언
//             → 여러 AI 가 동일 SO 공유 시 타이머가 오염되어 EnterGroggy() 건너뜀 버그
//     - 수정: aiCharacter.groggyTimer / aiCharacter.hasEnteredGroggy 로 이관
//             (AICharacterManager 인스턴스 필드 — 각 AI 오브젝트가 독립 보유)
//     - SO 내부 private 필드 제거. AICharacterManager 에 신규 필드 추가됨.
// =============================================================================
using UnityEngine;
using UnityEngine.AI;

namespace TDA.Character.AI
{
    [CreateAssetMenu(menuName = "AI/States/Groggy")]
    public class GroggyState : AIState
    {
        // =====================================================================
        // 다음 상태 참조 (인스펙터에서 SO 연결)
        // =====================================================================
        [Header("Next States")]
        [Tooltip("그로기 종료(타이머 만료) 후 복귀할 전투 대기 상태")]
        public CombatStanceState combatStanceState;

        // =====================================================================
        // 그로기 설정
        // =====================================================================
        [Header("Groggy Settings")]
        [Tooltip("그로기 유지 시간(초).\n이 시간 동안 처형 가능 상태가 유지됩니다.\n" +
                 "처형이 시작되면 타이머는 일시 정지됩니다.")]
        public float groggyDuration = 4.0f;

        [Tooltip("그로기 진입 시 재생할 ActionID.\n" +
                 "Enums.ActionID.Guard_Break_Stun = 111 (기본값).\n" +
                 "인스펙터에서 몬스터별로 교체 가능합니다.")]
        public int groggyAnimationID = 111; // ActionID.Guard_Break_Stun

        // =====================================================================
        // 런타임 상태 (ScriptableObject 이므로 초기화 필수)
        // =====================================================================
        // [I-08 수정] groggyTimer / hasEnteredGroggy 를 SO 필드에서 제거하고
        //             AICharacterManager 인스턴스 필드로 이관했습니다.
        //
        // 이관 이유:
        //   ScriptableObject 는 에셋 인스턴스이므로 여러 AI 오브젝트가 동일 SO 를 공유합니다.
        //   SO 에 런타임 상태를 저장하면 AI-A 가 값을 바꿀 때 AI-B 에게도 영향을 줍니다.
        //   (동시에 두 AI 가 그로기에 진입하면 hasEnteredGroggy = true 가 공유되어
        //    두 번째 AI 의 EnterGroggy() 가 건너뛰어지는 버그)
        //
        // 수정 결과:
        //   Tick() / EnterGroggy() / ExitGroggy() / ResetStateFlags() 에서
        //   aiCharacter.groggyTimer / aiCharacter.hasEnteredGroggy 를 사용합니다.
        //   각 AI 오브젝트(MonoBehaviour)가 독립된 값을 보유하므로 공유 오염이 없습니다.
        //
        // AICharacterManager.cs 에 추가된 필드:
        //   [HideInInspector] public float groggyTimer = 0f;
        //   [HideInInspector] public bool hasEnteredGroggy = false;
        // =====================================================================

        // =====================================================================
        // Tick
        // =====================================================================
        public override AIState Tick(AICharacterManager aiCharacter)
        {
            if (!aiCharacter.IsServer) return this;

            // ── 사망 → 즉시 복귀 (ProcessDeathEvent 가 처리) ────────────────
            if (aiCharacter.characterNetworkManager.isDead.Value)
                return SwitchState(aiCharacter, combatStanceState);

            // ── 최초 진입 처리 ────────────────────────────────────────────────
            // [I-08] SO 필드 → aiCharacter 인스턴스 필드 참조
            if (!aiCharacter.hasEnteredGroggy)
            {
                EnterGroggy(aiCharacter);
                return this;
            }

            // ── 처형 진행 중 → 타이머 일시 정지 ─────────────────────────────
            if (aiCharacter.characterExecutionManager != null
                && aiCharacter.characterExecutionManager.isBeingExecuted.Value)
                return this;

            // ── 그로기 타이머 진행 ────────────────────────────────────────────
            // [I-08] SO 필드 → aiCharacter 인스턴스 필드 참조
            aiCharacter.groggyTimer += Time.deltaTime;
            if (aiCharacter.groggyTimer >= groggyDuration)
            {
                ExitGroggy(aiCharacter);
                return SwitchState(aiCharacter, combatStanceState);
            }

            return this;
        }

        // =====================================================================
        // 그로기 진입 처리
        // =====================================================================
        private void EnterGroggy(AICharacterManager aiCharacter)
        {
            // [I-08] SO 필드 → aiCharacter 인스턴스 필드 참조
            aiCharacter.hasEnteredGroggy = true;
            aiCharacter.groggyTimer = 0f;

            // NavMesh 이동 즉시 정지
            if (aiCharacter.navMeshAgent != null
                && aiCharacter.navMeshAgent.isActiveAndEnabled
                && aiCharacter.navMeshAgent.isOnNavMesh)
            {
                aiCharacter.navMeshAgent.ResetPath();
                aiCharacter.navMeshAgent.velocity = Vector3.zero;
                aiCharacter.navMeshAgent.speed = 0f;
            }

            // 이동·회전 잠금 (FSM 책임)
            aiCharacter.canMove = false;
            aiCharacter.canRotate = false;

            // 포이즈 유지 플래그 해제 (처형 중 경직 무시는 AICharacterExecutionManager 가 담당)
            aiCharacter.isPoiseActive = false;

            // 그로기 모션 재생 (Funnel 경유 — Animator 직접 조작 금지)
            aiCharacter.characterAnimationManager.PlayTargetActionFunnel(
                groggyAnimationID,
                true,   // isPerformingAction
                false,  // applyRootMotion (그로기 중 미끄러짐 방지)
                false,  // canRotate
                false); // canMove

            // AICharacterExecutionManager 에 처형 가능 상태 알림
            if (aiCharacter.characterExecutionManager is AICharacterExecutionManager execMgr)
                execMgr.SetExecutable(true);

            DebugLog("[EnterGroggy] 그로기 진입. 처형 가능 상태 ON", aiCharacter);
        }

        // =====================================================================
        // 그로기 종료 처리 (타이머 만료)
        // =====================================================================
        private void ExitGroggy(AICharacterManager aiCharacter)
        {
            // 처형 가능 상태 해제
            if (aiCharacter.characterExecutionManager is AICharacterExecutionManager execMgr)
                execMgr.SetExecutable(false);

            // 포이즈 완전 회복 (서버 권위형)
            aiCharacter.characterNetworkManager.currentPoise.Value =
                aiCharacter.characterNetworkManager.maxPoise.Value;

            DebugLog($"[ExitGroggy] 그로기 종료. 포이즈 회복 완료 " +
                     $"({aiCharacter.characterNetworkManager.maxPoise.Value})", aiCharacter);
        }

        // =====================================================================
        // ResetStateFlags — 상태 전환 시 찌꺼기 정리
        // =====================================================================
        protected override void ResetStateFlags(AICharacterManager aiCharacterManager)
        {
            base.ResetStateFlags(aiCharacterManager); // NavMesh ResetPath + applyRootMotion=false

            // 런타임 상태 초기화 (ScriptableObject 재진입 대비)
            // [I-08] SO 필드 → aiCharacterManager 인스턴스 필드 참조
            aiCharacterManager.hasEnteredGroggy = false;
            aiCharacterManager.groggyTimer = 0f;

            // 이동·회전 복구
            aiCharacterManager.canMove = true;
            aiCharacterManager.canRotate = true;
        }

        // =====================================================================
        // 유틸
        // =====================================================================
        private void DebugLog(string msg, AICharacterManager aiCharacter)
        {
#if UNITY_EDITOR
            Debug.Log($"<color=cyan>[GroggyState:{aiCharacter.name}]</color> {msg}");
#endif
        }
    }
}