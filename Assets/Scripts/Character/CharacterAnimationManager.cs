using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace TDA.Character
{
    /// <summary>
    /// [P1] 캐릭터의 애니메이션 실행과 물리적 뼈대 조작을 전담하는 순수 제어자(Driver) 클래스입니다.
    /// 레거시 호출 함수들을 모두 덜어내고, 오직 Hash 기반의 네트워크 동기화 애니메이션 재생만 담당합니다.
    /// </summary>
    public class CharacterAnimationManager : MonoBehaviour
    {
        protected CharacterManager character;

        [Header("Animation State Tracking")]
        [Tooltip("중복 재생 방지를 위해 마지막으로 재생된 애니메이션의 해시값을 캐싱합니다.")]
        public int lastAnimationPlayedHash;

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

        /// <summary>
        /// 제공된 해시 리스트에서 무작위 애니메이션을 반환합니다. (중복 방지 로직 포함)
        /// </summary>
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
            float snappedHorizontal = horizontalValue;
            float snappedVertical = verticalValue;

            // 속도를 항상 -1, -0.5, 0, 0.5, 1로 수평 움직임 고정 (Snapping)
            if (horizontalValue > 0 && horizontalValue <= 0.5f) { snappedHorizontal = 0.5f; }
            else if (horizontalValue > 0.5f && horizontalValue <= 1) { snappedHorizontal = 1; }
            else if (horizontalValue < 0 && horizontalValue >= -0.5f) { snappedHorizontal = -0.5f; }
            else if (horizontalValue > -0.5f && horizontalValue <= -1) { snappedHorizontal = -1; }
            else { snappedHorizontal = 0; }

            if (verticalValue > 0 && verticalValue <= 0.5f) { snappedVertical = 0.5f; }
            else if (verticalValue > 0.5f && verticalValue <= 1) { snappedVertical = 1; }
            else if (verticalValue < 0 && verticalValue >= -0.5f) { snappedVertical = -0.5f; }
            else if (verticalValue > -0.5f && verticalValue <= -1) { snappedVertical = -1; }
            else { snappedVertical = 0; }

            if (isSprinting)
            {
                snappedVertical = 2;
            }

            character.animator.SetFloat("Horizontal", snappedHorizontal, 0.1f, Time.deltaTime);
            character.animator.SetFloat("Vertical", snappedVertical, 0.1f, Time.deltaTime);
        }

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
            global::AttackType attackType, // [컴파일 에러 해결] global:: 네임스페이스 명시
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