using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponItem : Item
{
    [Header("Weapon Model")]
    public GameObject weaponModel;

    [Header("Weapon Requirements")]
    public int muscleReq = 0;

    [Header("Weapon Base Damage")]
    public int physicalDamage = 0;
    public int elementalDamage = 0;

    // 무기 가드 흡수 (방어 파워), 블럭킹 적용시 추가

    [Header("Weapon Poise Damage")]
    public float poiseDamage = 10;
    // 공격시 포이즈 보너스

    [Header("Attack Modifiers")]
    // 웨폰 모디파이어
    // 라이트 어택 모디파이어
    public float light_Attack_01_Modifier = 1.1f;
    public float light_Attack_02_Modifier = 1.2f;
    public float heavy_Attack_01_Modifier = 1.4f;
    public float heavy_Attack_02_Modifier = 1.6f;
    public float charge_Attack_01_Modifier = 2.5f;
    public float charge_Attack_02_Modifier = 2.7f;
    // 헤비 어택 모디파이어
    // 크리티컬 데미지 모디파이어

    [Header("Stamina Costs Modifiers")]
    public int baseStaminaCost = 20;
    public float lightAttackStaminaCostMultiplier = 0.9f;
    // 달리면서 공격 스태미나 코스트 모디파이어
    // 라이트 어택 스태미나 코스트 모디파이어
    // 헤비어택 스태미나 코스트 모디파이어

    [Header("Actions")]
    public WeaponItemAction oh_RB_Action; // 한손 right bumper 액션.
    public WeaponItemAction oh_RT_Action; // 한손 right trigger 액션.


}
