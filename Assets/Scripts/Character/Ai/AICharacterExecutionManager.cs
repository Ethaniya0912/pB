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
//
// 수정 이력:
//   [개선] BeginExecution() — IsServer 게이트 과도 적용 문제 해결.
//          기존: canMove=false, NavMesh 정지 등 시각 플래그 전부 서버에서만 처리됨.
//          개선: 서버 판정(isBeingExecuted) 유지 + ApplyVictimStateClientRpc() 분리
//               → 모든 클라이언트가 AI 이동 잠금을 즉시 반영.
//   [개선] EndExecution() — RestoreVictimStateClientRpc() 분리로 처형 종료 시 모든 클라이언트 복원.
//   [개선] ReleaseExecutionExternal() — 강제 해제 시에도 ClientRpc 로 즉시 복원.
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
        ///
        /// [개선] 기존 문제:
        ///   BeginExecution() 전체가 IsServer 게이트로 막혀 있어
        ///   canMove=false, NavMesh 정지 같은 로컬 시각 플래그가
        ///   서버에서만 처리되고 다른 클라이언트에서 즉시 반응하지 않았습니다.
        ///
        /// 해결:
        ///   서버에서 처형 상태(isBeingExecuted) 판정 후 ApplyVictimStateClientRpc() 를 호출해
        ///   모든 클라이언트가 동일한 시각 상태를 즉시 적용합니다.
        /// </summary>
        public override void BeginExecution()
        {
            if (!IsServer) return;
            base.BeginExecution(); // isBeingExecuted.Value = true

            // 모든 클라이언트에 시각 상태 즉시 적용
            ApplyVictimStateClientRpc(true);

            DebugLog("[BeginExecution] 처형 피격 시퀀스 시작 → ClientRpc 발송");
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
            base.EndExecution(); // isBeingExecuted.Value = false
            SetExecutable(false);

            // 모든 클라이언트에 복원 상태 즉시 적용
            RestoreVictimStateClientRpc();

            DebugLog("[EndExecution] 처형 피격 시퀀스 종료 → ClientRpc 발송");
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
        // Override : 외부 강제 해제
        // =====================================================================
        /// <summary>
        /// QTE 실패 등 외부에서 처형 잠금을 강제 해제합니다.
        /// 모든 클라이언트에 즉시 복원 상태를 전파합니다.
        /// </summary>
        public override void ReleaseExecutionExternal()
        {
            if (!IsServer) return;
            // 모든 클라이언트 즉시 복원
            ApplyVictimStateClientRpc(false);
            CleanUpExecution();
        }

        // =====================================================================
        // ClientRpc — 시각 상태 적용 (모든 클라이언트)
        // =====================================================================

        /// <summary>
        /// [신규] 처형 시작/중단 시 모든 클라이언트에서 AI 이동 정지 및 상태 플래그를 설정합니다.
        /// isBeingVictim=true : 처형 진입 (이동 잠금)
        /// isBeingVictim=false: 처형 중단 (QTE 실패 등 강제 해제 시 즉시 복원)
        /// </summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void ApplyVictimStateClientRpc(bool isBeingVictim)
        {
            if (aiCharacter == null) return;

            if (isBeingVictim)
            {
                // 처형 중 이동·공격 불가
                aiCharacter.canMove = false;
                aiCharacter.canRotate = false;
                aiCharacter.isPerformingAction = true;

                // NavMesh 이동 즉시 정지
                if (aiCharacter.navMeshAgent != null
                    && aiCharacter.navMeshAgent.isActiveAndEnabled
                    && aiCharacter.navMeshAgent.isOnNavMesh)
                {
                    aiCharacter.navMeshAgent.ResetPath();
                    aiCharacter.navMeshAgent.velocity = Vector3.zero;
                }

                DebugLog("[ApplyVictimStateClientRpc] 처형 상태 적용 (모든 클라이언트)");
            }
            else
            {
                // QTE 실패 등 강제 중단 시 즉시 복원
                RestoreAIState();
                DebugLog("[ApplyVictimStateClientRpc] 처형 중단 → 상태 복원 (모든 클라이언트)");
            }
        }

        /// <summary>
        /// [신규] 처형 완료 후 모든 클라이언트에서 AI 상태를 복원합니다.
        /// </summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void RestoreVictimStateClientRpc()
        {
            RestoreAIState();
            DebugLog("[RestoreVictimStateClientRpc] 처형 종료 → 상태 복원 (모든 클라이언트)");
        }

        // =====================================================================
        // 유틸
        // =====================================================================

        /// <summary>AI 이동·회전 플래그와 NavMesh 를 정상 상태로 복원합니다.</summary>
        private void RestoreAIState()
        {
            if (aiCharacter == null) return;

            aiCharacter.canMove = true;
            aiCharacter.canRotate = true;
            aiCharacter.isPerformingAction = false;

            // NavMeshAgent 재활성화 (비활성화했던 경우)
            if (aiCharacter.navMeshAgent != null
                && aiCharacter.navMeshAgent.isActiveAndEnabled)
            {
                aiCharacter.navMeshAgent.ResetPath();
            }
        }

        private void DebugLog(string msg)
        {
#if UNITY_EDITOR
            if (showDebugLogs)
                Debug.Log($"<color=magenta>[AICharacterExecutionManager:{name}]</color> {msg}");
#endif
        }
    }
}