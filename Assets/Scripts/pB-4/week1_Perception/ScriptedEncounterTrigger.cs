// =============================================================================
// ScriptedEncounterTrigger.cs  |  pB×pC 통합 — Week 1 (pC 인지)
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI.Perception
//
// 역할:
//   Case A(강제 조우)를 관리한다.
//   정크션 Spawning 또는 아크 Climax 조건 시
//   AI를 플레이어 앞에 강제 스폰하고 즉시 전투를 시작한다.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;

namespace TDA.PB4.AI.Perception
{
    public enum EncounterCondition
    {
        /// <summary>JunctionLifecycleManager의 Spawning 단계 진입 시.</summary>
        JunctionSpawning,
        /// <summary>ArcProgressionManager의 Climax Phase 진입 시.</summary>
        ArcClimax,
        /// <summary>수동 트리거 (Inspector 버튼 또는 외부 호출).</summary>
        ManualTrigger
    }

    public enum SpawnFormation
    {
        /// <summary>플레이어 전방에 산개 배치.</summary>
        Frontal,
        /// <summary>플레이어 뒤/옆에서 기습.</summary>
        Ambush,
        /// <summary>플레이어를 360도 포위.</summary>
        Surround
    }

    public class ScriptedEncounterTrigger : MonoBehaviour
    {
        [Header("━━━ 트리거 조건 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("어떤 조건에서 강제 조우가 발동하는지. " +
                 "JunctionSpawning: 정크션 30m 이내 진입 시. " +
                 "ArcClimax: 시나리오 Climax Phase 진입 시. " +
                 "ManualTrigger: TriggerEncounter() 수동 호출.")]
        public EncounterCondition triggerCondition = EncounterCondition.JunctionSpawning;

        [Header("━━━ 스폰 설정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("스폰할 AI 프리팹 목록. 기존 AICharacterManager 프리팹 사용.")]
        public List<GameObject> spawnPrefabs = new List<GameObject>();

        [Tooltip("스폰 대형. Frontal=정면, Ambush=기습, Surround=포위.")]
        public SpawnFormation spawnFormation = SpawnFormation.Frontal;

        [Tooltip("스폰 거리 (미터). 플레이어로부터 이 거리에 AI 배치.")]
        [Range(5f, 30f)]
        public float spawnDistance = 10f;

        [Tooltip("스폰 즉시 currentTarget=플레이어 자동 설정. " +
                 "true = Case A (즉시 전투 진입).")]
        public bool forceTargetPlayer = true;

        [Tooltip("스폰된 AI의 팩션 ID (FactionDetectionSFXManager 연동용).")]
        public string factionId = "goblin";

        [Tooltip("스폰된 AI의 팩션 Tier (SFX 반복 횟수).")]
        [Range(1, 5)]
        public int factionTier = 1;

        [Header("━━━ 플레이어 참조 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("비어있으면 'Player' 태그로 자동 탐색.")]
        public Transform playerTransform;

        [Header("━━━ 상태 (Read Only) ━━━━━━━━━━━━━━━━")]
        [SerializeField] private bool hasTriggered = false;
        [SerializeField] private int spawnedCount = 0;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        private void Start()
        {
            if (playerTransform == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) playerTransform = p.transform;
            }
        }

        /// <summary>Case A: 강제 조우 발동.</summary>
        [ContextMenu("Trigger Encounter Now")]
        public void TriggerEncounter()
        {
            if (hasTriggered) return;
            if (playerTransform == null) { Debug.LogWarning("[Encounter] Player 없음"); return; }
            hasTriggered = true;

            for (int i = 0; i < spawnPrefabs.Count; i++)
            {
                if (spawnPrefabs[i] == null) continue;
                Vector3 spawnPos = CalculateSpawnPosition(i, spawnPrefabs.Count);
                var go = Instantiate(spawnPrefabs[i], spawnPos, Quaternion.identity);
                spawnedCount++;

                if (forceTargetPlayer)
                {
                    // pB-4 AI 타겟 설정
                    var mob = go.GetComponent<TDA.PB4.AI.Mob.MobAIBrain>();
                    if (mob != null) mob.currentTarget = playerTransform;
                    var hum = go.GetComponent<TDA.PB4.AI.Humanoid.HumanoidAIBrain>();
                    if (hum != null) hum.currentTarget = playerTransform;

                    // AIPerception 즉시 Combat
                    var perception = go.GetComponent<AIPerceptionSystem>();
                    if (perception != null)
                        perception.ForceSetAwareness(AwarenessState.Combat, "Case A 강제 조우");
                }

                if (debugLog) Debug.Log($"[Encounter] Case A 스폰: {go.name} at {spawnPos} (formation={spawnFormation})");
            }
        }

        private Vector3 CalculateSpawnPosition(int index, int total)
        {
            Vector3 playerPos = playerTransform.position;
            Vector3 playerFwd = playerTransform.forward;

            switch (spawnFormation)
            {
                case SpawnFormation.Frontal:
                    float spread = (index - total / 2f) * 3f;
                    return playerPos + playerFwd * spawnDistance + playerTransform.right * spread;
                case SpawnFormation.Ambush:
                    float angle = 90f + (index * 30f);
                    Vector3 dir = Quaternion.Euler(0, angle, 0) * playerFwd;
                    return playerPos + dir * spawnDistance;
                case SpawnFormation.Surround:
                    float circleAngle = (360f / total) * index;
                    Vector3 circleDir = Quaternion.Euler(0, circleAngle, 0) * Vector3.forward;
                    return playerPos + circleDir * spawnDistance;
                default:
                    return playerPos + playerFwd * spawnDistance;
            }
        }

        /// <summary>트리거 리셋 (재사용 가능).</summary>
        public void ResetTrigger() { hasTriggered = false; spawnedCount = 0; }
    }
}
