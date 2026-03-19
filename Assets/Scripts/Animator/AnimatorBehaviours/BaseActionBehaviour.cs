using UnityEngine;
using TDA.Character.Animation;
using TDA.Character;
using TDA.World; // [신규] 중앙 관제탑 참조를 위해 추가

namespace TDA.AnimatorBehaviours
{
    /// <summary>
    /// [P1 & P4] 애니메이션 프레임 스킵 방어형 중앙 감시자 (Observer)
    /// 
    /// SO(AnimationEventParamsSO)를 읽어들여 프레임 드랍 환경에서도 Enum 이벤트를 100% 안전하게 발송하며,
    /// 🚨 [추가] 노드 진입 시 카메라 연출 데이터(CameraSequence)를 WorldCameraManager로 토스합니다. (허브 A 역할)
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

            if (actionParams != null)
            {
                // 1. 물리/상태 플래그 데이터 덮어쓰기
                if (character != null)
                {
                    character.isPerformingAction = actionParams.isPerformingAction;
                    character.animator.applyRootMotion = actionParams.applyRootMotion;
                    character.canRotate = actionParams.canRotate;
                    character.canMove = actionParams.canMove;
                }

                // 2. 이벤트 트래커 초기화
                if (actionParams.eventPoints != null)
                {
                    hasEventFired = new bool[actionParams.eventPoints.Count];
                }
                else
                {
                    hasEventFired = new bool[0];
                }

                // =========================================================================================
                // 3. 🚨 [허브 A 발동] 애니메이션 노드 진입 시 카메라 관제탑으로 시퀀스 SO 즉시 발사!
                // =========================================================================================
                if (character is TDA.Character.Player.PlayerManager && actionParams.cameraSequence != null)
                {
                    if (WorldCameraManager.Instance != null)
                    {
                        WorldCameraManager.Instance.PlayCameraSequence(actionParams.cameraSequence);
                        // Debug.Log($"<color=cyan>[Camera Hub A]</color> '{actionParams.name}' 액션 진입! 카메라 시퀀스({actionParams.cameraSequence.name})를 재생합니다.");
                    }
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
            for (int i = 0; i < actionParams.eventPoints.Count; i++)
            {
                var point = actionParams.eventPoints[i];

                if (!hasEventFired[i] &&
                    previousNormalizedTime <= point.triggerTime &&
                    currentNormalizedTime >= point.triggerTime)
                {
                    string clipName = actionParams.targetClip != null ? actionParams.targetClip.name : "Unknown Clip";
                    string sourceInfo = $"SO: {actionParams.name} | Clip: {clipName} | Time: {point.triggerTime:F2}";

                    eventManager.NotifyAnimationEvent(point.eventType, sourceInfo);
                    hasEventFired[i] = true;
                }
            }

            // 4. 시간 갱신
            previousNormalizedTime = currentNormalizedTime;
        }
    }
}