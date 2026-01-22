using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Items/Item")]
public class Item : ScriptableObject
{
    [Header("Item Information")]
    public Sprite itemIcon;
    public string itemName;
    public int itemID;
    public GameObject itemModel;

    [Header("Grid Settings")]
    // 인벤토리 격자에서 차지하는 칸 수 (오류 해결을 위해 추가)
    public int itemSizeWidth = 1;
    public int itemSizeHeight = 1;

    [TextArea]
    public string itemDescription;
}