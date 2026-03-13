using UnityEngine;
using TDA.Items;
using TDA.Character;

/// <summary>
/// 방패 프리팹 전용 매니저입니다. WeaponManager를 상속받아 타격(Shield Bash) 기능을 유지하면서,
/// 장착 시 캐릭터의 DefenseManager에 방패 고유의 데이터(내구도, 컷률)를 주입하는 역할을 수행합니다.
/// </summary>
public class ShieldManager : WeaponManager
{
    [Header("Shield Specific Data")]
    public ShieldWeaponItemSO shieldWeaponData;

    /// <summary>
    /// 캐릭터 장비 매니저(EquipmentManager)가 방패를 손에 로드할 때 호출하는 초기화 함수입니다.
    /// </summary>
    /// <param name="characterWieldingShield">방패를 장착하는 주체 (Player 또는 AI)</param>
    /// <param name="shieldData">장착되는 방패의 ScriptableObject 데이터</param>
    public void SetShieldDefenseStats(CharacterManager characterWieldingShield, ShieldWeaponItemSO shieldData)
    {
        // 1. 방패 타격(Shield Bash) 데미지 세팅 (부모 클래스의 무기 세팅 로직 재활용)
        // (MeleeWeaponDamageCollider가 방패에 달려있다면 데미지 모디파이어를 적용합니다)
        base.SetWeaponDamage(characterWieldingShield, shieldData);

        // 2. 현재 장착된 방패 데이터 캐싱
        this.shieldWeaponData = shieldData;

        // 3. [방어 시스템 P0-02 연동] 캐릭터의 방어 매니저(DefenseManager)에게 이 방패의 스펙(내구도, 저항력 등)을 등록합니다.
        if (characterWieldingShield.characterDefenseManager != null)
        {
            characterWieldingShield.characterDefenseManager.SetDefendingItem(shieldData);
            Debug.Log($"<color=cyan>[ShieldManager]</color> {shieldData.itemName} 방패의 방어 스펙(내구도: {shieldData.maxDurability})이 캐릭터에게 성공적으로 주입되었습니다.");
        }
        else
        {
            Debug.LogWarning($"<color=yellow>[ShieldManager]</color> {characterWieldingShield.gameObject.name}에 CharacterDefenseManager가 존재하지 않아 방패 스펙을 주입할 수 없습니다.");
        }
    }
}