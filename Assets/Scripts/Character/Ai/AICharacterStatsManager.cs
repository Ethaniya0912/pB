using UnityEngine;
using Unity.Netcode;
using TDA.Character;

namespace TDA.Character.AI
{
    /// <summary>
    /// [L3 Domain Layer] 몬스터 및 NPC 전용 스탯 매니저입니다.
    /// CharacterStatsManager를 상속받으며, 인스펙터에서 설정한 
    /// 기본 체력과 스태미나를 기반으로 네트워크 스폰 시 상태를 초기화합니다.
    /// </summary>
    public class AICharacterStatsManager : CharacterStatsManager
    {
        // [에러 해결] 부모 클래스의 character 변수가 private이므로, 
        // 자식 클래스 전용으로 참조할 변수를 따로 캐싱합니다.
        private CharacterManager aiCharacter;

        [Header("AI Base Stats")]
        [Tooltip("몬스터의 고유 최대 체력입니다. 스폰 시 이 값으로 초기화됩니다.")]
        [SerializeField] private int baseHealth = 100;

        [Tooltip("몬스터의 고유 최대 스태미나입니다. (스태미나를 사용하는 AI 전용)")]
        [SerializeField] private int baseStamina = 100;

        protected override void Awake()
        {
            base.Awake();

            // 자신의 오브젝트에 붙어있는 CharacterManager (또는 AICharacterManager)를 찾아서 할당합니다.
            aiCharacter = GetComponent<CharacterManager>();
        }

        private void Start()
        {
            // 씬 시작 시 스탯을 강제 초기화합니다.
            InitializeAIStats();
        }

        /// <summary>
        /// 인스펙터에 설정된 고유 스탯을 네트워크 변수에 덮어씌웁니다.
        /// </summary>
        public void InitializeAIStats()
        {
            // 부모의 character 대신 자체 캐싱한 aiCharacter를 사용합니다.
            if (aiCharacter == null || aiCharacter.characterNetworkManager == null) return;

            // NGO 환경에서는 서버(호스트)가 값을 세팅해야 모든 클라이언트에게 정상적으로 전파됩니다.
            if (NetworkManager.Singleton.IsServer)
            {
                aiCharacter.characterNetworkManager.maxHealth.Value = baseHealth;
                aiCharacter.characterNetworkManager.currentHealth.Value = baseHealth;

                aiCharacter.characterNetworkManager.maxStamina.Value = baseStamina;
                aiCharacter.characterNetworkManager.currentStamina.Value = baseStamina;

                Debug.Log($"<color=lime>[AI Stats]</color> {gameObject.name} 스탯 초기화 완료! (Max HP: {baseHealth}, Max SP: {baseStamina})");
            }
        }

        // =========================================================================================
        // 향후 몬스터 티어(일반, 엘리트, 보스)에 따른 스탯 보정이나 
        // 페이즈 전환 시 체력 회복 등의 특수 로직을 이 클래스에 추가하시면 됩니다.
        // =========================================================================================
    }
}