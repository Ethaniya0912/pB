using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerEquipmentManager : CharacterEquipmentManager
{
    PlayerManager player;
    public WeaponModelInstantiationSlot rightHandSlot;
    public WeaponModelInstantiationSlot leftHandSlot;

    [SerializeField] WeaponManager rightWeaponManager;
    [SerializeField] WeaponManager leftWeaponManager;

    public GameObject rightHandWeaponModel;
    public GameObject leftHandWeaponModel;

    protected override void Awake()
    {
        base.Awake();

        player = GetComponent<PlayerManager>();

        // 슬롯 가져오기
        InitializeWeaponSlot();
    }

    protected override void Start()
    {
        base.Start();

        LoadWeaponOnBothHands();
    }

    private void InitializeWeaponSlot()
    {
        WeaponModelInstantiationSlot[] weaponSlots = GetComponentsInChildren<WeaponModelInstantiationSlot>();

        foreach (var weaponSlot in weaponSlots)
        {
            if (weaponSlot.weaponSlot == WeaponModelSlot.RightHand)
            {
                rightHandSlot = weaponSlot;
            }
            else if(weaponSlot.weaponSlot== WeaponModelSlot.LeftHand)
            {
                leftHandSlot = weaponSlot;
            }
        }
    }

    public void LoadWeaponOnBothHands()
    {
        LoadRightWeapon();
        LoadLeftWeapon();
    }

    public void SwitchRightWeapon()
    {
        if (!player.IsOwner)
            return;

        player.playerAnimationManager.PlayTargetAnimation("Swap_Weapon_01",false,false, true, true);

        // 엘든링 스타일 무기 스왑
        // 1. 메인 무기 이외 다른 무기가 있는지 체크, 존재시 언암가지말고 1,2 사이 스왑
        // 2. 메인 무기 외 없을 시, 언암으로 교체 후, 다른 빈 슬롯은 무시 후 메인무기로 스왑.

        WeaponItem selectedWeapon = null;

        // 양손에 무기를 들 경우 비활성화
        // 무기 인덱스 체크하기 (슬롯은 3개 있음으로, 3개의 인덱싱이 가능.)
        player.playerInventoryManager.rightHandWeaponIndex += 1;

        // 인덱스 아웃바운드 되었다면, 초기 포지션 1로 리셋(0)
        if (player.playerInventoryManager.rightHandWeaponIndex < 0 || player.playerInventoryManager.rightHandWeaponIndex > 2)
        {
            player.playerInventoryManager.rightHandWeaponIndex = 0;

            // 한개 이상의 무기를 들고 있는지 체크.
            float weaponCount = 0;
            WeaponItem firstWeapon = null;
            int firstWeaponPosition = 0;

            for (int i = 0; i < player.playerInventoryManager.weaponInRightHandSlots.Length; i++)
            {
                // 우측손슬롯의 아이템id가 언암이 아닐 경우
                if (player.playerInventoryManager.weaponInRightHandSlots[i].itemID != WorldItemDatabase.Instance.unarmedWeapon.itemID)
                {
                    // 웨폰카운트 +1
                    weaponCount += 1;

                    // 웨폰아이템의 변수 첫번째무기가 할당되어 있지 않다면
                    if (firstWeapon == null)
                    {   
                        // 첫번쨰 무기에 우측손무기슬롯에 있는 무기(번호)를 복사
                        // 첫번쨰무기포지션 인트 할당.
                        firstWeapon = player.playerInventoryManager.weaponInRightHandSlots[i];
                        firstWeaponPosition = i;
                    }
                }
            }

            if (weaponCount <= 1)
            {
                player.playerInventoryManager.rightHandWeaponIndex = -1;
                selectedWeapon = WorldItemDatabase.Instance.unarmedWeapon;
                player.playerNetworkManager.currentRightHandWeaponID.Value = selectedWeapon.itemID;
            }
            else
            {
                // 만약 웨폰카운트가 1보다 크다면, 언암으로 스위칭하지않고 서로 왓다갔다.
                player.playerInventoryManager.rightHandWeaponIndex = firstWeaponPosition;
                player.playerNetworkManager.currentRightHandWeaponID.Value = firstWeapon.itemID;
            }

            return;
        }

        // 언암이 있는지 체크
        foreach (WeaponItem weapon in player.playerInventoryManager.weaponInRightHandSlots)
        {
            // 언암드인지 아닌지 체크.
            // 무기오른손슬롯의 리스트에 있는 오른손무기인덱스의 아이디가 언암드웨폰의 아이디가 아닐 경우
            if (player.playerInventoryManager.weaponInRightHandSlots[player.playerInventoryManager.rightHandWeaponIndex].itemID !=
             WorldItemDatabase.Instance.unarmedWeapon.itemID)
            {
                // 선택한 무기 = 유저의 오른손슬롯에 유저의 오른손무기 인덱스.
                selectedWeapon = player.playerInventoryManager.weaponInRightHandSlots[player.playerInventoryManager.rightHandWeaponIndex];
                // 네트워크 무기 ID 를 할당하여 연결한 모든 클라이언트도 무기가 바뀌어 보이도록 설정.
                player.playerNetworkManager.currentRightHandWeaponID.Value = 
                player.playerInventoryManager.weaponInRightHandSlots[player.playerInventoryManager.rightHandWeaponIndex].itemID;
                return;
            }
        }

        if (selectedWeapon == null && player.playerInventoryManager.rightHandWeaponIndex <= 2)
        {
            // 자기 함수 부르기.
            SwitchRightWeapon();
        }
    }
    public void LoadRightWeapon()
    {
        if (player.playerInventoryManager.currentRightHandWeapon != null)
        {
            // 오래된 무기 제거
            rightHandSlot.UnloadWeapon();

            // 새 무기 가져오기
            rightHandWeaponModel = Instantiate(player.playerInventoryManager.currentRightHandWeapon.weaponModel);
            rightHandSlot.LoadWeapon(rightHandWeaponModel);
            rightWeaponManager = rightHandWeaponModel.GetComponent<WeaponManager>();
            rightWeaponManager.SetWeaponDamage(player, player.playerInventoryManager.currentRightHandWeapon);
            // 무기 데미지 적용, 콜라이더에.
        }
    }

    public void SwitchLeftWeapon()
    {
        
    }

    public void LoadLeftWeapon()
    {
        // 오래된 무기 제거
        leftHandSlot.UnloadWeapon();

        // 새무기 로딩
        leftHandWeaponModel = Instantiate(player.playerInventoryManager.currentLeftHandWeapon.weaponModel);
        leftHandSlot.LoadWeapon(leftHandWeaponModel);
        leftWeaponManager = leftHandWeaponModel.GetComponent<WeaponManager>();
        leftWeaponManager.SetWeaponDamage(player, player.playerInventoryManager.currentLeftHandWeapon);
        Debug.Log($"Player Equipment Manager, IsOwner : ,{ player.IsOwner}");
    }

    public void OpenDamageCollider()
    {
        // 우측 무기 데미지 콜라이더 오픈
        if (player.playerNetworkManager.isUsingRightHand.Value)
        {
            rightWeaponManager.meleeWeaponDamageCollider.EnableDamageCollider();
            Debug.Log("Collider opened");
        }

        // 좌측 무기 데미지 콜라이더 오픈
        else if (player.playerNetworkManager.isUsingLeftHand.Value)
        {
            leftWeaponManager.meleeWeaponDamageCollider.EnableDamageCollider();

        }

        // 베는 소리 sfx.
    }

    public void CloseDamageCollider()
    {
        // 우측 무기 데미지 콜라이더 닫기
        if (player.playerNetworkManager.isUsingRightHand.Value)
        {
            rightWeaponManager.meleeWeaponDamageCollider.DisableDamageCollider();
            Debug.Log("Collider closed");
        }

        // 좌측 무기 데미지 콜라이더 닫기
        else if (player.playerNetworkManager.isUsingLeftHand.Value)
        {
            leftWeaponManager.meleeWeaponDamageCollider.DisableDamageCollider();

        }

        // 베는 소리 sfx.
    }
}
