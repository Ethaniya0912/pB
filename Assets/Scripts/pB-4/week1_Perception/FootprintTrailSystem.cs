// =============================================================================
// FootprintTrailSystem.cs  |  pB×pC 통합 — Week 1 (pC 인지)
// Layer  : L3 Domain (환경)
// Namespace: TDA.PB4.AI.Perception
//
// 역할:
//   플레이어 이동 경로에 비가시 마커(FootprintPoint)를 생성한다.
//   AIPerceptionSystem.TickTraceDetection()이 이 마커를 검색하여
//   AI가 플레이어의 자취를 따라갈 수 있게 한다.
//
//   마커는 일정 시간(trailLifetime) 후 소멸하며,
//   지형 태그에 따라 강도(strength)가 보정된다.
//   NarrowPath에서는 흔적이 선명하고, WideOpen에서는 희미하다.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;

namespace TDA.PB4.AI.Perception
{
    /// <summary>개별 발자국 마커.</summary>
    [System.Serializable]
    public class FootprintPoint
    {
        public Vector3 position;
        public float strength;
        public float createdTime;
        public float lifetime;
    }

    public class FootprintTrailSystem : MonoBehaviour
    {
        public static FootprintTrailSystem Instance { get; private set; }

        [Header("━━━ 마커 생성 설정 ━━━━━━━━━━━━━━━━━━")]
        [Tooltip("마커 생성 간격 (미터). 플레이어가 이 거리만큼 이동할 때마다 마커 1개 생성. " +
                 "2 = 2m마다. 낮을수록 정밀하지만 메모리 사용 증가.")]
        [Range(0.5f, 5f)]
        public float trailInterval = 2f;

        [Tooltip("마커 수명 (초). 이 시간이 지나면 마커가 소멸. " +
                 "60 = 1분. 길수록 AI가 오래된 자취도 추적 가능.")]
        [Range(10f, 300f)]
        public float trailLifetime = 60f;

        [Tooltip("기본 마커 강도 (0~1). 걷기=0.5, 달리기=1.0, 웅크리기=0.3. " +
                 "AIPerceptionSystem의 traceDetectionRange에 곱해져서 감지 가능 거리 결정.")]
        [Range(0f, 1f)]
        public float baseTrailStrength = 0.5f;

        [Header("━━━ 지형 보정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("NarrowPath 지형에서의 강도 배율. " +
                 "좁은 통로에서는 흔적이 선명 → AI가 더 멀리서 감지.")]
        [Range(1f, 3f)]
        public float narrowPathMultiplier = 1.5f;

        [Tooltip("WideOpen 지형에서의 강도 배율. " +
                 "넓은 공간에서는 흔적이 희미 → AI 감지 거리 감소.")]
        [Range(0.1f, 1f)]
        public float wideOpenMultiplier = 0.5f;

        [Header("━━━ 용량 제한 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("최대 마커 수. 이 수를 넘으면 가장 오래된 마커부터 삭제.")]
        [Range(20, 200)]
        public int maxTrailPoints = 100;

        [Header("━━━ 플레이어 참조 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("마커를 생성할 플레이어 Transform. 비어있으면 'Player' 태그로 자동 탐색.")]
        public Transform playerTransform;

        [Header("━━━ 디버그 (Read Only) ━━━━━━━━━━━━━━━")]
        [SerializeField] private int currentTrailCount;
        public bool debugLog = true;
        public bool showGizmos = true;

        private List<FootprintPoint> trails = new List<FootprintPoint>();
        private Vector3 lastDropPosition;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) playerTransform = player.transform;
                else if (debugLog) Debug.LogWarning("[FootprintTrail] 'Player' 태그 오브젝트를 찾을 수 없습니다.");
            }
            if (playerTransform != null) lastDropPosition = playerTransform.position;
        }

        private void Update()
        {
            if (playerTransform == null) return;

            // 이동 거리 확인 → 마커 생성
            float dist = Vector3.Distance(playerTransform.position, lastDropPosition);
            if (dist >= trailInterval)
            {
                DropFootprint(playerTransform.position, baseTrailStrength);
                lastDropPosition = playerTransform.position;
            }

            // 수명 만료 마커 제거
            DecayTrails();
            currentTrailCount = trails.Count;
        }

        // ==================================================================
        // 마커 생성
        // ==================================================================
        public void DropFootprint(Vector3 position, float strength)
        {
            // 지형 태그 기반 보정
            var bb = GameBlackboard.Instance;
            if (bb != null && bb.ActiveTerrainTags != null)
            {
                foreach (var tag in bb.ActiveTerrainTags)
                {
                    if (tag == "NarrowPath") strength *= narrowPathMultiplier;
                    if (tag == "WideOpen") strength *= wideOpenMultiplier;
                }
            }

            trails.Add(new FootprintPoint
            {
                position = position,
                strength = Mathf.Clamp01(strength),
                createdTime = Time.time,
                lifetime = trailLifetime
            });

            // 용량 제한
            while (trails.Count > maxTrailPoints) trails.RemoveAt(0);
        }

        // ==================================================================
        // AI에서 호출: 가장 가까운 마커 검색
        // ==================================================================
        public Vector3? GetNearestTrail(Vector3 aiPos, float searchRange)
        {
            float bestDist = float.MaxValue;
            FootprintPoint best = null;

            foreach (var fp in trails)
            {
                float effectiveRange = searchRange * fp.strength;
                float dist = Vector3.Distance(aiPos, fp.position);
                if (dist < effectiveRange && dist < bestDist)
                {
                    bestDist = dist;
                    best = fp;
                }
            }
            return best?.position;
        }

        /// <summary>마커 연결선의 방향 벡터. AI가 이 방향으로 이동하면 플레이어에 접근.</summary>
        public Vector3? GetTrailDirection(Vector3 aiPos, float searchRange)
        {
            Vector3? nearest = GetNearestTrail(aiPos, searchRange);
            if (nearest == null) return null;

            // 해당 마커의 다음 마커(더 최근) 방향
            int idx = trails.FindIndex(fp => fp.position == nearest.Value);
            if (idx < 0 || idx >= trails.Count - 1) return (nearest.Value - aiPos).normalized;
            return (trails[idx + 1].position - trails[idx].position).normalized;
        }

        // ==================================================================
        // 수명 만료 마커 제거
        // ==================================================================
        private void DecayTrails()
        {
            trails.RemoveAll(fp => Time.time - fp.createdTime > fp.lifetime);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos || trails == null) return;
            foreach (var fp in trails)
            {
                float age = Time.time - fp.createdTime;
                float alpha = Mathf.Clamp01(1f - age / fp.lifetime);
                Gizmos.color = new Color(0f, 1f, 0.5f, alpha * 0.6f);
                Gizmos.DrawSphere(fp.position, 0.15f * fp.strength);
            }
        }
#endif
    }
}
