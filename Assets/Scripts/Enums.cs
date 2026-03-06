using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Enums : MonoBehaviour
{
}

// =========================================================================================
// [P4 신규 추가] 애니메이션 이벤트 통합 규약 (Type-Safe Event System)
// =========================================================================================
public enum AnimationEventType
{
    // [0~99] 시스템 및 전투 플로우 제어 (Logic & Flow)
    ComboEnable = 0,
    ComboDisable = 1,
    HitBoxEnable = 2,
    HitBoxDisable = 3,
    Action_Ended = 4,       // 애니메이션 종료 시 플래그 초기화용
    Damage_Calculated = 5,  // [P1 체인 로직] 연산 종료 후 연출을 부르는 2차 신호
    IFrameEnable = 6,
    IFrameDisable = 7,
    Charge_Started = 8,
    Charge_Ended = 9,

    // [100~199] 오디오 및 사운드 피드백 (Audio / SFX)
    PlaySound_WeaponSwing_Light = 100,
    PlaySound_WeaponSwing_Heavy = 101,
    PlaySound_Footstep = 102,
    Stamina_Exhausted = 103,
    Stamina_Recovered = 104,
    Roll_Start = 105,

    // [200~299] 시각 효과 및 카메라 연출 (Visuals & VFX)
    PlayVFX_BloodSplatter = 200,
    PlayVFX_WeaponTrail = 201,
    CameraShake_Light = 202,
    CameraShake_Heavy = 203
}

// =========================================================================================
// 기존 시스템 Enum 보존
// =========================================================================================

public enum WorldSlots { WorldSlots_01 }

public enum CharacterSlots
{
    CharacterSlots_01, CharacterSlots_02, CharacterSlots_03,
    CharacterSlots_04, CharacterSlots_05, No_Slot,
}

public enum CharacterGroup { Team01, Team02 }

public enum WeaponModelSlot { RightHand, LeftHand }

// 데미지 기반 공격 타입을 계산하는데 활용.
public enum AttackType
{
    LightAttack01, LightAttack02, HeavyAttack01, HeavyAttack02, ChargeAttack01, ChargeAttack02,
}

public enum LockOnDirection { Left, Right, None }

public enum SwithchWeaponSide { Left, Right }

public enum CookingState
{
    Empty,      // 비어있음
    Raw,        // 재료 투입됨 (조리 전)
    Cooking,    // 조리 중 (끓는 중/ 굽는 중)
    Cooked,     // 조리 완료
    Burnt       // 탐 (굽기 전용)
}

public enum CookingStationType
{
    Pot,        // 냄비 (끓이기)
    Grill,      // 석쇠 (굽기)
}

public enum GameState
{
    Normal, Chase, LockOn, Inventory, Table, Cooking, CinematicFocus,
}

// 장착 슬롯 정의
public enum EquipmentSlot
{
    RightHand, LeftHand, Helmet, ChestArmor, Pants, Leggings, Backpack, Accessory
}