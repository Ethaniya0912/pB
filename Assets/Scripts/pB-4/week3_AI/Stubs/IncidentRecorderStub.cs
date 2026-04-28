// ─────────────────────────────────────────────────────────────────────
// pB-4 Week 3 D2 3차 — STEP 3-3: IncidentRecorderStub (v3)
// ─────────────────────────────────────────────────────────────────────
// 작성: 2026-04-28 (D2 3차 STEP 3, Stub 3/6, v3)
// 위치: Scripts/pB-4/week3_AI/Stubs/IncidentRecorderStub.cs
//
// 목적: 시나리오팀 IIncidentRecorder Real 도착 전 폴백.
//       Wk3 D2-D4 시점에는 사건 기록 시스템이 본격 작동 안 함.
//
// PB4Interfaces.cs 동결 시그니처 (TDA.PB4.Interfaces.Narrative):
//   IIncidentRecorder.RecordIncident(string incidentId, float intensityScore, string moralAlignment)
//   IIncidentRecorder.SetVectorIndexer(Action<string, float[]>)
//
// ★ Wk0 동결 시그니처 박제 박제: read 메서드 없음 (write 전용).
//   사건 검색은 Wk5+ 시그니처 통일 협의 ([04/W3] BLOCKING) 완료 후 추가.
// ─────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Interfaces.Narrative;  // IIncidentRecorder 박제 위치

namespace TDA.PB4.AI.Stubs
{
    /// <summary>
    /// 사건 기록 In-memory Stub.
    /// 기록 자체는 받지만 검색은 인터페이스에 없음 (Wk0 동결 시그니처).
    /// </summary>
    public class IncidentRecorderStub : IIncidentRecorder
    {
        // ── 디버깅용 in-memory 저장 (Wk5+ Real 도착 시 JSON/RAG 색인) ─
        private readonly List<RecordEntry> _records = new();
        private Action<string, float[]> _vectorIndexer;

        private struct RecordEntry
        {
            public string incidentId;
            public float intensityScore;
            public string moralAlignment;
            public float timestamp;
        }

        /// <summary>
        /// 사건 기록 (In-memory).
        /// Wk0 동결 시그니처: (incidentId, intensityScore, moralAlignment).
        /// </summary>
        public void RecordIncident(string incidentId, float intensityScore, string moralAlignment)
        {
            _records.Add(new RecordEntry
            {
                incidentId = incidentId,
                intensityScore = intensityScore,
                moralAlignment = moralAlignment,
                timestamp = Time.time
            });
            // verboseLogging 가드 (Console 폭주 방지) — 기본 로그 X
            // Debug.Log($"[IncidentStub] 기록: {incidentId}");
        }

        /// <summary>
        /// Vector 인덱싱 hook 박제 (Wk7+ RAG 활성화 시 사용).
        /// 현재는 hook만 박제, 실제 호출 X.
        /// </summary>
        public void SetVectorIndexer(Action<string, float[]> indexer)
        {
            _vectorIndexer = indexer;
        }

        // ── 디버깅 헬퍼 (인터페이스 외, 디버그 용도만) ──────────────
        public int RecordCount => _records.Count;

        public void ClearAllRecords()
        {
            _records.Clear();
        }
    }
}
