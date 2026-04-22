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

                // =========================================================================================
                // [Fix-P0] 무기 교체/해제 직후 콤뱃 매니저의 damageColliders 리스트를 재스캔
                // 맨손 전환 시에도 기존 캐시가 스테일(destroy된 구 콜라이더 참조)일 수 있으므로
                // 반드시 Refresh 를 호출해 NullReferenceException / dangling reference 를 원천 차단합니다.
                // =========================================================================================
                RefreshCombatDamageColliders();
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

            // =========================================================================================
            // [Fix-P0 신규] 새 무기가 Instantiate 되어 자식 계층에 편입된 직후,
            // CharacterCombatManager.Awake() 에서 1회 캐싱된 damageColliders 리스트는 스테일 상태입니다.
            // 새 무기의 MeleeWeaponDamageCollider 를 반드시 재등록하세요.
            // =========================================================================================
            RefreshCombatDamageColliders();
        }

        public void LoadLeftWeapon(int itemID)
        {
            if (leftHandSlot == null) return;

            leftHandSlot.UnloadWeapon();
            defaultLeftWeaponID = itemID;

            if (itemID == -1 || WorldItemDatabase.Instance == null)
            {
                UpdateDefendingItem();

                // [Fix-P0] 맨손 전환 시에도 캐시 정합성 유지
                RefreshCombatDamageColliders();
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

            // =========================================================================================
            // [Fix-P0 신규] 왼손 무기 Instantiate 이후에도 콤뱃 매니저 캐시를 갱신.
            // 이원검/쌍검(dual wield) 셋업이 런타임에 생성되는 경우에도 양손 모두 타격 판정이 살아납니다.
            // =========================================================================================
            RefreshCombatDamageColliders();
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

        // =========================================================================================
        // [Fix-P0 신규] 콤뱃 매니저 damageColliders 캐시 갱신 헬퍼
        // ─────────────────────────────────────────────────────────────────────────────────────────
        // 무기 교체(Instantiate/Destroy)가 일어난 직후에는 CharacterCombatManager 가 Awake 에서
        // 1회 수집한 damageColliders 리스트가 구 콜라이더를 가리키는 dangling reference 가 됩니다.
        //
        // AICharacterManager 가 characterCombatManager 로 업캐스팅해 보유하고 있으므로,
        // 그 객체에 대해 RefreshDamageColliders() 를 호출해 리스트를 재수집합니다.
        //
        // NOTE:
        //   · aiCharacter 는 AICharacterManager 를 가리킬 수 있으므로 characterCombatManager 필드는
        //     부모 CharacterManager 가 보유한 업캐스팅 레퍼런스를 사용합니다.
        //   · 레퍼런스가 아직 초기화 전인 극초기 호출에서는 NRE 가 발생하지 않도록 ?. 체이닝을 사용합니다.
        // =========================================================================================
        private void RefreshCombatDamageColliders()
        {
            if (aiCharacter == null) return;
            aiCharacter.characterCombatManager?.RefreshDamageColliders();
        }
    }
}