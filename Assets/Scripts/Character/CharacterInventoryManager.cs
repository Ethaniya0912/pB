using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 캐릭터 및 월드 객체 인벤토리의 핵심 동기화 및 격자 충돌 로직을 담당합니다. (Dev A)
/// NGO 2.0 [Rpc] 구문을 사용하여 서버 권한 로직을 처리합니다.
/// </summary>
public class CharacterInventoryManager : NetworkBehaviour, IInventoryHolder
{
    [Header("Manager References")]
    protected CharacterManager character;

    [Header("Weapon Slots (Legacy Logic)")]
    public WeaponItem currentRightHandWeapon;
    public WeaponItem currentLeftHandWeapon;

    public WeaponItem[] weaponInRightHandSlots = new WeaponItem[3];
    public int rightHandWeaponIndex = 0;
    public WeaponItem[] weaponInLeftHandSlots = new WeaponItem[3];
    public int leftHandWeaponIndex = 0;

    [Header("Grid Capacity")]
    public NetworkVariable<int> currentGridWidth = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> currentGridHeight = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    protected NetworkList<InventoryItem> inventoryItems;

    protected virtual void Awake()
    {
        character = GetComponent<CharacterManager>();
        inventoryItems = new NetworkList<InventoryItem>(null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    }

    #region Grid Logic (Collision & Space)

    public bool IsSpaceAvailable(int startX, int startY, int itemW, int itemH, InventoryItem? ignoreItem = null)
    {
        if (startX < 0 || startY < 0 || startX + itemW > currentGridWidth.Value || startY + itemH > currentGridHeight.Value)
            return false;

        foreach (var item in inventoryItems)
        {
            if (ignoreItem.HasValue && item.Equals(ignoreItem.Value)) continue;

            Item itemSO = WorldItemDatabase.Instance.GetItemByID(item.itemID);
            if (itemSO == null) continue;

            int otherW = (item.rotationAngle == 90 || item.rotationAngle == 270) ? itemSO.itemSizeHeight : itemSO.itemSizeWidth;
            int otherH = (item.rotationAngle == 90 || item.rotationAngle == 270) ? itemSO.itemSizeWidth : itemSO.itemSizeHeight;

            if (startX < item.gridPositionX + otherW && startX + itemW > item.gridPositionX && startY < item.gridPositionY + otherH && startY + itemH > item.gridPositionY)
            {
                return false;
            }
        }
        return true;
    }

    #endregion

    #region IInventoryHolder Implementation

    public NetworkList<InventoryItem> GetInventoryItems() => inventoryItems;
    public NetworkObject GetNetworkObject() => NetworkObject;
    public int GetGridWidth() => currentGridWidth.Value;
    public int GetGridHeight() => currentGridHeight.Value;

    public virtual void SaveInventory(ref List<InventoryItem> saveList)
    {
        saveList.Clear();
        foreach (var item in inventoryItems) saveList.Add(item);
    }

    public virtual void LoadInventory(List<InventoryItem> loadList)
    {
        if (!IsServer) return;
        inventoryItems.Clear();
        if (loadList != null) foreach (var item in loadList) inventoryItems.Add(item);
    }

    #endregion

    #region NGO 2.0 RPC Operations

    // 오류 해결을 위해 메서드 이름을 기존 스크립트들이 호출하는 ...ServerRpc 형태로 복구/유지합니다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void UpdateGridSizeServerRpc(int width, int height)
    {
        currentGridWidth.Value = width;
        currentGridHeight.Value = height;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestAddItemServerRpc(InventoryItem item)
    {
        inventoryItems.Add(item);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestRemoveItemServerRpc(InventoryItem item)
    {
        inventoryItems.Remove(item);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestMoveItemServerRpc(InventoryItem item, int newX, int newY)
    {
        for (int i = 0; i < inventoryItems.Count; i++)
        {
            if (inventoryItems[i].itemID == item.itemID &&
                inventoryItems[i].gridPositionX == item.gridPositionX &&
                inventoryItems[i].gridPositionY == item.gridPositionY)
            {
                var updated = inventoryItems[i];
                updated.gridPositionX = newX;
                updated.gridPositionY = newY;
                inventoryItems[i] = updated;
                break;
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestDropItemServerRpc(InventoryItem item, Vector3 dropPosition, Vector3 dropDirection)
    {
        if (!IsServer) return;

        bool removed = false;
        for (int i = 0; i < inventoryItems.Count; i++)
        {
            if (inventoryItems[i].itemID == item.itemID &&
                inventoryItems[i].gridPositionX == item.gridPositionX &&
                inventoryItems[i].gridPositionY == item.gridPositionY)
            {
                inventoryItems.RemoveAt(i);
                removed = true;
                break;
            }
        }

        if (removed)
        {
            WorldItemSpawner.Instance.SpawnItemInWorld(item.itemID, item.stackCount, dropPosition, dropDirection);
        }
    }

    #endregion
}