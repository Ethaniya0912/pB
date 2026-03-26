// =============================================================================
// AICharacterExecutionManager.cs  |  TDA Project
// Layer  : L3 Domain — AI 처형 피격자 매니저
//
// 역할:
//   AI 캐릭터가 처형(Execution) 대상(Victim)이 될 때의 로직을 담당합니다.
//   GroggyState 에서 플레이어가 처형을 시도할 수 있는 조건을 제공하고,
//   처형 모션 진행 중 경직 무시 여부를 TakeDamageEffect 에 알립니다.
//
// 아키텍처 규약:
//   - CharacterExecutionManager 를 상속하여 공통 인터페이스를 제공합니다.
//   - Animator 직접 조작 금지 : PlayTargetActionFunnel() 경유 필수.
//   - NGO 서버 권위형 : 처형 판정은 IsServer 게이트 안에서만 수행합니다.
//   - FSM 과 직접 통신하지 않습니다. 상태 전환은 AICharacterManager.Update()
//     에서 currentState.Tick()이 처리합니다.
//
// 연동:
//   AICharacterManager.Awake() 에서 자동 등록됩니다.
//   GroggyState.Tick() 에서 isExecutable 플래그를 확인합니다.
//   TakeDamageEffect 에서 ShouldIgnoreStaggerDuringExecution() 을 확인합니다.
// =============================================================================
using Unity.Netcode;
using UnityEngine;

namespace TDA.Character.AI
{
    /// <summary>
    /// [L3 Domain] AI 캐릭터가 처형 대상이 될 때의 상태를 관리합니다.
    /// GroggyState 에서 처형 가능 여부를 노출하며,
    /// 처형 중 경직(Stagger) 무시를 TakeDamageEffect 에 알립니다.
    /// </summary>
    public class AICharacterExecutionManager : CharacterExecutionManager
    {
        // =====================================================================
        // 내부 참조
        // =====================================================================
        private AICharacterManager aiCharacter;

        // =====================================================================
        // 처형 가능 상태 플래그
        // =====================================================================
        /// <summary>
        /// true 이면 플레이어가 이 AI 를 처형할 수 있습니다.
        /// GroggyState.Tick() 에서 이 값을 true 로 설정합니다.
        /// 처형이 시작되거나 그로기 상태가 해제되면 false 로 초기화됩니다.
        /// </summary>
        public NetworkVariable<bool> isExecutable = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // =====================================================================
        // Unity 생명주기
        // =====================================================================
        protected override void Awake()
        {
            base.Awake();
            aiCharacter = GetComponent<AICharacterManager>();
        }

        // =====================================================================
        // 처형 가능 상태 진입 (GroggyState 에서 호출)
        // =====================================================================
        /// <summary>
        /// AI 가 그로기 상태가 되어 처형 가능 상태로 진입합니다.
        /// GroggyState.OnEnterGroggy() 에서 호출됩니다.
        /// </summary>
        public void SetExecutable(bool value)
        {
            if (!IsServer) return;
            isExecutable.Value = value;

            DebugLog(value
                ? "[SetExecutable] 처형 가능 상태 진입"
                : "[SetExecutable] 처형 가능 상태 해제");
        }

        // =====================================================================
        // Override : 처형 시퀀스 시작
        // =====================================================================
        /// <summary>
        /// AI 가 처형 피격자로 확정되었을 때 호출됩니다.
        /// PlayerExecutionManager.AttemptExecution() 에서 호출됩니다.
        /// </summary>
        public override void BeginExecution()
        {
            if (!IsServer) return;
            base.BeginExecution();

            // 처형 중 이동·공격 불가
            if (aiCharacter != null)
            {
                aiCharacter.canMove = false;
                aiCharacter.canRotate = false;
                aiCharacter.isPerformingAction = true;
            }

            // NavMesh 이동 즉시 정지
            if (aiCharacter?.navMeshAgent != null
                && aiCharacter.navMeshAgent.isActiveAndEnabled
                && aiCharacter.navMeshAgent.isOnNavMesh)
            {
                aiCharacter.navMeshAgent.ResetPath();
                aiCharacter.navMeshAgent.velocity = Vector3.zero;
            }

            DebugLog("[BeginExecution] 처형 피격 시퀀스 시작");
        }

        // =====================================================================
        // Override : 처형 시퀀스 종료
        // =====================================================================
        /// <summary>
        /// 처형 애니메이션이 완료된 뒤 호출됩니다.
        /// 사망 처리는 ProcessDeathEvent() 에서 별도로 수행합니다.
        /// </summary>
        public override void EndExecution()
        {
            if (!IsServer) return;
            base.EndExecution();
            SetExecutable(false);

            DebugLog("[EndExecution] 처형 피격 시퀀스 종료");
        }

        // =====================================================================
        // Override : 처형 중 경직 무시
        // =====================================================================
        /// <summary>
        /// 처형 피격자가 처형 모션 중 추가 피격을 받아도 경직(Stagger)되지 않도록
        /// TakeDamageEffect 에서 이 메서드를 확인합니다.
        /// </summary>
        public override bool ShouldIgnoreStaggerDuringExecution()
        {
            return isBeingExecuted.Value;
        }

        // =====================================================================
        // 유틸
        // =====================================================================
        private void DebugLog(string msg)
        {
#if UNITY_EDITOR
            Debug.Log($"<color=magenta>[AICharacterExecutionManager:{name}]</color> {msg}");
#endif
        }
    }
}