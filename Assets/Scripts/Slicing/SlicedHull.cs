using UnityEngine;

namespace SG
{
    public class SlicedHull : MonoBehaviour
    {
        private Rigidbody rb;
        private Collider col; // MeshCollider 혹은 BoxCollider

        // [Fix 1] Start -> Awake 변경: Instantiate 직후 즉시 물리 연산이 가능하도록 함
        private void Awake()
        {
            SetupPhysics();
            //Destroy(gameObject, 10f);
        }

        private void SetupPhysics()
        {
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null) return;

            // 메쉬가 할당되지 않았다면 처리 중단
            if (mf.sharedMesh == null && mf.mesh == null)
            {
                Debug.LogWarning($"[SlicedHull] No mesh found on {name}");
                return;
            }

            Mesh mesh = mf.sharedMesh;

            // [Fix 2] Bounds 재계산 (Scene 뷰 선택 불가 문제 해결)
            mesh.RecalculateBounds();

            // 1차 시도: MeshCollider
            MeshCollider mc = gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true; // Rigidbody 사용시 필수

            // [Fix 3] 안전장치: Convex 생성 실패로 Bounds가 0이면 BoxCollider로 대체
            // (메쉬가 너무 얇거나 기형적일 때 PhysX가 Collider 생성을 거부하는 경우 대비)
            if (mc.bounds.extents == Vector3.zero)
            {
                // Debug.LogWarning($"[SlicedHull] MeshCollider convex failed for {name}. Fallback to BoxCollider.");
                Destroy(mc);

                BoxCollider bc = gameObject.AddComponent<BoxCollider>();
                bc.size = mesh.bounds.size;
                bc.center = mesh.bounds.center;
                col = bc;
            }
            else
            {
                col = mc;
            }

            // Rigidbody 설정
            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // [Fix 4] 작은 파편이 무기를 뚫고 지나가는 현상(Tunneling) 방지
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // 물리 엔진 강제 업데이트
            rb.ResetCenterOfMass();
            rb.WakeUp();
        }

        public void AddExplosionForce(float force, Vector3 position, float radius)
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (rb != null) rb.AddExplosionForce(force, position, radius);
        }
    }
}