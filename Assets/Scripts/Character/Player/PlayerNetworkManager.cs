using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;

public class PlayerNetworkManager : CharacterNetworkManager
{
    PlayerManager player;

    public NetworkVariable<FixedString64Bytes> characterName = new NetworkVariable<FixedString64Bytes>("Character", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    [Header("Equipment")]
    public NetworkVariable<int> currentWeaponBeingUsed = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> currentRightHandWeaponID = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> currentLeftHandWeaponID = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // [New] 추가된 방어구 및 가방 동기화 변수
    public NetworkVariable<int> currentHelmetID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> currentChestArmorID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> currentBackpackID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    [Header("Flags")]
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

        // 기존 무기 ID 변경 이벤트 연결
        currentRightHandWeaponID.OnValueChanged += OnCurrentRightHandWeaponIDChange;
        currentLeftHandWeaponID.OnValueChanged += OnCurrentLeftHandWeaponIDChange;
        currentWeaponBeingUsed.OnValueChanged += OnCurrentWeaponBeingUsedIDChange;

        // [New] 추가 장비 ID 변경 이벤트 연결
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
        // [수정] 스폰 시 데이터 동기화 지연으로 Vitality가 0이 들어와 캐릭터가 즉사하는 현상 방어
        if (newVitality <= 0) newVitality = 1;

        maxHealth.Value = player.playerStatsManager.CalculateHealthBasedOnVitalityLevel(newVitality);

        // [수정] 최대 체력이 0 이하가 되지 않도록 안전장치 추가
        if (maxHealth.Value <= 0) maxHealth.Value = 100;

        // [수정] 내 캐릭터(Owner)일 때만 화면의 UI를 업데이트하도록 제한
        // (타인이 방에 접속했을 때 내 화면의 체력바가 타인의 체력으로 덮어씌워지는 버그 방지)
        if (player.IsOwner && PlayerUIManager.Instance != null)
        {
            PlayerUIManager.Instance.playerUIHUDManager.SetMaxHealthValue(maxHealth.Value);
        }

        currentHealth.Value = maxHealth.Value;
    }

    public void SetNewMaxStaminaValue(int oldEndurance, int newEndurance)
    {
        // [수정] 스폰 시 스태미나가 0으로 계산되는 버그 방어
        if (newEndurance <= 0) newEndurance = 1;

        maxStamina.Value = player.playerStatsManager.CalculateHealthBasedOnVitalityLevel(newEndurance);

        if (maxStamina.Value <= 0) maxStamina.Value = 100;

        // [수정] 내 캐릭터일 때만 스태미나 UI 업데이트
        if (player.IsOwner && PlayerUIManager.Instance != null)
        {
            PlayerUIManager.Instance.playerUIHUDManager.SetMaxStaminaValue(maxStamina.Value);
        }

        currentStamina.Value = maxStamina.Value;
    }

    public void OnCurrentRightHandWeaponIDChange(int oldID, int newID)
    {
        WeaponItem newWeapon = Instantiate(WorldItemDatabase.Instance.GetWeaponByID(newID));
        player.playerInventoryManager.currentRightHandWeapon = newWeapon;

        // 장비 매니저를 통해 실제 모델 로드 (새로운 PlayerEquipmentManager는 itemID를 인자로 받을 수 있음)
        player.playerEquipmentManager.LoadRightWeapon(newID);

        // 로컬플레이어일때만 호출
        if (player.IsOwner)
        {
            PlayerUIManager.Instance.playerUIHUDManager.SetRightWeaponQuickSlotIcon(newID);
        }
    }

    public void OnCurrentLeftHandWeaponIDChange(int oldID, int newID)
    {
        WeaponItem newWeapon = Instantiate(WorldItemDatabase.Instance.GetWeaponByID(newID));
        player.playerInventoryManager.currentLeftHandWeapon = newWeapon;

        player.playerEquipmentManager.LoadLeftWeapon(newID);

        // 로컬플레이얼때만 호출
        if (player.IsOwner)
        {
            PlayerUIManager.Instance.playerUIHUDManager.SetLeftWeaponQuickSlotIcon(newID);
        }
    }

    public void OnCurrentWeaponBeingUsedIDChange(int oldID, int newID)
    {
        WeaponItem newWeapon = Instantiate(WorldItemDatabase.Instance.GetWeaponByID(newID));
        player.playerCombatManager.currentWeaponBeingUsed = newWeapon;
    }

    // [New] 추가 장비 변경 핸들러
    private void OnCurrentHelmetIDChange(int oldID, int newID)
    {
        // TODO: 투구 모델 교체 로직 (PlayerEquipmentManager 연동)
    }

    private void OnCurrentChestArmorIDChange(int oldID, int newID)
    {
        // TODO: 갑옷 모델 교체 로직
    }

    private void OnCurrentBackpackIDChange(int oldID, int newID)
    {
        // CharacterEquipmentManager(Base) 에 있는 가방 변경 이벤트를 실행하거나 직접 처리
        // 델리게이트가 있다면 여기서 실행하여 인벤토리 용량을 갱신합니다.
    }

    // 아이템 액션
    [ServerRpc]
    public void NotifyTheServerOfWeaponActionServerRpc(ulong clientID, int actionID, int weaponID)
    {
        if (IsServer)
        {
            NotifyTheServerOfWeaponActionClientRpc(clientID, actionID, weaponID);
        }
    }

    [ClientRpc]
    private void NotifyTheServerOfWeaponActionClientRpc(ulong clientID, int actionID, int weaponID)
    {
        // 로컬클라이언트가 다시 액션을 실행하지 않도록 조건문.
        if (clientID != NetworkManager.Singleton.LocalClientId)
        {
            PerformWeaponBasedAction(actionID, weaponID);
        }
    }

    private void PerformWeaponBasedAction(int actionID, int weaponID)
    {
        // weaponAction에 월드액션매니저에 actionID를 넣어 검색한 아이디를 반환, 복사.
        WeaponItemAction weaponAction = WorldActionManager.Instance.GetWeaponItemActioByID(actionID);

        if (weaponAction != null)
        {
            weaponAction.AttemptToPerformAction(player, WorldItemDatabase.Instance.GetWeaponByID(weaponID));
        }
        else
        {
            // 액션 값이 없음, 에러(이런 일이 잇으면 안됨)
            Debug.LogError("Action Is null, Cannot perform");
        }
    }

    // TD : 향후 플레이어 전용 IK 변수(예: 손가락 모양 제스처 등) 추가 가능
}