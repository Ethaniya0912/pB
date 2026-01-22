using System.Collections.Generic;
using Unity.Netcode;


/// <summary>
/// 인벤토리를 소유할 수 있는 모든 객체(플레이어, 상자, 시체)의 공용 인터페이스입니다.
/// </summary>
public interface IInventoryHolder
{
    NetworkList<InventoryItem> GetInventoryItems();
    NetworkObject GetNetworkObject();
    int GetGridWidth();
    int GetGridHeight();
    void SaveInventory(ref List<InventoryItem> saveList);
    void LoadInventory(List<InventoryItem> loadList);
}
