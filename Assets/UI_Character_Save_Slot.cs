using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class UI_Character_Save_Slot : MonoBehaviour
{
    SaveFileDataWriter saveFileWriter;

    [Header("Game Slot")]
    public CharacterSlots characterSlot;

    [Header("Character Info")]
    public TextMeshProUGUI characterName;
    public TextMeshProUGUI timePlayed;

    private void OnEnable()
    {
        LoadSaveSlots();
    }

    private void LoadSaveSlots()
    {
        saveFileWriter = new SaveFileDataWriter();
        saveFileWriter.saveDataDirectoryPath = Application.persistentDataPath;

        // ���̺� ���� 01
        if (characterSlot == CharacterSlots.CharacterSlots_01)
        {
            saveFileWriter.saveFilename = WorldSaveGameManager.Instance.DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(characterSlot);

            // ������ ������ ���� ����
            // ���Ͽ��� �����͸� ������ ���ӿ�����Ʈ�� ����
            if (saveFileWriter.CheckToSeeIfFileExists())
            {
                characterName.text = WorldSaveGameManager.Instance.characterSlots01.characterName;
            }
            // �������� ������, ���ӿ�����Ʈ ��Ȱ��ȭ
            else
            {
                gameObject.SetActive(false);
            }
        }
        // ���̺� ���� 02
        else if (characterSlot == CharacterSlots.CharacterSlots_02)
        {
            saveFileWriter.saveFilename = WorldSaveGameManager.Instance.DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(characterSlot);

            // ������ ������ ���� ����
            // ���Ͽ��� �����͸� ������ ���ӿ�����Ʈ�� ����
            if (saveFileWriter.CheckToSeeIfFileExists())
            {
                characterName.text = WorldSaveGameManager.Instance.characterSlots02.characterName;
            }
            // �������� ������, ���ӿ�����Ʈ ��Ȱ��ȭ
            else
            {
                gameObject.SetActive(false);
            }
        }
        // ���̺� ���� 03
        else if (characterSlot == CharacterSlots.CharacterSlots_03)
        {
            saveFileWriter.saveFilename = WorldSaveGameManager.Instance.DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(characterSlot);

            // ������ ������ ���� ����
            // ���Ͽ��� �����͸� ������ ���ӿ�����Ʈ�� ����
            if (saveFileWriter.CheckToSeeIfFileExists())
            {
                characterName.text = WorldSaveGameManager.Instance.characterSlots03.characterName;
            }
            // �������� ������, ���ӿ�����Ʈ ��Ȱ��ȭ
            else
            {
                gameObject.SetActive(false);
            }
        }
        // ���̺� ���� 04
        else if (characterSlot == CharacterSlots.CharacterSlots_04)
        {
            saveFileWriter.saveFilename = WorldSaveGameManager.Instance.DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(characterSlot);

            // ������ ������ ���� ����
            // ���Ͽ��� �����͸� ������ ���ӿ�����Ʈ�� ����
            if (saveFileWriter.CheckToSeeIfFileExists())
            {
                characterName.text = WorldSaveGameManager.Instance.characterSlots04.characterName;
            }
            // �������� ������, ���ӿ�����Ʈ ��Ȱ��ȭ
            else
            {
                gameObject.SetActive(false);
            }
        }
        // ���̺� ���� 05
        else if (characterSlot == CharacterSlots.CharacterSlots_05)
        {
            saveFileWriter.saveFilename = WorldSaveGameManager.Instance.DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(characterSlot);

            // ������ ������ ���� ����
            // ���Ͽ��� �����͸� ������ ���ӿ�����Ʈ�� ����
            if (saveFileWriter.CheckToSeeIfFileExists())
            {
                characterName.text = WorldSaveGameManager.Instance.characterSlots05.characterName;
            }
            // �������� ������, ���ӿ�����Ʈ ��Ȱ��ȭ
            else
            {
                gameObject.SetActive(false);
            }
        }
    }

    public void LoadGameFromCharacterSlot() 
    {
        WorldSaveGameManager.Instance.currentCharacterSlotBeingUsed = characterSlot;
        WorldSaveGameManager.Instance.LoadGame();
    }

    public void SelectCurrentSlot()
    {
        TitleScreenManager.Instance.SelectCharacterSlot(characterSlot);
    }
}
