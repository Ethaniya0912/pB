using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
// 해당 데이터를 모든 세이브 파일에서 레퍼하려하기 떄문에, 모노비헤이비어가 아닌 Serealizable로.
public class CharacterSaveData
{
    [Header("Scene Index")]
    public int sceneIndex = 1;

    [Header("Character Name")]
    public string characterName = "Character";

    [Header("Time Played")]
    public float secondsPlayed;

    // 기본적인 변수만 저장 가능함
    // Vector3 같은 고기능 변수는 저장 불가
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
}
