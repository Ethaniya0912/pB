// =============================================================================
// LightweightRAGEngine.cs  |  pB-4 Project — Week 7
// Layer  : L3 Domain (시나리오)
// Namespace: TDA.PB4.Scenario
//
// 역할:
//   경량 RAG(Retrieval-Augmented Generation) 엔진.
//   현재 상황 벡터(768차원)와 과거 사건 기록의 코사인 유사도를 계산하여
//   유사한 과거 사건을 10ms 이내에 검색한다.
//
//   유사도 해석:
//     1.0 = 완전 일치 → "데자뷔" (NPC가 소름끼치는 회상)
//     0.7 = 서사적 공명 → "복선 회수" (과거 사건의 결과가 현재에 영향)
//     0.3 = 약한 연결 → "분위기 조성" (은은한 배경 대사)
//
// 연계:
//   - IncidentRecorder: 과거 사건 벡터 데이터 소스
//   - SituationVectorEncoder: 현재 상황 벡터 소스
//   - SpeechAssembler: 검색 결과에 따라 회상 대사 트리거
//   - JournalAutoWriter: 검색 결과를 수첩에 자동 기록
// =============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TDA.PB4.Scenario
{
    [Serializable]
    public class RAGSearchResult
    {
        public string incidentId;
        public float cosineSimilarity;
        public string summaryProse;
        public string resonanceType;
    }

    public class LightweightRAGEngine : MonoBehaviour
    {
        [Header("━━━ 데이터 소스 ━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("사건 기록 소스. 비어있으면 씬에서 자동 탐색.")]
        [SerializeField] private IncidentRecorder recorder;

        [Header("━━━ 유사도 임계값 ━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("데자뷔 판정 임계값. 이 이상이면 '완전 일치'. " +
                 "0.9 = 90% 이상 유사하면 데자뷔.")]
        [Range(0.7f, 1f)] public float dejaVuThreshold = 0.9f;

        [Tooltip("복선 회수 판정 임계값. " +
                 "0.6 = 60~90% 유사하면 서사적 공명.")]
        [Range(0.4f, 0.9f)] public float resonanceThreshold = 0.6f;

        [Tooltip("분위기 조성 판정 임계값. " +
                 "0.3 = 30~60% 유사하면 약한 연결.")]
        [Range(0.1f, 0.6f)] public float ambientThreshold = 0.3f;

        [Header("━━━ 검색 설정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("최대 반환 결과 수.")]
        [Range(1, 20)] public int maxResults = 5;

        [Header("━━━ 마지막 검색 결과 (Read Only) ━━━━━━")]
        [SerializeField] private List<RAGSearchResult> lastResults = new List<RAGSearchResult>();
        [SerializeField] private float lastSearchTimeMs;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        public bool debugLog = true;

        private void Awake()
        {
            if (recorder == null) recorder = FindFirstObjectByType<IncidentRecorder>();
        }

        // ==================================================================
        // 핵심 API: 유사 사건 검색
        // ==================================================================

        /// <summary>
        /// 현재 상황 벡터와 유사한 과거 사건을 검색.
        /// 10ms 이내 응답 목표.
        /// </summary>
        /// <param name="queryVector">현재 768차원 상황 벡터</param>
        public List<RAGSearchResult> Search(float[] queryVector)
        {
            lastResults.Clear();
            if (recorder == null || queryVector == null) return lastResults;

            var sw = Stopwatch.StartNew();
            var candidates = new List<(float sim, IncidentSnapshot snap)>();

            foreach (var record in recorder.Records)
            {
                if (record.situationVector == null) continue;
                float sim = CosineSimilarity(queryVector, record.situationVector);
                if (sim >= ambientThreshold)
                    candidates.Add((sim, record));
            }

            // 유사도 내림차순 정렬
            candidates.Sort((a, b) => b.sim.CompareTo(a.sim));

            // 최대 결과 수만큼 반환
            for (int i = 0; i < Mathf.Min(maxResults, candidates.Count); i++)
            {
                var (sim, snap) = candidates[i];
                string resonance = sim >= dejaVuThreshold ? "DejaVu" :
                                   sim >= resonanceThreshold ? "Resonance" : "Ambient";

                lastResults.Add(new RAGSearchResult
                {
                    incidentId = snap.incidentId,
                    cosineSimilarity = sim,
                    summaryProse = snap.summaryProse,
                    resonanceType = resonance
                });
            }

            sw.Stop();
            lastSearchTimeMs = (float)sw.Elapsed.TotalMilliseconds;

            if (debugLog)
            {
                Debug.Log($"[RAG] 검색 완료: {lastResults.Count}건 / {recorder.Records.Count}건 중 ({lastSearchTimeMs:F2}ms)");
                foreach (var r in lastResults)
                    Debug.Log($"  [{r.resonanceType}] {r.incidentId}: sim={r.cosineSimilarity:F3} '{r.summaryProse}'");
            }

            return lastResults;
        }

        // ==================================================================
        // 코사인 유사도
        // ==================================================================
        public static float CosineSimilarity(float[] a, float[] b)
        {
            if (a == null || b == null) return 0f;
            int len = Mathf.Min(a.Length, b.Length);
            float dot = 0f, magA = 0f, magB = 0f;
            for (int i = 0; i < len; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }
            float denom = Mathf.Sqrt(magA) * Mathf.Sqrt(magB);
            return denom > 0.0001f ? dot / denom : 0f;
        }

        public IReadOnlyList<RAGSearchResult> LastResults => lastResults;
        public float LastSearchTimeMs => lastSearchTimeMs;
    }
}
