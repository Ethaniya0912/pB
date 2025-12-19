using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Items/Cooking Recipe")]
public class CookingRecipeSO : ScriptableObject
{
    [Header("Recipe Info")]
    public string recipeName;

    [Header("Ingredients")]
    [Tooltip("요리에 필요한 재료 아이템 리스트")]
    public List<Item> ingredients = new List<Item>();

    [Header("Result")]
    [Tooltip("요리 성공 시 생성되는 아이템")]
    public Item resultItem;
    [Tooltip("요리 실패(탐) 시 생성되는 아이템")]
    public Item burntItem;

    [Header("Cooking Requirements")]
    [Tooltip("필요한 최소 화력")]
    public float requiredHeatPower = 1.0f;
    [Tooltip("기본 조리 시간 (초)")]
    public float baseCookingTime = 5.0f;
    [Tooltip("타버리는 시간 (완료 후 이 시간이 지나면 burntItem으로 변함)")]
    public float burnTimeThreshold = 10.0f;
}