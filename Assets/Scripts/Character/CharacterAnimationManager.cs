using JetBrains.Annotations;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CharacterAnimationManager : MonoBehaviour
{
    CharacterManager character;

    float vertical;
    float horizontal;

    [Header("Damage Animations")]
    public string lastAnimationPlayed; 

    [SerializeField] string hit_Forward_Medium_01 = "Hit_Forward_Medium_01";
    [SerializeField] string hit_Forward_Medium_02 = "Hit_Forward_Medium_02";
    [SerializeField] string hit_Backward_Medium_01 = "Hit_Backward_Medium_01";
    [SerializeField] string hit_Backward_Medium_02 = "Hit_Backward_Medium_02";
    [SerializeField] string hit_Left_Medium_01 = "Hit_Left_Medium_01";
    [SerializeField] string hit_Left_Medium_02 = "Hit_Left_Medium_02";
    [SerializeField] string hit_Right_Medium_01 = "Hit_Right_Medium_01";
    [SerializeField] string hit_Right_Medium_02 = "Hit_Right_Medium_02";

    public List<string> forward_Medium_Damage = new List<string>();
    public List<string> backward_Medium_Damage = new List<string>();
    public List<string> left_Medium_Damage = new List<string>();
    public List<string> right_Medium_Damage = new List<string>();

    protected virtual void Awake()
    {
        character = GetComponent<CharacterManager>();
    }

    protected virtual void Start()
    {
        forward_Medium_Damage.Add(hit_Forward_Medium_01);
        forward_Medium_Damage.Add(hit_Forward_Medium_02);

        backward_Medium_Damage.Add(hit_Backward_Medium_01);
        backward_Medium_Damage.Add(hit_Backward_Medium_02);

        left_Medium_Damage.Add(hit_Left_Medium_01);
        left_Medium_Damage.Add(hit_Left_Medium_02);

        right_Medium_Damage.Add(hit_Right_Medium_01);
        right_Medium_Damage.Add(hit_Right_Medium_02);
    }

    public string GetRandomAnimationFromList(List<string> animationList)
    {
        List<string> finalList = new List<string>();

        // 전달 받은 애니메이션 리스트를 finalList에 넣어 분리, 안전하게 기존 리스트 보존.
        foreach (var item in animationList)
        {
            finalList.Add(item);
        }

        // 리스트에서 플레이된 애니메이션을 체크, 중복실행방지.
        finalList.Remove(lastAnimationPlayed);

        // 리스트에 null 이 있으면 제거.
        for (int i = finalList.Count - 1; i > -1; i--)
        {
            if (finalList[i] == null)
            {
                finalList.RemoveAt(i);
            }
        }

        int randomValue = Random.Range(0, finalList.Count);
        
        return finalList[randomValue];
    }

    public void UpdateAnimatorMovementParameters(float horizontalValue, float verticalValue, bool isSprinting) 
    {
        float snappedHorizontal = horizontalValue;
        float snappedVertical = verticalValue;

        // 속도를 항상 -1,-0.5,0,0.5,1로 수평움직임고정.
        if (horizontalValue > 0 && horizontalValue <= 0.5f)
        {
            snappedHorizontal = 0.5f;
        }
        else if (horizontalValue > 0.5f && horizontalValue <= 1)
        {
            snappedHorizontal = 1;
        }
        else if (horizontalValue < 0 && horizontalValue >= -0.5f)
        {
            snappedHorizontal = -0.5f;
        }
        else if (horizontalValue > -0.5f && horizontalValue <= -1)
        {
            snappedHorizontal = -1;
        }
        else
        {
            snappedHorizontal = 0;
        }

        // 속도를 항상 -1,-0.5,0,0.5,1로 수직움직임고정.
        if (verticalValue > 0 && verticalValue <= 0.5f)
        {
            snappedVertical = 0.5f;
        }
        else if (verticalValue > 0.5f && verticalValue <= 1)
        {
            snappedVertical = 1;
        }
        else if (verticalValue < 0 && verticalValue >= -0.5f)
        {
            snappedVertical = -0.5f;
        }
        else if (verticalValue > -0.5f && verticalValue <= -1)
        {
            snappedVertical = -1;
        }
        else
        {
            snappedVertical = 0;
        }

        if (isSprinting)
        {
            snappedVertical = 2;
        }
        character.animator.SetFloat("Horizontal", snappedHorizontal, 0.1f, Time.deltaTime);
        character.animator.SetFloat("Vertical", snappedVertical, 0.1f, Time.deltaTime);
    }

    public virtual void PlayTargetAnimation(
        string targetAnimation, 
        bool isPerformingAction, 
        bool applyRootMotion = true, 
        bool canRotate = false, 
        bool canMove = false)
    {
        Debug.Log("Playing Animation: " + targetAnimation);
        character.animator.applyRootMotion = applyRootMotion;
        // 0.2초 간격으로 애니메이션을 블랜딩함.
        character.animator.CrossFade(targetAnimation, 0.2f);
        // 캐릭터가 새 애니메이션을 실행하는 걸 방지.
        // 예로, 데미지받은 경우, 데미지 애니메이션 재생
        // 해당 플래그는 스턴되었음으로 참으로 변함.
        // 그럼으로 새 액션을 시도하기 전 해당을 체크할 수 있음.
        character.isPerformingAction =  isPerformingAction;
        character.canRotate = canRotate;
        character.canMove = canMove;

        // 서버/호스트에게 우리가 애니메이션을 플레이하고 있다고 말하고
        // 세션에 있는 모든 사람이 애니메이션 재생.
        character.characterNetworkManager.NotifyTheServerOfActionAnimationServerRpc(
            NetworkManager.Singleton.LocalClientId,
            targetAnimation,
            applyRootMotion);
    }

    public virtual void PlayTargetAttackActionAnimation(AttackType attackType,
    string targetAnimation,
    bool isPerformingAction,
    bool applyRootMotion = true,
    bool canRotate = false,
    bool canMove = false)
    {
        // 마지막 공격을 트랙함(콤보를 위해)
        // 현재 공격 타입을 트랙함(라이트,헤비,ect)-공격이페리될수있거나,스태미나가 
        // 얼마나 드레인되는지 알 수 있어야하며, 데미지콜라이더등 알아야함.
        // 애니메이션을 현재 무기 애니메이션으로 업데이트
        // 네트워크에 "isAttacking" 플래그를 액티브하라 말함 (카운터 데미지등을 위해)
        character.characterCombatManager.currentAttackType = attackType;
        character.characterCombatManager.lastAttackAnimationPerformed = targetAnimation;
        character.animator.applyRootMotion = applyRootMotion;
        character.animator.CrossFade(targetAnimation, 0.2f);
        character.isPerformingAction = isPerformingAction;
        character.canRotate = canRotate;
        character.canMove = canMove;

        // 서버/호스트에게 우리가 애니메이션을 플레이하고 있다고 말하고
        // 세션에 있는 모든 사람이 애니메이션 재생.
        character.characterNetworkManager.NotifyTheServerOfAttackActionAnimationServerRpc(
            NetworkManager.Singleton.LocalClientId,
            targetAnimation,
            applyRootMotion);
    }

    public virtual void EnableCanDoCombo()
    {

    }

    public virtual void DisableCanDoCombo()
    {

    }
}
