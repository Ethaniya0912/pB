using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayerUIHUDManager : MonoBehaviour
{
    [Header("Stat Bars")]
    [SerializeField] UI_StatBar staminaBar;
    [SerializeField] UI_StatBar healthBar;

    [Header("Quick Slots")]
    [SerializeField] Image rightWeaponQuickSlotIcon;
    [SerializeField] Image leftWeaponQuickSlotIcon;

    public void RefreshHUD()
    {
        healthBar.gameObject.SetActive(false);
        healthBar.gameObject.SetActive(true);
        staminaBar.gameObject.SetActive(false);
        staminaBar.gameObject.SetActive(true);
    }

    public void SetNewHealthValue(int oldValue, int newValue)
    {
        healthBar.SetStat(newValue);
    }

    
    public void SetMaxHealthValue(int maxHelath)
    {
        healthBar.SetMaxStat(maxHelath);
    }

    public void SetNewStaminaValue(float oldValue, float newValue)
    {
        staminaBar.SetStat(newValue);
    }

    
    public void SetMaxStaminaValue(int maxStamina)
    {
        staminaBar.SetMaxStat(maxStamina);
    }

    public void SetRightWeaponQuickSlotIcon(int weaponID)
    {
        // 방법1 : 플레이어의 손에 있는 우측 무기를 직접 레퍼런스
        // 장점/단점 : 직관적 / 무기를 먼저 로딩하고 이 함수를 부르는걸 까먹으면, 에러가 남
        // 예 : 이전 세이브 게임을 로드했는데, 로딩 UI에 무기를 레퍼했는데 아직 인스턴스 안됨
        // 오더 순서만 기억하면 괜찮음.

        // 방법2 :  무기의 아이템 ID를 요구하며, 데이터베이스에서 무기를 가져와 무기 icon으로.
        // 장점 : 항상 무기 ID가 있기 때문에, 유저를 기다릴 필요가 없음.
        // 단점 : 직관적이지 않음.
        // 작동순서를 기억하지 않는다면 훌륭함. (요 방식으로 감)

        WeaponItem weapon = WorldItemDatabase.Instance.GetWeaponByID(weaponID);
        if (weapon == null)
        {
            Debug.Log("ITEM IS NULL");
            rightWeaponQuickSlotIcon.enabled = false;
            rightWeaponQuickSlotIcon.sprite = null;
            return;
        }

        if (weapon.itemIcon == null)
        {
            Debug.Log("ITEM NO ICON");
            rightWeaponQuickSlotIcon.enabled = false;
            rightWeaponQuickSlotIcon.sprite = null;
            return;
        }

        // UI를 

        rightWeaponQuickSlotIcon.sprite = weapon.itemIcon;
        rightWeaponQuickSlotIcon.enabled = true;
    }

    public void SetLeftWeaponQuickSlotIcon(int weaponID)
    {
        // 방법1 : 플레이어의 손에 있는 우측 무기를 직접 레퍼런스
        // 장점/단점 : 직관적 / 무기를 먼저 로딩하고 이 함수를 부르는걸 까먹으면, 에러가 남
        // 예 : 이전 세이브 게임을 로드했는데, 로딩 UI에 무기를 레퍼했는데 아직 인스턴스 안됨
        // 오더 순서만 기억하면 괜찮음.

        // 방법2 :  무기의 아이템 ID를 요구하며, 데이터베이스에서 무기를 가져와 무기 icon으로.
        // 장점 : 항상 무기 ID가 있기 때문에, 유저를 기다릴 필요가 없음.
        // 단점 : 직관적이지 않음.
        // 작동순서를 기억하지 않는다면 훌륭함. (요 방식으로 감)

        WeaponItem weapon = WorldItemDatabase.Instance.GetWeaponByID(weaponID);
        if (weapon == null)
        {
            Debug.Log("ITEM IS NULL");
            leftWeaponQuickSlotIcon.enabled = false;
            leftWeaponQuickSlotIcon.sprite = null;
            return;
        }

        if (weapon.itemIcon == null)
        {
            Debug.Log("ITEM NO ICON");
            leftWeaponQuickSlotIcon.enabled = false;
            leftWeaponQuickSlotIcon.sprite = null;
            return;
        }

        // UI를 

        leftWeaponQuickSlotIcon.sprite = weapon.itemIcon;
        leftWeaponQuickSlotIcon.enabled = true;
    }
}
