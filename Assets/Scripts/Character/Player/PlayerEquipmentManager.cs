using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TDA.Character.Player;
using System; // PlayerManager 참조를 위해 추가
using TDA.Core.Events; // [신규 추가] ActionID 및 Funnel 아키텍처 연동을 위한 네임스페이스
using TDA.Items; // [방어 시스템 P0-02] ShieldWeaponItemSO 캐스팅을 위해 추가

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

            if (itemID == -1)
            {
                UpdateDefendingItem();
                return;
            }

            WeaponItem weapon = WorldItemDatabase.Instance.GetWeaponByID(itemID);
            if (weapon != null && weapon.weaponModel != null)
            {
                rightHandWeaponModel = Instantiate(weapon.weaponModel);
                rightHandSlot.LoadWeapon(rightHandWeaponModel);

                rightWeaponManager = rightHandWeaponModel.GetComponent<WeaponManager>();
                if (rightWeaponManager != null)
                {
                    rightWeaponManager.SetWeaponDamage(player, weapon);

                    // =========================================================================================
                    // 🚨 [Steam Audio 연동] 부모(CharacterEquipmentManager)의 공통 기능을 호출하여 연결
                    // =========================================================================================
                    RegisterWeaponAudioSource(rightWeaponManager, weapon.itemName);
                }
            }

            // 무기 장착이 끝난 후, 무엇으로 방어할지 스마트 갱신
            // (내부에서 DefenseManager의 SetDefendingItem을 자동으로 호출합니다)
            UpdateDefendingItem();
        }

        public void LoadLeftWeapon(int itemID)
        {
            if (leftHandSlot == null) return;

            leftHandSlot.UnloadWeapon();

            if (itemID == -1)
            {
                UpdateDefendingItem();
                return;
            }

            WeaponItem weapon = WorldItemDatabase.Instance.GetWeaponByID(itemID);
            if (weapon != null && weapon.weaponModel != null)
            {
                leftHandWeaponModel = Instantiate(weapon.weaponModel);
                leftHandSlot.LoadWeapon(leftHandWeaponModel);

                leftWeaponManager = leftHandWeaponModel.GetComponent<WeaponManager>();
                if (leftWeaponManager != null)
                {
                    leftWeaponManager.SetWeaponDamage(player, weapon);

                    // =========================================================================================
                    // 🚨 [Steam Audio 연동] 부모(CharacterEquipmentManager)의 공통 기능을 호출하여 연결
                    // =========================================================================================
                    RegisterWeaponAudioSource(leftWeaponManager, weapon.itemName);
                }

                // [방어 시스템 P0-02 연동] 장착한 무기가 방패(ShieldWeaponItemSO)인지 검사합니다.
                if (weapon is TDA.Items.ShieldWeaponItemSO shieldSO)
                {
                    // 방패 매니저(ShieldManager) 세팅
                    ShieldManager shieldManager = leftHandWeaponModel.GetComponentInChildren<ShieldManager>();
                    if (shieldManager != null)
                    {
                        shieldManager.SetShieldDefenseStats(character, shieldSO);
                    }

                    // 캐릭터의 방어 매니저에 방패 데이터(내구도, 처냄 저항 등)를 주입합니다.
                    if (character.characterDefenseManager != null)
                    {
                        character.characterDefenseManager.SetDefendingItem(shieldSO);
                        Debug.Log($"<color=cyan>[Equipment]</color> {shieldSO.itemName} 방패의 내구도 및 스탯이 DefenseManager에 주입되었습니다.");
                    }
                }
                else
                {
                    // 만약 방패가 아닌 일반 무기(쌍검 등)를 들었다면 방패 데이터를 초기화합니다.
                    if (character.characterDefenseManager != null)
                    {
                        character.characterDefenseManager.SetDefendingItem(null);
                    }
                }
            }

            // 무기 장착이 끝난 후, 무엇으로 방어할지 스마트 갱신
            // (내부에서 DefenseManager의 SetDefendingItem을 자동으로 호출합니다)
            UpdateDefendingItem();
        }
        #endregion

        #region [신규] 방어 장비 스마트 판단 로직

        /// <summary>
        /// 캐릭터가 장착한 무기 상태를 분석하여 무엇으로 방어할지(DefenseManager) 자동으로 결정합니다.
        /// 1순위: 왼손 방패, 2순위: 오른손 무기(검)
        /// </summary>
        public void UpdateDefendingItem()
        {
            if (player.playerDefenseManager == null || player.playerNetworkManager == null) return;

            // 1. 왼손 무기 확인
            int leftID = player.playerNetworkManager.currentLeftHandWeaponID.Value;
            WeaponItem leftWeapon = WorldItemDatabase.Instance.GetWeaponByID(leftID);

            if (leftWeapon is ShieldWeaponItemSO shield)
            {
                // 왼손에 든 게 '방패'라면 무조건 1순위 방패로 막음!
                player.playerDefenseManager.SetDefendingItem(shield);
                return;
            }

            // 2. 왼손에 방패가 없다면 오른손 무기 확인
            int rightID = player.playerNetworkManager.currentRightHandWeaponID.Value;
            WeaponItem rightWeapon = WorldItemDatabase.Instance.GetWeaponByID(rightID);

            if (rightWeapon != null && rightWeapon.itemID != WorldItemDatabase.Instance.unarmedWeapon.itemID)
            {
                // 오른손에 검/도끼를 들고 있다면 이것으로 막음!
                player.playerDefenseManager.SetDefendingItem(rightWeapon);
            }
            else
            {
                // 양손 모두 맨손이면 방어 셋업 안함 (맨손 막기 불가)
                player.playerDefenseManager.SetDefendingItem(null);
            }
        }

        #endregion

        #region Weapon Switching & Colliders

        public void SwitchRightWeapon()
        {
            if (!player.IsOwner) return;

            // ── 의존성 사전 검사 (NRE 박멸) ──
            if (!ValidateSwitchWeaponDependencies(isLeftHand: false)) return;

            var inv = player.playerInventoryManager;
            var netMgr = player.playerNetworkManager;
            var unarmedID = WorldItemDatabase.Instance.unarmedWeapon.itemID;

            // [신규 아키텍처: Funnel 패턴] 무기 교체 애니메이션 위임
            player.playerAnimationManager.PlayTargetActionFunnel((int)ActionID.Weapon_Swap, false, false, true, true);
            inv.rightHandWeaponIndex += 1;

            if (inv.rightHandWeaponIndex < 0 || inv.rightHandWeaponIndex > 2)
            {
                inv.rightHandWeaponIndex = 0;
                FindFirstNonUnarmedWeapon(
                    inv.weaponInRightHandSlots, unarmedID,
                    out int weaponCount, out WeaponItem firstWeapon, out int firstWeaponPosition);

                if (weaponCount <= 1)
                {
                    inv.rightHandWeaponIndex = -1;
                    netMgr.currentRightHandWeaponID.Value = unarmedID;
                }
                else
                {
                    inv.rightHandWeaponIndex = firstWeaponPosition;
                    netMgr.currentRightHandWeaponID.Value = firstWeapon.itemID;
                }
                return;
            }

            // ── 현재 인덱스 슬롯 처리 (null 안전) ──
            var slot = inv.weaponInRightHandSlots[inv.rightHandWeaponIndex];
            if (slot == null)
            {
                Debug.LogWarning(
                    $"[Equipment] {name}: weaponInRightHandSlots[{inv.rightHandWeaponIndex}] = null. Unarmed fallback.",
                    this);
                netMgr.currentRightHandWeaponID.Value = unarmedID;
                return;
            }

            if (slot.itemID != unarmedID)
            {
                netMgr.currentRightHandWeaponID.Value = slot.itemID;
            }
            else
            {
                TrySwitchWeaponRecursive(isLeftHand: false);
            }
        }

        public void SwitchLeftWeapon()
        {
            if (!player.IsOwner) return;

            // ── 의존성 사전 검사 (NRE 박멸) ──
            if (!ValidateSwitchWeaponDependencies(isLeftHand: true)) return;

            var inv = player.playerInventoryManager;
            var netMgr = player.playerNetworkManager;
            var unarmedID = WorldItemDatabase.Instance.unarmedWeapon.itemID;

            // [신규 아키텍처: Funnel 패턴] 무기 교체 애니메이션 위임
            player.playerAnimationManager.PlayTargetActionFunnel((int)ActionID.Weapon_Swap, false, false, true, true);
            inv.leftHandWeaponIndex += 1;

            if (inv.leftHandWeaponIndex < 0 || inv.leftHandWeaponIndex > 2)
            {
                inv.leftHandWeaponIndex = 0;
                FindFirstNonUnarmedWeapon(
                    inv.weaponInLeftHandSlots, unarmedID,
                    out int weaponCount, out WeaponItem firstWeapon, out int firstWeaponPosition);

                if (weaponCount <= 1)
                {
                    inv.leftHandWeaponIndex = -1;
                    netMgr.currentLeftHandWeaponID.Value = unarmedID;
                }
                else
                {
                    inv.leftHandWeaponIndex = firstWeaponPosition;
                    netMgr.currentLeftHandWeaponID.Value = firstWeapon.itemID;
                }
                return;
            }

            // ── 현재 인덱스 슬롯 처리 (null 안전) ──
            var slot = inv.weaponInLeftHandSlots[inv.leftHandWeaponIndex];
            if (slot == null)
            {
                Debug.LogWarning(
                    $"[Equipment] {name}: weaponInLeftHandSlots[{inv.leftHandWeaponIndex}] = null. Unarmed fallback.",
                    this);
                netMgr.currentLeftHandWeaponID.Value = unarmedID;
                return;
            }

            if (slot.itemID != unarmedID)
            {
                netMgr.currentLeftHandWeaponID.Value = slot.itemID;
            }
            else
            {
                TrySwitchWeaponRecursive(isLeftHand: true);
            }
        }

        // ════════════════════════════════════════════════════════════════
        // SwitchWeapon Helpers — NRE 안전화 + 재귀 카운터
        // ════════════════════════════════════════════════════════════════

        // 무한 재귀 방지 — 한 호출에서 최대 3 회 (slot 수만큼)
        [System.NonSerialized] private int _switchWeaponRecursionDepth = 0;
        private const int MaxSwitchWeaponRecursion = 3;

        /// <summary>SwitchWeapon 의존성 사전 검사 — null 가능 6 항목 모두 검증.</summary>
        private bool ValidateSwitchWeaponDependencies(bool isLeftHand)
        {
            if (player == null)
            {
                Debug.LogError($"[Equipment] {name}: player = null", this);
                return false;
            }

            if (player.playerInventoryManager == null)
            {
                Debug.LogError($"[Equipment] {name}: playerInventoryManager = null", this);
                return false;
            }

            if (player.playerNetworkManager == null)
            {
                Debug.LogError($"[Equipment] {name}: playerNetworkManager = null", this);
                return false;
            }

            if (player.playerAnimationManager == null)
            {
                Debug.LogError($"[Equipment] {name}: playerAnimationManager = null", this);
                return false;
            }

            if (WorldItemDatabase.Instance == null)
            {
                Debug.LogError($"[Equipment] {name}: WorldItemDatabase.Instance = null", this);
                return false;
            }

            if (WorldItemDatabase.Instance.unarmedWeapon == null)
            {
                Debug.LogError(
                    $"[Equipment] {name}: WorldItemDatabase.Instance.unarmedWeapon = null. " +
                    $"WorldItemDatabase Inspector 에서 Unarmed Weapon 필드 할당 필요.", this);
                return false;
            }

            var slots = isLeftHand
                ? player.playerInventoryManager.weaponInLeftHandSlots
                : player.playerInventoryManager.weaponInRightHandSlots;

            if (slots == null)
            {
                Debug.LogError(
                    $"[Equipment] {name}: weaponIn{(isLeftHand ? "Left" : "Right")}HandSlots 배열 = null. " +
                    $"PlayerInventoryManager 초기화 확인.", this);
                return false;
            }

            if (slots.Length == 0)
            {
                Debug.LogError(
                    $"[Equipment] {name}: weaponIn{(isLeftHand ? "Left" : "Right")}HandSlots 배열 길이 0.", this);
                return false;
            }

            return true;
        }

        /// <summary>슬롯 배열에서 첫 비-Unarmed 무기 검색 (null 안전).</summary>
        private void FindFirstNonUnarmedWeapon(
            WeaponItem[] slots, int unarmedID,
            out int weaponCount, out WeaponItem firstWeapon, out int firstWeaponPosition)
        {
            weaponCount = 0;
            firstWeapon = null;
            firstWeaponPosition = 0;

            if (slots == null) return;

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];

                // ★ null 검사 추가 — 사용자 코드의 for 루프 안에서도 NRE 위험이었음
                if (slot == null)
                {
                    Debug.LogWarning($"[Equipment] {name}: slots[{i}] = null. skip.", this);
                    continue;
                }

                if (slot.itemID != unarmedID)
                {
                    weaponCount += 1;
                    if (firstWeapon == null)
                    {
                        firstWeapon = slot;
                        firstWeaponPosition = i;
                    }
                }
            }
        }

        /// <summary>재귀 시도 — 카운터로 Stack Overflow 차단.</summary>
        private void TrySwitchWeaponRecursive(bool isLeftHand)
        {
            _switchWeaponRecursionDepth++;

            if (_switchWeaponRecursionDepth > MaxSwitchWeaponRecursion)
            {
                Debug.LogWarning(
                    $"[Equipment] {name}: SwitchWeapon 재귀 한도 {MaxSwitchWeaponRecursion} 회 초과. " +
                    $"모든 슬롯이 Unarmed 인 듯. Unarmed 유지.", this);
                _switchWeaponRecursionDepth = 0;

                var unarmedID = WorldItemDatabase.Instance.unarmedWeapon.itemID;
                if (isLeftHand)
                    player.playerNetworkManager.currentLeftHandWeaponID.Value = unarmedID;
                else
                    player.playerNetworkManager.currentRightHandWeaponID.Value = unarmedID;
                return;
            }

            try
            {
                if (isLeftHand) SwitchLeftWeapon();
                else SwitchRightWeapon();
            }
            finally
            {
                _switchWeaponRecursionDepth = 0;
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