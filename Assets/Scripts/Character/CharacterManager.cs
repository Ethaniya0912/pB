using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CharacterManager : NetworkBehaviour
{
    [HideInInspector] public CharacterController characterController;
    [HideInInspector] public Animator animator;

    [HideInInspector] public CharacterNetworkManager characterNetworkManager;
    [HideInInspector] public CharacterEffectsManager characterEffectsManager;
    [HideInInspector] public CharacterAnimationManager characterAnimationManager;

    [Header("Flags")]
    public bool isPerformingAction = false;
    public bool applyRootMotion = false;
    public bool canRotate = true;
    public bool canMove = true;



    protected virtual void Awake()
    {
        DontDestroyOnLoad(this);

        characterController = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        characterNetworkManager = GetComponent<CharacterNetworkManager>();
        characterEffectsManager = GetComponent<CharacterEffectsManager>();
        characterAnimationManager = GetComponent<CharacterAnimationManager>();
    }

    protected virtual void Update()
    {
        // 캐릭터가 내쪽에서 움직일 경우, 네트워크포지션에 내 포지션을 할당
        if (IsOwner)
        {
            characterNetworkManager.networkPosition.Value = transform.position;
            characterNetworkManager.networkRotation.Value = transform.rotation;
        }

        // 캐릭터가 상대방에서 움직일 경우, 네트워크 포지션에서 로컬 포지션으로 할당.
        else
        {
            transform.position = Vector3.SmoothDamp
                (transform.position, 
                characterNetworkManager.networkPosition.Value, 
                ref characterNetworkManager.networkPositionVelocity, 
                characterNetworkManager.networkPositionSmoothTime);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                characterNetworkManager.networkRotation.Value,
                characterNetworkManager.networkRotationSmoothTime);
        }
    }

    protected virtual void LateUpdate()
    {

    }

    public virtual IEnumerator ProcessDeathEvent(bool manuallySelectDeathAnimation = false)
    {
         if (IsOwner)
         {
             characterNetworkManager.currentHealth.Value = 0;
             characterNetworkManager.isDead.Value = true;
         
             // 리셋해야할 플래그를 리셋해주기.
         
             // 땅에서 죽은게 아니라면, 다른 형식의 사망 애니메이션 재생.
         
             if (!manuallySelectDeathAnimation)
             {
                 characterAnimationManager.PlayTargetAnimation("Dead_01", true);
         
             }
         }

        // 사망 SFX 재생

        yield return new WaitForSeconds(5);

        // 플레이어에게 룬 제공 (ai 캐릭터사망시)

        // 캐릭터 비활성화disable
    }

    public virtual void ReviveCharacter()
    {

    }


}
