using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.Character.Player;
using System; // PlayerManager 참조를 위해 추가

namespace TDA.Character.Player
{
    /// <summary>
    /// 캐릭터 장착 상태(ID)에 따라 실제 3D 모델을 생성하고 애니메이션 셋, 데미지 콜라이더 및 가방 비주얼을 관리합니다. (Dev B 최종 보완본)
    /// 가방 장착 시 드래그 상호작용을 위한 콜라이더를 자동으로 생성하며, 오브젝트 스케일에 따른 콜라이더 크기 정밀 보정 로직이 포함되어 있습니다.
    /// </summary>
    public class PlayerEquipmentManager : CharacterEquipmentManager
    {
        private PlayerManager player;

        [Header("Weapon Model Slots")]
        public WeaponModelInstantiationSlot rightHandSlot;
        public WeaponModelInstantiationSlot leftHandSlot;

        [Header("Weapon Managers")]
        [SerializeField] private WeaponManager rightWeaponManager;
        [SerializeField] private WeaponManager leftWeaponManager;

        [Header("Runtime Models")]
        public GameObject rightHandWeaponModel;
        public GameObject leftHandWeaponModel;

        [Header("Backpack System")]
        [SerializeField] private BagVisualController bagVisualController;
        [SerializeField] public Transform backpackSlot; // 캐릭터 등뼈(Spine) 하위의 앵커 슬롯

        protected override void Awake()
        {
            base.Awake();
            player = GetComponent<PlayerManager>();
            InitializeWeaponSlots();

            // [Debug] 슬롯 할당 확인 및 자동 탐색 로직
            if (backpackSlot == null)
            {
                Debug.LogWarning($"[Equipment] {gameObject.name}의 Backpack Slot이 비어있습니다. 자식에서 'Backpack_Anchor_Slot'을 검색합니다.");
                backpackSlot = FindChildByName(transform, "Backpack_Anchor_Slot");
            }

            // 컨트롤러 참조 자동 탐색 보강
            if (bagVisualController == null)
            {
                bagVisualController = GetComponentInChildren<BagVisualController>();
            }
        }

        protected virtual void Start()
        {
            // 초기 무기 로드 (네트워크 변수 초기값 기반)
            LoadWeaponOnBothHands();
        }

        private void InitializeWeaponSlots()
        {
            WeaponModelInstantiationSlot[] slots = GetComponentsInChildren<WeaponModelInstantiationSlot>();
            foreach (var slot in slots)
            {
                if (slot.weaponSlot == WeaponModelSlot.RightHand) rightHandSlot = slot;
                else if (slot.weaponSlot == WeaponModelSlot.LeftHand) leftHandSlot = slot;
            }
        }

        /// <summary>
        /// 네트워크 변수에 저장된 무기 ID를 기반으로 양손 모델을 로드합니다.
        /// </summary>
        public void LoadWeaponOnBothHands()
        {
            if (player.playerNetworkManager == null) return;
            LoadRightWeapon(player.playerNetworkManager.currentRightHandWeaponID.Value);
            LoadLeftWeapon(player.playerNetworkManager.currentLeftHandWeaponID.Value);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            Debug.Log($"<color=yellow>[Equipment] {gameObject.name} OnNetworkSpawn 실행됨.</color>");

            // 무기 ID 변경 감지 이벤트 연결
            currentRightHandWeaponID.OnValueChanged += (oldID, newID) => LoadRightWeapon(newID);
            currentLeftHandWeaponID.OnValueChanged += (oldID, newID) => LoadLeftWeapon(newID);

            // 가방 ID 변경 감지 (비주얼 업데이트 연동)
            currentBackpackID.OnValueChanged += (oldID, newID) => RefreshBackpackVisual(newID);

            // 초기 스폰 시점에 가방이 이미 있다면 리프레시 실행
            if (currentBackpackID.Value != -1)
            {
                RefreshBackpackVisual(currentBackpackID.Value);
            }
        }

        #region Backpack Visual Logic

        /// <summary>
        /// 가방 모델을 갱신하고, 드래그 상호작용을 위한 콜라이더를 자동으로 추가 및 보정합니다.
        /// </summary>
        /// <param name="backpackID">장착할 가방의 아이템 ID</param>
        private void RefreshBackpackVisual(int backpackID)
        {
            // 1. 슬롯 유효성 최종 확인 및 재탐색 (참조 유실 방지)
            if (backpackSlot == null) backpackSlot = FindChildByName(transform, "Backpack_Anchor_Slot");

            if (backpackSlot == null)
            {
                Debug.LogError($"[Equipment] {gameObject.name}: 가방을 생성할 backpackSlot(Backpack_Anchor_Slot)이 없습니다! 생성을 중단합니다.");
                return;
            }

            // 2. 기존 가방 모델 제거
            foreach (Transform child in backpackSlot)
            {
                if (child.name.StartsWith("Backpack_")) Destroy(child.gameObject);
            }

            if (backpackID == -1)
            {
                Debug.Log("[Equipment] 가방 장착 해제");
                if (bagVisualController != null) bagVisualController.gameObject.SetActive(false);
                return;
            }

            // 3. 데이터베이스에서 모델 로드 및 생성
            Item item = WorldItemDatabase.Instance.GetItemByID(backpackID);
            if (item is EquipmentItem backpackData && item.itemModel != null)
            {
                // NGO의 SpawnStateException 방지를 위해 부모 없이 생성 후 스케일 적용
                GameObject newBagModel = Instantiate(item.itemModel);
                newBagModel.name = $"Backpack_{item.itemName}";

                // SO에 설정된 스케일 데이터 적용 (예: 0.15)
                newBagModel.transform.localScale = backpackData.backpackModelScale;

                // [핵심 보완] 콜라이더 자동 생성 및 메쉬 정밀 맞춤 로직
                BoxCollider col = newBagModel.GetComponent<BoxCollider>();
                if (col == null)
                {
                    col = newBagModel.AddComponent<BoxCollider>();

                    // SkinnedMeshRenderer(리깅됨) 또는 일반 MeshRenderer 검색
                    Renderer modelRenderer = newBagModel.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (modelRenderer == null) modelRenderer = newBagModel.GetComponentInChildren<MeshRenderer>();

                    if (modelRenderer != null)
                    {
                        // 월드 영역의 실제 메쉬 크기(bounds)를 가져옴
                        Vector3 worldSize = modelRenderer.bounds.size;

                        // 콜라이더의 중심점을 모델의 로컬 좌표로 변환
                        col.center = newBagModel.transform.InverseTransformPoint(modelRenderer.bounds.center);

                        // [물리 보정] 월드 크기를 현재 모델의 LossyScale로 나누어 정확한 로컬 콜라이더 Size 산출
                        // 스케일이 작아도(0.15) 실제 메쉬 외형과 1:1로 일치하는 클릭 영역을 확보합니다.
                        Vector3 currentWorldScale = newBagModel.transform.lossyScale;
                        col.size = new Vector3(
                            worldSize.x / Mathf.Max(currentWorldScale.x, 0.001f),
                            worldSize.y / Mathf.Max(currentWorldScale.y, 0.001f),
                            worldSize.z / Mathf.Max(currentWorldScale.z, 0.001f)
                        );
                    }
                    else
                    {
                        // 메쉬 정보가 없는 비상 상황 시 기본 크기 설정 (스케일 역산)
                        col.size = new Vector3(1f / backpackData.backpackModelScale.x, 1f / backpackData.backpackModelScale.y, 1f / backpackData.backpackModelScale.z);
                    }
                    Debug.Log($"<color=green>[Equipment] '{newBagModel.name}' 콜라이더 자동 생성 및 보정 완료. Size: {col.size}</color>");
                }

                // 레이어 설정 (인벤토리 드래그 인식용)
                SetLayerRecursively(newBagModel, LayerMask.NameToLayer("BackBag"));

                // 컨트롤러 참조 재확인 및 활성화
                if (bagVisualController == null) bagVisualController = GetComponentInChildren<BagVisualController>();

                if (bagVisualController != null)
                {
                    bagVisualController.gameObject.SetActive(true);
                    // 컨트롤러에게 모델 제어권 위임 (NGO 간섭 제거 및 부모 설정 포함)
                    bagVisualController.InitializeBagModel(newBagModel, backpackSlot, backpackData);
                    Debug.Log($"<color=cyan>[Equipment] 가방 비주얼 생성 및 초기화 완료: {newBagModel.name}</color>");
                }
                else
                {
                    Debug.LogError($"[Equipment] {gameObject.name}: 가방 모델을 제어할 BagVisualController가 없습니다!");
                    newBagModel.transform.SetParent(backpackSlot, false);
                    newBagModel.transform.localPosition = Vector3.zero;
                    newBagModel.transform.localRotation = Quaternion.identity;
                }
            }
            else
            {
                Debug.LogError($"[Equipment] ID {backpackID}번에 해당하는 아이템 모델을 찾을 수 없거나 EquipmentItem 타입이 아닙니다.");
            }
        }

        private void SetLayerRecursively(GameObject obj, int newLayer)
        {
            if (obj == null) return;
            obj.layer = newLayer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, newLayer);
            }
        }

        private Transform FindChildByName(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                Transform result = FindChildByName(child, name);
                if (result != null) return result;
            }
            return null;
        }

        #endregion

        #region Weapon Loading Logic

        public void LoadRightWeapon(int itemID)
        {
            if (rightHandSlot == null) return;

            // 무기를 장착할 때 손에 잡고 있는 상호작용 물건이 있다면 자동으로 놓기
            if (itemID != WorldItemDatabase.Instance.unarmedWeapon.itemID && itemID != -1)
            {
                if (player.playerInteractionManager.currentlyHeldObject != null)
                {
                    Debug.Log("[Equipment] 무기 장착을 위해 손에 든 물건을 놓습니다.");
                    player.playerInteractionManager.ReleaseGrabbedObject();
                }
            }

            rightHandSlot.UnloadWeapon();

            if (itemID == -1) return;

            WeaponItem weapon = WorldItemDatabase.Instance.GetWeaponByID(itemID);
            if (weapon != null && weapon.weaponModel != null)
            {
                rightHandWeaponModel = Instantiate(weapon.weaponModel);
                rightHandSlot.LoadWeapon(rightHandWeaponModel);

                rightWeaponManager = rightHandWeaponModel.GetComponent<WeaponManager>();
                if (rightWeaponManager != null)
                {
                    rightWeaponManager.SetWeaponDamage(player, weapon);
                }
            }
        }

        public void LoadLeftWeapon(int itemID)
        {
            if (leftHandSlot == null) return;

            leftHandSlot.UnloadWeapon();

            if (itemID == -1) return;

            WeaponItem weapon = WorldItemDatabase.Instance.GetWeaponByID(itemID);
            if (weapon != null && weapon.weaponModel != null)
            {
                leftHandWeaponModel = Instantiate(weapon.weaponModel);
                leftHandSlot.LoadWeapon(leftHandWeaponModel);

                leftWeaponManager = leftHandWeaponModel.GetComponent<WeaponManager>();
                if (leftWeaponManager != null)
                {
                    leftWeaponManager.SetWeaponDamage(player, weapon);
                }
            }
        }
        #endregion

        #region Weapon Switching & Colliders

        public void SwitchRightWeapon()
        {
            if (!player.IsOwner) return;

            player.playerAnimationManager.PlayTargetAnimation(Animator.StringToHash("Swap_Weapon_01"), false, false, true, true);
            player.playerInventoryManager.rightHandWeaponIndex += 1;

            if (player.playerInventoryManager.rightHandWeaponIndex < 0 || player.playerInventoryManager.rightHandWeaponIndex > 2)
            {
                player.playerInventoryManager.rightHandWeaponIndex = 0;
                float weaponCount = 0;
                WeaponItem firstWeapon = null;
                int firstWeaponPosition = 0;

                for (int i = 0; i < player.playerInventoryManager.weaponInRightHandSlots.Length; i++)
                {
                    if (player.playerInventoryManager.weaponInRightHandSlots[i].itemID != WorldItemDatabase.Instance.unarmedWeapon.itemID)
                    {
                        weaponCount += 1;
                        if (firstWeapon == null)
                        {
                            firstWeapon = player.playerInventoryManager.weaponInRightHandSlots[i];
                            firstWeaponPosition = i;
                        }
                    }
                }

                if (weaponCount <= 1)
                {
                    player.playerInventoryManager.rightHandWeaponIndex = -1;
                    player.playerNetworkManager.currentRightHandWeaponID.Value = WorldItemDatabase.Instance.unarmedWeapon.itemID;
                }
                else
                {
                    player.playerInventoryManager.rightHandWeaponIndex = firstWeaponPosition;
                    player.playerNetworkManager.currentRightHandWeaponID.Value = firstWeapon.itemID;
                }
                return;
            }

            if (player.playerInventoryManager.weaponInRightHandSlots[player.playerInventoryManager.rightHandWeaponIndex].itemID != WorldItemDatabase.Instance.unarmedWeapon.itemID)
            {
                player.playerNetworkManager.currentRightHandWeaponID.Value =
                    player.playerInventoryManager.weaponInRightHandSlots[player.playerInventoryManager.rightHandWeaponIndex].itemID;
            }
            else
            {
                SwitchRightWeapon();
            }
        }

        public void SwitchLeftWeapon()
        {
            if (!player.IsOwner) return;

            player.playerAnimationManager.PlayTargetAnimation(Animator.StringToHash("Swap_Weapon_01"), false, false, true, true);
            player.playerInventoryManager.leftHandWeaponIndex += 1;

            if (player.playerInventoryManager.leftHandWeaponIndex < 0 || player.playerInventoryManager.leftHandWeaponIndex > 2)
            {
                player.playerInventoryManager.leftHandWeaponIndex = 0;
                float weaponCount = 0;
                WeaponItem firstWeapon = null;
                int firstWeaponPosition = 0;

                for (int i = 0; i < player.playerInventoryManager.weaponInLeftHandSlots.Length; i++)
                {
                    if (player.playerInventoryManager.weaponInLeftHandSlots[i].itemID != WorldItemDatabase.Instance.unarmedWeapon.itemID)
                    {
                        weaponCount += 1;
                        if (firstWeapon == null)
                        {
                            firstWeapon = player.playerInventoryManager.weaponInLeftHandSlots[i];
                            firstWeaponPosition = i;
                        }
                    }
                }

                if (weaponCount <= 1)
                {
                    player.playerInventoryManager.leftHandWeaponIndex = -1;
                    player.playerNetworkManager.currentLeftHandWeaponID.Value = WorldItemDatabase.Instance.unarmedWeapon.itemID;
                }
                else
                {
                    player.playerInventoryManager.leftHandWeaponIndex = firstWeaponPosition;
                    player.playerNetworkManager.currentLeftHandWeaponID.Value = firstWeapon.itemID;
                }
                return;
            }

            if (player.playerInventoryManager.weaponInLeftHandSlots[player.playerInventoryManager.leftHandWeaponIndex].itemID != WorldItemDatabase.Instance.unarmedWeapon.itemID)
            {
                player.playerNetworkManager.currentLeftHandWeaponID.Value =
                    player.playerInventoryManager.weaponInLeftHandSlots[player.playerInventoryManager.leftHandWeaponIndex].itemID;
            }
            else
            {
                SwitchLeftWeapon();
            }
        }

        public void OpenDamageCollider()
        {
            if (player.playerNetworkManager.isUsingRightHand.Value)
            {
                if (rightWeaponManager != null && rightWeaponManager.meleeWeaponDamageCollider != null)
                    rightWeaponManager.meleeWeaponDamageCollider.EnableDamageCollider();
            }
            else if (player.playerNetworkManager.isUsingLeftHand.Value)
            {
                if (leftWeaponManager != null && leftWeaponManager.meleeWeaponDamageCollider != null)
                    leftWeaponManager.meleeWeaponDamageCollider.EnableDamageCollider();
            }
        }

        public void CloseDamageCollider()
        {
            if (player.playerNetworkManager.isUsingRightHand.Value)
            {
                if (rightWeaponManager != null && rightWeaponManager.meleeWeaponDamageCollider != null)
                    rightWeaponManager.meleeWeaponDamageCollider.DisableDamageCollider();
            }
            else if (player.playerNetworkManager.isUsingLeftHand.Value)
            {
                if (leftWeaponManager != null && leftWeaponManager.meleeWeaponDamageCollider != null)
                    leftWeaponManager.meleeWeaponDamageCollider.DisableDamageCollider();
            }
        }

        //TODO: 헬멧과 갑옷 모델 로드 및 애니메이션 셋 관리 로직 구현 (Dev B 최종 보완본에서는 우선순위 낮음)
        internal void LoadHelmetModel(int newID)
        {
            throw new NotImplementedException();
        }

        internal void LoadChestArmorModel(int newID)
        {
            throw new NotImplementedException();
        }

        internal void LoadBackpackModel(int newID)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}