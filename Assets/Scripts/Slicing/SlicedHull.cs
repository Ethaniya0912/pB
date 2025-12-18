using UnityEngine;

namespace SG
{
    /// <summary>
    /// [Dev A] 잘려진 파편에 자동으로 부착되는 스크립트
    /// 물리 설정 및 자동 파괴를 담당
    /// </summary>
    public class SlicedHull : MonoBehaviour
    {
        private Rigidbody rb;
        private MeshCollider mc;

        private void Start()
        {
            SetupPhysics();

            // 너무 많은 파편이 남지 않도록 10초 후 제거 (혹은 Object Pooling)
            // Destroy(gameObject, 10f);
        }

        private void SetupPhysics()
        {
            // MeshCollider 설정
            mc = gameObject.AddComponent<MeshCollider>();
            mc.convex = true; // 중요: Convex여야 Rigidbody와 함께 사용 가능
            mc.includeLayers = LayerMask.GetMask("Damage Collider"); // 필요 시 레이어 설정

            // Rigidbody 설정
            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 1f; // 필요 시 원본 질량 비례 계산
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // 무게중심 재계산 (파편의 움직임을 자연스럽게 함)
            rb.ResetCenterOfMass();
        }

        /// <summary>
        /// 절단 직후 파편을 밀어내는 힘을 적용
        /// </summary>
        public void AddExplosionForce(float force, Vector3 position, float radius)
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            rb.AddExplosionForce(force, position, radius);
        }
    }
}