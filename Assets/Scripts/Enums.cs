using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Enums : MonoBehaviour
{

}

public enum WorldSlots
{
    WorldSlots_01,
}

public enum CharacterSlots
{
    CharacterSlots_01,
    CharacterSlots_02,
    CharacterSlots_03,
    CharacterSlots_04,
    CharacterSlots_05,
    No_Slot,
}

public enum CharacterGroup
{
    Team01,
    Team02,
}

public enum WeaponModelSlot
{
    RightHand,
    LeftHand,
    // 우측 엉덩이
    // 좌측 엉덩이,
    // 등, 
}

// 데미지 기반 공격 타입을 계산하는데 활용.
public enum AttackType
{
    LightAttack01,
    LightAttack02,
    HeavyAttack01,
    HeavyAttack02,
    ChargeAttack01,
    ChargeAttack02,

}

public enum LockOnDirection
{
    Left,
    Right,
    None
}

public enum SwithchWeaponSide
{
    Left,
    Right,
}

public enum CookingState
{
    Empty,          //  비어잇음
    Raw,            //  재료 투입됨 (조리 전)
    Cooking,        //  조리 중 (끓는 중/ 굽는 중)
    Cooked,         //  조리 완료
    Burnt           //  탐 (굽기 전용)
}

public enum CookingStationType
{
    Pot,            //  냄비 (끓이기)
    Grill,          //  석쇠 (굽기)
}

public enum GameState
{
    Normal,
    Chase,
    LockOn,
    Inventory,
    Table,
    Cooking,
    CinematicFocus,
}

// 장착 슬롯 정의 남규 추가 260119
public enum EquipmentSlot
{
    RightHand,    // 주손 무기
    LeftHand,     // 보조손 무기/방패
    Helmet,       // 머리
    ChestArmor,   // 상체
    Pants,        // 하체
    Leggings,     // 발/각반
    Backpack,     // 가방 (인벤토리 격자 용량 결정)
    Accessory     // 장신구
}