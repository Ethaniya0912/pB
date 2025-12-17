using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting.Antlr3.Runtime.Tree;
using UnityEditor.SearchService;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using SG;
using System;

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

    [Header("World Save Data")]
    public WorldSaveData currentWorldData = new WorldSaveData();
    // 드롭된 아이템 재생성 아이템 프리펩 정보
    [SerializeField] private WorldItemDatabase itemDatabase;

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
        // 해당 스크립트의 인스턴스는 하나만 존재할 수 잇으며, 다른게 존재시 파괴.
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
        DontDestroyOnLoad(gameObject);
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

    // --[저장/로드 I/O]--
    public void SaveWorld()
    {
        // 월드 데이터는 서버(호소트)만 관리하고 저장.
        if (!NetworkManager.Singleton.IsServer) return;

        // TD : 존재하는 SaveFileDataWriter 등을 활용하여 currentWorldData 파일화
        Debug.Log("[WorldSave] 데이터 저장 완료");
    }

    public void LoadWorld()
    {
        // 월드 상태 로딩은 서버(호스트)만 수행.
        // 클라이언트는 서버가 스폰해주는 오브젝트(NetworkObject)를 동기화받기만함.
        // 클라이언트가 로컬 파일을 읽어 스폰 시도시 충돌발생.
        if (!NetworkManager.Singleton.IsServer) return;

        // TD : 파일에서 읽어와 currentWorldData에 덮어쓰기
        Debug.Log("[WorldSave] 월드 데이터 로드 완료 (Server Only");

        // 로드 직후 런타임 오브젝트(드롭아이템) 복구 실행
        SpawnDroppedItems();
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

        // 새 파일을 만듬, 어떤 슬롯을 사용하는 지에 따라 파일 이름이 달림
        // Check to see if file exist first before create new file.
        saveFileDataWriter.saveFileName = DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots.CharacterSlots_01);

        if(!saveFileDataWriter.CheckToSeeIfFileExists())
        {
            // If this profile slot is not taken, make new one using this slot.
            currentCharacterSlotBeingUsed = CharacterSlots.CharacterSlots_01;
            currentCharacterData = new CharacterSaveData();
            NewGame();
            return;
        }

        saveFileDataWriter.saveFileName = DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots.CharacterSlots_02);

        if(!saveFileDataWriter.CheckToSeeIfFileExists())
        {
            // If this profile slot is not taken, make new one using this slot.
            currentCharacterSlotBeingUsed = CharacterSlots.CharacterSlots_02;
            currentCharacterData = new CharacterSaveData();
            NewGame();
            return;
        }
        
        // ?먯쑀 ?щ’???놁쓣?? ?뚮젅?댁뼱???명떚?뚯씠
        TitleScreenManager.Instance.DisplayNofreeCharacterSlotPopUp();

    }

    private void NewGame()
    {
        // 새게임 시작시 캐릭터 스탯과, 아이템을 저장함(캐릭터크링에이션씬 추가시)
        player.playerNetworkManager.vitality.Value = 15;
        player.playerNetworkManager.endurance.Value = 10;

        SaveGame();
        StartCoroutine(LoadWorldScene());
    }

    public void LoadGame()
    {
        // 이전 파일을 부름, 어떤 슬롯을 사용하는 지에 따라 파일 이름이 갈림.
        saveFileName = DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(currentCharacterSlotBeingUsed);

        saveFileDataWriter = new SaveFileDataWriter();
        // 일반적으로 다양한 기계 타입에 작동됨(Application.persistentDataPath)
        saveFileDataWriter.saveDataDirectoryPath = Application.persistentDataPath;
        saveFileDataWriter.saveFileName = saveFileName;
        currentCharacterData = saveFileDataWriter.LoadSaveFile();

        StartCoroutine(LoadWorldScene());
    }

    public void SaveGame()
    {
        // 현재 파일을 저장함, 어떤 슬롯을 이용하고 있는지 파일 이름에 따라.
        saveFileName = DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(currentCharacterSlotBeingUsed);

        saveFileDataWriter = new SaveFileDataWriter();
        saveFileDataWriter.saveDataDirectoryPath = Application.persistentDataPath;
        saveFileDataWriter.saveFileName = saveFileName;

        // 플레이어 인포를 넘겨주고, 세이브파일화함
        player.SaveGameDataToCurrentCharacterData(ref currentCharacterData);

        // saveFileDataWriter에 createCharacterSaveFile 을 불러오고, currentCharacterData을 넘김
        saveFileDataWriter.CreateCharacterSaveFile(currentCharacterData);
    }

    public void DeleteGame(CharacterSlots characterSlots)
    {
        // 선택한 파일을 삭제, 어떤 슬롯을 사용하는 지에 따라 파일 이름이 갈림.

        saveFileDataWriter = new SaveFileDataWriter();
        // 일반적으로 다양한 기계 타입에 작동됨(Application.persistentDataPath)
        saveFileDataWriter.saveDataDirectoryPath = Application.persistentDataPath;
        saveFileDataWriter.saveFileName = DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(characterSlots);
        saveFileDataWriter.DeleteSaveFile();
        //currentCharacterData = saveFileDataWriter.LoadSaveFile();
    }

    // 게임을 시작하면 기계에 있는 모든 캐릭터 프로필을 불러오기
    private void LoadAllCharacterProfiles()
    {
        saveFileDataWriter = new SaveFileDataWriter();
        saveFileDataWriter.saveDataDirectoryPath = Application.persistentDataPath;

        saveFileDataWriter.saveFileName = 
            DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots.CharacterSlots_01);
        characterSlots01 = saveFileDataWriter.LoadSaveFile();

        saveFileDataWriter.saveFileName =
            DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots.CharacterSlots_02);
        characterSlots02 = saveFileDataWriter.LoadSaveFile();

        saveFileDataWriter.saveFileName =
            DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots.CharacterSlots_03);
        characterSlots03 = saveFileDataWriter.LoadSaveFile();

        saveFileDataWriter.saveFileName =
            DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots.CharacterSlots_04);
        characterSlots04 = saveFileDataWriter.LoadSaveFile();

        saveFileDataWriter.saveFileName =
            DecideCharacterFileNameBasedOnCharacterSlotBeingUsed(CharacterSlots.CharacterSlots_05);
        characterSlots05 = saveFileDataWriter.LoadSaveFile();
    }

    // --[런타임 로직]--

    // 1. 제거 등록 (루팅)

    public void AddRemovedObject(int id)
    {
        if (!currentWorldData.removedInteractableIDs.Contains(id))
            currentWorldData.removedInteractableIDs.Add(id);
    }

    // 2. 상태/위치 업데이트 (문 열기, 상자 밀기)
    public void  UpdateObjectState(int id, bool state, Vector3? pos = null, Quaternion? rot = null)
    {
        WorldObjectState data = new WorldObjectState
        {
            interactableID = id,
            boolValue = state,
            savePosition = pos.HasValue, // 위치값 전달되었으면 true
            position = pos ?? Vector3.zero,
            rotation = rot ?? Quaternion.identity,
        };

        if (currentWorldData.objectStates.ContainsKey(id))
            currentWorldData.objectStates[id] = data;
        else
            currentWorldData.objectStates.Add(id, data);
    }

    // 3. 드롭 아이템 등록
    public void AddDroppedItem(int itemID, int amount, Vector3 pos, Quaternion rot)
    {
        WorldItemSaveData data = new WorldItemSaveData
        {
            itemID = itemID,
            amount = amount,
            position = pos,
            rotation = rot
        };
        currentWorldData.droppedItems.Add(data);
    }

    // 4. 드롭 아이템 복구 (스폰)
    private void SpawnDroppedItems()
    {
        // 이 함수는 서버에서만 호출(IsServer)
        // late joiner 는 NetworkObject.Spawn되면 알아서 관리.
        foreach (var itemData in currentWorldData.droppedItems)
        {
            var prefab = WorldItemDatabase.Instance.GetItemPrefab(itemData.itemID);
            if (prefab != null)
            {
                // 1. 서버에서 아이템 인스턴스화 (위치/회전값 적용)
                GameObject obj = Instantiate(prefab, itemData.position, itemData.rotation);
                var netObj = obj.GetComponent<NetworkObject>();

                if (netObj != null)
                {
                    // 2. 네트워크 스폰 실행
                    // late joiner 처리 원리
                    // networkObject.Spawn()이 호출되면 netcode 시스템이 이 오브젝트 관리 시작.
                    // 나중에 클라 접속, 서버는 자동으로 현재 존재하는 모든 networkObject 정보를
                    // 그 클라이언트에 전송(Replication). 따라 별도 동기화 코드 없이도
                    // 나중에 들어온 유저는 해당 아이템이 해당 위치에 있는 것 확인 가능
                    // (단, 프리팹에 NetworkTransform이 있어야 위치 동기화가 정확)
                    netObj.Spawn();
                }
            }
        }
    }

    // 유틸리티 : 상태 조회
    public bool TryGetObjectState(int id, out WorldObjectState state)
    {
        return currentWorldData.objectStates.TryGetValue(id, out state);    
    }

    // 코루틴 역할을 하는 IEnumerator을 사용.
    public IEnumerator LoadWorldScene()
    {
        // 씬 하나일 경우 아래 코드
        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(worldSceneIndex);

        // 씬 여럿일 경우 아래코드
        // AsyncOperation loadOperation = SceneManager.LoadSceneAsync(currentCharacterData.sceneIndex);

        player.LoadGameDataFromCurrentCharacterData(ref currentCharacterData);

        yield return null;
    }

    public int GetWorldSceneIndex()
    {
        return worldSceneIndex;
    }
}
