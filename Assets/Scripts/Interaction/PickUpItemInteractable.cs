using System;
using System.Collections.Generic;
using SG;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 월드 상에 존재하는 모든 획득 가능한 아이템 객체를 관리합니다.
/// 아이템 데이터베이스(SO)의 설정을 바탕으로 시각화 및 물리 충돌체(Collider)를 자동으로 구성합니다.
/// 가방 아이템의 경우 내부에 담긴 아이템 목록(containedItems)을 보존하여 획득 시 전달합니다.
/// </summary>
public class PickUpItemInteractable : InteractableObject
{
    [Header("Item Status")]
    // 에디터에서 직접 배치할 때 사용할 초기 ID
    [SerializeField] private int initialItemID = -1;

    // 네트워크를 통해 모든 클라이언트에 동기화되는 아이템 정보
    public NetworkVariable<int> itemID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> stackCount = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Visuals")]
    [Tooltip("아이템 모델이 생성되어 배치될 부모 오브젝트입니다.")]
    [SerializeField] private Transform modelContainer;
    private GameObject currentModel;

    [Header("Contained Items (For Backpacks)")]
    [Tooltip("이 아이템이 가방일 경우, 내부에 들어있는 아이템 데이터 리스트입니다.")]
    public List<InventoryItem> containedItems = new List<InventoryItem>();

    protected virtual void Awake()
    {
        // Model Container가 지정되지 않았다면 이 스크립트가 붙은 오브젝트 자체를 부모로 사용합니다.
        if (modelContainer == null) modelContainer = transform;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 서버에서 초기 ID가 설정되어 있다면 네트워크 변수에 즉시 적용
        if (IsServer)
        {
            if (initialItemID != -1)
            {
                itemID.Value = initialItemID;
                Debug.Log($"<color=cyan>[PickUp] 서버에서 초기 ID 설정됨: {gameObject.name} -> ID {itemID.Value}</color>");
            }
        }

        // 아이템 ID가 변경될 때마다 모델을 다시 그립니다.
        itemID.OnValueChanged += (oldID, newID) => UpdateVisual(newID);

        // 초기 동기화 시점에 이미 ID가 있다면 비주얼 업데이트 수행
        if (itemID.Value != -1)
        {
            UpdateVisual(itemID.Value);
        }
    }

    /// <summary>
    /// 외부(WorldItemSpawner 등)에서 스폰된 아이템의 데이터를 설정합니다.
    /// </summary>
    public void SetItemData(int id, int count, List<InventoryItem> items = null)
    {
        if (!IsServer) return;

        itemID.Value = id;
        stackCount.Value = count;

        if (items != null)
        {
            containedItems = new List<InventoryItem>(items);
            Debug.Log($"[PickUp] 가방 내용물 데이터 주입 완료: {containedItems.Count}개");
        }
    }

    /// <summary>
    /// 아이템 ID에 해당하는 3D 모델을 생성하고, SO에 설정된 스케일, 각도 및 물리 충돌체를 자동으로 구성합니다.
    /// </summary>
    private void UpdateVisual(int id)
    {
        // 1. 기존 모델 제거
        if (currentModel != null) Destroy(currentModel);

        // 2. 데이터베이스에서 아이템 정보 검색
        Item data = WorldItemDatabase.Instance.GetItemByID(id);
        if (data != null && data.itemModel != null)
        {
            // 3. 모델 생성 및 초기화
            currentModel = Instantiate(data.itemModel, modelContainer);
            currentModel.transform.localPosition = Vector3.zero;

            // [보완] SO(EquipmentItem)에 정의된 각도와 스케일 값을 월드 모델에 강제 적용
            if (data is EquipmentItem equip)
            {
                // 가방이 바닥에 놓일 때의 회전값 적용 (인벤토리 설정과 일치)
                currentModel.transform.localRotation = Quaternion.Euler(equip.backpackLyingRotation);
                // 가방의 의도된 스케일 적용 (0.15 등)
                currentModel.transform.localScale = equip.backpackModelScale;
                Debug.Log($"<color=white>[PickUpVisual] '{data.itemName}' SO 데이터 적용 (각도: {equip.backpackLyingRotation}, 스케일: {equip.backpackModelScale})</color>");
            }
            else
            {
                currentModel.transform.localRotation = Quaternion.identity;
                currentModel.transform.localScale = Vector3.one;
            }

            // 4. Collider 자동 생성 및 메쉬 맞춤 보정 (0.9 고정값 제거)
            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null)
            {
                box = gameObject.AddComponent<BoxCollider>();
                Debug.Log($"<color=green>[PickUp] '{gameObject.name}'에 BoxCollider를 자동으로 추가했습니다.</color>");
            }

            // 생성된 모델의 실제 메쉬 영역을 계산하여 콜라이더 크기를 맞춥니다.
            Renderer modelRenderer = currentModel.GetComponentInChildren<SkinnedMeshRenderer>();
            if (modelRenderer == null) modelRenderer = currentModel.GetComponentInChildren<MeshRenderer>();

            if (modelRenderer != null)
            {
                // 모델의 실제 월드 바운드를 로컬 영역으로 변환하여 콜라이더 센터 계산
                box.center = transform.InverseTransformPoint(modelRenderer.bounds.center);

                // [수정] 모델의 스케일을 고려하여 콜라이더의 로컬 Size를 메쉬와 1:1로 일치시킴
                // 고정된 0.9f를 제거하고 실제 메쉬 크기(worldSize)를 오브젝트 스케일로 나누어 계산합니다.
                Vector3 worldSize = modelRenderer.bounds.size;
                box.size = new Vector3(
                    worldSize.x / Mathf.Max(transform.lossyScale.x, 0.001f),
                    worldSize.y / Mathf.Max(transform.lossyScale.y, 0.001f),
                    worldSize.z / Mathf.Max(transform.lossyScale.z, 0.001f)
                );

                Debug.Log($"<color=green>[PickUp] 콜라이더 메쉬 맞춤 완료. Size: {box.size}</color>");
            }

            // 5. 레이어 및 NGO 정리
            currentModel.layer = gameObject.layer;
            NetworkObject no = currentModel.GetComponent<NetworkObject>();
            if (no != null) DestroyImmediate(no); // 비주얼 모델의 불필요한 네트워크 컴포넌트 제거

            foreach (Transform t in currentModel.GetComponentsInChildren<Transform>())
                t.gameObject.layer = gameObject.layer;
        }
        else
        {
            Debug.LogWarning($"[PickUp] ID {id}번에 해당하는 모델을 찾을 수 없습니다.");
        }
    }

    /// <summary>
    /// 플레이어와 상호작용 시 호출됩니다. 가방 장착 또는 인벤토리 아이템 추가를 처리합니다.
    /// </summary>
    public override void Interact(CharacterManager character)
    {
        // 상호작용 및 데이터 변경은 서버에서만 처리합니다.
        if (!IsServer) return;

        PlayerManager player = character as PlayerManager;
        if (player == null) return;

        Item itemData = WorldItemDatabase.Instance.GetItemByID(itemID.Value);
        if (itemData == null) return;

        Debug.Log($"[PickUp] 상호작용 시도 중인 아이템: {itemData.itemName}");

        // 1. 가방 아이템 상호작용
        if (itemData is EquipmentItem equip && equip.isBackpack)
        {
            // 장착 업데이트를 담당하는 EquipmentManager의 ID를 직접 확인
            if (player.playerEquipmentManager.currentBackpackID.Value != -1)
            {
                Debug.LogWarning("[PickUp] 이미 가방을 장착하고 있습니다.");
                return;
            }

            // EquipmentManager와 NetworkManager 양쪽의 ID를 모두 업데이트하여 동기화 보장
            player.playerEquipmentManager.currentBackpackID.Value = itemID.Value;
            player.playerNetworkManager.currentBackpackID.Value = itemID.Value;

            // 가방 크기에 맞게 인벤토리 격자 용량 업데이트
            player.playerInventoryManager.UpdateGridSizeServerRpc(equip.backpackWidth, equip.backpackHeight);

            // [핵심] 가방 내부에 담겨있던 아이템들을 플레이어 인벤토리로 완벽히 복구
            foreach (var item in containedItems)
            {
                player.playerInventoryManager.RequestAddItemServerRpc(item);
            }

            Debug.Log($"<color=green>[PickUp] 가방 '{itemData.itemName}' 장착 완료. 내부 아이템 {containedItems.Count}개 복구됨.</color>");
            FinalizePickup();
            return;
        }

        // 2. 일반 아이템 상호작용 (가방 미장착 시 획득 불가)
        if (player.playerEquipmentManager.currentBackpackID.Value == -1)
        {
            Debug.LogWarning("[PickUp] 아이템을 담을 가방이 없습니다.");
            return;
        }

        InventoryItem newItem = new InventoryItem { itemID = itemID.Value, stackCount = stackCount.Value };
        player.playerInventoryManager.RequestAddItemServerRpc(newItem);

        Debug.Log($"[PickUp] 아이템 '{itemData.itemName}' 획득 완료.");
        FinalizePickup();
    }

    /// <summary>
    /// 획득이 완료된 월드 오브젝트를 서버에서 제거합니다.
    /// </summary>
    private void FinalizePickup()
    {
        if (!IsServer) return;

        var networkObj = GetComponent<NetworkObject>();
        if (networkObj != null && networkObj.IsSpawned)
        {
            Debug.Log($"[Server] {gameObject.name} Despawn 호출.");
            networkObj.Despawn();
        }
        else
        {
            Destroy(gameObject);
        }
    }
}