// =============================================================================
// AICharacterNetworkManager.cs  |  TDA Project
// Layer  : L2 Router — AI 전용 네트워크 동기화 백본
//
// 역할:
//   CharacterNetworkManager 를 상속하여 AI(몬스터/NPC)에 특화된
//   NetworkVariable 과 RPC 를 추가합니다.
//
// 플레이어와의 차이:
//   - 스태미나 소모·보간 없음 (AI 는 스태미나 쿨다운 없이 공격)
//   - 워핑(Warping) 불필요 — AI 는 에임 어시스트 없이 NavMesh 이동
//   - 인벤토리 동기화 최소화 — 무기 ID 만 전파 (Player 급 장비 슬롯 불필요)
//   - Aggro(어그로) / Phase(페이즈) / SpawnChunk 좌표 서버 전파 추가
//
// 아키텍처 규약:
//   ① NetworkVariable 변경은 반드시 Owner(= 서버) 에서만 수행
//   ② Rpc 는 NGO 2.0 [Rpc(SendTo.Server)] / [Rpc(SendTo.ClientsAndHost)] 사용
//   ③ 모든 OnValueChanged 구독은 OnNetworkSpawn/OnNetworkDespawn 쌍으로 관리
// =============================================================================
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TDA.Character.AI
{
    /// <summary>
    /// [L2 Router] AI(몬스터/NPC) 전용 네트워크 동기화 매니저.
    /// CharacterNetworkManager 의 공통 변수(HP·포이즈·위치·애니메이션)를 그대로 상속하며
    /// AI 전용 어그로·페이즈·스폰 정보를 추가합니다.
    /// </summary>
    public class AICharacterNetworkManager : CharacterNetworkManager
    {
        // =====================================================================
        // AI 전용 NetworkVariable — 서버 권위 (Server Write)
        // =====================================================================

        [Header("AI — Aggro (어그로)")]
        /// <summary>
        /// 현재 AI 가 어그로를 유지하고 있는 플레이어의 NetworkObjectId.
        /// 0 이면 타겟 없음.
        /// CharacterManager.currentTarget 과 이중 동기화하여
        /// 타 클라이언트도 AI 의 공격 대상을 알 수 있게 합니다.
        /// </summary>
        public NetworkVariable<ulong> aggroTargetNetworkObjectID = new NetworkVariable<ulong>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// 탐지 시야(FOV) 이내에 플레이어가 있는지 서버가 매 틱 갱신합니다.
        /// 클라이언트는 이 값으로 경계음(Alert SFX) 재생 여부를 판단합니다.
        /// </summary>
        public NetworkVariable<bool> isAlerted = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [Header("AI — Phase (보스 페이즈)")]
        /// <summary>
        /// 보스 전용 페이즈 번호. 일반 몬스터는 항상 0.
        /// 페이즈 전환 시 클라이언트에서 연출(연기, 카메라 진동)을 발동합니다.
        /// </summary>
        public NetworkVariable<int> currentPhase = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [Header("AI — Spawn Info")]
        /// <summary>
        /// AI 가 스폰된 Cave 청크 좌표.
        /// CaveSpawnerManager 가 스폰 직후 설정하며, 리스폰·재배치 시 참조합니다.
        /// </summary>
        public NetworkVariable<Vector3> spawnChunkCenter = new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // =====================================================================
        // 내부 참조
        // =====================================================================
        private AICharacterManager _ai;

        // =====================================================================
        // 생명주기
        // =====================================================================
        protected override void Awake()
        {
            base.Awake();
            _ai = GetComponent<AICharacterManager>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // 어그로 타겟이 바뀌면 aiCharacterCombatManager.currentTarget 갱신
            aggroTargetNetworkObjectID.OnValueChanged += OnAggroTargetChanged;

            // 경계 상태가 바뀌면 시각·청각 반응 처리
            isAlerted.OnValueChanged += OnIsAlertedChanged;

            // 보스 페이즈 전환 시 연출 트리거
            currentPhase.OnValueChanged += OnPhaseChanged;
        }

        public override void OnNetworkDespawn()
        {
            aggroTargetNetworkObjectID.OnValueChanged -= OnAggroTargetChanged;
            isAlerted.OnValueChanged -= OnIsAlertedChanged;
            currentPhase.OnValueChanged -= OnPhaseChanged;

            base.OnNetworkDespawn();
        }

        // =====================================================================
        // OnValueChanged 핸들러
        // =====================================================================

        /// <summary>
        /// 어그로 타겟 NetworkObjectId 변경 → 로컬 currentTarget 참조 갱신.
        /// CharacterNetworkManager.OnLockOnTargetIDChange 와 동일한 패턴.
        /// </summary>
        private void OnAggroTargetChanged(ulong oldId, ulong newId)
        {
            if (_ai == null || _ai.aiCharacterCombatManager == null) return;

            if (newId == 0)
            {
                _ai.aiCharacterCombatManager.currentTarget = null;
                return;
            }

            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects
                .TryGetValue(newId, out NetworkObject netObj))
            {
                _ai.aiCharacterCombatManager.currentTarget =
                    netObj.GetComponent<CharacterManager>();
            }
        }

        /// <summary>
        /// 경계 상태 전환 — 경계음(Alert) 이벤트 발송.
        /// </summary>
        private void OnIsAlertedChanged(bool wasAlerted, bool nowAlerted)
        {
            if (_ai == null || _ai.characterEventManager == null) return;

            // 경계 진입 시 SFX 이벤트 발송 (CharacterSoundFxManager 가 수신)
            if (!wasAlerted && nowAlerted)
            {
                _ai.characterEventManager.NotifyAnimationEvent(
                    AnimationEventType.PlayVoice_Stagger, "AIAlert");
            }
        }

        /// <summary>
        /// 보스 페이즈 전환 — 클라이언트 연출 트리거.
        /// WorldCameraManager / CharacterEffectsManager 가 이벤트를 수신합니다.
        /// </summary>
        private void OnPhaseChanged(int oldPhase, int newPhase)
        {
            if (_ai == null || _ai.characterEventManager == null) return;

            // 페이즈 전환 이벤트 발송 (추후 AnimationEventType.BossPhaseTransition 추가 권장)
            Debug.Log($"<color=red>[AINetworkManager:{_ai.name}]</color> 페이즈 전환 {oldPhase} → {newPhase}");
        }

        // =====================================================================
        // AI 전용 공개 메서드 — 서버에서만 호출
        // =====================================================================

        /// <summary>
        /// 어그로 타겟을 설정합니다. IsServer 게이트 포함.
        /// AICharacterCombatManager 에서 currentTarget 변경 시 함께 호출합니다.
        /// </summary>
        public void SetAggroTarget(CharacterManager target)
        {
            if (!IsServer) return;
            aggroTargetNetworkObjectID.Value = target != null
                ? target.GetComponent<NetworkObject>().NetworkObjectId
                : 0;
        }

        /// <summary>
        /// 경계 상태를 설정합니다. AI FOV 탐지 루프에서 호출합니다.
        /// </summary>
        public void SetAlerted(bool alerted)
        {
            if (!IsServer) return;
            if (isAlerted.Value == alerted) return;
            isAlerted.Value = alerted;
        }

        /// <summary>
        /// 보스 페이즈를 전환합니다.
        /// </summary>
        public void SetPhase(int phase)
        {
            if (!IsServer) return;
            currentPhase.Value = phase;
        }

        // =====================================================================
        // 모션 워핑 RPC — AI 는 워핑 사용 안 함, 빈 override 로 무해화
        // =====================================================================
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public override void NotifyWarpAttackServerRpc(ulong targetId, int boneIndex)
        {
            // AI 는 모션 워핑을 사용하지 않으므로 의도적으로 비워둡니다.
        }

        [Rpc(SendTo.ClientsAndHost)]
        public override void NotifyWarpAttackClientRpc(ulong targetId, int boneIndex)
        {
            // AI 는 모션 워핑을 사용하지 않으므로 의도적으로 비워둡니다.
        }
    }
}