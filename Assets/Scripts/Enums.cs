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
