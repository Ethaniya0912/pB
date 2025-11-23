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

    protected virtual void Awake()
    {
        character = GetComponent<CharacterManager>();
    }

    public void UpdateAnimatorMovementParameters(float horizontalValue, float verticalValue) 
    {
        character.animator.SetFloat("Horizontal", horizontalValue, 0.1f, Time.deltaTime);
        character.animator.SetFloat("Vertical", verticalValue, 0.1f, Time.deltaTime);
    }

    public virtual void PlayTargetAnimation(
        string targetAnimation, 
        bool isPerformingAction, 
        bool applyRootMotion = true, 
        bool canRotate = false, 
        bool canMove = false)
    {
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
}
