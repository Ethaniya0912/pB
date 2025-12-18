using UnityEngine;
using Unity.Netcode;

namespace SG
{
    /// <summary>
    /// [Dev A] 절단 가능한 오브젝트의 속성을 정의하는 컴포넌트
    /// Dev B는 이 컴포넌트의 변수들을 체크하여 자를 수 있는지 판단합니다.
    /// </summary>
    public class SliceableObject : NetworkBehaviour
    {
        [Header("Slicing Properties")]
        [Tooltip("절단면 내부의 재질 (고기 단면, 나무 단면 등)")]
        public Material crossSectionMaterial;

        [Header("Constraints")]
        [Tooltip("최대 절단 가능 횟수 (너무 잘게 잘리는 것 방지)")]
        [SerializeField] private int maxSliceCount = 5;

        [Tooltip("현재 절단된 횟수 (동기화 변수)")]
        public NetworkVariable<int> currentSliceCount = new NetworkVariable<int>(0);

        [Header("Weapon Restriction")]
        [Tooltip("이 오브젝트를 자르기 위해 필요한 무기 태그 (예: Knife, Axe)")]
        public WeaponType requiredWeaponType = WeaponType.Knife; // Enums.cs에 정의 필요, 임시로 Knife로 가정

        [Header("Physics")]
        public float separationForce = 100f; // 절단 시 벌어지는 힘

        /// <summary>
        /// 절단 가능 여부를 반환합니다.
        /// </summary>
        public bool CanBeSliced(WeaponItem weaponItem)
        {
            // 1. 횟수 제한 체크
            if (currentSliceCount.Value >= maxSliceCount)
            {
                Debug.Log("더 이상 자를 수 없습니다. (최대 횟수 도달)");
                return false;
            }

            // 2. 무기 타입 체크 (Dev B가 만든 WeaponItem SO에 'WeaponType'이나 'Tag'가 있다고 가정)
            // 예시: if (weaponItem.weaponType != this.requiredWeaponType) return false;
            // *현재 WeaponItem 구조에 맞게 커스텀 필요*

            return true;
        }

        #region Object-Specific Network Logic (Architecture Exception)

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void IncrementSliceCountServerRpc()
        {
            if (currentSliceCount.Value < maxSliceCount)
            {
                currentSliceCount.Value++;
            }
        }

        #endregion

    }

    // (참고) Enums.cs에 추가가 필요한 부분
    public enum WeaponType
    {
        Generic,
        Knife, // 식칼 등 슬라이싱 특화 무기
        Blunt  // 둔기
    }
}