// =============================================================================
// CharacterExecutionManager.cs  |  TDA Project
// Layer  : L3 Domain — 처형(Execution) 시스템 공통 기반 클래스
//
// 역할:
//   PlayerExecutionManager / AICharacterExecutionManager 의 공통 부모 클래스입니다.
//   CharacterManager.characterExecutionManager 필드의 타입으로 사용되어
//   플레이어·AI 모두 동일한 인터페이스로 접근할 수 있습니다.
//
// 아키텍처 규약:
//   - L3 Domain : 처형 상태 판단 및 진입/종료 로직만 담당합니다.
//   - Animator 직접 조작 금지 : PlayTargetActionFunnel() 경유 필수.
//   - NGO 서버 권위형 : 처형 판정은 IsServer 게이트 안에서만 수행합니다.
//
// 하위 클래스:
//   - PlayerExecutionManager : 플레이어가 처형을 시전(Executor) 할 때
//   - AICharacterExecutionManager     : AI가 처형 대상(Victim)이 될 때
// =============================================================================
using Unity.Netcode;
using UnityEngine;

namespace TDA.Character
{
    public class CharacterExecutionManager : NetworkBehaviour
    {
        // =====================================================================
        // 내부 참조
        // =====================================================================
        protected CharacterManager character;

        /// <summary>처형 파트너 (공격자면 피격자, 피격자면 공격자).</summary>
        protected CharacterManager executionPartner;

        // =====================================================================
        // 처형 상태 플래그
        // =====================================================================
        public NetworkVariable<bool> isBeingExecuted = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [Header("Debug")]
        public bool showDebugLogs = false;

        // =====================================================================
        // Unity 생명주기
        // =====================================================================
        protected virtual void Awake()
        {
            character = GetComponent<CharacterManager>();
        }

        /// <summary>
        /// 타이머·상태 갱신이 필요한 하위 클래스(PlayerExecutionManager)에서 Override 합니다.
        /// </summary>
        protected virtual void Update() { }

        // =====================================================================
        // 공개 API
        // =====================================================================

        /// <summary>
        /// 파라미터 없는 처형 시작. AICharacterExecutionManager 에서 사용합니다.
        /// </summary>
        public virtual void BeginExecution()
        {
            if (!IsServer) return;
            isBeingExecuted.Value = true;
        }

        /// <summary>
        /// 대상과 역할(공격자/피격자)을 지정하는 처형 시작.
        /// PlayerExecutionManager 에서 Override 합니다.
        /// </summary>
        public virtual void BeginExecution(CharacterManager target, bool asAttacker)
        {
            if (!IsServer) return;
            executionPartner = target;
            isBeingExecuted.Value = true;
        }

        /// <summary>처형 시퀀스를 종료합니다.</summary>
        public virtual void EndExecution()
        {
            if (!IsServer) return;
            isBeingExecuted.Value = false;
        }

        /// <summary>처형 진행 중 경직을 무시해야 하는지 반환합니다.</summary>
        public virtual bool ShouldIgnoreStaggerDuringExecution()
        {
            return isBeingExecuted.Value;
        }

        // =====================================================================
        // 처형 흐름 훅 — 하위 클래스에서 Override
        // =====================================================================

        /// <summary>
        /// 처형이 정상 완료(피니시)된 직후 호출됩니다.
        /// PlayerExecutionManager 에서 카메라 시퀀스·보상 처리에 사용합니다.
        /// </summary>
        protected virtual void OnExecutionFinished() { }

        /// <summary>
        /// 처형 가능 여부를 검사합니다.
        /// 하위 클래스에서 거리·포이즈·사망 등의 조건을 추가합니다.
        /// </summary>
        protected virtual bool CanAttemptExecution(CharacterManager target)
        {
            if (target == null) return false;
            if (isBeingExecuted.Value) return false;
            return true;
        }

        /// <summary>
        /// 처형 상태를 정리합니다 (중단 또는 완료 후 공통 처리).
        /// </summary>
        protected virtual void CleanUpExecution()
        {
            executionPartner = null;
            isBeingExecuted.Value = false;
        }

        /// <summary>
        /// 외부(상대방 QTE 실패 등)에서 처형 잠금을 강제 해제합니다.
        /// </summary>
        public virtual void ReleaseExecutionExternal()
        {
            if (!IsServer) return;
            CleanUpExecution();
        }

        // =====================================================================
        // NGO RPC — 처형 시작 동기화
        // =====================================================================

        /// <summary>
        /// 클라이언트(플레이어)가 처형 시작을 서버에 요청합니다.
        /// PlayerExecutionManager.AttemptExecution() 에서 호출합니다.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public virtual void RequestExecutionStartServerRpc(
            ulong attackerNetworkId, ulong victimNetworkId)
        {
            if (!IsServer) return;

            var spawned = NetworkManager.Singleton.SpawnManager.SpawnedObjects;
            if (!spawned.TryGetValue(attackerNetworkId, out NetworkObject attackerObj)) return;
            if (!spawned.TryGetValue(victimNetworkId, out NetworkObject victimObj)) return;

            var attacker = attackerObj.GetComponent<CharacterManager>();
            var victim = victimObj.GetComponent<CharacterManager>();
            if (attacker == null || victim == null) return;

            // 피격자 AICharacterExecutionManager 에 BeginExecution 호출
            var victimExec = victim.characterExecutionManager;
            if (victimExec != null)
                victimExec.BeginExecution();

            // 공격자·피격자 모두에게 처형 시작 알림
            NotifyExecutionStartClientRpc(attackerNetworkId, victimNetworkId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        public virtual void NotifyExecutionStartClientRpc(
            ulong attackerNetworkId, ulong victimNetworkId)
        {
            var spawned = NetworkManager.Singleton.SpawnManager.SpawnedObjects;
            if (!spawned.TryGetValue(attackerNetworkId, out NetworkObject attackerObj)) return;
            if (!spawned.TryGetValue(victimNetworkId, out NetworkObject victimObj)) return;

            var attacker = attackerObj.GetComponent<CharacterManager>();
            var victim = victimObj.GetComponent<CharacterManager>();
            if (attacker == null || victim == null) return;

            var attackerExec = attacker.characterExecutionManager;
            var victimExec = victim.characterExecutionManager;

            attackerExec?.BeginExecution(victim, true);
            victimExec?.BeginExecution(attacker, false);
        }
    }
}