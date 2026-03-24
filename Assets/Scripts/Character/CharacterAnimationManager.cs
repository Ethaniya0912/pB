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
        // 애니메이션 프레임 제한 (Animation Framerate Limiter)
        //
        // 구현 원리 — speed 0/1 토글 방식:
        //   animator.speed = 0f : 이 프레임은 애니메이션 진행 안 함 (정지)
        //   animator.speed = 1f : 이 프레임은 정상 속도로 한 스텝 진행
        //
        //   매 프레임 타이머 += deltaTime
        //   타이머 >= frameInterval(1/targetFPS) → speed=1, 타이머 -= frameInterval
        //   그 외 → speed=0
        //
        //   → 게임 FPS와 무관하게 목표 FPS로만 애니메이션이 스텝됨
        //   → speed=1 구간에서 SetTrigger/CrossFade 등 모든 Animator API 정상 동작
        //   → NGO NetworkAnimator 동기화 유지
        //
        // ❌ 이전 버그 (animator.speed=0 + animator.Update() 조합):
        //   Unity Animator.Update(dt)의 실제 진행량 = dt * animator.speed
        //   speed=0이면 dt를 얼마를 넘겨도 진행량 = 0 → 캐릭터 완전 정지
        // =========================================================================================
        [Header("애니메이션 프레임 제한 (Framerate Limiter)")]
        [Tooltip("켜면 애니메이션이 targetAnimationFPS로 끊겨 재생됩니다.물리·이동은 부드럽게 유지되고 모션만 제한됩니다.")]
        public bool enableAnimationFramerateLimit = true;

        [Tooltip("목표 애니메이션 FPS. 낮을수록 더 많이 끊겨 보입니다.(예: 8 = 극단적, 15 = 역동적, 30 = 기본)")]
        [Range(1, 60)]
        public int targetAnimationFPS = 30;

        // ── 내부 상태 ───────────────────────────────────────────────────────────────────
        private float animationFrameTimer = 0f;
        private bool wasFramerateLimitEnabled = false;

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
        /// 애니메이션 프레임 제한을 처리합니다.
        ///
        /// 구현 원리 — animator.speed 0/1 토글:
        ///   타이머 >= frameInterval(1/targetFPS) 인 프레임에만 speed=1 로 세팅.
        ///   그 외 프레임은 speed=0 으로 세팅.
        ///   Unity가 이 프레임의 Animator를 자동 업데이트할 때
        ///   speed=1이면 정상 진행, speed=0이면 정지되므로
        ///   결과적으로 targetAnimationFPS 간격으로만 한 스텝씩 진행됩니다.
        ///
        /// 이전 버그 원인 (speed=0 + animator.Update() 조합):
        ///   Animator.Update(dt)의 실제 진행량 = dt × animator.speed
        ///   speed=0 상태에서는 dt를 얼마를 넘겨도 진행량이 0이므로 캐릭터가 완전 정지.
        /// </summary>
        private void HandleAnimationFramerateLimit()
        {
            if (enableAnimationFramerateLimit)
            {
                if (!wasFramerateLimitEnabled)
                {
                    animationFrameTimer = 0f;
                    wasFramerateLimitEnabled = true;
                }

                animationFrameTimer += Time.deltaTime;

                float frameInterval = 1f / Mathf.Max(targetAnimationFPS, 1);

                if (animationFrameTimer >= frameInterval)
                {
                    // 이 프레임에서 한 스텝 진행
                    character.animator.speed = 1f;
                    animationFrameTimer -= frameInterval;

                    // 큰 deltaTime으로 타이머가 쌓여도 한 프레임에 한 스텝만 허용.
                    // 초과분은 다음 프레임 타이머로 자연스럽게 이월됩니다.
                    if (animationFrameTimer >= frameInterval)
                        animationFrameTimer = frameInterval - 0.0001f;
                }
                else
                {
                    // 아직 간격 미도달 → 이 프레임은 정지
                    character.animator.speed = 0f;
                }
            }
            else
            {
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