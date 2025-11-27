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

        // 세이브 슬롯 01
        if (characterSlot == CharacterSlots.CharacterSlots_01)
        {
            saveFileWriter.saveFileName = WorldSaveGameManager.Instance.DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(characterSlot);

            // 파일이 있으면 로직 실행
            // 파일에서 데이터를 가져와 게임오브젝트에 적용
            if (saveFileWriter.CheckToSeeIfFileExists())
            {
                characterName.text = WorldSaveGameManager.Instance.characterSlots01.characterName;
            }
            // 존재하지 않으면, 게임오브젝트 비활성화
            else
            {
                gameObject.SetActive(false);
            }
        }
        // 세이브 슬롯 02
        else if (characterSlot == CharacterSlots.CharacterSlots_02)
        {
            saveFileWriter.saveFileName = WorldSaveGameManager.Instance.DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(characterSlot);

            // 파일이 있으면 로직 실행
            // 파일에서 데이터를 가져와 게임오브젝트에 적용
            if (saveFileWriter.CheckToSeeIfFileExists())
            {
                characterName.text = WorldSaveGameManager.Instance.characterSlots02.characterName;
            }
            // 존재하지 않으면, 게임오브젝트 비활성화
            else
            {
                gameObject.SetActive(false);
            }
        }
        // 세이브 슬롯 03
        else if (characterSlot == CharacterSlots.CharacterSlots_03)
        {
            saveFileWriter.saveFileName = WorldSaveGameManager.Instance.DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(characterSlot);

            // 파일이 있으면 로직 실행
            // 파일에서 데이터를 가져와 게임오브젝트에 적용
            if (saveFileWriter.CheckToSeeIfFileExists())
            {
                characterName.text = WorldSaveGameManager.Instance.characterSlots03.characterName;
            }
            // 존재하지 않으면, 게임오브젝트 비활성화
            else
            {
                gameObject.SetActive(false);
            }
        }
        // 세이브 슬롯 04
        else if (characterSlot == CharacterSlots.CharacterSlots_04)
        {
            saveFileWriter.saveFileName = WorldSaveGameManager.Instance.DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(characterSlot);

            // 파일이 있으면 로직 실행
            // 파일에서 데이터를 가져와 게임오브젝트에 적용
            if (saveFileWriter.CheckToSeeIfFileExists())
            {
                characterName.text = WorldSaveGameManager.Instance.characterSlots04.characterName;
            }
            // 존재하지 않으면, 게임오브젝트 비활성화
            else
            {
                gameObject.SetActive(false);
            }
        }
        // 세이브 슬롯 05
        else if (characterSlot == CharacterSlots.CharacterSlots_05)
        {
            saveFileWriter.saveFileName = WorldSaveGameManager.Instance.DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(characterSlot);

            // 파일이 있으면 로직 실행
            // 파일에서 데이터를 가져와 게임오브젝트에 적용
            if (saveFileWriter.CheckToSeeIfFileExists())
            {
                characterName.text = WorldSaveGameManager.Instance.characterSlots05.characterName;
            }
            // 존재하지 않으면, 게임오브젝트 비활성화
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
