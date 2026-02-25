using System.Collections.Generic;
using UnityEngine;

// ==========================================
// 공용 데이터 구조 및 열거형 (전역 접근)
// ==========================================
public enum VoxelType { Solid, Air, Unbreakable }
public enum BiomeType { LiminalHalls, MineShafts, ConcreteBunker, DeepAbyss }
public enum FactionType { None, TheForgotten, AnomalySwarm, CultOfDepths }

public struct VoxelData
{
    public VoxelType Type;
    public BiomeType Biome;
    public FactionType Faction;
    public bool IsBase;
    public bool IsBossRoom;
}

public class RoomNode
{
    public Vector3Int position;
    public BiomeType biome;
    public FactionType faction = FactionType.None;
    public int radius;
    public bool isBase = false;
    public bool isBoss = false;
    public List<RoomNode> connectedRooms = new List<RoomNode>();
}