using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponManager : MonoBehaviour
{
    [SerializeField] MeleeWeaponDamageCollider meleeWeaponDamageCollider;

    private void Awake()
    {
        meleeWeaponDamageCollider = GetComponent<MeleeWeaponDamageCollider>();
    }

    public void SetWeaponDamage(WeaponItem weapon)
    {
        meleeWeaponDamageCollider.physicalDamage = weapon.physicalDamage;
        meleeWeaponDamageCollider.elementalDamage = weapon.elementalDamage;
    }
}
