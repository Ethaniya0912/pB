// =============================================================================
// TacticalAmbushPositioner.cs (T-ASP)  |  pB-4 Project — Week 3
// Layer  : L3 Domain (지형)
// Namespace: TDA.PB4.Terrain
//
// 역할:
//   매복에 적합한 위치를 점수화하여 몹 스폰 지점을 결정하는 시스템.
//   플레이어의 예상 복귀 경로 전방 50m 내 노드를 샘플링하고,
//   지형 태그 가중치 + 레거시 보정 + 플레이어 취약도를 종합하여 점수를 산출.
//
//   매복 적합도 수식:
//     S(P) = (S_base × Π W_tags) + S_legacy + S_player_vulnerability
//
//   S_base = 폐쇄도(EI) × 0.5 + 높이차(ΔH) × 0.3 + (1-가시성) × 0.2
//   W_tags = 지형 태그별 가중치 (NarrowPath=1.5, HighGround=1.3 등)
//   S_legacy = 과거 사건이 발생한 장소의 보정치 (Week 7 연동)
//   S_player_vulnerability = 플레이어 체력/자원 기반 취약도 보정
//
// 연계:
//   - TerrainSampler: SampleOcclusion, SampleSlope 원시 데이터 제공
//   - GameBlackboard: activeTerrainTags로 태그 가중치 결정
//   - GameplaySculptBuffer: ambushScore>0.7 구간에서 SDF 시야차단 기둥 합성
//   - GroupAIManager: 매복 위치를 스폰 지점으로 전달
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;

namespace TDA.PB4.Terrain
{
    /// <summary>
    /// 개별 노드(위치)의 매복 적합도 평가 결과.
    /// </summary>
    [Serializable]
    public class AmbushCandidate
    {
        [Tooltip("평가 대상 월드 위치")]
        public Vector3 position;

        [Tooltip("최종 매복 적합도 점수 (0.0~무한대). 높을수록 매복에 적합.")]
        public float totalScore;

        [Tooltip("기본 점수 S_base = EI×0.5 + ΔH×0.3 + (1-Vis)×0.2")]
        public float baseScore;

        [Tooltip("태그 가중치 곱 Π(W_tags)")]
        public float tagMultiplier;

        [Tooltip("레거시 보정 (과거 사건 장소 가산)")]
        public float legacyBonus;

        [Tooltip("플레이어 취약도 보정")]
        public float vulnerabilityBonus;
    }

    public class TacticalAmbushPositioner : MonoBehaviour
    {
        [Header("━━━ 샘플링 설정 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("플레이어 전방 샘플링 거리 (미터). " +
                 "이 범위 내의 노드들이 매복 후보로 평가된다. " +
                 "50 = 플레이어 전방 50m 이내를 스캔.")]
        [Range(10f, 100f)]
        public float sampleRange = 50f;

        [Tooltip("샘플링할 노드(위치) 수. " +
                 "많을수록 정밀하지만 CPU 비용 증가. " +
                 "16 = 50m 범위에서 16개 지점을 무작위 샘플링.")]
        [Range(4, 64)]
        public int sampleCount = 16;

        [Tooltip("플레이어의 현재 Transform. 비어있으면 Camera.main 사용.")]
        public Transform playerTransform;

        [Header("━━━ 기본 점수 가중치 (S_base) ━━━━━━━━━")]
        [Tooltip("폐쇄도(Enclosure Index)의 가중치. " +
                 "높을수록 좁은 공간이 매복에 유리하게 평가됨. " +
                 "0.5 = 폐쇄도가 S_base의 50% 차지.")]
        [Range(0f, 1f)]
        public float W_enclosure = 0.5f;

        [Tooltip("높이차(ΔH)의 가중치. " +
                 "높을수록 고지대가 매복에 유리하게 평가됨. " +
                 "0.3 = 높이차가 S_base의 30% 차지.")]
        [Range(0f, 1f)]
        public float W_heightDiff = 0.3f;

        [Tooltip("비가시성(1-Visibility)의 가중치. " +
                 "높을수록 플레이어 시야 밖이 매복에 유리. " +
                 "0.2 = 비가시성이 S_base의 20% 차지.")]
        [Range(0f, 1f)]
        public float W_invisibility = 0.2f;

        [Header("━━━ 태그별 가중치 (W_tags) ━━━━━━━━━━━")]
        [Tooltip("지형 태그별 매복 적합도 가중치. " +
                 "태그가 활성화되어 있으면 해당 가중치가 S_base에 곱해짐. " +
                 "예: NarrowPath=1.5 → 좁은 통로에서 매복 점수 50% 상승.")]
        [SerializeField] private List<TagWeight> tagWeights = new List<TagWeight>();

        [Header("━━━ 플레이어 취약도 ━━━━━━━━━━━━━━━━━")]
        [Tooltip("플레이어 취약도 수동 오버라이드 (0.0=완전 무적, 1.0=매우 취약). " +
                 "실제 게임에서는 HP/스태미나에서 자동 계산. " +
                 "프로토타입에서는 슬라이더로 직접 조절.")]
        [Range(0f, 1f)]
        public float playerVulnerability = 0.3f;

        [Header("━━━ ambushScore 임계값 (GameplaySculpt 연동) ━━━")]
        [Tooltip("이 점수 이상인 위치에서 GameplaySculptBuffer의 ambushScore가 설정되어 " +
                 "SDF가 시야 차단 기둥을 자동 합성한다. " +
                 "0.7 = 적합도 0.7 이상 구간에서 기둥 생성.")]
        [Range(0f, 1f)]
        public float sculptThreshold = 0.7f;

        [Header("━━━ 결과 (Read Only) ━━━━━━━━━━━━━━━━")]
        [Tooltip("마지막 평가에서 가장 점수가 높은 매복 후보")]
        [SerializeField] private AmbushCandidate bestCandidate;

        [Tooltip("마지막 평가의 전체 후보 목록")]
        [SerializeField] private List<AmbushCandidate> allCandidates = new List<AmbushCandidate>();

        // TerrainSampler 참조 (자동 탐색)
        private TerrainSampler terrainSampler;

        private void Awake()
        {
            terrainSampler = FindFirstObjectByType<TerrainSampler>();

            if (tagWeights.Count == 0)
            {
                tagWeights.Add(new TagWeight { tag = "NarrowPath", weight = 1.5f });
                tagWeights.Add(new TagWeight { tag = "HighGround", weight = 1.3f });
                tagWeights.Add(new TagWeight { tag = "SpookyCave", weight = 1.2f });
                tagWeights.Add(new TagWeight { tag = "DeathTrap", weight = 2.0f });
                tagWeights.Add(new TagWeight { tag = "WideOpen", weight = 0.5f });
            }
        }

        // ==================================================================
        // 핵심 API: 매복 위치 평가
        // ==================================================================

        /// <summary>
        /// 플레이어 전방 sampleRange 이내에서 sampleCount개 위치를 평가하고
        /// 가장 매복에 적합한 위치를 반환한다.
        /// </summary>
        public AmbushCandidate EvaluateAmbushPositions()
        {
            allCandidates.Clear();
            bestCandidate = null;

            Vector3 playerPos = GetPlayerPosition();
            Vector3 playerForward = GetPlayerForward();

            for (int i = 0; i < sampleCount; i++)
            {
                // 플레이어 전방 + 좌우 분산 샘플링
                float dist = UnityEngine.Random.Range(10f, sampleRange);
                float angle = UnityEngine.Random.Range(-60f, 60f); // 전방 120도 부채꼴
                Vector3 dir = Quaternion.Euler(0, angle, 0) * playerForward;
                Vector3 samplePos = playerPos + dir * dist;

                AmbushCandidate candidate = EvaluateSinglePosition(samplePos, playerPos);
                allCandidates.Add(candidate);

                if (bestCandidate == null || candidate.totalScore > bestCandidate.totalScore)
                    bestCandidate = candidate;
            }

            return bestCandidate;
        }

        private AmbushCandidate EvaluateSinglePosition(Vector3 pos, Vector3 playerPos)
        {
            var candidate = new AmbushCandidate { position = pos };

            // S_base 구성요소
            float enclosure = terrainSampler != null ? terrainSampler.SampleOcclusion(pos) : 0.5f;
            float heightDiff = Mathf.Clamp01((pos.y - playerPos.y) / 10f); // 10m 정규화
            float visibility = IsVisibleFromPlayer(pos, playerPos) ? 1f : 0f;

            candidate.baseScore = (enclosure * W_enclosure)
                                + (heightDiff * W_heightDiff)
                                + ((1f - visibility) * W_invisibility);

            // 태그 가중치 곱
            candidate.tagMultiplier = 1.0f;
            var bb = GameBlackboard.Instance;
            if (bb != null && bb.ActiveTerrainTags != null)
            {
                foreach (var tw in tagWeights)
                {
                    for (int j = 0; j < bb.ActiveTerrainTags.Count; j++)
                    {
                        if (bb.ActiveTerrainTags[j] == tw.tag)
                        {
                            candidate.tagMultiplier *= tw.weight;
                            break;
                        }
                    }
                }
            }

            // 레거시 보정 (Week 7 IncidentRecorder 연동 예정)
            candidate.legacyBonus = 0f;

            // 플레이어 취약도
            candidate.vulnerabilityBonus = playerVulnerability * 0.3f;

            // 최종 점수
            candidate.totalScore = (candidate.baseScore * candidate.tagMultiplier)
                                 + candidate.legacyBonus
                                 + candidate.vulnerabilityBonus;

            return candidate;
        }

        private bool IsVisibleFromPlayer(Vector3 targetPos, Vector3 playerPos)
        {
            Vector3 dir = (targetPos - playerPos).normalized;
            float dist = Vector3.Distance(playerPos, targetPos);
            return !Physics.Raycast(playerPos + Vector3.up, dir, dist);
        }

        private Vector3 GetPlayerPosition()
        {
            if (playerTransform != null) return playerTransform.position;
            if (Camera.main != null) return Camera.main.transform.position;
            return Vector3.zero;
        }

        private Vector3 GetPlayerForward()
        {
            if (playerTransform != null) return playerTransform.forward;
            if (Camera.main != null) return Camera.main.transform.forward;
            return Vector3.forward;
        }

        /// <summary>최근 평가의 최적 매복 후보.</summary>
        public AmbushCandidate BestCandidate => bestCandidate;
        /// <summary>최근 평가의 전체 후보 목록.</summary>
        public IReadOnlyList<AmbushCandidate> AllCandidates => allCandidates;

        // Gizmo: 매복 후보 점수를 씬에 시각화
        private void OnDrawGizmosSelected()
        {
            foreach (var c in allCandidates)
            {
                Gizmos.color = Color.Lerp(Color.green, Color.red, Mathf.Clamp01(c.totalScore));
                Gizmos.DrawWireSphere(c.position, 0.5f);
            }
            if (bestCandidate != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(bestCandidate.position, 0.8f);
            }
        }
    }

    [Serializable]
    public class TagWeight
    {
        [Tooltip("지형 태그 이름")]
        public string tag;
        [Tooltip("매복 적합도 가중치. 1.0=영향없음, >1.0=유리, <1.0=불리")]
        [Range(0.1f, 3f)]
        public float weight = 1.0f;
    }
}
