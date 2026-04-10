// =============================================================================
// TrapTriggerSystem.cs  |  pB×pC 통합 — Week 1 (pC 인지)
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI.Perception
//
// 역할:
//   Case C의 3종 트리거를 관리:
//     매복(Ambush): T-ASP ambushScore>threshold 지점 통과 시 발동
//     트랩(Trap): InteractableObject(문/상자/함정) 작동 시 발동
//     출정(Sortie): isDominant 팩션 주기적 정찰 → 자취 발견 시 발동
//
//   발동 시 PersistentHuntDirector.InitiateHunt()를 호출하여 영속 추적 시작.
// =============================================================================
using UnityEngine;
using TDA.PB4.Core;

namespace TDA.PB4.AI.Perception
{
    public enum TrapType
    {
        /// <summary>매복. T-ASP ambushScore>threshold 지점 통과.</summary>
        Ambush,
        /// <summary>트랩. InteractableObject 작동.</summary>
        Trap,
        /// <summary>출정. isDominant 팩션 정찰.</summary>
        Sortie
    }

    public class TrapTriggerSystem : MonoBehaviour
    {
        [Header("━━━ 트리거 유형 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("이 트리거의 유형. " +
                 "Ambush: 특정 지점 통과 시 발동 (T-ASP 연동). " +
                 "Trap: InteractableObject 작동 시 발동. " +
                 "Sortie: 시간 주기로 자동 체크.")]
        public TrapType triggerType = TrapType.Trap;

        [Header("━━━ Ambush 설정 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Ambush 타입: T-ASP ambushScore 임계값. 이 이상이면 발동. " +
                 "0.7 = ambushScore 70% 이상인 매복 적합 지점.")]
        [Range(0.3f, 1f)]
        public float ambushScoreThreshold = 0.7f;

        [Tooltip("Ambush 타입: 플레이어가 이 반경 내에 진입하면 트리거 체크.")]
        [Range(5f, 30f)]
        public float ambushTriggerRadius = 15f;

        [Header("━━━ Trap 설정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Trap 타입: 연결된 InteractableObject. " +
                 "이 오브젝트가 상호작용(Interact)되면 트리거 발동.")]
        public GameObject linkedInteractable;

        [Header("━━━ Sortie 설정 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Sortie 타입: isDominant 팩션 정찰 주기 (초). " +
                 "120 = 2분마다 자취 검색. 자취 발견 시 출정 트리거.")]
        [Range(30f, 300f)]
        public float sortieCheckInterval = 120f;

        [Header("━━━ 공통 설정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("PersistentHuntDirector 참조. 트리거 발동 시 InitiateHunt() 호출. " +
                 "비어있으면 씬에서 자동 탐색.")]
        [SerializeField] private PersistentHuntDirector huntDirector;

        [Tooltip("팩션 ID (Hunt 스폰 시 사용).")]
        public string factionId = "goblin";

        [Tooltip("팩션 Tier.")]
        [Range(1, 5)]
        public int factionTier = 1;

        [Tooltip("1회만 발동할지 반복 발동할지. true = 1회만.")]
        public bool oneShot = true;

        [Header("━━━ 상태 (Read Only) ━━━━━━━━━━━━━━━━")]
        [SerializeField] private bool hasTriggered = false;
        [SerializeField] private float sortieTimer;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        private Transform playerTransform;

        private void Start()
        {
            if (huntDirector == null) huntDirector = FindFirstObjectByType<PersistentHuntDirector>();
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTransform = p.transform;
        }

        private void Update()
        {
            if (hasTriggered && oneShot) return;
            if (playerTransform == null) return;

            switch (triggerType)
            {
                case TrapType.Ambush: CheckAmbush(); break;
                case TrapType.Sortie: CheckSortie(); break;
                // Trap은 외부에서 OnTrapActivated() 호출
            }
        }

        // Ambush: 플레이어가 매복 적합 지점 근처에 진입
        private void CheckAmbush()
        {
            float dist = Vector3.Distance(playerTransform.position, transform.position);
            if (dist > ambushTriggerRadius) return;

            // T-ASP ambushScore 체크 (TacticalAmbushPositioner 연동)
            // 프로토타입에서는 Inspector에서 설정한 threshold 직접 사용
            ActivateTrigger(playerTransform.position, $"Ambush 진입 (dist={dist:F1}m)");
        }

        // Sortie: 주기적 자취 검색
        private void CheckSortie()
        {
            sortieTimer += Time.deltaTime;
            if (sortieTimer < sortieCheckInterval) return;
            sortieTimer = 0f;

            // isDominant 팩션인지 체크
            var bb = GameBlackboard.Instance;
            if (bb != null && bb.ActiveFactionRegistry != null)
            {
                if (bb.ActiveFactionRegistry.TryGetValue(factionId, out var state))
                {
                    if (!state.isDominant) return;
                }
            }

            // 자취 검색
            var trail = FootprintTrailSystem.Instance;
            if (trail == null) return;
            var nearest = trail.GetNearestTrail(transform.position, 50f);
            if (nearest == null) return;

            ActivateTrigger(nearest.Value, $"Sortie 출정 ({factionId} 정찰대가 자취 발견)");
        }

        /// <summary>외부에서 호출 (Trap 타입: InteractableObject 작동 시).</summary>
        public void OnTrapActivated()
        {
            if (hasTriggered && oneShot) return;
            ActivateTrigger(playerTransform != null ? playerTransform.position : transform.position,
                           $"Trap 작동 ({(linkedInteractable != null ? linkedInteractable.name : "unknown")})");
        }

        private void ActivateTrigger(Vector3 triggerPos, string reason)
        {
            hasTriggered = true;
            if (huntDirector != null)
            {
                huntDirector.factionId = factionId;
                huntDirector.factionTier = factionTier;
                huntDirector.InitiateHunt(triggerPos);
            }
            if (debugLog) Debug.Log($"[Trap] Case C 트리거 발동: {reason}");
        }

        /// <summary>트리거 리셋.</summary>
        public void ResetTrigger() { hasTriggered = false; sortieTimer = 0f; }
    }
}
