using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 플레이어 전용 인벤토리 매니저입니다. (Dev B 최종 보완본)
/// 가방 장착 여부 확인, 물리적인 가방 여닫기 시퀀스, 
/// 그리고 가방을 내용물과 함께 월드에 벗어던지는 기능을 관리합니다.
/// </summary>
public class PlayerInventoryManager : CharacterInventoryManager
{
    private PlayerManager player;

    [Header("Initial Setup")]
    // 테스트 및 초기 시작 시 인벤토리에 들어있을 아이템 리스트
    [SerializeField] private List<InventoryItem> startingItems = new List<InventoryItem>();

    [Header("Inventory State")]
    public bool isInventoryOpen = false;
    private bool isInteractingWithBag = false;

    [Header("Visual Controllers")]
    [SerializeField] private BagVisualController bagVisualController;
    [SerializeField] private Transform bagStayPivot;

    [Header("Looting System")]
    // 현재 상호작용 중인 월드 인벤토리(상자 등) 컨테이너 참조
    public LootableInteractable currentLootContainer;

    // 인벤토리 오픈 시 카메라가 바라볼 피벗
    public Transform inventoryCameraPivot;

    protected override void Awake()
    {
        base.Awake();
        player = GetComponent<PlayerManager>();

        Debug.Log($"<color=white>[Inventory] PlayerInventoryManager 초기화 완료. Player: {player.gameObject.name}</color>");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 서버에서 초기 아이템 할당 처리 (테스트 및 초기 보급용)
        if (IsServer && startingItems != null)
        {
            foreach (var item in startingItems)
            {
                inventoryItems.Add(item);
            }
        }
    }

    private void Update()
    {
        if (!IsOwner) return;
        HandleAutoCloseOnMovement();
    }

    /// <summary>
    /// 플레이어의 이동량이 일정 수준 이상이면 열려있는 인벤토리나 루팅 창을 자동으로 닫습니다.
    /// </summary>
    private void HandleAutoCloseOnMovement()
    {
        if (isInventoryOpen && !isInteractingWithBag)
        {
            if (PlayerInputManager.Instance.moveAmount > 0.1f)
            {
                // [보완] 현재 루팅 중인 상자가 있다면 상자 닫기 시퀀스 실행
                if (currentLootContainer != null)
                {
                    Debug.Log("<color=orange>[Inventory] 이동 감지: 루팅 중인 상자를 닫습니다.</color>");
                    currentLootContainer.CloseLootContainer();
                    currentLootContainer = null;
                    isInventoryOpen = false; // 상태 해제 (커서 잠금 트리거)
                }
                else
                {
                    // 일반 인벤토리(자신의 가방)가 열려있다면 토글하여 닫기
                    ToggleInventory();
                }
            }
        }
    }

    /// <summary>
    /// 캐릭터의 등에 부착된 가방 모델의 활성화 여부를 제어합니다.
    /// 모델에 콜라이더가 없는 경우 자동으로 BoxCollider를 생성하며, SO의 스케일을 강제 적용합니다.
    /// </summary>
    /// <param name="visible">모델 표시 여부</param>
    public void RefreshBackpackModelVisibility(bool visible)
    {
        if (player.playerEquipmentManager == null) return;

        // PlayerEquipmentManager의 backpackSlot 하위의 실제 모델 탐색
        Transform anchor = player.playerEquipmentManager.backpackSlot;

        if (anchor == null)
        {
            Debug.LogWarning("[Inventory] 가방 슬롯(backpackSlot) 참조가 비어있습니다.");
            return;
        }

        // 현재 가방 ID를 통해 SO 데이터 획득
        int currentID = player.playerEquipmentManager.currentBackpackID.Value;
        EquipmentItem bagData = WorldItemDatabase.Instance.GetItemByID(currentID) as EquipmentItem;

        bool found = false;
        foreach (Transform child in anchor)
        {
            if (child == null) continue;

            // 모델 이름 규칙(Backpack/Bag)에 따라 시각적 활성화 제어
            if (child.name.ToLower().Contains("backpack") || child.name.ToLower().Contains("bag"))
            {
                // [보완] SO에 설정된 스케일 강제 적용 (장착 상태에서도 일관된 크기 유지)
                if (bagData != null)
                {
                    child.localScale = bagData.backpackModelScale;
                }

                // [보완] 콜라이더 자동 추가 로직: 드래그 레이캐스트 인식을 위해 필수입니다.
                Collider col = child.GetComponent<Collider>();
                if (col == null)
                {
                    Renderer targetRenderer = child.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (targetRenderer == null) targetRenderer = child.GetComponentInChildren<MeshRenderer>();

                    BoxCollider newCol = child.gameObject.AddComponent<BoxCollider>();
                    if (targetRenderer != null)
                    {
                        // 렌더러의 로컬 바운드를 기준으로 콜라이더 크기 설정
                        newCol.center = targetRenderer.localBounds.center;
                        newCol.size = targetRenderer.localBounds.size;
                    }
                    else
                    {
                        newCol.size = new Vector3(0.5f, 0.5f, 0.5f);
                    }
                    Debug.Log($"<color=green>[Inventory] 가방 '{child.name}'에 BoxCollider를 자동으로 생성했습니다.</color>");
                }

                if (child.gameObject != null)
                {
                    child.gameObject.SetActive(visible);
                    found = true;
                }
            }
        }

        if (!found && visible) Debug.LogWarning("[Inventory] 제어할 가방 모델을 슬롯 하위에서 찾지 못했습니다.");
    }

    /// <summary>
    /// 현재 장착 중인 가방을 내용물(아이템 목록)과 함께 캐릭터 앞에 벗어던집니다. (Alt+드래그 결과)
    /// 드래그 도중에는 등에 유지되며, 드롭 시 애니메이션 재생 후 지연 스폰됩니다.
    /// </summary>
    public void UnEquipAndDropBackpack()
    {
        if (!IsOwner) return;

        // 지연 처리를 위해 코루틴 시퀀스로 실행 (애니메이션 연출용)
        StartCoroutine(UnEquipAndDropRoutine());
    }

    /// <summary>
    /// 애니메이션 재생 후 가방을 실제로 월드에 스폰하는 코루틴 시퀀스입니다.
    /// </summary>
    private IEnumerator UnEquipAndDropRoutine()
    {
        Debug.Log("<color=red>[Inventory] 가방 벗어던지기 시퀀스 시작</color>");

        // 1. 가방 ID 확인 (데이터 불일치 방지를 위해 EquipmentManager 우선 참조)
        int bagID = player.playerEquipmentManager.currentBackpackID.Value;
        if (bagID == -1) bagID = player.playerNetworkManager.currentBackpackID.Value;

        if (bagID <= 0)
        {
            Debug.LogWarning("[Inventory] 장착된 가방이 없어 벗어던질 수 없습니다.");
            RefreshBackpackModelVisibility(true);
            yield break;
        }

        // 2. 가방을 버리는 애니메이션 재생 요청 (서버 RPC)
        // 캐릭터가 가방을 던지는 동작인 "Throw_Item" 애니메이션을 실행합니다.
        player.playerNetworkManager.NotifyTheServerOfActionAnimationServerRpc(
            NetworkManager.Singleton.LocalClientId, "Throw_Item", true);

        // 3. 애니메이션 동작 대기 (요구사항: 1초 후 스폰)
        Debug.Log("[Inventory] 버리는 애니메이션 재생 중... 1초 대기 후 월드 스폰");
        yield return new WaitForSeconds(1.0f);

        // 4. 데이터 추출 및 내용물 보존
        List<InventoryItem> itemsToInside = new List<InventoryItem>();
        foreach (var item in inventoryItems)
        {
            itemsToInside.Add(item);
        }

        // 5. 서버에 가방 스폰 및 내용물 전달 요청
        // 스폰 위치 보정: 캐릭터 앞쪽 낮은 위치 (공중 부양 방지)
        Vector3 spawnPos = player.transform.position + player.transform.forward * 0.6f + Vector3.up * 0.1f;
        Vector3 throwDir = player.transform.forward * 2.0f + Vector3.up * 0.5f;

        // [중요] 비주얼을 끄기 전 서버에 스폰을 먼저 요청하여 데이터의 연속성을 보장합니다.
        DropBackpackServerRpc(bagID, itemsToInside.ToArray(), spawnPos, throwDir);

        // 6. 비주얼 비활성화 (장착된 모델 제거 전 시각적 정리)
        RefreshBackpackModelVisibility(false);

        // 7. 로컬 및 네트워크 장착 상태 해제
        player.playerEquipmentManager.currentBackpackID.Value = -1;
        player.playerNetworkManager.currentBackpackID.Value = -1;

        // 8. 격자 크기 리셋 및 리스트 비우기
        UpdateGridSizeServerRpc(0, 0);
        inventoryItems.Clear();

        Debug.Log("<color=red>[Inventory] 가방 장착 해제 완료 및 지연 스폰 요청 완료.</color>");
    }

    /// <summary>
    /// 서버 측에서 월드에 가방 아이템을 물리적으로 생성하고 내용물 리스트를 주입합니다.
    /// </summary>
    [ServerRpc]
    private void DropBackpackServerRpc(int bagID, InventoryItem[] items, Vector3 pos, Vector3 dir)
    {
        Debug.Log($"<color=orange>[Server] 가방 스폰 요청 수신. BagID: {bagID}, 아이템 수: {items.Length}</color>");

        List<InventoryItem> itemList = new List<InventoryItem>(items);

        if (WorldItemSpawner.Instance != null)
        {
            // 내용물이 채워진 가방을 월드에 물리 객체로 생성
            WorldItemSpawner.Instance.SpawnFilledBackpack(bagID, itemList, pos, dir);
            Debug.Log($"<color=orange>[Server] 가방 스폰 성공 (위치: {pos})</color>");
        }
        else
        {
            Debug.LogError("[Server] WorldItemSpawner.Instance를 찾을 수 없습니다!");
        }

        // 서버 측 캐릭터 인벤토리 비우기 (동기화)
        inventoryItems.Clear();
    }

    /// <summary>
    /// 인벤토리 상태를 토글합니다. 가방을 장착하지 않은 상태에서는 작동하지 않습니다.
    /// </summary>
    public void ToggleInventory()
    {
        // [보완] 만약 상자가 열려 있다면 상자를 먼저 닫습니다.
        if (currentLootContainer != null)
        {
            currentLootContainer.CloseLootContainer();
            currentLootContainer = null;
            isInventoryOpen = false;
            // 상자만 닫고 싶을 때 I를 누르는 경우라면 여기서 리턴 가능
            // return; 
        }

        int currentID = player.playerEquipmentManager.currentBackpackID.Value;
        if (currentID == -1) currentID = player.playerNetworkManager.currentBackpackID.Value;

        if (currentID == -1)
        {
            Debug.LogWarning("가방을 장착하고 있지 않아 인벤토리를 열 수 없습니다.");
            return;
        }

        if (isInventoryOpen)
            CloseInventorySequence();
        else
            OpenInventorySequence();
    }

    #region Bag Interaction Sequence

    private void OpenInventorySequence()
    {
        if (isInteractingWithBag || isInventoryOpen) return;

        Debug.Log("<color=orange>[Inventory] Open Sequence 시작</color>");
        isInventoryOpen = true;
        isInteractingWithBag = true;

        if (PlayerCamera.Instance != null)
            PlayerCamera.Instance.ToggleInventoryCamera(true);

        player.playerNetworkManager.NotifyTheServerOfActionAnimationServerRpc(
            NetworkManager.Singleton.LocalClientId, "Put_Bag_Down", true);

        if (bagVisualController != null)
        {
            if (bagStayPivot == null) Debug.LogError("BagStayPivot이 할당되지 않았습니다!");
            bagVisualController.SetBagToWorldPivot(bagStayPivot, true);
        }

        isInteractingWithBag = false;
    }

    private void CloseInventorySequence()
    {
        if (isInteractingWithBag || !isInventoryOpen) return;

        Debug.Log("<color=orange>[Inventory] Close Sequence 시작</color>");
        isInventoryOpen = false;
        isInteractingWithBag = true;

        player.playerNetworkManager.NotifyTheServerOfActionAnimationServerRpc(
            NetworkManager.Singleton.LocalClientId, "Pick_Up_Bag", true);

        if (PlayerCamera.Instance != null)
            PlayerCamera.Instance.ToggleInventoryCamera(false);

        if (bagVisualController != null)
            bagVisualController.SetBagToSpine();

        // 루팅 중인 상자가 있었다면 참조 해제 (안전 장치)
        currentLootContainer = null;

        isInteractingWithBag = false;
    }

    #endregion

    #region Drag & Drop Interaction Logic

    public void OnItemDroppedOnCharacter(InventoryItem item, EquipmentSlot slot)
    {
        Debug.Log($"<color=white>[Inventory] 장착 드롭 감지: ItemID {item.itemID} -> Slot {slot}</color>");
        UpdateEquipmentNetworkState(item.itemID, slot);
        RequestRemoveItemServerRpc(item);
    }

    private void UpdateEquipmentNetworkState(int itemID, EquipmentSlot slot)
    {
        var net = player.playerNetworkManager;
        switch (slot)
        {
            case EquipmentSlot.RightHand: net.currentRightHandWeaponID.Value = itemID; break;
            case EquipmentSlot.Helmet: net.currentHelmetID.Value = itemID; break;
            case EquipmentSlot.Backpack:
                player.playerEquipmentManager.currentBackpackID.Value = itemID;
                net.currentBackpackID.Value = itemID;
                break;
        }
    }

    public void OnItemDroppedInWorld(InventoryItem item)
    {
        if (!IsOwner) return;

        Debug.Log($"<color=white>[Inventory] 아이템 월드 버리기 감지: ItemID {item.itemID}</color>");
        Vector3 spawnPos = player.transform.position + player.transform.forward * 0.5f + Vector3.up * 1.5f;
        Vector3 throwDir = player.transform.forward * 2.5f + Vector3.up * 0.5f;

        RequestDropItemServerRpc(item, spawnPos, throwDir);
    }

    #endregion
}