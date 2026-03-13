using UnityEngine;

namespace TDA.Items
{
    /// <summary>
    /// [Data Layer] 방어와 패링, 처냄(Deflect)에 특화된 방패 무기 데이터입니다.
    /// WeaponItem을 상속받아 왼손 무기 슬롯에 호환되며, 쉴드 배쉬(공격) 기능도 그대로 사용할 수 있습니다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewShieldWeapon", menuName = "TDA/Items/Weapons/Shield Weapon")]
    public class ShieldWeaponItemSO : WeaponItem // (※ 프로젝트의 무기 SO 부모 클래스 이름에 맞게 수정하세요)
    {
        [Header("Defense & Block Settings")]
        [Tooltip("방어 시 물리 데미지 컷 비율 (1.0 = 100% 방어, 0.5 = 50% 방어)")]
        [Range(0f, 1f)] public float physicalBlockAbsorption = 1.0f;

        [Tooltip("방어 시 마법 데미지 컷 비율")]
        [Range(0f, 1f)] public float magicBlockAbsorption = 0.5f;

        [Header("Durability & Guard Break (P0-02)")]
        [Tooltip("방패의 최대 내구도. 방어 시 깎이며 0이 되면 GuardBroken 상태가 됩니다.")]
        public float maxDurability = 100f;

        [Tooltip("처냄(Deflect) 저항력. 이 수치보다 높은 ImpactForce를 가진 무기(Heavy 등)의 공격을 막으면 방패가 튕겨집니다.")]
        public float deflectionResistance = 50f;

        [Header("Parry Settings")]
        [Tooltip("이 방패로 패링을 시도할 때 적용되는 패링 유효 프레임(초 단위)")]
        public float parryWindowDuration = 0.2f;
    }
}