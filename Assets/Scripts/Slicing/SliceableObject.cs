using UnityEngine;
using Unity.Netcode;

namespace SG
{
    public class SliceableObject : NetworkBehaviour
    {
        [Header("Slicing Properties")]
        public Material crossSectionMaterial;

        [Header("Constraints")]
        [SerializeField] private int maxSliceCount = 5;

        // [동기화 변수] 원본(서버 스폰 객체)용 - 이름을 변경하여 직접 접근 방지
        public NetworkVariable<int> networkSliceCount = new NetworkVariable<int>(0);

        // [로컬 변수] 파편(로컬 생성 객체)용
        // TD : 추후 동기화 문제 발생하거나 중복연산, 게임성 지장있을 시 로컬>네트워크 통합 검토할 것 25.12.19
        // TD : 현재 파편은 네트워크 오브젝트로 스폰되지 않으므로 로컬 변수로 유지 25.12.19
        [SerializeField] private int localSliceCount = 0;

        [Header("Weapon Restriction")]
        public WeaponType requiredWeaponType = WeaponType.Knife;

        [Header("Cooking Interaction")]
        [Tooltip("요리 냄비 등에 들어갈 때 식별될 아이템 데이터 (예: 썰린 당근)")]
        public Item ingredientItem;

        [Header("Physics")]
        public float separationForce = 100f;

        // [핵심 API] 외부(MeshSlicer 등)에서는 이 프로퍼티로 현재 카운트를 확인합니다.
        public int CurrentSliceCount
        {
            get
            {
                // 네트워크에 스폰된 상태라면 NetworkVariable 사용
                if (IsSpawned) return networkSliceCount.Value;
                // 로컬 오브젝트(파편)라면 로컬 변수 사용
                return localSliceCount;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // 서버에서 로컬 값이 설정되어 있었다면 동기화 변수에 반영
            if (IsServer && localSliceCount > 0)
            {
                networkSliceCount.Value = localSliceCount;
            }
        }

        // [핵심 API] MeshSlicer가 파편 생성 시 호출
        public void SetSliceCount(int count)
        {
            localSliceCount = count;
            if (IsSpawned && IsServer)
            {
                networkSliceCount.Value = count;
            }
        }

        public bool CanBeSliced(WeaponItem weaponItem)
        {
            if (CurrentSliceCount >= maxSliceCount)
            {
                return false;
            }
            // 무기 타입 체크 로직 (필요시 구현)
            // if (weaponItem.weaponType != requiredWeaponType) return false;

            return true;
        }

        // [핵심 API] SlicingDamageCollider에서 호출
        public void IncrementSliceCount()
        {
            if (IsSpawned)
            {
                IncrementSliceCountRpc();
            }
            else
            {
                localSliceCount++;
            }
        }

        // 요리 기구(Pot, Pan) 상호작용 시 호출: 재료로 사용되고 오브젝트 제거
        public void ConsumeAsIngredient()
        {
            if (IsSpawned && IsServer)
            {
                // 네트워크 오브젝트인 경우 Despawn
                NetworkObject.Despawn();
            }
            else
            {
                // 로컬 오브젝트인 경우 Destroy
                Destroy(gameObject);
            }
        }

        // NGO 2.0 RPC (RequireOwnership 파라미터 제거됨)
        [Rpc(SendTo.Server)]
        private void IncrementSliceCountRpc()
        {
            if (networkSliceCount.Value < maxSliceCount)
            {
                networkSliceCount.Value++;
            }
        }
    }

    // (만약 Enums.cs에 없다면 필요)
    public enum WeaponType
    {
        Generic,
        Knife,
        Blunt
    }
}