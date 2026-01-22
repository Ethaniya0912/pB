using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
/// <summary>
/// 기존 Save 시스템 구조를 유지하며 인벤토리 및 장착 데이터를 추가한 시리얼라이즈 클래스입니다.
/// </summary>
public class CharacterSaveData
{
    [Header("Scene Index")]
    public int sceneIndex = 1;

    [Header("Character Info")]
    public string characterName = "Character";
    public float secondsPlayed;

    [Header("World Coordinates")]
    public float xPosition;
    public float yPosition;
    public float zPosition;

    [Header("Resources")]
    public int currentHealth;
    public float currentStamina;

    [Header("Stats")]
    public int vitality;
    public int endurance;

    [Header("Inventory Data")]
    // 가방 내부에 위치한 아이템 정보 리스트
    public List<InventoryItem> inventoryItems;

    [Header("Equipment Data")]
    // 부위별 장착 아이템 ID (-1: 미장착)
    public int currentRightHandWeaponID = -1;
    public int currentLeftHandWeaponID = -1;
    public int currentHelmetID = -1;
    public int currentChestArmorID = -1;
    public int currentPantsID = -1;
    public int currentLeggingsID = -1;
    public int currentBackpackID = -1;

    public CharacterSaveData()
    {
        inventoryItems = new List<InventoryItem>();
    }
}