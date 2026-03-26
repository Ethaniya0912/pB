// =============================================================================
// AICharacterInventoryManager.cs  |  TDA Project
// Layer  : L3 Domain — AI 전용 인벤토리/무장 매니저
//
// 역할:
//   CharacterInventoryManager 를 상속하여 AI(몬스터/NPC)에 필요한
//   무장(Loadout) 초기화와 사망 시 아이템 드롭을 처리합니다.
//
// 플레이어와의 차이:
//   - 격자 인벤토리(Grid) 불필요 — gridWidth/Height = 0 으로 고정
//   - 아이템 추가/이동/회전 RPC 불필요 (플레이어 전용 UI 없음)
//   - 스폰 시 인스펙터 설정 Loadout 을 서버에서 자동 장착
//   - 사망 시 드롭 아이템 목록을 서버에서 WorldItemSpawner 에 위임
//   - 무기 슬롯 고정 (Right 1개, Left 0개 기본) — AI 는 무기 교체 없음
//
// 아키텍처 규약:
//   ① 무장 초기화는 OnNetworkSpawn(IsServer) 에서만 수행
//   ② 드롭 로직은 서버에서 WorldItemSpawner.SpawnItemInWorld 에 위임
//   ③ GridRPC 는 완전히 비워 두어 불필요한 패킷 차단
// =============================================================================
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace TDA.Character.AI
{
    /// <summary>
    /// [L3 Domain] AI(몬스터/NPC) 전용 인벤토리 매니저.
    /// 스폰 시 Loadout 자동 장착, 사망 시 드롭 처리를 담당합니다.
    /// </summary>
    public class AICharacterInventoryManager : CharacterInventoryManager
    {
        // =====================================================================
        // AI 전용 인스펙터 설정
        // =====================================================================
        [Header("AI Loadout (스폰 시 자동 장착)")]
        [Tooltip("AI 가 스폰 시 오른손에 장착할 무기 SO.\n" +
                 "null 이면 맨손 상태로 스폰됩니다.")]
        public WeaponItem defaultRightHandWeapon;

        [Tooltip("AI 가 스폰 시 왼손에 장착할 무기 SO (방어용).\n" +
                 "null 이면 왼손 빈 상태.")]
        public WeaponItem defaultLeftHandWeapon;

        [Header("사망 시 드롭 테이블")]
        [Tooltip("AI 사망 시 드롭할 아이템 목록.\n" +
                 "각 항목의 dropChance(0~1)에 따라 확률 드롭됩니다.")]
        public List<DropEntry> dropTable = new List<DropEntry>();

        [Tooltip("드롭 아이템의 최소 발사 거리 (m)")]
        [SerializeField] private float dropScatterRadius = 0.6f;

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

            if (!IsServer) return;

            // 격자 인벤토리 크기 0 으로 고정 (AI 는 Grid UI 불필요)
            currentGridWidth.Value = 0;
            currentGridHeight.Value = 0;

            // Loadout 자동 장착
            InitializeAILoadout();

            // 사망 이벤트 구독 (드롭 처리)
            if (_ai != null && _ai.characterNetworkManager != null)
                _ai.characterNetworkManager.isDead.OnValueChanged += OnDeadChangedServer;
        }

        public override void OnNetworkDespawn()
        {
            if (_ai != null && _ai.characterNetworkManager != null)
                _ai.characterNetworkManager.isDead.OnValueChanged -= OnDeadChangedServer;

            base.OnNetworkDespawn();
        }

        // =====================================================================
        // Loadout 초기화
        // =====================================================================

        /// <summary>
        /// 인스펙터에 설정된 무기를 무기 슬롯에 넣고 AICharacterEquipmentManager 에 장착 요청합니다.
        /// IsServer 에서만 호출합니다.
        /// </summary>
        private void InitializeAILoadout()
        {
            if (defaultRightHandWeapon != null)
            {
                currentRightHandWeapon = defaultRightHandWeapon;
                weaponInRightHandSlots[0] = defaultRightHandWeapon;
            }

            if (defaultLeftHandWeapon != null)
            {
                currentLeftHandWeapon = defaultLeftHandWeapon;
                weaponInLeftHandSlots[0] = defaultLeftHandWeapon;
            }

            // AICharacterEquipmentManager 에 장착 요청
            var equip = GetComponent<AICharacterEquipmentManager>();
            if (equip != null)
                equip.LoadWeaponOnBothHands();
        }

        // =====================================================================
        // 사망 드롭 처리
        // =====================================================================

        private void OnDeadChangedServer(bool was, bool isNow)
        {
            if (!isNow) return;
            ProcessDeathDrop();
        }

        /// <summary>
        /// 드롭 테이블을 순회하며 확률에 따라 아이템을 월드에 스폰합니다.
        /// WorldItemSpawner.SpawnItemInWorld 에 위임합니다.
        /// </summary>
        private void ProcessDeathDrop()
        {
            if (WorldItemSpawner.Instance == null) return;

            foreach (var entry in dropTable)
            {
                if (entry.itemID <= 0) continue;
                if (Random.value > entry.dropChance) continue;

                // 약간 무작위 방향으로 흩어지게 드롭
                Vector3 scatter = Random.insideUnitSphere * dropScatterRadius;
                scatter.y = Mathf.Abs(scatter.y); // 위쪽 방향만
                Vector3 dropPos = transform.position + Vector3.up * 0.5f + scatter;
                Vector3 dropDir = scatter.sqrMagnitude > 0.001f
                    ? scatter.normalized
                    : transform.forward;

                WorldItemSpawner.Instance.SpawnItemInWorld(
                    entry.itemID, entry.stackCount, dropPos, dropDir);
            }
        }

        // =====================================================================
        // Grid RPC 무해화 — AI 는 격자 인벤토리 조작 불필요
        // CharacterInventoryManager 의 RPC 는 virtual 이 아니므로
        // 'new' 키워드로 숨김(hiding) 처리합니다.
        // NGO RPC 는 런타임에 컴포넌트 타입으로 디스패치되므로
        // AICharacterInventoryManager 컴포넌트가 부착된 AI 에서 호출 시
        // 아래 빈 구현이 실행됩니다.
        // =====================================================================
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public new void UpdateGridSizeServerRpc(int width, int height)
        {
            // AI 는 격자 크기 변경 불필요 — 의도적으로 비워둡니다.
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public new void RequestAddItemServerRpc(InventoryItem item)
        {
            // AI 인벤토리에 외부에서 아이템 추가 금지.
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public new void RequestRemoveItemServerRpc(InventoryItem item)
        {
            // AI 인벤토리에서 외부 아이템 제거 금지.
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public new void RequestMoveItemServerRpc(InventoryItem item, int newX, int newY)
        {
            // AI 는 격자 이동 불필요.
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public new void RequestDropItemServerRpc(InventoryItem item, Vector3 dropPosition, Vector3 dropDirection)
        {
            // AI 아이템 드롭은 ProcessDeathDrop() 에서 서버 직접 처리.
        }
    }

    // =========================================================================
    // 드롭 테이블 항목 구조체
    // =========================================================================
    [System.Serializable]
    public struct DropEntry
    {
        [Tooltip("드롭할 아이템 ID (WorldItemDatabase 기준)")]
        public int itemID;

        [Tooltip("드롭 스택 수")]
        public int stackCount;

        [Tooltip("드롭 확률 0~1 (1 = 항상 드롭)")]
        [Range(0f, 1f)]
        public float dropChance;
    }
}