using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting.Antlr3.Runtime.Tree;
using UnityEditor.SearchService;
using UnityEngine;
using UnityEngine.SceneManagement;

public class WorldSaveGameManager : MonoBehaviour
{
    public PlayerManager player;
    public static WorldSaveGameManager Instance { get; set; }

    [Header("Save/Load")]
    [SerializeField] bool saveGame;
    [SerializeField] bool loadGame;

    [Header("World Scene Index")]
    [SerializeField] int worldSceneIndex = 1;

    [Header("Save Data Writer")]
    private SaveFileDataWriter saveFileDataWriter;

    [Header("Current Character Data")]
    public CharacterSlots currentCharacterSlotBeingUsed;
    public CharacterSaveData currentCharacterData;
    private string saveFileName;

    [Header("Character Slots")]
    public CharacterSaveData characterSlots01;
    public CharacterSaveData characterSlots02;
    public CharacterSaveData characterSlots03;
    public CharacterSaveData characterSlots04;
    public CharacterSaveData characterSlots05;

    private void Awake()
    {
        // �ش� ��ũ��Ʈ�� �ν��Ͻ��� �ϳ��� ������ �� ������, �ٸ��� ����� �ı�.
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        DontDestroyOnLoad(gameObject);
        LoadAllCharacterProfiles();
    }

    private void Update()
    {
        if (saveGame)
        {
            saveGame = false;
            SaveGame();
        }

        if (loadGame)
        {
            loadGame = false;
            LoadGame();
        }
    }

    public string DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots characterSlots)
    {
        string fileName = "";
        switch (characterSlots)
        {
            case CharacterSlots.CharacterSlots_01:
                fileName = "characterSlots_01";
                break;
            case CharacterSlots.CharacterSlots_02:
                fileName = "characterSlots_02";
                break;
            case CharacterSlots.CharacterSlots_03:
                fileName = "characterSlots_03";
                break;
            case CharacterSlots.CharacterSlots_04:
                fileName = "characterSlots_04";
                break;
            case CharacterSlots.CharacterSlots_05:
                fileName = "characterSlots_05";
                break;
            default:
                break;
        }

        return fileName;
    }

    public void AttemptToCreateNewGame()
    {
        saveFileDataWriter = new SaveFileDataWriter();
        saveFileDataWriter.saveDataDirectoryPath = Application.persistentDataPath;

        // �� ������ ����, � ������ ����ϴ� ���� ���� ���� �̸��� �޸�.
        // Check to see if file exist first before create new file.
        saveFileDataWriter.saveFilename = DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots.CharacterSlots_01);

        if(!saveFileDataWriter.CheckToSeeIfFileExists())
        {
            // If this profile slot is not taken, make new one using this slot.
            currentCharacterSlotBeingUsed = CharacterSlots.CharacterSlots_01;
            currentCharacterData = new CharacterSaveData();
            StartCoroutine(LoadWorldScene());
            return;
        }

        saveFileDataWriter.saveFilename = DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots.CharacterSlots_02);

        if(!saveFileDataWriter.CheckToSeeIfFileExists())
        {
            // If this profile slot is not taken, make new one using this slot.
            currentCharacterSlotBeingUsed = CharacterSlots.CharacterSlots_02;
            currentCharacterData = new CharacterSaveData();
            StartCoroutine(LoadWorldScene());
            return;
        }
        
        // 자유 슬롯이 없을때, 플레이어에 노티파이
        TitleScreenManager.Instance.DisplayNofreeCharacterSlotPopUp();

    }

    public void LoadGame()
    {
        // ���� ������ �θ�, � ������ ����ϴ����� ���� �����̸��� ����.
        saveFileName = DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(currentCharacterSlotBeingUsed);

        saveFileDataWriter = new SaveFileDataWriter();
        // �Ϲ������� �پ��� ��� Ÿ�Կ� �۵���(Application.persistentDataPath)
        saveFileDataWriter.saveDataDirectoryPath = Application.persistentDataPath;
        saveFileDataWriter.saveFilename = saveFileName;
        currentCharacterData = saveFileDataWriter.LoadSaveFile();

        StartCoroutine(LoadWorldScene());
    }

    public void SaveGame()
    {
        // ���� ������ ������, � ������ �̿��ϰ� �ִ��� �����̸��� ����.
        saveFileName = DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(currentCharacterSlotBeingUsed);

        saveFileDataWriter = new SaveFileDataWriter();
        saveFileDataWriter.saveDataDirectoryPath = Application.persistentDataPath;
        saveFileDataWriter.saveFilename = saveFileName;

        // �÷��̾������� �Ѱ��ְ�, ���̺�����ȭ��.
        player.SaveGameDataToCurrentCharacterData(ref currentCharacterData);

        // saveFileDataWriter�� createCharacterSaveFile �� �ҷ�����, currentCharacterData�� �ѱ�
        saveFileDataWriter.CreateCharacterSaveFile(currentCharacterData);
    }

    // ������ �����ϸ� ��迡 �ִ� ��� ĳ���� �������� �ҷ�����
    private void LoadAllCharacterProfiles()
    {
        saveFileDataWriter = new SaveFileDataWriter();
        saveFileDataWriter.saveDataDirectoryPath = Application.persistentDataPath;

        saveFileDataWriter.saveFilename = 
            DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots.CharacterSlots_01);
        characterSlots01 = saveFileDataWriter.LoadSaveFile();

        saveFileDataWriter.saveFilename =
            DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots.CharacterSlots_02);
        characterSlots02 = saveFileDataWriter.LoadSaveFile();

        saveFileDataWriter.saveFilename =
            DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots.CharacterSlots_03);
        characterSlots03 = saveFileDataWriter.LoadSaveFile();

        saveFileDataWriter.saveFilename =
            DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots.CharacterSlots_04);
        characterSlots04 = saveFileDataWriter.LoadSaveFile();

        saveFileDataWriter.saveFilename =
            DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots.CharacterSlots_05);
        characterSlots05 = saveFileDataWriter.LoadSaveFile();
    }

    // �ڷ�ƾ������ �ϴ� IEnumerator �� ���.
    public IEnumerator LoadWorldScene()
    {
        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(worldSceneIndex);

        player.LoadGameDataFromCurrentCharacterData(ref currentCharacterData);

        yield return null;
    }

    public int GetWorldSceneIndex()
    {
        return worldSceneIndex;
    }
}
