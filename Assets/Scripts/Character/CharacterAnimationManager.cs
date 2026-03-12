using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TDA.Core.Events; // AnimatorParameterHash 참조

namespace TDA.Character
{
    /// <summary>
    /// [P1] 캐릭터의 애니메이션 실행과 물리적 뼈대 조작을 전담하는 순수 제어자(Driver) 클래스입니다.
    /// 레거시 호출 함수들을 모두 덜어내고, 오직 Hash 기반의 네트워크 동기화 애니메이션 재생만 담당합니다.
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

        // 인스펙터 편집 편의성을 위해 문자열로 열어두지만, 런타임에서는 해시로 변환하여 사용합니다.
        [Header("Damage Animations (Inspector String Setup)")]
        [SerializeField] string hit_Forward_Medium_01 = "Hit_Forward_Medium_01";
        [SerializeField] string hit_Forward_Medium_02 = "Hit_Forward_Medium_02";
        [SerializeField] string hit_Backward_Medium_01 = "Hit_Backward_Medium_01";
        [SerializeField] string hit_Backward_Medium_02 = "Hit_Backward_Medium_02";
        [SerializeField] string hit_Left_Medium_01 = "Hit_Left_Medium_01";
        [SerializeField] string hit_Left_Medium_02 = "Hit_Left_Medium_02";
        [SerializeField] string hit_Right_Medium_01 = "Hit_Right_Medium_01";
        [SerializeField] string hit_Right_Medium_02 = "Hit_Right_Medium_02";

        // 런타임 최적화를 위한 Hash 리스트
        [HideInInspector] public List<int> forward_Medium_Damage_Hashes = new List<int>();
        [HideInInspector] public List<int> backward_Medium_Damage_Hashes = new List<int>();
        [HideInInspector] public List<int> left_Medium_Damage_Hashes = new List<int>();
        [HideInInspector] public List<int> right_Medium_Damage_Hashes = new List<int>();

        protected virtual void Awake()
        {
            character = GetComponent<CharacterManager>();
        }

        protected virtual void Start()
        {
            // 인스펙터의 문자열 세팅을 런타임 시작 시 단 1회 해시(int)로 변환하여 캐싱합니다. (퍼포먼스 향상)
            forward_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Forward_Medium_01));
            forward_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Forward_Medium_02));

            backward_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Backward_Medium_01));
            backward_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Backward_Medium_02));

            left_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Left_Medium_01));
            left_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Left_Medium_02));

            right_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Right_Medium_01));
            right_Medium_Damage_Hashes.Add(Animator.StringToHash(hit_Right_Medium_02));
        }

        // =========================================================================================
        // [핵심 추가] Funnel & Root Motion 통합 라우터 (Data-Driven Architecture)
        // =========================================================================================
        /// <summary>
        /// 하드코딩된 파라미터 전달을 폐기하고, SO 데이터를 읽어 모든 플래그를 자동으로 셋팅하는 단일 깔때기(Funnel) 메서드입니다.
        /// </summary>
        /// <param name="logicalStateName">애니메이터의 상태 이름 (SO와 매핑됨)</param>
        /// <param name="customAnimSet">특정 무기 등 커스텀 SO 세트 (Null일 경우 Base 세트 사용)</param>
        public virtual void PlayTargetAction(string logicalStateName, Animation.CharacterAnimationSetSO customAnimSet = null)
        {
            Animation.CharacterAnimationSetSO targetSet = customAnimSet != null ? customAnimSet : baseAnimationSet;

            if (targetSet == null)
            {
                Debug.LogWarning($"<color=yellow>[Animation Funnel]</color> 적용할 AnimationSetSO가 없습니다. Fallback 재생합니다. State: {logicalStateName}");
                PlayTargetAnimation(Animator.StringToHash(logicalStateName), true, true, false, false);
                return;
            }

            Animation.AnimationEventParamsSO actionParams = targetSet.GetParamsForState(logicalStateName);

            if (actionParams == null)
            {
                Debug.LogWarning($"<color=yellow>[Animation Funnel]</color> '{logicalStateName}' 상태에 매핑된 SO 데이터를 찾을 수 없습니다. Fallback 재생합니다.");
                PlayTargetAnimation(Animator.StringToHash(logicalStateName), true, true, false, false);
                return;
            }

            // SO 데이터로 캐릭터의 물리 및 제어 상태 플래그 덮어쓰기 (Data-Driven)
            character.isPerformingAction = actionParams.isPerformingAction;
            character.canRotate = actionParams.canRotate;
            character.canMove = actionParams.canMove;
            character.animator.applyRootMotion = actionParams.applyRootMotion;

            // 해시 변환 및 실제 재생
            int targetHash = Animator.StringToHash(logicalStateName);
            lastAnimationPlayedHash = targetHash;
            character.animator.CrossFade(targetHash, 0.2f);

            // 네트워크 동기화 (서버에 SO 플래그 동기화)
            if (character.characterNetworkManager != null)
            {
                character.characterNetworkManager.NotifyTheServerOfActionAnimationServerRpc(
                    NetworkManager.Singleton.LocalClientId,
                    targetHash,
                    actionParams.applyRootMotion);
            }
        }
        // =========================================================================================

        public int GetRandomAnimationFromList(List<int> animationHashList)
        {
            List<int> finalList = new List<int>();

            foreach (var item in animationHashList)
            {
                finalList.Add(item);
            }

            finalList.Remove(lastAnimationPlayedHash);

            for (int i = finalList.Count - 1; i > -1; i--)
            {
                if (finalList[i] == 0)
                {
                    finalList.RemoveAt(i);
                }
            }

            if (finalList.Count == 0) return 0;

            int randomValue = Random.Range(0, finalList.Count);
            return finalList[randomValue];
        }

        public void UpdateAnimatorMovementParameters(float horizontalValue, float verticalValue, bool isSprinting)
        {
            float snappedHorizontal = 0f;
            float snappedVertical = 0f;

            // [안전성 강화] 부등호 범위를 단순화하여 조이스틱/키보드의 소수점 오차(-1.0001f 등)를 완벽히 캡처합니다.
            if (horizontalValue > 0 && horizontalValue <= 0.5f) { snappedHorizontal = 0.5f; }
            else if (horizontalValue > 0.5f) { snappedHorizontal = 1f; }
            else if (horizontalValue < 0 && horizontalValue >= -0.5f) { snappedHorizontal = -0.5f; }
            else if (horizontalValue < -0.5f) { snappedHorizontal = -1f; }

            if (verticalValue > 0 && verticalValue <= 0.5f) { snappedVertical = 0.5f; }
            else if (verticalValue > 0.5f) { snappedVertical = 1f; }
            else if (verticalValue < 0 && verticalValue >= -0.5f) { snappedVertical = -0.5f; }
            else if (verticalValue < -0.5f) { snappedVertical = -1f; }

            // moveAmount 계산: 입력의 절대값을 합쳐 0~1 사이로 정규화합니다.
            float moveAmount = Mathf.Clamp01(Mathf.Abs(horizontalValue) + Mathf.Abs(verticalValue));

            if (isSprinting)
            {
                snappedVertical = 2;
                moveAmount = 2f;
            }

            // 디버깅을 위해 인스펙터 노출용 변수에 현재 값 캐싱
            debugHorizontalValue = snappedHorizontal;
            debugVerticalValue = snappedVertical;
            debugMoveAmountValue = moveAmount;

            // Hash를 사용하여 애니메이터 파라미터 갱신 (GC 제로)
            character.animator.SetFloat(AnimatorParameterHash.Horizontal, snappedHorizontal, locomotionDampTime, Time.deltaTime);
            character.animator.SetFloat(AnimatorParameterHash.Vertical, snappedVertical, locomotionDampTime, Time.deltaTime);

            // [수정 완료] Speed가 아닌, 기획된 애니메이터 파라미터인 moveAmount를 정확히 송출합니다.
            character.animator.SetFloat(AnimatorParameterHash.moveAmount, moveAmount, locomotionDampTime, Time.deltaTime);
            character.animator.SetBool(AnimatorParameterHash.isMoving, moveAmount > 0);
        }

        // =========================================================================================
        // [신규 아키텍처: Funnel 패턴] 능동적 액션 (onAction -> DoAttack 등으로 치환 가능)
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

            // 디버깅의 핵심: 누가 언제 어떤 액션을 발생시켰는지 역추적 가능
            Debug.Log($"<color=cyan>[Action Funnel]</color> Executing Action State: {targetActionIndex}");

            character.animator.SetInteger(AnimatorParameterHash.ActionState, targetActionIndex);

            // 전역 AnimatorParameterHash에 명시된 onAction (능동 액션) 트리거 사용
            character.animator.SetTrigger(AnimatorParameterHash.onAction);
        }

        // =========================================================================================
        // [신규 아키텍처: Funnel 패턴] 수동적 리액션 (onHit) - 절대적 인터럽트
        // =========================================================================================
        public virtual void PlayTargetHitReactionFunnel(int targetHitIndex)
        {
            character.isPerformingAction = true;
            character.animator.applyRootMotion = true;
            character.canRotate = false;
            character.canMove = false;

            Debug.Log($"<color=red>[Hit Funnel]</color> Executing Hit Reaction: {targetHitIndex}");

            character.animator.SetInteger(AnimatorParameterHash.ActionState, targetHitIndex);

            // 전역 AnimatorParameterHash에 명시된 onHit (피격) 트리거 사용
            character.animator.SetTrigger(AnimatorParameterHash.onHit);
        }

        // =========================================================================================
        // [레거시 호환용] 기존 스크립트(IK, Locomotion 등)에서 쓰던 CrossFade 방식 보존
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