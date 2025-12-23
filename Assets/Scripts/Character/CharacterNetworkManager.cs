using System.Collections;
using System.Collections.Generic;
using SG;
using Unity.Netcode;
using UnityEngine;

public class CharacterNetworkManager : NetworkBehaviour
{
    CharacterManager character;
    CharacterIKController characterIKController;

    [Header("Status")]
    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    [Header("Position")]
    public NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<Quaternion> networkRotation = new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public Vector3 networkPositionVelocity;
    public float networkPositionSmoothTime = 0.1f;
    public float networkRotationSmoothTime = 0.1f;

    [Header("Animator")]
    public NetworkVariable<float> animatorHorizontalMovement = new NetworkVariable<float>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<float> animatorVerticalMovement = new NetworkVariable<float>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<float> animatorMoveAmountMovement = new NetworkVariable<float>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    [Header("Target")]
    public NetworkVariable<ulong> currentTargetNetworkObjectID = new NetworkVariable<ulong>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    [Header("Flags")]
    public NetworkVariable<bool> isLockedOn = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<bool> isSprinting = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<bool> isJumping = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<bool> isChargingAttack = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    [Header("Stats")]
    public NetworkVariable<int> endurance = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> vitality = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    [Header("Resources")]
    public NetworkVariable<float> currentStamina = new NetworkVariable<float>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> maxStamina = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> currentHealth = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> maxHealth = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    [Header("IK Sync")] // 잡고 있는 물체의 NetworkObjectId를 동기화 (Vector3 동기화보다 효율적)
    public NetworkVariable<ulong> currentRightHandGrabbedObjectID = new NetworkVariable<ulong>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    protected virtual void Awake()
    {
        character = GetComponent<CharacterManager>();
        characterIKController = GetComponent<CharacterIKController>();
    }

    public void CheckHP(int oldHealth, int newHealth)
    {
        if (currentHealth.Value <= 0)
        {
            StartCoroutine(character.ProcessDeathEvent());
        }

        // 오버힐링 방지
        if (character.IsOwner)
        {
            if (currentHealth.Value > maxHealth.Value)
            { 
                currentHealth.Value = maxHealth.Value;
            }
        }
    }

    public void OnLockOnTargetIDChange(ulong oldId, ulong newId)
    {
        if (!IsOwner)
        {
            character.characterCombatManager.currentTarget = NetworkManager.Singleton.SpawnManager.SpawnedObjects[newId].gameObject.GetComponent<CharacterManager>();
        }
    }

    public void OnIsLockedOnChange(bool old, bool isLockedOn)
    {
        if (!isLockedOn)
        {
            character.characterCombatManager.currentTarget = null;
        }
    }

    public void OnIsChargingAttackChanged(bool oldStatus, bool newStatus)
    {
        character.animator.SetBool("isChargingAttack", isChargingAttack.Value);
    }

    // RPC는 클라이언트로 부터 불러지는 함수이며, 서버를 부르는 함수임.
    [ServerRpc]
    public void NotifyTheServerOfActionAnimationServerRpc(ulong clientID, string animationID, bool applyRootMotion)
    {
        // 수신자가 호스트나 서버라면, 클라이언트 RPC를 활성화
        if (IsServer)
        {
            PlayActionAnimationFromAllClientsClientRpc(clientID,animationID, applyRootMotion);
        }
    }

    [ClientRpc]
    // 서버로만 불러와질 수 있으며, 존재하는 모든 클라이언트에 전송
    public void PlayActionAnimationFromAllClientsClientRpc(ulong clientID, string animationID, bool applyRootMotion)
    {
        // 해당 함수가 이를 보낸 캐릭터에게 실행되지 않도록 체크(두번실행방지)
        if (clientID != NetworkManager.Singleton.LocalClientId)
        {
            PerformActionAnimationFromServer(animationID, applyRootMotion);
        }
    }

    private void PerformActionAnimationFromServer(string animationID, bool applyRootMotion)
    {
        character.applyRootMotion = applyRootMotion;
        character.animator.CrossFade(animationID, 0.2f);
    }

    // 공격 애니메이션
    [ServerRpc]
    public void NotifyTheServerOfAttackActionAnimationServerRpc(ulong clientID, string animationID, bool applyRootMotion)
    {
        // 수신자가 호스트나 서버라면, 클라이언트 RPC를 활성화
        if (IsServer)
        {
            PlayAttackActionAnimationFromAllClientsClientRpc(clientID, animationID, applyRootMotion);
        }
    }

    [ClientRpc]
    // 서버로만 불러와질 수 있으며, 존재하는 모든 클라이언트에 전송
    public void PlayAttackActionAnimationFromAllClientsClientRpc(ulong clientID, string animationID, bool applyRootMotion)
    {
        // 해당 함수가 이를 보낸 캐릭터에게 실행되지 않도록 체크(두번실행방지)
        if (clientID != NetworkManager.Singleton.LocalClientId)
        {
            PerformAttackActionAnimationFromServer(animationID, applyRootMotion);
        }
    }

    private void PerformAttackActionAnimationFromServer(string animationID, bool applyRootMotion)
    {
        character.applyRootMotion = applyRootMotion;
        character.animator.CrossFade(animationID, 0.2f);
    }

    // 데미지
    [ServerRpc(RequireOwnership = false)]
    public void NotifyTheServerOfCharacterDamageServerRpc(
        ulong damageCharacterID,
        ulong characterCausingDamageID,
        float physicalDamage,
        float elementalDamage,
        float poiseDamage,
        float angleHitFrom,
        float contactPointX,
        float contactPointY,
        float contactPointZ
        )
    {
        if (IsServer)
        {
            NotifyTheServerOfCharacterDamageClientRpc(damageCharacterID, characterCausingDamageID, physicalDamage, elementalDamage, poiseDamage, angleHitFrom, contactPointX, contactPointY, contactPointZ);
        }
    }

    [ClientRpc]
    public void NotifyTheServerOfCharacterDamageClientRpc(
        ulong damageCharacterID,
        ulong characterCausingDamageID,
        float physicalDamage,
        float elementalDamage,
        float poiseDamage,
        float angleHitFrom,
        float contactPointX,
        float contactPointY,
        float contactPointZ)
    {
        ProcessCharacterDamageFromServer(damageCharacterID, characterCausingDamageID, physicalDamage, elementalDamage, poiseDamage, angleHitFrom, contactPointX, contactPointY, contactPointZ);
    }

    public void ProcessCharacterDamageFromServer(
        ulong damageCharacterID,
        ulong characterCausingDamageID,
        float physicalDamage,
        float elementalDamage,
        float poiseDamage,
        float angleHitFrom,
        float contactPointX,
        float contactPointY,
        float contactPointZ)
    {
        CharacterManager damagedCharacter = NetworkManager.Singleton.SpawnManager.SpawnedObjects[damageCharacterID].gameObject.GetComponent<CharacterManager>();
        CharacterManager characterCausingDamage = NetworkManager.Singleton.SpawnManager.SpawnedObjects[characterCausingDamageID].gameObject.GetComponent<CharacterManager>();

        TakeDamageEffect damageEffect = Instantiate(WorldCharacterEffectsManager.Instance.takeDamageEffect);

        damageEffect.physicalDamage = physicalDamage;
        damageEffect.elementDamage = elementalDamage;
        damageEffect.poiseDamage = poiseDamage;
        damageEffect.angleHitFrom = angleHitFrom;
        damageEffect.contactPoint = new Vector3(contactPointX, contactPointY, contactPointZ);
        damageEffect.characterCausingDamage = characterCausingDamage;

        damagedCharacter.characterEffectsManager.ProcessInstantEffects(damageEffect);
    }

    // IK 타겟 동기화 설정
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 값이 변경될 때마다(누군가 물건을 잡거나 놓을 때) 호출
        currentRightHandGrabbedObjectID.OnValueChanged += OnGrabbedObjectChanged;
    }

    public override void OnNetworkDespawn()
    {
        currentRightHandGrabbedObjectID.OnValueChanged -= OnGrabbedObjectChanged;
        base.OnNetworkDespawn();
    }

    // 값이 바뀌면 클라이언트에서 IK 타겟을 찾아 연결
    private void OnGrabbedObjectChanged(ulong oldID, ulong newID)
    {
        // 0이면 놓은 것
        if (newID == 0)
        {
            characterIKController.SetHandIKTarget(null);
            return;
        }

        // NetworkObjectId로 실제 게임 오브젝트 찾기
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(newID, out NetworkObject netObj))
        {
            GrabbableObject grabbable = netObj.GetComponent<GrabbableObject>();
            if (grabbable != null)
            {
                // 해당 물체의 GripPoint를 IK 타겟으로 설정
                characterIKController.SetHandIKTarget(grabbable.gripPoint);
            }
        }
    }

    // [ServerRpc] 클라이언트가 물건을 잡았다고 서버에 알림
    [ServerRpc]
    public void NotifyServerOfGrabActionServerRpc(ulong objectID)
    {
        if (!IsServer) return;
        currentRightHandGrabbedObjectID.Value = objectID;
    }

    // [ServerRpc] 놓았다고 알림
    [ServerRpc]
    public void NotifyServerOfReleaseActionServerRpc()
    {
        if (!IsServer) return;
        currentRightHandGrabbedObjectID.Value = 0;
    }
}
