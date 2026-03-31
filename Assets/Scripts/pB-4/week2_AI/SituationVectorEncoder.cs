// =============================================================================
// SituationVectorEncoder.cs  |  pB-4 Project — Week 2
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI
//
// 역할:
//   현재 게임 상황을 768차원 벡터로 인코딩하여 GameBlackboard의
//   activeSituationVector에 게시한다.
//   Week 7의 LightweightRAGEngine이 이 벡터를 사용하여
//   과거 사건과의 코사인 유사도를 검색한다.
//
//   인코딩 구성:
//     [0~4]   = 성격 5축 (control/stability/openness/agreeable/directness)
//     [5~9]   = 생득 욕구 4종 + 전역 긴장도
//     [10~41] = 지형 태그 원핫 인코딩 (32슬롯 예약)
//     [42~767] = 제로 패딩 (Week 7에서 Gemini 임베딩으로 교체 예정)
//
// 사용법:
//   HumanoidAIBrain과 같은 GameObject에 부착.
//   매 틱마다 EncodeAndPublish()가 호출되어 블랙보드를 갱신.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.AI.Humanoid;

namespace TDA.PB4.AI
{
    public class SituationVectorEncoder : MonoBehaviour
    {
        [Header("━━━ References ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("이 NPC의 HumanoidAIBrain. 같은 GameObject에 있으면 자동 탐지.")]
        [SerializeField] private HumanoidAIBrain brain;

        [Header("━━━ 지형 태그 → 벡터 매핑 ━━━━━━━━━━━")]
        [Tooltip("지형 태그 이름 목록. 이 순서대로 원핫 인코딩된다. " +
                 "예: 인덱스 0='NarrowPath', 1='SpookyCave', 2='DeathTrap' 등. " +
                 "비어있으면 기본 태그 목록이 자동 생성된다.")]
        [SerializeField] private List<string> terrainTagVocabulary = new List<string>();

        [Header("━━━ Debug ━━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("인코딩 결과를 Console에 출력할지 여부")]
        public bool debugLog = false;

        [Tooltip("마지막 인코딩된 벡터의 논제로(non-zero) 요소 수 (Read Only)")]
        [SerializeField] private int lastNonZeroCount;

        private const int VECTOR_DIM = 768;
        private float[] currentVector;

        private void Awake()
        {
            currentVector = new float[VECTOR_DIM];

            if (brain == null)
                brain = GetComponent<HumanoidAIBrain>();

            if (terrainTagVocabulary.Count == 0)
            {
                // 기본 지형 태그 어휘 (Week 4 FuzzyTagConverter에서 생성하는 태그)
                terrainTagVocabulary.AddRange(new string[] {
                    "NarrowPath", "SpookyCave", "DeathTrap", "HighGround",
                    "DifficultEscape", "WideOpen", "Flooded", "Dark",
                    "EnemyNearby", "ResourceRich", "Graveyard", "Crossroads",
                    "AmbushPoint", "SafeZone", "LavaProximity", "CrystalCave"
                });
            }
        }

        // ==================================================================
        // 핵심 API: 현재 상황을 768차원 벡터로 인코딩
        // ==================================================================

        /// <summary>
        /// 성격 5축 + 생득 욕구 + 지형 태그를 768차원 벡터로 인코딩하고
        /// GameBlackboard에 게시한다.
        /// HumanoidAIBrain.UpdateDecision()에서 매 틱 호출.
        /// </summary>
        public void EncodeAndPublish()
        {
            // 벡터 초기화
            System.Array.Clear(currentVector, 0, VECTOR_DIM);

            if (brain == null) return;

            var personality = brain.Personality;
            var bb = GameBlackboard.Instance;

            // [0~4] 성격 5축
            currentVector[0] = personality.control;
            currentVector[1] = personality.stability;
            currentVector[2] = personality.openness;
            currentVector[3] = personality.agreeable;
            currentVector[4] = personality.directness;

            // [5~8] 생득 욕구 4종
            currentVector[5] = brain.fear;
            currentVector[6] = brain.hunger;
            currentVector[7] = brain.greed;
            currentVector[8] = brain.fatigue;

            // [9] 전역 긴장도
            if (bb != null)
                currentVector[9] = bb.GlobalIntensityScore;

            // [10~41] 지형 태그 원핫 인코딩
            if (bb != null && bb.ActiveTerrainTags != null)
            {
                var tags = bb.ActiveTerrainTags;
                for (int i = 0; i < terrainTagVocabulary.Count && i < 32; i++)
                {
                    string vocab = terrainTagVocabulary[i];
                    for (int j = 0; j < tags.Count; j++)
                    {
                        if (tags[j] == vocab)
                        {
                            currentVector[10 + i] = 1.0f;
                            break;
                        }
                    }
                }
            }

            // 블랙보드 게시
            if (bb != null)
                bb.SetSituationVector(currentVector);

            // 디버그
            if (debugLog)
            {
                int nonZero = 0;
                for (int i = 0; i < VECTOR_DIM; i++)
                    if (currentVector[i] != 0f) nonZero++;
                lastNonZeroCount = nonZero;
                Debug.Log($"[SitVector] 인코딩 완료: nonZero={nonZero}/{VECTOR_DIM}, " +
                          $"personality=[{personality.control:F2},{personality.stability:F2},{personality.openness:F2},{personality.agreeable:F2},{personality.directness:F2}]");
            }
        }

        /// <summary>현재 인코딩된 벡터의 복사본을 반환.</summary>
        public float[] GetCurrentVector() => (float[])currentVector.Clone();
    }
}
