using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

namespace SG
{
    public class SlicingDamageCollider : MeleeWeaponDamageCollider
    {
        public enum SlicingAxis
        {
            Right,      // X축 (빨강)
            Up,         // Y축 (초록)
            Forward     // Z축 (파랑)
        }

        [Header("Slicing Logic")]
        public float sliceExplosionForce = 200f;

        [Tooltip("무기 모델 기준, 칼날의 넓은 면이 향하는 축")]
        public SlicingAxis slicingAxis = SlicingAxis.Right;

        [Header("Debug")]
        [Tooltip("체크 시, 한 번의 공격으로 연속해서 자를 수 있습니다. (테스트용: 기본값 false 권장)")]
        public bool debugContinuousSlicing = false;

        // 중복 절단 방지용 리스트
        private HashSet<GameObject> alreadySlicedObjects = new HashSet<GameObject>();

        // Collider 상태 추적용 변수
        private Collider _collider;
        private bool _wasColliderEnabled = false;

        protected override void Awake()
        {
            base.Awake();
            // 부모 클래스 로직 외에 직접 Collider 참조 캐싱
            _collider = GetComponent<Collider>();
        }

        private void OnEnable()
        {
            // 스크립트가 처음 켜질 때 초기화
            alreadySlicedObjects.Clear();
        }

        private void FixedUpdate()
        {
            // [핵심 수정] Collider 컴포넌트가 꺼졌다가 켜지는 순간(새로운 공격 시작)을 감지
            if (_collider != null)
            {
                // 이번 프레임에 켜져있고, 이전 프레임에 꺼져있었다면 -> 새로운 스윙
                if (_collider.enabled && !_wasColliderEnabled)
                {
                    alreadySlicedObjects.Clear();
                    // Debug.Log("[SlicingCollider] New Swing Detected - List Cleared");
                }

                // 상태 업데이트
                _wasColliderEnabled = _collider.enabled;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (characterCausingDamage != null)
            {
                Gizmos.color = Color.yellow;
                Vector3 normal = GetPlaneNormal();
                Gizmos.DrawRay(transform.position, normal * 0.5f);
                Gizmos.DrawWireSphere(transform.position + normal * 0.5f, 0.05f);
            }
        }

        protected override void OnTriggerEnter(Collider other)
        {
            // 1. 중복 절단 방지 체크
            if (!debugContinuousSlicing && alreadySlicedObjects.Contains(other.gameObject))
            {
                return;
            }

            SliceableObject targetSliceable = other.GetComponent<SliceableObject>();

            if (targetSliceable != null)
            {
                WeaponItem currentWeapon = characterCausingDamage.characterInventoryManager.currentRightHandWeapon;

                // 2. 절단 가능 여부(횟수 등) 체크
                if (targetSliceable.CanBeSliced(currentWeapon))
                {
                    PerformSlice(other.gameObject, targetSliceable, other);
                }
            }
        }

        private Vector3 GetPlaneNormal()
        {
            switch (slicingAxis)
            {
                case SlicingAxis.Right: return transform.right;
                case SlicingAxis.Up: return transform.up;
                case SlicingAxis.Forward: return transform.forward;
                default: return transform.right;
            }
        }

        private void PerformSlice(GameObject target, SliceableObject sliceableProps, Collider other)
        {
            // 리스트 등록 (연속 절단 방지)
            alreadySlicedObjects.Add(target);

            Vector3 hitPoint = other.ClosestPoint(transform.position);
            Vector3 planeNormal = GetPlaneNormal();

            MeshSlicer.SlicedHullResult result = MeshSlicer.Slice(
                target,
                hitPoint,
                planeNormal,
                sliceableProps.crossSectionMaterial
            );

            if (result != null)
            {
                // 생성된 파편도 이번 스윙에서는 다시 잘리지 않도록 등록
                alreadySlicedObjects.Add(result.UpperHull);
                alreadySlicedObjects.Add(result.LowerHull);

                sliceableProps.IncrementSliceCount();
                target.SetActive(false);

                HandleHullsPhysics(result, target.transform.parent);
            }
        }

        private void HandleHullsPhysics(MeshSlicer.SlicedHullResult result, Transform originalParent)
        {
            GameObject upper = result.UpperHull;
            GameObject lower = result.LowerHull;

            Vector3 playerRight = characterCausingDamage.transform.right;
            Vector3 toUpper = upper.transform.position - characterCausingDamage.transform.position;
            Vector3 toLower = lower.transform.position - characterCausingDamage.transform.position;

            float dotUpper = Vector3.Dot(playerRight, toUpper.normalized);
            float dotLower = Vector3.Dot(playerRight, toLower.normalized);

            GameObject fixedPart = (dotUpper < dotLower) ? upper : lower;
            GameObject fallingPart = (dotUpper < dotLower) ? lower : upper;

            // 고정 파편
            Rigidbody fixedRb = fixedPart.GetComponent<Rigidbody>();
            if (fixedRb != null) fixedRb.isKinematic = true;
            if (originalParent != null) fixedPart.transform.SetParent(originalParent);

            // 낙하 파편
            Rigidbody fallingRb = fallingPart.GetComponent<Rigidbody>();
            if (fallingRb != null)
            {
                fallingRb.isKinematic = false;
                fallingRb.useGravity = true;

                Vector3 forceDir = Vector3.Cross(GetPlaneNormal(), Vector3.up).normalized;
                if (forceDir == Vector3.zero) forceDir = transform.forward;
                fallingRb.AddForce((forceDir + Vector3.up) * sliceExplosionForce);
            }
        }
    }
}