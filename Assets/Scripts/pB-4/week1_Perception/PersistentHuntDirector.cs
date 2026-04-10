// =============================================================================
// PersistentHuntDirector.cs  |  pB×pC 통합 — Week 1 (pC 인지)
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI.Perception
//
// 역할:
//   Case C(매복/출정/트랩)의 영속 추적을 디렉팅한다.
//   트리거 발동 시:
//     1. 플레이어로부터 60~100m 떨어진 위치에 AI 그룹 스폰
//     2. AIPerceptionSystem.isPersistentHunt = true 설정 (포기 불가)
//     3. awarenessLevel = Suspicious 강제 설정
//     4. lastKnownPosition = 트리거 발동 지점
//     5. AI가 자취를 따라 접근 → 시야 확인 → Combat
//     6. FactionDetectionSFXManager와 거리 기반 Phase 1~4 동기화
// =============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.AI.Perception
{
    public class PersistentHuntDirector : MonoBehaviour
    {
        [Header("━━━ 스폰 설정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("스폰 거리 범위(m). (60, 100) = 60~100m 사이에서 랜덤 스폰. " +
                 "시야 밖에서 스폰되어야 하므로 최소 50m 이상 권장.")]
        public Vector2 spawnDistanceRange = new Vector2(60f, 100f);

        [Tooltip("추적 그룹 프리팹. 기존 AI 프리팹을 드래그. " +
                 "PB4DecisionAdapter + AIPerceptionSystem 컴포넌트가 부착되어 있어야 함.")]
        public GameObject huntGroupPrefab;

        [Tooltip("추적 그룹 크기. 3 = 3마리 스폰.")]
        [Range(1, 8)]
        public int huntGroupSize = 3;

        [Header("━━━ 추적 설정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("스폰 시 초기 인지 상태. Suspicious가 기본 (자취 추적 시작). " +
                 "Alert로 설정하면 더 빠르게 접근.")]
        public AwarenessState initialAwareness = AwarenessState.Suspicious;

        [Tooltip("나선형 수색 활성화. 자취를 잃은 후 lastKnownPosition 주변을 나선형으로 탐색.")]
        public bool spiralSearchEnabled = true;

        [Tooltip("최대 추적 지속 시간 (초). 0 = 무한 (절대 포기 안 함). " +
                 "300 = 5분 후 포기. 무한 추적은 긴장감 극대화에 적합.")]
        [Range(0f, 600f)]
        public float maxHuntDuration = 0f;

        [Header("━━━ 팩션 정보 (SFX 연동) ━━━━━━━━━━━━━")]
        [Tooltip("스폰할 팩션 ID. FactionDetectionSFXManager 연동용.")]
        public string factionId = "goblin";

        [Tooltip("팩션 Tier. SFX 워밴드 반복 횟수 결정.")]
        [Range(1, 5)]
        public int factionTier = 1;

        [Header("━━━ 상태 (Read Only) ━━━━━━━━━━━━━━━━")]
        [SerializeField] private bool isHuntActive = false;
        [SerializeField] private float huntStartTime;
        [SerializeField] private int spawnedAICount;
        [SerializeField] private float currentDistanceToPlayer;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;
        public bool showGizmos = true;

        private List<GameObject> spawnedAIs = new List<GameObject>();
        private Transform playerTransform;

        private void Start()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTransform = p.transform;
        }

        // ==================================================================
        // 핵심 API: 추적 시작
        // ==================================================================

        /// <summary>
        /// Case C 추적 시작. TrapTriggerSystem에서 호출.
        /// </summary>
        /// <param name="triggerPosition">트리거 발동 위치 (플레이어가 상자를 열었던 위치 등)</param>
        [ContextMenu("Initiate Hunt (Test)")]
        public void InitiateHunt()
        {
            if (playerTransform == null) return;
            InitiateHunt(playerTransform.position);
        }

        public void InitiateHunt(Vector3 triggerPosition)
        {
            if (isHuntActive) return;
            if (playerTransform == null) { Debug.LogWarning("[Hunt] Player 없음"); return; }
            if (huntGroupPrefab == null) { Debug.LogWarning("[Hunt] 프리팹 미설정"); return; }

            isHuntActive = true;
            huntStartTime = Time.time;
            spawnedAIs.Clear();

            // 원거리 스폰
            for (int i = 0; i < huntGroupSize; i++)
            {
                float dist = Random.Range(spawnDistanceRange.x, spawnDistanceRange.y);
                float angle = Random.Range(0f, 360f);
                Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                Vector3 spawnPos = triggerPosition + dir * dist;
                spawnPos.y = triggerPosition.y; // 동일 높이

                var go = Instantiate(huntGroupPrefab, spawnPos, Quaternion.identity);
                spawnedAIs.Add(go);
                spawnedAICount++;

                // AIPerceptionSystem 설정
                var perception = go.GetComponent<AIPerceptionSystem>();
                if (perception != null)
                {
                    perception.isPersistentHunt = true;
                    perception.SetLastKnownPosition(triggerPosition);
                    perception.ForceSetAwareness(initialAwareness, $"Case C 영속 추적 (dist={dist:F0}m)");
                }

                if (debugLog)
                    Debug.Log($"[Hunt] Case C 스폰: {go.name} at {spawnPos} ({dist:F0}m away, angle={angle:F0}°)");
            }
        }

        private void Update()
        {
            if (!isHuntActive) return;

            // 최대 추적 시간 체크
            if (maxHuntDuration > 0 && Time.time - huntStartTime > maxHuntDuration)
            {
                AbortHunt("최대 추적 시간 초과");
                return;
            }

            // 거리 추적 (디버그용)
            if (playerTransform != null && spawnedAIs.Count > 0 && spawnedAIs[0] != null)
                currentDistanceToPlayer = Vector3.Distance(spawnedAIs[0].transform.position, playerTransform.position);

            // 전투 진입 확인 → 추적 종료
            foreach (var ai in spawnedAIs)
            {
                if (ai == null) continue;
                var perception = ai.GetComponent<AIPerceptionSystem>();
                if (perception != null && perception.CurrentAwareness == AwarenessState.Combat)
                {
                    if (debugLog) Debug.Log($"[Hunt] Case C 추적 성공! {ai.name}이 Combat 진입. 총 {Time.time - huntStartTime:F1}초 소요");
                    isHuntActive = false;
                    return;
                }
            }
        }

        /// <summary>추적 중단.</summary>
        public void AbortHunt(string reason = "")
        {
            isHuntActive = false;
            foreach (var ai in spawnedAIs)
            {
                if (ai == null) continue;
                var perception = ai.GetComponent<AIPerceptionSystem>();
                if (perception != null) perception.isPersistentHunt = false;
            }
            if (debugLog) Debug.Log($"[Hunt] Case C 추적 중단: {reason}");
        }

        public bool IsHuntActive => isHuntActive;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos || !isHuntActive) return;
            Gizmos.color = Color.magenta;
            foreach (var ai in spawnedAIs)
            {
                if (ai == null) continue;
                Gizmos.DrawLine(ai.transform.position, ai.transform.position + Vector3.up * 3f);
                Gizmos.DrawWireSphere(ai.transform.position, 1f);
            }
        }
#endif
    }
}
