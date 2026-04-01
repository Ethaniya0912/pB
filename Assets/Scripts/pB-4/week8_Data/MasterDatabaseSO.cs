// =============================================================================
// MasterDatabaseSO.cs  |  pB-4 Project — Week 8
// Layer  : L3 Domain (시나리오)
// Namespace: TDA.PB4.Scenario
//
// 역할:
//   Approved SO 전체를 인덱싱하여 런타임에서 코사인 유사도 기반으로
//   최적의 아크를 검색한다. API 호출 0건으로 동작.
//
//   FindBestArc(): 현재 상황 벡터와 가장 유사한 아크를 100ms 이내에 반환.
//   이 클래스가 pC(본개발)에서 Gemini API를 완전히 대체하는 오프라인 런타임의 핵심.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using TDA.PB4.Data;
using TDA.PB4.Core;
using Debug = UnityEngine.Debug;

namespace TDA.PB4.Scenario
{
    /// <summary>아크 인덱스 항목. SO + 대표 벡터.</summary>
    [Serializable]
    public class ArcIndexEntry
    {
        [Tooltip("Approved ScenarioArcSO 참조")]
        public ScenarioArcSO arcSO;

        [Tooltip("이 아크의 대표 768차원 벡터. " +
                 "아크의 주제/분위기를 인코딩. " +
                 "비어있으면 arcSO의 phases에서 자동 생성.")]
        public float[] representativeVector;

        [Tooltip("이 아크의 적합도 태그. 'betrayal', 'survival' 등. " +
                 "태그 매칭 보너스에 사용.")]
        public List<string> thematicTags = new List<string>();
    }

    /// <summary>검색 결과.</summary>
    [Serializable]
    public class ArcSearchResult
    {
        public string arcName;
        public float similarity;
        public ScenarioArcSO arcSO;
    }

    public class MasterDatabaseSO : MonoBehaviour
    {
        [Header("━━━ 아크 인덱스 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Approved SO 인덱스 목록. Inspector에서 Approved 폴더의 SO를 드래그하여 등록. " +
                 "각 항목에 대표 벡터가 없으면 자동 생성됨.")]
        [SerializeField] private List<ArcIndexEntry> arcIndex = new List<ArcIndexEntry>();

        [Header("━━━ 검색 설정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("최대 반환 결과 수.")]
        [Range(1, 10)] public int maxResults = 3;

        [Tooltip("태그 매칭 보너스. 현재 블랙보드 태그와 아크 태그가 겹치면 유사도에 가산. " +
                 "0.1 = 태그 1개 겹칠 때마다 유사도 +0.1.")]
        [Range(0f, 0.3f)] public float tagMatchBonus = 0.1f;

        [Header("━━━ 마지막 검색 결과 (Read Only) ━━━━━━")]
        [SerializeField] private List<ArcSearchResult> lastResults = new List<ArcSearchResult>();
        [SerializeField] private float lastSearchTimeMs;

        [Header("━━━ 통계 (Read Only) ━━━━━━━━━━━━━━━━")]
        [Tooltip("인덱스에 등록된 아크 수")]
        [SerializeField] private int indexedArcCount;
        [Tooltip("API 호출 수 (항상 0이어야 함)")]
        [SerializeField] private int apiCallCount = 0;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        private void Awake()
        {
            indexedArcCount = arcIndex.Count;

            // 대표 벡터가 없는 항목에 자동 생성
            foreach (var entry in arcIndex)
            {
                if (entry.representativeVector == null || entry.representativeVector.Length != 768)
                    entry.representativeVector = GenerateRepresentativeVector(entry);
            }
        }

        // ==================================================================
        // 핵심 API: 최적 아크 검색
        // ==================================================================

        /// <summary>
        /// 현재 상황에 가장 적합한 아크를 코사인 유사도로 검색.
        /// 100ms 이내 응답 목표. API 호출 0건.
        /// </summary>
        public List<ArcSearchResult> FindBestArc(float[] queryVector)
        {
            lastResults.Clear();
            apiCallCount = 0; // 항상 0

            if (queryVector == null || arcIndex.Count == 0) return lastResults;

            var sw = Stopwatch.StartNew();
            var candidates = new List<(float sim, ArcIndexEntry entry)>();

            // 블랙보드 태그 (보너스용)
            var bb = GameBlackboard.Instance;
            List<string> currentTags = null;
            if (bb != null && bb.ActiveTerrainTags != null)
                currentTags = new List<string>(bb.ActiveTerrainTags);

            foreach (var entry in arcIndex)
            {
                if (entry.arcSO == null || entry.representativeVector == null) continue;

                float sim = LightweightRAGEngine.CosineSimilarity(queryVector, entry.representativeVector);

                // 태그 매칭 보너스
                if (currentTags != null)
                {
                    foreach (var tag in entry.thematicTags)
                        if (currentTags.Contains(tag)) sim += tagMatchBonus;
                }

                candidates.Add((sim, entry));
            }

            candidates.Sort((a, b) => b.sim.CompareTo(a.sim));

            for (int i = 0; i < Mathf.Min(maxResults, candidates.Count); i++)
            {
                var (sim, entry) = candidates[i];
                lastResults.Add(new ArcSearchResult
                {
                    arcName = entry.arcSO.arcName,
                    similarity = sim,
                    arcSO = entry.arcSO
                });
            }

            sw.Stop();
            lastSearchTimeMs = (float)sw.Elapsed.TotalMilliseconds;

            if (debugLog)
            {
                Debug.Log($"[MasterDB] FindBestArc: {lastResults.Count}건 / {arcIndex.Count}건 ({lastSearchTimeMs:F2}ms, API={apiCallCount})");
                foreach (var r in lastResults)
                    Debug.Log($"  {r.arcName}: sim={r.similarity:F3}");
            }

            return lastResults;
        }

        // ==================================================================
        // 대표 벡터 자동 생성 (간이)
        // ==================================================================
        private float[] GenerateRepresentativeVector(ArcIndexEntry entry)
        {
            var vec = new float[768];
            if (entry.arcSO == null) return vec;

            // Phase 수에 따른 기본 인코딩
            int phaseCount = entry.arcSO.phases != null ? entry.arcSO.phases.Count : 0;
            vec[0] = phaseCount / 4f; // 0~1

            // 태그에서 해시 기반 벡터 생성
            int idx = 10;
            foreach (var tag in entry.thematicTags)
            {
                int hash = tag.GetHashCode();
                for (int i = 0; i < 5 && idx < 768; i++, idx++)
                    vec[idx] = ((hash >> (i * 6)) & 0x3F) / 63f;
            }

            return vec;
        }

        public IReadOnlyList<ArcSearchResult> LastResults => lastResults;
        public float LastSearchTimeMs => lastSearchTimeMs;
        public int IndexedArcCount => indexedArcCount;
        public int ApiCallCount => apiCallCount;
    }
}
