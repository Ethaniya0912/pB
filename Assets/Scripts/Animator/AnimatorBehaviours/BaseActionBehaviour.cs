using UnityEngine;
using TDA.Character.Animation;
using TDA.Character;

namespace TDA.AnimatorBehaviours
{
    /// <summary>
    /// [P1 & P4] 애니메이션 프레임 스킵 방어형 중앙 감시자 (Observer)
    /// 
    /// 이 스크립트는 1차 데이터 SO(AnimationEventParamsSO)를 읽어들여, 
    /// 델타 타임 트래킹을 통해 프레임 드랍 환경에서도 Enum 이벤트를 100% 안전하게 발송하며,
    /// 캐릭터의 물리/상태 플래그를 Data-Driven 방식으로 안전하게 덮어씌웁니다.
    /// </summary>
    public class BaseActionBehaviour : StateMachineBehaviour
    {
        [Header("Data Source")]
        public AnimationEventParamsSO actionParams;

        protected CharacterEventManager eventManager;
        protected CharacterManager character; // 캐릭터 상태 참조 캐싱

        protected float previousNormalizedTime;
        protected bool[] hasEventFired;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (eventManager == null)
            {
                eventManager = animator.GetComponentInParent<CharacterEventManager>();
            }
            if (character == null)
            {
                character = animator.GetComponentInParent<CharacterManager>();
            }

            previousNormalizedTime = 0f;

            if (actionParams != null && actionParams.eventPoints != null)
            {
                hasEventFired = new bool[actionParams.eventPoints.Count];

                // 🚨 [누락 복구] SO에 등록해둔 물리/상태 플래그를 캐릭터에 즉시 덮어씌웁니다. (Data-Driven 제어)
                if (character != null)
                {
                    character.isPerformingAction = actionParams.isPerformingAction;
                    character.animator.applyRootMotion = actionParams.applyRootMotion;
                    character.canRotate = actionParams.canRotate;
                    character.canMove = actionParams.canMove;
                }
            }
            else
            {
                hasEventFired = new bool[0];
            }
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (eventManager == null || actionParams == null || hasEventFired == null) return;

            // 1. 루프 정규화 보정
            float currentNormalizedTime = stateInfo.normalizedTime % 1f;

            // 2. 루프 스와이프 감지 및 플래그 초기화
            if (currentNormalizedTime < previousNormalizedTime)
            {
                for (int i = 0; i < hasEventFired.Length; i++) hasEventFired[i] = false;
                previousNormalizedTime = 0f;
            }

            // 3. 델타 타임 트래킹 (구간 교차 검증)
            // 프레임 스킵이 발생해도 이전 시간과 현재 시간 사이에 이벤트 지점이 있었다면 무조건 발동
            for (int i = 0; i < actionParams.eventPoints.Count; i++)
            {
                var point = actionParams.eventPoints[i];

                // 🚨 [버그 수정] previousNormalizedTime <= point.triggerTime 으로 변경
                // (부등호가 < 이면 0.0초에 등록된 시작 이벤트가 영원히 씹히는 현상 방어)
                if (!hasEventFired[i] &&
                    previousNormalizedTime <= point.triggerTime &&
                    currentNormalizedTime >= point.triggerTime)
                {
                    // [디버깅 확장] 이벤트를 쏘는 출처(SO 이름, 클립 이름, 타이밍 정보)를 상세히 작성합니다.
                    string clipName = actionParams.targetClip != null ? actionParams.targetClip.name : "Unknown Clip";
                    string sourceInfo = $"SO: {actionParams.name} | Clip: {clipName} | Time: {point.triggerTime:F2}";

                    // [P4 개선] 해시 대신 강력한 타입의 Enum과 상세 출처 정보를 함께 송출
                    eventManager.NotifyAnimationEvent(point.eventType, sourceInfo);
                    hasEventFired[i] = true;
                }
            }

            // 4. 시간 갱신
            previousNormalizedTime = currentNormalizedTime;
        }

        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // 강제 캔슬 시 안전망이 필요하다면 여기에 추가 (예: 필수 Disable 이벤트 강제 발송)
        }
    }
}