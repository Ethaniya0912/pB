// =============================================================================
// AICharacterLocomotionManager.cs  |  TDA Project
// Layer  : L3 Domain — AI 이동 물리 엔진
//
// 역할:
//   NavMeshAgent (경로 계산 전담) 와 Animator RootMotion (실제 이동 전담) 을
//   통합하여 AI 캐릭터의 물리적 이동을 처리합니다.
//
// 아키텍처 규약:
//   ① CharacterController.Move() 는 OnAnimatorMove() 에서만 호출합니다.
//      NavMeshAgent.updatePosition = false 로 고정 — 경로만 계산합니다.
//   ② NGO 서버 권위형 : Update / OnAnimatorMove 모두 IsServer 게이트 확인.
//   ③ SyncAnimatorParameters() : navMeshAgent.velocity → Animator 파라미터 동기화
//      (Animator 직접 조작 금지 — SetFloat/SetBool 은 이 메서드에서만 허용).
//
// 수정 이력:
//   pB-4  SyncAnimatorParameters()에서 부모 필드 moveAmount 동시 갱신 추가
//         → DetermineApplyRootMotion()이 올바른 값을 읽도록 보장
//         AI 전용 applyRootMotion 설정 추가 (IsOwner 가드 보상)
//         → 부모 CharacterLocomotionManager.Update()의 IsOwner 가드가
//           AI에서 DetermineApplyRootMotion()을 호출하지 않는 문제 해결
//
// 연동:
//   AICharacterManager.Awake() 에서 GetComponent<AICharacterLocomotionManager>() 로
//   자동 참조됩니다.
// =============================================================================
using TDA.Core.Events;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace TDA.Character.AI
{
    /// <summary>
    /// [L3 Domain] AI 전용 이동 엔진입니다.
    /// NavMeshAgent 경로 계산과 Animator RootMotion 실제 이동을 단일 책임으로 통합합니다.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class AICharacterLocomotionManager : CharacterLocomotionManager
    {
        // =====================================================================
        // 내부 참조
        // =====================================================================
        private AICharacterManager aiCharacter;
        private NavMeshAgent navMeshAgent;

        // =====================================================================
        // 이동 설정
        // =====================================================================
        [Header("AI Locomotion Settings")]
        [Tooltip("NavMeshAgent 의 이동 속도가 이 값 이하일 때 Idle 로 판정합니다.")]
        [SerializeField] private float idleVelocityThreshold = 0.1f;

        [Tooltip("Animator moveAmount 파라미터 보간 속도 (낮을수록 부드럽습니다).")]
        [SerializeField] private float animatorSmoothTime = 0.15f;

        // =====================================================================
        // Unity 생명주기
        // =====================================================================
        protected override void Awake()
        {
            base.Awake();

            aiCharacter = GetComponent<AICharacterManager>();
            navMeshAgent = GetComponent<NavMeshAgent>();

            // ──────────────────────────────────────────────────────────────────
            // [핵심 규약] NavMeshAgent 는 경로 계산만 담당합니다.
            // 실제 위치 이동은 OnAnimatorMove() → CharacterController.Move() 가 담당합니다.
            // ──────────────────────────────────────────────────────────────────
            if (navMeshAgent != null)
            {
                navMeshAgent.updatePosition = false; // RootMotion이 위치를 갱신
                navMeshAgent.updateRotation = false; // 회전은 FSM State 또는 RootMotion이 처리
            }
        }

        // =====================================================================
        // Update — Animator 파라미터 동기화 (서버 전용)
        // =====================================================================
        protected override void Update()
        {
            bool isNetworkActive = Unity.Netcode.NetworkManager.Singleton != null &&
                                   Unity.Netcode.NetworkManager.Singleton.IsListening;
            if (isNetworkActive && !aiCharacter.IsServer) return;

            base.Update();

            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.nextPosition = aiCharacter.transform.position;

                // ── [회전 처리] desiredVelocity 방향을 향해 회전 ──────────
                // navMeshAgent.updateRotation = false이므로 수동으로 회전해야 함.
                // 회전하지 않으면:
                //   도주 목적지가 캐릭터 뒤쪽 → localVel.z < 0 → Vertical 음수
                //   → 앞방향 걷기 애니메이션의 deltaPosition이 목적지 반대 방향
                //   → 캐릭터가 목적지에서 멀어지는 현상
                Vector3 moveDir = navMeshAgent.desiredVelocity;
                moveDir.y = 0f;
                if (moveDir.magnitude > 0.1f && aiCharacter.canRotate)
                {
                    Quaternion targetRot = Quaternion.LookRotation(moveDir.normalized);
                    aiCharacter.transform.rotation = Quaternion.RotateTowards(
                        aiCharacter.transform.rotation,
                        targetRot,
                        rotationSpeed * Time.deltaTime * 10f  // 빠른 회전으로 방향 전환
                    );
                }
            }

            SyncAnimatorParameters();

            // ── [진단 로그] 2초마다 핵심 값 출력 ─────────────────────────
#if UNITY_EDITOR
            _diagTimer += Time.deltaTime;
            if (_diagTimer >= 2f)
            {
                _diagTimer = 0f;
                if (navMeshAgent != null)
                {
                    Vector3 dv = navMeshAgent.desiredVelocity;
                    Debug.Log($"<color=yellow>[LocoDiag] {aiCharacter.name}</color>\n" +
                              $"  desiredVel={dv:F3} mag={dv.magnitude:F2}\n" +
                              $"  applyRoot={aiCharacter.animator.applyRootMotion}" +
                              $"  moveAmt={moveAmount:F3}\n" +
                              $"  isOnNavMesh={navMeshAgent.isOnNavMesh}" +
                              $"  hasPath={navMeshAgent.hasPath}" +
                              $"  pathPending={navMeshAgent.pathPending}\n" +
                              $"  pos={aiCharacter.transform.position:F3}" +
                              $"  facing={aiCharacter.transform.forward:F2}\n" +
                              $"  canMove={aiCharacter.canMove}" +
                              $"  isOwner={aiCharacter.IsOwner}" +
                              $"  isServer={aiCharacter.IsServer}");
                }
            }
#endif
        }

        private float _diagTimer = 1.9f; // 게임 시작 0.1초 후 첫 출력

        // =====================================================================
        // OnAnimatorMove — RootMotion 단일 이동 처리 (서버 전용)
        // =====================================================================
        private void OnAnimatorMove()
        {
            bool isNetworkActive = Unity.Netcode.NetworkManager.Singleton != null &&
                                   Unity.Netcode.NetworkManager.Singleton.IsListening;
            if (isNetworkActive && !aiCharacter.IsServer) return;
            if (aiCharacter.characterController == null) return;
            if (!aiCharacter.characterController.enabled) return;
            if (!aiCharacter.canMove) return;

            // ── 루트모션 이동 (원칙: 애니메이션이 이동 속도를 정한다) ──────
            // desiredVelocity * dt 방식은 NavMeshAgent.speed가 이동 속도를 결정하여
            // 애니메이션 재생 속도와 불일치 → 발 미끄러짐 발생. 사용 금지.
            //
            // 올바른 방식: deltaPosition(루트모션 델타)으로 이동.
            // 단, 루프 매치가 빨간색이면 루프 순간 역방향 스파이크 발생.
            // 코드로 해결할 수 없고 애니메이션 설정으로 해결해야 함:
            //   → great_sword_walk 임포트 설정: Loop Pose 체크 (Unity가 루프 강제 스무딩)
            // ────────────────────────────────────────────────────────────────
            if (aiCharacter.animator.applyRootMotion)
            {
                Vector3 rootMotionDelta = aiCharacter.animator.deltaPosition;

#if UNITY_EDITOR
                if (rootMotionDelta.magnitude < 0.0001f)
                {
                    var clips = aiCharacter.animator.GetCurrentAnimatorClipInfo(0);
                    string clipName = clips.Length > 0 ? clips[0].clip?.name : "none";
                    Debug.LogWarning($"[LocoDiag] {aiCharacter.name}: applyRootMotion=true but deltaPosition≈0. clipName={clipName}");
                }
#endif

                // ── Y 제거: 중력을 아래에서 명시적으로 처리 ──────────────────
                rootMotionDelta.y = 0f;

                // ── 중력 수동 계산 ────────────────────────────────────────────
                // HandleAirborneAndGravity()를 호출하면 내부에서 Move(yVelocity*dt)가
                // 먼저 실행된 뒤 Move(rootMotionDelta)가 실행되어 2회 호출이 됩니다.
                // CharacterController는 프레임당 Move() 1회만 보장하므로
                // 2회 호출 시 이동량이 극도로 작아집니다 (진단 로그로 확인됨).
                // → 중력과 루트모션을 합산하여 Move() 1회로 통합합니다.
                bool isGrounded = aiCharacter.characterController.isGrounded;
                if (isGrounded)
                {
                    if (yVelocity.y < 0f) yVelocity.y = groundedGravity;
                }
                else
                {
                    yVelocity.y += gravity * Time.deltaTime;
                }

                // Animator 파라미터 갱신 (isGrounded, verticalVelocity)
                if (aiCharacter.animator != null)
                {
                    aiCharacter.animator.SetBool(AnimatorParameterHash.isGrounded, isGrounded);
                    aiCharacter.animator.SetFloat(AnimatorParameterHash.verticalVelocity, yVelocity.y);
                }

                // ── 단일 Move() 호출: XZ 루트모션 + Y 중력 합산 ─────────────
                aiCharacter.characterController.Move(rootMotionDelta + yVelocity * Time.deltaTime);

                if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
                    navMeshAgent.nextPosition = aiCharacter.transform.position;

                return;
            }

            // ── RootMotion 꺼졌을 때 폴백 (정지 상태 중력 처리) ─────────────
            if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled && navMeshAgent.isOnNavMesh)
            {
                HandleAirborneAndGravity(Vector3.zero);
                navMeshAgent.nextPosition = aiCharacter.transform.position;
            }
        }

        // =====================================================================
        // SyncAnimatorParameters — Animator 파라미터 동기화
        // Animator 직접 조작이 허용되는 유일한 메서드입니다.
        // =====================================================================
        private void SyncAnimatorParameters()
        {
            if (aiCharacter.animator == null) return;
            if (navMeshAgent == null || !navMeshAgent.isActiveAndEnabled) return;

            // NavMeshAgent 속도 크기 → moveAmount (0~1)
            // [버그 수정] navMeshAgent.velocity → navMeshAgent.desiredVelocity 로 변경
            // updatePosition=false 상태에서 velocity는 에이전트가 실제로 자신을 이동시키지
            // 않으므로 0을 반환합니다. desiredVelocity는 경로 계산 결과로 "가고 싶은 속도"를
            // 반환하므로 updatePosition=false 환경에서도 정상 동작합니다.
            float speed = navMeshAgent.desiredVelocity.magnitude;
            float computedMoveAmount = Mathf.Clamp01(speed / Mathf.Max(navMeshAgent.speed, 0.01f));

            // 부드러운 보간으로 애니메이터 파라미터 갱신
            float current = aiCharacter.animator.GetFloat(AnimatorParameterHash.moveAmount);
            float smoothed = Mathf.Lerp(current, computedMoveAmount, Time.deltaTime / animatorSmoothTime);

            aiCharacter.animator.SetFloat(AnimatorParameterHash.moveAmount, smoothed);

            // ── [pB-4 추가] Horizontal / Vertical 파라미터 갱신 ─────────────
            // NavMeshAgent.desiredVelocity를 로컬 좌표로 변환하여
            // Horizontal(좌우), Vertical(앞뒤) 파라미터를 업데이트합니다.
            // 스켈레톤 등 8방향 블렌드트리 기반 애니메이터에서 올바른 방향
            // 애니메이션이 재생되도록 합니다.
            Vector3 worldVelocity = navMeshAgent.desiredVelocity;
            if (worldVelocity.magnitude > 0.01f)
            {
                float navSpeed = Mathf.Max(navMeshAgent.speed, 0.01f);
                Vector3 localVel = aiCharacter.transform.InverseTransformDirection(worldVelocity);
                float horizontal = Mathf.Clamp(localVel.x / navSpeed, -1f, 1f);
                float vertical = Mathf.Clamp(localVel.z / navSpeed, -1f, 1f);

                aiCharacter.animator.SetFloat(AnimatorParameterHash.Horizontal, horizontal,
                    animatorSmoothTime, Time.deltaTime);
                aiCharacter.animator.SetFloat(AnimatorParameterHash.Vertical, vertical,
                    animatorSmoothTime, Time.deltaTime);
            }
            else
            {
                // 정지 시 Horizontal/Vertical을 0으로 부드럽게 수렴
                aiCharacter.animator.SetFloat(AnimatorParameterHash.Horizontal, 0f,
                    animatorSmoothTime, Time.deltaTime);
                aiCharacter.animator.SetFloat(AnimatorParameterHash.Vertical, 0f,
                    animatorSmoothTime, Time.deltaTime);
            }
            // ────────────────────────────────────────────────────────────────

            // ── [pB-4 추가] 부모 필드 moveAmount 동기화 ─────────────────
            // CharacterLocomotionManager.moveAmount (public float) 필드를
            // NavMeshAgent velocity 기반으로 갱신합니다.
            //
            // 기존에는 이 필드가 AI에서 갱신되지 않았습니다.
            // (Player에서만 PlayerLocomotionManager가 입력값 기반으로 갱신)
            //
            // 이 필드가 0이면:
            //   DetermineApplyRootMotion() → moveAmount=0 → return false
            //   → applyRootMotion=false → OnAnimatorMove에서 RootMotion 비활성
            //   → 걷기 애니메이션은 나오지만 실제 이동 안 됨
            //
            // velocity 기반으로 갱신하면 DetermineApplyRootMotion()이
            // 올바른 값을 읽어 applyRootMotion을 정확히 결정합니다.
            // ────────────────────────────────────────────────────────────
            moveAmount = computedMoveAmount;

            // ── [pB-4 추가] AI 전용 applyRootMotion 보상 ────────────────
            // 부모 CharacterLocomotionManager.Update()의
            // DetermineApplyRootMotion()은 if(character.IsOwner) 가드 안에 있어
            // AI(IsOwner=false)에서는 호출되지 않습니다.
            //
            // AI에서도 동일한 로직을 실행하여 applyRootMotion을 올바르게 설정합니다.
            // - isPerformingAction 중이면 true (공격/피격 RootMotion 유지)
            // - moveAmount > 0이면 useRootMotionForLocomotion 설정값 따름
            // - 정지 시 false (미세 슬라이딩 방지)
            //
            // IsServer 가드 안에서 실행되므로 서버 권위형 규약을 준수합니다.
            // ────────────────────────────────────────────────────────────
            if (!aiCharacter.IsOwner)
            {
                aiCharacter.animator.applyRootMotion = DetermineApplyRootMotion();
            }
        }
    }
}