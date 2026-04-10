// =============================================================================
// AICharacterAnimationManager.cs  |  TDA Project
// Layer  : L4 View — AI 전용 애니메이션 드라이버
//
// 역할:
//   CharacterAnimationManager 를 상속하여 AI 캐릭터에 특화된 애니메이션
//   제어 로직을 담당합니다.
//
// 아키텍처 규약:
//   ① Animator 직접 조작(SetFloat/SetBool)은 이 클래스와
//      AICharacterLocomotionManager.SyncAnimatorParameters() 에서만 허용합니다.
//   ② PlayTargetActionFunnel / PlayTargetAnimation 등 공통 재생 메서드는
//      부모(CharacterAnimationManager)를 그대로 사용합니다.
//   ③ NGO 서버 권위형: IsServer 게이트가 필요한 로직은 AICharacterManager 를
//      거쳐 호출됩니다. 이 클래스에서는 직접 IsServer 를 체크하지 않습니다.
//   ④ OnAnimatorMove 는 AICharacterLocomotionManager 가 단독으로 처리합니다.
//      이 클래스에서 CharacterController.Move() 를 호출하지 않습니다.
//
// 연동:
//   AICharacterManager.Awake() 에서 characterAnimationManager 에 업캐스팅됩니다.
//   AICharacterCombatManager 가 characterAnimationManager.PlayTargetActionFunnel()
//   을 통해 공격·방어·위빙 모션을 요청합니다.
// =============================================================================
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using TDA.Core.Events;

namespace TDA.Character.AI
{
    /// <summary>
    /// [L4 View] AI 전용 애니메이션 드라이버.
    /// CharacterAnimationManager 의 공통 재생 메서드를 그대로 상속하며,
    /// AI 특화 파라미터 동기화와 isLockedOn·이동 방향 처리를 추가합니다.
    /// </summary>
    public class AICharacterAnimationManager : CharacterAnimationManager
    {
        // =====================================================================
        // 내부 참조
        // =====================================================================
        private AICharacterManager _ai;
        private NavMeshAgent _nav;

        // =====================================================================
        // 이동 파라미터 보간 설정
        // =====================================================================
        [Header("AI Animation Smoothing")]
        [Tooltip("moveAmount 파라미터 보간 시간. 값이 클수록 움직임이 묵직해집니다.")]
        [SerializeField] private float moveSmoothTime = 0.12f;

        [Tooltip("isLockedOn 전환 시 strafing 블렌드 보간 시간.")]
        [SerializeField] private float lockOnSmoothTime = 0.15f;

        // =====================================================================
        // Unity 생명주기
        // =====================================================================
        protected override void Awake()
        {
            base.Awake(); // CharacterAnimationManager.Awake() — character 캐싱

            _ai = GetComponent<AICharacterManager>();
            _nav = GetComponent<NavMeshAgent>();
        }

        protected override void Update()
        {
            // 베이스의 프레임 제한 업데이트 실행 (AnimatorFramerateLimit 등)
            base.Update();

            // 이동 파라미터 동기화는 AICharacterLocomotionManager.SyncAnimatorParameters()
            // 가 담당합니다. 여기서는 락온 여부에 따른 보조 파라미터만 처리합니다.
            // [pB-4 수정] 오프라인/테스트 씬에서는 IsSpawned=false이므로
            // NetworkManager가 활성화된 경우에만 IsSpawned를 체크합니다.
            bool isNetworkActive = Unity.Netcode.NetworkManager.Singleton != null &&
                                   Unity.Netcode.NetworkManager.Singleton.IsListening;
            if (_ai == null || (isNetworkActive && !_ai.IsSpawned)) return;

            SyncLockOnParameter();
        }

        // =====================================================================
        // isLockedOn 파라미터 동기화
        // =====================================================================
        /// <summary>
        /// NetworkVariable isLockedOn 을 Animator isLockedOn 파라미터에 반영합니다.
        /// 전투 스트레이핑·락온 블렌드 트리에 사용됩니다.
        /// </summary>
        private void SyncLockOnParameter()
        {
            if (character.animator == null) return;
            if (character.characterNetworkManager == null) return;

            bool locked = character.characterNetworkManager.isLockedOn.Value;
            character.animator.SetBool(AnimatorParameterHash.isLockedOn, locked);
        }

        // =====================================================================
        // AI 전용 — 이동 방향 파라미터 직접 설정
        //
        // AICharacterLocomotionManager 가 moveAmount 를 처리하지만,
        // 전투 스트레이핑처럼 horizontal/vertical 분리가 필요한 경우에는
        // 이 메서드를 통해 명시적으로 설정합니다.
        // =====================================================================
        /// <summary>
        /// 전투 스트레이핑 시 Horizontal / Vertical / moveAmount 파라미터를 갱신합니다.
        /// NavMeshAgent 속도 기반 자동 동기화(SyncAnimatorParameters)와 병행 사용 가능합니다.
        /// </summary>
        public void SetMovementParameters(float horizontal, float vertical, bool isSprinting = false)
        {
            if (character.animator == null) return;

            float move = Mathf.Clamp01(Mathf.Abs(horizontal) + Mathf.Abs(vertical));
            if (isSprinting) move = 2f;

            character.animator.SetFloat(AnimatorParameterHash.Horizontal, horizontal, moveSmoothTime, Time.deltaTime);
            character.animator.SetFloat(AnimatorParameterHash.Vertical, vertical, moveSmoothTime, Time.deltaTime);
            character.animator.SetFloat(AnimatorParameterHash.moveAmount, move, moveSmoothTime, Time.deltaTime);
        }

        // =====================================================================
        // AI 전용 — grounded 파라미터 갱신 (중력 시스템 연동)
        // =====================================================================
        /// <summary>
        /// AICharacterLocomotionManager.HandleAirborneAndGravity() 에서
        /// isGrounded 상태가 변경될 때 호출합니다.
        /// </summary>
        public void SetGroundedParameter(bool isGrounded, float verticalVelocity)
        {
            if (character.animator == null) return;
            character.animator.SetBool(AnimatorParameterHash.isGrounded, isGrounded);
            character.animator.SetFloat(AnimatorParameterHash.verticalVelocity, verticalVelocity);
        }

        // =====================================================================
        // 디버거 연동 (베이스 클래스에 이미 #if UNITY_EDITOR 후킹이 있으므로
        //             AICharacterManagerDebugger 가 자동으로 기록됩니다.)
        // 추가 override 없이 베이스 메서드를 그대로 사용합니다.
        // =====================================================================
    }
}