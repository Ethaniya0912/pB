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

    // 웨폰 모디파이어
    // 라이트 어택 모디파이어
    // 헤비 어택 모디파이어
    // 크리티컬 데미지 모디파이어

    [Header("Stamina Costs")]
    public int baseStaminaCost = 20;
    // 달리면서 공격 스태미나 코스트 모디파이어
    // 라이트 어택 스태미나 코스트 모디파이어
    // 헤비어택 스태미나 코스트 모디파이어


}
