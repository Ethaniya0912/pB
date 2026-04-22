// =============================================================================
// HumanoidCombatProfileSO.cs  |  pB-4 Week 2 — Day 1 T1.2
// 역할: 기존 FactionCombatProfileSO (Mob용)를 상속하여 Humanoid 전용 필드 추가.
//       Loot 탐색 반경, Command 추종 거리, Speech 쿨다운 등.
// 같은 namespace(TDA.PB4.Data)이므로 using 문 불필요.
// =============================================================================
using UnityEngine;

namespace TDA.PB4.Data
{
    [CreateAssetMenu(fileName = "NewHumanoidCombatProfile",
                     menuName = "pB-4/Humanoid/Combat Profile")]
    public class HumanoidCombatProfileSO : FactionCombatProfileSO
    {
        [Header("━━━ Humanoid 전용 — Loot ━━━━━━━━━")]

        [Tooltip("보물 탐색 반경 (m). 이 거리 내 Lootable만 고려.")]
        [Range(1f, 30f)] public float lootSearchRadius = 8f;

        [Tooltip("이 fear 값 이상이면 Loot 중단하고 도주. 0~1.")]
        [Range(0f, 1f)] public float lootInterruptThreshold = 0.6f;

        [Header("━━━ Humanoid 전용 — Command ━━━━━━━")]

        [Tooltip("리더 추종 시 기본 거리 (m). Companion alignment.")]
        [Range(1f, 20f)] public float commandFollowDistance = 4f;

        [Tooltip("명령 수락 기본 임계. trust * 이 값 이상이면 수락.")]
        [Range(0f, 1f)] public float commandAcceptanceBase = 0.3f;

        [Header("━━━ Humanoid 전용 — Speech ━━━━━━━━")]

        [Tooltip("대사 발화 쿨다운 (초). 너무 자주 말하지 않도록.")]
        [Range(0f, 60f)] public float speechCooldown = 12f;

        [Header("━━━ 레이어 마스크 ━━━━━━━━━━━━━━━━━")]

        [Tooltip("Loot 가능한 오브젝트의 레이어. Physics.OverlapSphere에 사용.")]
        public LayerMask lootableLayer = ~0;

        [Tooltip("아군 감지 레이어. 동료 구조/호위 판정에 사용.")]
        public LayerMask allyLayer = ~0;
    }
}
