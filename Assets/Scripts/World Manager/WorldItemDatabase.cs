using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class WorldItemDatabase : MonoBehaviour
{
    public static WorldItemDatabase Instance {get; private set;}
    public WeaponItem unarmedWeapon;

    [Header("Item Databases")]
    [SerializeField] List<WeaponItem> weapons = new List<WeaponItem>();
    // 게임에 존재하는모든 아이템의 리스트
    [SerializeField] List<Item> items = new List<Item>();
    // 빠른 검색을 위한 딕셔너리 (게임 시작 시 리스트를 딕셔너리로 변환)
    // [최적화] 게임 중 ID로 빠르게 아이템(프리팹)을 찾기 위한 딕셔너리
    private Dictionary<int, GameObject> itemPrefabDictionary = new Dictionary<int, GameObject>();
    // [편의성] ID로 Item 데이터 자체를 찾기 위한 딕셔너리
    private Dictionary<int, Item> itemDataDictionary = new Dictionary<int, Item>();

    [Header("Cooking Databases")]
    [SerializeField] List<CookingRecipeSO> cookingRecipes = new List<CookingRecipeSO>();


    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        DontDestroyOnLoad(gameObject);
        // 아이템 리스트 초기화 및 ID 자동 할당
        InitializeItemDatabase();
    }

    private void InitializeItemDatabase()
    {
        itemPrefabDictionary.Clear();
        itemDataDictionary.Clear();

        // 1. 모든 무기를 아이템 리스트에 산입
        foreach (var weapon in weapons)
        {
            // 중복 방지 체크 후 추가.
            if (!items.Contains(weapon))
            { 
                items.Add(weapon);
            }
        }

        // 2. 모든 아이템에 유니크 아이디와 딕셔너리 할당, i 넘버링 복사
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null)
            {
                // 사용자 로직: 리스트 순서대로 ID 부여 (0, 1, 2...)
                items[i].itemID = i;

                // [데이터 등록] ID로 Item 데이터 검색용
                if (!itemDataDictionary.ContainsKey(i))
                {
                    itemDataDictionary.Add(i, items[i]);
                }

                // [프리팹 등록] 월드 스폰용 (드롭 아이템)
                if (items[i].itemModel != null)
                {
                    if (!itemPrefabDictionary.ContainsKey(i))
                    {
                        itemPrefabDictionary.Add(i, items[i].itemModel);
                    }
                }

            }
        }
    }
    /// <summary>
    ///  ID 기반으로 월드에 생성될 프리팹(itemModel)반환
    ///  WorldSaveGameManager의 SpawnDroppedItems에서 사용.
    /// </summary>

/*    public WeaponItem GetWeaponByID(int ID)
    {
        return weapons.FirstOrDefault(weapon => weapon.itemID == ID);
    }*/
/*    public Item GetItemByID(int ID)
    {
        return items.FirstOrDefault(item => item.itemID == ID);
    }*/

    public WeaponItem GetWeaponByID(int ID)
    {
        // FirstOrDefault 대신 딕셔너리
        if (itemDataDictionary.TryGetValue(ID, out Item item))
        {
            return item as WeaponItem; // 형변환하여 반환
        }
        return null;
    }

    public Item GetItemByID(int ID)
    {
        if (itemDataDictionary.TryGetValue(ID,out Item item))
        {
            return item;
        }
        return null;
    }

    // WorldSaveGameManager에서 아이템 스폰을 위해 호출
    public GameObject GetItemPrefab(int itemID)
    {
        if (itemPrefabDictionary.TryGetValue(itemID, out GameObject prefab))
        {
            return prefab;
        }
        Debug.LogWarning($"[WorldItemDatabase] ID {itemID} 아이템을 찾을 수 없습니다.");
        return null;
    }

    // 주어진 재료 리스트와 일치하는 레시피 찾아 반환.
    // inputingridient 현재 냄비/도마에 올라간 재료 리스트
    // 매칭되는 레시피가 없으면 null 반환
    public CookingRecipeSO GetRecipeByIngredients(List<Item> inputIngredients)
    {
        if (inputIngredients == null || inputIngredients.Count == 0) return null;

        foreach (var recipe in cookingRecipes)
        {
            // 1. 재료 개수가 다르면 탈락.
            if (recipe.ingredients.Count != inputIngredients.Count) continue;

            // 2. 내용물 비교 (순서 상관없이 구성품이 같은지 확인)
            // GroupBy를 사용, 각 아이템의 ID와 개수 비교.
            var recipeCounts = recipe.ingredients
                .GroupBy(i => i.itemID)
                .ToDictionary(g => g.Key, g => g.Count());

            var inputCounts = inputIngredients
                .GroupBy(i => i.itemID)
                .ToDictionary(g => g.Key, g => g.Count());

            // 딕셔너리 비교 : 키 개수가 같고, 각 키에 대한 값(아이템 개수)이 모두 일치해야 함
            bool isMatch = recipeCounts.Count == inputCounts.Count && !recipeCounts.Except(inputCounts).Any();

            if (isMatch) return recipe;
        }

        return null;
    }
}


