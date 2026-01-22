using Unity.Netcode;
using UnityEngine;


/// <summary>
/// 인벤토리 격자 내 아이템의 상태를 정의하는 구조체입니다. (Dev A)
/// NGO 2.0 NetworkList 동기화를 위해 INetworkSerializable을 구현합니다.
/// </summary>
[System.Serializable]
public struct InventoryItem : INetworkSerializable, System.IEquatable<InventoryItem>
{
    public int itemID;          // WorldItemDatabase 참조 ID
    public int gridPositionX;   // 격자 내 좌측 상단 X 좌표
    public int gridPositionY;   // 격자 내 좌측 상단 Y 좌표
    public float rotationAngle; // 아이템의 회전 각도 (0, 90 등)
    public int stackCount;      // 중첩 개수

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref itemID);
        serializer.SerializeValue(ref gridPositionX);
        serializer.SerializeValue(ref gridPositionY);
        serializer.SerializeValue(ref rotationAngle);
        serializer.SerializeValue(ref stackCount);
    }

    public bool Equals(InventoryItem other)
    {
        return itemID == other.itemID &&
                gridPositionX == other.gridPositionX &&
                gridPositionY == other.gridPositionY &&
                rotationAngle == other.rotationAngle &&
                stackCount == other.stackCount;
    }

    public override bool Equals(object obj) => obj is InventoryItem other && Equals(other);
    public override int GetHashCode() => System.HashCode.Combine(itemID, gridPositionX, gridPositionY, rotationAngle, stackCount);
}