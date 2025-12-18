using UnityEngine;
using Unity.Netcode;

namespace SG
{
    /// <summary>
    /// [Dev B] 무기에 부착되어 실제 절단을 수행하는 콜라이더
    /// 충돌 시 MeshSlicer를 호출하고, 파편의 물리 처리를 담당함 (왼쪽 고정/오른쪽 낙하)
    /// </summary>
    public class SlicingDamageCollider : MeleeWeaponDamageCollider
    {
        [Header("Slicing Logic")]
        [Tooltip("절단 시 가해지는 폭발 힘")]
        public float sliceExplosionForce = 200f;
        [Tooltip("절단 반경")]
        public float sliceExplosionRadius = 1f;

        // 무기의 이전 프레임 위치 (절단면 Normal 계산용)
        private Vector3 previousTipPosition;
        private Transform weaponTip; // 무기 끝부분 트랜스폼 (설정 필요)

        protected override void Awake()
        {
            base.Awake();
            // 무기 끝부분 가상 포인트 설정 (없으면 콜라이더 센터 사용)
            if (weaponTip == null) weaponTip = transform;
        }

        private void FixedUpdate()
        {
            // 무기의 궤적을 추적하기 위해 위치 갱신
            if (weaponTip != null)
            {
                previousTipPosition = weaponTip.position;
            }
        }

        protected override void OnTriggerEnter(Collider other)
        {
            // 1. 기존 데미지 로직 수행 (선택 사항 - 자르면서 데미지도 줄 것인가?)
            // base.OnTriggerEnter(other); 

            // 2. 자를 수 있는 오브젝트인지 확인
            SliceableObject targetSliceable = other.GetComponent<SliceableObject>();

            // SliceableObject가 있고, 아직 자를 수 있는 횟수가 남아있는지 확인
            // (WeaponItem 정보는 부모 컴포넌트 등을 통해 가져와야 함. 여기서는 로직 단순화를 위해 생략하거나 characterCausingDamage에서 조회)
            if (targetSliceable != null)
            {
                // 무기 조건 체크 (Dev A가 만든 CanBeSliced 활용)
                // 현재 장착된 무기 정보를 가져오는 로직이 필요함.
                WeaponItem currentWeapon = characterCausingDamage.characterInventoryManager.currentRightHandWeapon;

                if (targetSliceable.CanBeSliced(currentWeapon))
                {
                    PerformSlice(other.gameObject, targetSliceable, other);
                }
            }
        }

        private void PerformSlice(GameObject target, SliceableObject sliceableProps, Collider other)
        {
            // 1. 절단면(Plane) 계산
            // 충돌 지점: 현재 무기 위치와 타겟 위치 사이 근사값, 혹은 Collider.ClosestPoint 사용
            Vector3 hitPoint = other.ClosestPoint(transform.position);

            // 절단면의 법선(Normal): 무기의 진행 방향(Velocity)과 무기의 위쪽(Up) 벡터의 외적 등 활용
            // 또는 간단히: 무기가 휘두러지는 궤적의 수직 방향
            Vector3 velocityVector = (weaponTip.position - previousTipPosition).normalized;
            Vector3 planeNormal = Vector3.Cross(velocityVector, transform.up).normalized;

            // 안전장치: 벡터가 0이면 기본값 사용
            if (planeNormal == Vector3.zero) planeNormal = Vector3.up;

            // 2. Dev A의 MeshSlicer 호출 (핵심)
            MeshSlicer.SlicedHullResult result = MeshSlicer.Slice(
                target,
                hitPoint,
                planeNormal,
                sliceableProps.crossSectionMaterial
            );

            if (result != null)
            {
                // 3. 원본 제거 (또는 비활성화)
                target.SetActive(false); // NGO 환경이면 Despawn 처리 필요

                // 슬라이스 카운트 증가 (ServerRpc 호출)
                sliceableProps.IncrementSliceCountServerRpc();

                // 4. 왼쪽/오른쪽 판별 및 물리 처리 (요청 사항 구현)
                HandleHullsPhysics(result, target.transform.parent);
            }
        }

        /// <summary>
        /// [핵심 로직] 플레이어 시점에서 왼쪽 파편은 고정하고 오른쪽은 날려버림
        /// </summary>
        private void HandleHullsPhysics(MeshSlicer.SlicedHullResult result, Transform originalParent)
        {
            GameObject upper = result.UpperHull;
            GameObject lower = result.LowerHull;

            // 플레이어의 오른쪽 방향 벡터 (기준)
            Vector3 playerRight = characterCausingDamage.transform.right;

            // 각 파편의 위치 벡터 (플레이어 -> 파편)
            Vector3 toUpper = upper.transform.position - characterCausingDamage.transform.position;
            Vector3 toLower = lower.transform.position - characterCausingDamage.transform.position;

            // 내적(Dot Product)을 이용해 좌우 판별
            // 값이 클수록 플레이어의 오른쪽, 작을수록 왼쪽에 위치함
            float dotUpper = Vector3.Dot(playerRight, toUpper.normalized);
            float dotLower = Vector3.Dot(playerRight, toLower.normalized);

            GameObject fixedPart; // 왼손에 남을 부분 (왼쪽)
            GameObject fallingPart; // 떨어져 나갈 부분 (오른쪽)

            if (dotUpper < dotLower) // Upper가 더 왼쪽에 있음
            {
                fixedPart = upper;
                fallingPart = lower;
            }
            else
            {
                fixedPart = lower;
                fallingPart = upper;
            }

            // --- A. 고정된 파편 (Left / Hand Held) 처리 ---
            Rigidbody fixedRb = fixedPart.GetComponent<Rigidbody>();
            if (fixedRb != null)
            {
                fixedRb.isKinematic = true; // 물리 영향 받지 않게 고정
            }

            // 원래 부모(왼손)에 다시 붙여줌
            if (originalParent != null)
            {
                fixedPart.transform.SetParent(originalParent);
                // 필요 시 로컬 위치/회전 조정 로직 추가
            }

            // 고정된 파편은 다시 자를 수 있도록 SliceableObject 상태 유지/초기화가 필요할 수 있음
            // (Dev A의 MeshSlicer가 컴포넌트를 복사해주므로 자동 적용됨)

            // --- B. 떨어지는 파편 (Right) 처리 ---
            Rigidbody fallingRb = fallingPart.GetComponent<Rigidbody>();
            if (fallingRb != null)
            {
                fallingRb.isKinematic = false; // 물리 활성화
                fallingRb.useGravity = true;

                // 폭발 힘 적용 (오른쪽 방향으로 살짝 더 밀어줌)
                Vector3 forceDir = (fallingPart.transform.position - characterCausingDamage.transform.position).normalized;
                fallingRb.AddForce(forceDir * sliceExplosionForce + Vector3.up * 50f);
                fallingRb.AddTorque(Random.insideUnitSphere * sliceExplosionForce);
            }
        }
    }
}