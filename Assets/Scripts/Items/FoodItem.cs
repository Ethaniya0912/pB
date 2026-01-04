using UnityEngine;

[CreateAssetMenu(menuName = "Items/Food&Cooking/Food Item")]
public class FoodItem : Item
{
    [Header("Cooking & Interaction Flags")]
    [Tooltip("연료로 사용 가능한가? (화덕에 넣을 수 있음)")]
    public bool isBurnable;
    [Tooltip("연료로 사용 시 제공하는 연료량")]
    public float fuelAmount = 0;

    [Tooltip("조리가 가능한 식재료인가? (냄비에 넣을 수 있음)")]
    public bool isCookable;

    [Tooltip("절단이 가능한가? (도마에서 칼로 자를 수 있음)")]
    public bool isCuttable;
    [Tooltip("절단 시 생성되는 아이템 (없으면 null)")]
    public Item cutResultItem;
}
