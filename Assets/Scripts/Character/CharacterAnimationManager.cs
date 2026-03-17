using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TDA.Core.Events; // AnimatorParameterHash 참조

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
        // [핵심 추가] Funnel & Root Motion 통합 라우터 (Data-Driven Architecture 적용)
        // =========================================================================================
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

            // [정합성 완성] SO 데이터가 물리/상태 제어의 절대적인 기준이 됩니다. (하드코딩 제거)
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

            if (horizontalValue > 0 && horizontalValue <= 0.5f) { snappedHorizontal = 0.5f; }
            else if (horizontalValue > 0.5f) { snappedHorizontal = 1f; }
            else if (horizontalValue < 0 && horizontalValue >= -0.5f) { snappedHorizontal = -0.5f; }
            else if (horizontalValue < -0.5f) { snappedHorizontal = -1f; }

            if (verticalValue > 0 && verticalValue <= 0.5f) { snappedVertical = 0.5f; }
            else if (verticalValue > 0.5f) { snappedVertical = 1f; }
            else if (verticalValue < 0 && verticalValue >= -0.5f) { snappedVertical = -0.5f; }
            else if (verticalValue < -0.5f) { snappedVertical = -1f; }

            float moveAmount = Mathf.Clamp01(Mathf.Abs(horizontalValue) + Mathf.Abs(verticalValue));

            if (isSprinting)
            {
                snappedVertical = 2;
                moveAmount = 2f;
            }

            debugHorizontalValue = snappedHorizontal;
            debugVerticalValue = snappedVertical;
            debugMoveAmountValue = moveAmount;

            // 🚨 [구조 완벽화] 해시(Hash) 변수를 사용하여 파라미터 업데이트 (가비지 컬렉션 및 오타 원천 차단)
            character.animator.SetFloat(AnimatorParameterHash.Horizontal, snappedHorizontal, locomotionDampTime, Time.deltaTime);
            character.animator.SetFloat(AnimatorParameterHash.Vertical, snappedVertical, locomotionDampTime, Time.deltaTime);
            character.animator.SetFloat(AnimatorParameterHash.moveAmount, moveAmount, locomotionDampTime, Time.deltaTime);
        }

        // =========================================================================================
        // [신규 아키텍처: Funnel 패턴] 트리거(Trigger) 기반 상태 전이용
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

            Debug.Log($"<color=cyan>[Action Funnel]</color> Executing Action State: {targetActionIndex}");

            // 🚨 [누락 복구 & 해시 적용] 이전에 씹힌 트리거 찌꺼기(Pending Trigger)를 청소하는 
            // ResetTrigger와 SetTrigger에 모두 안전한 정적(Static) 해시값을 주입합니다!
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

            Debug.Log($"<color=red>[Hit Funnel]</color> Executing Hit Reaction: {targetHitIndex}");

            // 🚨 [누락 복구 & 해시 적용] 피격 시에도 트리거 청소(ResetTrigger) 추가 및 해시 구조화 완비!
            character.animator.SetInteger(AnimatorParameterHash.ActionState, targetHitIndex);
            character.animator.ResetTrigger(AnimatorParameterHash.onHit);
            character.animator.SetTrigger(AnimatorParameterHash.onHit);
        }

        // =========================================================================================
        // [레거시 호환용] 기존 스크립트(IK, Locomotion 등)에서 쓰던 하드코딩 CrossFade 방식
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