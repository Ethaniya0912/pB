// =============================================================================
// T52_LocomotionBridge.cs  |  pB-4 Week 2 Day 5 T5.2  v5.11
//
// 역할: T5.2 시각 검증 씬 전용 임시 NavMesh→Animator 다리.
//   사용자 코드베이스의 AICharacterLocomotionManager가 부착해야 하는 매니저
//   체인 (AICharacterManager 등) 의존성 없이, NavMeshAgent + Animator만으로
//   Souls-like 표준 패턴 (RootMotion + Horizontal/Vertical blend tree)을 작동시킴.
//
// 핵심 패턴 (AICharacterLocomotionManager.cs:73-100 차용):
//   1. NavMeshAgent.updatePosition=false  (RootMotion이 transform 갱신)
//   2. NavMeshAgent.updateRotation=false  (수동 회전)
//   3. 매 프레임 nextPosition = transform.position (RootMotion 변화 알림)
//   4. desiredVelocity → 회전 + Horizontal/Vertical SetFloat
//
// 시각 검증 도구 전용. 실 게임 씬에선 AICharacterLocomotionManager가 처리.
// =============================================================================
using UnityEngine;

namespace TDA.PB4.Tooling.HumanoidVisual
{
    [RequireComponent(typeof(UnityEngine.AI.NavMeshAgent))]
    public class T52_LocomotionBridge : MonoBehaviour
    {
        private UnityEngine.AI.NavMeshAgent _agent;
        private Animator _animator;
        private static readonly int H_Hash = Animator.StringToHash("Horizontal");
        private static readonly int V_Hash = Animator.StringToHash("Vertical");

        public bool verboseLogging = true;
        private float _diagTimer = 1.9f;

        private void Start()
        {
            _agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            _animator = GetComponent<Animator>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>();

            if (_agent != null)
            {
                _agent.updatePosition = false;  // RootMotion이 위치 처리
                _agent.updateRotation = false;  // 수동 회전
            }

            if (_animator != null)
            {
                _animator.applyRootMotion = true;  // Root Motion 강제 활성
            }

            Debug.Log($"[T52_LocomotionBridge] {name}: 초기화 (agent={_agent != null}, animator={_animator != null}, applyRoot={_animator?.applyRootMotion})");
        }

        private void Update()
        {
            if (_agent == null || _animator == null) return;
            if (!_agent.isActiveAndEnabled || !_agent.isOnNavMesh) return;

            // RootMotion이 transform 갱신 → NavMeshAgent에 알림
            _agent.nextPosition = transform.position;

            Vector3 worldVel = _agent.desiredVelocity;
            worldVel.y = 0f;

            // 회전 (목적지 향해 부드럽게)
            if (worldVel.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(worldVel.normalized);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, 360f * Time.deltaTime);
            }

            // World velocity → local → Animator (정규화 -1~1)
            Vector3 localVel = transform.InverseTransformDirection(worldVel);
            float maxSpeed = _agent.speed > 0.1f ? _agent.speed : 6f;
            float h = localVel.x / maxSpeed;
            float v = localVel.z / maxSpeed;
            _animator.SetFloat(H_Hash, h);
            _animator.SetFloat(V_Hash, v);

            // 진단 로그 (2초 간격)
            if (verboseLogging)
            {
                _diagTimer += Time.deltaTime;
                if (_diagTimer >= 2f)
                {
                    _diagTimer = 0f;
                    Debug.Log($"<color=cyan>[T52_LocoBridge] {name}</color> " +
                              $"desiredVel={worldVel.magnitude:F2} H={h:F2} V={v:F2} " +
                              $"speed={_agent.speed:F1} pos={transform.position:F2} " +
                              $"applyRoot={_animator.applyRootMotion}");
                }
            }
        }

        // RootMotion 콜백 — Animator의 deltaPosition이 transform에 반영되도록
        // (applyRootMotion=true이면 자동, 명시적으로 추가 안 해도 작동)
    }
}
