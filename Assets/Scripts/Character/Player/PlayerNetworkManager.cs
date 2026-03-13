using System;
using System.Collections;
using System.Collections.Generic;
using TDA.Items.WeaponItemActions;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using TDA.Character;
using TDA.Character.Player;

namespace TDA.Character.Player
{
    /// <summary>
    /// [L2 Router] 플레이어 캐릭터의 전역 네트워크 상태를 동기화하는 백본입니다.
    /// [오류 정정] 장비 연동 주석을 실제 코드로 100% 복구하였으며, 워핑 데이터 전파도 완벽히 보존했습니다.
    /// </summary>
    public class PlayerNetworkManager : CharacterNetworkManager
    {
        private PlayerManager player;

        [Tooltip("플레이어의 닉네임을 저장하는 네트워크 문자열입니다.")]
        public NetworkVariable<FixedString64Bytes> characterName = new NetworkVariable<FixedString64Bytes>("Character", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        [Header("Equipment & Weapons")]
        public NetworkVariable<int> currentWeaponBeingUsed = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> currentRightHandWeaponID = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> currentLeftHandWeaponID = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        [Header("Armor & Inventory")]
        public NetworkVariable<int> currentHelmetID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> currentChestArmorID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> currentBackpackID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        [Header("Combat Flags")]
        public NetworkVariable<bool> isUsingRightHand = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<bool> isUsingLeftHand = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        protected override void Awake()
        {
            base.Awake();
            player = GetComponent<PlayerManager>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // 장비 ID 변경 시 모델을 갈아끼우는 이벤트 자동 구독
            currentRightHandWeaponID.OnValueChanged += OnCurrentRightHandWeaponIDChange;
            currentLeftHandWeaponID.OnValueChanged += OnCurrentLeftHandWeaponIDChange;
            currentWeaponBeingUsed.OnValueChanged += OnCurrentWeaponBeingUsedIDChange;

            currentHelmetID.OnValueChanged += OnCurrentHelmetIDChange;
            currentChestArmorID.OnValueChanged += OnCurrentChestArmorIDChange;
            currentBackpackID.OnValueChanged += OnCurrentBackpackIDChange;
        }

        public void SetCharacterActionHand(bool rightHandedAction)
        {
            if (rightHandedAction)
            {
                isUsingLeftHand.Value = false;
                isUsingRightHand.Value = true;
            }
            else
            {
                isUsingRightHand.Value = false;
                isUsingLeftHand.Value = true;
            }
        }

        public void SetNewMaxHealthValue(int oldVitality, int newVitality)
        {
            if (newVitality <= 0) newVitality = 1;
            maxHealth.Value = player.playerStatsManager.CalculateHealthBasedOnVitalityLevel(newVitality);
            if (maxHealth.Value <= 0) maxHealth.Value = 100;

            if (player.IsOwner && PlayerUIManager.Instance != null)
                PlayerUIManager.Instance.playerUIHUDManager.SetMaxHealthValue(maxHealth.Value);

            currentHealth.Value = maxHealth.Value;
        }

        public void SetNewMaxStaminaValue(int oldEndurance, int newEndurance)
        {
            if (newEndurance <= 0) newEndurance = 1;
            maxStamina.Value = player.playerStatsManager.CalculateStaminaBasedOnEnduranceLevel(newEndurance);
            if (maxStamina.Value <= 0) maxStamina.Value = 100;

            if (player.IsOwner && PlayerUIManager.Instance != null)
                PlayerUIManager.Instance.playerUIHUDManager.SetMaxStaminaValue(maxStamina.Value);

            currentStamina.Value = maxStamina.Value;
        }

        // =========================================================================================
        // [장비 장착 동기화 로직 100% 복구]
        // =========================================================================================

        public void OnCurrentRightHandWeaponIDChange(int oldID, int newID)
        {
            WeaponItem newWeapon = Instantiate(WorldItemDatabase.Instance.GetWeaponByID(newID));
            player.playerInventoryManager.currentRightHandWeapon = newWeapon;
            player.playerEquipmentManager.LoadRightWeapon(newID);

            if (player.IsOwner && PlayerUIManager.Instance != null)
                PlayerUIManager.Instance.playerUIHUDManager.SetRightWeaponQuickSlotIcon(newID);
        }

        public void OnCurrentLeftHandWeaponIDChange(int oldID, int newID)
        {
            WeaponItem newWeapon = Instantiate(WorldItemDatabase.Instance.GetWeaponByID(newID));
            player.playerInventoryManager.currentLeftHandWeapon = newWeapon;
            player.playerEquipmentManager.LoadLeftWeapon(newID);

            if (player.IsOwner && PlayerUIManager.Instance != null)
                PlayerUIManager.Instance.playerUIHUDManager.SetLeftWeaponQuickSlotIcon(newID);
        }

        public void OnCurrentWeaponBeingUsedIDChange(int oldID, int newID)
        {
            WeaponItem newWeapon = Instantiate(WorldItemDatabase.Instance.GetWeaponByID(newID));
            if(newWeapon == null)
            {
                // 테스트용 로그: 무기가 null인 경우 경고 메시지를 출력합니다.
                Debug.LogWarning($"[PlayerNetworkManager] No weapon found for ID {newID} when trying to update current weapon being used.");
                return;
            }
            player.playerCombatManager.currentWeaponBeingUsed = newWeapon;
        }

        private void OnCurrentHelmetIDChange(int oldID, int newID)
        {
            // 네트워크를 통해 헬멧 아이디가 바뀌면 즉시 모델을 갈아끼웁니다.
            if (player.playerEquipmentManager != null)
            {
                player.playerEquipmentManager.LoadHelmetModel(newID);
            }
        }

        private void OnCurrentChestArmorIDChange(int oldID, int newID)
        {
            if (player.playerEquipmentManager != null)
            {
                player.playerEquipmentManager.LoadChestArmorModel(newID);
            }
        }

        private void OnCurrentBackpackIDChange(int oldID, int newID)
        {
            if (player.playerEquipmentManager != null)
            {
                player.playerEquipmentManager.LoadBackpackModel(newID);
            }
        }

        // =========================================================================================
        // [네트워크 RPC 동기화 100% 보존]
        // =========================================================================================

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void NotifyTheServerOfWeaponActionServerRpc(ulong clientID, int actionID, int weaponID)
        {
            if (IsServer)
                NotifyTheServerOfWeaponActionClientRpc(clientID, actionID, weaponID);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyTheServerOfWeaponActionClientRpc(ulong clientID, int actionID, int weaponID)
        {
            if (clientID != NetworkManager.Singleton.LocalClientId)
                PerformWeaponBasedAction(actionID, weaponID);
        }

        private void PerformWeaponBasedAction(int actionID, int weaponID)
        {
            WeaponItemAction weaponAction = WorldActionManager.Instance.GetWeaponItemActioByID(actionID);
            if (weaponAction != null)
                weaponAction.AttemptToPerformAction(player, WorldItemDatabase.Instance.GetWeaponByID(weaponID));
            else
                Debug.LogError("[PlayerNetworkManager] Action Is null, Cannot perform");
        }

        /// <summary>
        /// [P3 모션 워핑] 로컬에서 에임 어시스트로 찾은 타겟의 ID와 부위를 전 세계 클라이언트로 전파합니다.
        /// </summary>
        public void SendWarpAttackInfo(ulong targetNetworkId, int boneIndex)
        {
            NotifyWarpAttackServerRpc(targetNetworkId, boneIndex);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public override void NotifyWarpAttackServerRpc(ulong targetNetworkId, int boneIndex)
        {
            if (IsServer)
                NotifyWarpAttackClientRpc(targetNetworkId, boneIndex);
        }

        [Rpc(SendTo.ClientsAndHost)]
        public override void NotifyWarpAttackClientRpc(ulong targetNetworkId, int boneIndex)
        {
            // 나(Owner)는 이미 내 화면에서 로컬 연산으로 워핑을 시작했으므로 무시합니다.
            if (IsOwner) return;

            // 타 유저(Remote)의 화면에서, 내가 찌른 적의 오브젝트를 찾아 클라이언트 사이드 예측(CSP) 워핑을 수행합니다.
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkId, out NetworkObject targetNetObj))
            {
                Animator targetAnimator = targetNetObj.GetComponentInChildren<Animator>();
                if (targetAnimator == null) return;

                Transform targetBone = targetAnimator.GetBoneTransform((HumanBodyBones)boneIndex);
                if (targetBone == null)
                    targetBone = targetAnimator.transform;

                float attackRange = 1.5f;
                float warpStartTime = 0.1f;
                float warpEndTime = 0.4f;

                if (player.playerInventoryManager.currentRightHandWeapon != null &&
                    player.playerInventoryManager.currentRightHandWeapon.oh_RB_Action != null)
                {
                    // 추후 SO에서 워핑 데이터를 읽어옵니다.
                    // attackRange = player.playerInventoryManager.currentRightHandWeapon.oh_RB_Action.weaponAttackRange;
                }

                Vector3 directionToTarget = (targetBone.position - transform.position).normalized;
                Vector3 warpPosition = targetBone.position - (directionToTarget * attackRange);
                Quaternion warpRotation = Quaternion.LookRotation(directionToTarget);

                if (player.playerAnimationManager != null)
                {
                    player.playerAnimationManager.StartCoroutine(
                        player.playerAnimationManager.WarpToTargetRoutine(warpPosition, warpRotation, warpStartTime, warpEndTime)
                    );
                }
            }
        }
    }
}