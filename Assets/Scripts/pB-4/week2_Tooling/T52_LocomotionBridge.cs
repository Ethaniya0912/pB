// =============================================================================
// T52_LocomotionBridge.cs  |  pB-4 Week 2 Day 5 T5.2  v5.11
//                          |  v5.12 [DEBT-18] Week 3 D1 cleanup — DEPRECATED
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
// [DEBT-18 v1 cleanup — Week 3 D1]
//   - DEPRECATED: 정식 AICharacterLocomotionManager가 동일 GameObject에 부착되면
//     이 컴포넌트는 자동 비활성화됨 (이중 RootMotion 처리 충돌 방지).
//   - verboseLogging 디폴트 OFF (이전 true → 노이즈 발생 원인).
//   - 초기화 로그 verboseLogging 가드.
//   - 진단 로그 주기 2초 → 5초 (노이즈 추가 감소).
//   - [Obsolete] 어트리뷰트로 컴파일 워닝 — Week 4+에서 사용 위치 박제 후 완전 제거 예정.
//
// 시각 검증 도구 전용. 실 게임 씬에선 AICharacterLocomotionManager가 처리.
// =============================================================================
using UnityEngine;
using TDA.Character.AI;          // [DEBT-18] 정식 매니저 감지용

namespace TDA.PB4.Tooling.HumanoidVisual
{
    [RequireComponent(typeof(UnityEngine.AI.NavMeshAgent))]
    [System.Obsolete("DEBT-18: T52_LocomotionBridge는 시각 검증 씬 전용 임시 다리. " +
                     "Week 3+ 실 게임 씬에선 AICharacterLocomotionManager 사용. " +
                     "AICharacterLocomotionManager 부착 시 자동 비활성화됨.", false)]
    public class T52_LocomotionBridge : MonoBehaviour
    {
        private UnityEngine.AI.NavMeshAgent _agent;
        private Animator _animator;
        private static readonly int H_Hash = Animator.StringToHash("Horizontal");
        private static readonly int V_Hash = Animator.StringToHash("Vertical");

        public bool verboseLogging = false;   // [DEBT-18] 디폴트 OFF (이전 true)
        private float _diagTimer = 4.9f;       // [DEBT-18] 5초 주기 (이전 2초)

        private void Start()
        {
            // [DEBT-18 v1 cleanup] 정식 매니저가 동일 GameObject에 부착되었으면 자동 비활성화.
            // AICharacterLocomotionManager가 이미 RootMotion + NavMeshAgent를 처리하므로
            // T52가 동시에 작동하면 nextPosition / Horizontal+Vertical SetFloat 이중 처리로 충돌.
            var locomotionManager = GetComponent<AICharacterLocomotionManager>();
            if (locomotionManager != null)
            {
                if (verboseLogging)
                {
                    Debug.LogWarning(
                        $"[T52_LocomotionBridge] {name}: AICharacterLocomotionManager 발견 → 자동 비활성화 " +
                        $"(DEBT-18 cleanup: 이중 RootMotion 처리 방지). 정식 매니저가 처리합니다.",
                        this);
                }
                enabled = false;
                return;
            }

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

            // [DEBT-18] 초기화 로그 verboseLogging 가드 (이전: 무조건 출력)
            if (verboseLogging)
            {
                Debug.Log($"[T52_LocomotionBridge] {name}: 초기화 (agent={_agent != null}, animator={_animator != null}, applyRoot={_animator?.applyRootMotion})");
            }
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

            // 진단 로그 ([DEBT-18] 5초 간격, 이전 2초)
            if (verboseLogging)
            {
                _diagTimer += Time.deltaTime;
                if (_diagTimer >= 5f)
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
