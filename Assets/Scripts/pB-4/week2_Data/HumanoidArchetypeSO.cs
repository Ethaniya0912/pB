// =============================================================================
// HumanoidArchetypeSO.cs  |  pB-4 Week 2 — Day 1 T1.2
// 역할: 하나의 완전한 NPC 빌드. Personality + Trust + Karma + CombatProfile + Prefab 통합.
//       예: 'merchant_greedy_01'은 Glutton 성격 + Neutral 진영 + 상인 Combat Profile + 특정 prefab.
// 사용처: T1.7에서 6종 프리셋 생성. HumanoidAIBrain.ApplyArchetype에서 적용.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(fileName = "NewHumanoidArchetype",
                     menuName = "pB-4/Humanoid/Archetype")]
    public class HumanoidArchetypeSO : ScriptableObject
    {
        [Header("━━━ 식별 ━━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("고유 ID. 예: 'merchant_greedy_01'.")]
        public string archetypeId;

        [Tooltip("UI에 표시될 이름. 예: '탐욕스러운 상인'.")]
        public string displayName;

        [Header("━━━ 성격 & 상태 ━━━━━━━━━━━━━━━━")]

        [Tooltip("기반 성격 프리셋. 예: Coward.asset.")]
        public PersonalityMatrixSO personality;

        [Tooltip("스폰 시 초기 진영.")]
        public NPCAlignment startingAlignment = NPCAlignment.Neutral;

        [Tooltip("스폰 시 초기 trust (0~100).")]
        [Range(0f, 100f)] public float startingTrust = 50f;

        [Tooltip("스폰 시 초기 karma (-100~+100).")]
        [Range(-100f, 100f)] public float startingKarma = 0f;

        [Tooltip("스폰 시 이미 가진 트라우마 목록. 보통 비어있음.")]
        public TraumaDataSO[] startingTraumas;

        [Header("━━━ 전투 ━━━━━━━━━━━━━━━━━━━━━━")]

        [Tooltip("전투 프로필. HumanoidCombatProfileSO 권장. Mob용 FactionCombatProfile도 가능.")]
        public FactionCombatProfileSO combatProfile;

        [Header("━━━ 프리팹 & 장비 ━━━━━━━━━━━━━━━")]

        [Tooltip("스폰할 prefab. 없으면 Spawner의 기본값 사용.")]
        public GameObject prefab;

        [Tooltip("스폰 시 장착할 아이템 ID 목록.")]
        public List<string> startingEquipmentIds = new List<string>();

        [Header("━━━ 스폰 컨텍스트 ━━━━━━━━━━━━━━━")]

        [Tooltip("이 아키타입이 스폰되는 상황 태그. 예: 'merchant_camp', 'bandit_ambush'.")]
        public string spawnContextTag;

        [Tooltip("Designer 메모.")]
        [TextArea(2, 4)] public string notes;

        /// <summary>아키타입이 Brain에 적용 가능한지 검증.</summary>
        public bool IsValid(out string reason)
        {
            if (string.IsNullOrEmpty(archetypeId))
            {
                reason = "archetypeId가 비어있음";
                return false;
            }
            if (personality == null)
            {
                reason = "personality 프리셋 미할당";
                return false;
            }
            if (startingTrust < 0f || startingTrust > 100f)
            {
                reason = $"startingTrust 범위 초과: {startingTrust}";
                return false;
            }
            reason = null;
            return true;
        }

        private void OnValidate()
        {
            startingTrust = Mathf.Clamp(startingTrust, 0f, 100f);
            startingKarma = Mathf.Clamp(startingKarma, -100f, 100f);
        }
    }
}
