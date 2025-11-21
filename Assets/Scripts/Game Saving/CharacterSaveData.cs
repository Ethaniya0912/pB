using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
// �ش� �����͸� ��� ���̺� ���Ͽ��� �����Ϸ��ϱ� ������, ������� �ƴϸ� serealizable��
public class CharacterSaveData
{
    [Header("Character Name")]
    public string characterName = "Character";

    [Header("Time Played")]
    public float secondsPlayed;

    // �⺻���� ������ ���� �����ϱ⿡
    // Vector3 ���� ����� ������ ����Ұ�
    [Header("World Coordinates")]
    public float xPosition;
    public float yPosition;
    public float zPosition;
}
