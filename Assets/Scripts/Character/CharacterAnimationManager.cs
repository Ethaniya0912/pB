using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TDA.Core.Events;

namespace TDA.Character
{
    /// <summary>
    /// [P1] 캐릭터의 애니메이션 실행과 물리적 뼈대 조작을 전담하는 순수 제어자(Driver) 클래스입니다.
    /// 레거시 호출 함수들을 점진적으로 덜어내고, 오직 SO 데이터 기반의 깔때기(Funnel) 재생만 담당하도록 고도화되었습니다.
    /// </summary>
    public class CharacterAnimationManager : MonoBehaviour
    {
        protected CharacterManager character;

        [Header("Animation Data (SO Funnel)")]
        [Tooltip("캐릭터의 기본 애니메이션 세트 SO입니다. (무기 미장착 시 턴, 회피 등 공통 모션)")]
        public Animation.CharacterAnimationSetSO baseAnimationSet;

        [Header("Animation State Tracking")]
        [Tooltip("중복 재생 방지를 위해 마지막으로 재생된 애니메이션의 해시값을 캐싱합니다.")]
        public int lastAnimationPlayedHash;

        [Header("Locomotion Damping (Task 8)")]
        [Tooltip("기본 이동 시 블렌드 트리 파라미터가 목표치에 도달하는 댐핑 시간입니다. (값이 클수록 묵직하고 유기적인 가감속)")]
        [SerializeField] protected float locomotionDampTime = 0.1f;

        [Header("Debug - Current Animator Parameters")]
        [Tooltip("현재 애니메이터로 쏴주고 있는 Horizontal (스냅 적용) 값입니다.")]
        [SerializeField] private float debugHorizontalValue;
        [Tooltip("현재 애니메이터로 쏴주고 있는 Vertical (스냅 적용) 값입니다.")]
        [SerializeField] private float debugVerticalValue;
        [Tooltip("현재 계산되어 애니메이터로 쏴주고 있는 moveAmount 값입니다.")]
        [SerializeField] private float debugMoveAmountValue;

        [Header("Damage Animations (Inspector String Setup)")]
        [SerializeField] string hit_Forward_Medium_01 = "Hit_Forward_Medium_01";
        [SerializeField] string hit_Forward_Medium_02 = "Hit_Forward_Medium_02";
        [SerializeField] string hit_Backward_Medium_01 = "Hit_Backward_Medium_01";
        [SerializeField] string hit_Backward_Medium_02 = "Hit_Backward_Medium_02";
        [SerializeField] string hit_Left_Medium_01 = "Hit_Left_Medium_01";
        [SerializeField] string hit_Left_Medium_02 = "Hit_Left_Medium_02";
        [SerializeField] string hit_Right_Medium_01 = "Hit_Right_Medium_01";
        [SerializeField] string hit_Right_Medium_02 = "Hit_Right_Medium_02";

        [HideInInspector] public List<int> forward_Medium_Damage_Hashes = new List<int>();
        [HideInInspector] public List<int> backward_Medium_Damage_Hashes = new List<int>();
        [HideInInspector] public List<int> left_Medium_Damage_Hashes = new List<int>();
        [HideInInspector] public List<int> right_Medium_Damage_Hashes = new List<int>();

        // =========================================================================================
        // 스톱모션(스파이더버스) 프레임 제한 — 수정된 구현
        //
        // ❌ 기존 문제점 3가지:
        //   1. animator.enabled = false → 모든 Animator API(SetTrigger, CrossFade 등) 무효화.
        //      PlayTargetActionFunnel / HitReactionFunnel 이 아예 동작하지 않는다.
        //   2. animator.Update(animationFrameTimer) — 누적 시간을 넘겨서 한 번에 큰 값이 들어감.
        //      animator.Update()는 '이 스텝에서 진행할 델타'를 받아야 한다.
        //   3. enabled=false 상태에서 Update() 호출 → Unity 버전에 따라 무시되거나 에러 발생.
        //
        // ✅ 올바른 방법:
        //   - animator.enabled 는 절대 건드리지 않는다.
        //   - animator.speed = 0f 로 자동 진행을 멈춘다.
        //     → SetFloat / SetTrigger / CrossFade 등은 speed=0에서도 정상 동작.
        //     → NGO NetworkAnimator 동기화도 유지된다.
        //   - 목표 간격(frameInterval)마다 animator.Update(frameInterval)을 수동 호출.
        //     → 넘기는 값이 항상 일정해서 모션 속도가 균일하게 끊긴다.
        // =========================================================================================
        [Header("Animation Framerate Limiter (Stop-motion Effect)")]
        [Tooltip("애니메이션 프레임을 강제 제한하여 스톱모션 효과를 낼지 여부입니다.\n" +
                 "물리·이동은 부드럽게 유지되고 팔다리 모션만 끊깁니다.")]
        public bool enableAnimationFramerateLimit = false;

        [Tooltip("목표 애니메이션 FPS (예: 8=극단적 스톱모션, 12=스파이더버스, 24=필름)")]
        [Range(1, 60)]
        public int targetAnimationFPS = 30;

        // ── 내부 상태 ───────────────────────────────────────────────────────────────────
        private float animationFrameTimer = 0f;   // 다음 수동 Update까지 남은 시간
        private bool wasFramerateLimitEnabled = false; // 이전 프레임 활성화 여부 (전환 감지)

        // =========================================================================================

        protected virtual void Awake()
        {
            character = GetComponent<CharacterManager>();
        }

        protected virtual void Start()
        {
            forward_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Forward_Medium_01));
            forward_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Forward_Medium_02));

            backward_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Backward_Medium_01));
            backward_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Backward_Medium_02));

            left_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Left_Medium_01));
            left_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Left_Medium_02));

            right_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Right_Medium_01));
            right_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Right_Medium_02));
        }

        protected virtual void Update()
        {
            if (character == null || character.animator == null) return;

            HandleAnimationFramerateLimit();
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// 스톱모션 프레임 제한을 처리합니다.
        ///
        /// 핵심 원리:
        ///   - animator.speed = 0f  : Unity가 매 프레임 자동으로 애니메이션을 진행하는 것을 멈춤.
        ///   - animator.Update(dt)  : 우리가 원하는 간격마다 정확히 frameInterval만큼 수동 진행.
        ///   - 결과: 게임 FPS와 완전히 독립된 일정한 스텝으로 애니메이션이 끊긴다.
        ///
        /// 주의: animator.enabled 는 절대 false로 바꾸지 않는다.
        ///   → false가 되면 SetTrigger/SetFloat 등 모든 파라미터 API가 무효화된다.
        /// </summary>
        private void HandleAnimationFramerateLimit()
        {
            if (enableAnimationFramerateLimit)
            {
                // 방금 활성화됐을 때만 초기화
                if (!wasFramerateLimitEnabled)
                {
                    // ✅ speed=0: 자동 진행 멈춤. enabled는 true 유지 → API 정상 동작
                    character.animator.speed = 0f;
                    animationFrameTimer = 0f;
                    wasFramerateLimitEnabled = true;
                }

                animationFrameTimer += Time.deltaTime;

                float frameInterval = 1f / Mathf.Max(targetAnimationFPS, 1);

                if (animationFrameTimer >= frameInterval)
                {
                    // ✅ Update()에는 '이 스텝에서 진행할 정확한 델타'를 넘긴다.
                    //    frameInterval로 고정해야 모션 속도가 균일하게 유지된다.
                    //    (animationFrameTimer를 넘기면 누적값이 들어가 속도가 불균일해진다.)
                    character.animator.Update(frameInterval);

                    // 자투리 시간을 보존해 다음 스텝 타이밍이 밀리지 않도록 처리
                    animationFrameTimer -= frameInterval;

                    // 만약 한 프레임에 여러 스텝이 쌓였다면 (예: 히치로 deltaTime이 매우 큼)
                    // 누적 스텝을 모두 소화한다 (드롭 없이 처리)
                    while (animationFrameTimer >= frameInterval)
                    {
                        character.animator.Update(frameInterval);
                        animationFrameTimer -= frameInterval;
                    }
                }
            }
            else
            {
                // 비활성화 전환 시 speed를 1로 복원
                if (wasFramerateLimitEnabled)
                {
                    character.animator.speed = 1f;
                    animationFrameTimer = 0f;
                    wasFramerateLimitEnabled = false;
                }
            }
        }

        // =========================================================================================
        // [핵심] Funnel & Root Motion 통합 라우터
        // =========================================================================================
        public virtual void PlayTargetAction(
            string logicalStateName,
            Animation.CharacterAnimationSetSO customAnimSet = null)
        {
            Animation.CharacterAnimationSetSO targetSet =
                customAnimSet != null ? customAnimSet : baseAnimationSet;

            if (targetSet == null)
            {
                Debug.LogWarning($"<color=yellow>[Animation Funnel]</color> AnimationSetSO 없음. Fallback. State: {logicalStateName}");
                PlayTargetAnimation(Animator.StringToHash(logicalStateName), true, true, false, false);
                return;
            }

            Animation.AnimationEventParamsSO actionParams = targetSet.GetParamsForState(logicalStateName);

            if (actionParams == null)
            {
                Debug.LogWarning($"<color=yellow>[Animation Funnel]</color> '{logicalStateName}' SO 데이터 없음. Fallback.");
                PlayTargetAnimation(Animator.StringToHash(logicalStateName), true, true, false, false);
                return;
            }

            character.isPerformingAction = actionParams.isPerformingAction;
            character.canRotate = actionParams.canRotate;
            character.canMove = actionParams.canMove;
            character.animator.applyRootMotion = actionParams.applyRootMotion;

            int targetHash = Animator.StringToHash(logicalStateName);
            lastAnimationPlayedHash = targetHash;
            character.animator.CrossFade(targetHash, 0.2f);

            if (character.characterNetworkManager != null)
            {
                character.characterNetworkManager.NotifyTheServerOfActionAnimationServerRpc(
                    NetworkManager.Singleton.LocalClientId,
                    targetHash,
                    actionParams.applyRootMotion);
            }
        }

        public int GetRandomAnimationFromList(List<int> animationHashList)
        {
            List<int> finalList = new List<int>(animationHashList);
            finalList.Remove(lastAnimationPlayedHash);
            finalList.RemoveAll(h => h == 0);
            if (finalList.Count == 0) return 0;
            return finalList[Random.Range(0, finalList.Count)];
        }

        public void UpdateAnimatorMovementParameters(
            float horizontalValue,
            float verticalValue,
            bool isSprinting)
        {
            float snappedHorizontal = 0f;
            float snappedVertical = 0f;

            if (horizontalValue > 0 && horizontalValue <= 0.5f) snappedHorizontal = 0.5f;
            else if (horizontalValue > 0.5f) snappedHorizontal = 1f;
            else if (horizontalValue < 0 && horizontalValue >= -0.5f) snappedHorizontal = -0.5f;
            else if (horizontalValue < -0.5f) snappedHorizontal = -1f;

            if (verticalValue > 0 && verticalValue <= 0.5f) snappedVertical = 0.5f;
            else if (verticalValue > 0.5f) snappedVertical = 1f;
            else if (verticalValue < 0 && verticalValue >= -0.5f) snappedVertical = -0.5f;
            else if (verticalValue < -0.5f) snappedVertical = -1f;

            float moveAmount = Mathf.Clamp01(
                Mathf.Abs(horizontalValue) + Mathf.Abs(verticalValue));

            if (isSprinting)
            {
                snappedVertical = 2f;
                moveAmount = 2f;
            }

            debugHorizontalValue = snappedHorizontal;
            debugVerticalValue = snappedVertical;
            debugMoveAmountValue = moveAmount;

            character.animator.SetFloat(AnimatorParameterHash.Horizontal, snappedHorizontal, locomotionDampTime, Time.deltaTime);
            character.animator.SetFloat(AnimatorParameterHash.Vertical, snappedVertical, locomotionDampTime, Time.deltaTime);
            character.animator.SetFloat(AnimatorParameterHash.moveAmount, moveAmount, locomotionDampTime, Time.deltaTime);
        }

        // =========================================================================================
        // Funnel — 트리거 기반 상태 전이
        // =========================================================================================
        public virtual void PlayTargetActionFunnel(
            int targetActionIndex,
            bool isPerformAction = true,
            bool applyRootMotion = true,
            bool canRotate = false,
            bool canMove = false)
        {
            character.isPerformingAction = isPerformAction;
            character.animator.applyRootMotion = applyRootMotion;
            character.canRotate = canRotate;
            character.canMove = canMove;

            Debug.Log($"<color=cyan>[Action Funnel]</color> ActionState: {targetActionIndex}");

            character.animator.SetInteger(AnimatorParameterHash.ActionState, targetActionIndex);
            character.animator.ResetTrigger(AnimatorParameterHash.onAction);
            character.animator.SetTrigger(AnimatorParameterHash.onAction);
        }

        public virtual void PlayTargetHitReactionFunnel(int targetHitIndex)
        {
            character.isPerformingAction = true;
            character.animator.applyRootMotion = true;
            character.canRotate = false;
            character.canMove = false;

            Debug.Log($"<color=red>[Hit Funnel]</color> HitReaction: {targetHitIndex}");

            character.animator.SetInteger(AnimatorParameterHash.ActionState, targetHitIndex);
            character.animator.ResetTrigger(AnimatorParameterHash.onHit);
            character.animator.SetTrigger(AnimatorParameterHash.onHit);
        }

        // =========================================================================================
        // 레거시 호환용
        // =========================================================================================
        public virtual void PlayTargetAnimation(
            int targetAnimHash,
            bool isPerformingAction,
            bool applyRootMotion = true,
            bool canRotate = false,
            bool canMove = false)
        {
            lastAnimationPlayedHash = targetAnimHash;
            character.animator.applyRootMotion = applyRootMotion;
            character.animator.CrossFade(targetAnimHash, 0.2f);
            character.isPerformingAction = isPerformingAction;
            character.canRotate = canRotate;
            character.canMove = canMove;

            if (character.characterNetworkManager != null)
            {
                character.characterNetworkManager.NotifyTheServerOfActionAnimationServerRpc(
                    NetworkManager.Singleton.LocalClientId,
                    targetAnimHash,
                    applyRootMotion);
            }
        }

        public virtual void PlayTargetAttackActionAnimation(
            global::AttackType attackType,
            int targetAnimHash,
            bool isPerformingAction,
            bool applyRootMotion = true,
            bool canRotate = false,
            bool canMove = false)
        {
            character.characterCombatManager.currentAttackType = attackType;
            character.characterCombatManager.lastAttackAnimationPerformedHash = targetAnimHash;
            lastAnimationPlayedHash = targetAnimHash;

            character.animator.applyRootMotion = applyRootMotion;
            character.animator.CrossFade(targetAnimHash, 0.2f);
            character.isPerformingAction = isPerformingAction;
            character.canRotate = canRotate;
            character.canMove = canMove;

            if (character.characterNetworkManager != null)
            {
                character.characterNetworkManager.NotifyTheServerOfAttackActionAnimationServerRpc(
                    NetworkManager.Singleton.LocalClientId,
                    targetAnimHash,
                    applyRootMotion);
            }
        }
    }
}