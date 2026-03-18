using UnityEngine;
using TDA.Items;
using Unity.Netcode;
using TDA.Character;

namespace TDA.Character.AI
{
    /// <summary>
    /// [L3 Domain Layer] AI(몬스터/NPC) 전용 장비 매니저입니다.
    /// 플레이어의 복잡한 인벤토리 UI 연동을 배제하고, 즉각적으로 무기와 방패 모델을 
    /// 뼈대에 로드/언로드하는 기능에 집중합니다.
    /// </summary>
    public class AICharacterEquipmentManager : CharacterEquipmentManager
    {
        private CharacterManager aiCharacter;

        [Header("Weapon Model Slots")]
        public WeaponModelInstantiationSlot rightHandSlot;
        public WeaponModelInstantiationSlot leftHandSlot;

        [Header("Weapon Managers")]
        [SerializeField] private WeaponManager rightWeaponManager;
        [SerializeField] private WeaponManager leftWeaponManager;

        [Header("Runtime Models")]
        public GameObject rightHandWeaponModel;
        public GameObject leftHandWeaponModel;

        [Header("AI Default Weapons")]
        [Tooltip("몬스터가 스폰될 때 기본적으로 들고 있을 무기 ID (네트워크 동기화 전 로컬 테스트용)")]
        public int defaultRightWeaponID = -1;
        public int defaultLeftWeaponID = -1;

        protected override void Awake()
        {
            base.Awake();
            aiCharacter = GetComponent<CharacterManager>();
            InitializeWeaponSlots();
        }

        protected virtual void Start()
        {
            // 게임 시작 시, 로컬에서 인스펙터에 지정된 무기를 즉시 로드합니다.
            LoadWeaponOnBothHands();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // 향후 멀티플레이 완벽 동기화를 위해 무기 NetworkVariable이 
            // CharacterNetworkManager로 이동되면 이곳에 OnValueChanged 이벤트를 연결합니다.
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

        public void LoadWeaponOnBothHands()
        {
            // 네트워크 변수 대신, AI 로컬 테스트 변수값을 사용하여 로드합니다.
            LoadRightWeapon(defaultRightWeaponID);
            LoadLeftWeapon(defaultLeftWeaponID);
        }

        public void LoadRightWeapon(int itemID)
        {
            if (rightHandSlot == null) return;

            // 기존 무기 파기
            rightHandSlot.UnloadWeapon();
            defaultRightWeaponID = itemID; // 현재 상태 기억

            // 맨손(-1) 처리
            if (itemID == -1 || WorldItemDatabase.Instance == null)
            {
                UpdateDefendingItem();
                return;
            }

            // 새 무기 생성 및 장착
            WeaponItem weapon = WorldItemDatabase.Instance.GetWeaponByID(itemID);
            if (weapon != null && weapon.weaponModel != null)
            {
                rightHandWeaponModel = Instantiate(weapon.weaponModel);
                rightHandSlot.LoadWeapon(rightHandWeaponModel);

                rightWeaponManager = rightHandWeaponModel.GetComponent<WeaponManager>();
                if (rightWeaponManager != null)
                {
                    rightWeaponManager.SetWeaponDamage(aiCharacter, weapon);

                    // =========================================================================================
                    // 🚨 [Steam Audio 연동] 부모(CharacterEquipmentManager)의 공통 기능을 호출하여 연결
                    // =========================================================================================
                    RegisterWeaponAudioSource(rightWeaponManager, weapon.itemName);
                }
            }

            UpdateDefendingItem();
        }

        public void LoadLeftWeapon(int itemID)
        {
            if (leftHandSlot == null) return;

            leftHandSlot.UnloadWeapon();
            defaultLeftWeaponID = itemID;

            if (itemID == -1 || WorldItemDatabase.Instance == null)
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
                    leftWeaponManager.SetWeaponDamage(aiCharacter, weapon);

                    // =========================================================================================
                    // 🚨 [Steam Audio 연동] 부모(CharacterEquipmentManager)의 공통 기능을 호출하여 연결
                    // =========================================================================================
                    RegisterWeaponAudioSource(leftWeaponManager, weapon.itemName);
                }

                // [방어 시스템 P0-02 연동] 장착한 무기가 방패(Shield)일 경우 스펙 주입
                if (weapon is ShieldWeaponItemSO shieldSO)
                {
                    ShieldManager shieldManager = leftHandWeaponModel.GetComponentInChildren<ShieldManager>();
                    if (shieldManager != null)
                    {
                        shieldManager.SetShieldDefenseStats(aiCharacter, shieldSO);
                    }
                }
            }

            UpdateDefendingItem();
        }

        /// <summary>
        /// AI 캐릭터가 현재 장착한 무기/방패를 분석하여 AIDefenseManager에 방어 스펙을 자동 주입합니다.
        /// </summary>
        public void UpdateDefendingItem()
        {
            if (aiCharacter.characterDefenseManager == null) return;

            WeaponItem leftWeapon = WorldItemDatabase.Instance != null ? WorldItemDatabase.Instance.GetWeaponByID(defaultLeftWeaponID) : null;

            if (leftWeapon is ShieldWeaponItemSO shield)
            {
                aiCharacter.characterDefenseManager.SetDefendingItem(shield);
                return;
            }

            WeaponItem rightWeapon = WorldItemDatabase.Instance != null ? WorldItemDatabase.Instance.GetWeaponByID(defaultRightWeaponID) : null;

            if (rightWeapon != null && WorldItemDatabase.Instance != null && rightWeapon.itemID != WorldItemDatabase.Instance.unarmedWeapon.itemID)
            {
                aiCharacter.characterDefenseManager.SetDefendingItem(rightWeapon);
            }
            else
            {
                aiCharacter.characterDefenseManager.SetDefendingItem(null);
            }
        }
    }
}