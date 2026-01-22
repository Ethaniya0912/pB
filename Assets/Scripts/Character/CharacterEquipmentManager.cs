using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 캐릭터의 모든 장착 정보를 동기화하고 가방 슬롯을 관리합니다. (Dev A)
/// </summary>
public class CharacterEquipmentManager : NetworkBehaviour
{
    protected CharacterManager character;

    [Header("Equipment States")]
    public NetworkVariable<int> currentRightHandWeaponID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> currentLeftHandWeaponID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> currentHelmetID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> currentChestArmorID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> currentBackpackID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    protected virtual void Awake()
    {
        character = GetComponent<CharacterManager>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        currentBackpackID.OnValueChanged += OnBackpackIDChanged;
    }

    protected virtual void OnBackpackIDChanged(int oldID, int newID)
    {
        if (character.characterInventoryManager == null) return;

        if (newID == -1)
        {
            character.characterInventoryManager.UpdateGridSizeServerRpc(0, 0);
        }
        else
        {
            Item item = WorldItemDatabase.Instance.GetItemByID(newID);
            if (item is EquipmentItem backpack && backpack.isBackpack)
            {
                character.characterInventoryManager.UpdateGridSizeServerRpc(backpack.backpackWidth, backpack.backpackHeight);
            }
        }
    }

    public virtual void SaveEquipment(ref CharacterSaveData saveData)
    {
        saveData.currentRightHandWeaponID = currentRightHandWeaponID.Value;
        saveData.currentBackpackID = currentBackpackID.Value;
        saveData.currentHelmetID = currentHelmetID.Value;
        saveData.currentChestArmorID = currentChestArmorID.Value;
    }

    public virtual void LoadEquipment(CharacterSaveData saveData)
    {
        if (!IsOwner) return;
        currentRightHandWeaponID.Value = saveData.currentRightHandWeaponID;
        currentBackpackID.Value = saveData.currentBackpackID;
        currentHelmetID.Value = saveData.currentHelmetID;
        currentChestArmorID.Value = saveData.currentChestArmorID;
    }
}
