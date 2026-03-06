using System.Collections;
using System.Collections.Generic;
using TDA.Character.Player;
using UnityEngine;


namespace TDA.Character
{
    /// <summary>
    /// [P3] 플레이어의 애니메이션 렌더링과 모션 워핑(강제 좌표 이동)의 물리적 집행을 전담하는 클래스입니다.
    ///
    /// [아키텍처 설계 철학]
    /// 1. 단일 책임 원칙 (SRP): 타겟을 찾거나 데미지를 계산하는 뇌(Brain) 역할은 배제하고, 오직 "주어진 좌표로 허용된 시간 내에 이동해라"라는 명령을 완벽하게 렌더링하는 근육(Muscle) 역할만 수행합니다.
    /// 2. 레거시 청소 완료: 기존에 존재하던 EnableCanDoCombo, DisableCanDoCombo 등의 이벤트 수신 콜백 함수들은
    ///    [P1] Pub-Sub 이벤트 통신망 개편에 따라 모두 제거되었으며, 제어권은 PlayerCombatManager로 완전히 이관되었습니다.
    /// </summary>
    public class PlayerAnimationManager : CharacterAnimationManager
    {
        private PlayerManager player;


        protected override void Awake()
        {
            base.Awake();


            // 부모 클래스의 character 변수(CharacterManager 타입) 외에
            // 플레이어 전용 기능 제어를 위해 PlayerManager 타입으로 별도 캐싱합니다.
            player = GetComponent<PlayerManager>();
        }


        // =========================================================================================
        // [물리 연산 보존] Root Motion 제어
        // =========================================================================================

        /// <summary>
        /// 애니메이터의 Root Motion을 게임의 실제 Transform 이동(CharacterController)으로 변환합니다.
        /// 기존 물리 기반 이동 로직을 그대로 보존합니다.
        /// </summary>
        private void OnAnimatorMove()
        {
            if (player.applyRootMotion && player.characterController != null)
            {
                Vector3 velocity = player.animator.deltaPosition;
                player.characterController.Move(velocity);
                player.transform.rotation *= player.animator.deltaRotation;
            }
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
        public IEnumerator WarpToTargetRoutine(Vector3 targetPosition, Quaternion targetRotation, float startNormalizedTime, float endNormalizedTime)
        {
            if (player.animator == null) yield break;


            // 1. 위치(XYZ)와 회전 모두에 100%(1f)의 가중치를 주어 목표 지점을 향해 강력하게 당겨지도록 설정합니다.
            MatchTargetWeightMask weightMask = new MatchTargetWeightMask(Vector3.one, 1f);


            // 2. 무한 루프를 돌며 렌더링 프레임 단위로 시간을 감시합니다.
            while (true)
            {
                // 트랜지션(CrossFade) 중이라면 다음 상태의 시간을, 아니라면 현재 상태의 시간을 가져와 안전성을 보장합니다.
                AnimatorStateInfo stateInfo = player.animator.IsInTransition(0)
                    ? player.animator.GetNextAnimatorStateInfo(0)
                    : player.animator.GetCurrentAnimatorStateInfo(0);


                // 루프 애니메이션을 대비한 모듈러 정규화 연산 (0.0 ~ 1.0)
                float currentNormalizedTime = stateInfo.normalizedTime % 1f;


                // [가장 중요한 타임라인 필터링 방어 로직]

                // 조건 A: 현재 진행도가 지정된 윈드업 구간(예: 10% ~ 40%) 안에 들어왔을 때만 워핑을 실행합니다.
                if (currentNormalizedTime >= startNormalizedTime && currentNormalizedTime <= endNormalizedTime)
                {
                    // 캐릭터의 루트 뼈대를 강제로 끌어당겨 스냅시킵니다.
                    // (주의: MatchTarget은 루프 안에서 지속적으로 호출되어야 부드러운 보간이 완성됩니다.)
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
                // 만약 이 방어 코드가 없으면 캐릭터가 공격을 마친 후에도 적에게 계속 들러붙으려 하는 기괴한 스케이팅(Skating) 버그가 발생합니다.
                if (currentNormalizedTime > endNormalizedTime)
                {
                    break; // 루프 강제 종료. 이로써 공격 후 휘두르는 동작의 관성과 무게감이 정상적으로 보존됩니다.
                }


                // 다음 렌더링 프레임까지 대기
                yield return null;
            }
        }
    }
}
