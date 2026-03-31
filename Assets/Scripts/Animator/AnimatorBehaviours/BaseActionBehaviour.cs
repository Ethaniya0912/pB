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
                //
                // [개선 P-1] 락온/오프 통합 isInLockContext 기반 분기 (최종안)
                //
                //   ─── 판단 기준 변천사 ────────────────────────────────────────────────────────
                //   v1: isLockedOn 여부
                //       문제 → 락오프 상태에서 LockOff Seq 중 공격 시 Seq 가 중단됨
                //
                //   v2: IsSequencePlaying 여부
                //       문제 → Seq Steps 가 빠르게 끝나면 이미 false 가 되어
                //              공격 타이밍에 항상 false 임 (Seq 는 종료됐지만 컨텍스트는 유지 중)
                //
                //   v3: WorldCameraManager.isInLockContext 플래그
                //       PlayerCombatManager 가 락온 ON/OFF 시 명시적으로 세팅합니다.
                //       Seq 종료 여부와 무관하게 "현재 락온/락오프 카메라 컨텍스트인가" 를
                //       정확히 반영합니다.
                //
                //   v4 (현재): isInLockContext 와 무관하게 공격 시 Seq 재생 완전 제거
                //       락온/락오프 모두 공격 시 cameraSequence 를 재생할 이유가 없습니다.
                //       현재 카메라 구도(LockOn/LockOff/탐험)를 그대로 유지하고
                //       임팩트 효과는 eventPoints[i].useOverlay = true 로만 처리합니다.
                //
                //   ─── 분기 규칙 (v4) ──────────────────────────────────────────────────────────
                //   lockOnStanceOverride 설정됨
                //     → ChangeCameraStance() : 현재 구도 유지하면서 Stance 만 교체
                //
                //   lockOnStanceOverride = null (미설정)
                //     → 아무것도 하지 않음 (Seq 도, Stance 도 변화 없음)
                //     → 타격 임팩트는 eventPoints[i].useOverlay = true 로 처리
                //
                //   ─── cameraSequence 필드는? ──────────────────────────────────────────────────
                //   AnimationEventParamsSO.cameraSequence 필드는 삭제하지 않습니다.
                //   공격 액션에서는 더 이상 참조하지 않지만, 가드/패링/피격 등
                //   카메라 구도 전환이 필요한 다른 액션에서 여전히 활용 가능합니다.
                //   (하위 호환 유지 — 기존에 연결된 Seq_LightSlash 등은 무시됨)
                // =========================================================================================
                if (character is TDA.Character.Player.PlayerManager player)
                {
                    if (WorldCameraManager.Instance == null) goto skipCamera;

                    // [개선 P-2] callerName 을 상세하게 구성 — 어느 액션/클립에서 호출됐는지 명확히 기록
                    string clipName = actionParams.targetClip != null ? actionParams.targetClip.name : "NoClip";
                    bool isLockedOn = player.playerNetworkManager != null && player.playerNetworkManager.isLockedOn.Value;
                    bool inLockCtx = WorldCameraManager.Instance.isInLockContext;
                    string callerInfo = $"BaseAction:{actionParams.name}|Clip:{clipName}|LockOn:{isLockedOn}|LockCtx:{inLockCtx}";

                    // ── lockOnStanceOverride 연결 시 → Stance 만 교체 ──────────────────────────
                    // 락온/락오프 무관하게 공격 시 Seq 는 절대 재생하지 않습니다.
                    // 현재 카메라 구도(LockOn/LockOff/탐험)를 그대로 유지합니다.
                    // lockOnStanceOverride 가 연결된 경우에만 Stance 를 교체합니다.
                    // null 이면 → 아무것도 하지 않음. 현재 구도 완전 유지.
                    if (actionParams.lockOnStanceOverride != null)
                    {
                        WorldCameraManager.Instance.ChangeCameraStance(
                            actionParams.lockOnStanceOverride, callerInfo);
                    }
                // else → 아무것도 하지 않음.
                // 타격 임팩트 효과는 OnStateUpdate 의 useOverlay 로 처리합니다.

                skipCamera:;
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

            // =========================================================================================
            // 🚨 [마스터 명세서 v2.0 반영] 애니메이션 진행도(t_norm) 추출 및 브로드캐스트
            // 타격 모션의 템포와 렌즈 FOV 줌인 축소 프레임의 100% 동기화를 달성하기 위해
            // Tracking Window(멀미 방지) 연산에 필요한 현재 애니메이션 진행도(t_norm)를 추출합니다.
            // =========================================================================================
            if (character is TDA.Character.Player.PlayerManager && WorldCameraManager.Instance != null)
            {
                // 추후 WorldCameraManager 내부에 public float currentAnimationNormTime 변수를 추가하시면,
                // 아래 주석을 해제하여 애니메이션 프레임 단위의 완벽한 멀미 방어 추적(Tracking Window)을 활성화할 수 있습니다.
                // WorldCameraManager.Instance.currentAnimationNormTime = currentNormalizedTime;
            }

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

                    // AnimationEventType 이벤트 발행
                    eventManager.NotifyAnimationEvent(point.eventType, sourceInfo);

                    // =============================================================================
                    // [신규] useOverlay 처리 — 타격 순간 카메라 오버레이 효과 발동
                    //
                    // AnimationEventParamsSO.eventPoints 에 useOverlay = true 로 설정하면
                    // 해당 triggerTime 에 WorldCameraManager.PlayOverlayEffect() 가 호출됩니다.
                    //
                    // 이 방식을 쓰면 Seq/Stance 전환 없이 현재 카메라 구도를 유지하면서
                    // 타격 순간에만 순간적인 효과(쉐이크, FOV 킥, 블러)를 얹을 수 있습니다.
                    // 락온 Seq 유지 중 공격 임팩트감을 주는 핵심 수단입니다.
                    //
                    // overlayData 설정 예시 (AnimationEventParamsSO Inspector):
                    //   useOverlay = true
                    //   fovDelta         = -5     (순간 줌인)
                    //   fovBlendIn       = 0.02s
                    //   fovBlendOut      = 0.15s
                    //   shakeIntensity   = 0.3    (카메라 흔들림)
                    //   shakeDuration    = 0.12s
                    //   blurStrengthDelta = 0.2   (모션블러)
                    //   blurDuration     = 0.05s
                    //   blurDecayTime    = 0.2s
                    // =============================================================================
                    if (point.useOverlay
                        && character is TDA.Character.Player.PlayerManager
                        && WorldCameraManager.Instance != null)
                    {
                        WorldCameraManager.Instance.PlayOverlayEffect(
                            point.overlayData,
                            $"AnimEventOverlay:{actionParams.name}|t={point.triggerTime:F2}");
                    }

                    hasEventFired[i] = true;
                }
            }

            // 4. 시간 갱신
            previousNormalizedTime = currentNormalizedTime;
        }
    }
}