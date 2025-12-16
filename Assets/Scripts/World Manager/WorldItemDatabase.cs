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
    private Dictionary<int, GameObject> itemLookupDictionary = new Dictionary<int, GameObject>();


    [Header("Cooking Databases")]
    [SerializeField] List<CookingRecipe> cookingRecipes = new List<CookingRecipe>();


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
        // 모든 무기를 아이템 리스트에 산입
        foreach (var weapon in weapons)
        {
            // 중복 방지 체크 후 추가.
            if (!items.Contains(weapon))
            { 
                items.Add(weapon);
            }
        }

        // 모든 아이템에 유니크 아이디 할당, i 넘버링 복사
        for (int i = 0; i < items.Count; i++)
        {
            items[i].itemID = i;
        }
    }

    // 월드세이브매니저에서 호출하는 필수 함수.
    public GameObject GetItemPrefab(int itemID)
    {
        if (itemLookupDictionary.TryGetValue(itemID, out GameObject prefab))
        {
            return prefab;
        }

        Debug.LogWarning($"[WorldItemDatabase] ID {itemID} 아이템을 찾을 수 없습니다.");
        return null;
    }

    public WeaponItem GetWeaponByID(int ID)
    {
        return weapons.FirstOrDefault(weapon => weapon.itemID == ID);
    }

    public Item GetItemByID(int ID)
    {
        return items.FirstOrDefault(item => item.itemID == ID);
    }

    // 주어진 재료 리스트와 일치하는 레시피 찾아 반환.
    // inputingridient 현재 냄비/도마에 올라간 재료 리스트
    // 매칭되는 레시피가 없으면 null 반환
    public CookingRecipe GetRecipeByIngredients(List<Item> inputIngredients)
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


