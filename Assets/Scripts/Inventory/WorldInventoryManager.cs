using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;


/// <summary>
/// 월드에 존재하는 상자, 시체 등의 인벤토리를 관리합니다. (Dev B)
/// 각 객체는 고유한 GUID를 통해 세이브 데이터에서 자신의 아이템 목록을 식별합니다.
/// </summary>
public class WorldInventoryManager : CharacterInventoryManager
{
    [Header("Persistence")]
    // 세이브 데이터에서 이 인벤토리를 식별하기 위한 고유 ID
    public string objectGuid;

    [Header("Initial Setup")]
    [SerializeField] private int initialWidth = 5;
    [SerializeField] private int initialHeight = 5;
    [SerializeField] private List<InventoryItem> startingItems = new List<InventoryItem>();

    protected override void Awake()
    {
        base.Awake();

        // GUID가 비어있다면 새로 생성 (에디터에서 미리 생성해두는 것을 권장)
        if (string.IsNullOrEmpty(objectGuid))
        {
            objectGuid = System.Guid.NewGuid().ToString();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // 서버에서 초기 격자 크기 설정
            currentGridWidth.Value = initialWidth;
            currentGridHeight.Value = initialHeight;

            // 세이브 데이터가 있다면 로드하고, 없다면 초기 아이템 생성
            LoadOrInitializeInventory();
        }
    }

    private void LoadOrInitializeInventory()
    {
        // TODO: WorldSaveGameManager를 통해 해당 GUID의 데이터가 있는지 확인
        // 데이터가 없다면 기본 아이템 배치
        if (startingItems != null && startingItems.Count > 0)
        {
            foreach (var item in startingItems)
            {
                inventoryItems.Add(item);
            }
        }
    }

    #region Saving Logic

    /// <summary>
    /// 월드 세이브 시스템에 현재 아이템 목록을 전달합니다.
    /// </summary>
    public void SyncToSaveData(ref List<InventoryItem> worldItemsList)
    {
        worldItemsList.Clear();
        foreach (var item in inventoryItems)
        {
            worldItemsList.Add(item);
        }
    }

    #endregion

    /// <summary>
    /// 상호작용 시 루팅 UI에 이 인벤토리의 정보를 전달하기 위해 사용됩니다.
    /// </summary>
    public IInventoryHolder GetInventoryHolder()
    {
        return this;
    }
}
