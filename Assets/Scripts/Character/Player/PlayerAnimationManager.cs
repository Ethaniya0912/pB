using System.Collections;
using System.Collections.Generic;
using TDA.Character.Player;
using UnityEngine;
using TDA.Core.Events; // AnimatorParameterHash 참조

namespace TDA.Character
{
    /// <summary>
    /// [P3] 플레이어의 애니메이션 렌더링과 모션 워핑(강제 좌표 이동)의 물리적 집행을 전담하는 클래스입니다.
    ///
    /// [아키텍처 설계 철학]
    /// 1. 단일 책임 원칙 (SRP): 타겟을 찾거나 데미지를 계산하는 뇌(Brain) 역할은 배제하고, 오직 "주어진 좌표로 허용된 시간 내에 이동해라"라는 명령을 완벽하게 렌더링하는 근육(Muscle) 역할만 수행합니다.
    /// 2. 레거시 청소 완료: 기존에 존재하던 EnableCanDoCombo, DisableCanDoCombo 등의 이벤트 수신 콜백 함수들은
    ///    [P1] Pub-Sub 이벤트 통신망 개편에 따라 모두 제거되었으며, 제어권은 PlayerCombatManager로 완전히 이관되었습니다.
    /// 3. [보강] 유니티 애니메이터의 Root Motion을 직접 스크립트로 제어(OnAnimatorMove)하여 캐릭터 컨트롤러와 물리적 충돌을 방지합니다.
    /// 4. [v3.9 수정] 공격(isPerformingAction) 중 루트모션 회전을 카메라에 전달하지 않아
    ///    베기 동작 시 카메라가 함께 꺾이는 현상을 방지합니다.
    ///    PlayerCamera.HandleRotation()의 bodyYaw 동결과 이 수정이 함께 작용하여
    ///    루트모션 회전이 카메라에 이중으로 전달(160%)되던 문제를 완전히 해결합니다.
    /// </summary>
    public class PlayerAnimationManager : CharacterAnimationManager
    {
        [Header("References")]
        [Tooltip("부모 클래스의 character 변수와 별도로 플레이어 전용 기능에 접근하기 위해 캐싱된 참조입니다.")]
        private PlayerManager player;

        [Header("Camera & Turn Compensation")]
        [Tooltip("제자리 Turn 등 이동 루트모션 회전 시 카메라 Yaw를 보정하는 가중치입니다.\n(0=고정, 1=100% 따라감)\n※ 공격(isPerformingAction) 중에는 v3.9 패치에 의해 자동으로 0으로 억제됩니다.")]
        [Range(0f, 1f)] public float cameraTurnCompensationWeight = 0.6f;

        protected override void Awake()
        {
            // 부모 클래스(CharacterAnimationManager)의 초기화 로직(character 캐싱 등)을 먼저 수행합니다.
            base.Awake();

            // 부모 클래스의 character 변수(CharacterManager 타입) 외에
            // 플레이어 전용 기능 제어(CharacterController, Camera, Input 등)를 위해 PlayerManager 타입으로 별도 캐싱합니다.
            player = GetComponent<PlayerManager>();
        }

        // =========================================================================================
        // [물리 연산 보존] Root Motion 제어
        // =========================================================================================

        /// <summary>
        /// 유니티 엔진의 애니메이터 이벤트 생명주기 메서드입니다. (애니메이터가 RootMotion을 갱신할 때마다 자동 호출됨)
        /// 애니메이터의 Root Motion 위치/회전 변화량을 캡처하여, 게임의 실제 Transform 이동(CharacterController)으로 변환합니다.
        /// 기존 물리 기반 이동 로직을 그대로 보존하며, 스크립트 이동과 중첩되는 이중 이동(Double Movement)을 방지하는 핵심 관문입니다.
        /// </summary>
        private void OnAnimatorMove()
        {
            // Root Motion 적용이 허용된 상태이고, 이동을 집행할 CharacterController가 존재할 때만 실행
            if (player.animator.applyRootMotion && player.characterController != null)
            {
                // animator.deltaPosition: 이전 프레임과 현재 프레임 사이의 '애니메이션 자체 이동량'
                Vector3 velocity = player.animator.deltaPosition;

                // [🚨 치명적 버그 수정] 루트 모션이 캐릭터를 땅에 파묻거나 띄우는 것을 원천 차단!
                // Y축(중력)은 오직 PlayerLocomotionManager만 제어해야 합니다.
                velocity.y = 0f;

                // CharacterController.Move를 통해 충돌체(콜라이더) 물리 연산을 고려하며 안전하게 이동시킵니다. 
                player.characterController.Move(velocity);

                // animator.deltaRotation: 애니메이션 자체의 회전 변화량을 현재 회전값에 누적(곱연산)하여 부드럽게 적용합니다.
                Quaternion deltaRotation = player.animator.deltaRotation;
                player.transform.rotation *= deltaRotation;

                // =========================================================================================
                // 🚨 [v3.9 수정] Turn 보정 시 카메라 Yaw 동기화
                //
                // 변경 전: isPerformingAction 여부를 체크하지 않아
                //          베기 루트모션 회전이 카메라 Yaw 에 직접 누적되었음.
                //          + HandleRotation()의 bodyYaw 추적까지 더해져
                //            루트모션 회전의 160%가 카메라에 전달되는 이중 꺾임 발생.
                //
                // 변경 후: 공격(isPerformingAction) 중에는 AdjustCameraYaw() 호출을 차단.
                //          PlayerCamera.AdjustCameraYaw() 내부에서도 동일하게 차단하여
                //          이중 안전망을 구성한다.
                //          Turn 애니메이션(이동 중 방향 전환)은 isPerformingAction=false 이므로
                //          기존과 동일하게 정상 작동한다.
                // =========================================================================================
                bool isCameraCompensationAllowed =
                    cameraTurnCompensationWeight > 0f
                    && player.playerCamera != null
                    && !player.isPerformingAction; // [v3.9] 공격 중 차단 — 이중 꺾임 방지

                if (isCameraCompensationAllowed)
                {
                    float deltaY = deltaRotation.eulerAngles.y;
                    if (deltaY > 180f) deltaY -= 360f;

                    float finalCameraAdjust = deltaY * cameraTurnCompensationWeight;

                    if (Mathf.Abs(finalCameraAdjust) > 0.001f)
                    {
                        // PlayerCamera.AdjustCameraYaw() 내부에서도 isPerformingAction 을 체크하므로
                        // 혹시 타이밍이 어긋나더라도 이중으로 보호된다.
                        player.playerCamera.AdjustCameraYaw(finalCameraAdjust);
                    }
                }
            }
        }

        // =========================================================================================
        // [아날로그 스티어링 완벽 호환] 애니메이터 파라미터 업데이트 덮어쓰기 (Snapping 제거)
        // =========================================================================================
        /// <summary>
        /// Base 클래스의 스냅핑(0, 0.5, 1.0 강제 고정) 로직이 마우스 스티어링의 부드러운 연속값을 
        /// 강제로 끊어지게(방지턱 현상) 만드는 것을 해결하기 위해 함수를 새롭게 정의(new)합니다.
        /// </summary>
        public new void UpdateAnimatorMovementParameters(float horizontalValue, float verticalValue, bool isSprinting)
        {
            // 1. 수직 이동(전/후진)은 키보드 W/S 입력을 위해 기존의 스냅핑 로직을 유지합니다.
            float snappedVertical = 0f;
            if (verticalValue > 0 && verticalValue <= 0.5f) { snappedVertical = 0.5f; }
            else if (verticalValue > 0.5f) { snappedVertical = 1f; }
            else if (verticalValue < 0 && verticalValue >= -0.5f) { snappedVertical = -0.5f; }
            else if (verticalValue < -0.5f) { snappedVertical = -1f; }

            // 2. [핵심 버그 수정] 수평 이동(좌/우 스티어링)은 스냅핑을 완전히 제거하여
            // -1.0 ~ 1.0 사이의 자연스러운 마우스 델타(연속값)를 그대로 살려냅니다!
            float rawHorizontal = horizontalValue;

            float moveAmount = Mathf.Clamp01(Mathf.Abs(rawHorizontal) + Mathf.Abs(verticalValue));

            if (isSprinting)
            {
                snappedVertical = 2f;
                moveAmount = 2f;
            }

            // 3. 파라미터 적용 (dampTime을 통해 애니메이터 내부적으로 부드럽게 보간됨)
            player.animator.SetFloat(AnimatorParameterHash.Horizontal, rawHorizontal, locomotionDampTime, Time.deltaTime);
            player.animator.SetFloat(AnimatorParameterHash.Vertical, snappedVertical, locomotionDampTime, Time.deltaTime);
            player.animator.SetFloat(AnimatorParameterHash.moveAmount, moveAmount, locomotionDampTime, Time.deltaTime);

            // 🚨 [치명적 버그 수정] 이전에 발생했던 'isMoving' 파라미터 누락 에러의 주범입니다.
            // 유저님 애니메이터에는 isMoving이 없으므로 아래 코드는 주석 처리되어야 합니다!
            // player.animator.SetBool("isMoving", moveAmount > 0);
        }

        // =========================================================================================
        // [P3] 모션 워핑(Motion Warping) 물리 집행자 (Animator.MatchTarget 연동)
        // =========================================================================================

        /// <summary>
        /// PlayerCombatManager(에임 어시스트 두뇌)가 계산해 준 최종 안전 좌표를 받아,
        /// 예비 동작 구간(Wind-up) 동안 캐릭터를 스르륵 미끄러지듯 빨아들이는 핵심 워핑 코루틴입니다.
        ///
        /// [프레임 스킵 방어 및 Skating 버그 방지 로직 탑재]
        /// 아무 때나 이동하지 않고 오직 SO에 명시된 시간(예: 0.1~0.4) 안에서만 개입하며, 타격 프레임 직후 강제 종료하여 관성을 보존합니다.
        /// </summary>
        /// <param name="targetPosition">타겟 Bone에서 무기 사거리만큼 후퇴시킨 클리핑(겹침) 방지용 최종 위치</param>
        /// <param name="targetRotation">대상을 완벽히 마주 보는 회전값</param>
        /// <param name="startNormalizedTime">워핑을 허용하기 시작할 애니메이션 시간 (예: 0.1f - 칼을 치켜드는 시점)</param>
        /// <param name="endNormalizedTime">워핑 개입을 멈출 애니메이션 시간 (예: 0.4f - 임팩트가 터지는 시점)</param>
        /// <param name="layerIndex">공격 애니메이션이 재생 중인 레이어 인덱스 (기본값 1: Action Override)</param>
        public IEnumerator WarpToTargetRoutine(Vector3 targetPosition, Quaternion targetRotation, float startNormalizedTime, float endNormalizedTime, int layerIndex = 1)
        {
            // 애니메이터 컴포넌트가 없으면 즉시 코루틴을 종료하여 NullReferenceException을 방지합니다.
            if (player.animator == null) yield break;

            // 1. MatchTargetWeightMask 설정
            // 위치(XYZ Vector3.one)와 회전 모두에 100%(1f)의 가중치를 주어 
            // 워핑 허용 시간 내에 목표 지점을 향해 강력하게 당겨지도록(보간되도록) 설정합니다.
            MatchTargetWeightMask weightMask = new MatchTargetWeightMask(Vector3.one, 1f);

            // 2. 무한 루프를 돌며 렌더링 프레임 단위로 애니메이션 시간을 감시합니다.
            while (true)
            {
                // 🚨 [치명적 버그 수정 1] 하드코딩된 '0'번(Base Layer) 레이어가 아닌, 
                // 실제로 공격이 재생되고 있는 '1'번(Action Override) 레이어의 시간을 읽어오도록 수정했습니다!
                AnimatorStateInfo stateInfo = player.animator.IsInTransition(layerIndex)
                    ? player.animator.GetNextAnimatorStateInfo(layerIndex)
                    : player.animator.GetCurrentAnimatorStateInfo(layerIndex);

                // 🚨 [치명적 버그 수정 2] % 1f (모듈러 연산) 강제 삭제!
                // 공격 애니메이션은 반복(Loop)되지 않는 단발성 모션입니다. % 1f를 쓰면 시간이 1.0을 초과했을 때 
                // 다시 0.01로 변해버려 영원히 루프를 탈출하지 못하는 '스케이팅 버그'가 발생합니다.
                float currentNormalizedTime = stateInfo.normalizedTime;

                // [가장 중요한 타임라인 필터링 방어 로직]

                // 조건 A: 현재 진행도가 지정된 윈드업 구간(예: 10% ~ 40%) 안에 들어왔을 때만 워핑을 실행합니다.
                if (currentNormalizedTime >= startNormalizedTime && currentNormalizedTime <= endNormalizedTime)
                {
                    // 캐릭터의 루트 뼈대(AvatarTarget.Root)를 목표 위치/회전으로 강제로 끌어당겨 스냅시킵니다.
                    // (주의: MatchTarget은 단발성 호출이 아니라, 코루틴 루프 안에서 지속적으로 매 프레임 호출되어야 
                    //  목표 시간(endNormalizedTime)에 도달할 때까지 스무스한 보간이 완성됩니다.)
                    player.animator.MatchTarget(
                        targetPosition,
                        targetRotation,
                        AvatarTarget.Root,
                        weightMask,
                        startNormalizedTime,
                        endNormalizedTime
                    );
                }

                // 조건 B: 타격 프레임(Impact)을 조금이라도 초과하는 순간 즉시 개입을 중단합니다.
                // 만약 이 방어 코드가 없으면 캐릭터가 공격을 마친 후(회수 동작 등)에도 
                // 적에게 계속 들러붙으려 하는 기괴한 스케이팅(Skating) 현상 및 위치 왜곡 버그가 발생합니다.
                if (currentNormalizedTime > endNormalizedTime)
                {
                    break; // 루프 강제 종료. 이로써 공격 후 무기를 휘두르는 동작 특유의 관성과 무게감이 정상적으로 보존됩니다.
                }

                // 다음 렌더링 프레임(Update 직후)까지 대기합니다.
                yield return null;
            }
        }
    }
}